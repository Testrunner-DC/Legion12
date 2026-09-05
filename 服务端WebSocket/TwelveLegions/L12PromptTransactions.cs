namespace TwelveLegions.Server;

/// <summary>
/// Prompt 与 PendingActivation 的权威事务绑定及边界自愈。这里只校验创建身份，
/// CreatedRevision 不与当前 State.Revision 比较，因此合法多步骤流程不会因正常修订增长失效。
/// </summary>
public sealed partial class L12GameEngine
{
    private bool TryGetBoundPendingActivation(L12Prompt prompt, L12Command command,
        out L12PendingActivation activation, out string error)
    {
        activation = null!;
        if (!HasCompleteActivationBinding(prompt))
        {
            error = "选择事务绑定缺失或已失效";
            return false;
        }
        var matches = State.PendingActivations
            .Where(candidate => candidate.ActivationId == prompt.ActivationId)
            .ToArray();
        if (matches.Length != 1 || !PromptMatchesActivation(prompt, matches[0]))
        {
            error = "选择事务绑定与待处理效果不一致";
            return false;
        }
        var siblingPrompts = State.PendingPrompts.Count(candidate =>
            candidate.Continuation.Equals("pending-activation", StringComparison.OrdinalIgnoreCase)
            && candidate.ActivationId == prompt.ActivationId);
        if (siblingPrompts != 1)
        {
            error = "选择事务存在重复或冲突提示";
            return false;
        }
        if (!IsPendingActivationSourceValid(matches[0]))
        {
            error = "选择事务来源或上游状态已失效";
            return false;
        }
        if (!CommandBindingMatchesPrompt(command, prompt, out error)) return false;
        activation = matches[0];
        error = string.Empty;
        return true;
    }

    private static bool HasCompleteActivationBinding(L12Prompt prompt)
        => !string.IsNullOrWhiteSpace(prompt.ActivationId)
            && !string.IsNullOrWhiteSpace(prompt.SourceInstanceId)
            && !string.IsNullOrWhiteSpace(prompt.SourceCardId)
            && prompt.Step is >= 0
            && prompt.CreatedRevision is >= 0
            && prompt.Controller is >= 0 and <= 1;

    private static bool PromptMatchesActivation(L12Prompt prompt, L12PendingActivation activation)
        => prompt.Data.TryGetValue("activationId", out var dataActivationId)
            && dataActivationId == activation.ActivationId
            && prompt.ActivationId == activation.ActivationId
            && prompt.SourceInstanceId == activation.SourceInstanceId
            && prompt.SourceCardId == activation.SourceCardId
            && prompt.Step == activation.CurrentStep
            && prompt.CreatedRevision == activation.CreatedRevision
            && prompt.Controller == activation.Controller
            && prompt.PlayerIndex == activation.Controller
            && activation.CurrentStep >= 0
            && activation.CurrentStep < activation.SelectionSteps.Count;

