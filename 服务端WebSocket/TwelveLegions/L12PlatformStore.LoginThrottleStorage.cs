using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Login throttles are attempt state, not account content: they carry a failure window and a
    // lock deadline, they are pruned during normal operation, and nothing else references them.
    // Keeping them out of the core snapshot stops every save from rewriting a domain that always
    // has its own rows, while the compatibility mirror still serializes them so the JSON fallback
    // stays a complete offline copy.
    private const string LoginThrottleSplitBackupSuffix = ".pre-login-throttle-split";

    private const string LoginThrottleColumns =
        "key,failure_count,window_started_utc,last_failure_utc,locked_until_utc";

    private static void InitializeLoginThrottleSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS login_throttles (
                key TEXT PRIMARY KEY,
                failure_count INTEGER NOT NULL,
                window_started_utc TEXT NOT NULL,
                last_failure_utc TEXT NOT NULL,
                locked_until_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_login_throttles_locked ON login_throttles(locked_until_utc);
            """;
        command.ExecuteNonQuery();
    }

    // Runs once per open, after the sessions have been hydrated. The table is the authority; a
    // snapshot that still carries throttles (schema 13, or a database whose split never
    // committed) is copied into it inside one transaction.
    private DataFile EnsureAndHydrateLoginThrottleStorage(SqliteConnection connection, DataFile data)
    {
        var stored = ReadLoginThrottleRows(connection, transaction: null);
        if (stored.Count == 0 && data.LoginThrottles.Count != 0)
        {
            // A schema-13 program rejects a schema-14 database while constructing the store, so
            // this copy is the only path back to the previous binary.
            WriteLoginThrottleSplitBackup();
            using var split = connection.BeginTransaction(deferred: false);
            SynchronizeLoginThrottleRows(connection, split, data.LoginThrottles);
            SetStorageSchemaVersion(connection, split, PlatformStorageSchemaVersion);
            split.Commit();
            return data;
        }

        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            SetStorageSchemaVersion(connection, transaction, PlatformStorageSchemaVersion);
            transaction.Commit();
        }
        if (stored.Count == 0) return data;
        data.LoginThrottles = stored;
        return data;
    }

    private void WriteLoginThrottleSplitBackup()
    {
        var path = _databasePath + LoginThrottleSplitBackupSuffix;
        _migrationBackupPath = path;
        if (File.Exists(path)) return;
        using var source = OpenDatabase(_databasePath, readOnly: true);
        using var destination = OpenDatabase(path, readOnly: false, initialize: false);
        source.BackupDatabase(destination);
    }

    // Every path that rebuilds in-memory state from a stored snapshot calls this: the core
    // snapshot no longer carries the throttles, so the table supplies them again.
    private static void HydrateLoginThrottles(SqliteConnection connection, DataFile data,
        SqliteTransaction? transaction)
    {
        var stored = ReadLoginThrottleRows(connection, transaction);
        if (stored.Count == 0) return;
        data.LoginThrottles = stored;
    }

    // Row-level sync inside the save transaction: only changed rows are rewritten and a row the
    // in-memory list no longer holds (pruned or cleared after a successful login) is deleted.
    private static void SynchronizeLoginThrottleRows(SqliteConnection connection,
        SqliteTransaction transaction, IReadOnlyList<LoginThrottleRow> throttles)
    {
        var pending = new Dictionary<string, LoginThrottleRow>(StringComparer.Ordinal);
        foreach (var throttle in throttles) pending[throttle.Key] = throttle;
        var present = ReadLoginThrottleRows(connection, transaction)
            .ToDictionary(row => row.Key, StringComparer.Ordinal);
        foreach (var throttle in pending.Values)
        {
            if (present.TryGetValue(throttle.Key, out var previous) && LoginThrottleEquals(previous, throttle))
                continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO login_throttles({LoginThrottleColumns})
                VALUES($key,$failures,$window,$last,$locked)
                ON CONFLICT(key) DO UPDATE SET
                    failure_count=excluded.failure_count,
                    window_started_utc=excluded.window_started_utc,
                    last_failure_utc=excluded.last_failure_utc,
                    locked_until_utc=excluded.locked_until_utc;
                """;
            command.Parameters.AddWithValue("$key", throttle.Key);
            command.Parameters.AddWithValue("$failures", throttle.FailureCount);
            command.Parameters.AddWithValue("$window", FormatAccountSessionStamp(throttle.WindowStartedAt));
            command.Parameters.AddWithValue("$last", FormatAccountSessionStamp(throttle.LastFailureAt));
            command.Parameters.AddWithValue("$locked", throttle.LockedUntil is { } locked
                ? FormatAccountSessionStamp(locked) : DBNull.Value);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException("登录限流行写入丢行");
        }

        using var prune = connection.CreateCommand();
        prune.Transaction = transaction;
        prune.CommandText = "DELETE FROM login_throttles WHERE key NOT IN (SELECT value FROM json_each($keys));";
        prune.Parameters.AddWithValue("$keys", JsonSerializer.Serialize(pending.Keys));
        prune.ExecuteNonQuery();
    }

    private static List<LoginThrottleRow> ReadLoginThrottleRows(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {LoginThrottleColumns} FROM login_throttles ORDER BY key;";
        using var reader = command.ExecuteReader();
        var rows = new List<LoginThrottleRow>();
        while (reader.Read())
        {
            rows.Add(new LoginThrottleRow
            {
                Key = reader.GetString(0),
                FailureCount = reader.GetInt32(1),
                WindowStartedAt = ParseAccountSessionStamp(reader.GetString(2)),
                LastFailureAt = ParseAccountSessionStamp(reader.GetString(3)),
                LockedUntil = reader.IsDBNull(4) ? null : ParseAccountSessionStamp(reader.GetString(4)),
            });
        }
        return rows;
    }

    private static bool LoginThrottleEquals(LoginThrottleRow left, LoginThrottleRow right)
        => string.Equals(left.Key, right.Key, StringComparison.Ordinal)
           && left.FailureCount == right.FailureCount
           && left.WindowStartedAt.UtcTicks == right.WindowStartedAt.UtcTicks
           && left.LastFailureAt.UtcTicks == right.LastFailureAt.UtcTicks
           && Nullable.Equals(left.LockedUntil?.UtcTicks, right.LockedUntil?.UtcTicks);
}
