using System.Collections;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class WebSocketInboundIsolationTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BusinessQueueIsSingleConsumerFifo()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        var active = 0;
        var maximumActive = 0;
        var inbound = new L12InboundConnection(async json =>
        {
            var current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            observed.Add(json);
            if (json == "first")
            {
                entered.TrySetResult();
                await release.Task;
            }
            Interlocked.Decrement(ref active);
        });

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("first"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("second"));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("third"));
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(["first", "second", "third"], observed);
            Assert.Equal(1, maximumActive);
            Assert.Equal(0, inbound.RetainedMessages);
            Assert.Equal(0, inbound.RetainedBytes);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task RetainedBudgetIncludesExecutingLegacyMaximumFrame()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = 0;
        var inbound = new L12InboundConnection(async _ =>
        {
            Interlocked.Increment(ref handled);
            entered.TrySetResult();
            await release.Task;
        });
        var legacyMaximumAsciiFrame = new string('a', 1024 * 1024);

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue(legacyMaximumAsciiFrame));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, inbound.RetainedMessages);
            Assert.Equal(96L + 2L * 1024 * 1024, inbound.RetainedBytes);
            Assert.Equal(L12InboundEnqueueResult.RetainedByteLimit,
                inbound.TryEnqueue(legacyMaximumAsciiFrame));
            Assert.True(inbound.MaximumObservedBytes <= L12InboundConnection.MaximumRetainedBytes);
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, handled);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task MessageCountIncludesExecutingItemAndFailsClosed()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inbound = new L12InboundConnection(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        });

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("gate"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            for (var index = 1; index < L12InboundConnection.MaximumRetainedMessages; index++)
                Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue($"queued-{index}"));
            Assert.Equal(L12InboundEnqueueResult.MessageLimit, inbound.TryEnqueue("overflow"));
            Assert.Equal(L12InboundConnection.MaximumRetainedMessages, inbound.RetainedMessages);
            inbound.StopAcceptingAndCancelPending();
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ClosingConnectionCancelsOnlyNotStartedMessages()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        var inbound = new L12InboundConnection(async json =>
        {
            observed.Add(json);
            entered.TrySetResult();
            await release.Task;
        });

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("started"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("old-generation-pending"));
            inbound.StopAcceptingAndCancelPending();
            Assert.Equal(L12InboundEnqueueResult.Completed, inbound.TryEnqueue("late"));
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(["started"], observed);
            Assert.Equal(0, inbound.RetainedMessages);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task AuthenticatedPingBypassesBlockedSyncStateOnSameSocket()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-ping", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"iq{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            using var socket = new ClientWebSocket();
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                await socket.ConnectAsync(endpoint, CancellationToken.None);
                await SendAsync(socket, new { type = "hello", authToken = token });
                _ = await ReceiveTypeAsync(socket, "session", TimeSpan.FromSeconds(10));
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                await SendAsync(socket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
                var gate = RuntimeGate(manager, room.GetProperty("roomCode").GetString()!);
                var holder = HoldGateAsync(gate);
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    await SendAsync(socket, new { type = "syncState" });
                    await SendAsync(socket, new { type = "ping" });
                    var pong = await ReceiveTypeAsync(socket, "pong", TimeSpan.FromSeconds(1));
                    Assert.Equal("pong", pong.GetProperty("type").GetString());
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                await SendAsync(socket, new { type = "ping" });
                _ = await ReceiveTypeAsync(socket, "pong", TimeSpan.FromSeconds(1));
            }
            finally
            {
                socket.Abort();
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally
        {
            DeleteIsolatedTestDirectory(directory, "l12-inbound-ping");
        }
    }

    [Fact]
    public async Task UnauthenticatedPingStillRequiresHello()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-unauth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, platform),
                recorder, platform, catalog);
            await server.StartAsync(0);
            using var socket = new ClientWebSocket();
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                await socket.ConnectAsync(new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri,
                    CancellationToken.None);
                await SendAsync(socket, new { type = "ping" });
                var rejected = await ReceiveTypeAsync(socket, "authenticationRequired", TimeSpan.FromSeconds(1));
                Assert.Equal("authentication-required", rejected.GetProperty("reason").GetString());
            }
            finally
            {
                socket.Abort();
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally
        {
            DeleteIsolatedTestDirectory(directory, "l12-inbound-unauth");
        }
    }

    [Fact]
    public async Task NonPingJsonShapesNeverEnterAuthenticatedControlLane()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-shape", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, platform),
                recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                var identity = Guid.NewGuid().ToString("N")[..8];
                var first = platform.Register($"im{identity}", "Password123!");
                using (var malformed = await ConnectAuthenticatedAsync(endpoint, first.Token!))
                {
                    await SendRawAsync(malformed, "{");
                    var error = await ReceiveTypeAsync(malformed, "error", TimeSpan.FromSeconds(2));
                    Assert.Equal("消息不是有效 JSON", error.GetProperty("message").GetString());
                    await SendAsync(malformed, new { type = "ping" });
                    _ = await ReceiveTypeAsync(malformed, "pong", TimeSpan.FromSeconds(2));
                    malformed.Abort();
                }

                var invalidPayloads = new[] { "[]", "42", "{\"type\":42}" };
                for (var index = 0; index < invalidPayloads.Length; index++)
                {
                    var account = platform.Register($"is{identity}{index}", "Password123!");
                    using var invalid = await ConnectAuthenticatedAsync(endpoint, account.Token!);
                    await SendRawAsync(invalid, invalidPayloads[index]);
                    await AssertRejectedOrClosedWithoutPongAsync(invalid, TimeSpan.FromSeconds(2));
                }
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-shape"); }
    }

    [Fact]
    public async Task QueuedBusinessRevalidatesCommittedSessionRevocationBeforeRoomMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-revoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"ir{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            var actor = platform.AuthenticateTokenSession(token)!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
                var roomCode = room.GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    var inbound = RuntimeInbound(server);
                    await SendAsync(socket, new { type = "syncState" });
                    await SendAsync(socket, new { type = "leaveRoom" });
                    Assert.True(SpinWait.SpinUntil(
                        () => HasOneExecutingAndOnePending(inbound), TimeSpan.FromSeconds(2)),
                        "one executing and one pending message were not retained behind the room gate");
                    var revoked = platform.RevokeOwnSession(actor, actor.SessionId);
                    Assert.Equal(1, revoked.RevokedCount);
                    Assert.False(inbound.IsAccepting);
                    Assert.Equal(1, inbound.RetainedMessages);
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }

                await AssertRejectedOrClosedWithoutPongAsync(socket, TimeSpan.FromSeconds(2));
                var replacement = platform.Login(account.Username, "Password123!").Token!;
                using var recovered = await ConnectAuthenticatedAsync(endpoint, replacement);
                await SendAsync(recovered, new { type = "syncState" });
                var recovery = await ReceiveTypeAsync(recovered, "recoveryComplete", TimeSpan.FromSeconds(10));
                Assert.Equal(roomCode, recovery.GetProperty("roomCode").GetString());
                recovered.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-revoke"); }
    }

    [Fact]
    public async Task ReplacementGenerationCancelsPendingOldSocketBusiness()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-generation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"ig{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var oldSocket = new ClientWebSocket();
                await oldSocket.ConnectAsync(endpoint, CancellationToken.None);
                await SendAsync(oldSocket, new { type = "hello", authToken = token });
                var oldSession = await ReceiveTypeAsync(oldSocket, "session", TimeSpan.FromSeconds(10));
                _ = await ReceiveTypeAsync(oldSocket, "recoveryComplete", TimeSpan.FromSeconds(10));
                var oldSessionId = oldSession.GetProperty("sessionId").GetGuid();
                var oldGeneration = oldSession.GetProperty("connectionGeneration").GetInt64();
                await SendAsync(oldSocket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(oldSocket, "roomState", TimeSpan.FromSeconds(10));
                var roomCode = room.GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                Task<ClientWebSocket>? replacementTask = null;
                var claimReserved = false;
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    await SendAsync(oldSocket, new { type = "syncState" });
                    await SendAsync(oldSocket, new { type = "leaveRoom" });
                    Assert.True(SpinWait.SpinUntil(
                        () => RuntimeInbound(server).RetainedMessages >= 2, TimeSpan.FromSeconds(2)));
                    replacementTask = ConnectAuthenticatedAsync(endpoint, token);
                    // A claim reserves its generation before waiting for the room gate, but it
                    // becomes authoritative only after recovery/its durable checkpoint succeeds.
                    // Do not require the old connection to become invalid before that boundary.
                    claimReserved = SpinWait.SpinUntil(
                        () => ReservedGeneration(manager, account.Id) > oldGeneration,
                        TimeSpan.FromSeconds(2));
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }

                Assert.NotNull(replacementTask);
                using var replacement = await replacementTask!.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(claimReserved, "replacement did not reach the original recovery claim boundary");
                Assert.False(manager.IsCurrentConnection(oldSessionId, account.Id, oldGeneration));
                await SendAsync(replacement, new { type = "syncState" });
                var recovery = await ReceiveTypeAsync(replacement, "recoveryComplete", TimeSpan.FromSeconds(10));
                Assert.Equal(roomCode, recovery.GetProperty("roomCode").GetString());
                replacement.Abort();
                oldSocket.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-generation"); }
    }

    [Fact]
    public async Task InboundMessageLimitDrainsAcceptedFifoThenRejectsOverflowWithoutBusinessAck()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-overload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"io{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
                var roomCode = room.GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    var inbound = RuntimeInbound(server);
                    for (var index = 0; index < L12InboundConnection.MaximumRetainedMessages; index++)
                        await SendAsync(socket, new { type = "syncState", requestId = $"accepted-{index}" });
                    Assert.True(SpinWait.SpinUntil(
                        () => inbound.RetainedMessages == L12InboundConnection.MaximumRetainedMessages,
                        TimeSpan.FromSeconds(2)));
                    await SendAsync(socket, new { type = "leaveRoom", requestId = "overflow-must-not-ack" });
                    Assert.True(SpinWait.SpinUntil(() => !inbound.IsAccepting,
                        TimeSpan.FromSeconds(2)), "overflow was not received before releasing the FIFO barrier");
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }

                var recoveryCount = 0;
                JsonElement rejection = default;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    while (true)
                    {
                        var message = await ReceiveOneAsync(socket, timeout.Token);
                        var type = message.GetProperty("type").GetString();
                        if (type == "recoveryComplete") { recoveryCount++; continue; }
                        if (type == "error" && message.TryGetProperty("code", out var code)
                            && code.GetString() == "inboundQueueCapacityExceeded")
                        {
                            rejection = message;
                            break;
                        }
                    }
                }
                Assert.Equal(L12InboundConnection.MaximumRetainedMessages, recoveryCount);
                Assert.Equal("overflow-must-not-ack", rejection.GetProperty("requestId").GetString());
                Assert.True(rejection.GetProperty("retryWithSameRequestId").GetBoolean());

                var replacementToken = platform.Login(account.Username, "Password123!").Token!;
                using var replacement = await ConnectAuthenticatedAsync(endpoint, replacementToken);
                await SendAsync(replacement, new { type = "syncState" });
                var recovery = await ReceiveTypeAsync(replacement, "recoveryComplete", TimeSpan.FromSeconds(10));
                Assert.Equal(roomCode, recovery.GetProperty("roomCode").GetString());
                replacement.Abort();
                socket.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-overload"); }
    }

    [Fact]
    public async Task InboundByteLimitRejectsLongRequestIdWithoutApplyingUnacceptedMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-bytes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var account = platform.Register($"ib{Guid.NewGuid():N}"[..10], "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createRoom" });
                var roomCode = (await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10)))
                    .GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    // A valid legacy ASCII frame just below the original 1 MiB wire limit is
                    // accepted. A second 128 KiB frame exceeds retained bytes, not item count.
                    await SendAsync(socket, new { type = "syncState", padding = new string('a', 1024 * 1024 - 128) });
                    var inbound = RuntimeInbound(server);
                    Assert.True(SpinWait.SpinUntil(() => inbound.RetainedMessages == 1
                        && inbound.RetainedBytes > 2_000_000, TimeSpan.FromSeconds(2)));
                    await SendAsync(socket, new { type = "leaveRoom", requestId = new string('x', 128 * 1024) });
                    Assert.True(SpinWait.SpinUntil(() => !inbound.IsAccepting, TimeSpan.FromSeconds(2)));
                    Assert.Equal(1, inbound.RetainedMessages);
                    Assert.True(inbound.MaximumObservedBytes <= L12InboundConnection.MaximumRetainedBytes);
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                var rejection = await ReceiveTypeAsync(socket, "error", TimeSpan.FromSeconds(10));
                Assert.Equal("inboundQueueCapacityExceeded", rejection.GetProperty("code").GetString());
                Assert.Equal("retained-byte-limit", rejection.GetProperty("reason").GetString());
                Assert.Equal(JsonValueKind.Null, rejection.GetProperty("requestId").ValueKind);
                Assert.False(rejection.GetProperty("retryWithSameRequestId").GetBoolean());
                using var replacement = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(replacement, new { type = "syncState" });
                Assert.Equal(roomCode, (await ReceiveTypeAsync(replacement, "recoveryComplete",
                    TimeSpan.FromSeconds(10))).GetProperty("roomCode").GetString());
                replacement.Abort();
                socket.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-bytes"); }
    }

    [Fact]
    public async Task SocketClosePreservesExecutingDurableCommandAndCancelsQueuedMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-durable-close", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var account = platform.Register($"ic{Guid.NewGuid():N}"[..10], "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createSandbox" });
                var roomCode = (await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10)))
                    .GetProperty("roomCode").GetString()!;
                var rooms = (IEnumerable)typeof(L12RoomManager)
                    .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
                var runtimeRoom = rooms.Cast<object>().Select(entry => entry.GetType().GetProperty("Value")!
                    .GetValue(entry)!).Single(room => (string)room.GetType().GetProperty("Code")!.GetValue(room)! == roomCode);
                var game = (L12GameEngine)runtimeRoom.GetType().GetProperty("Game")!.GetValue(runtimeRoom)!;
                var matchId = game.State.MatchId;
                recorder.StorageFailureInjector = phase =>
                {
                    if (phase != "before-match-command-commit") return;
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("synthetic durable command hold");
                };
                try
                {
                    await SendAsync(socket, new { type = "setResponsePreference", mode = "valid-only", requestId = "durable-first" });
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    var inbound = RuntimeInbound(server);
                    await SendAsync(socket, new { type = "setResponsePreference", mode = "invalid-five-seconds", requestId = "cancel-pending" });
                    Assert.True(SpinWait.SpinUntil(() => inbound.RetainedMessages == 2, TimeSpan.FromSeconds(2)));
                    socket.Abort();
                    Assert.True(SpinWait.SpinUntil(() => !inbound.IsAccepting && inbound.RetainedMessages == 1,
                        TimeSpan.FromSeconds(3)), "socket teardown did not cancel pending work before durable completion");
                }
                finally { release.Set(); }
                Assert.True(SpinWait.SpinUntil(() => platform.ResponsePreference(account.Id) == "valid-only",
                    TimeSpan.FromSeconds(5)), "executing durable command was lost on disconnect");
                await using var reopened = new MatchRecorder(Path.Combine(directory, "matches.db"));
                await reopened.InitializeAsync();
                reopened.AttachCatalog(catalog);
                var recorded = (await reopened.GetMatchAsync(matchId))!;
                var preferences = recorded.Commands.Where(command => command.Command.GetProperty("type")
                    .GetString() == "setResponsePreference").ToArray();
                Assert.Single(preferences);
                Assert.True(preferences[0].Accepted);
                Assert.Equal("valid-only", preferences[0].Command.GetProperty("responseMode").GetString());
                Assert.Equal("valid-only", (await reopened.LoadJournalEngineAsync(matchId))!.Engine.ResponseModeFor(0));
            }
            finally
            {
                release.Set();
                recorder.StorageFailureInjector = null;
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { release.Set(); DeleteIsolatedTestDirectory(directory, "l12-inbound-durable-close"); }
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task SendRawAsync(ClientWebSocket socket, string payload)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<ClientWebSocket> ConnectAuthenticatedAsync(Uri endpoint, string token)
    {
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, CancellationToken.None);
            await SendAsync(socket, new { type = "hello", authToken = token });
            _ = await ReceiveTypeAsync(socket, "session", TimeSpan.FromSeconds(10));
            _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
            return socket;
        }
        catch
        {
            socket.Abort();
            socket.Dispose();
            throw;
        }
    }

    private static async Task AssertRejectedOrClosedWithoutPongAsync(ClientWebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            var buffer = new byte[1024];
            var result = await socket.ReceiveAsync(buffer, cancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("type", out var type));
            Assert.Equal(JsonValueKind.String, type.ValueKind);
            Assert.NotEqual("pong", type.GetString());
        }
        catch (WebSocketException)
        {
            // The current Dispatch error boundary may abort malformed object shapes. The contract
            // here only freezes that these shapes cannot be mistaken for control-lane ping.
        }
    }

    private static async Task<JsonElement> ReceiveTypeAsync(ClientWebSocket socket, string expected,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[16 * 1024];
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("socket closed before expected message");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var document = JsonDocument.Parse(stream.ToArray());
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == expected) return root.Clone();
        }
        throw new InvalidDataException($"missing WebSocket message {expected}");
    }

    private static async Task<JsonElement> ReceiveOneAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("socket closed before expected message");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static SemaphoreSlim RuntimeGate(L12RoomManager manager, string roomCode)
    {
        var rooms = (IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in rooms)
        {
            var room = entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
            if (!string.Equals((string)room.GetType().GetProperty("Code")!.GetValue(room)!, roomCode,
                    StringComparison.OrdinalIgnoreCase)) continue;
            return (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        }
        throw new InvalidOperationException($"missing runtime room {roomCode}");
    }

    private static bool HasOneExecutingAndOnePending(L12InboundConnection inbound)
    {
        var gate = typeof(L12InboundConnection).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inbound)!;
        lock (gate)
        {
            var pending = (ICollection)typeof(L12InboundConnection)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inbound)!;
            return inbound.IsAccepting && inbound.RetainedMessages == 2 && pending.Count == 1;
        }
    }

    private static L12InboundConnection RuntimeInbound(L12WebSocketServer server)
    {
        var connections = (IEnumerable)typeof(L12WebSocketServer)
            .GetField("_inboundConnections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        foreach (var entry in connections)
            return (L12InboundConnection)entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
        throw new InvalidOperationException("missing runtime inbound connection");
    }

    private static long ReservedGeneration(L12RoomManager manager, string accountId)
        => ((System.Collections.Concurrent.ConcurrentDictionary<string, long>)typeof(L12RoomManager)
            .GetField("_accountConnectionGenerations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!).GetValueOrDefault(accountId);

    private static GateHolder HoldGateAsync(SemaphoreSlim gate)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Task.Run(async () =>
        {
            await gate.WaitAsync();
            entered.TrySetResult();
            try { await release.Task; }
            finally { gate.Release(); }
        });
        return new GateHolder(entered.Task, release, completion);
    }

    private static void DeleteIsolatedTestDirectory(string directory, string expectedParentName)
    {
        var target = new DirectoryInfo(Path.GetFullPath(directory));
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), expectedParentName));
        if (target.Parent is null
            || !string.Equals(target.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
                expectedParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || target.Name.Length != 32
            || !Guid.TryParseExact(target.Name, "N", out _))
            throw new InvalidOperationException($"refusing to delete unexpected test directory: {target.FullName}");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (target.Exists) target.Delete(recursive: true);
    }

    private sealed record GateHolder(Task Entered, TaskCompletionSource Release, Task Completion);
}
