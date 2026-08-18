using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

/// <summary>
/// 主牌库以外的构筑规则。数量由卡面规则文本推导，避免按某张主宰编号写死。
/// </summary>
public static partial class L12SpecialDeckRules
{
    [GeneratedRegex(@"可完成的试炼数量增加\s*(\d+)\s*张")]
    private static partial Regex TrialIncreaseRegex();

    [GeneratedRegex(@"可携带\s*(\d+)\s*张[^。；\n]*试炼")]
    private static partial Regex TrialCarryRegex();

    public static int TrialCapacity(L12CardDefinition master)
    {
        if (master.Faction != "otherworld") return 0;

        var effect = master.Effect ?? string.Empty;
        var capacity = 1;
        var carry = TrialCarryRegex().Match(effect);
        if (carry.Success && int.TryParse(carry.Groups[1].Value, out var carried))
            capacity = Math.Max(capacity, carried);

        foreach (Match match in TrialIncreaseRegex().Matches(effect))
            if (int.TryParse(match.Groups[1].Value, out var increase)) capacity += increase;

        return capacity;
    }
}
