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
            "S02-0501" => HeraclesPromotedAbilities(),
            "S02-0502" => HeraclesAbilities(),
            "S02-0503" => AchillesPromotedAbilities(),
            "S02-0504" => AchillesAbilities(),
            "S02-0505" => PerseusPromotedAbilities(),
            "S02-0506" => PerseusAbilities(),
            "S02-0507" => AtalantaAbilities(),
            "S02-0508" => AtalantaBaseAbilities(),
            "S02-0509" => OdysseusAbilities(),
            "S02-0510" => HippolytaAbilities(),
            "S02-0511" => ProloteAbilities(),
            "S02-0512" => AeneasAbilities(),
            "S02-0518" => TheseusAbilities(),
            "S01-0409" => YoshitsuneAbilities(),
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static IReadOnlyList<L12StructuredAbilityTemplate> AtalantaAbilities() => Assisted(
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
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HeraclesPromotedAbilities() => Assisted(
    [
        PromotionAbility(2),
        new("promotion-enter", "triggered", "晋升登场 可展示手牌中1张军团并将其放回牌库顶部：击杀对方1张费用不高于展示军团其费用的军团。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择手牌中的 1 张军团", "target", new() { ["zone"] = "controller.hand", ["filter"] = "card-type=legion", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("展示所选军团并由对手确认"),
            new(L12AtomKinds.MoveZone, "将展示军团放回牌库顶部", "cost", new() { ["from"] = "controller.hand", ["to"] = "controller.library-top", ["reason"] = "promotion-enter-cost" }),
            new(L12AtomKinds.SelectTarget, "选择对方费用不高于展示军团费用的军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion;cost<=revealed-card.cost", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "击杀所选军团", "resolution", new() { ["from"] = "opponent.field", ["to"] = "owner.graveyard", ["reason"] = "effect-kill" }),
        ]),
        new("enter", "triggered", "登场时 可对双方主宰各造成1点非致命伤害。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.DamageMaster, "双方主宰各受到 1 点非致命伤害", "resolution", new() { ["amount"] = "1", ["target"] = "both", ["lethal"] = "false" }),
        ]),
        new("attack", "triggered", "进攻时 本回合获得强攻。",
        [
            new(L12AtomKinds.Keyword, "获得【强攻】", "resolution", new() { ["keywordRef"] = "strong-attack", ["grantedAbility"] = "keyword.strong-attack" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HeraclesAbilities() => Confirmed(
    [
        new("enter", "triggered", "登场时 可抽取2张牌，并弃置1张手牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 2 张牌", "resolution", new() { ["amount"] = "2" }),
            new(L12AtomKinds.SelectTarget, "选择 1 张手牌", "target", new() { ["zone"] = "controller.hand", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选手牌", "resolution", new() { ["amount"] = "1", ["reason"] = "effect-discard" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AchillesPromotedAbilities() => Assisted(
    [
        PromotionAbility(2),
        new("static", "continuous", "进攻无损，此军团受到远程进攻兵力额外-1000。",
        [
            new(L12AtomKinds.AttackRule, "进攻无损", "resolution", new() { ["attackNoLoss"] = "true" }),
            new(L12AtomKinds.ModifyTroops, "受到远程进攻时兵力额外 -1000", "resolution", new() { ["condition"] = "incoming-attack.is-ranged", ["operation"] = "add", ["value"] = "-1000" }),
            new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
        ]),
        new("promotion-enter", "triggered", "晋升登场 本回合可进攻对方军团。",
        [
            new(L12AtomKinds.AttackRule, "本回合可进攻对方军团", "resolution", new() { ["canAttackOpponentLegion"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("after-attack", "triggered", "击杀时 直到我方下个回合开始前，此军团获得 ABILITY 5。",
        [
            new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }),
            new(L12AtomKinds.SetState, "获得 ABILITY 5", "resolution", new() { ["abilityRef"] = "S02-0503:ability:5", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "直到我方下个回合开始前", "duration", new() { ["duration"] = "until-controller-next-turn-start" }),
        ]),
        new("granted-static", "granted-continuous", "「位于前排」获得 ABILITY 6。",
        [
            new(L12AtomKinds.Condition, "位于前排且已获得 ABILITY 5", "condition", new() { ["expression"] = "source.row=front;source.has-ability=S02-0503:ability:5" }),
            new(L12AtomKinds.SetState, "启用 ABILITY 6", "resolution", new() { ["abilityRef"] = "S02-0503:ability:6", ["value"] = "true" }),
        ]),
        new("keyword-definition", "keyword-definition", "挑衅 对方只可进攻拥有挑衅效果的军团，若有多个具有挑衅效果的军团，则可以选择其中1个进行进攻。",
        [
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new() { ["keywordRef"] = "taunt", ["targetRule"] = "opponent-must-attack-taunt-legion" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AchillesAbilities() => Assisted(
    [
        new("static", "continuous", "「位于前排」我方主宰无法被兵力不高于2000的军团进攻。",
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.AttackRule, "保护我方主宰免受兵力不高于 2000 的军团进攻", "resolution", new() { ["protectMasterFromTroopsAtMost"] = "2000" }),
            new(L12AtomKinds.Duration, "位于前排期间持续", "duration", new() { ["duration"] = "while-source-row-front" }),
        ]),
        new("lethal-replacement", "replacement", "「位于前排」回合1次 即将阵亡时，可消耗并翻转1神力：代替承受本次致命进攻或效果。",
        [
            new(L12AtomKinds.Condition, "位于前排、即将阵亡且本回合未发动", "condition", new() { ["expression"] = "source.row=front;source.would-die=true;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "消耗并翻转 1 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = "1", ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.SetState, "代替承受致命结果并保持致命时刻状态", "resolution", new() { ["operation"] = "replace-lethal-result", ["preserveTroops"] = "true", ["preserveTappedState"] = "true" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PerseusPromotedAbilities() => Assisted(
    [
        PromotionAbility(1),
        new("promotion-enter", "triggered", "晋升登场 可选择对方1张休整的军团，使其在下个对方重置阶段无法转为活跃。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择对方 1 张休整的军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion;tapped=true", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SetState, "下个对方重置阶段无法转为活跃", "resolution", new() { ["key"] = "target.skip-next-reset-ready", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "持续至下个对方重置阶段", "duration", new() { ["duration"] = "until-opponent-next-reset" }),
        ]),
        new("active", "activated", "我方 回合1次 可进行1次位移。",
        [
            new(L12AtomKinds.Condition, "我方回合、此军团活跃且本回合未发动", "condition", new() { ["expression"] = "controller.turn;source.ready=true;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Move, "进行 1 次骑兵位移", "resolution", new() { ["operation"] = "cavalry-move", ["amount"] = "1" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
        new("enter", "triggered", "登场时 获得 ABILITY 5。",
        [
            new(L12AtomKinds.SetState, "获得 ABILITY 5", "resolution", new() { ["abilityRef"] = "S02-0505:ability:5", ["value"] = "true" }),
        ]),
        new("keyword-definition", "keyword-definition", "冲锋 在登场的回合即可进行进攻。",
        [
            new(L12AtomKinds.Keyword, "【冲锋】规则引用", "resolution", new() { ["keywordRef"] = "charge", ["attackRule"] = "ignore-summoning-sickness" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PerseusAbilities() => Confirmed(
    [
        new("enter", "triggered", "登场时 可弃置1张手牌：将墓地1张<珀尔修斯·晋升>加入手牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择 1 张手牌", "target", new() { ["zone"] = "controller.hand", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new() { ["amount"] = "1", ["reason"] = "ability-cost" }),
            new(L12AtomKinds.SelectTarget, "选择墓地中的〈珀尔修斯·晋升〉", "target", new() { ["zone"] = "controller.graveyard", ["filter"] = "card-id=S02-0505", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.graveyard", ["to"] = "controller.hand" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AtalantaBaseAbilities() => Assisted(
    [
        new("static", "continuous", "进攻距离+1，远程进攻无损。",
        [
            new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1", ["professionAssignment"] = "弓手" }),
            new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
        ]),
        new("death", "triggered", "阵亡时 翻转1张士气。",
        [
            new(L12AtomKinds.Special, "翻转 1 张士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip", ["amount"] = "1" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> OdysseusAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为0张，此军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 0 张", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power=0" }),
            new(L12AtomKinds.SetState, "登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("enter", "triggered", "登场时 本回合从手牌中打出的下1张战术卡无需消耗费用。",
        [
            new(L12AtomKinds.SetState, "下 1 张手牌战术无需消耗费用", "resolution", new() { ["key"] = "controller.next-hand-tactic-free", ["value"] = "1" }),
            new(L12AtomKinds.Duration, "使用后或本回合结束时失效", "duration", new() { ["duration"] = "next-use-or-turn-end" }),
        ]),
        new("attack", "triggered", "进攻时 可展示手牌中的1张战术卡：此军团本回合兵力+1000。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择手牌中的 1 张战术卡", "target", new() { ["zone"] = "controller.hand", ["filter"] = "card-type=tactic", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("展示所选战术并由对手确认"),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +1000", "resolution", new() { ["operation"] = "add", ["value"] = "1000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HippolytaAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为5张及以上，此军团登场费用-3。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 5 张及以上", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power>=5" }),
            new(L12AtomKinds.SetState, "登场费用 -3", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-3" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("static", "continuous", "此军团休整时，我方军团前后位移无需消耗费用。",
        [
            new(L12AtomKinds.Condition, "此军团位于战场且处于休整", "condition", new() { ["expression"] = "source.zone=field;source.tapped=true" }),
            new(L12AtomKinds.Move, "为符合通常位移条件的军团启用【免费位移】", "resolution", new() { ["operation"] = "enable-free-front-back-move", ["button"] = "免费位移", ["requiresReady"] = "true", ["direction"] = "front-back" }),
            new(L12AtomKinds.Duration, "此军团休整期间持续", "duration", new() { ["duration"] = "while-source-tapped-on-field" }),
        ]),
        new("active", "activated", "主动休整 消耗3士气并弃置1张手牌：选择墓地1张费用不高于4的【奥林匹斯】军团活跃登场。",
        [
            new(L12AtomKinds.Condition, "我方回合且此军团活跃", "condition", new() { ["expression"] = "controller.turn;source.ready=true" }),
            new(L12AtomKinds.RestSource, "将此军团转为休整", "cost", new()),
            new(L12AtomKinds.PayMorale, "支付 3 士气", "cost", new() { ["amount"] = "3" }),
            new(L12AtomKinds.SelectTarget, "选择 1 张手牌", "target", new() { ["zone"] = "controller.hand", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new() { ["amount"] = "1", ["reason"] = "ability-cost" }),
            new(L12AtomKinds.SelectTarget, "选择墓地中费用不高于 4 的【奥林匹斯】军团", "target", new() { ["zone"] = "controller.graveyard", ["filter"] = "card-type=legion;faction=olympus;cost<=4", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SelectTarget, "选择我方战场空位", "target", new() { ["zone"] = "controller.field-empty-slot", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "所选军团活跃登场", "resolution", new() { ["from"] = "controller.graveyard", ["to"] = "controller.field", ["tapped"] = "false" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> ProloteAbilities() => Assisted(
    [
        new("enter", "triggered", "登场时 本回合可进攻对方军团。",
        [
            new(L12AtomKinds.AttackRule, "本回合可进攻对方军团", "resolution", new() { ["canAttackOpponentLegion"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("attack", "triggered", "进攻时 若目标为对方军团，可消耗并翻转1神力：本回合兵力+1000，并获得 ABILITY 3。",
        [
            new(L12AtomKinds.Condition, "进攻目标为对方军团", "condition", new() { ["expression"] = "attack.target=opponent-legion" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "消耗并翻转 1 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = "1", ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +1000", "resolution", new() { ["operation"] = "add", ["value"] = "1000" }),
            new(L12AtomKinds.SetState, "获得 ABILITY 3", "resolution", new() { ["abilityRef"] = "S02-0511:ability:3", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("keyword-definition", "keyword-definition", "震击 进攻时，被进攻者左右相邻的军团在本回合中兵力-2000。",
        [
            new(L12AtomKinds.Keyword, "【震击】规则引用", "resolution", new() { ["keywordRef"] = "shock", ["adjacentTroopsDelta"] = "-2000" }),
            new(L12AtomKinds.Duration, "相邻军团兵力修正持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AeneasAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为0张，此军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 0 张", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power=0" }),
            new(L12AtomKinds.SetState, "登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("static", "continuous", "「位于前排」获得 ABILITY 3。",
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.SetState, "启用 ABILITY 3", "resolution", new() { ["abilityRef"] = "S02-0512:ability:3", ["value"] = "true" }),
        ]),
        new("keyword-definition", "keyword-definition", "挑衅 对方只可进攻拥有挑衅效果的军团，若有多个具有挑衅效果的军团，则可以选择其中1个进行进攻。",
        [
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new() { ["keywordRef"] = "taunt", ["targetRule"] = "opponent-must-attack-taunt-legion" }),
        ]),
        new("death", "triggered", "阵亡时 可抽取1张牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> TheseusAbilities() =>
    [
        new("static", "continuous", "「位于手牌」若我方神力为0张，此军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 0 张", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power=0" }),
            new(L12AtomKinds.SetState, "登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("enter", "triggered", "登场时 可翻转1张休整的士气。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "翻转 1 张休整士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip-rested", ["amount"] = "1" }),
        ]),
        new("death", "triggered", "阵亡时 可选择墓地1张【晋升者】军团加入手牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择墓地中的 1 张【晋升者】军团", "target", new() { ["zone"] = "controller.graveyard", ["filter"] = "card-type=legion;trait=晋升者", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.graveyard", ["to"] = "controller.hand" }),
        ]),
    ];

    private static L12StructuredAbilityTemplate PromotionAbility(int godPower) =>
        new("promotion", "summon-flow", $"晋升 消耗并翻转{godPower}神力，叠放至我方同名非【晋升者】军团上方登场。",
        [
            new(L12AtomKinds.Condition, "存在我方同名非【晋升者】军团", "condition", new() { ["expression"] = "controller.same-name-non-promoted-foundation" }),
            new(L12AtomKinds.Special, $"消耗并翻转 {godPower} 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = godPower.ToString(), ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.SelectTarget, "选择同名晋升基底", "target", new() { ["zone"] = "controller.field", ["filter"] = "same-name-non-promoted", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "叠放至基底上方晋升登场", "resolution", new() { ["operation"] = "promotion-enter", ["inheritState"] = "true" }),
        ]);

    private static L12StructuredAtomTemplate PublicReveal(string label) =>
        new(L12AtomKinds.Visibility, label, "resolution", new()
        {
            ["visibility"] = "both-players",
            ["opponentConfirmation"] = "required",
            ["log"] = "public-card-link",
        });

    private static IReadOnlyList<L12StructuredAbilityTemplate> Assisted(IReadOnlyList<L12StructuredAbilityTemplate> abilities)
        => abilities.Select(ability => ability with { ReviewStatus = "human-assisted", ReviewSource = "user-20260823" }).ToArray();

    private static IReadOnlyList<L12StructuredAbilityTemplate> Confirmed(IReadOnlyList<L12StructuredAbilityTemplate> abilities)
        => abilities.Select(ability => ability with { ReviewStatus = "confirmed", ReviewSource = "user-20260823" }).ToArray();

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
    IReadOnlyList<L12StructuredAtomTemplate> Atoms,
    string ReviewStatus = "unreviewed",
    string ReviewSource = "automatic");

public sealed record L12StructuredAtomTemplate(
    string Kind,
    string Label,
    string Stage,
    Dictionary<string, string> Parameters);
