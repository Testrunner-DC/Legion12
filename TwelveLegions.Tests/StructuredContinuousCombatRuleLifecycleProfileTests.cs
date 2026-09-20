using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StructuredContinuousCombatRuleLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly HashSet<string> ReviewedRuleParameters = new(StringComparer.Ordinal)
    {
        "cannotAttack", "cannotSupport", "attackNoLoss", "cannotBeRanged",
        "protectMasterFromTroopsAtMost", "cannotAttackMaster", "cannotReceiveBackRowSupport",
        "incomingRangedCombatDamageAdjustment", "targetableByAttack", "protect",
    };

    [Fact]
    [L12AbilityEvidence("S01-0004:ability:static:1644ef88125b05c1", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S01-0101:ability:static:1f027ad861ea0006", "authoritative-consumer", "combat-settlement")]
    [L12AbilityEvidence("S01-0101:ability:static:1041797d91099ae1", "authoritative-consumer", "combat-settlement")]
    [L12AbilityEvidence("S02-0002:ability:continuous:5643b9f0c6e298e6", "authoritative-consumer", "combat-settlement")]
    [L12AbilityEvidence("S02-0005:ability:continuous:0663e3d5b31edc67", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S02-0007:ability:continuous:602cafbbc29faa3f", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S02-0101:ability:continuous:4cd3104ae17d316d", "authoritative-consumer", "row-and-ready-condition")]
    [L12AbilityEvidence("S02-0201:ability:continuous:39b0b1524eaed536", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S02-02M1:ability:continuous:a83e1e0971bbe6f0", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S02-0302:ability:continuous:48719a94741bbf36", "authoritative-consumer", "row-and-ready-condition")]
    [L12AbilityEvidence("S02-0503:ability:static:5e2fcb0f2798f57a", "authoritative-consumer", "combat-settlement")]
    [L12AbilityEvidence("S02-0504:ability:static:0ada28f438439ac2", "authoritative-consumer", "row-and-ready-condition")]
    [L12AbilityEvidence("S02-0516:ability:static:17774ead9eb8ed69", "authoritative-consumer", "row-and-ready-condition")]
    [L12AbilityEvidence("S02-0603:ability:continuous:5e0d666ac6a386ba", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S02-0609:ability:continuous:dc2aa603cc3d136c", "authoritative-consumer", "candidate-and-submit-parity")]
    [L12AbilityEvidence("S02-0616:ability:continuous:5afe2828d587391f", "authoritative-consumer", "row-and-ready-condition")]
    public void EveryStructuredContinuousCombatRuleHasOneSharedRuntimeOwner()
    {
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(IsStructuredContinuousCombatRule)
            .OrderBy(ability => ability.AbilityId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(EffectLifecycleProfiles.StructuredContinuousCombatRuleAbilityIds.OrderBy(id => id),
            actual.Select(ability => ability.AbilityId));

        foreach (var ability in actual)
        {
            var runtimeRules = L12StructuredCardRules.GetCombatRuleAbilities(ability.CardId)
                .SelectMany(candidate => candidate.Atoms)
                .Where(atom => atom.Kind == L12AtomKinds.AttackRule)
                .ToArray();
            foreach (var rule in ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.AttackRule))
                Assert.Contains(runtimeRules, candidate => rule.Parameters.All(parameter =>
                    candidate.Parameters.GetValueOrDefault(parameter.Key) == parameter.Value));
        }
    }

    [Theory]
    [InlineData("S02-0101")]
    [InlineData("S02-0504")]
    public void FrontRowMasterProtectionExpiresOutsideTheFrontRow(string cardId)
    {
        var card = Card(cardId);
        Assert.True(L12StructuredCardRules.ProtectsMasterFromTroops(card, 0, 2000));
        Assert.False(L12StructuredCardRules.ProtectsMasterFromTroops(card, 0, 2001));
        Assert.False(L12StructuredCardRules.ProtectsMasterFromTroops(card, 1, 2000));
    }

    [Fact]
    public void ReadyAndRestedConditionsAreReevaluatedFromCurrentState()
    {
        var hannibal = Card("S02-0516");
        Assert.True(L12StructuredCardRules.CannotBeAttacked(hannibal, 0));
        hannibal.Tapped = true;
        Assert.False(L12StructuredCardRules.CannotBeAttacked(hannibal, 0));

        var amakine = Card("S02-0616");
        Assert.False(L12StructuredCardRules.ProtectsActiveTrialLegions(amakine));
        amakine.Tapped = true;
        Assert.True(L12StructuredCardRules.ProtectsActiveTrialLegions(amakine));
    }

    private static bool IsStructuredContinuousCombatRule(L12AtomicAbility ability)
    {
        if (ability.ExecutionModel is not ("continuous" or "granted-continuous")) return false;
        var rules = ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.AttackRule).ToArray();
        return rules.Length > 0 && rules.All(atom => !atom.Parameters.ContainsKey("text"))
            && rules.Any(atom => atom.Parameters.Keys.Any(ReviewedRuleParameters.Contains));
    }

    private static L12CardInstance Card(string cardId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = $"profile-{cardId}",
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            TrialValue = definition.TrialValue ?? 0,
        };
    }
}
