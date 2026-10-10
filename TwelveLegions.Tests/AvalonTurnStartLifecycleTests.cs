using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AvalonTurnStartLifecycleTests
{
    private const string AbilityId = "S02-06D1:ability:turn-start:97dca04b36fe51bf";
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool withTrial)
    {
        var baseDeck = Catalog.DeckAt(0);
        var avalonDeck = new L12PresetDeckDefinition
        {
            Name = "Avalon lifecycle",
            MasterId = "S02-06D1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "avalon-turn-start", "AVALON", seed,
            ["甲", "乙"], [avalonDeck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Reset;
        var player = game.State.Players[0];
        player.SpecialZones.Runes = 0;
        player.SpecialZones.Trials.Clear();
        if (withTrial)
        {
            var definition = Catalog.Cards["S02-06S4"];
            player.SpecialZones.Trials.Add(new L12CardInstance
            {
                InstanceId = $"avalon-trial-{seed}",
                CardId = definition.Id,
                Name = definition.NameZh,
                CardType = definition.CardType,
                Faction = definition.Faction,
                ImageUrl = definition.ImageUrl,
                Cost = definition.Cost ?? 0,
                EffectText = definition.Effect,
                Traits = [.. definition.Traits],
                Profession = definition.Profession,
                BaseTroops = definition.Troops ?? 0,
                Troops = definition.Troops ?? 0,
                TrialValue = definition.TrialValue ?? 0,
                OwnerIndex = 0,
            });
        }
        return game;
    }

    private static void Queue(L12GameEngine game)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueAvalonTurnStart",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [0]);
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static L12ActionEvent Result(L12GameEngine game, string status)
        => Assert.Single(game.State.Events, entry => entry.EffectAbilityId == AbilityId
            && entry.EffectResultStatus == status);

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "presentation-consumers")]
    public void TurnStartAdvancesTheOpenTrialAndGainsOneRuneAsOneEffect()
    {
        var game = Create(9121, withTrial: true);
        Queue(game);
        Assert.Single(game.State.EffectStack);
        Assert.Equal(0, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);

        PassResponses(game);

        Assert.Equal(1, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        Assert.False(string.IsNullOrWhiteSpace(Result(game, "resolved").EffectSceneId));
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "no-target")]
    public void MissingOpenTrialSkipsOnlyTheAdvanceAndStillGainsTheRune()
    {
        var game = Create(9122, withTrial: false);
        Queue(game);

        PassResponses(game);

        Assert.Empty(game.State.Players[0].SpecialZones.Trials);
        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        _ = Result(game, "resolved");
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "negated")]
    public void NegatingTheCombinedEffectStopsBothAdvanceAndRuneGain()
    {
        var game = Create(9123, withTrial: true);
        Queue(game);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Equal(0, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        _ = Result(game, "negated");
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "duplicate-submit", "reconnect")]
    public void ResponseWindowSurvivesReconnectAndRejectsAnExpiredPass()
    {
        var original = Create(9124, withTrial: true);
        Queue(original);
        var promptId = Assert.Single(original.State.PendingPrompts).PromptId;
        var checkpoint = original.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        var game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            original.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            original.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(promptId, Assert.Single(game.State.PendingPrompts).PromptId);

        Resolve(game, "pass");
        var duplicate = game.Handle(Assert.Single(game.State.PendingPrompts).PlayerIndex,
            new L12Command("resolvePrompt", PromptId: promptId, Choice: "pass"));
        Assert.False(duplicate.Accepted);
        PassResponses(game);

        Assert.Equal(1, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        _ = Result(game, "resolved");
    }
}
