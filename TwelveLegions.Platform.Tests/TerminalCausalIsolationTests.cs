using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TerminalCausalIsolationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ThreeSeconds = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SetupDeadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("normal")]
    [InlineData("held-target-sender")]
    [InlineData("held-platform-commit")]
    public async Task LocalhostTerminalSeparatesTargetSenderAndPlatformBackgroundFromHealthyPeers(string arm)
    {
        using var identity = new RankedIdentityFixture();
        var actorRegistration = identity.Store.Register($"ca{Guid.NewGuid():N}"[..10], "Password123!");
        var opponentRegistration = identity.Store.Register($"cb{Guid.NewGuid():N}"[..10], "Password123!");
        var healthRegistration = identity.Store.Register($"ch{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(actorRegistration.Success);
        Assert.True(opponentRegistration.Success);
        Assert.True(healthRegistration.Success);

        var evidence = new ConcurrentQueue<string>();
        var nonce = Guid.NewGuid().ToString("N");
        using var diagnostics = Assert.IsType<L12SyntheticTransportDiagnostics>(
            L12SyntheticTransportDiagnostics.Create(true,
                $"{L12SyntheticTransportDiagnostics.Mode}:1:{nonce}", evidence.Enqueue,
                ThreeSeconds, TimeSpan.FromMilliseconds(10)));
        await using var recorder = new MatchRecorder(Path.Combine(
            Path.GetDirectoryName(identity.Path)!, $"causal-{arm}.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog, null, null, null, null, null, null, null, diagnostics);

        SocketPeer? actor = null;
        SocketPeer? opponent = null;
        SocketPeer? healthy = null;
        HeldTargetSender? heldSender = null;
        ManualResetEventSlim? platformEntered = null;
        ManualResetEventSlim? platformRelease = null;
        Task? platformWriter = null;
        var serverStarted = false;
        try
        {
            await server.StartAsync(0).WaitAsync(SetupDeadline);
            serverStarted = true;
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            actor = await SocketPeer.ConnectAsync(endpoint, actorRegistration.Token!);
            opponent = await SocketPeer.ConnectAsync(endpoint, opponentRegistration.Token!);
            healthy = await SocketPeer.ConnectAsync(endpoint, healthRegistration.Token!);
            await StartFriendlyGameAsync(actor, opponent);
            await AssertHealthyAsync(healthy);

            if (arm == "held-target-sender")
            {
                // Receiving this pong proves all pre-swap frames on the target's original sender
                // have completed before the test-only dictionary replacement.
                await AssertHealthyAsync(actor);
                heldSender = await HeldTargetSender.InstallAsync(server, actor.SessionId);
                await heldSender.EnterAsync();
            }
            else if (arm == "held-platform-commit")
            {
                var entered = platformEntered = new ManualResetEventSlim();
                var release = platformRelease = new ManualResetEventSlim();
                identity.Store.StorageFailureInjector = stage =>
                {
                    if (stage != "before-commit") return;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(8)))
                        throw new TimeoutException("bounded causal platform barrier");
                };
                platformWriter = Task.Factory.StartNew(
                    () => identity.Store.Register($"cw{Guid.NewGuid():N}"[..10], "Password123!"),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.True(entered.Wait(TimeSpan.FromSeconds(2)),
                    "platform writer did not enter the bounded before-commit barrier");
            }

            var requestId = $"l12diag:{nonce}:0:{Guid.NewGuid():N}";
            var actorTerminalTask = actor.ReceiveAsync(
                frame => IsGameOver(frame) && ReadString(frame, "requestId") == requestId,
                ThreeSeconds);
            var opponentTerminalTask = opponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            var sendStartedAt = Stopwatch.GetTimestamp();
            await actor.SendAsync(new
            {
                type = "gameAction",
                requestId,
                command = new { type = "surrender" },
            });
            var sendCompletedAt = Stopwatch.GetTimestamp();
            await healthy.SendAsync(new { type = "ping" });
            var healthyPongTask = healthy.ReceiveTypeAsync("pong", ThreeSeconds);

            var opponentTerminal = await opponentTerminalTask;
            var opponentRawAt = opponentTerminal.ReceivedAt;
            var healthyPong = await healthyPongTask;
            Assert.True(ElapsedMilliseconds(sendStartedAt, opponentRawAt) < ThreeSeconds.TotalMilliseconds,
                "normal room peer missed the original three-second terminal boundary");
            Assert.True(ElapsedMilliseconds(sendStartedAt, healthyPong.ReceivedAt) < ThreeSeconds.TotalMilliseconds,
                "independent healthy peer missed the original three-second pong boundary");
            Assert.Null(ReadString(opponentTerminal.Payload, "requestId"));

            if (heldSender is not null)
            {
                // The opponent has already received the terminal broadcast, so the requester's
                // earlier per-room fanout item is known to be queued behind the held frame. Keep
                // that queue residence above one second instead of measuring from gate entry.
                await Task.Delay(TimeSpan.FromMilliseconds(1100));
                Assert.Equal(0, heldSender.GateOriginalStartedAt);
                Assert.False(actorTerminalTask.IsCompleted,
                    "requesting peer completed while its sender was still deliberately held");
                heldSender.Release();
            }

            var actorTerminal = await actorTerminalTask;
            Assert.True(ElapsedMilliseconds(sendStartedAt, actorTerminal.ReceivedAt) < ThreeSeconds.TotalMilliseconds,
                "requesting peer missed the original three-second terminal boundary");
            await AssertHealthyAsync(actor);
            Assert.Equal(1, actor.Received.Count(frame =>
                IsGameOver(frame.Payload) && ReadString(frame.Payload, "requestId") == requestId));
            if (platformWriter is not null)
                Assert.False(platformWriter.IsCompleted,
                    "platform control writer left its barrier before both terminal frames were observed");

            var diagnostic = await ReadDiagnosticRowAsync(evidence, SetupDeadline);
            Assert.Equal((int)L12SyntheticTransportStatus.Sent,
                diagnostic.GetProperty("status").GetInt32());
            var outboundQueueUs = diagnostic.GetProperty("out_queue_us").GetInt64();
            if (heldSender is not null)
            {
                Assert.True(ElapsedMicroseconds(heldSender.EnteredAt, heldSender.ReleasedAt) >= 1_000_000,
                    "target sender gate was not held for the required one-second control window");
                Assert.True(outboundQueueUs >= 1_000_000,
                    $"held sender was not represented by outbound queue time: {outboundQueueUs}us");
                Assert.True(heldSender.GateOriginalCompletedAt >= heldSender.GateOriginalStartedAt);
                Assert.True(heldSender.TerminalOriginalCompletedAt >= heldSender.TerminalOriginalStartedAt);
            }

            output.WriteLine(
                "arm={0} send_call_us={1} opponent_raw_us={2} requester_raw_us={3} healthy_raw_us={4} out_queue_us={5} send_us={6} server_total_us={7} gate_hold_us={8} gate_delegate_hold_us={9} gate_original_us={10} terminal_original_us={11}",
                arm,
                ElapsedMicroseconds(sendStartedAt, sendCompletedAt),
                ElapsedMicroseconds(sendStartedAt, opponentRawAt),
                ElapsedMicroseconds(sendStartedAt, actorTerminal.ReceivedAt),
                ElapsedMicroseconds(sendStartedAt, healthyPong.ReceivedAt),
                outboundQueueUs,
                diagnostic.GetProperty("send_us").GetInt64(),
                diagnostic.GetProperty("server_total_us").GetInt64(),
                diagnostic.GetProperty("gate_hold_us").GetInt64(),
                heldSender is null ? 0 : ElapsedMicroseconds(heldSender.EnteredAt, heldSender.ReleasedAt),
                heldSender is null ? 0 : ElapsedMicroseconds(
                    heldSender.GateOriginalStartedAt, heldSender.GateOriginalCompletedAt),
                heldSender is null ? 0 : ElapsedMicroseconds(
                    heldSender.TerminalOriginalStartedAt, heldSender.TerminalOriginalCompletedAt));
        }
        finally
        {
            heldSender?.Release();
            platformRelease?.Set();
            if (platformWriter is not null)
                await platformWriter.WaitAsync(SetupDeadline);
            identity.Store.StorageFailureInjector = null;
            platformEntered?.Dispose();
            platformRelease?.Dispose();
            if (actor is not null) await actor.DisposeAsync();
            if (opponent is not null) await opponent.DisposeAsync();
            if (healthy is not null) await healthy.DisposeAsync();
            if (serverStarted) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

    private static async Task StartFriendlyGameAsync(SocketPeer actor, SocketPeer opponent)
    {
        await actor.SendAsync(new
        {
            type = "createRoom",
            options = new { matchModeId = "friendly" },
        });
        var created = await actor.ReceiveTypeAsync("roomState", SetupDeadline);
        var roomCode = created.Payload.GetProperty("roomCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(roomCode));

        var actorJoined = actor.ReceiveTypeAsync("roomState", SetupDeadline);
        var opponentJoined = opponent.ReceiveTypeAsync("roomState", SetupDeadline);
        await opponent.SendAsync(new { type = "joinRoom", roomCode });
        await Task.WhenAll(actorJoined, opponentJoined);

        var actorReady = actor.ReceiveTypeAsync("roomState", SetupDeadline);
        var opponentSeesReady = opponent.ReceiveTypeAsync("roomState", SetupDeadline);
        await actor.SendAsync(new { type = "ready", ready = true });
        await Task.WhenAll(actorReady, opponentSeesReady);

        var actorStarted = actor.ReceiveTypeAsync("gameState", SetupDeadline);
        var opponentStarted = opponent.ReceiveTypeAsync("gameState", SetupDeadline);
        await opponent.SendAsync(new { type = "ready", ready = true });
        var started = await Task.WhenAll(actorStarted, opponentStarted);
        Assert.All(started, frame => Assert.False(IsGameOver(frame.Payload)));
    }

    private static async Task AssertHealthyAsync(SocketPeer peer)
    {
        await peer.SendAsync(new { type = "ping" });
        _ = await peer.ReceiveTypeAsync("pong", ThreeSeconds);
    }

    private static bool IsGameOver(JsonElement payload) =>
        ReadString(payload, "type") == "gameState"
        && payload.TryGetProperty("state", out var state)
        && ReadString(state, "phase") == "GameOver";

    private static string? ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static async Task<JsonElement> ReadDiagnosticRowAsync(
        ConcurrentQueue<string> evidence, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.IsCancellationRequested)
        {
            while (evidence.TryDequeue(out var line))
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("kind", out var kind)
                    && kind.GetInt32() == 1)
                    return document.RootElement.Clone();
            }
            await Task.Delay(10, deadline.Token).ConfigureAwait(false);
        }
        throw new Xunit.Sdk.XunitException("numeric terminal diagnostic row was not emitted");
    }

    private static long ElapsedMicroseconds(long startedAt, long completedAt) =>
        startedAt <= 0 || completedAt < startedAt
            ? 0
            : (long)(Stopwatch.GetElapsedTime(startedAt, completedAt).TotalMilliseconds * 1000);

    private static double ElapsedMilliseconds(long startedAt, long completedAt) =>
        Stopwatch.GetElapsedTime(startedAt, completedAt).TotalMilliseconds;

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    private sealed record ReceivedFrame(JsonElement Payload, long ReceivedAt);

    private sealed class SocketPeer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        internal Guid SessionId { get; private set; }
        internal List<ReceivedFrame> Received { get; } = [];

        internal static async Task<SocketPeer> ConnectAsync(Uri endpoint, string token)
        {
            var peer = new SocketPeer();
            try
            {
                using var deadline = new CancellationTokenSource(SetupDeadline);
                await peer._socket.ConnectAsync(endpoint, deadline.Token);
                await peer.SendAsync(new
                {
                    type = "hello",
                    authToken = token,
                    capabilities = new
                    {
                        requestIds = true,
                        deltaGameState = false,
                        eventWindowDeltaV1 = false,
                        presenceDeltaV1 = false,
                    },
                });
                var session = await peer.ReceiveTypeAsync("session", SetupDeadline);
                peer.SessionId = session.Payload.GetProperty("sessionId").GetGuid();
                _ = await peer.ReceiveTypeAsync("recoveryComplete", SetupDeadline);
                return peer;
            }
            catch
            {
                peer._socket.Dispose();
                throw;
            }
        }

        internal async Task SendAsync(object payload)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Wire);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        internal Task<ReceivedFrame> ReceiveTypeAsync(string type, TimeSpan timeout) =>
            ReceiveAsync(payload => ReadString(payload, "type") == type, timeout);

        internal async Task<ReceivedFrame> ReceiveAsync(
            Func<JsonElement, bool> predicate, TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            while (true)
            {
                var frame = await ReceiveOneAsync(deadline.Token);
                Received.Add(frame);
                if (predicate(frame.Payload)) return frame;
            }
        }

        private async Task<ReceivedFrame> ReceiveOneAsync(CancellationToken cancellationToken)
        {
            using var message = new MemoryStream();
            var buffer = new byte[32 * 1024];
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("socket closed before the expected frame");
                await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            } while (!result.EndOfMessage);
            var receivedAt = Stopwatch.GetTimestamp();
            using var document = JsonDocument.Parse(message.ToArray());
            return new(document.RootElement.Clone(), receivedAt);
        }

        public ValueTask DisposeAsync()
        {
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HeldTargetSender
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gatePayload = new L12QueuedPayload(
            Encoding.UTF8.GetBytes("{\"type\":\"error\",\"code\":\"causalSenderGate\"}"),
            false, false);
        private readonly L12OutboundConnection _replacement;

        private HeldTargetSender(L12OutboundConnection replacement)
        {
            _replacement = replacement;
        }

        internal long EnteredAt { get; private set; }
        internal long ReleasedAt { get; private set; }
        internal long GateOriginalStartedAt { get; private set; }
        internal long GateOriginalCompletedAt { get; private set; }
        internal long TerminalOriginalStartedAt { get; private set; }
        internal long TerminalOriginalCompletedAt { get; private set; }

        internal static async Task<HeldTargetSender> InstallAsync(
            L12WebSocketServer server, Guid sessionId)
        {
            var outbounds = Field<ConcurrentDictionary<Guid, L12OutboundConnection>>(
                server, "_outboundConnections");
            var originalConnection = outbounds[sessionId];
            var originalSender = Field<Func<object, L12SyntheticTransportSample?, CancellationToken, Task>?>(
                originalConnection, "_timedSender");
            Assert.NotNull(originalSender);
            var onFault = Field<Action<Exception>?>(originalConnection, "_onFault");
            var sendTimeout = Field<TimeSpan?>(originalConnection, "_sendTimeout");
            HeldTargetSender? holder = null;
            var replacement = new L12OutboundConnection(async (payload, diagnostic, cancellationToken) =>
            {
                var active = holder ?? throw new InvalidOperationException("held sender was not initialized");
                var isGate = ReferenceEquals(payload, active._gatePayload);
                if (isGate)
                {
                    active.EnteredAt = Stopwatch.GetTimestamp();
                    active._entered.TrySetResult();
                    await active._release.Task.WaitAsync(cancellationToken);
                    active.ReleasedAt = Stopwatch.GetTimestamp();
                    active.GateOriginalStartedAt = Stopwatch.GetTimestamp();
                }
                else if (diagnostic is not null)
                {
                    active.TerminalOriginalStartedAt = Stopwatch.GetTimestamp();
                }

                await originalSender!(payload, diagnostic, cancellationToken);
                var completedAt = Stopwatch.GetTimestamp();
                if (isGate) active.GateOriginalCompletedAt = completedAt;
                else if (diagnostic is not null) active.TerminalOriginalCompletedAt = completedAt;
            }, onFault, sendTimeout);
            holder = new HeldTargetSender(replacement);
            if (!outbounds.TryUpdate(sessionId, replacement, originalConnection))
            {
                await replacement.DisposeAsync();
                throw new Xunit.Sdk.XunitException(
                    "target outbound connection changed before the idle test-only swap");
            }
            await originalConnection.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));
            await originalConnection.DisposeAsync();
            return holder;
        }

        internal async Task EnterAsync()
        {
            Assert.True(_replacement.TryEnqueue(_gatePayload));
            await _entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        internal void Release() => _release.TrySetResult();
    }
}
