namespace TwelveLegions.Server;

internal sealed record L12SimpleDrawTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string Name,
    bool Optional,
    string? Condition,
    string DrawRecipient,
    string SettlementText,
    string EventText,
    string EmptyLossReason);

internal static class L12SimpleDrawTriggerEffects
{
    internal static readonly L12SimpleDrawTriggerSpec[] All =
    [
        new("S01-0004", 3, "death", "无名的渗透者", Optional: false, Condition: null,
            DrawRecipient: "source-owner", EventText: "无名的渗透者阵亡时，其所有者抽取1张牌",
            SettlementText: "阵亡时 此军团的所有者抽取1张牌。",
            EmptyLossReason: "无名的渗透者所有者因阵亡效果抽牌时牌库为空"),
        new("S01-0110", 3, "death", "墨子", Optional: false, Condition: null,
            DrawRecipient: "controller", EventText: "墨子阵亡时抽取1张牌",
            SettlementText: "阵亡时 抽取1张牌。",
            EmptyLossReason: "墨子阵亡效果抽牌时牌库为空"),
        new("S01-0301", 4, "death", "贝奥武夫", Optional: true, Condition: null,
            DrawRecipient: "controller", EventText: "贝奥武夫阵亡时抽取1张牌",
            SettlementText: "阵亡时 可抽取1张牌。",
            EmptyLossReason: "贝奥武夫阵亡效果抽牌时牌库为空"),
        new("S01-0309", 3, "death", "布伦希尔德", Optional: true,
            Condition: "controller.hp<=opponent.hp", DrawRecipient: "controller",
            EventText: "布伦希尔德阵亡时抽取1张牌",
            SettlementText: "阵亡时 若我方主宰血量不高于对方，可抽取1张牌。",
            EmptyLossReason: "布伦希尔德阵亡效果抽牌时牌库为空"),
        new("S02-0203", 3, "death", "哈特谢普苏特", Optional: true, Condition: null,
            DrawRecipient: "controller", EventText: "哈特谢普苏特阵亡时抽取1张牌",
            SettlementText: "阵亡时 可抽取1张牌。",
            EmptyLossReason: "哈特谢普苏特阵亡效果抽牌时牌库为空"),
        new("S02-0402", 2, "death", "井伊直虎", Optional: true, Condition: null,
            DrawRecipient: "controller", EventText: "井伊直虎阵亡时抽取1张牌",
            SettlementText: "阵亡时 可抽取1张牌。",
            EmptyLossReason: "井伊直虎阵亡效果抽牌时牌库为空"),
        new("S02-0512", 4, "death", "埃涅阿斯", Optional: true, Condition: null,
            DrawRecipient: "controller", EventText: "埃涅阿斯阵亡时抽取1张牌",
            SettlementText: "阵亡时 可抽取1张牌。",
            EmptyLossReason: "埃涅阿斯阵亡效果抽牌时牌库为空"),
    ];

    internal static L12SimpleDrawTriggerSpec? Find(string cardId, string trigger)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
}
