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
    [InlineData("held-peer")]
    [InlineData("held-actor")]
    public async Task BothTerminalRecipientsKeepTheirOwnQueuePrepareSendAndAuthenticatedReceipt(string arm)
    {
        using var identity = new RankedIdentityFixture();
        var actorRegistration = identity.Store.Register($"ra{Guid.NewGuid():N}"[..10], "Password123!");
        var peerRegistration = identity.Store.Register($"rb{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(actorRegistration.Success);
        Assert.True(peerRegistration.Success);
        var evidence = new ConcurrentQueue<string>();
        var nonce = Guid.NewGuid().ToString("N");
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.RecipientsMode}:1:{nonce}", evidence.Enqueue,
            ThreeSeconds, TimeSpan.FromMilliseconds(10))!;
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(identity.Path)!, $"recipients-{arm}.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog, null, null, null, null, null, null, null, diagnostics);
        SocketPeer? actor = null;
        SocketPeer? peer = null;
        HeldTargetSender? held = null;
        var started = false;
        try
        {
            await server.StartAsync(0).WaitAsync(SetupDeadline);
            started = true;
            var endpoint = new UriBuilder(Assert.Single(server.Addresses))
                { Host = "127.0.0.1", Scheme = "ws", Path = "/ws" }.Uri;
            actor = await SocketPeer.ConnectAsync(endpoint, actorRegistration.Token!);
            peer = await SocketPeer.ConnectAsync(endpoint, peerRegistration.Token!);
            await StartFriendlyGameAsync(actor, peer);
            await AssertHealthyAsync(actor);
            await AssertHealthyAsync(peer);
            if (arm != "normal")
            {
                held = await HeldTargetSender.InstallAsync(server, arm == "held-peer" ? peer.SessionId : actor.SessionId);
                await held.EnterAsync();
            }
            var requestId = $"l12diag:{nonce}:0:{Guid.NewGuid():N}";
            var actorTask = actor.ReceiveAsync(frame => IsGameOver(frame)
                && ReadString(frame, "requestId") == requestId, ThreeSeconds);
            var peerTask = peer.ReceiveAsync(IsGameOver, ThreeSeconds);
            var sentAt = Stopwatch.GetTimestamp();
            await actor.SendAsync(new { type = "gameAction", requestId, command = new { type = "surrender" } });
            if (held is not null)
            {
                _ = await (arm == "held-peer" ? actorTask : peerTask);
                Assert.False((arm == "held-peer" ? peerTask : actorTask).IsCompleted);
                await Task.Delay(1100);
                held.Release();
            }
            var frames = await Task.WhenAll(actorTask, peerTask);
            Assert.All(frames, frame => Assert.True(ElapsedMilliseconds(sentAt, frame.ReceivedAt) < 3000));
            Assert.Null(ReadString(frames[1].Payload, "requestId"));
            Assert.Equal(frames[0].Payload.GetProperty("state").GetProperty("matchId").GetString(),
                frames[1].Payload.GetProperty("state").GetProperty("matchId").GetString());
            Assert.Equal(ReadInt64(frames[0].Payload.GetProperty("state"), "revision"),
                ReadInt64(frames[1].Payload.GetProperty("state"), "revision"));
            var peers = new[] { actor, peer };
            for (var role = 0; role < 2; role++)
            {
                await peers[role].SendAsync(new { type = "ping", diagnosticNonce = nonce,
                    diagnosticSlot = 0, diagnosticRecipient = role });
                var pong = await peers[role].ReceiveTypeAsync("pong", ThreeSeconds);
                Assert.Equal(new[] { "type", "utc" }, pong.Payload.EnumerateObject().Select(property => property.Name));
            }
            using var deadline = new CancellationTokenSource(SetupDeadline);
            while (evidence.Count < 3) await Task.Delay(10, deadline.Token);
            Assert.Equal(3, evidence.Count);
            var rows = evidence.Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .Where(row => row.GetProperty("kind").GetInt32() == 1)
                .OrderBy(row => row.GetProperty("recipient").GetInt32()).ToArray();
            Assert.Equal(2, rows.Length);
            for (var role = 0; role < 2; role++)
            {
                var row = rows[role];
                Assert.Equal(4, row.GetProperty("schema").GetInt32());
                Assert.Equal(role, row.GetProperty("recipient").GetInt32());
                Assert.Equal((int)L12SyntheticTransportStatus.Sent, row.GetProperty("status").GetInt32());
                Assert.Equal(1, row.GetProperty("receipt_valid").GetInt32());
                Assert.Equal(1, row.GetProperty("receipt_after_send_valid").GetInt32());
                Assert.Equal(1, row.GetProperty("append_valid").GetInt32());
                Assert.Equal(frames[role].RawBytes, row.GetProperty("wire_bytes").GetInt32());
                Assert.True(row.GetProperty("send_end_rel_us").GetInt64() >= row.GetProperty("send_start_rel_us").GetInt64());
                output.WriteLine("recipient-arm={0} role={1} callback_us={2} queue_us={3} prepare_us={4} send_us={5} bytes={6}",
                    arm, role, ElapsedMicroseconds(sentAt, frames[role].ReceivedAt),
                    row.GetProperty("out_queue_us").GetInt64(), row.GetProperty("prepare_us").GetInt64(),
                    row.GetProperty("send_us").GetInt64(), row.GetProperty("wire_bytes").GetInt32());
            }
            if (held is not null)
                Assert.True(rows[arm == "held-peer" ? 1 : 0].GetProperty("out_queue_us").GetInt64() >= 1_000_000);
            using var summary = JsonDocument.Parse(evidence.Last());
            Assert.Equal(2, summary.RootElement.GetProperty("receipts").GetInt32());
            Assert.Equal(0, summary.RootElement.GetProperty("missing").GetInt32());
            Assert.Equal(0, summary.RootElement.GetProperty("invalid").GetInt32());
        }
        finally
        {
            held?.Release();
            if (actor is not null) await actor.DisposeAsync();
            if (peer is not null) await peer.DisposeAsync();
            if (started) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

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
            var grown = await GrowLargeFriendlyRoomAsync(actor, opponent, started);
            var acceptedCommands = grown.AcceptedCommands;
            var commandTypes = grown.CommandTypes;
            var preTerminalBytes = grown.PreTerminalBytes;
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

    [Theory]
    [InlineData("normal")]
    [InlineData("holdAppend")]
    [InlineData("holdComplete")]
    public Task LocalhostLargeTwoRoomRecorderBarrierSeparatesActualOutboundEnqueue(string arm)
        => RunLargeTwoRoomRecorderBarrierAsync(arm, recorderV3: false);

    [Theory]
    [InlineData("normal")]
    [InlineData("holdAppend")]
    [InlineData("holdComplete")]
    public Task TerminalRecorderV3MeasuresExactSharedWriteGateWaitBeforeOutbound(string arm)
        => RunLargeTwoRoomRecorderBarrierAsync(arm, recorderV3: true);

    private async Task RunLargeTwoRoomRecorderBarrierAsync(string arm, bool recorderV3)
    {
        var heldStage = arm switch
        {
            "normal" => null,
            "holdAppend" => "before-match-command-commit",
            "holdComplete" => "before-match-complete-commit",
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };
        using var identity = new RankedIdentityFixture();
        var registrations = Enumerable.Range(0, 5).Select(index =>
            identity.Store.Register($"dq{index}{Guid.NewGuid():N}"[..10], "Password123!")).ToArray();
        Assert.All(registrations, registration => Assert.True(registration.Success));
        var evidence = new ConcurrentQueue<string>();
        var nonce = Guid.NewGuid().ToString("N");
        using var diagnostics = Assert.IsType<L12SyntheticTransportDiagnostics>(
            L12SyntheticTransportDiagnostics.Create(true,
                $"{(recorderV3 ? L12SyntheticTransportDiagnostics.RecorderMode : L12SyntheticTransportDiagnostics.BoundsMode)}:2:{nonce}", evidence.Enqueue,
                ThreeSeconds, TimeSpan.FromMilliseconds(10)));
        var databasePath = Path.Combine(Path.GetDirectoryName(identity.Path)!, $"causal-two-large-{arm}.db");
        await using var recorder = new MatchRecorder(databasePath);
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog, null, null, null, null, null, null, null, diagnostics);
        var peers = new List<SocketPeer>();
        using var barrierEntered = new ManualResetEventSlim();
        using var barrierRelease = new ManualResetEventSlim();
        using var barrierExited = new ManualResetEventSlim();
        var barrierLimit = TimeSpan.FromSeconds(2);
        var beforeCommit = new ConcurrentQueue<BeforeCommitObservation>();
        var ordinal = 0;
        var barrierClaimed = 0;
        long barrierEnteredAt = 0, barrierReleaseRequestedAt = 0, barrierExitedAt = 0;
        var serverStarted = false;
        try
        {
            await server.StartAsync(0).WaitAsync(SetupDeadline);
            serverStarted = true;
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            foreach (var registration in registrations)
                peers.Add(await SocketPeer.ConnectAsync(endpoint, registration.Token!));
            var aActor = peers[0];
            var aOpponent = peers[1];
            var bActor = peers[2];
            var bOpponent = peers[3];
            var healthy = peers[4];
            var roomA = await StartLargeFriendlyGameAsync(aActor, aOpponent);
            var roomB = await StartLargeFriendlyGameAsync(bActor, bOpponent);
            var grownA = await GrowLargeFriendlyRoomAsync(aActor, aOpponent, roomA);
            var grownB = await GrowLargeFriendlyRoomAsync(bActor, bOpponent, roomB);
            foreach (var peer in peers) await AssertHealthyAsync(peer);
            var gateB = CausalRoomGate(rooms, roomB.RoomCode);
            Assert.Equal(1, gateB.CurrentCount);
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
                    beforeCommit.Enqueue(new(Interlocked.Increment(ref ordinal), kind,
                        ElapsedMicroseconds(observationStartedAt, Stopwatch.GetTimestamp())));
                if (heldStage is null || stage != heldStage
                    || Interlocked.CompareExchange(ref barrierClaimed, 1, 0) != 0) return;
                Interlocked.Exchange(ref barrierEnteredAt, Stopwatch.GetTimestamp());
                barrierEntered.Set();
                try
                {
                    if (!barrierRelease.Wait(barrierLimit))
                        throw new TimeoutException("bounded large two-room recorder barrier timed out");
                }
                finally
                {
                    Interlocked.Exchange(ref barrierExitedAt, Stopwatch.GetTimestamp());
                    barrierExited.Set();
                }
            };

            var requestA = $"l12diag:{nonce}:0:{Guid.NewGuid():N}";
            var requestB = $"l12diag:{nonce}:1:{Guid.NewGuid():N}";
            var aActorTerminal = aActor.ReceiveAsync(
                payload => IsGameOver(payload) && ReadString(payload, "requestId") == requestA, ThreeSeconds);
            var aOpponentTerminal = aOpponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            var sentAAt = Stopwatch.GetTimestamp();
            await aActor.SendAsync(new { type = "gameAction", requestId = requestA,
                command = new { type = "surrender" } });
            if (heldStage is not null)
                Assert.True(await WaitForCausalConditionAsync(() => barrierEntered.IsSet,
                    TimeSpan.FromSeconds(1)), "room A did not enter the selected recorder barrier");

            var bActorTerminal = bActor.ReceiveAsync(
                payload => IsGameOver(payload) && ReadString(payload, "requestId") == requestB, ThreeSeconds);
            var bOpponentTerminal = bOpponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            var sentBAt = Stopwatch.GetTimestamp();
            await bActor.SendAsync(new { type = "gameAction", requestId = requestB,
                command = new { type = "surrender" } });
            OutboundTimeline? bWhileHeld = null;
            L12SyntheticTransportSnapshot? bRecorderWhileHeld = null;
            if (heldStage is not null)
            {
                Assert.True(await WaitForCausalConditionAsync(() => diagnostics.AcceptedSamples == 2
                    && ReadActualOutboundTimeline(diagnostics, 1).GateAcquiredAt > 0
                    && gateB.CurrentCount == 0,
                    TimeSpan.FromMilliseconds(750)), "room B never acquired its independent room gate");
                Assert.False(barrierExited.IsSet);
                Assert.Equal(2, diagnostics.AcceptedSamples);
                bWhileHeld = ReadActualOutboundTimeline(diagnostics, 1);
                Assert.True(bWhileHeld.GateAcquiredAt >= sentBAt);
                Assert.Equal(0, bWhileHeld.EnqueuedAt);
                Assert.Equal(0, bWhileHeld.DequeuedAt);
                Assert.False(aActorTerminal.IsCompleted);
                Assert.False(aOpponentTerminal.IsCompleted);
                Assert.False(bActorTerminal.IsCompleted);
                Assert.False(bOpponentTerminal.IsCompleted);
                if (recorderV3)
                {
                    Assert.True(await WaitForCausalConditionAsync(() =>
                        diagnostics.SnapshotForTests().Single(row => row.Slot == 1)
                            .RecorderAppend.Count == 1, TimeSpan.FromMilliseconds(750)),
                        "room B never reached its precise recorder append wait");
                    bRecorderWhileHeld = diagnostics.SnapshotForTests().Single(row => row.Slot == 1);
                    Assert.True(bRecorderWhileHeld.Value.RecorderAppend.WaitStartedRelativeMicroseconds
                        >= bRecorderWhileHeld.Value.RoomGateAcquiredRelativeMicroseconds);
                    Assert.Equal(0, bRecorderWhileHeld.Value.RecorderAppend.AcquiredRelativeMicroseconds);
                    Assert.Equal(0, bRecorderWhileHeld.Value.RecorderAppend.HoldEndedRelativeMicroseconds);
                    Assert.Equal(0, bRecorderWhileHeld.Value.RecorderAppend.Valid);
                    Assert.Equal(0, bRecorderWhileHeld.Value.RecorderComplete.Count);
                }
            }

            var pingSentAt = Stopwatch.GetTimestamp();
            await healthy.SendAsync(new { type = "ping" });
            var healthyPong = await healthy.ReceiveTypeAsync("pong",
                heldStage is null ? ThreeSeconds : TimeSpan.FromSeconds(1));
            if (heldStage is not null)
            {
                Assert.False(barrierExited.IsSet, "healthy pong was only received after the barrier exited");
                Assert.Equal(0, gateB.CurrentCount);
                Interlocked.Exchange(ref barrierReleaseRequestedAt, Stopwatch.GetTimestamp());
                barrierRelease.Set();
                Assert.True(await WaitForCausalConditionAsync(() => barrierExited.IsSet,
                    TimeSpan.FromSeconds(1)), "recorder barrier did not exit after its bounded release");
            }

            var terminalFrames = await Task.WhenAll(aActorTerminal, aOpponentTerminal,
                bActorTerminal, bOpponentTerminal);
            var sentAt = new[] { sentAAt, sentAAt, sentBAt, sentBAt };
            for (var index = 0; index < terminalFrames.Length; index++)
            {
                Assert.True(Stopwatch.GetElapsedTime(sentAt[index], terminalFrames[index].ReceivedAt) < ThreeSeconds,
                    $"large-state room peer {index} exceeded the original three-second terminal boundary");
                Assert.InRange(terminalFrames[index].RawBytes, MinimumTerminalBytes, MaximumTerminalBytes);
            }
            Assert.Null(ReadString(terminalFrames[1].Payload, "requestId"));
            Assert.Null(ReadString(terminalFrames[3].Payload, "requestId"));
            // Receipts are sent only after complete terminal frames arrived. Pongs also drain
            // each peer's existing sender before asserting one and only one terminal per seat.
            for (var index = 0; index < 4; index++)
            {
                if (index is 0 or 2)
                    await peers[index].SendAsync(new { type = "ping", diagnosticNonce = nonce,
                        diagnosticSlot = index / 2 });
                else await peers[index].SendAsync(new { type = "ping" });
                _ = await peers[index].ReceiveTypeAsync("pong", ThreeSeconds);
                Assert.Equal(1, peers[index].Received.Count(frame => IsGameOver(frame.Payload)));
            }

            var rows = new[] { await ReadDiagnosticRowAsync(evidence, SetupDeadline),
                await ReadDiagnosticRowAsync(evidence, SetupDeadline) }.ToDictionary(
                    row => row.GetProperty("slot").GetInt32());
            Assert.Equal(new[] { 0, 1 }, rows.Keys.Order().ToArray());
            Assert.Equal(2, diagnostics.CompletedSamples);
            var outbound = new[] { ReadActualOutboundTimeline(diagnostics, 0),
                ReadActualOutboundTimeline(diagnostics, 1) };
            for (var slot = 0; slot < 2; slot++)
            {
                var row = rows[slot];
                Assert.Equal(recorderV3 ? 3 : 2, row.GetProperty("schema").GetInt32());
                Assert.Equal((int)L12SyntheticTransportStatus.Sent, row.GetProperty("status").GetInt32());
                Assert.Equal(1, row.GetProperty("receive_valid").GetInt32());
                Assert.Equal(1, row.GetProperty("receipt_valid").GetInt32());
                Assert.Equal(1, row.GetProperty("receipt_after_send_valid").GetInt32());
                Assert.Equal(0, row.GetProperty("wire_delta").GetInt32());
                Assert.Equal(terminalFrames[slot * 2].RawBytes, row.GetProperty("wire_bytes").GetInt32());
                Assert.True(outbound[slot].EnqueuedAt >= sentAt[slot * 2]);
                Assert.True(outbound[slot].DequeuedAt >= outbound[slot].EnqueuedAt);
                Assert.True(outbound[slot].DequeuedAt <= terminalFrames[slot * 2].ReceivedAt);
                Assert.Equal(ElapsedMicroseconds(outbound[slot].EnqueuedAt, outbound[slot].DequeuedAt),
                    row.GetProperty("out_queue_us").GetInt64());
                if (heldStage is not null)
                    Assert.True(outbound[slot].EnqueuedAt >= Interlocked.Read(ref barrierExitedAt),
                        "actual terminal outbound enqueue preceded the recorder callback exit");
                if (recorderV3)
                {
                    foreach (var operation in new[] { "append", "complete" })
                    {
                        Assert.Equal(1, row.GetProperty($"{operation}_count").GetInt32());
                        Assert.Equal(1, row.GetProperty($"{operation}_valid").GetInt32());
                        var wait = row.GetProperty($"{operation}_wait_rel_us").GetInt64();
                        var acquired = row.GetProperty($"{operation}_acquired_rel_us").GetInt64();
                        var holdEnd = row.GetProperty($"{operation}_hold_end_rel_us").GetInt64();
                        Assert.True(wait >= row.GetProperty("room_acquired_rel_us").GetInt64());
                        Assert.True(acquired >= wait);
                        Assert.True(holdEnd >= acquired);
                        Assert.True(row.GetProperty("out_enqueued_rel_us").GetInt64() >= holdEnd);
                    }
                    Assert.True(row.GetProperty("complete_wait_rel_us").GetInt64()
                        >= row.GetProperty("append_hold_end_rel_us").GetInt64());
                    Assert.True(row.GetProperty("out_dequeued_rel_us").GetInt64()
                        >= row.GetProperty("out_enqueued_rel_us").GetInt64());
                    Assert.True(row.GetProperty("send_start_rel_us").GetInt64()
                        >= row.GetProperty("out_dequeued_rel_us").GetInt64());
                    if (heldStage is not null && slot == 1)
                    {
                        var origin = ReadReceiveCompletedAt(diagnostics, slot);
                        var barrierExit = ElapsedMicroseconds(origin, Interlocked.Read(ref barrierExitedAt));
                        Assert.True(row.GetProperty("append_wait_rel_us").GetInt64() <= barrierExit);
                        Assert.True(row.GetProperty("append_acquired_rel_us").GetInt64() >= barrierExit);
                        var held = Assert.IsType<L12SyntheticTransportSnapshot>(bRecorderWhileHeld);
                        Assert.Equal(held.RecorderAppend.WaitStartedRelativeMicroseconds,
                            row.GetProperty("append_wait_rel_us").GetInt64());
                    }
                }
            }
            if (heldStage is not null)
            {
                Assert.True(Interlocked.Read(ref barrierReleaseRequestedAt) >= Interlocked.Read(ref barrierEnteredAt));
                Assert.True(Interlocked.Read(ref barrierExitedAt) >= Interlocked.Read(ref barrierReleaseRequestedAt));
                Assert.InRange(ElapsedMilliseconds(barrierEnteredAt, barrierExitedAt), 0, barrierLimit.TotalMilliseconds);
            }
            var durableA = await AssertLargeDurableTerminalAsync(recorder, databasePath,
                roomA.MatchId, requestA, grownA.AcceptedCommands + 1);
            var durableB = await AssertLargeDurableTerminalAsync(recorder, databasePath,
                roomB.MatchId, requestB, grownB.AcceptedCommands + 1);
            var callbacks = beforeCommit.OrderBy(item => item.Ordinal).ToArray();
            Assert.Equal(2, callbacks.Count(item => item.Kind == 1));
            Assert.Equal(2, callbacks.Count(item => item.Kind == 2));

            output.WriteLine(JsonSerializer.Serialize(new
            {
                schema = 1,
                scenario = "two-friendly-rooms-large-legal-state-actual-outbound-enqueue",
                arm,
                transportDiagnosticSchema = recorderV3 ? 3 : 2,
                commandSelectionSeed = LargeStateSelectionSeed,
                productStateOrRngInjected = false,
                maximumAcceptedCommandsPerRoom = MaximumAcceptedLargeStateCommands,
                acceptedCommands = new[] { grownA.AcceptedCommands, grownB.AcceptedCommands },
                commandTypes = new[] { grownA.CommandTypes, grownB.CommandTypes },
                preTerminalBytes = new[] { grownA.PreTerminalBytes, grownB.PreTerminalBytes },
                terminalBytes = terminalFrames.Select(frame => frame.RawBytes).ToArray(),
                terminalRangeBytes = new { minimum = MinimumTerminalBytes, maximum = MaximumTerminalBytes },
                originalThresholdMicroseconds = (long)(ThreeSeconds.TotalMilliseconds * 1000),
                terminalSendToRawMicroseconds = terminalFrames.Select((frame, index) =>
                    ElapsedMicroseconds(sentAt[index], frame.ReceivedAt)).ToArray(),
                healthyPingMicroseconds = ElapsedMicroseconds(pingSentAt, healthyPong.ReceivedAt),
                barrierApplicable = heldStage is not null,
                barrierEnteredRelativeMicroseconds = heldStage is null ? (long?)null :
                    ElapsedMicroseconds(observationStartedAt, barrierEnteredAt),
                barrierReleaseRequestedRelativeMicroseconds = heldStage is null ? (long?)null :
                    ElapsedMicroseconds(observationStartedAt, barrierReleaseRequestedAt),
                barrierExitedRelativeMicroseconds = heldStage is null ? (long?)null :
                    ElapsedMicroseconds(observationStartedAt, barrierExitedAt),
                bOutboundEnqueuedWhileHeld = bWhileHeld is null ? (bool?)null : bWhileHeld.EnqueuedAt != 0,
                bOutboundDequeuedWhileHeld = bWhileHeld is null ? (bool?)null : bWhileHeld.DequeuedAt != 0,
                bRecorderWhileHeld,
                outbound = outbound.Select((timing, slot) => new
                {
                    slot,
                    enqueuedRelativeMicroseconds = ElapsedMicroseconds(observationStartedAt, timing.EnqueuedAt),
                    gateAcquiredRelativeMicroseconds = ElapsedMicroseconds(observationStartedAt, timing.GateAcquiredAt),
                    dequeuedRelativeMicroseconds = ElapsedMicroseconds(observationStartedAt, timing.DequeuedAt),
                    queuedMicroseconds = ElapsedMicroseconds(timing.EnqueuedAt, timing.DequeuedAt),
                    requestDiagnostic = NumericDiagnostic(rows[slot]),
                    recorderDiagnostic = recorderV3 ? rows[slot].EnumerateObject()
                        .Where(property => property.Name.StartsWith("append_", StringComparison.Ordinal)
                            || property.Name.StartsWith("complete_", StringComparison.Ordinal)
                            || property.Name is "room_acquired_rel_us" or "out_enqueued_rel_us" or "out_dequeued_rel_us")
                        .ToDictionary(property => property.Name, property => property.Value.GetInt64()) : null,
                }).ToArray(),
                durability = new[] { durableA, durableB },
                exactTerminalFramesPerSeat = 1,
                observedBeforeCommitSequence = callbacks.Select(item => item.Kind).ToArray(),
                timingBoundary = "same-process Stopwatch; actual _outboundEnqueuedAt/_outboundDequeuedAt, not inbound enqueue_rel_us; recorder wait is immediately before WaitAsync through resumption; hold end is immediately before Release, not fsync; barrier exit is pre-commit callback exit; raw arrival is complete WS message before JSON parse, not native first byte or one-way network",
                scopeBoundary = "controlled localhost two-room evidence only, not original 30+10 workload acceptance or production-tail root cause",
            }));
        }
        finally
        {
            barrierRelease.Set();
            recorder.StorageFailureInjector = null;
            foreach (var peer in peers) await peer.DisposeAsync();
            if (serverStarted) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

    private static OutboundTimeline ReadActualOutboundTimeline(
        L12SyntheticTransportDiagnostics diagnostics, int slot)
    {
        L12SyntheticTransportSample sample;
        lock (Field<object>(diagnostics, "_gate"))
            sample = Assert.IsType<L12SyntheticTransportSample>(
                Field<L12SyntheticTransportSample?[]>(diagnostics, "_samples")[slot]);
        // Pending reads occur only while the recorder barrier prevents enqueue; final reads
        // occur after the complete frame and its authenticated receipt/pong, not during a send.
        return new(Field<long>(sample, "_outboundEnqueuedAt"),
            Field<long>(sample, "_outboundDequeuedAt"), Field<long>(sample, "_gateAcquiredAt"));
    }

    private static long ReadReceiveCompletedAt(L12SyntheticTransportDiagnostics diagnostics, int slot)
    {
        lock (Field<object>(diagnostics, "_gate"))
            return Field<long>(Field<L12SyntheticTransportSample?[]>(diagnostics, "_samples")[slot]!,
                "_receiveCompletedAt");
    }

    private static SemaphoreSlim CausalRoomGate(L12RoomManager manager, string roomCode)
    {
        var rooms = Field<System.Collections.IEnumerable>(manager, "_rooms");
        foreach (var entry in rooms)
        {
            var room = entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
            if (string.Equals((string)room.GetType().GetProperty("Code")!.GetValue(room)!, roomCode,
                    StringComparison.OrdinalIgnoreCase))
                return (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        }
        throw new InvalidOperationException("missing large-state causal room");
    }

    private static async Task<bool> WaitForCausalConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public void LargeStateSelectorBuildsEveryAuthoritativePlacementWireCommand()
    {
        var scenarios = new (string Mode, int ChoiceCount, string[] Top, string[] Bottom,
            string? Destination)[]
        {
            ("single-top-bottom", 1, [], [], "bottom"),
            ("split-top-bottom", 2, ["choice-a", "choice-b"], [], null),
            ("all-top-bottom", 2, ["choice-a", "choice-b"], [], null),
            ("all-bottom", 2, [], ["choice-a", "choice-b"], null),
        };
        foreach (var scenario in scenarios)
        {
            var choices = new[] { "choice-a", "choice-b" }.Take(scenario.ChoiceCount).ToArray();
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                promptId = $"prompt-{scenario.Mode}",
                playerIndex = 0,
                validChoices = choices,
                minChoose = scenario.ChoiceCount,
                maxChoose = scenario.ChoiceCount,
                activationId = "activation-contract",
                sourceInstanceId = "source-instance-contract",
                sourceCardId = "source-card-contract",
                step = 4,
                createdRevision = 99L,
                controller = 0,
                data = new { placementMode = scenario.Mode, action = "contract-placement" },
            }, Wire));

            var command = BuildLargePromptCommand(document.RootElement);
            Assert.Equal("resolvePrompt", command["type"]);
            Assert.Equal($"prompt-{scenario.Mode}", command["promptId"]);
            Assert.Equal("activation-contract",
                Assert.IsType<JsonElement>(command["activationId"]).GetString());
            Assert.Equal("source-instance-contract",
                Assert.IsType<JsonElement>(command["sourceInstanceId"]).GetString());
            Assert.Equal("source-card-contract",
                Assert.IsType<JsonElement>(command["sourceCardId"]).GetString());
            Assert.Equal(4, Assert.IsType<JsonElement>(command["step"]).GetInt32());
            Assert.Equal(99L, Assert.IsType<JsonElement>(command["createdRevision"]).GetInt64());
            Assert.Equal(0, Assert.IsType<JsonElement>(command["controller"]).GetInt32());
            if (scenario.Destination is not null)
            {
                Assert.Equal(choices, Assert.IsType<string[]>(command["cardInstanceIds"]));
                Assert.Equal(scenario.Destination, command["destination"]);
                Assert.False(command.ContainsKey("topCardInstanceIds"));
                Assert.False(command.ContainsKey("bottomCardInstanceIds"));
            }
            else
            {
                Assert.Equal(scenario.Top, Assert.IsType<string[]>(command["topCardInstanceIds"]));
                Assert.Equal(scenario.Bottom,
                    Assert.IsType<string[]>(command["bottomCardInstanceIds"]));
                Assert.False(command.ContainsKey("cardInstanceIds"));
                Assert.False(command.ContainsKey("destination"));
            }
        }

        using var unknown = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            promptId = "prompt-unknown",
            playerIndex = 0,
            validChoices = new[] { "choice-a" },
            minChoose = 1,
            maxChoose = 1,
            data = new { placementMode = "future-placement", action = "future-action" },
        }, Wire));
        var rejection = Assert.Throws<Xunit.Sdk.XunitException>(
            () => BuildLargePromptCommand(unknown.RootElement));
        Assert.Contains("future-placement", rejection.Message, StringComparison.Ordinal);
        Assert.Contains("future-action", rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeStateSelectorChoosesAProductLegalGraveFactionCombination()
    {
        var owner = new L12PlayerState
        {
            PlayerIndex = 0,
            Name = "selector-owner",
            DeckName = "selector-deck",
            Faction = "asgard",
            MasterId = "selector-master",
            MasterName = "selector-master",
        };
        var ordinaryA = new L12CardInstance
        {
            InstanceId = "ordinary-a", CardId = "S01-0301", Name = "ordinary-a",
            CardType = "legion", Faction = "asgard",
        };
        var ordinaryB = new L12CardInstance
        {
            InstanceId = "ordinary-b", CardId = "S01-0302", Name = "ordinary-b",
            CardType = "legion", Faction = "asgard",
        };
        var warrior = new L12CardInstance
        {
            InstanceId = "warrior", CardId = "ST03-08", Name = "warrior",
            CardType = "legion", Faction = "asgard",
        };
        owner.Graveyard.AddRange([ordinaryA, ordinaryB, warrior]);
        var legality = typeof(L12StructuredCardRules).GetMethod(
            "CanPotentiallyRepresentGraveFactionCount", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(legality);
        bool ProductAccepts(params L12CardInstance[] cards) =>
            Assert.IsType<bool>(legality.Invoke(null, [owner, cards, "asgard", 3, false]));
        Assert.False(ProductAccepts(ordinaryA));
        Assert.True(ProductAccepts(warrior));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            promptId = "prompt-grave-faction",
            playerIndex = 0,
            validChoices = new[] { ordinaryA.InstanceId, ordinaryB.InstanceId, warrior.InstanceId },
            minChoose = 1,
            maxChoose = 3,
            data = new Dictionary<string, string>
            {
                ["action"] = "grave-faction-proof",
                ["selectionConstraint"] = "grave-faction-exact",
                ["factionConstraint"] = "asgard",
                ["representedCount"] = "3",
                ["legionCardsOnly"] = "false",
                [$"{ordinaryA.InstanceId}:cardId"] = ordinaryA.CardId,
                [$"{ordinaryA.InstanceId}:cardType"] = ordinaryA.CardType,
                [$"{ordinaryA.InstanceId}:zone"] = "墓地",
                [$"{ordinaryB.InstanceId}:cardId"] = ordinaryB.CardId,
                [$"{ordinaryB.InstanceId}:cardType"] = ordinaryB.CardType,
                [$"{ordinaryB.InstanceId}:zone"] = "墓地",
                [$"{warrior.InstanceId}:cardId"] = warrior.CardId,
                [$"{warrior.InstanceId}:cardType"] = warrior.CardType,
                [$"{warrior.InstanceId}:zone"] = "墓地",
            },
        }, Wire));

        var command = BuildLargePromptCommand(document.RootElement);
        var selectedIds = Assert.IsType<string[]>(command["cardInstanceIds"]);
        var selectedCards = selectedIds.Select(id => owner.Graveyard.Single(card => card.InstanceId == id)).ToArray();
        Assert.True(ProductAccepts(selectedCards),
            $"selector emitted product-illegal grave combination: {string.Join(',', selectedIds)}");
        Assert.Equal(new[] { warrior.InstanceId }, selectedIds);
    }

    [Fact]
    public void LargeStateSelectorHonorsAuthoritativeCombinationMetadataAndFailsClosed()
    {
        Assert.Equal(new[] { "same-a", "unique" }, LargePromptSelectionForTest(
            "prompt-distinct", ["same-a", "same-b", "unique"], 2, 2,
            new Dictionary<string, string>
            {
                ["action"] = "distinct-proof",
                ["selectionConstraint"] = "distinct-card-names",
                ["same-a:name"] = "same",
                ["same-b:name"] = "same",
                ["unique:name"] = "unique",
            }));

        Assert.Equal(new[] { "cost-two-a", "cost-one" }, LargePromptSelectionForTest(
            "prompt-hijikata", ["cost-two-a", "cost-two-b", "cost-one"], 2, 2,
            LargeFieldConstraintData("hijikata-entry-targets",
                ("cost-two-a", 2), ("cost-two-b", 2), ("cost-one", 1))));

        var mixedData = LargeFieldConstraintData("one-resource-two-field-legions",
            ("field-a", 2), ("field-b", 1));
        mixedData["resource-a:resourceType"] = "morale";
        mixedData["resource-a:activityState"] = "active";
        mixedData["resource-b:resourceType"] = "temporary-morale";
        mixedData["resource-b:activityState"] = "active";
        Assert.Equal(new[] { "resource-a", "field-a", "field-b" }, LargePromptSelectionForTest(
            "prompt-one-resource", ["resource-a", "resource-b", "field-a", "field-b"], 3, 3,
            mixedData));

        mixedData["selectionConstraint"] = "zero-resource-two-field-legions";
        Assert.Equal(new[] { "field-a", "field-b" }, LargePromptSelectionForTest(
            "prompt-zero-resource", ["resource-a", "field-a", "field-b"], 2, 2, mixedData));

        Assert.Equal(new[] { "prefiltered" }, LargePromptSelectionForTest(
            "prompt-taiyangcheng", ["prefiltered"], 1, 1,
            new Dictionary<string, string>
            {
                ["action"] = "horus-entry",
                ["selectionConstraint"] = "taiyangcheng",
            }));

        var unknown = Assert.Throws<Xunit.Sdk.XunitException>(() => LargePromptSelectionForTest(
            "prompt-future-constraint", ["choice-a"], 1, 1,
            new Dictionary<string, string>
            {
                ["action"] = "future-combination-action",
                ["selectionConstraint"] = "future-combination",
            }));
        Assert.Contains("future-combination", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("future-combination-action", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("prompt-future-constraint", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("min=1", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeStateSelectorKeepsZeroChoiceSkipAndCancelSemanticsSeparate()
    {
        var controls = new Dictionary<string, string>
        {
            ["action"] = "control-proof",
            ["allowCancel"] = "true",
            ["skip"] = "取消整次发动",
            ["cancel"] = "取消打出",
        };
        Assert.Empty(LargePromptSelectionForTest(
            "prompt-zero", ["skip", "cancel", "material"], 0, 2, controls, "activation-zero"));
        Assert.Equal(new[] { "material-a", "material-b" }, LargePromptSelectionForTest(
            "prompt-material", ["skip", "cancel", "material-a", "material-b"], 2, 2,
            controls, "activation-material"));
        Assert.Equal(new[] { "skip" }, LargePromptSelectionForTest(
            "prompt-skip", ["skip", "material"], 2, 2, controls, "activation-skip"));
        Assert.Equal(new[] { "cancel" }, LargePromptSelectionForTest(
            "prompt-cancel", ["cancel", "material"], 2, 2, controls));

        var movePayment = new Dictionary<string, string>
        {
            ["action"] = "move-payment-proof",
            ["choiceMode"] = "resource-payment",
            ["cardInstanceId"] = "moving-legion",
        };
        Assert.Equal(new[] { "morale" }, LargePromptSelectionForTest(
            "prompt-move-payment", ["moving-legion", "morale"], 1, 1, movePayment));
    }

    [Fact]
    public void LargeStateSelectorExcludesDefaultDeckLegionMatchingVisibleFlippedLibraryTop()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            spendableResourceCount = 8,
            // Both cards are in the default 高天原 S1 preset. libraryTop is exposed only while
            // S02-DS01 is active, when the product forbids this matching-profession hand play.
            libraryTop = new { cardId = "S01-0410", profession = "弓手" },
            hand = new object[]
            {
                new { instanceId = "matching", cardId = "S01-0416", cardType = "legion",
                    profession = "弓手", playCost = 1, playBlockedReason = (string?)null },
                new { instanceId = "other", cardId = "S01-0415", cardType = "legion",
                    profession = "刺客", playCost = 0, playBlockedReason = (string?)null },
            },
        }, Wire));
        var own = document.RootElement;
        var hand = own.GetProperty("hand").EnumerateArray().ToArray();
        Assert.False(IsLargeStateLegionCandidate(own, hand[0], 8));
        Assert.True(IsLargeStateLegionCandidate(own, hand[1], 0));
    }

    [Fact]
    public async Task TenNaturalFriendlyRoomsReachBothLargeActiveViewsBeforeAnyPerformanceTiming()
    {
        // This is a preparation feasibility gate, not a ten-room latency/capacity claim.
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var identity = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(Path.Combine(
            Path.GetDirectoryName(identity.Path)!, "ten-room-preparation.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog, null, null, null, null, null, null, null);
        var peers = new List<SocketPeer>();
        var startedRooms = new List<StartedLargeRoom>();
        var grownRooms = new List<GrownLargeRoom>();
        var serverStarted = false;
        try
        {
            await server.StartAsync(0).WaitAsync(SetupDeadline);
            serverStarted = true;
            var endpoint = new UriBuilder(Assert.Single(server.Addresses))
                { Host = "127.0.0.1", Scheme = "ws", Path = "/ws" }.Uri;
            for (var slot = 0; slot < 10; slot++)
            {
                budget.Token.ThrowIfCancellationRequested();
                var actor = identity.Store.Register($"pa{Guid.NewGuid():N}"[..10], "Password123!");
                var opponent = identity.Store.Register($"pb{Guid.NewGuid():N}"[..10], "Password123!");
                Assert.True(actor.Success);
                Assert.True(opponent.Success);
                peers.Add(await SocketPeer.ConnectAsync(endpoint, actor.Token!, budget.Token));
                peers.Add(await SocketPeer.ConnectAsync(endpoint, opponent.Token!, budget.Token));
                startedRooms.Add(await StartLargeFriendlyGameAsync(
                    peers[slot * 2], peers[slot * 2 + 1], budget.Token, (0, 1)));
            }
            Assert.Equal(10, startedRooms.Select(room => room.MatchId).Distinct().Count());
            for (var slot = 0; slot < 10; slot++)
            {
                var grown = await GrowLargeFriendlyRoomAsync(
                    peers[slot * 2], peers[slot * 2 + 1], startedRooms[slot], budget.Token);
                grownRooms.Add(grown);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    stage = "legal-preparation", slot, acceptedCommands = grown.AcceptedCommands,
                    commandTypes = grown.CommandTypes, logicalViewBytes = grown.PreTerminalBytes,
                    presetIndexes = new[] { 0, 1 }, performanceTimingStarted = false,
                }));
            }

            // Flush queued frames before the final all-room barrier. In particular a timed
            // response must not let an older, larger preparation view stand in for current state.
            for (var slot = 0; slot < 10; slot++)
            {
                var actor = peers[slot * 2];
                var opponent = peers[slot * 2 + 1];
                await actor.SendAsync(new { type = "ping" }, budget.Token);
                await opponent.SendAsync(new { type = "ping" }, budget.Token);
                await Task.WhenAll(actor.ReceiveTypeAsync("pong", SetupDeadline, budget.Token),
                    opponent.ReceiveTypeAsync("pong", SetupDeadline, budget.Token));
                var boundPeers = new SocketPeer[2];
                var latest = new ReceivedFrame[2];
                BindPlayerFrame(actor, startedRooms[slot].Actor, boundPeers, latest);
                BindPlayerFrame(opponent, startedRooms[slot].Opponent, boundPeers, latest);
                foreach (var peer in new[] { actor, opponent })
                    BindPlayerFrame(peer, peer.Received.Last(frame =>
                        ReadString(frame.Payload, "type") == "gameState"), boundPeers, latest);
                var checkedRoom = CompleteLargeRoomPreparation(latest, startedRooms[slot].MatchId,
                    startedRooms[slot].RoomCode,
                    grownRooms[slot].AcceptedCommands, grownRooms[slot].CommandTypes);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    stage = "all-room-barrier", slot, logicalViewBytes = checkedRoom.PreTerminalBytes,
                    revision = ReadInt64(latest[0].Payload.GetProperty("state"), "revision"),
                    performanceTimingStarted = false,
                }));
            }
            output.WriteLine("ten-room legal preparation barrier passed; no terminal/performance workload measured");
        }
        finally
        {
            foreach (var peer in peers) await peer.DisposeAsync();
            if (serverStarted) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

    [Theory]
    [InlineData(180 * 1024 - 1, "Main")]
    [InlineData(300 * 1024 + 1, "Main")]
    [InlineData(180 * 1024, "GameOver")]
    public void LargePreparationCannotSucceedAfterCommandBudgetWithUndersizeOrEndedView(
        int secondViewBytes, string secondViewPhase)
    {
        // Synthetic boundary-only frames: these declared sizes are not a workload proof.
        var frames = new[]
        {
            PreparationBoundaryFrame(0, PreferredPreTerminalBytes, "Main"),
            PreparationBoundaryFrame(1, secondViewBytes, secondViewPhase),
        };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CompleteLargeRoomPreparation(
            frames, "boundary-match", "boundary-room", MaximumAcceptedLargeStateCommands,
            new Dictionary<string, int>()));
    }

    [Theory]
    [InlineData("other-match", "boundary-room", 1, 7)]
    [InlineData("boundary-match", "other-room", 1, 7)]
    [InlineData("boundary-match", "boundary-room", 0, 7)]
    [InlineData("boundary-match", "boundary-room", 1, 8)]
    public void LargePreparationRejectsMixedMatchRoomSeatOrRevision(
        string matchId, string roomCode, int secondSeat, long revision)
    {
        var frames = new[]
        {
            PreparationBoundaryFrame(0, PreferredPreTerminalBytes, "Main"),
            PreparationBoundaryFrame(secondSeat, PreferredPreTerminalBytes, "Main", matchId, roomCode, revision),
        };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CompleteLargeRoomPreparation(
            frames, "boundary-match", "boundary-room", MaximumAcceptedLargeStateCommands,
            new Dictionary<string, int>()));
    }

    [Theory]
    [InlineData(180 * 1024)]
    [InlineData(300 * 1024)]
    public void LargePreparationAcceptsExactBoundsEvenOnTheLastAllowedCommand(int bytes)
    {
        var frames = new[]
        {
            PreparationBoundaryFrame(0, bytes, "Main"),
            PreparationBoundaryFrame(1, bytes, "Main"),
        };
        var grown = CompleteLargeRoomPreparation(frames, "boundary-match", "boundary-room",
            MaximumAcceptedLargeStateCommands, new Dictionary<string, int>());
        Assert.Equal(new[] { bytes, bytes }, grown.PreTerminalBytes);
    }

    [Fact]
    public async Task LargePreparationRejectsSwappedSocketSeatsInsteadOfSortingThemAway()
    {
        await using var actor = new SocketPeer();
        await using var opponent = new SocketPeer();
        var boundPeers = new SocketPeer[2];
        var latest = new ReceivedFrame[2];
        BindPlayerFrame(actor, PreparationBoundaryFrame(0, PreferredPreTerminalBytes, "Main"), boundPeers, latest);
        BindPlayerFrame(opponent, PreparationBoundaryFrame(1, PreferredPreTerminalBytes, "Main"), boundPeers, latest);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => BindPlayerFrame(
            actor, PreparationBoundaryFrame(1, PreferredPreTerminalBytes, "Main"), boundPeers, latest));
    }

    private static ReceivedFrame PreparationBoundaryFrame(int playerIndex, int bytes, string phase,
        string matchId = "boundary-match", string roomCode = "boundary-room", long revision = 7)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "gameState",
            state = new { matchId, roomCode, you = playerIndex, revision, phase, players = new object[] { new { }, new { } } },
        }, Wire));
        return new(document.RootElement.Clone(), Stopwatch.GetTimestamp(), bytes);
    }

    private static GrownLargeRoom CompleteLargeRoomPreparation(
        IReadOnlyList<ReceivedFrame> frames, string matchId, string roomCode, int acceptedCommands,
        IReadOnlyDictionary<string, int> commandTypes)
    {
        Assert.Equal(2, frames.Count);
        Assert.InRange(acceptedCommands, 0, MaximumAcceptedLargeStateCommands);
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            Assert.Equal("gameState", ReadString(frame.Payload, "type"));
            var state = frame.Payload.GetProperty("state");
            Assert.Equal(matchId, ReadString(state, "matchId"));
            Assert.Equal(roomCode, ReadString(state, "roomCode"));
            Assert.Equal(index, ReadInt32(state, "you"));
            Assert.Equal(2, state.GetProperty("players").GetArrayLength());
            Assert.False(string.IsNullOrWhiteSpace(ReadString(state, "phase")));
            Assert.False(IsGameOver(frame.Payload),
                $"preparation ended the game after {acceptedCommands} commands");
            Assert.True(frame.RawBytes >= PreferredPreTerminalBytes && frame.RawBytes <= MaximumTerminalBytes,
                $"preparation incomplete after {acceptedCommands}/{MaximumAcceptedLargeStateCommands} commands: " +
                $"player {index} view={frame.RawBytes}, required={PreferredPreTerminalBytes}..{MaximumTerminalBytes}");
        }
        Assert.Equal(ReadInt64(frames[0].Payload.GetProperty("state"), "revision"),
            ReadInt64(frames[1].Payload.GetProperty("state"), "revision"));
        return new(acceptedCommands, commandTypes, frames.Select(frame => frame.RawBytes).ToArray());
    }

    private static async Task<GrownLargeRoom> GrowLargeFriendlyRoomAsync(
        SocketPeer actor, SocketPeer opponent, StartedLargeRoom started,
        CancellationToken cancellationToken = default)
    {
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
            cancellationToken.ThrowIfCancellationRequested();
            Assert.DoesNotContain(latestByPlayer,
                frame => IsGameOver(frame.Payload));
            var selected = SelectLargeStateCommand(latestByPlayer, playedTurns, selector);
            var sender = peersByPlayer[selected.PlayerIndex];
            var other = peersByPlayer[1 - selected.PlayerIndex];
            var revisionBefore = ReadInt64(
                latestByPlayer[selected.PlayerIndex].Payload.GetProperty("state"), "revision");
            var requestId = $"large-state-{acceptedCommands:D3}-{Guid.NewGuid():N}";
            using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var confirmationTask = sender.ReceiveAsync(
                payload => ReadString(payload, "requestId") == requestId, SetupDeadline, exchange.Token);
            var peerTask = other.ReceiveAsync(
                payload => IsAdvancedGameState(payload, revisionBefore), SetupDeadline, exchange.Token);
            ReceivedFrame confirmation;
            ReceivedFrame peerFrame;
            try
            {
                await sender.SendAsync(new
                {
                    type = "gameAction", requestId, command = selected.Command,
                }, exchange.Token);
                confirmation = await confirmationTask;
                if (ReadString(confirmation.Payload, "type") != "gameState")
                {
                    // Synthetic failure context only; no extra successful-path serialization.
                    var inputState = latestByPlayer[selected.PlayerIndex].Payload.GetProperty("state");
                    var handContext = selected.Type == "playCard"
                        ? PlayerView(inputState, selected.PlayerIndex).GetProperty("hand").GetRawText()
                        : "<not-playCard>";
                    Assert.Fail($"large-state preparation rejected command {acceptedCommands} ({selected.Type}) " +
                        $"at revision {revisionBefore}; command={JsonSerializer.Serialize(selected.Command, Wire)}; " +
                        $"phase={ReadString(inputState, "phase")}; prompts={inputState.GetProperty("prompts").GetRawText()}; " +
                        $"hand={handContext}; reply={confirmation.Payload.GetRawText()}");
                }
                peerFrame = await peerTask;
            }
            catch
            {
                exchange.Cancel();
                // Drain both receives before the caller tears down sockets. Preserve the original failure.
                try { await Task.WhenAll(confirmationTask, peerTask); } catch { }
                throw;
            }
            Assert.Equal("gameState", ReadString(confirmation.Payload, "type"));
            Assert.True(IsAdvancedGameState(confirmation.Payload, revisionBefore),
                $"accepted {selected.Type} did not advance the authoritative revision");
            BindPlayerFrame(sender, confirmation, peersByPlayer, latestByPlayer);
            BindPlayerFrame(other, peerFrame, peersByPlayer, latestByPlayer);
            Assert.Equal(
                ReadInt64(latestByPlayer[0].Payload.GetProperty("state"), "revision"),
                ReadInt64(latestByPlayer[1].Payload.GetProperty("state"), "revision"));
            if (selected.TurnKey is not null) playedTurns.Add(selected.TurnKey);
            commandTypes[selected.Type] = commandTypes.GetValueOrDefault(selected.Type) + 1;
            acceptedCommands++;
        }

        return CompleteLargeRoomPreparation(latestByPlayer, started.MatchId, started.RoomCode,
            acceptedCommands, commandTypes);
    }

    private static async Task<StartedLargeRoom> StartLargeFriendlyGameAsync(
        SocketPeer actor, SocketPeer opponent, CancellationToken cancellationToken = default,
        (int Actor, int Opponent)? presetIndexes = null)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = exchange.Token;
        var receives = new List<Task<ReceivedFrame>>();
        Task<ReceivedFrame> Receive(SocketPeer peer, string type)
        {
            var pending = peer.ReceiveTypeAsync(type, SetupDeadline, cancellationToken);
            receives.Add(pending);
            return pending;
        }
        try
        {
        await actor.SendAsync(new
        {
            type = "createRoom",
            options = new { matchModeId = "friendly" },
        }, cancellationToken);
        var created = await Receive(actor, "roomState");
        var roomCode = ReadString(created.Payload, "roomCode");
        Assert.False(string.IsNullOrWhiteSpace(roomCode));

        var actorJoined = Receive(actor, "roomState");
        var opponentJoined = Receive(opponent, "roomState");
        await opponent.SendAsync(new { type = "joinRoom", roomCode }, cancellationToken);
        await Task.WhenAll(actorJoined, opponentJoined);

        if (presetIndexes is { } presets)
        {
            var actorSeat = ReadInt32(actorJoined.Result.Payload, "yourPlayerIndex");
            var opponentSeat = ReadInt32(opponentJoined.Result.Payload, "yourPlayerIndex");
            Assert.InRange(actorSeat, 0, 1);
            Assert.InRange(opponentSeat, 0, 1);
            Assert.NotEqual(actorSeat, opponentSeat);
            var selectedIndexes = new Dictionary<int, int>();
            foreach (var (peer, seat, deckIndex) in new[]
            {
                (actor, actorSeat, presets.Actor),
                (opponent, opponentSeat, presets.Opponent),
            })
            {
                var actorSelected = Receive(actor, "roomState");
                var opponentSelected = Receive(opponent, "roomState");
                await peer.SendAsync(new { type = "selectDeck", deckIndex }, cancellationToken);
                var selected = await Task.WhenAll(actorSelected, opponentSelected);
                selectedIndexes[seat] = deckIndex;
                foreach (var frame in selected)
                {
                    Assert.Equal(roomCode, ReadString(frame.Payload, "roomCode"));
                    foreach (var (selectedSeat, selectedIndex) in selectedIndexes)
                    {
                        var member = frame.Payload.GetProperty("players").EnumerateArray()
                            .Single(player => ReadInt32(player, "playerIndex") == selectedSeat);
                        Assert.Equal(selectedIndex, ReadInt32(member, "deckIndex"));
                    }
                }
            }
        }

        var actorReady = Receive(actor, "roomState");
        var opponentSeesReady = Receive(opponent, "roomState");
        await actor.SendAsync(new { type = "ready", ready = true }, cancellationToken);
        await Task.WhenAll(actorReady, opponentSeesReady);

        var actorStarted = Receive(actor, "gameState");
        var opponentStarted = Receive(opponent, "gameState");
        await opponent.SendAsync(new { type = "ready", ready = true }, cancellationToken);
        var started = await Task.WhenAll(actorStarted, opponentStarted);
        Assert.All(started, frame => Assert.False(IsGameOver(frame.Payload)));
        var actorMatchId = ReadString(started[0].Payload.GetProperty("state"), "matchId");
        var opponentMatchId = ReadString(started[1].Payload.GetProperty("state"), "matchId");
        Assert.False(string.IsNullOrWhiteSpace(actorMatchId));
        Assert.Equal(actorMatchId, opponentMatchId);
        return new(roomCode!, actorMatchId!, started[0], started[1]);
        }
        catch
        {
            exchange.Cancel();
            try { await Task.WhenAll(receives); } catch { }
            throw;
        }
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
            return new(playerIndex, "resolvePrompt", BuildLargePromptCommand(prompt), null);
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
                    var candidates = own.GetProperty("hand").EnumerateArray()
                        .Where(card => IsLargeStateLegionCandidate(own, card, budget)).ToArray();
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

    private static bool IsLargeStateLegionCandidate(JsonElement own, JsonElement card, int budget)
    {
        if (!string.Equals(ReadString(card, "cardType"), "legion", StringComparison.OrdinalIgnoreCase)
            || !card.TryGetProperty("instanceId", out var instance)
            || instance.ValueKind != JsonValueKind.String
            || !card.TryGetProperty("playCost", out var cost)
            || cost.ValueKind != JsonValueKind.Number
            || !cost.TryGetInt32(out var playCost)
            || playCost < 0 || playCost > budget
            || (card.TryGetProperty("playBlockedReason", out var blocked)
                && blocked.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                && !string.IsNullOrWhiteSpace(blocked.GetString())))
            return false;

        // The server exposes libraryTop only for S02-DS01. Its dynamic same-profession
        // restriction is not folded into the static hand PlayBlockedReason snapshot.
        return !own.TryGetProperty("libraryTop", out var libraryTop)
            || libraryTop.ValueKind != JsonValueKind.Object
            || string.IsNullOrWhiteSpace(ReadString(card, "profession"))
            || !string.Equals(ReadString(card, "profession"), ReadString(libraryTop, "profession"),
                StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> BuildLargePromptCommand(JsonElement prompt)
    {
        var validChoices = prompt.GetProperty("validChoices").EnumerateArray()
            .Select(choice => choice.GetString())
            .Where(choice => !string.IsNullOrWhiteSpace(choice))
            .Cast<string>().ToArray();
        Assert.Equal(validChoices.Length, validChoices.Distinct(StringComparer.Ordinal).Count());
        var minimum = ReadInt32(prompt, "minChoose");
        var maximum = ReadInt32(prompt, "maxChoose");
        Assert.InRange(minimum, 0, validChoices.Length);
        Assert.InRange(maximum, minimum, validChoices.Length);

        var data = default(JsonElement);
        string? placementMode = null;
        string? placementAction = null;
        if (prompt.TryGetProperty("data", out var promptData) && promptData.ValueKind == JsonValueKind.Object)
        {
            data = promptData;
            placementAction = ReadString(data, "action");
            if (data.TryGetProperty("placementMode", out var placement)
                && placement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                placementMode = placement.ValueKind == JsonValueKind.String
                    ? placement.GetString()
                    : $"<{placement.ValueKind}>";
                if (string.IsNullOrWhiteSpace(placementMode)) placementMode = "<empty>";
            }
        }

        var selected = SelectLargePromptChoices(prompt, data, validChoices, minimum, maximum);
        var command = new Dictionary<string, object?>
        {
            ["type"] = "resolvePrompt",
            ["promptId"] = ReadString(prompt, "promptId"),
        };
        switch (placementMode)
        {
            case null:
                command["cardInstanceIds"] = selected;
                break;
            case "single-top-bottom":
                Assert.Equal(1, minimum);
                command["cardInstanceIds"] = selected;
                command["destination"] = "bottom";
                break;
            case "split-top-bottom":
            case "all-top-bottom":
                Assert.Equal(validChoices.Length, minimum);
                Assert.Equal(validChoices.Length, maximum);
                command["topCardInstanceIds"] = selected;
                command["bottomCardInstanceIds"] = Array.Empty<string>();
                break;
            case "all-bottom":
                Assert.Equal(validChoices.Length, minimum);
                Assert.Equal(validChoices.Length, maximum);
                command["topCardInstanceIds"] = Array.Empty<string>();
                command["bottomCardInstanceIds"] = selected;
                break;
            default:
                throw new Xunit.Sdk.XunitException(
                    $"large-state legal selector reached unsupported authoritative placementMode " +
                    $"'{placementMode}' for action '{placementAction ?? "<missing>"}'");
        }
        CopyPromptBinding(prompt, command, "activationId");
        CopyPromptBinding(prompt, command, "sourceInstanceId");
        CopyPromptBinding(prompt, command, "sourceCardId");
        CopyPromptBinding(prompt, command, "step");
        CopyPromptBinding(prompt, command, "createdRevision");
        CopyPromptBinding(prompt, command, "controller");
        return command;
    }

    private static string[] SelectLargePromptChoices(JsonElement prompt, JsonElement data,
        string[] validChoices, int minimum, int maximum)
    {
        var constraint = data.ValueKind == JsonValueKind.Object
            ? ReadString(data, "selectionConstraint") : null;
        var action = data.ValueKind == JsonValueKind.Object ? ReadString(data, "action") : null;
        var promptId = ReadString(prompt, "promptId") ?? "<missing>";
        var context = $"promptId='{promptId}', action='{action ?? "<missing>"}', " +
            $"constraint='{constraint ?? "<none>"}', min={minimum}, max={maximum}, " +
            $"choices=[{string.Join(',', validChoices.Take(12))}]";
        if (!string.IsNullOrWhiteSpace(constraint)
            && constraint is not ("distinct-card-names" or "grave-faction-exact"
                or "hijikata-entry-targets" or "one-resource-two-field-legions"
                or "zero-resource-two-field-legions" or "taiyangcheng"))
            throw new Xunit.Sdk.XunitException(
                $"large-state legal selector reached unsupported authoritative selectionConstraint; {context}");

        var hasBoundSkip = prompt.TryGetProperty("activationId", out var activation)
            && activation.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(activation.GetString())
            && validChoices.Contains("skip", StringComparer.OrdinalIgnoreCase);
        var hasExplicitCancel = data.ValueKind == JsonValueKind.Object
            && string.Equals(ReadString(data, "allowCancel"), "true", StringComparison.Ordinal)
            && validChoices.Contains("cancel", StringComparer.OrdinalIgnoreCase);
        if (minimum == 0) return [];

        var resourcePayment = data.ValueKind == JsonValueKind.Object
            && string.Equals(ReadString(data, "choiceMode"), "resource-payment", StringComparison.Ordinal);
        var excludedSource = resourcePayment ? ReadString(data, "cardInstanceId") : null;
        var materialChoices = validChoices
            .Where(choice => !(hasBoundSkip && choice.Equals("skip", StringComparison.OrdinalIgnoreCase)))
            .Where(choice => !(hasExplicitCancel && choice.Equals("cancel", StringComparison.OrdinalIgnoreCase)))
            // Restored move-payment prompts may still expose the moving legion itself.
            .Where(choice => string.IsNullOrWhiteSpace(excludedSource)
                || !choice.Equals(excludedSource, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (materialChoices.Length < minimum)
        {
            if (hasBoundSkip) return ["skip"];
            if (hasExplicitCancel) return ["cancel"];
            throw new Xunit.Sdk.XunitException(
                $"large-state legal selector has fewer material choices than required; {context}");
        }

        if (string.IsNullOrWhiteSpace(constraint) || constraint == "taiyangcheng")
            return materialChoices.Take(minimum).ToArray();

        return FindLargePromptCombination(materialChoices, minimum,
            selected => LargePromptCombinationIsAccepted(constraint, data, selected), context);
    }

    private static string[] FindLargePromptCombination(string[] candidates, int count,
        Func<IReadOnlyList<string>, bool> accepts, string context)
    {
        const int maximumChecks = 4096;
        var buffer = new string[count];
        string[]? result = null;
        var checks = 0;
        bool Search(int start, int depth)
        {
            if (depth == count)
            {
                if (++checks > maximumChecks)
                    throw new Xunit.Sdk.XunitException(
                        $"large-state legal selector exceeded {maximumChecks} bounded combination checks; {context}");
                if (!accepts(buffer)) return false;
                result = buffer.ToArray();
                return true;
            }
            var remaining = count - depth;
            for (var index = start; index <= candidates.Length - remaining; index++)
            {
                buffer[depth] = candidates[index];
                if (Search(index + 1, depth + 1)) return true;
            }
            return false;
        }

        if (Search(0, 0)) return result!;
        throw new Xunit.Sdk.XunitException(
            $"large-state legal selector found no product-legal minimum-size combination; {context}");
    }

    private static bool LargePromptCombinationIsAccepted(string constraint, JsonElement data,
        IReadOnlyList<string> selected)
    {
        string? CardData(string id, string property) => ReadString(data, $"{id}:{property}");
        bool IsFieldLegion(string id) =>
            string.Equals(CardData(id, "zone"), "战场", StringComparison.Ordinal)
            && string.Equals(CardData(id, "cardType"), "legion", StringComparison.OrdinalIgnoreCase);
        bool IsActiveResource(string id) =>
            !string.IsNullOrWhiteSpace(CardData(id, "resourceType"))
            && string.Equals(CardData(id, "activityState"), "active", StringComparison.Ordinal);

        switch (constraint)
        {
            case "distinct-card-names":
            {
                var names = selected.Select(id => CardData(id, "name") ?? ReadString(data, id)).ToArray();
                return names.All(name => !string.IsNullOrWhiteSpace(name))
                    && names.Distinct(StringComparer.Ordinal).Count() == names.Length;
            }
            case "grave-faction-exact":
            {
                if (!int.TryParse(ReadString(data, "representedCount"), out var represented)
                    || represented <= 0 || selected.Count == 0 || selected.Count > represented)
                    return false;
                var faction = ReadString(data, "factionConstraint") ?? string.Empty;
                var legionOnly = string.Equals(ReadString(data, "legionCardsOnly"), "true",
                    StringComparison.Ordinal);
                if (selected.Any(id => !string.Equals(CardData(id, "zone"), "墓地", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(CardData(id, "cardId"))
                    || (legionOnly && !string.Equals(CardData(id, "cardType"), "legion",
                        StringComparison.OrdinalIgnoreCase))))
                    return false;
                return selected.Sum(id =>
                    string.Equals(CardData(id, "cardId"), "ST03-08", StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(faction)
                        || faction.Equals("asgard", StringComparison.OrdinalIgnoreCase)) ? 3 : 1) >= represented;
            }
            case "hijikata-entry-targets":
            {
                if (selected.Count != 2) return true;
                var costs = selected.Select(id => int.TryParse(CardData(id, "cost"), out var cost)
                    ? cost : int.MaxValue).ToArray();
                return selected.All(IsFieldLegion) && costs.All(cost => cost <= 2)
                    && costs.Any(cost => cost <= 1);
            }
            case "one-resource-two-field-legions":
            case "zero-resource-two-field-legions":
            {
                var requiredResources = constraint == "one-resource-two-field-legions" ? 1 : 0;
                return selected.Count(IsActiveResource) == requiredResources
                    && selected.Count(IsFieldLegion) == 2;
            }
            default:
                return false;
        }
    }

    private static string[] LargePromptSelectionForTest(string promptId, string[] validChoices,
        int minimum, int maximum, Dictionary<string, string> data, string? activationId = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["promptId"] = promptId,
            ["playerIndex"] = 0,
            ["validChoices"] = validChoices,
            ["minChoose"] = minimum,
            ["maxChoose"] = maximum,
            ["data"] = data,
        };
        if (!string.IsNullOrWhiteSpace(activationId)) payload["activationId"] = activationId;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, Wire));
        var command = BuildLargePromptCommand(document.RootElement);
        return Assert.IsType<string[]>(command["cardInstanceIds"]);
    }

    private static Dictionary<string, string> LargeFieldConstraintData(string constraint,
        params (string Id, int Cost)[] cards)
    {
        var data = new Dictionary<string, string>
        {
            ["action"] = "field-constraint-proof",
            ["selectionConstraint"] = constraint,
        };
        foreach (var card in cards)
        {
            data[$"{card.Id}:cardType"] = "legion";
            data[$"{card.Id}:zone"] = "战场";
            data[$"{card.Id}:cost"] = card.Cost.ToString();
        }
        return data;
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

    private sealed record StartedLargeRoom(string RoomCode, string MatchId,
        ReceivedFrame Actor, ReceivedFrame Opponent);
    private sealed record GrownLargeRoom(int AcceptedCommands,
        IReadOnlyDictionary<string, int> CommandTypes, int[] PreTerminalBytes);
    private sealed record OutboundTimeline(long EnqueuedAt, long DequeuedAt, long GateAcquiredAt);
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

        internal static async Task<SocketPeer> ConnectAsync(Uri endpoint, string token,
            CancellationToken cancellationToken = default)
        {
            var peer = new SocketPeer();
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(SetupDeadline);
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
                }, cancellationToken);
                var session = await peer.ReceiveTypeAsync("session", SetupDeadline, cancellationToken);
                peer.SessionId = session.Payload.GetProperty("sessionId").GetGuid();
                _ = await peer.ReceiveTypeAsync("recoveryComplete", SetupDeadline, cancellationToken);
                return peer;
            }
            catch
            {
                peer._socket.Dispose();
                throw;
            }
        }

        internal async Task SendAsync(object payload, CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(SetupDeadline);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Wire);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, deadline.Token);
        }

        internal Task<ReceivedFrame> ReceiveTypeAsync(string type, TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            ReceiveAsync(payload => ReadString(payload, "type") == type, timeout, cancellationToken);

        internal async Task<ReceivedFrame> ReceiveAsync(
            Func<JsonElement, bool> predicate, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
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
