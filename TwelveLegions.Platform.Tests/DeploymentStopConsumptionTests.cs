using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class DeploymentStopConsumptionTests
{
    private const string ActiveCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TargetCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private const string ProcessInstance = "11111111111111111111111111111111";
    private const string OtherProcessInstance = "22222222222222222222222222222222";
    private const string OperationId = "33333333333333333333333333333333";
    private const string OtherOperationId = "44444444444444444444444444444444";
    private const string OtherSealId = "55555555555555555555555555555555";

    [Fact]
    public void ConsumingCurrentPermitPreventsCancelFromClearingFenceOrReopeningAdmission()
    {
        var fixture = CreateSealed();

        var consumed = fixture.Coordinator.ConsumeStopPermit(fixture.Permit);

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, consumed.Code);
        Assert.Equal(fixture.Permit, consumed.Permit);
        Assert.True(consumed.Snapshot.StopConsumed);
        Assert.True(L12DeploymentDrainOwner.TryCreate(OtherOperationId, TargetCommit,
            ProcessInstance, out var foreignOwner));
        Assert.Equal(L12DeploymentDrainTransitionCode.OwnerMismatch,
            fixture.Coordinator.Cancel(foreignOwner!).Code);
        var cancel = fixture.Coordinator.Cancel(fixture.Owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.WrongPhase, cancel.Code);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, cancel.Snapshot.Phase);
        Assert.True(cancel.Snapshot.StopConsumed);
        Assert.Equal(0, fixture.Store.ClearAttempts);
        Assert.NotNull(fixture.Store.CurrentFence);
        Assert.False(fixture.Coordinator.TryAcquireAdmission(out _));
        Assert.False(fixture.Coordinator.TryAcquireActivity(out _));
    }

    [Fact]
    public void CancelBeforeConsumptionMakesReturnedPermitStaleAndLeavesMarkerUnset()
    {
        var fixture = CreateSealed();

        var cancel = fixture.Coordinator.Cancel(fixture.Owner);
        var stale = fixture.Coordinator.ConsumeStopPermit(fixture.Permit);

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, cancel.Code);
        Assert.Equal(L12DeploymentDrainTransitionCode.StaleEpoch, stale.Code);
        Assert.Null(stale.Permit);
        Assert.Equal(L12DeploymentDrainPhase.Open, stale.Snapshot.Phase);
        Assert.False(stale.Snapshot.StopConsumed);
        Assert.Equal(1, fixture.Store.ClearAttempts);
        Assert.True(fixture.Coordinator.TryAcquireAdmission(out var admission));
        admission!.Dispose();
    }

    [Fact]
    public async Task ConcurrentCancelAndConsumeHaveExactlyOneSuccessfulWinner()
    {
        var fixture = CreateSealed();
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        var consumeTask = Task.Factory.StartNew(() =>
        {
            ready.Signal();
            Assert.True(start.Wait(TimeSpan.FromSeconds(3)));
            return fixture.Coordinator.ConsumeStopPermit(fixture.Permit);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var cancelTask = Task.Factory.StartNew(() =>
        {
            ready.Signal();
            Assert.True(start.Wait(TimeSpan.FromSeconds(3)));
            return fixture.Coordinator.Cancel(fixture.Owner);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(3)));

        start.Set();
        var consume = await consumeTask.WaitAsync(TimeSpan.FromSeconds(3));
        var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, (consume.Succeeded ? 1 : 0) + (cancel.Succeeded ? 1 : 0));
        var snapshot = fixture.Coordinator.Snapshot();
        if (consume.Succeeded)
        {
            Assert.False(cancel.Succeeded);
            Assert.True(snapshot.StopConsumed);
            Assert.Equal(L12DeploymentDrainPhase.Sealed, snapshot.Phase);
            Assert.Equal(0, fixture.Store.ClearAttempts);
            Assert.False(fixture.Coordinator.TryAcquireAdmission(out _));
        }
        else
        {
            Assert.True(cancel.Succeeded);
            Assert.False(snapshot.StopConsumed);
            Assert.Equal(L12DeploymentDrainPhase.Open, snapshot.Phase);
            Assert.Equal(1, fixture.Store.ClearAttempts);
            Assert.True(fixture.Coordinator.TryAcquireAdmission(out var admission));
            admission!.Dispose();
        }
    }

    [Fact]
    public async Task ConsumeIsRejectedAfterCancelEntersFenceIo()
    {
        var fixture = CreateSealed();
        fixture.Store.BlockClear = true;
        var cancelTask = Task.Factory.StartNew(() => fixture.Coordinator.Cancel(fixture.Owner),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(fixture.Store.ClearEntered.Wait(TimeSpan.FromSeconds(3)));

        try
        {
            var consume = fixture.Coordinator.ConsumeStopPermit(fixture.Permit);
            Assert.Equal(L12DeploymentDrainTransitionCode.TransitionInProgress, consume.Code);
            Assert.False(consume.Snapshot.StopConsumed);
            Assert.Null(consume.Permit);
        }
        finally
        {
            fixture.Store.ReleaseClear.Set();
        }
        var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, cancel.Code);
        Assert.Equal(L12DeploymentDrainPhase.Open, fixture.Coordinator.Snapshot().Phase);
        Assert.False(fixture.Coordinator.Snapshot().StopConsumed);
        Assert.Equal(1, fixture.Store.ClearAttempts);
    }

    [Fact]
    public void ExactConsumedTupleCanBeRetriedButDifferentTupleCannotChangeMarker()
    {
        var fixture = CreateSealed();
        var invalidPermits = new[]
        {
            fixture.Permit with { ProtocolVersion = fixture.Permit.ProtocolVersion + 1 },
            fixture.Permit with { OperationId = OtherOperationId },
            fixture.Permit with { TargetCommit = OtherCommit },
            fixture.Permit with { ProcessInstance = OtherProcessInstance },
            fixture.Permit with { ActiveCommit = OtherCommit },
            fixture.Permit with { Epoch = fixture.Permit.Epoch + 1 },
            fixture.Permit with { SealId = OtherSealId },
        };
        foreach (var invalidPermit in invalidPermits)
        {
            var rejected = fixture.Coordinator.ConsumeStopPermit(invalidPermit);
            Assert.Equal(L12DeploymentDrainTransitionCode.StaleEpoch, rejected.Code);
            Assert.False(rejected.Snapshot.StopConsumed);
            Assert.Null(rejected.Permit);
        }

        var first = fixture.Coordinator.ConsumeStopPermit(fixture.Permit);
        var retry = fixture.Coordinator.ConsumeStopPermit(fixture.Permit);
        var wrongRetry = fixture.Coordinator.ConsumeStopPermit(
            fixture.Permit with { SealId = OtherSealId });

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, first.Code);
        Assert.Equal(L12DeploymentDrainTransitionCode.Idempotent, retry.Code);
        Assert.Equal(fixture.Permit, retry.Permit);
        Assert.True(retry.Snapshot.StopConsumed);
        Assert.Equal(L12DeploymentDrainTransitionCode.StaleEpoch, wrongRetry.Code);
        Assert.Null(wrongRetry.Permit);
        Assert.True(wrongRetry.Snapshot.StopConsumed);
    }

    [Fact]
    public void RestartDoesNotPersistConsumptionAndRejectsOldProcessPermit()
    {
        var fixture = CreateSealed();
        Assert.True(fixture.Coordinator.ConsumeStopPermit(fixture.Permit).Succeeded);

        var restarted = new L12DeploymentDrainCoordinator(OtherProcessInstance, ActiveCommit,
            fixture.Store);
        var snapshot = restarted.Snapshot();
        var rejected = restarted.ConsumeStopPermit(fixture.Permit);

        Assert.Equal(L12DeploymentDrainPhase.Draining, snapshot.Phase);
        Assert.Equal(fixture.Permit.Epoch + 1, snapshot.Epoch);
        Assert.Equal(OtherProcessInstance, snapshot.ProcessInstance);
        Assert.Equal(OtherProcessInstance, snapshot.Owner!.ProcessInstance);
        Assert.False(snapshot.StopConsumed);
        Assert.Equal(L12DeploymentDrainTransitionCode.StaleEpoch, rejected.Code);
        Assert.False(rejected.Snapshot.StopConsumed);
        Assert.False(restarted.TryAcquireAdmission(out _));
        Assert.True(restarted.TryAcquireActivity(out var recovery));
        recovery!.Dispose();
    }

    [Fact]
    public void ConsumedMarkerSurvivesCleanupFailureAndKeepsEveryLeaseClosed()
    {
        var fixture = CreateSealed();
        Assert.True(fixture.Coordinator.ConsumeStopPermit(fixture.Permit).Succeeded);

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Coordinator.TryRunSealedTransportCleanup(
                () => throw new InvalidOperationException("synthetic cleanup failure")));

        var failed = fixture.Coordinator.Snapshot();
        Assert.Equal(L12DeploymentDrainPhase.Draining, failed.Phase);
        Assert.False(failed.FenceSynchronized);
        Assert.True(failed.StopConsumed);
        Assert.False(fixture.Coordinator.TryAcquireAdmission(out _));
        Assert.False(fixture.Coordinator.TryAcquireActivity(out _));
        var retry = fixture.Coordinator.ConsumeStopPermit(fixture.Permit);
        Assert.Equal(L12DeploymentDrainTransitionCode.StaleEpoch, retry.Code);
        Assert.Null(retry.Permit);
        Assert.True(retry.Snapshot.StopConsumed);
        Assert.Equal(L12DeploymentDrainTransitionCode.WrongPhase,
            fixture.Coordinator.Cancel(fixture.Owner).Code);
        Assert.Equal(0, fixture.Store.ClearAttempts);
    }

    private static SealedFixture CreateSealed()
    {
        var store = new SyntheticFenceStore();
        var coordinator = new L12DeploymentDrainCoordinator(ProcessInstance, ActiveCommit, store);
        var owner = CreateOwner(ProcessInstance);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
            coordinator.BeginDrain(owner).Code);
        var sealing = coordinator.TryBeginSeal(owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, sealing.Code);
        var completed = coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, completed.Code);
        var permit = Assert.IsType<L12DeploymentSealPermit>(completed.Permit);
        return new SealedFixture(store, coordinator, owner, permit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelWithdrawsSealedCleanupBeforeFenceIoAndAdvancesEpochOnlyOnce(bool clearSucceeds)
    {
        var fixture = CreateSealed();
        fixture.Store.BlockClear = true;
        fixture.Store.ClearSucceeds = clearSucceeds;
        var originalEpoch = fixture.Permit.Epoch;
        var cancelTask = Task.Factory.StartNew(() => fixture.Coordinator.Cancel(fixture.Owner),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(fixture.Store.ClearEntered.Wait(TimeSpan.FromSeconds(3)));
        try
        {
            var called = false;
            Assert.False(fixture.Coordinator.TryRunSealedTransportCleanup(() => called = true));
            Assert.False(called);
            var during = fixture.Coordinator.Snapshot();
            Assert.Equal(L12DeploymentDrainPhase.Draining, during.Phase);
            Assert.Equal(originalEpoch + 1, during.Epoch);
            Assert.Null(during.SealId);
            Assert.False(during.FenceSynchronized);
            Assert.False(fixture.Coordinator.IsCurrentSealPermit(fixture.Permit));
            Assert.False(fixture.Coordinator.TryAcquireAdmission(out _));
            Assert.True(fixture.Coordinator.TryAcquireActivity(out var activity));
            activity!.Dispose();
        }
        finally
        {
            fixture.Store.ReleaseClear.Set();
            await cancelTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        var result = await cancelTask;
        Assert.Equal(originalEpoch + 1, result.Snapshot.Epoch);
        Assert.Equal(clearSucceeds ? L12DeploymentDrainPhase.Open : L12DeploymentDrainPhase.Draining,
            result.Snapshot.Phase);
        Assert.Equal(clearSucceeds, result.Snapshot.FenceSynchronized);
    }

    private static L12DeploymentDrainOwner CreateOwner(string processInstance)
    {
        Assert.True(L12DeploymentDrainOwner.TryCreate(OperationId, TargetCommit, processInstance,
            out var owner));
        return owner!;
    }

    private sealed record SealedFixture(SyntheticFenceStore Store,
        L12DeploymentDrainCoordinator Coordinator, L12DeploymentDrainOwner Owner,
        L12DeploymentSealPermit Permit);

    private sealed class SyntheticFenceStore : IL12DeploymentDrainFenceStore
    {
        private readonly object _gate = new();
        private L12DeploymentPersistedFence? _currentFence;
        private int _clearAttempts;

        internal bool BlockClear { get; set; }
        internal bool ClearSucceeds { get; set; } = true;
        internal ManualResetEventSlim ClearEntered { get; } = new(false);
        internal ManualResetEventSlim ReleaseClear { get; } = new(false);
        internal int ClearAttempts => Volatile.Read(ref _clearAttempts);
        internal L12DeploymentPersistedFence? CurrentFence
        {
            get { lock (_gate) return _currentFence; }
        }

        public L12DeploymentDrainFenceLoadResult Load()
        {
            lock (_gate)
            {
                return _currentFence is null
                    ? L12DeploymentDrainFenceLoadResult.Missing
                    : L12DeploymentDrainFenceLoadResult.Found(_currentFence);
            }
        }

        public bool TryWrite(L12DeploymentPersistedFence fence)
        {
            lock (_gate) _currentFence = fence;
            return true;
        }

        public bool TryClear(string operationId, string targetCommit)
        {
            Interlocked.Increment(ref _clearAttempts);
            ClearEntered.Set();
            if (BlockClear && !ReleaseClear.Wait(TimeSpan.FromSeconds(5))) return false;
            if (!ClearSucceeds) return false;
            lock (_gate)
            {
                if (_currentFence is null
                    || _currentFence.OperationId != operationId
                    || _currentFence.TargetCommit != targetCommit)
                    return false;
                _currentFence = null;
                return true;
            }
        }
    }
}
