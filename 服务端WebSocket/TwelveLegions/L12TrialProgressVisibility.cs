using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

// Progress is public; the identity of an unfinished trial is not. Redact at the
// output boundary so old checkpoints/journal hashes remain reproducible.
internal static partial class L12TrialProgressVisibility
{
    [GeneratedRegex(@"^(?:《[^》]*》)?试炼进度 (?<before>[0-9]+) → (?<after>[0-9]+)$")]
    private static partial Regex ProgressLogPattern();

    private static bool IsProgressEvent(string? type, string? text)
        => type == "trial" && text?.Contains("试炼进度", StringComparison.Ordinal) == true;

    private static string PublicText(string text)
    {
        var match = ProgressLogPattern().Match(text);
        return match.Success ? $"试炼进度 {match.Groups["before"].Value} → {match.Groups["after"].Value}"
            : "试炼进度已更新";
    }

    internal static L12ActionEvent PublicEvent(L12ActionEvent actionEvent)
    {
        if (!IsProgressEvent(actionEvent.Type, actionEvent.Text)) return actionEvent;
        var cards = actionEvent.Cards
            .Where(card => card.CardType != "trial" && !card.Hidden).ToArray();
        var semantic = actionEvent.PlayerLogSemantic;
        if (semantic is null)
            return new L12ActionEvent(actionEvent.Sequence, actionEvent.Type, actionEvent.PlayerIndex,
                PublicText(actionEvent.Text), cards);
        var visibleSource = cards.FirstOrDefault(card =>
            card.InstanceId == semantic.SourceInstanceId);
        semantic = semantic with
        {
            SourceInstanceId = visibleSource?.InstanceId,
            SourceName = visibleSource?.Name,
            TargetInstanceId = null,
            TargetName = null,
        };
        return actionEvent with
        {
            Text = PublicText(actionEvent.Text),
            Cards = cards,
            PlayerLogSemantic = semantic,
        };
    }

    internal static void RedactRecordedState(JsonObject state)
    {
        if (state["Events"] is JsonArray events)
            for (var index = 0; index < events.Count; index++)
                if (events[index] is JsonObject action && PublicRecordedEvent(action) is { } redacted)
                    events[index] = redacted;
        if (state["LastAction"] is JsonObject last && PublicRecordedEvent(last) is { } redactedLast)
            state["LastAction"] = redactedLast;
        if (state["Log"] is JsonArray logs)
            for (var index = 0; index < logs.Count; index++)
                if (logs[index] is JsonValue value && value.TryGetValue<string>(out var text)
                    && ProgressLogPattern().IsMatch(text)) logs[index] = PublicText(text);
    }

    private static JsonObject? PublicRecordedEvent(JsonObject action)
    {
        if (!IsProgressEvent(action["Type"]?.GetValue<string>(), action["Text"]?.GetValue<string>()))
            return null;
        var cards = new JsonArray((action["Cards"] as JsonArray ?? [])
            .OfType<JsonObject>().Where(card => card["CardType"]?.GetValue<string>() != "trial"
                && card["Hidden"]?.GetValue<bool>() != true).Select(card => card.DeepClone()).ToArray());
        if (action["PlayerLogSemantic"] is not JsonObject)
            return new JsonObject
            {
                ["Sequence"] = action["Sequence"]?.DeepClone(),
                ["Type"] = action["Type"]?.DeepClone(),
                ["PlayerIndex"] = action["PlayerIndex"]?.DeepClone(),
                ["Text"] = PublicText(action["Text"]!.GetValue<string>()),
                ["Cards"] = cards,
            };
        var redacted = (JsonObject)action.DeepClone();
        redacted["Text"] = PublicText(action["Text"]!.GetValue<string>());
        redacted["Cards"] = cards;
        if (redacted["PlayerLogSemantic"] is JsonObject semantic)
        {
            var sourceId = semantic["SourceInstanceId"]?.GetValue<string>();
            var visibleSource = cards.OfType<JsonObject>().FirstOrDefault(card =>
                card["InstanceId"]?.GetValue<string>() == sourceId);
            if (visibleSource is null)
            {
                semantic.Remove("SourceInstanceId");
                semantic.Remove("SourceName");
            }
            else
            {
                semantic["SourceInstanceId"] = visibleSource["InstanceId"]?.DeepClone();
                semantic["SourceName"] = visibleSource["Name"]?.DeepClone();
            }
            semantic.Remove("TargetInstanceId");
            semantic.Remove("TargetName");
        }
        return redacted;
    }
}
