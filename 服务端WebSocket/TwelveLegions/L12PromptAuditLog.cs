namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private CommandResult ResolvePrompt(int playerIndex, L12Command command)
    {
        var prompt = State.PendingPrompts.FirstOrDefault(item => item.PromptId == command.PromptId);
        var choices = new List<string>();
        if (prompt?.Data.GetValueOrDefault("placementMode") is "split-top-bottom" or "all-top-bottom" or "all-bottom")
        {
            choices.AddRange(command.TopCardInstanceIds ?? []);
            choices.AddRange(command.BottomCardInstanceIds ?? []);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(command.Choice)) choices.Add(command.Choice);
            choices.AddRange(command.CardInstanceIds ?? []);
        }
        // Capture permitted identities before zone movement, publish only once the
        // server has accepted this exact choice. Rejections never claim completion.
        var audit = prompt is null ? null : BuildResolvedPromptLog(prompt, choices.Distinct().ToArray());
        var eventIndex = State.Events.Count;
        var result = ResolvePromptCore(playerIndex, command);
        // A private search may end by explicitly revealing the selected card. Keep
        // that permitted public result authoritative instead of masking it with a
        // later generic "private choice complete" event.
        var publiclyRevealedChoice = result.Accepted && prompt?.IsPrivate == true
            && State.Events.Skip(eventIndex).Any(entry => entry.Type == "reveal" && entry.Cards.Length > 0);
        if (result.Accepted && !publiclyRevealedChoice && audit is { } entry)
            AddEvent("prompt-resolved", playerIndex, entry.Text, entry.Cards);
        return result;
    }

    private void AddResolvedPromptLog(L12Prompt prompt, IReadOnlyCollection<string> chosen)
    {
        if (BuildResolvedPromptLog(prompt, chosen) is { } entry)
            AddEvent("prompt-resolved", prompt.PlayerIndex, entry.Text, entry.Cards);
    }

    private (string Text, L12CardInstance[] Cards)? BuildResolvedPromptLog(L12Prompt prompt, IReadOnlyCollection<string> chosen)
    {
        // Setup/order choices have dedicated events. A pure acknowledgement has
        // no selection result and must not produce a misleading '未选择'.
        if (prompt.MaxChoose == 0 || prompt.Continuation.StartsWith("setup-", StringComparison.Ordinal)
            || prompt.Continuation == "trigger-batch-order") return null;
        var playerName = State.Players[prompt.PlayerIndex].Name;
        // Hidden-zone selections never put their candidates or order in a shared
        // event. Separate reveal/move events already publish the permitted result.
        if (prompt.IsPrivate)
        {
            return ($"{playerName} 已完成非公开选择", []);
        }
        var publicCards = State.Players.SelectMany(player =>
            PublicLegions(player).Concat(player.Graveyard))
            .Where(card => !card.Hidden).DistinctBy(card => card.InstanceId)
            .ToDictionary(card => card.InstanceId, StringComparer.OrdinalIgnoreCase);
        var cards = new List<L12CardInstance>();
        var labels = chosen.Select(id =>
        {
            if (publicCards.TryGetValue(id, out var card))
            {
                cards.Add(card.Clone());
                return $"〈{card.Name}〉";
            }
            if (id is "skip" or "mode:none" or "cancel") return "不发动 / 跳过";
            if (id.StartsWith("temporary-morale:", StringComparison.Ordinal)) return "临时士气";
            // Only choice labels already exposed by this public prompt are used.
            if (prompt.ChoiceLabels.TryGetValue(id, out var label) && !string.IsNullOrWhiteSpace(label)) return label;
            if (id.StartsWith("slot:", StringComparison.Ordinal)) return $"场地位置 {id[5..]}";
            if (id.StartsWith("mode:", StringComparison.Ordinal)) return "发动";
            return "已选择";
        }).ToArray();
        var result = labels.Length == 0 ? "未选择" : string.Join("、", labels);
        return ($"{playerName}：{prompt.Text} → {result}", cards.ToArray());
    }
}
