using System.Collections.Frozen;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // One in-memory projection of the existing committed authority; never a token/role cache.
    private sealed record CommittedSessionActivity(long Revision, byte[] RollbackSnapshot,
        FrozenDictionary<string, DateTimeOffset> Sessions, bool Available,
        L12DeploymentPlatformReadiness DeploymentReadiness,
        CommittedRankedIdentity? RankedIdentity,
        CommittedGameplayReads? GameplayReads)
    {
        public FrozenDictionary<string, CommittedAuthentication> Authentication { get; init; }
            = FrozenDictionary<string, CommittedAuthentication>.Empty;
        public FrozenDictionary<string, L12AccountView> ActiveAccounts { get; init; }
            = FrozenDictionary<string, L12AccountView>.Empty;
        public CommittedTournamentReads? CommittedTournaments { get; init; }
        public CommittedPresenceReads? PresenceReads { get; init; }
        public L12CommittedStatisticsExclusions? StatisticsExclusions { get; init; }
    }

    private sealed record CommittedAuthentication(L12AuthenticatedSession Session, DateTimeOffset ExpiresAt);

    private static readonly CommittedSessionActivity UnavailableSessionActivity = new(-1, [],
        FrozenDictionary<string, DateTimeOffset>.Empty, false,
        L12DeploymentPlatformReadiness.Unknown("committed_generation_unavailable"), null, null);
    private CommittedSessionActivity _committedSessionActivity = UnavailableSessionActivity;
    private readonly List<string> _pendingSessionRevocations = [];

    private CommittedSessionActivity PrepareCommittedSessionActivity(DataFile data, byte[] rollbackSnapshot)
    {
        StorageFailureInjector?.Invoke("before-session-index-build");
        var now = DateTimeOffset.UtcNow;
        var accounts = data.Accounts.Where(row => !row.Disabled && !row.Deleted)
            .Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        // Preserve the authority's original FirstOrDefault account/token ordering.
        // Views contain only immutable public account metadata, never credentials.
        var firstAccounts = new Dictionary<string, AccountRow>(StringComparer.Ordinal);
        foreach (var account in data.Accounts) firstAccounts.TryAdd(account.Id, account);
        var authentication = new Dictionary<string, CommittedAuthentication>(StringComparer.Ordinal);
        var accountViews = new Dictionary<string, L12AccountView>(StringComparer.Ordinal);
        foreach (var account in firstAccounts.Values)
            if (!account.Disabled && !account.Deleted) accountViews.Add(account.Id, ToView(account));
        var tokenAuthorities = new HashSet<string>(StringComparer.Ordinal);
        var sessions = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var row in data.Sessions)
        {
            if (row.RevokedAt is not null || row.ExpiresAt <= now) continue;
            // A corrupt earlier authority must never fall through to a later account.
            if (string.IsNullOrWhiteSpace(row.TokenHash) || !tokenAuthorities.Add(row.TokenHash))
                throw new InvalidDataException("已提交会话凭据标识为空或重复");
            if (!accounts.Contains(row.AccountId)) continue;
            if (string.IsNullOrWhiteSpace(row.Id) || !sessions.TryAdd(row.Id, row.ExpiresAt))
                throw new InvalidDataException("已提交会话标识为空或重复");
            if (!firstAccounts.TryGetValue(row.AccountId, out var account)
                || account.Disabled || account.Deleted) continue;
            authentication.Add(row.TokenHash, new(new(accountViews[account.Id], row.Id), row.ExpiresAt));
        }
        var prepared = new CommittedSessionActivity(data.Version, rollbackSnapshot,
            sessions.ToFrozenDictionary(StringComparer.Ordinal), true,
            PrepareCommittedDeploymentReadiness(data), PrepareCommittedRankedIdentity(data),
            PrepareCommittedGameplayReads(data))
        {
            Authentication = authentication.ToFrozenDictionary(StringComparer.Ordinal),
            ActiveAccounts = accountViews.ToFrozenDictionary(StringComparer.Ordinal),
            CommittedTournaments = PrepareCommittedTournamentReads(data),
            PresenceReads = PrepareCommittedPresenceReads(data),
            StatisticsExclusions = PrepareCommittedStatisticsExclusions(data),
        };
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

    private L12AuthenticatedSession? ReadCommittedAuthentication(string tokenHash)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available)
            throw new L12PlatformStorageUnavailableException("平台已提交状态不可恢复");
        return committed.Authentication.TryGetValue(tokenHash, out var authentication)
            && authentication.ExpiresAt > DateTimeOffset.UtcNow ? authentication.Session : null;
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
