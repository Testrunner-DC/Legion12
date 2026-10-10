using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MatchRecorderSeasonIdentityMigrationTests
{
    [Fact]
    public async Task PreciselySwapsSeasonKeysAndPreservesAppliedOutboxEvidence()
    {
        var path = TemporaryDatabasePath();
        await using var recorder = new MatchRecorder(path,
            () => new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        await recorder.InitializeAsync();
        await SeedMatchAsync(path, "old-season-match", "S01",
            "{\"CardId\":\"S01-0001\",\"Season\":{\"Id\":\"S01\"}}");
        await SeedMatchAsync(path, "current-season-match", "T01", "{\"CardId\":\"S01-0002\"}");
        await SeedMatchAsync(path, "unrelated-match", "S02", "{\"CardId\":\"S01-0003\"}");
        await SeedAppliedOutboxAsync(path, "current-season-match",
            "{\"Version\":1,\"MatchId\":\"current-season-match\",\"SeasonId\":\"T01\"}",
            "immutable-payload-hash");

        var preview = await recorder.PreviewSeasonIdentityNormalizationAsync();

        Assert.True(preview.CanApply);
        Assert.Equal("ready", preview.Status);
        Assert.Equal(1, preview.LegacySeasonZeroMatches);
        Assert.Equal(1, preview.LegacySeasonOneMatches);
        Assert.Equal(0, preview.CanonicalSeasonZeroCollisions);

        var result = await recorder.ApplySeasonIdentityNormalizationAsync(preview.Fingerprint,
            "test-owner", TimeSpan.FromMinutes(2));

        Assert.True(result.Applied);
        Assert.False(result.Replayed);
        Assert.Equal(1, result.SeasonZeroMatchesMigrated);
        Assert.Equal(1, result.SeasonOneMatchesMigrated);
        Assert.Equal("S00", await ReadSeasonIdAsync(path, "old-season-match"));
        Assert.Equal("S01", await ReadSeasonIdAsync(path, "current-season-match"));
        Assert.Equal("S02", await ReadSeasonIdAsync(path, "unrelated-match"));
        Assert.Equal("{\"CardId\":\"S01-0001\",\"Season\":{\"Id\":\"S01\"}}",
            await ReadInitialStateAsync(path, "old-season-match"));
        Assert.Equal((
                "{\"Version\":1,\"MatchId\":\"current-season-match\",\"SeasonId\":\"T01\"}",
                "immutable-payload-hash", "applied"),
            await ReadOutboxEvidenceAsync(path, "current-season-match"));

        await SeedMatchAsync(path, "post-migration-current-match", "S01");
        var replay = await recorder.ApplySeasonIdentityNormalizationAsync(preview.Fingerprint,
            "second-owner", TimeSpan.FromMinutes(2));
        Assert.False(replay.Applied);
        Assert.True(replay.Replayed);
        Assert.Equal(result.ResultFingerprint, replay.ResultFingerprint);
    }

    [Fact]
    public async Task RejectsCanonicalSeasonZeroCollisionWithoutChangingRows()
    {
        var path = TemporaryDatabasePath();
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await SeedMatchAsync(path, "legacy", "S01");
        await SeedMatchAsync(path, "collision", "S00");

        var preview = await recorder.PreviewSeasonIdentityNormalizationAsync();

        Assert.False(preview.CanApply);
        Assert.Contains("season_identity_target_collision", preview.BlockingCodes);
        var error = await Assert.ThrowsAsync<L12SeasonIdentityMigrationException>(() =>
            recorder.ApplySeasonIdentityNormalizationAsync(preview.Fingerprint,
                "test-owner", TimeSpan.FromMinutes(2)));
        Assert.Equal("season_identity_target_collision", error.Code);
        Assert.Equal("S01", await ReadSeasonIdAsync(path, "legacy"));
        Assert.Equal("S00", await ReadSeasonIdAsync(path, "collision"));
    }

    [Theory]
    [InlineData("pending-outbox", "season_identity_pending_outbox")]
    [InlineData("active-runtime", "season_identity_active_ranked_runtime")]
    public async Task RejectsUnsafeRankedPersistenceState(string unsafeState, string expectedCode)
    {
        var path = TemporaryDatabasePath();
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await SeedMatchAsync(path, "legacy", "S01");
        if (unsafeState == "pending-outbox")
            await SeedPendingOutboxAsync(path, "legacy");
        else
            await SeedActiveRuntimeAsync(path, "legacy");

        var preview = await recorder.PreviewSeasonIdentityNormalizationAsync();

        Assert.False(preview.CanApply);
        Assert.Contains(expectedCode, preview.BlockingCodes);
        var error = await Assert.ThrowsAsync<L12SeasonIdentityMigrationException>(() =>
            recorder.ApplySeasonIdentityNormalizationAsync(preview.Fingerprint,
                "test-owner", TimeSpan.FromMinutes(2)));
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("S01", await ReadSeasonIdAsync(path, "legacy"));
    }

    [Fact]
    public async Task RejectsPreviewDriftAndConcurrentApplyCommitsOnlyOnce()
    {
        var path = TemporaryDatabasePath();
        await using var first = new MatchRecorder(path);
        await using var second = new MatchRecorder(path);
        await first.InitializeAsync();
        await second.InitializeAsync();
        await SeedMatchAsync(path, "legacy", "S01");
        var stale = await first.PreviewSeasonIdentityNormalizationAsync();
        await SeedMatchAsync(path, "late-current", "T01");

        var drift = await Assert.ThrowsAsync<L12SeasonIdentityMigrationException>(() =>
            first.ApplySeasonIdentityNormalizationAsync(stale.Fingerprint,
                "first-owner", TimeSpan.FromMinutes(2)));
        Assert.Equal("season_identity_preview_expired", drift.Code);

        var current = await first.PreviewSeasonIdentityNormalizationAsync();
        var attempts = await Task.WhenAll(
            first.ApplySeasonIdentityNormalizationAsync(current.Fingerprint,
                "first-owner", TimeSpan.FromMinutes(2)),
            second.ApplySeasonIdentityNormalizationAsync(current.Fingerprint,
                "second-owner", TimeSpan.FromMinutes(2)));

        Assert.Equal(1, attempts.Count(result => result.Applied));
        Assert.Equal(1, attempts.Count(result => result.Replayed));
        Assert.Equal("S00", await ReadSeasonIdAsync(path, "legacy"));
        Assert.Equal("S01", await ReadSeasonIdAsync(path, "late-current"));
    }

    [Fact]
    public async Task StorageFailureRollsBackRowsAndCompletionMarker()
    {
        var path = TemporaryDatabasePath();
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await SeedMatchAsync(path, "legacy", "S01");
        await SeedMatchAsync(path, "current", "T01");
        var preview = await recorder.PreviewSeasonIdentityNormalizationAsync();
        recorder.StorageFailureInjector = point =>
        {
            if (point == "before-season-identity-recorder-commit")
                throw new IOException("injected recorder migration failure");
        };

        await Assert.ThrowsAsync<IOException>(() => recorder.ApplySeasonIdentityNormalizationAsync(
            preview.Fingerprint, "test-owner", TimeSpan.FromMinutes(2)));

        Assert.Equal("S01", await ReadSeasonIdAsync(path, "legacy"));
        Assert.Equal("T01", await ReadSeasonIdAsync(path, "current"));
        Assert.Equal(0, await CountMigrationMarkersAsync(path));
    }

    [Fact]
    public async Task ActiveLeaseRejectsAnotherOwnerAndExpiredLeaseCanBeTakenOver()
    {
        var path = TemporaryDatabasePath();
        var now = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        await SeedMatchAsync(path, "legacy", "S01");
        var preview = await recorder.PreviewSeasonIdentityNormalizationAsync();
        await SeedExecutingMarkerAsync(path, preview.Fingerprint, now.AddMinutes(1));

        var executing = await recorder.PreviewSeasonIdentityNormalizationAsync();
        Assert.False(executing.CanApply);
        Assert.Equal("executing", executing.Status);
        Assert.Contains("season_identity_lease_conflict", executing.BlockingCodes);

        var conflict = await Assert.ThrowsAsync<L12SeasonIdentityMigrationException>(() =>
            recorder.ApplySeasonIdentityNormalizationAsync(preview.Fingerprint,
                "second-owner", TimeSpan.FromMinutes(2)));
        Assert.Equal("season_identity_lease_conflict", conflict.Code);
        Assert.Equal("S01", await ReadSeasonIdAsync(path, "legacy"));

        now = now.AddMinutes(2);
        var takeover = await recorder.ApplySeasonIdentityNormalizationAsync(preview.Fingerprint,
            "second-owner", TimeSpan.FromMinutes(2));
        Assert.True(takeover.Applied);
        Assert.Equal("S00", await ReadSeasonIdAsync(path, "legacy"));
    }

    private static string TemporaryDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-season-id-migration",
            Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "matches.db");
    }

    private static async Task SeedMatchAsync(string path, string matchId, string seasonId,
        string initialStateJson = "{}")
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                started_utc,ended_utc,winner,final_hash,error,mode_id,season_id,initial_state_json)
            VALUES($match,$room,1,'甲','乙','甲卡组','乙卡组',$started,$ended,0,'hash',NULL,
                'ranked',$season,$initial);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$room", matchId);
        command.Parameters.AddWithValue("$started", "2026-09-01T00:00:00.0000000+00:00");
        command.Parameters.AddWithValue("$ended", "2026-09-01T00:10:00.0000000+00:00");
        command.Parameters.AddWithValue("$season", seasonId);
        command.Parameters.AddWithValue("$initial", initialStateJson);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedAppliedOutboxAsync(string path, string matchId,
        string payload, string hash)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,attempts,
                created_utc,applied_utc)
            VALUES($match,$payload,$hash,'applied',1,$created,$created);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$created", "2026-09-01T00:10:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }

    private static Task SeedPendingOutboxAsync(string path, string matchId)
        => SeedOutboxAsync(path, matchId, "pending");

    private static async Task SeedOutboxAsync(string path, string matchId, string status)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,attempts,created_utc)
            VALUES($match,'{}','hash',$status,0,$created);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$created", "2026-09-01T00:10:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedActiveRuntimeAsync(string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ranked_match_runtime(match_id,room_code,status,checkpoint_json,
                checkpoint_hash,checkpoint_generation,updated_utc)
            VALUES($match,$room,'active','{}','hash',1,$updated);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$room", matchId);
        command.Parameters.AddWithValue("$updated", "2026-09-01T00:10:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadSeasonIdAsync(string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT season_id FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadInitialStateAsync(string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT initial_state_json FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<(string Payload, string Hash, string Status)> ReadOutboxEvidenceAsync(
        string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json,payload_hash,status FROM ranked_settlement_outbox WHERE match_id=$match;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<int> CountMigrationMarkersAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM season_identity_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SeedExecutingMarkerAsync(string path, string fingerprint,
        DateTimeOffset leaseExpiresAt)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO season_identity_migrations(
                migration_id,status,owner,lease_expires_utc,source_fingerprint,result_fingerprint,
                season_zero_matches,season_one_matches,started_utc,completed_utc)
            VALUES('season-id-normalization-s00-s01-v1','executing','first-owner',$lease,
                $fingerprint,NULL,1,0,$started,NULL);
            """;
        command.Parameters.AddWithValue("$lease", leaseExpiresAt.ToString("O"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$started", leaseExpiresAt.AddMinutes(-1).ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
