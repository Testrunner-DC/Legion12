using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CardNameUsageLimitTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private const string FaithKey = "card-name:S02-0006";

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "card-name-limit", "NAMED", 91781,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        return game;
    }

    private static void Discard(L12GameEngine game, string id, int controller = 0,
        string origin = "library", bool byEffect = true)
    {
        var definition = Catalog.Cards["S02-0006"];
        var card = new L12CardInstance { InstanceId = id, CardId = definition.Id,
            Name = definition.NameZh, CardType = definition.CardType, EffectText = definition.Effect,
            Faction = definition.Faction, OwnerIndex = controller };
        var player = game.State.Players[controller];
        if (!player.Graveyard.Any(existing => existing.InstanceId == id)) player.Graveyard.Add(card);
        typeof(L12GameEngine).GetMethod("NotifyCardDiscarded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [player, card, origin, byEffect]);
    }

    private static void Choose(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void Pass(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
            Choose(game, game.State.PendingPrompts[0], "pass");
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeclineDoesNotUseNameAndLaterDiscardCanActivate(bool sameInstance, bool restore)
    {
        var game = Create();
        Discard(game, "first");
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains(Catalog.AtomicEffects.Find("S02-0006")!.Abilities.Single(ability => ability.Trigger == "discarded").Text,
            prompt.Text);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(FaithKey, game.State.Players[0].UsedAbilities);
        if (restore) game = Restore(game);
        Choose(game, prompt, "mode:none");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.Players[0].UsedAbilities,
            key => key.Contains("faith-zealot") || key == FaithKey);
        var nextId = sameInstance ? "first" : "second";
        Discard(game, nextId);
        Choose(game, Assert.Single(game.State.PendingPrompts), "mode:use");
        Assert.Contains(FaithKey, game.State.Players[0].UsedAbilities);
        Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == nextId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcceptedNameIsConsumedEvenWhenNegatedOrGeneratedChoiceSkipped(bool negated, bool restore)
    {
        var game = Create();
        Discard(game, "first");
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Choose(game, declaration, "mode:use");
        Assert.Contains(FaithKey, game.State.Players[0].UsedAbilities);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "s2-faith-zealot");
        Assert.Single(game.State.EffectStack).Negated = negated;
        if (restore) game = Restore(game);
        var stale = game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId, Choice: "mode:use"));
        Assert.False(stale.Accepted);
        Pass(game);
        if (!negated)
        {
            Assert.Empty(game.State.EffectStack);
            var generated = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("faith-zealot-post-resolution", generated.Continuation);
            Choose(game, generated, "skip");
        }
        Discard(game, "second");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(FaithKey, game.State.Players[0].UsedAbilities);
        Assert.DoesNotContain("active:master-0:drawCycle", game.State.Players[0].UsedAbilities);
    }

    [Fact]
    public void LegacyInstanceUsageAlsoBlocksAnotherCopyAfterRestore()
    {
        var game = Create();
        game.State.Players[0].UsedAbilities.Add("trigger:faith-zealot:old-instance");
        game = Restore(game);
        Discard(game, "new-instance");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void SharedNameIsNotSharedBetweenPlayers()
    {
        var game = Create();
        game.State.Players[0].UsedAbilities.Add(FaithKey);
        game.State.ActivePlayer = 1;
        Discard(game, "opponent", 1);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Choose(game, prompt, "mode:use");
        Assert.Contains(FaithKey, game.State.Players[1].UsedAbilities);
    }

    [Fact]
    public void EveryPrintedCardNameLimitHasAnExplicitSharedIdentity()
    {
        var named = Catalog.Cards.Values.Where(card =>
            System.Text.RegularExpressions.Regex.IsMatch(card.Effect ?? string.Empty,
                @"[<〈《].+?[>〉》](?:的效果)?每回合只可使用1次")).Select(card => card.Id).Order().ToArray();
        Assert.Equal(new[] { "S02-0006", "S02-0306" }, named);
        Assert.Equal(named, L12CardNameUsageRules.Keys.Keys.Order());
    }

    [Theory]
    [InlineData("hand", false, 0)]
    [InlineData("library", true, 1)]
    [InlineData("graveyard", true, 0)]
    public void IneligibleDiscardDoesNotCreateAnActivationOrConsumeUse(string origin, bool byEffect, int activePlayer)
    {
        var game = Create();
        game.State.ActivePlayer = activePlayer;
        Discard(game, "ineligible", origin: origin, byEffect: byEffect);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(FaithKey, game.State.Players[0].UsedAbilities);
    }

    [Fact]
    public void InvalidChoiceKeepsDeclarationAndDoesNotConsumeUse()
    {
        var game = Create();
        Discard(game, "invalid");
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "drawCycle"));
        Assert.False(result.Accepted);
        Assert.Equal(prompt.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
        Assert.DoesNotContain(FaithKey, game.State.Players[0].UsedAbilities);
        Choose(game, prompt, "mode:none");
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    public void NormalEndTurnClearsSharedAndLegacyUsage()
    {
        var game = Create();
        var player = game.State.Players[0];
        player.UsedAbilities.UnionWith([FaithKey, "trigger:faith-zealot:old", "s2-mimir-used"]);
        var result = game.Handle(0, new L12Command("endTurn"));
        Assert.True(result.Accepted, result.Error);
        Assert.False(L12CardNameUsageRules.HasUsed(player, "S02-0006"));
        Assert.False(L12CardNameUsageRules.HasUsed(player, "S02-0306"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MimirUseLocksAllCopiesIncludingAfterNegationAndRestore(bool negated)
    {
        var game = Create();
        var player = game.State.Players[0];
        var definition = Catalog.Cards["S02-0306"];
        var copies = new[] { "mimir-first", "mimir-second" }.Select(id => new L12CardInstance
        {
            InstanceId = id, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            Cost = definition.Cost ?? 0, EffectText = definition.Effect,
        }).ToArray();
        player.Hand.AddRange(copies);
        player.MasterDamageTakenThisTurn = 2;
        for (var i = 0; i < 10; i++) player.Morale.Add(new L12MoraleCard
            { InstanceId = $"mimir-morale-{i}", CardId = "S01-01C1" });
        var result = game.Handle(0, new L12Command("playCard", copies[0].InstanceId));
        Assert.True(result.Accepted, result.Error);
        Assert.Single(game.State.EffectStack).Negated = negated;
        game = Restore(game);
        Pass(game);
        var laterSegment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", laterSegment.Continuation);
        Choose(game, laterSegment, "mode:none");
        Pass(game);
        result = game.Handle(0, new L12Command("playCard", copies[1].InstanceId));
        Assert.False(result.Accepted);
        Assert.Contains("每回合只可使用1次", result.Error);
        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == copies[1].InstanceId);
        Assert.Contains("s2-mimir-used", game.State.Players[0].UsedAbilities);
    }
}
