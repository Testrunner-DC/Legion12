using System.Diagnostics;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class ProcessMetricSamplingTests
{
    [Fact]
    public void SyntheticCgroupV2FilesProduceTypedMemorySwapEventAndPressureMetrics()
    {
        var reader = CompleteLinuxReader(eventsLocal: true);

        var metrics = L12LinuxCgroupV2MetricProbe.Read(true, reader);

        Assert.Equal(L12MetricAvailabilityReason.Available, metrics.MemoryCurrentAvailability);
        Assert.Equal(4096, metrics.MemoryCurrentBytes);
        Assert.Equal(8192, metrics.MemoryMaximum.Bytes);
        Assert.False(metrics.MemoryMaximum.Unlimited);
        Assert.Equal(1024, metrics.SwapCurrentBytes);
        Assert.Null(metrics.SwapMaximum.Bytes);
        Assert.True(metrics.SwapMaximum.Unlimited);
        Assert.Equal(L12CgroupEventScope.Local, metrics.Events.Scope);
        Assert.Equal(new L12CgroupMemoryEvents(1, 2, 3, 4, 5, 6), metrics.Events.Cumulative);
        Assert.Equal(new L12CgroupPressure(1.25, 123, 0.5, 45), metrics.Pressure.Value);
        Assert.DoesNotContain("/team/slice", metrics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingLocalEventsUsesExplicitHierarchicalFallback()
    {
        var reader = CompleteLinuxReader(eventsLocal: false);

        var metrics = L12LinuxCgroupV2MetricProbe.Read(true, reader);

        Assert.Equal(L12MetricAvailabilityReason.Available, metrics.Events.Availability);
        Assert.Equal(L12CgroupEventScope.Hierarchical, metrics.Events.Scope);
        Assert.Equal(5, metrics.Events.Cumulative!.OomKill);
    }

    [Fact]
    public void UnavailableAndMalformedLinuxSourcesStayNullableWithFixedReasons()
    {
        var reader = CompleteLinuxReader(eventsLocal: true);
        reader.Results["/sys/fs/cgroup/team/slice/memory.current"] =
            L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.AccessDenied);
        reader.Results["/sys/fs/cgroup/team/slice/memory.swap.max"] =
            L12MetricTextReadResult.Available("not-a-limit\n");
        reader.Results["/sys/fs/cgroup/team/slice/memory.events.local"] =
            L12MetricTextReadResult.Available("oom_kill -1\n");
        reader.Results["/sys/fs/cgroup/team/slice/memory.pressure"] =
            L12MetricTextReadResult.Available("some avg10=NaN total=5\n");

        var metrics = L12LinuxCgroupV2MetricProbe.Read(true, reader);

        Assert.Null(metrics.MemoryCurrentBytes);
        Assert.Equal(L12MetricAvailabilityReason.AccessDenied, metrics.MemoryCurrentAvailability);
        Assert.Null(metrics.SwapMaximum.Bytes);
        Assert.Null(metrics.SwapMaximum.Unlimited);
        Assert.Equal(L12MetricAvailabilityReason.InvalidData, metrics.SwapMaximum.Availability);
        Assert.Null(metrics.Events.Cumulative);
        Assert.Equal(L12MetricAvailabilityReason.InvalidData, metrics.Events.Availability);
        Assert.Null(metrics.Pressure.Value);
        Assert.Equal(L12MetricAvailabilityReason.InvalidData, metrics.Pressure.Availability);
    }

    [Fact]
    public void NonLinuxProbeReportsNullRatherThanSyntheticZeroes()
    {
        var metrics = L12LinuxCgroupV2MetricProbe.Read(false, new DictionaryReader());

        Assert.Equal(L12MetricAvailabilityReason.NotLinux, metrics.MemoryCurrentAvailability);
        Assert.Null(metrics.MemoryCurrentBytes);
        Assert.Equal(L12MetricAvailabilityReason.NotLinux, metrics.MemoryMaximum.Availability);
        Assert.Null(metrics.MemoryMaximum.Bytes);
        Assert.Null(metrics.SwapCurrentBytes);
        Assert.Null(metrics.Events.Cumulative);
        Assert.Null(metrics.Pressure.Value);
    }

    [Fact]
    public void SamplerUsesInjectedRuntimeAndKeepsTheFifteenStageSnapshotNonDestructive()
    {
        var window = new PerformanceMetricWindow();
        window.Value("websocket.queue-depth", 9);
        var runtime = new L12ProcessRuntimeMetrics(L12MetricAvailabilityReason.Available,
            11, 12, 13, 14.5, 15, 16);
        var probe = new L12ProcessMetricProbe(
            () => new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
            () => runtime,
            () => L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.NotLinux),
            () => window.TrySnapshot(out var batch) ? (true, batch) : (false, null));
        var sampler = new L12ProcessMetricSampler(probe);

        Assert.True(sampler.TryCapture(out var captured));
        var sample = Assert.IsType<L12ProcessMetricSample>(captured);
        Assert.Same(runtime, sample.Runtime);
        Assert.Equal(L12MetricAvailabilityReason.Available, sample.PerformanceWindowAvailability);
        Assert.Equal(15, sample.PerformanceWindow!.Series.Count);
        Assert.Equal(9, Row(sample.PerformanceWindow, "websocket.queue-depth").Total);

        Assert.True(sampler.TryCapture(out var second));
        Assert.Equal(9, Row(second!.PerformanceWindow!, "websocket.queue-depth").Total);
        PerformanceMetricBatch? flushed = null;
        Assert.True(window.TryFlush(batch => flushed = batch));
        Assert.Equal(1, Row(flushed!, "websocket.queue-depth").Count);
    }

    [Fact]
    public void SamplerReportsCumulativeEventDeltasResetAndClockRegression()
    {
        var readings = new Queue<L12LinuxProcessMetrics>(
        [LinuxWithEvents(new(10, 20, 30, 40, 50, 60)),
         LinuxWithEvents(new(11, 22, 33, 44, 55, 66)),
         LinuxWithEvents(new(1, 2, 3, 4, 5, 6))]);
        var times = new Queue<DateTimeOffset>(
        [DateTimeOffset.UnixEpoch.AddSeconds(10), DateTimeOffset.UnixEpoch.AddSeconds(20),
         DateTimeOffset.UnixEpoch.AddSeconds(15)]);
        var sampler = new L12ProcessMetricSampler(new(
            () => times.Dequeue(), L12SystemProcessMetricProbe.Unavailable,
            () => readings.Dequeue(), () => (false, null)));

        Assert.True(sampler.TryCapture(out var first));
        Assert.Null(first!.EventDeltas);
        Assert.Equal(L12CgroupEventDeltaStatus.Baseline, first.EventDeltaStatus);
        Assert.False(first.EventCounterReset);
        Assert.True(sampler.TryCapture(out var second));
        Assert.Equal(new L12CgroupMemoryEvents(1, 2, 3, 4, 5, 6), second!.EventDeltas);
        Assert.Equal(L12CgroupEventDeltaStatus.Available, second.EventDeltaStatus);
        Assert.False(second.EventCounterReset);
        Assert.True(sampler.TryCapture(out var third));
        Assert.Null(third!.EventDeltas);
        Assert.Equal(L12CgroupEventDeltaStatus.CounterReset, third.EventDeltaStatus);
        Assert.True(third.EventCounterReset);
        Assert.Equal(L12MetricAvailabilityReason.ClockRegressed, third.ClockAvailability);
    }

    [Fact]
    public void EventScopeSourceAndMissingIntervalsStartNewBaselinesWithoutFalseReset()
    {
        var unavailable = L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.SourceMissing);
        var readings = new Queue<L12LinuxProcessMetrics>(
        [LinuxWithEvents(new(10, 20, 30, 40, 50, 60), L12CgroupEventScope.Local, "slice-a/local"),
         LinuxWithEvents(new(1, 2, 3, 4, 5, 6), L12CgroupEventScope.Hierarchical, "slice-a/hierarchical"),
         LinuxWithEvents(new(2, 3, 4, 5, 6, 7), L12CgroupEventScope.Hierarchical, "slice-a/hierarchical"),
         LinuxWithEvents(new(1, 1, 1, 1, 1, 1), L12CgroupEventScope.Hierarchical, "slice-b/hierarchical"),
         unavailable,
         LinuxWithEvents(new(3, 3, 3, 3, 3, 3), L12CgroupEventScope.Hierarchical, "slice-b/hierarchical")]);
        var sampler = new L12ProcessMetricSampler(new(
            () => DateTimeOffset.UnixEpoch, L12SystemProcessMetricProbe.Unavailable,
            () => readings.Dequeue(), () => (false, null)));

        Assert.True(sampler.TryCapture(out var first));
        Assert.Equal(L12CgroupEventDeltaStatus.Baseline, first!.EventDeltaStatus);
        Assert.True(sampler.TryCapture(out var changedScope));
        Assert.Equal(L12CgroupEventDeltaStatus.SourceChanged, changedScope!.EventDeltaStatus);
        Assert.False(changedScope.EventCounterReset);
        Assert.Null(changedScope.EventDeltas);
        Assert.True(sampler.TryCapture(out var sameSource));
        Assert.Equal(L12CgroupEventDeltaStatus.Available, sameSource!.EventDeltaStatus);
        Assert.Equal(new L12CgroupMemoryEvents(1, 1, 1, 1, 1, 1), sameSource.EventDeltas);
        Assert.True(sampler.TryCapture(out var changedCgroup));
        Assert.Equal(L12CgroupEventDeltaStatus.SourceChanged, changedCgroup!.EventDeltaStatus);
        Assert.False(changedCgroup.EventCounterReset);
        Assert.Null(changedCgroup.EventDeltas);
        Assert.True(sampler.TryCapture(out var missing));
        Assert.Equal(L12CgroupEventDeltaStatus.Unavailable, missing!.EventDeltaStatus);
        Assert.Null(missing.EventDeltas);
        Assert.True(sampler.TryCapture(out var recovered));
        Assert.Equal(L12CgroupEventDeltaStatus.Baseline, recovered!.EventDeltaStatus);
        Assert.Null(recovered.EventDeltas);
    }

    [Fact]
    public async Task ConcurrentCaptureDropsImmediatelyInsteadOfOverlappingSlowProbe()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var sampler = new L12ProcessMetricSampler(new(
            () => DateTimeOffset.UnixEpoch,
            () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic probe");
                return L12SystemProcessMetricProbe.Unavailable();
            },
            () => L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.NotLinux),
            () => (false, null)));
        var capturing = Task.Factory.StartNew(() => sampler.TryCapture(out _), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var watch = Stopwatch.StartNew();
            Assert.False(sampler.TryCapture(out var skipped));
            Assert.Null(skipped);
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
        }
        finally { release.Set(); Assert.True(await capturing.WaitAsync(TimeSpan.FromSeconds(2))); }
    }

    [Fact]
    public void ProbeFailuresExposeOnlyFixedReasonsNotExceptionMessagesOrPaths()
    {
        const string sensitive = "/secret/account-room-token";
        var sampler = new L12ProcessMetricSampler(new(
            () => throw new IOException(sensitive),
            () => throw new IOException(sensitive),
            () => throw new IOException(sensitive),
            () => throw new IOException(sensitive)));

        Assert.True(sampler.TryCapture(out var captured));
        var sample = Assert.IsType<L12ProcessMetricSample>(captured);
        Assert.Equal(L12MetricAvailabilityReason.ReadFailure, sample.ClockAvailability);
        Assert.Equal(L12MetricAvailabilityReason.ReadFailure, sample.Runtime.Availability);
        Assert.Equal(L12MetricAvailabilityReason.ReadFailure, sample.Linux.MemoryCurrentAvailability);
        Assert.Equal(L12MetricAvailabilityReason.ReadFailure, sample.PerformanceWindowAvailability);
        Assert.DoesNotContain(sensitive, sample.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateEventSourceIdentityIsAbsentFromObjectTextAndJson()
    {
        const string sourceSentinel = "/private/account-room-token/cgroup-slice";
        var cumulative = new L12CgroupMemoryEvents(10, 20, 30, 40, 50, 60);
        var linux = LinuxWithEvents(cumulative, L12CgroupEventScope.Local, sourceSentinel);
        var sampler = new L12ProcessMetricSampler(new(
            () => DateTimeOffset.UnixEpoch, L12SystemProcessMetricProbe.Unavailable,
            () => linux, () => (false, null)));

        Assert.True(sampler.TryCapture(out var captured));
        var sample = Assert.IsType<L12ProcessMetricSample>(captured);
        Assert.Equal(cumulative, sample.Linux.Events.Cumulative);
        var texts = new[]
        {
            linux.ToString(), sample.ToString(), JsonSerializer.Serialize(linux),
            JsonSerializer.Serialize(sample),
            JsonSerializer.Serialize(linux, new JsonSerializerOptions { IncludeFields = true }),
            JsonSerializer.Serialize(sample, new JsonSerializerOptions { IncludeFields = true }),
        };
        Assert.All(texts, text => Assert.DoesNotContain(sourceSentinel, text, StringComparison.Ordinal));
        Assert.All(texts, text => Assert.DoesNotContain("account-room-token", text, StringComparison.Ordinal));
    }

    [Fact]
    public void BoundedReaderAcceptsStrictUtf8ThroughExactLimitAndRejectsOverflowOrInvalidUtf8()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-process-metric-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "synthetic-metric");
        var completed = false;
        try
        {
            var reader = new L12BoundedMetricTextReader();
            File.WriteAllBytes(path, Enumerable.Repeat((byte)'a', 32 * 1024).ToArray());
            var ordinary = reader.Read(path);
            Assert.Equal(L12MetricAvailabilityReason.Available, ordinary.Availability);
            Assert.Equal(32 * 1024, ordinary.Text!.Length);

            File.WriteAllBytes(path, Enumerable.Repeat((byte)'b', L12BoundedMetricTextReader.MaximumBytes).ToArray());
            var exact = reader.Read(path);
            Assert.Equal(L12MetricAvailabilityReason.Available, exact.Availability);
            Assert.Equal(L12BoundedMetricTextReader.MaximumBytes, exact.Text!.Length);

            File.WriteAllBytes(path,
                Enumerable.Repeat((byte)'c', L12BoundedMetricTextReader.MaximumBytes + 1).ToArray());
            var overflow = reader.Read(path);
            Assert.Equal(L12MetricAvailabilityReason.InvalidData, overflow.Availability);
            Assert.Null(overflow.Text);

            File.WriteAllBytes(path, [0xC3, 0x28]);
            var invalidUtf8 = reader.Read(path);
            Assert.Equal(L12MetricAvailabilityReason.InvalidData, invalidUtf8.Availability);
            Assert.Null(invalidUtf8.Text);
            completed = true;
        }
        finally
        {
            // Preserve a failing fixture for diagnosis; successful runs own and remove only this GUID directory.
            if (completed && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RealWindowsProbeDoesNotPresentLinuxMetricsAsZero()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sampler = new L12ProcessMetricSampler(L12ProcessMetricProbe.System());

        Assert.True(sampler.TryCapture(out var captured));
        var linux = captured!.Linux;
        Assert.Equal(L12MetricAvailabilityReason.NotLinux, linux.MemoryCurrentAvailability);
        Assert.Null(linux.MemoryCurrentBytes);
        Assert.Equal(L12MetricAvailabilityReason.NotLinux, linux.SwapCurrentAvailability);
        Assert.Null(linux.SwapCurrentBytes);
        Assert.Null(linux.Events.Cumulative);
        Assert.Null(linux.Pressure.Value);
    }

    [Theory]
    [InlineData("0::/team/../escape\n")]
    [InlineData("1:name=/legacy\n")]
    [InlineData("0::relative\n")]
    public void CgroupResolutionRejectsMissingOrTraversalHierarchy(string cgroup)
    {
        const string mount = "29 23 0:26 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n";
        Assert.False(L12LinuxCgroupV2MetricProbe.TryResolveCgroupDirectory(cgroup, mount, out _));
    }

    private static DictionaryReader CompleteLinuxReader(bool eventsLocal)
    {
        var reader = new DictionaryReader();
        reader.Add("/proc/self/cgroup", "0::/team/slice\n");
        reader.Add("/proc/self/mountinfo",
            "29 23 0:26 / /sys/fs/cgroup rw,nosuid,nodev,noexec,relatime - cgroup2 cgroup rw\n");
        reader.Add("/sys/fs/cgroup/team/slice/memory.current", "4096\n");
        reader.Add("/sys/fs/cgroup/team/slice/memory.max", "8192\n");
        reader.Add("/sys/fs/cgroup/team/slice/memory.swap.current", "1024\n");
        reader.Add("/sys/fs/cgroup/team/slice/memory.swap.max", "max\n");
        reader.Add(eventsLocal ? "/sys/fs/cgroup/team/slice/memory.events.local"
                : "/sys/fs/cgroup/team/slice/memory.events",
            "low 1\nhigh 2\nmax 3\noom 4\noom_kill 5\noom_group_kill 6\n");
        reader.Add("/sys/fs/cgroup/team/slice/memory.pressure",
            "some avg10=1.25 avg60=0.80 avg300=0.20 total=123\n"
            + "full avg10=0.50 avg60=0.30 avg300=0.10 total=45\n");
        return reader;
    }

    private static L12LinuxProcessMetrics LinuxWithEvents(L12CgroupMemoryEvents events,
        L12CgroupEventScope scope = L12CgroupEventScope.Local, string source = "same/local") => new(
        L12MetricAvailabilityReason.SourceMissing, null,
        new(null, null, L12MetricAvailabilityReason.SourceMissing),
        L12MetricAvailabilityReason.SourceMissing, null,
        new(null, null, L12MetricAvailabilityReason.SourceMissing),
        new(L12MetricAvailabilityReason.Available, scope, events),
        new(L12MetricAvailabilityReason.SourceMissing, null), source);

    private static PerformanceMetricSummary Row(PerformanceMetricBatch batch, string stage)
        => Assert.Single(batch.Series.Where(row => row.Stage == stage));

    private sealed class DictionaryReader : IL12BoundedMetricTextReader
    {
        internal Dictionary<string, L12MetricTextReadResult> Results { get; } =
            new(StringComparer.Ordinal);
        internal void Add(string path, string value) => Results.Add(path, L12MetricTextReadResult.Available(value));
        public L12MetricTextReadResult Read(string path) => Results.TryGetValue(path, out var result)
            ? result : L12MetricTextReadResult.Unavailable(L12MetricAvailabilityReason.SourceMissing);
    }
}
