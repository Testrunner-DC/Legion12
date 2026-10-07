using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed record L12PrivateDeckQuery(int Page = 1, int PageSize = 30,
    string? Keyword = null, string? MasterId = null, bool? Legal = null, string Sort = "latest")
{
    public bool IsValid => Page > 0 && PageSize is > 0 and <= 100 && Sort is "latest" or "name";

    public static bool TryParse(IQueryCollection values, out L12PrivateDeckQuery query)
    {
        query = new();
        var allowed = new HashSet<string>(["page", "pageSize", "keyword", "masterId", "legal", "sort"], StringComparer.Ordinal);
        if (values.Any(item => !allowed.Contains(item.Key) || item.Value.Count != 1)) return false;
        bool Integer(string key, int fallback, out int value)
        {
            value = fallback;
            return !values.TryGetValue(key, out var raw)
                || int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
        }
        if (!Integer("page", 1, out var page) || !Integer("pageSize", 30, out var size) || size > 100) return false;
        bool? legal = null;
        if (values.TryGetValue("legal", out var rawLegal))
        {
            if (rawLegal[0] is not ("true" or "false")) return false;
            legal = rawLegal[0] == "true";
        }
        var sort = values.TryGetValue("sort", out var rawSort) ? rawSort[0] : "latest";
        if (sort is not ("latest" or "name")) return false;
        query = new(page, size, values["keyword"].FirstOrDefault()?.Trim(),
            values["masterId"].FirstOrDefault()?.Trim(), legal, sort);
        return true;
    }

    public static bool TryParseRevision(IQueryCollection values, out long? revision)
    {
        revision = null;
        if (values.Any(item => item.Key != "expectedRevision" || item.Value.Count != 1)) return false;
        if (!values.TryGetValue("expectedRevision", out var raw)) return true;
        if (!long.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1) return false;
        revision = parsed;
        return true;
    }
}

public sealed record L12DeckSummaryCounts(long Main, long UncountedMain, long Morale, long Special, long Bench);
public sealed record L12PrivateDeckSummaryView(string Id, long Revision, string Name, string MasterId,
    DateTimeOffset UpdatedAt, string? PublicationId, int? PublicationVersion,
    L12DeckSummaryCounts Counts, bool Legal, string? LegalityReason);
public sealed record L12DeckSummaryMasterFacet(string MasterId, int Count);
public sealed record L12PrivateDeckFacets(IReadOnlyList<L12DeckSummaryMasterFacet> Masters, int Legal, int Illegal);
public sealed record L12PrivateDeckSummaryPage(IReadOnlyList<L12PrivateDeckSummaryView> Items, int Total,
    int Page, int PageSize, long Generation, int PermissionVersion, string CatalogVersion, long PolicyVersion,
    L12PrivateDeckFacets Facets);
public sealed record L12PrivateDeckReadResult(string Status, L12AccountDeckView? Deck = null, long? CurrentRevision = null);
