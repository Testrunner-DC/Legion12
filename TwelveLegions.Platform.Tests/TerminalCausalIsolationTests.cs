using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
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
    private const int LargeStateSelectionSeed = 20261016;
    private const int MaximumAcceptedLargeStateCommands = 96;
    private const int MinimumTerminalBytes = 169 * 1024;
    private const int MaximumTerminalBytes = 300 * 1024;
    private const int PreferredPreTerminalBytes = 180 * 1024;

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

    [Fact]
    public async Task LocalhostBoundsReceiptUsesAuthenticatedPingWithoutChangingPongOrPeerDelivery()
    {
        using var identity = new RankedIdentityFixture();
        var actorRegistration = identity.Store.Register($"va{Guid.NewGuid():N}"[..10], "Password123!");
        var opponentRegistration = identity.Store.Register($"vb{Guid.NewGuid():N}"[..10], "Password123!");
        var healthRegistration = identity.Store.Register($"vh{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(actorRegistration.Success);
        Assert.True(opponentRegistration.Success);
        Assert.True(healthRegistration.Success);

        var evidence = new ConcurrentQueue<string>();
        var nonce = Guid.NewGuid().ToString("N");
        using var diagnostics = Assert.IsType<L12SyntheticTransportDiagnostics>(
            L12SyntheticTransportDiagnostics.Create(true,
                $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}", evidence.Enqueue,
                ThreeSeconds, TimeSpan.FromMilliseconds(10)));
        await using var recorder = new MatchRecorder(Path.Combine(
            Path.GetDirectoryName(identity.Path)!, "causal-bounds.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog, null, null, null, null, null, null, null, diagnostics);

        SocketPeer? actor = null;
        SocketPeer? opponent = null;
        SocketPeer? healthy = null;
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

            var requestId = $"l12diag:{nonce}:0:{Guid.NewGuid():N}";
            var actorTerminalTask = actor.ReceiveAsync(
                frame => IsGameOver(frame) && ReadString(frame, "requestId") == requestId,
                ThreeSeconds);
            var opponentTerminalTask = opponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            await actor.SendAsync(new
            {
                type = "gameAction",
                requestId,
                command = new { type = "surrender" },
            });
            _ = await actorTerminalTask;
            _ = await opponentTerminalTask;

            await actor.SendAsync(new
            {
                type = "ping",
                diagnosticNonce = nonce,
                diagnosticSlot = 0,
            });
            var pong = await actor.ReceiveTypeAsync("pong", ThreeSeconds);
            Assert.Equal(2, pong.Payload.EnumerateObject().Count());
            Assert.True(pong.Payload.TryGetProperty("utc", out _));
            Assert.False(pong.Payload.TryGetProperty("diagnosticNonce", out _));
            Assert.False(pong.Payload.TryGetProperty("diagnosticSlot", out _));
            await AssertHealthyAsync(healthy);

            var diagnostic = await ReadDiagnosticRowAsync(evidence, SetupDeadline);
            Assert.Equal(2, diagnostic.GetProperty("schema").GetInt32());
            Assert.Equal((int)L12SyntheticTransportStatus.Sent,
                diagnostic.GetProperty("status").GetInt32());
            Assert.Equal(1, diagnostic.GetProperty("receive_valid").GetInt32());
            Assert.Equal(1, diagnostic.GetProperty("receipt_valid").GetInt32());
            Assert.InRange(diagnostic.GetProperty("receipt_after_send_valid").GetInt32(), 0, 1);
            Assert.True(diagnostic.GetProperty("receipt_rel_us").GetInt64() >= 0);
        }
        finally
        {
            if (actor is not null) await actor.DisposeAsync();
            if (opponent is not null) await opponent.DisposeAsync();
            if (healthy is not null) await healthy.DisposeAsync();
            if (serverStarted) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

    [Fact]
    public async Task LocalhostLargeLegalTerminalSeparatesRecorderProjectionTransportAndRawArrival()
    {
        using var identity = new RankedIdentityFixture();
        var actorRegistration = identity.Store.Register($"la{Guid.NewGuid():N}"[..10], "Password123!");
        var opponentRegistration = identity.Store.Register($"lb{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(actorRegistration.Success);
        Assert.True(opponentRegistration.Success);

        var evidence = new ConcurrentQueue<string>();
        var nonce = Guid.NewGuid().ToString("N");
        using var diagnostics = Assert.IsType<L12SyntheticTransportDiagnostics>(
            L12SyntheticTransportDiagnostics.Create(true,
                $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}", evidence.Enqueue,
                ThreeSeconds, TimeSpan.FromMilliseconds(10)));
        var databasePath = Path.Combine(Path.GetDirectoryName(identity.Path)!, "causal-large-state.db");
        await using var recorder = new MatchRecorder(databasePath);
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog, null, null, null, null, null, null, null, diagnostics);

        SocketPeer? actor = null;
        SocketPeer? opponent = null;
        var beforeCommit = new ConcurrentQueue<BeforeCommitObservation>();
        var beforeCommitOrdinal = 0;
        var serverStarted = false;
        try
        {
            await server.StartAsync(0).WaitAsync(SetupDeadline);
            serverStarted = true;
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            actor = await SocketPeer.ConnectAsync(endpoint, actorRegistration.Token!);
            opponent = await SocketPeer.ConnectAsync(endpoint, opponentRegistration.Token!);
            var started = await StartLargeFriendlyGameAsync(actor, opponent);
            var peersByPlayer = new SocketPeer[2];
            var latestByPlayer = new ReceivedFrame[2];
            BindPlayerFrame(actor, started.Actor, peersByPlayer, latestByPlayer);
            BindPlayerFrame(opponent, started.Opponent, peersByPlayer, latestByPlayer);
            Assert.All(peersByPlayer, peer => Assert.NotNull(peer));

            var selector = new Random(LargeStateSelectionSeed);
            var playedTurns = new HashSet<string>(StringComparer.Ordinal);
            var commandTypes = new Dictionary<string, int>(StringComparer.Ordinal);
            var acceptedCommands = 0;
            while (acceptedCommands < MaximumAcceptedLargeStateCommands
                   && latestByPlayer.Min(frame => frame.RawBytes) < PreferredPreTerminalBytes)
            {
                Assert.DoesNotContain(latestByPlayer,
                    frame => IsGameOver(frame.Payload));
                var selected = SelectLargeStateCommand(latestByPlayer, playedTurns, selector);
                var sender = peersByPlayer[selected.PlayerIndex];
                var other = peersByPlayer[1 - selected.PlayerIndex];
                var revisionBefore = ReadInt64(
                    latestByPlayer[selected.PlayerIndex].Payload.GetProperty("state"), "revision");
                var requestId = $"large-state-{acceptedCommands:D3}-{Guid.NewGuid():N}";
                var confirmationTask = sender.ReceiveAsync(
                    payload => ReadString(payload, "requestId") == requestId, SetupDeadline);
                var peerTask = other.ReceiveAsync(
                    payload => IsAdvancedGameState(payload, revisionBefore), SetupDeadline);
                await sender.SendAsync(new
                {
                    type = "gameAction",
                    requestId,
                    command = selected.Command,
                });
                var confirmation = await confirmationTask;
                Assert.Equal("gameState", ReadString(confirmation.Payload, "type"));
                Assert.True(IsAdvancedGameState(confirmation.Payload, revisionBefore),
                    $"accepted {selected.Type} did not advance the authoritative revision");
                var peerFrame = await peerTask;
                BindPlayerFrame(sender, confirmation, peersByPlayer, latestByPlayer);
                BindPlayerFrame(other, peerFrame, peersByPlayer, latestByPlayer);
                Assert.Equal(
                    ReadInt64(latestByPlayer[0].Payload.GetProperty("state"), "revision"),
                    ReadInt64(latestByPlayer[1].Payload.GetProperty("state"), "revision"));
                if (selected.TurnKey is not null) playedTurns.Add(selected.TurnKey);
                commandTypes[selected.Type] = commandTypes.GetValueOrDefault(selected.Type) + 1;
                acceptedCommands++;
            }

            var preTerminalBytes = latestByPlayer.Select(frame => frame.RawBytes).ToArray();
            var metricsBefore = PerformanceSnapshot();
            var observationStartedAt = Stopwatch.GetTimestamp();
            recorder.StorageFailureInjector = stage =>
            {
                var kind = stage switch
                {
                    "before-match-command-commit" => 1,
                    "before-match-complete-commit" => 2,
                    _ => 0,
                };
                if (kind != 0)
                    beforeCommit.Enqueue(new BeforeCommitObservation(
                        Interlocked.Increment(ref beforeCommitOrdinal), kind,
                        ElapsedMicroseconds(observationStartedAt, Stopwatch.GetTimestamp())));
            };

            var terminalRequestId = $"l12diag:{nonce}:0:{Guid.NewGuid():N}";
            var actorTerminalTask = actor.ReceiveAsync(
                payload => IsGameOver(payload) && ReadString(payload, "requestId") == terminalRequestId,
                ThreeSeconds);
            var opponentTerminalTask = opponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            var terminalSentAt = Stopwatch.GetTimestamp();
            await actor.SendAsync(new
            {
                type = "gameAction",
                requestId = terminalRequestId,
                command = new { type = "surrender" },
            });
            var terminalFrames = await Task.WhenAll(actorTerminalTask, opponentTerminalTask);
            var terminalReceivedAt = terminalFrames.Max(frame => frame.ReceivedAt);
            Assert.True(Stopwatch.GetElapsedTime(terminalSentAt, terminalReceivedAt) < ThreeSeconds,
                "large-state localhost terminal delivery exceeded the original three-second boundary");

            await actor.SendAsync(new
            {
                type = "ping",
                diagnosticNonce = nonce,
                diagnosticSlot = 0,
            });
            _ = await actor.ReceiveTypeAsync("pong", ThreeSeconds);
            var diagnostic = await ReadDiagnosticRowAsync(evidence, SetupDeadline);
            var metricsAfter = PerformanceSnapshot();
            var durable = await AssertLargeDurableTerminalAsync(
                recorder, databasePath, started.MatchId, terminalRequestId, acceptedCommands + 1);

            var orderedCallbacks = beforeCommit.OrderBy(item => item.Ordinal).ToArray();
            Assert.Equal(1, orderedCallbacks.Count(item => item.Kind == 1));
            Assert.Equal(1, orderedCallbacks.Count(item => item.Kind == 2));
            Assert.All(orderedCallbacks, item =>
            {
                Assert.True(item.RelativeMicroseconds >= 0);
                Assert.True(item.RelativeMicroseconds <=
                    ElapsedMicroseconds(observationStartedAt, terminalReceivedAt));
            });
            Assert.Equal(2, diagnostic.GetProperty("schema").GetInt32());
            Assert.Equal((int)L12SyntheticTransportStatus.Sent,
                diagnostic.GetProperty("status").GetInt32());
            Assert.Equal(1, diagnostic.GetProperty("receive_valid").GetInt32());
            Assert.Equal(1, diagnostic.GetProperty("receipt_valid").GetInt32());
            Assert.Equal(1, diagnostic.GetProperty("receipt_after_send_valid").GetInt32());
            Assert.Equal(0, diagnostic.GetProperty("wire_delta").GetInt32());
            Assert.Equal(terminalFrames[0].RawBytes,
                diagnostic.GetProperty("wire_bytes").GetInt32());

            var terminalBytes = terminalFrames.Select(frame => frame.RawBytes).ToArray();
            var globalMetrics = MetricDelta(metricsBefore, metricsAfter,
            [
                "duration.action.engine",
                "duration.snapshot.build",
                "duration.persistence.serialize-and-hash",
                "duration.persistence.checkpoint-serialize",
                "duration.persistence.transaction",
                "duration.persistence.total",
                "duration.websocket.serialize",
                "duration.websocket.send",
                "bytes.websocket.full-payload",
            ]);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                schema = 1,
                scenario = "single-friendly-room-large-legal-command-history-localhost-terminal",
                commandSelectionSeed = LargeStateSelectionSeed,
                productRoomSeedBoundary = "real websocket room seed remains product-generated; no state or RNG injection",
                maximumAcceptedCommands = MaximumAcceptedLargeStateCommands,
                acceptedCommands,
                commandTypes,
                preferredPreTerminalBytes = PreferredPreTerminalBytes,
                preTerminalBytes,
                terminalRangeBytes = new { minimum = MinimumTerminalBytes, maximum = MaximumTerminalBytes },
                terminalBytes,
                terminalSendToRawMicroseconds = terminalFrames.Select(frame =>
                    ElapsedMicroseconds(terminalSentAt, frame.ReceivedAt)).ToArray(),
                originalThresholdMicroseconds = (long)(ThreeSeconds.TotalMilliseconds * 1000),
                beforeCommitStageContract = new { append = 1, complete = 2 },
                observedBeforeCommitSequence = orderedCallbacks.Select(item => item.Kind).ToArray(),
                observedBeforeCommitRelativeMicroseconds = orderedCallbacks
                    .Select(item => item.RelativeMicroseconds).ToArray(),
                beforeCommitBoundary = "callbacks are pre-commit observations, not commit/fsync completion timestamps",
                durability = durable,
                requestDiagnostic = NumericDiagnostic(diagnostic),
                globalMetricDelta = globalMetrics,
                metricBoundary = "global stage rows are aggregate window deltas; request diagnostic is requester-only; SendAsync completion and receipt round-trip are not one-way public network time",
            }));

            Assert.All(terminalBytes, bytes => Assert.InRange(
                bytes, MinimumTerminalBytes, MaximumTerminalBytes));
            Assert.InRange(diagnostic.GetProperty("wire_bytes").GetInt32(),
                MinimumTerminalBytes, MaximumTerminalBytes);
        }
        finally
        {
            recorder.StorageFailureInjector = null;
            if (actor is not null) await actor.DisposeAsync();
            if (opponent is not null) await opponent.DisposeAsync();
            if (serverStarted) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

    private static async Task<StartedLargeRoom> StartLargeFriendlyGameAsync(
        SocketPeer actor, SocketPeer opponent)
    {
        await actor.SendAsync(new
        {
            type = "createRoom",
            options = new { matchModeId = "friendly" },
        });
        var created = await actor.ReceiveTypeAsync("roomState", SetupDeadline);
        var roomCode = ReadString(created.Payload, "roomCode");
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
        var actorMatchId = ReadString(started[0].Payload.GetProperty("state"), "matchId");
        var opponentMatchId = ReadString(started[1].Payload.GetProperty("state"), "matchId");
        Assert.False(string.IsNullOrWhiteSpace(actorMatchId));
        Assert.Equal(actorMatchId, opponentMatchId);
        return new(actorMatchId!, started[0], started[1]);
    }

    private static void BindPlayerFrame(SocketPeer peer, ReceivedFrame frame,
        SocketPeer[] peersByPlayer, ReceivedFrame[] latestByPlayer)
    {
        var state = frame.Payload.GetProperty("state");
        var playerIndex = ReadInt32(state, "you");
        Assert.InRange(playerIndex, 0, 1);
        if (peersByPlayer[playerIndex] is not null)
            Assert.Same(peer, peersByPlayer[playerIndex]);
        peersByPlayer[playerIndex] = peer;
        latestByPlayer[playerIndex] = frame;
    }

    private static SelectedLargeCommand SelectLargeStateCommand(
        IReadOnlyList<ReceivedFrame> latestByPlayer, HashSet<string> playedTurns, Random selector)
    {
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var state = latestByPlayer[playerIndex].Payload.GetProperty("state");
            var prompts = state.GetProperty("prompts");
            if (prompts.GetArrayLength() == 0) continue;
            var prompt = prompts.EnumerateArray().First();
            Assert.Equal(playerIndex, ReadInt32(prompt, "playerIndex"));
            if (prompt.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("placementMode", out var placementMode)
                && placementMode.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                throw new Xunit.Sdk.XunitException(
                    "large-state legal selector reached an unsupported authoritative placement prompt");
            var validChoices = prompt.GetProperty("validChoices").EnumerateArray()
                .Select(choice => choice.GetString())
                .Where(choice => !string.IsNullOrWhiteSpace(choice))
                .Cast<string>().ToArray();
            Assert.Equal(validChoices.Length, validChoices.Distinct(StringComparer.Ordinal).Count());
            var minimum = ReadInt32(prompt, "minChoose");
            var maximum = ReadInt32(prompt, "maxChoose");
            Assert.InRange(minimum, 0, validChoices.Length);
            Assert.InRange(maximum, minimum, validChoices.Length);
            var command = new Dictionary<string, object?>
            {
                ["type"] = "resolvePrompt",
                ["promptId"] = ReadString(prompt, "promptId"),
                ["cardInstanceIds"] = validChoices.Take(minimum).ToArray(),
            };
            CopyPromptBinding(prompt, command, "activationId");
            CopyPromptBinding(prompt, command, "sourceInstanceId");
            CopyPromptBinding(prompt, command, "sourceCardId");
            CopyPromptBinding(prompt, command, "step");
            CopyPromptBinding(prompt, command, "createdRevision");
            CopyPromptBinding(prompt, command, "controller");
            return new(playerIndex, "resolvePrompt", command, null);
        }

        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var state = latestByPlayer[playerIndex].Payload.GetProperty("state");
            if (!string.Equals(ReadString(state, "phase"), "Mulligan", StringComparison.Ordinal)) continue;
            var own = PlayerView(state, playerIndex);
            if (!own.GetProperty("mulliganDone").GetBoolean())
                return new(playerIndex, "mulligan", new Dictionary<string, object?>
                {
                    ["type"] = "mulligan",
                    ["cardInstanceIds"] = Array.Empty<string>(),
                }, null);
        }

        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var state = latestByPlayer[playerIndex].Payload.GetProperty("state");
            if (!string.Equals(ReadString(state, "phase"), "Defense", StringComparison.Ordinal)
                || state.GetProperty("effectStack").GetArrayLength() != 0
                || state.GetProperty("pendingDefense").ValueKind != JsonValueKind.Object)
                continue;
            var pending = state.GetProperty("pendingDefense");
            if (ReadString(pending, "stage") == "DefenseChoice"
                && ReadInt32(pending, "attackerPlayer") != playerIndex)
                return new(playerIndex, "resolveDefense", new Dictionary<string, object?>
                {
                    ["type"] = "resolveDefense",
                    ["cardInstanceIds"] = Array.Empty<string>(),
                }, null);
        }

        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var state = latestByPlayer[playerIndex].Payload.GetProperty("state");
            if (!string.Equals(ReadString(state, "phase"), "Main", StringComparison.Ordinal)
                || ReadInt32(state, "activePlayer") != playerIndex
                || state.GetProperty("prompts").GetArrayLength() != 0
                || state.GetProperty("effectStack").GetArrayLength() != 0
                || state.GetProperty("pendingDefense").ValueKind != JsonValueKind.Null
                || state.GetProperty("waitingPrompt").ValueKind != JsonValueKind.Null)
                continue;

            var attacks = new List<(string Attacker, string Target)>();
            foreach (var attack in state.GetProperty("legalAttackTargets").EnumerateObject())
            foreach (var target in attack.Value.EnumerateArray())
            {
                var targetId = target.GetString();
                if (!string.IsNullOrWhiteSpace(targetId)
                    && !string.Equals(targetId, "master", StringComparison.OrdinalIgnoreCase))
                    attacks.Add((attack.Name, targetId));
            }
            if (attacks.Count > 0)
            {
                var selected = attacks[selector.Next(attacks.Count)];
                return new(playerIndex, "attack", new Dictionary<string, object?>
                {
                    ["type"] = "attack",
                    ["cardInstanceId"] = selected.Attacker,
                    ["target"] = new Dictionary<string, object?>
                    {
                        ["type"] = "legion",
                        ["instanceId"] = selected.Target,
                    },
                }, null);
            }

            var own = PlayerView(state, playerIndex);
            var turnKey = $"{playerIndex}:{ReadInt32(state, "turnSerial")}";
            if (!playedTurns.Contains(turnKey))
            {
                var row = own.GetProperty("field")[0];
                var slot = -1;
                for (var index = 0; index < row.GetArrayLength(); index++)
                    if (row[index].ValueKind == JsonValueKind.Null) { slot = index; break; }
                if (slot >= 0)
                {
                    var budget = ReadInt32(own, "spendableResourceCount");
                    var candidates = own.GetProperty("hand").EnumerateArray().Where(card =>
                        string.Equals(ReadString(card, "cardType"), "legion", StringComparison.OrdinalIgnoreCase)
                        && card.TryGetProperty("instanceId", out var instance)
                        && instance.ValueKind == JsonValueKind.String
                        && card.TryGetProperty("playCost", out var cost)
                        && cost.ValueKind == JsonValueKind.Number
                        && cost.TryGetInt32(out var playCost)
                        && playCost >= 0 && playCost <= budget
                        && (!card.TryGetProperty("playBlockedReason", out var blocked)
                            || blocked.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                            || string.IsNullOrWhiteSpace(blocked.GetString()))).ToArray();
                    if (candidates.Length > 0)
                    {
                        var card = candidates[selector.Next(candidates.Length)];
                        return new(playerIndex, "playCard", new Dictionary<string, object?>
                        {
                            ["type"] = "playCard",
                            ["cardInstanceId"] = ReadString(card, "instanceId"),
                            ["row"] = 0,
                            ["slot"] = slot,
                            ["targetPlayerIndex"] = playerIndex,
                            ["choice"] = "normal-entry",
                        }, turnKey);
                    }
                }
            }
            return new(playerIndex, "endTurn", new Dictionary<string, object?>
            {
                ["type"] = "endTurn",
            }, null);
        }

        var states = latestByPlayer.Select(frame => frame.Payload.GetProperty("state"))
            .Select(state => new
            {
                phase = ReadString(state, "phase"),
                revision = ReadInt64(state, "revision"),
                activePlayer = ReadInt32(state, "activePlayer"),
                prompts = state.GetProperty("prompts").GetArrayLength(),
                stack = state.GetProperty("effectStack").GetArrayLength(),
            }).ToArray();
        throw new Xunit.Sdk.XunitException(
            "no legal command selected from authoritative snapshots: " + JsonSerializer.Serialize(states));
    }

    private static JsonElement PlayerView(JsonElement state, int playerIndex) =>
        state.GetProperty("players").EnumerateArray()
            .Single(player => ReadInt32(player, "playerIndex") == playerIndex);

    private static void CopyPromptBinding(JsonElement prompt,
        IDictionary<string, object?> command, string property)
    {
        if (prompt.TryGetProperty(property, out var value)
            && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            command[property] = value.Clone();
    }

    private static bool IsAdvancedGameState(JsonElement payload, long revision) =>
        ReadString(payload, "type") == "gameState"
        && payload.TryGetProperty("state", out var state)
        && ReadInt64(state, "revision") > revision;

    private static int ReadInt32(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number : throw new Xunit.Sdk.XunitException($"missing integer property {name}");

    private static long ReadInt64(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number : throw new Xunit.Sdk.XunitException($"missing integer property {name}");

    private static async Task<LargeDurabilityEvidence> AssertLargeDurableTerminalAsync(
        MatchRecorder recorder, string databasePath, string matchId, string requestId,
        int expectedCommandCount)
    {
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        Assert.Equal(expectedCommandCount, detail.Commands.Count);
        var terminal = detail.Commands[^1];
        Assert.Equal("surrender", ReadString(terminal.Command, "type"));
        Assert.True(terminal.Accepted);
        Assert.NotNull(detail.Match.EndedUtc);
        Assert.NotNull(detail.Match.FinalHash);
        Assert.Equal(terminal.StateHash, detail.Match.FinalHash);

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(matchId));
        Assert.Equal(terminal.Sequence, recovered.CommandSequence);
        Assert.Equal(L12Phase.GameOver, recovered.Engine.State.Phase);
        Assert.Equal(detail.Match.FinalHash, recovered.Engine.ComputeStateHash());
        var processed = Assert.Single(recovered.ProcessedRequests,
            item => item.RequestId == requestId);
        Assert.True(processed.Accepted);
        Assert.Equal(terminal.Revision, processed.Revision);

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = """
            SELECT sequence,state_hash,uncompressed_bytes
            FROM match_state_checkpoints
            WHERE match_id=$match
            ORDER BY sequence DESC
            LIMIT 1;
            """;
        checkpoint.Parameters.AddWithValue("$match", matchId);
        await using var reader = await checkpoint.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(terminal.Sequence, reader.GetInt64(0));
        Assert.Equal(detail.Match.FinalHash, reader.GetString(1));
        var checkpointBytes = reader.GetInt64(2);
        Assert.True(checkpointBytes > 0);
        Assert.False(await reader.ReadAsync());
        return new(detail.Commands.Count, checkpointBytes, true, true);
    }

    [Fact]
    public void LocalMetricDeltaRejectsContentionDropsInsteadOfClaimingCompleteEvidence()
    {
        const string stage = "duration.snapshot.build";
        var observedAt = DateTimeOffset.UnixEpoch;
        var prior = new PerformanceMetricSummary(stage, "ms", observedAt, observedAt,
            1, 1, 1, false, [], null, null, 0, 0);
        var diagnostics = new PerformanceMetricDiagnostics(0, 0, 0, 0, 0, 0);
        var before = new PerformanceMetricBatch([prior], diagnostics);
        var clean = prior with { Count = 2, Total = 2 };
        Assert.True(MetricDelta(before, new([clean], diagnostics), [stage]).Available);

        var dropped = clean with { DroppedContentionTotal = 1 };
        var incomplete = MetricDelta(before, new([dropped], diagnostics), [stage]);
        Assert.False(incomplete.Available);
        Assert.Equal($"samples-dropped:{stage}", incomplete.Reason);
        Assert.Null(incomplete.Stages);
    }

    private static PerformanceMetricBatch? PerformanceSnapshot() =>
        L12PerformanceMetrics.TrySnapshot(out var batch) ? batch : null;

    private static MetricDeltaEvidence MetricDelta(PerformanceMetricBatch? before,
        PerformanceMetricBatch? after, IReadOnlyList<string> stages)
    {
        if (before is null || after is null)
            return new(false, "snapshot-unavailable", null);
        var prior = before.Series.ToDictionary(row => row.Stage, StringComparer.Ordinal);
        var current = after.Series.ToDictionary(row => row.Stage, StringComparer.Ordinal);
        var rows = new Dictionary<string, MetricStageDelta>(StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            if (!prior.TryGetValue(stage, out var first) || !current.TryGetValue(stage, out var last))
                return new(false, $"stage-missing:{stage}", null);
            if (first.StartedAt != last.StartedAt || last.Count < first.Count
                || last.Total < first.Total
                || last.DroppedContentionTotal < first.DroppedContentionTotal)
                return new(false, $"window-changed:{stage}", null);
            if (last.DroppedContentionTotal != first.DroppedContentionTotal)
                return new(false, $"samples-dropped:{stage}", null);
            rows[stage] = new(last.Count - first.Count, last.Total - first.Total,
                last.Maximum, last.DroppedContentionTotal - first.DroppedContentionTotal, last.Unit);
        }
        return new(true, null, rows);
    }

    private static Dictionary<string, long> NumericDiagnostic(JsonElement diagnostic)
    {
        var names = new[]
        {
            "in_fifo_us", "in_depth", "in_bytes", "in_remaining", "pre_gate_us",
            "gate_wait_us", "gate_hold_us", "serialize_us", "out_enq_depth",
            "out_deq_depth", "out_queue_us", "prepare_us", "wire_bytes", "wire_delta",
            "send_us", "commit_us", "server_total_us", "receive_to_enqueue_us",
            "receive_valid", "lease_valid", "lease_start_rel_us", "lease_end_rel_us",
            "enqueue_rel_us", "send_start_rel_us", "send_end_rel_us", "commit_rel_us",
            "receipt_rel_us", "receipt_valid", "receipt_after_send_valid",
        };
        return names.ToDictionary(name => name,
            name => diagnostic.GetProperty(name).GetInt64(), StringComparer.Ordinal);
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

    private sealed record StartedLargeRoom(string MatchId, ReceivedFrame Actor, ReceivedFrame Opponent);
    private sealed record SelectedLargeCommand(int PlayerIndex, string Type,
        IReadOnlyDictionary<string, object?> Command, string? TurnKey);
    private sealed record BeforeCommitObservation(int Ordinal, int Kind, long RelativeMicroseconds);
    private sealed record LargeDurabilityEvidence(int CommandCount, long CheckpointUncompressedBytes,
        bool CheckpointHashVerified, bool ReplayHashVerified);
    private sealed record MetricStageDelta(long Count, double Total, double WindowMaximum,
        long DroppedContention, string Unit);
    private sealed record MetricDeltaEvidence(bool Available, string? Reason,
        IReadOnlyDictionary<string, MetricStageDelta>? Stages);
    private sealed record ReceivedFrame(JsonElement Payload, long ReceivedAt, int RawBytes);

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
            return new(document.RootElement.Clone(), receivedAt, checked((int)message.Length));
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
