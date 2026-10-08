using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AnnihilationTerminologyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void CorrectedNameKeepsStableCardAndAbilityIdentities()
    {
        var card = Catalog.Cards["S01-DS10"];
        var effect = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(card.Id));

        Assert.Equal("S01-DS10", L12ActiveDisasterRules.AnnihilationCardId);
        Assert.Equal("S01-DS10", card.Id);
        Assert.Equal("湮灭", card.NameZh);
        Assert.Equal("湮灭", effect.Name);
        Assert.Contains(effect.Abilities,
            ability => ability.AbilityId == "S01-DS10:ability:static:33501d2503c08b73");
        Assert.Contains(effect.Abilities,
            ability => ability.AbilityId == "S01-DS10:ability:turn-start:a790e35d0012c86f");
    }

    [Fact]
    public void CorrectedRuntimeRouteAndLegacyFlowKeyNormalizeConsistently()
    {
        var route = Assert.IsType<L12VerifiedAtomicProgram>(
            L12RuntimeEffectRoutes.FindProgram("S01-DS10", "disaster"));
        var flow = Assert.Single(route.Atoms, atom => atom.Kind == L12AtomKinds.CompositeFlow);
        Assert.Equal("湮灭", flow.Parameters["flow"]);

        var normalize = typeof(L12GameEngine).GetMethod("NormalizeAtomicFlowKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(normalize);
        Assert.Equal("湮灭", normalize.Invoke(null, ["\u5819\u706D"]));
        Assert.Equal("诸神黄昏", normalize.Invoke(null, ["诸神黄昏"]));
    }
}
