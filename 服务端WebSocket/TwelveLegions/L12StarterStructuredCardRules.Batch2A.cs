namespace TwelveLegions.Server;

/// <summary>
/// ST 第二批 A 的带目标军团效果。能力结构同时供后台原子目录和实战 verified program 使用；
/// 目标声明、费用提交和结算由现有 PublicTrigger/PendingActivation/Stack 事务链执行。
/// </summary>
public static partial class L12StructuredCardRules
{
    internal const string StarterHanXinCardId = "S01-0104";
    internal const string StarterTombGuardCardId = "S01-0212";
    internal const string StarterDesertCobraCardId = "ST02-09";

    internal static string? StarterTargetedBatch2APlan(string cardId, string trigger)
        => (cardId, trigger) switch
        {
            ("ST01-03", "enter") => "xiaohe-summon",
            ("ST02-01", "enter") => "khufu-debuff",
            ("ST02-03", "enter") => "snake-charmer-summon",
            ("ST02-06", "enter") => "george-debuff",
            ("ST03-03", "enter") => "freydis-recover",
            _ => null,
        };

    internal static bool TryGetStarterTargetedBatch2AAbilities(string cardId,
        out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "ST01-03" =>
            [
                Targeted("enter", "登场时 可返还1士气：将1张<韩信>从手牌活跃登场。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择手牌中的<韩信>", "controller.hand", "card-id=S01-0104"),
                    new(L12AtomKinds.SelectTarget, "选择登场位置", "target", new()
                    {
                        ["zone"] = "controller.field.empty-slot", ["min"] = "1", ["max"] = "1",
                    }),
                    new(L12AtomKinds.ReturnMorale, "返还1士气", "cost", new() { ["amount"] = "1" }),
                    Move("controller.hand", "controller.field", "active")),
            ],
            "ST02-01" =>
            [
                new("continuous", "continuous", "此军团登场时不受反击战术效果影响。",
                [
                    new(L12AtomKinds.SetState, "登场回合不受反击战术效果影响", "continuous", new()
                    {
                        ["key"] = "source.summon-turn-counter-tactic-protection", ["value"] = "true",
                    }),
                ], "human-assisted", "product-database"),
                Targeted("enter", "登场时 可弃置我方战场1张<陵墓守卫>：选择对方1张军团，本回合兵力-4000。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择要弃置的<陵墓守卫>", "controller.field", "card-id=S01-0212"),
                    Select("选择对方1张军团", "opponent.field", "legion=true"),
                    new(L12AtomKinds.Discard, "弃置所选<陵墓守卫>", "cost", new()
                    {
                        ["from"] = "controller.field", ["amount"] = "1",
                    }),
                    Troops(-4000)),
            ],
            "ST02-03" =>
            [
                Targeted("enter", "登场时 可将手牌/牌库中1张<沙漠眼镜蛇>活跃登场。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择手牌或牌库中的<沙漠眼镜蛇>", "controller.hand-or-library", "card-id=ST02-09"),
                    new(L12AtomKinds.Visibility, "仅控制者查看隐藏区域候选", "target", new()
                    {
                        ["audience"] = "controller-only",
                    }),
                    new(L12AtomKinds.SelectTarget, "选择登场位置", "target", new()
                    {
                        ["zone"] = "controller.field.empty-slot", ["min"] = "1", ["max"] = "1",
                    }),
                    Move("controller.hand-or-library", "controller.field", "active")),
            ],
            "ST02-06" =>
            [
                Targeted("enter", "登场时 可弃置我方战场上1张军团：选择对方1张军团，本回合兵力-2000。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择要弃置的我方军团", "controller.field", "legion=true"),
                    Select("选择对方1张军团", "opponent.field", "legion=true"),
                    new(L12AtomKinds.Discard, "弃置所选我方军团", "cost", new()
                    {
                        ["from"] = "controller.field", ["amount"] = "1",
                    }),
                    Troops(-2000)),
            ],
            "ST03-03" =>
            [
                Targeted("enter", "登场时 可弃置1张手牌：将墓地1张【阿斯加德】军团加入手牌。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择要弃置的1张手牌", "controller.hand", "any=true"),
                    Select("选择墓地1张【阿斯加德】军团", "controller.graveyard", "faction=asgard;legion=true"),
                    new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new()
                    {
                        ["from"] = "controller.hand", ["amount"] = "1",
                    }),
                    Move("controller.graveyard", "controller.hand", "revealed")),
            ],
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static L12StructuredAbilityTemplate Targeted(string trigger, string text,
        params L12StructuredAtomTemplate[] atoms)
        => new(trigger, "triggered", text, atoms, "human-assisted", "product-database");

    private static L12StructuredAtomTemplate Select(string label, string zone, string filter)
        => new(L12AtomKinds.SelectTarget, label, "target", new()
        {
            ["zone"] = zone, ["filter"] = filter, ["min"] = "1", ["max"] = "1",
        });

    private static L12StructuredAtomTemplate Move(string from, string to, string state)
        => new(L12AtomKinds.MoveZone, "移动所选卡牌", "resolution", new()
        {
            ["from"] = from, ["to"] = to, ["state"] = state,
        });

    private static L12StructuredAtomTemplate Troops(int value)
        => new(L12AtomKinds.ModifyTroops, $"本回合兵力{value:+#;-#;0}", "resolution", new()
        {
            ["operation"] = "add", ["value"] = value.ToString(), ["target"] = "declared",
            ["duration"] = "this-turn",
        });
}
