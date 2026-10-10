using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CardAnalyticsRequestLifecycleTests
{
    private readonly ITestOutputHelper _output;
    public CardAnalyticsRequestLifecycleTests(ITestOutputHelper output) => _output = output;
    private sealed record Result(string Value);

    [Fact]
    public async Task OneWaiterCancellationDoesNotCancelTheOtherAndComputesExactlyOnce()
    {
        await using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var firstCancellation = new CancellationTokenSource();
        var calls = 0;
        var ownerTiming = new CardAnalyticsRequestTiming();
        var joinedTiming = new CardAnalyticsRequestTiming();
        Result Compute(MatchRecorder.CardAnalyticsExecution execution)
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(execution.CancellationToken);
            return new Result("shared");
        }
        var first = fixture.Run("same", Compute, firstCancellation.Token, ownerTiming);
        await WaitUntilAsync(() => entered.IsSet);
        var second = fixture.Run("same", Compute, timing: joinedTiming);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        release.Set();
        Assert.Equal("shared", (await second)!.Value);
        await fixture.DrainedAsync();
        Assert.Equal(1, calls);
        Assert.Contains("singleflight;desc=\"owner\"", ownerTiming.ToServerTiming());
        Assert.Contains("singleflight;desc=\"joined\"", joinedTiming.ToServerTiming());
        Assert.Equal(1, fixture.Recorder.AnalyticsResultCacheCount);
        Assert.Equal("shared", (await fixture.Run("same", _ => throw new Exception("cache miss")))!.Value);
    }

    [Fact]
    public async Task AllWaitersLeavingInterruptsRunningNativeSqlAndDoesNotCache()
    {
        await using var fixture = new Fixture();
        using var started = new ManualResetEventSlim();
        using var cancelA = new CancellationTokenSource();
        using var cancelB = new CancellationTokenSource();
        var first = fixture.Run("native", execution => LongQuery(execution, started), cancelA.Token);
        await WaitUntilAsync(() => started.IsSet);
        var second = fixture.Run("native", _ => throw new Exception("duplicate native query"), cancelB.Token);
        var watch = Stopwatch.StartNew();
        cancelA.Cancel();
        cancelB.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await fixture.DrainedAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        _output.WriteLine($"all-waiters native owner drained: {watch.Elapsed.TotalMilliseconds:F2}ms");
        Assert.Equal(1, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
        Assert.Equal("fresh", (await fixture.Run("native", _ => new Result("fresh")))!.Value);
    }

    [Fact]
    public async Task CancellationBetweenStatementsStillInterruptsTheNextNativeQuery()
    {
        await using var fixture = new Fixture();
        using var between = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var task = fixture.Run("between", execution =>
        {
            between.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(3)));
            // Deliberately bypass phase.Check to exercise the persistent native progress hook.
            return LongQuery(execution, started, checkPhase: false);
        }, cancellation.Token);
        await WaitUntilAsync(() => between.IsSet);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        release.Set();
        await fixture.DrainedAsync();
        Assert.Equal(1, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task InterruptingOneDedicatedConnectionDoesNotAffectTheOtherOrANewConnection()
    {
        await using var fixture = new Fixture();
        using var startedA = new ManualResetEventSlim();
        using var startedB = new ManualResetEventSlim();
        using var releaseB = new ManualResetEventSlim();
        using var cancelA = new CancellationTokenSource();
        var first = fixture.Run("connection-a", execution => LongQuery(execution, startedA), cancelA.Token);
        var second = fixture.Run("connection-b", execution =>
        {
            Assert.False(new SqliteConnectionStringBuilder(execution.Connection.ConnectionString).Pooling);
            startedB.Set();
            releaseB.Wait(execution.CancellationToken);
            using var command = execution.Connection.CreateCommand();
            command.CommandText = "SELECT 42;";
            return new Result(command.ExecuteScalar()!.ToString()!);
        });
        await WaitUntilAsync(() => startedA.IsSet && startedB.IsSet);
        cancelA.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        releaseB.Set();
        Assert.Equal("42", (await second)!.Value);
        await fixture.DrainedAsync();
        using var fresh = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fixture.Path, Pooling = false }.ToString());
        fresh.Open();
        using var query = fresh.CreateCommand();
        query.CommandText = "SELECT 43;";
        Assert.Equal(43L, query.ExecuteScalar());
    }

    [Fact]
    public async Task OldOwnerFinallyCannotDeleteItsReplacementJob()
    {
        await using var fixture = new Fixture();
        using var enteredA = new ManualResetEventSlim();
        using var enteredB = new ManualResetEventSlim();
        using var releaseA = new ManualResetEventSlim();
        using var releaseB = new ManualResetEventSlim();
        using var cancelA = new CancellationTokenSource();
        var first = fixture.Run("replacement", _ =>
        {
            enteredA.Set();
            Assert.True(releaseA.Wait(TimeSpan.FromSeconds(3)));
            return new Result("old");
        }, cancelA.Token);
        await WaitUntilAsync(() => enteredA.IsSet);
        cancelA.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = fixture.Run("replacement", execution =>
        {
            enteredB.Set();
            releaseB.Wait(execution.CancellationToken);
            return new Result("new");
        });
        await WaitUntilAsync(() => enteredB.IsSet);
        releaseA.Set();
        await WaitUntilAsync(() => fixture.Recorder.CardAnalyticsExecutionCounts.Active == 1);
        Assert.Equal(1, fixture.Recorder.CardAnalyticsExecutionCounts.Registered);
        var third = fixture.Run("replacement", _ => throw new Exception("replacement removed"));
        releaseB.Set();
        Assert.Equal("new", (await second)!.Value);
        Assert.Equal("new", (await third)!.Value);
        await fixture.DrainedAsync();
    }

    [Fact]
    public async Task EpochChangeInterruptsNativeWorkAndFreshEpochDoesNotReturnOrCacheOldStatistics()
    {
        await using var fixture = new Fixture();
        using var started = new ManualResetEventSlim();
        var task = fixture.Run("epoch", execution => LongQuery(execution, started));
        await WaitUntilAsync(() => started.IsSet);
        fixture.Recorder.InvalidateAnalyticsCache();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await fixture.DrainedAsync();
        Assert.Equal(1, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
        Assert.Equal("new-epoch", (await fixture.Run("epoch", _ => new Result("new-epoch")))!.Value);
    }

    [Fact]
    public async Task RealSqlErrorsRemainErrorsAndFailedEntriesCanBeRetried()
    {
        await using var fixture = new Fixture();
        var task = fixture.Run("failure", execution =>
        {
            using var command = execution.Connection.CreateCommand();
            command.CommandText = "SELECT * FROM synthetic_missing_table;";
            command.ExecuteScalar();
            return new Result("impossible");
        });
        var error = await Assert.ThrowsAsync<SqliteException>(() => task);
        Assert.Equal(SQLitePCL.raw.SQLITE_ERROR, error.SqliteErrorCode);
        await fixture.DrainedAsync();
        Assert.Equal(0, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
        Assert.Equal("retry", (await fixture.Run("failure", _ => new Result("retry")))!.Value);
    }

    [Fact]
    public async Task CancellationBeforeStartAndAfterCompletionCannotTouchAClosedHandle()
    {
        await using var fixture = new Fixture();
        using var before = new CancellationTokenSource();
        before.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Run("before",
            _ => throw new Exception("started after cancellation"), before.Token));
        Assert.Equal(0, fixture.Recorder.CardAnalyticsExecutionCounts.Active);
        using var after = new CancellationTokenSource();
        Assert.Equal("done", (await fixture.Run("after", _ => new Result("done"), after.Token))!.Value);
        await fixture.DrainedAsync();
        after.Cancel();
        Assert.Equal("done", (await fixture.Run("after", _ => throw new Exception("cache miss")))!.Value);
        Assert.Equal(0, fixture.Recorder.CardAnalyticsNativeInterruptions);
    }

    [Fact]
    public async Task HardBudgetInterruptsSqlEvenWhenAWaiterRemains()
    {
        await using var fixture = new Fixture();
        using var started = new ManualResetEventSlim();
        var timing = new CardAnalyticsRequestTiming();
        var watch = Stopwatch.StartNew();
        var task = fixture.Run("budget", execution => LongQuery(execution, started), timing: timing);
        await WaitUntilAsync(() => started.IsSet);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await fixture.DrainedAsync();
        Assert.InRange(watch.Elapsed.TotalSeconds, 6, 7.5);
        _output.WriteLine($"hard budget native owner drained: {watch.Elapsed.TotalMilliseconds:F2}ms");
        Assert.Equal(1, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Contains("interrupted;dur=0", timing.ToServerTiming());
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task SaturationIsBoundedAndQueuedJobsExpireWithoutStartingSql()
    {
        await using var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        var started = 0;
        var queuedStarts = 0;
        var blockers = Enumerable.Range(0, 2).Select(index => fixture.Run("block-" + index, _ =>
        {
            Interlocked.Increment(ref started);
            if (!release.Wait(TimeSpan.FromSeconds(12))) throw new TimeoutException("test release missing");
            return new Result("released");
        })).ToArray();
        var queued = new List<Task<Result?>>();
        try
        {
            await WaitUntilAsync(() => Volatile.Read(ref started) == 2);
            for (var index = 0; index < 14; index++)
                queued.Add(fixture.Run("queue-" + index, _ =>
                { Interlocked.Increment(ref queuedStarts); return new Result("should not run"); }));
            Assert.Equal((16, 2, 16), fixture.Recorder.CardAnalyticsExecutionCounts);
            var busy = await Assert.ThrowsAsync<CardAnalyticsUnavailableException>(() =>
                fixture.Run("seventeenth", _ => new Result("unbounded")));
            Assert.Equal("analytics_busy", busy.Code);
            Assert.Equal(503, busy.StatusCode);
            using var duplicateCancellation = new CancellationTokenSource();
            var duplicate = fixture.Run("queue-0", _ => throw new Exception("duplicate job"),
                duplicateCancellation.Token);
            duplicateCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => duplicate);
            foreach (var task in queued) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(0, queuedStarts);
        }
        finally
        {
            release.Set();
            foreach (var task in blockers) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            await fixture.DrainedAsync();
        }
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task ApplicationStoppingCancelsEveryWaiterAndDrainsTheNativeOwner()
    {
        await using var fixture = new Fixture();
        using var started = new ManualResetEventSlim();
        using var shutdown = new CancellationTokenSource();
        using var disconnected = new CancellationTokenSource();
        using var firstToken = MatchRecorder.CreateCardAnalyticsRequestCancellation(disconnected.Token, shutdown.Token);
        using var secondToken = MatchRecorder.CreateCardAnalyticsRequestCancellation(CancellationToken.None, shutdown.Token);
        var first = fixture.Run("shutdown", execution => LongQuery(execution, started), firstToken.Token);
        await WaitUntilAsync(() => started.IsSet);
        var second = fixture.Run("shutdown", _ => throw new Exception("duplicate job"), secondToken.Token);
        disconnected.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        var watch = Stopwatch.StartNew();
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await fixture.DrainedAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
        _output.WriteLine($"shutdown-linked native owner drained: {watch.Elapsed.TotalMilliseconds:F2}ms");
        Assert.Equal(1, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task BusyLockIsBoundedAndCanBeRetriedAfterTheLockIsReleased()
    {
        await using var fixture = new Fixture();
        using var locked = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fixture.Path, Pooling = false }.ToString());
        locked.Open();
        using var command = locked.CreateCommand();
        command.CommandText = "CREATE TABLE synthetic_busy(value); BEGIN EXCLUSIVE; INSERT INTO synthetic_busy VALUES(1);";
        command.ExecuteNonQuery();
        var watch = Stopwatch.StartNew();
        try
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => fixture.Run("locked", execution =>
            {
                using var query = execution.Connection.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM synthetic_busy;";
                return new Result(query.ExecuteScalar()!.ToString()!);
            }));
            Assert.Equal(SQLitePCL.raw.SQLITE_BUSY, error.SqliteErrorCode);
            await fixture.DrainedAsync();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
            _output.WriteLine($"busy lock owner drained: {watch.Elapsed.TotalMilliseconds:F2}ms");
            Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
        }
        finally { command.CommandText = "ROLLBACK;"; command.ExecuteNonQuery(); }
        Assert.Equal("retry", (await fixture.Run("locked", _ => new Result("retry")))!.Value);
    }

    [Fact]
    public async Task BusyRetryNearWorkDeadlineAndNativeDisposalFinishInsideTotalBudget()
    {
        await using var fixture = new Fixture();
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fixture.Path, Pooling = false }.ToString());
        writer.Open();
        using var write = writer.CreateCommand();
        write.CommandText = "CREATE TABLE synthetic_busy(value);";
        write.ExecuteNonQuery();
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var watch = Stopwatch.StartNew();
        var task = fixture.Run("late-lock", execution =>
        {
            ready.Set();
            if (!release.Wait(TimeSpan.FromSeconds(7))) throw new TimeoutException("test release missing");
            using var query = execution.Connection.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM synthetic_busy;";
            return new Result(query.ExecuteScalar()!.ToString()!);
        });
        try
        {
            await WaitUntilAsync(() => ready.IsSet);
            write.CommandText = "BEGIN EXCLUSIVE; INSERT INTO synthetic_busy VALUES(1);";
            write.ExecuteNonQuery();
            var target = MatchRecorder.CardAnalyticsWorkBudget - TimeSpan.FromMilliseconds(750);
            var remaining = target - watch.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            release.Set();
            var error = await Record.ExceptionAsync(() => task);
            Assert.True(error is OperationCanceledException
                || error is SqliteException { SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY }, error?.ToString());
            await fixture.DrainedAsync();
            Assert.True(watch.Elapsed < MatchRecorder.CardAnalyticsHardBudget, watch.Elapsed.ToString());
            _output.WriteLine($"late busy retry + native teardown owner drained: {watch.Elapsed.TotalMilliseconds:F2}ms");
            Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
        }
        finally
        {
            release.Set();
            write.CommandText = "ROLLBACK;";
            write.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task SqliteInterruptWithoutJobCancellationRemainsARealSqlError()
    {
        await using var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<SqliteException>(() => fixture.Run("unexpected-interrupt", execution =>
        {
            execution.Connection.CreateFunction("synthetic_interrupt", () =>
            {
                SQLitePCL.raw.sqlite3_interrupt(execution.Connection.Handle!);
                return 1;
            });
            using var command = execution.Connection.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE numbers(value) AS (
                    SELECT synthetic_interrupt()
                    UNION ALL SELECT value+1 FROM numbers WHERE value<1000000000
                ) SELECT SUM(value) FROM numbers;
                """;
            command.ExecuteScalar();
            return new Result("impossible");
        }));
        Assert.Equal(SQLitePCL.raw.SQLITE_INTERRUPT, error.SqliteErrorCode);
        await fixture.DrainedAsync();
        Assert.Equal(0, fixture.Recorder.CardAnalyticsNativeInterruptions);
        Assert.Equal(0, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task CompletionAndCancellationRaceCannotInterruptADisposedOrReusedHandle()
    {
        await using var fixture = new Fixture();
        var canceled = 0;
        var completed = 0;
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var cancellationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = fixture.Run("race-" + iteration, execution =>
            {
                execution.Connection.CreateFunction("synthetic_race", () =>
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { cancellation.Cancel(); cancellationFinished.TrySetResult(); }
                        catch (Exception error) { cancellationFinished.TrySetException(error); }
                    });
                    return 0L;
                });
                using var query = execution.Connection.CreateCommand();
                query.CommandText = """
                    WITH RECURSIVE numbers(value) AS (
                        SELECT synthetic_race()
                        UNION ALL SELECT value+1 FROM numbers WHERE value<10000
                    ) SELECT SUM(value) FROM numbers;
                    """;
                return new Result(query.ExecuteScalar()!.ToString()!);
            }, cancellation.Token);
            var error = await Record.ExceptionAsync(() => task);
            Assert.True(error is null or OperationCanceledException, error?.ToString());
            if (error is null) completed++; else canceled++;
            await cancellationFinished.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await fixture.DrainedAsync();
            Assert.Equal("healthy", (await fixture.Run("health-" + iteration, execution =>
            {
                using var query = execution.Connection.CreateCommand();
                query.CommandText = "SELECT 1;";
                Assert.Equal(1L, query.ExecuteScalar());
                return new Result("healthy");
            }))!.Value);
            await fixture.DrainedAsync();
        }
        _output.WriteLine($"native completion/cancellation races: 32, canceled={canceled}, completed={completed}");
    }

    [Fact]
    public async Task ActualHttpHandlersKeepAuthorizationAndTimingAndRealHostStopDrainsNativeWork()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-card-http-" + Guid.NewGuid().ToString("N"));
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        var catalog = L12Catalog.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var path = System.IO.Path.Combine(root, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var platform = new L12PlatformStore(System.IO.Path.Combine(root, "platform.json"), officialCards: catalog.Cards);
        var login = platform.Login("Admin", "L12master");
        Assert.True(login.Success);
        var rooms = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(rooms, recorder, platform, catalog);
        var passed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            foreach (var suffix in new[] { "", "/synthetic-missing" })
            {
                using var unauthorized = await client.GetAsync("/api/admin/analytics/cards" + suffix);
                Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
                // Existing global middleware emits app timing even for denied requests.
                if (unauthorized.Headers.TryGetValues("Server-Timing", out var deniedValues))
                    Assert.All(deniedValues, value => Assert.DoesNotContain("scope-total", value));
                Assert.Equal(0, recorder.CardAnalyticsExecutionCounts.Active);
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/analytics/cards" + suffix);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
                using var response = await client.SendAsync(request);
                Assert.Equal(suffix.Length == 0 ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
                Assert.True(response.Headers.TryGetValues("Server-Timing", out var values));
                var header = string.Join(",", values);
                Assert.Contains("scope-total;dur=", header);
                Assert.Contains("total;dur=", header);
                Assert.DoesNotContain("synthetic-missing", header);
            }
            var app = (WebApplication)typeof(L12WebSocketServer).GetField("_app",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
            using var stopping = MatchRecorder.CreateCardAnalyticsRequestCancellation(
                CancellationToken.None, app.Lifetime.ApplicationStopping);
            using var started = new ManualResetEventSlim();
            var task = recorder.ExecuteCardAnalyticsAsync("real-host-stop", recorder.AnalyticsCacheEpoch,
                stopping.Token, new CardAnalyticsRequestTiming(), execution => LongQuery(execution, started));
            await WaitUntilAsync(() => started.IsSet);
            var watch = Stopwatch.StartNew();
            await server.StopAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            await WaitUntilAsync(() => recorder.CardAnalyticsExecutionCounts.Active == 0);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
            Assert.Equal(1, recorder.CardAnalyticsNativeInterruptions);
            _output.WriteLine($"actual host stop + native owner drained: {watch.Elapsed.TotalMilliseconds:F2}ms");
            passed = true;
        }
        finally
        {
            await server.StopAsync();
            using var pooled = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(pooled);
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (passed) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PhaseTimingContainsOnlyFixedNamesAndNumericDurations()
    {
        await using var fixture = new Fixture();
        var timing = new CardAnalyticsRequestTiming();
        await fixture.Run("private-filter-and-identity", execution =>
            execution.Measure(CardAnalyticsStage.ScopeTotal, () => new Result("secret-value")), timing: timing);
        var header = timing.ToServerTiming();
        Assert.Contains("scope-total;dur=", header);
        Assert.Contains("queue;dur=", header);
        Assert.Contains("wait;dur=", header);
        Assert.DoesNotContain("private-filter", header);
        Assert.DoesNotContain("secret-value", header);
        var hit = new CardAnalyticsRequestTiming();
        await fixture.Run("private-filter-and-identity", _ => throw new Exception("cache miss"), timing: hit);
        Assert.Contains("cache;desc=\"hit\"", hit.ToServerTiming());
    }

    private static Result LongQuery(MatchRecorder.CardAnalyticsExecution execution, ManualResetEventSlim started,
        bool checkPhase = true)
    {
        Assert.False(new SqliteConnectionStringBuilder(execution.Connection.ConnectionString).Pooling);
        execution.Connection.CreateFunction("synthetic_started", () => { started.Set(); return 0L; });
        using var command = execution.Connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE numbers(value) AS (
                SELECT synthetic_started()
                UNION ALL SELECT value+1 FROM numbers WHERE value<1000000000
            ) SELECT SUM(value) FROM numbers;
            """;
        return checkPhase ? execution.Measure(CardAnalyticsStage.Rows,
            () => new Result(command.ExecuteScalar()!.ToString()!))
            : new Result(command.ExecuteScalar()!.ToString()!);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("synthetic lifecycle condition");
            await Task.Delay(10);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-card-lifecycle-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "matches.db");
        internal MatchRecorder Recorder { get; }
        internal Fixture() => Recorder = new MatchRecorder(Path);
        internal Task<Result?> Run(string key, Func<MatchRecorder.CardAnalyticsExecution, Result?> compute,
            CancellationToken token = default, CardAnalyticsRequestTiming? timing = null)
            => Recorder.ExecuteCardAnalyticsAsync(key, Recorder.AnalyticsCacheEpoch, token,
                timing ?? new CardAnalyticsRequestTiming(), compute);
        internal Task DrainedAsync() => WaitUntilAsync(() => Recorder.CardAnalyticsExecutionCounts.Active == 0);
        public async ValueTask DisposeAsync()
        {
            await DrainedAsync();
            await Recorder.DisposeAsync();
            Directory.Delete(_root, true);
        }
    }
}
