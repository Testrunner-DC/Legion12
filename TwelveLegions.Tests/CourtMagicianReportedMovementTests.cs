using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CourtMagicianReportedMovementTests
{
    private const string MagicianId = "reported-court-magician";
    private const string LuBuId = "reported-lu-bu";
    private const string FirstPaymentId = "reported-move-payment-a";
    private const string SecondPaymentId = "reported-move-payment-b";
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, 0, 1, false)]
    [InlineData(0, 1, 2, false)]
    [InlineData(1, 0, 1, false)]
    [InlineData(1, 1, 2, false)]
    [InlineData(0, 0, 1, true)]
    [InlineData(0, 1, 2, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(1, 1, 2, true)]
    public void ReportedBoardMovesCourtMagicianToEachEmptyAdjacentSquare(
        int actor, int targetRow, int targetSlot, bool restorePaymentPrompt)
    {
        var game = CreateReportedBoard(actor);

        var begin = game.Handle(actor, new L12Command("move", MagicianId,
            Row: targetRow, Slot: targetSlot));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", prompt.Kind);
        Assert.Equal("move-morale-choice", prompt.Continuation);
        Assert.Contains(FirstPaymentId, prompt.ValidChoices);
        Assert.Contains(SecondPaymentId, prompt.ValidChoices);
        Assert.Null(game.State.Players[actor].Field[targetRow][targetSlot]);

        if (restorePaymentPrompt)
            game = Restore(game);

        prompt = Assert.Single(game.State.PendingPrompts);
        var payment = game.Handle(actor, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, CardInstanceIds: [FirstPaymentId]));
        Assert.True(payment.Accepted, payment.Error);

        var player = game.State.Players[actor];
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(player.Field[1][1]);
        var magician = Assert.IsType<L12CardInstance>(player.Field[targetRow][targetSlot]);
        Assert.Equal(MagicianId, magician.InstanceId);
        Assert.False(magician.Tapped);
        Assert.Equal(game.State.TurnSerial, magician.LastMovedTurn);
        Assert.Equal(LuBuId, player.Field[1][0]?.InstanceId);
        Assert.True(player.Morale.Single(card => card.InstanceId == FirstPaymentId).Tapped);
        Assert.False(player.Morale.Single(card => card.InstanceId == SecondPaymentId).Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "move"
            && entry.Cards.Any(card => card.InstanceId == MagicianId));
        AssertReportedOpponentBoard(game, actor);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void OccupiedDestinationAndRestedCourtMagicianRejectBeforePayment(
        int actor, bool restore)
    {
        var game = CreateReportedBoard(actor);
        if (restore)
            game = Restore(game);

        var player = game.State.Players[actor];
        var occupiedBefore = game.SerializeFullState();
        var occupied = game.Handle(actor, new L12Command("move", MagicianId,
            Row: 1, Slot: 0, CardInstanceIds: [FirstPaymentId]));
        Assert.False(occupied.Accepted);
        Assert.Contains("占用", occupied.Error, StringComparison.Ordinal);
        Assert.Equal(occupiedBefore, game.SerializeFullState());

        var magician = Assert.IsType<L12CardInstance>(player.Field[1][1]);
        magician.Tapped = true;
        var restedBefore = game.SerializeFullState();
        var rested = game.Handle(actor, new L12Command("move", MagicianId,
            Row: 0, Slot: 1, CardInstanceIds: [FirstPaymentId]));
        Assert.False(rested.Accepted);
        Assert.Contains("活跃", rested.Error, StringComparison.Ordinal);
        Assert.Equal(restedBefore, game.SerializeFullState());

        Assert.Empty(game.State.PendingPrompts);
        Assert.False(player.Morale.Single(card => card.InstanceId == FirstPaymentId).Tapped);
        Assert.False(player.Morale.Single(card => card.InstanceId == SecondPaymentId).Tapped);
        Assert.Equal(LuBuId, player.Field[1][0]?.InstanceId);
        Assert.Equal(MagicianId, player.Field[1][1]?.InstanceId);
        Assert.Null(player.Field[0][1]);
        AssertReportedOpponentBoard(game, actor);
    }

    private static L12GameEngine CreateReportedBoard(int actor)
    {
        var game = new L12GameEngine(Catalog, "court-magician-reported-movement",
            "COURT-MOVE", 102019 + actor, ["甲", "乙"], [0, 0],
            skipPreparation: true, disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        foreach (var participant in game.State.Players)
        {
            participant.Hand.Clear();
            participant.Morale.Clear();
            participant.TemporaryMorale = 0;
            participant.Relic = null;
            participant.ExtraRelics.Clear();
            foreach (var row in participant.Field) Array.Clear(row);
        }

        var player = game.State.Players[actor];
        player.Field[1][0] = Card("S01-0101", LuBuId, actor);
        player.Field[1][1] = Card("S02-0003", MagicianId, actor);
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = FirstPaymentId,
            CardId = "S01-01C1",
        });
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = SecondPaymentId,
            CardId = "S01-01C1",
            IsGodPower = true,
        });

        var opponent = game.State.Players[1 - actor];
        opponent.Field[0][0] = Card("S01-0405", "reported-miyamoto", 1 - actor);
        opponent.Relic = Card("S01-0417", "reported-kusanagi", 1 - actor);
        AssertReportedOpponentBoard(game, actor);
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static void AssertReportedOpponentBoard(L12GameEngine game, int actor)
    {
        var opponent = game.State.Players[1 - actor];
        var miyamoto = Assert.IsType<L12CardInstance>(opponent.Field[0][0]);
        Assert.Equal("S01-0405", miyamoto.CardId);
        Assert.Equal("legion", miyamoto.CardType);
        var kusanagi = Assert.IsType<L12CardInstance>(opponent.Relic);
        Assert.Equal("S01-0417", kusanagi.CardId);
        Assert.Equal("artifact", kusanagi.CardType);
    }
}
