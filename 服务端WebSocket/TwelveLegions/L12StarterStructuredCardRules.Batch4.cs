namespace TwelveLegions.Server;

/// <summary>
/// ST 第四批：四张「位于前排」获得【挑衅】军团（程咬金、沙漠卫兵、森可成、森林魁熊）的
/// 结构化收口。迁移前它们的印刷段来自 atomicReference 文本拆分，挑衅定义与前排授予的
/// 运行时来源（关键词定义、前排 overlay、对方回合兵力规则）互相分离；本批与 S02 同型——
/// 关键词定义段携带 Keyword 原子，前排授予/兵力组合行经 abilityRef 引用定义段，
/// 运行时入口不变（HasTaunt 条件链与对方回合兵力规则），四卡同时从前排挑衅 overlay
/// 封闭集合退役。森可成的位移行按既有骑兵位移规则行动模板结构化。
/// </summary>
public static partial class L12StructuredCardRules
{
    internal static bool TryGetStarterBatch4Abilities(string cardId,
        out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "ST01-04" =>
            [
                StarterFrontRowTauntGrant("「位于前排」获得【挑衅】。"),
                StarterTauntKeywordDefinition(),
            ],
            "ST02-02" =>
            [
                StarterFrontRowTauntTroopsLine(2),
                StarterTauntKeywordDefinition(),
            ],
            "ST06-02" =>
            [
                StarterFrontRowTauntTroopsLine(2),
                StarterTauntKeywordDefinition(),
            ],
            "ST04-01" =>
            [
                new("active", "rule-action", "我方 回合1次 可进行1次位移。",
                [
                    new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new()
                    {
                        ["expression"] = "controller.turn;source.once-per-turn-unused=true",
                    }),
                    new(L12AtomKinds.Move, "进行 1 次位移", "resolution", new() { ["operation"] = "cavalry-move", ["amount"] = "1" }),
                    new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
                ], "human-assisted", "product-database"),
                StarterFrontRowTauntTroopsLine(3),
                StarterTauntKeywordDefinition(),
            ],
            _ => [],
        };
        return abilities.Count > 0;
    }

    // 与 S02 同型的关键词定义段；ST 卡文措辞保持产品数据库原文。
    private static L12StructuredAbilityTemplate StarterTauntKeywordDefinition() =>
        new("keyword-definition", "keyword-definition", "挑衅 对方只可进攻带有此效果的军团。",
        [
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new()
            {
                ["keywordRef"] = "taunt", ["targetRule"] = "opponent-must-attack-taunt-legion",
            }),
        ], "human-assisted", "product-database");

    // 程咬金的授予行与前排挑衅 overlay 模板同文同原子；结构化接管后由本段承载。
    private static L12StructuredAbilityTemplate StarterFrontRowTauntGrant(string text) =>
        new("static", "continuous", text,
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new()
            {
                ["keywordRef"] = "taunt", ["targetRule"] = "opponent-must-attack-taunt-legion",
            }),
            new(L12AtomKinds.Duration, "位于前排期间持续", "duration", new() { ["duration"] = "while-source-row-front" }),
        ], "human-assisted", "product-database");

    // 与 S02 组合行同型：前排授予挑衅经 abilityRef 引用本卡关键词定义段；
    // 对方回合兵力 +1000 为声明性原子，运行时由对方回合前排兵力规则承载。
    private static L12StructuredAbilityTemplate StarterFrontRowTauntTroopsLine(int definitionSequence) =>
        new("continuous", "continuous", $"「位于前排」获得ABILITY {definitionSequence}，且在对方回合此军团兵力+1000。",
        [
            new(L12AtomKinds.Condition, "位于前排且对方回合", "condition", new() { ["expression"] = "source.row=front;opponent.turn" }),
            new(L12AtomKinds.ModifyTroops, "对方回合此军团兵力 +1000", "resolution", new() { ["operation"] = "add", ["value"] = "1000" }),
            new(L12AtomKinds.SetState, $"启用 ABILITY {definitionSequence}", "resolution", new()
            {
                ["abilityRef"] = $"ability:{definitionSequence}", ["value"] = "true",
            }),
        ], "human-assisted", "product-database");
}
