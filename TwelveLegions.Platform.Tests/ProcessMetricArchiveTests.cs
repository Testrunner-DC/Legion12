using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class ProcessMetricArchiveTests
{
    private const string Commit = "abc123";
    private const string MarkerText = "l12-process-metrics-owned-v1\n";
    private static readonly Guid InstanceA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public void CompactEntryContainsFifteenStagesButNoHistogramOrPrivateSource()
    {
        const string sourceSentinel = "/private/account-room-token/cgroup";
        var sample = Sample(sourceSentinel);
        Assert.True(L12CompactProcessMetricEncoder.TryEncode(sample,
            new(Commit, InstanceA.ToString("N")), sample.ObservedAt, 1000, 1000,
            out var serialized, out var reason));
        Assert.Equal(L12ProcessMetricArchiveReason.None, reason);
        Assert.InRange(serialized!.Json.Length + 1, 1, L12ProcessMetricCollector.MaximumLineBytes);
        var text = Encoding.UTF8.GetString(serialized.Json);
        Assert.DoesNotContain("Histogram", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sourceSentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain("account-room-token", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(serialized.Json);
        Assert.Equal(15, json.RootElement.GetProperty("stages").GetArrayLength());
        Assert.Contains("duration.action.engine", json.RootElement.GetProperty("stages")
            .EnumerateArray().Select(row => row.GetProperty("stage").GetString()));
        Assert.Equal(50, json.RootElement.GetProperty("linux").GetProperty("eventsTotal")
            .GetProperty("OomKill").GetInt64());
    }

    [Fact]
    public void InvalidIdentityAndNonFixedStageSetAreRejectedBeforeQueueing()
    {
        var sample = Sample("private");
        Assert.False(L12CompactProcessMetricEncoder.TryEncode(sample,
            new("bad/path", "not-a-guid"), sample.ObservedAt, 1, 1,
            out var invalid, out var invalidReason));
        Assert.Null(invalid);
        Assert.Equal(L12ProcessMetricArchiveReason.InvalidMetadata, invalidReason);
        var stages = Enumerable.Range(0, 15).Select(index => new PerformanceMetricSummary(
            "duration.synthetic-" + index, "ms", sample.ObservedAt, sample.ObservedAt,
            1, 1, 1, false, Array.AsReadOnly(new long[21]), null, null, 0, 0)).ToArray();
        var nonFixed = sample with
        {
            PerformanceWindow = new PerformanceMetricBatch(stages,
                new PerformanceMetricDiagnostics(0, 0, 0, 0, 0, 0))
        };
        Assert.False(L12CompactProcessMetricEncoder.TryEncode(nonFixed,
            new(Commit, InstanceA.ToString("N")), sample.ObservedAt, 1, 1,
            out var rejected, out var nonFixedReason));
        Assert.Null(rejected);
        Assert.Equal(L12ProcessMetricArchiveReason.InvalidSample, nonFixedReason);
    }

    [Fact]
    public void NonFiniteMetricIsRejectedByTheExistingJsonEncoder()
    {
        var sample = Sample("private") with
        {
            Runtime = Runtime() with { GcPauseTotalMilliseconds = double.NaN }
        };
        Assert.False(L12CompactProcessMetricEncoder.TryEncode(sample,
            new(Commit, InstanceA.ToString("N")), sample.ObservedAt, 1, 1,
            out var rejected, out var reason));
        Assert.Null(rejected);
        Assert.Equal(L12ProcessMetricArchiveReason.InvalidSample, reason);
    }

    [Fact]
    public async Task SchedulerRequestsTheFixedTenSecondInterval()
    {
        var delayObserved = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = Collector(new ImmediateSink(), delay: (duration, _, token) =>
        {
            delayObserved.TrySetResult(duration);
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        try
        {
            Assert.True(collector.Start());
            Assert.Equal(TimeSpan.FromSeconds(10),
                await delayObserved.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2))); }
    }

    [Fact]
    public async Task SlowCaptureDoesNotOverlapAndReportsOneFixedDrop()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var time = new ManualTimeProvider(UtcDay());
        var probe = Probe(time, runtime: () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic capture");
            return Runtime();
        });
        var collector = new L12ProcessMetricCollector(time, probe, new ImmediateSink(),
            new(Commit, InstanceA.ToString("N")), delay: NeverDelay);
        Assert.True(collector.Start());
        var first = Task.Factory.StartNew(collector.TryCollectNow, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var watch = Stopwatch.StartNew();
            Assert.False(collector.TryCollectNow());
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
            Assert.Equal(1, collector.Diagnostics().CaptureBusyDrops);
        }
        finally
        {
            release.Set();
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task SlowWriterHasExactlyTwelveQueuedSlotsThenDropsWithoutWaiting()
    {
        var sink = new BlockingSink();
        var collector = Collector(sink);
        Assert.True(collector.Start());
        try
        {
            Assert.True(collector.TryCollectNow());
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.All(Enumerable.Range(0, L12ProcessMetricCollector.QueueCapacity),
                _ => Assert.True(collector.TryCollectNow()));
            var watch = Stopwatch.StartNew();
            Assert.False(collector.TryCollectNow());
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
            Assert.Equal(1, collector.Diagnostics().QueueDrops);
        }
        finally
        {
            sink.Release.TrySetResult();
            Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task BlockedSinkMakesShutdownBoundedAndExplicitlyIncomplete()
    {
        var sink = new BlockingSink();
        var collector = Collector(sink, shutdownTimeout: TimeSpan.FromMilliseconds(100));
        Assert.True(collector.Start());
        Assert.True(collector.TryCollectNow());
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var watch = Stopwatch.StartNew();
        Assert.False(await collector.StopAsync(TimeSpan.FromMilliseconds(100)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(1, collector.Diagnostics().ShutdownTimeouts);
        sink.Release.TrySetResult();
        Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task BlockingCancellationCallbackCannotBlockTheStopCaller()
    {
        var sink = new CancellationCallbackSink();
        var collector = Collector(sink, shutdownTimeout: TimeSpan.FromMilliseconds(100));
        Assert.True(collector.Start());
        Assert.True(collector.TryCollectNow());
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = Task.Run(() => collector.StopAsync(TimeSpan.FromMilliseconds(100)));
        try
        {
            Assert.True(sink.CallbackEntered.Wait(TimeSpan.FromSeconds(1)));
            Assert.False(await stop.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, collector.Diagnostics().ShutdownTimeouts);
        }
        finally
        {
            sink.Release.Set();
            Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task SlowAlertRunsOnlyOnWriterAndCannotBlockCollectOrBoundedStop()
    {
        using var alertEntered = new ManualResetEventSlim();
        using var releaseAlert = new ManualResetEventSlim();
        var collector = Collector(new FailingSink(), shutdownTimeout: TimeSpan.FromMilliseconds(100),
            alert: _ =>
            {
                alertEntered.Set();
                if (!releaseAlert.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic alert");
            });
        Assert.True(collector.Start());
        Assert.True(collector.TryCollectNow());
        Assert.True(alertEntered.Wait(TimeSpan.FromSeconds(2)));
        var collectWatch = Stopwatch.StartNew();
        Assert.True(collector.TryCollectNow());
        Assert.True(collectWatch.Elapsed < TimeSpan.FromMilliseconds(250));
        var stopWatch = Stopwatch.StartNew();
        Assert.False(await collector.StopAsync(TimeSpan.FromMilliseconds(100)));
        Assert.True(stopWatch.Elapsed < TimeSpan.FromSeconds(1));
        releaseAlert.Set();
        Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task AlertsAreAggregatedOnTheWriterAtNoMoreThanOncePerMinute()
    {
        var time = new ManualTimeProvider(UtcDay());
        var alerts = 0;
        var sink = new FailingSink();
        var collector = new L12ProcessMetricCollector(time, Probe(time), sink,
            new(Commit, InstanceA.ToString("N")), delay: NeverDelay,
            alert: _ => Interlocked.Increment(ref alerts));
        Assert.True(collector.Start());
        try
        {
            Assert.True(collector.TryCollectNow());
            await WaitUntil(() => sink.CallCount >= 1 && Volatile.Read(ref alerts) == 1);
            Assert.True(collector.TryCollectNow());
            await WaitUntil(() => sink.CallCount >= 2);
            Assert.Equal(1, Volatile.Read(ref alerts));
            time.Advance(TimeSpan.FromSeconds(61));
            Assert.True(collector.TryCollectNow());
            await WaitUntil(() => sink.CallCount >= 3 && Volatile.Read(ref alerts) == 2);
        }
        finally { Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2))); }
    }

    [Fact]
    public async Task SinkExceptionIsFixedReasonPrivateAndNeverRetried()
    {
        const string exceptionSentinel = "/private/account-room-token/archive";
        var sink = new ThrowingSink(exceptionSentinel);
        var lastAlert = 0;
        var collector = Collector(sink,
            alert: reason => Volatile.Write(ref lastAlert, (int)reason));
        Assert.True(collector.Start());
        try
        {
            Assert.True(collector.TryCollectNow());
            await WaitUntil(() => collector.Diagnostics().ArchiveDrops == 1);
            await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref sink.CallCount));
            Assert.Equal(L12ProcessMetricArchiveReason.WriteFailure, collector.Diagnostics().LastFailure);
            Assert.Equal((int)L12ProcessMetricArchiveReason.WriteFailure, Volatile.Read(ref lastAlert));
            Assert.DoesNotContain(exceptionSentinel, collector.Diagnostics().ToString(), StringComparison.Ordinal);
        }
        finally { Assert.True(await collector.StopAsync(TimeSpan.FromSeconds(2))); }
    }

    [Fact]
    public async Task EmptyOwnedRootCreatesExactMarkersAndFreshGuidSegment()
    {
        using var root = new TestRoot();
        await using var archive = Archive(root, InstanceA);
        var day = UtcDay();
        Assert.True((await archive.WriteAsync(Entry(day, 1000, InstanceA), default)).Succeeded);
        await archive.DisposeAsync();
        Assert.Equal(MarkerText, File.ReadAllText(System.IO.Path.Combine(root.Path,
            L12ProcessMetricFileArchive.OwnershipMarkerName)));
        Assert.Equal(MarkerText, File.ReadAllText(System.IO.Path.Combine(root.Day(day),
            L12ProcessMetricFileArchive.OwnershipMarkerName)));
        var segment = System.IO.Path.Combine(root.Day(day), InstanceA.ToString("N") + ".jsonl");
        Assert.True(File.Exists(segment));
        Assert.EndsWith("\n", File.ReadAllText(segment), StringComparison.Ordinal);
        root.Success();
    }

    [Fact]
    public void DayNameParsingAlwaysProducesUtcMidnightIncludingOnPositiveOffsetHosts()
    {
        Assert.True(L12ProcessMetricFileArchive.TryParseDay("2026-10-07", out var parsed));
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.Equal(new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(parsed, new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.FromHours(8)).UtcDateTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrWrongRootMarkerInNonEmptyRootFailsClosed(bool wrongMarker)
    {
        using var root = new TestRoot();
        root.WriteRoot("incident-evidence.txt", Encoding.UTF8.GetBytes("keep"));
        if (wrongMarker) root.WriteRoot(L12ProcessMetricFileArchive.OwnershipMarkerName,
            Encoding.UTF8.GetBytes("wrong-marker\n"));
        await using var archive = Archive(root, InstanceA);
        var result = await archive.WriteAsync(Entry(UtcDay(), 1000, InstanceA), default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.UnsafeRoot, result.Reason);
        Assert.True(File.Exists(System.IO.Path.Combine(root.Path, "incident-evidence.txt")));
        Assert.False(File.Exists(System.IO.Path.Combine(root.Path,
            L12ProcessMetricFileArchive.LockFileName)));
        root.Success();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrWrongDayMarkerInNonEmptyDayFailsClosed(bool wrongMarker)
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        Directory.CreateDirectory(root.Day(day));
        root.WriteDay(day, "failure-evidence.txt", Encoding.UTF8.GetBytes("keep"));
        if (wrongMarker) root.WriteDay(day, L12ProcessMetricFileArchive.OwnershipMarkerName,
            Encoding.UTF8.GetBytes("wrong-marker\n"));
        await using var archive = Archive(root, InstanceA);
        var result = await archive.WriteAsync(Entry(day, 1000, InstanceA), default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.UnsafeRoot, result.Reason);
        Assert.True(File.Exists(System.IO.Path.Combine(root.Day(day), "failure-evidence.txt")));
        root.Success();
    }

    [Fact]
    public async Task RetentionDeletesOnlyCanonicalOwnedDaysAndProtectsAllOtherEvidence()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        for (var index = 0; index <= 7; index++) root.WriteOwnedDay(day.AddDays(-index), InstanceB);
        var validExpired = root.WriteOwnedDay(day.AddDays(-8), InstanceB);
        var pinned = root.WriteOwnedDay(day.AddDays(-9), InstanceB, pinned: true);
        var badJson = root.WriteOwnedDay(day.AddDays(-10), InstanceB, raw: Encoding.UTF8.GetBytes("{bad}\n"));
        var unknown = root.WriteOwnedDay(day.AddDays(-11), InstanceB, unknownFile: true);
        var incomplete = root.WriteOwnedDay(day.AddDays(-12), InstanceB,
            raw: Entry(day.AddDays(-12), 1, InstanceB).Json);
        var foreignInstance = root.WriteOwnedDay(day.AddDays(-13), InstanceB,
            raw: Line(Entry(day.AddDays(-13), 1, InstanceA)));
        var firstCommit = Line(Entry(day.AddDays(-14), 1, InstanceB, "commit-a"));
        var secondCommit = Line(Entry(day.AddDays(-14).AddSeconds(10), 11000, InstanceB, "commit-b"));
        var mixedCommit = root.WriteOwnedDay(day.AddDays(-14), InstanceB,
            raw: firstCommit.Concat(secondCommit).ToArray());
        var canonical = Encoding.UTF8.GetString(Entry(day.AddDays(-15), 1, InstanceB).Json);
        var nonWhitelisted = root.WriteOwnedDay(day.AddDays(-15), InstanceB,
            raw: Encoding.UTF8.GetBytes(canonical[..^1] + ",\"privateAccount\":\"sentinel\"}\n"));
        var wrongArchiveDay = root.WriteOwnedDay(day.AddDays(-16), InstanceB,
            raw: Line(Entry(day, 1, InstanceB)));
        var legacyFlat = root.WriteRoot("l12-process-metrics-20000101.jsonl",
            Encoding.UTF8.GetBytes("legacy unknown"));
        await using var archive = Archive(root, InstanceA);
        await Stabilize(archive, day, InstanceA);
        Assert.False(Directory.Exists(validExpired));
        Assert.True(Directory.Exists(pinned));
        Assert.True(Directory.Exists(badJson));
        Assert.True(Directory.Exists(unknown));
        Assert.True(Directory.Exists(incomplete));
        Assert.True(Directory.Exists(foreignInstance));
        Assert.True(Directory.Exists(mixedCommit));
        Assert.True(Directory.Exists(nonWhitelisted));
        Assert.True(Directory.Exists(wrongArchiveDay));
        Assert.True(File.Exists(legacyFlat));
        for (var index = 0; index <= 7; index++) Assert.True(Directory.Exists(root.Day(day.AddDays(-index))));
        root.Success();
    }

    [Fact]
    public async Task RetentionProtectsCanonicalJsonWithSemanticallyInvalidMetricValues()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        var unknownAvailabilityRaw = MutatedLine(day.AddDays(-20), root =>
            root["linux"]!["memoryCurrentAvailability"] = 999);
        var unknownAvailability = root.WriteOwnedDay(day.AddDays(-20), InstanceB,
            raw: unknownAvailabilityRaw);
        var negativeMemoryRaw = MutatedLine(day.AddDays(-21), root =>
            root["process"]!["workingSetBytes"] = -1);
        var negativeMemory = root.WriteOwnedDay(day.AddDays(-21), InstanceB,
            raw: negativeMemoryRaw);
        var negativeEventsRaw = MutatedLine(day.AddDays(-22), root =>
            root["linux"]!["eventsTotal"]!["Low"] = -1);
        var negativeEvents = root.WriteOwnedDay(day.AddDays(-22), InstanceB,
            raw: negativeEventsRaw);
        var negativeStageCountRaw = MutatedLine(day.AddDays(-23), root =>
            root["stages"]!.AsArray()[0]!["count"] = -1);
        var negativeStageCount = root.WriteOwnedDay(day.AddDays(-23), InstanceB,
            raw: negativeStageCountRaw);
        var availableMissingValueRaw = MutatedLine(day.AddDays(-24), root =>
            root["process"]!.AsObject().Remove("workingSetBytes"));
        var availableMissingValue = root.WriteOwnedDay(day.AddDays(-24), InstanceB,
            raw: availableMissingValueRaw);
        var legalUnavailable = root.WriteOwnedDay(day.AddDays(-25), InstanceB,
            raw: Line(UnavailableEntry(day.AddDays(-25), 1, InstanceB)));

        await using var archive = Archive(root, InstanceA);
        await Stabilize(archive, day, InstanceA);

        Assert.True(Directory.Exists(unknownAvailability));
        Assert.True(Directory.Exists(negativeMemory));
        Assert.True(Directory.Exists(negativeEvents));
        Assert.True(Directory.Exists(negativeStageCount));
        Assert.True(Directory.Exists(availableMissingValue));
        Assert.Equal(unknownAvailabilityRaw, ReadInstanceLine(unknownAvailability, InstanceB));
        Assert.Equal(negativeMemoryRaw, ReadInstanceLine(negativeMemory, InstanceB));
        Assert.Equal(negativeEventsRaw, ReadInstanceLine(negativeEvents, InstanceB));
        Assert.Equal(negativeStageCountRaw, ReadInstanceLine(negativeStageCount, InstanceB));
        Assert.Equal(availableMissingValueRaw, ReadInstanceLine(availableMissingValue, InstanceB));
        Assert.False(Directory.Exists(legalUnavailable));
        root.Success();
    }

    [Fact]
    public async Task CurrentWriteRejectsSemanticallyInvalidCanonicalJson()
    {
        using var root = new TestRoot();
        var day = UtcDay();
        var valid = Entry(day, 1000, InstanceA);
        var invalid = valid with
        {
            Json = MutatedJson(day, InstanceA, root => root["process"]!["workingSetBytes"] = -1)
        };
        await using var archive = Archive(root, InstanceA);
        var result = await archive.WriteAsync(invalid, default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.ContentMismatch, result.Reason);
        root.Success();
    }

    [Fact]
    public async Task EachRetentionPassDeletesAtMostEightFullyValidatedDays()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        for (var index = 20; index < 30; index++) root.WriteOwnedDay(day.AddDays(-index), InstanceB);
        await using var archive = Archive(root, InstanceA);
        await Stabilize(archive, day, InstanceA);
        Assert.Equal(2, ExpiredDirectories(root, day).Count());
        Assert.True((await archive.WriteAsync(Entry(day.AddSeconds(30), 31000, InstanceA), default)).Succeeded);
        Assert.Empty(ExpiredDirectories(root, day));
        root.Success();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public async Task UtcJumpOrRollbackRequiresTwoNewStableTransitionsBeforeCleanup(int dayJump)
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        var expired = root.WriteOwnedDay(day.AddDays(-20), InstanceB);
        await using var archive = Archive(root, InstanceA);
        Assert.True((await archive.WriteAsync(Entry(day, 1000, InstanceA), default)).Succeeded);
        var jumped = day.AddDays(dayJump);
        Assert.True((await archive.WriteAsync(Entry(jumped, 11000, InstanceA), default)).Succeeded);
        Assert.True(Directory.Exists(expired));
        Assert.True((await archive.WriteAsync(Entry(jumped.AddSeconds(10), 21000, InstanceA), default)).Succeeded);
        Assert.True(Directory.Exists(expired));
        Assert.True((await archive.WriteAsync(Entry(jumped.AddSeconds(20), 31000, InstanceA), default)).Succeeded);
        Assert.False(Directory.Exists(expired));
        root.Success();
    }

    [Fact]
    public async Task CrossProcessLockFreshSegmentsAndDailyAggregatePreventQuotaRace()
    {
        using var root = new TestRoot();
        var day = UtcDay();
        var firstEntry = Entry(day, 1000, InstanceA);
        var secondEntry = Entry(day.AddSeconds(10), 11000, InstanceB);
        var dailyCap = Encoding.UTF8.GetByteCount(MarkerText) + firstEntry.Json.Length + 1
            + secondEntry.Json.Length + 1;
        var first = Archive(root, InstanceA, dailyCap: dailyCap);
        var second = Archive(root, InstanceB, dailyCap: dailyCap);
        try
        {
            Assert.True((await first.WriteAsync(firstEntry, default)).Succeeded);
            var locked = await second.WriteAsync(secondEntry, default);
            Assert.False(locked.Succeeded);
            Assert.Equal(L12ProcessMetricArchiveReason.OwnershipConflict, locked.Reason);
            await first.DisposeAsync();
            Assert.True((await second.WriteAsync(secondEntry, default)).Succeeded);
            var over = await second.WriteAsync(Entry(day.AddSeconds(20), 21000, InstanceB), default);
            Assert.False(over.Succeeded);
            Assert.Equal(L12ProcessMetricArchiveReason.DailyCapacity, over.Reason);
            Assert.Equal(2, Directory.EnumerateFiles(root.Day(day), "*.jsonl").Count());
            await second.DisposeAsync();
            var reused = Archive(root, InstanceA, dailyCap: dailyCap * 2);
            try
            {
                var collision = await reused.WriteAsync(Entry(day.AddSeconds(30), 31000, InstanceA), default);
                Assert.False(collision.Succeeded);
                Assert.Equal(L12ProcessMetricArchiveReason.ContentMismatch, collision.Reason);
            }
            finally { await reused.DisposeAsync(); }
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
        root.Success();
    }

    [Fact]
    public async Task UnknownCurrentGuidSegmentIsNeverAppended()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        root.EnsureDayMarker(day);
        var path = root.WriteDay(day, InstanceA.ToString("N") + ".jsonl", Encoding.UTF8.GetBytes("unknown\n"));
        var before = File.ReadAllBytes(path);
        await using var archive = Archive(root, InstanceA);
        var result = await archive.WriteAsync(Entry(day, 1000, InstanceA), default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.ContentMismatch, result.Reason);
        Assert.Equal(before, File.ReadAllBytes(path));
        root.Success();
    }

    [Fact]
    public async Task ClosedSegmentCreatedByThisInstanceIsFreshlyValidatedBeforeDeletion()
    {
        using var root = new TestRoot();
        var day = UtcDay();
        var expiredDay = day.AddDays(-20);
        await using var archive = Archive(root, InstanceA);
        Assert.True((await archive.WriteAsync(Entry(expiredDay, 1000, InstanceA), default)).Succeeded);
        Assert.True((await archive.WriteAsync(Entry(day, 11000, InstanceA), default)).Succeeded);

        var expiredSegment = System.IO.Path.Combine(root.Day(expiredDay),
            InstanceA.ToString("N") + ".jsonl");
        var originalWrite = File.GetLastWriteTimeUtc(expiredSegment);
        var changed = File.ReadAllBytes(expiredSegment);
        changed[0] = (byte)'[';
        File.WriteAllBytes(expiredSegment, changed);
        File.SetLastWriteTimeUtc(expiredSegment, originalWrite);

        Assert.True((await archive.WriteAsync(Entry(day.AddSeconds(10), 21000, InstanceA), default)).Succeeded);
        Assert.True((await archive.WriteAsync(Entry(day.AddSeconds(20), 31000, InstanceA), default)).Succeeded);
        Assert.True(Directory.Exists(root.Day(expiredDay)));
        Assert.Equal(changed, File.ReadAllBytes(expiredSegment));
        root.Success();
    }

    [Fact]
    public async Task CachedValidSegmentChangedAtSameLengthAndTimestampIsRevalidatedBeforeDeletion()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        var expiredSegment = root.WriteOwnedDay(day, InstanceB);
        var segmentPath = System.IO.Path.Combine(expiredSegment, InstanceB.ToString("N") + ".jsonl");
        var originalWrite = File.GetLastWriteTimeUtc(segmentPath);
        await using var archive = Archive(root, InstanceA);
        Assert.True((await archive.WriteAsync(Entry(day, 1000, InstanceA), default)).Succeeded);

        var futureDay = day.AddDays(20);
        Assert.True((await archive.WriteAsync(Entry(futureDay, 11000, InstanceA), default)).Succeeded);
        var changed = File.ReadAllBytes(segmentPath);
        changed[0] = (byte)'[';
        File.WriteAllBytes(segmentPath, changed);
        File.SetLastWriteTimeUtc(segmentPath, originalWrite);
        Assert.Equal(originalWrite, File.GetLastWriteTimeUtc(segmentPath));

        Assert.True((await archive.WriteAsync(Entry(futureDay.AddSeconds(10), 21000, InstanceA), default)).Succeeded);
        var cleanupAttempt = await archive.WriteAsync(
            Entry(futureDay.AddSeconds(20), 31000, InstanceA), default);
        Assert.False(cleanupAttempt.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.UnsafeRoot, cleanupAttempt.Reason);
        Assert.True(Directory.Exists(expiredSegment));
        Assert.Equal(changed, File.ReadAllBytes(segmentPath));
        root.Success();
    }

    [Fact]
    public async Task ProtectedRootAtCapacityDropsWithoutDeletingEvidence()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        var pinned = root.WriteOwnedDay(day.AddDays(-30), InstanceB, pinned: true);
        var unknown = root.WriteRoot("unknown.bin", new byte[256]);
        await using var archive = Archive(root, InstanceA, rootCap: 128);
        var result = await archive.WriteAsync(Entry(day, 1000, InstanceA), default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.RootCapacity, result.Reason);
        Assert.True(Directory.Exists(pinned));
        Assert.True(File.Exists(unknown));
        root.Success();
    }

    [Fact]
    public async Task BoundedScanFailureLeavesAllEvidenceUntouched()
    {
        using var root = new TestRoot();
        root.EnsureRootMarker();
        var day = UtcDay();
        var expired = root.WriteOwnedDay(day.AddDays(-20), InstanceB);
        var unknown = root.WriteRoot("unknown-a", [1]);
        root.WriteRoot("unknown-b", [2]);
        await using var archive = Archive(root, InstanceA, maxEntries: 3);
        var result = await archive.WriteAsync(Entry(day, 1000, InstanceA), default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.UnsafeRoot, result.Reason);
        Assert.True(Directory.Exists(expired));
        Assert.True(File.Exists(unknown));
        root.Success();
    }

    [Fact]
    public void ReparsePointAttributeIsAlwaysClassifiedAsProtected()
    {
        Assert.True(L12ProcessMetricFileArchive.IsProtectedReparsePoint(FileAttributes.ReparsePoint));
        Assert.True(L12ProcessMetricFileArchive.IsProtectedReparsePoint(
            FileAttributes.ReparsePoint | FileAttributes.ReadOnly));
        Assert.False(L12ProcessMetricFileArchive.IsProtectedReparsePoint(FileAttributes.Normal));
    }

    [Fact]
    public async Task ReparseAncestorIsRejectedBeforeAnyArchiveDirectoryIsCreatedThroughIt()
    {
        var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-process-metric-reparse-" + Guid.NewGuid().ToString("N"));
        var target = System.IO.Path.Combine(basePath, "target");
        var link = System.IO.Path.Combine(basePath, "link");
        var successful = false;
        Directory.CreateDirectory(target);
        var sentinel = System.IO.Path.Combine(target, "incident-evidence.txt");
        File.WriteAllText(sentinel, "keep", new UTF8Encoding(false));
        try
        {
            CreateDirectoryLink(link, target);
            var archiveRoot = System.IO.Path.Combine(link, "runtime", "metrics", "process");
            await using (var archive = new L12ProcessMetricFileArchive(archiveRoot, Commit, InstanceA))
            {
                var result = await archive.WriteAsync(Entry(UtcDay(), 1000, InstanceA), default);
                Assert.False(result.Succeeded);
                Assert.Equal(L12ProcessMetricArchiveReason.UnsafeRoot, result.Reason);
            }
            Assert.False(Directory.Exists(System.IO.Path.Combine(target, "runtime")));
            Assert.Equal("keep", File.ReadAllText(sentinel));
            successful = true;
        }
        finally
        {
            if (successful)
            {
                if (Directory.Exists(link)
                    && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link);
                Directory.Delete(basePath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileArchiveRejectsOverEightKiBBeforeCreatingOwnedRoot()
    {
        using var root = new TestRoot();
        await using var archive = Archive(root, InstanceA);
        var entry = new L12SerializedProcessMetric(new byte[L12ProcessMetricCollector.MaximumLineBytes],
            UtcDay(), 1000, 1000);
        var result = await archive.WriteAsync(entry, default);
        Assert.False(result.Succeeded);
        Assert.Equal(L12ProcessMetricArchiveReason.EntryTooLarge, result.Reason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
        root.Success();
    }

    [Fact]
    public void ArchiveRootOutsideExactRuntimeMetricsProcessSuffixIsRejectedWithoutCreatingIt()
    {
        var wrongRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-process-metric-wrong-root-" + Guid.NewGuid().ToString("N"), "metrics-process");
        Assert.Throws<ArgumentException>(() => new L12ProcessMetricFileArchive(
            wrongRoot, Commit, InstanceA));
        Assert.False(Directory.Exists(wrongRoot));
    }

    private static IEnumerable<string> ExpiredDirectories(TestRoot root, DateTimeOffset day) =>
        Directory.EnumerateDirectories(root.Path).Where(path =>
            DateTime.TryParseExact(System.IO.Path.GetFileName(path), "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var parsed) && parsed < day.UtcDateTime.Date.AddDays(-7));

    private static L12ProcessMetricCollector Collector(IL12ProcessMetricArchiveSink sink,
        TimeSpan? shutdownTimeout = null,
        Func<TimeSpan, TimeProvider, CancellationToken, Task>? delay = null,
        Action<L12ProcessMetricArchiveReason>? alert = null)
    {
        var time = new ManualTimeProvider(UtcDay());
        return new(time, Probe(time), sink, new(Commit, InstanceA.ToString("N")), shutdownTimeout,
            delay ?? NeverDelay, alert);
    }

    private static L12ProcessMetricProbe Probe(ManualTimeProvider time,
        Func<L12ProcessRuntimeMetrics>? runtime = null)
    {
        var sample = Sample("private-source");
        return new(() => time.GetUtcNow(), runtime ?? (() => sample.Runtime),
            () => sample.Linux, () => (true, sample.PerformanceWindow));
    }

    private static L12ProcessMetricSample Sample(string source)
    {
        var now = UtcDay();
        var window = new PerformanceMetricWindow();
        window.Duration("action.engine", 3);
        window.Value("websocket.queue-depth", 4);
        Assert.True(window.TrySnapshot(out var performance));
        var events = new L12CgroupMemoryEvents(10, 20, 30, 40, 50, 60);
        var linux = new L12LinuxProcessMetrics(L12MetricAvailabilityReason.Available, 100,
            new(200, false, L12MetricAvailabilityReason.Available),
            L12MetricAvailabilityReason.Available, 300,
            new(null, true, L12MetricAvailabilityReason.Available),
            new(L12MetricAvailabilityReason.Available, L12CgroupEventScope.Local, events),
            new(L12MetricAvailabilityReason.Available, new(1.5, 1000, 0.5, 200)), source);
        return new(now, L12MetricAvailabilityReason.Available, Runtime(), linux,
            new(1, 2, 3, 4, 5, 6), L12CgroupEventDeltaStatus.Available, false,
            L12MetricAvailabilityReason.Available, performance);
    }

    private static L12ProcessRuntimeMetrics Runtime() => new(L12MetricAvailabilityReason.Available,
        1000, 2000, 3000, 4.5, 6, 7);

    private static L12ProcessMetricFileArchive Archive(TestRoot root, Guid instance,
        long dailyCap = 1024 * 1024, long rootCap = 8 * 1024 * 1024, int maxEntries = 4096) =>
        new(root.Path, Commit, instance, dailyCap, rootCap, maxEntries);

    private static async Task Stabilize(L12ProcessMetricFileArchive archive, DateTimeOffset day, Guid instance)
    {
        Assert.True((await archive.WriteAsync(Entry(day, 1000, instance), default)).Succeeded);
        Assert.True((await archive.WriteAsync(Entry(day.AddSeconds(10), 11000, instance), default)).Succeeded);
        Assert.True((await archive.WriteAsync(Entry(day.AddSeconds(20), 21000, instance), default)).Succeeded);
    }

    private static L12SerializedProcessMetric Entry(DateTimeOffset utc, long timestamp, Guid instance,
        string commit = Commit)
    {
        var sample = Sample("private-source") with { ObservedAt = utc };
        Assert.True(L12CompactProcessMetricEncoder.TryEncode(sample,
            new(commit, instance.ToString("N")), utc, timestamp, 1000,
            out var serialized, out var reason), reason.ToString());
        return serialized!;
    }

    private static L12SerializedProcessMetric UnavailableEntry(DateTimeOffset utc, long timestamp,
        Guid instance)
    {
        var sample = Sample("private-source") with
        {
            ObservedAt = utc,
            Runtime = new(L12MetricAvailabilityReason.ReadFailure, null, null, null, null, null, null),
            Linux = L12LinuxProcessMetrics.Unavailable(L12MetricAvailabilityReason.NotLinux),
            EventDeltas = null,
            EventDeltaStatus = L12CgroupEventDeltaStatus.Unavailable,
        };
        Assert.True(L12CompactProcessMetricEncoder.TryEncode(sample,
            new(Commit, instance.ToString("N")), utc, timestamp, 1000,
            out var serialized, out var reason), reason.ToString());
        return serialized!;
    }

    private static byte[] MutatedLine(DateTimeOffset utc, Action<JsonObject> mutate)
        => MutatedJson(utc, InstanceB, mutate).Concat([(byte)'\n']).ToArray();

    private static byte[] MutatedJson(DateTimeOffset utc, Guid instance, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(Entry(utc, 1, instance).Json)!.AsObject();
        mutate(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static byte[] ReadInstanceLine(string dayDirectory, Guid instance) =>
        File.ReadAllBytes(System.IO.Path.Combine(dayDirectory, instance.ToString("N") + ".jsonl"));

    private static byte[] Line(L12SerializedProcessMetric entry) => entry.Json.Concat([(byte)'\n']).ToArray();
    private static DateTimeOffset UtcDay() => new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static Task NeverDelay(TimeSpan _, TimeProvider __, CancellationToken token) =>
        Task.Delay(Timeout.InfiniteTimeSpan, token);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("synthetic wait");
            await Task.Delay(10);
        }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path "
            + Quote(link) + " -Value " + Quote(target) + " | Out-Null";
        var start = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("L12_TEST_FIXTURE_PWSH") ?? "pwsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Owned junction fixture creation timed out");
        }
        Assert.Equal(0, process.ExitCode);
        output.GetAwaiter().GetResult();
        error.GetAwaiter().GetResult();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp = 1000;
        public override DateTimeOffset GetUtcNow() { lock (_gate) return _utcNow; }
        public override long GetTimestamp() { lock (_gate) return _timestamp; }
        public override long TimestampFrequency => 1000;
        internal void Advance(TimeSpan value)
        {
            lock (_gate) { _utcNow += value; _timestamp += (long)(value.TotalSeconds * TimestampFrequency); }
        }
    }

    private sealed class ImmediateSink : IL12ProcessMetricArchiveSink
    {
        public ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(L12SerializedProcessMetric metric,
            CancellationToken cancellationToken) => ValueTask.FromResult(L12ProcessMetricArchiveWriteResult.Success);
    }

    private sealed class FailingSink : IL12ProcessMetricArchiveSink
    {
        internal int CallCount;
        public ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(L12SerializedProcessMetric metric,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return ValueTask.FromResult(L12ProcessMetricArchiveWriteResult.Failure(
                L12ProcessMetricArchiveReason.WriteFailure));
        }
    }

    private sealed class ThrowingSink(string message) : IL12ProcessMetricArchiveSink
    {
        internal int CallCount;
        public ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(L12SerializedProcessMetric metric,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            throw new IOException(message);
        }
    }

    private sealed class BlockingSink : IL12ProcessMetricArchiveSink
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(L12SerializedProcessMetric metric,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            return L12ProcessMetricArchiveWriteResult.Success;
        }
    }

    private sealed class CancellationCallbackSink : IL12ProcessMetricArchiveSink
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim CallbackEntered { get; } = new();
        internal ManualResetEventSlim Release { get; } = new();
        public async ValueTask<L12ProcessMetricArchiveWriteResult> WriteAsync(L12SerializedProcessMetric metric,
            CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                CallbackEntered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("synthetic cancellation callback");
            });
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return L12ProcessMetricArchiveWriteResult.Success;
        }
    }

    private sealed class TestRoot : IDisposable
    {
        private bool _successful;
        internal string BasePath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-process-metric-archive-" + Guid.NewGuid().ToString("N"));
        internal string Path { get; }
        internal TestRoot()
        {
            Path = System.IO.Path.Combine(BasePath, "runtime", "metrics", "process");
            Directory.CreateDirectory(Path);
        }
        internal string Day(DateTimeOffset day) => System.IO.Path.Combine(Path,
            day.UtcDateTime.ToString("yyyy-MM-dd"));
        internal void EnsureRootMarker() => File.WriteAllText(System.IO.Path.Combine(Path,
            L12ProcessMetricFileArchive.OwnershipMarkerName), MarkerText, new UTF8Encoding(false));
        internal void EnsureDayMarker(DateTimeOffset day)
        {
            Directory.CreateDirectory(Day(day));
            File.WriteAllText(System.IO.Path.Combine(Day(day), L12ProcessMetricFileArchive.OwnershipMarkerName),
                MarkerText, new UTF8Encoding(false));
        }
        internal string WriteRoot(string name, byte[] value)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, value);
            return path;
        }
        internal string WriteDay(DateTimeOffset day, string name, byte[] value)
        {
            Directory.CreateDirectory(Day(day));
            var path = System.IO.Path.Combine(Day(day), name);
            File.WriteAllBytes(path, value);
            return path;
        }
        internal string WriteOwnedDay(DateTimeOffset day, Guid instance, bool pinned = false,
            bool unknownFile = false, byte[]? raw = null)
        {
            EnsureRootMarker();
            EnsureDayMarker(day);
            WriteDay(day, instance.ToString("N") + ".jsonl", raw ?? Line(Entry(day, 1, instance)));
            if (pinned) WriteDay(day, L12ProcessMetricFileArchive.PinFileName, Encoding.UTF8.GetBytes("incident"));
            if (unknownFile) WriteDay(day, "unknown-evidence.bin", [1, 2, 3]);
            return Day(day);
        }
        internal void Success() => _successful = true;
        public void Dispose()
        {
            if (_successful && Directory.Exists(BasePath)) Directory.Delete(BasePath, recursive: true);
        }
    }
}
