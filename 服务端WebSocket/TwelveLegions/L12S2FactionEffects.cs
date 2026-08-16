namespace TwelveLegions.Server;

// 第二季阵营卡效。只收录已经能由规则文本唯一确定的流程；需要额外裁定的卡留在 OPEN-QUESTIONS 中。
public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> S2FactionEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0101", "S02-0203", "S02-0204", "S02-0205",
        "S02-0301", "S02-0302", "S02-0304", "S02-0402",
        "S02-0502", "S02-0503", "S02-0509", "S02-0511", "S02-0513", "S02-0517", "S02-0518", "S02-0521", "S02-0613",
        "S02-0603", "S02-0606", "S02-0607", "S02-0608", "S02-0612", "S02-0616", "S02-0618",
    };

    private static readonly HashSet<string> S2FactionTacticCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0206", "S02-0207", "S02-0307", "S02-0522", "S02-0620",
    };

    private static readonly HashSet<string> S2FactionAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0103", "S02-0605", "S02-0606", "S02-0607", "S02-0612", "S02-0617",
    };

    private static readonly HashSet<string> S2FactionDeathCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0202", "S02-0203", "S02-0301", "S02-0402", "S02-0508", "S02-0512", "S02-0609", "S02-0613",
    };

    private static bool HasS2FactionImmediateEffect(string cardId, string trigger)
        => trigger switch
        {
            "enter" => S2FactionEnterCards.Contains(cardId),
            "play" => S2FactionTacticCards.Contains(cardId),
            "attack" => S2FactionAttackCards.Contains(cardId),
            "death" => S2FactionDeathCards.Contains(cardId),
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
        _ => [],
    };

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

    private bool TryResolveS2FactionEnter(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
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
            case "S02-0603":
            case "S02-0606":
            case "S02-0607":
            case "S02-0616":
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
        if (ability == "godPowerDraw" && source.CardId == "S02-05C1")
        {
            if (!L12S2ZoneOps.ConsumeAndFlipGodPower(player, 1)) return CommandResult.Reject("需要1张活跃的神力");
            player.UsedAbilities.Add($"active:{source.InstanceId}:{ability}");
            PushEffect(playerIndex, source, "active", "主动效果", data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "factionGainRune" && source.CardId == "S02-06C1A")
        {
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
            FinishStackItem(item);
            return true;
        }
        return false;
    }

    private bool TryContinueS2Faction(L12StackItem item, L12Prompt prompt, List<string> chosen, L12Command command)
    {
        var player = State.Players[item.Controller];
        switch (prompt.Data.GetValueOrDefault("action"))
        {
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
