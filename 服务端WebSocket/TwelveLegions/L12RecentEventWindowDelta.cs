using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwelveLegions.Server;

/// <summary>
/// Optional wire-only optimization AFTER recipient projection. Privacy may leave sequence gaps.
/// Reuse only an exactly equal frozen suffix, never refill historical cards from the catalog.
/// </summary>
internal static class L12RecentEventWindowDelta
{
    internal const int MaximumEvents = 128;
    private const long MaximumSafeSequence = 9_007_199_254_740_991;

    internal static JsonObject? TryCreate(JsonElement previous, JsonElement current)
    {
        if (!TryEvents(previous, out var before) || !TryEvents(current, out var after)
            || !TrySequences(before, out var baseSequences) || !TrySequences(after, out var resultSequences)
            || before.GetArrayLength() == 0 || after.GetArrayLength() == 0
            || JsonElement.DeepEquals(before, after)) return null;
        var removeFirst = Array.IndexOf(baseSequences, resultSequences[0]);
        if (removeFirst < 0) return null;
        var retained = baseSequences.Length - removeFirst;
        if (retained > resultSequences.Length) return null;
        for (var index = 0; index < retained; index++)
            if (!JsonElement.DeepEquals(before[index + removeFirst], after[index])) return null;
        // Historical presentation-fact changes fail equality above and use the original diff path.
        var append = new JsonArray();
        for (var index = retained; index < resultSequences.Length; index++)
            append.Add(JsonSerializer.SerializeToNode(after[index]));
        return new JsonObject
        {
            ["baseSequences"] = JsonSerializer.SerializeToNode(baseSequences),
            ["removeFirst"] = removeFirst,
            ["append"] = append,
            ["resultSequences"] = JsonSerializer.SerializeToNode(resultSequences),
        };
    }

    private static bool TryEvents(JsonElement frame, out JsonElement events)
    {
        events = default;
        return frame.ValueKind == JsonValueKind.Object
            && frame.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("recentEvents", out events) && events.ValueKind == JsonValueKind.Array;
    }

    private static bool TrySequences(JsonElement events, out long[] sequences)
    {
        sequences = [];
        if (events.ValueKind != JsonValueKind.Array || events.GetArrayLength() > MaximumEvents) return false;
        var result = new long[events.GetArrayLength()];
        var previous = -1L;
        for (var index = 0; index < result.Length; index++)
        {
            var item = events[index];
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("sequence", out var sequence)
                || sequence.ValueKind != JsonValueKind.Number || !sequence.TryGetInt64(out var number)
                || number <= 0 || number > MaximumSafeSequence || number <= previous) return false;
            result[index] = previous = number;
        }
        sequences = result;
        return true;
    }

    // Test/oracle decoder for the client wire contract, never an authority/recovery format.
    internal static void Apply(JsonObject result, JsonObject delta)
    {
        var frame = JsonSerializer.SerializeToElement(result);
        var patch = JsonSerializer.SerializeToElement(delta);
        if (!TryEvents(frame, out var before) || !TrySequences(before, out var baseSequences)
            || !patch.TryGetProperty("baseSequences", out var expectedBase)
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(baseSequences), expectedBase)
            || !patch.TryGetProperty("removeFirst", out var count) || count.ValueKind != JsonValueKind.Number
            || !count.TryGetInt32(out var removeFirst) || removeFirst < 0 || removeFirst >= baseSequences.Length
            || !patch.TryGetProperty("append", out var append) || !TrySequences(append, out _)
            || !patch.TryGetProperty("resultSequences", out var expectedResult))
            throw new InvalidDataException("历史事件窗口基线或形状不匹配");
        var rebuilt = new JsonArray();
        foreach (var item in before.EnumerateArray().Skip(removeFirst).Concat(append.EnumerateArray()))
            rebuilt.Add(JsonSerializer.SerializeToNode(item));
        var rebuiltElement = JsonSerializer.SerializeToElement(rebuilt);
        if (!TrySequences(rebuiltElement, out var resultSequences)
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(resultSequences), expectedResult))
            throw new InvalidDataException("历史事件窗口结果不匹配");
        result["state"]!["recentEvents"] = rebuilt;
    }

    internal static bool OverlapsEvents(JsonArray path)
        => path.Count > 0 && path[0]?.GetValue<string>() == "state"
            && (path.Count == 1 || path[1]?.GetValue<string>() == "recentEvents");
}
