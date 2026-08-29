namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private bool CanUseS2CounterAtStack(string cardId, int playerIndex, L12StackItem top)
    {
        if (cardId == "S02-0106")
            return top.Controller != playerIndex
                && top.Trigger is not ("s2-reaction" or "disaster" or "authority-event");
        var timing = ResponseTimingContext(top);
        if (timing.Trigger != "authority-event" || timing.Controller == playerIndex) return false;
        var eventType = timing.Data.GetValueOrDefault("eventType");
        return cardId switch
        {
            "S02-0015" => eventType == "defense" && timing.Data.GetValueOrDefault("action") is "block" or "support",
            "S02-0016" => eventType == "non-hand-entry",
            "S02-0017" => eventType == "effect-hand-add",
            "S02-0018" => eventType == "effect-ready",
            _ => false,
        };
    }

    private void CommitS2CounterResponse(int playerIndex, L12CardInstance response, string targetStackId)
    {
        var player = State.Players[playerIndex];
        if (FindOnField(player, response.InstanceId, out var row, out var slot) is not null) player.Field[row][slot] = null;
        response.Hidden = false;
        player.Resolving.Add(response);
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = playerIndex,
            SourceInstanceId = response.InstanceId,
            SourceCardId = response.CardId,
            SourceName = response.Name,
            Trigger = "s2-reaction",
            Text = "反击战术效果",
        };
        item.Targets.Add(targetStackId);
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{player.Name}发动〈{response.Name}〉", response);
        PublishEffectPresentation("effect-response", playerIndex, response, item.Trigger, item.Text, item.Data);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void ResolveS2CounterEffect(L12StackItem item)
    {
        if (item.SourceCardId == "S02-0106")
        {
            ResolveS2CosmosYin(item);
            return;
        }
        var target = TargetAuthorityStackItem(item);
        if (target is null) { FinishStackItem(item); return; }
        var affectedPlayer = target.Controller;
        var affected = State.Players[affectedPlayer];

        switch (AtomicFlowKey(item))
        {
            case "地主的胁迫":
            {
                var excluded = target.Data.GetValueOrDefault("blockIds", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var choices = affected.Hand.Where(card => !excluded.Contains(card.InstanceId))
                    .Select(card => card.InstanceId).ToList();
                choices.Add("decline");
                CreatePrompt(affectedPlayer, "discard-or-decline", "地主的胁迫：额外弃置1张手牌，否则本次抵挡/支援无效",
                    choices, 1, 1, "card-effect", item.StackItemId, isPrivate: true,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-landlord-extra-discard", ["targetStackId"] = target.StackItemId,
                        ["choiceMode"] = "instant", ["decline"] = "不弃置，本次抵挡/支援无效",
                    });
                return;
            }
            case "破败仪式":
            {
                var modes = new List<string>();
                if (affected.Hand.Count > 0) modes.Add("discard");
                if (FindOnField(affected, target.SourceInstanceId, out _, out _) is not null) modes.Add("suppress");
                if (modes.Count == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "option", "破败仪式：选择一项",
                    modes, 1, 1, "card-effect", item.StackItemId, isPrivate: true,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-ruin-mode", ["targetStackId"] = target.StackItemId,
                        ["choiceMode"] = "instant", ["discard"] = "弃置对方1张手牌",
                        ["suppress"] = "使该军团本回合登场效果无效，且兵力-3000",
                    });
                return;
            }
            case "粮草掠夺":
                PromptS2OpponentHandChoice(item, target, "s2-plunder-return",
                    "粮草掠夺：选择对方1张手牌返回其牌库顶部，随后我方抽取1张牌");
                return;
            case "毒药发作":
                NegateEffectReadyBatch(target);
                if (affected.Hand.Count == 0) { FinishStackItem(item); return; }
                CreatePrompt(affectedPlayer, "hand-card", "毒药发作：弃置1张手牌",
                    affected.Hand.Select(card => card.InstanceId), 1, 1, "card-effect", item.StackItemId, isPrivate: true,
                    data: new Dictionary<string, string> { ["action"] = "s2-poison-discard" });
                return;
            default:
                FinishStackItem(item);
                return;
        }
    }

    private void ResolveS2CosmosYin(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        if (player.Library.Count == 0)
        {
            SetWinner(1 - item.Controller, "〈乾坤·阴〉展示牌库顶部时牌库为空");
            FinishStackItem(item);
            return;
        }

        var revealed = player.Library[0];
        AddEvent("reveal", item.Controller, $"〈乾坤·阴〉展示牌库顶部的〈{revealed.Name}〉", revealed);
        if (revealed.CardType != "legion" || revealed.Faction != "tianting" || revealed.CurrentCost > 3)
        {
            player.Library.RemoveAt(0);
            player.Library.Add(revealed);
            AddEvent("return", item.Controller, $"〈{revealed.Name}〉置于牌库底部", revealed);
            FinishStackItem(item);
            return;
        }

        player.Library.RemoveAt(0);
        player.Graveyard.Add(revealed);
        AddEvent("discard", item.Controller, $"〈乾坤·阴〉从牌库弃置〈{revealed.Name}〉", revealed);
        var choices = PublicLegions(player).Select(card => card.InstanceId).ToList();
        if (choices.Count == 0)
        {
            FinishStackItem(item);
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-cosmos-yin-target",
            ["bonusTroops"] = revealed.Troops.ToString(),
            ["bonusCost"] = revealed.CurrentCost.ToString(),
        };
        foreach (var card in PublicLegions(player)) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "field-legion", "乾坤·阴：选择我方1张军团获得被弃置军团的费用与兵力",
            choices, 1, 1, "card-effect", item.StackItemId, isPrivate: true, data: data);
    }

    private L12StackItem? TargetAuthorityStackItem(L12StackItem response)
    {
        var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == response.Targets.FirstOrDefault());
        if (target is null) return null;
        var timing = ResponseTimingContext(target);
        return timing.Trigger == "authority-event" ? timing : null;
    }

    private void PromptS2OpponentHandChoice(L12StackItem item, L12StackItem target, string action, string text)
    {
        var opponent = State.Players[target.Controller];
        if (opponent.Hand.Count == 0) { FinishStackItem(item); return; }
        var data = new Dictionary<string, string> { ["action"] = action, ["targetStackId"] = target.StackItemId };
        CreateAnonymousHandChoicePrompt(item.Controller, opponent.Hand, "opponent-hand-card", text,
            1, 1, "card-effect", item.StackItemId, data);
    }

    private void NegateEffectReadyBatch(L12StackItem target)
    {
        target.Negated = true;
        var originStackId = target.Data.GetValueOrDefault("originStackId");
        if (string.IsNullOrWhiteSpace(originStackId)) return;
        foreach (var sibling in State.EffectStack.Concat(State.DeferredEffectStack))
        {
            if (sibling.Trigger == "authority-event"
                && sibling.Data.GetValueOrDefault("eventType") == "effect-ready"
                && sibling.Data.GetValueOrDefault("originStackId") == originStackId)
                sibling.Negated = true;
        }
    }

    private bool ContinueS2CounterEffect(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var action = prompt.Data.GetValueOrDefault("action");
        if (action is null || !action.StartsWith("s2-", StringComparison.Ordinal)) return false;
        var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == prompt.Data.GetValueOrDefault("targetStackId"));

        switch (action)
        {
            case "s2-cosmos-yin-target":
            {
                var targetLegion = FindOnField(State.Players[prompt.PlayerIndex], chosen[0], out _, out _);
                if (targetLegion is not null)
                {
                    _ = int.TryParse(prompt.Data.GetValueOrDefault("bonusTroops"), out var troops);
                    _ = int.TryParse(prompt.Data.GetValueOrDefault("bonusCost"), out var cost);
                    AddTimedModifier(targetLegion, troops, cost, State.TurnSerial, "乾坤·阴");
                }
                FinishStackItem(item);
                return true;
            }
            case "s2-landlord-extra-discard":
                if (chosen[0] == "decline")
                {
                    if (target is not null) target.Data["invalid"] = "true";
                }
                else MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: true);
                FinishStackItem(item);
                return true;
            case "s2-ruin-mode":
                if (target is null) { FinishStackItem(item); return true; }
                if (chosen[0] == "discard")
                {
                    PromptS2OpponentHandChoice(item, target, "s2-ruin-discard", "破败仪式：选择对方1张手牌弃置");
                    return true;
                }
                target.Data["suppressEnter"] = "true";
                var entered = FindOnField(State.Players[target.Controller], target.SourceInstanceId, out _, out _);
                if (entered is not null) AddTimedModifier(entered, -3000, 0, State.TurnSerial, "破败仪式");
                FinishStackItem(item);
                return true;
            case "s2-ruin-discard":
                if (target is not null)
                    MoveHandToGrave(State.Players[target.Controller], ResolveHiddenPromptChoice(prompt, chosen[0]), causedByEffect: true);
                FinishStackItem(item);
                return true;
            case "s2-plunder-return":
                if (target is not null)
                {
                    var opponent = State.Players[target.Controller];
                    var selectedId = ResolveHiddenPromptChoice(prompt, chosen[0]);
                    var selected = opponent.Hand.FirstOrDefault(card => card.InstanceId == selectedId);
                    if (selected is not null)
                    {
                        opponent.Hand.Remove(selected);
                        opponent.Library.Insert(0, selected);
                        AddEvent("return", item.Controller, "〈粮草掠夺〉将所选的1张对方手牌返回所有者牌库顶部");
                    }
                }
                if (!Draw(State.Players[item.Controller], 1)) SetWinner(1 - item.Controller, "〈粮草掠夺〉抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "s2-poison-discard":
                MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: true);
                FinishStackItem(item);
                return true;
            default:
                return false;
        }
    }
}
