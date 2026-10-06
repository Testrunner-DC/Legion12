namespace TwelveLegions.Server;

internal enum L12DeploymentDrainPhase
{
    Open,
    Draining,
    Sealing,
    Sealed,
}

internal enum L12DeploymentLeaseKind
{
    Admission,
    Activity,
}

internal enum L12DeploymentExternalReadiness
{
    Clear,
    Blocked,
    Unknown,
}

internal enum L12DeploymentDrainTransitionCode
{
    Applied,
    Idempotent,
    TransitionInProgress,
    WrongPhase,
    OwnerMismatch,
    OutstandingLeases,
    StaleEpoch,
    ExternalReadinessBlocked,
    ExternalReadinessUnknown,
    FenceFailure,
    UnknownFence,
}

internal enum L12DeploymentDrainFenceLoadKind
{
    Missing,
    Found,
    Unknown,
}

internal sealed record L12DeploymentDrainOwner
{
    private L12DeploymentDrainOwner(string operationId, string targetCommit, string processInstance)
    {
        OperationId = operationId;
        TargetCommit = targetCommit;
        ProcessInstance = processInstance;
    }

    internal string OperationId { get; }
    internal string TargetCommit { get; }
    internal string ProcessInstance { get; }

    internal static bool TryCreate(string operationId, string targetCommit, string processInstance,
        out L12DeploymentDrainOwner? owner)
    {
        owner = null;
        if (!L12DeploymentDrainCoordinator.IsCanonicalGuid(operationId)
            || !L12DeploymentDrainCoordinator.IsCommit(targetCommit)
            || !L12DeploymentDrainCoordinator.IsCanonicalGuid(processInstance))
            return false;

        owner = new L12DeploymentDrainOwner(operationId, targetCommit.ToLowerInvariant(), processInstance);
        return true;
    }
}

internal sealed record L12DeploymentPersistedFence(int ProtocolVersion, L12DeploymentDrainPhase Phase,
    long Epoch, string OperationId, string TargetCommit, string ProcessInstance, string ActiveCommit,
    string? SealId);

internal sealed record L12DeploymentDrainFenceLoadResult(L12DeploymentDrainFenceLoadKind Kind,
    L12DeploymentPersistedFence? Fence)
{
    internal static L12DeploymentDrainFenceLoadResult Missing { get; } =
        new(L12DeploymentDrainFenceLoadKind.Missing, null);

    internal static L12DeploymentDrainFenceLoadResult Unknown { get; } =
        new(L12DeploymentDrainFenceLoadKind.Unknown, null);

    internal static L12DeploymentDrainFenceLoadResult Found(L12DeploymentPersistedFence fence) =>
        new(L12DeploymentDrainFenceLoadKind.Found, fence);
}

/// <summary>
/// The implementation must use atomic replacement for writes. A failed write must leave the
/// previous closed fence intact. Clear must compare the operation and target commit before delete.
/// </summary>
internal interface IL12DeploymentDrainFenceStore
{
    L12DeploymentDrainFenceLoadResult Load();
    bool TryWrite(L12DeploymentPersistedFence fence);
    bool TryClear(string operationId, string targetCommit);
}

internal sealed record L12DeploymentDrainSnapshot(L12DeploymentDrainPhase Phase, long Epoch,
    string ProcessInstance, string ActiveCommit, L12DeploymentDrainOwner? Owner,
    long AdmissionLeases, long ActivityLeases, string? SealId, bool FenceSynchronized,
    bool FenceUnknown);

internal sealed record L12DeploymentSealPermit(int ProtocolVersion, string OperationId,
    string TargetCommit, string ProcessInstance, string ActiveCommit, long Epoch, string SealId);

internal sealed record L12DeploymentDrainTransition(L12DeploymentDrainTransitionCode Code,
    L12DeploymentDrainSnapshot Snapshot, L12DeploymentSealPermit? Permit = null)
{
    internal bool Succeeded => Code is L12DeploymentDrainTransitionCode.Applied
        or L12DeploymentDrainTransitionCode.Idempotent;
}

