namespace TwelveLegions.Server;

/// <summary>
/// ST 第三批 A：无目标的费用与伤害后触发。能力目录和 verified runtime 共用这些边界。
/// </summary>
public static partial class L12StructuredCardRules
{
    internal static bool TryGetStarterBatch3AAbilities(string cardId,
        out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "ST01-01" =>
            [
                new("active", "active", "我方 回合1次 可进行1次位移。", [new(L12AtomKinds.Move, "进行1次骑兵位移", "resolution", new() { ["operation"] = "cavalry-move" })], "human-assisted", "product-database"),
                new("enter", "triggered", "登场时 可返还1士气：获得冲锋。",
                [new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.ReturnMorale, "返还1士气", "cost", new() { ["amount"] = "1" }), new(L12AtomKinds.Keyword, "获得冲锋", "resolution", new() { ["keyword"] = "charge" })], "human-assisted", "product-database"),
                new("granted", "granted-effect", "冲锋 在登场的回合即可进行进攻。", [new(L12AtomKinds.Keyword, "冲锋", "resolution", new() { ["keywordRef"] = "charge" })], "human-assisted", "product-database"),
                new("after-attack", "triggered", "击杀时 可返还1士气：本回合获得贯穿。",
                [new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }), new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.ReturnMorale, "返还1士气", "cost", new() { ["amount"] = "1" }), new(L12AtomKinds.Keyword, "本回合获得贯穿", "resolution", new() { ["keyword"] = "piercing" })], "human-assisted", "product-database"),
                new("granted", "granted-effect", "贯穿 击杀时，以此军团剩余兵力对对方主宰发动1次进攻；此次进攻不会触发进攻时效果。", [new(L12AtomKinds.Keyword, "贯穿", "resolution", new() { ["keywordRef"] = "piercing" })], "human-assisted", "product-database"),
            ],
            "ST01-07" => [new("after-attack", "triggered", "我方 回合1次 此军团进攻后，可返还1士气：将此军团转为活跃。", [new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.ReturnMorale, "返还1士气", "cost", new() { ["amount"] = "1" }), new(L12AtomKinds.Ready, "将此军团转为活跃", "resolution", new())], "human-assisted", "product-database")],
            "ST01-08" => [new("enter", "triggered", "登场时 本回合对方军团挑衅效果无效。", [new(L12AtomKinds.SetState, "本回合对方军团挑衅效果无效", "resolution", new() { ["key"] = "opponent.taunt-disabled-this-turn", ["duration"] = "this-turn" })], "human-assisted", "product-database")],
            "ST01-09" => [new("enter", "triggered", "登场时 可返还1士气：抽取1张牌。", [new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.ReturnMorale, "返还1士气", "cost", new() { ["amount"] = "1" }), new(L12AtomKinds.Draw, "抽取1张牌", "resolution", new() { ["amount"] = "1" })], "human-assisted", "product-database")],
            "ST01-10" => [new("reaction", "triggered", "对方 进攻后，可返还1士气：从我方手牌中将1张费用不高于4的【天廷】军团活跃登场。", [new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.ReturnMorale, "返还1士气", "cost", new() { ["amount"] = "1" }), new(L12AtomKinds.SelectTarget, "选择手牌中1张费用不高于4的【天廷】军团", "target", new() { ["zone"] = "controller.hand", ["filter"] = "faction=tianting;legion=true;current-cost<=4" }), new(L12AtomKinds.SelectTarget, "选择活跃登场位置", "target", new() { ["zone"] = "controller.field.empty-slot", ["empty"] = "true" }), new(L12AtomKinds.MoveZone, "所选军团活跃登场", "resolution", new() { ["from"] = "controller.hand", ["to"] = "controller.field", ["state"] = "active" })], "human-assisted", "product-database")],
            "ST03-07" => [new("enter", "triggered", "登场时 可弃置我方牌库顶部2张牌。", [new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.MoveZone, "弃置牌库顶部2张牌", "resolution", new() { ["from"] = "controller.library-top", ["to"] = "controller.graveyard", ["amount"] = "2" })], "human-assisted", "product-database")],
            "ST01-06" =>
            [
                new("enter", "triggered", "登场时 本回合我方下1张军团登场费用-1。",
                [
                    new(L12AtomKinds.SetState, "本回合我方下1张军团登场费用-1", "resolution", new()
                    {
                        ["key"] = "controller.next-legion-entry-discount",
                        ["operation"] = "increment",
                        ["value"] = "1",
                        ["uses"] = "1",
                        ["duration"] = "this-turn",
                    }),
                ], "human-assisted", "product-database"),
            ],
            "ST03-02" =>
            [
                new("continuous", "continuous", "「位于手牌」若我方主宰血量不高于7，此军团登场费用-1。",
                [
                    new(L12AtomKinds.Condition, "此军团位于手牌，且我方主宰血量不高于7", "condition", new()
                    {
                        ["expression"] = "source.zone=hand;controller.hp<=7",
                    }),
                    new(L12AtomKinds.SetState, "登场费用-1", "continuous", new()
                    {
                        ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1",
                    }),
                ], "human-assisted", "product-database"),
            ],
            "ST03-04" =>
            [
                new("after-damage", "triggered", "此军团对对方主宰造成伤害时，抽取2张牌。",
                [
                    new(L12AtomKinds.Draw, "抽取2张牌", "resolution", new() { ["amount"] = "2" }),
                ], "human-assisted", "product-database"),
            ],
            "ST05-04" =>
            [
                new("enter", "triggered", "登场时 若我方战场军团合计兵力少于对方，可抽取2张牌。",
                [
                    new(L12AtomKinds.Condition, "我方战场军团合计兵力少于对方", "condition", new() { ["expression"] = "controller.field-troops<opponent.field-troops" }),
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    new(L12AtomKinds.Draw, "抽取2张牌", "resolution", new() { ["amount"] = "2" }),
                ], "human-assisted", "product-database"),
            ],
            "ST05-09" =>
            [
                new("attack", "triggered", "进攻时 获得震击，本回合兵力+1000。",
                [
                    new(L12AtomKinds.Keyword, "获得震击", "resolution", new() { ["keyword"] = "shock" }),
                    new(L12AtomKinds.ModifyTroops, "本回合兵力+1000", "resolution", new() { ["operation"] = "add", ["value"] = "1000", ["duration"] = "this-turn" }),
                ], "human-assisted", "product-database"),
            ],
            _ => [],
        };
        return abilities.Count > 0;
    }
}
