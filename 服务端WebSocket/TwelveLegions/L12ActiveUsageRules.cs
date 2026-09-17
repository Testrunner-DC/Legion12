namespace TwelveLegions.Server;

// A missing rule means no per-turn cap, not an implicit once-per-turn rule.
// Card text is reviewed offline; live requests never infer limits from prose.
public sealed record L12ActiveUsageRule(string CardId, string Ability, string? SharedGroup = null);

public static class L12ActiveUsageRules
{
    public static IReadOnlyList<L12ActiveUsageRule> All { get; } =
    [
        new("S01-01C1", "factionAddActive"),
        new("S01-01C1", "factionZeroRecovery"),
        new("S01-01D1", "palaceReward"),
        new("S01-01M1", "drawCycle"),
        new("S01-01M1", "nonLethal"),
        new("S01-01M2", "mengpoSilence", "mengpo-choice"),
        new("S01-01M2", "mengpoMorale", "mengpo-choice"),
        new("S01-02C1", "sunGuard"),
        new("S01-02C1", "sunDraw"),
        new("S01-02D1", "sunTopThree"),
        new("S01-02D1", "sunBottomEnemy"),
        new("S01-02M3", "medjedDebuff"),
        new("S01-03C1", "asgardDraw"),
        new("S01-03D1", "valhallaDiscount"),
        new("S01-03D1", "valhallaRecover"),
        new("S01-03M1", "valkyrieRecover"),
        new("S01-03M2", "lokiCycle", "loki"),
        new("S01-03M2", "lokiHeal", "loki"),
        new("S01-0417", "kusanagiDebuff", "choice"),
        new("S01-0417", "kusanagiStrong", "choice"),
        new("S01-04C1", "factionDrawMove"),
        new("S01-04D1", "yomiDiscount"),
        new("S01-04D1", "yomiSweep"),
        new("S01-04M1", "amaterasuKill"),
        new("S01-04M1", "amaterasuReady"),
        new("S01-04M2", "frontBuff"),
        new("S02-01M1", "wukongTransform"),
        new("S02-0205", "scarabDebuff"),
        new("S02-02M1", "nephthysSacrifice"),
        new("S02-0301", "thorHammerRevive"),
        new("S02-05C1", "olympusMoraleFlip"),
        new("S02-05C1", "godPowerDraw"),
        new("S02-05D1", "divinityFlipMorale"),
        new("S02-05D1", "divinityPower"),
        new("S02-05M1", "artemisBuff"),
        new("S02-05M2", "prometheusTopThree"),
        new("S02-06C1", "factionGainRune"),
        new("S02-06C1", "runeUse"),
        new("S02-06D1", "avalonRecover"),
        new("S02-06M1", "morriganReadyOnKill"),
        new("S02-06S5", "fenianReady"),
        new("S02-06S6", "crusadeTrialNoLoss", "crusade-choice"),
        new("S02-06S6", "crusadeRichardPiercing", "crusade-choice"),
        new("S02-06S6", "crusadeRecover", "crusade-choice"),
        new("ST02-M1", "horusRevive"),
        new("ST03-M1", "sifCycle"),
        new("ST05-M1", "athenaFrontBuff"),
        new("ST06-M1", "nuadaReadyMorale"),
        new("ST06-S1", "skyCityDiscount"),
    ];

    private static readonly IReadOnlyDictionary<string, L12ActiveUsageRule> ByAbility = All
        .ToDictionary(rule => $"{rule.CardId}|{rule.Ability}", StringComparer.Ordinal);

    // The caller canonicalizes versioned morale cards through the catalog registry.
    public static L12ActiveUsageRule? Find(string canonicalCardId, string ability)
        => ByAbility.GetValueOrDefault($"{canonicalCardId}|{ability}");

    public static string UsageKey(string sourceInstanceId, string canonicalCardId, string ability)
        => $"active:{sourceInstanceId}:{Find(canonicalCardId, ability)?.SharedGroup ?? ability}";
}
