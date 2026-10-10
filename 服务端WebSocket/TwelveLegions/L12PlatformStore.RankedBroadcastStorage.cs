using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private const int RankedBroadcastObjectSchemaVersion = 9;
    private const string RankedBroadcastObjectStateKey = "ranked_broadcast_object_state";
    private const string RankedBroadcastObjectActiveState = "active-v1";
    private const string RankedBroadcastGenerationKey = "ranked_broadcast_generation";
    private const string RankedBroadcastCutoverKey = "ranked_broadcast_delivery_cutover";
    private const string RankedBroadcastSchemaEightPreviousMirrorHashKey =
        "ranked_broadcast_schema8_previous_mirror_sha256";
    private static readonly TimeSpan RankedBroadcastClaimLease = TimeSpan.FromSeconds(45);

    private static void InitializeRankedBroadcastObjectStorage(SqliteConnection connection,
        int? existingSchemaVersion)
    {
        if (existingSchemaVersion == RankedBroadcastObjectSchemaVersion)
        {
            AssertRankedBroadcastObjectSchema(connection, transaction: null);
            ValidateRankedBroadcastObjectRows(connection, transaction: null);
            return;
        }
        if (existingSchemaVersion != CompactDeckPayloadSchemaVersion)
            throw new L12PlatformStorageIncompatibleException(
                $"排位广播对象存储不支持版本：{existingSchemaVersion?.ToString() ?? "null"}");

        using var transaction = connection.BeginTransaction(deferred: false);
        CreateRankedBroadcastObjectTables(connection, transaction);
        AssertRankedBroadcastMigrationTargetEmpty(connection, transaction);

        var snapshot = ReadSnapshot(connection, transaction);
        var data = snapshot is null
            ? new DataFile { BusinessVersion = 0 }
            : DeserializeDataAndValidate(ValidateSnapshot(snapshot.Value));
        ValidateLegacyRankedBroadcastProjection(data);

        var maximumRowGeneration = data.RankedBroadcasts.Select(row => row.Generation)
            .Concat(data.RankedBroadcastDeliveries.Select(row => row.Generation))
            .DefaultIfEmpty(0).Max();
        var generation = Math.Max(data.RankedBroadcastGeneration, maximumRowGeneration);
        if (generation == 0 && (data.RankedBroadcasts.Count != 0
                               || data.RankedBroadcastDeliveries.Count != 0
                               || data.RankedBroadcastDeliveryCutover is not null))
            generation = 1;

        foreach (var row in data.RankedBroadcasts)
        {
            var rowGeneration = NormalizeMigratedRowGeneration(row.Generation, generation);
            InsertRankedBroadcastDefinition(connection, transaction, row, rowGeneration);
        }
        foreach (var row in data.RankedBroadcastDeliveries)
        {
            var rowGeneration = NormalizeMigratedRowGeneration(row.Generation, generation);
            InsertRankedBroadcastDelivery(connection, transaction, row, rowGeneration);
        }

        SetStorageMeta(connection, transaction, RankedBroadcastObjectStateKey,
            RankedBroadcastObjectActiveState);
        SetStorageMeta(connection, transaction, RankedBroadcastGenerationKey,
            generation.ToString(CultureInfo.InvariantCulture));
        SetStorageMeta(connection, transaction, RankedBroadcastCutoverKey,
            FormatRankedBroadcastCutover(data.RankedBroadcastDeliveryCutover));
        SetStorageSchemaVersion(connection, transaction, RankedBroadcastObjectSchemaVersion);
        ValidateRankedBroadcastObjectRows(connection, transaction);
        transaction.Commit();
    }

    private static void AssertRankedBroadcastMigrationTargetEmpty(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM ranked_broadcast_definitions),
              (SELECT COUNT(*) FROM ranked_broadcast_deliveries);
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != 0 || reader.GetInt64(1) != 0
            || reader.Read())
            throw new L12PlatformStorageIncompatibleException("排位广播迁移目标表不为空");
        reader.Close();
        if (ReadMeta(connection, RankedBroadcastObjectStateKey, transaction) is not null
            || ReadMeta(connection, RankedBroadcastGenerationKey, transaction) is not null
            || ReadMeta(connection, RankedBroadcastCutoverKey, transaction) is not null)
            throw new L12PlatformStorageIncompatibleException("排位广播迁移目标元数据已存在");
    }

    private static string ValidateSnapshot((string Json, string Checksum) snapshot)
    {
        if (!FixedEquals(snapshot.Checksum, Sha256(snapshot.Json)))
            throw new InvalidDataException("排位广播迁移的平台快照校验和不匹配");
        return snapshot.Json;
    }

    private static long NormalizeMigratedRowGeneration(long rowGeneration, long generation)
    {
        if (rowGeneration < 0 || rowGeneration > generation)
            throw new InvalidDataException("排位广播迁移行代次无效");
        return rowGeneration == 0 ? generation : rowGeneration;
    }

    private static void CreateRankedBroadcastObjectTables(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ranked_broadcast_definitions (
                broadcast_id TEXT NOT NULL COLLATE NOCASE PRIMARY KEY,
                match_id TEXT NOT NULL COLLATE NOCASE,
                event_type TEXT NOT NULL COLLATE NOCASE,
                message TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                row_generation INTEGER NOT NULL CHECK(row_generation >= 0),
                row_digest TEXT NOT NULL,
                UNIQUE(match_id,event_type)
            );
            CREATE TABLE IF NOT EXISTS ranked_broadcast_deliveries (
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
            CREATE INDEX IF NOT EXISTS ix_ranked_broadcast_definitions_delivery
                ON ranked_broadcast_definitions(created_utc,broadcast_id);
            CREATE INDEX IF NOT EXISTS ix_ranked_broadcast_deliveries_broadcast
                ON ranked_broadcast_deliveries(broadcast_id);
            """;
        command.ExecuteNonQuery();
        AssertRankedBroadcastTableShape(connection, transaction, "ranked_broadcast_definitions",
            "broadcast_id", "match_id", "event_type", "message", "created_utc", "row_generation", "row_digest");
        AssertRankedBroadcastTableShape(connection, transaction, "ranked_broadcast_deliveries",
            "account_id", "broadcast_id", "claim_token", "lease_expires_utc", "completed_utc",
            "row_generation", "row_digest");
    }

    private static void AssertRankedBroadcastObjectSchema(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        AssertRankedBroadcastTableShape(connection, transaction, "ranked_broadcast_definitions",
            "broadcast_id", "match_id", "event_type", "message", "created_utc", "row_generation", "row_digest");
        AssertRankedBroadcastTableShape(connection, transaction, "ranked_broadcast_deliveries",
            "account_id", "broadcast_id", "claim_token", "lease_expires_utc", "completed_utc",
            "row_generation", "row_digest");
        if (!string.Equals(ReadMeta(connection, RankedBroadcastObjectStateKey, transaction),
                RankedBroadcastObjectActiveState, StringComparison.Ordinal))
            throw new L12PlatformStorageIncompatibleException("排位广播对象状态缺失或未知");
        _ = ReadRankedBroadcastGeneration(connection, transaction);
        _ = ReadRankedBroadcastCutover(connection, transaction);
    }

    private static void AssertRankedBroadcastTableShape(SqliteConnection connection,
        SqliteTransaction? transaction, string table, params string[] expectedColumns)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        var columns = new List<(string Name, int PrimaryKeyOrder)>();
        while (reader.Read()) columns.Add((reader.GetString(1), reader.GetInt32(5)));
        reader.Close();
        if (!columns.Select(column => column.Name)
                .SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase))
            throw new L12PlatformStorageIncompatibleException($"排位广播对象表 {table} 结构无效");
        var expectedPrimaryKey = table == "ranked_broadcast_definitions"
            ? new[] { "broadcast_id" }
            : new[] { "account_id", "broadcast_id" };
        var actualPrimaryKey = columns.Where(column => column.PrimaryKeyOrder > 0)
            .OrderBy(column => column.PrimaryKeyOrder).Select(column => column.Name).ToArray();
        if (!actualPrimaryKey.SequenceEqual(expectedPrimaryKey, StringComparer.OrdinalIgnoreCase))
            throw new L12PlatformStorageIncompatibleException($"排位广播对象表 {table} 主键无效");
        if (table == "ranked_broadcast_definitions"
            && !HasRankedBroadcastUniqueIndex(connection, transaction, table,
                "match_id", "event_type"))
            throw new L12PlatformStorageIncompatibleException("排位广播定义缺少对局事件唯一约束");
        if (table == "ranked_broadcast_deliveries")
            AssertRankedBroadcastDeliveryForeignKey(connection, transaction);
    }

    private static bool HasRankedBroadcastUniqueIndex(SqliteConnection connection,
        SqliteTransaction? transaction, string table, params string[] expectedColumns)
    {
        var indexes = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA index_list({table});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (reader.GetInt32(2) != 0) indexes.Add(reader.GetString(1));
        }
        foreach (var index in indexes)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA index_info(\"{index.Replace("\"", "\"\"")}\");";
            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read()) columns.Add(reader.GetString(2));
            if (columns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void AssertRankedBroadcastDeliveryForeignKey(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_list(ranked_broadcast_deliveries);";
        using var reader = command.ExecuteReader();
        var references = new List<(string Table, string From, string To, string OnDelete)>();
        while (reader.Read()) references.Add((reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(6)));
        if (references.Count != 1
            || !string.Equals(references[0].Table, "ranked_broadcast_definitions", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(references[0].From, "broadcast_id", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(references[0].To, "broadcast_id", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(references[0].OnDelete, "CASCADE", StringComparison.OrdinalIgnoreCase))
            throw new L12PlatformStorageIncompatibleException("排位广播投递外键约束无效");
    }

    private static void ValidateRankedBroadcastObjectRows(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        var generation = ReadRankedBroadcastGeneration(connection, transaction);
        var definitions = ReadRankedBroadcastDefinitions(connection, transaction, generation);
        var deliveries = ReadRankedBroadcastDeliveries(connection, transaction, generation);
        var definitionIds = definitions.Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (definitions.Count > 300
            || definitions.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1)
            || definitions.GroupBy(row => $"{row.MatchId}\n{row.EventType}",
                    StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)
            || deliveries.GroupBy(row => $"{row.AccountId}\n{row.BroadcastId}",
                    StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)
            || deliveries.Any(row => !definitionIds.Contains(row.BroadcastId))
            || generation == 0 && (definitions.Count != 0 || deliveries.Count != 0
                                   || ReadRankedBroadcastCutover(connection, transaction) is not null))
            throw new InvalidDataException("排位广播对象唯一性、引用或代次无效");
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.Transaction = transaction;
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        using var reader = foreignKeys.ExecuteReader();
        if (reader.Read()) throw new InvalidDataException("排位广播对象外键校验失败");
    }

    private static void ValidateLegacyRankedBroadcastProjection(DataFile data)
    {
        var maximumRowGeneration = data.RankedBroadcasts.Select(row => row.Generation)
            .Concat(data.RankedBroadcastDeliveries.Select(row => row.Generation))
            .DefaultIfEmpty(0).Max();
        if (data.RankedBroadcastGeneration < 0
            || maximumRowGeneration > data.RankedBroadcastGeneration
            || data.RankedBroadcasts.Count > 300
            || data.RankedBroadcasts.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1)
            || data.RankedBroadcasts.GroupBy(row => $"{row.MatchId}\n{row.EventType}",
                    StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1)
            || data.RankedBroadcasts.Any(row => string.IsNullOrWhiteSpace(row.MatchId)
                || string.IsNullOrWhiteSpace(row.EventType) || string.IsNullOrWhiteSpace(row.Message)
                || row.CreatedAt == default || row.Generation < 0)
            || data.RankedBroadcastDeliveries.Any(row => string.IsNullOrWhiteSpace(row.AccountId)
                || string.IsNullOrWhiteSpace(row.BroadcastId) || string.IsNullOrWhiteSpace(row.ClaimToken)
                || row.LeaseExpiresAt == default || row.Generation < 0))
            throw new InvalidDataException("排位广播旧投影无效");
    }

    private static long ReadRankedBroadcastGeneration(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        var value = ReadMeta(connection, RankedBroadcastGenerationKey, transaction)
            ?? throw new L12PlatformStorageIncompatibleException("排位广播全局代次缺失");
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            || generation < 0)
            throw new InvalidDataException("排位广播全局代次无效");
        return generation;
    }

    private static DateTimeOffset? ReadRankedBroadcastCutover(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        var value = ReadMeta(connection, RankedBroadcastCutoverKey, transaction)
            ?? throw new L12PlatformStorageIncompatibleException("排位广播切点元数据缺失");
        if (value.Length == 0) return null;
        if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var cutover))
            throw new InvalidDataException("排位广播切点无效");
        return cutover.ToUniversalTime();
    }

    private static string FormatRankedBroadcastCutover(DateTimeOffset? cutover)
        => cutover?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static List<RankedBroadcastRow> ReadRankedBroadcastDefinitions(
        SqliteConnection connection, SqliteTransaction? transaction, long generation)
    {
        var rows = new List<RankedBroadcastRow>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT broadcast_id,match_id,event_type,message,created_utc,row_generation,row_digest
            FROM ranked_broadcast_definitions ORDER BY created_utc,broadcast_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var created = ParseRankedBroadcastDate(reader.GetString(4), "定义创建时间");
            var rowGeneration = reader.GetInt64(5);
            var expected = RankedBroadcastDefinitionDigest(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), created, rowGeneration);
            if (rowGeneration <= 0 || rowGeneration > generation
                || !FixedEquals(reader.GetString(6), expected))
                throw new InvalidDataException("排位广播定义代次或摘要无效");
            rows.Add(new RankedBroadcastRow
            {
                Id = reader.GetString(0),
                MatchId = reader.GetString(1),
                EventType = reader.GetString(2),
                Message = reader.GetString(3),
                CreatedAt = created,
                Generation = rowGeneration,
            });
        }
        return rows;
    }

    private static List<RankedBroadcastDeliveryRow> ReadRankedBroadcastDeliveries(
        SqliteConnection connection, SqliteTransaction? transaction, long generation)
    {
        var rows = new List<RankedBroadcastDeliveryRow>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT account_id,broadcast_id,claim_token,lease_expires_utc,completed_utc,
                   row_generation,row_digest
            FROM ranked_broadcast_deliveries ORDER BY account_id,broadcast_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var lease = ParseRankedBroadcastDate(reader.GetString(3), "领取租约时间");
            var completed = reader.IsDBNull(4)
                ? (DateTimeOffset?)null
                : ParseRankedBroadcastDate(reader.GetString(4), "投递完成时间");
            var rowGeneration = reader.GetInt64(5);
            var expected = RankedBroadcastDeliveryDigest(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), lease, completed, rowGeneration);
            if (rowGeneration <= 0 || rowGeneration > generation
                || string.IsNullOrWhiteSpace(reader.GetString(2))
                || !FixedEquals(reader.GetString(6), expected))
                throw new InvalidDataException("排位广播投递代次或摘要无效");
            rows.Add(new RankedBroadcastDeliveryRow
            {
                AccountId = reader.GetString(0),
                BroadcastId = reader.GetString(1),
                ClaimToken = reader.GetString(2),
                LeaseExpiresAt = lease,
                CompletedAt = completed,
                Generation = rowGeneration,
            });
        }
        return rows;
    }

    private static DateTimeOffset ParseRankedBroadcastDate(string value, string field)
    {
        if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidDataException($"排位广播{field}无效");
        return parsed.ToUniversalTime();
    }

    private static void HydrateRankedBroadcastObjects(SqliteConnection connection, DataFile data,
        SqliteTransaction? transaction = null)
    {
        AssertRankedBroadcastObjectSchema(connection, transaction);
        var generation = ReadRankedBroadcastGeneration(connection, transaction);
        var broadcasts = ReadRankedBroadcastDefinitions(connection, transaction, generation);
        var deliveries = ReadRankedBroadcastDeliveries(connection, transaction, generation);
        var broadcastIds = broadcasts.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accountIds = data.Accounts.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (broadcasts.Count > 300
            || deliveries.Any(row => !broadcastIds.Contains(row.BroadcastId)
                                     || !accountIds.Contains(row.AccountId)))
            throw new InvalidDataException("排位广播对象投影引用无效");
        data.RankedBroadcasts = broadcasts;
        data.RankedBroadcastDeliveries = deliveries;
        data.RankedBroadcastGeneration = generation;
        data.RankedBroadcastDeliveryCutover = ReadRankedBroadcastCutover(connection, transaction);
    }

    private static void SynchronizeRankedBroadcastObjectsForFullSave(SqliteConnection connection,
        SqliteTransaction transaction, DataFile data, bool allowLegacyDeliveryImport = false)
    {
        AssertRankedBroadcastObjectSchema(connection, transaction);
        ValidateLegacyRankedBroadcastProjection(data);
        var currentGeneration = ReadRankedBroadcastGeneration(connection, transaction);
        var current = ReadRankedBroadcastDefinitions(connection, transaction, currentGeneration);
        var currentDeliveries = ReadRankedBroadcastDeliveries(connection, transaction, currentGeneration);
        var desired = data.RankedBroadcasts.Select(CloneRankedBroadcast).ToArray();
        var currentById = current.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        var desiredById = desired.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in desiredById)
        {
            if (currentById.TryGetValue(pair.Key, out var existing)
                && !RankedBroadcastDefinitionEquals(existing, pair.Value))
                throw new InvalidDataException("已提交的排位广播定义不得就地改写");
        }

        var removed = currentById.Keys.Except(desiredById.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        var added = desired.Where(row => !currentById.ContainsKey(row.Id)).ToArray();
        var currentDeliveryKeys = currentDeliveries.Select(RankedBroadcastDeliveryKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedDeliveries = data.RankedBroadcastDeliveries
            .Where(row => !currentDeliveryKeys.Contains(RankedBroadcastDeliveryKey(row))).ToArray();
        if (addedDeliveries.Length != 0 && !allowLegacyDeliveryImport)
            throw new InvalidDataException("排位广播旧投影不得创建对象投递");
        var desiredCutover = data.RankedBroadcastDeliveryCutover?.ToUniversalTime();
        var currentCutover = ReadRankedBroadcastCutover(connection, transaction);
        var changed = removed.Length != 0 || added.Length != 0 || addedDeliveries.Length != 0
                      || desiredCutover != currentCutover;
        if (changed)
        {
            var requestedGeneration = allowLegacyDeliveryImport
                ? data.RankedBroadcasts.Select(row => row.Generation)
                    .Concat(data.RankedBroadcastDeliveries.Select(row => row.Generation))
                    .Append(data.RankedBroadcastGeneration).DefaultIfEmpty(0).Max()
                : 0;
            var nextGeneration = Math.Max(checked(currentGeneration + 1), requestedGeneration);
            foreach (var id in removed)
            {
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM ranked_broadcast_definitions WHERE broadcast_id=$id;";
                delete.Parameters.AddWithValue("$id", id);
                if (delete.ExecuteNonQuery() != 1)
                    throw new InvalidDataException("排位广播定义删除丢行");
            }
            foreach (var row in added)
                InsertRankedBroadcastDefinition(connection, transaction, row,
                    allowLegacyDeliveryImport && row.Generation > 0 ? row.Generation : nextGeneration);
            foreach (var row in addedDeliveries)
                InsertRankedBroadcastDelivery(connection, transaction, row,
                    allowLegacyDeliveryImport && row.Generation > 0 ? row.Generation : nextGeneration);
            SetStorageMeta(connection, transaction, RankedBroadcastCutoverKey,
                FormatRankedBroadcastCutover(desiredCutover));
            AdvanceRankedBroadcastGeneration(connection, transaction, currentGeneration, nextGeneration);
        }

        HydrateRankedBroadcastObjects(connection, data, transaction);
    }

    private static string RankedBroadcastDeliveryKey(RankedBroadcastDeliveryRow row)
        => $"{row.AccountId}\n{row.BroadcastId}";

    private static bool RankedBroadcastDefinitionEquals(RankedBroadcastRow first,
        RankedBroadcastRow second)
        => string.Equals(first.MatchId, second.MatchId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(first.EventType, second.EventType, StringComparison.OrdinalIgnoreCase)
           && string.Equals(first.Message, second.Message, StringComparison.Ordinal)
           && first.CreatedAt.ToUniversalTime() == second.CreatedAt.ToUniversalTime();

    private static RankedBroadcastRow CloneRankedBroadcast(RankedBroadcastRow row) => new()
    {
        Id = row.Id,
        MatchId = row.MatchId,
        EventType = row.EventType,
        Message = row.Message,
        CreatedAt = row.CreatedAt.ToUniversalTime(),
        Generation = row.Generation,
    };

    private static void InsertRankedBroadcastDefinition(SqliteConnection connection,
        SqliteTransaction transaction, RankedBroadcastRow row, long generation)
    {
        var created = row.CreatedAt.ToUniversalTime();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ranked_broadcast_definitions(
                broadcast_id,match_id,event_type,message,created_utc,row_generation,row_digest)
            VALUES($id,$match,$event,$message,$created,$generation,$digest);
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$match", row.MatchId);
        command.Parameters.AddWithValue("$event", row.EventType);
        command.Parameters.AddWithValue("$message", row.Message);
        command.Parameters.AddWithValue("$created", created.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$generation", generation);
        command.Parameters.AddWithValue("$digest", RankedBroadcastDefinitionDigest(row.Id, row.MatchId,
            row.EventType, row.Message, created, generation));
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidDataException("排位广播定义写入丢行");
    }

    private static void InsertRankedBroadcastDelivery(SqliteConnection connection,
        SqliteTransaction transaction, RankedBroadcastDeliveryRow row, long generation)
    {
        var lease = row.LeaseExpiresAt.ToUniversalTime();
        var completed = row.CompletedAt?.ToUniversalTime();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ranked_broadcast_deliveries(
                account_id,broadcast_id,claim_token,lease_expires_utc,completed_utc,
                row_generation,row_digest)
            VALUES($account,$broadcast,$token,$lease,$completed,$generation,$digest);
            """;
        command.Parameters.AddWithValue("$account", row.AccountId);
        command.Parameters.AddWithValue("$broadcast", row.BroadcastId);
        command.Parameters.AddWithValue("$token", row.ClaimToken);
        command.Parameters.AddWithValue("$lease", lease.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$completed", completed is null
            ? DBNull.Value : completed.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$generation", generation);
        command.Parameters.AddWithValue("$digest", RankedBroadcastDeliveryDigest(row.AccountId,
            row.BroadcastId, row.ClaimToken, lease, completed, generation));
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidDataException("排位广播投递写入丢行");
    }

    private static void AdvanceRankedBroadcastGeneration(SqliteConnection connection,
        SqliteTransaction transaction, long expected, long next)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE storage_meta SET value=$next
            WHERE key=$key AND value=$expected;
            """;
        command.Parameters.AddWithValue("$next", next.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$key", RankedBroadcastGenerationKey);
        command.Parameters.AddWithValue("$expected", expected.ToString(CultureInfo.InvariantCulture));
        if (command.ExecuteNonQuery() != 1)
            throw new L12PlatformStorageConflictException(
                new InvalidOperationException("排位广播代次 CAS 失败"));
    }

    private L12RankedBroadcastClaimView? ClaimRankedBroadcastObject(string accountId,
        DateTimeOffset? subscriptionStartedAt, DateTimeOffset now, TimeSpan realtimeWindow)
    {
        using var deployment = EnterDeploymentMutation();
        lock (_gate)
        {
            if (!_storageWritable)
                throw new L12PlatformStorageUnavailableException(
                    _storageIssue ?? "事务存储处于只读回退模式");
            try
            {
                using var connection = OpenDatabase(_databasePath, readOnly: false);
                using var transaction = connection.BeginTransaction(deferred: false);
                var latest = ReadRankedBroadcastCommittedProjection(connection, transaction);
                var account = latest.Accounts.FirstOrDefault(row => row.Id == accountId
                    && !row.Disabled && !row.Deleted)
                    ?? throw new KeyNotFoundException("账号不存在或不可用");
                var normalizedNow = now.ToUniversalTime();
                var cutoff = latest.RankedBroadcastDeliveryCutover ?? normalizedNow;
                if (account.CreatedAt.ToUniversalTime() > cutoff) cutoff = account.CreatedAt.ToUniversalTime();
                var freshFloor = normalizedNow - realtimeWindow;
                var requestedStart = (subscriptionStartedAt ?? freshFloor).ToUniversalTime();
                if (requestedStart > normalizedNow) requestedStart = normalizedNow;
                if (requestedStart < freshFloor) requestedStart = freshFloor;
                if (requestedStart > cutoff) cutoff = requestedStart;
                var delivered = latest.RankedBroadcastDeliveries
                    .Where(row => string.Equals(row.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.BroadcastId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var next = latest.RankedBroadcasts.Where(row => row.CreatedAt >= cutoff
                        && !delivered.Contains(row.Id))
                    .OrderBy(row => row.CreatedAt)
                    .ThenBy(row => row.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (next is null)
                {
                    PublishRankedBroadcastProjectionWithoutCommit(latest, transaction);
                    return null;
                }

                var nextGeneration = checked(latest.RankedBroadcastGeneration + 1);
                var delivery = new RankedBroadcastDeliveryRow
                {
                    AccountId = accountId,
                    BroadcastId = next.Id,
                    ClaimToken = Guid.NewGuid().ToString("N"),
                    LeaseExpiresAt = normalizedNow + RankedBroadcastClaimLease,
                    Generation = nextGeneration,
                };
                InsertRankedBroadcastDelivery(connection, transaction, delivery, nextGeneration);
                AdvanceRankedBroadcastGeneration(connection, transaction,
                    latest.RankedBroadcastGeneration, nextGeneration);
                latest.RankedBroadcastGeneration = nextGeneration;
                latest.RankedBroadcastDeliveries.Add(delivery);
                CommitRankedBroadcastProjection(latest, transaction,
                    "before-ranked-broadcast-commit");
                return new(ToView(next), delivery.ClaimToken, delivery.LeaseExpiresAt);
            }
            catch (L12PlatformStorageUnavailableException)
            {
                throw;
            }
            catch (KeyNotFoundException)
            {
                throw;
            }
            catch (Exception error)
            {
                _storageIssue = $"排位广播领取事务失败：{error.Message}";
                throw new L12PlatformStorageUnavailableException(_storageIssue, error);
            }
        }
    }

    private bool CompleteRankedBroadcastObject(string accountId, string broadcastId,
        string claimToken, DateTimeOffset now)
    {
        using var deployment = EnterDeploymentMutation();
        lock (_gate)
        {
            if (!_storageWritable)
                throw new L12PlatformStorageUnavailableException(
                    _storageIssue ?? "事务存储处于只读回退模式");
            try
            {
                using var connection = OpenDatabase(_databasePath, readOnly: false);
                using var transaction = connection.BeginTransaction(deferred: false);
                var latest = ReadRankedBroadcastCommittedProjection(connection, transaction);
                var account = latest.Accounts.FirstOrDefault(row => row.Id == accountId
                    && !row.Disabled && !row.Deleted);
                if (account is null)
                {
                    PublishRankedBroadcastProjectionWithoutCommit(latest, transaction);
                    return false;
                }
                var delivery = latest.RankedBroadcastDeliveries.FirstOrDefault(row =>
                    string.Equals(row.AccountId, accountId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.BroadcastId, broadcastId, StringComparison.OrdinalIgnoreCase));
                if (delivery is null || string.IsNullOrWhiteSpace(claimToken)
                    || !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(delivery.ClaimToken), Encoding.UTF8.GetBytes(claimToken)))
                {
                    PublishRankedBroadcastProjectionWithoutCommit(latest, transaction);
                    return false;
                }
                if (delivery.CompletedAt is not null)
                {
                    PublishRankedBroadcastProjectionWithoutCommit(latest, transaction);
                    return true;
                }

                var nextGeneration = checked(latest.RankedBroadcastGeneration + 1);
                var completed = now.ToUniversalTime();
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE ranked_broadcast_deliveries
                    SET completed_utc=$completed,row_generation=$generation,row_digest=$digest
                    WHERE account_id=$account AND broadcast_id=$broadcast
                      AND completed_utc IS NULL AND claim_token=$token;
                    """;
                update.Parameters.AddWithValue("$completed", completed.ToString("O", CultureInfo.InvariantCulture));
                update.Parameters.AddWithValue("$generation", nextGeneration);
                update.Parameters.AddWithValue("$digest", RankedBroadcastDeliveryDigest(delivery.AccountId,
                    delivery.BroadcastId, delivery.ClaimToken, delivery.LeaseExpiresAt, completed, nextGeneration));
                update.Parameters.AddWithValue("$account", delivery.AccountId);
                update.Parameters.AddWithValue("$broadcast", delivery.BroadcastId);
                update.Parameters.AddWithValue("$token", delivery.ClaimToken);
                if (update.ExecuteNonQuery() != 1)
                    throw new L12PlatformStorageConflictException(
                        new InvalidOperationException("排位广播完成 CAS 失败"));
                AdvanceRankedBroadcastGeneration(connection, transaction,
                    latest.RankedBroadcastGeneration, nextGeneration);
                delivery.CompletedAt = completed;
                delivery.Generation = nextGeneration;
                latest.RankedBroadcastGeneration = nextGeneration;
                CommitRankedBroadcastProjection(latest, transaction,
                    "before-ranked-broadcast-complete-commit");
                return true;
            }
            catch (L12PlatformStorageUnavailableException)
            {
                throw;
            }
            catch (Exception error)
            {
                _storageIssue = $"排位广播完成事务失败：{error.Message}";
                throw new L12PlatformStorageUnavailableException(_storageIssue, error);
            }
        }
    }

    private DataFile ReadRankedBroadcastCommittedProjection(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        AssertRankedBroadcastObjectSchema(connection, transaction);
        var stored = ReadSnapshot(connection, transaction)
            ?? throw new InvalidDataException("排位广播事务缺少平台快照");
        var latest = DeserializeDataAndValidate(ValidateSnapshot(stored));
        if (latest.Version != ReadStorageRevision(connection, transaction))
            throw new InvalidDataException("排位广播事务平台版本不一致");
        HydrateDeckDomain(connection, latest, transaction);
        HydrateRankedBroadcastObjects(connection, latest, transaction);
        MergeIndependentAudit(connection, latest, transaction);
        return latest;
    }

    private void CommitRankedBroadcastProjection(DataFile latest, SqliteTransaction transaction,
        string failureStage)
    {
        var rollbackSnapshot = SerializeRollbackState(latest);
        var sessionActivity = PrepareCommittedSessionActivity(latest, rollbackSnapshot);
        StorageFailureInjector?.Invoke(failureStage);
        StorageFailureInjector?.Invoke("after-ranked-broadcast-rollback-serialize");
        transaction.Commit();
        _data = latest;
        _lastCommittedSnapshot = rollbackSnapshot;
        PublishCommittedSessionActivity(sessionActivity);
        _storageIssue = null;
    }

    private void PublishRankedBroadcastProjectionWithoutCommit(DataFile latest,
        SqliteTransaction transaction)
    {
        var rollbackSnapshot = SerializeRollbackState(latest);
        var sessionActivity = PrepareCommittedSessionActivity(latest, rollbackSnapshot);
        transaction.Rollback();
        _data = latest;
        _lastCommittedSnapshot = rollbackSnapshot;
        PublishCommittedSessionActivity(sessionActivity);
        _storageIssue = null;
    }

    private static string RankedBroadcastDefinitionDigest(string id, string matchId,
        string eventType, string message, DateTimeOffset createdAt, long generation)
        => Sha256(RankedBroadcastCanonical("definition", id, matchId, eventType, message,
            createdAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            generation.ToString(CultureInfo.InvariantCulture)));

    private static string RankedBroadcastDeliveryDigest(string accountId, string broadcastId,
        string token, DateTimeOffset leaseExpiresAt, DateTimeOffset? completedAt, long generation)
        => Sha256(RankedBroadcastCanonical("delivery", accountId, broadcastId, token,
            leaseExpiresAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            completedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            generation.ToString(CultureInfo.InvariantCulture)));

    private static string RankedBroadcastCanonical(params string?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (value is null)
            {
                builder.Append("-1:");
                continue;
            }
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(value).Append(';');
        }
        return builder.ToString();
    }

    // Offline exit rehearsal: publish one schema-8 snapshot/mirror from the same
    // committed object generation, then remove schema-9 authority transactionally.
    internal static void RevertRankedBroadcastObjectStorageForRehearsal(string databasePath)
    {
        var mirrorPath = Path.Combine(Path.GetDirectoryName(databasePath)!,
            Path.GetFileNameWithoutExtension(databasePath) + ".json");
        var pendingMirrorPath = mirrorPath + ".ranked-broadcast-schema8.pending";
        using var connection = OpenDatabase(databasePath, readOnly: false, initialize: false);
        using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragmas.ExecuteNonQuery();
        }
        var existingVersion = ReadExistingStorageSchemaVersion(connection);
        if (existingVersion == CompactDeckPayloadSchemaVersion)
        {
            PublishOrValidateRankedBroadcastSchemaEightMirror(connection, pendingMirrorPath, mirrorPath);
            return;
        }
        if (existingVersion != RankedBroadcastObjectSchemaVersion)
            throw new InvalidDataException("排位广播反向迁移副本不是 schema 9");

        var pendingMirrorVerified = false;
        try
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            AssertRankedBroadcastObjectSchema(connection, transaction);
            var stored = ReadSnapshot(connection, transaction)
                ?? throw new InvalidDataException("排位广播反向迁移缺少平台快照");
            var data = DeserializeDataAndValidate(ValidateSnapshot(stored));
            if (data.Version != ReadStorageRevision(connection, transaction))
                throw new InvalidDataException("排位广播反向迁移平台版本不一致");
            HydrateDeckDomain(connection, data, transaction);
            VerifyDeckDomainSnapshot(connection, transaction, data);
            HydrateRankedBroadcastObjects(connection, data, transaction);
            ValidateRankedBroadcastObjectRows(connection, transaction);
            var snapshotJson = SerializeSnapshot(data);
            var mirrorJson = SerializeFullDeckDomainBackup(data);
            var mirrorHash = Sha256(mirrorJson);
            var previousMirrorHash = ReadMeta(connection, "fallback_json_sha256", transaction)
                ?? throw new InvalidDataException("排位广播反向迁移缺少上一代镜像摘要");

            // The full mirror is persisted and read back before SQLite authority is
            // changed. A prewrite failure therefore leaves the complete schema-9
            // database untouched. The deterministic pending file is the only
            // post-commit recovery token and always contains this exact generation.
            WriteDurableRankedBroadcastSchemaEightPendingMirror(pendingMirrorPath, mirrorJson);
            var persistedMirror = File.ReadAllText(pendingMirrorPath);
            if (!FixedEquals(Sha256(persistedMirror), mirrorHash)
                || !HasCompleteLegacyDeckDomain(persistedMirror))
                throw new InvalidDataException("排位广播反向迁移待发布镜像校验失败");
            pendingMirrorVerified = true;

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE platform_state
                    SET schema_version=$schema,snapshot_json=$json,snapshot_sha256=$checksum,
                        updated_utc=$updated
                    WHERE singleton_id=1;
                    """;
                update.Parameters.AddWithValue("$schema", CompactDeckPayloadSchemaVersion);
                update.Parameters.AddWithValue("$json", snapshotJson);
                update.Parameters.AddWithValue("$checksum", Sha256(snapshotJson));
                update.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidDataException("排位广播反向迁移快照写入丢行");
            }
            SetStorageMeta(connection, transaction, "fallback_json_sha256", mirrorHash);
            SetStorageMeta(connection, transaction,
                RankedBroadcastSchemaEightPreviousMirrorHashKey, previousMirrorHash);
            using (var removeMeta = connection.CreateCommand())
            {
                removeMeta.Transaction = transaction;
                removeMeta.CommandText = """
                    DELETE FROM storage_meta
                    WHERE key IN ($state,$generation,$cutover);
                    """;
                removeMeta.Parameters.AddWithValue("$state", RankedBroadcastObjectStateKey);
                removeMeta.Parameters.AddWithValue("$generation", RankedBroadcastGenerationKey);
                removeMeta.Parameters.AddWithValue("$cutover", RankedBroadcastCutoverKey);
                if (removeMeta.ExecuteNonQuery() != 3)
                    throw new InvalidDataException("排位广播反向迁移元数据计数无效");
            }
            using (var drop = connection.CreateCommand())
            {
                drop.Transaction = transaction;
                drop.CommandText = """
                    DROP TABLE ranked_broadcast_deliveries;
                    DROP TABLE ranked_broadcast_definitions;
                    """;
                drop.ExecuteNonQuery();
            }
            SetStorageSchemaVersion(connection, transaction, CompactDeckPayloadSchemaVersion);
            transaction.Commit();

            // SQLite commit and filesystem rename cannot be one atomic primitive.
            // After commit, a failed rename deliberately retains the verified
            // pending file; a later schema-8 invocation only publishes that file
            // after binding it back to the committed snapshot and deck projection.
            File.Move(pendingMirrorPath, mirrorPath, true);
            ClearRankedBroadcastSchemaEightPreviousMirrorHash(connection);
        }
        catch
        {
            // Once the pending generation has reached stable storage, retain it
            // even when the SQLite commit outcome is uncertain. A schema-9 retry
            // overwrites it; a schema-8 retry validates it before publication.
            if (!pendingMirrorVerified) TryDelete(pendingMirrorPath);
            throw;
        }
    }

    private static void WriteDurableRankedBroadcastSchemaEightPendingMirror(string path,
        string mirrorJson)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var bytes = new UTF8Encoding(false).GetBytes(mirrorJson);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void PublishOrValidateRankedBroadcastSchemaEightMirror(SqliteConnection connection,
        string pendingMirrorPath, string mirrorPath)
    {
        if (Directory.Exists(pendingMirrorPath))
            throw new InvalidDataException("schema 8 排位广播待发布镜像路径不是文件");
        if (Directory.Exists(mirrorPath))
            throw new InvalidDataException("schema 8 排位广播最终镜像路径不是文件");
        var hasPending = File.Exists(pendingMirrorPath);
        var hasFinal = File.Exists(mirrorPath);
        if (!hasPending && !hasFinal)
            throw new InvalidDataException("schema 8 排位广播反向迁移没有可验证镜像");
        if (TableExists(connection, "ranked_broadcast_definitions")
            || TableExists(connection, "ranked_broadcast_deliveries"))
            throw new InvalidDataException("schema 8 排位广播反向迁移仍残留对象表");

        using var transaction = connection.BeginTransaction(deferred: true);
        var stored = ReadSnapshot(connection, transaction)
            ?? throw new InvalidDataException("schema 8 排位广播反向迁移缺少平台快照");
        var expectedHash = ReadMeta(connection, "fallback_json_sha256", transaction)
            ?? throw new InvalidDataException("schema 8 排位广播反向迁移缺少镜像摘要");
        if (hasPending)
        {
            ValidateRankedBroadcastSchemaEightMirror(connection, transaction, stored,
                expectedHash, pendingMirrorPath, "待发布");
            if (hasFinal)
            {
                var finalJson = File.ReadAllText(mirrorPath);
                var finalHash = Sha256(finalJson);
                if (FixedEquals(finalHash, expectedHash))
                    ValidateRankedBroadcastSchemaEightMirror(connection, transaction, stored,
                        expectedHash, mirrorPath, "最终");
                else
                {
                    var previousHash = ReadMeta(connection,
                        RankedBroadcastSchemaEightPreviousMirrorHashKey, transaction);
                    if (previousHash is null || !FixedEquals(finalHash, previousHash))
                        throw new InvalidDataException("schema 8 排位广播最终镜像与已冻结前代不一致");
                    ValidateRankedBroadcastSchemaEightPredecessorMirror(previousHash, mirrorPath);
                }
            }
        }
        else
            ValidateRankedBroadcastSchemaEightMirror(connection, transaction, stored,
                expectedHash, mirrorPath, "最终");
        transaction.Commit();
        if (hasPending) File.Move(pendingMirrorPath, mirrorPath, true);
        ClearRankedBroadcastSchemaEightPreviousMirrorHash(connection);
    }

    private static void ValidateRankedBroadcastSchemaEightMirror(SqliteConnection connection,
        SqliteTransaction transaction, (string Json, string Checksum) stored, string expectedHash,
        string path, string stage)
    {
        var json = File.ReadAllText(path);
        if (!FixedEquals(Sha256(json), expectedHash) || !HasCompleteLegacyDeckDomain(json))
            throw new InvalidDataException($"schema 8 排位广播{stage}镜像摘要或牌库正文无效");
        var data = DeserializeDataAndValidate(json);
        if (!FixedEquals(Sha256(SerializeSnapshot(data)), stored.Checksum))
            throw new InvalidDataException($"schema 8 排位广播{stage}镜像不属于已提交平台代次");
        VerifyDeckDomainSnapshot(connection, transaction, data);
        ValidateLegacyRankedBroadcastProjection(data);
    }

    private static void ValidateRankedBroadcastSchemaEightPredecessorMirror(string expectedHash,
        string path)
    {
        var json = File.ReadAllText(path);
        if (!FixedEquals(Sha256(json), expectedHash))
            throw new InvalidDataException("schema 8 排位广播已冻结前代镜像摘要无效");
        // The predecessor is the schema-9 compact compatibility mirror whose hash
        // was frozen by the same transaction that committed schema 8. It is not a
        // schema-8 full-deck artifact and must never be accepted as the final exit
        // mirror; it is parsed only to prove the exact replaceable predecessor.
        var data = DeserializeDataAndValidate(json);
        ValidateCompactRuntimeDeckDomain(data);
        ValidateLegacyRankedBroadcastProjection(data);
    }

    private static void ClearRankedBroadcastSchemaEightPreviousMirrorHash(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM storage_meta WHERE key=$key;";
        command.Parameters.AddWithValue("$key", RankedBroadcastSchemaEightPreviousMirrorHashKey);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
