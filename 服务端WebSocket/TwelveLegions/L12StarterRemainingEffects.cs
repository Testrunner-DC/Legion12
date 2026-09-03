namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static List<L12AbilityView> GetStarterRemainingAbilityViews(string cardId)
        => L12StructuredCardRules.StarterRemainingAbilityViews(cardId).ToList();

    private CommandResult? TryBeginStarterRemainingActiveAbility(int controller, L12CardInstance source,
        string ability)
    {
        var player = State.Players[controller];
        switch (ability)
        {
            case "oasisDancerBuff":
            case "christinaFreeTactic":
            case "kaneMillOne":
                if (source.Tapped) return CommandResult.Reject($"{source.Name}必须为活跃状态");
                return CommitActiveAbility(controller, source, ability, null);
            case "oiranTransfer":
            {
                if (source.Tapped) return CommandResult.Reject("吉原的花魁必须为活跃状态");
                var own = PublicLegions(player).Select(card => card.InstanceId).ToList();
                var enemy = PublicLegions(State.Players[1 - controller]).Select(card => card.InstanceId).ToList();
                if (own.Count == 0 || enemy.Count == 0) return CommandResult.Reject("双方战场各需要至少1张军团");
                return BeginPendingActivationSequence(controller, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "enemy-legion", DeclarationKey = "enemyTarget", Text = "吉原的花魁：选择本回合兵力-1000的对方军团", ValidChoices = enemy, MinChoose = 1, MaxChoose = 1, TargetPlayerIndex = 1 - controller },
                    new L12ActivationSelectionStep { Kind = "field-legion", DeclarationKey = "ownTarget", Text = "吉原的花魁：选择本回合兵力+1000的我方军团", ValidChoices = own, MinChoose = 1, MaxChoose = 1 },
                ]);
            }
            case "lightSwordActive":
            {
                if (source.Tapped) return CommandResult.Reject("光之剑必须为活跃状态");
                if (player.Hand.Count == 0) return CommandResult.Reject("需要弃置1张手牌");
                var targets = player.Field[0].Where(card => card is not null && IsFieldLegion(card)
                        && L12StructuredCardRules.HasFaction(player, card, "otherworld"))
                    .Select(card => card!.InstanceId).ToList();
                return BeginPendingActivationSequence(controller, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "option", DeclarationKey = "mode", Text = "光之剑：选择使我方前排1张【彼界】军团本回合兵力+2000，或获得1符文", ValidChoices = targets.Count > 0 ? ["mode:buff", "mode:rune"] : ["mode:rune"], MinChoose = 1, MaxChoose = 1, ChoiceLabels = new() { ["mode:buff"] = "我方前排1张【彼界】军团本回合兵力+2000", ["mode:rune"] = "获得1符文" } },
                    new L12ActivationSelectionStep { Kind = "hand-card", DeclarationKey = "discardCost", Text = "光之剑：选择弃置的1张手牌", ValidChoices = player.Hand.Select(card => card.InstanceId).ToList(), MinChoose = 1, MaxChoose = 1 },
                    new L12ActivationSelectionStep { Kind = "field-legion", DeclarationKey = "buffTarget", Text = "光之剑：选择本回合兵力+2000的我方前排【彼界】军团", ValidChoices = targets, MinChoose = 1, MaxChoose = 1, RequiredDeclaredChoice = "mode:buff" },
                ]);
            }
            case "horusRevive":
            {
                var field = PublicLegions(player).Select(card => card.InstanceId).ToList();
                var grave = player.Graveyard.Where(card => card.CardType == "legion" && card.BaseTroops <= 2000
                        && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng"))
                    .Select(card => card.InstanceId).ToList();
                if (field.Count < 2 || grave.Count == 0) return CommandResult.Reject("战场需要2张军团，墓地需要兵力不高于2000的太阳城军团");
                return BeginPendingActivationSequence(controller, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "field-legion", DeclarationKey = "discardCosts", Text = "荷鲁斯：选择弃置的2张我方军团", ValidChoices = field, MinChoose = 2, MaxChoose = 2 },
                    new L12ActivationSelectionStep { Kind = "grave-card", DeclarationKey = "entryCard", Text = "荷鲁斯：选择休整登场的太阳城军团", ValidChoices = grave, MinChoose = 1, MaxChoose = 1 },
                    new L12ActivationSelectionStep { Kind = "slot", DeclarationKey = "entrySlot", Text = "荷鲁斯：选择休整登场位置", ValidChoices = Enumerable.Range(0, 2).SelectMany(row => Enumerable.Range(0, 3).Select(slot => $"{row}:{slot}")).ToList(), MinChoose = 1, MaxChoose = 1 },
                ]);
            }
            case "sifCycle":
            {
                var grave = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard")
                        && CanEnterHandOrLibrary(card)).Select(card => card.InstanceId).ToList();
                if (grave.Count < 3) return CommandResult.Reject("墓地需要至少3张阿斯加德卡牌");
                return BeginPendingActivationSequence(controller, source, ability,
                [new L12ActivationSelectionStep { Kind = "order", DeclarationKey = "graveOrder", Text = "西芙：依序选择返回牌库底部的3张阿斯加德卡牌", ValidChoices = grave, MinChoose = 3, MaxChoose = 3 }]);
            }
            case "athenaFrontBuff":
                if (player.Hand.Count == 0 || player.Morale.Count == 0)
                    return CommandResult.Reject("需要1张手牌和1张士气");
                return BeginPendingActivationSequence(controller, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "hand-card", DeclarationKey = "discardCost", Text = "雅典娜：选择弃置的1张手牌", ValidChoices = player.Hand.Select(card => card.InstanceId).ToList(), MinChoose = 1, MaxChoose = 1 },
                    new L12ActivationSelectionStep { Kind = "target-morale", DeclarationKey = "flipTarget", Text = "雅典娜：选择翻转的1张士气", ValidChoices = player.Morale.Select(card => card.InstanceId).ToList(), MinChoose = 1, MaxChoose = 1 },
                    new L12ActivationSelectionStep { Kind = "field-legion", DeclarationKey = "buffTargets", Text = "雅典娜：选择我方前排最多2张奥林匹斯军团", ValidChoices = player.Field[0].Where(card => card is not null && IsFieldLegion(card) && L12StructuredCardRules.HasFaction(player, card, "olympus")).Select(card => card!.InstanceId).ToList(), MinChoose = 0, MaxChoose = 2 },
                ]);
            case "telemachusTopThree":
                if (source.Tapped) return CommandResult.Reject("特勒马科斯必须为活跃状态");
                return BeginPendingActivationSequence(controller, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "option", DeclarationKey = "mode",
                        Text = "特勒马科斯：主动休整，查看牌库顶部3张牌，可选择其中1张【远程】军团或【奥林匹斯】战术卡，展示并加入手牌，其余卡牌自选顺序全部返回牌库顶部或全部返回牌库底部。",
                        ValidChoices = ["mode:use"], MinChoose = 1, MaxChoose = 1,
                        CancellationPolicy = L12ActivationCancellationPolicy.SeparateChoice,
                        ChoiceLabels = new() { ["mode:use"] = "发动" },
                    },
                ]);
            case "nuadaReadyMorale":
            {
                var rested = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).ToList();
                if (player.SpecialZones.Runes < 2)
                    return CommandResult.Reject("需要2符文");
                return BeginPendingActivationSequence(controller, source, ability,
                [new L12ActivationSelectionStep { Kind = "target-morale", DeclarationKey = "readyTargets", Text = "银臂努阿达：选择转为活跃的最多2张士气", ValidChoices = rested, MinChoose = 0, MaxChoose = 2, AutoSelectWhenExact = true, CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed }]);
            }
            case "skyCityDiscount":
                return CommitActiveAbility(controller, source, ability, null);
            default:
                return null;
        }
    }

    private string? ValidateStarterRemainingActiveDeclaration(int controller, L12CardInstance source,
        string ability, string? target)
    {
        var player = State.Players[controller];
        var values = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        switch (ability)
        {
            case "oasisDancerBuff":
            case "christinaFreeTactic":
            case "kaneMillOne":
                return values.Length == 0 && !source.Tapped ? null : $"{source.Name}必须为活跃状态且无需选择目标";
            case "oiranTransfer":
                return values.Length == 2 && !source.Tapped
                    && DeclaredEnemyTarget(controller, values[0]) is not null
                    && FindOnField(player, values[1], out _, out _) is { } own && IsFieldLegion(own)
                    ? null : "吉原的花魁选择的军团已失效";
            case "lightSwordActive":
            {
                if (source.Tapped || values.Length is < 2 or > 3) return "光之剑必须活跃并完成费用与效果选择";
                if (values[0] is not ("mode:buff" or "mode:rune")) return "光之剑选择的效果无效";
                if (!player.Hand.Any(card => card.InstanceId == values[1])) return "光之剑选择的弃牌已失效";
                if (values[0] == "mode:buff" && (values.Length != 3
                    || FindOnField(player, values[2], out var row, out _) is not { } lightTarget || row != 0
                    || !IsFieldLegion(lightTarget) || !L12StructuredCardRules.HasFaction(player, lightTarget, "otherworld")))
                    return "光之剑选择的前排彼界军团已失效";
                return null;
            }
            case "horusRevive":
            {
                if (values.Length != 4 || values.Take(2).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
                    return "荷鲁斯的费用、墓地军团或位置选择不完整";
                var costs = values.Take(2).Select(id => FindOnField(player, id, out _, out _)).ToArray();
                if (costs.Any(card => card is null || !IsFieldLegion(card))) return "荷鲁斯选择的弃置军团已失效";
                if (!player.Graveyard.Any(card => card.InstanceId == values[2] && card.CardType == "legion"
                        && card.BaseTroops <= 2000 && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")))
                    return "荷鲁斯选择的墓地军团已失效";
                var (row, slot) = ParseSlot(values[3]);
                if (row is < 0 or > 1 || slot is < 0 or > 2) return "荷鲁斯选择的登场位置无效";
                var occupant = player.Field[row][slot];
                if (occupant is not null && !values.Take(2).Contains(occupant.InstanceId, StringComparer.OrdinalIgnoreCase))
                    return "荷鲁斯选择的登场位置已被其他军团占用";
                return null;
            }
            case "sifCycle":
                return values.Length == 3 && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3
                    && values.All(id => player.Graveyard.Any(card => card.InstanceId == id
                        && L12StructuredCardRules.HasFaction(player, card, "asgard") && CanEnterHandOrLibrary(card)))
                    ? null : "西芙选择的墓地卡牌已失效";
            case "athenaFrontBuff":
                if (values.Length is < 2 or > 4 || !player.Hand.Any(card => card.InstanceId == values[0])
                    || !player.Morale.Any(card => card.InstanceId == values[1])) return "雅典娜选择的弃牌费用或士气已失效";
                if (values.Skip(2).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length - 2
                    || values.Skip(2).Any(id => player.Field[0].All(card => card?.InstanceId != id
                        || !IsFieldLegion(card) || !L12StructuredCardRules.HasFaction(player, card, "olympus"))))
                    return "雅典娜选择的前排奥林匹斯军团已失效";
                return null;
            case "nuadaReadyMorale":
                return player.SpecialZones.Runes >= 2 && values.Length <= 2
                    && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length
                    && values.All(id => player.Morale.Any(card => card.InstanceId == id && card.Tapped))
                    ? null : "银臂努阿达选择的符文费用或休整士气已失效";
            case "skyCityDiscount":
                return values.Length == 0 ? null : "探寻天空之城无需选择目标";
            case "telemachusTopThree":
                return values is ["mode:use"] && !source.Tapped
                    ? null : "特勒马科斯必须为活跃状态并确认发动";
            default:
                return null;
        }
    }

    private CommandResult? TryCommitStarterRemainingActiveAbility(int controller, L12CardInstance source,
        string ability, string? target, string onceKey)
    {
        if (ability is not ("oasisDancerBuff" or "christinaFreeTactic" or "kaneMillOne" or "oiranTransfer" or "lightSwordActive" or "horusRevive" or "sifCycle" or "athenaFrontBuff" or "nuadaReadyMorale" or "skyCityDiscount" or "telemachusTopThree"))
            return null;
        var player = State.Players[controller];
        var values = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (ValidateStarterRemainingActiveDeclaration(controller, source, ability, target) is { } error)
            return CommandResult.Reject(error);
        switch (ability)
        {
            case "oasisDancerBuff":
            case "christinaFreeTactic":
            case "kaneMillOne":
            case "oiranTransfer":
            case "lightSwordActive":
                source.Tapped = true;
                AddEvent("cost", controller, $"{source.Name}主动休整", source);
                if (ability == "lightSwordActive")
                {
                    MoveHandToGrave(player, values[1], causedByEffect: false);
                    AddEvent("cost", controller, "光之剑弃置1张手牌", source);
                }
                break;
            case "horusRevive":
                if (!TryConsumeMorale(player, 1)) return CommandResult.Reject("需要消耗1士气");
                var costs = values.Take(2).Select(id => FindOnField(player, id, out _, out _)!).ToArray();
                foreach (var cost in costs)
                    if (!RemoveFromField(player, cost, true, "作为荷鲁斯效果的费用弃置", leaveKind: L12FieldLeaveKind.Discard))
                        return CommandResult.Reject("荷鲁斯选择的弃置军团已失效");
                AddEvent("cost", controller, "荷鲁斯消耗1士气并弃置2张我方军团", [source, .. costs]);
                break;
            case "sifCycle":
                MoveGraveToLibraryBottom(player, values.Select(id => player.Graveyard.First(card => card.InstanceId == id)));
                AddEvent("cost", controller, "西芙将墓地3张阿斯加德卡牌依序返回牌库底部", source);
                break;
            case "athenaFrontBuff":
                var discard = player.Hand.First(card => card.InstanceId == values[0]);
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false);
                player.HandDiscardedByMasterThisTurn = true;
                AddEvent("cost", controller, "雅典娜弃置1张手牌", source, discard);
                break;
            case "nuadaReadyMorale":
                if (!L12S2ZoneOps.SpendRunes(player, 2)) return CommandResult.Reject("需要消耗2符文");
                AddEvent("cost", controller, "银臂努阿达消耗2符文", source);
                break;
            case "telemachusTopThree":
                if (source.Tapped) return CommandResult.Reject("特勒马科斯必须为活跃状态");
                source.Tapped = true;
                AddEvent("cost", controller, "特勒马科斯主动休整", source);
                break;
        }
        if (ability is not ("telemachusTopThree" or "oasisDancerBuff" or "christinaFreeTactic" or "kaneMillOne" or "oiranTransfer" or "lightSwordActive")) player.UsedAbilities.Add(onceKey);
        var data = new Dictionary<string, string> { ["ability"] = ability };
        if (values.Length > 0) data["target"] = string.Join('|', values);
        IEnumerable<string>? publicTargets = null;
        if (ability == "athenaFrontBuff")
        {
            data["compositePlan"] = "starter-athena-active";
            data["compositeSegment"] = "0";
            data["atomicFlow"] = "athena-morale-flip";
            data["atomicContinuation"] = "true";
            data["declared:flipTarget"] = values[1];
            data["declared:buffTargets"] = string.Join('|', values.Skip(2));
            publicTargets = values.Skip(1);
        }
        PushEffect(controller, source, "active", "主动效果", publicTargets, data);
        return CommandResult.Ok();
    }

    private bool TryResolveStarterRemainingActiveEffect(L12StackItem item, L12CardInstance? source,
        string ability)
    {
        if (ability is not ("oasisDancerBuff" or "christinaFreeTactic" or "kaneMillOne" or "oiranTransfer" or "lightSwordActive" or "horusRevive" or "sifCycle" or "athenaFrontBuff" or "nuadaReadyMorale" or "skyCityDiscount" or "telemachusTopThree"))
            return false;
        var player = State.Players[item.Controller];
        var values = item.Data.GetValueOrDefault("target", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);
        switch (ability)
        {
            case "oasisDancerBuff":
                foreach (var legion in PublicLegions(player).Where(card => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")))
                    AddTimedModifier(legion, 1000, 0, State.TurnSerial, source?.Name ?? "绿洲的舞女");
                AddEvent("effect", item.Controller, "绿洲的舞女使我方所有【太阳城】军团本回合兵力+1000", source is null ? [] : [source]);
                break;
            case "christinaFreeTactic":
                player.UsedAbilities.Add($"starter-christina-free-tactic:{State.TurnSerial}");
                AddEvent("effect", item.Controller, "本回合从手牌打出的下1张费用不高于3的主动战术无需消耗费用，改为对我方主宰造成1点伤害", source is null ? [] : [source]);
                break;
            case "kaneMillOne":
                Mill(player, 1, "送葬者凯恩主动效果");
                AddEvent("effect", item.Controller, "送葬者凯恩弃置我方牌库顶部1张牌", source is null ? [] : [source]);
                break;
            case "oiranTransfer":
            {
                var enemy = DeclaredEnemyTarget(item.Controller, values.ElementAtOrDefault(0));
                var own = FindOnField(player, values.ElementAtOrDefault(1), out _, out _);
                if (enemy is not null) AddTimedModifier(enemy, -1000, 0, State.TurnSerial, source?.Name ?? "吉原的花魁");
                if (own is not null && IsFieldLegion(own)) AddTimedModifier(own, 1000, 0, State.TurnSerial, source?.Name ?? "吉原的花魁");
                AddEvent("effect", item.Controller, "吉原的花魁使所选对方军团本回合兵力-1000，并使所选我方军团本回合兵力+1000", source is null ? [] : [source]);
                break;
            }
            case "lightSwordActive":
                if (values.ElementAtOrDefault(0) == "mode:rune")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    AddEvent("runes", item.Controller, "光之剑使我方获得1符文", source is null ? [] : [source]);
                }
                else if (FindOnField(player, values.ElementAtOrDefault(2), out var row, out _) is { } target
                         && row == 0 && L12StructuredCardRules.HasFaction(player, target, "otherworld"))
                {
                    AddTimedModifier(target, 2000, 0, State.TurnSerial, source?.Name ?? "光之剑");
                    AddEvent("effect", item.Controller, $"光之剑使〈{target.Name}〉本回合兵力+2000", target);
                }
                break;
            case "horusRevive":
                _ = TrySummonFromAnyPrivateZone(player, item.Controller, values.ElementAtOrDefault(2) ?? string.Empty,
                    values.ElementAtOrDefault(3) ?? string.Empty, tapped: true);
                break;
            case "sifCycle":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "西芙效果抽牌时牌库为空");
                break;
            case "athenaFrontBuff":
                // New declarations are split by TryResolveStarterRemainingEffect. Keep this
                // fallback for serialized games created before the structured plan existed.
                if (item.Data.ContainsKey("compositePlan")) return false;
                ResolveStarterAthenaMoraleFlip(item, source, values.ElementAtOrDefault(1));
                ResolveStarterAthenaFrontBuff(item, source, values.Skip(2));
                break;
            case "nuadaReadyMorale":
                foreach (var id in values)
                    if (player.Morale.FirstOrDefault(card => card.InstanceId == id) is { Tapped: true } target)
                        target.Tapped = false;
                AddEvent("effect", item.Controller, $"银臂努阿达将{values.Length}张士气转为活跃", source is null ? [] : [source]);
                break;
            case "skyCityDiscount":
                player.NextOtherworldLegionEntryDiscount++;
                AddEvent("effect", item.Controller, "本回合下一张彼界军团登场费用-1", source is null ? [] : [source]);
                break;
            case "telemachusTopThree":
            {
                var top = player.Library.Take(3).ToArray();
                if (top.Length == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                item.Data["telemachusTop"] = string.Join('|', top.Select(card => card.InstanceId));
                var choices = top.Where(card => card.CardType == "legion"
                            && L12StructuredCardRules.HasAnyRowRangeBonus(card)
                        || card.CardType is "tactic" or "counter-tactic"
                            && L12StructuredCardRules.HasFaction(player, card, "olympus"))
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0)
                {
                    BeginAllTopBottomReorder(item, "telemachus", top.Select(card => card.InstanceId),
                        "特勒马科斯：调整其余卡牌的顺序，然后将其全部返回牌库顶部或全部返回牌库底部");
                    return true;
                }
                var data = new Dictionary<string, string>
                {
                    ["displayCardIds"] = string.Join('|', top.Select(card => card.InstanceId)),
                };
                foreach (var card in top) AddPromptCardData(data, card);
                CreateDelayedPublicResolutionPrompt(item, "card",
                    "特勒马科斯：选择1张【远程】军团或【奥林匹斯】战术卡，展示并加入手牌",
                    choices, "starter-telemachus-pick", data, isPrivate: true, min: 1, max: 1);
                return true;
            }
        }
        FinishStackItem(item);
        return true;
    }

    private bool TryBeginStarterRemainingTriggerDeclaration(L12TriggerCandidate candidate,
        L12CardInstance source, string plan)
    {
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var steps = new List<L12ActivationSelectionStep>();
        List<string> OptionalModes(bool canUse) => canUse ? ["mode:none", "mode:use"] : ["mode:none"];

        switch (plan)
        {
            case "zhaoyun-enter-charge":
            case "zhaoyun-kill-piercing":
            case "crossbow-ready":
            case "wangzhaojun-draw":
            {
                if (plan == "zhaoyun-kill-piercing" && candidate.Data.GetValueOrDefault("killed") != "true")
                {
                    RemoveUnstackedTriggerCandidate(candidate, "赵云本次进攻没有完成击杀");
                    return true;
                }
                var onceKey = plan == "crossbow-ready" ? $"trigger:starter-crossbow-ready:{source.InstanceId}:{State.TurnSerial}" : null;
                var canUse = player.Morale.Count > 0 && (onceKey is null || !player.UsedAbilities.Contains(onceKey));
                var effectText = plan switch
                {
                    "zhaoyun-enter-charge" => "赵云：是否返还1张士气并获得冲锋？",
                    "zhaoyun-kill-piercing" => "赵云：是否返还1张士气，以剩余兵力对对方主宰发动1次贯穿进攻？",
                    "crossbow-ready" => "诸葛连弩兵：是否返还1张士气，将此军团转为活跃？",
                    _ => "王昭君：是否返还1张士气并抽取1张牌？",
                };
                steps.Add(StarterStep("option", "mode", effectText, OptionalModes(canUse)));
                steps.Add(StarterStep("target-morale", "returnCost", $"{source.Name}：选择返还的1张士气",
                    player.Morale.Select(card => card.InstanceId), requiredChoice: "mode:use"));
                if (onceKey is not null) candidate.Data["onceKey"] = onceKey;
                break;
            }
            case "kane-enter-mill":
                steps.Add(StarterStep("option", "mode", "送葬者凯恩：是否弃置我方牌库顶部2张牌？",
                    OptionalModes(player.Library.Count > 0)));
                break;
            case "hidden-pass-summon":
            {
                var entries = player.Hand.Where(card => card.CardType == "legion" && card.CurrentCost <= 4
                        && L12StructuredCardRules.HasFaction(player, card, "tianting"))
                    .Select(card => card.InstanceId).ToList();
                var slots = EmptySlots(player).ToList();
                var canUse = player.Morale.Count > 0 && entries.Count > 0 && slots.Count > 0;
                steps.Add(StarterStep("option", "mode",
                    "暗度陈仓：是否返还1张士气，从我方手牌中将1张费用不高于4的【天廷】军团活跃登场？",
                    OptionalModes(canUse)));
                steps.Add(StarterStep("target-morale", "returnCost", "暗度陈仓：选择返还的1张士气",
                    player.Morale.Select(card => card.InstanceId), requiredChoice: "mode:use"));
                steps.Add(StarterStep("hand-card", "entryCard",
                    "暗度陈仓：选择手牌中1张费用不高于4的【天廷】军团",
                    entries, requiredChoice: "mode:use"));
                steps.Add(StarterStep("unused-slot", "entrySlot", "暗度陈仓：选择该军团活跃登场的位置",
                    slots, requiredChoice: "mode:use"));
                break;
            }
            case "change-rested-morale":
                steps.Add(StarterStep("option", "mode",
                    "嫦娥：是否从士气牌库追加1张休整的士气？",
                    OptionalModes(player.MoraleDeck.Count > 0)));
                break;
            case "tomb-defender-debuff":
            {
                var target = DeclaredEnemyTarget(candidate.Controller,
                    candidate.Data.GetValueOrDefault("target"));
                steps.Add(StarterStep("option", "mode",
                    target is null
                        ? "陵墓的守卫者：触发目标已经离场"
                        : $"陵墓的守卫者：是否使〈{target.Name}〉本回合兵力-3000？",
                    OptionalModes(target is not null)));
                break;
            }
            case "kojiro-discard":
                if (player.Hand.Count > opponent.Hand.Count || opponent.Hand.Count == 0)
                {
                    RemoveUnstackedTriggerCandidate(candidate,
                        "佐佐木小次郎进攻时双方手牌数量不符合条件，未生成空堆叠项");
                    return true;
                }
                candidate.Data["starterConditionLocked"] = "true";
                candidate.Data["declaration-complete"] = "true";
                return false;
            case "kojiro-death-kill":
            {
                var targets = PublicLegions(opponent).Where(card => card.BaseTroops <= 2000)
                    .Select(card => card.InstanceId).ToList();
                steps.Add(StarterSelectionStep("field-legion", "enemyTargets",
                    "佐佐木小次郎：可选择对方最多2张原本兵力不高于2000的军团击杀",
                    targets, 0, 2, opponent.PlayerIndex, autoSelectWhenExact: targets.Count == 0));
                break;
            }
            case "kai-master-waiver":
                candidate.Data["declaration-complete"] = "true";
                return false;
            case "kagutsuchi-buff":
            {
                var target = FindOnField(player, candidate.Data.GetValueOrDefault("target"), out _, out _);
                var moraleCost = player.MasterMoraleWaiverUntilTurn >= State.TurnSerial ? 0 : 2;
                var modes = new List<string>();
                if (target is not null && ActiveResourceCount(player) >= moraleCost) modes.Add("mode:morale");
                if (target is not null && player.Hand.Count > 0) modes.Add("mode:discard");
                modes.Add("mode:none");
                var visibleCost = Math.Max(0, moraleCost - player.TemporaryMorale);
                var resources = player.Morale.Where(card => !card.Tapped).Select(card => card.InstanceId)
                    .Concat(ActiveTombGuardResources(player).Select(card => card.InstanceId)).ToList();
                steps.Add(StarterSelectionStep("option", "mode",
                    target is null ? "火之迦具土：触发目标已经离场" : $"火之迦具土：是否使〈{target.Name}〉本回合兵力+2000？",
                    modes, 1, 1, labels: new()
                    {
                        ["mode:none"] = "不发动",
                        ["mode:morale"] = moraleCost == 0 ? "无需消耗士气" : "消耗2士气",
                        ["mode:discard"] = "弃置1张手牌",
                    }));
                steps.Add(new L12ActivationSelectionStep
                {
                    Kind = "resource-payment", DeclarationKey = "moraleCost",
                    Text = "火之迦具土：选择用于支付2士气的资源", ValidChoices = resources,
                    MinChoose = visibleCost, MaxChoose = visibleCost, RequiredDeclaredChoice = "mode:morale",
                    AutoSelectWhenExact = resources.Count == visibleCost,
                    CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
                });
                steps.Add(StarterStep("hand-card", "discardCost", "火之迦具土：选择弃置的1张手牌",
                    player.Hand.Select(card => card.InstanceId), requiredChoice: "mode:discard"));
                break;
            }
            case "aeneas-promotion-search":
                // 牌库中的具体远程军团是隐藏信息；此时只声明是否发动，身份延迟到结算。
                steps.Add(StarterStep("option", "mode",
                    "埃涅阿斯：是否在结算时查看牌库，选择最多2张远程军团活跃登场？",
                    OptionalModes(player.Library.Count > 0 && EmptySlots(player).Any())));
                break;
            case "nuada-rune-buff":
            {
                var targets = PublicLegions(player)
                    .Where(card => L12StructuredCardRules.HasFaction(player, card, "otherworld"))
                    .Select(card => card.InstanceId).ToList();
                steps.Add(StarterStep("option", "mode",
                    "银臂努阿达：是否使我方1张【彼界】军团本回合兵力+1000？",
                    OptionalModes(targets.Count > 0)));
                steps.Add(StarterStep("field-legion", "buffTarget", "银臂努阿达：选择本回合兵力+1000的彼界军团",
                    targets, requiredChoice: "mode:use"));
                break;
            }
            case "akhenaten-death-heal":
            {
                steps.Add(StarterStep("option", "mode",
                    "阿肯那顿：是否弃置1张手牌，使我方主宰增加1点血量？",
                    OptionalModes(player.Hand.Count > 0)));
                steps.Add(StarterStep("hand-card", "discardCost", "阿肯那顿：选择弃置的1张手牌",
                    player.Hand.Select(card => card.InstanceId), requiredChoice: "mode:use"));
                break;
            }
            case "light-sword-enter-kill":
            {
                var targets = PublicLegions(opponent).Where(card => card.BaseTroops <= 2000)
                    .Select(card => card.InstanceId).ToList();
                steps.Add(StarterStep("option", "mode",
                    "光之剑：是否击杀对方最多2张原本兵力不高于2000的军团？",
                    OptionalModes(targets.Count > 0)));
                steps.Add(StarterSelectionStep("field-legion", "enemyTargets",
                    "光之剑：选择对方最多2张原本兵力不高于2000的军团",
                    targets, 0, 2, opponent.PlayerIndex, requiredChoice: "mode:use"));
                break;
            }
            case "sky-city-completion":
                steps.Add(StarterSelectionStep("option", "runeMode",
                    "探寻天空之城：是否获得2符文？", ["mode:none", "mode:use"], 1, 1,
                    labels: new() { ["mode:none"] = "不获得符文", ["mode:use"] = "获得2符文" }));
                steps.Add(StarterSelectionStep("option", "healMode",
                    "探寻天空之城：是否使我方主宰增加2点血量？", ["mode:none", "mode:use"], 1, 1,
                    labels: new() { ["mode:none"] = "不增加血量", ["mode:use"] = "增加2点血量" }));
                steps.Add(StarterSelectionStep("option", "drawMode",
                    "探寻天空之城：是否抽取1张牌？", ["mode:none", "mode:use"], 1, 1,
                    labels: new() { ["mode:none"] = "不抽牌", ["mode:use"] = "抽取1张牌" }));
                break;
            default:
                return false;
        }

        var result = BeginPendingActivationSequence(candidate.Controller, source,
            "public-trigger-declaration", steps, candidate.CandidateId);
        if (!result.Accepted)
            RemoveUnstackedTriggerCandidate(candidate, result.Error ?? $"〈{source.Name}〉当前无法发动");
        return true;
    }

    private bool TryCompleteStarterRemainingTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation, string plan)
    {
        var player = State.Players[candidate.Controller];
        var source = FindAuthoritativeCard(candidate.SourceInstanceId) ?? candidate.SourceSnapshot
            ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        var optionalDeclined = plan is "zhaoyun-enter-charge" or "zhaoyun-kill-piercing" or "crossbow-ready"
                or "wangzhaojun-draw" or "kane-enter-mill" or "hidden-pass-summon"
                or "change-rested-morale" or "tomb-defender-debuff"
                or "kagutsuchi-buff" or "aeneas-promotion-search" or "nuada-rune-buff"
                or "akhenaten-death-heal" or "light-sword-enter-kill"
            && mode == "mode:none";
        if (plan == "sky-city-completion")
        {
            var selectedSegments = new[]
                {
                    ("runeMode", "runes"), ("healMode", "heal"), ("drawMode", "draw"),
                }
                .Where(pair => activation.DeclaredValues.GetValueOrDefault(pair.Item1, []).SingleOrDefault() == "mode:use")
                .Select(pair => pair.Item2).ToArray();
            if (selectedSegments.Length == 0)
                optionalDeclined = true;
            else
            {
                candidate.Data["trialCompletionPlan"] = "sky-city";
                candidate.Data["skySegments"] = string.Join('|', selectedSegments);
                candidate.Data["trialSegment"] = "0";
                candidate.Data["stackText"] = StarterSkySegmentText(selectedSegments[0]);
            }
        }
        if (optionalDeclined)
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                $"〈{candidate.SourceName}〉的可选效果未发动，未进入堆叠");
            AdvanceTriggerBatches();
            AdvanceCombatTimelineIfIdle();
            return true;
        }

        string? error = null;
        switch (plan)
        {
            case "zhaoyun-enter-charge":
            case "zhaoyun-kill-piercing":
            case "crossbow-ready":
            case "wangzhaojun-draw":
            {
                var ids = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
                if (ids.Count != 1 || !CanReturnSelectedMoraleById(player, ids, 1))
                    error = $"{source.Name}选择的返还士气已失效；未支付费用且效果未入栈";
                break;
            }
            case "tomb-defender-debuff":
                if (DeclaredEnemyTarget(candidate.Controller, candidate.Data.GetValueOrDefault("target")) is null)
                    error = "陵墓的守卫者对应的位移军团已离场；效果未入栈";
                break;
            case "kojiro-death-kill":
            {
                var targets = activation.DeclaredValues.GetValueOrDefault("enemyTargets", []);
                if (targets.Count > 2 || targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count
                    || targets.Any(id => DeclaredEnemyTarget(candidate.Controller, id,
                        card => card.BaseTroops <= 2000) is null))
                    error = "佐佐木小次郎选择的军团已失效；效果未入栈";
                break;
            }
            case "kagutsuchi-buff":
            {
                var target = FindOnField(player, candidate.Data.GetValueOrDefault("target"), out _, out _);
                var moraleCost = player.MasterMoraleWaiverUntilTurn >= State.TurnSerial ? 0 : 2;
                if (target is null) error = "火之迦具土对应的军团已离场；未支付费用且效果未入栈";
                else if (mode == "mode:morale" && !CanConsumeSelectedResources(player, moraleCost,
                             activation.DeclaredValues.GetValueOrDefault("moraleCost", [])))
                    error = "火之迦具土选择的支付资源已失效；未支付费用且效果未入栈";
                else if (mode == "mode:discard" && !player.Hand.Any(card => card.InstanceId ==
                             activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault()))
                    error = "火之迦具土选择的弃牌已失效；未支付费用且效果未入栈";
                else if (mode is not ("mode:morale" or "mode:discard"))
                    error = "火之迦具土的费用方式无效；效果未入栈";
                break;
            }
            case "nuada-rune-buff":
                if (FindOnField(player,
                        activation.DeclaredValues.GetValueOrDefault("buffTarget", []).SingleOrDefault(), out _, out _)
                    is not { } nuadaTarget || !L12StructuredCardRules.HasFaction(player, nuadaTarget, "otherworld"))
                    error = "银臂努阿达选择的彼界军团已失效；效果未入栈";
                break;
            case "akhenaten-death-heal":
                if (!player.Hand.Any(card => card.InstanceId ==
                        activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault()))
                    error = "阿肯那顿选择的弃牌已失效；未支付费用且效果未入栈";
                break;
            case "light-sword-enter-kill":
            {
                var targets = activation.DeclaredValues.GetValueOrDefault("enemyTargets", []);
                if (targets.Count > 2 || targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count
                    || targets.Any(id => DeclaredEnemyTarget(candidate.Controller, id,
                        card => card.BaseTroops <= 2000) is null))
                    error = "光之剑选择的军团已失效；效果未入栈";
                break;
            }
            case "hidden-pass-summon":
            {
                var returnCost = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
                var entryId = activation.DeclaredValues.GetValueOrDefault("entryCard", []).SingleOrDefault();
                var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
                if (returnCost.Count != 1 || !CanReturnSelectedMoraleById(player, returnCost, 1)
                    || !player.Hand.Any(card => card.InstanceId == entryId && card.CardType == "legion"
                        && card.CurrentCost <= 4 && L12StructuredCardRules.HasFaction(player, card, "tianting"))
                    || string.IsNullOrWhiteSpace(slot)
                    || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                    error = "暗度陈仓选择的士气、手牌军团或登场位置已失效；未返还士气且效果未入栈";
                break;
            }
        }
        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            AdvanceCombatTimelineIfIdle();
            return true;
        }

        candidate.Data["declaration-committing"] = "true";
        if (plan is "zhaoyun-enter-charge" or "zhaoyun-kill-piercing" or "crossbow-ready" or "wangzhaojun-draw")
        {
            var ids = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
            _ = ReturnSelectedMoraleById(player, ids, 1);
            AddEvent("cost", candidate.Controller, $"{source.Name}返还1张士气", source);
        }
        if (plan == "kagutsuchi-buff")
        {
            var moraleCost = player.MasterMoraleWaiverUntilTurn >= State.TurnSerial ? 0 : 2;
            if (mode == "mode:morale")
                _ = TryConsumeSelectedResources(player, moraleCost,
                    activation.DeclaredValues.GetValueOrDefault("moraleCost", []));
            else
            {
                var discard = player.Hand.First(card => card.InstanceId ==
                    activation.DeclaredValues.GetValueOrDefault("discardCost", []).Single());
                MoveHandToGrave(player, discard.InstanceId, causedByEffect: false);
            }
            AddEvent("cost", candidate.Controller,
                mode == "mode:morale"
                    ? moraleCost == 0 ? "火之迦具土本次无需消耗士气" : "火之迦具土消耗2士气"
                    : "火之迦具土弃置1张手牌", source);
        }
        else if (plan == "akhenaten-death-heal")
        {
            var discard = activation.DeclaredValues.GetValueOrDefault("discardCost", []).Single();
            MoveHandToGrave(player, discard, causedByEffect: false);
            AddEvent("cost", candidate.Controller, "阿肯那顿弃置1张手牌", source);
        }
        else if (plan == "hidden-pass-summon")
        {
            var returnCost = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
            _ = ReturnSelectedMoraleById(player, returnCost, 1);
            AddEvent("cost", candidate.Controller, "暗度陈仓返还1张士气", source);
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        if (plan == "aeneas-promotion-search")
        {
            candidate.Data["compositePlan"] = "starter-aeneas-promotion";
            candidate.Data["compositeSegment"] = "0";
            candidate.Data["atomicFlow"] = "aeneas-promotion-search";
            candidate.Data["atomicContinuation"] = "true";
        }
        if (candidate.Data.GetValueOrDefault("target") is { Length: > 0 } fixedTarget)
            candidate.Data["declared:fixedTarget"] = fixedTarget;
        if (candidate.Data.GetValueOrDefault("onceKey") is { Length: > 0 } onceKey)
            player.UsedAbilities.Add(onceKey);
        candidate.Data.Remove("declaration-committing");
        candidate.Data["declaration-complete"] = "true";
        CleanupPublicTriggerReservation(candidate);
        AdvanceTriggerBatches();
        return true;
    }

    private L12TriggerCandidate? BuildStarterKagutsuchiCandidate(int controller, L12CardInstance target)
    {
        var player = State.Players[controller];
        var moraleCost = player.MasterMoraleWaiverUntilTurn >= State.TurnSerial ? 0 : 2;
        if (L12StructuredCardRules.StarterRemainingPlan(player.MasterId, "legion-attack-timing")
            != "kagutsuchi-buff" || ActiveResourceCount(player) < moraleCost && player.Hand.Count == 0)
            return null;
        var onceKey = $"trigger:starter-kagutsuchi:{State.TurnSerial}";
        var pendingKey = $"{onceKey}:pending";
        if (player.UsedAbilities.Contains(onceKey) || !player.UsedAbilities.Add(pendingKey)) return null;
        var master = CreateCard(player.MasterId, $"master-{controller}");
        return CreateTriggerCandidate(controller, master, "legion-attack-timing",
            "我方军团进攻或被进攻时效果", new Dictionary<string, string>
            {
                ["target"] = target.InstanceId,
                ["onceKey"] = onceKey,
                ["cleanupReservation"] = pendingKey,
            }, master);
    }

    private void FlushStarterResourceTriggerBatches()
    {
        var candidates = new List<L12TriggerCandidate>();
        foreach (var player in State.Players)
        {
            var returnedEvents = player.PendingStarterMoraleReturnEvents;
            var runeEvents = player.PendingStarterRuneSpendEvents;
            player.PendingStarterMoraleReturnEvents = 0;
            player.PendingStarterRuneSpendEvents = 0;

            if (returnedEvents > 0 && L12StructuredCardRules.StarterRemainingPlan(player.MasterId,
                    "morale-return") == "change-rested-morale")
            {
                var onceKey = $"trigger:starter-change:{State.TurnSerial}";
                var pendingKey = $"{onceKey}:pending";
                if (!player.UsedAbilities.Contains(onceKey) && player.UsedAbilities.Add(pendingKey))
                {
                    var master = CreateCard(player.MasterId, $"master-{player.PlayerIndex}");
                    candidates.Add(CreateTriggerCandidate(player.PlayerIndex, master, "morale-return",
                        "我方返还士气时效果", new Dictionary<string, string>
                        {
                            ["onceKey"] = onceKey, ["cleanupReservation"] = pendingKey,
                        }, master));
                }
            }

            if (runeEvents <= 0 || State.ActivePlayer != player.PlayerIndex
                || L12StructuredCardRules.StarterRemainingPlan(player.MasterId, "rune-spent")
                    != "nuada-rune-buff") continue;
            var nuada = CreateCard(player.MasterId, $"master-{player.PlayerIndex}");
            for (var index = 0; index < runeEvents; index++)
                candidates.Add(CreateTriggerCandidate(player.PlayerIndex, nuada, "rune-spent",
                    "我方消耗符文时效果", sourceSnapshot: nuada));
        }
        QueueTriggerCandidates(candidates);
    }

    private static string StarterSkySegmentText(string segment) => segment switch
    {
        "runes" => "探寻天空之城：获得2符文",
        "heal" => "探寻天空之城：我方主宰增加2点血量",
        "draw" => "探寻天空之城：抽取1张牌",
        _ => "探寻天空之城完成效果",
    };

    private CommandResult BeginStarterDisasterAttackDiscard(int controller, L12CardInstance attacker,
        L12AttackTarget target)
        => BeginPendingActivationSequence(controller, attacker, "starter-disaster-attack",
        [
            new L12ActivationSelectionStep
            {
                Kind = "hand-card",
                DeclarationKey = "discardCost",
                Text = "色欲之罪：选择弃置1张手牌以发动进攻",
                ValidChoices = State.Players[controller].Hand.Select(card => card.InstanceId).ToList(),
                MinChoose = 1,
                MaxChoose = 1,
                ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["attack-target-type"] = target.Type,
                    ["attack-target-id"] = target.InstanceId ?? string.Empty,
                },
            },
        ]);

    private void CompleteStarterDisasterAttackDiscard(L12PendingActivation activation)
    {
        var targetType = activation.SelectionSteps[0].ChoiceLabels.GetValueOrDefault("attack-target-type", "master");
        var targetId = activation.SelectionSteps[0].ChoiceLabels.GetValueOrDefault("attack-target-id");
        var result = Attack(activation.Controller, new L12Command("attack",
            CardInstanceId: activation.SourceInstanceId,
            CardInstanceIds: activation.DeclaredValues.GetValueOrDefault("discardCost", []),
            Target: new L12AttackTarget(targetType, string.IsNullOrWhiteSpace(targetId) ? null : targetId)));
        if (!result.Accepted)
            AddEvent("ability-rejected", activation.Controller, result.Error ?? "进攻弃牌费用已失效");
    }

    private static string? StarterDeclaredOne(L12StackItem item, string key)
        => item.Data.GetValueOrDefault($"declared:{key}")
            ?.Split('|', StringSplitOptions.RemoveEmptyEntries).SingleOrDefault();

    private static string[] StarterDeclaredMany(L12StackItem item, string key)
        => item.Data.GetValueOrDefault($"declared:{key}")
            ?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private int CountGraveFactionLegions(L12PlayerState player, string faction)
        => player.Graveyard.Sum(card => L12StructuredCardRules.StarterGraveFactionLegionCopies(player, card, faction));

    private bool TryResolveStarterRemainingEffect(L12StackItem item)
    {
        var flow = item.Data.GetValueOrDefault("atomicFlow");
        if (flow is null) return false;
        var player = State.Players[item.Controller];
        switch (flow)
        {
            case "zhaoyun-enter-charge":
                if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } zhaoyun)
                {
                    zhaoyun.HasCharge = true;
                    AddEvent("effect", item.Controller, "赵云获得冲锋", zhaoyun);
                }
                FinishStackItem(item);
                return true;
            case "zhaoyun-kill-piercing":
                if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } piercingZhaoyun)
                {
                    player.UsedAbilities.Add($"starter-piercing:{piercingZhaoyun.InstanceId}:{State.TurnSerial}");
                    BeginPiercingAttack(item.Controller, piercingZhaoyun);
                }
                FinishStackItem(item);
                return true;
            case "crossbow-ready":
                if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } crossbow)
                    ReadyCardByEffect(item.Controller, crossbow, crossbow, "诸葛连弩兵因进攻后效果转为活跃");
                FinishStackItem(item);
                return true;
            case "wangzhaojun-draw":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "王昭君效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "kane-enter-mill":
                Mill(player, 2, "送葬者凯恩登场时效果");
                FinishStackItem(item);
                return true;
            case "starter-lust-disaster":
            case "starter-grave-asgard-copies":
            case "kondo-lethal-substitution":
                FinishStackItem(item);
                return true;
            case "change-rested-morale":
            {
                var added = AddMorale(player, 1, tapped: true);
                AddEvent("morale", item.Controller,
                    added > 0 ? "嫦娥使我方追加1张休整的士气" : "士气牌库为空，嫦娥未能追加士气",
                    FindSource(item) is { } change ? [change] : []);
                FinishStackItem(item);
                return true;
            }
            case "tomb-defender-debuff":
            {
                var target = DeclaredEnemyTarget(item.Controller, StarterDeclaredOne(item, "fixedTarget"));
                if (target is not null)
                {
                    AddTimedModifier(target, -3000, 0, State.TurnSerial, item.SourceName);
                    AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合兵力-3000", target);
                }
                else AddEvent("effect-cancelled", item.Controller,
                    "陵墓的守卫者对应的位移军团已离场，本次兵力变化未生效");
                FinishStackItem(item);
                return true;
            }
            case "kojiro-discard":
            {
                var opponent = State.Players[1 - item.Controller];
                if (item.Data.GetValueOrDefault("starterConditionLocked") != "true" || opponent.Hand.Count == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                CreateDelayedPublicResolutionPrompt(item, "hand-card",
                    "佐佐木小次郎：选择弃置1张手牌", opponent.Hand.Select(card => card.InstanceId),
                    "starter-kojiro-discard", new(), isPrivate: true, chooser: opponent.PlayerIndex);
                return true;
            }
            case "kojiro-death-kill":
                foreach (var targetId in StarterDeclaredMany(item, "enemyTargets"))
                    if (DeclaredEnemyTarget(item.Controller, targetId, card => card.BaseTroops <= 2000) is not null)
                        KillTarget(item, targetId, "被佐佐木小次郎的阵亡时效果击杀");
                FinishStackItem(item);
                return true;
            case "kai-master-waiver":
                player.MasterMoraleWaiverUntilTurn = Math.Max(player.MasterMoraleWaiverUntilTurn, State.TurnSerial);
                AddEvent("effect", item.Controller, "本回合我方主宰效果无需消耗士气",
                    FindSource(item) is { } kai ? [kai] : []);
                FinishStackItem(item);
                return true;
            case "kagutsuchi-buff":
            {
                var target = FindOnField(player, StarterDeclaredOne(item, "fixedTarget"), out _, out _);
                if (target is not null)
                {
                    AddTimedModifier(target, 2000, 0, State.TurnSerial, item.SourceName);
                    AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合兵力+2000", target);
                }
                else AddEvent("effect-cancelled", item.Controller,
                    "火之迦具土对应的军团已离场，本次兵力+2000未生效；已支付费用不返还");
                FinishStackItem(item);
                return true;
            }
            case "aeneas-promotion-search":
            {
                var choices = player.Library.Where(card => card.CardType == "legion"
                        && L12StructuredCardRules.HasAnyRowRangeBonus(card))
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0 || !EmptySlots(player).Any())
                {
                    FinishStackItem(item);
                    return true;
                }
                CreateDelayedPublicResolutionPrompt(item, "card",
                    "埃涅阿斯：查看牌库并选择最多2张远程军团活跃登场",
                    choices, "starter-aeneas-search", new(), isPrivate: true,
                    min: 0, max: Math.Min(2, choices.Length));
                return true;
            }
            case "aeneas-promotion-shuffle":
                ShuffleLibrary(player, "埃涅阿斯晋升登场检索结算");
                FinishStackItem(item);
                return true;
            case "athena-morale-flip":
                ResolveStarterAthenaMoraleFlip(item, FindSource(item),
                    StarterDeclaredOne(item, "flipTarget"));
                FinishStackItem(item);
                return true;
            case "athena-front-buff":
                ResolveStarterAthenaFrontBuff(item, FindSource(item),
                    StarterDeclaredMany(item, "buffTargets"));
                FinishStackItem(item);
                return true;
            case "nuada-rune-buff":
            {
                var target = FindOnField(player, StarterDeclaredOne(item, "buffTarget"), out _, out _);
                if (target is not null && L12StructuredCardRules.HasFaction(player, target, "otherworld"))
                {
                    AddTimedModifier(target, 1000, 0, State.TurnSerial, item.SourceName);
                    AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合兵力+1000", target);
                }
                else AddEvent("effect-cancelled", item.Controller,
                    "银臂努阿达选择的彼界军团已离场，本次兵力+1000未生效");
                FinishStackItem(item);
                return true;
            }
            case "akhenaten-death-heal":
                HealMaster(item.Controller, 1, "阿肯那顿阵亡时效果", legionEffect: true);
                FinishStackItem(item);
                return true;
            case "light-sword-enter-kill":
                foreach (var targetId in StarterDeclaredMany(item, "enemyTargets"))
                    if (DeclaredEnemyTarget(item.Controller, targetId, card => card.BaseTroops <= 2000) is not null)
                        KillTarget(item, targetId, "被光之剑的登场时效果击杀");
                FinishStackItem(item);
                return true;
            case "sky-city-completion":
            {
                var segments = item.Data.GetValueOrDefault("skySegments", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var index = int.TryParse(item.Data.GetValueOrDefault("trialSegment"), out var parsed) ? parsed : 0;
                var segment = index >= 0 && index < segments.Length ? segments[index] : string.Empty;
                if (segment == "runes")
                {
                    L12S2ZoneOps.GainRunes(player, 2);
                    AddEvent("runes", item.Controller, "探寻天空之城使我方获得2符文");
                }
                else if (segment == "heal")
                    HealMaster(item.Controller, 2, "探寻天空之城");
                else if (segment == "draw" && !Draw(player, 1))
                    SetWinner(1 - item.Controller, "探寻天空之城效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            }
            case "starter-tomb-guard-revive":
            {
                var targetId = StarterDeclaredOne(item, "entryCard");
                if (targetId is not null && TrySummonFromAnyPrivateZone(player, item.Controller, targetId,
                        StarterDeclaredOne(item, "entrySlot") ?? string.Empty, tapped: false))
                {
                    var summoned = FindOnField(player, targetId, out _, out _);
                    if (summoned is not null)
                    {
                        summoned.DiscardAtEndOfTurnUntilTurn = State.TurnSerial;
                        AddEvent("effect", item.Controller, $"〈{summoned.Name}〉将在本回合结束时弃置", summoned);
                    }
                }
                FinishStackItem(item);
                return true;
            }
            case "legendary-bloodline-base":
            {
                var target = FindOnField(player, StarterDeclaredOne(item, "buffTarget"), out _, out _);
                if (target is not null && L12StructuredCardRules.HasFaction(player, target, "asgard"))
                {
                    AddTimedModifier(target, 2000, 0, State.TurnSerial, item.SourceName);
                    AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合兵力+2000", target);
                }
                else AddEvent("effect-cancelled", item.Controller, "所选阿斯加德军团已离场，本次兵力+2000未生效");
                FinishStackItem(item);
                return true;
            }
            case "legendary-bloodline-grave":
            {
                var target = FindOnField(player, StarterDeclaredOne(item, "buffTarget"), out _, out _);
                if (target is not null && L12StructuredCardRules.HasFaction(player, target, "asgard"))
                {
                    var bonus = CountGraveFactionLegions(player, "asgard") / 3 * 1000;
                    if (bonus > 0)
                    {
                        AddTimedModifier(target, bonus, 0, State.TurnSerial, item.SourceName);
                        AddEvent("effect", item.Controller,
                            $"墓地的阿斯加德军团使〈{target.Name}〉本回合额外兵力+{bonus}", target);
                    }
                    else AddEvent("effect", item.Controller, "墓地中不足3张阿斯加德军团，没有获得额外兵力", target);
                }
                else AddEvent("effect-cancelled", item.Controller, "所选阿斯加德军团已离场，本次按墓地数量增加兵力未生效");
                FinishStackItem(item);
                return true;
            }
            case "invasion-fire":
            {
                var source = FindSource(item);
                var target = FindOnField(player, StarterDeclaredOne(item, "attachTarget"), out _, out _);
                if (source is not null && target is not null
                    && L12StructuredCardRules.HasFaction(player, target, "gaotianyuan")
                    && player.Resolving.Remove(source))
                {
                    source.OwnerIndex ??= item.Controller;
                    target.AttachedCards.Add(source);
                    AddEvent("attach", item.Controller, $"〈{source.Name}〉叠放至〈{target.Name}〉下方；该军团获得强攻", source, target);
                }
                else AddEvent("effect-cancelled", item.Controller, "所选高天原军团已离场，〈侵略如火〉未能叠放");
                FinishStackItem(item);
                return true;
            }
            case "hunter-gift":
            {
                var mode = StarterDeclaredOne(item, "mode");
                var targetId = mode == "mode:shock"
                    ? StarterDeclaredOne(item, "shockTarget")
                    : StarterDeclaredOne(item, "rangedTarget");
                var target = FindOnField(player, targetId, out _, out _);
                if (target is not null && L12StructuredCardRules.HasFaction(player, target, "olympus")
                    && (mode != "mode:ranged" || L12StructuredCardRules.HasAnyRowRangeBonus(target)))
                {
                    if (mode == "mode:shock")
                    {
                        target.ShockDamageBonus = 2000;
                        target.ShockDamageBonusUntilTurn = State.TurnSerial;
                        AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合震击伤害+2000", target);
                    }
                    else
                    {
                        target.AttackOnlyTroopsBonus = 2000;
                        target.AttackOnlyTroopsBonusUntilTurn = State.TurnSerial;
                        AddEvent("effect", item.Controller, $"〈{target.Name}〉本回合进攻时兵力+2000", target);
                    }
                }
                else AddEvent("effect-cancelled", item.Controller, "所选奥林匹斯军团已离场或不再符合条件");
                FinishStackItem(item);
                return true;
            }
            case "telemachus-top-three":
            {
                var top = player.Library.Take(3).ToArray();
                if (top.Length == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                item.Data["telemachusTop"] = string.Join('|', top.Select(card => card.InstanceId));
                var choices = top.Where(card => card.CardType == "legion"
                            && L12StructuredCardRules.HasAnyRowRangeBonus(card)
                        || card.CardType is "tactic" or "counter-tactic"
                            && L12StructuredCardRules.HasFaction(player, card, "olympus"))
                    .Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0)
                {
                    BeginAllTopBottomReorder(item, "telemachus", top.Select(card => card.InstanceId),
                        "特勒马科斯：调整其余卡牌的顺序，然后将其全部返回牌库顶部或全部返回牌库底部");
                    return true;
                }
                var data = new Dictionary<string, string>
                {
                    ["displayCardIds"] = string.Join('|', top.Select(card => card.InstanceId)),
                };
                foreach (var card in top) AddPromptCardData(data, card);
                CreateDelayedPublicResolutionPrompt(item, "card",
                    "特勒马科斯：选择1张【远程】军团或【奥林匹斯】战术卡，展示并加入手牌",
                    choices, "starter-telemachus-pick", data, isPrivate: true, min: 1, max: 1);
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryContinueStarterRemainingEffect(L12StackItem item, L12Prompt prompt,
        IReadOnlyCollection<string> chosen)
    {
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            case "starter-kojiro-discard":
            {
                var opponent = State.Players[1 - item.Controller];
                var target = opponent.Hand.FirstOrDefault(card => chosen.Contains(card.InstanceId,
                    StringComparer.OrdinalIgnoreCase));
                if (target is not null)
                {
                    MoveHandToGrave(opponent, target.InstanceId, causedByEffect: true);
                    AddEvent("discard", opponent.PlayerIndex,
                        $"佐佐木小次郎使对方弃置〈{target.Name}〉", target);
                }
                FinishStackItem(item);
                return true;
            }
            case "starter-aeneas-search":
            {
                var player = State.Players[item.Controller];
                var selected = chosen.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(id => player.Library.Any(card => card.InstanceId == id && card.CardType == "legion"
                        && L12StructuredCardRules.HasAnyRowRangeBonus(card)))
                    .Take(Math.Min(2, EmptySlots(player).Count())).ToArray();
                if (selected.Length == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                BeginQueuedSummons(item, selected, tapped: false,
                    "埃涅阿斯：选择远程军团活跃登场的位置");
                return true;
            }
            case "starter-telemachus-pick":
            {
                var player = State.Players[item.Controller];
                var topIds = item.Data.GetValueOrDefault("telemachusTop", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries);
                var chosenId = chosen.SingleOrDefault();
                if (chosenId is not null && topIds.Contains(chosenId, StringComparer.OrdinalIgnoreCase)
                    && player.Library.FirstOrDefault(card => card.InstanceId == chosenId) is { } selected
                    && (selected.CardType == "legion" && L12StructuredCardRules.HasAnyRowRangeBonus(selected)
                        || selected.CardType is "tactic" or "counter-tactic"
                            && L12StructuredCardRules.HasFaction(player, selected, "olympus")))
                {
                    AddEvent("reveal", item.Controller,
                        $"特勒马科斯展示〈{selected.Name}〉并将其加入手牌", selected);
                    _ = MoveLibraryCardToHandByEffect(player, selected.InstanceId,
                        $"特勒马科斯展示〈{selected.Name}〉并将其加入手牌");
                }
                var remaining = topIds.Where(id => !string.Equals(id, chosenId,
                    StringComparison.OrdinalIgnoreCase) && player.Library.Any(card => card.InstanceId == id)).ToArray();
                if (remaining.Length == 0)
                {
                    FinishStackItem(item);
                    return true;
                }
                BeginAllTopBottomReorder(item, "telemachus", remaining,
                    "特勒马科斯：调整其余卡牌的顺序，然后将其全部返回牌库顶部或全部返回牌库底部");
                return true;
            }
            default:
                return false;
        }
    }

    private void ResolveStarterAthenaMoraleFlip(L12StackItem item, L12CardInstance? source,
        string? moraleId)
    {
        var player = State.Players[item.Controller];
        if (player.Morale.FirstOrDefault(card => card.InstanceId == moraleId) is not { } morale)
        {
            AddEvent("effect-cancelled", item.Controller, "雅典娜选择的士气已离开士气区，本次翻转未生效",
                source is null ? [] : [source]);
            return;
        }
        L12S2ZoneOps.FlipMoraleFace(player, morale.InstanceId, toGodPower: !morale.IsGodPower);
        AddEvent("morale", item.Controller, "雅典娜翻转1张士气", source is null ? [] : [source]);
    }

    private void ResolveStarterAthenaFrontBuff(L12StackItem item, L12CardInstance? source,
        IEnumerable<string> targetIds)
    {
        var player = State.Players[item.Controller];
        var applied = 0;
        foreach (var id in targetIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(2))
            if (FindOnField(player, id, out var row, out _) is { } target && row == 0
                && L12StructuredCardRules.HasFaction(player, target, "olympus"))
            {
                AddTimedModifier(target, 1000, 0, State.TurnSerial, "雅典娜");
                target.MasterAttackDamageBonus = 1;
                target.MasterAttackDamageBonusUntilTurn = State.TurnSerial;
                applied++;
            }
        AddEvent("effect", item.Controller,
            $"雅典娜使{applied}张前排奥林匹斯军团本回合兵力+1000，且对主宰造成的伤害+1",
            source is null ? [] : [source]);
    }
}
