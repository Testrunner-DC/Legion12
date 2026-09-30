namespace TwelveLegions.Server;

/// <summary>
/// Pure recipient-visibility rules shared by live snapshots and persisted player replays.
/// This layer must not advance the engine or mutate authoritative state.
/// </summary>
internal static class L12RecipientVisibility
{
    private static readonly HashSet<string> PublicCombatKinds = new(StringComparer.Ordinal)
    {
        "attack", "defense", "defense-invalid", "support", "combat", "attack-aborted", "attack-ended",
    };
    private static readonly HashSet<string> PublicCombatOutcomes = new(StringComparer.Ordinal)
    {
        "declared", "blocked", "unblocked", "supported", "invalid-block", "invalid-support",
        "defeated", "not-defeated", "aborted", "completed", "unknown",
    };
    private static readonly HashSet<string> PublicCombatReasons = new(StringComparer.Ordinal)
    {
        "attacker-left", "target-left", "choice-unavailable", "context-unavailable",
        "extra-cost-unpaid", "thunder-roll-failed", "unknown",
    };

    private static L12ActionEvent ProjectCombatEvent(L12ActionEvent actionEvent)
    {
        var combat = actionEvent.PlayerCombat;
        if (combat is null) return actionEvent;
        var visibleIds = actionEvent.Cards.Where(card => !card.Hidden && !string.IsNullOrWhiteSpace(card.Name))
            .Select(card => card.InstanceId).ToHashSet(StringComparer.Ordinal);
        return actionEvent with
        {
            PlayerCombat = combat with
            {
                CombatId = combat.CombatId is { Length: > 0 and <= 64 } id
                    && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') ? id : null,
                EventKind = combat.EventKind is { } kind && PublicCombatKinds.Contains(kind) ? kind : "unknown",
                OutcomeCode = combat.OutcomeCode is { } outcome && PublicCombatOutcomes.Contains(outcome)
                    ? outcome : "unknown",
                PublicReasonCode = combat.PublicReasonCode is { } reason && PublicCombatReasons.Contains(reason)
                    ? reason : "unknown",
                AttackerInstanceId = combat.AttackerInstanceId is { } attacker && visibleIds.Contains(attacker)
                    ? attacker : null,
                TargetInstanceId = combat.TargetInstanceId is { } target && visibleIds.Contains(target)
                    ? target : null,
                AttackerTroops = combat.AttackerTroops is >= 0
                    && combat.AttackerInstanceId is { } attackValueSource && visibleIds.Contains(attackValueSource)
                    ? combat.AttackerTroops : null,
                DefenderTroops = combat.DefenderTroops is >= 0
                    && combat.TargetInstanceId is { } defenseValueSource && visibleIds.Contains(defenseValueSource)
                    ? combat.DefenderTroops : null,
                MasterDamage = combat.MasterDamage is >= 0 ? combat.MasterDamage : null,
            },
        };
    }

    private static L12ActionEvent ProjectBattlefieldMovementEvent(L12ActionEvent actionEvent)
    {
        var movement = actionEvent.PlayerBattlefieldMovement;
        if (movement is null) return actionEvent;
        if (actionEvent.Type is not ("move" or "faction-effect"))
            return actionEvent with { PlayerBattlefieldMovement = null };
        var visibleIds = actionEvent.Cards
            .Where(card => !card.Hidden && !string.IsNullOrWhiteSpace(card.Name))
            .Select(card => card.InstanceId).ToHashSet(StringComparer.Ordinal);
        var safe = (movement.Facts ?? [])
            .Where(fact => fact.InstanceId is { Length: > 0 } id && visibleIds.Contains(id)
                && fact.BattlefieldPlayerIndex is >= 0 and <= 1
                && fact.FromRow is >= 0 and <= 1 && fact.ToRow is >= 0 and <= 1
                && fact.FromSlot is >= 0 and <= 2 && fact.ToSlot is >= 0 and <= 2
                && (fact.FromRow != fact.ToRow || fact.FromSlot != fact.ToSlot))
            .ToArray();
        return actionEvent with
        {
            PlayerBattlefieldMovement = safe.Length == 0 ? null : new(safe),
        };
    }

