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
        // 〈陵墓守卫〉的文字只要求“我方回合”且其处于我方战场；跨控制后仍由当前
        // 控制者使用该公开资源，不能再以控制者阵营作额外限制。
        => State.ActivePlayer == player.PlayerIndex;

    private IEnumerable<L12CardInstance> ActiveTombGuardResources(L12PlayerState player)
        => CanUseTombGuardsAsResource(player)
            ? PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped)
            : [];

    private static IEnumerable<string> TemporaryMoralePaymentChoices(L12PlayerState player,
        int temporaryMoraleReserve = 0)
    {
        var usable = Math.Max(0, player.TemporaryMorale - temporaryMoraleReserve);
        for (var index = 1; index <= usable; index++) yield return $"temporary-morale:{index}";
    }

    private static bool TryParseTemporaryMoralePaymentChoice(string? choiceId, out int index)
    {
        index = 0;
        const string prefix = "temporary-morale:";
        return choiceId?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            && int.TryParse(choiceId[prefix.Length..], out index) && index > 0;
    }

    private static int SelectedTemporaryMoraleCount(IReadOnlyCollection<string> selectedIds)
        => selectedIds.Count(id => TryParseTemporaryMoralePaymentChoice(id, out _));

    private string? OrdinaryPaymentSemanticKey(L12PlayerState player, string choiceId)
    {
        if (TryParseTemporaryMoralePaymentChoice(choiceId, out _)) return "temporary-morale";
        var morale = player.Morale.FirstOrDefault(card => card.InstanceId.Equals(
            choiceId, StringComparison.OrdinalIgnoreCase) && !card.Tapped);
        if (morale is not null)
            return $"morale:{morale.CardId}:{morale.IsGodPower}:{morale.CannotUntapUntilRound}";
        // 场上陵墓守卫的位置、兵力及附加状态都可能影响后续效果；即使同名也不能
        // 自动替玩家选定其中一张。
        var guard = ActiveTombGuardResources(player).FirstOrDefault(card => card.InstanceId.Equals(
            choiceId, StringComparison.OrdinalIgnoreCase));
        return guard is null ? null : $"tomb-guard:{guard.InstanceId}";
    }

    private static string? EquivalentOrdinaryMoralePaymentKey(L12PlayerState player, string choiceId)
    {
        var morale = player.Morale.FirstOrDefault(card => card.InstanceId.Equals(
            choiceId, StringComparison.OrdinalIgnoreCase) && !card.Tapped);
        if (morale is null || morale.IsGodPower || morale.CardId == "S02-0010") return null;
        return $"ordinary-morale:{morale.CardId}:{morale.CannotUntapUntilRound}";
    }

    private bool NeedsManualOrdinaryResourcePayment(L12PlayerState player, int totalCost,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var temporary = TemporaryMoralePaymentChoices(player, temporaryMoraleReserve).ToArray();
        var morale = player.Morale.Where(card => !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var guards = ActiveTombGuardResources(player).Where(card => !excluded.Contains(card.InstanceId)).ToArray();
        var candidateCount = temporary.Length + morale.Length + guards.Length;
        // 所有公开资源都必须支付时没有选择空间；直接支付可避免只有一个合法答案的空弹框。
        if (candidateCount <= totalCost) return false;

        var resourceKinds = temporary
            .Concat(morale.Select(card => card.InstanceId))
            .Concat(guards.Select(card => card.InstanceId))
            .Select(choice => OrdinaryPaymentSemanticKey(player, choice))
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
        var availableTemporaryMorale = TemporaryMoralePaymentChoices(player, temporaryMoraleReserve).ToArray();
        var availableMorale = player.Morale.Where(card => !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var availableGuards = ActiveTombGuardResources(player).Where(card => !excluded.Contains(card.InstanceId)).ToArray();
        var choices = availableTemporaryMorale
            .Concat(availableMorale.Select(card => card.InstanceId))
            .Concat(availableGuards.Select(card => card.InstanceId))
            .ToArray();
        data["cost"] = totalCost.ToString();
        data["visibleCost"] = totalCost.ToString();
        data["choiceMode"] = "resource-payment";
        foreach (var choiceId in availableTemporaryMorale)
            data[$"{choiceId}:resourceType"] = "temporary-morale";
        foreach (var morale in availableMorale)
            data[$"{morale.InstanceId}:resourceType"] = morale.IsGodPower ? "god-power" : "morale";
        foreach (var guard in availableGuards)
            data[$"{guard.InstanceId}:resourceType"] = "tomb-guard";
        var resourceNames = new List<string>();
        if (availableTemporaryMorale.Length > 0) resourceNames.Add("临时士气");
        if (availableMorale.Any(card => !card.IsGodPower)) resourceNames.Add("士气");
        if (availableMorale.Any(card => card.IsGodPower)) resourceNames.Add("神力");
        if (availableGuards.Length > 0) resourceNames.Add("陵墓守卫");
        var promptText = $"请选择支付费用的{string.Join("、", resourceNames)}";
        CreatePrompt(playerIndex, "resource-payment", promptText, choices,
            totalCost, totalCost, continuation, stackItemId, isPrivate: true, data: data);
    }

    private bool TryConsumeSelectedResources(L12PlayerState player, int totalCost, IReadOnlyCollection<string> selectedIds,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        if (!CanConsumeSelectedResources(player, totalCost, selectedIds, excludedResourceIds, temporaryMoraleReserve)) return false;
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var temporary = SelectedTemporaryMoraleCount(selectedIds);
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var morale = player.Morale.Where(card => selected.Contains(card.InstanceId) && !card.Tapped
            && !excluded.Contains(card.InstanceId)).ToArray();
        var guards = ActiveTombGuardResources(player)
            .Where(card => selected.Contains(card.InstanceId) && !excluded.Contains(card.InstanceId)).ToArray();
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
        if (selectedIds.Count != totalCost || selectedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != totalCost)
            return false;
        var validTemporaryChoices = TemporaryMoralePaymentChoices(player, temporaryMoraleReserve)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedTemporary = selectedIds.Where(id => TryParseTemporaryMoralePaymentChoice(id, out _)).ToArray();
        if (selectedTemporary.Any(id => !validTemporaryChoices.Contains(id))) return false;
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var morale = player.Morale.Where(card => selected.Contains(card.InstanceId)
            && !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var guards = ActiveTombGuardResources(player).Where(card => selected.Contains(card.InstanceId)
            && !excluded.Contains(card.InstanceId)).ToArray();
        return selectedTemporary.Length + morale.Length + guards.Length == totalCost;
    }

    private string[] SelectAutomaticOrdinaryResourcePaymentIds(L12PlayerState player, int totalCost,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return TemporaryMoralePaymentChoices(player, temporaryMoraleReserve)
            .Concat(player.Morale.Where(card => !card.Tapped && !excluded.Contains(card.InstanceId))
            .Select(card => card.InstanceId)
            .Concat(ActiveTombGuardResources(player)
                .Where(card => !excluded.Contains(card.InstanceId))
                .Select(card => card.InstanceId)))
            .Take(totalCost).ToArray();
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
        switch (afterPayment)
        {
            case "optional-paid-effect-operation":
                CompleteOptionalPaidEffectOperation(item, data);
                break;
            default: FinishStackItem(item); break;
        }
    }

    private void CompleteOptionalPaidEffectOperation(L12StackItem item, IReadOnlyDictionary<string, string> data)
    {
        switch (data.GetValueOrDefault("operation"))
        {
            case "heal-master":
            {
                var amount = int.TryParse(data.GetValueOrDefault("amount"), out var parsedAmount)
                    ? Math.Max(0, parsedAmount)
                    : 0;
                if (amount > 0)
                    HealMaster(item.Controller, amount, data.GetValueOrDefault("reason") ?? item.SourceName);
                break;
            }
        }
        FinishStackItem(item);
    }
}
