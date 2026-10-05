using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed record L12ResponseAutoCloseRecordedCommand(
    string PromptId,
    string StackItemId,
    int PriorityPlayer,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset ObservedAtUtc)
{
    internal static L12ResponseAutoCloseRecordedCommand Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("响应超时记录必须是 JSON 对象");
        return new L12ResponseAutoCloseRecordedCommand(
            RequiredAliasedString(root, "promptId", "PromptId"),
            RequiredAliasedString(root, "stackItemId", "StackItemId"),
            RequiredAliasedInt32(root, "priorityPlayer", "PriorityPlayer"),
            RequiredDateTimeOffset(root, "deadlineUtc"),
            RequiredDateTimeOffset(root, "observedAtUtc"));
    }

    private static string RequiredAliasedString(JsonElement root, string currentName, string legacyName)
    {
        var hasCurrent = root.TryGetProperty(currentName, out var current);
        var hasLegacy = root.TryGetProperty(legacyName, out var legacy);
        if (!hasCurrent && !hasLegacy)
            throw new InvalidDataException($"响应超时记录缺少字段：{currentName}");
        var currentValue = hasCurrent ? RequiredString(current, currentName) : null;
        var legacyValue = hasLegacy ? RequiredString(legacy, legacyName) : null;
        if (hasCurrent && hasLegacy && !string.Equals(currentValue, legacyValue, StringComparison.Ordinal))
            throw new InvalidDataException($"响应超时记录字段冲突：{currentName}/{legacyName}");
        return currentValue ?? legacyValue!;
    }

    private static int RequiredAliasedInt32(JsonElement root, string currentName, string legacyName)
    {
        var hasCurrent = root.TryGetProperty(currentName, out var current);
        var hasLegacy = root.TryGetProperty(legacyName, out var legacy);
        if (!hasCurrent && !hasLegacy)
            throw new InvalidDataException($"响应超时记录缺少字段：{currentName}");
        var currentValue = hasCurrent ? RequiredInt32(current, currentName) : (int?)null;
        var legacyValue = hasLegacy ? RequiredInt32(legacy, legacyName) : (int?)null;
        if (hasCurrent && hasLegacy && currentValue != legacyValue)
            throw new InvalidDataException($"响应超时记录字段冲突：{currentName}/{legacyName}");
        return currentValue ?? legacyValue!.Value;
    }

    private static string RequiredString(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"响应超时记录字段必须是非空字符串：{name}");
        return value.GetString()!;
    }

    private static int RequiredInt32(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"响应超时记录字段必须是 Int32：{name}");
        return result;
    }

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out var result))
            throw new InvalidDataException($"响应超时记录字段必须是 DateTimeOffset：{name}");
        return result;
    }
}
