namespace TwelveLegions.Server;

/// <summary>
/// A set counter tactic spends its response lifecycle only when its initial effect is legally
/// placed on the stack. Trigger snapshots preserve already-stacked settlement, but never grant a
/// second initial activation after the physical card has left its covered back-line slot.
/// </summary>
public sealed partial class L12GameEngine
{
    private const string CounterTacticResponseLifecycleConsumed = "counterTacticResponseLifecycleConsumed";

    private bool IsFreshSetCounterTacticTrigger(L12TriggerCandidate candidate)
        => IsCounterTactic(candidate.SourceCardId)
            && candidate.Trigger is "reaction" or "trojan-after-attack";

    private bool CanDeclareFreshSetCounterTacticTrigger(L12TriggerCandidate candidate)
    {
        if (!IsFreshSetCounterTacticTrigger(candidate)) return true;
        if (candidate.Data.ContainsKey(CounterTacticResponseLifecycleConsumed)) return false;
        if (candidate.Controller < 0 || candidate.Controller >= State.Players.Length) return false;

        return State.Players[candidate.Controller].Field[1].Any(card =>
            card is { Hidden: true, CardType: "tactic" }
            && card.InstanceId.Equals(candidate.SourceInstanceId, StringComparison.OrdinalIgnoreCase)
            && card.CardId.Equals(candidate.SourceCardId, StringComparison.OrdinalIgnoreCase)
            && card.CannotRespondUntilRound < State.Round
            && IsCounterTactic(card.CardId));
    }

    private static void MarkFreshSetCounterCandidateConsumed(L12TriggerCandidate candidate)
        => candidate.Data[CounterTacticResponseLifecycleConsumed] = "true";

    private static bool IsSameFreshSetCounterSource(L12TriggerCandidate candidate,
        L12TriggerCandidate consumed)
        => candidate.Controller == consumed.Controller
            && candidate.SourceInstanceId.Equals(consumed.SourceInstanceId, StringComparison.OrdinalIgnoreCase)
            && candidate.SourceCardId.Equals(consumed.SourceCardId, StringComparison.OrdinalIgnoreCase)
            && consumed.Trigger is "reaction" or "trojan-after-attack"
            && candidate.Trigger is "reaction" or "trojan-after-attack";

    private void ConsumeFreshSetCounterResponseLifecycle(L12TriggerCandidate consumed)
    {
        MarkFreshSetCounterCandidateConsumed(consumed);

        void CloseQueuedCandidate(L12TriggerCandidate stale)
        {
            MarkFreshSetCounterCandidateConsumed(stale);
            CleanupPublicTriggerReservation(stale);
            AddFreshSetCounterLifecycleEvent("effect-skipped", stale,
                $"〈{stale.SourceName}〉的盖伏来源已完成一次合法入栈，跳过旧响应时机");
        }

        var pendingStackCandidates = State.PendingTriggerStackCandidates
            .Where(candidate => !ReferenceEquals(candidate, consumed)
                && IsSameFreshSetCounterSource(candidate, consumed))
            .ToArray();
        foreach (var stale in pendingStackCandidates)
        {
            State.PendingTriggerStackCandidates.Remove(stale);
            CloseQueuedCandidate(stale);
        }

        for (var index = State.PendingTriggerBatches.Count - 1; index >= 0; index--)
        {
            var batch = State.PendingTriggerBatches[index];
            var staleCandidates = batch.Candidates.Where(candidate =>
                    !ReferenceEquals(candidate, consumed)
                    && IsSameFreshSetCounterSource(candidate, consumed))
                .ToArray();
            foreach (var stale in staleCandidates)
            {
                batch.Candidates.Remove(stale);
                CloseQueuedCandidate(stale);
            }
            if (batch.Candidates.Count == 0) State.PendingTriggerBatches.RemoveAt(index);
        }
    }

    private void PruneInvalidFreshSetCounterTriggerBatches()
    {
        for (var index = State.PendingTriggerBatches.Count - 1; index >= 0; index--)
        {
            var batch = State.PendingTriggerBatches[index];
            var invalid = batch.Candidates.Where(candidate =>
                    IsFreshSetCounterTacticTrigger(candidate)
                    && !CanDeclareFreshSetCounterTacticTrigger(candidate))
                .ToArray();
            foreach (var candidate in invalid)
            {
                MarkFreshSetCounterCandidateConsumed(candidate);
                CleanupPublicTriggerReservation(candidate);
                batch.Candidates.Remove(candidate);
                AddFreshSetCounterLifecycleEvent("effect-skipped", candidate,
                    $"〈{candidate.SourceName}〉已不再是可发动的盖伏来源，未进入触发排序");
            }
            if (batch.Candidates.Count == 0) State.PendingTriggerBatches.RemoveAt(index);
        }
    }

    private void AddFreshSetCounterLifecycleEvent(string type, L12TriggerCandidate candidate, string text)
        => AddEvent(candidate.SourceSnapshot is null || candidate.SourceSnapshot.Hidden
                ? $"private-trigger-{type}" : type,
            candidate.Controller, text);
}
