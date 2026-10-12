using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class RecoverySnapshotFanoutTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public async Task RecoveryIsReliableForEveryRecipientButOnlyRequesterForcesFull(int requester, bool ack)
    {
        await using var fixture = await Fixture.CreateAsync();
        var codecs = fixture.Sessions.ToDictionary(id => id, _ => Codec());
        var baselines = new Dictionary<Guid, JsonObject>();
        foreach (var message in fixture.Broadcast(false))
        {
            var first = await SendAsync(codecs[message.SessionId], message);
            Assert.False(first.IsDelta);
            baselines[message.SessionId] = JsonNode.Parse(first.Payload)!.AsObject();
        }
        var before = fixture.Game.SerializeFullState();
        var recovered = ack
            ? await fixture.Manager.RecoveryStateWithAckAsync(fixture.Sessions[requester], recovered: true)
            : await fixture.Manager.RecoveryStateAsync(fixture.Sessions[requester]);
        var games = recovered.Where(message => message.IsGameState).ToArray();
        Assert.Equal(4, games.Length);
        foreach (var message in games)
        {
            Assert.False(message.ReplaceableGameState);
            Assert.Equal(message.SessionId == fixture.Sessions[requester], message.ForceFullGameState);
            var wire = await SendAsync(codecs[message.SessionId], message);
            Assert.Equal(message.SessionId != fixture.Sessions[requester], wire.IsDelta);
            var rebuilt = wire.IsDelta ? L12SnapshotWireCodec.ApplyDelta(baselines[message.SessionId], wire.Payload)
                : JsonNode.Parse(wire.Payload)!.AsObject();
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Queue(message).FullPayload), rebuilt));
            fixture.AssertPrivacy(message.SessionId, rebuilt.ToJsonString());
        }
        Assert.Equal(before, fixture.Game.SerializeFullState());
        if (ack)
            Assert.Equal("recoveryComplete", JsonSerializer.SerializeToElement(recovered.Last().Payload, WireJson)
                .GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrdinarySpectatorFirstPacketCommitsBaselineOnlyAfterSuccessfulSend(bool successful)
    {
        await using var fixture = await Fixture.CreateAsync(joinSpectators: false);
        var joined = fixture.Manager.SpectateRoom(fixture.Sessions[2], fixture.Code)
            .Single(message => JsonSerializer.SerializeToElement(message.Payload, WireJson)
                .GetProperty("type").GetString() == "gameState");
        Assert.True(joined.IsGameState);
        Assert.True(joined.ForceFullGameState);
        Assert.False(joined.ReplaceableGameState);
        var codec = Codec();
        var first = codec.Prepare(Queue(joined));
        Assert.False(first.IsDelta);
        if (successful) first.Commit(); // Failure never publishes a baseline.
        var next = fixture.Broadcast(false).Single(message => message.SessionId == fixture.Sessions[2]);
        var wire = await SendAsync(codec, next);
        Assert.Equal(successful, wire.IsDelta);
        var rebuilt = wire.IsDelta ? L12SnapshotWireCodec.ApplyDelta(JsonNode.Parse(first.Payload)!.AsObject(), wire.Payload)
            : JsonNode.Parse(wire.Payload)!.AsObject();
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Queue(next).FullPayload), rebuilt));
        fixture.AssertPrivacy(next.SessionId, rebuilt.ToJsonString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task GameOverScopedRecoveryStillForcesEveryRecipientFull(int requester)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True(fixture.Game.Handle(0, new L12Command("surrender")).Accepted);
        Assert.Equal(L12Phase.GameOver, fixture.Game.State.Phase);
        var games = (await fixture.Manager.RecoveryStateWithAckAsync(fixture.Sessions[requester]))
            .Where(message => message.IsGameState).ToArray();
        Assert.Equal(4, games.Length);
        Assert.All(games, message => { Assert.True(message.ForceFullGameState); Assert.False(message.ReplaceableGameState); });
    }

    [Fact]
    public async Task DefaultCriticalBroadcastRemainsFullForAllAndUnknownRecoveryPublishesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.All(fixture.Broadcast(true), message =>
        {
            Assert.True(message.ForceFullGameState);
            Assert.False(message.ReplaceableGameState);
        });
        Assert.Empty(await fixture.Manager.RecoveryStateAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("cold")]
    [InlineData("failed-send")]
    public async Task PeerScopedFlagCannotBypassLegacyColdOrFailedInitialSendFullFallback(string kind)
    {
        await using var fixture = await Fixture.CreateAsync();
        var codec = new L12SnapshotWireCodec();
        if (kind != "legacy") codec.SetDeltaEnabled(true, true);
        if (kind == "failed-send")
            codec.Prepare(Queue(fixture.Broadcast(false).Single(message => message.SessionId == fixture.Sessions[1])));
        var peer = (await fixture.Manager.RecoveryStateWithAckAsync(fixture.Sessions[0]))
            .Single(message => message.IsGameState && message.SessionId == fixture.Sessions[1]);
        Assert.False(peer.ForceFullGameState);
        var wire = await SendAsync(codec, peer);
        Assert.False(wire.IsDelta);
        Assert.Equal(Queue(peer).FullPayload, wire.Payload);
    }

    internal static L12QueuedPayload Queue(OutgoingMessage message) => new(
        JsonSerializer.SerializeToUtf8Bytes(message.Payload, WireJson), message.IsGameState, message.ForceFullGameState);
    internal static L12SnapshotWireCodec Codec() { var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true); return codec; }
    internal static async Task<L12PreparedWirePayload> SendAsync(L12SnapshotWireCodec codec, OutgoingMessage message)
    {
        L12PreparedWirePayload? sent = null;
        await using var outbound = new L12OutboundConnection((payload, _) =>
        {
            sent = codec.Prepare(Assert.IsType<L12QueuedPayload>(payload));
            sent.Commit();
            return Task.CompletedTask;
        });
        Assert.True(await outbound.EnqueueAndWaitAsync(Queue(message), CancellationToken.None));
        await outbound.CompleteAsync();
        return Assert.IsType<L12PreparedWirePayload>(sent);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required L12RoomManager Manager { get; init; }
        public required MatchRecorder Recorder { get; init; }
        public required L12GameEngine Game { get; init; }
        public required object Room { get; init; }
        public required Guid[] Sessions { get; init; }
        public required string Code { get; init; }
        public required string DatabasePath { get; init; }
        public static async Task<Fixture> CreateAsync(bool joinSpectators = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "l12-recovery-fanout-" + Guid.NewGuid().ToString("N"));
            var database = Path.Combine(directory, "matches.db");
            var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var manager = new L12RoomManager(catalog, recorder);
            var sessions = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
            for (var i = 0; i < sessions.Length; i++) manager.Connect(sessions[i], "fanout-anonymous-" + i, "匿名" + i);
            var created = manager.CreateRoom(sessions[0], new L12RoomOptions { Spectating = "public" });
            var code = JsonSerializer.SerializeToElement(created[0].Payload, WireJson).GetProperty("roomCode").GetString()!;
            manager.JoinRoom(sessions[1], code);
            var rooms = (IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(manager)!;
            var entry = rooms.Cast<object>().Single();
            var room = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
            var game = new L12GameEngine(catalog, "fanout-" + Guid.NewGuid().ToString("N"), code, 65300,
                ["匿名甲", "匿名乙"], [0, 1], skipPreparation: true,
                stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
            game.State.Phase = L12Phase.Main;
            game.State.PendingPrompts.Clear();
            room.GetType().GetProperty("Game")!.SetValue(room, game);
            await recorder.StartAsync(game);
            var fixture = new Fixture { Manager = manager, Recorder = recorder, Game = game, Room = room,
                Sessions = sessions, Code = code, DatabasePath = database };
            if (joinSpectators) foreach (var id in sessions.Skip(2)) manager.SpectateRoom(id, code);
            return fixture;
        }
        public IReadOnlyList<OutgoingMessage> Broadcast(bool critical)
        {
            var method = typeof(L12RoomManager).GetMethod("BroadcastGame", BindingFlags.NonPublic | BindingFlags.Instance)!;
            // Supports the RED baseline and the new appended optional argument; does not alter product flags.
            var args = new object?[method.GetParameters().Length]; args[0] = Room; args[1] = critical;
            return (IReadOnlyList<OutgoingMessage>)method.Invoke(Manager, args)!;
        }
        public void AssertPrivacy(Guid recipient, string json)
        {
            var seat = Array.IndexOf(Sessions, recipient);
            foreach (var player in Game.State.Players)
                if (seat >= 2 || player.PlayerIndex != seat)
                    foreach (var card in player.Hand) Assert.DoesNotContain(card.InstanceId, json, StringComparison.Ordinal);
        }
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
            SqliteConnection.ClearPool(pool);
            // Governed test-run temporary root owns subsequent directory cleanup.
        }
    }
}