/// <summary>
/// A process-local linearization barrier for deployment drain. The short state lock never covers
/// fence I/O. Management transitions are fail-fast serialized so a late write cannot race a clear.
/// </summary>
internal sealed class L12DeploymentDrainCoordinator
{
    internal const int ProtocolVersion = 1;

    private sealed class AdmissionScopeNode
    {
        private readonly object _gate = new();

        internal AdmissionScopeNode(LeaseRoot root, AdmissionScopeNode? previous)
        {
            Root = root;
            Previous = previous;
        }

        internal LeaseRoot Root { get; }
        internal AdmissionScopeNode? Previous { get; }
        internal int Active = 1;

        internal bool TryBorrow(out L12DeploymentDrainLease? lease)
        {
            lock (_gate)
            {
                if (Active == 0)
                {
                    lease = null;
                    return false;
                }
                return Root.TryBorrow(out lease);
            }
        }

        internal void Deactivate()
        {
            lock (_gate) Active = 0;
        }
    }

    private sealed class AdmissionExecutionScope : IDisposable
    {
        private readonly L12DeploymentDrainCoordinator _coordinator;
        private readonly LeaseRoot _root;
        private readonly AdmissionScopeNode _node;
        private int _disposed;

        internal AdmissionExecutionScope(L12DeploymentDrainCoordinator coordinator, LeaseRoot root,
            AdmissionScopeNode node)
        {
            _coordinator = coordinator;
            _root = root;
            _node = node;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _node.Deactivate();
            _coordinator.NormalizeAdmissionScope();
            _root.ReleaseReference();
        }
    }

    internal sealed class LeaseRoot
    {
        private readonly object _gate = new();
        private readonly L12DeploymentDrainCoordinator _coordinator;
        private readonly object _authority;
        private int _references = 1;
        private bool _originalReleased;
        private bool _barrierReleased;

        internal LeaseRoot(L12DeploymentDrainCoordinator coordinator, L12DeploymentLeaseKind kind,
            object authority)
        {
            _coordinator = coordinator;
            _authority = authority;
            Kind = kind;
        }

        internal L12DeploymentLeaseKind Kind { get; }
        internal L12DeploymentDrainCoordinator Coordinator => _coordinator;

        internal bool OriginalActive
        {
            get { lock (_gate) return !_originalReleased; }
        }

        internal bool TryBorrow(out L12DeploymentDrainLease? lease)
        {
            lease = null;
            lock (_gate)
            {
                if (_originalReleased || Kind != L12DeploymentLeaseKind.Admission) return false;
                checked { _references++; }
            }

            lease = new L12DeploymentDrainLease(this, original: false, _authority);
            return true;
        }

        internal bool TryRetainScope()
        {
            lock (_gate)
            {
                if (_originalReleased || Kind != L12DeploymentLeaseKind.Admission) return false;
                checked { _references++; }
                return true;
            }
        }

        internal void ReleaseOriginal()
        {
            var releaseBarrier = false;
            lock (_gate)
            {
                if (_originalReleased) return;
                _originalReleased = true;
                _references--;
                releaseBarrier = TryMarkBarrierReleasedLocked();
            }
            if (releaseBarrier) _coordinator.ReleaseBarrier(Kind, _authority);
        }

        internal void ReleaseReference()
        {
            var releaseBarrier = false;
            lock (_gate)
            {
                if (_references <= 0)
                    throw new InvalidOperationException("部署 drain lease 引用计数失衡");
                _references--;
                releaseBarrier = TryMarkBarrierReleasedLocked();
            }
            if (releaseBarrier) _coordinator.ReleaseBarrier(Kind, _authority);
        }

        private bool TryMarkBarrierReleasedLocked()
        {
            if (!_originalReleased || _references != 0 || _barrierReleased) return false;
            _barrierReleased = true;
            return true;
        }

