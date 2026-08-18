using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialValueRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void TrialLegionsUseOfficialTrialValues()
    {
        var expected = new Dictionary<string, int>
        {
            ["S02-0618"] = 2,
            ["S02-0609"] = 1,
            ["S02-0604"] = 2,
            ["S02-0613"] = 1,
            ["S02-0606"] = 1,
            ["S02-0614"] = 1,
            ["S02-0617"] = 1,
            ["S02-0610"] = 1,
        };

        foreach (var (cardId, trialValue) in expected)
            Assert.Equal(trialValue, Catalog.Cards[cardId].TrialValue);
    }
}
