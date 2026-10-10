namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static bool IsStillInInspectedLibrarySet(L12StackItem item, string dataKey,
        L12CardInstance card)
        => item.Data.GetValueOrDefault(dataKey, string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase);

    private static bool IsShanheSearchCandidate(L12CardInstance card)
        => card.Faction == "tianting";

    private static bool IsFactionTopSearchCandidate(L12PlayerState player, L12CardInstance card,
        string faction, string excludedCardId)
        => L12StructuredCardRules.HasFaction(player, card, faction)
            && card.CardId != excludedCardId;

    private static (string Faction, string ExcludedCardId) FactionTopSearchConstraints(L12StackItem item)
    {
        var faction = item.Data.GetValueOrDefault("faction-search-faction", string.Empty);
        var excluded = item.Data.GetValueOrDefault("faction-search-excluded", string.Empty);
        if (!string.IsNullOrWhiteSpace(faction)) return (faction, excluded);

        // Compatibility for an in-flight stack item restored from a pre-fix room snapshot.
        return item.Data.GetValueOrDefault("faction-search-context", string.Empty) switch
        {
            "ivar-search" => ("asgard", "S01-0315"),
            "sun-divinity" => ("taiyangcheng", string.Empty),
            "s2-plato-search" => ("olympus", "S02-0514"),
            _ => (string.Empty, excluded),
        };
    }

    private static bool IsCampSearchCandidate(L12PlayerState player, L12CardInstance card)
        => card.CardType == "legion"
            && L12StructuredCardRules.HasFaction(player, card, player.Faction);

    private static bool IsOiranGiftSearchCandidate(L12PlayerState player, L12CardInstance card)
        => card.CardId != "S01-0419"
            && L12StructuredCardRules.HasFaction(player, card, "gaotianyuan");

    private static bool IsRunePowerSearchCandidate(L12PlayerState player, L12CardInstance card)
        => card.CardId != "S02-0620"
            && L12StructuredCardRules.HasFaction(player, card, "otherworld");

    private bool IsMerlinSearchCandidate(L12CardInstance card)
        => card.CardType == "tactic"
            && L12StructuredCardRules.SearchCostAtMost(card, 4)
            && !IsCounterTactic(card.CardId);
}
