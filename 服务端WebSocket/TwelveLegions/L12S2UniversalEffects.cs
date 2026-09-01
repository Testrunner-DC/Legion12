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
        switch (AtomicFlowKey(item, card))
        {
            case "宫廷魔术师":
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
            case "万物统御之戒":
            {
                if (player.Hand.Count == 0 || !player.Library.Any(candidate => candidate.Faction == "universal"))
                {
                    FinishStackItem(item);
                    return true;
                }
                CreatePrompt(item.Controller, "optional", "万物统御之戒：是否弃置1张手牌，检索1张【通用】卡牌？",
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-ring-start", ["choiceMode"] = "instant",
                        ["yes"] = "弃置1张手牌，检索1张【通用】卡牌",
                        ["no"] = "不发动",
                    });
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryResolveS2UniversalTactic(L12StackItem item, L12CardInstance card)
    {
        var player = State.Players[item.Controller];
        switch (AtomicFlowKey(item, card))
        {
            case "defense-deployment-set":
            {
                var cards = CompositeDeclared(item, "entryCards");
                for (var index = 0; index < cards.Length; index++)
                {
                    var slotText = CompositeDeclared(item, $"entrySlot{index + 1}").SingleOrDefault();
                    if (slotText?.Split(':') is not ["1", var slotValue]
                        || !int.TryParse(slotValue, out var slot) || slot is < 0 or > 2
                        || player.Field[1][slot] is not null) continue;
                    var counter = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == cards[index]
                        && IsCounterTactic(candidate.CardId));
                    if (counter is null) continue;
                    player.Hand.Remove(counter);
                    counter.Hidden = true;
                    counter.SetRound = State.Round;
                    counter.SummonRound = State.Round;
                    player.Field[1][slot] = counter;
                    AddEvent("counter-set", item.Controller, $"{player.Name}因〈防御部署〉在后排{slot + 1}号位覆盖1张反击战术");
                }
                FinishStackItem(item);
                return true;
            }
            case "defense-deployment-draw":
                DrawAfterDefenseDeployment(item, card);
                return true;
            case "black-lotus-disaster":
            {
                var delta = int.TryParse(CompositeDeclared(item, "disasterMode").SingleOrDefault(), out var parsed)
                    ? Math.Clamp(parsed, -1, 1) : 0;
                AdjustDisasterValue(delta);
                AddEvent("disaster-value", item.Controller, $"黑色莲花将天灾值调整为 {State.DisasterValue}", card);
                FinishStackItem(item);
                return true;
            }
            case "black-lotus-morale":
                if (player.Resolving.Remove(card))
                {
                    player.Morale.Add(new L12MoraleCard
                    {
                        InstanceId = card.InstanceId,
                        CardId = card.CardId,
                        Tapped = true,
                    });
                    AddEvent("morale", item.Controller, "〈黑色莲花〉休整置入士气区，视为1张士气", card);
                }
                FinishStackItem(item);
                return true;
            case "chaotic-arrows-effect":
                foreach (var targetId in CompositeDeclared(item, "killTargets"))
                    if (DeclaredEnemyTarget(item.Controller, targetId, target => target.BaseTroops <= 2000) is not null)
                        KillTarget(item, targetId, "被〈纷乱箭〉击杀");
                FinishStackItem(item);
                return true;
            case "holy-lock-effect":
                ResolveDeclaredHolyLock(item, CompositeDeclared(item, "artifactTarget").SingleOrDefault());
                return true;
            case "祷告仪式":
                CreatePrompt(1 - item.Controller, "opponent-confirm", "祷告仪式：是否同意公开下1张天灾卡？",
                    ["agree", "refuse"], 1, 1, "card-effect", item.StackItemId, isPrivate: false,
                    data: new Dictionary<string, string>
                    {
                        ["action"] = "s2-prayer-consent", ["choiceMode"] = "instant",
                        ["agree"] = "同意公开", ["refuse"] = "不同意公开",
                    });
                return true;
            case "qianyang-kill":
            {
                var targetId = CompositeDeclared(item, "killTarget").SingleOrDefault();
                if (DeclaredEnemyTarget(item.Controller, targetId, target => target.BaseTroops <= 3000) is not null)
                    KillTarget(item, targetId!, "被〈乾坤 阳〉击杀");
                FinishStackItem(item);
                return true;
            }
            case "qianyang-draw":
            {
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "〈乾坤 阳〉抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryResolveS2UniversalAfterAttack(L12StackItem item, L12CardInstance card)
    {
        if (card.CardId != "S02-0002") return false;
        if (item.Data.GetValueOrDefault("killed") != "true"
            || PublicTriggerDeclared(item, "mode") != "mode:use")
        {
            FinishStackItem(item);
            return true;
        }
        var source = FindSource(item);
        if (source is not null && FindOnField(State.Players[item.Controller], source.InstanceId, out _, out _) is not null)
        {
            ReadyCardByEffect(item.Controller, source, source, $"{source.Name}因击杀转为活跃");
            AddEvent("effect", item.Controller, "疯狂的爱丽丝因击杀转为活跃", source);
        }
        else AddEvent("effect-cancelled", item.Controller,
            "疯狂的爱丽丝在结算时已不在战场；转为活跃效果取消", card);
        FinishStackItem(item);
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
        if (PublicTriggerDeclared(item, "mode") != "mode:use")
        {
            FinishStackItem(item);
            return;
        }
        if (source is not null && source.CardId == "S02-0001"
            && FindOnField(State.Players[item.Controller], source.InstanceId, out _, out _) is not null)
            MoveFieldCardToZone(State.Players[item.Controller], source, "hand", "因自身效果返回手牌");
        else AddEvent("effect-cancelled", item.Controller,
            "驱魔道士 陆瑛在结算时已不在战场；返回手牌效果取消", item.SourceSnapshot is null ? [] : [item.SourceSnapshot]);
        FinishStackItem(item);
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

    private CommandResult? TryCommitS2UniversalActiveAbility(int playerIndex, L12CardInstance source, string ability, string? target, string onceKey,
        bool returnMoralePrepaid = false)
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
            if (!returnMoralePrepaid && !ReturnMorale(State.Players[playerIndex], 1)) return CommandResult.Reject("需要返还1张士气");
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
            State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
            State.CounterTacticsDisabledExpiresAtPlayerTurnStart = item.Controller;
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
            case "s2-ring-start":
                if (chosen[0] == "no")
                {
                    FinishStackItem(item);
                    break;
                }
                CreatePrompt(item.Controller, "hand-card", "万物统御之戒：弃置1张手牌",
                    State.Players[item.Controller].Hand.Select(candidate => candidate.InstanceId), 1, 1,
                    "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-ring-discard" });
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
                ShuffleLibrary(player, "万物统御之戒检索结算");
                FinishStackItem(item);
                break;
            }
            case "s2-prayer-consent":
                if (chosen[0] == "agree") BeginPrayerPublicPreview(item);
                else
                {
                    var prayer = FindSource(item) ?? item.SourceSnapshot;
                    if (prayer is not null && State.DisasterDeck.Count > 0
                        && ActiveResourceCount(State.Players[item.Controller]) >= 1)
                        QueueTriggerCandidates([
                            CreateTriggerCandidate(item.Controller, prayer, "prayer-private",
                                "对方拒绝公开后的私下查看天灾效果", sourceSnapshot: prayer)
                        ]);
                    FinishStackItem(item);
                }
                break;
            case "s2-prayer-private-confirm":
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
        }
    }

    private void ResolveDeclaredHolyLock(L12StackItem item, string? targetId)
    {
        var owner = State.Players[item.Controller];
        var source = FindSource(item);
        var target = FindPublicCard(targetId, out var targetOwner);
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
    }

    private void DrawAfterDefenseDeployment(L12StackItem item, L12CardInstance? source)
    {
        var player = State.Players[item.Controller];
        if (player.Hand.Count <= 4)
        {
            if (!Draw(player, 1, logEffectDraw: false)) SetWinner(1 - item.Controller, "〈防御部署〉抽牌时牌库为空");
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

    private void ResolvePrayerPrivatePreview(L12StackItem item)
    {
        if (PublicTriggerDeclared(item, "mode") == "mode:use")
            BeginPrayerPrivatePreview(item);
        else FinishStackItem(item);
    }
}
