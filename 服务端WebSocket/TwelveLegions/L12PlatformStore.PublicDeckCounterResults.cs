namespace TwelveLegions.Server;

public sealed record L12PublicDeckCounterView(string Id, string PublicCode, int Views, int Likes, int Copies,
    bool ViewerLiked, bool CanEdit);
public sealed record L12PublicDeckCounterResult(string Status, L12PublicDeckCounterView? Counters = null);

public sealed partial class L12PlatformStore
{
    public L12PublicDeckCounterResult UpdatePublicDeckCounter(string reference, string kind,
        L12AuthenticatedSession? viewer = null)
    {
        if (kind is not ("view" or "copy" or "like") || string.IsNullOrWhiteSpace(reference)
            || reference.Length > 64 || reference.Any(char.IsControl)) return new("invalid_request");
        if (kind == "like" && viewer is null) return new("unauthorized");
        using var deployment = EnterDeploymentMutation();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (viewer is not null && (!IsCurrentDeckReader(viewer.Account) || !_data.Sessions.Any(session =>
                session.Id == viewer.SessionId && session.AccountId == viewer.Account.Id && session.RevokedAt is null
                && session.ExpiresAt > now && session.PermissionVersion == viewer.Account.PermissionVersion))) return new("unauthorized");
            if (!CaptureOperationsPolicy().IsFeatureEnabled("publicDecks")) return new("feature_disabled");
            var row = FindPublishedDeck(reference);
            if (row is null) return new("not_found");
            switch (kind)
            {
                case "view": row.Views = IncrementStoredPublishedDeckCounter(row.Id, "views"); break;
                case "copy": row.Copies = IncrementStoredPublishedDeckCounter(row.Id, "copies"); break;
                case "like":
                    var liked = ToggleStoredPublishedDeckLike(viewer!.Account.Id, row.Id);
                    if (liked)
                    {
                        if (!row.LikedByAccountIds.Contains(viewer.Account.Id)) row.LikedByAccountIds.Add(viewer.Account.Id);
                    }
                    else row.LikedByAccountIds.Remove(viewer.Account.Id);
                    break;
            }
            return new("ok", new(row.Id, row.PublicCode, row.Views, row.LikedByAccountIds.Count, row.Copies,
                viewer is not null && row.LikedByAccountIds.Contains(viewer.Account.Id),
                viewer is not null && row.OwnerId == viewer.Account.Id));
        }
    }
}
