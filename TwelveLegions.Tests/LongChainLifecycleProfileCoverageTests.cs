using Xunit;
using TwelveLegions.Server;

namespace TwelveLegions.Tests;

public sealed class LongChainLifecycleProfileCoverageTests
{
    public static IEnumerable<object[]> Representatives()
        => LongChainAllCardHarness.ProfileRepresentatives()
            .Where(item => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("L12_LONG_CHAIN_PROFILE"))
                || item.ProfileId == Environment.GetEnvironmentVariable("L12_LONG_CHAIN_PROFILE"))
            .Select(item => new object[] { item.ProfileId, item.Ability });

    [Fact]
    [Trait("L12Evidence", "lc02-lifecycle-profile-inventory")]
    public void RepresentativesAreTheExactSeventyEightReviewedProfiles()
    {
        var bindings = EffectLifecycleProfiles.Read(LongChainAllCardHarness.Catalog);
        var inventory = EffectLifecycleInventoryTests.Build(LongChainAllCardHarness.Catalog);
        var completion = EffectLifecycleCompletionMatrix.Build(inventory);
        var expected = bindings.Values.Select(profile => profile.Id).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var representatives = LongChainAllCardHarness.ProfileRepresentatives();
        Assert.Equal(78, expected.Length);
        Assert.Equal(78, completion.ProfileCount);
        Assert.Equal(expected, representatives.Select(item => item.ProfileId));
        Assert.All(representatives, representative =>
        {
            var profile = bindings[representative.Ability.AbilityId];
            Assert.Equal(representative.ProfileId, profile.Id);
            Assert.NotEmpty(profile.RuntimeOwners);
            Assert.All(profile.SharedProtocolScopes, scope =>
                Assert.Contains(scope, EffectLifecycleCompletionMatrix.Items));
            Assert.Equal(representative.Ability.CardId,
                LongChainAllCardHarness.Catalog.AtomicEffects.Find(representative.Ability.CardId)!.CardId);
            var matrixRow = Assert.Single(completion.Profiles, row => row.ProfileId == representative.ProfileId);
            Assert.Equal(profile.AdditionalChecks, matrixRow.AdditionalChecks);
            Assert.Contains(representative.Ability.AbilityId, matrixRow.AbilityIds);
        });
        Assert.NotEmpty(EffectLifecycleEvidence.Read(LongChainAllCardHarness.Catalog));
    }

    [Theory]
    [MemberData(nameof(Representatives), DisableDiscoveryEnumeration = true)]
    [Trait("L12Evidence", "lc02-lifecycle-profile-journal-replay")]
    public async Task EveryProfileRepresentativeSurvivesJournalRecoveryAndRejectionAtomicity(
        string profileId, L12AtomicAbility ability)
    {
        var testCase = LongChainAllCardHarness.Inventory().Single(item => item.CardId == ability.CardId);
        var evidence = await LongChainAllCardHarness.RunAsync(testCase, profileId, journal: true);
        Assert.Equal(profileId, evidence.ProfileId);
        Assert.True(evidence.RejectedSteps >= 1);
        Assert.Contains(evidence.Cutpoints, cutpoint => cutpoint.StartsWith("journal-sequence-", StringComparison.Ordinal));
    }
}
