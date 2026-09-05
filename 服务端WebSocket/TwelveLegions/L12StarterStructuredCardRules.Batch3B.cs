namespace TwelveLegions.Server;

/// <summary>
/// ST 第三批 B：剩余主宰、战术、试炼与持续规则。卡号只在这份产品规则目录出现；
/// 实战层按稳定的 plan/ability/flow 键执行，后台原子清单与对局入口共用同一能力边界。
/// </summary>
public static partial class L12StructuredCardRules
{
    internal const string StarterTakedaShingenCardId = "S01-0403";

    internal static string? StarterHandPlayPlanId(string cardId) => cardId switch
    {
        "ST02-10" => "starter-tomb-guard-revive",
        "ST03-10" => "legendary-bloodline",
        "ST04-10" => "invasion-fire",
        "ST05-10" => "hunter-gift",
        _ => null,
    };

    internal static IReadOnlyList<L12AbilityView> StarterRemainingAbilityViews(string cardId) => cardId switch
    {
        "ST02-05" => [new("oasisDancerBuff", "主动休整 我方所有【太阳城】军团，本回合兵力+1000。")],
        "ST03-05" => [new("christinaFreeTactic", "主动休整 本回合从我方手牌中打出的下1张费用不高于3的〈主动战术〉无需消耗费用，改为对我方主宰造成1点伤害。")],
        "ST03-07" => [new("kaneMillOne", "主动休整 弃置我方牌库顶部1张牌。")],
        "ST04-06" => [new("oiranTransfer", "主动休整 选择对方1张军团，本回合兵力-1000。选择我方1张军团，本回合兵力+1000。")],
        "ST02-M1" => [new("horusRevive", "我方 回合1次 可消耗1士气并弃置我方战场2张军团：选择墓地1张兵力不高于2000的【太阳城】军团休整登场。")],
        "ST03-M1" => [new("sifCycle", "我方 回合1次 可将墓地3张【阿斯加德】卡牌自选顺序返回牌库底部：抽取1张牌。")],
        "ST05-M1" => [new("athenaFrontBuff", "我方 回合1次 可弃置1张手牌：翻转1张士气。选择我方前排最多2张【奥林匹斯】军团，本回合兵力+1000，且对对方主宰造成的伤害+1。")],
        "ST05-06" => [new("telemachusTopThree", "主动休整 查看牌库顶部3张牌，选择其中1张【远程】军团或【奥林匹斯】战术卡，展示并加入手牌，其余卡牌自选顺序全部返回牌库顶部或全部返回牌库底部。")],
        "ST06-09" => [new("lightSwordActive", "主动休整 弃置1张手牌：选择我方前排1张【彼界】军团，本回合兵力+2000；或获得1符文。")],
        "ST06-M1" => [new("nuadaReadyMorale", "我方 回合1次 可消耗2符文：将我方最多2张士气转为活跃，试炼+2。")],
        "ST06-S1" =>
        [
            new("completeTrial", "试炼达到8：完成《探寻天空之城》。"),
            new("skyCityDiscount", "我方 回合1次 本回合我方下1张【彼界】军团登场费用-1。"),
        ],
        _ => [],
    };

    internal static bool HasDedicatedEndTurnDiscardRoute(string cardId)
        => cardId == "S02-0523";

    internal static bool IsReversibleOlympusMorale(string cardId)
        => cardId is "S02-05C1" or "S02-05C1A" or "ST05-C1";

