namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private bool TryGetIsisVictorySource(L12PlayerState player, out L12CardInstance? osiris, out string error)
    {
        osiris = player.Graveyard.FirstOrDefault(card => card.CardId == "S01-02M2");
        var completed = player.SpecialZones.CanopicProgress
            .Where(card => card.CardId is "S01-0216" or "S01-0217" or "S01-0218" or "S01-0219" or "S01-0220")
            .Select(card => card.CardId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (player.MasterId == "S01-02M1" && completed >= 5 && osiris is not null)
        {
            error = string.Empty;
            return true;
        }
        error = "需要伊西斯、墓地的复苏的奥西里斯与5种已完成的卡诺匹斯圣物";
        return false;
    }

    private CommandResult BeginActiveAbility(int playerIndex, L12Command command)
    {
        var player = State.Players[playerIndex];
        var ability = command.Ability ?? string.Empty;
        var source = FindOnField(player, command.CardInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == command.CardInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
            ?? player.SpecialZones.Trials.FirstOrDefault(card => card.InstanceId == command.CardInstanceId)
            ?? player.Graveyard.FirstOrDefault(card => card.InstanceId == command.CardInstanceId
                && IsLegalGraveyardActiveAbilitySource(player, card, ability))
            ?? player.Graveyard.FirstOrDefault(card => card.InstanceId == command.CardInstanceId
                && card.CardId == "S01-02M2" && ability == "isisVictory");
        if (source is null && ability == "destroyInfiltrator"
            && FindPublicCard(command.CardInstanceId, out _) is { CardId: "S01-0004" } infiltrator)
            source = infiltrator;
        if (source is null && player.Morale.FirstOrDefault(card => card.InstanceId == command.CardInstanceId) is { } morale)
            source = CreateCard(morale.IsGodPower ? "S02-05C1" : morale.CardId, morale.InstanceId);
        if (source is null && command.CardInstanceId is not null
            && (command.CardInstanceId == player.MasterId || command.CardInstanceId == $"master-{playerIndex}"))
        {
            source = CreateCard(player.MasterId, $"master-{playerIndex}");
            source.Tapped = player.MasterTapped;
        }
        if (source is null && command.CardInstanceId == $"faction-{playerIndex}")
        {
            var moraleId = player.Faction == "olympus"
                ? (ability == "godPowerDraw" ? "S02-05C1" : "S02-05C1A")
                : player.Morale.FirstOrDefault()?.CardId ?? player.MoraleDeck.FirstOrDefault()?.CardId;
            if (moraleId is not null)
                source = CreateCard(CanonicalFactionEffectCardId(moraleId), $"faction-{playerIndex}");
        }
        if (source is null) return CommandResult.Reject("主动效果来源不在我方公开区域");
        if (ability != "discardHolyLock" && source.AttachedCards.Any(card => card.CardId == "S02-0013"))
            return CommandResult.Reject("该圣物被〈神圣伽锁〉叠放，当前无法使用");
        if (player.UsedAbilities.Contains(ActiveAbilityUsageKey(source.InstanceId, source.CardId, ability))
            && !MatchesPendingFreeMasterActivation(playerIndex, source, ability))
            return CommandResult.Reject("该效果本回合已经发动");

        string[] choices;
        switch (ability)
        {
            case "frontBuff" when source.CardId == "S01-04M2":
                choices = PublicFactionLegions(player, "gaotianyuan").Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择我方 1 张【高天原】军团");
            case "kusanagi" when source.CardId == "S01-04M2":
                if (player.Relic?.CardId != "S01-0417") return CommandResult.Reject("圣物区没有〈草薙剑〉");
                choices = Enumerable.Range(0, 3).Where(slot => player.Field[0][slot] is null).Select(slot => $"0:{slot}").ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择〈草薙剑〉置入前排的位置");
            case "artifactSearch" when source.CardId == "S01-0117":
                choices = player.Hand.Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择弃置的 1 张手牌");
            case "kusanagiDebuff" when source.CardId == "S01-0417":
                choices = PublicLegions(State.Players[1 - playerIndex]).Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择对方 1 张军团，本回合费用 -1");
            case "kusanagiStrong" when source.CardId == "S01-0417":
                choices = PublicFactionLegions(player, "gaotianyuan").Select(card => card.InstanceId).ToArray();
                return PromptActiveTarget(playerIndex, source, ability, choices, "选择我方 1 张【高天原】军团，本回合获得强攻");
            default:
                return TryBeginStarterRemainingActiveAbility(playerIndex, source, ability)
                    ?? TryBeginPublicActiveDeclaration(playerIndex, source, ability)
                    ?? TryBeginS2UniversalActiveAbility(playerIndex, source, ability)
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

    private static bool IsLegalGraveyardActiveAbilitySource(L12PlayerState player, L12CardInstance card, string ability)
        => card.CardId == "S02-0301" && ability == "thorHammerRevive" && player.MasterId == "S02-03M1";

    private static string ActiveAbilityUsageKey(string sourceInstanceId, string sourceCardId, string ability)
        => sourceCardId == "S01-03M2" && ability is "lokiCycle" or "lokiHeal"
            ? $"active:{sourceInstanceId}:loki"
            : sourceCardId == "S02-06S6" && ability is "crusadeTrialNoLoss" or "crusadeRichardPiercing" or "crusadeRecover"
                ? $"active:{sourceInstanceId}:crusade-choice"
            : $"active:{sourceInstanceId}:{ability}";

    private string[] ActiveAbilityReservedResourceIds(L12PlayerState player, L12CardInstance source, string ability,
        string? target, bool reserveInternalCosts)
    {
        if (ability == "horusRevive")
            return (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).Take(2).ToArray();
        if (L12StructuredCardSemantics.IsMedjed(source.CardId) && ability == "medjedDebuff")
        {
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            return declared.Length == 3 && declared[0] == "mode:strong" ? [declared[1]] : [];
        }
        if (reserveInternalCosts && L12StructuredCardSemantics.IsIsis(source.CardId) && ability == "isisCanopic")
            return (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).Take(3).ToArray();
        return [];
    }

    private string? ValidatePublicActiveDeclarationBeforePayment(int playerIndex, L12CardInstance source, string ability, string? target)
    {
        if (ValidateStarterRemainingActiveDeclaration(playerIndex, source, ability, target) is { } starterError)
            return starterError;
        var player = State.Players[playerIndex];
        var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        switch ((source.CardId, ability))
        {
            case ("S01-03C1", "asgardDraw"):
                if (declared.Length != 1 || declared[0] is not ("mode:none" or "mode:heal"))
                    return "阿斯加德阵营效果必须预先声明是否治疗";
                if (declared[0] == "mode:heal" && player.Hp > 5)
                    return "主宰血量高于5，不能声明额外治疗";
                break;
            case ("S01-01M2", "mengpoMorale"):
                if (player.Morale.Count >= State.Players[1 - playerIndex].Morale.Count
                    || declared.Length != 1 || !player.Hand.Any(card => card.InstanceId == declared[0]))
                    return "孟婆的士气条件或弃牌费用已失效";
                break;
            case ("S01-02D1", "sunTopThree"):
                if (declared.Length != 1 || declared[0] != "mode:none"
                    && !player.Graveyard.Any(card => card.InstanceId == declared[0]
                        && card.Faction == "taiyangcheng" && CanEnterHandOrLibrary(card)))
                    return "众神之乡声明的墓地回收目标已失效";
                break;
            case ("S01-02M1", "isisCanopic"):
            {
                if (declared.Length != 5 || declared.Take(3).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3
                    || declared.Take(3).Any(id => !PublicLegions(player).Any(card => card.InstanceId == id && card.CardId == "S01-0212"))
                    || !player.Graveyard.Any(card => card.InstanceId == declared[3]
                        && card.Name.Contains("卡诺匹斯", StringComparison.Ordinal) && card.CardType == "artifact"
                        && !player.SpecialZones.CanopicProgress.Any(done => done.CardId == card.CardId))
                    || declared[4] is not ("mode:draw" or "mode:heal"))
                    return "伊西斯声明的陵墓守卫、卡诺匹斯圣物或奖励模式已失效";
                break;
            }
            case ("S01-04M1", "amaterasuReady"):
                if (declared.Length is < 1 or > 3
                    || !player.Hand.Any(card => card.InstanceId == declared[0])
                    || declared.Skip(1).Distinct(StringComparer.OrdinalIgnoreCase).Count() != declared.Length - 1
                    || declared.Skip(1).Any(id => !player.Morale.Any(card => card.InstanceId == id && card.Tapped)))
                    return "天照大神声明的弃牌费用或士气目标已失效";
                break;
        }
        return null;
    }

    private CommandResult CommitActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target,
        bool? useTombGuards = null, IReadOnlyCollection<string>? selectedResourceIds = null,
        IReadOnlyCollection<string>? selectedReturnIds = null)
    {
        var player = State.Players[playerIndex];
        if (TryCommitTrialAdvanceActivation(playerIndex, source, ability) is { } trialAdvanceResult)
            return trialAdvanceResult;
        var onceKey = ActiveAbilityUsageKey(source.InstanceId, source.CardId, ability);
        if (TryCommitFreeMasterActivation(playerIndex, source, ability, target) is { } freeResult)
            return freeResult;
        if (player.UsedAbilities.Contains(onceKey)) return CommandResult.Reject("该效果本回合已经发动");
        if (ValidatePublicActiveDeclarationBeforePayment(playerIndex, source, ability, target) is { } declarationError)
            return CommandResult.Reject(declarationError);
        var disasterMasterSurcharge = State.ActiveDisaster?.CardId == "S02-DS06" && source.CardId == player.MasterId ? 1 : 0;
        var baseMoraleCost = GetActiveAbilityMoraleCost(source, ability, target);
        var masterMoraleWaived = source.CardId == player.MasterId
            && player.MasterMoraleWaiverUntilTurn >= State.TurnSerial;
        var moraleCost = (masterMoraleWaived ? 0 : baseMoraleCost) + disasterMasterSurcharge;
        var returnCost = GetActiveAbilityReturnMoraleCost(player, source, ability, target);
        var requireActiveReturn = ActiveReturnRequiresActiveMorale(source, ability);
        var reservedResourceIds = ActiveAbilityReservedResourceIds(player, source, ability, target,
            reserveInternalCosts: disasterMasterSurcharge > 0);
        if (L12StructuredCardSemantics.IsMedjed(source.CardId) && ability == "medjedDebuff" && reservedResourceIds.Length > 0)
        {
            var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
            var guard = PublicLegions(player).FirstOrDefault(card => card.InstanceId == reservedResourceIds[0]
                && card.CardId == "S01-0212" && !card.Tapped);
            if (declared.Length != 3 || guard is null || DeclaredEnemyTarget(playerIndex, declared[2]) is null)
                return CommandResult.Reject("梅杰德强模式声明的陵墓守卫或目标已失效");
        }
        if (returnCost > 0 && ValidateActiveReturnPrepayment(playerIndex, source, ability, target) is { } returnError)
            return CommandResult.Reject(returnError);
        var declaredReturnIds = selectedReturnIds?.ToArray();
        if (returnCost > 0 && declaredReturnIds is null
            && NeedsManualReturnMoraleSelection(player, returnCost, requireActiveReturn))
        {
            CreateReturnMoralePrompt(playerIndex, returnCost, "active-return-choice", null,
                new Dictionary<string, string>
                {
                    ["sourceId"] = source.InstanceId, ["sourceCardId"] = source.CardId, ["ability"] = ability,
                    ["target"] = target ?? string.Empty,
                }, requireActiveReturn);
            return CommandResult.Ok();
        }
        if (returnCost > 0 && declaredReturnIds is null)
            declaredReturnIds = player.Morale.Where(card => !requireActiveReturn || !card.Tapped)
                .OrderByDescending(card => card.Tapped).Take(returnCost).Select(card => card.InstanceId).ToArray();
        if (declaredReturnIds is not null
            && !CanReturnSelectedMoraleById(player, declaredReturnIds, returnCost, requireActiveReturn))
            return CommandResult.Reject("选择的返还士气已失效或数量不正确");
        var excludedResourceIds = (declaredReturnIds ?? [])
            .Concat(reservedResourceIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // 奥林匹斯阵营效果支付后还要从同一士气区选择翻转目标。只要存在多种
        // 支付结果，支付对象就会影响效果结果，必须让玩家先点击真正要消耗的资源。
        var forceManualResourcePayment = ability == "olympusMoraleFlip"
            && ActiveResourceCountExcluding(player, excludedResourceIds) > moraleCost;
        if (disasterMasterSurcharge > 0 && ActiveResourceCountExcluding(player, excludedResourceIds) < moraleCost)
            return CommandResult.Reject("〈傲慢之罪〉使主宰效果额外需要消耗1士气");
        if (moraleCost > 0 && (reservedResourceIds.Length > 0 || disasterMasterSurcharge > 0)
            && selectedResourceIds is null
            && !NeedsManualOrdinaryResourcePayment(player, moraleCost, excludedResourceIds))
        {
            selectedResourceIds = SelectAutomaticOrdinaryResourcePaymentIds(player, moraleCost, excludedResourceIds);
        }
        if (moraleCost > 0 && useTombGuards is null && selectedResourceIds is null
            && (forceManualResourcePayment
                || NeedsManualOrdinaryResourcePayment(player, moraleCost, excludedResourceIds)))
        {
            var paymentData = new Dictionary<string, string>
            {
                ["sourceId"] = source.InstanceId, ["sourceCardId"] = source.CardId, ["ability"] = ability,
                ["target"] = target ?? string.Empty,
            };
            if (declaredReturnIds is not null) paymentData["returnIds"] = string.Join('|', declaredReturnIds);
            CreateResourcePaymentPrompt(playerIndex, moraleCost, "active-morale-choice", null, paymentData,
                excludedResourceIds);
            return CommandResult.Ok();
        }
        if (selectedResourceIds is not null
            && !CanConsumeSelectedResources(player, moraleCost, selectedResourceIds, excludedResourceIds))
            return CommandResult.Reject("选择的支付资源已失效或数量不正确");
        var returnPrepaid = false;
        if (declaredReturnIds is not null)
        {
            if (!ReturnSelectedMoraleById(player, declaredReturnIds, returnCost, requireActiveReturn))
                return CommandResult.Reject("选择的返还士气已失效或数量不正确");
            returnPrepaid = true;
        }
        if (selectedResourceIds is not null)
        {
            if (!TryConsumeSelectedResources(player, moraleCost, selectedResourceIds, excludedResourceIds))
                return CommandResult.Reject("选择的支付资源已失效或数量不正确");
            // 下层各阵营效果仍通过统一 ConsumeMorale 申报费用；以临时士气作为一次性预付凭证，避免重复扣费。
            player.TemporaryMorale += moraleCost;
        }
        player.MasterMoraleWaiverCredit = masterMoraleWaived ? baseMoraleCost : 0;
        bool ConsumeMorale(int cost) => useTombGuards switch
        {
            true => TryConsumeMorale(player, cost, preferTombGuards: true, allowTombGuards: true),
            false => TryConsumeMorale(player, cost, preferTombGuards: false, allowTombGuards: false),
            _ => TryConsumeMorale(player, cost),
        };
        bool ReturnMoraleCost(int cost, bool requireActive = false)
        {
            if (returnPrepaid && cost == returnCost && requireActive == requireActiveReturn)
            {
                returnPrepaid = false;
                return true;
            }
            if (requireActive) return ReturnActiveMorale(player, cost);
            return ReturnMorale(player, cost);
        }
        var moraleReturnedByMasterEffect = 0;
        switch (ability)
        {
            case "isisVictory" when source.CardId is "S01-02M1" or "S01-02M2":
            {
                if (!TryGetIsisVictorySource(player, out var osiris, out var error))
                    return CommandResult.Reject(error);
                SetWinner(playerIndex, "复苏的奥西里斯登场，达成伊西斯特殊胜利");
                AddEvent("special-victory", playerIndex, "〈复苏的奥西里斯〉替换〈伊西斯〉登场，达成特殊胜利", osiris!);
                return CommandResult.Ok();
            }
            case "drawCycle" when source.CardId == "S01-01M1":
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要消耗 1 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            case "nonLethal" when source.CardId == "S01-01M1":
                if (!ReturnMoraleCost(4)) return CommandResult.Reject("需要返还 4 张士气");
                moraleReturnedByMasterEffect = 4;
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
                if (!returnPrepaid && !CanReturnMorale(player, 1)) return CommandResult.Reject("需要返还 1 张士气");
                source.Tapped = true; if (!ReturnMoraleCost(1)) return CommandResult.Reject("需要返还 1 张士气"); break;
            case "artifactDraw" when source.CardId == "S01-0117":
                if (source.Tapped) return CommandResult.Reject("山河社稷图必须为活跃状态");
                if (!ReturnMoraleCost(1, requireActive: true)) return CommandResult.Reject("需要返还 1 张活跃士气");
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
                if (player.UsedAbilities.Contains($"active:{source.InstanceId}:choice")) return CommandResult.Reject("草薙剑的效果本回合已经发动");
                if (!ConsumeMorale(1)) return CommandResult.Reject("需要消耗 1 张活跃士气");
                player.UsedAbilities.Add($"active:{source.InstanceId}:choice"); break;
            case "discardHolyLock" when source.AttachedCards.Any(card => card.CardId == "S02-0013"):
                if (!ConsumeMorale(3)) return CommandResult.Reject("需要消耗3张活跃士气");
                break;
            case "factionAddActive" when source.CardId == "S01-01C1":
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要消耗 2 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            case "factionDrawMove" when source.CardId == "S01-04C1":
                if (!ConsumeMorale(2)) return CommandResult.Reject("需要消耗 2 张活跃士气");
                player.UsedAbilities.Add(onceKey); break;
            default:
            {
                var result = TryCommitS2UniversalActiveAbility(playerIndex, source, ability, target, onceKey, returnPrepaid)
                    ?? TryCommitStarterRemainingActiveAbility(playerIndex, source, ability, target, onceKey)
                    ?? TryCommitS2FactionActiveAbility(playerIndex, source, ability, target, onceKey, useTombGuards)
                    ?? TryCommitS1ExtendedActiveAbility(playerIndex, source, ability, target, onceKey, useTombGuards, returnPrepaid)
                    ?? CommandResult.Reject("该卡没有此主动效果");
                if (!result.Accepted || disasterMasterSurcharge == 0) return result;
                if (!ConsumeMorale(disasterMasterSurcharge))
                    return CommandResult.Reject("〈傲慢之罪〉使主宰效果额外需要消耗1士气");
                return result;
            }
        }
        if (disasterMasterSurcharge > 0 && !ConsumeMorale(disasterMasterSurcharge))
            return CommandResult.Reject("〈傲慢之罪〉使主宰效果额外需要消耗1士气");
        var data = new Dictionary<string, string> { ["ability"] = ability };
        if (!string.IsNullOrWhiteSpace(target)) data["target"] = target;
        var activeCompositePlan = (source.CardId, ability) switch
        {
            ("S01-0105", "searchBrothers") => "active:S01-0105:searchBrothers",
            ("S01-01M1", "drawCycle") => "active:S01-01M1:drawCycle",
            _ => null,
        };
        if (activeCompositePlan is not null)
            foreach (var pair in CompositeFirstSegmentData(activeCompositePlan,
                         new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)))
                data[pair.Key] = pair.Value;
        PushEffect(playerIndex, source, "active", "主动效果", data: data);
        if (moraleReturnedByMasterEffect > 0)
            QueueS2MasterMoraleReturnTriggers(playerIndex, source, moraleReturnedByMasterEffect);
        return CommandResult.Ok();
    }

    private CommandResult? TryCommitFreeMasterActivation(int playerIndex, L12CardInstance source, string ability, string? target)
    {
        if (!MatchesPendingFreeMasterActivation(playerIndex, source, ability)) return null;
        var free = State.FreeMasterActivation!;

        var player = State.Players[playerIndex];
        var legalAbility = GetAbilities(player.MasterId).Any(view => view.Id.Equals(ability, StringComparison.OrdinalIgnoreCase))
            && GetActiveAbilityMoraleCost(source, ability) > 0;
        State.FreeMasterActivation = null;
        if (!legalAbility) return CommandResult.Reject("信仰狂热者选择的主宰效果已不合法");

        var data = new Dictionary<string, string>
        {
            ["ability"] = ability,
            ["freeMasterActivation"] = "true",
            ["freeMasterSource"] = free.SourceInstanceId,
        };
        if (!string.IsNullOrWhiteSpace(target)) data["target"] = target;
        if (source.CardId == "S01-01M1" && ability == "drawCycle")
            foreach (var pair in CompositeFirstSegmentData("active:S01-01M1:drawCycle",
                         new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)))
                data[pair.Key] = pair.Value;
        PushEffect(playerIndex, source, "active", "由〈信仰狂热者〉无视消耗触发的主宰效果", data: data);
        AddEvent("effect", playerIndex, $"〈信仰狂热者〉无视全部消耗触发〈{source.Name}〉的主宰效果，且不计入使用次数", source);
        return CommandResult.Ok();
    }

    private bool MatchesPendingFreeMasterActivation(int playerIndex, L12CardInstance source, string ability)
    {
        var free = State.FreeMasterActivation;
        return free is not null
            && free.Controller == playerIndex
            && source.CardId == State.Players[playerIndex].MasterId
            && free.Ability.Equals(ability, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetActiveAbilityMoraleCost(L12CardInstance source, string ability, string? target = null) => ability switch
    {
        "drawCycle" or "frontBuff" or "kusanagiDebuff" or "kusanagiStrong" or "cleopatraGuard" or "sunDraw"
            or "medjedDebuff" or "valkyrieRecover" or "lokiCycle" or "lokiHeal" or "amaterasuKill" => 1,
        "asgardDraw" when (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Contains("mode:heal", StringComparer.OrdinalIgnoreCase) => 3,
        "kusanagi" or "factionAddActive" or "factionDrawMove" or "destroyInfiltrator" or "sunGuard" or "asgardDraw"
            or "gramReady" or "sunTopThree" or "sunBottomEnemy" or "valhallaRecover" or "yomiSweep" => 2,
        "extendedRange" when source.CardId == "S01-0003" => 2,
        "discardHolyLock" => 3,
        "forgePromotionDiscount" or "forgeReadyOnKill" or "olympusMoraleFlip" or "horusRevive" => 1,
        "thorCharge" => 2,
        "hippolytaRevive" => 3,
        "factionGainRune" => 2,
        _ => 0,
    };

    private bool ReturnActiveMorale(L12PlayerState player, int count)
    {
        var selected = player.Morale.Where(card => !card.Tapped).Take(count).ToArray();
        if (selected.Length < count) return false;
        return ReturnSelectedMorale(player, selected, requireActive: true);
    }

    private void ResolveActiveEffect(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var source = FindSource(item);
        var ability = item.Data.GetValueOrDefault("ability") ?? string.Empty;
        if (TryResolveStarterRemainingActiveEffect(item, source, ability)) return;
        switch (ability)
        {
            case "drawCycle":
                if (AtomicFlowKey(item) == "yangjian-draw")
                {
                    if (!Draw(player, 1)) SetWinner(1 - item.Controller, "杨戬效果抽牌时牌库为空");
                    FinishStackItem(item); return;
                }
                if (AtomicFlowKey(item) == "yangjian-return")
                {
                    if (player.Hand.Count == 0) { FinishStackItem(item); return; }
                    CreatePrompt(item.Controller, "card", "选择 1 张手牌放回牌库顶部或底部",
                        player.Hand.Select(card => card.InstanceId), 1, 1, "card-effect", item.StackItemId,
                        data: new Dictionary<string, string>
                        {
                            ["action"] = "yangjian-return-card",
                            ["placementMode"] = "single-top-bottom",
                        });
                    return;
                }
                FinishStackItem(item); return;
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
                    player.Relic = null;
                    L12DerivedStats.SetUntilTurnEnd(sword, 5000, int.MaxValue);
                    player.Field[row][slot] = sword;
                    AddEvent("put", item.Controller, "须佐之男将草薙剑置入前排，视为兵力 5000 的【武者】军团", sword);
                }
                FinishStackItem(item); return;
            }
            case "addMorale":
                AddMorale(player, 1, tapped: true); FinishStackItem(item); return;
            case "searchBrothers":
            {
                if (AtomicFlowKey(item) == "liubei-shuffle")
                {
                    ShuffleLibrary(player, "刘备检索结算"); FinishStackItem(item); return;
                }
                var choices = player.Library.Where(card => card.CardId is "S01-0106" or "S01-0107").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) { AddEvent("reveal", item.Controller, "刘备检索未命中，向对手展示牌库"); FinishStackItem(item); return; }
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
                item.Data["shanhe-top"] = string.Join('|', top.Select(card => card.InstanceId));
                var choices = top.Where(card => card.Faction == "tianting").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0)
                {
                    AddEvent("reveal", item.Controller, "山河社稷图检索未命中，向对手展示顶部 3 张牌", top);
                    BeginAllTopBottomReorder(item, "shanhe", top.Select(card => card.InstanceId),
                        "山河社稷图：排列其余卡牌，并将其全部放回牌库顶部或全部放回牌库底部");
                    return;
                }
                CreatePrompt(item.Controller, "search", "选择其中 1 张【天廷】卡牌加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "shanhe-search-pick" });
                return;
            }
            case "kusanagiDebuff":
            {
                var target = PublicLegions(State.Players[1 - item.Controller])
                    .FirstOrDefault(card => card.InstanceId == item.Data["target"]);
                if (target is not null) target.CostModifier--;
                FinishStackItem(item); return;
            }
            case "kusanagiStrong":
            {
                var target = PublicFactionLegions(player, "gaotianyuan")
                    .FirstOrDefault(card => card.InstanceId == item.Data["target"]);
                if (target is not null) GrantStrongAttack(target);
                FinishStackItem(item); return;
            }
            case "factionAddActive":
                AddMorale(player, 1, tapped: false, fromFactionEffect: true);
                AddEvent("faction-effect", item.Controller, "天廷阵营效果：追加 1 张活跃士气");
                FinishStackItem(item); return;
            case "factionZeroRecovery":
                AddMorale(player, 2, tapped: true, fromFactionEffect: true);
                AddEvent("faction-effect", item.Controller, "天廷阵营效果：追加 2 张休整士气");
                FinishStackItem(item); return;
            case "factionDrawMove":
            {
                if (!Draw(player, 1)) { SetWinner(1 - item.Controller, "高天原阵营效果抽牌时牌库为空"); FinishStackItem(item); return; }
                BeginGaotianyuanMoveChoice(item);
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
        player.Library.Remove(card);
        AddCardToHandByEffect(player, card, "library", $"刘备将{card.Name}加入手牌");
        AddEvent("search", item.Controller, $"刘备将 {card.Name} 加入手牌", card);
        FinishStackItem(item);
    }

    private void CompleteShanheSearch(L12StackItem item, string cardId)
    {
        var player = State.Players[item.Controller];
        var card = player.Library.First(candidate => candidate.InstanceId == cardId);
        player.Library.Remove(card);
        AddCardToHandByEffect(player, card, "library", $"山河社稷图将{card.Name}加入手牌");
        AddEvent("search", item.Controller, $"山河社稷图将 {card.Name} 加入手牌", card);
        var remaining = item.Data["shanhe-top"].Split('|').Where(id => id != cardId).ToArray();
        if (remaining.Length == 0) { FinishStackItem(item); return; }
        BeginAllTopBottomReorder(item, "shanhe", remaining,
            "山河社稷图：排列其余卡牌，并将其全部放回牌库顶部或全部放回牌库底部");
    }
}
