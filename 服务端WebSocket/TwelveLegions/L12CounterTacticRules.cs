namespace TwelveLegions.Server;

/// <summary>
/// Counter identity shared by placement/response rules and catalog fallback mapping.
/// An observed opponent timing is not an enter/attack ability owned by the tactic.
/// Explicit structured abilities still take precedence (including granted branches).
/// </summary>
public static class L12CounterTacticRules
{
    public static bool IsTactic(L12CardDefinition card)
        => card.CardType.Equals("tactic", StringComparison.OrdinalIgnoreCase);

    public static bool IsCounterTactic(L12CardDefinition card)
        => IsTactic(card) && card.IsCounterTactic;

    public static bool IsCounterTactic(L12CardInstance card)
        => card.CardType.Equals("tactic", StringComparison.OrdinalIgnoreCase) && card.IsCounterTactic;

    public static bool AffectsRespondedEffect(L12CardDefinition card)
        => IsCounterTactic(card) && card.AffectsRespondedEffect;

    public static bool BlocksAttack(L12CardDefinition card)
        => card.BlocksAttack;

    public static bool IsActiveTactic(L12CardDefinition card)
        => IsTactic(card) && !card.IsCounterTactic;

    public static string? FallbackTrigger(L12CardDefinition card) => !IsCounterTactic(card) ? null
        : card.Id.StartsWith("S02-", StringComparison.Ordinal) ? "s2-reaction" : "reaction";
}
