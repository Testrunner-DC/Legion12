using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// B-1 guard: a save must never replay inserts for audit rows the ledger already holds.
// LastAuditAppend reports the append path of the most recent save: Compared counts the
// staged rows that reached the unchanged check, Inserted the rows actually written.
// The retained ledger is still re-read and re-verified on every save (that sweep is the
// deliberate tamper check, see IncrementalReadAuditTests), so Compared grows with the
// ledger while Inserted must stay equal to the number of newly staged rows.
public sealed class AuditAppendReplayGuardTests
{
    private const int LedgerRows = 4400;

    [Fact]
    public void ConsecutiveSavesInsertOnlyNewAudits()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            WriteLegacyPlatform(path, LedgerRows);
            var store = new L12PlatformStore(path);
            Assert.Equal(LedgerRows, store.StorageStatus().RetainedAuditEvents);
            Assert.True(store.Register("auditowner", "password-123").Success);

            Assert.True(store.Login("auditowner", "password-123").Success);
            AssertOnlyNewAuditsWritten(store.LastAuditAppend, "first");

            Assert.True(store.Login("auditowner", "password-123").Success);
            AssertOnlyNewAuditsWritten(store.LastAuditAppend, "second");

            using var connection = ReadOnlyConnection(store.TransactionalStoragePath);
            Assert.Equal(LedgerRows + 2, Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;"));
            Assert.Equal(2, store.AdminAudit(category: "authentication").Count);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RestartDoesNotReplayRetainedLedgerInserts()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            WriteLegacyPlatform(path, 600);
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("restartown", "password-123").Success);
            Assert.True(store.Login("restartown", "password-123").Success);

            var reloaded = new L12PlatformStore(path);
            var restartLogin = reloaded.Login("restartown", "password-123");
            Assert.True(restartLogin.Success, $"mode={reloaded.StorageStatus().Mode} message={restartLogin.Message}");
            AssertOnlyNewAuditsWritten(reloaded.LastAuditAppend, "restart");

            using var connection = ReadOnlyConnection(reloaded.TransactionalStoragePath);
            Assert.Equal(602, Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void FailedCommitDoesNotAdvanceAppendStateAndTheRetryStillWritesTheAudit()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            WriteLegacyPlatform(path, 120);
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("retryowner", "password-123").Success);
            using var connection = ReadOnlyConnection(store.TransactionalStoragePath);
            var committed = Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;");

            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-audit-append") throw new IOException("synthetic-before-audit-append");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.Login("retryowner", "password-123"));
            store.StorageFailureInjector = null;
            Assert.Equal(committed, Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;"));

            Assert.True(store.Login("retryowner", "password-123").Success);
            AssertOnlyNewAuditsWritten(store.LastAuditAppend, "retry");
            Assert.Equal(committed + 1, Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;"));
            Assert.Single(store.AdminAudit(category: "authentication"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void CoreSnapshotAndMirrorCarryNoSecondCopyOfTheLedger()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            WriteLegacyPlatform(path, 1200);
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("snapownera", "password-123").Success);
            Assert.True(store.Login("snapownera", "password-123").Success);
            Assert.Equal(1201, store.StorageStatus().RetainedAuditEvents);

            using (var connection = ReadOnlyConnection(store.TransactionalStoragePath))
            {
                var snapshot = Text(connection, "SELECT snapshot_json FROM platform_state;");
                var auditBytes = Scalar(connection, "SELECT SUM(LENGTH(payload_json)) FROM admin_audit_events;");
                Assert.DoesNotContain("\"AdminAudit\"", snapshot);
                Assert.True(snapshot.Length < auditBytes,
                    $"snapshot={snapshot.Length} archived audit payloads={auditBytes}");
                Assert.Equal(1201, Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;"));
            }
            // The compatibility mirror stays a complete offline copy of the platform.
            Assert.Contains("\"AdminAudit\"", File.ReadAllText(path));

            // The ledger is restored from its own table, never from the snapshot.
            var reloaded = new L12PlatformStore(path);
            Assert.Equal(1201, reloaded.StorageStatus().RetainedAuditEvents);
            Assert.True(reloaded.Login("snapownera", "password-123").Success);
            Assert.True(reloaded.LastAuditAppend.Compared >= 1200,
                $"restored staged ledger compared={reloaded.LastAuditAppend.Compared}");
            Assert.Equal(2, reloaded.AdminAudit(category: "authentication").Count);
            using var reopened = ReadOnlyConnection(reloaded.TransactionalStoragePath);
            Assert.Equal(1202, Scalar(reopened, "SELECT COUNT(*) FROM admin_audit_events;"));
        }
        finally { Cleanup(root); }
    }

    private static void AssertOnlyNewAuditsWritten((long Compared, long Inserted) append, string stage)
    {
        Assert.True(append.Inserted == 1, $"{stage} save compared={append.Compared} inserted={append.Inserted}");
    }

    private static void WriteLegacyPlatform(string path, int auditRows)
    {
        var audits = Enumerable.Range(0, auditRows).Select(index => new
        {
            Id = $"audit-{index:D5}",
            ActorId = "legacy-actor",
            ActorName = "Legacy",
            Category = "security",
            Action = "legacy-event",
            Target = index.ToString(),
            CreatedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z").AddSeconds(index),
            Outcome = "succeeded",
        }).ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Version = 11,
            BusinessVersion = 11,
            Accounts = Array.Empty<object>(),
            Sessions = Array.Empty<object>(),
            AdminAudit = audits,
        }));
    }

    private static SqliteConnection ReadOnlyConnection(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-audit-append-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); }
        catch (IOException) { }
    }
}
