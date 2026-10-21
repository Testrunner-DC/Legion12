using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwelveLegions.Server;

// Wire-only, after recipient projection. Reuse complete equal values, never catalog data.
internal static class L12HandArrayDelta
{
    internal const string Capability = "handArrayDeltaV1";
    private const int MaximumHand = 512;

    internal static JsonObject? TryCreate(JsonElement previous, JsonElement current)
    {
        if (!TryPlayers(previous, out var before, out var baseHash)
            || !TryPlayers(current, out var after, out var resultHash)
            || !SameOptional(previous, current, "observerView")
            || !SameOptional(previous.GetProperty("state"), current.GetProperty("state"), "you")) return null;
        var edits = new JsonArray();
        for (var owner = 0; owner < 2; owner++)
        {
            if (!TryHand(before[owner], owner, out var oldHand, out var oldIds)
                || !TryHand(after[owner], owner, out var newHand, out var newIds)) continue;
            if (oldIds.Length == newIds.Length) continue; // Original field diff remains best for ordinary same-length updates.
            var prefix = 0;
            while (prefix < Math.Min(oldIds.Length, newIds.Length)
                && JsonElement.DeepEquals(oldHand[prefix], newHand[prefix])) prefix++;
            var suffix = 0;
            while (suffix < Math.Min(oldIds.Length, newIds.Length) - prefix
                && JsonElement.DeepEquals(oldHand[oldIds.Length - 1 - suffix], newHand[newIds.Length - 1 - suffix])) suffix++;
            var insert = new JsonArray();
            for (var index = prefix; index < newIds.Length - suffix; index++)
                insert.Add(JsonSerializer.SerializeToNode(newHand[index]));
            edits.Add(new JsonObject
            {
                ["playerIndex"] = owner, ["baseIds"] = JsonSerializer.SerializeToNode(oldIds),
                ["prefix"] = prefix, ["remove"] = oldIds.Length - prefix - suffix,
                ["insert"] = insert, ["resultIds"] = JsonSerializer.SerializeToNode(newIds),
            });
        }
        return edits.Count == 0 ? null : new JsonObject
        {
            ["baseStateHash"] = baseHash, ["resultStateHash"] = resultHash, ["edits"] = edits,
        };
    }

    internal static HashSet<int> Players(JsonObject wire)
        => wire["edits"]!.AsArray().Select(edit => edit!["playerIndex"]!.GetValue<int>()).ToHashSet();

    private static bool SameOptional(JsonElement before, JsonElement after, string key)
    {
        var hasBefore = before.TryGetProperty(key, out var oldValue);
        var hasAfter = after.TryGetProperty(key, out var newValue);
        return hasBefore == hasAfter && (!hasBefore || JsonElement.DeepEquals(oldValue, newValue));
    }

    private static bool TryPlayers(JsonElement frame, out JsonElement players, out string hash)
    {
        players = default; hash = string.Empty;
        return frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("state", out var state)
            && state.ValueKind == JsonValueKind.Object && state.TryGetProperty("stateHash", out var hashValue)
            && hashValue.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(hash = hashValue.GetString()!)
            && state.TryGetProperty("players", out players) && players.ValueKind == JsonValueKind.Array
            && players.GetArrayLength() == 2;
    }