    private static L12ActionEvent ProjectPublicPlacementEvent(L12ActionEvent actionEvent)
    {
        var placement = actionEvent.PlayerPublicPlacement;
        if (placement is null) return actionEvent;
        var matches = actionEvent.Cards.Where(candidate =>
            candidate.InstanceId == placement.InstanceId).Take(2).ToArray();
        var valid = actionEvent.Type == "put" && matches.Length == 1
            && !matches[0].Hidden && !string.IsNullOrWhiteSpace(matches[0].Name)
            && !string.IsNullOrWhiteSpace(placement.InstanceId)
            && placement.OwnerPlayerIndex is >= 0 and <= 1
            && placement.ControllerPlayerIndex is >= 0 and <= 1
            && placement.OwnerPlayerIndex != placement.ControllerPlayerIndex
            && placement.Row is >= 0 and <= 1 && placement.Slot is >= 0 and <= 2
            && placement.Tapped is not null && matches[0].Tapped == placement.Tapped
            && matches[0].OwnerIndex == placement.OwnerPlayerIndex
            && placement.DurationCode is null or "until-owner-next-turn-end";
        return valid ? actionEvent : actionEvent with { PlayerPublicPlacement = null };
    }
    internal readonly record struct Policy(bool BothHands, bool CoveredBattlefieldIdentity,
        bool AllDisasters, bool PrivatePrompts, bool PrivateHandEvents, bool DeckOrder,
        bool LegalActions)
    {
        internal static Policy Player => new(false, false, false, false, false, false, true);
        internal static Policy Gm => new(true, true, true, true, true, true, true);
        internal static Policy PublicSpectator => new(false, false, false, false, false, false, false);
        internal static Policy Referee => new(true, true, true, false, false, false, false);
    }

    internal static bool CanSeeDisaster(L12GameState state, L12CardInstance card, int viewer,
        bool revealAllDisasters)
    {
        if (revealAllDisasters) return true;
        if (state.ActiveDisaster?.InstanceId == card.InstanceId
            || state.RemovedDisasters.Any(item => item.InstanceId == card.InstanceId)
            || state.RevealedDisasters.Any(item => item.InstanceId == card.InstanceId))
            return true;
        var owner = state.ChosenDisasterOwners.GetValueOrDefault(card.InstanceId,
            card.OwnerIndex ?? -1);
        return viewer >= 0 && owner == viewer;
    }

    internal static L12ActionEvent ProjectActionEvent(L12GameState state,
        L12ActionEvent actionEvent, int viewer, bool revealAllDisasters,
        bool revealAllHands = false)
    {
        actionEvent = ProjectPublicPlacementEvent(ProjectBattlefieldMovementEvent(
            ProjectCombatEvent(L12TrialProgressVisibility.PublicEvent(actionEvent))));
        if (actionEvent.Type == "private-return")
            return revealAllHands || actionEvent.PlayerIndex == viewer
                ? actionEvent with { Type = "return" }
                : new L12ActionEvent(actionEvent.Sequence, "return", actionEvent.PlayerIndex,
                    "放回1张牌", []);
        if (!revealAllDisasters && actionEvent.Type == "private-disaster-reveal"
            && actionEvent.PlayerIndex != viewer)
        {
            var viewingPlayerName = actionEvent.PlayerIndex is >= 0 and <= 1
                ? state.Players[actionEvent.PlayerIndex.Value].Name
                : "玩家";
            return new L12ActionEvent(actionEvent.Sequence, actionEvent.Type,
                actionEvent.PlayerIndex, $"{viewingPlayerName}查看了下一张天灾", []);
        }
        if (revealAllDisasters || actionEvent.Type != "disaster-selected"
            || actionEvent.Cards.Length == 0)
            return actionEvent;

        var visibleCards = actionEvent.Cards
            .Where(card => CanSeeDisaster(state, card, viewer, revealAllDisasters))
            .Select(card => card.Clone())
            .ToArray();
        if (visibleCards.Length == actionEvent.Cards.Length) return actionEvent;

        var playerName = actionEvent.PlayerIndex is >= 0 and <= 1
            ? state.Players[actionEvent.PlayerIndex.Value].Name
            : "玩家";
        return new L12ActionEvent(actionEvent.Sequence, actionEvent.Type,
            actionEvent.PlayerIndex, $"{playerName} 已完成天灾选择", visibleCards);
    }
}
