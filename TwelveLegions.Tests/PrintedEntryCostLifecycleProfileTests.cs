using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PrintedEntryCostLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0104:ability:static:a91d7d481db612a9", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S01-0114:ability:static:a91d7d481db612a9", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S01-0301:ability:static:71dd875155781eb0", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S01-0302:ability:static:acc29b0ca499d087", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S01-0305:ability:static:9ed1ca8df2e5f029", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S01-0306:ability:static:9ed1ca8df2e5f029", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S02-0202:ability:continuous:94759febdd62fd32", "condition-false", "display-and-payment-parity")]
    [L12AbilityEvidence("S02-0203:ability:continuous:418e71545576e12d", "condition-false", "display-and-payment-parity")]
    public void PrintedEntryCostDefinitionsMatchTheReviewedCardFamily()
    {
        var expected = EffectLifecycleProfiles.PrintedEntryCostAbilityIds
            .Select(id => id[..id.IndexOf(":ability:", StringComparison.Ordinal)])
            .Concat(["S01-0107", "S01-0202"])
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var actual = Catalog.Cards.Keys
            .Where(id => L12StructuredCardSemantics.PrintedEntryCostRule(id) is not null)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("S01-0104")]
    [InlineData("S01-0114")]
    [InlineData("S01-0301")]
    [InlineData("S01-0302")]
    [InlineData("S01-0305")]
    [InlineData("S01-0306")]
    [InlineData("S02-0202")]
    [InlineData("S02-0203")]
    [L12AbilityEvidence("S01-0104:ability:static:a91d7d481db612a9", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0114:ability:static:a91d7d481db612a9", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0301:ability:static:71dd875155781eb0", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0302:ability:static:acc29b0ca499d087", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0305:ability:static:9ed1ca8df2e5f029", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0306:ability:static:9ed1ca8df2e5f029", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0202:ability:continuous:94759febdd62fd32", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0203:ability:continuous:418e71545576e12d", "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    public void DiscountedSnapshotCostIsTheRecoveredAuthoritativePayment(string cardId)
    {
        var game = Create(70160 + cardId[^1]);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        var card = PutOnlyCardInHand(game, cardId);
        SatisfyPrintedDiscountCondition(game, cardId);
        var displayedCost = HandCost(game, card);
        Assert.InRange(displayedCost, 0, Math.Max(0, card.Cost - 1));
        var player = game.State.Players[0];
        player.Morale.Clear();
        for (var index = 0; index < displayedCost; index++)
            player.Morale.Add(Morale($"payment-{cardId}-{index}"));
        if (cardId is "S01-0104" or "S01-0114")
        {
            game.State.Players[1].Morale.Clear();
            for (var index = 0; index <= displayedCost; index++)
                game.State.Players[1].Morale.Add(Morale($"opponent-ahead-{index}"));
        }

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        card = Assert.Single(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.Equal(displayedCost, HandCost(game, card));
        var paymentIds = player.Morale.Where(morale => !morale.Tapped)
            .Take(displayedCost).Select(morale => morale.InstanceId).ToList();
        var command = new L12Command("playCard", card.InstanceId,
            CardInstanceIds: paymentIds, Row: 0, Slot: 1);

        var result = game.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        Assert.All(player.Morale.Where(morale => paymentIds.Contains(morale.InstanceId)), morale => Assert.True(morale.Tapped));
        Assert.DoesNotContain(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.False(game.Handle(0, command).Accepted);
    }

    [Theory]
    [InlineData("S01-0104")]
    [InlineData("S01-0107")]
    [InlineData("S01-0114")]
    [L12AbilityEvidence("S01-0107:ability:static:715fe715dcb8ea28", "entry-cost-condition-current", "presentation-consumers")]
    public void MoraleComparisonIsReadFromTheCurrentPlayers(string cardId)
    {
        var game = Create(70100 + cardId[^1]);
        var card = PutOnlyCardInHand(game, cardId);
        Assert.Equal(card.Cost, HandCost(game, card));

        game.State.Players[1].Morale.Add(Morale("opponent-extra"));
        Assert.Equal(Math.Max(0, card.Cost - 1), HandCost(game, card));

        game.State.Players[0].Morale.Add(Morale("controller-equal"));
        Assert.Equal(card.Cost, HandCost(game, card));
    }

    [Fact]
    public void GraveFactionAndFriendlyFieldCountsScaleWithoutCardBranches()
    {
        var beowulfGame = Create(70120);
        var beowulf = PutOnlyCardInHand(beowulfGame, "S01-0301");
        for (var i = 0; i < 3; i++) beowulfGame.State.Players[0].Graveyard.Add(Card("S01-0309", $"grave-{i}"));
        Assert.Equal(beowulf.Cost, HandCost(beowulfGame, beowulf));
        beowulfGame.State.Players[0].Graveyard.Add(Card("S01-0309", "grave-3"));
        Assert.Equal(Math.Max(0, beowulf.Cost - 1), HandCost(beowulfGame, beowulf));
        for (var i = 4; i < 8; i++) beowulfGame.State.Players[0].Graveyard.Add(Card("S01-0309", $"grave-{i}"));
        Assert.Equal(Math.Max(0, beowulf.Cost - 2), HandCost(beowulfGame, beowulf));

        var haraldGame = Create(70121);
        var harald = PutOnlyCardInHand(haraldGame, "S01-0302");
        haraldGame.State.Players[0].Field[0][0] = Card("S02-0004", "field-a");
        haraldGame.State.Players[0].Field[1][0] = Card("S02-0007", "field-b");
        Assert.Equal(Math.Max(0, harald.Cost - 2), HandCost(haraldGame, harald));
    }

    [Theory]
    [InlineData("S01-0305")]
    [InlineData("S01-0306")]
    public void HealthThresholdUsesTheCurrentMasterHealth(string cardId)
    {
        var game = Create(70130 + cardId[^1]);
        var card = PutOnlyCardInHand(game, cardId);
        game.State.Players[0].Hp = 7;
        Assert.Equal(card.Cost, HandCost(game, card));
        game.State.Players[0].Hp = 6;
        Assert.Equal(Math.Max(0, card.Cost - 1), HandCost(game, card));
    }

    [Theory]
    [InlineData("S01-0202", 2)]
    [InlineData("S02-0203", 1)]
    public void MissingTombGuardDiscountUsesTheCurrentField(string cardId, int discount)
    {
        var game = Create(70140 + cardId[^1]);
        var card = PutOnlyCardInHand(game, cardId);
        Assert.Equal(Math.Max(0, card.Cost - discount), HandCost(game, card));

        game.State.Players[0].Field[0][0] = Card("S01-0212", "field-tomb-guard");
        Assert.Equal(card.Cost, HandCost(game, card));

        game.State.Players[0].Field[0][0] = null;
        Assert.Equal(Math.Max(0, card.Cost - discount), HandCost(game, card));
    }

    [Fact]
    public void TombNamedLegionLeaveCountScalesTheCurrentPlayCost()
    {
        var game = Create(70150);
        var card = PutOnlyCardInHand(game, "S02-0202");
        Assert.Equal(card.Cost, HandCost(game, card));
        game.State.Players[0].TombNamedLegionsLeftThisTurn = 2;
        Assert.Equal(Math.Max(0, card.Cost - 2), HandCost(game, card));
    }

    private static L12GameEngine Create(int seed)
        => new(Catalog, "printed-entry-cost", "ENTRY-COST", seed, ["甲", "乙"], [0, 0],
            skipPreparation: true, stateFormatVersion: 2);

    private static void SatisfyPrintedDiscountCondition(L12GameEngine game, string cardId)
    {
        var player = game.State.Players[0];
        switch (cardId)
        {
            case "S01-0104":
            case "S01-0114":
                game.State.Players[1].Morale.Add(Morale("opponent-ahead"));
                break;
            case "S01-0301":
                for (var index = 0; index < 4; index++)
                    player.Graveyard.Add(Card("S01-0309", $"asgard-grave-{index}"));
                break;
            case "S01-0302":
                player.Field[0][0] = Card("S02-0004", "friendly-front");
                player.Field[1][0] = Card("S02-0007", "friendly-back");
                break;
            case "S01-0305":
            case "S01-0306":
                player.Hp = 6;
                break;
            case "S02-0202":
                player.TombNamedLegionsLeftThisTurn = 1;
                break;
        }
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

    private static L12MoraleCard Morale(string id)
        => new() { InstanceId = id, CardId = "S01-00C1" };

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
