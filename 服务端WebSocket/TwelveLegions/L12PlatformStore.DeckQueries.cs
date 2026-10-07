using System.Globalization;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private bool IsCurrentDeckReader(L12AccountView actor)
        => _data.Accounts.Any(account => account.Id == actor.Id && !account.Deleted && !account.Disabled
            && account.PermissionVersion == actor.PermissionVersion);

    public L12PrivateDeckReadResult ReadPrivateDeck(L12AccountView actor, string id, long? expectedRevision = null)
    {
        if (expectedRevision is < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        lock (_gate)
        {
            if (!IsCurrentDeckReader(actor)) return new("unauthorized");
            var row = _data.Decks.FirstOrDefault(deck => deck.AccountId == actor.Id && deck.Id == id);
            if (row is null) return new("not_found");
            if (expectedRevision is not null && row.Revision != expectedRevision)
                return new("revision_conflict", CurrentRevision: row.Revision);
            return new("ok", ToView(row));
        }
    }

    public L12PrivateDeckSummaryPage? PrivateDeckSummaries(L12AccountView actor, L12Catalog catalog,
        L12PrivateDeckQuery? query = null)
    {
        query ??= new();
        if (!query.IsValid) throw new ArgumentOutOfRangeException(nameof(query));
        lock (_gate)
        {
            if (!IsCurrentDeckReader(actor)) return null;
            var data = _data;
            var policy = EffectiveOperationsPolicy();
            var summaries = data.Decks.Where(deck => deck.AccountId == actor.Id)
                .Where(deck => query.ExactName is null || DeckNameKey(deck.Name) == DeckNameKey(query.ExactName))
                .Where(deck => query.PublicationId is null || string.Equals(deck.PublicationId,
                    query.PublicationId.Trim(), StringComparison.Ordinal))
                .Select(deck => PrivateDeckSummary(data, deck, catalog, policy.CardRestrictions))
                .Where(deck => string.IsNullOrEmpty(query.Keyword)
                    || CultureInfo.GetCultureInfo("zh-CN").CompareInfo.IndexOf(deck.Name, query.Keyword, CompareOptions.IgnoreCase) >= 0)
                .Where(deck => string.IsNullOrEmpty(query.MasterId)
                    || string.Equals(deck.MasterId, query.MasterId, StringComparison.OrdinalIgnoreCase))
                .Where(deck => query.Legal is null || deck.Legal == query.Legal).ToArray();
            var facets = new L12PrivateDeckFacets(summaries.GroupBy(deck => deck.MasterId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new L12DeckSummaryMasterFacet(group.Key, group.Count())).ToArray(),
                summaries.Count(deck => deck.Legal), summaries.Count(deck => !deck.Legal));
            var sorted = query.Sort == "name"
                ? summaries.OrderBy(deck => deck.Name, StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true)).ThenBy(deck => deck.Id, StringComparer.Ordinal)
                : summaries.OrderByDescending(deck => deck.UpdatedAt).ThenBy(deck => deck.Id, StringComparer.Ordinal);
            var offset = ((long)query.Page - 1) * query.PageSize;
            var items = offset >= summaries.Length ? [] : sorted.Skip((int)offset).Take(query.PageSize).ToArray();
            return new(items, summaries.Length, query.Page, query.PageSize, data.Version, actor.PermissionVersion,
                DeckSummaryCatalogVersion(catalog), policy.Version, facets);
        }
    }
}
