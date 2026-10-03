using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S02FactionCountCostAuditTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void NativeOtherworldLegionReducesBorsAuthoritativePlayCostByOne()
    {
        var game = Create(95700);
        var player = game.State.Players[0];
        player.Field[0][0] = Card("S02-0609", "bors-native-otherworld-legion", player.PlayerIndex);
        var bors = PutBorsInHand(player, "bors-native-cost");

        Assert.Equal(5, HandCost(game, bors));
        player.TemporaryMorale = 5;
        var result = PlayBors(game, bors);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Same(bors, player.Field[0][2]);
    }

    [Fact]
    public void CoveredUniversalCounterDoesNotReduceBorsCostWithoutTheRing()
    {
        var game = Create(95701);
        var player = game.State.Players[0];
        var counter = SetCoveredUniversalCounter(game, "bors-covered-no-ring");
        var bors = PutBorsInHand(player, "bors-covered-no-ring-cost");

        Assert.True(counter.Hidden);
        Assert.Equal("universal", L12StructuredCardRules.EffectiveFaction(player, counter));
        Assert.Equal(6, HandCost(game, bors));
        player.TemporaryMorale = 6;
        var result = PlayBors(game, bors);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Same(bors, player.Field[0][2]);
    }

    [Fact]
    public void RingLegallyMapsAUniversalLegionForBorsDiscount()
    {
        var game = Create(95702);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0008", "bors-ring", player.PlayerIndex);
        var mappedLegion = Card("S02-0004", "bors-ring-universal-legion", player.PlayerIndex);
        player.Field[0][0] = mappedLegion;
        var bors = PutBorsInHand(player, "bors-ring-legion-cost");

        Assert.Contains("所有【通用】卡牌都视为与我方主宰阵营相同", player.Relic.EffectText,
            StringComparison.Ordinal);
        Assert.Contains("每存在1张【彼界】军团", bors.EffectText, StringComparison.Ordinal);
        Assert.Equal("otherworld", L12StructuredCardRules.EffectiveFaction(player, mappedLegion));
        Assert.Equal(5, HandCost(game, bors));
        player.TemporaryMorale = 5;
        var result = PlayBors(game, bors);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Same(bors, player.Field[0][2]);
    }

    [Fact]
    public void BorsCountsOnlyCurrentEffectiveOtherworldLegionsAcrossMultipleLegionsAndLeave()
    {
        var game = Create(95706);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0008", "bors-ring-multiple", player.PlayerIndex);
        var nativeOtherworld = Card("S02-0609", "bors-multiple-native", player.PlayerIndex);
        var mappedUniversal = Card("S02-0004", "bors-multiple-mapped", player.PlayerIndex);
        var wrongFaction = Card("S02-0501", "bors-multiple-wrong-faction", player.PlayerIndex);
        player.Field[0][0] = nativeOtherworld;
        player.Field[0][1] = mappedUniversal;
        player.Field[1][0] = wrongFaction;
        var bors = PutBorsInHand(player, "bors-multiple-cost");

        Assert.Equal("otherworld", L12StructuredCardRules.EffectiveFaction(player, nativeOtherworld));
        Assert.Equal("otherworld", L12StructuredCardRules.EffectiveFaction(player, mappedUniversal));
        Assert.Equal("olympus", L12StructuredCardRules.EffectiveFaction(player, wrongFaction));
        Assert.Equal(4, HandCost(game, bors));

        player.Field[0][0] = null;
        Assert.Equal(5, HandCost(game, bors));

        player.Field[0][1] = null;
        Assert.Equal(6, HandCost(game, bors));
    }

    [Fact]
    public void EnemyFaceUpTrojanHorseIsNotAFriendlyOtherworldLegionForBorsCost()
    {
        var game = Create(95703);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0008", "bors-ring-with-enemy-horse", player.PlayerIndex);
        var horse = Card("S02-0523", "bors-enemy-face-up-horse", owner: 1);
        horse.Hidden = false;
        horse.DiscardAtEndOfTurnUntilTurn = game.State.TurnSerial + 1;
        player.Field[1][1] = horse;
        var bors = PutBorsInHand(player, "bors-enemy-horse-cost");

        game = RestoreV2(game);
        player = game.State.Players[0];
        horse = Assert.Single(player.Field.SelectMany(row => row),
            card => card?.InstanceId == horse.InstanceId)!;
        bors = Assert.Single(player.Hand, card => card.InstanceId == bors.InstanceId);
        Assert.False(horse.Hidden);
        Assert.Equal(1, horse.OwnerIndex);
        Assert.Equal("olympus", L12StructuredCardRules.EffectiveFaction(player, horse));
        Assert.Equal(6, HandCost(game, bors));
        player.TemporaryMorale = 6;
        var result = PlayBors(game, bors);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Same(bors, player.Field[0][2]);
    }

    [Fact]
    public void RingMustNotTurnACoveredUniversalCounterIntoABorsLegionAcrossV2()
    {
        var game = Create(95704);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0008", "bors-ring-covered", player.PlayerIndex);
        var counter = SetCoveredUniversalCounter(game, "bors-ring-covered-counter");
        var bors = PutBorsInHand(player, "bors-ring-covered-cost");
        var costBeforeRestore = HandCost(game, bors);

        game = RestoreV2(game);
        player = game.State.Players[0];
        counter = Assert.Single(player.Field.SelectMany(row => row),
            card => card?.InstanceId == counter.InstanceId)!;
        bors = Assert.Single(player.Hand, card => card.InstanceId == bors.InstanceId);
        var costAfterRestore = HandCost(game, bors);

        Assert.True(counter.Hidden);
        Assert.Equal("tactic", counter.CardType);
        Assert.Equal("otherworld", L12StructuredCardRules.EffectiveFaction(player, counter));
        Assert.Equal(costBeforeRestore, costAfterRestore);
        Assert.Equal(6, costAfterRestore);
    }

    [Fact]
    public void RingCoveredCounterDoesNotAuthorizeUnderpayingBorsThroughPlayCardAfterV2()
    {
        var game = Create(95705);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0008", "bors-ring-payment", player.PlayerIndex);
        _ = SetCoveredUniversalCounter(game, "bors-ring-payment-counter");
        var bors = PutBorsInHand(player, "bors-ring-payment-cost");

        game = RestoreV2(game);
        player = game.State.Players[0];
        bors = Assert.Single(player.Hand, card => card.InstanceId == bors.InstanceId);
        player.TemporaryMorale = 5;

        var underpaid = PlayBors(game, bors);

        Assert.False(underpaid.Accepted);
        Assert.Contains(bors, player.Hand);
        Assert.Equal(5, player.TemporaryMorale);

        player.TemporaryMorale++;
        var paid = PlayBors(game, bors);
        Assert.True(paid.Accepted, paid.Error);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Same(bors, player.Field[0][2]);
    }

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var otherworldDeck = new L12PresetDeckDefinition
        {
            Name = "鲍斯费用审计",
            MasterId = "S02-06M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "s02-faction-count-cost-audit", "S02-COST-AUDIT", seed,
            ["甲", "乙"], [otherworldDeck, baseDeck], skipPreparation: true,
            stateFormatVersion: 2, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false, disasterMode: "none");
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        Assert.Equal("otherworld", game.State.Players[0].Faction);
        return game;
    }

    private static L12CardInstance SetCoveredUniversalCounter(L12GameEngine game, string instanceId)
    {
        var player = game.State.Players[0];
        player.Hand.Clear();
        var counter = Card("S02-0015", instanceId, player.PlayerIndex);
        player.Hand.Add(counter);
        player.Morale.AddRange(
        [
            new L12MoraleCard { CardId = "S02-06C1", InstanceId = $"{instanceId}-morale-1" },
            new L12MoraleCard { CardId = "S02-06C1", InstanceId = $"{instanceId}-morale-2" },
        ]);

        var set = game.Handle(player.PlayerIndex,
            new L12Command("playCard", counter.InstanceId, Row: 1, Slot: 0));

        Assert.True(set.Accepted, set.Error);
        Assert.Same(counter, player.Field[1][0]);
        Assert.True(counter.Hidden);
        Assert.Equal(player.PlayerIndex, counter.OwnerIndex);
        Assert.DoesNotContain(player.Morale, morale => !morale.Tapped);
        return counter;
    }

    private static L12CardInstance PutBorsInHand(L12PlayerState player, string instanceId)
    {
        player.Hand.Clear();
        var bors = Card("S02-0605", instanceId, player.PlayerIndex);
        player.Hand.Add(bors);
        return bors;
    }

    private static CommandResult PlayBors(L12GameEngine game, L12CardInstance bors)
        => game.Handle(0, new L12Command("playCard", bors.InstanceId, Row: 0, Slot: 2));

    private static int HandCost(L12GameEngine game, L12CardInstance card)
    {
        var player = game.SnapshotFor(0).Players[0];
        var hand = Assert.IsType<L12CardInstance[]>(player.GetType().GetProperty("hand")!.GetValue(player));
        return Assert.Single(hand, view => view.InstanceId == card.InstanceId).PlayCost!.Value;
    }

    private static L12GameEngine RestoreV2(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int? owner)
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            OwnerIndex = owner,
        };
    }
}
