using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckReadQuery(int Page = 1, int PageSize = 30, string? ExpectedReadToken = null)
{
    public static bool IsValidToken(string? value) => value is null || value.Length == 64 && value.All(Uri.IsHexDigit);
    public bool IsValid => Page > 0 && PageSize is > 0 and <= 100 && IsValidToken(ExpectedReadToken);
    public static bool TryParse(IQueryCollection values, bool paged, out L12PublicDeckReadQuery query)
    {
        query = new();
        if (values.Any(item => item.Value.Count != 1 || item.Value[0] is null
            || item.Key != "expectedReadToken" && (!paged || item.Key is not ("page" or "pageSize")))) return false;
        bool Integer(string key, int fallback, out int value)
        {
            value = fallback;
            return !values.TryGetValue(key, out var raw)
                || int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
        }
        if (!Integer("page", 1, out var page) || !Integer("pageSize", 30, out var size)) return false;
        query = new(page, size, values.TryGetValue("expectedReadToken", out var token) ? token[0] : null);
        return query.IsValid;
    }
}

public sealed record L12PublicDeckVersionMetadataView(int Version, string Name, string MasterId, DateTimeOffset CreatedAt,
    L12DeckSummaryCounts Counts, bool Legal, string? LegalityReason, L12DeckEnvironmentView Environment,
    IReadOnlyList<L12PublicDeckVersionChangeView> Changes);
public sealed record L12PublicDeckVersionPage(string Id, string PublicCode,
    IReadOnlyList<L12PublicDeckVersionMetadataView> Items, int Total, int Page, int PageSize,
    string ReadToken, string CatalogVersion, long PolicyVersion, bool CanEdit);
public sealed record L12PublicDeckVersionPageResult(string Status, L12PublicDeckVersionPage? Page = null);
public sealed record L12PublicDeckVersionReadView(string Id, string PublicCode, L12PublicDeckVersionMetadataView Metadata,
    L12AccountDeckView Deck, string ReadToken, string CatalogVersion, long PolicyVersion, bool CanEdit);
public sealed record L12PublicDeckVersionReadResult(string Status, L12PublicDeckVersionReadView? Detail = null);

public sealed partial class L12PlatformStore
{
    private sealed record PinnedPublicDeckVersion(int Version, string Name, NormalizedDeckPayload Payload, DateTimeOffset CreatedAt);

