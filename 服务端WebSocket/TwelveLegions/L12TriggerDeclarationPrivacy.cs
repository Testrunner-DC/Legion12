namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private const string PrivateTriggerDeclaration = "privateTriggerDeclaration";

    // Persist the event-time privacy decision; a later reveal must not disclose a prior decline.
    private bool IsPrivateTriggerSource(L12CardInstance? source)
        => source is not null && (source.Hidden || State.Players.Any(player =>
            player.Hand.Any(card => card.InstanceId == source.InstanceId)));

    private bool IsPrivateTriggerCandidate(L12TriggerCandidate candidate)
        => IsPrivateTriggerSource(FindAuthoritativeCard(candidate.SourceInstanceId)
            ?? candidate.SourceSnapshot);

    private bool IsPrivateTriggerActivation(L12PendingActivation activation)
        => activation.TriggerCandidateId is not null
            && IsPrivateTriggerSource(FindAuthoritativeCard(activation.SourceInstanceId)
                ?? State.PendingTriggerStackCandidates.FirstOrDefault(candidate =>
                    candidate.CandidateId == activation.TriggerCandidateId)?.SourceSnapshot);

    private void AddTriggerDeclarationEvent(string type, L12TriggerCandidate candidate,
        string text, params L12CardInstance[] cards)
        => AddEvent(IsPrivateTriggerCandidate(candidate) ? "private-trigger-" + type : type,
            candidate.Controller, text, cards);
}
