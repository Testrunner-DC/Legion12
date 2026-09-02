namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static string? StarterTargetedPlan(string cardId, string trigger)
        => L12StructuredCardRules.StarterTargetedBatch2APlan(cardId, trigger)
            ?? L12StructuredCardRules.StarterTargetedBatch2BPlan(cardId, trigger)
            ?? L12StructuredCardRules.StarterRemainingPlan(cardId, trigger);

    private static bool HasStarterTargetedTriggerDeclarationPlan(string cardId, string trigger)
        => StarterTargetedPlan(cardId, trigger) is not null;

    private bool TryBeginStarterTargetedTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        var plan = StarterTargetedPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return false;

        if (L12StructuredCardRules.IsStarterRemainingPlan(plan))
            return TryBeginStarterRemainingTriggerDeclaration(candidate, source, plan);

        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var slots = EmptySlots(player).ToList();
        var enemy = PublicLegions(opponent).Select(card => card.InstanceId).ToList();
        List<L12ActivationSelectionStep> steps;

        List<string> Modes(bool canUse) => canUse ? ["mode:none", "mode:use"] : ["mode:none"];

        switch (plan)
        {
            case "xiaohe-summon":
            {
                var hanXin = player.Hand.Where(card => card.CardId == L12StructuredCardRules.StarterHanXinCardId)
                    .Select(card => card.InstanceId).ToList();
                var canUse = player.Morale.Count > 0 && hanXin.Count > 0 && slots.Count > 0;
                steps =
                [
                    StarterStep("option", "mode", "萧何：是否返还1张士气，使手牌中的〈韩信〉活跃登场？",
                        Modes(canUse)),
                    StarterStep("target-morale", "returnCost", "萧何：选择要返还的1张士气",
                        player.Morale.Select(card => card.InstanceId), requiredChoice: "mode:use"),
                    StarterStep("hand-card", "entryCard", "萧何：选择手牌中的1张〈韩信〉",
                        hanXin, requiredChoice: "mode:use"),
                    StarterStep("unused-slot", "entrySlot", "萧何：选择〈韩信〉活跃登场的位置",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case "khufu-debuff":
            {
                var guards = PublicLegions(player).Where(card => card.CardId == L12StructuredCardRules.StarterTombGuardCardId)
                    .Select(card => card.InstanceId).ToList();
                var canUse = guards.Count > 0 && enemy.Count > 0;
                steps =
                [
                    StarterStep("option", "mode", "胡夫：是否弃置我方1张〈陵墓守卫〉，使对方1张军团本回合兵力-4000？",
                        Modes(canUse)),
                    StarterStep("field-legion", "discardCost", "胡夫：选择要弃置的1张〈陵墓守卫〉",
                        guards, requiredChoice: "mode:use"),
                    StarterStep("field-legion", "enemyTarget", "胡夫：选择本回合兵力-4000的对方军团",
                        enemy, requiredChoice: "mode:use"),
                ];
                break;
            }
            case "snake-charmer-summon":
            {
                var cobras = player.Hand.Concat(player.Library)
                    .Where(card => card.CardId == L12StructuredCardRules.StarterDesertCobraCardId)
                    .Select(card => card.InstanceId).ToList();
                var canUse = cobras.Count > 0 && slots.Count > 0;
                steps =
                [
                    StarterStep("option", "mode", "白沙瓦舞蛇人：是否使手牌或牌库中的〈沙漠眼镜蛇〉活跃登场？",
                        Modes(canUse)),
                    StarterStep("controller-private-card", "entryCard", "白沙瓦舞蛇人：选择1张〈沙漠眼镜蛇〉",
                        cobras, requiredChoice: "mode:use"),
                    StarterStep("unused-slot", "entrySlot", "白沙瓦舞蛇人：选择〈沙漠眼镜蛇〉活跃登场的位置",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case "george-debuff":
            {
                var costs = PublicLegions(player).Select(card => card.InstanceId).ToList();
                var canUse = costs.Count > 0 && enemy.Count > 0;
                steps =
                [
                    StarterStep("option", "mode", "乔泽：是否弃置我方战场上1张军团，使对方1张军团本回合兵力-2000？",
                        Modes(canUse)),
                    StarterStep("field-legion", "discardCost", "乔泽：选择要弃置的我方军团",
                        costs, requiredChoice: "mode:use"),
                    StarterStep("field-legion", "enemyTarget", "乔泽：选择本回合兵力-2000的对方军团",
                        enemy, requiredChoice: "mode:use"),
                ];
                break;
            }
            case "freydis-recover":
            {
                var costs = player.Hand.Select(card => card.InstanceId).ToList();
                var recover = player.Graveyard.Where(card => card.CardType == "legion"
                        && L12StructuredCardRules.HasFaction(player, card, "asgard") && CanEnterHandOrLibrary(card))
                    .Select(card => card.InstanceId).ToList();
                var canUse = costs.Count > 0 && recover.Count > 0;
                steps =
                [
                    StarterStep("option", "mode", "弗蕾迪斯：是否弃置1张手牌，将墓地1张【阿斯加德】军团加入手牌？",
                        Modes(canUse)),
                    StarterStep("hand-card", "discardCost", "弗蕾迪斯：选择要弃置的1张手牌",
                        costs, requiredChoice: "mode:use"),
                    StarterStep("grave-card", "recoverTarget", "弗蕾迪斯：选择要加入手牌的【阿斯加德】军团",
                        recover, requiredChoice: "mode:use"),
                ];
                break;
            }
            case "antinous-ready":
            {
                if (!player.HandDiscardedByMasterThisTurn)
                {
                    RemoveUnstackedTriggerCandidate(candidate,
                        "〈安提诺乌斯〉登场前，本回合尚未因主宰弃置过手牌");
                    return true;
                }
                candidate.Data["starterConditionLocked"] = "true";
                var restedOlympus = PublicLegions(player)
                    .Where(card => card.Tapped && L12StructuredCardRules.HasFaction(player, card, "olympus"))
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    StarterStep("option", "mode", "安提诺乌斯：是否将我方1张休整的【奥林匹斯】军团转为活跃？",
                        Modes(restedOlympus.Count > 0)),
                    StarterStep("field-legion", "readyTarget", "安提诺乌斯：选择要转为活跃的【奥林匹斯】军团",
                        restedOlympus, requiredChoice: "mode:use"),
                ];
                break;
            }
            case "elizabeth-lock-morale":
            {
                var restedMorale = opponent.Morale.Where(card => card.Tapped)
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    StarterSelectionStep("target-morale", "moraleTargets",
                        "伊丽莎白一世：选择对方最多2张休整的士气，使其下个重置阶段无法转为活跃",
                        restedMorale, 0, 2, targetPlayerIndex: opponent.PlayerIndex,
                        autoSelectWhenExact: restedMorale.Count == 0),
                ];
                break;
            }
            case "mordred-enter-choice":
                steps =
                [
                    StarterSelectionStep("option", "mode", "莫德雷德：选择获得1符文或获得冲锋",
                        ["mode:rune", "mode:charge"], 1, 1, labels: new()
                        {
                            ["mode:rune"] = "获得1符文",
                            ["mode:charge"] = "获得冲锋",
                        }),
                ];
                break;
            case "mordred-death-kill":
            {
                var killTargets = PublicLegions(opponent).Where(card => card.Troops <= 2000)
                    .Select(card => card.InstanceId).ToList();
                var required = killTargets.Count == 0 ? 0 : 1;
                steps =
                [
                    StarterSelectionStep("field-legion", "enemyTarget",
                        "莫德雷德：选择对方1张兵力不高于2000的军团击杀",
                        killTargets, required, 1, targetPlayerIndex: opponent.PlayerIndex,
                        autoSelectWhenExact: true),
                ];
                break;
            }
            case "boudica-immortal":
            {
                var otherworld = PublicLegions(player)
                    .Where(card => L12StructuredCardRules.HasFaction(player, card, "otherworld"))
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    StarterSelectionStep("field-legion", "immortalTarget",
                        "布狄卡：选择我方1张【彼界】军团，直到下个我方回合开始前获得一次免死",
                        otherworld, 1, 1, targetPlayerIndex: player.PlayerIndex),
                ];
                break;
            }
            default:
                return false;
        }

        var result = BeginPendingActivationSequence(candidate.Controller, source,
            "public-trigger-declaration", steps, candidate.CandidateId);
        if (!result.Accepted)
            RemoveUnstackedTriggerCandidate(candidate, result.Error ?? $"〈{source.Name}〉当前无法发动");
        return true;
    }

    private static L12ActivationSelectionStep StarterStep(string kind, string key, string text,
        IEnumerable<string> choices, string? requiredChoice = null) => new()
    {
        Kind = kind,
        DeclarationKey = key,
        Text = text,
        ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        MinChoose = 1,
        MaxChoose = 1,
        CancellationPolicy = L12ActivationCancellationPolicy.WhenNoExplicitDecline,
        RequiredDeclaredChoice = requiredChoice,
        ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode:none"] = "不发动",
            ["mode:use"] = "发动",
        },
    };

    private static L12ActivationSelectionStep StarterSelectionStep(string kind, string key, string text,
        IEnumerable<string> choices, int min, int max, int? targetPlayerIndex = null,
        bool autoSelectWhenExact = false, Dictionary<string, string>? labels = null) => new()
    {
        Kind = kind,
        DeclarationKey = key,
        Text = text,
        ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        MinChoose = min,
        MaxChoose = max,
        TargetPlayerIndex = targetPlayerIndex,
        AutoSelectWhenExact = autoSelectWhenExact,
        CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
        ChoiceLabels = labels ?? [],
    };

    private bool TryCompleteStarterTargetedTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        var plan = StarterTargetedPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return false;

        if (L12StructuredCardRules.IsStarterRemainingPlan(plan))
            return TryCompleteStarterRemainingTriggerDeclaration(candidate, activation, plan);

        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        var isOptionalActivation = plan is "xiaohe-summon" or "khufu-debuff" or "snake-charmer-summon"
            or "george-debuff" or "freydis-recover" or "antinous-ready";
        if (isOptionalActivation && mode != "mode:use")
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                $"〈{candidate.SourceName}〉的可选登场效果未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        var player = State.Players[candidate.Controller];
        var source = FindAuthoritativeCard(candidate.SourceInstanceId) ?? candidate.SourceSnapshot
            ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        string? error = null;
        L12CardInstance? fieldCost = null;
        L12CardInstance? handCost = null;
        var returnCost = activation.DeclaredValues.GetValueOrDefault("returnCost", []);

        switch (plan)
        {
            case "xiaohe-summon":
            {
                var entry = activation.DeclaredValues.GetValueOrDefault("entryCard", []).SingleOrDefault();
                var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
                if (returnCost.Count != 1 || !CanReturnSelectedMoraleById(player, returnCost, 1)
                    || !player.Hand.Any(card => card.InstanceId == entry
                        && card.CardId == L12StructuredCardRules.StarterHanXinCardId)
                    || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                    error = "萧何选择的士气、〈韩信〉或登场位置已失效；未返还士气且效果未入栈";
                break;
            }
            case "khufu-debuff":
                fieldCost = FindOnField(player,
                    activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault(), out _, out _);
                if (fieldCost?.CardId != L12StructuredCardRules.StarterTombGuardCardId
                    || DeclaredEnemyTarget(candidate.Controller,
                        activation.DeclaredValues.GetValueOrDefault("enemyTarget", []).SingleOrDefault()) is null)
                    error = "胡夫选择的〈陵墓守卫〉或对方军团已失效；未弃置军团且效果未入栈";
                break;
            case "snake-charmer-summon":
            {
                var entry = activation.DeclaredValues.GetValueOrDefault("entryCard", []).SingleOrDefault();
                var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
                if (!player.Hand.Concat(player.Library).Any(card => card.InstanceId == entry
                        && card.CardId == L12StructuredCardRules.StarterDesertCobraCardId)
                    || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                    error = "白沙瓦舞蛇人选择的〈沙漠眼镜蛇〉或登场位置已失效；效果未入栈";
                break;
            }
            case "george-debuff":
                fieldCost = FindOnField(player,
                    activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault(), out _, out _);
                if (fieldCost is null || !IsFieldLegion(fieldCost) || DeclaredEnemyTarget(candidate.Controller,
                        activation.DeclaredValues.GetValueOrDefault("enemyTarget", []).SingleOrDefault()) is null)
                    error = "乔泽选择的我方军团或对方军团已失效；未弃置军团且效果未入栈";
                break;
            case "freydis-recover":
                handCost = player.Hand.FirstOrDefault(card => card.InstanceId ==
                    activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault());
                var recover = activation.DeclaredValues.GetValueOrDefault("recoverTarget", []).SingleOrDefault();
                if (handCost is null || !player.Graveyard.Any(card => card.InstanceId == recover
                        && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "asgard")
                        && CanEnterHandOrLibrary(card)))
                    error = "弗蕾迪斯选择的弃牌或墓地军团已失效；未弃置手牌且效果未入栈";
                break;
            case "antinous-ready":
                if (candidate.Data.GetValueOrDefault("starterConditionLocked") != "true"
                    || FindOnField(player,
                        activation.DeclaredValues.GetValueOrDefault("readyTarget", []).SingleOrDefault(), out _, out _)
                        is not { Tapped: true } readyTarget
                    || !L12StructuredCardRules.HasFaction(player, readyTarget, "olympus"))
                    error = "安提诺乌斯选择的休整【奥林匹斯】军团已失效；效果未入栈";
                break;
            case "elizabeth-lock-morale":
            {
                var moraleTargets = activation.DeclaredValues.GetValueOrDefault("moraleTargets", []);
                if (moraleTargets.Count > 2 || moraleTargets.Count != moraleTargets.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    || moraleTargets.Any(id => !State.Players[1 - candidate.Controller].Morale
                        .Any(card => card.InstanceId == id && card.Tapped)))
                    error = "伊丽莎白一世选择的休整士气已失效；效果未入栈";
                break;
            }
            case "mordred-enter-choice":
                if (mode is not ("mode:rune" or "mode:charge"))
                    error = "莫德雷德必须选择获得1符文或获得冲锋";
                break;
            case "mordred-death-kill":
            {
                var targetId = activation.DeclaredValues.GetValueOrDefault("enemyTarget", []).SingleOrDefault();
                if (targetId is not null && DeclaredEnemyTarget(candidate.Controller, targetId,
                        card => card.Troops <= 2000) is null)
                    error = "莫德雷德选择的对方军团已失效；效果未入栈";
                break;
            }
            case "boudica-immortal":
            {
                var target = FindOnField(player,
                    activation.DeclaredValues.GetValueOrDefault("immortalTarget", []).SingleOrDefault(), out _, out _);
                if (target is null || !L12StructuredCardRules.HasFaction(player, target, "otherworld"))
                    error = "布狄卡选择的【彼界】军团已失效；效果未入栈";
                break;
            }
        }

        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        candidate.Data["declaration-committing"] = "true";
        if (returnCost.Count == 1)
        {
            _ = ReturnSelectedMoraleById(player, returnCost, 1);
            AddEvent("cost", candidate.Controller, "萧何返还1张士气", source);
        }
        if (fieldCost is not null)
        {
            _ = RemoveFromField(player, fieldCost, true, $"作为〈{source.Name}〉效果的费用被弃置",
                leaveKind: L12FieldLeaveKind.Discard);
            AddEvent("cost", candidate.Controller, $"〈{source.Name}〉弃置我方军团作为费用", source, fieldCost);
        }
        if (handCost is not null)
        {
            MoveHandToGrave(player, handCost.InstanceId, causedByEffect: false);
            AddEvent("cost", candidate.Controller, "弗蕾迪斯弃置1张手牌", source, handCost);
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        candidate.Data.Remove("declaration-committing");
        candidate.Data["declaration-complete"] = "true";
        CleanupPublicTriggerReservation(candidate);
        AdvanceTriggerBatches();
        return true;
    }

    private bool TryResolveStarterTargetedEffect(L12StackItem item)
    {
        var flow = item.Data.GetValueOrDefault("atomicFlow");
        if (flow is not ("xiaohe-summon" or "khufu-debuff" or "snake-charmer-summon"
            or "george-debuff" or "freydis-recover" or "khufu-counter-protection"
            or "antinous-ready" or "elizabeth-derived-cost" or "elizabeth-lock-morale"
            or "mordred-enter-choice" or "mordred-death-kill" or "boudica-immortal"))
            return false;

        var player = State.Players[item.Controller];
        string? One(string key) => item.Data.GetValueOrDefault($"declared:{key}")
            ?.Split('|', StringSplitOptions.RemoveEmptyEntries).SingleOrDefault();
        string[] Many(string key) => item.Data.GetValueOrDefault($"declared:{key}")
            ?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? [];

        switch (flow)
        {
            case "xiaohe-summon":
            case "snake-charmer-summon":
                _ = TrySummonFromAnyPrivateZone(player, item.Controller, One("entryCard") ?? string.Empty,
                    One("entrySlot") ?? string.Empty, tapped: false);
                break;
            case "khufu-debuff":
            case "george-debuff":
                if (DeclaredEnemyTarget(item.Controller, One("enemyTarget")) is { } enemy)
                {
                    var delta = flow == "khufu-debuff" ? -4000 : -2000;
                    AddTimedModifier(enemy, delta, 0, State.TurnSerial, item.SourceName);
                    AddEvent("effect", item.Controller, $"〈{enemy.Name}〉本回合兵力{delta}", enemy);
                }
                else AddEvent("effect-cancelled", item.Controller,
                    $"〈{item.SourceName}〉选择的对方军团已离场，本次兵力变化未生效");
                break;
            case "freydis-recover":
            {
                var recover = player.Graveyard.FirstOrDefault(card => card.InstanceId == One("recoverTarget")
                    && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "asgard")
                    && CanEnterHandOrLibrary(card));
                if (recover is not null)
                {
                    player.Graveyard.Remove(recover);
                    AddEvent("reveal", item.Controller,
                        $"弗蕾迪斯展示〈{recover.Name}〉并将其加入手牌", recover);
                    AddCardToHandByEffect(player, recover, "graveyard",
                        $"弗蕾迪斯展示〈{recover.Name}〉并将其加入手牌");
                }
                else AddEvent("effect-cancelled", item.Controller,
                    "弗蕾迪斯选择的墓地军团已离开墓地，本次回收未生效");
                break;
            }
            case "antinous-ready":
            {
                var target = FindOnField(player, One("readyTarget"), out _, out _);
                var source = FindOnField(player, item.SourceInstanceId, out _, out _)
                    ?? item.SourceSnapshot ?? CreateCard(item.SourceCardId, item.SourceInstanceId);
                if (target is { Tapped: true } && L12StructuredCardRules.HasFaction(player, target, "olympus"))
                    ReadyCardByEffect(item.Controller, source, target, $"{target.Name}因安提诺乌斯效果转为活跃");
                else AddEvent("effect-cancelled", item.Controller,
                    "安提诺乌斯选择的军团已不再是休整的【奥林匹斯】军团，本次转为活跃未生效");
                break;
            }
            case "elizabeth-derived-cost":
                break;
            case "elizabeth-lock-morale":
            {
                var opponent = State.Players[1 - item.Controller];
                foreach (var targetId in Many("moraleTargets"))
                {
                    var morale = opponent.Morale.FirstOrDefault(card => card.InstanceId == targetId && card.Tapped);
                    if (morale is null)
                    {
                        AddEvent("effect-cancelled", item.Controller,
                            "伊丽莎白一世选择的士气已不再休整，本次限制未生效");
                        continue;
                    }
                    morale.CannotUntapUntilRound = Math.Max(morale.CannotUntapUntilRound, State.Round + 1);
                    AddEvent("effect", item.Controller,
                        "所选士气下个重置阶段无法转为活跃");
                }
                break;
            }
            case "mordred-enter-choice":
            {
                if (One("mode") == "mode:rune")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    AddEvent("runes", item.Controller, "莫德雷德使我方获得1符文");
                }
                else if (One("mode") == "mode:charge"
                    && FindOnField(player, item.SourceInstanceId, out _, out _) is { } mordred)
                {
                    mordred.HasCharge = true;
                    AddEvent("effect", item.Controller, "莫德雷德获得冲锋", mordred);
                }
                break;
            }
            case "mordred-death-kill":
            {
                var targetId = One("enemyTarget");
                if (targetId is not null && DeclaredEnemyTarget(item.Controller, targetId,
                        card => card.Troops <= 2000) is not null)
                    KillTarget(item, targetId, "被莫德雷德阵亡时效果击杀");
                else if (targetId is not null)
                    AddEvent("effect-cancelled", item.Controller,
                        "莫德雷德选择的军团兵力已高于2000或已离场，本次击杀未生效");
                break;
            }
            case "boudica-immortal":
            {
                var target = FindOnField(player, One("immortalTarget"), out _, out _);
                if (target is not null && L12StructuredCardRules.HasFaction(player, target, "otherworld"))
                {
                    GrantImmortalUntilNextTurnStart(target, item.Controller);
                    AddEvent("effect", item.Controller,
                        $"〈{target.Name}〉直到下个我方回合开始前获得一次免死", target);
                }
                else AddEvent("effect-cancelled", item.Controller,
                    "布狄卡选择的【彼界】军团已离场，本次免死未生效");
                break;
            }
        }

        FinishStackItem(item);
        return true;
    }
}
