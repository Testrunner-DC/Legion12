using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PropertySnapshotFrameTests
{
    private static byte[] Bytes(JsonNode value) => L12SnapshotJson.SerializeToUtf8Bytes(value);

    private static JsonObject Frame(int revision = 1)
    {
        var cards = new JsonArray();
        for (var index = 0; index < 96; index++) cards.Add(new JsonObject
        {
            ["instanceId"] = $"synthetic-{index}", ["cardId"] = "SYNTHETIC", ["name"] = "合成军团",
            ["effectText"] = "原始值含 <>&、\\u003C 以及 \"instanceId\": 字样，不可改写",
            ["continuousTroopsModifier"] = index, ["cannotUntapUntilRound"] = -1,
            ["attackOnlyTroopsBonusUntilTurn"] = -1, ["canAttackBackAndMasterUntilTurn"] = -1,
            ["unknownFutureField"] = new JsonObject { ["@0"] = "保留", ["~name"] = null },
        });
        return new JsonObject { ["type"] = "gameState", ["requestId"] = "synthetic-ack",
            ["state"] = new JsonObject { ["matchId"] = "synthetic-property", ["revision"] = revision,
                ["stateHash"] = "hash-" + revision, ["phase"] = "GameOver", ["cards"] = cards,
                ["recentEvents"] = new JsonArray(), ["lastAction"] = null } };
    }

    internal static JsonNode Restore(JsonNode wire)
    {
        JsonNode? Decode(JsonNode? value)
        {
            if (value is JsonArray array) return new JsonArray(array.Select(Decode).ToArray());
            if (value is not JsonObject obj) return value?.DeepClone();
            var restored = new JsonObject();
            foreach (var pair in obj)
            {
                var key = pair.Key.StartsWith('~') ? pair.Key[1..]
                    : pair.Key.StartsWith('@') ? L12PropertySnapshotFrame.Names[
                        pair.Key[1..].Aggregate(0, (number, digit) => number * 36
                            + (digit <= '9' ? digit - '0' : digit - 'a' + 10))] : pair.Key;
                restored.Add(key, Decode(pair.Value));
            }
            return restored;
        }
        return Decode(wire["payload"])!;
    }

    [Fact]
    public void FixedPublicNamesPreserveAllValuesUnknownKeysAndEnvelope()
    {
        var frame = Frame(); var original = Bytes(frame);
        Assert.True(L12PropertySnapshotFrame.TryEncode(original, out var packed));
        var wire = JsonNode.Parse(packed)!;
        Assert.Equal(L12PropertySnapshotFrame.WireType, wire["type"]!.GetValue<string>());
        Assert.Equal("synthetic-property", wire["matchId"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(frame, Restore(wire)));
        Assert.True(packed.Length < original.Length * .75);
        Assert.Equal(original, Bytes(frame));
        Assert.Contains("原始值含", System.Text.Encoding.UTF8.GetString(packed), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("authenticationRequired")]
    [InlineData("chatMessage")]
    [InlineData("session")]
    [InlineData("presenceSnapshot")]
    public void OnlyAlreadyProjectedGamePacketsAreEligible(string type)
    {
        var frame = Frame(); frame["type"] = type;
        Assert.False(L12PropertySnapshotFrame.TryEncode(Bytes(frame), out _));
        var codec = new L12SnapshotWireCodec(); codec.SetPropertyFrameEnabled(true);
        var original = Bytes(frame);
        Assert.Equal(original, codec.Prepare(new(original, false, false)).Payload);
    }

    [Fact]
    public void HardBoundsAndUnknownIdentityFallbackButValidSmallPacketsHaveNoBenefitBranch()
    {
        foreach (var bytes in new[] { "{}"u8.ToArray(), new byte[L12PropertySnapshotFrame.MaximumWireBytes + 1],
                     System.Text.Encoding.UTF8.GetBytes(new string('x', 5000)),
                     Bytes(new JsonObject { ["type"] = "gameState", ["state"] = null, ["text"] = new string('x', 6000) }) })
            Assert.False(L12PropertySnapshotFrame.TryEncode(bytes, out _));
        var unprofitable = new JsonObject { ["type"] = "gameState", ["state"] = new JsonObject
            { ["matchId"] = "synthetic", ["text"] = new string('x', 10000) } };
        Assert.True(L12PropertySnapshotFrame.TryEncode(Bytes(unprofitable), out var fixedEncoding));
        Assert.True(JsonNode.DeepEquals(unprofitable, Restore(JsonNode.Parse(fixedEncoding)!)));
        var missing = Frame(); missing["state"]!["matchId"] = "";
        Assert.False(L12PropertySnapshotFrame.TryEncode(Bytes(missing), out _));
    }

    [Fact]
    public void SuccessfulSendKeepsOriginalDeltaBaselineAndLateCommitCannotUndoRetirement()
    {
        var first = Frame(1); var second = Frame(2); var third = Frame(3);
        // Enough fields change for the real delta to benefit from the same optional encoding.
        foreach (var card in second["state"]!["cards"]!.AsArray()) card!["continuousTroopsModifier"] = 999;
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        codec.SetPropertyFrameEnabled(true);
        var initial = codec.Prepare(new(Bytes(first), true, true));
        Assert.Equal(L12PropertySnapshotFrame.WireType, JsonNode.Parse(initial.Payload)!["type"]!.GetValue<string>());
        // A failed send does not advance baseline.
        Assert.False(codec.Prepare(new(Bytes(second), true, false)).IsDelta);
        initial.Commit();
        var next = codec.Prepare(new(Bytes(second), true, false)); Assert.True(next.IsDelta);
        var delta = JsonNode.Parse(next.Payload)!;
        if (delta["type"]!.GetValue<string>() == L12PropertySnapshotFrame.WireType) delta = Restore(delta);
        Assert.True(JsonNode.DeepEquals(second, L12SnapshotWireCodec.ApplyDelta(first, Bytes(delta))));
        codec.SetPropertyFrameEnabled(false);
        next.Commit(); // Completion of a retired send cannot reintroduce its baseline.
        var recovery = codec.Prepare(new(Bytes(third), true, false));
        Assert.False(recovery.IsDelta); Assert.Equal(Bytes(third), recovery.Payload);
        recovery.Commit();
        Assert.True(codec.Prepare(new(Bytes(Frame(4)), true, false)).IsDelta);
        var legacy = new L12SnapshotWireCodec(); Assert.Equal(Bytes(first), legacy.Prepare(new(Bytes(first), true, true)).Payload);
    }

    [Fact]
    public void FixedNamesDoNotCouplePublicGuessesToPrivateStringEquality()
    {
        int Measure(string publicText, string privateText)
        {
            var frame = Frame(); frame["publicText"] = publicText; frame["privateText"] = privateText;
            Assert.True(L12PropertySnapshotFrame.TryEncode(Bytes(frame), out var payload)); return payload.Length;
        }
        Assert.Equal(0, Measure("PUBLIC-A", "SECRET-X") - Measure("PUBLIC-A", "SECRET-Y")
            - Measure("PUBLIC-B", "SECRET-X") + Measure("PUBLIC-B", "SECRET-Y"));
        Assert.Equal(Measure("SAME-TEXT", "SAME-TEXT"), Measure("SAME-TEXT", "DIFF-TEXT"));
    }

    [Fact]
    public void EncodingSelectionDoesNotExposeAPublicPrivateValueLengthThreshold()
    {
        var frame = Frame(); frame["publicText"] = ""; frame["privateText"] = "";
        var original = Bytes(frame);
        Assert.True(L12PropertySnapshotFrame.TryEncode(original, out var initial));
        var previousPercentageBoundary = (original.Length - initial.Length) * 10 - original.Length;
        Assert.InRange(previousPercentageBoundary, 4096, 1000000);
        int Measure(int publicLength, int privateLength)
        {
            frame["publicText"] = new string('a', publicLength);
            frame["privateText"] = new string('s', privateLength);
            var bytes = Bytes(frame);
            return L12PropertySnapshotFrame.TryEncode(bytes, out var encoded) ? encoded.Length : bytes.Length;
        }
        var low = previousPercentageBoundary - 1536;
        var high = previousPercentageBoundary - 512;
        Assert.Equal(0, Measure(low, 0) - Measure(low, 1024) - Measure(high, 0) + Measure(high, 1024));
    }

    [Fact]
    public void EncodingSelectionDoesNotCouplePublicAndPrivateArrayStructure()
    {
        int Measure(int publicCount, int privateCount)
        {
            JsonArray Rows(int count, string name) => new(Enumerable.Range(0, count)
                .Select(_ => (JsonNode)new JsonObject { [name] = -1 }).ToArray());
            var value = new JsonObject { ["type"] = "gameState", ["state"] = new JsonObject
            {
                ["matchId"] = "synthetic", ["publicRows"] = Rows(publicCount, "canAttackBackAndMasterUntilTurn"),
                ["privateRows"] = Rows(privateCount, "cannotUntapUntilRound"),
            } };
            var bytes = Bytes(value);
            return L12PropertySnapshotFrame.TryEncode(bytes, out var encoded) ? encoded.Length : bytes.Length;
        }
        Assert.Equal(0, Measure(30, 0) - Measure(30, 10) - Measure(40, 0) + Measure(40, 10));
    }

    [Fact]
    public void LegalInputNearSizeLimitCannotSelectPlainFromCombinedPublicPrivateLengths()
    {
        var sizes = new Dictionary<(int Public, int Private), int>();
        foreach (var publicLength in new[] { 2096934, 2096974 })
        foreach (var privateLength in new[] { 0, 40 })
        {
            var frame = new JsonObject { ["type"] = "gameState", ["state"] = new JsonObject
            {
                ["matchId"] = "synthetic", ["publicText"] = new string('a', publicLength),
                ["privateText"] = new string('s', privateLength),
            } };
            var original = Bytes(frame);
            Assert.True(original.Length <= L12PropertySnapshotFrame.MaximumWireBytes);
            Assert.True(L12PropertySnapshotFrame.TryEncode(original, out var encoded),
                $"Legal input unexpectedly selected plain: {publicLength}/{privateLength}");
            Assert.True(JsonNode.DeepEquals(frame, Restore(JsonNode.Parse(encoded)!)));
            sizes[(publicLength, privateLength)] = encoded.Length;
        }
        Assert.Equal(0, sizes[(2096934, 0)] - sizes[(2096934, 40)]
            - sizes[(2096974, 0)] + sizes[(2096974, 40)]);
    }

    [Fact]
    public void ReservedUnknownKeyEscapingDoesNotReencodeItsRawUnicodeOrHtmlBytes()
    {
        var key = "@" + new string('香', 300000) + string.Concat(Enumerable.Repeat("<>&😀", 1000));
        var frame = new JsonObject { ["type"] = "gameState", ["state"] = new JsonObject
            { ["matchId"] = "synthetic", [key] = 0 } };
        var original = Bytes(frame);
        Assert.True(original.Length < L12PropertySnapshotFrame.MaximumWireBytes);
        // The old branch really expands this legal fixture; do not rely on an
        // already-oversized HTML fixture failing before it reaches the encoder.
        Assert.True(JsonSerializer.SerializeToUtf8Bytes("~" + key).Length > original.Length + 4096);
        Assert.True(L12PropertySnapshotFrame.TryEncode(original, out var encoded));
        Assert.True(encoded.Length < original.Length + 4096);
        Assert.True(JsonNode.DeepEquals(frame, Restore(JsonNode.Parse(encoded)!)));
    }

    [Fact]
    public void VocabularyAndExactTokenAndDepthLimitsKeepTheEncodedBoundProof()
    {
        foreach (var (name, index) in L12PropertySnapshotFrame.Names.Select((name, index) => (name, index)))
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var token = index < 36 ? "@" + digits[index] : "@" + digits[index / 36] + digits[index % 36];
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(token).Length
                - JsonSerializer.SerializeToUtf8Bytes(name).Length <= 2);
        }
        Assert.Equal(2 * 1024 * 1024 + 2 * 100000 + 2048, L12PropertySnapshotFrame.MaximumEncodedWireBytes);
        foreach (var count in new[] { 99988, 99989 })
        {
            var frame = new JsonObject { ["type"] = "gameState", ["state"] = new JsonObject
                { ["matchId"] = "synthetic", ["nodes"] = new JsonArray(new JsonNode?[count]) } };
            Assert.Equal(count == 99988, L12PropertySnapshotFrame.TryEncode(Bytes(frame), out var encoded));
            Assert.True(encoded.Length <= L12PropertySnapshotFrame.MaximumEncodedWireBytes);
        }
        foreach (var count in new[] { 61, 62 })
        {
            var raw = "{\"type\":\"gameState\",\"state\":{\"matchId\":\"synthetic\",\"deep\":"
                + string.Concat(Enumerable.Repeat("{\"child\":", count)) + "{}" + new string('}', count) + "}}";
            Assert.Equal(count == 61, L12PropertySnapshotFrame.TryEncode(System.Text.Encoding.UTF8.GetBytes(raw), out _));
        }
    }

    [Fact]
    public void EncodingHasBoundedAllocationAndNoCrossFrameDictionary()
    {
        var original = Bytes(Frame()); Assert.True(L12PropertySnapshotFrame.TryEncode(original, out var first));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 8; index++)
        {
            Assert.True(L12PropertySnapshotFrame.TryEncode(original, out var again)); Assert.Equal(first, again);
        }
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 8;
        Assert.True(perFrame < original.Length * 8L + 256 * 1024, $"Encoding {original.Length}B allocated {perFrame}B");
    }

    [Theory]
    [InlineData("{\"capabilities\":{\"gameStatePropertyFrameV1\":true}}", true)]
    [InlineData("{\"capabilities\":[\"gameStatePropertyFrameV1\"]}", true)]
    [InlineData("{\"capabilities\":{\"gameStatePropertyFrameV1\":\"true\"}}", false)]
    [InlineData("{}", false)]
    public void CapabilityIsExplicitAndLegacyDefaultIsOff(string hello, bool expected)
    {
        using var document = JsonDocument.Parse(hello);
        var result = typeof(L12WebSocketServer).GetMethod("ReadProtocolCapabilities", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [document.RootElement])!;
        Assert.Equal(expected, result.GetType().GetProperty("GameStatePropertyFrameV1")!.GetValue(result));
    }

    [Fact]
    public async Task ActualWebSocketSendNegotiatesPerRecipientAndPlainRecoveryDoesNotChangePeers()
    {
        using var identity = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(identity.Path)!, "property-wire.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(manager, recorder, identity.Store, identity.Catalog);
        await server.StartAsync(0);
        var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1", Scheme = "ws", Path = "/ws" }.Uri;
        var tokens = Enumerable.Range(0, 3).Select(index =>
        {
            var result = identity.Store.Register($"pf{index}{Guid.NewGuid():N}"[..10], "Password123!");
            Assert.True(result.Success); return result.Token!;
        }).ToArray();
        await using var first = await Peer.Connect(endpoint, tokens[0], true);
        await using var legacy = await Peer.Connect(endpoint, tokens[1], false);
        await using var observer = await Peer.Connect(endpoint, tokens[2], true);
        var created = manager.CreateRoom(first.SessionId, new L12RoomOptions { Spectating = "public" });
        var roomCode = JsonSerializer.SerializeToElement(created[0].Payload, L12SnapshotJson.Options).GetProperty("roomCode").GetString()!;
        manager.JoinRoom(legacy.SessionId, roomCode); await manager.SetReadyAsync(first.SessionId, true);
        await manager.SetReadyAsync(legacy.SessionId, true); manager.SpectateRoom(observer.SessionId, roomCode);
        await first.Send(new { type = "syncState" });
        var a = await first.ReceiveGame(); var b = await legacy.ReceiveGame(); var c = await observer.ReceiveGame();
        Assert.Equal(L12PropertySnapshotFrame.WireType, a["type"]!.GetValue<string>());
        Assert.Equal("gameState", b["type"]!.GetValue<string>());
        Assert.Equal(L12PropertySnapshotFrame.WireType, c["type"]!.GetValue<string>());
        var player = Restore(a); var spectator = Restore(c);
        Assert.Equal(player["state"]!["matchId"]!.GetValue<string>(), spectator["state"]!["matchId"]!.GetValue<string>());
        Assert.NotNull(player["state"]!["players"]![0]!["hand"]);
        // The public projection omits hand entirely rather than publishing an empty private field.
        Assert.False(spectator["state"]!["players"]![0]!.AsObject().ContainsKey("hand"));
        foreach (var card in player["state"]!["players"]![0]!["hand"]!.AsArray())
            Assert.DoesNotContain(card!["instanceId"]!.GetValue<string>(), c.ToJsonString(), StringComparison.Ordinal);
        await first.Receive("recoveryComplete");
        await first.Send(new { type = "syncState", plainFull = true });
        Assert.Equal("gameState", (await first.ReceiveGame())["type"]!.GetValue<string>());
        Assert.Equal(L12PropertySnapshotFrame.WireType, (await observer.ReceiveGame())["type"]!.GetValue<string>());
        await first.Receive("recoveryComplete");
        await first.Send(new { type = "syncState" });
        Assert.Equal("gameState", (await first.ReceiveGame())["type"]!.GetValue<string>());
        await first.Receive("recoveryComplete");
    }

    private sealed class Peer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        internal Guid SessionId;
        internal static async Task<Peer> Connect(Uri endpoint, string token, bool property)
        {
            var peer = new Peer();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await peer._socket.ConnectAsync(endpoint, deadline.Token);
                await peer.Send(new { type = "hello", authToken = token, capabilities = new { gameStatePropertyFrameV1 = property } });
                var session = await peer.Receive("session"); peer.SessionId = session["sessionId"]!.GetValue<Guid>();
                Assert.Equal(property, session["capabilities"]![L12PropertySnapshotFrame.Capability]!.GetValue<bool>());
                await peer.Receive("recoveryComplete"); return peer;
            }
            catch { await peer.DisposeAsync(); throw; }
        }
        internal Task Send(object value) => _socket.SendAsync(L12SnapshotJson.SerializeToUtf8Bytes(value).AsMemory(),
            WebSocketMessageType.Text, true, CancellationToken.None).AsTask();
        internal Task<JsonNode> Receive(string type) => Read(value => value["type"]!.GetValue<string>() == type);
        internal Task<JsonNode> ReceiveGame() => Read(value => value["type"]!.GetValue<string>() is "gameState" or L12PropertySnapshotFrame.WireType);
        private async Task<JsonNode> Read(Func<JsonNode, bool> predicate)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                using var body = new MemoryStream(); var buffer = new byte[16384]; WebSocketReceiveResult read;
                do { read = await _socket.ReceiveAsync(buffer, deadline.Token); Assert.NotEqual(WebSocketMessageType.Close, read.MessageType);
                    body.Write(buffer, 0, read.Count); } while (!read.EndOfMessage);
                var value = JsonNode.Parse(body.ToArray())!; if (predicate(value)) return value;
            }
        }
        public ValueTask DisposeAsync() { _socket.Abort(); _socket.Dispose(); return ValueTask.CompletedTask; }
    }
}
