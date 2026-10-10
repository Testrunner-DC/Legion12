namespace TwelveLegions.Server;

internal sealed record L12DeploymentDrainControlResult(string Code,
    L12DeploymentDrainSnapshot Snapshot, L12DeploymentSealPermit? Permit = null,
    L12DeploymentRoomReadiness? Rooms = null, L12DeploymentDurabilityReadiness? Durability = null,
    L12DeploymentPlatformReadiness? Platform = null)
{
    internal bool Granted => Permit is not null;
}

// Combines existing authorities only after all producers are fenced. Fixed aggregate
// results are not a second match-state authority, and status alone never permits stop.
internal sealed class L12DeploymentDrainController
{
    private readonly L12RoomManager _rooms;
    private readonly MatchRecorder _recorder;
    private readonly L12PlatformStore _platform;
    private readonly L12DeploymentDrainCoordinator _coordinator;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly Action<string>? _testObserver;

    internal L12DeploymentDrainController(L12RoomManager rooms, MatchRecorder recorder,
        L12PlatformStore platform, L12DeploymentDrainCoordinator coordinator,
        Action<string>? testObserver = null)
    {
        if (!ReferenceEquals(rooms.DeploymentDrain, coordinator) || !rooms.UsesDeploymentPlatform(platform))
            throw new InvalidOperationException("Deployment controller requires the room authority");
        _rooms = rooms;
        _recorder = recorder;
        _platform = platform;
        _coordinator = coordinator;
        _testObserver = testObserver;
    }

    internal L12DeploymentDrainControlResult Status() => new("status", _coordinator.Snapshot());

    internal L12DeploymentDrainControlResult Begin(L12DeploymentDrainOwner owner)
    {
        var result = _coordinator.BeginDrain(owner);
        return new(result.Code.ToString(), result.Snapshot);
    }

    internal L12DeploymentDrainControlResult Cancel(L12DeploymentDrainOwner owner)
    {
        var result = _coordinator.Cancel(owner);
        return new(result.Code.ToString(), result.Snapshot);
    }

    internal L12DeploymentDrainControlResult ConsumeStopPermit(L12DeploymentSealPermit permit)
    {
        var result = _coordinator.ConsumeStopPermit(permit);
        var observation = _coordinator.CaptureCurrentSealPermit(result.Permit);
        return new(result.Code.ToString(), observation.Snapshot, observation.Permit);
    }

    internal async Task<L12DeploymentDrainControlResult> SealAsync(L12DeploymentDrainOwner owner,
        CancellationToken cancellationToken = default)
    {
        if (!_probeGate.Wait(0)) return new("probe_busy", _coordinator.Snapshot());
        long? sealingEpoch = null;
        var completed = false;
        try
        {
            var state = _coordinator.Snapshot();
            if (state.Owner != owner || state.ProcessInstance != owner.ProcessInstance)
                return new("OwnerMismatch", state);
            if (state.Phase == L12DeploymentDrainPhase.Sealed)
            {
                // Same-operation retry may retrieve the existing permit, not generate a new one.
                var repeat = _coordinator.CompleteSeal(owner, state.Epoch - 1, L12DeploymentExternalReadiness.Clear);
                var repeatObservation = _coordinator.CaptureCurrentSealPermit(repeat.Permit);
                return new(repeat.Code.ToString(), repeatObservation.Snapshot, repeatObservation.Permit);
            }
            if (state.Phase != L12DeploymentDrainPhase.Draining || state.FenceUnknown || !state.FenceSynchronized)
                return new("drain_not_ready", state);
            if (state.AdmissionLeases != 0 || state.ActivityLeases != 0)
                return new("OutstandingLeases", state);

            // Do not freeze existing game actions for a multi-second SQL read while
            // known active/pregame/pending work still needs to finish.
            var initialRooms = _rooms.CaptureDeploymentRoomReadiness();
            var initialPlatform = _platform.CaptureDeploymentPlatformReadiness();
            if (!initialRooms.Clear || !initialPlatform.Clear)
                return new("work_remaining", _coordinator.Snapshot(), Rooms: initialRooms, Platform: initialPlatform);
            var initialDurability = await _recorder.DeploymentReadinessAsync(cancellationToken: cancellationToken);
            if (!initialDurability.Clear)
                return new("durability_not_clear", _coordinator.Snapshot(), Rooms: initialRooms,
                    Durability: initialDurability, Platform: initialPlatform);
            _testObserver?.Invoke("after-initial-readiness");

            var sealing = _coordinator.TryBeginSeal(owner);
            if (!sealing.Succeeded) return new(sealing.Code.ToString(), sealing.Snapshot);
            sealingEpoch = sealing.Snapshot.Epoch;
            _testObserver?.Invoke("after-begin-seal");

            // Sealing excludes all new producers. Each store is read through its own
            // committed contract; do not pretend two SQLite databases share a transaction.
            var roomsBefore = _rooms.CaptureDeploymentRoomReadiness();
            var platformBefore = _platform.CaptureDeploymentPlatformReadiness();
            var durability = await _recorder.DeploymentReadinessAsync(cancellationToken: cancellationToken);
            var roomsAfter = _rooms.CaptureDeploymentRoomReadiness();
            var platformAfter = _platform.CaptureDeploymentPlatformReadiness();
            var final = _coordinator.Snapshot();
            var known = roomsBefore.Verified && roomsAfter.Verified && durability.Verified
                && platformBefore.Verified && platformAfter.Verified
                && platformBefore.Revision == platformAfter.Revision
                && final.Phase == L12DeploymentDrainPhase.Sealing
                && final.Epoch == sealingEpoch && final.Owner == owner;
            var clear = known && roomsBefore.Clear && roomsAfter.Clear && durability.Clear
                && platformBefore.Clear && platformAfter.Clear;
            _testObserver?.Invoke("before-complete-seal");
            var readiness = !known ? L12DeploymentExternalReadiness.Unknown
                : clear ? L12DeploymentExternalReadiness.Clear : L12DeploymentExternalReadiness.Blocked;
            var result = _coordinator.CompleteSeal(owner, sealingEpoch.Value, readiness);
            completed = true;
            // Never return a permit invalidated concurrently by transport cleanup/reopen.
            var observation = _coordinator.CaptureCurrentSealPermit(result.Permit);
            return new(result.Code.ToString(), observation.Snapshot, observation.Permit,
                roomsAfter, durability, platformAfter);
        }
        catch (OperationCanceledException)
        {
            if (sealingEpoch is { } epoch)
                _coordinator.CompleteSeal(owner, epoch, L12DeploymentExternalReadiness.Unknown);
            completed = true;
            return new("probe_cancelled", _coordinator.Snapshot());
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (sealingEpoch is { } epoch)
                _coordinator.CompleteSeal(owner, epoch, L12DeploymentExternalReadiness.Unknown);
            completed = true;
            // No SQL, paths, room identifiers or exception bodies in control responses.
            return new("probe_unknown", _coordinator.Snapshot());
        }
        finally
        {
            if (sealingEpoch is { } epoch && !completed)
                _coordinator.CompleteSeal(owner, epoch, L12DeploymentExternalReadiness.Unknown);
            _probeGate.Release();
        }
    }
}
