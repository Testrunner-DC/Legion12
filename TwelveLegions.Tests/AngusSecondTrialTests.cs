using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AngusSecondTrialTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string secondTrial = "ST06-S1")
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition { Name = "安格斯双试炼回归", MasterId = "S02-06M2",
            CardIds = [.. baseDeck.CardIds], MoraleIds = [.. baseDeck.MoraleIds], SpecialIds = ["S02-06S6", secondTrial] };
        var game = new L12GameEngine(Catalog, "angus-two-trials", "ANGUS307", 30709, ["甲", "乙"],
            [deck, baseDeck], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main; game.State.Round = 2; game.State.TurnSerial = 3;
        game.State.PendingPrompts.Clear(); game.State.EffectStack.Clear();
        foreach (var player in game.State.Players) { player.Hand.Clear(); player.Resolving.Clear(); player.Graveyard.Clear(); player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3]; }
        return game;
    }

    private static JsonElement[] TrialViews(L12GameEngine game, int viewer)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(viewer), Json).GetProperty("players")[0]
            .GetProperty("specialZones").GetProperty("trials").EnumerateArray().ToArray();

    [Fact]
    public void SecondTrialSnapshotRebuildsAbilitiesForProgressCompletionAndTurnWithoutMutatingState()
    {
        var game = Create(); var player = game.State.Players[0];
        Assert.Equal(2, player.SpecialZones.TrialCapacity);
        Assert.Equal(2, player.SpecialZones.Trials.Count);
        player.SpecialZones.Trials[0].TrialCompleted = true;
        var second = player.SpecialZones.Trials[1];
        var zero = TrialViews(game, 0)[1].GetProperty("abilities").EnumerateArray().ToArray();
        Assert.Equal("completeTrial", Assert.Single(zero).GetProperty("id").GetString());
        Assert.False(zero[0].GetProperty("enabled").GetBoolean());
        second.TrialProgress = 8;
        Assert.True(Assert.Single(TrialViews(game, 0)[1].GetProperty("abilities").EnumerateArray()).GetProperty("enabled").GetBoolean());
        var hidden = TrialViews(game, 1)[1];
        Assert.Equal("hidden-trial", hidden.GetProperty("cardId").GetString());
        Assert.False(hidden.TryGetProperty("abilities", out _));
        second.TrialCompleted = true;
        var completed = Assert.Single(TrialViews(game, 0)[1].GetProperty("abilities").EnumerateArray());
        Assert.Equal("skyCityDiscount", completed.GetProperty("id").GetString());
        Assert.Equal(second.CardId, TrialViews(game, 1)[1].GetProperty("cardId").GetString());
        game.State.ActivePlayer = 1;
        Assert.False(Assert.Single(TrialViews(game, 0)[1].GetProperty("abilities").EnumerateArray()).GetProperty("enabled").GetBoolean());
        Assert.True(second.TrialCompleted); Assert.Equal(8, second.TrialProgress);
    }

    [Fact]
    public void FirstCompletedTrialDoesNotPreventAdvancingAndCompletingSecond()
    {
        var game = Create(); var player = game.State.Players[0];
        var first = player.SpecialZones.Trials[0]; var second = player.SpecialZones.Trials[1];
        first.TrialProgress = 8;
        Complete(game, first);
        Assert.False(second.TrialCompleted);
        Assert.Equal(0, second.TrialProgress);
        // Isolate progress routing from Angus's separate optional rune trigger (covered elsewhere).
        player.UsedAbilities.Add($"trigger:angus-trial-rune:{game.State.TurnSerial}");
        typeof(L12GameEngine).GetMethod("AdvanceTrial", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [0, 8, second]);
        Assert.Equal(8, second.TrialProgress); Assert.Equal(8, first.TrialProgress);
        game.State.PendingPrompts.Clear(); game.State.EffectStack.Clear();
        Complete(game, second);
        Assert.True(second.TrialCompleted); Assert.True(first.TrialCompleted);
        Assert.Equal(2, player.SpecialZones.Trials.Count);
    }

    [Fact]
    public void SecondCompletedFenianTrialCanPayRuneAndReadyItsSelectedLegion()
    {
        var game = Create("S02-06S5"); var player = game.State.Players[0];
        foreach (var trial in player.SpecialZones.Trials) trial.TrialCompleted = true;
        var second = player.SpecialZones.Trials[1];
        var legion = (L12CardInstance)typeof(L12GameEngine)
            .GetMethod("CreateCard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, ["S02-0610", "second-trial-finn"])!;
        legion.Tapped = true; player.Field[0][0] = legion; player.SpecialZones.Runes = 1;
        var begin = game.Handle(0, new L12Command("activateAbility", second.InstanceId, Ability: "fenianReady"));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(legion.InstanceId, prompt.ValidChoices);
        var selected = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: legion.InstanceId));
        Assert.True(selected.Accepted, selected.Error);
        PassResponses(game);
        Assert.False(legion.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.False(game.Handle(0, new L12Command("activateAbility", second.InstanceId, Ability: "fenianReady")).Accepted);
    }

    private static void Complete(L12GameEngine game, L12CardInstance trial)
    {
        var result = game.Handle(0, new L12Command("activateAbility", trial.InstanceId, Ability: "completeTrial"));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
        Assert.True(trial.TrialCompleted);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var i = 0; i < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; i++)
        {
            var prompt = game.State.PendingPrompts[0];
            var pass = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(pass.Accepted, pass.Error);
        }
    }
}
