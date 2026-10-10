using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterRottenEarthLiveLifecycleTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "rotten-earth-live", "ROTTENEARTH", 100302,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            disasterMode: "all", stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterValue = 8;
        game.State.DisasterDeck.Clear();
        game.State.RemovedDisasters.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix = "base")
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "ST05-C1",
                InstanceId = $"rotten-earth-morale-{prefix}-{index}",
            });
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static L12CardInstance HandCard(L12GameEngine game, string instanceId)
        => ((L12CardInstance[])typeof(L12GameEngine).GetMethod("SnapshotHand",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [0])!)
            .Single(card => card.InstanceId == instanceId);

    public static IEnumerable<object[]> CounterTactics()
        => Catalog.Cards.Values.Where(card => card.IsCounterTactic)
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .Select(card => new object[] { card.Id });

    [Fact]
    [Trait("L12Evidence", "card:S01-DS03")]
    [Trait("L12Evidence", "entry:natural-reveal-live-continuous-rules")]
    [Trait("L12Evidence", "reconnect:v2-disaster-trigger-and-active-state")]
    public void NaturalRottenEarthRevealClearsRearLegionsBlocksNewOnesAndSetsCountersForFree()
    {
        var game = Create();
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var triggerLegion = Card("ST05-02", "rotten-earth-natural-trigger", 0);
        var blockedRearLegion = Card("ST03-09", "rotten-earth-blocked-rear-legion", 0);
        var freeCounter = Card("S01-0018", "rotten-earth-free-counter", 0);
        var friendlyRear = Card("ST03-06", "rotten-earth-friendly-rear", 0);
        var enemyRear = Card("ST04-01", "rotten-earth-enemy-rear", 1);
        player.Field[1][0] = friendlyRear;
        opponent.Field[1][0] = enemyRear;
        player.Hand.AddRange([triggerLegion, blockedRearLegion, freeCounter]);
        AddReadyMorale(player, triggerLegion.Cost);
        game.State.DisasterDeck.Add(Card("S01-DS03", "naturally-revealed-rotten-earth", 0));

        var played = game.Handle(0,
            new L12Command("playCard", triggerLegion.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.Equal("S01-DS03", game.State.ActiveDisaster?.CardId);
        Assert.Contains(game.State.Events, entry => entry.Type == "disaster-trigger-source"
            && entry.Text.Contains("军团登场或卡牌效果", StringComparison.Ordinal));
        Assert.Contains(player.Graveyard, card => card.InstanceId == friendlyRear.InstanceId);
        Assert.Contains(opponent.Graveyard, card => card.InstanceId == enemyRear.InstanceId);
        Assert.Null(player.Field[1][0]);
        Assert.Null(opponent.Field[1][0]);
        Assert.Empty(game.State.EffectStack);

        game = Restore(game);
        player = game.State.Players[0];
        opponent = game.State.Players[1];

        Assert.Equal("S01-DS03", game.State.ActiveDisaster?.CardId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == friendlyRear.InstanceId);
        Assert.Contains(opponent.Graveyard, card => card.InstanceId == enemyRear.InstanceId);
        Assert.Null(player.Field[1][0]);
        Assert.Null(opponent.Field[1][0]);
        Assert.DoesNotContain(player.Morale, card => !card.Tapped);

        game = Restore(game);
        player = game.State.Players[0];
        var beforeBlockedPlay = game.SerializeFullState();
        var blocked = game.Handle(0,
            new L12Command("playCard", blockedRearLegion.InstanceId, Row: 1, Slot: 0));
        Assert.False(blocked.Accepted);
        Assert.Contains("腐秽大地", blocked.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(beforeBlockedPlay, game.SerializeFullState());

        var set = game.Handle(0,
            new L12Command("playCard", freeCounter.InstanceId, Row: 1, Slot: 0));
        Assert.True(set.Accepted, set.Error);
        var covered = Assert.IsType<L12CardInstance>(player.Field[1][0]);
        Assert.Equal(freeCounter.InstanceId, covered.InstanceId);
        Assert.True(covered.Hidden);
        Assert.DoesNotContain(player.Morale, card => !card.Tapped);

        AddReadyMorale(player, blockedRearLegion.Cost, "front-entry");
        var front = game.Handle(0,
            new L12Command("playCard", blockedRearLegion.InstanceId, Row: 0, Slot: 1));
        Assert.True(front.Accepted, front.Error);
        Assert.Equal(blockedRearLegion.InstanceId,
            Assert.IsType<L12CardInstance>(player.Field[0][1]).InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-DS03")]
    [Trait("L12Evidence", "audit:all-counter-tactics")]
    public void AuditedCounterTacticPoolContainsEighteenCards()
        => Assert.Equal(18, CounterTactics().Count());

    [Theory]
    [MemberData(nameof(CounterTactics))]
    [Trait("L12Evidence", "card:S01-DS03")]
    [Trait("L12Evidence", "entry:natural-reveal-all-counters-free-cover")]
    [Trait("L12Evidence", "reconnect:v2-counter-cost-projection")]
    public void EveryCounterCanBeCoveredInTheRearForFreeAfterNaturalRottenEarthReveal(string counterCardId)
    {
        var game = Create();
        var player = game.State.Players[0];
        var trigger = Card("ST05-02", $"natural-trigger-{counterCardId}", 0);
        var counter = Card(counterCardId, $"free-counter-{counterCardId}", 0);
        player.Hand.AddRange([trigger, counter]);
        AddReadyMorale(player, trigger.Cost, counterCardId);
        game.State.DisasterDeck.Add(Card("S01-DS03", $"natural-rotten-{counterCardId}", 0));

        var reveal = game.Handle(0,
            new L12Command("playCard", trigger.InstanceId, Row: 0, Slot: 0));
        Assert.True(reveal.Accepted, reveal.Error);
        Assert.Equal("S01-DS03", game.State.ActiveDisaster?.CardId);

        game = Restore(game);
        player = game.State.Players[0];
        var projected = HandCard(game, counter.InstanceId);
        Assert.Equal(0, projected.PlayCost);
        Assert.Equal(0, projected.MinimumPlayCost);
        Assert.DoesNotContain(player.Morale, card => !card.Tapped);

        var illegalFront = game.SerializeFullState();
        var front = game.Handle(0,
            new L12Command("playCard", counter.InstanceId, Row: 0, Slot: 1));
        Assert.False(front.Accepted);
        Assert.Equal(illegalFront, game.SerializeFullState());

        var rear = game.Handle(0,
            new L12Command("playCard", counter.InstanceId, Row: 1, Slot: 0));
        Assert.True(rear.Accepted, rear.Error);
        var covered = Assert.IsType<L12CardInstance>(player.Field[1][0]);
        Assert.Equal(counter.InstanceId, covered.InstanceId);
        Assert.True(covered.Hidden);
        Assert.DoesNotContain(player.Morale, card => !card.Tapped);
    }
}
