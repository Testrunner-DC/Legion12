using System.Diagnostics;

namespace TwelveLegions.Server;

internal enum L12PlatformOperationKind
{
    AdminTransaction,
    TransactionalSave,
    SeasonFinalization,
    Recovery,
}

internal enum L12PlatformOperationPhase
{
    SnapshotSerialize,
    MirrorSerialize,
    RollbackGzip,
    Hydration,
    CommittedIndexPrepare,
}

internal readonly record struct L12PlatformMetricSample(
    bool Available,
    double DurationMilliseconds,
    bool AllocationAvailable,
    long AllocatedBytes);

/// <summary>
/// Typed, allocation-free facade over the existing bounded performance window.
/// Allocation deltas are valid only while synchronous work remains on the starting thread.
/// Operation totals are inclusive and can overlap nested recovery; they must not be summed as net allocation.
/// </summary>
internal static class L12PlatformOperationMetrics
{
    internal const string LockWaitStage = "platform.lock-wait";
    internal const string LockHoldStage = "platform.lock-hold";

    internal const string AdminTransactionStage = "platform.operation.admin-transaction";
    internal const string TransactionalSaveStage = "platform.operation.transactional-save";
    internal const string SeasonFinalizationStage = "platform.operation.season-finalization";
    internal const string RecoveryStage = "platform.operation.recovery";

    internal const string SnapshotSerializeStage = "platform.phase.snapshot-serialize";
    internal const string MirrorSerializeStage = "platform.phase.mirror-serialize";
    internal const string RollbackGzipStage = "platform.phase.rollback-gzip";
    internal const string HydrationStage = "platform.phase.hydration";
    internal const string CommittedIndexPrepareStage = "platform.phase.index-prepare";

    internal const string AdminTransactionFailureStage = "platform.failure.admin-transaction";
    internal const string TransactionalSaveFailureStage = "platform.failure.transactional-save";
    internal const string SeasonFinalizationFailureStage = "platform.failure.season-finalization";
    internal const string RecoveryFailureStage = "platform.failure.recovery";

    internal static L12PlatformMetricMeasurement Begin() => L12PlatformMetricMeasurement.Start();

    internal static L12PlatformPhaseScope Measure(L12PlatformOperationPhase phase)
        => new(Begin(), phase);

    internal static void RecordOperation(L12PlatformMetricSample sample, L12PlatformOperationKind operation,
        bool failed)
    {
        try
        {
            if (!sample.Available) return;
            var stage = OperationStage(operation);
            L12PerformanceMetrics.Duration(stage, sample.DurationMilliseconds);
            if (sample.AllocationAvailable) L12PerformanceMetrics.Bytes(stage, sample.AllocatedBytes);
            if (failed) L12PerformanceMetrics.Value(FailureStage(operation), 1);
        }
        catch { }
    }

    internal static void RecordPhase(L12PlatformMetricSample sample, L12PlatformOperationPhase phase)
    {
        try
        {
            if (!sample.Available) return;
            var stage = PhaseStage(phase);
            L12PerformanceMetrics.Duration(stage, sample.DurationMilliseconds);
            if (sample.AllocationAvailable) L12PerformanceMetrics.Bytes(stage, sample.AllocatedBytes);
        }
        catch { }
    }

    internal static void RecordLockWait(L12PlatformMetricSample sample)
        => RecordDurationOnly(sample, LockWaitStage);

    internal static void RecordLockHold(L12PlatformMetricSample sample)
        => RecordDurationOnly(sample, LockHoldStage);

    private static void RecordDurationOnly(L12PlatformMetricSample sample, string stage)
    {
        try
        {
            if (sample.Available) L12PerformanceMetrics.Duration(stage, sample.DurationMilliseconds);
        }
        catch { }
    }

    internal static string OperationStage(L12PlatformOperationKind operation) => operation switch
    {
        L12PlatformOperationKind.AdminTransaction => AdminTransactionStage,
        L12PlatformOperationKind.TransactionalSave => TransactionalSaveStage,
        L12PlatformOperationKind.SeasonFinalization => SeasonFinalizationStage,
        L12PlatformOperationKind.Recovery => RecoveryStage,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static string FailureStage(L12PlatformOperationKind operation) => operation switch
    {
        L12PlatformOperationKind.AdminTransaction => AdminTransactionFailureStage,
        L12PlatformOperationKind.TransactionalSave => TransactionalSaveFailureStage,
        L12PlatformOperationKind.SeasonFinalization => SeasonFinalizationFailureStage,
        L12PlatformOperationKind.Recovery => RecoveryFailureStage,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static string PhaseStage(L12PlatformOperationPhase phase) => phase switch
    {
        L12PlatformOperationPhase.SnapshotSerialize => SnapshotSerializeStage,
        L12PlatformOperationPhase.MirrorSerialize => MirrorSerializeStage,
        L12PlatformOperationPhase.RollbackGzip => RollbackGzipStage,
        L12PlatformOperationPhase.Hydration => HydrationStage,
        L12PlatformOperationPhase.CommittedIndexPrepare => CommittedIndexPrepareStage,
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };
}

internal readonly struct L12PlatformMetricMeasurement
{
    private readonly bool _available;
    private readonly long _startedAt;
    private readonly long _allocatedAtStart;
    private readonly int _threadId;

    private L12PlatformMetricMeasurement(bool available, long startedAt, long allocatedAtStart, int threadId)
        => (_available, _startedAt, _allocatedAtStart, _threadId) =
            (available, startedAt, allocatedAtStart, threadId);

    internal static L12PlatformMetricMeasurement Start()
    {
        try
        {
            // Force all registry/timer construction before a business commit can occur.
            L12PerformanceMetrics.EnsureInitialized();
            return new(true, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(),
                Environment.CurrentManagedThreadId);
        }
        catch { return default; }
    }

    internal L12PlatformMetricSample Stop()
    {
        try
        {
            if (!_available) return default;
            var stoppedAt = Stopwatch.GetTimestamp();
            var duration = Stopwatch.GetElapsedTime(_startedAt, stoppedAt).TotalMilliseconds;
            if (Environment.CurrentManagedThreadId != _threadId)
                return new(true, duration, false, 0);
            var allocatedAtEnd = GC.GetAllocatedBytesForCurrentThread();
            return allocatedAtEnd >= _allocatedAtStart
                ? new(true, duration, true, allocatedAtEnd - _allocatedAtStart)
                : new(true, duration, false, 0);
        }
        catch { return default; }
    }
}

internal readonly struct L12PlatformPhaseScope : IDisposable
{
    private readonly L12PlatformMetricMeasurement _measurement;
    private readonly L12PlatformOperationPhase _phase;

    internal L12PlatformPhaseScope(L12PlatformMetricMeasurement measurement, L12PlatformOperationPhase phase)
        => (_measurement, _phase) = (measurement, phase);

    public void Dispose()
    {
        try { L12PlatformOperationMetrics.RecordPhase(_measurement.Stop(), _phase); }
        catch { }
    }
}