    internal static string? StarterRemainingPlan(string cardId, string trigger)
        => (cardId, trigger) switch
        {
            ("ST01-01", "enter") => "zhaoyun-enter-charge",
            ("ST01-01", "after-attack") => "zhaoyun-kill-piercing",
            ("ST01-07", "after-attack") => "crossbow-ready",
            ("ST01-09", "enter") => "wangzhaojun-draw",
            ("ST01-10", "reaction") => "hidden-pass-summon",
            ("ST03-07", "enter") => "kane-enter-mill",
            ("ST01-M1", "morale-return") => "change-rested-morale",
            ("ST02-07", "opponent-back-to-front") => "tomb-defender-debuff",
            ("ST04-02", "attack") => "kojiro-discard",
            ("ST04-02", "death") => "kojiro-death-kill",
            ("ST04-04", "enter") => "kai-master-waiver",
            ("ST04-M1", "legion-attack-timing") => "kagutsuchi-buff",
            ("ST06-03", "after-attack") => "gareth-kill-ready",
            ("ST02-08", "death") => "akhenaten-death-heal",
            ("ST06-09", "enter") => "light-sword-enter-kill",
            ("ST05-01", "promotion-enter") => "aeneas-promotion-search",
            ("ST06-M1", "rune-spent") => "nuada-rune-buff",
            ("ST06-S1", "trial-complete") => "sky-city-completion",
            _ => null,
        };

    internal static bool IsStarterRemainingPlan(string plan)
        => plan is "zhaoyun-enter-charge" or "zhaoyun-kill-piercing" or "crossbow-ready"
            or "wangzhaojun-draw" or "hidden-pass-summon" or "kane-enter-mill"
            or "change-rested-morale" or "tomb-defender-debuff" or "kojiro-discard"
            or "kojiro-death-kill" or "kai-master-waiver" or "kagutsuchi-buff"
            or "gareth-kill-ready"
            or "aeneas-promotion-search" or "nuada-rune-buff" or "sky-city-completion"
            or "akhenaten-death-heal" or "light-sword-enter-kill";

    internal static bool RequiresStarterDisasterAttackDiscard(string? cardId)
        => cardId == "ST-DS02";

    internal static bool IsStarterKondoReplacementSource(string cardId)
        => cardId == "ST04-05";

    internal static int StarterGraveFactionLegionCopies(L12PlayerState owner, L12CardInstance card,
        string faction)
    {
        if (card.CardType != "legion" || !HasFaction(owner, card, faction)) return 0;
        return card.CardId == "ST03-08" && faction == "asgard" ? 3 : 1;
    }

    internal static bool CanRepresentGraveFactionLegionCount(L12PlayerState owner,
        IReadOnlyCollection<L12CardInstance> cards, string faction, int required)
        => cards.Count <= required
            && cards.All(card => card.CardType == "legion" && HasFaction(owner, card, faction))
            && cards.Sum(card => StarterGraveFactionLegionCopies(owner, card, faction)) >= required;

    internal static int StarterGraveFactionCardCopies(L12PlayerState owner, L12CardInstance card,
        string faction)
    {
        if (!HasFaction(owner, card, faction)) return 0;
        return card.CardId == "ST03-08" && faction == "asgard" ? 3 : 1;
    }

    internal static int StarterGraveFactionCopies(L12PlayerState owner, L12CardInstance card,
        string faction, bool legionOnly)
        => string.IsNullOrWhiteSpace(faction)
            ? StarterGraveCardCopies(card)
            : legionOnly
            ? StarterGraveFactionLegionCopies(owner, card, faction)
            : StarterGraveFactionCardCopies(owner, card, faction);

    internal static int StarterGraveCardCopies(L12CardInstance card)
        => card.CardId == "ST03-08" ? 3 : 1;

    internal static int MinimumPhysicalGraveCardsForCount(L12PlayerState owner,
        IReadOnlyCollection<L12CardInstance> cards, string faction, int required, bool legionOnly)
    {
        var represented = 0;
        var physical = 0;
        foreach (var copies in cards.Select(card => StarterGraveFactionCopies(owner, card, faction, legionOnly))
                     .Where(copies => copies > 0).OrderByDescending(copies => copies))
        {
            represented += copies;
            physical++;
            if (represented >= required) return physical;
        }
        return required + 1;
    }

    internal static bool CanPotentiallyRepresentGraveFactionCount(L12PlayerState owner,
        IReadOnlyCollection<L12CardInstance> cards, string faction, int required, bool legionOnly)
        => required > 0 && cards.Count is > 0 && cards.Count <= required
            && cards.All(card => StarterGraveFactionCopies(owner, card, faction, legionOnly) > 0)
            && cards.Sum(card => StarterGraveFactionCopies(owner, card, faction, legionOnly)) >= required;

