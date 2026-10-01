using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class SeasonAutomaticActivationTests
{
    [Fact]
    public async Task SeasonEndEntersDurableDrainingAndFinalizesWithoutNextSeason()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            var (store, admin, catalog) = PrepareEndingStore(root, endsAt, removeDraft: true);
            Assert.Equal(endsAt, store.NextSeasonActivationWakeAt(endsAt.AddHours(-1),
                TimeSpan.FromSeconds(1)));
            Assert.Null(store.RankedEntryBlock("account", endsAt.AddTicks(-1)));
            Assert.Contains("赛季正在切换", store.RankedEntryBlock("account", endsAt));
            Assert.Equal("active", store.SeasonCatalog(admin, endsAt.AddTicks(-1)).Current.FinalizationStatus);
            Assert.Equal("draining", store.SeasonCatalog(admin, endsAt).Current.FinalizationStatus);

            var restartedStore = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => endsAt);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, restartedStore, () => endsAt);
            await using var server = new L12WebSocketServer(rooms, recorder, restartedStore, catalog,
                seasonActivationUtcNow: () => endsAt);

            Assert.True(await server.RunSeasonFinalizationOnceAsync(endsAt));
            var finalized = restartedStore.SeasonCatalog(admin, endsAt);
            Assert.Equal("finalized", finalized.Current.FinalizationStatus);
            Assert.Equal(endsAt, finalized.Current.FinalizedAt);
            Assert.Null(finalized.Next);
            Assert.Empty(finalized.Archives);
            Assert.Contains("赛季正在切换", restartedStore.RankedEntryBlock("account", endsAt.AddHours(8)));
            Assert.False(await server.RunSeasonFinalizationOnceAsync(endsAt.AddHours(8)));

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.Equal("finalized", reopened.SeasonCatalog(admin, endsAt.AddHours(8)).Current.FinalizationStatus);
            Assert.False(reopened.TryClaimDueSeasonFinalization("restart", endsAt.AddHours(8),
                TimeSpan.FromMinutes(1)) is not null);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task CoordinatorWakesAtDeadlineAndFinalizesAsSoonAsStartedRankedMatchEnds()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var endsAt = DateTimeOffset.UtcNow.AddSeconds(2);
            var (store, admin, catalog) = PrepareEndingStore(root, endsAt, removeDraft: true);
            var first = store.Register("排空唤醒甲", "Password123!").Account!;
            var second = store.Register("排空唤醒乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"),
                () => DateTimeOffset.UtcNow);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => DateTimeOffset.UtcNow);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            await rooms.ConnectAsync(firstSession, first.Id, first.Username);
            await rooms.ConnectAsync(secondSession, second.Id, second.Username);
            await rooms.JoinMatchmakingAsync(firstSession, "ranked", null);
            var started = await rooms.JoinMatchmakingAsync(secondSession, "ranked", null);
            Assert.Contains(started, message => MessageType(message.Payload) == "gameState");
            Assert.Equal(1, await recorder.CountActiveRankedRuntimesAsync());

            server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => DateTimeOffset.UtcNow);
            await server.StartAsync(0);
            await WaitUntilAsync(() => store.SeasonCatalog(admin).Current.FinalizationStatus == "draining",
                TimeSpan.FromSeconds(8), "协调器未在 EndsAt 到点后进入 draining");
            Assert.Null(store.SeasonCatalog(admin).Current.FinalizedAt);
            Assert.Contains("赛季正在切换", store.RankedEntryBlock(first.Id,
                DateTimeOffset.UtcNow));

            using var surrender = JsonDocument.Parse("{\"type\":\"surrender\"}");
            var endedAt = DateTimeOffset.UtcNow;
            var finished = await rooms.HandleActionAsync(firstSession, surrender.RootElement);
            Assert.Contains(finished, message => MessageType(message.Payload) == "gameState");
            await WaitUntilAsync(() => store.SeasonCatalog(admin).Current.FinalizedAt is not null,
                TimeSpan.FromSeconds(8), "存量排位终局后协调器未自动结算");

            var finalized = store.SeasonCatalog(admin).Current;
            Assert.Equal("finalized", finalized.FinalizationStatus);
            Assert.True(finalized.FinalizedAt >= endsAt);
            Assert.True(finalized.FinalizedAt <= endedAt.AddSeconds(3));
            Assert.Single(store.AdminAudit(category: "operations")
                .Where(item => item.Action == "season-finalize"));
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Cleanup(root);
        }
    }

    [Fact]
    public async Task CoordinatorRestartAfterDeadlineFinalizesWithoutDraftOrExternalSignal()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var endsAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var (initial, admin, catalog) = PrepareEndingStore(root, endsAt, removeDraft: true);
            Assert.Equal("draining", initial.SeasonCatalog(admin).Current.FinalizationStatus);

            var restarted = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"),
                () => DateTimeOffset.UtcNow);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, restarted,
                () => DateTimeOffset.UtcNow);
            server = new L12WebSocketServer(rooms, recorder, restarted, catalog,
                seasonActivationUtcNow: () => DateTimeOffset.UtcNow);
            await server.StartAsync(0);

            await WaitUntilAsync(() => restarted.SeasonCatalog(admin).Current.FinalizedAt is not null,
                TimeSpan.FromSeconds(5), "跨过 EndsAt 重启后未自动补结算");
            Assert.Equal("finalized", restarted.SeasonCatalog(admin).Current.FinalizationStatus);
            Assert.Single(restarted.AdminAudit(category: "operations")
                .Where(item => item.Action == "season-finalize"));
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Cleanup(root);
        }
    }

    [Fact]
    public void ShanghaiAndUtcBoundaryAreTheSameFinalizationInstantAndClaimsAreSingleOwner()
    {
        var root = TempRoot();
        try
        {
            var utc = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            var shanghai = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(8));
            var (store, admin, _) = PrepareEndingStore(root, shanghai, removeDraft: false);
            Assert.Equal(utc, store.SeasonCatalog(admin).Current.EndsAt);
            Assert.False(store.IsRankedSeasonCutoverFenced(utc.AddTicks(-1)));
            Assert.True(store.IsRankedSeasonCutoverFenced(shanghai));

            var claims = new L12SeasonFinalizationClaim?[32];
            Parallel.For(0, claims.Length, index => claims[index] =
                store.TryClaimDueSeasonFinalization($"worker-{index}", utc, TimeSpan.FromMinutes(1)));
            Assert.Single(claims.Where(claim => claim is not null));
            Assert.Equal("draining", store.SeasonCatalog(admin, utc).Current.FinalizationStatus);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task IndependentStoreInstancesCannotClaimTheSameSeasonFinalization()
    {
        var root = TempRoot();
        try
        {
            var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            var (firstStore, _, catalog) = PrepareEndingStore(root, endsAt, removeDraft: true);
            var secondStore = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);

            using var barrier = new Barrier(2);
            var claims = await Task.WhenAll(
                Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    return firstStore.TryClaimDueSeasonFinalization("instance-a", endsAt,
                        TimeSpan.FromMinutes(1));
                }),
                Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    return secondStore.TryClaimDueSeasonFinalization("instance-b", endsAt,
                        TimeSpan.FromMinutes(1));
                }));

            Assert.Single(claims.Where(claim => claim is not null));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void ExpiredLeaseCanBeTakenOverAndTheOldOwnerCannotCommit()
    {
        var root = TempRoot();
        try
        {
            var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            var (firstStore, admin, catalog) = PrepareEndingStore(root, endsAt, removeDraft: true);
            var secondStore = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            var firstClaim = firstStore.TryClaimDueSeasonFinalization("instance-a", endsAt,
                TimeSpan.FromMinutes(1))!;
            Assert.Null(secondStore.TryClaimDueSeasonFinalization("instance-b",
                endsAt.AddSeconds(59), TimeSpan.FromMinutes(1)));

            var takeoverAt = endsAt.AddMinutes(1);
            var secondClaim = secondStore.TryClaimDueSeasonFinalization("instance-b", takeoverAt,
                TimeSpan.FromMinutes(1))!;
            var readiness = new L12RankedSeasonCutoverReadiness(firstClaim.SeasonId, 0, 0, 0, 0);
            var stale = Assert.Throws<L12OperationsConfigException>(() =>
                firstStore.FinalizeClaimedRankedSeason(admin, firstClaim, readiness, takeoverAt,
                    Context("stale-owner")));
            Assert.Equal("season_finalization_claim_stale", stale.Code);

            secondStore.FinalizeClaimedRankedSeason(admin, secondClaim, readiness,
                takeoverAt.AddSeconds(1), Context("takeover-owner"));
            Assert.Null(firstStore.TryClaimDueSeasonFinalization("instance-a",
                takeoverAt.AddHours(1), TimeSpan.FromMinutes(1)));
            var reopened = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.Equal("finalized", reopened.SeasonCatalog(admin, takeoverAt.AddHours(1))
                .Current.FinalizationStatus);
            Assert.Null(reopened.TryClaimDueSeasonFinalization("instance-c",
                takeoverAt.AddHours(1), TimeSpan.FromMinutes(1)));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void FinalizationCrashRollsBackFactsAndStaleOrdinaryWriteCannotEraseRecovery()
    {
        var root = TempRoot();
        try
        {
            var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            var (coordinator, admin, catalog) = PrepareEndingStore(root, endsAt, removeDraft: true);
            var staleOrdinaryWriter = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            var firstClaim = coordinator.TryClaimDueSeasonFinalization("crashing", endsAt,
                TimeSpan.FromMinutes(1))!;
            var readiness = new L12RankedSeasonCutoverReadiness(firstClaim.SeasonId, 0, 0, 0, 0);
            Assert.True(staleOrdinaryWriter.Register("结算前普通写", "Password123!").Success);
            coordinator.StorageFailureInjector = stage =>
            {
                if (stage == "after-season-finalization-complete-marker")
                    throw new IOException("simulated crash before commit");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                coordinator.FinalizeClaimedRankedSeason(admin, firstClaim, readiness,
                    endsAt.AddSeconds(1), Context("crash-before-commit")));
            coordinator.StorageFailureInjector = null;

            var afterCrash = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.Equal("draining", afterCrash.SeasonCatalog(admin, endsAt.AddSeconds(1))
                .Current.FinalizationStatus);
            Assert.Empty(afterCrash.AdminAudit(category: "operations")
                .Where(item => item.Action == "season-finalize"));

            var retryAt = endsAt.AddMinutes(1);
            var retryClaim = afterCrash.TryClaimDueSeasonFinalization("recovery", retryAt,
                TimeSpan.FromMinutes(1))!;
            afterCrash.FinalizeClaimedRankedSeason(admin, retryClaim, readiness,
                retryAt.AddSeconds(1), Context("recovery-commit"));
            Assert.Equal("finalized", afterCrash.SeasonCatalog(admin, retryAt.AddSeconds(1))
                .Current.FinalizationStatus);
            var completedRevision = afterCrash.StorageStatus().StorageRevision;
            Assert.True(afterCrash.Login("结算前普通写", "Password123!").Success);

            using (var connection = new SqliteConnection(
                       new SqliteConnectionStringBuilder { DataSource = afterCrash.TransactionalStoragePath }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT status,completed_storage_revision
                    FROM season_finalization_coordination
                    WHERE definition_id=$definition AND season_id=$season;
                    """;
                command.Parameters.AddWithValue("$definition", retryClaim.DefinitionId);
                command.Parameters.AddWithValue("$season", retryClaim.SeasonId);
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("finalized", reader.GetString(0));
                Assert.Equal(completedRevision, reader.GetInt64(1));
            }

            var staleWrite = Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() =>
                staleOrdinaryWriter.Register("结算后普通写", "Password123!"));
            Assert.Contains("早于已完成赛季结算版本", staleWrite.Message);
            Assert.True(staleOrdinaryWriter.Register("结算后普通写", "Password123!").Success);
            Assert.Equal("finalized", staleOrdinaryWriter.SeasonCatalog(admin, retryAt.AddHours(1))
                .Current.FinalizationStatus);
            Assert.Single(staleOrdinaryWriter.AdminAudit(category: "operations")
                .Where(item => item.Action == "season-finalize"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void DrainingWaitsForAuthoritativeReadinessAndActivationDoesNotRepeatFinalSummary()
    {
        var root = TempRoot();
        try
        {
            var endsAt = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(8));
            var (store, admin, _) = PrepareEndingStore(root, endsAt, removeDraft: false);
            var first = store.Register("结算等待甲", "Password123!").Account!;
            var second = store.Register("结算等待乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            var config = store.RankedConfig(admin);
            store.UpdateRankedConfig(admin, config with
                {
                    PlacementMatches = 1,
                    PlacementMaximum = 1,
                    Factions = config.Factions.Select(faction => faction with
                    {
                        Tiers = faction.Tiers.Select((tier, index) => tier with
                            { Minimum = index switch { 0 => 0, 1 => 1, 2 => 50, 3 => 75, _ => 100 } })
                            .ToArray(),
                    }).ToArray(),
                }, "compact finalization ranks", Context("compact-finalization"));
            var firstMaster = config.MasterTitles[0].MasterId;
            var secondMaster = config.MasterTitles.First(item => item.MasterId != firstMaster).MasterId;
            store.SettleRankedMatch("finalize-summary-1", first.Id, second.Id, 0,
                firstMaster, secondMaster);
            store.SettleRankedMatch("finalize-summary-2", first.Id, second.Id, 0,
                firstMaster, secondMaster);
            var finalProfile = store.RankedProfile(first.Id);
            var current = store.SeasonCatalog(admin, endsAt).Current;

            var blockedReadiness = new[]
            {
                new L12RankedSeasonCutoverReadiness(current.SeasonId, 1, 0, 0, 0),
                new L12RankedSeasonCutoverReadiness(current.SeasonId, 0, 1, 0, 0),
                new L12RankedSeasonCutoverReadiness(current.SeasonId, 0, 0, 1, 0),
                new L12RankedSeasonCutoverReadiness(current.SeasonId, 0, 0, 0, 1),
            };
            for (var index = 0; index < blockedReadiness.Length; index++)
            {
                var attemptedAt = endsAt.AddMilliseconds(index);
                var blockedClaim = store.TryClaimDueSeasonFinalization($"worker-{index}", attemptedAt,
                    TimeSpan.FromMinutes(1))!;
                var blocked = Assert.Throws<L12OperationsConfigException>(() =>
                    store.FinalizeClaimedRankedSeason(admin, blockedClaim,
                        blockedReadiness[index], attemptedAt, Context($"blocked-finalization-{index}")));
                Assert.Equal("season_finalization_not_ready", blocked.Code);
                store.RecordSeasonFinalizationWaiting(blockedClaim, attemptedAt,
                    blocked.Code, blocked.Message);
            }
            Assert.Empty(store.RankedOverview(first.Id).History);

            var readyAt = endsAt.AddSeconds(2);
            var readyClaim = store.TryClaimDueSeasonFinalization("worker-b", readyAt,
                TimeSpan.FromMinutes(1))!;
            store.FinalizeClaimedRankedSeason(admin, readyClaim,
                new L12RankedSeasonCutoverReadiness(current.SeasonId, 0, 0, 0, 0), readyAt,
                Context("ready-finalization"));
            var summary = Assert.Single(store.PendingSeasonSummaryNotifications(first.Id));
            Assert.Equal("秩序", summary.Faction);
            Assert.Equal(finalProfile.Faction, summary.Faction);
            Assert.Equal(finalProfile.Tier, summary.RankLabel);
            Assert.Equal(1, summary.FactionRank);
            Assert.Equal(1, summary.OverallRank);
            Assert.Equal(finalProfile.SevenValue, summary.SevenValue);
            Assert.NotNull(summary.FactionTitle);
            Assert.Contains(summary.FactionTitle!, summary.Titles);
            Assert.All(summary.MasterTitles, title => Assert.Contains(title, summary.Titles));

            var seasonCatalog = store.SeasonCatalog(admin, readyAt);
            var draft = store.UpdateSeasonDraft(admin, seasonCatalog.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S-after-gap", "空档后赛季", readyAt.AddHours(1),
                    readyAt.AddDays(90), seasonCatalog.Next.Configuration),
                seasonCatalog.Next.Revision, "prepare post-gap season", Context("prepare-post-gap"));
            var latestCurrent = store.SeasonCatalog(admin, readyAt).Current;
            store.ActivateSeason(admin, draft.DefinitionId, latestCurrent.Revision, draft.Revision,
                "activate after finalized gap",
                new L12RankedSeasonCutoverReadiness(latestCurrent.SeasonId, 0, 0, 0, 0),
                Context("activate-post-gap"));
            Assert.Equal("S-after-gap", store.SeasonCatalog(admin).Current.SeasonId);
            Assert.Single(store.PendingSeasonSummaryNotifications(first.Id));
            Assert.Single(store.RankedOverview(first.Id).History);
            Assert.Single(store.AdminAudit(category: "operations")
                .Where(item => item.Action == "season-finalize"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void BoundaryInstantFencesRankedAdmissionAndConcurrentTriggersHaveOneLease()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            var (store, admin, _) = PrepareArmedStore(root, now.AddMinutes(1), now);
            Assert.True(store.Login("Admin", "L12master").Success);
            Assert.Null(store.RankedEntryBlock("any-account", now.AddMinutes(1).AddTicks(-1)));
            Assert.Null(store.TryClaimDueSeasonActivation("early", now.AddMinutes(1).AddTicks(-1),
                TimeSpan.FromMinutes(1)));

            var due = now.AddMinutes(1);
            Assert.Contains("赛季正在切换", store.RankedEntryBlock("any-account", due));
            var claims = new L12SeasonActivationClaim?[32];
            Parallel.For(0, claims.Length, index => claims[index] =
                store.TryClaimDueSeasonActivation($"worker-{index}", due, TimeSpan.FromMinutes(1)));

            var claim = Assert.Single(claims.Where(item => item is not null))!;
            Assert.Equal("executing", store.SeasonCatalog(admin).Next!.ActivationPlan!.Status);
            Assert.Equal(1, claim.Generation);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RestartKeepsUnexpiredLeaseAndRecoversItOnlyAfterExpiry()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero);
            var (store, _, catalog) = PrepareArmedStore(root, now.AddMinutes(1), now);
            var due = now.AddMinutes(1);
            var first = store.TryClaimDueSeasonActivation("worker-a", due, TimeSpan.FromMinutes(2));
            Assert.NotNull(first);

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.Null(reopened.TryClaimDueSeasonActivation("worker-b", due.AddMinutes(1),
                TimeSpan.FromMinutes(2)));
            var recovered = reopened.TryClaimDueSeasonActivation("worker-b", due.AddMinutes(2),
                TimeSpan.FromMinutes(2));
            Assert.NotNull(recovered);
            Assert.Equal(first!.Generation, recovered!.Generation);
            Assert.Equal(first.IntentKey, recovered.IntentKey);
            Assert.NotEqual(first.LeaseOwner, recovered.LeaseOwner);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void OperationsUpdateRaceExpiresPlanAndRequiresExplicitDisarmBeforeEditing()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero);
            var (store, admin, _) = PrepareArmedStore(root, now.AddMinutes(1), now);
            var armed = store.SeasonCatalog(admin).Next!;
            var editBlocked = Assert.Throws<L12OperationsConfigException>(() =>
                store.UpdateSeasonDraft(admin, armed.DefinitionId,
                    new L12SeasonDefinitionDraft(armed.SeasonId, "不得静默修改", armed.StartsAt,
                        armed.EndsAt, armed.Configuration), armed.Revision, "race edit", Context("race-edit")));
            Assert.Equal("season_activation_armed", editBlocked.Code);

            var operations = store.OperationsConfig(admin);
            store.ApplyOperationsConfig(admin, operations.Config with
                {
                    Announcements =
                    [new L12AnnouncementConfig("plan-race", "版本变化", true)],
                }, operations.Version, "force plan precondition race", Context("ops-race"));

            Assert.Null(store.TryClaimDueSeasonActivation("worker", now.AddMinutes(1),
                TimeSpan.FromMinutes(1)));
            var failed = store.SeasonCatalog(admin).Next!;
            Assert.Equal("failed", failed.ActivationPlan!.Status);
            Assert.Equal("season_activation_plan_expired", failed.ActivationPlan.LastErrorCode);
            Assert.Contains("赛季正在切换", store.RankedEntryBlock("account", now.AddMinutes(1)));

            var disarmed = store.DisarmSeasonActivation(admin, failed.DefinitionId, failed.Revision,
                failed.ActivationPlan.Generation, failed.ActivationPlan.DisarmGuardToken,
                "review stale plan", now.AddMinutes(2), Context("disarm"));
            Assert.Equal("disarmed", disarmed.ActivationPlan!.Status);
            var updated = store.UpdateSeasonDraft(admin, disarmed.DefinitionId,
                new L12SeasonDefinitionDraft(disarmed.SeasonId, "允许显式修改", now.AddHours(2),
                    disarmed.EndsAt, disarmed.Configuration), disarmed.Revision,
                "edit after disarm", Context("edit-after-disarm"));
            Assert.Equal("允许显式修改", updated.Name);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task AutomaticAndManualPathsShareAtomicActivationAndDuplicateTicksDoNotRepeatIt()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
            var (store, admin, catalog) = PrepareArmedStore(root, now.AddMinutes(1), now);
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => now.AddMinutes(1));
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => now.AddMinutes(1));

            var runs = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => server.RunSeasonActivationOnceAsync(now.AddMinutes(1))));
            Assert.Single(runs.Where(result => result));
            var automaticallyActivated = store.SeasonCatalog(admin);
            Assert.Equal("S-auto", automaticallyActivated.Current.SeasonId);
            Assert.Equal(now.AddMinutes(1), automaticallyActivated.Current.ActivatedAt);
            Assert.Equal(now.AddMinutes(1), automaticallyActivated.Current.ActivationPlan!.CompletedAt);
            Assert.Null(automaticallyActivated.Next);
            Assert.Single(store.SeasonArchives(admin));
            Assert.False(await server.RunSeasonActivationOnceAsync(now.AddMinutes(2)));
            Assert.Single(store.SeasonArchives(admin));

            var manualRoot = Path.Combine(root, "manual");
            Directory.CreateDirectory(manualRoot);
            var (manualStore, manualAdmin, _) = PrepareArmedStore(manualRoot, now.AddHours(1), now);
            var manualCatalog = manualStore.SeasonCatalog(manualAdmin);
            var manual = manualStore.ActivateSeason(manualAdmin, manualCatalog.Next!.DefinitionId,
                manualCatalog.Current.Revision, manualCatalog.Next.Revision, "manual override",
                new L12RankedSeasonCutoverReadiness(manualCatalog.Current.SeasonId, 0, 0, 0, 0),
                Context("manual-activate"));
            Assert.True(manual.Activated);
            Assert.Equal("completed", manual.Current.ActivationPlan!.Status);
            Assert.Single(manualStore.SeasonArchives(manualAdmin));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task DurableRankedCheckpointKeepsDuePlanWaitingAcrossServiceRestart()
    {
        var root = TempRoot();
        var database = Path.Combine(root, "matches.db");
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
            var (store, admin, catalog) = PrepareArmedStore(root, now.AddMinutes(1), now);
            var first = store.Register("autoa", "Password123!").Account!;
            var second = store.Register("autob", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");

            await using (var recorder = new MatchRecorder(database, () => now))
            {
                await recorder.InitializeAsync();
                var rooms = new L12RoomManager(catalog, recorder, store, () => now);
                var firstSession = Guid.NewGuid();
                var secondSession = Guid.NewGuid();
                await rooms.ConnectAsync(firstSession, first.Id, first.Username);
                await rooms.ConnectAsync(secondSession, second.Id, second.Username);
                await rooms.JoinMatchmakingAsync(firstSession, "ranked", null);
                var started = await rooms.JoinMatchmakingAsync(secondSession, "ranked", null);
                Assert.Contains(started, message => MessageType(message.Payload) == "gameState");
            }

            await using var reopenedRecorder = new MatchRecorder(database, () => now.AddMinutes(1));
            await reopenedRecorder.InitializeAsync();
            var reopenedStore = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedRooms = new L12RoomManager(catalog, reopenedRecorder, reopenedStore,
                () => now.AddMinutes(1));
            var recovery = await reopenedRooms.RestoreRankedRoomsAsync();
            Assert.Equal(1, recovery.Restored);
            await using var server = new L12WebSocketServer(reopenedRooms, reopenedRecorder,
                reopenedStore, catalog, seasonActivationUtcNow: () => now.AddMinutes(1));

            Assert.False(await server.RunSeasonActivationOnceAsync(now.AddMinutes(1)));
            var waiting = reopenedStore.SeasonCatalog(admin).Next!.ActivationPlan!;
            Assert.Equal("waiting", waiting.Status);
            Assert.Equal("season_cutover_not_ready", waiting.LastErrorCode);
            Assert.Equal("S01", reopenedStore.SeasonCatalog(admin).Current.SeasonId);
            Assert.Empty(reopenedStore.SeasonArchives(admin));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task SqliteCommitFailureLeavesNoHalfCutoverAndExpiredLeaseRetriesSafely()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);
            var (store, admin, catalog) = PrepareArmedStore(root, now.AddMinutes(1), now);
            var oldSeasonId = store.SeasonCatalog(admin).Current.SeasonId;
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => now.AddMinutes(1));
            var activationTime = now.AddMinutes(1);
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => activationTime);
            var commits = 0;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit" && Interlocked.Increment(ref commits) == 2)
                    throw new IOException("injected automatic cutover commit failure");
            };

            Assert.False(await server.RunSeasonActivationOnceAsync(now.AddMinutes(1)));
            store.StorageFailureInjector = null;
            var rolledBack = store.SeasonCatalog(admin);
            Assert.Equal(oldSeasonId, rolledBack.Current.SeasonId);
            Assert.NotNull(rolledBack.Next);
            Assert.Equal("executing", rolledBack.Next!.ActivationPlan!.Status);
            Assert.Empty(rolledBack.Archives);
            Assert.Equal(oldSeasonId, store.OperationsConfig(admin).Config.Season.Id);

            activationTime = now.AddMinutes(1).AddSeconds(31);
            Assert.True(await server.RunSeasonActivationOnceAsync(activationTime));
            Assert.Equal("S-auto", store.SeasonCatalog(admin).Current.SeasonId);
            Assert.Single(store.SeasonArchives(admin));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ArmAndDisarmApiAreHighRiskIdempotentCommands()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var login = store.Login("Admin", "L12master");
            var seasons = store.SeasonCatalog(login.Account!);
            var scheduledAt = DateTimeOffset.UtcNow.AddHours(1);
            var draft = store.UpdateSeasonDraft(login.Account!, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S-api-auto", "API 自动赛季", scheduledAt,
                    scheduledAt.AddDays(90), seasons.Next.Configuration), seasons.Next.Revision,
                "prepare api automatic season", Context("api-prepare"));
            var operationsVersion = store.OperationsConfig(login.Account!).Version;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder,
                store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            using (var missingPreviewRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/arm", login.Token!,
                new SeasonActivationArmRequest(seasons.Current.Revision, draft.Revision,
                    "reject missing preview", "season-arm-missing-preview", operationsVersion)))
            using (var missingPreviewResponse = await client.SendAsync(missingPreviewRequest))
                Assert.Equal(HttpStatusCode.Conflict, missingPreviewResponse.StatusCode);
            L12SeasonActivationImpactPreviewView impactPreview;
            using (var previewRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/activation-preview", login.Token!,
                new SeasonActivationPreviewRequest(seasons.Current.Revision, draft.Revision,
                    operationsVersion)))
            using (var previewResponse = await client.SendAsync(previewRequest))
            {
                Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
                impactPreview = (await previewResponse.Content
                    .ReadFromJsonAsync<L12SeasonActivationImpactPreviewView>())!;
                Assert.True(impactPreview.Valid);
                Assert.Equal("unarmed", impactPreview.PlanStatus);
            }
            using (var stalePreviewRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/arm", login.Token!,
                new SeasonActivationArmRequest(seasons.Current.Revision, draft.Revision,
                    "reject stale preview", "season-arm-stale-preview", operationsVersion,
                    impactPreview.PreviewToken + "-stale")))
            using (var stalePreviewResponse = await client.SendAsync(stalePreviewRequest))
                Assert.Equal(HttpStatusCode.Conflict, stalePreviewResponse.StatusCode);
            var body = new SeasonActivationArmRequest(seasons.Current.Revision, draft.Revision,
                "arm from api", "season-arm-api-1", operationsVersion,
                impactPreview.PreviewToken);

            using var firstRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/arm", login.Token!, body);
            using var firstResponse = await client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var firstJson = await firstResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("\"intentKey\"", firstJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"leaseOwner\"", firstJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"lastErrorMessage\"", firstJson, StringComparison.OrdinalIgnoreCase);
            var armed = JsonSerializer.Deserialize<L12SeasonDefinitionView>(firstJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("armed", armed.ActivationPlan!.Status);

            using var replayRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/arm", login.Token!, body);
            using var replayResponse = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            Assert.Equal("true", replayResponse.Headers.GetValues("X-Idempotent-Replay").Single());
            var replay = (await replayResponse.Content.ReadFromJsonAsync<L12SeasonDefinitionView>())!;
            Assert.Equal(armed.Revision, replay.Revision);

            var staleDisarmBody = new SeasonActivationDisarmRequest(armed.Revision,
                armed.ActivationPlan.Generation, armed.ActivationPlan.DisarmGuardToken + "-stale",
                "reject replaced plan", "season-disarm-api-stale", operationsVersion);
            using (var staleDisarmRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/disarm", login.Token!,
                staleDisarmBody))
            using (var staleDisarmResponse = await client.SendAsync(staleDisarmRequest))
                Assert.Equal(HttpStatusCode.Conflict, staleDisarmResponse.StatusCode);

            var disarmBody = new SeasonActivationDisarmRequest(armed.Revision,
                armed.ActivationPlan.Generation, armed.ActivationPlan.DisarmGuardToken,
                "disarm from api", "season-disarm-api-1", operationsVersion);
            using var disarmRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/disarm", login.Token!, disarmBody);
            using var disarmResponse = await client.SendAsync(disarmRequest);
            Assert.Equal(HttpStatusCode.OK, disarmResponse.StatusCode);
            var disarmed = (await disarmResponse.Content.ReadFromJsonAsync<L12SeasonDefinitionView>())!;
            Assert.Equal("disarmed", disarmed.ActivationPlan!.Status);
            Assert.Contains(store.AdminCommands(type: "operations.config.season-activation-arm"),
                command => command.Risk == "high" && command.Status == "executed");
            Assert.Contains(store.AdminCommands(type: "operations.config.season-activation-disarm"),
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
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Cleanup(root);
        }
    }

    [Fact]
    public async Task DueFenceAllowsLoginButRejectsQueuedPairingBeforeDurableGameStart()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
            var clockReads = 0;
            DateTimeOffset Clock() => Interlocked.Increment(ref clockReads) >= 4
                ? now.AddMinutes(1) : now;
            var (store, _, catalog) = PrepareArmedStore(root, now.AddMinutes(1), now);
            var first = store.Register("gatea", "Password123!").Account!;
            var second = store.Register("gateb", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => now);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, Clock);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            await rooms.ConnectAsync(firstSession, first.Id, first.Username);
            await rooms.ConnectAsync(secondSession, second.Id, second.Username);
            var queued = await rooms.JoinMatchmakingAsync(firstSession, "ranked", null);
            Assert.Contains(queued, message => MessageType(message.Payload) == "matchmakingState");

            Assert.True(store.Login(first.Username, "Password123!").Success);
            Assert.Equal(first.Id, store.RankedProfile(first.Id).AccountId);
            var blocked = await rooms.JoinMatchmakingAsync(secondSession, "ranked", null);
            Assert.Contains(blocked, message => MessageType(message.Payload) == "matchmakingRejected");
            Assert.Equal(0, await recorder.CountActiveRankedRuntimesAsync());
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task CrossLeaseTakeoverInvalidatesDelayedWorkerAtFinalCheck()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero);
            var due = now.AddMinutes(1);
            var currentTime = due;
            var (store, admin, catalog) = PrepareArmedStore(root, due, now);
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"),
                () => currentTime);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => currentTime);
            L12SeasonActivationClaim? takeover = null;
            rooms.RankedSeasonCutoverFinalCheckInjector = () =>
            {
                currentTime = due.AddSeconds(31);
                var originalOwner = store.SeasonCatalog(admin).Next!.ActivationPlan!.LeaseOwner!;
                takeover = store.TryClaimDueSeasonActivation(originalOwner, currentTime,
                    TimeSpan.FromMinutes(1));
                return Task.CompletedTask;
            };
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => currentTime);

            Assert.False(await server.RunSeasonActivationOnceAsync(due));
            Assert.NotNull(takeover);
            var catalogAfter = store.SeasonCatalog(admin);
            Assert.Equal("S01", catalogAfter.Current.SeasonId);
            Assert.Empty(catalogAfter.Archives);
            Assert.Equal("executing", catalogAfter.Next!.ActivationPlan!.Status);
            Assert.Equal(takeover!.LeaseOwner, catalogAfter.Next.ActivationPlan.LeaseOwner);
            Assert.Equal(takeover.LeaseExpiresAt, catalogAfter.Next.ActivationPlan.LeaseExpiresAt);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task AutomaticActivationFailsClosedWhenIndependentAuditIsUnavailable()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
            var due = now.AddMinutes(1);
            var (store, admin, catalog) = PrepareArmedStore(root, due, now);
            store.AuditAvailabilityProbeOverride = () => false;
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => due);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => due);
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => due);

            Assert.False(await server.RunSeasonActivationOnceAsync(due));
            var blocked = store.SeasonCatalog(admin);
            Assert.Equal("S01", blocked.Current.SeasonId);
            Assert.Empty(blocked.Archives);
            Assert.Equal("waiting", blocked.Next!.ActivationPlan!.Status);
            Assert.Equal("audit_unavailable", blocked.Next.ActivationPlan.LastErrorCode);
        }
        finally { Cleanup(root); }
    }

    private static (L12PlatformStore Store, L12AccountView Admin, L12Catalog Catalog) PrepareArmedStore(
        string root, DateTimeOffset scheduledAt, DateTimeOffset now)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var seasons = store.SeasonCatalog(admin);
        var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
            new L12SeasonDefinitionDraft("S-auto", "自动赛季", scheduledAt, scheduledAt.AddDays(90),
                seasons.Next.Configuration), seasons.Next.Revision, "prepare automatic season",
            Context("prepare"));
        var operationsVersion = store.OperationsConfig(admin).Version;
        var readiness = new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 0, 0, 0, 0);
        var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
            seasons.Current.Revision, draft.Revision, operationsVersion, readiness, now);
        store.ArmSeasonActivation(admin, draft.DefinitionId, seasons.Current.Revision, draft.Revision,
            operationsVersion, preview.PreviewToken, readiness, "arm automatic season", now,
            Context("arm", operationsVersion));
        return (store, admin, catalog);
    }

    private static (L12PlatformStore Store, L12AccountView Admin, L12Catalog Catalog) PrepareEndingStore(
        string root, DateTimeOffset endsAt, bool removeDraft)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var operations = store.OperationsConfig(admin);
        store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = endsAt },
            }, operations.Version, "set deterministic season end", Context("prepare-ending"));
        if (removeDraft)
        {
            var seasons = store.SeasonCatalog(admin);
            store.DeleteSeasonDraft(admin, seasons.Next!.DefinitionId, seasons.Next.Revision,
                "exercise finalization gap without next season", Context("remove-next"));
        }
        return (store, admin, catalog);
    }

    private static string? MessageType(object payload)
    {
        var property = payload.GetType().GetProperty("type");
        return property?.GetValue(payload)?.ToString();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout,
        string failureMessage)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(25);
        Assert.True(predicate(), failureMessage);
    }

    private static L12AdminAuditContext Context(string id, long? expected = null)
        => new(id, ExpectedVersion: expected, RequestMethod: "TEST", RequestPath: "/test");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-season-auto-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
}
