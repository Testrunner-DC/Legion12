using System.Diagnostics;

namespace TwelveLegions.Server;

/// <summary>
/// 单个 WebSocket 连接的有界发送队列。只有连续且尚未发送的普通对局快照可以原位合并；
/// 任何关键消息都会切断合并窗口，从而保持 Prompt、拒绝、终局和恢复边界的严格顺序。
/// </summary>
internal sealed class L12OutboundConnection : IAsyncDisposable
{
    internal const int MaximumQueuedMessages = 32;

    private sealed class PendingMessage
    {
        internal PendingMessage(object payload, bool replaceableGameState,
            TaskCompletionSource? delivered = null, L12SyntheticTransportSample? diagnostic = null)
        {
            Payload = payload;
            ReplaceableGameState = replaceableGameState;
            Delivered = delivered;
            Diagnostic = diagnostic;
        }

        internal object Payload { get; }
        internal bool ReplaceableGameState { get; }
        internal TaskCompletionSource? Delivered { get; }
        internal L12SyntheticTransportSample? Diagnostic { get; }
    }

    private readonly object _gate = new();
    private readonly LinkedList<PendingMessage> _queue = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Func<object, CancellationToken, Task> _sender;
    private readonly Func<object, L12SyntheticTransportSample?, CancellationToken, Task>? _timedSender;
    private readonly Action<Exception>? _onFault;
    private readonly TimeSpan? _sendTimeout;
    private readonly CancellationTokenSource _stop = new();
    private LinkedListNode<PendingMessage>? _replaceableTail;
    private readonly Task _senderLoop;
    private bool _completed;
    private long _mergedCount;
    private long _droppedCount;
    private int _maximumDepth;

    internal L12OutboundConnection(Func<object, CancellationToken, Task> sender,
        Action<Exception>? onFault = null, TimeSpan? sendTimeout = null)
    {
        _sender = sender;
        _onFault = onFault;
        _sendTimeout = sendTimeout;
        _senderLoop = Task.Run(SenderLoopAsync);
    }

    internal L12OutboundConnection(
        Func<object, L12SyntheticTransportSample?, CancellationToken, Task> sender,
        Action<Exception>? onFault = null, TimeSpan? sendTimeout = null)
    {
        _sender = (_, _) => Task.CompletedTask;
        _timedSender = sender;
        _onFault = onFault;
        _sendTimeout = sendTimeout;
        _senderLoop = Task.Run(SenderLoopAsync);
    }

    internal int QueuedCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    internal int MaximumObservedDepth => Volatile.Read(ref _maximumDepth);
    internal long MergedCount => Interlocked.Read(ref _mergedCount);
    internal long DroppedCount => Interlocked.Read(ref _droppedCount);

    internal bool TryEnqueue(object payload, bool replaceableGameState = false)
        => TryEnqueueCore(new PendingMessage(payload, replaceableGameState));

    internal bool TryEnqueue(object payload, bool replaceableGameState,
        L12SyntheticTransportSample? diagnostic)
        => TryEnqueueCore(new PendingMessage(payload, replaceableGameState, diagnostic: diagnostic));

