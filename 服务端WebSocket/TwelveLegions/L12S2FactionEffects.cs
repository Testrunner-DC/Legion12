namespace TwelveLegions.Server;

// 第二季阵营卡效。只收录已经能由规则文本唯一确定的流程；需要额外裁定的卡留在 OPEN-QUESTIONS 中。
public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> S2FactionEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0101", "S02-0203", "S02-0204", "S02-0205",
        "S02-0301", "S02-0302", "S02-0304", "S02-0402",
        "S02-0501", "S02-0502", "S02-0503", "S02-0505", "S02-0507", "S02-0509", "S02-0511", "S02-0513", "S02-0517", "S02-0518", "S02-0521", "S02-0613",
        "S02-0602", "S02-0603", "S02-0604", "S02-0606", "S02-0607", "S02-0608", "S02-0610", "S02-0612", "S02-0614", "S02-0616", "S02-0617", "S02-0618", "S02-0619",
    };

    private static readonly HashSet<string> S2FactionTacticCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0206", "S02-0207", "S02-0307", "S02-0522", "S02-0620",
    };

    private static readonly HashSet<string> S2FactionAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0103", "S02-0501", "S02-0605", "S02-0606", "S02-0607", "S02-0612", "S02-0617",
    };

    private static readonly HashSet<string> S2FactionDeathCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0202", "S02-0203", "S02-0301", "S02-0402", "S02-0508", "S02-0512", "S02-0609", "S02-0613",
    };

    private static readonly HashSet<string> S2PromotionEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0501", "S02-0503", "S02-0505", "S02-0507",
    };

    private static readonly HashSet<string> S2FactionAfterAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0602",
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

    private static List<L12AbilityView> GetS2FactionAbilities(string cardId) => cardId switch
    {
        "S02-0204" => [new("imhotepDiscount", "主动休整：本回合下1张带有天灾等级的【太阳城】军团登场费用-1")],
        "S02-0513" => [new("aristotleDiscount", "主动休整：本回合下一张【奥林匹斯】军团登场费用-1")],
        "S02-0205" =>
        [
            new("scarabSummon", "主动休整：将墓地1张〈增殖的甲虫〉活跃登场"),
            new("scarabDebuff", "我方回合1次：弃置1张手牌，选择对方最多2张军团，本回合兵力-1000"),
        ],
        "S02-0603" => [new("merlinRune", "主动休整：消耗1符文，选择敌方军团-3000，或检索费用不高于4的【主动战术】")],
        "S02-0616" => [new("amakineTop", "主动休整 展示牌库顶部1张牌：若其只拥有【彼界】特征，可加入手牌；否则返回牌库顶部或底部。")],
        _ => [],
    };

    private static bool IsTrialLegion(L12CardInstance card)
        => card.CardId is "S02-0604" or "S02-0610" or "S02-0614";

    private static bool IsProtectedByRestedAmakine(L12PlayerState owner, L12CardInstance target)
        => !target.Tapped && IsTrialLegion(target)
            && PublicLegions(owner).Any(card => card.CardId == "S02-0616" && card.Tapped);

    private void AdvanceTrial(int playerIndex, int count, L12CardInstance? source = null)
    {
        var player = State.Players[playerIndex];
        var trial = player.SpecialZones.Trials.FirstOrDefault(card => !card.TrialCompleted);
        if (trial is null || count <= 0) return;
        var before = trial.TrialProgress;
        trial.TrialProgress = Math.Min(8, trial.TrialProgress + count);
        player.SpecialZones.TrialLevel = trial.TrialProgress;
        AddEvent("trial", playerIndex, $"《{trial.Name}》试炼进度 {before} → {trial.TrialProgress}", source ?? trial);
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
        switch (card.CardId)
        {
            case "S02-0501":
                CreatePrompt(item.Controller, "optional", "是否对双方主宰各造成1点非致命伤害？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-heracles-entry-damage" });
                return true;
            case "S02-0502":
                if (!Draw(player, 2)) { SetWinner(1 - item.Controller, "该军团登场时抽牌，牌库为空"); FinishStackItem(item); return true; }
                if (player.Hand.Count == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "hand-card", "抽取2张牌后弃置1张手牌", player.Hand.Select(candidate => candidate.InstanceId), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-olympus-draw-discard" });
                return true;
            case "S02-0509":
                player.FreeTacticCount++;
                AddEvent("effect", item.Controller, $"{card.Name}使本回合下一张战术无需消耗费用", card);
                FinishStackItem(item);
                return true;
            case "S02-0503":
            case "S02-0517":
                card.CanAttackLegionsOnSummonUntilTurn = State.TurnSerial;
                FinishStackItem(item);
                return true;
            case "S02-0505":
                card.HasCharge = true;
                AddEvent("effect", item.Controller, $"{card.Name}获得冲锋", card);
                FinishStackItem(item);
                return true;
            case "S02-0507":
                CreatePrompt(item.Controller, "optional", "是否抽取1张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-atalanta-entry-draw" });
                return true;
            case "S02-0511":
                card.CanAttackLegionsOnSummonUntilTurn = State.TurnSerial;
                FinishStackItem(item);
                return true;
            case "S02-0513":
                return PromptS2FlipMorale(item, card, optional: true);
            case "S02-0518":
                return PromptS2FlipMorale(item, card, optional: true, onlyTapped: true);
            case "S02-0521":
                return PromptS2FlipMorale(item, card, optional: true);
            case "S02-0613":
            {
                var choices = player.Hand.Select(candidate => candidate.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "圣女贞德：可弃置1张手牌，使我方主宰直到下个我方回合开始前无法被进攻", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-joan-master-guard" });
                return true;
            }
            case "S02-0604":
                CreatePrompt(item.Controller, "optional", "加拉哈德：是否休整并发动试炼（试炼值2）？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-galahad-entry-trial", ["yes"] = "发动试炼", ["no"] = "不发动",
                    });
                return true;
            case "S02-0602":
                if (player.SpecialZones.Runes < 1) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional", "兰斯洛特：是否消耗1符文获得冲锋？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-lancelot-entry-charge", ["yes"] = "消耗1符文并获得冲锋", ["no"] = "不发动",
                    });
                return true;
            case "S02-0610":
                CreatePrompt(item.Controller, "optional", "芬恩：是否休整并发动试炼（试炼值1）？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-finn-entry-trial", ["yes"] = "发动试炼", ["no"] = "不发动",
                    });
                return true;
            case "S02-0614":
                CreatePrompt(item.Controller, "option", "康斯坦丝：选择登场时效果", ["rune", "trial", "skip"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-constance-entry", ["rune"] = "获得1符文",
                        ["trial"] = "休整并发动试炼（试炼值1）", ["skip"] = "不发动",
                    });
                return true;
            case "S02-0617":
            {
                var squires = player.Hand.Concat(player.Library).Concat(player.Graveyard)
                    .Where(candidate => candidate.CardId == "S02-0609").Select(candidate => candidate.InstanceId).ToList();
                squires.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "罗宾汉：可从手牌、牌库或墓地选择1张〈侍从骑士〉活跃登场",
                    squires, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-robin-summon-squire", ["skip"] = "不发动" });
                return true;
            }
            case "S02-0619":
            {
                if (player.SpecialZones.Runes < 1) { FinishStackItem(item); return true; }
                var targets = PublicLegions(State.Players[1 - item.Controller]).Select(target => target.InstanceId).ToList();
                targets.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "克劳迪娅：可消耗1符文，选择对方1张军团本回合兵力-2000",
                    targets, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-claudia-debuff", ["skip"] = "不发动" });
                return true;
            }
            case "S02-0616":
                CreatePrompt(item.Controller, "optional", "阿麦金：是否获得1符文？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-amakine-entry-rune", ["yes"] = "获得1符文", ["no"] = "不发动",
                    });
                return true;
            case "S02-0603":
            case "S02-0606":
            case "S02-0607":
            case "S02-0618":
                L12S2ZoneOps.GainRunes(player, 1);
                AddEvent("runes", item.Controller, $"{card.Name}使我方获得1符文", card);
                FinishStackItem(item);
                return true;
            case "S02-0612":
                card.HasCharge = true;
                FinishStackItem(item);
                return true;
            case "S02-0608":
                AdvanceTrial(item.Controller, 2, card);
                FinishStackItem(item);
                return true;
            case "S02-0301":
                card.CanAttackMasterOnSummonUntilTurn = State.TurnSerial;
                AddEvent("effect", item.Controller, "此军团本回合可以进攻对方主宰", card);
                FinishStackItem(item);
                return true;
            case "S02-0302":
                HealMaster(item.Controller, 1, "该军团登场时效果");
                FinishStackItem(item);
                return true;
            case "S02-0304":
                if (player.Library.Count > 0)
                {
                    var top = player.Library[0];
                    player.Library.RemoveAt(0);
                    player.Graveyard.Add(top);
                    AddEvent("discard", item.Controller, $"{card.Name}弃置牌库顶部的{top.Name}", top);
                }
                FinishStackItem(item);
                return true;
            case "S02-0402":
            {
                var choices = player.Hand.Select(candidate => candidate.InstanceId).ToList();
                var targets = PublicLegions(player).Where(candidate => candidate.Faction == "gaotianyuan" && candidate.Tapped)
                    .Select(candidate => candidate.InstanceId).ToList();
                if (choices.Count == 0 || targets.Count == 0) { FinishStackItem(item); return true; }
                item.Data["s2-gaotianyuan-ready-targets"] = string.Join('|', targets);
                CreatePrompt(item.Controller, "hand-card", "弃置1张手牌：选择1张休整的【高天原】军团转为活跃", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-gaotianyuan-ready-discard" });
                return true;
            }
            case "S02-0101":
            {
                var choices = player.Hand.Where(candidate => candidate.Cost == 8).Select(candidate => candidate.InstanceId).ToArray();
                if (choices.Length == 0)
                {
                    AddEvent("reveal", item.Controller, "始皇帝 嬴政登场时未满足发动条件，展示我方所有手牌", player.Hand.ToArray());
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "hand-card", "始皇帝 嬴政：弃置手牌中1张费用为8的军团", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-yingzheng-discard" });
                return true;
            }
            case "S02-0203":
            case "S02-0205":
            {
                var scarab = player.Graveyard.FirstOrDefault(candidate => candidate.CardId == "S02-0201");
                if (scarab is null || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                item.Data["scarab"] = scarab.InstanceId;
                var data = new Dictionary<string, string> { ["action"] = "s2-scarab-enter-slot", ["previewCardId"] = scarab.InstanceId };
                AddPromptCardData(data, scarab);
                CreatePrompt(item.Controller, "slot", $"{card.Name}：选择〈增殖的甲虫〉活跃登场的位置", EmptySlots(player), 1, 1,
                    "card-effect", item.StackItemId, data: data);
                return true;
            }
            case "S02-0204":
            {
                if (player.Hand.Count >= State.Players[1 - item.Controller].Hand.Count) { FinishStackItem(item); return true; }
                var choices = player.Graveyard.Where(candidate => candidate.Faction == "taiyangcheng" && candidate.CardType == "legion" && candidate.Cost >= 6)
                    .Select(candidate => candidate.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "伊姆何泰普：可将墓地1张费用为6及以上的【太阳城】军团加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-imhotep-recover" });
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
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S02-0501":
            {
                var handLegions = player.Hand.Where(candidate => candidate.CardType == "legion").Select(candidate => candidate.InstanceId).ToList();
                if (handLegions.Count == 0) { FinishStackItem(item); return; }
                handLegions.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "可展示手牌中1张军团并放回牌库顶部，随后击杀费用不高于该牌费用的对方军团",
                    handLegions, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-heracles-promotion-show" });
                return;
            }
            case "S02-0503":
                card.CanAttackLegionsOnSummonUntilTurn = State.TurnSerial;
                AddEvent("effect", item.Controller, $"{card.Name}本回合可以进攻对方军团", card);
                FinishStackItem(item);
                return;
            case "S02-0505":
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
            case "S02-0507":
                CreatePrompt(item.Controller, "optional", "是否抽取1张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-atalanta-promotion-draw" });
                return;
            default:
                FinishStackItem(item);
                return;
        }
    }

    private bool TryResolveS2FactionTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        if (card.CardId == "S02-0522")
        {
            PromptEnemyLegion(item, "s2-olympus-decree", "选择对方1张军团，本回合兵力-3000", _ => true, false);
            return true;
        }
        if (card.CardId == "S02-0620")
        {
            L12S2ZoneOps.GainRunes(player, 1);
            AddEvent("runes", item.Controller, $"{card.Name}使我方获得1符文", card);
            FinishStackItem(item);
            return true;
        }
        if (card.CardId == "S02-0307")
        {
            if (player.Library.Count == 0) { FinishStackItem(item); return true; }
            var top = player.Library[0];
            player.Library.RemoveAt(0);
            player.Graveyard.Add(top);
            AddEvent("discard", item.Controller, $"弃置牌库顶部的{top.Name}", top);
            PromptEnemyLegion(item, "s2-asgard-curse", "选择对方1张军团，本回合兵力-3000", _ => true, false);
            return true;
        }
        if (card.CardId == "S02-0206")
        {
            var targets = player.Field[0].Where(candidate => candidate is not null && IsFieldLegion(candidate)
                    && candidate.Faction == "taiyangcheng" && !candidate.Hidden)
                .Select(candidate => candidate!.InstanceId).ToArray();
            if (targets.Length == 0) { FinishStackItem(item); return true; }
            CreatePrompt(item.Controller, "target", "无畏的刺杀：选择我方前排1张【太阳城】军团", targets, 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-fearless-assassination" });
            return true;
        }
        if (card.CardId != "S02-0207") return false;
        var choices = PublicLegions(player).Select(candidate => candidate.InstanceId).ToList();
        choices.Add("skip");
        CreatePrompt(item.Controller, "optional-targets", "沙漠君临：选择我方最多3张军团弃置", choices, 0, Math.Min(3, choices.Count - 1),
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-desert-discard" });
        return true;
    }

    private bool TryResolveS2FactionAttack(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        if (card.CardId == "S02-0501")
        {
            card.HasStrongAttack = true;
            AddEvent("effect", item.Controller, $"{card.Name}本回合获得强攻", card);
            FinishStackItem(item);
            return true;
        }
        if (card.CardId == "S02-0605")
        {
            if (!player.Morale.Any(morale => !morale.Tapped)) { FinishStackItem(item); return true; }
            CreatePrompt(item.Controller, "optional", "鲍斯：是否消耗1士气，使此军团本回合获得强攻？", ["yes", "no"], 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-bors-strong" });
            return true;
        }
        if (card.CardId == "S02-0606")
        {
            var choices = player.Hand.Select(candidate => candidate.InstanceId).ToList(); choices.Add("skip");
            CreatePrompt(item.Controller, "optional-card", "帕西瓦尔：可弃置1张手牌，使此军团本回合兵力+2000", choices, 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-percival-attack-discard" });
            return true;
        }
        if (card.CardId == "S02-0607")
        {
            var choices = Enumerable.Range(0, player.SpecialZones.Runes).Select(count => $"runes:{count}").ToArray();
            CreatePrompt(item.Controller, "optional", "高文：选择本次进攻消耗的符文数量", choices, 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-gawain-runes" });
            return true;
        }
        if (card.CardId == "S02-0612")
        {
            if (player.SpecialZones.Runes <= 0) { FinishStackItem(item); return true; }
            CreatePrompt(item.Controller, "optional", "斯卡哈：是否消耗1符文，使此军团本回合进攻无损且兵力+2000？", ["yes", "no"], 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-scathach-rune" });
            return true;
        }
        if (card.CardId == "S02-0617")
        {
            L12S2ZoneOps.GainRunes(player, 1);
            if (PublicLegions(player).Any(candidate => candidate.CardId == "S02-0608")) Draw(player, 1);
            FinishStackItem(item);
            return true;
        }
        if (card.CardId != "S02-0103") return false;
        var top = player.Library.FirstOrDefault();
        if (top is not null)
        {
            AddEvent("reveal", item.Controller, $"平阳昭公主展示牌库顶部的〈{top.Name}〉", top);
            if (top.Faction == "tianting" && top.CurrentCost <= 5)
            {
                AddTimedModifier(card, 2000, 0, ExpiryAtNextOwnEnd(item.Controller), "平阳昭公主");
                AddEvent("effect", item.Controller, "平阳昭公主本回合兵力 +2000", card);
            }
            else
            {
                player.Library.Remove(top);
                player.Library.Add(top);
            }
        }
        FinishStackItem(item);
        return true;
    }

    private bool TryResolveS2FactionAfterAttack(L12StackItem item, L12CardInstance card)
    {
        if (card.CardId != "S02-0602" || item.Data.GetValueOrDefault("killed") != "true") return false;
        CreatePrompt(item.Controller, "option", "兰斯洛特击杀军团：可选择试炼+1或获得1符文", ["trial", "rune", "skip"], 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string>
            {
                ["action"] = "s2-lancelot-kill", ["trial"] = "试炼+1", ["rune"] = "获得1符文", ["skip"] = "不发动",
            });
        return true;
    }

    private bool TryResolveS2FactionDeath(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S02-0508":
                return PromptS2FlipMorale(item, card);
            case "S02-0512":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "该军团阵亡时抽牌，牌库为空");
                else AddEvent("draw", item.Controller, $"{card.Name}阵亡时抽取1张牌", card);
                FinishStackItem(item);
                return true;
            case "S02-0609":
                AdvanceTrial(item.Controller, 1, card);
                FinishStackItem(item);
                return true;
            case "S02-0613":
                HealMaster(0, 1, $"{card.Name}阵亡时效果");
                HealMaster(1, 1, $"{card.Name}阵亡时效果");
                FinishStackItem(item);
                return true;
            case "S02-0301":
            {
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "该军团阵亡时抽牌，牌库为空");
                else AddEvent("draw", item.Controller, $"{card.Name}阵亡时抽取1张牌", card);
                if (player.Hand.Count == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "hand-card", "弃置1张手牌", player.Hand.Select(candidate => candidate.InstanceId), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-asgard-death-discard" });
                return true;
            }
            case "S02-0202":
            {
                var guard = player.Graveyard.FirstOrDefault(candidate => candidate.CardId == "S01-0212");
                if (guard is not null && EmptySlots(player).Any())
                    BeginQueuedSummons(item, [guard.InstanceId], tapped: false, "陵墓圣武士：选择〈陵墓守卫〉活跃登场的位置");
                else FinishStackItem(item);
                return true;
            }
            case "S02-0203":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "哈特谢普苏特阵亡时抽牌，牌库为空");
                else AddEvent("draw", item.Controller, "哈特谢普苏特阵亡时抽取1张牌", card);
                FinishStackItem(item);
                return true;
            case "S02-0402":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "该军团阵亡时抽牌，牌库为空");
                else AddEvent("draw", item.Controller, $"{card.Name}阵亡时抽取1张牌", card);
                FinishStackItem(item);
                return true;
            default:
                return false;
        }
    }

    private CommandResult? TryBeginS2FactionActiveAbility(int playerIndex, L12CardInstance source, string ability)
    {
        var player = State.Players[playerIndex];
        if (ability == "trialAdvance" && source.TrialValue > 0)
        {
            if (source.Tapped) return CommandResult.Reject("该军团必须为活跃状态");
            if (source.SummonRound >= State.Round) return CommandResult.Reject("登场回合不能通过通常行动发动试炼");
            if (player.SpecialZones.Trials.All(card => card.TrialCompleted)) return CommandResult.Reject("没有尚未完成的试炼");
            if (player.UsedAbilities.Contains($"trial-used:{source.InstanceId}:{State.TurnSerial}"))
                return CommandResult.Reject("该军团本回合无法再次发动试炼");
            source.Tapped = true;
            AdvanceTrial(playerIndex, source.TrialValue, source);
            AddEvent("trial-action", playerIndex, $"{source.Name}发动试炼（试炼值{source.TrialValue}）", source);
            return CommandResult.Ok();
        }
        if (ability == "godPowerDraw" && source.CardId == "S02-05C1")
        {
            if (player.UsedAbilities.Contains($"active:{source.InstanceId}:{ability}")) return CommandResult.Reject("该效果本回合已经发动");
            if (!L12S2ZoneOps.ConsumeAndFlipGodPower(player, 1)) return CommandResult.Reject("需要1张活跃的神力");
            player.UsedAbilities.Add($"active:{source.InstanceId}:{ability}");
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "factionGainRune" && source.CardId == "S02-06C1A")
        {
            if (player.UsedAbilities.Contains($"active:{source.InstanceId}:{ability}")) return CommandResult.Reject("该效果本回合已经发动");
            if (!TryConsumeMorale(player, 1)) return CommandResult.Reject("需要1张活跃的士气");
            player.UsedAbilities.Add($"active:{source.InstanceId}:{ability}");
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "merlinRune" && source.CardId == "S02-0603")
        {
            var enemy = State.Players[1 - playerIndex].Field.SelectMany(row => row)
                .Where(card => card is not null && IsFieldLegion(card) && !card.Hidden).Select(card => card!.InstanceId);
            var tactics = player.Library.Where(card => card.CardType == "tactic" && card.Cost <= 4 && !IsCounterTactic(card.CardId))
                .Select(card => card.InstanceId);
            if (!enemy.Any() && !tactics.Any()) return CommandResult.Reject("没有可选择的目标或可检索的主动战术");
            return BeginPendingActivationSequence(playerIndex, source, ability,
            [
                new L12ActivationSelectionStep { Kind = "option", Text = "梅林：选择效果", ValidChoices = ["mode:debuff", "mode:search"], MinChoose = 1, MaxChoose = 1 },
                new L12ActivationSelectionStep { Kind = "active-target", Text = "梅林：声明对应的军团或牌库中的主动战术", ValidChoices = enemy.Concat(tactics).ToList(), MinChoose = 1, MaxChoose = 1 },
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
            return CommitActiveAbility(playerIndex, source, ability, target: null);
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
            if (player.UsedAbilities.Contains($"active:{source.InstanceId}:{ability}")) return CommandResult.Reject("该效果本回合已经发动");
            if (ability == "fenianReady")
            {
                if (player.SpecialZones.Runes < 1) return CommandResult.Reject("需要消耗1符文");
                var choices = PublicLegions(player).Where(card => card.Tapped && card.Faction == "otherworld"
                        && (card.CardId == "S02-0610" || card.BaseTroops <= 4000))
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) return CommandResult.Reject("没有符合条件的休整军团");
                return BeginPendingActivation(playerIndex, source, ability, choices, "选择我方1张〈芬恩〉或原本兵力不高于4000的【彼界】军团转为活跃");
            }
            if (ability == "crusadeTrialNoLoss")
            {
                if (player.SpecialZones.Runes < 1) return CommandResult.Reject("需要消耗1符文");
                var choices = PublicLegions(player).Where(card => card.CardId is "S02-0604" or "S02-0610" or "S02-0614")
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) return CommandResult.Reject("战场上没有【试炼军团】");
                return BeginPendingActivation(playerIndex, source, ability, choices, "选择我方1张【试炼军团】，本回合下一次进攻无损");
            }
            if (ability == "crusadeRichardPiercing")
            {
                if (player.SpecialZones.Runes < 2) return CommandResult.Reject("需要消耗2符文");
                var choices = PublicLegions(player).Where(card => card.CardId == "S02-0608").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) return CommandResult.Reject("战场上没有〈狮心王理查一世〉");
                return BeginPendingActivation(playerIndex, source, ability, choices, "选择我方1张〈狮心王理查一世〉");
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
        return null;
    }

    private CommandResult? TryCommitS2FactionActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey, bool? useTombGuards,
        IEnumerable<string>? returnedMoraleIds = null)
    {
        var player = State.Players[playerIndex];
        if (ability == "merlinRune" && source.CardId == "S02-0603")
        {
            if (source.Tapped || player.SpecialZones.Runes < 1) return CommandResult.Reject("梅林需要为活跃状态且消耗1符文");
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (declared.Length != 2) return CommandResult.Reject("需要声明效果模式和目标");
            var valid = declared[0] switch
            {
                "mode:debuff" => DeclaredEnemyTarget(playerIndex, declared[1]) is not null,
                "mode:search" => player.Library.Any(card => card.InstanceId == declared[1] && card.CardType == "tactic" && card.Cost <= 4 && !IsCounterTactic(card.CardId)),
                _ => false,
            };
            if (!valid) return CommandResult.Reject("梅林声明的目标已不合法");
            source.Tapped = true;
            L12S2ZoneOps.SpendRunes(player, 1);
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability, ["mode"] = declared[0], ["target"] = declared[1] });
            return CommandResult.Ok();
        }
        if (ability == "aristotleDiscount" && source.CardId == "S02-0513")
        {
            if (source.Tapped) return CommandResult.Reject("亚里士多德必须为活跃状态");
            source.Tapped = true;
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "imhotepDiscount" && source.CardId == "S02-0204")
        {
            if (source.Tapped) return CommandResult.Reject("伊姆何泰普必须为活跃状态");
            source.Tapped = true;
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "scarabSummon" && source.CardId == "S02-0205")
        {
            if (source.Tapped) return CommandResult.Reject("黄金圣甲虫必须为活跃状态");
            if (!EmptySlots(player).Contains(target ?? string.Empty)) return CommandResult.Reject("登场位置不合法");
            source.Tapped = true;
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability, ["target"] = target! });
            return CommandResult.Ok();
        }
        if (ability == "scarabDebuff" && source.CardId == "S02-0205")
        {
            if (source.Tapped) return CommandResult.Reject("黄金圣甲虫必须为活跃状态");
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var discard = declared.Length == 0 ? null : player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]);
            if (discard is null) return CommandResult.Reject("需要弃置1张手牌");
            if (declared.Skip(1).Distinct().Count() > 2 || declared.Skip(1).Any(id => DeclaredEnemyTarget(playerIndex, id) is null))
                return CommandResult.Reject("减兵目标不合法");
            source.Tapped = true;
            player.Hand.Remove(discard);
            player.Graveyard.Add(discard);
            player.UsedAbilities.Add(onceKey);
            var data = new Dictionary<string, string> { ["ability"] = ability, ["targets"] = string.Join('|', declared.Skip(1)) };
            PushEffect(playerIndex, source, "active", "主动效果", data: data);
            AddEvent("cost", playerIndex, $"弃置〈{discard.Name}〉支付黄金圣甲虫费用", discard);
            return CommandResult.Ok();
        }
        if (ability == "amakineTop" && source.CardId == "S02-0616")
        {
            if (source.Tapped) return CommandResult.Reject("阿麦金必须为活跃状态");
            if (player.Library.Count == 0) return CommandResult.Reject("牌库为空，无法展示牌库顶部的牌");
            source.Tapped = true;
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "galahadGrailReward" && source.CardId == "S02-0604")
        {
            if (!player.SpecialZones.Trials.Any(card => card.CardId == "S02-06S4" && card.TrialCompleted))
                return CommandResult.Reject("试炼《寻找圣杯之旅》尚未完成");
            PushEffect(playerIndex, source, "active", "完成试炼后的主动效果",
                data: new Dictionary<string, string> { ["ability"] = ability });
            RemoveFromField(player, source, true, "作为加拉哈德主动效果的费用被弃置");
            player.UsedAbilities.Add(onceKey);
            return CommandResult.Ok();
        }
        if (source.CardType == "trial" && ability is "fenianReady" or "crusadeTrialNoLoss" or "crusadeRichardPiercing" or "crusadeRecover")
        {
            if (!source.TrialCompleted) return CommandResult.Reject("该试炼尚未完成");
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var runeCost = ability == "fenianReady" || ability == "crusadeTrialNoLoss" ? 1 : 2;
            if (player.SpecialZones.Runes < runeCost) return CommandResult.Reject($"需要消耗{runeCost}符文");
            if (ability == "fenianReady")
            {
                var chosen = FindOnField(player, declared.FirstOrDefault(), out _, out _);
                if (chosen is null || !chosen.Tapped || chosen.Faction != "otherworld" || (chosen.CardId != "S02-0610" && chosen.BaseTroops > 4000))
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
                player.Hand.Remove(discard);
                player.Graveyard.Add(discard);
                AddEvent("cost", playerIndex, $"弃置〈{discard.Name}〉支付十字军东征费用", discard);
            }
            L12S2ZoneOps.SpendRunes(player, runeCost);
            player.UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "已完成试炼的主动效果",
                data: new Dictionary<string, string> { ["ability"] = ability, ["target"] = target ?? string.Empty });
            return CommandResult.Ok();
        }
        return null;
    }

    private bool TryResolveS2FactionActive(L12StackItem item, L12CardInstance? source, string ability)
    {
        var player = State.Players[item.Controller];
        if (ability == "godPowerDraw" && source?.CardId == "S02-05C1")
        {
            Draw(player, 1);
            FinishStackItem(item);
            return true;
        }
        if (ability == "factionGainRune" && source?.CardId == "S02-06C1A")
        {
            L12S2ZoneOps.GainRunes(player, 1);
            AddEvent("effect", item.Controller, "获得1枚符文", source);
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
                var target = player.Library.FirstOrDefault(card => card.InstanceId == item.Data.GetValueOrDefault("target")
                    && card.CardType == "tactic" && card.Cost <= 4 && !IsCounterTactic(card.CardId));
                if (target is not null)
                {
                    player.Library.Remove(target);
                    AddCardToHandByEffect(player, target, "library", "梅林检索主动战术");
                    Shuffle(player.Library);
                }
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
            player.NextS2SunDisasterLegionDiscount = Math.Max(player.NextS2SunDisasterLegionDiscount, 1);
            AddEvent("effect", item.Controller, "伊姆何泰普使本回合下1张带有天灾等级的【太阳城】军团登场费用-1", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "scarabSummon" && source?.CardId == "S02-0205")
        {
            var scarab = player.Graveyard.FirstOrDefault(card => card.CardId == "S02-0201");
            if (scarab is not null && EmptySlots(player).Contains(item.Data.GetValueOrDefault("target") ?? string.Empty))
                SummonFromAnyPrivateZone(player, scarab.InstanceId, item.Data["target"], tapped: false);
            FinishStackItem(item);
            return true;
        }
        if (ability == "scarabDebuff" && source?.CardId == "S02-0205")
        {
            foreach (var id in (item.Data.GetValueOrDefault("targets") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).Take(2))
            {
                var target = DeclaredEnemyTarget(item.Controller, id);
                if (target is not null) AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "黄金圣甲虫");
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
            HealMaster(item.Controller, 1, "加拉哈德完成寻找圣杯之旅后的效果");
            FinishStackItem(item);
            return true;
        }
        if (ability == "completeTrial" && source?.CardType == "trial")
        {
            source.TrialCompleted = true;
            player.SpecialZones.TrialLevel = player.SpecialZones.Trials.Where(card => !card.TrialCompleted).Select(card => card.TrialProgress).DefaultIfEmpty().Max();
            AddEvent("trial", item.Controller, $"完成试炼《{source.Name}》", source);
            if (player.MasterId == "S02-06M2")
            {
                L12S2ZoneOps.GainRunes(player, 1);
                AddEvent("runes", item.Controller, "完成试炼，安格斯·麦·奥格获得1枚符文", source);
            }
            ResolveCompletedTrialTrigger(item, source);
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
        return false;
    }

    private void ResolveCompletedTrialTrigger(L12StackItem item, L12CardInstance trial)
    {
        var player = State.Players[item.Controller];
        switch (trial.CardId)
        {
            case "S02-06S3":
            {
                player.S2ArthurDiscountUntilTurn = Math.Max(player.S2ArthurDiscountUntilTurn, State.TurnSerial);
                var choices = player.Library.Concat(player.Graveyard).Where(card => card.CardId == "S02-0601")
                    .Select(card => card.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "可从牌库或墓地将1张〈亚瑟王〉加入手牌；随后墓地其余〈亚瑟王〉返回牌库并重洗",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-lake-lady-arthur" });
                return;
            }
            case "S02-06S4":
            {
                var choices = player.Library.Where(card => card.Faction == "otherworld" && card.CardType == "legion")
                    .Select(card => card.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-card", "可查看牌库并选择1张【彼界】军团展示加入手牌，随后重洗牌库",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-grail-search" });
                return;
            }
            case "S02-06S5":
            {
                var choices = State.Players[1 - item.Controller].Field.SelectMany(row => row)
                    .Where(card => card is not null && IsFieldLegion(card) && !card.Hidden)
                    .Select(card => card!.InstanceId).ToList();
                choices.Add("skip");
                if (player.SpecialZones.Runes == 0 || choices.Count == 1) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "optional-targets", "可消耗X符文；每消耗1符文选择对方1张军团，本回合兵力-3000",
                    choices, 1, Math.Min(player.SpecialZones.Runes, choices.Count - 1), "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-fenian-trial-debuff" });
                return;
            }
            default:
                FinishStackItem(item);
                return;
        }
    }

    private bool TryContinueS2Faction(L12StackItem item, L12Prompt prompt, List<string> chosen, L12Command command)
    {
        var player = State.Players[item.Controller];
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            case "s2-amakine-entry-rune":
                if (chosen[0] == "yes")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    var amakine = FindSource(item);
                    if (amakine is null) AddEvent("runes", item.Controller, "阿麦金使我方获得1符文");
                    else AddEvent("runes", item.Controller, "阿麦金使我方获得1符文", amakine);
                }
                FinishStackItem(item);
                return true;
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
            case "s2-lancelot-entry-charge":
                if (chosen[0] == "yes" && player.SpecialZones.Runes >= 1
                    && SourceIsFieldCard(item.Controller, item.SourceInstanceId, out var lancelot))
                {
                    L12S2ZoneOps.SpendRunes(player, 1);
                    lancelot.HasCharge = true;
                    AddEvent("effect", item.Controller, "兰斯洛特消耗1符文获得冲锋", lancelot);
                }
                FinishStackItem(item);
                return true;
            case "s2-lancelot-kill":
                if (chosen[0] == "trial") AdvanceTrial(item.Controller, 1, FindSource(item));
                else if (chosen[0] == "rune")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    var killSource = FindSource(item);
                    if (killSource is null) AddEvent("runes", item.Controller, "兰斯洛特击杀后获得1符文");
                    else AddEvent("runes", item.Controller, "兰斯洛特击杀后获得1符文", killSource);
                }
                FinishStackItem(item);
                return true;
            case "s2-robin-summon-squire":
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                BeginQueuedSummons(item, [chosen[0]], tapped: false, "罗宾汉：选择〈侍从骑士〉活跃登场的位置");
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
            case "s2-galahad-entry-trial":
                if (chosen[0] == "yes" && SourceIsFieldCard(item.Controller, item.SourceInstanceId, out var galahad))
                {
                    galahad.Tapped = true;
                    AdvanceTrial(item.Controller, galahad.TrialValue, galahad);
                }
                FinishStackItem(item);
                return true;
            case "s2-finn-entry-trial":
                if (chosen[0] != "yes" || !SourceIsFieldCard(item.Controller, item.SourceInstanceId, out var finn))
                {
                    FinishStackItem(item);
                    return true;
                }
                finn.Tapped = true;
                AdvanceTrial(item.Controller, finn.TrialValue, finn);
                if (player.SpecialZones.Runes < 1)
                {
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "optional", "芬恩发动试炼后：是否消耗1符文将其转为活跃？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-finn-entry-ready", ["yes"] = "消耗1符文并转为活跃", ["no"] = "保持休整",
                    });
                return true;
            case "s2-finn-entry-ready":
                if (chosen[0] == "yes" && player.SpecialZones.Runes >= 1
                    && SourceIsFieldCard(item.Controller, item.SourceInstanceId, out var readyFinn))
                {
                    L12S2ZoneOps.SpendRunes(player, 1);
                    readyFinn.Tapped = false;
                    player.UsedAbilities.Add($"trial-used:{readyFinn.InstanceId}:{State.TurnSerial}");
                    AddEvent("effect", item.Controller, "芬恩消耗1符文转为活跃，本回合无法再次发动试炼", readyFinn);
                }
                FinishStackItem(item);
                return true;
            case "s2-constance-entry":
                if (chosen[0] == "rune")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    var source = FindSource(item);
                    if (source is null) AddEvent("runes", item.Controller, "康斯坦丝使我方获得1符文");
                    else AddEvent("runes", item.Controller, "康斯坦丝使我方获得1符文", source);
                }
                else if (chosen[0] == "trial" && SourceIsFieldCard(item.Controller, item.SourceInstanceId, out var constance))
                {
                    constance.Tapped = true;
                    AdvanceTrial(item.Controller, constance.TrialValue, constance);
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
            case "s2-atalanta-entry-draw":
            case "s2-atalanta-promotion-draw":
                if (chosen[0] == "yes" && !Draw(player, 1)) SetWinner(1 - item.Controller, "阿塔兰忒·晋升效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "s2-heracles-promotion-show":
            {
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                var shown = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == chosen[0] && candidate.CardType == "legion");
                if (shown is null) { FinishStackItem(item); return true; }
                player.Hand.Remove(shown);
                player.Library.Insert(0, shown);
                item.Data["heracles-shown-cost"] = shown.CurrentCost.ToString();
                AddEvent("reveal", item.Controller, $"展示〈{shown.Name}〉并放回牌库顶部", shown);
                var targets = State.Players[1 - item.Controller].Field.SelectMany(row => row)
                    .Where(target => target is not null && IsFieldLegion(target) && !target.Hidden && target.CurrentCost <= shown.CurrentCost)
                    .Select(target => target!.InstanceId).ToArray();
                if (targets.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target", $"选择对方1张费用不高于{shown.CurrentCost}的军团并击杀", targets, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-heracles-promotion-kill" });
                return true;
            }
            case "s2-heracles-promotion-kill":
            {
                var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                var maxCost = int.TryParse(item.Data.GetValueOrDefault("heracles-shown-cost"), out var parsed) ? parsed : -1;
                if (target is not null && target.CurrentCost <= maxCost)
                    RemoveFromField(State.Players[1 - item.Controller], target, true, "被赫拉克勒斯·晋升击杀");
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
            case "s2-lake-lady-arthur":
            {
                if (chosen[0] != "skip")
                {
                    var selected = player.Library.Concat(player.Graveyard).FirstOrDefault(card => card.InstanceId == chosen[0] && card.CardId == "S02-0601");
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        player.Graveyard.Remove(selected);
                        AddCardToHandByEffect(player, selected, "library-or-graveyard", "湖中仙女的馈赠将亚瑟王加入手牌");
                    }
                }
                foreach (var arthur in player.Graveyard.Where(card => card.CardId == "S02-0601").ToArray())
                {
                    player.Graveyard.Remove(arthur);
                    player.Library.Add(arthur);
                }
                Shuffle(player.Library);
                FinishStackItem(item);
                return true;
            }
            case "s2-grail-search":
            {
                if (chosen[0] != "skip")
                {
                    var selected = player.Library.FirstOrDefault(card => card.InstanceId == chosen[0] && card.Faction == "otherworld" && card.CardType == "legion");
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        AddCardToHandByEffect(player, selected, "library", "寻找圣杯之旅将彼界军团加入手牌");
                    }
                }
                Shuffle(player.Library);
                FinishStackItem(item);
                return true;
            }
            case "s2-fenian-trial-debuff":
            {
                var targets = chosen.Where(id => id != "skip").Distinct().Take(player.SpecialZones.Runes).ToArray();
                if (targets.Length > 0 && L12S2ZoneOps.SpendRunes(player, targets.Length))
                    foreach (var id in targets)
                    {
                        var target = DeclaredEnemyTarget(item.Controller, id);
                        if (target is not null) AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), "芬尼亚传奇");
                    }
                FinishStackItem(item);
                return true;
            }
            case "s2-bors-strong":
            {
                var source = FindSource(item);
                if (chosen[0] == "yes" && source is not null && TryConsumeMorale(player, 1)) source.HasStrongAttack = true;
                FinishStackItem(item);
                return true;
            }
            case "s2-percival-attack-discard":
            {
                var source = FindSource(item);
                if (chosen[0] != "skip" && source is not null && player.Hand.Any(card => card.InstanceId == chosen[0]))
                {
                    MoveHandToGrave(player, chosen[0]);
                    AddTimedModifier(source, 2000, 0, ExpiryAtNextOwnEnd(item.Controller), "帕西瓦尔");
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-gawain-runes":
            {
                var source = FindSource(item);
                var count = int.TryParse(chosen[0].Split(':').LastOrDefault(), out var parsed) ? parsed : 0;
                if (source is not null && count > 0 && player.SpecialZones.Runes >= count && L12S2ZoneOps.SpendRunes(player, count))
                {
                    AddTimedModifier(source, count * 1000, 0, ExpiryAtNextOwnEnd(item.Controller), "高文");
                    if (State.PendingDefense is { Target.Type: "master" } pending)
                        pending.MasterDamage += count;
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-scathach-rune":
            {
                var source = FindSource(item);
                if (chosen[0] == "yes" && source is not null && L12S2ZoneOps.SpendRunes(player, 1))
                {
                    AddTimedModifier(source, 2000, 0, ExpiryAtNextOwnEnd(item.Controller), "斯卡哈");
                    source.AttackNoLossUntilTurn = Math.Max(source.AttackNoLossUntilTurn, ExpiryAtNextOwnEnd(item.Controller));
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-faith-zealot":
            {
                var ability = chosen[0];
                if (ability == "skip")
                {
                    FinishStackItem(item);
                    return true;
                }
                var master = CreateCard(player.MasterId, $"master-{item.Controller}");
                if (!GetAbilities(player.MasterId).Any(view => view.Id.Equals(ability, StringComparison.OrdinalIgnoreCase))
                    || GetActiveAbilityMoraleCost(master, ability) <= 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                item.Data["selected"] = ability;
                State.FreeMasterActivation = new L12FreeMasterActivation
                {
                    Controller = item.Controller,
                    Ability = ability,
                    SourceInstanceId = item.SourceInstanceId,
                };
                var result = BeginActiveAbility(item.Controller, new L12Command("activateAbility", CardInstanceId: master.InstanceId, Ability: ability));
                if (!result.Accepted)
                {
                    State.FreeMasterActivation = null;
                    AddEvent("effect-failed", item.Controller, $"〈信仰狂热者〉无法发动所选主宰效果：{result.Error}");
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-yingzheng-discard":
            {
                var discard = player.Hand.FirstOrDefault(card => card.InstanceId == chosen[0] && card.Cost == 8);
                if (discard is not null)
                {
                    player.Hand.Remove(discard);
                    player.Graveyard.Add(discard);
                    foreach (var owner in State.Players)
                        foreach (var target in owner.Field.SelectMany(row => row).Where(target => target is not null
                                     && target.InstanceId != item.SourceInstanceId && IsFieldLegion(target)).Cast<L12CardInstance>().ToArray())
                            RemoveFromField(owner, target, true, "被始皇帝 嬴政击杀");
                    var returned = player.Morale.Count;
                    ReturnMorale(player, returned);
                    player.FactionMoraleAdditionForbiddenUntilTurn = Math.Max(player.FactionMoraleAdditionForbiddenUntilTurn, State.TurnSerial);
                    AddEvent("effect", item.Controller, $"始皇帝 嬴政击杀其他所有军团，返还{returned}张士气", item.SourceInstanceId is { } ? FindSource(item) ?? discard : discard);
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
            case "s2-olympus-draw-discard":
                MoveHandToGrave(player, chosen[0]);
                FinishStackItem(item);
                return true;
            case "s2-flip-morale":
            {
                if (chosen.Count == 0 || chosen[0] == "skip") { FinishStackItem(item); return true; }
                var morale = player.Morale.FirstOrDefault(card => card.InstanceId == chosen[0]);
                if (morale is not null)
                {
                    morale.Tapped = !morale.Tapped;
                    AddEvent("morale", item.Controller, "翻转1张士气", FindSource(item) is { } source ? [source] : []);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-olympus-decree":
                if (chosen[0] != "skip")
                {
                    var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                    if (target is not null) AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), "奥林匹斯法令");
                }
                FinishStackItem(item);
                return true;
            case "s2-joan-master-guard":
                if (chosen.Count > 0 && chosen[0] != "skip" && player.Hand.Any(card => card.InstanceId == chosen[0]))
                {
                    MoveHandToGrave(player, chosen[0]);
                    player.MasterCannotBeAttackedUntilTurn = Math.Max(player.MasterCannotBeAttackedUntilTurn, State.TurnSerial + 1);
                }
                FinishStackItem(item);
                return true;
            case "s2-gaotianyuan-ready-target":
            {
                var target = FindOnField(player, chosen[0], out _, out _);
                if (target is { Faction: "gaotianyuan", Tapped: true }) target.Tapped = false;
                FinishStackItem(item);
                return true;
            }
            case "s2-asgard-death-discard":
                MoveHandToGrave(player, chosen[0]);
                FinishStackItem(item);
                return true;
            case "s2-asgard-curse":
                if (chosen[0] != "skip")
                {
                    var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                    if (target is not null) AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), "阿斯加德诅咒");
                }
                FinishStackItem(item);
                return true;
            case "s2-scarab-enter-slot":
                SummonFromAnyPrivateZone(player, item.Data["scarab"], chosen[0], tapped: false);
                FinishStackItem(item);
                return true;
            case "s2-imhotep-recover":
                if (chosen[0] != "skip") MoveGraveToHand(player, chosen[0]);
                FinishStackItem(item);
                return true;
            case "s2-desert-discard":
            {
                var discarded = 0;
                foreach (var id in chosen.Distinct().Take(3))
                {
                    var target = FindOnField(player, id, out _, out _);
                    if (target is null || !IsFieldLegion(target)) continue;
                    RemoveFromField(player, target, true, "因〈沙漠君临〉弃置");
                    discarded++;
                }
                var candidate = player.Hand.FirstOrDefault(card => card.Faction == "taiyangcheng" && card.CardType == "legion" && card.DisasterLevel == discarded);
                if (candidate is null || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                item.Data["desert-summon"] = candidate.InstanceId;
                var data = new Dictionary<string, string> { ["action"] = "s2-desert-slot", ["previewCardId"] = candidate.InstanceId };
                AddPromptCardData(data, candidate);
                CreatePrompt(item.Controller, "slot", "沙漠君临：选择该军团活跃登场的位置", EmptySlots(player), 1, 1, "card-effect", item.StackItemId, data: data);
                return true;
            }
            case "s2-fearless-assassination":
            {
                var target = FindOnField(player, chosen[0], out var row, out _);
                if (target is not null && row == 0 && target.Faction == "taiyangcheng")
                {
                    AddTimedModifier(target, 3000, 0, ExpiryAtNextOwnEnd(item.Controller), "无畏的刺杀");
                    target.SureHitAgainstLegionsUntilTurn = Math.Max(target.SureHitAgainstLegionsUntilTurn, ExpiryAtNextOwnEnd(item.Controller));
                    target.CannotReadyByEffectUntilTurn = Math.Max(target.CannotReadyByEffectUntilTurn, ExpiryAtNextOwnEnd(item.Controller));
                    target.DiscardAtEndOfTurnUntilTurn = Math.Max(target.DiscardAtEndOfTurnUntilTurn, ExpiryAtNextOwnEnd(item.Controller));
                    AddEvent("effect", item.Controller, $"〈无畏的刺杀〉使{target.Name}本回合兵力+3000、获得必中", target);
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-desert-slot":
                SummonFromAnyPrivateZone(player, item.Data["desert-summon"], chosen[0], tapped: false);
                FinishStackItem(item);
                return true;
            default:
                return false;
        }
    }

    private bool PromptS2FlipMorale(L12StackItem item, L12CardInstance source, bool optional = false, bool onlyTapped = false)
    {
        var player = State.Players[item.Controller];
        var choices = player.Morale.Where(card => !onlyTapped || card.Tapped).Select(card => card.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return true; }
        if (optional) choices.Add("skip");
        CreatePrompt(item.Controller, "target-morale", $"{source.Name}：选择1张士气翻转", choices, optional ? 0 : 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-flip-morale" });
        return true;
    }
}
