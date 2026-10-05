using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BloodEagleDeclineTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> Decisions()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var extraGraves in new[] { -1, 0, 1, 2, 4 })
        foreach (var use in new[] { false, true })
        foreach (var restore in new[] { false, true })
            yield return [owner, extraGraves, use, restore];
    }

    [Theory]
    [MemberData(nameof(Decisions))]
    [Trait("L12Evidence", "card:S01-0320")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void MissingRecoveryTargetsNeverRemoveTheSetCountersActivationDecision(
        int owner, int extraGraves, bool use, bool restore)
    {
        var game = Create(owner, extraGraves);
        var originalTroops = game.State.Players[1 - owner].Field[0][0]!.CurrentTroops;
        DestroyAndOrderBloodLast(game, owner, "fallen");
        var decision = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", decision.Continuation);
        Assert.Equal("blood-eagle", decision.SourceInstanceId);
        Assert.Empty(game.State.EffectStack);
        Assert.True(game.State.Players[owner].Field[1][0]!.Hidden);
        if (restore) game = Restore(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(decision.PromptId, prompt.PromptId);
        L12Command command;
        if (extraGraves <= 0)
        {
            Assert.Equal("option", prompt.Kind);
            Assert.Contains("mode:none", prompt.ValidChoices);
            Assert.Contains("mode:use", prompt.ValidChoices);
            Assert.Equal("不发动", prompt.ChoiceLabels["mode:none"]);
            command = new("resolvePrompt", PromptId: prompt.PromptId,
                Choice: use ? "mode:use" : "mode:none");
        }
        else
        {
            Assert.Equal("order", prompt.Kind);
            Assert.Contains("skip", prompt.ValidChoices);
            command = use
                ? new("resolvePrompt", PromptId: prompt.PromptId,
                    CardInstanceIds: prompt.ValidChoices.Where(id => id != "skip").Take(2).ToList())
                : new("resolvePrompt", PromptId: prompt.PromptId, Choice: "skip");
        }
        Accept(game, owner, command);
        var after = game.SerializeFullState();
        Assert.False(game.Handle(owner, command).Accepted);
        Assert.Equal(after, game.SerializeFullState());
        if (restore) game = Restore(game);
        Settle(game, restore);

        var player = game.State.Players[owner];
        var enemy = game.State.Players[1 - owner].Field[0][0]!;
        Assert.Equal(originalTroops - (use ? 1000 : 0), enemy.CurrentTroops);
        Assert.Equal(use ? 1 : 0, player.Graveyard.Count(card => card.InstanceId == "blood-eagle"));
        if (use)
        {
            Assert.Null(player.Field[1][0]);
            Assert.Equal(extraGraves > 0 ? 1 : 0, player.Hand.Count);
            Assert.Equal(extraGraves > 0 ? 1 : 0, player.Library.Count);
        }
        else
        {
            Assert.Equal("blood-eagle", player.Field[1][0]!.InstanceId);
            Assert.True(player.Field[1][0]!.Hidden);
            Assert.Empty(player.Hand);
            Assert.Empty(player.Library);
            Assert.DoesNotContain(game.State.Events, item => item.Type == "effect-trigger"
                && item.Cards.Any(card => card.InstanceId == "blood-eagle"));
        }
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:S01-0320")]
    public void DecliningWithOneLegalGravePreservesTheNextRealResponseOpportunity(int owner, bool restore)
    {
        var game = Create(owner, 0);
        DestroyAndOrderBloodLast(game, owner, "fallen");
        Resolve(game, "mode:none");
        Settle(game, restore);
        if (restore) game = Restore(game);
        game.State.Players[owner].Field[0][0] = Card("S01-0309", "next-fallen", owner);
        DestroyAndOrderBloodLast(game, owner, "next-fallen");
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("blood-eagle", prompt.SourceInstanceId);
        Assert.Equal("order", prompt.Kind);
        Assert.Contains("skip", prompt.ValidChoices);
        Resolve(game, "skip");
        Settle(game, restore);
        Assert.True(game.State.Players[owner].Field[1][0]!.Hidden);
        Assert.Empty(game.State.Players[owner].Hand);
        Assert.Empty(game.State.Players[owner].Library);
    }

    private static L12GameEngine Create(int owner, int extraGraves)
    {
        var game = new L12GameEngine(Catalog, "blood-eagle-decline", "LOCAL", 1060100 + owner,
            ["合成甲", "合成乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 1 - owner;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear();
            player.Morale.Clear(); player.Resolving.Clear(); player.Removed.Clear();
            player.ExtraRelics.Clear(); player.Relic = null; player.UsedAbilities.Clear();
        }
        var counter = Card("S01-0320", "blood-eagle", owner);
        counter.Hidden = true; counter.SetRound = 0;
        game.State.Players[owner].Field[1][0] = counter;
        game.State.Players[owner].Field[0][0] = Card(extraGraves < 0 ? "S01-0002" : "S01-0309", "fallen", owner);
        game.State.Players[1 - owner].Field[0][0] = Card("S01-0001", "enemy", 1 - owner);
        for (var index = 0; index < extraGraves; index++)
            game.State.Players[owner].Graveyard.Add(Card("S01-0312", $"grave-{index}", owner));
        return game;
    }

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new() { InstanceId = instance, CardId = id, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, BaseTroops = d.Troops ?? 0,
            Troops = d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0,
            TrialValue = d.TrialValue ?? 0, OwnerIndex = owner, SummonRound = -1 };
    }

    private static void DestroyAndOrderBloodLast(L12GameEngine game, int owner, string instance)
    {
        var result = game.HandleGm(new L12GmCommand("destroyCard", owner, CardInstanceId: instance));
        Assert.True(result.Accepted, result.Error);
        var order = Assert.Single(game.State.PendingPrompts);
        if (order.Continuation != "trigger-batch-order")
        {
            Assert.Equal("pending-activation", order.Continuation);
            Assert.Equal("blood-eagle", order.SourceInstanceId);
            return;
        }
        var blood = order.ValidChoices.Single(id => order.Data[id].Contains("复仇血鹰"));
        Accept(game, owner, new("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [.. order.ValidChoices.Where(id => id != blood), blood]));
    }

    private static void Settle(L12GameEngine game, bool restore)
    {
        for (var count = 0; game.State.PendingPrompts.Count > 0; count++)
        {
            Assert.True(count < 20);
            var prompt = Assert.Single(game.State.PendingPrompts);
            Resolve(game, prompt.Kind == "response" ? "pass" : "mode:none");
        }
        // Checkpoint equivalence at the settled boundary as well as before declaration.
        if (restore) Assert.Equal(game.SerializeFullState(), Restore(game).SerializeFullState());
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Accept(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
    }

    private static void Accept(L12GameEngine game, int actor, L12Command command)
    {
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static L12GameEngine Restore(L12GameEngine game)
    {
        var state = game.SerializeFullState();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, state,
            game.RandomState ?? throw new InvalidOperationException("Synthetic checkpoint has no random state"),
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(state, restored.SerializeFullState());
        return restored;
    }
}
