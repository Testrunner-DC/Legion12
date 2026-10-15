using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;
using Barrier = TwelveLegions.Platform.Tests.ChatOutboundPriorityTests.Barrier;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ChatConnectionStartFenceTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("takeover")]
    [InlineData("expired")]
    [InlineData("logout")]
    public async Task S03_LogicalRetirementClosesBeforeBindingOrOwnerPublication(string mode)
    {
        var started = Stopwatch.GetTimestamp();
        await using var f = await Host.Create();
        var old = f.Initial;
        using var borrowed = new Barrier();
        using var pair = f.Registry.TryAcquire(f.A.Id, f.B.Id)!;
        var probe = f.Configure(old, borrowed.Block);
        QueueHold? queue = null;
        Task<Peer>? replacement = null;
        Task<HttpResponseMessage>? logout = null;
        try
        {
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(old, "OLD"));
            await borrowed.WaitEntered();
            if (mode == "takeover")
            {
                replacement = f.OpenPeer(f.NewBToken());
                await Eventually(() => old.Out.ChatStartsClosed || f.Owner(f.B.Id) != old.Id);
            }
            else
            {
                // A dedicated thread holds the EXISTING queue gate; no Monitor spans an await.
                // It prevents a later terminal/Dispose fence from hiding early binding retirement.
                queue = await QueueHold.Acquire(Field<object>(old.Out, "_gate"));
                if (mode == "expired")
                {
                    f.ExpireInitialSession();
                    await Send(old.Client, new { type = "ping" });
                }
                else logout = f.LogoutInitial();
                await Eventually(() => old.Out.ChatStartsClosed || !f.HasExpectedBinding(old));
            }
            output.WriteLine($"mode={mode}; closedAtBoundary={old.Out.ChatStartsClosed}; expectedBindingPresent={f.HasExpectedBinding(old)}; ownerIsOld={f.Owner(f.B.Id) == old.Id}; actualSocketOLD={probe.Starts.Count}; elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}");
            Assert.True(old.Out.ChatStartsClosed,
                $"{mode}: old chat starts must close before logical owner/binding retirement, not only during eventual socket cleanup.");
            Assert.Empty(probe.Starts);
            queue?.Open(); borrowed.Open();

            Peer current;
            if (mode == "takeover")
            {
                current = await replacement!.WaitAsync(Deadline);
                Assert.Equal(old.Generation + 1, current.Generation);
                var notice = await ReadType(old.Client, "sessionSuperseded");
                Assert.Equal("newer-connection-generation", notice.GetProperty("reason").GetString());
                await ReadCloseAndAcknowledge(old.Client, (WebSocketCloseStatus)4002);
            }
            else
            {
                if (logout is not null)
                {
                    using var response = await logout.WaitAsync(Deadline);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
                else
                {
                    var notice = await ReadType(old.Client, "authenticationRequired");
                    Assert.Equal("platform-session-revoked", notice.GetProperty("reason").GetString());
                    await ReadCloseAndAcknowledge(old.Client, WebSocketCloseStatus.PolicyViolation);
                }
                current = await f.OpenPeer(f.NewBToken());
            }
            await Eventually(() => old.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.Empty(probe.Starts);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, pair.Entry.Generation); // Retiring a transport never bans the friend pair.
            Assert.Equal(current.Id, f.Owner(f.B.Id));
            Assert.False(current.Out.ChatStartsClosed);
            var next = f.Configure(current);
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(current, "NEW"));
            Assert.Equal("NEW", (await ReadType(current.Client, "error")).GetProperty("message").GetString());
            await Eventually(() => current.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.Equal(new[] { "NEW" }, next.Starts.ToArray());
            await Send(current.Client, new { type = "ping" });
            _ = await ReadType(current.Client, "pong");
            // A repeated stale close/notice path addresses the OLD Guid, never the current owner.
            await f.RepeatSupersede(old);
            Assert.Equal(current.Id, f.Owner(f.B.Id));
            Assert.False(current.Out.ChatStartsClosed);
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(current, "NEW-AGAIN"));
            Assert.Equal("NEW-AGAIN", (await ReadType(current.Client, "error")).GetProperty("message").GetString());
            await Eventually(() => current.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
        }
        finally
        {
            queue?.Open(); borrowed.Open();
            if (queue is not null) await queue.DisposeAsync();
            if (replacement is not null) _ = await replacement.WaitAsync(Deadline);
            if (logout is not null) { using var response = await logout.WaitAsync(Deadline); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S03_SameGuidDuplicateHelloRejectsIdentityChangeWithoutClosingValidLane(bool otherIdentity)
    {
        await using var f = await Host.Create();
        var peer = f.Initial;
        var probe = f.Configure(peer);
        await Send(peer.Client, new { type = "hello", authToken = otherIdentity ? f.A.Token : f.B.Token });
        var rejected = await ReadType(peer.Client, "connectionRejected");
        Assert.Equal("duplicate-hello", rejected.GetProperty("reason").GetString());
        Assert.True(f.HasExpectedBinding(peer));
        Assert.Equal(peer.Id, f.Owner(f.B.Id));
        Assert.False(peer.Out.ChatStartsClosed);
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(peer, "NEW"));
        Assert.Equal("NEW", (await ReadType(peer.Client, "error")).GetProperty("message").GetString());
        await Send(peer.Client, new { type = "syncState" });
        var recovery = await ReadType(peer.Client, "recoveryComplete");
        Assert.Equal(peer.Generation, recovery.GetProperty("connectionGeneration").GetInt64());
        Assert.Equal(new[] { "NEW" }, probe.Starts.ToArray());
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static async Task Send(ClientWebSocket client, object payload)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        await client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload).AsMemory(),
            WebSocketMessageType.Text, true, deadline.Token);
    }

    private static async Task<JsonElement> ReadType(ClientWebSocket client, string type)
    {
        using var deadline = new CancellationTokenSource(Deadline);
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
            using var json = JsonDocument.Parse(stream.ToArray());
            if (json.RootElement.GetProperty("type").GetString() == type) return json.RootElement.Clone();
        }
    }

    private static async Task ReadCloseAndAcknowledge(ClientWebSocket client, WebSocketCloseStatus expected)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do { result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token); }
        while (result.MessageType != WebSocketMessageType.Close);
        Assert.Equal(expected, result.CloseStatus);
        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test acknowledged", deadline.Token);
    }

    private sealed record Peer(ClientWebSocket Client, Guid Id, long Generation,
        L12OutboundConnection Out, WebSocket ServerSocket, object Binding);
    private sealed record Probe(ConcurrentQueue<string> Starts);

    private sealed class QueueHold : IAsyncDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _owner;
        private QueueHold(object gate) => _owner = Task.Run(() =>
        {
            lock (gate)
            {
                _entered.TrySetResult();
                if (!_release.Wait(Deadline)) throw new TimeoutException("test queue-gate holder was not released");
            }
        });
        internal static async Task<QueueHold> Acquire(object gate)
        {
            var hold = new QueueHold(gate);
            try { await hold._entered.Task.WaitAsync(Deadline); return hold; }
            catch { await hold.DisposeAsync(); throw; }
        }
        internal void Open() => _release.Set();
        public async ValueTask DisposeAsync()
        {
            Open();
            try { await _owner.WaitAsync(Deadline); }
            finally { _release.Dispose(); }
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        internal readonly RankedIdentityFixture Data = new();
        internal readonly L12ChatPairRegistry Registry = new(8, 8192, 8, 1024);
        internal readonly L12ChatGlobalBudget Budget = new(8, 8192);
        internal readonly (string Id, string Token, string Username, string SessionId) A, B;
        private readonly MatchRecorder _recorder;
        private readonly L12WebSocketServer _server;
        private readonly ConcurrentBag<ClientWebSocket> _clients = new();
        private readonly HttpClient _http = new() { Timeout = Deadline };
        private Uri _endpoint = null!;
        internal Peer Initial = null!;
        private IDictionary Bindings => Field<IDictionary>(_server, "_socketPlatformSessions");

        private Host()
        {
            var a = Data.Store.Register($"sa{Guid.NewGuid():N}"[..10], "Password123!");
            var b = Data.Store.Register($"sb{Guid.NewGuid():N}"[..10], "Password123!");
            Assert.True(a.Success); Assert.True(b.Success);
            A = (a.Account!.Id, a.Token!, a.Account.Username, Data.Store.AuthenticateTokenSession(a.Token)!.SessionId);
            B = (b.Account!.Id, b.Token!, b.Account.Username, Data.Store.AuthenticateTokenSession(b.Token)!.SessionId);
            Assert.True(Data.Store.SendFriendRequest(A.Id, B.Id).Success);
            Assert.True(Data.Store.ResolveFriendRequest(B.Id, A.Id, true).Success);
            _recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(Data.Path)!, "matches.db"));
            _server = new L12WebSocketServer(new L12RoomManager(Data.Catalog, _recorder, Data.Store),
                _recorder, Data.Store, Data.Catalog);
        }

        internal static async Task<Host> Create()
        {
            var host = new Host();
            try
            {
                await host._recorder.InitializeAsync();
                await host._server.StartAsync(0).WaitAsync(Deadline);
                var http = new UriBuilder(Assert.Single(host._server.Addresses)) { Host = "127.0.0.1" };
                host._http.BaseAddress = http.Uri;
                host._endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                host.Initial = await host.OpenPeer(host.B.Token);
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }

        internal async Task<Peer> OpenPeer(string token)
        {
            var client = new ClientWebSocket(); _clients.Add(client);
            using var deadline = new CancellationTokenSource(Deadline);
            await client.ConnectAsync(_endpoint, deadline.Token);
            await Send(client, new { type = "hello", authToken = token });
            var session = await ReadType(client, "session");
            _ = await ReadType(client, "recoveryComplete");
            var id = session.GetProperty("sessionId").GetGuid();
            var outbound = Field<ConcurrentDictionary<Guid, L12OutboundConnection>>(_server, "_outboundConnections")[id];
            var socket = Field<ConcurrentDictionary<Guid, WebSocket>>(_server, "_sockets")[id];
            Assert.False(outbound.HasChatLane);
            return new(client, id, session.GetProperty("connectionGeneration").GetInt64(),
                outbound, socket, Bindings[id]!);
        }

        internal Guid Owner(string accountId) => Field<ConcurrentDictionary<string, Guid>>(
            _server, "_activeAccountSockets").GetValueOrDefault(accountId);
        internal bool HasExpectedBinding(Peer peer) => ReferenceEquals(peer.Binding, Bindings[peer.Id]);
        internal string NewBToken()
        {
            var login = Data.Store.Login(B.Username, "Password123!");
            Assert.True(login.Success);
            return login.Token!;
        }

        internal Probe Configure(Peer peer, Action? borrowed = null)
        {
            var probe = new Probe(new());
            peer.Out.ConfigureChatLane(new(8, 8192, 1024, 512), Budget, Registry, () => 1,
                (a, b) => Data.Store.ReadCommittedChatRelationship(a, b) is
                    { Available: true, AcceptedAndUnblocked: true },
                (bytes, token) =>
                {
                    using var json = JsonDocument.Parse(bytes.AsMemory());
                    probe.Starts.Enqueue(json.RootElement.GetProperty("message").GetString()!);
                    return L12WebSocketServer.StartPreparedTextSend(peer.ServerSocket,
                        bytes.Array!.AsMemory(bytes.Offset, bytes.Count), token);
                }, borrowed);
            return probe;
        }

        internal L12ChatQueueResult Enqueue(Peer peer, string value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "error", message = value });
            var backing = new byte[bytes.Length + 37]; bytes.CopyTo(backing, 17);
            return peer.Out.TryEnqueueChat(A.Id, B.Id, new(backing, 17, bytes.Length), 100);
        }

        internal void ExpireInitialSession()
        {
            // Same committed-expiry fixture as ExpiredWebSocketSessionTests; no new timer/authority.
            var state = Field<object>(Data.Store, "_state");
            var rows = (IEnumerable)state.GetType().GetProperty("Sessions")!.GetValue(state)!;
            var row = rows.Cast<object>().Single(item =>
                (string)item.GetType().GetProperty("Id")!.GetValue(item)! == B.SessionId);
            row.GetType().GetProperty("ExpiresAt")!.SetValue(row, DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.True(Data.Store.SetRole(Data.Admin, B.Id, "player"));
            Assert.False(Data.Store.IsSessionActive(B.SessionId));
        }

        internal async Task<HttpResponseMessage> LogoutInitial()
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sessions/current");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", B.Token);
            return await _http.SendAsync(request);
        }

        internal Task RepeatSupersede(Peer old) => (Task)typeof(L12WebSocketServer)
            .GetMethod("SupersedeSocketAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_server, [old.Id, B.Id, CancellationToken.None])!;

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients) client.Abort();
            _http.Dispose();
            try { await _server.StopAsync().WaitAsync(Deadline); }
            finally
            {
                try { await _server.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally
                {
                    await _recorder.DisposeAsync();
                    foreach (var client in _clients) client.Dispose();
                    Data.Dispose();
                }
            }
        }
    }
}
