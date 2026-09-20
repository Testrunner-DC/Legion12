namespace TwelveLegions.Server;

/// <summary>
/// 公开触发效果的统一声明计划。触发候选只有在可选模式、公开费用对象、公开目标与公开位置
/// 全部声明并再次验证后才能入栈；结算时只重验对应效果段，不回滚已经合法支付的费用。
/// 依赖隐藏信息的对象不得加入这里，必须在效果合法开始并公开该信息后走延迟公开声明。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string PublicTriggerTombGuardCard = "S01-0212";
    private const string PublicTriggerSigurdCard = "S01-0310";
    private const string PublicTriggerScarabCard = "S02-0201";
    private static readonly IReadOnlyDictionary<string, string> FifthBatchPublicTriggerPlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0320|reaction"] = "blood-eagle",
            ["S01-0224|wisdom-reward"] = "wisdom-reward",
        };

    private static readonly IReadOnlyDictionary<string, string> Batch6DPublicTriggerPlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0204|death"] = "tomb-construct",
            ["S01-0204|leave"] = "tomb-construct",
            ["S01-0414|after-attack"] = "after-attack-katsura",
            ["S01-0414|return-library-top"] = "returned-top-katsura",
            ["S01-0417|play"] = "owner-top-kusanagi",
        };

    private static readonly IReadOnlyDictionary<string, string> Batch6IBPublicTriggerPlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0001|death"] = "teach-draw-cycle",
            ["S01-0112|death"] = "grave-to-hand",
            ["S01-0115|death"] = "jingke-kill",
            ["S01-0207|death"] = "tutankhamun-top",
            ["S01-0210|death"] = "grave-legion-summon",
            ["S01-0303|death"] = "ragnar-draw-cycle",
            ["S01-0304|death"] = "harald-kill",
            ["S01-0306|death"] = "olaf-draw-cycle",
            ["S01-0307|death"] = "grave-to-hand",
            ["S01-0308|death"] = "grave-legion-summon",
            ["S01-0403|death"] = "uesugi-counters",
            ["S01-0407|death"] = "hand-legion-summon",
            ["S02-0202|death"] = "grave-legion-summon",
            ["S02-0301|death"] = "thor-draw-cycle",
            ["S02-0518|death"] = "grave-to-hand",
            ["S02-0601|death"] = "hand-legion-summon",
            ["S02-0615|death"] = "gwen-choice",
        };

    private static readonly IReadOnlyDictionary<string, string> Batch6JBPublicTriggerPlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0101|after-attack|"] = "lubu-ready",
            ["S01-0108|death|"] = "mulan-lock-morale",
            ["S01-0311|after-attack|"] = "gustav-ready",
            ["S02-0001|s2-after-opponent-tactic|"] = "exorcist-return",
            ["S02-0012|prayer-private|"] = "prayer-private",
        };

    private static string? Batch6JBPublicTriggerPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data = null)
        => Batch6JBPublicTriggerPlans.GetValueOrDefault(
            $"{cardId}|{trigger}|{data?.GetValueOrDefault("ability") ?? string.Empty}");

    private bool PrepareBatch6JBPublicTriggerCandidate(L12TriggerCandidate candidate)
    {
        var plan = Batch6JBPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        if (plan is null || candidate.Data.GetValueOrDefault("batch6JBConditionLocked") == "true") return true;
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var fieldSource = FindOnField(player, candidate.SourceInstanceId, out _, out _);
        var sourceOnField = fieldSource is not null;
        var legal = plan switch
        {
            "lubu-ready" => sourceOnField && CanReturnMorale(player, 4),
            "mulan-lock-morale" => State.ActivePlayer != candidate.Controller
                && opponent.Morale.Any(card => card.Tapped),
            "gustav-ready" => sourceOnField
                && player.Graveyard.Where(CanEnterHandOrLibrary)
                    .Sum(L12StructuredCardRules.StarterGraveCardCopies) >= 2,
            "exorcist-return" => sourceOnField,
            "prayer-private" => State.DisasterDeck.Count > 0 && ActiveResourceCount(player) >= 1,
            _ => false,
        };
        if (!legal)
            return false;

        if (plan == "gustav-ready")
        {
            var onceKey = $"gustav-ready:{candidate.SourceInstanceId}:{State.TurnSerial}";
            var pendingKey = $"{onceKey}:pending";
            if (player.UsedAbilities.Contains(onceKey) || player.UsedAbilities.Contains(pendingKey)) return false;
            player.UsedAbilities.Add(pendingKey);
            candidate.Data["onceKey"] = onceKey;
            candidate.Data["cleanupReservation"] = pendingKey;
        }
        candidate.Data["batch6JBConditionLocked"] = "true";
        return true;
    }

    private static string? Batch6IBPublicTriggerPlan(string cardId, string trigger)
        => Batch6IBPublicTriggerPlans.GetValueOrDefault($"{cardId}|{trigger}");

    private bool PrepareBatch6IBPublicTriggerCandidate(L12TriggerCandidate candidate)
    {
        var plan = Batch6IBPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return true;
        if (candidate.Data.GetValueOrDefault("batch6IBConditionLocked") == "true") return true;

        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var legal = plan switch
        {
            "grave-to-hand" => TryGetGraveToHandTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var recovery)
                && GraveToHandTriggerConditionMet(recovery) && LegalGraveToHandTargets(recovery, player).Length > 0,
            "jingke-kill" => CanReturnMorale(player, 1),
            "tutankhamun-top" => player.Graveyard.Any(card => CanEnterHandOrLibrary(card)
                && card.CardId != "S01-0207" && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
                && L12StructuredCardRules.CurrentCostAtMost(card, 4)),
            "grave-legion-summon" => TryGetGraveLegionSummonTriggerSpec(candidate.SourceCardId,
                    candidate.Trigger, out var summon)
                && EmptySlots(player).Any() && LegalGraveLegionSummonTargets(summon, player).Length > 0,
            "harald-kill" => PublicLegions(opponent).Any(card => card.Troops <= 2000),
            "uesugi-counters" => Enumerable.Range(0, 3).Any(slot => player.Field[1][slot] is null)
                && player.Hand.Any(card => IsCounterTactic(card.CardId)),
            "hand-legion-summon" => TryGetHandLegionSummonTriggerSpec(candidate.SourceCardId,
                    candidate.Trigger, out var handSummon)
                && EmptySlots(player).Any() && LegalHandLegionSummonTargets(handSummon, player).Length > 0,
            "gwen-choice" => candidate.Data.GetValueOrDefault("cause") == "effect",
            _ => true,
        };
        if (!legal)
            return false;

        candidate.Data["batch6IBConditionLocked"] = "true";
        return true;
    }

    private static string? Batch6DPublicTriggerPlan(string cardId, string trigger)
        => Batch6DPublicTriggerPlans.GetValueOrDefault($"{cardId}|{trigger}");

    private static string? FifthBatchPublicTriggerPlan(string cardId, string trigger)
        => FifthBatchPublicTriggerPlans.GetValueOrDefault($"{cardId}|{trigger}");

    private IEnumerable<string> SimpleResourceMoraleTargets(L12PlayerState player,
        L12SimpleResourceTriggerSpec spec)
        => player.Morale.Where(card => CanFlipMoraleToGodPower(card,
                onlyTapped: spec.TargetFilter == L12SimpleResourceTriggerEffects.RestedMorale))
            .Select(card => card.InstanceId);

    private bool SimpleResourceTriggerConditionMet(L12TriggerCandidate candidate,
        L12SimpleResourceTriggerSpec spec)
    {
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        if (spec.TargetFilter is not null && !SimpleResourceMoraleTargets(player, spec).Any()) return false;
        return spec.CandidateCondition switch
        {
            null => true,
            "morale-deck-not-empty" => player.MoraleDeck.Count > 0,
            "controller-morale-less-than-opponent" => player.Morale.Count < opponent.Morale.Count
                && player.MoraleDeck.Count > 0,
            "controller-morale-zero-or-return-locked" => (player.Morale.Count == 0
                    || candidate.Data.GetValueOrDefault("factionZeroEligibleAtReturn") == "true")
                && player.MoraleDeck.Count > 0,
            _ => false,
        };
    }

    private bool PrepareSimpleResourceTriggerCandidate(L12TriggerCandidate candidate)
    {
        var spec = L12SimpleResourceTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        if (spec is null) return true;
        if (candidate.Data.GetValueOrDefault("simpleResourceConditionLocked") == "true") return true;
        if (!SimpleResourceTriggerConditionMet(candidate, spec))
        {
            CleanupPublicTriggerReservation(candidate);
            return false;
        }

        if (spec.RequiresOnceReservation)
        {
            var player = State.Players[candidate.Controller];
            if (spec.DataAbility == "factionZeroRecovery"
                && string.IsNullOrWhiteSpace(candidate.Data.GetValueOrDefault("onceKey")))
            {
                const string onceKey = "trigger:factionZeroRecovery";
                const string pendingKey = "pending:factionZeroRecovery";
                const string queuedKey = "queued:factionZeroRecovery";
                if (player.UsedAbilities.Contains(onceKey) || player.UsedAbilities.Contains(pendingKey)) return false;
                player.UsedAbilities.Add(pendingKey);
                candidate.Data["onceKey"] = onceKey;
                candidate.Data["cleanupReservation"] = pendingKey;
                candidate.Data["cleanupQueuedReservation"] = queuedKey;
            }
            var once = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var pending = candidate.Data.GetValueOrDefault("cleanupReservation") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(once) || string.IsNullOrWhiteSpace(pending)
                || player.UsedAbilities.Contains(once) || !player.UsedAbilities.Contains(pending))
            {
                CleanupPublicTriggerReservation(candidate);
                return false;
            }
        }
        candidate.Data["simpleResourceConditionLocked"] = "true";
        return true;
    }

    private bool TryBeginSimpleResourceTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        var spec = L12SimpleResourceTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        if (spec is null) return false;
        var player = State.Players[candidate.Controller];
        var steps = new List<L12ActivationSelectionStep>();
        if (spec.Optional)
            steps.Add(PublicTriggerStep("option", "mode",
                $"〈{spec.Name}〉：预先声明是否发动{spec.SettlementText}", ["mode:none", "mode:use"]));
        if (spec.TargetFilter is not null)
            steps.Add(PublicTriggerStep("target-morale", "moraleTarget",
                $"〈{spec.Name}〉：选择要翻转为神力的1张{(spec.TargetFilter == L12SimpleResourceTriggerEffects.RestedMorale ? "休整" : string.Empty)}士气",
                SimpleResourceMoraleTargets(player, spec), requiredChoice: spec.Optional ? "mode:use" : null,
                allowCancel: spec.Optional));

        // 必发且没有目标/模式需要玩家声明的单段资源效果直接进入响应堆叠。
        // 不创建零步骤 PendingActivation，否则刷新恢复后会留下无法完成的空声明。
        if (steps.Count == 0)
        {
            candidate.Data["declaration-complete"] = "true";
            return false;
        }

        var result = BeginPendingActivationSequence(candidate.Controller, source,
            "public-trigger-declaration", steps, candidate.CandidateId);
        if (!result.Accepted)
            RemoveUnstackedTriggerCandidate(candidate, result.Error ?? "单段资源效果声明已失效；效果未入栈");
        return true;
    }

    private bool TryCompleteSimpleResourceTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        var spec = L12SimpleResourceTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger, candidate.Data);
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
                    $"〈{spec.Name}〉的可选资源效果选择不发动，未进入堆叠", sceneId, source);
            else
                AddEvent("ability-cancelled", candidate.Controller,
                    $"〈{spec.Name}〉的可选资源效果选择不发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        string? error = candidate.Data.GetValueOrDefault("simpleResourceConditionLocked") == "true"
            ? null : $"〈{spec.Name}〉的资源触发条件未在候选建立时锁定；效果未入栈";
        if (error is null && (!SimpleResourceTriggerConditionMet(candidate, spec)
            || spec.Optional && mode != "mode:use"))
            error = $"〈{spec.Name}〉的资源条件或发动选择已失效；效果未入栈";
        if (error is null && spec.TargetFilter is not null)
        {
            var target = activation.DeclaredValues.GetValueOrDefault("moraleTarget", []).SingleOrDefault();
            if (target is null || !SimpleResourceMoraleTargets(player, spec)
                .Contains(target, StringComparer.OrdinalIgnoreCase))
                error = $"〈{spec.Name}〉声明的士气目标已失效；效果未入栈";
        }
        if (error is null && spec.RequiresOnceReservation)
        {
            var once = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var pending = candidate.Data.GetValueOrDefault("cleanupReservation") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(once) || string.IsNullOrWhiteSpace(pending)
                || player.UsedAbilities.Contains(once) || !player.UsedAbilities.Contains(pending))
                error = $"〈{spec.Name}〉的回合次数保留已失效；效果未入栈";
            else
                player.UsedAbilities.Add(once);
        }
        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        candidate.Data["declaration-complete"] = "true";
        CleanupPublicTriggerReservation(candidate);
        AdvanceTriggerBatches();
        return true;
    }

    private static string? Batch6GAPublicTriggerPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data)
        => (cardId, trigger, data?.GetValueOrDefault("ability"), data?.GetValueOrDefault("mode")) switch
        {
            ("S02-0102", "enter", _, _) => "limu-enter",
            ("S02-0304", "enter", _, _) => "margaret-entry-mill",
            ("S02-0304", "master-damaged-by-effect", "margaretMasterDamage", _) => "margaret-master-damage",
            ("S02-0305", "master-damaged", "anderstorpRingDraw", _) => "anderstorp-draw",
            _ => null,
        };

    private static L12VerifiedAtomicProgram? VerifiedAtomicOptionalTriggerPlan(string cardId, string trigger)
    {
        var program = L12VerifiedAtomicPrograms.Find(cardId, trigger);
        return program?.Atoms.Any(atom => atom.Kind == L12AtomKinds.Optional) == true ? program : null;
    }

    private bool PrepareVerifiedAtomicOptionalCandidate(L12TriggerCandidate candidate)
    {
        // 单段资源触发的候选条件、目标选择与次数预占由统一资源协议负责。
        // 它仍注册为已验证原子程序供后台审计，但不能再被旧的通用可选触发
        // 准备器重复解释，否则资源协议的领域条件会落入旧表达式解释器。
        if (L12SimpleResourceTriggerEffects.Find(
                candidate.SourceCardId, candidate.Trigger, candidate.Data) is not null
            || L12SimpleCardStateTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger) is not null)
            return true;

        var program = VerifiedAtomicOptionalTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        if (program is null) return true;
        if (candidate.Data.GetValueOrDefault("verifiedAtomicConditionLocked") == "true") return true;

        var source = FindAuthoritativeCard(candidate.SourceInstanceId)
            ?? candidate.SourceSnapshot ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        var controller = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        foreach (var atom in program.Atoms.TakeWhile(atom => atom.Kind != L12AtomKinds.Optional)
                     .Where(atom => atom.Kind == L12AtomKinds.Condition))
        {
            if (!CheckVerifiedAtomicCondition(atom.Parameters.GetValueOrDefault("expression"), candidate.Data,
                    source, controller, opponent))
                return false;
        }

        candidate.Data["verifiedAtomicOptional"] = "true";
        candidate.Data["verifiedAtomicConditionLocked"] = "true";
        return true;
    }

    private static bool HasPublicTriggerDeclarationPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data = null)
        => L12SimpleCardStateTriggerEffects.Find(cardId, trigger) is not null
            || HasStarterTargetedTriggerDeclarationPlan(cardId, trigger)
            || L12OpponentHandDiscardTriggerEffects.Find(cardId, trigger) is not null
            || HasTrialAdvanceTriggerDeclarationPlan(cardId, trigger, data)
            || Batch6JAEnterPlan(cardId, trigger) is not null
            || HasAttackPublicTriggerDeclarationPlan(cardId, trigger)
            || HasTrialCompletionTriggerDeclarationPlan(cardId, trigger, data)
            || L12SimpleResourceTriggerEffects.Find(cardId, trigger, data) is not null
            || Batch6JBPublicTriggerPlan(cardId, trigger, data) is not null
            || Batch6IBPublicTriggerPlan(cardId, trigger) is not null
            || VerifiedAtomicOptionalTriggerPlan(cardId, trigger) is not null
            || Batch6GAPublicTriggerPlan(cardId, trigger, data) is not null
            || Batch6DPublicTriggerPlan(cardId, trigger) is not null
            || FifthBatchPublicTriggerPlan(cardId, trigger) is not null
            || (cardId, trigger, data?.GetValueOrDefault("ability"), data?.GetValueOrDefault("mode")) switch
        {
            ("S02-0006", "discard-trigger", _, _) => true,
            ("S02-04M1", "friendly-legion-moves", "tsukuyomiFollowMove", _) => true,
            ("S02-04M1", "friendly-front-to-back", "tsukuyomiReadyMorale", _) => true,
            ("S02-0523", "trojan-after-attack", _, _) => true,
            ("S01-02M3", "medjed-master-damage", _, _) => true,
            ("S02-02M1", "nephthys-own-death", _, _) => true,
            ("S02-01S1", "master-morale-return", _, "xiaotian") => true,
            ("S01-0105" or "S01-0207" or "S01-0208" or "S01-0309" or "S02-0203"
                or "S02-0205" or "S01-0407", "enter", _, _) => true,
            ("S01-0021" or "S01-0213" or "S01-0223", "reaction", _, _) => true,
            ("S02-0202", "death", _, _) => true,
            ("S01-0206", "attack", _, _) => true,
            ("S01-0201", "attack" or "death", _, _) => true,
            ("S01-0315", "enter", _, _) => true,
            _ => false,
        };

    /// <summary>
    /// 传统的直接 PushEffect 路径也必须接入同一触发候选声明链，否则手牌打出、GM 置入或
    /// 其他效果登场会绕过公开声明。只有已登记公共计划的效果改走候选，其余效果保持原流程。
    /// </summary>
    private void QueueOrPushTriggeredEffect(int controller, L12CardInstance source, string trigger, string text,
        IEnumerable<string>? targets = null, Dictionary<string, string>? data = null)
    {
        // 冒号前的登场费用必须由所有登场入口共用同一预支付网关。手牌打出、效果登场、
        // GM 置入与权威事件都可能来到这里，任何调用方都不得先把嬴政的击杀段直接入栈。
        if (trigger == "enter" && L12StructuredCardRules.RequiresPreStackEnterCost(source)
            && data?.GetValueOrDefault("entryCostPaid") != "true"
            && data?.GetValueOrDefault("entryCostUnavailable") != "true")
        {
            BeginYingzhengEnterActivation(controller, source);
            return;
        }
        if (TryQueueAttackPublicTriggerCandidates(controller, source, trigger, text, targets, data))
            return;
        if (!HasPublicTriggerDeclarationPlan(source.CardId, trigger, data))
        {
            var directData = data is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
            if (!directData.ContainsKey("compositePlan")
                && directData.GetValueOrDefault("entryCostUnavailable") != "true"
                && DefaultTriggerCompositePlanData(source.CardId, trigger) is { } composite)
                foreach (var pair in composite) directData[pair.Key] = pair.Value;
            PushEffect(controller, source, trigger, text, targets, directData);
            return;
        }

        var candidate = CreateTriggerCandidate(controller, source, trigger, text, data);
        if (targets is not null)
            candidate.Data["declaredTargets"] = string.Join('|', targets);
        QueueTriggerCandidates([candidate]);
    }

    private bool TryBeginPublicTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        if (L12OpponentHandDiscardTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger) is not null)
        {
            candidate.Data["declaration-complete"] = "true";
            return false;
        }
        if (TryBeginSimpleCardStateTriggerDeclaration(candidate, source))
            return true;
        if (TryBeginStarterTargetedTriggerDeclaration(candidate, source))
            return true;
        if (TryBeginTrialAdvanceTriggerDeclaration(candidate, source))
            return true;
        if (TryBeginBatch6JAEnterDeclaration(candidate, source))
            return true;
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        List<L12ActivationSelectionStep>? steps = null;
        var batch6JBPlan = Batch6JBPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        var batch6IBPlan = Batch6IBPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var batch6GAPlan = Batch6GAPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        var batch6DPlan = Batch6DPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var fifthBatchPlan = FifthBatchPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);

        if (TryBeginTrialCompletionTriggerDeclaration(candidate, source))
            return true;
        if (TryBeginAttackPublicTriggerDeclaration(candidate, source))
            return true;
        if (TryBeginSimpleResourceTriggerDeclaration(candidate, source))
            return true;

        if (candidate.SourceCardId == "S02-0006" && candidate.Trigger == "discard-trigger")
        {
            // 多张同名候选可共存，但每个声明开始/提交时都重验共享次数。
            if (L12CardNameUsageRules.HasUsed(player, candidate.SourceCardId))
            {
                RemoveUnstackedTriggerCandidate(candidate, "〈信仰狂热者〉的卡名共享次数已使用；效果未入栈");
                return true;
            }
            steps =
            [
                PublicTriggerStep("option", "mode",
                    $"{source.Name}：是否发动？{_catalog.AtomicEffects.Find(candidate.SourceCardId)!.Abilities.Single(ability => ability.Trigger == "discarded").Text}",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6JBPlan == "lubu-ready")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "吕布：预先声明是否返还4张士气并转为活跃",
                    ["mode:none", "mode:use"]),
                PublicTriggerStep("target-morale", "returnCost", "吕布：预先选择返还的4张士气",
                    player.Morale.Select(card => card.InstanceId), min: 4, max: 4, requiredChoice: "mode:use"),
            ];
        }
        else if (batch6JBPlan == "mulan-lock-morale")
        {
            steps =
            [
                PublicTriggerStep("target-morale", "moraleTarget",
                    "花木兰：预先选择对方1张休整士气，使其下个重置阶段无法转为活跃",
                    opponent.Morale.Where(card => card.Tapped).Select(card => card.InstanceId),
                    targetPlayerIndex: 1 - candidate.Controller, allowCancel: false),
            ];
        }
        else if (batch6JBPlan == "gustav-ready")
        {
            var graveCards = player.Graveyard.Where(CanEnterHandOrLibrary).ToArray();
            steps =
            [
                PublicTriggerStep("option", "mode", "古斯塔夫一世：预先声明是否将墓地2张牌置于牌库底部并转为活跃",
                    ["mode:none", "mode:use"]),
                GraveCostSelectionStep(player,
                    "古斯塔夫一世：依声明顺序选择合计视为2张、置于牌库底部的墓地卡牌",
                    "graveCost", graveCards, required: 2, requiredDeclaredChoice: "mode:use"),
            ];
        }
        else if (batch6JBPlan == "exorcist-return")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", $"〈{source.Name}〉：预先声明是否发动可选触发效果",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6JBPlan == "prayer-private")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "祷告仪式：对方拒绝公开后，预先声明是否消耗1份资源私下查看",
                    ["mode:none", "mode:use"]),
                PublicTriggerStep("composite-ordinary-payment", "cost", "祷告仪式：预先选择消耗的1份资源",
                    CompositeOrdinaryPaymentChoices(player), requiredChoice: "mode:use",
                    autoSelectEquivalentOrdinaryMorale: true),
            ];
        }
        else if (batch6IBPlan is "teach-draw-cycle" or "ragnar-draw-cycle" or "olaf-draw-cycle"
            or "thor-draw-cycle")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", $"〈{source.Name}〉：预先声明是否发动阵亡/击杀时的可选效果",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6IBPlan == "grave-to-hand"
            && TryGetGraveToHandTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var recovery))
        {
            var targets = LegalGraveToHandTargets(recovery, player).Select(card => card.InstanceId).ToList();
            steps = recovery.Optional
                ?
                [
                    PublicTriggerStep("option", "mode", $"〈{recovery.Name}〉：预先声明是否发动墓地回收效果",
                        ["mode:none", "mode:use"]),
                    PublicTriggerStep("grave-card", "recoverTarget", recovery.PromptText,
                        targets, requiredChoice: "mode:use"),
                ]
                :
                [
                    PublicTriggerStep("grave-card", "recoverTarget", recovery.PromptText,
                        targets, allowCancel: false),
                ];
        }
        else if (batch6IBPlan == "jingke-kill")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "荆轲：预先声明是否返还1士气发动阵亡效果", ["mode:none", "mode:use"]),
                PublicTriggerStep("target-morale", "returnCost", "荆轲：预先选择返还的1张士气",
                    player.Morale.Select(card => card.InstanceId), requiredChoice: "mode:use"),
                PublicTriggerStep("field-legion", "killTarget", "荆轲：预先选择对方最多1张兵力不高于2000的军团",
                    PublicLegions(opponent).Where(card => card.Troops <= 2000).Select(card => card.InstanceId),
                    min: 0, max: 1, requiredChoice: "mode:use"),
            ];
        }
        else if (batch6IBPlan == "tutankhamun-top")
        {
            var targets = batch6IBPlan switch
            {
                "tutankhamun-top" => player.Graveyard.Where(card => CanEnterHandOrLibrary(card)
                        && card.CardId != "S01-0207"
                        && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng") && L12StructuredCardRules.CurrentCostAtMost(card, 4))
                    .Select(card => card.InstanceId),
                _ => [],
            };
            const string key = "recoverTarget";
            steps =
            [
                PublicTriggerStep("option", "mode", $"〈{source.Name}〉：预先声明是否发动可选阵亡效果", ["mode:none", "mode:use"]),
                PublicTriggerStep("grave-card", key,
                    $"〈{source.Name}〉：预先选择阵亡效果的公开目标", targets,
                    requiredChoice: "mode:use"),
            ];
        }
        else if (batch6IBPlan == "grave-legion-summon"
            && TryGetGraveLegionSummonTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var summon))
        {
            steps =
            [
                PublicTriggerStep("grave-card", "entryCard", summon.PromptText,
                    LegalGraveLegionSummonTargets(summon, player).Select(card => card.InstanceId), allowCancel: false),
                PublicTriggerStep("unused-slot", "entrySlot", $"{summon.Name}：选择活跃登场位置", EmptySlots(player),
                    allowCancel: false),
            ];
        }
        else if (batch6IBPlan == "harald-kill")
        {
            steps =
            [
                PublicTriggerStep("field-legion", "killTarget", "无情者哈拉尔：预先选择对方1张兵力不高于2000的军团",
                    PublicLegions(opponent).Where(card => card.Troops <= 2000).Select(card => card.InstanceId),
                    allowCancel: false),
            ];
        }
        else if (batch6IBPlan == "uesugi-counters")
        {
            var counters = player.Hand.Where(card => IsCounterDeploymentCandidate(card)).Select(card => card.InstanceId).ToList();
            var backSlots = Enumerable.Range(0, 3).Where(slot => player.Field[1][slot] is null)
                .Select(slot => $"1:{slot}").ToList();
            var maximum = Math.Min(2, Math.Min(counters.Count, backSlots.Count));
            steps =
            [
                PublicTriggerStep("option", "mode", "上杉谦信：预先声明是否从手牌盖伏反击战术", ["mode:none", "mode:use"]),
                PublicTriggerStep("hand-cards", "entryCards", "上杉谦信：私密选择1至2张反击战术",
                    counters, min: 1, max: maximum, requiredChoice: "mode:use"),
                PublicTriggerStep("composite-defense-slot", "entrySlot1", "上杉谦信：公开声明第1张反击战术的后排位置",
                    backSlots, referenceKey: "entryCards", requiredChoice: "mode:use"),
                PublicTriggerStep("composite-defense-slot", "entrySlot2", "上杉谦信：公开声明第2张反击战术的后排位置",
                    backSlots, referenceKey: "entryCards", requiredChoice: "mode:use", minReferenceCount: 2),
            ];
        }
        else if (batch6IBPlan == "hand-legion-summon"
            && TryGetHandLegionSummonTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var handSummon))
        {
            var cards = LegalHandLegionSummonTargets(handSummon, player).Select(card => card.InstanceId);
            steps = [];
            if (handSummon.Optional)
                steps.Add(PublicTriggerStep("option", "mode",
                    $"〈{source.Name}〉：预先声明是否从手牌登场军团", ["mode:none", "mode:use"]));
            steps.Add(PublicTriggerStep("hand-card", "entryCard", handSummon.PromptText, cards,
                min: handSummon.MinimumSelection, max: 1,
                requiredChoice: handSummon.Optional ? "mode:use" : null,
                allowCancel: handSummon.Optional));
            steps.Add(PublicTriggerStep("unused-slot", "entrySlot",
                $"{handSummon.Name}：公开声明{(handSummon.Tapped ? "休整" : "活跃")}登场位置",
                EmptySlots(player), referenceKey: "entryCard",
                requiredChoice: handSummon.Optional ? "mode:use" : null,
                minReferenceCount: 1, allowCancel: handSummon.Optional));
        }
        else if (batch6IBPlan == "gwen-choice")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "格温莉安：预先声明主宰回复1点血量或抽取1张牌",
                    ["mode:heal", "mode:draw"], allowCancel: false),
            ];
        }
        else if (VerifiedAtomicOptionalTriggerPlan(candidate.SourceCardId, candidate.Trigger) is { } verifiedOptional)
        {
            var optional = verifiedOptional.Atoms.First(atom => atom.Kind == L12AtomKinds.Optional);
            steps =
            [
                PublicTriggerStep("option", "mode",
                    optional.Parameters.GetValueOrDefault("prompt") ?? $"{source.Name}：预先声明是否发动可选触发效果",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6GAPlan == "limu-enter")
        {
            candidate.Data["preserveIndependentStack"] = "true";
            var available = player.Library.Count > 0 ? new[] { "mode:none", "mode:use" } : ["mode:none"];
            steps =
            [
                PublicTriggerStep("option", "revealMode", "李牧：预先声明是否展示牌库顶部1张牌",
                    available),
                PublicTriggerStep("option", "drawMode", "李牧：预先声明是否发动独立的随后抽取1张牌效果",
                    available),
            ];
        }
        else if (batch6GAPlan == "margaret-entry-mill")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "玛格丽特一世：预先声明是否弃置我方牌库顶部1张牌",
                    player.Library.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
            ];
        }
        else if (batch6GAPlan == "margaret-master-damage")
        {
            var canUse = State.ActivePlayer == candidate.Controller
                && FindOnField(player, candidate.SourceInstanceId, out _, out _) is { CardId: "S02-0304", Tapped: false };
            steps =
            [
                PublicTriggerStep("option", "mode", "玛格丽特一世：预先声明是否将此军团转为休整，使我方主宰增加1点血量",
                    canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
            ];
        }
        else if (batch6GAPlan == "anderstorp-draw")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", $"{source.Name}：预先声明是否发动本回合1次的可选触发效果",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6DPlan == "after-attack-katsura")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "桂小五郎：预先声明是否返回所有者牌库顶部",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6DPlan == "returned-top-katsura")
        {
            var morale = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).ToList();
            steps = morale.Count == 0 ? [] :
            [
                PublicTriggerStep("target-morale", "moraleTargets",
                    "桂小五郎：预先选择我方最多2张休整士气转为活跃",
                    morale, min: 0, max: Math.Min(2, morale.Count)),
            ];
        }
        else if (batch6DPlan == "owner-top-kusanagi")
        {
            steps =
            [
                PublicTriggerStep("option", "mode", "草薙剑：预先声明是否返回所有者牌库顶部",
                    ["mode:none", "mode:use"]),
            ];
        }
        else if (batch6DPlan == "tomb-construct")
        {
            var guards = TombConstructGuardPlans(source);
            candidate.Data["tombGuardIds"] = string.Join('|', guards.Select(entry => entry.Guard.InstanceId));
            candidate.Data["tombGuardOwners"] = string.Join('|', guards.Select(entry => entry.Owner));
            steps = guards.Select((entry, index) => PublicTriggerStep("owner-unused-slot", $"tombSlot{index}",
                $"陵墓构造体：选择第{index + 1}张〈陵墓守卫〉在所有者战场休整登场的位置",
                EmptySlots(State.Players[entry.Owner]), targetPlayerIndex: entry.Owner)).ToList();
        }
        else if (fifthBatchPlan == "blood-eagle")
        {
            var asgard = player.Graveyard.Where(card => card.InstanceId != candidate.SourceInstanceId
                    && CanEnterHandOrLibrary(card)
                    && L12StructuredCardRules.HasFaction(player, card, "asgard"))
                .Select(card => card.InstanceId).ToList();
            if (asgard.Count < 2)
            {
                var direct = CompositeFirstSegmentData("trigger:S01-0320",
                    new Dictionary<string, List<string>> { ["mode"] = ["mode:none"] });
                foreach (var pair in direct) candidate.Data[pair.Key] = pair.Value;
                candidate.Data["declaration-complete"] = "true";
                return false;
            }
            steps =
            [
                PublicTriggerStep("order", "graveOrder",
                    "复仇血鹰：预先选择并排序墓地2张【阿斯加德】卡牌；第1张加入手牌，第2张置于牌库底部",
                    asgard, min: 2, max: 2),
            ];
        }
        else if (fifthBatchPlan == "wisdom-reward")
        {
            candidate.Data["preserveIndependentStack"] = "true";
            var recover = player.Graveyard.Where(IsWisdomCodexRecoveryCandidate)
                .Select(card => card.InstanceId).ToList();
            if (recover.Count == 0)
            {
                var direct = CompositeFirstSegmentData("wisdom-reward:S01-0224",
                    new Dictionary<string, List<string>> { ["mode"] = ["mode:none"] });
                foreach (var pair in direct) candidate.Data[pair.Key] = pair.Value;
                candidate.Data["declaration-complete"] = "true";
                return false;
            }
            steps =
            [
                PublicTriggerStep("option", "mode", "智慧法典：选择是否将墓地最多1张其他战术或圣物加入手牌",
                    ["mode:none", "mode:recover"]),
                PublicTriggerStep("grave-card", "recoverTarget",
                    "智慧法典：预先选择墓地1张费用不高于3的其他战术或圣物",
                    recover, requiredChoice: "mode:recover"),
            ];
        }
        else switch ((candidate.SourceCardId, candidate.Trigger, candidate.Data.GetValueOrDefault("ability")))
        {
            case ("S02-04M1", "friendly-legion-moves", "tsukuyomiFollowMove"):
            {
                var movedId = candidate.Data.GetValueOrDefault("moved");
                var targets = State.Players.SelectMany(targetController => PublicLegions(targetController)
                        .Where(card => card.InstanceId != movedId))
                    .Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var canUse = targets.Count > 0 && ActiveResourceCount(player) > 0;
                steps =
                [
                    PublicTriggerStep("option", "mode", "月读：预先声明是否消耗1士气并位移另一张军团",
                        canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("composite-ordinary-payment", "cost", "月读：预先选择消耗的1份公开资源",
                        CompositeOrdinaryPaymentChoices(player), requiredChoice: "mode:use",
                        autoSelectEquivalentOrdinaryMorale: true),
                    PublicTriggerStep("field-legion", "target", "月读：预先选择双方战场另一张军团进行1格位移",
                        targets, requiredChoice: "mode:use"),
                    PublicTriggerStep("adjacent-slot", "slot", "月读：预先选择该军团位移后的相邻空位",
                        ["dynamic"], min: 0, max: 1, referenceKey: "target", requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S02-04M1", "friendly-front-to-back", "tsukuyomiReadyMorale"):
                steps =
                [
                    PublicTriggerStep("target-morale", "morale", "月读：预先选择1张休整士气转为活跃",
                        player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId)),
                ];
                break;
            case ("S02-0523", "trojan-after-attack", _):
            {
                var hostIndex = int.TryParse(candidate.Data.GetValueOrDefault("attacker"), out var attacker)
                    && attacker is >= 0 and <= 1 ? attacker : 1 - candidate.Controller;
                var slots = EmptySlots(State.Players[hostIndex]).ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "特洛伊木马：预先声明是否置入对方战场",
                        slots.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("enemy-slot", "slot", "特洛伊木马：预先选择置入对方战场的空位",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-02M3", "medjed-master-damage", _):
            {
                candidate.Data["cleanupReservation"] = "pending:medjedDamageResponse";
                var guards = player.Graveyard.Where(card => State.ActivePlayer == 1 - candidate.Controller
                        && card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "梅杰德：预先选择墓地1张〈陵墓守卫〉活跃登场，或不发动",
                        guards),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "梅杰德：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "梅杰德：预先选择〈陵墓守卫〉活跃登场的位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S02-02M1", "nephthys-own-death", _):
            {
                var scarabs = player.Graveyard.Where(card => card.CardId == PublicTriggerScarabCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "奈芙蒂斯：预先选择墓地1张〈增殖的甲虫〉活跃登场，或不发动",
                        scarabs),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "奈芙蒂斯：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "奈芙蒂斯：预先选择〈增殖的甲虫〉活跃登场的位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S02-01S1", "master-morale-return", _) when candidate.Data.GetValueOrDefault("mode") == "xiaotian":
            {
                var slots = Enumerable.Range(0, 3).Where(slot => player.Field[0][slot] is null)
                    .Select(slot => $"0:{slot}").ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "杨戬专属：预先声明是否使〈哮天犬·稚〉在前排活跃登场",
                        slots.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("slot", "slot", "哮天犬·稚：预先选择前排活跃登场的位置",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-0105", "enter", _):
            {
                var brothers = player.Hand.Where(card => card.CardId is "S01-0106" or "S01-0107")
                    .Select(card => card.InstanceId).ToList();
                var canUse = player.Morale.Count > 0;
                steps =
                [
                    PublicTriggerStep("option", "mode", "刘备：预先声明是否返还1士气并使关羽或张飞活跃登场",
                        canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("target-morale", "returnCost", "刘备：预先选择返还的1张士气",
                        player.Morale.Select(card => card.InstanceId), requiredChoice: "mode:use"),
                ];
                if (brothers.Count > 0 && EmptySlots(player).Any())
                {
                    steps.Add(PublicTriggerStep("hand-card", "entryCard", "刘备：预先选择手牌1张关羽或张飞",
                        brothers, requiredChoice: "mode:use"));
                    steps.Add(PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "刘备：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", requiredChoice: "mode:use"));
                    steps.Add(PublicTriggerStep("effect-entry-slot", "entrySlot", "刘备：预先选择活跃登场的位置",
                        ["dynamic"], referenceKey: "entryCard", requiredChoice: "mode:use"));
                }
                break;
            }
            case ("S01-0207", "enter", _):
            {
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).ToList();
                var maximum = Math.Min(2, Math.Min(guards.Count, EmptySlots(player).Count()));
                steps = maximum == 0 ? [] :
                [
                    PublicTriggerStep("cards", "entryCards", "图坦卡蒙：预先选择墓地最多2张陵墓守卫活跃登场",
                        guards, min: 1, max: maximum),
                    PublicTriggerStep("unused-slot", "entrySlot1", "图坦卡蒙：预先选择第1张陵墓守卫登场位置",
                        EmptySlots(player)),
                    PublicTriggerStep("unused-slot", "entrySlot2", "图坦卡蒙：预先选择第2张陵墓守卫登场位置",
                        EmptySlots(player), referenceKey: "entryCards", minReferenceCount: 2),
                ];
                break;
            }
            case ("S01-0208", "enter", _):
            case ("S02-0202", "death", _):
            {
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    PublicTriggerStep("grave-card", "entryCard", $"{source.Name}：预先选择墓地1张陵墓守卫登场", guards),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", $"{source.Name}：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard"),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", $"{source.Name}：预先选择登场位置",
                        ["dynamic"], referenceKey: "entryCard"),
                ];
                break;
            }
            case ("S01-0309", "enter", _):
            {
                var sigurd = player.Hand.Concat(player.Graveyard).Where(card => card.CardId == PublicTriggerSigurdCard)
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "布伦希尔德：预先声明是否支付1点主宰伤害费用",
                        CanPayMasterDamageCost(player, 1) ? ["mode:none", "mode:use"] : ["mode:none"]),
                ];
                if (sigurd.Count > 0 && EmptySlots(player).Any())
                {
                    steps.Add(PublicTriggerStep("card", "entryCard", "布伦希尔德：预先选择手牌或墓地的齐格鲁德",
                        sigurd, requiredChoice: "mode:use"));
                    steps.Add(PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "布伦希尔德：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", requiredChoice: "mode:use"));
                    steps.Add(PublicTriggerStep("effect-entry-slot", "entrySlot", "布伦希尔德：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", requiredChoice: "mode:use"));
                }
                break;
            }
            case ("S01-0021", "reaction", _):
            {
                var legions = player.Hand.Where(card => card.CardType == "legion" && L12StructuredCardRules.CurrentCostAtMost(card, 3)
                        && EffectEntryBattlefieldChoices(candidate.Controller, card).Any())
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    PublicTriggerStep("hand-card", "entryCard", "摄政皇权：预先选择手牌1张费用不高于3的军团", legions,
                        allowCancel: false),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "摄政皇权：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", allowCancel: false),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "摄政皇权：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", allowCancel: false),
                ];
                break;
            }
            case ("S01-0213", "reaction", _):
            {
                var slots = EmptySlots(player).ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "锡瓦的卡巴：预先声明是否从手牌活跃登场",
                        slots.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("slot", "entrySlot", "锡瓦的卡巴：预先选择活跃登场位置",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-0223", "reaction", _):
            {
                candidate.Data["preserveIndependentStack"] = "true";
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "不朽之礼：预先声明是否发动抽取1张牌",
                        ["mode:none", "mode:use"]),
                    PublicTriggerStep("optional-card", "entryCard", "不朽之礼：预先声明抽牌后登场的陵墓守卫，或不登场",
                        guards, requiredChoice: "mode:use"),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "不朽之礼：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true,
                        requiredChoice: "mode:use"),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "不朽之礼：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true,
                        requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S02-0203", "enter", _):
            case ("S02-0205", "enter", _):
            {
                var scarabs = player.Graveyard.Where(card => card.CardId == PublicTriggerScarabCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", $"{source.Name}：预先选择墓地1张增殖的甲虫，或不发动", scarabs),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", $"{source.Name}：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", $"{source.Name}：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S01-0206", "attack", _):
            {
                var guards = PublicLegions(player).Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "moveTarget", "萨拉丁：预先选择位移的陵墓守卫，或不发动", guards),
                    PublicTriggerStep("unused-slot", "moveSlot", "萨拉丁：预先选择陵墓守卫位移后的位置",
                        EmptySlots(player), referenceKey: "moveTarget", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S01-0201", "attack" or "death", _):
            {
                candidate.Data["preserveIndependentStack"] = "true";
                foreach (var pair in CompositeFirstSegmentData(
                             $"trigger:S01-0201:{candidate.Trigger}",
                             new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)))
                    candidate.Data[pair.Key] = pair.Value;
                // 第二段必须读取减兵与阵亡检查完成后的场面；这里不提前锁定目标。
                steps = [];
                break;
            }
            case ("S01-0315", "enter", _):
                steps =
                [
                    PublicTriggerStep("option", "mode", "无骨者伊瓦尔：预先声明是否查看牌库顶部3张牌",
                        player.Library.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                ];
                break;
            case ("S01-0407", "enter", _):
            {
                var emptySlotAvailable = EmptySlots(player).Any();
                var moverCards = PublicLegions(player)
                    .Where(card => emptySlotAvailable || card.Tapped).ToList();
                var movers = moverCards.Select(card => card.InstanceId).ToList();
                var minimum = emptySlotAvailable ? 1 : 2;
                var canUse = movers.Count >= minimum;
                steps =
                [
                    PublicTriggerStep("option", "mode", "坂本龙马：预先声明是否位移我方最多2张军团",
                        canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("cards", "moveTargets", "坂本龙马：预先选择并排序要位移的最多2张军团",
                        movers, min: minimum, max: Math.Min(2, movers.Count), requiredChoice: "mode:use"),
                    PublicTriggerStep("public-move-slot", "moveSlot1", "坂本龙马：预先选择第1张军团位移位置",
                        ["dynamic"], referenceKey: "moveTargets", requiredChoice: "mode:use"),
                    PublicTriggerStep("public-move-slot", "moveSlot2", "坂本龙马：预先选择第2张军团位移位置",
                        ["dynamic"], referenceKey: "moveTargets", minReferenceCount: 2, referenceChoiceIndex: 1,
                        requiredChoice: "mode:use"),
                ];
                break;
            }
        }

        if (steps is null) return false;
        if (ShouldSilentlySkipUnavailableOptionalTrigger(steps))
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            AdvanceTriggerBatches();
            return true;
        }
        if (steps.Count == 0 && !RequiresPrideMasterSurcharge(candidate.Controller, source))
        {
            candidate.Data["declaration-complete"] = "true";
            return false;
        }
        var result = BeginPendingActivationSequence(candidate.Controller, source, "public-trigger-declaration",
            steps, candidate.CandidateId);
        if (result.Accepted) return true;

        RemoveUnstackedTriggerCandidate(candidate, result.Error ?? "公开声明已失效，效果未入栈");
        return true;
    }

    /// <summary>
    /// A public optional trigger whose declaration has collapsed to an explicit decline-only
    /// choice has no legal effect to activate.  Do not expose an implementation-detail prompt
    /// or leave a trigger candidate waiting for the player to acknowledge that fact.  Every
    /// following step must be conditional on accepting the unavailable option; an independent
    /// mandatory continuation therefore remains eligible and is not swallowed by this guard.
    /// </summary>
    private static bool ShouldSilentlySkipUnavailableOptionalTrigger(
        IReadOnlyList<L12ActivationSelectionStep> steps)
    {
        if (steps.Count == 0 || steps[0].Kind is not ("option" or "optional-card")) return false;
        var choices = steps[0].ValidChoices;
        if (choices.Count == 0 || choices.Any(choice => !IsPublicTriggerDeclineChoice(choice))) return false;
        return steps.Skip(1).All(step => step.RequiredDeclaredChoice is not null
            || step.SkipWhenReferenceIsNone || step.SkipWhenPreviousStepEmpty);
    }

    private static bool IsPublicTriggerDeclineChoice(string choice)
        => choice is "mode:none" or "skip" or "decline" or "refuse" or "cancel";

    private static L12ActivationSelectionStep PublicTriggerStep(string kind, string key, string text,
        IEnumerable<string> choices, int min = 1, int max = 1, string? referenceKey = null,
        bool skipWhenReferenceIsNone = false, string? requiredChoice = null,
        int minReferenceCount = 0, int referenceChoiceIndex = 0, int? targetPlayerIndex = null,
        bool allowCancel = true, string? selectionConstraint = null,
        bool autoSelectEquivalentOrdinaryMorale = false)
        => new()
        {
            Kind = kind,
            DeclarationKey = key,
            Text = text,
            ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = min,
            MaxChoose = max,
            CancellationPolicy = !allowCancel
                ? L12ActivationCancellationPolicy.NotAllowed
                : requiredChoice is not null
                    ? L12ActivationCancellationPolicy.SeparateChoice
                    : L12ActivationCancellationPolicy.WhenNoExplicitDecline,
            ReferenceDeclarationKey = referenceKey,
            PreviewPresentation = referenceKey is not null
                && kind is "effect-entry-battlefield" or "effect-entry-slot"
                    ? "handled-card"
                    : null,
            SkipWhenReferenceIsNone = skipWhenReferenceIsNone,
            RequiredDeclaredChoice = requiredChoice,
            MinimumReferenceCount = minReferenceCount,
            ReferenceChoiceIndex = referenceChoiceIndex,
            TargetPlayerIndex = targetPlayerIndex,
            SelectionConstraint = selectionConstraint,
            AutoSelectEquivalentOrdinaryMorale = autoSelectEquivalentOrdinaryMorale,
            ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = "不发动",
                ["mode:use"] = "发动",
            },
        };

    private List<(L12CardInstance Guard, int Owner)> TombConstructGuardPlans(L12CardInstance source)
    {
        var plans = new List<(L12CardInstance Guard, int Owner)>();
        var usedByOwner = new Dictionary<int, int>();
        foreach (var instanceId in source.LastKnownAttachedCardIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
        {
            var guard = FindAuthoritativeCard(instanceId);
            if (guard?.CardId != PublicTriggerTombGuardCard) continue;
            var owner = Enumerable.Range(0, State.Players.Length)
                .FirstOrDefault(index => State.Players[index].Graveyard.Contains(guard), -1);
            if (owner < 0) continue;
            var used = usedByOwner.GetValueOrDefault(owner);
            if (used >= EmptySlots(State.Players[owner]).Count()) continue;
            plans.Add((guard, owner));
            usedByOwner[owner] = used + 1;
        }
        return plans;
    }

    private bool TryCompletePublicTriggerDeclaration(L12TriggerCandidate candidate, L12PendingActivation activation)
    {
        if (TryCompleteSimpleCardStateTriggerDeclaration(candidate, activation))
            return true;
        if (TryCompleteStarterTargetedTriggerDeclaration(candidate, activation))
            return true;
        if (TryCompleteTrialAdvanceTriggerDeclaration(candidate, activation))
            return true;
        if (TryCompleteBatch6JAEnterDeclaration(candidate, activation))
            return true;
        if (TryCompleteTrialCompletionTriggerDeclaration(candidate, activation))
            return true;
        if (TryCompleteAttackPublicTriggerDeclaration(candidate, activation))
            return true;
        if (TryCompleteSimpleResourceTriggerDeclaration(candidate, activation))
            return true;
        var key = (candidate.SourceCardId, candidate.Trigger, candidate.Data.GetValueOrDefault("ability"));
        var batch6JBPlan = Batch6JBPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        var batch6IBPlan = Batch6IBPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var batch6GAPlan = Batch6GAPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        var batch6DPlan = Batch6DPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var fifthBatchPlan = FifthBatchPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var verifiedAtomicOptional = VerifiedAtomicOptionalTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var handled = batch6JBPlan is not null || batch6IBPlan is not null || verifiedAtomicOptional is not null || batch6GAPlan is not null || batch6DPlan is not null
            || fifthBatchPlan is not null || key switch
        {
            ("S02-0006", "discard-trigger", _) => true,
            ("S02-04M1", "friendly-legion-moves", "tsukuyomiFollowMove") => true,
            ("S02-04M1", "friendly-front-to-back", "tsukuyomiReadyMorale") => true,
            ("S02-0523", "trojan-after-attack", _) => true,
            ("S01-02M3", "medjed-master-damage", _) => true,
            ("S02-02M1", "nephthys-own-death", _) => true,
            ("S02-01S1", "master-morale-return", _) => true,
            ("S01-0105", "enter", _) => true,
            ("S01-0207", "enter", _) => true,
            ("S01-0208", "enter", _) => true,
            ("S01-0309", "enter", _) => true,
            ("S01-0021", "reaction", _) => true,
            ("S01-0213", "reaction", _) => true,
            ("S01-0223", "reaction", _) => true,
            ("S02-0202", "death", _) => true,
            ("S02-0203", "enter", _) => true,
            ("S02-0205", "enter", _) => true,
            ("S01-0206", "attack", _) => true,
            ("S01-0201", "attack" or "death", _) => true,
            ("S01-0315", "enter", _) => true,
            ("S01-0407", "enter", _) => true,
            _ => false,
        };
        if (!handled)
            return false;

        var player = State.Players[candidate.Controller];
        var declaredSource = FindAuthoritativeCard(candidate.SourceInstanceId)
            ?? candidate.SourceSnapshot ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        var entryCard = activation.DeclaredValues.GetValueOrDefault("entryCard", []).SingleOrDefault();
        var declaredNone = activation.DeclaredValues.Values.Any(values =>
            values.Contains("mode:none", StringComparer.OrdinalIgnoreCase));
        if (batch6GAPlan == "limu-enter")
        {
            var revealMode = activation.DeclaredValues.GetValueOrDefault("revealMode", []).SingleOrDefault();
            var drawMode = activation.DeclaredValues.GetValueOrDefault("drawMode", []).SingleOrDefault();
            if (revealMode == "mode:none" && drawMode == "mode:none")
            {
                State.PendingTriggerStackCandidates.Remove(candidate);
                AddEvent("ability-cancelled", candidate.Controller,
                    "〈李牧〉的展示与随后抽牌均未发动，未生成空堆叠项");
                AdvanceTriggerBatches();
                return true;
            }
        }
        if (key is ("S01-0223", "reaction", _) && mode == "mode:none")
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                "〈不朽之礼〉未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }
        if (declaredNone && candidate.Data.GetValueOrDefault("preserveIndependentStack") != "true")
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            var atomicCard = _catalog.AtomicEffects.Find(candidate.SourceCardId);
            if (atomicCard is not null
                && L12SingleSegmentTriggeredEffectPresentations.TryResolveScene(atomicCard,
                    candidate.Trigger, out var declinedSceneId))
                AddPresentationEventById("effect-declined", candidate.Controller,
                    $"〈{candidate.SourceName}〉的可选触发效果选择不发动，未进入堆叠",
                    declinedSceneId, declaredSource);
            else
                AddEvent("ability-cancelled", candidate.Controller,
                    $"〈{candidate.SourceName}〉的可选触发效果未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        string? error = null;
        if (batch6JBPlan is not null && candidate.Data.GetValueOrDefault("batch6JBConditionLocked") != "true")
            error = $"〈{candidate.SourceName}〉的公开触发条件未在候选建立时锁定；效果未入栈";
        else if (batch6IBPlan is not null && candidate.Data.GetValueOrDefault("batch6IBConditionLocked") != "true")
            error = $"〈{candidate.SourceName}〉的公开触发条件未在候选建立时锁定；效果未入栈";
        else if (verifiedAtomicOptional is not null
            && candidate.Data.GetValueOrDefault("verifiedAtomicConditionLocked") != "true")
            error = $"〈{candidate.SourceName}〉的可选原子条件未在触发时点锁定；效果未入栈";
        if (error is null && batch6JBPlan == "gustav-ready" && mode == "mode:use")
        {
            var onceKey = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var pendingKey = candidate.Data.GetValueOrDefault("cleanupReservation") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(onceKey) || string.IsNullOrWhiteSpace(pendingKey)
                || player.UsedAbilities.Contains(onceKey) || !player.UsedAbilities.Contains(pendingKey))
                error = $"〈{candidate.SourceName}〉的次数保留已失效；效果未入栈";
        }
        if (error is null && batch6JBPlan == "lubu-ready")
        {
            var returnedMorale = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
            if (mode == "mode:use" && (FindOnField(player, candidate.SourceInstanceId, out _, out _) is null
                || returnedMorale.Count != 4 || !CanReturnSelectedMoraleById(player, returnedMorale, 4)))
                error = "吕布声明的4张士气或来源已失效；未支付费用且效果未入栈";
        }
        else if (batch6JBPlan == "mulan-lock-morale")
        {
            var target = activation.DeclaredValues.GetValueOrDefault("moraleTarget", []).SingleOrDefault();
            if (target is null || State.ActivePlayer == candidate.Controller
                || !State.Players[1 - candidate.Controller].Morale.Any(card => card.InstanceId == target && card.Tapped))
                error = "花木兰声明的对方休整士气目标已失效；效果未入栈";
        }
        else if (batch6JBPlan == "gustav-ready")
        {
            var costValues = activation.DeclaredValues.GetValueOrDefault("graveCost", [])
                .Concat(activation.DeclaredValues.GetValueOrDefault("graveCostCopies", [])).ToArray();
            if (mode == "mode:use" && (FindOnField(player, candidate.SourceInstanceId, out _, out _) is null
                || !L12StructuredCardRules.TryResolveGraveCostDeclaration(player, costValues, 2,
                    string.Empty, legionOnly: false, out _, out _)))
                error = "古斯塔夫一世声明的墓地费用或来源已失效；未支付费用且效果未入栈";
            else if (mode == "mode:use")
            {
                _ = L12StructuredCardRules.TryResolveGraveCostDeclaration(player, costValues, 2,
                    string.Empty, legionOnly: false, out var physicalCosts, out _);
                MoveGraveToLibraryBottom(player, physicalCosts);
                AddEvent("cost", candidate.Controller,
                    $"古斯塔夫一世在入栈前将墓地{physicalCosts.Length}张实体卡牌依声明顺序置于牌库底部，合计视为2张",
                    declaredSource);
            }
        }
        else if (batch6JBPlan == "exorcist-return" && mode == "mode:use"
            && FindOnField(player, candidate.SourceInstanceId, out _, out _) is null)
            error = "驱魔道士 陆瑛已不在战场；效果未入栈";
        else if (batch6JBPlan == "prayer-private")
        {
            var payment = new L12CompositeEffectSegmentSpec("prayer-private", "私下查看天灾",
                "mode:use", "ordinary-payment", "cost", 1, PreStackCost: true);
            if (mode == "mode:use" && (State.DisasterDeck.Count == 0
                || !TryPayCompositeDeclaredCost(candidate.Controller, declaredSource,
                    payment, activation.DeclaredValues)))
                error = "祷告仪式声明的资源费用或天灾牌库已失效；未支付费用且效果未入栈";
        }
        else if (batch6IBPlan == "grave-to-hand"
            && TryGetGraveToHandTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var graveRecoverySpec))
        {
            var target = activation.DeclaredValues.GetValueOrDefault("recoverTarget", []).SingleOrDefault();
            if ((!graveRecoverySpec.Optional || mode == "mode:use")
                && (!GraveToHandTriggerConditionMet(graveRecoverySpec) || target is null
                || !player.Graveyard.Any(card => card.InstanceId == target
                    && IsLegalGraveToHandTarget(graveRecoverySpec, player, card))))
                error = $"{graveRecoverySpec.Name}声明的墓地目标已失效；效果未入栈";
        }
        else if (batch6IBPlan == "jingke-kill")
        {
            var cost = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
            var target = activation.DeclaredValues.GetValueOrDefault("killTarget", []);
            if (mode == "mode:use" && (cost.Count != 1 || !CanReturnSelectedMoraleById(player, cost, 1)
                || target.Count > 1 || target.Any(id => DeclaredEnemyTarget(candidate.Controller, id,
                    card => card.Troops <= 2000) is null)))
                error = "荆轲声明的士气费用或公开目标已失效；未支付费用且效果未入栈";
            else if (mode == "mode:use")
            {
                _ = ReturnSelectedMoraleById(player, cost, 1);
                candidate.Data["return-morale-prepaid"] = "true";
                AddEvent("cost", candidate.Controller, "荆轲在入栈前返还1张已声明士气");
            }
        }
        else if (batch6IBPlan == "tutankhamun-top")
        {
            var target = activation.DeclaredValues.GetValueOrDefault("recoverTarget", []).SingleOrDefault();
            if (mode == "mode:use" && (target is null || !player.Graveyard.Any(card => card.InstanceId == target
                && CanEnterHandOrLibrary(card) && card.CardId != "S01-0207"
                && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
                && L12StructuredCardRules.CurrentCostAtMost(card, 4))))
                error = "图坦卡蒙声明的墓地目标已失效；效果未入栈";
        }
        else if (batch6IBPlan == "grave-legion-summon"
            && TryGetGraveLegionSummonTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var summonSpec))
        {
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && IsLegalGraveLegionSummonTarget(summonSpec, player, card))
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = $"{summonSpec.Name}声明的墓地军团或登场位置已失效；效果未入栈";
        }
        else if (batch6IBPlan == "harald-kill")
        {
            var target = activation.DeclaredValues.GetValueOrDefault("killTarget", []).SingleOrDefault();
            if (DeclaredEnemyTarget(candidate.Controller, target, card => card.Troops <= 2000) is null)
                error = "无情者哈拉尔的强制公开目标已失效；效果未入栈";
        }
        else if (batch6IBPlan == "uesugi-counters")
        {
            var cards = activation.DeclaredValues.GetValueOrDefault("entryCards", []);
            var slots = new[] { "entrySlot1", "entrySlot2" }
                .SelectMany(name => activation.DeclaredValues.GetValueOrDefault(name, [])).ToArray();
            if (mode == "mode:use" && (cards.Count is < 1 or > 2 || cards.Count != slots.Length
                || cards.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Count
                || slots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Length
                || cards.Any(id => !player.Hand.Any(card => card.InstanceId == id && IsCounterTactic(card.CardId)))
                || slots.Any(slot => !Enumerable.Range(0, 3).Where(index => player.Field[1][index] is null)
                    .Select(index => $"1:{index}").Contains(slot, StringComparer.OrdinalIgnoreCase))))
                error = "上杉谦信声明的私密反击战术或公开后排位置已失效；效果未入栈";
        }
        else if (batch6IBPlan == "hand-legion-summon"
            && TryGetHandLegionSummonTriggerSpec(candidate.SourceCardId, candidate.Trigger, out var handSummon))
        {
            var entries = activation.DeclaredValues.GetValueOrDefault("entryCard", []);
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            var expectsSelection = !handSummon.Optional || mode == "mode:use";
            var validEmpty = entries.Count == 0 && handSummon.MinimumSelection == 0
                && string.IsNullOrWhiteSpace(slot);
            var validSelected = entries.Count == 1
                && player.Hand.Any(card => card.InstanceId == entries[0]
                    && IsLegalHandLegionSummonTarget(handSummon, player, card))
                && slot is not null && EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase);
            if (expectsSelection && !validEmpty && !validSelected)
                error = $"〈{candidate.SourceName}〉声明的私密手牌军团或公开登场位置已失效；效果未入栈";
        }
        else if (batch6IBPlan == "gwen-choice" && (mode is not ("mode:heal" or "mode:draw")
            || candidate.Data.GetValueOrDefault("cause") != "effect"))
            error = "格温莉安选择的阵亡原因或效果已失效；效果未入栈";
        else if (batch6GAPlan == "limu-enter")
        {
            var revealMode = activation.DeclaredValues.GetValueOrDefault("revealMode", []).SingleOrDefault();
            var drawMode = activation.DeclaredValues.GetValueOrDefault("drawMode", []).SingleOrDefault();
            if (revealMode is not ("mode:none" or "mode:use")
                || drawMode is not ("mode:none" or "mode:use"))
                error = "李牧选择的展示或随后抽牌效果不完整；效果未入栈";
            else if ((revealMode == "mode:use" || drawMode == "mode:use") && player.Library.Count == 0)
                error = "李牧的牌库已空；声明的效果未入栈";
        }
        else if (batch6GAPlan == "margaret-entry-mill")
        {
            if (mode == "mode:use" && player.Library.Count == 0)
                error = "玛格丽特一世的牌库顶已失效；效果未入栈";
        }
        else if (batch6GAPlan == "margaret-master-damage")
        {
            var margaret = FindOnField(player, candidate.SourceInstanceId, out _, out _);
            if (mode == "mode:use" && (State.ActivePlayer != candidate.Controller
                || margaret is not { CardId: "S02-0304", Tapped: false }))
                error = "玛格丽特一世的公开休整费用已失效；未支付费用且效果未入栈";
            else if (mode == "mode:use")
            {
                margaret!.Tapped = true;
                AddEvent("cost", candidate.Controller, "玛格丽特一世入栈前转为休整", margaret);
            }
        }
        else if (batch6DPlan == "tomb-construct")
        {
            var guardIds = candidate.Data.GetValueOrDefault("tombGuardIds", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            var ownerTexts = candidate.Data.GetValueOrDefault("tombGuardOwners", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            var slots = Enumerable.Range(0, guardIds.Length)
                .Select(index => activation.DeclaredValues.GetValueOrDefault($"tombSlot{index}", []).SingleOrDefault())
                .ToArray();
            if (ownerTexts.Length != guardIds.Length || slots.Length != guardIds.Length
                || ownerTexts.Select(text => int.TryParse(text, out var owner) ? owner : -1)
                    .Any(owner => owner is < 0 or > 1))
                error = "陵墓构造体的所有者区域声明不完整；效果未入栈";
            else
            {
                var owners = ownerTexts.Select(int.Parse).ToArray();
                for (var index = 0; index < guardIds.Length && error is null; index++)
                {
                    var owner = State.Players[owners[index]];
                    if (!owner.Graveyard.Any(card => card.InstanceId == guardIds[index]
                            && card.CardId == PublicTriggerTombGuardCard)
                        || slots[index] is null
                        || !EmptySlots(owner).Contains(slots[index]!, StringComparer.OrdinalIgnoreCase))
                        error = "陵墓构造体声明的守卫或所有者位置已失效；效果未入栈";
                    else if (Enumerable.Range(0, index).Any(prior => owners[prior] == owners[index]
                        && string.Equals(slots[prior], slots[index], StringComparison.OrdinalIgnoreCase)))
                        error = "陵墓构造体不能为同一所有者的多张守卫声明重复位置；效果未入栈";
                }
            }
        }
        else if (batch6DPlan == "returned-top-katsura")
        {
            var moraleTargets = activation.DeclaredValues.GetValueOrDefault("moraleTargets", []);
            if (moraleTargets.Count > 2
                || moraleTargets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != moraleTargets.Count
                || moraleTargets.Any(id => !player.Morale.Any(card => card.InstanceId == id && card.Tapped)))
                error = "桂小五郎声明的休整士气目标已失效；效果未入栈";
        }
        else if (key == ("S02-04M1", "friendly-legion-moves", "tsukuyomiFollowMove"))
        {
            var cost = activation.DeclaredValues.GetValueOrDefault("cost", []);
            var targetId = activation.DeclaredValues.GetValueOrDefault("target", []).SingleOrDefault();
            var slot = activation.DeclaredValues.GetValueOrDefault("slot", []).SingleOrDefault();
            var target = FindPublicCard(targetId, out var targetController);
            var targetPlayer = targetController is >= 0 and <= 1 ? State.Players[targetController] : null;
            var row = -1;
            var oldSlot = -1;
            var targetOnField = targetPlayer is null ? null
                : FindOnField(targetPlayer, targetId, out row, out oldSlot);
            var onceKey = L12MasterTriggeredUsageRules.Key("tsukuyomiFollowMove", player.PlayerIndex, State.TurnSerial);
            var adjacentSlots = targetPlayer is null || targetOnField is null
                ? [] : AdjacentEmptySlots(targetPlayer, row, oldSlot).ToArray();
            if (player.UsedAbilities.Contains(onceKey) || targetOnField is null
                || !IsFieldLegion(targetOnField) || targetOnField.Hidden
                || targetOnField.InstanceId == candidate.Data.GetValueOrDefault("moved")
                || slot is not null && !adjacentSlots.Contains(slot, StringComparer.OrdinalIgnoreCase)
                || slot is null && adjacentSlots.Length > 0
                || !CanConsumeSelectedResources(player, 1, cost))
                error = "月读的费用、公开目标或位移位置已失效；未支付费用且效果未入栈";
            else
            {
                _ = TryConsumeSelectedResources(player, 1, cost);
                player.UsedAbilities.Add(onceKey);
                candidate.Data["targetPlayerIndex"] = targetController.ToString();
            }
        }
        else if (key == ("S02-04M1", "friendly-front-to-back", "tsukuyomiReadyMorale"))
        {
            var moraleId = activation.DeclaredValues.GetValueOrDefault("morale", []).SingleOrDefault();
            if (!player.Morale.Any(card => card.InstanceId == moraleId && card.Tapped))
                error = "月读声明的休整士气已失效；效果未入栈";
        }
        else if (key.Item1 == "S02-0523")
        {
            var hostIndex = int.TryParse(candidate.Data.GetValueOrDefault("attacker"), out var attacker)
                && attacker is >= 0 and <= 1 ? attacker : 1 - candidate.Controller;
            var slot = activation.DeclaredValues.GetValueOrDefault("slot", []).SingleOrDefault();
            if (!IsSetTrojanHorse(FindOnField(player, candidate.SourceInstanceId, out _, out _))
                || slot is null || !EmptySlots(State.Players[hostIndex]).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "特洛伊木马的来源或公开置入位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-02M3")
        {
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (State.ActivePlayer != 1 - candidate.Controller
                || entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerTombGuardCard)
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase)
                || player.UsedAbilities.Contains(L12MasterTriggeredUsageRules.Key("medjedDamageResponse", player.PlayerIndex, State.TurnSerial)))
                error = "梅杰德的公开军团或登场位置已失效；效果未入栈";
            else
                player.UsedAbilities.Add(L12MasterTriggeredUsageRules.Key("medjedDamageResponse", player.PlayerIndex, State.TurnSerial));
        }
        else if (key.Item1 == "S02-02M1")
        {
            var onceKey = L12MasterTriggeredUsageRules.Key("nephthysScarab", player.PlayerIndex, State.TurnSerial);
            var pendingKey = candidate.Data.GetValueOrDefault("cleanupReservation") ?? string.Empty;
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (State.ActivePlayer == candidate.Controller || player.MasterId != "S02-02M1"
                || player.UsedAbilities.Contains(onceKey) || string.IsNullOrWhiteSpace(pendingKey)
                || !player.UsedAbilities.Contains(pendingKey) || entryCard is null
                || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerScarabCard)
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "奈芙蒂斯的公开军团或登场位置已失效；效果未入栈";
            else
            {
                player.UsedAbilities.Add(onceKey);
                candidate.Data["onceKey"] = onceKey;
            }
        }
        else if (key is ("S02-01S1", "master-morale-return", _)
            && candidate.Data.GetValueOrDefault("mode") == "xiaotian")
        {
            var onceKey = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var slot = activation.DeclaredValues.GetValueOrDefault("slot", []).SingleOrDefault();
            if (string.IsNullOrWhiteSpace(onceKey) || player.UsedAbilities.Contains(onceKey)
                || slot is null || !Enumerable.Range(0, 3).Where(index => player.Field[0][index] is null)
                    .Select(index => $"0:{index}").Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "哮天犬·稚的公开登场位置已失效；效果未入栈";
            else
                player.UsedAbilities.Add(onceKey);
        }
        else if (key.Item1 == "S01-0105")
        {
            var costId = activation.DeclaredValues.GetValueOrDefault("returnCost", []).SingleOrDefault();
            if (costId is null || !CanReturnSelectedMoraleById(player, [costId], 1)
                || entryCard is not null && (!player.Hand.Any(card => card.InstanceId == entryCard
                    && card.CardId is "S01-0106" or "S01-0107")
                    || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Hand)))
                error = "刘备的士气费用、关羽/张飞或登场位置已失效；未返还士气且效果未入栈";
            else
                _ = ReturnSelectedMoraleById(player, [costId], 1);
        }
        else if (key.Item1 == "S01-0207")
        {
            var cards = activation.DeclaredValues.GetValueOrDefault("entryCards", []);
            var slots = new[] { "entrySlot1", "entrySlot2" }
                .SelectMany(name => activation.DeclaredValues.GetValueOrDefault(name, [])).ToArray();
            if (cards.Count is < 1 or > 2 || slots.Length != cards.Count
                || cards.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Count
                || slots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Length
                || cards.Any(id => !player.Graveyard.Any(card => card.InstanceId == id
                    && card.CardId == PublicTriggerTombGuardCard))
                || slots.Any(slot => !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase)))
                error = "图坦卡蒙声明的陵墓守卫或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 is "S01-0208" or "S02-0202")
        {
            if (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerTombGuardCard)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Graveyard))
                error = $"{candidate.SourceName}声明的陵墓守卫或登场位置已失效；效果未入栈";
        }
        else if (key is ("S01-0309", "enter", _))
        {
            var sigurdZone = player.Hand.Concat(player.Graveyard).ToList();
            if (!CanPayMasterDamageCost(player, 1) || entryCard is not null && (!sigurdZone.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerSigurdCard)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, sigurdZone)))
                error = "布伦希尔德声明的齐格鲁德或登场位置已失效；未承受伤害且效果未入栈";
            else
                if (!PayMasterDamageCostAndCanContinue(candidate.Controller, 1, "布伦希尔德登场效果费用")) return true;
        }
        else if (key.Item1 == "S01-0021")
        {
            if (entryCard is null || !player.Hand.Any(card => card.InstanceId == entryCard
                    && card.CardType == "legion" && L12StructuredCardRules.CurrentCostAtMost(card, 3))
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Hand))
                error = "摄政皇权声明的手牌军团或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0213")
        {
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (!player.Hand.Any(card => card.InstanceId == candidate.SourceInstanceId)
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "锡瓦的卡巴来源或公开登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0223")
        {
            if (entryCard != "mode:none"
                && (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                        && card.CardId == PublicTriggerTombGuardCard)
                    || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Graveyard)))
                error = "不朽之礼选择的陵墓守卫或登场位置已失效；抽取1张牌的效果仍会结算";
            if (error is not null)
            {
                activation.DeclaredValues["entryCard"] = ["mode:none"];
                activation.DeclaredValues.Remove("entryBattlefield");
                activation.DeclaredValues.Remove("entrySlot");
                AddEvent("effect-cancelled", candidate.Controller, error);
                error = null;
            }
            activation.DeclaredValues["entryMode"] =
                activation.DeclaredValues.GetValueOrDefault("entryCard", []).SingleOrDefault() == "mode:none"
                    ? ["mode:none"]
                    : ["mode:summon"];
        }
        else if (fifthBatchPlan == "blood-eagle")
        {
            var order = activation.DeclaredValues.GetValueOrDefault("graveOrder", []);
            if (order.Count != 2 || order.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
                || order.Any(id => !player.Graveyard.Any(card => card.InstanceId == id
                    && card.InstanceId != candidate.SourceInstanceId && CanEnterHandOrLibrary(card)
                    && L12StructuredCardRules.HasFaction(player, card, "asgard"))))
                error = "复仇血鹰选择的墓地顺序已失效；此前的兵力减少效果仍会进入堆叠";
            if (error is not null)
            {
                activation.DeclaredValues.Clear();
                activation.DeclaredValues["mode"] = ["mode:none"];
                AddEvent("effect-cancelled", candidate.Controller, error);
                error = null;
            }
            else
                activation.DeclaredValues["mode"] = ["mode:recover"];
        }
        else if (fifthBatchPlan == "wisdom-reward")
        {
            var recovery = activation.DeclaredValues.GetValueOrDefault("recoverTarget", []).SingleOrDefault();
            if (mode == "mode:recover" && (recovery is null || !player.Graveyard.Any(card =>
                    card.InstanceId == recovery && IsWisdomCodexRecoveryCandidate(card))))
            {
                activation.DeclaredValues["mode"] = ["mode:none"];
                activation.DeclaredValues.Remove("recoverTarget");
                AddEvent("effect-cancelled", candidate.Controller,
                    "智慧法典选择的墓地目标已失效；抽取1张牌的效果仍会结算");
            }
        }
        else if (key is ("S02-0203" or "S02-0205", "enter", _))
        {
            if (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerScarabCard)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Graveyard))
                error = $"{candidate.SourceName}声明的增殖甲虫或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0206" && key.Item2 == "attack")
        {
            var targetId = activation.DeclaredValues.GetValueOrDefault("moveTarget", []).SingleOrDefault();
            var slot = activation.DeclaredValues.GetValueOrDefault("moveSlot", []).SingleOrDefault();
            if (FindOnField(player, targetId, out _, out _) is not { CardId: PublicTriggerTombGuardCard }
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "萨拉丁声明的陵墓守卫或位移位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0201" && key.Item2 is "attack" or "death")
        {
            var target = activation.DeclaredValues.GetValueOrDefault("killTarget", []).SingleOrDefault();
            if (DeclaredEnemyTarget(candidate.Controller, target, card => card.Troops <= 1000) is null)
            {
                activation.DeclaredValues.Remove("killTarget");
                activation.DeclaredValues["killMode"] = ["mode:none"];
                AddEvent("effect-cancelled", candidate.Controller,
                    "图特摩斯三世声明的随后击杀目标已失效；兵力降低段仍独立入栈");
            }
            else activation.DeclaredValues["killMode"] = ["mode:kill"];
        }
        else if (key.Item1 == "S01-0315" && mode == "mode:use" && player.Library.Count == 0)
            error = "无骨者伊瓦尔的牌库已空；效果未入栈";
        else if (key.Item1 == "S01-0407")
        {
            var targets = activation.DeclaredValues.GetValueOrDefault("moveTargets", []);
            var slots = new[] { "moveSlot1", "moveSlot2" }
                .SelectMany(name => activation.DeclaredValues.GetValueOrDefault(name, [])).ToArray();
            if (!ValidateRyomaMoveDeclaration(player, targets, slots))
                error = "坂本龙马声明的军团或位移位置已失效；效果未入栈";
        }

        if (error is null && key is ("S02-0006", "discard-trigger", _)
            && (mode != "mode:use" || !L12CardNameUsageRules.TryUse(player, candidate.SourceCardId)))
            error = "〈信仰狂热者〉的发动选择或卡名共享次数已失效；效果未入栈";

        if (error is null && mode == "mode:use" && batch6GAPlan == "anderstorp-draw")
        {
            var onceKey = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var pendingKey = candidate.Data.GetValueOrDefault("cleanupReservation") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(onceKey) || string.IsNullOrWhiteSpace(pendingKey)
                || player.UsedAbilities.Contains(onceKey) || !player.UsedAbilities.Contains(pendingKey))
                error = $"{candidate.SourceName}的回合次数保留已失效；效果未入栈";
            else
                player.UsedAbilities.Add(onceKey);
        }

        if (error is null && batch6JBPlan == "gustav-ready" && mode == "mode:use")
        {
            var onceKey = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            player.UsedAbilities.Add(onceKey);
        }

        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        if (batch6IBPlan == "gwen-choice")
        {
            candidate.Data["presentationFlow"] = "gwen-choice";
            RefreshDeclaredPresentationSceneId(candidate, declaredSource);
        }
        if (batch6IBPlan is not null)
        {
            var legacyTargets = batch6IBPlan switch
            {
                "grave-to-hand" or "tutankhamun-top" =>
                    activation.DeclaredValues.GetValueOrDefault("recoverTarget", []),
                "jingke-kill" or "harald-kill" => activation.DeclaredValues.GetValueOrDefault("killTarget", []),
                "uesugi-counters" => activation.DeclaredValues.GetValueOrDefault("entryCards", []),
                "grave-legion-summon" or "hand-legion-summon" =>
                    activation.DeclaredValues.GetValueOrDefault("entryCard", [])
                        .Concat(activation.DeclaredValues.GetValueOrDefault("entrySlot", [])).ToList(),
                _ => [],
            };
            candidate.Data["declaredTargets"] = string.Join('|', legacyTargets);
        }
        if (batch6IBPlan is "teach-draw-cycle" or "ragnar-draw-cycle" or "olaf-draw-cycle"
            or "thor-draw-cycle")
        {
            var composite = CompositeFirstSegmentData($"trigger:{candidate.SourceCardId}:death",
                activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
            RefreshDeclaredPresentationSceneId(candidate, declaredSource);
        }
        if (batch6GAPlan == "limu-enter")
        {
            var composite = CompositeFirstSegmentData("trigger:S02-0102:enter", activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        else if (batch6GAPlan == "margaret-master-damage")
        {
            var composite = CompositeFirstSegmentData("trigger:S02-0304:margaretMasterDamage",
                activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        if (batch6DPlan == "tomb-construct")
        {
            var guardIds = candidate.Data.GetValueOrDefault("tombGuardIds", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            candidate.Data["declaredCardIds"] = string.Join('|', guardIds);
            candidate.Data["declaredGuardOwners"] = candidate.Data.GetValueOrDefault("tombGuardOwners", string.Empty);
            candidate.Data["declaredTargets"] = string.Join('|', Enumerable.Range(0, guardIds.Length)
                .Select(index => activation.DeclaredValues[$"tombSlot{index}"].Single()));
        }
        if (fifthBatchPlan == "blood-eagle")
        {
            var composite = CompositeFirstSegmentData("trigger:S01-0320", activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        else if (fifthBatchPlan == "wisdom-reward")
        {
            var composite = CompositeFirstSegmentData("wisdom-reward:S01-0224", activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        if (key is ("S02-0523", "trojan-after-attack", _))
        {
            var composite = CompositeFirstSegmentData("trigger:S02-0523:trojan-after-attack",
                activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
            RefreshDeclaredPresentationSceneId(candidate, declaredSource);
        }
        else if (key.Item1 == "S01-0201" && key.Item2 is "attack" or "death")
        {
            var composite = CompositeFirstSegmentData($"trigger:S01-0201:{candidate.Trigger}",
                activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        else if (key is ("S01-0021", "reaction", _))
        {
            var composite = CompositeFirstSegmentData("trigger:S01-0021:reaction",
                activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
            RefreshDeclaredPresentationSceneId(candidate, declaredSource);
        }
        else if (key is ("S01-0223", "reaction", _))
        {
            var composite = CompositeFirstSegmentData("trigger:S01-0223:reaction",
                activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
            RefreshDeclaredPresentationSceneId(candidate, declaredSource);
        }
        candidate.Data["declaration-complete"] = "true";
        CleanupPublicTriggerReservation(candidate);
        AdvanceTriggerBatches();
        return true;
    }

    private void RemoveUnstackedTriggerCandidate(L12TriggerCandidate candidate, string reason)
    {
        CleanupPublicTriggerReservation(candidate);
        State.PendingTriggerStackCandidates.Remove(candidate);
        AddEvent("ability-rejected", candidate.Controller, reason);
        AdvanceTriggerBatches();
    }

    private void CleanupPublicTriggerReservation(L12TriggerCandidate candidate)
    {
        var reservation = candidate.Data.GetValueOrDefault("cleanupReservation");
        if (!string.IsNullOrWhiteSpace(reservation))
            State.Players[candidate.Controller].UsedAbilities.Remove(reservation);
        var queuedReservation = candidate.Data.GetValueOrDefault("cleanupQueuedReservation");
        if (!string.IsNullOrWhiteSpace(queuedReservation))
            State.Players[candidate.Controller].UsedAbilities.Remove(queuedReservation);
    }

    private bool ValidateDeclaredEntry(int controller, L12PendingActivation activation, string cardId,
        IEnumerable<L12CardInstance> zone)
    {
        var card = zone.FirstOrDefault(candidate => candidate.InstanceId == cardId);
        var battlefield = ParseEffectEntryBattlefieldChoice(
            activation.DeclaredValues.GetValueOrDefault("entryBattlefield", []).SingleOrDefault());
        var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
        return card is not null && battlefield is not null
            && EffectEntryBattlefieldChoices(controller, card).Contains(battlefield.Value)
            && slot is not null && EmptySlots(State.Players[battlefield.Value])
                .Contains(slot, StringComparer.OrdinalIgnoreCase);
    }

    private bool ValidateRyomaMoveDeclaration(L12PlayerState player, IReadOnlyList<string> targets,
        IReadOnlyList<string> slots)
    {
        if (targets.Count is < 1 or > 2 || targets.Count != slots.Count
            || targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count
            || slots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Count) return false;
        if (targets.Count == 2
            && FindOnField(player, targets[0], out var firstRow, out var firstSlot) is { Tapped: true }
            && FindOnField(player, targets[1], out var secondRow, out var secondSlot) is { Tapped: true }
            && slots[0] == $"{secondRow}:{secondSlot}" && slots[1] == $"{firstRow}:{firstSlot}")
            return true;
        var available = EmptySlots(player).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < targets.Count; index++)
        {
            if (FindOnField(player, targets[index], out var row, out var slot) is null
                || !available.Remove(slots[index])) return false;
            available.Add($"{row}:{slot}");
        }
        return true;
    }

    private static string PublicTriggerDeclared(L12StackItem item, string key)
        => item.Data.GetValueOrDefault($"declared:{key}", string.Empty);

    private void CreateDelayedPublicResolutionPrompt(L12StackItem item, string kind, string text,
        IEnumerable<string> choices, string action, Dictionary<string, string> data,
        bool isPrivate = false, int min = 1, int max = 1, int? chooser = null)
    {
        data["declarationTiming"] = "post-hidden-reveal";
        CreateResolutionChoicePrompt(item, kind, text, choices, action, data, isPrivate, min, max, chooser);
    }

    private void CreateResolutionChoicePrompt(L12StackItem item, string kind, string text,
        IEnumerable<string> choices, string action, Dictionary<string, string> data,
        bool isPrivate = false, int min = 1, int max = 1, int? chooser = null)
    {
        data["action"] = action;
        CreatePrompt(chooser ?? item.Controller, kind, text, choices, min, max, "card-effect", item.StackItemId,
            isPrivate: isPrivate, data: data);
    }
}
