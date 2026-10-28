namespace TwelveLegions.Server;

// An advisory snapshot only: neither classification ends a room or grants a deployment permit.
internal sealed record L12RoomInactivityObservation(DateTimeOffset? LastGameplayActivityUtc,
    bool AutomaticCloseCandidate, bool DeploymentIdleCandidate);

public sealed partial class L12RoomManager
{
    private static bool TracksRoomGameplayActivity(Room room)
        => room.Tutorial is null && room.TournamentId is null
           && (room.IsSandbox ? room.Options.MatchModeId == "sandbox"
               : room.Options.MatchModeId is "friendly" or "casual");

    private void RecordRoomGameplayActivity(Room room)
    {
        if (!TracksRoomGameplayActivity(room)) return;
        var ticks = _utcNow().UtcTicks;
        var previous = Volatile.Read(ref room.LastGameplayActivityUtcTicks);
        // Pregame callers do not all own room.Gate. Never let concurrent calls or a
        // backwards wall-clock correction move the authoritative activity baseline back.
        while (ticks > previous)
        {
            var observed = Interlocked.CompareExchange(ref room.LastGameplayActivityUtcTicks, ticks, previous);
            if (observed == previous) return;
            previous = observed;
        }
    }

    internal L12RoomInactivityObservation? ObserveRoomInactivity(string roomCode,
        DateTimeOffset? utcNow = null)
    {
        if (!_rooms.TryGetValue(roomCode, out var room) || !room.Gate.Wait(0)) return null;
        try
        {
            if (!_rooms.TryGetValue(roomCode, out var current) || !ReferenceEquals(current, room)) return null;
            var ticks = Volatile.Read(ref room.LastGameplayActivityUtcTicks);
            DateTimeOffset? activity = ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
            var eligible = TracksRoomGameplayActivity(room) && !room.Closed
                           && room.Game?.State.Phase != L12Phase.GameOver;
            var now = utcNow ?? _utcNow();
            if (!eligible || activity is null || now < activity.Value)
                return new(activity, false, false);
            var idle = now - activity.Value;
            return new(activity,
                !room.IsSandbox && (room.Options.MatchModeId is "friendly" or "casual")
                    && idle > TimeSpan.FromMinutes(40),
                (room.IsSandbox || room.Options.MatchModeId == "friendly")
                    && idle >= TimeSpan.FromHours(1));
        }
        finally { room.Gate.Release(); }
    }

    private static bool SameGameplayRoomOptions(L12RoomOptions left, L12RoomOptions right)
        => left.MatchModeId == right.MatchModeId && left.Spectating == right.Spectating
           && left.HandVisibility == right.HandVisibility && left.DisasterMode == right.DisasterMode
           && left.UseCardRestrictions == right.UseCardRestrictions;

    private static bool SameGameplayDeck(L12PresetDeckDefinition left, L12PresetDeckDefinition right)
        => ReferenceEquals(left, right) || left.MasterId == right.MasterId
           && left.CardIds.SequenceEqual(right.CardIds) && left.MoraleIds.SequenceEqual(right.MoraleIds)
           && left.SpecialIds.SequenceEqual(right.SpecialIds);

    private readonly record struct GameplayCommandActivity(long Events, long Facts,
        L12Phase Phase, int PreparationStep, int PromptCount);

    private static GameplayCommandActivity CaptureGameplayCommandActivity(L12GameEngine game)
        // The first of two public-disaster confirmations removes a prompt without
        // emitting an event/fact or advancing the shared setup step.
        => new(game.State.EventSequence, game.CardFactSignalSequence, game.State.Phase,
            game.State.DisasterPreparationStep, game.State.PendingPrompts.Count);

    // GM setters emit a log and increment revision even when the value is unchanged.
    // Observe their existing target values, without predicting rules, hashing, or cloning state.
    private readonly record struct GmGameplayActivity(long First, long Second = 0,
        long Third = 0, long Fourth = 0, long Facts = 0);

    private static GmGameplayActivity CaptureGmGameplayActivity(L12GameEngine game, L12GmCommand command)
    {
        if (command.TargetPlayer is < 0 or > 1) return default;
        var state = game.State;
        var player = state.Players[command.TargetPlayer];
        GmGameplayActivity sample;
        switch (command.Type)
        {
            case "setLife": sample = new(player.Hp); break;
            case "setDisaster": sample = new(state.DisasterValue); break;
            case "setCardState":
            case "setTroops":
                var card = FindGameplayActivityCard(player, command.CardInstanceId);
                sample = command.Type == "setCardState" ? new(card?.Tapped == true ? 1 : 0)
                    : new(card?.Troops ?? -1, card?.SetTroopsValue ?? -1, card?.SetTroopsUntilTurn ?? -1);
                break;
            case "readyAll":
            case "restAll":
                var tapped = player.Relic?.Tapped == true ? 1 : 0;
                foreach (var row in player.Field)
                    foreach (var item in row)
                        if (item?.Tapped == true) tapped++;
                foreach (var item in player.ExtraRelics)
                    if (item.Tapped) tapped++;
                sample = new(player.MasterTapped ? 1 : 0, tapped);
                break;
            case "readyMorale":
            case "restMorale": sample = new(player.Morale.Count(item => item.Tapped)); break;
            case "destroyAll":
                var fieldCards = 0;
                foreach (var row in player.Field)
                    foreach (var item in row)
                        if (item is not null) fieldCards++;
                sample = new(fieldCards);
                break;
            case "resetCardEffects": sample = new(player.UsedAbilities.Count); break;
            case "setPhase":
                sample = new((long)state.Phase, state.ActivePlayer,
                    state.PendingPrompts.Count + state.PendingActivations.Count + state.PendingTriggerBatches.Count
                    + state.PendingTriggerStackCandidates.Count + state.EffectStack.Count + state.DeferredEffectStack.Count
                    + state.SuspendedCombatContexts.Count + (state.PendingDefense is null ? 0 : 1)
                    + (state.ResponseWindow is null ? 0 : 1) + (state.IsResolvingStack ? 1 : 0),
                    state.Players[0].Resolving.Count + state.Players[1].Resolving.Count);
                break;
            default: sample = new(state.EventSequence); break;
        }
        return sample with { Facts = game.CardFactSignalSequence };
    }

    private static L12CardInstance? FindGameplayActivityCard(L12PlayerState player, string? instanceId)
    {
        foreach (var row in player.Field)
            foreach (var card in row)
                if (card is not null && card.InstanceId == instanceId) return card;
        if (player.Relic is { } relic && relic.InstanceId == instanceId) return relic;
        foreach (var card in player.ExtraRelics)
            if (card.InstanceId == instanceId) return card;
        return player.Resolving.FirstOrDefault(card => card.InstanceId == instanceId);
    }
}
