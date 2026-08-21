namespace TwelveLegions.Server;

/// <summary>
/// 卡面中会随位置改变的职介与战斗参数。这里是实战判定和原子化后台共用的权威定义，
/// 禁止再在进攻流程中按卡号分别判断距离、远程无损或“兵力视为”。
/// </summary>
public sealed record L12ConditionalCombatProfile(
    string? EffectiveProfession,
    bool HasRangeBonus,
    bool HasRangedNoLoss,
    int? AttackTroopsSetValue,
    string ConditionExpression);

public static class L12StructuredCardRules
{
    public static string EffectiveFaction(L12PlayerState owner, L12CardInstance card)
    {
        if (!string.Equals(card.Faction, "universal", StringComparison.Ordinal)) return card.Faction;
        var ringActive = owner.Relic?.CardId == "S02-0008"
            || owner.ExtraRelics.Any(relic => relic.CardId == "S02-0008");
        return ringActive ? owner.Faction : card.Faction;
    }

    public static bool HasFaction(L12PlayerState owner, L12CardInstance card, string faction)
        => string.Equals(EffectiveFaction(owner, card), faction, StringComparison.Ordinal);

    public static L12ConditionalCombatProfile CombatProfile(L12CardInstance card, int row)
    {
        var frontOnlyRanged = card.EffectText?.Contains("「位于前排」进攻距离+1", StringComparison.Ordinal) == true;
        var backOnlyRanged = card.CardId is "S01-0409" or "S02-0507";
        var ranged = card.HasRangeBonus
            && (!frontOnlyRanged || row == 0)
            && (!backOnlyRanged || row == 1);
        var rangedNoLoss = card.HasRangedNoLoss && ranged;

        return card.CardId switch
        {
            "S01-0409" when row == 1 => new(card.Profession, true, true, 2000, "source.row=back"),
            "S02-0507" when row == 1 => new("弓手", true, true, 3000, "source.row=back"),
            "S01-0409" or "S02-0507" => new(card.Profession, false, false, null, "source.row=back"),
            _ => new(card.Profession, ranged, rangedNoLoss, null,
                frontOnlyRanged ? "source.row=front" : "always"),
        };
    }

    public static string? EffectiveProfession(L12CardInstance card, int row)
        => CombatProfile(card, row).EffectiveProfession;

    public static bool HasProfession(L12CardInstance card, int row, string profession)
        => string.Equals(EffectiveProfession(card, row), profession, StringComparison.Ordinal);

    public static bool TryGetStructuredAbilities(L12CardDefinition card, out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = card.Id switch
        {
            "S02-0507" => AtalantaAbilities(),
            "S01-0409" => YoshitsuneAbilities(),
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static IReadOnlyList<L12StructuredAbilityTemplate> AtalantaAbilities() =>
    [
        new("promotion", "summon-flow", "晋升 消耗并翻转1神力，叠放至我方同名非【晋升者】军团上方登场。",
        [
            new(L12AtomKinds.Condition, "存在我方同名非【晋升者】军团", "condition", new() { ["expression"] = "controller.same-name-non-promoted-foundation" }),
            new(L12AtomKinds.Special, "消耗并翻转 1 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = "1", ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.SelectTarget, "选择同名晋升基底", "target", new() { ["zone"] = "controller.field", ["filter"] = "same-name-non-promoted", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "叠放至基底上方晋升登场", "resolution", new() { ["operation"] = "promotion-enter", ["inheritState"] = "true" }),
        ]),
        new("promotion-enter", "triggered", "晋升登场 可抽取1张牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
        new("enter", "triggered", "登场时 可抽取1张牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
        new("static", "continuous", "「位于后排」此军团视为【弓手】，进攻距离+1，远程进攻无损。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.SetState, "职介视为【弓手】", "resolution", new() { ["key"] = "source.derived-profession", ["value"] = "弓手" }),
            new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1" }),
            new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于后排期间持续", "duration", new() { ["duration"] = "while-source-row-back" }),
        ]),
        new("attack", "triggered", "「位于后排」进攻时 此军团兵力视为3000。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.ModifyTroops, "本次进攻兵力视为 3000", "resolution", new() { ["operation"] = "set", ["value"] = "3000" }),
            new(L12AtomKinds.Duration, "仅本次进攻", "duration", new() { ["duration"] = "current-attack" }),
        ]),
    ];

    private static IReadOnlyList<L12StructuredAbilityTemplate> YoshitsuneAbilities() =>
    [
        new("static", "continuous", "「位于后排」进攻距离+1，远程进攻无损。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1" }),
            new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于后排期间持续", "duration", new() { ["duration"] = "while-source-row-back" }),
        ]),
        new("attack", "triggered", "「位于后排」进攻时 此军团兵力视为2000。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.ModifyTroops, "本次进攻兵力视为 2000", "resolution", new() { ["operation"] = "set", ["value"] = "2000" }),
            new(L12AtomKinds.Duration, "仅本次进攻", "duration", new() { ["duration"] = "current-attack" }),
        ]),
        new("active", "activated", "我方 回合1次 可进行1次位移。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn-and-once" }),
            new(L12AtomKinds.Move, "进行 1 次骑兵位移", "resolution", new() { ["operation"] = "cavalry-move", ["amount"] = "1" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
        new("after-attack", "triggered", "击杀时 可抽取1张牌。",
        [
            new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
    ];
}

public sealed record L12StructuredAbilityTemplate(
    string Trigger,
    string ExecutionModel,
    string Text,
    IReadOnlyList<L12StructuredAtomTemplate> Atoms);

public sealed record L12StructuredAtomTemplate(
    string Kind,
    string Label,
    string Stage,
    Dictionary<string, string> Parameters);
