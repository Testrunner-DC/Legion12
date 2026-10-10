namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private bool CompleteLibrarySequence(L12PlayerState player, L12LibraryResult result,
        int requested, L12StackItem? origin, string source)
    {
        if (result.Success) return true;
        if (origin is not null) origin.Data["effectResultStatus"] = "failed";
        if (requested > 0) SetWinner(1 - player.PlayerIndex, $"{source}操作牌库时牌库为空");
        return false;
    }
}
