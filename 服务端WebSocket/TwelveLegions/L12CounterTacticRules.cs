namespace TwelveLegions.Server;

/// <summary>
/// Counter identity shared by placement/response rules and catalog fallback mapping.
/// An observed opponent timing is not an enter/attack ability owned by the tactic.
/// Explicit structured abilities still take precedence (including granted branches).
/// </summary>
public static class L12CounterTacticRules
{
    private static readonly HashSet<string> CardIds = new(StringComparer.Ordinal)
    {
        "S01-0016", "S01-0017", "S01-0018", "S01-0019", "S01-0020", "S01-0021", "S01-0120",
        "S01-0223", "S01-0224", "S01-0320", "S01-0420",
        "S02-0015", "S02-0016", "S02-0017", "S02-0018", "S02-0106", "S02-0523",
        "ST01-10",
    };

    public static bool Contains(string cardId) => CardIds.Contains(cardId);

    public static string? FallbackTrigger(string cardId) => !Contains(cardId) ? null
        : cardId.StartsWith("S02-", StringComparison.Ordinal) ? "s2-reaction" : "reaction";
}
