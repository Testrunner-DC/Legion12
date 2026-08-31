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
        "S01-0215" => GetAnkhSteleAbilityViews(),
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

    private static List<L12AbilityView> GetAnkhSteleAbilityViews()
    {
        if (!L12StructuredCardRules.TryGetStructuredAbilities("S01-0215", out var abilities)) return [];
        return abilities.Where(ability => ability.ExecutionModel == "granted-effect")
            .Select(ability => new
            {
                Ability = ability,
                RuntimeId = ability.Atoms.Select(atom => atom.Parameters.GetValueOrDefault("runtimeAbility"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            })
            .Where(entry => entry.RuntimeId is not null)
            .Select(entry => new L12AbilityView(entry.RuntimeId!, $"主动休整 选择此效果：{entry.Ability.Text}"))
            .ToList();
    }

    private bool TryResolveS1FactionEnter(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "图特摩斯三世":
                PromptEnemyByTroops(item, "thutmose-kill", "图特摩斯三世：击杀对方1张兵力不高于5000的军团", 5000, false); return true;
            case "拉美西斯二世":
            {
                var choices = PublicLegions(player).Where(target => target.Faction == "taiyangcheng"
                        && !target.Name.Equals(card.Name, StringComparison.Ordinal))
                    .Select(target => target.InstanceId).ToList();
                if (choices.Count == 0) { FinishStackItem(item); return true; }
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-targets", "拉美西斯二世：选择最多3张其他【太阳城】军团，依次发动其登场时效果", choices,
                    1, Math.Min(3, choices.Count - 1), "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "ramses-repeat" });
                return true;
            }
            case "陵墓构造体":
            {
                var guards = player.Graveyard.Where(candidate => candidate.CardId == "S01-0212").ToArray();
                foreach (var guard in guards) { player.Graveyard.Remove(guard); card.AttachedCards.Add(guard); }
                AddEvent("effect", item.Controller, $"陵墓构造体叠放{guards.Length}张陵墓守卫，兵力+{guards.Length * 1000}", card);
                RecalculateContinuousTroops();
                FinishStackItem(item); return true;
            }
            case "霍列姆赫布":
            {
                var guards = PublicLegions(player).Where(target => target.CardId == "S01-0212").Select(target => target.InstanceId).ToList();
                guards.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "霍列姆赫布：可弃置我方1张陵墓守卫，获得冲锋", guards, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "horemheb-charge" });
                return true;
            }
            case "图坦卡蒙":
            {
                if (PublicLegions(player).Count() >= PublicLegions(State.Players[1 - item.Controller]).Count()) { FinishStackItem(item); return true; }
                var declaredGuards = PublicTriggerDeclared(item, "entryCards")
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (declaredGuards.Length > 0)
                {
                    var slots = new[] { PublicTriggerDeclared(item, "entrySlot1"), PublicTriggerDeclared(item, "entrySlot2") };
                    for (var index = 0; index < declaredGuards.Length; index++)
                        if (EmptySlots(player).Contains(slots[index], StringComparer.OrdinalIgnoreCase))
                            SummonFromAnyPrivateZone(player, declaredGuards[index], slots[index], tapped: false);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            }
            case "阿伊":
                if (!string.IsNullOrWhiteSpace(PublicTriggerDeclared(item, "entryCard")))
                {
                    SummonFromAnyPrivateZone(player, PublicTriggerDeclared(item, "entryCard"),
                        PublicTriggerDeclared(item, "entrySlot"), tapped: true);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            case "纳芙蒂蒂":
                if (State.Players[1 - item.Controller].Hand.Count >= 6)
                    PromptDiscard(item, 1 - item.Controller, 1, "纳芙蒂蒂：对方弃置1张手牌", "nefertiti-discard");
                else FinishStackItem(item);
                return true;
            case "尼托克丽丝":
            {
                var guards = PublicLegions(player).Where(target => target.CardId == "S01-0212" && target.Tapped).Select(target => target.InstanceId).ToArray();
                if (guards.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target", "尼托克丽丝：选择我方1张陵墓守卫转为活跃", guards, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "nitocris-ready" });
                return true;
            }
            case "托勒密十三世":
            {
                var previousId = player.LastActiveTacticCardId;
                if (string.IsNullOrEmpty(previousId) || !_catalog.Cards.ContainsKey(previousId)) { FinishStackItem(item); return true; }
                var copy = CreateCard(previousId, $"repeat-{++State.StackSequence}"); player.Resolving.Add(copy);
                FinishStackItem(item); PushEffect(item.Controller, copy, "play", "托勒密十三世再次发动的主动战术效果"); return true;
            }
            case "安卡神碑":
                PromptOwnLegion(item, "ankh-enter", "安卡神碑：选择我方1张陵墓守卫，本回合兵力+2000", target => target.CardId == "S01-0212", false); return true;
            case "卡诺匹斯箱":
            {
                var choices = player.Library.Where(candidate => candidate.Name.Contains("卡诺匹斯罐", StringComparison.Ordinal)).Select(candidate => candidate.InstanceId).ToArray();
                if (choices.Length == 0) { ShuffleLibrary(player, "卡诺匹斯箱检索未命中"); HealMaster(item.Controller, 1, "卡诺匹斯箱"); DiscardRelic(player, card); FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "search", "卡诺匹斯箱：选择牌库中1张卡诺匹斯罐加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "canopic-search" });
                return true;
            }
            case "卡诺匹斯罐 一": PromptOwnLegion(item, "canopic-one", "卡诺匹斯罐一：选择我方1张【太阳城】军团，兵力+2000并获得强攻", target => target.Faction == "taiyangcheng", false); return true;
            case "卡诺匹斯罐 二": player.FreeTacticCount++; DiscardRelic(player, card); FinishStackItem(item); return true;
            case "卡诺匹斯罐 三": player.TemporaryMorale += 2; DiscardRelic(player, card); FinishStackItem(item); return true;
            case "卡诺匹斯罐 四":
            {
                var choices = PublicLegions(player).Where(target => target.Faction == "taiyangcheng").Select(target => target.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-targets", "卡诺匹斯罐四：选择我方最多2张【太阳城】军团获得免死", choices, 1, Math.Min(2, Math.Max(1, choices.Count - 1)),
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "canopic-four" });
                return true;
            }
            case "贝奥武夫":
                Mill(player, 2, "贝奥武夫"); FinishStackItem(item); return true;
            case "布伦希尔德":
            {
                if (!string.IsNullOrWhiteSpace(PublicTriggerDeclared(item, "entryCard")))
                {
                    SummonFromAnyPrivateZone(player, PublicTriggerDeclared(item, "entryCard"),
                        PublicTriggerDeclared(item, "entrySlot"), tapped: false);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            }
            case "神箭奥德尔":
                CreatePrompt(item.Controller, "optional", "神箭奥德尔：是否令我方主宰受到1点伤害并抽1张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "oddr-draw" }); return true;
            case "无骨者伊瓦尔": BeginFactionTopSearch(item, 3, "asgard", "S01-0315", "ivar-search"); return true;
            case "夺命诗人埃吉尔":
                CreatePrompt(item.Controller, "optional", "夺命诗人埃吉尔：是否令主宰受到1点伤害并弃置牌库顶部2张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "egil-pay" }); return true;
            case "神剑格拉墨":
                Mill(player, 2, "神剑格拉墨");
                PromptEnemyByTroops(item, "gram-bottom", "神剑格拉墨：选择对方1张兵力不高于3000的军团返回牌库底部",
                    3000, true, predicate: card => !L12SpecialDeckRules.IsDerivedSpecialCard(card));
                return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "杜阿特之门":
                CreatePrompt(item.Controller, "option", "杜阿特之门：选择击杀军团，或回收太阳城卡牌", ["kill", "recover"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "duat-mode", ["choiceMode"] = "instant",
                        ["kill"] = "击杀对方1张兵力不高于5000的军团。",
                        ["recover"] = "选择墓地最多1张〈杜阿特之门〉以外的【太阳城】卡牌加入手牌。",
                    }); return true;
            case "法老王的庆典": BeginPharaohFestival(item); return true;
            case "女武神的召唤":
            {
                var choices = player.Graveyard.Where(candidate => candidate.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, candidate, "asgard") && candidate.CurrentCost <= 5)
                    .Select(candidate => candidate.InstanceId).ToArray();
                if (choices.Length == 0 || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                if (player.Hp > 5) DamageMaster(item.Controller, 1, "女武神的召唤");
                CreatePrompt(item.Controller, "card", "女武神的召唤：选择墓地1张费用不高于5的【阿斯加德】军团活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "valkyrie-card" }); return true;
            }
            case "猎杀时刻":
                if (player.Graveyard.Count < 4) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "order", "猎杀时刻：选择墓地4张牌，依选择顺序返回牌库底部", player.Graveyard.Select(card => card.InstanceId), 4, 4,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "hunt-return" }); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionAttack(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "图特摩斯三世": ApplySunKingAttack(item); return true;
            case "美尼斯":
            {
                var choices = PublicLegions(player).Where(target => target.InstanceId != card.InstanceId).Select(target => target.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "美尼斯：可弃置我方战场1张军团，自身兵力+2000并获得强攻", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "menes-sacrifice" }); return true;
            }
            case "萨拉丁":
                if (!string.IsNullOrWhiteSpace(PublicTriggerDeclared(item, "moveTarget")))
                {
                    MoveOwnCardToSlot(player, PublicTriggerDeclared(item, "moveTarget"), PublicTriggerDeclared(item, "moveSlot"));
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            case "阿伊":
                if (ActiveResourceCount(player) < 1) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "阿伊：是否消耗1士气，使我方前排1张低兵力军团本回合兵力+2000？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "ay-pay" }); return true;
            case "贝奥武夫":
                CreatePrompt(item.Controller, "optional", "贝奥武夫：是否令我方主宰受到1点伤害，自身兵力+2000？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "beowulf-buff" }); return true;
            case "奥拉夫二世":
            {
                var choices = player.Graveyard.Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "奥拉夫二世：可将墓地1张牌置入牌库底部，获得强攻", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "olaf-strong" }); return true;
            }
            case "齐格鲁德": if (player.Relic?.CardId == "S01-0317" || player.ExtraRelics.Any(relic => relic.CardId == "S01-0317")) AddTimedModifier(card, 1000, 0, State.TurnSerial, "齐格鲁德"); FinishStackItem(item); return true;
            case "古斯塔夫一世":
                if (player.Graveyard.Count < 2) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "古斯塔夫一世：是否将墓地2张牌返回牌库底部，使自身本回合兵力+2000？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "gustav-attack-choice" }); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionDeath(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "图特摩斯三世": ApplySunKingAttack(item); return true;
            case "陵墓构造体":
                if (item.Data.TryGetValue("declaredCardIds", out var declaredGuards)
                    && item.Data.TryGetValue("declaredTargets", out var declaredGuardSlots))
                {
                    var guardIds = declaredGuards.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    var slots = declaredGuardSlots.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    for (var index = 0; index < Math.Min(guardIds.Length, slots.Length); index++)
                        SummonFromAnyPrivateZone(player, guardIds[index], slots[index], tapped: true);
                    FinishStackItem(item); return true;
                }
                var attachedIds = card.LastKnownAttachedCardIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                BeginQueuedSummons(item, player.Graveyard.Where(candidate => candidate.CardId == "S01-0212" && attachedIds.Contains(candidate.InstanceId)).Select(candidate => candidate.InstanceId), tapped: true,
                    "陵墓构造体：选择陵墓守卫休整登场的位置"); return true;
            case "萨拉丁":
                if (item.Data.TryGetValue("declaredTargets", out var saladinDeclared))
                {
                    var selected = saladinDeclared.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (selected.Length == 2) MoveOwnCardToSlot(player, selected[0], selected[1]);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            case "图坦卡蒙":
            {
                if (item.Data.TryGetValue("declaredTargets", out var declared))
                {
                    if (!string.IsNullOrWhiteSpace(declared)) MoveGraveToLibraryTop(player, declared);
                    FinishStackItem(item); return true;
                }
                var choices = player.Graveyard.Where(candidate => CanEnterHandOrLibrary(candidate) && candidate.CardId != "S01-0207" && candidate.Faction == "taiyangcheng" && candidate.CurrentCost <= 4)
                    .Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "图坦卡蒙阵亡：可将墓地1张费用不高于4的其他【太阳城】卡牌放回牌库顶部", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "tutankhamun-top" }); return true;
            }
            case "纳芙蒂蒂": if (player.Hand.Count < State.Players[1 - item.Controller].Hand.Count) { DamageMaster(1 - item.Controller, 1, "纳芙蒂蒂阵亡效果"); HealMaster(item.Controller, 1, "纳芙蒂蒂阵亡效果", legionEffect: true); } FinishStackItem(item); return true;
            case "尼托克丽丝":
            {
                if (item.Data.TryGetValue("declaredTargets", out var declared))
                {
                    var selected = declared.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (selected.Length == 2) SummonFromAnyPrivateZone(player, selected[0], selected[1], tapped: false);
                    FinishStackItem(item); return true;
                }
                var choices = player.Graveyard.Where(candidate => candidate.CardType == "legion" && candidate.Faction == "taiyangcheng" && candidate.CurrentCost <= 2)
                    .Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "尼托克丽丝阵亡：选择墓地1张费用不高于2的【太阳城】军团活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "nitocris-summon" }); return true;
            }
            case "传奇的拉格纳": CreatePrompt(item.Controller, "optional", "传奇的拉格纳阵亡：是否抽取1张并弃置1张？", ["yes", "no"], 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "death-cycle-one" }); return true;
            case "无情者哈拉尔":
                if (item.Data.TryGetValue("declaredTargets", out var haraldTarget))
                {
                    if (!string.IsNullOrWhiteSpace(haraldTarget)) KillTarget(item, haraldTarget, "被无情者哈拉尔阵亡效果击杀");
                    FinishStackItem(item); return true;
                }
                PromptEnemyByTroops(item, "harald-kill", "无情者哈拉尔阵亡：击杀对方1张兵力不高于2000的军团", 2000, false); return true;
            case "勇士比约恩":
                if (item.Data.TryGetValue("declaredGraveOrder", out var declaredGraveOrder)
                    && item.Data.TryGetValue("declaredSlot", out var declaredBjornSlot))
                {
                    var ordered = declaredGraveOrder.Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id))
                        .Where(card => card is not null).Cast<L12CardInstance>().ToArray();
                    if (ordered.Length == 4)
                    {
                        MoveGraveToLibraryBottom(player, ordered);
                        SummonFromAnyPrivateZone(player, card.InstanceId, declaredBjornSlot, tapped: true);
                    }
                    FinishStackItem(item); return true;
                }
                if (player.Graveyard.Count < 4 || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "勇士比约恩阵亡：是否令主宰受到1点伤害并将墓地4张牌返回牌库底部，使其休整登场？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "bjorn-revive-choice" }); return true;
            case "奥拉夫二世": CreatePrompt(item.Controller, "optional", "奥拉夫二世阵亡：是否抽2张牌并弃置1张？", ["yes", "no"], 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "death-cycle-two" }); return true;
            case "阿尔维达":
                if (item.Data.TryGetValue("declaredTargets", out var alvidaDeclared))
                {
                    if (!string.IsNullOrWhiteSpace(alvidaDeclared)) MoveGraveToHand(player, alvidaDeclared);
                    FinishStackItem(item); return true;
                }
                RecoverAsgard(item, 3, legionOnly: false); return true;
            case "血斧艾瑞克":
                if (item.Data.TryGetValue("declaredTargets", out var erikDeclared))
                {
                    var selected = erikDeclared.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (selected.Length == 2) SummonFromAnyPrivateZone(player, selected[0], selected[1], tapped: false);
                    FinishStackItem(item); return true;
                }
                SummonAsgardFromGrave(item, 3); return true;
            case "神箭奥德尔":
            {
                if (item.Data.TryGetValue("declaredTargets", out var declared))
                {
                    var target = FindOnField(State.Players[1 - item.Controller], declared, out _, out _);
                    if (target is not null && !target.Tapped)
                    {
                        target.Tapped = true;
                        AddEvent("effect", item.Controller, "神箭奥德尔将目标转为休整", card);
                    }
                    FinishStackItem(item); return true;
                }
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
            case "thutmose-kill": case "hunt-kill": case "harald-kill": if (chosen[0] != "skip") KillTarget(item, chosen[0], $"被{source?.Name}击杀"); FinishStackItem(item); return true;
            case "ramses-repeat":
            {
                var inheritsCounterProtection = source is not null
                    && L12StructuredCardRules.HasSummonTurnCounterTacticProtection(source, State.Round);
                FinishStackItem(item);
                foreach (var id in chosen.Where(id => id != "skip").Reverse())
                {
                    var target = FindOnField(player, id, out _, out _);
                    if (target is null || !HasImmediateEffect(target, "enter")) continue;
                    QueueOrPushTriggeredEffect(item.Controller, target, "enter", "拉美西斯二世再次发动的【登场时】效果",
                        data: inheritsCounterProtection
                            ? new Dictionary<string, string>
                            {
                                ["inheritedCounterTacticProtection"] = "true",
                                ["counterTacticProtectionSourceInstanceId"] = source!.InstanceId,
                            }
                            : null);
                }
                return true;
            }
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
            case "canopic-search":
            {
                var selected = player.Library.First(candidate => candidate.InstanceId == chosen[0]); player.Library.Remove(selected); AddCardToHandByEffect(player, selected, "library", $"{selected.Name}因效果加入手牌"); ShuffleLibrary(player, "卡诺匹斯箱检索结算");
                HealMaster(item.Controller, 1, "卡诺匹斯箱"); if (source is not null) DiscardRelic(player, source); FinishStackItem(item); return true;
            }
            case "canopic-one":
            {
                var target = FindOnField(player, chosen[0], out _, out _); if (target is not null) { AddTimedModifier(target, 2000, 0, State.TurnSerial, "卡诺匹斯罐 一"); GrantStrongAttack(target); }
                if (source is not null) DiscardRelic(player, source); FinishStackItem(item); return true;
            }
            case "canopic-four":
                foreach (var id in chosen.Where(id => id != "skip")) { var target = FindOnField(player, id, out _, out _); if (target is not null) GrantImmortalUntilNextTurnStart(target, item.Controller); }
                if (source is not null) DiscardRelic(player, source); FinishStackItem(item); return true;
            case "oddr-draw": if (chosen[0] == "yes") { DamageMaster(item.Controller, 1, "神箭奥德尔登场效果"); Draw(player, 1); } FinishStackItem(item); return true;
            case "egil-pay":
                if (chosen[0] == "no") { FinishStackItem(item); return true; }
                DamageMaster(item.Controller, 1, "夺命诗人埃吉尔效果"); Mill(player, 2, "夺命诗人埃吉尔"); PromptEnemyByTroops(item, "egil-debuff", "选择对方1张军团，本回合兵力-2000", int.MaxValue, false); return true;
            case "egil-debuff": { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) AddTimedModifier(target, -2000, 0, State.TurnSerial, "夺命诗人埃吉尔"); FinishStackItem(item); return true; }
            case "gram-bottom":
                if (chosen[0] != "skip") ReturnEnemyFieldToLibraryBottom(item.Controller, chosen[0]);
                FinishStackItem(item); return true;
            case "duat-mode":
                if (chosen[0] == "kill") PromptEnemyByTroops(item, "duat-kill", "杜阿特之门：击杀对方1张兵力不高于5000的军团", 5000, false);
                else RecoverSunCard(item, "S01-0221"); return true;
            case "duat-kill": if (chosen[0] != "skip") KillTarget(item, chosen[0], "被杜阿特之门击杀"); FinishStackItem(item); return true;
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
                    AddTimedModifier(source, 2000, 0, State.TurnSerial, "美尼斯");
                    GrantStrongAttack(source);
                }
                FinishStackItem(item); return true;
            case "ay-buff": { var target = FindOnField(player, chosen[0], out _, out _); if (target is not null) AddTimedModifier(target, 2000, 0, State.TurnSerial, "阿伊"); FinishStackItem(item); return true; }
            case "ay-pay":
                if (chosen[0] == "yes") BeginEffectMoralePayment(item, 1, "ay-buff"); else FinishStackItem(item); return true;
            case "beowulf-buff": if (chosen[0] == "yes" && source is not null) { DamageMaster(item.Controller, 1, "贝奥武夫进攻效果"); AddTimedModifier(source, 2000, 0, State.TurnSerial, "贝奥武夫"); } FinishStackItem(item); return true;
            case "olaf-strong":
                if (chosen[0] != "skip" && source is not null)
                {
                    var selected = player.Graveyard.FirstOrDefault(card => card.InstanceId == chosen[0]);
                    if (selected is not null) { MoveGraveToLibraryBottom(player, [selected]); GrantStrongAttack(source); }
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
                if (source is not null) AddTimedModifier(source, 2000, 0, State.TurnSerial, "古斯塔夫一世");
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
            case "queued-summon-slot": CompleteQueuedSummon(item, chosen[0]); return true;
            case "mengpo-silence": { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) target.SuppressDeathUntilTurn = State.TurnSerial; if (player.Hand.Count <= 5) Draw(player, 1); FinishStackItem(item); return true; }
            case "sun-bottom": ReturnEnemyFieldToLibraryBottom(item.Controller, chosen[0]); FinishStackItem(item); return true;
            case "medjed-debuff": { var target = FindOnField(enemy, chosen[0], out _, out _); if (target is not null) AddTimedModifier(target, -1000, 0, State.TurnSerial, "梅杰德"); FinishStackItem(item); return true; }
            default: return false;
        }
    }

    private CommandResult? TryBeginS1FactionActiveAbility(int playerIndex, L12CardInstance source, string ability)
    {
        if (!GetS1FactionAbilities(source.CardId).Any(entry => entry.Id == ability)) return null;
        var player = State.Players[playerIndex];
        var enemy = State.Players[1 - playerIndex];
        if (TryBeginPublicActiveDeclaration(playerIndex, source, ability) is { } publicDeclaration)
            return publicDeclaration;
        string[] choices;
        switch (ability)
        {
            case "ankhReady" or "ankhDraw" when source.CardId == "S01-0215" && source.Tapped:
                return CommandResult.Reject("安卡神碑已经休整，需先因其他效果转为活跃");
            case "olgaDebuff":
                choices = enemy.Field[0].Where(card => card is not null && IsFieldLegion(card) && !card.Hidden).Select(card => card!.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "奥尔加：选择对方前排 1 张军团");
            case "mengpoSilence":
                choices = PublicLegions(enemy).Select(card => card.InstanceId).ToArray();
                return BeginPendingActivation(playerIndex, source, ability, choices,
                    "孟婆：选择对方最多1张军团，本回合失去「阵亡时」效果", min: 0, max: 1);
            case "sunBottomEnemy":
                choices = PublicLegions(enemy).Where(card => card.Troops <= 4000
                        && !L12SpecialDeckRules.IsDerivedSpecialCard(card))
                    .Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "众神之乡：选择返回牌库底部的军团");
            case "ankhReady":
                if (!PublicLegions(player).Any(card => card.CardId == "S01-0212" && card.Tapped))
                    return CommandResult.Reject("需要我方存在休整的陵墓守卫");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "active-target", Text = "安卡神碑：选择我方1张休整的陵墓守卫",
                        ValidChoices = PublicLegions(player).Where(card => card.CardId == "S01-0212" && card.Tapped).Select(card => card.InstanceId).ToList(),
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "card", Text = "安卡神碑：选择弃置的1张手牌",
                        ValidChoices = player.Hand.Select(card => card.InstanceId).ToList(),
                    },
                ]);
            case "ankhDraw":
                choices = PublicLegions(player).Where(card => card.CardId == "S01-0212" && !card.Tapped).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "安卡神碑：选择转为休整的陵墓守卫");
            case "gramDamage":
                choices = player.Graveyard.Where(card => card.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, card, "asgard")).Select(card => card.InstanceId).ToArray();
                return BeginPendingActivation(playerIndex, source, ability, choices, "神剑格拉墨：依次选择4张【阿斯加德】军团返回牌库底部", 4, 4);
            case "valhallaKill":
            {
                var graveChoices = player.Graveyard.Where(CanEnterHandOrLibrary).Select(card => card.InstanceId).ToList();
                if (graveChoices.Count < 2) return CommandResult.Reject("墓地需要至少2张可返回牌库的卡牌");
                var lowTargets = PublicLegions(enemy).Where(card => card.Troops <= 1000).Select(card => card.InstanceId).ToList();
                var broadTargets = PublicLegions(enemy).Where(card => card.Troops <= 5000).Select(card => card.InstanceId).ToList();
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "card", Text = "英灵殿：依次选择墓地2张卡牌返回牌库底部",
                        ValidChoices = graveChoices, MinChoose = 2, MaxChoose = 2,
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "enemy-unselected-required", Text = "英灵殿：选择对方1张兵力不高于1000的军团",
                        ValidChoices = lowTargets.Count == 0 ? ["mode:none"] : lowTargets, MinChoose = 1, MaxChoose = 1,
                        ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["mode:none"] = "没有合法目标，继续结算",
                        },
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "enemy-unselected-required", Text = "英灵殿：选择另一张兵力不高于5000的军团",
                        ValidChoices = broadTargets.Count == 0 ? ["mode:none"] : broadTargets, MinChoose = 1, MaxChoose = 1,
                        ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["mode:none"] = "没有其他合法目标，继续结算",
                        },
                    },
                ]);
            }
            case "valkyrieRecover":
            {
                var graveChoices = player.Graveyard.Where(CanEnterHandOrLibrary).Select(card => card.InstanceId).ToList();
                if (graveChoices.Count < 2) return CommandResult.Reject("墓地需要至少2张可处理的卡牌");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "card", Text = "瓦尔基里：选择墓地2张牌",
                        ValidChoices = graveChoices, MinChoose = 2, MaxChoose = 2,
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "declared-card", Text = "瓦尔基里：选择其中1张加入手牌，另1张返回牌库底部",
                        ValidChoices = graveChoices, MinChoose = 1, MaxChoose = 1,
                    },
                ]);
            }
            case "medjedDebuff":
            {
                var targets = PublicLegions(enemy).Select(card => card.InstanceId).ToList();
                var strongGuards = PublicLegions(player)
                    .Where(card => card.CardId == "S01-0212" && !card.Tapped
                        && ActiveResourceCountExcluding(player, [card.InstanceId]) >= 1)
                    .Select(card => card.InstanceId).ToList();
                var modes = new List<string>();
                if (ActiveResourceCount(player) >= 1) modes.Add("mode:normal");
                if (strongGuards.Count > 0) modes.Add("mode:strong");
                if (modes.Count == 0) return CommandResult.Reject("需要1张可用资源；强模式还需另有1张活跃陵墓守卫");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "option", Text = "梅杰德：先声明本次兵力降低模式",
                        ValidChoices = modes, MinChoose = 1, MaxChoose = 1,
                        ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["mode:normal"] = "普通模式：目标本回合兵力-1000",
                            ["mode:strong"] = "强模式：额外休整1张活跃〈陵墓守卫〉，目标本回合兵力-3000",
                        },
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "active-target", Text = "梅杰德：选择强模式要休整的1张活跃〈陵墓守卫〉",
                        ValidChoices = strongGuards, MinChoose = 1, MaxChoose = 1,
                        RequiredDeclaredChoice = "mode:strong",
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "active-target", Text = "梅杰德：选择本回合降低兵力的对方军团",
                        ValidChoices = targets, MinChoose = 1, MaxChoose = 1,
                    },
                ]);
            }
            case "amaterasuKill":
                choices = PublicLegions(enemy).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "天照大神：选择本回合费用 -1 的军团");
            default:
                return CommitActiveAbility(playerIndex, source, ability, null);
        }
    }

    private CommandResult? TryCommitS1FactionActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey, bool? useTombGuards,
        bool returnMoralePrepaid = false)
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
            case "cleopatraGuard" when source.CardId == "S01-0214":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var guard = declared.Length == 3
                    ? player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[0] && card.CardId == "S01-0212")
                    : null;
                var battlefield = declared.Length == 3 ? ParseEffectEntryBattlefieldChoice(declared[1]) : null;
                var (row, slot) = declared.Length == 3 ? ParseSlot(declared[2]) : (-1, -1);
                if (source.Tapped || guard is null || battlefield != playerIndex
                    || row is < 0 or > 1 || slot is < 0 or > 2 || player.Field[row][slot] is not null)
                    return CommandResult.Reject("需要活跃的克利奥帕特拉七世及完整、合法的陵墓守卫登场声明");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气");
                source.Tapped = true;
                break;
            }
            case "sunGuard" when source.CardId == "S01-02C1":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var guard = declared.Length == 3
                    ? player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[0] && card.CardId == "S01-0212")
                    : null;
                var battlefield = declared.Length == 3 ? ParseEffectEntryBattlefieldChoice(declared[1]) : null;
                var (row, slot) = declared.Length == 3 ? ParseSlot(declared[2]) : (-1, -1);
                if (guard is null || battlefield != playerIndex || row is < 0 or > 1 || slot is < 0 or > 2
                    || player.Field[row][slot] is not null)
                    return CommandResult.Reject("不朽之礼的陵墓守卫或登场位置已失效");
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气");
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "sunDraw" when source.CardId == "S01-02C1": if (player.Hand.Count > 3 || !ConsumeMorale(1)) return CommandResult.Reject("手牌需不高于3张，且需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "asgardDraw" when source.CardId == "S01-03C1":
            {
                var mode = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).SingleOrDefault();
                var cost = mode == "mode:heal" ? 3 : 2;
                if (mode is not ("mode:none" or "mode:heal") || !ConsumeMorale(cost))
                    return CommandResult.Reject($"需要完整声明模式并消耗{cost}张活跃士气");
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "alvidaSummon" when source.CardId == "S01-0307":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var legion = declared.Length == 3
                    ? player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]
                        && card.CardType == "legion" && card.DisasterLevel == 2)
                    : null;
                var battlefield = declared.Length == 3 ? ParseEffectEntryBattlefieldChoice(declared[1]) : null;
                var (row, slot) = declared.Length == 3 ? ParseSlot(declared[2]) : (-1, -1);
                if (legion is null || battlefield != playerIndex || row is < 0 or > 1 || slot is < 0 or > 2
                    || player.Field[row][slot] is not null)
                    return CommandResult.Reject("阿尔维达声明的军团或登场位置已失效");
                RemoveFromField(player, source, true, "被主动效果弃置", leaveKind: L12FieldLeaveKind.Discard);
                break;
            }
            case "olgaDebuff" when source.CardId == "S01-0314":
                if (!IsEnemyTargetLegal(playerIndex, target, card => FindOnField(State.Players[1 - playerIndex], card.InstanceId, out var row, out _) is not null && row == 0)) return CommandResult.Reject("目标不再合法");
                RemoveFromField(player, source, true, "被主动效果弃置", leaveKind: L12FieldLeaveKind.Discard); break;
            case "gramReady" when source.CardId == "S01-0317": if (!source.Tapped || !ConsumeMorale(2)) return CommandResult.Reject("神剑格拉墨需为休整，且需要2张活跃士气"); break;
            case "palaceReward" when source.CardId == "S01-01D1": if (player.ReturnedMoraleThisTurn <= 1) return CommandResult.Reject("本回合返还士气需高于1张"); player.UsedAbilities.Add(onceKey); break;
            case "palaceExchange" when source.CardId == "S01-01D1":
            {
                var values = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var enemyId = PublicDeclaredEnemyId(target);
                var declared = DeclaredEnemyTarget(playerIndex, enemyId);
                var noRevive = values.Length == 2 && values[0] == "mode:none";
                var revive = values.Length == 4
                    ? player.Graveyard.FirstOrDefault(card => card.InstanceId == values[0]
                        && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "tianting"))
                    : null;
                var battlefield = revive is not null ? ParseEffectEntryBattlefieldChoice(values[1]) : null;
                var (row, slot) = revive is not null ? ParseSlot(values[2]) : (-1, -1);
                if (source.Tapped || declared is null || !noRevive && (revive is null
                        || revive.CurrentCost > declared.CurrentCost || battlefield != playerIndex
                        || row is < 0 or > 1 || slot is < 0 or > 2 || player.Field[row][slot] is not null))
                    return CommandResult.Reject("凌霄宝殿的敌方目标或登场声明已失效");
                var paid = declared.CurrentCost;
                if (!returnMoralePrepaid && !ReturnMorale(player, paid))
                    return CommandResult.Reject("士气不足以支付所选目标费用");
                source.Tapped = true;
                player.MasterTapped = true;
                break;
            }
            case "mengpoSilence" when source.CardId == "S01-01M2":
                if (!string.IsNullOrWhiteSpace(target) && DeclaredEnemyTarget(playerIndex, target) is null)
                    return CommandResult.Reject("目标不再合法");
                if (!returnMoralePrepaid && !ReturnMorale(player, 1)) return CommandResult.Reject("需要返还1张士气"); player.UsedAbilities.Add(onceKey); break;
            case "mengpoMorale" when source.CardId == "S01-01M2":
            {
                var discard = player.Hand.FirstOrDefault(card => card.InstanceId == target);
                if (player.Morale.Count >= State.Players[1 - playerIndex].Morale.Count || discard is null)
                    return CommandResult.Reject("士气需少于对方，且声明的弃牌费用必须保持合法");
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false);
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "sunTopThree" or "sunBottomEnemy" when source.CardId == "S01-02D1":
                if (ability == "sunTopThree" && target != "mode:none"
                    && !player.Graveyard.Any(card => card.InstanceId == target && card.Faction == "taiyangcheng"
                        && CanEnterHandOrLibrary(card)))
                    return CommandResult.Reject("众神之乡声明的墓地回收目标已失效");
                if (ability == "sunBottomEnemy" && DeclaredEnemyTarget(playerIndex, target,
                        card => card.Troops <= 4000 && !L12SpecialDeckRules.IsDerivedSpecialCard(card)) is null)
                    return CommandResult.Reject("目标不再合法");
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "ankhReady" when source.CardId == "S01-0215":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var guard = declared.Length == 2 ? PublicLegions(player).FirstOrDefault(card => card.InstanceId == declared[0]
                    && card.CardId == "S01-0212" && card.Tapped) : null;
                var discard = declared.Length == 2 ? player.Hand.FirstOrDefault(card => card.InstanceId == declared[1]) : null;
                if (source.Tapped || guard is null || discard is null) return CommandResult.Reject("安卡神碑的目标或弃牌费用已不合法");
                source.Tapped = true;
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false);
                break;
            }
            case "ankhDraw" when source.CardId == "S01-0215":
            {
                var guard = PublicLegions(player).FirstOrDefault(card => card.InstanceId == target && card.CardId == "S01-0212" && !card.Tapped);
                if (source.Tapped || guard is null) return CommandResult.Reject("需要活跃的安卡神碑与陵墓守卫");
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
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var guards = declared.Take(3).Select(id => PublicLegions(player)
                    .FirstOrDefault(card => card.InstanceId == id && card.CardId == "S01-0212")).ToArray();
                var canopic = declared.Length == 5 ? player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[3]
                    && card.Name.Contains("卡诺匹斯", StringComparison.Ordinal) && card.CardType == "artifact"
                    && !player.SpecialZones.CanopicProgress.Any(done => done.CardId == card.CardId)) : null;
                if (declared.Length != 5 || guards.Length != 3 || guards.Any(card => card is null)
                    || declared.Take(3).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3 || canopic is null
                    || declared[4] is not ("mode:draw" or "mode:heal"))
                    return CommandResult.Reject("伊西斯声明的陵墓守卫、卡诺匹斯圣物或奖励模式已失效");
                foreach (var guard in guards)
                    RemoveFromField(player, guard!, true, "作为伊西斯主宰效果的发动费用弃置",
                        leaveKind: L12FieldLeaveKind.Discard);
                break;
            }
            case "medjedDebuff" when source.CardId == "S01-02M3":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var strong = declared.FirstOrDefault() == "mode:strong";
                var targetId = declared.LastOrDefault();
                var enemyTarget = DeclaredEnemyTarget(playerIndex, targetId);
                var guard = strong && declared.Length == 3
                    ? PublicLegions(player).FirstOrDefault(card => card.InstanceId == declared[1]
                        && card.CardId == "S01-0212" && !card.Tapped)
                    : null;
                if (declared.FirstOrDefault() is not ("mode:normal" or "mode:strong") || enemyTarget is null
                    || strong && guard is null)
                    return CommandResult.Reject("梅杰德声明的模式、陵墓守卫或目标不再合法");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张可用资源");
                if (guard is not null) guard.Tapped = true;
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "valhallaDiscount" when source.CardId == "S01-03D1": if (player.Hp <= 1) return CommandResult.Reject("主宰血量不足"); DamageMaster(playerIndex, 1, "英灵殿费用减免"); player.UsedAbilities.Add(onceKey); break;
            case "valhallaRecover" when source.CardId == "S01-03D1": if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "valhallaKill" when source.CardId == "S01-03D1":
            {
                var ids = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (source.Tapped || ids.Length != 4) return CommandResult.Reject("英灵殿需为活跃并完成全部对象声明");
                var graveCards = ids.Take(2).Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id && CanEnterHandOrLibrary(card))).ToArray();
                if (graveCards.Any(card => card is null) || graveCards.Select(card => card!.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
                    return CommandResult.Reject("需要选择墓地2张不同的合法卡牌");
                var lowTarget = ids[2] == "mode:none" ? null : DeclaredEnemyTarget(playerIndex, ids[2], card => card.Troops <= 1000);
                var broadTarget = ids[3] == "mode:none" ? null : DeclaredEnemyTarget(playerIndex, ids[3], card => card.Troops <= 5000);
                if (ids[2] != "mode:none" && lowTarget is null || ids[3] != "mode:none" && broadTarget is null
                    || lowTarget is not null && broadTarget?.InstanceId == lowTarget.InstanceId)
                    return CommandResult.Reject("英灵殿声明的击杀目标不再合法");
                if (lowTarget is null && PublicLegions(State.Players[1 - playerIndex]).Any(card => card.Troops <= 1000)
                    || broadTarget is null && PublicLegions(State.Players[1 - playerIndex]).Any(card => card.Troops <= 5000 && card.InstanceId != lowTarget?.InstanceId))
                    return CommandResult.Reject("仍存在必须选择的合法击杀目标");
                source.Tapped = true;
                player.MasterTapped = true;
                MoveGraveToLibraryBottom(player, graveCards.Cast<L12CardInstance>());
                break;
            }
            case "valkyrieRecover" when source.CardId == "S01-03M1":
            {
                var ids = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (ids.Length != 3 || player.Hp <= 1 || ActiveResourceCount(player) < 1)
                    return CommandResult.Reject("需要完成墓地选择、1张活跃士气且主宰血量需高于1");
                var pair = ids.Take(2).Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id && CanEnterHandOrLibrary(card))).ToArray();
                if (pair.Any(card => card is null) || pair.Select(card => card!.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
                    || !ids.Take(2).Contains(ids[2], StringComparer.OrdinalIgnoreCase))
                    return CommandResult.Reject("瓦尔基里声明的墓地卡牌不再合法");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气");
                DamageMaster(playerIndex, 1, "瓦尔基里主宰效果");
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "lokiCycle" when source.CardId == "S01-03M2":
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            case "lokiHeal" when source.CardId == "S01-03M2":
            {
                var ids = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (ids.Length != 2 || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
                    || ids.Any(id => !player.Graveyard.Any(card => card.InstanceId == id && CanEnterHandOrLibrary(card))))
                    return CommandResult.Reject("洛基声明的2张墓地卡牌已失效");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气");
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "yomiDiscount" when source.CardId == "S01-04D1": player.UsedAbilities.Add(onceKey); break;
            case "yomiSweep" when source.CardId == "S01-04D1":
            {
                var ids = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                bool ValidTarget(string id, int maximum) => id == "mode:none"
                    || DeclaredEnemyTarget(playerIndex, id, card => card.CurrentCost - 1 <= maximum) is not null;
                if (ids.Length != 2 || !ValidTarget(ids[0], 3) || !ValidTarget(ids[1], 1)
                    || ids[0] != "mode:none" && ids[0].Equals(ids[1], StringComparison.OrdinalIgnoreCase))
                    return CommandResult.Reject("黄泉之门的公开击杀目标声明已失效");
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气");
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "yomiRecover" when source.CardId == "S01-04D1":
                if (source.Tapped || string.IsNullOrWhiteSpace(target)
                    || !player.Graveyard.Any(card => card.InstanceId == target && card.Faction == "gaotianyuan"))
                    return CommandResult.Reject("黄泉之门必须为活跃状态且墓地目标需保持合法");
                source.Tapped = true;
                player.MasterTapped = true;
                break;
            case "amaterasuKill" when source.CardId == "S01-04M1":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var debuff = declared.Length == 2 ? DeclaredEnemyTarget(playerIndex, declared[0]) : null;
                var kill = declared.Length == 2 && declared[1] != "mode:none"
                    ? DeclaredEnemyTarget(playerIndex, declared[1], card => card.CurrentCost
                        - (card.InstanceId == debuff?.InstanceId ? 1 : 0) == 0)
                    : null;
                if (debuff is null || declared[1] != "mode:none" && kill is null)
                    return CommandResult.Reject("天照大神声明的费用降低或击杀目标已失效");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            }
            case "amaterasuReady" when source.CardId == "S01-04M1":
            {
                var discard = player.Hand.FirstOrDefault(card => card.InstanceId == target);
                if (discard is null) return CommandResult.Reject("声明的弃牌费用已失效");
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false);
                player.UsedAbilities.Add(onceKey);
                break;
            }
            default: return null;
        }
        var data = new Dictionary<string, string> { ["ability"] = ability };
        if (!string.IsNullOrWhiteSpace(target)) data["target"] = target;
        if (ability == "palaceExchange" && DeclaredEnemyTarget(playerIndex, PublicDeclaredEnemyId(target)) is { } palaceTarget)
        {
            var values = target!.Split('|', StringSplitOptions.RemoveEmptyEntries);
            data["target"] = palaceTarget.InstanceId;
            data["paid"] = palaceTarget.CurrentCost.ToString();
            if (values.Length == 4)
            {
                data["entryCard"] = values[0];
                data["entryBattlefield"] = values[1];
                data["entrySlot"] = values[2];
            }
        }
        if (ability == "yomiSweep" && target is not null)
        {
            var values = target.Split('|', StringSplitOptions.RemoveEmptyEntries);
            data["compositePlan"] = "active:S01-04D1:yomiSweep";
            data["compositeSegment"] = "0";
            data["atomicFlow"] = "yomi-draw";
            data["atomicContinuation"] = "true";
            data["declared:kill3Target"] = values[0];
            data["declared:kill1Target"] = values[1];
        }
        PushEffect(playerIndex, source, "active", "主动效果", data: data); return CommandResult.Ok();
    }

    private bool TryResolveS1FactionActive(L12StackItem item, L12CardInstance? source, string ability)
    {
        var player = State.Players[item.Controller];
        switch (ability)
        {
            case "cleopatraGuard":
            {
                var declared = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (declared.Length == 3 && ParseEffectEntryBattlefieldChoice(declared[1]) == item.Controller)
                    SummonFromAnyPrivateZone(player, declared[0], declared[2], tapped: false);
                FinishStackItem(item);
                return true;
            }
            case "sunGuard":
            {
                var declared = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (declared.Length == 3 && ParseEffectEntryBattlefieldChoice(declared[1]) == item.Controller)
                    SummonFromAnyPrivateZone(player, declared[0], declared[2], tapped: false);
                FinishStackItem(item);
                return true;
            }
            case "sunDraw": Draw(player, 1); FinishStackItem(item); return true;
            case "asgardDraw":
                Draw(player, 1);
                if (item.Data.GetValueOrDefault("target") == "mode:heal") HealMaster(item.Controller, 1, "阿斯加德阵营效果");
                FinishStackItem(item);
                return true;
            case "alvidaSummon":
            {
                var declared = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                DamageMaster(item.Controller, 1, "阿尔维达主动效果");
                if (declared.Length == 3 && ParseEffectEntryBattlefieldChoice(declared[1]) == item.Controller)
                    SummonFromAnyPrivateZone(player, declared[0], declared[2], tapped: false);
                FinishStackItem(item);
                return true;
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
            case "mengpoMorale": AddMorale(player, 1, true); FinishStackItem(item); return true;
            case "sunTopThree": BeginFactionTopSearch(item, 3, "taiyangcheng", string.Empty, "sun-divinity"); return true;
            case "sunBottomEnemy": ReturnEnemyFieldToLibraryBottom(item.Controller, item.Data.GetValueOrDefault("target") ?? string.Empty); FinishStackItem(item); return true;
            case "ankhReady":
            {
                var guardId = item.Data.GetValueOrDefault("target", string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                var guard = PublicLegions(player).FirstOrDefault(card => card.InstanceId == guardId && card.CardId == "S01-0212" && card.Tapped);
                if (guard is not null) ReadyCardByEffect(item.Controller, source ?? guard, guard, $"{guard.Name}因安卡神碑转为活跃");
                FinishStackItem(item);
                return true;
            }
            case "ankhDraw": Draw(player, 1); FinishStackItem(item); return true;
            case "gramDamage": DamageMasterNonLethal(1 - item.Controller, 1, "神剑格拉墨"); FinishStackItem(item); return true;
            case "isisCanopic":
            {
                var declared = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var canopic = declared.Length == 5 ? player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[3]
                    && card.Name.Contains("卡诺匹斯", StringComparison.Ordinal) && card.CardType == "artifact"
                    && !player.SpecialZones.CanopicProgress.Any(done => done.CardId == card.CardId)) : null;
                if (canopic is null)
                {
                    AddEvent("effect-cancelled", item.Controller, "伊西斯声明的卡诺匹斯圣物已失效");
                    FinishStackItem(item);
                    return true;
                }
                player.Graveyard.Remove(canopic);
                ResetCardAfterLeavingField(canopic);
                player.SpecialZones.CanopicProgress.Add(canopic);
                if (declared[4] == "mode:draw") Draw(player, 1);
                else HealMaster(item.Controller, 1, "伊西斯");
                FinishStackItem(item);
                return true;
            }
            case "medjedDebuff":
            {
                var declared = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var target = DeclaredEnemyTarget(item.Controller, declared.LastOrDefault());
                if (target is not null)
                    AddTimedModifier(target, declared.FirstOrDefault() == "mode:strong" ? -3000 : -1000,
                        0, State.TurnSerial, "梅杰德");
                FinishStackItem(item);
                return true;
            }
            case "valhallaDiscount": player.NextFactionLegionDiscount = Math.Max(player.NextFactionLegionDiscount, 1); FinishStackItem(item); return true;
            case "valhallaRecover": Mill(player, 2, "英灵殿"); RecoverAsgard(item, int.MaxValue, false); return true;
            case "valhallaKill":
            {
                var ids = item.Data.GetValueOrDefault("target")?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (ids.Length == 4)
                {
                    if (ids[2] != "mode:none") KillTarget(item, ids[2], "被英灵殿击杀");
                    if (ids[3] != "mode:none") KillTarget(item, ids[3], "被英灵殿击杀");
                }
                FinishStackItem(item); return true;
            }
            case "valkyrieRecover":
            {
                var ids = item.Data.GetValueOrDefault("target")?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (ids.Length != 3) { FinishStackItem(item); return true; }
                var handCard = player.Graveyard.FirstOrDefault(card => card.InstanceId == ids[2]);
                var bottomCard = player.Graveyard.FirstOrDefault(card => ids.Take(2).Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase)
                    && card.InstanceId != ids[2]);
                if (handCard is not null)
                {
                    player.Graveyard.Remove(handCard);
                    AddCardToHandByEffect(player, handCard, "graveyard", $"{handCard.Name}因瓦尔基里效果加入手牌");
                }
                if (bottomCard is not null) MoveGraveToLibraryBottom(player, [bottomCard]);
                FinishStackItem(item); return true;
            }
            case "lokiCycle": Draw(player, 1); PromptDiscard(item, item.Controller, 1, "洛基：弃置1张手牌", "death-cycle-discard"); return true;
            case "lokiHeal":
            {
                var ids = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var cards = ids.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id
                        && CanEnterHandOrLibrary(card)))
                    .Where(card => card is not null).Cast<L12CardInstance>().ToArray();
                if (cards.Length == 2)
                {
                    MoveGraveToLibraryBottom(player, cards);
                    HealMaster(item.Controller, 1, "洛基主宰效果");
                }
                FinishStackItem(item);
                return true;
            }
            case "yomiDiscount": player.NextFactionLegionDiscount = 2; FinishStackItem(item); return true;
            case "yomiSweep":
            {
                switch (item.Data.GetValueOrDefault("atomicFlow"))
                {
                    case "yomi-draw":
                        Draw(player, 1);
                        break;
                    case "yomi-cost-debuff":
                        foreach (var enemyTarget in PublicLegions(State.Players[1 - item.Controller]))
                            enemyTarget.CostModifier--;
                        break;
                    case "yomi-kill3":
                    {
                        var targetId = CompositeDeclared(item, "kill3Target").SingleOrDefault();
                        if (targetId != "mode:none"
                            && DeclaredEnemyTarget(item.Controller, targetId, card => card.CurrentCost <= 3) is not null)
                            KillTarget(item, targetId!, "被黄泉之门击杀");
                        break;
                    }
                    case "yomi-kill1":
                    {
                        var targetId = CompositeDeclared(item, "kill1Target").SingleOrDefault();
                        if (targetId != "mode:none"
                            && DeclaredEnemyTarget(item.Controller, targetId, card => card.CurrentCost <= 1) is not null)
                            KillTarget(item, targetId!, "被黄泉之门击杀");
                        break;
                    }
                }
                FinishStackItem(item);
                return true;
            }
            case "yomiRecover":
            {
                var targetId = item.Data.GetValueOrDefault("target");
                var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == targetId
                    && card.Faction == "gaotianyuan");
                if (recover is not null)
                {
                    player.Graveyard.Remove(recover);
                    AddCardToHandByEffect(player, recover, "graveyard", "黄泉之门回收高天原卡牌");
                }
                FinishStackItem(item);
                return true;
            }
            case "amaterasuKill":
            {
                var declared = item.Data.GetValueOrDefault("target", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var debuff = declared.Length == 2 ? DeclaredEnemyTarget(item.Controller, declared[0]) : null;
                if (debuff is not null) debuff.CostModifier--;
                if (declared.Length == 2 && declared[1] != "mode:none"
                    && DeclaredEnemyTarget(item.Controller, declared[1], card => card.CurrentCost == 0) is not null)
                    KillTarget(item, declared[1], "被天照大神击杀");
                FinishStackItem(item);
                return true;
            }
            case "amaterasuReady":
                if (source is not null)
                    foreach (var morale in player.Morale.Where(card => card.Tapped).Take(2).ToArray())
                        ReadyMoraleByEffect(item.Controller, source, morale, "士气因效果转为活跃");
                foreach (var legion in player.Field[0].Where(card => card?.Faction == "gaotianyuan").Cast<L12CardInstance>())
                    AddTimedModifier(legion, 1000, 0, State.TurnSerial, "天照大神");
                FinishStackItem(item);
                return true;
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
        foreach (var target in PublicLegions(State.Players[1 - item.Controller]))
            AddTimedModifier(target, -1000, 0, State.TurnSerial, "图特摩斯三世");
        PromptEnemyByTroops(item, "thutmose-kill", "选择对方1张兵力不高于1000的军团并击杀", 1000, true);
    }

    private void PromptEnemyByTroops(L12StackItem item, string action, string text, int maxTroops, bool optional,
        int? row = null, Func<L12CardInstance, bool>? predicate = null)
    {
        var enemy = State.Players[1 - item.Controller];
        var choices = PublicLegions(enemy).Where(target => target.Troops <= maxTroops
                && (predicate?.Invoke(target) ?? true)
                && (row is null || FindOnField(enemy, target.InstanceId, out var targetRow, out _) is not null && targetRow == row))
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
        var guardId = PublicTriggerDeclared(item, "entryCard");
        var destination = PublicTriggerDeclared(item, "entrySlot");
        if (player.MasterId != "S01-02M3"
            || !player.UsedAbilities.Contains("trigger:medjedDamageResponse")
            || !player.Graveyard.Any(card => card.InstanceId == guardId && card.CardId == "S01-0212")
            || !EmptySlots(player).Contains(destination, StringComparer.OrdinalIgnoreCase))
        {
            AddEvent("effect-cancelled", item.Controller, "梅杰德声明的陵墓守卫或登场位置已失效；本段取消");
            FinishStackItem(item);
            return;
        }
        SummonFromAnyPrivateZone(player, guardId, destination, tapped: false);
        FinishStackItem(item);
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
        var owner = CardOwner(relic, player);
        ResetCardAfterLeavingField(relic);
        if (L12SpecialDeckRules.VanishesWhenLeavingField(relic))
            AddEvent("derived-vanished", owner.PlayerIndex,
                $"衍生卡〈{relic.Name}〉离开圣物区时消灭，不进入其他区域", relic);
        else if (!owner.Graveyard.Contains(relic))
            owner.Graveyard.Add(relic);
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
        var player = State.Players[item.Controller];
        foreach (var id in order)
        {
            var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == id);
            if (card is null) continue;
            player.Library.Remove(card);
            player.Library.Add(card);
        }
        if (item.Data.GetValueOrDefault("ability") == "sunTopThree")
        {
            var targetId = item.Data.GetValueOrDefault("target");
            if (targetId != "mode:none")
            {
                var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == targetId
                    && card.Faction == "taiyangcheng" && CanEnterHandOrLibrary(card));
                if (recover is null)
                    AddEvent("effect-cancelled", item.Controller, "众神之乡声明的墓地回收目标已失效");
                else
                {
                    player.Graveyard.Remove(recover);
                    AddCardToHandByEffect(player, recover, "graveyard", "众神之乡回收太阳城卡牌");
                }
            }
        }
        FinishStackItem(item);
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
        if (card is not null && !L12SpecialDeckRules.IsDerivedSpecialCard(card))
            MoveFieldCardToZone(enemy, card, "library-bottom", "返回牌库底部");
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
        player.Hand.Remove(card); player.Graveyard.Remove(card); player.Library.Remove(card); card.OwnerIndex ??= player.PlayerIndex;
        var (row, slot) = ParseSlot(slotChoice); card.Tapped = tapped; card.SummonRound = State.Round; player.Field[row][slot] = card;
        AddEvent("put", player.PlayerIndex, $"{card.Name}{(tapped ? "休整" : "活跃")}登场", card);
        ApplyDisasterLevelOnEntry(player.PlayerIndex, card, deferTriggerUntilStackSettles: true);
        if (fromHand)
        {
            if (HasImmediateEffect(card, "enter"))
                QueueOrPushTriggeredEffect(player.PlayerIndex, card, "enter", "【登场时】效果");
            QueueS2GrailRoundTableEntry(player.PlayerIndex, card);
        }
        else QueueNonHandEntry(player.PlayerIndex, card, fromLibrary ? "library" : "graveyard");
    }

    private void MoveOwnCardToSlot(L12PlayerState player, string instanceId, string slotChoice)
    {
        var card = FindOnField(player, instanceId, out var row, out var slot); if (card is null) return; var (targetRow, targetSlot) = ParseSlot(slotChoice); player.Field[row][slot] = null; player.Field[targetRow][targetSlot] = card; card.LastMovedTurn = State.TurnSerial;
        RecordLegionMovement(player.PlayerIndex, card, row, targetRow);
    }
}
