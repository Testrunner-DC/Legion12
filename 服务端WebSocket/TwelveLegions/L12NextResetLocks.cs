namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static void LockUntilNextReset(L12CardInstance card)
        => card.CannotUntapAtNextReset = true;

    private static void LockUntilNextReset(L12MoraleCard card)
        => card.CannotUntapAtNextReset = true;

    private bool CanUntapDuringReset(L12CardInstance card)
    {
        var blockedAtThisReset = card.CannotUntapAtNextReset;
        card.CannotUntapAtNextReset = false;
        return !blockedAtThisReset && card.CannotUntapUntilRound < State.Round;
    }

    private bool CanUntapDuringReset(L12MoraleCard card)
    {
        var blockedAtThisReset = card.CannotUntapAtNextReset;
        card.CannotUntapAtNextReset = false;
        return !blockedAtThisReset && card.CannotUntapUntilRound < State.Round;
    }
}
