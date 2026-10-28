namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    internal const string AgreedDrawConclusionKind = "agreed-draw";

    internal void ConcludeAgreedDrawByAuthority(string reason)
        => ConcludeDrawByAuthority(reason, agreed: true);

    internal void ConcludeInactivityDrawByAuthority(string reason)
        => ConcludeDrawByAuthority(reason, agreed: false);

    private void ConcludeDrawByAuthority(string reason, bool agreed)
    {
        if (State.Phase == L12Phase.GameOver) return;
        reason = string.IsNullOrWhiteSpace(reason)
            ? agreed ? "双方同意平局" : "对局超过40分钟无活动，按平局结束" : reason.Trim();
        State.Winner = null;
        State.WinnerReason = reason;
        State.EndedByAgreedDraw = agreed;
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
