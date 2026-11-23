using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// B-3 step 2: friends and blocks moved out of the platform snapshot into account_friends and
// account_blocks (schema 14 -> 15). The core snapshot must not carry a second copy, the
// friend/block semantics must not move, and the split must stay recoverable.
public sealed class AccountRelationshipStorageMigrationTests
{
    private const string Password = "password-123";

    [Fact]
    public void SplitMigrationMovesRelationshipsOutOfTheCoreSnapshot()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var (database, friendRows, blockRows, ids) = BuildSchemaFourteenDatabase(path);
            using (var legacy = OpenReadOnly(database))
                Assert.Equal("14", Text(legacy, "SELECT value FROM storage_meta WHERE key='schema_version';"));

            var migrated = new L12PlatformStore(path);
            Assert.Equal("sqlite", migrated.StorageStatus().Mode);
            Assert.Equal(15, migrated.StorageStatus().SchemaVersion);
            Assert.Equal(database + ".pre-account-relationship-split", migrated.StorageStatus().MigrationBackupPath);
            Assert.True(File.Exists(database + ".pre-account-relationship-split"),
                "回滚唯一通道缺失：platform.db.pre-account-relationship-split");

            using (var connection = OpenReadOnly(database))
            {
                Assert.Equal("15", Text(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("15", Text(connection, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal(friendRows, RowCount(connection, "account_friends"));
                Assert.Equal(blockRows, RowCount(connection, "account_blocks"));
            }

            // Behaviour is unchanged: the accepted friend and the block both survive the split.
            Assert.True(migrated.AreFriends(ids.Requester, ids.Addressee));
            Assert.Equal(ids.Addressee, Assert.Single(migrated.Friends(ids.Requester)).AccountId);
            Assert.Equal(ids.Blocked, Assert.Single(migrated.BlockedAccounts(ids.Requester)).AccountId);

            // The first save after the split rewrites the core snapshot without the relationships;
            // the tables keep them, so each domain has exactly one authority.
            Assert.True(migrated.Register("relowner9", Password).Success);
            using (var connection = OpenReadOnly(database))
            {
                var snapshot = Text(connection, "SELECT snapshot_json FROM platform_state;");
                Assert.DoesNotContain("\"Friends\"", snapshot, StringComparison.Ordinal);
                Assert.DoesNotContain("\"BlockedAccounts\"", snapshot, StringComparison.Ordinal);
                Assert.Equal(friendRows, RowCount(connection, "account_friends"));
                Assert.Equal(blockRows, RowCount(connection, "account_blocks"));
            }

            // A second open must not move a single row or rewrite the backup.
            var reopened = new L12PlatformStore(path);
            Assert.Equal(15, reopened.StorageStatus().SchemaVersion);
            Assert.True(reopened.AreFriends(ids.Requester, ids.Addressee));
            using (var connection = OpenReadOnly(database))
            {
                Assert.Equal(friendRows, RowCount(connection, "account_friends"));
                Assert.Equal(blockRows, RowCount(connection, "account_blocks"));
            }
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RelationshipTablesAreTheAuthorityAfterTheSplit()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var (database, _, _, ids) = BuildSchemaFourteenDatabase(path);
            var store = new L12PlatformStore(path);
            Assert.True(store.AreFriends(ids.Requester, ids.Addressee));
            // One save after the split rewrites the core snapshot without the relationships; only
            // then is the snapshot free of the stale copy the table outranks.
            Assert.True(store.Register("relown9x", Password).Success);

            // Clearing the tables out-of-band must be visible on the next open: the rows, not the
            // stale snapshot, decide.
            using (var connection = OpenWritable(database))
            {
                Execute(connection, "DELETE FROM account_friends;");
                Execute(connection, "DELETE FROM account_blocks;");
            }
            var reopened = new L12PlatformStore(path);
            Assert.False(reopened.AreFriends(ids.Requester, ids.Addressee));
            Assert.Empty(reopened.BlockedAccounts(ids.Requester));

            // A new relationship is written back into the tables.
            Assert.True(reopened.SendFriendRequest(ids.Requester, ids.Addressee).Success);
            using (var connection = OpenReadOnly(database))
                Assert.True(RowCount(connection, "account_friends") > 0, "好友关系未写回 account_friends");
        }
        finally { Cleanup(root); }
    }

    // Builds the schema-14 shape a B-3a database has: relationships live in the core snapshot and
    // the tables are empty. Rows are produced by the real friend/block paths.
    private static (string Database, int FriendRows, int BlockRows, (string Requester, string Addressee, string Blocked) Ids)
        BuildSchemaFourteenDatabase(string path)
    {
        var store = new L12PlatformStore(path);
        Assert.True(store.Register("relreq9a1", Password).Success);
        Assert.True(store.Register("reladd9b2", Password).Success);
        Assert.True(store.Register("relblk9c3", Password).Success);
        var requester = store.Login("relreq9a1", Password).Account!.Id;
        var addressee = store.Login("reladd9b2", Password).Account!.Id;
        var blocked = store.Login("relblk9c3", Password).Account!.Id;
        Assert.True(store.SendFriendRequest(requester, addressee).Success);
        Assert.True(store.ResolveFriendRequest(addressee, requester, accept: true).Success);
        Assert.True(store.BlockAccount(requester, blocked).Success);
        var database = store.TransactionalStoragePath;
        using var connection = OpenWritable(database);
        var friends = ReadRows(connection, "account_friends",
            "requester_id,addressee_id,status,created_utc,updated_utc",
            new[] { "RequesterId", "AddresseeId", "Status", "CreatedAt", "UpdatedAt" });
        var blocks = ReadRows(connection, "account_blocks", "account_id,blocked_account_id,created_utc",
            new[] { "AccountId", "BlockedAccountId", "CreatedAt" });
        Assert.NotEmpty(friends);
        Assert.NotEmpty(blocks);
        var snapshot = JsonNode.Parse(Text(connection, "SELECT snapshot_json FROM platform_state;"))!.AsObject();
        snapshot["Friends"] = friends;
        snapshot["BlockedAccounts"] = blocks;
        var json = snapshot.ToJsonString();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE platform_state SET schema_version=14, snapshot_json=$json, snapshot_sha256=$hash
            WHERE singleton_id=1;
            UPDATE storage_meta SET value='14' WHERE key='schema_version';
            DELETE FROM account_friends;
            DELETE FROM account_blocks;
            """;
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$hash", Sha256(json));
        command.ExecuteNonQuery();
        return (database, friends.Count, blocks.Count, (requester, addressee, blocked));
    }

    private static JsonArray ReadRows(SqliteConnection connection, string table, string columns, string[] names)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {columns} FROM {table};";
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var index = 0; index < names.Length; index++)
                row[names[index]] = reader.IsDBNull(index) ? null : reader.GetValue(index).ToString();
            rows.Add(row);
        }
        return rows;
    }

    private static int RowCount(SqliteConnection connection, string table)
        => (int)Scalar(connection, $"SELECT COUNT(*) FROM {table};");

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
        var path = Path.Combine(Path.GetTempPath(), $"l12-relationship-split-{Guid.NewGuid():N}");
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