namespace TwelveLegions.Server;

internal sealed record L12HandLegionSummonTriggerSpec(
    string CardId,
    string Trigger,
    string Name,
    string TargetRule,
    bool Optional,
    int MinimumSelection,
    bool Tapped,
    string PromptText);

public sealed partial class L12GameEngine
{
    private static readonly IReadOnlyDictionary<string, L12HandLegionSummonTriggerSpec> HandLegionSummonTriggerSpecs =
        new Dictionary<string, L12HandLegionSummonTriggerSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0407|death"] = new("S01-0407", "death", "坂本龙马", "takamagahara-cost-at-most-3",
                Optional: false, MinimumSelection: 0, Tapped: true,
                "坂本龙马：选择手牌中最多1张当前费用不高于3的【高天原】军团休整登场"),
            ["S02-0601|death"] = new("S02-0601", "death", "亚瑟王", "round-table-cost-at-most-4",
                Optional: true, MinimumSelection: 1, Tapped: false,
                "亚瑟王：选择手牌中1张当前费用不高于4的【圆桌骑士】军团活跃登场"),
        };

    private static bool TryGetHandLegionSummonTriggerSpec(string cardId, string trigger,
        out L12HandLegionSummonTriggerSpec spec)
        => HandLegionSummonTriggerSpecs.TryGetValue($"{cardId}|{trigger}", out spec!);

    private static bool IsLegalHandLegionSummonTarget(L12HandLegionSummonTriggerSpec spec,
        L12PlayerState player, L12CardInstance card)
    {
        if (card.CardType != "legion") return false;
        return spec.TargetRule switch
        {
            "takamagahara-cost-at-most-3" => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")
                && L12StructuredCardRules.CurrentCostAtMost(card, 3),
            "round-table-cost-at-most-4" => L12StructuredCardRules.EffectiveTraits(player, card)
                .Contains("圆桌骑士") && L12StructuredCardRules.CurrentCostAtMost(card, 4),
            _ => false,
        };
    }

    private static L12CardInstance[] LegalHandLegionSummonTargets(
        L12HandLegionSummonTriggerSpec spec, L12PlayerState player)
        => player.Hand.Where(card => IsLegalHandLegionSummonTarget(spec, player, card)).ToArray();

    private bool TryResolveHandLegionSummonTrigger(L12StackItem item, L12CardInstance source)
    {
        if (!TryGetHandLegionSummonTriggerSpec(source.CardId, item.Trigger, out var spec)) return false;
        if (!item.Data.ContainsKey("declared:entryCard") && !item.Data.ContainsKey("declaredTargets"))
            return false;

        var player = State.Players[item.Controller];
        var entryId = PublicTriggerDeclared(item, "entryCard");
        var slot = PublicTriggerDeclared(item, "entrySlot");
        if (string.IsNullOrWhiteSpace(entryId) && item.Data.TryGetValue("declaredTargets", out var declared))
        {
            var legacy = declared.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (legacy.Length >= 2) { entryId = legacy[0]; slot = legacy[^1]; }
        }

        if (string.IsNullOrWhiteSpace(entryId) && spec.MinimumSelection == 0)
        {
            FinishStackItem(item);
            return true;
        }

        var target = player.Hand.FirstOrDefault(card => card.InstanceId == entryId
            && IsLegalHandLegionSummonTarget(spec, player, card));
        if (target is null || string.IsNullOrWhiteSpace(slot)
            || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
        {
            RecordTargetSettlementFailure(item, entryId,
                $"已选择的手牌军团或登场位置在逆结算后失效（{spec.Name}）");
            FinishStackItem(item);
            return true;
        }

        SummonFromHand(player, target.InstanceId, slot, spec.Tapped);
        FinishStackItem(item);
        return true;
    }
}
