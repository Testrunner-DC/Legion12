using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private const int PublicDeckSummaryCacheLimit = 32;
    private sealed record PublicDeckSummaryCacheKey(long DataVersion, string CatalogVersion,
        long PolicyVersion, string? ViewerId, int ViewerPermissionVersion, string InputFingerprint);
    private sealed record PublishedDeckSummaryInput(string Id, string OwnerId, string Author, string Name,
        string MasterId, string PublicCode, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        string PayloadHash, int PayloadFormat, string PayloadJson, int Views, int Likes, int Copies,
        IReadOnlyList<string>? RepresentativeCardIds, bool ViewerLiked);

    private readonly object _publicDeckSummaryCacheGate = new();
    private readonly Dictionary<PublicDeckSummaryCacheKey,
        Lazy<IReadOnlyList<DeckLibraryEntry>>> _publicDeckSummaryCache = [];

    public L12PublicDeckSummaryResult DeckLibrarySummaries(L12Catalog catalog, L12PublicDeckSummaryQuery? query = null,
        L12AuthenticatedSession? viewer = null, bool includeCover = false)
    {
        query ??= new();
        if (!query.IsValid) throw new ArgumentOutOfRangeException(nameof(query));
        while (true)
        {
            PublicDeckSummaryCacheKey cacheKey;
            L12OperationsPolicySnapshot policy;
            bool publicEnabled;
            string catalogVersion;
            IReadOnlyList<PublishedDeckSummaryInput> publishedInputs;
            lock (_gate)
            {
                var data = _data;
                var now = DateTimeOffset.UtcNow;
                if (viewer is not null && (!IsCurrentDeckReader(viewer.Account) || !data.Sessions.Any(session =>
                    session.Id == viewer.SessionId && session.AccountId == viewer.Account.Id && session.RevokedAt is null
                    && session.ExpiresAt > now && session.PermissionVersion == viewer.Account.PermissionVersion))) return new("unauthorized");
                policy = CaptureOperationsPolicy();
                publicEnabled = policy.IsFeatureEnabled("publicDecks");
                if (!publicEnabled && query.Source == "public") return new("feature_disabled");
                catalogVersion = LibraryCatalogVersion(catalog);
                var fingerprint = new System.Text.StringBuilder();
                // Official-only reads must not decode or hash every published deck. Besides
                // avoiding needless work, this preserves the feature-disabled semantics when
                // the public directory is switched off by policy.
                publishedInputs = !publicEnabled || string.Equals(query.Source, "official", StringComparison.Ordinal)
                    ? [] : data.PublishedDecks.Select(row =>
                {
                    var payload = RequireDeckPayload(data, row.PayloadHash, row.MasterId);
                    fingerprint.Append(row.Id).Append('\u001f').Append(row.OwnerId).Append('\u001f')
                        .Append(row.Name).Append('\u001f').Append(row.MasterId).Append('\u001f')
                        .Append(row.PublicCode).Append('\u001f').Append(row.Version).Append('\u001f')
                        .Append(row.CreatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\u001f')
                        .Append(row.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\u001f')
                        .Append(row.PayloadHash).Append('\u001f').Append(row.Views).Append('\u001f')
                        .Append(row.Copies).Append('\u001f').Append(row.LikedByAccountIds.Count).Append('\u001f')
                        .Append(viewer is not null && row.LikedByAccountIds.Contains(viewer.Account.Id)).Append('\u001f');
                    foreach (var representative in row.RepresentativeCardIds ?? [])
                        fingerprint.Append(representative).Append(',');
                    fingerprint.Append('\u001e');
                    var owner = data.Accounts.FirstOrDefault(account => account.Id == row.OwnerId);
                    return new PublishedDeckSummaryInput(row.Id, row.OwnerId,
                        owner is null ? "已注销玩家" : PublicUsername(owner), row.Name, row.MasterId,
                        row.PublicCode, row.Version, row.CreatedAt, row.UpdatedAt, row.PayloadHash,
                        payload.Format, payload.PayloadJson, row.Views, row.LikedByAccountIds.Count,
                        row.Copies, row.RepresentativeCardIds?.ToArray(),
                        viewer is not null && row.LikedByAccountIds.Contains(viewer.Account.Id));
                }).ToArray();
                cacheKey = new(data.Version, catalogVersion, policy.Version, viewer?.Account.Id,
                    viewer?.Account.PermissionVersion ?? 0, fingerprint.ToString());
            }

            Lazy<IReadOnlyList<DeckLibraryEntry>> lazy;
            lock (_publicDeckSummaryCacheGate)
            {
                if (!_publicDeckSummaryCache.TryGetValue(cacheKey, out lazy!))
                {
                    var capturedPublished = publishedInputs;
                    var capturedCatalog = catalog;
                    var capturedPolicy = policy;
                    var capturedViewerId = viewer?.Account.Id;
                    lazy = new(() =>
                    {
                        var result = new List<DeckLibraryEntry>(capturedPublished.Count + capturedCatalog.PresetDecks.Count);
                        if (publicEnabled)
                            result.AddRange(capturedPublished.Select(input =>
                                PublishedLibraryEntry(input, capturedCatalog, capturedPolicy, capturedViewerId)));
                        result.AddRange(capturedCatalog.PresetDecks.Select(deck =>
                            OfficialLibraryEntry(deck, capturedCatalog, capturedPolicy)));
                        return result.ToArray();
                    }, LazyThreadSafetyMode.ExecutionAndPublication);
                    _publicDeckSummaryCache[cacheKey] = lazy;
                }
                if (_publicDeckSummaryCache.Count > PublicDeckSummaryCacheLimit)
                {
                    foreach (var stale in _publicDeckSummaryCache.Keys
                        .Where(key => key.DataVersion != cacheKey.DataVersion)
                        .Take(Math.Max(1, _publicDeckSummaryCache.Count - PublicDeckSummaryCacheLimit))
                        .ToArray())
                        _publicDeckSummaryCache.Remove(stale);
                    while (_publicDeckSummaryCache.Count > PublicDeckSummaryCacheLimit)
                        _publicDeckSummaryCache.Remove(_publicDeckSummaryCache.Keys.First());
                }
            }

            var entries = lazy.Value;
            // Do not publish a projection built from a generation that changed while
            // decoding/validating payloads outside the store lock.
            lock (_gate)
            {
                if (_data.Version != cacheKey.DataVersion || CaptureOperationsPolicy().Version != cacheKey.PolicyVersion)
                    continue;
            }

            var availability = new L12DeckLibrarySourceAvailability(publicEnabled ? "available" : "disabled");
            var sourceEntries = entries.Where(entry => query.Source is "all"
                || string.Equals(entry.View.Source, query.Source, StringComparison.Ordinal)).ToArray();
            if (sourceEntries.Select(entry => entry.View.Id).Distinct(StringComparer.Ordinal).Count() != sourceEntries.Length)
                throw new L12PlatformStorageUnavailableException("统一牌库目录存在重复身份");
            // Fingerprint actual visible metadata, including locally committed counter/like
            // values. Those SQL paths deliberately do not advance _data.Version.
            var generation = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                catalogVersion, policy.Version, policy.VersionId, availability,
                Viewer = viewer?.Account.Id, Permission = viewer?.Account.PermissionVersion,
                // A directory generation tracks visible metadata and counters. Per-item
                // content pins are attached only after paging and never enter this hash.
                Entries = sourceEntries.Select(entry => entry.View with { ReadToken = null, CoverCardIds = null })
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal),
            })));
            var comparison = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;
            var filtered = sourceEntries.Where(entry => string.IsNullOrEmpty(query.Keyword) || new[]
                { entry.View.Name, entry.View.Author, entry.View.MasterName }.Any(value => comparison.IndexOf(value, query.Keyword, CompareOptions.IgnoreCase) >= 0))
                .Where(entry => string.IsNullOrEmpty(query.MasterId) || string.Equals(entry.View.MasterId, query.MasterId, StringComparison.OrdinalIgnoreCase))
                .Where(entry => string.IsNullOrEmpty(query.Faction) || string.Equals(entry.View.Faction, query.Faction, StringComparison.OrdinalIgnoreCase))
                .Where(entry => query.Legal is null || entry.View.Legal == query.Legal)
                .Where(entry => query.Environment is null || entry.View.Environment.Value == query.Environment)
                .Where(entry => string.IsNullOrEmpty(query.CardId) || entry.CardIds.Contains(query.CardId))
                .Where(entry => query.UpdatedAfter is null || entry.View.UpdatedAt is null || entry.View.UpdatedAt >= query.UpdatedAfter).ToArray();
            var facets = new L12PublicDeckSummaryFacets(LibraryFacets(filtered.Select(entry => entry.View.Source)),
                LibraryFacets(filtered.Select(entry => entry.View.MasterId)), LibraryFacets(filtered.Select(entry => entry.View.Faction)),
                LibraryFacets(filtered.Select(entry => entry.View.Environment.Value ?? "unconfigured")),
                LibraryFacets(filtered.SelectMany(entry => entry.CardIds)), filtered.Count(entry => entry.View.Legal), filtered.Count(entry => !entry.View.Legal));
            var views = filtered.Select(entry => entry.View);
            IOrderedEnumerable<L12PublicDeckSummaryView> sorted = query.Sort switch
            {
                "name" => views.OrderBy(entry => entry.Name, StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true)),
                "latest" => views.OrderByDescending(entry => entry.CreatedAt),
                "likes" => views.OrderByDescending(entry => entry.Likes).ThenByDescending(entry => entry.CreatedAt),
                "views" => views.OrderByDescending(entry => entry.Views).ThenByDescending(entry => entry.CreatedAt),
                "copies" => views.OrderByDescending(entry => entry.Copies).ThenByDescending(entry => entry.CreatedAt),
                _ => views.OrderByDescending(entry => (long)entry.Copies * 4 + (long)entry.Likes * 3 + entry.Views).ThenByDescending(entry => entry.UpdatedAt),
            };
            var offset = ((long)query.Page - 1) * query.PageSize;
            var items = offset >= filtered.Length ? [] : sorted.ThenBy(entry => entry.Id, StringComparer.Ordinal).Skip((int)offset).Take(query.PageSize).ToArray();
            var publishedById = publishedInputs.ToDictionary(input => input.Id, StringComparer.Ordinal);
            L12PublicDeckSummaryView[] pinned;
            try
            {
                using var connection = OpenDatabase(_databasePath, readOnly: true);
                using var transaction = connection.BeginTransaction(deferred: true);
                ValidatePublicDeckReadGeneration(connection, transaction, cacheKey.DataVersion);
                pinned = new L12PublicDeckSummaryView[items.Length];
                for (var index = 0; index < items.Length; index++)
                {
                    var item = includeCover ? items[index] : items[index] with { CoverCardIds = null };
                    if (item.Source == "official")
                    {
                        pinned[index] = item with { ReadToken = null };
                        continue;
                    }
                    if (!publishedById.TryGetValue(item.Id, out var input))
                        throw new InvalidDataException("公开牌库摘要页引用了不存在的已提交目录项");
                    var row = PublishedDeckRowFromSummaryInput(input);
                    var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer,
                        catalogVersion, input.Author, cacheKey.DataVersion);
                    pinned[index] = item with { ReadToken = head.Token };
                }
                transaction.Commit();
            }
            catch (InvalidDataException error) when (error.Message.Contains("代际不一致", StringComparison.Ordinal))
            {
                continue;
            }

            lock (_gate)
            {
                if (_data.Version != cacheKey.DataVersion || CaptureOperationsPolicy().Version != cacheKey.PolicyVersion)
                    continue;
            }
            return new("ok", new(pinned, filtered.Length, query.Page, query.PageSize, generation, catalogVersion,
                policy.Version, availability, facets));
        }
    }

    private static PublishedDeckRow PublishedDeckRowFromSummaryInput(PublishedDeckSummaryInput input)
        => new()
        {
            Id = input.Id, OwnerId = input.OwnerId, Name = input.Name, MasterId = input.MasterId,
            PublicCode = input.PublicCode, Version = input.Version, CreatedAt = input.CreatedAt,
            UpdatedAt = input.UpdatedAt, PayloadHash = input.PayloadHash,
            RepresentativeCardIds = input.RepresentativeCardIds?.ToList(),
        };

    private static IReadOnlyList<L12DeckSummaryFacet> LibraryFacets(IEnumerable<string> values)
        => values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new L12DeckSummaryFacet(group.Key, group.Count())).ToArray();
}
