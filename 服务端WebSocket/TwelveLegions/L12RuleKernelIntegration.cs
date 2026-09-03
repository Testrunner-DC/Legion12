namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private CommandResult BeginPendingActivation(int playerIndex, L12CardInstance source, string ability,
        IEnumerable<string> choices, string text, int min = 1, int max = 1)
        => BeginPendingActivationSequence(playerIndex, source, ability,
        [new L12ActivationSelectionStep { Kind = "active-target", Text = text, ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), MinChoose = min, MaxChoose = max }]);

    private CommandResult BeginPendingActivationSequence(int playerIndex, L12CardInstance source, string ability,
        IEnumerable<L12ActivationSelectionStep> selectionSteps)
        => BeginPendingActivationSequence(playerIndex, source, ability, selectionSteps, null);

    private CommandResult BeginPendingActivationSequence(int playerIndex, L12CardInstance source, string ability,
        IEnumerable<L12ActivationSelectionStep> selectionSteps, string? triggerCandidateId)
        => BeginPendingActivationSequence(playerIndex, source, ability, selectionSteps, triggerCandidateId, null, null);

    private CommandResult BeginPendingHandPlay(int playerIndex, L12CardInstance source,
        IEnumerable<string> choices, string text)
        => BeginPendingActivationSequence(playerIndex, source, "play-card",
        [new L12ActivationSelectionStep { Kind = "active-target", Text = text, ValidChoices = choices.ToList() }],
        null, source.InstanceId, null);

    private CommandResult BeginPendingResponseActivation(int playerIndex, L12CardInstance source,
        string targetStackItemId, IEnumerable<string> choices, string text)
        => BeginPendingActivationSequence(playerIndex, source, "response",
        [new L12ActivationSelectionStep { Kind = "active-target", Text = text, ValidChoices = choices.ToList() }],
        null, null, targetStackItemId);

    private CommandResult BeginPendingActivationSequence(int playerIndex, L12CardInstance source, string ability,
        IEnumerable<L12ActivationSelectionStep> selectionSteps, string? triggerCandidateId, string? playCardInstanceId,
        string? responseTargetStackItemId)
    {
        var steps = selectionSteps.Select(step => new L12ActivationSelectionStep
        {
            Kind = step.Kind,
            Text = step.Text,
            ValidChoices = step.ValidChoices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = step.MinChoose,
            MaxChoose = Math.Min(step.MaxChoose, step.ValidChoices.Count),
            CancellationPolicy = step.CancellationPolicy,
            AutoSelectWhenExact = step.AutoSelectWhenExact,
            ChoiceLabels = new Dictionary<string, string>(step.ChoiceLabels, StringComparer.OrdinalIgnoreCase),
            SkipWhenPreviousStepEmpty = step.SkipWhenPreviousStepEmpty,
            RequiredDeclaredChoice = step.RequiredDeclaredChoice,
            DeclarationKey = step.DeclarationKey,
            ReferenceDeclarationKey = step.ReferenceDeclarationKey,
            PreviewPresentation = step.PreviewPresentation,
            MinimumReferenceCount = step.MinimumReferenceCount,
            MinimumReferenceNumericValue = step.MinimumReferenceNumericValue,
            ReferenceNumericChoicePrefix = step.ReferenceNumericChoicePrefix,
            ReferenceChoiceIndex = step.ReferenceChoiceIndex,
            SkipWhenReferenceIsNone = step.SkipWhenReferenceIsNone,
            TargetPlayerIndex = step.TargetPlayerIndex,
            CostThreshold = step.CostThreshold,
            SelectionConstraint = step.SelectionConstraint,
        }).ToList();
        if (steps.Count == 0 || steps.Any(step => step.ValidChoices.Count < step.MinChoose
                && step.RequiredDeclaredChoice is null))
            return CommandResult.Reject("没有足够的合法目标");
        var first = steps[0];
        var activation = new L12PendingActivation
        {
            ActivationId = $"activation-{++State.ActivationSequence}",
            Controller = playerIndex,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            Ability = ability,
            Text = first.Text,
            ValidChoices = first.ValidChoices,
            MinChoose = first.MinChoose,
            MaxChoose = first.MaxChoose,
            SelectionSteps = steps,
            TriggerCandidateId = triggerCandidateId,
            PlayCardInstanceId = playCardInstanceId,
            ResponseTargetStackItemId = responseTargetStackItemId,
        };
        State.PendingActivations.Add(activation);
        CreateActivationStepPrompt(activation);
        if (responseTargetStackItemId is null)
            AddEvent("activation-declare", playerIndex, $"{State.Players[playerIndex].Name} 正在声明〈{source.Name}〉的目标", source);
        else
            AddEvent("activation-declare", playerIndex, $"{State.Players[playerIndex].Name} 正在声明反击战术目标");
        return CommandResult.Ok();
    }

    private void CreateActivationStepPrompt(L12PendingActivation activation)
    {
        while (activation.CurrentStep < activation.SelectionSteps.Count)
        {
            var pendingStep = activation.SelectionSteps[activation.CurrentStep];
            if (pendingStep.RequiredDeclaredChoice is { } requiredChoice
                && !activation.DeclaredTargets.Contains(requiredChoice, StringComparer.OrdinalIgnoreCase))
            {
                activation.CurrentStep++;
                continue;
            }
            if (pendingStep.SkipWhenReferenceIsNone
                && pendingStep.ReferenceDeclarationKey is { } referenceKey
                && activation.DeclaredValues.GetValueOrDefault(referenceKey, [])
                    .SingleOrDefault()?.Equals("mode:none", StringComparison.OrdinalIgnoreCase) == true)
            {
                activation.CurrentStep++;
                continue;
            }
            if (pendingStep.MinimumReferenceCount > 0
                && pendingStep.ReferenceDeclarationKey is { } countedReference
                && activation.DeclaredValues.GetValueOrDefault(countedReference, []).Count < pendingStep.MinimumReferenceCount)
            {
                activation.CurrentStep++;
                continue;
            }
            if (pendingStep.MinimumReferenceNumericValue > 0
                && pendingStep.ReferenceDeclarationKey is { } numericReference
                && !DeclaredNumericValueAtLeast(activation, numericReference,
                    pendingStep.ReferenceNumericChoicePrefix, pendingStep.MinimumReferenceNumericValue))
            {
                activation.CurrentStep++;
                continue;
            }
            // 可选后续段在条件、目标或支付能力不足时，构造器只会留下“不发动”。
            // 该结果没有玩家决策空间：自动记录拒绝并继续后续强制段，避免无意义弹框。
            if (IsOnlyNegativeOptionalChoice(pendingStep))
            {
                activation.DeclaredTargets.Add(pendingStep.ValidChoices[0]);
                if (!string.IsNullOrWhiteSpace(pendingStep.DeclarationKey))
                    activation.DeclaredValues[pendingStep.DeclarationKey] = [pendingStep.ValidChoices[0]];
                activation.CurrentStep++;
                continue;
            }
            if ((pendingStep.AutoSelectWhenExact || IsDeterministicCostSelection(pendingStep))
                && pendingStep.MinChoose == pendingStep.MaxChoose
                && pendingStep.ValidChoices.Count == pendingStep.MinChoose)
            {
                activation.DeclaredTargets.AddRange(pendingStep.ValidChoices);
                if (!string.IsNullOrWhiteSpace(pendingStep.DeclarationKey))
                    activation.DeclaredValues[pendingStep.DeclarationKey] = pendingStep.ValidChoices.ToList();
                activation.CurrentStep++;
                continue;
            }
            break;
        }
        if (activation.CurrentStep >= activation.SelectionSteps.Count)
        {
            CompleteResolvedPendingActivation(activation);
            return;
        }
        var step = activation.SelectionSteps[activation.CurrentStep];
        var promptKind = step.Kind;
        int? targetPlayerIndex = null;
        if (step.Kind == "adjacent-slot")
        {
            var player = State.Players[activation.Controller];
            var row = -1;
            var slot = -1;
            var movingId = step.ReferenceDeclarationKey is { } referenceKey
                ? activation.DeclaredValues.GetValueOrDefault(referenceKey, []).SingleOrDefault()
                : activation.DeclaredTargets.FirstOrDefault();
            var moving = movingId is null ? null : FindOnField(player, movingId, out row, out slot);
            List<string> choices = moving is null ? [] : AdjacentEmptySlots(player, row, slot).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "所选军团没有可位移的相邻空位，效果未支付费用也未入栈");
                return;
            }
            promptKind = "slot";
        }
        else if (step.Kind == "cavalry-slot")
        {
            if (activation.DeclaredTargets.Count > 0
                && FindOnField(State.Players[activation.Controller], activation.DeclaredTargets[0], out _, out _) is not null)
                targetPlayerIndex = activation.Controller;
            var choices = targetPlayerIndex is null ? [] : EffectCavalryDestinations(State.Players[targetPlayerIndex.Value]).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "所选军团所在战场没有可进行骑兵位移的空位，效果未支付费用也未入栈");
                return;
            }
            promptKind = "slot";
        }
        else if (step.Kind == "unused-slot")
        {
            var player = State.Players[activation.Controller];
            var choices = EmptySlots(player)
                .Except(activation.DeclaredTargets, StringComparer.OrdinalIgnoreCase)
                .ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "可用战场位置不足，效果未支付费用也未入栈");
                return;
            }
            promptKind = "slot";
        }
        else if (step.Kind == "owner-unused-slot")
        {
            targetPlayerIndex = step.TargetPlayerIndex ?? activation.Controller;
            var used = activation.SelectionSteps
                .Where(candidate => candidate.Kind == "owner-unused-slot"
                    && candidate.TargetPlayerIndex == targetPlayerIndex
                    && !string.IsNullOrWhiteSpace(candidate.DeclarationKey))
                .SelectMany(candidate => activation.DeclaredValues.GetValueOrDefault(candidate.DeclarationKey!, []))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var choices = EmptySlots(State.Players[targetPlayerIndex.Value])
                .Where(choice => !used.Contains(choice)).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "所有者战场没有足够的合法位置，效果未入栈");
                return;
            }
            promptKind = "slot";
        }
        else if (step.Kind == "composite-defense-slot")
        {
            var player = State.Players[activation.Controller];
            var choices = Enumerable.Range(0, 3).Where(slot => player.Field[1][slot] is null)
                .Select(slot => $"1:{slot}")
                .Except(activation.DeclaredValues.Values.SelectMany(values => values), StringComparer.OrdinalIgnoreCase)
                .ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (choices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "防御部署没有足够的合法后排位置；效果未入栈");
                return;
            }
            promptKind = "slot";
            targetPlayerIndex = activation.Controller;
        }
        else if (step.Kind == "composite-opposite-slot")
        {
            var opponent = State.Players[1 - activation.Controller];
            var movingIds = step.ReferenceDeclarationKey is { } referenceKey
                ? activation.DeclaredValues.GetValueOrDefault(referenceKey, []) : [];
            var movingId = movingIds.ElementAtOrDefault(step.ReferenceChoiceIndex);
            var row = -1;
            var slot = -1;
            var moving = movingId is null ? null : FindOnField(opponent, movingId, out row, out slot);
            var destination = moving is null || opponent.Field[1 - row][slot] is not null
                || State.ActiveDisaster?.CardId == "S01-DS03" && 1 - row == 1
                ? null : $"{1 - row}:{slot}";
            step.ValidChoices.Clear();
            if (destination is not null) step.ValidChoices.Add(destination);
            if (destination is null)
            {
                RejectPendingActivation(activation, "伪造密令的公开位移位置已失效；效果未入栈");
                return;
            }
            activation.DeclaredTargets.Add(destination);
            if (!string.IsNullOrWhiteSpace(step.DeclarationKey))
                activation.DeclaredValues[step.DeclarationKey] = [destination];
            activation.CurrentStep++;
            CreateActivationStepPrompt(activation);
            return;
        }
        else if (step.Kind == "public-move-slot")
        {
            var player = State.Players[activation.Controller];
            var movingIds = step.ReferenceDeclarationKey is { } referenceKey
                ? activation.DeclaredValues.GetValueOrDefault(referenceKey, []) : [];
            var choices = EmptySlots(player).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (step.ReferenceChoiceIndex == 0 && movingIds.Count == 2
                && FindOnField(player, movingIds[0], out _, out _) is { Tapped: true }
                && FindOnField(player, movingIds[1], out var swapRow, out var swapSlot) is { Tapped: true })
                choices.Add($"{swapRow}:{swapSlot}");
            if (step.ReferenceChoiceIndex == 1 && movingIds.Count == 2
                && FindOnField(player, movingIds[0], out var firstRow, out var firstSlot) is { Tapped: true }
                && FindOnField(player, movingIds[1], out var secondRow, out var secondSlot) is { Tapped: true }
                && activation.DeclaredValues.GetValueOrDefault("moveSlot1", []).SingleOrDefault() == $"{secondRow}:{secondSlot}")
            {
                choices.Clear();
                choices.Add($"{firstRow}:{firstSlot}");
            }
            for (var index = 0; index < step.ReferenceChoiceIndex && index < movingIds.Count; index++)
            {
                if (FindOnField(player, movingIds[index], out var originRow, out var originSlot) is not null)
                    choices.Add($"{originRow}:{originSlot}");
                var priorDestination = activation.DeclaredValues.GetValueOrDefault($"moveSlot{index + 1}", [])
                    .SingleOrDefault();
                if (priorDestination is not null
                    && !(step.ReferenceChoiceIndex == 1 && choices.Count == 1)) choices.Remove(priorDestination);
            }
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "公开位移没有足够的合法位置；效果未入栈");
                return;
            }
            promptKind = "slot";
        }
        else if (step.Kind == "effect-entry-battlefield")
        {
            var reference = step.ReferenceDeclarationKey is { } referenceKey
                ? activation.DeclaredValues.GetValueOrDefault(referenceKey, []).SingleOrDefault()
                : activation.DeclaredTargets.FirstOrDefault();
            var card = reference is null ? null : FindPromptCard(activation.Controller, reference);
            var choices = card is null
                ? []
                : EffectEntryBattlefieldChoices(activation.Controller, card)
                    .Select(EffectEntryBattlefieldChoice).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            foreach (var choice in choices)
            {
                var battlefield = ParseEffectEntryBattlefieldChoice(choice);
                if (battlefield is not null)
                    step.ChoiceLabels[choice] = $"{State.Players[battlefield.Value].Name}的战场";
            }
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "所选军团当前没有可合法登场的战场，效果未支付费用也未入栈");
                return;
            }
            if (choices.Count == 1)
            {
                activation.DeclaredTargets.Add(choices[0]);
                if (!string.IsNullOrWhiteSpace(step.DeclarationKey))
                    activation.DeclaredValues[step.DeclarationKey] = [choices[0]];
                activation.CurrentStep++;
                CreateActivationStepPrompt(activation);
                return;
            }
            promptKind = "option";
        }
        else if (step.Kind == "effect-entry-slot")
        {
            var battlefieldChoice = activation.DeclaredTargets.LastOrDefault(choice =>
                choice.StartsWith("battlefield:", StringComparison.OrdinalIgnoreCase));
            targetPlayerIndex = ParseEffectEntryBattlefieldChoice(battlefieldChoice);
            var choices = targetPlayerIndex is null
                ? []
                : EmptySlots(State.Players[targetPlayerIndex.Value]).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "所选战场当前没有可合法登场的位置，效果未支付费用也未入栈");
                return;
            }
            promptKind = "slot";
        }
        else if (step.Kind == "declared-card")
        {
            var choices = activation.DeclaredTargets
                .Where(id => !id.StartsWith("mode:", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "此前声明的卡牌已失效，效果未支付费用也未入栈");
                return;
            }
            promptKind = "card";
        }
        else if (step.Kind == "enemy-unselected-required")
        {
            var choices = step.ValidChoices
                .Where(id => !id.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)
                    && !activation.DeclaredTargets.Contains(id, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            step.ValidChoices.Clear();
            if (choices.Count == 0)
            {
                step.ValidChoices.Add("mode:none");
                step.ChoiceLabels["mode:none"] = "没有其他合法目标，继续结算";
                promptKind = "option";
            }
            else
            {
                step.ValidChoices.AddRange(choices);
                promptKind = "active-target";
            }
        }
        else if (step.Kind == "target-morale")
        {
            targetPlayerIndex = Enumerable.Range(0, State.Players.Length)
                .FirstOrDefault(index => step.ValidChoices.Any(choice =>
                    State.Players[index].Morale.Any(morale => morale.InstanceId == choice)), -1);
            if (targetPlayerIndex < 0) targetPlayerIndex = null;
        }
        else if (step.Kind == "composite-glory-god-power-cost")
        {
            var player = State.Players[activation.Controller];
            var plannedFlips = activation.DeclaredValues.GetValueOrDefault("flipTargets", [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var choices = player.Morale.Where(card => !card.Tapped
                    && (card.IsGodPower || plannedFlips.Contains(card.InstanceId)))
                .Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (choices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "荣耀之路没有足够的已声明神力费用，效果未支付费用也未入栈");
                return;
            }
            promptKind = "resource-payment";
            targetPlayerIndex = activation.Controller;
        }
        else if (step.Kind == "composite-ordinary-payment")
        {
            var choices = CompositeOrdinaryPaymentChoices(State.Players[activation.Controller]).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (choices.Count < step.MinChoose)
            {
                RejectPendingActivation(activation, "没有可预声明的支付资源，效果未支付费用也未入栈");
                return;
            }
            promptKind = "resource-payment";
            targetPlayerIndex = activation.Controller;
        }
        else if (step.Kind == "composite-desert-hand")
        {
            var player = State.Players[activation.Controller];
            var repeatCountText = activation.DeclaredValues.GetValueOrDefault("desertRepeatCount", [])
                .SingleOrDefault();
            var discardCount = repeatCountText?.Split(':') is ["count", var countValue]
                && int.TryParse(countValue, out var parsedCount)
                    ? parsedCount
                    : activation.DeclaredValues.GetValueOrDefault("discardTargets", [])
                        .Count(id => !id.StartsWith("mode:", StringComparison.OrdinalIgnoreCase));
            var choices = player.Hand.Where(card => card.CardType == "legion" && card.Faction == "taiyangcheng"
                    && card.DisasterLevel == discardCount && card.InstanceId != activation.SourceInstanceId)
                .Select(card => card.InstanceId).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (choices.Count == 0)
            {
                RejectPendingActivation(activation, "手牌中没有天灾等级等于弃置数量的【太阳城】军团，效果未支付费用也未入栈");
                return;
            }
            promptKind = "hand-card";
        }
        else if (step.Kind == "composite-desert-slot")
        {
            var player = State.Players[activation.Controller];
            var choices = EmptySlots(player).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in activation.DeclaredValues.GetValueOrDefault("discardTargets", []))
                if (FindOnField(player, id, out var row, out var slot) is not null)
                    choices.Add($"{row}:{slot}");
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (choices.Count == 0)
            {
                RejectPendingActivation(activation, "弃置结算后仍没有合法登场位置，效果未支付费用也未入栈");
                return;
            }
            promptKind = "slot";
            targetPlayerIndex = activation.Controller;
        }
        else if (step.Kind == "public-palace-enemy")
        {
            var player = State.Players[activation.Controller];
            var referenceId = step.ReferenceDeclarationKey is { } referenceKey
                ? activation.DeclaredValues.GetValueOrDefault(referenceKey, []).SingleOrDefault()
                : null;
            var minimumCost = referenceId == "mode:none" ? 0
                : player.Graveyard.FirstOrDefault(card => card.InstanceId == referenceId)?.CurrentCost ?? int.MaxValue;
            var choices = PublicLegions(State.Players[1 - activation.Controller])
                .Where(card => card.CurrentCost >= minimumCost && player.Morale.Count >= card.CurrentCost)
                .Select(card => card.InstanceId).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (choices.Count == 0)
            {
                RejectPendingActivation(activation, "没有可覆盖已声明登场军团费用的合法敌方目标，效果未支付费用也未入栈");
                return;
            }
            promptKind = "active-target";
        }
        else if (step.Kind == "public-enemy-after-cost-debuff")
        {
            var threshold = step.CostThreshold ?? 0;
            var choices = PublicLegions(State.Players[1 - activation.Controller])
                .Where(card => card.CurrentCost - 1 <= threshold
                    && !activation.DeclaredTargets.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase))
                .Select(card => card.InstanceId).ToList();
            choices.Insert(0, "mode:none");
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            step.ChoiceLabels["mode:none"] = "不选择击杀目标";
            promptKind = "active-target";
        }
        else if (step.Kind == "public-enemy-after-declared-cost-debuff")
        {
            var debuffTarget = step.ReferenceDeclarationKey is { } referenceKey
                ? activation.DeclaredValues.GetValueOrDefault(referenceKey, []).SingleOrDefault()
                : null;
            var choices = PublicLegions(State.Players[1 - activation.Controller])
                .Where(card => card.CurrentCost - (card.InstanceId == debuffTarget ? 1 : 0) == 0)
                .Select(card => card.InstanceId).ToList();
            choices.Insert(0, "mode:none");
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            step.ChoiceLabels["mode:none"] = "不选择可击杀目标";
            promptKind = "active-target";
        }
        var promptData = new Dictionary<string, string>(step.ChoiceLabels, StringComparer.OrdinalIgnoreCase)
        {
            ["activationId"] = activation.ActivationId,
            ["activationStep"] = activation.CurrentStep.ToString(),
        };
        if (step.Kind == "controller-private-card")
        {
            promptKind = "card";
            var controller = State.Players[activation.Controller];
            foreach (var card in controller.Hand.Concat(controller.Library)
                .Where(card => step.ValidChoices.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase)))
                AddPromptCardData(promptData, card);
        }
        var addCancellationChoice = ShouldAddActivationCancellationChoice(step);
        if (step.CancellationPolicy == L12ActivationCancellationPolicy.SeparateChoice)
            promptData["skip"] = "取消整次发动";
        if (!string.IsNullOrWhiteSpace(step.SelectionConstraint))
            promptData["selectionConstraint"] = step.SelectionConstraint;
        if (activation.Ability == "public-trigger-declaration")
        {
            var triggerSource = FindPromptCard(activation.Controller, activation.SourceInstanceId)
                ?? CreateCard(activation.SourceCardId, activation.SourceInstanceId);
            var triggerCandidate = activation.TriggerCandidateId is null ? null
                : State.PendingTriggerStackCandidates.FirstOrDefault(candidate => candidate.CandidateId == activation.TriggerCandidateId);
            if (step.Kind == "option")
            {
                promptData["uiPattern"] = "effect-decision";
                promptData["sourceName"] = triggerCandidate?.SourceName ?? triggerSource.Name;
                promptData["effectText"] = triggerCandidate?.Text ?? step.Text;
                promptData["mode:use"] = "发动";
                promptData["mode:none"] = "不发动";
            }
            var referencedCardId = step.PreviewPresentation == "handled-card"
                && step.ReferenceDeclarationKey is { } previewReferenceKey
                    ? activation.DeclaredValues.GetValueOrDefault(previewReferenceKey, []).FirstOrDefault()
                    : null;
            var previewCard = referencedCardId is null
                || referencedCardId.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : FindPromptCard(activation.Controller, referencedCardId);
            if (previewCard is not null)
            {
                promptData["previewCardId"] = previewCard.InstanceId;
                promptData["previewPresentation"] = step.PreviewPresentation!;
                AddPromptCardData(promptData, previewCard);
            }
        }
        if (targetPlayerIndex is not null) promptData["targetPlayerIndex"] = targetPlayerIndex.Value.ToString();
        if (step.Kind == "opponent-hand-anonymous")
        {
            var hiddenCards = State.Players[1 - activation.Controller].Hand
                .Where(card => step.ValidChoices.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (hiddenCards.Length < step.MinChoose)
            {
                RejectPendingActivation(activation, "对方手牌已失效，响应未揭示且未进入堆叠");
                return;
            }
            var anonymousPrompt = CreateAnonymousHandChoicePrompt(activation.Controller, hiddenCards,
                "opponent-hand-card", step.Text, step.MinChoose, step.MaxChoose,
                "pending-activation", data: promptData);
            if (addCancellationChoice)
            {
                anonymousPrompt.ValidChoices.Add("skip");
                var cancellationLabel = step.CancellationPolicy == L12ActivationCancellationPolicy.SeparateChoice
                    ? "取消整次发动" : "不发动";
                anonymousPrompt.Data["skip"] = cancellationLabel;
                anonymousPrompt.ChoiceLabels["skip"] = cancellationLabel;
            }
            return;
        }
        var promptChoices = (addCancellationChoice ? step.ValidChoices.Append("skip") : step.ValidChoices)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        CreatePrompt(activation.Controller, promptKind, step.Text, promptChoices, step.MinChoose,
            Math.Min(step.MaxChoose, step.ValidChoices.Count),
            "pending-activation", isPrivate: true,
            data: promptData);
    }

    private static bool ShouldAddActivationCancellationChoice(L12ActivationSelectionStep step)
        => step.CancellationPolicy switch
        {
            L12ActivationCancellationPolicy.NotAllowed => false,
            L12ActivationCancellationPolicy.SeparateChoice => true,
            _ => !step.ValidChoices.Any(choice => choice.Equals("mode:none", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("no", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("skip", StringComparison.OrdinalIgnoreCase)),
        };

    private static bool IsDeterministicCostSelection(L12ActivationSelectionStep step)
    {
        if (step.ValidChoices.Count == 0 || step.MinChoose <= 0) return false;
        // Auto-pay only the shared resource-payment controls. Other colon costs may be
        // the player's last opportunity to cancel an active effect, or may carry ordering
        // semantics even when only one card is currently legal. Those flows opt in with
        // AutoSelectWhenExact after their own activation confirmation instead.
        return step.Kind is "resource-payment" or "composite-ordinary-payment";
    }

    private static bool IsOnlyNegativeOptionalChoice(L12ActivationSelectionStep step)
        => step.MinChoose == 1
            && step.MaxChoose == 1
            && step.ValidChoices.Count == 1
            && step.ValidChoices[0] is var choice
            && (choice.Equals("mode:none", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("no", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("skip", StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> AdjacentEmptySlots(L12PlayerState player, int row, int slot)
        => new[] { (row - 1, slot), (row + 1, slot), (row, slot - 1), (row, slot + 1) }
            .Where(position => position.Item1 is >= 0 and < 2 && position.Item2 is >= 0 and < 3
                && !(State.ActiveDisaster?.CardId == "S01-DS03" && position.Item1 == 1)
                && player.Field[position.Item1][position.Item2] is null)
            .Select(position => $"{position.Item1}:{position.Item2}");

    private static bool DeclaredNumericValueAtLeast(L12PendingActivation activation, string referenceKey,
        string? prefix, int minimum)
    {
        var choice = activation.DeclaredValues.GetValueOrDefault(referenceKey, []).SingleOrDefault();
        if (choice is null || prefix is null || !choice.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(choice[prefix.Length..], out var value) && value >= minimum;
    }

    private void ResolvePendingActivation(L12Prompt prompt, List<string> chosen)
    {
        if (!prompt.Data.TryGetValue("activationId", out var activationId)) return;
        var activation = State.PendingActivations.FirstOrDefault(item => item.ActivationId == activationId);
        if (activation is null) return;
        if (chosen.Count == 1 && chosen[0] == "skip")
        {
            State.PendingActivations.Remove(activation);
            ClearFreeMasterActivation(activation);
            if (activation.Ability == EffectGeneratedFreePlayAbility)
            {
                AbortEffectGeneratedFreePlay(activation, "已取消效果生成的打出声明；卡牌保留在原区域");
                return;
            }
            AddEvent("ability-cancelled", prompt.PlayerIndex, "已取消发动，未支付费用且未进入堆叠");
            if (activation.Ability == "composite-committed-play")
            {
                AbortCommittedCompositeEffectDeclaration(activation, "已打出的复合战术取消声明，卡牌结算至墓地");
                return;
            }
            if (activation.Ability is "composite-repeated-effect" or "repeated-tactic-effect")
            {
                ResumeAfterPostResolutionGeneratedInteraction();
                return;
            }
            if (activation.TriggerCandidateId is not null)
            {
                var candidate = State.PendingTriggerStackCandidates.FirstOrDefault(item =>
                    item.CandidateId == activation.TriggerCandidateId);
                if (candidate is not null) CleanupPublicTriggerReservation(candidate);
                State.PendingTriggerStackCandidates.RemoveAll(candidate => candidate.CandidateId == activation.TriggerCandidateId);
                AdvanceTriggerBatches();
            }
            if (activation.ResponseTargetStackItemId is not null) ResumeResponseAfterCancelledDeclaration(activation);
            return;
        }
        var step = activation.SelectionSteps[activation.CurrentStep];
        if (step.Kind == "opponent-hand-anonymous")
            chosen = chosen.Select(choice => ResolveHiddenPromptChoice(prompt, choice)).ToList();
        if (chosen.Count < step.MinChoose || chosen.Count > step.MaxChoose
            || chosen.Any(id => !step.ValidChoices.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            RejectPendingActivation(activation, "目标声明已失效，效果未支付费用也未入栈");
            return;
        }
        activation.DeclaredTargets.AddRange(chosen);
        if (!string.IsNullOrWhiteSpace(step.DeclarationKey))
            activation.DeclaredValues[step.DeclarationKey] = chosen.ToList();
        activation.CurrentStep++;
        while (activation.CurrentStep < activation.SelectionSteps.Count
            && activation.SelectionSteps[activation.CurrentStep].SkipWhenPreviousStepEmpty
            && chosen.Count == 0)
        {
            activation.CurrentStep++;
        }
        if (activation.CurrentStep < activation.SelectionSteps.Count)
        {
            CreateActivationStepPrompt(activation);
            return;
        }
        CompleteResolvedPendingActivation(activation);
    }

    private void CompleteResolvedPendingActivation(L12PendingActivation activation)
    {
        State.PendingActivations.Remove(activation);

        if (activation.TriggerCandidateId is not null)
        {
            CompleteTriggerDeclaration(activation);
            return;
        }

        if (activation.PlayCardInstanceId is not null)
        {
            if (activation.Ability == EffectGeneratedFreePlayAbility)
            {
                CompleteEffectGeneratedFreePlay(activation);
                return;
            }
            if (activation.Ability == "composite-committed-play")
            {
                CompleteCommittedCompositeEffectDeclaration(activation);
                return;
            }
            if (activation.Ability == "composite-repeated-effect")
            {
                CompleteRepeatedCompositeEffectDeclaration(activation);
                return;
            }
            if (activation.Ability == "repeated-tactic-effect")
            {
                CompleteRepeatedSimpleTacticEffect(activation);
                return;
            }
            if (activation.Ability == "composite-play")
            {
                CompleteCompositeHandPlayDeclaration(activation);
                return;
            }
            var declaredTarget = activation.DeclaredTargets.SingleOrDefault();
            var card = State.Players[activation.Controller].Hand.FirstOrDefault(candidate =>
                candidate.InstanceId == activation.PlayCardInstanceId);
            if (card is null || DeclaredEnemyTarget(activation.Controller, declaredTarget) is null)
            {
                AddEvent("ability-rejected", activation.Controller, "手牌来源或目标已不合法，未支付费用也未入栈");
                return;
            }
            var handPlayResult = PlayCard(activation.Controller, new L12Command("playCard", card.InstanceId,
                Target: new L12AttackTarget("legion", declaredTarget)));
            if (!handPlayResult.Accepted) AddEvent("ability-rejected", activation.Controller, handPlayResult.Error ?? "手牌打出失败");
            return;
        }

        if (activation.ResponseTargetStackItemId is not null)
        {
            if (activation.Ability == "public-response-declaration")
            {
                CompletePublicResponseDeclaration(activation);
                return;
            }
            var responsePlayer = State.Players[activation.Controller];
            var response = FindOnField(responsePlayer, activation.SourceInstanceId, out _, out _);
            var declaredTarget = activation.DeclaredTargets.SingleOrDefault();
            if (response is null || !L12StructuredCardRules.RequiresOwnLegionResponseTarget(response.CardId)
                || State.EffectStack.All(item => item.StackItemId != activation.ResponseTargetStackItemId)
                || !PublicLegions(responsePlayer).Any(card => card.InstanceId == declaredTarget))
            {
                AddEvent("ability-rejected", activation.Controller, "响应来源或目标已不合法，未支付费用也未入栈");
                ResumeResponseAfterCancelledDeclaration(activation);
                return;
            }
            CommitS1ReactionResponse(activation.Controller, response, activation.ResponseTargetStackItemId, declaredTarget);
            return;
        }

        if (activation.Ability == "starter-disaster-attack")
        {
            CompleteStarterDisasterAttackDiscard(activation);
            return;
        }

        var player = State.Players[activation.Controller];
        var source = FindOnField(player, activation.SourceInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == activation.SourceInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId)
            ?? player.SpecialZones.Trials.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId)
            ?? player.Graveyard.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId
                && IsLegalGraveyardActiveAbilitySource(player, card, activation.Ability))
            ?? (activation.SourceCardId == player.MasterId ? CreateActiveMasterSource(player, activation.SourceInstanceId) : null)
            ?? (activation.SourceInstanceId == $"faction-{activation.Controller}" ? CreateCard(activation.SourceCardId, activation.SourceInstanceId) : null);
        if (source is null || activation.DeclaredTargets.Any(id => !IsDeclaredChoiceStillLegal(activation.Controller, id, activation)))
        {
            ClearFreeMasterActivation(activation);
            AddEvent("ability-rejected", activation.Controller, "来源或目标已不合法，效果未支付费用也未入栈");
            return;
        }
        var result = CommitActiveAbility(activation.Controller, source, activation.Ability,
            activation.DeclaredTargets.Count == 0 ? null : string.Join('|', activation.DeclaredTargets));
        if (!result.Accepted) AddEvent("ability-rejected", activation.Controller, result.Error ?? "主动效果发动失败");
    }

    private void RejectPendingActivation(L12PendingActivation activation, string reason)
    {
        State.PendingActivations.Remove(activation);
        ClearFreeMasterActivation(activation);
        if (activation.Ability == EffectGeneratedFreePlayAbility)
        {
            AbortEffectGeneratedFreePlay(activation, reason);
            return;
        }
        if (activation.Ability == "composite-committed-play")
        {
            AbortCommittedCompositeEffectDeclaration(activation, reason);
            return;
        }
        if (activation.Ability is "composite-repeated-effect" or "repeated-tactic-effect")
        {
            AddEvent("effect-cancelled", activation.Controller, reason);
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        AddEvent("ability-rejected", activation.Controller, reason);
        if (activation.ResponseTargetStackItemId is not null)
        {
            ResumeResponseAfterCancelledDeclaration(activation);
            return;
        }
        if (activation.TriggerCandidateId is null) return;
        var triggerCandidate = State.PendingTriggerStackCandidates.FirstOrDefault(candidate =>
            candidate.CandidateId == activation.TriggerCandidateId);
        if (triggerCandidate is not null) CleanupPublicTriggerReservation(triggerCandidate);
        State.PendingTriggerStackCandidates.RemoveAll(candidate => candidate.CandidateId == activation.TriggerCandidateId);
        AdvanceTriggerBatches();
    }

    private void ResumeResponseAfterCancelledDeclaration(L12PendingActivation activation)
    {
        if (State.EffectStack.All(item => item.StackItemId != activation.ResponseTargetStackItemId)) return;
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = activation.Controller };
        OfferResponse();
    }

    private L12CardInstance CreateActiveMasterSource(L12PlayerState player, string instanceId)
    {
        var source = CreateCard(player.MasterId, instanceId);
        source.Tapped = player.MasterTapped;
        return source;
    }

    private void ClearFreeMasterActivation(L12PendingActivation activation)
    {
        if (State.FreeMasterActivation is { } free
            && free.Controller == activation.Controller
            && free.Ability.Equals(activation.Ability, StringComparison.OrdinalIgnoreCase))
            State.FreeMasterActivation = null;
    }

    private bool IsDeclaredChoiceStillLegal(int controller, string choice, L12PendingActivation? activation = null)
    {
        if (choice is "yes" or "no" or "skip" or "top" or "bottom") return true;
        if (choice.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)) return true;
        if (choice.StartsWith("rune:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(choice.AsSpan("rune:".Length), out var runeIndex))
            return runeIndex >= 1 && runeIndex <= State.Players[controller].SpecialZones.Runes;
        if (choice.StartsWith("battlefield:", StringComparison.OrdinalIgnoreCase))
        {
            var entryStep = activation?.SelectionSteps.FirstOrDefault(step => step.Kind == "effect-entry-battlefield"
                && step.DeclarationKey is not null
                && activation.DeclaredValues.GetValueOrDefault(step.DeclarationKey, [])
                    .Contains(choice, StringComparer.OrdinalIgnoreCase));
            var reference = entryStep?.ReferenceDeclarationKey is { } referenceKey
                ? activation!.DeclaredValues.GetValueOrDefault(referenceKey, []).SingleOrDefault()
                : activation?.DeclaredTargets.FirstOrDefault();
            var card = reference is null ? null : FindPromptCard(controller, reference);
            var battlefield = ParseEffectEntryBattlefieldChoice(choice);
            return card is not null && battlefield is not null
                && EffectEntryBattlefieldChoices(controller, card).Contains(battlefield.Value);
        }
        // PendingActivation 也用于士气/神力等真实资源的预声明。士气不是
        // L12CardInstance，不能仅依赖 FindPromptCard 校验，否则合法选择会在支付前被误判失效。
        if (State.Players[controller].Morale.Any(card => card.InstanceId == choice)) return true;
        if (GetAbilities(State.Players[controller].MasterId).Any(view => view.Id.Equals(choice, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (choice.Split(':') is [var rowText, var slotText]
            && int.TryParse(rowText, out var row) && int.TryParse(slotText, out var slot)
            && row is >= 0 and < 2 && slot is >= 0 and < 3)
        {
            var currentStep = activation is not null && activation.CurrentStep < activation.SelectionSteps.Count
                ? activation.SelectionSteps[activation.CurrentStep] : null;
            if (currentStep?.Kind == "owner-unused-slot")
            {
                var target = currentStep.TargetPlayerIndex ?? controller;
                var used = activation!.SelectionSteps
                    .Where(step => step.Kind == "owner-unused-slot" && step.TargetPlayerIndex == target
                        && !string.IsNullOrWhiteSpace(step.DeclarationKey))
                    .SelectMany(step => activation.DeclaredValues.GetValueOrDefault(step.DeclarationKey!, []));
                return State.Players[target].Field[row][slot] is null
                    && !used.Contains(choice, StringComparer.OrdinalIgnoreCase);
            }
            var effectEntryBattlefield = activation?.DeclaredTargets
                .Select(ParseEffectEntryBattlefieldChoice)
                .FirstOrDefault(index => index is not null);
            if (effectEntryBattlefield is not null)
                return State.Players[effectEntryBattlefield.Value].Field[row][slot] is null;
            if (activation?.Ability == "magatamaMove" && activation.DeclaredTargets.Count > 0)
            {
                var battlefield = State.Players[controller];
                return FindOnField(battlefield, activation.DeclaredTargets[0], out _, out _) is not null
                    && battlefield.Field[row][slot] is null
                    && EffectCavalryDestinations(battlefield).Contains(choice);
            }
            if (activation?.Ability == "horusRevive")
            {
                var occupant = State.Players[controller].Field[row][slot];
                return occupant is null || activation.DeclaredValues.GetValueOrDefault("discardCosts", [])
                    .Contains(occupant.InstanceId, StringComparer.OrdinalIgnoreCase);
            }
            return State.Players[controller].Field[row][slot] is null;
        }
        return FindPromptCard(controller, choice) is not null;
    }

    private L12CardInstance? DeclaredEnemyTarget(int controller, string? instanceId,
        Func<L12CardInstance, bool>? predicate = null)
    {
        var card = FindOnField(State.Players[1 - controller], instanceId, out _, out _);
        return card is not null && IsFieldLegion(card) && !card.Hidden && (predicate?.Invoke(card) ?? true) ? card : null;
    }

    private bool IsEnemyTargetLegal(int controller, string? instanceId, Func<L12CardInstance, bool> predicate)
        => DeclaredEnemyTarget(controller, instanceId, predicate) is not null;

    private void ApplyDeclaredTroopsDelta(L12StackItem item, int delta)
    {
        var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
        if (target is not null) AddTimedModifier(target, delta, 0, State.TurnSerial, item.SourceName);
    }

    private void ResolveDeclaredPalaceExchangeKill(L12StackItem item)
    {
        var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
        if (target is not null) KillTarget(item, target.InstanceId, "被凌霄宝殿击杀");
        FinishStackItem(item);
    }

    private void ResolveDeclaredPalaceExchangeRevive(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var paid = int.TryParse(item.Data.GetValueOrDefault("paid"), out var parsed) ? parsed : 0;
        var reviveId = item.Data.GetValueOrDefault("entryCard");
        var battlefield = ParseEffectEntryBattlefieldChoice(item.Data.GetValueOrDefault("entryBattlefield"));
        var slotChoice = item.Data.GetValueOrDefault("entrySlot");
        var revive = player.Graveyard.FirstOrDefault(card => card.InstanceId == reviveId
            && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "tianting")
            && card.CurrentCost <= paid);
        if (revive is not null && battlefield == item.Controller && slotChoice is not null
            && slotChoice.Split(':') is [var rowText, var slotText]
            && int.TryParse(rowText, out var row) && int.TryParse(slotText, out var slot)
            && row is >= 0 and <= 1 && slot is >= 0 and <= 2 && player.Field[row][slot] is null)
            SummonFromAnyPrivateZone(player, revive.InstanceId, slotChoice, tapped: false);
        FinishStackItem(item);
    }

    private void QueueSimultaneousDeathTriggers(
        IEnumerable<(int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)> deaths)
    {
        var materializedDeaths = deaths.ToArray();
        var candidates = materializedDeaths.SelectMany(entry =>
        {
            var sameTime = BuildS1LeaveReactionCandidates(entry.Controller, entry.SourceSnapshot).ToList();
            if (HasDeathTrigger(entry.SourceSnapshot))
                sameTime.Add(CreateTriggerCandidate(entry.Controller, entry.SourceSnapshot, "death", "【阵亡时】效果",
                    new Dictionary<string, string> { ["cause"] = "effect" }, entry.SourceSnapshot));
            return sameTime;
        }).ToList();
        foreach (var defeatedController in materializedDeaths.Select(entry => entry.Controller).Distinct())
        {
            var morrigan = BuildMorriganEnemyDeathCandidate(defeatedController);
            if (morrigan is not null) candidates.Add(morrigan);
        }
        foreach (var entry in materializedDeaths)
        {
            var nephthys = BuildNephthysOwnDeathCandidate(entry.Controller, entry.SourceSnapshot);
            if (nephthys is not null) candidates.Add(nephthys);
            var artemis = BuildArtemisRangedDeathCandidate(entry.Controller, entry.SourceSnapshot);
            if (artemis is not null) candidates.Add(artemis);
        }
        QueueTriggerCandidates(candidates);
    }

    private void QueueTriggerCandidates(IEnumerable<L12TriggerCandidate> candidates)
    {
        var supplied = candidates.ToArray();
        var materialized = supplied.Where(PrepareBatch6JAEnterCandidate)
            .Where(PrepareBatch6JBPublicTriggerCandidate)
            .Where(PrepareBatch6IBPublicTriggerCandidate)
            .Where(PrepareVerifiedAtomicOptionalCandidate).ToArray();
        if (materialized.Length == 0)
        {
            if (supplied.Length > 0) TrySettleScheduledDisasterIfIdle();
            return;
        }

        foreach (var planned in L12TriggerBatchPlanner.Plan(materialized, State.ActivePlayer))
        {
            State.PendingTriggerBatches.Add(new L12TriggerBatch
            {
                BatchId = $"batch-{++State.TriggerBatchSequence}",
                Controller = planned[0].Controller,
                Candidates = planned.ToList(),
            });
        }
        if (!State.IsResolvingStack && State.EffectStack.Count == 0 && State.ResponseWindow is null)
            AdvanceTriggerBatches();
    }

    private L12TriggerCandidate CreateTriggerCandidate(int controller, L12CardInstance card, string trigger, string text,
        IReadOnlyDictionary<string, string>? data = null, L12CardInstance? sourceSnapshot = null)
    {
        var candidate = new L12TriggerCandidate
        {
            CandidateId = $"trigger-{++State.TriggerBatchSequence}", Controller = controller,
            SourceInstanceId = card.InstanceId, SourceCardId = card.CardId, SourceName = card.Name,
            Trigger = trigger, Text = text, SourceSnapshot = CaptureLastKnownSourceSnapshot(sourceSnapshot ?? card),
        };
        if (data is not null)
            foreach (var pair in data) candidate.Data[pair.Key] = pair.Value;
        AttachDefaultTriggerCompositePlan(candidate);
        candidate.Data.TryAdd("triggerEffectText", ResolveTriggeredEffectDisplayText(card, trigger, text, candidate.Data));
        return candidate;
    }

    /// <summary>
    /// 无公开声明、无费用且每段均强制的触发式复合计划，可在候选建立时直接绑定首段。
    /// 需要 PendingActivation 的计划仍由各声明计划在提交后写入不可变数据，避免提前
    /// 跳过公开选择或读取隐藏信息。
    /// </summary>
    private void AttachDefaultTriggerCompositePlan(L12TriggerCandidate candidate)
    {
        if (candidate.Data.ContainsKey("compositePlan")) return;
        var data = DefaultTriggerCompositePlanData(candidate.SourceCardId, candidate.Trigger);
        if (data is null) return;
        foreach (var pair in data) candidate.Data[pair.Key] = pair.Value;
    }

    private Dictionary<string, string>? DefaultTriggerCompositePlanData(string cardId, string trigger)
    {
        var planId = $"trigger:{cardId}:{trigger}";
        var segments = L12CompositeEffectPlans.Segments(planId);
        if (segments.Count == 0 || segments.Any(segment => segment.RequiredMode is not null
                || segment.Cost != 0 || segment.PublicTargetKeys is { Length: > 0 })) return null;
        return CompositeFirstSegmentData(planId,
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
    }

    internal static string ResolveTriggeredEffectDisplayText(L12CardInstance card, string trigger, string fallback,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(card.EffectText)) return fallback;
        var lines = card.EffectText.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return fallback;

        var ability = data?.GetValueOrDefault("ability");
        string[] markers = ability switch
        {
            "angusTacticTrial" => ["战术效果", "结算成功时"],
            "grailRoundTableRune" => ["圆桌骑士", "登场时"],
            "artemisDeathFlip" => ["远程军团", "阵亡时"],
            "anderstorpRingDraw" => ["主宰受到伤害时"],
            "tsukuyomiFrontAttackBuff" => ["后排位移至前排时"],
            "tsukuyomiReadyMorale" => ["前排位移至后排时"],
            "tsukuyomiFollowMove" => ["军团位移时"],
            "margaretMasterDamage" => ["主宰因效果受到伤害时"],
            _ => trigger switch
            {
                "promotion-enter" => ["晋升登场"],
                "enter" => ["登场时"],
                "attack" => ["进攻时"],
                "after-damage" => ["对主宰造成伤害时", "主宰受到伤害时"],
                "disaster" => ["触发"],
                "death" => ["阵亡时"],
                "leave" or "play" => ["离场时"],
                "after-attack" or "trojan-after-attack" => ["进攻后"],
                "forge-ready-after-kill" => [],
                "master-morale-return" => ["主宰效果返还", "返还4张及以上"],
                "medjed-master-damage" => ["主宰受到伤害时"],
                "discard-trigger" => ["弃置时", "从牌库弃置", "从手牌弃置"],
                "s2-after-opponent-tactic" => ["战术效果结算后", "战术卡效果结算后"],
                "morrigan-enemy-death" => ["对方军团阵亡时"],
                "nephthys-own-death" => ["我方军团阵亡时"],
                "trial-complete" => ["触发"],
                "reaction" when fallback.Contains("进攻后", StringComparison.Ordinal) => ["进攻后"],
                "reaction" when fallback.Contains("主宰受到伤害", StringComparison.Ordinal) => ["主宰受到伤害时"],
                "reaction" when fallback.Contains("军团阵亡", StringComparison.Ordinal) => ["军团阵亡时"],
                "reaction" when fallback.Contains("军团离场", StringComparison.Ordinal) => ["军团", "离场时"],
                _ => [],
            },
        };
        var abilitySegments = lines.SelectMany(SplitEffectAbilitySegments).ToArray();
        var markerMatch = abilitySegments.FirstOrDefault(segment => markers.Length > 0
            && (ability is null ? markers.Any(marker => NormalizeTriggeredEffectText(segment).Contains(
                    NormalizeTriggeredEffectText(marker), StringComparison.Ordinal))
                : markers.All(marker => NormalizeTriggeredEffectText(segment).Contains(
                    NormalizeTriggeredEffectText(marker), StringComparison.Ordinal))));
        if (!string.IsNullOrWhiteSpace(markerMatch)) return markerMatch.Trim();

        var normalizedFallback = NormalizeTriggeredEffectText(fallback);
        var fallbackMatch = abilitySegments.FirstOrDefault(segment => normalizedFallback.Length >= 2
            && NormalizeTriggeredEffectText(segment).Contains(normalizedFallback, StringComparison.Ordinal));
        return fallbackMatch ?? fallback;
    }

    private static IEnumerable<string> SplitEffectAbilitySegments(string text)
    {
        const string boundary = @"(?=(?:(?:我方|对方)\s*回合(?:1次)?\s*)?(?:晋升登场|登场时|进攻时|阵亡时|离场时|击杀时|进攻后|主动休整|主动\s|触发\s))";
        var pieces = System.Text.RegularExpressions.Regex.Split(text, boundary)
            .Select(piece => piece.Trim().TrimStart('。').Trim())
            .Where(piece => piece.Length > 0)
            .ToArray();
        return pieces.Length == 0 ? [text] : pieces;
    }

    private static string NormalizeTriggeredEffectText(string value)
    {
        var normalized = value;
        string[] noise = ["【", "】", "〈", "〉", "《", "》", " ", "　", "：", ":", "，", ",", "。", "·", "•"];
        foreach (var token in noise) normalized = normalized.Replace(token, string.Empty, StringComparison.Ordinal);
        return normalized.Replace("反击战术", string.Empty, StringComparison.Ordinal)
            .Replace("手牌效果", string.Empty, StringComparison.Ordinal)
            .Replace("触发式效果", string.Empty, StringComparison.Ordinal)
            .Replace("效果", string.Empty, StringComparison.Ordinal);
    }

    private bool IsDirectTriggeredEffect(string trigger, L12CardInstance source, string text)
        => trigger switch
        {
            "enter" or "promotion-enter" or "after-damage" => true,
            "attack" => !text.Contains("不触发", StringComparison.Ordinal)
                && HasImmediateEffect(source, trigger),
            "play" or "active" or "disaster" => false,
            "response-negate" or "response-block" or "response-retarget-master" or "reaction" or "s2-reaction" => false,
            _ when !string.IsNullOrWhiteSpace(source.EffectText) => true,
            _ => false,
        };

    private string ResolveEffectPresentationText(L12CardInstance source, string trigger, string fallback,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var abilityId = data?.GetValueOrDefault("ability");
        if (!string.IsNullOrWhiteSpace(abilityId))
        {
            var ability = GetAbilities(source.CardId)
                .FirstOrDefault(candidate => candidate.Id.Equals(abilityId, StringComparison.OrdinalIgnoreCase));
            if (ability is not null && !string.IsNullOrWhiteSpace(ability.Label)) return ability.Label;
        }

        if (trigger is "response-block" or "response-retarget-master" or "response-negate" or "reaction" or "s2-reaction")
            return ResolveResponseEffectDisplayText(source, fallback);

        var lines = (source.EffectText ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (trigger == "play")
            return lines.Length == 0 ? fallback : string.Join(' ', lines);
        return ResolveTriggeredEffectDisplayText(source, trigger, fallback, data);
    }

    internal static string ResolveResponseEffectDisplayText(L12CardInstance source, string fallback)
    {
        if (string.IsNullOrWhiteSpace(source.EffectText)) return fallback;
        string[] markers =
        [
            "进攻我方军团时", "进攻我方主宰时", "进攻或发动效果时", "军团登场时",
            "进攻时", "发动战术效果或圣物效果时", "进行抵挡/支援时", "以手牌以外的方式登场时",
            "因效果将1张卡牌加入手牌时", "休整的卡牌因效果转为活跃时",
        ];
        var lines = source.EffectText.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = Array.FindIndex(lines, line => markers.Any(marker => NormalizeTriggeredEffectText(line)
            .Contains(NormalizeTriggeredEffectText(marker), StringComparison.Ordinal)));
        if (index < 0) return fallback;

        var abilityLines = new List<string> { lines[index] };
        for (var next = index + 1; next < lines.Length && lines[next].StartsWith('•'); next++)
            abilityLines.Add(lines[next]);
        var selected = string.Join(' ', abilityLines);
        var sentences = selected.Split('。', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstResponseSentence = Array.FindIndex(sentences, sentence => markers.Any(marker =>
            NormalizeTriggeredEffectText(sentence).Contains(NormalizeTriggeredEffectText(marker), StringComparison.Ordinal)));
        return firstResponseSentence <= 0 ? selected : string.Join('。', sentences.Skip(firstResponseSentence));
    }

    private void PublishEffectPresentation(string eventType, int? controller, L12CardInstance source,
        string trigger, string fallback, IReadOnlyDictionary<string, string>? data = null)
        => AddEvent(eventType, controller, ResolveEffectPresentationText(source, trigger, fallback, data), source);

    private bool HasDeathTrigger(L12CardInstance card)
        => card.SuppressDeathUntilTurn < State.TurnSerial && (card.CardId is "S01-0102" or "S01-0108" or "S01-0417"
            || S1ExtendedDeathCards.Contains(card.CardId) || IsS1FactionDeathCard(card.CardId)
            || IsS2FactionDeathCard(card.CardId)
            || L12VerifiedAtomicPrograms.Find(card.CardId, "death") is not null);

    private void AdvanceTriggerBatches()
    {
        if (State.PendingPrompts.Any(prompt => prompt.Continuation == "trigger-batch-order")) return;
        AdvancePendingTriggerStackCandidates();
        if (State.PendingTriggerStackCandidates.Count > 0
            || State.PendingActivations.Any(activation => activation.TriggerCandidateId is not null)) return;
        while (State.PendingTriggerBatches.Count > 0)
        {
            var batch = State.PendingTriggerBatches[0];
            State.PendingTriggerBatches.RemoveAt(0);
            if (batch.Candidates.Count == 1)
            {
                State.PendingTriggerStackCandidates.Add(batch.Candidates[0]);
                AdvancePendingTriggerStackCandidates();
                if (State.PendingTriggerStackCandidates.Count > 0
                    || State.PendingActivations.Any(activation => activation.TriggerCandidateId is not null)) return;
                continue;
            }
            var data = new Dictionary<string, string> { ["batchId"] = batch.BatchId, ["choiceMode"] = "ordered" };
            foreach (var candidate in batch.Candidates)
            {
                data[candidate.CandidateId] = $"〈{candidate.SourceName}〉{candidate.Text}";
                data[$"sourceInstance:{candidate.CandidateId}"] = candidate.SourceInstanceId;
                data[$"trigger:{candidate.CandidateId}"] = candidate.Trigger;
            }
            State.PendingTriggerBatches.Insert(0, batch);
            CreatePrompt(batch.Controller, "trigger-order", "同一时点有多个效果触发，请按结算先后排列",
                batch.Candidates.Select(candidate => candidate.CandidateId), batch.Candidates.Count, batch.Candidates.Count,
                "trigger-batch-order", isPrivate: false, data: data);
            return;
        }
        if (!State.IsResolvingStack && State.EffectStack.Count > 0 && State.ResponseWindow is null)
            BeginResponseWindow(State.EffectStack[^1]);
        else if (State.EffectStack.Count == 0)
        {
            TrySettleScheduledDisasterIfIdle();
            // A declaration can disappear without ever creating a stack item (for example an
            // optional attack trigger whose cost/target is unavailable).  In that path there is
            // no FinishStackItem callback to resume the persisted combat state machine, so the
            // attack used to remain stranded at AttackerAttackTiming.  Resuming here is safe:
            // AdvanceCombatTimelineIfIdle rechecks every prompt/activation/trigger/stack guard.
            AdvanceCombatTimelineIfIdle();
        }
    }

    private void ResolveTriggerBatchOrder(L12Prompt prompt, List<string> chosen)
    {
        var batch = State.PendingTriggerBatches.FirstOrDefault(item => item.BatchId == prompt.Data.GetValueOrDefault("batchId"));
        if (batch is null) return;
        State.PendingTriggerBatches.Remove(batch);
        var byId = batch.Candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase);
        // 玩家选择的是结算顺序；堆叠后进先出，因此反向压栈。
        foreach (var id in chosen.AsEnumerable().Reverse()) State.PendingTriggerStackCandidates.Add(byId[id]);
        AddEvent("trigger-order", batch.Controller, $"{State.Players[batch.Controller].Name} 已排列同一时点的 {chosen.Count} 个触发效果");
        AdvanceTriggerBatches();
    }

    private void AdvancePendingTriggerStackCandidates()
    {
        while (State.PendingTriggerStackCandidates.Count > 0)
        {
            var candidate = State.PendingTriggerStackCandidates[0];
            if (candidate.Data.ContainsKey("declaration-committing")) return;
            if (State.PendingActivations.Any(activation => activation.TriggerCandidateId == candidate.CandidateId)) return;
            if (!candidate.Data.ContainsKey("declaration-complete") && TryBeginTriggerDeclaration(candidate)) return;
            State.PendingTriggerStackCandidates.RemoveAt(0);
            AddTriggerCandidateToStack(candidate);
        }
    }

    private void AddTriggerCandidateToStack(L12TriggerCandidate candidate)
    {
        var stackText = candidate.Data.GetValueOrDefault("stackText", candidate.Text);
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = candidate.Controller,
            SourceInstanceId = candidate.SourceInstanceId,
            SourceCardId = candidate.SourceCardId,
            SourceName = candidate.SourceName,
            Trigger = candidate.Trigger,
            Text = stackText,
            SourceSnapshot = candidate.SourceSnapshot,
        };
        foreach (var pair in candidate.Data) item.Data[pair.Key] = pair.Value;
        if (State.IsResolvingStack) State.DeferredEffectStack.Add(item);
        else State.EffectStack.Add(item);
        RevealSetReactionSourceWhenStacked(candidate);
        var source = FindSource(item) ?? candidate.SourceSnapshot ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        AddEvent("effect-trigger", candidate.Controller,
            candidate.Data.GetValueOrDefault("triggerEffectText", candidate.Text),
            source);
        AddEvent("stack-push", candidate.Controller, $"〈{candidate.SourceName}〉的{stackText}进入同一时点触发批次",
            source);
    }

    private void RevealSetReactionSourceWhenStacked(L12TriggerCandidate candidate)
    {
        if (candidate.Trigger != "reaction" || !IsCounterTactic(candidate.SourceCardId)) return;
        var player = State.Players[candidate.Controller];
        var source = FindOnField(player, candidate.SourceInstanceId, out var row, out var slot);
        if (source is null || !source.Hidden) return;
        player.Field[row][slot] = null;
        source.Hidden = false;
        if (!player.Resolving.Any(card => card.InstanceId == source.InstanceId)) player.Resolving.Add(source);
        AddEvent("reveal", candidate.Controller, $"〈{source.Name}〉在触发效果入栈时翻开", source);
    }

    private bool TryBeginTriggerDeclaration(L12TriggerCandidate candidate)
    {
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var source = FindAuthoritativeCard(candidate.SourceInstanceId)
            ?? candidate.SourceSnapshot ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        if (TryBeginPublicTriggerDeclaration(candidate, source)) return true;
        var steps = new List<L12ActivationSelectionStep>();
        if (candidate.SourceCardId == "S02-0516" && candidate.Trigger == "attack")
        {
            var canUse = player.Morale.Any(card => card.IsGodPower && !card.Tapped)
                && PublicLegions(player).Any() && PublicLegions(opponent).Any();
            steps.Add(CompositeStep("option", "mode", "汉尼拔：预先声明是否消耗1神力并选择双方各1张军团",
                canUse ? ["mode:none", "mode:use"] : ["mode:none"], 1, 1,
                new()
                {
                    ["mode:none"] = "不消耗神力且不降低双方军团兵力",
                    ["mode:use"] = "消耗1神力：我方与对方各1张军团本回合兵力-2000",
                }));
            steps.Add(CompositeStep("target-morale", "hannibalCost", "汉尼拔：预先选择消耗的1神力",
                player.Morale.Where(card => card.IsGodPower && !card.Tapped).Select(card => card.InstanceId), 1,
                requiredChoice: "mode:use"));
            steps.Add(CompositeStep("field-legion", "hannibalOwn", "汉尼拔：预先选择我方1张军团",
                PublicLegions(player).Select(card => card.InstanceId), 1, requiredChoice: "mode:use"));
            steps.Add(CompositeStep("enemy-legion", "hannibalEnemy", "汉尼拔：预先选择对方1张军团",
                PublicLegions(opponent).Select(card => card.InstanceId), 1, requiredChoice: "mode:use"));
        }
        var postAttackDeclaration = L12StructuredCardRules.PostAttackDeclarationKind(
            candidate.SourceCardId, candidate.Trigger);
        if (steps.Count > 0)
        {
            // 已由公共复合声明计划建立。
        }
        else if (postAttackDeclaration == "last-stand")
        {
            var rested = PublicLegions(opponent).Where(card => card.Tapped).ToList();
            var choices = rested.Select(card => card.InstanceId).Prepend("mode:all").ToList();
            var labels = rested.ToDictionary(card => card.InstanceId, card => $"单体：{card.Name}兵力-2000");
            labels["mode:all"] = "全部休整军团兵力-1000";
            steps.Add(new L12ActivationSelectionStep
            {
                Kind = "option", Text = "拼死反抗：预先声明结算方式与合法目标",
                ValidChoices = choices, MinChoose = 1, MaxChoose = 1, ChoiceLabels = labels,
            });
        }
        else if (postAttackDeclaration == "seppuku")
        {
            steps.Add(TriggerStep("field-legion", "切腹仪式：预先选择对方1张军团，直到下个我方回合结束前费用-2",
                PublicLegions(opponent).Select(card => card.InstanceId), 1));
        }
        else if (candidate.Trigger != "death") return false;
        else if (candidate.SourceCardId == "S01-0108" && State.ActivePlayer != candidate.Controller)
        {
            steps.Add(TriggerStep("target-morale", "花木兰：选择对方1张休整士气，使其下个重置阶段无法转为活跃",
                opponent.Morale.Where(card => card.Tapped).Select(card => card.InstanceId), 1));
        }
        else if (candidate.SourceCardId == "S01-0206")
        {
            steps.Add(TriggerStep("field-legion", "萨拉丁：可选择我方1张〈陵墓守卫〉进行位移",
                PublicLegions(player).Where(card => card.CardId == "S01-0212").Select(card => card.InstanceId), 0));
            steps.Add(new L12ActivationSelectionStep
            {
                Kind = "unused-slot", Text = "选择〈陵墓守卫〉位移后的位置", ValidChoices = EmptySlots(player).ToList(),
                MinChoose = 1, MaxChoose = 1, SkipWhenPreviousStepEmpty = true,
            });
        }
        else if (candidate.SourceCardId == "S01-0305")
        {
            var graveCards = player.Graveyard.Where(card => card.InstanceId != candidate.SourceInstanceId
                    && CanEnterHandOrLibrary(card)).Select(card => card.InstanceId).ToList();
            steps.Add(TriggerStep("order", "勇士比约恩：选择墓地4张牌并决定返回牌库底部的顺序；完成声明后主宰受到1点伤害",
                graveCards, 4, 4));
            steps.Add(new L12ActivationSelectionStep
            {
                Kind = "unused-slot", Text = "选择〈勇士比约恩〉休整登场的位置", ValidChoices = EmptySlots(player).ToList(),
                MinChoose = 1, MaxChoose = 1,
            });
        }
        else if (candidate.SourceCardId == "S01-0307")
        {
            steps.Add(TriggerStep("grave-card", "阿尔维达：可选择墓地1张费用不高于3的【阿斯加德】卡牌加入手牌",
                player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard")
                    && card.CurrentCost <= 3).Select(card => card.InstanceId), 0));
        }
        else if (candidate.SourceCardId == "S01-0308")
        {
            steps.Add(TriggerStep("grave-card", "血斧艾瑞克：可选择墓地1张费用不高于3的【阿斯加德】军团活跃登场",
                player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard")
                    && card.CardType == "legion" && card.CurrentCost <= 3).Select(card => card.InstanceId), 0));
            steps.Add(new L12ActivationSelectionStep
            {
                Kind = "unused-slot", Text = "选择该军团活跃登场的位置", ValidChoices = EmptySlots(player).ToList(),
                MinChoose = 1, MaxChoose = 1, SkipWhenPreviousStepEmpty = true,
            });
        }
        else return false;
        if (steps.Count == 0 || steps[0].ValidChoices.Count == 0) return false;
        BeginPendingActivationSequence(candidate.Controller, source, "trigger-declaration", steps, candidate.CandidateId);
        return true;
    }

    private static L12ActivationSelectionStep TriggerStep(string kind, string text, IEnumerable<string> choices,
        int min, int max = 1) => new()
    {
        Kind = kind, Text = text, ValidChoices = choices.ToList(), MinChoose = min, MaxChoose = max,
    };

    private void CompleteTriggerDeclaration(L12PendingActivation activation)
    {
        var candidate = State.PendingTriggerStackCandidates.FirstOrDefault(item => item.CandidateId == activation.TriggerCandidateId);
        if (candidate is null) { AdvanceTriggerBatches(); return; }
        if (TryCompletePublicTriggerDeclaration(candidate, activation)) return;
        var declared = activation.DeclaredTargets.ToList();
        if (candidate.SourceCardId == "S02-0516" && candidate.Trigger == "attack")
        {
            var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
            candidate.Data["declaredMode"] = mode ?? "mode:none";
            if (mode == "mode:use")
            {
                var player = State.Players[candidate.Controller];
                var source = FindOnField(player, candidate.SourceInstanceId, out _, out _);
                var costId = activation.DeclaredValues.GetValueOrDefault("hannibalCost", []).SingleOrDefault();
                var ownId = activation.DeclaredValues.GetValueOrDefault("hannibalOwn", []).SingleOrDefault();
                var enemyId = activation.DeclaredValues.GetValueOrDefault("hannibalEnemy", []).SingleOrDefault();
                var power = player.Morale.FirstOrDefault(card => card.InstanceId == costId && card.IsGodPower && !card.Tapped);
                var own = FindOnField(player, ownId, out _, out _);
                var enemy = DeclaredEnemyTarget(candidate.Controller, enemyId);
                if (source is null || power is null || own is null || enemy is null)
                {
                    State.PendingTriggerStackCandidates.Remove(candidate);
                    AddEvent("ability-rejected", candidate.Controller,
                        "汉尼拔的来源、费用对象或目标已失效；未消耗神力且效果未进入堆叠");
                    AdvanceTriggerBatches();
                    return;
                }
                power.Tapped = true;
                candidate.Data["hannibalOwn"] = own.InstanceId;
                candidate.Data["hannibalEnemy"] = enemy.InstanceId;
                AddEvent("cost", candidate.Controller, "汉尼拔消耗1神力", source);
            }
            declared.Clear();
        }
        else if (candidate.SourceCardId == "S01-0305" && declared.Count == 5)
        {
            var player = State.Players[candidate.Controller];
            var costIds = declared.Take(4).ToArray();
            var costs = costIds.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id
                    && card.InstanceId != candidate.SourceInstanceId && CanEnterHandOrLibrary(card)))
                .ToArray();
            var slot = declared[4];
            if (costIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4
                || costs.Any(card => card is null)
                || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
            {
                State.PendingTriggerStackCandidates.Remove(candidate);
                AddEvent("ability-rejected", candidate.Controller,
                    "勇士比约恩声明的墓地费用或登场位置已失效；未支付费用且效果未进入堆叠");
                AdvanceTriggerBatches();
                return;
            }
            DamageMaster(candidate.Controller, 1, "勇士比约恩阵亡效果");
            MoveGraveToLibraryBottom(player, costs.Cast<L12CardInstance>());
            candidate.Data["declaredGraveOrder"] = string.Join('|', costIds);
            candidate.Data["declaredSlot"] = slot;
            candidate.Data["bjornCostsPrepaid"] = "true";
            AddEvent("cost", candidate.Controller,
                "勇士比约恩对主宰造成1点伤害，并将墓地4张牌依声明顺序置于牌库底部作为阵亡效果费用");
            declared.Clear();
        }
        candidate.Data["declaredTargets"] = string.Join('|', declared);
        candidate.Data["declaration-complete"] = "true";
        AdvanceTriggerBatches();
    }

    private void RecalculateContinuousTroops()
    {
        foreach (var player in State.Players)
        {
            var trojanHorses = player.Field.SelectMany(row => row).Count(card => card?.CardId == "S02-0523");
            var globalModifier = -1000 * trojanHorses;
            for (var row = 0; row < player.Field.Length; row++)
            for (var slot = 0; slot < player.Field[row].Length; slot++)
            {
                var card = player.Field[row][slot];
                if (card is null || !IsFieldLegion(card)) continue;
                card.EffectiveProfession = L12StructuredCardRules.EffectiveProfession(card, row);
                card.ContinuousCostModifier = card.CardId == "S01-0212" && State.ActivePlayer != player.PlayerIndex ? 1 : 0;
                var bonus = GetTurnAndPositionContinuousTroops(player, card, row, slot);
                L12DerivedStats.ApplyContinuousModifiers(card, bonus, globalModifier, State.TurnSerial);
            }
        }
    }

    /// <summary>
    /// 统一计算只依赖当前回合、位置与公开场面状态的持续兵力修正。
    /// 这些效果不是“被进攻时”或“进攻时”触发，任何快照、目标校验和效果结算前都必须保持生效。
    /// </summary>
    private IReadOnlyDictionary<string, int> GetTurnAndPositionContinuousTroops(
        L12PlayerState owner, L12CardInstance card, int row, int slot)
    {
        var bonuses = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string key, int amount) => bonuses[key] = bonuses.GetValueOrDefault(key) + amount;
        foreach (var sword in card.AttachedCards.Where(attached => attached.CardId == "S02-06S2"))
            Add($"attached:{sword.InstanceId}:king-sword", 1000);
        var starterDisasterBonus = L12StructuredCardRules.StarterDisasterTroopsBonus(
            State.ActiveDisaster?.CardId, card);
        if (starterDisasterBonus != 0)
            Add($"disaster:{State.ActiveDisaster!.InstanceId}:disaster-legion", starterDisasterBonus);
        var isOpponentTurn = State.ActivePlayer != owner.PlayerIndex;
        if (card.CardId == "S01-0204")
            foreach (var guard in card.AttachedCards.Where(attached => attached.CardId == "S01-0212"))
                Add($"attached:{guard.InstanceId}:tomb-guard", 1000);

        if (isOpponentTurn)
        {
            if (row == 0 && card.CardId is "S01-0107" or "S01-0212" or "S01-0312" or "S02-0004" or "S02-0007" or "S02-0615")
                Add($"self:{card.InstanceId}:opponent-turn-front", 1000);
            if (card.CardId == "S02-0519") Add($"self:{card.InstanceId}:opponent-turn", 2000);
            if (card.CardId == "S01-0203"
                && !owner.Field.SelectMany(fieldRow => fieldRow).Any(fieldCard => fieldCard?.CardId == "S01-0212"))
                Add($"self:{card.InstanceId}:no-tomb-guard", 1000);
        }

        if (card.CardId == "S01-0212" && owner.MasterId == "S01-02D1")
            Add($"master:{owner.MasterId}:tomb-guard", 1000);

        // 汉尼拔给予同排左右相邻军团的静态兵力修正；多个来源可以叠加。
        foreach (var adjacentSlot in new[] { slot - 1, slot + 1 })
            if (adjacentSlot is >= 0 and < 3 && owner.Field[row][adjacentSlot] is { CardId: "S02-0516" } hannibal)
                Add($"adjacent:{hannibal.InstanceId}:hannibal", 1000);

        return bonuses;
    }
}
