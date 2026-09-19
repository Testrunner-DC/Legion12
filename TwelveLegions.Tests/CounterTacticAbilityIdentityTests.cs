using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CounterTacticAbilityIdentityTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> S1Counters()
    {
        return Catalog.Cards.Values.Where(card => card.Id.StartsWith("S01-", StringComparison.Ordinal)
                && card.IsCounterTactic)
            .OrderBy(card => card.Id).Select(card => new object[] { card.Id });
    }

    [Theory]
    [MemberData(nameof(S1Counters))]
    public void RegisteredCounterKeepsItsWholeObservedTimingInOneResponseAbility(string cardId)
    {
        var catalog = Catalog;
        var ability = Assert.Single(catalog.AtomicEffects.Find(cardId)!.Abilities);
        Assert.Equal("reaction", ability.Trigger);
        Assert.Equal("reaction", ability.ExecutionModel);
        Assert.Equal(catalog.Cards[cardId].Effect!.Trim(), ability.Text);
        Assert.DoesNotContain(ability.Text.Trim(), new[] { "对方", "对方 军团", "我方 军团" });
        Assert.NotEmpty(ability.Presentations);
        Assert.All(ability.Presentations, scene => Assert.Equal(ability.AbilityId, scene.AbilityId));
    }

    [Fact]
    public void EmptyCityResponseRetainsItsCostAndBothActualResultScenes()
    {
        var ability = Assert.Single(Catalog.AtomicEffects.Find("S01-0120")!.Abilities);
        Assert.Contains("返还1士气", ability.CostText);
        Assert.Contains(ability.Atoms, atom => atom.Stage == "cost");
        Assert.Contains("抵挡本次进攻", ability.ResolutionText);
        Assert.Contains("抽取1张牌", ability.ResolutionText);
        Assert.True(ability.Presentations.Count >= 2);
    }
}