    internal async Task<bool> EnqueueAndWaitAsync(object payload, CancellationToken cancellationToken)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueueCore(new PendingMessage(payload, replaceableGameState: false, delivered))) return false;
        await delivered.Task.WaitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Atomically fences future output, discards every frame that has not started sending, and lets
    /// the single existing sender deliver one final transport notice. A frame already owned by the
    /// sender cannot be recalled and remains ordered before the terminal notice.
    /// </summary>
    internal async Task<bool> EnqueueTerminalAndCompleteAsync(object payload,
        CancellationToken cancellationToken)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_completed) return false;

            _completed = true;
            _replaceableTail = null;
            foreach (var queued in _queue)
            {
                queued.Delivered?.TrySetCanceled();
                queued.Diagnostic?.Discarded();
            }
            _queue.Clear();
            _queue.AddLast(new PendingMessage(payload, replaceableGameState: false, delivered));
            UpdateMaximumDepth(_queue.Count);

            // One permit exposes the terminal frame; the second lets the sender observe the
            // completed empty queue. Permits left by discarded frames are harmless.
            _signal.Release(2);
        }

        await delivered.Task.WaitAsync(cancellationToken);
        await _senderLoop.WaitAsync(cancellationToken);
        return true;
    }

    private bool TryEnqueueCore(PendingMessage message)
    {
        lock (_gate)
        {
            if (_completed)
            {
                message.Diagnostic?.QueueRejected();
                return false;
            }

            if (message.ReplaceableGameState && _replaceableTail is not null)
            {
                _replaceableTail.Value.Delivered?.TrySetCanceled();
                _replaceableTail.Value.Diagnostic?.Discarded();
                _replaceableTail.Value = message;
                Interlocked.Increment(ref _mergedCount);
                message.Diagnostic?.OutboundEnqueued(Stopwatch.GetTimestamp(), _queue.Count);
                return true;
            }

            // 关键消息不得被后续快照跨越。
            if (!message.ReplaceableGameState) _replaceableTail = null;

            var reusedQueueSlot = false;
            if (_queue.Count >= MaximumQueuedMessages)
            {
                if (message.ReplaceableGameState)
                {
                    Interlocked.Increment(ref _droppedCount);
                    message.Diagnostic?.QueueRejected();
                    return false;
                }

                var discardable = _queue.First;
                while (discardable is not null && !discardable.Value.ReplaceableGameState)
                    discardable = discardable.Next;
                if (discardable is null)
                {
                    Interlocked.Increment(ref _droppedCount);
                    message.Diagnostic?.QueueRejected();
                    return false;
                }
                if (ReferenceEquals(_replaceableTail, discardable)) _replaceableTail = null;
                discardable.Value.Delivered?.TrySetCanceled();
                discardable.Value.Diagnostic?.Discarded();
                _queue.Remove(discardable);
                Interlocked.Increment(ref _droppedCount);
                reusedQueueSlot = true;
            }

            var node = _queue.AddLast(message);
            if (message.ReplaceableGameState) _replaceableTail = node;
            UpdateMaximumDepth(_queue.Count);
            message.Diagnostic?.OutboundEnqueued(Stopwatch.GetTimestamp(), _queue.Count);
            if (!reusedQueueSlot) _signal.Release();
            return true;
        }
    }

    internal async Task CompleteAsync(bool drain = true)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _completed = true;
                _replaceableTail = null;
                if (!drain)
                {
                    foreach (var message in _queue)
                    {
                        message.Delivered?.TrySetCanceled();
                        message.Diagnostic?.Discarded();
                    }
                    _queue.Clear();
                    _stop.Cancel();
                }
                _signal.Release();
            }
        }
        try { await _senderLoop; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task SenderLoopAsync()
    {
        while (true)
        {
            await _signal.WaitAsync(_stop.Token);
            PendingMessage? message = null;
            var remainingDepth = 0;
            lock (_gate)
            {
                if (_queue.First is { } first)
                {
                    message = first.Value;
                    if (ReferenceEquals(_replaceableTail, first)) _replaceableTail = null;
                    _queue.RemoveFirst();
                    remainingDepth = _queue.Count;
                }
                else if (_completed)
                {
                    return;
                }
            }

            if (message is null) continue;
            message.Diagnostic?.OutboundDequeued(Stopwatch.GetTimestamp(), remainingDepth);
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                if (_sendTimeout is { } timeout) sendTimeout.CancelAfter(timeout);
                if (_timedSender is null)
                    await _sender(message.Payload, sendTimeout.Token);
                else
                    await _timedSender(message.Payload, message.Diagnostic, sendTimeout.Token);
                message.Delivered?.TrySetResult();
            }
            catch (Exception error)
            {
                message.Delivered?.TrySetException(error);
                message.Diagnostic?.SendFault();
                lock (_gate)
                {
                    _completed = true;
                    _replaceableTail = null;
                    foreach (var queued in _queue)
                    {
                        queued.Delivered?.TrySetException(error);
                        queued.Diagnostic?.Discarded();
                    }
                    _queue.Clear();
                }
                try { _onFault?.Invoke(error); }
                catch (Exception callbackError)
                {
                    Console.Error.WriteLine($"WebSocket 发送隔离回调失败：{callbackError.Message}");
                }
                return;
            }
        }
    }

    private void UpdateMaximumDepth(int depth)
    {
        var current = Volatile.Read(ref _maximumDepth);
        while (depth > current)
        {
            var observed = Interlocked.CompareExchange(ref _maximumDepth, depth, current);
            if (observed == current) return;
            current = observed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CompleteAsync(drain: false);
        _signal.Dispose();
        _stop.Dispose();
    }
}
