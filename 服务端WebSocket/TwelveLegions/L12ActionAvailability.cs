namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private string? ExtendedRangeSourceUnavailableReason(L12PlayerState player, L12CardInstance source)
        => FindOnField(player, source.InstanceId, out var row, out _) is not { } current
            || !IsFieldLegion(current) || current.Hidden || row != 1
            ? "该效果只能由我方后排的公开军团发动" : null;

    private bool HasUsedLimitedActiveAbility(L12PlayerState player, string cardId, string instanceId, string ability)
    {
        var canonical = _catalog.MoraleIdentities.CanonicalEffectCardId(cardId);
        return L12ActiveUsageRules.Find(canonical, ability) is not null
            && player.UsedAbilities.Contains(ActiveAbilityUsageKey(instanceId, canonical, ability));
    }

    private void RecordLimitedActiveAbilityUse(L12PlayerState player, L12CardInstance source, string ability)
    {
        var canonical = _catalog.MoraleIdentities.CanonicalEffectCardId(source.CardId);
        if (L12ActiveUsageRules.Find(canonical, ability) is not null)
            player.UsedAbilities.Add(ActiveAbilityUsageKey(source.InstanceId, canonical, ability));
    }

    private string? UsedMasterAbilityUsageKey(L12PlayerState player, string ability)
    {
        var key = L12ActiveUsageRules.Find(player.MasterId, ability) is not null
            ? ActiveAbilityUsageKey($"master-{player.PlayerIndex}", player.MasterId, ability)
            : L12MasterTriggeredUsageRules.Find(player.MasterId, ability)?.Key(player.PlayerIndex, State.TurnSerial);
        return key is not null && player.UsedAbilities.Contains(key) ? key : null;
    }

    private L12AbilityView[] UsedMasterUsageResetChoices(L12PlayerState player)
        => GetAbilities(player.MasterId)
            .Where(view => !view.TriggerOnly && HasUsedLimitedActiveAbility(player, player.MasterId, $"master-{player.PlayerIndex}", view.Id))
            .GroupBy(view => ActiveAbilityUsageKey($"master-{player.PlayerIndex}", player.MasterId, view.Id))
            .Select(group => group.First() with { Label = string.Join(" / ", group.Select(view => view.Label)) })
            .Concat(L12MasterTriggeredUsageRules.All.Where(rule => rule.CardId == player.MasterId
                    && UsedMasterAbilityUsageKey(player, rule.Ability) is not null)
                .Select(rule => new L12AbilityView(rule.Ability, _catalog.AtomicEffects.Find(rule.CardId)!.Abilities
                    .Single(ability => ability.Sequence == rule.AbilitySequence).Text)))
            .ToArray();

    // Hand placement is not the activation cost printed in a counter's effect.
    // Both the authoritative hand preview and SetCounterTactic use this rule.
    private int CounterTacticPlacementCost(L12PlayerState player)
        => L12ActiveDisasterRules.CounterTacticsAreFree(State.ActiveDisaster?.CardId) || player.FreeTacticCount > 0 ? 0 : 2;
}
