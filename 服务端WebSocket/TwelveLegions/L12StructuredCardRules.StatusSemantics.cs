namespace TwelveLegions.Server;

/// <summary>
/// Runtime identity predicates backed by the structured card rule layer.
/// Keeping these identities here prevents presentation and lifecycle consumers
/// from introducing new card-id branches into the rule engine.
/// </summary>
public static class L12StructuredCardSemantics
{
    private const string HeavenEarthChangeCardId = "S02-DS01";
    private const string HannibalCardId = "S02-0516";
    private const string KingsSwordCardId = "S02-06S2";

    public static bool IsHeavenEarthChange(string? cardId)
        => string.Equals(cardId, HeavenEarthChangeCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsHannibal(string? cardId)
        => string.Equals(cardId, HannibalCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsKingsSword(string? cardId)
        => string.Equals(cardId, KingsSwordCardId, StringComparison.OrdinalIgnoreCase);
}
