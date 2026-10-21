using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OasisDancerPrintedTroopsRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void PrintedTroopsMatchTheApprovedThreeThousandCardArt()
    {
        var definition = Catalog.Cards["ST02-05"];
        Assert.Equal(3000, definition.Troops);
        Assert.Equal(3, definition.Cost);
        Assert.Equal("taiyangcheng", definition.Faction);
        Assert.Equal("主动休整 我方所有【太阳城】军团，本回合兵力+1000。", definition.Effect);
    }

    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "none")]
    [InlineData(0, "hand")]
    [InlineData(1, "hand")]
    [InlineData(0, "entered")]
    [InlineData(1, "entered")]
    [InlineData(0, "pending")]
    [InlineData(1, "pending")]
    [InlineData(0, "settled")]
    [InlineData(1, "settled")]
    [Trait("L12Evidence", "card:ST02-05")]
    public void RealPlayAndActiveRestKeepPrintedTroopsAcrossTurnEndAndRecovery(int owner, string restoreAt)
    {
        var game = new L12GameEngine(Catalog, "oasis-printed", "OASIS-PRINTED", 2026102105 + owner,
            ["甲", "乙"], [Catalog.DeckAt(1), Catalog.DeckAt(1)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.ActivePlayer = owner;
        game.State.FirstPlayer = owner;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Removed.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.TemporaryMorale = 0;
            player.UsedAbilities.Clear();
            player.SpecialZones.Trials.Clear();
        }
        game.State.Players[owner].Hand.Add(Card("ST02-05", "oasis-source", owner));
        game.State.Players[owner].TemporaryMorale = 3;
        game.State.Players[owner].Field[0][1] = Card("ST02-09", "oasis-ally", owner);
        game.State.Players[owner].Field[0][2] = Card("ST01-09", "oasis-other-faction", owner);
        game.State.Players[1 - owner].Field[0][0] = Card("ST02-09", "oasis-opponent", 1 - owner);

        if (restoreAt == "hand") game = Restore(game);
        Send(game, owner, new("playCard", "oasis-source", Row: 0, Slot: 0));
        PassResponses(game);
        if (restoreAt == "entered") game = Restore(game);
        Assert.Equal(0, game.State.Players[owner].TemporaryMorale);
        Assert.Empty(game.State.Players[owner].Hand);
        AssertTroops(game, owner, 3000, 2000);
        Assert.False(game.State.Players[owner].Field[0][0]!.Tapped);

        Send(game, owner, new("activateAbility", "oasis-source", Ability: "oasisDancerBuff"));
        Assert.True(game.State.Players[owner].Field[0][0]!.Tapped);
        AssertTroops(game, owner, 3000, 2000);
        Assert.Single(game.State.EffectStack);
        if (restoreAt == "pending") game = Restore(game);
        PassResponses(game);
        if (restoreAt == "settled") game = Restore(game);
        AssertTroops(game, owner, 4000, 3000);
        Assert.True(game.State.Players[owner].Field[0][0]!.Tapped);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "oasis-source"));

        Send(game, owner, new("endTurn"));
        PassResponses(game);
        Assert.Equal(1 - owner, game.State.ActivePlayer);
        AssertTroops(game, owner, 3000, 2000);
        game = Restore(game);
        AssertTroops(game, owner, 3000, 2000);
    }

    private static void AssertTroops(L12GameEngine game, int owner, int sourceTroops, int allyTroops)
    {
        var player = game.State.Players[owner];
        Assert.Equal(3000, player.Field[0][0]!.BaseTroops);
        Assert.Equal(sourceTroops, player.Field[0][0]!.CurrentTroops);
        Assert.Equal(allyTroops, player.Field[0][1]!.CurrentTroops);
        Assert.Equal(2000, player.Field[0][2]!.CurrentTroops);
        Assert.Equal(2000, game.State.Players[1 - owner].Field[0][0]!.CurrentTroops);
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(
        Catalog, game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void Send(L12GameEngine game, int owner, L12Command command)
    {
        var result = game.Handle(owner, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Send(game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }
}