        internal bool IsIssuedBy(L12DeploymentDrainCoordinator coordinator, object authority) =>
            ReferenceEquals(_coordinator, coordinator) && ReferenceEquals(_authority, authority);
    }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _managementGate = new(1, 1);
    private readonly AsyncLocal<AdmissionScopeNode?> _admissionScope = new();
    private readonly object _leaseAuthority = new();
    private readonly IL12DeploymentDrainFenceStore _fenceStore;
    private readonly string _processInstance;
    private readonly string _activeCommit;
    private L12DeploymentDrainPhase _phase;
    private long _epoch;
    private L12DeploymentDrainOwner? _owner;
    private long _admissionLeases;
    private long _activityLeases;
    private string? _sealId;
    private bool _fenceSynchronized;
    private bool _fenceUnknown;

    internal L12DeploymentDrainCoordinator(string processInstance, string activeCommit,
        IL12DeploymentDrainFenceStore fenceStore)
    {
        if (!IsCanonicalGuid(processInstance))
            throw new ArgumentException("process instance 必须为 GUID N 格式", nameof(processInstance));
        if (!IsCommit(activeCommit))
            throw new ArgumentException("active commit 必须为 40 位十六进制提交", nameof(activeCommit));

        _processInstance = processInstance;
        _activeCommit = activeCommit.ToLowerInvariant();
        _fenceStore = fenceStore ?? throw new ArgumentNullException(nameof(fenceStore));
        LoadFence();
    }

    internal string ProcessInstance => _processInstance;
    internal string ActiveCommit => _activeCommit;

    internal L12DeploymentDrainSnapshot Snapshot()
    {
        lock (_gate) return SnapshotLocked();
    }

    internal bool TryAcquireAdmission(out L12DeploymentDrainLease? lease)
    {
        lease = null;
        var ambientScope = NormalizeAdmissionScope();
        if (ambientScope is not null && ambientScope.TryBorrow(out lease)) return true;

        lock (_gate)
        {
            if (_phase != L12DeploymentDrainPhase.Open) return false;
            checked { _admissionLeases++; }
            lease = new L12DeploymentDrainLease(
                new LeaseRoot(this, L12DeploymentLeaseKind.Admission, _leaseAuthority),
                original: true, _leaseAuthority);
            return true;
        }
    }

    internal bool TryAcquireActivity(out L12DeploymentDrainLease? lease)
    {
        lease = null;
        lock (_gate)
        {
            if (_phase is not (L12DeploymentDrainPhase.Open or L12DeploymentDrainPhase.Draining))
                return false;
            checked { _activityLeases++; }
            lease = new L12DeploymentDrainLease(
                new LeaseRoot(this, L12DeploymentLeaseKind.Activity, _leaseAuthority),
                original: true, _leaseAuthority);
            return true;
        }
    }

