namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void RollInitiative()
    {
        int first;
        int second;
        do
        {
            first = _random.Next(1, 7);
            second = _random.Next(1, 7);
        } while (first == second);
        State.InitiativeRolls = [first, second];
        State.DiceWinner = first > second ? 0 : 1;
    }

    private void BuildDisasterPool()
    {
        for (var number = 1; number <= 9; number++)
        {
            var id = $"S01-DS{number:00}";
            if (_catalog.Cards.ContainsKey(id))
                State.DisasterPool.Add(CreateCard(id, $"disaster-{number:00}"));
        }
    }

    private void PrepareLibrariesAndHands()
    {
        foreach (var player in State.Players)
        {
            Shuffle(player.Library);
            Draw(player, 6);
            if (player.MasterId is "S01-02D1" or "S01-03D1" or "S01-04D1") AddMorale(player, 2);
        }
    }

    private L12Prompt CreatePrompt(
        int playerIndex,
        string kind,
        string text,
        IEnumerable<string> choices,
        int min,
        int max,
        string continuation,
        string? stackItemId = null,
        bool isPrivate = true,
        Dictionary<string, string>? data = null)
    {
        var validChoices = choices.Distinct().ToList();
        data ??= [];
        if (validChoices.Count == 2 && validChoices.Contains("yes") && validChoices.Contains("no"))
            data.TryAdd("choiceMode", "instant");
        EnrichPromptCardData(playerIndex, validChoices, data);
        var prompt = new L12Prompt
        {
            PromptId = $"prompt-{++State.PromptSequence}",
            PlayerIndex = playerIndex,
            Kind = kind,
            Text = text,
            ValidChoices = validChoices,
            MinChoose = min,
            MaxChoose = max,
            Continuation = continuation,
            StackItemId = stackItemId,
            IsPrivate = isPrivate,
            Data = data,
        };
        State.PendingPrompts.Add(prompt);
        AddEvent("prompt", playerIndex, $"等待 {State.Players[playerIndex].Name}：{text}");
        return prompt;
    }

    private void EnrichPromptCardData(int viewer, IEnumerable<string> choices, Dictionary<string, string> data)
    {
        foreach (var id in choices)
        {
            var card = FindPromptCard(viewer, id);
            if (card is null) continue;
            AddPromptCardData(data, card);
        }
    }

    private static void AddPromptCardData(Dictionary<string, string> data, L12CardInstance card)
    {
        var id = card.InstanceId;
        data.TryAdd(id, card.Name);
        if (!string.IsNullOrWhiteSpace(card.ImageUrl)) data.TryAdd($"{id}:image", card.ImageUrl);
        if (!string.IsNullOrWhiteSpace(card.EffectText)) data.TryAdd($"{id}:effect", card.EffectText);
        data.TryAdd($"{id}:cardId", card.CardId);
        data.TryAdd($"{id}:cardType", card.CardType);
        data.TryAdd($"{id}:faction", card.Faction);
        data.TryAdd($"{id}:cost", card.CurrentCost.ToString());
        data.TryAdd($"{id}:troops", card.Troops.ToString());
        data.TryAdd($"{id}:baseTroops", card.BaseTroops.ToString());
        data.TryAdd($"{id}:disasterLevel", card.DisasterLevel.ToString());
    }

    private L12CardInstance? FindPromptCard(int viewer, string instanceId)
    {
        var mine = State.Players[viewer];
        var morale = mine.Morale.Concat(mine.MoraleDeck).FirstOrDefault(item => item.InstanceId == instanceId);
        if (morale is not null) return CreateCard(morale.CardId, morale.InstanceId);
        var card = mine.Hand.Concat(mine.Library).Concat(mine.Graveyard).Concat(mine.Removed)
            .Concat(mine.Resolving).Concat(mine.SpecialZones.GodPower).Concat(mine.SpecialZones.Trials).FirstOrDefault(item => item.InstanceId == instanceId)
            ?? mine.Field.SelectMany(row => row).FirstOrDefault(item => item?.InstanceId == instanceId)
            ?? (mine.Relic?.InstanceId == instanceId ? mine.Relic : null);
        if (card is not null) return card;

        var opponent = State.Players[1 - viewer];
        card = opponent.Field.SelectMany(row => row).FirstOrDefault(item => item?.InstanceId == instanceId)
            ?? opponent.Graveyard.Concat(opponent.Removed).Concat(opponent.Resolving)
                .FirstOrDefault(item => item.InstanceId == instanceId)
            ?? (opponent.Relic?.InstanceId == instanceId ? opponent.Relic : null);
        if (card is not null && !card.Hidden) return card;

        return State.DisasterPool.Concat(State.DisasterDeck).Concat(State.BannedDisasters)
            .Concat(State.RemovedDisasters).Concat(State.SelectedDisasters)
            .Append(State.ActiveDisaster).FirstOrDefault(item => item?.InstanceId == instanceId);
    }

    private CommandResult ResolvePrompt(int playerIndex, L12Command command)
    {
        if (string.IsNullOrWhiteSpace(command.PromptId)) return CommandResult.Reject("缺少 promptId");
        var prompt = State.PendingPrompts.FirstOrDefault(item => item.PromptId == command.PromptId);
        if (prompt is null) return CommandResult.Reject("选择请求不存在或已结算");
        if (prompt.PlayerIndex != playerIndex) return CommandResult.Reject("不能替其他玩家作出选择");
        var chosen = new List<string>();
        if (prompt.Data.GetValueOrDefault("placementMode") is "split-top-bottom" or "all-top-bottom" or "all-bottom")
        {
            chosen.AddRange(command.TopCardInstanceIds ?? []);
            chosen.AddRange(command.BottomCardInstanceIds ?? []);
            if (chosen.Count != chosen.Distinct().Count()) return CommandResult.Reject("同一张牌不能同时靠顶和靠底");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(command.Choice)) chosen.Add(command.Choice);
            if (command.CardInstanceIds is not null) chosen.AddRange(command.CardInstanceIds);
            chosen = chosen.Distinct().ToList();
        }
        if (chosen.Count < prompt.MinChoose || chosen.Count > prompt.MaxChoose)
            return CommandResult.Reject($"必须选择 {prompt.MinChoose} 至 {prompt.MaxChoose} 项");
        if (chosen.Any(item => !prompt.ValidChoices.Contains(item)))
            return CommandResult.Reject("包含无效选项");

        State.PendingPrompts.Remove(prompt);
        AddEvent("prompt-resolved", playerIndex, $"{State.Players[playerIndex].Name} 已完成选择");

        switch (prompt.Continuation)
        {
            case "setup-initiative":
                ResolveInitiativeChoice(playerIndex, chosen[0]);
                break;
            case "setup-ban":
                ResolveDisasterBan(playerIndex, chosen[0]);
                break;
            case "setup-public-confirm":
                if (!State.PendingPrompts.Any(item => item.Continuation == "setup-public-confirm"))
                {
                    State.DisasterPreparationStep++;
                    ContinueDisasterPreparation();
                }
                break;
            case "setup-first-pick":
            case "setup-second-pick":
                ResolveDisasterPick(playerIndex, chosen[0], prompt);
                break;
            case "disaster-trigger-confirm":
                if (!State.PendingPrompts.Any(item => item.Continuation == "disaster-trigger-confirm"))
                {
                    var disaster = State.ActiveDisaster;
                    if (disaster is not null)
                        PushEffect(State.ActivePlayer, disaster, "disaster", "天灾触发效果",
                            data: new Dictionary<string, string>
                            {
                                ["opening"] = prompt.Data.GetValueOrDefault("opening", "false")
                            });
                }
                break;
            case "s2-prayer-public-confirm":
                if (!State.PendingPrompts.Any(candidate => candidate.Continuation == "s2-prayer-public-confirm"
                    && candidate.StackItemId == prompt.StackItemId))
                {
                    var prayerItem = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == prompt.StackItemId);
                    if (prayerItem is not null) FinishStackItem(prayerItem);
                }
                break;
            case "stack-response":
                ResolveStackResponse(playerIndex, prompt, chosen[0]);
                break;
            case "stack-response-discard":
                ResolveAbsoluteDefenseDiscard(playerIndex, prompt, chosen[0]);
                break;
            case "stack-response-puppet-slot":
                ResolvePuppetResponseSlot(playerIndex, prompt, chosen[0]);
                break;
            case "card-effect":
            case "disaster-effect":
            case "active-ability":
                ContinueCardEffect(prompt, chosen, command);
                break;
            case "pending-activation":
                ResolvePendingActivation(prompt, chosen);
                break;
            case "play-cost-choice":
            {
                var result = ResolvePlayCostChoice(prompt, chosen[0]);
                if (!result.Accepted) return result;
                break;
            }
            case "play-morale-choice":
            {
                var result = ResolveTombGuardPlayPaymentChoice(prompt, chosen[0]);
                if (!result.Accepted) return result;
                break;
            }
            case "active-morale-choice":
            {
                var result = ResolveTombGuardActivePaymentChoice(prompt, chosen[0]);
                if (!result.Accepted) return result;
                break;
            }
            case "active-return-morale-choice":
            {
                var result = ResolveActiveReturnMoraleChoice(prompt, chosen, command.PreferNormalMoraleReturn);
                if (!result.Accepted) return result;
                break;
            }
            case "trigger-batch-order":
                ResolveTriggerBatchOrder(prompt, chosen);
                break;
            case "end-disaster-hand":
                ContinueEndDisasterHand(prompt, chosen);
                break;
            default:
                return CommandResult.Reject("未知选择续接点");
        }
        return CommandResult.Ok();
    }

    private CommandResult ResolvePlayCostChoice(L12Prompt prompt, string choice)
    {
        if (!int.TryParse(prompt.Data.GetValueOrDefault("row"), out var row)
            || !int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var slot))
            return CommandResult.Reject("登场位置数据无效");
        return PlayCard(prompt.PlayerIndex, new L12Command(
            "playCard",
            CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"),
            Row: row,
            Slot: slot,
            Choice: choice == "yes" ? "self-damage-cost" : "normal-cost"));
    }

    private CommandResult ResolveTombGuardPlayPaymentChoice(L12Prompt prompt, string choice)
    {
        var canPayWithoutGuards = bool.TryParse(prompt.Data.GetValueOrDefault("canPayWithoutGuards"), out var parsedCanPay) && parsedCanPay;
        if (choice == "no" && !canPayWithoutGuards) return CommandResult.Ok();
        int? row = int.TryParse(prompt.Data.GetValueOrDefault("row"), out var parsedRow) ? parsedRow : null;
        int? slot = int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var parsedSlot) ? parsedSlot : null;
        var baseChoice = prompt.Data.GetValueOrDefault("baseChoice", "normal-cost");
        return PlayCard(prompt.PlayerIndex, new L12Command(
            "playCard",
            CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"),
            Row: row,
            Slot: slot,
            Choice: $"{baseChoice}|{(choice == "yes" ? "tomb-guards" : "morale-only")}"));
    }

    private CommandResult ResolveTombGuardActivePaymentChoice(L12Prompt prompt, string choice)
    {
        var canPayWithoutGuards = bool.TryParse(prompt.Data.GetValueOrDefault("canPayWithoutGuards"), out var parsedCanPay) && parsedCanPay;
        if (choice == "no" && !canPayWithoutGuards) return CommandResult.Ok();
        var player = State.Players[prompt.PlayerIndex];
        var sourceId = prompt.Data.GetValueOrDefault("sourceId") ?? string.Empty;
        var source = FindOnField(player, sourceId, out _, out _)
            ?? (player.Relic?.InstanceId == sourceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == sourceId)
            ?? (prompt.Data.GetValueOrDefault("sourceCardId") == player.MasterId ? CreateCard(player.MasterId, sourceId) : null)
            ?? (sourceId == $"faction-{prompt.PlayerIndex}" ? CreateCard(prompt.Data.GetValueOrDefault("sourceCardId") ?? string.Empty, sourceId) : null);
        if (source is null) return CommandResult.Reject("主动效果来源已不在合法区域");
        return CommitActiveAbility(prompt.PlayerIndex, source, prompt.Data.GetValueOrDefault("ability") ?? string.Empty,
            prompt.Data.GetValueOrDefault("target"), useTombGuards: choice == "yes");
    }

    private void ResolveInitiativeChoice(int playerIndex, string choice)
    {
        State.FirstPlayer = choice == "first" ? playerIndex : 1 - playerIndex;
        State.ActivePlayer = State.FirstPlayer;
        State.Phase = L12Phase.DisasterPreparation;
        AddEvent("initiative-choice", playerIndex,
            $"{State.Players[playerIndex].Name} 选择{(choice == "first" ? "先攻" : "后攻")}；{State.Players[State.FirstPlayer].Name} 为先攻玩家");
        State.DisasterPreparationStep = 0;
        ContinueDisasterPreparation();
    }

    private void ContinueDisasterPreparation()
    {
        var first = State.FirstPlayer;
        var second = 1 - first;
        switch (State.DisasterPreparationStep)
        {
            case 0:
                PromptDisasterBan(first, "先攻玩家禁用第 1 张天灾");
                break;
            case 1:
                PromptDisasterBan(second, "后攻玩家禁用 1 张天灾");
                break;
            case 2:
                PromptDisasterBan(first, "先攻玩家禁用第 2 张天灾");
                break;
            case 3:
            {
                var available = State.DisasterPool.Where(card => !State.BannedDisasters.Contains(card)).ToList();
                var publicCard = available[_random.Next(available.Count)];
                State.DisasterPool.Remove(publicCard);
                State.SelectedDisasters.Add(publicCard);
                State.RevealedDisasters.Add(publicCard);
                AddEvent("disaster-public", null, $"随机公开天灾〈{publicCard.Name}〉", publicCard);
                var data = new Dictionary<string, string> { ["previewCardId"] = publicCard.InstanceId };
                AddPromptCardData(data, publicCard);
                CreatePrompt(first, "disaster-reveal", $"随机公开天灾〈{publicCard.Name}〉", [], 0, 0,
                    "setup-public-confirm", isPrivate: false, data: new Dictionary<string, string>(data));
                CreatePrompt(second, "disaster-reveal", $"随机公开天灾〈{publicCard.Name}〉", [], 0, 0,
                    "setup-public-confirm", isPrivate: false, data: new Dictionary<string, string>(data));
                break;
            }
            case 4:
                PromptDisasterPick(first, 3, "先攻玩家从 3 张候选天灾中选择 1 张", "setup-first-pick");
                break;
            case 5:
                PromptDisasterPick(second, 2, "后攻玩家从 2 张候选天灾中选择 1 张", "setup-second-pick");
                break;
            default:
                FinishDisasterPreparation();
                break;
        }
    }

    private void PromptDisasterBan(int playerIndex, string text)
    {
        var data = new Dictionary<string, string>();
        foreach (var card in State.DisasterPool)
        {
            data[card.InstanceId] = card.Name;
            if (!string.IsNullOrWhiteSpace(card.ImageUrl)) data[$"{card.InstanceId}:image"] = card.ImageUrl;
        }
        CreatePrompt(playerIndex, "disaster-ban", text,
            State.DisasterPool.Select(card => card.InstanceId), 1, 1, "setup-ban", isPrivate: false,
            data: data);
    }

    private void ResolveDisasterBan(int playerIndex, string instanceId)
    {
        var card = State.DisasterPool.First(card => card.InstanceId == instanceId);
        State.DisasterPool.Remove(card);
        State.BannedDisasters.Add(card);
        AddEvent("disaster-banned", playerIndex, $"{State.Players[playerIndex].Name} 禁用〈{card.Name}〉", card);
        State.DisasterPreparationStep++;
        ContinueDisasterPreparation();
    }

    private void PromptDisasterPick(int playerIndex, int count, string text, string continuation)
    {
        var candidates = State.DisasterPool.OrderBy(_ => _random.Next()).Take(count).ToArray();
        var data = new Dictionary<string, string>();
        foreach (var card in candidates)
        {
            data[card.InstanceId] = card.Name;
            if (!string.IsNullOrWhiteSpace(card.ImageUrl)) data[$"{card.InstanceId}:image"] = card.ImageUrl;
        }
        CreatePrompt(playerIndex, "disaster-pick", text, candidates.Select(card => card.InstanceId),
            1, 1, continuation, isPrivate: true,
            data: data);
    }

    private void ResolveDisasterPick(int playerIndex, string instanceId, L12Prompt prompt)
    {
        var card = State.DisasterPool.First(item => item.InstanceId == instanceId);
        // 本步骤中随机抽到的候选牌全部离开后续候选池；未选择的牌也不能被下一位玩家抽到。
        State.DisasterPool.RemoveAll(item => prompt.ValidChoices.Contains(item.InstanceId));
        State.SelectedDisasters.Add(card);
        State.ChosenDisasters.Add(card);
        State.ChosenDisasterOwners[card.InstanceId] = playerIndex;
        card.OwnerIndex = playerIndex;
        AddEvent("disaster-selected", playerIndex, $"{State.Players[playerIndex].Name} 选择〈{card.Name}〉", card);
        State.DisasterPreparationStep++;
        ContinueDisasterPreparation();
    }

    private void FinishDisasterPreparation()
    {
        Shuffle(State.SelectedDisasters);
        State.DisasterDeck.AddRange(State.SelectedDisasters);
        State.DisasterDeck.Add(CreateCard("S01-DS10", "disaster-final"));
        State.DisasterPool.Clear();
        PrepareLibrariesAndHands();
        State.DisasterValue = 0;
        State.Phase = L12Phase.Mulligan;
        AddEvent("disaster-deck-ready", null, "本局 4 张天灾牌库已组成，〈湮灭〉位于牌库底部");
        AddEvent("mulligan-start", null, "双方同时进行调度");
    }

    private L12StackItem PushEffect(int controller, L12CardInstance source, string trigger, string text,
        IEnumerable<string>? targets = null, Dictionary<string, string>? data = null)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = controller,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = trigger,
            Text = text,
        };
        if (targets is not null) item.Targets.AddRange(targets);
        if (data is not null)
            foreach (var pair in data) item.Data[pair.Key] = pair.Value;
        if (State.IsResolvingStack)
        {
            State.DeferredEffectStack.Add(item);
            AddEvent("stack-deferred", controller, $"〈{source.Name}〉的{text}将在当前堆叠关闭后开启新堆叠", source);
        }
        else
        {
            State.EffectStack.Add(item);
            AddEvent("stack-push", controller, $"〈{source.Name}〉的{text}进入堆叠", source);
            BeginResponseWindow(item);
        }
        return item;
    }

    private void BeginResponseWindow(L12StackItem item)
    {
        State.ResponseWindow = new L12ResponseWindow
        {
            PriorityPlayer = State.ActivePlayer,
            ConsecutivePasses = 0,
        };
        OfferResponse();
    }

    private void OfferResponse()
    {
        if (State.ResponseWindow is null || State.EffectStack.Count == 0) return;
        var playerIndex = State.ResponseWindow.PriorityPlayer;
        var top = State.EffectStack[^1];
        var player = State.Players[playerIndex];
        var choices = new List<string>();
        var responseCards = player.Field[1].Where(card => card is { CardType: "tactic" }
            && card.SetRound < State.Round && card.CannotRespondUntilRound < State.Round).Cast<L12CardInstance>().ToArray();
        if (State.TurnSerial < State.CounterTacticsDisabledUntilTurnSerial) responseCards = [];
        foreach (var card in responseCards)
        {
            if (card.CardId == "S01-0016" && top.Controller != playerIndex && top.Trigger != "authority-event"
                && player.Hand.Count > 0)
                choices.Add(card.InstanceId);
            if (card.CardId == "S01-0018" && top.Controller != playerIndex && top.Trigger == "enter")
                choices.Add(card.InstanceId);
            if (CanUseS1ReactionAtStack(card.CardId, playerIndex, top)) choices.Add(card.InstanceId);
            if (CanUseS2CounterAtStack(card.CardId, playerIndex, top)) choices.Add(card.InstanceId);
        }
        if (top.Trigger == "attack" && State.PendingDefense?.Target.Type == "legion" && top.Controller != playerIndex)
            choices.AddRange(player.Hand.Where(card => card.CardId == "S01-0002").Select(card => card.InstanceId));
        if (top.Trigger == "attack" && State.PendingDefense?.Target.Type == "master" && top.Controller != playerIndex
            && Enumerable.Range(0, 3).Any(slot => player.Field[0][slot] is null))
            choices.AddRange(player.Hand.Where(card => card.CardId == "S02-0005").Select(card => card.InstanceId));
        if (choices.Count == 0)
        {
            PassPriority(playerIndex);
            return;
        }
        choices.Add("pass");
        var responseData = choices.Where(choice => choice != "pass")
            .Select(id => responseCards.FirstOrDefault(card => card.InstanceId == id)
                ?? player.Hand.First(card => card.InstanceId == id))
            .ToDictionary(card => card.InstanceId, card => card.Name);
        responseData["choiceMode"] = "instant";
        CreatePrompt(playerIndex, "response", $"是否响应堆叠顶部：{top.SourceName}－{top.Text}", choices,
            1, 1, "stack-response", top.StackItemId, isPrivate: true, data: responseData);
    }

    private void ResolveStackResponse(int playerIndex, L12Prompt prompt, string choice)
    {
        if (choice == "pass")
        {
            PassPriority(playerIndex);
            return;
        }
        var player = State.Players[playerIndex];
        var response = FindOnField(player, choice, out _, out _)
            ?? player.Hand.FirstOrDefault(card => card.InstanceId == choice);
        if (response is null) { PassPriority(playerIndex); return; }
        if (response.CardId == "S01-0002")
        {
            CommitMercenaryResponse(playerIndex, response, prompt.StackItemId!);
            return;
        }
        if (response.CardId == "S02-0005")
        {
            var frontSlots = Enumerable.Range(0, 3)
                .Where(slot => player.Field[0][slot] is null)
                .Select(slot => $"0:{slot}")
                .ToArray();
            if (frontSlots.Length == 0) { PassPriority(playerIndex); return; }
            CreatePrompt(playerIndex, "slot", $"{response.Name}：预先选择休整登场的前排位置", frontSlots,
                1, 1, "stack-response-puppet-slot", prompt.StackItemId, isPrivate: true,
                data: new Dictionary<string, string>
                {
                    ["responseId"] = response.InstanceId,
                    ["choiceMode"] = "board-slot",
                });
            return;
        }
        if (response.CardId == "S01-0016")
        {
            var discards = player.Hand.Select(card => card.InstanceId).ToArray();
            CreatePrompt(playerIndex, "discard-cost", "弃置 1 张手牌作为〈绝对防御〉的费用", discards,
                1, 1, "stack-response-discard", prompt.StackItemId, isPrivate: true,
                data: new Dictionary<string, string> { ["responseId"] = response.InstanceId });
            return;
        }
        if (response.CardId is "S01-0019" or "S01-0020" or "S01-0120" or "S01-0224")
        {
            CommitS1ReactionResponse(playerIndex, response, prompt.StackItemId!);
            return;
        }
        if (response.CardId is "S02-0015" or "S02-0016" or "S02-0017" or "S02-0018")
        {
            CommitS2CounterResponse(playerIndex, response, prompt.StackItemId!);
            return;
        }
        CommitNegateResponse(playerIndex, response, prompt.StackItemId!);
    }

    private void ResolveAbsoluteDefenseDiscard(int playerIndex, L12Prompt prompt, string discardId)
    {
        var player = State.Players[playerIndex];
        var responseId = prompt.Data["responseId"];
        var response = FindOnField(player, responseId, out _, out _)!;
        var discard = player.Hand.First(card => card.InstanceId == discardId);
        player.Hand.Remove(discard);
        player.Graveyard.Add(discard);
        AddEvent("cost", playerIndex, $"{player.Name} 弃置 {discard.Name} 支付〈绝对防御〉费用", discard);
        CommitNegateResponse(playerIndex, response, prompt.StackItemId!);
    }

    private void ResolvePuppetResponseSlot(int playerIndex, L12Prompt prompt, string slotChoice)
    {
        var player = State.Players[playerIndex];
        var response = player.Hand.FirstOrDefault(card => card.InstanceId == prompt.Data.GetValueOrDefault("responseId")
            && card.CardId == "S02-0005");
        if (response is null || prompt.StackItemId is null)
        {
            PassPriority(playerIndex);
            return;
        }
        CommitPuppetResponse(playerIndex, response, prompt.StackItemId, slotChoice);
    }

    private void CommitPuppetResponse(int playerIndex, L12CardInstance response, string targetStackId, string slotChoice)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = playerIndex,
            SourceInstanceId = response.InstanceId,
            SourceCardId = response.CardId,
            SourceName = response.Name,
            Trigger = "response-retarget-master",
            Text = "从手牌休整登场于前排，并将本次进攻目标改为此军团",
        };
        item.Targets.Add(targetStackId);
        item.Data["slot"] = slotChoice;
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{State.Players[playerIndex].Name} 发动〈{response.Name}〉响应主宰进攻", response);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void CommitNegateResponse(int playerIndex, L12CardInstance response, string targetStackId)
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
            Trigger = "response-negate",
            Text = "无效堆叠中的效果",
        };
        item.Targets.Add(targetStackId);
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{player.Name} 打出〈{response.Name}〉响应", response);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void CommitMercenaryResponse(int playerIndex, L12CardInstance response, string targetStackId)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = playerIndex,
            SourceInstanceId = response.InstanceId,
            SourceCardId = response.CardId,
            SourceName = response.Name,
            Trigger = "response-block",
            Text = "弃置此军团，抵挡本次进攻",
        };
        item.Targets.Add(targetStackId);
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{playerIndex + 1} 号玩家发动〈佣兵部队〉抵挡进攻", response);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void PassPriority(int playerIndex)
    {
        var window = State.ResponseWindow;
        if (window is null) return;
        window.ConsecutivePasses++;
        AddEvent("priority-pass", playerIndex, $"{State.Players[playerIndex].Name} 不响应");
        if (window.ConsecutivePasses >= 2)
        {
            State.ResponseWindow = null;
            State.IsResolvingStack = true;
            ResolveTopStack();
            return;
        }
        window.PriorityPlayer = 1 - playerIndex;
        OfferResponse();
    }

    private void ResolveTopStack()
    {
        if (State.EffectStack.Count == 0)
        {
            AfterStackSettled();
            return;
        }
        var item = State.EffectStack[^1];
        if (item.Trigger == "response-negate")
        {
            var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == item.Targets.FirstOrDefault());
            if (target is not null) target.Negated = true;
            AddEvent("effect-negated", item.Controller,
                target is null ? "响应目标已经离开堆叠" : $"〈{target.SourceName}〉的{target.Text}被无效");
            FinishStackItem(item);
            return;
        }
        if (item.Trigger == "response-block")
        {
            var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == item.Targets.FirstOrDefault());
            if (target is not null) target.Negated = true;
            var player = State.Players[item.Controller];
            var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == item.SourceInstanceId);
            if (card is not null) { player.Hand.Remove(card); player.Graveyard.Add(card); }
            AddEvent("defense", item.Controller, "佣兵部队抵挡本次进攻", card is null ? [] : [card]);
            FinishStackItem(item);
            return;
        }
        if (item.Negated)
        {
            AddEvent("stack-resolve", item.Controller, $"〈{item.SourceName}〉的{item.Text}未产生效果");
            if (item.Trigger == "attack")
            {
                State.PendingDefense = null;
                State.Phase = L12Phase.Main;
                AddEvent("attack-ended", item.Controller, "本次进攻被抵挡");
            }
            FinishStackItem(item);
            return;
        }
        if (item.Trigger == "response-retarget-master")
        {
            ResolvePuppetResponse(item);
            return;
        }
        ResolveCardEffect(item);
    }

    private void ResolvePuppetResponse(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == item.SourceInstanceId
            && candidate.CardId == "S02-0005");
        var attackItem = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == item.Targets.FirstOrDefault()
            && candidate.Trigger == "attack" && !candidate.Negated);
        var slotParts = item.Data.GetValueOrDefault("slot")?.Split(':');
        var slot = -1;
        var validSlot = slotParts is { Length: 2 }
            && int.TryParse(slotParts[0], out var row) && row == 0
            && int.TryParse(slotParts[1], out slot) && slot is >= 0 and <= 2
            && player.Field[0][slot] is null;
        if (card is null || attackItem is null || State.PendingDefense?.Target.Type != "master" || !validSlot)
        {
            AddEvent("effect-failed", item.Controller, $"〈{item.SourceName}〉未能在预先选择的位置登场，进攻目标不变");
            FinishStackItem(item);
            return;
        }

        player.Hand.Remove(card);
        card.Tapped = true;
        card.SummonRound = State.Round;
        player.Field[0][slot] = card;
        ApplyDisasterLevelOnEntry(item.Controller, card, deferTriggerUntilStackSettles: true);
        State.PendingDefense.Target = new L12AttackTarget("legion", card.InstanceId);
        AddEvent("enter", item.Controller, $"{card.Name} 从手牌休整登场于前排，并成为本次进攻目标", card);
        if (HasImmediateEffect(card, "enter"))
            PushEffect(item.Controller, card, "enter", "【登场时】效果");
        FinishStackItem(item);
    }

    private void FinishStackItem(L12StackItem item)
    {
        if (item.Trigger == "authority-event" && FindAuthorityEvent(item) is { } authorityEvent)
            authorityEvent.Resolved = true;
        var completedSource = FindSource(item);
        var queueExorcistReturn = !item.Negated
            && completedSource?.CardType == "tactic"
            && item.Trigger is "play" or "reaction";
        State.EffectStack.Remove(item);
        var owner = State.Players[item.Controller];
        var resolving = owner.Resolving.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId);
        if (resolving is not null && State.EffectStack.All(other => other.SourceInstanceId != resolving.InstanceId))
        {
            owner.Resolving.Remove(resolving);
            owner.Graveyard.Add(resolving);
        }
        if (queueExorcistReturn) QueueS2ExorcistReturns(item.Controller, completedSource!);
        if (State.EffectStack.Count > 0)
        {
            if (State.IsResolvingStack) ResolveTopStack();
            else BeginResponseWindow(State.EffectStack[^1]);
            return;
        }
        State.IsResolvingStack = false;
        if (State.PendingTriggerBatches.Count > 0)
        {
            AdvanceTriggerBatches();
            if (State.PendingPrompts.Any(prompt => prompt.Continuation == "trigger-batch-order")
                || State.EffectStack.Count > 0) return;
        }
        if (State.DeferredEffectStack.Count > 0)
        {
            State.EffectStack.AddRange(State.DeferredEffectStack);
            State.DeferredEffectStack.Clear();
            AddEvent("stack-open", null, "当前堆叠关闭，处理结算中产生的额外触发式效果");
            BeginResponseWindow(State.EffectStack[^1]);
            return;
        }
        AfterStackSettled();
    }

    private void AfterStackSettled()
    {
        State.ResponseWindow = null;
        if (State.CheckDisasterAfterStack)
        {
            State.CheckDisasterAfterStack = false;
            if (State.DisasterValue > 8)
            {
                BeginDisasterTrigger(opening: false);
                if (State.EffectStack.Count > 0 || State.PendingPrompts.Count > 0) return;
            }
        }
        if (State.ResumeTurnStartAfterStack)
        {
            State.ResumeTurnStartAfterStack = false;
            ContinueAutomaticTurnStart();
            return;
        }
        if (State.Phase == L12Phase.End && State.PendingPrompts.Count == 0)
        {
            CompleteEndTurn(State.ActivePlayer);
            return;
        }
        if (State.PendingDefense is not null) State.Phase = L12Phase.Defense;
    }
}
