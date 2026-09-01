namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void ContinueCardEffect(L12Prompt prompt, List<string> chosen, L12Command command)
    {
        if (prompt.Continuation == "active-ability" && prompt.StackItemId is null)
        {
            CommitPromptedActiveAbility(prompt, chosen);
            return;
        }
        var item = State.EffectStack.FirstOrDefault(stack => stack.StackItemId == prompt.StackItemId);
        if (item is null) return;
        var action = prompt.Data.GetValueOrDefault("action") ?? string.Empty;
        var source = FindSource(item);
        var player = State.Players[item.Controller];
        if (action.StartsWith("s2-", StringComparison.Ordinal))
        {
            if (ContinueS2CounterEffect(item, prompt, chosen)) return;
            if (TryContinueS2Faction(item, prompt, chosen, command)) return;
            ContinueS2UniversalEffect(item, prompt, chosen);
            return;
        }
        switch (action)
        {
            case "effect-morale-payment": ContinueEffectMoralePayment(item, prompt, chosen); break;
            case "effect-morale-return": ContinueEffectMoraleReturn(item, prompt, chosen); break;
            case "lubu-kill":
                if (chosen[0] != "skip") BeginEffectMoraleReturn(item, 2, "lubu-kill", new() { ["target"] = chosen[0] });
                else FinishStackItem(item); break;
            case "wuzetian-lock":
                if (chosen.Count > 0) BeginEffectMoraleReturn(item, 1, "wuzetian-lock", new() { ["targets"] = string.Join('|', chosen) });
                else FinishStackItem(item); break;
            case "mulan-charge":
                if (chosen[0] == "yes" && source is not null) BeginEffectMoraleReturn(item, 1, "mulan-charge");
                else FinishStackItem(item); break;
            case "kusanagi-enter-kill":
                KillTarget(item, chosen[0], $"被{item.SourceName}击杀");
                FinishStackItem(item); break;
            case "peace-talk":
                if (chosen[0] == "agree")
                {
                    for (var index = 0; index < 2; index++)
                        if (!Draw(State.Players[index], 1, logEffectDraw: false)) SetWinner(1 - index, "议和谈判抽牌时牌库为空");
                    AddEvent("effect", prompt.PlayerIndex, "双方同意议和谈判并各抽取 1 张牌", source is null ? [] : [source]);
                }
                else AddEvent("effect", prompt.PlayerIndex, "对方不同意议和谈判，双方不额外抽牌");
                FinishStackItem(item); break;
            case "inaihime-buff":
            {
                var target = FindOnField(player, chosen[0], out var row, out _);
                if (target is not null && row == 0 && IsFieldLegion(target)
                    && target.InstanceId != item.SourceInstanceId && target.Troops <= 5000
                    && L12StructuredCardRules.HasFaction(player, target, "gaotianyuan"))
                    AddTimedModifier(target, 1000, 0, State.TurnSerial, item.SourceName);
                FinishStackItem(item); break;
            }
            case "lijing-choice": ContinueLiJingChoice(item, chosen[0]); break;
            case "lijing-slot": CompleteLiJingRecruit(item, chosen[0]); break;
            case "reorder-order":
                if (command.TopCardInstanceIds is not null || command.BottomCardInstanceIds is not null)
                    CompleteTopDeckReorderDirect(item, command.TopCardInstanceIds ?? [], command.BottomCardInstanceIds ?? []);
                else ContinueReorderOrder(item, prompt, chosen);
                break;
            case "reorder-count": CompleteTopDeckReorder(item, prompt, chosen[0]); break;
            case "oiran-pick": ContinueOiranPick(item, chosen[0]); break;
            case "oiran-order":
                CompleteOiranOrder(item, command.BottomCardInstanceIds ?? chosen);
                break;
            case "yangjian-return-card":
                if (command.Destination is "top" or "bottom") CompleteYangJianReturn(item, chosen[0], command.Destination);
                else ContinueYangJianReturn(item, chosen[0]);
                break;
            case "yangjian-return-place": CompleteYangJianReturn(item, chosen[0]); break;
            case "liubei-search": CompleteLiuBeiSearch(item, chosen[0]); break;
            case "shanhe-search-pick": CompleteShanheSearch(item, chosen[0]); break;
            case "disaster-return-field": CompleteDisasterReturnField(item, prompt, chosen[0]); break;
            case "disaster-grave-bottom": ContinueDisasterGraveBottom(item, prompt, chosen); break;
            case "disaster-discard": CompleteDisasterDiscard(item, prompt, chosen); break;
            case "disaster-keep-field": CompleteDisasterKeepField(item, prompt, chosen); break;
            case "disaster-apocalypse-hand-order": ContinueApocalypseHandOrder(item, prompt, chosen); break;
            case "disaster-s2-fog-discard": CompleteS2FogDiscard(item, prompt, chosen); break;
            case "disaster-s2-pride-mode": ContinueS2PrideMode(item, prompt, chosen[0]); break;
            case "disaster-s2-pride-discard": CompleteS2PrideDiscard(item, prompt, chosen); break;
            case "disaster-main-choice":
                player.UsedAbilities.Add(chosen[0] == "free-tactic" ? "ds01-free-tactic" : "ds01-back-master");
                FinishStackItem(item); break;
            default:
                if (!TryContinueS1Extended(item, prompt, chosen, command)
                    && !TryContinueS2Faction(item, prompt, chosen, command)) FinishStackItem(item);
                break;
        }
    }

    private void KillTarget(L12StackItem sourceItem, string instanceId, string reason)
    {
        for (var owner = 0; owner < 2; owner++)
        {
            var target = FindOnField(State.Players[owner], instanceId, out _, out _);
            if (target is null) continue;
            var source = FindSource(sourceItem);
            if (source is null) return;
            var killEvent = new L12KillSourceEvent(
                $"effect-kill:{sourceItem.StackItemId}:{source.InstanceId}",
                L12KillSourceKind.CardEffect,
                sourceItem.Controller,
                source.InstanceId,
                source.CardId,
                TriggersPrintedKillTiming: false,
                CausedBySourceCard: true,
                [target.InstanceId]);
            if (!RemoveFromField(State.Players[owner], target, true, reason))
            {
                AttachCardLethalKillSource(target, killEvent);
                return;
            }
            ResolveTypedKillSourceEvent(killEvent);
            return;
        }
    }

    private void BeginLiJingEffect(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        if (player.Library.Count == 0) { FinishStackItem(item); return; }
        var top = player.Library[0];
        AddEvent("reveal", item.Controller, $"李靖展示牌库顶部的〈{top.Name}〉", top);
        item.Data["revealed"] = top.InstanceId;
        var choices = new List<string> { "top", "bottom" };
        if (top.CardType == "legion" && top.Faction == "tianting" && top.CurrentCost <= 5
            && CanReturnMorale(player, 1) && player.Field.SelectMany(row => row).Any(card => card is null))
            choices.Add("recruit");
        var data = new Dictionary<string, string>
        {
            ["action"] = "lijing-choice",
            ["previewCardId"] = top.InstanceId
        };
        AddPromptCardData(data, top);
        CreatePrompt(item.Controller, "option", "将展示牌放回牌库顶部、底部，或返还 1 张士气使其活跃登场", choices, 1, 1,
            "card-effect", item.StackItemId, data: data);
    }

    private void ContinueLiJingChoice(L12StackItem item, string choice)
    {
        var player = State.Players[item.Controller];
        var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == item.Data["revealed"]);
        if (card is null) { FinishStackItem(item); return; }
        if (choice == "bottom")
        {
            player.Library.Remove(card); player.Library.Add(card); FinishStackItem(item); return;
        }
        if (choice == "top") { FinishStackItem(item); return; }
        BeginEffectMoraleReturn(item, 1, "lijing-recruit", new() { ["card"] = card.InstanceId });
    }

    private void CompleteLiJingRecruit(L12StackItem item, string slotChoice)
    {
        var player = State.Players[item.Controller];
        var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == item.Data["revealed"]);
        if (card is null) { FinishStackItem(item); return; }
        var (row, slot) = ParseSlot(slotChoice);
        player.Library.Remove(card); card.SummonRound = State.Round; card.Tapped = false; player.Field[row][slot] = card;
        AddEvent("put", item.Controller, $"李靖使 {card.Name} 活跃登场", card);
        ApplyDisasterLevelOnEntry(item.Controller, card, deferTriggerUntilStackSettles: true);
        QueueNonHandEntry(item.Controller, card, "library");
        FinishStackItem(item);
    }

    private void BeginLiuBeiEnter(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        if (!string.IsNullOrWhiteSpace(PublicTriggerDeclared(item, "entryCard")))
        {
            var battlefield = ParseEffectEntryBattlefieldChoice(PublicTriggerDeclared(item, "entryBattlefield"))
                ?? item.Controller;
            SummonFromHand(player, PublicTriggerDeclared(item, "entryCard"),
                PublicTriggerDeclared(item, "entrySlot"), tapped: false, battlefield);
            FinishStackItem(item);
            return;
        }
        FinishStackItem(item);
    }

    private IEnumerable<string> EmptySlots(L12PlayerState player)
    {
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (player.Field[row][slot] is null && !(State.ActiveDisaster?.CardId == "S01-DS03" && row == 1))
                    yield return $"{row}:{slot}";
    }

    private static (int Row, int Slot) ParseSlot(string choice)
    {
        var parts = choice.Split(':');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private void BeginTopDeckReorder(L12StackItem item, int count, string context)
    {
        var cards = State.Players[item.Controller].Library.Take(count).ToArray();
        if (cards.Length == 0) { FinishStackItem(item); return; }
        if (context is "observing-stars" or "prometheus")
        {
            BeginAllTopBottomReorder(item, context, cards.Select(card => card.InstanceId),
                $"排列牌库顶部 {cards.Length} 张牌，并将其全部放回牌库顶部或全部放回牌库底部");
            return;
        }
        item.Data["reorder-context"] = context;
        item.Data["reorder-cards"] = string.Join('|', cards.Select(card => card.InstanceId));
        CreatePrompt(item.Controller, "order", $"依次选择牌库顶部 {cards.Length} 张牌的排列顺序", cards.Select(card => card.InstanceId),
            cards.Length, cards.Length, "card-effect", item.StackItemId,
            data: new Dictionary<string, string>
            {
                ["action"] = "reorder-order",
                ["placementMode"] = "split-top-bottom",
            });
    }

    private void BeginAllTopBottomReorder(L12StackItem item, string context, IEnumerable<string> cardIds, string text)
    {
        var ids = cardIds.Distinct().ToArray();
        if (ids.Length == 0) { FinishStackItem(item); return; }
        item.Data["reorder-context"] = context;
        item.Data["reorder-cards"] = string.Join('|', ids);
        var data = new Dictionary<string, string>
        {
            ["action"] = "reorder-order",
            ["placementMode"] = "all-top-bottom",
            ["displayCardIds"] = string.Join('|', ids),
        };
        var player = State.Players[item.Controller];
        foreach (var id in ids)
        {
            var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == id);
            if (card is not null) AddPromptCardData(data, card);
        }
        CreatePrompt(item.Controller, "order", text, ids, ids.Length, ids.Length,
            "card-effect", item.StackItemId, data: data);
    }

    private void ContinueReorderOrder(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        item.Data["reorder-order"] = string.Join('|', chosen);
        CreatePrompt(item.Controller, "option", "选择排列中有多少张返回牌库顶部，其余依次返回牌库底部",
            Enumerable.Range(0, chosen.Count + 1).Select(value => value.ToString()), 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "reorder-count" });
    }

    private void CompleteTopDeckReorderDirect(L12StackItem item, List<string> topIds, List<string> bottomIds)
    {
        var player = State.Players[item.Controller];
        var expected = item.Data["reorder-cards"].Split('|');
        var ordered = topIds.Concat(bottomIds).ToArray();
        if (ordered.Length != expected.Length || ordered.Distinct().Count() != expected.Length
            || ordered.Any(id => !expected.Contains(id)))
        {
            AddEvent("effect-rejected", item.Controller, "牌库调整结果不完整");
            FinishStackItem(item);
            return;
        }
        var cards = expected.ToDictionary(id => id, id => player.Library.First(card => card.InstanceId == id));
        foreach (var card in cards.Values) player.Library.Remove(card);
        player.Library.InsertRange(0, topIds.Select(id => cards[id]));
        player.Library.AddRange(bottomIds.Select(id => cards[id]));
        AddEvent("reorder", item.Controller, $"将 {topIds.Count} 张牌放回牌库顶部、{bottomIds.Count} 张牌放回牌库底部");
        FinishStackItem(item);
    }

    private void CompleteTopDeckReorder(L12StackItem item, L12Prompt prompt, string topCountText)
    {
        var player = State.Players[item.Controller];
        var ids = item.Data["reorder-order"].Split('|');
        var cards = ids.Select(id => player.Library.First(card => card.InstanceId == id)).ToArray();
        foreach (var card in cards) player.Library.Remove(card);
        var topCount = int.Parse(topCountText);
        player.Library.InsertRange(0, cards.Take(topCount));
        player.Library.AddRange(cards.Skip(topCount));
        FinishStackItem(item);
    }

    private void BeginOiranGift(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var top = player.Library.Take(3).ToArray();
        item.Data["oiran-cards"] = string.Join('|', top.Select(card => card.InstanceId));
        var choices = top.Where(card => card.CardId != "S01-0419"
                && L12StructuredCardRules.HasFaction(player, card, "gaotianyuan"))
            .Select(card => card.InstanceId).ToList();
        choices.Add("skip");
        var data = new Dictionary<string, string>
        {
            ["action"] = "oiran-pick",
            ["choiceMode"] = "optional-add",
            ["displayCardIds"] = string.Join('|', top.Select(card => card.InstanceId))
        };
        foreach (var card in top) AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "card", "查看牌库顶部 3 张牌，选择 1 张符合条件的牌", choices, 1, 1,
            "card-effect", item.StackItemId, data: data);
    }

    private void ContinueOiranPick(L12StackItem item, string choice)
    {
        var player = State.Players[item.Controller];
        if (choice != "skip")
        {
            var card = player.Library.First(candidate => candidate.InstanceId == choice);
            player.Library.Remove(card);
            AddCardToHandByEffect(player, card, "library", $"花魁的馈赠将〈{card.Name}〉加入手牌");
            AddEvent("search", item.Controller, $"花魁的馈赠将〈{card.Name}〉加入手牌", card);
        }
        var remaining = item.Data["oiran-cards"].Split('|').Where(id => id != choice).ToList();
        if (remaining.Count <= 1)
        {
            CompleteOiranOrder(item, remaining);
            return;
        }
        var data = new Dictionary<string, string>
        {
            ["action"] = "oiran-order",
            ["placementMode"] = "all-bottom",
            ["displayCardIds"] = string.Join('|', remaining)
        };
        foreach (var id in remaining)
        {
            var card = player.Library.First(candidate => candidate.InstanceId == id);
            AddPromptCardData(data, card);
        }
        CreatePrompt(item.Controller, "order", "调整其余展示牌的顺序，然后将它们全部放回牌库底部。",
            remaining, remaining.Count, remaining.Count, "card-effect", item.StackItemId, data: data);
    }

    private void CompleteOiranOrder(L12StackItem item, List<string> order)
    {
        var player = State.Players[item.Controller];
        foreach (var id in order)
        {
            var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == id);
            if (card is null) continue;
            player.Library.Remove(card); player.Library.Add(card);
        }
        FinishStackItem(item);
    }

    private void ReturnFieldCardToLibraryTop(int playerIndex, L12CardInstance card)
    {
        var player = State.Players[playerIndex];
        MoveFieldCardToZone(player, card, "library-top", "返回牌库顶部");
    }
}
