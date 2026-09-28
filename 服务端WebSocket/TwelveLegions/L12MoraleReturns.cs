namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void CreateReturnMoralePrompt(int playerIndex, int count, string continuation, string? stackItemId,
        Dictionary<string, string> data, bool requireActive = false)
    {
        var player = State.Players[playerIndex];
        var choices = player.Morale.Where(card => !requireActive || !card.Tapped).Select(card => card.InstanceId).ToList();
        // Only a pre-stack activation cost is cancellable; effect-stage returns remain mandatory.
        if (continuation == "active-return-choice")
        {
            choices.Add("cancel");
            data["allowCancel"] = "true";
            data["cancel"] = "不发动";
        }
        data["count"] = count.ToString();
        data["requireActive"] = requireActive.ToString();
        data["choiceMode"] = "resource-return";
        foreach (var morale in player.Morale.Where(card => choices.Contains(card.InstanceId)))
        {
            data[$"{morale.InstanceId}:resourceType"] = L12StructuredCardSemantics
                .MoraleZoneResourceRule(morale.CardId)?.ResourceType
                ?? (morale.IsGodPower ? "god-power" : morale.Tapped ? "rested-morale" : "active-morale");
            data[$"{morale.InstanceId}:activityState"] = morale.Tapped ? "rested" : "active";
        }
        var sourceName = stackItemId is null
            ? data.GetValueOrDefault("sourceName")
            : State.EffectStack.Concat(State.DeferredEffectStack)
                .FirstOrDefault(item => item.StackItemId == stackItemId)?.SourceName;
        sourceName = string.IsNullOrWhiteSpace(sourceName) ? "返还士气" : sourceName;
        var consequences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (continuation == "active-return-choice")
            consequences["cancel"] = "不发动当前主动效果，不返还士气。";
        CreatePrompt(playerIndex, "resource-return", "请选择返还的士气", choices, count, count,
            continuation, stackItemId, isPrivate: true,
            data: WithPromptNarrative(data,
                new(sourceName, $"〈{sourceName}〉需要返还{count}张{(requireActive ? "活跃" : "可选")}士气才能继续。",
                    continuation == "active-return-choice"
                        ? $"请选择恰好{count}张符合条件的士气并确认；也可以选择不发动。"
                        : $"请选择恰好{count}张符合条件的士气并确认。",
                    L12PromptWaitingAction.ResourceReturn, consequences)));
    }

    private int GetActiveAbilityReturnMoraleCost(L12PlayerState player, L12CardInstance source, string ability, string? target)
        => ability switch
        {
            "nonLethal" when source.CardId == "S01-01M1" => 4,
            "searchBrothers" when source.CardId == "S01-0105" => 1,
            "artifactDraw" when source.CardId == "S01-0117" => 1,
            "extendedRange" when L12StructuredCardSemantics.ExtendedRangeRule(source.CardId) is { } rangeRule => rangeRule.ReturnMorale,
            "xishiExchange" when source.CardId == "S01-0116" => 1,
            "mengpoSilence" when source.CardId == "S01-01M2" => 1,
            "shennongReset" when source.CardId == "S02-0104" => 1,
            "palaceExchange" when source.CardId == "S01-01D1"
                => DeclaredEnemyTarget(player.PlayerIndex, PublicDeclaredEnemyId(target))?.CurrentCost ?? 0,
            _ => 0,
        };

    private static bool ActiveReturnRequiresActiveMorale(L12CardInstance source, string ability)
        => source.CardId == "S01-0117" && ability == "artifactDraw";

    private string? ValidateActiveReturnPrepayment(int playerIndex, L12CardInstance source, string ability, string? target)
    {
        var player = State.Players[playerIndex];
        if (ability == "extendedRange" && L12StructuredCardSemantics.HasBackRowExtendedRangeActive(source.CardId))
            return ExtendedRangeSourceUnavailableReason(player, source);
        return ability switch
        {
            "searchBrothers" when source.CardId == "S01-0105" && source.Tapped
                => "刘备必须为活跃状态",
            "artifactDraw" when source.CardId == "S01-0117" && source.Tapped
                => "山河社稷图必须为活跃状态",
            "xishiExchange" when source.CardId == "S01-0116" && !IsValidXishiDeclaration(player, source, target)
                => "声明的手牌目标、战场或位置不再合法",
            "palaceExchange" when source.CardId == "S01-01D1" && source.Tapped
                => "凌霄宝殿必须为活跃状态",
            "palaceExchange" when source.CardId == "S01-01D1"
                && DeclaredEnemyTarget(playerIndex, PublicDeclaredEnemyId(target)) is null
                => "目标不再合法",
            "mengpoSilence" when source.CardId == "S01-01M2" && !string.IsNullOrWhiteSpace(target)
                && DeclaredEnemyTarget(playerIndex, target) is null
                => "目标不再合法",
            "shennongReset" when source.CardId == "S02-0104" && source.Tapped
                => "神农鼎必须为活跃状态",
            "shennongReset" when source.CardId == "S02-0104"
                && (string.IsNullOrWhiteSpace(target)
                    || UsedMasterAbilityUsageKey(player, target) is null)
                => "所选主宰效果已不再处于使用过的状态",
            _ => null,
        };
    }

    private bool IsValidXishiDeclaration(L12PlayerState player, L12CardInstance source, string? target)
    {
        var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (declared.Length == 0) return true;
        if (declared.Length != 3) return false;
        var handCard = player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]
            && card.CardType == "legion" && card.CardId != "S01-0116" && card.Troops <= 2000);
        var battlefield = ParseEffectEntryBattlefieldChoice(declared[1]);
        var (row, slot) = ParseSlot(declared[2]);
        var usingSourceSlot = battlefield == player.PlayerIndex
            && SourceSlotAfterCost(player.PlayerIndex, source.InstanceId) == declared[2];
        return handCard is not null && battlefield is not null
            && (usingSourceSlot || EffectEntryBattlefieldChoices(player.PlayerIndex, handCard).Contains(battlefield.Value))
            && row is >= 0 and <= 1 && slot is >= 0 and <= 2
            && (State.ActiveDisaster?.CardId != "S01-DS03" || row == 0)
            && (usingSourceSlot || State.Players[battlefield.Value].Field[row][slot] is null);
    }

    private bool BeginEffectMoraleReturn(L12StackItem item, int count, string afterReturn,
        Dictionary<string, string>? extra = null, bool requireActive = false)
    {
        var player = State.Players[item.Controller];
        var eligible = player.Morale.Count(card => !requireActive || !card.Tapped);
        if (count <= 0 || eligible < count) return false;
        if (!NeedsManualReturnMoraleSelection(player, count, requireActive))
        {
            var selected = player.Morale.Where(card => !requireActive || !card.Tapped)
                .OrderByDescending(card => card.Tapped).Take(count).ToArray();
            if (!ReturnSelectedMorale(player, selected, requireActive)) return false;
            CompleteEffectMoraleReturn(item, afterReturn, extra ?? []);
            return true;
        }

        var data = new Dictionary<string, string>
        {
            ["action"] = "effect-morale-return",
            ["afterReturn"] = afterReturn,
        };
        if (extra is not null) foreach (var pair in extra) data[$"return:{pair.Key}"] = pair.Value;
        CreateReturnMoralePrompt(item.Controller, count, "card-effect", item.StackItemId, data, requireActive);
        return true;
    }

    private void ContinueEffectMoraleReturn(L12StackItem item, L12Prompt prompt, IReadOnlyCollection<string> selectedIds)
    {
        var count = int.TryParse(prompt.Data.GetValueOrDefault("count"), out var parsed) ? parsed : 0;
        var requireActive = bool.TryParse(prompt.Data.GetValueOrDefault("requireActive"), out var active) && active;
        if (!ReturnSelectedMoraleById(State.Players[item.Controller], selectedIds, count, requireActive))
        {
            FinishStackItem(item);
            return;
        }
        var extra = prompt.Data.Where(pair => pair.Key.StartsWith("return:", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[7..], pair => pair.Value);
        CompleteEffectMoraleReturn(item, prompt.Data.GetValueOrDefault("afterReturn") ?? string.Empty, extra);
    }

    private void CompleteEffectMoraleReturn(L12StackItem item, string afterReturn, IReadOnlyDictionary<string, string> data)
    {
        var player = State.Players[item.Controller];
        var source = FindSource(item);
        switch (afterReturn)
        {
            case "lubu-kill":
            case "jingke-kill":
                if (data.GetValueOrDefault("target") is { Length: > 0 } killTarget)
                {
                    var reason = afterReturn switch
                    {
                        "jingke-kill" => "被荆轲击杀",
                        _ => "被吕布效果击杀",
                    };
                    KillTarget(item, killTarget, reason);
                }
                FinishStackItem(item);
                break;
            case "mulan-charge":
                if (source is not null) source.HasCharge = true;
                FinishStackItem(item);
                break;
            case "wuzetian-lock":
            {
                var declared = (data.GetValueOrDefault("targets") ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var locked = 0;
                foreach (var id in declared)
                {
                    var lockTarget = FindOnField(State.Players[1 - item.Controller], id, out _, out _);
                    if (lockTarget is null || !IsFieldLegion(lockTarget) || lockTarget.Hidden) continue;
                    lockTarget.CannotUntapUntilRound = State.Round + 1;
                    locked++;
                }
                if (locked == 0)
                    RecordTargetSettlementFailure(item, string.Join('|', declared),
                        declared.Length == 0 ? "发动时没有选择休整军团" : "所选军团已离场、不再是军团或已不再公开");
                else if (locked < declared.Length)
                    AddEvent("effect", item.Controller,
                        $"〈{item.SourceName}〉有{declared.Length - locked}个已声明对象在逆结算后失效；其余对象继续结算");
                FinishStackItem(item);
                break;
            }
            case "march-followup-paid":
            {
                var targets = PublicLegions(State.Players[1 - item.Controller])
                    .Where(target => target.Troops <= 6000)
                    .Select(target => target.InstanceId).ToArray();
                if (targets.Length == 0)
                {
                    FinishStackItem(item);
                    break;
                }
                CreateResolutionChoicePrompt(item, "enemy-target",
                    "神妙行军：选择对方1张兵力不高于6000的军团并击杀",
                    targets, "march-followup-target", new Dictionary<string, string>());
                break;
            }
            case "mozi-immortal":
            {
                var declared = (data.GetValueOrDefault("targets") ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var granted = 0;
                foreach (var id in declared)
                {
                    var target = FindOnField(player, id, out _, out _);
                    if (target is null || !IsFieldLegion(target) || target.Hidden) continue;
                    GrantImmortalUntilNextTurnStart(target, item.Controller);
                    granted++;
                }
                if (granted == 0)
                    RecordTargetSettlementFailure(item, string.Join('|', declared),
                        declared.Length == 0 ? "发动时没有选择我方军团" : "所选军团已离场、不再是军团或已不再公开");
                else if (granted < declared.Length)
                    AddEvent("effect", item.Controller,
                        $"〈{item.SourceName}〉有{declared.Length - granted}个已声明对象在逆结算后失效；其余对象继续结算");
                FinishStackItem(item);
                break;
            }
            case "zhuge-peek":
            {
                if (player.Library.Count == 0) { FinishStackItem(item); break; }
                var top = player.Library[0];
                player.Library.RemoveAt(0);
                AddPresentationEvent("reveal", item.Controller, $"诸葛亮展示 {top.Name}",
                    "S01-0111", item.Trigger == "death" ? "death-top-card" : "attack-top-card", top);
                if (top.CardType == "artifact")
                {
                    player.Resolving.Add(top);
                    item.Data["zhuge-card"] = top.InstanceId;
                    CreatePrompt(item.Controller, "option", "将展示的圣物活跃登场，或加入手牌？", ["play", "hand"], 1, 1,
                        "card-effect", item.StackItemId,
                        data: WithPromptNarrative(
                            new Dictionary<string, string>
                            {
                                ["action"] = "zhuge-artifact",
                                ["play"] = "活跃登场",
                                ["hand"] = "加入手牌",
                            },
                            new(item.SourceName, $"〈{item.SourceName}〉展示了牌库顶部的圣物〈{top.Name}〉。",
                                "请选择让这张圣物活跃登场，或将其加入手牌。",
                                L12PromptWaitingAction.EffectDecision,
                                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["play"] = $"让〈{top.Name}〉在我方圣物区活跃登场。",
                                    ["hand"] = $"将〈{top.Name}〉加入我方手牌。",
                                })));
                }
                else
                {
                    AddPreviouslyRevealedCardToHandByEffect(player, top, "library",
                        $"诸葛亮将{top.Name}加入手牌");
                    FinishStackItem(item);
                }
                break;
            }
            case "empty-city-block":
            {
                var targetStack = State.EffectStack.FirstOrDefault(stack => stack.StackItemId == item.Targets.FirstOrDefault());
                if (targetStack is not null) targetStack.Negated = true;
                if (!player.Field[0].Any(card => card is not null && IsFieldLegion(card))) Draw(player, 1);
                FinishStackItem(item);
                break;
            }
            case "lijing-recruit":
            {
                if (data.GetValueOrDefault("card") is not { Length: > 0 } recruit) { FinishStackItem(item); break; }
                item.Data["revealed"] = recruit;
                var promptData = new Dictionary<string, string>
                {
                    ["action"] = "lijing-slot", ["previewCardId"] = recruit,
                    ["previewPresentation"] = "handled-card",
                };
                var recruitCard = player.Library.FirstOrDefault(card => card.InstanceId == recruit);
                if (recruitCard is not null) AddPromptCardData(promptData, recruitCard);
                var recruitName = recruitCard?.Name ?? "展示的军团";
                var slotConsequences = EmptySlots(player).ToDictionary(
                    slot => slot,
                    slot => $"让〈{recruitName}〉活跃登场到{PlayerBattlefieldSlotLabel(item.Controller, item.Controller, int.Parse(slot.Split(':')[0]), int.Parse(slot.Split(':')[1]))}。",
                    StringComparer.OrdinalIgnoreCase);
                CreatePrompt(item.Controller, "slot", "请直接点击战场上的高亮空位，使展示的军团活跃登场", EmptySlots(player), 1, 1,
                    "card-effect", item.StackItemId,
                    data: WithPromptNarrative(promptData,
                        new(item.SourceName, $"〈{item.SourceName}〉已展示〈{recruitName}〉，现在需要为其选择登场位置。",
                            "请选择我方战场上的1个高亮空位；确认后该军团将活跃登场。",
                            L12PromptWaitingAction.PositionSelection, slotConsequences)));
                break;
            }
            case "free-tactic":
                player.FreeTacticCount++;
                FinishStackItem(item);
                break;
            default:
                FinishStackItem(item);
                break;
        }
    }
}
