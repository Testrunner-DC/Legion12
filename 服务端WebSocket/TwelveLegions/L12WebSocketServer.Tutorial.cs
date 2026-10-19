using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    internal static bool TryReadTutorialRequest(JsonElement root, out string lessonId, out string requestId)
    {
        lessonId = requestId = string.Empty;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("requestId", out var echoed) && echoed.ValueKind == JsonValueKind.String
            && echoed.GetString() is { Length: > 0 and <= 128 } safeId
            && safeId == safeId.Trim() && !safeId.Any(char.IsControl)) requestId = safeId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (property.Name is not ("type" or "lessonId" or "requestId") || !seen.Add(property.Name)) return false;
        if (seen.Count != 3 || !root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String || type.GetString() != "createTutorial"
            || !root.TryGetProperty("lessonId", out var lesson) || lesson.ValueKind != JsonValueKind.String
            || lesson.GetString() is not ("deploy" or "move" or "combat" or "response" or "preparation" or "finale")
            || !root.TryGetProperty("requestId", out var request) || request.ValueKind != JsonValueKind.String
            || request.GetString() is not { Length: > 0 and <= 128 } id
            || id.Any(char.IsControl) || id != id.Trim()) return false;
        lessonId = lesson.GetString()!;
        requestId = id;
        return true;
    }

    private Task<IReadOnlyList<OutgoingMessage>> CreateTutorialAsync(Guid sessionId, JsonElement root)
        => TryReadTutorialRequest(root, out var lessonId, out var requestId)
            ? _rooms.CreateTutorialAsync(sessionId, lessonId, requestId)
            : Task.FromResult<IReadOnlyList<OutgoingMessage>>([new(sessionId, new
                { type = "tutorialRejected", requestId = string.IsNullOrEmpty(requestId) ? null : requestId,
                    message = "教学请求只接受关卡与请求标识" })]);
}
