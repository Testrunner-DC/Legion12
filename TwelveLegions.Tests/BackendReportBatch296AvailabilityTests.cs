using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackendReportBatch296AvailabilityTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static L12GameEngine Create(string? faction = null, string disasterMode = "none")
    {
        var deck = Catalog.DeckAt(0);
        var firstDeck = faction is null ? deck : new L12PresetDeckDefinition
        {
            Name = "资源可用性回归",
            MasterId = Catalog.Cards.Values.First(card => card.CardType == "master" && card.Faction == faction).Id,
            CardIds = [.. deck.CardIds], MoraleIds = [.. deck.MoraleIds], SpecialIds = [.. deck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "availability296", "AV296", 29607,
            ["甲", "乙"], [firstDeck, deck], skipPreparation: true,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false, disasterMode: disasterMode);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        foreach (var p in game.State.Players)
        {
            p.Hand.Clear(); p.Morale.Clear(); p.Resolving.Clear(); p.TemporaryMorale = 0;
            p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }
    private static L12CardInstance Card(string id, string instance)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance, CardId = id, Name = d.NameZh, CardType = d.CardType,
            Faction = d.Faction, Cost = d.Cost ?? 0, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, EffectiveProfession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0,
        };
    }
    private static L12CardInstance HandCard(L12GameEngine game)
        => Assert.Single((L12CardInstance[])typeof(L12GameEngine).GetMethod("SnapshotHand",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [0])!);
    private static int Resources(L12GameEngine game, int seat, bool spectator = false)
        => JsonSerializer.SerializeToElement(spectator ? game.SnapshotForSpectator() : game.SnapshotFor(seat),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("players")[seat]
            .GetProperty("spendableResourceCount").GetInt32();

    [Theory]
    [InlineData(false, 0, 2)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 1, 0)]
    public void CounterPlacementSnapshotUsesActualSetCost(bool disaster, int freeCount, int expected)
    {
        var game = Create();
        var p = game.State.Players[0];
        var counter = Card("S01-0016", "counter"); p.Hand.Add(counter);
        p.FreeTacticCount = freeCount;
        if (disaster) game.State.ActiveDisaster = Card("S01-DS03", "disaster");
        var view = HandCard(game);
        Assert.Equal(expected, view.PlayCost);
        Assert.Equal(expected, view.MinimumPlayCost);
        var result = game.Handle(0, new L12Command("playCard", CardInstanceId: counter.InstanceId, Row: 1, Slot: 0));
        Assert.True(result.Accepted == (expected == 0), result.Error);
        Assert.Empty(p.Morale);
        Assert.Equal(expected == 0 ? 0 : 1, p.Hand.Count);
        if (expected == 0) Assert.Same(counter, p.Field[1][0]);
    }

    [Theory]
    [InlineData("tianting")]
    [InlineData("taiyangcheng")]
    public void OwnTurnPublicTombGuardCountsAndPaysForMovementRegardlessOfFaction(string faction)
    {
        var game = Create(faction); var p = game.State.Players[0];
        var mover = Card("S01-0101", "mover");
        var guard = Card("S01-0212", "guard");
        p.Field[0][0] = mover; p.Field[1][2] = guard;
        Assert.Equal(1, Resources(game, 0));
        Assert.Equal(1, Resources(game, 0, true));
        var result = game.Handle(0, new L12Command("move", CardInstanceId: mover.InstanceId,
            CardInstanceIds: [guard.InstanceId], Row: 0, Slot: 1));
        Assert.True(result.Accepted, result.Error);
        Assert.Same(mover, p.Field[0][1]); Assert.True(guard.Tapped);
        Assert.Equal(0, Resources(game, 0));
    }

    [Fact]
    public void RestedHiddenOrOpponentsTurnTombGuardIsNotAnAvailableResource()
    {
        var game = Create(); var p = game.State.Players[0];
        var guard = Card("S01-0212", "guard"); p.Field[0][0] = guard;
        guard.Tapped = true; Assert.Equal(0, Resources(game, 0));
        guard.Tapped = false; guard.Hidden = true; Assert.Equal(0, Resources(game, 0));
        guard.Hidden = false; game.State.ActivePlayer = 1; Assert.Equal(0, Resources(game, 0));
        p.TemporaryMorale = 1; Assert.Equal(1, Resources(game, 0));
    }

    [Fact]
    public void DisasterFreePlacementPreservesEffectCreditAndRejectsDuplicateSubmission()
    {
        var game = Create(); var p = game.State.Players[0];
        var counter = Card("S01-0016", "free-counter"); p.Hand.Add(counter);
        p.FreeTacticCount = 1;
        game.State.ActiveDisaster = Card("S01-DS03", "disaster");
        var command = new L12Command("playCard", CardInstanceId: counter.InstanceId, Row: 1, Slot: 0);
        Assert.True(game.Handle(0, command).Accepted);
        Assert.Equal(1, p.FreeTacticCount);
        Assert.False(game.Handle(0, command).Accepted);
        Assert.Equal(1, p.FreeTacticCount);
        Assert.Same(counter, p.Field[1][0]);
        var next = Card("S01-0018", "next-counter"); p.Hand.Add(next);
        game.State.ActiveDisaster = null;
        Assert.Equal(0, HandCard(game).PlayCost);
        Assert.True(game.Handle(0, new L12Command("playCard", CardInstanceId: next.InstanceId, Row: 1, Slot: 1)).Accepted);
        Assert.Equal(0, p.FreeTacticCount);
        p.Hand.Add(Card("S01-0016", "paid-counter"));
        Assert.Equal(2, HandCard(game).PlayCost);
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260908-d7f0d002")]
    [Trait("L12Evidence", "bug:BUG-20260908-ed8c5a1a")]
    public void RevealingCorruptEarthMidTurnRefreshesCounterCostAndAllowsZeroResourcePlacement()
    {
        var game = Create(disasterMode: "all");
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS03", "revealed-corrupt-earth"));
        var player = game.State.Players[0];
        var counter = Card("S01-0016", "counter-after-disaster-reveal");
        player.Hand.Add(counter);
        Assert.Equal(2, HandCard(game).PlayCost);
        Assert.Equal(0, Resources(game, 0));

        var reveal = game.HandleGm(new L12GmCommand("triggerDisaster"));
        Assert.True(reveal.Accepted, reveal.Error);
        Assert.Equal("S01-DS03", game.State.ActiveDisaster?.CardId);
        Assert.Contains(game.State.Events, entry => entry.Cards.Any(card =>
            card.InstanceId == "revealed-corrupt-earth"));
        Assert.Equal(0, HandCard(game).PlayCost);
        Assert.Equal(0, HandCard(game).MinimumPlayCost);
        Assert.Equal(0, Resources(game, 0));
        var command = new L12Command("playCard", CardInstanceId: counter.InstanceId, Row: 1, Slot: 0);
        var placed = game.Handle(0, command);
        Assert.True(placed.Accepted, placed.Error);
        Assert.Same(counter, player.Field[1][0]);
        Assert.Empty(player.Morale);
        Assert.False(game.Handle(0, command).Accepted);
    }

    [Fact]
    public void OrdinaryPlacementUsesSelectedTwoResourcesAndDoesNotChargeAgain()
    {
        var game = Create(); var p = game.State.Players[0];
        var counter = Card("S01-0016", "counter"); p.Hand.Add(counter);
        var guard = Card("S01-0212", "guard"); p.Field[0][0] = guard;
        var morale = new L12MoraleCard { InstanceId = "morale", CardId = "S01-01C1" }; p.Morale.Add(morale);
        Assert.Equal(2, Resources(game, 0));
        Assert.Equal(2, HandCard(game).MinimumPlayCost);
        var command = new L12Command("playCard", CardInstanceId: counter.InstanceId,
            CardInstanceIds: [guard.InstanceId, morale.InstanceId], Row: 1, Slot: 1);
        Assert.True(game.Handle(0, command).Accepted);
        Assert.True(guard.Tapped); Assert.True(morale.Tapped);
        Assert.Equal(0, Resources(game, 0));
        Assert.False(game.Handle(0, command).Accepted);
        Assert.Same(counter, p.Field[1][1]);
    }

    [Fact]
    public void EveryCounterTacticUsesTheSameNormalAndDisasterPlacementProjection()
    {
        var game = Create(); var p = game.State.Players[0];
        var classifier = typeof(L12GameEngine).GetMethod("IsCounterTactic", BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!;
        var counterIds = Catalog.Cards.Keys.Where(id => (bool)classifier.Invoke(game, [id])!).ToArray();
        Assert.NotEmpty(counterIds);
        foreach (var id in counterIds)
        {
            p.Hand.Clear(); p.Hand.Add(Card(id, $"pool-{id}"));
            game.State.ActiveDisaster = null;
            Assert.Equal(2, HandCard(game).MinimumPlayCost);
            game.State.ActiveDisaster = Card("S01-DS03", "disaster");
            Assert.Equal(0, HandCard(game).MinimumPlayCost);
        }
    }
}
