using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Sessions live in their own table for the same reason as the audit ledger: the core
    // snapshot must not carry a second copy of a domain that already has one, and the table
    // is what keeps revocation and expiry durable independently of the platform snapshot.
    // The compatibility mirror still serializes them; it stays a complete offline copy.
    private const string AccountSessionSplitBackupSuffix = ".pre-session-split";

    private const string AccountSessionColumns =
        "id,token_hash,account_id,created_utc,expires_utc,revoked_utc,auth_strength,permission_version";

    private static void InitializeAccountSessionSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS account_sessions (
                id TEXT PRIMARY KEY,
                token_hash TEXT NOT NULL,
                account_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                expires_utc TEXT NOT NULL,
                revoked_utc TEXT NULL,
                auth_strength TEXT NOT NULL,
                permission_version INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_account_sessions_account ON account_sessions(account_id);
            CREATE INDEX IF NOT EXISTS ix_account_sessions_expires ON account_sessions(expires_utc);
            """;
        command.ExecuteNonQuery();
    }

    // Runs once per open, after the core snapshot has been read and the deck domain hydrated.
    // The table is the authority; a snapshot that still carries sessions (schema 12, or a
    // database whose split never committed) is copied into it inside one transaction.
    private DataFile EnsureAndHydrateAccountSessionStorage(SqliteConnection connection, DataFile data)
    {
        var stored = ReadAccountSessionRows(connection, transaction: null);
        if (stored.Count == 0 && data.Sessions.Count != 0)
        {
            // A schema-12 program rejects a schema-13 database while constructing the store, so
            // this copy is the only path back to the previous binary.
            WriteAccountSessionSplitBackup();
            using var split = connection.BeginTransaction(deferred: false);
            SynchronizeAccountSessionRows(connection, split, data.Sessions);
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
        data.Sessions = stored;
        if (data.Sessions.Any(row => string.IsNullOrWhiteSpace(row.Id)))
            throw new InvalidDataException("会话 ID 为空或重复");
        return data;
    }

    private void WriteAccountSessionSplitBackup()
    {
        var path = _databasePath + AccountSessionSplitBackupSuffix;
        _migrationBackupPath = path;
        if (File.Exists(path)) return;
        using var source = OpenDatabase(_databasePath, readOnly: true);
        using var destination = OpenDatabase(path, readOnly: false, initialize: false);
        source.BackupDatabase(destination);
    }

    // Every path that rebuilds in-memory state from a stored snapshot calls this: the core
    // snapshot no longer carries the sessions, so the table supplies them again.
    private static void HydrateAccountSessions(SqliteConnection connection, DataFile data,
        SqliteTransaction? transaction)
    {
        var stored = ReadAccountSessionRows(connection, transaction);
        if (stored.Count == 0) return;
        data.Sessions = stored;
    }

    // Row-level sync inside the save transaction: only changed rows are rewritten and a row the
    // in-memory collection no longer holds is deleted, so a failed commit leaves the previously
    // committed session generation untouched.
    private static void SynchronizeAccountSessionRows(SqliteConnection connection,
        SqliteTransaction transaction, IReadOnlyList<SessionRow> sessions)
    {
        var pending = new Dictionary<string, SessionRow>(StringComparer.Ordinal);
        foreach (var session in sessions) pending[session.Id] = session;
        var present = ReadAccountSessionRows(connection, transaction)
            .ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var session in pending.Values)
        {
            if (present.TryGetValue(session.Id, out var previous) && AccountSessionEquals(previous, session)) continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO account_sessions({AccountSessionColumns})
                VALUES($id,$hash,$account,$created,$expires,$revoked,$strength,$version)
                ON CONFLICT(id) DO UPDATE SET
                    token_hash=excluded.token_hash,
                    account_id=excluded.account_id,
                    created_utc=excluded.created_utc,
                    expires_utc=excluded.expires_utc,
                    revoked_utc=excluded.revoked_utc,
                    auth_strength=excluded.auth_strength,
                    permission_version=excluded.permission_version;
                """;
            command.Parameters.AddWithValue("$id", session.Id);
            command.Parameters.AddWithValue("$hash", session.TokenHash);
            command.Parameters.AddWithValue("$account", session.AccountId);
            command.Parameters.AddWithValue("$created", FormatAccountSessionStamp(session.CreatedAt));
            command.Parameters.AddWithValue("$expires", FormatAccountSessionStamp(session.ExpiresAt));
            command.Parameters.AddWithValue("$revoked", session.RevokedAt is { } revoked
                ? FormatAccountSessionStamp(revoked) : DBNull.Value);
            command.Parameters.AddWithValue("$strength", session.AuthStrength);
            command.Parameters.AddWithValue("$version", session.PermissionVersion);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException("会话行写入丢行");
        }

        using var prune = connection.CreateCommand();
        prune.Transaction = transaction;
        prune.CommandText = "DELETE FROM account_sessions WHERE id NOT IN (SELECT value FROM json_each($ids));";
        prune.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(pending.Keys));
        prune.ExecuteNonQuery();
    }

    private static List<SessionRow> ReadAccountSessionRows(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {AccountSessionColumns} FROM account_sessions ORDER BY id;";
        using var reader = command.ExecuteReader();
        var rows = new List<SessionRow>();
        while (reader.Read())
        {
            rows.Add(new SessionRow
            {
                Id = reader.GetString(0),
                TokenHash = reader.GetString(1),
                AccountId = reader.GetString(2),
                CreatedAt = ParseAccountSessionStamp(reader.GetString(3)),
                ExpiresAt = ParseAccountSessionStamp(reader.GetString(4)),
                RevokedAt = reader.IsDBNull(5) ? null : ParseAccountSessionStamp(reader.GetString(5)),
                AuthStrength = reader.GetString(6),
                PermissionVersion = reader.GetInt32(7),
            });
        }
        return rows;
    }

    private static bool AccountSessionEquals(SessionRow left, SessionRow right)
        => string.Equals(left.Id, right.Id, StringComparison.Ordinal)
           && string.Equals(left.TokenHash, right.TokenHash, StringComparison.Ordinal)
           && string.Equals(left.AccountId, right.AccountId, StringComparison.Ordinal)
           && left.CreatedAt.UtcTicks == right.CreatedAt.UtcTicks
           && left.ExpiresAt.UtcTicks == right.ExpiresAt.UtcTicks
           && left.RevokedAt?.UtcTicks == right.RevokedAt?.UtcTicks
           && string.Equals(left.AuthStrength, right.AuthStrength, StringComparison.Ordinal)
           && left.PermissionVersion == right.PermissionVersion;

    private static string FormatAccountSessionStamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseAccountSessionStamp(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
