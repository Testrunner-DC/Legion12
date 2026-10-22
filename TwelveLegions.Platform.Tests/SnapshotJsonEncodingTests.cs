using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class SnapshotJsonEncodingTests
{
    private static readonly JsonSerializerOptions Legacy = new(JsonSerializerDefaults.Web);
    private const string Text = "嫦娥与迦具土；费用、兵力和卡牌效果。";
    private static JsonObject Card(string id) => new()
    {
        ["instanceId"] = id, ["cardId"] = "synthetic", ["name"] = "陵墓守卫",
        ["effectText"] = string.Concat(Enumerable.Repeat(Text, 20)),
        ["future"] = new JsonObject { ["原样保留"] = "中文<script>&\"'\u2028\u2029😀" },
    };
    private static JsonObject Frame(int revision)
    {
        var card = Card("synthetic-card");
        return new JsonObject
        {
            ["type"] = "gameState", ["requestId"] = "request-" + revision,
            ["state"] = new JsonObject
            {
                ["matchId"] = "synthetic-utf8", ["revision"] = revision,
                ["stateHash"] = "unchanged-rule-hash-" + revision,
                ["players"] = new JsonArray(new JsonObject
                { ["playerIndex"] = 0, ["hand"] = new JsonArray(Card("private-own")) },
                    new JsonObject { ["playerIndex"] = 1, ["hand"] = new JsonArray() }),
                ["recentEvents"] = new JsonArray(Enumerable.Range(1, 32).Select(index => (JsonNode)new JsonObject
                { ["sequence"] = index, ["text"] = Text, ["cards"] = new JsonArray(card.DeepClone()) }).ToArray()),
                ["lastAction"] = null, ["actionText"] = Text + revision,
            },
        };
    }
    private static byte[] Bytes(object value) => L12SnapshotJson.SerializeToUtf8Bytes(value);

    [Fact]
    public void SnapshotOptionsKeepSafeEscapingNamesNumbersAndAllValues()
    {
        var original = new { MixedName = "中文<script>&\"'\u2028\u2029😀", Limit = long.MaxValue,
            Zero = 0, Missing = (string?)null, Nested = new Dictionary<string, string> { ["中文键"] = Text } };
        var bytes = Bytes(original); var wire = Encoding.UTF8.GetString(bytes);
        Assert.Contains("中文", wire);
        foreach (var escape in new[] { "\\u003C", "\\u003E", "\\u0026", "\\u0027", "\\u2028", "\\u2029" })
            Assert.Contains(escape, wire);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(original, Legacy)), JsonNode.Parse(bytes)));
        Assert.True(L12SnapshotJson.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => L12SnapshotJson.Options.WriteIndented = true);
    }

    [Fact]
    public void DeltaUsesUtf8WithoutChangingTheProjectedValuesOrFailedSendBaseline()
    {
        var before = Frame(1); var after = Frame(2);
        var codec = new L12SnapshotWireCodec(); codec.SetDeltaEnabled(true, true, true);
        // A previously escaped baseline can be followed by a literal-UTF8 frame.
        codec.Prepare(new(JsonSerializer.SerializeToUtf8Bytes(before, Legacy), true, true)).Commit();
        var delta = codec.Prepare(new(Bytes(after), true, false));
        Assert.True(delta.IsDelta); Assert.Contains(Text, Encoding.UTF8.GetString(delta.Payload));
        Assert.True(JsonNode.DeepEquals(after, L12SnapshotWireCodec.ApplyDelta(before, delta.Payload)));
        // Do not commit this payload: the retry must still be based on revision one.
        var retry = codec.Prepare(new(Bytes(Frame(3)), true, false));
        Assert.Equal(1, JsonNode.Parse(retry.Payload)!["baseRevision"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(Frame(3), L12SnapshotWireCodec.ApplyDelta(before, retry.Payload)));
        Assert.DoesNotContain("private-opponent", Encoding.UTF8.GetString(retry.Payload));
    }

    [Fact]
    public void PresenceDeltaUsesTheSameEncodingAndRecipientOnlyValues()
    {
        JsonObject Presence(int revision) => new()
        {
            ["type"] = "presenceSnapshot", ["resource"] = "presence", ["epoch"] = "synthetic",
            ["revision"] = revision,
            ["items"] = new JsonArray(Enumerable.Range(0, 30).Select(i => (JsonNode)new JsonObject
            { ["accountId"] = "synthetic-" + i, ["displayName"] = Text,
                ["activity"] = i == 0 ? Text + revision : "空闲" }).ToArray()),
        };
        var codec = new L12PresenceSnapshotWireCodec(); codec.SetDeltaEnabled(true);
        codec.Prepare(new(Bytes(Presence(1)), false, false, true)).Commit();
        var delta = codec.Prepare(new(Bytes(Presence(2)), false, false, true));
        Assert.True(delta.IsDelta); Assert.Contains(Text, Encoding.UTF8.GetString(delta.Payload));
        var root = JsonNode.Parse(delta.Payload)!;
        Assert.Equal(Text + 2, root["upserts"]![0]!["activity"]!.GetValue<string>());
        Assert.Single(root["upserts"]!.AsArray()); Assert.Equal(30, root["order"]!.AsArray().Count);
        Assert.DoesNotContain("private-opponent", Encoding.UTF8.GetString(delta.Payload));
    }

    [Fact]
    public void PackedFullFrameRetainsUtf8AndEveryCardValue()
    {
        var original = Frame(1); var full = Bytes(original);
        Assert.True(L12CardSnapshotFrame.TryEncode(full, out var packed));
        Assert.Contains(Text, Encoding.UTF8.GetString(packed));
        Assert.True(JsonNode.DeepEquals(original, Restore(JsonNode.Parse(packed)!.AsObject())));
        Assert.True(packed.Length < full.Length * 9L / 10);
    }

    [Fact]
    public void OrdinaryDeltaCounterExactlyMatchesTheActualUnicodeWriter()
    {
        var after = Frame(2);
        var delta = new JsonObject { ["type"] = "gameStateDelta", ["matchId"] = "synthetic-utf8",
            ["baseRevision"] = 1, ["revision"] = 2, ["changes"] = new JsonArray(new JsonObject
            { ["path"] = new JsonArray("state", "actionText"), ["value"] = Text + "<>&\u2028", ["remove"] = false }),
            ["eventWindow"] = new JsonObject { ["text"] = Text },
            ["handSplices"] = new JsonObject { ["edits"] = new JsonArray(new JsonObject { ["playerIndex"] = 0 }) } };
        using var document = JsonDocument.Parse(Bytes(after));
        var measured = L12SnapshotWireCodec.MeasureOrdinaryDelta(delta, document.RootElement, delta["handSplices"]!.AsObject());
        var ordinary = delta.DeepClone().AsObject(); ordinary.Remove("handSplices");
        ordinary["changes"]!.AsArray().Add(new JsonObject { ["path"] = new JsonArray("state", "players", 0, "hand"),
            ["value"] = after["state"]!["players"]![0]!["hand"]!.DeepClone(), ["remove"] = false });
        Assert.Equal(Bytes(ordinary).LongLength, measured);
        Assert.True(measured < JsonSerializer.SerializeToUtf8Bytes(ordinary, Legacy).LongLength);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealSendQueueDeliversSnapshotsToLegacyAndNegotiatedClientsButLeavesControlBytesUnchanged(bool negotiated)
    {
        using var identity = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(identity.Path)!, "utf8-wire.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(identity.Catalog, recorder, identity.Store);
        await using var server = new L12WebSocketServer(manager, recorder, identity.Store, identity.Catalog);
        await server.StartAsync(0);
        var registration = identity.Store.Register("utf" + Guid.NewGuid().ToString("N")[..7], "Password123!");
        Assert.True(registration.Success);
        var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Scheme = "ws", Host = "127.0.0.1", Path = "/ws" }.Uri;
        using var socket = new ClientWebSocket(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(endpoint, deadline.Token);
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "hello", authToken = registration.Token,
            capabilities = new { requestIds = true, deltaGameState = negotiated, eventWindowDeltaV1 = negotiated,
                handArrayDeltaV1 = negotiated, gameStateCardFrameV1 = negotiated, presenceDeltaV1 = negotiated } }, Legacy).AsMemory(),
            WebSocketMessageType.Text, true, deadline.Token);
        var sessionBytes = await Receive(socket, "session", deadline.Token);
        var id = JsonNode.Parse(sessionBytes)!["sessionId"]!.GetValue<Guid>();
        _ = await Receive(socket, "recoveryComplete", deadline.Token);
        var method = typeof(L12WebSocketServer).GetMethod("SendManyAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        async Task Send(OutgoingMessage message)
            => await (Task)method.Invoke(server, new object?[] { new[] { message }, deadline.Token, null, Guid.Empty })!;
        var control = new { type = "syntheticControl", message = Text };
        await Send(new OutgoingMessage(id, control));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(control, Legacy), await Receive(socket, "syntheticControl", deadline.Token));
        var first = Frame(1);
        await Send(new OutgoingMessage(id, first, IsGameState: true, ForceFullGameState: true));
        var firstBytes = await Receive(socket, negotiated ? L12CardSnapshotFrame.WireType : "gameState", deadline.Token);
        Assert.Contains(Text, Encoding.UTF8.GetString(firstBytes));
        var rebuilt = negotiated ? Restore(JsonNode.Parse(firstBytes)!.AsObject()) : JsonNode.Parse(firstBytes)!.AsObject();
        Assert.True(JsonNode.DeepEquals(first, rebuilt));
        var next = Frame(2);
        await Send(new OutgoingMessage(id, next, IsGameState: true));
        var nextBytes = await Receive(socket, negotiated ? "gameStateDelta" : "gameState", deadline.Token);
        Assert.Contains(Text, Encoding.UTF8.GetString(nextBytes));
        rebuilt = negotiated ? L12SnapshotWireCodec.ApplyDelta(rebuilt, nextBytes) : JsonNode.Parse(nextBytes)!.AsObject();
        Assert.True(JsonNode.DeepEquals(next, rebuilt));
        // A forced-full recovery remains a full frame, with the same privacy projection.
        await Send(new OutgoingMessage(id, Frame(3), IsGameState: true, ForceFullGameState: true));
        var recovery = await Receive(socket, negotiated ? L12CardSnapshotFrame.WireType : "gameState", deadline.Token);
        Assert.True(JsonNode.DeepEquals(Frame(3), negotiated ? Restore(JsonNode.Parse(recovery)!.AsObject()) : JsonNode.Parse(recovery)));
        socket.Abort();
    }

    private static async Task<byte[]> Receive(ClientWebSocket socket, string type, CancellationToken token)
    {
        while (true)
        {
            using var message = new MemoryStream(); var buffer = new byte[16 * 1024]; WebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer, token);
                Assert.Equal(WebSocketMessageType.Text, part.MessageType);
                await message.WriteAsync(buffer.AsMemory(0, part.Count), token);
            } while (!part.EndOfMessage);
            var bytes = message.ToArray();
            if (JsonNode.Parse(bytes)?["type"]?.GetValue<string>() == type) return bytes;
        }
    }
    private static JsonObject Restore(JsonObject packed)
    {
        var result = packed["payload"]!.DeepClone().AsObject(); var cards = packed["cardSnapshots"]!.AsArray();
        var events = result["state"]!["recentEvents"]!.AsArray();
        for (var i = 0; i < events.Count; i++) events[i]!["cards"] = new JsonArray(packed["eventCardRefs"]![i]!.AsArray()
            .Select(id => cards[id!.GetValue<int>()]!.DeepClone()).ToArray());
        if (result["state"]!["lastAction"] is JsonObject action) action["cards"] = new JsonArray(packed["lastActionCardRefs"]!.AsArray()
            .Select(id => cards[id!.GetValue<int>()]!.DeepClone()).ToArray());
        return result;
    }
}
