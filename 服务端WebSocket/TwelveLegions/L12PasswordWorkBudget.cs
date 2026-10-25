namespace TwelveLegions.Server;

internal sealed class L12AuthenticationBusyException : Exception
{
    internal L12AuthenticationBusyException() : base("Authentication computation is busy.") { }
    internal const int RetryAfterSeconds = 1;
}

/// <summary>Process-wide CPU admission, separate from persistent failed-login throttles.</summary>
internal sealed class L12PasswordWorkBudget
{
    internal static L12PasswordWorkBudget Shared { get; } = new(
        Math.Clamp(Environment.ProcessorCount / 2, 1, 2), 8, TimeSpan.FromMilliseconds(250));

    private readonly SemaphoreSlim _workers;
    private readonly int _maximumAdmitted;
    private readonly TimeSpan _maximumWait;
    private int _admitted;

    internal L12PasswordWorkBudget(int workers, int queued, TimeSpan maximumWait)
    {
        if (workers < 1 || queued < 0 || maximumWait < TimeSpan.Zero
            || maximumWait > TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(workers));
        _workers = new(workers, workers);
        _maximumAdmitted = checked(workers + queued);
        _maximumWait = maximumWait;
    }

    internal IDisposable Acquire()
    {
        if (Interlocked.Increment(ref _admitted) > _maximumAdmitted)
        {
            Interlocked.Decrement(ref _admitted);
            throw new L12AuthenticationBusyException();
        }
        var acquired = false;
        try
        {
            acquired = _workers.Wait(_maximumWait);
            if (!acquired) throw new L12AuthenticationBusyException();
            return new Lease(this);
        }
        finally
        {
            if (!acquired) Interlocked.Decrement(ref _admitted);
        }
    }

    private sealed class Lease(L12PasswordWorkBudget owner) : IDisposable
    {
        private L12PasswordWorkBudget? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            current._workers.Release();
            Interlocked.Decrement(ref current._admitted);
        }
    }
}
