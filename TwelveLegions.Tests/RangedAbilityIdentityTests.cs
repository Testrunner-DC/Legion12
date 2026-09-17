using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RangedAbilityIdentityTests
{
    private const string RangedText = "进攻距离+1，远程进攻无损";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> PrintedRangedCards() => Catalog.Cards.Values
        .Where(card => card.Effect?.Contains(RangedText, StringComparison.Ordinal) == true)
        .OrderBy(card => card.Id).Select(card => new object[] { card.Id });

    [Theory]
    [MemberData(nameof(PrintedRangedCards))]
    public void EveryPrintedRangedAbilityHasOneIndependentIdentityWithoutSiblingCosts(string cardId)
    {
        var abilities = Catalog.AtomicEffects.Find(cardId)!.Abilities;
        var ranged = Assert.Single(abilities, ability => ability.Text.Contains(RangedText, StringComparison.Ordinal));
        Assert.Equal("continuous", ranged.ExecutionModel);
        Assert.Null(ranged.CostText);
        Assert.DoesNotContain(ranged.Atoms, atom => atom.Stage == "cost");
        Assert.All(new[] { "对方", "登场时", "返还", "消耗" }, phrase => Assert.DoesNotContain(phrase, ranged.Text));
        Assert.All(ranged.Presentations, scene => Assert.Equal(ranged.AbilityId, scene.AbilityId));
        Assert.Equal(Enumerable.Range(1, abilities.Count), abilities.Select(ability => ability.Sequence));
        Assert.Equal(abilities.Count, abilities.Select(ability => ability.AbilityId).Distinct().Count());
    }

    [Theory]
    [InlineData("S01-0003", "位于后排 可消耗2士气", "此军团本回合可进攻对方后排和主宰")]
    [InlineData("S01-0113", "「位于后排」可返还1士气", "此军团本回合可进攻对方后排")]
    public void RangedPrefixCannotBecomePartOfAnotherAbilityCost(string cardId, string cost, string effect)
    {
        var paid = Assert.Single(Catalog.AtomicEffects.Find(cardId)!.Abilities, ability => ability.CostText is not null);
        Assert.Equal(cost, paid.CostText);
        Assert.Equal(effect, paid.ResolutionText.TrimEnd('。'));
        Assert.DoesNotContain(paid.Atoms, atom => atom.Parameters.ContainsKey("rangeBonus"));
    }

    [Fact]
    public void RemovingRangedPrefixPreservesTheObservedOpponentTimingAndDiscountClause()
    {
        var kaba = Catalog.AtomicEffects.Find("S01-0213")!.Abilities;
        Assert.Equal(2, kaba.Count);
        var response = Assert.Single(kaba, ability => ability.Trigger == "after-attack");
        Assert.StartsWith("对方进攻后：", response.Text.Replace(" ", ""));
        Assert.Contains("下个我方重置阶段", response.Text);
        var qin = Catalog.AtomicEffects.Find("S01-0114")!.Abilities;
        Assert.Equal(3, qin.Count);
        Assert.Single(qin, ability => ability.Text == "若我方士气少于对方，此军团登场费用-1");
        Assert.Single(qin, ability => ability.Trigger == "enter");
    }

    [Fact]
    public void HumanReviewedRangedDefinitionWinsOverTheCompatibilityOverlay()
    {
        Assert.True(L12StructuredCardRules.TryGetStructuredAbilities("ST05-03", out var source));
        var reviewed = Assert.Single(source, ability => ability.Text.Contains(RangedText, StringComparison.Ordinal));
        var actual = Assert.Single(Catalog.AtomicEffects.Find("ST05-03")!.Abilities,
            ability => ability.Text.Contains(RangedText, StringComparison.Ordinal));
        Assert.Equal(reviewed.Trigger, actual.Trigger);
        Assert.Equal(reviewed.ReviewSource, actual.ReviewSource);
        Assert.Equal(reviewed.ReviewStatus, actual.ReviewStatus);
        Assert.Single(L12StructuredCardRules.GetCombatRuleAbilities("ST05-03"),
            ability => ability.Text.Contains(RangedText, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("登场时 此军团本回合获得进攻距离+1，远程进攻无损。", "enter")]
    [InlineData("「位于前排」进攻距离+1，远程进攻无损。", "static")]
    [InlineData("进攻距离+1，远程进攻无损时，抽取1张牌。", "static")]
    public void OverlayDoesNotAbsorbATriggeredGrantDifferentRowOrNonBoundary(string text, string trigger)
    {
        // Exercise the two shared boundary helpers directly. Real cards may acquire
        // reviewed definitions later and then correctly bypass the fallback parser.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var card = SyntheticCard("S01-0003", text);
        var abilities = Assert.IsType<List<L12AtomicAbility>>(typeof(L12AtomicEffectCatalog)
            .GetMethod("BuildFallbackAbilities", flags)!.Invoke(null, [card, text]));
        var actual = Assert.Single(abilities);
        Assert.Equal(text.TrimEnd('。'), actual.Text.TrimEnd('。'));
        Assert.Equal(trigger, actual.Trigger);
        var overlay = Assert.Single(L12StructuredCardRules.GetCombatOverlayAbilities(card.Id));
        Assert.Equal(false, typeof(L12StructuredCardRules).GetMethod("MatchesRangedOverlay", flags)!
            .Invoke(null, [actual.Text, actual.ExecutionModel, overlay]));
    }

    [Fact]
    public void AnUnregisteredCardDoesNotAcquireRegisteredCombatAtomsByTextMatching()
    {
        var card = SyntheticCard("TEST-UNREGISTERED-RANGED", RangedText + "。我方 回合1次 可抽取1张牌。");
        var abilities = L12AtomicEffectCatalog.Build([card]).Find(card.Id)!.Abilities;
        Assert.DoesNotContain(abilities.SelectMany(ability => ability.Atoms),
            atom => atom.Parameters.ContainsKey("rangeBonus") || atom.Parameters.ContainsKey("rangedNoLoss"));
    }

    private static L12CardDefinition SyntheticCard(string id, string text) => new()
    {
        Id = id, Number = id, NameZh = "边界测试", CardType = "legion",
        Product = "test", Faction = "generic", Effect = text,
    };
}
