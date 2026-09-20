using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ActiveRestCommonLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0105:ability:active:0e81cd47a6221fd8", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0109:ability:active:88c64e7a7e50fb25", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0117:ability:active:ba48403c4da1e24c", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-01D1:ability:active:2b7ae6d9b09b600b", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0214:ability:active:30e47404439f2371", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0215:ability:active:6984859bdd4fa8b1", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0317:ability:active:90c21e26f3d58b69", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-03D1:ability:active:79829ccbe13dcca0", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-04D1:ability:active:67457fb394219836", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0003:ability:active:484fb98a6af8df3f", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0104:ability:active:1687d445c6acc308", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0204:ability:active:4257a82eec559a94", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0205:ability:active:8023ed21f8771697", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0404:ability:active:b30de444d37a3b6e", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0510:ability:active:2ee4c7f29b568e48", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0513:ability:active:0b4d5245336709f8", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0520:ability:active:e4e320d416a9c103", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-05D1:ability:active:f160e84288ecb28c", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0603:ability:active:8768d3f1fcb44728", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0616:ability:active:3616b237df312569", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-06D1:ability:active:30a9d18991dc8481", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST02-05:ability:active:80aa98cc24ef764e", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST03-05:ability:active:87d142bd0e12a218", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST03-07:ability:active:0d4ebc1a2ab8b128", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST04-06:ability:active:8f6b1b9dfc246e36", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST05-06:ability:active:cc5d71f55d3a253f", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST06-09:ability:active:e533dbf15f08cea0", "active-rest-cost", "runtime-branch-mapping")]
    public void EveryPrintedActiveRestSegmentUsesTheSharedCostBoundary()
    {
        var activeRest = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "active"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost"))
            .ToArray();
        Assert.Equal(27, activeRest.Length);
        Assert.Equal(EffectLifecycleProfiles.ActiveRestAbilityIds.Order(),
            activeRest.Select(ability => ability.AbilityId).Order());
        Assert.All(activeRest, ability =>
        {
            Assert.Contains("主动休整", ability.Text);
            Assert.Contains(ability.Atoms,
                atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost");
        });
    }
}
