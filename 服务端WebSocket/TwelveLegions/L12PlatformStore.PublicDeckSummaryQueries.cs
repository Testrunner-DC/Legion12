using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    public L12PublicDeckSummaryResult DeckLibrarySummaries(L12Catalog catalog, L12PublicDeckSummaryQuery? query = null,
        L12AuthenticatedSession? viewer = null)
    {
        query ??= new();
        if (!query.IsValid) throw new ArgumentOutOfRangeException(nameof(query));
        lock (_gate)
        {
            var data = _data;
            var now = DateTimeOffset.UtcNow;
            if (viewer is not null && (!IsCurrentDeckReader(viewer.Account) || !data.Sessions.Any(session =>
                session.Id == viewer.SessionId && session.AccountId == viewer.Account.Id && session.RevokedAt is null
                && session.ExpiresAt > now && session.PermissionVersion == viewer.Account.PermissionVersion))) return new("unauthorized");
            var policy = CaptureOperationsPolicy();
            var publicEnabled = policy.IsFeatureEnabled("publicDecks");
            if (!publicEnabled && query.Source == "public") return new("feature_disabled");
            var catalogVersion = LibraryCatalogVersion(catalog);
            var availability = new L12DeckLibrarySourceAvailability(publicEnabled ? "available" : "disabled");
            var entries = new List<DeckLibraryEntry>();
            if (query.Source is "all" or "public" && publicEnabled)
                entries.AddRange(data.PublishedDecks.Select(row => PublishedLibraryEntry(data, row, catalog, policy, viewer?.Account.Id)));
            if (query.Source is "all" or "official")
                entries.AddRange(catalog.PresetDecks.Select(deck => OfficialLibraryEntry(deck, catalog, policy)));
            if (entries.Select(entry => entry.View.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                throw new L12PlatformStorageUnavailableException("统一牌库目录存在重复身份");
            // Fingerprint actual visible metadata, including locally committed counter/like
            // values. Those SQL paths deliberately do not advance _data.Version.
            var generation = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                catalogVersion, policy.Version, policy.VersionId, availability,
                Viewer = viewer?.Account.Id, Permission = viewer?.Account.PermissionVersion,
                Entries = entries.Select(entry => entry.View).OrderBy(entry => entry.Id, StringComparer.Ordinal),
            })));
            var comparison = CultureInfo.GetCultureInfo("zh-CN").CompareInfo;
            var filtered = entries.Where(entry => string.IsNullOrEmpty(query.Keyword) || new[]
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
            return new("ok", new(items, filtered.Length, query.Page, query.PageSize, generation, catalogVersion, policy.Version, availability, facets));
        }
    }

    private static IReadOnlyList<L12DeckSummaryFacet> LibraryFacets(IEnumerable<string> values)
        => values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new L12DeckSummaryFacet(group.Key, group.Count())).ToArray();
}
