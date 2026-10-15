using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ChatSocketAdmissionBoundaryTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HostDeadline = TimeSpan.FromSeconds(10);
    private static readonly Func<WebSocket, L12SnapshotWireCodec, L12PresenceSnapshotWireCodec,
        object, L12SyntheticTransportSample?, CancellationToken, Task> SendDirect =
        typeof(L12WebSocketServer).GetMethod("SendDirectAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<WebSocket, L12SnapshotWireCodec, L12PresenceSnapshotWireCodec,
                object, L12SyntheticTransportSample?, CancellationToken, Task>>();

    [Fact]
    public async Task S01_ThinStartIsSynchronousAndPreservesMemoryFlagsTokenAndValueTask()
    {
        var release = NewSignal();
        using var socket = new ControlledSocket { Responder = _ => new ValueTask(release.Task) };
        using var cancellation = new CancellationTokenSource();
        var backing = Encoding.UTF8.GetBytes("prefix 原字节 suffix");
        var payload = backing.AsMemory(3, 8);
        try
        {
            var wire = L12WebSocketServer.StartPreparedTextSend(socket, payload, cancellation.Token);
            var call = Assert.Single(socket.Calls);
            Assert.True(MemoryMarshal.TryGetArray(call.Payload, out var observed));
            Assert.Same(backing, observed.Array);
            Assert.Equal(3, observed.Offset);
            Assert.Equal(8, observed.Count);
            Assert.Equal(WebSocketMessageType.Text, call.Type);
            Assert.True(call.EndOfMessage);
            Assert.Equal(cancellation.Token, call.Token);
            Assert.False(wire.IsCompleted);
            Assert.Same(release.Task, wire.AsTask());
            var method = typeof(L12WebSocketServer).GetMethod("StartPreparedTextSend",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Null(method.GetCustomAttribute<AsyncStateMachineAttribute>());
            release.TrySetResult();
            await wire.AsTask().WaitAsync(TestDeadline);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public void S01_ThinStartPropagatesSynchronousSocketFailure()
    {
        var failure = new WebSocketException("synchronous wire failure");
        using var socket = new ControlledSocket { Responder = _ => throw failure };
        var observed = Assert.Throws<WebSocketException>(() =>
        {
            _ = L12WebSocketServer.StartPreparedTextSend(socket, new byte[1], CancellationToken.None);
        });
        Assert.Same(failure, observed);
        Assert.Single(socket.Calls);
    }

    [Fact]
    public async Task S01_ThinStartPropagatesAsynchronousSocketFailure()
    {
        var release = NewSignal();
        var failure = new WebSocketException("asynchronous wire failure");
        using var socket = new ControlledSocket { Responder = _ => new ValueTask(release.Task) };
        var wire = L12WebSocketServer.StartPreparedTextSend(socket, new byte[1], CancellationToken.None);
        Assert.Single(socket.Calls);
        Assert.False(wire.IsCompleted);
        release.TrySetException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<WebSocketException>(
            () => wire.AsTask().WaitAsync(TestDeadline)));
    }

    [Fact]
    public async Task S01_ThinStartPreservesAlreadyCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var socket = new ControlledSocket
        {
            Responder = call => new ValueTask(Task.FromCanceled(call.Token)),
        };
        var wire = L12WebSocketServer.StartPreparedTextSend(socket, new byte[1], cancellation.Token);
        Assert.Equal(cancellation.Token, Assert.Single(socket.Calls).Token);
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wire.AsTask());
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task S01_SyntheticLowRetainsFullBufferUntilWireCompletesOnTheExistingSender()
    {
        var entered = NewSignal();
        var release = NewSignal();
        using var socket = new ControlledSocket
        {
            Responder = _ => { entered.TrySetResult(); return new ValueTask(release.Task); },
        };
        var faults = 0;
        var legacyCalls = 0;
        await using var outbound = new L12OutboundConnection((object _, CancellationToken token) =>
        {
            Interlocked.Increment(ref legacyCalls);
            return Task.CompletedTask;
        }, _ => Interlocked.Increment(ref faults));
        var budget = new L12ChatGlobalBudget(1, 1024);
        outbound.ConfigureChatLane(new(1, 1024, 64, 512), budget,
            new(1, 1024, 1, 128), () => 1, (_, _) => true,
            (bytes, token) => L12WebSocketServer.StartPreparedTextSend(socket,
                bytes.Array!.AsMemory(bytes.Offset, bytes.Count), token));
        var backing = new byte[512];
        try
        {
            Assert.Equal(L12ChatQueueResult.AcceptedQueue,
                outbound.TryEnqueueChat("a", "b", new(backing, 23, 5), 100));
            await entered.Task.WaitAsync(TestDeadline);
            await Eventually(() => outbound.ReadChatLaneSnapshot().Started == 1);
            var call = Assert.Single(socket.Calls);
            Assert.True(MemoryMarshal.TryGetArray(call.Payload, out var slice));
            Assert.Same(backing, slice.Array);
            Assert.Equal(23, slice.Offset);
            Assert.Equal(5, slice.Count);
            Assert.Equal((1, 1024L), budget.Snapshot);
            Assert.Equal(1, outbound.ReadChatLaneSnapshot().RetainedItems);
            var legacy = outbound.EnqueueAndWaitAsync("legacy-after-low", CancellationToken.None);
            Assert.False(legacy.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref legacyCalls));
            release.TrySetResult();
            Assert.True(await legacy.WaitAsync(TestDeadline));
            await Eventually(() => outbound.ReadChatLaneSnapshot() is { Sent: 1, RetainedItems: 0 });
            Assert.Equal((0, 0L), budget.Snapshot);
            Assert.Equal(1, Volatile.Read(ref legacyCalls));
            Assert.Equal(0, Volatile.Read(ref faults));
            Assert.Equal(0, socket.Aborts);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S01_SyntheticGuardRejectOrUnreadableNeverStartsSocket(bool unreadable)
    {
        using var socket = new ControlledSocket();
        var faults = 0;
        await using var outbound = new L12OutboundConnection((object _, CancellationToken token) =>
            Task.CompletedTask, _ => Interlocked.Increment(ref faults));
        var budget = new L12ChatGlobalBudget(1, 1024);
        outbound.ConfigureChatLane(new(1, 1024, 64, 512), budget,
            new(1, 1024, 1, 128), () => 1,
            (_, _) => unreadable ? throw new InvalidOperationException("unreadable facts") : false,
            (bytes, token) => L12WebSocketServer.StartPreparedTextSend(socket,
                bytes.Array!.AsMemory(bytes.Offset, bytes.Count), token));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue,
            outbound.TryEnqueueChat("a", "b", new byte[8], 100));
        await Eventually(() => outbound.ReadChatLaneSnapshot() is { Suppressed: 1, RetainedItems: 0 });
        Assert.Empty(socket.Calls);
        Assert.Equal((0, 0L), budget.Snapshot);
        Assert.Equal(0, Volatile.Read(ref faults));
        Assert.Equal(0, socket.Aborts);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task S01_LegacyColdDeltaCommitsOnlyAfterSuccessfulActualSend(bool presence, bool failWire)
    {
        var gameCodec = new L12SnapshotWireCodec();
        var presenceCodec = new L12PresenceSnapshotWireCodec();
        gameCodec.SetDeltaEnabled(true);
        presenceCodec.SetDeltaEnabled(true);
        using var socket = new ControlledSocket();
        L12PreparedWirePayload Prepare(int revision) => presence
            ? presenceCodec.Prepare(Frame(true, revision)) : gameCodec.Prepare(Frame(false, revision));
        Task Send(int revision) => SendDirect(socket, gameCodec, presenceCodec,
            Frame(presence, revision), null, CancellationToken.None);
        await Send(1).WaitAsync(TestDeadline);
        Assert.Equal(Frame(presence, 1).FullPayload, Assert.Single(socket.Calls).Payload.ToArray());
        var release = NewSignal();
        socket.Responder = _ => new ValueTask(release.Task);
        try
        {
            var sending = Send(2);
            Assert.Equal(2, socket.Calls.Length);
            Assert.False(sending.IsCompleted);
            AssertDelta(socket.Calls[1].Payload, presence, 1, 2);
            AssertDelta(Prepare(3).Payload, presence, 1, 3);
            if (failWire)
            {
                var failure = new WebSocketException("failed delta must not commit");
                release.TrySetException(failure);
                Assert.Same(failure, await Assert.ThrowsAsync<WebSocketException>(
                    () => sending.WaitAsync(TestDeadline)));
            }
            else
            {
                release.TrySetResult();
                await sending.WaitAsync(TestDeadline);
            }
            AssertDelta(Prepare(3).Payload, presence, failWire ? 1 : 2, 3);
            socket.Responder = _ => ValueTask.CompletedTask;
            await Send(3).WaitAsync(TestDeadline);
            AssertDelta(socket.Calls[2].Payload, presence, failWire ? 1 : 2, 3);
            AssertDelta(Prepare(4).Payload, presence, 3, 4);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S01_LocalhostExistingWebSocketDeliversOriginalLegacyEnvelope(bool syntheticLow)
    {
        using var fixture = new RankedIdentityFixture();
        var recipient = fixture.Store.Register($"sr{Guid.NewGuid():N}"[..10], "Password123!");
        var sender = fixture.Store.Register($"ss{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(recipient.Success);
        Assert.True(sender.Success);
        var recipientId = recipient.Account!.Id;
        var senderId = sender.Account!.Id;
        Assert.True(fixture.Store.SendFriendRequest(senderId, recipientId).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(recipientId, senderId, true).Success);
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "matches.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        var server = new L12WebSocketServer(rooms, recorder, fixture.Store, fixture.Catalog);
        using var client = new ClientWebSocket();
        try
        {
            await server.StartAsync(0).WaitAsync(HostDeadline);
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            using var connect = new CancellationTokenSource(HostDeadline);
            await client.ConnectAsync(endpoint, connect.Token);
            await SendJson(client, new { type = "hello", authToken = recipient.Token });
            var sessionBytes = await ReceiveType(client, "session");
            using var session = JsonDocument.Parse(sessionBytes);
            var id = session.RootElement.GetProperty("sessionId").GetGuid();
            _ = await ReceiveType(client, "recoveryComplete");
            var outbound = Field<ConcurrentDictionary<Guid, L12OutboundConnection>>(server, "_outboundConnections")[id];
            var serverSocket = Field<ConcurrentDictionary<Guid, WebSocket>>(server, "_sockets")[id];
            Assert.False(outbound.HasChatLane);
            // Existing error envelope, not a new chat protocol or production chat caller.
            var bytes = Encoding.UTF8.GetBytes("{\"type\":\"error\",\"message\":\"S01 原字节\\n边界\"}");
            L12ChatGlobalBudget? budget = null;
            if (syntheticLow)
            {
                budget = new(1, 4096);
                outbound.ConfigureChatLane(new(1, 4096, 1024, 512), budget,
                    new(1, 2048, 1, 128), () => 1,
                    (a, b) => fixture.Store.ReadCommittedChatRelationship(a, b) is
                        { Available: true, AcceptedAndUnblocked: true },
                    (payload, token) => L12WebSocketServer.StartPreparedTextSend(serverSocket,
                        payload.Array!.AsMemory(payload.Offset, payload.Count), token));
                var backing = new byte[bytes.Length + 37];
                bytes.CopyTo(backing, 17);
                Assert.Equal(L12ChatQueueResult.AcceptedQueue,
                    outbound.TryEnqueueChat(senderId, recipientId, new(backing, 17, bytes.Length), 100));
            }
            else Assert.True(outbound.TryEnqueue(new L12QueuedPayload(bytes, false, false)));
            Assert.Equal(bytes, await ReceiveType(client, "error"));
            await SendJson(client, new { type = "ping" });
            _ = await ReceiveType(client, "pong");
            if (syntheticLow)
            {
                await Eventually(() => outbound.ReadChatLaneSnapshot() is { Sent: 1, RetainedItems: 0 });
                Assert.Equal((0, 0L), budget!.Snapshot);
            }
            else Assert.False(outbound.HasChatLane);
        }
        finally
        {
            client.Abort();
            try { await server.StopAsync().WaitAsync(HostDeadline); }
            finally { await server.DisposeAsync().AsTask().WaitAsync(HostDeadline); }
        }
    }

    private static L12QueuedPayload Frame(bool presence, int revision)
    {
        var bytes = presence
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "presenceSnapshot", resource = "presence", epoch = "s01-epoch", revision,
                items = Enumerable.Range(0, 12).Select(i => new
                {
                    accountId = $"p{i}", username = new string('x', 128),
                    activity = i == 0 ? revision : 0,
                }).ToArray(),
            })
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "gameState", state = new
                {
                    matchId = "s01-match", revision, padding = new string('x', 4096), value = revision,
                },
            });
        return new(bytes, !presence, false, presence, false);
    }

    private static void AssertDelta(ReadOnlyMemory<byte> bytes, bool presence, long baseline, long revision)
    {
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(presence ? "presenceDelta" : "gameStateDelta",
            json.RootElement.GetProperty("type").GetString());
        Assert.Equal(baseline, json.RootElement.GetProperty("baseRevision").GetInt64());
        Assert.Equal(revision, json.RootElement.GetProperty("revision").GetInt64());
    }

    private static T Field<T>(object owner, string name) => Assert.IsType<T>(owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));

    private static async Task SendJson(ClientWebSocket client, object payload)
    {
        using var deadline = new CancellationTokenSource(HostDeadline);
        await client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload).AsMemory(),
            WebSocketMessageType.Text, true, deadline.Token);
    }

    private static async Task<byte[]> ReceiveType(ClientWebSocket client, string expected)
    {
        using var deadline = new CancellationTokenSource(HostDeadline);
        var buffer = new byte[16 * 1024];
        while (true)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                Assert.Equal(WebSocketMessageType.Text, result.MessageType);
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var bytes = stream.ToArray();
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.GetProperty("type").GetString() == expected) return bytes;
        }
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Eventually(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    private sealed record SendCall(ReadOnlyMemory<byte> Payload, WebSocketMessageType Type,
        bool EndOfMessage, CancellationToken Token);

    private sealed class ControlledSocket : WebSocket
    {
        private readonly ConcurrentQueue<SendCall> _calls = new();
        private WebSocketState _state = WebSocketState.Open;
        internal Func<SendCall, ValueTask> Responder { get; set; } = _ => ValueTask.CompletedTask;
        internal SendCall[] Calls => _calls.ToArray();
        internal int Aborts { get; private set; }
        public override WebSocketState State => _state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken = default)
        {
            var call = new SendCall(buffer, messageType, endOfMessage, cancellationToken);
            _calls.Enqueue(call);
            return Responder(call);
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Unexpected ArraySegment transport overload.");
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public override void Abort() { Aborts++; _state = WebSocketState.Aborted; }
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) { _state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) { _state = WebSocketState.CloseSent; return Task.CompletedTask; }
    }
}
