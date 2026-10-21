using System.Reflection;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class HandArrayDeltaTests
{
    private static byte[] Bytes(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node);
    private static JsonObject Card(string id) => new()
    {
        ["instanceId"] = id, ["cardId"] = "same-card", ["name"] = "同名卡",
        ["effectText"] = string.Concat(Enumerable.Repeat("合成冻结效果字段；", 24)),
        ["troops"] = 4000, ["unknownField"] = new JsonObject { ["future"] = true },
        ["attachedCards"] = new JsonArray(new JsonObject { ["instanceId"] = "child-" + id, ["tapped"] = true }),
    };
    private static JsonObject Frame(long revision, params string[] ids) => new()
    {
        ["type"] = "gameState", ["state"] = new JsonObject
        {
            ["matchId"] = "synthetic-hand-delta", ["revision"] = revision, ["stateHash"] = "hash-" + revision,
            ["players"] = new JsonArray(new JsonObject { ["playerIndex"] = 0, ["hand"] = new JsonArray(ids.Select(id => (JsonNode)Card(id)).ToArray()) },
                new JsonObject { ["playerIndex"] = 1, ["hand"] = new JsonArray() }),
        },
    };
    private static L12PreparedWirePayload Next(L12SnapshotWireCodec codec, JsonObject before, JsonObject after)
    {
        var first = codec.Prepare(new(Bytes(before), true, false)); first.Commit();
        return codec.Prepare(new(Bytes(after), true, false));
    }
    private static JsonObject Root(L12PreparedWirePayload wire) => JsonNode.Parse(wire.Payload)!.AsObject();

    [Theory]
    [InlineData("append")] [InlineData("remove")] [InlineData("middle")]
    [InlineData("changed-same-id")] [InlineData("empty-before")] [InlineData("empty-after")]
    public void CompleteValuesAndOrderSurviveEveryLengthChange(string scenario)
    {
        var before = Frame(1, "a", "b", "c");
        var after = scenario switch
        {
            "remove" => Frame(2, "a", "c"), "middle" or "changed-same-id" => Frame(2, "a", "b", "d", "c"),
            "empty-after" => Frame(2), _ => Frame(2, "a", "b", "c", "d"),
        };
        if (scenario == "empty-before") before = Frame(1);
        if (scenario == "changed-same-id")
        {
            var changed = after["state"]!["players"]![0]!["hand"]![1]!;
            changed["troops"] = 1000; changed["unknownField"]!["future"] = false;
            changed["attachedCards"]![0]!["tapped"] = false;
        }
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        var wire = Next(codec, before, after);
        var rebuilt = wire.IsDelta ? L12SnapshotWireCodec.ApplyDelta(before, wire.Payload) : Root(wire);
        Assert.True(JsonNode.DeepEquals(after, rebuilt));
        if (scenario is not "empty-before" and not "empty-after")
        {
            Assert.NotNull(Root(wire)["handSplices"]);
            var oldCodec = new L12SnapshotWireCodec(); oldCodec.SetDeltaEnabled(true, true);
            Assert.True(wire.Payload.Length < Next(oldCodec, before, after).Payload.Length);
        }
        rebuilt["state"]!["players"]![0]!["hand"]?.AsArray().FirstOrDefault()?.AsObject().Add("consumerOnly", true);
        Assert.DoesNotContain("consumerOnly", before.ToJsonString());
    }

    [Fact]
    public void FailedSendOldClientCapabilitiesRecoveryAndIntervalsKeepOriginalContracts()
    {
        var a = Frame(1, "a", "b", "c"); var b = Frame(2, "a", "b", "c", "d"); var c = Frame(3, "a", "b", "c", "e");
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        Assert.NotNull(Root(Next(codec, a, b))["handSplices"]); // Deliberately not committed.
        var retry = codec.Prepare(new(Bytes(c), true, false));
        Assert.Equal(1, Root(retry)["baseRevision"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(c, L12SnapshotWireCodec.ApplyDelta(a, retry.Payload)));
        retry.Commit(); codec.SetDeltaEnabled(true, true, false);
        Assert.False(codec.Prepare(new(Bytes(b), true, false)).IsDelta);
        Assert.False(codec.Prepare(new(Bytes(b), true, true)).IsDelta);
        var legacy = new L12SnapshotWireCodec(); legacy.SetDeltaEnabled(true, true);
        Assert.Null(Root(Next(legacy, a, b))["handSplices"]);
        codec.SetDeltaEnabled(true, true, true);
        for (var i = 0; i <= 32; i++)
        {
            var wire = codec.Prepare(new(Bytes(Frame(10 + i, i % 2 == 0 ? ["a", "b", "c"] : ["a", "b", "c", "d"])), true, false));
            Assert.Equal(i is not (0 or 32), wire.IsDelta); wire.Commit();
        }
    }

    [Theory]
    [InlineData("base")] [InlineData("result")] [InlineData("duplicate")]
    [InlineData("range")] [InlineData("fraction")] [InlineData("insert-id")]
    [InlineData("parent-overlap")] [InlineData("leaf-overlap")] [InlineData("owner")]
    [InlineData("base-revision")] [InlineData("result-revision")] [InlineData("match")]
    [InlineData("null")] [InlineData("unknown")]
    public void CorruptionAndConflictsNeverMutateTheBaseline(string fault)
    {
        var a = Frame(1, "a", "b", "c"); var b = Frame(2, "a", "b", "c", "d");
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        var delta = Root(Next(codec, a, b)); var window = delta["handSplices"]!; var edit = window["edits"]![0]!;
        switch (fault)
        {
            case "base": edit["baseIds"]![0] = "wrong"; break;
            case "result": edit["resultIds"]![0] = "wrong"; break;
            case "duplicate": window["edits"]!.AsArray().Add(edit.DeepClone()); break;
            case "range": edit["remove"] = 99; break;
            case "fraction": edit["prefix"] = 1.5; break;
            case "insert-id": edit["insert"]![0]!["instanceId"] = "a"; break;
            case "owner": edit["playerIndex"] = 1; break;
            case "base-revision": delta["baseRevision"] = 9; break;
            case "result-revision": delta["revision"] = 9; break;
            case "match": delta["matchId"] = "other"; break;
            case "null": delta["handSplices"] = null; break;
            case "unknown": window["unknown"] = true; break;
            default:
                delta["changes"]!.AsArray().Add(new JsonObject { ["path"] = fault == "parent-overlap"
                    ? new JsonArray("state", "players", 0) : new JsonArray("state", "players", 0, "hand", 0, "name"),
                    ["value"] = fault == "parent-overlap" ? b["state"]!["players"]![0]!.DeepClone() : JsonValue.Create("changed"), ["remove"] = false });
                break;
        }
        var original = a.ToJsonString();
        Assert.ThrowsAny<Exception>(() => L12SnapshotWireCodec.ApplyDelta(a, Bytes(delta)));
        Assert.Equal(original, a.ToJsonString());
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"handArrayDeltaV1\":false}")] [InlineData("{\"handArrayDeltaV1\":true}")]
    [InlineData("[\"handArrayDeltaV1\"]")]
    public void HandCapabilityMustBeExplicit(string requested)
    {
        using var document = JsonDocument.Parse("{\"capabilities\":" + requested + "}");
        var method = typeof(L12WebSocketServer).GetMethod("ReadProtocolCapabilities", BindingFlags.Static | BindingFlags.NonPublic)!;
        var capability = method.Invoke(null, [document.RootElement])!;
        Assert.Equal(requested.Contains("true") || requested.StartsWith('['),
            capability.GetType().GetProperty("HandArrayDeltaV1")!.GetValue(capability));
    }

    [Fact]
    public void ActualEngineRecipientFramesPreservePrivacyAndExportExactClientFixtures()
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var game = new L12GameEngine(catalog, "synthetic-hand-array-wire", "LOCAL", 922104,
            ["Synthetic A", "Synthetic B"], [0, 1], skipPreparation: true, disasterMode: "none");
        game.State.Phase = L12Phase.Main; game.State.PendingPrompts.Clear();
        var codecs = new[] { new L12SnapshotWireCodec(), new L12SnapshotWireCodec(), new L12SnapshotWireCodec() };
        var legacy = new[] { new L12SnapshotWireCodec(), new L12SnapshotWireCodec(), new L12SnapshotWireCodec() };
        foreach (var codec in codecs) codec.SetDeltaEnabled(true, true, true);
        foreach (var codec in legacy) codec.SetDeltaEnabled(true, true);
        var previous = new JsonObject?[3]; var clientCases = new JsonArray();
        long newBytes = 0, oldBytes = 0; var edits = 0;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        for (var frame = 0; frame < 19; frame++)
        {
            if (frame > 0)
            {
                var result = game.Handle(game.State.ActivePlayer, new("endTurn"));
                Assert.True(result.Accepted, result.Error); Assert.Empty(game.State.PendingPrompts);
            }
            var state = game.SerializeFullState(); var random = game.RandomState; var fact = game.CardFactSignalSequence;
            for (var index = 0; index < 3; index++)
            {
                var viewer = index - 1;
                var projection = viewer < 0 ? game.SnapshotForSpectator() : game.SnapshotFor(viewer);
                var full = JsonSerializer.SerializeToUtf8Bytes(new { type = "gameState", state = projection }, options);
                var expected = JsonNode.Parse(full)!.AsObject();
                var next = codecs[index].Prepare(new(full, true, false));
                var original = legacy[index].Prepare(new(full, true, false));
                newBytes += next.Payload.Length; oldBytes += original.Payload.Length;
                foreach (var player in game.State.Players.Where(player => player.PlayerIndex != viewer))
                    foreach (var hidden in player.Hand)
                        Assert.DoesNotContain(hidden.InstanceId, Encoding.UTF8.GetString(next.Payload), StringComparison.Ordinal);
                if (next.IsDelta)
                {
                    Assert.NotNull(previous[index]);
                    Assert.True(JsonNode.DeepEquals(expected, L12SnapshotWireCodec.ApplyDelta(previous[index]!, next.Payload)));
                    var delta = Root(next);
                    if (delta.ContainsKey("handSplices"))
                    {
                        edits++;
                        clientCases.Add(new JsonObject { ["baseline"] = previous[index]!.DeepClone(),
                            ["delta"] = delta, ["expected"] = expected.DeepClone() });
                    }
                }
                next.Commit(); original.Commit(); previous[index] = expected;
            }
            Assert.Equal(state, game.SerializeFullState()); Assert.Equal(random, game.RandomState);
            Assert.Equal(fact, game.CardFactSignalSequence);
        }
        Assert.True(edits > 0); Assert.True(newBytes < oldBytes);
        if (Environment.GetEnvironmentVariable("L12_HAND_DELTA_CLIENT_FIXTURES") is { Length: > 0 } target)
        {
            Assert.True(Path.IsPathFullyQualified(target));
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { schema = 1, syntheticOnly = true, frames = 57, realEndTurns = 18,
                newBytes, oldBytes, handDeltaFrames = edits, clientCases });
        }
    }

    [Theory]
    [InlineData("you")] [InlineData("observerView")]
    public void ReceiverScopeChangeNeverUsesHandArraySplices(string field)
    {
        var a = Frame(1, "a", "b", "c"); var b = Frame(2, "a", "b", "c", "d");
        if (field == "you") { a["state"]![field] = 0; b["state"]![field] = -1; }
        else { a[field] = "referee"; b[field] = "public"; }
        Assert.Null(L12HandArrayDelta.TryCreate(JsonSerializer.SerializeToElement(a), JsonSerializer.SerializeToElement(b)));
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        var wire = Next(codec, a, b);
        Assert.Null(Root(wire)["handSplices"]);
        Assert.True(JsonNode.DeepEquals(b, wire.IsDelta ? L12SnapshotWireCodec.ApplyDelta(a, wire.Payload) : Root(wire)));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void ExactOrdinaryByteCountMatchesOriginalEncodingForBothHandsAndEscapedFields(bool both, bool events)
    {
        var a = Frame(1, "a", "b", "c"); var b = Frame(2, "a", "b", "c", "d");
        // Hold the legacy packet below its full-snapshot threshold so this test
        // compares the actual original delta encoder, not its legitimate full fallback.
        a["stablePublicFixture"] = new string('中', 2000);
        b["stablePublicFixture"] = new string('中', 2000);
        b["state"]!["players"]![0]!["hand"]![3]!["unknownField"] =
            new JsonObject { ["引号\"与斜杠\\"] = "中文😀\n\t\"\\", ["null"] = null, ["number"] = -1.25 };
        if (both)
        {
            a["state"]!["players"]![1]!["hand"] = new JsonArray(Card("e"), Card("f"), Card("g"));
            b["state"]!["players"]![1]!["hand"] = new JsonArray(Card("e"), Card("g"));
        }
        if (events)
        {
            JsonObject Event(int sequence) => new() { ["sequence"] = sequence, ["text"] = "完全公开事件", ["cards"] = new JsonArray() };
            a["state"]!["recentEvents"] = new JsonArray(Event(1), Event(2));
            b["state"]!["recentEvents"] = new JsonArray(Event(2), Event(3));
        }
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        var candidate = Root(Next(codec, a, b));
        var hands = Assert.IsType<JsonObject>(candidate["handSplices"]);
        var ordinary = new L12SnapshotWireCodec(); ordinary.SetDeltaEnabled(true, true);
        var original = Next(ordinary, a, b); Assert.True(original.IsDelta);
        Assert.Equal(original.Payload.Length,
            L12SnapshotWireCodec.MeasureOrdinaryDelta(candidate, JsonSerializer.SerializeToElement(b), hands));
        Assert.True(JsonNode.DeepEquals(b, L12SnapshotWireCodec.ApplyDelta(a, Bytes(candidate))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealSocketNegotiationCommandsAndRecoveryPreserveRecipientHands(bool enabled)
    {
        using var identity = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(identity.Path)!, "hand-wire.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(manager, recorder, identity.Store, identity.Catalog);
        await server.StartAsync(0);
        var endpoint = new UriBuilder(server.Addresses.Single()) { Host = "127.0.0.1", Scheme = "ws", Path = "/ws" }.Uri;
        var tokens = Enumerable.Range(0, 2).Select(index => identity.Store.Register(
            $"hd{index}{Guid.NewGuid():N}"[..10], "Password123!").Token!).ToArray();
        await using var first = await HandWirePeer.Connect(endpoint, tokens[0], enabled);
        await using var second = await HandWirePeer.Connect(endpoint, tokens[1], enabled);
        var peers = new[] { first, second };
        var created = manager.CreateRoom(first.Session, new L12RoomOptions { Spectating = "public" });
        var code = JsonSerializer.SerializeToElement(created[0].Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("roomCode").GetString()!;
        manager.JoinRoom(second.Session, code);
        var rooms = (System.Collections.IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var entry = rooms.Cast<object>().Single();
        var room = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        // Isolated initial fixture precedes the recorder; all measured changes use real socket commands.
        var game = new L12GameEngine(identity.Catalog, "hand-socket-" + Guid.NewGuid().ToString("N"), code, 922104,
            ["Synthetic A", "Synthetic B"], [0, 1], skipPreparation: true,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.Phase = L12Phase.Main; game.State.PendingPrompts.Clear();
        room.GetType().GetProperty("Game")!.SetValue(room, game);
        await recorder.StartAsync(game);
        foreach (var peer in peers)
        {
            await peer.Send(new { type = "syncState" });
            await peer.Receive("recoveryComplete");
            Assert.NotNull(peer.Current);
        }
        var cases = new JsonArray();
        for (var step = 0; step < 10; step++)
        {
            var sender = peers[game.State.ActivePlayer];
            var requestId = "hand-real-" + step;
            var revision = game.SnapshotFor(0).Revision;
            await sender.Send(new { type = "gameAction", requestId, command = new { type = "endTurn" } });
            foreach (var peer in peers)
            {
                await peer.ReceiveGame(revision);
                var current = peer.Current!;
                if (peer == sender) Assert.Equal(requestId, current["requestId"]?.GetValue<string>());
                Assert.Equal(game.ComputeStateHash(), current["state"]!["stateHash"]!.GetValue<string>());
                var seat = Array.IndexOf(peers, peer);
                var projection = JsonSerializer.SerializeToNode(seat < 2 ? game.SnapshotFor(seat) : game.SnapshotForSpectator(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.True(JsonNode.DeepEquals(projection["players"], current["state"]!["players"]));
                foreach (var player in game.State.Players.Where(player => player.PlayerIndex != seat))
                    foreach (var hidden in player.Hand)
                        Assert.DoesNotContain(hidden.InstanceId, peer.LastRaw!, StringComparison.Ordinal);
                if (peer.LastDelta is { } delta)
                    cases.Add(new JsonObject { ["baseline"] = peer.LastBase!.DeepClone(),
                        ["delta"] = delta.DeepClone(), ["expected"] = current.DeepClone() });
            }
        }
        Assert.Equal(enabled, peers.Sum(peer => peer.HandFrames) > 0);
        await first.Send(new { type = "syncState", plainFull = true });
        await first.Receive("recoveryComplete");
        Assert.Equal("gameState", first.LastType);
        await first.Send(new { type = "syncState" });
        await first.Receive("recoveryComplete");
        Assert.Equal("gameState", first.LastType);
        await first.DisposeAsync();
        await using var reconnected = await HandWirePeer.Connect(endpoint, tokens[0], enabled);
        Assert.Equal("gameState", reconnected.LastType);
        Assert.Equal(0, reconnected.HandFrames);
        Assert.Equal(game.ComputeStateHash(), reconnected.Current!["state"]!["stateHash"]!.GetValue<string>());
        if (enabled && Environment.GetEnvironmentVariable("L12_HAND_DELTA_SOCKET_FIXTURES") is { Length: > 0 } path)
        {
            Assert.True(Path.IsPathFullyQualified(path));
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(stream, new { schema = 1, syntheticOnly = true, realSocket = true,
                realCommands = 10, handDeltaFrames = cases.Count, clientCases = cases });
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(8192)] [InlineData(80000)]
    public void ExactByteCountSurvivesScratchGrowthAndRepeatedPoolReuse(int length)
    {
        var a = Frame(1, "a", "b", "c"); var b = Frame(2, "a", "b", "c", "d");
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        var delta = Root(Next(codec, a, b));
        var hands = Assert.IsType<JsonObject>(delta["handSplices"]);
        b["state"]!["players"]![0]!["hand"]![3]!["futureLongValue"] = new string('中', length) + "😀\"\\\n";
        var ordinary = delta.DeepClone().AsObject(); ordinary.Remove("handSplices");
        ordinary["changes"]!.AsArray().Add(new JsonObject
        {
            ["path"] = new JsonArray("state", "players", 0, "hand"),
            ["value"] = b["state"]!["players"]![0]!["hand"]!.DeepClone(), ["remove"] = false,
        });
        var expected = Bytes(ordinary).Length;
        var current = JsonSerializer.SerializeToElement(b);
        for (var repeat = 0; repeat < 3; repeat++)
            Assert.Equal(expected, L12SnapshotWireCodec.MeasureOrdinaryDelta(delta, current, hands));
    }

    private sealed class HandWirePeer : IAsyncDisposable
    {
        private readonly ClientWebSocket socket = new();
        internal Guid Session;
        internal JsonObject? Current, LastBase, LastDelta;
        internal int HandFrames;
        internal string? LastRaw, LastType;
        internal static async Task<HandWirePeer> Connect(Uri endpoint, string token, bool enabled)
        {
            var peer = new HandWirePeer();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await peer.socket.ConnectAsync(endpoint, deadline.Token);
                await peer.Send(new { type = "hello", authToken = token, capabilities = new
                    { requestIds = true, deltaGameState = true, eventWindowDeltaV1 = true, handArrayDeltaV1 = enabled } });
                var session = await peer.Receive("session");
                peer.Session = session["sessionId"]!.GetValue<Guid>();
                Assert.Equal(enabled, session["capabilities"]!["handArrayDeltaV1"]!.GetValue<bool>());
                await peer.Receive("recoveryComplete");
                return peer;
            }
            catch { await peer.DisposeAsync(); throw; }
        }
        internal async Task Send(object payload)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload).AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
        }
        internal Task<JsonObject> Receive(string type) => Read(value => value["type"]?.GetValue<string>() == type);
        internal Task<JsonObject> ReceiveGame(long revision) => Read(value =>
            (value["type"]?.GetValue<string>() is "gameState" or "gameStateDelta")
            && Current?["state"]?["revision"]?.GetValue<long>() > revision);
        private async Task<JsonObject> Read(Func<JsonObject, bool> predicate)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (true)
            {
                using var message = new MemoryStream(); var buffer = new byte[16384];
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, deadline.Token);
                    Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
                    await message.WriteAsync(buffer.AsMemory(0, result.Count), deadline.Token);
                } while (!result.EndOfMessage);
                var raw = Encoding.UTF8.GetString(message.ToArray());
                var value = JsonNode.Parse(raw)!.AsObject();
                var type = value["type"]?.GetValue<string>();
                if (type is "gameState" or "gameStateDelta")
                {
                    LastBase = Current?.DeepClone().AsObject(); LastDelta = null;
                    Current = type == "gameState" ? value : L12SnapshotWireCodec.ApplyDelta(Current!, Encoding.UTF8.GetBytes(raw));
                    if (value["handSplices"] is not null) { HandFrames++; LastDelta = value; }
                    LastRaw = raw; LastType = type;
                }
                Assert.NotEqual("actionRejected", type);
                if (predicate(value)) return value;
            }
        }
        public ValueTask DisposeAsync() { socket.Abort(); socket.Dispose(); return ValueTask.CompletedTask; }
    }
}
