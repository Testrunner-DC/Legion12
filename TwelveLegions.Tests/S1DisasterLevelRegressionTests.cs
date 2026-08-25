using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S1DisasterLevelRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void ConfirmedSeasonOneLegionsUseOfficialDisasterLevels()
    {
        var expected = new Dictionary<string, int>
        {
            ["S01-0001"] = 2,
            ["S01-0101"] = 3,
            ["S01-0102"] = 2,
            ["S01-0103"] = 2,
            ["S01-0104"] = 1,
            ["S01-0105"] = 1,
            ["S01-0106"] = 1,
            ["S01-0107"] = 1,
            ["S01-0201"] = 3,
            ["S01-0202"] = 3,
            ["S01-0203"] = 1,
            ["S01-0204"] = 2,
            ["S01-0205"] = 1,
            ["S01-0301"] = 3,
            ["S01-0302"] = 3,
            ["S01-0303"] = 2,
            ["S01-0304"] = 2,
            ["S01-0305"] = 1,
            ["S01-0306"] = 1,
            ["S01-0307"] = 1,
            ["S01-0308"] = 1,
            ["S01-0401"] = 3,
            ["S01-0402"] = 2,
            ["S01-0403"] = 2,
            ["S01-0404"] = 1,
            ["S01-0405"] = 1,
            ["S01-0406"] = 1,
        };

        var actual = Catalog.Cards.Values
            .Where(card => string.Equals(card.Product, "S01", StringComparison.OrdinalIgnoreCase) && card.DisasterLevel is not null)
            .ToDictionary(card => card.Id, card => card.DisasterLevel!.Value);

        Assert.Equal(expected.OrderBy(entry => entry.Key), actual.OrderBy(entry => entry.Key));
        Assert.All(actual.Keys, cardId => Assert.Equal("legion", Catalog.Cards[cardId].CardType));
    }
}
