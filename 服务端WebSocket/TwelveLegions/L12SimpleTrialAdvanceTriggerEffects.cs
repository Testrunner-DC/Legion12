namespace TwelveLegions.Server;

internal sealed record L12SimpleTrialAdvanceTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string Name,
    int Amount,
    string SettlementText);

internal static class L12SimpleTrialAdvanceTriggerEffects
{
    internal static readonly L12SimpleTrialAdvanceTriggerSpec[] All =
    [
        new("S02-0609", 3, "death", "侍从骑士", Amount: 1,
            SettlementText: "阵亡时 试炼+1。"),
        new("ST06-06", 2, "death", "费奥纳的骑士", Amount: 2,
            SettlementText: "阵亡时 试炼+2。"),
    ];

    internal static L12SimpleTrialAdvanceTriggerSpec? Find(string cardId, string trigger)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
}
