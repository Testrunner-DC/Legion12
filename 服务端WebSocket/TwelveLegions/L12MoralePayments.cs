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
        IReadOnlyCollection<string>? excludedResourceIds = null)
    {
        var visibleCost = Math.Max(0, totalCost - player.TemporaryMorale);
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
        Dictionary<string, string> data, IReadOnlyCollection<string>? excludedResourceIds = null)
    {
        var player = State.Players[playerIndex];
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var visibleCost = Math.Max(0, totalCost - player.TemporaryMorale);
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
        IReadOnlyCollection<string>? excludedResourceIds = null)
    {
        if (!CanConsumeSelectedResources(player, totalCost, selectedIds, excludedResourceIds)) return false;
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var temporary = Math.Min(totalCost, player.TemporaryMorale);
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
        IReadOnlyCollection<string> selectedIds, IReadOnlyCollection<string>? excludedResourceIds = null)
    {
        if (totalCost < 0) return false;
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var temporary = Math.Min(totalCost, player.TemporaryMorale);
        var visibleCost = totalCost - temporary;
        if (selectedIds.Count != visibleCost || selectedIds.Distinct(StringComparer.Ordinal).Count() != visibleCost)
            return false;
        var morale = player.Morale.Where(card => selectedIds.Contains(card.InstanceId)
            && !card.Tapped && !excluded.Contains(card.InstanceId)).ToArray();
        var guards = ActiveTombGuardResources(player).Where(card => selectedIds.Contains(card.InstanceId)
            && !excluded.Contains(card.InstanceId)).ToArray();
        return morale.Length + guards.Length == visibleCost;
    }

    private int ActiveResourceCountExcluding(L12PlayerState player, IReadOnlyCollection<string>? excludedResourceIds)
    {
        var excluded = excludedResourceIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        return player.TemporaryMorale
            + player.Morale.Count(card => !card.Tapped && !excluded.Contains(card.InstanceId))
            + ActiveTombGuardResources(player).Count(card => !excluded.Contains(card.InstanceId));
    }

    private void CompleteEffectMoralePayment(L12StackItem item, string afterPayment, IReadOnlyDictionary<string, string> data)
    {
        var player = State.Players[item.Controller]; var enemy = State.Players[1 - item.Controller]; var source = FindSource(item);
        switch (afterPayment)
        {
            case "nobunaga-debuff":
                foreach (var target in PublicLegions(enemy)) target.CostModifier--;
                AddEvent("effect", item.Controller, "织田信长：对方所有军团本回合费用-1", source is null ? [] : [source]);
                FinishStackItem(item); break;
            case "hijikata-kill":
                PromptEnemyLegion(item, "hijikata-attack-kill", "土方岁三：击杀对方1张费用不高于1的军团", target => target.CurrentCost <= 1, true); break;
            case "takasugi-debuff":
                PromptEnemyLegion(item, "takasugi-debuff", "高杉晋作：选择对方1张军团，本回合费用-2", _ => true, false); break;
            case "scout-shuffle":
                if (enemy.Hand.Count == 0) { FinishStackItem(item); break; }
                CreatePrompt(1 - item.Controller, "card", "前线侦查：选择1张手牌洗回牌库", enemy.Hand.Select(card => card.InstanceId), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "scout-shuffle" }); break;
            case "ay-buff":
                PromptOwnLegion(item, "ay-buff", "阿伊：选择我方前排1张兵力不高于2000的军团，本回合兵力+2000",
                    target => target.Troops <= 2000 && FindOnField(player, target.InstanceId, out var row, out _) is not null && row == 0, false); break;
            case "asgard-heal": HealMaster(item.Controller, 1, "阿斯加德阵营效果"); FinishStackItem(item); break;
            case "camp-mode":
                if (data.GetValueOrDefault("mode") == "heal") HealMaster(item.Controller, 1, "野外扎营");
                else if (data.GetValueOrDefault("mode") == "draw") Draw(player, 1);
                FinishStackItem(item); break;
            case "s2-prayer-private": BeginPrayerPrivatePreview(item); break;
            case "s2-black-lotus-morale":
                if (source is not null && player.Resolving.Remove(source))
                {
                    player.Morale.Add(new L12MoraleCard
                    {
                        InstanceId = source.InstanceId,
                        CardId = source.CardId,
                        Tapped = true,
                    });
                    AddEvent("morale", item.Controller, "〈黑色莲花〉休整置入士气区，视为1张士气", source);
                }
                FinishStackItem(item); break;
            case "s2-round-table-buff":
            {
                var target = FindOnField(player, data.GetValueOrDefault("target") ?? string.Empty, out _, out _);
                if (target is not null && target.HasTrait("圆桌骑士"))
                {
                    AddTimedModifier(target, 2000, 0, ExpiryAtNextOwnEnd(item.Controller), "圆桌领域");
                    AddEvent("effect", item.Controller, $"〈圆桌领域〉使{target.Name}本回合兵力+2000", source is null ? [target] : [source, target]);
                }
                FinishStackItem(item); break;
            }
            case "s2-rune-power-search":
                BeginRunePowerSearch(item); break;
            case "s2-bors-strong":
                if (source is not null)
                {
                    GrantStrongAttack(source);
                    AddEvent("effect", item.Controller, $"〈{source.Name}〉获得强攻", source);
                }
                FinishStackItem(item); break;
            default: FinishStackItem(item); break;
        }
    }
}
