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

    private void CommitS2CounterResponse(int playerIndex, L12CardInstance response, string targetStackId,
        IReadOnlyDictionary<string, string>? data = null)
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
        if (data is not null)
            foreach (var pair in data) item.Data[pair.Key] = pair.Value;
        var planId = $"response:{response.CardId}";
        if (L12CompositeEffectPlans.InitialResponseDeclaration(planId) is { } declaration)
        {
            foreach (var pair in CompositeFirstSegmentData(planId, declaration))
                item.Data[pair.Key] = pair.Value;
        }
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{player.Name}发动〈{response.Name}〉", response);
        PublishEffectPresentation("effect-response", playerIndex, response, item.Trigger, item.Text, item.Data);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = playerIndex };
        OfferResponse();
    }

    private void ResolveS2CounterEffect(L12StackItem item)
    {
        if (AtomicFlowKey(item) is "cosmos-yin-reveal" or "cosmos-yin-buff")
        {
            if (AtomicFlowKey(item) == "cosmos-yin-reveal") ResolveS2CosmosYin(item);
            else ResolveS2CosmosYinBuff(item);
            return;
        }
        if (item.SourceCardId == "S02-0106")
        {
            // Compatibility for a checkpoint created before the segmented response plan.
            ResolveS2CosmosYin(item);
            return;
        }
        var target = TargetAuthorityStackItem(item);
        var affectedPlayer = int.TryParse(item.Data.GetValueOrDefault("affectedPlayer"), out var declaredAffected)
            && declaredAffected is >= 0 and <= 1 ? declaredAffected : target?.Controller ?? -1;
        if (affectedPlayer < 0) { FinishStackItem(item); return; }
        var affected = State.Players[affectedPlayer];

        switch (AtomicFlowKey(item))
        {
            case "地主的胁迫":
            case "landlord-coercion":
            {
                if (target is null)
                {
                    RecordTargetSettlementFailure(item, item.Targets.FirstOrDefault(),
                        "原抵挡/支援权威事件已经离开堆叠");
                    FinishStackItem(item);
                    return;
                }
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
            case "ruined-ritual":
            {
                var mode = CompositeDeclared(item, "mode").SingleOrDefault();
                if (mode == "mode:discard")
                {
                    var selected = CompositeDeclared(item, "handTarget").SingleOrDefault();
                    if (selected is not null && affected.Hand.Any(card => card.InstanceId == selected))
                        MoveHandToGrave(affected, selected, causedByEffect: true);
                    FinishStackItem(item);
                    return;
                }
                if (mode == "mode:suppress" && target is not null)
                {
                    target.Data["suppressEnter"] = "true";
                    var entered = FindOnField(affected, target.SourceInstanceId, out _, out _);
                    if (entered is not null) AddTimedModifier(entered, -3000, 0, State.TurnSerial, "破败仪式");
                }
                FinishStackItem(item);
                return;
            }
            case "粮草掠夺":
            case "supply-plunder-return":
            {
                var selectedId = CompositeDeclared(item, "handTarget").SingleOrDefault();
                var selected = affected.Hand.FirstOrDefault(card => card.InstanceId == selectedId);
                if (selected is not null)
                {
                    affected.Hand.Remove(selected);
                    affected.Library.Insert(0, selected);
                    AddEvent("return", item.Controller, "〈粮草掠夺〉将盲选的1张对方手牌返回所有者牌库顶部");
                }
                FinishStackItem(item);
                return;
            }
            case "supply-plunder-draw":
                if (!Draw(State.Players[item.Controller], 1)) SetWinner(1 - item.Controller, "〈粮草掠夺〉抽牌时牌库为空");
                FinishStackItem(item);
                return;
            case "毒药发作":
            case "poison-negate":
                if (target is not null) NegateEffectReadyBatch(target);
                FinishStackItem(item);
                return;
            case "poison-discard":
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
            AddEvent("effect-failed", item.Controller, "〈乾坤·阴〉展示牌库顶部时牌库为空");
            FinishStackItem(item);
            return;
        }

        var revealed = player.Library[0];
        AddPresentationEvent("reveal", item.Controller,
            $"〈乾坤·阴〉展示牌库顶部的〈{revealed.Name}〉", "S02-0106", "top-card", revealed);
        if (revealed.CardType != "legion" || revealed.Faction != "tianting" || !L12StructuredCardRules.CurrentCostAtMost(revealed, 3))
        {
            player.Library.RemoveAt(0);
            player.Library.Add(revealed);
            DeclarePresentationBranch(item.Data, "cosmos-yin-reveal", "revealMode", "mode:return");
            item.Data.Remove("presentationSceneId");
            AddEvent("return", item.Controller, $"〈{revealed.Name}〉置于牌库底部", revealed);
            FinishStackItem(item);
            return;
        }

        player.Library.RemoveAt(0);
        player.Graveyard.Add(revealed);
        DeclarePresentationBranch(item.Data, "cosmos-yin-reveal", "revealMode", "mode:hit");
        item.Data.Remove("presentationSceneId");
        item.Data["bonusTroops"] = revealed.Troops.ToString();
        item.Data["bonusCost"] = revealed.CurrentCost.ToString();
        AddEvent("discard", item.Controller, $"〈乾坤·阴〉从牌库弃置〈{revealed.Name}〉", revealed);
        FinishStackItem(item);
    }

    private void ResolveS2CosmosYinBuff(L12StackItem item)
    {
        var targetId = CompositeDeclared(item, "buffTarget").SingleOrDefault();
        var target = FindOnField(State.Players[item.Controller], targetId, out _, out _);
        if (target is not null && IsFieldLegion(target))
        {
            _ = int.TryParse(item.Data.GetValueOrDefault("bonusTroops"), out var troops);
            _ = int.TryParse(item.Data.GetValueOrDefault("bonusCost"), out var cost);
            AddTimedModifier(target, troops, cost, State.TurnSerial, "乾坤·阴");
        }
        else RecordTargetSettlementFailure(item, targetId, "所选我方军团已离场或不再是军团");
        FinishStackItem(item);
    }

    private L12StackItem? TargetAuthorityStackItem(L12StackItem response)
    {
        var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == response.Targets.FirstOrDefault());
        if (target is null) return null;
        var timing = ResponseTimingContext(target);
        return timing.Trigger == "authority-event" ? timing : null;
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
                    DeclarePresentationBranch(item.Data, "landlord-coercion", "mode", "mode:invalidate");
                    item.Data.Remove("presentationSceneId");
                    if (target is not null) target.Data["invalid"] = "true";
                    else RecordTargetSettlementFailure(item, item.Targets.FirstOrDefault(),
                        "原抵挡/支援权威事件已经离开堆叠");
                }
                else
                {
                    DeclarePresentationBranch(item.Data, "landlord-coercion", "mode", "mode:discard");
                    item.Data.Remove("presentationSceneId");
                    MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0], causedByEffect: true);
                }
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