    internal static IReadOnlyDictionary<string, string> GraveFactionRepresentationChoices(L12PlayerState owner,
        IReadOnlyList<L12CardInstance> cards, string faction, int required, bool legionOnly)
        => GraveRepresentationChoices(owner, cards, faction, required, required, legionOnly);

    internal static IReadOnlyDictionary<string, string> GraveRepresentationChoices(L12PlayerState owner,
        IReadOnlyList<L12CardInstance> cards, string faction, int minimum, int maximum, bool legionOnly)
    {
        if (cards.Count == 0 || minimum < 0 || maximum < minimum
            || cards.Any(card => StarterGraveFactionCopies(owner, card, faction, legionOnly) == 0)
            || cards.Count > maximum
            || cards.Sum(card => StarterGraveFactionCopies(owner, card, faction, legionOnly)) < minimum)
            return new Dictionary<string, string>();
        var variables = cards.Where(card => StarterGraveFactionCopies(owner, card, faction, legionOnly) > 1).ToArray();
        if (variables.Length == 0) return new Dictionary<string, string>();
        var fixedCount = cards.Count - variables.Length;
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var counts = new int[variables.Length];

        void Build(int index, int total)
        {
            if (index == variables.Length)
            {
                if (total < minimum || total > maximum) return;
                var token = "grave-copies:" + string.Join(',', variables.Select((card, variableIndex) =>
                    $"{card.InstanceId}={counts[variableIndex]}"));
                var label = string.Join("；", variables.Select((card, variableIndex) =>
                    variables.Length == 1
                        ? $"〈{card.Name}〉本次视为{counts[variableIndex]}张"
                        : $"第{variableIndex + 1}张〈{card.Name}〉本次视为{counts[variableIndex]}张"));
                results[token] = label;
                return;
            }
            var cardMaximum = StarterGraveFactionCopies(owner, variables[index], faction, legionOnly);
            for (var count = 1; count <= cardMaximum; count++)
            {
                counts[index] = count;
                Build(index + 1, total + count);
            }
        }

        Build(0, fixedCount);
        return results;
    }

