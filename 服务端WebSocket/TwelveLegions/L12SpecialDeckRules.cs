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

    public static bool DoesNotCountTowardMainDeck(L12CardDefinition card)
        => HasRule(card.Effect, "构筑时不计入卡组数量");

    public static bool StartsInGraveyard(L12CardDefinition card)
        => HasRule(card.Effect, "游戏开始时置入墓地");

    public static bool CannotEnterHandOrLibrary(L12CardInstance card)
        => HasRule(card.EffectText, "不能进入手牌和牌库")
           || IsDerivedSpecialCard(card);

    /// <summary>
    /// 由主宰或其他规则在主牌库以外产生的衍生卡，不得被通用的回手、回牌库、
    /// 检索等效果带入普通区域。专属效果仍可直接在其规定区域之间移动它们。
    /// </summary>
    public static bool IsDerivedSpecialCard(L12CardInstance card)
        => card.CardType == "token"
           || card.CardId == "S02-01S1"
           || card.Traits.Any(trait => trait.EndsWith("专属", StringComparison.Ordinal));

    /// <summary>
    /// 规则书“衍生卡/指示物”通则：衍生卡离开战场时直接消灭，
    /// 不会进入墓地、手牌、牌库或移出游戏区。离场/阵亡触发仍由调用方
    /// 按最后已知信息建立，只有离场后的区域归属被本规则替代。
    /// </summary>
    public static bool VanishesWhenLeavingField(L12CardInstance card)
        => IsDerivedSpecialCard(card);

    public static bool AlwaysReturnsToOwnerGraveyard(L12CardInstance card)
        => HasRule(card.EffectText, "以任何形式离场")
           && HasRule(card.EffectText, "所有者墓地");

    private static bool HasRule(string? effect, string rule)
        => !string.IsNullOrWhiteSpace(effect)
           && effect.Contains(rule, StringComparison.Ordinal);
}
