namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static string? StarterTargetedPlan(string cardId, string trigger)
        => L12StructuredCardRules.StarterTargetedBatch2APlan(cardId, trigger);

    private static bool HasStarterTargetedTriggerDeclarationPlan(string cardId, string trigger)
        => StarterTargetedPlan(cardId, trigger) is not null;

    private bool TryBeginStarterTargetedTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        var plan = StarterTargetedPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return false;

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

    private bool TryCompleteStarterTargetedTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        var plan = StarterTargetedPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return false;

        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        if (mode != "mode:use")
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
            or "george-debuff" or "freydis-recover" or "khufu-counter-protection"))
            return false;

        var player = State.Players[item.Controller];
        string? One(string key) => item.Data.GetValueOrDefault($"declared:{key}")
            ?.Split('|', StringSplitOptions.RemoveEmptyEntries).SingleOrDefault();

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
        }

        FinishStackItem(item);
        return true;
    }
}