    private static bool CommandBindingMatchesPrompt(L12Command command, L12Prompt prompt, out string error)
    {
        var supplied = command.ActivationId is not null || command.SourceInstanceId is not null
            || command.SourceCardId is not null || command.Step is not null
            || command.CreatedRevision is not null || command.Controller is not null;
        if (!supplied)
        {
            // 旧客户端兼容：省略回显字段仍只能通过服务端内部的完整 Prompt↔Activation 校验。
            error = string.Empty;
            return true;
        }
        var complete = command.ActivationId is not null && command.SourceInstanceId is not null
            && command.SourceCardId is not null && command.Step is not null
            && command.CreatedRevision is not null && command.Controller is not null;
        if (!complete)
        {
            error = "客户端选择事务绑定不完整";
            return false;
        }
        if (command.ActivationId != prompt.ActivationId
            || command.SourceInstanceId != prompt.SourceInstanceId
            || command.SourceCardId != prompt.SourceCardId
            || command.Step != prompt.Step
            || command.CreatedRevision != prompt.CreatedRevision
            || command.Controller != prompt.Controller)
        {
            error = "客户端选择事务绑定已陈旧或不匹配";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool ReconcilePendingActivationTransactions()
    {
        var changed = false;
        var activationSnapshot = State.PendingActivations.ToArray();
        foreach (var activation in activationSnapshot)
        {
            if (!State.PendingActivations.Contains(activation)) continue;
            var activationCount = State.PendingActivations.Count(candidate =>
                candidate.ActivationId == activation.ActivationId);
            var prompts = State.PendingPrompts.Where(prompt =>
                prompt.Continuation.Equals("pending-activation", StringComparison.OrdinalIgnoreCase)
                && (prompt.ActivationId == activation.ActivationId
                    || prompt.Data.GetValueOrDefault("activationId") == activation.ActivationId))
                .ToArray();
            if (activationCount == 1 && prompts.Length == 1 && PromptMatchesActivation(prompts[0], activation)
                && IsPendingActivationSourceValid(activation)) continue;

            foreach (var prompt in prompts) State.PendingPrompts.Remove(prompt);
            RejectPendingActivation(activation,
                "待处理选择事务的提示、来源或步骤已失效；已安全取消且未继续结算");
            changed = true;
        }

        foreach (var prompt in State.PendingPrompts.Where(prompt =>
                     prompt.Continuation.Equals("pending-activation", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var matches = State.PendingActivations.Count(activation =>
                activation.ActivationId == prompt.ActivationId && PromptMatchesActivation(prompt, activation));
            if (matches == 1) continue;
            State.PendingPrompts.Remove(prompt);
            AddEvent("prompt-orphan-cleared", prompt.PlayerIndex,
                "已清理无法对应合法待处理效果的选择提示");
            changed = true;
        }
        return changed;
    }

    private bool IsPendingActivationSourceValid(L12PendingActivation activation)
    {
        if (activation.Controller is < 0 or > 1
            || string.IsNullOrWhiteSpace(activation.ActivationId)
            || string.IsNullOrWhiteSpace(activation.SourceInstanceId)
            || string.IsNullOrWhiteSpace(activation.SourceCardId)
            || activation.CurrentStep < 0 || activation.CurrentStep >= activation.SelectionSteps.Count)
            return false;

        if (activation.TriggerCandidateId is not null)
            return State.PendingTriggerStackCandidates.Any(candidate =>
                candidate.CandidateId == activation.TriggerCandidateId
                && candidate.Controller == activation.Controller
                && candidate.SourceInstanceId == activation.SourceInstanceId
                && candidate.SourceCardId == activation.SourceCardId);

        if (activation.ResponseTargetStackItemId is not null)
            return State.EffectStack.Any(item => item.StackItemId == activation.ResponseTargetStackItemId)
                && FindAuthoritativeCard(activation.SourceInstanceId)?.CardId == activation.SourceCardId;

        // 手牌打出、效果生成打出和“仅再次发动效果”具有各自的提交期来源规则，
        // 其中后者可使用没有权威区域的虚拟来源；这里只绑定身份，提交函数继续复验真实区域。
        if (activation.PlayCardInstanceId is not null) return true;

        var player = State.Players[activation.Controller];
        var source = FindOnField(player, activation.SourceInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == activation.SourceInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId)
            ?? player.SpecialZones.Trials.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId)
            ?? player.Graveyard.FirstOrDefault(card => card.InstanceId == activation.SourceInstanceId
                && IsLegalGraveyardActiveAbilitySource(player, card, activation.Ability));
        if (source is not null) return source.CardId == activation.SourceCardId;
        if (activation.Ability == "destroyInfiltrator")
            return FindPublicCard(activation.SourceInstanceId, out _)?.CardId == activation.SourceCardId;
        if (player.Morale.Any(card => card.InstanceId == activation.SourceInstanceId)) return true;
        if (activation.SourceCardId == player.MasterId
            && activation.SourceInstanceId == $"master-{activation.Controller}") return true;
        return activation.SourceInstanceId == $"faction-{activation.Controller}";
    }
}
