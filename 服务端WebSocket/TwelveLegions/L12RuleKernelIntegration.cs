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
    {
        var steps = selectionSteps.Select(step => new L12ActivationSelectionStep
        {
            Kind = step.Kind,
            Text = step.Text,
            ValidChoices = step.ValidChoices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = step.MinChoose,
            MaxChoose = Math.Min(step.MaxChoose, step.ValidChoices.Count),
            ChoiceLabels = new Dictionary<string, string>(step.ChoiceLabels, StringComparer.OrdinalIgnoreCase),
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
            TriggerCandidateId = triggerCandidateId,
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
                RejectPendingActivation(activation, "所选军团没有可位移的相邻空位，效果未支付费用也未入栈");
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
        var promptChoices = step.ValidChoices.Append("skip").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var promptData = new Dictionary<string, string>(step.ChoiceLabels, StringComparer.OrdinalIgnoreCase)
        {
            ["activationId"] = activation.ActivationId,
            ["activationStep"] = activation.CurrentStep.ToString(),
        };
        CreatePrompt(activation.Controller, promptKind, step.Text, promptChoices, step.MinChoose,
            Math.Min(step.MaxChoose, step.ValidChoices.Count),
            "pending-activation", isPrivate: true,
            data: promptData);
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
        if (chosen.Count == 1 && chosen[0] == "skip")
        {
            State.PendingActivations.Remove(activation);
            ClearFreeMasterActivation(activation);
            AddEvent("ability-cancelled", prompt.PlayerIndex, "已取消发动，未支付费用且未进入堆叠");
            if (activation.TriggerCandidateId is not null)
            {
                State.PendingTriggerStackCandidates.RemoveAll(candidate => candidate.CandidateId == activation.TriggerCandidateId);
                AdvanceTriggerBatches();
            }
            return;
        }
        var step = activation.SelectionSteps[activation.CurrentStep];
        if (chosen.Count < step.MinChoose || chosen.Count > step.MaxChoose
            || chosen.Any(id => !step.ValidChoices.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            RejectPendingActivation(activation, "目标声明已失效，效果未支付费用也未入栈");
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

        if (activation.TriggerCandidateId is not null)
        {
            CompleteTriggerDeclaration(activation);
            return;
        }

        var player = State.Players[prompt.PlayerIndex];
        var source = FindOnField(player, activation.SourceInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == activation.SourceInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId)
            ?? player.Graveyard.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId
                && IsLegalGraveyardActiveAbilitySource(player, card, activation.Ability))
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

    private void RejectPendingActivation(L12PendingActivation activation, string reason)
    {
        State.PendingActivations.Remove(activation);
        ClearFreeMasterActivation(activation);
        AddEvent("ability-rejected", activation.Controller, reason);
        if (activation.TriggerCandidateId is null) return;
        State.PendingTriggerStackCandidates.RemoveAll(candidate => candidate.CandidateId == activation.TriggerCandidateId);
        AdvanceTriggerBatches();
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
        if (choice.StartsWith("rune:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(choice.AsSpan("rune:".Length), out var runeIndex))
            return runeIndex >= 1 && runeIndex <= State.Players[controller].SpecialZones.Runes;
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
        if (!State.IsResolvingStack && State.EffectStack.Count == 0 && State.ResponseWindow is null)
            AdvanceTriggerBatches();
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
        candidate.Data.TryAdd("triggerEffectText", ResolveTriggeredEffectDisplayText(card, trigger, text, candidate.Data));
        return candidate;
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
            "tsukuyomiReadyMorale" => ["前排位移至后排时"],
            "tsukuyomiFollowMove" => ["军团位移时"],
            "margaretMasterDamage" => ["主宰因效果受到伤害时"],
            _ => trigger switch
            {
                "promotion-enter" => ["晋升登场"],
                "enter" => ["登场时"],
                "attack" => ["进攻时"],
                "after-damage" => ["对主宰造成伤害时", "主宰受到伤害时"],
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
        var markerMatch = lines.FirstOrDefault(line => markers.Length > 0
            && (ability is null ? markers.Any(marker => NormalizeTriggeredEffectText(line).Contains(
                    NormalizeTriggeredEffectText(marker), StringComparison.Ordinal))
                : markers.All(marker => NormalizeTriggeredEffectText(line).Contains(
                    NormalizeTriggeredEffectText(marker), StringComparison.Ordinal))));
        if (!string.IsNullOrWhiteSpace(markerMatch)) return markerMatch;

        var normalizedFallback = NormalizeTriggeredEffectText(fallback);
        var fallbackMatch = lines.FirstOrDefault(line => normalizedFallback.Length >= 2
            && NormalizeTriggeredEffectText(line).Contains(normalizedFallback, StringComparison.Ordinal));
        return fallbackMatch ?? fallback;
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
            _ => false,
        };

    private bool HasDeathTrigger(L12CardInstance card)
        => card.SuppressDeathUntilTurn < State.TurnSerial && (card.CardId is "S01-0102" or "S01-0108" or "S01-0417"
            || S1ExtendedDeathCards.Contains(card.CardId) || IsS1FactionDeathCard(card.CardId)
            || IsS2FactionDeathCard(card.CardId));

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
            if (!candidate.Data.ContainsKey("declaration-complete") && TryBeginTriggerDeclaration(candidate)) return;
            State.PendingTriggerStackCandidates.RemoveAt(0);
            AddTriggerCandidateToStack(candidate);
        }
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
        var source = FindSource(item) ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        AddEvent("effect-trigger", candidate.Controller,
            candidate.Data.GetValueOrDefault("triggerEffectText", candidate.Text),
            source);
        AddEvent("stack-push", candidate.Controller, $"〈{candidate.SourceName}〉的{candidate.Text}进入同一时点触发批次",
            source);
    }

    private bool TryBeginTriggerDeclaration(L12TriggerCandidate candidate)
    {
        if (candidate.Trigger != "death") return false;
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var source = FindPromptCard(candidate.Controller, candidate.SourceInstanceId)
            ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        var steps = new List<L12ActivationSelectionStep>();
        if (candidate.SourceCardId == "S01-0108" && State.ActivePlayer != candidate.Controller)
        {
            steps.Add(TriggerStep("target-morale", "花木兰：选择对方1张休整士气，使其下个重置阶段无法转为活跃",
                opponent.Morale.Where(card => card.Tapped).Select(card => card.InstanceId), 1));
        }
        else if (candidate.SourceCardId == "S01-0112" && State.DisasterValue <= 4)
        {
            steps.Add(TriggerStep("grave-card", "孙武：可选择墓地1张费用不高于4的战术卡回到手牌",
                player.Graveyard.Where(card => card.CardType == "tactic" && card.CurrentCost <= 4).Select(card => card.InstanceId), 0));
        }
        else if (candidate.SourceCardId == "S01-0115" && CanReturnMorale(player, 1))
        {
            steps.Add(TriggerStep("field-legion", "荆轲：可选择对方最多1张兵力不高于2000的军团",
                PublicLegions(opponent).Where(card => card.Troops <= 2000).Select(card => card.InstanceId), 0));
            if (NeedsManualReturnMoraleSelection(player, 1))
                steps.Add(new L12ActivationSelectionStep
                {
                    Kind = "resource-return", Text = "请选择返还的士气",
                    ValidChoices = player.Morale.Select(card => card.InstanceId).ToList(), MinChoose = 1, MaxChoose = 1,
                    SkipWhenPreviousStepEmpty = true,
                });
        }
        else if (candidate.SourceCardId == "S01-0403")
        {
            steps.Add(TriggerStep("hand-card", "上杉谦信：选择手牌中最多2张反击战术置入后排",
                player.Hand.Where(card => IsCounterTactic(card.CardId)).Select(card => card.InstanceId), 0, 2));
        }
        else if (candidate.SourceCardId == "S01-0407")
        {
            steps.Add(TriggerStep("hand-card", "坂本龙马：可选择手牌1张费用不高于3的【高天原】军团休整登场",
                player.Hand.Where(card => card.CardType == "legion" && card.Faction == "gaotianyuan" && card.CurrentCost <= 3)
                    .Select(card => card.InstanceId), 0));
            steps.Add(new L12ActivationSelectionStep
            {
                Kind = "slot", Text = "选择该军团休整登场的位置", ValidChoices = EmptySlots(player).ToList(),
                MinChoose = 1, MaxChoose = 1, SkipWhenPreviousStepEmpty = true,
            });
        }
        else if (candidate.SourceCardId == "S01-0207")
        {
            steps.Add(TriggerStep("grave-card", "图坦卡蒙：可将墓地1张费用不高于4的其他【太阳城】卡牌放回牌库顶部",
                player.Graveyard.Where(card => CanEnterHandOrLibrary(card) && card.CardId != "S01-0207"
                    && card.Faction == "taiyangcheng" && card.CurrentCost <= 4).Select(card => card.InstanceId), 0));
        }
        else if (candidate.SourceCardId == "S01-0204")
        {
            var attachedIds = source.LastKnownAttachedCardIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var guards = player.Graveyard
                .Where(card => card.CardId == "S01-0212" && attachedIds.Contains(card.InstanceId))
                .Take(EmptySlots(player).Count()).Select(card => card.InstanceId).ToList();
            candidate.Data["declaredCardIds"] = string.Join('|', guards);
            for (var index = 0; index < guards.Count; index++)
                steps.Add(TriggerStep("unused-slot", $"陵墓构造体：选择第{index + 1}张〈陵墓守卫〉休整登场的位置",
                    EmptySlots(player), 1));
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
        else if (candidate.SourceCardId == "S01-0210")
        {
            steps.Add(TriggerStep("grave-card", "尼托克丽丝：选择墓地1张费用不高于2的【太阳城】军团活跃登场",
                player.Graveyard.Where(card => card.CardType == "legion" && card.Faction == "taiyangcheng" && card.CurrentCost <= 2)
                    .Select(card => card.InstanceId), 0));
            steps.Add(new L12ActivationSelectionStep
            {
                Kind = "slot", Text = "选择该军团活跃登场的位置", ValidChoices = EmptySlots(player).ToList(),
                MinChoose = 1, MaxChoose = 1, SkipWhenPreviousStepEmpty = true,
            });
        }
        else if (candidate.SourceCardId == "S01-0304")
        {
            steps.Add(TriggerStep("field-legion", "无情者哈拉尔：选择对方1张兵力不高于2000的军团并击杀",
                PublicLegions(opponent).Where(card => card.Troops <= 2000).Select(card => card.InstanceId), 1));
        }
        else if (candidate.SourceCardId == "S01-0305")
        {
            var graveCards = player.Graveyard.Where(card => card.InstanceId != candidate.SourceInstanceId)
                .Select(card => card.InstanceId).ToList();
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
        else if (candidate.SourceCardId == "S01-0313")
        {
            steps.Add(TriggerStep("field-legion", "神箭奥德尔：可选择对方1张活跃军团转为休整",
                PublicLegions(opponent).Where(card => !card.Tapped).Select(card => card.InstanceId), 0));
        }
        else if (candidate.SourceCardId == "S02-0518")
        {
            steps.Add(TriggerStep("grave-card", "忒修斯：可选择墓地1张【晋升者】军团展示并加入手牌",
                player.Graveyard.Where(card => card.CardType == "legion" && card.HasTrait("晋升者"))
                    .Select(card => card.InstanceId), 0));
        }
        else if (candidate.SourceCardId == "S02-0601")
        {
            steps.Add(TriggerStep("hand-card", "亚瑟王：可选择手牌1张费用不高于4的【圆桌骑士】军团活跃登场",
                player.Hand.Where(card => card.CardType == "legion" && card.HasTrait("圆桌骑士") && card.CurrentCost <= 4)
                    .Select(card => card.InstanceId), 0));
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
        var declared = activation.DeclaredTargets.ToList();
        if (candidate.SourceCardId == "S01-0115" && declared.Count > 0)
        {
            var moraleId = declared.FirstOrDefault(id => State.Players[candidate.Controller].Morale.Any(card => card.InstanceId == id));
            if (moraleId is not null)
            {
                if (!ReturnSelectedMoraleById(State.Players[candidate.Controller], [moraleId], 1))
                {
                    State.PendingTriggerStackCandidates.Remove(candidate);
                    AddEvent("ability-rejected", candidate.Controller, "返还士气已失效，效果未进入堆叠");
                    AdvanceTriggerBatches();
                    return;
                }
                declared.Remove(moraleId);
            }
            else if (!ReturnMorale(State.Players[candidate.Controller], 1))
            {
                State.PendingTriggerStackCandidates.Remove(candidate);
                AddEvent("ability-rejected", candidate.Controller, "返还士气失败，效果未进入堆叠");
                AdvanceTriggerBatches();
                return;
            }
            candidate.Data["return-morale-prepaid"] = "true";
        }
        else if (candidate.SourceCardId == "S01-0305" && declared.Count == 5)
        {
            DamageMaster(candidate.Controller, 1, "勇士比约恩阵亡效果");
            candidate.Data["declaredGraveOrder"] = string.Join('|', declared.Take(4));
            candidate.Data["declaredSlot"] = declared[4];
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
