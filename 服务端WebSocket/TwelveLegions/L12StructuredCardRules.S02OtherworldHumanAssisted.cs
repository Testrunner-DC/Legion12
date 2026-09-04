namespace TwelveLegions.Server;

/// <summary>
/// 2026-09-04 玩家人工确认的【彼界】/【圆桌骑士】能力边界。
/// 这里同时作为后台原子清单、派生费用、职介与关键词查询的权威结构；
/// 能力之间按真实触发与授予关系拆分，不按标点机械切句。
/// </summary>
public static partial class L12StructuredCardRules
{
    private const string OtherworldHumanReviewSource = "user-20260904";

    private static bool TryGetHumanAssistedOtherworldAbilities(
        string cardId, out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "S02-0601" => OtherworldCard(
                OH("enter", "triggered", "登场时 可消耗1符文：将1张<王者之剑>叠放至此军团下方。",
                    Atom(L12AtomKinds.Special, "王者之剑遵守唯一实例规则；已有剑时支付后无事发生", "resolution",
                        ("semantic", "attach-unique-kings-sword"), ("destination", "source.attached"))),
                OH("death", "triggered", "阵亡时 可将手牌中1张费用不高于4的【圆桌骑士】军团活跃登场。")),

            "S02-0602" => OtherworldCard(
                OH("enter", "triggered", "登场时 可消耗1符文：获得ABILITY 2。"),
                OH("keyword-definition", "granted-continuous", "冲锋 在登场的回合即可进行进攻。"),
                OH("after-kill", "triggered", "击杀时 可选择ABILITY 4或ABILITY 5。"),
                OH("granted", "granted-effect", "试炼+1。"),
                OH("granted", "granted-effect", "获得1符文。")),

            "S02-0603" => OtherworldCard(
                OH("continuous", "continuous", "此军团无法进攻。"),
                OH("enter", "triggered", "登场时 获得1符文。"),
                OH("active", "activated", "主动休整 消耗1符文，可选择ABILITY 4或ABILITY 5。"),
                OH("granted", "granted-effect", "选择对方1张军团，本回合兵力-3000。"),
                OH("granted", "granted-effect", "查看我方牌库，选择1张费用不高于4的<主动战术>展示并加入手牌。随后重洗牌库。")),

            "S02-0604" => OtherworldCard(
                OtherworldTrial(2),
                OH("enter", "triggered", "登场时 可发动试炼。"),
                OH("trial-completed", "triggered", "我方回合 试炼<寻找圣杯之旅>完成后，可弃置此军团：抽取1张牌，我方主宰可增加1点血量。",
                    Atom(L12AtomKinds.Condition, "寻找圣杯之旅已经完成", "condition",
                        ("expression", "controller.turn;trial.card=S02-06S1;trial.completed=true")))),

            "S02-0605" => OtherworldCard(
                OH("continuous", "continuous", "「位于手牌」我方战场每存在1张【彼界】军团，此军团登场费用-1。",
                    Atom(L12AtomKinds.Special, "每张我方【彼界】军团使登场费用-1", "resolution",
                        ("semantic", "entry-cost-minus-per-friendly-faction-legion"), ("faction", "otherworld"))),
                OH("attack", "triggered", "进攻时 可消耗1士气：本回合获得ABILITY 3。"),
                OH("keyword-definition", "granted-continuous", "强攻 此军团因进攻对主宰造成伤害时，额外再造成1点伤害。"),
                OH("death", "triggered", "阵亡时 对方弃置1张手牌。")),

            "S02-0606" => OtherworldCard(
                OtherworldTrial(1),
                OH("enter", "triggered", "登场时 获得1符文。"),
                OH("attack", "triggered", "进攻时 可弃置1张手牌：本回合兵力+2000。"),
                OH("after-kill", "triggered", "击杀时 本回合获得ABILITY 5。"),
                OH("keyword-definition", "granted-continuous", "贯穿 击杀时 在进攻军团后，以此军团剩余的兵力对对方主宰发动1次进攻，此次进攻不会触发“进攻时”效果。")),

            "S02-0607" => OtherworldCard(
                OH("enter", "triggered", "登场时 获得1符文。"),
                OH("attack", "triggered", "进攻时 可消耗X符文。每消耗1符文，本回合此军团兵力+1000，且对对方主宰造成的伤害+1。",
                    Atom(L12AtomKinds.Special, "由玩家声明并支付X符文", "cost",
                        ("semantic", "pay-variable-runes"), ("minimum", "1")),
                    Atom(L12AtomKinds.SetState, "每消耗1符文，对主宰造成的伤害+1", "resolution",
                        ("key", "source.master-damage-bonus"), ("operation", "add-per-paid-rune")))),

            "S02-0608" => OtherworldCard(
                OH("enter", "triggered", "登场时 试炼+2。可从我方战场/手牌/牌库/墓地将最多3张<侍从骑士>叠放至此军团下方，且直到下个我方回合开始前，获得ABILITY 2。",
                    Atom(L12AtomKinds.SelectTarget, "从战场、手牌、牌库、墓地选择最多3张侍从骑士", "resolution",
                        ("zone", "controller.field|controller.hand|controller.library|controller.grave"),
                        ("filter", "card-id=S02-0609"), ("min", "0"), ("max", "3"), ("showSource", "true"))),
                OH("keyword-definition", "granted-continuous", "免死 仅1次，即将阵亡时，将兵力在本回合变为1000作为代替。"),
                OH("attack", "triggered", "进攻时 对方进行抵挡/支援需要额外弃置1张手牌，否则本次抵挡/支援无效。"),
                OH("attack", "triggered", "可弃置下方任意数量<侍从骑士>：每弃置1张，本回合兵力+1000。")),

            "S02-0609" => OtherworldCard(
                OtherworldTrial(1),
                OH("continuous", "continuous", "无法进攻主宰。",
                    Atom(L12AtomKinds.AttackRule, "无法进攻主宰", "resolution", ("cannotAttackMaster", "true"))),
                OH("death", "triggered", "阵亡时 试炼+1。")),

            "S02-0610" => OtherworldCard(
                OtherworldTrial(1),
                OH("enter", "triggered", "登场时 可发动试炼。"),
                OH("after-trial", "triggered", "此军团发动试炼后可消耗1符文：将此军团转为活跃，且本回合无法再次发动试炼。",
                    Atom(L12AtomKinds.SetState, "本回合此军团无法再次发动试炼", "resolution",
                        ("key", "source.trial-advance-lock"), ("value", "current-turn")))),

            "S02-0611" => OtherworldCard(
                OH("continuous", "continuous", "「位于手牌」我方战场上存在<斯卡哈>时，此军团登场费用-2。"),
                OH("enter", "triggered", "登场时 直到下个我方回合开始前，此军团「位于前排」获得ABILITY 3。"),
                OH("keyword-definition", "granted-continuous", "免死 仅1次，即将阵亡时，将兵力在本回合变为1000作为代替。"),
                OH("after-kill", "triggered", "击杀时 本回合获得ABILITY 5。"),
                OH("keyword-definition", "granted-continuous", "贯穿 击杀时 在进攻军团后，以此军团剩余的兵力对对方主宰发动1次进攻，此次进攻不会触发“进攻时”效果。")),

            "S02-0612" => OtherworldCard(
                OH("continuous", "continuous", "「位于手牌」我方战场上存在<库丘林>时，此军团登场费用-2。"),
                OH("enter", "triggered", "登场时 获得ABILITY 3。"),
                OH("keyword-definition", "granted-continuous", "冲锋 在登场的回合即可进行进攻。"),
                OH("attack", "triggered", "进攻时 可消耗1符文：本回合进攻无损且兵力+2000。")),

            "S02-0613" => OtherworldCard(
                OtherworldTrial(1),
                OH("enter", "triggered", "登场时 可弃置1张手牌：我方主宰直到下个我方回合开始前无法被进攻。"),
                OH("death", "triggered", "阵亡时 双方主宰增加1点血量。")),

            "S02-0614" => OtherworldCard(
                OtherworldTrial(1),
                OH("continuous", "continuous", "进攻距离+1，远程进攻无损。"),
                OH("enter", "triggered", "登场时 可选择ABILITY 4或ABILITY 5。"),
                OH("granted", "granted-effect", "获得1符文。"),
                OH("granted", "granted-effect", "发动试炼。")),

            "S02-0615" => OtherworldCard(
                OH("continuous", "continuous", "「位于前排」获得ABILITY 2，且在对方回合此军团兵力+1000。"),
                OH("keyword-definition", "granted-continuous", "挑衅 对方只可进攻拥有 挑衅 效果的军团，若有多个具有 挑衅效果的军团，则可以选择其中1个进行进攻。"),
                OH("death", "triggered", "当此军团因效果阵亡时，我方主宰可增加1点血量或抽取1张牌。",
                    Atom(L12AtomKinds.Condition, "此军团因效果阵亡", "condition", ("expression", "death.cause=effect")))),

            "S02-0616" => OtherworldCard(
                OH("continuous", "continuous", "此军团休整时，我方试炼军团活跃时不可被进攻。",
                    Atom(L12AtomKinds.AttackRule, "保护我方活跃试炼军团不可被进攻", "resolution",
                        ("protect", "controller.active-trial-legions"))),
                OH("enter", "triggered", "登场时 可获得1符文。"),
                OH("active", "activated", "主动休整 展示牌库顶部1张牌：若其只拥有【彼界】特征，可将其加入手牌；否则将其返回牌库顶部或底部。")),

            "S02-0617" => OtherworldCard(
                OtherworldTrial(1),
                OH("continuous", "continuous", "进攻距离+1，远程进攻无损。"),
                OH("enter", "triggered", "登场时 可从我方手牌/牌库/墓地将1张<侍从骑士>活跃登场。"),
                OH("attack", "triggered", "进攻时 获得1符文。若我方战场上存在<狮心王理查一世>，可抽取1张牌。")),

            "S02-0618" => OtherworldCard(
                OtherworldTrial(2),
                OH("continuous", "continuous", "进攻距离+1，远程进攻无损。"),
                OH("enter", "triggered", "登场时 获得1符文。")),

            "S02-0619" => OtherworldCard(
                OH("continuous", "continuous", "「位于前排」进攻距离+1，远程进攻无损。"),
                OH("enter", "triggered", "登场时 可消耗1符文：选择对方1张军团，本回合兵力-2000。")),

            "S02-0620" => OtherworldCard(
                OH("play", "spell", "获得1符文。"),
                OH("play", "spell", "可消耗1士气：查看牌库顶部3张牌，选择1张<符文之力>以外的【彼界】卡牌，展示并加入手牌，其余卡牌自选顺序返回牌库底部。")),

            "S02-0621" => OtherworldCard(
                OH("play", "spell", "查看我方牌库，选择1张【圆桌骑士】军团展示并加入手牌。随后重洗牌库。"),
                OH("play", "spell", "可消耗1士气：选择我方1张【圆桌骑士】军团，本回合兵力+2000。")),

            "S02-0622" => OtherworldCard(
                OH("hand-play", "cost-modifier", "「位于手牌」可消耗X符文：每消耗1符文，本回合此战术打出的费用-2。",
                    Atom(L12AtomKinds.Special, "由玩家声明并支付X符文", "cost",
                        ("semantic", "pay-variable-runes"), ("minimum", "0"))),
                OH("play", "spell", "选择对方1张军团，本回合兵力-6000。")),
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static IReadOnlyList<L12StructuredAbilityTemplate> OtherworldCard(
        params L12StructuredAbilityTemplate[] abilities)
    {
        var claimedRoutes = new HashSet<string>(StringComparer.Ordinal);
        return abilities.Select(ability => ability with
        {
            RuntimeRouteOwner = claimedRoutes.Add(ability.Trigger),
        }).ToArray();
    }

    private static L12StructuredAbilityTemplate OH(string trigger, string executionModel, string text,
        params L12StructuredAtomTemplate[] additionalAtoms)
        => new(trigger, executionModel, text,
            InferHumanAtoms(trigger, executionModel, text).Concat(additionalAtoms).ToArray(),
            "human-assisted", OtherworldHumanReviewSource);

    private static L12StructuredAbilityTemplate OtherworldTrial(int value)
        => new("trial", "rule", $"试炼 {value}",
        [
            Atom(L12AtomKinds.Special, $"此军团的试炼值为 {value}", "rule",
                ("semantic", "trial-value"), ("value", value.ToString())),
        ], "human-assisted", OtherworldHumanReviewSource);
}
