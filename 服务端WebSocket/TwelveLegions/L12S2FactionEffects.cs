namespace TwelveLegions.Server;

// 第二季阵营卡效。只收录已经能由规则文本唯一确定的流程；需要额外裁定的卡留在 OPEN-QUESTIONS 中。
public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> S2FactionEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0101", "S02-0102", "S02-0103", "S02-0203", "S02-0204", "S02-0205",
        "S02-0301", "S02-0302", "S02-0303", "S02-0304", "S02-0401", "S02-0402", "S02-0403", "S02-0404",
        "S02-0501", "S02-0502", "S02-0505", "S02-0506", "S02-0507", "S02-0509", "S02-0511", "S02-0513", "S02-0514", "S02-0515", "S02-0517", "S02-0518", "S02-0520", "S02-0611", "S02-0613",
        "S02-0601", "S02-0602", "S02-0603", "S02-0604", "S02-0606", "S02-0607", "S02-0608", "S02-0610", "S02-0612", "S02-0614", "S02-0616", "S02-0617", "S02-0618", "S02-0619",
    };

    private static readonly HashSet<string> S2FactionTacticCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0206", "S02-0207", "S02-0306", "S02-0307", "S02-0405", "S02-0406", "S02-0521", "S02-0522", "S02-0620", "S02-0621", "S02-0622",
    };

    private static readonly HashSet<string> S2FactionAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0103", "S02-0403", "S02-0501", "S02-0509", "S02-0511", "S02-0516", "S02-0517", "S02-0519", "S02-0605", "S02-0606", "S02-0607", "S02-0608", "S02-0612", "S02-0617",
    };

    private static readonly HashSet<string> S2FactionDeathCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-01S1", "S02-0202", "S02-0203", "S02-0301", "S02-0402", "S02-0508", "S02-0512", "S02-0518", "S02-0601", "S02-0609", "S02-0613", "S02-0615",
    };

    private static readonly HashSet<string> S2PromotionEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0501", "S02-0503", "S02-0505", "S02-0507",
    };

    private static readonly HashSet<string> S2FactionAfterAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0503", "S02-0602", "S02-0606", "S02-0611", "S02-0608",
    };

    private static bool IsS2FactionAfterAttackCard(string cardId) => S2FactionAfterAttackCards.Contains(cardId);

    private static bool HasS2FactionImmediateEffect(string cardId, string trigger)
        => trigger switch
        {
            "enter" => S2FactionEnterCards.Contains(cardId),
            "play" => S2FactionTacticCards.Contains(cardId),
            "attack" => S2FactionAttackCards.Contains(cardId),
            "death" => S2FactionDeathCards.Contains(cardId),
            "promotion-enter" => S2PromotionEnterCards.Contains(cardId),
            _ => false,
        };

    private static bool IsS2FactionDeathCard(string cardId) => S2FactionDeathCards.Contains(cardId);

    private L12CardInstance? FindKingsSwordOwner(L12PlayerState player)
        => PublicLegions(player).FirstOrDefault(candidate =>
            candidate.AttachedCards.Any(attached => L12StructuredCardSemantics.IsKingsSword(attached.CardId)));

    private static List<L12AbilityView> GetS2FactionAbilities(string cardId) => cardId switch
    {
        "S02-02M1" =>
        [
            new("nephthysSacrifice", "我方 回合1次：弃置我方战场任意数量的军团；每弃置1张，本回合下一张带有天灾等级的【太阳城】军团登场费用-1。"),
        ],
        "S02-0204" => [new("imhotepDiscount", "主动休整：本回合下1张带有天灾等级的【太阳城】军团登场费用-1")],
        "S02-0513" => [new("aristotleDiscount", "主动休整：本回合下一张【奥林匹斯】军团登场费用-1")],
        "S02-0205" =>
        [
            new("scarabSummon", "主动休整：将墓地1张〈增殖的甲虫〉活跃登场"),
            new("scarabDebuff", "我方回合1次：弃置1张手牌，选择对方最多2张军团，本回合兵力-1000"),
        ],
        "S02-0603" => [new("merlinRune", "主动休整：消耗1符文，选择敌方军团-3000，或检索费用不高于4的【主动战术】")],
        "S02-0604" => [new("galahadGrailReward", "《寻找圣杯之旅》完成后 可弃置此军团：抽取1张牌，我方主宰增加1点血量。")],
        "S02-0616" => [new("amakineTop", "主动休整 展示牌库顶部1张牌：若其只拥有【彼界】特征，可加入手牌；否则返回牌库顶部或底部。")],
        "S02-0404" =>
        [
            new("magatamaMove", "主动休整：选择战场上1张军团，进行1次骑兵位移。"),
            new("magatamaImmortal", "主动休整：选择我方1张本回合位移过的军团，本回合获得免死。"),
        ],
        _ => [],
    };

    /// <summary>
    /// 统一处理“从区域弃置”产生的卡牌自身触发。调用者必须明确标记手牌弃置
    /// 是否由效果造成；牌库弃置无论是效果还是支付费用，均符合〈信仰狂热者〉文本。
    /// </summary>
    private void NotifyCardDiscarded(L12PlayerState player, L12CardInstance card, string originZone, bool causedByEffect)
    {
        if (card.CardId != "S02-0006" || State.ActivePlayer != player.PlayerIndex) return;
        if (originZone != "library" && !(originZone == "hand" && causedByEffect)) return;

        var onceKey = $"trigger:faith-zealot:{card.InstanceId}";
        if (!player.UsedAbilities.Add(onceKey)) return;
        QueueTriggerCandidates(
        [
            CreateTriggerCandidate(player.PlayerIndex, card, "discard-trigger", "弃置时效果",
                new Dictionary<string, string> { ["originZone"] = originZone })
        ]);
    }

    private void ResolveS2DiscardTrigger(L12StackItem item)
    {
        if (item.SourceCardId != "S02-0006") { FinishStackItem(item); return; }
        item.Data["postResolutionGenerated"] = "faith-zealot-master";
        FinishStackItem(item);
    }

    private void QueueS2MasterMoraleReturnTriggers(int playerIndex, L12CardInstance master, int returned)
    {
        if (returned < 4 || master.CardType != "master") return;
        var player = State.Players[playerIndex];
        var candidates = new List<L12TriggerCandidate>();
        foreach (var liMu in PublicLegions(player).Where(card => card.CardId == "S02-0102"))
        {
            var onceKey = $"trigger:limu-morale:{liMu.InstanceId}:{State.TurnSerial}";
            var pendingKey = $"{onceKey}:pending";
            if (player.UsedAbilities.Contains(onceKey) || !player.UsedAbilities.Add(pendingKey)) continue;
            candidates.Add(CreateTriggerCandidate(playerIndex, liMu, "master-morale-return", "【主宰效果返还士气时】效果",
                new Dictionary<string, string>
                {
                    ["mode"] = "limu", ["onceKey"] = onceKey, ["returned"] = returned.ToString(),
                    ["cleanupReservation"] = pendingKey,
                }));
        }
        if (master.CardId == "S01-01M1"
            && !player.UsedAbilities.Contains($"trigger:xiaotian-morale:{State.TurnSerial}")
            && player.Field[0].Any(card => card is null)
            && PublicLegions(player).All(card => card.CardId != "S02-01S1"))
        {
            var xiaotian = CreateCard("S02-01S1", $"p{playerIndex}-xiaotian");
            candidates.Add(CreateTriggerCandidate(playerIndex, xiaotian, "master-morale-return", "【主宰效果返还士气时】效果",
                new Dictionary<string, string>
                {
                    ["mode"] = "xiaotian", ["ability"] = "xiaotianEntry",
                    ["onceKey"] = $"trigger:xiaotian-morale:{State.TurnSerial}",
                    ["returned"] = returned.ToString(),
                }));
        }
        QueueTriggerCandidates(candidates);
    }

    private void ResolveS2MasterMoraleReturn(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var onceKey = item.Data.GetValueOrDefault("onceKey") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(onceKey) || !player.UsedAbilities.Contains(onceKey))
        {
            FinishStackItem(item);
            return;
        }
        if (item.Data.GetValueOrDefault("mode") == "limu")
        {
            if (player.MoraleDeck.Count > 0)
            {
                AddMorale(player, 1, tapped: true);
                AddEvent("morale", item.Controller, "李牧从士气牌库追加1张休整士气",
                    FindSource(item) is { } liMu ? [liMu] : []);
            }
            else
                AddEvent("effect-cancelled", item.Controller, "李牧结算时士气牌库已空；无法追加士气，回合次数不恢复");
            FinishStackItem(item);
            return;
        }
        if (item.Data.GetValueOrDefault("mode") != "xiaotian" || player.Field[0].All(card => card is not null))
        {
            FinishStackItem(item);
            return;
        }
        var destination = PublicTriggerDeclared(item, "slot");
        if (!player.UsedAbilities.Contains(onceKey)
            || !Enumerable.Range(0, 3).Where(slot => player.Field[0][slot] is null)
                .Select(slot => $"0:{slot}").Contains(destination, StringComparer.OrdinalIgnoreCase))
        {
            AddEvent("effect-cancelled", item.Controller, "哮天犬·稚选择的前排登场位置已失效；该军团不登场");
            FinishStackItem(item);
            return;
        }
        var xiaotian = player.Graveyard.LastOrDefault(card => card.CardId == "S02-01S1")
            ?? player.Removed.LastOrDefault(card => card.CardId == "S02-01S1")
            ?? CreateCard("S02-01S1", $"p{item.Controller}-xiaotian");
        var (row, slot) = ParseSlot(destination);
        player.Graveyard.Remove(xiaotian);
        player.Removed.Remove(xiaotian);
        xiaotian.Tapped = false;
        xiaotian.SummonRound = State.Round;
        player.Field[row][slot] = xiaotian;
        AddEvent("enter", item.Controller, "〈哮天犬·稚〉在前排活跃登场", xiaotian);
        FinishStackItem(item);
    }

    private static bool IsTrialLegion(L12CardInstance card)
        => card.CardId is "S02-0604" or "S02-0610" or "S02-0614";

    private static bool IsProtectedByRestedAmakine(L12PlayerState owner, L12CardInstance target)
        => !target.Tapped && IsTrialLegion(target)
            && PublicLegions(owner).Any(card => card.CardId == "S02-0616" && card.Tapped);

    private IEnumerable<string> EffectCavalryDestinations(L12PlayerState battlefield)
        => EmptySlots(battlefield).Where(choice => State.ActiveDisaster?.CardId != "S01-DS03"
            || !choice.StartsWith("1:", StringComparison.Ordinal));

    private bool AdvanceTrial(int playerIndex, int count, L12CardInstance? source = null)
    {
        var player = State.Players[playerIndex];
        var trial = player.SpecialZones.Trials.FirstOrDefault(card => !card.TrialCompleted);
        if (trial is null || count <= 0) return false;
        var before = trial.TrialProgress;
        trial.TrialProgress = Math.Min(8, trial.TrialProgress + count);
        player.SpecialZones.TrialLevel = trial.TrialProgress;
        AddEvent("trial", playerIndex, $"《{trial.Name}》试炼进度 {before} → {trial.TrialProgress}", source ?? trial);
        return trial.TrialProgress > before;
    }

    private bool SourceIsFieldCard(int playerIndex, string? instanceId, out L12CardInstance card)
    {
        var found = FindOnField(State.Players[playerIndex], instanceId, out _, out _);
        card = found!;
        return found is not null;
    }

    private bool TryResolveS2FactionEnter(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "亚瑟王":
                if (player.SpecialZones.Runes < 1) { FinishStackItem(item); return true; }
                var existingSwordOwner = FindKingsSwordOwner(player);
                var promptText = existingSwordOwner is null
                    ? "亚瑟王：是否消耗1符文，将〈王者之剑〉叠放在此军团下方？"
                    : "场上已存在〈王者之剑〉。若继续发动，仍会消耗1符文，但不会叠放、生成或转移〈王者之剑〉。";
                CreatePrompt(item.Controller, "optional", promptText,
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-arthur-sword",
                        ["yes"] = existingSwordOwner is null ? "消耗1符文并叠放〈王者之剑〉" : "继续支付并发动",
                        ["no"] = existingSwordOwner is null ? "不发动" : "取消",
                    });
                return true;
            case "limu-reveal":
                RevealS2LiMuTop(item);
                return true;
            case "limu-draw":
                if (player.Library.Count > 0)
                {
                    if (!Draw(player, 1)) SetWinner(1 - item.Controller, "〈李牧〉效果抽牌时牌库为空");
                }
                else
                    AddEvent("effect-cancelled", item.Controller, "李牧结算抽牌效果时牌库已空；无法抽牌", card);
                FinishStackItem(item);
                return true;
            case "平阳昭公主":
                player.NextMasterDamageToOpponentBecomesTwoUntilTurn = State.TurnSerial;
                AddEvent("effect", item.Controller,
                    "平阳昭公主使本回合我方主宰对对方主宰造成的下一次伤害变为2", card);
                FinishStackItem(item);
                return true;
            case "赫拉克勒斯·晋升":
                CreatePrompt(item.Controller, "optional", "是否对双方主宰各造成1点非致命伤害？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-heracles-entry-damage" });
                return true;
            case "赫拉克勒斯":
                CreatePrompt(item.Controller, "optional", "赫拉克勒斯：是否抽取2张牌，并弃置1张手牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-heracles-draw-discard-choice",
                        ["yes"] = "抽取2张牌，并弃置1张手牌",
                        ["no"] = "不发动",
                    });
                return true;
            case "海伦":
                if (!player.Morale.Any(morale => morale.IsGodPower))
                {
                    FinishStackItem(item);
                    return true;
                }
                PromptDiscard(item, 1 - item.Controller, 1, "海伦：对方弃置1张手牌", "s2-helen-entry-discard");
                return true;
            case "亚里士多德":
                return PromptS2FlipMorale(item, card, optional: true);
            case "匠神锻造炉":
                return PromptS2FlipMorale(item, card, optional: true);
            case "忒修斯":
                return PromptS2FlipMorale(item, card, optional: true, onlyTapped: true);
            case "圣女贞德":
            {
                var choices = player.Hand.Select(candidate => candidate.InstanceId).ToList();
                if (choices.Count == 0) { FinishStackItem(item); return true; }
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "圣女贞德：可弃置1张手牌，使我方主宰直到下个我方回合开始前无法被进攻", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-joan-master-guard" });
                return true;
            }
            // Batch 6F：四张牌的公开模式与冒号前休整/符文费用均已在触发候选阶段声明。
            // 合法 StackItem 会先由 TryResolveTrialAdvanceEffect 消费；这里仅关闭无声明的旧旁路。
            case "加拉哈德" or "兰斯洛特" or "芬恩" or "康斯坦丝":
                FinishStackItem(item);
                return true;
            case "罗宾汉":
            {
                var candidates = player.Hand.Concat(player.Library).Concat(player.Graveyard)
                    .Where(candidate => candidate.CardId == "S02-0609").ToArray();
                if (candidates.Length == 0) { FinishStackItem(item); return true; }
                var squires = candidates.Select(candidate => candidate.InstanceId).Append("skip").ToArray();
                var data = new Dictionary<string, string>
                {
                    ["action"] = "s2-robin-summon-squire",
                    ["skip"] = "不发动",
                    ["layout"] = "single-row",
                    ["displayCardIds"] = string.Join('|', candidates.Select(candidate => candidate.InstanceId)),
                };
                foreach (var candidate in candidates)
                {
                    AddPromptCardData(data, candidate);
                    data[$"{candidate.InstanceId}:zone"] = player.Hand.Contains(candidate) ? "手牌"
                        : player.Library.Contains(candidate) ? "牌库" : "墓地";
                }
                CreatePrompt(item.Controller, "optional-card", "罗宾汉：可从手牌、牌库或墓地选择1张〈侍从骑士〉活跃登场",
                    squires, 1, 1, "card-effect", item.StackItemId,
                    data: data);
                return true;
            }
            case "克劳迪娅":
            {
                if (player.SpecialZones.Runes < 1) { FinishStackItem(item); return true; }
                var targets = PublicLegions(State.Players[1 - item.Controller]).Select(target => target.InstanceId).ToList();
                if (targets.Count == 0) { FinishStackItem(item); return true; }
                targets.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "克劳迪娅：可消耗1符文，选择对方1张军团本回合兵力-2000",
                    targets, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-claudia-debuff", ["skip"] = "不发动" });
                return true;
            }
            case "狮心王理查一世":
                AdvanceTrial(item.Controller, 2, card);
                GrantImmortalUntilNextTurnStart(card, item.Controller);
                return PromptS2RichardEntryAttach(item, card);
            case "库丘林":
                GrantImmortalUntilNextTurnStart(card, item.Controller);
                card.ImmortalRequiresFrontRow = true;
                AddEvent("effect", item.Controller, $"{card.Name}直到下个我方回合开始前，在前排获得免死", card);
                FinishStackItem(item);
                return true;
            case "八尺琼勾玉":
            {
                var choices = player.Library
                    .Where(candidate => L12StructuredCardRules.HasFaction(player, candidate, "gaotianyuan")
                        && candidate.CardType == "legion"
                        && candidate.Profession == "骑兵")
                    .Select(candidate => candidate.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card",
                    "八尺琼勾玉：可查看牌库，选择1张【高天原】的【骑兵】军团展示并加入手牌，随后重洗牌库",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-magatama-search",
                        ["choiceMode"] = "optional-add",
                        ["skip"] = "不加入手牌",
                    });
                return true;
            }
            case "卡纽特大帝":
            {
                var choices = PublicLegions(player).Concat(player.Graveyard)
                    .Where(candidate => candidate.CardType == "legion" && candidate.Faction == "asgard"
                        && candidate.InstanceId != card.InstanceId && HasDeathTrigger(candidate))
                    .Select(candidate => candidate.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                var data = new Dictionary<string, string>
                {
                    ["action"] = "s2-canute-trigger-deaths",
                    ["selectionConstraint"] = "distinct-card-names",
                    ["layout"] = "single-row",
                };
                foreach (var candidateId in choices)
                    if (FindPromptCard(item.Controller, candidateId) is { } candidate) AddPromptCardData(data, candidate);
                CreatePrompt(item.Controller, "cards", "卡纽特大帝：可选择我方战场或墓地最多2张非同名的【阿斯加德】军团，触发其阵亡时效果",
                    choices, 0, Math.Min(2, choices.Length), "card-effect", item.StackItemId, data: data);
                return true;
            }
            case "玛格丽特一世":
                if (PublicTriggerDeclared(item, "mode") == "mode:use")
                    Mill(player, 1, "玛格丽特一世登场时效果");
                FinishStackItem(item);
                return true;
            case "珀尔修斯":
            {
                var promotion = player.Graveyard.FirstOrDefault(candidate => candidate.CardId == "S02-0505");
                if (promotion is null || player.Hand.Count == 0) { FinishStackItem(item); return true; }
                var choices = player.Hand.Select(candidate => candidate.InstanceId).Append("skip").ToArray();
                var data = new Dictionary<string, string>
                {
                    ["action"] = "s2-perseus-recover-promotion", ["choiceMode"] = "optional-add", ["skip"] = "不发动",
                };
                foreach (var handCard in player.Hand) AddPromptCardData(data, handCard);
                CreatePrompt(item.Controller, "hand-card", "珀尔修斯：可弃置1张手牌，将墓地1张〈珀尔修斯·晋升〉加入手牌",
                    choices, 1, 1, "card-effect", item.StackItemId, data: data);
                return true;
            }
            case "柏拉图":
                BeginFactionTopSearch(item, 3, "olympus", "S02-0514", "s2-plato-search");
                return true;
            case "井伊直虎":
            {
                var choices = player.Hand.Select(candidate => candidate.InstanceId).ToList();
                var targets = PublicFactionLegions(player, "gaotianyuan").Where(candidate => candidate.Tapped)
                    .Select(candidate => candidate.InstanceId).ToList();
                if (choices.Count == 0 || targets.Count == 0) { FinishStackItem(item); return true; }
                item.Data["s2-gaotianyuan-ready-targets"] = string.Join('|', targets);
                CreatePrompt(item.Controller, "hand-card", "弃置1张手牌：选择1张休整的【高天原】军团转为活跃", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-gaotianyuan-ready-discard" });
                return true;
            }
            case "冲田总司":
            {
                var grassSwordInFront = player.Field[0].Any(candidate => candidate?.CardId == "S01-0417");
                if (grassSwordInFront)
                {
                    card.HasCharge = true;
                    AddTimedModifier(card, 1000, 0, ExpiryAtNextOwnEnd(item.Controller), "冲田总司");
                    AddEvent("effect", item.Controller, "〈草薙剑〉位于前排，冲田总司获得冲锋且本回合兵力+1000", card);
                }
                FinishStackItem(item);
                return true;
            }
            case "始皇帝 嬴政":
            {
                if (item.Data.GetValueOrDefault("entryCostPaid") == "true")
                {
                    foreach (var pair in CompositeFirstSegmentData("trigger:S02-0101:enter",
                                 new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)))
                        item.Data[pair.Key] = pair.Value;
                    ResolveYingzhengKillSegment(item);
                    FinishStackItem(item);
                    return true;
                }
                AddEvent("reveal", item.Controller, "始皇帝 嬴政登场时未满足发动条件，展示我方所有手牌", player.Hand.ToArray());
                FinishStackItem(item);
                return true;
            }
            case "yingzheng-kill":
                ResolveYingzhengKillSegment(item);
                FinishStackItem(item);
                return true;
            case "yingzheng-return":
                ResolveYingzhengReturnSegment(item);
                FinishStackItem(item);
                return true;
            case "哈特谢普苏特":
            case "黄金圣甲虫":
            {
                var declaredScarab = PublicTriggerDeclared(item, "entryCard");
                if (!string.IsNullOrWhiteSpace(declaredScarab))
                {
                    _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, declaredScarab,
                        PublicTriggerDeclared(item, "entrySlot"), tapped: false);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            }
            case "伊姆何泰普":
            {
                if (player.Hand.Count >= State.Players[1 - item.Controller].Hand.Count) { FinishStackItem(item); return true; }
                var choices = player.Graveyard.Where(candidate => candidate.Faction == "taiyangcheng" && candidate.CardType == "legion" && candidate.Cost >= 6)
                    .Select(candidate => candidate.InstanceId).ToList();
                if (choices.Count == 0) { FinishStackItem(item); return true; }
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "伊姆何泰普：可将墓地1张费用为6及以上的【太阳城】军团加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-imhotep-recover" });
                return true;
            }
            case "武田信玄":
            {
                var choices = player.Library.Where(candidate => L12StructuredCardRules.HasFaction(player, candidate, "gaotianyuan")
                        && candidate.CardType == "legion" && candidate.BaseTroops <= 5000)
                    .Select(candidate => candidate.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "武田信玄：可查看牌库并选择1张兵力不高于5000的【高天原】军团展示并加入手牌",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-takeda-search" });
                return true;
            }
            default:
                return false;
        }
    }

    private void ResolveS2PromotionEnter(L12StackItem item)
    {
        var card = FindSource(item);
        if (card is null) { FinishStackItem(item); return; }
        if (TryResolveBatch6JAEnterEffect(item, card)) return;
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "赫拉克勒斯·晋升":
            {
                var handLegions = player.Hand.Where(candidate => candidate.CardType == "legion").Select(candidate => candidate.InstanceId).ToList();
                if (handLegions.Count == 0) { FinishStackItem(item); return; }
                handLegions.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "可展示手牌中1张军团并放回牌库顶部，随后击杀费用不高于该牌费用的对方军团",
                    handLegions, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-heracles-promotion-show" });
                return;
            }
            case "阿喀琉斯·晋升":
                card.CanAttackLegionsOnSummonUntilTurn = State.TurnSerial;
                AddEvent("effect", item.Controller, $"{card.Name}本回合可以进攻对方军团", card);
                FinishStackItem(item);
                return;
            case "珀尔修斯·晋升":
            {
                var targets = State.Players[1 - item.Controller].Field.SelectMany(row => row)
                    .Where(target => target is { Tapped: true } && IsFieldLegion(target) && !target.Hidden)
                    .Select(target => target!.InstanceId).ToList();
                if (targets.Count == 0) { FinishStackItem(item); return; }
                targets.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "可选择对方1张休整军团，使其在下个对方重置阶段无法转为活跃",
                    targets, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-perseus-promotion-lock" });
                return;
            }
            default:
                FinishStackItem(item);
                return;
        }
    }

    private bool TryResolveS2FactionTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        if (AtomicFlowKey(item, card) == "mimir-recover-draw")
        {
            HealMaster(item.Controller, 1, "〈密米尔之泉〉");
            if (!Draw(player, 1))
            {
                SetWinner(1 - item.Controller, "〈密米尔之泉〉抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            }
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "mimir-mill")
        {
            Mill(player, 2, "〈密米尔之泉〉");
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "fortune-search")
        {
            BeginS2FortuneSearch(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "fortune-next-uesugi")
        {
            player.UsedAbilities.Add("s2-fortune-next-uesugi");
            AddEvent("effect", item.Controller,
                "〈武运在天 铠甲在前〉使本回合打出的下一张〈上杉谦信〉费用-2且获得冲锋", card);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "tenka-effect")
        {
            var mode = CompositeDeclared(item, "mode").SingleOrDefault();
            if (mode == "mode:row-cost")
            {
                var tenkaRow = CompositeDeclared(item, "row").SingleOrDefault() == "row:1" ? 1 : 0;
                foreach (var target in State.Players[1 - item.Controller].Field[tenkaRow]
                             .Where(target => target is not null && IsFieldLegion(target)).Cast<L12CardInstance>())
                    AddTimedModifier(target, 0, -2, ExpiryAtNextOwnEnd(item.Controller), "天下布武");
                AddEvent("effect", item.Controller, $"天下布武：对方{(tenkaRow == 0 ? "前排" : "后排")}所有军团本回合费用-2", card);
            }
            else if (mode == "mode:front-attack")
            {
                player.UsedAbilities.Add($"s2-tenka-front-attack:{State.TurnSerial}");
                AddEvent("effect", item.Controller, "天下布武：本回合我方前排【高天原】军团进攻时兵力+1000", card);
            }
            else
            {
                foreach (var legion in PublicFactionLegions(player, "gaotianyuan").Where(target => !target.Tapped))
                    player.UsedAbilities.Add($"s2-tenka-free-move:{legion.InstanceId}:{State.TurnSerial}");
                AddEvent("effect", item.Controller, "天下布武：本回合我方当前所有活跃【高天原】军团各可免费进行1格位移", card);
            }
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "hela-curse")
        {
            var target = DeclaredEnemyTarget(item.Controller, CompositeDeclared(item, "curseTarget").SingleOrDefault());
            if (target is null)
                AddEvent("effect-cancelled", item.Controller, "海拉声明的军团目标已失效", card);
            else
                AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "fearless-assassination")
        {
            var target = FindOnField(player, CompositeDeclared(item, "buffTarget").SingleOrDefault(), out var fearlessRow, out _);
            if (target is null || fearlessRow != 0 || target.Faction != "taiyangcheng")
                AddEvent("effect-cancelled", item.Controller, "无畏的刺杀声明的前排【太阳城】军团已失效", card);
            else
            {
                var expiry = ExpiryAtNextOwnEnd(item.Controller);
                AddTimedModifier(target, 3000, 0, expiry, "无畏的刺杀");
                target.SureHitAgainstLegionsUntilTurn = Math.Max(target.SureHitAgainstLegionsUntilTurn, expiry);
                target.CannotReadyByEffectUntilTurn = Math.Max(target.CannotReadyByEffectUntilTurn, expiry);
                target.DiscardAtEndOfTurnUntilTurn = Math.Max(target.DiscardAtEndOfTurnUntilTurn, expiry);
                AddEvent("effect", item.Controller, $"〈无畏的刺杀〉使{target.Name}本回合兵力+3000、获得必中", target);
            }
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "nyx-primary")
        {
            var target = DeclaredEnemyTarget(item.Controller, CompositeDeclared(item, "primaryTarget").SingleOrDefault());
            if (target is not null) AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "nyx-secondary")
        {
            var target = DeclaredEnemyTarget(item.Controller, CompositeDeclared(item, "secondaryTarget").SingleOrDefault());
            if (target is not null) AddTimedModifier(target, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "glory-flip")
        {
            var flipped = 0;
            foreach (var id in CompositeDeclared(item, "flipTargets").Take(3))
            {
                var morale = player.Morale.FirstOrDefault(resource => resource.InstanceId == id && !resource.IsGodPower);
                if (morale is null) continue;
                L12S2ZoneOps.FlipMoraleFace(player, morale.InstanceId, toGodPower: true);
                flipped++;
            }
            if (flipped > 0) AddEvent("morale", item.Controller, $"〈荣耀之路〉翻转{flipped}张士气", card);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "glory-search")
        {
            PromptS2GlorySearch(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "rune-gain")
        {
            L12S2ZoneOps.GainRunes(player, 1);
            AddEvent("runes", item.Controller, $"{card.Name}使我方获得1符文", card);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "rune-search")
        {
            BeginRunePowerSearch(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "round-table-search")
        {
            var candidates = player.Library
                .Where(candidate => candidate.CardType == "legion" && candidate.HasTrait("圆桌骑士"))
                .Select(candidate => candidate.InstanceId)
                .ToArray();
            if (candidates.Length == 0)
            {
                ShuffleLibrary(player, "圆桌领域检索未命中");
                FinishStackItem(item);
                return true;
            }
            CreatePrompt(item.Controller, "card", "圆桌领域：选择牌库中1张【圆桌骑士】军团展示并加入手牌",
                candidates, 1, 1, "card-effect", item.StackItemId,
                data: new Dictionary<string, string> { ["action"] = "s2-round-table-search" });
            return true;
        }
        if (card.CardId == "S02-0622")
        {
            var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
            if (target is not null) AddTimedModifier(target, -6000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) == "round-table-buff")
        {
            var target = FindOnField(player, CompositeDeclared(item, "buffTarget").SingleOrDefault(), out _, out _);
            if (target is not null && target.HasTrait("圆桌骑士"))
            {
                AddTimedModifier(target, 2000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
                AddEvent("effect", item.Controller, $"〈圆桌领域〉使{target.Name}本回合兵力+2000", card, target);
            }
            FinishStackItem(item);
            return true;
        }
        if (AtomicFlowKey(item, card) != "desert-transaction") return false;
        var discardIds = CompositeDeclared(item, "discardTargets");
        var repeatCountText = CompositeDeclared(item, "desertRepeatCount").SingleOrDefault();
        var discardCount = item.Data.GetValueOrDefault("repeatedEffectOnly") == "true"
            && repeatCountText?.Split(':') is ["count", var countValue]
            && int.TryParse(countValue, out var parsedCount)
                ? parsedCount
                : discardIds.Length;
        var summonId = CompositeDeclared(item, "summonTarget").SingleOrDefault();
        var slotChoice = CompositeDeclared(item, "summonSlot").SingleOrDefault();
        var summon = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == summonId
            && candidate.CardType == "legion" && candidate.Faction == "taiyangcheng"
            && candidate.DisasterLevel == discardCount);
        if (discardIds.Length > 3 || summon is null || slotChoice is null
            || !TrySummonFromAnyPrivateZone(player, item.Controller, summon.InstanceId, slotChoice, tapped: false))
        {
            AddEvent("effect-cancelled", item.Controller,
                "〈沙漠君临〉声明的手牌军团或登场位置已失效；登场取消，已弃置费用不恢复", card);
            FinishStackItem(item);
            return true;
        }
        FinishStackItem(item);
        return true;
    }

    private bool TryResolveS2FactionAttack(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        if (card.HasShock)
        {
            ApplyS2Shock(item, card);
            item.Data["shockApplied"] = "true";
        }
        if (AtomicFlowKey(item, card) == "冲田总司")
        {
            BeginS2OkitaAttack(item);
            return true;
        }
        if (card.CardId == "S02-0516")
        {
            if (item.Data.GetValueOrDefault("declaredMode") == "mode:use")
            {
                var own = FindOnField(player, item.Data.GetValueOrDefault("hannibalOwn"), out _, out _);
                var enemy = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("hannibalEnemy"));
                if (own is not null) AddTimedModifier(own, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
                if (enemy is not null) AddTimedModifier(enemy, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), card.Name);
            }
            FinishStackItem(item);
            return true;
        }
        return false;
    }

    private bool TryResolveS2FactionAfterAttack(L12StackItem item, L12CardInstance card)
    {
        if (card.CardId == "S02-0503")
        {
            if (item.Data.GetValueOrDefault("killed") == "true"
                && item.Data.GetValueOrDefault("combatKillConfirmed") == "true"
                && !string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("defeatedInstanceId"))
                && FindOnField(State.Players[item.Controller], card.InstanceId, out _, out _) is not null)
            {
                GrantTauntUntilNextOwnTurnEnd(card, item.Controller, requiresFrontRow: true);
                AddEvent("effect", item.Controller, $"{card.Name}因击杀军团，在我方下个回合结束前于前排获得挑衅", card);
            }
            FinishStackItem(item);
            return true;
        }
        if (card.CardId is "S02-0606" or "S02-0611" or "S02-0608")
        {
            var killed = item.Data.GetValueOrDefault("killed") == "true";
            var granted = card.CardId != "S02-0608"
                || State.Players[item.Controller].UsedAbilities.Remove($"crusade-piercing:{card.InstanceId}:{State.TurnSerial}");
            if (killed && granted) BeginPiercingAttack(item.Controller, card);
            FinishStackItem(item);
            return true;
        }
        if (card.CardId != "S02-0602" || item.Data.GetValueOrDefault("killed") != "true") return false;
        // Batch 6F：合法击杀候选已携带不可变公开模式并由统一推进事件结算。
        FinishStackItem(item);
        return true;
    }

    private bool TryResolveS2FactionDeath(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "亚瑟王":
            {
                _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex,
                    PublicTriggerDeclared(item, "entryCard"), PublicTriggerDeclared(item, "entrySlot"), tapped: false);
                FinishStackItem(item); return true;
            }
            case "忒修斯":
            {
                var targetId = PublicTriggerDeclared(item, "recoverTarget");
                var target = player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == targetId
                    && candidate.CardType == "legion" && candidate.HasTrait("晋升者"));
                if (target is not null)
                {
                    AddEvent("reveal", item.Controller, $"忒修斯展示墓地的〈{target.Name}〉并加入手牌", target);
                    MoveGraveToHand(player, target.InstanceId);
                }
                else AddEvent("effect-cancelled", item.Controller,
                    "忒修斯已声明的【晋升者】目标失效；效果取消", card);
                FinishStackItem(item); return true;
            }
            case "哮天犬·稚":
                if (PublicTriggerDeclared(item, "mode") == "mode:use" && player.MoraleDeck.Count > 0)
                {
                    AddMorale(player, 1, tapped: true);
                    AddEvent("morale", item.Controller, "哮天犬·稚从士气牌库追加1张休整士气", card);
                }
                FinishStackItem(item); return true;
            case "阿塔兰忒":
            {
                var moraleId = PublicTriggerDeclared(item, "moraleTarget");
                if (player.Morale.Any(candidate => candidate.InstanceId == moraleId && !candidate.IsGodPower))
                {
                    L12S2ZoneOps.FlipMoraleFace(player, moraleId, toGodPower: true);
                    AddEvent("morale", item.Controller, "阿塔兰忒阵亡时翻转1张士气", card);
                }
                else AddEvent("effect-cancelled", item.Controller, "阿塔兰忒已声明的士气目标失效；效果取消", card);
                FinishStackItem(item); return true;
            }
            case "格温莉安":
                if (item.Data.GetValueOrDefault("cause") != "effect") { FinishStackItem(item); return true; }
                if (PublicTriggerDeclared(item, "mode") == "mode:heal")
                    HealMaster(item.Controller, 1, "格温莉安阵亡时效果", legionEffect: true);
                else if (!Draw(player, 1)) SetWinner(1 - item.Controller, "格温莉安效果抽牌时牌库为空");
                AddEvent("effect", item.Controller, $"格温莉安因效果阵亡，选择{(PublicTriggerDeclared(item, "mode") == "mode:heal" ? "主宰恢复1点血量" : "抽取1张牌")}", card);
                FinishStackItem(item); return true;
            case "雷神之锤":
                if (PublicTriggerDeclared(item, "mode") != "mode:use") { FinishStackItem(item); return true; }
                if (!Draw(player, 1))
                {
                    SetWinner(1 - item.Controller, "雷神之锤阵亡效果抽牌时牌库为空");
                    FinishStackItem(item); return true;
                }
                AddEvent("draw", item.Controller, "雷神之锤阵亡时抽取1张牌", card);
                CreateDelayedPublicResolutionPrompt(item, "hand-card", "雷神之锤：抽牌后弃置1张手牌",
                    player.Hand.Select(candidate => candidate.InstanceId), "s2-asgard-death-discard", []);
                return true;
            case "陵墓圣武士":
            {
                var declaredGuard = PublicTriggerDeclared(item, "entryCard");
                if (!string.IsNullOrWhiteSpace(declaredGuard))
                {
                    _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, declaredGuard,
                        PublicTriggerDeclared(item, "entrySlot"), tapped: false);
                    FinishStackItem(item); return true;
                }
                FinishStackItem(item); return true;
            }
            default:
                return false;
        }
    }

    private CommandResult? TryBeginS2FactionActiveAbility(int playerIndex, L12CardInstance source, string ability)
    {
        var player = State.Players[playerIndex];
        if (ability == "nephthysSacrifice" && source.CardId == "S02-02M1")
        {
            var onceKey = $"active:{source.InstanceId}:{ability}";
            if (player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
            var choices = PublicLegions(player).Select(card => card.InstanceId).ToArray();
            if (choices.Length == 0) return CommandResult.Reject("我方战场没有可弃置的军团");
            return BeginPendingActivation(playerIndex, source, ability, choices,
                "奈芙蒂斯：选择我方战场任意数量的军团弃置", min: 1, max: choices.Length);
        }
        if (ability == "avalonRecover" && source.CardId == "S02-06D1")
        {
            if (player.SpecialZones.Runes < 2) return CommandResult.Reject("需要消耗2符文");
            var legions = player.Graveyard.Where(card => card.CardType == "legion").Select(card => card.InstanceId).ToList();
            var tactics = player.Graveyard.Where(card => card.CardType is "tactic" or "counter-tactic").Select(card => card.InstanceId).ToList();
            if (legions.Count == 0 || tactics.Count == 0) return CommandResult.Reject("墓地中需要同时存在军团和战术");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep { Kind = "grave-card", Text = "彼界 阿瓦隆：选择墓地1张军团加入手牌", ValidChoices = legions },
                new L12ActivationSelectionStep { Kind = "grave-card", Text = "彼界 阿瓦隆：选择墓地1张战术加入手牌", ValidChoices = tactics },
            ]);
        }
        if (ability == "avalonDebuff" && source.CardId == "S02-06D1")
        {
            if (player.MasterTapped) return CommandResult.Reject("彼界 阿瓦隆必须为活跃状态");
            var choices = PublicLegions(State.Players[1 - playerIndex]).Select(card => card.InstanceId).ToArray();
            if (choices.Length == 0) return CommandResult.Reject("对方战场没有可选择的军团");
            return BeginPendingActivation(playerIndex, source, ability, choices,
                "彼界 阿瓦隆：选择对方1张军团，本回合兵力-4000");
        }
        if (ability == "forgeReadyOnKill" && source.CardId == "S02-0520")
        {
            if (source.Tapped) return CommandResult.Reject("匠神锻造炉必须为活跃状态");
            var choices = PublicLegions(player)
                .Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus")
                    && !card.HasTrait("晋升者"))
                .Select(card => card.InstanceId).ToArray();
            if (choices.Length == 0) return CommandResult.Reject("我方战场没有【晋升者】以外的【奥林匹斯】军团");
            return BeginPendingActivation(playerIndex, source, ability, choices,
                "选择我方1张【晋升者】以外的【奥林匹斯】军团");
        }
        if (ability == "trialAdvance" && source.TrialValue > 0)
            return BeginTrialAdvanceActivation(playerIndex, source);
        if (ability == "godPowerDraw" && source.CardId == "S02-05C1")
        {
            if (player.UsedAbilities.Contains($"active:{source.InstanceId}:{ability}")) return CommandResult.Reject("该效果本回合已经发动");
            if (!L12S2ZoneOps.ConsumeAndFlipGodPower(player, 1)) return CommandResult.Reject("需要1张活跃的神力");
            player.UsedAbilities.Add($"active:{source.InstanceId}:{ability}");
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "factionGainRune" && source.CardId == "S02-06C1")
        {
            if (player.UsedAbilities.Contains($"active:{source.InstanceId}:{ability}")) return CommandResult.Reject("该效果本回合已经发动");
            if (!TryConsumeMorale(player, 2)) return CommandResult.Reject("需要2张活跃的士气");
            player.UsedAbilities.Add($"active:{source.InstanceId}:{ability}");
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "olympusMoraleFlip" && source.CardId == "S02-05C1A")
        {
            var onceKey = $"active:{source.InstanceId}:{ability}";
            if (player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
            if (!player.Morale.Any(card => !card.IsGodPower)) return CommandResult.Reject("没有可翻转的士气");
            return CommitActiveAbility(playerIndex, source, ability, null);
        }
        if (ability == "prometheusTopThree" && source.CardId == "S02-05M2")
        {
            var onceKey = $"active:{source.InstanceId}:{ability}";
            if (player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
            if (!L12S2ZoneOps.ConsumeGodPower(player, 1)) return CommandResult.Reject("需要1张活跃的神力");
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主宰效果",
                data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "morriganReadyOnKill" && source.CardId == "S02-06M1")
        {
            var onceKey = $"active:{source.InstanceId}:{ability}";
            if (player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
            if (player.SpecialZones.Runes < 2) return CommandResult.Reject("需要消耗2符文");
            var choices = PublicLegions(player)
                .Where(card => L12StructuredCardRules.HasFaction(player, card, "otherworld"))
                .Select(card => card.InstanceId).ToArray();
            if (choices.Length == 0) return CommandResult.Reject("我方战场没有可选择的【彼界】军团");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep { Kind = "active-target", Text = "莫瑞甘：选择我方1张【彼界】军团，本回合其下一次击杀对方军团后转为活跃", ValidChoices = choices.ToList() },
            ]);
        }
        if (ability == "runeUse" && source.CardId == "S02-06C1")
        {
            if (player.UsedAbilities.Contains($"active:{source.InstanceId}:{ability}")) return CommandResult.Reject("符文效果本回合已经发动");
            if (player.SpecialZones.Runes < 1) return CommandResult.Reject("需要消耗1符文");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "option", Text = "彼界阵营效果：选择效果", ValidChoices = ["mode:trial", "mode:draw"],
                    ChoiceLabels = new Dictionary<string, string>
                    {
                        ["mode:trial"] = "消耗1符文：当前试炼进度+1",
                        ["mode:draw"] = "消耗1符文：抽取1张牌",
                    },
                },
            ]);
        }
        if (ability == "merlinRune" && source.CardId == "S02-0603")
        {
            var enemy = PublicLegions(State.Players[1 - playerIndex]).Select(card => card.InstanceId).ToList();
            var modes = new List<string>();
            if (enemy.Count > 0) modes.Add("mode:debuff");
            // 牌库命中身份与是否命中都在效果合法开始后才可知；声明期只读取公开牌库数量。
            if (player.Library.Count > 0) modes.Add("mode:search");
            if (modes.Count == 0) return CommandResult.Reject("没有可选择的公开目标且牌库为空");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "option", DeclarationKey = "mode", Text = "梅林：选择效果", ValidChoices = modes, MinChoose = 1, MaxChoose = 1,
                    ChoiceLabels = new Dictionary<string, string>
                    {
                        ["mode:debuff"] = "消耗1符文：选择对方1张军团，本回合兵力-3000",
                        ["mode:search"] = "消耗1符文：检索1张费用不高于4的主动战术",
                    },
                },
                new L12ActivationSelectionStep
                {
                    Kind = "active-target", DeclarationKey = "target", Text = "梅林：预先选择对方1张军团",
                    ValidChoices = enemy, MinChoose = 1, MaxChoose = 1, RequiredDeclaredChoice = "mode:debuff",
                },
            ]);
        }
        if (ability == "amakineTop" && source.CardId == "S02-0616")
        {
            if (source.Tapped) return CommandResult.Reject("阿麦金必须为活跃状态");
            if (player.Library.Count == 0) return CommandResult.Reject("牌库为空，无法展示牌库顶部的牌");
            return CommitActiveAbility(playerIndex, source, ability, target: null);
        }
        if (ability == "galahadGrailReward" && source.CardId == "S02-0604")
        {
            if (!player.SpecialZones.Trials.Any(card => card.CardId == "S02-06S4" && card.TrialCompleted))
                return CommandResult.Reject("试炼《寻找圣杯之旅》尚未完成");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "option", DeclarationKey = "healMode",
                    Text = "加拉哈德：预先声明抽牌后是否使我方主宰增加1点血量",
                    ValidChoices = ["mode:none", "mode:heal"],
                    ChoiceLabels = new Dictionary<string, string>
                    {
                        ["mode:none"] = "抽取1张牌",
                        ["mode:heal"] = "抽取1张牌；我方主宰增加1点血量",
                    },
                },
            ]);
        }
        if (ability == "aristotleDiscount" && source.CardId == "S02-0513")
            return CommitActiveAbility(playerIndex, source, ability, target: null);
        if (ability == "scarabSummon" && source.CardId == "S02-0205")
        {
            var scarab = player.Graveyard.FirstOrDefault(card => card.CardId == "S02-0201");
            if (scarab is null || !EmptySlots(player).Any()) return CommandResult.Reject("墓地没有可登场的〈增殖的甲虫〉或没有空位");
            return BeginPendingActivation(playerIndex, source, ability, EmptySlots(player).ToArray(), "选择〈增殖的甲虫〉活跃登场的位置");
        }
        if (ability == "scarabDebuff" && source.CardId == "S02-0205")
        {
            if (player.Hand.Count == 0) return CommandResult.Reject("需要弃置1张手牌");
            var choices = State.Players[1 - playerIndex].Field.SelectMany(row => row)
                .Where(card => card is not null && IsFieldLegion(card) && !card.Hidden).Select(card => card!.InstanceId).ToArray();
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "hand-card", Text = "黄金圣甲虫：选择弃置的1张手牌", ValidChoices = player.Hand.Select(card => card.InstanceId).ToList(), MinChoose = 1, MaxChoose = 1,
                },
                new L12ActivationSelectionStep
                {
                    Kind = "active-target", Text = "黄金圣甲虫：选择对方最多2张军团，本回合兵力-1000", ValidChoices = choices.ToList(), MinChoose = 0, MaxChoose = Math.Min(2, choices.Length),
                },
            ]);
        }
        if (ability == "magatamaMove" && source.CardId == "S02-0404")
        {
            if (source.Tapped) return CommandResult.Reject("八尺琼勾玉必须为活跃状态");
            var candidates = EffectCavalryDestinations(player).Any()
                ? PublicLegions(player)
                    .Where(card => !card.Tapped)
                    .Select(card => card.InstanceId).ToList()
                : [];
            if (candidates.Count == 0) return CommandResult.Reject("战场上没有可进行骑兵位移的军团");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "active-target", Text = "八尺琼勾玉：选择我方1张活跃军团",
                    ValidChoices = candidates,
                },
                new L12ActivationSelectionStep
                {
                    Kind = "cavalry-slot", Text = "八尺琼勾玉：选择该军团进行骑兵位移后的空位",
                    ValidChoices = EffectCavalryDestinations(player).ToList(),
                },
            ]);
        }
        if (ability == "magatamaImmortal" && source.CardId == "S02-0404")
        {
            if (source.Tapped) return CommandResult.Reject("八尺琼勾玉必须为活跃状态");
            var choices = PublicLegions(player).Where(card => card.LastMovedTurn == State.TurnSerial)
                .Select(card => card.InstanceId).ToArray();
            if (choices.Length == 0) return CommandResult.Reject("我方没有本回合位移过的军团");
            return BeginPendingActivation(playerIndex, source, ability, choices,
                "八尺琼勾玉：选择我方1张本回合位移过的军团，本回合获得免死");
        }
        if (ability == "completeTrial" && source.CardType == "trial")
        {
            if (source.TrialCompleted || source.TrialProgress < 8)
                return CommandResult.Reject("试炼进度达到8后才可完成试炼");
            PushEffect(playerIndex, source, "active", "完成试炼", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (source.CardType == "trial" && ability is "fenianReady" or "crusadeTrialNoLoss" or "crusadeRichardPiercing" or "crusadeRecover")
        {
            if (!source.TrialCompleted) return CommandResult.Reject("该试炼尚未完成");
            if (player.UsedAbilities.Contains(ActiveAbilityUsageKey(source.InstanceId, source.CardId, ability)))
                return CommandResult.Reject("该效果本回合已经发动");
            if (ability == "fenianReady")
            {
                if (player.SpecialZones.Runes < 1) return CommandResult.Reject("需要消耗1符文");
                var choices = PublicLegions(player).Where(card => card.Tapped
                        && L12StructuredCardRules.HasFaction(player, card, "otherworld")
                        && (card.CardId == "S02-0610" || card.BaseTroops <= 4000))
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) return CommandResult.Reject("没有符合条件的休整军团");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "active-target", Text = "选择我方1张〈芬恩〉或原本兵力不高于4000的【彼界】军团转为活跃", ValidChoices = choices.ToList() },
                ]);
            }
            if (ability == "crusadeTrialNoLoss")
            {
                if (player.SpecialZones.Runes < 1) return CommandResult.Reject("需要消耗1符文");
                var choices = PublicLegions(player).Where(card => card.CardId is "S02-0604" or "S02-0610" or "S02-0614")
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) return CommandResult.Reject("战场上没有【试炼军团】");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "active-target", Text = "选择我方1张【试炼军团】，本回合下一次进攻无损", ValidChoices = choices.ToList() },
                ]);
            }
            if (ability == "crusadeRichardPiercing")
            {
                if (player.SpecialZones.Runes < 2) return CommandResult.Reject("需要消耗2符文");
                var choices = PublicLegions(player).Where(card => card.CardId == "S02-0608").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) return CommandResult.Reject("战场上没有〈狮心王理查一世〉");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "active-target", Text = "选择我方1张〈狮心王理查一世〉", ValidChoices = choices.ToList() },
                ]);
            }
            if (player.SpecialZones.Runes < 2 || player.Hand.Count == 0) return CommandResult.Reject("需要消耗2符文并弃置1张手牌");
            var grave = player.Graveyard.Where(card => card.Faction == "otherworld").Select(card => card.InstanceId).ToArray();
            if (grave.Length == 0) return CommandResult.Reject("墓地没有只有【彼界】特征的卡牌");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep { Kind = "hand-card", Text = "选择弃置的1张手牌", ValidChoices = player.Hand.Select(card => card.InstanceId).ToList(), MinChoose = 1, MaxChoose = 1 },
                new L12ActivationSelectionStep { Kind = "grave-card", Text = "选择墓地1张只有【彼界】特征的卡牌加入手牌", ValidChoices = grave.ToList(), MinChoose = 1, MaxChoose = 1 },
            ]);
        }
        return TryBeginS2RemainingAbility(playerIndex, source, ability);
    }

    private L12TriggerCandidate? BuildMorriganEnemyDeathCandidate(int defeatedController)
    {
        var controller = 1 - defeatedController;
        var player = State.Players[controller];
        var onceKey = $"s2-morrigan-rune:{State.TurnSerial}";
        var pendingKey = $"{onceKey}:pending";
        if (State.ActivePlayer != controller || player.MasterId != "S02-06M1" || player.UsedAbilities.Contains(onceKey))
            return null;
        if (!player.UsedAbilities.Add(pendingKey)) return null;
        var master = CreateCard(player.MasterId, $"master-{controller}");
        return CreateTriggerCandidate(controller, master, "morrigan-enemy-death", "【对方军团阵亡时】效果",
            new Dictionary<string, string>
            {
                ["onceKey"] = onceKey, ["cleanupReservation"] = pendingKey,
            });
    }

    private L12TriggerCandidate? BuildNephthysOwnDeathCandidate(int defeatedController, L12CardInstance defeated)
    {
        var player = State.Players[defeatedController];
        var onceKey = $"s2-nephthys-scarab:{State.TurnSerial}";
        if (State.ActivePlayer == defeatedController || player.MasterId != "S02-02M1"
            || player.UsedAbilities.Contains(onceKey) || defeated.Faction != "taiyangcheng"
            || defeated.CurrentCost < 2 || !player.Graveyard.Any(card => card.CardId == "S02-0201")
            || !EmptySlots(player).Any())
            return null;
        var master = CreateCard(player.MasterId, $"master-{defeatedController}");
        return CreateTriggerCandidate(defeatedController, master, "nephthys-own-death", "【我方军团阵亡时】效果",
            new Dictionary<string, string>
            {
                ["ability"] = "nephthysScarabEntry", ["defeated"] = defeated.InstanceId,
                ["onceKey"] = onceKey,
            });
    }

    private void ResolveS2MorriganEnemyDeath(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        L12S2ZoneOps.GainRunes(player, 1);
        AddEvent("runes", item.Controller, "莫瑞甘因对方军团阵亡使我方获得1符文",
            FindSource(item) is { } source ? [source] : []);
        FinishStackItem(item);
    }

    private void ResolveS2NephthysOwnDeath(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var onceKey = item.Data.GetValueOrDefault("onceKey") ?? $"s2-nephthys-scarab:{State.TurnSerial}";
        if (State.ActivePlayer == item.Controller || player.MasterId != "S02-02M1"
            || !player.UsedAbilities.Contains(onceKey) || !player.Graveyard.Any(card => card.CardId == "S02-0201")
            || !EmptySlots(player).Any())
        {
            FinishStackItem(item);
            return;
        }
        var scarabId = PublicTriggerDeclared(item, "entryCard");
        var destination = PublicTriggerDeclared(item, "entrySlot");
        if (!player.UsedAbilities.Contains(onceKey)
            || !player.Graveyard.Any(card => card.InstanceId == scarabId && card.CardId == "S02-0201")
            || !EmptySlots(player).Contains(destination, StringComparer.OrdinalIgnoreCase))
        {
            AddEvent("effect-cancelled", item.Controller, "奈芙蒂斯选择的增殖甲虫或登场位置已失效；该军团不登场");
            FinishStackItem(item);
            return;
        }
        SummonFromAnyPrivateZone(player, scarabId, destination, tapped: false);
        FinishStackItem(item);
    }

    private CommandResult? TryCommitS2FactionActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey, bool? useTombGuards,
        IEnumerable<string>? returnedMoraleIds = null)
    {
        var player = State.Players[playerIndex];
        if (ability == "nephthysSacrifice" && source.CardId == "S02-02M1")
        {
            if (player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
            var declaredIds = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (declaredIds.Length == 0) return CommandResult.Reject("至少需要选择1张我方军团");
            var declared = declaredIds.Select(id => FindOnField(player, id, out _, out _)).ToArray();
            if (declared.Any(card => card is null || !IsFieldLegion(card))) return CommandResult.Reject("选择的军团已不在我方战场");
            foreach (var card in declared.Cast<L12CardInstance>())
                MoveFieldCardToZone(player, card, "graveyard", "被奈芙蒂斯效果弃置");
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主宰效果", data: new Dictionary<string, string>
            {
                ["ability"] = ability,
                ["count"] = declaredIds.Length.ToString(),
            });
            return CommandResult.Ok();
        }
        if (ability == "avalonRecover" && source.CardId == "S02-06D1")
        {
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (declared.Length != 2 || declared[0] == declared[1]) return CommandResult.Reject("需要分别选择1张军团和1张战术");
            var legion = player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[0] && card.CardType == "legion");
            var tactic = player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[1] && card.CardType is "tactic" or "counter-tactic");
            if (legion is null || tactic is null) return CommandResult.Reject("选择的墓地卡牌已不合法");
            if (!L12S2ZoneOps.SpendRunes(player, 2)) return CommandResult.Reject("需要消耗2符文");
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主神效果", data: new Dictionary<string, string>
            {
                ["ability"] = ability,
                ["legion"] = legion.InstanceId,
                ["tactic"] = tactic.InstanceId,
            });
            return CommandResult.Ok();
        }
        if (ability == "avalonDebuff" && source.CardId == "S02-06D1")
        {
            var declared = DeclaredEnemyTarget(playerIndex, target);
            if (player.MasterTapped || declared is null) return CommandResult.Reject("彼界 阿瓦隆必须为活跃状态且目标合法");
            player.MasterTapped = true;
            PushEffect(playerIndex, source, "active", "主神主动休整效果", data: new Dictionary<string, string>
            {
                ["ability"] = ability,
                ["target"] = declared.InstanceId,
            });
            return CommandResult.Ok();
        }
        if (ability is "forgePromotionDiscount" or "forgeReadyOnKill" && source.CardId == "S02-0520")
        {
            if (source.Tapped) return CommandResult.Reject("匠神锻造炉必须为活跃状态");
            L12CardInstance? declaredTarget = null;
            if (ability == "forgeReadyOnKill")
            {
                declaredTarget = FindOnField(player, target, out _, out _);
                if (declaredTarget is null || !L12StructuredCardRules.HasFaction(player, declaredTarget, "olympus")
                    || declaredTarget.HasTrait("晋升者"))
                    return CommandResult.Reject("选择的军团不符合匠神锻造炉条件");
            }
            var paid = useTombGuards switch
            {
                true => TryConsumeMorale(player, 1, preferTombGuards: true, allowTombGuards: true),
                false => TryConsumeMorale(player, 1, preferTombGuards: false, allowTombGuards: false),
                _ => TryConsumeMorale(player, 1),
            };
            if (!paid) return CommandResult.Reject("需要消耗1张活跃的士气");
            source.Tapped = true;
            var data = new Dictionary<string, string> { ["ability"] = ability };
            if (declaredTarget is not null) data["target"] = declaredTarget.InstanceId;
            PushEffect(playerIndex, source, "active", "主动休整效果", data: data);
            return CommandResult.Ok();
        }
        if (ability == "morriganReadyOnKill" && source.CardId == "S02-06M1")
        {
            var declaredTarget = FindOnField(player, target, out _, out _);
            if (declaredTarget is null || !L12StructuredCardRules.HasFaction(player, declaredTarget, "otherworld"))
                return CommandResult.Reject("选择的军团不符合莫瑞甘效果条件");
            if (!L12S2ZoneOps.SpendRunes(player, 2)) return CommandResult.Reject("需要消耗2符文");
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主宰效果", data: new Dictionary<string, string>
            {
                ["ability"] = ability,
                ["target"] = declaredTarget.InstanceId,
            });
            return CommandResult.Ok();
        }
        if (ability == "merlinRune" && source.CardId == "S02-0603")
        {
            if (source.Tapped || player.SpecialZones.Runes < 1) return CommandResult.Reject("梅林需要为活跃状态且消耗1符文");
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (declared.Length == 0) return CommandResult.Reject("需要选择效果");
            var valid = declared[0] switch
            {
                "mode:debuff" => declared.Length == 2 && DeclaredEnemyTarget(playerIndex, declared[1]) is not null,
                "mode:search" => declared.Length == 1 && player.Library.Count > 0,
                _ => false,
            };
            if (!valid) return CommandResult.Reject("梅林选择的效果或目标已不合法");
            source.Tapped = true;
            if (!L12S2ZoneOps.SpendRunes(player, 1)) return CommandResult.Reject("需要消耗1符文");
            var data = new Dictionary<string, string> { ["ability"] = ability, ["mode"] = declared[0] };
            if (declared.Length == 2) data["target"] = declared[1];
            PushEffect(playerIndex, source, "active", "主动效果", data: data);
            return CommandResult.Ok();
        }
        if (ability == "aristotleDiscount" && source.CardId == "S02-0513")
        {
            if (source.Tapped) return CommandResult.Reject("亚里士多德必须为活跃状态");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "imhotepDiscount" && source.CardId == "S02-0204")
        {
            if (source.Tapped) return CommandResult.Reject("伊姆何泰普必须为活跃状态");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "scarabSummon" && source.CardId == "S02-0205")
        {
            if (source.Tapped) return CommandResult.Reject("黄金圣甲虫必须为活跃状态");
            if (!EmptySlots(player).Contains(target ?? string.Empty)) return CommandResult.Reject("登场位置不合法");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability, ["target"] = target! });
            return CommandResult.Ok();
        }
        if (ability == "scarabDebuff" && source.CardId == "S02-0205")
        {
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var discard = declared.Length == 0 ? null : player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]);
            if (discard is null) return CommandResult.Reject("需要弃置1张手牌");
            if (declared.Skip(1).Distinct().Count() > 2 || declared.Skip(1).Any(id => DeclaredEnemyTarget(playerIndex, id) is null))
                return CommandResult.Reject("减兵目标不合法");
            player.Hand.Remove(discard);
            player.Graveyard.Add(discard);
            player.UsedAbilities.Add(onceKey);
            var data = new Dictionary<string, string> { ["ability"] = ability, ["targets"] = string.Join('|', declared.Skip(1)) };
            PushEffect(playerIndex, source, "active", "主动效果", data: data);
            AddEvent("cost", playerIndex, $"弃置〈{discard.Name}〉支付黄金圣甲虫费用", discard);
            return CommandResult.Ok();
        }
        if (ability == "magatamaMove" && source.CardId == "S02-0404")
        {
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (source.Tapped || declared.Length != 2) return CommandResult.Reject("八尺琼勾玉必须为活跃状态且位移声明完整");
            var legion = FindOnField(player, declared[0], out _, out _);
            var destination = ParseSlot(declared[1]);
            if (legion is null || legion.Hidden || legion.Tapped || !IsFieldLegion(legion)
                || !EffectCavalryDestinations(player).Contains(declared[1]))
                return CommandResult.Reject("所选军团或位移位置已不合法");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动休整效果", data: new Dictionary<string, string>
            {
                ["ability"] = ability,
                ["target"] = legion.InstanceId,
                ["destination"] = $"{destination.Row}:{destination.Slot}",
                ["targetPlayerIndex"] = playerIndex.ToString(),
            });
            return CommandResult.Ok();
        }
        if (ability == "magatamaImmortal" && source.CardId == "S02-0404")
        {
            var legion = FindOnField(player, target, out _, out _);
            if (source.Tapped || legion is null || !IsFieldLegion(legion) || legion.LastMovedTurn != State.TurnSerial)
                return CommandResult.Reject("八尺琼勾玉必须为活跃状态，且目标必须在本回合位移过");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动休整效果", data: new Dictionary<string, string>
            {
                ["ability"] = ability,
                ["target"] = legion.InstanceId,
            });
            return CommandResult.Ok();
        }
        if (ability == "amakineTop" && source.CardId == "S02-0616")
        {
            if (source.Tapped) return CommandResult.Reject("阿麦金必须为活跃状态");
            if (player.Library.Count == 0) return CommandResult.Reject("牌库为空，无法展示牌库顶部的牌");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "galahadGrailReward" && source.CardId == "S02-0604")
        {
            if (!player.SpecialZones.Trials.Any(card => card.CardId == "S02-06S4" && card.TrialCompleted))
                return CommandResult.Reject("试炼《寻找圣杯之旅》尚未完成");
            if (target is not ("mode:none" or "mode:heal"))
                return CommandResult.Reject("需要预先声明是否使我方主宰增加血量");
            // 冒号前弃置是发动费用：先完成权威离场，再创建可响应的堆叠项。
            RemoveFromField(player, source, true, "作为加拉哈德主动效果的费用被弃置",
                leaveKind: L12FieldLeaveKind.Discard);
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "完成试炼后的主动效果",
                data: new Dictionary<string, string> { ["ability"] = ability, ["healMode"] = target });
            return CommandResult.Ok();
        }
        if (ability == "runeUse" && source.CardId == "S02-06C1")
        {
            if (player.SpecialZones.Runes < 1) return CommandResult.Reject("需要消耗1符文");
            var mode = target;
            if (mode is not ("mode:trial" or "mode:draw")) return CommandResult.Reject("符文效果选项不合法");
            L12S2ZoneOps.SpendRunes(player, 1);
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "符文效果", data: new Dictionary<string, string> { ["ability"] = ability, ["mode"] = mode });
            return CommandResult.Ok();
        }
        if (source.CardType == "trial" && ability is "fenianReady" or "crusadeTrialNoLoss" or "crusadeRichardPiercing" or "crusadeRecover")
        {
            if (!source.TrialCompleted) return CommandResult.Reject("该试炼尚未完成");
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var runeCost = ability == "fenianReady" || ability == "crusadeTrialNoLoss" ? 1 : 2;
            if (player.SpecialZones.Runes < runeCost) return CommandResult.Reject($"需要消耗{runeCost}符文");
            L12CardInstance? discardCost = null;
            if (ability == "fenianReady")
            {
                var chosen = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (chosen is null || !chosen.Tapped
                    || !L12StructuredCardRules.HasFaction(player, chosen, "otherworld")
                    || (chosen.CardId != "S02-0610" && chosen.BaseTroops > 4000))
                    return CommandResult.Reject("目标不符合转为活跃的条件");
            }
            else if (ability == "crusadeTrialNoLoss")
            {
                var chosen = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (chosen?.CardId is not ("S02-0604" or "S02-0610" or "S02-0614")) return CommandResult.Reject("目标不是【试炼军团】");
            }
            else if (ability == "crusadeRichardPiercing")
            {
                var chosen = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (chosen?.CardId != "S02-0608") return CommandResult.Reject("目标不是〈狮心王理查一世〉");
            }
            else
            {
                if (declared.Length != 2) return CommandResult.Reject("需要声明弃置手牌和回收墓地牌");
                var discard = player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]);
                var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[1] && card.Faction == "otherworld");
                if (discard is null || recover is null) return CommandResult.Reject("弃置或回收的卡牌已不合法");
                discardCost = discard;
            }
            if (!L12S2ZoneOps.SpendRunes(player, runeCost)) return CommandResult.Reject($"需要消耗{runeCost}符文");
            if (discardCost is not null)
            {
                player.Hand.Remove(discardCost);
                player.Graveyard.Add(discardCost);
                AddEvent("cost", playerIndex, $"弃置〈{discardCost.Name}〉支付十字军东征费用", discardCost);
            }
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "已完成试炼的主动效果",
                data: new Dictionary<string, string> { ["ability"] = ability, ["target"] = target ?? string.Empty });
            return CommandResult.Ok();
        }
        if (ability == "olympusMoraleFlip" && source.CardId == "S02-05C1A")
        {
            if (!TryConsumeMorale(player, 1)) return CommandResult.Reject("需要1张活跃的士气");
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "阵营效果",
                data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        return TryCommitS2RemainingAbility(playerIndex, source, ability, target, onceKey);
    }

    private bool TryResolveS2FactionActive(L12StackItem item, L12CardInstance? source, string ability)
    {
        var player = State.Players[item.Controller];
        if (ability == "margaretMasterDamage" && source?.CardId == "S02-0304")
        {
            switch (item.Data.GetValueOrDefault("atomicFlow"))
            {
                case "margaret-heal":
                    HealMaster(item.Controller, 1, "玛格丽特一世效果", legionEffect: true);
                    AddEvent("effect", item.Controller, "玛格丽特一世使我方主宰增加1点血量", source);
                    break;
                case "margaret-heal-lock":
                    player.LegionEffectHealForbiddenUntilTurn = State.TurnSerial;
                    AddEvent("effect", item.Controller,
                        "玛格丽特一世：本回合我方主宰无法再因军团效果增加血量", source);
                    break;
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "nephthysSacrifice" && source?.CardId == "S02-02M1")
        {
            var count = int.TryParse(item.Data.GetValueOrDefault("count"), out var parsed) ? parsed : 0;
            player.NextS2SunDisasterLegionDiscount += Math.Max(0, count);
            AddEvent("effect", item.Controller,
                $"奈芙蒂斯弃置{count}张军团；本回合下一张带有天灾等级的【太阳城】军团登场费用-{count}", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "avalonRecover" && source?.CardId == "S02-06D1")
        {
            var recovered = new List<L12CardInstance>();
            foreach (var id in new[] { item.Data.GetValueOrDefault("legion"), item.Data.GetValueOrDefault("tactic") })
            {
                var card = player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == id);
                if (card is null) continue;
                player.Graveyard.Remove(card);
                AddCardToHandByEffect(player, card, "graveyard", $"彼界 阿瓦隆将{card.Name}加入手牌");
                recovered.Add(card);
            }
            player.FreeTacticCount++;
            AddEvent("effect", item.Controller, "彼界 阿瓦隆回收1张军团和1张战术；本回合下一张从手牌打出的战术无需消耗费用", recovered.Prepend(source).ToArray());
            FinishStackItem(item);
            return true;
        }
        if (ability == "avalonDebuff" && source?.CardId == "S02-06D1")
        {
            var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
            if (target is not null)
            {
                AddTimedModifier(target, -4000, 0, ExpiryAtNextOwnEnd(item.Controller), "彼界 阿瓦隆");
                AddEvent("effect", item.Controller, $"彼界 阿瓦隆使〈{target.Name}〉本回合兵力-4000", source, target);
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "forgePromotionDiscount" && source?.CardId == "S02-0520")
        {
            player.NextS2PromotionGodPowerDiscount++;
            AddEvent("effect", item.Controller, "本回合下一次晋升登场消耗并翻转的神力-1", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "forgeReadyOnKill" && source?.CardId == "S02-0520")
        {
            var target = FindOnField(player, item.Data.GetValueOrDefault("target"), out _, out _);
            if (target is not null)
            {
                target.ReadyAfterNextKillUntilTurn = State.TurnSerial;
                target.ReadyAfterNextKillSourceName = "匠神锻造炉";
                AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合下一次击杀对方军团后转为活跃", source, target);
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "morriganReadyOnKill" && source?.CardId == "S02-06M1")
        {
            var target = FindOnField(player, item.Data.GetValueOrDefault("target"), out _, out _);
            if (target is not null && L12StructuredCardRules.HasFaction(player, target, "otherworld"))
            {
                target.ReadyAfterNextKillUntilTurn = State.TurnSerial;
                target.ReadyAfterNextKillSourceName = "莫瑞甘";
                AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合下一次击杀对方军团后转为活跃", source, target);
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "godPowerDraw" && source?.CardId == "S02-05C1")
        {
            Draw(player, 1);
            FinishStackItem(item);
            return true;
        }
        if (ability == "factionGainRune" && source?.CardId == "S02-06C1")
        {
            L12S2ZoneOps.GainRunes(player, 1);
            AddEvent("effect", item.Controller, "获得1枚符文", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "olympusMoraleFlip" && source?.CardId == "S02-05C1A")
            return PromptS2FlipMorale(item, source);
        if (ability == "prometheusTopThree" && source?.CardId == "S02-05M2")
        {
            var top = player.Library.Take(3).ToArray();
            if (top.Length == 0)
            {
                FinishStackItem(item);
                return true;
            }
            item.Data["prometheus-top"] = string.Join('|', top.Select(card => card.InstanceId));
            var choices = top.Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus"))
                .Select(card => card.InstanceId).ToList();
            choices.Add("skip");
            var data = new Dictionary<string, string>
            {
                ["action"] = "s2-prometheus-pick",
                ["choiceMode"] = "optional-add",
                ["skip"] = "未找到可加入手牌的【奥林匹斯】卡牌",
            };
            foreach (var card in top) AddPromptCardData(data, card);
            CreatePrompt(item.Controller, "optional-card", "普罗米修斯：查看牌库顶部3张牌，选择1张【奥林匹斯】卡牌加入手牌",
                choices, 1, 1, "card-effect", item.StackItemId, data: data);
            return true;
        }
        if (ability == "runeUse" && source?.CardId == "S02-06C1")
        {
            if (item.Data.GetValueOrDefault("mode") == "mode:trial")
                AdvanceTrial(item.Controller, 1, CreateCard("S02-06S1", $"rune-effect-{State.TurnSerial}"));
            else if (!Draw(player, 1))
                SetWinner(1 - item.Controller, "符文效果抽牌时牌库为空");
            FinishStackItem(item);
            return true;
        }
        if (ability == "merlinRune" && source?.CardId == "S02-0603")
        {
            if (item.Data.GetValueOrDefault("mode") == "mode:debuff")
            {
                var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
                if (target is not null) AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), "梅林");
            }
            else
            {
                var choices = player.Library.Where(card => card.CardType == "tactic"
                        && card.CurrentCost <= 4 && !IsCounterTactic(card.CardId))
                    .Select(card => card.InstanceId).ToList();
                if (choices.Count == 0)
                {
                    AddEvent("reveal", item.Controller, "梅林查看牌库，但未找到费用不高于4的主动战术", source);
                    ShuffleLibrary(player, "梅林检索未命中");
                    FinishStackItem(item);
                    return true;
                }
                var data = new Dictionary<string, string> { ["action"] = "s2-merlin-search" };
                foreach (var card in player.Library.Where(card => choices.Contains(card.InstanceId)))
                    AddPromptCardData(data, card);
                CreateDelayedPublicResolutionPrompt(item, "card",
                    "梅林：查看牌库并选择1张费用不高于4的主动战术展示并加入手牌",
                    choices, "s2-merlin-search", data, isPrivate: true);
                return true;
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "aristotleDiscount" && source?.CardId == "S02-0513")
        {
            player.NextS2OlympusLegionDiscount = Math.Max(player.NextS2OlympusLegionDiscount, 1);
            AddEvent("effect", item.Controller, "亚里士多德使本回合下一张【奥林匹斯】军团登场费用-1", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "imhotepDiscount" && source?.CardId == "S02-0204")
        {
                    player.NextS2SunDisasterLegionDiscount += 1;
            AddEvent("effect", item.Controller, "伊姆何泰普使本回合下1张带有天灾等级的【太阳城】军团登场费用-1", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "scarabSummon" && source?.CardId == "S02-0205")
        {
            var scarab = player.Graveyard.FirstOrDefault(card => card.CardId == "S02-0201");
            if (scarab is null)
                AddEvent("effect-cancelled", item.Controller,
                    "黄金圣甲虫声明的增殖甲虫已失效；主动休整费用不返还");
            else
                _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, scarab.InstanceId,
                    item.Data.GetValueOrDefault("target") ?? string.Empty, tapped: false);
            FinishStackItem(item);
            return true;
        }
        if (ability == "scarabDebuff" && source?.CardId == "S02-0205")
        {
            foreach (var id in (item.Data.GetValueOrDefault("targets") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).Take(2))
            {
                var target = DeclaredEnemyTarget(item.Controller, id);
                if (target is not null)
                    AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "黄金圣甲虫");
            }
            ResolveStateBasedLegionDeaths();
            FinishStackItem(item);
            return true;
        }
        if (ability == "magatamaMove" && source?.CardId == "S02-0404")
        {
            var targetPlayer = State.Players[item.Controller];
            var legion = FindOnField(targetPlayer, item.Data.GetValueOrDefault("target"), out var row, out var slot);
            var destinationText = item.Data.GetValueOrDefault("destination") ?? string.Empty;
            if (legion is not null && !legion.Hidden && !legion.Tapped && IsFieldLegion(legion)
                && EffectCavalryDestinations(targetPlayer).Contains(destinationText))
            {
                var (targetRow, targetSlot) = ParseSlot(destinationText);
                targetPlayer.Field[row][slot] = null;
                targetPlayer.Field[targetRow][targetSlot] = legion;
                legion.LastMovedTurn = State.TurnSerial;
                AddEvent("move", item.Controller, $"八尺琼勾玉使〈{legion.Name}〉位移", source, legion);
                RecordLegionMovement(item.Controller, legion, row, targetRow);
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "magatamaImmortal" && source?.CardId == "S02-0404")
        {
            var legion = FindOnField(player, item.Data.GetValueOrDefault("target"), out _, out _);
            if (legion is not null && legion.LastMovedTurn == State.TurnSerial)
            {
                legion.ImmortalUses = Math.Max(legion.ImmortalUses, 1);
                legion.ImmortalUntilTurn = Math.Max(legion.ImmortalUntilTurn, ExpiryAtNextOwnEnd(item.Controller));
                AddEvent("effect", item.Controller, $"八尺琼勾玉使〈{legion.Name}〉本回合获得免死", source, legion);
            }
            FinishStackItem(item);
            return true;
        }
        if (ability == "amakineTop" && item.SourceCardId == "S02-0616")
        {
            if (player.Library.Count == 0)
            {
                FinishStackItem(item);
                return true;
            }
            var top = player.Library[0];
            item.Data["amakine-top"] = top.InstanceId;
            var choices = top.Faction == "otherworld" ? new[] { "hand", "top", "bottom" } : new[] { "top", "bottom" };
            var data = new Dictionary<string, string>
            {
                ["action"] = "s2-amakine-top-place", ["previewCardId"] = top.InstanceId,
                ["previewPresentation"] = "handled-card",
                ["hand"] = "加入手牌", ["top"] = "返回牌库顶部", ["bottom"] = "返回牌库底部",
            };
            AddPromptCardData(data, top);
            CreatePrompt(item.Controller, "option", $"阿麦金展示牌库顶部的〈{top.Name}〉", choices, 1, 1,
                "card-effect", item.StackItemId, data: data);
            return true;
        }
        if (ability == "galahadGrailReward" && item.SourceCardId == "S02-0604")
        {
            if (!Draw(player, 1))
            {
                SetWinner(1 - item.Controller, "加拉哈德效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            }
            if (item.Data.GetValueOrDefault("healMode") == "mode:heal")
                HealMaster(item.Controller, 1, "加拉哈德完成寻找圣杯之旅后的效果", legionEffect: true);
            FinishStackItem(item);
            return true;
        }
        if (ability == "completeTrial" && source?.CardType == "trial")
        {
            source.TrialCompleted = true;
            player.SpecialZones.TrialLevel = player.SpecialZones.Trials.Where(card => !card.TrialCompleted).Select(card => card.TrialProgress).DefaultIfEmpty().Max();
            AddEvent("trial", item.Controller, $"完成试炼《{source.Name}》", source);
            QueueCompletedTrialTriggerBatch(item.Controller, source);
            FinishStackItem(item);
            return true;
        }
        if (source?.CardType == "trial" && ability is "fenianReady" or "crusadeTrialNoLoss" or "crusadeRichardPiercing" or "crusadeRecover")
        {
            var declared = (item.Data.GetValueOrDefault("target") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (ability == "fenianReady")
            {
                var target = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (target is not null) target.Tapped = false;
            }
            else if (ability == "crusadeTrialNoLoss")
            {
                var target = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (target is not null) target.NextAttackNoLossUses++;
            }
            else if (ability == "crusadeRichardPiercing")
            {
                var target = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (target is not null) player.UsedAbilities.Add($"crusade-piercing:{target.InstanceId}:{State.TurnSerial}");
            }
            else if (declared.Length == 2)
            {
                var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[1] && card.Faction == "otherworld");
                if (recover is not null)
                {
                    player.Graveyard.Remove(recover);
                    AddCardToHandByEffect(player, recover, "graveyard", "十字军东征回收彼界卡牌");
                }
            }
            FinishStackItem(item);
            return true;
        }
        return TryResolveS2RemainingAbility(item, source, ability);
    }

    private bool TryContinueS2Faction(L12StackItem item, L12Prompt prompt, List<string> chosen, L12Command command)
    {
        if (TryContinueTrialCompletionEffect(item, prompt, chosen)) return true;
        var player = State.Players[item.Controller];
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            case "s2-merlin-search":
            {
                var selected = player.Library.FirstOrDefault(card => card.InstanceId == chosen[0]
                    && card.CardType == "tactic" && card.CurrentCost <= 4 && !IsCounterTactic(card.CardId));
                if (selected is not null)
                {
                    player.Library.Remove(selected);
                    AddCardToHandByEffect(player, selected, "library", "梅林检索主动战术");
                    AddEvent("reveal", item.Controller, $"梅林展示〈{selected.Name}〉并加入手牌", selected);
                }
                ShuffleLibrary(player, "梅林检索结算");
                FinishStackItem(item);
                return true;
            }
            case "s2-arthur-sword":
            {
                var arthur = FindSource(item);
                if (chosen[0] == "yes" && arthur is not null && L12S2ZoneOps.SpendRunes(player, 1))
                {
                    var existingSwordOwner = FindKingsSwordOwner(player);
                    if (existingSwordOwner is not null)
                    {
                        AddEvent("effect-noop", item.Controller,
                            $"〈王者之剑〉为 Limit 1，符文已消耗；剑仍叠放在〈{existingSwordOwner.Name}〉下方，本次效果无事发生",
                            arthur, existingSwordOwner);
                        FinishStackItem(item);
                        return true;
                    }
                    var sword = player.Graveyard.FirstOrDefault(card => card.CardId == "S02-06S2")
                        ?? CreateCard("S02-06S2", $"p{item.Controller}-arthur-sword-{State.TurnSerial}");
                    player.Graveyard.Remove(sword);
                    sword.OwnerIndex = item.Controller;
                    arthur.AttachedCards.Add(sword);
                    RecalculateContinuousTroops();
                    AddEvent("attach", item.Controller, "〈王者之剑〉叠放至〈亚瑟王〉下方；原本兵力+1000并获得强攻", arthur, sword);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-limu-tactic":
                if (chosen[0] == "play") PlayS2LiMuRevealedTactic(item);
                else
                {
                    MoveS2LiMuRevealedToBottom(item);
                    FinishStackItem(item);
                }
                return true;
            case "s2-okita-top":
                if (chosen[0] == "play")
                    PlayS2OkitaRevealedCard(item);
                else
                    AddS2OkitaRevealedCardToHand(item);
                return true;
            case "s2-fortune-artifact":
                CompleteS2FortunePick(item, chosen[0], "artifact");
                return true;
            case "s2-fortune-uesugi":
                CompleteS2FortunePick(item, chosen[0], "uesugi");
                return true;
            case "s2-fortune-bottom-order":
                CompleteS2FortuneBottomOrder(item, command.BottomCardInstanceIds ?? chosen);
                return true;
            case "s2-prometheus-pick":
            {
                var topIds = item.Data.GetValueOrDefault("prometheus-top", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var selectedId = chosen[0];
                if (selectedId != "skip")
                {
                    var selected = player.Library.FirstOrDefault(card => card.InstanceId == selectedId
                        && topIds.Contains(card.InstanceId)
                        && L12StructuredCardRules.HasFaction(player, card, "olympus"));
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        AddCardToHandByEffect(player, selected, "library", "普罗米修斯将奥林匹斯卡牌加入手牌");
                    }
                }
                var remaining = topIds.Where(id => player.Library.Any(card => card.InstanceId == id)).ToArray();
                if (remaining.Length == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                item.Data["reorder-context"] = "prometheus";
                item.Data["reorder-cards"] = string.Join('|', remaining);
                CreatePrompt(item.Controller, "order", "普罗米修斯：排列其余卡牌，并将其全部放回牌库顶部或全部放回牌库底部",
                    remaining, remaining.Length, remaining.Length, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "reorder-order",
                        ["placementMode"] = "all-top-bottom",
                    });
                return true;
            }
            case "s2-takeda-search":
            {
                if (chosen[0] != "skip")
                {
                    var selected = player.Library.FirstOrDefault(card => card.InstanceId == chosen[0]
                        && L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")
                        && card.CardType == "legion" && card.BaseTroops <= 5000);
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        AddCardToHandByEffect(player, selected, "library", "武田信玄检索高天原军团");
                    }
                }
                ShuffleLibrary(player, "武田信玄检索结算");
                FinishStackItem(item);
                return true;
            }
            case "s2-takeda-sanada":
                if (chosen[0] == "skip")
                {
                    FinishStackItem(item);
                    return true;
                }
                item.Data["takeda-sanada"] = chosen[0];
                PromptFirstEmptySlot(item, "s2-takeda-sanada-slot", "武田信玄：选择〈真田幸村〉活跃登场的位置");
                return true;
            case "s2-takeda-sanada-slot":
            {
                var sanadaId = item.Data.GetValueOrDefault("takeda-sanada");
                if (!string.IsNullOrWhiteSpace(sanadaId))
                    SummonFromAnyPrivateZone(player, sanadaId, chosen[0], tapped: false);
                var restedMorale = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).ToArray();
                if (restedMorale.Length == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "target-morale", "武田信玄：选择1张休整士气转为活跃",
                    restedMorale, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-takeda-ready-morale" });
                return true;
            }
            case "s2-takeda-ready-morale":
            {
                var morale = player.Morale.FirstOrDefault(card => card.InstanceId == chosen[0] && card.Tapped);
                var source = FindSource(item);
                if (morale is not null && source is not null)
                    ReadyMoraleByEffect(item.Controller, source, morale, "武田信玄使1张士气转为活跃");
                FinishStackItem(item);
                return true;
            }
            case "s2-amakine-top-place":
            {
                var top = player.Library.FirstOrDefault(card => card.InstanceId == item.Data.GetValueOrDefault("amakine-top"));
                if (top is not null)
                {
                    player.Library.Remove(top);
                    if (chosen[0] == "hand" && top.Faction == "otherworld")
                        AddCardToHandByEffect(player, top, "library", "阿麦金将牌库顶部的彼界卡牌加入手牌");
                    else if (chosen[0] == "bottom")
                    {
                        player.Library.Add(top);
                        AddEvent("return", item.Controller, $"阿麦金将〈{top.Name}〉返回牌库底部", top);
                    }
                    else
                    {
                        player.Library.Insert(0, top);
                        AddEvent("return", item.Controller, $"阿麦金将〈{top.Name}〉返回牌库顶部", top);
                    }
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-robin-summon-squire":
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, chosen[0],
                    PublicTriggerDeclared(item, "entrySlot"), tapped: false);
                FinishStackItem(item);
                return true;
            case "s2-claudia-debuff":
                if (chosen[0] != "skip" && player.SpecialZones.Runes >= 1)
                {
                    var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                    if (target is not null && L12S2ZoneOps.SpendRunes(player, 1))
                    {
                        AddTimedModifier(target, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), "克劳迪娅");
                        AddEvent("effect", item.Controller, $"克劳迪娅使{target.Name}本回合兵力-2000", target);
                    }
                }
                FinishStackItem(item);
                return true;
            case "s2-heracles-entry-damage":
                if (chosen[0] == "yes")
                {
                    DamageMasterNonLethal(0, 1, "赫拉克勒斯·晋升的登场时效果");
                    DamageMasterNonLethal(1, 1, "赫拉克勒斯·晋升的登场时效果");
                }
                FinishStackItem(item);
                return true;
            case "s2-perseus-recover-promotion":
            {
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                var discarded = player.Hand.FirstOrDefault(card => card.InstanceId == chosen[0]);
                var promotion = player.Graveyard.FirstOrDefault(card => card.CardId == "S02-0505");
                if (discarded is not null && promotion is not null)
                {
                    MoveHandToGrave(player, discarded.InstanceId, causedByEffect: false);
                    player.Graveyard.Remove(promotion);
                    AddCardToHandByEffect(player, promotion, "graveyard", "珀尔修斯将〈珀尔修斯·晋升〉加入手牌");
                    AddEvent("effect", item.Controller, "珀尔修斯弃置1张手牌，将墓地的〈珀尔修斯·晋升〉加入手牌", promotion, discarded);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-heracles-promotion-show":
            {
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                var shown = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == chosen[0] && candidate.CardType == "legion");
                if (shown is null) { FinishStackItem(item); return true; }
                player.Hand.Remove(shown);
                player.Library.Insert(0, shown);
                item.Data["heracles-shown-cost"] = shown.CurrentCost.ToString();
                AddEvent("reveal", item.Controller, $"赫拉克勒斯·晋升展示手牌中的〈{shown.Name}〉并放回牌库顶部", shown);
                ContinueHeraclesPromotionTargetChoice(item);
                return true;
            }
            case "s2-heracles-promotion-kill":
            {
                var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                var maxCost = int.TryParse(item.Data.GetValueOrDefault("heracles-shown-cost"), out var parsed) ? parsed : -1;
                if (target is not null && target.CurrentCost <= maxCost)
                    KillTarget(item, target.InstanceId, "被赫拉克勒斯·晋升击杀");
                FinishStackItem(item);
                return true;
            }
            case "s2-perseus-promotion-lock":
            {
                if (chosen[0] != "skip")
                {
                    var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                    if (target is { Tapped: true }) target.CannotUntapUntilRound = Math.Max(target.CannotUntapUntilRound, State.Round + 1);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-gaotianyuan-ready-discard":
            {
                var discarded = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == chosen[0]);
                if (discarded is null) { FinishStackItem(item); return true; }
                player.Hand.Remove(discarded);
                player.Graveyard.Add(discarded);
                var targets = (item.Data.GetValueOrDefault("s2-gaotianyuan-ready-targets") ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (targets.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target", "选择1张休整的【高天原】军团转为活跃", targets, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-gaotianyuan-ready-target" });
                return true;
            }
            case "s2-heracles-draw-discard-choice":
                if (chosen[0] != "yes")
                {
                    FinishStackItem(item);
                    return true;
                }
                if (!Draw(player, 2))
                {
                    SetWinner(1 - item.Controller, "赫拉克勒斯登场效果抽牌时牌库为空");
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "hand-card", "赫拉克勒斯：抽取2张牌后弃置1张手牌",
                    player.Hand.Select(candidate => candidate.InstanceId), 1, 1,
                    "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-olympus-draw-discard" });
                return true;
            case "s2-olympus-draw-discard":
                MoveHandToGrave(player, chosen[0], causedByEffect: true);
                FinishStackItem(item);
                return true;
            case "s2-helen-entry-discard":
                MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: true);
                FinishStackItem(item);
                return true;
            case "s2-canute-trigger-deaths":
            {
                var triggers = chosen.Select(id => FindPromptCard(item.Controller, id))
                    .Where(candidate => candidate is not null && candidate.CardType == "legion"
                        && candidate.Faction == "asgard" && HasDeathTrigger(candidate))
                    .Cast<L12CardInstance>()
                    .DistinctBy(candidate => candidate.Name, StringComparer.Ordinal)
                    .Take(2)
                    .Select(candidate => CreateTriggerCandidate(item.Controller, candidate, "death", "【阵亡时】效果"))
                    .ToArray();
                if (triggers.Length > 0)
                {
                    QueueTriggerCandidates(triggers);
                    AddEvent("effect", item.Controller, $"卡纽特大帝触发了{triggers.Length}张军团的阵亡时效果", FindSource(item) is { } source ? [source] : []);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-flip-morale":
            {
                if (chosen.Count == 0 || chosen[0] == "skip") { FinishStackItem(item); return true; }
                var morale = player.Morale.FirstOrDefault(card => card.InstanceId == chosen[0]);
                if (morale is not null)
                {
                    L12S2ZoneOps.FlipMoraleFace(player, morale.InstanceId, toGodPower: true);
                    AddEvent("morale", item.Controller, "翻转1张士气", FindSource(item) is { } source ? [source] : []);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-rune-power-pick":
            {
                var ids = item.Data.GetValueOrDefault("rune-power-top", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var selected = chosen[0] == "skip" ? null : player.Library.FirstOrDefault(card => card.InstanceId == chosen[0]);
                if (selected is not null)
                {
                    player.Library.Remove(selected);
                    AddCardToHandByEffect(player, selected, "library", "符文之力将【彼界】卡牌加入手牌");
                    AddEvent("reveal", item.Controller, $"〈符文之力〉展示〈{selected.Name}〉并加入手牌", selected);
                }
                PromptRunePowerBottomOrder(item, ids);
                return true;
            }
            case "s2-rune-power-bottom-order":
                CompleteRunePowerBottomOrder(item, chosen);
                return true;
            case "s2-joan-master-guard":
                if (chosen.Count > 0 && chosen[0] != "skip" && player.Hand.Any(card => card.InstanceId == chosen[0]))
                {
                    MoveHandToGrave(player, chosen[0], causedByEffect: false);
                    ProtectMasterUntilNextTurnStart(player, item.Controller);
                }
                FinishStackItem(item);
                return true;
            case "s2-gaotianyuan-ready-target":
            {
                var target = FindOnField(player, chosen[0], out _, out _);
                if (target is { Tapped: true } && IsFieldLegion(target)
                    && L12StructuredCardRules.HasFaction(player, target, "gaotianyuan")) target.Tapped = false;
                FinishStackItem(item);
                return true;
            }
            case "s2-asgard-death-discard":
                MoveHandToGrave(player, chosen[0], causedByEffect: true);
                FinishStackItem(item);
                return true;
            case "s2-mistletoe-debuff":
                if (chosen[0] != "skip")
                {
                    var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                    if (target is not null) AddTimedModifier(target, -6000, 0, ExpiryAtNextOwnEnd(item.Controller), "槲寄生符咒");
                }
                FinishStackItem(item);
                return true;
            case "s2-round-table-search":
            {
                var selected = player.Library.FirstOrDefault(candidate => candidate.InstanceId == chosen[0]
                    && candidate.CardType == "legion" && candidate.HasTrait("圆桌骑士"));
                if (selected is not null)
                {
                    player.Library.Remove(selected);
                    AddCardToHandByEffect(player, selected, "library", "圆桌领域将【圆桌骑士】军团加入手牌");
                }
                ShuffleLibrary(player, "圆桌领域检索结算");
                FinishStackItem(item);
                return true;
            }
            case "s2-magatama-search":
            {
                if (chosen[0] != "skip")
                {
                    var selected = player.Library.FirstOrDefault(candidate => candidate.InstanceId == chosen[0]
                        && L12StructuredCardRules.HasFaction(player, candidate, "gaotianyuan")
                        && candidate.CardType == "legion"
                        && candidate.Profession == "骑兵");
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        AddCardToHandByEffect(player, selected, "library", "八尺琼勾玉将【高天原】的【骑兵】军团加入手牌");
                        AddEvent("reveal", item.Controller, $"八尺琼勾玉展示〈{selected.Name}〉", selected);
                    }
                }
                ShuffleLibrary(player, "八尺琼勾玉检索结算");
                FinishStackItem(item);
                return true;
            }
            case "s2-glory-search":
            {
                var selected = player.Library.FirstOrDefault(card => card.InstanceId == chosen[0]
                    && L12StructuredCardRules.HasFaction(player, card, "olympus"));
                if (selected is not null)
                {
                    player.Library.Remove(selected);
                    AddCardToHandByEffect(player, selected, "library", "荣耀之路将【奥林匹斯】卡牌加入手牌");
                    AddEvent("reveal", item.Controller, $"〈荣耀之路〉展示〈{selected.Name}〉并加入手牌", selected);
                }
                ShuffleLibrary(player, "荣耀之路检索结算");
                FinishStackItem(item);
                return true;
            }
            case "s2-richard-entry-attach":
            {
                var source = FindSource(item);
                if (source is not null)
                {
                    var attached = new List<L12CardInstance>();
                    foreach (var id in chosen.Where(id => id != "skip").Take(3))
                        if (TakeS2RichardSquire(player, id) is { } squire)
                        {
                            source.AttachedCards.Add(squire);
                            attached.Add(squire);
                        }
                    if (attached.Count > 0)
                        AddEvent("attach", item.Controller, $"〈狮心王理查一世〉下方叠放{attached.Count}张〈侍从骑士〉", [source, .. attached]);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-richard-defense-extra-discard":
            {
                item.Data["richardExtraResolved"] = "true";
                if (chosen[0] == "decline")
                {
                    item.Data["invalid"] = "true";
                    AddEvent("defense", prompt.PlayerIndex, "未支付〈狮心王理查一世〉要求的额外弃牌费用，本次抵挡/支援无效");
                }
                else MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: false);
                ResolveAuthorityEvent(item);
                return true;
            }
            case "s2-imhotep-recover":
                if (chosen[0] != "skip") MoveGraveToHand(player, chosen[0]);
                FinishStackItem(item);
                return true;
            default:
                return TryContinueS2RemainingEffect(item, prompt, chosen);
        }
    }


    private void RevealS2LiMuTop(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        if (player.Library.Count == 0)
        {
            FinishStackItem(item);
            return;
        }
        var top = player.Library[0];
        item.Data["s2-limu-top"] = top.InstanceId;
        AddEvent("reveal", item.Controller, "李牧登场时，展示牌库顶的1张牌。", top);
        if (top.CardType != "tactic" || IsCounterTactic(top.CardId) || top.CurrentCost > 4)
        {
            MoveS2LiMuRevealedToBottom(item);
            FinishStackItem(item);
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-limu-tactic", ["previewCardId"] = top.InstanceId,
            ["previewPresentation"] = "handled-card",
            ["play"] = "无需消耗费用打出", ["bottom"] = "不打出，返回牌库底部",
        };
        AddPromptCardData(data, top);
        CreatePrompt(item.Controller, "option", $"李牧展示了费用不高于4的主动战术〈{top.Name}〉",
            ["play", "bottom"], 1, 1, "card-effect", item.StackItemId, data: data);
    }

    private L12CardInstance? FindS2LiMuRevealedCard(L12StackItem item)
    {
        var id = item.Data.GetValueOrDefault("s2-limu-top");
        return string.IsNullOrWhiteSpace(id)
            ? null
            : State.Players[item.Controller].Library.FirstOrDefault(card => card.InstanceId == id);
    }

    private void MoveS2LiMuRevealedToBottom(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var card = FindS2LiMuRevealedCard(item);
        if (card is null) return;
        player.Library.Remove(card);
        player.Library.Add(card);
        AddEvent("library", item.Controller, $"李牧将〈{card.Name}〉返回牌库底部", card);
    }

    private void PlayS2LiMuRevealedTactic(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var card = FindS2LiMuRevealedCard(item);
        if (card is null || card.CardType != "tactic" || IsCounterTactic(card.CardId) || card.CurrentCost > 4)
        {
            FinishStackItem(item);
            return;
        }
        _ = BeginEffectGeneratedFreePlay(item.Controller, card, item, "library", "〈李牧〉");
    }

    private void BeginS2FortuneSearch(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var top = player.Library.Take(5).ToArray();
        item.Data["s2-fortune-cards"] = string.Join('|', top.Select(card => card.InstanceId));
        var artifacts = top.Where(card => card.CardType == "artifact").ToArray();
        if (artifacts.Length == 0)
        {
            PromptS2FortuneUesugi(item);
            return;
        }

        CreateS2FortuneCardPrompt(item, "s2-fortune-artifact", "〈武运在天 铠甲在前〉：选择其中1张【圣物】加入手牌", artifacts);
    }

    private List<L12CardInstance> S2FortuneCardsStillInLibrary(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        return item.Data.GetValueOrDefault("s2-fortune-cards", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => player.Library.FirstOrDefault(card => card.InstanceId == id))
            .Where(card => card is not null)
            .Cast<L12CardInstance>()
            .ToList();
    }

    private void CreateS2FortuneCardPrompt(L12StackItem item, string action, string text,
        IReadOnlyCollection<L12CardInstance> choices)
    {
        var displayed = S2FortuneCardsStillInLibrary(item);
        var data = new Dictionary<string, string>
        {
            ["action"] = action,
            ["layout"] = "single-row",
            ["displayCardIds"] = string.Join('|', displayed.Select(card => card.InstanceId)),
        };
        foreach (var card in displayed) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "card", text, choices.Select(card => card.InstanceId), 1, 1,
            "card-effect", item.StackItemId, data: data);
    }

    private void CompleteS2FortunePick(L12StackItem item, string choice, string stage)
    {
        var player = State.Players[item.Controller];
        var selected = player.Library.FirstOrDefault(card => card.InstanceId == choice);
        var valid = selected is not null && (stage == "artifact"
            ? selected.CardType == "artifact"
            : selected.CardId == "S01-0403");
        if (valid)
        {
            player.Library.Remove(selected!);
            AddCardToHandByEffect(player, selected!, "library", $"〈武运在天 铠甲在前〉将〈{selected!.Name}〉加入手牌");
            AddEvent("search", item.Controller, $"〈武运在天 铠甲在前〉将〈{selected.Name}〉加入手牌", selected);
        }

        if (stage == "artifact") PromptS2FortuneUesugi(item);
        else PromptS2FortuneBottomOrder(item);
    }

    private void PromptS2FortuneUesugi(L12StackItem item)
    {
        var uesugi = S2FortuneCardsStillInLibrary(item)
            .Where(card => card.CardId == "S01-0403")
            .ToArray();
        if (uesugi.Length == 0)
        {
            PromptS2FortuneBottomOrder(item);
            return;
        }

        CreateS2FortuneCardPrompt(item, "s2-fortune-uesugi", "〈武运在天 铠甲在前〉：选择其中1张〈上杉谦信〉加入手牌", uesugi);
    }

    private void PromptS2FortuneBottomOrder(L12StackItem item)
    {
        var remaining = S2FortuneCardsStillInLibrary(item);
        if (remaining.Count <= 1)
        {
            CompleteS2FortuneBottomOrder(item, remaining.Select(card => card.InstanceId).ToList());
            return;
        }

        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-fortune-bottom-order",
            ["placementMode"] = "all-bottom",
            ["layout"] = "single-row",
            ["displayCardIds"] = string.Join('|', remaining.Select(card => card.InstanceId)),
        };
        foreach (var card in remaining) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "order", "调整其余卡牌的顺序，然后全部放回牌库底部。",
            remaining.Select(card => card.InstanceId), remaining.Count, remaining.Count,
            "card-effect", item.StackItemId, data: data);
    }

    private void CompleteS2FortuneBottomOrder(L12StackItem item, List<string> order)
    {
        var player = State.Players[item.Controller];
        var remainingIds = S2FortuneCardsStillInLibrary(item).Select(card => card.InstanceId).ToHashSet(StringComparer.Ordinal);
        if (order.Count != remainingIds.Count || order.Any(id => !remainingIds.Contains(id)) || order.Distinct().Count() != order.Count)
        {
            FinishStackItem(item);
            return;
        }

        foreach (var id in order)
        {
            var card = player.Library.First(candidate => candidate.InstanceId == id);
            player.Library.Remove(card);
            player.Library.Add(card);
        }
        FinishStackItem(item);
    }

    private void BeginRunePowerSearch(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var top = player.Library.Take(3).ToArray();
        item.Data["rune-power-top"] = string.Join('|', top.Select(card => card.InstanceId));
        if (top.Length == 0) { FinishStackItem(item); return; }
        var choices = top.Where(card => card.CardId != "S02-0620"
                && L12StructuredCardRules.HasFaction(player, card, "otherworld"))
            .Select(card => card.InstanceId).Append("skip").ToArray();
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-rune-power-pick", ["choiceMode"] = "optional-add",
            ["displayCardIds"] = string.Join('|', top.Select(card => card.InstanceId)),
            ["layout"] = "single-row", ["skip"] = "不将卡牌加入手牌",
        };
        foreach (var card in top) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "optional-card", "符文之力：选择1张〈符文之力〉以外的【彼界】卡牌展示并加入手牌",
            choices, 1, 1, "card-effect", item.StackItemId, data: data);
    }

    private void PromptRunePowerBottomOrder(L12StackItem item, IEnumerable<string> originalIds)
    {
        var player = State.Players[item.Controller];
        var remaining = originalIds.Select(id => player.Library.FirstOrDefault(card => card.InstanceId == id))
            .Where(card => card is not null).Cast<L12CardInstance>().ToArray();
        if (remaining.Length <= 1)
        {
            CompleteRunePowerBottomOrder(item, remaining.Select(card => card.InstanceId).ToList());
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-rune-power-bottom-order", ["placementMode"] = "all-bottom",
            ["displayCardIds"] = string.Join('|', remaining.Select(card => card.InstanceId)), ["layout"] = "single-row",
        };
        foreach (var card in remaining) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "order", "调整其余卡牌的顺序，然后全部放回牌库底部。",
            remaining.Select(card => card.InstanceId), remaining.Length, remaining.Length,
            "card-effect", item.StackItemId, data: data);
    }

    private void CompleteRunePowerBottomOrder(L12StackItem item, IReadOnlyCollection<string> order)
    {
        var player = State.Players[item.Controller];
        var remainingIds = item.Data.GetValueOrDefault("rune-power-top", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Where(id => player.Library.Any(card => card.InstanceId == id)).ToHashSet(StringComparer.Ordinal);
        if (order.Count != remainingIds.Count || order.Any(id => !remainingIds.Contains(id))
            || order.Distinct(StringComparer.Ordinal).Count() != order.Count)
        {
            FinishStackItem(item);
            return;
        }
        foreach (var id in order)
        {
            var card = player.Library.First(candidate => candidate.InstanceId == id);
            player.Library.Remove(card);
            player.Library.Add(card);
        }
        FinishStackItem(item);
    }

    private bool PromptS2FlipMorale(L12StackItem item, L12CardInstance source, bool optional = false, bool onlyTapped = false)
    {
        var player = State.Players[item.Controller];
        var choices = player.Morale.Where(card => !card.IsGodPower && (!onlyTapped || card.Tapped)).Select(card => card.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return true; }
        if (optional) choices.Add("skip");
        CreatePrompt(item.Controller, "target-morale", $"{source.Name}：选择1张士气翻转", choices, optional ? 0 : 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-flip-morale" });
        return true;
    }


    private bool PromptS2RichardEntryAttach(L12StackItem item, L12CardInstance source)
    {
        var player = State.Players[item.Controller];
        var candidates = PublicLegions(player).Where(card => card.InstanceId != source.InstanceId && card.CardId == "S02-0609")
            .Concat(player.Hand.Where(card => card.CardId == "S02-0609"))
            .Concat(player.Library.Where(card => card.CardId == "S02-0609"))
            .Concat(player.Graveyard.Where(card => card.CardId == "S02-0609"))
            .ToArray();
        if (candidates.Length == 0) { FinishStackItem(item); return true; }
        var choices = candidates.Select(card => card.InstanceId).Append("skip").ToArray();
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-richard-entry-attach", ["choiceMode"] = "multi-card", ["skip"] = "不叠放",
            ["layout"] = "single-row", ["displayCardIds"] = string.Join('|', candidates.Select(card => card.InstanceId)),
        };
        foreach (var card in candidates)
        {
            AddPromptCardData(data, card);
            data[$"{card.InstanceId}:zone"] = FindOnField(player, card.InstanceId, out _, out _) is not null ? "战场"
                : player.Hand.Contains(card) ? "手牌" : player.Library.Contains(card) ? "牌库" : "墓地";
        }
        CreatePrompt(item.Controller, "optional-cards", "狮心王理查一世：选择最多3张〈侍从骑士〉叠放至此军团下方",
            choices, 1, Math.Min(3, candidates.Length), "card-effect", item.StackItemId, data: data);
        return true;
    }

    private L12CardInstance? TakeS2RichardSquire(L12PlayerState player, string instanceId)
    {
        var squire = FindOnField(player, instanceId, out var row, out var slot);
        if (squire?.CardId == "S02-0609")
        {
            player.Field[row][slot] = null;
            ResetCardAfterLeavingField(squire);
            return squire;
        }
        squire = player.Hand.FirstOrDefault(card => card.InstanceId == instanceId && card.CardId == "S02-0609");
        if (squire is not null) { player.Hand.Remove(squire); return squire; }
        squire = player.Library.FirstOrDefault(card => card.InstanceId == instanceId && card.CardId == "S02-0609");
        if (squire is not null) { player.Library.Remove(squire); return squire; }
        squire = player.Graveyard.FirstOrDefault(card => card.InstanceId == instanceId && card.CardId == "S02-0609");
        if (squire is not null) player.Graveyard.Remove(squire);
        return squire;
    }

    private void PromptS2GlorySearch(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var choices = player.Library.Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus"))
            .Select(card => card.InstanceId).ToArray();
        if (choices.Length == 0)
        {
            AddEvent("reveal", item.Controller, "〈荣耀之路〉查看牌库但未找到【奥林匹斯】卡牌");
            ShuffleLibrary(player, "荣耀之路检索未命中");
            FinishStackItem(item);
            return;
        }
        CreatePrompt(item.Controller, "search", "荣耀之路：查看牌库并选择1张【奥林匹斯】卡牌展示加入手牌", choices, 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string>
            {
                ["action"] = "s2-glory-search", ["layout"] = "single-row", ["previewMode"] = "library-search",
                ["displayCardIds"] = string.Join('|', choices),
            });
    }

    private void ContinueHeraclesPromotionTargetChoice(L12StackItem item)
    {
        var maxCost = int.TryParse(item.Data.GetValueOrDefault("heracles-shown-cost"), out var parsed) ? parsed : -1;
        var targets = State.Players[1 - item.Controller].Field.SelectMany(row => row)
            .Where(target => target is not null && IsFieldLegion(target) && !target.Hidden && target.CurrentCost <= maxCost)
            .Select(target => target!.InstanceId)
            .ToArray();
        if (targets.Length == 0)
        {
            FinishStackItem(item);
            return;
        }
        CreatePrompt(item.Controller, "target", $"选择对方1张费用不高于{maxCost}的军团并击杀", targets, 1, 1,
            "card-effect", item.StackItemId,
            data: new Dictionary<string, string> { ["action"] = "s2-heracles-promotion-kill" });
    }

    private void ApplyS2Shock(L12StackItem item, L12CardInstance source)
    {
        var pending = State.PendingDefense;
        if (pending?.Target.Type != "legion") return;
        var defender = State.Players[1 - item.Controller];
        var target = FindOnField(defender, pending.Target.InstanceId, out var row, out var slot);
        if (target is null) return;
        var adjacent = new[] { slot - 1, slot + 1 }
            .Where(candidate => candidate is >= 0 and < 3)
            .Select(candidate => defender.Field[row][candidate])
            .Where(candidate => candidate is not null && IsFieldLegion(candidate) && !candidate.Hidden)
            .Cast<L12CardInstance>()
            .ToArray();
        var shockDamage = 2000 + (source.ShockDamageBonusUntilTurn == State.TurnSerial
            ? source.ShockDamageBonus : 0);
        foreach (var candidate in adjacent)
            AddTimedModifier(candidate, -shockDamage, 0, ExpiryAtNextOwnEnd(item.Controller), $"{source.Name}的震击");
        RecordPotentialStateBasedSourceKills(item, source, adjacent);
        if (adjacent.Length > 0)
            AddEvent("effect", item.Controller, $"{source.Name}的震击使进攻目标左右相邻军团本回合兵力-{shockDamage}", [source, .. adjacent]);
    }

    private void BeginS2OkitaAttack(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var top = player.Library.FirstOrDefault();
        if (top is null)
        {
            FinishStackItem(item);
            return;
        }

        item.Data["s2-okita-top"] = top.InstanceId;
        AddEvent("reveal", item.Controller, $"冲田总司展示牌库顶部的〈{top.Name}〉", top);
        var eligible = L12StructuredCardRules.HasFaction(player, top, "gaotianyuan") && top.CurrentCost <= 3
            && top.CardType is "legion" or "artifact" or "tactic";
        if (!eligible || top.CardType == "legion" && !EffectGeneratedFreePlaySlots(player).Any())
        {
            AddS2OkitaRevealedCardToHand(item);
            return;
        }

        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-okita-top",
            ["previewCardId"] = top.InstanceId,
            ["previewPresentation"] = "handled-card",
            ["play"] = "无需消耗费用将其打出",
            ["hand"] = "不打出，将其加入手牌",
        };
        AddPromptCardData(data, top);
        CreatePrompt(item.Controller, "option", $"冲田总司展示了费用不高于3的【高天原】卡牌〈{top.Name}〉",
            ["play", "hand"], 1, 1, "card-effect", item.StackItemId, data: data);
    }

    private L12CardInstance? FindS2OkitaRevealedCard(L12StackItem item)
    {
        var id = item.Data.GetValueOrDefault("s2-okita-top");
        return string.IsNullOrWhiteSpace(id)
            ? null
            : State.Players[item.Controller].Library.FirstOrDefault(card => card.InstanceId == id);
    }

    private void AddS2OkitaRevealedCardToHand(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var card = FindS2OkitaRevealedCard(item);
        if (card is not null)
        {
            player.Library.Remove(card);
            AddCardToHandByEffect(player, card, "library", $"冲田总司将〈{card.Name}〉加入手牌");
        }
        FinishStackItem(item);
    }

    private void PlayS2OkitaRevealedCard(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var card = FindS2OkitaRevealedCard(item);
        if (card is null || !L12StructuredCardRules.HasFaction(player, card, "gaotianyuan") || card.CurrentCost > 3)
        {
            FinishStackItem(item);
            return;
        }

        _ = BeginEffectGeneratedFreePlay(item.Controller, card, item, "library", "〈冲田总司〉");
    }

    private void BeginYingzhengEnterActivation(int playerIndex, L12CardInstance source)
    {
        var player = State.Players[playerIndex];
        var choices = player.Hand.Where(candidate => candidate.CardType == "legion" && candidate.Cost == 8)
            .Select(candidate => candidate.InstanceId).ToArray();
        if (choices.Length == 0)
        {
            QueueOrPushTriggeredEffect(playerIndex, source, "enter", "【登场时】效果",
                data: new Dictionary<string, string> { ["entryCostUnavailable"] = "true" });
            return;
        }

        var data = new Dictionary<string, string>
        {
            ["sourceInstanceId"] = source.InstanceId,
            ["choiceMode"] = "instant",
        };
        foreach (var card in player.Hand.Where(candidate => choices.Contains(candidate.InstanceId)))
            AddPromptCardData(data, card);
        CreatePrompt(playerIndex, "hand-card", "始皇帝 嬴政：请先弃置手牌中1张费用为8的军团作为登场时效果费用",
            choices, 1, 1, "s2-yingzheng-enter-cost", isPrivate: true, data: data);
    }

    private CommandResult ResolveYingzhengEnterCost(L12Prompt prompt, string selectedId)
    {
        var player = State.Players[prompt.PlayerIndex];
        var sourceId = prompt.Data.GetValueOrDefault("sourceInstanceId");
        var source = FindOnField(player, sourceId, out _, out _);
        if (source is null || !L12StructuredCardRules.RequiresPreStackEnterCost(source))
            return CommandResult.Reject("始皇帝 嬴政已不在战场，登场时效果无法发动");
        var discard = player.Hand.FirstOrDefault(card => card.InstanceId == selectedId
            && card.CardType == "legion" && card.Cost == 8);
        if (discard is null) return CommandResult.Reject("所选费用为8的军团已不在手牌中");

        player.Hand.Remove(discard);
        player.Graveyard.Add(discard);
        AddEvent("cost", prompt.PlayerIndex, $"始皇帝 嬴政弃置〈{discard.Name}〉作为登场时效果费用", discard);
        var data = CompositeFirstSegmentData("trigger:S02-0101:enter",
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
        data["entryCostPaid"] = "true";
        QueueOrPushTriggeredEffect(prompt.PlayerIndex, source, "enter", "【登场时】效果", data: data);
        return CommandResult.Ok();
    }

    private void ResolveYingzhengKillSegment(L12StackItem item)
    {
        foreach (var owner in State.Players)
            foreach (var target in owner.Field.SelectMany(row => row).Where(target => target is not null
                         && target.InstanceId != item.SourceInstanceId && IsFieldLegion(target)).Cast<L12CardInstance>().ToArray())
                KillTarget(item, target.InstanceId, "被始皇帝 嬴政击杀");
        AddEvent("effect", item.Controller, "始皇帝 嬴政击杀除此军团以外的所有军团",
            FindSource(item) is { } source ? [source] : []);
    }

    private void ResolveYingzhengReturnSegment(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var returned = player.Morale.Count;
        ReturnMorale(player, returned);
        player.FactionMoraleAdditionForbiddenUntilTurn = Math.Max(player.FactionMoraleAdditionForbiddenUntilTurn, State.TurnSerial);
        AddEvent("effect", item.Controller, $"始皇帝 嬴政返还{returned}张士气，并限制本回合非阵营效果追加士气",
            FindSource(item) is { } source ? [source] : []);
    }
}
