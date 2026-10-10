using System.Collections;
using System.Diagnostics;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PerformanceMetricWindowTests
{
    [Fact]
    public void FixedStagesAndLegacyEntryPointsStayBounded()
    {
        var window = new PerformanceMetricWindow();
        window.Duration("snapshot.build", 50);
        window.Bytes("websocket.full-payload", 1024);
        window.Value("websocket.queue-depth", 32);
        window.Value("duration.action.engine", 5);
        for (var index = 0; index < 10000; index++) window.Value("synthetic-unknown-" + index, 1);
        var batch = Capture(window);
        Assert.Equal(15, batch.Series.Count);
        Assert.Equal(4, batch.Series.Sum(row => row.Count));
        Assert.Equal(10000, batch.Diagnostics.UnknownStages);
        Assert.Equal(50, Row(batch, "duration.snapshot.build").Maximum);
        Assert.Equal(1024, Row(batch, "bytes.websocket.full-payload").Maximum);
        Assert.Equal(32, Row(batch, "websocket.queue-depth").Maximum);
    }

    [Fact]
    public void PercentilesAreExplicitBucketRangesWithOverflowNotCappedSamples()
    {
        var window = new PerformanceMetricWindow();
        for (var index = 0; index < 95; index++) window.Duration("snapshot.build", 50);
        for (var index = 0; index < 4; index++) window.Duration("snapshot.build", 100);
        window.Duration("snapshot.build", 68000);
        var row = Row(Capture(window), "duration.snapshot.build");
        Assert.Equal(100, row.Count);
        Assert.Equal(73150, row.Total);
        Assert.Equal(68000, row.Maximum);
        Assert.Equal(1, row.OverflowCount);
        Assert.Equal(new PerformanceMetricBand(25, 50, false), row.P95);
        Assert.Equal(new PerformanceMetricBand(50, 100, false), row.P99);
        window.Duration("snapshot.build", 68000);
        row = Row(Capture(window), "duration.snapshot.build");
        Assert.Equal(new PerformanceMetricBand(60000, null, true), row.P95);
        Assert.Equal(row.P95, row.P99);
    }

    [Fact]
    public void ByteAndValueBucketUpperEdgesAndOverflowRemainExplicit()
    {
        var window = new PerformanceMetricWindow();
        window.Bytes("websocket.full-payload", 268435456);
        window.Value("websocket.queue-depth", 64);
        var first = Capture(window);
        Assert.Equal(new PerformanceMetricBand(67108864, 268435456, false),
            Row(first, "bytes.websocket.full-payload").P99);
        Assert.Equal(new PerformanceMetricBand(32, 64, false), Row(first, "websocket.queue-depth").P99);
        window.Bytes("websocket.full-payload", 268435457);
        window.Value("websocket.queue-depth", 65);
        var second = Capture(window);
        Assert.Equal(new PerformanceMetricBand(268435456, null, true),
            Row(second, "bytes.websocket.full-payload").P95);
        Assert.Equal(new PerformanceMetricBand(64, null, true), Row(second, "websocket.queue-depth").P99);
        Assert.Equal(1, Row(second, "bytes.websocket.full-payload").OverflowCount);
        Assert.Equal(1, Row(second, "websocket.queue-depth").OverflowCount);
    }

    [Fact]
    public void FlushResetsAcceptedWindowButKeepsDiagnosticsAndDetachedImmutableSummary()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 2);
        window.Value("unregistered", 1);
        var first = Capture(window);
        window.Value("websocket.queue-depth", 4);
        var second = Capture(window);
        var empty = Row(Capture(window), "websocket.queue-depth");
        Assert.Equal(2, Row(first, "websocket.queue-depth").Total);
        Assert.Equal(4, Row(second, "websocket.queue-depth").Total);
        Assert.Equal(1, second.Diagnostics.UnknownStages);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.P95);
        Assert.Null(empty.P99);
        Assert.Equal(1, Row(first, "websocket.queue-depth").Histogram.Sum());
    }

    [Fact]
    public void InvalidNumericInputsAreRejectedAndFiniteNegativesKeepLegacyClamp()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", double.NaN);
        window.Value("websocket.queue-depth", double.PositiveInfinity);
        window.Value("websocket.queue-depth", double.NegativeInfinity);
        window.Value("websocket.queue-depth", double.MaxValue);
        window.Bytes("websocket.full-payload", long.MaxValue);
        window.Value(null!, 1);
        window.Value("websocket.queue-depth", -1);
        window.Bytes("websocket.full-payload", -1);
        var batch = Capture(window);
        Assert.Equal(5, batch.Diagnostics.InvalidSamples);
        Assert.Equal(1, batch.Diagnostics.UnknownStages);
        Assert.Equal(2, batch.Diagnostics.ClampedNegatives);
        Assert.Equal(2, batch.Series.Sum(row => row.Count));
        Assert.All(batch.Series, row => Assert.Equal(0, row.Total));
        Assert.Equal(new PerformanceMetricBand(0, 0, false, true), Row(batch, "websocket.queue-depth").P95);
    }

    [Fact]
    public void SaturatedTotalIsReportedRatherThanWrappingNegative()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 8e15);
        window.Value("websocket.queue-depth", 8e15);
        var row = Row(Capture(window), "websocket.queue-depth");
        Assert.True(row.TotalSaturated);
        Assert.Equal(2, row.Count);
        Assert.True(row.Total > 0);
        Assert.Equal(8e15, row.Maximum);
    }

    [Fact]
    public async Task ContendedStageDropsWithoutWaitingAndReportsItsLoss()
    {
        var window = new PerformanceMetricWindow();
        var series = ((IEnumerable)typeof(PerformanceMetricWindow).GetField("_series",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Cast<object>().Single(item =>
                (string)item.GetType().GetField("Stage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item)!
                    == "websocket.queue-depth");
        var gate = series.GetType().GetField("Gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(series)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Factory.StartNew(() =>
        {
            lock (gate)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic metric gate");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var record = Task.Factory.StartNew(() =>
            {
                var watch = Stopwatch.StartNew();
                window.Value("websocket.queue-depth", 1);
                return watch.Elapsed;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(await record.WaitAsync(TimeSpan.FromSeconds(1)) < TimeSpan.FromMilliseconds(250));
        }
        finally { release.Set(); await holder.WaitAsync(TimeSpan.FromSeconds(2)); }
        var row = Row(Capture(window), "websocket.queue-depth");
        Assert.Equal(0, row.Count);
        Assert.Equal(1, row.DroppedContentionTotal);
    }

    [Fact]
    public async Task SlowSinkNeverRunsOnRecorderAndConcurrentFlushIsSkipped()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var sinkThread = 0;
        var flushing = Task.Factory.StartNew(() => window.TryFlush(_ =>
        {
            sinkThread = Environment.CurrentManagedThreadId;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic slow sink");
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.NotEqual(Environment.CurrentManagedThreadId, sinkThread);
            var recording = Task.Factory.StartNew(() =>
            {
                for (var index = 0; index < 1000; index++) window.Value("websocket.queue-depth", 2);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await recording.WaitAsync(TimeSpan.FromSeconds(1));
            var invoked = false;
            Assert.False(window.TryFlush(_ => invoked = true));
            Assert.False(invoked);
        }
        finally { release.Set(); Assert.True(await flushing.WaitAsync(TimeSpan.FromSeconds(2))); }
        var batch = Capture(window);
        Assert.Equal(1000, Row(batch, "websocket.queue-depth").Count);
        Assert.Equal(1, batch.Diagnostics.SkippedFlushes);
    }

    [Fact]
    public void ThrowingSinkLosesOnlyCapturedWindowAndNextFlushStillWorks()
    {
        var window = new PerformanceMetricWindow();
        for (var index = 0; index < 7; index++) window.Value("websocket.queue-depth", 3);
        Assert.False(window.TryFlush(_ => throw new IOException("synthetic sink")));
        window.Value("websocket.queue-depth", 4);
        var batch = Capture(window);
        Assert.Equal(1, Row(batch, "websocket.queue-depth").Count);
        Assert.Equal(1, batch.Diagnostics.SinkFailures);
        Assert.Equal(7, batch.Diagnostics.LostSinkSamples);
    }

    [Fact]
    public async Task ConcurrentRecordingAndFlushKeepEveryAcceptedHistogramCompositionCoherent()
    {
        var window = new PerformanceMetricWindow();
        const int writers = 4, perWriter = 10000;
        var accepted = 0L;
        var writing = Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
        {
            for (var index = 0; index < perWriter; index++) window.Value("websocket.queue-depth", 2);
        })).ToArray();
        for (var index = 0; index < 100; index++)
        {
            var row = Row(Capture(window), "websocket.queue-depth");
            Assert.Equal(row.Count, row.Histogram.Sum());
            Assert.Equal(row.Count * 2, row.Total);
            accepted += row.Count;
            await Task.Yield();
        }
        await Task.WhenAll(writing);
        var final = Row(Capture(window), "websocket.queue-depth");
        Assert.Equal(final.Count, final.Histogram.Sum());
        accepted += final.Count;
        Assert.Equal(writers * perWriter, accepted + final.DroppedContentionTotal);
    }

    [Fact]
    public void ReentrantSinkRecordsOnlyNextWindowAndCannotRecursivelyOutput()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 1);
        var recursiveSinkCalled = false;
        var nestedFlushSucceeded = true;
        PerformanceMetricBatch? first = null;
        var firstSucceeded = window.TryFlush(batch =>
        {
            first = batch;
            window.Value("websocket.queue-depth", 2);
            nestedFlushSucceeded = window.TryFlush(_ => recursiveSinkCalled = true);
        });
        Assert.True(firstSucceeded);
        Assert.False(nestedFlushSucceeded);
        Assert.False(recursiveSinkCalled);
        Assert.Equal(1, Row(Assert.IsType<PerformanceMetricBatch>(first), "websocket.queue-depth").Total);
        var second = Capture(window);
        Assert.Equal(2, Row(second, "websocket.queue-depth").Total);
        Assert.Equal(1, second.Diagnostics.SkippedFlushes);
    }

    [Fact]
    public void MetricKindsAndOrdinalNamesCannotAliasAnotherFixedSeries()
    {
        var window = new PerformanceMetricWindow();
        window.Duration("SNAPSHOT.BUILD", 1);
        window.Bytes("snapshot.build", 1);
        window.Duration("duration.snapshot.build", 1);
        window.Value("snapshot.build", 1);
        var batch = Capture(window);
        Assert.Equal(4, batch.Diagnostics.UnknownStages);
        Assert.Equal(0, batch.Series.Sum(row => row.Count));
        Assert.Equal(15, batch.Series.Count);
    }

    [Fact]
    public void SnapshotKeepsAllFixedStagesAndDoesNotResetTheMinuteWindow()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 7);

        Assert.True(window.TrySnapshot(out var first));
        Assert.True(window.TrySnapshot(out var second));
        Assert.Equal(15, Assert.IsType<PerformanceMetricBatch>(first).Series.Count);
        Assert.Equal(7, Row(first!, "websocket.queue-depth").Total);
        Assert.Equal(7, Row(Assert.IsType<PerformanceMetricBatch>(second), "websocket.queue-depth").Total);

        var flushed = Capture(window);
        Assert.Equal(1, Row(flushed, "websocket.queue-depth").Count);
        Assert.True(window.TrySnapshot(out var afterFlush));
        Assert.Equal(0, Row(Assert.IsType<PerformanceMetricBatch>(afterFlush), "websocket.queue-depth").Count);
    }

    [Fact]
    public async Task SnapshotReturnsUnavailableWithoutWaitingForAnyBusyStage()
    {
        var window = new PerformanceMetricWindow();
        var series = ((IEnumerable)typeof(PerformanceMetricWindow).GetField("_series",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Cast<object>().Single(item =>
                (string)item.GetType().GetField("Stage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item)!
                    == "websocket.queue-depth");
        var gate = series.GetType().GetField("Gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(series)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Factory.StartNew(() =>
        {
            lock (gate)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic metric gate");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var watch = Stopwatch.StartNew();
            Assert.False(window.TrySnapshot(out var snapshot));
            Assert.Null(snapshot);
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
        }
        finally { release.Set(); await holder.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    [Fact]
    public async Task SnapshotNeverPublishesAWindowWhileDestructiveFlushEpochIsOpen()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var flushing = Task.Factory.StartNew(() => window.TryFlush(_ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic slow sink");
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(window.TrySnapshot(out var duringFlush));
            Assert.Null(duringFlush);
            window.Value("websocket.queue-depth", 2);
        }
        finally { release.Set(); Assert.True(await flushing.WaitAsync(TimeSpan.FromSeconds(2))); }

        Assert.True(window.TrySnapshot(out var nextWindow));
        Assert.Equal(2, Row(Assert.IsType<PerformanceMetricBatch>(nextWindow),
            "websocket.queue-depth").Total);
    }

    private static PerformanceMetricBatch Capture(PerformanceMetricWindow window)
    {
        PerformanceMetricBatch? batch = null;
        Assert.True(window.TryFlush(value => batch = value));
        return Assert.IsType<PerformanceMetricBatch>(batch);
    }
    private static PerformanceMetricSummary Row(PerformanceMetricBatch batch, string stage)
        => Assert.Single(batch.Series.Where(row => row.Stage == stage));
}
