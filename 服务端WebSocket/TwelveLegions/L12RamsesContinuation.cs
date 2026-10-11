namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private const string RamsesChainId = "ramsesChainId";
    private const string RamsesDelegate = "ramsesDelegate";
    private const string RamsesDelegateOrdinal = "ramsesDelegateOrdinal";
    private const string RamsesContinuationEnd = "ramsesContinuationEnd";
    private const string RamsesHeldDeferredStackIds = "ramsesHeldDeferredStackIds";
    private const string RamsesHeldPendingCandidateIds = "ramsesHeldPendingCandidateIds";

    // A terminal batch is durable scheduling metadata, never a card effect. It survives
    // the last delegate so that that delegate's later children still precede old parents.
    private void RegisterRamsesDelegateContinuation(L12StackItem parent,
        IReadOnlyList<(L12CardInstance Card, string Text)> orderedDelegates, bool inheritedCounterProtection)
    {
        if (orderedDelegates.Count == 0) return;
        var heldDeferred = string.Join('|', State.DeferredEffectStack.Select(item => item.StackItemId));
        var heldCandidates = string.Join('|', State.PendingTriggerStackCandidates.Select(item => item.CandidateId));
        var chainId = $"ramses-chain-{++State.TriggerBatchSequence}";
        var batches = new List<L12TriggerBatch>();
        for (var ordinal = 0; ordinal < orderedDelegates.Count; ordinal++)
        {
            var (card, text) = orderedDelegates[ordinal];
            var candidate = CreateTriggerCandidate(parent.Controller, card, "enter", text);
            candidate.Data[DeferredTriggerQualification] = "true";
            candidate.Data[RamsesChainId] = chainId;
            candidate.Data[RamsesDelegate] = "true";
            candidate.Data[RamsesDelegateOrdinal] = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (inheritedCounterProtection)
            {
                candidate.Data["inheritedCounterTacticProtection"] = "true";
                candidate.Data["counterTacticProtectionSourceInstanceId"] = parent.SourceInstanceId;
            }
            batches.Add(new L12TriggerBatch
            {
                BatchId = $"batch-{++State.TriggerBatchSequence}",
                Controller = parent.Controller, Candidates = [candidate],
            });
        }
        var marker = new L12TriggerCandidate
        {
            CandidateId = $"ramses-end-{++State.TriggerBatchSequence}", Controller = parent.Controller,
            SourceInstanceId = parent.SourceInstanceId, SourceCardId = parent.SourceCardId,
            SourceName = parent.SourceName, Trigger = RamsesContinuationEnd, Text = string.Empty,
            Data = new()
            {
                [RamsesChainId] = chainId, [RamsesContinuationEnd] = "true",
                [RamsesHeldDeferredStackIds] = heldDeferred, [RamsesHeldPendingCandidateIds] = heldCandidates,
            },
        };
        batches.Add(new L12TriggerBatch
        {
            BatchId = $"batch-{++State.TriggerBatchSequence}", Controller = parent.Controller, Candidates = [marker],
        });
        State.PendingTriggerBatches.InsertRange(0, batches);
    }

    private (L12TriggerBatch Batch, L12TriggerCandidate Marker)? FindActiveRamsesContinuationEnd()
    {
        foreach (var batch in State.PendingTriggerBatches)
        {
            var marker = batch.Candidates.FirstOrDefault(candidate =>
                candidate.Trigger == RamsesContinuationEnd || candidate.Data.ContainsKey(RamsesContinuationEnd));
            if (marker is null) continue;
            // Do not let damaged internal metadata fall through into qualification or
            // response/presentation. In particular, do not reconstruct missing ID sets.
            if (batch.Candidates.Count != 1 || marker.Trigger != RamsesContinuationEnd
                || marker.Data.GetValueOrDefault(RamsesContinuationEnd) != "true"
                || string.IsNullOrWhiteSpace(marker.Data.GetValueOrDefault(RamsesChainId))
                || marker.Controller != batch.Controller
                || !marker.Data.ContainsKey(RamsesHeldDeferredStackIds)
                || !marker.Data.ContainsKey(RamsesHeldPendingCandidateIds))
                throw new InvalidOperationException("Invalid Ramses continuation boundary.");
            _ = HeldRamsesIds(marker, RamsesHeldDeferredStackIds);
            _ = HeldRamsesIds(marker, RamsesHeldPendingCandidateIds);
            return (batch, marker);
        }
        return null;
    }

    private static IReadOnlySet<string> HeldRamsesIds(L12TriggerCandidate marker, string key)
    {
        var value = marker.Data[key];
        if (value.Length == 0) return new HashSet<string>(StringComparer.Ordinal);
        var ids = value.Split('|');
        var result = new HashSet<string>(ids, StringComparer.Ordinal);
        if (result.Count != ids.Length || ids.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Invalid Ramses held-work boundary.");
        return result;
    }

    private bool InsertPlannedBatchesBeforeRamsesContinuation(IReadOnlyList<L12TriggerBatch> planned)
    {
        if (FindActiveRamsesContinuationEnd() is not { } end) return false;
        var chainId = end.Marker.Data[RamsesChainId];
        var boundary = State.PendingTriggerBatches.FindIndex(batch => batch.Candidates.Any(candidate =>
            candidate.Data.GetValueOrDefault(RamsesChainId) == chainId
            && candidate.Data.GetValueOrDefault(RamsesDelegate) == "true"));
        if (boundary < 0) boundary = State.PendingTriggerBatches.IndexOf(end.Batch);
        State.PendingTriggerBatches.InsertRange(boundary, planned);
        return true;
    }

    private bool TryAdvanceRamsesAheadOfHeldDeferred()
    {
        if (State.DeferredEffectStack.Count == 0 || FindActiveRamsesContinuationEnd() is not { } end
            || !HeldRamsesIds(end.Marker, RamsesHeldDeferredStackIds)
                .Contains(State.DeferredEffectStack[^1].StackItemId)) return false;
        AdvanceTriggerBatches();
        return true;
    }

    private bool TryReleaseRamsesContinuationEnd(L12TriggerBatch batch, L12TriggerCandidate marker)
    {
        var heldCandidates = HeldRamsesIds(marker, RamsesHeldPendingCandidateIds);
        var heldDeferred = HeldRamsesIds(marker, RamsesHeldDeferredStackIds);
        if (State.EffectStack.Count > 0 || State.ResponseWindow is not null || State.IsResolvingStack
            || State.PendingPrompts.Count > 0 || State.PendingActivations.Count > 0
            || State.PendingTriggerStackCandidates.Any(candidate => !heldCandidates.Contains(candidate.CandidateId))
            || State.DeferredEffectStack.Any(item => !heldDeferred.Contains(item.StackItemId)))
            return false;
        State.PendingTriggerBatches.Remove(batch);
        // A generated-interaction no-op can arrive here outside the ordinary finish
        // callback. Schedule the existing pump once; never directly start an old parent.
        RequestStackProgress();
        return true;
    }
}
