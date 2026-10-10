using System.Collections.ObjectModel;

namespace TwelveLegions.Server;

internal sealed record L12DeploymentRoomReadiness(bool Verified,
    IReadOnlyDictionary<string, long> Blockers)
{
    internal bool Clear => Verified && Blockers.Values.All(value => value == 0);
    internal bool GrantsStopPermit => false;
}

internal sealed class L12DeploymentBarrierClosedException : InvalidOperationException
{
    internal L12DeploymentBarrierClosedException() : base("服务器正在更新，请稍后再试") { }
}

public sealed partial class L12RoomManager
{
    private L12DeploymentDrainCoordinator? _deploymentDrain;
    internal L12DeploymentDrainCoordinator? DeploymentDrain => Volatile.Read(ref _deploymentDrain);
    internal bool UsesDeploymentPlatform(L12PlatformStore platform) => ReferenceEquals(_platform, platform);

    // Startup-only attachment. Never replace a live coordinator or discard its old leases.
    internal void AttachDeploymentDrain(L12DeploymentDrainCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        if (ReferenceEquals(DeploymentDrain, coordinator)) return;
        if (!_sessions.IsEmpty || !_rooms.IsEmpty || !_friendInvitations.IsEmpty)
            throw new InvalidOperationException("Deployment coordinator must be attached before admission");
        lock (_matchmakingGate)
        {
            if (_matchmaking.Count != 0 || DeploymentDrain is not null)
                throw new InvalidOperationException("Deployment coordinator is already active");
            _platform?.AttachDeploymentDrain(coordinator);
            Volatile.Write(ref _deploymentDrain, coordinator);
        }
    }

    private sealed class DeploymentCommandGuard : IDisposable
    {
        private readonly L12DeploymentDrainLease _lease;
        private readonly IDisposable? _scope;
        private int _disposed;
        internal bool HasAdmission => _lease.Kind == L12DeploymentLeaseKind.Admission;
        internal DeploymentCommandGuard(L12DeploymentDrainLease lease)
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

    private bool TryDeploymentGuard(bool admission, out DeploymentCommandGuard? guard)
    {
        guard = null;
        var coordinator = DeploymentDrain;
        if (coordinator is null) return true;
        var acquired = admission ? coordinator.TryAcquireAdmission(out var lease)
            : coordinator.TryAcquireActivity(out lease);
        if (!acquired) return false;
        guard = new DeploymentCommandGuard(lease!);
        return true;
    }

    private bool TryDeploymentMixedGuard(out DeploymentCommandGuard? guard)
    {
        guard = null;
        var coordinator = DeploymentDrain;
        if (coordinator is null) return true;
        if (!coordinator.TryAcquireAdmission(out var lease)
            && !coordinator.TryAcquireActivity(out lease)) return false;
        guard = new DeploymentCommandGuard(lease!);
        return true;
    }

    private static IReadOnlyList<OutgoingMessage> DeploymentEntryRejected(Guid sessionId, string? requestId = null)
        => [new OutgoingMessage(sessionId, new
        {
            type = "error", code = "deploymentDrainActive", message = "服务器正在更新，请稍后再试", requestId,
        })];

    internal IReadOnlyList<OutgoingMessage> DisconnectTransportAfterDeploymentSeal(Guid sessionId)
    {
        var coordinator = DeploymentDrain;
        if (coordinator is null) return Disconnect(sessionId);
        if (!coordinator.TryRunSealedTransportCleanup(() =>
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;
            if (session.RoomCode is { } code && _rooms.TryGetValue(code, out var room)
                && (room.Game is null || room.Game.State.Phase != L12Phase.GameOver || !CanRetireSettlement(room)))
                throw new InvalidOperationException("Unexpected live room during sealed transport cleanup");
            // Do not create a new disconnectedAt/grace baseline or checkpoint.
            session.Connected = false;
        })) return Disconnect(sessionId);
        return [];
    }

    // Fixed aggregate fields only. No names, room identifiers, card state or database writes.
    // This is advisory by itself; only the final stable-epoch controller may combine it with
    // committed durability/platform facts to issue a stop permit.
    internal L12DeploymentRoomReadiness CaptureDeploymentRoomReadiness()
    {
        var blockers = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["room_lock_busy"] = 0, ["pregame_rooms"] = 0, ["unfinished_rooms"] = 0,
            ["unrecorded_completion"] = 0, ["unreported_ranked"] = 0,
            ["unreported_tournament"] = 0, ["pending_response_sync"] = 0,
            ["queued_matchmaking"] = 0, ["pending_invitations"] = 0,
            ["unknown_rooms"] = 0, ["invalid_memberships"] = 0,
        };
        if (Monitor.TryEnter(_matchmakingGate))
        {
            try { blockers["queued_matchmaking"] = _matchmaking.Count; }
            finally { Monitor.Exit(_matchmakingGate); }
        }
        else blockers["room_lock_busy"]++;
        if (Monitor.TryEnter(_friendInvitationGate))
        {
            try { blockers["pending_invitations"] = _friendInvitations.Count; }
            finally { Monitor.Exit(_friendInvitationGate); }
        }
        else blockers["room_lock_busy"]++;
        foreach (var room in _rooms.Values.ToArray())
        {
            if (!room.Gate.Wait(0)) { blockers["room_lock_busy"]++; continue; }
            try
            {
                if (room.Closed) blockers["unknown_rooms"]++;
                if (room.Game is null) { blockers["pregame_rooms"]++; continue; }
                if (room.Game.State.Phase != L12Phase.GameOver) blockers["unfinished_rooms"]++;
                if (!room.CompletionRecorded) blockers["unrecorded_completion"]++;
                if (room.Options.MatchModeId == "ranked" && !room.RankedResultReported)
                    blockers["unreported_ranked"]++;
                if (room.TournamentId is not null && !room.TournamentResultReported)
                    blockers["unreported_tournament"]++;
                if (room.ResponsePreferenceSyncPending.Any(value => value)) blockers["pending_response_sync"]++;
                if (room.Sessions.Count != 2 || room.Sessions.Distinct().Count() != 2)
                    blockers["invalid_memberships"]++;
                for (var index = 0; index < room.Sessions.Count; index++)
                    if (!_sessions.TryGetValue(room.Sessions[index], out var member)
                        || member.RoomCode != room.Code || member.PlayerIndex != index || member.IsSpectator)
                        blockers["invalid_memberships"]++;
                if (room.Options.MatchModeId is not ("friendly" or "casual" or "ranked" or "tournament" or "sandbox"))
                    blockers["unknown_rooms"]++;
            }
            finally { room.Gate.Release(); }
        }
        foreach (var member in _sessions.Values)
            if (member.RoomCode is { } code && !_rooms.ContainsKey(code)) blockers["invalid_memberships"]++;
        return new(DeploymentDrain is not null,
            new ReadOnlyDictionary<string, long>(blockers));
    }
}
