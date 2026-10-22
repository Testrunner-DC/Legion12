using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace TwelveLegions.Server;

/// <summary>
/// Compresses repeated, already-recipient-projected card snapshots inside one full game-state frame.
/// The dictionary is local to this frame and keys exact serialized card values, never catalog/card ids.
/// </summary>
internal static class L12CardSnapshotFrame
{
    internal const string Capability = "gameStateCardFrameV1";
    internal const string WireType = "gameStateCardFrameV1";
    internal const int MaximumWireBytes = 2 * 1024 * 1024;
    internal const int MaximumEvents = 128;
    internal const int MaximumReferences = 1024;
    private const int MinimumSavedBytes = 1024;

    internal static bool TryEncode(ReadOnlyMemory<byte> fullPayload, out byte[] encoded)
    {
        encoded = [];
        if (fullPayload.Length == 0 || fullPayload.Length > MaximumWireBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(fullPayload);
            var source = document.RootElement;
            if (source.ValueKind != JsonValueKind.Object
                || !source.TryGetProperty("type", out var sourceType)
                || sourceType.ValueKind != JsonValueKind.String
                || sourceType.GetString() != "gameState"
                || !source.TryGetProperty("state", out var sourceState)
                || sourceState.ValueKind != JsonValueKind.Object
                || !sourceState.TryGetProperty("recentEvents", out var sourceEvents)
                || sourceEvents.ValueKind != JsonValueKind.Array
                || sourceEvents.GetArrayLength() > MaximumEvents
                || !sourceState.TryGetProperty("lastAction", out var sourceLastAction))
                return false;

            var payload = JsonNode.Parse(fullPayload.Span)?.AsObject();
            var state = payload?["state"] as JsonObject;
            var events = state?["recentEvents"] as JsonArray;
            if (payload is null || state is null || events is null
                || events.Count != sourceEvents.GetArrayLength()) return false;

            var snapshots = new JsonArray();
            // Keys borrow the original document only for this synchronous call.
            // Compare exact UTF-8 bytes, not card ids or semantic JSON equality.
            var lookup = new Dictionary<JsonElement, int>(RawCardSnapshotComparer.Instance);
            var eventCardRefs = new JsonArray();
            var referenceCount = 0;
            var eventIndex = 0;
            foreach (var sourceEvent in sourceEvents.EnumerateArray())
            {
                if (sourceEvent.ValueKind != JsonValueKind.Object
                    || events[eventIndex] is not JsonObject targetEvent
                    || !TryReplaceCards(sourceEvent, targetEvent, lookup, snapshots,
                        ref referenceCount, out var references)) return false;
                eventCardRefs.Add(references);
                eventIndex++;
            }

            JsonNode? lastActionCardRefs;
            if (sourceLastAction.ValueKind == JsonValueKind.Null)
            {
                if (state["lastAction"] is not null) return false;
                lastActionCardRefs = null;
            }
            else
            {
                if (sourceLastAction.ValueKind != JsonValueKind.Object
                    || state["lastAction"] is not JsonObject targetLastAction
                    || !TryReplaceCards(sourceLastAction, targetLastAction, lookup, snapshots,
                        ref referenceCount, out var references)) return false;
                lastActionCardRefs = references;
            }

            var wrapper = new JsonObject
            {
                ["type"] = WireType,
                ["payload"] = payload,
                ["eventCardRefs"] = eventCardRefs,
                ["lastActionCardRefs"] = lastActionCardRefs,
                ["cardSnapshots"] = snapshots,
            };
            var candidate = L12SnapshotJson.SerializeToUtf8Bytes(wrapper);
            // A negotiated client can always receive the original full frame. Small/noisy frames
            // stay on that path rather than paying a new protocol shape for negligible savings.
            if (candidate.Length > MaximumWireBytes
                || candidate.Length + MinimumSavedBytes > fullPayload.Length
                || candidate.Length >= fullPayload.Length * 9L / 10L) return false;
            encoded = candidate;
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
                                      or ArgumentException or FormatException or OverflowException)
        {
            encoded = [];
            return false;
        }
    }

    private static bool TryReplaceCards(JsonElement sourceOwner, JsonObject targetOwner,
        IDictionary<JsonElement, int> lookup, JsonArray snapshots, ref int referenceCount,
        out JsonArray references)
    {
        references = [];
        if (!sourceOwner.TryGetProperty("cards", out var sourceCards)
            || sourceCards.ValueKind != JsonValueKind.Array
            || targetOwner["cards"] is not JsonArray targetCards
            || targetCards.Count != sourceCards.GetArrayLength()) return false;
        foreach (var sourceCard in sourceCards.EnumerateArray())
        {
            if (sourceCard.ValueKind != JsonValueKind.Object
                || ++referenceCount > MaximumReferences) return false;
            if (!lookup.TryGetValue(sourceCard, out var index))
            {
                if (snapshots.Count >= MaximumReferences
                    || JsonNode.Parse(JsonMarshal.GetRawUtf8Value(sourceCard)) is not JsonObject snapshot) return false;
                index = snapshots.Count;
                lookup.Add(sourceCard, index);
                snapshots.Add(snapshot);
            }
            references.Add(index);
        }
        targetOwner["cards"] = null;
        return true;
    }

    internal sealed class RawCardSnapshotComparer : IEqualityComparer<JsonElement>
    {
        internal static readonly RawCardSnapshotComparer Instance = new();

        public bool Equals(JsonElement first, JsonElement second)
            => JsonMarshal.GetRawUtf8Value(first).SequenceEqual(JsonMarshal.GetRawUtf8Value(second));

        public int GetHashCode(JsonElement value)
        {
            var hash = new HashCode();
            hash.AddBytes(JsonMarshal.GetRawUtf8Value(value));
            return hash.ToHashCode();
        }
    }
}
