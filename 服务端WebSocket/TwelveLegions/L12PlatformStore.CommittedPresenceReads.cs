using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TwelveLegions.Server;

internal sealed record L12CommittedPresencePlatformView(
    IReadOnlyDictionary<string, string> Usernames,
    IReadOnlyDictionary<string, L12FriendView> Relationships,
    IReadOnlySet<string> AcceptedFriendIds);

public sealed partial class L12PlatformStore
{
    private sealed record CommittedPresenceFriend(string RequesterId, string AddresseeId,
        string Status, DateTimeOffset CreatedAt);
    private sealed record CommittedPresenceReads(FrozenDictionary<string, string> Usernames,
        ImmutableArray<CommittedPresenceFriend> Friends,
        FrozenSet<(string AccountId, string BlockedAccountId)> Blocked);

    private static CommittedPresenceReads PrepareCommittedPresenceReads(DataFile data)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var account in data.Accounts) names.TryAdd(account.Id, PublicUsername(account));
        return new(names.ToFrozenDictionary(StringComparer.Ordinal),
            data.Friends.Select(row => new CommittedPresenceFriend(row.RequesterId,
                row.AddresseeId, row.Status, row.CreatedAt)).ToImmutableArray(),
            data.BlockedAccounts.Select(row => (row.AccountId, row.BlockedAccountId)).ToFrozenSet());
    }

    internal L12CommittedPresencePlatformView ReadCommittedPresencePlatformView(string viewerId)
    {
        CommittedPresenceReads reads;
        if (Monitor.IsEntered(_gate)) reads = PrepareCommittedPresenceReads(_data);
        else
        {
            var committed = Volatile.Read(ref _committedSessionActivity);
            if (!committed.Available || committed.PresenceReads is null)
                throw new L12PlatformStorageUnavailableException("平台已提交在线读取状态不可恢复");
            reads = committed.PresenceReads;
        }
        var rows = reads.Friends.Where(row => row.RequesterId == viewerId || row.AddresseeId == viewerId).ToArray();
        var first = new Dictionary<string, CommittedPresenceFriend>(StringComparer.Ordinal);
        foreach (var row in rows)
            first.TryAdd(row.RequesterId == viewerId ? row.AddresseeId : row.RequesterId, row);
        var accepted = first.Where(pair => pair.Value.Status == "accepted")
            .Select(pair => pair.Key).ToFrozenSet(StringComparer.Ordinal);
        var friends = new Dictionary<string, L12FriendView>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in rows.Where(row => row.Status == "accepted")
            .Select(row => row.RequesterId == viewerId ? row.AddresseeId : row.RequesterId)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!reads.Usernames.TryGetValue(other, out var name)) continue;
            var relation = first[other];
            var hidden = relation.Status == "suppressed" && relation.RequesterId != viewerId;
            var blocked = reads.Blocked.Contains((viewerId, other));
            friends.Add(other, new(other, name, blocked ? "blocked" : hidden ? "none"
                : relation.Status == "suppressed" ? "pending" : relation.Status,
                blocked || hidden ? "none" : relation.AddresseeId == viewerId ? "incoming" : "outgoing",
                relation.CreatedAt));
        }
        // Preserve request ordering and duplicate/missing-authority rejection rather than
        // allowing a malformed pending row to change an accepted relationship silently.
        var pending = rows.Where(row => row.Status == "pending"
                || row.Status == "suppressed" && row.RequesterId == viewerId)
            .OrderByDescending(row => row.CreatedAt).ToDictionary(
                row => row.RequesterId == viewerId ? row.AddresseeId : row.RequesterId,
                row => new L12FriendView(row.RequesterId == viewerId ? row.AddresseeId : row.RequesterId,
                    reads.Usernames[row.RequesterId == viewerId ? row.AddresseeId : row.RequesterId],
                    "pending", row.AddresseeId == viewerId ? "incoming" : "outgoing", row.CreatedAt),
                StringComparer.OrdinalIgnoreCase);
        foreach (var (other, relation) in pending) friends.TryAdd(other, relation);
        return new(reads.Usernames, friends.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), accepted);
    }
}
