using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckReferenceQuery(IReadOnlyList<string> PublicationIds)
{
    public const int Limit = 100;

    public static bool TryParse(IQueryCollection values, out L12PublicDeckReferenceQuery query)
    {
        query = new([]);
        if (values.Count != 1 || values.Any(item => item.Key != "publicationId")
            || !values.TryGetValue("publicationId", out var raw) || raw.Count is < 1 or > Limit)
            return false;
        var ids = new List<string>(raw.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in raw)
        {
            var id = candidate ?? string.Empty;
            if (!IsValidReference(id) || !seen.Add(id)) return false;
            ids.Add(id);
        }
        query = new(ids);
        return true;
    }

    private static bool IsValidReference(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value == value.Trim()
            && !value.Any(char.IsControl);
}

public sealed record L12PublicDeckReferenceView(string Id, string PublicCode,
    int PublicationVersion, string OwnerId);
public sealed record L12PublicDeckReferenceResult(string Status,
    IReadOnlyList<L12PublicDeckReferenceView> Items);

public sealed partial class L12PlatformStore
{
    // This guard is deliberately independent of result cardinality. Missing,
    // foreign and off-page references must not turn a drifted store into an
    // authoritative empty result.
    private void ValidatePublicDeckReadGeneration(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!_storageWritable)
            throw new L12PlatformStorageUnavailableException(_storageIssue ?? "公开牌库事务存储当前不可用");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT schema_version,storage_revision,
                   (SELECT value FROM storage_meta WHERE key='schema_version'),
                   (SELECT value FROM storage_meta WHERE key='deck_payload_format_state')
            FROM platform_state WHERE singleton_id=1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != PlatformStorageSchemaVersion
            || reader.GetInt64(1) != _data.Version || reader.IsDBNull(2)
            || reader.GetString(2) != PlatformStorageSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || reader.IsDBNull(3) || reader.GetString(3) != DeckPayloadFormatActiveState || reader.Read())
            throw new InvalidDataException("公开牌库读取存储格式或已提交代际不一致");
    }

    public L12PublicDeckReferenceResult PublicDeckReferences(L12Catalog catalog,
        IReadOnlyList<string> publicationIds, L12AuthenticatedSession? viewer)
    {
        if (publicationIds.Count is < 1 or > L12PublicDeckReferenceQuery.Limit
            || publicationIds.Any(id => !IsPublicDeckReadReference(id) || id != id.Trim())
            || publicationIds.Distinct(StringComparer.Ordinal).Count() != publicationIds.Count)
            return new("invalid_request", []);
        lock (_gate)
        {
            if (viewer is null || !IsCurrentPublicDeckReader(viewer)) return new("unauthorized", []);
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return new("feature_disabled", []);
            var data = _data;
            var requested = publicationIds.ToHashSet(StringComparer.Ordinal);
            var selected = data.PublishedDecks.Where(row => row.OwnerId == viewer.Account.Id && requested.Contains(row.Id)).ToArray();
            if (selected.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != selected.Length)
                throw new L12PlatformStorageUnavailableException("公开牌库引用目录存在重复身份");
            var rows = selected.ToDictionary(row => row.Id, StringComparer.Ordinal);
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            ValidatePublicDeckReadGeneration(connection, transaction);
            var result = new List<L12PublicDeckReferenceView>(rows.Count);
            var catalogVersion = LibraryCatalogVersion(catalog);
            foreach (var id in publicationIds)
            {
                if (!rows.TryGetValue(id, out var row)) continue;
                var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer, catalogVersion);
                result.Add(new(head.Id, row.PublicCode, head.Version, viewer.Account.Id));
            }
            transaction.Commit();
            return new("available", result);
        }
    }
}
