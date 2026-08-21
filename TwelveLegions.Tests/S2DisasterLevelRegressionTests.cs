using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S2DisasterLevelRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void ConfirmedSeasonTwoLegionsUseOfficialDisasterLevels()
    {
        var expected = new Dictionary<string, int>
        {
            ["S02-0004"] = 1,
            ["S02-0101"] = 3,
            ["S02-0102"] = 2,
            ["S02-0202"] = 2,
            ["S02-0302"] = 3,
            ["S02-0303"] = 2,
            ["S02-0401"] = 3,
            ["S02-0402"] = 1,
            ["S02-0501"] = 3,
            ["S02-0503"] = 3,
            ["S02-0505"] = 2,
            ["S02-0509"] = 2,
            ["S02-0510"] = 2,
            ["S02-0511"] = 1,
            ["S02-0601"] = 2,
            ["S02-0602"] = 3,
            ["S02-0603"] = 1,
            ["S02-0605"] = 2,
            ["S02-0607"] = 2,
            ["S02-0608"] = 3,
            ["S02-0611"] = 1,
            ["S02-0612"] = 1,
            ["S02-0613"] = 2,
        };

        foreach (var (cardId, disasterLevel) in expected)
        {
            var card = Catalog.Cards[cardId];
            Assert.Equal("legion", card.CardType);
            Assert.Equal(disasterLevel, card.DisasterLevel);
        }
    }

    [Fact]
    public void PerlotAiUsesCorrectOfficialName()
    {
        Assert.Equal("珀洛特埃", Catalog.Cards["S02-0511"].NameZh);
    }
}
