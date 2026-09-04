using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedPlatformTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

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

    [Fact]
    public void FactionPlacementTitlesRequireTheHighestTier()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-title-tier", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var leader = store.Register("ranked-tier-leader", "Password123!").Account!;
        var rival = store.Register("ranked-tier-rival", "Password123!").Account!;
        store.SelectRankedFaction(leader.Id, "order");
        store.SelectRankedFaction(rival.Id, "order");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"title-placement-{index}", leader.Id, rival.Id, 0);

        var placedLeader = store.RankedProfile(leader.Id);
        Assert.Equal(1, placedLeader.FactionRank);
        Assert.True(placedLeader.TierIndex < 4);
        Assert.Null(placedLeader.Title);

        for (var index = 0; index < 150 && store.RankedProfile(leader.Id).TierIndex < 4; index++)
            store.SettleRankedMatch($"title-climb-{index}", leader.Id, rival.Id, 0);

        var highestTierLeader = store.RankedProfile(leader.Id);
        Assert.Equal(4, highestTierLeader.TierIndex);
        Assert.Equal("秩序冠首", highestTierLeader.Title);
        Assert.Contains("秩序冠首", highestTierLeader.Titles);
        Assert.Null(store.RankedProfile(rival.Id).Title);
    }

    [Fact]
    public void RankedMasterChampionUsesAuthoritativeSeasonUsageAndDedicatedTitle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-master", Guid.NewGuid().ToString("N"));
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"),
            catalog.PresetDecks, officialCards: catalog.Cards);
        var amaterasu = store.Register("ranked-amaterasu", "Password123!").Account!;
        var rival = store.Register("ranked-master-rival", "Password123!").Account!;
        store.SelectRankedFaction(amaterasu.Id, "order");
        store.SelectRankedFaction(rival.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"master-placement-{index}", amaterasu.Id, rival.Id, 0,
                "S01-04M1", "S02-03M1");

        var champion = Assert.Single(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");
        Assert.Equal("天照大神", champion.MasterName);
        Assert.Equal("最强天照", champion.Title);
        Assert.Equal(amaterasu.Username, champion.Username);
        Assert.Equal(5, champion.Games);
        var profile = store.RankedProfile(amaterasu.Id);
        Assert.Contains("最强天照", profile.Titles);
        Assert.Contains("最强天照", profile.MasterTitles);
        var selected = store.SelectRankedMasterTitle(amaterasu.Id, "最强天照");
        Assert.Equal("最强天照", selected.SelectedMasterTitle);
        var battleIdentity = store.RankedBattleIdentity(amaterasu.Id, 0);
        Assert.Equal(selected.RankLabel, battleIdentity.RankLabel);
        Assert.Equal("最强天照", battleIdentity.MasterTitle);
        Assert.Throws<ArgumentException>(() => store.SelectRankedMasterTitle(amaterasu.Id, "未获得的称号"));

        store.SelectRankedFaction(amaterasu.Id, "fate");
        Assert.DoesNotContain(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");
        Assert.Null(store.RankedProfile(amaterasu.Id).SelectedMasterTitle);
    }

    [Fact]
    public void ExistingSeasonRankedMatchesBackfillMasterTitlesExactlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-master-import", Guid.NewGuid().ToString("N"));
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"),
            catalog.PresetDecks, officialCards: catalog.Cards);
        var amaterasu = store.Register("import-amaterasu", "Password123!").Account!;
        var rival = store.Register("import-rival", "Password123!").Account!;
        store.SelectRankedFaction(amaterasu.Id, "order");
        store.SelectRankedFaction(rival.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"import-placement-{index}", amaterasu.Id, rival.Id, 0);
        var now = DateTimeOffset.UtcNow;
        var history = Enumerable.Range(0, 5).Select(index => new L12RankingMatch(
            $"historic-master-{index}", amaterasu.Username, rival.Username,
            now.AddMinutes(index).ToString("O"), now.AddMinutes(index + 1).ToString("O"), 0,
            "天照大神", "西芙", 0)).ToArray();

        Assert.Equal(5, store.ImportRankedMasterHistory(history));
        Assert.Equal(0, store.ImportRankedMasterHistory(history));
        var champion = Assert.Single(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");
        Assert.Equal(5, champion.Games);
        Assert.Equal(5, champion.Wins);
        Assert.Equal("最强天照", champion.Title);
    }

    [Fact]
    public void SeasonSwitchFreezesHistoricalTitlesAndPlayerName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-season-honors", Guid.NewGuid().ToString("N"));
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"),
            catalog.PresetDecks, officialCards: catalog.Cards);
        var champion = store.Register("season-champion", "Password123!").Account!;
        var rival = store.Register("season-rival", "Password123!").Account!;
        store.SelectRankedFaction(champion.Id, "order");
        store.SelectRankedFaction(rival.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"season-honor-{index}", champion.Id, rival.Id, 0,
                "S01-04M1", "S02-03M1");
        Assert.Contains("最强天照", store.RankedProfile(champion.Id).Titles);
        Assert.Empty(store.RankedSeasonHonors());

        var admin = store.Login("Admin", "L12master").Account!;
        var current = store.OperationsConfig(admin);
        store.ApplyOperationsConfig(admin, current.Config with
        {
            Season = new L12SeasonConfig("S-history-next", "下一赛季", "active", null, null),
        }, current.Version, "验证赛季荣誉归档", new L12AdminAuditContext("ranked-season-honor-test"));

        var honor = Assert.Single(store.RankedSeasonHonors(), item => item.Username == champion.Username);
        Assert.Equal(current.Config.Season.Id, honor.SeasonId);
        Assert.Equal(current.Config.Season.Name, honor.SeasonName);
        Assert.Contains("最强天照", honor.Titles);
        Assert.Equal($"七曜值 {honor.SevenValue:N0}", honor.DisplayValue);
    }

    [Fact]
    public void RankedBroadcastIsClaimedAndCompletedOncePerAccountAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-delivery", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var first = store.Register("broadcast-first", "Password123!").Account!;
        var second = store.Register("broadcast-second", "Password123!").Account!;
        var viewer = store.Register("broadcast-viewer", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"broadcast-placement-{index}", first.Id, second.Id, 0);

        var claimed = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewer.Id));
        Assert.Equal("win-streak", claimed.Broadcast.EventType);
        Assert.Null(store.ClaimRankedBroadcast(viewer.Id));
        Assert.Equal(claimed.Broadcast.Id, store.ClaimRankedBroadcast(first.Id)!.Broadcast.Id);
        Assert.False(store.CompleteRankedBroadcast(viewer.Id, claimed.Broadcast.Id, "wrong-token"));
        Assert.True(store.CompleteRankedBroadcast(viewer.Id, claimed.Broadcast.Id, claimed.ClaimToken));
        Assert.True(store.CompleteRankedBroadcast(viewer.Id, claimed.Broadcast.Id, claimed.ClaimToken));
        Assert.Null(store.ClaimRankedBroadcast(viewer.Id));

        var reloaded = new L12PlatformStore(path);
        Assert.Null(reloaded.ClaimRankedBroadcast(viewer.Id));
    }

    [Fact]
    public void RankedAnalyticsAggregatesMasterUsageSidesAndMatchupsWithoutRawMatches()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-analytics", Guid.NewGuid().ToString("N"));
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"),
            catalog.PresetDecks, officialCards: catalog.Cards);
        var now = DateTimeOffset.UtcNow;
        var matches = new[]
        {
            new L12RankingMatch("analytics-1", "甲", "乙", now.AddHours(-2).ToString("O"),
                now.AddHours(-1).ToString("O"), 0, "天照大神", "西芙", 0),
            new L12RankingMatch("analytics-2", "乙", "甲", now.AddMinutes(-40).ToString("O"),
                now.AddMinutes(-20).ToString("O"), 0, "西芙", "天照大神", 0),
            new L12RankingMatch("analytics-old", "甲", "乙", now.AddDays(-40).ToString("O"),
                now.AddDays(-40).AddMinutes(10).ToString("O"), 0, "天照大神", "西芙", 0),
        };

        var analytics = store.RankedAnalytics(matches, "7d");

        Assert.Equal(2, analytics.Summary.Matches);
        Assert.Equal(2, analytics.Summary.ActiveMasters);
        var amaterasu = Assert.Single(analytics.Masters, item => item.MasterId == "S01-04M1");
        Assert.Equal(2, amaterasu.Games);
        Assert.Equal(1, amaterasu.Wins);
        Assert.Equal(1, amaterasu.FirstGames);
        Assert.Equal(1, amaterasu.SecondGames);
        Assert.Equal(50d, amaterasu.WinRate);
        var versusSif = Assert.Single(analytics.Matchups,
            item => item.MasterId == "S01-04M1" && item.OpponentMasterId == "ST03-M1");
        Assert.Equal(2, versusSif.Games);
        Assert.Equal(1, versusSif.Wins);
    }

    [Fact]
    public async Task RepeatedOpponentMatchesAllSettleAndConcurrentReplayIsIdempotentButConflictsFailClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-replay", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("repeat-first", "Password123!").Account!;
        var second = store.Register("repeat-second", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");

        for (var index = 0; index < 8; index++)
        {
            var result = store.SettleRankedMatch($"repeat-{index}", first.Id, second.Id, index % 2);
            Assert.DoesNotContain(result.First.Components, component => component.Kind == "same-opponent");
            Assert.DoesNotContain(result.Second.Components, component => component.Kind == "same-opponent");
        }
        Assert.Equal(8, store.RankedProfile(first.Id).Wins + store.RankedProfile(first.Id).Losses);
        Assert.Equal(8, store.RankedProfile(second.Id).Wins + store.RankedProfile(second.Id).Losses);

        var parallel = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
            store.SettleRankedMatch("repeat-concurrent", first.Id, second.Id, 0))));
        Assert.All(parallel, result => Assert.Equal("repeat-concurrent", result.First.MatchId));
        Assert.Equal(9, store.RankedProfile(first.Id).Wins + store.RankedProfile(first.Id).Losses);
        Assert.Throws<InvalidOperationException>(() =>
            store.SettleRankedMatch("repeat-concurrent", first.Id, second.Id, 1));

        var reloaded = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var replay = reloaded.SettleRankedMatch("repeat-concurrent", first.Id, second.Id, 0);
        Assert.Equal("repeat-concurrent", replay.First.MatchId);
        Assert.Equal(9, reloaded.RankedProfile(first.Id).Wins + reloaded.RankedProfile(first.Id).Losses);
    }
}
