namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private L12StackItem? DeclaredResponseTimingTarget(L12StackItem response)
    {
        if (response.Data.TryGetValue("authorityTarget", out var declaredId))
            return State.EffectStack.FirstOrDefault(item => item.StackItemId == declaredId);
        // Older checkpoints did not pin the root; preserve their chain lookup,
        // but never redirect a newer declaration if its original root is gone.
        var target = State.EffectStack.FirstOrDefault(item => item.StackItemId == response.Targets.FirstOrDefault());
        return target is null ? null : ResponseTimingContext(target);
    }

    // Only committed conditional draw branches enter here. A declined branch is
    // omitted by the composite plan; it must not create a fictitious settlement.
    private void ResolveConditionalResponseDraw(L12StackItem item, bool conditionMet, string failureReason)
    {
        if (!conditionMet) RecordResolutionFailure(item, failureReason);
        else if (!Draw(State.Players[item.Controller], 1))
        {
            RecordResolutionFailure(item, "牌库数量不足，无法抽取1张牌");
            SetWinner(1 - item.Controller, $"〈{item.SourceName}〉抽牌时牌库为空");
        }
        FinishStackItem(item);
    }
}
