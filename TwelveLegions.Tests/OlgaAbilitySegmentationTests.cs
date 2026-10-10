using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OlgaAbilitySegmentationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void OlgaHasThreeIndependentAbilityScopesWithoutRangedOverlayDuplication()
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0314"));
        Assert.Collection(card.Abilities,
            ranged =>
            {
                Assert.Equal("static", ranged.Trigger);
                Assert.Equal("进攻距离+1，远程进攻无损。", ranged.Text);
                Assert.Null(ranged.CostText);
                Assert.Contains(ranged.Atoms, atom => atom.Kind == L12AtomKinds.AttackRule);
                Assert.DoesNotContain(ranged.Atoms, atom => atom.Stage == "cost");
            },
            discount =>
            {
                Assert.Equal("hand-play", discount.Trigger);
                Assert.Equal("可对我方主宰造成1点伤害", discount.CostText);
                Assert.Equal("此军团登场费用-1。", discount.ResolutionText);
                Assert.Contains(discount.Atoms, atom => atom.Kind == L12AtomKinds.Condition
                    && atom.Parameters.GetValueOrDefault("expression") == "source.zone=hand");
                Assert.Contains(discount.Atoms, atom => atom.Kind == L12AtomKinds.DamageMaster
                    && atom.Stage == "cost"
                    && atom.Parameters.GetValueOrDefault("amount") == "1");
                Assert.DoesNotContain(discount.Atoms, atom => atom.Kind == L12AtomKinds.Discard);
                Assert.DoesNotContain(discount.Atoms, atom => atom.Kind == L12AtomKinds.AttackRule);
            },
            active =>
            {
                Assert.Equal("active", active.Trigger);
                Assert.Equal("我方回合 可弃置此军团", active.CostText);
                Assert.Equal("选择对方前排1张军团，本回合兵力-2000。", active.ResolutionText);
                var target = Assert.Single(active.Atoms, atom => atom.Kind == L12AtomKinds.SelectTarget);
                var cost = Assert.Single(active.Atoms, atom => atom.Kind == L12AtomKinds.Discard);
                Assert.Equal("0", target.Parameters.GetValueOrDefault("min"));
                Assert.Equal("explicit-click-when-present", target.Parameters.GetValueOrDefault("selection"));
                Assert.Equal("skip-resolution", target.Parameters.GetValueOrDefault("emptyPolicy"));
                Assert.Equal("cost", cost.Stage);
                Assert.True(target.Order < cost.Order);
                Assert.DoesNotContain(active.Atoms, atom => atom.Kind == L12AtomKinds.DamageMaster);
                Assert.DoesNotContain(active.Atoms, atom => atom.Kind == L12AtomKinds.AttackRule);
            });
    }

    [Fact]
    public void OlgaRuntimeButtonReadsTheStructuredActiveAbilityText()
    {
        var ability = Assert.IsType<L12StructuredAbilityTemplate>(
            L12StructuredCardRules.FindRuntimeAbility("S01-0314", "olgaDebuff"));

        Assert.Equal("active", ability.Trigger);
        Assert.Equal("我方回合 可弃置此军团：选择对方前排1张军团，本回合兵力-2000。", ability.Text);
        Assert.True(L12StructuredCardRules.HasOptionalSelfDamageEntryDiscount("S01-0314"));
    }
}
