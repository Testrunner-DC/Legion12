using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S02HumanAssistedAtomicTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> ExpectedAbilityCounts = new Dictionary<string, int>
    {
        ["S02-0001"] = 2, ["S02-0002"] = 2, ["S02-0003"] = 3, ["S02-0004"] = 2,
        ["S02-0005"] = 2, ["S02-0006"] = 2, ["S02-0007"] = 3, ["S02-0008"] = 2,
        ["S02-0009"] = 1, ["S02-0010"] = 2, ["S02-0011"] = 1, ["S02-0012"] = 3,
        ["S02-0013"] = 3, ["S02-0014"] = 1, ["S02-0015"] = 1, ["S02-0016"] = 3,
        ["S02-0017"] = 1, ["S02-0018"] = 1,
        ["S02-0101"] = 2, ["S02-0102"] = 2, ["S02-0103"] = 2, ["S02-0104"] = 2,
        ["S02-0105"] = 1, ["S02-0106"] = 1, ["S02-01S1"] = 2,
        ["S02-0201"] = 2, ["S02-0202"] = 2, ["S02-0203"] = 3, ["S02-0204"] = 3,
        ["S02-0205"] = 4, ["S02-0206"] = 3, ["S02-0207"] = 1, ["S02-02M1"] = 3,
        ["S02-0301"] = 4, ["S02-0302"] = 4, ["S02-0303"] = 2, ["S02-0304"] = 3,
        ["S02-0305"] = 4, ["S02-0306"] = 2, ["S02-0307"] = 1, ["S02-03M1"] = 3,
        ["S02-0401"] = 2, ["S02-0402"] = 2, ["S02-0403"] = 3, ["S02-0404"] = 5,
        ["S02-0405"] = 3, ["S02-0406"] = 4, ["S02-04M1"] = 3,
    };

    [Fact]
    public void AllFortyEightCardsUseTheHumanAssistedAbilityBoundaries()
    {
        Assert.Equal(48, ExpectedAbilityCounts.Count);
        foreach (var (cardId, expected) in ExpectedAbilityCounts)
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            Assert.Equal(expected, card.Abilities.Count);
            Assert.All(card.Abilities, ability =>
            {
                Assert.Equal("human-assisted", ability.ReviewStatus);
                Assert.Equal("user-20260829", ability.ReviewSource);
            });
        }
    }

    [Fact]
    public void HolyLockOpponentPermissionExistsOnlyWhileCurrentlyAttached()
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0013"));
        var ability = Assert.Single(card.Abilities, item => item.Trigger == "active-while-attached");
        Assert.Equal("activated-by-opponent", ability.ExecutionModel);
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.Condition
            && atom.Parameters.GetValueOrDefault("expression") == "source.zone=attached");
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.PayMorale
            && atom.Parameters.GetValueOrDefault("actor") == "opponent"
            && atom.Parameters.GetValueOrDefault("amount") == "3");
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.MoveZone
            && atom.Parameters.GetValueOrDefault("to") == "owner.grave");
        Assert.DoesNotContain(ability.Atoms, atom => atom.Kind == L12AtomKinds.Duration
            && atom.Parameters.GetValueOrDefault("duration") == "once-per-turn");
    }

    [Fact]
    public void HiddenInformationRulesRemainExplicitInTheAtomicCatalog()
    {
        var paladin = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0202"));
        Assert.Contains(paladin.Abilities.SelectMany(ability => ability.Atoms), atom =>
            atom.Kind == L12AtomKinds.Visibility
            && atom.Parameters.GetValueOrDefault("audience") == "controller-only");

        var supplies = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0017"));
        Assert.Contains(supplies.Abilities.SelectMany(ability => ability.Atoms), atom =>
            atom.Kind == L12AtomKinds.Visibility
            && atom.Parameters.GetValueOrDefault("selection") == "server-randomized");
        Assert.DoesNotContain("查看", supplies.Abilities.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void HumanTargetDeclarationPrecedesAbilityCosts()
    {
        foreach (var cardId in new[] { "S02-0101", "S02-0402", "S02-04M1" })
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            foreach (var ability in card.Abilities.Where(item =>
                         item.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectTarget)
                         && item.Atoms.Any(atom => atom.Stage == "cost")))
            {
                var target = ability.Atoms.First(atom => atom.Kind == L12AtomKinds.SelectTarget);
                var cost = ability.Atoms.First(atom => atom.Stage == "cost");
                Assert.True(target.Order < cost.Order, $"{cardId} 的目标必须在费用前预声明");
            }
        }
    }

    [Fact]
    public void ExplicitCombatAbilitiesDoNotReceiveLegacyOverlayDuplicates()
    {
        foreach (var cardId in new[] { "S02-0002", "S02-0003", "S02-0004", "S02-0007", "S02-0101", "S02-0204", "S02-0302", "S02-0304" })
        {
            var expected = ExpectedAbilityCounts[cardId];
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            Assert.Equal(expected, card.Abilities.Count);
        }
    }

    [Fact]
    public void RolloSeparatesFrontRowGrantTauntDefinitionAndEnterHealing()
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0302"));

        Assert.Equal(4, card.Abilities.Count);
        Assert.Contains(card.Abilities[1].Atoms, atom => atom.Kind == L12AtomKinds.Condition
            && atom.Parameters.GetValueOrDefault("expression") == "source.row=front");
        Assert.Contains(card.Abilities[1].Atoms, atom => atom.Kind == L12AtomKinds.SetState
            && atom.Parameters.GetValueOrDefault("abilityRef") == "ability:3");
        Assert.Contains(card.Abilities[1].Atoms, atom => atom.Kind == L12AtomKinds.AttackRule
            && atom.Parameters.GetValueOrDefault("cannotReceiveBackRowSupport") == "true");
        Assert.Contains(card.Abilities[2].Atoms, atom => atom.Kind == L12AtomKinds.Keyword
            && atom.Parameters.GetValueOrDefault("keywordRef") == "taunt");
        Assert.Contains(card.Abilities[3].Atoms, atom => atom.Kind == L12AtomKinds.HealMaster);

        var rollo = new L12CardInstance
        {
            InstanceId = "rollo-test",
            CardId = "S02-0302",
            Name = "步行者罗洛",
            CardType = "军团",
            Faction = "阿斯加德",
        };
        Assert.True(L12StructuredCardRules.HasTaunt(rollo, 0));
        Assert.False(L12StructuredCardRules.HasTaunt(rollo, 1));
        Assert.True(L12StructuredCardRules.CannotReceiveBackRowSupport(rollo, 0));
        Assert.False(L12StructuredCardRules.CannotReceiveBackRowSupport(rollo, 1));
    }
}
