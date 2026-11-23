using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// B-3 step 1: login throttles moved out of the platform snapshot into login_throttles
// (schema 13 -> 14). The core snapshot must not carry a second copy, the throttle semantics
// (window, lock, prune) must not move, and the split must stay recoverable.
public sealed class LoginThrottleStorageMigrationTests
{
    private const string Password = "password-123";

    [Fact]
    public void SplitMigrationMovesThrottlesOutOfTheCoreSnapshot()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var (database, rows, username) = BuildSchemaThirteenDatabase(path);
            using (var legacy = OpenReadOnly(database))
                Assert.Equal("13", Text(legacy, "SELECT value FROM storage_meta WHERE key='schema_version';"));

            var migrated = new L12PlatformStore(path);
            Assert.Equal("sqlite", migrated.StorageStatus().Mode);
            Assert.Equal(15, migrated.StorageStatus().SchemaVersion);
            Assert.Equal(database + ".pre-login-throttle-split", migrated.StorageStatus().MigrationBackupPath);
            Assert.True(File.Exists(database + ".pre-login-throttle-split"),
                "回滚唯一通道缺失：platform.db.pre-login-throttle-split");

            using (var connection = OpenReadOnly(database))
            {
                Assert.Equal("15", Text(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("15", Text(connection, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal(rows, ThrottleRowCount(connection));
            }

            // The lock the pre-split program recorded still throttles the same account.
            Assert.Equal("login_rate_limited", migrated.Login(username, "wrong-password").Code);

            // The first save after the split rewrites the core snapshot without the throttles; the
            // table keeps them, so the domain has exactly one authority.
            Assert.True(migrated.Register("thrtl9b2", Password).Success);
            using (var connection = OpenReadOnly(database))
            {
                Assert.DoesNotContain("LoginThrottles",
                    Text(connection, "SELECT snapshot_json FROM platform_state;"), StringComparison.Ordinal);
                Assert.Equal(rows, ThrottleRowCount(connection));
            }

            // A second open must not move a single row or rewrite the backup.
            var reopened = new L12PlatformStore(path);
            Assert.Equal(15, reopened.StorageStatus().SchemaVersion);
            using (var connection = OpenReadOnly(database))
                Assert.Equal(rows, ThrottleRowCount(connection));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void TheTableIsTheAuthorityAfterTheSplit()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var (database, rows, username) = BuildSchemaThirteenDatabase(path);
            Assert.True(rows > 0);
            var store = new L12PlatformStore(path);
            Assert.Equal("login_rate_limited", store.Login(username, "wrong-password").Code);

            // Clearing the table out-of-band (a prune or an administrative clear) must unlock the
            // account on the next open: the rows, not the stale snapshot, decide.
            using (var connection = OpenWritable(database)) Execute(connection, "DELETE FROM login_throttles;");
            var reopened = new L12PlatformStore(path);
            Assert.True(reopened.Login(username, Password).Success);

            // A new failure is written into the table again.
            Assert.Equal("authentication_failed", reopened.Login(username, "wrong-password").Code);
            using (var connection = OpenReadOnly(database))
                Assert.True(ThrottleRowCount(connection) > 0, "失败记录未写回 login_throttles");
        }
        finally { Cleanup(root); }
    }

    // Builds the schema-13 shape a B-2 database has: throttles live in the core snapshot and the
    // table does not exist yet. Rows are produced by real failed logins so the key format and the
    // window/lock timestamps are the ones the production code writes.
    private static (string Database, int Rows, string Username) BuildSchemaThirteenDatabase(string path)
    {
        const string username = "thrtl9a1";
        var store = new L12PlatformStore(path);
        Assert.True(store.Register(username, Password).Success);
        for (var attempt = 0; attempt < 5; attempt++) store.Login(username, "wrong-password");
        var database = store.TransactionalStoragePath;
        using var connection = OpenWritable(database);
        var throttles = ReadThrottleRows(connection);
        Assert.NotEmpty(throttles);
        var snapshot = JsonNode.Parse(Text(connection, "SELECT snapshot_json FROM platform_state;"))!.AsObject();
        snapshot["LoginThrottles"] = throttles;
        var json = snapshot.ToJsonString();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE platform_state SET schema_version=13, snapshot_json=$json, snapshot_sha256=$hash
            WHERE singleton_id=1;
            UPDATE storage_meta SET value='13' WHERE key='schema_version';
            DELETE FROM login_throttles;
            """;
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$hash", Sha256(json));
        command.ExecuteNonQuery();
        return (database, throttles.Count, username);
    }

    private static JsonArray ReadThrottleRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key,failure_count,window_started_utc,last_failure_utc,locked_until_utc
            FROM login_throttles ORDER BY key;
            """;
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read())
        {
            rows.Add(new JsonObject
            {
                ["Key"] = reader.GetString(0),
                ["FailureCount"] = reader.GetInt32(1),
                ["WindowStartedAt"] = reader.GetString(2),
                ["LastFailureAt"] = reader.GetString(3),
                ["LockedUntil"] = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }
        return rows;
    }

    private static int ThrottleRowCount(SqliteConnection connection)
        => (int)Scalar(connection, "SELECT COUNT(*) FROM login_throttles;");

    private static string Sha256(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-throttle-split-{Guid.NewGuid():N}");
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
