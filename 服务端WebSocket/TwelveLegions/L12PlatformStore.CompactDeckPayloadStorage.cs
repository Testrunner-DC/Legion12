using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private const int LegacyDeckPayloadSchemaVersion = 7;
    private const string DeckPayloadFormatStateKey = "deck_payload_format_state";
    private const string DeckPayloadFormatActiveState = "compact-v1";
    private const string DeckPayloadFormatLegacyState = "legacy-v7";

    private sealed record LegacyDeckPayloadRow(
        string Hash,
        string MasterId,
        string MainJson,
        string MoraleJson,
        string SpecialJson);

    private sealed record CompactDeckPayloadRow(
        string Hash,
        string MasterId,
        int Format,
        string PayloadJson);

    private static void EnsureCompactDeckPayloadStorage(SqliteConnection connection, int? existingSchemaVersion)
    {
        var columns = ReadDeckPayloadColumns(connection);
        var hasLegacy = HasAll(columns, "main_cards_json", "morale_cards_json", "special_cards_json");
        var hasCompact = HasAll(columns, "payload_format", "payload_json");
        if (hasLegacy == hasCompact)
            throw new L12PlatformStorageIncompatibleException("牌库构筑正文列处于不完整或双写状态");

        if (hasLegacy)
        {
            if (existingSchemaVersion is > LegacyDeckPayloadSchemaVersion)
                throw new L12PlatformStorageIncompatibleException("新版本存储不得回退读取旧牌库正文列");
            MigrateLegacyDeckPayloads(connection);
            return;
        }

        var rows = ReadCompactPayloads(connection, transaction: null);
        foreach (var row in rows)
        {
            if (row.Format != L12DeckPayloadCodec.CurrentFormat)
                throw new L12PlatformStorageIncompatibleException(
                    $"不支持的牌库构筑正文格式：{row.Format}");
            ValidateCompactPayload(row);
        }
        if (existingSchemaVersion is not null && existingSchemaVersion < PlatformStorageSchemaVersion)
            throw new L12PlatformStorageIncompatibleException("牌库构筑正文格式已切换，但存储版本未提交");
        if (existingSchemaVersion == PlatformStorageSchemaVersion
            && !string.Equals(ReadMeta(connection, DeckPayloadFormatStateKey),
                DeckPayloadFormatActiveState, StringComparison.Ordinal))
            throw new L12PlatformStorageIncompatibleException("牌库构筑正文格式状态未提交或未知");

        using var transaction = connection.BeginTransaction(deferred: false);
        SetStorageMeta(connection, transaction, DeckPayloadFormatStateKey, DeckPayloadFormatActiveState);
        SetStorageSchemaVersion(connection, transaction, PlatformStorageSchemaVersion);
        transaction.Commit();
    }

    private static void MigrateLegacyDeckPayloads(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var legacyRows = ReadLegacyPayloads(connection, transaction);
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads ADD COLUMN payload_format INTEGER NOT NULL DEFAULT 1;");
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads ADD COLUMN payload_json TEXT NOT NULL DEFAULT '';");
        foreach (var row in legacyRows)
        {
            var payloadJson = L12DeckPayloadCodec.EncodeLegacyJson(
                row.MainJson, row.MoraleJson, row.SpecialJson);
            var decoded = L12DeckPayloadCodec.Decode(L12DeckPayloadCodec.CurrentFormat, payloadJson);
            L12DeckPayloadCodec.ValidateHash(row.Hash, row.MasterId, decoded);
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE deck_payloads SET payload_format=$format,payload_json=$payload WHERE payload_hash=$hash;";
            update.Parameters.AddWithValue("$format", L12DeckPayloadCodec.CurrentFormat);
            update.Parameters.AddWithValue("$payload", payloadJson);
            update.Parameters.AddWithValue("$hash", row.Hash);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidDataException("牌库构筑正文迁移读写丢行");
        }

        foreach (var row in ReadCompactPayloads(connection, transaction)) ValidateCompactPayload(row);
        VerifyForeignKeys(connection, transaction);
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads DROP COLUMN main_cards_json;");
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads DROP COLUMN morale_cards_json;");
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads DROP COLUMN special_cards_json;");
        VerifyForeignKeys(connection, transaction);
        SetStorageMeta(connection, transaction, DeckPayloadFormatStateKey, DeckPayloadFormatActiveState);
        SetStorageSchemaVersion(connection, transaction, PlatformStorageSchemaVersion);
        transaction.Commit();
    }

    // Exit rehearsal is intentionally internal and path-based so callers can only
    // exercise it against an explicit offline database copy.
    internal static void RevertCompactDeckPayloadStorageForRehearsal(string databasePath)
    {
        using var connection = OpenDatabase(databasePath, readOnly: false, initialize: false);
        using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragmas.ExecuteNonQuery();
        }
        var version = ReadExistingStorageSchemaVersion(connection);
        if (version != PlatformStorageSchemaVersion)
            throw new InvalidDataException("反向迁移副本不是当前牌库存储版本");
        if (!string.Equals(ReadMeta(connection, DeckPayloadFormatStateKey),
                DeckPayloadFormatActiveState, StringComparison.Ordinal))
            throw new InvalidDataException("反向迁移副本的牌库正文格式状态无效");
        var columns = ReadDeckPayloadColumns(connection);
        if (!HasAll(columns, "payload_format", "payload_json")
            || columns.Contains("main_cards_json") || columns.Contains("morale_cards_json")
            || columns.Contains("special_cards_json"))
            throw new InvalidDataException("反向迁移副本的牌库正文列无效");

        using var transaction = connection.BeginTransaction(deferred: false);
        var compactRows = ReadCompactPayloads(connection, transaction);
        var decodedRows = compactRows.Select(row =>
        {
            var decoded = ValidateCompactPayload(row);
            return (row.Hash, Decoded: decoded);
        }).ToArray();
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads ADD COLUMN main_cards_json TEXT NOT NULL DEFAULT '';");
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads ADD COLUMN morale_cards_json TEXT NOT NULL DEFAULT '';");
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads ADD COLUMN special_cards_json TEXT NOT NULL DEFAULT '';");
        foreach (var row in decodedRows)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE deck_payloads SET main_cards_json=$main,morale_cards_json=$morale,
                    special_cards_json=$special WHERE payload_hash=$hash;
                """;
            update.Parameters.AddWithValue("$main", row.Decoded.MainJson);
            update.Parameters.AddWithValue("$morale", row.Decoded.MoraleJson);
            update.Parameters.AddWithValue("$special", row.Decoded.SpecialJson);
            update.Parameters.AddWithValue("$hash", row.Hash);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidDataException("牌库构筑正文反向迁移读写丢行");
        }

        foreach (var row in ReadLegacyPayloads(connection, transaction))
        {
            var decoded = new L12DecodedDeckPayload(row.MainJson, row.MoraleJson, row.SpecialJson);
            L12DeckPayloadCodec.ValidateHash(row.Hash, row.MasterId, decoded);
        }
        VerifyForeignKeys(connection, transaction);
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads DROP COLUMN payload_json;");
        ExecutePayloadDdl(connection, transaction, "ALTER TABLE deck_payloads DROP COLUMN payload_format;");
        VerifyForeignKeys(connection, transaction);
        SetStorageMeta(connection, transaction, DeckPayloadFormatStateKey, DeckPayloadFormatLegacyState);
        SetStorageSchemaVersion(connection, transaction, LegacyDeckPayloadSchemaVersion);
        transaction.Commit();
    }

    private static L12DecodedDeckPayload ValidateCompactPayload(CompactDeckPayloadRow row)
    {
        var decoded = L12DeckPayloadCodec.Decode(row.Format, row.PayloadJson);
        L12DeckPayloadCodec.ValidateHash(row.Hash, row.MasterId, decoded);
        return decoded;
    }

    private static List<LegacyDeckPayloadRow> ReadLegacyPayloads(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var rows = new List<LegacyDeckPayloadRow>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload_hash,master_id,main_cards_json,morale_cards_json,special_cards_json
            FROM deck_payloads ORDER BY payload_hash;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1),
            reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private static List<CompactDeckPayloadRow> ReadCompactPayloads(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        var rows = new List<CompactDeckPayloadRow>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload_hash,master_id,payload_format,payload_json
            FROM deck_payloads ORDER BY payload_hash;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1),
            reader.GetInt32(2), reader.GetString(3)));
        return rows;
    }

    private static HashSet<string> ReadDeckPayloadColumns(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(deck_payloads);";
        using var reader = command.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static bool HasAll(HashSet<string> columns, params string[] required)
        => required.All(columns.Contains);

    private static void ExecutePayloadDdl(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void VerifyForeignKeys(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        if (reader.Read()) throw new InvalidDataException("牌库构筑正文迁移破坏了规范化引用");
    }

    private static void SetStorageSchemaVersion(SqliteConnection connection, SqliteTransaction transaction,
        int version)
    {
        SetStorageMeta(connection, transaction, "schema_version", version.ToString());
        using var state = connection.CreateCommand();
        state.Transaction = transaction;
        state.CommandText = "UPDATE platform_state SET schema_version=$schema WHERE singleton_id=1;";
        state.Parameters.AddWithValue("$schema", version);
        state.ExecuteNonQuery();
    }
}
