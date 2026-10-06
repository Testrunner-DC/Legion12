using System.Collections.Concurrent;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class DeploymentDrainCoordinatorTests
{
    private const string ActiveCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TargetCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private const string ProcessInstance = "11111111111111111111111111111111";
    private const string OtherProcessInstance = "22222222222222222222222222222222";
    private const string OperationId = "33333333333333333333333333333333";
    private const string OtherOperationId = "44444444444444444444444444444444";

    [Fact]
    public void PhaseMatrixCountsEveryLeaseAndOnlyExplicitCancelReopens()
    {
        var store = new FakeFenceStore();
        var coordinator = CreateCoordinator(store);
        var owner = CreateOwner();

        Assert.True(coordinator.TryAcquireAdmission(out var admission));
        Assert.True(coordinator.TryAcquireActivity(out var activity));
        var draining = coordinator.BeginDrain(owner);

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, draining.Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, draining.Snapshot.Phase);
        Assert.Equal(1, draining.Snapshot.Epoch);
        Assert.Equal(1, draining.Snapshot.AdmissionLeases);
        Assert.Equal(1, draining.Snapshot.ActivityLeases);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.True(coordinator.TryAcquireActivity(out var drainingActivity));
        Assert.Equal(L12DeploymentDrainTransitionCode.OutstandingLeases,
            coordinator.TryBeginSeal(owner).Code);

        admission!.Dispose();
        activity!.Dispose();
        drainingActivity!.Dispose();
        var sealing = coordinator.TryBeginSeal(owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, sealing.Code);
        Assert.Equal(L12DeploymentDrainPhase.Sealing, sealing.Snapshot.Phase);
        Assert.Equal(2, sealing.Snapshot.Epoch);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.False(coordinator.TryAcquireActivity(out _));

        var sealedResult = coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, sealedResult.Snapshot.Phase);
        Assert.Equal(3, sealedResult.Snapshot.Epoch);
        Assert.NotNull(sealedResult.Permit);
        Assert.True(coordinator.IsCurrentSealPermit(sealedResult.Permit));
        var observation = coordinator.CaptureCurrentSealPermit(sealedResult.Permit);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, observation.Snapshot.Phase);
        Assert.Equal(sealedResult.Permit, observation.Permit);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.False(coordinator.TryAcquireActivity(out _));

        var reopened = coordinator.Cancel(owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, reopened.Code);
        Assert.Equal(L12DeploymentDrainPhase.Open, reopened.Snapshot.Phase);
        Assert.Equal(4, reopened.Snapshot.Epoch);
        Assert.False(coordinator.IsCurrentSealPermit(sealedResult.Permit));
        var staleObservation = coordinator.CaptureCurrentSealPermit(sealedResult.Permit);
        Assert.Equal(L12DeploymentDrainPhase.Open, staleObservation.Snapshot.Phase);
        Assert.Null(staleObservation.Permit);
        Assert.True(coordinator.TryAcquireAdmission(out var afterCancel));
        afterCancel!.Dispose();
    }

    [Fact]
    public void BeginDrainRejectsNewAdmissionWithoutWaitingForAlreadyAcceptedWork()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        Assert.True(coordinator.TryAcquireAdmission(out var oldAdmission));

        var result = coordinator.BeginDrain(owner);

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, result.Code);
        Assert.Equal(1, result.Snapshot.AdmissionLeases);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        oldAdmission!.Dispose();
        Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
    }

    [Fact]
    public void OwnerEpochCommitAndPermitBindingsCannotBeReplayed()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        var otherOperation = CreateOwner(OtherOperationId, TargetCommit, ProcessInstance);
        var otherTarget = CreateOwner(OperationId, OtherCommit, ProcessInstance);
        var otherProcess = CreateOwner(OperationId, TargetCommit, OtherProcessInstance);

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, coordinator.BeginDrain(owner).Code);
        Assert.Equal(L12DeploymentDrainTransitionCode.OwnerMismatch,
            coordinator.BeginDrain(otherOperation).Code);
        Assert.Equal(L12DeploymentDrainTransitionCode.OwnerMismatch,
            coordinator.BeginDrain(otherTarget).Code);
        Assert.Equal(L12DeploymentDrainTransitionCode.OwnerMismatch,
            coordinator.BeginDrain(otherProcess).Code);

        var firstSealing = coordinator.TryBeginSeal(owner);
        var stale = coordinator.CompleteSeal(owner, firstSealing.Snapshot.Epoch + 1,
            L12DeploymentExternalReadiness.Clear);
        Assert.Equal(L12DeploymentDrainTransitionCode.StaleEpoch, stale.Code);
        Assert.Equal(L12DeploymentDrainPhase.Sealing, stale.Snapshot.Phase);
        Assert.Null(stale.Permit);
        var foreignCompletion = coordinator.CompleteSeal(otherOperation,
            firstSealing.Snapshot.Epoch, L12DeploymentExternalReadiness.Clear);
        Assert.Equal(L12DeploymentDrainTransitionCode.OwnerMismatch, foreignCompletion.Code);
        Assert.Equal(L12DeploymentDrainPhase.Sealing, foreignCompletion.Snapshot.Phase);

        var completed = coordinator.CompleteSeal(owner, firstSealing.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear);
        var permit = Assert.IsType<L12DeploymentSealPermit>(completed.Permit);
        Assert.Equal(ActiveCommit, permit.ActiveCommit);
        Assert.Equal(TargetCommit, permit.TargetCommit);
        Assert.NotEqual(permit.ActiveCommit, permit.TargetCommit);
        Assert.True(coordinator.IsCurrentSealPermit(permit));
        Assert.False(coordinator.IsCurrentSealPermit(permit with { ActiveCommit = TargetCommit }));
        Assert.False(coordinator.IsCurrentSealPermit(permit with { TargetCommit = OtherCommit }));
        Assert.False(coordinator.IsCurrentSealPermit(permit with { Epoch = permit.Epoch - 1 }));
        Assert.False(coordinator.IsCurrentSealPermit(permit with
        {
            ProcessInstance = OtherProcessInstance,
        }));
        Assert.Equal(L12DeploymentDrainTransitionCode.OwnerMismatch,
            coordinator.Cancel(otherOperation).Code);
        Assert.True(coordinator.IsCurrentSealPermit(permit));

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, coordinator.Cancel(owner).Code);
        Assert.False(coordinator.IsCurrentSealPermit(permit));
    }

    [Fact]
    public void OldLeaseSurvivesEpochChangesAndDoubleDisposeIsHarmless()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        Assert.True(coordinator.TryAcquireAdmission(out var oldLease));
        coordinator.BeginDrain(owner);
        coordinator.Cancel(owner);
        Assert.Equal(L12DeploymentDrainPhase.Open, coordinator.Snapshot().Phase);
        Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);
        Assert.True(coordinator.TryAcquireAdmission(out var newLease));
        Assert.Equal(2, coordinator.Snapshot().AdmissionLeases);

        oldLease!.Dispose();
        oldLease.Dispose();
        Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);
        newLease!.Dispose();
        newLease.Dispose();
        Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
    }

    [Fact]
    public void AdmissionExecutionScopeBorrowsOnlyOriginalLiveLeaseAndRestoresNestedScope()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var otherCoordinator = CreateCoordinator(new FakeFenceStore(), OtherProcessInstance);
        var owner = CreateOwner();
        Assert.True(coordinator.TryAcquireAdmission(out var root));
        Assert.True(otherCoordinator.TryAcquireAdmission(out var otherRoot));
        coordinator.BeginDrain(owner);

        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.EnterAdmissionExecutionScope(otherRoot!));

        L12DeploymentDrainLease borrowed;
        using (root!.EnterExecutionScope())
        {
            Assert.True(coordinator.TryAcquireAdmission(out var firstBorrow));
            borrowed = firstBorrow!;
            Assert.False(borrowed.IsOriginal);
            Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);
            Assert.Throws<InvalidOperationException>(() => borrowed.EnterExecutionScope());

            using (root.EnterExecutionScope())
            {
                Assert.True(coordinator.TryAcquireAdmission(out var nestedBorrow));
                nestedBorrow!.Dispose();
            }
            Assert.True(coordinator.TryAcquireAdmission(out var restoredBorrow));
            restoredBorrow!.Dispose();
        }

        Assert.False(coordinator.TryAcquireAdmission(out _));
        root.Dispose();
        Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);
        borrowed.Dispose();
        Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
        Assert.Throws<InvalidOperationException>(() => root.EnterExecutionScope());
        otherRoot!.Dispose();
    }

    [Fact]
    public async Task CapturedChildContextCannotBorrowAfterSharedScopeExits()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        Assert.True(coordinator.TryAcquireAdmission(out var root));
        coordinator.BeginDrain(owner);
        var runChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> child;

        using (root!.EnterExecutionScope())
        {
            child = Task.Run(async () =>
            {
                await runChild.Task;
                var acquired = coordinator.TryAcquireAdmission(out var lease);
                lease?.Dispose();
                return acquired;
            });
        }

        runChild.TrySetResult();
        Assert.False(await child.WaitAsync(TimeSpan.FromSeconds(2)));
        root.Dispose();
        Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
    }

    [Fact]
    public void FenceFailuresRemainClosedAndSealFailureReturnsToDraining()
    {
        var store = new FakeFenceStore { WriteSucceeds = false };
        var coordinator = CreateCoordinator(store);
        var owner = CreateOwner();

        var beginFailure = coordinator.BeginDrain(owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.FenceFailure, beginFailure.Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, beginFailure.Snapshot.Phase);
        Assert.False(beginFailure.Snapshot.FenceSynchronized);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.Equal(L12DeploymentDrainTransitionCode.FenceFailure,
            coordinator.TryBeginSeal(owner).Code);

        store.WriteSucceeds = true;
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, coordinator.BeginDrain(owner).Code);
        var sealing = coordinator.TryBeginSeal(owner);
        store.WriteSucceeds = false;
        var sealFailure = coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear);
        Assert.Equal(L12DeploymentDrainTransitionCode.FenceFailure, sealFailure.Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, sealFailure.Snapshot.Phase);
        Assert.Null(sealFailure.Permit);
        Assert.True(coordinator.TryAcquireActivity(out var worker));
        worker!.Dispose();

        store.WriteSucceeds = true;
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, coordinator.BeginDrain(owner).Code);
        var retry = coordinator.TryBeginSeal(owner);
        var sealedResult = coordinator.CompleteSeal(owner, retry.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear);
        Assert.NotNull(sealedResult.Permit);
        store.ClearSucceeds = false;
        Assert.Equal(L12DeploymentDrainTransitionCode.FenceFailure,
            coordinator.Cancel(owner).Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, coordinator.Snapshot().Phase);
        Assert.False(coordinator.IsCurrentSealPermit(sealedResult.Permit));
        Assert.False(coordinator.TryAcquireAdmission(out _));
    }

    [Theory]
    [InlineData((int)L12DeploymentExternalReadiness.Blocked,
        (int)L12DeploymentDrainTransitionCode.ExternalReadinessBlocked)]
    [InlineData((int)L12DeploymentExternalReadiness.Unknown,
        (int)L12DeploymentDrainTransitionCode.ExternalReadinessUnknown)]
    public void FailedExternalReadinessNeverGrantsPermitAndWorkersCanResume(
        int readinessValue, int expectedCodeValue)
    {
        var readiness = (L12DeploymentExternalReadiness)readinessValue;
        var expectedCode = (L12DeploymentDrainTransitionCode)expectedCodeValue;
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        coordinator.BeginDrain(owner);
        var sealing = coordinator.TryBeginSeal(owner);

        var result = coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch, readiness);

        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Permit);
        Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
        Assert.True(coordinator.TryAcquireActivity(out var resumedWorker));
        resumedWorker!.Dispose();
    }

    [Fact]
    public void UnknownAndMalformedPersistedFencesStartClosed()
    {
        var unknown = CreateCoordinator(new FakeFenceStore
        {
            LoadResult = L12DeploymentDrainFenceLoadResult.Unknown,
        });
        AssertClosedUnknown(unknown);

        var malformed = CreateCoordinator(new FakeFenceStore
        {
            LoadResult = L12DeploymentDrainFenceLoadResult.Found(
                new L12DeploymentPersistedFence(1, L12DeploymentDrainPhase.Open, 7,
                    OperationId, TargetCommit, ProcessInstance, ActiveCommit, null)),
        });
        AssertClosedUnknown(malformed);

        var throwing = CreateCoordinator(new FakeFenceStore { ThrowOnLoad = true });
        AssertClosedUnknown(throwing);
    }

    [Fact]
    public void RestartedProcessRebindsValidFenceAndInvalidatesOldProcessIdentity()
    {
        var oldSealId = "55555555555555555555555555555555";
        var store = new FakeFenceStore
        {
            LoadResult = L12DeploymentDrainFenceLoadResult.Found(
                new L12DeploymentPersistedFence(1, L12DeploymentDrainPhase.Sealed, 9,
                    OperationId, TargetCommit, OtherProcessInstance, ActiveCommit, oldSealId)),
        };
        var coordinator = CreateCoordinator(store);

        var snapshot = coordinator.Snapshot();
        Assert.Equal(L12DeploymentDrainPhase.Draining, snapshot.Phase);
        Assert.Equal(10, snapshot.Epoch);
        Assert.Equal(ProcessInstance, snapshot.Owner!.ProcessInstance);
        Assert.NotEqual(OtherProcessInstance, snapshot.Owner.ProcessInstance);
        Assert.Null(snapshot.SealId);
        Assert.True(coordinator.TryAcquireActivity(out var recovery));
        recovery!.Dispose();
    }

    [Fact]
    public async Task ManagementOperationsAreFailFastSerializedAcrossFenceIo()
    {
        var store = new FakeFenceStore { BlockWrites = true };
        var coordinator = CreateCoordinator(store);
        var owner = CreateOwner();
        var begin = Task.Run(() => coordinator.BeginDrain(owner));
        Assert.True(store.WriteEntered.Wait(TimeSpan.FromSeconds(2)));

        var racingCancel = coordinator.Cancel(owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.TransitionInProgress, racingCancel.Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, racingCancel.Snapshot.Phase);

        store.ReleaseWrite.Set();
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
            (await begin.WaitAsync(TimeSpan.FromSeconds(2))).Code);
        Assert.NotNull(store.CurrentFence);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, coordinator.Cancel(owner).Code);
        Assert.Null(store.CurrentFence);
        Assert.Equal(L12DeploymentDrainPhase.Open, coordinator.Snapshot().Phase);
    }

    [Fact]
    public async Task InboundLeaseCoversQueuedAndSlowExecutingAdmissionUntilFinally()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new ConcurrentQueue<string>();
        var inbound = new L12InboundConnection(async json =>
        {
            observed.Enqueue(json);
            Assert.True(coordinator.TryAcquireAdmission(out var nested));
            nested!.Dispose();
            if (json == "first")
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
            else
            {
                secondEntered.TrySetResult();
                await releaseSecond.Task;
            }
        }, leaseAcquirer: _ => AcquireAdmission(coordinator));

        try
        {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("first"));
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("second"));
            var draining = coordinator.BeginDrain(owner);
            Assert.Equal(2, draining.Snapshot.AdmissionLeases);
            Assert.Equal(2, inbound.RetainedMessages);

            releaseFirst.TrySetResult();
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);
            Assert.Equal(1, inbound.RetainedMessages);
            releaseSecond.TrySetResult();
            await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(new string[] { "first", "second" }, observed.ToArray());
            Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
            Assert.Equal(0, inbound.RetainedMessages);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task InboundCapacityRejectionAndPendingCancellationReleaseExactlyTheirLeases()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inbound = new L12InboundConnection(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }, leaseAcquirer: _ => AcquireActivity(coordinator));

        try
        {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("executing"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var index = 1; index < L12InboundConnection.MaximumRetainedMessages; index++)
                Assert.Equal(L12InboundEnqueueResult.Accepted,
                    inbound.TryEnqueue($"queued-{index}"));
            Assert.Equal(8, coordinator.Snapshot().ActivityLeases);

            Assert.Equal(L12InboundEnqueueResult.MessageLimit, inbound.TryEnqueue("overflow"));
            Assert.Equal(8, coordinator.Snapshot().ActivityLeases);
            inbound.StopAcceptingAndCancelPending();
            Assert.Equal(1, coordinator.Snapshot().ActivityLeases);
            Assert.Equal(1, inbound.RetainedMessages);
        }
        finally
        {
            release.TrySetResult();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(0, coordinator.Snapshot().ActivityLeases);
    }

    [Fact]
    public async Task InboundHandlerFaultReleasesExecutingAndQueuedLeasesAndKeepsFaultCallback()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = 0;
        var inbound = new L12InboundConnection(async _ =>
        {
            Interlocked.Increment(ref handled);
            entered.TrySetResult();
            await fail.Task;
            throw new InvalidOperationException("expected-handler-fault");
        }, error => faulted.TrySetResult(error), _ => AcquireActivity(coordinator));

        try
        {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("faulting"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("must-cancel"));
            Assert.Equal(2, coordinator.Snapshot().ActivityLeases);
            fail.TrySetResult();
            var error = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("expected-handler-fault", error.Message);
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, handled);
            Assert.Equal(0, coordinator.Snapshot().ActivityLeases);
            Assert.Equal(0, inbound.RetainedMessages);
        }
        finally
        {
            fail.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task InboundReturnsExplicitDeploymentDrainingWithoutRetainingWork()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        coordinator.BeginDrain(CreateOwner());
        var handled = 0;
        var inbound = new L12InboundConnection(_ =>
        {
            Interlocked.Increment(ref handled);
            return Task.CompletedTask;
        }, leaseAcquirer: _ => AcquireAdmission(coordinator));

        try
        {
            Assert.Equal(L12InboundEnqueueResult.DeploymentDraining,
                inbound.TryEnqueue("new-admission"));
            Assert.Equal(0, inbound.RetainedMessages);
            Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
            Assert.Equal(0, handled);
        }
        finally
        {
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void OwnerRequiresCanonicalOperationProcessAndFortyHexCommit()
    {
        Assert.False(L12DeploymentDrainOwner.TryCreate(Guid.NewGuid().ToString("D"), TargetCommit,
            ProcessInstance, out _));
        Assert.False(L12DeploymentDrainOwner.TryCreate("AAAAAAAA111111111111111111111111",
            TargetCommit, ProcessInstance, out _));
        Assert.False(L12DeploymentDrainOwner.TryCreate(OperationId, "abc", ProcessInstance, out _));
        Assert.False(L12DeploymentDrainOwner.TryCreate(OperationId, TargetCommit, "process", out _));
        Assert.True(L12DeploymentDrainOwner.TryCreate(OperationId, TargetCommit.ToUpperInvariant(),
            ProcessInstance, out var normalized));
        Assert.Equal(TargetCommit, normalized!.TargetCommit);
    }

    [Fact]
    public void UnexpectedSealedTransportStateInvalidatesPermitWithoutReopening()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        coordinator.BeginDrain(owner);
        var sealing = coordinator.TryBeginSeal(owner);
        var completed = coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch, L12DeploymentExternalReadiness.Clear);
        Assert.True(coordinator.IsCurrentSealPermit(completed.Permit));
        Assert.Throws<InvalidOperationException>(() => coordinator.TryRunSealedTransportCleanup(
            () => throw new InvalidOperationException("synthetic unexpected active room")));
        Assert.False(coordinator.IsCurrentSealPermit(completed.Permit));
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.Equal(L12DeploymentDrainPhase.Draining, coordinator.Snapshot().Phase);
        Assert.False(coordinator.Snapshot().FenceSynchronized);
    }

    [Fact]
    public async Task SealedCleanupAndExplicitReopenAreLinearized()
    {
        var coordinator = CreateCoordinator(new FakeFenceStore());
        var owner = CreateOwner();
        coordinator.BeginDrain(owner);
        var sealing = coordinator.TryBeginSeal(owner);
        var completed = coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch, L12DeploymentExternalReadiness.Clear);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cleanup = Task.Factory.StartNew(() => coordinator.TryRunSealedTransportCleanup(() =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(3)));
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        var cancel = Task.Run(() => coordinator.Cancel(owner));
        try { Assert.NotSame(cancel, await Task.WhenAny(cancel, Task.Delay(50))); }
        finally { release.Set(); }
        Assert.True(await cleanup.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True((await cancel.WaitAsync(TimeSpan.FromSeconds(3))).Succeeded);
        Assert.False(coordinator.IsCurrentSealPermit(completed.Permit));
        var called = false;
        Assert.False(coordinator.TryRunSealedTransportCleanup(() => called = true));
        Assert.False(called);
    }

    private static void AssertClosedUnknown(L12DeploymentDrainCoordinator coordinator)
    {
        var snapshot = coordinator.Snapshot();
        Assert.Equal(L12DeploymentDrainPhase.Draining, snapshot.Phase);
        Assert.True(snapshot.FenceUnknown);
        Assert.False(snapshot.FenceSynchronized);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.True(coordinator.TryAcquireActivity(out var activity));
        activity!.Dispose();
        Assert.Equal(L12DeploymentDrainTransitionCode.UnknownFence,
            coordinator.BeginDrain(CreateOwner()).Code);
    }

    private static L12DeploymentDrainLease? AcquireAdmission(
        L12DeploymentDrainCoordinator coordinator) =>
        coordinator.TryAcquireAdmission(out var lease) ? lease : null;

    private static L12DeploymentDrainLease? AcquireActivity(
        L12DeploymentDrainCoordinator coordinator) =>
        coordinator.TryAcquireActivity(out var lease) ? lease : null;

    private static L12DeploymentDrainCoordinator CreateCoordinator(FakeFenceStore store,
        string processInstance = ProcessInstance) => new(processInstance, ActiveCommit, store);

    private static L12DeploymentDrainOwner CreateOwner(string operationId = OperationId,
        string targetCommit = TargetCommit, string processInstance = ProcessInstance)
    {
        Assert.True(L12DeploymentDrainOwner.TryCreate(operationId, targetCommit, processInstance,
            out var owner));
        return owner!;
    }

    private sealed class FakeFenceStore : IL12DeploymentDrainFenceStore
    {
        private readonly object _gate = new();

        internal L12DeploymentDrainFenceLoadResult LoadResult { get; set; } =
            L12DeploymentDrainFenceLoadResult.Missing;
        internal bool ThrowOnLoad { get; set; }
        internal bool WriteSucceeds { get; set; } = true;
        internal bool ClearSucceeds { get; set; } = true;
        internal bool BlockWrites { get; set; }
        internal ManualResetEventSlim WriteEntered { get; } = new(false);
        internal ManualResetEventSlim ReleaseWrite { get; } = new(false);
        internal L12DeploymentPersistedFence? CurrentFence { get; private set; }

        public L12DeploymentDrainFenceLoadResult Load()
        {
            if (ThrowOnLoad) throw new IOException("unknown fence");
            return LoadResult;
        }

        public bool TryWrite(L12DeploymentPersistedFence fence)
        {
            WriteEntered.Set();
            if (BlockWrites) ReleaseWrite.Wait(TimeSpan.FromSeconds(5));
            if (!WriteSucceeds) return false;
            lock (_gate) CurrentFence = fence;
            return true;
        }

        public bool TryClear(string operationId, string targetCommit)
        {
            if (!ClearSucceeds) return false;
            lock (_gate)
            {
                if (CurrentFence is not null
                    && (CurrentFence.OperationId != operationId
                        || CurrentFence.TargetCommit != targetCommit))
                    return false;
                CurrentFence = null;
                return true;
            }
        }
    }
}
