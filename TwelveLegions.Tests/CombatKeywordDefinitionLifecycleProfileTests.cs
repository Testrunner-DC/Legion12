using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CombatKeywordDefinitionLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-0302:ability:keyword-definition:eaba79729a9d7a65", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0503:ability:keyword-definition:6692b63a59c971d0", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0512:ability:keyword-definition:6692b63a59c971d0", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0615:ability:keyword-definition:8a4c9aff096f6526", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0505:ability:keyword-definition:cf232142ca7d10f9", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0602:ability:keyword-definition:beff9037e2c10a9d", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0612:ability:keyword-definition:beff9037e2c10a9d", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0511:ability:keyword-definition:96aa4e9504b12339", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05M1:ability:keyword-definition:41657ed47ef085ae", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05M1:ability:keyword-definition:995c52041c470ca4", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0605:ability:keyword-definition:60bccaeb6d982ea8", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0606:ability:keyword-definition:672734be0285300f", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0611:ability:keyword-definition:672734be0285300f", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0608:ability:keyword-definition:4d1e472a814a1e0b", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0611:ability:keyword-definition:4d1e472a814a1e0b", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("ST01-04:ability:keyword-definition:c24a6b9d8de8435a", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("ST02-02:ability:keyword-definition:c24a6b9d8de8435a", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("ST04-01:ability:keyword-definition:c24a6b9d8de8435a", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("ST06-02:ability:keyword-definition:c24a6b9d8de8435a", "parent-grant-boundary", "authoritative-consumer")]
    public void EveryKeywordDefinitionHasOneStructuredSemanticOwner()
    {
        var expected = EffectLifecycleProfiles.CombatKeywordDefinitionAbilityIds
            .SelectMany(pair => pair.Value.Select(id => (Id: id, Keyword: pair.Key)))
            .OrderBy(entry => entry.Id).ToArray();
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "keyword-definition")
            .Select(ability => (Id: ability.AbilityId, Keyword: Assert.Single(ability.Atoms,
                atom => atom.Kind == L12AtomKinds.Keyword).Parameters["keywordRef"]))
            .OrderBy(entry => entry.Id).ToArray();
        Assert.Equal(expected, actual);
        Assert.All(actual, entry =>
        {
            var cardId = entry.Id[..entry.Id.IndexOf(":ability:", StringComparison.Ordinal)];
            Assert.True(L12StructuredCardRules.HasKeywordDefinition(cardId, entry.Keyword));
        });
    }

    [Fact]
    [L12AbilityEvidence("S02-0004:ability:granted:be3174252606645e", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0007:ability:granted:be3174252606645e", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-03M1:ability:granted:f4dd24f1fb07f3d5", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0403:ability:granted:f4dd24f1fb07f3d5", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0405:ability:granted:f4dd24f1fb07f3d5", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("ST01-01:ability:granted:c502e9ac1489cd1a", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0206:ability:granted:1aba3f5bd15a426d", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0404:ability:granted:e3ff02735b6b18f4", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("ST01-01:ability:granted:6ec4b634ed12b206", "parent-grant-boundary", "authoritative-consumer")]
    public void EveryGrantedKeywordDefinitionHasOneStructuredSemanticOwner()
    {
        var expected = EffectLifecycleProfiles.GrantedKeywordDefinitionAbilityIds
            .SelectMany(pair => pair.Value.Select(id => (Id: id, Keyword: pair.Key)))
            .OrderBy(entry => entry.Id).ToArray();
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "granted"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword))
            .Select(ability => (Id: ability.AbilityId, Keyword: Assert.Single(ability.Atoms,
                atom => atom.Kind == L12AtomKinds.Keyword).Parameters["keywordRef"]))
            .OrderBy(entry => entry.Id).ToArray();
        Assert.Equal(expected, actual);
        Assert.All(actual, entry =>
        {
            var cardId = entry.Id[..entry.Id.IndexOf(":ability:", StringComparison.Ordinal)];
            Assert.True(L12StructuredCardRules.HasPrintedKeywordReference(cardId, entry.Keyword));
            Assert.False(L12StructuredCardRules.HasKeywordDefinition(cardId, entry.Keyword));
        });
    }

    [Fact]
    public void PrintedPiercingIdentityComesFromStructuredDefinitionsAndStarterGrantedReferences()
    {
        Assert.True(L12StructuredCardRules.HasPrintedKeywordReference("S02-0606", "piercing"));
        Assert.True(L12StructuredCardRules.HasPrintedKeywordReference("S02-0611", "piercing"));
        Assert.True(L12StructuredCardRules.HasPrintedKeywordReference("ST01-01", "piercing"));
        Assert.False(L12StructuredCardRules.HasPrintedKeywordReference("S02-0608", "piercing"));
        Assert.False(L12StructuredCardRules.HasPrintedKeywordReference("S02-0606", "charge"));
    }
}
