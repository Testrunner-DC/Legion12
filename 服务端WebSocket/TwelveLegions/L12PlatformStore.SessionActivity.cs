using System.Collections.Frozen;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // One in-memory projection of the existing committed authority; never a token/role cache.
    private sealed record CommittedSessionActivity(long Revision, byte[] RollbackSnapshot,
        FrozenDictionary<string, DateTimeOffset> Sessions, bool Available);

    private static readonly CommittedSessionActivity UnavailableSessionActivity = new(-1, [],
        FrozenDictionary<string, DateTimeOffset>.Empty, false);
    private CommittedSessionActivity _committedSessionActivity = UnavailableSessionActivity;
    private readonly List<string> _pendingSessionRevocations = [];

    private CommittedSessionActivity PrepareCommittedSessionActivity(DataFile data, byte[] rollbackSnapshot)
    {
        StorageFailureInjector?.Invoke("before-session-index-build");
        var now = DateTimeOffset.UtcNow;
        var accounts = data.Accounts.Where(row => !row.Disabled && !row.Deleted)
            .Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var sessions = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var row in data.Sessions)
        {
            if (row.RevokedAt is not null || row.ExpiresAt <= now || !accounts.Contains(row.AccountId)) continue;
            if (string.IsNullOrWhiteSpace(row.Id) || !sessions.TryAdd(row.Id, row.ExpiresAt))
                throw new InvalidDataException("已提交会话标识为空或重复");
        }
        var prepared = new CommittedSessionActivity(data.Version, rollbackSnapshot,
            sessions.ToFrozenDictionary(StringComparer.Ordinal), true);
        StorageFailureInjector?.Invoke("after-session-index-build");
        return prepared;
    }

    // All allocation, validation and injectable code must precede the database commit.
    private void PublishCommittedSessionActivity(CommittedSessionActivity prepared)
        => Volatile.Write(ref _committedSessionActivity, prepared);

    private bool ReadCommittedSessionActivity(string sessionId)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available)
            throw new L12PlatformStorageUnavailableException("平台已提交状态不可恢复");
        if (string.IsNullOrEmpty(sessionId)) return false;
        return committed.Sessions.TryGetValue(sessionId, out var expiresAt)
            && expiresAt > DateTimeOffset.UtcNow;
    }

    private CommittedSessionActivity SessionActivityForRollback(DataFile restored, byte[] snapshot)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available) return UnavailableSessionActivity;
        return ReferenceEquals(committed.RollbackSnapshot, snapshot)
            ? committed : PrepareCommittedSessionActivity(restored, snapshot);
    }

    private bool DeferSessionRevocations(IReadOnlyList<string> sessionIds)
    {
        // Only the thread owning this synchronous transaction can append to its queue.
        // A concurrent post-commit notifier must not join an unrelated transaction.
        if (!Monitor.IsEntered(_gate) || _adminTransactionDepth == 0) return false;
        _pendingSessionRevocations.AddRange(sessionIds);
        return true;
    }

    private IReadOnlyList<string> PreparePendingSessionRevocations()
    {
        // A nested authority refresh can replace an earlier pending revocation.
        // Prepare against the final synchronous outer write input, not merely its queue.
        // PersistData subsequently changes revisions/audit/decks, not account/session facts.
        var ordered = _pendingSessionRevocations.Distinct(StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0) return Array.Empty<string>();
        var revoked = ordered.ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _data.Sessions)
        {
            if (!revoked.Contains(session.Id) || session.RevokedAt is not null || session.ExpiresAt <= now) continue;
            if (_data.Accounts.Any(account => account.Id == session.AccountId && !account.Disabled && !account.Deleted))
                revoked.Remove(session.Id);
        }
        return Array.AsReadOnly(ordered.Where(revoked.Contains).ToArray());
    }

    private void TrimPendingSessionRevocations(int count)
    {
        if (_pendingSessionRevocations.Count > count)
            _pendingSessionRevocations.RemoveRange(count, _pendingSessionRevocations.Count - count);
    }

    private void DispatchCommittedSessionRevocations(IReadOnlyList<string> sessionIds)
    {
        if (sessionIds.Count == 0) return;
        try
        {
            var handlers = SessionsRevoked;
            if (handlers is null) return;
            foreach (Action<IReadOnlyList<string>> handler in handlers.GetInvocationList())
            {
                try { handler(sessionIds); }
                catch { } // Notification failures never reverse an already durable commit.
            }
        }
        catch { }
    }
}
