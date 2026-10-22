using System.Text.Json;
using System.Text.Json.Nodes;
using System.Buffers;

namespace TwelveLegions.Server;

internal sealed record L12QueuedPayload(byte[] FullPayload, bool IsGameState, bool ForceFullGameState,
    bool IsPresenceSnapshot = false, bool ForceFullPresence = false);
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
    private bool _eventWindowEnabled;
    private bool _handArrayEnabled;
    private bool _cardSnapshotFrameEnabled;
    // Outbound payloads are immutable UTF-8 bytes. Retain only the successfully
    // sent bytes; do not hydrate a complete mutable card tree for every connection.
    private byte[]? _baseline;
    private int _sinceFull;

    internal void SetDeltaEnabled(bool enabled)
        => SetDeltaEnabled(enabled, false);

    internal void SetDeltaEnabled(bool enabled, bool eventWindowDeltaV1)
        => SetDeltaEnabled(enabled, eventWindowDeltaV1, false);

    internal void SetDeltaEnabled(bool enabled, bool eventWindowDeltaV1, bool handArrayDeltaV1)
    {
        lock (_gate)
        {
            var windowEnabled = enabled && eventWindowDeltaV1;
            var handEnabled = enabled && handArrayDeltaV1;
            if (_enabled == enabled && _eventWindowEnabled == windowEnabled && _handArrayEnabled == handEnabled) return;
            _enabled = enabled;
            _eventWindowEnabled = windowEnabled;
            _handArrayEnabled = handEnabled;
            _baseline = null;
            _sinceFull = 0;
        }
    }

    internal void SetCardSnapshotFrameEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_cardSnapshotFrameEnabled == enabled) return;
            _cardSnapshotFrameEnabled = enabled;
            // A peer that rejected a packed frame must recover from an original full frame.
            // Never carry its pre-recovery baseline across this capability boundary.
            _baseline = null;
            _sinceFull = 0;
        }
    }

    internal L12PreparedWirePayload Prepare(L12QueuedPayload queued)
    {
        if (!queued.IsGameState) return new(queued.FullPayload, static () => { }, false);
        lock (_gate)
        {
            if (!_enabled)
                return _cardSnapshotFrameEnabled
                    ? Full(queued.FullPayload)
                    : new(queued.FullPayload, static () => { }, false);
            using var currentDocument = JsonDocument.Parse(queued.FullPayload);
            var current = currentDocument.RootElement;
            if (current.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("gameState 载荷不是 JSON 对象");
            if (queued.ForceFullGameState || _baseline is null || _sinceFull >= FullSnapshotInterval - 1)
                return Full(queued.FullPayload);
            using var baselineDocument = JsonDocument.Parse(_baseline);
            var previous = baselineDocument.RootElement;
            var (matchId, revision) = Identity(current);
            var (baselineMatch, baseRevision) = Identity(previous);
            if (!string.Equals(matchId, baselineMatch, StringComparison.Ordinal))
                return Full(queued.FullPayload);

            var changes = new JsonArray();
            var eventWindow = _eventWindowEnabled ? L12RecentEventWindowDelta.TryCreate(previous, current) : null;
            var handSplices = _handArrayEnabled ? L12HandArrayDelta.TryCreate(previous, current) : null;
            BuildChanges(previous, current, [], changes, eventWindow is not null,
                handSplices is null ? null : L12HandArrayDelta.Players(handSplices));
            if (changes.Count > MaximumChanges) return Full(queued.FullPayload);
            var delta = new JsonObject
            {
                ["type"] = "gameStateDelta",
                ["matchId"] = matchId,
                ["baseRevision"] = baseRevision,
                ["revision"] = revision,
                ["changes"] = changes,
            };
            if (eventWindow is not null) delta["eventWindow"] = eventWindow;
            if (handSplices is not null) delta["handSplices"] = handSplices;
            var encoded = L12SnapshotJson.SerializeToUtf8Bytes(delta);
            if (handSplices is not null && changes.Count + handSplices["edits"]!.AsArray().Count <= MaximumChanges
                && MeasureOrdinaryDelta(delta, current, handSplices) <= encoded.Length)
            {
                // Only materialize the ordinary packet when it actually wins. Its exact
                // UTF-8 length is measured with a reusable discard buffer, not a second tree.
                var ordinaryChanges = new JsonArray();
                BuildChanges(previous, current, [], ordinaryChanges, eventWindow is not null);
                delta.Remove("handSplices"); delta["changes"] = ordinaryChanges;
                encoded = L12SnapshotJson.SerializeToUtf8Bytes(delta);
            }
            if (encoded.Length >= queued.FullPayload.Length * 9 / 10)
                return Full(queued.FullPayload);
            return new L12PreparedWirePayload(encoded, () =>
            {
                lock (_gate)
                {
                    _baseline = queued.FullPayload;
                    _sinceFull++;
                }
            }, true);
        }
    }

    internal static long MeasureOrdinaryDelta(JsonObject delta, JsonElement current, JsonObject hands)
    {
        using var count = new ByteCountWriter();
        using (var writer = new Utf8JsonWriter(count, L12SnapshotJson.WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var property in delta)
            {
                if (property.Key == "handSplices") continue;
                writer.WritePropertyName(property.Key);
                if (property.Key != "changes")
                {
                    if (property.Value is { } value) value.WriteTo(writer); else writer.WriteNullValue();
                    continue;
                }
                writer.WriteStartArray();
                foreach (var change in property.Value!.AsArray()) change!.WriteTo(writer);
                foreach (var edit in hands["edits"]!.AsArray())
                {
                    var owner = edit!["playerIndex"]!.GetValue<int>();
                    writer.WriteStartObject();
                    writer.WritePropertyName("path"); writer.WriteStartArray();
                    writer.WriteStringValue("state"); writer.WriteStringValue("players");
                    writer.WriteNumberValue(owner); writer.WriteStringValue("hand"); writer.WriteEndArray();
                    writer.WritePropertyName("value");
                    current.GetProperty("state").GetProperty("players")[owner].GetProperty("hand").WriteTo(writer);
                    writer.WriteBoolean("remove", false); writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject(); writer.Flush();
        }
        return count.Length;
    }

    private sealed class ByteCountWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[]? buffer = ArrayPool<byte>.Shared.Rent(4096);
        internal long Length { get; private set; }
        public void Advance(int count)
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            if (count < 0 || count > buffer!.Length) throw new ArgumentOutOfRangeException(nameof(count));
            Length = checked(Length + count);
        }
        public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return buffer!; }
        public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return buffer!; }
        private void Ensure(int sizeHint)
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
            if (sizeHint <= buffer!.Length) return;
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            buffer = null;
            buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
        }
        public void Dispose()
        {
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            buffer = null;
        }
    }

    private static (string MatchId, long Revision) Identity(JsonElement root)
    {
        if (!root.TryGetProperty("state", out var state) || state.ValueKind == JsonValueKind.Null)
            return (string.Empty, -1);
        var match = state.TryGetProperty("matchId", out var matchId) ? matchId.GetString() : null;
        var revision = state.TryGetProperty("revision", out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt64() : -1;
        return (match ?? string.Empty, revision);
    }

    private L12PreparedWirePayload Full(byte[] bytes)
    {
        var payload = _cardSnapshotFrameEnabled && L12CardSnapshotFrame.TryEncode(bytes, out var packed)
            ? packed : bytes;
        return new(payload, () =>
        {
            lock (_gate)
            {
                // Delta always compares against the original complete per-recipient projection,
                // never against the frame-local dictionary representation sent on the wire.
                _baseline = bytes;
                _sinceFull = 0;
            }
        }, false);
    }

    private static void BuildChanges(JsonElement previous, JsonElement current,
        IReadOnlyList<object> path, JsonArray changes, bool skipRecentEvents = false,
        IReadOnlySet<int>? skipHands = null)
    {
        if (changes.Count > MaximumChanges) return;
        if (JsonElement.DeepEquals(previous, current)) return;
        if (previous.ValueKind == JsonValueKind.Object && current.ValueKind == JsonValueKind.Object)
        {
            // Existing properties first, then additions: keep the previous wire change order.
            foreach (var property in previous.EnumerateObject())
            {
                var key = property.Name;
                var hasCurrent = current.TryGetProperty(key, out var currentValue);
                if (hasCurrent && JsonElement.DeepEquals(property.Value, currentValue)) continue;
                var next = path.Append(key).ToArray();
                if (skipRecentEvents && next.Length == 2 && Equals(next[0], "state")
                    && key == "recentEvents") continue;
                if (next.Length == 4 && Equals(next[0], "state") && Equals(next[1], "players")
                    && next[2] is int owner && skipHands?.Contains(owner) == true && key == "hand") continue;
                if (!hasCurrent)
                {
                    changes.Add(Change(next, null, remove: true));
                }
                else
                    BuildChanges(property.Value, currentValue, next, changes, skipRecentEvents, skipHands);
                if (changes.Count > MaximumChanges) return;
            }
            foreach (var property in current.EnumerateObject())
            {
                var key = property.Name;
                if (previous.TryGetProperty(key, out _)) continue;
                // A new property with explicit null must be added, not treated as missing.
                changes.Add(Change(path.Append(key).ToArray(), JsonSerializer.SerializeToNode(property.Value), remove: false));
                if (changes.Count > MaximumChanges) return;
            }
            return;
        }
        if (previous.ValueKind == JsonValueKind.Array && current.ValueKind == JsonValueKind.Array
            && previous.GetArrayLength() == current.GetArrayLength())
        {
            for (var index = 0; index < current.GetArrayLength(); index++)
            {
                if (JsonElement.DeepEquals(previous[index], current[index])) continue;
                BuildChanges(previous[index], current[index], path.Append(index).ToArray(), changes, skipRecentEvents, skipHands);
                if (changes.Count > MaximumChanges) return;
            }
            return;
        }
        changes.Add(Change(path, JsonSerializer.SerializeToNode(current), remove: false));
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
        var hasWindow = delta.ContainsKey("eventWindow");
        foreach (var changeNode in delta["changes"]?.AsArray() ?? [])
        {
            var change = changeNode?.AsObject() ?? throw new InvalidDataException("增量变更格式错误");
            var path = change["path"]?.AsArray() ?? throw new InvalidDataException("增量变更缺少路径");
            if (hasWindow && L12RecentEventWindowDelta.OverlapsEvents(path))
                throw new InvalidDataException("历史事件窗口与普通变更冲突");
            ApplyChange(result, path, change["value"], change["remove"]?.GetValue<bool>() == true);
        }
        if (hasWindow)
            L12RecentEventWindowDelta.Apply(result, delta["eventWindow"]?.AsObject()
                ?? throw new InvalidDataException("历史事件窗口不是对象"));
        if (delta.ContainsKey("handSplices"))
        {
            var (baseMatch, baseRevision) = Identity(JsonSerializer.SerializeToElement(baseline));
            var (resultMatch, resultRevision) = Identity(JsonSerializer.SerializeToElement(result));
            if (delta["matchId"]?.GetValue<string>() != baseMatch || resultMatch != baseMatch
                || delta["baseRevision"]?.GetValue<long>() != baseRevision
                || delta["revision"]?.GetValue<long>() != resultRevision)
                throw new InvalidDataException("手牌增量的对局或版本不匹配");
            L12HandArrayDelta.Apply(baseline, result, delta["handSplices"] as JsonObject
                ?? throw new InvalidDataException("手牌增量不是对象"), delta["changes"] as JsonArray
                ?? throw new InvalidDataException("手牌增量缺少普通变更列表"));
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
