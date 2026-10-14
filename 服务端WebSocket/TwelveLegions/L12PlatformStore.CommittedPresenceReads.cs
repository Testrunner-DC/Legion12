using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TwelveLegions.Server;

internal sealed record L12CommittedPresencePlatformView(
    IReadOnlyDictionary<string, string> Usernames,
    IReadOnlyDictionary<string, L12FriendView> Relationships,
    IReadOnlySet<string> AcceptedFriendIds);

// A relationship snapshot only: not session authentication or permission to start a send.
internal readonly record struct L12CommittedChatRelationshipView(
    long Revision, bool Available, bool AcceptedAndUnblocked);

public sealed partial class L12PlatformStore
{
    private sealed record CommittedPresenceFriend(string RequesterId, string AddresseeId,
        string Status, DateTimeOffset CreatedAt);
    private sealed record CommittedPresenceReads(FrozenDictionary<string, string> Usernames,
        ImmutableArray<CommittedPresenceFriend> Friends,
        FrozenSet<(string AccountId, string BlockedAccountId)> Blocked,
        FrozenDictionary<(string FirstId, string SecondId), bool> ChatRelationships);

    private CommittedPresenceReads PrepareCommittedPresenceReads(DataFile data, bool prepareChatIndex = true)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var account in data.Accounts) names.TryAdd(account.Id, PublicUsername(account));
        var friends = data.Friends.Select(row => new CommittedPresenceFriend(row.RequesterId,
            row.AddresseeId, row.Status, row.CreatedAt)).ToImmutableArray();
        var pairs = prepareChatIndex
            ? PrepareCommittedChatPairs(friends, Volatile.Read(ref _committedSessionActivity).PresenceReads)
            : FrozenDictionary<(string, string), bool>.Empty;
        return new(names.ToFrozenDictionary(StringComparer.Ordinal), friends,
            data.BlockedAccounts.Select(row => (row.AccountId, row.BlockedAccountId)).ToFrozenSet(), pairs);
    }

    private static FrozenDictionary<(string FirstId, string SecondId), bool> PrepareCommittedChatPairs(
        ImmutableArray<CommittedPresenceFriend> friends, CommittedPresenceReads? previous)
    {
        if (previous is not null && SameOrderedChatFacts(friends, previous.Friends))
            return previous.ChatRelationships;
        if (friends.IsEmpty) return FrozenDictionary<(string, string), bool>.Empty;

        var first = new Dictionary<(string, string), bool>(friends.Length);
        foreach (var row in friends)
        {
            // Historical malformed/unknown rows remain saveable, but never authorize chat.
            if (string.IsNullOrWhiteSpace(row.RequesterId) || string.IsNullOrWhiteSpace(row.AddresseeId)
                || row.RequesterId == row.AddresseeId) continue;
            first.TryAdd(ChatPair(row.RequesterId, row.AddresseeId), row.Status == "accepted");
        }
        return first.ToFrozenDictionary();
    }

    private static bool SameOrderedChatFacts(ImmutableArray<CommittedPresenceFriend> current,
        ImmutableArray<CommittedPresenceFriend> previous)
    {
        if (current.Length != previous.Length) return false;
        for (var index = 0; index < current.Length; index++)
        {
            var row = current[index];
            var old = previous[index];
            if (row.RequesterId != old.RequesterId || row.AddresseeId != old.AddresseeId
                || row.Status != old.Status) return false;
        }
        return true;
    }

    private static (string FirstId, string SecondId) ChatPair(string firstId, string secondId)
        => string.CompareOrdinal(firstId, secondId) <= 0 ? (firstId, secondId) : (secondId, firstId);

    internal L12CommittedChatRelationshipView ReadCommittedChatRelationship(string senderId, string recipientId)
    {
        // Never take the public reader's staged _data branch, even if this caller owns _gate.
        var committed = Volatile.Read(ref _committedSessionActivity);
        var reads = committed.PresenceReads;
        if (!committed.Available || reads is null) return new(committed.Revision, false, false);
        var accepted = !string.IsNullOrWhiteSpace(senderId) && !string.IsNullOrWhiteSpace(recipientId)
            && senderId != recipientId
            && committed.ActiveAccounts.ContainsKey(senderId) && committed.ActiveAccounts.ContainsKey(recipientId)
            && reads.ChatRelationships.TryGetValue(ChatPair(senderId, recipientId), out var related) && related
            && !reads.Blocked.Contains((senderId, recipientId))
            && !reads.Blocked.Contains((recipientId, senderId));
        return new(committed.Revision, true, accepted);
    }

    internal L12CommittedPresencePlatformView ReadCommittedPresencePlatformView(string viewerId)
    {
        CommittedPresenceReads reads;
        if (Monitor.IsEntered(_gate)) reads = PrepareCommittedPresenceReads(_data, prepareChatIndex: false);
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
