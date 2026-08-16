namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private CommandResult BeginActiveAbility(int playerIndex, L12Command command)
    {
        var player = State.Players[playerIndex];
        var ability = command.Ability ?? string.Empty;
        var source = FindOnField(player, command.CardInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == command.CardInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
            ?? player.SpecialZones.Trials.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
            ?? player.SpecialZones.GodPower.FirstOrDefault(card => card.InstanceId == command.CardInstanceId);
        if (source is null && command.CardInstanceId is not null
            && (command.CardInstanceId == player.MasterId || command.CardInstanceId == $"master-{playerIndex}"))
            source = CreateCard(player.MasterId, $"master-{playerIndex}");
        if (source is null && command.CardInstanceId == $"faction-{playerIndex}")
        {
            var moraleId = player.SpecialZones.GodPower.FirstOrDefault()?.CardId
                ?? player.Morale.FirstOrDefault()?.CardId
                ?? player.MoraleDeck.FirstOrDefault()?.CardId;
            if (moraleId is not null) source = CreateCard(moraleId, $"faction-{playerIndex}");
        }
        if (source is null) return CommandResult.Reject("主动效果来源不在我方公开区域");
        if (ability != "discardHolyLock" && source.AttachedCards.Any(card => card.CardId == "S02-0013"))
            return CommandResult.Reject("该圣物被〈神圣伽锁〉叠放，当前无法使用");

        string[] choices;
        switch (ability)
        {
            case "frontBuff" when source.CardId == "S01-04M2":
                choices = player.Field.SelectMany(row => row).Where(card => card?.Faction == "gaotianyuan")
                    .Select(card => card!.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择我方 1 张【高天原】军团");
            case "kusanagi" when source.CardId == "S01-04M2":
                if (player.Relic?.CardId != "S01-0417") return CommandResult.Reject("圣物区没有〈草雉剑〉");
                choices = Enumerable.Range(0, 3).Where(slot => player.Field[0][slot] is null).Select(slot => $"0:{slot}").ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择〈草雉剑〉置入前排的位置");
            case "artifactSearch" when source.CardId == "S01-0117":
                choices = player.Hand.Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择弃置的 1 张手牌");
            case "kusanagiDebuff" when source.CardId == "S01-0417":
                choices = State.Players[1 - playerIndex].Field.SelectMany(row => row).Where(card => card is not null && !card.Hidden)
                    .Select(card => card!.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择对方 1 张军团，本回合费用 -1");
            case "kusanagiStrong" when source.CardId == "S01-0417":
                choices = player.Field.SelectMany(row => row).Where(card => card?.Faction == "gaotianyuan")
                    .Select(card => card!.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择我方 1 张【高天原】军团，本回合获得强攻");
            default:
                return TryBeginS2UniversalActiveAbility(playerIndex, source, ability)
                    ?? TryBeginS2FactionActiveAbility(playerIndex, source, ability)
                    ?? TryBeginS1ExtendedActiveAbility(playerIndex, source, ability)
                    ?? CommitActiveAbility(playerIndex, source, ability, command.CardInstanceIds?.FirstOrDefault());
        }
    }

    private CommandResult PromptActiveTarget(int playerIndex, L12CardInstance source, string ability, string[] choices, string text)
        => BeginPendingActivation(playerIndex, source, ability, choices, text);

    private void CommitPromptedActiveAbility(L12Prompt prompt, List<string> chosen)
    {
        var player = State.Players[prompt.PlayerIndex];
        var sourceId = prompt.Data["sourceId"];
        var source = FindOnField(player, sourceId, out _, out _)
            ?? (player.Relic?.InstanceId == sourceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == sourceId)
            ?? (prompt.Data["sourceCardId"] == player.MasterId ? CreateCard(player.MasterId, sourceId) : null);
        if (source is null) return;
        var result = CommitActiveAbility(prompt.PlayerIndex, source, prompt.Data["ability"], chosen[0]);
        if (!result.Accepted) AddEvent("ability-rejected", prompt.PlayerIndex, result.Error ?? "主动效果发动失败");
    }

    private CommandResult CommitActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, bool? useTombGuards = null,
        IEnumerable<string>? returnedMoraleIds = null)
    {
        var player = State.Players[playerIndex];
        var onceKey = $"active:{source.InstanceId}:{ability}";
        var faithFree = State.FreeMasterActivation is { } free
            && free.Controller == playerIndex
            && free.Ability.Equals(ability, StringComparison.OrdinalIgnoreCase)
            && source.CardId == player.MasterId;
        if (!faithFree && player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
        if (faithFree)
        {
            // 〈信仰狂热者〉裁定：本次发动忽略冒号前的所有费用，
            // 也不登记主宰效果的本回合使用次数；目标仍已由正常流程声明。
            State.FreeMasterActivation = null;
            var freeData = new Dictionary<string, string> { ["ability"] = ability, ["faith-free"] = "true" };
            if (!string.IsNullOrWhiteSpace(target)) freeData["target"] = target;
            PushEffect(playerIndex, source, "active", "〈信仰狂热者〉无视消耗发动的主宰效果", data: freeData);
            AddEvent("effect", playerIndex, $"〈信仰狂热者〉使主宰效果「{ability}」无视全部消耗发动", source);
            return CommandResult.Ok();
        }
        var returnMoraleCost = GetActiveAbilityReturnMoraleCost(playerIndex, source, ability, target);
        if (returnMoraleCost > 0 && returnedMoraleIds is null && RequiresMoraleIdentityChoice(player))
        {
            if (!CanReturnMorale(player, returnMoraleCost)) return CommandResult.Reject($"需要返还 {returnMoraleCost} 张士气");
            var normalMorale = player.Morale.Where(card => card.CardId != "S02-0010").Take(returnMoraleCost).Select(card => card.InstanceId).ToArray();
            if (player.PreferNormalMoraleReturns && normalMorale.Length == returnMoraleCost)
                returnedMoraleIds = normalMorale;
            else
            {
                var requiresLotusConfirmation = player.PreferNormalMoraleReturns;
                var returnChoices = requiresLotusConfirmation
                    ? player.Morale.Where(card => card.CardId == "S02-0010").Select(card => card.InstanceId)
                    : player.Morale.Select(card => card.InstanceId);
                var lotusNeeded = returnMoraleCost - normalMorale.Length;
            var returnChoiceData = new Dictionary<string, string>
            {
                ["sourceId"] = source.InstanceId,
                ["sourceCardId"] = source.CardId,
                ["ability"] = ability,
                ["target"] = target ?? string.Empty,
                ["returnCount"] = returnMoraleCost.ToString(),
                ["choiceMode"] = "selected-morale",
            };
                if (requiresLotusConfirmation)
                {
                    returnChoiceData["autoNormalMoraleIds"] = string.Join('|', normalMorale);
                    returnChoiceData["lotusConfirmation"] = "true";
                }
                CreatePrompt(playerIndex, "morale-return", requiresLotusConfirmation
                    ? "普通士气不足：请确认返还〈黑色莲花〉；它会置入墓地"
                    : "请选择要返还的士气；〈黑色莲花〉被返还时会置入墓地",
                returnChoices, requiresLotusConfirmation ? lotusNeeded : returnMoraleCost, requiresLotusConfirmation ? lotusNeeded : returnMoraleCost,
                "active-return-morale-choice", data: returnChoiceData);
                return CommandResult.Ok();
            }
        }
        bool ReturnSelectedMorale(int count) => ReturnMorale(player, count, returnedMoraleIds);
        var moraleCost = GetActiveAbilityMoraleCost(source, ability);
        if (moraleCost > 0 && useTombGuards is null
            && PublicLegions(player).Any(card => card.CardId == "S01-0212" && !card.Tapped && State.ActivePlayer == playerIndex))
        {
            var canPayWithoutGuards = ActiveMoraleCountWithoutTombGuards(player) >= moraleCost;
            CreatePrompt(playerIndex, "optional", $"{source.Name}：是否使用活跃的陵墓守卫支付费用？", ["yes", "no"], 1, 1,
                "active-morale-choice", data: new Dictionary<string, string>
                {
                    ["sourceId"] = source.InstanceId, ["sourceCardId"] = source.CardId, ["ability"] = ability,
                    ["target"] = target ?? string.Empty, ["canPayWithoutGuards"] = canPayWithoutGuards.ToString(),
                    ["choiceMode"] = "instant", ["yes"] = $"使用陵墓守卫优先支付（费用 {moraleCost}）",
                    ["no"] = canPayWithoutGuards ? $"仅使用士气支付（费用 {moraleCost}）" : "不使用并取消发动",
                });
            return CommandResult.Ok();
        }
        bool ConsumeMorale(int cost) => useTombGuards switch
        {
            true => TryConsumeMorale(player, cost, preferTombGuards: true, allowTombGuards: true),
            false => TryConsumeMorale(player, cost, preferTombGuards: false, allowTombGuards: false),
            _ => TryConsumeMorale(player, cost),
        };
        switch (ability)
        {
            case "drawCycle" when source.CardId == "S01-01M1":
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要消耗 1 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            case "nonLethal" when source.CardId == "S01-01M1":
                if (!ReturnSelectedMorale(4)) return CommandResult.Reject("需要返还 4 张士气");
                player.UsedAbilities.Add(onceKey); break;
            case "frontBuff" when source.CardId == "S01-04M2":
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要消耗 1 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            case "kusanagi" when source.CardId == "S01-04M2":
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要消耗 2 张活跃士气");
                break;
            case "addMorale" when source.CardId == "S01-0109":
                if (source.Tapped) return CommandResult.Reject("白起必须为活跃状态");
                source.Tapped = true; break;
            case "searchBrothers" when source.CardId == "S01-0105":
                if (source.Tapped) return CommandResult.Reject("刘备必须为活跃状态");
                if (!CanReturnMorale(player, 1)) return CommandResult.Reject("需要返还 1 张士气");
                source.Tapped = true; if (!ReturnSelectedMorale(1)) return CommandResult.Reject("需要返还 1 张士气"); break;
            case "artifactDraw" when source.CardId == "S01-0117":
                if (source.Tapped) return CommandResult.Reject("山河社稷图必须为活跃状态");
                if (!ReturnActiveMorale(player, 1)) return CommandResult.Reject("需要返还 1 张活跃士气");
                source.Tapped = true; break;
            case "artifactSearch" when source.CardId == "S01-0117":
            {
                if (source.Tapped) return CommandResult.Reject("山河社稷图必须为活跃状态");
                var discard = player.Hand.FirstOrDefault(card => card.InstanceId == target);
                if (discard is null) return CommandResult.Reject("弃置费用不在手牌中");
                source.Tapped = true; player.Hand.Remove(discard); player.Graveyard.Add(discard);
                AddEvent("cost", playerIndex, $"弃置 {discard.Name} 支付山河社稷图费用", discard); break;
            }
            case "kusanagiDebuff" or "kusanagiStrong" when source.CardId == "S01-0417":
                if (player.UsedAbilities.Contains($"active:{source.InstanceId}:choice")) return CommandResult.Reject("草雉剑的效果本回合已经发动");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要消耗 1 张活跃士气");
                player.UsedAbilities.Add($"active:{source.InstanceId}:choice"); break;
            case "discardHolyLock" when source.AttachedCards.Any(card => card.CardId == "S02-0013"):
                if (!ConsumeMorale(3)) return CommandResult.Reject("需要消耗3张活跃士气");
                break;
            case "factionAddActive" when source.CardId == "S01-01C1":
                if (player.FactionMoraleAdditionForbiddenUntilTurn >= State.TurnSerial)
                    return CommandResult.Reject("本回合无法因阵营效果追加士气");
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要消耗 2 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            case "factionZeroRecovery" when source.CardId == "S01-01C1":
                if (player.FactionMoraleAdditionForbiddenUntilTurn >= State.TurnSerial)
                    return CommandResult.Reject("本回合无法因阵营效果追加士气");
                if (player.Morale.Count != 0) return CommandResult.Reject("只有我方士气为 0 张时才能发动");
                player.UsedAbilities.Add(onceKey); break;
            case "factionDrawMove" when source.CardId == "S01-04C1":
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要消耗 2 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            default:
                return TryCommitS2UniversalActiveAbility(playerIndex, source, ability, target, onceKey, returnedMoraleIds)
                    ?? TryCommitS2FactionActiveAbility(playerIndex, source, ability, target, onceKey, useTombGuards, returnedMoraleIds)
                    ?? TryCommitS1FactionActiveAbility(playerIndex, source, ability, target, onceKey, useTombGuards, returnedMoraleIds)
                    ?? TryCommitS1ExtendedActiveAbility(playerIndex, source, ability, target, onceKey, useTombGuards)
                    ?? CommandResult.Reject("该卡没有此主动效果");
        }
        var data = new Dictionary<string, string> { ["ability"] = ability };
        if (!string.IsNullOrWhiteSpace(target)) data["target"] = target;
        PushEffect(playerIndex, source, "active", "主动效果", data: data);
        if (State.ActiveDisaster?.CardId == "S01-DS08" && source.CardType == "artifact")
            DamageMasterNonLethal(playerIndex, 1, "使用圣物效果");
        return CommandResult.Ok();
    }

    private static int GetActiveAbilityMoraleCost(L12CardInstance source, string ability) => ability switch
    {
        "drawCycle" or "frontBuff" or "kusanagiDebuff" or "kusanagiStrong" or "cleopatraGuard" or "sunDraw"
            or "medjedDebuff" or "valkyrieRecover" or "lokiCycle" or "lokiHeal" or "amaterasuKill" => 1,
        "kusanagi" or "factionAddActive" or "factionDrawMove" or "destroyInfiltrator" or "sunGuard" or "asgardDraw"
            or "gramReady" or "sunTopThree" or "sunBottomEnemy" or "valhallaRecover" or "yomiSweep" => 2,
        "extendedRange" when source.CardId == "S01-0003" => 2,
        "discardHolyLock" => 3,
        _ => 0,
    };

    private int GetActiveAbilityReturnMoraleCost(int playerIndex, L12CardInstance source, string ability, string? target)
    {
        if (ability == "nonLethal" && source.CardId == "S01-01M1") return 4;
        if (ability == "searchBrothers" && source.CardId == "S01-0105") return 1;
        if (ability == "palaceExchange" && source.CardId == "S01-01D1")
            return DeclaredEnemyTarget(playerIndex, target)?.CurrentCost ?? 0;
        if (ability == "mengpoSilence" && source.CardId == "S01-01M2") return 1;
        if (ability == "shennongReset" && source.CardId == "S02-0104") return 1;
        return 0;
    }

    private CommandResult ResolveActiveReturnMoraleChoice(L12Prompt prompt, IReadOnlyCollection<string> selectedMoraleIds, bool? preferNormalMoraleReturn)
    {
        var player = State.Players[prompt.PlayerIndex];
        var sourceId = prompt.Data.GetValueOrDefault("sourceId") ?? string.Empty;
        var source = FindOnField(player, sourceId, out _, out _)
            ?? (player.Relic?.InstanceId == sourceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == sourceId)
            ?? (prompt.Data.GetValueOrDefault("sourceCardId") == player.MasterId ? CreateCard(player.MasterId, sourceId) : null);
        if (source is null) return CommandResult.Reject("返还士气时效果来源已不合法");
        if (preferNormalMoraleReturn == true) player.PreferNormalMoraleReturns = true;
        var automaticNormalIds = (prompt.Data.GetValueOrDefault("autoNormalMoraleIds") ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var returnedIds = automaticNormalIds.Concat(selectedMoraleIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!int.TryParse(prompt.Data.GetValueOrDefault("returnCount"), out var count)
            || returnedIds.Length != count
            || returnedIds.Any(id => player.Morale.All(card => card.InstanceId != id)))
            return CommandResult.Reject("请选择指定数量、且仍在士气区的士气");
        return CommitActiveAbility(prompt.PlayerIndex, source, prompt.Data.GetValueOrDefault("ability") ?? string.Empty,
            prompt.Data.GetValueOrDefault("target"), returnedMoraleIds: returnedIds);
    }

    private bool ReturnActiveMorale(L12PlayerState player, int count)
    {
        var selected = player.Morale.Where(card => !card.Tapped).Take(count).ToArray();
        if (selected.Length < count) return false;
        foreach (var card in selected)
        {
            player.Morale.Remove(card); card.Tapped = false; ReturnMoraleCardToDestination(player, card);
        }
        return true;
    }

    private void ResolveActiveEffect(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var source = FindSource(item);
        var ability = item.Data.GetValueOrDefault("ability") ?? string.Empty;
        switch (ability)
        {
            case "drawCycle":
                if (!Draw(player, 1)) { SetWinner(1 - item.Controller, "杨戬效果抽牌时牌库为空"); FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "card", "选择 1 张手牌放回牌库顶部或底部", player.Hand.Select(card => card.InstanceId), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string>
                    {
                        ["action"] = "yangjian-return-card",
                        ["placementMode"] = "single-top-bottom",
                    });
                return;
            case "nonLethal":
                DamageMasterNonLethal(1 - item.Controller, 1, "杨戬的主宰效果"); FinishStackItem(item); return;
            case "frontBuff":
                player.UsedAbilities.Add($"susano-buff:{item.Data["target"]}"); FinishStackItem(item); return;
            case "kusanagi":
            {
                var sword = player.Relic;
                if (sword?.CardId == "S01-0417")
                {
                    var (row, slot) = ParseSlot(item.Data["target"]);
                    // 保留圣物原本的登场回合；置入前排不是再次召唤，因此不会重新获得召唤失调。
                    DiscardAttachedCards(sword, "被叠放的圣物离开圣物区");
                    player.Relic = null; sword.Troops = 5000; player.Field[row][slot] = sword;
                    AddEvent("put", item.Controller, "须佐之男将草雉剑置入前排，视为兵力 5000 的【武者】军团", sword);
                }
                FinishStackItem(item); return;
            }
            case "addMorale":
                AddMorale(player, 1, tapped: true); FinishStackItem(item); return;
            case "searchBrothers":
            {
                var choices = player.Library.Where(card => card.CardId is "S01-0106" or "S01-0107").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) { AddEvent("reveal", item.Controller, "刘备检索未命中，向对手展示牌库"); Shuffle(player.Library); FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "search", "选择牌库中 1 张〈关羽〉或〈张飞〉加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "liubei-search" });
                return;
            }
            case "artifactDraw":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "山河社稷图效果抽牌时牌库为空");
                FinishStackItem(item); return;
            case "artifactSearch":
            {
                var top = player.Library.Take(3).ToArray();
                var choices = top.Where(card => card.Faction == "tianting").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) { AddEvent("reveal", item.Controller, "山河社稷图检索未命中，向对手展示顶部 3 张牌", top); FinishStackItem(item); return; }
                item.Data["shanhe-top"] = string.Join('|', top.Select(card => card.InstanceId));
                CreatePrompt(item.Controller, "search", "选择其中 1 张【天廷】卡牌加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "shanhe-search-pick" });
                return;
            }
            case "kusanagiDebuff":
            {
                var target = FindPublicCard(item.Data["target"], out _);
                if (target is not null) target.CostModifier--;
                FinishStackItem(item); return;
            }
            case "kusanagiStrong":
            {
                var target = FindOnField(player, item.Data["target"], out _, out _);
                if (target is not null) target.HasStrongAttack = true;
                FinishStackItem(item); return;
            }
            case "factionAddActive":
                AddMorale(player, 1, tapped: false);
                AddEvent("faction-effect", item.Controller, "天廷阵营效果：追加 1 张活跃士气");
                FinishStackItem(item); return;
            case "factionZeroRecovery":
                AddMorale(player, 2, tapped: true);
                AddEvent("faction-effect", item.Controller, "天廷阵营效果：追加 2 张休整士气");
                FinishStackItem(item); return;
            case "factionDrawMove":
            {
                if (!Draw(player, 1)) { SetWinner(1 - item.Controller, "高天原阵营效果抽牌时牌库为空"); FinishStackItem(item); return; }
                var choices = player.Field.SelectMany(row => row)
                    .Where(card => card is not null && IsFieldLegion(card) && !card.Tapped && !card.Hidden)
                    .Select(card => card!.InstanceId).ToList();
                choices.Add("skip");
                CreatePrompt(item.Controller, "optional-target", "可选择我方 1 张活跃军团进行 1 格位移", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "faction-move-card" });
                return;
            }
            default:
                if (!TryResolveS2UniversalActive(item, source, ability) && !TryResolveS1ExtendedActive(item, source, ability)
                    && !TryResolveS2FactionActive(item, source, ability)) FinishStackItem(item);
                return;
        }
    }

    private void ContinueYangJianReturn(L12StackItem item, string cardId)
    {
        item.Data["return-card"] = cardId;
        CreatePrompt(item.Controller, "option", "将该手牌放回牌库顶部或底部", ["top", "bottom"], 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "yangjian-return-place" });
    }

    private void CompleteYangJianReturn(L12StackItem item, string place)
    {
        var player = State.Players[item.Controller];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == item.Data["return-card"]);
        if (card is not null)
        {
            player.Hand.Remove(card);
            if (place == "top") player.Library.Insert(0, card); else player.Library.Add(card);
        }
        FinishStackItem(item);
    }

    private void CompleteYangJianReturn(L12StackItem item, string cardId, string place)
    {
        var player = State.Players[item.Controller];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == cardId);
        if (card is not null)
        {
            player.Hand.Remove(card);
            if (place == "top") player.Library.Insert(0, card); else player.Library.Add(card);
            AddEvent("return", item.Controller, $"{card.Name} 返回牌库{(place == "top" ? "顶部" : "底部")}", card);
        }
        FinishStackItem(item);
    }

    private void CompleteLiuBeiSearch(L12StackItem item, string cardId)
    {
        var player = State.Players[item.Controller];
        var card = player.Library.First(candidate => candidate.InstanceId == cardId);
        player.Library.Remove(card); AddCardToHandByEffect(player, card, "library", $"刘备将{card.Name}加入手牌"); AddEvent("search", item.Controller, $"刘备将 {card.Name} 加入手牌", card);
        Shuffle(player.Library); FinishStackItem(item);
    }

    private void CompleteShanheSearch(L12StackItem item, string cardId)
    {
        var player = State.Players[item.Controller];
        var card = player.Library.First(candidate => candidate.InstanceId == cardId);
        player.Library.Remove(card); AddCardToHandByEffect(player, card, "library", $"山河社稷图将{card.Name}加入手牌"); AddEvent("search", item.Controller, $"山河社稷图将 {card.Name} 加入手牌", card);
        var remaining = item.Data["shanhe-top"].Split('|').Where(id => id != cardId).ToArray();
        if (remaining.Length == 0) { FinishStackItem(item); return; }
        item.Data["reorder-context"] = "shanhe";
        item.Data["reorder-cards"] = string.Join('|', remaining);
        CreatePrompt(item.Controller, "order", "依次选择其余卡牌的排列顺序", remaining, remaining.Length, remaining.Length,
            "card-effect", item.StackItemId, data: new Dictionary<string, string>
            {
                ["action"] = "reorder-order",
                ["placementMode"] = "split-top-bottom",
            });
    }
}
