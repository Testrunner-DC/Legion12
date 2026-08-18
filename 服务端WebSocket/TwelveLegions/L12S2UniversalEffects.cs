namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> S2UniversalEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0001", "S02-0003", "S02-0008", "S02-0104",
    };

    private static readonly HashSet<string> S2UniversalTacticCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0009", "S02-0010", "S02-0011", "S02-0012", "S02-0013", "S02-0014", "S02-0105",
    };

    internal static readonly HashSet<string> S2UniversalAfterAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S02-0002",
    };

    private static bool HasS2UniversalImmediateEffect(string cardId, string trigger)
        => trigger == "enter" ? S2UniversalEnterCards.Contains(cardId) : S2UniversalTacticCards.Contains(cardId);

    private bool TryResolveS2UniversalEnter(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S02-0001":
                State.Players[1 - item.Controller].NextActiveTacticSurcharge++;
                AddEvent("effect", item.Controller, "对方下个回合从手牌打出的主动战术费用 +1", card);
                FinishStackItem(item);
                return true;
            case "S02-0003":
            {
                var choices = State.Players.SelectMany(player => player.Field[1])
                    .Where(target => target is not null && IsCounterTactic(target.CardId))
                    .Select(target => target!.InstanceId).ToList();
                if (choices.Count == 0) { FinishStackItem(item); return true; }
                choices.Add("skip");
                CreatePrompt(item.Controller, "covered-counter", "宫廷魔术师：可选择战场上 1 张反击战术置入所有者墓地",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-magician-remove-counter" });
                return true;
            }
            case "S02-0008":
            {
                if (player.Hand.Count == 0 || !player.Library.Any(candidate => candidate.Faction == "universal"))
                {
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "hand-card", "万物统御之戒：弃置1张手牌",
                    player.Hand.Select(candidate => candidate.InstanceId), 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-ring-discard" });
                return true;
            }
            case "S02-0104":
                CreatePrompt(item.Controller, "optional", "神农鼎：是否抽取1张牌？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-shennong-draw", ["choiceMode"] = "instant",
                        ["yes"] = "抽取1张牌", ["no"] = "不抽牌",
                    });
                return true;
            default:
                return false;
        }
    }

    private bool TryResolveS2UniversalTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S02-0009":
            {
                var availableSlots = Enumerable.Range(0, 3).Count(slot => player.Field[1][slot] is null);
                var choices = player.Hand.Where(candidate => IsCounterTactic(candidate.CardId))
                    .Select(candidate => candidate.InstanceId).ToArray();
                var maximum = Math.Min(2, Math.Min(availableSlots, choices.Length));
                if (maximum == 0)
                {
                    DrawAfterDefenseDeployment(item, card);
                    return true;
                }
                CreatePrompt(item.Controller, "hand-cards", "防御部署：选择手牌中最多2张反击战术置入后排",
                    choices, 0, maximum, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-defense-deployment" });
                return true;
            }
            case "S02-0010":
                CreatePrompt(item.Controller, "option", "黑色莲花：将天灾值增加或减少最多1点",
                    ["-1", "0", "1"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-black-lotus-disaster",
                        ["-1"] = "天灾值-1", ["0"] = "不改变", ["1"] = "天灾值+1",
                    });
                return true;
            case "S02-0011":
            {
                var choices = State.Players[1 - item.Controller].Field.SelectMany(row => row)
                    .Where(target => target is not null && IsFieldLegion(target) && !target.Hidden && target.BaseTroops <= 2000)
                    .Select(target => target!.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "targets", "纷乱箭：选择对方最多 3 张原本兵力不高于 2000 的军团并击杀",
                    choices, 1, Math.Min(3, choices.Length), "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-chaotic-arrows" });
                return true;
            }
            case "S02-0012":
                CreatePrompt(1 - item.Controller, "opponent-confirm", "祷告仪式：是否同意公开下1张天灾卡？",
                    ["agree", "refuse"], 1, 1, "card-effect", item.StackItemId, isPrivate: false,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-prayer-consent", ["choiceMode"] = "instant",
                        ["agree"] = "同意公开", ["refuse"] = "不同意公开",
                    });
                return true;
            case "S02-0013":
            {
                var opponent = State.Players[1 - item.Controller];
                var choices = new[] { opponent.Relic }.Concat(opponent.ExtraRelics)
                    .Where(candidate => candidate is not null)
                    .Select(candidate => candidate!.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "artifact-target", "神圣伽锁：选择对方圣物区的1张【圣物】叠放",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-holy-lock-attach" });
                return true;
            }
            case "S02-0014":
                if (player.Hand.Count <= 4)
                {
                    if (!Draw(player, 2)) SetWinner(1 - item.Controller, "〈瞬间的思路〉抽牌时牌库为空");
                    else AddEvent("draw", item.Controller, "〈瞬间的思路〉抽取 2 张牌", card);
                }
                FinishStackItem(item);
                return true;
            case "S02-0105":
            {
                var choices = PublicLegions(State.Players[1 - item.Controller])
                    .Where(target => target.BaseTroops <= 3000 && !target.Hidden)
                    .Select(target => target.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target", "乾坤 阳：击杀对方1张原本兵力不高于3000的军团",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-qianyang-kill" });
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryResolveS2UniversalAfterAttack(L12StackItem item, L12CardInstance card)
    {
        if (card.CardId != "S02-0002") return false;
        var onceKey = $"alice-ready:{card.InstanceId}:{State.TurnSerial}";
        if (item.Data.GetValueOrDefault("killed") != "true" || State.Players[item.Controller].UsedAbilities.Contains(onceKey))
        {
            FinishStackItem(item);
            return true;
        }
        item.Data["onceKey"] = onceKey;
        CreatePrompt(item.Controller, "optional", "疯狂的爱丽丝击杀军团后，是否转为活跃？", ["yes", "no"], 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-alice-ready" });
        return true;
    }

    private void QueueS2ExorcistReturns(int tacticController, L12CardInstance tactic)
    {
        var owner = State.Players[1 - tacticController];
        var candidates = PublicLegions(owner).Where(card => card.CardId == "S02-0001")
            .Select(exorcist => CreateTriggerCandidate(owner.PlayerIndex, exorcist, "s2-after-opponent-tactic",
                $"对方〈{tactic.Name}〉效果结算后的触发",
                new Dictionary<string, string> { ["tacticId"] = tactic.InstanceId }))
            .ToArray();
        QueueTriggerCandidates(candidates);
    }

    private void ResolveS2ExorcistReturn(L12StackItem item)
    {
        var source = FindSource(item);
        if (source is null || source.CardId != "S02-0001"
            || FindOnField(State.Players[item.Controller], source.InstanceId, out _, out _) is null)
        {
            FinishStackItem(item);
            return;
        }
        CreatePrompt(item.Controller, "optional", "驱魔道士 陆瑛：是否从战场返回手牌？", ["yes", "no"], 1, 1,
            "card-effect", item.StackItemId,
            data: new Dictionary<string, string>
            {
                ["action"] = "s2-exorcist-return", ["choiceMode"] = "instant",
                ["yes"] = "返回手牌", ["no"] = "留在战场",
            });
    }

    private CommandResult? TryBeginS2UniversalActiveAbility(int playerIndex, L12CardInstance source, string ability)
    {
        if (ability == "disableCounters" && source.CardId == "S02-0003")
            return CommitActiveAbility(playerIndex, source, ability, null);
        if (ability == "shennongReset" && source.CardId == "S02-0104")
        {
            var player = State.Players[playerIndex];
            var used = GetAbilities(player.MasterId)
                .Where(view => player.UsedAbilities.Contains($"active:master-{playerIndex}:{view.Id}"))
                .ToArray();
            if (used.Length == 0) return CommandResult.Reject("我方主宰没有已使用的效果次数");
            var result = BeginPendingActivation(playerIndex, source, ability, used.Select(view => view.Id).ToArray(),
                "神农鼎：选择要重置使用次数的主宰效果");
            var prompt = State.PendingPrompts.LastOrDefault(candidate => candidate.PlayerIndex == playerIndex
                && candidate.Continuation == "pending-activation");
            if (prompt is not null)
                foreach (var view in used) prompt.Data[view.Id] = view.Label;
            return result;
        }
        return null;
    }

    private CommandResult? TryCommitS2UniversalActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey)
    {
        if (ability == "disableCounters" && source.CardId == "S02-0003")
        {
            if (source.Tapped) return CommandResult.Reject("宫廷魔术师必须为活跃状态");
            source.Tapped = true;
            State.Players[playerIndex].UsedAbilities.Add(onceKey);
            PushEffect(playerIndex, source, "active", "主动效果",
                data: new Dictionary<string, string> { ["ability"] = ability });
            return CommandResult.Ok();
        }
        if (ability == "shennongReset" && source.CardId == "S02-0104")
        {
            if (source.Tapped) return CommandResult.Reject("神农鼎必须为活跃状态");
            if (!ReturnMorale(State.Players[playerIndex], 1)) return CommandResult.Reject("需要返还1张士气");
            source.Tapped = true;
            PushEffect(playerIndex, source, "active", "主动效果",
                data: new Dictionary<string, string> { ["ability"] = ability, ["target"] = target ?? string.Empty });
            return CommandResult.Ok();
        }
        return null;
    }

    private bool TryResolveS2UniversalActive(L12StackItem item, L12CardInstance? source, string ability)
    {
        if (ability == "disableCounters" && source?.CardId == "S02-0003")
        {
            State.CounterTacticsDisabledUntilTurnSerial = Math.Max(State.CounterTacticsDisabledUntilTurnSerial, State.TurnSerial + 2);
            AddEvent("effect", item.Controller, "直到发动方下个回合开始前，战场上所有反击战术无法发动", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "shennongReset" && source?.CardId == "S02-0104")
        {
            var targetAbility = item.Data.GetValueOrDefault("target") ?? string.Empty;
            State.Players[item.Controller].UsedAbilities.Remove($"active:master-{item.Controller}:{targetAbility}");
            AddEvent("effect", item.Controller, "神农鼎重置我方主宰1个效果的使用次数", source);
            FinishStackItem(item);
            return true;
        }
        if (ability == "discardHolyLock" && source is not null)
        {
            var holyLock = source.AttachedCards.FirstOrDefault(card => card.CardId == "S02-0013");
            if (holyLock is not null)
            {
                source.AttachedCards.Remove(holyLock);
                var owner = holyLock.OwnerIndex is >= 0 and <= 1 ? holyLock.OwnerIndex.Value : 1 - item.Controller;
                ResetCardAfterLeavingField(holyLock);
                State.Players[owner].Graveyard.Add(holyLock);
                if (source.AttachedCards.All(card => card.CardId != "S02-0013"))
                    source.Abilities.RemoveAll(view => view.Id == "discardHolyLock");
                AddEvent("grave", item.Controller, $"{State.Players[item.Controller].Name}消耗3士气弃置〈神圣伽锁〉", holyLock, source);
            }
            FinishStackItem(item);
            return true;
        }
        return false;
    }

    private void ContinueS2UniversalEffect(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            case "s2-holy-lock-attach":
            {
                var owner = State.Players[item.Controller];
                var source = FindSource(item);
                var target = FindPublicCard(chosen[0], out var targetOwner);
                if (source is not null && target is not null && targetOwner == 1 - item.Controller
                    && target.CardType == "artifact" && owner.Resolving.Remove(source))
                {
                    source.OwnerIndex = item.Controller;
                    target.AttachedCards.Add(source);
                    if (target.Abilities.All(view => view.Id != "discardHolyLock"))
                        target.Abilities.Add(new L12AbilityView("discardHolyLock", "消耗3士气：弃置叠放的〈神圣伽锁〉"));
                    AddEvent("attach", item.Controller, $"〈神圣伽锁〉叠放至{target.Name}，该圣物无法使用", source, target);
                }
                FinishStackItem(item);
                break;
            }
            case "s2-black-lotus-disaster":
            {
                var delta = int.TryParse(chosen[0], out var parsed) ? Math.Clamp(parsed, -1, 1) : 0;
                AdjustDisasterValue(delta);
                AddEvent("disaster-value", item.Controller, $"黑色莲花将天灾值调整为 {State.DisasterValue}", FindSource(item) is { } lotus ? [lotus] : []);
                if (ActiveResourceCount(State.Players[item.Controller]) < 3)
                {
                    FinishStackItem(item);
                    break;
                }
                CreatePrompt(item.Controller, "optional", "是否消耗3士气，将〈黑色莲花〉休整置入士气区？",
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-black-lotus-morale", ["choiceMode"] = "instant",
                        ["yes"] = "消耗3士气并置入士气区", ["no"] = "置入墓地",
                    });
                break;
            }
            case "s2-black-lotus-morale":
                if (chosen[0] == "yes") BeginEffectMoralePayment(item, 3, "s2-black-lotus-morale");
                else FinishStackItem(item);
                break;
            case "s2-ring-discard":
            {
                MoveHandToGrave(State.Players[item.Controller], chosen[0], causedByEffect: false);
                var candidates = State.Players[item.Controller].Library
                    .Where(candidate => candidate.Faction == "universal")
                    .Select(candidate => candidate.InstanceId).ToArray();
                if (candidates.Length == 0) { FinishStackItem(item); break; }
                CreatePrompt(item.Controller, "library-search", "万物统御之戒：选择牌库1张【通用】卡牌展示并加入手牌",
                    candidates, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-ring-search" });
                break;
            }
            case "s2-ring-search":
            {
                var player = State.Players[item.Controller];
                var target = player.Library.FirstOrDefault(candidate => candidate.InstanceId == chosen[0]);
                if (target is not null)
                {
                    player.Library.Remove(target);
                    AddCardToHandByEffect(player, target, "library", $"万物统御之戒将{target.Name}加入手牌");
                    AddEvent("search", item.Controller, $"万物统御之戒展示并将〈{target.Name}〉加入手牌", target);
                }
                Shuffle(player.Library);
                FinishStackItem(item);
                break;
            }
            case "s2-defense-deployment":
            {
                var player = State.Players[item.Controller];
                var slots = Enumerable.Range(0, 3).Where(slot => player.Field[1][slot] is null).ToArray();
                var selected = chosen.Take(Math.Min(2, slots.Length)).ToArray();
                for (var index = 0; index < selected.Length; index++)
                {
                    var counter = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == selected[index]
                        && IsCounterTactic(candidate.CardId));
                    if (counter is null) continue;
                    player.Hand.Remove(counter);
                    counter.Hidden = true;
                    counter.SetRound = State.Round;
                    counter.SummonRound = State.Round;
                    player.Field[1][slots[index]] = counter;
                    AddEvent("counter-set", item.Controller, $"{player.Name}因〈防御部署〉在后排覆盖1张反击战术");
                }
                DrawAfterDefenseDeployment(item, FindSource(item));
                break;
            }
            case "s2-prayer-consent":
                if (chosen[0] == "agree") BeginPrayerPublicPreview(item);
                else if (CanReturnMorale(State.Players[item.Controller], 1))
                    CreatePrompt(item.Controller, "optional", "对方不同意公开。是否消耗1士气，仅由我方查看下1张天灾卡？",
                        ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                        data: new Dictionary<string, string>
                        {
                            ["action"] = "s2-prayer-private-cost", ["choiceMode"] = "instant",
                            ["yes"] = "消耗1士气并查看", ["no"] = "不查看",
                        });
                else FinishStackItem(item);
                break;
            case "s2-prayer-private-cost":
                if (chosen[0] == "yes") BeginEffectMoralePayment(item, 1, "s2-prayer-private");
                else FinishStackItem(item);
                break;
            case "s2-prayer-private-confirm":
                FinishStackItem(item);
                break;
            case "s2-exorcist-return":
            {
                var source = FindSource(item);
                if (chosen[0] == "yes" && source is not null)
                    MoveFieldCardToZone(State.Players[item.Controller], source, "hand", "因自身效果返回手牌");
                FinishStackItem(item);
                break;
            }
            case "s2-shennong-draw":
                if (chosen[0] == "yes" && !Draw(State.Players[item.Controller], 1))
                    SetWinner(1 - item.Controller, "〈神农鼎〉抽牌时牌库为空");
                FinishStackItem(item);
                break;
            case "s2-qianyang-kill":
                KillTarget(chosen[0], "被〈乾坤 阳〉击杀");
                if (CanReturnMorale(State.Players[item.Controller], 1))
                    CreatePrompt(item.Controller, "optional", "乾坤 阳：是否返还1士气并抽取1张牌？", ["yes", "no"], 1, 1,
                        "card-effect", item.StackItemId,
                        data: new Dictionary<string, string>
                        {
                            ["action"] = "s2-qianyang-draw", ["choiceMode"] = "instant",
                            ["yes"] = "返还1士气并抽1张", ["no"] = "不发动",
                        });
                else FinishStackItem(item);
                break;
            case "s2-qianyang-draw":
                if (chosen[0] == "yes" && ReturnMorale(State.Players[item.Controller], 1)
                    && !Draw(State.Players[item.Controller], 1))
                    SetWinner(1 - item.Controller, "〈乾坤 阳〉抽牌时牌库为空");
                FinishStackItem(item);
                break;
            case "s2-magician-remove-counter":
                if (chosen[0] != "skip")
                {
                    var target = FindPublicCard(chosen[0], out var owner);
                    if (target is not null) RemoveFromField(State.Players[owner], target, true, "被宫廷魔术师置入墓地",
                        leaveKind: L12FieldLeaveKind.PutIntoGraveyard);
                }
                FinishStackItem(item);
                break;
            case "s2-chaotic-arrows":
                foreach (var targetId in chosen) KillTarget(targetId, "被〈纷乱箭〉击杀");
                FinishStackItem(item);
                break;
            case "s2-alice-ready":
                if (chosen[0] == "yes")
                {
                    var source = FindSource(item);
                    if (source is not null)
                    {
                        State.Players[item.Controller].UsedAbilities.Add(item.Data["onceKey"]);
                        ReadyCardByEffect(item.Controller, source, source, $"{source.Name}因击杀转为活跃");
                        AddEvent("effect", item.Controller, "疯狂的爱丽丝因击杀转为活跃", source);
                    }
                }
                FinishStackItem(item);
                break;
        }
    }

    private void DrawAfterDefenseDeployment(L12StackItem item, L12CardInstance? source)
    {
        var player = State.Players[item.Controller];
        if (player.Hand.Count <= 4)
        {
            if (!Draw(player, 1)) SetWinner(1 - item.Controller, "〈防御部署〉抽牌时牌库为空");
            else AddEvent("draw", item.Controller, "〈防御部署〉因手牌不高于4张抽取1张牌", source is null ? [] : [source]);
        }
        FinishStackItem(item);
    }

    private void BeginPrayerPublicPreview(L12StackItem item)
    {
        if (State.DisasterDeck.Count == 0) { FinishStackItem(item); return; }
        var disaster = State.DisasterDeck[0];
        var data = new Dictionary<string, string>
        {
            ["previewCardId"] = disaster.InstanceId,
            ["sourceStackItemId"] = item.StackItemId,
        };
        AddPromptCardData(data, disaster);
        for (var playerIndex = 0; playerIndex < State.Players.Length; playerIndex++)
            CreatePrompt(playerIndex, "information-confirm", $"祷告仪式公开下1张天灾卡〈{disaster.Name}〉", [], 0, 0,
                "s2-prayer-public-confirm", item.StackItemId, isPrivate: false,
                data: new Dictionary<string, string>(data));
        AddEvent("reveal", item.Controller, $"〈祷告仪式〉公开下1张天灾卡〈{disaster.Name}〉", disaster);
    }

    private void BeginPrayerPrivatePreview(L12StackItem item)
    {
        if (State.DisasterDeck.Count == 0) { FinishStackItem(item); return; }
        var disaster = State.DisasterDeck[0];
        var data = new Dictionary<string, string>
        {
            ["action"] = "s2-prayer-private-confirm",
            ["previewCardId"] = disaster.InstanceId,
        };
        AddPromptCardData(data, disaster);
        CreatePrompt(item.Controller, "information-confirm", $"祷告仪式：查看下1张天灾卡〈{disaster.Name}〉", [], 0, 0,
            "card-effect", item.StackItemId, isPrivate: true, data: data);
    }
}
