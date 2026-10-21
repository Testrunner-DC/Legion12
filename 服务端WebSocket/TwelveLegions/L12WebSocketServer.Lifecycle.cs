using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private static readonly object RequestLifetimeKey = new();
    private static readonly TimeSpan SocketShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly object _lifecycleGate = new();
    private readonly HashSet<ServerRequestLifetime> _activeRequests = [];
    private Task? _startTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    internal Func<Task>? BeforeSocketRegistrationForTests { get; set; }

    // This owns only transport/request lifetime. It is not a second room, game or drain state.
    private sealed class ServerRequestLifetime(HttpContext context)
    {
        internal readonly HttpContext Context = context;
        internal readonly bool IsWebSocket = context.WebSockets.IsWebSocketRequest;
        internal readonly TaskCompletionSource TransportReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource WireClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ShutdownOwnsWire;
        internal bool SocketDisposalStarted;
        internal WebSocket? Socket;
        internal L12OutboundConnection? Outbound;
        internal L12InboundConnection? Inbound;
    }

    private bool IsStopping => Volatile.Read(ref _stopTask) is not null;

    public Task StartAsync(int port)
    {
        TaskCompletionSource completion;
        lock (_lifecycleGate)
        {
            if (_startTask is not null || _stopTask is not null || _disposeTask is not null)
                throw new InvalidOperationException("服务已启动或正在停止，不能再次启动");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _startTask = completion.Task;
        }
        _ = StartLifecycleAsync(port, completion);
        return completion.Task;
    }

    private async Task StartLifecycleAsync(int port, TaskCompletionSource completion)
    {
        try { await StartCoreAsync(port); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task InvokeRequestLifetimeAsync(HttpContext context, Func<Task> next)
    {
        ServerRequestLifetime? lifetime = null;
        lock (_lifecycleGate)
        {
            if (_stopTask is null)
            {
                lifetime = new(context);
                _activeRequests.Add(lifetime);
                context.Items[RequestLifetimeKey] = lifetime;
            }
        }
        if (lifetime is null)
        {
            context.Items[L12HttpPerformanceMonitor.ExpectedUnavailableItemName] = true;
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }
        try { await next(); }
        finally
        {
            lock (_lifecycleGate) _activeRequests.Remove(lifetime);
            lifetime.TransportReady.TrySetResult();
            lifetime.Completed.TrySetResult();
        }
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        ServerRequestLifetime[] requests;
        var errors = new List<Exception>();
        lock (_lifecycleGate)
        {
            if (_stopTask is not null) return _stopTask;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task;
            requests = _activeRequests.ToArray();
            foreach (var request in requests)
            {
                request.ShutdownOwnsWire = request.IsWebSocket && !request.SocketDisposalStarted;
                try { request.Inbound?.StopAcceptingAndCancelPending(); }
                catch (Exception error) { errors.Add(error); }
            }
        }
        _ = StopLifecycleAsync(requests, completion, errors);
        return completion.Task;
    }

    private async Task StopLifecycleAsync(ServerRequestLifetime[] requests, TaskCompletionSource completion,
        List<Exception> errors)
    {
        try
        {
            // A concurrent start must finish registering its workers before they can be joined.
            if (_startTask is { } start)
                try { await start; }
                catch { /* A partially started host still needs real cleanup. */ }

            // Start all transport deadlines together, before waiting on producers that may send.
            // Non-WebSocket HTTP work and already executing inbound commands remain true joins.
            var joins = requests.Select(request => request.ShutdownOwnsWire
                ? StopRequestTransportAsync(request) : request.Completed.Task).ToList();
            foreach (var stop in new Func<Task>[] { _presenceRefreshCoalescer.StopAsync,
                         StopSeasonActivationCoordinatorAsync, StopRulesContentTransitionAsync,
                         StopOperationsTransitionAsync, StopSandboxReplayMaintenanceAsync,
                         StopRankedClockWatchdogAsync })
                joins.Add(InvokeShutdownJoin(stop));
            await CollectShutdownErrorAsync(() => Task.WhenAll(joins), errors);
            if (_chatRuntime is not null)
                await CollectShutdownErrorAsync(() => _chatRuntime.DisposeAsync().AsTask(), errors);
            if (_app is not null) await CollectShutdownErrorAsync(() => _app.StopAsync(), errors);
            if (errors.Count == 0) completion.TrySetResult();
            else completion.TrySetException(new AggregateException("Server shutdown failed after joining owners", errors));
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private static async Task StopRequestTransportAsync(ServerRequestLifetime request)
    {
        using var deadline = new CancellationTokenSource(SocketShutdownTimeout);
        Task? sender = null;
        Task? closeFrame = null;
        var errors = new List<Exception>();
        try
        {
            await request.TransportReady.Task.WaitAsync(deadline.Token);
            request.Inbound?.StopAcceptingAndCancelPending();
            if (request.Outbound is { } outbound)
            {
                sender = outbound.CompleteAsync(drain: false);
                await sender.WaitAsync(deadline.Token);
            }
            // The sender is truly finished before starting a close frame on the same wire.
            if (request.Socket is { State: WebSocketState.Open or WebSocketState.CloseReceived } socket)
            {
                closeFrame = socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "server stopped", deadline.Token);
                await closeFrame.WaitAsync(deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested || TransportIsRetired(request))
        { AbortRequestTransport(request); }
        catch (WebSocketException) { AbortRequestTransport(request); }
        catch (ObjectDisposedException) { AbortRequestTransport(request); }
        catch (Exception error) { errors.Add(error); AbortRequestTransport(request); }
        finally
        {
            // If a transport ignores cancellation, Abort must release its actual close operation
            // before connection-finally may dispose that same wire.
            try { if (closeFrame is not null) await closeFrame; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested || TransportIsRetired(request)) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
            catch (Exception error) { errors.Add(error); }
            request.WireClosed.TrySetResult();
        }

        // Connection-finally may dispose only after the close-frame owner has released the wire.
        // Signal that boundary BEFORE awaiting the whole request to avoid a circular join.
        try { await request.Completed.Task.WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { AbortRequestTransport(request); }

        // Abort releases the actual wire, not the authoritative command/commit. Never report a
        // timed-out join as complete or dispose resources while those real owners still run.
        if (sender is not null) await CollectShutdownErrorAsync(() => sender, errors);
        if (request.Inbound is { } inbound)
            await CollectShutdownErrorAsync(() => inbound.CompleteAsync(drain: false), errors);
        await request.Completed.Task;
        if (errors.Count != 0) throw new AggregateException(errors);
    }

    private async Task DisposeRequestSocketAsync(ServerRequestLifetime request)
    {
        Task? closeFrame = null;
        lock (_lifecycleGate)
        {
            if (request.ShutdownOwnsWire) closeFrame = request.WireClosed.Task;
            else request.SocketDisposalStarted = true;
        }
        if (closeFrame is not null) await closeFrame;
        request.Socket?.Dispose();
    }

    private static async Task CollectShutdownErrorAsync(Func<Task> join, List<Exception> errors)
    {
        Task? task = null;
        try { task = join(); await task; }
        catch (Exception error)
        {
            if (task?.Exception is { } aggregate) errors.AddRange(aggregate.Flatten().InnerExceptions);
            else errors.Add(error);
        }
    }

    private static Task InvokeShutdownJoin(Func<Task> join)
    {
        try { return join(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static void AbortRequestTransport(ServerRequestLifetime request)
    {
        // Keep the captured reference: connection-finally may already have removed _sockets.
        try { request.Socket?.Abort(); }
        catch (ObjectDisposedException) { }
        try { request.Context.Abort(); }
        catch (ObjectDisposedException) { }
    }

    private static bool TransportIsRetired(ServerRequestLifetime request)
        => request.Completed.Task.IsCompleted || request.Context.RequestAborted.IsCancellationRequested
            || request.Socket?.State is WebSocketState.Closed or WebSocketState.Aborted;

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_lifecycleGate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }
        _ = DisposeLifecycleAsync(completion);
        return new(completion.Task);
    }

    private async Task DisposeLifecycleAsync(TaskCompletionSource completion)
    {
        try
        {
            var errors = new List<Exception>();
            await CollectShutdownErrorAsync(StopAsync, errors);
            _platform.SessionsRevoked -= HandlePlatformSessionsRevoked;
            await CollectShutdownErrorAsync(() => _presenceRefreshCoalescer.DisposeAsync().AsTask(), errors);
            try { _modianImports.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            if (_app is not null) await CollectShutdownErrorAsync(() => _app.DisposeAsync().AsTask(), errors);
            if (errors.Count == 0) completion.TrySetResult();
            else completion.TrySetException(new AggregateException("Server disposal failed after joining owners", errors));
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
