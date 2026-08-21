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
            "destroyCard" => GmDestroyCard(command),
            "setCardState" => GmSetCardState(command),
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
            "setPhase" => GmSetPhase(command),
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
        if (!TryCreateGmCard(command, out var card, out var error)) return CommandResult.Reject(error);
        var player = State.Players[command.TargetPlayer];
        var destination = command.Destination switch
        {
            "library-top" => "library-top",
            "library-bottom" => "library-bottom",
            "graveyard" => "graveyard",
            "removed" => "removed",
            _ => "hand",
        };
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
        return CommandResult.Ok();
    }

    private CommandResult GmPlaceCard(L12GmCommand command)
    {
        if (!TryCreateGmCard(command, out var card, out var error)) return CommandResult.Reject(error);
        var player = State.Players[command.TargetPlayer];
        card.OwnerIndex = command.TargetPlayer;
        card.SummonRound = State.Round;

        if (card.CardType == "legion")
        {
            if (command.Row is null || command.Row < 0 || command.Row > 1
                || command.Slot is null || command.Slot < 0 || command.Slot > 2)
                return CommandResult.Reject("请选择 0–1 排、0–2 格的合法阵地");
            var targetRow = command.Row.GetValueOrDefault();
            var targetSlot = command.Slot.GetValueOrDefault();
            if (player.Field[targetRow][targetSlot] is not null)
                return CommandResult.Reject("目标阵地已有卡牌");
            player.Field[targetRow][targetSlot] = card;
        }
        else if (card.CardType == "artifact")
        {
            DiscardFieldArtifactsForRelicReplacement(player);
            if (player.Relic is not null) DiscardRelic(player, player.Relic);
            player.Relic = card;
        }
        else if (card.CardType == "tactic")
        {
            player.Resolving.Add(card);
        }
        else return CommandResult.Reject("该卡牌类型不能由 GM 打出到场上");

        ApplyDisasterLevelOnEntry(command.TargetPlayer, card, deferTriggerUntilStackSettles: false);
        AddEvent("gm", command.TargetPlayer, $"[GM] 使〈{card.Name}〉无视费用打出", card);
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
        }
        else if (player.Resolving.Remove(card))
        {
            ResetCardAfterLeavingField(card);
            player.Graveyard.Add(card);
        }
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

    private CommandResult GmSetCardState(L12GmCommand command)
    {
        var card = FindPublicCard(command.CardInstanceId, out var owner);
        if (card is null || owner != command.TargetPlayer) return CommandResult.Reject("目标卡牌不在该玩家场上");
        var tapped = command.Destination == "rested";
        card.Tapped = tapped;
        AddEvent("gm", command.TargetPlayer, $"[GM] 将〈{card.Name}〉转为{(tapped ? "休整" : "活跃")}", card);
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
        State.PendingDefense = null;
        State.EffectStack.Clear();
        State.DeferredEffectStack.Clear();
        State.ResponseWindow = null;
        State.IsResolvingStack = false;
        State.ActivePlayer = command.TargetPlayer;
        State.Phase = phase;
        AddEvent("gm", command.TargetPlayer, $"[GM] 将玩家{command.TargetPlayer + 1}设为回合玩家并跳转至{GmPhaseLabel(phase)}");
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