    private static bool TryHand(JsonElement player, int owner, out JsonElement hand, out string[] ids)
    {
        hand = default; ids = [];
        return player.ValueKind == JsonValueKind.Object && player.TryGetProperty("playerIndex", out var index)
            && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var number)
            && number == owner && player.TryGetProperty("hand", out hand)
            && TryKeys(hand, out ids);
    }

    private static bool TryKeys(JsonElement cards, out string[] ids)
    {
        ids = [];
        if (cards.ValueKind != JsonValueKind.Array || cards.GetArrayLength() > MaximumHand) return false;
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards.EnumerateArray())
        {
            if (card.ValueKind != JsonValueKind.Object || !card.TryGetProperty("instanceId", out var value)
                || value.ValueKind != JsonValueKind.String || value.GetString() is not { } id
                || string.IsNullOrWhiteSpace(id) || id.Length > 256 || !seen.Add(id)) return false;
            result.Add(id);
        }
        ids = [.. result]; return true;
    }

    internal static void Apply(JsonObject baseline, JsonObject result, JsonObject wire, JsonArray changes)
    {
        var oldFrame = JsonSerializer.SerializeToElement(baseline);
        var newFrame = JsonSerializer.SerializeToElement(result);
        if (!SameOptional(oldFrame, newFrame, "observerView")
            || !SameOptional(oldFrame.GetProperty("state"), newFrame.GetProperty("state"), "you")) throw Invalid();
        if (!Exact(wire, "baseStateHash,edits,resultStateHash")
            || wire["baseStateHash"] is not JsonValue baseValue || !baseValue.TryGetValue<string>(out var baseHash)
            || string.IsNullOrEmpty(baseHash) || baseline["state"]?["stateHash"]?.GetValue<string>() != baseHash
            || wire["resultStateHash"] is not JsonValue resultValue || !resultValue.TryGetValue<string>(out var resultHash)
            || string.IsNullOrEmpty(resultHash) || result["state"]?["stateHash"]?.GetValue<string>() != resultHash
            || wire["edits"] is not JsonArray edits || edits.Count is < 1 or > 2) throw Invalid();
        var assignments = new List<(JsonObject Player, JsonArray Cards)>(); var seen = new HashSet<int>();
        foreach (var node in edits)
        {
            if (node is not JsonObject edit || !Exact(edit, "baseIds,insert,playerIndex,prefix,remove,resultIds")
                || !Integer(edit["playerIndex"], out var owner) || owner > 1 || !seen.Add(owner)
                || !Integer(edit["prefix"], out var prefix) || !Integer(edit["remove"], out var remove)
                || edit["insert"] is not JsonArray insert || !Keys(insert, out _)) throw Invalid();
            var before = baseline["state"]?["players"]?[owner] as JsonObject;
            var after = result["state"]?["players"]?[owner] as JsonObject;
            if (before?["playerIndex"]?.GetValue<int>() != owner || after?["playerIndex"]?.GetValue<int>() != owner
                || before["hand"] is not JsonArray hand || !Keys(hand, out var ids) || !Same(ids, edit["baseIds"])
                || prefix > hand.Count || remove > hand.Count - prefix) throw Invalid();
            foreach (var change in changes)
            {
                if (change?["path"] is not JsonArray path || path.Count == 0) throw Invalid();
                if (path[0]?.GetValue<string>() == "state" && (path.Count == 1
                    || path[1]?.GetValue<string>() == "players" && (path.Count == 2
                        || path[2] is JsonValue index && index.TryGetValue<int>(out var number) && number == owner
                            && (path.Count == 3 || path[3]?.GetValue<string>() == "hand")))) throw Invalid();
            }
            var cards = new JsonArray(hand.Take(prefix).Concat(insert).Concat(hand.Skip(prefix + remove))
                .Select(card => card?.DeepClone()).ToArray());
            if (!Keys(cards, out var resultIds) || !Same(resultIds, edit["resultIds"])) throw Invalid();
            assignments.Add((after!, cards));
        }
        foreach (var (player, cards) in assignments) player["hand"] = cards;
    }

    private static bool Exact(JsonObject value, string expected)
        => string.Join(',', value.Select(pair => pair.Key).Order(StringComparer.Ordinal)) == expected;
    private static bool Integer(JsonNode? value, out int number)
    {
        number = -1; return value is JsonValue scalar && scalar.TryGetValue<int>(out number) && number >= 0;
    }
    private static bool Keys(JsonArray cards, out string[] ids)
        => TryKeys(JsonSerializer.SerializeToElement(cards), out ids);
    private static bool Same(string[] ids, JsonNode? value)
        => value is JsonArray array && array.Count == ids.Length
            && ids.Select((id, index) => array[index] is JsonValue scalar
                && scalar.TryGetValue<string>(out var actual) && actual == id).All(match => match);
    private static InvalidDataException Invalid() => new("手牌增量的基线、区间、身份或结果不匹配");
}
