using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwelveLegions.Server;

/// <summary>
/// Per-connection presence baseline. The baseline contains the already-personalized wire view and
/// is committed only after its payload is successfully written to that connection.
/// </summary>
internal sealed class L12PresenceSnapshotWireCodec
{
    internal const string OrderReuseCapability = "presenceOrderReuseV1";
    private readonly object _gate = new();
    private bool _enabled;
    private bool _reuseOrder;
    private byte[]? _baseline;
    private long _generation;

    internal void SetDeltaEnabled(bool enabled, bool reuseOrder = false)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _reuseOrder = enabled && reuseOrder;
            _baseline = null;
            _generation++;
        }
    }

    internal L12PreparedWirePayload Prepare(L12QueuedPayload queued)
    {
        if (!queued.IsPresenceSnapshot) return new(queued.FullPayload, static () => { }, false);
        lock (_gate)
        {
            if (!_enabled || queued.ForceFullPresence || _baseline is null)
                return Full(queued.FullPayload, _generation);
            if (!TryReadSnapshot(queued.FullPayload, out var current))
                return new(queued.FullPayload, static () => { }, false);
            if (!TryReadSnapshot(_baseline, out var previous)
                || !string.Equals(previous.Epoch, current.Epoch, StringComparison.Ordinal)
                || current.Revision <= previous.Revision)
                return Full(queued.FullPayload, _generation);

            var removed = new JsonArray();
            foreach (var accountId in previous.Order)
                if (!current.Items.ContainsKey(accountId)) removed.Add(accountId);
            var upserts = new JsonArray();
            foreach (var accountId in current.Order)
                if (!previous.Items.TryGetValue(accountId, out var oldItem)
                    || !JsonElement.DeepEquals(oldItem, current.Items[accountId]))
                    upserts.Add(JsonSerializer.SerializeToNode(current.Items[accountId]));
            var delta = new JsonObject
            {
                ["type"] = "presenceDelta",
                ["resource"] = "presence",
                ["epoch"] = current.Epoch,
                ["baseRevision"] = previous.Revision,
                ["revision"] = current.Revision,
                ["removedAccountIds"] = removed,
                ["upserts"] = upserts,
            };
            // Removed identities leave the relative order of surviving entries
            // intact. New entries or a real reorder still need the complete order.
            if (_reuseOrder && previous.Order.Where(current.Items.ContainsKey)
                    .SequenceEqual(current.Order, StringComparer.Ordinal))
                delta["orderFromBaseline"] = true;
            else
            {
                var order = new JsonArray();
                foreach (var accountId in current.Order) order.Add(accountId);
                delta["order"] = order;
            }
            var encoded = L12SnapshotJson.SerializeToUtf8Bytes(delta);
            if (encoded.Length >= queued.FullPayload.Length) return Full(queued.FullPayload, _generation);
            var generation = _generation;
            return new(encoded, () => Commit(queued.FullPayload, generation), true);
        }
    }

    private L12PreparedWirePayload Full(byte[] bytes, long generation)
        => new(bytes, () => Commit(bytes, generation), false);

    private void Commit(byte[] bytes, long generation)
    {
        lock (_gate)
            if (_generation == generation) _baseline = bytes;
    }

    private static bool TryReadSnapshot(byte[] bytes, out PresenceSnapshot snapshot)
    {
        snapshot = default!;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != "presenceSnapshot"
                || !root.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.String
                || resource.GetString() != "presence"
                || !root.TryGetProperty("epoch", out var epochElement)
                || epochElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(epochElement.GetString())
                || !root.TryGetProperty("revision", out var revisionElement)
                || revisionElement.ValueKind != JsonValueKind.Number
                || !revisionElement.TryGetInt64(out var revision) || revision < 0
                || !root.TryGetProperty("items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Array) return false;
            var items = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("accountId", out var accountIdElement)
                    || accountIdElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(accountIdElement.GetString())) return false;
                var accountId = accountIdElement.GetString()!;
                if (!items.TryAdd(accountId, item.Clone())) return false;
                order.Add(accountId);
            }
            snapshot = new PresenceSnapshot(epochElement.GetString()!, revision, items, order);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record PresenceSnapshot(string Epoch, long Revision,
        Dictionary<string, JsonElement> Items, List<string> Order);
}
