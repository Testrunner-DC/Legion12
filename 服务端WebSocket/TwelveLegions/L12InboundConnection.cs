using System.Diagnostics;

namespace TwelveLegions.Server;

internal readonly record struct L12InboundQueueTiming(
    long EnqueuedAt,
    long DequeuedAt,
    int AcceptedDepth,
    long AcceptedRetainedBytes,
    int RemainingQueuedMessages,
    long ReceiveCompletedAt = 0,
    long LeaseStartedAt = 0,
    long LeaseCompletedAt = 0);

internal enum L12InboundEnqueueResult
{
    Accepted,
    DeploymentDraining,
    MessageLimit,
    RetainedByteLimit,
    Completed,
}

/// <summary>
/// A single-consumer business-message queue for one authenticated WebSocket. The retained-byte
/// budget follows the UTF-16 string that is actually held in memory, including the item currently
/// executing. It is deliberately independent from the outbound snapshot queue.
/// </summary>
internal sealed class L12InboundConnection : IAsyncDisposable
{
    // The transport still accepts one UTF-8 frame up to 1 MiB. An all-ASCII maximum frame occupies
    // roughly 2 MiB as a managed string, so this cap preserves that one-frame compatibility while
    // preventing several maximum frames from accumulating on one connection.
    internal const int MaximumRetainedMessages = 8;
    internal const long MaximumRetainedBytes = 2_228_224;
    // PendingMessage remains within this conservative x64 allowance after the three diagnostic
    // timestamps: 16-byte header + two references + five Int64s + three Int32s <= 88 bytes before
    // alignment. Keep the public retained-byte budget unchanged.
    private const int RetainedItemOverhead = 96;

    private sealed class PendingMessage
    {
        private int _released;

        internal PendingMessage(string json, int retainedBytes, L12DeploymentDrainLease? lease,
            long enqueuedAt, int acceptedDepth, long acceptedRetainedBytes,
            long receiveCompletedAt, long leaseStartedAt, long leaseCompletedAt)
        {
            Json = json;
            RetainedBytes = retainedBytes;
            Lease = lease;
            EnqueuedAt = enqueuedAt;
            AcceptedDepth = acceptedDepth;
            AcceptedRetainedBytes = acceptedRetainedBytes;
            ReceiveCompletedAt = receiveCompletedAt;
            LeaseStartedAt = leaseStartedAt;
            LeaseCompletedAt = leaseCompletedAt;
        }

        internal string Json { get; }
        internal int RetainedBytes { get; }
        internal L12DeploymentDrainLease? Lease { get; }
        internal long EnqueuedAt { get; }
        internal int AcceptedDepth { get; }
        internal long AcceptedRetainedBytes { get; }
        internal long ReceiveCompletedAt { get; }
        internal long LeaseStartedAt { get; }
        internal long LeaseCompletedAt { get; }

        internal bool TryRelease() => Interlocked.Exchange(ref _released, 1) == 0;
    }

    private readonly object _gate = new();
    private readonly Queue<PendingMessage> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Func<string, Task> _handler;
    private readonly Func<string, L12InboundQueueTiming, Task>? _timedHandler;
    private readonly Action<Exception>? _onFault;
    private readonly Func<string, L12DeploymentDrainLease?>? _leaseAcquirer;
    private readonly Task _consumer;
    private bool _accepting = true;
    private bool _drain = true;
    private int _retainedMessages;
    private long _retainedBytes;
    private int _maximumObservedMessages;
    private long _maximumObservedBytes;

    internal L12InboundConnection(Func<string, Task> handler, Action<Exception>? onFault = null,
        Func<string, L12DeploymentDrainLease?>? leaseAcquirer = null)
    {
        _handler = handler;
        _onFault = onFault;
        _leaseAcquirer = leaseAcquirer;
        _consumer = Task.Run(ConsumeAsync);
    }

    internal L12InboundConnection(Func<string, L12InboundQueueTiming, Task> handler,
        Action<Exception>? onFault = null,
        Func<string, L12DeploymentDrainLease?>? leaseAcquirer = null)
    {
        _handler = _ => Task.CompletedTask;
        _timedHandler = handler;
        _onFault = onFault;
        _leaseAcquirer = leaseAcquirer;
        _consumer = Task.Run(ConsumeAsync);
    }

    internal int RetainedMessages
    {
        get { lock (_gate) return _retainedMessages; }
    }

    internal bool IsAccepting
    {
        get { lock (_gate) return _accepting; }
    }

    internal long RetainedBytes
    {
        get { lock (_gate) return _retainedBytes; }
    }

    internal int MaximumObservedMessages => Volatile.Read(ref _maximumObservedMessages);
    internal long MaximumObservedBytes => Interlocked.Read(ref _maximumObservedBytes);

    internal L12InboundEnqueueResult TryEnqueue(string json) => TryEnqueue(json, 0);

