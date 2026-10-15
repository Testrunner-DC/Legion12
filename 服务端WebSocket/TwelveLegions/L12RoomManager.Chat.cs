using System.Collections.Concurrent;

namespace TwelveLegions.Server;

internal sealed record L12CommittedOpponentChatScope(
    string MatchId,
    long ScopeGeneration,
    string FirstAccountId,
    Guid FirstSessionId,
    long FirstConnectionGeneration,
    string SecondAccountId,
    Guid SecondSessionId,
    long SecondConnectionGeneration)
{
    internal bool ContainsPair(string firstId, string secondId)
        => string.Equals(FirstAccountId, firstId, StringComparison.Ordinal)
               && string.Equals(SecondAccountId, secondId, StringComparison.Ordinal)
           || string.Equals(FirstAccountId, secondId, StringComparison.Ordinal)
               && string.Equals(SecondAccountId, firstId, StringComparison.Ordinal);

    internal bool ContainsConnection(string accountId, Guid sessionId, long connectionGeneration)
        => string.Equals(FirstAccountId, accountId, StringComparison.Ordinal)
               && FirstSessionId == sessionId && FirstConnectionGeneration == connectionGeneration
           || string.Equals(SecondAccountId, accountId, StringComparison.Ordinal)
               && SecondSessionId == sessionId && SecondConnectionGeneration == connectionGeneration;
}

public sealed partial class L12RoomManager
{
    private sealed class OpponentChatScopeSlot
    {
        internal readonly object StartGate = new();
        internal L12CommittedOpponentChatScope? Current;
    }

    private readonly ConcurrentDictionary<string, OpponentChatScopeSlot> _opponentChatScopes =
        new(StringComparer.Ordinal);
    private long _opponentChatScopeGeneration;

    internal event Action<string, long, bool>? OpponentChatScopeChanged;

    internal L12CommittedOpponentChatScope? ReadCommittedOpponentChatScope(string senderId,
        string recipientId, string? matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(senderId)
            || string.IsNullOrWhiteSpace(recipientId) || senderId == recipientId
            || !_opponentChatScopes.TryGetValue(matchId, out var slot)
            || Volatile.Read(ref slot.Current) is not { } scope
            || !scope.ContainsPair(senderId, recipientId)) return null;
        return scope;
    }

    internal L12ChatQueueResult StartCommittedOpponentChatScope(L12ChatDeliveryMetadata metadata,
        Func<L12ChatQueueResult> kickoff)
    {
        if (metadata.Scope != L12ChatDeliveryScope.Opponent || metadata.MatchId is null
            || !_opponentChatScopes.TryGetValue(metadata.MatchId, out var slot))
            return L12ChatQueueResult.Suppressed;
        lock (slot.StartGate)
        {
            var scope = slot.Current;
            if (scope is null || scope.ScopeGeneration != metadata.ScopeGeneration
                || !scope.ContainsPair(metadata.SenderAccountId, metadata.RecipientAccountId)
                || !scope.ContainsConnection(metadata.Origin.AccountId, metadata.Origin.SessionId,
                    metadata.Origin.ConnectionGeneration)
                || !scope.ContainsConnection(metadata.Transport.AccountId, metadata.Transport.SessionId,
                    metadata.Transport.ConnectionGeneration))
                return L12ChatQueueResult.Suppressed;
            return kickoff();
        }
    }

    private void RefreshOpponentChatScopeLocked(Room room)
    {
        if (room.Game is null || room.Closed || room.Game.State.Phase == L12Phase.GameOver
            || room.Sessions.Count != 2
            || !_sessions.TryGetValue(room.Sessions[0], out var first)
            || !_sessions.TryGetValue(room.Sessions[1], out var second)
            || first.IsVirtual || second.IsVirtual || first.IsSpectator || second.IsSpectator
            || first.PlayerIndex != 0 || second.PlayerIndex != 1
            || string.IsNullOrWhiteSpace(first.AccountId) || string.IsNullOrWhiteSpace(second.AccountId)
            || first.AccountId == second.AccountId)
        {
            WithdrawOpponentChatScopeLocked(room);
            return;
        }

        var matchId = room.Game.State.MatchId;
        var slot = _opponentChatScopes.GetOrAdd(matchId, static _ => new());
        long generation;
        lock (slot.StartGate)
        {
            var previous = slot.Current;
            generation = previous is not null && previous.ContainsPair(first.AccountId, second.AccountId)
                ? previous.ScopeGeneration
                : Interlocked.Increment(ref _opponentChatScopeGeneration);
        }
        if (generation <= 0 || generation > L12PlatformStore.ChatMaximumSafeSequence)
        {
            WithdrawOpponentChatScopeLocked(room);
            return;
        }
        var published = new L12CommittedOpponentChatScope(matchId, generation,
            first.AccountId, first.Id, first.ConnectionGeneration,
            second.AccountId, second.Id, second.ConnectionGeneration);
        lock (slot.StartGate) Volatile.Write(ref slot.Current, published);
        NotifyOpponentChatScopeChanged(matchId, generation, available: true);
    }

    private void WithdrawOpponentChatScopeLocked(Room room)
    {
        var matchId = room.Game?.State.MatchId;
        if (string.IsNullOrWhiteSpace(matchId)
            || !_opponentChatScopes.TryGetValue(matchId, out var slot)) return;
        L12CommittedOpponentChatScope? current;
        lock (slot.StartGate)
        {
            current = slot.Current;
            if (current is null) return;
            Volatile.Write(ref slot.Current, null);
        }
        _opponentChatScopes.TryRemove(new KeyValuePair<string, OpponentChatScopeSlot>(matchId, slot));
        NotifyOpponentChatScopeChanged(matchId, current.ScopeGeneration, available: false);
    }

    private void NotifyOpponentChatScopeChanged(string matchId, long generation, bool available)
    {
        try
        {
            var handlers = OpponentChatScopeChanged;
            if (handlers is null) return;
            foreach (Action<string, long, bool> handler in handlers.GetInvocationList())
            {
                try { handler(matchId, generation, available); }
                catch { }
            }
        }
        catch { }
    }
}
