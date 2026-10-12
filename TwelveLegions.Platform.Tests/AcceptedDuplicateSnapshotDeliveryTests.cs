using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class AcceptedDuplicateSnapshotDeliveryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("ordinary", 0)]
    [InlineData("ordinary", 1)]
    [InlineData("gm", 0)]
    [InlineData("sandbox", 0)]
    [InlineData("sandbox", 1)]
    public async Task AcceptedReplayRetainsReliableAcknowledgementWithoutForcingValidBaselinesFull(string mode, int actor)
    {
        await using var fixture = await Fixture.CreateAsync(mode, actor);
        var codecs = fixture.Accepted.ToDictionary(m => m.SessionId, _ => RecoverySnapshotFanoutTests.Codec());
        var baselines = new Dictionary<Guid, JsonObject>();
        foreach (var message in fixture.Accepted)
        {
            var wire = await RecoverySnapshotFanoutTests.SendAsync(codecs[message.SessionId], message);
            Assert.False(wire.IsDelta); // Every connection starts cold.
            baselines[message.SessionId] = JsonNode.Parse(wire.Payload)!.AsObject();
        }
        var before = await fixture.CaptureAsync();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var messages = await fixture.ReplayAsync();
            Assert.Equal(fixture.Accepted.Count, messages.Count);
            foreach (var message in messages)
            {
                Assert.True(message.IsGameState);
                Assert.False(message.ReplaceableGameState); // Reliability is not relaxed.
                Assert.False(message.ForceFullGameState);
                var wire = await RecoverySnapshotFanoutTests.SendAsync(codecs[message.SessionId], message);
                Assert.True(wire.IsDelta);
                var rebuilt = L12SnapshotWireCodec.ApplyDelta(baselines[message.SessionId], wire.Payload);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(RecoverySnapshotFanoutTests.Queue(message).FullPayload), rebuilt));
                Assert.Equal(message.SessionId == fixture.Requester ? fixture.RequestId : null,
                    rebuilt["requestId"]?.GetValue<string>());
                Assert.Equal(fixture.Game.State.Revision, rebuilt["state"]!["revision"]!.GetValue<long>());
                fixture.AssertPrivacy(message.SessionId, rebuilt.ToJsonString());
                baselines[message.SessionId] = rebuilt;
            }
            Assert.Equal(before, await fixture.CaptureAsync());
        }
        // Request identity, not a second payload, governs an already-accepted replay.
        var altered = await fixture.CallAsync(JsonSerializer.SerializeToElement(new { type = "surrender" }, Json), fixture.RequestId);
        Assert.Equal(fixture.Accepted.Count, altered.Count(m => m.IsGameState));
        Assert.All(altered, message =>
        {
            Assert.True(message.IsGameState);
            Assert.False(message.ReplaceableGameState);
            Assert.False(message.ForceFullGameState);
            var payload = JsonSerializer.SerializeToElement(message.Payload, Json);
            Assert.Equal(message.SessionId == fixture.Requester ? fixture.RequestId : null,
                payload.TryGetProperty("requestId", out var acknowledgement) ? acknowledgement.GetString() : null);
        });
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("cold")]
    [InlineData("failed-send")]
    public async Task ReplayCannotBypassMissingSuccessfulOrNegotiatedBaseline(string kind)
    {
        await using var fixture = await Fixture.CreateAsync("ordinary", 0);
        var codec = new L12SnapshotWireCodec();
        if (kind != "legacy") codec.SetDeltaEnabled(true, true);
        if (kind == "failed-send")
            codec.Prepare(RecoverySnapshotFanoutTests.Queue(fixture.Accepted.Single(m => m.SessionId == fixture.Requester)));
        var message = (await fixture.ReplayAsync()).Single(m => m.SessionId == fixture.Requester);
        Assert.False(message.ReplaceableGameState);
        var wire = await RecoverySnapshotFanoutTests.SendAsync(codec, message);
        Assert.False(wire.IsDelta);
        Assert.Equal(RecoverySnapshotFanoutTests.Queue(message).FullPayload, wire.Payload);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("gm")]
    [InlineData("sandbox")]
    public async Task TerminalStateStillForcesEveryReplayRecipientFull(string mode)
    {
        await using var fixture = await Fixture.CreateAsync(mode, 0);
        await fixture.EndAsync();
        Assert.Equal(L12Phase.GameOver, fixture.Game.State.Phase);
        var before = await fixture.CaptureAsync();
        var messages = await fixture.ReplayAsync();
        Assert.Equal(fixture.Accepted.Count, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.False(message.ReplaceableGameState);
            Assert.True(message.ForceFullGameState);
        });
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ExplicitFullRecoveryTargetRemainsFullEvenWhenCriticalDeltaIsAllowed()
    {
        await using var fixture = await Fixture.CreateAsync("ordinary", 0);
        var method = typeof(L12RoomManager).GetMethod("BroadcastGame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var messages = (IReadOnlyList<OutgoingMessage>)method.Invoke(fixture.Manager,
            [fixture.Room, true, null, null, fixture.Requester, true])!;
        Assert.Equal(3, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.False(message.ReplaceableGameState);
            Assert.Equal(message.SessionId == fixture.Requester, message.ForceFullGameState);
        });
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("gm")]
    [InlineData("sandbox")]
    public async Task RejectedDuplicatesKeepTheirExistingRequesterOnlyError(string mode)
    {
        await using var fixture = await Fixture.CreateAsync(mode, 0);
        var command = JsonSerializer.SerializeToElement(new { type = "unknown-command" }, Json);
        var request = fixture.RequestId + "-rejected";
        await fixture.CallAsync(command, request);
        var before = await fixture.CaptureAsync();
        var message = Assert.Single(await fixture.CallAsync(command, request));
        Assert.Equal(fixture.Requester, message.SessionId);
        Assert.False(message.IsGameState);
        var payload = JsonSerializer.SerializeToElement(message.Payload, Json);
        Assert.Equal(request, payload.GetProperty("requestId").GetString());
        Assert.Equal("actionRejected", payload.GetProperty("type").GetString());
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required L12RoomManager Manager { get; init; }
        public required MatchRecorder Recorder { get; init; }
        public required L12GameEngine Game { get; init; }
        public required object Room { get; init; }
        public required Guid Requester { get; init; }
        public required Guid[] Players { get; init; }
        public required string Database { get; init; }
        public required string Mode { get; init; }
        public required int Actor { get; init; }
        public required JsonElement Command { get; init; }
        public required IReadOnlyList<OutgoingMessage> Accepted { get; set; }
        public string RequestId => "accepted-duplicate-" + Mode + "-" + Actor;

        public static async Task<Fixture> CreateAsync(string mode, int actor)
        {
            var database = Path.Combine(Path.GetTempPath(), "l12-accepted-duplicate-" + Guid.NewGuid().ToString("N"), "matches.db");
            var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var manager = new L12RoomManager(catalog, recorder);
            var host = Guid.NewGuid();
            manager.Connect(host, "anonymous-duplicate-host", "甲");
            if (mode == "ordinary")
            {
                var peer = Guid.NewGuid();
                var spectator = Guid.NewGuid();
                manager.Connect(peer, "anonymous-duplicate-peer", "乙");
                manager.Connect(spectator, "anonymous-duplicate-observer", "观战者");
                var code = Payload(manager.CreateRoom(host, new L12RoomOptions { Spectating = "public", DisasterMode = "none" })[0])
                    .GetProperty("roomCode").GetString()!;
                manager.JoinRoom(peer, code);
                await manager.SetReadyAsync(host, true);
                await manager.SetReadyAsync(peer, true);
                var room = OnlyRoom(manager);
                var game = (L12GameEngine)room.GetType().GetProperty("Game")!.GetValue(room)!;
                var initiative = game.State.PendingPrompts.Single(p => p.Kind == "initiative");
                var players = new[] { host, peer };
                await manager.HandleActionAsync(players[initiative.PlayerIndex], JsonSerializer.SerializeToElement(new
                {
                    type = "resolvePrompt", promptId = initiative.PromptId, cardInstanceIds = new[] { "first" },
                }, Json));
                Assert.Equal(L12Phase.Mulligan, game.State.Phase);
                manager.SpectateRoom(spectator, code);
                var fixture = new Fixture { Manager = manager, Recorder = recorder, Game = game, Room = room,
                    Requester = players[actor], Players = players, Database = database, Mode = mode, Actor = actor,
                    Command = JsonSerializer.SerializeToElement(new { type = "mulligan", cardInstanceIds = Array.Empty<string>() }, Json), Accepted = [] };
                fixture.Accepted = await fixture.ReplayAsync();
                Assert.Equal(3, fixture.Accepted.Count);
                return fixture;
            }
            await manager.CreateSandboxAsync(host, new L12SandboxRequest(DisasterMode: "none"));
            var sandbox = OnlyRoom(manager);
            var engine = (L12GameEngine)sandbox.GetType().GetProperty("Game")!.GetValue(sandbox)!;
            var sessions = ((IEnumerable<Guid>)sandbox.GetType().GetProperty("Sessions")!.GetValue(sandbox)!).ToArray();
            var command = mode == "gm"
                ? JsonSerializer.SerializeToElement(new { type = "setLife", targetPlayer = 1, value = engine.State.Players[1].Hp - 1 }, Json)
                : JsonSerializer.SerializeToElement(new { type = "endTurn" }, Json);
            // Sandbox lets its controller act for either seat, but endTurn belongs to the active seat.
            if (mode == "sandbox" && engine.State.ActivePlayer != actor)
                await manager.HandleSandboxActionAsync(host, engine.State.ActivePlayer, command, "setup-other-seat");
            var result = new Fixture { Manager = manager, Recorder = recorder, Game = engine, Room = sandbox,
                Requester = host, Players = sessions, Database = database, Mode = mode, Actor = actor, Command = command, Accepted = [] };
            result.Accepted = await result.ReplayAsync();
            Assert.Equal(2, result.Accepted.Count); // One host and one virtual opponent, not two network connections.
            Assert.NotEqual(L12Phase.GameOver, engine.State.Phase);
            return result;
        }

        public async Task<IReadOnlyList<OutgoingMessage>> ReplayAsync()
        {
            var messages = await CallAsync(Command, RequestId);
            return messages.Where(m => m.IsGameState).ToArray();
        }
        public Task<IReadOnlyList<OutgoingMessage>> CallAsync(JsonElement command, string requestId)
            => Mode == "ordinary" ? Manager.HandleActionAsync(Requester, command, requestId)
                : Mode == "gm" ? Manager.HandleGmActionAsync(Requester, command, requestId)
                : Manager.HandleSandboxActionAsync(Requester, Actor, command, requestId);
        public async Task EndAsync()
        {
            var surrender = JsonSerializer.SerializeToElement(new { type = "surrender" }, Json);
            if (Mode == "ordinary") await Manager.HandleActionAsync(Players[0], surrender, "end-fixture");
            else await Manager.HandleSandboxActionAsync(Requester, 0, surrender, "end-fixture");
        }
        public async Task<string> CaptureAsync()
        {
            var detail = (await Recorder.GetMatchAsync(Game.State.MatchId))!;
            var journal = (await Recorder.LoadJournalEngineAsync(Game.State.MatchId))!;
            var rows = new SortedDictionary<string, long>(StringComparer.Ordinal);
            var contents = new SortedDictionary<string, string>(StringComparer.Ordinal);
            await using var connection = new SqliteConnection($"Data Source={Database};Mode=ReadOnly");
            await connection.OpenAsync();
            var list = connection.CreateCommand(); list.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            var names = new List<string>();
            await using (var reader = await list.ExecuteReaderAsync()) while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            foreach (var name in names)
            {
                var count = connection.CreateCommand(); count.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\"";
                rows[name] = Convert.ToInt64(await count.ExecuteScalarAsync());
                var all = connection.CreateCommand(); all.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"")}\"";
                await using var data = await all.ExecuteReaderAsync();
                var columns = Enumerable.Range(0, data.FieldCount).Select(data.GetName).ToArray();
                var values = new List<string>();
                while (await data.ReadAsync())
                {
                    var row = Enumerable.Range(0, data.FieldCount).Select(index => data.IsDBNull(index)
                        ? null : data.GetValue(index)).ToArray();
                    values.Add(JsonSerializer.Serialize(row, Json));
                }
                values.Sort(StringComparer.Ordinal); // Physical SQL enumeration order is not a business change.
                contents[name] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { columns, values }, Json))));
            }
            return JsonSerializer.Serialize(new { state = Game.SerializeFullState(), hash = Game.ComputeStateHash(),
                Game.RandomDrawCount, Game.RandomState, Game.CardFactSignalSequence, Game.CardFactSignals,
                sequence = Room.GetType().GetProperty("CommandSequence")!.GetValue(Room),
                processed = ((IDictionary)Room.GetType().GetProperty("ProcessedActionRequests")!.GetValue(Room)!).Count,
                detail.Commands, journal.CommandSequence, journal.ProcessedRequests, rows, contents }, Json);
        }
        public void AssertPrivacy(Guid recipient, string json)
        {
            if (Mode != "ordinary") return; // Authorized sandbox host intentionally sees both seats.
            var seat = Array.IndexOf(Players, recipient);
            foreach (var player in Game.State.Players)
                if (seat != player.PlayerIndex)
                    foreach (var card in player.Hand) Assert.DoesNotContain(card.InstanceId, json, StringComparison.Ordinal);
        }
        private static object OnlyRoom(L12RoomManager manager)
        {
            var rooms = (IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            var entry = rooms.Cast<object>().Single(); return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        }
        private static JsonElement Payload(OutgoingMessage message) => JsonSerializer.SerializeToElement(message.Payload, Json);
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            using var pool = new SqliteConnection($"Data Source={Database}"); SqliteConnection.ClearPool(pool);
            // The governed test process owns temporary cleanup after every DB handle has exited.
        }
    }
}
