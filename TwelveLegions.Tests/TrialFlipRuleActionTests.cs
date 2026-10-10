using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialFlipRuleActionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    public static IEnumerable<object[]> Trials() => Catalog.Cards.Values
        .Where(card => card.CardType == "trial").SelectMany(card => new[] { new object[] { card.Id, false }, new object[] { card.Id, true } });

    [Theory]
    [MemberData(nameof(Trials))]
    public void CompletionIsImmediateRuleActionAndCannotEnterNegatableActiveStack(string cardId, bool restore)
    {
        var game = Create(cardId);
        if (restore) game = Restore(game);
        var trial = game.State.Players[0].SpecialZones.Trials.Single();
        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId, Ability: "completeTrial")).Accepted);
        Assert.True(trial.TrialCompleted);
        Assert.DoesNotContain(game.State.EffectStack, item => item.Data.GetValueOrDefault("ability") == "completeTrial");
        Assert.Single(game.State.Events, entry => entry.Type == "trial" && entry.Text.Contains("完成试炼"));
        game = Restore(game);
        Assert.True(game.State.Players[0].SpecialZones.Trials.Single().TrialCompleted);
        Assert.False(game.Handle(0, new L12Command("activateAbility", trial.InstanceId, Ability: "completeTrial")).Accepted);
        Assert.Single(game.State.Events, entry => entry.Type == "trial" && entry.Text.Contains("完成试炼"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegatingPrintedTriggerDoesNotUndoRuleFlip(bool restore)
    {
        var game = Create("S02-06S3");
        var arthur = Catalog.Cards["S02-0601"];
        game.State.Players[0].Graveyard.Add(new L12CardInstance { InstanceId = "arthur",
            CardId = arthur.Id, Name = arthur.NameZh, CardType = arthur.CardType, Faction = arthur.Faction });
        Assert.True(game.Handle(0, new L12Command("activateAbility", "trial", Ability: "completeTrial")).Accepted);
        Assert.True(game.State.Players[0].SpecialZones.Trials.Single().TrialCompleted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none")).Accepted);
        if (restore) game = Restore(game);
        var effect = Assert.Single(game.State.EffectStack);
        Assert.Equal("trial-complete", effect.Trigger);
        effect.Negated = true;
        for (var count = 0; count < 12 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
        {
            prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.True(game.State.Players[0].SpecialZones.Trials.Single().TrialCompleted);
        Assert.Single(game.State.Events, entry => entry.Type == "trial" && entry.Text.Contains("完成试炼"));
    }

    [Theory]
    [MemberData(nameof(Trials))]
    public void InsufficientProgressCannotFlip(string cardId, bool restore)
    {
        var game = Create(cardId);
        game.State.Players[0].SpecialZones.Trials.Single().TrialProgress = 7;
        if (restore) game = Restore(game);
        Assert.False(game.Handle(0, new L12Command("activateAbility", "trial", Ability: "completeTrial")).Accepted);
        Assert.False(game.State.Players[0].SpecialZones.Trials.Single().TrialCompleted);
        Assert.Empty(game.State.EffectStack);
    }

    private static L12GameEngine Create(string cardId)
    {
        var game = new L12GameEngine(Catalog, "trial-flip-rule", "TRIAL-FLIP", 918,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.SpecialZones.Trials.Clear();
        }
        var definition = Catalog.Cards[cardId];
        game.State.Players[0].SpecialZones.Trials.Add(new L12CardInstance
        {
            InstanceId = "trial", CardId = cardId, Name = definition.NameZh, CardType = "trial",
            EffectText = definition.Effect, Faction = definition.Faction, TrialProgress = 8,
        });
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
}
