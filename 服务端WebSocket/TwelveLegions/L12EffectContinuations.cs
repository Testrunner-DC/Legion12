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
            case "lubu-kill":
                if (chosen[0] != "skip" && ReturnMorale(player, 2)) KillTarget(chosen[0], "被吕布击杀");
                FinishStackItem(item); break;
            case "wuzetian-lock":
                if (chosen.Count > 0 && ReturnMorale(player, 1))
                    foreach (var id in chosen)
                    {
                        var target = FindOnField(State.Players[1 - item.Controller], id, out _, out _);
                        if (target is not null) target.CannotUntapUntilRound = State.Round + 1;
                    }
                FinishStackItem(item); break;
            case "mulan-charge":
                if (chosen[0] == "yes" && source is not null && ReturnMorale(player, 1)) source.HasCharge = true;
                FinishStackItem(item); break;
            case "kusanagi-enter-kill":
            case "divine-punishment-kill":
            case "honda-kill-zero":
                KillTarget(chosen[0], $"被{item.SourceName}击杀");
                FinishStackItem(item); break;
            case "peace-talk":
                if (chosen[0] == "agree")
                {
                    for (var index = 0; index < 2; index++)
                        if (!Draw(State.Players[index], 1)) SetWinner(1 - index, "议和谈判抽牌时牌库为空");
                    AddEvent("effect", prompt.PlayerIndex, "双方同意议和谈判并各抽取 1 张牌", source is null ? [] : [source]);
                }
                else AddEvent("effect", prompt.PlayerIndex, "对方不同意议和谈判，双方不额外抽牌");
                FinishStackItem(item); break;
            case "march-buff":
            {
                var target = FindOnField(player, chosen[0], out _, out _);
                if (target is not null) target.Troops += 2000;
                item.Targets.Clear(); item.Targets.Add(chosen[0]);
                var enemyChoices = State.Players[1 - item.Controller].Field.SelectMany(row => row)
                    .Where(card => card is not null && card.Troops <= 6000 && !card.Hidden).Select(card => card!.InstanceId).ToList();
                if (CanReturnMorale(player, 2) && enemyChoices.Count > 0)
                {
                    enemyChoices.Add("skip");
                    CreatePrompt(item.Controller, "optional-target", "可返还 2 张士气：击杀对方 1 张兵力不高于 6000 的军团",
                        enemyChoices, 1, 1, "card-effect", item.StackItemId,
                        data: new Dictionary<string, string> { ["action"] = "march-kill" });
                }
                else FinishStackItem(item);
                break;
            }
            case "march-kill":
                if (chosen[0] != "skip" && ReturnMorale(player, 2)) KillTarget(chosen[0], "被神妙行军击杀");
                FinishStackItem(item); break;
            case "hanxin-attack":
                if (chosen[0] == "yes" && source is not null && ReturnMorale(player, 1))
                {
                    source.Troops += 1000;
                    source.HasStrongAttack = true;
                    if (State.PendingDefense is not null)
                    {
                        var pending = State.PendingDefense;
                        State.PendingDefense = new L12PendingDefense
                        {
                            AttackerPlayer = pending.AttackerPlayer, AttackerInstanceId = pending.AttackerInstanceId,
                            Target = pending.Target, IsRanged = pending.IsRanged, SureHit = pending.SureHit,
                            MasterDamage = pending.MasterDamage + 1,
                        };
                    }
                }
                FinishStackItem(item); break;
            case "guanyu-attack":
                if (chosen[0] == "yes" && source is not null && ReturnMorale(player, 1))
                {
                    source.Troops += 1000;
                    source.HasSureHit = true;
                    if (State.PendingDefense is not null)
                    {
                        var pending = State.PendingDefense;
                        State.PendingDefense = new L12PendingDefense
                        {
                            AttackerPlayer = pending.AttackerPlayer, AttackerInstanceId = pending.AttackerInstanceId,
                            Target = pending.Target, IsRanged = pending.IsRanged, SureHit = true,
                            MasterDamage = pending.MasterDamage,
                        };
                    }
                }
                FinishStackItem(item); break;
            case "inaihime-buff":
            {
                var target = FindOnField(player, chosen[0], out _, out _);
                if (target is not null) target.Troops += 1000;
                FinishStackItem(item); break;
            }
            case "hiromasa-disable":
            {
                var counter = FindOnField(State.Players[1 - item.Controller], chosen[0], out _, out _);
                if (counter is not null) counter.CannotRespondUntilRound = State.Round;
                FinishStackItem(item); break;
            }
            case "mulan-lock-morale":
            {
                var morale = State.Players[1 - item.Controller].Morale.FirstOrDefault(card => card.InstanceId == chosen[0]);
                if (morale is not null) morale.CannotUntapUntilRound = State.Round + 1;
                FinishStackItem(item); break;
            }
            case "lubu-ready":
                if (chosen[0] == "yes" && source is not null && ReturnMorale(player, 4))
                    ReadyCardByEffect(item.Controller, source, source, $"{source.Name}因效果转为活跃");
                FinishStackItem(item); break;
            case "katsura-return":
                if (chosen[0] == "yes" && source is not null)
                {
                    ReturnFieldCardToLibraryTop(item.Controller, source);
                    var active = player.Morale.Where(morale => morale.Tapped).Select(morale => morale.InstanceId).ToArray();
                    if (active.Length > 0)
                    {
                        CreatePrompt(item.Controller, "morale", "将我方最多 2 张休整士气转为活跃", active, 0, Math.Min(2, active.Length),
                            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "katsura-ready-morale" });
                        break;
                    }
                }
                FinishStackItem(item); break;
            case "katsura-ready-morale":
                foreach (var id in chosen)
                {
                    var morale = player.Morale.FirstOrDefault(card => card.InstanceId == id);
                    if (morale is not null && source is not null)
                        ReadyMoraleByEffect(item.Controller, source, morale, "士气因效果转为活跃");
                }
                FinishStackItem(item); break;
            case "kusanagi-return-top":
                if (chosen[0] == "yes" && source is not null)
                {
                    player.Graveyard.Remove(source);
                    player.Library.Insert(0, source);
                    AddEvent("return", item.Controller, "草雉剑返回牌库顶部", source);
                }
                FinishStackItem(item); break;
            case "lijing-choice": ContinueLiJingChoice(item, chosen[0]); break;
            case "lijing-slot": CompleteLiJingRecruit(item, chosen[0]); break;
            case "liubei-card": ContinueLiuBeiCard(item, chosen[0]); break;
            case "liubei-slot": CompleteLiuBeiEnter(item, chosen[0]); break;
            case "reorder-order":
                if (command.TopCardInstanceIds is not null || command.BottomCardInstanceIds is not null)
                    CompleteTopDeckReorderDirect(item, command.TopCardInstanceIds ?? [], command.BottomCardInstanceIds ?? []);
                else ContinueReorderOrder(item, prompt, chosen);
                break;
            case "reorder-count": CompleteTopDeckReorder(item, prompt, chosen[0]); break;
            case "observing-stars-morale":
                if (chosen[0] == "yes") AddMorale(player, 1);
                FinishStackItem(item); break;
            case "oiran-pick": ContinueOiranPick(item, chosen[0]); break;
            case "oiran-order":
                CompleteOiranOrder(item, command.BottomCardInstanceIds ?? chosen);
                break;
            case "oiran-morale":
                if (chosen[0] == "yes")
                {
                    var morale = player.Morale.FirstOrDefault(card => card.Tapped);
                    if (morale is not null && source is not null)
                        ReadyMoraleByEffect(item.Controller, source, morale, "士气因效果转为活跃");
                }
                FinishStackItem(item); break;
            case "yangjian-return-card":
                if (command.Destination is "top" or "bottom") CompleteYangJianReturn(item, chosen[0], command.Destination);
                else ContinueYangJianReturn(item, chosen[0]);
                break;
            case "yangjian-return-place": CompleteYangJianReturn(item, chosen[0]); break;
            case "liubei-search": CompleteLiuBeiSearch(item, chosen[0]); break;
            case "shanhe-search-pick": CompleteShanheSearch(item, chosen[0]); break;
            case "disaster-return-field": CompleteDisasterReturnField(item, chosen[0]); break;
            case "disaster-grave-bottom": ContinueDisasterGraveBottom(item, prompt, chosen); break;
            case "disaster-discard": CompleteDisasterDiscard(item, prompt, chosen); break;
            case "disaster-keep-field": CompleteDisasterKeepField(item, prompt, chosen); break;
            case "disaster-s2-fog-discard": CompleteS2FogDiscard(item, prompt, chosen); break;
            case "disaster-s2-pride-mode": ContinueS2PrideMode(item, prompt, chosen[0]); break;
            case "disaster-s2-pride-discard": CompleteS2PrideDiscard(item, prompt, chosen); break;
            case "disaster-main-choice":
                player.UsedAbilities.Add(chosen[0] == "free-tactic" ? "ds01-free-tactic" : "ds01-back-master");
                FinishStackItem(item); break;
            case "faction-move-card":
            {
                if (chosen[0] == "skip") { FinishStackItem(item); break; }
                var legion = FindOnField(player, chosen[0], out var row, out var slot);
                if (legion is null || !IsFieldLegion(legion) || legion.Tapped) { FinishStackItem(item); break; }
                var slots = new[] { (row - 1, slot), (row + 1, slot), (row, slot - 1), (row, slot + 1) }
                    .Where(position => position.Item1 is >= 0 and < 2 && position.Item2 is >= 0 and < 3
                        && !(State.ActiveDisaster?.CardId == "S01-DS03" && position.Item1 == 1)
                        && player.Field[position.Item1][position.Item2] is null)
                    .Select(position => $"{position.Item1}:{position.Item2}").ToArray();
                if (slots.Length == 0) { FinishStackItem(item); break; }
                item.Data["faction-move-source"] = legion.InstanceId;
                CreatePrompt(item.Controller, "slot", "选择该军团位移 1 格后的阵地", slots, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "faction-move-slot" });
                break;
            }
            case "faction-move-slot":
            {
                var legion = FindOnField(player, item.Data.GetValueOrDefault("faction-move-source"), out var row, out var slot);
                if (legion is not null && !legion.Tapped)
                {
                    var (targetRow, targetSlot) = ParseSlot(chosen[0]);
                    if (Math.Abs(row - targetRow) + Math.Abs(slot - targetSlot) == 1 && player.Field[targetRow][targetSlot] is null)
                    {
                        player.Field[row][slot] = null;
                        player.Field[targetRow][targetSlot] = legion;
                        legion.LastMovedTurn = State.TurnSerial;
                        AddEvent("faction-effect", item.Controller, $"高天原阵营效果使 {legion.Name} 位移 1 格", legion);
                        NotifyS2LegionMoved(item.Controller, legion, row, targetRow);
                    }
                }
                FinishStackItem(item); break;
            }
            case "loki-heal-return":
            {
                var cards = chosen.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id))
                    .Where(card => card is not null && CanEnterHandOrLibrary(card)).Cast<L12CardInstance>().ToArray();
                if (cards.Length == 2)
                {
                    MoveGraveToLibraryBottom(player, cards);
                    HealMaster(item.Controller, 1, "洛基主宰效果");
                }
                FinishStackItem(item); break;
            }
            default:
                if (!TryContinueS1Extended(item, prompt, chosen, command)
                    && !TryContinueS2Faction(item, prompt, chosen, command)) FinishStackItem(item);
                break;
        }
    }

    private void KillTarget(string instanceId, string reason)
    {
        for (var owner = 0; owner < 2; owner++)
        {
            var target = FindOnField(State.Players[owner], instanceId, out _, out _);
            if (target is null) continue;
            RemoveFromField(State.Players[owner], target, true, reason);
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
        if (!ReturnMorale(player, 1)) { FinishStackItem(item); return; }
        var slots = EmptySlots(player).ToArray();
        var data = new Dictionary<string, string>
        {
            ["action"] = "lijing-slot",
            ["previewCardId"] = card.InstanceId
        };
        AddPromptCardData(data, card);
        CreatePrompt(item.Controller, "slot", "请直接点击战场上的高亮空位，使展示的军团活跃登场", slots, 1, 1,
            "card-effect", item.StackItemId, data: data);
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
        var choices = player.Hand.Where(card => card.CardId is "S01-0106" or "S01-0107")
            .Select(card => card.InstanceId).ToList();
        if (!CanReturnMorale(player, 1) || choices.Count == 0 || !EmptySlots(player).Any()) { FinishStackItem(item); return; }
        choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "可返还 1 张士气：选择手牌中 1 张〈关羽〉或〈张飞〉活跃登场", choices, 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "liubei-card" });
    }

    private void ContinueLiuBeiCard(L12StackItem item, string choice)
    {
        if (choice == "skip" || !ReturnMorale(State.Players[item.Controller], 1)) { FinishStackItem(item); return; }
        item.Data["summon"] = choice;
        CreatePrompt(item.Controller, "slot", "选择活跃登场位置", EmptySlots(State.Players[item.Controller]), 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "liubei-slot" });
    }

    private void CompleteLiuBeiEnter(L12StackItem item, string slotChoice)
    {
        var player = State.Players[item.Controller];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == item.Data["summon"]);
        if (card is null) { FinishStackItem(item); return; }
        var (row, slot) = ParseSlot(slotChoice);
        player.Hand.Remove(card); card.SummonRound = State.Round; player.Field[row][slot] = card;
        AddEvent("put", item.Controller, $"刘备使 {card.Name} 活跃登场", card);
        ApplyDisasterLevelOnEntry(item.Controller, card, deferTriggerUntilStackSettles: true);
        if (HasImmediateEffect(card, "enter")) PushEffect(item.Controller, card, "enter", "【登场时】效果");
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
        if (item.Data["reorder-context"] == "observing-stars")
        {
            CreatePrompt(item.Controller, "optional", "是否从士气牌库追加 1 张活跃士气？", ["yes", "no"], 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "observing-stars-morale" });
            return;
        }
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
        if (item.Data["reorder-context"] == "observing-stars")
        {
            CreatePrompt(item.Controller, "optional", "是否从士气牌库追加 1 张活跃士气？", ["yes", "no"], 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "observing-stars-morale" });
            return;
        }
        FinishStackItem(item);
    }

    private void BeginOiranGift(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var top = player.Library.Take(3).ToArray();
        item.Data["oiran-cards"] = string.Join('|', top.Select(card => card.InstanceId));
        var choices = top.Where(card => card.CardId != "S01-0419" && card.Faction == "gaotianyuan")
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
            player.Library.Remove(card); AddCardToHandByEffect(player, card, "library", $"花魁的馈赠将{card.Name}加入手牌");
            AddEvent("search", item.Controller, $"花魁的馈赠将 {card.Name} 加入手牌", card);
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
        if (player.Morale.Any(card => card.Tapped))
            CreatePrompt(item.Controller, "optional", "是否将 1 张士气转为活跃？", ["yes", "no"], 1, 1,
                "card-effect", item.StackItemId, data: new Dictionary<string, string>
                {
                    ["action"] = "oiran-morale",
                    ["choiceMode"] = "instant"
                });
        else FinishStackItem(item);
    }

    private void ReturnFieldCardToLibraryTop(int playerIndex, L12CardInstance card)
    {
        var player = State.Players[playerIndex];
        MoveFieldCardToZone(player, card, "library-top", "返回牌库顶部");
    }
}
