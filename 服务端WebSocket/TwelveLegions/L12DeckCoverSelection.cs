namespace TwelveLegions.Server;

public static class L12DeckCoverSelection
{
    private static readonly string[] TypePriority = ["legion", "tactic", "artifact"];

    /// <summary>
    /// Chooses up to three distinct main-deck cards in right-to-left display order.
    /// </summary>
    public static IReadOnlyList<string> SelectCardIds(L12Catalog catalog,
        IReadOnlyList<L12DeckCardQuantity> main)
        => SelectCardIds(catalog.Cards, main);

    public static IReadOnlyList<string> SelectCardIds(IReadOnlyDictionary<string, L12CardDefinition> cardsById,
        IReadOnlyList<L12DeckCardQuantity> main)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = TypePriority.ToDictionary(type => type, _ => new List<L12CardDefinition>(), StringComparer.Ordinal);
        foreach (var item in main)
        {
            if (item.Quantity <= 0 || !seen.Add(item.CardId)
                || !cardsById.TryGetValue(item.CardId, out var card)
                || !groups.TryGetValue(card.CardType, out var cards)) continue;
            cards.Add(card);
        }
        foreach (var cards in groups.Values) cards.Sort(CompareCards);

        var selected = TypePriority.ToDictionary(type => type, _ => new List<L12CardDefinition>(), StringComparer.Ordinal);
        foreach (var type in TypePriority)
        {
            var card = groups[type].FirstOrDefault();
            if (card is not null) selected[type].Add(card);
        }
        // Missing types refill in the same legion -> tactic -> artifact priority.
        foreach (var type in TypePriority)
        {
            for (var index = 1; index < groups[type].Count && SelectedCount(selected) < 3; index++)
                selected[type].Add(groups[type][index]);
        }
        return TypePriority.SelectMany(type => selected[type]).Take(3).Select(card => card.Id).ToArray();
    }

    private static int SelectedCount(IReadOnlyDictionary<string, List<L12CardDefinition>> selected)
        => selected.Sum(item => item.Value.Count);

    private static int CompareCards(L12CardDefinition left, L12CardDefinition right)
    {
        var result = (right.Cost ?? int.MinValue).CompareTo(left.Cost ?? int.MinValue);
        if (result != 0) return result;
        result = (right.Troops ?? 0).CompareTo(left.Troops ?? 0);
        if (result != 0) return result;
        result = CompareNatural(right.Number.Length == 0 ? right.Id : right.Number,
            left.Number.Length == 0 ? left.Id : left.Number);
        return result != 0 ? result : CompareNatural(right.Id, left.Id);
    }

    private static int CompareNatural(string left, string right)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            var leftDigit = char.IsAsciiDigit(left[leftIndex]);
            var rightDigit = char.IsAsciiDigit(right[rightIndex]);
            if (leftDigit && rightDigit)
            {
                var leftEnd = leftIndex;
                var rightEnd = rightIndex;
                while (leftEnd < left.Length && char.IsAsciiDigit(left[leftEnd])) leftEnd++;
                while (rightEnd < right.Length && char.IsAsciiDigit(right[rightEnd])) rightEnd++;
                var leftSignificant = leftIndex;
                var rightSignificant = rightIndex;
                while (leftSignificant < leftEnd - 1 && left[leftSignificant] == '0') leftSignificant++;
                while (rightSignificant < rightEnd - 1 && right[rightSignificant] == '0') rightSignificant++;
                var length = (leftEnd - leftSignificant).CompareTo(rightEnd - rightSignificant);
                if (length != 0) return length;
                for (var index = 0; index < leftEnd - leftSignificant; index++)
                {
                    var digit = left[leftSignificant + index].CompareTo(right[rightSignificant + index]);
                    if (digit != 0) return digit;
                }
                var padding = (leftEnd - leftIndex).CompareTo(rightEnd - rightIndex);
                if (padding != 0) return padding;
                leftIndex = leftEnd;
                rightIndex = rightEnd;
                continue;
            }
            if (leftDigit != rightDigit) return leftDigit ? -1 : 1;
            var insensitive = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (insensitive != 0) return insensitive;
            var ordinal = left[leftIndex].CompareTo(right[rightIndex]);
            if (ordinal != 0) return ordinal;
            leftIndex++;
            rightIndex++;
        }
        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }
}
