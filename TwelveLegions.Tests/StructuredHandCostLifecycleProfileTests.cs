using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StructuredHandCostLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-0509:ability:static:fff4ed8e0ac25ed9", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S02-0510:ability:static:52b46f1b508e6aa1", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S02-0512:ability:static:fff4ed8e0ac25ed9", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S02-0518:ability:static:fff4ed8e0ac25ed9", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S02-0605:ability:continuous:5ff487de55c0ca1d", "effective-faction", "zero-floor")]
    [L12AbilityEvidence("S02-0611:ability:continuous:5745356459e85080", "condition-false", "source-still-in-hand")]
    [L12AbilityEvidence("S02-0612:ability:continuous:064a0a1c5382575c", "condition-false", "source-still-in-hand")]
    [L12AbilityEvidence("ST03-02:ability:continuous:057a02a660ebfae1", "condition-false", "source-still-in-hand")]
    [L12AbilityEvidence("ST04-10:ability:continuous:2a1c905931cd7b32", "condition-false", "source-still-in-hand")]
    [L12AbilityEvidence("ST06-01:ability:continuous:3ced1d4d38141877", "condition-false", "source-still-in-hand")]
    public void StructuredHandCostFamilyIsClosedOverTheSharedConsumer()
    {
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(EffectLifecycleProfiles.IsStructuredHandCostAbility)
            .Select(ability => ability.AbilityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(EffectLifecycleProfiles.StructuredHandCostAbilityIds.OrderBy(id => id, StringComparer.Ordinal), actual);
    }

    [Theory]
    [InlineData("S02-0509")]
    [InlineData("S02-0512")]
    [InlineData("S02-0518")]
    public void ZeroGodPowerDiscountTracksCurrentMoraleAndHandZone(string cardId)
    {
        var game = Create(70200 + cardId[^1]);
        var card = PutOnlyCardInHand(game, cardId);
        Assert.Equal(Math.Max(0, card.Cost - 1), HandCost(game, card));

        game.State.Players[0].Morale.Add(GodPower("god-power"));
        Assert.Equal(card.Cost, HandCost(game, card));

        game.State.Players[0].Morale.Clear();
        game.State.Players[0].Hand.Remove(card);
        game.State.Players[0].Field[0][0] = card;
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(game.State.Players[0], card));
    }

    [Fact]
    public void FiveGodPowerDiscountTracksThresholdAndNeverDisplaysNegativeCost()
    {
        var game = Create(70210);
        var card = PutOnlyCardInHand(game, "S02-0510");
        for (var i = 0; i < 4; i++) game.State.Players[0].Morale.Add(GodPower($"god-{i}"));
        Assert.Equal(card.Cost, HandCost(game, card));
        game.State.Players[0].Morale.Add(GodPower("god-4"));
        Assert.Equal(Math.Max(0, card.Cost - 3), HandCost(game, card));
        Assert.True(HandCost(game, card) >= 0);
    }

    [Theory]
    [InlineData("ST03-02", "", 1)]
    [InlineData("ST04-10", "S01-0403", 1)]
    [InlineData("ST06-01", "S02-0618", 2)]
    [InlineData("S02-0611", "S02-0612", 2)]
    [InlineData("S02-0612", "S02-0611", 2)]
    public void ThresholdAndNamedFieldConditionsRecalculateFromCurrentState(string cardId, string requiredCardId,
        int discount)
    {
        var game = Create(70220 + cardId[^1]);
        var card = PutOnlyCardInHand(game, cardId);
        if (cardId == "ST03-02")
        {
            game.State.Players[0].Hp = 8;
            Assert.Equal(card.Cost, HandCost(game, card));
            game.State.Players[0].Hp = 7;
        }
        else
        {
            Assert.Equal(card.Cost, HandCost(game, card));
            game.State.Players[0].Field[0][0] = Card(requiredCardId, $"field-{requiredCardId}");
        }
        Assert.Equal(Math.Max(0, card.Cost - discount), HandCost(game, card));

        game.State.Players[0].Hand.Remove(card);
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(game.State.Players[0], card));
    }

    [Fact]
    public void BorsCountsCurrentEffectiveOtherworldLegionsAndClampsDisplayAtZero()
    {
        var game = Create(70230);
        var player = game.State.Players[0];
        var bors = PutOnlyCardInHand(game, "S02-0605");
        player.Field[0][0] = Card("S02-0609", "otherworld-field");
        player.Field[0][1] = Card("S02-0004", "universal-field");
        Assert.Equal(Math.Max(0, bors.Cost - 1), HandCost(game, bors));

        player.Field[1][0] = Card("S02-0609", "otherworld-field-2");
        player.Field[1][1] = Card("S02-0609", "otherworld-field-3");
        player.Field[0][2] = Card("S02-0609", "otherworld-field-4");
        player.Field[1][2] = Card("S02-0609", "otherworld-field-5");
        bors.CostModifier = -bors.Cost;
        Assert.Equal(0, HandCost(game, bors));

        var mappedController = new L12PlayerState
        {
            Name = "彼界主宰", DeckName = "effective-faction", Faction = "otherworld", MasterId = "S02-06M1",
            MasterName = "莫瑞甘",
        };
        var mappedBors = Card("S02-0605", "mapped-bors");
        mappedController.Hand.Add(mappedBors);
        mappedController.Field[0][0] = Card("S02-0004", "mapped-universal");
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(mappedController, mappedBors));
        mappedController.ExtraRelics.Add(Card("S02-0008", "mapped-ring"));
        Assert.Equal(-1, L12StructuredCardRules.HandPlayCostModifier(mappedController, mappedBors));
    }

    [Fact]
    public void DisplayedDiscountAndActualPaymentUseTheSameCurrentCondition()
    {
        var success = Create(70240);
        var successPlayer = success.State.Players[0];
        var discounted = PutOnlyCardInHand(success, "ST03-02");
        successPlayer.Hp = 7;
        var displayedCost = HandCost(success, discounted);
        successPlayer.TemporaryMorale = displayedCost;

        var accepted = success.Handle(0, new L12Command("playCard", discounted.InstanceId, Row: 0, Slot: 0));
        Assert.True(accepted.Accepted, accepted.Error);
        Assert.Equal(0, successPlayer.TemporaryMorale);
        Assert.DoesNotContain(discounted, successPlayer.Hand);

        var stale = Create(70241);
        var stalePlayer = stale.State.Players[0];
        var staleCard = PutOnlyCardInHand(stale, "ST03-02");
        stalePlayer.Hp = 7;
        var staleDisplayedCost = HandCost(stale, staleCard);
        stalePlayer.TemporaryMorale = staleDisplayedCost;
        stalePlayer.Hp = 8;

        var rejected = stale.Handle(0, new L12Command("playCard", staleCard.InstanceId, Row: 0, Slot: 0));
        Assert.False(rejected.Accepted);
        Assert.Contains(staleCard, stalePlayer.Hand);
        Assert.Equal(staleDisplayedCost, stalePlayer.TemporaryMorale);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "structured-hand-cost", "HAND-COST", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12CardInstance PutOnlyCardInHand(L12GameEngine game, string cardId)
    {
        var player = game.State.Players[0];
        player.Hand.Clear();
        var card = Card(cardId, $"hand-{cardId}");
        player.Hand.Add(card);
        return card;
    }

    private static int HandCost(L12GameEngine game, L12CardInstance card)
    {
        var player = game.SnapshotFor(0).Players[0];
        var hand = Assert.IsType<L12CardInstance[]>(player.GetType().GetProperty("hand")!.GetValue(player));
        return Assert.Single(hand, view => view.InstanceId == card.InstanceId).PlayCost!.Value;
    }

    private static L12MoraleCard GodPower(string id)
        => new() { InstanceId = id, CardId = "S02-05C1", IsGodPower = true };

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            TrialValue = definition.TrialValue ?? 0,
        };
    }
}
