namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void BeginEffectMoralePayment(L12StackItem item, int cost, string afterPayment, Dictionary<string, string>? extra = null)
    {
        var player = State.Players[item.Controller];
        if (ActiveResourceCount(player) < cost) { FinishStackItem(item); return; }
        if (!NeedsManualOrdinaryResourcePayment(player, cost))
        {
            if (TryConsumeMorale(player, cost)) CompleteEffectMoralePayment(item, afterPayment, extra ?? []);
            else FinishStackItem(item);
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "effect-morale-payment", ["afterPayment"] = afterPayment, ["cost"] = cost.ToString(),
            ["choiceMode"] = "resource-payment",
        };
        if (extra is not null) foreach (var pair in extra) data[$"payment:{pair.Key}"] = pair.Value;
        CreateResourcePaymentPrompt(item.Controller, cost, "card-effect", item.StackItemId, data);
    }

    private void ContinueEffectMoralePayment(L12StackItem item, L12Prompt prompt, IReadOnlyCollection<string> selectedIds)
    {
        var cost = int.TryParse(prompt.Data.GetValueOrDefault("cost"), out var parsedCost) ? parsedCost : 0;
        var player = State.Players[item.Controller];
        if (!TryConsumeSelectedResources(player, cost, selectedIds)) { FinishStackItem(item); return; }
        var extra = prompt.Data.Where(pair => pair.Key.StartsWith("payment:", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[8..], pair => pair.Value);
        CompleteEffectMoralePayment(item, prompt.Data.GetValueOrDefault("afterPayment") ?? string.Empty, extra);
    }

    private bool CanUseTombGuardsAsResource(L12PlayerState player)
        => player.Faction == "taiyangcheng" && State.ActivePlayer == player.PlayerIndex;

    private IEnumerable<L12CardInstance> ActiveTombGuardResources(L12PlayerState player)
        => CanUseTombGuardsAsResource(player)
            ? PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped)
            : [];

    private bool NeedsManualOrdinaryResourcePayment(L12PlayerState player, int totalCost,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        var usableTemporaryMorale = Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve);
        var visibleCost = Math.Max(0, totalCost - usableTemporaryMorale);
        if (visibleCost <= 0) return false;

        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var morale = player.Morale.Where(card => !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var guards = ActiveTombGuardResources(player).Where(card => !excluded.Contains(card.InstanceId)).ToArray();
        var candidateCount = morale.Length + guards.Length;
        // 所有公开资源都必须支付时没有选择空间；直接支付可避免只有一个合法答案的空弹框。
        if (candidateCount <= visibleCost) return false;

        var resourceKinds = morale.Select(card => card.CardId == "S02-0010"
                ? "black-lotus"
                : card.IsGodPower ? "god-power" : "morale")
            .Concat(guards.Select(_ => "tomb-guard"))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count();
        return resourceKinds > 1;
    }

    private void CreateResourcePaymentPrompt(int playerIndex, int totalCost, string continuation, string? stackItemId,
        Dictionary<string, string> data, IReadOnlyCollection<string>? excludedResourceIds = null,
        int temporaryMoraleReserve = 0)
    {
        var player = State.Players[playerIndex];
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var visibleCost = Math.Max(0, totalCost - Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve));
        var availableMorale = player.Morale.Where(card => !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var availableGuards = ActiveTombGuardResources(player).Where(card => !excluded.Contains(card.InstanceId)).ToArray();
        var choices = availableMorale.Select(card => card.InstanceId)
            .Concat(availableGuards.Select(card => card.InstanceId))
            .ToArray();
        data["cost"] = totalCost.ToString();
        data["visibleCost"] = visibleCost.ToString();
        data["choiceMode"] = "resource-payment";
        foreach (var morale in availableMorale)
            data[$"{morale.InstanceId}:resourceType"] = morale.IsGodPower ? "god-power" : "morale";
        foreach (var guard in availableGuards)
            data[$"{guard.InstanceId}:resourceType"] = "tomb-guard";
        var resourceNames = new List<string>();
        if (availableMorale.Any(card => !card.IsGodPower)) resourceNames.Add("士气");
        if (availableMorale.Any(card => card.IsGodPower)) resourceNames.Add("神力");
        if (availableGuards.Length > 0) resourceNames.Add("陵墓守卫");
        var promptText = $"请选择支付费用的{string.Join("、", resourceNames)}";
        CreatePrompt(playerIndex, "resource-payment", promptText, choices,
            visibleCost, visibleCost, continuation, stackItemId, isPrivate: true, data: data);
    }

    private bool TryConsumeSelectedResources(L12PlayerState player, int totalCost, IReadOnlyCollection<string> selectedIds,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        if (!CanConsumeSelectedResources(player, totalCost, selectedIds, excludedResourceIds, temporaryMoraleReserve)) return false;
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var temporary = Math.Min(totalCost, Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve));
        var visibleCost = totalCost - temporary;
        var morale = player.Morale.Where(card => selectedIds.Contains(card.InstanceId) && !card.Tapped).ToArray();
        var guards = ActiveTombGuardResources(player)
            .Where(card => selectedIds.Contains(card.InstanceId) && !excluded.Contains(card.InstanceId)).ToArray();
        player.TemporaryMorale -= temporary;
        foreach (var card in morale) card.Tapped = true;
        foreach (var card in guards) card.Tapped = true;
        return true;
    }

    private bool CanConsumeSelectedResources(L12PlayerState player, int totalCost,
        IReadOnlyCollection<string> selectedIds, IReadOnlyCollection<string>? excludedResourceIds = null,
        int temporaryMoraleReserve = 0)
    {
        if (totalCost < 0) return false;
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var temporary = Math.Min(totalCost, Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve));
        var visibleCost = totalCost - temporary;
        if (selectedIds.Count != visibleCost || selectedIds.Distinct(StringComparer.Ordinal).Count() != visibleCost)
            return false;
        var morale = player.Morale.Where(card => selectedIds.Contains(card.InstanceId)
            && !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var guards = ActiveTombGuardResources(player).Where(card => selectedIds.Contains(card.InstanceId)
            && !excluded.Contains(card.InstanceId)).ToArray();
        return morale.Length + guards.Length == visibleCost;
    }

    private string[] SelectAutomaticOrdinaryResourcePaymentIds(L12PlayerState player, int totalCost,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var visibleCost = Math.Max(0, totalCost - Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve));
        return player.Morale.Where(card => !card.Tapped && !excluded.Contains(card.InstanceId))
            .Select(card => card.InstanceId)
            .Concat(ActiveTombGuardResources(player)
                .Where(card => !excluded.Contains(card.InstanceId))
                .Select(card => card.InstanceId))
            .Take(visibleCost).ToArray();
    }

    private int ActiveResourceCountExcluding(L12PlayerState player, IReadOnlyCollection<string>? excludedResourceIds,
        int temporaryMoraleReserve = 0)
    {
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        return Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve)
            + player.Morale.Count(card => !card.Tapped && !excluded.Contains(card.InstanceId))
            + ActiveTombGuardResources(player).Count(card => !excluded.Contains(card.InstanceId));
    }

    private void CompleteEffectMoralePayment(L12StackItem item, string afterPayment, IReadOnlyDictionary<string, string> data)
    {
        var player = State.Players[item.Controller]; var enemy = State.Players[1 - item.Controller]; var source = FindSource(item);
        switch (afterPayment)
        {
            case "scout-shuffle":
                if (enemy.Hand.Count == 0) { FinishStackItem(item); break; }
                CreatePrompt(1 - item.Controller, "card", "前线侦查：选择1张手牌洗回牌库", enemy.Hand.Select(card => card.InstanceId), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "scout-shuffle" }); break;
            case "camp-mode":
                if (data.GetValueOrDefault("mode") == "heal") HealMaster(item.Controller, 1, "野外扎营");
                else if (data.GetValueOrDefault("mode") == "draw") Draw(player, 1);
                FinishStackItem(item); break;
            case "s2-prayer-private": BeginPrayerPrivatePreview(item); break;
            default: FinishStackItem(item); break;
        }
    }
}