    internal L12InboundEnqueueResult TryEnqueue(string json, long receiveCompletedAt)
    {
        ArgumentNullException.ThrowIfNull(json);
        var retainedBytes = checked(RetainedItemOverhead + json.Length * sizeof(char));

        // Preserve the legacy Completed result without acquiring a deployment lease after the
        // connection has already been fenced. The second check below closes this observation race.
        lock (_gate)
        {
            if (!_accepting) return L12InboundEnqueueResult.Completed;
        }

        L12DeploymentDrainLease? lease = null;
        var leaseStartedAt = 0L;
        var leaseCompletedAt = 0L;
        if (_leaseAcquirer is not null)
        {
            if (_timedHandler is not null && receiveCompletedAt > 0)
                leaseStartedAt = Stopwatch.GetTimestamp();
            lease = _leaseAcquirer(json);
            if (_timedHandler is not null && receiveCompletedAt > 0)
                leaseCompletedAt = Stopwatch.GetTimestamp();
            if (lease is null || !lease.IsOriginal || !lease.IsActive)
            {
                lease?.Dispose();
                return L12InboundEnqueueResult.DeploymentDraining;
            }
        }

        L12InboundEnqueueResult result;
        lock (_gate)
        {
            if (!_accepting) result = L12InboundEnqueueResult.Completed;
            else if (_retainedMessages >= MaximumRetainedMessages)
                result = L12InboundEnqueueResult.MessageLimit;
            else if (retainedBytes > MaximumRetainedBytes - _retainedBytes)
                result = L12InboundEnqueueResult.RetainedByteLimit;
            else
            {
                var enqueuedAt = _timedHandler is null ? 0 : Stopwatch.GetTimestamp();
                var acceptedDepth = _retainedMessages + 1;
                var acceptedRetainedBytes = _retainedBytes + retainedBytes;
                _queue.Enqueue(new PendingMessage(json, retainedBytes, lease, enqueuedAt,
                    acceptedDepth, acceptedRetainedBytes, receiveCompletedAt,
                    leaseStartedAt, leaseCompletedAt));
                lease = null; // PendingMessage now owns it.
                _retainedMessages = acceptedDepth;
                _retainedBytes = acceptedRetainedBytes;
                UpdateMaximum(ref _maximumObservedMessages, _retainedMessages);
                UpdateMaximum(ref _maximumObservedBytes, _retainedBytes);
                _signal.Release();
                result = L12InboundEnqueueResult.Accepted;
            }
        }
        lease?.Dispose();
        return result;
    }

    /// <summary>
    /// Fences new input and removes work that has not begun. The currently executing command is
    /// intentionally not cancelled: it may already own a room gate or a durability transaction.
    /// </summary>
    internal void StopAcceptingAndCancelPending()
    {
        List<PendingMessage>? cancelled = null;
        lock (_gate)
        {
            _accepting = false;
            _drain = false;
            while (_queue.TryDequeue(out var pending))
            {
                ReleaseRetainedLocked(pending);
                (cancelled ??= []).Add(pending);
            }
            _signal.Release();
        }
        ReleaseLeases(cancelled);
    }

    internal async Task CompleteAsync(bool drain)
    {
        List<PendingMessage>? cancelled = null;
        lock (_gate)
        {
            _accepting = false;
            _drain = drain;
            if (!drain)
            {
                while (_queue.TryDequeue(out var pending))
                {
                    ReleaseRetainedLocked(pending);
                    (cancelled ??= []).Add(pending);
                }
            }
            _signal.Release();
        }
        ReleaseLeases(cancelled);
        await _consumer;
    }

    private async Task ConsumeAsync()
    {
        while (true)
        {
            await _signal.WaitAsync();
            PendingMessage? pending;
            var remainingQueuedMessages = 0;
            lock (_gate)
            {
                if (_queue.TryDequeue(out pending)) remainingQueuedMessages = _queue.Count;
                else if (!_accepting) return;
                else continue;
            }

            IDisposable? executionScope = null;
            try
            {
                if (pending.Lease?.Kind == L12DeploymentLeaseKind.Admission)
                    executionScope = pending.Lease.EnterExecutionScope();
                if (_timedHandler is null)
                    await _handler(pending.Json);
                else
                    await _timedHandler(pending.Json, new L12InboundQueueTiming(
                        pending.EnqueuedAt, Stopwatch.GetTimestamp(), pending.AcceptedDepth,
                        pending.AcceptedRetainedBytes, remainingQueuedMessages,
                        pending.ReceiveCompletedAt, pending.LeaseStartedAt,
                        pending.LeaseCompletedAt));
            }
            catch (Exception error)
            {
                StopAcceptingAndCancelPending();
                try { _onFault?.Invoke(error); }
                catch (Exception callbackError)
                {
                    Console.Error.WriteLine($"WebSocket 入站隔离回调失败：{callbackError.Message}");
                }
                return;
            }
            finally
            {
                try { executionScope?.Dispose(); }
                finally { ReleasePending(pending); }
            }

            lock (_gate)
            {
                if (!_accepting && (!_drain || _queue.Count == 0)) return;
            }
        }
    }

    private void ReleaseRetainedLocked(PendingMessage pending)
    {
        if (!pending.TryRelease()) return;
        _retainedMessages--;
        _retainedBytes -= pending.RetainedBytes;
        if (_retainedMessages < 0 || _retainedBytes < 0)
            throw new InvalidOperationException("WebSocket 入站队列容量记账失衡");
    }

    private void ReleasePending(PendingMessage pending)
    {
        var releaseLease = false;
        lock (_gate)
        {
            if (!pending.TryRelease()) return;
            _retainedMessages--;
            _retainedBytes -= pending.RetainedBytes;
            if (_retainedMessages < 0 || _retainedBytes < 0)
                throw new InvalidOperationException("WebSocket 入站队列容量记账失衡");
            releaseLease = true;
        }
        if (releaseLease) pending.Lease?.Dispose();
    }

    private static void ReleaseLeases(List<PendingMessage>? messages)
    {
        if (messages is null) return;
        foreach (var pending in messages) pending.Lease?.Dispose();
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    public async ValueTask DisposeAsync() => await CompleteAsync(drain: false);
}
