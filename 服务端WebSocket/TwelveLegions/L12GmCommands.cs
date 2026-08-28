namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    /// <summary>
    /// 为跳过开局准备的测试沙盒建立可直接触发的天灾牌库。
    /// 普通对局仍严格走禁用、公开与选择流程；此捷径只由沙盒创建链调用。
    /// </summary>
    public void InitializeGmDisasters()
    {
        if (!DisastersEnabled || State.DisasterDeck.Count > 0) return;
        BuildDisasterPool();
        if (State.DisasterMode == "random")
        {
            BuildRandomDisasterDeck();
        }
        else if (State.DisasterMode == "custom")
        {
            var normal = State.DisasterPool.Where(card => card.CardId != "S01-DS10")
                .OrderBy(_ => _random.Next()).Take(3).ToList();
            State.CustomDisasters.Clear();
            State.CustomDisasters.AddRange(normal);
            State.CustomDisasters.Add(CreateCard("S01-DS10", "custom-disaster-final"));
            State.DisasterDeck.AddRange(State.CustomDisasters);
            State.DisasterPool.Clear();
            SetDisasterValue(0);
        }
        else
        {
            var all = State.DisasterPool.OrderBy(_ => _random.Next()).ToList();
            Shuffle(all);
            State.DisasterDeck.AddRange(all);
            State.DisasterDeck.Add(CreateCard("S01-DS10", "disaster-final"));
            State.DisasterPool.Clear();
            SetDisasterValue(0);
        }
        AddEvent("gm", null, "[GM] 已为测试沙盒建立天灾牌库；〈堙灭〉固定置于最底部");
    }

    /// <summary>
    /// 执行测试沙盒的权威 GM 指令。权限边界由 L12RoomManager 负责；这里仍完整校验
    /// 玩家、卡号、区域和位置，且所有成功动作都会写入对局事件并增加 revision。
    /// </summary>
    public CommandResult HandleGm(L12GmCommand command)
    {
        if (command.TargetPlayer is < 0 or > 1) return CommandResult.Reject("GM 目标玩家无效");
        if (string.IsNullOrWhiteSpace(command.Type)) return CommandResult.Reject("缺少 GM 操作类型");

        var result = command.Type switch
        {
            "addCard" => GmAddCard(command),
            "placeCard" => GmPlaceCard(command),
            "moveHandCard" => GmMoveHandCard(command),
            "playHandCard" => GmPlayHandCard(command),
            "destroyCard" => GmDestroyCard(command),
            "returnCardToHand" => GmReturnCardToHand(command),
            "resetCardEffects" => GmResetCardEffects(command),
            "setCardState" => GmSetCardState(command),
            "setTroops" => GmSetTroops(command),
            "startAttack" => GmStartAttack(command),
            "readyAll" => GmSetAllCardsTapped(command.TargetPlayer, false),
            "restAll" => GmSetAllCardsTapped(command.TargetPlayer, true),
            "destroyAll" => GmDestroyAll(command.TargetPlayer),
            "addMorale" => GmAddMorale(command),
            "readyMorale" => GmSetMoraleTapped(command.TargetPlayer, false),
            "restMorale" => GmSetMoraleTapped(command.TargetPlayer, true),
            "draw" => GmDraw(command),
            "mill" => GmMill(command),
            "shuffleLibrary" => GmShuffleLibrary(command.TargetPlayer),
            "setLife" => GmSetLife(command),
            "setDisaster" => GmSetDisaster(command),
            "triggerDisaster" => GmTriggerDisaster(),
            "replaceDisaster" => GmReplaceDisaster(command),
            "setPhase" => GmSetPhase(command),
            "nextPhase" => GmAdvancePhase(),
            _ => CommandResult.Reject("未知 GM 操作"),
        };

        if (!result.Accepted) return result;
        ResolveStateBasedLegionDeaths();
        State.Revision++;
        CheckWinner();
        return result;
    }

    private CommandResult GmAddCard(L12GmCommand command)
    {
        if (!TryCreateGmCard(command, out _, out var error)) return CommandResult.Reject(error);
        var player = State.Players[command.TargetPlayer];
        var count = Math.Clamp(command.Value ?? 1, 1, 20);
        var destination = command.Destination switch
        {
            "library-top" => "library-top",
            "library-bottom" => "library-bottom",
            "graveyard" => "graveyard",
            "removed" => "removed",
            _ => "hand",
        };
        for (var index = 0; index < count; index++)
        {
            if (!TryCreateGmCard(command, out var card, out error)) return CommandResult.Reject(error);
            switch (destination)
            {
                case "library-top": player.Library.Insert(0, card); break;
                case "library-bottom": player.Library.Add(card); break;
                case "graveyard": player.Graveyard.Add(card); break;
                case "removed": player.Removed.Add(card); break;
                default: player.Hand.Add(card); break;
            }
            AddEvent("gm", command.TargetPlayer,
                $"[GM] 将〈{card.Name}〉加入{GmZoneLabel(destination)}", card);
        }
        return CommandResult.Ok();
    }

    private CommandResult GmPlaceCard(L12GmCommand command)
    {
        if (!TryCreateGmCard(command, out var card, out var error)) return CommandResult.Reject(error);
        return GmPlaceCardInstance(command, card, removeFromHand: false);
    }

    private CommandResult GmPlayHandCard(L12GmCommand command)
    {
        var player = State.Players[command.TargetPlayer];
        var card = player.Hand.FirstOrDefault(item => item.InstanceId == command.CardInstanceId);
        if (card is null) return CommandResult.Reject("目标卡牌不在该玩家手牌中");
        return GmPlaceCardInstance(command, card, removeFromHand: true);
    }

    private CommandResult GmMoveHandCard(L12GmCommand command)
    {
        var player = State.Players[command.TargetPlayer];
        var card = player.Hand.FirstOrDefault(item => item.InstanceId == command.CardInstanceId);
        if (card is null) return CommandResult.Reject("目标卡牌不在该玩家手牌中");
        var destination = command.Destination switch
        {
            "library-top" => "library-top",
            "library-bottom" => "library-bottom",
            "graveyard" => "graveyard",
            "removed" => "removed",
            _ => string.Empty,
        };
        if (destination.Length == 0) return CommandResult.Reject("请选择手牌要前往的合法区域");
        player.Hand.Remove(card);
        switch (destination)
        {
            case "library-top": player.Library.Insert(0, card); break;
            case "library-bottom": player.Library.Add(card); break;
            case "graveyard": player.Graveyard.Add(card); break;
            case "removed": player.Removed.Add(card); break;
        }
        AddEvent("gm", command.TargetPlayer, $"[GM] 将手牌〈{card.Name}〉移至{GmZoneLabel(destination)}", card);
        return CommandResult.Ok();
    }

    private CommandResult GmPlaceCardInstance(L12GmCommand command, L12CardInstance card, bool removeFromHand)
    {
        var player = State.Players[command.TargetPlayer];
        card.OwnerIndex = command.TargetPlayer;

        if (card.CardType == "legion")
        {
            if (command.Row is null || command.Row < 0 || command.Row > 1
                || command.Slot is null || command.Slot < 0 || command.Slot > 2)
                return CommandResult.Reject("请选择 0–1 排、0–2 格的合法阵地");
            var targetRow = command.Row.GetValueOrDefault();
            var targetSlot = command.Slot.GetValueOrDefault();
            if (player.Field[targetRow][targetSlot] is not null)
                return CommandResult.Reject("目标阵地已有卡牌");
            if (removeFromHand) player.Hand.Remove(card);
            card.SummonRound = State.Round;
            player.Field[targetRow][targetSlot] = card;
        }
        else if (card.CardType == "artifact")
        {
            if (removeFromHand) player.Hand.Remove(card);
            card.SummonRound = State.Round;
            if (player.Relic is not null) DiscardRelic(player, player.Relic);
            player.Relic = card;
        }
        else if (card.CardType == "tactic")
        {
            if (removeFromHand) player.Hand.Remove(card);
            player.Resolving.Add(card);
        }
        else return CommandResult.Reject("该卡牌类型不能由 GM 打出到场上");

        ApplyDisasterLevelOnEntry(command.TargetPlayer, card, deferTriggerUntilStackSettles: true);
        AddEvent("gm", command.TargetPlayer,
            $"[GM] 使{(removeFromHand ? "手牌" : string.Empty)}〈{card.Name}〉无视费用打出", card);
        ResolveOnPlayContinuousEffects(command.TargetPlayer, card);
        RecalculateContinuousTroops();
        if (command.TriggerEffects)
        {
            var trigger = card.CardType is "legion" or "artifact" ? "enter" : "play";
            if (HasImmediateEffect(card, trigger))
                PushEffect(command.TargetPlayer, card, trigger, trigger == "enter" ? "【登场时】效果" : "战术效果");
            else if (player.Resolving.Remove(card))
            {
                ResetCardAfterLeavingField(card);
                player.Graveyard.Add(card);
            }
            if (card.CardType == "legion") QueueS2GrailRoundTableEntry(command.TargetPlayer, card);
        }
        else if (player.Resolving.Remove(card))
        {
            ResetCardAfterLeavingField(card);
            player.Graveyard.Add(card);
        }
        TrySettleScheduledDisasterIfIdle();
        return CommandResult.Ok();
    }

    private CommandResult GmDestroyCard(L12GmCommand command)
    {
        var player = State.Players[command.TargetPlayer];
        var fieldCard = FindOnField(player, command.CardInstanceId, out _, out _);
        if (fieldCard is not null)
        {
            if (!RemoveFromField(player, fieldCard, true, "被 GM 击杀"))
                return CommandResult.Reject("该军团的替代或免死效果阻止了击杀");
            AddEvent("gm", command.TargetPlayer, $"[GM] 击杀〈{fieldCard.Name}〉", fieldCard);
            return CommandResult.Ok();
        }
        var relic = player.Relic?.InstanceId == command.CardInstanceId
            ? player.Relic
            : player.ExtraRelics.FirstOrDefault(card => card.InstanceId == command.CardInstanceId);
        if (relic is null) return CommandResult.Reject("目标卡牌不在该玩家场上");
        DiscardRelic(player, relic);
        AddEvent("gm", command.TargetPlayer, $"[GM] 将〈{relic.Name}〉置入墓地", relic);
        return CommandResult.Ok();
    }

    private CommandResult GmReturnCardToHand(L12GmCommand command)
    {
        var card = FindPublicCard(command.CardInstanceId, out var controllerIndex);
        if (card is null || controllerIndex != command.TargetPlayer)
            return CommandResult.Reject("目标卡牌不在该玩家场上");

        var controller = State.Players[controllerIndex];
        var vanishes = L12SpecialDeckRules.VanishesWhenLeavingField(card);
        if (FindOnField(controller, card.InstanceId, out _, out _) is not null)
        {
            if (!MoveFieldCardToZone(controller, card, "hand", "被 GM 返回手牌"))
                return CommandResult.Reject("目标卡牌无法返回手牌");
        }
        else
        {
            var inRelicZone = controller.Relic?.InstanceId == card.InstanceId
                || controller.ExtraRelics.Any(candidate => candidate.InstanceId == card.InstanceId);
            if (!inRelicZone) return CommandResult.Reject("目标卡牌不在可返回手牌的场上区域");

            if (controller.Relic?.InstanceId == card.InstanceId) controller.Relic = null;
            else controller.ExtraRelics.Remove(card);
            if (card.AttachedCards.Count > 0)
                DiscardAttachedCards(card, $"{card.Name}离开圣物区");
            var owner = CardOwner(card, controller);
            ResetCardAfterLeavingField(card);
            if (vanishes)
            {
                AddEvent("derived-vanished", owner.PlayerIndex,
                    $"衍生卡〈{card.Name}〉离开圣物区时消灭，不进入其他区域", card);
                AddEvent("leave", controllerIndex, $"{card.Name}被 GM 移出圣物区", card);
            }
            else
            {
                owner.Hand.Add(card);
                AddEvent("leave", controllerIndex, $"{card.Name}被 GM 返回所有者手牌", card);
            }
            QueueTriggerCandidates(BuildS1LeaveReactionCandidates(controllerIndex, card));
            RecalculateContinuousTroops();
        }

        AddEvent("gm", controllerIndex, vanishes
            ? $"[GM] 令衍生卡〈{card.Name}〉离场；该卡依规则消灭"
            : $"[GM] 将〈{card.Name}〉返回所有者手牌", card);
        return CommandResult.Ok();
    }

    private CommandResult GmSetCardState(L12GmCommand command)
    {
        var card = FindPublicCard(command.CardInstanceId, out var owner);
        if (card is null || owner != command.TargetPlayer) return CommandResult.Reject("目标卡牌不在该玩家场上");
        var tapped = command.Destination == "rested";
        card.Tapped = tapped;
        AddEvent("gm", command.TargetPlayer, $"[GM] 将〈{card.Name}〉转为{(tapped ? "休整" : "活跃")}", card);
        return CommandResult.Ok();
    }

    private CommandResult GmSetTroops(L12GmCommand command)
    {
        var player = State.Players[command.TargetPlayer];
        var card = FindOnField(player, command.CardInstanceId, out _, out _);
        if (card is null || !IsFieldLegion(card)) return CommandResult.Reject("目标不是该玩家战场上的军团");
        var displayedValue = Math.Clamp(command.Value ?? card.Troops, 0, 99999);
        card.SetTroopsValue = Math.Max(0, displayedValue - card.ContinuousTroopsModifier);
        card.SetTroopsUntilTurn = int.MaxValue;
        card.Troops = displayedValue;
        AddEvent("gm", command.TargetPlayer, $"[GM] 将〈{card.Name}〉当前兵力设为 {displayedValue}", card);
        return CommandResult.Ok();
    }

    private CommandResult GmStartAttack(L12GmCommand command)
    {
        if (State.PendingDefense is not null || State.PendingPrompts.Count > 0 || State.EffectStack.Count > 0
            || State.PendingActivations.Count > 0 || State.PendingTriggerBatches.Count > 0)
            return CommandResult.Reject("当前仍有待处理的选择、触发或堆叠，请先完成结算或使用阶段跳转清场");

        var attacker = FindOnField(State.Players[command.TargetPlayer], command.CardInstanceId, out _, out _);
        if (attacker is null || !IsFieldLegion(attacker)) return CommandResult.Reject("请选择该玩家战场上的进攻军团");

        var previousActivePlayer = State.ActivePlayer;
        var previousPhase = State.Phase;
        var previousTapped = attacker.Tapped;
        var previousSummonRound = attacker.SummonRound;
        State.ActivePlayer = command.TargetPlayer;
        State.Phase = L12Phase.Main;
        attacker.Tapped = false;
        attacker.SummonRound = Math.Min(attacker.SummonRound, State.Round - 1);

        var target = string.IsNullOrWhiteSpace(command.TargetInstanceId)
            ? new L12AttackTarget("master")
            : new L12AttackTarget("legion", command.TargetInstanceId);
        var result = Attack(command.TargetPlayer,
            new L12Command("attack", CardInstanceId: attacker.InstanceId, Target: target));
        if (!result.Accepted)
        {
            State.ActivePlayer = previousActivePlayer;
            State.Phase = previousPhase;
            attacker.Tapped = previousTapped;
            attacker.SummonRound = previousSummonRound;
            return result;
        }

        AddEvent("gm", command.TargetPlayer,
            $"[GM] 令〈{attacker.Name}〉对{(target.Type == "master" ? "对方主宰" : "指定军团")}发起规则内测试进攻", attacker);
        return CommandResult.Ok();
    }

    private CommandResult GmSetAllCardsTapped(int playerIndex, bool tapped)
    {
        var player = State.Players[playerIndex];
        player.MasterTapped = tapped;
        foreach (var card in PublicLegions(player)) card.Tapped = tapped;
        if (player.Relic is not null) player.Relic.Tapped = tapped;
        foreach (var card in player.ExtraRelics) card.Tapped = tapped;
        AddEvent("gm", playerIndex, $"[GM] 将玩家{playerIndex + 1}场上全部卡牌转为{(tapped ? "休整" : "活跃")}");
        return CommandResult.Ok();
    }

    private CommandResult GmDestroyAll(int playerIndex)
    {
        var player = State.Players[playerIndex];
        var cards = PublicLegions(player).ToArray();
        foreach (var card in cards) RemoveFromField(player, card, true, "被 GM 击杀");
        AddEvent("gm", playerIndex, $"[GM] 尝试击杀玩家{playerIndex + 1}场上全部军团", cards);
        return CommandResult.Ok();
    }

    private CommandResult GmAddMorale(L12GmCommand command)
    {
        var count = Math.Clamp(command.Value ?? 1, 1, 8);
        var added = AddMorale(State.Players[command.TargetPlayer], count, tapped: false);
        if (added == 0) return CommandResult.Reject("士气牌库为空");
        AddEvent("gm", command.TargetPlayer, $"[GM] 追加 {added} 张活跃士气");
        return CommandResult.Ok();
    }

    private CommandResult GmSetMoraleTapped(int playerIndex, bool tapped)
    {
        var player = State.Players[playerIndex];
        foreach (var morale in player.Morale) morale.Tapped = tapped;
        AddEvent("gm", playerIndex, $"[GM] 将玩家{playerIndex + 1}全部士气/神力转为{(tapped ? "休整" : "活跃")}");
        return CommandResult.Ok();
    }

    private CommandResult GmDraw(L12GmCommand command)
    {
        var count = Math.Clamp(command.Value ?? 1, 1, 20);
        if (!Draw(State.Players[command.TargetPlayer], count)) return CommandResult.Reject("牌库数量不足");
        AddEvent("gm", command.TargetPlayer, $"[GM] 使玩家{command.TargetPlayer + 1}抽取 {count} 张牌");
        return CommandResult.Ok();
    }

    private CommandResult GmMill(L12GmCommand command)
    {
        var count = Math.Clamp(command.Value ?? 1, 1, 20);
        if (State.Players[command.TargetPlayer].Library.Count < count) return CommandResult.Reject("牌库数量不足");
        Mill(State.Players[command.TargetPlayer], count, "GM 指令");
        return CommandResult.Ok();
    }

    private CommandResult GmShuffleLibrary(int playerIndex)
    {
        Shuffle(State.Players[playerIndex].Library);
        AddEvent("gm", playerIndex, $"[GM] 洗切玩家{playerIndex + 1}牌库");
        return CommandResult.Ok();
    }

    private CommandResult GmSetLife(L12GmCommand command)
    {
        var value = Math.Clamp(command.Value ?? 1, 0, 99);
        State.Players[command.TargetPlayer].Hp = value;
        AddEvent("gm", command.TargetPlayer, $"[GM] 将玩家{command.TargetPlayer + 1}主宰血量设为 {value}");
        return CommandResult.Ok();
    }

    private CommandResult GmSetDisaster(L12GmCommand command)
    {
        if (!DisastersEnabled) return CommandResult.Reject("当前沙盒未启用天灾");
        var value = Math.Clamp(command.Value ?? 0, 0, 99);
        SetDisasterValue(value, command.TargetPlayer, $"[GM] 将当前天灾值设为 {value}");
        return CommandResult.Ok();
    }

    private CommandResult GmTriggerDisaster()
    {
        if (!DisastersEnabled) return CommandResult.Reject("当前沙盒未启用天灾");
        if (State.ActiveDisaster?.CardId == "S01-DS10") return CommandResult.Reject("最终天灾〈堙灭〉已触发");
        if (State.DisasterDeck.Count == 0) return CommandResult.Reject("天灾牌库为空");
        SetDisasterValue(9, null, "[GM] 将天灾值设为触发阈值 9");
        BeginDisasterTrigger(opening: false);
        return CommandResult.Ok();
    }

    private CommandResult GmReplaceDisaster(L12GmCommand command)
    {
        if (State.DisasterMode != "custom" || State.CustomDisasters.Count != 4)
            return CommandResult.Reject("只有自定天灾沙盒可更换本局天灾");
        if (command.Slot is null || command.Slot is < 0 or > 3)
            return CommandResult.Reject("天灾槽位无效");
        if (command.Slot == 3) return CommandResult.Reject("最终天灾〈堙灭〉固定在第四槽，不能更换");
        if (!TryCreateGmCard(command, out var replacement, out var error)) return CommandResult.Reject(error);
        if (replacement.CardType != "destruction" || replacement.CardId == "S01-DS10")
            return CommandResult.Reject("请选择非最终天灾卡牌");
        if (State.CustomDisasters.Where((_, index) => index != command.Slot)
            .Any(card => card.CardId == replacement.CardId))
            return CommandResult.Reject("本局自定天灾不能重复");

        var slot = command.Slot.Value;
        var previous = State.CustomDisasters[slot];
        State.CustomDisasters[slot] = replacement;

        var deckIndex = State.DisasterDeck.FindIndex(card => card.InstanceId == previous.InstanceId);
        if (deckIndex >= 0) State.DisasterDeck[deckIndex] = replacement;
        var removedIndex = State.RemovedDisasters.FindIndex(card => card.InstanceId == previous.InstanceId);
        if (removedIndex >= 0) State.RemovedDisasters[removedIndex] = replacement;
        var revealedIndex = State.RevealedDisasters.FindIndex(card => card.InstanceId == previous.InstanceId);
        if (revealedIndex >= 0) State.RevealedDisasters[revealedIndex] = replacement;
        if (State.ActiveDisaster?.InstanceId == previous.InstanceId)
            State.ActiveDisaster = replacement;

        RecalculateContinuousTroops();
        AddEvent("gm", null,
            $"[GM] 将自定天灾第 {slot + 1} 槽由〈{previous.Name}〉更换为〈{replacement.Name}〉；当前持续效果已刷新且不重复触发翻开效果",
            previous, replacement);
        return CommandResult.Ok();
    }

    private CommandResult GmSetPhase(L12GmCommand command)
    {
        if (!Enum.TryParse<L12Phase>(command.Phase, ignoreCase: true, out var phase)
            || phase is L12Phase.Initiative or L12Phase.DisasterPreparation or L12Phase.Mulligan or L12Phase.GameOver)
            return CommandResult.Reject("只可跳转至触发天灾、重置、抽牌、士气、主要或结束阶段");
        foreach (var player in State.Players)
        {
            foreach (var resolving in player.Resolving.ToArray())
            {
                player.Resolving.Remove(resolving);
                ResetCardAfterLeavingField(resolving);
                player.Graveyard.Add(resolving);
            }
        }
        State.PendingPrompts.Clear();
        State.PendingActivations.Clear();
        State.PendingTriggerBatches.Clear();
        State.PendingTriggerStackCandidates.Clear();
        State.PendingDefense = null;
        State.SuspendedCombatContexts.Clear();
        State.EffectStack.Clear();
        State.DeferredEffectStack.Clear();
        State.ResponseWindow = null;
        State.IsResolvingStack = false;
        State.ActivePlayer = command.TargetPlayer;
        State.Phase = phase;
        AddEvent("gm", command.TargetPlayer, $"[GM] 将玩家{command.TargetPlayer + 1}设为回合玩家并跳转至{GmPhaseLabel(phase)}");
        return CommandResult.Ok();
    }

    private CommandResult GmResetCardEffects(L12GmCommand command)
    {
        var card = FindPublicCard(command.CardInstanceId, out var controllerIndex);
        if (card is null || controllerIndex != command.TargetPlayer)
            return CommandResult.Reject("目标卡牌不在该玩家场上");
        if (State.PendingPrompts.Any(prompt => prompt.Data.Values.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase))
            || State.EffectStack.Any(item => item.SourceInstanceId == card.InstanceId))
            return CommandResult.Reject("该卡牌仍有待处理的选择或效果，请完成结算后再重置");

        var sourceIds = card.AttachedCards.Prepend(card).Select(source => source.InstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resetKeys = State.Players[controllerIndex].UsedAbilities
            .Where(key => sourceIds.Any(sourceId => IsCardOncePerTurnUsageKey(key, sourceId)))
            .ToArray();
        foreach (var key in resetKeys)
            State.Players[controllerIndex].UsedAbilities.Remove(key);

        AddEvent("gm", controllerIndex,
            $"[GM] 重置〈{card.Name}〉的回合1次效果限制（{resetKeys.Length}项）", card);
        return CommandResult.Ok();
    }

    private static bool IsCardOncePerTurnUsageKey(string key, string sourceInstanceId)
    {
        var prefixes = new[]
        {
            $"active:{sourceInstanceId}:",
            $"trigger:faith-zealot:{sourceInstanceId}",
            $"trigger:limu-morale:{sourceInstanceId}:",
            $"gustav-ready:{sourceInstanceId}:",
            $"alice-ready:{sourceInstanceId}:",
            $"s2-achilles-lethal-replacement:{sourceInstanceId}:",
        };
        return prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private CommandResult GmAdvancePhase()
    {
        if (State.PendingDefense is not null || State.PendingPrompts.Count > 0 || State.EffectStack.Count > 0
            || State.PendingActivations.Count > 0 || State.PendingTriggerBatches.Count > 0
            || State.ResponseWindow is not null)
            return CommandResult.Reject("当前仍有待处理的选择、响应、触发或战斗，完成后才能进入下一阶段");

        var playerIndex = State.ActivePlayer;
        var player = State.Players[playerIndex];
        switch (State.Phase)
        {
            case L12Phase.Disaster:
                State.Phase = L12Phase.Reset;
                if (player.MasterId == "S02-06D1")
                {
                    AdvanceTrial(playerIndex, 1, CreateCard(player.MasterId, $"master-{playerIndex}"));
                    L12S2ZoneOps.GainRunes(player, 1);
                    AddEvent("runes", playerIndex, "彼界 阿瓦隆在回合开始时获得1符文");
                }
                AddEvent("phase", playerIndex, "执行重置阶段");
                Untap(player);
                AddEvent("phase-detail", playerIndex, "将本回合玩家所有可以重置的士气、军团与圣物转为活跃");
                break;
            case L12Phase.Reset:
                State.Phase = L12Phase.Draw;
                AddEvent("phase", playerIndex, "执行抽牌阶段");
                if (State.Round == 1 && playerIndex == State.FirstPlayer)
                    AddEvent("draw-skipped", playerIndex, "先手玩家首回合不抽牌");
                else if (player.MasterId == "S01-03M1")
                {
                    Mill(player, 2, "瓦尔基里的抽牌阶段替代效果");
                    AddEvent("phase-detail", playerIndex, "瓦尔基里将抽牌阶段改为弃置牌库顶部2张牌");
                }
                else if (!Draw(player, 1))
                {
                    SetWinner(1 - playerIndex, "抽牌阶段牌库为空");
                }
                else AddEvent("phase-detail", playerIndex, "从牌库抽取 1 张牌");
                break;
            case L12Phase.Draw:
                State.Phase = L12Phase.Morale;
                AddEvent("phase", playerIndex, "执行士气阶段");
                var moraleAdded = AddMorale(player, State.Round == 1 && playerIndex == State.FirstPlayer ? 1 : 2);
                AddEvent("phase-detail", playerIndex, $"从士气牌库追加 {moraleAdded} 张士气");
                break;
            case L12Phase.Morale:
                State.Phase = L12Phase.Main;
                AddEvent("phase", playerIndex, "进入主要阶段");
                if (DisastersEnabled) BeginMainPhaseDisasterEffect();
                break;
            case L12Phase.Main:
                return EndTurn(playerIndex);
            case L12Phase.End:
                CompleteEndTurn(playerIndex);
                break;
            default:
                return CommandResult.Reject("当前阶段不能由 GM 进入下一阶段");
        }

        AddEvent("gm", playerIndex, $"[GM] 推进至{GmPhaseLabel(State.Phase)}");
        return CommandResult.Ok();
    }

    private bool TryCreateGmCard(L12GmCommand command, out L12CardInstance card, out string error)
    {
        card = null!;
        var cardId = (command.CardId ?? string.Empty).Trim();
        if (!_catalog.Cards.TryGetValue(cardId, out _))
        {
            error = "卡号不存在";
            return false;
        }
        var instanceId = $"gm-p{command.TargetPlayer}-r{State.Revision + 1}-e{State.EventSequence + 1}";
        card = CreateCard(cardId, instanceId);
        card.OwnerIndex = command.TargetPlayer;
        error = string.Empty;
        return true;
    }

    private static string GmZoneLabel(string destination) => destination switch
    {
        "library-top" => "牌库顶部",
        "library-bottom" => "牌库底部",
        "graveyard" => "墓地",
        "removed" => "移出区",
        _ => "手牌",
    };

    private static string GmPhaseLabel(L12Phase phase) => phase switch
    {
        L12Phase.Disaster => "触发天灾",
        L12Phase.Reset => "重置阶段",
        L12Phase.Draw => "抽牌阶段",
        L12Phase.Morale => "士气阶段",
        L12Phase.Main => "主要阶段",
        L12Phase.End => "结束阶段",
        _ => phase.ToString(),
    };
}
