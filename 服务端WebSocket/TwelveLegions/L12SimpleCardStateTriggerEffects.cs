namespace TwelveLegions.Server;

internal sealed record L12SimpleCardStateTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string Name,
    string Operation,
    bool Optional,
    bool RequiresOnceReservation,
    string TargetScope,
    string SettlementText,
    string PromptText,
    string EventText,
    string? RequiredCardId = null,
    string? RequiredFaction = null,
    string? CandidateCondition = null,
    string? OnceKeyPrefix = null);

/// <summary>
/// 单段军团状态触发的权威规格。这里只收录完整结算仅令一张军团转为活跃/休整的效果；
/// 带费用、多段结果、士气对象、灾害规则或临时赋予的效果继续使用各自协议。
/// 候选、是否选发、人工目标、次数、响应文案、逆序复核、结算和日志均读取同一规格。
/// </summary>
internal static class L12SimpleCardStateTriggerEffects
{
    internal const string Ready = "ready";
    internal const string Rest = "rest";
    internal const string Source = "source";
    internal const string ControllerLegion = "controller-legion";
    internal const string OpponentLegion = "opponent-legion";

    internal static readonly L12SimpleCardStateTriggerSpec[] All =
    [
        new("S01-0210", 2, "enter", "尼托克丽丝", Ready,
            Optional: false, RequiresOnceReservation: false, TargetScope: ControllerLegion,
            SettlementText: "登场时 选择我方1张<陵墓守卫>转为活跃。",
            PromptText: "尼托克丽丝：选择我方1张休整的〈陵墓守卫〉转为活跃",
            EventText: "尼托克丽丝将所选〈陵墓守卫〉转为活跃",
            RequiredCardId: "S01-0212"),
        new("S01-0313", 3, "death", "神箭奥德尔", Rest,
            Optional: true, RequiresOnceReservation: false, TargetScope: OpponentLegion,
            SettlementText: "阵亡时 可选择对方1张活跃军团，将其转为休整。",
            PromptText: "神箭奥德尔：选择对方1张活跃军团，将其转为休整",
            EventText: "神箭奥德尔将所选军团转为休整"),
        new("S02-0002", 2, "after-kill", "疯狂的爱丽丝", Ready,
            Optional: true, RequiresOnceReservation: true, TargetScope: Source,
            SettlementText: "我方 回合1次 此军团击杀对方军团后，可转为活跃。",
            PromptText: "疯狂的爱丽丝：是否将此军团转为活跃？",
            EventText: "疯狂的爱丽丝因击杀转为活跃",
            CandidateCondition: "authoritative-combat-kill", OnceKeyPrefix: "alice-ready"),
        new("ST05-07", 1, "enter", "安提诺乌斯", Ready,
            Optional: true, RequiresOnceReservation: false, TargetScope: ControllerLegion,
            SettlementText: "登场时 若本回合因主宰弃置过手牌，可将我方1张休整的【奥林匹斯】军团转为活跃。",
            PromptText: "安提诺乌斯：选择我方1张休整的【奥林匹斯】军团转为活跃",
            EventText: "安提诺乌斯将所选【奥林匹斯】军团转为活跃",
            RequiredFaction: "olympus", CandidateCondition: "master-discarded-hand-this-turn"),
    ];

    internal static L12SimpleCardStateTriggerSpec? Find(string cardId, string trigger)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
}

public sealed partial class L12GameEngine
{
    private bool SimpleCardStateCandidateConditionMet(L12TriggerCandidate candidate,
        L12SimpleCardStateTriggerSpec spec)
    {
        var player = State.Players[candidate.Controller];
        return spec.CandidateCondition switch
        {
            null => true,
            "authoritative-combat-kill" => candidate.Data.GetValueOrDefault("killed") == "true"
                && candidate.Data.GetValueOrDefault("combatKillConfirmed") == "true",
            "master-discarded-hand-this-turn" => player.HandDiscardedByMasterThisTurn,
            _ => false,
        };
    }

