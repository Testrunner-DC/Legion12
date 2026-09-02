using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StarterProductCatalogTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Instance(string cardId)
    {
        var card = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = $"test-{cardId}",
            CardId = card.Id,
            Name = card.NameZh,
            CardType = card.CardType,
            Faction = card.Faction,
            Cost = card.Cost ?? 0,
            BaseTroops = card.Troops ?? 0,
            Troops = card.Troops ?? 0,
            Profession = card.Profession,
            EffectText = card.Effect,
        };
    }

    [Fact]
    public void StarterDatabaseProducesSeventySixUniqueCards()
    {
        var starterCards = Catalog.Cards.Values
            .Where(card => card.Id.StartsWith("ST", StringComparison.Ordinal))
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(76, starterCards.Length);
        Assert.Equal(76, starterCards.Select(card => card.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(6, starterCards.Count(card => card.CardType == "rune"));
        Assert.Equal(3, starterCards.Count(card => card.CardType == "destruction"));
        Assert.Equal("若我方场上存在<伊丽莎白 都铎>，此军团登场费用-2。\n登场时 选择对方最多2张休整的士气，下个重置阶段无法转为活跃。",
            Catalog.Cards["ST06-01"].Effect);
    }

    [Fact]
    public void DatabaseAtomicReferenceDefinesAbilityBoundariesWithoutInventingNoEffectAbilities()
    {
        Assert.Equal(5, Catalog.AtomicEffects.Find("ST01-01")?.Abilities.Count);
        Assert.Equal("human-assisted", Catalog.AtomicEffects.Find("ST01-01")?.ReviewStatus);
        Assert.Empty(Catalog.AtomicEffects.Find("ST01-05")?.Abilities ?? []);
        Assert.Equal("no-effect", Catalog.AtomicEffects.Find("ST01-05")?.MigrationStatus);
    }

    [Fact]
    public void FirstStarterRuntimeBatchUsesTheSameStructuredDefinitionsAsTheAdminCatalog()
    {
        var verified = new (string CardId, string Trigger)[]
        {
            ("ST01-02", "after-attack"), ("ST02-04", "enter"), ("ST03-01", "enter"),
            ("ST04-03", "enter"), ("ST04-09", "enter"), ("ST05-08", "enter"),
            ("ST06-03", "enter"), ("ST06-05", "enter"), ("ST06-05", "attack"),
            ("ST06-06", "enter"), ("ST06-06", "death"), ("ST06-08", "enter"),
            ("ST06-10", "play"),
        };

        foreach (var (cardId, trigger) in verified)
        {
            Assert.NotNull(L12VerifiedAtomicPrograms.Find(cardId, trigger));
            Assert.Contains(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                ability => ability.Trigger == trigger && ability.MigrationStatus == "verified"
                    && !ability.HasLegacyFallback);
        }
    }

    [Fact]
    public void ExplicitRemoteTextDrivesBothArcherAndMageCombatProfiles()
    {
        var explicitRemoteCards = Catalog.Cards.Values
            .Where(card => card.Id.StartsWith("ST", StringComparison.Ordinal)
                && card.Effect?.Contains("进攻距离+1，远程进攻无损", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Contains(explicitRemoteCards, card => card.Profession == "弓手");
        Assert.Contains(explicitRemoteCards, card => card.Profession == "术师");
        Assert.All(explicitRemoteCards, card =>
        {
            var instance = Instance(card.Id);
            var expectedFrontOnly = card.Effect!.Contains("「位于前排」进攻距离+1", StringComparison.Ordinal);
            Assert.True(L12StructuredCardRules.CombatProfile(instance, 0).HasRangeBonus);
            Assert.True(L12StructuredCardRules.CombatProfile(instance, 0).HasRangedNoLoss);
            Assert.Equal(!expectedFrontOnly, L12StructuredCardRules.CombatProfile(instance, 1).HasRangeBonus);
            Assert.Equal(!expectedFrontOnly, L12StructuredCardRules.CombatProfile(instance, 1).HasRangedNoLoss);
        });
    }

    [Theory]
    [InlineData("ST01-04")]
    [InlineData("ST02-02")]
    [InlineData("ST04-01")]
    [InlineData("ST06-02")]
    public void StarterFrontRowTauntUsesSharedCombatRule(string cardId)
    {
        var card = Instance(cardId);

        Assert.True(L12StructuredCardRules.HasTaunt(card, 0));
        Assert.False(L12StructuredCardRules.HasTaunt(card, 1));
    }
}
