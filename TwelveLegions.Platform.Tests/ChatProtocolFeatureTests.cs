using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ChatProtocolFeatureTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FriendHostPersistsPagesDeduplicatesReadsBlocksAndSlowSqliteDoesNotBlockGameFifo()
    {
        await using var host = await FeatureHost.CreateAsync();
        var baseTime = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var worst = new string('<', L12ChatTextPolicy.MaximumGraphemes);
        for (var index = 0; index < 100; index++)
        {
            var issued = baseTime.AddMilliseconds(index);
            host.Data.Store.AppendCommittedFriendChat(host.A.Id, host.B.Id, RequestId(issued),
                L12ChatTextPolicy.Fingerprint("friend", host.B.Id, null, worst), Apply(worst),
                issued, issued, () => true);
        }

        await using var a = await SocketPeer.ConnectAsync(host.Endpoint, host.A.Token);
        await using var b = await SocketPeer.ConnectAsync(host.Endpoint, host.B.Token);
        Assert.True(a.ChatV1);
        Assert.True(b.ChatV1);
        Assert.InRange(Math.Abs(a.ServerUtcMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), 0L, 10_000L);

        var busyRequest = RequestId(a.ServerNow());
        using (var blocker = Open(host.Data.Store.TransactionalStoragePath, SqliteOpenMode.ReadWrite))
        {
            Execute(blocker, "PRAGMA busy_timeout=1000; BEGIN IMMEDIATE;");
            await a.SendAsync(new
            {
                type = "chatSend", requestId = busyRequest, scope = "friend",
                peerAccountId = host.B.Id, text = "slow-write",
            });
            await a.SendAsync(new { type = "ping" });
            await a.SendAsync(new { type = "createRoom", options = new { matchModeId = "friendly" } });
            var observed = Stopwatch.StartNew();
            var sawPong = false;
            var sawRoom = false;
            var sawBusy = false;
            while (!(sawPong && sawRoom && sawBusy))
            {
                var frame = await a.ReceiveOneAsync(Deadline);
                var type = String(frame.Payload, "type");
                sawPong |= type == "pong";
                sawRoom |= type == "roomState";
                sawBusy |= type == "chatAck"
                           && String(frame.Payload, "requestId") == busyRequest
                           && !frame.Payload.GetProperty("success").GetBoolean()
                           && String(frame.Payload, "code") == "busy";
            }
            Assert.True(observed.Elapsed < TimeSpan.FromSeconds(2),
                $"chat SQLite contention delayed ping/room FIFO for {observed.Elapsed}");
            Execute(blocker, "ROLLBACK;");
        }

        var first = await SendChatAsync(a, b, busyRequest, "friend", host.B.Id, null, "slow-write");
        Assert.False(first.Ack.GetProperty("duplicate").GetBoolean());
        var firstId = String(first.Ack.GetProperty("entry"), "id")!;
        Assert.Equal(firstId, String(first.Message.GetProperty("entry"), "id"));

        await a.SendAsync(new
        {
            type = "chatSend", requestId = busyRequest, scope = "friend",
            peerAccountId = host.B.Id, text = "slow-write",
        });
        var duplicate = await a.ReceiveAsync(frame => String(frame, "type") == "chatAck"
            && String(frame, "requestId") == busyRequest, Deadline);
        Assert.True(duplicate.Payload.GetProperty("success").GetBoolean());
        Assert.True(duplicate.Payload.GetProperty("duplicate").GetBoolean());
        Assert.Equal(firstId, String(duplicate.Payload.GetProperty("entry"), "id"));

        var nextRequest = RequestId(a.ServerNow());
        var next = await SendChatAsync(a, b, nextRequest, "friend", host.B.Id, null, "after-duplicate");
        Assert.NotEqual(firstId, String(next.Message.GetProperty("entry"), "id"));
        var nextSeq = next.Ack.GetProperty("seq").GetInt64();

        await b.SendAsync(new
        {
            type = "chatRead", requestId = RequestId(b.ServerNow()), scope = "friend",
            peerAccountId = host.A.Id, seq = nextSeq,
        });
        var read = await b.ReceiveTypeAsync("chatReadAck", Deadline);
        Assert.True(read.Payload.GetProperty("success").GetBoolean());
        Assert.InRange(read.Payload.GetProperty("seq").GetInt64(), 0, nextSeq);

        var allSequences = await ReadAllHistoryAsync(a, "friend", host.B.Id, null);
        Assert.Equal(102, allSequences.Count);
        Assert.Equal(Enumerable.Range(1, 102).Select(value => (long)value), allSequences.Order());

        using (var http = new HttpClient { BaseAddress = host.HttpEndpoint, Timeout = Deadline })
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/friends/blocked")
        {
            Content = JsonContent.Create(new { accountId = host.A.Id }),
        })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.B.Token);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        var blockedRequest = RequestId(a.ServerNow());
        await a.SendAsync(new
        {
            type = "chatSend", requestId = blockedRequest, scope = "friend",
            peerAccountId = host.B.Id, text = "must-not-store",
        });
        var blocked = await a.ReceiveAsync(frame => String(frame, "type") == "chatAck"
            && String(frame, "requestId") == blockedRequest, Deadline);
        Assert.False(blocked.Payload.GetProperty("success").GetBoolean());
        Assert.Equal("scopeUnavailable", String(blocked.Payload, "code"));
    }

    [Fact]
    public async Task OpponentHostAllowsBothPlayersCapsHistoryAndScopeGateSerializesWithdrawal()
    {
        await using var host = await FeatureHost.CreateAsync();
        await using var a = await SocketPeer.ConnectAsync(host.Endpoint, host.A.Token);
        await using var b = await SocketPeer.ConnectAsync(host.Endpoint, host.B.Token);
        var matchId = await StartFriendlyGameAsync(a, b);

        var reverse = await SendChatAsync(b, a, RequestId(b.ServerNow()), "opponent",
            host.A.Id, matchId, "reverse");
        Assert.Equal(1, reverse.Ack.GetProperty("seq").GetInt64());
        var worst = new string('<', L12ChatTextPolicy.MaximumGraphemes);
        for (var index = 0; index < 99; index++)
        {
            var sent = await SendChatAsync(a, b, RequestId(a.ServerNow()), "opponent",
                host.B.Id, matchId, worst);
            Assert.Equal(index + 2, sent.Ack.GetProperty("seq").GetInt64());
        }
        var allSequences = await ReadAllHistoryAsync(a, "opponent", host.B.Id, matchId);
        Assert.Equal(Enumerable.Range(1, 100).Select(value => (long)value), allSequences.Order());

        var expiredRequest = RequestId(a.ServerNow().AddHours(-73));
        await a.SendAsync(new
        {
            type = "chatSend", requestId = expiredRequest, scope = "opponent",
            peerAccountId = host.B.Id, matchId, text = "expired",
        });
        var expired = await a.ReceiveAsync(frame => String(frame, "type") == "chatAck"
            && String(frame, "requestId") == expiredRequest, Deadline);
        Assert.False(expired.Payload.GetProperty("success").GetBoolean());
        Assert.Equal("invalidRequest", String(expired.Payload, "code"));

        var scope = host.Rooms.ReadCommittedOpponentChatScope(host.A.Id, host.B.Id, matchId);
        Assert.NotNull(scope);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var origin = new L12ChatRequestIdentity(a.SessionId, string.Empty, host.A.Id,
            scope!.FirstAccountId == host.A.Id ? scope.FirstConnectionGeneration : scope.SecondConnectionGeneration);
        var transport = new L12ChatRequestIdentity(b.SessionId, string.Empty, host.B.Id,
            scope.FirstAccountId == host.B.Id ? scope.FirstConnectionGeneration : scope.SecondConnectionGeneration);
        var metadata = new L12ChatDeliveryMetadata(host.A.Id, host.B.Id,
            L12ChatDeliveryScope.Opponent, host.A.Id, host.B.Id, matchId,
            scope.ScopeGeneration, transport, origin);
        var actualStart = Task.Factory.StartNew(() => host.Rooms.StartCommittedOpponentChatScope(metadata, () =>
        {
            entered.Set();
            Assert.True(release.Wait(Deadline));
            return L12ChatQueueResult.ActuallyStarted;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(entered.Wait(Deadline));
        var withdrawal = Task.Factory.StartNew(() => host.Rooms.HandleActionAsync(a.SessionId,
                JsonSerializer.SerializeToElement(new { type = "surrender" }, Wire),
                $"chat-surrender-{Guid.NewGuid():N}"), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        try
        {
            await Task.Delay(100);
            Assert.False(withdrawal.IsCompleted);
            release.Set();
            Assert.Equal(L12ChatQueueResult.ActuallyStarted, await actualStart.WaitAsync(Deadline));
            _ = await withdrawal.WaitAsync(Deadline);
        }
        finally { release.Set(); }
        Assert.Null(host.Rooms.ReadCommittedOpponentChatScope(host.A.Id, host.B.Id, matchId));
        Assert.Equal(L12ChatQueueResult.Suppressed,
            host.Rooms.StartCommittedOpponentChatScope(metadata,
                () => L12ChatQueueResult.ActuallyStarted));

        var runtime = typeof(L12WebSocketServer).GetField("_chatRuntime",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host.Server)!;
        var conversations = Assert.IsAssignableFrom<IDictionary>(runtime.GetType().GetField("_opponent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime));
        Assert.Empty(conversations.Keys.Cast<object>());
    }

    private static async Task<(JsonElement Ack, JsonElement Message)> SendChatAsync(SocketPeer sender,
        SocketPeer recipient, string requestId, string scope, string peerAccountId, string? matchId,
        string text)
    {
        await sender.SendAsync(new { type = "chatSend", requestId, scope, peerAccountId, matchId, text });
        var ack = await sender.ReceiveAsync(frame => String(frame, "type") == "chatAck"
            && String(frame, "requestId") == requestId, Deadline);
        var message = await recipient.ReceiveTypeAsync("chatMessage", Deadline);
        Assert.True(ack.Payload.GetProperty("success").GetBoolean());
        return (ack.Payload, message.Payload);
    }

    private static async Task<HashSet<long>> ReadAllHistoryAsync(SocketPeer peer, string scope,
        string peerAccountId, string? matchId)
    {
        var sequences = new HashSet<long>();
        long? before = null;
        for (var page = 0; page < 16; page++)
        {
            var requestId = RequestId(peer.ServerNow());
            var request = new Dictionary<string, object?>
            {
                ["type"] = "chatSync",
                ["requestId"] = requestId,
                ["scope"] = scope,
                ["peerAccountId"] = peerAccountId,
                ["matchId"] = matchId,
            };
            if (before is { } cursor) request["beforeSeq"] = cursor;
            await peer.SendAsync(request);
            var history = await peer.ReceiveAsync(frame => String(frame, "type") == "chatHistory"
                && String(frame, "requestId") == requestId, Deadline);
            Assert.InRange(history.ByteCount, 1, 64 * 1024);
            var entries = history.Payload.GetProperty("entries").EnumerateArray().ToArray();
            Assert.NotEmpty(entries);
            foreach (var entry in entries) Assert.True(sequences.Add(entry.GetProperty("seq").GetInt64()));
            if (!history.Payload.GetProperty("hasMore").GetBoolean()) return sequences;
            before = entries[0].GetProperty("seq").GetInt64();
        }
        throw new Xunit.Sdk.XunitException("chat history pagination did not terminate");
    }

    private static async Task<string> StartFriendlyGameAsync(SocketPeer a, SocketPeer b)
    {
        await a.SendAsync(new { type = "createRoom", options = new { matchModeId = "friendly" } });
        var created = await a.ReceiveTypeAsync("roomState", Deadline);
        var roomCode = String(created.Payload, "roomCode");
        Assert.False(string.IsNullOrWhiteSpace(roomCode));
        var aJoined = a.ReceiveTypeAsync("roomState", Deadline);
        var bJoined = b.ReceiveTypeAsync("roomState", Deadline);
        await b.SendAsync(new { type = "joinRoom", roomCode });
        await Task.WhenAll(aJoined, bJoined);
        var aReady = a.ReceiveTypeAsync("roomState", Deadline);
        var bSeesReady = b.ReceiveTypeAsync("roomState", Deadline);
        await a.SendAsync(new { type = "ready", ready = true });
        await Task.WhenAll(aReady, bSeesReady);
        var aStarted = a.ReceiveTypeAsync("gameState", Deadline);
        var bStarted = b.ReceiveTypeAsync("gameState", Deadline);
        await b.SendAsync(new { type = "ready", ready = true });
        var started = await Task.WhenAll(aStarted, bStarted);
        return started[0].Payload.GetProperty("state").GetProperty("matchId").GetString()!;
    }

    private static L12ChatTextResult Apply(string source)
    {
        Assert.True(L12ChatTextPolicy.TryApply(source, out var result, out var error), error);
        return result;
    }

    private static string RequestId(DateTimeOffset issuedAt)
        => $"c1.{issuedAt.ToUnixTimeMilliseconds()}.{Guid.NewGuid():D}";

    private static string? String(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record ReceivedFrame(JsonElement Payload, int ByteCount);

    private sealed class SocketPeer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private long _serverUtcAtHello;
        private long _monotonicAtHello;
        internal Guid SessionId { get; private set; }
        internal long ServerUtcMs => _serverUtcAtHello;
        internal bool ChatV1 { get; private set; }

        internal static async Task<SocketPeer> ConnectAsync(Uri endpoint, string token)
        {
            var peer = new SocketPeer();
            try
            {
                using var deadline = new CancellationTokenSource(Deadline);
                await peer._socket.ConnectAsync(endpoint, deadline.Token);
                await peer.SendAsync(new
                {
                    type = "hello", authToken = token,
                    capabilities = new { requestIds = true, chatV1 = true },
                });
                var session = await peer.ReceiveTypeAsync("session", Deadline);
                peer.SessionId = session.Payload.GetProperty("sessionId").GetGuid();
                peer._serverUtcAtHello = session.Payload.GetProperty("chatServerUtcMs").GetInt64();
                peer._monotonicAtHello = Stopwatch.GetTimestamp();
                peer.ChatV1 = session.Payload.GetProperty("capabilities").GetProperty("chatV1").GetBoolean();
                _ = await peer.ReceiveTypeAsync("recoveryComplete", Deadline);
                return peer;
            }
            catch
            {
                peer._socket.Dispose();
                throw;
            }
        }

        internal DateTimeOffset ServerNow()
            => DateTimeOffset.FromUnixTimeMilliseconds(_serverUtcAtHello
                + (long)Stopwatch.GetElapsedTime(_monotonicAtHello).TotalMilliseconds);

        internal Task SendAsync(object payload)
            => _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload, Wire).AsMemory(),
                WebSocketMessageType.Text, true, CancellationToken.None).AsTask();

        internal Task<ReceivedFrame> ReceiveTypeAsync(string type, TimeSpan timeout)
            => ReceiveAsync(payload => String(payload, "type") == type, timeout);

        internal async Task<ReceivedFrame> ReceiveAsync(Func<JsonElement, bool> predicate,
            TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            while (true)
            {
                var frame = await ReceiveOneAsync(deadline.Token);
                if (predicate(frame.Payload)) return frame;
            }
        }

        internal async Task<ReceivedFrame> ReceiveOneAsync(TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            return await ReceiveOneAsync(deadline.Token);
        }

        private async Task<ReceivedFrame> ReceiveOneAsync(CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            var buffer = new byte[16 * 1024];
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("socket closed before expected chat frame");
                await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            } while (!result.EndOfMessage);
            var bytes = message.ToArray();
            using var document = JsonDocument.Parse(bytes);
            return new(document.RootElement.Clone(), bytes.Length);
        }

        public ValueTask DisposeAsync()
        {
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FeatureHost : IAsyncDisposable
    {
        internal readonly RankedIdentityFixture Data = new();
        internal readonly (string Id, string Token) A;
        internal readonly (string Id, string Token) B;
        internal readonly MatchRecorder Recorder;
        internal readonly L12RoomManager Rooms;
        internal readonly L12WebSocketServer Server;
        internal Uri Endpoint { get; private set; } = null!;
        internal Uri HttpEndpoint { get; private set; } = null!;

        private FeatureHost()
        {
            var a = Data.Store.Register($"wa{Guid.NewGuid():N}"[..10], "Password123!");
            var b = Data.Store.Register($"wb{Guid.NewGuid():N}"[..10], "Password123!");
            Assert.True(a.Success);
            Assert.True(b.Success);
            A = (a.Account!.Id, a.Token!);
            B = (b.Account!.Id, b.Token!);
            Assert.True(Data.Store.SendFriendRequest(A.Id, B.Id).Success);
            Assert.True(Data.Store.ResolveFriendRequest(B.Id, A.Id, true).Success);
            Recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(Data.Path)!, "chat-feature.db"));
            Rooms = new L12RoomManager(Data.Catalog, Recorder, Data.Store);
            Server = new L12WebSocketServer(Rooms, Recorder, Data.Store, Data.Catalog,
                chatEnabled: true);
        }

        internal static async Task<FeatureHost> CreateAsync()
        {
            var host = new FeatureHost();
            try
            {
                await host.Recorder.InitializeAsync();
                await host.Server.StartAsync(0).WaitAsync(Deadline);
                host.HttpEndpoint = new UriBuilder(Assert.Single(host.Server.Addresses))
                    { Host = "127.0.0.1" }.Uri;
                host.Endpoint = new UriBuilder(host.HttpEndpoint) { Scheme = "ws", Path = "/ws" }.Uri;
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await Server.StopAsync().WaitAsync(Deadline); }
            catch { }
            await Server.DisposeAsync();
            await Recorder.DisposeAsync();
            Data.Dispose();
        }
    }
}
