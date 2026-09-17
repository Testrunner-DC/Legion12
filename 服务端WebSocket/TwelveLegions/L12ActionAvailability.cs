namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private string? ExtendedRangeSourceUnavailableReason(L12PlayerState player, L12CardInstance source)
        => FindOnField(player, source.InstanceId, out var row, out _) is not { } current
            || !IsFieldLegion(current) || current.Hidden || row != 1
            ? "该效果只能由我方后排的公开军团发动" : null;

    private static bool HasUsedLimitedActiveAbility(L12PlayerState player, string cardId, string instanceId, string ability)
        => !L12StructuredCardRules.IsActiveRestAbility(cardId, ability)
            && !(ability == "extendedRange" && L12StructuredCardSemantics.HasBackRowExtendedRangeActive(cardId))
            && player.UsedAbilities.Contains(ActiveAbilityUsageKey(instanceId, cardId, ability));

    // Hand placement is not the activation cost printed in a counter's effect.
    // Both the authoritative hand preview and SetCounterTactic use this rule.
    private int CounterTacticPlacementCost(L12PlayerState player)
        => State.ActiveDisaster?.CardId == "S01-DS03" || player.FreeTacticCount > 0 ? 0 : 2;
}
