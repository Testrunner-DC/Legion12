namespace TwelveLegions.Server;

/// <summary>
/// 单个 WebSocket 连接的有界发送队列。只有连续且尚未发送的普通对局快照可以原位合并；
/// 任何关键消息都会切断合并窗口，从而保持 Prompt、拒绝、终局和恢复边界的严格顺序。
/// </summary>
internal sealed class L12OutboundConnection : IAsyncDisposable
{
    internal const int MaximumQueuedMessages = 32;

    private sealed record PendingMessage(
        object Payload,
        bool ReplaceableGameState,
        TaskCompletionSource? Delivered = null);

    private readonly object _gate = new();
    private readonly LinkedList<PendingMessage> _queue = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Func<object, CancellationToken, Task> _sender;
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

    internal int QueuedCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    internal int MaximumObservedDepth => Volatile.Read(ref _maximumDepth);
    internal long MergedCount => Interlocked.Read(ref _mergedCount);
    internal long DroppedCount => Interlocked.Read(ref _droppedCount);

    internal bool TryEnqueue(object payload, bool replaceableGameState = false)
        => TryEnqueueCore(new PendingMessage(payload, replaceableGameState));

    internal async Task<bool> EnqueueAndWaitAsync(object payload, CancellationToken cancellationToken)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueueCore(new PendingMessage(payload, ReplaceableGameState: false, delivered))) return false;
        await delivered.Task.WaitAsync(cancellationToken);
        return true;
    }

    private bool TryEnqueueCore(PendingMessage message)
    {
        lock (_gate)
        {
            if (_completed) return false;

            if (message.ReplaceableGameState && _replaceableTail is not null)
            {
                _replaceableTail.Value.Delivered?.TrySetCanceled();
                _replaceableTail.Value = message;
                Interlocked.Increment(ref _mergedCount);
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
                    return false;
                }

                var discardable = _queue.First;
                while (discardable is not null && !discardable.Value.ReplaceableGameState)
                    discardable = discardable.Next;
                if (discardable is null)
                {
                    Interlocked.Increment(ref _droppedCount);
                    return false;
                }
                if (ReferenceEquals(_replaceableTail, discardable)) _replaceableTail = null;
                discardable.Value.Delivered?.TrySetCanceled();
                _queue.Remove(discardable);
                Interlocked.Increment(ref _droppedCount);
                reusedQueueSlot = true;
            }

            var node = _queue.AddLast(message);
            if (message.ReplaceableGameState) _replaceableTail = node;
            UpdateMaximumDepth(_queue.Count);
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
                    foreach (var message in _queue) message.Delivered?.TrySetCanceled();
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
            lock (_gate)
            {
                if (_queue.First is { } first)
                {
                    message = first.Value;
                    if (ReferenceEquals(_replaceableTail, first)) _replaceableTail = null;
                    _queue.RemoveFirst();
                }
                else if (_completed)
                {
                    return;
                }
            }

            if (message is null) continue;
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                if (_sendTimeout is { } timeout) sendTimeout.CancelAfter(timeout);
                await _sender(message.Payload, sendTimeout.Token);
                message.Delivered?.TrySetResult();
            }
            catch (Exception error)
            {
                message.Delivered?.TrySetException(error);
                lock (_gate)
                {
                    _completed = true;
                    _replaceableTail = null;
                    foreach (var queued in _queue) queued.Delivered?.TrySetException(error);
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
