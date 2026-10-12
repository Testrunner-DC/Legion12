using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

public sealed class CriticalSnapshotDeltaDeliveryTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private readonly ITestOutputHelper _output;
    public CriticalSnapshotDeltaDeliveryTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    public async Task ActionableAndPromptSnapshotsRemainOrderedButCanUseRecipientDelta(int owner, bool prompt, bool ownTurn)
    {
        // Synthetic engine state, real room projection, mailbox and sender codec.
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var directory = Path.Combine(Path.GetTempPath(), "l12-critical-delta", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder);
        var sessions = new[] { Guid.NewGuid(), Guid.NewGuid() };
        manager.Connect(sessions[0], "delta-a", "甲");
        manager.Connect(sessions[1], "delta-b", "乙");
        var created = JsonSerializer.SerializeToElement(manager.CreateRoom(sessions[0])[0].Payload, WireJson);
        var roomCode = created.GetProperty("roomCode").GetString()!;
        manager.JoinRoom(sessions[1], roomCode);
        var rooms = (IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        var roomEntry = rooms.Cast<object>().Single();
        var room = roomEntry.GetType().GetProperty("Value")!.GetValue(roomEntry)!;
        var engine = new L12GameEngine(catalog, "critical-delta-match", roomCode, 104729,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        engine.State.Phase = L12Phase.Main;
        engine.State.ActivePlayer = ownTurn ? owner : 1 - owner;
        engine.State.PendingPrompts.Clear();
        room.GetType().GetProperty("Game")!.SetValue(room, engine);
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var delivered = new List<(byte[] Bytes, bool Delta)>();
        await using var outbound = new L12OutboundConnection((payload, _) =>
        {
            var wire = codec.Prepare(Assert.IsType<L12QueuedPayload>(payload));
            delivered.Add((wire.Payload, wire.IsDelta));
            wire.Commit();
            return Task.CompletedTask;
        });

        OutgoingMessage Snapshot(bool force) => Broadcast(manager, room, force).Single(message => message.SessionId == sessions[owner]);
        L12QueuedPayload Queue(OutgoingMessage message) => new(
            JsonSerializer.SerializeToUtf8Bytes(message.Payload, WireJson), message.IsGameState, message.ForceFullGameState);
        var initial = Snapshot(true);
        Assert.False(initial.ReplaceableGameState);
        Assert.True(initial.ForceFullGameState);
        Assert.True(await outbound.EnqueueAndWaitAsync(Queue(initial), CancellationToken.None));

        engine.State.Revision++;
        if (prompt)
            engine.State.PendingPrompts.Add(new L12Prompt
            {
                PromptId = "delta-owner-choice", PlayerIndex = owner, Kind = "fixture",
                Text = "选择", ValidChoices = ["confirm"], Continuation = "fixture",
            });
        var next = Snapshot(false);
        Assert.False(next.ReplaceableGameState); // Reliability does not depend on wire encoding.
        Assert.False(next.ForceFullGameState);
        Assert.True(await outbound.EnqueueAndWaitAsync(Queue(next), CancellationToken.None));
        await outbound.CompleteAsync();
        Assert.Equal(2, delivered.Count);
        Assert.False(delivered[0].Delta);
        Assert.True(delivered[1].Delta);
        var rebuilt = L12SnapshotWireCodec.ApplyDelta(JsonNode.Parse(delivered[0].Bytes)!.AsObject(), delivered[1].Bytes);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Queue(next).FullPayload), rebuilt));
        Assert.True(delivered[1].Bytes.Length < delivered[0].Bytes.Length / 2);
        _output.WriteLine($"Synthetic recipient={owner}, prompt={prompt}, ownTurn={ownTurn}: full={delivered[0].Bytes.Length}B delta={delivered[1].Bytes.Length}B");
        if (prompt)
            Assert.Equal("delta-owner-choice", rebuilt["state"]!["prompts"]![0]!["promptId"]!.GetValue<string>());
        var opponentIds = engine.State.Players[1 - owner].Hand.Select(card => card.InstanceId).ToArray();
        Assert.NotEmpty(opponentIds);
        foreach (var id in opponentIds)
        {
            Assert.DoesNotContain(id, Encoding.UTF8.GetString(delivered[0].Bytes), StringComparison.Ordinal);
            Assert.DoesNotContain(id, Encoding.UTF8.GetString(delivered[1].Bytes), StringComparison.Ordinal);
            Assert.DoesNotContain(id, rebuilt.ToJsonString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeltaPreservesNewNullPropertiesAndRemovesMissingProperties()
    {
        var original = JsonNode.Parse("""
            {"type":"gameState","state":{"matchId":"null-property","revision":1,"stable":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx","oldNull":null}}
            """)!.AsObject();
        var next = original.DeepClone().AsObject();
        next["state"]!["revision"] = 2;
        next["state"]!.AsObject().Remove("oldNull");
        next["state"]!["newNull"] = null;
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var first = codec.Prepare(new(JsonSerializer.SerializeToUtf8Bytes(original), true, true));
        first.Commit();
        var delta = codec.Prepare(new(JsonSerializer.SerializeToUtf8Bytes(next), true, false));
        Assert.True(delta.IsDelta);
        var rebuilt = L12SnapshotWireCodec.ApplyDelta(original, delta.Payload);
        Assert.True(rebuilt["state"]!.AsObject().ContainsKey("newNull"));
        Assert.False(rebuilt["state"]!.AsObject().ContainsKey("oldNull"));
        Assert.True(JsonNode.DeepEquals(next, rebuilt));
    }

    [Fact]
    public void FailedSendDoesNotAdvanceDeltaBaselineAndLegacyClientStillReceivesFull()
    {
        byte[] State(int revision) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "gameState", state = new { matchId = "failed-send", revision, stable = new string('稳', 2000) },
        }, WireJson);
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var baseline = codec.Prepare(new(State(1), true, true));
        baseline.Commit();
        Assert.True(codec.Prepare(new(State(2), true, false)).IsDelta); // Simulated failure: no Commit.
        var retry = codec.Prepare(new(State(3), true, false));
        Assert.Equal(1, JsonNode.Parse(retry.Payload)!["baseRevision"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(State(3)),
            L12SnapshotWireCodec.ApplyDelta(JsonNode.Parse(State(1))!.AsObject(), retry.Payload)));
        var legacy = new L12SnapshotWireCodec();
        Assert.False(legacy.Prepare(new(State(1), true, false)).IsDelta);
        Assert.False(legacy.Prepare(new(State(2), true, false)).IsDelta);
    }

    [Theory]
    [InlineData("{}", "{\"a\":null}")]
    [InlineData("{\"a\":null}", "{}")]
    [InlineData("{\"a\":null}", "{\"a\":{\"b\":null}}")]
    [InlineData("{\"a\":{\"b\":1}}", "{\"a\":null}")]
    [InlineData("[1,2,3]", "[3,2,1]")]
    [InlineData("[1,2]", "[1,2,3]")]
    [InlineData("[1,2]", "[]")]
    [InlineData("[{\"a\":1},{}]", "[{\"a\":null},{\"b\":null}]")]
    [InlineData("{\"兵力\":1000,\"0\":1}", "{\"兵力\":2000,\"0\":null}")]
    [InlineData("{\"a/b\":1,\"a.b\":2}", "{\"a/b\":2,\"a.b\":1}")]
    [InlineData("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}")]
    [InlineData("{\"a\":1e2}", "{\"a\":100.0}")]
    [InlineData("\"一\\n二\"", "\"一\\n三\"")]
    [InlineData("true", "false")]
    public void ReadOnlyComparisonPreservesJsonSemantics(string previousView, string nextView)
    {
        byte[] Frame(int revision, string view) => JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["type"] = "gameState",
            ["state"] = new JsonObject
            {
                ["matchId"] = "semantic-delta", ["revision"] = revision,
                ["stable"] = new string('稳', 2000), ["view"] = JsonNode.Parse(view),
            },
        });
        var original = Frame(1, previousView);
        var next = Frame(2, nextView);
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true);
        var first = codec.Prepare(new(original, true, true)); first.Commit();
        var delta = codec.Prepare(new(next, true, false));
        Assert.True(delta.IsDelta);
        var rebuilt = L12SnapshotWireCodec.ApplyDelta(JsonNode.Parse(original)!.AsObject(), delta.Payload);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(next), rebuilt));
    }

    private static IReadOnlyList<OutgoingMessage> Broadcast(L12RoomManager manager, object room, bool force)
        => (IReadOnlyList<OutgoingMessage>)typeof(L12RoomManager)
            .GetMethod("BroadcastGame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, [room, force, null, null, null, false])!;
}
