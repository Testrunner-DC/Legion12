using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

public sealed class PresenceOrderReuseTests(ITestOutputHelper output)
{
    [Fact]
    public void NegotiatedStableOrderReusesBaselineAndPreservesAllEntryValues()
    {
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(true, true);
        var initial = Snapshot(10, Entries());
        codec.Prepare(Queue(initial)).Commit();
        var changed = Entries();
        changed[17]["activity"] = "inRoom";
        var current = Snapshot(11, changed);
        var wire = codec.Prepare(Queue(current));
        Assert.True(wire.IsDelta);
        var delta = JsonNode.Parse(wire.Payload)!.AsObject();
        Assert.True(delta["orderFromBaseline"]?.GetValue<bool>() == true,
            "Negotiated stable order must not retransmit the whole account-id vector.");
        Assert.False(delta.ContainsKey("order"));
        Assert.Single(delta["upserts"]!.AsArray());
        Assert.Equal(10, delta["baseRevision"]!.GetValue<long>());
        Assert.Equal(11, delta["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(changed[17], delta["upserts"]![0]));
        output.WriteLine("stable-order current-full={0} wire={1}", current.Length, wire.Payload.Length);
    }

    [Fact]
    public void RemovingOfflineMembersReusesTheRemainingBaselineOrder()
    {
        var codec = new L12PresenceSnapshotWireCodec(); codec.SetDeltaEnabled(true, true);
        codec.Prepare(Queue(Snapshot(1, Entries()))).Commit();
        var changed = Entries(); changed.RemoveAt(4); changed.RemoveAt(1);
        var wire = codec.Prepare(Queue(Snapshot(2, changed)));
        Assert.True(wire.IsDelta);
        var delta = JsonNode.Parse(wire.Payload)!.AsObject();
        Assert.False(delta.ContainsKey("order"), "Removal-preserving order must not retransmit the remaining identity vector.");
        Assert.True(delta["orderFromBaseline"]!.GetValue<bool>());
        Assert.Equal(2, delta["removedAccountIds"]!.AsArray().Count);
        Assert.Empty(delta["upserts"]!.AsArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BothCapabilitiesAreRequiredAndColdConnectionAlwaysStartsFull(bool deltaEnabled, bool reuseOrder)
    {
        var codec = new L12PresenceSnapshotWireCodec();
        codec.SetDeltaEnabled(deltaEnabled, reuseOrder);
        var initial = codec.Prepare(Queue(Snapshot(1, Entries())));
        Assert.False(initial.IsDelta); initial.Commit();
        var changed = Entries(); changed[1]["activity"] = "inRoom";
        var wire = codec.Prepare(Queue(Snapshot(2, changed)));
        Assert.Equal(deltaEnabled, wire.IsDelta);
        var root = JsonNode.Parse(wire.Payload)!.AsObject();
        if (!deltaEnabled) Assert.Equal("presenceSnapshot", root["type"]!.GetValue<string>());
        else if (reuseOrder) Assert.True(root["orderFromBaseline"]!.GetValue<bool>());
        else
        {
            Assert.False(root.ContainsKey("orderFromBaseline"));
            Assert.Equal(70, root["order"]!.AsArray().Count);
        }
    }

    [Theory]
    [InlineData("reorder")]
    [InlineData("add")]
    public void NewMembersOrReorderingStillSendTheExactCompleteOrder(string scenario)
    {
        var codec = new L12PresenceSnapshotWireCodec(); codec.SetDeltaEnabled(true, true);
        codec.Prepare(Queue(Snapshot(1, Entries()))).Commit();
        var changed = Entries();
        if (scenario == "reorder") (changed[1], changed[2]) = (changed[2], changed[1]);
        else { var extra = Entries(71)[70]; changed.Insert(2, extra); }
        var wire = codec.Prepare(Queue(Snapshot(2, changed)));
        Assert.True(wire.IsDelta);
        var root = JsonNode.Parse(wire.Payload)!.AsObject();
        Assert.False(root.ContainsKey("orderFromBaseline"));
        Assert.Equal(changed.Select(item => item["accountId"]!.GetValue<string>()),
            root["order"]!.AsArray().Select(item => item!.GetValue<string>()));
    }

    [Fact]
    public void FailedSendDoesNotAdvanceReusedOrderBaselineAndSyncStillForcesFull()
    {
        var codec = new L12PresenceSnapshotWireCodec(); codec.SetDeltaEnabled(true, true);
        codec.Prepare(Queue(Snapshot(1, Entries()))).Commit();
        var changed = Entries(); changed[1]["activity"] = "inRoom";
        var failed = codec.Prepare(Queue(Snapshot(2, changed))); // no Commit: real sender failed
        Assert.True(failed.IsDelta);
        changed[2]["activity"] = "inRoom";
        var retry = codec.Prepare(Queue(Snapshot(3, changed)));
        var delta = JsonNode.Parse(retry.Payload)!;
        Assert.Equal(1, delta["baseRevision"]!.GetValue<long>());
        Assert.Equal(2, delta["upserts"]!.AsArray().Count);
        Assert.True(delta["orderFromBaseline"]!.GetValue<bool>());
        var forced = codec.Prepare(Queue(Snapshot(4, changed), forceFull: true));
        Assert.False(forced.IsDelta); forced.Commit();
        var next = JsonNode.Parse(codec.Prepare(Queue(Snapshot(5, changed))).Payload)!;
        Assert.Equal(4, next["baseRevision"]!.GetValue<long>());
    }

    [Fact]
    public void FailedRemovalSendRetainsAllUnsentRemovalsOnTheNextPayload()
    {
        var codec = new L12PresenceSnapshotWireCodec(); codec.SetDeltaEnabled(true, true);
        codec.Prepare(Queue(Snapshot(1, Entries()))).Commit();
        var changed = Entries(); changed.RemoveAt(4);
        var failed = codec.Prepare(Queue(Snapshot(2, changed))); // deliberately not committed
        Assert.True(failed.IsDelta);
        changed.RemoveAt(1); changed[7]["activity"] = "inRoom";
        var retry = JsonNode.Parse(codec.Prepare(Queue(Snapshot(3, changed))).Payload)!;
        Assert.Equal(1, retry["baseRevision"]!.GetValue<long>());
        Assert.Equal(new[] { 1.ToString("x32"), 4.ToString("x32") },
            retry["removedAccountIds"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Single(retry["upserts"]!.AsArray());
        Assert.True(retry["orderFromBaseline"]!.GetValue<bool>());
    }

    [Fact]
    public void RenegotiationRejectsLateCommitsAndEpochMismatchRemainsFull()
    {
        var codec = new L12PresenceSnapshotWireCodec(); codec.SetDeltaEnabled(true, true);
        var retired = codec.Prepare(Queue(Snapshot(1, Entries())));
        codec.SetDeltaEnabled(true, false);
        retired.Commit();
        var cold = codec.Prepare(Queue(Snapshot(2, Entries())));
        Assert.False(cold.IsDelta); cold.Commit();
        var legacy = JsonNode.Parse(codec.Prepare(Queue(Snapshot(3, Entries()))).Payload)!.AsObject();
        Assert.True(legacy.ContainsKey("order")); Assert.False(legacy.ContainsKey("orderFromBaseline"));
        codec.SetDeltaEnabled(true, true);
        codec.Prepare(Queue(Snapshot(4, Entries()))).Commit();
        Assert.False(codec.Prepare(Queue(Snapshot(5, Entries(), "different-epoch"))).IsDelta);
    }

    [Fact]
    public void ReusedOrderCannotBorrowAnotherRecipientsValuesOrBaseline()
    {
        var first = new L12PresenceSnapshotWireCodec(); var second = new L12PresenceSnapshotWireCodec();
        first.SetDeltaEnabled(true, true); second.SetDeltaEnabled(true, true);
        first.Prepare(Queue(Snapshot(1, Entries(marker: "viewer-a-only")))).Commit();
        second.Prepare(Queue(Snapshot(1, Entries(marker: "viewer-b-only")))).Commit();
        foreach (var (codec, marker, other) in new[] { (first,"viewer-a-only","viewer-b-only"), (second,"viewer-b-only","viewer-a-only") })
        {
            var changed = Entries(marker: marker); changed[2]["activity"] = "inRoom";
            var text = Encoding.UTF8.GetString(codec.Prepare(Queue(Snapshot(2, changed))).Payload);
            Assert.Contains(marker, text); Assert.DoesNotContain(other, text);
            Assert.True(JsonNode.Parse(text)!["orderFromBaseline"]!.GetValue<bool>());
        }
    }

    [Theory]
    [InlineData("{\"presenceDeltaV1\":true,\"presenceOrderReuseV1\":true}", true, true)]
    [InlineData("[\"presenceDeltaV1\",\"presenceOrderReuseV1\"]", true, true)]
    [InlineData("{\"presenceDeltaV1\":true}", true, false)]
    [InlineData("{\"presenceOrderReuseV1\":true}", false, true)]
    [InlineData("{\"presenceDeltaV1\":true,\"presenceOrderReuseV1\":\"true\"}", true, false)]
    public void ObjectAndArrayNegotiationAreExplicitAndDoNotTreatStringsAsBooleans(string capabilities, bool delta, bool reuse)
    {
        using var document = JsonDocument.Parse("{\"capabilities\":"+capabilities+"}");
        var method = typeof(L12WebSocketServer).GetMethod("ReadProtocolCapabilities", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parsed = method.Invoke(null, [document.RootElement])!;
        Assert.Equal(delta, (bool)parsed.GetType().GetProperty("PresenceDeltaV1")!.GetValue(parsed)!);
        Assert.Equal(reuse, (bool)parsed.GetType().GetProperty("PresenceOrderReuseV1")!.GetValue(parsed)!);
    }

    [Fact]
    public void SixtyStableUpdatesAndRemovalsAttributeSavingsToOnlyTheRepeatedOrder()
    {
        var legacy = new L12PresenceSnapshotWireCodec(); var reused = new L12PresenceSnapshotWireCodec();
        legacy.SetDeltaEnabled(true); reused.SetDeltaEnabled(true, true);
        var entries = Entries(); var initial = Snapshot(1, entries);
        legacy.Prepare(Queue(initial)).Commit(); reused.Prepare(Queue(initial)).Commit();
        long legacyBytes = 0, reusedBytes = 0, exactOrderSavings = 0, legacyAllocated = 0, reusedAllocated = 0;
        var fixtures = new List<object>();
        // Warm both serialization paths before allocation accounting. Neither
        // preparation commits: the actual sequence still starts at revision 1.
        var warm = Snapshot(2, entries);
        legacy.Prepare(Queue(warm)); reused.Prepare(Queue(warm));
        for (var index = 0; index < 60; index++)
        {
            var before = Snapshot(index+1, entries);
            if (index < 30) entries[index]["activity"] = "inRoom";
            else entries.RemoveAt(0);
            var current = Snapshot(index+2, entries);
            var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            var oldWire = legacy.Prepare(Queue(current));
            legacyAllocated += GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
            allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            var newWire = reused.Prepare(Queue(current));
            reusedAllocated += GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
            Assert.True(oldWire.IsDelta); Assert.True(newWire.IsDelta);
            var oldDelta = JsonNode.Parse(oldWire.Payload)!.AsObject();
            var newDelta = JsonNode.Parse(newWire.Payload)!.AsObject();
            var removedBytes = Encoding.UTF8.GetByteCount("\"order\":"+oldDelta["order"]!.ToJsonString())
                - Encoding.UTF8.GetByteCount("\"orderFromBaseline\":true");
            Assert.Equal(removedBytes, oldWire.Payload.Length-newWire.Payload.Length);
            oldDelta.Remove("order"); newDelta.Remove("orderFromBaseline");
            Assert.True(JsonNode.DeepEquals(oldDelta, newDelta), "No other field may disappear or change.");
            legacyBytes += oldWire.Payload.Length; reusedBytes += newWire.Payload.Length; exactOrderSavings += removedBytes;
            fixtures.Add(new { before=Convert.ToBase64String(before), wire=Convert.ToBase64String(newWire.Payload), after=Convert.ToBase64String(current) });
            oldWire.Commit(); newWire.Commit();
        }
        Assert.Equal(exactOrderSavings, legacyBytes-reusedBytes);
        Assert.True(reusedBytes < legacyBytes);
        output.WriteLine("order-reuse initial-entries=70 updates=60 legacy={0} reused={1} exact-order-saving={2} legacy-allocated={3} reused-allocated={4}", legacyBytes, reusedBytes, exactOrderSavings, legacyAllocated, reusedAllocated);
        var target = Environment.GetEnvironmentVariable("L12_PRESENCE_ORDER_FIXTURES");
        if (!string.IsNullOrEmpty(target))
        {
            Assert.StartsWith(Path.GetFullPath("D:/GPT/Legion12/artifacts/stage-goals-20261023/presence-order-reuse") + Path.DirectorySeparatorChar,
                Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("actual-wire-fixtures.json", Path.GetFileName(target));
            Assert.False(File.Exists(target));
            var serverSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(L12WebSocketServer).Assembly.Location)));
            File.WriteAllBytes(target, JsonSerializer.SerializeToUtf8Bytes(new { syntheticOnly=true, serverDllSha256=serverSha,
                legacyBytes, reusedBytes, exactOrderSavings, legacyAllocated, reusedAllocated, rows=fixtures }));
        }
    }

    private static L12QueuedPayload Queue(byte[] payload, bool forceFull = false)
        => new(payload, false, false, true, forceFull);

    private static byte[] Snapshot(long revision, IReadOnlyList<JsonObject> entries, string epoch = "presence-order-test")
        => JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["type"] = "presenceSnapshot", ["resource"] = "presence", ["epoch"] = epoch,
            ["revision"] = revision,
            ["items"] = new JsonArray(entries.Select(entry => entry.DeepClone()).ToArray()),
        });

    private static List<JsonObject> Entries(int count = 70, string marker = "recipient-only")
        => Enumerable.Range(0, count).Select(index => new JsonObject
        {
            ["accountId"] = index.ToString("x32"), ["username"] = $"synthetic-{index:D2}",
            ["online"] = true, ["activity"] = "idle", ["roomCode"] = null,
            ["canInvite"] = true, ["canSpectate"] = false, ["actionReason"] = null,
            ["friendStatus"] = marker, ["friendDirection"] = "none",
        }).ToList();
}
