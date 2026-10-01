using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class SeasonActivationTests
{
    [Fact]
    public void ActivationArchivesOnceAndResetsVisibleRankWhilePreservingArchiveAndHiddenRating()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("赛季承接甲", "Password123!").Account!;
            var second = store.Register("赛季承接乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            for (var index = 0; index < 6; index++)
                store.SettleRankedMatch($"carry-{index}", first.Id, second.Id, index == 5 ? 1 : 0);

            var before = store.RankedProfile(first.Id);
            var hidden = store.HiddenRating(first.Id);
            var catalogBefore = store.SeasonCatalog(admin);
            var draft = catalogBefore.Next!;
            var ranked = draft.Configuration.Ranked with
            {
                Factions = draft.Configuration.Ranked.Factions.Select(faction => faction with
                {
                    Tiers = faction.Tiers.Select((tier, index) => index == 0
                        ? tier with { BaseDelta = tier.BaseDelta + 17 }
                        : tier).ToArray(),
                }).ToArray(),
            };
            var updated = store.UpdateSeasonDraft(admin, draft.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", DateTimeOffset.UtcNow.AddDays(30),
                    DateTimeOffset.UtcNow.AddDays(120), draft.Configuration with { Ranked = ranked }),
                draft.Revision, "prepare S02", Context("prepare", draft.Revision));

            var result = store.ActivateSeason(admin, updated.DefinitionId,
                catalogBefore.Current.Revision, updated.Revision, "activate S02",
                new L12RankedSeasonCutoverReadiness(catalogBefore.Current.SeasonId, 0, 0, 0, 0),
                Context("activate", store.OperationsConfig(admin).Version));

            Assert.True(result.Activated);
            Assert.Equal("S02", result.Current.SeasonId);
            Assert.Equal(catalogBefore.Current.SeasonId, result.Archive.SeasonId);
            Assert.Null(store.SeasonCatalog(admin).Next);
            Assert.Single(store.SeasonArchives(admin));
            Assert.Null(store.RankedConfig(admin).PendingGradient);
            Assert.Equal(ranked.Factions[0].Tiers[0].BaseDelta,
                store.RankedConfig(admin).Factions[0].Tiers[0].BaseDelta);

            var after = store.RankedProfile(first.Id);
            Assert.Equal("S02", after.SeasonId);
            Assert.Equal(before.Faction, after.Faction);
            Assert.Equal(0, after.SevenValue);
            Assert.Equal(hidden, store.HiddenRating(first.Id));
            Assert.Equal(0, after.PlacementPlayed);
            Assert.Equal(0, after.PlacementWins);
            Assert.Equal(0, after.Wins);
            Assert.Equal(0, after.Losses);
            Assert.Equal(0, after.WinStreak);
            Assert.Equal(0, after.LossStreak);
            Assert.Null(after.SelectedMasterTitle);
            _ = store.SettleRankedMatch("carry-0", first.Id, second.Id, 0,
                seasonId: catalogBefore.Current.SeasonId);
            Assert.Equal(0, store.RankedProfile(first.Id).Wins);
            Assert.Throws<InvalidOperationException>(() => store.SettleRankedMatch("late-old-season",
                first.Id, second.Id, 0, seasonId: catalogBefore.Current.SeasonId));
            Assert.Equal(0, store.RankedProfile(first.Id).Wins);
            var history = Assert.Single(store.RankedOverview(first.Id).History);
            Assert.Equal(catalogBefore.Current.SeasonId, history.SeasonId);
            Assert.Equal(before.SevenValue, history.SevenValue);
            Assert.Equal(before.PlacementPlayed, history.PlacementPlayed);
            Assert.Equal(before.Wins, history.Wins);
            Assert.Equal(before.Losses, history.Losses);

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            Assert.Equal("S02", reopened.SeasonCatalog(reopenedAdmin).Current.SeasonId);
            Assert.Single(reopened.SeasonArchives(reopenedAdmin));
            Assert.Equal(0, reopened.RankedProfile(first.Id).SevenValue);
            Assert.Equal(0, reopened.RankedProfile(first.Id).PlacementPlayed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairIsGuardedAuditedPersistentAndPreservesPriorSeasonFacts()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var first = initial.Register("修复承接甲", "Password123!").Account!;
            var second = initial.Register("修复承接乙", "Password123!").Account!;
            initial.SelectRankedFaction(first.Id, "order");
            initial.SelectRankedFaction(second.Id, "chaos");
            for (var index = 0; index < 6; index++)
                initial.SettleRankedMatch($"repair-prior-{index}", first.Id, second.Id, 0);
            var priorProfile = initial.RankedProfile(first.Id);
            var hidden = initial.HiddenRating(first.Id);
            ActivateNextSeason(initial, initialAdmin, "T01", "T01 赛季");
            var priorHistory = Assert.Single(initial.RankedOverview(first.Id).History);
            Assert.Equal(priorProfile.SevenValue, priorHistory.SevenValue);

            RewriteSnapshot(path, data =>
            {
                foreach (var profile in data["RankedProfiles"]!.AsArray().Select(node => node!.AsObject())
                             .Where(profile => profile["SeasonId"]!.GetValue<string>() == "T01"))
                {
                    profile["SevenValue"] = 888;
                    profile["PlacementPlayed"] = 5;
                    profile["PlacementWins"] = 0;
                    profile["Wins"] = 0;
                    profile["Losses"] = 0;
                    profile["WinStreak"] = 4;
                    profile["LossStreak"] = 3;
                    profile["HighestFloor"] = 888;
                    profile["ReachedHighestTier"] = true;
                    profile["SelectedMasterTitle"] = "旧赛季称号";
                }
            });

            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var operationsVersion = store.OperationsConfig(admin).Version;
            var auditBefore = store.AdminAudit("ranked").Count;
            var result = RepairWithPreview(store, admin, "修复 T01 切季承接污染",
                operationsVersion, Context("repair-t01", operationsVersion));

            Assert.False(result.Replayed);
            Assert.Equal(2, result.ProfilesReset);
            Assert.Equal(2, result.NonzeroSevenValueProfiles);
            Assert.Equal(2, result.NonzeroPlacementProfiles);
            Assert.Equal(0, result.RankedProfilesWithMatchStats);
            var repaired = store.RankedProfile(first.Id);
            Assert.Equal("T01", repaired.SeasonId);
            Assert.Equal(priorProfile.Faction, repaired.Faction);
            Assert.Equal(0, repaired.SevenValue);
            Assert.Equal(0, repaired.PlacementPlayed);
            Assert.Equal(0, repaired.PlacementWins);
            Assert.Equal(0, repaired.Wins);
            Assert.Equal(0, repaired.Losses);
            Assert.Equal(0, repaired.WinStreak);
            Assert.Equal(0, repaired.LossStreak);
            Assert.Null(repaired.SelectedMasterTitle);
            Assert.Equal(hidden, store.HiddenRating(first.Id));
            Assert.Equal(priorHistory, Assert.Single(store.RankedOverview(first.Id).History));
            var audit = Assert.Single(store.AdminAudit("ranked").Skip(auditBefore));
            Assert.Equal("season-reset-repair", audit.Action);
            Assert.Contains("profiles=2", audit.FromValue);
            Assert.Contains("matches=0", audit.FromValue);
            Assert.Contains("seven=0;placement=0;match-stats=0", audit.ToValue);

            var replay = store.RepairT01RankedSeasonReset(admin, "t01", "重试修复",
                operationsVersion, new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0),
                Context("repair-t01-replay", operationsVersion));
            Assert.True(replay.Replayed);
            Assert.Equal(result.AppliedAt, replay.AppliedAt);
            Assert.Equal(auditBefore + 1, store.AdminAudit("ranked").Count);

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            var persistedReplay = reopened.RepairT01RankedSeasonReset(reopenedAdmin, "T01", "重启后重试",
                operationsVersion, new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0),
                Context("repair-t01-reopen", operationsVersion));
            Assert.True(persistedReplay.Replayed);
            Assert.Equal(0, reopened.RankedProfile(first.Id).SevenValue);
            Assert.Equal(priorHistory, Assert.Single(reopened.RankedOverview(first.Id).History));

            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var persistedProfile = persisted["RankedProfiles"]!.AsArray()
                .Select(node => node!.AsObject()).Single(profile =>
                    profile["AccountId"]!.GetValue<string>() == first.Id);
            Assert.Equal(0, persistedProfile["HighestFloor"]!.GetValue<int>());
            Assert.False(persistedProfile["ReachedHighestTier"]!.GetValue<bool>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void T01ResetRepairRejectsEveryNonReadyCutoverDimension(int active, int pending,
        int reconciliation, int quarantined)
    {
        var root = TempRoot();
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            ActivateNextSeason(store, admin, "T01", "T01 赛季");
            var version = store.OperationsConfig(admin).Version;

            var error = Assert.Throws<L12OperationsConfigException>(() =>
                store.RepairT01RankedSeasonReset(admin, "T01", "在途门禁", version,
                    new L12RankedSeasonCutoverReadiness("T01", active, pending, reconciliation,
                        quarantined), Context("repair-not-ready", version)));

            Assert.Equal("ranked_season_reset_repair_not_ready", error.Code);
            Assert.Empty(store.AdminAudit("ranked").Where(item => item.Action == "season-reset-repair"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairWaivesBoundedTransitionMatchesAndRestoresHiddenRatingBaseline()
    {
        var root = TempRoot();
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("新季对局甲", "Password123!").Account!;
            var second = store.Register("新季对局乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            ActivateNextSeason(store, admin, "T01", "T01 赛季");
            store.SettleRankedMatch("t01-played", first.Id, second.Id, 0, seasonId: "T01");
            var hiddenBefore = 1500d;
            Assert.NotEqual(hiddenBefore, store.HiddenRating(first.Id));
            var version = store.OperationsConfig(admin).Version;

            var result = RepairWithPreview(store, admin, "放弃过渡期对局", version,
                Context("repair-played", version));

            Assert.Equal(1, result.TransitionMatchesWaived);
            Assert.Equal("voided", store.RankedSettlement("t01-played", first.Id)!.RewardStatus);
            Assert.Contains("t01-played", store.RankedIntegrityExcludedMatchIds());
            Assert.Equal(hiddenBefore, store.HiddenRating(first.Id));
            Assert.Equal(0, store.RankedProfile(first.Id).PlacementPlayed);
            Assert.Equal(0, store.RankedProfile(first.Id).Wins);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void T01ResetRepairRejectsSeasonSettlementLedgerEvenWhenDerivedFactsAreMissing(
        bool legacyUntaggedSettlement)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var first = initial.Register("结算账本甲", "Password123!").Account!;
            var second = initial.Register("结算账本乙", "Password123!").Account!;
            initial.SelectRankedFaction(first.Id, "order");
            initial.SelectRankedFaction(second.Id, "chaos");
            ActivateNextSeason(initial, initialAdmin, "T01", "T01 赛季");
            initial.SettleRankedMatch("t01-ledger-only", first.Id, second.Id, 0, seasonId: "T01");
            RewriteSnapshot(path, data =>
            {
                data["RankedIntegrityAudits"] = new JsonArray();
                data["RankedSettlementProfileFacts"] = new JsonArray();
                data["RankedMasterRecords"] = new JsonArray();
                if (legacyUntaggedSettlement)
                    foreach (var settlement in data["RankedSettlements"]!.AsArray()
                                 .Select(node => node!.AsObject()))
                        settlement.Remove("SeasonId");
                foreach (var profile in data["RankedProfiles"]!.AsArray()
                             .Select(node => node!.AsObject()))
                {
                    profile["SevenValue"] = 0;
                    profile["PlacementPlayed"] = 0;
                    profile["PlacementWins"] = 0;
                    profile["Wins"] = 0;
                    profile["Losses"] = 0;
                    profile["WinStreak"] = 0;
                    profile["LossStreak"] = 0;
                }
            });
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var version = store.OperationsConfig(admin).Version;

            var error = Assert.Throws<L12OperationsConfigException>(() =>
                store.RepairT01RankedSeasonReset(admin, "T01", "已有结算账本", version,
                    new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0),
                    Context("repair-ledger-only", version)));

            Assert.Equal("ranked_season_reset_repair_orphan_fact", error.Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairMarkerWinsOverLaterMatchesWithoutResettingThemAgain()
    {
        var root = TempRoot();
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("幂等重试甲", "Password123!").Account!;
            var second = store.Register("幂等重试乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            ActivateNextSeason(store, admin, "T01", "T01 赛季");
            var version = store.OperationsConfig(admin).Version;
            var firstRepair = RepairWithPreview(store, admin, "首次修复", version,
                Context("repair-before-play", version));
            Assert.False(firstRepair.Replayed);
            store.SettleRankedMatch("after-t01-repair", first.Id, second.Id, 0, seasonId: "T01");
            var afterMatch = store.RankedProfile(first.Id);

            var replay = store.RepairT01RankedSeasonReset(admin, "T01", "不同幂等键重试", version,
                new L12RankedSeasonCutoverReadiness("T01", 1, 1, 1, 1),
                Context("repair-after-play", version));

            Assert.True(replay.Replayed);
            Assert.Equal(firstRepair.AppliedAt, replay.AppliedAt);
            Assert.Equal(afterMatch, store.RankedProfile(first.Id));
            Assert.Single(store.AdminAudit("ranked").Where(item => item.Action == "season-reset-repair"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairStorageFailureRollsBackProfilesMarkerAndAudit()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var player = initial.Register("修复回滚玩家", "Password123!").Account!;
            initial.SelectRankedFaction(player.Id, "order");
            ActivateNextSeason(initial, initialAdmin, "T01", "T01 赛季");
            RewriteSnapshot(path, data =>
            {
                var profile = data["RankedProfiles"]!.AsArray().Select(node => node!.AsObject())
                    .Single(row => row["AccountId"]!.GetValue<string>() == player.Id);
                profile["SevenValue"] = 456;
                profile["PlacementPlayed"] = 5;
            });
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var version = store.OperationsConfig(admin).Version;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("injected repair commit failure");
            };

            var preview = store.PreviewT01RankedSeasonReset(admin, "T01", version,
                new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0), DateTimeOffset.UtcNow);
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.RepairT01RankedSeasonReset(admin, "T01", "注入存储失败", version,
                    new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0),
                    Context("repair-storage-failure", version), preview.EvidenceFingerprint));
            store.StorageFailureInjector = null;

            Assert.Equal(456, store.RankedProfile(player.Id).SevenValue);
            Assert.Empty(store.AdminAudit("ranked").Where(item => item.Action == "season-reset-repair"));
            var retry = RepairWithPreview(store, admin, "失败后重试", version,
                Context("repair-storage-retry", version));
            Assert.False(retry.Replayed);
            Assert.Equal(0, store.RankedProfile(player.Id).SevenValue);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task T01ResetRepairApiUsesRankedCutoverGateAndCommandIdempotency()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var player = initial.Register("修复接口玩家", "Password123!").Account!;
            initial.SelectRankedFaction(player.Id, "order");
            ActivateNextSeason(initial, initialAdmin, "T01", "T01 赛季");
            RewriteSnapshot(path, data =>
            {
                var profile = data["RankedProfiles"]!.AsArray().Select(node => node!.AsObject())
                    .Single(row => row["AccountId"]!.GetValue<string>() == player.Id);
                profile["SevenValue"] = 321;
                profile["PlacementPlayed"] = 5;
            });

            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var login = store.Login("Admin", "L12master");
            var operationsVersion = store.OperationsConfig(login.Account!).Version;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder,
                store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var scheduledStart = DateTimeOffset.UtcNow.AddMinutes(10);
            using var previewRequest = Authorized(HttpMethod.Post,
                "/api/admin/ranked/season-reset-repair/preview", login.Token!,
                new RankedSeasonResetRepairPreviewRequest("T01", operationsVersion,
                    scheduledStart));
            using var previewResponse = await client.SendAsync(previewRequest);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<
                L12RankedSeasonResetRepairPreviewView>())!;
            Assert.Equal(scheduledStart, preview.CompetitiveStartAt);
            var body = new RankedSeasonResetRepairRequest("T01", "修复线上 T01 承接污染",
                "repair-t01-api-1", operationsVersion, preview.EvidenceFingerprint,
                scheduledStart);

            using var request = Authorized(HttpMethod.Post, "/api/admin/ranked/season-reset-repair",
                login.Token!, body);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var repaired = (await response.Content.ReadFromJsonAsync<L12RankedSeasonResetRepairView>())!;
            Assert.False(repaired.Replayed);
            Assert.Equal(scheduledStart, repaired.CompetitiveStartAt);
            Assert.Equal(scheduledStart, store.OperationsConfig(login.Account!).Config.Season.StartsAt);
            Assert.Equal(0, store.RankedProfile(player.Id).SevenValue);

            using var replayRequest = Authorized(HttpMethod.Post,
                "/api/admin/ranked/season-reset-repair", login.Token!, body);
            using var replayResponse = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            Assert.Equal("true", replayResponse.Headers.GetValues("X-Idempotent-Replay").Single());
            Assert.Single(store.AdminCommands(type:
                "operations.config.ranked-season-reset-repair"));
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FinalizationFreezesRanksAndOnlyHighestTierReceivesOverallRank()
    {
        var root = TempRoot();
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var winner = store.Register("最终名次甲", "Password123!").Account!;
            var loser = store.Register("最终名次乙", "Password123!").Account!;
            store.SelectRankedFaction(winner.Id, "order");
            store.SelectRankedFaction(loser.Id, "chaos");
            var config = store.RankedConfig(admin);
            var compact = config with
            {
                PlacementMatches = 1,
                PlacementMaximum = 10,
                Factions = config.Factions.Select(faction => faction with
                {
                    Tiers = faction.Tiers.Select((tier, index) => tier with
                        { Minimum = index == 0 ? 0 : index == 1 ? 11 : index * 10 }).ToArray(),
                }).ToArray(),
            };
            store.UpdateRankedConfig(admin, compact, "test final ranking",
                Context("compact-ranking"));
            store.SettleRankedMatch("final-rank-1", winner.Id, loser.Id, 0);
            store.SettleRankedMatch("final-rank-2", winner.Id, loser.Id, 0);
            var frozenWinner = store.RankedProfile(winner.Id);
            var frozenLoser = store.RankedProfile(loser.Id);

            ActivateNextSeason(store, admin, "S02-rank", "排名冻结赛季");

            var winnerHistory = Assert.Single(store.RankedOverview(winner.Id).History);
            Assert.True(winnerHistory.Placed);
            Assert.Equal(frozenWinner.Tier, winnerHistory.RankLabel);
            Assert.Equal(1, winnerHistory.FactionRank);
            Assert.Equal(1, winnerHistory.OverallRank);
            Assert.Equal(frozenWinner.SevenValue, winnerHistory.SevenValue);
            Assert.Equal(100d, winnerHistory.WinRate);

            var loserHistory = Assert.Single(store.RankedOverview(loser.Id).History);
            Assert.True(loserHistory.Placed);
            Assert.Equal(frozenLoser.Tier, loserHistory.RankLabel);
            Assert.Equal(1, loserHistory.FactionRank);
            Assert.Null(loserHistory.OverallRank);
            Assert.Equal(0d, loserHistory.WinRate);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DrawParticipantsReceivePersistentAccountIsolatedSummaryButFactionOnlyPlayerDoesNot()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("平局参与甲", "Password123!").Account!;
            var second = store.Register("平局参与乙", "Password123!").Account!;
            var idle = store.Register("仅选派系者", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            store.SelectRankedFaction(idle.Id, "fate");
            store.SettleRankedDrawMatch("draw-only-final", first.Id, second.Id);
            Assert.Empty(store.RankedOverview(first.Id).History);

            ActivateNextSeason(store, admin, "S02-draw", "平局总结赛季");

            var history = Assert.Single(store.RankedOverview(first.Id).History);
            Assert.False(history.Placed);
            Assert.Null(history.FactionRank);
            Assert.Null(history.OverallRank);
            Assert.Equal("定级 0/5", history.RankLabel);
            Assert.Empty(store.RankedOverview(idle.Id).History);
            Assert.Empty(store.PendingSeasonSummaryNotifications(idle.Id));

            var summary = Assert.Single(store.PendingSeasonSummaryNotifications(first.Id));
            Assert.Equal(history.SeasonId, summary.SeasonId);
            Assert.False(summary.Placed);
            Assert.Equal("定级 0/5", summary.RankLabel);
            Assert.Throws<KeyNotFoundException>(() =>
                store.AcknowledgeSeasonSummaryNotification(second.Id, summary.Id));
            store.AcknowledgeSeasonSummaryNotification(first.Id, summary.Id);
            store.AcknowledgeSeasonSummaryNotification(first.Id, summary.Id);
            Assert.Empty(store.PendingSeasonSummaryNotifications(first.Id));

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.Empty(reopened.PendingSeasonSummaryNotifications(first.Id));
            Assert.Single(reopened.PendingSeasonSummaryNotifications(second.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacyFinalHistoryKeepsUnknownRanksAndDoesNotCreateUnreadSummary()
    {
        var root = TempRoot();
        var legacyRoot = TempRoot();
        try
        {
            var catalog = Catalog();
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("旧历史记录甲", "Password123!").Account!;
            var second = store.Register("旧历史记录乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            store.SettleRankedMatch("legacy-history-1", first.Id, second.Id, 0);
            ActivateNextSeason(store, admin, "S02-legacy", "旧历史迁移赛季");

            var snapshot = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var history = snapshot["RankedProfileHistory"]!.AsArray();
            foreach (var node in history)
            {
                var row = node!.AsObject();
                foreach (var property in new[] { "FactionRank", "OverallRank", "Placed",
                             "PlacementRequired", "RankLabel", "WinRate",
                             "SummaryAvailableAt", "SummarySeenAt" })
                    row.Remove(property);
            }
            var legacyPath = Path.Combine(legacyRoot, "platform.json");
            File.WriteAllText(legacyPath, snapshot.ToJsonString(new JsonSerializerOptions
                { WriteIndented = true }));

            var reopened = new L12PlatformStore(legacyPath, catalog.PresetDecks,
                officialCards: catalog.Cards);
            var legacy = Assert.Single(reopened.RankedOverview(first.Id).History);
            Assert.Null(legacy.FactionRank);
            Assert.Null(legacy.OverallRank);
            Assert.Null(legacy.Placed);
            Assert.Null(legacy.PlacementRequired);
            Assert.Null(legacy.RankLabel);
            Assert.Empty(reopened.PendingSeasonSummaryNotifications(first.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
            Directory.Delete(legacyRoot, true);
        }
    }

    [Fact]
    public void FactionChangeSnapshotDoesNotEnterPlayerSeasonHistory()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var first = store.Register("换派系历史甲", "Password123!").Account!;
            var second = store.Register("换派系历史乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            store.SettleRankedMatch("faction-change-history", first.Id, second.Id, 0);
            store.SelectRankedFaction(first.Id, "fate");

            Assert.Empty(store.RankedOverview(first.Id).History);
            Assert.Empty(store.PendingSeasonSummaryNotifications(first.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ActivationRejectsUndrainedSeasonAndRollsBackPersistenceFailure()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var seasons = store.SeasonCatalog(admin);
            var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", null, null,
                    seasons.Next.Configuration), seasons.Next.Revision, "prepare",
                Context("prepare", seasons.Next.Revision));

            var blocked = Assert.Throws<L12OperationsConfigException>(() => store.ActivateSeason(admin,
                draft.DefinitionId, seasons.Current.Revision, draft.Revision, "blocked",
                new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 1, 0, 0, 0),
                Context("blocked")));
            Assert.Equal("season_cutover_not_ready", blocked.Code);
            Assert.Empty(store.SeasonArchives(admin));

            store.StorageFailureInjector = phase =>
            {
                if (phase == "before-commit") throw new IOException("injected season commit failure");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ActivateSeason(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, "atomic failure",
                new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 0, 0, 0, 0),
                Context("failure")));
            store.StorageFailureInjector = null;

            Assert.Equal(seasons.Current.SeasonId, store.SeasonCatalog(admin).Current.SeasonId);
            Assert.NotNull(store.SeasonCatalog(admin).Next);
            Assert.Empty(store.SeasonArchives(admin));
            Assert.Equal(seasons.Current.SeasonId, store.OperationsConfig(admin).Config.Season.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenericOperationsApplyCannotBypassSeasonActivation()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);
            var error = Assert.Throws<L12OperationsConfigException>(() => store.ApplyOperationsConfig(admin,
                current.Config with { Season = current.Config.Season with { Id = "S-bypass" } },
                current.Version, "bypass", Context("bypass")));
            Assert.Equal("season_activation_required", error.Code);
            Assert.Equal(current.Config.Season.Id, store.OperationsConfig(admin).Config.Season.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EquivalentSeasonIdVariantKeepsCanonicalIdentityAndPlayerProgress()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("赛季标识甲", "Password123!").Account!;
            var second = store.Register("赛季标识乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            for (var index = 0; index < 6; index++)
                store.SettleRankedMatch($"identity-{index}", first.Id, second.Id, 0);
            var before = store.RankedProfile(first.Id);
            var current = store.OperationsConfig(admin);

            store.ApplyOperationsConfig(admin, current.Config with
                {
                    Season = current.Config.Season with { Id = "  ｓ０１  " },
                },
                current.Version, "normalize equivalent identity", Context("identity"));

            var after = store.RankedProfile(first.Id);
            Assert.Equal(current.Config.Season.Id, store.OperationsConfig(admin).Config.Season.Id);
            Assert.Equal(current.Config.Season.Id, after.SeasonId);
            Assert.Equal(before.SevenValue, after.SevenValue);
            Assert.Equal(before.PlacementPlayed, after.PlacementPlayed);
            Assert.Equal(before.Wins, after.Wins);
            Assert.Equal(before.WinStreak, after.WinStreak);
            Assert.Empty(store.RankedOverview(first.Id).History);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ActivationApiIsHighRiskIdempotentAndRequiresOperationsPreconditions()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var login = store.Login("Admin", "L12master");
            var seasons = store.SeasonCatalog(login.Account!);
            var draft = store.UpdateSeasonDraft(login.Account!, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S-api-next", "API 新赛季", null, null,
                    seasons.Next.Configuration), seasons.Next.Revision, "prepare api season",
                Context("prepare-api"));
            var operationsVersion = store.OperationsConfig(login.Account!).Version;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var body = new SeasonActivationRequest(seasons.Current.Revision, draft.Revision,
                "activate from api", ExpectedVersion: operationsVersion);

            using (var missingKey = Authorized(HttpMethod.Post,
                       $"/api/admin/seasons/draft/{draft.DefinitionId}/activate", login.Token!, body))
            using (var response = await client.SendAsync(missingKey))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var authorizedBody = body with { IdempotencyKey = "season-activate-api-1" };
            using var firstRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/activate", login.Token!, authorizedBody);
            using var firstResponse = await client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var first = (await firstResponse.Content.ReadFromJsonAsync<L12SeasonActivationView>())!;
            Assert.True(first.Activated);
            Assert.False(firstResponse.Headers.Contains("X-Idempotent-Replay"));

            using var replayRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/activate", login.Token!, authorizedBody);
            using var replayResponse = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            var replay = (await replayResponse.Content.ReadFromJsonAsync<L12SeasonActivationView>())!;
            Assert.True(replay.Activated);
            Assert.Equal("true", replayResponse.Headers.GetValues("X-Idempotent-Replay").Single());
            Assert.Single(store.SeasonArchives(login.Account!));
            Assert.Equal("S-api-next", store.SeasonCatalog(login.Account!).Current.SeasonId);
            Assert.Contains(store.AdminCommands(type: "operations.config.season-activate"),
                command => command.Risk == "high" && command.Status == "executed");
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RankedLobbyPinnedToArchivedSeasonCannotStartAfterCutover()
    {
        var root = TempRoot();
        await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("旧赛季房间甲", "Password123!").Account!;
            var second = store.Register("旧赛季房间乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, store);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            manager.Connect(firstSession, first.Id, first.Username);
            manager.Connect(secondSession, second.Id, second.Username);
            var created = JsonSerializer.SerializeToElement(Assert.Single(manager.CreateRoom(firstSession,
                new L12RoomOptions
                {
                    MatchModeId = "ranked",
                    DisasterMode = "season",
                    UseCardRestrictions = true,
                })).Payload);
            var roomCode = created.GetProperty("roomCode").GetString()!;
            manager.JoinRoom(secondSession, roomCode);
            // Matchmaking normally starts immediately. Model its pinned pre-start boundary directly so
            // the test can hold the room across the administrative cutover.
            var rooms = typeof(L12RoomManager).GetField("_rooms",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(manager)!;
            var arguments = new object?[] { roomCode, null };
            Assert.True((bool)rooms.GetType().GetMethod("TryGetValue")!.Invoke(rooms, arguments)!);
            var room = arguments[1]!;
            room.GetType().GetProperty("Options")!.SetValue(room, new L12RoomOptions
            {
                MatchModeId = "ranked",
                DisasterMode = "season",
                UseCardRestrictions = true,
            });

            var seasons = store.SeasonCatalog(admin);
            var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", null, null,
                    seasons.Next.Configuration), seasons.Next.Revision, "prepare",
                Context("prepare-old-lobby"));
            store.ActivateSeason(admin, draft.DefinitionId, seasons.Current.Revision, draft.Revision,
                "activate", new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 0, 0, 0, 0),
                Context("activate-old-lobby"));

            await manager.SetReadyAsync(firstSession, true);
            var blockedPayloads = (await manager.SetReadyAsync(secondSession, true))
                .Select(message => JsonSerializer.SerializeToElement(message.Payload)).ToArray();
            var blocked = blockedPayloads.FirstOrDefault(payload =>
                payload.GetProperty("type").GetString() == "roomSeasonExpired");
            Assert.True(blocked.ValueKind != JsonValueKind.Undefined,
                string.Join(Environment.NewLine, blockedPayloads.Select(payload => payload.ToString())));
            Assert.Contains("所属赛季已结束", blocked.GetProperty("message").GetString());
            Assert.Equal(0, manager.RuntimeStats().ActiveGameCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CommandBusLateValidationFailureRollsBackEverySeasonBusinessProjection()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var first = initial.Register("晚失败甲", "Password123!").Account!;
            var second = initial.Register("晚失败乙", "Password123!").Account!;
            initial.SelectRankedFaction(first.Id, "order");
            initial.SelectRankedFaction(second.Id, "chaos");
            for (var index = 0; index < 6; index++)
                initial.SettleRankedMatch($"late-validation-{index}", first.Id, second.Id, 0);
            var seasons = initial.SeasonCatalog(initialAdmin);
            var restrictedCard = catalog.Cards.Keys.First(cardId =>
                !seasons.Next!.Configuration.CardRestrictions.Any(item =>
                    item.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)));
            initial.UpdateSeasonDraft(initialAdmin, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", null, null,
                    seasons.Next.Configuration with
                    {
                        CardRestrictions = seasons.Next.Configuration.CardRestrictions
                            .Append(new L12CardRestrictionConfig(restrictedCard, 0, "late validation"))
                            .ToArray(),
                    }), seasons.Next.Revision, "prepare late validation", Context("prepare-late"));

            var reducedCards = catalog.Cards.Where(pair => !pair.Key.Equals(restrictedCard,
                    StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: reducedCards);
            var admin = store.Login("Admin", "L12master").Account!;
            var before = CaptureSeasonBusinessState(store, admin, first.Id);
            var operationsAuditBefore = store.AdminAudit("operations").Count;

            var outcome = ActivateThroughBus(store, admin, "late-validation-command");

            Assert.False(outcome.Success);
            Assert.Equal("unknown_restricted_card", outcome.Code);
            AssertSeasonBusinessState(before, store, admin, first.Id);
            Assert.Equal(operationsAuditBefore, store.AdminAudit("operations").Count);
            AssertMirrorAndDatabaseState(before, path, catalog.PresetDecks, reducedCards, first.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CommandBusReconciliationFailureUsesNestedSavepoint()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = PrepareValidActivation(path, catalog, out var admin, out var playerId);
            var before = CaptureSeasonBusinessState(store, admin, playerId);
            var operationsAuditBefore = store.AdminAudit("operations").Count;
            store.SeasonActivationFailureInjector = stage =>
            {
                if (stage == "after-season-profile-carry")
                    throw new InvalidOperationException("injected reconciliation failure");
            };

            var outcome = ActivateThroughBus(store, admin, "late-reconciliation-command");
            store.SeasonActivationFailureInjector = null;

            Assert.False(outcome.Success);
            Assert.Equal("command_failed", outcome.Code);
            AssertSeasonBusinessState(before, store, admin, playerId);
            Assert.Equal(operationsAuditBefore, store.AdminAudit("operations").Count);
            AssertMirrorAndDatabaseState(before, path, catalog.PresetDecks, catalog.Cards, playerId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("before-mirror-serialize")]
    [InlineData("before-audit-append")]
    [InlineData("after-audit-append")]
    [InlineData("before-commit")]
    public void StorageLateFailureRollsBackSeasonMemoryDatabaseMirrorAndAudit(string failureStage)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = PrepareValidActivation(path, catalog, out var admin, out var playerId);
            var before = CaptureSeasonBusinessState(store, admin, playerId);
            var mirrorBefore = File.ReadAllText(path);
            var auditBefore = store.AdminAudit().Count;
            store.StorageFailureInjector = stage =>
            {
                if (stage == failureStage) throw new IOException($"injected {failureStage}");
            };

            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                ActivateThroughBus(store, admin, $"storage-{failureStage}"));
            store.StorageFailureInjector = null;

            AssertSeasonBusinessState(before, store, admin, playerId);
            Assert.Equal(auditBefore, store.AdminAudit().Count);
            Assert.Equal(mirrorBefore, File.ReadAllText(path));
            AssertMirrorAndDatabaseState(before, path, catalog.PresetDecks, catalog.Cards, playerId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private sealed record SeasonBusinessState(
        string CurrentSeasonId,
        string? NextDefinitionId,
        long CurrentRevision,
        long? NextRevision,
        int ArchiveCount,
        int SevenValue,
        int PlacementPlayed,
        int Wins,
        int WinStreak,
        int HistoryCount,
        long OperationsVersion);

    private static SeasonBusinessState CaptureSeasonBusinessState(L12PlatformStore store,
        L12AccountView admin, string playerId)
    {
        var catalog = store.SeasonCatalog(admin);
        var profile = store.RankedProfile(playerId);
        return new SeasonBusinessState(catalog.Current.SeasonId, catalog.Next?.DefinitionId,
            catalog.Current.Revision, catalog.Next?.Revision, catalog.Archives.Count,
            profile.SevenValue, profile.PlacementPlayed, profile.Wins, profile.WinStreak,
            store.RankedOverview(playerId).History.Count, store.OperationsConfig(admin).Version);
    }

    private static void AssertSeasonBusinessState(SeasonBusinessState expected, L12PlatformStore store,
        L12AccountView admin, string playerId)
        => Assert.Equal(expected, CaptureSeasonBusinessState(store, admin, playerId));

    private static void AssertMirrorAndDatabaseState(SeasonBusinessState expected, string path,
        IReadOnlyList<L12PresetDeckDefinition> decks,
        IReadOnlyDictionary<string, L12CardDefinition> cards, string playerId)
    {
        var database = new L12PlatformStore(path, decks, officialCards: cards);
        var databaseAdmin = database.Login("Admin", "L12master").Account!;
        AssertSeasonBusinessState(expected, database, databaseAdmin, playerId);

        var mirrorPath = Path.Combine(Path.GetDirectoryName(path)!, $"mirror-{Guid.NewGuid():N}.json");
        File.Copy(path, mirrorPath);
        var mirror = new L12PlatformStore(mirrorPath, decks, officialCards: cards);
        var mirrorAdmin = mirror.Login("Admin", "L12master").Account!;
        AssertSeasonBusinessState(expected, mirror, mirrorAdmin, playerId);
    }

    private static L12PlatformStore PrepareValidActivation(string path, L12Catalog catalog,
        out L12AccountView admin, out string playerId)
    {
        var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register(("txn" + Guid.NewGuid().ToString("N"))[..11], "Password123!").Account!;
        var second = store.Register(("rvl" + Guid.NewGuid().ToString("N"))[..11], "Password123!").Account!;
        playerId = first.Id;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        for (var index = 0; index < 6; index++)
            store.SettleRankedMatch($"txn-{Guid.NewGuid():N}", first.Id, second.Id, 0);
        var seasons = store.SeasonCatalog(admin);
        store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
            new L12SeasonDefinitionDraft("S02", "第二赛季", null, null, seasons.Next.Configuration),
            seasons.Next.Revision, "prepare transaction", Context("prepare-transaction"));
        return store;
    }

    private static void ActivateNextSeason(L12PlatformStore store, L12AccountView admin,
        string seasonId, string seasonName, DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null)
    {
        var seasons = store.SeasonCatalog(admin);
        var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
            new L12SeasonDefinitionDraft(seasonId, seasonName, startsAt, endsAt,
                seasons.Next.Configuration), seasons.Next.Revision, "prepare summary test",
            Context("prepare-summary"));
        var current = store.SeasonCatalog(admin).Current;
        store.ActivateSeason(admin, draft.DefinitionId, current.Revision, draft.Revision,
            "activate summary test",
            new L12RankedSeasonCutoverReadiness(current.SeasonId, 0, 0, 0, 0),
            Context("activate-summary"));
    }

    [Fact]
    public void T01ResetRepairWaivesMultipleLegacyUntaggedMatchesAndMovesOnlyCompetitiveStart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var first = initial.Register("过渡修复甲", "Password123!").Account!;
            var second = initial.Register("过渡修复乙", "Password123!").Account!;
            var third = initial.Register("过渡修复丙", "Password123!").Account!;
            initial.SelectRankedFaction(first.Id, "order");
            initial.SelectRankedFaction(second.Id, "chaos");
            initial.SelectRankedFaction(third.Id, "fate");
            var originalStartsAt = DateTimeOffset.UtcNow.AddMinutes(-30);
            var originalEndsAt = DateTimeOffset.UtcNow.AddDays(30);
            ActivateNextSeason(initial, initialAdmin, "T01", "T01 赛季", originalStartsAt,
                originalEndsAt);
            var originalActivatedAt = initial.SeasonCatalog(initialAdmin).Current.ActivatedAt;
            var priorHistory = initial.RankedOverview(first.Id).History.ToArray();

            RewriteSnapshot(path, data =>
            {
                foreach (var profile in data["RankedProfiles"]!.AsArray().Select(node => node!.AsObject()))
                {
                    profile["SevenValue"] = 1800;
                    profile["PlacementPlayed"] = 5;
                    profile["PlacementWins"] = 2;
                    profile["Wins"] = 3;
                    profile["Losses"] = 2;
                    profile["WinStreak"] = 2;
                    profile["HighestFloor"] = 1000;
                }
            });
            var played = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var playedAdmin = played.Login("Admin", "L12master").Account!;
            var firstHiddenBaseline = played.HiddenRating(first.Id);
            var secondHiddenBaseline = played.HiddenRating(second.Id);
            var thirdHiddenBaseline = played.HiddenRating(third.Id);
            played.SettleRankedMatch("transition-a", first.Id, second.Id, 0,
                "S01-01M1", "S01-02M1", seasonId: "T01");
            played.SettleRankedMatch("transition-b", first.Id, third.Id, 1,
                "S01-01M1", "S01-03M1", seasonId: "T01");
            Assert.NotEqual(firstHiddenBaseline, played.HiddenRating(first.Id));

            RewriteSnapshot(path, data =>
            {
                foreach (var settlement in data["RankedSettlements"]!.AsArray()
                             .Select(node => node!.AsObject())
                             .Where(row => row["MatchId"]!.GetValue<string>().StartsWith("transition-")))
                    settlement.Remove("SeasonId");
                data["RankedBroadcasts"]!.AsArray().Add(new JsonObject
                {
                    ["Id"] = "transition-broadcast",
                    ["MatchId"] = "transition-a",
                    ["EventType"] = "highest-tier",
                    ["Message"] = "过渡期广播",
                    ["CreatedAt"] = DateTimeOffset.UtcNow,
                });
                data["RankedBroadcastDeliveries"]!.AsArray().Add(new JsonObject
                {
                    ["AccountId"] = first.Id,
                    ["BroadcastId"] = "transition-broadcast",
                    ["ClaimToken"] = "transition-claim",
                    ["LeaseExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(1),
                });
                data["AlternateArtGrants"]!.AsArray().Add(new JsonObject
                {
                    ["Id"] = "transition-rank-grant",
                    ["AccountId"] = first.Id,
                    ["AlternateArtId"] = "local-fixture-art",
                    ["SourceKind"] = "rank-reached",
                    ["SourceReference"] = "T01",
                    ["GrantedByAccountId"] = "system",
                    ["GrantedAt"] = DateTimeOffset.UtcNow,
                });
            });
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var version = store.OperationsConfig(admin).Version;
            var observedAt = DateTimeOffset.UtcNow.AddMinutes(1);
            var competitiveStart = observedAt.AddMinutes(30);
            var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
            var afterStart = Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewT01RankedSeasonReset(admin, "T01", version, readiness,
                    observedAt, originalStartsAt.AddMinutes(1)));
            Assert.Equal("ranked_season_reset_repair_after_start_fact", afterStart.Code);
            var preview = store.PreviewT01RankedSeasonReset(admin, "T01", version, readiness,
                observedAt, competitiveStart);

            Assert.Equal(competitiveStart, preview.CompetitiveStartAt);
            Assert.Equal(2, preview.TransitionMatches);
            Assert.Equal(4, preview.SettlementRows);
            Assert.Equal(2, preview.ProfileFacts);
            Assert.Equal(3, preview.MasterRecordsToRemove);
            Assert.Equal(1, preview.BroadcastsToRemove);
            Assert.Equal(1, preview.GrantsToRevoke);
            var changedTarget = Assert.Throws<L12OperationsConfigException>(() =>
                store.RepairT01RankedSeasonReset(admin, "T01", "篡改预定开季时间",
                    version, readiness, Context("repair-transition-target-mismatch", version),
                    preview.EvidenceFingerprint, observedAt, competitiveStart.AddMinutes(1)));
            Assert.Equal("ranked_season_reset_repair_evidence_changed", changedTarget.Code);
            var result = store.RepairT01RankedSeasonReset(admin, "T01", "放弃部署前过渡期",
                version, readiness, Context("repair-transition", version),
                preview.EvidenceFingerprint, observedAt, competitiveStart);

            Assert.Equal(2, result.TransitionMatchesWaived);
            Assert.Equal(version, result.OperationsVersionBefore);
            Assert.Equal(version + 1, result.OperationsVersionAfter);
            Assert.Equal(competitiveStart, result.CompetitiveStartAt);
            Assert.Equal(competitiveStart, store.OperationsConfig(admin).Config.Season.StartsAt);
            var season = store.SeasonCatalog(admin).Current;
            Assert.Equal(competitiveStart, season.StartsAt);
            Assert.Equal(originalActivatedAt, season.ActivatedAt);
            Assert.Equal(originalEndsAt, season.EndsAt);
            Assert.Equal(firstHiddenBaseline, store.HiddenRating(first.Id));
            Assert.Equal(secondHiddenBaseline, store.HiddenRating(second.Id));
            Assert.Equal(thirdHiddenBaseline, store.HiddenRating(third.Id));
            foreach (var accountId in new[] { first.Id, second.Id, third.Id })
            {
                var profile = store.RankedProfile(accountId);
                Assert.Equal(0, profile.SevenValue);
                Assert.Equal(0, profile.PlacementPlayed);
                Assert.Equal(0, profile.Wins);
                Assert.Equal(0, profile.Losses);
            }
            Assert.Equal("voided", store.RankedSettlement("transition-a", first.Id)!.RewardStatus);
            Assert.Equal("voided", store.RankedSettlement("transition-b", third.Id)!.RewardStatus);
            Assert.Contains("transition-a", store.RankedIntegrityExcludedMatchIds());
            Assert.Contains("transition-b", store.RankedIntegrityExcludedMatchIds());
            Assert.Equal(priorHistory, store.RankedOverview(first.Id).History);

            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(4, persisted["RankedSettlements"]!.AsArray().Count(node =>
                node!["MatchId"]!.GetValue<string>().StartsWith("transition-")));
            Assert.Equal(2, persisted["RankedSettlementProfileFacts"]!.AsArray().Count(node =>
                node!["MatchId"]!.GetValue<string>().StartsWith("transition-")));
            Assert.DoesNotContain(persisted["RankedMasterRecords"]!.AsArray(), node =>
                node!["SeasonId"]!.GetValue<string>() == "T01");
            Assert.DoesNotContain(persisted["RankedBroadcasts"]!.AsArray(), node =>
                node!["Id"]!.GetValue<string>() == "transition-broadcast");
            Assert.NotNull(persisted["AlternateArtGrants"]!.AsArray().Single(node =>
                node!["Id"]!.GetValue<string>() == "transition-rank-grant")!["RevokedAt"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairRejectsWhenFactsChangeAfterPreview()
    {
        var root = TempRoot();
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("预览竞态甲", "Password123!").Account!;
            var second = store.Register("预览竞态乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            ActivateNextSeason(store, admin, "T01", "T01 赛季");
            var version = store.OperationsConfig(admin).Version;
            var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
            var preview = store.PreviewT01RankedSeasonReset(admin, "T01", version, readiness,
                DateTimeOffset.UtcNow);
            store.SettleRankedMatch("preview-race", first.Id, second.Id, 0, seasonId: "T01");

            var error = Assert.Throws<L12OperationsConfigException>(() =>
                store.RepairT01RankedSeasonReset(admin, "T01", "旧预览不得自动吸收新局", version,
                    readiness, Context("repair-preview-race", version),
                    preview.EvidenceFingerprint));

            Assert.Equal("ranked_season_reset_repair_evidence_changed", error.Code);
            Assert.Equal(1, store.RankedProfile(first.Id).Wins);
            Assert.Empty(store.AdminAudit("ranked").Where(row => row.Action == "season-reset-repair"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairRejectsAtSeasonEndAndAboveBoundedMatchLimit()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = initial.Login("Admin", "L12master").Account!;
            var first = initial.Register("边界修复甲", "Password123!").Account!;
            var second = initial.Register("边界修复乙", "Password123!").Account!;
            initial.SelectRankedFaction(first.Id, "order");
            initial.SelectRankedFaction(second.Id, "chaos");
            var endsAt = DateTimeOffset.UtcNow.AddHours(2);
            ActivateNextSeason(initial, admin, "T01", "T01 赛季", DateTimeOffset.UtcNow, endsAt);
            var version = initial.OperationsConfig(admin).Version;
            var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
            var ended = Assert.Throws<L12OperationsConfigException>(() =>
                initial.PreviewT01RankedSeasonReset(admin, "T01", version, readiness, endsAt));
            Assert.Equal("ranked_season_reset_repair_season_ended", ended.Code);

            initial.SettleRankedMatch("limit-source", first.Id, second.Id, 0, seasonId: "T01");
            RewriteSnapshot(path, data =>
            {
                var source = data["RankedIntegrityAudits"]!.AsArray().Single()!.AsObject();
                var audits = new JsonArray();
                for (var index = 0; index < 501; index++)
                {
                    var clone = JsonNode.Parse(source.ToJsonString())!.AsObject();
                    clone["Id"] = $"limit-audit-{index}";
                    clone["MatchId"] = $"limit-match-{index}";
                    audits.Add(clone);
                }
                data["RankedIntegrityAudits"] = audits;
            });
            var overLimit = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var overLimitAdmin = overLimit.Login("Admin", "L12master").Account!;
            var limitError = Assert.Throws<L12OperationsConfigException>(() =>
                overLimit.PreviewT01RankedSeasonReset(overLimitAdmin, "T01", version, readiness,
                    DateTimeOffset.UtcNow.AddMinutes(1)));
            Assert.Equal("ranked_season_reset_repair_too_many_matches", limitError.Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void T01ResetRepairWaivesHeldTransitionWithoutPlayerNotificationOrCooldown()
    {
        var root = TempRoot();
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("暂扣过渡甲", "Password123!").Account!;
            var second = store.Register("暂扣过渡乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            ActivateNextSeason(store, admin, "T01", "T01 赛季");
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            for (var index = 0; index < 3; index++)
            {
                var endedAt = baseTime.AddSeconds(index * 10 + 5);
                store.SettleRankedMatch($"held-transition-{index}", first.Id, second.Id, 0,
                    integrity: new L12RankedIntegrityContext(endedAt.AddSeconds(-5), endedAt, 0,
                        "surrender", null, null), seasonId: "T01");
            }
            Assert.NotEmpty(store.RankedIntegrityNotifications(first).Items);
            Assert.NotNull(store.RankedEntryBlock(first.Id, DateTimeOffset.UtcNow));
            var version = store.OperationsConfig(admin).Version;

            var result = RepairWithPreview(store, admin, "放弃含暂扣的过渡局", version,
                Context("repair-held-transition", version));

            Assert.Equal(3, result.TransitionMatchesWaived);
            Assert.Empty(store.RankedIntegrityNotifications(first).Items);
            Assert.Null(store.RankedEntryBlock(first.Id, DateTimeOffset.UtcNow));
            Assert.Equal("voided", store.RankedSettlement("held-transition-2", first.Id)!.RewardStatus);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static L12RankedSeasonResetRepairView RepairWithPreview(L12PlatformStore store,
        L12AccountView admin, string reason, long operationsVersion, L12AdminAuditContext context,
        DateTimeOffset? observedAt = null)
    {
        var now = observedAt ?? DateTimeOffset.UtcNow;
        var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
        var preview = store.PreviewT01RankedSeasonReset(admin, "T01", operationsVersion,
            readiness, now);
        return store.RepairT01RankedSeasonReset(admin, "T01", reason, operationsVersion,
            readiness, context, preview.EvidenceFingerprint, now);
    }

    private static L12AdminCommandResult<L12SeasonActivationView> ActivateThroughBus(
        L12PlatformStore store, L12AccountView admin, string idempotencyKey)
    {
        var seasons = store.SeasonCatalog(admin);
        var operationsVersion = store.OperationsConfig(admin).Version;
        var commandId = Guid.NewGuid().ToString("N");
        var command = new L12AdminCommandEnvelope<L12SeasonActivationCommandPayload>(commandId,
            idempotencyKey, "operations.config.season-activate", admin, DateTimeOffset.UtcNow,
            "operations:config", "activate through command bus", false, operationsVersion,
            new L12SeasonActivationCommandPayload(seasons.Next!.DefinitionId,
                seasons.Current.Revision, seasons.Next.Revision),
            new L12AdminAuditContext(commandId,
                L12Authorization.Key(L12Permission.AdminOperationsWrite), commandId, idempotencyKey,
                operationsVersion, Reason: "activate through command bus", RequestMethod: "POST",
                RequestPath: "/api/admin/seasons/draft/activate"));
        return new L12AdminCommandBus(store).Execute(command, L12Permission.AdminOperationsWrite,
            current =>
            {
                try
                {
                    return L12AdminCommandResult<L12SeasonActivationView>.Ok(store.ActivateSeason(
                        current.Actor, current.Payload.DefinitionId,
                        current.Payload.ExpectedCurrentRevision, current.Payload.ExpectedDraftRevision,
                        current.Reason ?? string.Empty,
                        new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 0, 0, 0, 0),
                        current.AuditContext));
                }
                catch (L12OperationsConfigException error)
                {
                    return L12AdminCommandResult<L12SeasonActivationView>.Fail(error.Code,
                        error.Message, 400);
                }
            }, risk: L12AdminCommandRisk.High);
    }

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    private static L12AdminAuditContext Context(string id, long? expected = null)
        => new(id, ExpectedVersion: expected, RequestMethod: "TEST", RequestPath: "/test");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static void RewriteSnapshot(string path, Action<JsonObject> mutate)
    {
        var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        mutate(data);
        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(Path.GetDirectoryName(path)!, "platform.db"));
        File.WriteAllText(path, data.ToJsonString());
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-season-activation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