    internal L12DeploymentDrainTransition BeginDrain(L12DeploymentDrainOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_managementGate.Wait(0)) return Transition(L12DeploymentDrainTransitionCode.TransitionInProgress);
        try
        {
            long epoch;
            lock (_gate)
            {
                if (_fenceUnknown) return TransitionLocked(L12DeploymentDrainTransitionCode.UnknownFence);
                if (!OwnerBelongsToProcess(owner))
                    return TransitionLocked(L12DeploymentDrainTransitionCode.OwnerMismatch);

                if (_phase == L12DeploymentDrainPhase.Open)
                {
                    _phase = L12DeploymentDrainPhase.Draining;
                    _epoch = NextEpoch(_epoch);
                    _owner = owner;
                    _sealId = null;
                    _fenceSynchronized = false;
                }
                else
                {
                    if (_owner != owner)
                        return TransitionLocked(L12DeploymentDrainTransitionCode.OwnerMismatch);
                    if (_phase != L12DeploymentDrainPhase.Draining)
                        return TransitionLocked(_fenceSynchronized
                            ? L12DeploymentDrainTransitionCode.Idempotent
                            : L12DeploymentDrainTransitionCode.FenceFailure);
                    if (_fenceSynchronized)
                        return TransitionLocked(L12DeploymentDrainTransitionCode.Idempotent);
                }
                epoch = _epoch;
            }

            var persisted = PersistedFence(L12DeploymentDrainPhase.Draining, epoch, owner, sealId: null);
            var written = TryWriteFence(persisted);
            lock (_gate)
            {
                if (written && _phase == L12DeploymentDrainPhase.Draining && _epoch == epoch
                    && _owner == owner)
                {
                    _fenceSynchronized = true;
                    return TransitionLocked(L12DeploymentDrainTransitionCode.Applied);
                }

                // The admission gate remains closed on any durability uncertainty.
                _fenceSynchronized = false;
                return TransitionLocked(L12DeploymentDrainTransitionCode.FenceFailure);
            }
        }
        finally
        {
            _managementGate.Release();
        }
    }

    internal L12DeploymentDrainTransition TryBeginSeal(L12DeploymentDrainOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_managementGate.Wait(0)) return Transition(L12DeploymentDrainTransitionCode.TransitionInProgress);
        try
        {
            lock (_gate)
            {
                if (_fenceUnknown) return TransitionLocked(L12DeploymentDrainTransitionCode.UnknownFence);
                if (_owner != owner || !OwnerBelongsToProcess(owner))
                    return TransitionLocked(L12DeploymentDrainTransitionCode.OwnerMismatch);
                if (_phase == L12DeploymentDrainPhase.Sealing)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.Idempotent);
                if (_phase != L12DeploymentDrainPhase.Draining)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.WrongPhase);
                if (!_fenceSynchronized)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.FenceFailure);
                if (_admissionLeases != 0 || _activityLeases != 0)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.OutstandingLeases);

                _phase = L12DeploymentDrainPhase.Sealing;
                _epoch = NextEpoch(_epoch);
                _sealId = null;
                return TransitionLocked(L12DeploymentDrainTransitionCode.Applied);
            }
        }
        finally
        {
            _managementGate.Release();
        }
    }

    internal L12DeploymentDrainTransition CompleteSeal(L12DeploymentDrainOwner owner,
        long expectedSealingEpoch, L12DeploymentExternalReadiness externalReadiness)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_managementGate.Wait(0)) return Transition(L12DeploymentDrainTransitionCode.TransitionInProgress);
        try
        {
            long sealedEpoch;
            string sealId;
            lock (_gate)
            {
                if (_phase == L12DeploymentDrainPhase.Sealed && _fenceSynchronized
                    && _owner == owner && _sealId is not null
                    && expectedSealingEpoch == _epoch - 1)
                {
                    var currentPermit = PermitLocked();
                    return new L12DeploymentDrainTransition(L12DeploymentDrainTransitionCode.Idempotent,
                        SnapshotLocked(), currentPermit);
                }
                if (_phase != L12DeploymentDrainPhase.Sealing)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.WrongPhase);
                if (_fenceUnknown)
                    return FailSealLocked(L12DeploymentDrainTransitionCode.UnknownFence);
                if (_owner != owner || !OwnerBelongsToProcess(owner))
                    return TransitionLocked(L12DeploymentDrainTransitionCode.OwnerMismatch);
                if (_epoch != expectedSealingEpoch)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.StaleEpoch);
                if (_admissionLeases != 0 || _activityLeases != 0)
                    return FailSealLocked(L12DeploymentDrainTransitionCode.OutstandingLeases);
                if (externalReadiness != L12DeploymentExternalReadiness.Clear)
                {
                    var code = externalReadiness == L12DeploymentExternalReadiness.Unknown
                        ? L12DeploymentDrainTransitionCode.ExternalReadinessUnknown
                        : L12DeploymentDrainTransitionCode.ExternalReadinessBlocked;
                    return FailSealLocked(code);
                }

                sealedEpoch = NextEpoch(_epoch);
                sealId = Guid.NewGuid().ToString("N");
            }

            var persisted = PersistedFence(L12DeploymentDrainPhase.Sealed, sealedEpoch, owner, sealId);
            var written = TryWriteFence(persisted);
            lock (_gate)
            {
                if (!written || _phase != L12DeploymentDrainPhase.Sealing
                    || _epoch != expectedSealingEpoch || _owner != owner)
                {
                    _fenceSynchronized = false;
                    return FailSealLocked(L12DeploymentDrainTransitionCode.FenceFailure);
                }

                _phase = L12DeploymentDrainPhase.Sealed;
                _epoch = sealedEpoch;
                _sealId = sealId;
                _fenceSynchronized = true;
                var permit = PermitLocked();
                return new L12DeploymentDrainTransition(L12DeploymentDrainTransitionCode.Applied,
                    SnapshotLocked(), permit);
            }
        }
        finally
        {
            _managementGate.Release();
        }
    }

    internal L12DeploymentDrainTransition Cancel(L12DeploymentDrainOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_managementGate.Wait(0)) return Transition(L12DeploymentDrainTransitionCode.TransitionInProgress);
        try
        {
            lock (_gate)
            {
                if (_fenceUnknown) return TransitionLocked(L12DeploymentDrainTransitionCode.UnknownFence);
                if (_phase == L12DeploymentDrainPhase.Open)
                    return TransitionLocked(L12DeploymentDrainTransitionCode.WrongPhase);
                if (_owner != owner || !OwnerBelongsToProcess(owner))
                    return TransitionLocked(L12DeploymentDrainTransitionCode.OwnerMismatch);
            }

            var cleared = TryClearFence(owner);
            lock (_gate)
            {
                if (!cleared)
                {
                    _phase = L12DeploymentDrainPhase.Draining;
                    _epoch = NextEpoch(_epoch);
                    _sealId = null;
                    _fenceSynchronized = false;
                    return TransitionLocked(L12DeploymentDrainTransitionCode.FenceFailure);
                }

                _phase = L12DeploymentDrainPhase.Open;
                _epoch = NextEpoch(_epoch);
                _owner = null;
                _sealId = null;
                _fenceSynchronized = true;
                return TransitionLocked(L12DeploymentDrainTransitionCode.Applied);
            }
        }
        finally
        {
            _managementGate.Release();
        }
    }

    internal bool IsCurrentSealPermit(L12DeploymentSealPermit? permit)
    {
        lock (_gate) return IsCurrentSealPermitLocked(permit);
    }

    internal L12DeploymentDrainTransition CaptureCurrentSealPermit(L12DeploymentSealPermit? permit)
    {
        lock (_gate)
        {
            var valid = IsCurrentSealPermitLocked(permit);
            return new(valid ? L12DeploymentDrainTransitionCode.Applied : L12DeploymentDrainTransitionCode.StaleEpoch,
                SnapshotLocked(), valid ? permit : null);
        }
    }

    private bool IsCurrentSealPermitLocked(L12DeploymentSealPermit? permit)
        => permit is not null && _phase == L12DeploymentDrainPhase.Sealed
                && _fenceSynchronized && !_fenceUnknown
                && _owner is not null
                && permit.ProtocolVersion == ProtocolVersion
                && permit.OperationId == _owner.OperationId
                && permit.TargetCommit == _owner.TargetCommit
                && permit.ProcessInstance == _processInstance
                && permit.ActiveCommit == _activeCommit
                && permit.Epoch == _epoch
                && permit.SealId == _sealId;

    // Only bounded in-memory transport cleanup is allowed here. Never wait for a
    // room/platform gate or perform persistence under this short state lock.
    // Cancel and a new admission cannot race the cleanup after its sealed check.
    internal bool TryRunSealedTransportCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_gate)
        {
            if (_phase != L12DeploymentDrainPhase.Sealed || !_fenceSynchronized || _fenceUnknown)
                return false;
            try { cleanup(); return true; }
            catch
            {
                // An unexpected live game must invalidate, not reuse, the old permit.
                _phase = L12DeploymentDrainPhase.Draining;
                _epoch = NextEpoch(_epoch);
                _sealId = null;
                _fenceSynchronized = false;
                throw;
            }
        }
    }

    internal IDisposable EnterAdmissionExecutionScope(L12DeploymentDrainLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsOriginalIssuedBy(this, _leaseAuthority)
            || lease.Kind != L12DeploymentLeaseKind.Admission || !lease.TryRetainScope())
            throw new InvalidOperationException("只有本协调器仍有效的原始 admission lease 可建立执行作用域");

        var previous = NormalizeAdmissionScope();
        var node = new AdmissionScopeNode(lease.Root, previous);
        _admissionScope.Value = node;
        return new AdmissionExecutionScope(this, lease.Root, node);
    }

    private void LoadFence()
    {
        L12DeploymentDrainFenceLoadResult loaded;
        try { loaded = _fenceStore.Load() ?? L12DeploymentDrainFenceLoadResult.Unknown; }
        catch { loaded = L12DeploymentDrainFenceLoadResult.Unknown; }

        if (loaded.Kind == L12DeploymentDrainFenceLoadKind.Missing && loaded.Fence is null)
        {
            _phase = L12DeploymentDrainPhase.Open;
            _epoch = 0;
            _fenceSynchronized = true;
            return;
        }

        if (loaded.Kind == L12DeploymentDrainFenceLoadKind.Found && loaded.Fence is { } fence
            && TryValidateFence(fence, out var persistedOwner))
        {
            _phase = L12DeploymentDrainPhase.Draining;
            _epoch = NextEpoch(fence.Epoch);
            _owner = new L12DeploymentDrainOwnerProxy(persistedOwner!.OperationId,
                persistedOwner.TargetCommit, _processInstance).Owner;
            _fenceSynchronized = true;
            return;
        }

        _phase = L12DeploymentDrainPhase.Draining;
        _epoch = 1;
        _fenceUnknown = true;
        _fenceSynchronized = false;
    }

    // Keeps L12DeploymentDrainOwner's constructor private while allowing a recovered fence to bind
    // to this process instance only after the persisted identity has passed full validation.
    private sealed class L12DeploymentDrainOwnerProxy
    {
        internal L12DeploymentDrainOwnerProxy(string operationId, string targetCommit,
            string processInstance)
        {
            if (!L12DeploymentDrainOwner.TryCreate(operationId, targetCommit, processInstance,
                    out var owner))
                throw new InvalidOperationException("已验证的 deployment drain owner 无法重建");
            Owner = owner!;
        }

        internal L12DeploymentDrainOwner Owner { get; }
    }

    private bool TryValidateFence(L12DeploymentPersistedFence fence,
        out L12DeploymentDrainOwner? persistedOwner)
    {
        persistedOwner = null;
        if (fence.ProtocolVersion != ProtocolVersion
            || fence.Phase is not (L12DeploymentDrainPhase.Draining
                or L12DeploymentDrainPhase.Sealing or L12DeploymentDrainPhase.Sealed)
            || fence.Epoch < 0 || fence.Epoch == long.MaxValue
            || !IsCommit(fence.ActiveCommit)
            || !L12DeploymentDrainOwner.TryCreate(fence.OperationId, fence.TargetCommit,
                fence.ProcessInstance, out persistedOwner))
            return false;

        return fence.Phase != L12DeploymentDrainPhase.Sealed
            ? fence.SealId is null
            : IsCanonicalGuid(fence.SealId);
    }

    private L12DeploymentDrainTransition FailSealLocked(L12DeploymentDrainTransitionCode code)
    {
        _phase = L12DeploymentDrainPhase.Draining;
        _epoch = NextEpoch(_epoch);
        _sealId = null;
        return TransitionLocked(code);
    }

    private void ReleaseBarrier(L12DeploymentLeaseKind kind, object authority)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(authority, _leaseAuthority))
                throw new InvalidOperationException("未知 deployment drain lease");
            if (kind == L12DeploymentLeaseKind.Admission)
            {
                if (_admissionLeases <= 0)
                    throw new InvalidOperationException("deployment admission lease 计数失衡");
                _admissionLeases--;
            }
            else
            {
                if (_activityLeases <= 0)
                    throw new InvalidOperationException("deployment activity lease 计数失衡");
                _activityLeases--;
            }
        }
    }

    private AdmissionScopeNode? NormalizeAdmissionScope()
    {
        var current = _admissionScope.Value;
        var normalized = current;
        while (normalized is not null && Volatile.Read(ref normalized.Active) == 0)
            normalized = normalized.Previous;
        if (!ReferenceEquals(current, normalized)) _admissionScope.Value = normalized;
        return normalized;
    }

    private bool OwnerBelongsToProcess(L12DeploymentDrainOwner owner) =>
        owner.ProcessInstance == _processInstance;

    private L12DeploymentDrainTransition Transition(L12DeploymentDrainTransitionCode code)
    {
        lock (_gate) return TransitionLocked(code);
    }

    private L12DeploymentDrainTransition TransitionLocked(L12DeploymentDrainTransitionCode code) =>
        new(code, SnapshotLocked());

    private L12DeploymentDrainSnapshot SnapshotLocked() =>
        new(_phase, _epoch, _processInstance, _activeCommit, _owner, _admissionLeases,
            _activityLeases, _sealId, _fenceSynchronized, _fenceUnknown);

    private L12DeploymentSealPermit PermitLocked()
    {
        if (_owner is null || _sealId is null || _phase != L12DeploymentDrainPhase.Sealed)
            throw new InvalidOperationException("当前状态没有 deployment seal permit");
        return new L12DeploymentSealPermit(ProtocolVersion, _owner.OperationId,
            _owner.TargetCommit, _processInstance, _activeCommit, _epoch, _sealId);
    }

    private L12DeploymentPersistedFence PersistedFence(L12DeploymentDrainPhase phase, long epoch,
        L12DeploymentDrainOwner owner, string? sealId) =>
        new(ProtocolVersion, phase, epoch, owner.OperationId, owner.TargetCommit,
            _processInstance, _activeCommit, sealId);

    private bool TryWriteFence(L12DeploymentPersistedFence fence)
    {
        try { return _fenceStore.TryWrite(fence); }
        catch { return false; }
    }

    private bool TryClearFence(L12DeploymentDrainOwner owner)
    {
        try { return _fenceStore.TryClear(owner.OperationId, owner.TargetCommit); }
        catch { return false; }
    }

    private static long NextEpoch(long epoch) =>
        epoch == long.MaxValue ? throw new InvalidOperationException("deployment drain epoch 已耗尽") : epoch + 1;

    internal static bool IsCanonicalGuid(string? value) =>
        value is not null && Guid.TryParseExact(value, "N", out var parsed)
        && string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);

    internal static bool IsCommit(string? value)
    {
        if (value is null || value.Length != 40) return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }
        return true;
    }
}

