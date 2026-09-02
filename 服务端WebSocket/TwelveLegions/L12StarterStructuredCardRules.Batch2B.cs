namespace TwelveLegions.Server;

/// <summary>
/// ST 第二批 B 的公开目标军团效果。声明式计划同时供后台能力目录和 verified program 使用；
/// 实战仅按计划键路由，不按卡号建立平行分支。
/// </summary>
public static partial class L12StructuredCardRules
{
    internal const string StarterElizabethTudorCardId = "S02-0618";

    internal static string? StarterTargetedBatch2BPlan(string cardId, string trigger)
        => (cardId, trigger) switch
        {
            ("ST05-07", "enter") => "antinous-ready",
            ("ST06-01", "enter") => "elizabeth-lock-morale",
            ("ST06-04", "enter") => "mordred-enter-choice",
            ("ST06-04", "death") => "mordred-death-kill",
            ("ST06-07", "enter") => "boudica-immortal",
            _ => null,
        };

    internal static bool TryGetStarterTargetedBatch2BAbilities(string cardId,
        out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "ST05-07" =>
            [
                Targeted("enter", "登场时 若本回合因主宰弃置过手牌，可将我方1张休整的【奥林匹斯】军团转为活跃。",
                    new(L12AtomKinds.Condition, "本回合因主宰弃置过手牌", "condition", new()
                    {
                        ["expression"] = "controller.hand-discarded-by-master-this-turn=true",
                    }),
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择我方1张休整的【奥林匹斯】军团", "controller.field",
                        "faction=olympus;legion=true;tapped=true"),
                    new(L12AtomKinds.Ready, "将所选军团转为活跃", "resolution", new()
                    {
                        ["target"] = "declared",
                    })),
            ],
            "ST06-01" =>
            [
                new("continuous", "continuous", "若我方场上存在〈伊丽莎白 都铎〉，此军团登场费用-2。",
                [
                    new(L12AtomKinds.Condition, "此军团位于手牌，且我方场上存在〈伊丽莎白 都铎〉", "condition", new()
                    {
                        ["expression"] = $"source.zone=hand;controller.field.card-id={StarterElizabethTudorCardId}",
                    }),
                    new(L12AtomKinds.SetState, "登场费用-2", "continuous", new()
                    {
                        ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-2",
                    }),
                ], "human-assisted", "product-database"),
                Targeted("enter", "登场时 选择对方最多2张休整的士气，下个重置阶段无法转为活跃。",
                    new(L12AtomKinds.SelectTarget, "选择对方最多2张休整的士气", "target", new()
                    {
                        ["zone"] = "opponent.morale", ["filter"] = "tapped=true", ["min"] = "0", ["max"] = "2",
                    }),
                    new(L12AtomKinds.SetState, "下个重置阶段无法转为活跃", "resolution", new()
                    {
                        ["key"] = "target.cannot-untap-next-reset", ["value"] = "true",
                    })),
            ],
            "ST06-04" =>
            [
                new("active", "active", "我方 回合1次 可进行1次骑兵位移。",
                [
                    new(L12AtomKinds.Move, "进行1次骑兵位移", "resolution", new()
                    {
                        ["operation"] = "cavalry-move", ["amount"] = "1",
                    }),
                ], "human-assisted", "product-database"),
                Targeted("enter", "登场时 选择获得1符文或获得冲锋。",
                    new(L12AtomKinds.SelectMode, "选择获得1符文或获得冲锋", "target", new()
                    {
                        ["options"] = "gain-rune|gain-charge", ["min"] = "1", ["max"] = "1",
                    }),
                    new(L12AtomKinds.GainRune, "获得1符文", "resolution", new() { ["amount"] = "1" }),
                    new(L12AtomKinds.Keyword, "获得冲锋", "resolution", new() { ["keyword"] = "charge" })),
                Targeted("death", "阵亡时 击杀对方1张兵力不高于2000的军团。",
                    Select("选择对方1张兵力不高于2000的军团", "opponent.field",
                        "legion=true;current-troops<=2000"),
                    new(L12AtomKinds.MoveZone, "击杀所选军团", "resolution", new()
                    {
                        ["from"] = "opponent.field", ["to"] = "owner.graveyard", ["reason"] = "effect-kill",
                    })),
            ],
            "ST06-07" =>
            [
                Targeted("enter", "登场时 选择我方1张【彼界】军团，直到下个我方回合开始前获得一次免死。",
                    Select("选择我方1张【彼界】军团", "controller.field", "faction=otherworld;legion=true"),
                    new(L12AtomKinds.Keyword, "直到下个我方回合开始前获得一次免死", "resolution", new()
                    {
                        ["keyword"] = "immortal", ["uses"] = "1", ["duration"] = "until-next-controller-turn-start",
                    })),
            ],
            _ => [],
        };
        return abilities.Count > 0;
    }
}
