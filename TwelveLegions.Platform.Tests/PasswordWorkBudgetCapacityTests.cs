using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PasswordWorkBudgetCapacityTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    [InlineData(1, 0, 3001)]
    public void InvalidAdmissionConfigurationIsRejected(int workers, int queued, int waitMs)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new L12PasswordWorkBudget(workers, queued, TimeSpan.FromMilliseconds(waitMs)));

    [Fact]
    public void TotalAdmissionCannotOverflow()
        => Assert.Throws<OverflowException>(() => new L12PasswordWorkBudget(int.MaxValue, 1, TimeSpan.Zero));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WaitingRequestOccupiesQueueSlotAndOverflowDoesNotDisplaceIt(int workers)
    {
        var budget = new L12PasswordWorkBudget(workers, 1, TimeSpan.FromSeconds(1));
        var held = Enumerable.Range(0, workers).Select(_ => budget.Acquire()).ToArray();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var queued = Run(() =>
        {
            using var lease = budget.Acquire();
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic queue barrier.");
            return true;
        });
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Admitted(budget) == workers + 1, TimeSpan.FromMilliseconds(500)));
            Assert.False(entered.IsSet);
            Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
            Assert.Equal(workers + 1, Admitted(budget));
            held[0].Dispose();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
            Assert.Equal(workers, Admitted(budget));
        }
        finally
        {
            foreach (var lease in held) lease.Dispose();
            release.Set();
            await Drain(queued);
        }
        Assert.True(await queued);
        Assert.Equal(0, Admitted(budget));
        using (budget.Acquire()) { }
        Assert.Equal(0, Admitted(budget));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TimedOutWaiterReturnsAdmissionWithoutReleasingSomeoneElsesWorker(int workers)
    {
        var budget = new L12PasswordWorkBudget(workers, 1, TimeSpan.FromMilliseconds(20));
        var held = Enumerable.Range(0, workers).Select(_ => budget.Acquire()).ToArray();
        var queued = Run(() => { using var lease = budget.Acquire(); return true; });
        try
        {
            await Assert.ThrowsAsync<L12AuthenticationBusyException>(async () => await queued);
            Assert.Equal(workers, Admitted(budget));
            // A timed-out waiter must not accidentally release a real worker.
            Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
            Assert.Equal(workers, Admitted(budget));
        }
        finally
        {
            foreach (var lease in held) lease.Dispose();
            await Drain(queued);
        }
        Assert.Equal(0, Admitted(budget));
        var reusable = Enumerable.Range(0, workers).Select(_ => budget.Acquire()).ToArray();
        foreach (var lease in reusable) lease.Dispose();
        Assert.Equal(0, Admitted(budget));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ALeaseCannotMintExtraWorkersWhenDisposedTwice(int workers)
    {
        var budget = new L12PasswordWorkBudget(workers, 0, TimeSpan.Zero);
        var first = Enumerable.Range(0, workers).Select(_ => budget.Acquire()).ToArray();
        foreach (var lease in first) { lease.Dispose(); lease.Dispose(); }
        Assert.Equal(0, Admitted(budget));
        var second = Enumerable.Range(0, workers).Select(_ => budget.Acquire()).ToArray();
        try
        {
            Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
            Assert.Equal(workers, Admitted(budget));
        }
        finally { foreach (var lease in second) lease.Dispose(); }
        Assert.Equal(0, Admitted(budget));
    }

    [Fact]
    public void SharedBudgetKeepsCpuAndQueueLimitsWithApprovedThreeSecondWait()
    {
        var budget = L12PasswordWorkBudget.Shared;
        Assert.Equal(Math.Clamp(Environment.ProcessorCount / 2, 1, 2) + 8,
            (int)typeof(L12PasswordWorkBudget).GetField("_maximumAdmitted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(budget)!);
        Assert.Equal(TimeSpan.FromSeconds(3),
            (TimeSpan)typeof(L12PasswordWorkBudget).GetField("_maximumWait", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(budget)!);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(3000)]
    public void ApprovedWaitRangeDoesNotIncreaseAdmission(int waitMs)
    {
        var budget = new L12PasswordWorkBudget(2, 8, TimeSpan.FromMilliseconds(waitMs));
        Assert.Equal(10, (int)typeof(L12PasswordWorkBudget).GetField("_maximumAdmitted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(budget)!);
        Assert.Equal(0, Admitted(budget));
    }

    [Fact]
    public async Task EightQueuedRequestsSurviveOldCutoffWithoutIncreasingWorkers()
    {
        var budget = new L12PasswordWorkBudget(2, 8, TimeSpan.FromSeconds(3));
        var held = new[] { budget.Acquire(), budget.Acquire() };
        var active = 0;
        var maximumActive = 0;
        async Task Work()
        {
            using var lease = await budget.AcquireAsync();
            var current = Interlocked.Increment(ref active);
            int previous;
            do { previous = Volatile.Read(ref maximumActive); }
            while (current > previous && Interlocked.CompareExchange(ref maximumActive, current, previous) != previous);
            try { await Task.Delay(5); }
            finally { Interlocked.Decrement(ref active); }
        }
        var waiting = Enumerable.Range(0, 8).Select(_ => Work()).ToArray();
        try
        {
            Assert.Equal(10, Admitted(budget));
            await Task.Delay(350);
            Assert.All(waiting, task => Assert.False(task.IsCompleted));
            Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
            await Assert.ThrowsAsync<L12AuthenticationBusyException>(async () => await budget.AcquireAsync());
        }
        finally
        {
            foreach (var lease in held) lease.Dispose();
            await Task.WhenAll(waiting).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.InRange(maximumActive, 1, 2);
        Assert.Equal(0, Admitted(budget));
        using var first = budget.Acquire();
        using var second = budget.Acquire();
    }

    [Fact]
    public async Task SynchronousConsumerUsesTheSameExtendedBoundedWait()
    {
        var budget = new L12PasswordWorkBudget(1, 1, TimeSpan.FromSeconds(3));
        var held = budget.Acquire();
        var waiting = Run(() => { using var lease = budget.Acquire(); return true; });
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Admitted(budget) == 2, TimeSpan.FromMilliseconds(500)));
            await Task.Delay(350);
            Assert.False(waiting.IsCompleted);
        }
        finally { held.Dispose(); await Drain(waiting); }
        Assert.True(await waiting);
        Assert.Equal(0, Admitted(budget));
    }

    [Fact]
    public async Task AsyncAdmissionReturnsIncompleteTasksAndSharesTheExactTwoPlusEightLimitWithSync()
    {
        var budget = new L12PasswordWorkBudget(2, 8, TimeSpan.FromMilliseconds(250));
        var held = new[] { budget.Acquire(), budget.Acquire() };
        var queued = new List<Task<IDisposable>>();
        using var cancellation = new CancellationTokenSource();
        try
        {
            for (var index = 0; index < 8; index++)
            {
                var attempt = AcquireAsync(budget, cancellation.Token);
                queued.Add(attempt);
                Assert.False(attempt.IsCompleted, "Admission must return to its caller while both real workers remain occupied.");
                Assert.Equal(3 + index, Admitted(budget));
            }
            Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
            await Assert.ThrowsAsync<L12AuthenticationBusyException>(async () => await AcquireAsync(budget));
            Assert.Equal(10, Admitted(budget));
            cancellation.Cancel();
            foreach (var attempt in queued)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await attempt);
            Assert.Equal(2, Admitted(budget));
        }
        finally
        {
            cancellation.Cancel();
            foreach (var lease in held) lease.Dispose();
            foreach (var attempt in queued) await DrainLease(attempt);
        }
        Assert.Equal(0, Admitted(budget));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncCancelledOrTimedOutWaiterCannotReleaseASynchronousOwnersWorker(bool cancel)
    {
        var budget = new L12PasswordWorkBudget(2, 8, TimeSpan.FromMilliseconds(250));
        var held = new[] { budget.Acquire(), budget.Acquire() };
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable>? attempt = null;
        try
        {
            attempt = AcquireAsync(budget, cancellation.Token);
            Assert.False(attempt.IsCompleted);
            Assert.Equal(3, Admitted(budget));
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await attempt);
            }
            else await Assert.ThrowsAsync<L12AuthenticationBusyException>(async () => await attempt);
            Assert.Equal(2, Admitted(budget));
            Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
            Assert.Equal(2, Admitted(budget));
        }
        finally
        {
            cancellation.Cancel();
            foreach (var lease in held) lease.Dispose();
            if (attempt is not null) await DrainLease(attempt);
        }
        Assert.Equal(0, Admitted(budget));
        using var first = await AcquireAsync(budget);
        using var second = budget.Acquire();
        Assert.Equal(2, Admitted(budget));
    }

    [Fact]
    public async Task AsyncLeaseHandoffAndDoubleDisposeNeverMintAnExtraWorker()
    {
        var budget = new L12PasswordWorkBudget(2, 8, TimeSpan.FromMilliseconds(250));
        var first = budget.Acquire();
        var second = budget.Acquire();
        Task<IDisposable>? queued = null;
        IDisposable? granted = null;
        try
        {
            queued = AcquireAsync(budget);
            Assert.False(queued.IsCompleted);
            first.Dispose();
            granted = await queued.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, Admitted(budget));
            granted.Dispose(); granted.Dispose();
            using var replacement = budget.Acquire();
            Assert.Equal(2, Admitted(budget));
            await Assert.ThrowsAsync<L12AuthenticationBusyException>(async () => await AcquireAsync(budget));
            Assert.Equal(2, Admitted(budget));
        }
        finally
        {
            first.Dispose(); second.Dispose(); granted?.Dispose();
            if (queued is not null) await DrainLease(queued);
        }
        Assert.Equal(0, Admitted(budget));
    }

    [Fact]
    public async Task PreCancelledAsyncAdmissionDoesNotConsumeAnyCapacity()
    {
        var budget = new L12PasswordWorkBudget(2, 8, TimeSpan.FromMilliseconds(250));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await AcquireAsync(budget, cancellation.Token));
        Assert.Equal(0, Admitted(budget));
        using var first = budget.Acquire();
        using var second = await AcquireAsync(budget);
        Assert.Equal(2, Admitted(budget));
    }

    private static Task<IDisposable> AcquireAsync(L12PasswordWorkBudget budget, CancellationToken cancellation = default)
    {
        // Reflection keeps the old product compilable, so the initial RED is an
        // executable missing-API assertion rather than a compiler error.
        var method = typeof(L12PasswordWorkBudget).GetMethod("AcquireAsync", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(CancellationToken)], null);
        Assert.True(method is not null, "Missing shared-budget AcquireAsync(CancellationToken): queued admission still has no nonblocking entry.");
        return method!.CreateDelegate<Func<CancellationToken, Task<IDisposable>>>(budget)(cancellation);
    }

    private static async Task DrainLease(Task<IDisposable> task)
    {
        try { (await task.WaitAsync(TimeSpan.FromSeconds(10))).Dispose(); }
        catch (Exception) when (task.IsCompleted) { }
    }

    private static int Admitted(L12PasswordWorkBudget budget) => (int)typeof(L12PasswordWorkBudget)
        .GetField("_admitted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(budget)!;
    private static Task<T> Run<T>(Func<T> work) => Task.Factory.StartNew(work,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private static async Task Drain(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception) when (task.IsCompleted) { }
    }
}
