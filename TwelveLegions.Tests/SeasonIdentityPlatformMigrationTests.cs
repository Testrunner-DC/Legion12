using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SeasonIdentityPlatformMigrationTests
{
    [Fact]
    public async Task NormalizesBothDatabasesWithoutChangingCardIdsOrLegacyEvidence()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        var recorderPreview = await recorder.PreviewSeasonIdentityNormalizationAsync();
        var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
        var beforeCardIds = fixture.Store.OperationsConfig(fixture.Admin).Config.DisasterPool.CardIds.ToArray();
        var beforePreviewVersion = fixture.Store.OperationsConfigVersion();
        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            recorderPreview, readiness, fixture.Now);

        Assert.True(preview.CanApply, string.Join(',', preview.BlockingCodes));
        Assert.Equal(beforePreviewVersion, fixture.Store.OperationsConfigVersion());
        Assert.Equal("S01", await ReadMatchSeasonAsync(fixture.MatchDatabasePath, "legacy-zero"));
        Assert.Equal("T01", await ReadMatchSeasonAsync(fixture.MatchDatabasePath, "legacy-one"));
        var claim = fixture.Store.ClaimSeasonIdentityNormalization(fixture.Admin, recorderPreview,
            readiness, preview.PlatformFingerprint, preview.RecorderFingerprint, "worker-a",
            TimeSpan.FromMinutes(2), fixture.Now, new L12AdminAuditContext("s0-claim"));
        Assert.False(claim.Replayed);
        Assert.True(fixture.Store.IsRankedSeasonCutoverFenced(fixture.Now));

        var recorderResult = await recorder.ApplySeasonIdentityNormalizationAsync(
            recorderPreview.Fingerprint, claim.Owner, TimeSpan.FromMinutes(2));
        var committed = fixture.Store.CommitSeasonIdentityNormalization(fixture.Admin, claim,
            recorderResult, readiness, "归一赛季内部编号", fixture.Now.AddSeconds(1),
            new L12AdminAuditContext("s0-commit"));
        Assert.True(committed.Applied);
        Assert.Equal("platform_committed", committed.Status);
        Assert.True(fixture.Store.IsRankedSeasonCutoverFenced(fixture.Now.AddSeconds(1)));

        var verified = fixture.Store.VerifySeasonIdentityNormalization(fixture.Admin, recorderResult,
            fixture.Now.AddSeconds(2), new L12AdminAuditContext("s0-verify"));
        Assert.Equal("verified", verified.Status);
        Assert.False(fixture.Store.IsRankedSeasonCutoverFenced(fixture.Now.AddSeconds(2)));

        var catalog = fixture.Store.SeasonCatalog(fixture.Admin, fixture.Now.AddSeconds(2));
        Assert.Equal("S01", catalog.Current.SeasonId);
        Assert.Equal(1, catalog.Current.SeasonOrdinal);
        var archive = Assert.Single(catalog.Archives);
        Assert.Equal("S00", archive.SeasonId);
        Assert.Equal(0, archive.SeasonOrdinal);
        Assert.Equal("S01", archive.NextSeasonId);
        Assert.Equal("S01", fixture.Store.OperationsConfig(fixture.Admin).Config.Season.Id);
        Assert.Equal(beforeCardIds, fixture.Store.OperationsConfig(fixture.Admin).Config.DisasterPool.CardIds);
        Assert.Contains(beforeCardIds, id => id.StartsWith("S01-", StringComparison.Ordinal));
        Assert.Equal("S00", Assert.Single(fixture.Store.AlternateArtAwardRules()).SeasonId);
        Assert.All(fixture.Store.AlternateArtGrants("s0player01"), grant =>
        {
            Assert.Equal("S00", grant.SourceReference);
            Assert.Equal("season-final", grant.SourceKind);
        });
        Assert.All(fixture.Store.RankedOverview(fixture.Player.Id).History,
            row => Assert.Equal("S00", row.SeasonId));
        Assert.Equal("S00", await ReadMatchSeasonAsync(fixture.MatchDatabasePath, "legacy-zero"));
        Assert.Equal("S01", await ReadMatchSeasonAsync(fixture.MatchDatabasePath, "legacy-one"));
        Assert.Equal("immutable-hash", await ReadOutboxHashAsync(fixture.MatchDatabasePath, "legacy-one"));
        var coordination = await ReadFinalizationCoordinationAsync(fixture.Store.TransactionalStoragePath);
        Assert.Equal("S00", coordination.SeasonId);
        Assert.Equal(5, coordination.CompletedStorageRevision);
        var marker = await ReadPlatformMarkerAsync(fixture.Store.TransactionalStoragePath);
        Assert.Equal(committed.CompletedStorageRevision, marker.CompletedStorageRevision);
        Assert.Equal("verified", marker.Status);

        var repairReplay = fixture.Store.RepairT01RankedSeasonReset(fixture.Admin, "T01",
            "幂等复核", -1, readiness, new L12AdminAuditContext("b0-replay"));
        Assert.True(repairReplay.Replayed);
        var verifyReplay = fixture.Store.VerifySeasonIdentityNormalization(fixture.Admin,
            recorderResult, fixture.Now.AddSeconds(3), new L12AdminAuditContext("s0-verify-replay"));
        Assert.True(verifyReplay.Replayed);

        var current = fixture.Store.SeasonCatalog(fixture.Admin).Current;
        var next = fixture.Store.CreateSeasonDraft(fixture.Admin, current.Revision,
            "创建第2赛季", new L12AdminAuditContext("create-s02"));
        Assert.Equal("S02", next.SeasonId);
        Assert.Equal(2, next.SeasonOrdinal);
        var ordinalConflict = Assert.Throws<L12OperationsConfigException>(() =>
            fixture.Store.UpdateSeasonDraft(fixture.Admin, next.DefinitionId,
                new L12SeasonDefinitionDraft("S03", next.Name, next.StartsAt, next.EndsAt,
                    next.Configuration), next.Revision, "拒绝改写固定序号",
                new L12AdminAuditContext("reject-s03")));
        Assert.Equal("season_ordinal_identity_conflict", ordinalConflict.Code);
    }

    [Fact]
    public async Task IndependentStoresHaveSingleOwnerAndExpiredLeaseCanResumeRecorderCommit()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
        var recorderPreview = await recorder.PreviewSeasonIdentityNormalizationAsync();
        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            recorderPreview, readiness, fixture.Now);
        var claim = fixture.Store.ClaimSeasonIdentityNormalization(fixture.Admin, recorderPreview,
            readiness, preview.PlatformFingerprint, preview.RecorderFingerprint, "owner-a",
            TimeSpan.FromSeconds(20), fixture.Now, new L12AdminAuditContext("owner-a"));

        var second = new L12PlatformStore(fixture.PlatformJsonPath);
        var secondAdmin = second.Login("Admin", "L12master").Account!;
        var conflict = Assert.Throws<L12OperationsConfigException>(() =>
            second.ClaimSeasonIdentityNormalization(secondAdmin, recorderPreview, readiness,
                preview.PlatformFingerprint, preview.RecorderFingerprint, "owner-b",
                TimeSpan.FromSeconds(20), fixture.Now.AddSeconds(1),
                new L12AdminAuditContext("owner-b-conflict")));
        Assert.Equal("season_identity_lease_conflict", conflict.Code);

        var recorderResult = await recorder.ApplySeasonIdentityNormalizationAsync(
            recorderPreview.Fingerprint, claim.Owner, TimeSpan.FromMinutes(2));
        var committedPreview = await recorder.PreviewSeasonIdentityNormalizationAsync();
        var resumePreview = second.PreviewSeasonIdentityNormalization(secondAdmin,
            committedPreview, readiness, fixture.Now.AddSeconds(21));
        Assert.True(resumePreview.CanApply, string.Join(',', resumePreview.BlockingCodes));
        var takeover = second.ClaimSeasonIdentityNormalization(secondAdmin, committedPreview,
            readiness, resumePreview.PlatformFingerprint, resumePreview.RecorderFingerprint,
            "owner-b", TimeSpan.FromMinutes(2), fixture.Now.AddSeconds(21),
            new L12AdminAuditContext("owner-b-takeover"));
        Assert.False(takeover.Replayed);
        var staleOwner = Assert.Throws<L12OperationsConfigException>(() =>
            fixture.Store.CommitSeasonIdentityNormalization(fixture.Admin, claim,
                recorderResult, readiness, "旧 owner 不得提交", fixture.Now.AddSeconds(22),
                new L12AdminAuditContext("owner-a-stale")));
        Assert.Equal("season_identity_lease_lost", staleOwner.Code);
        var committed = second.CommitSeasonIdentityNormalization(secondAdmin, takeover,
            recorderResult, readiness, "恢复双库迁移", fixture.Now.AddSeconds(23),
            new L12AdminAuditContext("owner-b-commit"));
        Assert.True(committed.Applied);
        var verified = second.VerifySeasonIdentityNormalization(secondAdmin, recorderResult,
            fixture.Now.AddSeconds(24), new L12AdminAuditContext("owner-b-verify"));
        Assert.Equal("verified", verified.Status);

        var third = new L12PlatformStore(fixture.PlatformJsonPath);
        var thirdAdmin = third.Login("Admin", "L12master").Account!;
        var recorderDrift = third.PreviewSeasonIdentityNormalization(thirdAdmin,
            committedPreview with { ResultFingerprint = new string('a', 64) },
            new L12RankedSeasonCutoverReadiness("S01", 0, 0, 0, 0), fixture.Now.AddSeconds(25));
        Assert.False(recorderDrift.CanApply);
        Assert.Contains("season_identity_recorder_completed_state_drift",
            recorderDrift.BlockingCodes);
        var completed = third.PreviewSeasonIdentityNormalization(thirdAdmin, committedPreview,
            new L12RankedSeasonCutoverReadiness("S01", 0, 0, 0, 0), fixture.Now.AddSeconds(25));
        Assert.True(completed.CanApply);
        Assert.Equal("verified", completed.Status);
        var replay = third.ClaimSeasonIdentityNormalization(thirdAdmin, committedPreview,
            new L12RankedSeasonCutoverReadiness("S01", 0, 0, 0, 0), completed.PlatformFingerprint,
            completed.RecorderFingerprint, "owner-c", TimeSpan.FromMinutes(2),
            fixture.Now.AddSeconds(25), new L12AdminAuditContext("owner-c-replay"));
        Assert.True(replay.Replayed);
    }

    [Fact]
    public async Task PlatformFailureRollsBackEveryTypedReferenceAndCanBeRetried()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
        var recorderPreview = await recorder.PreviewSeasonIdentityNormalizationAsync();
        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            recorderPreview, readiness, fixture.Now);
        var claim = fixture.Store.ClaimSeasonIdentityNormalization(fixture.Admin, recorderPreview,
            readiness, preview.PlatformFingerprint, preview.RecorderFingerprint, "owner-a",
            TimeSpan.FromSeconds(20), fixture.Now, new L12AdminAuditContext("rollback-claim"));
        var recorderResult = await recorder.ApplySeasonIdentityNormalizationAsync(
            recorderPreview.Fingerprint, claim.Owner, TimeSpan.FromMinutes(2));
        fixture.Store.StorageFailureInjector = point =>
        {
            if (point == "after-season-identity-platform-marker")
                throw new IOException("injected platform migration failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() =>
            fixture.Store.CommitSeasonIdentityNormalization(fixture.Admin, claim, recorderResult,
                readiness, "注入回滚", fixture.Now.AddSeconds(1),
                new L12AdminAuditContext("rollback-commit")));

        var reloaded = new L12PlatformStore(fixture.PlatformJsonPath);
        var admin = reloaded.Login("Admin", "L12master").Account!;
        Assert.Equal("T01", reloaded.SeasonCatalog(admin).Current.SeasonId);
        Assert.Equal("S01", Assert.Single(reloaded.SeasonCatalog(admin).Archives).SeasonId);
        var recorderCommitted = await recorder.PreviewSeasonIdentityNormalizationAsync();
        var retryPreview = reloaded.PreviewSeasonIdentityNormalization(admin, recorderCommitted,
            readiness, fixture.Now.AddSeconds(21));
        Assert.True(retryPreview.CanApply, string.Join(',', retryPreview.BlockingCodes));
        var retryClaim = reloaded.ClaimSeasonIdentityNormalization(admin, recorderCommitted,
            readiness, retryPreview.PlatformFingerprint, retryPreview.RecorderFingerprint,
            "owner-b", TimeSpan.FromMinutes(2), fixture.Now.AddSeconds(21),
            new L12AdminAuditContext("rollback-retry-claim"));
        var retryCommit = reloaded.CommitSeasonIdentityNormalization(admin, retryClaim,
            recorderResult, readiness, "从 recorder_committed 恢复", fixture.Now.AddSeconds(22),
            new L12AdminAuditContext("rollback-retry-commit"));
        Assert.True(retryCommit.Applied);
        var retryVerify = reloaded.VerifySeasonIdentityNormalization(admin, recorderResult,
            fixture.Now.AddSeconds(23), new L12AdminAuditContext("rollback-retry-verify"));
        Assert.Equal("verified", retryVerify.Status);
    }

    [Fact]
    public async Task PreviewFailsClosedWhenB0CoverageDriftsAfterRepair()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        fixture.Store.SettleRankedMatch("post-b0-drift", fixture.Player.Id, fixture.Rival.Id, 0,
            "S01-04M1", "S02-03M1", seasonId: "T01");

        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            await recorder.PreviewSeasonIdentityNormalizationAsync(),
            new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0), fixture.Now);

        Assert.False(preview.CanApply);
        Assert.Contains("season_identity_b0_coverage_changed", preview.BlockingCodes);
    }

    [Fact]
    public async Task PreviewFailsClosedWhenB0WasNeverApplied()
    {
        var fixture = await CreateFixtureAsync(applyB0: false);
        await using var recorder = fixture.Recorder;

        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            await recorder.PreviewSeasonIdentityNormalizationAsync(),
            new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0), fixture.Now);

        Assert.False(preview.CanApply);
        Assert.Contains("season_identity_b0_marker_missing", preview.BlockingCodes);
    }

    [Fact]
    public async Task PreviewFailsClosedOutsideExplicitMaintenance()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        fixture.Store.EndImmediateMaintenance(fixture.Admin,
            fixture.Store.OperationsConfigVersion(), "结束维护以验证门禁",
            new L12AdminAuditContext("maintenance-off"));

        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            await recorder.PreviewSeasonIdentityNormalizationAsync(),
            new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0), fixture.Now);

        Assert.False(preview.CanApply);
        Assert.Contains("season_identity_maintenance_required", preview.BlockingCodes);
    }

    [Fact]
    public async Task PreviewFailsClosedForCanonicalTargetCollision()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        fixture.Store.SeedSeasonIdentityAlternateArtFixture(fixture.Player.Id, "manual",
            "target-collision", "S00");

        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            await recorder.PreviewSeasonIdentityNormalizationAsync(),
            new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0), fixture.Now);

        Assert.False(preview.CanApply);
        Assert.Contains("season_identity_target_collision", preview.BlockingCodes);
    }

    [Fact]
    public async Task PreviewFailsClosedForUnknownAlternateArtSeasonEncoding()
    {
        var fixture = await CreateFixtureAsync();
        await using var recorder = fixture.Recorder;
        fixture.Store.SeedSeasonIdentityAlternateArtFixture(fixture.Player.Id,
            "ranked-participants", "S01:unknown-shape");

        var preview = fixture.Store.PreviewSeasonIdentityNormalization(fixture.Admin,
            await recorder.PreviewSeasonIdentityNormalizationAsync(),
            new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0), fixture.Now);

        Assert.False(preview.CanApply);
        Assert.Contains("season_identity_unknown_alternate_art_reference", preview.BlockingCodes);
    }

    private static async Task<Fixture> CreateFixtureAsync(bool applyB0 = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-season-identity-platform",
            Guid.NewGuid().ToString("N"));
        var jsonPath = Path.Combine(directory, "platform.json");
        var matchPath = Path.Combine(directory, "matches.db");
        var store = new L12PlatformStore(jsonPath);
        var admin = store.Login("Admin", "L12master").Account!;
        var player = store.Register("s0player01", "Password123!").Account!;
        var rival = store.Register("s0rival002", "Password123!").Account!;
        store.SelectRankedFaction(player.Id, "order");
        store.SelectRankedFaction(rival.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"old-season-{index}", player.Id, rival.Id, 0,
                "S01-04M1", "S02-03M1", seasonId: "S01");

        var initialCatalog = store.SeasonCatalog(admin);
        var draft = initialCatalog.Next ?? store.CreateSeasonDraft(admin,
            initialCatalog.Current.Revision, "创建第1赛季", new L12AdminAuditContext("create-t01"));
        var now = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var updated = store.UpdateSeasonDraft(admin, draft.DefinitionId,
            new L12SeasonDefinitionDraft("T01", "第1赛季", now.AddDays(-1), now.AddDays(30),
                draft.Configuration), draft.Revision, "配置第1赛季", new L12AdminAuditContext("update-t01"));
        store.ActivateSeason(admin, updated.DefinitionId, initialCatalog.Current.Revision,
            updated.Revision, "激活第1赛季", new L12RankedSeasonCutoverReadiness("S01", 0, 0, 0, 0),
            new L12AdminAuditContext("activate-t01"));
        var operations = store.OperationsConfig(admin);
        store.BeginImmediateMaintenance(admin, 2, operations.Version,
            "赛季编号迁移维护", new L12AdminAuditContext("s0-maintenance"));
        if (applyB0)
        {
            var repairVersion = store.OperationsConfig(admin).Version;
            var readiness = new L12RankedSeasonCutoverReadiness("T01", 0, 0, 0, 0);
            var repairPreview = store.PreviewT01RankedSeasonReset(admin, "T01", repairVersion,
                readiness, now);
            store.RepairT01RankedSeasonReset(admin, "T01", "废弃部署前过渡期排位",
                repairVersion, readiness, new L12AdminAuditContext("b0-apply"),
                repairPreview.EvidenceFingerprint, now);
        }
        store.SeedSeasonIdentityAlternateArtFixture(player.Id, "season-final", "S01", "S01");
        await SeedFinalizationCoordinationAsync(store.TransactionalStoragePath,
            store.SeasonCatalog(admin).Archives.Single().SourceDefinitionId);

        var recorder = new MatchRecorder(matchPath, () => now);
        await recorder.InitializeAsync();
        await SeedMatchAsync(matchPath, "legacy-zero", "S01");
        await SeedMatchAsync(matchPath, "legacy-one", "T01");
        await SeedAppliedOutboxAsync(matchPath, "legacy-one", "immutable-hash");
        return new(store, admin, player, rival, recorder, jsonPath, matchPath, now);
    }

    private static async Task SeedFinalizationCoordinationAsync(string path, string definitionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO season_finalization_coordination(
                definition_id,season_id,status,lease_owner,lease_expires_utc,completed_utc,
                completed_storage_revision,updated_utc)
            VALUES($definition,'S01','finalized',NULL,NULL,$completed,5,$completed);
            """;
        command.Parameters.AddWithValue("$definition", definitionId);
        command.Parameters.AddWithValue("$completed", "2026-10-01T00:00:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedMatchAsync(string path, string matchId, string seasonId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                started_utc,ended_utc,winner,final_hash,error,mode_id,season_id,initial_state_json)
            VALUES($match,$match,1,'甲','乙','甲卡组','乙卡组',$started,$ended,0,'hash',NULL,
                'ranked',$season,'{"CardId":"S01-0001"}');
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$started", "2026-09-01T00:00:00.0000000+00:00");
        command.Parameters.AddWithValue("$ended", "2026-09-01T00:10:00.0000000+00:00");
        command.Parameters.AddWithValue("$season", seasonId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAppliedOutboxAsync(string path, string matchId, string hash)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,attempts,
                created_utc,applied_utc)
            VALUES($match,'{"Version":1,"SeasonId":"T01"}',$hash,'applied',1,$created,$created);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$created", "2026-09-01T00:10:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadMatchSeasonAsync(string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT season_id FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadOutboxHashAsync(string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_hash FROM ranked_settlement_outbox WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(string SeasonId, long CompletedStorageRevision)>
        ReadFinalizationCoordinationAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT season_id,completed_storage_revision
            FROM season_finalization_coordination;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetString(0), reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<(string Status, long CompletedStorageRevision)>
        ReadPlatformMarkerAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status,completed_storage_revision
            FROM platform_season_identity_migrations
            WHERE migration_id='season-id-normalization-s00-s01-v1';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetString(0), reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private sealed record Fixture(L12PlatformStore Store, L12AccountView Admin,
        L12AccountView Player, L12AccountView Rival, MatchRecorder Recorder, string PlatformJsonPath,
        string MatchDatabasePath, DateTimeOffset Now);
}
