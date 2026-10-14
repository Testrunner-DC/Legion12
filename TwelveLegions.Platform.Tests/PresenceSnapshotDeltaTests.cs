using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PresenceSnapshotDeltaTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private readonly ITestOutputHelper _output;

    public PresenceSnapshotDeltaTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("[]")]
    public void MalformedRevisionFallsBackWithoutThrowingOrPoisoningGoodBaseline(string revision)
    {
        var malformed = Encoding.UTF8.GetBytes("{\"type\":\"presenceSnapshot\",\"resource\":\"presence\","
            + "\"epoch\":\"epoch-a\",\"revision\":" + revision + ",\"items\":[]}");
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        codec.Prepare(Queue(malformed)).Commit();
        var good = codec.Prepare(Queue(Snapshot(2, Entries(12))));
        Assert.False(good.IsDelta);
        good.Commit();

        var invalidCurrent = codec.Prepare(Queue(malformed));
        Assert.False(invalidCurrent.IsDelta);
        Assert.Same(malformed, invalidCurrent.Payload);
        invalidCurrent.Commit();
        var changed = Entries(12);
        changed[0]["activity"] = "room";
        var next = codec.Prepare(Queue(Snapshot(3, changed)));
        Assert.True(next.IsDelta);
        Assert.Equal(2, Root(next)["baseRevision"]!.GetValue<long>());
    }

    [Fact]
    public void SeventyEntryPayloadMeasurementsPreserveExactProjectedSnapshot()
    {
        foreach (var scenario in new[] { "revision-only", "one-change", "add-remove-reorder", "all-change" })
        {
            var codec = new L12PresenceSnapshotWireCodec();
            codec.SetDeltaEnabled(true);
            var beforeEntries = Entries(70, "recipient-only");
            var before = Snapshot(10, beforeEntries);
            codec.Prepare(Queue(before)).Commit();
            var afterEntries = Entries(70, "recipient-only");
            if (scenario == "one-change") afterEntries[17]["activity"] = "room";
            if (scenario == "add-remove-reorder")
            {
                afterEntries.RemoveAt(11);
                var added = (JsonObject)beforeEntries[11].DeepClone();
                added["accountId"] = "new-account";
                afterEntries.Insert(3, added);
                (afterEntries[20], afterEntries[21]) = (afterEntries[21], afterEntries[20]);
            }
            if (scenario == "all-change")
                foreach (var item in afterEntries) item["activity"] = "playing";
            var after = Snapshot(11, afterEntries);
            var prepared = codec.Prepare(Queue(after));
            JsonNode reconstructed;
            if (prepared.IsDelta)
            {
                var delta = Root(prepared);
                var items = JsonNode.Parse(before)!["items"]!.AsArray()
                    .ToDictionary(item => item!["accountId"]!.GetValue<string>(), item => item!.DeepClone());
                foreach (var removed in delta["removedAccountIds"]!.AsArray())
                    Assert.True(items.Remove(removed!.GetValue<string>()));
                foreach (var upsert in delta["upserts"]!.AsArray())
                    items[upsert!["accountId"]!.GetValue<string>()] = upsert.DeepClone();
                reconstructed = new JsonObject
                {
                    ["type"] = "presenceSnapshot", ["resource"] = "presence",
                    ["epoch"] = delta["epoch"]!.DeepClone(), ["revision"] = delta["revision"]!.DeepClone(),
                    ["items"] = new JsonArray(delta["order"]!.AsArray()
                        .Select(id => items[id!.GetValue<string>()]).ToArray()),
                };
            }
            else reconstructed = JsonNode.Parse(prepared.Payload)!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(after), reconstructed));
            Assert.Equal(scenario != "all-change", prepared.IsDelta);
            Assert.DoesNotContain("other-recipient", Encoding.UTF8.GetString(prepared.Payload));
            _output.WriteLine("presence-bytes scenario={0} entries=70 full={1} wire={2} delta={3}",
                scenario, after.Length, prepared.Payload.Length, prepared.IsDelta);
        }
    }

    [Fact]
    public void NegotiatedConnectionUsesOrderedEntryDeltaAfterSuccessfulFull()
    {
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var before = Snapshot(7, Entries(12));
        var initial = codec.Prepare(Queue(before));
        Assert.False(initial.IsDelta);
        initial.Commit();

        var entries = Entries(12);
        entries[5]["activity"] = "room";
        (entries[2], entries[3]) = (entries[3], entries[2]);
        var current = Snapshot(11, entries);
        var wire = codec.Prepare(Queue(current));

        Assert.True(wire.IsDelta);
        var delta = JsonNode.Parse(wire.Payload)!.AsObject();
        Assert.Equal("presenceDelta", delta["type"]!.GetValue<string>());
        Assert.Equal(7, delta["baseRevision"]!.GetValue<long>());
        Assert.Equal(11, delta["revision"]!.GetValue<long>());
        Assert.Empty(delta["removedAccountIds"]!.AsArray());
        Assert.Single(delta["upserts"]!.AsArray());
        Assert.Equal(entries.Select(item => item["accountId"]!.GetValue<string>()),
            delta["order"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.True(wire.Payload.Length < current.Length);
    }

    [Fact]
    public void FailedSendDoesNotAdvanceBaselineAndForcedFullDoes()
    {
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var initial = codec.Prepare(Queue(Snapshot(1, Entries(12))));
        initial.Commit();

        var revision2 = Entries(12);
        revision2[0]["activity"] = "matching";
        var failed = codec.Prepare(Queue(Snapshot(2, revision2)));
        Assert.True(failed.IsDelta);
        Assert.Equal(1, Root(failed)["baseRevision"]!.GetValue<long>());

        var revision3 = Entries(12);
        revision3[1]["activity"] = "room";
        var retry = codec.Prepare(Queue(Snapshot(3, revision3)));
        Assert.True(retry.IsDelta);
        Assert.Equal(1, Root(retry)["baseRevision"]!.GetValue<long>());

        var forced = codec.Prepare(Queue(Snapshot(4, revision3), forceFull: true));
        Assert.False(forced.IsDelta);
        forced.Commit();
        revision3[2]["activity"] = "playing";
        var afterForced = codec.Prepare(Queue(Snapshot(5, revision3)));
        Assert.True(afterForced.IsDelta);
        Assert.Equal(4, Root(afterForced)["baseRevision"]!.GetValue<long>());
    }

    [Fact]
    public void LegacyColdMalformedAndLargerDeltaAllFallBackToFull()
    {
        var legacy = new L12PresenceSnapshotWireCodec();
        Assert.False(legacy.Prepare(Queue(Snapshot(1, Entries(2)))).IsDelta);

        var cold = new L12PresenceSnapshotWireCodec();
        cold.SetDeltaEnabled(true);
        Assert.False(cold.Prepare(Queue(Snapshot(1, Entries(2)))).IsDelta);

        var malformed = new L12PresenceSnapshotWireCodec();
        malformed.SetDeltaEnabled(true);
        var bad = malformed.Prepare(new L12QueuedPayload(
            JsonSerializer.SerializeToUtf8Bytes(new { type = "presenceSnapshot", resource = "presence",
                epoch = "epoch-a", revision = 1, items = new[] { new { username = "missing-id" } } }),
            false, false, true));
        bad.Commit();
        Assert.False(malformed.Prepare(Queue(Snapshot(2, Entries(2)))).IsDelta);

        var small = new L12PresenceSnapshotWireCodec();
        small.SetDeltaEnabled(true);
        var first = small.Prepare(Queue(Snapshot(1, Entries(1))));
        first.Commit();
        var changed = Entries(1);
        changed[0]["activity"] = "playing";
        Assert.False(small.Prepare(Queue(Snapshot(2, changed))).IsDelta);
    }

    [Fact]
    public void PersonalizedBaselinesNeverCrossConnections()
    {
        var first = new L12PresenceSnapshotWireCodec();
        var second = new L12PresenceSnapshotWireCodec();
        first.SetDeltaEnabled(true);
        second.SetDeltaEnabled(true);
        var firstItems = Entries(12, "first-only");
        var secondItems = Entries(12, "second-only");
        first.Prepare(Queue(Snapshot(1, firstItems))).Commit();
        second.Prepare(Queue(Snapshot(1, secondItems))).Commit();
        firstItems[0]["activity"] = "room";
        secondItems[0]["activity"] = "matching";

        var firstWire = first.Prepare(Queue(Snapshot(2, firstItems)));
        var secondWire = second.Prepare(Queue(Snapshot(2, secondItems)));
        Assert.True(firstWire.IsDelta);
        Assert.True(secondWire.IsDelta);
        Assert.Contains("first-only", System.Text.Encoding.UTF8.GetString(firstWire.Payload));
        Assert.DoesNotContain("second-only", System.Text.Encoding.UTF8.GetString(firstWire.Payload));
        Assert.Contains("second-only", System.Text.Encoding.UTF8.GetString(secondWire.Payload));
        Assert.DoesNotContain("first-only", System.Text.Encoding.UTF8.GetString(secondWire.Payload));
    }

    [Fact]
    public void EpochOrNonIncreasingRevisionForcesFullAndReestablishesBaseline()
    {
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var first = codec.Prepare(Queue(Snapshot(8, Entries(12))));
        first.Commit();
        Assert.False(codec.Prepare(Queue(Snapshot(8, Entries(12)))).IsDelta);
        var restarted = codec.Prepare(Queue(Snapshot(1, Entries(12), "epoch-b")));
        Assert.False(restarted.IsDelta);
        restarted.Commit();
        var changed = Entries(12);
        changed[4]["activity"] = "room";
        var afterRestart = codec.Prepare(Queue(Snapshot(2, changed, "epoch-b")));
        Assert.True(afterRestart.IsDelta);
        Assert.Equal(1, Root(afterRestart)["baseRevision"]!.GetValue<long>());
    }

    [Fact]
    public void RepeatedSuccessfulHelloConfigurationClearsAnEqualCapabilityBaseline()
    {
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(true);
        var initial = codec.Prepare(Queue(Snapshot(1, Entries(12))));
        initial.Commit();
        var changed = Entries(12);
        changed[0]["activity"] = "room";
        Assert.True(codec.Prepare(Queue(Snapshot(2, changed))).IsDelta);
        codec.SetDeltaEnabled(true);
        Assert.False(codec.Prepare(Queue(Snapshot(2, changed))).IsDelta);

        var stale = codec.Prepare(Queue(Snapshot(3, changed)));
        codec.SetDeltaEnabled(true);
        stale.Commit();
        Assert.False(codec.Prepare(Queue(Snapshot(4, changed))).IsDelta);
    }

    [Fact]
    public async Task RealSendFaultAndQueueRejectionNeverPublishAPresenceBaseline()
    {
        var failedCodec = new L12PresenceSnapshotWireCodec();
        failedCodec.SetDeltaEnabled(true);
        var sendAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var failing = new L12OutboundConnection((payload, _) =>
        {
            var prepared = failedCodec.Prepare(Assert.IsType<L12QueuedPayload>(payload));
            sendAttempted.TrySetResult();
            throw new IOException($"synthetic send failure before commit: {prepared.Payload.Length}");
        }))
        {
            Assert.True(failing.TryEnqueue(Queue(Snapshot(1, Entries(12)))));
            await sendAttempted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.False(failedCodec.Prepare(Queue(Snapshot(2, Entries(12)))).IsDelta);

        var queueCodec = new L12PresenceSnapshotWireCodec();
        queueCodec.SetDeltaEnabled(true);
        var initial = queueCodec.Prepare(Queue(Snapshot(1, Entries(12))));
        initial.Commit();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparedCount = 0;
        await using var blocked = new L12OutboundConnection(async (payload, _) =>
        {
            if (payload is L12QueuedPayload queued)
            {
                Interlocked.Increment(ref preparedCount);
                queueCodec.Prepare(queued).Commit();
            }
            started.TrySetResult();
            await release.Task;
        });
        Assert.True(blocked.TryEnqueue(new object()));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var index = 0; index < L12OutboundConnection.MaximumQueuedMessages; index++)
            Assert.True(blocked.TryEnqueue(new object()));
        var rejectedEntries = Entries(12);
        rejectedEntries[0]["activity"] = "room";
        Assert.False(blocked.TryEnqueue(Queue(Snapshot(2, rejectedEntries))));
        Assert.Equal(0, Volatile.Read(ref preparedCount));
        release.TrySetResult();
        await blocked.CompleteAsync();
        var next = queueCodec.Prepare(Queue(Snapshot(3, rejectedEntries)));
        Assert.True(next.IsDelta);
        Assert.Equal(1, Root(next)["baseRevision"]!.GetValue<long>());
    }

    [Fact]
    public async Task LoopbackNegotiationLegacySyncAndReconnectKeepFullFallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-presence-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        var sockets = new List<ClientWebSocket>();
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var accounts = Enumerable.Range(0, 9)
                .Select(index => store.Register($"presence{index:D2}", "password-123"))
                .ToArray();
            Assert.All(accounts, account => Assert.True(account.Success));
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            var uri = new UriBuilder(Assert.Single(server.Addresses)) { Scheme = "ws", Path = "/ws" }.Uri;

            var negotiated = await ConnectAsync(uri, accounts[0].Token!, presenceDelta: true);
            sockets.Add(negotiated.Socket);
            Assert.True(negotiated.Session["capabilities"]!["presenceDeltaV1"]!.GetValue<bool>());
            Assert.Equal("presenceSnapshot", negotiated.InitialPresence["type"]!.GetValue<string>());
            var legacy = await ConnectAsync(uri, accounts[1].Token!, presenceDelta: false);
            sockets.Add(legacy.Socket);
            Assert.False(legacy.Session["capabilities"]!["presenceDeltaV1"]!.GetValue<bool>());
            for (var index = 2; index < accounts.Length; index++)
            {
                var peer = await ConnectAsync(uri, accounts[index].Token!, presenceDelta: false);
                sockets.Add(peer.Socket);
            }

            await SendAsync(negotiated.Socket, new { type = "syncPresence" });
            var negotiatedFull = await ReceivePresenceAsync(negotiated.Socket,
                message => message["type"]?.GetValue<string>() == "presenceSnapshot"
                    && message["items"]!.AsArray().Count == accounts.Length);
            var fullRevision = negotiatedFull["revision"]!.GetValue<long>();
            await SendAsync(legacy.Socket, new { type = "syncPresence" });
            var legacyFull = await ReceivePresenceAsync(legacy.Socket,
                message => message["type"]?.GetValue<string>() == "presenceSnapshot"
                    && message["items"]!.AsArray().Count == accounts.Length);

            await SendAsync(sockets[2], new { type = "createRoom" });
            var delta = await ReceivePresenceAsync(negotiated.Socket,
                message => message["revision"]!.GetValue<long>() > fullRevision);
            Assert.Equal("presenceDelta", delta["type"]!.GetValue<string>());
            Assert.Equal(fullRevision, delta["baseRevision"]!.GetValue<long>());
            Assert.NotEmpty(delta["upserts"]!.AsArray());
            var legacyRefresh = await ReceivePresenceAsync(legacy.Socket,
                message => message["revision"]!.GetValue<long>() > legacyFull["revision"]!.GetValue<long>());
            Assert.Equal("presenceSnapshot", legacyRefresh["type"]!.GetValue<string>());

            await SendAsync(negotiated.Socket, new { type = "syncPresence" });
            Assert.Equal("presenceSnapshot", (await ReceivePresenceAsync(negotiated.Socket,
                message => message["type"]?.GetValue<string>() == "presenceSnapshot"))["type"]!.GetValue<string>());

            negotiated.Socket.Abort();
            var reconnected = await ConnectAsync(uri, accounts[0].Token!, presenceDelta: true);
            sockets.Add(reconnected.Socket);
            Assert.Equal("presenceSnapshot", reconnected.InitialPresence["type"]!.GetValue<string>());
        }
        finally
        {
            foreach (var socket in sockets) socket.Abort();
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static L12QueuedPayload Queue(byte[] payload, bool forceFull = false)
        => new(payload, false, false, true, forceFull);

    private static JsonObject Root(L12PreparedWirePayload payload)
        => JsonNode.Parse(payload.Payload)!.AsObject();

    private static byte[] Snapshot(long revision, IReadOnlyList<JsonObject> entries, string epoch = "epoch-a")
        => JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["type"] = "presenceSnapshot",
            ["resource"] = "presence",
            ["epoch"] = epoch,
            ["revision"] = revision,
            ["items"] = new JsonArray(entries.Select(entry => entry.DeepClone()).ToArray()),
        });

    private static List<JsonObject> Entries(int count, string marker = "shared")
        => Enumerable.Range(0, count).Select(index => new JsonObject
        {
            ["accountId"] = $"account-{index:D2}",
            ["username"] = $"player-{index:D2}",
            ["online"] = true,
            ["activity"] = "idle",
            ["roomCode"] = null,
            ["canInvite"] = true,
            ["canSpectate"] = false,
            ["actionReason"] = new string('x', 80),
            ["friendStatus"] = marker,
            ["friendDirection"] = "none",
        }).ToList();

    private static async Task<(ClientWebSocket Socket, JsonObject Session, JsonObject InitialPresence)> ConnectAsync(
        Uri uri, string token, bool presenceDelta)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, CancellationToken.None);
        await SendAsync(socket, new
        {
            type = "hello",
            authToken = token,
            capabilities = new { requestIds = true, deltaGameState = true, presenceDeltaV1 = presenceDelta },
        });
        var session = await ReceiveTypeAsync(socket, "session");
        var presence = await ReceivePresenceAsync(socket);
        return (socket, session, presence);
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonObject> ReceiveTypeAsync(ClientWebSocket socket, string type)
        => await ReceiveAsync(socket, message => message["type"]?.GetValue<string>() == type);

    private static async Task<JsonObject> ReceivePresenceAsync(ClientWebSocket socket,
        Func<JsonObject, bool>? predicate = null)
        => await ReceiveAsync(socket, message => (message["type"]?.GetValue<string>() is "presenceSnapshot" or "presenceDelta")
            && (predicate is null || predicate(message)));

    private static async Task<JsonObject> ReceiveAsync(ClientWebSocket socket, Func<JsonObject, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var buffer = new byte[32 * 1024];
        while (true)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("WebSocket closed before the expected presence message.");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var message = JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()))!.AsObject();
            if (predicate(message)) return message;
        }
    }
}
