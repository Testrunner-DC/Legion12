using System.Collections;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class CardSnapshotFrameTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RawCardKeysBorrowUtf8WithoutAllocatingRepeatedTextAndCheckEqualityAfterHashCollision()
    {
        var content = "{\"instanceId\":\"synthetic\",\"text\":\"" + new string('x', 2048) + "\"}";
        using var first = JsonDocument.Parse(content);
        using var second = JsonDocument.Parse(content);
        using var different = JsonDocument.Parse(content.Replace("synthetic", "different", StringComparison.Ordinal));
        var comparer = L12CardSnapshotFrame.RawCardSnapshotComparer.Instance;
        Assert.True(comparer.Equals(first.RootElement, second.RootElement));
        Assert.False(comparer.Equals(first.RootElement, different.RootElement));
        Assert.Equal(comparer.GetHashCode(first.RootElement), comparer.GetHashCode(second.RootElement));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            _ = comparer.GetHashCode(first.RootElement);
            if (!comparer.Equals(first.RootElement, second.RootElement)) throw new InvalidOperationException();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated <= 512, $"Raw-key lookup allocated {allocated} bytes");
        var collision = new Dictionary<JsonElement, int>(new ConstantHashRawComparer())
            { [first.RootElement] = 1, [different.RootElement] = 2 };
        Assert.Equal(2, collision.Count);
        Assert.Equal(1, collision[second.RootElement]);
    }

    [Fact]
    public void SemanticallyEqualButByteDifferentCardSnapshotsAreNotMerged()
    {
        var tail = ",\"effectText\":\"" + new string('x', 1024) + "\"}";
        var cards = new[] { "{\"instanceId\":\"a\",\"value\":1" + tail,
            "{\"instanceId\":\"a\",\"value\":1.0" + tail,
            "{\"instanceId\":\"\\u0061\",\"value\":1" + tail,
            "{\"value\":1,\"instanceId\":\"a\"" + tail };
        var events = string.Join(',', Enumerable.Range(0, 32)
            .Select(index => "{\"sequence\":" + index + ",\"cards\":[" + string.Join(',', cards) + "]}"));
        var raw = "{\"type\":\"gameState\",\"state\":{\"matchId\":\"synthetic-raw\",\"revision\":1,\"recentEvents\":["
            + events + "],\"lastAction\":null}}";
        Assert.True(L12CardSnapshotFrame.TryEncode(System.Text.Encoding.UTF8.GetBytes(raw), out var encoded));
        var packed = JsonNode.Parse(encoded)!.AsObject();
        Assert.Equal(4, packed["cardSnapshots"]!.AsArray().Count);
        foreach (var references in packed["eventCardRefs"]!.AsArray())
            Assert.Equal(new[] { 0, 1, 2, 3 }, references!.AsArray().Select(value => value!.GetValue<int>()));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(raw), Restore(packed)));
    }

    private sealed class ConstantHashRawComparer : IEqualityComparer<JsonElement>
    {
        public bool Equals(JsonElement first, JsonElement second)
            => L12CardSnapshotFrame.RawCardSnapshotComparer.Instance.Equals(first, second);
        public int GetHashCode(JsonElement value) => 0;
    }

    [Fact]
    public void RepeatedWholeCardEncodingDoesNotAllocateUtf16CopiesForEveryReference()
    {
        var original = Frame(1);
        var events = original["state"]!["recentEvents"]!.AsArray();
        var template = events[0]!.DeepClone();
        events.Clear();
        for (var index = 0; index < 48; index++)
        {
            var entry = template.DeepClone();
            entry["sequence"] = index;
            events.Add(entry);
        }
        var bytes = Bytes(original);
        Assert.True(L12CardSnapshotFrame.TryEncode(bytes, out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 4; index++)
            Assert.True(L12CardSnapshotFrame.TryEncode(bytes, out _));
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 4;
        // Wide enough for parsing/writing and one dictionary entry, not a second
        // UTF-16 copy of the repeated card for each occurrence in the envelope.
        Assert.True(perFrame < bytes.LongLength * 7 / 2 + 256 * 1024,
            $"Full frame {bytes.LongLength} bytes allocated {perFrame} bytes per encoding");
    }

    [Fact]
    public void NegotiatedFullFramePreservesEnvelopeAndRestoresIndependentWholeCards()
    {
        var original = Frame(7);
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true, true);
        codec.SetCardSnapshotFrameEnabled(true);

        var prepared = codec.Prepare(new(Bytes(original), true, true));

        Assert.False(prepared.IsDelta);
        var wire = JsonNode.Parse(prepared.Payload)!.AsObject();
        Assert.Equal(L12CardSnapshotFrame.WireType, wire["type"]!.GetValue<string>());
        Assert.Single(wire["cardSnapshots"]!.AsArray());
        var restored = Restore(wire);
        Assert.True(JsonNode.DeepEquals(original, restored));
        Assert.Equal("req-synthetic", restored["requestId"]!.GetValue<string>());
        Assert.Equal("accepted", restored["matchGovernanceResult"]!["status"]!.GetValue<string>());

        restored["state"]!["recentEvents"]![0]!["cards"]![0]!["nested"]!["values"]!.AsArray().Add(99);
        Assert.Equal(2, restored["state"]!["recentEvents"]![1]!["cards"]![0]!["nested"]!["values"]!.AsArray().Count);
    }

    [Fact]
    public void SuccessfulPackedSendCommitsOriginalBaselineAndDeltaWireRemainsUnchanged()
    {
        var first = Frame(1);
        var second = Frame(2);
        second["state"]!["turn"] = 3;
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true, true);
        codec.SetCardSnapshotFrameEnabled(true);

        var initial = codec.Prepare(new(Bytes(first), true, true));
        Assert.Equal(L12CardSnapshotFrame.WireType,
            JsonNode.Parse(initial.Payload)!["type"]!.GetValue<string>());
        initial.Commit();
        var delta = codec.Prepare(new(Bytes(second), true, false));

        Assert.True(delta.IsDelta);
        Assert.Equal("gameStateDelta", JsonNode.Parse(delta.Payload)!["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(second, L12SnapshotWireCodec.ApplyDelta(first, delta.Payload)));
    }

    [Fact]
    public void FailedSendCapabilityRetirementAndFallbacksNeverLoseOriginalFullFrame()
    {
        var first = Frame(1);
        var second = Frame(2);
        var third = Frame(3);
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true, true);
        codec.SetCardSnapshotFrameEnabled(true);

        var failed = codec.Prepare(new(Bytes(first), true, true));
        Assert.Equal(L12CardSnapshotFrame.WireType, JsonNode.Parse(failed.Payload)!["type"]!.GetValue<string>());
        // Deliberately do not commit: a failed socket send cannot publish this baseline.
        var retry = codec.Prepare(new(Bytes(second), true, false));
        Assert.False(retry.IsDelta);
        Assert.Equal(L12CardSnapshotFrame.WireType, JsonNode.Parse(retry.Payload)!["type"]!.GetValue<string>());
        retry.Commit();
        Assert.True(codec.Prepare(new(Bytes(third), true, false)).IsDelta);

        codec.SetCardSnapshotFrameEnabled(false);
        var plain = codec.Prepare(new(Bytes(third), true, true));
        Assert.False(plain.IsDelta);
        Assert.Equal(Bytes(third), plain.Payload);

        var legacy = new L12SnapshotWireCodec();
        Assert.Equal(Bytes(first), legacy.Prepare(new(Bytes(first), true, true)).Payload);
        var small = new JsonObject
        {
            ["type"] = "gameState",
            ["state"] = new JsonObject
            {
                ["matchId"] = "small",
                ["revision"] = 1,
                ["recentEvents"] = new JsonArray(),
                ["lastAction"] = null,
            },
        };
        var smallCodec = new L12SnapshotWireCodec();
        smallCodec.SetCardSnapshotFrameEnabled(true);
        Assert.Equal(Bytes(small), smallCodec.Prepare(new(Bytes(small), true, true)).Payload);
        Assert.False(L12CardSnapshotFrame.TryEncode(
            new byte[L12CardSnapshotFrame.MaximumWireBytes + 1], out _));
    }

    [Theory]
    [InlineData("object", true)]
    [InlineData("array", true)]
    [InlineData("absent", false)]
    public void CapabilityMustBeExplicitlyNegotiated(string shape, bool expected)
    {
        var hello = shape switch
        {
            "object" => "{\"capabilities\":{\"gameStateCardFrameV1\":true}}",
            "array" => "{\"capabilities\":[\"gameStateCardFrameV1\"]}",
            _ => "{}",
        };
        using var document = JsonDocument.Parse(hello);
        var method = typeof(L12WebSocketServer).GetMethod("ReadProtocolCapabilities",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var capabilities = method.Invoke(null, [document.RootElement])!;
        var property = capabilities.GetType().GetProperty("GameStateCardFrameV1")!;
        Assert.Equal(expected, (bool)property.GetValue(capabilities)!);
    }

    [Fact]
    public async Task RealRecipientProjectionsRoundTripAndPlainRecoveryRetiresOnlyNegotiatedTransport()
    {
        using var identity = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(identity.Path)!,
            "card-snapshot-frame-protocol.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(manager, recorder, identity.Store, identity.Catalog);
        await server.StartAsync(0);
        var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
        var webSocketEndpoint = new UriBuilder(endpoint) { Scheme = "ws", Path = "/ws" }.Uri;

        var registrations = Enumerable.Range(0, 4).Select(index =>
        {
            var result = identity.Store.Register($"cf{index}{Guid.NewGuid():N}"[..10], "Password123!");
            Assert.True(result.Success, result.Message);
            return result.Token!;
        }).ToArray();
        await using var first = await WirePeer.ConnectAsync(webSocketEndpoint, registrations[0], enabled: true);
        await using var second = await WirePeer.ConnectAsync(webSocketEndpoint, registrations[1], enabled: true);
        await using var spectator = await WirePeer.ConnectAsync(webSocketEndpoint, registrations[2], enabled: true);
        await using var legacy = await WirePeer.ConnectAsync(webSocketEndpoint, registrations[3], enabled: false);
        Assert.True(first.CardFrameEnabled);
        Assert.True(second.CardFrameEnabled);
        Assert.True(spectator.CardFrameEnabled);
        Assert.False(legacy.CardFrameEnabled);

        var created = manager.CreateRoom(first.SessionId, new L12RoomOptions { Spectating = "public" });
        var roomCode = JsonSerializer.SerializeToElement(created[0].Payload, Wire)
            .GetProperty("roomCode").GetString()!;
        manager.JoinRoom(second.SessionId, roomCode);
        await manager.SetReadyAsync(first.SessionId, true);
        await manager.SetReadyAsync(second.SessionId, true);
        manager.SpectateRoom(spectator.SessionId, roomCode);
        manager.SpectateRoom(legacy.SessionId, roomCode);

        var room = RuntimeRoom(manager);
        var game = Assert.IsType<L12GameEngine>(room.GetType().GetProperty("Game")!.GetValue(room));
        SeedRepeatedPublicCardEvents(game);
        var playerZeroHidden = game.State.Players[0].Library[1].InstanceId;
        var playerOneHidden = game.State.Players[1].Library[0].InstanceId;
        var expected = Broadcast(manager, room).ToDictionary(message => message.SessionId,
            message => JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(message.Payload, Wire))!.AsObject());
        Assert.DoesNotContain(playerOneHidden, expected[first.SessionId].ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(playerZeroHidden, expected[second.SessionId].ToJsonString(), StringComparison.Ordinal);
        foreach (var peer in new[] { spectator, legacy })
        {
            var projection = expected[peer.SessionId].ToJsonString();
            Assert.DoesNotContain(playerZeroHidden, projection, StringComparison.Ordinal);
            Assert.DoesNotContain(playerOneHidden, projection, StringComparison.Ordinal);
        }

        foreach (var peer in new[] { first, second, spectator, legacy })
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(expected[peer.SessionId], Wire);
            var codec = new L12SnapshotWireCodec();
            codec.SetCardSnapshotFrameEnabled(peer.CardFrameEnabled);
            var prepared = codec.Prepare(new(bytes, true, true));
            var wire = JsonNode.Parse(prepared.Payload)!.AsObject();
            var rebuilt = peer.CardFrameEnabled ? Restore(wire) : wire;
            Assert.Equal(peer.CardFrameEnabled ? L12CardSnapshotFrame.WireType : "gameState",
                wire["type"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(expected[peer.SessionId], rebuilt));
            var encoded = wire.ToJsonString();
            if (peer != first) Assert.DoesNotContain(playerZeroHidden, encoded, StringComparison.Ordinal);
            if (peer != second) Assert.DoesNotContain(playerOneHidden, encoded, StringComparison.Ordinal);
        }

        await first.SendAsync(new { type = "syncState" });
        var firstFrames = await Task.WhenAll(first.ReceiveGameAsync(), second.ReceiveGameAsync(),
            spectator.ReceiveGameAsync(), legacy.ReceiveGameAsync());
        var peers = new[] { first, second, spectator, legacy };
        for (var index = 0; index < peers.Length; index++)
        {
            var peer = peers[index];
            var frame = firstFrames[index];
            var packed = frame["type"]!.GetValue<string>() == L12CardSnapshotFrame.WireType;
            Assert.Equal(peer.CardFrameEnabled, packed);
            var rebuilt = packed ? Restore(frame) : frame;
            Assert.True(JsonNode.DeepEquals(expected[peer.SessionId], rebuilt));
        }
        _ = await first.ReceiveTypeAsync("recoveryComplete");

        await first.SendAsync(new { type = "syncState", plainFull = true });
        Assert.Equal("gameState", (await first.ReceiveGameAsync())["type"]!.GetValue<string>());
        _ = await first.ReceiveTypeAsync("recoveryComplete");
        await first.SendAsync(new { type = "syncState" });
        Assert.Equal("gameState", (await first.ReceiveGameAsync())["type"]!.GetValue<string>());
        _ = await first.ReceiveTypeAsync("recoveryComplete");
    }

    private static JsonObject Frame(int revision)
    {
        var card = new JsonObject
        {
            ["instanceId"] = "synthetic-card",
            ["cardId"] = "SYNTHETIC",
            ["effectText"] = new string('甲', 1800),
            ["nested"] = new JsonObject { ["values"] = new JsonArray(1, 2) },
        };
        JsonArray Cards() => new(card.DeepClone(), card.DeepClone());
        return new JsonObject
        {
            ["type"] = "gameState",
            ["requestId"] = "req-synthetic",
            ["matchGovernanceResult"] = new JsonObject { ["status"] = "accepted" },
            ["rankedSettlement"] = new JsonObject { ["state"] = "pending" },
            ["state"] = new JsonObject
            {
                ["matchId"] = "match-synthetic",
                ["revision"] = revision,
                ["stateHash"] = $"hash-{revision}",
                ["phase"] = "GameOver",
                ["turn"] = 2,
                ["recentEvents"] = new JsonArray(
                    new JsonObject { ["sequence"] = 1, ["cards"] = Cards() },
                    new JsonObject { ["sequence"] = 2, ["cards"] = Cards() }),
                ["lastAction"] = new JsonObject { ["sequence"] = 2, ["cards"] = Cards() },
            },
        };
    }

    private static byte[] Bytes(JsonNode value) => JsonSerializer.SerializeToUtf8Bytes(value);

    private static JsonObject Restore(JsonObject wire)
    {
        var payload = wire["payload"]!.DeepClone().AsObject();
        var snapshots = wire["cardSnapshots"]!.AsArray();
        var events = payload["state"]!["recentEvents"]!.AsArray();
        var eventRefs = wire["eventCardRefs"]!.AsArray();
        for (var index = 0; index < events.Count; index++)
            events[index]!["cards"] = RestoreCards(eventRefs[index]!.AsArray(), snapshots);
        if (payload["state"]!["lastAction"] is JsonObject lastAction)
            lastAction["cards"] = RestoreCards(wire["lastActionCardRefs"]!.AsArray(), snapshots);
        return payload;
    }

    private static JsonArray RestoreCards(JsonArray references, JsonArray snapshots)
    {
        var cards = new JsonArray();
        foreach (var reference in references)
            cards.Add(snapshots[reference!.GetValue<int>()]!.DeepClone());
        return cards;
    }

    private static object RuntimeRoom(L12RoomManager manager)
    {
        var rooms = (IEnumerable)typeof(L12RoomManager).GetField("_rooms",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        var entry = rooms.Cast<object>().Single();
        return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
    }

    private static IReadOnlyList<OutgoingMessage> Broadcast(L12RoomManager manager, object room)
    {
        var method = typeof(L12RoomManager).GetMethod("BroadcastGame",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var arguments = new object?[method.GetParameters().Length];
        arguments[0] = room;
        arguments[1] = false;
        return (IReadOnlyList<OutgoingMessage>)method.Invoke(manager, arguments)!;
    }

    private static void SeedRepeatedPublicCardEvents(L12GameEngine game)
    {
        var card = game.State.Players[0].Library[0];
        var addEvent = typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "AddEvent" && method.GetParameters().Length == 4
                && method.GetParameters()[3].ParameterType == typeof(L12CardInstance[]));
        for (var index = 0; index < 48; index++)
            addEvent.Invoke(game, ["wire-fixture", 0, $"recipient projection fixture {index}",
                new[] { card }]);
    }

    private sealed class WirePeer : IAsyncDisposable
    {
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
        private readonly ClientWebSocket _socket = new();
        internal Guid SessionId { get; private set; }
        internal bool CardFrameEnabled { get; private set; }

        internal static async Task<WirePeer> ConnectAsync(Uri endpoint, string token, bool enabled)
        {
            var peer = new WirePeer();
            try
            {
                using var deadline = new CancellationTokenSource(Deadline);
                await peer._socket.ConnectAsync(endpoint, deadline.Token);
                await peer.SendAsync(new
                {
                    type = "hello",
                    authToken = token,
                    capabilities = enabled ? new { requestIds = true, gameStateCardFrameV1 = true }
                        : new { requestIds = true, gameStateCardFrameV1 = false },
                });
                var session = await peer.ReceiveTypeAsync("session");
                peer.SessionId = session["sessionId"]!.GetValue<Guid>();
                peer.CardFrameEnabled = session["capabilities"]![L12CardSnapshotFrame.Capability]!
                    .GetValue<bool>();
                _ = await peer.ReceiveTypeAsync("recoveryComplete");
                return peer;
            }
            catch
            {
                await peer.DisposeAsync();
                throw;
            }
        }

        internal Task SendAsync(object payload)
            => _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload, Wire).AsMemory(),
                WebSocketMessageType.Text, true, CancellationToken.None).AsTask();

        internal Task<JsonObject> ReceiveTypeAsync(string type)
            => ReceiveAsync(payload => payload["type"]?.GetValue<string>() == type);

        internal Task<JsonObject> ReceiveGameAsync()
            => ReceiveAsync(payload => payload["type"]?.GetValue<string>() is "gameState" or "gameStateCardFrameV1");

        private async Task<JsonObject> ReceiveAsync(Func<JsonObject, bool> predicate)
        {
            using var deadline = new CancellationTokenSource(Deadline);
            while (true)
            {
                using var message = new MemoryStream();
                var buffer = new byte[16 * 1024];
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, deadline.Token);
                    Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
                    await message.WriteAsync(buffer.AsMemory(0, result.Count), deadline.Token);
                } while (!result.EndOfMessage);
                var payload = JsonNode.Parse(message.ToArray())!.AsObject();
                if (predicate(payload)) return payload;
            }
        }

        public ValueTask DisposeAsync()
        {
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
