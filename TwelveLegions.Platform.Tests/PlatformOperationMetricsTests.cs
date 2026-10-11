using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PlatformOperationMetricsTests
{
    [Fact]
    public void PlatformCatalogIsFixedTypedAndContainsNoDynamicLabels()
    {
        Assert.Equal("platform.operation.admin-transaction",
            L12PlatformOperationMetrics.OperationStage(L12PlatformOperationKind.AdminTransaction));
        Assert.Equal("platform.operation.transactional-save",
            L12PlatformOperationMetrics.OperationStage(L12PlatformOperationKind.TransactionalSave));
        Assert.Equal("platform.operation.season-finalization",
            L12PlatformOperationMetrics.OperationStage(L12PlatformOperationKind.SeasonFinalization));
        Assert.Equal("platform.operation.recovery",
            L12PlatformOperationMetrics.OperationStage(L12PlatformOperationKind.Recovery));

        Assert.Equal("platform.phase.snapshot-serialize",
            L12PlatformOperationMetrics.PhaseStage(L12PlatformOperationPhase.SnapshotSerialize));
        Assert.Equal("platform.phase.mirror-serialize",
            L12PlatformOperationMetrics.PhaseStage(L12PlatformOperationPhase.MirrorSerialize));
        Assert.Equal("platform.phase.rollback-gzip",
            L12PlatformOperationMetrics.PhaseStage(L12PlatformOperationPhase.RollbackGzip));
        Assert.Equal("platform.phase.hydration",
            L12PlatformOperationMetrics.PhaseStage(L12PlatformOperationPhase.Hydration));
        Assert.Equal("platform.phase.index-prepare",
            L12PlatformOperationMetrics.PhaseStage(L12PlatformOperationPhase.CommittedIndexPrepare));

        Assert.Equal(39, PerformanceMetricWindow.FixedStageCount);
        Assert.Equal(39, PerformanceMetricWindow.FixedStageNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(PerformanceMetricWindow.FixedStageNames, stage =>
            stage.Contains('{') || stage.Contains('/') || stage.Contains('\\'));
    }

    [Fact]
    public void SynchronousMeasurementReportsThreadAllocationWithoutClaimingNetAllocation()
    {
        var measurement = L12PlatformOperationMetrics.Begin();
        var payload = GC.AllocateUninitializedArray<byte>(64 * 1024);
        var sample = measurement.Stop();
        GC.KeepAlive(payload);

        Assert.True(sample.Available);
        Assert.True(sample.AllocationAvailable);
        Assert.True(sample.DurationMilliseconds >= 0);
        Assert.True(sample.AllocatedBytes >= payload.Length);
    }

    [Fact]
    public async Task CrossThreadStopKeepsDurationButRejectsAllocationDelta()
    {
        var measurement = L12PlatformOperationMetrics.Begin();
        var sample = await Task.Factory.StartNew(measurement.Stop, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert.True(sample.Available);
        Assert.True(sample.DurationMilliseconds >= 0);
        Assert.False(sample.AllocationAvailable);
        Assert.Equal(0, sample.AllocatedBytes);
    }

    [Fact]
    public void InvalidTypedLabelsAndScopeDisposalNeverEscapeIntoBusinessCode()
    {
        var sample = new L12PlatformMetricSample(true, 1, true, 1);
        Assert.Null(Record.Exception(() => L12PlatformOperationMetrics.RecordOperation(sample,
            (L12PlatformOperationKind)int.MaxValue, failed: true)));
        Assert.Null(Record.Exception(() => L12PlatformOperationMetrics.RecordPhase(sample,
            (L12PlatformOperationPhase)int.MaxValue)));

        var scope = new L12PlatformPhaseScope(L12PlatformOperationMetrics.Begin(),
            (L12PlatformOperationPhase)int.MaxValue);
        Assert.Null(Record.Exception(scope.Dispose));
    }
}
