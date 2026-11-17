using System.Globalization;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Building a private-deck directory used to happen entirely under _gate.  A
    // directory request consequently held up every other platform read/write while
    // it decoded and validated every deck.  Keep one immutable projection per
    // account/version and single-flight its construction; filtering and paging are
    // then just in-memory work.
    private const int PrivateDeckSummaryCacheLimit = 64;
    private sealed record PrivateDeckSummaryCacheKey(string AccountId, long DataVersion,
        string CatalogVersion, long PolicyVersion, int PermissionVersion, string InputFingerprint,
        bool IncludeCover);
    private sealed record PrivateDeckSummaryInput(string Id, long Revision, string Name, string MasterId,
        DateTimeOffset UpdatedAt, string? PublicationId, int? PublicationVersion, string PayloadHash,
        int PayloadFormat, string PayloadJson, string BenchJson, IReadOnlyList<string>? RepresentativeCardIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>?> AlternateArtCopies);

    private readonly object _privateDeckSummaryCacheGate = new();
    private readonly Dictionary<PrivateDeckSummaryCacheKey,
        Lazy<IReadOnlyList<L12PrivateDeckSummaryView>>> _privateDeckSummaryCache = [];

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
        L12PrivateDeckQuery? query = null, bool includeCover = false)
    {
        query ??= new();
        if (!query.IsValid) throw new ArgumentOutOfRangeException(nameof(query));
        var catalogVersion = DeckSummaryCatalogVersion(catalog);
        while (true)
        {
            PrivateDeckSummaryCacheKey cacheKey;
            IReadOnlyList<PrivateDeckSummaryInput> inputs;
            L12OperationsPolicySnapshot policy;
            lock (_gate)
            {
                if (!IsCurrentDeckReader(actor)) return null;
                var data = _data;
                policy = CaptureOperationsPolicy();
                var fingerprint = new System.Text.StringBuilder();
                // Copy only the fields needed by the projection while the state is
                // protected.  Payload decoding/validation is deliberately deferred
                // until after the lock is released.
                inputs = data.Decks.Where(deck => deck.AccountId == actor.Id)
                    .Select(deck =>
                    {
                        var payload = RequireDeckPayload(data, deck.PayloadHash, deck.MasterId);
                        fingerprint.Append(deck.Id).Append('\u001f').Append(deck.Revision).Append('\u001f')
                            .Append(deck.Name).Append('\u001f').Append(deck.MasterId).Append('\u001f')
                            .Append(deck.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\u001f')
                            .Append(deck.PublicationId).Append('\u001f').Append(deck.PublicationVersion).Append('\u001f')
                            .Append(deck.PayloadHash).Append('\u001f').Append(DeckBenchJson(deck)).Append('\u001f');
                        foreach (var representative in deck.RepresentativeCardIds ?? [])
                            fingerprint.Append(representative).Append(',');
                        fingerprint.Append('\u001f');
                        foreach (var copy in (deck.AlternateArtCopies ?? []).OrderBy(item => item.Key,
                            StringComparer.OrdinalIgnoreCase))
                            fingerprint.Append(copy.Key).Append('=').Append(string.Join(',', copy.Value)).Append(';');
                        fingerprint.Append('\u001e');
                        return new PrivateDeckSummaryInput(deck.Id, deck.Revision, deck.Name, deck.MasterId,
                            deck.UpdatedAt, deck.PublicationId, deck.PublicationVersion, deck.PayloadHash,
                            payload.Format, payload.PayloadJson, DeckBenchJson(deck),
                            deck.RepresentativeCardIds?.ToArray(),
                            (deck.AlternateArtCopies ?? []).ToDictionary(item => item.Key,
                            item => (IReadOnlyList<string>?)item.Value.ToArray(),
                                StringComparer.OrdinalIgnoreCase));
                    }).ToArray();
                cacheKey = new(actor.Id, data.Version, catalogVersion, policy.Version,
                    actor.PermissionVersion, fingerprint.ToString(), includeCover);
            }

            Lazy<IReadOnlyList<L12PrivateDeckSummaryView>> lazy;
            lock (_privateDeckSummaryCacheGate)
            {
                if (!_privateDeckSummaryCache.TryGetValue(cacheKey, out lazy!))
                {
                    var capturedInputs = inputs;
                    var capturedCatalog = catalog;
                    var capturedRestrictions = policy.CardRestrictions;
                    lazy = new(() =>
                    {
                        PrivateDeckSummaryProjectionObserver?.Invoke();
                        return capturedInputs.Select(input =>
                            PrivateDeckSummary(input, capturedCatalog, capturedRestrictions, includeCover)).ToArray();
                    },
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    _privateDeckSummaryCache[cacheKey] = lazy;
                }

                // Versioned keys naturally invalidate old projections.  Bound the
                // dictionary so a busy account/deck churn cannot grow it forever.
                if (_privateDeckSummaryCache.Count > PrivateDeckSummaryCacheLimit)
                {
                    foreach (var stale in _privateDeckSummaryCache.Keys
                        .Where(key => key.DataVersion != cacheKey.DataVersion)
                        .Take(Math.Max(1, _privateDeckSummaryCache.Count - PrivateDeckSummaryCacheLimit))
                        .ToArray())
                        _privateDeckSummaryCache.Remove(stale);
                    while (_privateDeckSummaryCache.Count > PrivateDeckSummaryCacheLimit)
                        _privateDeckSummaryCache.Remove(_privateDeckSummaryCache.Keys.First());
                }
            }

            var allSummaries = lazy.Value;
            // A write may have committed while the lock-free projection was being
            // built.  Never return a page from the old generation; retry against the
            // latest snapshot instead.
            lock (_gate)
            {
                if (!IsCurrentDeckReader(actor) || _data.Version != cacheKey.DataVersion
                    || EffectiveOperationsPolicy().Version != cacheKey.PolicyVersion)
                    continue;
            }

            var summaries = allSummaries
                .Where(deck => query.ExactName is null || DeckNameKey(deck.Name) == DeckNameKey(query.ExactName))
                .Where(deck => query.PublicationId is null || string.Equals(deck.PublicationId,
                    query.PublicationId.Trim(), StringComparison.Ordinal))
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
            return new(items, summaries.Length, query.Page, query.PageSize, cacheKey.DataVersion,
                actor.PermissionVersion, cacheKey.CatalogVersion, cacheKey.PolicyVersion, facets);
        }
    }
}
