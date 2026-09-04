using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S02OtherworldHumanAssistedAtomicTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> ExpectedAbilityCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["S02-0601"] = 2, ["S02-0602"] = 5, ["S02-0603"] = 5, ["S02-0604"] = 3,
            ["S02-0605"] = 4, ["S02-0606"] = 5, ["S02-0607"] = 2, ["S02-0608"] = 4,
            ["S02-0609"] = 3, ["S02-0610"] = 3, ["S02-0611"] = 5, ["S02-0612"] = 4,
            ["S02-0613"] = 3, ["S02-0614"] = 5, ["S02-0615"] = 3, ["S02-0616"] = 3,
            ["S02-0617"] = 4, ["S02-0618"] = 3, ["S02-0619"] = 2, ["S02-0620"] = 2,
            ["S02-0621"] = 2, ["S02-0622"] = 2,
        };

    [Fact]
    public void AllTwentyTwoOtherworldCardsUseTheConfirmedSeventyFourAbilityBoundaries()
    {
        Assert.Equal(22, ExpectedAbilityCounts.Count);
        Assert.Equal(74, ExpectedAbilityCounts.Values.Sum());
        foreach (var (cardId, count) in ExpectedAbilityCounts)
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            Assert.Equal(count, card.Abilities.Count);
            Assert.All(card.Abilities, ability =>
            {
                Assert.Equal("human-assisted", ability.ReviewStatus);
                Assert.Equal("user-20260904", ability.ReviewSource);
            });
        }
    }

    [Theory]
    [InlineData("S02-0604", 2)]
    [InlineData("S02-0606", 1)]
    [InlineData("S02-0609", 1)]
    [InlineData("S02-0610", 1)]
    [InlineData("S02-0613", 1)]
    [InlineData("S02-0614", 1)]
    [InlineData("S02-0617", 1)]
    [InlineData("S02-0618", 2)]
    public void TrialValueIsAnIndependentRuleAbility(string cardId, int value)
    {
        var definition = Catalog.Cards[cardId];
        Assert.Equal(value, definition.TrialValue);
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
        var trial = Assert.Single(card.Abilities, ability => ability.Trigger == "trial");
        Assert.Equal($"试炼 {value}", trial.Text);
        Assert.Contains(trial.Atoms, atom => atom.Kind == L12AtomKinds.Special
            && atom.Parameters.GetValueOrDefault("semantic") == "trial-value"
            && atom.Parameters.GetValueOrDefault("value") == value.ToString());
    }

    [Fact]
    public void HandCostModifiersAreDrivenByTheConfirmedStructuredConditions()
    {
        var player = NewPlayer();
        var bors = Card("S02-0605", "鲍斯", "otherworld");
        var cuchulainn = Card("S02-0611", "库丘林", "otherworld");
        var scathach = Card("S02-0612", "斯卡哈", "otherworld");
        player.Hand.AddRange([bors, cuchulainn, scathach]);
        player.Field[0][0] = Card("otherworld-a", "彼界军团A", "otherworld");
        player.Field[1][0] = Card("otherworld-b", "彼界军团B", "otherworld");

        Assert.Equal(-2, L12StructuredCardRules.HandPlayCostModifier(player, bors));
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, cuchulainn));
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, scathach));

        player.Field[0][1] = scathach;
        Assert.Equal(-2, L12StructuredCardRules.HandPlayCostModifier(player, cuchulainn));
        player.Field[1][1] = cuchulainn;
        Assert.Equal(-2, L12StructuredCardRules.HandPlayCostModifier(player, scathach));
    }

    [Fact]
    public void RangedTauntAndSquireAttackRulesUseTheSharedStructuredQueries()
    {
        foreach (var cardId in new[] { "S02-0614", "S02-0617", "S02-0618" })
        {
            var card = Card(cardId, Catalog.Cards[cardId].NameZh, "otherworld");
            Assert.True(L12StructuredCardRules.CombatProfile(card, 0).HasRangeBonus);
            Assert.True(L12StructuredCardRules.CombatProfile(card, 1).HasRangedNoLoss);
        }

        var claudia = Card("S02-0619", "克劳迪娅", "otherworld");
        Assert.True(L12StructuredCardRules.CombatProfile(claudia, 0).HasRangeBonus);
        Assert.False(L12StructuredCardRules.CombatProfile(claudia, 1).HasRangeBonus);

        var gwen = Card("S02-0615", "格温莉安", "otherworld");
        Assert.True(L12StructuredCardRules.HasTaunt(gwen, 0));
        Assert.False(L12StructuredCardRules.HasTaunt(gwen, 1));

        var squire = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0609"));
        var restriction = Assert.Single(squire.Abilities, ability => ability.Text == "无法进攻主宰。");
        Assert.Contains(restriction.Atoms, atom => atom.Kind == L12AtomKinds.AttackRule
            && atom.Parameters.GetValueOrDefault("cannotAttackMaster") == "true");
        Assert.DoesNotContain(restriction.Atoms, atom => atom.Parameters.GetValueOrDefault("cannotAttack") == "true");
    }

    [Theory]
    [InlineData("S02-0602", "charge")]
    [InlineData("S02-0605", "strong-attack")]
    [InlineData("S02-0606", "piercing")]
    [InlineData("S02-0608", "death-immunity")]
    [InlineData("S02-0611", "piercing")]
    [InlineData("S02-0612", "charge")]
    [InlineData("S02-0615", "taunt")]
    public void GrantedKeywordsRemainIndependentAbilities(string cardId, string keyword)
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
        Assert.Contains(card.Abilities, ability => ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword
            && atom.Parameters.GetValueOrDefault("keywordRef") == keyword));
    }

    [Fact]
    public void ExactUserConfirmedComplexAbilityTextsArePreserved()
    {
        var richard = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0608"));
        Assert.Contains(richard.Abilities, ability => ability.Text.Contains(
            "我方战场/手牌/牌库/墓地将最多3张<侍从骑士>", StringComparison.Ordinal));

        var amakine = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0616"));
        Assert.Contains(amakine.Abilities, ability => ability.Text.Contains(
            "若其只拥有【彼界】特征", StringComparison.Ordinal));

        foreach (var cardId in new[] { "S02-0606", "S02-0611" })
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            Assert.Contains(card.Abilities, ability => ability.Text.Contains(
                "此次进攻不会触发“进攻时”效果", StringComparison.Ordinal));
        }
    }

    private static L12PlayerState NewPlayer() => new()
    {
        Name = "P1", DeckName = "test", Faction = "otherworld", MasterId = "S02-06M1",
        MasterName = "莫瑞甘", Hp = 8, MaxHp = 8,
    };

    private static L12CardInstance Card(string cardId, string name, string faction) => new()
    {
        InstanceId = $"{cardId}-{Guid.NewGuid():N}", CardId = cardId, Name = name,
        CardType = "legion", Faction = faction, Cost = 4, BaseTroops = 4000, Troops = 4000,
    };
}
