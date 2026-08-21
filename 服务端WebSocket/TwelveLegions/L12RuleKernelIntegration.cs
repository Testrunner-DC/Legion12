namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private CommandResult BeginPendingActivation(int playerIndex, L12CardInstance source, string ability,
        IEnumerable<string> choices, string text, int min = 1, int max = 1)
        => BeginPendingActivationSequence(playerIndex, source, ability,
        [new L12ActivationSelectionStep { Kind = "active-target", Text = text, ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), MinChoose = min, MaxChoose = max }]);

    private CommandResult BeginPendingActivationSequence(int playerIndex, L12CardInstance source, string ability,
        IEnumerable<L12ActivationSelectionStep> selectionSteps)
    {
        var steps = selectionSteps.Select(step => new L12ActivationSelectionStep
        {
            Kind = step.Kind,
            Text = step.Text,
            ValidChoices = step.ValidChoices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = step.MinChoose,
            MaxChoose = Math.Min(step.MaxChoose, step.ValidChoices.Count),
            SkipWhenPreviousStepEmpty = step.SkipWhenPreviousStepEmpty,
        }).ToList();
        if (steps.Count == 0 || steps.Any(step => step.ValidChoices.Count < step.MinChoose)) return CommandResult.Reject("没有足够的合法目标");
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
        };
        State.PendingActivations.Add(activation);
        CreateActivationStepPrompt(activation);
        AddEvent("activation-declare", playerIndex, $"{State.Players[playerIndex].Name} 正在声明〈{source.Name}〉的目标", source);
        return CommandResult.Ok();
    }

    private void CreateActivationStepPrompt(L12PendingActivation activation)
    {
        var step = activation.SelectionSteps[activation.CurrentStep];
        var promptKind = step.Kind;
        if (step.Kind == "adjacent-slot")
        {
            var player = State.Players[activation.Controller];
            var row = -1;
            var slot = -1;
            var moving = activation.DeclaredTargets.Count == 0
                ? null
                : FindOnField(player, activation.DeclaredTargets[0], out row, out slot);
            List<string> choices = moving is null ? [] : AdjacentEmptySlots(player, row, slot).ToList();
            step.ValidChoices.Clear();
            step.ValidChoices.AddRange(choices);
            if (step.ValidChoices.Count < step.MinChoose)
            {
                State.PendingActivations.Remove(activation);
                ClearFreeMasterActivation(activation);
                AddEvent("ability-rejected", activation.Controller, "所选军团没有可位移的相邻空位，效果未支付费用也未入栈");
                return;
            }
            promptKind = "slot";
        }
        CreatePrompt(activation.Controller, promptKind, step.Text, step.ValidChoices, step.MinChoose,
            Math.Min(step.MaxChoose, step.ValidChoices.Count),
            "pending-activation", isPrivate: true,
            data: new Dictionary<string, string>
            {
                ["activationId"] = activation.ActivationId,
                ["activationStep"] = activation.CurrentStep.ToString(),
            });
    }

    private IEnumerable<string> AdjacentEmptySlots(L12PlayerState player, int row, int slot)
        => new[] { (row - 1, slot), (row + 1, slot), (row, slot - 1), (row, slot + 1) }
            .Where(position => position.Item1 is >= 0 and < 2 && position.Item2 is >= 0 and < 3
                && !(State.ActiveDisaster?.CardId == "S01-DS03" && position.Item1 == 1)
                && player.Field[position.Item1][position.Item2] is null)
            .Select(position => $"{position.Item1}:{position.Item2}");

    private void ResolvePendingActivation(L12Prompt prompt, List<string> chosen)
    {
        if (!prompt.Data.TryGetValue("activationId", out var activationId)) return;
        var activation = State.PendingActivations.FirstOrDefault(item => item.ActivationId == activationId);
        if (activation is null) return;
        var step = activation.SelectionSteps[activation.CurrentStep];
        if (chosen.Count < step.MinChoose || chosen.Count > step.MaxChoose
            || chosen.Any(id => !step.ValidChoices.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            State.PendingActivations.Remove(activation);
            ClearFreeMasterActivation(activation);
            AddEvent("ability-rejected", prompt.PlayerIndex, "目标声明已失效，效果未支付费用也未入栈");
            return;
        }
        activation.DeclaredTargets.AddRange(chosen);
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
        State.PendingActivations.Remove(activation);

        var player = State.Players[prompt.PlayerIndex];
        var source = FindOnField(player, activation.SourceInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == activation.SourceInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId)
            ?? (activation.SourceCardId == player.MasterId ? CreateActiveMasterSource(player, activation.SourceInstanceId) : null)
            ?? (activation.SourceInstanceId == $"faction-{prompt.PlayerIndex}" ? CreateCard(activation.SourceCardId, activation.SourceInstanceId) : null);
        if (source is null || activation.DeclaredTargets.Any(id => !IsDeclaredChoiceStillLegal(prompt.PlayerIndex, id)))
        {
            ClearFreeMasterActivation(activation);
            AddEvent("ability-rejected", prompt.PlayerIndex, "来源或目标已不合法，效果未支付费用也未入栈");
            return;
        }
        var result = CommitActiveAbility(prompt.PlayerIndex, source, activation.Ability,
            activation.DeclaredTargets.Count == 0 ? null : string.Join('|', activation.DeclaredTargets));
        if (!result.Accepted) AddEvent("ability-rejected", prompt.PlayerIndex, result.Error ?? "主动效果发动失败");
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

    private bool IsDeclaredChoiceStillLegal(int controller, string choice)
    {
        if (choice is "yes" or "no" or "skip" or "top" or "bottom") return true;
        if (choice.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)) return true;
        // PendingActivation 也用于士气/神力等真实资源的预声明。士气不是
        // L12CardInstance，不能仅依赖 FindPromptCard 校验，否则合法选择会在支付前被误判失效。
        if (State.Players[controller].Morale.Any(card => card.InstanceId == choice)) return true;
        if (GetAbilities(State.Players[controller].MasterId).Any(view => view.Id.Equals(choice, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (choice.Split(':') is [var rowText, var slotText]
            && int.TryParse(rowText, out var row) && int.TryParse(slotText, out var slot)
            && row is >= 0 and < 2 && slot is >= 0 and < 3)
            return State.Players[controller].Field[row][slot] is null;
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
        if (target is not null) target.Troops += delta;
    }

    private void ResolveDeclaredPalaceExchange(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var target = DeclaredEnemyTarget(item.Controller, item.Data.GetValueOrDefault("target"));
        if (target is null) { FinishStackItem(item); return; }
        var paid = int.TryParse(item.Data.GetValueOrDefault("paid"), out var parsed) ? parsed : target.CurrentCost;
        KillTarget(target.InstanceId, "被凌霄宝殿击杀");
        var choices = player.Graveyard.Where(card => card.CardType == "legion"
            && L12StructuredCardRules.HasFaction(player, card, "tianting") && card.CurrentCost <= paid)
            .Select(card => card.InstanceId).ToList();
        choices.Add("skip");
        CreatePrompt(item.Controller, "optional-card", "选择墓地1张费用不高于返还士气数量的【天廷】军团活跃登场",
            choices, 1, 1, "card-effect", item.StackItemId,
            data: new Dictionary<string, string> { ["action"] = "palace-revive" });
    }

    private void QueueSimultaneousDeathTriggers(IEnumerable<(int Controller, L12CardInstance Card)> deaths)
    {
        var materializedDeaths = deaths.ToArray();
        var candidates = materializedDeaths.SelectMany(entry =>
        {
            var sameTime = BuildS1LeaveReactionCandidates(entry.Controller, entry.Card).ToList();
            if (HasDeathTrigger(entry.Card))
                sameTime.Add(CreateTriggerCandidate(entry.Controller, entry.Card, "death", "【阵亡时】效果",
                    new Dictionary<string, string> { ["cause"] = "combat" }));
            return sameTime;
        }).ToList();
        foreach (var defeatedController in materializedDeaths.Select(entry => entry.Controller).Distinct())
        {
            var morrigan = BuildMorriganEnemyDeathCandidate(defeatedController);
            if (morrigan is not null) candidates.Add(morrigan);
        }
        foreach (var entry in materializedDeaths)
        {
            var nephthys = BuildNephthysOwnDeathCandidate(entry.Controller, entry.Card);
            if (nephthys is not null) candidates.Add(nephthys);
            var artemis = BuildArtemisRangedDeathCandidate(entry.Controller, entry.Card);
            if (artemis is not null) candidates.Add(artemis);
        }
        QueueTriggerCandidates(candidates);
    }

    private void QueueTriggerCandidates(IEnumerable<L12TriggerCandidate> candidates)
    {
        var materialized = candidates.ToArray();
        if (materialized.Length == 0) return;

        foreach (var planned in L12TriggerBatchPlanner.Plan(materialized, State.ActivePlayer))
        {
            State.PendingTriggerBatches.Add(new L12TriggerBatch
            {
                BatchId = $"batch-{++State.TriggerBatchSequence}",
                Controller = planned[0].Controller,
                Candidates = planned.ToList(),
            });
        }
        if (!State.IsResolvingStack) AdvanceTriggerBatches();
    }

    private L12TriggerCandidate CreateTriggerCandidate(int controller, L12CardInstance card, string trigger, string text,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var candidate = new L12TriggerCandidate
        {
            CandidateId = $"trigger-{++State.TriggerBatchSequence}", Controller = controller,
            SourceInstanceId = card.InstanceId, SourceCardId = card.CardId, SourceName = card.Name,
            Trigger = trigger, Text = text,
        };
        if (data is not null)
            foreach (var pair in data) candidate.Data[pair.Key] = pair.Value;
        return candidate;
    }

    private bool HasDeathTrigger(L12CardInstance card)
        => card.SuppressDeathUntilTurn < State.TurnSerial && (card.CardId is "S01-0102" or "S01-0108" or "S01-0417"
            || S1ExtendedDeathCards.Contains(card.CardId) || IsS1FactionDeathCard(card.CardId)
            || IsS2FactionDeathCard(card.CardId));

    private void AdvanceTriggerBatches()
    {
        if (State.PendingPrompts.Any(prompt => prompt.Continuation == "trigger-batch-order")) return;
        while (State.PendingTriggerBatches.Count > 0)
        {
            var batch = State.PendingTriggerBatches[0];
            State.PendingTriggerBatches.RemoveAt(0);
            if (batch.Candidates.Count == 1)
            {
                AddTriggerCandidateToStack(batch.Candidates[0]);
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
    }

    private void ResolveTriggerBatchOrder(L12Prompt prompt, List<string> chosen)
    {
        var batch = State.PendingTriggerBatches.FirstOrDefault(item => item.BatchId == prompt.Data.GetValueOrDefault("batchId"));
        if (batch is null) return;
        State.PendingTriggerBatches.Remove(batch);
        var byId = batch.Candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase);
        // 玩家选择的是结算顺序；堆叠后进先出，因此反向压栈。
        foreach (var id in chosen.AsEnumerable().Reverse()) AddTriggerCandidateToStack(byId[id]);
        AddEvent("trigger-order", batch.Controller, $"{State.Players[batch.Controller].Name} 已排列同一时点的 {chosen.Count} 个触发效果");
        AdvanceTriggerBatches();
    }

    private void AddTriggerCandidateToStack(L12TriggerCandidate candidate)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = candidate.Controller,
            SourceInstanceId = candidate.SourceInstanceId,
            SourceCardId = candidate.SourceCardId,
            SourceName = candidate.SourceName,
            Trigger = candidate.Trigger,
            Text = candidate.Text,
        };
        foreach (var pair in candidate.Data) item.Data[pair.Key] = pair.Value;
        if (State.IsResolvingStack) State.DeferredEffectStack.Add(item);
        else State.EffectStack.Add(item);
        var source = FindSource(item);
        AddEvent("stack-push", candidate.Controller, $"〈{candidate.SourceName}〉的{candidate.Text}进入同一时点触发批次",
            source is null ? [] : [source]);
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
                var modifier = globalModifier + GetTurnAndPositionContinuousTroops(player, card, row, slot);
                L12DerivedStats.ApplyContinuousModifier(card, modifier, State.TurnSerial);
            }
        }
    }

    /// <summary>
    /// 统一计算只依赖当前回合、位置与公开场面状态的持续兵力修正。
    /// 这些效果不是“被进攻时”或“进攻时”触发，任何快照、目标校验和效果结算前都必须保持生效。
    /// </summary>
    private int GetTurnAndPositionContinuousTroops(L12PlayerState owner, L12CardInstance card, int row, int slot)
    {
        var attachedSword = card.AttachedCards.Any(attached => attached.CardId == "S02-06S2") ? 1000 : 0;
        var isOpponentTurn = State.ActivePlayer != owner.PlayerIndex;
        var modifier = attachedSword;
        if (card.CardId == "S01-0204")
            modifier += card.AttachedCards.Count(attached => attached.CardId == "S01-0212") * 1000;

        if (isOpponentTurn)
        {
            if (row == 0 && card.CardId is "S01-0107" or "S01-0212" or "S01-0312" or "S02-0004" or "S02-0007" or "S02-0615")
                modifier += 1000;
            if (card.CardId == "S02-0519") modifier += 2000;
            if (card.CardId == "S01-0203"
                && !owner.Field.SelectMany(fieldRow => fieldRow).Any(fieldCard => fieldCard?.CardId == "S01-0212"))
                modifier += 1000;
        }

        if (card.CardId == "S01-0212" && owner.MasterId == "S01-02D1") modifier += 1000;

        // 汉尼拔给予同排左右相邻军团的静态兵力修正；多个来源可以叠加。
        foreach (var adjacentSlot in new[] { slot - 1, slot + 1 })
            if (adjacentSlot is >= 0 and < 3 && owner.Field[row][adjacentSlot]?.CardId == "S02-0516")
                modifier += 1000;

        return modifier;
    }
}
