namespace TwelveLegions.Server;

internal sealed record L12SimpleMasterHealTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string Name,
    string HealRecipient,
    int Amount,
    string SettlementText,
    string Reason);

internal static class L12SimpleMasterHealTriggerEffects
{
    internal static readonly L12SimpleMasterHealTriggerSpec[] All =
    [
        new("S01-0302", 3, "death", "金发哈拉尔", HealRecipient: "controller", Amount: 1,
            SettlementText: "阵亡时 我方主宰增加1点血量。",
            Reason: "金发哈拉尔阵亡效果"),
        new("S02-0613", 3, "death", "圣女贞德", HealRecipient: "both", Amount: 1,
            SettlementText: "阵亡时 双方主宰增加1点血量。",
            Reason: "圣女贞德阵亡时效果"),
    ];

    internal static L12SimpleMasterHealTriggerSpec? Find(string cardId, string trigger)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
}
