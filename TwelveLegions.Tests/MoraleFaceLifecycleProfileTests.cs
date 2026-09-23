using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MoraleFaceLifecycleProfileTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-0508:ability:death:9aea23b4138e399e", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-0513:ability:enter:eef83ec51f2ef093", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-0518:ability:enter:6e9ddf89fefa712f", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-0520:ability:enter:361ec387b847ecee", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-0521:ability:play:4ae24413479102d1", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-05C1:ability:active:1ae9b19504eac93a", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-05C1A:ability:active:1ae9b19504eac93a", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-05D1:ability:active:519ab3c1379a9256", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("S02-05M1:ability:friendly-ranged-death:049d5f20b59f5888", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("ST05-C1:ability:static:6fe475d8923feb65", "identity-definition", "runtime-owner")]
    [L12AbilityEvidence("ST05-M1:ability:active:b1f11ab05f68dda0", "identity-definition", "runtime-owner")]
    public void EveryPrintedMoraleFaceFlipOwnsTheSharedIdentityBoundary()
    {
        var profiles = EffectLifecycleProfiles.Read(Catalog);
        var bound = profiles.Where(pair => pair.Value.Id == "resource:morale-face-flip")
            .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(EffectLifecycleProfiles.MoraleFaceFlipAbilityIds.Order(StringComparer.Ordinal), bound);
        Assert.All(bound, id =>
        {
            var profile = profiles[id];
            Assert.Equal("L12MoraleIdentityCatalog.CanUseGodPowerFace",
                profile.RuntimeOwners["identity-definition"]);
            Assert.Equal("L12S2ZoneOps.FlipMoraleFace", profile.RuntimeOwners["settlement-mutation"]);
        });
    }

    [Fact]
    public void OnlyOlympusMoraleVersionsSupportTheGodPowerFace()
    {
        var identities = Catalog.MoraleIdentities;
        Assert.All(new[] { "S02-05C1", "S02-05C1A", "ST05-C1" },
            cardId => Assert.True(identities.CanUseGodPowerFace(cardId)));
        Assert.All(new[] { "S01-01C1", "S01-02C1", "S01-03C1", "S01-04C1", "S02-06C1", "S02-0010" },
            cardId => Assert.False(identities.CanUseGodPowerFace(cardId)));
    }
}
