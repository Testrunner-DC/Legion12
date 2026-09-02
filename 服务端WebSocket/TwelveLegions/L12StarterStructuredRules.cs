namespace TwelveLegions.Server;

/// <summary>
/// ST 产品卡效的结构化批次。这里与实战原子程序共用能力边界，禁止另建仅供后台展示的描述。
/// 第一批只接管无目标的原子效果与公共战斗规则；带目标、支付和复合结算依旧明确保留旧实现边界。
/// </summary>
public static partial class L12StructuredCardRules
{
    internal static bool TryGetStarterBatch1Abilities(string cardId,
        out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "ST01-02" =>
            [
                new("after-attack", "triggered", "击杀时 可从士气牌库追加1张休整的士气。",
                [
                    new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }),
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    new(L12AtomKinds.AddMorale, "追加 1 张休整士气", "resolution", new() { ["amount"] = "1", ["state"] = "rested" }),
                ], "human-assisted", "product-database"),
            ],
            "ST02-04" => [OptionalDraw("enter", "登场时 可抽取1张牌。")],
            "ST03-01" =>
            [
                new("entry-discount", "replacement", "可将墓地1张卡牌返回牌库底部：此军团登场费用-1。",
                [
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    new(L12AtomKinds.SelectTarget, "选择墓地 1 张卡牌", "target", new() { ["zone"] = "controller.graveyard", ["min"] = "1", ["max"] = "1" }),
                    new(L12AtomKinds.MoveZone, "将所选卡牌返回牌库底部", "cost", new() { ["from"] = "controller.graveyard", ["to"] = "controller.library-bottom" }),
                    new(L12AtomKinds.SetState, "本次登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
                ], "human-assisted", "product-database"),
                CanAttackOnEnter("opponent-legion", "登场时 本回合可进攻对方军团。"),
            ],
            "ST04-03" => [CanAttackOnEnter("opponent-legion", "登场时 本回合可进攻对方军团。")],
            "ST04-09" => [CanAttackOnEnter("opponent-master", "登场时 本回合可进攻对方主宰。")],
            "ST05-08" =>
            [
                new("enter", "triggered", "登场时 获得冲锋。（可在登场回合进攻）",
                [new(L12AtomKinds.Keyword, "获得【冲锋】", "resolution", new() { ["keywordRef"] = "charge" })],
                "human-assisted", "product-database"),
            ],
            "ST06-03" =>
            [
                OptionalRune("enter", "登场时 可获得1符文。"),
                new("after-attack", "triggered", "击杀时 可将此军团转为活跃，本回合兵力+2000。",
                [
                    new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }),
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    new(L12AtomKinds.Ready, "此军团转为活跃", "resolution", new()),
                    new(L12AtomKinds.ModifyTroops, "本回合兵力 +2000", "resolution", new() { ["operation"] = "add", ["value"] = "2000" }),
                    new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
                ], "human-assisted", "product-database"),
            ],
            "ST06-05" =>
            [
                OptionalDraw("enter", "登场时 可抽取1张牌。"),
                OptionalDraw("attack", "进攻时 可抽取1张牌。"),
            ],
            "ST06-06" =>
            [
                OptionalDraw("enter", "登场时 可抽取1张牌。"),
                new("death", "triggered", "阵亡时 试炼+2。",
                [new(L12AtomKinds.AdvanceTrial, "试炼 +2", "resolution", new() { ["amount"] = "2" })],
                "human-assisted", "product-database"),
            ],
            "ST06-08" => [OptionalRune("enter", "登场时 可获得1符文。")],
            "ST06-10" =>
            [
                new("play", "activated", "试炼+2。",
                [new(L12AtomKinds.AdvanceTrial, "试炼 +2", "resolution", new() { ["amount"] = "2" })],
                "human-assisted", "product-database"),
            ],
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static L12StructuredAbilityTemplate OptionalDraw(string trigger, string text) =>
        new(trigger, "triggered", text,
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ], "human-assisted", "product-database");

    private static L12StructuredAbilityTemplate OptionalRune(string trigger, string text) =>
        new(trigger, "triggered", text,
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.GainRune, "获得 1 符文", "resolution", new() { ["amount"] = "1" }),
        ], "human-assisted", "product-database");

    private static L12StructuredAbilityTemplate CanAttackOnEnter(string target, string text) =>
        new("enter", "triggered", text,
        [
            new(L12AtomKinds.AttackRule, target == "opponent-master" ? "本回合可进攻对方主宰" : "本回合可进攻对方军团",
                "resolution", new() { [target == "opponent-master" ? "canAttackOpponentMaster" : "canAttackOpponentLegion"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ], "human-assisted", "product-database");
}
