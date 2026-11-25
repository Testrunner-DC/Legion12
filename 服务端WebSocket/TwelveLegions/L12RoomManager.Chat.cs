using System.Collections.Concurrent;

namespace TwelveLegions.Server;

/// <summary>本桌频道的第三方成员（当前只装裁判）：账号 + 会话 + 连接代次，逐帧复核。</summary>
internal sealed record L12CommittedChatMember(string AccountId, Guid SessionId, long ConnectionGeneration);

// 本桌频道：两名选手固定，裁判成员可有可无（用户 2026-11-25 裁定受众＝选手+裁判，裁判可发言，
// 且**裁判之间互相可见**；普通观战者一律不在内）。裁判身份是逐请求算出来的，所以成员表在
// 作用域刷新时重算，判定失败即不放入。
internal sealed record L12CommittedOpponentChatScope(
    string MatchId,
    long ScopeGeneration,
    string FirstAccountId,
    Guid FirstSessionId,
    long FirstConnectionGeneration,
    string SecondAccountId,
    Guid SecondSessionId,
    long SecondConnectionGeneration,
    IReadOnlyList<L12CommittedChatMember>? Referees = null)
{
    internal IReadOnlyList<L12CommittedChatMember> Members
        => Referees is { Count: > 0 }
            ? [new L12CommittedChatMember(FirstAccountId, FirstSessionId, FirstConnectionGeneration),
               new L12CommittedChatMember(SecondAccountId, SecondSessionId, SecondConnectionGeneration),
               .. Referees]
            : [new L12CommittedChatMember(FirstAccountId, FirstSessionId, FirstConnectionGeneration),
               new L12CommittedChatMember(SecondAccountId, SecondSessionId, SecondConnectionGeneration)];

    internal bool ContainsMember(string accountId)
        => Members.Any(member => string.Equals(member.AccountId, accountId, StringComparison.Ordinal));

    /// <summary>本桌成员两两可见：选手↔选手、选手↔裁判、裁判↔裁判。</summary>
    internal bool ContainsPair(string firstId, string secondId)
        => !string.Equals(firstId, secondId, StringComparison.Ordinal)
           && ContainsMember(firstId) && ContainsMember(secondId);

    internal bool ContainsConnection(string accountId, Guid sessionId, long connectionGeneration)
        => Members.Any(member => string.Equals(member.AccountId, accountId, StringComparison.Ordinal)
            && member.SessionId == sessionId && member.ConnectionGeneration == connectionGeneration);
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

    // 本桌广播：发送者只需是本桌成员，不需要指定单一收件人（peer 用通配）。
    internal L12CommittedOpponentChatScope? ReadCommittedTableChatScope(string accountId, string? matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(accountId)
            || !_opponentChatScopes.TryGetValue(matchId, out var slot)
            || Volatile.Read(ref slot.Current) is not { } scope
            || !scope.ContainsMember(accountId)) return null;
        return scope;
    }

    internal L12ChatQueueResult StartCommittedOpponentChatScope(L12ChatDeliveryMetadata metadata,
        Func<L12ChatQueueResult> kickoff)
    {
        if (metadata.Scope is not (L12ChatDeliveryScope.Opponent or L12ChatDeliveryScope.Table)
            || metadata.MatchId is null
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
        // 裁判成员：带裁判权且不是本桌参赛者的观战会话（用户裁定裁判可发言且裁判之间互相可见）。
        // 判定失败一律不放入——宁可在本桌频道里少一个人，也不能把围观者放进来。
        var referees = new List<L12CommittedChatMember>();
        if (room.TournamentId is not null && room.TournamentMatchId is not null && _platform is not null)
        {
            List<Guid> spectators;
            lock (room.Spectators) spectators = [.. room.Spectators];
            foreach (var spectatorSession in spectators)
            {
                if (!_sessions.TryGetValue(spectatorSession, out var observer) || !observer.Connected
                    || !observer.IsSpectator || string.IsNullOrWhiteSpace(observer.AccountId)
                    || observer.RoomCode != room.Code
                    || observer.AccountId == first.AccountId || observer.AccountId == second.AccountId)
                    continue;
                var canReferee = false;
                try
                {
                    var access = _platform.TournamentRoomAccess(observer.AccountId, room.TournamentId,
                        room.TournamentMatchId, spectate: true);
                    canReferee = access is not null && access.CanRefereeView && !access.CanPlay
                        && access.RoomCode == room.Code;
                }
                catch { canReferee = false; }
                if (canReferee)
                    referees.Add(new L12CommittedChatMember(observer.AccountId, observer.Id,
                        observer.ConnectionGeneration));
            }
        }
        var published = new L12CommittedOpponentChatScope(matchId, generation,
            first.AccountId, first.Id, first.ConnectionGeneration,
            second.AccountId, second.Id, second.ConnectionGeneration, referees);
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
