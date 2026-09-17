using System.Globalization;

namespace TwelveLegions.Server;

// Limits belong to effect segments, not to the list of clickable active buttons.
// Keep existing persisted keys so old checkpoints remain readable.
public sealed record L12MasterTriggeredUsageRule(string CardId, int AbilitySequence, string Ability, string KeyPattern)
{
    public string Key(int playerIndex, int turnSerial) => KeyPattern
        .Replace("{player}", playerIndex.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{turn}", turnSerial.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

public static class L12MasterTriggeredUsageRules
{
    public static IReadOnlyList<L12MasterTriggeredUsageRule> All { get; } =
    [
        new("S01-02M3", 2, "medjedDamageResponse", "trigger:medjedDamageResponse"),
        new("S02-02M1", 3, "nephthysScarab", "s2-nephthys-scarab:{turn}"),
        new("S02-04M1", 1, "tsukuyomiFollowMove", "active:master-{player}:tsukuyomiFollowMove"),
        new("S02-05M1", 1, "artemisDeathFlip", "trigger:artemis-ranged-death:{turn}"),
        new("S02-06M1", 1, "morriganEnemyDeathRune", "s2-morrigan-rune:{turn}"),
        new("S02-06M2", 2, "angusTrialAdvanceRune", "trigger:angus-trial-rune:{turn}"),
        new("S02-06M2", 3, "angusTacticTrial", "trigger:angus-tactic:{turn}"),
        new("ST01-M1", 1, "changeRestedMorale", "trigger:starter-change:{turn}"),
        new("ST04-M1", 1, "kagutsuchiBuff", "trigger:starter-kagutsuchi:{turn}"),
    ];

    private static readonly IReadOnlyDictionary<string, L12MasterTriggeredUsageRule> ByAbility = All
        .ToDictionary(rule => rule.Ability, StringComparer.Ordinal);

    public static L12MasterTriggeredUsageRule? Find(string cardId, string ability)
        => ByAbility.TryGetValue(ability, out var rule) && rule.CardId == cardId ? rule : null;

    public static string Key(string ability, int playerIndex, int turnSerial)
        => ByAbility[ability].Key(playerIndex, turnSerial);
}
