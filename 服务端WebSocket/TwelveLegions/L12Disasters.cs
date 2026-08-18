namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void BeginDisasterTrigger(bool opening)
    {
        if (!DisastersEnabled) { SetDisasterValue(0); return; }
        if (State.ActiveDisaster?.CardId == "S01-DS10" && State.DisasterDeck.Count == 0)
        {
            State.DisasterValue = 0;
            AddEvent("disaster", State.ActivePlayer, "最终天灾〈堙灭〉持续生效，天灾值保持为 0", State.ActiveDisaster);
            return;
        }
        if (State.DisasterDeck.Count == 0)
        {
            State.DisasterValue = 0;
            return;
        }
        if (State.ActiveDisaster is not null)
        {
            State.RemovedDisasters.Add(State.ActiveDisaster);
            AddEvent("disaster-removed", null, $"旧天灾〈{State.ActiveDisaster.Name}〉移出游戏", State.ActiveDisaster);
        }
        var disaster = State.DisasterDeck[0];
        State.DisasterDeck.RemoveAt(0);
        State.ActiveDisaster = disaster;
        State.DisasterValue = 0;
        AddEvent("disaster", State.ActivePlayer, $"翻开天灾〈{disaster.Name}〉", disaster);
        var data = new Dictionary<string, string>
        {
            ["opening"] = opening ? "true" : "false",
            ["previewCardId"] = disaster.InstanceId
        };
        AddPromptCardData(data, disaster);
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
            CreatePrompt(playerIndex, "disaster-trigger", $"天灾〈{disaster.Name}〉已触发", [], 0, 0,
                "disaster-trigger-confirm", isPrivate: false, data: new Dictionary<string, string>(data));
    }

    private void ResolveDisasterEffect(L12StackItem item)
    {
        var disaster = FindSource(item);
        if (disaster is null) { FinishStackItem(item); return; }
        var subkind = item.Data.GetValueOrDefault("subkind");
        if (subkind == "main")
        {
            ResolveDarkMorningStarMain(item);
            return;
        }
        switch (disaster.CardId)
        {
            case "S01-DS01":
            case "S01-DS08":
            case "S01-DS10":
                AddEvent("disaster-active", null, $"〈{disaster.Name}〉的持续效果开始生效", disaster);
                FinishStackItem(item); return;
            case "S01-DS02":
                DamageMasterNonLethal(0, 1, "〈百鬼夜行〉", neutralSource: true);
                DamageMasterNonLethal(1, 1, "〈百鬼夜行〉", neutralSource: true);
                FinishStackItem(item); return;
            case "S01-DS03":
                for (var owner = 0; owner < 2; owner++)
                    for (var slot = 0; slot < 3; slot++)
                    {
                        var card = State.Players[owner].Field[1][slot];
                        if (card is not null && IsDisasterFieldCard(card)) RemoveFromField(State.Players[owner], card, true, "因腐秽大地置入墓地",
                            queueDeathTrigger: false, leaveKind: L12FieldLeaveKind.PutIntoGraveyard);
                    }
                FinishStackItem(item); return;
            case "S01-DS04": BeginThunderWrath(item); return;
            case "S01-DS05": BeginDragonDescent(item); return;
            case "S01-DS06": BeginDivineBalance(item); return;
            case "S01-DS07": BeginApocalypse(item); return;
            case "S01-DS09": ResolveRagnarok(item); return;
            case "S02-DS01":
                AddEvent("disaster-active", null, "〈天地异变〉的持续效果开始生效", disaster);
                FinishStackItem(item); return;
            case "S02-DS02": BeginS2FogDeadEnd(item); return;
            case "S02-DS03":
                for (var owner = 0; owner < 2; owner++)
                    foreach (var card in State.Players[owner].Field.SelectMany(row => row)
                        .Where(card => card is not null && IsDisasterFieldCard(card) && card.BaseTroops <= 2000).Cast<L12CardInstance>().ToArray())
                        RemoveFromField(State.Players[owner], card, true, "因〈无眠之夜〉弃置",
                            queueDeathTrigger: false, leaveKind: L12FieldLeaveKind.Discard);
                FinishStackItem(item); return;
            case "S02-DS04": ResolveS2StormChaos(item); return;
            case "S02-DS05":
                DamageMasterNonLethal(0, 1, "〈暴怒之罪〉", neutralSource: true);
                DamageMasterNonLethal(1, 1, "〈暴怒之罪〉", neutralSource: true);
                FinishStackItem(item); return;
            case "S02-DS06": BeginS2Pride(item); return;
            default:
                FinishStackItem(item); return;
        }
    }

    private static bool IsDisasterFieldCard(L12CardInstance card)
        => IsFieldLegion(card) || card.CardId == "S01-0415";

    private void BeginS2FogDeadEnd(L12StackItem item)
    {
        var prompted = 0;
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var player = State.Players[playerIndex];
            var excess = Math.Max(0, player.Hand.Count - 5);
            if (excess == 0) continue;
            CreatePrompt(playerIndex, "discard", $"〈迷雾绝境〉：选择{excess}张手牌弃置", player.Hand.Select(card => card.InstanceId), excess, excess,
                "disaster-effect", item.StackItemId, isPrivate: true,
                data: new Dictionary<string, string> { ["action"] = "disaster-s2-fog-discard", ["player"] = playerIndex.ToString(), ["simultaneous"] = "true" });
            prompted++;
        }
        if (prompted == 0) FinishStackItem(item);
    }

    private void CompleteS2FogDiscard(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var playerIndex = int.Parse(prompt.Data["player"]);
        item.Data[$"fog-discard:{playerIndex}"] = string.Join(',', chosen);
        if (State.PendingPrompts.Any(candidate => candidate.StackItemId == item.StackItemId
            && candidate.Data.GetValueOrDefault("action") == "disaster-s2-fog-discard")) return;
        for (var owner = 0; owner < 2; owner++)
        {
            var player = State.Players[owner];
            var ids = item.Data.GetValueOrDefault($"fog-discard:{owner}")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
            foreach (var id in ids) MoveHandToGrave(player, id, causedByEffect: true);
        }
        FinishStackItem(item);
    }

    private void ResolveS2StormChaos(L12StackItem item)
    {
        for (var owner = 0; owner < 2; owner++)
        {
            var player = State.Players[owner];
            for (var slot = 0; slot < 3; slot++)
            {
                var back = player.Field[1][slot];
                if (back is not null) MoveFieldCardToZone(player, back, "hand", "因〈风暴乱象〉返回手牌", queueLeaveTrigger: false);
            }
            for (var slot = 0; slot < 3; slot++)
            {
                var front = player.Field[0][slot];
                if (front is null || !IsFieldLegion(front)) continue;
                player.Field[0][slot] = null;
                player.Field[1][slot] = front;
                AddEvent("move", owner, $"〈风暴乱象〉使〈{front.Name}〉从前排位移至后排", front);
            }
        }
        FinishStackItem(item);
    }

    private void BeginS2Pride(L12StackItem item)
    {
        var counts = State.Players.Select(player => PublicLegions(player).Count()).ToArray();
        if (counts[0] == counts[1]) { FinishStackItem(item); return; }
        var owner = counts[0] > counts[1] ? 0 : 1;
        var difference = Math.Abs(counts[0] - counts[1]);
        var choices = new List<string> { "field" };
        if (State.Players[owner].Hand.Count >= difference) choices.Add("hand");
        CreatePrompt(owner, "option", $"〈傲慢之罪〉：选择弃置{difference}张战场军团，或弃置{difference}张手牌", choices, 1, 1,
            "disaster-effect", item.StackItemId, data: new Dictionary<string, string>
            {
                ["action"] = "disaster-s2-pride-mode", ["player"] = owner.ToString(), ["count"] = difference.ToString(),
                ["field"] = $"弃置{difference}张战场军团", ["hand"] = $"弃置{difference}张手牌",
            });
    }

    private void ContinueS2PrideMode(L12StackItem item, L12Prompt prompt, string choice)
    {
        var owner = int.Parse(prompt.Data["player"]);
        var count = int.Parse(prompt.Data["count"]);
        var player = State.Players[owner];
        var candidates = choice == "field"
            ? PublicLegions(player).Select(card => card.InstanceId)
            : player.Hand.Select(card => card.InstanceId);
        CreatePrompt(owner, choice == "field" ? "targets" : "discard", $"〈傲慢之罪〉：选择弃置{count}张{(choice == "field" ? "军团" : "手牌")}",
            candidates, count, count, "disaster-effect", item.StackItemId, isPrivate: choice != "field",
            data: new Dictionary<string, string> { ["action"] = "disaster-s2-pride-discard", ["player"] = owner.ToString(), ["zone"] = choice });
    }

    private void CompleteS2PrideDiscard(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var owner = int.Parse(prompt.Data["player"]);
        var player = State.Players[owner];
        if (prompt.Data["zone"] == "field")
        {
            foreach (var id in chosen)
            {
                var card = FindOnField(player, id, out _, out _);
                if (card is not null) RemoveFromField(player, card, true, "因〈傲慢之罪〉弃置",
                    queueDeathTrigger: false, leaveKind: L12FieldLeaveKind.Discard);
            }
        }
        else
        {
            foreach (var id in chosen)
            {
                var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == id);
                if (card is null) continue;
                player.Hand.Remove(card); player.Graveyard.Add(card);
                NotifyCardDiscarded(player, card, "hand", causedByEffect: true);
            }
        }
        FinishStackItem(item);
    }

    private void BeginMainPhaseDisasterEffect()
    {
        if (State.ActiveDisaster?.CardId != "S01-DS01") return;
        PushEffect(State.ActivePlayer, State.ActiveDisaster, "disaster", "主要阶段开始时效果",
            data: new Dictionary<string, string> { ["subkind"] = "main" });
    }

    private void ResolveDarkMorningStarMain(L12StackItem item)
    {
        var roll = _random.Next(1, 7);
        AddEvent("dice", State.ActivePlayer, $"〈黯陨晨星〉掷骰结果为 {roll}");
        var player = State.Players[State.ActivePlayer];
        if (roll % 2 == 0)
        {
            var morale = player.Morale.FirstOrDefault(card => !card.Tapped);
            if (morale is not null) morale.Tapped = true;
            FinishStackItem(item);
            return;
        }
        CreatePrompt(State.ActivePlayer, "option", "选择〈黯陨晨星〉本回合持续效果", ["free-tactic", "back-master"], 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "disaster-main-choice" });
    }

    private void BeginThunderWrath(L12StackItem item)
    {
        int first;
        int second;
        do { first = _random.Next(1, 7); second = _random.Next(1, 7); } while (first == second);
        var loser = first < second ? 0 : 1;
        AddEvent("dice", null, $"〈雷霆天怒〉掷骰：{State.Players[0].Name} {first}，{State.Players[1].Name} {second}");
        var choices = State.Players[loser].Field.SelectMany(row => row).Where(card => card is not null).Select(card => card!.InstanceId).ToArray();
        if (choices.Length == 0) { FinishStackItem(item); return; }
        CreatePrompt(loser, "target", "选择我方 1 张军团返回手牌", choices, 1, 1,
            "disaster-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "disaster-return-field" });
    }

    private void CompleteDisasterReturnField(L12StackItem item, string cardId)
    {
        for (var owner = 0; owner < 2; owner++)
        {
            var card = FindOnField(State.Players[owner], cardId, out var row, out var slot);
            if (card is null) continue;
            MoveFieldCardToZone(State.Players[owner], card, "hand", "因雷霆天怒返回手牌", queueLeaveTrigger: false); break;
        }
        FinishStackItem(item);
    }

    private void BeginDragonDescent(L12StackItem item)
    {
        var roll = _random.Next(1, 7);
        var column = roll <= 2 ? 0 : roll <= 4 ? 2 : 1;
        AddEvent("dice", State.ActivePlayer, $"〈魔龙降世〉掷骰结果为 {roll}，清除第 {column + 1} 列");
        for (var owner = 0; owner < 2; owner++)
            for (var row = 0; row < 2; row++)
            {
                var card = State.Players[owner].Field[row][column];
                if (card is not null) RemoveFromField(State.Players[owner], card, true, "因魔龙降世置入墓地",
                    queueDeathTrigger: false, leaveKind: L12FieldLeaveKind.PutIntoGraveyard);
            }
        BeginDisasterGraveBottom(item);
    }

    private void BeginDisasterGraveBottom(L12StackItem item)
    {
        var prompted = 0;
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var grave = State.Players[playerIndex].Graveyard.Where(CanEnterHandOrLibrary).ToArray();
            var count = Math.Min(4, grave.Length);
            if (count == 0) continue;
            CreatePrompt(playerIndex, "order", $"选择墓地 {count} 张牌，依选择顺序返回牌库底部", grave.Select(card => card.InstanceId),
                count, count, "disaster-effect", item.StackItemId,
                data: new Dictionary<string, string>
                {
                    ["action"] = "disaster-grave-bottom",
                    ["player"] = playerIndex.ToString(),
                    ["simultaneous"] = "true"
                });
            prompted++;
        }
        if (prompted == 0) FinishStackItem(item);
    }

    private void ContinueDisasterGraveBottom(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var playerIndex = int.Parse(prompt.Data["player"]);
        var player = State.Players[playerIndex];
        foreach (var id in chosen)
        {
            var card = player.Graveyard.First(candidate => candidate.InstanceId == id);
            MoveGraveToLibraryBottom(player, [card]);
        }
        if (!State.PendingPrompts.Any(candidate => candidate.StackItemId == item.StackItemId
            && candidate.Data.GetValueOrDefault("action") == "disaster-grave-bottom"))
            FinishStackItem(item);
    }

    private void BeginDivineBalance(L12StackItem item)
    {
        var lowest = State.Players.Min(player => player.Hp);
        var changed = new List<int>();
        for (var index = 0; index < 2; index++)
            if (State.Players[index].Hp > lowest)
            {
                State.Players[index].Hp = lowest;
                changed.Add(index);
            }
        if (changed.Count == 0)
        {
            Draw(State.Players[0], 1); Draw(State.Players[1], 1);
        }
        else foreach (var index in changed) Draw(State.Players[index], 2);
        var prompted = 0;
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var hand = State.Players[playerIndex].Hand;
            if (hand.Count == 0) continue;
            CreatePrompt(playerIndex, "discard", "弃置 1 张手牌，随后抽取 1 张牌", hand.Select(card => card.InstanceId), 1, 1,
                "disaster-effect", item.StackItemId, isPrivate: true,
                data: new Dictionary<string, string> { ["action"] = "disaster-discard", ["player"] = playerIndex.ToString(), ["simultaneous"] = "true" });
            prompted++;
        }
        if (prompted == 0) FinishStackItem(item);
    }

    private void CompleteDisasterDiscard(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var promptPlayer = int.Parse(prompt.Data["player"]);
        item.Data[$"balance-discard:{promptPlayer}"] = string.Join(',', chosen);
        if (State.PendingPrompts.Any(candidate => candidate.StackItemId == item.StackItemId
            && candidate.Data.GetValueOrDefault("action") == "disaster-discard")) return;
        for (var owner = 0; owner < 2; owner++)
        {
            var player = State.Players[owner];
            var cardId = item.Data.GetValueOrDefault($"balance-discard:{owner}");
            var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == cardId);
            if (card is null) continue;
            MoveHandToGrave(player, card.InstanceId, causedByEffect: true);
            Draw(player, 1);
            AddEvent("effect", owner, $"{player.Name} 因〈神之天平〉抽取 1 张牌", card);
        }
        FinishStackItem(item);
    }

    private void BeginApocalypse(L12StackItem item)
    {
        item.Data["apocalypse-player"] = "0";
        PromptApocalypseField(item, 0);
    }

    private void PromptApocalypseField(L12StackItem item, int playerIndex)
    {
        var cards = State.Players[playerIndex].Field.SelectMany(row => row).Where(card => card is not null && IsFieldLegion(card)).Select(card => card!.InstanceId).ToArray();
        var excess = Math.Max(0, cards.Length - 2);
        if (excess == 0)
        {
            if (playerIndex == 0) PromptApocalypseField(item, 1); else CompleteApocalypseHands(item);
            return;
        }
        CreatePrompt(playerIndex, "targets", $"选择 {excess} 张我方军团置入墓地，使战场军团不高于 2 张", cards, excess, excess,
            "disaster-effect", item.StackItemId,
            data: new Dictionary<string, string> { ["action"] = "disaster-keep-field", ["player"] = playerIndex.ToString() });
    }

    private void CompleteDisasterKeepField(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var playerIndex = int.Parse(prompt.Data["player"]);
        foreach (var id in chosen)
            for (var owner = 0; owner < 2; owner++)
            {
                var target = FindOnField(State.Players[owner], id, out _, out _);
                if (target is not null) RemoveFromField(State.Players[owner], target, true, "因天启默示录置入墓地",
                    queueDeathTrigger: false, leaveKind: L12FieldLeaveKind.PutIntoGraveyard);
            }
        if (playerIndex == 0) PromptApocalypseField(item, 1); else CompleteApocalypseHands(item);
    }

    private void CompleteApocalypseHands(L12StackItem item)
    {
        for (var index = 0; index < 2; index++)
        {
            var player = State.Players[index];
            foreach (var card in player.Hand.ToArray()) { player.Hand.Remove(card); player.Library.Add(card); }
            if (!Draw(player, 4)) SetWinner(1 - index, "天启默示录抽牌时牌库为空");
        }
        FinishStackItem(item);
    }

    private void ResolveRagnarok(L12StackItem item)
    {
        for (var owner = 0; owner < 2; owner++)
            foreach (var card in State.Players[owner].Field.SelectMany(row => row).Where(card => card is not null && IsFieldLegion(card)).Cast<L12CardInstance>().ToArray())
                RemoveFromField(State.Players[owner], card, true, "因诸神黄昏置入墓地",
                    queueDeathTrigger: false, leaveKind: L12FieldLeaveKind.PutIntoGraveyard);
        var opening = item.Data.GetValueOrDefault("opening") == "true";
        if (opening)
        {
            Draw(State.Players[0], 2); Draw(State.Players[1], 2);
        }
        else
        {
            Draw(State.Players[1 - State.ActivePlayer], 2);
            State.ExtraTurnsForPlayer = State.ActivePlayer;
            State.Phase = L12Phase.End;
            AddEvent("extra-turn", State.ActivePlayer, $"{State.Players[State.ActivePlayer].Name} 将在本回合后追加 1 个回合");
        }
        FinishStackItem(item);
    }

    private void ResolveEndPhaseDisasterEffect(int playerIndex)
    {
        if (State.ActiveDisaster?.CardId != "S01-DS02") return;
        var player = State.Players[playerIndex];
        var excess = Math.Max(0, player.Hand.Count - 5);
        if (excess == 0) return;
        CreatePrompt(playerIndex, "order", $"〈百鬼夜行〉：选择 {excess} 张手牌依次返回牌库底部", player.Hand.Select(card => card.InstanceId),
            excess, excess, "end-disaster-hand", isPrivate: true);
    }

    private void ContinueEndDisasterHand(L12Prompt prompt, List<string> chosen)
    {
        var player = State.Players[prompt.PlayerIndex];
        foreach (var id in chosen)
        {
            var card = player.Hand.First(candidate => candidate.InstanceId == id);
            player.Hand.Remove(card); player.Library.Add(card);
        }
        CompleteEndTurn(prompt.PlayerIndex);
    }
}
