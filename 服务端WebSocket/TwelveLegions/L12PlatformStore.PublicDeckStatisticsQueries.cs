using System.Security.Cryptography;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckStatisticsPage(string Id, string PublicCode, string ReadToken,
    string CatalogVersion, long PolicyVersion, DateTimeOffset From, DateTimeOffset To, int RecentDays,
    int Games, string SampleStatus, IReadOnlyList<L12PublicDeckVersionStatisticView> Groups,
    int Total, int Page, int PageSize);

internal sealed record L12PublicDeckStatisticsReadLease(string Id, string PublicCode, int PublicationVersion,
    string ReadToken, string CatalogVersion, long PolicyVersion, string? ViewerId, string? SessionId,
    int? PermissionVersion, IReadOnlyList<string> ExcludedMatchIds, IReadOnlyList<string> ExcludedAccountIds,
    string ExclusionFingerprint);
internal sealed record L12PublicDeckStatisticsReadCapture(string Status,
    L12PublicDeckStatisticsReadLease? Lease = null);

public sealed partial class L12PlatformStore
{
    internal L12PublicDeckStatisticsReadCapture CapturePublicDeckStatisticsRead(L12Catalog catalog,
        string reference, string? expectedReadToken, L12AuthenticatedSession? viewer)
    {
        if (!IsPublicDeckReadReference(reference) || !L12PublicDeckReadQuery.IsValidToken(expectedReadToken))
            return new("invalid_request");
        lock (_gate)
        {
            if (!IsCurrentPublicDeckReader(viewer)) return new("unauthorized");
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return new("feature_disabled");
            var row = FindPublishedDeck(reference);
            if (row is null) return new("not_found");
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            ValidatePublicDeckReadGeneration(connection, transaction);
            var catalogVersion = LibraryCatalogVersion(catalog);
            var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer, catalogVersion);
            if (expectedReadToken is not null && expectedReadToken != head.Token) return new("read_conflict");
            var excludedMatches = RankedIntegrityExcludedMatchIds().Order(StringComparer.Ordinal).ToArray();
            var excludedAccounts = StatisticsExcludedAccountIds().Order(StringComparer.Ordinal).ToArray();
            var fingerprint = PublicDeckStatisticsExclusionFingerprint(excludedMatches, excludedAccounts);
            transaction.Commit();
            return new("available", new(head.Id, row.PublicCode, head.Version, head.Token, head.CatalogVersion,
                policy.Version, viewer?.Account.Id, viewer?.SessionId, viewer?.Account.PermissionVersion,
                excludedMatches, excludedAccounts, fingerprint));
        }
    }

    internal string RevalidatePublicDeckStatisticsRead(L12Catalog catalog,
        L12PublicDeckStatisticsReadLease lease, L12AuthenticatedSession? viewer)
    {
        lock (_gate)
        {
            if (viewer?.Account.Id != lease.ViewerId || viewer?.SessionId != lease.SessionId
                || viewer?.Account.PermissionVersion != lease.PermissionVersion || !IsCurrentPublicDeckReader(viewer))
                return "unauthorized";
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return "feature_disabled";
            var row = FindPublishedDeck(lease.Id);
            if (row is null) return "read_conflict";
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            ValidatePublicDeckReadGeneration(connection, transaction);
            var catalogVersion = LibraryCatalogVersion(catalog);
            var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer, catalogVersion);
            var excludedMatches = RankedIntegrityExcludedMatchIds().Order(StringComparer.Ordinal).ToArray();
            var excludedAccounts = StatisticsExcludedAccountIds().Order(StringComparer.Ordinal).ToArray();
            var fingerprint = PublicDeckStatisticsExclusionFingerprint(excludedMatches, excludedAccounts);
            transaction.Commit();
            return row.PublicCode != lease.PublicCode || head.Version != lease.PublicationVersion
                || head.Token != lease.ReadToken || head.CatalogVersion != lease.CatalogVersion
                || policy.Version != lease.PolicyVersion || fingerprint != lease.ExclusionFingerprint
                ? "read_conflict" : "available";
        }
    }

    private static string PublicDeckStatisticsExclusionFingerprint(IReadOnlyList<string> excludedMatchIds,
        IReadOnlyList<string> excludedAccountIds)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Matches = excludedMatchIds,
            Accounts = excludedAccountIds,
        }))).ToLowerInvariant();
}
