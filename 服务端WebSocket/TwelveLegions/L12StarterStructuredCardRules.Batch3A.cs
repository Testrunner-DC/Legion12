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
            _ => [],
        };
        return abilities.Count > 0;
    }
}
