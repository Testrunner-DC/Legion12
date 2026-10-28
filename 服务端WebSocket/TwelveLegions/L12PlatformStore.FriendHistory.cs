namespace TwelveLegions.Server;

public sealed record L12HistoryPreferenceView(string AccountId, bool PublicHistory);
internal sealed record L12FriendHistoryReadLease(string ViewerId, string SessionId, int PermissionVersion,
    string TargetId, string TargetName, IReadOnlyList<string> ExcludedMatches,
    IReadOnlyList<string> ExcludedAccounts, L12SeasonConfig Season);

public sealed partial class L12PlatformStore
{
    internal (string Status, L12HistoryPreferenceView? Value) SetOwnHistoryPreference(
        string? authorization, bool publicHistory)
    {
        lock (_gate)
        {
            var viewer = AuthenticateSession(authorization);
            if (viewer is null) return ("unauthorized", null);
            var row = _data.Accounts.First(item => item.Id == viewer.Account.Id);
            if (row.PublicHistory != publicHistory)
            {
                row.PublicHistory = publicHistory;
                Save();
            }
            return ("available", new(row.Id, row.PublicHistory));
        }
    }

    internal (string Status, L12FriendHistoryReadLease? Lease) CaptureFriendHistoryRead(
        string? authorization, string targetId)
    {
        lock (_gate)
        {
            var viewer = AuthenticateSession(authorization);
            if (viewer is null) return ("unauthorized", null);
            if (string.IsNullOrWhiteSpace(targetId) || targetId.Length > 64) return ("not_found", null);
            var target = _data.Accounts.FirstOrDefault(item => item.Id == targetId && !item.Disabled && !item.Deleted);
            if (target is null || FindFriendRow(viewer.Account.Id, targetId)?.Status != "accepted"
                || _data.BlockedAccounts.Any(item => item.AccountId == viewer.Account.Id && item.BlockedAccountId == targetId
                    || item.AccountId == targetId && item.BlockedAccountId == viewer.Account.Id)) return ("not_found", null);
            if (!target.PublicHistory) return ("private", null);
            return ("available", new(viewer.Account.Id, viewer.SessionId, viewer.Account.PermissionVersion,
                target.Id, PublicUsername(target), RankedIntegrityExcludedMatchIds().Order(StringComparer.Ordinal).ToArray(),
                StatisticsExcludedAccountIds().Order(StringComparer.Ordinal).ToArray(), CaptureOperationsPolicy().Season));
        }
    }

    internal string RevalidateFriendHistoryRead(string? authorization, L12FriendHistoryReadLease lease)
    {
        lock (_gate)
        {
            var current = CaptureFriendHistoryRead(authorization, lease.TargetId);
            if (current.Lease is not { } next) return current.Status;
            if (next.ViewerId != lease.ViewerId || next.SessionId != lease.SessionId
                || next.PermissionVersion != lease.PermissionVersion) return "unauthorized";
            return next.TargetName != lease.TargetName || !next.ExcludedMatches.SequenceEqual(lease.ExcludedMatches)
                || !next.ExcludedAccounts.SequenceEqual(lease.ExcludedAccounts) ? "read_conflict" : "available";
        }
    }
}
