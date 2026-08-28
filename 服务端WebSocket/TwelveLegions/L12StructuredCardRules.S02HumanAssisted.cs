using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

/// <summary>
/// 2026-08-29 玩家人工辅助拆分的 S02 卡效。
/// 能力边界由人工定义；能力内部仍按条件、目标、费用、结算、期限与信息可见性组织原子，
/// 严禁再按句号或换行机械切分。尚未迁移的实战分支只保留一个复合流程/旧实现兜底。
/// </summary>
public static partial class L12StructuredCardRules
{
    private const string HumanS02ReviewSource = "user-20260829";

    private static bool TryGetHumanAssistedS02BatchAbilities(
        string cardId, out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        abilities = cardId switch
        {
            "S02-0001" => HumanCard(
                H("after-opponent-tactic", "triggered", "对方 战术的效果结算后，我方 可选择将此军团从战场上回到手牌。"),
                H("enter", "triggered", "登场时 对方下个回合手牌中<主动战术>打出的费用+1。")),
            "S02-0002" => HumanCard(
                H("continuous", "continuous", "进攻无损。"),
                H("after-kill", "triggered", "我方 回合1次 此军团击杀对方军团后，可转为活跃。")),
            "S02-0003" => HumanCard(
                H("continuous", "continuous", "进攻距离+1，远程进攻无损。"),
                H("enter", "triggered", "登场时 可选择战场上1张<反击战术>，将其置入所有者墓地。"),
                H("active", "activated", "主动休整 直到我方下个回合开始前，战场上所有<反击战术>无法发动。")),
            "S02-0004" => HumanCard(
                H("continuous", "continuous", "「位于前排」获得ABILITY 2，且在对方回合此军团兵力+1000。"),
                H("granted", "granted-continuous", "挑衅 对方只可进攻拥有 挑衅 效果的军团，若有多个具有 挑衅效果的军团，则可以选择其中1个进行进攻。")),
            "S02-0005" => HumanCard(
                H("continuous", "continuous", "无法进攻。"),
                H("opponent-attacks-master", "reaction", "对方进攻我方主宰时，可从手牌中休整登场于前排。将本次进攻目标改为此军团。")),
            "S02-0006" => HumanCard(
                H("continuous", "rule", "<信仰狂热者>的效果每回合只可使用1次。"),
                H("discarded", "triggered", "我方回合 此军团从牌库弃置或因效果从手牌弃置时：可无视消耗触发1次我方需要消耗士气的主宰效果，且不计入主宰效果使用次数。")),
            "S02-0007" => HumanCard(
                H("continuous", "continuous", "无法进攻，无法被远程进攻。"),
                H("continuous", "continuous", "「位于前排」获得ABILITY 3，且在对方回合此军团兵力+1000。"),
                H("granted", "granted-continuous", "挑衅 对方只可进攻拥有 挑衅 效果的军团，若有多个具有 挑衅效果的军团，则可以选择其中1个进行进攻。")),
            "S02-0008" => HumanCard(
                H("continuous", "rule", "规则上，本圣物位于圣物区时，所有【通用】卡牌都视为与我方主宰阵营相同。"),
                H("enter", "triggered", "登场时 可弃置1张手牌：查看我方牌库，选择1张【通用】卡牌展示并加入手牌。随后重洗牌库。")),
            "S02-0009" => HumanCard(
                H("play", "spell", "将手牌中最多2张<反击战术>置入战场。若手牌数量不高于4，可抽取1张牌。")),
            "S02-0010" => HumanCard(
                H("play", "spell", "将天灾值增加或减少最多1点。 可消耗3士气：将此战术休整置入士气区，此战术视为1张士气。"),
                H("return-as-morale", "replacement", "「作为士气」当此战术作为士气被返还时，置入所有者墓地。")),
            "S02-0011" => HumanCard(
                H("play", "spell", "击杀对方最多3张原本兵力不高于2000的军团。")),
            "S02-0012" => HumanCard(
                H("play", "spell", "询问对方是否同意公开下1张天灾卡。若同意则执行ABILITY 2；若不同意则执行ABILITY 3。"),
                H("granted", "granted-effect", "公开下1张天灾卡。"),
                H("granted", "granted-effect", "可消耗1士气查看下1张天灾卡。")),
            "S02-0013" => HolyLockAbilities(),
            "S02-0014" => HumanCard(
                H("play", "spell", "我方 手牌不高于4张时，抽取2张牌。")),
            "S02-0015" => HumanCard(
                H("s2-reaction", "reaction", "当对方进行抵挡/支援时：对方需额外弃置1张手牌，否则本次抵挡/支援无效。")),
            "S02-0016" => HumanCard(
                H("s2-reaction", "reaction", "对方军团以手牌以外的方式登场时：选择ABILITY 2或ABILITY 3。"),
                H("granted", "granted-effect", "弃置对方1张手牌。"),
                H("granted", "granted-effect", "使触发此战术的1张军团本回合登场效果无效，且兵力-3000。")),
            "S02-0017" => HumanCard(
                H("s2-reaction", "reaction", "对方 因效果将1张卡牌加入手牌时：随机选择对方1张手牌，将其返回所有者牌库顶部。随后我方抽取1张牌。",
                    Atom(L12AtomKinds.Visibility, "只公开随机手牌位置，不公开对手手牌身份与顺序", "target",
                        ("audience", "opponent-hand-anonymous"), ("selection", "server-randomized")))),
            "S02-0018" => HumanCard(
                H("s2-reaction", "reaction", "对方休整的卡牌因效果转为活跃时，可无效该效果。随后对方弃置1张手牌。")),
            "S02-0101" => HumanCard(
                H("continuous", "continuous", "「位于前排」我方主宰无法被兵力不高于2000的军团进攻。"),
                H("enter", "triggered", "登场时 弃置手牌中1张费用为8的军团：击杀除此军团以外的所有军团。随后返还所有士气，且本回合我方无法因阵营效果以外的方式追加士气。若未能满足登场时效果的发动条件，则展示我方所有手牌。")),
            "S02-0102" => HumanCard(
                H("morale-returned-by-master", "triggered", "我方 回合1次 我方士气因主宰效果返还4张及以上时，可从士气牌库追加1张休整的士气。"),
                H("enter", "triggered", "登场时 可展示牌库顶部1张牌。若其为费用不高于4的<主动战术>，可无需消耗费用将其打出；否则将其返回牌库底部。随后可抽取1张牌。")),
            "S02-0103" => HumanCard(
                H("enter", "triggered", "登场时 本回合我方主宰对对方主宰造成的下一次伤害变为2。"),
                H("attack", "triggered", "进攻时 可展示牌库顶部1张牌。若其为费用不高于5的【天廷】卡牌，此军团本回合兵力+2000；否则将其返回牌库底部。")),
            "S02-0104" => HumanCard(
                H("enter", "triggered", "登场时 可抽取1张牌。"),
                H("active", "activated", "主动休整 返还1士气：重置我方主宰其中1个效果的使用次数。")),
            "S02-0105" => HumanCard(
                H("play", "spell", "击杀对方1张原本兵力不高于3000的军团。 可返还1士气：抽取1张牌。")),
            "S02-0106" => HumanCard(
                H("opponent-attack-or-effect", "reaction", "对方 进攻或发动效果时：展示牌库顶部1张牌。若其为费用不高于3的【天廷】军团，将其弃置。随后选择我方1张军团，本回合增加因此效果弃置军团的费用和兵力，否则将其返回牌库底部。")),
            "S02-01S1" => HumanCard(
                H("morale-returned-by-master", "triggered", "「主宰为杨戬时」我方 回合1次 我方士气因主宰效果返还4张及以上时，<哮天犬·稚>可在前排活跃登场，视为1张兵力2000的【特殊】军团。"),
                H("death", "triggered", "阵亡时 可从士气牌库追加1张休整的士气。")),
            "S02-0201" => HumanCard(
                H("continuous", "rule", "规则上，此军团构筑时不计入卡组数量，不能进入手牌和牌库，游戏开始时置入墓地，此军团以任何形式离场均视为置入所有者墓地。"),
                H("continuous", "continuous", "无法进攻，无法支援。")),
            "S02-0202" => HumanCard(
                H("continuous", "continuous", "我方回合 本回合我方每有1张卡名包含<陵墓>的军团离场时，此军团登场费用-1。",
                    Atom(L12AtomKinds.Visibility, "费用修正属于控制者隐藏信息", "condition",
                        ("audience", "controller-only"))),
                H("death", "triggered", "阵亡时 将墓地1张<陵墓守卫>活跃登场。")),
            "S02-0203" => HumanCard(
                H("continuous", "continuous", "若我方战场不存在<陵墓守卫>，此军团登场费用-1。"),
                H("enter", "triggered", "登场时 可将墓地1张<增殖的甲虫>活跃登场。"),
                H("death", "triggered", "阵亡时 可抽取1张牌。")),
            "S02-0204" => HumanCard(
                H("continuous", "continuous", "进攻距离+1，远程进攻无损。"),
                H("enter", "triggered", "登场时 若我方手牌数量少于对方，可将墓地1张费用为6及以上的【太阳城】军团展示并加入手牌。"),
                H("active", "activated", "主动休整 本回合我方下1张带有天灾等级的【太阳城】军团登场费用-1。")),
            "S02-0205" => HumanCard(
                H("continuous", "continuous", "若<黄金圣甲虫>位于我方圣物区，我方无法从手牌打出其他圣物。"),
                H("enter", "triggered", "登场时 可将墓地1张<增殖的甲虫>活跃登场。"),
                H("active", "activated", "主动休整 可将墓地1张<增殖的甲虫>活跃登场。"),
                H("active", "activated", "我方 回合1次 可弃置1张手牌：选择对方最多2张军团，本回合兵力-1000。")),
            "S02-0206" => HumanCard(
                H("play", "spell", "选择我方前排1张【太阳城】军团，本回合兵力+3000，进攻对方军团时获得ABILITY 2。"),
                H("granted", "granted-continuous", "必中 进攻无法被抵挡/支援。"),
                H("play", "spell", "本回合此军团无法因效果重置为活跃，回合结束时弃置此军团。")),
            "S02-0207" => HumanCard(
                H("play", "spell", "弃置我方战场上最多3张军团：将手牌中1张天灾等级与弃置军团数量相同的【太阳城】军团活跃登场。")),
            "S02-02M1" => HumanCard(
                H("continuous", "continuous", "我方<陵墓守卫>无法进攻主宰。"),
                H("active", "activated", "我方 回合1次 可弃置我方战场上任意数量军团，每弃置1张，本回合我方下1张带有天灾等级的【太阳城】军团登场费用-1。"),
                H("friendly-legion-death", "triggered", "对方 回合1次 我方费用为2及以上的【太阳城】军团阵亡时，可将墓地1张<增殖的甲虫>活跃登场。")),
            "S02-0301" => HumanCard(
                H("enter", "triggered", "登场时 本回合可进攻对方主宰。"),
                H("death", "triggered", "阵亡时 可抽取1张牌，并弃置1张手牌。"),
                H("continuous", "rule", "规则上，当我方主宰为【雷神索尔】，可使用ABILITY 4。"),
                H("active", "activated", "「位于墓地」我方 回合1次 可将墓地3张卡牌自选顺序返回我方牌库底部：将此军团活跃登场。")),
            "S02-0302" => HumanCard(
                H("hand-play", "special-summon", "可将墓地最多8张【阿斯加德】卡牌自选顺序返回我方牌库底部：每返回2张，此军团登场费用-1。"),
                H("continuous", "continuous", "「位于前排」获得ABILITY 3，且无法被后排支援。",
                    Atom(L12AtomKinds.AttackRule, "无法被后排支援", "resolution", ("cannotReceiveBackRowSupport", "true"))),
                H("keyword-definition", "keyword-definition", "挑衅 对方只可进攻拥有挑衅效果的军团，若有多个具有挑衅效果的军团，则可以选择其中1个进行进攻。"),
                H("enter", "triggered", "登场时 我方主宰增加1点血量。")),
            "S02-0303" => HumanCard(
                H("hand-play", "special-summon", "可对我方主宰造成1点伤害：此军团登场费用-1。"),
                H("enter", "triggered", "登场时 可选择我方战场或墓地最多2张非同名的【阿斯加德】军团，触发其阵亡效果。")),
            "S02-0304" => HumanCard(
                H("continuous", "continuous", "进攻距离+1，远程进攻无损。"),
                H("enter", "triggered", "登场时 可弃置我方牌库顶部1张牌。"),
                H("master-damaged-by-effect", "triggered", "我方回合 我方主宰因效果受到伤害时，可将此军团转为休整：我方主宰增加1点血量。随后本回合我方主宰血量无法因军团效果增加。")),
            "S02-0305" => HumanCard(
                H("game-setup", "setup", "规则上，在游戏开始时可将此圣物从牌库置入圣物区，起始手牌数量为4张，且我方回合结束时弃置手牌，直至手牌数量不高于6张。"),
                H("continuous", "continuous", "我方无法从手牌打出圣物。"),
                H("master-damaged", "triggered", "我方 回合1次 我方主宰受到伤害时，可抽取1张牌。"),
                H("master-damaged", "replacement", "对方回合 我方主宰受到的第一次伤害变为2。")),
            "S02-0306" => HumanCard(
                H("continuous", "rule", "<密米尔之泉>每回合只可使用1次。"),
                H("master-effect-damage-threshold", "triggered", "若本回合我方主宰因效果受到累计2点及更多伤害：我方主宰可增加1点血量，抽取1张牌。随后可弃置我方牌库顶部2张牌。")),
            "S02-0307" => HumanCard(
                H("play", "spell", "弃置我方牌库顶部1张牌：选择对方1张军团，本回合兵力-3000。")),
            "S02-03M1" => HumanCard(
                H("game-setup", "setup", "游戏开始时，可将1张<雷神之锤>加入手牌，其视为1张起始手牌。"),
                H("active", "activated", "我方回合 当我方主宰血量不高于3时，可消耗2士气：本回合我方所有【阿斯加德】军团在登场时获得ABILITY 3。以上效果发动后，我方主宰本局游戏无法因任何效果增加血量。"),
                H("granted", "granted-continuous", "冲锋 在登场的回合即可进行进攻。")),
            "S02-0401" => HumanCard(
                H("continuous", "continuous", "我方士气无法因主宰效果转为活跃。"),
                H("enter", "triggered", "登场时 可查看我方牌库，选择1张兵力不高于5000的【高天原】军团展示并加入手牌。随后重洗牌库，选择将手牌中1张<真田幸村>活跃登场，并将1张士气转为活跃。")),
            "S02-0402" => HumanCard(
                H("enter", "triggered", "登场时 可弃置1张手牌：将我方1张休整的【高天原】军团转为活跃。"),
                H("death", "triggered", "阵亡时 可抽取1张牌。")),
            "S02-0403" => HumanCard(
                H("enter", "triggered", "登场时 若<草薙剑>位于我方前排，此军团获得ABILITY 2，本回合兵力+1000。"),
                H("granted", "granted-continuous", "冲锋 在登场的回合即可进行进攻。"),
                H("attack", "triggered", "进攻时 展示牌库顶部1张牌，若其为费用不高于3的【高天原】卡牌，可无需消耗费用将其打出；否则加入手牌。")),
            "S02-0404" => HumanCard(
                H("enter", "triggered", "登场时 可查看我方牌库，选择1张【高天原】的【骑兵】军团展示并加入手牌。随后重洗牌库。"),
                H("active", "activated", "主动休整 选择ABILITY 3或ABILITY 4。"),
                H("granted", "granted-effect", "选择战场上1张军团，进行1次骑兵位移。"),
                H("granted", "granted-effect", "选择我方1张本回合位移过的军团，本回合获得ABILITY 5。"),
                H("granted", "granted-continuous", "免死 仅1次，即将阵亡时，将兵力在本回合变为1000作为代替。")),
            "S02-0405" => HumanCard(
                H("play", "spell", "展示我方牌库顶部5张牌，选择其中1张【圣物】和1张<上杉谦信>加入手牌，其余牌自选顺序返回我方牌库底部。"),
                H("play", "spell", "本回合我方下1张<上杉谦信>登场费用-2，并获得ABILITY 3。"),
                H("granted", "granted-continuous", "冲锋 在登场的回合即可进行进攻。")),
            "S02-0406" => HumanCard(
                H("play", "spell", "选择ABILITY 2、ABILITY 3或ABILITY 4。"),
                H("granted", "granted-effect", "选择对方1排所有军团本回合费用-2。"),
                H("granted", "granted-continuous", "本回合我方位于前排的所有【高天原】军团进攻时兵力+1000。"),
                H("granted", "granted-effect", "本回合我方所有活跃的【高天原】军团可免费进行1格位移。")),
            "S02-04M1" => HumanCard(
                H("friendly-legion-moves", "triggered", "我方 回合1次 我方军团位移时，可消耗1士气：选择另外1张军团进行1格位移，并使其本回合费用-1。"),
                H("friendly-back-to-front", "triggered", "我方回合 当我方1张【高天原】军团从后排位移至前排时，此军团本回合进攻时兵力+1000。"),
                H("friendly-front-to-back", "triggered", "我方回合 当我方1张【高天原】军团从前排位移至后排时，将我方1张士气转为活跃。")),
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static IReadOnlyList<L12StructuredAbilityTemplate> HumanCard(
        params L12StructuredAbilityTemplate[] abilities)
    {
        var claimedRoutes = new HashSet<string>(StringComparer.Ordinal);
        return abilities.Select(ability =>
        {
            var ownsRoute = claimedRoutes.Add(ability.Trigger);
            return ability with { RuntimeRouteOwner = ownsRoute };
        }).ToArray();
    }

    private static L12StructuredAbilityTemplate H(string trigger, string executionModel, string text,
        params L12StructuredAtomTemplate[] additionalAtoms)
    {
        var atoms = InferHumanAtoms(trigger, executionModel, text).Concat(additionalAtoms).ToArray();
        return new(trigger, executionModel, text, atoms, "human-assisted", HumanS02ReviewSource);
    }

    private static IReadOnlyList<L12StructuredAbilityTemplate> HolyLockAbilities() => HumanCard(
        new("play", "spell", "叠放至对方圣物区的1张【圣物】之上，对方无法使用此【圣物】，直到此战术被弃置。",
        [
            Atom(L12AtomKinds.SelectTarget, "选择对方圣物区 1 张圣物", "target",
                ("zone", "opponent.artifact"), ("filter", "card-type=artifact"), ("min", "1"), ("max", "1")),
            Atom(L12AtomKinds.MoveZone, "叠放至所选圣物上", "resolution",
                ("from", "stack"), ("to", "declared-target.attached"), ("role", "status-below")),
            Atom(L12AtomKinds.SetState, "被叠放圣物无法使用", "resolution",
                ("key", "host.ability-disabled"), ("value", "true")),
            Atom(L12AtomKinds.Duration, "持续至此战术被弃置", "duration", ("duration", "while-attached")),
        ], "human-assisted", HumanS02ReviewSource),
        new("host-leaves-artifact", "triggered", "被叠放的【圣物】以任何形式离开圣物区时，弃置此战术。",
        [
            Atom(L12AtomKinds.Condition, "宿主圣物离开圣物区", "condition",
                ("expression", "source.zone=attached;host.left-artifact-zone=true")),
            Atom(L12AtomKinds.MoveZone, "将此战术置入所有者墓地", "resolution",
                ("from", "attached"), ("to", "owner.grave")),
        ], "human-assisted", HumanS02ReviewSource),
        new("active-while-attached", "activated-by-opponent", "「叠放时」对方 可消耗3士气：弃置此战术。",
        [
            Atom(L12AtomKinds.Condition, "此战术当前处于叠放状态", "condition",
                ("expression", "source.zone=attached")),
            Atom(L12AtomKinds.Optional, "对方可发动", "condition", ("actor", "opponent")),
            Atom(L12AtomKinds.PayMorale, "对方消耗 3 士气", "cost",
                ("actor", "opponent"), ("amount", "3"), ("selection", "field-resource-click")),
            Atom(L12AtomKinds.MoveZone, "将此战术置入所有者墓地", "resolution",
                ("from", "attached"), ("to", "owner.grave")),
        ], "human-assisted", HumanS02ReviewSource));

    private static L12StructuredAtomTemplate Atom(string kind, string label, string stage,
        params (string Key, string Value)[] parameters)
        => new(kind, label, stage, parameters.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.Ordinal));

    private static IReadOnlyList<L12StructuredAtomTemplate> InferHumanAtoms(
        string trigger, string executionModel, string text)
    {
        var atoms = new List<L12StructuredAtomTemplate>();
        void Add(string kind, string label, string stage, params (string Key, string Value)[] values)
        {
            if (atoms.Any(atom => atom.Kind == kind && atom.Label == label && atom.Stage == stage)) return;
            atoms.Add(Atom(kind, label, stage, values));
        }

        var condition = HumanConditionExpression(text);
        if (condition is not null) Add(L12AtomKinds.Condition, "检查已确认的发动与状态条件", "condition",
            ("expression", condition));
        if (text.Contains('可')) Add(L12AtomKinds.Optional, "可选择发动或执行", "condition");

        var modeMatches = Regex.Matches(text, @"ABILITY\s*(\d+)").Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (text.Contains("选择ABILITY", StringComparison.Ordinal) && modeMatches.Length > 0)
            Add(L12AtomKinds.SelectMode, "选择一项已确认能力", "target",
                ("options", string.Join('|', modeMatches.Select(value => $"ability:{value}"))));

        AddHumanTargets(text, Add);

        if (text.Contains("主动休整", StringComparison.Ordinal))
            Add(L12AtomKinds.RestSource, "将来源转为休整", "cost");
        foreach (Match match in Regex.Matches(text, @"消耗(\d+)士气"))
            Add(L12AtomKinds.PayMorale, $"消耗 {match.Groups[1].Value} 士气", "cost",
                ("amount", match.Groups[1].Value), ("selection", "auto-or-field-resource-click"));
        foreach (Match match in Regex.Matches(text, @"返还(\d+)士气"))
            Add(L12AtomKinds.ReturnMorale, $"返还 {match.Groups[1].Value} 士气", "cost",
                ("amount", match.Groups[1].Value), ("selection", "field-resource-click"));
        if (text.Contains("弃置1张手牌：", StringComparison.Ordinal)
            || text.Contains("弃置手牌中1张", StringComparison.Ordinal))
            Add(L12AtomKinds.Discard, "弃置 1 张手牌", "cost", ("amount", "1"), ("zone", "controller.hand"));
        else if (text.Contains("弃置1张手牌", StringComparison.Ordinal))
            Add(L12AtomKinds.Discard, "弃置 1 张手牌", "resolution", ("amount", "1"), ("zone", "controller.hand"));

        foreach (Match match in Regex.Matches(text, @"抽取(\d+)张牌"))
            Add(L12AtomKinds.Draw, $"抽取 {match.Groups[1].Value} 张牌", "resolution",
                ("amount", match.Groups[1].Value));
        if (text.Contains("加入手牌", StringComparison.Ordinal))
            Add(L12AtomKinds.MoveZone, "将所选或展示的卡牌加入手牌", "resolution",
                ("to", "controller.hand"), ("event", "add-card-to-hand-by-effect"));
        if (text.Contains("返回", StringComparison.Ordinal) && text.Contains("牌库", StringComparison.Ordinal))
            Add(L12AtomKinds.MoveZone, "按文本将卡牌返回牌库", "resolution", ("to", "owner.library"));
        if (text.Contains("活跃登场", StringComparison.Ordinal) || text.Contains("休整登场", StringComparison.Ordinal))
            Add(L12AtomKinds.MoveZone, "将所选军团按指定状态登场", "resolution", ("to", "field"));
        if (text.Contains("置入所有者墓地", StringComparison.Ordinal) || text.Contains("置入墓地", StringComparison.Ordinal))
            Add(L12AtomKinds.MoveZone, "置入所有者墓地", "resolution", ("to", "owner.grave"));
        if (text.Contains("重洗牌库", StringComparison.Ordinal))
            Add(L12AtomKinds.Shuffle, "重洗牌库", "resolution", ("zone", "controller.library"));
        if (text.Contains("展示", StringComparison.Ordinal) || text.Contains("公开", StringComparison.Ordinal)
            || text.Contains("查看", StringComparison.Ordinal))
            Add(L12AtomKinds.Visibility, "按文本公开、展示或查看信息", "resolution",
                ("audience", text.Contains("查看", StringComparison.Ordinal) ? "controller" : "public"));

        AddHumanCombatAtoms(text, Add);
        AddHumanStateAtoms(text, Add);

        if (text.Contains("转为活跃", StringComparison.Ordinal))
            Add(L12AtomKinds.Ready, "将合法对象转为活跃", "resolution");
        if (text.Contains("转为休整", StringComparison.Ordinal))
            Add(L12AtomKinds.Rest, "将合法对象转为休整", "resolution");
        if (text.Contains("位移", StringComparison.Ordinal))
            Add(L12AtomKinds.Move, text.Contains("骑兵位移", StringComparison.Ordinal) ? "进行骑兵位移" : "进行效果位移",
                "resolution", ("operation", text.Contains("骑兵位移", StringComparison.Ordinal) ? "cavalry-move" : "effect-move"));

        if (text.Contains("本回合", StringComparison.Ordinal))
            Add(L12AtomKinds.Duration, "持续至本回合结束", "duration", ("duration", "this-turn"));
        else if (text.Contains("下个回合开始前", StringComparison.Ordinal))
            Add(L12AtomKinds.Duration, "持续至我方下个回合开始前", "duration", ("duration", "until-next-controller-turn"));
        else if (text.Contains("下个回合", StringComparison.Ordinal))
            Add(L12AtomKinds.Duration, "持续至文本指定的下个回合", "duration", ("duration", "next-turn"));
        if (text.Contains("回合1次", StringComparison.Ordinal) || text.Contains("每回合只可使用1次", StringComparison.Ordinal))
            Add(L12AtomKinds.Duration, "每个来源实例每回合 1 次", "duration",
                ("duration", "once-per-turn"), ("scope", "source-instance"));

        if (atoms.Count == 0)
            Add(L12AtomKinds.Special, "按人工确认的完整能力语义执行", "resolution",
                ("semantic", trigger), ("executionModel", executionModel));
        return atoms;
    }

    private delegate void HumanAtomAdder(string kind, string label, string stage,
        params (string Key, string Value)[] values);

    private static void AddHumanTargets(string text, HumanAtomAdder add)
    {
        if (!text.Contains("选择", StringComparison.Ordinal)) return;
        var zone = text.Contains("墓地", StringComparison.Ordinal) ? "grave"
            : text.Contains("手牌", StringComparison.Ordinal) ? "hand"
            : text.Contains("牌库", StringComparison.Ordinal) ? "library"
            : text.Contains("圣物区", StringComparison.Ordinal) ? "artifact"
            : "field";
        var side = text.Contains("对方", StringComparison.Ordinal) ? "opponent" : "controller";
        var max = Regex.Match(text, @"最多(\d+)张");
        var exact = Regex.Match(text, @"选择.{0,16}?(\d+)张");
        var upper = max.Success ? max.Groups[1].Value : exact.Success ? exact.Groups[1].Value : "1";
        var lower = max.Success ? "0" : "1";
        add(L12AtomKinds.SelectTarget, "选择文本规定的合法对象", "target",
            ("zone", $"{side}.{zone}"), ("min", lower), ("max", upper), ("declaration", "before-cost"));
    }

    private static void AddHumanCombatAtoms(string text, HumanAtomAdder add)
    {
        if (text.Contains("进攻距离+1", StringComparison.Ordinal) || text.Contains("远程进攻无损", StringComparison.Ordinal))
            add(L12AtomKinds.AttackRule, "获得完整远程进攻规则", "resolution",
                ("rangeBonus", "1"), ("rangedNoLoss", "true"));
        if (text.Contains("进攻无损", StringComparison.Ordinal) && !text.Contains("远程进攻无损", StringComparison.Ordinal))
            add(L12AtomKinds.AttackRule, "进攻无损", "resolution", ("attackNoLoss", "true"));
        if (text.Contains("无法进攻", StringComparison.Ordinal))
            add(L12AtomKinds.AttackRule, "无法进攻", "resolution", ("cannotAttack", "true"));
        if (text.Contains("无法被远程进攻", StringComparison.Ordinal))
            add(L12AtomKinds.AttackRule, "无法被远程进攻", "resolution", ("cannotBeRanged", "true"));
        if (text.Contains("主宰无法被兵力不高于2000的军团进攻", StringComparison.Ordinal))
            add(L12AtomKinds.AttackRule, "保护主宰免受兵力不高于 2000 的军团进攻", "resolution",
                ("protectMasterFromTroopsAtMost", "2000"));
        foreach (var keyword in new[] { "挑衅", "冲锋", "必中", "免死" })
            if (text.Contains(keyword, StringComparison.Ordinal))
                add(L12AtomKinds.Keyword, $"获得{keyword}", "resolution", ("keywordRef", KeywordRef(keyword)));
    }

    private static void AddHumanStateAtoms(string text, HumanAtomAdder add)
    {
        foreach (Match match in Regex.Matches(text, @"兵力([+-])(\d+)"))
            add(L12AtomKinds.ModifyTroops, $"兵力{match.Groups[1].Value}{match.Groups[2].Value}", "resolution",
                ("operation", "add"), ("value", match.Groups[1].Value == "-" ? $"-{match.Groups[2].Value}" : match.Groups[2].Value));
        foreach (Match match in Regex.Matches(text, @"兵力(?:在本回合)?变为(\d+)"))
            add(L12AtomKinds.ModifyTroops, $"兵力设定为 {match.Groups[1].Value}", "resolution",
                ("operation", "set"), ("value", match.Groups[1].Value));
        foreach (Match match in Regex.Matches(text, @"登场费用([+-])(\d+)"))
            add(L12AtomKinds.SetState, $"登场费用{match.Groups[1].Value}{match.Groups[2].Value}", "resolution",
                ("key", "source.derived-cost"), ("operation", "add"),
                ("value", match.Groups[1].Value == "-" ? $"-{match.Groups[2].Value}" : match.Groups[2].Value));
        foreach (Match match in Regex.Matches(text, @"获得ABILITY\s*(\d+)"))
            add(L12AtomKinds.SetState, $"获得 ABILITY {match.Groups[1].Value}", "resolution",
                ("abilityRef", $"ability:{match.Groups[1].Value}"));
        if (text.Contains("无效", StringComparison.Ordinal))
            add(L12AtomKinds.SetState, "按文本无效对应效果", "resolution", ("key", "effect.invalidated"), ("value", "true"));
        if (text.Contains("天灾值", StringComparison.Ordinal))
            add(L12AtomKinds.Special, "调整天灾值", "resolution", ("operation", "adjust-disaster"));
        if (text.Contains("士气牌库追加", StringComparison.Ordinal))
            add(L12AtomKinds.AddMorale, "从士气牌库追加士气", "resolution");
    }

    private static string? HumanConditionExpression(string text)
    {
        var parts = new List<string>();
        if (text.Contains("位于前排", StringComparison.Ordinal)) parts.Add("source.row=front");
        if (text.Contains("位于后排", StringComparison.Ordinal)) parts.Add("source.row=back");
        if (text.Contains("位于墓地", StringComparison.Ordinal)) parts.Add("source.zone=grave");
        if (text.Contains("作为士气", StringComparison.Ordinal)) parts.Add("source.identity=morale");
        if (text.Contains("主宰为杨戬", StringComparison.Ordinal)) parts.Add("controller.master=S01-01M1");
        if (text.Contains("主宰为【雷神索尔】", StringComparison.Ordinal)) parts.Add("controller.master=S02-03M1");
        if (text.Contains("我方回合", StringComparison.Ordinal)) parts.Add("controller.turn");
        if (text.Contains("对方回合", StringComparison.Ordinal)) parts.Add("opponent.turn");
        if (text.Contains("手牌不高于4张", StringComparison.Ordinal)) parts.Add("controller.hand<=4");
        if (text.Contains("手牌数量不高于4", StringComparison.Ordinal)) parts.Add("controller.hand<=4");
        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    private static string KeywordRef(string keyword) => keyword switch
    {
        "挑衅" => "taunt",
        "冲锋" => "charge",
        "必中" => "must-hit",
        "免死" => "death-immunity",
        _ => keyword,
    };
}
