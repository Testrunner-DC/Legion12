using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// B-2 step 1: sessions moved out of the platform snapshot into account_sessions (schema 12 -> 13).
// The core snapshot must not carry a second copy, the compatibility mirror must stay a complete
// offline copy, and authentication/revocation/expiry semantics must not move at all.
public sealed class SessionStorageMigrationTests
{
    private const string Password = "password-123";

    [Fact]
    public void SplitMigrationIsIdempotentAndKeepsIssuedTokens()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var (token, accountId, database, rows) = BuildSchemaTwelveDatabase(path, "splitowner");
            using (var legacy = OpenReadOnly(database))
                Assert.Equal("12", Text(legacy, "SELECT value FROM storage_meta WHERE key='schema_version';"));

            var migrated = new L12PlatformStore(path);
            Assert.Equal("sqlite", migrated.StorageStatus().Mode);
            // B-3 raised the current schema to 14, so a schema-12 database now lands on 14 in one
            // ladder. The session-split claims of this test (rows, token, backup) are unchanged.
            Assert.Equal(15, migrated.StorageStatus().SchemaVersion);
            Assert.Equal(database + ".pre-session-split", migrated.StorageStatus().MigrationBackupPath);
            Assert.True(File.Exists(database + ".pre-session-split"),
                "回滚唯一通道缺失：platform.db.pre-session-split");
            Assert.Equal(rows, RowCount(database));
            using (var connection = OpenReadOnly(database))
            {
                Assert.Equal("15", Text(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("15", Text(connection,
                    "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            }

            // A token the pre-split program issued still authenticates afterwards.
            var authenticated = migrated.AuthenticateTokenSession(token);
            Assert.NotNull(authenticated);
            Assert.Equal(accountId, authenticated!.Account.Id);
            Assert.Equal(rows, migrated.Sessions(accountId).Count);

            // A second open must not move a single row.
            var reopened = new L12PlatformStore(path);
            Assert.Equal(15, reopened.StorageStatus().SchemaVersion);
            Assert.Equal(rows, RowCount(database));
            Assert.Equal(rows, reopened.Sessions(accountId).Count);
            Assert.NotNull(reopened.AuthenticateTokenSession(token));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RevocationExpiryAndTheActiveSessionCapKeepTheirSemantics()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("splitowner", Password).Success);
            var first = store.Login("splitowner", Password);
            var second = store.Login("splitowner", Password);
            var accountId = first.Account!.Id;
            Assert.Equal(3, store.Sessions(accountId).Count);

            var revoke = store.RevokeOwnSession(Session(store, first.Token!), SessionId(store, first.Token!));
            Assert.Equal(1, revoke.RevokedCount);
            Assert.Null(store.AuthenticateTokenSession(first.Token));
            Assert.Equal(2, store.Sessions(accountId).Count);
            using (var connection = OpenReadOnly(store.TransactionalStoragePath))
                Assert.Equal(1, Scalar(connection,
                    $"SELECT COUNT(*) FROM account_sessions WHERE revoked_utc IS NOT NULL AND account_id='{accountId}';"));

            var reopened = new L12PlatformStore(path);
            Assert.Null(reopened.AuthenticateTokenSession(first.Token));
            Assert.NotNull(reopened.AuthenticateTokenSession(second.Token!));
            Assert.Equal(2, reopened.Sessions(accountId).Count);

            // Expiry is still decided by the stored instant, not by any snapshot fact.
            var expiring = SessionId(reopened, second.Token!);
            using (var connection = OpenWritable(reopened.TransactionalStoragePath))
                Execute(connection,
                    $"UPDATE account_sessions SET expires_utc='2020-01-01T00:00:00.0000000+00:00' WHERE id='{expiring}';");
            var expired = new L12PlatformStore(path);
            Assert.Null(expired.AuthenticateTokenSession(second.Token));
            Assert.Single(expired.Sessions(accountId));

            // Four active sessions stay the ceiling, and the table keeps exactly those four.
            Assert.True(expired.Register("capowner", Password).Success);
            var capAccount = expired.Login("capowner", Password).Account!;
            for (var index = 0; index < 5; index++) Assert.True(expired.Login("capowner", Password).Success);
            Assert.Equal(4, expired.Sessions(capAccount.Id).Count);
            using var final = OpenReadOnly(expired.TransactionalStoragePath);
            Assert.Equal(4, Scalar(final,
                $"SELECT COUNT(*) FROM account_sessions WHERE account_id='{capAccount.Id}';"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RestartHydratesSessionsFromTheirOwnTable()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("hydrateown", Password).Success);
            var login = store.Login("hydrateown", Password);
            Assert.True(login.Success);
            var sessionId = SessionId(store, login.Token!);
            var accountId = login.Account!.Id;
            using (var connection = OpenReadOnly(store.TransactionalStoragePath))
            {
                Assert.DoesNotContain("\"Sessions\"", Text(connection, "SELECT snapshot_json FROM platform_state;"));
                Assert.Equal(1, Scalar(connection,
                    $"SELECT COUNT(*) FROM account_sessions WHERE id='{sessionId}';"));
            }

            var reloaded = new L12PlatformStore(path);
            Assert.True(reloaded.IsSessionActive(sessionId));
            var authenticated = reloaded.AuthenticateTokenSession(login.Token);
            Assert.NotNull(authenticated);
            Assert.Equal(sessionId, authenticated!.SessionId);
            Assert.Equal(2, reloaded.Sessions(accountId).Count);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void CommitFailureKeepsTheCommittedSessionGenerationAndTheRetrySucceeds()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("retryowner", Password).Success);
            var login = store.Login("retryowner", Password);
            var sessionId = SessionId(store, login.Token!);

            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("synthetic-before-commit");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.RevokeOwnSession(Session(store, login.Token!), sessionId));
            store.StorageFailureInjector = null;

            using (var connection = OpenReadOnly(store.TransactionalStoragePath))
                Assert.Equal(0, Scalar(connection,
                    "SELECT COUNT(*) FROM account_sessions WHERE revoked_utc IS NOT NULL;"));
            var afterFailure = new L12PlatformStore(path);
            Assert.NotNull(afterFailure.AuthenticateTokenSession(login.Token));

            var retried = afterFailure.RevokeOwnSession(Session(afterFailure, login.Token!), sessionId);
            Assert.Equal(1, retried.RevokedCount);
            Assert.Null(afterFailure.AuthenticateTokenSession(login.Token));
            using (var connection = OpenReadOnly(afterFailure.TransactionalStoragePath))
                Assert.Equal(1, Scalar(connection,
                    $"SELECT COUNT(*) FROM account_sessions WHERE id='{sessionId}' AND revoked_utc IS NOT NULL;"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void FailedSplitCommittedNothingAndTheRetryStillMigrates()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var (token, accountId, database, rows) = BuildSchemaTwelveDatabase(path, "splitowner");
            using (var connection = OpenWritable(database))
                Execute(connection, """
                    CREATE TRIGGER fail_session_split BEFORE INSERT ON account_sessions
                    BEGIN SELECT RAISE(ABORT,'synthetic-session-split-failure'); END;
                    """);

            // The open fails out (or degrades to the complete JSON mirror); either way the
            // database must still be the untouched schema-12 generation.
            try { _ = new L12PlatformStore(path); }
            catch (L12PlatformStorageUnavailableException) { }
            Assert.True(File.Exists(database + ".pre-session-split"));
            using (var connection = OpenWritable(database))
            {
                Assert.Equal("12", Text(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("12", Text(connection,
                    "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM account_sessions;"));
            }

            using (var connection = OpenWritable(database))
                Execute(connection, "DROP TRIGGER fail_session_split;");
            var retried = new L12PlatformStore(path);
            Assert.Equal("sqlite", retried.StorageStatus().Mode);
            Assert.Equal(15, retried.StorageStatus().SchemaVersion);
            Assert.Equal(rows, RowCount(database));
            var authenticated = retried.AuthenticateTokenSession(token);
            Assert.NotNull(authenticated);
            Assert.Equal(accountId, authenticated!.Account.Id);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void CoreSnapshotDropsTheSessionsAndTheMirrorKeepsThem()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("mirrorown", Password).Success);
            Assert.True(store.Login("mirrorown", Password).Success);
            var rows = RowCount(store.TransactionalStoragePath);
            Assert.True(rows >= 2, $"account_sessions rows={rows}");

            using (var connection = OpenReadOnly(store.TransactionalStoragePath))
            {
                var snapshot = Text(connection, "SELECT snapshot_json FROM platform_state;");
                Assert.DoesNotContain("\"Sessions\"", snapshot);
                Assert.Contains("\"Accounts\"", snapshot);
                Assert.Equal(rows, Scalar(connection, "SELECT COUNT(*) FROM account_sessions;"));
            }
            // The compatibility mirror stays a complete offline copy of the platform.
            var mirror = File.ReadAllText(path);
            Assert.Contains("\"Sessions\"", mirror);
            Assert.Contains("\"Accounts\"", mirror);
        }
        finally { Cleanup(root); }
    }

    // Builds the schema-12 shape the previous program wrote: the same session rows, still held
    // inside the platform snapshot, with an empty account_sessions table.
    private static (string Token, string AccountId, string Database, int Rows) BuildSchemaTwelveDatabase(
        string path, string username)
    {
        var store = new L12PlatformStore(path);
        Assert.True(store.Register(username, Password).Success);
        var login = store.Login(username, Password);
        Assert.True(login.Success);
        var database = store.TransactionalStoragePath;
        using var connection = OpenWritable(database);
        var sessions = ReadSessionRows(connection);
        var snapshot = JsonNode.Parse(Text(connection, "SELECT snapshot_json FROM platform_state;"))!.AsObject();
        snapshot["Sessions"] = sessions;
        var json = snapshot.ToJsonString();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE platform_state SET schema_version=12, snapshot_json=$json, snapshot_sha256=$hash
            WHERE singleton_id=1;
            UPDATE storage_meta SET value='12' WHERE key='schema_version';
            DELETE FROM account_sessions;
            """;
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$hash", Sha256(json));
        command.ExecuteNonQuery();
        return (login.Token!, login.Account!.Id, database, sessions.Count);
    }

    private static JsonArray ReadSessionRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,token_hash,account_id,created_utc,expires_utc,revoked_utc,auth_strength,permission_version
            FROM account_sessions ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read())
        {
            rows.Add(new JsonObject
            {
                ["Id"] = reader.GetString(0),
                ["TokenHash"] = reader.GetString(1),
                ["AccountId"] = reader.GetString(2),
                ["CreatedAt"] = reader.GetString(3),
                ["ExpiresAt"] = reader.GetString(4),
                ["RevokedAt"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                ["AuthStrength"] = reader.GetString(6),
                ["PermissionVersion"] = reader.GetInt32(7),
            });
        }
        return rows;
    }

    private static L12AuthenticatedSession Session(L12PlatformStore store, string token)
        => store.AuthenticateTokenSession(token)
           ?? throw new InvalidOperationException("测试会话不可认证");

    private static string SessionId(L12PlatformStore store, string token) => Session(store, token).SessionId;

    private static string Sha256(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static int RowCount(string database)
    {
        using var connection = OpenReadOnly(database);
        return (int)Scalar(connection, "SELECT COUNT(*) FROM account_sessions;");
    }

    private static SqliteConnection OpenReadOnly(string path) => Open(path, SqliteOpenMode.ReadOnly);

    private static SqliteConnection OpenWritable(string path) => Open(path, SqliteOpenMode.ReadWrite);

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
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

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-session-split-{Guid.NewGuid():N}");
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
