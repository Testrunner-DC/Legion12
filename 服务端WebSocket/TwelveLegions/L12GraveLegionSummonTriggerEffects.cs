namespace TwelveLegions.Server;

internal sealed record L12GraveLegionSummonTriggerSpec(
    string CardId,
    string Trigger,
    string Name,
    string TargetRule,
    string PromptText);

public sealed partial class L12GameEngine
{
    private static readonly IReadOnlyDictionary<string, L12GraveLegionSummonTriggerSpec> GraveLegionSummonTriggerSpecs =
        new Dictionary<string, L12GraveLegionSummonTriggerSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0210|death"] = new("S01-0210", "death", "尼托克丽丝", "sun-city-cost-at-most-2",
                "尼托克丽丝：选择墓地1张当前费用不高于2的【太阳城】军团活跃登场"),
            ["S01-0308|death"] = new("S01-0308", "death", "血斧艾瑞克", "asgard-cost-at-most-3",
                "血斧艾瑞克：选择墓地1张当前费用不高于3的【阿斯加德】军团活跃登场"),
            ["S02-0202|death"] = new("S02-0202", "death", "陵墓圣武士", "tomb-guard",
                "陵墓圣武士：选择墓地1张〈陵墓守卫〉活跃登场"),
        };

    private static bool TryGetGraveLegionSummonTriggerSpec(string cardId, string trigger,
        out L12GraveLegionSummonTriggerSpec spec)
        => GraveLegionSummonTriggerSpecs.TryGetValue($"{cardId}|{trigger}", out spec!);

    private static bool IsLegalGraveLegionSummonTarget(L12GraveLegionSummonTriggerSpec spec,
        L12PlayerState player, L12CardInstance card)
    {
        if (card.CardType != "legion") return false;
        return spec.TargetRule switch
        {
            "sun-city-cost-at-most-2" => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
                && L12StructuredCardRules.CurrentCostAtMost(card, 2),
            "asgard-cost-at-most-3" => L12StructuredCardRules.HasFaction(player, card, "asgard")
                && L12StructuredCardRules.CurrentCostAtMost(card, 3),
            "tomb-guard" => card.CardId == "S01-0212",
            _ => false,
        };
    }

    private static L12CardInstance[] LegalGraveLegionSummonTargets(
        L12GraveLegionSummonTriggerSpec spec, L12PlayerState player)
        => player.Graveyard.Where(card => IsLegalGraveLegionSummonTarget(spec, player, card)).ToArray();

    private bool TryResolveGraveLegionSummonTrigger(L12StackItem item, L12CardInstance source)
    {
        if (!TryGetGraveLegionSummonTriggerSpec(source.CardId, item.Trigger, out var spec)) return false;
        if (!item.Data.ContainsKey("declared:entryCard") && !item.Data.ContainsKey("declaredTargets"))
            return false;

        var player = State.Players[item.Controller];
        var entryId = PublicTriggerDeclared(item, "entryCard");
        var slot = PublicTriggerDeclared(item, "entrySlot");
        if (string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(slot))
        {
            var legacy = item.Data.GetValueOrDefault("declaredTargets", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (legacy.Length >= 2) { entryId = legacy[0]; slot = legacy[^1]; }
        }
        var target = player.Graveyard.FirstOrDefault(card => card.InstanceId == entryId
            && IsLegalGraveLegionSummonTarget(spec, player, card));
        if (target is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
        {
            RecordTargetSettlementFailure(item, entryId,
                $"已选择的墓地军团或登场位置在逆结算后失效（{spec.Name}）");
            FinishStackItem(item);
            return true;
        }

        _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, target.InstanceId, slot, tapped: false);
        FinishStackItem(item);
        return true;
    }
}
