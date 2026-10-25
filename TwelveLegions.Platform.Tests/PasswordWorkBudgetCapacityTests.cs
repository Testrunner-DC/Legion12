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
    [InlineData(1, 0, 1001)]
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