    private bool IsLegalSimpleCardStateTarget(L12PlayerState targetPlayer, L12CardInstance target,
        L12SimpleCardStateTriggerSpec spec)
    {
        if (spec.Operation == L12SimpleCardStateTriggerEffects.Ready && !target.Tapped) return false;
        if (spec.Operation == L12SimpleCardStateTriggerEffects.Rest && target.Tapped) return false;
        if (spec.RequiredCardId is not null
            && !target.CardId.Equals(spec.RequiredCardId, StringComparison.OrdinalIgnoreCase)) return false;
        return spec.RequiredFaction is null
            || L12StructuredCardRules.HasFaction(targetPlayer, target, spec.RequiredFaction);
    }

    private IEnumerable<L12CardInstance> LegalSimpleCardStateTargets(int controller,
        L12SimpleCardStateTriggerSpec spec, string sourceInstanceId)
    {
        var player = State.Players[controller];
        var targetPlayer = spec.TargetScope == L12SimpleCardStateTriggerEffects.OpponentLegion
            ? State.Players[1 - controller] : player;
        var candidates = spec.TargetScope == L12SimpleCardStateTriggerEffects.Source
            ? PublicLegions(player).Where(card => card.InstanceId == sourceInstanceId)
            : PublicLegions(targetPlayer);
        return candidates.Where(card => IsLegalSimpleCardStateTarget(targetPlayer, card, spec));
    }

    private bool PrepareSimpleCardStateTriggerCandidate(L12TriggerCandidate candidate)
    {
        var spec = L12SimpleCardStateTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger);
        if (spec is null) return true;
        if (candidate.Data.GetValueOrDefault("simpleCardStateConditionLocked") == "true") return true;
        if (!SimpleCardStateCandidateConditionMet(candidate, spec)
            || !LegalSimpleCardStateTargets(candidate.Controller, spec, candidate.SourceInstanceId).Any())
        {
            CleanupPublicTriggerReservation(candidate);
            return false;
        }

