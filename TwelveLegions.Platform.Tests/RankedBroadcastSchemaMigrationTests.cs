using Microsoft.Data.Sqlite;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedBroadcastSchemaMigrationTests
{
    [Fact]
    public void FreshStoreCommitsSchemaNineBroadcastDefinitionDeliveryAndGeneration()
    {
        InStore((_, store) =>
        {
            using var connection = Open(store.TransactionalStoragePath);
            Assert.Equal(9, ScalarLong(connection,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal("9", ScalarText(connection,
                "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal(1, ScalarLong(connection,
                "SELECT EXISTS(SELECT 1 FROM sqlite_schema WHERE type='table' AND name='ranked_broadcast_definitions');"));
            Assert.Equal(1, ScalarLong(connection,
                "SELECT EXISTS(SELECT 1 FROM sqlite_schema WHERE type='table' AND name='ranked_broadcast_deliveries');"));
            Assert.Equal("active-v1", ScalarText(connection,
                "SELECT value FROM storage_meta WHERE key='ranked_broadcast_object_state';"));
            Assert.Equal("1", ScalarText(connection,
                "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';"));
        });
    }

    [Fact]
    public void FailedSchemaEightMigrationDoesNotPublishSchemaNine()
    {
        InStore((path, store) =>
        {
            CreateLiveBroadcast(store);
            SqliteConnection.ClearAllPools();
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);
            using (var connection = Open(store.TransactionalStoragePath))
            {
                Execute(connection, """
                    CREATE TABLE ranked_broadcast_definitions (
                        broadcast_id TEXT NOT NULL COLLATE NOCASE PRIMARY KEY,
                        match_id TEXT NOT NULL COLLATE NOCASE,
                        event_type TEXT NOT NULL COLLATE NOCASE,
                        message TEXT NOT NULL,
                        created_utc TEXT NOT NULL,
                        row_generation INTEGER NOT NULL CHECK(row_generation >= 0),
                        row_digest TEXT NOT NULL,
                        UNIQUE(match_id,event_type)
                    );
                    CREATE TABLE ranked_broadcast_deliveries (
                        account_id TEXT NOT NULL COLLATE NOCASE,
                        broadcast_id TEXT NOT NULL COLLATE NOCASE,
                        claim_token TEXT NOT NULL,
                        lease_expires_utc TEXT NOT NULL,
                        completed_utc TEXT,
                        row_generation INTEGER NOT NULL CHECK(row_generation >= 0),
                        row_digest TEXT NOT NULL,
                        PRIMARY KEY(account_id,broadcast_id),
                        FOREIGN KEY(broadcast_id) REFERENCES ranked_broadcast_definitions(broadcast_id)
                            ON DELETE CASCADE
                    );
                    CREATE INDEX ix_ranked_broadcast_definitions_delivery
                        ON ranked_broadcast_definitions(created_utc,broadcast_id);
                    CREATE INDEX ix_ranked_broadcast_deliveries_broadcast
                        ON ranked_broadcast_deliveries(broadcast_id);
                    CREATE TRIGGER fail_ranked_broadcast_migration
                    BEFORE INSERT ON ranked_broadcast_definitions
                    BEGIN SELECT RAISE(ABORT,'injected-ranked-broadcast-migration-failure'); END;
                    """);
            }

            var unavailable = Assert.Throws<L12PlatformStorageUnavailableException>(
                () => new L12PlatformStore(path));
            Assert.Contains("injected-ranked-broadcast-migration-failure",
                unavailable.ToString(), StringComparison.Ordinal);
            using var read = Open(store.TransactionalStoragePath);
            Assert.Equal(8, ScalarLong(read,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal("8", ScalarText(read,
                "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal(0, ScalarLong(read, "SELECT COUNT(*) FROM ranked_broadcast_definitions;"));
            Assert.Null(ScalarNullableText(read,
                "SELECT value FROM storage_meta WHERE key='ranked_broadcast_object_state';"));
        });
    }

    [Fact]
    public void SchemaNineRestartNeverDowngradesAndFutureTenFailsClosed()
    {
        InStore((path, store) =>
        {
            var generation = ReadScalarText(store.TransactionalStoragePath,
                "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';");
            var restarted = new L12PlatformStore(path);
            using (var current = Open(restarted.TransactionalStoragePath))
            {
                Assert.Equal(9, ScalarLong(current,
                    "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal("9", ScalarText(current,
                    "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal(generation, ScalarText(current,
                    "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';"));
                Execute(current, """
                    UPDATE storage_meta SET value='10' WHERE key='schema_version';
                    UPDATE platform_state SET schema_version=10 WHERE singleton_id=1;
                    """);
            }
            SqliteConnection.ClearAllPools();
            Assert.Throws<L12PlatformStorageUnavailableException>(
                () => new L12PlatformStore(path));
            using var future = Open(store.TransactionalStoragePath);
            Assert.Equal("10", ScalarText(future,
                "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal(10, ScalarLong(future,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
        });
    }

    [Fact]
    public void MissingSchemaNineObjectTableFailsClosedWithoutLegacyArrayResend()
    {
        InStore((path, store) =>
        {
            CreateLiveBroadcast(store);
            using (var connection = Open(store.TransactionalStoragePath))
                Execute(connection, "DROP TABLE ranked_broadcast_deliveries;");
            SqliteConnection.ClearAllPools();

            var error = Assert.Throws<L12PlatformStorageUnavailableException>(
                () => new L12PlatformStore(path));
            Assert.Contains("ranked_broadcast_deliveries", error.ToString(),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void InvalidSchemaNineGenerationFailsClosedWithoutMutation()
    {
        InStore((path, store) =>
        {
            using (var connection = Open(store.TransactionalStoragePath))
                Execute(connection, """
                    UPDATE storage_meta SET value='invalid-generation'
                    WHERE key='ranked_broadcast_generation';
                    """);
            SqliteConnection.ClearAllPools();

            Assert.Throws<L12PlatformStorageUnavailableException>(
                () => new L12PlatformStore(path));
            using var preserved = Open(store.TransactionalStoragePath);
            Assert.Equal("invalid-generation", ScalarText(preserved,
                "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';"));
            Assert.Equal("9", ScalarText(preserved,
                "SELECT value FROM storage_meta WHERE key='schema_version';"));
        });
    }

    [Fact]
    public void ReverseExitMirrorPrewriteFailureLeavesSchemaNineAuthorityUntouched()
    {
        InStore((path, store) =>
        {
            CreateLiveBroadcast(store);
            var pending = path + ".ranked-broadcast-schema8.pending";
            Directory.CreateDirectory(pending);

            var failure = Record.Exception(() =>
                L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                    store.TransactionalStoragePath));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                failure?.ToString() ?? "missing filesystem prewrite failure");

            using var preserved = Open(store.TransactionalStoragePath);
            Assert.Equal(9, ScalarLong(preserved,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal("9", ScalarText(preserved,
                "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal(1, ScalarLong(preserved, """
                SELECT EXISTS(SELECT 1 FROM sqlite_schema
                WHERE type='table' AND name='ranked_broadcast_deliveries');
                """));
        });
    }

    [Fact]
    public void ReverseExitRenameFailureLeavesVerifiedPendingMirrorThatSchemaEightRetryPublishes()
    {
        InStore((path, store) =>
        {
            var viewer = store.Register("trmigrename", "password-123").Account!;
            var subscribedAt = DateTimeOffset.UtcNow;
            CreateLiveBroadcast(store);
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewer.Id, subscribedAt));
            var pending = path + ".ranked-broadcast-schema8.pending";
            Exception? failure;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.None))
                failure = Record.Exception(() =>
                    L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                        store.TransactionalStoragePath));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                failure?.ToString() ?? "missing filesystem publication failure");

            using (var committed = Open(store.TransactionalStoragePath))
            {
                Assert.Equal(8, ScalarLong(committed,
                    "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.True(File.Exists(pending));
                var expectedHash = ScalarText(committed,
                    "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';");
                var pendingHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(pending)));
                Assert.Equal(expectedHash, pendingHash, ignoreCase: true);
                var previousHash = ScalarText(committed, """
                    SELECT value FROM storage_meta
                    WHERE key='ranked_broadcast_schema8_previous_mirror_sha256';
                    """);
                var finalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(path)));
                Assert.Equal(previousHash, finalHash, ignoreCase: true);
            }

            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);
            Assert.False(File.Exists(pending));
            var recovered = new L12PlatformStore(path);
            Assert.True(recovered.CompleteRankedBroadcast(
                viewer.Id, claim.Broadcast.Id, claim.ClaimToken));
        });
    }

    [Fact]
    public void ReverseExitRetryAfterPublishedFinalIsIdempotentWithoutPendingFile()
    {
        InStore((path, store) =>
        {
            CreateLiveBroadcast(store);
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);
            var pending = path + ".ranked-broadcast-schema8.pending";
            Assert.False(File.Exists(pending));
            var finalBytes = File.ReadAllBytes(path);
            var expectedHash = ReadScalarText(store.TransactionalStoragePath,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';");

            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);

            Assert.False(File.Exists(pending));
            Assert.Equal(finalBytes, File.ReadAllBytes(path));
            using var committed = Open(store.TransactionalStoragePath);
            Assert.Equal(8, ScalarLong(committed,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal(expectedHash, ScalarText(committed,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));
        });
    }

    [Fact]
    public void ReverseExitRejectsBadPendingWithoutReplacingVerifiedFinal()
    {
        InStore((path, store) =>
        {
            CreateLiveBroadcast(store);
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);
            var pending = path + ".ranked-broadcast-schema8.pending";
            var finalBytes = File.ReadAllBytes(path);
            var expectedHash = ReadScalarText(store.TransactionalStoragePath,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';");

            File.WriteAllText(path, "{}");
            Assert.Throws<InvalidDataException>(() =>
                L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                    store.TransactionalStoragePath));
            File.WriteAllBytes(path, finalBytes);
            File.WriteAllText(pending, "{}");

            Assert.Throws<InvalidDataException>(() =>
                L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                    store.TransactionalStoragePath));

            Assert.Equal(finalBytes, File.ReadAllBytes(path));
            Assert.True(File.Exists(pending));
            using var committed = Open(store.TransactionalStoragePath);
            Assert.Equal(8, ScalarLong(committed,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal(expectedHash, ScalarText(committed,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));
        });
    }

    [Fact]
    public void ReverseExitRejectsOldGenerationPendingEvenWhenItsHashIsRecorded()
    {
        InStore((path, store) =>
        {
            var players = CreateLiveBroadcast(store);
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);
            var oldMirror = File.ReadAllBytes(path);

            SqliteConnection.ClearAllPools();
            var upgraded = new L12PlatformStore(path);
            for (var index = 0; index < 5; index++)
                upgraded.SettleRankedMatch($"broadcast-new-generation-{index}",
                    players.FirstId, players.SecondId, 0);
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                upgraded.TransactionalStoragePath);
            var pending = path + ".ranked-broadcast-schema8.pending";
            File.Delete(path);
            File.WriteAllBytes(pending, oldMirror);
            var oldHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(oldMirror));
            using (var connection = Open(upgraded.TransactionalStoragePath))
            {
                using var update = connection.CreateCommand();
                update.CommandText = """
                    UPDATE storage_meta SET value=$hash WHERE key='fallback_json_sha256';
                    """;
                update.Parameters.AddWithValue("$hash", oldHash);
                Assert.Equal(1, update.ExecuteNonQuery());
            }

            Assert.Throws<InvalidDataException>(() =>
                L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                    upgraded.TransactionalStoragePath));

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(pending));
            using var committed = Open(upgraded.TransactionalStoragePath);
            Assert.Equal(8, ScalarLong(committed,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
        });
    }

    [Fact]
    public void ReverseExitRejectsFutureSchemaTenWithoutChangingObjectAuthority()
    {
        InStore((_, store) =>
        {
            CreateLiveBroadcast(store);
            using (var connection = Open(store.TransactionalStoragePath))
                Execute(connection, """
                    UPDATE storage_meta SET value='10' WHERE key='schema_version';
                    UPDATE platform_state SET schema_version=10 WHERE singleton_id=1;
                    """);

            Assert.Throws<InvalidDataException>(() =>
                L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                    store.TransactionalStoragePath));

            using var preserved = Open(store.TransactionalStoragePath);
            Assert.Equal("10", ScalarText(preserved,
                "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal(10, ScalarLong(preserved,
                "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal(1, ScalarLong(preserved, """
                SELECT EXISTS(SELECT 1 FROM sqlite_schema
                WHERE type='table' AND name='ranked_broadcast_deliveries');
                """));
        });
    }

    [Fact]
    public void LatestClaimCompletionNewDefinitionDeletionAndCutoverSurviveNineToEightToSevenToNine()
    {
        InStore((path, store) =>
        {
            var viewer = store.Register("trmigviewer", "password-123").Account!;
            var subscribedAt = DateTimeOffset.UtcNow;
            var players = CreateLiveBroadcast(store);
            var completedClaim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewer.Id, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(
                viewer.Id, completedClaim.Broadcast.Id, completedClaim.ClaimToken));

            var pendingResult = store.SettleRankedMatch(
                "broadcast-migration-pending", players.FirstId, players.SecondId, 0);
            Assert.NotEmpty(pendingResult.Broadcasts);
            var pendingClaim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewer.Id, subscribedAt));

            var deletedResult = store.SettleRankedMatch(
                "broadcast-migration-deleted", players.FirstId, players.SecondId, 0);
            var deleted = Assert.Single(deletedResult.Broadcasts.Take(1));
            var admin = store.Login("Admin", "L12master").Account!;
            foreach (var broadcast in deletedResult.Broadcasts)
                Assert.True(store.DeleteRankedBroadcast(admin, broadcast.Id,
                    new L12AdminAuditContext("ranked-broadcast-exit-delete-" + broadcast.Id)));
            var expectedBroadcastIds = store.RankedBroadcasts(100).Select(row => row.Id)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.DoesNotContain(deleted.Id, expectedBroadcastIds,
                StringComparer.OrdinalIgnoreCase);
            var generation = ReadScalarText(store.TransactionalStoragePath,
                "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';");

            Assert.Throws<InvalidDataException>(() =>
                L12PlatformStore.RevertCompactDeckPayloadStorageForRehearsal(
                    store.TransactionalStoragePath));
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
                store.TransactionalStoragePath);

            using (var schemaEight = Open(store.TransactionalStoragePath))
            {
                Assert.Equal("8", ScalarText(schemaEight,
                    "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal(8, ScalarLong(schemaEight,
                    "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal(0, ScalarLong(schemaEight, """
                    SELECT COUNT(*) FROM sqlite_schema
                    WHERE type='table' AND name LIKE 'ranked_broadcast_%';
                    """));
                using var snapshot = JsonDocument.Parse(ScalarText(schemaEight,
                    "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;"));
                var root = snapshot.RootElement;
                Assert.Equal(long.Parse(generation),
                    root.GetProperty("RankedBroadcastGeneration").GetInt64());
                Assert.NotEqual(JsonValueKind.Null,
                    root.GetProperty("RankedBroadcastDeliveryCutover").ValueKind);
                var broadcasts = root.GetProperty("RankedBroadcasts").EnumerateArray()
                    .Select(row => row.GetProperty("Id").GetString()!).Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Assert.Equal(expectedBroadcastIds, broadcasts);
                var deliveries = root.GetProperty("RankedBroadcastDeliveries").EnumerateArray().ToArray();
                Assert.Contains(deliveries, row =>
                    row.GetProperty("BroadcastId").GetString() == completedClaim.Broadcast.Id
                    && row.GetProperty("CompletedAt").ValueKind != JsonValueKind.Null);
                Assert.Contains(deliveries, row =>
                    row.GetProperty("BroadcastId").GetString() == pendingClaim.Broadcast.Id
                    && row.GetProperty("ClaimToken").GetString() == pendingClaim.ClaimToken
                    && row.GetProperty("CompletedAt").ValueKind == JsonValueKind.Null);
                Assert.DoesNotContain(deliveries, row =>
                    row.GetProperty("BroadcastId").GetString() == deleted.Id);
            }

            var mirrorOnlyPath = Path.Combine(Path.GetDirectoryName(path)!,
                "mirror-only", "platform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(mirrorOnlyPath)!);
            File.Copy(path, mirrorOnlyPath);
            var mirrorRecovered = new L12PlatformStore(mirrorOnlyPath);
            using (var recovered = Open(mirrorRecovered.TransactionalStoragePath))
            {
                Assert.Equal(generation, ScalarText(recovered,
                    "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';"));
                Assert.Equal(1, ScalarLong(recovered, $"""
                    SELECT COUNT(*) FROM ranked_broadcast_deliveries
                    WHERE account_id='{viewer.Id}' AND broadcast_id='{pendingClaim.Broadcast.Id}'
                      AND claim_token='{pendingClaim.ClaimToken}' AND completed_utc IS NULL;
                    """));
            }
            Assert.True(mirrorRecovered.CompleteRankedBroadcast(
                viewer.Id, pendingClaim.Broadcast.Id, pendingClaim.ClaimToken));

            L12PlatformStore.RevertCompactDeckPayloadStorageForRehearsal(
                store.TransactionalStoragePath);
            using (var schemaSeven = Open(store.TransactionalStoragePath))
                Assert.Equal("7", ScalarText(schemaSeven,
                    "SELECT value FROM storage_meta WHERE key='schema_version';"));

            SqliteConnection.ClearAllPools();
            var upgraded = new L12PlatformStore(path);
            using (var schemaNine = Open(upgraded.TransactionalStoragePath))
            {
                Assert.Equal("9", ScalarText(schemaNine,
                    "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal(generation, ScalarText(schemaNine,
                    "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';"));
            }
            Assert.Equal(expectedBroadcastIds, upgraded.RankedBroadcasts(100).Select(row => row.Id)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray());
            Assert.True(upgraded.CompleteRankedBroadcast(
                viewer.Id, pendingClaim.Broadcast.Id, pendingClaim.ClaimToken));
            using var verified = Open(upgraded.TransactionalStoragePath);
            Assert.Equal(1, ScalarLong(verified, $"""
                SELECT COUNT(*) FROM ranked_broadcast_deliveries
                WHERE account_id='{viewer.Id}' AND broadcast_id='{pendingClaim.Broadcast.Id}'
                  AND completed_utc IS NOT NULL;
                """));
        });
    }

    private static (string FirstId, string SecondId) CreateLiveBroadcast(L12PlatformStore store)
    {
        var first = store.Register("trmigfirst", "password-123").Account!;
        var second = store.Register("trmigsecond", "password-123").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"broadcast-migration-{index}", first.Id, second.Id, 0);
        Assert.NotEmpty(store.RankedBroadcasts());
        return (first.Id, second.Id);
    }

    private static void InStore(Action<string, L12PlatformStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            action(path, new L12PlatformStore(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarText(SqliteConnection connection, string sql)
        => Assert.IsType<string>(Scalar(connection, sql));

    private static string? ScalarNullableText(SqliteConnection connection, string sql)
        => Scalar(connection, sql) as string;

    private static string ReadScalarText(string path, string sql)
    {
        using var connection = Open(path);
        return ScalarText(connection, sql);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
