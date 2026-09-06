namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    internal const string AgreedDrawConclusionKind = "agreed-draw";

    internal void ConcludeAgreedDrawByAuthority(string reason)
    {
        if (State.Phase == L12Phase.GameOver) return;
        reason = string.IsNullOrWhiteSpace(reason) ? "双方同意平局" : reason.Trim();
        State.Winner = null;
        State.WinnerReason = reason;
        State.Phase = L12Phase.GameOver;
        State.PendingDefense = null;
        State.SuspendedCombatContexts.Clear();
        State.PendingPrompts.Clear();
        State.EffectStack.Clear();
        State.DeferredEffectStack.Clear();
        State.IsResolvingStack = false;
        State.ResponseWindow = null;
        AddEvent("game-draw", null, reason);
        State.Revision++;
    }
}
