using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialCapacityLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    [L12AbilityEvidence("S02-06D1:ability:static:b173428fa383ae26",
        "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-06M2:ability:rule:f86cd3914a10b001",
        "normal", "reconnect", "presentation-consumers")]
    public void TrialCapacityAndCompletedSetupUseTheSameAuthoritativeDeckRuleAcrossReconnect()
    {
        var basis = Catalog.DeckAt(0);
        var avalon = new L12PresetDeckDefinition
        {
            Name = "阿瓦隆已完成试炼",
            MasterId = "S02-06D1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = ["S02-06S6"],
        };
        var angus = new L12PresetDeckDefinition
        {
            Name = "安格斯双试炼",
            MasterId = "S02-06M2",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = ["S02-06S5", "S02-06S6"],
        };
        var game = new L12GameEngine(Catalog, "trial-capacity-lifecycle", "TRIAL-CAPACITY", 92601,
            ["甲", "乙"], [avalon, angus], skipPreparation: true, stateFormatVersion: 2);

        Assert.Equal(1, L12SpecialDeckRules.TrialCapacity(Catalog.Cards["S02-06D1"]));
        Assert.Equal(2, L12SpecialDeckRules.TrialCapacity(Catalog.Cards["S02-06M2"]));
        Assert.True(L12SpecialDeckRules.StartsTrialsCompleted(Catalog.Cards["S02-06D1"]));
        Assert.False(L12SpecialDeckRules.StartsTrialsCompleted(Catalog.Cards["S02-06M2"]));
        Assert.Equal(1, game.State.Players[0].SpecialZones.TrialCapacity);
        var completed = Assert.Single(game.State.Players[0].SpecialZones.Trials);
        Assert.True(completed.TrialCompleted);
        Assert.Equal(8, completed.TrialProgress);
        Assert.Equal(2, game.State.Players[1].SpecialZones.TrialCapacity);
        Assert.All(game.State.Players[1].SpecialZones.Trials, trial => Assert.False(trial.TrialCompleted));

        var opponentView = JsonSerializer.SerializeToElement(game.SnapshotFor(1), Json);
        var visibleTrial = opponentView.GetProperty("players")[0].GetProperty("specialZones")
            .GetProperty("trials").EnumerateArray().Single();
        Assert.Equal(completed.CardId, visibleTrial.GetProperty("cardId").GetString());
        Assert.True(visibleTrial.GetProperty("trialCompleted").GetBoolean());

        var checkpoint = game.SerializeFullState();
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence);
        Assert.Equal(1, game.State.Players[0].SpecialZones.TrialCapacity);
        completed = Assert.Single(game.State.Players[0].SpecialZones.Trials);
        Assert.True(completed.TrialCompleted);
        Assert.Equal(8, completed.TrialProgress);
        Assert.Equal(2, game.State.Players[1].SpecialZones.TrialCapacity);
        Assert.All(game.State.Players[1].SpecialZones.Trials, trial => Assert.False(trial.TrialCompleted));
    }
}
