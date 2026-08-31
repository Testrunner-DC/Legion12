namespace TwelveLegions.Server;

/// <summary>
/// 【进攻时】公开声明的单一入口。卡面在冒号前写明的公开费用、目标和模式在候选入栈前
/// 一次性声明并支付；结算只读取 candidate 携带的不可变声明。
/// </summary>
public sealed partial class L12GameEngine
{
    private sealed record AttackPublicTriggerPlan(
        string PlanId,
        string CostKind = "none",
        string TargetKind = "none",
        bool Optional = true);

    private static readonly IReadOnlyDictionary<string, AttackPublicTriggerPlan> AttackPublicTriggerPlans =
        new Dictionary<string, AttackPublicTriggerPlan>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0104"] = new("hanxin", "return-morale"),
            ["S01-0106"] = new("guanyu", "return-morale"),
            ["S01-0203"] = new("menes", "discard-own-legion"),
            ["S01-0208"] = new("ay", "ordinary-morale", "own-front-low"),
            ["S01-0301"] = new("beowulf", "master-damage"),
            ["S01-0306"] = new("olaf", "grave-bottom-one"),
            ["S01-0311"] = new("gustav", "grave-bottom-two"),
            ["S01-0402"] = new("nobunaga", "ordinary-morale"),
            ["S01-0405"] = new("miyamoto"),
            ["S01-0406"] = new("hijikata", "ordinary-morale", "enemy-cost-one"),
            ["S01-0408"] = new("takasugi", "ordinary-morale", "enemy-legion"),
            ["S01-0413"] = new("hiromasa", TargetKind: "enemy-covered-counter", Optional: false),
            ["S01-0416"] = new("inahime", TargetKind: "own-front-gaotianyuan", Optional: false),
            ["S02-0103"] = new("pingyang"),
            ["S02-0509"] = new("odysseus", "show-hand-tactic"),
            ["S02-0511"] = new("perot", "god-power", "attack-legion"),
            ["S02-0517"] = new("penthesilea", "god-power"),
            ["S02-0519"] = new("spartan", "god-power"),
            ["S02-0605"] = new("bors", "ordinary-morale"),
            ["S02-0606"] = new("percival", "discard-hand"),
            ["S02-0607"] = new("gawain", "rune-count", Optional: false),
            ["S02-0608"] = new("richard"),
            ["S02-0612"] = new("scathach", "rune-one"),
            ["S02-0617"] = new("robin"),
        };

    private static bool HasAttackPublicTriggerDeclarationPlan(string cardId, string trigger)
        => trigger.Equals("attack", StringComparison.OrdinalIgnoreCase)
            && AttackPublicTriggerPlans.ContainsKey(cardId);

    private bool TryQueueAttackPublicTriggerCandidates(int controller, L12CardInstance source, string trigger,
        string text, IEnumerable<string>? targets, IReadOnlyDictionary<string, string>? data)
    {
        if (!HasAttackPublicTriggerDeclarationPlan(source.CardId, trigger)) return false;

        L12TriggerCandidate Candidate(string planId, string candidateText, bool complete = false)
        {
            var candidateData = data is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
            candidateData["attackPlan"] = planId;
            if (complete) candidateData["declaration-complete"] = "true";
            var candidate = CreateTriggerCandidate(controller, source, trigger, candidateText, candidateData, source);
            if (targets is not null) candidate.Data["declaredTargets"] = string.Join('|', targets);
            return candidate;
        }

        if (source.CardId == "S02-0608")
        {
            var candidates = new List<L12TriggerCandidate>
            {
                Candidate("richard-defense", "进攻时：对方抵挡/支援需额外弃置1张手牌", complete: true),
            };
            if (source.AttachedCards.Any(card => card.CardId == "S02-0609"))
                candidates.Add(Candidate("richard-squires", "进攻时：可弃置侍从骑士使兵力增加"));
            QueueTriggerCandidates(candidates);
            return true;
        }

        if (source.CardId == "S02-0617")
        {
            var candidates = new List<L12TriggerCandidate>
            {
                Candidate("robin-rune", "进攻时：获得1符文", complete: true),
            };
            if (PublicLegions(State.Players[controller]).Any(card => card.CardId == "S02-0608"))
                candidates.Add(Candidate("robin-draw", "进攻时：可抽牌1张"));
            QueueTriggerCandidates(candidates);
            return true;
        }

        QueueTriggerCandidates([Candidate(AttackPublicTriggerPlans[source.CardId].PlanId, text)]);
        return true;
    }

    private bool TryBeginAttackPublicTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        if (candidate.Trigger != "attack" || !candidate.Data.TryGetValue("attackPlan", out var planId))
            return false;
        if (planId is "richard-defense" or "robin-rune" or "gawain-buff")
            return false;

        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var plan = AttackPublicTriggerPlans.GetValueOrDefault(candidate.SourceCardId);
        var steps = new List<L12ActivationSelectionStep>();

        if (planId == "richard-squires")
        {
            var squires = source.AttachedCards.Where(card => card.CardId == "S02-0609")
                .Select(card => card.InstanceId).ToList();
            steps.Add(PublicTriggerStep("option", "mode", "狮心王理查一世：预先声明是否弃置侍从骑士",
                squires.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]));
            steps.Add(PublicTriggerStep("attached-cards", "squires", "狮心王理查一世：预先选择并弃置任意数量侍从骑士",
                squires, min: 1, max: Math.Max(1, squires.Count), requiredChoice: "mode:use"));
        }
        else if (planId == "robin-draw")
        {
            steps.Add(PublicTriggerStep("option", "mode", "罗宾汉：预先声明是否抽取1张牌",
                ["mode:none", "mode:use"]));
        }
        else if (plan is not null)
        {
            var canUse = CanDeclareAttackPlan(candidate, plan, source);
            if (plan.Optional)
                steps.Add(PublicTriggerStep("option", "mode", $"{source.Name}：预先声明是否发动进攻时效果",
                    canUse ? ["mode:none", "mode:use"] : ["mode:none"]));

            var required = plan.Optional ? "mode:use" : null;
            switch (plan.CostKind)
            {
                case "return-morale":
                    steps.Add(PublicTriggerStep("target-morale", "cost", $"{source.Name}：预先选择返还的1张士气",
                        player.Morale.Select(card => card.InstanceId), requiredChoice: required));
                    break;
                case "discard-own-legion":
                    steps.Add(PublicTriggerStep("field-legion", "cost", "美尼斯：预先选择作为费用弃置的我方1张军团",
                        PublicLegions(player).Select(card => card.InstanceId), requiredChoice: required));
                    break;
                case "ordinary-morale":
                    steps.Add(PublicTriggerStep("composite-ordinary-payment", "cost", $"{source.Name}：预先选择消耗的1份公开资源",
                        CompositeOrdinaryPaymentChoices(player), requiredChoice: required));
                    break;
                case "grave-bottom-one":
                    steps.Add(PublicTriggerStep("grave-card", "cost", "奥拉夫二世：预先选择置于牌库底部的墓地1张牌",
                        player.Graveyard.Select(card => card.InstanceId), requiredChoice: required));
                    break;
                case "grave-bottom-two":
                    steps.Add(PublicTriggerStep("order", "cost", "古斯塔夫一世：预先选择并排序返回牌库底部的墓地2张牌",
                        player.Graveyard.Select(card => card.InstanceId), min: 2, max: 2, requiredChoice: required));
                    break;
                case "show-hand-tactic":
                    steps.Add(PublicTriggerStep("hand-card", "cost", "奥德修斯：预先选择并展示手牌中的1张战术",
                        player.Hand.Where(card => card.CardType == "tactic").Select(card => card.InstanceId), requiredChoice: required));
                    break;
                case "god-power":
                    steps.Add(PublicTriggerStep("target-morale", "cost", $"{source.Name}：预先选择消耗并翻转的1神力",
                        player.Morale.Where(card => card.IsGodPower && !card.Tapped).Select(card => card.InstanceId),
                        requiredChoice: required));
                    break;
                case "discard-hand":
                    steps.Add(PublicTriggerStep("hand-card", "cost", "帕西瓦尔：预先选择弃置的1张手牌",
                        player.Hand.Select(card => card.InstanceId), requiredChoice: required));
                    break;
                case "rune-count":
                {
                    var choices = Enumerable.Range(1, player.SpecialZones.Runes)
                        .Select(count => $"rune-count:{count}").ToList();
                    var runeStep = PublicTriggerStep("option", "runeCount", "高文：预先声明本次效果要消耗的符文数量",
                        choices);
                    foreach (var choice in choices)
                        runeStep.ChoiceLabels[choice] = $"消耗{choice["rune-count:".Length..]}符文";
                    steps.Add(runeStep);
                    break;
                }
                case "rune-one":
                    break;
                case "master-damage":
                    break;
            }

            switch (plan.TargetKind)
            {
                case "own-front-low":
                    steps.Add(PublicTriggerStep("field-legion", "target", "阿伊：预先选择我方前排1张兵力不高于2000的军团",
                        player.Field[0].Where(card => card is not null && IsFieldLegion(card) && card.Troops <= 2000)
                            .Select(card => card!.InstanceId), requiredChoice: required));
                    break;
                case "enemy-cost-one":
                    steps.Add(PublicTriggerStep("enemy-legion", "target", "土方岁三：预先选择对方1张费用不高于1的军团",
                        PublicLegions(opponent).Where(card => card.CurrentCost <= 1).Select(card => card.InstanceId),
                        requiredChoice: required));
                    break;
                case "enemy-legion":
                    steps.Add(PublicTriggerStep("enemy-legion", "target", "高杉晋作：预先选择对方1张军团",
                        PublicLegions(opponent).Select(card => card.InstanceId), requiredChoice: required));
                    break;
                case "enemy-covered-counter":
                    steps.Add(PublicTriggerStep("covered-counter", "target", "源博雅：预先选择对方后排1张覆盖的反击战术",
                        opponent.Field[1].Where(card => card is { CardType: "tactic" }).Select(card => card!.InstanceId)));
                    break;
                case "own-front-gaotianyuan":
                    steps.Add(PublicTriggerStep("field-legion", "target", "稻姬：预先选择我方前排1张其他低兵力【高天原】军团",
                        player.Field[0].Where(card => card is not null && card.InstanceId != source.InstanceId
                                && card.Faction == "gaotianyuan" && card.Troops <= 5000)
                            .Select(card => card!.InstanceId)));
                    break;
            }
        }

        if (steps.Count == 0) return false;
        var result = BeginPendingActivationSequence(candidate.Controller, source, "public-trigger-declaration",
            steps, candidate.CandidateId);
        if (result.Accepted) return true;
        RemoveUnstackedTriggerCandidate(candidate, result.Error ?? "进攻时效果的公开声明已失效，效果未入栈");
        return true;
    }

    private bool CanDeclareAttackPlan(L12TriggerCandidate candidate, AttackPublicTriggerPlan plan,
        L12CardInstance source)
    {
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var costAvailable = plan.CostKind switch
        {
            "return-morale" => CanReturnMorale(player, 1),
            "discard-own-legion" => PublicLegions(player).Any(),
            "ordinary-morale" => ActiveResourceCount(player) > 0,
            "master-damage" => player.Hp > 1,
            "grave-bottom-one" => player.Graveyard.Count > 0,
            "grave-bottom-two" => player.Graveyard.Count >= 2,
            "show-hand-tactic" => player.Hand.Any(card => card.CardType == "tactic"),
            "god-power" => player.Morale.Any(card => card.IsGodPower && !card.Tapped),
            "discard-hand" => player.Hand.Count > 0,
            "rune-count" or "rune-one" => player.SpecialZones.Runes > 0,
            _ => true,
        };
        var targetAvailable = plan.TargetKind switch
        {
            "own-front-low" => player.Field[0].Any(card => card is not null && IsFieldLegion(card) && card.Troops <= 2000),
            "enemy-cost-one" => PublicLegions(opponent).Any(card => card.CurrentCost <= 1),
            "enemy-legion" => PublicLegions(opponent).Any(),
            "attack-legion" => State.PendingDefense?.Target.Type == "legion",
            "enemy-covered-counter" => opponent.Field[1].Any(card => card is { CardType: "tactic" }),
            "own-front-gaotianyuan" => player.Field[0].Any(card => card is not null
                && card.InstanceId != source.InstanceId && card.Faction == "gaotianyuan" && card.Troops <= 5000),
            _ => true,
        };
        var condition = plan.PlanId switch
        {
            "miyamoto" => player.Hand.Count <= opponent.Hand.Count,
            "pingyang" => player.Library.Count > 0,
            _ => true,
        };
        return costAvailable && targetAvailable && condition;
    }

    private bool TryCompleteAttackPublicTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        if (candidate.Trigger != "attack" || !candidate.Data.TryGetValue("attackPlan", out var planId))
            return false;

        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        if (mode == "mode:none")
        {
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                $"〈{candidate.SourceName}〉的可选进攻时效果未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        var source = FindOnField(player, candidate.SourceInstanceId, out _, out _);
        var costIds = activation.DeclaredValues.GetValueOrDefault("cost", []);
        var targetId = activation.DeclaredValues.GetValueOrDefault("target", []).SingleOrDefault();
        string? error = null;

        if (planId == "richard-squires")
        {
            var ids = activation.DeclaredValues.GetValueOrDefault("squires", []);
            if (source is null || ids.Count == 0 || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count
                || ids.Any(id => !source.AttachedCards.Any(card => card.InstanceId == id && card.CardId == "S02-0609")))
                error = "理查声明的侍从骑士费用已失效；未支付费用且效果未入栈";
            else
            {
                var discarded = source.AttachedCards.Where(card => ids.Contains(card.InstanceId,
                    StringComparer.OrdinalIgnoreCase)).ToArray();
                foreach (var squire in discarded)
                {
                    source.AttachedCards.Remove(squire);
                    ResetCardAfterLeavingField(squire);
                    player.Graveyard.Add(squire);
                }
                activation.DeclaredValues["squireCount"] = [discarded.Length.ToString()];
                AddEvent("cost", candidate.Controller, $"狮心王理查一世弃置{discarded.Length}张侍从骑士作为费用",
                    [source, .. discarded]);
            }
        }
        else if (planId == "robin-draw")
        {
            // 没有公开费用或目标；只需锁定是否发动。
        }
        else if (AttackPublicTriggerPlans.GetValueOrDefault(candidate.SourceCardId) is { } plan)
        {
            if (!CanDeclareAttackPlan(candidate, plan, source ?? candidate.SourceSnapshot
                    ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId)))
                error = $"{candidate.SourceName}的公开费用、条件或目标已失效；未支付费用且效果未入栈";

            if (error is null)
            {
                error = plan.CostKind switch
                {
                    "return-morale" when costIds.Count != 1 || !CanReturnSelectedMoraleById(player, costIds, 1)
                        => $"{candidate.SourceName}声明的返还士气已失效；未支付费用且效果未入栈",
                    "discard-own-legion" when costIds.Count != 1
                        || FindOnField(player, costIds[0], out _, out _) is not { } sacrifice
                        || !IsFieldLegion(sacrifice)
                        => "美尼斯声明的弃置费用已失效；未支付费用且效果未入栈",
                    "ordinary-morale" when !CanConsumeAttackOrdinaryCost(player, costIds)
                        => $"{candidate.SourceName}声明的士气费用已失效；未支付费用且效果未入栈",
                    "master-damage" when player.Hp <= 1
                        => "贝奥武夫的主宰伤害费用已失效；未支付费用且效果未入栈",
                    "grave-bottom-one" when costIds.Count != 1
                        || !player.Graveyard.Any(card => card.InstanceId == costIds[0])
                        => "奥拉夫二世声明的墓地费用已失效；未支付费用且效果未入栈",
                    "grave-bottom-two" when costIds.Count != 2
                        || costIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
                        || costIds.Any(id => !player.Graveyard.Any(card => card.InstanceId == id))
                        => "古斯塔夫一世声明的墓地费用已失效；未支付费用且效果未入栈",
                    "show-hand-tactic" when costIds.Count != 1
                        || !player.Hand.Any(card => card.InstanceId == costIds[0] && card.CardType == "tactic")
                        => "奥德修斯声明展示的战术已失效；效果未入栈",
                    "god-power" when costIds.Count != 1
                        || !player.Morale.Any(card => card.InstanceId == costIds[0] && card.IsGodPower && !card.Tapped)
                        => $"{candidate.SourceName}声明的神力已失效；未支付费用且效果未入栈",
                    "discard-hand" when costIds.Count != 1
                        || !player.Hand.Any(card => card.InstanceId == costIds[0])
                        => "帕西瓦尔声明的手牌费用已失效；未支付费用且效果未入栈",
                    "rune-one" when player.SpecialZones.Runes < 1
                        => "斯卡哈声明的符文费用已失效；未支付费用且效果未入栈",
                    "rune-count" when ParseRuneCount(activation) is not > 0
                        => "高文必须声明消耗至少1符文；效果未入栈",
                    _ => null,
                };
            }

            if (error is null)
            {
                error = plan.TargetKind switch
                {
                    "own-front-low" when targetId is null
                        || FindOnField(player, targetId, out var row, out _) is not { } target
                        || row != 0 || !IsFieldLegion(target) || target.Troops > 2000
                        => "阿伊声明的前排目标已失效；未支付费用且效果未入栈",
                    "enemy-cost-one" when targetId is null
                        || FindOnField(opponent, targetId, out _, out _) is not { } target || target.CurrentCost > 1
                        => "土方岁三声明的击杀目标已失效；未支付费用且效果未入栈",
                    "enemy-legion" when targetId is null || FindOnField(opponent, targetId, out _, out _) is null
                        => "高杉晋作声明的目标已失效；未支付费用且效果未入栈",
                    "enemy-covered-counter" when targetId is null
                        || FindOnField(opponent, targetId, out var row, out _) is not { CardType: "tactic" } || row != 1
                        => "源博雅声明的覆盖反击战术已失效；效果未入栈",
                    "own-front-gaotianyuan" when targetId is null
                        || FindOnField(player, targetId, out var row, out _) is not { } target || row != 0
                        || target.InstanceId == candidate.SourceInstanceId || target.Faction != "gaotianyuan"
                        || target.Troops > 5000
                        => "稻姬声明的前排目标已失效；效果未入栈",
                    _ => null,
                };
            }

            if (error is null)
                PayAttackPublicCost(candidate, activation, plan, player, source, costIds);
        }

        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        candidate.Data["declaration-complete"] = "true";
        AdvanceTriggerBatches();
        return true;
    }

    private bool CanConsumeAttackOrdinaryCost(L12PlayerState player, IReadOnlyList<string> costIds)
        => costIds.Count == 1 && costIds[0] == "temporary-morale:1"
            ? CanConsumeSelectedResources(player, 1, [])
            : CanConsumeSelectedResources(player, 1, costIds);

    private void PayAttackPublicCost(L12TriggerCandidate candidate, L12PendingActivation activation,
        AttackPublicTriggerPlan plan, L12PlayerState player, L12CardInstance? source, IReadOnlyList<string> costIds)
    {
        switch (plan.CostKind)
        {
            case "return-morale":
                _ = ReturnSelectedMoraleById(player, costIds, 1);
                break;
            case "discard-own-legion":
                if (FindOnField(player, costIds[0], out _, out _) is { } sacrifice)
                    RemoveFromField(player, sacrifice, true, "被美尼斯作为进攻效果费用弃置",
                        leaveKind: L12FieldLeaveKind.Discard);
                break;
            case "ordinary-morale":
                _ = TryConsumeSelectedResources(player, 1,
                    costIds.Count == 1 && costIds[0] == "temporary-morale:1" ? [] : costIds);
                break;
            case "master-damage":
                DamageMaster(candidate.Controller, 1, "贝奥武夫进攻效果费用");
                break;
            case "grave-bottom-one":
            case "grave-bottom-two":
                MoveGraveToLibraryBottom(player, costIds.Select(id => player.Graveyard.First(card =>
                    card.InstanceId == id)).ToArray());
                break;
            case "show-hand-tactic":
                if (player.Hand.FirstOrDefault(card => card.InstanceId == costIds[0]) is { } shown)
                    AddEvent("reveal", candidate.Controller, $"奥德修斯展示手牌中的〈{shown.Name}〉作为进攻效果费用", shown);
                break;
            case "god-power":
                if (player.Morale.First(card => card.InstanceId == costIds[0]) is { } power)
                {
                    power.Tapped = true;
                    power.IsGodPower = false;
                }
                break;
            case "discard-hand":
                MoveHandToGrave(player, costIds[0], causedByEffect: false);
                break;
            case "rune-one":
                _ = L12S2ZoneOps.SpendRunes(player, 1);
                break;
        }
    }

    private static int ParseRuneCount(L12PendingActivation activation)
    {
        var choice = activation.DeclaredValues.GetValueOrDefault("runeCount", []).SingleOrDefault();
        return choice?.Split(':') is ["rune-count", var countText] && int.TryParse(countText, out var count)
            ? count : 0;
    }

    private bool TryResolveAttackPublicTriggerEffect(L12StackItem item, L12CardInstance card)
    {
        if (item.Trigger != "attack" || !item.Data.TryGetValue("attackPlan", out var planId)) return false;
        if (planId == "miyamoto") return false;

        var player = State.Players[item.Controller];
        var opponent = State.Players[1 - item.Controller];
        var source = FindOnField(player, item.SourceInstanceId, out _, out _);
        var targetId = PublicTriggerDeclared(item, "target");

        void Cancel(string reason) => AddEvent("effect-cancelled", item.Controller, reason, card);
        void Finish() => FinishStackItem(item);
        void BuffSource(int amount, string label)
        {
            if (source is null) Cancel($"〈{item.SourceName}〉已离开战场；其自身增益段取消");
            else AddTimedModifier(source, amount, 0,
                item.SourceCardId.StartsWith("S02", StringComparison.Ordinal) ? ExpiryAtNextOwnEnd(item.Controller) : State.TurnSerial,
                label);
        }

        switch (planId)
        {
            case "hanxin":
                BuffSource(1000, "韩信");
                if (source is not null) GrantStrongAttack(source);
                Finish(); return true;
            case "guanyu":
                BuffSource(1000, "关羽");
                if (source is not null)
                {
                    source.HasSureHit = true;
                    if (State.PendingDefense is not null) State.PendingDefense.SureHit = true;
                }
                Finish(); return true;
            case "menes":
                BuffSource(2000, "美尼斯");
                if (source is not null) GrantStrongAttack(source);
                Finish(); return true;
            case "ay":
                if (FindOnField(player, targetId, out _, out _) is { } ayTarget)
                    AddTimedModifier(ayTarget, 2000, 0, State.TurnSerial, "阿伊");
                else Cancel("阿伊声明的目标已失效；已支付费用不返还");
                Finish(); return true;
            case "beowulf":
                BuffSource(2000, "贝奥武夫");
                Finish(); return true;
            case "olaf":
                if (source is null) Cancel("奥拉夫二世已离开战场；已支付的墓地费用不返还");
                else GrantStrongAttack(source);
                Finish(); return true;
            case "gustav":
                BuffSource(2000, "古斯塔夫一世");
                Finish(); return true;
            case "nobunaga":
                foreach (var enemy in PublicLegions(opponent)) enemy.CostModifier--;
                Finish(); return true;
            case "hijikata":
                if (FindOnField(opponent, targetId, out _, out _) is { } hijikataTarget
                    && hijikataTarget.CurrentCost <= 1)
                    KillTarget(item, targetId, "被土方岁三击杀");
                else Cancel("土方岁三声明的目标已失效；已支付费用不返还");
                Finish(); return true;
            case "takasugi":
                if (FindOnField(opponent, targetId, out _, out _) is { } takasugiTarget)
                    takasugiTarget.CostModifier -= 2;
                else Cancel("高杉晋作声明的目标已失效；已支付费用不返还");
                Finish(); return true;
            case "hiromasa":
                if (FindOnField(opponent, targetId, out var counterRow, out _) is { CardType: "tactic" } counter
                    && counterRow == 1)
                    counter.CannotRespondUntilRound = State.Round;
                else Cancel("源博雅声明的覆盖反击战术已失效");
                Finish(); return true;
            case "inahime":
                if (FindOnField(player, targetId, out var targetRow, out _) is { } inahimeTarget && targetRow == 0
                    && inahimeTarget.InstanceId != item.SourceInstanceId && inahimeTarget.Faction == "gaotianyuan"
                    && inahimeTarget.Troops <= 5000)
                    AddTimedModifier(inahimeTarget, 1000, 0, State.TurnSerial, "稻姬本多小松");
                else Cancel("稻姬声明的目标已失效");
                Finish(); return true;
            case "pingyang":
            {
                var top = player.Library.FirstOrDefault();
                if (top is not null)
                {
                    AddEvent("reveal", item.Controller, $"平阳昭公主展示牌库顶部的〈{top.Name}〉", top);
                    if (top.Faction == "tianting" && top.CurrentCost <= 5)
                    {
                        if (source is not null)
                            AddTimedModifier(source, 2000, 0, ExpiryAtNextOwnEnd(item.Controller), "平阳昭公主");
                        else Cancel("平阳昭公主已离开战场；展示仍结算但自身增益取消");
                    }
                    else
                    {
                        player.Library.Remove(top);
                        player.Library.Add(top);
                    }
                }
                Finish(); return true;
            }
            case "odysseus":
                BuffSource(1000, "奥德修斯");
                Finish(); return true;
            case "perot":
                BuffSource(1000, "珀洛特埃");
                if (source is not null)
                {
                    source.HasShock = true;
                    ApplyS2Shock(item, source);
                }
                Finish(); return true;
            case "penthesilea":
                BuffSource(2000, "彭忒西勒亚");
                Finish(); return true;
            case "spartan":
                BuffSource(2000, "斯巴达勇士");
                Finish(); return true;
            case "bors":
                if (source is null) Cancel("鲍斯已离开战场；已支付费用不返还");
                else GrantStrongAttack(source);
                Finish(); return true;
            case "percival":
                BuffSource(2000, "帕西瓦尔");
                Finish(); return true;
            case "gawain":
            {
                var count = PublicTriggerDeclared(item, "runeCount").Split(':') is ["rune-count", var countText]
                    && int.TryParse(countText, out var declared) ? declared : 0;
                if (count <= 0 || !L12S2ZoneOps.SpendRunes(player, count))
                {
                    Cancel("高文声明的符文数量在结算时已失效；消耗段取消");
                    Finish(); return true;
                }
                var data = new Dictionary<string, string>
                {
                    ["attackPlan"] = "gawain-buff",
                    ["declared:runeCount"] = $"rune-count:{count}",
                    ["declaration-complete"] = "true",
                };
                QueueTriggerCandidates([
                    CreateTriggerCandidate(item.Controller, card, "attack", "进攻时：每消耗1符文获得兵力与伤害加成",
                        data, item.SourceSnapshot ?? card)
                ]);
                Finish(); return true;
            }
            case "gawain-buff":
            {
                var count = PublicTriggerDeclared(item, "runeCount").Split(':') is ["rune-count", var countText]
                    && int.TryParse(countText, out var declared) ? declared : 0;
                if (source is null || count <= 0)
                    Cancel("高文已离开战场；已消耗符文不返还，增益段取消");
                else
                {
                    AddTimedModifier(source, count * 1000, 0, ExpiryAtNextOwnEnd(item.Controller), "高文");
                    if (source.GawainMasterDamageBonusUntilTurn != State.TurnSerial)
                        source.GawainMasterDamageBonus = 0;
                    source.GawainMasterDamageBonusUntilTurn = State.TurnSerial;
                    source.GawainMasterDamageBonus += count;
                    if (State.PendingDefense is { Target.Type: "master" } pending
                        && pending.AttackerInstanceId == source.InstanceId)
                        pending.MasterDamage += count;
                }
                Finish(); return true;
            }
            case "richard-defense":
                if (State.PendingDefense is { } richardAttack
                    && richardAttack.AttackerInstanceId == item.SourceInstanceId)
                    richardAttack.RichardDefenseTaxActive = true;
                else Cancel("理查的抵挡费用段已不属于当前进攻，效果取消");
                Finish(); return true;
            case "richard-squires":
            {
                var count = int.TryParse(PublicTriggerDeclared(item, "squireCount"), out var declared) ? declared : 0;
                if (source is null || count <= 0)
                    Cancel("理查已离开战场；已弃置的侍从骑士不返还，增益段取消");
                else
                    AddTimedModifier(source, count * 1000, 0, ExpiryAtNextOwnEnd(item.Controller), "狮心王理查一世");
                Finish(); return true;
            }
            case "scathach":
                BuffSource(2000, "斯卡哈");
                if (source is not null)
                    source.AttackNoLossUntilTurn = Math.Max(source.AttackNoLossUntilTurn,
                        ExpiryAtNextOwnEnd(item.Controller));
                Finish(); return true;
            case "robin-rune":
                L12S2ZoneOps.GainRunes(player, 1);
                Finish(); return true;
            case "robin-draw":
                _ = Draw(player, 1);
                Finish(); return true;
            default:
                return false;
        }
    }
}
