namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static bool IsLibraryPlacementAction(string? action) => action is "reorder-order"
        or "oiran-order" or "festival-bottom-order" or "faction-search-order" or "camp-order"
        or "s2-fortune-bottom-order" or "s2-rune-power-bottom-order";

    private static bool IsLibraryPlacementPrompt(L12Prompt prompt)
        => prompt.Continuation == "card-effect" && IsLibraryPlacementAction(prompt.Data.GetValueOrDefault("action"));

    private static bool IsLibraryInspectionChoiceAction(string? action) => action is "oiran-pick"
        or "festival-hand" or "festival-grave" or "faction-search-pick" or "camp-pick"
        or "s2-fortune-artifact" or "s2-fortune-uesugi" or "s2-rune-power-pick"
        or "s2-prometheus-pick" or "shanhe-search-pick" or "starter-telemachus-pick";

    private void CreateLibraryPlacementPrompt(L12StackItem item, IEnumerable<string> cardIds,
        string action, string placementMode, string text)
    {
        var ids = cardIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) { FinishStackItem(item); return; }
        var player = State.Players[item.Controller];
        if (ids.Any(id => player.Library.All(card => card.InstanceId != id)))
        {
            FailLibraryPlacement(item);
            return;
        }
        var data = new Dictionary<string, string> { ["action"] = action, ["placementMode"] = placementMode,
            ["layout"] = "single-row", ["displayCardIds"] = string.Join('|', ids) };
        foreach (var id in ids) AddPromptCardData(data, player.Library.First(card => card.InstanceId == id));
        CreatePrompt(item.Controller, "order", text, ids, ids.Length, ids.Length,
            "card-effect", item.StackItemId, isPrivate: true, data: data);
    }

    private void FailLibraryPlacement(L12StackItem item)
    {
        item.Data["effectResultStatus"] = "failed";
        AddEvent("effect-failed", item.Controller, "牌库整理对象或选择步骤已失效，本段停止；已完成的行动保留");
        FinishStackItem(item);
    }

    private bool TryContinueLibraryPlacement(L12StackItem item, L12Prompt prompt, L12Command command)
    {
        if (!IsLibraryPlacementPrompt(prompt)
            || command.TopCardInstanceIds is null && command.BottomCardInstanceIds is null) return false;
        CompleteLibraryPlacement(item, prompt.ValidChoices, command.TopCardInstanceIds ?? [], command.BottomCardInstanceIds ?? []);
        return true;
    }

    private void CompleteLibraryPlacement(L12StackItem item, IEnumerable<string> expectedIds,
        IReadOnlyCollection<string> topIds, IReadOnlyCollection<string> bottomIds)
    {
        var expected = expectedIds.ToArray();
        var ordered = topIds.Concat(bottomIds).ToArray();
        var player = State.Players[item.Controller];
        var cards = expected.Select(id => player.Library.FirstOrDefault(card => card.InstanceId == id)).ToArray();
        if (expected.Length != expected.Distinct().Count() || cards.Any(card => card is null)
            || ordered.Length != expected.Length || ordered.Distinct().Count() != expected.Length
            || ordered.Any(id => !expected.Contains(id)))
        {
            FailLibraryPlacement(item);
            return;
        }
        var byId = cards.ToDictionary(card => card!.InstanceId, card => card!);
        foreach (var card in byId.Values) player.Library.Remove(card);
        player.Library.InsertRange(0, topIds.Select(id => byId[id]));
        player.Library.AddRange(bottomIds.Select(id => byId[id]));
        AddEvent("reorder", item.Controller, $"将 {topIds.Count} 张牌放回牌库顶部、{bottomIds.Count} 张牌放回牌库底部");
        FinishStackItem(item);
    }

    private bool ReconcileLibraryPlacementPrompts()
    {
        var changed = false;
        foreach (var prompt in State.PendingPrompts.Where(IsLibraryPlacementPrompt).ToArray())
        {
            if (!State.PendingPrompts.Contains(prompt)) continue;
            var item = State.EffectStack.FirstOrDefault(stack => stack.StackItemId == prompt.StackItemId);
            if (item is not null && item.Controller is >= 0 and <= 1 && item.Controller == prompt.PlayerIndex
                && prompt.MinChoose == prompt.ValidChoices.Count && prompt.MaxChoose == prompt.ValidChoices.Count
                && prompt.ValidChoices.Count == prompt.ValidChoices.Distinct().Count()
                && State.PendingPrompts.Count(other => other.StackItemId == prompt.StackItemId && IsLibraryPlacementPrompt(other)) == 1
                && prompt.ValidChoices.All(id => State.Players[item.Controller].Library.Any(card => card.InstanceId == id))) continue;
            State.PendingPrompts.RemoveAll(other => other.StackItemId == prompt.StackItemId && IsLibraryPlacementPrompt(other));
            changed = true;
            if (item is not null) FailLibraryPlacement(item);
            else
            {
                AddEvent("effect-failed", prompt.PlayerIndex, "牌库整理步骤已失效，已清理等待，未移动卡牌");
                if (State.EffectStack.Count == 0 && State.PendingPrompts.Count == 0 && State.ResponseWindow is null)
                {
                    State.IsResolvingStack = false;
                    AdvanceTriggerBatches();
                }
            }
        }
        return changed;
    }
}
