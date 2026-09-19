using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TacticIdentityConsistencyTests
{
    private static readonly string[] ExpectedCounterTacticIds =
    [
        "S01-0016", "S01-0017", "S01-0018", "S01-0019", "S01-0020", "S01-0021",
        "S01-0120", "S01-0223", "S01-0224", "S01-0320", "S01-0420",
        "S02-0015", "S02-0016", "S02-0017", "S02-0018", "S02-0106", "S02-0523",
        "ST01-10",
    ];

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [Trait("L12Evidence", "family:tactic-identity")]
    public void OfficialCatalogUsesOneTacticTypeAndAnIndependentCounterFlag()
    {
        var property = typeof(L12CardDefinition).GetProperty("IsCounterTactic",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        var catalog = Catalog;
        var flagged = catalog.Cards.Values
            .Where(card => (bool)(property!.GetValue(card) ?? false))
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedCounterTacticIds, flagged.Select(card => card.Id));
        Assert.All(flagged, card => Assert.Equal("tactic", card.CardType));
        Assert.DoesNotContain(catalog.Cards.Values,
            card => card.CardType.Equals("counter-tactic", StringComparison.OrdinalIgnoreCase));

        Assert.All(flagged, card =>
        {
            Assert.True(L12CounterTacticRules.IsTactic(card));
            Assert.True(L12CounterTacticRules.IsCounterTactic(card));
            Assert.False(L12CounterTacticRules.IsActiveTactic(card));
            var atomic = catalog.AtomicEffects.Find(card.Id);
            Assert.NotNull(atomic);
            Assert.Equal("tactic", atomic.CardType);
            Assert.True(atomic.IsCounterTactic);
        });

        var activeTactic = Assert.Single(catalog.Cards.Values
            .Where(card => card.CardType == "tactic" && !card.IsCounterTactic)
            .Take(1));
        Assert.True(L12CounterTacticRules.IsActiveTactic(activeTactic));
        Assert.False(L12CounterTacticRules.IsCounterTactic(activeTactic));
    }
}
