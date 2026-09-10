namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    // Declaration and presentation share the same numeric quote. A quote never pays
    // resources; submission must recompute it after all selections are complete.
    private sealed record ActiveMoraleQuote(int BaseCost, int Surcharge, bool Waived)
    {
        public int Total => (Waived ? 0 : BaseCost) + Surcharge;
    }

    private ActiveMoraleQuote QuoteActiveMorale(L12PlayerState player,
        L12CardInstance source, string ability, string? target = null)
        => new(GetActiveAbilityMoraleCost(source, ability, target),
            State.ActiveDisaster?.CardId == "S02-DS06" && source.CardId == player.MasterId ? 1 : 0,
            source.CardId == player.MasterId && player.MasterMoraleWaiverUntilTurn >= State.TurnSerial);
}
