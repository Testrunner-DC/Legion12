namespace TwelveLegions.Server;

internal sealed record L12SimpleSelfTroopBuffTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string PlanId,
    string Name,
    string CostKind,
    int Amount,
    string SettlementText,
    string PromptText,
    string EventText);

/// <summary>
/// 进攻时支付既定费用、完整效果仅令来源军团在本回合增加固定兵力的权威规格。
/// 费用种类仍由公开进攻声明器统一选择、复验和预付；本规格统一卡牌/能力身份、
/// 发动文案、结算数值、来源重验、日志、单段场景与细原子程序。
/// </summary>
internal static class L12SimpleSelfTroopBuffTriggerEffects
{
    internal static readonly L12SimpleSelfTroopBuffTriggerSpec[] All =
    [
        new("S01-0301", 3, "attack", "beowulf", "贝奥武夫", "master-damage", 2000,
            "进攻时 可对我方主宰造成1点伤害：此军团本回合兵力+2000。",
            "贝奥武夫：是否对我方主宰造成1点伤害，使此军团本回合兵力+2000？",
            "贝奥武夫本回合兵力+2000"),
        new("S01-0311", 1, "attack", "gustav", "古斯塔夫一世", "grave-bottom-two", 2000,
            "进攻时 可将墓地2张卡牌自选顺序返回我方牌库底部：此军团本回合兵力+2000。",
            "古斯塔夫一世：是否将墓地2张卡牌自选顺序返回牌库底部，使此军团本回合兵力+2000？",
            "古斯塔夫一世本回合兵力+2000"),
        new("S02-0509", 3, "attack", "odysseus", "奥德修斯", "show-hand-tactic", 1000,
            "进攻时 可展示手牌中的1张战术卡：此军团本回合兵力+1000。",
            "奥德修斯：是否展示手牌中的1张战术卡，使此军团本回合兵力+1000？",
            "奥德修斯本回合兵力+1000"),
        new("S02-0517", 3, "attack", "penthesilea", "彭忒西勒亚", "god-power", 2000,
            "进攻时 可消耗并翻转1神力：本回合兵力+2000。",
            "彭忒西勒亚：是否消耗并翻转1神力，使此军团本回合兵力+2000？",
            "彭忒西勒亚本回合兵力+2000"),
        new("S02-0519", 1, "attack", "spartan", "斯巴达勇士", "god-power", 2000,
            "进攻时 可消耗并翻转1神力：此军团本回合兵力+2000。",
            "斯巴达勇士：是否消耗并翻转1神力，使此军团本回合兵力+2000？",
            "斯巴达勇士本回合兵力+2000"),
        new("S02-0606", 3, "attack", "percival", "帕西瓦尔", "discard-hand", 2000,
            "进攻时 可弃置1张手牌：本回合兵力+2000。",
            "帕西瓦尔：是否弃置1张手牌，使此军团本回合兵力+2000？",
            "帕西瓦尔本回合兵力+2000"),
    ];

    internal static L12SimpleSelfTroopBuffTriggerSpec? Find(string cardId, string trigger)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));

}
