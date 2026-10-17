namespace TwelveLegions.Server;

// Observation only: never part of State, player events, RNG or the card-fact cursor.
internal sealed record L12DisasterCompositionCard(string CardId, string Source, int? OwnerIndex);
internal sealed record L12DisasterCaptureSeed(string Mode, string EffectVersion, string[] Pool);
internal sealed record L12DisasterSetupObservation(string Kind, int Step, int? Actor = null,
    string[]? Candidates = null, string? CardId = null, L12DisasterCompositionCard[]? Composition = null);

public sealed partial class L12GameEngine
{
    private L12DisasterCaptureSeed? _disasterCaptureSeed;
    private readonly List<L12DisasterSetupObservation> _disasterSetupObservations = [];
    internal bool? DisasterCaptureRegistered { get; set; }
    internal L12DisasterCaptureSeed? DisasterCaptureSeed => _disasterCaptureSeed;
    internal IReadOnlyList<L12DisasterSetupObservation> DisasterSetupObservations => _disasterSetupObservations;
    internal void ClearDisasterSetupObservations() => _disasterSetupObservations.Clear();

    private void ObserveDisasterCaptureStart()
    {
        if (State.StateFormatVersion < L12PersistenceContract.MinimumCheckpointRecoveryVersion) return;
        _disasterCaptureSeed = new(State.DisasterMode, AnalyticsEffectVersion,
            State.DisasterPool.Select(card => card.CardId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray());
    }

    private void ObserveDisasterOffer(L12Prompt prompt)
    {
        var byInstance = State.DisasterPool.ToDictionary(card => card.InstanceId, card => card.CardId,
            StringComparer.OrdinalIgnoreCase);
        _disasterSetupObservations.Add(new("offer", State.DisasterPreparationStep, prompt.PlayerIndex,
            prompt.ValidChoices.Select(id => byInstance[id]).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray()));
    }

    private void ObserveDisasterDecision(int actor, L12CardInstance card)
        => _disasterSetupObservations.Add(new("decision", State.DisasterPreparationStep, actor,
            CardId: card.CardId));

    private void ObserveDisasterPublic(L12CardInstance card)
        => _disasterSetupObservations.Add(new("public", 3, CardId: card.CardId));

    private void ObserveDisasterComposition()
    {
        var composition = State.DisasterDeck.Select(card =>
        {
            if (card.CardId == L12ActiveDisasterRules.AnnihilationCardId)
                return new L12DisasterCompositionCard(card.CardId, "fixed", null);
            if (State.DisasterMode == "random") return new(card.CardId, "random-mode", null);
            return State.ChosenDisasterOwners.TryGetValue(card.InstanceId, out var owner)
                ? new(card.CardId, "player-pick", owner)
                : new(card.CardId, "random-public", null);
        }).OrderBy(card => card.CardId, StringComparer.Ordinal).ToArray();
        _disasterSetupObservations.Add(new("complete", 6, Composition: composition));
    }
}
