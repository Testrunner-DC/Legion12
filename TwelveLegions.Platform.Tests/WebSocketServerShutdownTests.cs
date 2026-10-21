using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class WebSocketServerShutdownTests
{
    [Theory]
    [InlineData("RulesContent", "_rulesContentResourceRevision")]
    [InlineData("Operations", "_operationsResourceRevision")]
    public async Task FarFutureResourceTransitionCanBeCanceledWithoutEarlyNotification(string resource, string revision)
    {
        await using var fixture = await Fixture.CreateAsync(start: false);
        using var cancellation = new CancellationTokenSource();
        var transition = (Task)typeof(L12WebSocketServer).GetMethod($"Run{resource}TransitionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Server,
            [DateTimeOffset.UtcNow.AddYears(2), cancellation.Token])!;
        try
        {
            Assert.False(transition.IsCompleted, "long schedules must wait, not fault or fire immediately");
            Assert.Equal(0L, RuntimeField<long>(fixture.Server, revision));
        }
        finally { cancellation.Cancel(); await transition.WaitAsync(TimeSpan.FromSeconds(3)); }
        Assert.Equal(0L, RuntimeField<long>(fixture.Server, revision));
    }

    [Theory]
    [InlineData("RulesContent", "_rulesContentResourceRevision")]
    [InlineData("Operations", "_operationsResourceRevision")]
    public async Task ResourceTransitionNotifiesOnlyAfterItsActualDueTime(string resource, string revision)
    {
        await using var fixture = await Fixture.CreateAsync(start: false);
        using var cancellation = new CancellationTokenSource();
        var due = DateTimeOffset.UtcNow.AddMilliseconds(100);
        var transition = (Task)typeof(L12WebSocketServer).GetMethod($"Run{resource}TransitionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Server, [due, cancellation.Token])!;
        try
        {
            Assert.False(transition.IsCompleted);
            Assert.Equal(0L, RuntimeField<long>(fixture.Server, revision));
            await transition.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(DateTimeOffset.UtcNow >= due);
            Assert.Equal(1L, RuntimeField<long>(fixture.Server, revision));
        }
        finally { cancellation.Cancel(); await transition.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Fact]
    public async Task UnexpectedWireCancellationStillFailsAfterJoiningTheRequest()
    {
        await using var fixture = await Fixture.CreateAsync(start: false);
        using var socket = new AbortOnlySocket();
        var entered = NewSignal();
        socket.CloseFrame = _ =>
        {
            entered.TrySetResult();
            return Task.FromCanceled(new CancellationToken(canceled: true));
        };
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new SyntheticWebSocketFeature(socket));
        var request = InvokeRequest(fixture.Server, context, async () =>
        {
            var lifetime = context.Items.Values.Single(value => value?.GetType().Name == "ServerRequestLifetime")!;
            SetField(lifetime, "Socket", socket);
            RuntimeField<TaskCompletionSource>(lifetime, "TransportReady").TrySetResult();
            await entered.Task;
            await (Task)typeof(L12WebSocketServer).GetMethod("DisposeRequestSocketAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Server, [lifetime])!;
        });
        var stopping = fixture.Server.StopAsync();
        var error = await Assert.ThrowsAsync<AggregateException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(3)));
        var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(error.Flatten().InnerExceptions));
        fixture.ExpectedShutdownFailure = cancellation.Message;
        await request;
        Assert.True(socket.AbortCount >= 1);
        Assert.Equal(1, socket.DisposeCount);
    }

    [Fact]
    public async Task ShutdownWaitsForExecutingCommitButCancelsQueuedMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = fixture.Platform.Register($"sc{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        Assert.True(fixture.Platform.SetRole(fixture.Platform.Login("Admin", "L12master").Account!, account.Id, "admin"));
        var token = fixture.Platform.Login(account.Username, "Password123!").Token!;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(fixture.Endpoint, CancellationToken.None);
        await SendAsync(socket, new { type = "hello", authToken = token });
        _ = await ReceiveTypeAsync(socket, "session");
        _ = await ReceiveTypeAsync(socket, "recoveryComplete");
        await SendAsync(socket, new { type = "createSandbox" });
        var roomCode = (await ReceiveTypeAsync(socket, "roomState")).GetProperty("roomCode").GetString()!;
        var rooms = RuntimeField<IEnumerable>(fixture.Manager, "_rooms");
        var runtimeRoom = rooms.Cast<object>().Select(entry => entry.GetType().GetProperty("Value")!.GetValue(entry)!)
            .Single(room => (string)room.GetType().GetProperty("Code")!.GetValue(room)! == roomCode);
        var matchId = ((L12GameEngine)runtimeRoom.GetType().GetProperty("Game")!.GetValue(runtimeRoom)!).State.MatchId;
        var entered = NewSignal();
        using var release = new ManualResetEventSlim();
        fixture.Recorder.StorageFailureInjector = phase =>
        {
            if (phase != "before-match-command-commit") return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("synthetic shutdown commit hold");
        };
        Task? stopping = null;
        Task? disposing = null;
        try
        {
            await SendAsync(socket, new { type = "setResponsePreference", mode = "valid-only", requestId = "committing" });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var inbound = Assert.Single(RuntimeField<ConcurrentDictionary<Guid, L12InboundConnection>>(
                fixture.Server, "_inboundConnections")).Value;
            await SendAsync(socket, new { type = "setResponsePreference", mode = "invalid-five-seconds", requestId = "unstarted" });
            Assert.True(SpinWait.SpinUntil(() => inbound.RetainedMessages == 2, TimeSpan.FromSeconds(3)));
            stopping = fixture.Server.StopAsync();
            disposing = fixture.Server.DisposeAsync().AsTask();
            await Task.Delay(2300);
            Assert.False(stopping.IsCompleted, "an elapsed wire deadline cannot complete a real database owner");
            Assert.False(disposing.IsCompleted);
            Assert.False(inbound.IsAccepting);
            Assert.Equal(1, inbound.RetainedMessages);
            release.Set();
            await Task.WhenAll(stopping, disposing).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, inbound.RetainedMessages);
            Assert.Equal("valid-only", fixture.Platform.ResponsePreference(account.Id));
            await using var reopened = new MatchRecorder(Path.Combine(fixture.DirectoryPath, "matches.db"));
            await reopened.InitializeAsync();
            reopened.AttachCatalog(fixture.Catalog);
            var recorded = (await reopened.GetMatchAsync(matchId))!;
            var command = Assert.Single(recorded.Commands.Where(command => command.Command.GetProperty("type")
                .GetString() == "setResponsePreference"));
            Assert.True(command.Accepted);
            Assert.Equal("valid-only", command.Command.GetProperty("responseMode").GetString());
            Assert.Equal("valid-only", (await reopened.LoadJournalEngineAsync(matchId))!.Engine.ResponseModeFor(0));
        }
        finally
        {
            release.Set();
            fixture.Recorder.StorageFailureInjector = null;
            socket.Abort();
            if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposing is not null) await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ConnectionFinallyCannotDisposeWhileShutdownCloseFrameStillOwnsTheWire()
    {
        await using var fixture = await Fixture.CreateAsync(start: false);
        using var socket = new AbortOnlySocket();
        var closeEntered = NewSignal();
        var closeRelease = NewSignal();
        var finallyEntered = NewSignal();
        socket.CloseFrame = async token => { closeEntered.TrySetResult(); await closeRelease.Task.WaitAsync(token); };
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new SyntheticWebSocketFeature(socket));
        var request = InvokeRequest(fixture.Server, context, async () =>
        {
            var lifetime = context.Items.Values.Single(value => value?.GetType().Name == "ServerRequestLifetime")!;
            SetField(lifetime, "Socket", socket);
            RuntimeField<TaskCompletionSource>(lifetime, "TransportReady").TrySetResult();
            await closeEntered.Task;
            finallyEntered.TrySetResult();
            await (Task)typeof(L12WebSocketServer).GetMethod("DisposeRequestSocketAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Server, [lifetime])!;
        });
        var stopping = fixture.Server.StopAsync();
        try
        {
            await finallyEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(request.IsCompleted);
            Assert.Equal(0, socket.DisposeCount);
            closeRelease.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, socket.DisposeCount);
            Assert.Equal(0, socket.AbortCount);
        }
        finally { closeRelease.TrySetResult(); await request.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Fact]
    public async Task CloseFrameIgnoringCancellationIsAbortedAndReallyJoinedBeforeDispose()
    {
        await using var fixture = await Fixture.CreateAsync(start: false);
        using var socket = new AbortOnlySocket();
        var entered = NewSignal();
        var exited = false;
        socket.CloseFrame = async _ => { entered.TrySetResult(); await socket.Aborted.Task; exited = true; };
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new SyntheticWebSocketFeature(socket));
        var request = InvokeRequest(fixture.Server, context, async () =>
        {
            var lifetime = context.Items.Values.Single(value => value?.GetType().Name == "ServerRequestLifetime")!;
            SetField(lifetime, "Socket", socket);
            RuntimeField<TaskCompletionSource>(lifetime, "TransportReady").TrySetResult();
            await entered.Task;
            await (Task)typeof(L12WebSocketServer).GetMethod("DisposeRequestSocketAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Server, [lifetime])!;
            Assert.True(exited);
        });
        var stopping = fixture.Server.StopAsync();
        try
        {
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(exited);
            Assert.True(socket.AbortCount >= 1);
            Assert.Equal(1, socket.DisposeCount);
        }
        finally { socket.Abort(); await request.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Fact]
    public async Task FailedWorkerStillJoinsOtherOwnersAndStopsTheHostBeforeReportingFailure()
    {
        await using var fixture = await Fixture.CreateAsync(chatEnabled: true);
        await (Task)typeof(L12WebSocketServer).GetMethod("StopRankedClockWatchdogAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Server, null)!;
        SetField(fixture.Server, "_rankedClockWatchdogCancellation", new CancellationTokenSource());
        SetField(fixture.Server, "_rankedClockWatchdogTask", Task.FromException(new InvalidOperationException("synthetic-watchdog-failure")));
        var runtime = RuntimeField<L12ChatRuntime>(fixture.Server, "_chatRuntime");
        var worker = RuntimeField<Task>(runtime, "_worker");
        var context = new DefaultHttpContext();
        var release = NewSignal();
        var request = InvokeRequest(fixture.Server, context, () => release.Task);
        var stopping = fixture.Server.StopAsync();
        fixture.ExpectedShutdownFailure = "synthetic-watchdog-failure";
        Assert.False(stopping.IsCompleted);
        release.TrySetResult();
        var error = await Assert.ThrowsAsync<AggregateException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("synthetic-watchdog-failure", Assert.Single(error.Flatten().InnerExceptions).Message);
        await request;
        Assert.True(worker.IsCompletedSuccessfully, "failed watchdog must not skip the chat owner join");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri(fixture.HttpEndpoint, "/health")));
        await Assert.ThrowsAsync<AggregateException>(() => fixture.Server.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CooperativeCloseAndConcurrentStopDisposeShareTheirRealCompletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(fixture.Endpoint, CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => RuntimeSockets(fixture.Server).Count == 1, TimeSpan.FromSeconds(3)));
        var peer = Task.Run(async () =>
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(new byte[512]), CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Close, result.MessageType);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, result.CloseStatus);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", CancellationToken.None);
        });
        var first = fixture.Server.StopAsync();
        Assert.Same(first, fixture.Server.StopAsync());
        var disposing = fixture.Server.DisposeAsync().AsTask();
        Assert.Same(disposing, fixture.Server.DisposeAsync().AsTask());
        await Task.WhenAll(first, disposing, peer).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(RuntimeSockets(fixture.Server));
        Assert.Empty(RuntimeField<ConcurrentDictionary<Guid, L12InboundConnection>>(fixture.Server, "_inboundConnections"));
        Assert.Empty(RuntimeField<ConcurrentDictionary<Guid, L12OutboundConnection>>(fixture.Server, "_outboundConnections"));
        Assert.Same(first, fixture.Server.StopAsync());
    }

    [Fact]
    public async Task StopFencesNewRequestsAndJoinsAConnectionStillRegistering()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = NewSignal();
        var release = NewSignal();
        fixture.Server.BeforeSocketRegistrationForTests = () =>
        {
            entered.TrySetResult();
            return release.Task;
        };
        using var socket = new ClientWebSocket();
        Task? stopping = null;
        Task? disposing = null;
        try
        {
            await socket.ConnectAsync(fixture.Endpoint, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stopping = fixture.Server.StopAsync();
            disposing = fixture.Server.DisposeAsync().AsTask();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await http.GetAsync(new Uri(fixture.HttpEndpoint, "/health"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var late = new ClientWebSocket();
            await Assert.ThrowsAsync<WebSocketException>(() => late.ConnectAsync(fixture.Endpoint, CancellationToken.None));
            await Task.Delay(2300); // Cross the transport deadline; the true request still owns cleanup.
            Assert.False(stopping.IsCompleted);
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            socket.Abort();
            if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposing is not null) await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Empty(RuntimeSockets(fixture.Server));
        var requests = RuntimeField<object>(fixture.Server, "_activeRequests");
        Assert.Equal(0, (int)requests.GetType().GetProperty("Count")!.GetValue(requests)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbortReleasesARealSenderJoinEvenAfterSocketWasRemoved(bool terminalAlreadyStarted)
    {
        await using var fixture = await Fixture.CreateAsync(start: false);
        using var socket = new AbortOnlySocket();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new SyntheticWebSocketFeature(socket));
        var started = NewSignal();
        var cleanupEntered = NewSignal();
        var senderExited = false;
        var outbound = new L12OutboundConnection(async (_, _) =>
        {
            started.TrySetResult();
            await socket.Aborted.Task; // Deliberately ignores cancellation; only actual Abort releases the wire.
            senderExited = true;
        });
        Task? earlierTerminal = null;
        var request = InvokeRequest(fixture.Server, context, async () =>
        {
            var lifetime = context.Items.Values.Single(value => value?.GetType().Name == "ServerRequestLifetime")!;
            SetField(lifetime, "Socket", socket);
            SetField(lifetime, "Outbound", outbound);
            RuntimeField<TaskCompletionSource>(lifetime, "TransportReady").TrySetResult();
            var id = Guid.NewGuid();
            RuntimeSockets(fixture.Server)[id] = socket;
            Assert.True(outbound.TryEnqueue("in-flight"));
            await started.Task;
            if (terminalAlreadyStarted)
                earlierTerminal = outbound.EnqueueTerminalAndCompleteAsync("terminal", CancellationToken.None);
            RuntimeSockets(fixture.Server).TryRemove(id, out _);
            cleanupEntered.TrySetResult();
            await outbound.DisposeAsync(); // Simulates connection-finally waiting after map removal.
        });
        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stopping = fixture.Server.StopAsync();
        try { await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally
        {
            socket.Abort();
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            if (earlierTerminal is not null)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => earlierTerminal);
        }
        Assert.True(senderExited);
        Assert.True(socket.AbortCount >= 1);
        Assert.Equal(0, socket.CloseFramesBeforeAbort);
        Assert.Equal(0, outbound.QueuedCount);
        Assert.False(outbound.TryEnqueue("late"));
        await outbound.CompleteAsync(drain: false); // Legal even after concurrent disposal completed.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonDrainCancelsAnAlreadyClosingChatWireAndReleasesItsBudgets(bool terminal)
    {
        var budget = new L12ChatGlobalBudget(8, 8192);
        var registry = new L12ChatPairRegistry(8, 8192, 8, 8192);
        var started = NewSignal();
        var canceled = NewSignal();
        var release = NewSignal();
        var outbound = new L12OutboundConnection((_, _) => Task.CompletedTask);
        outbound.ConfigureChatLane(new(8, 8192, 64, 512), budget, registry, () => 1, (_, _) => true,
            (_, token) =>
            {
                started.TrySetResult();
                return new ValueTask(WaitChatWire(token));
            });
        async Task WaitChatWire(CancellationToken token)
        {
            try { await release.Task.WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                canceled.TrySetResult();
                throw;
            }
        }
        Assert.Equal(L12ChatQueueResult.AcceptedQueue,
            outbound.TryEnqueueChat("synthetic-a", "synthetic-b", new ArraySegment<byte>(new byte[1]), 100));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var closing = terminal ? outbound.EnqueueTerminalAndCompleteAsync("terminal", CancellationToken.None)
            : outbound.CompleteAsync(drain: true);
        var stopping = outbound.CompleteAsync(drain: false);
        try
        {
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            if (terminal) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
            else await closing;
        }
        finally { release.TrySetResult(); await outbound.DisposeAsync(); }
        Assert.Equal((0, 0L), budget.Snapshot);
        Assert.Equal(0, registry.Snapshot.Entries);
        Assert.Equal(0, outbound.ReadChatLaneSnapshot().RetainedItems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonDrainingCompletionUpgradesAnAlreadyClosingSender(bool terminal)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new ConcurrentQueue<string>();
        var outbound = new L12OutboundConnection(async (payload, token) =>
        {
            sent.Enqueue((string)payload);
            started.TrySetResult();
            try { await release.Task.WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                canceled.TrySetResult();
                throw;
            }
        });
        Assert.True(outbound.TryEnqueue("in-flight"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var earlierClose = terminal
            ? outbound.EnqueueTerminalAndCompleteAsync("terminal", CancellationToken.None)
            : outbound.CompleteAsync(drain: true);
        var stopped = outbound.CompleteAsync(drain: false);
        var cancellationObserved = false;
        try
        {
            cancellationObserved = await Task.WhenAny(canceled.Task, Task.Delay(500)) == canceled.Task;
        }
        finally
        {
            release.TrySetResult();
            try { await earlierClose.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) when (terminal) { }
            await stopped.WaitAsync(TimeSpan.FromSeconds(3));
            await outbound.DisposeAsync();
        }
        Assert.True(cancellationObserved, "non-drain must cancel even after drain/terminal completion began");
        Assert.Equal(new[] { "in-flight" }, sent);
        Assert.Equal(0, outbound.QueuedCount);
        Assert.False(outbound.TryEnqueue("too-late"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task ClientsThatNeverReadOrAcknowledgeCloseCannotHoldServerStop(int clients)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-server-shutdown", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sockets = new List<ClientWebSocket>();
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            var endpoint = new UriBuilder(Assert.Single(server.Addresses))
            {
                Host = "127.0.0.1", Scheme = "ws", Path = "/ws",
            }.Uri;
            Task? stopping = null;
            var bounded = false;
            try
            {
                for (var index = 0; index < clients; index++)
                {
                    var socket = new ClientWebSocket();
                    sockets.Add(socket);
                    await socket.ConnectAsync(endpoint, CancellationToken.None);
                }
                Assert.True(SpinWait.SpinUntil(() => RuntimeSockets(server).Count == clients,
                    TimeSpan.FromSeconds(3)));
                stopping = server.StopAsync();
                bounded = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5))) == stopping;
                if (bounded) await stopping;
            }
            finally
            {
                // The RED run must unblock the old implementation too, without leaking a host.
                foreach (var socket in sockets) socket.Abort();
                if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(10));
                await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.True(bounded, "client-controlled close handshake must not hold server StopAsync");
            Assert.Empty(RuntimeSockets(server));
        }
        finally
        {
            foreach (var socket in sockets) socket.Dispose();
            DeleteIsolatedTestDirectory(directory, "l12-server-shutdown");
        }
    }

    private static ConcurrentDictionary<Guid, WebSocket> RuntimeSockets(L12WebSocketServer server)
        => (ConcurrentDictionary<Guid, WebSocket>)typeof(L12WebSocketServer)
            .GetField("_sockets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task SendAsync(ClientWebSocket socket, object payload)
        => socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonElement> ReceiveTypeAsync(ClientWebSocket socket, string expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16 * 1024];
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed before expected message");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var document = JsonDocument.Parse(stream.ToArray());
            if (document.RootElement.GetProperty("type").GetString() == expected) return document.RootElement.Clone();
        }
        throw new InvalidDataException("missing expected message");
    }

    private static T RuntimeField<T>(object owner, string name)
        => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void SetField(object owner, string name, object value)
        => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    private static Task InvokeRequest(L12WebSocketServer server, HttpContext context, Func<Task> next)
        => (Task)typeof(L12WebSocketServer).GetMethod("InvokeRequestLifetimeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(server, [context, next])!;

    private sealed class SyntheticWebSocketFeature(WebSocket socket) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(socket);
    }

    private sealed class AbortOnlySocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        internal readonly TaskCompletionSource Aborted = NewSignal();
        internal int AbortCount;
        internal int CloseFramesBeforeAbort;
        internal int DisposeCount;
        internal Func<CancellationToken, Task>? CloseFrame;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { AbortCount++; _state = WebSocketState.Aborted; Aborted.TrySetResult(); }
        public override void Dispose() { DisposeCount++; _state = WebSocketState.Closed; }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => throw new InvalidOperationException("must not wait on peer handshake");
        public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken)
        {
            if (!Aborted.Task.IsCompleted) CloseFramesBeforeAbort++;
            if (CloseFrame is { } closeFrame) await closeFrame(cancellationToken);
            _state = WebSocketState.CloseSent;
        }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal required string DirectoryPath;
        internal required L12Catalog Catalog;
        internal required L12PlatformStore Platform;
        internal required MatchRecorder Recorder;
        internal required L12RoomManager Manager;
        internal required L12WebSocketServer Server;
        internal string? ExpectedShutdownFailure;
        internal Uri HttpEndpoint => new UriBuilder(Assert.Single(Server.Addresses)) { Host = "127.0.0.1" }.Uri;
        internal Uri Endpoint => new UriBuilder(HttpEndpoint) { Scheme = "ws", Path = "/ws" }.Uri;

        internal static async Task<Fixture> CreateAsync(bool start = true, bool chatEnabled = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "l12-server-shutdown", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog, chatEnabled: chatEnabled);
            if (start) await server.StartAsync(0);
            return new() { DirectoryPath = directory, Catalog = catalog, Platform = platform,
                Recorder = recorder, Manager = manager, Server = server };
        }

        public async ValueTask DisposeAsync()
        {
            try { await Server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (AggregateException error) when (ExpectedShutdownFailure is { } expected
                && error.Flatten().InnerExceptions.All(item => item.Message == expected)) { }
            finally
            {
                await Recorder.DisposeAsync();
                DeleteIsolatedTestDirectory(DirectoryPath, "l12-server-shutdown");
            }
        }
    }

    private static void DeleteIsolatedTestDirectory(string directory, string expectedParentName)
    {
        var target = new DirectoryInfo(Path.GetFullPath(directory));
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), expectedParentName));
        if (target.Parent is null || !string.Equals(target.Parent.FullName, expectedParent,
                StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(target.Name, "N", out _))
            throw new InvalidOperationException("refusing unexpected test cleanup");
        PlatformTestDirectoryCleanup.Delete(target.FullName);
    }
}
