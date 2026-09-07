using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwelveLegions.Server;

internal sealed record L12QueuedPayload(byte[] FullPayload, bool IsGameState, bool ForceFullGameState);
internal sealed record L12PreparedWirePayload(byte[] Payload, Action Commit, bool IsDelta);

/// <summary>
/// 每条连接独占一个实例，基线永不跨玩家/观战视角共享。差量在单发送循环真正发送前
/// 才生成，成功后才提交基线，因此排队快照被合并或发送失败都不会制造悬空 baseRevision。
/// </summary>
internal sealed class L12SnapshotWireCodec
{
    internal const int FullSnapshotInterval = 32;
    private const int MaximumChanges = 4096;
    private readonly object _gate = new();
    private bool _enabled;
    private JsonObject? _baseline;
    private int _sinceFull;

    internal void SetDeltaEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            _baseline = null;
            _sinceFull = 0;
        }
    }

    internal L12PreparedWirePayload Prepare(L12QueuedPayload queued)
    {
        if (!queued.IsGameState) return new(queued.FullPayload, static () => { }, false);
        lock (_gate)
        {
            if (!_enabled) return new(queued.FullPayload, static () => { }, false);
            var current = JsonNode.Parse(queued.FullPayload)?.AsObject()
                ?? throw new InvalidDataException("gameState 载荷不是 JSON 对象");
            var matchId = current["state"]?["matchId"]?.GetValue<string>() ?? string.Empty;
            var revision = current["state"]?["revision"]?.GetValue<long>() ?? -1;
            var baselineMatch = _baseline?["state"]?["matchId"]?.GetValue<string>() ?? string.Empty;
            var baseRevision = _baseline?["state"]?["revision"]?.GetValue<long>() ?? -1;
            if (queued.ForceFullGameState || _baseline is null || _sinceFull >= FullSnapshotInterval - 1
                || !string.Equals(matchId, baselineMatch, StringComparison.Ordinal))
                return Full(current, queued.FullPayload);

            var changes = new JsonArray();
            BuildChanges(_baseline, current, [], changes);
            if (changes.Count > MaximumChanges) return Full(current, queued.FullPayload);
            var delta = new JsonObject
            {
                ["type"] = "gameStateDelta",
                ["matchId"] = matchId,
                ["baseRevision"] = baseRevision,
                ["revision"] = revision,
                ["changes"] = changes,
            };
            var encoded = JsonSerializer.SerializeToUtf8Bytes(delta);
            if (encoded.Length >= queued.FullPayload.Length * 9 / 10)
                return Full(current, queued.FullPayload);
            return new L12PreparedWirePayload(encoded, () =>
            {
                lock (_gate)
                {
                    _baseline = current;
                    _sinceFull++;
                }
            }, true);
        }
    }

    private L12PreparedWirePayload Full(JsonObject current, byte[] bytes)
        => new(bytes, () =>
        {
            lock (_gate)
            {
                _baseline = current;
                _sinceFull = 0;
            }
        }, false);

    private static void BuildChanges(JsonNode? previous, JsonNode? current,
        IReadOnlyList<object> path, JsonArray changes)
    {
        if (changes.Count > MaximumChanges) return;
        if (JsonNode.DeepEquals(previous, current)) return;
        if (previous is JsonObject previousObject && current is JsonObject currentObject)
        {
            foreach (var key in previousObject.Select(pair => pair.Key)
                         .Union(currentObject.Select(pair => pair.Key), StringComparer.Ordinal))
            {
                var next = path.Append(key).ToArray();
                if (!currentObject.ContainsKey(key))
                {
                    changes.Add(Change(next, null, remove: true));
                    continue;
                }
                BuildChanges(previousObject[key], currentObject[key], next, changes);
                if (changes.Count > MaximumChanges) return;
            }
            return;
        }
        if (previous is JsonArray previousArray && current is JsonArray currentArray
            && previousArray.Count == currentArray.Count)
        {
            for (var index = 0; index < currentArray.Count; index++)
            {
                BuildChanges(previousArray[index], currentArray[index], path.Append(index).ToArray(), changes);
                if (changes.Count > MaximumChanges) return;
            }
            return;
        }
        changes.Add(Change(path, current?.DeepClone(), remove: false));
    }

    private static JsonObject Change(IReadOnlyList<object> path, JsonNode? value, bool remove)
    {
        var pathNode = new JsonArray();
        foreach (var segment in path)
            pathNode.Add(segment is int index ? JsonValue.Create(index) : JsonValue.Create((string)segment));
        return new JsonObject { ["path"] = pathNode, ["value"] = value, ["remove"] = remove };
    }

    internal static JsonObject ApplyDelta(JsonObject baseline, ReadOnlySpan<byte> deltaBytes)
    {
        var result = baseline.DeepClone().AsObject();
        var delta = JsonNode.Parse(deltaBytes)?.AsObject()
            ?? throw new InvalidDataException("增量载荷不是 JSON 对象");
        foreach (var changeNode in delta["changes"]?.AsArray() ?? [])
        {
            var change = changeNode?.AsObject() ?? throw new InvalidDataException("增量变更格式错误");
            var path = change["path"]?.AsArray() ?? throw new InvalidDataException("增量变更缺少路径");
            ApplyChange(result, path, change["value"], change["remove"]?.GetValue<bool>() == true);
        }
        return result;
    }

    private static void ApplyChange(JsonNode root, JsonArray path, JsonNode? value, bool remove)
    {
        if (path.Count == 0) throw new InvalidDataException("增量不允许替换根对象");
        JsonNode current = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            var segment = path[index] ?? throw new InvalidDataException("增量路径为空");
            current = segment.GetValueKind() == JsonValueKind.Number
                ? current.AsArray()[segment.GetValue<int>()]!
                : current.AsObject()[segment.GetValue<string>()]!;
        }
        var last = path[^1] ?? throw new InvalidDataException("增量路径为空");
        if (last.GetValueKind() == JsonValueKind.Number)
        {
            if (remove) throw new InvalidDataException("固定长度数组不支持删除元素");
            current.AsArray()[last.GetValue<int>()] = value?.DeepClone();
        }
        else
        {
            var key = last.GetValue<string>();
            if (remove) current.AsObject().Remove(key);
            else current.AsObject()[key] = value?.DeepClone();
        }
    }
}
