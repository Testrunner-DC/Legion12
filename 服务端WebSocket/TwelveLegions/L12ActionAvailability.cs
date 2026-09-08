namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    // Hand placement is not the activation cost printed in a counter's effect.
    // Both the authoritative hand preview and SetCounterTactic use this rule.
    private int CounterTacticPlacementCost(L12PlayerState player)
        => State.ActiveDisaster?.CardId == "S01-DS03" || player.FreeTacticCount > 0 ? 0 : 2;
}
