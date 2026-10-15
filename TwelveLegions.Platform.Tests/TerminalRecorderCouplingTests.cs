using System.Collections;
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
public sealed class TerminalRecorderCouplingTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ThreeSeconds = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SetupDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BarrierLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HealthyWhileHeldDeadline = TimeSpan.FromSeconds(1);

    [Theory]
    [InlineData("before-match-command-commit")]
    [InlineData("before-match-complete-commit")]
    public async Task GlobalRecorderGateDelaysAnotherRoomsTerminalDeliveryButNotHealthyPing(
        string heldStage)
    {
        using var identity = new RankedIdentityFixture();
        var a0Token = Register(identity, "ra");
        var a1Token = Register(identity, "rb");
        var b0Token = Register(identity, "rc");
        var b1Token = Register(identity, "rd");
        var healthyToken = Register(identity, "rh");
        var databasePath = Path.Combine(Path.GetDirectoryName(identity.Path)!,
            $"terminal-recorder-coupling-{heldStage}.db");

        await using var recorder = new MatchRecorder(databasePath);
        recorder.AttachCatalog(identity.Catalog);
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(rooms, recorder, identity.Store,
            identity.Catalog);

        SocketPeer? aActor = null;
        SocketPeer? aOpponent = null;
        SocketPeer? bActor = null;
        SocketPeer? bOpponent = null;
        SocketPeer? healthy = null;
        using var barrierEntered = new ManualResetEventSlim();
        using var barrierRelease = new ManualResetEventSlim();
        using var barrierExited = new ManualResetEventSlim();
        var stageObservations = new ConcurrentQueue<StageObservation>();
        var stageOrdinal = 0;
        var observationStartedAt = Stopwatch.GetTimestamp();
        var barrierClaimed = 0;
        long barrierEnteredAt = 0;
        long barrierExitedAt = 0;
        var serverStarted = false;

        try
        {
            await server.StartAsync(0).WaitAsync(SetupDeadline);
            serverStarted = true;
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            aActor = await SocketPeer.ConnectAsync(endpoint, a0Token);
            aOpponent = await SocketPeer.ConnectAsync(endpoint, a1Token);
            bActor = await SocketPeer.ConnectAsync(endpoint, b0Token);
            bOpponent = await SocketPeer.ConnectAsync(endpoint, b1Token);
            healthy = await SocketPeer.ConnectAsync(endpoint, healthyToken);

            var roomA = await StartFriendlyGameAsync(aActor, aOpponent);
            var roomB = await StartFriendlyGameAsync(bActor, bOpponent);
            await DrainToPongAsync(aActor, ThreeSeconds);
            await DrainToPongAsync(aOpponent, ThreeSeconds);
            await DrainToPongAsync(bActor, ThreeSeconds);
            await DrainToPongAsync(bOpponent, ThreeSeconds);
            await DrainToPongAsync(healthy, ThreeSeconds);

            var gateB = RuntimeGate(rooms, roomB.RoomCode);
            Assert.Equal(1, gateB.CurrentCount);
            recorder.StorageFailureInjector = stage =>
            {
                var stageKind = stage switch
                {
                    "before-match-command-commit" => 1,
                    "before-match-complete-commit" => 2,
                    _ => 0,
                };
                if (stageKind != 0)
                    stageObservations.Enqueue(new StageObservation(
                        Interlocked.Increment(ref stageOrdinal), stageKind,
                        ElapsedMicroseconds(observationStartedAt, Stopwatch.GetTimestamp())));
                if (!string.Equals(stage, heldStage, StringComparison.Ordinal)
                    || Interlocked.CompareExchange(ref barrierClaimed, 1, 0) != 0)
                    return;
                Interlocked.Exchange(ref barrierEnteredAt, Stopwatch.GetTimestamp());
                barrierEntered.Set();
                try
                {
                    if (!barrierRelease.Wait(BarrierLimit))
                        throw new TimeoutException("bounded terminal recorder coupling barrier timed out");
                }
                finally
                {
                    Interlocked.Exchange(ref barrierExitedAt, Stopwatch.GetTimestamp());
                    barrierExited.Set();
                }
            };

            var requestA = $"terminal-recorder-a-{Guid.NewGuid():N}";
            var aActorTerminalTask = aActor.ReceiveAsync(
                payload => IsGameOver(payload) && ReadString(payload, "requestId") == requestA,
                ThreeSeconds);
            var aOpponentTerminalTask = aOpponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            var sentAAt = Stopwatch.GetTimestamp();
            await aActor.SendAsync(new
            {
                type = "gameAction",
                requestId = requestA,
                command = new { type = "surrender" },
            });
            Assert.True(barrierEntered.Wait(ThreeSeconds),
                $"room A did not enter recorder stage {heldStage}");

            var requestB = $"terminal-recorder-b-{Guid.NewGuid():N}";
            var bActorTerminalTask = bActor.ReceiveAsync(
                payload => IsGameOver(payload) && ReadString(payload, "requestId") == requestB,
                ThreeSeconds);
            var bOpponentTerminalTask = bOpponent.ReceiveAsync(IsGameOver, ThreeSeconds);
            var sentBAt = Stopwatch.GetTimestamp();
            await bActor.SendAsync(new
            {
                type = "gameAction",
                requestId = requestB,
                command = new { type = "surrender" },
            });

            Assert.True(await WaitForAsync(() => gateB.CurrentCount == 0,
                    TimeSpan.FromMilliseconds(750)),
                "room B never acquired its room gate while waiting on the shared recorder");
            await Task.Delay(100);
            Assert.False(barrierExited.IsSet, "recorder barrier released before the isolation observation");
            Assert.Equal(0, gateB.CurrentCount);
            Assert.False(aActorTerminalTask.IsCompleted,
                "room A terminal frame was visible while its recorder commit was held");
            Assert.False(aOpponentTerminalTask.IsCompleted,
                "room A opponent terminal frame was visible while its recorder commit was held");
            Assert.False(bActorTerminalTask.IsCompleted,
                "room B terminal frame was delivered while the shared recorder stage remained held");
            Assert.False(bOpponentTerminalTask.IsCompleted,
                "room B opponent terminal frame was delivered while the shared recorder stage remained held");

            await DrainToPongAsync(healthy, HealthyWhileHeldDeadline);
            Assert.False(barrierExited.IsSet, "healthy ping only completed after the recorder barrier left");
            Assert.Equal(0, gateB.CurrentCount);

            barrierRelease.Set();
            Assert.True(barrierExited.Wait(TimeSpan.FromSeconds(1)),
                "recorder barrier did not observe the bounded release");

            var aActorTerminal = await aActorTerminalTask;
            var aOpponentTerminal = await aOpponentTerminalTask;
            var bActorTerminal = await bActorTerminalTask;
            var bOpponentTerminal = await bOpponentTerminalTask;
            AssertWithinOriginalSlo(sentAAt, aActorTerminal.ReceivedAt, "room A requester");
            AssertWithinOriginalSlo(sentAAt, aOpponentTerminal.ReceivedAt, "room A opponent");
            AssertWithinOriginalSlo(sentBAt, bActorTerminal.ReceivedAt, "room B requester");
            AssertWithinOriginalSlo(sentBAt, bOpponentTerminal.ReceivedAt, "room B opponent");

            await DrainToPongAsync(aActor, ThreeSeconds);
            await DrainToPongAsync(aOpponent, ThreeSeconds);
            await DrainToPongAsync(bActor, ThreeSeconds);
            await DrainToPongAsync(bOpponent, ThreeSeconds);
            Assert.Equal(1, aActor.Received.Count(frame => IsGameOver(frame.Payload)));
            Assert.Equal(1, aOpponent.Received.Count(frame => IsGameOver(frame.Payload)));
            Assert.Equal(1, bActor.Received.Count(frame => IsGameOver(frame.Payload)));
            Assert.Equal(1, bOpponent.Received.Count(frame => IsGameOver(frame.Payload)));

            await AssertDurableTerminalAsync(recorder, databasePath, roomA.MatchId, requestA);
            await AssertDurableTerminalAsync(recorder, databasePath, roomB.MatchId, requestB);

            var enteredAt = Interlocked.Read(ref barrierEnteredAt);
            var exitedAt = Interlocked.Read(ref barrierExitedAt);
            var heldMilliseconds = Stopwatch.GetElapsedTime(enteredAt, exitedAt).TotalMilliseconds;
            Assert.InRange(heldMilliseconds, 0, BarrierLimit.TotalMilliseconds);
            var orderedStages = stageObservations.OrderBy(item => item.Ordinal).ToArray();
            Assert.Equal(2, orderedStages.Count(item => item.Kind == 1));
            Assert.Equal(2, orderedStages.Count(item => item.Kind == 2));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                schema = 1,
                heldStage,
                stateScale = "fresh-friendly-room-small-state",
                largeStateEquivalent = false,
                heldMilliseconds,
                roomATerminalBytes = Encoding.UTF8.GetByteCount(aActorTerminal.Payload.GetRawText()),
                roomBTerminalBytes = Encoding.UTF8.GetByteCount(bActorTerminal.Payload.GetRawText()),
                roomBTerminalUndeliveredWhileRecorderHeld = true,
                healthyPingWhileHeld = true,
                exactTerminalFramesPerPeer = 1,
                checkpointRecoveryHashVerified = true,
                stageKindContract = new { append = 1, complete = 2 },
                stageCounts = new
                {
                    append = orderedStages.Count(item => item.Kind == 1),
                    complete = orderedStages.Count(item => item.Kind == 2),
                },
                observedStageSequence = orderedStages.Select(item => item.Kind).ToArray(),
                stageRelativeMicroseconds = orderedStages
                    .Select(item => item.RelativeMicroseconds).ToArray(),
            }));
        }
        finally
        {
            barrierRelease.Set();
            recorder.StorageFailureInjector = null;
            if (aActor is not null) await aActor.DisposeAsync();
            if (aOpponent is not null) await aOpponent.DisposeAsync();
            if (bActor is not null) await bActor.DisposeAsync();
            if (bOpponent is not null) await bOpponent.DisposeAsync();
            if (healthy is not null) await healthy.DisposeAsync();
            if (serverStarted) await server.StopAsync().WaitAsync(SetupDeadline);
        }
    }

    private static string Register(RankedIdentityFixture identity, string prefix)
    {
        var result = identity.Store.Register($"{prefix}{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        return result.Token!;
    }

    private static async Task<StartedRoom> StartFriendlyGameAsync(SocketPeer actor,
        SocketPeer opponent)
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
        return new StartedRoom(roomCode!, actorMatchId!);
    }

    private static async Task AssertDurableTerminalAsync(MatchRecorder recorder, string databasePath,
        string matchId, string requestId)
    {
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        var command = Assert.Single(detail.Commands);
        Assert.Equal("surrender", ReadString(command.Command, "type"));
        Assert.True(command.Accepted);
        Assert.Equal(0, command.PlayerIndex);
        Assert.NotNull(detail.Match.EndedUtc);
        Assert.NotNull(detail.Match.FinalHash);
        Assert.Equal(command.StateHash, detail.Match.FinalHash);

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(matchId));
        Assert.Equal(command.Sequence, recovered.CommandSequence);
        Assert.Equal(L12Phase.GameOver, recovered.Engine.State.Phase);
        Assert.Equal(detail.Match.FinalHash, recovered.Engine.ComputeStateHash());
        var processed = Assert.Single(recovered.ProcessedRequests);
        Assert.Equal(requestId, processed.RequestId);
        Assert.True(processed.Accepted);
        Assert.Equal(command.Revision, processed.Revision);

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
        Assert.Equal(command.Sequence, reader.GetInt64(0));
        Assert.Equal(detail.Match.FinalHash, reader.GetString(1));
        Assert.True(reader.GetInt64(2) > 0);
        Assert.False(await reader.ReadAsync());
    }

    private static SemaphoreSlim RuntimeGate(L12RoomManager manager, string roomCode)
    {
        var rooms = (IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in rooms)
        {
            var room = entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
            if (!string.Equals((string)room.GetType().GetProperty("Code")!.GetValue(room)!, roomCode,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            return (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        }
        throw new InvalidOperationException($"missing runtime room {roomCode}");
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private static async Task DrainToPongAsync(SocketPeer peer, TimeSpan timeout)
    {
        await peer.SendAsync(new { type = "ping" });
        _ = await peer.ReceiveTypeAsync("pong", timeout);
    }

    private static void AssertWithinOriginalSlo(long sentAt, long receivedAt, string receiver)
        => Assert.True(Stopwatch.GetElapsedTime(sentAt, receivedAt) < ThreeSeconds,
            $"{receiver} exceeded the original three-second terminal boundary");

    private static bool IsGameOver(JsonElement payload)
        => ReadString(payload, "type") == "gameState"
           && payload.TryGetProperty("state", out var state)
           && ReadString(state, "phase") == "GameOver";

    private static string? ReadString(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long ElapsedMicroseconds(long startedAt, long completedAt)
        => startedAt <= 0 || completedAt < startedAt
            ? 0
            : (long)(Stopwatch.GetElapsedTime(startedAt, completedAt).TotalMilliseconds * 1000);

    private sealed record StartedRoom(string RoomCode, string MatchId);
    private sealed record ReceivedFrame(JsonElement Payload, long ReceivedAt);
    private sealed record StageObservation(int Ordinal, int Kind, long RelativeMicroseconds);

    private sealed class SocketPeer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
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
                _ = await peer.ReceiveTypeAsync("session", SetupDeadline);
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

        internal Task<ReceivedFrame> ReceiveTypeAsync(string type, TimeSpan timeout)
            => ReceiveAsync(payload => ReadString(payload, "type") == type, timeout);

        internal async Task<ReceivedFrame> ReceiveAsync(Func<JsonElement, bool> predicate,
            TimeSpan timeout)
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
            return new ReceivedFrame(document.RootElement.Clone(), receivedAt);
        }

        public ValueTask DisposeAsync()
        {
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
