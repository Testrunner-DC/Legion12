using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;
using Barrier = TwelveLegions.Platform.Tests.ChatOutboundPriorityTests.Barrier;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ChatBlockSendHttpTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task S02_HttpRevocationAndNewFriendshipNeverReviveOldUnstartedMessage(bool block)
    {
        var started = Stopwatch.GetTimestamp();
        await using var f = await Host.Create();
        using var borrowed = new Barrier();
        f.Configure(borrowed: borrowed.Block);
        try
        {
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue("OLD"));
            await borrowed.WaitEntered();
            using (var revoke = await f.Send(block ? HttpMethod.Post : HttpMethod.Delete,
                block ? "/api/friends/blocked" : $"/api/friends/{f.A.Id}",
                block ? new { accountId = f.A.Id } : null))
                Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            if (block)
            {
                using var unblock = await f.Send(HttpMethod.Delete, $"/api/friends/blocked/{f.A.Id}");
                Assert.Equal(HttpStatusCode.OK, unblock.StatusCode);
            }
            await f.BefriendViaHttp();
            Assert.True(f.Data.Store.ReadCommittedChatRelationship(f.A.Id, f.B.Id).AcceptedAndUnblocked);
            borrowed.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            var observed = await f.ReadThroughMarker();
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue("NEW"));
            Assert.Equal("NEW", await f.ReadError());
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            output.WriteLine($"block={block}; actualSocketOLD={f.ActualStarts.Count(x => x == "OLD")}; realClientOLD={observed.Contains("OLD")}; realClientNEW=true; elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}");
            Assert.DoesNotContain("OLD", observed);
            Assert.DoesNotContain("OLD", f.ActualStarts);
            Assert.Equal(1, f.ActualStarts.Count(x => x == "NEW"));
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, f.Registry.Snapshot.Pending);
        }
        finally { borrowed.Open(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S02_DuplicateBlockInvalidatesInEitherDirectionWithoutAnotherStoreCommit(bool reverse)
    {
        await using var f = await Host.Create();
        using var pair = f.Registry.TryAcquire(f.A.Id, f.B.Id)!;
        var target = reverse ? f.B.Id : f.A.Id;
        using (var first = await f.Send(HttpMethod.Post, "/api/friends/blocked",
            new { accountId = target }, asA: reverse)) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, pair.Entry.Generation);
        var version = f.Data.Store.Version;
        using (var duplicate = await f.Send(HttpMethod.Post, "/api/friends/blocked",
            new { accountId = target }, asA: reverse)) Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(version, f.Data.Store.Version);
        Assert.Equal(2, pair.Entry.Generation);
        Assert.Equal(0, pair.Entry.Pending);
        Assert.False(pair.Entry.RecoveryClosed);
        Assert.Empty(f.ActualStarts);
        Assert.Equal(WebSocketState.Open, f.Client.State);
    }

    [Theory]
    [InlineData("remove", 404)]
    [InlineData("unblock", 404)]
    [InlineData("missing", 400)]
    [InlineData("self", 400)]
    [InlineData("unauthorized", 401)]
    public async Task S02_BusinessRejectionAndUnauthorizedKeepOriginalStatusAndReleasePending(string mode, int status)
    {
        await using var f = await Host.Create();
        if (mode == "remove")
        {
            using var remove = await f.Send(HttpMethod.Delete, $"/api/friends/{f.A.Id}");
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        }
        using var pair = f.Registry.TryAcquire(f.A.Id, f.B.Id)!;
        var generation = pair.Entry.Generation;
        using var response = mode switch
        {
            "remove" => await f.Send(HttpMethod.Delete, $"/api/friends/{f.A.Id}"),
            "unblock" => await f.Send(HttpMethod.Delete, $"/api/friends/blocked/{f.A.Id}"),
            "missing" => await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = "not-an-account" }),
            "self" => await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.B.Id }),
            _ => await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id }, authorized: false),
        };
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(generation, pair.Entry.Generation);
        Assert.Equal(0, f.Registry.Snapshot.Pending);
        Assert.False(pair.Entry.RecoveryClosed);
        Assert.Empty(f.ActualStarts);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("remove")]
    [InlineData("unblock")]
    public async Task S02_StorageExceptionStaysUnknownEvenWhenCommittedFactsLookUnchanged(string route)
    {
        await using var f = await Host.Create();
        using var pair = f.Registry.TryAcquire(f.A.Id, f.B.Id)!;
        if (route == "unblock")
        {
            using var block = await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id });
            Assert.Equal(HttpStatusCode.OK, block.StatusCode);
        }
        var generation = pair.Entry.Generation;
        var before = f.Data.Store.ReadCommittedChatRelationship(f.A.Id, f.B.Id);
        var injected = 0;
        f.Data.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") { Interlocked.Increment(ref injected); throw new IOException("S02 controlled pre-commit failure"); }
        };
        using var response = await f.Send(route == "block" ? HttpMethod.Post : HttpMethod.Delete,
            route == "block" ? "/api/friends/blocked" : route == "remove"
                ? $"/api/friends/{f.A.Id}" : $"/api/friends/blocked/{f.A.Id}",
            route == "block" ? new { accountId = f.A.Id } : null);
        f.Data.Store.StorageFailureInjector = null;
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(1, injected);
        Assert.Equal(generation, pair.Entry.Generation);
        Assert.Equal(0, pair.Entry.Pending);
        Assert.True(pair.Entry.RecoveryClosed);
        var after = f.Data.Store.ReadCommittedChatRelationship(f.A.Id, f.B.Id);
        Assert.True(after.Available);
        Assert.Equal(before.AcceptedAndUnblocked, after.AcceptedAndUnblocked);
        f.Configure();
        Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue("must-stay-closed"));
        Assert.Empty(await f.ReadThroughMarker());
        Assert.Empty(f.ActualStarts);
        Assert.Equal((0, 0L), f.Budget.Snapshot);
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("entry-bytes")]
    [InlineData("pending")]
    [InlineData("pending-bytes")]
    public async Task S02_FenceCapacityRejectsBeforeAuthorityWithoutAbortingOrEvictingLegacy(string capacity)
    {
        var registry = capacity switch
        {
            "entries" => new L12ChatPairRegistry(1, 2048, 8, 1024),
            "entry-bytes" => new L12ChatPairRegistry(1, 512, 8, 1024),
            "pending" => new L12ChatPairRegistry(8, 8192, 1, 1024),
            _ => new L12ChatPairRegistry(8, 8192, 8, 128),
        };
        await using var f = await Host.Create(registry);
        using var other = capacity == "entries" ? registry.TryAcquire("other-a", "other-b") : null;
        using var mutation = capacity is "pending" or "pending-bytes"
            ? registry.TryBeginMutation("other-a", "other-b") : null;
        try
        {
            var version = f.Data.Store.Version;
            using var response = await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(version, f.Data.Store.Version);
            Assert.True(f.Data.Store.ReadCommittedChatRelationship(f.A.Id, f.B.Id).AcceptedAndUnblocked);
            Assert.Equal(capacity is "pending" or "pending-bytes" ? 1 : 0, registry.Snapshot.Pending);
            Assert.Empty(await f.ReadThroughMarker());
            Assert.False(f.Out.ChatStartsClosed);
            Assert.Empty(f.ActualStarts);
            Assert.Equal(WebSocketState.Open, f.Client.State);
        }
        finally { mutation?.Complete(L12ChatMutationOutcome.KnownRollback); }
    }

    [Fact]
    public async Task S02_PreclosePrecedesSqlWhileLegacySendingDoesNotWaitForSql()
    {
        await using var f = await Host.Create();
        using var beforeCommit = new Barrier();
        using var pair = f.Registry.TryAcquire(f.A.Id, f.B.Id)!;
        f.Configure();
        f.Data.Store.StorageFailureInjector = stage => { if (stage == "before-commit") beforeCommit.Block(); };
        var change = f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id });
        try
        {
            await beforeCommit.WaitEntered();
            Assert.Equal(1, pair.Entry.Pending);
            Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue("paused-before-sql-commit"));
            Assert.Empty(await f.ReadThroughMarker());
            Assert.Empty(f.ActualStarts);
            beforeCommit.Open();
            using var response = await change.WaitAsync(Deadline);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, pair.Entry.Pending);
            Assert.Equal(1, pair.Entry.Generation);
        }
        finally { beforeCommit.Open(); using var response = await change.WaitAsync(Deadline); }
    }

    [Fact]
    public async Task S02_ActualSocketStartWinsBeforeHttpSuccessWhenFinalCheckAlreadyOwnsGate()
    {
        await using var f = await Host.Create();
        using var fresh = new Barrier();
        var order = new ConcurrentQueue<string>();
        f.Fresh = (_, _) => { fresh.Block(); return true; };
        f.BeforeActualStart = () => order.Enqueue("socket-invoked");
        f.Configure();
        Task<HttpResponseMessage>? change = null;
        try
        {
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue("already-started"));
            await fresh.WaitEntered();
            change = f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id });
            fresh.Open();
            using var response = await change.WaitAsync(Deadline);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            order.Enqueue("http-success");
            Assert.Equal("already-started", await f.ReadError());
            Assert.Equal(new[] { "socket-invoked", "http-success" }, order.ToArray());
        }
        finally { fresh.Open(); if (change is not null) { using var response = await change.WaitAsync(Deadline); } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S02_HttpMutationDoesNotReopenClosedOrCompletingConnection(bool complete)
    {
        await using var f = await Host.Create();
        using var borrowed = new Barrier();
        f.Configure(borrowed.Block);
        Task? closing = null;
        try
        {
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue("OLD"));
            await borrowed.WaitEntered();
            if (complete) closing = f.Out.CompleteAsync(false);
            else f.Out.CloseChatStarts();
            using var response = await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            borrowed.Open();
            if (closing is not null) await closing.WaitAsync(Deadline);
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.True(f.Out.ChatStartsClosed);
            Assert.Equal(L12ChatQueueResult.Suppressed, f.Enqueue("must-stay-closed"));
            Assert.Empty(f.ActualStarts);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, f.Registry.Snapshot.Pending);
        }
        finally { borrowed.Open(); if (closing is not null) await closing.WaitAsync(Deadline); }
    }

    [Fact]
    public async Task S02_SuccessfulHttpNeverReopensAnEarlierUnknownPair()
    {
        await using var f = await Host.Create();
        using var pair = f.Registry.TryAcquire(f.A.Id, f.B.Id)!;
        f.Registry.TryBeginMutation(f.A.Id, f.B.Id)!.Dispose();
        f.Configure();
        using (var block = await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id }))
            Assert.Equal(HttpStatusCode.OK, block.StatusCode);
        using (var unblock = await f.Send(HttpMethod.Delete, $"/api/friends/blocked/{f.A.Id}"))
            Assert.Equal(HttpStatusCode.OK, unblock.StatusCode);
        await f.BefriendViaHttp();
        Assert.True(f.Data.Store.ReadCommittedChatRelationship(f.A.Id, f.B.Id).AcceptedAndUnblocked);
        Assert.True(pair.Entry.RecoveryClosed);
        Assert.Equal(0, pair.Entry.Pending);
        Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue("must-stay-closed"));
        Assert.Empty(await f.ReadThroughMarker());
        Assert.Empty(f.ActualStarts);
    }

    [Fact]
    public async Task S02_NullRegistryPreservesExistingHttpFlowAndDoesNotConfigureLane()
    {
        await using var f = await Host.Create(wired: false);
        using (var block = await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id }))
            Assert.Equal(HttpStatusCode.OK, block.StatusCode);
        using (var duplicate = await f.Send(HttpMethod.Post, "/api/friends/blocked", new { accountId = f.A.Id }))
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using (var unblock = await f.Send(HttpMethod.Delete, $"/api/friends/blocked/{f.A.Id}"))
            Assert.Equal(HttpStatusCode.OK, unblock.StatusCode);
        await f.BefriendViaHttp();
        using (var remove = await f.Send(HttpMethod.Delete, $"/api/friends/{f.A.Id}"))
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        using (var missing = await f.Send(HttpMethod.Delete, $"/api/friends/{f.A.Id}"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False(f.Out.HasChatLane);
        Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
        Assert.Empty(await f.ReadThroughMarker());
    }

    private static async Task Eventually(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    private sealed class Host : IAsyncDisposable
    {
        internal readonly RankedIdentityFixture Data = new();
        internal readonly L12ChatPairRegistry Registry;
        internal readonly L12ChatGlobalBudget Budget = new(8, 8192);
        internal readonly ConcurrentQueue<string> ActualStarts = new();
        internal readonly (string Id, string Token) A, B;
        internal readonly MatchRecorder Recorder;
        internal readonly L12WebSocketServer Server;
        internal readonly ClientWebSocket Client = new();
        internal readonly HttpClient Http = new() { Timeout = Deadline };
        internal L12OutboundConnection Out = null!;
        internal WebSocket ServerSocket = null!;
        internal Func<string, string, bool>? Fresh;
        internal Action? BeforeActualStart;

        private Host(L12ChatPairRegistry? registry, bool wired)
        {
            Registry = registry ?? new(8, 8192, 8, 1024);
            var a = Data.Store.Register($"ha{Guid.NewGuid():N}"[..10], "Password123!");
            var b = Data.Store.Register($"hb{Guid.NewGuid():N}"[..10], "Password123!");
            Assert.True(a.Success); Assert.True(b.Success);
            A = (a.Account!.Id, a.Token!); B = (b.Account!.Id, b.Token!);
            Assert.True(Data.Store.SendFriendRequest(A.Id, B.Id).Success);
            Assert.True(Data.Store.ResolveFriendRequest(B.Id, A.Id, true).Success);
            Recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(Data.Path)!, "matches.db"));
            Server = new L12WebSocketServer(new L12RoomManager(Data.Catalog, Recorder, Data.Store),
                Recorder, Data.Store, Data.Catalog);
            if (wired) typeof(L12WebSocketServer).GetField("_chatMutationRegistry",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Server, Registry);
        }

        internal static async Task<Host> Create(L12ChatPairRegistry? registry = null, bool wired = true)
        {
            var host = new Host(registry, wired);
            try
            {
                await host.Recorder.InitializeAsync();
                await host.Server.StartAsync(0).WaitAsync(Deadline);
                var http = new UriBuilder(Assert.Single(host.Server.Addresses)) { Host = "127.0.0.1" };
                host.Http.BaseAddress = http.Uri;
                using var deadline = new CancellationTokenSource(Deadline);
                await host.Client.ConnectAsync(new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri, deadline.Token);
                await host.Client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new
                    { type = "hello", authToken = host.B.Token }).AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
                var session = await host.ReadType("session");
                var id = session.GetProperty("sessionId").GetGuid();
                _ = await host.ReadType("recoveryComplete");
                host.Out = Field<ConcurrentDictionary<Guid, L12OutboundConnection>>(host.Server, "_outboundConnections")[id];
                host.ServerSocket = Field<ConcurrentDictionary<Guid, WebSocket>>(host.Server, "_sockets")[id];
                Assert.False(host.Out.HasChatLane);
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }

        internal void Configure(Action? borrowed = null)
            => Out.ConfigureChatLane(new(8, 8192, 1024, 512), Budget, Registry, () => 1,
                (a, b) => Fresh?.Invoke(a, b) ?? Data.Store.ReadCommittedChatRelationship(a, b) is
                    { Available: true, AcceptedAndUnblocked: true },
                (bytes, token) =>
                {
                    using var json = JsonDocument.Parse(bytes.AsMemory());
                    ActualStarts.Enqueue(json.RootElement.GetProperty("message").GetString()!);
                    BeforeActualStart?.Invoke();
                    return L12WebSocketServer.StartPreparedTextSend(ServerSocket,
                        bytes.Array!.AsMemory(bytes.Offset, bytes.Count), token);
                }, borrowed);

        internal L12ChatQueueResult Enqueue(string value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "error", message = value });
            var backing = new byte[bytes.Length + 37];
            bytes.CopyTo(backing, 17);
            return Out.TryEnqueueChat(A.Id, B.Id, new(backing, 17, bytes.Length), 100);
        }

        internal async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null,
            bool asA = false, bool authorized = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (authorized) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", asA ? A.Token : B.Token);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await Http.SendAsync(request);
        }

        internal async Task BefriendViaHttp()
        {
            using var request = await Send(HttpMethod.Post, "/api/friends/requests", new { accountId = B.Id }, asA: true);
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
            using var accept = await Send(HttpMethod.Post, $"/api/friends/requests/{A.Id}/resolve", new { accept = true });
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        }

        internal async Task<string[]> ReadThroughMarker()
        {
            var marker = $"MARKER-{Guid.NewGuid():N}";
            Assert.True(Out.TryEnqueue(new L12QueuedPayload(JsonSerializer.SerializeToUtf8Bytes(
                new { type = "error", message = marker }), false, false)));
            var observed = new List<string>();
            while (true)
            {
                var value = await ReadError();
                if (value == marker) return observed.ToArray();
                observed.Add(value);
            }
        }

        internal async Task<string> ReadError() => (await ReadType("error")).GetProperty("message").GetString()!;

        private async Task<JsonElement> ReadType(string type)
        {
            using var deadline = new CancellationTokenSource(Deadline);
            var buffer = new byte[16 * 1024];
            while (true)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await Client.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                    Assert.Equal(WebSocketMessageType.Text, result.MessageType);
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                using var json = JsonDocument.Parse(stream.ToArray());
                if (json.RootElement.GetProperty("type").GetString() == type) return json.RootElement.Clone();
            }
        }

        private static T Field<T>(object owner, string name) => Assert.IsType<T>(owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));

        public async ValueTask DisposeAsync()
        {
            Data.Store.StorageFailureInjector = null;
            Client.Abort(); Http.Dispose();
            try { await Server.StopAsync().WaitAsync(Deadline); }
            finally
            {
                try { await Server.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally { await Recorder.DisposeAsync(); Client.Dispose(); Data.Dispose(); }
            }
        }
    }
}