internal sealed class L12DeploymentDrainLease : IDisposable
{
    private readonly L12DeploymentDrainCoordinator.LeaseRoot _root;
    private readonly bool _original;
    private readonly object _authority;
    private int _disposed;

    internal L12DeploymentDrainLease(L12DeploymentDrainCoordinator.LeaseRoot root, bool original,
        object authority)
    {
        _root = root;
        _original = original;
        _authority = authority;
    }

    internal L12DeploymentLeaseKind Kind => _root.Kind;
    internal bool IsOriginal => _original;
    internal bool IsActive => Volatile.Read(ref _disposed) == 0
        && (!_original || _root.OriginalActive);
    internal L12DeploymentDrainCoordinator Coordinator => _root.Coordinator;
    internal L12DeploymentDrainCoordinator.LeaseRoot Root => _root;

    internal bool IsOriginalIssuedBy(L12DeploymentDrainCoordinator coordinator, object authority) =>
        _original && ReferenceEquals(_authority, authority) && _root.IsIssuedBy(coordinator, authority);

    internal IDisposable EnterExecutionScope() => Coordinator.EnterAdmissionExecutionScope(this);

    internal bool TryRetainScope() => Volatile.Read(ref _disposed) == 0
        && _original && _root.TryRetainScope();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_original) _root.ReleaseOriginal();
        else _root.ReleaseReference();
    }
}
