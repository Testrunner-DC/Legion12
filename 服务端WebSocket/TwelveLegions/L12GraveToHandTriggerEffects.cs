namespace TwelveLegions.Server;

internal sealed record L12GraveToHandTriggerSpec(
    string CardId,
    string Trigger,
    string Name,
    string TargetRule,
    string PromptText,
    string TargetDescription,
    bool Optional);

public sealed partial class L12GameEngine
{
    /// <summary>
    /// “阵亡时从墓地选择1张牌加入手牌”的权威规格。候选、声明、入栈校验、
    /// 逆序结算复核、公开展示和日志均读取这里，卡牌结算器不再各写一套筛选条件。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, L12GraveToHandTriggerSpec> GraveToHandTriggerSpecs =
        new Dictionary<string, L12GraveToHandTriggerSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0112|death"] = new("S01-0112", "death", "孙武", "tactic-cost-at-most-4",
                "孙武：选择墓地1张当前费用不高于4的战术卡加入手牌",
                "当前费用不高于4的战术卡", Optional: true),
            ["S01-0307|death"] = new("S01-0307", "death", "阿尔维达", "asgard-cost-at-most-3",
                "阿尔维达：选择墓地1张当前费用不高于3的【阿斯加德】卡牌加入手牌",
                "当前费用不高于3的【阿斯加德】卡牌", Optional: false),
            ["S02-0518|death"] = new("S02-0518", "death", "忒修斯", "promoted-legion",
                "忒修斯：选择墓地1张【晋升者】军团加入手牌",
                "【晋升者】军团", Optional: true),
        };

    private static bool TryGetGraveToHandTriggerSpec(string cardId, string trigger,
        out L12GraveToHandTriggerSpec spec)
        => GraveToHandTriggerSpecs.TryGetValue($"{cardId}|{trigger}", out spec!);

    private bool GraveToHandTriggerConditionMet(L12GraveToHandTriggerSpec spec)
        => spec.CardId != "S01-0112" || State.DisasterValue <= 4;

    private static bool IsLegalGraveToHandTarget(L12GraveToHandTriggerSpec spec,
        L12PlayerState player, L12CardInstance card)
    {
        if (!CanEnterHandOrLibrary(card)) return false;
        return spec.TargetRule switch
        {
            "tactic-cost-at-most-4" => card.CardType == "tactic"
                && L12StructuredCardRules.CurrentCostAtMost(card, 4),
            "asgard-cost-at-most-3" => L12StructuredCardRules.HasFaction(player, card, "asgard")
                && L12StructuredCardRules.CurrentCostAtMost(card, 3),
            "promoted-legion" => card.CardType == "legion"
                && L12StructuredCardRules.EffectiveTraits(player, card).Contains("晋升者"),
            _ => false,
        };
    }

    private static L12CardInstance[] LegalGraveToHandTargets(L12GraveToHandTriggerSpec spec,
        L12PlayerState player)
        => player.Graveyard.Where(card => IsLegalGraveToHandTarget(spec, player, card)).ToArray();

    /// <summary>
    /// 只接管已经完成公共声明的新流程及带声明字段的历史检查点；没有声明数据的旧检查点
    /// 仍交给原结算分支补选，以保证部署后可以继续恢复未完成对局。
    /// </summary>
    private bool TryResolveGraveToHandTrigger(L12StackItem item, L12CardInstance source)
    {
        if (!TryGetGraveToHandTriggerSpec(source.CardId, item.Trigger, out var spec)) return false;
        if (!item.Data.ContainsKey("declared:recoverTarget") && !item.Data.ContainsKey("declaredTargets"))
            return false;

        if (PublicTriggerDeclared(item, "mode") == "mode:none")
        {
            FinishStackItem(item);
            return true;
        }

        var player = State.Players[item.Controller];
        var targetId = PublicTriggerDeclared(item, "recoverTarget");
        if (string.IsNullOrWhiteSpace(targetId))
            targetId = item.Data.GetValueOrDefault("declaredTargets", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries).SingleOrDefault() ?? string.Empty;
        var target = player.Graveyard.FirstOrDefault(card => card.InstanceId == targetId
            && IsLegalGraveToHandTarget(spec, player, card));
        if (target is null)
        {
            RecordTargetSettlementFailure(item, targetId,
                $"已选择的墓地对象已离开墓地，或不再是{spec.TargetDescription}");
            FinishStackItem(item);
            return true;
        }

        player.Graveyard.Remove(target);
        PubliclyRevealThenAddCardToHandByEffect(player, target, "graveyard",
            $"{spec.Name}展示墓地的〈{target.Name}〉并加入手牌",
            $"{spec.Name}将〈{target.Name}〉从墓地加入手牌", spec.CardId, "grave-hit");
        AddEvent("return", item.Controller, $"{target.Name}从墓地回到手牌", target);
        FinishStackItem(item);
        return true;
    }
}
