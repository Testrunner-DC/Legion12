namespace TwelveLegions.Server;

/// <summary>
/// Pure recipient-visibility rules shared by live snapshots and persisted player replays.
/// This layer must not advance the engine or mutate authoritative state.
/// </summary>
internal static class L12RecipientVisibility
{
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
        actionEvent = L12TrialProgressVisibility.PublicEvent(actionEvent);
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
