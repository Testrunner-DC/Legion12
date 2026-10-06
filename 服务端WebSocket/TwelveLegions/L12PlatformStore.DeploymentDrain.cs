namespace TwelveLegions.Server;

internal sealed record L12DeploymentPlatformReadiness(bool Verified, long Revision,
    long PendingRoomCommands, string? FailureCode)
{
    internal bool Clear => Verified && PendingRoomCommands == 0;
    internal bool GrantsStopPermit => false;
    internal static L12DeploymentPlatformReadiness Unknown(string code) => new(false, -1, -1, code);
}

public sealed partial class L12PlatformStore
{
    private L12DeploymentDrainCoordinator? _deploymentDrain;

    // Startup-only shared barrier. Platform and room manager must never use two authorities.
    internal void AttachDeploymentDrain(L12DeploymentDrainCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        var previous = Interlocked.CompareExchange(ref _deploymentDrain, coordinator, null);
        if (previous is not null && !ReferenceEquals(previous, coordinator))
            throw new InvalidOperationException("Deployment coordinator cannot be replaced");
    }

    private sealed class DeploymentMutationGuard : IDisposable
    {
        private readonly L12DeploymentDrainLease _lease;
        private readonly IDisposable? _scope;
        private int _disposed;
        internal DeploymentMutationGuard(L12DeploymentDrainLease lease)
        {
            _lease = lease;
            try
            {
                if (lease.Kind == L12DeploymentLeaseKind.Admission && lease.IsOriginal)
                    _scope = lease.EnterExecutionScope();
            }
            catch { lease.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _scope?.Dispose(); }
            finally { _lease.Dispose(); }
        }
    }

    private IDisposable? EnterDeploymentMutation(bool admission = false)
    {
        var coordinator = Volatile.Read(ref _deploymentDrain);
        if (coordinator is null) return null;
        var acquired = admission ? coordinator.TryAcquireAdmission(out var lease)
            : coordinator.TryAcquireActivity(out lease);
        if (!acquired) throw new L12DeploymentBarrierClosedException();
        return new DeploymentMutationGuard(lease!);
    }

    private static L12DeploymentPlatformReadiness PrepareCommittedDeploymentReadiness(DataFile data)
    {
        long count = 0;
        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tournament in data.Tournaments)
        foreach (var command in tournament.RoomCommands.Where(item => item.CompletedAt is null))
        {
            if (string.IsNullOrWhiteSpace(command.Id) || !commandIds.Add(command.Id)
                || command.Kind is not ("pause" or "cancel")
                || tournament.Rounds.SelectMany(round => round.Matches)
                    .Count(match => match.Id == command.MatchId) != 1)
                return L12DeploymentPlatformReadiness.Unknown("invalid_room_command");
            count++;
        }
        return new(true, data.Version, count, null);
    }

    // Non-waiting, fixed aggregate of the exact committed generation. Uncommitted outer
    // transactions, failed authority refresh and a held store lock never mean zero pending.
    internal L12DeploymentPlatformReadiness CaptureDeploymentPlatformReadiness()
    {
        if (Volatile.Read(ref _deploymentDrain) is null)
            return L12DeploymentPlatformReadiness.Unknown("barrier_not_configured");
        if (!Monitor.TryEnter(_gate))
            return L12DeploymentPlatformReadiness.Unknown("platform_busy");
        try
        {
            var committed = Volatile.Read(ref _committedSessionActivity);
            if (_adminTransactionDepth != 0 || !committed.Available
                || committed.Revision != _data.Version
                || !ReferenceEquals(committed.RollbackSnapshot, _lastCommittedSnapshot))
                return L12DeploymentPlatformReadiness.Unknown("committed_generation_unavailable");
            return committed.DeploymentReadiness;
        }
        finally { Monitor.Exit(_gate); }
    }
}
