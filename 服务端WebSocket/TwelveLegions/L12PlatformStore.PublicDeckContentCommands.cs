namespace TwelveLegions.Server;

public sealed record L12PublicDeckContentHeadWriteView(string Id, string PublicCode,
    L12PublicDeckGuideView Guide, IReadOnlyList<L12PublicDeckMatchupView> Matchups,
    int ContentRevision, DateTimeOffset? ContentUpdatedAt, string ReadToken,
    string CatalogVersion, long PolicyVersion, bool CanEdit);
public sealed record L12PublicDeckContentHeadWriteResult(string Status,
    L12PublicDeckContentHeadWriteView? Head = null);

public sealed partial class L12PlatformStore
{
    internal Action<string>? PublicDeckContentWriteFaultHook { get; set; }

    public L12PublicDeckContentHeadWriteResult WritePublicDeckContentCurrent(L12Catalog catalog,
        string reference, string expectedReadToken, L12AuthenticatedSession actor, L12PublicDeckContentInput input)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (!IsPublicDeckReadReference(reference) || !IsLowerPublicDeckReadToken(expectedReadToken))
            return new("invalid_request");
        using var deployment = EnterDeploymentMutation();
        lock (_gate)
        {
            if (!IsCurrentPublicDeckReader(actor)) return new("unauthorized");
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return new("feature_disabled");
            var published = FindPublishedDeck(reference);
            if (published is null) return new("not_found");
            if (!string.Equals(published.OwnerId, actor.Account.Id, StringComparison.Ordinal))
                return new("forbidden");
            if (!_storageWritable)
                throw new L12PlatformStorageUnavailableException("公开牌库内容写入需要可写事务存储");

            using var connection = OpenDatabase(_databasePath, readOnly: false);
            using var transaction = connection.BeginTransaction();
            var catalogVersion = LibraryCatalogVersion(catalog);
            var before = CapturePublicDeckReadHead(connection, transaction, published, catalog, policy, actor,
                catalogVersion);
            if (!string.Equals(before.Token, expectedReadToken, StringComparison.Ordinal))
                return new("read_conflict");
            var current = ReadPinnedPublicDeckContent(connection, transaction, before);
            var content = WritePublicDeckContentCanonical(connection, transaction, published, actor.Account.Id,
                input, current);
            var after = CapturePublicDeckReadHead(connection, transaction, published, catalog, policy, actor,
                catalogVersion);
            if (after.ContentRevision != content.Revision || after.ContentUpdatedAt != content.UpdatedAt)
                throw new InvalidDataException("公开牌库内容写入头不一致");
            var persisted = ReadPinnedPublicDeckContent(connection, transaction, after);
            if (persisted.Revision != content.Revision || persisted.UpdatedAt != content.UpdatedAt
                || persisted.Guide != content.Guide || !persisted.Matchups.SequenceEqual(content.Matchups))
                throw new InvalidDataException("公开牌库内容写入正文不一致");
            PublicDeckContentWriteFaultHook?.Invoke("before-commit");
            transaction.Commit();
            return new("ok", new(after.Id, published.PublicCode, persisted.Guide, persisted.Matchups,
                persisted.Revision, persisted.UpdatedAt, after.Token, after.CatalogVersion, policy.Version, true));
        }
    }

    private static bool IsLowerPublicDeckReadToken(string value)
        => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
