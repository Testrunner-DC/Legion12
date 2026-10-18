using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class DeckCoverSelectionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("2", false)]
    [InlineData("1,1", false)]
    [InlineData("1 ", false)]
    [InlineData("1", true)]
    public void CoverCapabilityRequiresOneExactSupportedHeader(string? value, bool expected)
    {
        var headers = new HeaderDictionary();
        if (value is not null) headers[L12DeckCoverCapability.HeaderName] = value;
        Assert.Equal(expected, L12DeckCoverCapability.IsEnabled(headers));
        headers[L12DeckCoverCapability.HeaderName] = new StringValues(["1", "1"]);
        Assert.False(L12DeckCoverCapability.IsEnabled(headers));
    }

    [Fact]
    public void OnePerTypeUsesPrintedCostThenTroopsThenLatestNaturalNumber()
    {
        var cards = Catalog(
            Card("L-COST", "L-99", "legion", 4, 9000),
            Card("L-POWER", "L-1", "legion", 5, 2000),
            Card("L-10", "L-10", "legion", 5, 3000),
            Card("L-12", "L-12", "legion", 5, 3000),
            Card("T-2", "T-2", "tactic", 3),
            Card("T-10", "T-10", "tactic", 3),
            Card("A-NULL", "A-100", "artifact", null),
            Card("A-9", "A-9", "artifact", 4));

        var selected = L12DeckCoverSelection.SelectCardIds(cards,
            Quantities("A-NULL", "L-COST", "T-2", "L-POWER", "T-10", "L-10", "A-9", "L-12"));

        Assert.Equal(["L-12", "T-10", "A-9"], selected);
    }

    [Fact]
    public void MissingArtifactRefillsFromLegionAndRegroupsRightToLeft()
    {
        var cards = Catalog(
            Card("L-2", "L-2", "legion", 5, 3000),
            Card("L-10", "L-10", "legion", 5, 3000),
            Card("T-1", "T-1", "tactic", 2));
        var source = new[]
        {
            new L12DeckCardQuantity("T-1", 3),
            new L12DeckCardQuantity("L-2", 2),
            new L12DeckCardQuantity("L-10", 1),
            new L12DeckCardQuantity("L-10", 8),
        };

        var selected = L12DeckCoverSelection.SelectCardIds(cards, source);

        Assert.Equal(["L-10", "L-2", "T-1"], selected);
        Assert.Equal(4, source.Length);
        Assert.Equal(3, source[0].Quantity);
    }

    [Fact]
    public void MissingLegionRefillsFromTacticBeforeArtifact()
    {
        var cards = Catalog(
            Card("T-2", "T-2", "tactic", 3),
            Card("T-10", "T-10", "tactic", 3),
            Card("A-8", "A-8", "artifact", 8),
            Card("A-9", "A-9", "artifact", 9));

        var selected = L12DeckCoverSelection.SelectCardIds(cards, Quantities("A-8", "T-2", "A-9", "T-10"));

        Assert.Equal(["T-10", "T-2", "A-9"], selected);
    }

    [Fact]
    public void MissingTacticRefillsFromLegionBeforeArtifact()
    {
        var cards = Catalog(
            Card("L-2", "L-2", "legion", 5, 3000),
            Card("L-10", "L-10", "legion", 5, 3000),
            Card("A-9", "A-9", "artifact", 9),
            Card("A-10", "A-10", "artifact", 10));

        var selected = L12DeckCoverSelection.SelectCardIds(cards, Quantities("A-9", "L-2", "A-10", "L-10"));

        Assert.Equal(["L-10", "L-2", "A-10"], selected);
    }

    [Fact]
    public void InvalidUnknownAndZeroQuantityCardsNeverEnterCover()
    {
        var cards = Catalog(
            Card("T-VALID", "T-7", "tactic", 1),
            Card("MASTER", "M-99", "master", 99),
            Card("A-ZERO", "A-99", "artifact", 99));
        var source = new[]
        {
            new L12DeckCardQuantity("UNKNOWN", 1),
            new L12DeckCardQuantity("MASTER", 1),
            new L12DeckCardQuantity("A-ZERO", 0),
            new L12DeckCardQuantity("T-VALID", 1),
        };

        Assert.Equal(["T-VALID"], L12DeckCoverSelection.SelectCardIds(cards, source));
    }

    [Fact]
    public void EqualPrintedNumberFallsBackToLatestNaturalCardId()
    {
        var cards = Catalog(
            Card("ALT-2", "ALT", "legion", 4, 2000),
            Card("ALT-10", "ALT", "legion", 4, 2000));

        Assert.Equal(["ALT-10", "ALT-2"],
            L12DeckCoverSelection.SelectCardIds(cards, Quantities("ALT-2", "ALT-10")));
    }

    [Fact]
    public void FewerThanThreeUniqueEligibleCardsAreNotFabricated()
    {
        var cards = Catalog(Card("L-ONLY", "L-1", "legion", 1, 1000));

        Assert.Equal(["L-ONLY"],
            L12DeckCoverSelection.SelectCardIds(cards,
                [new L12DeckCardQuantity("L-ONLY", 3), new L12DeckCardQuantity("l-only", 2)]));
    }

    private static IReadOnlyDictionary<string, L12CardDefinition> Catalog(params L12CardDefinition[] cards)
        => cards.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);

    private static L12DeckCardQuantity[] Quantities(params string[] ids)
        => ids.Select(id => new L12DeckCardQuantity(id, 1)).ToArray();

    private static L12CardDefinition Card(string id, string number, string cardType, int? cost, int? troops = null)
        => new()
        {
            Id = id,
            Number = number,
            NameZh = id,
            CardType = cardType,
            Product = "test",
            Faction = "universal",
            Cost = cost,
            Troops = troops,
        };
}
