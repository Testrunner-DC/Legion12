using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SelfDamageEntryDiscountLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0303:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0304:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0308:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0310:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0314:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S02-0303:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    public void EveryPrintedSelfDamageDiscountUsesOneHandPlayCostProtocol()
    {
        var abilities = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "hand-play"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.DamageMaster && atom.Stage == "cost"
                    && atom.Parameters.GetValueOrDefault("semantic") == "self-damage-entry-discount-cost"))
            .ToArray();
        Assert.Equal(6, abilities.Length);
        Assert.Equal(EffectLifecycleProfiles.SelfDamageEntryDiscountAbilityIds.Order(),
            abilities.Select(ability => ability.AbilityId).Order());
        Assert.All(abilities, ability =>
        {
            Assert.Equal("可对我方主宰造成1点伤害", ability.CostText);
            Assert.Equal("此军团登场费用-1。", ability.ResolutionText);
            var rule = Assert.IsType<L12SelfDamageEntryDiscountRule>(
                L12StructuredCardRules.SelfDamageEntryDiscount(ability.CardId));
            Assert.Equal(1, rule.DamageAmount);
            Assert.Equal(-1, rule.CostAdjustment);
        });
    }
}
