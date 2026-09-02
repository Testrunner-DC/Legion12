using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedPlatformTests
{
    [Fact]
    public void PlacementSettlementIsIdempotentAndLeaderboardIsFactionScoped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("ranked-first", "Password123!").Account!;
        var second = store.Register("ranked-second", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");

        L12RankedSettlementPair? fifth = null;
        for (var index = 0; index < 5; index++)
            fifth = store.SettleRankedMatch($"placement-{index}", first.Id, second.Id, 0);
        var replay = store.SettleRankedMatch("placement-4", first.Id, second.Id, 0);

        Assert.NotNull(fifth);
        Assert.Equal(fifth!.First.After, replay.First.After);
        Assert.True(store.RankedProfile(first.Id).Placed);
        Assert.True(store.RankedProfile(first.Id).SevenValue <= 29999);
        Assert.Single(store.RankedLeaderboard("order"));
        Assert.Single(store.RankedLeaderboard("chaos"));
    }

    [Fact]
    public void FactionChangeResetsVisibleSeasonProgressButKeepsHiddenRating()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-switch", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("switch-first", "Password123!").Account!;
        var second = store.Register("switch-second", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        store.SettleRankedMatch("switch-match", first.Id, second.Id, 0);
        var hidden = store.HiddenRating(first.Id);

        var changed = store.SelectRankedFaction(first.Id, "fate");

        Assert.Equal("命运", changed.Faction);
        Assert.Equal(0, changed.PlacementPlayed);
        Assert.Equal(0, changed.SevenValue);
        Assert.Equal(hidden, store.HiddenRating(first.Id));
        var history = Assert.Single(store.RankedOverview(first.Id).History);
        Assert.Equal("秩序", history.Faction);
        Assert.Equal(1, history.PlacementPlayed);
    }

    [Fact]
    public void RatingGapCorrectionHelpsUnderdogOnBothWinAndLoss()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-gap", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var strong = store.Register("ranked-strong", "Password123!").Account!;
        var underdog = store.Register("ranked-underdog", "Password123!").Account!;
        store.SelectRankedFaction(strong.Id, "order");
        store.SelectRankedFaction(underdog.Id, "chaos");
        for (var index = 0; index < 5; index++) store.SettleRankedMatch($"gap-placement-{index}", strong.Id, underdog.Id, 0);

        var result = store.SettleRankedMatch("gap-ranked", strong.Id, underdog.Id, 0);
        var loserGap = Assert.Single(result.Second.Components, component => component.Kind == "gap");

        Assert.True(loserGap.Value > 0);
    }

    [Fact]
    public void EndingFiveWinStreakAwardsTerminationButUsesPreMatchSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-streak", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var challenger = store.Register("ranked-challenger", "Password123!").Account!;
        var streaking = store.Register("ranked-streaking", "Password123!").Account!;
        store.SelectRankedFaction(challenger.Id, "order");
        store.SelectRankedFaction(streaking.Id, "fate");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"streak-placement-{index}", challenger.Id, streaking.Id, 1);

        var result = store.SettleRankedMatch("streak-ended", challenger.Id, streaking.Id, 0);

        Assert.Contains(result.First.Components, component => component.Kind == "streak-termination");
        Assert.Contains(result.Broadcasts, item => item.EventType == "streak-ended");
    }
}
