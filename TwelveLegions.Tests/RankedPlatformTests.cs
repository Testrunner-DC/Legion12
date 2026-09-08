using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedPlatformTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void BattleIdentityDoesNotInventEmptyRankOrTitlePlaceholders()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-empty-identity", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var account = store.Register("temptya0ad7", "Password123!").Account!;

        var identity = store.RankedBattleIdentity(account.Id, 0);

        Assert.Equal(string.Empty, identity.RankLabel);
        Assert.Null(identity.MasterTitle);
    }

    [Fact]
    public void PlacementSettlementIsIdempotentAndLeaderboardIsFactionScoped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("trankeff2d4", "Password123!").Account!;
        var second = store.Register("tranked6015", "Password123!").Account!;
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
        var first = store.Register("tswitc417e8", "Password123!").Account!;
        var second = store.Register("tswitcbe334", "Password123!").Account!;
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
        var strong = store.Register("tranke753f9", "Password123!").Account!;
        var underdog = store.Register("tranke04938", "Password123!").Account!;
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
        var challenger = store.Register("trankead8d1", "Password123!").Account!;
        var streaking = store.Register("tranke0df09", "Password123!").Account!;
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
        var leader = store.Register("tranke53686", "Password123!").Account!;
        var rival = store.Register("trankeb73d9", "Password123!").Account!;
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
        var amaterasu = store.Register("tranke6f6d2", "Password123!").Account!;
        var rival = store.Register("trankefabe6", "Password123!").Account!;
        store.SelectRankedFaction(amaterasu.Id, "order");
        store.SelectRankedFaction(rival.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"master-placement-{index}", amaterasu.Id, rival.Id, 0,
                "S01-04M1", "S02-03M1");
        var now = DateTimeOffset.UtcNow;
        store.ImportRankedMasterTitleFacts(TitleFacts("master-title", amaterasu.Id,
            "S01-04M1", "ST03-M1", now));

        var champion = Assert.Single(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");
        Assert.Equal("天照大神", champion.MasterName);
        Assert.Equal("最强天照", champion.Title);
        Assert.Equal(amaterasu.Username, champion.Username);
        Assert.Equal(20, champion.Games);
        var leaderboard = Assert.Single(store.RankedLeaderboard("order"));
        Assert.Equal("S01-04M1", leaderboard.FavoriteMasterId);
        Assert.Equal("天照大神", leaderboard.FavoriteMasterName);
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
        Assert.Contains(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");
        Assert.Equal("最强天照", store.RankedProfile(amaterasu.Id).SelectedMasterTitle);
    }

    [Fact]
    public void LegacyRankedHistoryDoesNotFabricateTitleFactsAndRichFactsImportExactlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-master-import", Guid.NewGuid().ToString("N"));
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"),
            catalog.PresetDecks, officialCards: catalog.Cards);
        var amaterasu = store.Register("timpor2f195", "Password123!").Account!;
        var rival = store.Register("timpor3fc53", "Password123!").Account!;
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
        Assert.DoesNotContain(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");

        var facts = TitleFacts("historic-master-fact", amaterasu.Id,
            "S01-04M1", "ST03-M1", now);
        Assert.Equal(20, store.ImportRankedMasterTitleFacts(facts));
        Assert.Equal(0, store.ImportRankedMasterTitleFacts(facts));
        var champion = Assert.Single(store.RankedMasterChampions(), item => item.MasterId == "S01-04M1");
        Assert.Equal(20, champion.Games);
        Assert.Equal(20, champion.Wins);
        Assert.Equal("最强天照", champion.Title);
    }

    [Fact]
    public void SeasonSwitchFreezesHistoricalTitlesAndPlayerName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-season-honors", Guid.NewGuid().ToString("N"));
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"),
            catalog.PresetDecks, officialCards: catalog.Cards);
        var champion = store.Register("tseaso0614c", "Password123!").Account!;
        var rival = store.Register("tseaso6a2c9", "Password123!").Account!;
        store.SelectRankedFaction(champion.Id, "order");
        store.SelectRankedFaction(rival.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"season-honor-{index}", champion.Id, rival.Id, 0,
                "S01-04M1", "S02-03M1");
        store.ImportRankedMasterTitleFacts(TitleFacts("season-honor-title", champion.Id,
            "S01-04M1", "ST03-M1", DateTimeOffset.UtcNow));
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
        var first = store.Register("tbroadb3958", "Password123!").Account!;
        var second = store.Register("tbroadb355a", "Password123!").Account!;
        var viewer = store.Register("tbroad418d4", "Password123!").Account!;
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
    public void RankedBroadcastSubscriptionSkipsBacklogAndNeverReissuesUnconfirmedClaims()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-subscription", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var first = store.Register("tbclivc171f", "Password123!").Account!;
        var second = store.Register("tbcliv9080f", "Password123!").Account!;
        var viewer = store.Register("tbclivf139d", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"broadcast-offline-{index}", first.Id, second.Id, 0);

        var subscribedAt = DateTimeOffset.UtcNow;
        store.SettleRankedMatch("broadcast-live-5", first.Id, second.Id, 0);
        var firstClaim = Assert.IsType<L12RankedBroadcastClaimView>(
            store.ClaimRankedBroadcast(viewer.Id, subscribedAt));
        Assert.Equal("broadcast-live-5", firstClaim.Broadcast.MatchId);

        store.SettleRankedMatch("broadcast-live-6", first.Id, second.Id, 0);
        var secondClaim = Assert.IsType<L12RankedBroadcastClaimView>(
            store.ClaimRankedBroadcast(viewer.Id, subscribedAt));
        Assert.Equal("broadcast-live-6", secondClaim.Broadcast.MatchId);
        Assert.NotEqual(firstClaim.Broadcast.Id, secondClaim.Broadcast.Id);
        Assert.Null(store.ClaimRankedBroadcast(viewer.Id, subscribedAt));
        Assert.False(store.CompleteRankedBroadcast(viewer.Id, firstClaim.Broadcast.Id, "wrong-token"));
        Assert.True(store.CompleteRankedBroadcast(viewer.Id, firstClaim.Broadcast.Id, firstClaim.ClaimToken));

        var reloaded = new L12PlatformStore(path);
        Assert.Null(reloaded.ClaimRankedBroadcast(viewer.Id, subscribedAt));
        Assert.Null(reloaded.ClaimRankedBroadcastAt(viewer.Id, subscribedAt,
            secondClaim.LeaseExpiresAt.AddSeconds(1)));
        Assert.True(reloaded.CompleteRankedBroadcast(viewer.Id, secondClaim.Broadcast.Id,
            secondClaim.ClaimToken));
        Assert.Null(reloaded.ClaimRankedBroadcast(viewer.Id, subscribedAt));
    }

    [Fact]
    public void RankedBroadcastRejectsClientClockRollbackOutsideRealtimeWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-clock", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("tbccloe7962", "Password123!").Account!;
        var second = store.Register("tbcclo71042", "Password123!").Account!;
        var viewer = store.Register("tbcclob96ee", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"broadcast-clock-old-{index}", first.Id, second.Id, 0);

        Assert.Null(store.ClaimRankedBroadcastAt(viewer.Id, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    [Fact]
    public async Task ConcurrentTabsCanClaimTheSameRankedBroadcastOnlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-tabs", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("tbctab0fa4a", "Password123!").Account!;
        var second = store.Register("tbctabab421", "Password123!").Account!;
        var viewer = store.Register("tbctabfd997", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        for (var index = 0; index < 4; index++)
            store.SettleRankedMatch($"broadcast-tabs-setup-{index}", first.Id, second.Id, 0);
        var subscribedAt = DateTimeOffset.UtcNow;
        store.SettleRankedMatch("broadcast-tabs-live", first.Id, second.Id, 0);

        var claims = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            store.ClaimRankedBroadcast(viewer.Id, subscribedAt))));

        Assert.Single(claims, claim => claim is not null);
        Assert.Equal("broadcast-tabs-live", claims.Single(claim => claim is not null)!.Broadcast.MatchId);
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
        var first = store.Register("trepea1ca17", "Password123!").Account!;
        var second = store.Register("trepeacf7fc", "Password123!").Account!;
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

    [Fact]
    public void RankedConfigRejectsDifferentNumericValuesForTheSameTierAcrossFactions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-shared-tiers", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var admin = store.Login("Admin", "L12master").Account!;
        var config = store.RankedConfig(admin);
        var factions = config.Factions.Select((faction, factionIndex) => faction with
        {
            Tiers = faction.Tiers.Select((tier, tierIndex) => factionIndex == 1 && tierIndex == 2
                ? tier with { BaseDelta = tier.BaseDelta + 1 }
                : tier).ToArray(),
        }).ToArray();

        var error = Assert.Throws<L12OperationsConfigException>(() => store.UpdateRankedConfig(admin,
            config with { Factions = factions }, "验证同段位共享数值", new L12AdminAuditContext("ranked-tier-test")));

        Assert.Equal("inconsistent_ranked_tier_values", error.Code);
        Assert.Equal(config.Factions[1].Tiers[2].BaseDelta,
            store.RankedConfig(admin).Factions[1].Tiers[2].BaseDelta);
    }

    [Fact]
    public void RankedTimeControlPersistsValidValuesAndRejectsEveryOutOfRangeFieldWithAudit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-time-control", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var admin = store.Login("Admin", "L12master").Account!;
        var original = store.RankedConfig(admin);
        Assert.Equal(new L12RankedTimeControlConfig(1500, 240, 240, 60, 60), original.TimeControl);
        var configured = new L12RankedTimeControlConfig(1800, 300, 180, 75, 90);

        var saved = store.UpdateRankedConfig(admin, original with { TimeControl = configured },
            "调整排位计时", new L12AdminAuditContext("ranked-time-control"));

        Assert.Equal(configured, saved.TimeControl);
        Assert.Equal(configured, new L12PlatformStore(path).RankedConfig(admin).TimeControl);
        Assert.Contains(store.AdminAudit(category: "operations"), audit =>
            audit.Action == "ranked-config-apply" && audit.Reason == "调整排位计时");

        var invalid = new[]
        {
            configured with { TotalTimeSeconds = 299 },
            configured with { OperationTimeSeconds = 901 },
            configured with { ReconnectGraceSeconds = 14 },
            configured with { DisasterDecisionSeconds = 301 },
            configured with { MulliganDecisionSeconds = 9 },
        };
        foreach (var value in invalid)
            Assert.Throws<L12OperationsConfigException>(() => store.UpdateRankedConfig(admin,
                saved with { TimeControl = value }, "非法范围", new L12AdminAuditContext("ranked-time-invalid")));
        Assert.Equal(configured, store.RankedConfig(admin).TimeControl);
    }

    [Fact]
    public void RankedBroadcastRulesPersistAndControlOnlyFutureBroadcasts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-config", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register("tbroadb4498", "Password123!").Account!;
        var second = store.Register("tbroad76572", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        var original = store.RankedConfig(admin);
        var configured = new L12RankedBroadcastConfig(28, 7, 21, 2, 8, 0,
            true, false, false, false, false);

        var saved = store.UpdateRankedConfig(admin, original with { Broadcast = configured },
            "调整排位广播", new L12AdminAuditContext("ranked-broadcast-config"));

        Assert.Equal(configured, saved.Broadcast);
        Assert.Equal(configured, store.RankedBroadcastSettings());
        store.SettleRankedMatch("broadcast-config-1", first.Id, second.Id, 0);
        var secondResult = store.SettleRankedMatch("broadcast-config-2", first.Id, second.Id, 0);
        Assert.Single(secondResult.Broadcasts, row => row.EventType == "win-streak");
        Assert.DoesNotContain(secondResult.Broadcasts, row => row.EventType == "streak-ended");

        var reloaded = new L12PlatformStore(path);
        Assert.Equal(configured, reloaded.RankedBroadcastSettings());
        var loaded = reloaded.RankedConfig(admin);
        var blocked = configured with { MinimumTierIndex = 4 };
        reloaded.UpdateRankedConfig(admin, loaded with { Broadcast = blocked },
            "提高广播段位门槛", new L12AdminAuditContext("ranked-broadcast-tier"));
        var thirdResult = reloaded.SettleRankedMatch("broadcast-config-3", first.Id, second.Id, 0);
        Assert.DoesNotContain(thirdResult.Broadcasts, row => row.EventType == "win-streak");

        foreach (var invalid in new[]
        {
            blocked with { DisplaySeconds = 4 }, blocked with { LobbyDelaySeconds = 121 },
            blocked with { IntervalSeconds = 2 }, blocked with { WinStreakThreshold = 1 },
            blocked with { StreakEndedThreshold = 101 }, blocked with { MinimumTierIndex = 5 },
        })
            Assert.Throws<L12OperationsConfigException>(() => reloaded.UpdateRankedConfig(admin,
                reloaded.RankedConfig(admin) with { Broadcast = invalid }, "非法广播范围",
                new L12AdminAuditContext("ranked-broadcast-invalid")));
    }

    private static L12RankedMasterTitleMatchFact[] TitleFacts(string prefix, string accountId,
        string masterId, string opponentMasterId, DateTimeOffset now, int count = 20)
        => Enumerable.Range(0, count).Select(index => new L12RankedMasterTitleMatchFact(
            $"{prefix}-{index}", accountId, $"{prefix}-opponent-{index % 10}", masterId,
            opponentMasterId, 0, 6, now.AddDays(-(index % 5)).AddMinutes(-index - 1),
            "normal", true)).ToArray();
}
