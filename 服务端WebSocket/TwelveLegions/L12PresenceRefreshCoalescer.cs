namespace TwelveLegions.Server;

internal readonly record struct L12PresenceRefreshBatch(
    long Revision,
    bool AllAccounts,
    IReadOnlyList<string> AccountIds);

/// <summary>
/// Coalesces presence invalidations before the personalized snapshots are projected. The worker is
/// deliberately independent from the per-connection reliable outbound queues: it only bounds how
/// often a burst is projected, and never makes a presence frame replaceable.
/// </summary>
internal sealed class L12PresenceRefreshCoalescer : IAsyncDisposable
{
    internal const int MaximumTargetedAccounts = 128;
    internal static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(25);

    private enum Lifecycle
    {
        Created,
        Running,
        Stopping,
        Stopped,
        Disposed,
    }

    private readonly object _gate = new();
    private readonly Func<L12PresenceRefreshBatch, CancellationToken, Task> _emit;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<Exception>? _onEmitFailure;
    private readonly TimeSpan _window;
    private readonly HashSet<string> _pendingAccountIds = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource _requestAvailable = NewSignal();
    private Lifecycle _lifecycle;
    private CancellationTokenSource? _delayCancellation;
    private Task? _worker;
    private Task? _stopTask;
    private Task? _disposeTask;
    private bool _disposeRequested;
    private bool _hasPending;
    private bool _pendingAllAccounts;
    private long _pendingRevision;

    internal L12PresenceRefreshCoalescer(
        Func<L12PresenceRefreshBatch, CancellationToken, Task> emit,
        TimeSpan? window = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<Exception>? onEmitFailure = null)
    {
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
        _window = window ?? DefaultWindow;
        if (_window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        _delay = delay ?? (static (duration, cancellationToken) => Task.Delay(duration, cancellationToken));
        _onEmitFailure = onEmitFailure;
    }

    internal bool Start()
    {
        lock (_gate)
        {
            if (_lifecycle == Lifecycle.Running) return true;
            if (_lifecycle is Lifecycle.Stopping or Lifecycle.Disposed || _disposeRequested) return false;
            _requestAvailable = NewSignal();
            var cancellation = new CancellationTokenSource();
            _delayCancellation = cancellation;
            _stopTask = null;
            _lifecycle = Lifecycle.Running;
            _worker = Task.Run(() => WorkerAsync(cancellation.Token));
            return true;
        }
    }

    internal bool Request(long revision, IEnumerable<string>? accountIds = null)
    {
        lock (_gate)
            if (_lifecycle != Lifecycle.Running) return false;
        var allAccounts = accountIds is null;
        HashSet<string>? requestedAccounts = null;
        if (!allAccounts)
        {
            requestedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var accountId in accountIds!)
            {
                if (string.IsNullOrWhiteSpace(accountId)) continue;
                requestedAccounts.Add(accountId);
                if (requestedAccounts.Count <= MaximumTargetedAccounts) continue;
                allAccounts = true;
                requestedAccounts = null;
                break;
            }
        }

        lock (_gate)
        {
            if (_lifecycle != Lifecycle.Running) return false;
            if (!allAccounts && requestedAccounts!.Count == 0) return true;
            MergePendingNoLock(revision, allAccounts, requestedAccounts);
            _requestAvailable.TrySetResult();
            return true;
        }
    }

    internal Task StopAsync()
    {
        lock (_gate) return StopNoLock();
    }

    private async Task WorkerAsync(CancellationToken delayCancellationToken)
    {
        while (true)
        {
            Task requestAvailable;
            lock (_gate)
            {
                if (_lifecycle != Lifecycle.Running) return;
                requestAvailable = _requestAvailable.Task;
            }
            await requestAvailable.ConfigureAwait(false);
            lock (_gate)
            {
                if (_lifecycle != Lifecycle.Running) return;
            }

            try
            {
                await _delay(_window, delayCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (delayCancellationToken.IsCancellationRequested)
            {
                return;
            }

            L12PresenceRefreshBatch batch;
            lock (_gate)
            {
                if (_lifecycle != Lifecycle.Running) return;
                if (!_hasPending)
                {
                    _requestAvailable = NewSignal();
                    continue;
                }
                batch = TakePendingNoLock();
                _requestAvailable = NewSignal();
            }

            try
            {
                // Stop cancels only a pending coalescing delay. Once projection starts, let the
                // authoritative batch finish before the worker is joined.
                await _emit(batch, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                var retained = false;
                lock (_gate)
                {
                    if (_lifecycle == Lifecycle.Running)
                    {
                        MergePendingNoLock(batch.Revision, batch.AllAccounts, batch.AccountIds);
                        retained = true;
                    }
                }
                if (retained) ReportEmitFailure(error);
            }
        }
    }

    private void MergePendingNoLock(long revision, bool allAccounts,
        IEnumerable<string>? accountIds)
    {
        _hasPending = true;
        _pendingRevision = Math.Max(_pendingRevision, revision);
        if (_pendingAllAccounts) return;
        if (allAccounts)
        {
            _pendingAllAccounts = true;
            _pendingAccountIds.Clear();
            return;
        }

        foreach (var accountId in accountIds!)
        {
            _pendingAccountIds.Add(accountId);
            if (_pendingAccountIds.Count <= MaximumTargetedAccounts) continue;
            _pendingAllAccounts = true;
            _pendingAccountIds.Clear();
            return;
        }
    }

    private L12PresenceRefreshBatch TakePendingNoLock()
    {
        var batch = new L12PresenceRefreshBatch(
            _pendingRevision,
            _pendingAllAccounts,
            _pendingAllAccounts ? [] : _pendingAccountIds.ToArray());
        _hasPending = false;
        _pendingAllAccounts = false;
        _pendingRevision = 0;
        _pendingAccountIds.Clear();
        return batch;
    }

    private void ReportEmitFailure(Exception error)
    {
        if (_onEmitFailure is null) return;
        try { _onEmitFailure(error); }
        catch (Exception callbackError)
        {
            Console.Error.WriteLine($"在线状态刷新异常回调失败：{callbackError.GetType().Name}");
        }
    }

    private Task StopNoLock()
    {
        if (_stopTask is not null) return _stopTask;
        if (_lifecycle is Lifecycle.Created or Lifecycle.Stopped or Lifecycle.Disposed)
        {
            _lifecycle = _lifecycle == Lifecycle.Disposed ? Lifecycle.Disposed : Lifecycle.Stopped;
            return _stopTask = Task.CompletedTask;
        }

        _lifecycle = Lifecycle.Stopping;
        var cancellation = _delayCancellation!;
        cancellation.Cancel();
        _requestAvailable.TrySetResult();
        return _stopTask = CompleteStopAsync(_worker, cancellation);
    }

    private async Task CompleteStopAsync(Task? worker, CancellationTokenSource cancellation)
    {
        try
        {
            if (worker is not null) await worker.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _lifecycle = Lifecycle.Stopped;
                _worker = null;
                if (ReferenceEquals(_delayCancellation, cancellation)) _delayCancellation = null;
                _hasPending = false;
                _pendingAllAccounts = false;
                _pendingRevision = 0;
                _pendingAccountIds.Clear();
            }
            cancellation.Dispose();
        }
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task DisposeCoreAsync(Task stopTask)
    {
        await stopTask.ConfigureAwait(false);
        lock (_gate) _lifecycle = Lifecycle.Disposed;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposeRequested = true;
            _disposeTask = DisposeCoreAsync(StopNoLock());
            return new ValueTask(_disposeTask);
        }
    }
}