    internal static bool TryGetGraveRepresentationCount(L12PlayerState owner,
        IReadOnlyList<L12CardInstance> cards, string? representation, string faction, bool legionOnly,
        out int representedCount)
    {
        representedCount = 0;
        if (cards.Count == 0
            || cards.Any(card => StarterGraveFactionCopies(owner, card, faction, legionOnly) == 0)) return false;
        var variables = cards.Where(card => StarterGraveFactionCopies(owner, card, faction, legionOnly) > 1).ToArray();
        if (variables.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(representation)) return false;
            representedCount = cards.Count;
            return true;
        }
        if (string.IsNullOrWhiteSpace(representation)
            || !representation.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase)) return false;
        var declared = representation["grave-copies:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[1], out _))
            .ToDictionary(parts => parts[0], parts => int.Parse(parts[1]), StringComparer.OrdinalIgnoreCase);
        if (declared.Count != variables.Length || variables.Any(card => !declared.ContainsKey(card.InstanceId))) return false;
        if (variables.Any(card => declared[card.InstanceId] is < 1
                || declared[card.InstanceId] > StarterGraveFactionCopies(owner, card, faction, legionOnly))) return false;
        representedCount = cards.Sum(card => declared.GetValueOrDefault(card.InstanceId, 1));
        return true;
    }

    internal static bool IsExactGraveFactionRepresentation(L12PlayerState owner,
        IReadOnlyList<L12CardInstance> cards, string? representation, string faction, int required, bool legionOnly)
    {
        if (!CanPotentiallyRepresentGraveFactionCount(owner, cards, faction, required, legionOnly)) return false;
        return TryGetGraveRepresentationCount(owner, cards, representation, faction, legionOnly,
            out var representedCount) && representedCount == required;
    }

    internal static bool IsExactGraveCardRepresentation(L12PlayerState owner,
        IReadOnlyList<L12CardInstance> cards, string? representation, int required)
        => IsExactGraveFactionRepresentation(owner, cards, representation, string.Empty, required, legionOnly: false);

    internal static bool TryResolveGraveCostDeclaration(L12PlayerState owner,
        IEnumerable<string> declaredValues, int required, string faction, bool legionOnly,
        out L12CardInstance[] physicalCards, out string? representation)
    {
        var values = declaredValues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        representation = values.SingleOrDefault(value =>
            value.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase));
        physicalCards = values.Where(value => !value.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase))
            .Select(value => owner.Graveyard.FirstOrDefault(card => card.InstanceId == value
                && StarterGraveFactionCopies(owner, card, faction, legionOnly) > 0))
            .OfType<L12CardInstance>().ToArray();
        var declaredPhysicalIds = values.Where(value => !value.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase)
            && owner.Graveyard.Any(card => card.InstanceId == value)).ToArray();
        return physicalCards.Length == declaredPhysicalIds.Length
            && IsExactGraveFactionRepresentation(owner, physicalCards, representation, faction, required, legionOnly);
    }

    internal static bool CanRepresentGraveFactionCardCount(L12PlayerState owner,
        IReadOnlyCollection<L12CardInstance> cards, string faction, int required)
        => CanPotentiallyRepresentGraveFactionCount(owner, cards, faction, required, legionOnly: false);

    internal static int StarterDisasterTroopsBonus(string? activeDisasterCardId, L12CardInstance card)
        => activeDisasterCardId == "ST-DS02" && card.DisasterLevel > 0 ? 1000 : 0;

    internal static bool TryGetStarterBatch3BAbilities(string cardId,
        out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "ST-DS02" =>
            [
                new("continuous", "continuous", "持续 带有天灾等级的军团兵力+1000，且发动进攻需要弃置1张手牌。",
                [
                    new(L12AtomKinds.Condition, "军团带有天灾等级", "condition", new() { ["expression"] = "source.disaster-level>0" }),
                    new(L12AtomKinds.ModifyTroops, "兵力+1000", "continuous", new() { ["value"] = "1000" }),
                    new(L12AtomKinds.Discard, "发动进攻时弃置1张手牌", "cost", new() { ["from"] = "attacker-controller.hand", ["amount"] = "1" }),
                ], "human-assisted", "product-database"),
            ],
            "ST01-M1" => [Targeted("morale-return", "回合1次 我方返还士气时，可从士气牌库追加1张休整的士气。",
                new(L12AtomKinds.Optional, "可发动", "condition", new()),
                new(L12AtomKinds.AddMorale, "追加1张休整士气", "resolution", new() { ["amount"] = "1", ["state"] = "rested" }))],
            "ST02-07" => [Targeted("opponent-back-to-front", "回合1次 对方军团从后排位移至前排时，可使其本回合兵力-3000。",
                new(L12AtomKinds.Optional, "可发动", "condition", new()), Troops(-3000))],
            "ST02-10" => [Targeted("play", "将墓地1张<陵墓守卫>活跃登场，回合结束时，弃置该<陵墓守卫>。",
                Select("选择墓地1张<陵墓守卫>", "controller.graveyard", "card-id=S01-0212"),
                Select("选择登场位置", "controller.field.empty-slot", "empty=true"),
                Move("controller.graveyard", "controller.field", "active"),
                new(L12AtomKinds.SetState, "回合结束时弃置", "resolution", new() { ["key"] = "target.discard-at-turn-end", ["value"] = "true" }))],
            "ST02-M1" => [Targeted("active", "我方 回合1次 可消耗1士气并弃置我方战场2张军团：选择墓地1张兵力不高于2000的【太阳城】军团休整登场。",
                new(L12AtomKinds.PayMorale, "消耗1士气", "cost", new() { ["amount"] = "1" }),
                Select("选择弃置的2张我方军团", "controller.field", "legion=true;count=2"),
                new(L12AtomKinds.Discard, "弃置所选2张军团", "cost", new() { ["amount"] = "2" }),
                Select("选择墓地目标", "controller.graveyard", "faction=taiyangcheng;legion=true;base-troops<=2000"),
                Select("选择登场位置", "controller.field.empty-slot", "empty=true"), Move("controller.graveyard", "controller.field", "rested"))],
            "ST03-08" => [new("continuous", "continuous", "「位于墓地」可最多视为3张【阿斯加德】军团。",
                [new(L12AtomKinds.SetState, "在墓地计算为最多3张阿斯加德军团", "continuous", new() { ["key"] = "source.grave-faction-legion-copies", ["value"] = "3" })],
                "human-assisted", "product-database")],
            "ST03-10" => [Targeted("play", "选择我方1张【阿斯加德】军团，本回合兵力+2000。每当我方墓地有3张【阿斯加德】军团，该军团兵力额外+1000。",
                Select("选择我方1张阿斯加德军团", "controller.field", "faction=asgard;legion=true"), Troops(2000),
                new(L12AtomKinds.ModifyTroops, "墓地每3张阿斯加德军团额外+1000", "resolution", new() { ["per"] = "3", ["value"] = "1000" }))],
            "ST03-M1" => [Targeted("active", "我方 回合1次 可将墓地3张【阿斯加德】卡牌自选顺序返回牌库底部：抽取1张牌。",
                Select("选择并排序墓地1至3张可合计视为3张的阿斯加德卡牌", "controller.graveyard", "faction=asgard;represented-count=3"),
                new(L12AtomKinds.MoveZone, "依序返回牌库底部", "cost", new() { ["to"] = "controller.library-bottom" }),
                new(L12AtomKinds.Draw, "抽取1张牌", "resolution", new() { ["amount"] = "1" }))],
            "ST04-02" =>
            [
                Targeted("attack", "进攻时 若我方手牌数量不高于对方，对方弃置1张手牌。",
                    new(L12AtomKinds.Condition, "我方手牌数量不高于对方", "condition", new() { ["expression"] = "controller.hand<=opponent.hand" }),
                    new(L12AtomKinds.Discard, "对方弃置1张手牌", "resolution", new() { ["from"] = "opponent.hand", ["amount"] = "1", ["selection"] = "opponent" })),
                Targeted("death", "阵亡时 可击杀对方最多2张原本兵力不高于2000的军团。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    new(L12AtomKinds.SelectTarget, "选择对方最多2张军团", "target", new() { ["zone"] = "opponent.field", ["min"] = "0", ["max"] = "2", ["filter"] = "base-troops<=2000" }),
                    new(L12AtomKinds.MoveZone, "击杀所选军团", "resolution", new() { ["to"] = "owner.graveyard" })),
            ],
            "ST04-04" => [Targeted("enter", "登场时 本回合我方主宰效果无需消耗士气。",
                new L12StructuredAtomTemplate(L12AtomKinds.SetState, "本回合主宰效果士气费用为0", "resolution", new() { ["key"] = "controller.master-morale-waiver", ["duration"] = "this-turn" }))],
            "ST04-05" => [new("opponent-turn-lethal", "replacement", "对方回合 当我方其他【高天原】军团即将阵亡时，可弃置此军团：代替其承受该次进攻或效果。",
                [new(L12AtomKinds.Discard, "弃置此军团", "cost", new()), new(L12AtomKinds.SetState, "代替承受致命结果", "resolution", new() { ["key"] = "lethal-substitution" })],
                "human-assisted", "product-database")],
            "ST04-10" =>
            [
                new("continuous", "continuous", "若我方战场存在<武田信玄>，此战术从手牌打出的费用-1。",
                [
                    new(L12AtomKinds.Condition, "位于手牌且我方战场有武田信玄", "condition", new() { ["expression"] = $"source.zone=hand;controller.field.card-id={StarterTakedaShingenCardId}" }),
                    new(L12AtomKinds.SetState, "登场费用-1", "continuous", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
                ], "human-assisted", "product-database"),
                Targeted("play", "将此战术叠放至我方1张【高天原】军团下方，被叠放的军团获得强攻。（进攻对主宰造成额外1点伤害。）",
                    Select("选择我方1张高天原军团", "controller.field", "faction=gaotianyuan;legion=true"),
                    new(L12AtomKinds.MoveZone, "叠放至所选军团下方", "resolution", new() { ["to"] = "target.attached" }),
                    new(L12AtomKinds.Keyword, "获得强攻", "continuous", new() { ["keyword"] = "strong-attack" })),
            ],
            "ST04-M1" => [Targeted("legion-attack-timing", "回合1次 我方军团进攻/被进攻时，可消耗1士气或弃置1张手牌：该军团本回合兵力+2000。",
                new(L12AtomKinds.Optional, "可发动", "condition", new()), new(L12AtomKinds.SelectMode, "选择费用", "target", new() { ["options"] = "morale|discard" }),
                new(L12AtomKinds.PayMorale, "消耗1士气", "cost", new() { ["amount"] = "1" }),
                new(L12AtomKinds.Discard, "或弃置1张手牌", "cost", new() { ["amount"] = "1" }), Troops(2000))],
            "ST05-01" => [Targeted("promotion-enter", "晋升登场时 可从牌库选择最多2张【远程】军团活跃登场，随后重洗牌库。",
                new(L12AtomKinds.Optional, "可发动", "condition", new()),
                new(L12AtomKinds.Visibility, "仅控制者查看牌库候选", "target", new() { ["audience"] = "controller-only" }),
                new(L12AtomKinds.SelectTarget, "选择牌库最多2张远程军团", "target", new() { ["zone"] = "controller.library", ["min"] = "0", ["max"] = "2", ["filter"] = "ranged=true" }),
                Move("controller.library", "controller.field", "active"), new(L12AtomKinds.Shuffle, "重洗牌库", "resolution", new()))],
            "ST05-06" => [Targeted("active", "主动休整 查看牌库顶部3张牌，选择其中1张【远程】军团或【奥林匹斯】战术卡，展示并加入手牌，其余卡牌自选顺序全部返回牌库顶部或全部返回牌库底部。",
                new(L12AtomKinds.RestSource, "休整此军团", "cost", new()),
                new(L12AtomKinds.Visibility, "查看牌库顶部3张牌", "resolution", new()
                {
                    ["audience"] = "controller-only", ["count"] = "3",
                }),
                new(L12AtomKinds.SelectTarget, "选择1张远程军团或奥林匹斯战术卡", "resolution", new()
                {
                    ["zone"] = "controller.library-top-3", ["min"] = "1", ["max"] = "1",
                    ["filter"] = "ranged-legion=true|faction=olympus;tactic=true",
                }),
                new(L12AtomKinds.Visibility, "展示所选卡牌", "resolution", new()
                {
                    ["audience"] = "both",
                }),
                Move("controller.library", "controller.hand", "revealed"),
                new(L12AtomKinds.MoveZone, "其余卡牌自选顺序全部返回牌库顶部或全部返回牌库底部", "resolution", new()
                {
                    ["from"] = "controller.library-top-3", ["to"] = "controller.library",
                    ["placement"] = "all-top-or-all-bottom",
                }))],
            "ST05-10" => [Targeted("play", "选择一项：我方1张【奥林匹斯】军团本回合震击伤害+2000；或我方1张【奥林匹斯】【远程】军团本回合进攻时兵力+2000。",
                new(L12AtomKinds.SelectMode, "选择一项效果", "target", new() { ["options"] = "shock|ranged-attack" }),
                Select("选择我方奥林匹斯军团", "controller.field", "faction=olympus;legion=true"),
                new(L12AtomKinds.SetState, "震击伤害或进攻兵力+2000", "resolution", new() { ["duration"] = "this-turn" }))],
            "ST02-08" =>
            [
                new("enter", "triggered", "登场时 抽取1张卡牌。随后，对方抽取1张牌。",
                [
                    new(L12AtomKinds.Draw, "我方抽取1张牌", "resolution", new() { ["amount"] = "1", ["target"] = "controller" }),
                    new(L12AtomKinds.Draw, "随后对方抽取1张牌", "resolution", new() { ["amount"] = "1", ["target"] = "opponent" }),
                ], "human-assisted", "product-database"),
                Targeted("death", "阵亡时 可弃置1张手牌：我方主宰增加1点血量。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    Select("选择弃置的1张手牌", "controller.hand", "any=true"),
                    new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new()),
                    new(L12AtomKinds.HealMaster, "我方主宰增加1点血量", "resolution", new() { ["amount"] = "1" })),
            ],
            "ST06-09" =>
            [
                Targeted("enter", "登场时 可击杀对方最多2张原本兵力不高于2000的军团。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()),
                    new(L12AtomKinds.SelectTarget, "选择对方最多2张原本兵力不高于2000的军团", "target", new()
                    {
                        ["zone"] = "opponent.field", ["min"] = "0", ["max"] = "2", ["filter"] = "base-troops<=2000",
                    }),
                    new(L12AtomKinds.MoveZone, "击杀所选军团", "resolution", new() { ["to"] = "owner.graveyard" })),
                Targeted("active", "主动休整 弃置1张手牌：选择我方前排1张【彼界】军团，本回合兵力+2000；或获得1符文。",
                    new(L12AtomKinds.RestSource, "主动休整", "cost", new()),
                    Select("选择弃置的1张手牌", "controller.hand", "any=true"),
                    new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new()),
                    new(L12AtomKinds.SelectMode, "选择使军团兵力+2000或获得1符文", "target", new() { ["options"] = "buff|rune" })),
            ],
        "ST05-M1" => [Targeted("active", "我方 回合1次 可弃置1张手牌：翻转1张士气。选择我方前排最多2张【奥林匹斯】军团，本回合兵力+1000，且对对方主宰造成的伤害+1。",
                Select("选择弃置的1张手牌", "controller.hand", "any=true"), new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new()),
                Select("选择翻转的1张士气", "controller.morale", "any=true"),
                new(L12AtomKinds.SelectTarget, "选择前排最多2张奥林匹斯军团", "target", new() { ["zone"] = "controller.front", ["min"] = "0", ["max"] = "2" }),
                Troops(1000), new(L12AtomKinds.AttackRule, "对主宰伤害+1", "resolution", new() { ["value"] = "1", ["duration"] = "this-turn" }))],
            "ST06-M1" =>
            [
                Targeted("rune-spent", "我方消耗符文时，每消耗1符文，可选择我方1张【彼界】军团，本回合兵力+1000。",
                    new(L12AtomKinds.Optional, "可发动", "condition", new()), Select("选择我方1张彼界军团", "controller.field", "faction=otherworld;legion=true"), Troops(1000)),
                Targeted("active", "我方 回合1次 可消耗2符文：将我方最多2张士气转为活跃，试炼+2。",
                    new(L12AtomKinds.Special, "消耗2符文", "cost", new() { ["resource"] = "rune", ["amount"] = "2" }),
                    new(L12AtomKinds.SelectTarget, "选择最多2张休整士气", "target", new() { ["zone"] = "controller.morale", ["min"] = "0", ["max"] = "2", ["filter"] = "tapped=true" }),
                    new(L12AtomKinds.Ready, "将所选士气转为活跃", "resolution", new()),
                    new(L12AtomKinds.AdvanceTrial, "试炼+2", "resolution", new() { ["amount"] = "2" })),
            ],
            "ST06-S1" =>
            [
                Targeted("trial-complete", "触发 可获得2符文。我方主宰可增加2点血量。可抽取1张牌。",
                    new(L12AtomKinds.GainRune, "可获得2符文", "resolution", new() { ["amount"] = "2" }),
                    new(L12AtomKinds.HealMaster, "我方主宰可增加2点血量", "resolution", new() { ["amount"] = "2" }),
                    new(L12AtomKinds.Draw, "可抽取1张牌", "resolution", new() { ["amount"] = "1" })),
                Targeted("active", "我方 回合1次 本回合我方下1张【彼界】军团登场费用-1。",
                    new L12StructuredAtomTemplate(L12AtomKinds.SetState, "下一张彼界军团登场费用-1", "resolution", new() { ["key"] = "controller.next-otherworld-entry-discount", ["value"] = "1", ["duration"] = "this-turn" })),
            ],
            _ => [],
        };
        return abilities.Count > 0;
    }
}
