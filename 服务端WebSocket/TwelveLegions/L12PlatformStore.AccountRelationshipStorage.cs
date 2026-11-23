using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Friends and blocks are account relationships, not account content: they are read through the
    // in-memory lists on every friend/block/mail path, and the core snapshot only needs them once.
    // Moving them into their own tables keeps every save from rewriting the whole relationship set,
    // while the compatibility mirror still serializes them so the JSON fallback stays complete.
    private const string AccountRelationshipSplitBackupSuffix = ".pre-account-relationship-split";

    // `ordinal` preserves the in-memory order: the pair authority is "the first row of an unordered
    // pair wins", so reloading a table sorted by key would silently change which row is first.
    private const string FriendColumns = "requester_id,addressee_id,status,created_utc,updated_utc,ordinal";

    private const string BlockedAccountColumns = "account_id,blocked_account_id,created_utc";

    private static void InitializeAccountRelationshipSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS account_friends (
                requester_id TEXT NOT NULL,
                addressee_id TEXT NOT NULL,
                status TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                PRIMARY KEY (requester_id, addressee_id)
            );
            CREATE INDEX IF NOT EXISTS ix_account_friends_addressee ON account_friends(addressee_id);
            CREATE TABLE IF NOT EXISTS account_blocks (
                account_id TEXT NOT NULL,
                blocked_account_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY (account_id, blocked_account_id)
            );
            CREATE INDEX IF NOT EXISTS ix_account_blocks_blocked ON account_blocks(blocked_account_id);
            """;
        command.ExecuteNonQuery();
    }

    // Runs once per open, after the throttles have been hydrated. The tables are the authority; a
    // snapshot that still carries relationships (schema 14, or a database whose split never
    // committed) is copied into them inside one transaction.
    private DataFile EnsureAndHydrateAccountRelationshipStorage(SqliteConnection connection, DataFile data)
    {
        var storedFriendRows = ReadFriendRows(connection, transaction: null);
        var storedFriends = storedFriendRows.Select(entry => entry.Row).ToList();
        var storedBlocks = ReadBlockedAccountRows(connection, transaction: null);
        var friendsStale = storedFriends.Count == 0 && data.Friends.Count != 0;
        var blocksStale = storedBlocks.Count == 0 && data.BlockedAccounts.Count != 0;
        if (friendsStale || blocksStale)
        {
            // A schema-14 program rejects a schema-15 database while constructing the store, so
            // this copy is the only path back to the previous binary.
            WriteAccountRelationshipSplitBackup();
            using var split = connection.BeginTransaction(deferred: false);
            if (friendsStale) SynchronizeFriendRows(connection, split, data.Friends);
            if (blocksStale) SynchronizeBlockedAccountRows(connection, split, data.BlockedAccounts);
            SetStorageSchemaVersion(connection, split, PlatformStorageSchemaVersion);
            split.Commit();
            return data;
        }

        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            SetStorageSchemaVersion(connection, transaction, PlatformStorageSchemaVersion);
            transaction.Commit();
        }
        if (storedFriends.Count != 0) data.Friends = storedFriends;
        if (storedBlocks.Count != 0) data.BlockedAccounts = storedBlocks;
        return data;
    }

    private void WriteAccountRelationshipSplitBackup()
    {
        var path = _databasePath + AccountRelationshipSplitBackupSuffix;
        _migrationBackupPath = path;
        if (File.Exists(path)) return;
        using var source = OpenDatabase(_databasePath, readOnly: true);
        using var destination = OpenDatabase(path, readOnly: false, initialize: false);
        source.BackupDatabase(destination);
    }

    // Every path that rebuilds in-memory state from a stored snapshot calls this: the core snapshot
    // no longer carries the relationships, so the tables supply them again.
    private static void HydrateAccountRelationshipStorage(SqliteConnection connection, DataFile data,
        SqliteTransaction? transaction)
    {
        var friends = ReadFriendRows(connection, transaction).Select(entry => entry.Row).ToList();
        if (friends.Count != 0) data.Friends = friends;
        var blocks = ReadBlockedAccountRows(connection, transaction);
        if (blocks.Count != 0) data.BlockedAccounts = blocks;
    }

    // Row-level sync inside the save transaction: changed rows are rewritten, removed relationships
    // are deleted by key, so a failed commit leaves the previously committed set untouched.
    private static void SynchronizeFriendRows(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<FriendRow> friends)
    {
        // A relationship without both endpoints cannot form a primary key; the authority
        // computation ignores such rows in memory, so they are not persisted either.
        var pending = new Dictionary<(string, string), FriendRow>();
        var ordinals = new Dictionary<(string, string), int>();
        foreach (var friend in friends)
        {
            if (string.IsNullOrWhiteSpace(friend.RequesterId) || string.IsNullOrWhiteSpace(friend.AddresseeId))
                continue;
            var key = (friend.RequesterId, friend.AddresseeId);
            if (!pending.ContainsKey(key)) ordinals[key] = ordinals.Count;
            pending[key] = friend;
        }
        var present = ReadFriendRows(connection, transaction)
            .ToDictionary(entry => (entry.Row.RequesterId, entry.Row.AddresseeId));
        foreach (var friend in pending.Values)
        {
            var ordinal = ordinals[(friend.RequesterId, friend.AddresseeId)];
            if (present.TryGetValue((friend.RequesterId, friend.AddresseeId), out var previous)
                && FriendEquals(previous.Row, friend) && previous.Ordinal == ordinal) continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO account_friends({FriendColumns})
                VALUES($requester,$addressee,$status,$created,$updated,$ordinal)
                ON CONFLICT(requester_id, addressee_id) DO UPDATE SET
                    status=excluded.status,
                    created_utc=excluded.created_utc,
                    updated_utc=excluded.updated_utc,
                    ordinal=excluded.ordinal;
                """;
            command.Parameters.AddWithValue("$requester", friend.RequesterId);
            command.Parameters.AddWithValue("$addressee", friend.AddresseeId);
            command.Parameters.AddWithValue("$status", friend.Status ?? string.Empty);
            command.Parameters.AddWithValue("$created", FormatAccountSessionStamp(friend.CreatedAt));
            command.Parameters.AddWithValue("$updated", FormatAccountSessionStamp(friend.UpdatedAt));
            command.Parameters.AddWithValue("$ordinal", ordinal);
            if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("好友关系写入丢行");
        }
        foreach (var removed in present.Keys.Where(key => !pending.ContainsKey(key)))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM account_friends WHERE requester_id=$requester AND addressee_id=$addressee;";
            command.Parameters.AddWithValue("$requester", removed.Item1);
            command.Parameters.AddWithValue("$addressee", removed.Item2);
            command.ExecuteNonQuery();
        }
    }

    private static void SynchronizeBlockedAccountRows(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<BlockedAccountRow> blocks)
    {
        var pending = new Dictionary<(string, string), BlockedAccountRow>();
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.AccountId) || string.IsNullOrWhiteSpace(block.BlockedAccountId))
                continue;
            pending[(block.AccountId, block.BlockedAccountId)] = block;
        }
        var present = ReadBlockedAccountRows(connection, transaction)
            .ToDictionary(row => (row.AccountId, row.BlockedAccountId));
        foreach (var block in pending.Values)
        {
            if (present.TryGetValue((block.AccountId, block.BlockedAccountId), out var previous)
                && BlockedAccountEquals(previous, block)) continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO account_blocks({BlockedAccountColumns})
                VALUES($account,$blocked,$created)
                ON CONFLICT(account_id, blocked_account_id) DO UPDATE SET
                    created_utc=excluded.created_utc;
                """;
            command.Parameters.AddWithValue("$account", block.AccountId);
            command.Parameters.AddWithValue("$blocked", block.BlockedAccountId);
            command.Parameters.AddWithValue("$created", FormatAccountSessionStamp(block.CreatedAt));
            if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("黑名单关系写入丢行");
        }
        foreach (var removed in present.Keys.Where(key => !pending.ContainsKey(key)))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM account_blocks WHERE account_id=$account AND blocked_account_id=$blocked;";
            command.Parameters.AddWithValue("$account", removed.Item1);
            command.Parameters.AddWithValue("$blocked", removed.Item2);
            command.ExecuteNonQuery();
        }
    }

    private static List<(FriendRow Row, int Ordinal)> ReadFriendRows(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {FriendColumns} FROM account_friends ORDER BY ordinal, requester_id, addressee_id;";
        using var reader = command.ExecuteReader();
        var rows = new List<(FriendRow Row, int Ordinal)>();
        while (reader.Read())
        {
            rows.Add((new FriendRow
            {
                RequesterId = reader.GetString(0),
                AddresseeId = reader.GetString(1),
                Status = reader.GetString(2),
                CreatedAt = ParseAccountSessionStamp(reader.GetString(3)),
                UpdatedAt = ParseAccountSessionStamp(reader.GetString(4)),
            }, reader.GetInt32(5)));
        }
        return rows;
    }

    private static List<BlockedAccountRow> ReadBlockedAccountRows(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {BlockedAccountColumns} FROM account_blocks ORDER BY account_id, blocked_account_id;";
        using var reader = command.ExecuteReader();
        var rows = new List<BlockedAccountRow>();
        while (reader.Read())
        {
            rows.Add(new BlockedAccountRow
            {
                AccountId = reader.GetString(0),
                BlockedAccountId = reader.GetString(1),
                CreatedAt = ParseAccountSessionStamp(reader.GetString(2)),
            });
        }
        return rows;
    }

    private static bool FriendEquals(FriendRow left, FriendRow right)
        => string.Equals(left.RequesterId, right.RequesterId, StringComparison.Ordinal)
           && string.Equals(left.AddresseeId, right.AddresseeId, StringComparison.Ordinal)
           && string.Equals(left.Status, right.Status, StringComparison.Ordinal)
           && left.CreatedAt.UtcTicks == right.CreatedAt.UtcTicks
           && left.UpdatedAt.UtcTicks == right.UpdatedAt.UtcTicks;

    private static bool BlockedAccountEquals(BlockedAccountRow left, BlockedAccountRow right)
        => string.Equals(left.AccountId, right.AccountId, StringComparison.Ordinal)
           && string.Equals(left.BlockedAccountId, right.BlockedAccountId, StringComparison.Ordinal)
           && left.CreatedAt.UtcTicks == right.CreatedAt.UtcTicks;
}
