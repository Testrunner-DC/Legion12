using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckSummaryQuery(string Source = "all", int Page = 1, int PageSize = 30,
    string? Keyword = null, string? MasterId = null, string? Faction = null, bool? Legal = null,
    string? Environment = null, string? CardId = null, DateTimeOffset? UpdatedAfter = null, string Sort = "trend")
{
    public bool IsValid => Source is "all" or "public" or "official" && Page > 0 && PageSize is > 0 and <= 100
        && Sort is "trend" or "copies" or "likes" or "views" or "latest" or "name"
        && Environment is null or "1.0" or "2.0" or "2.5"
        && (Keyword?.Length ?? 0) <= 128 && (MasterId?.Length ?? 0) <= 64 && (Faction?.Length ?? 0) <= 32
        && (CardId?.Length ?? 0) <= 64 && (UpdatedAfter is null || UpdatedAfter.Value.Offset == TimeSpan.Zero);

    public static bool TryParse(IQueryCollection values, out L12PublicDeckSummaryQuery query)
    {
        query = new();
        var allowed = new HashSet<string>(["source", "page", "pageSize", "keyword", "masterId", "faction", "legal",
            "environment", "cardId", "updatedAfter", "sort"], StringComparer.Ordinal);
        if (values.Any(item => !allowed.Contains(item.Key) || item.Value.Count != 1 || item.Value[0] is null
            || item.Value[0]!.Length > 128 || item.Value[0]!.Any(char.IsControl))) return false;
        string? Text(string key) => values.TryGetValue(key, out var raw) ? raw[0] : null;
        bool Integer(string key, int fallback, out int value)
        {
            value = fallback;
            return Text(key) is not { } raw || int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
        }
        if (!Integer("page", 1, out var page) || !Integer("pageSize", 30, out var size)) return false;
        bool? legal = null;
        if (Text("legal") is { } rawLegal)
        {
            if (rawLegal is not ("true" or "false")) return false;
            legal = rawLegal == "true";
        }
        DateTimeOffset? updatedAfter = null;
        if (Text("updatedAfter") is { } rawDate)
        {
            if (!rawDate.Contains('T') || !(rawDate.EndsWith('Z') || rawDate.EndsWith("+00:00", StringComparison.Ordinal))
                || !DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date.Offset != TimeSpan.Zero) return false;
            updatedAfter = date;
        }
        query = new(Text("source") ?? "all", page, size, Text("keyword")?.Trim(), Text("masterId")?.Trim(),
            Text("faction")?.Trim(), legal, Text("environment"), Text("cardId")?.Trim(), updatedAfter, Text("sort") ?? "trend");
        return query.IsValid;
    }
}

public sealed record L12PublicDeckSummaryView(string Id, string Source, string Name, string MasterId,
    string MasterName, string Faction, string Author, string? PublicCode, int? PublicationVersion,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, L12DeckSummaryCounts Counts,
    bool Legal, string? LegalityReason, L12DeckEnvironmentView Environment,
    int Views, int Likes, int Copies, bool ViewerLiked, bool CanEdit);
public sealed record L12DeckSummaryFacet(string Value, int Count);
public sealed record L12PublicDeckSummaryFacets(IReadOnlyList<L12DeckSummaryFacet> Sources,
    IReadOnlyList<L12DeckSummaryFacet> Masters, IReadOnlyList<L12DeckSummaryFacet> Factions,
    IReadOnlyList<L12DeckSummaryFacet> Environments, IReadOnlyList<L12DeckSummaryFacet> Cards, int Legal, int Illegal);
public sealed record L12DeckLibrarySourceAvailability(string Public, string Official = "available");
public sealed record L12PublicDeckSummaryPage(IReadOnlyList<L12PublicDeckSummaryView> Items, int Total, int Page,
    int PageSize, string Generation, string CatalogVersion, long PolicyVersion,
    L12DeckLibrarySourceAvailability SourceAvailability, L12PublicDeckSummaryFacets Facets);
public sealed record L12PublicDeckSummaryResult(string Status, L12PublicDeckSummaryPage? Page = null);
