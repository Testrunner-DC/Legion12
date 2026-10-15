namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> CanopicRelicCardIds = new(StringComparer.OrdinalIgnoreCase)
    { "S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220" };

    // CanopicProgress is an existing physical storage representation, not a success flag.
    // Include retained relics without moving or cloning any authoritative card while reading.
    private static IEnumerable<L12CardInstance> CanopicRelicsInRelicZone(L12PlayerState player)
        => player.ExtraRelics.Concat(player.SpecialZones.CanopicProgress)
            .Concat(player.Relic is null ? [] : new[] { player.Relic })
            .Where(card => CanopicRelicCardIds.Contains(card.CardId))
            .DistinctBy(card => card.InstanceId, StringComparer.Ordinal);

    private static HashSet<string> CanopicRelicIdsInRelicZone(L12PlayerState player)
        => CanopicRelicsInRelicZone(player).Select(card => card.CardId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool CanopicRelicIsPresent(L12PlayerState player, string cardId)
        => CanopicRelicsInRelicZone(player)
            .Any(card => card.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase));
}
