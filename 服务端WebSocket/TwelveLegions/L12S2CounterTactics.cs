namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private bool CanUseS2CounterAtStack(string cardId, int playerIndex, L12StackItem top)
    {
        if (top.Trigger != "authority-event" || top.Controller == playerIndex) return false;
        var eventType = top.Data.GetValueOrDefault("eventType");
        return cardId switch
        {
            "S02-0015" => eventType == "defense" && top.Data.GetValueOrDefault("action") is "block" or "support",
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
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void ResolveS2CounterEffect(L12StackItem item)
    {
        var target = TargetAuthorityStackItem(item);
        if (target is null) { FinishStackItem(item); return; }
        var affectedPlayer = target.Controller;
        var affected = State.Players[affectedPlayer];

        switch (item.SourceCardId)
        {
            case "S02-0015":
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
            case "S02-0016":
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
            case "S02-0017":
                PromptS2OpponentHandChoice(item, target, "s2-plunder-return",
                    "粮草掠夺：选择对方1张手牌返回其牌库顶部，随后我方抽取1张牌");
                return;
            case "S02-0018":
                target.Negated = true;
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

    private L12StackItem? TargetAuthorityStackItem(L12StackItem response)
        => State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == response.Targets.FirstOrDefault()
            && candidate.Trigger == "authority-event");

    private void PromptS2OpponentHandChoice(L12StackItem item, L12StackItem target, string action, string text)
    {
        var opponent = State.Players[target.Controller];
        if (opponent.Hand.Count == 0) { FinishStackItem(item); return; }
        var data = new Dictionary<string, string> { ["action"] = action, ["targetStackId"] = target.StackItemId };
        foreach (var card in opponent.Hand) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "opponent-hand-card", text, opponent.Hand.Select(card => card.InstanceId),
            1, 1, "card-effect", item.StackItemId, isPrivate: true, data: data);
    }

    private bool ContinueS2CounterEffect(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var action = prompt.Data.GetValueOrDefault("action");
        if (action is null || !action.StartsWith("s2-", StringComparison.Ordinal)) return false;
        var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == prompt.Data.GetValueOrDefault("targetStackId"));

        switch (action)
        {
            case "s2-landlord-extra-discard":
                if (chosen[0] == "decline")
                {
                    if (target is not null) target.Data["invalid"] = "true";
                }
                else MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0]);
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
                if (target is not null) MoveHandToGrave(State.Players[target.Controller], chosen[0]);
                FinishStackItem(item);
                return true;
            case "s2-plunder-return":
                if (target is not null)
                {
                    var opponent = State.Players[target.Controller];
                    var selected = opponent.Hand.FirstOrDefault(card => card.InstanceId == chosen[0]);
                    if (selected is not null)
                    {
                        opponent.Hand.Remove(selected);
                        opponent.Library.Insert(0, selected);
                        AddEvent("return", item.Controller, $"〈粮草掠夺〉将{selected.Name}返回所有者牌库顶部", selected);
                    }
                }
                if (!Draw(State.Players[item.Controller], 1)) SetWinner(1 - item.Controller, "〈粮草掠夺〉抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "s2-poison-discard":
                MoveHandToGrave(State.Players[prompt.PlayerIndex], chosen[0]);
                FinishStackItem(item);
                return true;
            default:
                return false;
        }
    }
}
