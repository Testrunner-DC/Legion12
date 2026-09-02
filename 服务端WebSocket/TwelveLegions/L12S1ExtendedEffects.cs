namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> S1ExtendedEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0001", "S01-0004",
        "S01-0110", "S01-0111", "S01-0112", "S01-0114", "S01-0115",
        "S01-0402", "S01-0403", "S01-0406", "S01-0407", "S01-0408", "S01-0411", "S01-0412",
    };

    private static readonly HashSet<string> S1ExtendedTacticCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0005", "S01-0006", "S01-0007", "S01-0008", "S01-0009", "S01-0010", "S01-0011",
        "S01-0013", "S01-0014",
    };

    private static readonly HashSet<string> S1ExtendedAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0111", "S01-0402", "S01-0406", "S01-0408",
    };

    internal static readonly HashSet<string> S1ExtendedDeathCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0001", "S01-0004", "S01-0110", "S01-0111", "S01-0112", "S01-0115",
        "S01-0403", "S01-0407", "S01-0412",
    };

    internal static readonly HashSet<string> S1ExtendedAfterAttackCards = new(StringComparer.OrdinalIgnoreCase);

    private static bool HasS1ExtendedImmediateEffect(string cardId, string trigger)
        => trigger == "enter" ? S1ExtendedEnterCards.Contains(cardId) || HasS1FactionImmediateEffect(cardId, trigger)
            : S1ExtendedTacticCards.Contains(cardId) || HasS1FactionImmediateEffect(cardId, trigger);

    private static readonly IReadOnlyDictionary<string, string> FactionEffectCardAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ST01-C1"] = "S01-01C1",
        ["ST02-C1"] = "S01-02C1",
        ["ST03-C1"] = "S01-03C1",
        ["ST04-C1"] = "S01-04C1",
        ["ST05-C1"] = "S02-05C1A",
        ["ST06-C1"] = "S02-06C1",
    };

    private static string CanonicalFactionEffectCardId(string cardId)
        => FactionEffectCardAliases.GetValueOrDefault(cardId, cardId);

    private static List<L12AbilityView> GetAbilities(string cardId)
    {
        cardId = CanonicalFactionEffectCardId(cardId);
        return cardId switch
        {
        "S01-0003" => [new("extendedRange", "消耗2士气：扩展进攻范围")],
        "S01-0004" => [new("destroyInfiltrator", "消耗2士气：击杀此军团")],
        "S01-0105" => [new("searchBrothers", "检索关羽/张飞")],
        "S01-0109" => [new("addMorale", "追加士气")],
        "S01-0113" => [new("extendedRange", "返还1士气：进攻后排")],
        "S01-0116" => [new("xishiExchange", "弃置自身并返还1士气：替换登场")],
        "S01-0117" => [new("artifactDraw", "返还1活跃士气：抽取1张牌。"), new("artifactSearch", "弃置1张手牌：查看牌库顶部3张牌，选择其中1张【天廷】卡牌，展示并加入手牌，其余卡牌自选顺序返回牌库顶部或底部。")],
        "S01-0415" => [new("revealHidden", "主动：翻回正面，作为军团恢复公开状态。")],
        "S01-0417" => [new("kusanagiDebuff", "选择对方1张军团，本回合费用-1。"), new("kusanagiStrong", "选择我方1张【高天原】军团，本回合获得强攻。")],
        "S01-01C1" => [new("factionAddActive", "我方 回合1次 可消耗2士气：从士气牌库追加1张活跃的士气。"), new("factionZeroRecovery", "我方 回合1次 我方士气为0张时，可从士气牌库追加2张休整的士气。")],
        "S01-04C1" => [new("factionDrawMove", "我方 回合1次 可消耗2士气：抽取1张牌。随后可选择我方1张活跃的军团进行1格位移。")],
        "S01-01M1" => [new("drawCycle", "消耗1张活跃士气：抽取1张牌，再将1张手牌放回牌库顶部或底部。"), new("nonLethal", "返还4张士气：对方主宰失去1点血量，此效果不能令其血量低于1。")],
        "S01-04M2" =>
        [
            new("frontBuff", "我方 回合1次 可消耗1士气：选择我方1张【高天原】军团，本回合位于前排进攻时兵力+2000。"),
            new("kusanagi", "消耗2士气：将我方〈草薙剑〉置入前排，视为1张兵力5000的【武者】军团。（草薙剑仍可发动其效果）〈草薙剑〉离场时：可选择将其放回牌库顶部。"),
        ],
        "S02-0003" => [new("disableCounters", "主动休整：直到我方下个回合开始前，战场上所有反击战术无法发动。")],
        "S02-0104" => [new("shennongReset", "主动休整 返还1士气：重置我方主宰其中1个效果的使用次数。")],
        "S02-05C1" => [new("godPowerDraw", "我方 回合1次 可消耗并翻转1神力：抽取1张牌。")],
        "S02-05C1A" => [new("olympusMoraleFlip", "我方 回合1次 可消耗1士气：翻转1张士气。")],
        "S02-05M2" => [new("prometheusTopThree", "我方 回合1次 消耗1神力：查看牌库顶部3张牌，选择其中1张【奥林匹斯】卡牌，展示并加入手牌，其余卡牌自选顺序返回牌库顶部或底部。")],
        "S02-01M1" => [new("wukongTransform", "我方 回合1次 可返还2至8士气：将此主宰作为【斗士】军团在我方前排活跃登场，兵力=本次返还的士气数量×1000，且在登场回合即可进攻。")],
        "S02-03M1" => [new("thorCharge", "我方回合 当我方主宰血量不高于3时，可消耗2士气：本回合我方所有【阿斯加德】军团在登场时获得冲锋。以上效果发动后，我方主宰本局游戏无法因任何效果增加血量。")],
        "S02-0301" => [new("thorHammerRevive", "我方 回合1次 当此军团在墓地时，可将墓地3张其他卡牌自选顺序返回我方牌库底部：将此军团活跃登场。")],
        "S02-05D1" =>
        [
            new("divinityFlipMorale", "我方 回合1次 可翻转1张士气。"),
            new("divinityPower", "我方 回合1次 可消耗并翻转2神力：选择回收并登场，或对对方所有军团造成合计6000兵力的伤害。"),
            new("divinityFreePromotion", "主动休整：本回合我方下1张【奥林匹斯】军团「晋升登场」无需消耗并翻转神力。"),
        ],
        "S02-05M1" => [new("artemisBuff", "我方 回合1次 可消耗并翻转1神力或弃置1张手牌：选择我方1张费用为3至6的【奥林匹斯】军团，本回合获得强攻或震击。")],
        "S02-0510" => [new("hippolytaRevive", "主动休整 消耗3士气并弃置1张手牌：选择墓地1张费用不高于4的【奥林匹斯】军团活跃登场。")],
        "S02-06D1" =>
        [
            new("avalonRecover", "我方 回合1次 可消耗2符文：选择墓地中1张军团和1张战术加入手牌。随后，本回合从手牌中打出的下1张战术卡无需消耗费用。"),
            new("avalonDebuff", "主动休整 选择对方1张军团，本回合兵力-4000。"),
        ],
        "S02-06M1" => [new("morriganReadyOnKill", "我方 回合1次 可消耗2符文：选择我方1张【彼界】军团，在本回合其下一次击杀对方军团后转为活跃。")],
        "S02-0520" =>
        [
            new("forgePromotionDiscount", "主动休整 消耗1士气：本回合我方下1张军团「晋升登场」消耗并翻转的神力-1。"),
            new("forgeReadyOnKill", "主动休整 消耗1士气：选择我方1张【晋升者】以外的【奥林匹斯】军团，在本回合其下一次击杀对方军团后转为活跃。"),
        ],
        "S02-06C1" =>
        [
            new("factionGainRune", "我方 回合1次 可消耗2士气：获得1符文。"),
            new("runeUse", "我方 回合1次 可消耗1符文：选择试炼+1，或抽取1张牌。"),
        ],
        "S02-06S3" => [new("completeTrial", "试炼达到8：完成《湖中仙女的馈赠》。")],
        "S02-06S4" => [new("completeTrial", "试炼达到8：完成《寻找圣杯之旅》。")],
        "S02-06S5" =>
        [
            new("completeTrial", "试炼达到8：完成《芬尼亚传奇》。"),
            new("fenianReady", "我方 回合1次 可消耗1符文：将我方1张〈芬恩〉或原本兵力不高于4000的【彼界】军团转为活跃。"),
        ],
        "S02-06S6" =>
        [
            new("completeTrial", "试炼达到8：完成《十字军东征》。"),
            new("crusadeTrialNoLoss", "消耗1符文：选择我方1张【试炼军团】，本回合下一次进攻无损。"),
            new("crusadeRichardPiercing", "消耗2符文：本回合我方1张〈狮心王理查一世〉击杀时获得贯穿。"),
            new("crusadeRecover", "消耗2符文并弃置1张手牌：将墓地1张只有【彼界】特征的卡牌加入手牌。"),
        ],
            _ => GetStarterRemainingAbilityViews(cardId) is { Count: > 0 } starterAbilities
                ? starterAbilities
                : GetS1FactionAbilities(cardId) is { Count: > 0 } s1Abilities ? s1Abilities : GetS2FactionAbilities(cardId),
        };
    }

    private bool TryResolveS1ExtendedEnter(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "黑胡子蒂奇":
            case "teach-enter-discard":
                BeginBlackbeardSimultaneousDiscard(item);
                return true;
            case "teach-enter-draw":
                Draw(player, 2);
                Draw(State.Players[1 - item.Controller], 1);
                FinishStackItem(item);
                return true;
            case "无名的渗透者":
                card.Tapped = true;
                card.CannotAttack = true;
                card.CannotSupport = true;
                AddEvent("effect", item.Controller, "无名的渗透者休整登场，且不可进攻或支援", card);
                FinishStackItem(item);
                return true;
            case "墨子":
            {
                var choices = PublicLegions(player).Where(target => target.Faction == "tianting").Select(target => target.InstanceId).ToList();
                if (!CanReturnMorale(player, 1) || choices.Count == 0) { FinishStackItem(item); return true; }
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-targets", "可返还1士气：选择我方最多2张【天廷】军团获得免死", choices,
                    1, Math.Min(2, choices.Count - 1), "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "mozi-immortal" });
                return true;
            }
            case "诸葛亮":
            {
                var next = State.DisasterDeck.FirstOrDefault();
                AddEvent("reveal", item.Controller, next is null ? "诸葛亮查看天灾牌库：没有下一张天灾" : $"诸葛亮查看下一张天灾：{next.Name}", next is null ? [] : [next]);
                CreatePrompt(item.Controller, "option", "诸葛亮：可将天灾值增加或减少1点", ["-1", "0", "1"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "zhuge-disaster" });
                return true;
            }
            case "孙武":
                if (!CanReturnMorale(player, 1)) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "是否返还1士气，使本回合从手牌打出的下1张战术卡无需费用？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "sunwu-free-tactic" });
                return true;
            case "秦良玉":
                AddMorale(player, 1, tapped: true);
                AddEvent("effect", item.Controller, "秦良玉从士气牌库追加1张休整士气", card);
                FinishStackItem(item);
                return true;
            case "织田信长":
                PromptEnemyLegion(item, "nobunaga-kill", "织田信长：击杀对方1张费用不高于4的军团", target => target.CurrentCost <= 4, false);
                return true;
            case "上杉谦信":
            {
                var x = State.Players.SelectMany(owner => owner.Field[1]).Count(target => target is { CardType: "tactic" });
                PromptEnemyLegion(item, "kenshin-kill", $"上杉谦信：击杀对方1张费用不高于{x}的军团", target => target.CurrentCost <= x, false);
                return true;
            }
            case "土方岁三":
                item.Data["hijikata-step"] = "2";
                PromptEnemyLegion(item, "hijikata-enter-kill", "土方岁三：击杀对方1张费用不高于2的军团", target => target.CurrentCost <= 2, true);
                return true;
            case "坂本龙马":
            {
                var declaredTargets = PublicTriggerDeclared(item, "moveTargets")
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (declaredTargets.Length > 0)
                {
                    var destinations = new[] { PublicTriggerDeclared(item, "moveSlot1"), PublicTriggerDeclared(item, "moveSlot2") };
                    if (declaredTargets.Length == 2
                        && FindOnField(player, declaredTargets[0], out var firstRow, out var firstSlot) is { Tapped: true } first
                        && FindOnField(player, declaredTargets[1], out var secondRow, out var secondSlot) is { Tapped: true } second
                        && destinations[0] == $"{secondRow}:{secondSlot}" && destinations[1] == $"{firstRow}:{firstSlot}")
                    {
                        player.Field[firstRow][firstSlot] = second;
                        player.Field[secondRow][secondSlot] = first;
                        first.LastMovedTurn = State.TurnSerial;
                        second.LastMovedTurn = State.TurnSerial;
                        RecordLegionMovement(item.Controller, first, firstRow, secondRow);
                        RecordLegionMovement(item.Controller, second, secondRow, firstRow);
                        AddEvent("move", item.Controller, $"坂本龙马使{first.Name}与{second.Name}互换阵地", first, second);
                        FinishStackItem(item); return true;
                    }
                    for (var index = 0; index < declaredTargets.Length; index++)
                        MoveOwnCardToSlot(player, declaredTargets[index], destinations[index]);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            }
            case "高杉晋作":
                if (!Draw(player, 1)) { SetWinner(1 - item.Controller, "高杉晋作效果抽牌时牌库为空"); FinishStackItem(item); return true; }
                PromptEnemyLegion(item, "takasugi-debuff", "高杉晋作：选择对方1张军团，本回合费用-2", _ => true, false);
                return true;
            case "安倍晴明":
            {
                var choices = PublicLegions(player).Select(target => target.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target", "安倍晴明：选择我方1张军团，获得免死直到下个我方回合开始前", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "abe-immortal" });
                return true;
            }
            case "立花誾千代":
                PromptEnemyLegion(item, "tachibana-debuff", "立花誾千代：选择对方1张军团，本回合费用-3", _ => true, false);
                return true;
            default:
                return TryResolveS1FactionEnter(item, card);
        }
    }

    private bool TryResolveS1ExtendedTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        var enemy = State.Players[1 - item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "volley-effect":
            {
                var mode = CompositeDeclared(item, "volleyMode").SingleOrDefault();
                if (mode is "mode:front" or "mode:back")
                {
                    var row = mode == "mode:front" ? 0 : 1;
                    foreach (var target in enemy.Field[row].Where(target => target is not null).Cast<L12CardInstance>())
                        AddTimedModifier(target, -2000, 0, State.TurnSerial, card.Name);
                }
                else
                {
                    var target = FindOnField(enemy, CompositeDeclared(item, "singleTarget").SingleOrDefault(), out _, out _);
                    if (target is not null) AddTimedModifier(target, -4000, 0, State.TurnSerial, card.Name);
                }
                FinishStackItem(item);
                return true;
            }
            case "evil-ritual-effect":
                DamageMasterNonLethal(1 - item.Controller, 1, card.Name);
                FinishStackItem(item);
                return true;
            case "strategic-transfer-effect":
            {
                var returnTarget = FindOnField(player, CompositeDeclared(item, "returnTarget").SingleOrDefault(), out _, out _);
                if (returnTarget is not null) MoveFieldCardToZone(player, returnTarget, "hand", "因战略转移返回手牌");
                var buffTarget = FindOnField(player, CompositeDeclared(item, "buffTarget").SingleOrDefault(), out _, out _);
                if (buffTarget is not null) AddTimedModifier(buffTarget, 2000, 0, State.TurnSerial, card.Name);
                FinishStackItem(item);
                return true;
            }
            case "forged-orders-effect":
            {
                var targets = CompositeDeclared(item, "moveTargets");
                for (var index = 0; index < targets.Length; index++)
                {
                    var target = FindOnField(enemy, targets[index], out var row, out var slot);
                    var destination = CompositeDeclared(item, $"moveSlot{index + 1}").SingleOrDefault();
                    if (target is null || destination?.Split(':') is not [var rowText, var slotText]
                        || !int.TryParse(rowText, out var nextRow) || !int.TryParse(slotText, out var nextSlot)
                        || nextRow != 1 - row || nextSlot != slot || enemy.Field[nextRow][nextSlot] is not null
                        || State.ActiveDisaster?.CardId == "S01-DS03" && nextRow == 1) continue;
                    enemy.Field[row][slot] = null;
                    enemy.Field[nextRow][nextSlot] = target;
                    RecordLegionMovement(1 - item.Controller, target, row, nextRow);
                }
                FinishStackItem(item);
                return true;
            }
            case "plague-effect":
            {
                var targetId = CompositeDeclared(item, "lockTarget").SingleOrDefault();
                var legion = FindOnField(enemy, targetId, out _, out _);
                if (legion is not null) legion.CannotUntapUntilRound = State.Round + 1;
                var morale = enemy.Morale.FirstOrDefault(candidate => candidate.InstanceId == targetId);
                if (morale is not null) morale.CannotUntapUntilRound = State.Round + 1;
                FinishStackItem(item);
                return true;
            }
            case "野外扎营":
            case "camp-search":
            {
                var top = player.Library.Take(3).ToArray();
                item.Data["camp-top"] = string.Join('|', top.Select(candidate => candidate.InstanceId));
                var choices = top.Where(candidate => candidate.CardType == "legion" && candidate.Faction == player.Faction)
                    .Select(candidate => candidate.InstanceId).ToList();
                choices.Add("skip");
                var data = new Dictionary<string, string>
                {
                    ["action"] = "camp-pick",
                    ["displayCardIds"] = string.Join('|', top.Select(card => card.InstanceId))
                };
                foreach (var candidate in top) AddPromptCardData(data, candidate);
                CreatePrompt(item.Controller, "search", "野外扎营：选择其中1张与主宰阵营相同的军团加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: data);
                return true;
            }
            case "兵临城下":
                foreach (var target in State.Players[1 - item.Controller].Field[0].Where(target => target is not null).Cast<L12CardInstance>())
                    AddTimedModifier(target, -1000, 0, State.TurnSerial, "兵临城下");
                State.Players[1 - item.Controller].BackRowCannotSupport = true;
                AddEvent("effect", item.Controller, "兵临城下：对方前排军团本回合兵力-1000，后排军团无法支援", card);
                FinishStackItem(item);
                return true;
            case "前线侦查":
            case "scout-reveal":
            {
                AddEvent("reveal", item.Controller, $"前线侦查查看对方全部{enemy.Hand.Count}张手牌", enemy.Hand.ToArray());
                FinishStackItem(item);
                return true;
            }
            case "scout-shuffle-effect":
            {
                if (enemy.Hand.Count == 0) { FinishStackItem(item); return true; }
                CreatePrompt(1 - item.Controller, "card", "前线侦查：选择1张手牌洗回牌库",
                    enemy.Hand.Select(card => card.InstanceId), 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "scout-shuffle" });
                return true;
            }
            case "camp-heal":
                HealMaster(item.Controller, 1, "野外扎营");
                FinishStackItem(item);
                return true;
            case "camp-draw":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "野外扎营抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "ritual-draw":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "祭天仪式抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "ritual-disaster":
                if (int.TryParse(CompositeDeclared(item, "disasterValue").SingleOrDefault(), out var delta))
                    AdjustDisasterValue(delta);
                FinishStackItem(item);
                return true;
            default:
                return TryResolveS1FactionTactic(item, card);
        }
    }

    private bool TryResolveS1ExtendedAttack(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "诸葛亮":
                BeginZhugePeek(item);
                return true;
            default:
                return TryResolveS1FactionAttack(item, card);
        }
    }

    private bool TryResolveS1ExtendedDeath(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "黑胡子蒂奇":
                if (PublicTriggerDeclared(item, "mode") != "mode:use") { FinishStackItem(item); return true; }
                Draw(player, 2);
                PromptDiscard(item, item.Controller, 1, "黑胡子蒂奇：抽牌后弃置1张手牌", "teach-death-discard");
                return true;
            case "无名的渗透者":
            {
                var ownerIndex = card.OwnerIndex is >= 0 and <= 1 ? card.OwnerIndex.Value : item.Controller;
                if (!Draw(State.Players[ownerIndex], 1)) SetWinner(1 - ownerIndex, "无名的渗透者所有者因阵亡效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            }
            case "墨子":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "墨子阵亡效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "诸葛亮":
                BeginZhugePeek(item);
                return true;
            case "孙武":
            {
                var target = PublicTriggerDeclared(item, "recoverTarget");
                if (State.DisasterValue <= 4 && player.Graveyard.Any(candidate => candidate.InstanceId == target
                        && candidate.CardType == "tactic" && candidate.CurrentCost <= 4))
                    MoveGraveToHand(player, target);
                FinishStackItem(item); return true;
            }
            case "荆轲":
            {
                var target = PublicTriggerDeclared(item, "killTarget");
                if (!string.IsNullOrWhiteSpace(target)
                    && DeclaredEnemyTarget(item.Controller, target, legion => legion.Troops <= 2000) is not null)
                    KillTarget(item, target, "被荆轲阵亡效果击杀");
                FinishStackItem(item); return true;
            }
            case "上杉谦信":
            {
                var selected = PublicTriggerDeclared(item, "entryCards")
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var destinations = new[] { PublicTriggerDeclared(item, "entrySlot1"), PublicTriggerDeclared(item, "entrySlot2") };
                for (var index = 0; index < Math.Min(selected.Length, destinations.Length); index++)
                {
                    var counter = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == selected[index]
                        && IsCounterTactic(candidate.CardId));
                    var (row, slot) = ParseSlot(destinations[index]);
                    if (counter is null || row != 1 || slot is < 0 or > 2 || player.Field[row][slot] is not null)
                    {
                        AddEvent("effect-cancelled", item.Controller,
                            "上杉谦信已声明的反击战术或后排位置失效；仅取消该对象", card);
                        continue;
                    }
                    player.Hand.Remove(counter); counter.Hidden = true; counter.SetRound = State.Round;
                    player.Field[row][slot] = counter;
                }
                FinishStackItem(item); return true;
            }
            case "坂本龙马":
            {
                var entry = PublicTriggerDeclared(item, "entryCard");
                var slot = PublicTriggerDeclared(item, "entrySlot");
                if (player.Hand.Any(candidate => candidate.InstanceId == entry && candidate.CardType == "legion"
                        && L12StructuredCardRules.HasFaction(player, candidate, "gaotianyuan")
                        && candidate.CurrentCost <= 3)
                    && EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                    SummonFromHand(player, entry, slot, tapped: true);
                else AddEvent("effect-cancelled", item.Controller,
                    "坂本龙马已声明的手牌军团或登场位置失效；效果取消", card);
                FinishStackItem(item); return true;
            }
            case "立花誾千代":
                foreach (var target in PublicLegions(State.Players[1 - item.Controller]))
                    AddTimedModifier(target, 0, -1, ExpiryAtNextOwnEnd(item.Controller), "立花誾千代");
                AddEvent("effect", item.Controller, "立花誾千代阵亡：直到下个我方回合结束前，对方所有军团费用-1", card);
                FinishStackItem(item);
                return true;
            default:
                return TryResolveS1FactionDeath(item, card);
        }
    }

    private bool TryResolveS1ExtendedAfterAttack(L12StackItem item, L12CardInstance card) => TryResolveS1FactionAfterAttack(item, card);

    private bool TryContinueS1Extended(L12StackItem item, L12Prompt prompt, List<string> chosen, L12Command command)
    {
        var action = prompt.Data.GetValueOrDefault("action") ?? string.Empty;
        var player = State.Players[item.Controller];
        var enemy = State.Players[1 - item.Controller];
        var source = FindSource(item);
        switch (action)
        {
            case "teach-discard":
            {
                item.Data[$"teach-discard:{prompt.PlayerIndex}"] = string.Join(',', chosen);
                if (State.PendingPrompts.Any(candidate => candidate.StackItemId == item.StackItemId
                    && candidate.Data.GetValueOrDefault("action") == "teach-discard")) return true;
                for (var chooserIndex = 0; chooserIndex < 2; chooserIndex++)
                {
                    var chooser = State.Players[chooserIndex];
                    var selected = item.Data.GetValueOrDefault($"teach-discard:{chooserIndex}")?
                        .Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                    foreach (var id in selected) MoveHandToGrave(chooser, id, causedByEffect: true);
                }
                FinishStackItem(item);
                return true;
            }
            case "teach-death-discard":
                MoveHandToGrave(player, chosen[0], causedByEffect: true); FinishStackItem(item); return true;
            case "mozi-immortal":
                if (chosen.Contains("skip")) { FinishStackItem(item); return true; }
                BeginEffectMoraleReturn(item, 1, "mozi-immortal", new() { ["targets"] = string.Join('|', chosen) }); return true;
            case "zhuge-disaster":
                AdjustDisasterValue(int.Parse(chosen[0])); FinishStackItem(item); return true;
            case "zhuge-peek-pay":
                if (chosen[0] == "no") { FinishStackItem(item); return true; }
                BeginEffectMoraleReturn(item, 1, "zhuge-peek");
                return true;
            case "zhuge-artifact":
            {
                var artifact = player.Resolving.First(candidate => candidate.InstanceId == item.Data["zhuge-card"]);
                player.Resolving.Remove(artifact);
                if (chosen[0] == "play")
                {
                    if (player.Relic is not null) DiscardRelic(player, player.Relic);
                    player.Relic = artifact; artifact.Tapped = false;
                }
                else AddCardToHandByEffect(player, artifact, "library", $"诸葛亮将{artifact.Name}加入手牌");
                FinishStackItem(item); return true;
            }
            case "sunwu-free-tactic":
                if (chosen[0] == "yes") BeginEffectMoraleReturn(item, 1, "free-tactic");
                else FinishStackItem(item); return true;
            case "nobunaga-kill":
            case "kenshin-kill":
                if (chosen[0] != "skip") KillTarget(item, chosen[0], $"被{source?.Name}击杀");
                FinishStackItem(item); return true;
            case "hijikata-enter-kill":
                if (chosen[0] != "skip") KillTarget(item, chosen[0], "被土方岁三击杀");
                if (item.Data["hijikata-step"] == "2")
                {
                    item.Data["hijikata-step"] = "1";
                    PromptEnemyLegion(item, "hijikata-enter-kill", "土方岁三：击杀对方1张费用不高于1的军团", target => target.CurrentCost <= 1, true);
                }
                else FinishStackItem(item);
                return true;
            case "takasugi-debuff":
            case "tachibana-debuff":
            {
                var target = FindOnField(enemy, chosen[0], out _, out _);
                if (target is not null) target.CostModifier += action == "tachibana-debuff" ? -3 : -2;
                FinishStackItem(item); return true;
            }
            case "abe-immortal":
            {
                var target = FindOnField(player, chosen[0], out _, out _);
                if (target is not null) GrantImmortalUntilNextTurnStart(target, item.Controller);
                FinishStackItem(item); return true;
            }
            case "camp-pick":
                CompleteCampPick(item, chosen[0]); return true;
            case "camp-order":
                CompleteCampOrder(item, chosen); return true;
            case "scout-shuffle":
            {
                var target = enemy.Hand.First(candidate => candidate.InstanceId == chosen[0]); enemy.Hand.Remove(target); enemy.Library.Add(target); ShuffleLibrary(enemy, "前线侦查结算");
                FinishStackItem(item); return true;
            }
            case "ambush-buff":
            {
                var target = FindOnField(player, chosen[0], out _, out _); if (target is not null) AddTimedModifier(target, 2000, 0, State.TurnSerial, "伏击");
                FinishStackItem(item); return true;
            }
            case "empty-city-block":
                if (chosen[0] == "yes") BeginEffectMoraleReturn(item, 1, "empty-city-block");
                else FinishStackItem(item); return true;
            case "last-stand-mode":
            {
                var rested = PublicLegions(enemy).Where(card => card.Tapped).ToArray();
                if (chosen[0] == "all")
                {
                    foreach (var target in rested) AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "拼死反抗");
                    FinishStackItem(item);
                }
                else if (rested.Length == 0) FinishStackItem(item);
                else CreatePrompt(item.Controller, "target", "拼死反抗：选择对方1张休整军团，直到下个我方回合结束前兵力-2000",
                    rested.Select(card => card.InstanceId), 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "last-stand-single" });
                return true;
            }
            case "last-stand-single":
            {
                var target = FindOnField(enemy, chosen[0], out _, out _);
                if (target is not null) AddTimedModifier(target, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), "拼死反抗");
                FinishStackItem(item); return true;
            }
            case "seppuku-cost":
            {
                var target = FindOnField(enemy, chosen[0], out _, out _);
                if (target is not null) AddTimedModifier(target, 0, -2, ExpiryAtNextOwnEnd(item.Controller), "切腹仪式");
                FinishStackItem(item); return true;
            }
            case "wisdom-discard":
            {
                var targetStack = State.EffectStack.FirstOrDefault(stack => stack.StackItemId == item.Targets.FirstOrDefault());
                if (chosen[0] == "abandon")
                {
                    if (targetStack is not null) targetStack.Negated = true;
                    AddEvent("effect-abandoned", prompt.PlayerIndex, "智慧法典使发动者不弃牌并放弃当前效果");
                }
                else
                {
                    MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: false);
                    if (targetStack is not null)
                    {
                        var marker = $"{item.Controller}|{item.SourceInstanceId}";
                        var rewards = targetStack.Data.GetValueOrDefault("wisdomRewards") ?? string.Empty;
                        targetStack.Data["wisdomRewards"] = string.IsNullOrEmpty(rewards) ? marker : $"{rewards};{marker}";
                    }
                }
                FinishStackItem(item);
                return true;
            }
            default:
                return TryContinueS1Faction(item, prompt, chosen, command);
        }
    }

    private CommandResult? TryBeginS1ExtendedActiveAbility(int playerIndex, L12CardInstance source, string ability)
    {
        if (ability == "xishiExchange" && source.CardId == "S01-0116")
        {
            var player = State.Players[playerIndex];
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "card", Text = "西施：选择手牌中最多 1 张兵力不高于 2000 的其他军团",
                    ValidChoices = player.Hand.Where(card => card.CardType == "legion" && card.CardId != "S01-0116" && card.Troops <= 2000
                        && EffectEntryBattlefieldChoices(playerIndex, card).Any()).Select(card => card.InstanceId).ToList(),
                    MinChoose = 0,
                    MaxChoose = 1,
                },
                new L12ActivationSelectionStep
                {
                    Kind = "effect-entry-battlefield", Text = "西施：选择该军团活跃登场的战场", ValidChoices = ["dynamic"],
                    SkipWhenPreviousStepEmpty = true,
                },
                new L12ActivationSelectionStep
                {
                    Kind = "effect-entry-slot", Text = "西施：预先选择该军团活跃登场的位置", ValidChoices = ["dynamic"],
                    SkipWhenPreviousStepEmpty = true,
                },
            ]);
        }
        if (ability is "extendedRange" or "destroyInfiltrator" or "revealHidden")
            return CommitActiveAbility(playerIndex, source, ability, null);
        return TryBeginS1FactionActiveAbility(playerIndex, source, ability);
    }

    private CommandResult? TryCommitS1ExtendedActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey, bool? useTombGuards,
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
            case "extendedRange" when source.CardId is "S01-0003" or "S01-0113":
            {
                if (FindOnField(player, source.InstanceId, out var row, out _) is null || row != 1) return CommandResult.Reject("该效果只能在后排发动");
                var paid = source.CardId == "S01-0003" ? ConsumeMorale(2) : returnMoralePrepaid || ReturnMorale(player, 1);
                if (!paid) return CommandResult.Reject(source.CardId == "S01-0003" ? "需要消耗2张活跃士气" : "需要返还1张士气");
                if (source.CardId == "S01-0003") player.UsedAbilities.Add(onceKey);
                break;
            }
            case "xishiExchange" when source.CardId == "S01-0116":
            {
                var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (declared.Length is not (0 or 3)) return CommandResult.Reject("手牌目标、战场与登场位置声明不完整");
                if (declared.Length == 3)
                {
                    var handCard = player.Hand.FirstOrDefault(card => card.InstanceId == declared[0] && card.CardType == "legion" && card.CardId != "S01-0116" && card.Troops <= 2000);
                    var battlefield = ParseEffectEntryBattlefieldChoice(declared[1]);
                    var (row, slot) = ParseSlot(declared[2]);
                    if (handCard is null || battlefield is null
                        || !EffectEntryBattlefieldChoices(playerIndex, handCard).Contains(battlefield.Value)
                        || row is < 0 or > 1 || slot is < 0 or > 2 || State.Players[battlefield.Value].Field[row][slot] is not null)
                        return CommandResult.Reject("声明的手牌目标、战场或位置不再合法");
                }
                if (!returnMoralePrepaid && !CanReturnMorale(player, 1)) return CommandResult.Reject("需要返还1张士气");
                if (!returnMoralePrepaid) ReturnMorale(player, 1);
                RemoveFromField(player, source, true, "被西施效果弃置", leaveKind: L12FieldLeaveKind.Discard);
                break;
            }
            case "destroyInfiltrator" when source.CardId == "S01-0004":
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要消耗2张活跃士气");
                break;
            case "revealHidden" when source.CardId == "S01-0415":
                if (!source.Hidden) return CommandResult.Reject("服部半藏当前已经为正面");
                break;
            default:
                return TryCommitS1FactionActiveAbility(playerIndex, source, ability, target, onceKey, useTombGuards, returnMoralePrepaid);
        }
        var data = new Dictionary<string, string> { ["ability"] = ability };
        if (!string.IsNullOrWhiteSpace(target)) data["target"] = target;
        if (ability == "xishiExchange")
        {
            var values = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var declared = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["summonMode"] = [values.Length == 3 ? "mode:summon" : "mode:none"],
            };
            if (values.Length == 3)
            {
                declared["entryCard"] = [values[0]];
                declared["entryBattlefield"] = [values[1]];
                declared["entrySlot"] = [values[2]];
            }
            foreach (var pair in CompositeFirstSegmentData("active:S01-0116:xishiExchange", declared))
                data[pair.Key] = pair.Value;
        }
        PushEffect(playerIndex, source, "active", "主动效果", data: data);
        return CommandResult.Ok();
    }

    private bool TryResolveS1ExtendedActive(L12StackItem item, L12CardInstance? source, string ability)
    {
        var player = State.Players[item.Controller];
        switch (ability)
        {
            case "extendedRange" when source is not null:
                source.CanAttackBackAndMasterUntilTurn = State.TurnSerial;
                FinishStackItem(item); return true;
            case "xishiExchange":
            {
                switch (AtomicFlowKey(item))
                {
                    case "xishi-summon":
                    {
                        var declared = item.Data.GetValueOrDefault("target", string.Empty)
                            .Split('|', StringSplitOptions.RemoveEmptyEntries);
                        if (declared.Length == 3 && ParseEffectEntryBattlefieldChoice(declared[1]) is { } battlefield)
                            SummonFromHand(player, declared[0], declared[2], tapped: false, battlefield);
                        FinishStackItem(item); return true;
                    }
                    case "xishi-draw":
                        Draw(player, 1); FinishStackItem(item); return true;
                    default:
                        FinishStackItem(item); return true;
                }
            }
            case "destroyInfiltrator" when source is not null:
                if (FindPublicCard(source.InstanceId, out var battlefieldController) is not null)
                    KillTarget(item, source.InstanceId, "被主动效果击杀");
                FinishStackItem(item); return true;
            default:
                return TryResolveS1FactionActive(item, source, ability);
        }
    }

    private void BeginZhugePeek(L12StackItem item)
    {
        if (!CanReturnMorale(State.Players[item.Controller], 1)) { FinishStackItem(item); return; }
        CreatePrompt(item.Controller, "optional", "诸葛亮：是否返还1士气，展示牌库顶部1张牌并加入手牌？", ["yes", "no"], 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "zhuge-peek-pay" });
    }

    private static IEnumerable<L12CardInstance> PublicLegions(L12PlayerState player)
        => player.Field.SelectMany(row => row).Where(card => card is not null && !card.Hidden && IsFieldLegion(card)).Cast<L12CardInstance>();

    private static IEnumerable<L12CardInstance> PublicFactionLegions(L12PlayerState player, string faction)
        => PublicLegions(player).Where(card => L12StructuredCardRules.HasFaction(player, card, faction));

    private void BeginBlackbeardSimultaneousDiscard(L12StackItem item)
    {
        var prompted = 0;
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var hand = State.Players[playerIndex].Hand;
            var actual = Math.Min(2, hand.Count);
            item.Data[$"teach-discard:{playerIndex}"] = string.Empty;
            if (actual == 0) continue;
            CreatePrompt(playerIndex, "discard", "黑胡子蒂奇：弃置合计2张手牌",
                hand.Select(card => card.InstanceId), actual, actual, "card-effect", item.StackItemId, isPrivate: true,
                data: new Dictionary<string, string>
                {
                    ["action"] = "teach-discard", ["sourceZone"] = "hand", ["layout"] = "single-row",
                    ["simultaneous"] = "true",
                });
            prompted++;
        }
        if (prompted > 0) return;
        FinishStackItem(item);
    }

    private void PromptDiscard(L12StackItem item, int playerIndex, int count, string text, string action)
    {
        var hand = State.Players[playerIndex].Hand;
        var actual = Math.Min(count, hand.Count);
        if (actual == 0)
        {
            FinishStackItem(item);
            return;
        }
        CreatePrompt(playerIndex, "discard", text, hand.Select(card => card.InstanceId), actual, actual, "card-effect", item.StackItemId,
            data: new Dictionary<string, string> { ["action"] = action, ["sourceZone"] = "hand", ["layout"] = "single-row" });
    }

    private void MoveHandToGrave(L12PlayerState player, string instanceId, bool causedByEffect,
        L12CardInstance? source = null)
    {
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return;
        player.Hand.Remove(card);
        player.Graveyard.Add(card);
        var authoritativeSource = source;
        if (authoritativeSource is null && State.IsResolvingStack && State.EffectStack.LastOrDefault() is { } origin)
            authoritativeSource = FindAuthoritativeCard(origin.SourceInstanceId) ?? origin.SourceSnapshot;
        if (authoritativeSource is not null
            && (authoritativeSource.CardType == "master" || authoritativeSource.CardId == player.MasterId))
            player.HandDiscardedByMasterThisTurn = true;
        AddEvent("discard", player.PlayerIndex, $"{player.Name}弃置{card.Name}", card);
        NotifyCardDiscarded(player, card, "hand", causedByEffect);
    }

    private void MoveGraveToHand(L12PlayerState player, string instanceId)
    {
        var card = player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return;
        if (!CanEnterHandOrLibrary(card)) { AddEvent("replacement", player.PlayerIndex, $"{card.Name}不能进入手牌，仍置于墓地", card); return; }
        player.Graveyard.Remove(card); AddCardToHandByEffect(player, card, "graveyard", $"{card.Name}从墓地回到手牌"); AddEvent("return", player.PlayerIndex, $"{card.Name}从墓地回到手牌", card);
    }

    private int ExpiryAtNextOwnEnd(int controller) => State.TurnSerial + (State.ActivePlayer == controller ? 0 : 1);
    private int ExpiryAtNextOwnStart(int controller) => State.TurnSerial + (State.ActivePlayer == controller ? 1 : 0);

    private static void AddTimedModifier(L12CardInstance card, int troops, int cost, int expiry, string source)
    {
        card.TimedModifiers.Add(new L12TimedModifier { TroopsDelta = troops, CostDelta = cost, ExpiresAfterTurn = expiry, Source = source });
        card.Troops += troops; card.CostModifier += cost;
    }

    private void CompleteCampPick(L12StackItem item, string choice)
    {
        var player = State.Players[item.Controller];
        var topIds = item.Data["camp-top"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (choice != "skip")
        {
            var selected = player.Library.First(card => card.InstanceId == choice); player.Library.Remove(selected); AddCardToHandByEffect(player, selected, "library", $"{selected.Name}因效果加入手牌");
        }
        var remaining = topIds.Where(id => id != choice && player.Library.Any(card => card.InstanceId == id)).ToArray();
        if (remaining.Length <= 1)
        {
            CompleteCampOrder(item, remaining.ToList());
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "camp-order",
            ["placementMode"] = "all-bottom",
            ["layout"] = "single-row",
            ["displayCardIds"] = string.Join('|', remaining),
        };
        foreach (var id in remaining)
        {
            var card = player.Library.First(candidate => candidate.InstanceId == id);
            AddPromptCardData(data, card);
        }
        CreatePrompt(item.Controller, "order", "野外扎营：调整其余卡牌的顺序，然后全部放回牌库底部。",
            remaining, remaining.Length, remaining.Length, "card-effect", item.StackItemId, data: data);
    }

    private void CompleteCampOrder(L12StackItem item, List<string> order)
    {
        var player = State.Players[item.Controller];
        foreach (var id in order)
        {
            var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == id); if (card is null) continue;
            player.Library.Remove(card); player.Library.Add(card);
        }
        FinishStackItem(item);
    }

    private IEnumerable<int> EffectEntryBattlefieldChoices(int ownerIndex, L12CardInstance card)
    {
        var candidates = card.CardId == "S01-0004" ? Enumerable.Range(0, State.Players.Length) : [ownerIndex];
        return candidates.Where(index => EmptySlots(State.Players[index]).Any());
    }

    private static string EffectEntryBattlefieldChoice(int playerIndex) => $"battlefield:{playerIndex}";

    private static int? ParseEffectEntryBattlefieldChoice(string? choice)
        => choice is not null && choice.StartsWith("battlefield:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(choice.AsSpan("battlefield:".Length), out var playerIndex)
            && playerIndex is >= 0 and <= 1
                ? playerIndex
                : null;

    private void SummonFromHand(L12PlayerState player, string cardId, string slotChoice, bool tapped, int? targetPlayerIndex = null)
    {
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == cardId); if (card is null) return;
        var battlefield = targetPlayerIndex ?? player.PlayerIndex;
        if (battlefield != player.PlayerIndex && card.CardId != "S01-0004") return;
        var (row, slot) = ParseSlot(slotChoice);
        if (row is < 0 or > 1 || slot is < 0 or > 2 || State.Players[battlefield].Field[row][slot] is not null) return;
        player.Hand.Remove(card); card.OwnerIndex ??= player.PlayerIndex; card.Tapped = tapped; card.SummonRound = State.Round;
        State.Players[battlefield].Field[row][slot] = card;
        AddEvent("put", battlefield, $"{card.Name}{(tapped ? "休整" : "活跃")}登场", card);
        ApplyDisasterLevelOnEntry(battlefield, card, deferTriggerUntilStackSettles: true);
        if (HasImmediateEffect(card, "enter"))
            QueueOrPushTriggeredEffect(battlefield, card, "enter", "【登场时】效果");
        QueueS2GrailRoundTableEntry(battlefield, card);
    }

    private static bool IsCounterTactic(string cardId) => cardId is
        "S01-0016" or "S01-0017" or "S01-0018" or "S01-0019" or "S01-0020" or "S01-0021" or "S01-0120" or
        "S01-0223" or "S01-0224" or "S01-0320" or "S01-0420" or
        "S02-0015" or "S02-0016" or "S02-0017" or "S02-0018" or "S02-0106" or "S02-0523";

    private bool CanUseS1ReactionAtStack(string cardId, int playerIndex, L12StackItem top)
    {
        if (top.Controller == playerIndex) return false;
        var timing = ResponseTimingContext(top);
        var defendingPlayer = State.PendingDefense is null ? -1 : 1 - State.PendingDefense.AttackerPlayer;
        if (RequiresMoraleResponseCost(cardId) && !CanReturnMorale(State.Players[playerIndex], 1)) return false;
        if (L12StructuredCardRules.RequiresOwnLegionResponseTarget(cardId))
            return PublicLegions(State.Players[playerIndex]).Any()
                && (timing.Trigger == "opponent-attack"
                    ? playerIndex == defendingPlayer
                    : timing.Trigger is "enter" or "play" or "active" or "disaster");
        return cardId switch
        {
            "S01-0020" or "S01-0120" => timing.Trigger == "opponent-attack" && playerIndex == defendingPlayer,
            "S01-0224" => FindSource(top)?.CardType is "tactic" or "artifact",
            _ => false,
        };
    }

    private void CommitS1ReactionResponse(int playerIndex, L12CardInstance response, string targetStackId,
        string? declaredTarget = null, IReadOnlyDictionary<string, string>? data = null)
    {
        var player = State.Players[playerIndex];
        if (FindOnField(player, response.InstanceId, out var row, out var slot) is not null) player.Field[row][slot] = null;
        response.Hidden = false;
        player.Resolving.Add(response);
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}", Controller = playerIndex,
            SourceInstanceId = response.InstanceId, SourceCardId = response.CardId, SourceName = response.Name,
            Trigger = "reaction", Text = "反击战术效果",
        };
        item.Targets.Add(targetStackId);
        if (!string.IsNullOrWhiteSpace(declaredTarget)) item.Data["target"] = declaredTarget;
        if (data is not null)
            foreach (var pair in data) item.Data[pair.Key] = pair.Value;
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{player.Name}发动〈{response.Name}〉", response);
        PublishEffectPresentation("effect-response", playerIndex, response, item.Trigger, item.Text, item.Data);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void ResolveS1ReactionEffect(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var source = FindOnField(player, item.SourceInstanceId, out var sourceRow, out var sourceSlot);
        if (source is not null)
        {
            player.Field[sourceRow][sourceSlot] = null;
            source.Hidden = false;
            player.Resolving.Add(source);
        }
        switch (AtomicFlowKey(item))
        {
            case "伏击":
            {
                var target = PublicLegions(player).FirstOrDefault(card => card.InstanceId == item.Data.GetValueOrDefault("target"));
                if (target is not null) AddTimedModifier(target, 2000, 0, State.TurnSerial, "伏击");
                FinishStackItem(item);
                return;
            }
            case "战斗至黎明":
            case "battle-until-dawn-buff":
                foreach (var target in PublicLegions(player)) AddTimedModifier(target, 1000, 0, State.TurnSerial, "战斗至黎明");
                FinishStackItem(item); return;
            case "battle-until-dawn-draw":
                if (player.Graveyard.Count >= 5) Draw(player, 1);
                FinishStackItem(item); return;
            case "空城计":
            case "empty-city-block":
            {
                var targetStack = State.EffectStack.FirstOrDefault(stack => stack.StackItemId == item.Targets.FirstOrDefault());
                if (targetStack is not null) targetStack.Negated = true;
                FinishStackItem(item);
                return;
            }
            case "empty-city-draw":
                if (!player.Field[0].Any(card => card is not null && IsFieldLegion(card))) Draw(player, 1);
                FinishStackItem(item); return;
            case "拼死反抗":
            {
                var declared = item.Data.GetValueOrDefault("declaredTargets");
                if (declared == "mode:all")
                {
                    foreach (var target in PublicLegions(State.Players[1 - item.Controller]).Where(card => card.Tapped))
                        AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "拼死反抗");
                }
                else
                {
                    var target = PublicLegions(State.Players[1 - item.Controller])
                        .FirstOrDefault(card => card.InstanceId == declared && card.Tapped);
                    if (target is not null) AddTimedModifier(target, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), "拼死反抗");
                }
                FinishStackItem(item);
                return;
            }
            case "切腹仪式":
                Draw(player, 1);
                var seppukuTarget = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("declaredTargets"));
                if (seppukuTarget is not null)
                    AddTimedModifier(seppukuTarget, 0, -2, ExpiryAtNextOwnEnd(item.Controller), "切腹仪式");
                FinishStackItem(item);
                return;
            case "摄政皇权":
            {
                var declaredCard = PublicTriggerDeclared(item, "entryCard");
                if (!string.IsNullOrWhiteSpace(declaredCard))
                {
                    var battlefield = ParseEffectEntryBattlefieldChoice(PublicTriggerDeclared(item, "entryBattlefield"))
                        ?? item.Controller;
                    SummonFromHand(player, declaredCard, PublicTriggerDeclared(item, "entrySlot"), tapped: false, battlefield);
                    FinishStackItem(item); return;
                }
                FinishStackItem(item); return;
            }
            case "复仇血鹰":
            case "blood-eagle-debuff":
                foreach (var target in PublicLegions(State.Players[1 - item.Controller]))
                    AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "复仇血鹰");
                FinishStackItem(item);
                return;
            case "blood-eagle-recover":
            {
                var order = CompositeDeclared(item, "graveOrder");
                if (order is not [var handId, var bottomId]) { FinishStackItem(item); return; }
                var handCard = player.Graveyard.FirstOrDefault(card => card.InstanceId == handId);
                var bottomCard = player.Graveyard.FirstOrDefault(card => card.InstanceId == bottomId);
                if (handCard is not null)
                {
                    player.Graveyard.Remove(handCard);
                    AddCardToHandByEffect(player, handCard, "graveyard", $"{handCard.Name}从墓地加入手牌");
                }
                if (bottomCard is not null)
                {
                    player.Graveyard.Remove(bottomCard);
                    player.Library.Add(bottomCard);
                    AddEvent("return", item.Controller, $"〈{bottomCard.Name}〉从墓地置于牌库底部", bottomCard);
                }
                FinishStackItem(item);
                return;
            }
            case "锡瓦的卡巴":
                if (!string.IsNullOrWhiteSpace(PublicTriggerDeclared(item, "entrySlot")))
                {
                    SummonFromHand(player, item.SourceInstanceId, PublicTriggerDeclared(item, "entrySlot"), false);
                    var lockedMorale = player.Morale.FirstOrDefault(card => card.Tapped);
                    if (lockedMorale is not null) lockedMorale.CannotUntapUntilRound = State.Round + 1;
                    FinishStackItem(item); return;
                }
                FinishStackItem(item); return;
            case "不朽之礼":
                if (!Draw(player, 1)) { FinishStackItem(item); return; }
                var declaredGuard = PublicTriggerDeclared(item, "entryCard");
                if (!string.IsNullOrWhiteSpace(declaredGuard) && declaredGuard != "mode:none"
                    && player.Graveyard.Any(card => card.InstanceId == declaredGuard && card.CardId == "S01-0212")
                    && EmptySlots(player).Contains(PublicTriggerDeclared(item, "entrySlot"), StringComparer.OrdinalIgnoreCase))
                {
                    SummonFromAnyPrivateZone(player, declaredGuard, PublicTriggerDeclared(item, "entrySlot"), false);
                    FinishStackItem(item); return;
                }
                if (item.Data.ContainsKey("declared:entryCard")) { FinishStackItem(item); return; }
                FinishStackItem(item); return;
            case "智慧法典 卷一":
            {
                var opponent = State.Players[1 - item.Controller];
                var choices = opponent.Hand.Select(card => card.InstanceId).Append("abandon").ToArray();
                CreatePrompt(1 - item.Controller, "optional-card", "智慧法典：弃置1张手牌以继续发动本次效果，或放弃当前效果",
                    choices, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "wisdom-discard",
                        ["choiceMode"] = "instant",
                        ["abandon"] = "不弃牌，放弃当前效果",
                    });
                return;
            }
            default:
                FinishStackItem(item); return;
        }
    }

    private void ResolveWisdomCodexReward(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item))
        {
            case "wisdom-draw":
                Draw(player, 1);
                FinishStackItem(item);
                return;
            case "wisdom-recover":
                var target = CompositeDeclared(item, "recoverTarget").SingleOrDefault();
                if (target is not null) MoveGraveToHand(player, target);
                FinishStackItem(item);
                return;
            default:
                Draw(player, 1);
                FinishStackItem(item);
                return;
        }
    }

    private void QueueS1PostAttackReactions(int attackerPlayer)
    {
        var defenderIndex = 1 - attackerPlayer;
        var defender = State.Players[defenderIndex];
        var hasOpponentLegion = PublicLegions(State.Players[attackerPlayer]).Any();
        var hasRestedOpponentLegion = PublicLegions(State.Players[attackerPlayer]).Any(target => target.Tapped);
        var candidates = defender.Field[1].Where(card => card is not null
                && L12StructuredCardRules.CanOfferPostAttackReaction(card.CardId, hasOpponentLegion,
                    hasRestedOpponentLegion)).Cast<L12CardInstance>()
            .Select(counter => IsTrojanHorse(counter)
                ? CreateTriggerCandidate(defenderIndex, counter, "trojan-after-attack", "【对方进攻后】反击战术",
                    new Dictionary<string, string> { ["attacker"] = attackerPlayer.ToString() })
                : CreateTriggerCandidate(defenderIndex, counter, "reaction", "【对方进攻后】反击战术"))
            .Concat(defender.Hand.Where(card => card.CardId == "S01-0213")
                .Select(kaba => CreateTriggerCandidate(defenderIndex, kaba, "reaction", "【对方进攻后】手牌效果")))
            .ToArray();
        QueueTriggerCandidates(candidates);
    }

    private void QueueS1MasterDamageReaction(int damagedPlayer, int? sourcePlayer, bool effectDamage)
    {
        var player = State.Players[damagedPlayer];
        var candidates = new List<L12TriggerCandidate>();
        var counter = player.Field[1].FirstOrDefault(card => card is { CardId: "S01-0021" });
        if (counter is not null)
            candidates.Add(CreateTriggerCandidate(damagedPlayer, counter, "reaction", "【主宰受到伤害时】反击战术"));
        if (player.MasterId == "S01-02M3" && sourcePlayer == 1 - damagedPlayer
            && player.Graveyard.Any(card => card.CardId == "S01-0212")
            && !player.UsedAbilities.Contains("trigger:medjedDamageResponse")
            && player.UsedAbilities.Add("pending:medjedDamageResponse"))
        {
            var master = CreateCard(player.MasterId, $"master-{damagedPlayer}");
            candidates.Add(CreateTriggerCandidate(damagedPlayer, master, "medjed-master-damage", "【主宰受到伤害时】效果"));
        }
        if (effectDamage && State.ActivePlayer == damagedPlayer)
        {
            candidates.AddRange(PublicLegions(player)
                .Where(card => card.CardId == "S02-0304" && !card.Tapped)
                .Select(card => CreateTriggerCandidate(damagedPlayer, card, "active", "【我方主宰因效果受到伤害时】效果",
                    new Dictionary<string, string> { ["ability"] = "margaretMasterDamage" })));
        }
        if (BuildAnderstorpRingDrawCandidate(damagedPlayer) is { } ringDraw)
            candidates.Add(ringDraw);
        QueueTriggerCandidates(candidates);
    }

    private IEnumerable<L12TriggerCandidate> BuildS1LeaveReactionCandidates(int owner, L12CardInstance left)
    {
        var player = State.Players[owner];
        var candidates = new List<L12TriggerCandidate>();
        if (left.CardId == "S01-0417" && player.MasterId == "S01-04M2")
            candidates.Add(CreateTriggerCandidate(owner, left, "play", "【离场时】效果"));
        if (left.CardId == "S01-0204" && left.LastKnownAttachedCardIds.Count > 0)
            candidates.Add(CreateTriggerCandidate(owner, left, "leave", "【离场时】效果"));
        if (!IsFieldLegion(left)) return candidates;
        var bloodEagle = player.Field[1].FirstOrDefault(card => card is { CardId: "S01-0320" });
        if (bloodEagle is not null) candidates.Add(CreateTriggerCandidate(owner, bloodEagle, "reaction", "【我方军团阵亡时】反击战术"));
        if (left.CurrentCost > 2 && State.ActivePlayer != owner)
        {
            var gift = player.Field[1].FirstOrDefault(card => card is { CardId: "S01-0223" });
            if (gift is not null) candidates.Add(CreateTriggerCandidate(owner, gift, "reaction", "【我方高费用军团离场时】反击战术"));
        }
        return candidates;
    }
}
