namespace TwelveLegions.Server;

internal static class L12LibraryReturnFacts
{
    internal static string PublicText(L12PlayerLibraryReturn fact)
    {
        var parts = new List<string>();
        if (fact.TopCount > 0) parts.Add($"将{fact.TopCount}张牌放回牌库顶部");
        if (fact.BottomCount > 0) parts.Add($"将{fact.BottomCount}张牌放回牌库底部");
        if (fact.ShuffledCount > 0) parts.Add($"将{fact.ShuffledCount}张牌返回牌库并重洗");
        return string.Join("、", parts);
    }
}

public sealed partial class L12GameEngine
{
    // Public sequence + destination owner only. No instance, stack or private ordering identity.
    private string LibraryReturnOperationId(int libraryOwner, long? firstPublishedSequence = null)
        => $"library-return:{libraryOwner}:{firstPublishedSequence ?? State.EventSequence + 1}";

    private void ReplaceLibraryReturnEvent(L12ActionEvent updated)
    {
        for (var index = 0; index < State.Events.Count; index++)
            if (State.Events[index].Sequence == updated.Sequence) State.Events[index] = updated;
        for (var index = 0; index < _unpersistedEvents.Count; index++)
            if (_unpersistedEvents[index].Sequence == updated.Sequence) _unpersistedEvents[index] = updated;
        if (State.LastAction?.Sequence == updated.Sequence) State.LastAction = updated;
        _cachedStateHash = null;
    }

    private void AttachLibraryReturnOperation(long sequence, string operationId)
    {
        if (!State.LibraryReturnFactsProtocolEnabled) return;
        var entry = State.Events.FirstOrDefault(item => item.Sequence == sequence)
            ?? _unpersistedEvents.FirstOrDefault(item => item.Sequence == sequence);
        if (entry is not null) ReplaceLibraryReturnEvent(entry with { PlayerLogGroupId = operationId });
    }

    private void AttachLibraryReturnFact(long sequence, int libraryOwner, int topCount, int bottomCount,
        int shuffledCount, bool identitiesPublic, string? operationId = null)
    {
        if (!State.LibraryReturnFactsProtocolEnabled || topCount + bottomCount + shuffledCount == 0) return;
        if (libraryOwner is < 0 or > 1 || topCount < 0 || bottomCount < 0 || shuffledCount < 0)
            throw new InvalidOperationException("Invalid successful library return fact.");
        var entry = State.Events.FirstOrDefault(item => item.Sequence == sequence)
            ?? _unpersistedEvents.FirstOrDefault(item => item.Sequence == sequence)
            ?? throw new InvalidOperationException("Library return must attach to its actual published event.");
        if (entry.Type is not ("return" or "private-return" or "library" or "reorder" or "leave" or "promotion-stack-leave"))
            throw new InvalidOperationException("Only an actual successful placement/movement event may carry a return receipt.");
        operationId ??= LibraryReturnOperationId(libraryOwner, sequence);
        var prefix = $"library-return:{libraryOwner}:";
        if (!operationId.StartsWith(prefix, StringComparison.Ordinal)
            || !long.TryParse(operationId.AsSpan(prefix.Length), out var firstSequence)
            || firstSequence <= 0 || firstSequence > sequence
            || operationId != $"{prefix}{firstSequence}")
            throw new InvalidOperationException("Library return operation identity must contain only public owner and sequence.");
        ReplaceLibraryReturnEvent(entry with
        {
            PlayerLogGroupId = operationId,
            PlayerLibraryReturn = new(operationId, libraryOwner, topCount, bottomCount, shuffledCount, identitiesPublic),
        });
    }

    private void AttachLastLibraryReturnFact(int libraryOwner, string destination, int count,
        bool identitiesPublic, string? operationId = null)
    {
        if (!State.LibraryReturnFactsProtocolEnabled || count == 0) return;
        if (count < 0 || destination is not ("top" or "bottom" or "shuffled"))
            throw new InvalidOperationException("Invalid successful return destination or quantity.");
        AttachLibraryReturnFact(State.LastAction?.Sequence
                ?? throw new InvalidOperationException("Missing library return event."),
            libraryOwner, destination == "top" ? count : 0, destination == "bottom" ? count : 0,
            destination == "shuffled" ? count : 0, identitiesPublic, operationId);
    }

    // Only for previously silent successful exits. Legacy checkpoints emit nothing new.
    private void PublishLibraryReturn(int actor, int libraryOwner, string destination,
        IEnumerable<L12CardInstance> returnedCards, bool identitiesPublic, string? operationId = null,
        bool placementOnly = false)
    {
        if (!State.LibraryReturnFactsProtocolEnabled) return;
        var cards = returnedCards.DistinctBy(card => card.InstanceId).ToArray();
        if (cards.Length == 0) return;
        if (destination is not ("top" or "bottom" or "shuffled"))
            throw new InvalidOperationException("Unknown successful return destination.");
        operationId ??= LibraryReturnOperationId(libraryOwner);
        var fact = new L12PlayerLibraryReturn(operationId, libraryOwner,
            destination == "top" ? cards.Length : 0, destination == "bottom" ? cards.Length : 0,
            destination == "shuffled" ? cards.Length : 0, identitiesPublic);
        if (placementOnly)
        {
            // These cards stayed inside the library. Publish a single log fact, not
            // fictitious zone movements or a chosen private within-end ordering.
            AddEvent("reorder", actor, L12LibraryReturnFacts.PublicText(fact), identitiesPublic
                ? cards.OrderBy(card => card.InstanceId, StringComparer.Ordinal).ToArray() : []);
            AttachLastLibraryReturnFact(libraryOwner, destination, cards.Length, identitiesPublic, operationId);
            return;
        }
        foreach (var card in cards)
        {
            var single = fact with { TopCount = destination == "top" ? 1 : 0,
                BottomCount = destination == "bottom" ? 1 : 0, ShuffledCount = destination == "shuffled" ? 1 : 0 };
            AddEvent(identitiesPublic ? "return" : "private-return", actor,
                L12LibraryReturnFacts.PublicText(single), card);
            AttachLastLibraryReturnFact(libraryOwner, destination, 1, identitiesPublic, operationId);
        }
    }

    private void PublishLibraryPlacementReturn(L12StackItem item, IEnumerable<L12CardInstance> returnedCards,
        string destination = "bottom")
        => PublishLibraryReturn(item.Controller, item.Controller, destination, returnedCards,
            LibraryPlacementIdentitiesPublic(item), placementOnly: true);
}
