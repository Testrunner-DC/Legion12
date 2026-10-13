using System.Collections.Concurrent;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PresenceRefreshCoalescerTests
{
    [Fact]
    public async Task StoppedWorkerRejectsTargetsBeforeEnumeratingThem()
    {
        var enumerated = false;
        IEnumerable<string> Targets()
        {
            enumerated = true;
            yield return "alpha";
        }
        await using var coalescer = new L12PresenceRefreshCoalescer((_, _) => Task.CompletedTask);
        Assert.True(coalescer.Start());
        await coalescer.StopAsync();
        Assert.False(coalescer.Request(1, Targets()));
        Assert.False(enumerated);
    }

    [Fact]
    public async Task SixtyGlobalRequestsShareOneWindowAndUseLatestRevision()
    {
        var delay = new ControlledDelay();
        var emitted = new TaskCompletionSource<L12PresenceRefreshBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coalescer = new L12PresenceRefreshCoalescer(
            (batch, _) => { emitted.TrySetResult(batch); return Task.CompletedTask; },
            delay: delay.WaitAsync);
        Assert.True(coalescer.Start());

        for (var revision = 1; revision <= 60; revision++)
            Assert.True(coalescer.Request(revision));

        await delay.WaitForCountAsync(1);
        Assert.False(emitted.Task.IsCompleted);
        delay.Release(0);
        var batch = await emitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(batch.AllAccounts);
        Assert.Equal(60, batch.Revision);
        Assert.Empty(batch.AccountIds);
        Assert.Equal(1, delay.Count);
    }

    [Fact]
    public async Task TargetedRequestsUnionCaseInsensitivelyAndGlobalWins()
    {
        var delay = new ControlledDelay();
        var batches = new ConcurrentQueue<L12PresenceRefreshBatch>();
        var emitted = new SemaphoreSlim(0);
        await using var coalescer = new L12PresenceRefreshCoalescer((batch, _) =>
        {
            batches.Enqueue(batch);
            emitted.Release();
            return Task.CompletedTask;
        }, delay: delay.WaitAsync);
        coalescer.Start();

        coalescer.Request(1, ["alpha", "ALPHA", "beta"]);
        coalescer.Request(2, ["gamma"]);
        await delay.WaitForCountAsync(1);
        delay.Release(0);
        Assert.True(await emitted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(batches.TryDequeue(out var targeted));
        Assert.False(targeted.AllAccounts);
        Assert.Equal(2, targeted.Revision);
        Assert.Equal(3, targeted.AccountIds.Count);
        Assert.Contains(targeted.AccountIds, value => value.Equals("alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("beta", targeted.AccountIds);
        Assert.Contains("gamma", targeted.AccountIds);

        coalescer.Request(3, ["delta"]);
        coalescer.Request(4);
        coalescer.Request(5, ["epsilon"]);
        await delay.WaitForCountAsync(2);
        delay.Release(1);
        Assert.True(await emitted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(batches.TryDequeue(out var global));
        Assert.True(global.AllAccounts);
        Assert.Equal(5, global.Revision);
        Assert.Empty(global.AccountIds);
    }

    [Fact]
    public async Task MoreThanTargetLimitSafelyPromotesToGlobal()
    {
        var delay = new ControlledDelay();
        var batches = new ConcurrentQueue<L12PresenceRefreshBatch>();
        var emitted = new SemaphoreSlim(0);
        await using var coalescer = new L12PresenceRefreshCoalescer((batch, _) =>
        {
            batches.Enqueue(batch);
            emitted.Release();
            return Task.CompletedTask;
        }, delay: delay.WaitAsync);
        coalescer.Start();

        coalescer.Request(1, Enumerable.Range(0, 128).Select(index => $"account-{index}"));
        await delay.WaitForCountAsync(1);
        delay.Release(0);
        Assert.True(await emitted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(batches.TryDequeue(out var bounded));
        Assert.False(bounded.AllAccounts);
        Assert.Equal(128, bounded.AccountIds.Count);

        coalescer.Request(2, Enumerable.Range(0, 129).Select(index => $"next-{index}"));
        await delay.WaitForCountAsync(2);
        delay.Release(1);
        Assert.True(await emitted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(batches.TryDequeue(out var promoted));
        Assert.True(promoted.AllAccounts);
        Assert.Empty(promoted.AccountIds);
    }

    [Fact]
    public async Task RequestArrivingDuringEmitGetsItsOwnLaterFlush()
    {
        var delay = new ControlledDelay();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEmitted = new TaskCompletionSource<L12PresenceRefreshBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var coalescer = new L12PresenceRefreshCoalescer(async (batch, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return;
            }
            secondEmitted.TrySetResult(batch);
        }, delay: delay.WaitAsync);
        coalescer.Start();

        coalescer.Request(1, ["alpha"]);
        await delay.WaitForCountAsync(1);
        delay.Release(0);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coalescer.Request(2, ["beta"]);
        releaseFirst.TrySetResult();

        await delay.WaitForCountAsync(2);
        delay.Release(1);
        var second = await secondEmitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, second.Revision);
        Assert.Equal(new[] { "beta" }, second.AccountIds);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task FailedEmitIsRetainedButOnlyRetriedAfterAnotherRequest()
    {
        var delay = new ControlledDelay();
        var failureObserved = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<L12PresenceRefreshBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var coalescer = new L12PresenceRefreshCoalescer((batch, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("synthetic emit failure");
            recovered.TrySetResult(batch);
            return Task.CompletedTask;
        }, delay: delay.WaitAsync,
            onEmitFailure: error => failureObserved.TrySetResult(error));
        coalescer.Start();

        coalescer.Request(1, ["alpha"]);
        await delay.WaitForCountAsync(1);
        delay.Release(0);
        var failure = await failureObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("synthetic emit failure", failure.Message);
        Assert.Equal(1, attempts);
        Assert.Equal(1, delay.Count);

        coalescer.Request(2, ["beta"]);
        await delay.WaitForCountAsync(2);
        delay.Release(1);
        var batch = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, batch.Revision);
        Assert.Equal(2, batch.AccountIds.Count);
        Assert.Contains("alpha", batch.AccountIds);
        Assert.Contains("beta", batch.AccountIds);
    }

    [Fact]
    public async Task StopCancelsPendingDelayRejectsRequestsAndIsIdempotent()
    {
        var delay = new ControlledDelay();
        var emitCount = 0;
        var coalescer = new L12PresenceRefreshCoalescer((_, _) =>
        {
            Interlocked.Increment(ref emitCount);
            return Task.CompletedTask;
        }, delay: delay.WaitAsync);
        coalescer.Start();
        coalescer.Request(1);
        await delay.WaitForCountAsync(1);

        var firstStop = coalescer.StopAsync();
        var secondStop = coalescer.StopAsync();
        Assert.Same(firstStop, secondStop);
        await firstStop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, emitCount);
        Assert.False(coalescer.Request(2));
        await coalescer.DisposeAsync();
        await coalescer.DisposeAsync();
    }

    [Fact]
    public async Task StopWaitsForAnEmitAlreadyInProgress()
    {
        var delay = new ControlledDelay();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coalescer = new L12PresenceRefreshCoalescer(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        }, delay: delay.WaitAsync);
        coalescer.Start();
        coalescer.Request(1);
        await delay.WaitForCountAsync(1);
        delay.Release(0);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stop = coalescer.StopAsync();
        Assert.False(stop.IsCompleted);
        Assert.False(coalescer.Request(2));
        release.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        await coalescer.DisposeAsync();
    }

    [Fact]
    public async Task ACompletedStopCanStartANewWorkerGenerationUntilDisposed()
    {
        var delay = new ControlledDelay();
        var emitted = new SemaphoreSlim(0);
        var revisions = new ConcurrentQueue<long>();
        var coalescer = new L12PresenceRefreshCoalescer((batch, _) =>
        {
            revisions.Enqueue(batch.Revision);
            emitted.Release();
            return Task.CompletedTask;
        }, delay: delay.WaitAsync);

        Assert.True(coalescer.Start());
        Assert.True(coalescer.Request(1));
        await delay.WaitForCountAsync(1);
        delay.Release(0);
        Assert.True(await emitted.WaitAsync(TimeSpan.FromSeconds(2)));
        await coalescer.StopAsync();

        Assert.True(coalescer.Start());
        Assert.True(coalescer.Request(2));
        await delay.WaitForCountAsync(2);
        delay.Release(1);
        Assert.True(await emitted.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(new long[] { 1, 2 }, revisions);

        await coalescer.DisposeAsync();
        Assert.False(coalescer.Start());
        Assert.False(coalescer.Request(3));
    }

    private sealed class ControlledDelay
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _calls = [];
        private readonly SemaphoreSlim _arrived = new(0);

        internal int Count
        {
            get { lock (_gate) return _calls.Count; }
        }

        internal Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Assert.Equal(L12PresenceRefreshCoalescer.DefaultWindow, duration);
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(), source);
            lock (_gate) _calls.Add(source);
            _arrived.Release();
            return AwaitWithRegistrationAsync(source.Task, registration);
        }

        internal async Task WaitForCountAsync(int expected)
        {
            while (Count < expected)
                Assert.True(await _arrived.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        internal void Release(int index)
        {
            TaskCompletionSource source;
            lock (_gate) source = _calls[index];
            source.TrySetResult();
        }

        private static async Task AwaitWithRegistrationAsync(Task task,
            CancellationTokenRegistration registration)
        {
            using (registration) await task;
        }
    }
}
