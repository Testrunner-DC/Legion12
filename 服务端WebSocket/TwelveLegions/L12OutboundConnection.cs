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
    // Lazy and deliberately unused by production callers until the separate socket bridge is reviewed.
    private ChatLane? _chatLane;

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
        var admitted = false;
        lock (_gate)
        {
            if (!_completed)
            {
                admitted = true;
                _completed = true;
                _replaceableTail = null;
                foreach (var queued in _queue)
                {
                    queued.Delivered?.TrySetCanceled();
                    queued.Diagnostic?.Discarded();
                }
                _queue.Clear();
                DiscardQueuedChatLocked();
                _queue.AddLast(new PendingMessage(payload, replaceableGameState: false, delivered));
                UpdateMaximumDepth(_queue.Count);

                // One permit exposes the terminal frame; the second lets the sender observe the
                // completed empty queue. Permits left by discarded frames are harmless.
                _signal.Release(2);
            }
        }

        // Admission is fenced under the queue gate first, so a lazy lane cannot be configured
        // behind shutdown. Close the actual start boundary only AFTER releasing that gate.
        // Even a losing/repeated terminal caller must cross this boundary before returning.
        CloseChatStarts();
        if (!admitted) return false;

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
        var cancel = false;
        lock (_gate)
        {
            if (!_completed)
            {
                _completed = true;
                _replaceableTail = null;
                DiscardQueuedChatLocked();
                if (!drain)
                {
                    foreach (var message in _queue)
                    {
                        message.Delivered?.TrySetCanceled();
                        message.Diagnostic?.Discarded();
                    }
                    _queue.Clear();
                    cancel = true;
                }
                _signal.Release();
            }
        }
        CloseChatStarts();
        if (cancel) _stop.Cancel();
        try { await _senderLoop; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task SenderLoopAsync()
    {
        while (true)
        {
            await _signal.WaitAsync(_stop.Token);
            PendingMessage? message = null;
            ChatMessage? chat = null;
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
                else if (_chatLane is { } lane)
                {
                    // Bounded scan; paused items do not generate their own permits or workers.
                    var now = lane.Clock();
                    for (var node = lane.Queue.First; node is not null; node = node.Next)
                    {
                        if (!ChatReady(lane, node.Value, now)) continue;
                        chat = node.Value;
                        lane.Queue.Remove(node);
                        break;
                    }
                }
            }

            if (chat is not null)
            {
                var pendingSend = SendChatAsync(chat);
                // Synthetic-only, nonthrowing allocator probe on this same synchronous sender turn.
                // It observes allocation after a suspended async owner exists; it grants no permission.
                _chatLane!.AsyncOwnerProbe?.Invoke();
                var continueSending = await pendingSend;
                pendingSend = default;
                chat = null;
                if (!continueSending) return;
                ResumeReadyChat();
                continue;
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
                    DiscardQueuedChatLocked();
                }
                CloseChatStarts();
                try { _onFault?.Invoke(error); }
                catch (Exception callbackError)
                {
                    Console.Error.WriteLine($"WebSocket 发送隔离回调失败：{callbackError.Message}");
                }
                return;
            }
        }
    }

    /// <summary>
    /// Explicit synthetic-only configuration. The predicate must be a pure committed-fact read;
    /// clock must be a pure nonthrowing monotonic read. Kickoff must directly start the transport
    /// before returning its ValueTask, with no first await.
    /// B1 proves only this primitive. An actual socket bridge needs its own call-site review.
    /// </summary>
    internal void ConfigureChatLane(L12ChatLaneLimits limits, L12ChatGlobalBudget budget,
        L12ChatPairRegistry registry, Func<long> clock, Func<string, string, bool> freshAdmission,
        Func<ArraySegment<byte>, CancellationToken, ValueTask> kickoff, Action? borrowedBarrier = null,
        Action? asyncOwnerProbe = null)
        => ConfigureChatLaneCore(limits, budget, registry, clock,
            (_, firstId, secondId) => freshAdmission(firstId, secondId), kickoff,
            accountRegistry: null, scopedStart: null, borrowedBarrier, asyncOwnerProbe);

    internal void ConfigureChatLane(L12ChatLaneLimits limits, L12ChatGlobalBudget budget,
        L12ChatPairRegistry registry, Func<long> clock,
        Func<L12ChatDeliveryMetadata, bool> freshAdmission,
        Func<ArraySegment<byte>, CancellationToken, ValueTask> kickoff,
        L12ChatAccountRegistry? accountRegistry,
        Func<L12ChatDeliveryMetadata, Func<L12ChatQueueResult>, L12ChatQueueResult>? scopedStart = null,
        Action? borrowedBarrier = null,
        Action? asyncOwnerProbe = null)
        => ConfigureChatLaneCore(limits, budget, registry, clock,
            (metadata, _, _) => metadata is not null && freshAdmission(metadata), kickoff,
            accountRegistry, scopedStart, borrowedBarrier, asyncOwnerProbe);

    private void ConfigureChatLaneCore(L12ChatLaneLimits limits, L12ChatGlobalBudget budget,
        L12ChatPairRegistry registry, Func<long> clock,
        Func<L12ChatDeliveryMetadata?, string, string, bool> freshAdmission,
        Func<ArraySegment<byte>, CancellationToken, ValueTask> kickoff,
        L12ChatAccountRegistry? accountRegistry,
        Func<L12ChatDeliveryMetadata, Func<L12ChatQueueResult>, L12ChatQueueResult>? scopedStart,
        Action? borrowedBarrier, Action? asyncOwnerProbe)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(freshAdmission);
        ArgumentNullException.ThrowIfNull(kickoff);
        lock (_gate)
        {
            if (_completed || _chatLane is not null) throw new InvalidOperationException("Chat lane unavailable.");
            Volatile.Write(ref _chatLane,
                new ChatLane(limits, budget, registry, clock, freshAdmission, kickoff, accountRegistry,
                    scopedStart, borrowedBarrier, asyncOwnerProbe));
        }
    }

    internal bool HasChatLane { get { lock (_gate) return _chatLane is not null; } }
    internal bool ChatStartsClosed => Volatile.Read(ref _chatLane) is { } lane && Volatile.Read(ref lane.Closed);

    internal L12ChatQueueResult TryEnqueueChat(string senderId, string recipientId,
        ArraySegment<byte> bytes, long expiresAt)
        => TryEnqueueChatCore(senderId, recipientId, metadata: null, bytes, expiresAt);

    internal L12ChatQueueResult TryEnqueueChat(L12ChatDeliveryMetadata metadata,
        ArraySegment<byte> bytes, long expiresAt)
        => TryEnqueueChatCore(metadata.PairFirstId, metadata.PairSecondId, metadata, bytes, expiresAt);

    private L12ChatQueueResult TryEnqueueChatCore(string firstId, string secondId,
        L12ChatDeliveryMetadata? metadata, ArraySegment<byte> bytes, long expiresAt)
    {
        var lane = Volatile.Read(ref _chatLane);
        if (lane is null || bytes.Array is null || bytes.Count == 0) return L12ChatQueueResult.Suppressed;
        if (expiresAt <= lane.Clock()) return L12ChatQueueResult.Expired;
        long charge = (long)bytes.Array.Length + lane.Limits.MetadataBytes;
        if (bytes.Count > lane.Limits.MaximumPacketBytes || charge > lane.Limits.MaximumRetainedBytes)
            return L12ChatQueueResult.QuotaRejected;
        var pair = lane.Registry.TryAcquire(firstId, secondId);
        if (pair is null) return L12ChatQueueResult.QuotaRejected;
        var accounts = metadata is null ? null : lane.AccountRegistry?.TryAcquire(
            metadata.SenderAccountId, metadata.RecipientAccountId);
        if (metadata is not null && lane.AccountRegistry is not null && accounts is null)
        {
            pair.Dispose();
            return L12ChatQueueResult.QuotaRejected;
        }
        long generation;
        lock (pair.Entry.StartGate)
        {
            if (pair.Entry.Pending != 0 || pair.Entry.RecoveryClosed)
            {
                // Release below the pair gate; registry and start gates never nest.
                generation = -1;
            }
            else generation = pair.Entry.Generation;
        }
        if (generation < 0)
        {
            accounts?.Dispose();
            pair.Dispose();
            return L12ChatQueueResult.Paused;
        }
        var reservation = lane.Budget.TryReserve(charge);
        if (reservation is null)
        {
            accounts?.Dispose();
            pair.Dispose();
            return L12ChatQueueResult.QuotaRejected;
        }
        lock (_gate)
        {
            if (_completed || Volatile.Read(ref lane.Closed))
            {
                reservation.Dispose();
                accounts?.Dispose();
                pair.Dispose();
                return L12ChatQueueResult.Suppressed;
            }
            if (lane.RetainedItems >= lane.Limits.MaximumRetainedItems ||
                charge > lane.Limits.MaximumRetainedBytes - lane.RetainedBytes)
            {
                reservation.Dispose();
                accounts?.Dispose();
                pair.Dispose();
                return L12ChatQueueResult.QuotaRejected;
            }
            var authorization = metadata is null ? null : new ChatAuthorization(metadata, accounts);
            var message = new ChatMessage(bytes, authorization, pair, reservation, generation,
                Volatile.Read(ref lane.Epoch), expiresAt, charge);
            lane.Queue.AddLast(message.Node);
            lane.RetainedItems++;
            lane.RetainedBytes += charge;
            _signal.Release();
            return L12ChatQueueResult.AcceptedQueue;
        }
    }

    // Control callers close this gate BEFORE retiring a binding/claim or reporting its fence.
    // Ordinary legacy enqueue never waits for this gate. Shutdown closes admission under _gate,
    // releases _gate, then crosses this start boundary before its control operation can complete.
    internal void CloseChatStarts()
    {
        var lane = Volatile.Read(ref _chatLane);
        if (lane is null) return;
        lock (lane.StartGate)
        {
            lane.Closed = true;
            if (lane.Epoch < long.MaxValue) lane.Epoch++;
        }
        WakeChatLane();
    }

    internal bool WakeChatLane()
    {
        lock (_gate)
        {
            if (_completed || _chatLane is not { } lane || lane.Queue.Count == 0) return false;
            _signal.Release();
            return true;
        }
    }

    internal L12ChatLaneSnapshot ReadChatLaneSnapshot()
    {
        lock (_gate)
        {
            var lane = _chatLane;
            return lane is null ? default : new(lane.Queue.Count, lane.RetainedItems, lane.RetainedBytes,
                Interlocked.Read(ref lane.Started), Interlocked.Read(ref lane.Sent),
                Interlocked.Read(ref lane.Suppressed), Interlocked.Read(ref lane.Expired),
                Interlocked.Read(ref lane.Paused), Interlocked.Read(ref lane.TransportFaults));
        }
    }

    private static bool ChatReady(ChatLane lane, ChatMessage message, long now)
    {
        var pair = message.Pair!.Entry;
        if (Volatile.Read(ref lane.Closed) || message.Epoch != Volatile.Read(ref lane.Epoch) ||
            message.Generation != Volatile.Read(ref pair.Generation) || message.ExpiresAt <= now) return true;
        if (message.Authorization?.Accounts is { } accounts && lane.AccountRegistry is { } accountRegistry
            && !accountRegistry.Ready(accounts)) return false;
        return Volatile.Read(ref pair.Pending) == 0 && !Volatile.Read(ref pair.RecoveryClosed);
    }

    private void ResumeReadyChat()
    {
        lock (_gate)
        {
            if (_completed || _chatLane is not { } lane || lane.Queue.Count == 0 ||
                _signal.CurrentCount != 0) return;
            // An explicit wake is a readiness hint, not a permit for just one message. After a
            // completed low turn, expose remaining ready work with at most one existing-sender
            // permit. The next turn still selects legacy first; entirely paused work stays asleep.
            if (_queue.Count != 0) { _signal.Release(); return; }
            var now = lane.Clock();
            for (var node = lane.Queue.First; node is not null; node = node.Next)
            {
                if (!ChatReady(lane, node.Value, now)) continue;
                _signal.Release();
                return;
            }
        }
    }

    private async ValueTask<bool> SendChatAsync(ChatMessage message)
    {
        var lane = _chatLane!;
        var retained = true;
        try
        {
            // Test barrier is outside every gate and cannot issue a send permit.
            lane.BorrowedBarrier?.Invoke();
            lock (_gate)
            {
                if (_completed) { Interlocked.Increment(ref lane.Suppressed); return true; }
                if (_queue.Count != 0)
                {
                    lane.Queue.AddFirst(message.Node);
                    retained = false;
                    _signal.Release();
                    return true;
                }
            }

            ValueTask wire = default;
            var result = L12ChatQueueResult.Suppressed;
            lock (lane.StartGate)
            {
                L12ChatQueueResult StartCore()
                {
                    var pair = message.Pair!.Entry;
                    lock (pair.StartGate)
                    {
                        if (Volatile.Read(ref _completed) || lane.Closed || message.Epoch != lane.Epoch ||
                            message.Generation != pair.Generation)
                            return L12ChatQueueResult.Suppressed;
                        if (message.ExpiresAt <= lane.Clock()) return L12ChatQueueResult.Expired;
                        if (pair.Pending != 0 || pair.RecoveryClosed) return L12ChatQueueResult.Paused;
                        var allowed = false;
                        try
                        {
                            allowed = lane.FreshAdmission(message.Authorization?.Metadata,
                                pair.FirstId, pair.SecondId);
                        }
                        catch { /* Unreadable authorization is a normal suppression, never an Abort. */ }
                        if (!allowed) return L12ChatQueueResult.Suppressed;
                        L12ChatQueueResult BeginTransport()
                        {
                            // The synchronous invocation, not ValueTask completion, is the start boundary.
                            wire = lane.Kickoff(message.Bytes, _stop.Token);
                            Interlocked.Increment(ref lane.Started);
                            return L12ChatQueueResult.ActuallyStarted;
                        }
                        return message.Authorization?.Metadata is { Scope: L12ChatDeliveryScope.Opponent } metadata
                               && lane.ScopedStart is { } scopedStart
                            ? scopedStart(metadata, BeginTransport)
                            : BeginTransport();
                    }
                }
                result = message.Authorization?.Accounts is { } accounts
                         && lane.AccountRegistry is { } accountRegistry
                    ? accountRegistry.Start(accounts, StartCore)
                    : StartCore();
            }
            if (result == L12ChatQueueResult.Paused)
            {
                Interlocked.Increment(ref lane.Paused);
                lock (_gate)
                {
                    if (!_completed)
                    {
                        lane.Queue.AddFirst(message.Node);
                        retained = false;
                        // Completion may have woken while this item was borrowed. Do not lose that wake.
                        if (ChatReady(lane, message, lane.Clock())) _signal.Release();
                    }
                }
                return true;
            }
            if (result == L12ChatQueueResult.Expired)
            {
                Interlocked.Increment(ref lane.Expired);
                return true;
            }
            if (result != L12ChatQueueResult.ActuallyStarted)
            {
                Interlocked.Increment(ref lane.Suppressed);
                return true;
            }
            // No privacy/quota cancellation of an already started frame, nor a chat-specific timer.
            await wire;
            Interlocked.Increment(ref lane.Sent);
            return true;
        }
        catch (Exception error)
        {
            Interlocked.Increment(ref lane.TransportFaults);
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
                DiscardQueuedChatLocked();
            }
            CloseChatStarts();
            try { _onFault?.Invoke(error); }
            catch (Exception callbackError)
            {
                Console.Error.WriteLine($"WebSocket 发送隔离回调失败：{callbackError.Message}");
            }
            return false;
        }
        finally
        {
            if (retained) ReleaseChat(message);
        }
    }

    // _gate only; no start gate/network is acquired by cleanup or critical enqueue.
    private void DiscardQueuedChatLocked()
    {
        if (_chatLane is not { } lane) return;
        while (lane.Queue.First is { } first)
        {
            lane.Queue.RemoveFirst();
            Interlocked.Increment(ref lane.Suppressed);
            ReleaseChat(first.Value);
        }
    }

    private void ReleaseChat(ChatMessage message)
    {
        if (Interlocked.Exchange(ref message.Released, 1) != 0) return;
        // Clear the actual payload reference before refunding either retained-byte budget.
        message.Bytes = default;
        var pair = message.Pair;
        var accounts = message.Authorization?.Accounts;
        var reservation = message.Reservation;
        message.Pair = null;
        message.Authorization = null;
        message.Reservation = null;
        lock (_gate)
        {
            reservation!.Dispose();
            accounts?.Dispose();
            pair!.Dispose();
            _chatLane!.RetainedItems--;
            _chatLane.RetainedBytes -= message.Charge;
        }
    }

    private sealed class ChatMessage
    {
        internal ChatMessage(ArraySegment<byte> bytes, ChatAuthorization? authorization,
            L12ChatPairRegistry.PairLease pair,
            L12ChatGlobalBudget.Reservation reservation, long generation, long epoch, long expiresAt, long charge)
        {
            Bytes = bytes; Authorization = authorization; Pair = pair;
            Reservation = reservation; Generation = generation;
            Epoch = epoch; ExpiresAt = expiresAt; Charge = charge; Node = new(this);
        }
        internal ArraySegment<byte> Bytes;
        internal ChatAuthorization? Authorization;
        internal L12ChatPairRegistry.PairLease? Pair;
        internal L12ChatGlobalBudget.Reservation? Reservation;
        internal readonly long Generation, Epoch, ExpiresAt, Charge;
        internal readonly LinkedListNode<ChatMessage> Node;
        internal int Released;
    }

    private sealed record ChatAuthorization(L12ChatDeliveryMetadata Metadata,
        L12ChatAccountRegistry.AccountPairLease? Accounts);

    private sealed class ChatLane(L12ChatLaneLimits limits, L12ChatGlobalBudget budget,
        L12ChatPairRegistry registry, Func<long> clock,
        Func<L12ChatDeliveryMetadata?, string, string, bool> freshAdmission,
        Func<ArraySegment<byte>, CancellationToken, ValueTask> kickoff,
        L12ChatAccountRegistry? accountRegistry,
        Func<L12ChatDeliveryMetadata, Func<L12ChatQueueResult>, L12ChatQueueResult>? scopedStart,
        Action? borrowedBarrier, Action? asyncOwnerProbe)
    {
        internal readonly L12ChatLaneLimits Limits = limits;
        internal readonly L12ChatGlobalBudget Budget = budget;
        internal readonly L12ChatPairRegistry Registry = registry;
        internal readonly L12ChatAccountRegistry? AccountRegistry = accountRegistry;
        internal readonly Func<L12ChatDeliveryMetadata, Func<L12ChatQueueResult>,
            L12ChatQueueResult>? ScopedStart = scopedStart;
        internal readonly Func<long> Clock = clock;
        internal readonly Func<L12ChatDeliveryMetadata?, string, string, bool> FreshAdmission = freshAdmission;
        internal readonly Func<ArraySegment<byte>, CancellationToken, ValueTask> Kickoff = kickoff;
        internal readonly Action? BorrowedBarrier = borrowedBarrier;
        internal readonly Action? AsyncOwnerProbe = asyncOwnerProbe;
        internal readonly object StartGate = new();
        internal readonly LinkedList<ChatMessage> Queue = [];
        internal bool Closed;
        internal long Epoch = 1;
        internal int RetainedItems;
        internal long RetainedBytes, Started, Sent, Suppressed, Expired, Paused, TransportFaults;
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

internal enum L12ChatQueueResult
{
    AcceptedQueue, Paused, Suppressed, Expired, QuotaRejected, ActuallyStarted
}

internal readonly record struct L12ChatLaneSnapshot(int Queued, int RetainedItems, long RetainedBytes,
    long Started, long Sent, long Suppressed, long Expired, long Paused, long TransportFaults);

internal sealed class L12ChatLaneLimits
{
    // Conservative charge for descriptor/node/leases, array padding and one in-flight async owner.
    // Transport-specific codec/task retention remains part of the separately reviewed B2 bridge.
    internal const int MinimumMetadataBytes = 512;
    internal L12ChatLaneLimits(int maximumRetainedItems, long maximumRetainedBytes,
        int maximumPacketBytes, int metadataBytes)
    {
        if (maximumRetainedItems <= 0 || maximumRetainedBytes <= 0 || maximumPacketBytes <= 0 ||
            metadataBytes < MinimumMetadataBytes || maximumPacketBytes > maximumRetainedBytes - metadataBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
        MaximumRetainedItems = maximumRetainedItems; MaximumRetainedBytes = maximumRetainedBytes;
        MaximumPacketBytes = maximumPacketBytes; MetadataBytes = metadataBytes;
    }
    internal int MaximumRetainedItems { get; }
    internal long MaximumRetainedBytes { get; }
    internal int MaximumPacketBytes { get; }
    internal int MetadataBytes { get; }
}

/// <summary>Shared, explicitly injected count/byte budget. No runtime defaults or server activation.</summary>
internal sealed class L12ChatGlobalBudget
{
    private readonly object _gate = new();
    private readonly int _maximumItems;
    private readonly long _maximumBytes;
    private int _items;
    private long _bytes;
    internal L12ChatGlobalBudget(int maximumItems, long maximumBytes)
    {
        if (maximumItems <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        _maximumItems = maximumItems; _maximumBytes = maximumBytes;
    }
    internal (int Items, long Bytes) Snapshot { get { lock (_gate) return (_items, _bytes); } }
    internal Reservation? TryReserve(long bytes)
    {
        if (bytes <= 0) return null;
        lock (_gate)
        {
            if (_items >= _maximumItems || bytes > _maximumBytes - _bytes) return null;
            _items++; _bytes += bytes;
            return new Reservation(this, bytes);
        }
    }
    internal sealed class Reservation(L12ChatGlobalBudget owner, long bytes) : IDisposable
    {
        private L12ChatGlobalBudget? _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            lock (owner._gate) { owner._items--; owner._bytes -= bytes; }
        }
    }
}

internal enum L12ChatMutationOutcome { Committed, KnownRollback, Unknown }

/// <summary>
/// One explicitly owned registry for all recipient connections. Pair gates never protect SQL or await.
/// References pin old generations until queued/borrowed/in-flight owners really release them.
/// Unknown outcomes remain closed and bounded until an explicit fresh recovery resolves them.
/// </summary>
internal sealed class L12ChatPairRegistry
{
    internal const int MutationMetadataBytes = 128;
    private readonly object _gate = new();
    // Fixed explicitly budgeted buckets avoid uncharged Dictionary capacity after entry removal.
    private readonly PairEntry?[] _buckets;
    private readonly int _maximumEntries, _maximumPending;
    private readonly long _maximumEntryBytes, _maximumPendingBytes;
    private long _entryBytes, _pendingBytes;
    private int _entries, _pending;
    internal long IndexBytes { get; }

    internal L12ChatPairRegistry(int maximumEntries, long maximumEntryBytes,
        int maximumPending, long maximumPendingBytes)
    {
        // Includes the registry/gate/array headers as well as every retained bucket reference.
        var indexBytes = 192L + 8L * maximumEntries;
        if (maximumEntries <= 0 || maximumEntries > Array.MaxLength || maximumEntryBytes < indexBytes + 256 || maximumPending <= 0 ||
            maximumPendingBytes < MutationMetadataBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        _maximumEntries = maximumEntries; _maximumEntryBytes = maximumEntryBytes;
        _maximumPending = maximumPending; _maximumPendingBytes = maximumPendingBytes;
        IndexBytes = indexBytes; _entryBytes = indexBytes;
        _buckets = new PairEntry?[maximumEntries];
    }
    internal (int Entries, long EntryBytes, int Pending, long PendingBytes) Snapshot
    {
        get { lock (_gate) return (_entries, _entryBytes, _pending, _pendingBytes); }
    }

    internal PairLease? TryAcquire(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second) ||
            string.Equals(first, second, StringComparison.Ordinal)) return null;
        if (string.CompareOrdinal(first, second) > 0) (first, second) = (second, first);
        // Cast before addition: long retained-byte calculation cannot wrap at int string lengths.
        var charge = 256L + 2L * first.Length + 2L * second.Length;
        lock (_gate)
        {
            var bucket = Bucket(first, second);
            var entry = _buckets[bucket];
            while (entry is not null && (entry.FirstId != first || entry.SecondId != second)) entry = entry.Next;
            if (entry is null)
            {
                if (_entries >= _maximumEntries || charge > _maximumEntryBytes - _entryBytes) return null;
                entry = new PairEntry(first, second, charge);
                entry.Next = _buckets[bucket]; _buckets[bucket] = entry; _entries++;
                _entryBytes += charge;
            }
            if (entry.References == int.MaxValue) return null;
            entry.References++;
            return new PairLease(this, entry);
        }
    }

    internal MutationLease? TryBeginMutation(string first, string second)
    {
        var pair = TryAcquire(first, second);
        if (pair is null) return null;
        var admitted = false;
        lock (_gate)
        {
            if (_pending < _maximumPending && MutationMetadataBytes <= _maximumPendingBytes - _pendingBytes)
            {
                _pending++; _pendingBytes += MutationMetadataBytes; admitted = true;
            }
        }
        if (!admitted) { pair.Dispose(); return null; }
        // This pre-close finishes before the caller may enter its authority mutation.
        lock (pair.Entry.StartGate) pair.Entry.Pending++;
        return new MutationLease(this, pair);
    }

    internal bool TryResolveUnknown(string first, string second, Func<string, string, bool> knownCommittedState)
    {
        ArgumentNullException.ThrowIfNull(knownCommittedState);
        using var pair = TryAcquire(first, second);
        if (pair is null) return false;
        lock (pair.Entry.StartGate)
        {
            if (pair.Entry.Pending != 0 || pair.Entry.GenerationExhausted) return false;
            var known = false;
            try { known = knownCommittedState(pair.Entry.FirstId, pair.Entry.SecondId); }
            catch { }
            if (!known) return false;
            pair.Entry.RecoveryClosed = false;
            return true;
        }
    }

    private void Release(PairEntry entry)
    {
        lock (_gate)
        {
            entry.References--;
            if (entry.References != 0 || Volatile.Read(ref entry.Pending) != 0 ||
                Volatile.Read(ref entry.RecoveryClosed)) return;
            var bucket = Bucket(entry.FirstId, entry.SecondId);
            PairEntry? previous = null;
            for (var current = _buckets[bucket]; current is not null; current = current.Next)
            {
                if (ReferenceEquals(current, entry))
                {
                    if (previous is null) _buckets[bucket] = current.Next;
                    else previous.Next = current.Next;
                    current.Next = null; _entries--; break;
                }
                previous = current;
            }
            _entryBytes -= entry.Charge;
        }
    }

    private int Bucket(string first, string second) => (int)((uint)HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(first), StringComparer.Ordinal.GetHashCode(second)) % (uint)_buckets.Length);

    internal sealed class PairEntry(string first, string second, long charge)
    {
        internal readonly object StartGate = new();
        internal readonly string FirstId = first, SecondId = second;
        internal readonly long Charge = charge;
        internal PairEntry? Next;
        internal long Generation;
        internal int Pending, References;
        internal bool RecoveryClosed, GenerationExhausted;
    }
    internal sealed class PairLease(L12ChatPairRegistry owner, PairEntry entry) : IDisposable
    {
        private L12ChatPairRegistry? _owner = owner;
        internal PairEntry Entry { get; } = entry;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(Entry);
    }
    internal sealed class MutationLease(L12ChatPairRegistry owner, PairLease pair) : IDisposable
    {
        private PairLease? _pair = pair;
        internal void Complete(L12ChatMutationOutcome outcome)
        {
            var pair = Interlocked.Exchange(ref _pair, null);
            if (pair is null) return;
            lock (pair.Entry.StartGate)
            {
                if (outcome == L12ChatMutationOutcome.Committed)
                {
                    if (pair.Entry.Generation == long.MaxValue)
                        pair.Entry.RecoveryClosed = pair.Entry.GenerationExhausted = true;
                    else pair.Entry.Generation++;
                }
                else if (outcome != L12ChatMutationOutcome.KnownRollback) pair.Entry.RecoveryClosed = true;
                pair.Entry.Pending--;
            }
            // Neither registry accounting nor caller queue wake takes a pair gate.
            lock (owner._gate) { owner._pending--; owner._pendingBytes -= MutationMetadataBytes; }
            pair.Dispose();
        }
        public void Dispose() => Complete(L12ChatMutationOutcome.Unknown);
    }
}