        if (spec.RequiresOnceReservation)
        {
            var player = State.Players[candidate.Controller];
            var onceKey = $"{spec.OnceKeyPrefix}:{candidate.SourceInstanceId}:{State.TurnSerial}";
            var pendingKey = $"{onceKey}:pending";
            if (player.UsedAbilities.Contains(onceKey) || player.UsedAbilities.Contains(pendingKey)) return false;
            player.UsedAbilities.Add(pendingKey);
            candidate.Data["onceKey"] = onceKey;
            candidate.Data["cleanupReservation"] = pendingKey;
        }
        candidate.Data["simpleCardStateConditionLocked"] = "true";
        return true;
    }

    private bool TryBeginSimpleCardStateTriggerDeclaration(L12TriggerCandidate candidate,
        L12CardInstance source)
    {
        var spec = L12SimpleCardStateTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger);
        if (spec is null) return false;
        var steps = new List<L12ActivationSelectionStep>();
        if (spec.Optional)
            steps.Add(PublicTriggerStep("option", "mode", spec.TargetScope == L12SimpleCardStateTriggerEffects.Source
                    ? spec.PromptText : $"〈{spec.Name}〉：是否发动{spec.SettlementText}",
                ["mode:none", "mode:use"]));
        if (spec.TargetScope != L12SimpleCardStateTriggerEffects.Source)
        {
            var targets = LegalSimpleCardStateTargets(candidate.Controller, spec, candidate.SourceInstanceId)
                .Select(card => card.InstanceId);
            steps.Add(PublicTriggerStep("field-legion", "cardTarget", spec.PromptText, targets,
                requiredChoice: spec.Optional ? "mode:use" : null,
                targetPlayerIndex: spec.TargetScope == L12SimpleCardStateTriggerEffects.OpponentLegion
                    ? 1 - candidate.Controller : candidate.Controller,
                allowCancel: spec.Optional));
        }

        var result = BeginPendingActivationSequence(candidate.Controller, source,
            "public-trigger-declaration", steps, candidate.CandidateId);
        if (!result.Accepted)
            RemoveUnstackedTriggerCandidate(candidate,
                result.Error ?? $"〈{spec.Name}〉的军团状态效果声明已失效；效果未入栈");
        return true;
    }

    private bool TryCompleteSimpleCardStateTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        var spec = L12SimpleCardStateTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger);
        if (spec is null) return false;
        var player = State.Players[candidate.Controller];
        var source = FindAuthoritativeCard(candidate.SourceInstanceId)
            ?? candidate.SourceSnapshot ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        if (spec.Optional && mode == "mode:none")
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            if (_catalog.AtomicEffects.Find(candidate.SourceCardId) is { } atomic
                && L12SingleSegmentTriggeredEffectPresentations.TryResolveScene(atomic,
                    candidate.Trigger, out var sceneId))
                AddPresentationEventById("effect-declined", candidate.Controller,
                    $"〈{spec.Name}〉的可选状态效果选择不发动，未进入堆叠", sceneId, source);
            else
                AddEvent("ability-cancelled", candidate.Controller,
                    $"〈{spec.Name}〉的可选状态效果选择不发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        string? error = candidate.Data.GetValueOrDefault("simpleCardStateConditionLocked") == "true"
            ? null : $"〈{spec.Name}〉的状态触发条件未在候选建立时锁定；效果未入栈";
        if (error is null && spec.Optional && mode != "mode:use")
            error = $"〈{spec.Name}〉的发动选择已失效；效果未入栈";
        string? targetId = null;
        if (error is null && spec.TargetScope != L12SimpleCardStateTriggerEffects.Source)
        {
            targetId = activation.DeclaredValues.GetValueOrDefault("cardTarget", []).SingleOrDefault();
            if (targetId is null || !LegalSimpleCardStateTargets(candidate.Controller, spec,
                    candidate.SourceInstanceId).Any(card => card.InstanceId == targetId))
                error = $"〈{spec.Name}〉声明的军团目标已失效；效果未入栈";
        }
        if (error is null && spec.RequiresOnceReservation)
        {
            var onceKey = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var pendingKey = candidate.Data.GetValueOrDefault("cleanupReservation") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(onceKey) || string.IsNullOrWhiteSpace(pendingKey)
                || player.UsedAbilities.Contains(onceKey) || !player.UsedAbilities.Contains(pendingKey))
                error = $"〈{spec.Name}〉的回合次数保留已失效；效果未入栈";
            else
                player.UsedAbilities.Add(onceKey);
        }
        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        candidate.Data["declaredTargets"] = spec.TargetScope == L12SimpleCardStateTriggerEffects.Source
            ? candidate.SourceInstanceId : targetId ?? string.Empty;
        candidate.Data["declaration-complete"] = "true";
        CleanupPublicTriggerReservation(candidate);
        AdvanceTriggerBatches();
        return true;
    }

    private bool TryResolveSimpleCardStateTrigger(L12StackItem item)
    {
        var spec = L12SimpleCardStateTriggerEffects.Find(item.SourceCardId, item.Trigger);
        if (spec is null) return false;
        if (spec.Optional && PublicTriggerDeclared(item, "mode") != "mode:use")
        {
            FinishStackItem(item);
            return true;
        }
        var targetId = spec.TargetScope == L12SimpleCardStateTriggerEffects.Source
            ? item.SourceInstanceId : PublicTriggerDeclared(item, "cardTarget");
        var targetPlayer = spec.TargetScope == L12SimpleCardStateTriggerEffects.OpponentLegion
            ? State.Players[1 - item.Controller] : State.Players[item.Controller];
        var target = LegalSimpleCardStateTargets(item.Controller, spec, item.SourceInstanceId)
            .FirstOrDefault(card => card.InstanceId == targetId);
        var source = FindSource(item) ?? item.SourceSnapshot ?? CreateCard(item.SourceCardId, item.SourceInstanceId);
        if (target is null)
        {
            AddEvent("effect-cancelled", item.Controller,
                $"〈{spec.Name}〉声明的军团已离场、类型或阵营已变化，或不再处于要求的{(spec.Operation == L12SimpleCardStateTriggerEffects.Ready ? "休整" : "活跃")}状态；本次效果未生效",
                source);
            FinishStackItem(item);
            return true;
        }

        if (spec.Operation == L12SimpleCardStateTriggerEffects.Ready)
            ReadyCardByEffect(targetPlayer.PlayerIndex, source, target, spec.EventText, item);
        else
        {
            target.Tapped = true;
            AddEvent("effect", item.Controller, spec.EventText, source, target);
        }
        FinishStackItem(item);
        return true;
    }
}