    private static PinnedPublicDeckVersion? ReadPinnedPublicDeckVersion(SqliteConnection connection,
        SqliteTransaction transaction, PublicDeckReadHead head, int version)
    {
        if (version < 1) throw new InvalidDataException("公开牌库不可变版本号无效");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT version.name,version.payload_hash,version.created_utc,payload.master_id,payload.payload_format,payload.payload_json
            FROM published_deck_versions version
            LEFT JOIN deck_payloads payload ON payload.payload_hash=version.payload_hash
            WHERE version.publication_id=$id AND version.version=$version AND version.version<=$head;
            """;
        command.Parameters.AddWithValue("$id", head.Id);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$head", head.Version);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        if (reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5))
            throw new InvalidDataException("公开牌库版本正文引用缺失");
        var name = reader.GetString(0);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("公开牌库版本名称缺失");
        var hash = reader.GetString(1);
        var master = reader.GetString(3);
        var decoded = L12DeckPayloadCodec.Decode(reader.GetInt32(4), reader.GetString(5));
        L12DeckPayloadCodec.ValidateHash(hash, master, decoded);
        return new(version, name, new(hash, master, decoded.MainJson, decoded.MoraleJson, decoded.SpecialJson, [], [], []),
            DateTimeOffset.Parse(reader.GetString(2)));
    }

    private static PinnedPublicDeckVersion? ReadPreviousPinnedPublicDeckVersion(SqliteConnection connection,
        SqliteTransaction transaction, PublicDeckReadHead head, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MAX(version) FROM published_deck_versions WHERE publication_id=$id AND version<$version AND version<=$head;";
        command.Parameters.AddWithValue("$id", head.Id);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$head", head.Version);
        var previous = command.ExecuteScalar();
        return previous is null or DBNull ? null : ReadPinnedPublicDeckVersion(connection, transaction, head, Convert.ToInt32(previous));
    }

    private static IReadOnlyList<L12PublicDeckVersionChangeView> DiffPinnedPublicDeckCounts(
        NormalizedDeckPayload? previous, NormalizedDeckPayload current)
    {
        if (previous is null) return [];
        var changes = new List<L12PublicDeckVersionChangeView>();
        if (previous.MasterId != current.MasterId) changes.Add(new("master", current.MasterId, 0, 1));
        Add("main", previous.MainJson, current.MainJson);
        Add("morale", previous.MoraleJson, current.MoraleJson);
        Add("special", previous.SpecialJson, current.SpecialJson);
        return changes;
        void Add(string section, string before, string after)
        {
            var left = ReadRecoveryCardCounts(before).ToDictionary(item => item.CardId, item => item.Quantity, StringComparer.Ordinal);
            var right = ReadRecoveryCardCounts(after).ToDictionary(item => item.CardId, item => item.Quantity, StringComparer.Ordinal);
            foreach (var id in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                var from = left.GetValueOrDefault(id);
                var to = right.GetValueOrDefault(id);
                if (from != to) changes.Add(new(section, id, from, to));
            }
        }
    }

    private static L12PublicDeckVersionMetadataView PinnedPublicDeckVersionMetadata(L12Catalog catalog,
        PublicDeckReadHead head, PinnedPublicDeckVersion selected, PinnedPublicDeckVersion? previous)
    {
        var payload = selected.Payload;
        var summary = LibraryEntry(catalog, head.Policy.CardRestrictions, head.Id, "public", selected.Name, payload.MasterId,
            "", null, null, selected.CreatedAt, selected.CreatedAt, SummaryCardCounts(payload.MainJson),
            SummaryCardCounts(payload.MoraleJson), SummaryCardCounts(payload.SpecialJson), [], null, 0, 0, 0, false, false).View;
        return new(selected.Version, selected.Name, payload.MasterId, selected.CreatedAt, summary.Counts, summary.Legal,
            summary.LegalityReason, summary.Environment, DiffPinnedPublicDeckCounts(previous?.Payload, payload));
    }

    public L12PublicDeckVersionPageResult ReadPublicDeckVersionPage(L12Catalog catalog, string reference,
        L12PublicDeckReadQuery? query = null, L12AuthenticatedSession? viewer = null)
    {
        query ??= new();
        if (!IsPublicDeckReadReference(reference) || !query.IsValid) return new("invalid_request");
        lock (_gate)
        {
            if (!IsCurrentPublicDeckReader(viewer)) return new("unauthorized");
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return new("feature_disabled");
            var row = FindPublishedDeck(reference);
            if (row is null) return new("not_found");
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer);
            if (query.ExpectedReadToken is not null && query.ExpectedReadToken != head.Token) return new("read_conflict");
            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM published_deck_versions WHERE publication_id=$id AND version<=$head;";
            count.Parameters.AddWithValue("$id", head.Id);
            count.Parameters.AddWithValue("$head", head.Version);
            var total = checked(Convert.ToInt32(count.ExecuteScalar()));
            using var page = connection.CreateCommand();
            page.Transaction = transaction;
            page.CommandText = "SELECT version FROM published_deck_versions WHERE publication_id=$id AND version<=$head ORDER BY version DESC LIMIT $limit OFFSET $offset;";
            page.Parameters.AddWithValue("$id", head.Id);
            page.Parameters.AddWithValue("$head", head.Version);
            page.Parameters.AddWithValue("$limit", query.PageSize);
            page.Parameters.AddWithValue("$offset", ((long)query.Page - 1) * query.PageSize);
            var versions = new List<int>();
            using (var reader = page.ExecuteReader()) while (reader.Read()) versions.Add(reader.GetInt32(0));
            var items = versions.Select(version =>
            {
                var selected = ReadPinnedPublicDeckVersion(connection, transaction, head, version)
                    ?? throw new InvalidDataException("公开牌库目录版本引用缺失");
                var previous = ReadPreviousPinnedPublicDeckVersion(connection, transaction, head, version);
                return PinnedPublicDeckVersionMetadata(catalog, head, selected, previous);
            }).ToArray();
            transaction.Commit();
            return new("ok", new(row.Id, row.PublicCode, items, total, query.Page, query.PageSize, head.Token,
                head.CatalogVersion, policy.Version, viewer?.Account.Id == row.OwnerId));
        }
    }

    public L12PublicDeckVersionReadResult ReadPublicDeckVersion(L12Catalog catalog, string reference, int version,
        string? expectedReadToken = null, L12AuthenticatedSession? viewer = null)
    {
        if (!IsPublicDeckReadReference(reference) || version < 1 || !L12PublicDeckReadQuery.IsValidToken(expectedReadToken))
            return new("invalid_request");
        lock (_gate)
        {
            if (!IsCurrentPublicDeckReader(viewer)) return new("unauthorized");
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return new("feature_disabled");
            var row = FindPublishedDeck(reference);
            if (row is null || version > row.Version) return new("not_found");
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer);
            if (expectedReadToken is not null && expectedReadToken != head.Token) return new("read_conflict");
            var selected = ReadPinnedPublicDeckVersion(connection, transaction, head, version);
            if (selected is null) return new("not_found");
            var previous = ReadPreviousPinnedPublicDeckVersion(connection, transaction, head, version);
            var metadata = PinnedPublicDeckVersionMetadata(catalog, head, selected, previous);
            // Only the selected body is a consumer. The preceding diff uses counts.
            PreflightOnlineDeckProjection([selected.Payload]);
            transaction.Commit();
            var payload = ExpandRuntimeDeckPayload(selected.Payload);
            var owner = viewer?.Account.Id == row.OwnerId;
            var deck = new L12AccountDeckView(selected.Name, payload.MasterId, payload.MainCards, payload.MoraleCards,
                payload.SpecialCards, selected.CreatedAt, PublicationId: owner ? row.Id : null,
                PublicationVersion: owner ? version : null);
            return new("ok", new(row.Id, row.PublicCode, metadata, deck, head.Token, head.CatalogVersion, policy.Version, owner));
        }
    }
}
