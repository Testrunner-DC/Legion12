namespace TwelveLegions.Server;

/// <summary>
/// 试炼推进事件的统一声明与结算。推进只改变进度；达到8点后仍须另行发动“完成试炼”，
/// 因而这里绝不发布 Batch 6B 的 trial-complete 事件。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string TrialAdvanceFinnCardId = "S02-0610";
    private static string? TrialAdvanceTriggerPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data)
        => (cardId, trigger, data?.GetValueOrDefault("ability"), data?.GetValueOrDefault("killed")) switch
        {
            ("S02-0602", "enter", _, _) => "lancelot-entry",
            ("S02-0602", "after-attack", _, "true") => "lancelot-kill",
            ("S02-0604", "enter", _, _) => "galahad-entry",
            ("S02-0610", "enter", _, _) => "finn-entry",
            ("S02-0614", "enter", _, _) => "constance-entry",
            ("S02-0610", "trial-advance-followup", "finnReady", _) => "finn-ready",
            ("S02-06M2", "active", "angusTacticTrial", _) => "angus",
            ("S02-06D1", "turn-start", "avalonTurnStart", _) => "avalon",
            _ => null,
        };

    private static bool HasTrialAdvanceTriggerDeclarationPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data)
        => TrialAdvanceTriggerPlan(cardId, trigger, data) is not null;

    private bool TryBeginTrialAdvanceTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        var plan = TrialAdvanceTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        if (plan is null) return false;

        var player = State.Players[candidate.Controller];
        var hasOpenTrial = player.SpecialZones.Trials.Any(card => !card.TrialCompleted);
        List<L12ActivationSelectionStep> steps = plan switch
        {
            "lancelot-entry" =>
            [PublicTriggerStep("option", "mode", "兰斯洛特：是否消耗1符文并获得冲锋",
                player.SpecialZones.Runes > 0 ? ["mode:none", "mode:use"] : ["mode:none"])],
            "lancelot-kill" =>
            [PublicTriggerStep("option", "mode", "兰斯洛特：选择击杀时效果",
                hasOpenTrial ? ["mode:none", "mode:trial", "mode:rune"] : ["mode:none", "mode:rune"])],
            "galahad-entry" =>
            [PublicTriggerStep("option", "mode", "加拉哈德：是否休整并发动试炼",
                hasOpenTrial && SourceIsFieldCard(candidate.Controller, candidate.SourceInstanceId, out var galahad)
                    && !galahad.Tapped ? ["mode:none", "mode:trial"] : ["mode:none"])],
            "finn-entry" =>
            [PublicTriggerStep("option", "mode", "芬恩：是否休整并发动试炼",
                hasOpenTrial && SourceIsFieldCard(candidate.Controller, candidate.SourceInstanceId, out var finn)
                    && !finn.Tapped ? ["mode:none", "mode:trial"] : ["mode:none"])],
            "constance-entry" =>
            [PublicTriggerStep("option", "mode", "康斯坦丝：选择登场时效果",
                hasOpenTrial && SourceIsFieldCard(candidate.Controller, candidate.SourceInstanceId, out var constance)
                    && !constance.Tapped
                    ? ["mode:none", "mode:rune", "mode:trial"] : ["mode:none", "mode:rune"])],
            "finn-ready" =>
            [PublicTriggerStep("option", "mode", "芬恩：是否消耗1符文并转为活跃",
                player.SpecialZones.Runes > 0
                    && SourceIsFieldCard(candidate.Controller, candidate.SourceInstanceId, out var readyFinn)
                    && readyFinn.Tapped ? ["mode:none", "mode:use"] : ["mode:none"])],
            "angus" or "avalon" =>
            [new L12ActivationSelectionStep
            {
                Kind = "option", DeclarationKey = "mode", Text = "试炼推进事件",
                ValidChoices = ["mode:mandatory"], MinChoose = 1, MaxChoose = 1,
                AutoSelectWhenExact = true,
            }],
            _ => [],
        };
        candidate.Data["trialAdvancePlan"] = plan;
        var result = BeginPendingActivationSequence(candidate.Controller, source, "public-trigger-declaration",
            steps, candidate.CandidateId);
        if (result.Accepted) return true;
        RemoveUnstackedTriggerCandidate(candidate, result.Error ?? "试炼推进选择已失效，效果未入栈");
        return true;
    }

    private bool TryCompleteTrialAdvanceTriggerDeclaration(L12TriggerCandidate candidate,
        L12PendingActivation activation)
    {
        var plan = candidate.Data.GetValueOrDefault("trialAdvancePlan")
            ?? TrialAdvanceTriggerPlan(candidate.SourceCardId, candidate.Trigger, candidate.Data);
        if (plan is null) return false;

        var player = State.Players[candidate.Controller];
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        if (mode == "mode:none")
        {
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                $"〈{candidate.SourceName}〉的可选试炼效果未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        string? error = null;
        var source = FindOnField(player, candidate.SourceInstanceId, out _, out _);
        switch (plan)
        {
            case "lancelot-entry":
            case "finn-ready":
                if (source is null || (plan == "finn-ready" && !source.Tapped)
                    || player.SpecialZones.Runes < 1)
                    error = $"{candidate.SourceName}的来源或符文费用已失效；未支付费用且效果未入栈";
                else
                {
                    player.SpecialZones.Runes--;
                    AddEvent("cost", candidate.Controller, $"〈{candidate.SourceName}〉入栈前消耗1符文", source);
                }
                break;
            case "galahad-entry":
            case "finn-entry":
            case "constance-entry" when mode == "mode:trial":
                if (source is null || source.Tapped || player.SpecialZones.Trials.All(card => card.TrialCompleted))
                    error = $"{candidate.SourceName}的来源或试炼已失效；未休整且效果未入栈";
                else
                {
                    source.Tapped = true;
                    AddEvent("cost", candidate.Controller, $"〈{candidate.SourceName}〉入栈前休整以发动试炼", source);
                }
                break;
            case "lancelot-kill" when mode == "mode:trial":
            case "angus":
                if (player.SpecialZones.Trials.All(card => card.TrialCompleted))
                    error = $"{candidate.SourceName}发动时已没有尚未完成的试炼；效果未入栈";
                break;
        }
        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        candidate.Data["trialAdvancePlan"] = plan;
        if (plan is "galahad-entry" or "finn-entry" or "angus" or "avalon"
            || plan == "constance-entry" && mode == "mode:trial"
            || plan == "lancelot-kill" && mode == "mode:trial")
        {
            candidate.Data["trialAdvanceEvent"] = "true";
            candidate.Data["trialAdvanceCount"] = plan == "galahad-entry" ? "2" : "1";
        }
        candidate.Data["declaration-complete"] = "true";
        AdvanceTriggerBatches();
        return true;
    }

    private CommandResult BeginTrialAdvanceActivation(int playerIndex, L12CardInstance source)
    {
        var player = State.Players[playerIndex];
        if (source.Tapped) return CommandResult.Reject("该军团必须为活跃状态");
        if (source.SummonRound >= State.Round) return CommandResult.Reject("登场回合不能通过通常行动发动试炼");
        if (player.SpecialZones.Trials.All(card => card.TrialCompleted)) return CommandResult.Reject("没有尚未完成的试炼");
        if (player.UsedAbilities.Contains($"trial-card-lock:{source.InstanceId}:{State.TurnSerial}"))
            return CommandResult.Reject("该军团因卡牌效果本回合无法再次发动试炼");
        return ResolveUsualTrialAdvance(playerIndex, source);
    }

    private CommandResult? TryCommitTrialAdvanceActivation(int playerIndex, L12CardInstance source, string ability)
    {
        if (ability != "trialAdvance" || source.TrialValue <= 0) return null;
        var player = State.Players[playerIndex];
        if (source.Tapped || source.SummonRound >= State.Round
            || player.SpecialZones.Trials.All(card => card.TrialCompleted)
            || player.UsedAbilities.Contains($"trial-card-lock:{source.InstanceId}:{State.TurnSerial}"))
            return CommandResult.Reject("发动试炼的来源或条件已失效");
        return ResolveUsualTrialAdvance(playerIndex, source);
    }

    /// <summary>
    /// FAQ“发动试炼”是试炼军团的通常行动，不是卡牌效果：休整后立即推进，
    /// 不进入效果堆叠，也不会开启响应窗口。卡牌文字产生的试炼推进仍走各自的
    /// 触发/主动效果流程，两类入口不得合并。
    /// </summary>
    private CommandResult ResolveUsualTrialAdvance(int playerIndex, L12CardInstance source)
    {
        var player = State.Players[playerIndex];
        source.Tapped = true;
        AddEvent("trial-action", playerIndex, $"〈{source.Name}〉休整并发动试炼", source);
        if (AdvanceTrial(playerIndex, source.TrialValue, source))
            QueueFinnReadyAfterTrial(playerIndex, source);
        return CommandResult.Ok();
    }

    private bool TryResolveTrialAdvanceEffect(L12StackItem item)
    {
        var plan = item.Data.GetValueOrDefault("trialAdvancePlan");
        if (string.IsNullOrWhiteSpace(plan)) return false;
        var player = State.Players[item.Controller];
        var mode = item.Data.GetValueOrDefault("declared:mode");
        var source = FindOnField(player, item.SourceInstanceId, out _, out _);
        switch (plan)
        {
            case "generic":
                if (AdvanceTrial(item.Controller, int.Parse(item.Data["trialAdvanceCount"]), item.SourceSnapshot ?? source))
                    QueueFinnReadyAfterTrial(item.Controller, source);
                break;
            case "galahad-entry":
                _ = AdvanceTrial(item.Controller, 2, item.SourceSnapshot ?? source);
                break;
            case "finn-entry":
                if (AdvanceTrial(item.Controller, 1, item.SourceSnapshot ?? source))
                    QueueFinnReadyAfterTrial(item.Controller, source);
                break;
            case "constance-entry":
                if (mode == "mode:trial") _ = AdvanceTrial(item.Controller, 1, item.SourceSnapshot ?? source);
                else if (mode == "mode:rune")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    if ((item.SourceSnapshot ?? source) is { } constanceSource)
                        AddEvent("runes", item.Controller, "康斯坦丝使我方获得1符文", constanceSource);
                    else AddEvent("runes", item.Controller, "康斯坦丝使我方获得1符文");
                }
                break;
            case "lancelot-entry":
                if (source is not null) source.HasCharge = true;
                else AddEvent("effect-cancelled", item.Controller, "兰斯洛特已离场；获得冲锋段取消，已支付符文不返还");
                break;
            case "lancelot-kill":
                if (mode == "mode:trial") _ = AdvanceTrial(item.Controller, 1, item.SourceSnapshot);
                else if (mode == "mode:rune")
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    if (item.SourceSnapshot is { } lancelotSource)
                        AddEvent("runes", item.Controller, "兰斯洛特击杀效果使我方获得1符文", lancelotSource);
                    else AddEvent("runes", item.Controller, "兰斯洛特击杀效果使我方获得1符文");
                }
                break;
            case "finn-ready":
                if (source is not null)
                {
                    source.Tapped = false;
                    player.UsedAbilities.Add($"trial-card-lock:{source.InstanceId}:{State.TurnSerial}");
                    AddEvent("ready", item.Controller, "芬恩转为活跃，本回合不能再次发动试炼", source);
                }
                else AddEvent("effect-cancelled", item.Controller, "芬恩已离场；转为活跃段取消，已支付符文不返还");
                break;
            case "angus":
                _ = AdvanceTrial(item.Controller, 1, item.SourceSnapshot);
                break;
            case "avalon":
                _ = AdvanceTrial(item.Controller, 1, item.SourceSnapshot);
                L12S2ZoneOps.GainRunes(player, 1);
                if (item.SourceSnapshot is { } avalonSource)
                    AddEvent("runes", item.Controller, "彼界 阿瓦隆在回合开始时获得1符文", avalonSource);
                else AddEvent("runes", item.Controller, "彼界 阿瓦隆在回合开始时获得1符文");
                break;
        }
        FinishStackItem(item);
        return true;
    }

    private void QueueFinnReadyAfterTrial(int playerIndex, L12CardInstance? source)
    {
        if (source?.CardId != TrialAdvanceFinnCardId) return;
        var player = State.Players[playerIndex];
        if (FindOnField(player, source.InstanceId, out _, out _) is null || !source.Tapped
            || player.SpecialZones.Runes < 1) return;
        QueueTriggerCandidates([CreateTriggerCandidate(playerIndex, source, "trial-advance-followup",
            "发动试炼后效果", new Dictionary<string, string> { ["ability"] = "finnReady" })]);
    }

    private void QueueAvalonTurnStart(int playerIndex)
    {
        var player = State.Players[playerIndex];
        var source = CreateCard("S02-06D1", $"master-{playerIndex}");
        QueueTriggerCandidates([CreateTriggerCandidate(playerIndex, source, "turn-start", "回合开始时效果",
            new Dictionary<string, string> { ["ability"] = "avalonTurnStart" })]);
    }
}
