using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

// Numeric-only qualification, not a traffic distribution or concurrent-room SLA.
public sealed class SnapshotWireFieldAttributionTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void ActualRecipientProjectionAndCodecAccountForEveryWireByte(int viewer)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var game = new L12GameEngine(catalog, "synthetic-field-attribution", "SYNTH", 90210,
            ["合成甲", "合成乙"], [0, 1], skipPreparation: true);
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
        var addEvent = typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "AddEvent" && method.GetParameters().Length == 4
                && method.GetParameters()[3].ParameterType == typeof(L12CardInstance[]));
        var cards = game.State.Players[0].Library.Take(6).ToArray();
        // Explicitly public synthetic facts, with real catalog snapshots and no padding.
        void Add(int index) => addEvent.Invoke(game,
            ["wire-fixture", 0, "合成公开展示", new[] { cards[index % cards.Length] }]);
        for (var index = 0; index < 128; index++) Add(index);
        var codec = new L12SnapshotWireCodec();
        codec.SetDeltaEnabled(true, true);
        codec.SetCardSnapshotFrameEnabled(true);
        JsonObject? previous = null;
        for (var index = 0; index < 6; index++)
        {
            if (index > 0) Add(index + 128);
            var projected = viewer < 0 ? game.SnapshotForSpectator() : game.SnapshotFor(viewer);
            var full = JsonSerializer.SerializeToUtf8Bytes(new { type = "gameState", state = projected }, Wire);
            var expected = JsonNode.Parse(full)!.AsObject();
            var publicIds = cards.Select(card => card.InstanceId).ToHashSet(StringComparer.Ordinal);
            foreach (var player in game.State.Players.Where(player => player.PlayerIndex != viewer))
                foreach (var hidden in player.Library.Concat(player.Hand).Where(card => !publicIds.Contains(card.InstanceId)))
                    Assert.DoesNotContain(JsonSerializer.Serialize(hidden.InstanceId), Encoding.UTF8.GetString(full), StringComparison.Ordinal);
            var prepared = codec.Prepare(new(full, true, false));
            var encoded = JsonNode.Parse(prepared.Payload)!.AsObject();
            Assert.Equal(index > 0, prepared.IsDelta);
            if (prepared.IsDelta)
            {
                Assert.NotNull(previous);
                Assert.True(JsonNode.DeepEquals(expected, L12SnapshotWireCodec.ApplyDelta(previous!, prepared.Payload)));
            }
            else Assert.True(JsonNode.DeepEquals(expected, RestoreCardFrame(encoded)));
            Report("full", full, viewer, index);
            Report(prepared.IsDelta ? "delta" : encoded["type"]!.GetValue<string>(), prepared.Payload, viewer, index);
            previous = expected;
            prepared.Commit();
        }
    }

    private static JsonObject RestoreCardFrame(JsonObject wire)
    {
        if (wire["type"]!.GetValue<string>() != L12CardSnapshotFrame.WireType) return wire;
        var payload = wire["payload"]!.DeepClone().AsObject();
        var snapshots = wire["cardSnapshots"]!.AsArray();
        JsonArray Cards(JsonNode references) => new(references.AsArray()
            .Select(reference => snapshots[reference!.GetValue<int>()]!.DeepClone()).ToArray());
        var events = payload["state"]!["recentEvents"]!.AsArray();
        for (var index = 0; index < events.Count; index++)
            events[index]!["cards"] = Cards(wire["eventCardRefs"]![index]!);
        if (payload["state"]!["lastAction"] is JsonObject last)
            last["cards"] = Cards(wire["lastActionCardRefs"]!);
        return payload;
    }

    private void Report(string mode, byte[] bytes, int viewer, int sample)
    {
        using var document = JsonDocument.Parse(bytes);
        var groups = new Dictionary<string, long>(StringComparer.Ordinal);
        void Add(string field, long size) => groups[field] = groups.GetValueOrDefault(field) + size;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name == "state" && property.Value.ValueKind == JsonValueKind.Object)
                foreach (var child in property.Value.EnumerateObject()) Add("state." + child.Name, Size(child.Value));
            else if (property.Name == "changes" && property.Value.ValueKind == JsonValueKind.Array)
                foreach (var change in property.Value.EnumerateArray())
                {
                    var path = change.GetProperty("path").EnumerateArray().Take(2)
                        .Select(part => part.ValueKind == JsonValueKind.String ? part.GetString()! : "[]");
                    Add("changes." + string.Join('.', path), Size(change));
                }
            else Add(property.Name, Size(property.Value));
        }
        // Values/whole change records above are disjoint slices of the original
        // UTF-8 JSON. All keys, commas, colons and braces remain explicit overhead.
        Add("json-structure", bytes.LongLength - groups.Values.Sum());
        Assert.All(groups.Values, value => Assert.True(value >= 0));
        Assert.Equal(bytes.LongLength, groups.Values.Sum());
        output.WriteLine("wire_fields viewer={0} sample={1} mode={2} total_bytes={3} groups={4}",
            viewer, sample, mode, bytes.Length, JsonSerializer.Serialize(groups.OrderByDescending(row => row.Value)
                .ToDictionary(row => row.Key, row => row.Value)));
    }

    private static int Size(JsonElement value) => Encoding.UTF8.GetByteCount(value.GetRawText());
}
