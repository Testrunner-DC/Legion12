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
                if (guards.Count == 0) { FinishStackItem(item); return true; }
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
                    _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex,
                        PublicTriggerDeclared(item, "entryCard"), PublicTriggerDeclared(item, "entrySlot"),
                        tapped: true);
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
                item.Data["postResolutionGenerated"] = "ptolemy-repeat";
                item.Data["repeatCardId"] = previousId;
                FinishStackItem(item);
                return true;
            }
            case "安卡神碑":
                PromptOwnLegion(item, "ankh-enter", "安卡神碑：选择我方1张陵墓守卫，本回合兵力+2000", target => target.CardId == "S01-0212", false); return true;
            case "canopic-box-search":
            case "卡诺匹斯箱":
            {
                var choices = player.Library.Where(candidate => candidate.Name.Contains("卡诺匹斯罐", StringComparison.Ordinal)).Select(candidate => candidate.InstanceId).ToArray();
                if (choices.Length == 0) { ShuffleLibrary(player, "卡诺匹斯箱检索未命中"); FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "search", "卡诺匹斯箱：选择牌库中1张卡诺匹斯罐加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "canopic-search" });
                return true;
            }
            case "canopic-box-heal-discard":
                HealMaster(item.Controller, 1, "卡诺匹斯箱");
                if (FindAuthoritativeCard(item.SourceInstanceId) is { } box) DiscardRelic(player, box);
                FinishStackItem(item); return true;
            case "卡诺匹斯罐 一": PromptOwnLegion(item, "canopic-one", "卡诺匹斯罐一：选择我方1张【太阳城】军团，兵力+2000并获得强攻", target => target.Faction == "taiyangcheng", false); return true;
            case "canopic-two-free": player.FreeTacticCount++; FinishStackItem(item); return true;
            case "canopic-two-discard":
            case "canopic-three-discard":
                if (FindAuthoritativeCard(item.SourceInstanceId) is { } canopic) DiscardRelic(player, canopic);
                FinishStackItem(item); return true;
            case "canopic-three-morale": player.TemporaryMorale += 2; FinishStackItem(item); return true;
            case "卡诺匹斯罐 二": player.FreeTacticCount++; DiscardRelic(player, card); FinishStackItem(item); return true;
            case "卡诺匹斯罐 三": player.TemporaryMorale += 2; DiscardRelic(player, card); FinishStackItem(item); return true;
            case "卡诺匹斯罐 四":
            {
                var choices = PublicLegions(player).Where(target => target.Faction == "taiyangcheng").Select(target => target.InstanceId).ToList();
                if (choices.Count == 0)
                {
                    DiscardRelic(player, card);
                    FinishStackItem(item);
                    return true;
                }
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
                    _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex,
                        PublicTriggerDeclared(item, "entryCard"), PublicTriggerDeclared(item, "entrySlot"),
                        tapped: false);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            }
            case "神箭奥德尔":
                CreatePrompt(item.Controller, "optional", "神箭奥德尔：是否令我方主宰受到1点伤害并抽1张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "oddr-draw" }); return true;
            case "无骨者伊瓦尔":
                if (PublicTriggerDeclared(item, "mode") == "mode:use")
                    BeginFactionTopSearch(item, 3, "asgard", "S01-0315", "ivar-search");
                else FinishStackItem(item);
                return true;
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
            case "duat-effect":
            {
                var mode = CompositeDeclared(item, "duatMode").SingleOrDefault();
                if (mode == "mode:kill")
                {
                    var targetId = CompositeDeclared(item, "killTarget").SingleOrDefault();
                    if (DeclaredEnemyTarget(item.Controller, targetId, target => target.Troops <= 5000) is not null)
                        KillTarget(item, targetId!, "被杜阿特之门击杀");
                }
                else
                {
                    var targetId = CompositeDeclared(item, "recoverTarget").SingleOrDefault();
                    var target = player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == targetId
                        && candidate.CardId != card.CardId && CanEnterHandOrLibrary(candidate)
                        && L12StructuredCardRules.HasFaction(player, candidate, "taiyangcheng"));
                    if (target is not null)
                    {
                        player.Graveyard.Remove(target);
                        AddCardToHandByEffect(player, target, "graveyard", $"{target.Name}从墓地加入手牌");
                    }
                }
                FinishStackItem(item);
                return true;
            }
            case "valkyrie-summon-effect":
            {
                var targetId = CompositeDeclared(item, "entryCard").SingleOrDefault();
                var slot = CompositeDeclared(item, "entrySlot").SingleOrDefault();
                if (targetId is not null && slot is not null
                    && player.Graveyard.Any(candidate => candidate.InstanceId == targetId && candidate.CardType == "legion"
                        && candidate.CurrentCost <= 5 && L12StructuredCardRules.HasFaction(player, candidate, "asgard"))
                    && EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                    SummonFromAnyPrivateZone(player, targetId, slot, false);
                FinishStackItem(item);
                return true;
            }
            case "hunt-kill-effect":
            {
                var targetId = CompositeDeclared(item, "killTarget").SingleOrDefault();
                if (DeclaredEnemyTarget(item.Controller, targetId, target => target.Troops <= 6000) is not null)
                    KillTarget(item, targetId!, "被猎杀时刻击杀");
                FinishStackItem(item);
                return true;
            }
            case "法老王的庆典": BeginPharaohFestival(item); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionAttack(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "thutmose-debuff": ApplySunKingDebuff(item); return true;
            case "thutmose-kill": ResolveDeclaredSunKingKill(item); return true;
            case "图特摩斯三世": ApplySunKingAttack(item); return true;
            case "萨拉丁":
                if (!string.IsNullOrWhiteSpace(PublicTriggerDeclared(item, "moveTarget")))
                {
                    MoveOwnCardToSlot(player, PublicTriggerDeclared(item, "moveTarget"), PublicTriggerDeclared(item, "moveSlot"));
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            case "齐格鲁德": if (player.Relic?.CardId == "S01-0317" || player.ExtraRelics.Any(relic => relic.CardId == "S01-0317")) AddTimedModifier(card, 1000, 0, State.TurnSerial, "齐格鲁德"); FinishStackItem(item); return true;
            default: return false;
        }
    }

    private bool TryResolveS1FactionDeath(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "thutmose-debuff": ApplySunKingDebuff(item); return true;
            case "thutmose-kill": ResolveDeclaredSunKingKill(item); return true;
            case "图特摩斯三世": ApplySunKingAttack(item); return true;
            case "陵墓构造体":
                if (item.Data.TryGetValue("declaredCardIds", out var declaredGuards)
                    && item.Data.TryGetValue("declaredTargets", out var declaredGuardSlots))
                {
                    var guardIds = declaredGuards.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    var slots = declaredGuardSlots.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    var ownerTexts = item.Data.GetValueOrDefault("declaredGuardOwners", string.Empty)
                        .Split('|', StringSplitOptions.RemoveEmptyEntries);
                    for (var index = 0; index < Math.Min(guardIds.Length, slots.Length); index++)
                    {
                        var ownerIndex = index < ownerTexts.Length && int.TryParse(ownerTexts[index], out var parsedOwner)
                            && parsedOwner is >= 0 and <= 1 ? parsedOwner : item.Controller;
                        var owner = State.Players[ownerIndex];
                        if (owner.Graveyard.Any(candidate => candidate.InstanceId == guardIds[index]
                                && candidate.CardId == "S01-0212")
                            && EmptySlots(owner).Contains(slots[index], StringComparer.OrdinalIgnoreCase))
                            SummonFromAnyPrivateZone(owner, guardIds[index], slots[index], tapped: true);
                        else
                            AddEvent("effect-cancelled", item.Controller,
                                "陵墓构造体已声明的守卫或所有者位置失效；仅取消该对象", card);
                    }
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
                var target = PublicTriggerDeclared(item, "recoverTarget");
                if (player.Graveyard.Any(candidate => candidate.InstanceId == target && CanEnterHandOrLibrary(candidate)
                        && candidate.CardId != "S01-0207" && candidate.Faction == "taiyangcheng" && candidate.CurrentCost <= 4))
                    MoveGraveToLibraryTop(player, target);
                else AddEvent("effect-cancelled", item.Controller, "图坦卡蒙已声明的墓地目标失效；效果取消", card);
                FinishStackItem(item); return true;
            }
            case "纳芙蒂蒂": if (player.Hand.Count < State.Players[1 - item.Controller].Hand.Count) { DamageMaster(1 - item.Controller, 1, "纳芙蒂蒂阵亡效果"); HealMaster(item.Controller, 1, "纳芙蒂蒂阵亡效果", legionEffect: true); } FinishStackItem(item); return true;
            case "尼托克丽丝":
            {
                _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex,
                    PublicTriggerDeclared(item, "entryCard"), PublicTriggerDeclared(item, "entrySlot"), tapped: false);
                FinishStackItem(item); return true;
            }
            case "传奇的拉格纳":
                if (PublicTriggerDeclared(item, "mode") != "mode:use") { FinishStackItem(item); return true; }
                Draw(player, 1); PromptDiscard(item, item.Controller, 1, "传奇的拉格纳：抽牌后弃置1张手牌", "death-cycle-discard"); return true;
            case "无情者哈拉尔":
            {
                var target = PublicTriggerDeclared(item, "killTarget");
                if (DeclaredEnemyTarget(item.Controller, target, legion => legion.Troops <= 2000) is not null)
                    KillTarget(item, target, "被无情者哈拉尔阵亡效果击杀");
                else AddEvent("effect-cancelled", item.Controller, "无情者哈拉尔已声明的目标失效；效果取消", card);
                FinishStackItem(item); return true;
            }
            case "勇士比约恩":
                if (item.Data.TryGetValue("declaredGraveOrder", out var declaredGraveOrder)
                    && item.Data.TryGetValue("declaredSlot", out var declaredBjornSlot))
                {
                    var costsPaid = item.Data.GetValueOrDefault("bjornCostsPrepaid") == "true";
                    if (!costsPaid)
                    {
                        var ordered = declaredGraveOrder.Split('|', StringSplitOptions.RemoveEmptyEntries)
                            .Select(id => player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == id
                                && CanEnterHandOrLibrary(candidate)))
                            .Where(candidate => candidate is not null).Cast<L12CardInstance>().ToArray();
                        if (ordered.Length == 4)
                        {
                            MoveGraveToLibraryBottom(player, ordered);
                            costsPaid = true;
                        }
                    }
                    if (costsPaid)
                        _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, card.InstanceId,
                            declaredBjornSlot, tapped: true);
                    FinishStackItem(item); return true;
                }
                AddEvent("effect-cancelled", item.Controller,
                    "勇士比约恩缺少公共墓地费用声明；效果不进入旧式补选流程", card);
                FinishStackItem(item); return true;
            case "奥拉夫二世":
                if (PublicTriggerDeclared(item, "mode") != "mode:use") { FinishStackItem(item); return true; }
                Draw(player, 2); PromptDiscard(item, item.Controller, 1, "奥拉夫二世：抽牌后弃置1张手牌", "death-cycle-discard"); return true;
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
                    if (selected.Length == 2)
                        _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, selected[0], selected[1], tapped: false);
                    FinishStackItem(item); return true;
                }
                SummonAsgardFromGrave(item, 3); return true;
            case "神箭奥德尔":
            {
                var targetId = PublicTriggerDeclared(item, "restTarget");
                var target = FindOnField(State.Players[1 - item.Controller], targetId, out _, out _);
                if (target is not null && !target.Tapped)
                {
                    target.Tapped = true;
                    AddEvent("effect", item.Controller, "神箭奥德尔将目标转为休整", card);
                }
                else AddEvent("effect-cancelled", item.Controller, "神箭奥德尔已声明的活跃目标失效；效果取消", card);
                FinishStackItem(item); return true;
            }
            default: return false;
        }
    }

    private bool TryResolveS1FactionAfterAttack(L12StackItem item, L12CardInstance card)
    {
        if (card.CardId != "S01-0311") return false;
        if (PublicTriggerDeclared(item, "mode") == "mode:use")
        {
            var player = State.Players[item.Controller];
            var source = FindOnField(player, item.SourceInstanceId, out _, out _);
            if (source is not null)
                ReadyCardByEffect(item.Controller, source, source, "古斯塔夫一世因进攻后效果转为活跃");
            else AddEvent("effect-cancelled", item.Controller,
                "古斯塔夫一世在结算时已不在战场；转为活跃段取消，已置底费用不恢复", card);
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
            case "thutmose-kill": if (chosen[0] != "skip") KillTarget(item, chosen[0], $"被{source?.Name}击杀"); FinishStackItem(item); return true;
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
                var selected = player.Library.First(candidate => candidate.InstanceId == chosen[0]);
                player.Library.Remove(selected);
                PubliclyRevealThenAddCardToHandByEffect(player, selected, "library",
                    $"卡诺匹斯箱展示〈{selected.Name}〉并加入手牌", $"{selected.Name}因效果加入手牌");
                ShuffleLibrary(player, "卡诺匹斯箱检索结算");
                FinishStackItem(item); return true;
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
            case "faction-search-pick": CompleteFactionTopSearch(item, chosen); return true;
            case "faction-search-order": CompleteFactionSearchOrder(item, command.BottomCardInstanceIds ?? chosen); return true;
            case "festival-hand": ContinuePharaohFestivalHand(item, chosen[0]); return true;
            case "festival-grave": ContinuePharaohFestivalGrave(item, chosen[0]); return true;
            case "festival-bottom-order": CompletePharaohFestivalOrder(item, command.BottomCardInstanceIds ?? chosen); return true;
            case "faction-summon-slot": SummonFromAnyPrivateZone(player, item.Data["faction-summon"], chosen[0], false); FinishStackItem(item); return true;
            case "death-cycle-discard": MoveHandToGrave(player, chosen[0], causedByEffect: true,
                FindSource(item) ?? item.SourceSnapshot); FinishStackItem(item); return true;
            case "recover-asgard": if (chosen[0] != "skip") MoveGraveToHand(player, chosen[0]); FinishStackItem(item); return true;
            case "summon-asgard": if (chosen[0] == "skip") FinishStackItem(item); else { item.Data["faction-summon"] = chosen[0]; PromptFirstEmptySlot(item, "faction-summon-slot", "选择军团活跃登场的位置"); } return true;
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
                var gramCandidates = player.Graveyard.Where(card => card.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, card, "asgard")).ToArray();
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    GraveCostSelectionStep(player,
                        "神剑格拉墨：依次选择返回牌库底部、合计视为4张的阿斯加德军团",
                        "graveOrder", gramCandidates, required: 4, faction: "asgard", legionOnly: true),
                ]);
            case "valhallaKill":
            {
                var graveChoices = player.Graveyard.Where(CanEnterHandOrLibrary).ToArray();
                if (graveChoices.Sum(L12StructuredCardRules.StarterGraveCardCopies) < 2)
                    return CommandResult.Reject("墓地卡牌合计需能视为2张");
                var lowTargets = PublicLegions(enemy).Where(card => card.Troops <= 1000).Select(card => card.InstanceId).ToList();
                var broadTargets = PublicLegions(enemy).Where(card => card.Troops <= 5000).Select(card => card.InstanceId).ToList();
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    GraveCostSelectionStep(player, "英灵殿：依次选择合计视为2张、返回牌库底部的墓地卡牌",
                        "graveCost", graveChoices, required: 2),
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
                if (modes.Count == 0) return CommandResult.Reject("需要1份可用资源；若要使目标兵力-3000，还需额外休整1张活跃〈陵墓守卫〉");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "option", Text = "梅杰德：消耗1士气，使对方1张军团本回合兵力-1000；若额外休整我方1张〈陵墓守卫〉，则改为兵力-3000",
                        ValidChoices = modes, MinChoose = 1, MaxChoose = 1,
                        ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["mode:normal"] = "消耗1士气：对方1张军团本回合兵力-1000",
                            ["mode:strong"] = "消耗1士气并额外休整1张活跃〈陵墓守卫〉：对方1张军团本回合兵力-3000",
                        },
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "active-target", Text = "梅杰德：选择额外休整的1张活跃〈陵墓守卫〉",
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
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气");
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
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false, source);
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
                var representation = ids.SingleOrDefault(id => id.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase));
                var cardIds = ids.Where(id => !id.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase)).ToArray();
                var cards = cardIds.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id
                    && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "asgard"))).ToArray();
                if (source.Tapped || cards.Any(card => card is null)
                    || cardIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cardIds.Length
                    || !L12StructuredCardRules.IsExactGraveFactionRepresentation(player,
                        cards.Cast<L12CardInstance>().ToArray(), representation, "asgard", 4, legionOnly: true))
                    return CommandResult.Reject("需要活跃的神剑格拉墨与可视为合计4张的墓地阿斯加德军团");
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
                    return CommandResult.Reject("伊西斯选择的陵墓守卫、卡诺匹斯圣物或奖励选项已失效");
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
                    return CommandResult.Reject("梅杰德选择的效果、陵墓守卫或目标不再合法");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张可用资源");
                if (guard is not null) guard.Tapped = true;
                player.UsedAbilities.Add(onceKey);
                break;
            }
            case "valhallaDiscount" when source.CardId == "S01-03D1": if (player.Hp <= 1) return CommandResult.Reject("主宰血量不足"); DamageMaster(playerIndex, 1, "英灵殿费用减免"); player.UsedAbilities.Add(onceKey); break;
            case "valhallaRecover" when source.CardId == "S01-03D1":
                if (target != "mode:none" && !player.Graveyard.Any(card => card.InstanceId == target
                        && card.Faction == "asgard" && CanEnterHandOrLibrary(card)))
                    return CommandResult.Reject("英灵殿声明的墓地回收目标已失效");
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要2张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            case "valhallaKill" when source.CardId == "S01-03D1":
            {
                var ids = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (source.Tapped || !L12StructuredCardRules.TryResolveGraveCostDeclaration(player, ids,
                        2, string.Empty, legionOnly: false, out var graveCards, out var representation))
                    return CommandResult.Reject("英灵殿需为活跃并完成合计2张墓地费用声明");
                var targetIds = ids.Where(id => !graveCards.Any(card => card.InstanceId == id)
                    && !id.Equals(representation, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (targetIds.Length != 2) return CommandResult.Reject("英灵殿需完成两个击杀目标声明");
                var lowTarget = targetIds[0] == "mode:none" ? null : DeclaredEnemyTarget(playerIndex, targetIds[0], card => card.Troops <= 1000);
                var broadTarget = targetIds[1] == "mode:none" ? null : DeclaredEnemyTarget(playerIndex, targetIds[1], card => card.Troops <= 5000);
                if (targetIds[0] != "mode:none" && lowTarget is null || targetIds[1] != "mode:none" && broadTarget is null
                    || lowTarget is not null && broadTarget?.InstanceId == lowTarget.InstanceId)
                    return CommandResult.Reject("英灵殿声明的击杀目标不再合法");
                if (lowTarget is null && PublicLegions(State.Players[1 - playerIndex]).Any(card => card.Troops <= 1000)
                    || broadTarget is null && PublicLegions(State.Players[1 - playerIndex]).Any(card => card.Troops <= 5000 && card.InstanceId != lowTarget?.InstanceId))
                    return CommandResult.Reject("仍存在必须选择的合法击杀目标");
                source.Tapped = true;
                player.MasterTapped = true;
                MoveGraveToLibraryBottom(player, graveCards);
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
                if (!L12StructuredCardRules.TryResolveGraveCostDeclaration(player, ids, 2, string.Empty,
                        legionOnly: false, out _, out _))
                    return CommandResult.Reject("洛基声明的合计2张墓地费用已失效");
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
                    || !player.Graveyard.Any(card => card.InstanceId == target
                        && L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")))
                    return CommandResult.Reject("黄泉之门必须为活跃状态且墓地目标需保持合法");
                source.Tapped = true;
                player.MasterTapped = true;
                break;
            case "amaterasuKill" when source.CardId == "S01-04M1":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var debuff = declared.Length == 2 ? DeclaredEnemyTarget(playerIndex, declared[0]) : null;
                var kill = declared.Length == 2 && declared[1] != "mode:none"
                    ? DeclaredEnemyTarget(playerIndex, declared[1], card => Math.Max(0, card.CurrentCost
                        - (card.InstanceId == debuff?.InstanceId ? 1 : 0)) == 0)
                    : null;
                if (debuff is null || declared[1] != "mode:none" && kill is null)
                    return CommandResult.Reject("天照大神声明的费用降低或击杀目标已失效");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要1张活跃士气"); player.UsedAbilities.Add(onceKey); break;
            }
            case "amaterasuReady" when source.CardId == "S01-04M1":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                var discard = declared.Length > 0
                    ? player.Hand.FirstOrDefault(card => card.InstanceId == declared[0])
                    : null;
                if (discard is null) return CommandResult.Reject("声明的弃牌费用已失效");
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false, source);
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
        if (ability == "palaceReward")
        {
            foreach (var pair in CompositeFirstSegmentData("active:S01-01D1:palaceReward",
                         new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)))
                data[pair.Key] = pair.Value;
        }
        if (ability == "palaceExchange")
        {
            var values = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var declared = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["enemyTarget"] = [data.GetValueOrDefault("target", string.Empty)],
                ["reviveMode"] = [values.Length == 4 ? "mode:revive" : "mode:none"],
            };
            if (values.Length == 4)
            {
                declared["entryCard"] = [values[0]];
                declared["entryBattlefield"] = [values[1]];
                declared["entrySlot"] = [values[2]];
            }
            foreach (var pair in CompositeFirstSegmentData("active:S01-01D1:palaceExchange", declared))
                data[pair.Key] = pair.Value;
        }
        if (ability is "sunTopThree" or "valhallaRecover")
        {
            var recover = target ?? "mode:none";
            var declared = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["recoverMode"] = [recover == "mode:none" ? "mode:none" : "mode:recover"],
                ["graveCard"] = [recover],
            };
            var plan = ability == "sunTopThree"
                ? "active:S01-02D1:sunTopThree"
                : "active:S01-03D1:valhallaRecover";
            foreach (var pair in CompositeFirstSegmentData(plan, declared)) data[pair.Key] = pair.Value;
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
        if (ability is "amaterasuKill" or "amaterasuReady")
        {
            var values = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var declared = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var plan = $"active:S01-04M1:{ability}";
            if (ability == "amaterasuKill")
            {
                declared["debuffTarget"] = [values[0]];
                declared["killTarget"] = [values[1]];
            }
            else
            {
                declared["discardCost"] = [values[0]];
                declared["moraleTargets"] = [.. values.Skip(1)];
            }
            foreach (var pair in CompositeFirstSegmentData(plan, declared)) data[pair.Key] = pair.Value;
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
                BeginOptionalPaidEffectFollowup(item,
                    player.Hp <= 5,
                    1,
                    "若我方主宰血量不高于5，可额外消耗1士气：我方主宰增加1点血量。",
                    "heal-master",
                    new Dictionary<string, string> { ["amount"] = "1", ["reason"] = "阿斯加德阵营效果" });
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
            case "palaceReward":
                if (AtomicFlowKey(item) == "palace-reward-morale") AddMorale(player, 2, true);
                else if (AtomicFlowKey(item) == "palace-reward-draw") Draw(player, 1);
                FinishStackItem(item); return true;
            case "palaceExchange":
                if (AtomicFlowKey(item) == "palace-exchange-kill") ResolveDeclaredPalaceExchangeKill(item);
                else if (AtomicFlowKey(item) == "palace-exchange-revive") ResolveDeclaredPalaceExchangeRevive(item);
                else FinishStackItem(item);
                return true;
            case "mengpoSilence":
            {
                var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
                if (target is not null) target.SuppressDeathUntilTurn = State.TurnSerial;
                if (player.Hand.Count <= 5) Draw(player, 1); FinishStackItem(item); return true;
            }
            case "mengpoMorale": AddMorale(player, 1, true); FinishStackItem(item); return true;
            case "sunTopThree":
                if (AtomicFlowKey(item) == "sun-top-three-recover")
                {
                    var targetId = CompositeDeclared(item, "graveCard").SingleOrDefault();
                    var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == targetId
                        && card.Faction == "taiyangcheng" && CanEnterHandOrLibrary(card));
                    if (recover is null)
                        AddEvent("effect-cancelled", item.Controller, "众神之乡声明的墓地回收目标已失效");
                    else
                    {
                        player.Graveyard.Remove(recover);
                        AddCardToHandByEffect(player, recover, "graveyard", "众神之乡回收太阳城卡牌");
                    }
                    FinishStackItem(item); return true;
                }
                BeginFactionTopSearch(item, 3, "taiyangcheng", string.Empty, "sun-divinity"); return true;
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
            case "valhallaRecover":
                if (AtomicFlowKey(item) == "valhalla-recover")
                {
                    var targetId = CompositeDeclared(item, "graveCard").SingleOrDefault();
                    var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == targetId
                        && card.Faction == "asgard" && CanEnterHandOrLibrary(card));
                    if (recover is null)
                        AddEvent("effect-cancelled", item.Controller, "英灵殿声明的墓地回收目标已失效");
                    else
                    {
                        player.Graveyard.Remove(recover);
                        AddCardToHandByEffect(player, recover, "graveyard", "英灵殿回收阿斯加德卡牌");
                    }
                    FinishStackItem(item); return true;
                }
                Mill(player, 2, "英灵殿"); FinishStackItem(item); return true;
            case "valhallaKill":
            {
                var ids = item.Data.GetValueOrDefault("target")?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [];
                var graveIds = ids.Where(id => player.Library.Any(card => card.InstanceId == id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var targets = ids.Where(id => !graveIds.Contains(id)
                    && !id.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (targets.Length == 2)
                {
                    if (targets[0] != "mode:none") KillTarget(item, targets[0], "被英灵殿击杀");
                    if (targets[1] != "mode:none") KillTarget(item, targets[1], "被英灵殿击杀");
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
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Where(id => !id.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase)).ToArray();
                var cards = ids.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id
                        && CanEnterHandOrLibrary(card)))
                    .Where(card => card is not null).Cast<L12CardInstance>().ToArray();
                if (cards.Length > 0)
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
                    && L12StructuredCardRules.HasFaction(player, card, "gaotianyuan"));
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
                if (AtomicFlowKey(item) == "amaterasu-debuff")
                {
                    var targetId = CompositeDeclared(item, "debuffTarget").SingleOrDefault();
                    if (DeclaredEnemyTarget(item.Controller, targetId) is { } debuff) debuff.CostModifier--;
                    else AddEvent("effect-cancelled", item.Controller,
                        "天照大神选择的费用降低目标失效；该项目标的费用降低不结算", source is null ? [] : [source]);
                }
                else if (AtomicFlowKey(item) == "amaterasu-kill")
                {
                    var targetId = CompositeDeclared(item, "killTarget").SingleOrDefault();
                    if (targetId != "mode:none"
                        && DeclaredEnemyTarget(item.Controller, targetId, card => card.CurrentCost == 0) is not null)
                        KillTarget(item, targetId!, "被天照大神击杀");
                    else if (targetId != "mode:none") AddEvent("effect-cancelled", item.Controller,
                        "天照大神选择的费用为0目标失效；该目标不会被击杀", source is null ? [] : [source]);
                }
                FinishStackItem(item);
                return true;
            }
            case "amaterasuReady":
                if (AtomicFlowKey(item) == "amaterasu-ready")
                {
                    foreach (var targetId in CompositeDeclared(item, "moraleTargets"))
                    {
                        var morale = player.Morale.FirstOrDefault(card => card.InstanceId == targetId && card.Tapped);
                        if (source is not null && morale is not null)
                            ReadyMoraleByEffect(item.Controller, source, morale, "士气因天照大神效果转为活跃");
                        else AddEvent("effect-cancelled", item.Controller,
                            "天照大神已声明的休整士气目标失效；仅取消该对象", source is null ? [] : [source]);
                    }
                }
                else if (AtomicFlowKey(item) == "amaterasu-front-buff")
                {
                    foreach (var legion in PublicLegions(player).Where(card => FindOnField(player,
                                 card.InstanceId, out var row, out _) is not null && row == 0
                             && L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")))
                        AddTimedModifier(legion, 1000, 0, State.TurnSerial, "天照大神");
                }
                FinishStackItem(item);
                return true;
            default: return false;
        }
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

    private void ApplySunKingDebuff(L12StackItem item)
    {
        foreach (var target in PublicLegions(State.Players[1 - item.Controller]))
            AddTimedModifier(target, -1000, 0, State.TurnSerial, "图特摩斯三世");
        FinishStackItem(item);
    }

    private void ResolveDeclaredSunKingKill(L12StackItem item)
    {
        var targetId = CompositeDeclared(item, "killTarget").SingleOrDefault();
        if (DeclaredEnemyTarget(item.Controller, targetId, target => target.Troops <= 1000) is not null)
            KillTarget(item, targetId!, "被图特摩斯三世击杀");
        else AddEvent("effect-cancelled", item.Controller,
            "图特摩斯三世选择的击杀目标已失效；该目标不会被击杀");
        FinishStackItem(item);
    }

    private void PromptEnemyByTroops(L12StackItem item, string action, string text, int maxTroops, bool optional,
        int? row = null, Func<L12CardInstance, bool>? predicate = null)
    {
        var enemy = State.Players[1 - item.Controller];
        var choices = PublicLegions(enemy).Where(target => target.Troops <= maxTroops
                && (predicate?.Invoke(target) ?? true)
                && (row is null || FindOnField(enemy, target.InstanceId, out var targetRow, out _) is not null && targetRow == row))
            .Select(target => target.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return; }
        if (optional) choices.Add("skip");
        CreatePrompt(item.Controller, "target", text, choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = action });
    }

    private void PromptOwnLegion(L12StackItem item, string action, string text, Func<L12CardInstance, bool> predicate, bool optional)
    {
        var choices = PublicLegions(State.Players[item.Controller]).Where(predicate).Select(target => target.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return; }
        if (optional) choices.Add("skip");
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
            AddEvent("effect-cancelled", item.Controller, "梅杰德选择的陵墓守卫或登场位置已失效；该军团不登场");
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
        if (queue.Count == 0 || !EmptySlots(player).Any())
        {
            FinishStackItem(item);
            return;
        }
        item.Data["summon-queue"] = string.Join('|', queue);
        var current = player.Graveyard.Concat(player.Hand).Concat(player.Library).First(card => card.InstanceId == queue[0]);
        var data = new Dictionary<string, string>
        {
            ["action"] = "queued-summon-slot", ["previewCardId"] = current.InstanceId,
            ["previewPresentation"] = "handled-card",
        };
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
        var sourceSnapshot = CaptureLastKnownSourceSnapshot(relic);
        if (player.Relic?.InstanceId == relic.InstanceId) player.Relic = null; else player.ExtraRelics.Remove(relic);
        DiscardAttachedCards(relic, "被叠放的圣物离开圣物区");
        var owner = CardOwner(relic, player);
        ResetCardAfterLeavingField(relic);
        if (L12SpecialDeckRules.VanishesWhenLeavingField(relic))
            AddEvent("derived-vanished", owner.PlayerIndex,
                $"衍生卡〈{relic.Name}〉离开圣物区时消灭，不进入其他区域", relic);
        else if (!owner.Graveyard.Contains(relic))
            owner.Graveyard.Add(relic);
        QueueTriggerCandidates(BuildS1LeaveReactionCandidates(player.PlayerIndex, sourceSnapshot));
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
        var player = State.Players[item.Controller]; var choices = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard") && card.CurrentCost <= maxCost && (!legionOnly || card.CardType == "legion")).Select(card => card.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return; }
        choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "选择墓地1张【阿斯加德】卡牌加入手牌", choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "recover-asgard" });
    }

    private void SummonAsgardFromGrave(L12StackItem item, int maxCost)
    {
        var player = State.Players[item.Controller]; var choices = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard") && card.CardType == "legion" && card.CurrentCost <= maxCost).Select(card => card.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return; }
        choices.Add("skip");
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
        if (selected is not null)
        {
            player.Library.Remove(selected);
            PubliclyRevealThenAddCardToHandByEffect(player, selected, "library",
                $"法老王的庆典展示〈{selected.Name}〉并加入手牌",
                $"法老王的庆典将{selected.Name}加入手牌");
        }
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
        if (context == "sun-divinity" && top.Length > 0)
            AddEvent("reveal", item.Controller, "众神之乡公开牌库顶部3张牌", top);
        const int max = 1;
        var choices = top.Where(card => L12StructuredCardRules.HasFaction(player, card, faction)
            && card.CardId != excluded).Select(card => card.InstanceId).ToArray();
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
        foreach (var id in chosen)
        {
            var card = player.Library.First(candidate => candidate.InstanceId == id);
            player.Library.Remove(card);
            PubliclyRevealThenAddCardToHandByEffect(player, card, "library",
                $"〈{item.SourceName}〉展示〈{card.Name}〉并加入手牌", $"{card.Name}因效果加入手牌");
        }
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
        FinishStackItem(item);
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

    private bool TrySummonFromAnyPrivateZone(L12PlayerState sourceOwner, int destinationPlayerIndex,
        string instanceId, string slotChoice, bool tapped)
    {
        if (destinationPlayerIndex < 0 || destinationPlayerIndex >= State.Players.Length)
        {
            AddEvent("effect-cancelled", sourceOwner.PlayerIndex, "声明的登场战场已失效；仅取消本次登场");
            return false;
        }

        var destination = State.Players[destinationPlayerIndex];
        if (!EmptySlots(destination).Contains(slotChoice, StringComparer.OrdinalIgnoreCase))
        {
            AddEvent("effect-cancelled", sourceOwner.PlayerIndex, "声明的登场位置已失效；不覆盖、不改选，仅取消本次登场");
            return false;
        }

        var matches = sourceOwner.Hand.Concat(sourceOwner.Graveyard).Concat(sourceOwner.Library)
            .Where(candidate => candidate.InstanceId == instanceId).ToArray();
        if (matches.Length != 1 || !IsFieldLegion(matches[0]))
        {
            AddEvent("effect-cancelled", sourceOwner.PlayerIndex, "声明的登场卡牌真实实例已失效；仅取消本次登场");
            return false;
        }

        var card = matches[0];
        if (!EffectEntryBattlefieldChoices(sourceOwner.PlayerIndex, card).Contains(destinationPlayerIndex))
        {
            AddEvent("effect-cancelled", sourceOwner.PlayerIndex,
                "该军团不能在其他玩家战场登场；仅取消本次登场");
            return false;
        }
        var fromHand = sourceOwner.Hand.Contains(card);
        var fromLibrary = sourceOwner.Library.Contains(card);
        var (row, slot) = ParseSlot(slotChoice);
        if (destination.Field[row][slot] is not null)
        {
            AddEvent("effect-cancelled", sourceOwner.PlayerIndex, "声明的登场位置已失效；不覆盖、不改选，仅取消本次登场");
            return false;
        }

        sourceOwner.Hand.Remove(card);
        sourceOwner.Graveyard.Remove(card);
        sourceOwner.Library.Remove(card);
        card.OwnerIndex ??= sourceOwner.PlayerIndex;
        card.Tapped = tapped;
        card.SummonRound = State.Round;
        destination.Field[row][slot] = card;
        AddEvent("put", destinationPlayerIndex, $"{card.Name}{(tapped ? "休整" : "活跃")}登场", card);
        ApplyDisasterLevelOnEntry(destinationPlayerIndex, card, deferTriggerUntilStackSettles: true);
        if (fromHand)
        {
            if (HasImmediateEffect(card, "enter"))
                QueueOrPushTriggeredEffect(destinationPlayerIndex, card, "enter", "【登场时】效果");
            QueueS2GrailRoundTableEntry(destinationPlayerIndex, card);
        }
        else QueueNonHandEntry(destinationPlayerIndex, card, fromLibrary ? "library" : "graveyard");
        return true;
    }

    private void SummonFromAnyPrivateZone(L12PlayerState player, string instanceId, string slotChoice, bool tapped)
        => _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, instanceId, slotChoice, tapped);

    private void MoveOwnCardToSlot(L12PlayerState player, string instanceId, string slotChoice)
    {
        var card = FindOnField(player, instanceId, out var row, out var slot); if (card is null) return; var (targetRow, targetSlot) = ParseSlot(slotChoice); player.Field[row][slot] = null; player.Field[targetRow][targetSlot] = card; card.LastMovedTurn = State.TurnSerial;
        RecordLegionMovement(player.PlayerIndex, card, row, targetRow);
    }
}
