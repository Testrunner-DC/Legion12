namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> S1FactionEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0201", "S01-0202", "S01-0204", "S01-0205", "S01-0207", "S01-0208", "S01-0209", "S01-0210", "S01-0211",
        "S01-0215", "S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220",
        "S01-0301", "S01-0303", "S01-0304", "S01-0309", "S01-0313", "S01-0315", "S01-0316", "S01-0317",
    };

    private static readonly HashSet<string> S1FactionTacticCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0221", "S01-0222", "S01-0318", "S01-0319",
    };

    private static readonly HashSet<string> S1FactionDeathCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0201", "S01-0204", "S01-0206", "S01-0207", "S01-0209", "S01-0210",
        "S01-0301", "S01-0302", "S01-0303", "S01-0304", "S01-0305", "S01-0306", "S01-0307", "S01-0308", "S01-0309", "S01-0313",
    };

    private static readonly HashSet<string> S1FactionAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0201", "S01-0203", "S01-0206", "S01-0208",
        "S01-0301", "S01-0302", "S01-0306", "S01-0310", "S01-0311",
    };

    private static bool HasS1FactionImmediateEffect(string cardId, string trigger)
        => trigger == "enter" ? S1FactionEnterCards.Contains(cardId) : S1FactionTacticCards.Contains(cardId);
    private static bool IsS1FactionDeathCard(string cardId) => S1FactionDeathCards.Contains(cardId);
    private static bool IsS1FactionAfterAttackCard(string cardId) => cardId == "S01-0311";

    private static List<L12AbilityView> GetS1FactionAbilities(string cardId) => cardId switch
    {
        "S01-0214" => [new("cleopatraGuard", "主动休整 可消耗1士气：将墓地1张<陵墓守卫>活跃登场。")],
        "S01-0215" => [new("ankhReady", "弃置1张手牌：选择我方1张休整的<陵墓守卫>转为活跃。"), new("ankhDraw", "将我方1张<陵墓守卫>转为休整：抽取1张牌。")],
        "S01-02C1" => [new("sunGuard", "我方 回合1次 可消耗2士气：将1张<陵墓守卫>从我方墓地活跃登场。"), new("sunDraw", "我方 回合1次 若我方手牌不高于3张，可消耗1士气：抽取1张牌。")],
        "S01-03C1" => [new("asgardDraw", "我方 回合1次 可消耗2士气：抽取1张牌。若我方主宰血量不高于5，可额外消耗1士气：我方主宰增加1点血量。")],
        "S01-0307" => [new("alvidaSummon", "我方回合 可弃置此军团：对我方主宰造成1点伤害，将手牌中1张天灾等级2的军团活跃登场。")],
        "S01-0314" => [new("olgaDebuff", "我方回合 可弃置此军团：选择对方前排1张军团，本回合兵力-2000。")],
        "S01-0317" => [new("gramDamage", "主动休整 将墓地4张【阿斯加德】军团自选顺序返回我方牌库底部：对对方主宰造成1点非致命伤害。"), new("gramReady", "可消耗2士气：将此圣物转为活跃。")],
        "S01-01D1" => [new("palaceReward", "我方 回合1次 若本回合返还的士气高于1张，可从士气牌库追加2张休整的士气，随后抽取1张牌。"), new("palaceExchange", "主动休整 击杀对方1张军团，我方需返还此军团相应费用的士气。随后选择墓地1张费用不高于本次返还士气数量的【天廷】军团，将其活跃登场。")],
        "S01-01M2" => [new("mengpoSilence", "返还1士气：选择对方1张军团，本回合失去「阵亡时」效果。若我方手牌不高于5张，可抽取1张牌。"), new("mengpoMorale", "若我方士气少于对方，弃置1张手牌：从士气牌库追加1张休整的士气。")],
        "S01-02D1" => [new("sunTopThree", "我方 回合1次 可消耗2士气：公开牌库顶部3张牌，选择其中1张加入手牌，其余卡牌自选顺序返回牌库底部。随后可选择墓地1张【太阳城】卡牌加入手牌。"), new("sunBottomEnemy", "我方 回合1次 可消耗2士气：选择对方1张兵力不高于4000的军团，将其返回所有者牌库底部。")],
        "S01-02M1" =>
        [
            new("isisCanopic", "我方回合 可弃置我方战场3张<陵墓守卫>：将墓地1张名字包含<卡诺匹斯>的圣物置入圣物区。以上操作完成后，可选择抽取1张牌，或主宰增加1点血量。"),
            new("isisVictory", "圣物区存在5张名字包含<卡诺匹斯>的圣物时：可使墓地的<复苏的奥西里斯>替换<伊西斯>登场，并获得游戏胜利。"),
        ],
        "S01-02M2" => [new("isisVictory", "圣物区存在5张名字包含<卡诺匹斯>的圣物时：可替换<伊西斯>登场，并获得游戏胜利。")],
        "S01-02M3" =>
        [
            new("medjedDebuff", "我方 回合1次 可消耗1士气：选择对方1张军团本回合兵力-1000。若额外休整我方1张<陵墓守卫>，则选择的军团本回合兵力-3000作为代替。"),
            new("medjedDamageResponse", "对方 回合1次 我方主宰因对方进攻或效果受到伤害时：可将我方墓地1张<陵墓守卫>活跃登场。", false, "仅在触发时点发动", true),
        ],
        "S01-03D1" => [new("valhallaDiscount", "我方 回合1次 可对我方主宰造成1点伤害：手牌所有【阿斯加德】军团本回合费用-1。"), new("valhallaRecover", "我方 回合1次 可消耗2士气：弃置牌库顶部2张牌，随后可选择墓地1张【阿斯加德】卡牌加入手牌。"), new("valhallaKill", "主动休整 将墓地2张卡牌返回我方牌库底部：击杀对方1张兵力不高于5000的军团和1张兵力不高于1000的军团。")],
        "S01-03M1" => [new("valkyrieRecover", "我方 回合1次 可消耗1士气并对我方主宰造成1点伤害：选择墓地2张牌，其中1张返回牌库底部，另1张加入手牌。")],
        "S01-03M2" => [new("lokiCycle", "消耗1士气：抽取1张牌，并弃置1张手牌。"), new("lokiHeal", "消耗1士气：将墓地2张卡牌返回牌库底部，我方主宰增加1点血量。")],
        "S01-04D1" => [new("yomiDiscount", "我方 回合1次 本回合从手牌打出的下1张【高天原】军团登场费用-2。"), new("yomiSweep", "我方 回合1次 可消耗2士气：抽取1张牌。对方所有军团在本回合费用-1。随后可击杀对方1张费用不高于3的军团和1张费用不高于1的军团。"), new("yomiRecover", "主动休整 选择墓地1张【高天原】卡牌加入手牌。")],
        "S01-04M1" => [new("amaterasuKill", "我方 回合1次 可消耗1士气：选择对方1张军团，本回合费用-1。随后击杀对方1张费用为0的军团。"), new("amaterasuReady", "我方 回合1次 可弃置1张手牌：将我方最多2张士气转为活跃。")],
        _ => [],
    };

    private bool TryResolveS1FactionEnter(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0201":
                PromptEnemyByTroops(item, "thutmose-kill", "图特摩斯三世：击杀对方1张兵力不高于5000的军团", 5000, false); return true;
            case "S01-0202":
            {
                var choices = PublicLegions(player).Where(target => target.InstanceId != card.InstanceId && target.Faction == "taiyangcheng")
                    .Select(target => target.InstanceId).ToList();
                if (choices.Count == 0) { FinishStackItem(item); return true; }
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-targets", "拉美西斯二世：选择最多3张其他【太阳城】军团，依次发动其登场时效果", choices,
                    1, Math.Min(3, choices.Count - 1), "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "ramses-repeat" });
                return true;
            }
            case "S01-0204":
            {
                var guards = player.Graveyard.Where(candidate => candidate.CardId == "S01-0212").ToArray();
                foreach (var guard in guards) { player.Graveyard.Remove(guard); card.AttachedCards.Add(guard); }
                AddEvent("effect", item.Controller, $"陵墓构造体叠放{guards.Length}张陵墓守卫，兵力+{guards.Length * 1000}", card);
                RecalculateContinuousTroops();
                FinishStackItem(item); return true;
            }
            case "S01-0205":
            {
                var guards = PublicLegions(player).Where(target => target.CardId == "S01-0212").Select(target => target.InstanceId).ToList();
                guards.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "霍列姆赫布：可弃置我方1张陵墓守卫，获得冲锋", guards, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "horemheb-charge" });
                return true;
            }
            case "S01-0207":
            {
                if (PublicLegions(player).Count() >= PublicLegions(State.Players[1 - item.Controller]).Count()) { FinishStackItem(item); return true; }
                var guards = player.Graveyard.Where(candidate => candidate.CardId == "S01-0212").Select(candidate => candidate.InstanceId).Take(2).ToArray();
                BeginQueuedSummons(item, guards, tapped: false, "图坦卡蒙：选择陵墓守卫活跃登场的位置"); return true;
            }
            case "S01-0208":
                BeginQueuedSummons(item, player.Graveyard.Where(candidate => candidate.CardId == "S01-0212").Take(1).Select(candidate => candidate.InstanceId), tapped: true,
                    "阿伊：选择陵墓守卫休整登场的位置"); return true;
            case "S01-0209":
                if (State.Players[1 - item.Controller].Hand.Count >= 6)
                    PromptDiscard(item, 1 - item.Controller, 1, "纳芙蒂蒂：对方弃置1张手牌", "nefertiti-discard");
                else FinishStackItem(item);
                return true;
            case "S01-0210":
            {
                var guards = PublicLegions(player).Where(target => target.CardId == "S01-0212" && target.Tapped).Select(target => target.InstanceId).ToArray();
                if (guards.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target", "尼托克丽丝：选择我方1张陵墓守卫转为活跃", guards, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "nitocris-ready" });
                return true;
            }
            case "S01-0211":
            {
                var previousId = player.LastActiveTacticCardId;
                if (string.IsNullOrEmpty(previousId) || !_catalog.Cards.ContainsKey(previousId)) { FinishStackItem(item); return true; }
                var copy = CreateCard(previousId, $"repeat-{++State.StackSequence}"); player.Resolving.Add(copy);
                FinishStackItem(item); PushEffect(item.Controller, copy, "play", "托勒密十三世再次发动的主动战术效果"); return true;
            }
            case "S01-0215":
                PromptOwnLegion(item, "ankh-enter", "安卡神碑：选择我方1张陵墓守卫，本回合兵力+2000", target => target.CardId == "S01-0212", false); return true;
            case "S01-0216":
            {
                var choices = player.Library.Where(candidate => candidate.Name.Contains("卡诺匹斯罐", StringComparison.Ordinal)).Select(candidate => candidate.InstanceId).ToArray();
                if (choices.Length == 0) { HealMaster(item.Controller, 1, "卡诺匹斯箱"); DiscardRelic(player, card); FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "search", "卡诺匹斯箱：选择牌库中1张卡诺匹斯罐加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "canopic-search" });
                return true;
            }
            case "S01-0217": PromptOwnLegion(item, "canopic-one", "卡诺匹斯罐一：选择我方1张【太阳城】军团，兵力+2000并获得强攻", target => target.Faction == "taiyangcheng", false); return true;
            case "S01-0218": player.FreeTacticCount++; DiscardRelic(player, card); FinishStackItem(item); return true;
            case "S01-0219": player.TemporaryMorale += 2; DiscardRelic(player, card); FinishStackItem(item); return true;
            case "S01-0220":
            {
                var choices = PublicLegions(player).Where(target => target.Faction == "taiyangcheng").Select(target => target.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-targets", "卡诺匹斯罐四：选择我方最多2张【太阳城】军团获得免死", choices, 1, Math.Min(2, Math.Max(1, choices.Count - 1)),
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "canopic-four" });
                return true;
            }
            case "S01-0301":
                Mill(player, 2, "贝奥武夫"); FinishStackItem(item); return true;
            case "S01-0303":
                if (player.Hp <= 7) card.HasCharge = true;
                FinishStackItem(item); return true;
            case "S01-0304":
                if (State.Players[1 - item.Controller].Hp > player.Hp) DamageMaster(1 - item.Controller, 1, "无情者哈拉尔登场效果");
                FinishStackItem(item); return true;
            case "S01-0309":
            {
                var choices = player.Hand.Concat(player.Graveyard).Where(candidate => candidate.CardId == "S01-0310").Select(candidate => candidate.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "布伦希尔德：可令主宰受到1点伤害，将手牌或墓地1张齐格鲁德活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "brynhild-sigurd" });
                return true;
            }
            case "S01-0313":
                CreatePrompt(item.Controller, "optional", "神箭奥德尔：是否令我方主宰受到1点伤害并抽1张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "oddr-draw" }); return true;
            case "S01-0315": BeginFactionTopSearch(item, 3, "asgard", "S01-0315", "ivar-search"); return true;
            case "S01-0316":
                CreatePrompt(item.Controller, "optional", "夺命诗人埃吉尔：是否令主宰受到1点伤害并弃置牌库顶部2张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "egil-pay" }); return true;
            case "S01-0317":
                Mill(player, 2, "神剑格拉墨"); PromptEnemyByTroops(item, "gram-bottom", "神剑格拉墨：选择对方1张兵力不高于3000的军团返回牌库底部", 3000, true); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0221":
                CreatePrompt(item.Controller, "option", "杜阿特之门：选择击杀军团，或回收太阳城卡牌", ["kill", "recover"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "duat-mode", ["choiceMode"] = "instant",
                        ["kill"] = "击杀对方1张兵力不高于5000的军团。",
                        ["recover"] = "选择墓地最多1张〈杜阿特之门〉以外的【太阳城】卡牌加入手牌。",
                    }); return true;
            case "S01-0222": BeginPharaohFestival(item); return true;
            case "S01-0318":
            {
                var choices = player.Graveyard.Where(candidate => candidate.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, candidate, "asgard") && candidate.CurrentCost <= 5)
                    .Select(candidate => candidate.InstanceId).ToArray();
                if (choices.Length == 0 || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                if (player.Hp > 5) DamageMaster(item.Controller, 1, "女武神的召唤");
                CreatePrompt(item.Controller, "card", "女武神的召唤：选择墓地1张费用不高于5的【阿斯加德】军团活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "valkyrie-card" }); return true;
            }
            case "S01-0319":
                if (player.Graveyard.Count < 4) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "order", "猎杀时刻：选择墓地4张牌，依选择顺序返回牌库底部", player.Graveyard.Select(card => card.InstanceId), 4, 4,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "hunt-return" }); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionAttack(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0201": ApplySunKingAttack(item); return true;
            case "S01-0203":
            {
                var choices = PublicLegions(player).Where(target => target.InstanceId != card.InstanceId).Select(target => target.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "美尼斯：可弃置我方战场1张军团，自身兵力+2000并获得强攻", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "menes-sacrifice" }); return true;
            }
            case "S01-0206": PromptOwnLegion(item, "saladin-move", "萨拉丁：可选择我方1张陵墓守卫位移", target => target.CardId == "S01-0212", true); return true;
            case "S01-0208":
                if (ActiveResourceCount(player) < 1) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "阿伊：是否消耗1士气，使我方前排1张低兵力军团本回合兵力+2000？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "ay-pay" }); return true;
            case "S01-0301":
                CreatePrompt(item.Controller, "optional", "贝奥武夫：是否令我方主宰受到1点伤害，自身兵力+2000？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "beowulf-buff" }); return true;
            case "S01-0302": if (player.Hp <= 6) card.HasStrongAttack = true; FinishStackItem(item); return true;
            case "S01-0306":
            {
                var choices = player.Graveyard.Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "奥拉夫二世：可将墓地1张牌置入牌库底部，获得强攻", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "olaf-strong" }); return true;
            }
            case "S01-0310": if (player.Relic?.CardId == "S01-0317" || player.ExtraRelics.Any(relic => relic.CardId == "S01-0317")) card.Troops += 1000; FinishStackItem(item); return true;
            case "S01-0311":
                if (player.Graveyard.Count < 2) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "古斯塔夫一世：是否将墓地2张牌返回牌库底部，使自身本回合兵力+2000？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "gustav-attack-choice" }); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionDeath(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0201": ApplySunKingAttack(item); return true;
            case "S01-0204":
                var attachedIds = card.LastKnownAttachedCardIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                BeginQueuedSummons(item, player.Graveyard.Where(candidate => candidate.CardId == "S01-0212" && attachedIds.Contains(candidate.InstanceId)).Select(candidate => candidate.InstanceId), tapped: true,
                    "陵墓构造体：选择陵墓守卫休整登场的位置"); return true;
            case "S01-0206": PromptOwnLegion(item, "saladin-move", "萨拉丁阵亡：可选择我方1张陵墓守卫位移", target => target.CardId == "S01-0212", true); return true;
            case "S01-0207":
            {
                var choices = player.Graveyard.Where(candidate => CanEnterHandOrLibrary(candidate) && candidate.CardId != "S01-0207" && candidate.Faction == "taiyangcheng" && candidate.CurrentCost <= 4)
                    .Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "图坦卡蒙阵亡：可将墓地1张费用不高于4的其他【太阳城】卡牌放回牌库顶部", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "tutankhamun-top" }); return true;
            }
            case "S01-0209": if (player.Hand.Count < State.Players[1 - item.Controller].Hand.Count) { DamageMaster(1 - item.Controller, 1, "纳芙蒂蒂阵亡效果"); HealMaster(item.Controller, 1, "纳芙蒂蒂阵亡效果", legionEffect: true); } FinishStackItem(item); return true;
            case "S01-0210":
            {
                var choices = player.Graveyard.Where(candidate => candidate.CardType == "legion" && candidate.Faction == "taiyangcheng" && candidate.CurrentCost <= 2)
                    .Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "尼托克丽丝阵亡：选择墓地1张费用不高于2的【太阳城】军团活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "nitocris-summon" }); return true;
            }
            case "S01-0302": HealMaster(item.Controller, 1, "金发哈拉尔阵亡效果", legionEffect: true); FinishStackItem(item); return true;
            case "S01-0303": CreatePrompt(item.Controller, "optional", "传奇的拉格纳阵亡：是否抽取1张并弃置1张？", ["yes", "no"], 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "death-cycle-one" }); return true;
            case "S01-0304": PromptEnemyByTroops(item, "harald-kill", "无情者哈拉尔阵亡：击杀对方1张兵力不高于2000的军团", 2000, false); return true;
            case "S01-0305":
                if (player.Graveyard.Count < 4 || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "勇士比约恩阵亡：是否令主宰受到1点伤害并将墓地4张牌返回牌库底部，使其休整登场？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "bjorn-revive-choice" }); return true;
            case "S01-0306": CreatePrompt(item.Controller, "optional", "奥拉夫二世阵亡：是否抽2张牌并弃置1张？", ["yes", "no"], 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "death-cycle-two" }); return true;
            case "S01-0307": RecoverAsgard(item, 3, legionOnly: false); return true;
            case "S01-0308": SummonAsgardFromGrave(item, 3); return true;
            case "S01-0313":
            {
                var choices = PublicLegions(State.Players[1 - item.Controller]).Where(target => !target.Tapped).Select(target => target.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "神箭奥德尔阵亡：可将对方1张活跃军团转为休整", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "oddr-tap" }); return true;
            }
            default: return false;
        }
    }

    private bool TryResolveS1FactionAfterAttack(L12StackItem item, L12CardInstance card)
    {
        if (card.CardId != "S01-0311") return false;
        var player = State.Players[item.Controller];
        if (player.Graveyard.Count >= 2)
        {
            var onceKey = $"gustav-ready:{card.InstanceId}:{State.TurnSerial}";
            if (!player.UsedAbilities.Contains(onceKey))
            {
                CreatePrompt(item.Controller, "optional", "古斯塔夫一世：是否将墓地2张牌返回牌库底部，将自身转为活跃？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "gustav-ready-choice", ["onceKey"] = onceKey });
                return true;
            }
        }
        FinishStackItem(item); return true;
    }

    private void ResolveS1FactionAfterDamage(L12StackItem item)
    {
        if (item.SourceCardId != "S01-0308") { FinishStackItem(item); return; }
        var opponent = State.Players[1 - item.Controller];
        if (opponent.Hand.Count == 0) { FinishStackItem(item); return; }
        CreatePrompt(1 - item.Controller, "discard", "血斧艾瑞克对主宰造成伤害：弃置1张手牌", opponent.Hand.Select(card => card.InstanceId), 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "erik-discard" });
    }

    private bool TryContinueS1Faction(L12StackItem item, L12Prompt prompt, List<string> chosen, L12Command command)
    {
        var action = prompt.Data.GetValueOrDefault("action") ?? string.Empty;
        var player = State.Players[item.Controller]; var enemy = State.Players[1 - item.Controller]; var source = FindSource(item);
        switch (action)
        {
            case "thutmose-kill": case "hunt-kill": case "harald-kill": if (chosen[0] != "skip") KillTarget(chosen[0], $"被{source?.Name}击杀"); FinishStackItem(item); return true;
            case "ramses-repeat":
                FinishStackItem(item);
                foreach (var id in chosen.Where(id => id != "skip").Reverse()) { var target = FindOnField(player, id, out _, out _); if (target is not null && HasImmediateEffect(target, "enter")) PushEffect(item.Controller, target, "enter", "拉美西斯二世再次发动的【登场时】效果"); }
                return true;
            case "horemheb-charge":
                if (chosen[0] != "skip" && source is not null)
                {
                    var target = FindOnField(player, chosen[0], out _, out _);
                    if (target is not null)
                        RemoveFromField(player, target, true, "被霍列姆赫布弃置", leaveKind: L12FieldLeaveKind.Discard);
                    source.HasCharge = true;
                }
                FinishStackItem(item); return true;
            case "nefertiti-discard": MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: true); FinishStackItem(item); return true;
            case "nitocris-ready": { var target = FindOnField(player, chosen[0], out _, out _); if (target is not null && source is not null) ReadyCardByEffect(item.Controller, source, target, $"{target.Name}因效果转为活跃"); FinishStackItem(item); return true; }
            case "ankh-enter":
            {
                var target = FindOnField(player, chosen[0], out _, out _);
                if (target is not null) AddTimedModifier(target, 2000, 0, State.TurnSerial, "安卡神碑");
                FinishStackItem(item);
                return true;
            }
            case "ankh-ready-target": { var target = FindOnField(player, chosen[0], out _, out _); if (target is not null && target.CardId == "S01-0212" && source is not null) ReadyCardByEffect(item.Controller, source, target, $"{target.Name}因效果转为活跃"); FinishStackItem(item); return true; }
            case "asgard-draw-heal": if (chosen[0] == "yes") BeginEffectMoralePayment(item, 1, "asgard-heal"); else FinishStackItem(item); return true;
            case "canopic-search":
            {
                var selected = player.Library.First(candidate => candidate.InstanceId == chosen[0]); player.Library.Remove(selected); AddCardToHandByEffect(player, selected, "library", $"{selected.Name}因效果加入手牌"); Shuffle(player.Library);
                HealMaster(item.Controller, 1, "卡诺匹斯箱"); if (source is not null) DiscardRelic(player, source); FinishStackItem(item); return true;
            }
            case "canopic-one":
            {
                var target = FindOnField(player, chosen[0], out _, out _); if (target is not null) { target.Troops += 2000; target.HasStrongAttack = true; }
                if (source is not null) DiscardRelic(player, source); FinishStackItem(item); return true;
            }
            case "canopic-four":
                foreach (var id in chosen.Where(id => id != "skip")) { var target = FindOnField(player, id, out _, out _); if (target is not null) { target.ImmortalUses = 1; target.ImmortalUntilTurn = ExpiryAtNextOwnStart(item.Controller); } }
                if (source is not null) DiscardRelic(player, source); FinishStackItem(item); return true;
            case "brynhild-sigurd":
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                DamageMaster(item.Controller, 1, "布伦希尔德登场效果"); item.Data["faction-summon"] = chosen[0]; PromptFirstEmptySlot(item, "faction-summon-slot", "选择齐格鲁德活跃登场的位置"); return true;
            case "oddr-draw": if (chosen[0] == "yes") { DamageMaster(item.Controller, 1, "神箭奥德尔登场效果"); Draw(player, 1); } FinishStackItem(item); return true;
            case "egil-pay":
                if (chosen[0] == "no") { FinishStackItem(item); return true; }
                DamageMaster(item.Controller, 1, "夺命诗人埃吉尔效果"); Mill(player, 2, "夺命诗人埃吉尔"); PromptEnemyByTroops(item, "egil-debuff", "选择对方1张军团，本回合兵力-2000", int.MaxValue, false); return true;
            case "egil-debuff": { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) target.Troops -= 2000; FinishStackItem(item); return true; }
            case "gram-bottom": if (chosen[0] != "skip") ReturnEnemyFieldToLibraryBottom(item.Controller, chosen[0]); FinishStackItem(item); return true;
            case "duat-mode":
                if (chosen[0] == "kill") PromptEnemyByTroops(item, "duat-kill", "杜阿特之门：击杀对方1张兵力不高于5000的军团", 5000, false);
                else RecoverSunCard(item, "S01-0221"); return true;
            case "duat-kill": if (chosen[0] != "skip") KillTarget(chosen[0], "被杜阿特之门击杀"); FinishStackItem(item); return true;
            case "faction-search-pick": CompleteFactionTopSearch(item, chosen); return true;
            case "faction-search-order": CompleteFactionSearchOrder(item, command.BottomCardInstanceIds ?? chosen); return true;
            case "festival-hand": ContinuePharaohFestivalHand(item, chosen[0]); return true;
            case "festival-grave": ContinuePharaohFestivalGrave(item, chosen[0]); return true;
            case "festival-bottom-order": CompletePharaohFestivalOrder(item, command.BottomCardInstanceIds ?? chosen); return true;
            case "valkyrie-card": item.Data["faction-summon"] = chosen[0]; PromptFirstEmptySlot(item, "faction-summon-slot", "选择军团活跃登场的位置"); return true;
            case "faction-summon-slot": SummonFromAnyPrivateZone(player, item.Data["faction-summon"], chosen[0], false); FinishStackItem(item); return true;
            case "menes-sacrifice":
                if (chosen[0] != "skip" && source is not null)
                {
                    var target = FindOnField(player, chosen[0], out _, out _);
                    if (target is not null)
                        RemoveFromField(player, target, true, "被美尼斯弃置", leaveKind: L12FieldLeaveKind.Discard);
                    source.Troops += 2000;
                    source.HasStrongAttack = true;
                }
                FinishStackItem(item); return true;
            case "saladin-move": if (chosen[0] == "skip") FinishStackItem(item); else { item.Data["saladin-unit"] = chosen[0]; CreatePrompt(item.Controller, "slot", "选择陵墓守卫位移后的位置", EmptySlots(player), 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "saladin-slot" }); } return true;
            case "saladin-slot": MoveOwnCardToSlot(player, item.Data["saladin-unit"], chosen[0]); FinishStackItem(item); return true;
            case "ay-buff": { var target = FindOnField(player, chosen[0], out _, out _); if (target is not null) target.Troops += 2000; FinishStackItem(item); return true; }
            case "ay-pay":
                if (chosen[0] == "yes") BeginEffectMoralePayment(item, 1, "ay-buff"); else FinishStackItem(item); return true;
            case "beowulf-buff": if (chosen[0] == "yes" && source is not null) { DamageMaster(item.Controller, 1, "贝奥武夫进攻效果"); source.Troops += 2000; } FinishStackItem(item); return true;
            case "olaf-strong":
                if (chosen[0] != "skip" && source is not null)
                {
                    var selected = player.Graveyard.FirstOrDefault(card => card.InstanceId == chosen[0]);
                    if (selected is not null) { MoveGraveToLibraryBottom(player, [selected]); source.HasStrongAttack = true; }
                }
                FinishStackItem(item); return true;
            case "gustav-attack-choice":
                if (chosen[0] == "yes")
                    CreatePrompt(item.Controller, "order", "古斯塔夫一世：选择墓地2张牌，依选择顺序返回牌库底部", player.Graveyard.Select(card => card.InstanceId), 2, 2,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "gustav-attack-return" });
                else FinishStackItem(item);
                return true;
            case "gustav-attack-return":
                MoveGraveToLibraryBottom(player, chosen.Select(id => player.Graveyard.First(card => card.InstanceId == id)).ToArray());
                if (source is not null) source.Troops += 2000;
                FinishStackItem(item); return true;
            case "gustav-ready-choice":
                if (chosen[0] == "yes")
                    CreatePrompt(item.Controller, "order", "古斯塔夫一世：选择墓地2张牌，依选择顺序返回牌库底部", player.Graveyard.Select(card => card.InstanceId), 2, 2,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "gustav-ready-return", ["onceKey"] = prompt.Data["onceKey"] });
                else FinishStackItem(item);
                return true;
            case "gustav-ready-return":
                MoveGraveToLibraryBottom(player, chosen.Select(id => player.Graveyard.First(card => card.InstanceId == id)).ToArray());
                if (source is not null) { ReadyCardByEffect(item.Controller, source, source, $"{source.Name}因效果转为活跃"); player.UsedAbilities.Add(prompt.Data["onceKey"]); AddEvent("effect", item.Controller, "古斯塔夫一世回收墓地2张牌并转为活跃", source); }
                FinishStackItem(item); return true;
            case "hunt-return":
                MoveGraveToLibraryBottom(player, chosen.Select(id => player.Graveyard.First(card => card.InstanceId == id)).ToArray());
                PromptEnemyByTroops(item, "hunt-kill", "猎杀时刻：击杀对方1张兵力不高于6000的军团", 6000, false); return true;
            case "tutankhamun-top": if (chosen[0] != "skip") MoveGraveToLibraryTop(player, chosen[0]); FinishStackItem(item); return true;
            case "nitocris-summon":
                if (chosen[0] == "skip") FinishStackItem(item);
                else { item.Data["faction-summon"] = chosen[0]; PromptFirstEmptySlot(item, "faction-summon-slot", "尼托克丽丝：选择军团活跃登场的位置"); }
                return true;
            case "bjorn-revive-choice":
                if (chosen[0] == "no") { FinishStackItem(item); return true; }
                var candidates = player.Graveyard.Where(card => card.InstanceId != source?.InstanceId).Select(card => card.InstanceId).ToArray();
                if (candidates.Length < 4) { FinishStackItem(item); return true; }
                DamageMaster(item.Controller, 1, "勇士比约恩阵亡效果");
                CreatePrompt(item.Controller, "order", "勇士比约恩：选择墓地4张牌，依选择顺序返回牌库底部", candidates, 4, 4,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "bjorn-revive-return" });
                return true;
            case "bjorn-revive-return":
                MoveGraveToLibraryBottom(player, chosen.Select(id => player.Graveyard.First(card => card.InstanceId == id)).ToArray());
                if (source is null) FinishStackItem(item);
                else BeginQueuedSummons(item, [source.InstanceId], tapped: true, "勇士比约恩：选择其休整登场的位置");
                return true;
            case "death-cycle-one": if (chosen[0] == "yes") { Draw(player, 1); PromptDiscard(item, item.Controller, 1, "弃置1张手牌", "death-cycle-discard"); } else FinishStackItem(item); return true;
            case "death-cycle-two": if (chosen[0] == "yes") { Draw(player, 2); PromptDiscard(item, item.Controller, 1, "弃置1张手牌", "death-cycle-discard"); } else FinishStackItem(item); return true;
            case "death-cycle-discard": MoveHandToGrave(player, chosen[0], causedByEffect: true); FinishStackItem(item); return true;
            case "recover-asgard": if (chosen[0] != "skip") MoveGraveToHand(player, chosen[0]); FinishStackItem(item); return true;
            case "summon-asgard": if (chosen[0] == "skip") FinishStackItem(item); else { item.Data["faction-summon"] = chosen[0]; PromptFirstEmptySlot(item, "faction-summon-slot", "选择军团活跃登场的位置"); } return true;
            case "oddr-tap": if (chosen[0] != "skip") { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) target.Tapped = true; } FinishStackItem(item); return true;
            case "erik-discard": MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: true); FinishStackItem(item); return true;
            case "palace-kill":
            {
                var target = FindOnField(enemy, chosen[0], out _, out _); if (target is null) { FinishStackItem(item); return true; }
                var paid = target.CurrentCost; if (!ReturnMorale(player, paid)) { FinishStackItem(item); return true; } KillTarget(target.InstanceId, "被凌霄宝殿击杀");
                var choices = player.Graveyard.Where(card => card.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, card, "tianting") && card.CurrentCost <= paid).Select(card => card.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "选择墓地1张费用不高于返还士气数量的【天廷】军团活跃登场", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "palace-revive" }); return true;
            }
            case "palace-revive": if (chosen[0] == "skip") FinishStackItem(item); else { item.Data["faction-summon"] = chosen[0]; PromptFirstEmptySlot(item, "faction-summon-slot", "选择军团活跃登场的位置"); } return true;
            case "queued-summon-slot": CompleteQueuedSummon(item, chosen[0]); return true;
            case "mengpo-silence": { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) target.SuppressDeathUntilTurn = State.TurnSerial; if (player.Hand.Count <= 5) Draw(player, 1); FinishStackItem(item); return true; }
            case "mengpo-discard": MoveHandToGrave(player, chosen[0], causedByEffect: false); AddMorale(player, 1, true); FinishStackItem(item); return true;
            case "sun-bottom": ReturnEnemyFieldToLibraryBottom(item.Controller, chosen[0]); FinishStackItem(item); return true;
            case "isis-canopic":
            {
                var relic = player.Graveyard.First(card => card.InstanceId == chosen[0]); player.Graveyard.Remove(relic);
                ResetCardAfterLeavingField(relic);
                player.SpecialZones.CanopicProgress.Add(relic);
                CreatePrompt(item.Controller, "option", "伊西斯：选择抽取1张牌或主宰增加1点血量",
                    ["draw", "heal"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "isis-canopic-reward", ["choiceMode"] = "instant",
                        ["draw"] = "抽取1张牌", ["heal"] = "主宰增加1点血量",
                    });
                return true;
            }
            case "isis-canopic-reward":
                if (chosen[0] == "draw") Draw(player, 1); else HealMaster(item.Controller, 1, "伊西斯");
                FinishStackItem(item); return true;
            case "medjed-debuff": { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) target.Troops -= 1000; FinishStackItem(item); return true; }
            case "medjed-extra-choice":
                if (chosen[0] == "normal") { ApplyDeclaredTroopsDelta(item, -1000); FinishStackItem(item); }
                else
                {
                    var guards = PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped)
                        .Select(card => card.InstanceId).ToArray();
                    if (guards.Length == 0) { ApplyDeclaredTroopsDelta(item, -1000); FinishStackItem(item); }
                    else CreatePrompt(item.Controller, "friendly-target", "梅杰德：选择额外休整的陵墓守卫", guards, 1, 1,
                        "card-effect", item.StackItemId, isPrivate: true,
                        data: new Dictionary<string, string> { ["action"] = "medjed-extra-guard", ["choiceMode"] = "board-target" });
                }
                return true;
            case "medjed-extra-guard":
            {
                var guard = FindOnField(player, chosen[0], out _, out _);
                if (guard is null || guard.CardId != "S01-0212" || guard.Tapped)
                {
                    ApplyDeclaredTroopsDelta(item, -1000);
                    FinishStackItem(item);
                    return true;
                }
                guard.Tapped = true;
                ApplyDeclaredTroopsDelta(item, -3000);
                FinishStackItem(item);
                return true;
            }
            case "medjed-damage-response":
                player.UsedAbilities.Remove("pending:medjedDamageResponse");
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                player.UsedAbilities.Add("trigger:medjedDamageResponse");
                BeginQueuedSummons(item, [chosen[0]], tapped: false, "梅杰德：选择〈陵墓守卫〉活跃登场的位置");
                return true;
            case "valhalla-kill5": if (chosen[0] != "skip") KillTarget(chosen[0], "被英灵殿击杀"); PromptEnemyByTroops(item, "valhalla-kill1", "英灵殿：可击杀对方1张兵力不高于1000的军团", 1000, true); return true;
            case "valhalla-kill1": if (chosen[0] != "skip") KillTarget(chosen[0], "被英灵殿击杀"); FinishStackItem(item); return true;
            case "valkyrie-pick":
            {
                var selected = player.Graveyard.First(card => card.InstanceId == chosen[0]); var pair = player.Graveyard.Take(2).ToArray(); player.Graveyard.Remove(selected); AddCardToHandByEffect(player, selected, "graveyard", $"{selected.Name}因效果加入手牌");
                MoveGraveToLibraryBottom(player, pair.Where(card => card.InstanceId != selected.InstanceId)); FinishStackItem(item); return true;
            }
            case "yomi-kill3": if (chosen[0] != "skip") KillTarget(chosen[0], "被黄泉之门击杀"); PromptEnemyLegion(item, "yomi-kill1", "黄泉之门：可击杀对方1张费用不高于1的军团", target => target.CurrentCost <= 1, true); return true;
            case "yomi-kill1": if (chosen[0] != "skip") KillTarget(chosen[0], "被黄泉之门击杀"); FinishStackItem(item); return true;
            case "amaterasu-debuff":
            {
                var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) target.CostModifier--;
                PromptEnemyLegion(item, "amaterasu-kill", "天照大神：击杀对方1张费用为0的军团", card => card.CurrentCost == 0, true); return true;
            }
            case "amaterasu-kill": if (chosen[0] != "skip") KillTarget(chosen[0], "被天照大神击杀"); FinishStackItem(item); return true;
            case "amaterasu-discard":
                MoveHandToGrave(player, chosen[0], causedByEffect: false); if (source is not null) foreach (var morale in player.Morale.Where(card => card.Tapped).Take(2).ToArray()) ReadyMoraleByEffect(item.Controller, source, morale, "士气因效果转为活跃");
                foreach (var legion in player.Field[0].Where(card => card?.Faction == "gaotianyuan").Cast<L12CardInstance>()) legion.Troops += 1000;
                FinishStackItem(item); return true;
            default: return false;
        }
    }

    private CommandResult? TryBeginS1FactionActiveAbility(int playerIndex, L12CardInstance source, string ability)
    {
        if (!GetS1FactionAbilities(source.CardId).Any(entry => entry.Id == ability)) return null;
        var player = State.Players[playerIndex];
        var enemy = State.Players[1 - playerIndex];
        string[] choices;
        switch (ability)
        {
            case "olgaDebuff":
                choices = enemy.Field[0].Where(card => card is not null && IsFieldLegion(card) && !card.Hidden).Select(card => card!.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "奥尔加：选择对方前排 1 张军团");
            case "palaceExchange":
                choices = PublicLegions(enemy).Where(card => player.Morale.Count >= card.CurrentCost).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "凌霄宝殿：选择要击杀并按费用返还士气的军团");
            case "mengpoSilence":
                choices = PublicLegions(enemy).Select(card => card.InstanceId).ToArray();
                return BeginPendingActivation(playerIndex, source, ability, choices,
                    "孟婆：选择对方最多1张军团，本回合失去「阵亡时」效果", min: 0, max: 1);
            case "sunBottomEnemy":
                choices = PublicLegions(enemy).Where(card => card.Troops <= 4000).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "众神之乡：选择返回牌库底部的军团");
            case "ankhReady":
                if (!PublicLegions(player).Any(card => card.CardId == "S01-0212" && card.Tapped))
                    return CommandResult.Reject("需要我方存在休整的陵墓守卫");
                choices = player.Hand.Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "安卡神杯：选择弃置的1张手牌");
            case "ankhDraw":
                choices = PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "安卡神杯：选择转为休整的陵墓守卫");
            case "gramDamage":
                choices = player.Graveyard.Where(card => card.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, card, "asgard")).Select(card => card.InstanceId).ToArray();
                return BeginPendingActivation(playerIndex, source, ability, choices, "神剑格拉墨：依次选择4张【阿斯加德】军团返回牌库底部", 4, 4);
            case "medjedDebuff":
            case "amaterasuKill":
                choices = PublicLegions(enemy).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, ability == "medjedDebuff"
                    ? "梅杰德：选择本回合兵力 -1000 的军团" : "天照大神：选择本回合费用 -1 的军团");
            default:
                return CommitActiveAbility(playerIndex, source, ability, null);
        }
    }

    private CommandResult? TryCommitS1FactionActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey, bool? useTombGuards)
    {
        var player = State.Players[playerIndex];
        bool ConsumeMorale(int cost) => useTombGuards switch
        {
            true => TryConsumeMorale(player, cost, preferTombGuards: true, allowTombGuards: true),
            false => TryConsumeMorale(player, cost, preferTombGuards: false, allowTombGuards: false),
            _ => TryConsumeMorale(player, cost),
        };
        switch (ability)
        {
            case "cleopatraGuard" when source.CardId == "S01-0214": if (source.Tapped || !ConsumeMorale(1)) return CommandResult.Reject("需要活跃的克利奥帕特拉七世与1张活跃士气"); source.Tapped = true; break;
            case "sunGuard" when source.CardId == "S01-02C1": if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "sunDraw" when source.CardId == "S01-02C1": if (player.Hand.Count > 3 || !ConsumeMorale(1)) return CommandResult.Reject("手牌需不高于3张，且需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "asgardDraw" when source.CardId == "S01-03C1": if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "alvidaSummon" when source.CardId == "S01-0307": RemoveFromField(player, source, true, "被主动效果弃置",
                leaveKind: L12FieldLeaveKind.Discard); break;
            case "olgaDebuff" when source.CardId == "S01-0314":
                if (!IsEnemyTargetLegal(playerIndex, target, card => FindOnField(State.Players[1 - playerIndex], card.InstanceId, out var row, out _) is not null && row == 0)) return CommandResult.Reject("目标不再合法");
                RemoveFromField(player, source, true, "被主动效果弃置", leaveKind: L12FieldLeaveKind.Discard); break;
            case "gramReady" when source.CardId == "S01-0317": if (!source.Tapped || !ConsumeMorale(2)) return CommandResult.Reject("神剑格拉墨需为休整，且需要2张活跃士气"); break;
            case "palaceReward" when source.CardId == "S01-01D1": if (player.ReturnedMoraleThisTurn <= 1) return CommandResult.Reject("本回合返还士气需高于1张"); player.UsedAbilities.Add(onceKey); break;
            case "palaceExchange" when source.CardId == "S01-01D1":
            {
                var declared = DeclaredEnemyTarget(playerIndex, target); if (source.Tapped || declared is null) return CommandResult.Reject("凌霄宝殿必须为活跃状态且目标合法");
                var paid = declared.CurrentCost; if (!ReturnMorale(player, paid)) return CommandResult.Reject("士气不足以支付所选目标费用");
                source.Tapped = true; player.MasterTapped = true; target = declared.InstanceId; break;
            }
            case "mengpoSilence" when source.CardId == "S01-01M2":
                if (!string.IsNullOrWhiteSpace(target) && DeclaredEnemyTarget(playerIndex, target) is null)
                    return CommandResult.Reject("目标不再合法");
                if (!ReturnMorale(player, 1)) return CommandResult.Reject("需要返还1张士气"); player.UsedAbilities.Add(onceKey); break;
            case "mengpoMorale" when source.CardId == "S01-01M2": if (player.Morale.Count >= State.Players[1 - playerIndex].Morale.Count || player.Hand.Count == 0) return CommandResult.Reject("士气需少于对方，且需弃置1张手牌"); player.UsedAbilities.Add(onceKey); break;
            case "sunTopThree" or "sunBottomEnemy" when source.CardId == "S01-02D1":
                if (ability == "sunBottomEnemy" && DeclaredEnemyTarget(playerIndex, target, card => card.Troops <= 4000) is null) return CommandResult.Reject("目标不再合法");
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "ankhReady" when source.CardId == "S01-0215":
                if (source.Tapped) return CommandResult.Reject("安卡神杯必须为活跃状态");
                if (string.IsNullOrWhiteSpace(target) || !player.Hand.Any(card => card.InstanceId == target)) return CommandResult.Reject("弃置的手牌不再合法");
                if (!PublicLegions(player).Any(card => card.CardId == "S01-0212" && card.Tapped)) return CommandResult.Reject("需要我方存在休整的陵墓守卫");
                source.Tapped = true; MoveHandToGrave(player, target, causedByEffect: false); break;
            case "ankhDraw" when source.CardId == "S01-0215":
            {
                var guard = PublicLegions(player).FirstOrDefault(card => card.InstanceId == target && card.CardId == "S01-0212" && !card.Tapped);
                if (source.Tapped || guard is null) return CommandResult.Reject("需要活跃的安卡神杯与陵墓守卫");
                source.Tapped = true; guard.Tapped = true; break;
            }
            case "gramDamage" when source.CardId == "S01-0317":
            {
                var ids = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var cards = ids.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id
                    && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "asgard"))).ToArray();
                if (source.Tapped || cards.Length != 4 || cards.Any(card => card is null) || ids.Distinct().Count() != 4)
                    return CommandResult.Reject("需要活跃的神剑格拉墨与墓地4张不同的【阿斯加德】军团");
                source.Tapped = true;
                foreach (var card in cards.Cast<L12CardInstance>()) { player.Graveyard.Remove(card); player.Library.Add(card); }
                break;
            }
            case "isisCanopic" when source.CardId == "S01-02M1":
            {
                var guards = PublicLegions(player).Where(card => card.CardId == "S01-0212").Take(3).ToArray();
                if (guards.Length < 3) return CommandResult.Reject("战场需要3张陵墓守卫");
                foreach (var guard in guards)
                    RemoveFromField(player, guard, true, "作为伊西斯主宰效果的发动费用弃置",
                        leaveKind: L12FieldLeaveKind.Discard);
                break;
            }
            case "medjedDebuff" when source.CardId == "S01-02M3":
                if (DeclaredEnemyTarget(playerIndex, target) is null) return CommandResult.Reject("目标不再合法");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "valhallaDiscount" when source.CardId == "S01-03D1": if (player.Hp <= 1) return CommandResult.Reject("主宰血量不足"); DamageMaster(playerIndex, 1, "英灵殿费用减免"); player.UsedAbilities.Add(onceKey); break;
            case "valhallaRecover" when source.CardId == "S01-03D1": if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "valhallaKill" when source.CardId == "S01-03D1": if (source.Tapped || player.Graveyard.Count < 2) return CommandResult.Reject("英灵殿需为活跃，墓地需至少2张牌"); source.Tapped = true; player.MasterTapped = true; break;
            case "valkyrieRecover" when source.CardId == "S01-03M1": if (!ConsumeMorale(1) || player.Hp <= 1) return CommandResult.Reject("需要1张活跃士气且主宰血量需高于1"); DamageMaster(playerIndex, 1, "瓦尔基里主宰效果"); player.UsedAbilities.Add(onceKey); break;
            case "lokiCycle" when source.CardId == "S01-03M2":
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "lokiHeal" when source.CardId == "S01-03M2":
                if (player.Graveyard.Count(CanEnterHandOrLibrary) < 2) return CommandResult.Reject("墓地需要至少2张可返回牌库的卡牌");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "yomiDiscount" when source.CardId == "S01-04D1": player.UsedAbilities.Add(onceKey); break;
            case "yomiSweep" when source.CardId == "S01-04D1": if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "yomiRecover" when source.CardId == "S01-04D1": if (source.Tapped) return CommandResult.Reject("黄泉之门必须为活跃状态"); source.Tapped = true; player.MasterTapped = true; break;
            case "amaterasuKill" when source.CardId == "S01-04M1":
                if (DeclaredEnemyTarget(playerIndex, target) is null) return CommandResult.Reject("目标不再合法");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "amaterasuReady" when source.CardId == "S01-04M1": if (player.Hand.Count == 0) return CommandResult.Reject("需要弃置1张手牌"); player.UsedAbilities.Add(onceKey); break;
            default: return null;
        }
        var data = new Dictionary<string, string> { ["ability"] = ability };
        if (!string.IsNullOrWhiteSpace(target)) data["target"] = target;
        if (ability == "palaceExchange" && DeclaredEnemyTarget(playerIndex, target) is { } palaceTarget)
            data["paid"] = palaceTarget.CurrentCost.ToString();
        PushEffect(playerIndex, source, "active", "主动效果", data: data); return CommandResult.Ok();
    }

    private bool TryResolveS1FactionActive(L12StackItem item, L12CardInstance? source, string ability)
    {
        var player = State.Players[item.Controller];
        switch (ability)
        {
            case "cleopatraGuard": case "sunGuard":
                BeginQueuedSummons(item, player.Graveyard.Where(card => card.CardId == "S01-0212").Take(1).Select(card => card.InstanceId), false,
                    "选择陵墓守卫活跃登场的位置"); return true;
            case "sunDraw": Draw(player, 1); FinishStackItem(item); return true;
            case "asgardDraw":
                Draw(player, 1);
                if (player.Hp <= 5 && ActiveResourceCount(player) > 0)
                    CreatePrompt(item.Controller, "optional", "是否额外消耗1张活跃士气，使我方主宰增加1点血量？", ["yes", "no"], 1, 1,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "asgard-draw-heal", ["choiceMode"] = "instant" });
                else FinishStackItem(item);
                return true;
            case "alvidaSummon":
            {
                var choices = player.Hand.Where(card => card.CardType == "legion" && card.DisasterLevel == 2).Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                DamageMaster(item.Controller, 1, "阿尔维达主动效果"); CreatePrompt(item.Controller, "card", "选择手牌1张天灾等级2的军团活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "summon-asgard" }); return true;
            }
            case "olgaDebuff": ApplyDeclaredTroopsDelta(item, -2000); FinishStackItem(item); return true;
            case "gramReady": if (source is not null) ReadyCardByEffect(item.Controller, source, source, $"{source.Name}因效果转为活跃"); FinishStackItem(item); return true;
            case "palaceReward": AddMorale(player, 2, true); Draw(player, 1); FinishStackItem(item); return true;
            case "palaceExchange": ResolveDeclaredPalaceExchange(item); return true;
            case "mengpoSilence":
            {
                var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
                if (target is not null) target.SuppressDeathUntilTurn = State.TurnSerial;
                if (player.Hand.Count <= 5) Draw(player, 1); FinishStackItem(item); return true;
            }
            case "mengpoMorale": PromptDiscard(item, item.Controller, 1, "孟婆：弃置1张手牌", "mengpo-discard"); return true;
            case "sunTopThree": BeginFactionTopSearch(item, 3, "taiyangcheng", string.Empty, "sun-divinity"); return true;
            case "sunBottomEnemy": ReturnEnemyFieldToLibraryBottom(item.Controller, item.Data.GetValueOrDefault("target") ?? string.Empty); FinishStackItem(item); return true;
            case "ankhReady": PromptOwnLegion(item, "ankh-ready-target", "安卡神杯：选择我方1张休整的陵墓守卫转为活跃", card => card.CardId == "S01-0212" && card.Tapped, false); return true;
            case "ankhDraw": Draw(player, 1); FinishStackItem(item); return true;
            case "gramDamage": DamageMasterNonLethal(1 - item.Controller, 1, "神剑格拉墨"); FinishStackItem(item); return true;
            case "isisCanopic":
            {
                var completedIds = player.SpecialZones.CanopicProgress.Select(card => card.CardId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var choices = player.Graveyard.Where(card => card.Name.Contains("卡诺匹斯", StringComparison.Ordinal)
                    && card.CardType == "artifact" && !completedIds.Contains(card.CardId)).Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "card", "伊西斯：选择墓地1张卡诺匹斯圣物置入圣物区", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "isis-canopic" }); return true;
            }
            case "medjedDebuff":
            {
                var activeGuards = PublicLegions(player)
                    .Where(card => card.CardId == "S01-0212" && !card.Tapped)
                    .Select(card => card.InstanceId).ToArray();
                if (activeGuards.Length == 0)
                {
                    ApplyDeclaredTroopsDelta(item, -1000);
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "option", "梅杰德：是否额外休整1张陵墓守卫？",
                    ["normal", "extra"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "medjed-extra-choice", ["choiceMode"] = "instant",
                        ["normal"] = "使选择的军团本回合兵力-1000。",
                        ["extra"] = "额外休整我方1张〈陵墓守卫〉：改为使选择的军团本回合兵力-3000。",
                    });
                return true;
            }
            case "valhallaDiscount": player.NextFactionLegionDiscount = Math.Max(player.NextFactionLegionDiscount, 1); FinishStackItem(item); return true;
            case "valhallaRecover": Mill(player, 2, "英灵殿"); RecoverAsgard(item, int.MaxValue, false); return true;
            case "valhallaKill": MoveGraveToLibraryBottom(player, player.Graveyard.Take(2)); PromptEnemyByTroops(item, "valhalla-kill5", "英灵殿：击杀对方1张兵力不高于5000的军团", 5000, true); return true;
            case "valkyrieRecover":
            {
                var choices = player.Graveyard.Take(2).Select(card => card.InstanceId).ToArray(); if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "card", "瓦尔基里：选择其中1张加入手牌，其余返回牌库底部", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "valkyrie-pick" }); return true;
            }
            case "lokiCycle": Draw(player, 1); PromptDiscard(item, item.Controller, 1, "洛基：弃置1张手牌", "death-cycle-discard"); return true;
            case "lokiHeal":
            {
                var choices = player.Graveyard.Where(CanEnterHandOrLibrary).Select(card => card.InstanceId).ToArray();
                if (choices.Length < 2) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "cards", "洛基：选择墓地2张卡牌返回牌库底部", choices, 2, 2,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "loki-heal-return", ["sourceZone"] = "graveyard", ["layout"] = "single-row",
                    });
                return true;
            }
            case "yomiDiscount": player.NextFactionLegionDiscount = 2; FinishStackItem(item); return true;
            case "yomiSweep": Draw(player, 1); foreach (var target in PublicLegions(State.Players[1 - item.Controller])) target.CostModifier--; PromptEnemyLegion(item, "yomi-kill3", "黄泉之门：可击杀对方1张费用不高于3的军团", target => target.CurrentCost <= 3, true); return true;
            case "yomiRecover":
            {
                var choices = player.Graveyard.Where(card => card.Faction == "gaotianyuan").Select(card => card.InstanceId).ToArray(); if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "card", "黄泉之门：选择墓地1张【高天原】卡牌加入手牌", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "recover-asgard" }); return true;
            }
            case "amaterasuKill":
            {
                var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target")); if (target is not null) target.CostModifier--;
                PromptEnemyLegion(item, "amaterasu-kill", "天照大神：击杀对方1张费用为0的军团", card => card.CurrentCost == 0, true); return true;
            }
            case "amaterasuReady": PromptDiscard(item, item.Controller, 1, "天照大神：弃置1张手牌", "amaterasu-discard"); return true;
            default: return false;
        }
    }

    private bool TryPreventS1FactionDeath(L12PlayerState player, L12CardInstance card)
    {
        if (card.CardId != "S01-0205") return false;
        var guard = PublicLegions(player).FirstOrDefault(target => target.CardId == "S01-0212" && target.InstanceId != card.InstanceId);
        if (guard is null) return false;
        if (FindOnField(player, guard.InstanceId, out var row, out var slot) is not null)
        {
            player.Field[row][slot] = null;
            ResetCardAfterLeavingField(guard);
            player.Graveyard.Add(guard);
        }
        card.Troops = 1000; AddEvent("effect", player.PlayerIndex, "霍列姆赫布弃置陵墓守卫代替阵亡", card, guard); return true;
    }

    private static bool HasS1Taunt(L12CardInstance card, int row)
        => L12StructuredCardRules.HasTaunt(card, row);

    private int ApplyS1FactionAttackPassives(int playerIndex, L12CardInstance attacker, int row)
    {
        var temporaryBonus = 0;
        var player = State.Players[playerIndex];
        if (row == 0 && attacker.Faction == "taiyangcheng")
        {
            var slot = Array.FindIndex(player.Field[0], card => card?.InstanceId == attacker.InstanceId);
            if (slot >= 0 && player.Field[0].Where((card, index) => Math.Abs(index - slot) == 1 && card?.CardId == "S01-0206").Any())
            {
                attacker.Troops += 1000;
                temporaryBonus += 1000;
                AddEvent("effect", playerIndex, "萨拉丁使相邻太阳城军团本次进攻兵力+1000", attacker);
            }
        }
        return temporaryBonus;
    }

    private void ApplySunKingAttack(L12StackItem item)
    {
        foreach (var target in PublicLegions(State.Players[1 - item.Controller])) target.Troops -= 1000;
        PromptEnemyByTroops(item, "thutmose-kill", "选择对方1张兵力不高于1000的军团并击杀", 1000, true);
    }

    private void PromptEnemyByTroops(L12StackItem item, string action, string text, int maxTroops, bool optional, int? row = null)
    {
        var enemy = State.Players[1 - item.Controller];
        var choices = PublicLegions(enemy).Where(target => target.Troops <= maxTroops && (row is null || FindOnField(enemy, target.InstanceId, out var targetRow, out _) is not null && targetRow == row))
            .Select(target => target.InstanceId).ToList();
        if (optional) choices.Add("skip");
        if (choices.Count == 0) { FinishStackItem(item); return; }
        CreatePrompt(item.Controller, "target", text, choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = action });
    }

    private void PromptOwnLegion(L12StackItem item, string action, string text, Func<L12CardInstance, bool> predicate, bool optional)
    {
        var choices = PublicLegions(State.Players[item.Controller]).Where(predicate).Select(target => target.InstanceId).ToList();
        if (optional) choices.Add("skip");
        if (choices.Count == 0) { FinishStackItem(item); return; }
        CreatePrompt(item.Controller, "target", text, choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = action });
    }

    private void BeginQueuedSummons(L12StackItem item, IEnumerable<string> ids, bool tapped, string text)
    {
        var player = State.Players[item.Controller];
        var queue = ids.Distinct().Where(id => player.Graveyard.Any(card => card.InstanceId == id)
                || player.Hand.Any(card => card.InstanceId == id) || player.Library.Any(card => card.InstanceId == id))
            .Take(EmptySlots(player).Count()).ToArray();
        if (queue.Length == 0) { FinishStackItem(item); return; }
        item.Data["summon-queue"] = string.Join('|', queue);
        item.Data["summon-tapped"] = tapped ? "true" : "false";
        item.Data["summon-text"] = text;
        PromptNextQueuedSummon(item);
    }

    private void ResolveMedjedMasterDamageReaction(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        player.UsedAbilities.Remove("pending:medjedDamageResponse");
        if (player.MasterId != "S01-02M3" || player.UsedAbilities.Contains("trigger:medjedDamageResponse"))
        {
            FinishStackItem(item);
            return;
        }
        var choices = player.Graveyard.Where(card => card.CardId == "S01-0212")
            .Select(card => card.InstanceId).ToList();
        if (choices.Count == 0 || !EmptySlots(player).Any())
        {
            FinishStackItem(item);
            return;
        }
        choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "梅杰德：可将墓地1张〈陵墓守卫〉活跃登场",
            choices, 1, 1, "card-effect", item.StackItemId, isPrivate: true,
            data: new Dictionary<string, string> { ["action"] = "medjed-damage-response", ["choiceMode"] = "card-row" });
    }

    private void PromptNextQueuedSummon(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var queue = item.Data.GetValueOrDefault("summon-queue", string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        queue.RemoveAll(id => player.Graveyard.All(card => card.InstanceId != id)
            && player.Hand.All(card => card.InstanceId != id) && player.Library.All(card => card.InstanceId != id));
        if (queue.Count == 0 || !EmptySlots(player).Any()) { FinishStackItem(item); return; }
        item.Data["summon-queue"] = string.Join('|', queue);
        var current = player.Graveyard.Concat(player.Hand).Concat(player.Library).First(card => card.InstanceId == queue[0]);
        var data = new Dictionary<string, string> { ["action"] = "queued-summon-slot", ["previewCardId"] = current.InstanceId };
        AddPromptCardData(data, current);
        CreatePrompt(item.Controller, "slot", item.Data.GetValueOrDefault("summon-text", "选择军团登场的位置"), EmptySlots(player), 1, 1,
            "card-effect", item.StackItemId, data: data);
    }

    private void CompleteQueuedSummon(L12StackItem item, string slotChoice)
    {
        var queue = item.Data.GetValueOrDefault("summon-queue", string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (queue.Count == 0) { FinishStackItem(item); return; }
        SummonFromAnyPrivateZone(State.Players[item.Controller], queue[0], slotChoice, item.Data.GetValueOrDefault("summon-tapped") == "true");
        queue.RemoveAt(0);
        item.Data["summon-queue"] = string.Join('|', queue);
        PromptNextQueuedSummon(item);
    }

    private void DiscardRelic(L12PlayerState player, L12CardInstance relic)
    {
        if (player.Relic?.InstanceId == relic.InstanceId) player.Relic = null; else player.ExtraRelics.Remove(relic);
        DiscardAttachedCards(relic, "被叠放的圣物离开圣物区");
        ResetCardAfterLeavingField(relic);
        if (!player.Graveyard.Contains(relic)) player.Graveyard.Add(relic);
        QueueTriggerCandidates(BuildS1LeaveReactionCandidates(player.PlayerIndex, relic));
    }

    private void Mill(L12PlayerState player, int count, string source)
    {
        var result = L12LibraryOps.Mill(player, count);
        if (!result.Success) { SetWinner(1 - player.PlayerIndex, $"{source}操作牌库时牌库数量不足"); return; }
        AddEvent("mill", player.PlayerIndex, $"{source}弃置牌库顶部{result.Cards.Count}张牌", result.Cards.ToArray());
        foreach (var card in result.Cards) NotifyCardDiscarded(player, card, "library", causedByEffect: true);
    }

    private void MoveGraveToLibraryBottom(L12PlayerState player, IEnumerable<L12CardInstance> cards)
    {
        var requested = cards.Distinct().ToArray();
        var legal = requested.Where(CanEnterHandOrLibrary).ToArray();
        foreach (var guard in requested.Where(card => !CanEnterHandOrLibrary(card)))
            AddEvent("replacement", player.PlayerIndex, $"{guard.Name}不能进入牌库，仍置于墓地", guard);
        L12LibraryOps.PutOnBottom(player, legal);
    }

    private void MoveGraveToLibraryTop(L12PlayerState player, string instanceId)
    {
        var card = player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return;
        if (!CanEnterHandOrLibrary(card)) { AddEvent("replacement", player.PlayerIndex, $"{card.Name}不能进入牌库，仍置于墓地", card); return; }
        L12LibraryOps.PutOnTop(player, [card]);
    }

    private void RecoverAsgard(L12StackItem item, int maxCost, bool legionOnly)
    {
        var player = State.Players[item.Controller]; var choices = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard") && card.CurrentCost <= maxCost && (!legionOnly || card.CardType == "legion")).Select(card => card.InstanceId).ToList(); choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "选择墓地1张【阿斯加德】卡牌加入手牌", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "recover-asgard" });
    }

    private void SummonAsgardFromGrave(L12StackItem item, int maxCost)
    {
        var player = State.Players[item.Controller]; var choices = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard") && card.CardType == "legion" && card.CurrentCost <= maxCost).Select(card => card.InstanceId).ToList(); choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "选择墓地1张【阿斯加德】军团活跃登场", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "summon-asgard" });
    }

    private void BeginPharaohFestival(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var top = player.Library.Take(5).ToArray();
        item.Data["festival-cards"] = string.Join('|', top.Select(card => card.InstanceId));
        var eligible = top.Where(card => card.Faction == "taiyangcheng" && card.CardId != "S01-0222").ToArray();
        if (eligible.Length == 0) { PromptPharaohFestivalOrder(item); return; }
        CreateFestivalCardPrompt(item, "festival-hand", "法老王的庆典：选择1张【太阳城】卡牌加入手牌", eligible);
    }

    private void ContinuePharaohFestivalHand(L12StackItem item, string choice)
    {
        var player = State.Players[item.Controller];
        var selected = player.Library.FirstOrDefault(card => card.InstanceId == choice);
        if (selected is not null) { player.Library.Remove(selected); AddCardToHandByEffect(player, selected, "library", $"法老王的庆典将{selected.Name}加入手牌"); AddEvent("search", item.Controller, $"法老王的庆典将 {selected.Name} 加入手牌", selected); }
        var eligible = FestivalCardsStillInLibrary(item, player).Where(card => card.Faction == "taiyangcheng" && card.CardId != "S01-0222").ToArray();
        if (eligible.Length == 0) { PromptPharaohFestivalOrder(item); return; }
        CreateFestivalCardPrompt(item, "festival-grave", "法老王的庆典：选择另1张【太阳城】卡牌置入墓地", eligible);
    }

    private void ContinuePharaohFestivalGrave(L12StackItem item, string choice)
    {
        var player = State.Players[item.Controller];
        var selected = player.Library.FirstOrDefault(card => card.InstanceId == choice);
        if (selected is not null) { player.Library.Remove(selected); player.Graveyard.Add(selected); AddEvent("discard", item.Controller, $"法老王的庆典将 {selected.Name} 置入墓地", selected); }
        PromptPharaohFestivalOrder(item);
    }

    private void CreateFestivalCardPrompt(L12StackItem item, string action, string text, IReadOnlyCollection<L12CardInstance> choices)
    {
        var displayed = FestivalCardsStillInLibrary(item, State.Players[item.Controller]);
        var data = new Dictionary<string, string> { ["action"] = action, ["displayCardIds"] = string.Join('|', displayed.Select(card => card.InstanceId)) };
        foreach (var card in displayed) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "card", text, choices.Select(card => card.InstanceId), 1, 1, "card-effect", item.StackItemId, data: data);
    }

    private List<L12CardInstance> FestivalCardsStillInLibrary(L12StackItem item, L12PlayerState player)
        => item.Data["festival-cards"].Split('|').Select(id => player.Library.FirstOrDefault(card => card.InstanceId == id)).Where(card => card is not null).Cast<L12CardInstance>().ToList();

    private void PromptPharaohFestivalOrder(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var remaining = FestivalCardsStillInLibrary(item, player);
        if (remaining.Count <= 1) { CompletePharaohFestivalOrder(item, remaining.Select(card => card.InstanceId).ToList()); return; }
        var data = new Dictionary<string, string>
        {
            ["action"] = "festival-bottom-order",
            ["placementMode"] = "all-bottom",
            ["displayCardIds"] = string.Join('|', remaining.Select(card => card.InstanceId))
        };
        foreach (var card in remaining) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "order", "调整其余卡牌的顺序，然后全部放回牌库底部。", remaining.Select(card => card.InstanceId), remaining.Count, remaining.Count,
            "card-effect", item.StackItemId, data: data);
    }

    private void CompletePharaohFestivalOrder(L12StackItem item, List<string> order)
    {
        var player = State.Players[item.Controller];
        foreach (var id in order)
        {
            var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == id); if (card is null) continue;
            player.Library.Remove(card); player.Library.Add(card);
        }
        FinishStackItem(item);
    }

    private void BeginFactionTopSearch(L12StackItem item, int count, string faction, string excluded, string context)
    {
        var player = State.Players[item.Controller]; var top = player.Library.Take(count).ToArray(); item.Data["faction-search-top"] = string.Join('|', top.Select(card => card.InstanceId)); item.Data["faction-search-context"] = context;
        const int max = 1;
        var choices = top.Where(card => card.Faction == faction && card.CardId != excluded).Select(card => card.InstanceId).ToArray();
        if (choices.Length == 0)
        {
            PromptFactionSearchOrder(item, top.Select(card => card.InstanceId).ToList());
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "faction-search-pick",
            ["displayCardIds"] = string.Join('|', top.Select(card => card.InstanceId))
        };
        foreach (var card in top) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "search", $"选择{max}张符合条件的卡牌", choices, max, max, "card-effect", item.StackItemId, data: data);
    }

    private void CompleteFactionTopSearch(L12StackItem item, List<string> chosen)
    {
        var player = State.Players[item.Controller];
        foreach (var id in chosen) { var card = player.Library.First(candidate => candidate.InstanceId == id); player.Library.Remove(card); AddCardToHandByEffect(player, card, "library", $"{card.Name}因效果加入手牌"); }
        var remaining = item.Data["faction-search-top"].Split('|').Where(id => player.Library.Any(card => card.InstanceId == id)).ToList();
        PromptFactionSearchOrder(item, remaining);
    }

    private void PromptFactionSearchOrder(L12StackItem item, List<string> remaining)
    {
        if (remaining.Count <= 1) { CompleteFactionSearchOrder(item, remaining); return; }
        var player = State.Players[item.Controller];
        var data = new Dictionary<string, string>
        {
            ["action"] = "faction-search-order", ["placementMode"] = "all-bottom",
            ["displayCardIds"] = string.Join('|', remaining),
        };
        foreach (var id in remaining)
        {
            var card = player.Library.First(candidate => candidate.InstanceId == id);
            AddPromptCardData(data, card);
        }
        CreatePrompt(item.Controller, "order", "调整其余卡牌的顺序，然后全部放回牌库底部。",
            remaining, remaining.Count, remaining.Count, "card-effect", item.StackItemId, data: data);
    }

    private void CompleteFactionSearchOrder(L12StackItem item, List<string> order)
    {
        var player = State.Players[item.Controller]; foreach (var id in order) { var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == id); if (card is null) continue; player.Library.Remove(card); player.Library.Add(card); } FinishStackItem(item);
    }

    private void RecoverSunCard(L12StackItem item, string excluded)
    {
        var choices = State.Players[item.Controller].Graveyard.Where(card => CanEnterHandOrLibrary(card) && card.Faction == "taiyangcheng" && card.CardId != excluded).Select(card => card.InstanceId).ToList(); choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "选择墓地1张【太阳城】卡牌加入手牌", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "recover-asgard" });
    }

    private void ReturnEnemyFieldToLibraryBottom(int controller, string instanceId)
    {
        var enemy = State.Players[1 - controller];
        var card = FindOnField(enemy, instanceId, out _, out _);
        if (card is not null) MoveFieldCardToZone(enemy, card, "library-bottom", "返回牌库底部");
    }

    private void PromptFirstEmptySlot(L12StackItem item, string action, string text)
        => CreatePrompt(item.Controller, "slot", text, EmptySlots(State.Players[item.Controller]), 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = action });

    private void SummonFromAnyPrivateZone(L12PlayerState player, string instanceId, string slotChoice, bool tapped)
    {
        var fromHand = player.Hand.Any(candidate => candidate.InstanceId == instanceId);
        var fromLibrary = player.Library.Any(candidate => candidate.InstanceId == instanceId);
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == instanceId)
            ?? player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == instanceId)
            ?? player.Library.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return;
        player.Hand.Remove(card); player.Graveyard.Remove(card); player.Library.Remove(card);
        var (row, slot) = ParseSlot(slotChoice); card.Tapped = tapped; card.SummonRound = State.Round; player.Field[row][slot] = card;
        AddEvent("put", player.PlayerIndex, $"{card.Name}{(tapped ? "休整" : "活跃")}登场", card);
        ApplyDisasterLevelOnEntry(player.PlayerIndex, card, deferTriggerUntilStackSettles: true);
        if (fromHand)
        {
            if (HasImmediateEffect(card, "enter")) PushEffect(player.PlayerIndex, card, "enter", "【登场时】效果");
        }
        else QueueNonHandEntry(player.PlayerIndex, card, fromLibrary ? "library" : "graveyard");
    }

    private void MoveOwnCardToSlot(L12PlayerState player, string instanceId, string slotChoice)
    {
        var card = FindOnField(player, instanceId, out var row, out var slot); if (card is null) return; var (targetRow, targetSlot) = ParseSlot(slotChoice); player.Field[row][slot] = null; player.Field[targetRow][targetSlot] = card; card.LastMovedTurn = State.TurnSerial;
        NotifyS2LegionMoved(player.PlayerIndex, card, row, targetRow);
    }
}
