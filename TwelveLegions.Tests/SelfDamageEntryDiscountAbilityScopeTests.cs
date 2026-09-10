using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SelfDamageEntryDiscountAbilityScopeTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static TheoryData<string, int> Cards => new()
    {
        { "S01-0303", 3 },
        { "S01-0304", 3 },
        { "S01-0308", 3 },
        { "S01-0310", 3 },
        { "S01-0314", 3 },
        { "S02-0303", 2 },
    };

    [Theory]
    [MemberData(nameof(Cards))]
    public void EverySelfDamageDiscountIsAnIndependentHandPlayAbility(string cardId, int abilityCount)
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
        Assert.Equal(abilityCount, card.Abilities.Count);
        var ability = Assert.Single(card.Abilities, candidate => candidate.Trigger == "hand-play");

        Assert.Equal("可对我方主宰造成1点伤害", ability.CostText);
        Assert.Equal("此军团登场费用-1。", ability.ResolutionText);
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.Condition
            && atom.Parameters.GetValueOrDefault("expression") == "source.zone=hand");
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.DamageMaster
            && atom.Stage == "cost"
            && atom.Parameters.GetValueOrDefault("semantic") == "self-damage-entry-discount-cost");
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.SetState
            && atom.Stage == "resolution"
            && atom.Parameters.GetValueOrDefault("key") == "source.derived-cost"
            && atom.Parameters.GetValueOrDefault("value") == "-1");
        Assert.DoesNotContain(card.Abilities.Where(candidate => candidate != ability).SelectMany(candidate => candidate.Atoms),
            atom => atom.Parameters.GetValueOrDefault("semantic") == "self-damage-entry-discount-cost");
        Assert.True(L12StructuredCardRules.HasOptionalSelfDamageEntryDiscount(cardId));
        var runtimeRule = Assert.IsType<L12SelfDamageEntryDiscountRule>(
            L12StructuredCardRules.SelfDamageEntryDiscount(cardId));
        Assert.Equal(1, runtimeRule.DamageAmount);
        Assert.Equal(-1, runtimeRule.CostAdjustment);
        Assert.Equal(ability.CostText, runtimeRule.CostText);
        Assert.Equal(ability.ResolutionText, runtimeRule.ResolutionText);
    }

    [Fact]
    public void ColonsInTriggerClausesDoNotBecomeCosts()
    {
        var erik = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0308"));
        var afterDamage = Assert.Single(erik.Abilities, ability => ability.Trigger == "after-damage");

        Assert.Null(afterDamage.CostText);
        Assert.Equal(afterDamage.Text, afterDamage.ResolutionText);
        Assert.Contains(afterDamage.Atoms, atom => atom.Kind == L12AtomKinds.Discard
            && atom.Stage == "resolution"
            && atom.Parameters.GetValueOrDefault("zone") == "opponent.hand");
        Assert.DoesNotContain(afterDamage.Atoms, atom => atom.Stage == "cost");

        var ragnar = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0303"));
        var death = Assert.Single(ragnar.Abilities, ability => ability.Trigger == "death");
        Assert.Null(death.CostText);
        Assert.Contains(death.Atoms, atom => atom.Kind == L12AtomKinds.Discard && atom.Stage == "resolution");
    }

    [Theory]
    [InlineData("S01-0304", "death")]
    [InlineData("S01-0308", "death")]
    public void MandatoryObjectEffectsRequireExplicitSelectionWhenAnObjectExists(string cardId, string trigger)
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
        var ability = Assert.Single(card.Abilities, candidate => candidate.Trigger == trigger);
        var target = Assert.Single(ability.Atoms, atom => atom.Kind == L12AtomKinds.SelectTarget);

        Assert.Equal("0", target.Parameters.GetValueOrDefault("min"));
        Assert.Equal("1", target.Parameters.GetValueOrDefault("max"));
        Assert.Equal("explicit-click-when-present", target.Parameters.GetValueOrDefault("selection"));
        Assert.Equal("skip-resolution", target.Parameters.GetValueOrDefault("emptyPolicy"));
    }
}
