namespace TwelveLegions.Server;

/// <summary>
/// 试炼完成事件的公共候选、公开声明与独立效果段。
/// completeTrial 只改变试炼状态；卡面完成效果与主宰监听均从同一 TriggerBatch 开始。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string TrialLakeLady = "S02-06S3";
    private const string TrialGrailJourney = "S02-06S4";
    private const string TrialFenianLegend = "S02-06S5";
    private const string TrialAngusMaster = "S02-06M2";
    private static bool HasTrialCompletionTriggerDeclarationPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data)
        => trigger == "trial-complete"
            && (cardId is TrialLakeLady or TrialGrailJourney or TrialFenianLegend
                || L12StructuredCardRules.StarterRemainingPlan(cardId, trigger) == "sky-city-completion"
                || cardId == TrialAngusMaster
                    && data?.GetValueOrDefault("trialCompletionPlan") == "angus-rune");

    private void QueueCompletedTrialTriggerBatch(int controller, L12CardInstance trial)
    {
        var candidates = new List<L12TriggerCandidate>();
        var printedPlan = trial.CardId switch
        {
            TrialLakeLady => "lake-lady",
            TrialGrailJourney => "grail-journey",
            TrialFenianLegend => "fenian-legend",
            _ => null,
        };
        if (printedPlan is null
            && L12StructuredCardRules.StarterRemainingPlan(trial.CardId, "trial-complete") == "sky-city-completion")
            printedPlan = "sky-city";
        if (printedPlan is not null)
        {
            var triggerText = ResolveTriggeredEffectDisplayText(trial, "trial-complete", "触发");
            candidates.Add(CreateTriggerCandidate(controller, trial, "trial-complete", "试炼完成触发效果",
                new Dictionary<string, string>
                {
                    ["trialCompletionPlan"] = printedPlan,
                    ["triggerEffectText"] = triggerText,
                }, trial));
        }

        if (State.Players[controller].MasterId == TrialAngusMaster)
        {
            var angus = CreateCard(TrialAngusMaster, $"master-{controller}");
            candidates.Add(CreateTriggerCandidate(controller, angus, "trial-complete",
                "安格斯·麦·奥格：完成试炼时可获得1符文",
                new Dictionary<string, string>
                {
                    ["trialCompletionPlan"] = "angus-rune",
                    ["triggerEffectText"] = "每完成1次试炼，可获得1符文。",
                    ["completedTrial"] = trial.InstanceId,
                }, angus));
        }

        QueueTriggerCandidates(candidates);
    }

    private bool TryBeginTrialCompletionTriggerDeclaration(L12TriggerCandidate candidate,
        L12CardInstance source)
    {
        if (candidate.Trigger != "trial-complete") return false;
        var plan = candidate.Data.GetValueOrDefault("trialCompletionPlan");
        var player = State.Players[candidate.Controller];
        var enemyTargets = PublicLegions(State.Players[1 - candidate.Controller])
            .Select(card => card.InstanceId).ToList();
        var steps = new List<L12ActivationSelectionStep>();

        switch (plan)
        {
            case "lake-lady":
            {
                var modes = new List<string> { "mode:none" };
                var graveArthurs = player.Graveyard.Where(card => card.CardId == "S02-0601")
                    .Select(card => card.InstanceId).ToList();
                if (graveArthurs.Count > 0) modes.Add("mode:grave");
                if (player.Library.Any(card => card.CardId == "S02-0601")) modes.Add("mode:library");
                steps.Add(TrialCompletionStep("option", "mode",
                    "湖中仙女的馈赠：预先声明是否从牌库或墓地将1张〈亚瑟王〉加入手牌",
                    modes, allowCancel: false, labels: new()
                    {
                        ["mode:none"] = "不从牌库或墓地将〈亚瑟王〉加入手牌",
                        ["mode:grave"] = "从墓地选择1张〈亚瑟王〉加入手牌",
                        ["mode:library"] = "查看牌库，选择1张〈亚瑟王〉加入手牌",
                    }));
                steps.Add(TrialCompletionStep("grave-card", "graveArthur",
                    "湖中仙女的馈赠：预先选择墓地1张〈亚瑟王〉加入手牌",
                    graveArthurs, requiredChoice: "mode:grave", allowCancel: false));
                break;
            }
            case "grail-journey":
            {
                // 牌库中是否存在命中牌属于隐藏信息；声明阶段只可依据公开的牌库数量决定是否可查看。
                var canUse = player.Library.Count > 0;
                steps.Add(TrialCompletionStep("option", "mode",
                    "寻找圣杯之旅：预先声明是否查看牌库并检索1张【彼界】军团",
                    canUse ? ["mode:none", "mode:use"] : ["mode:none"]));
                break;
            }
            case "fenian-legend":
            {
                var maximum = enemyTargets.Count == 0 ? 0 : player.SpecialZones.Runes;
                steps.Add(TrialCompletionStep("option", "mode",
                    "芬尼亚传奇：预先声明是否消耗X符文选择对方军团",
                    maximum > 0 ? ["mode:none", "mode:use"] : ["mode:none"]));
                if (maximum > 0)
                {
                    steps.Add(TrialCompletionStep("option", "runeCount",
                        "芬尼亚传奇：预先声明消耗的符文数量X",
                        Enumerable.Range(1, maximum).Select(value => $"rune-count:{value}"),
                        requiredChoice: "mode:use",
                        labels: Enumerable.Range(1, maximum).ToDictionary(value => $"rune-count:{value}",
                            value => $"消耗{value}符文")));
                    for (var index = 1; index <= maximum; index++)
                    {
                        steps.Add(TrialCompletionStep("enemy-legion", $"target{index}",
                            $"芬尼亚传奇：预先选择第{index}个兵力-3000的公开军团目标（可重复）",
                            enemyTargets, requiredChoice: "mode:use", referenceKey: "runeCount",
                            minimumReferenceNumericValue: index, referenceNumericChoicePrefix: "rune-count:"));
                    }
                }
                break;
            }
            case "angus-rune":
                steps.Add(TrialCompletionStep("option", "mode",
                    "安格斯·麦·奥格：预先声明是否获得1符文", ["mode:none", "mode:use"]));
                break;
            default:
                return false;
        }

        var result = BeginPendingActivationSequence(candidate.Controller, source,
            "trial-completion-trigger-declaration", steps, candidate.CandidateId);
        if (result.Accepted) return true;
        RemoveUnstackedTriggerCandidate(candidate, result.Error ?? "试炼完成触发声明已失效，效果未入栈");
        return true;
    }

    private static L12ActivationSelectionStep TrialCompletionStep(string kind, string key, string text,
        IEnumerable<string> choices, string? requiredChoice = null, string? referenceKey = null,
        int minimumReferenceNumericValue = 0, string? referenceNumericChoicePrefix = null,
        bool allowCancel = true, Dictionary<string, string>? labels = null)
        => new()
        {
            Kind = kind,
            DeclarationKey = key,
            Text = text,
            ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CancellationPolicy = allowCancel
                ? L12ActivationCancellationPolicy.WhenNoExplicitDecline
                : L12ActivationCancellationPolicy.NotAllowed,
            RequiredDeclaredChoice = requiredChoice,
            ReferenceDeclarationKey = referenceKey,
            MinimumReferenceNumericValue = minimumReferenceNumericValue,
            ReferenceNumericChoicePrefix = referenceNumericChoicePrefix,
            ChoiceLabels = labels ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = "不发动",
                ["mode:use"] = "发动",
            },
        };

    private bool TryCompleteTrialCompletionTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        if (!HasTrialCompletionTriggerDeclarationPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data))
            return false;

        var player = State.Players[candidate.Controller];
        var plan = candidate.Data.GetValueOrDefault("trialCompletionPlan");
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();

        if (plan is "grail-journey" or "fenian-legend" or "angus-rune" && mode == "mode:none")
        {
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                $"〈{candidate.SourceName}〉的可选试炼完成触发未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        if (plan == "lake-lady" && mode == "mode:grave")
        {
            var graveArthur = activation.DeclaredValues.GetValueOrDefault("graveArthur", []).SingleOrDefault();
            if (graveArthur is null || !player.Graveyard.Any(card =>
                    card.InstanceId == graveArthur && card.CardId == "S02-0601"))
            {
                activation.DeclaredValues["mode"] = ["mode:none"];
                activation.DeclaredValues.Remove("graveArthur");
                AddEvent("effect-cancelled", candidate.Controller,
                    "湖中仙女的馈赠选择的墓地亚瑟王已失效；不会加入手牌，其余效果仍会结算");
            }
        }
        else if (plan == "fenian-legend")
        {
            var countChoice = activation.DeclaredValues.GetValueOrDefault("runeCount", []).SingleOrDefault();
            var count = countChoice?.Split(':') is ["rune-count", var countText]
                && int.TryParse(countText, out var parsed) ? parsed : 0;
            var targets = Enumerable.Range(1, Math.Max(0, count))
                .Select(index => activation.DeclaredValues.GetValueOrDefault($"target{index}", []).SingleOrDefault())
                .ToArray();
            if (count <= 0 || targets.Any(target => target is null
                    || DeclaredEnemyTarget(candidate.Controller, target) is null)
                || player.SpecialZones.Runes < count || !L12S2ZoneOps.SpendRunes(player, count))
            {
                RemoveUnstackedTriggerCandidate(candidate,
                    "芬尼亚传奇声明的符文数量或公开目标已失效；未支付符文且效果未入栈");
                return true;
            }
            candidate.Data["fenianTargets"] = string.Join('|', targets!);
            candidate.Data["trialSegment"] = "0";
            AddEvent("cost", candidate.Controller, $"芬尼亚传奇入栈前消耗{count}符文", candidate.SourceSnapshot ??
                CreateCard(candidate.SourceCardId, candidate.SourceInstanceId));
        }

        if (plan == "lake-lady")
        {
            var finalMode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
            var skipOptionalSearch = finalMode == "mode:none";
            candidate.Data["trialSegment"] = skipOptionalSearch ? "1" : "0";
            candidate.Data["stackText"] = skipOptionalSearch
                ? "湖中仙女的馈赠：墓地所有亚瑟王返回牌库并重洗"
                : "湖中仙女的馈赠：从牌库或墓地将1张亚瑟王加入手牌";
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        candidate.Data.TryAdd("trialSegment", "0");
        candidate.Data["declaration-complete"] = "true";
        AdvanceTriggerBatches();
        return true;
    }

    private void ResolveTrialCompletionTriggerEffect(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var source = FindSource(item) ?? item.SourceSnapshot
            ?? CreateCard(item.SourceCardId, item.SourceInstanceId);
        var plan = item.Data.GetValueOrDefault("trialCompletionPlan");
        var segment = int.TryParse(item.Data.GetValueOrDefault("trialSegment"), out var parsed) ? parsed : 0;

        if (plan == "angus-rune")
        {
            L12S2ZoneOps.GainRunes(player, 1);
            AddEvent("runes", item.Controller, "完成试炼，安格斯·麦·奥格获得1枚符文", source);
            FinishStackItem(item);
            return;
        }

        if (plan == "lake-lady")
        {
            if (segment == 0)
            {
                var mode = TrialCompletionDeclared(item, "mode").SingleOrDefault();
                if (mode == "mode:grave")
                {
                    var targetId = TrialCompletionDeclared(item, "graveArthur").SingleOrDefault();
                    var arthur = player.Graveyard.FirstOrDefault(card =>
                        card.InstanceId == targetId && card.CardId == "S02-0601");
                    if (arthur is null)
                        AddEvent("effect-cancelled", item.Controller,
                            "湖中仙女的馈赠选择的墓地亚瑟王已失效；不会加入手牌，其余效果仍会继续", source);
                    else
                    {
                        player.Graveyard.Remove(arthur);
                        AddCardToHandByEffect(player, arthur, "graveyard", "湖中仙女的馈赠将亚瑟王加入手牌");
                    }
                    FinishStackItem(item);
                    return;
                }
                if (mode == "mode:library")
                {
                    var choices = player.Library.Where(card => card.CardId == "S02-0601")
                        .Select(card => card.InstanceId).Append("skip").ToList();
                    if (choices.Count > 1)
                    {
                        CreatePrompt(item.Controller, "optional-card",
                            "湖中仙女的馈赠：查看牌库并选择1张〈亚瑟王〉加入手牌",
                            choices, 1, 1, "card-effect", item.StackItemId, isPrivate: true,
                            data: new Dictionary<string, string>
                            {
                                ["action"] = "trial-completion-library-arthur",
                            });
                        return;
                    }
                }
                FinishStackItem(item);
                return;
            }
            if (segment == 1)
            {
                foreach (var arthur in player.Graveyard.Where(card => card.CardId == "S02-0601").ToArray())
                {
                    player.Graveyard.Remove(arthur);
                    player.Library.Add(arthur);
                }
                ShuffleLibrary(player, "湖中仙女的馈赠返回墓地亚瑟王并重洗");
                FinishStackItem(item);
                return;
            }
            player.S2ArthurDiscountUntilTurn = Math.Max(player.S2ArthurDiscountUntilTurn, State.TurnSerial);
            AddEvent("effect", item.Controller, "本回合〈亚瑟王〉登场费用-3", source);
            FinishStackItem(item);
            return;
        }

        if (plan == "grail-journey")
        {
            var choices = player.Library.Where(card => card.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, card, "otherworld"))
                .Select(card => card.InstanceId).Append("skip").ToList();
            if (choices.Count == 1)
            {
                ShuffleLibrary(player, "寻找圣杯之旅检索未命中");
                FinishStackItem(item);
                return;
            }
            CreatePrompt(item.Controller, "optional-card",
                "寻找圣杯之旅：查看牌库并选择1张【彼界】军团展示并加入手牌",
                choices, 1, 1, "card-effect", item.StackItemId, isPrivate: true,
                data: new Dictionary<string, string> { ["action"] = "trial-completion-library-search" });
            return;
        }

        if (plan == "fenian-legend")
        {
            var targets = item.Data.GetValueOrDefault("fenianTargets", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            var targetId = segment >= 0 && segment < targets.Length ? targets[segment] : null;
            var target = DeclaredEnemyTarget(item.Controller, targetId);
            if (target is null)
                AddEvent("effect-cancelled", item.Controller,
                    "芬尼亚传奇选择的目标已失效；该目标不受影响，已支付符文不返还", source);
            else
                AddTimedModifier(target, -3000, 0, ExpiryAtNextOwnEnd(item.Controller), "芬尼亚传奇");
            ResolveStateBasedLegionDeaths();
            FinishStackItem(item);
            return;
        }

        FinishStackItem(item);
    }

    private bool TryContinueTrialCompletionEffect(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        var player = State.Players[item.Controller];
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            case "trial-completion-library-arthur":
            {
                if (chosen[0] != "skip")
                {
                    var selected = player.Library.FirstOrDefault(card =>
                        card.InstanceId == chosen[0] && card.CardId == "S02-0601");
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        AddCardToHandByEffect(player, selected, "library",
                            "湖中仙女的馈赠将亚瑟王加入手牌");
                    }
                }
                FinishStackItem(item);
                return true;
            }
            case "trial-completion-library-search":
            {
                if (chosen[0] != "skip")
                {
                    var selected = player.Library.FirstOrDefault(card => card.InstanceId == chosen[0]
                        && card.CardType == "legion"
                        && L12StructuredCardRules.HasFaction(player, card, "otherworld"));
                    if (selected is not null)
                    {
                        player.Library.Remove(selected);
                        PubliclyRevealThenAddCardToHandByEffect(player, selected, "library",
                            $"寻找圣杯之旅展示〈{selected.Name}〉并加入手牌",
                            "寻找圣杯之旅将彼界军团展示并加入手牌");
                    }
                }
                ShuffleLibrary(player, "寻找圣杯之旅检索结算");
                FinishStackItem(item);
                return true;
            }
            default:
                return false;
        }
    }

    private void QueueNextTrialCompletionSegment(L12StackItem item)
    {
        if (item.Trigger != "trial-complete") return;
        var plan = item.Data.GetValueOrDefault("trialCompletionPlan");
        var current = int.TryParse(item.Data.GetValueOrDefault("trialSegment"), out var parsed) ? parsed : 0;
        var next = plan switch
        {
            "lake-lady" when current < 2 => current + 1,
            "fenian-legend" when current + 1 < item.Data.GetValueOrDefault("fenianTargets", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries).Length => current + 1,
            "sky-city" when current + 1 < item.Data.GetValueOrDefault("skySegments", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries).Length => current + 1,
            _ => -1,
        };
        if (next < 0) return;
        var source = FindSource(item) ?? item.SourceSnapshot
            ?? CreateCard(item.SourceCardId, item.SourceInstanceId);
        var data = new Dictionary<string, string>(item.Data, StringComparer.OrdinalIgnoreCase)
        {
            ["trialSegment"] = next.ToString(),
        };
        var text = plan == "sky-city"
            ? StarterSkySegmentText(item.Data.GetValueOrDefault("skySegments", string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries)[next])
            : plan == "fenian-legend"
            ? $"芬尼亚传奇：第{next + 1}个目标本回合兵力-3000"
            : next == 1
                ? "湖中仙女的馈赠：墓地所有亚瑟王返回牌库并重洗"
                : "湖中仙女的馈赠：本回合亚瑟王登场费用-3";
        PushEffect(item.Controller, source, "trial-complete", text, data: data);
    }

    private static string[] TrialCompletionDeclared(L12StackItem item, string key)
        => item.Data.GetValueOrDefault($"declared:{key}", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);
}
