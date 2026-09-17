using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ReferencedTimingBoundaryTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("「阵亡时」")]
    [InlineData("『登场时』")]
    [InlineData("“离场时”")]
    [InlineData("〈进攻时〉")]
    [InlineData("《击杀时》")]
    [InlineData("【回合开始时】")]
    [InlineData("<反击战术>")]
    [InlineData("反击战术")]
    [InlineData("登场时效果")]
    [InlineData("阵亡时效果")]
    [InlineData("「登场时 抽取1张牌。阵亡时 抽取1张牌」")]
    public void ReferencedTimingCannotSplitOrReclassifyItsContainingAbility(string reference)
    {
        var text = $"主动休整 可消耗1士气：选择带有{reference}的卡牌。登场时 抽取1张牌。";
        var card = new L12CardDefinition { Id = "TEST-REFERENCE", Number = "TEST-REFERENCE",
            NameZh = "引用边界", CardType = "legion", Product = "test", Faction = "universal", Effect = text };
        var abilities = L12AtomicEffectCatalog.Build([card]).Find(card.Id)!.Abilities;
        Assert.Equal(2, abilities.Count);
        Assert.Equal("active", abilities[0].Trigger);
        Assert.Contains(reference, abilities[0].Text);
        Assert.EndsWith("的卡牌", abilities[0].Text);
        Assert.Equal("enter", abilities[1].Trigger);
        Assert.Equal("主动休整 可消耗1士气", abilities[0].CostText);
    }

    [Fact]
    public void MengpoReferenceRemainsInsideItsCompleteBranchText()
    {
        var effect = Catalog.AtomicEffects.Find("S01-01M2")!;
        var ability = Assert.Single(effect.Abilities);
        Assert.Contains("失去「阵亡时」效果", ability.Text);
        Assert.Contains("·若我方士气少于对方，弃置1张手牌", ability.Text);
        Assert.NotEqual("death", ability.Trigger);
    }

    [Theory]
    [InlineData("S01-0201")]
    [InlineData("S01-0202")]
    public void CounterTacticProtectionRemainsOneCompleteClause(string cardId)
    {
        var abilities = Catalog.AtomicEffects.Find(cardId)!.Abilities;
        var protection = Assert.Single(abilities, ability => ability.Text.Contains("不受反击战术效果影响"));
        Assert.Equal("static", protection.Trigger);
        Assert.DoesNotContain(abilities, ability => ability.Text == "登场时效果");
    }

    [Fact]
    public void UesugiCounterTacticReferencesDoNotCreateExtraAbilities()
    {
        var abilities = Catalog.AtomicEffects.Find("S01-0403")!.Abilities;
        Assert.Equal(2, abilities.Count);
        Assert.Contains("（X=双方战场<反击战术>合计数量）", abilities[0].Text);
        Assert.Equal("enter", abilities[0].Trigger);
        Assert.Equal("death", abilities[1].Trigger);
        Assert.Contains("<反击战术>置入我方后排", abilities[1].Text);
    }

    [Theory]
    [InlineData("S01-01M2", 1)]
    [InlineData("S01-0201", 4)]
    [InlineData("S01-0202", 2)]
    [InlineData("S01-0403", 2)]
    [InlineData("S01-0413", 3)]
    [InlineData("S01-DS03", 2)]
    public void ReviewedCardsKeepStableCompleteReferenceBoundaries(string cardId, int count)
    {
        var card = Catalog.AtomicEffects.Find(cardId)!;
        Assert.Equal(count, card.Abilities.Count);
        Assert.Equal(Enumerable.Range(1, count), card.Abilities.Select(ability => ability.Sequence));
        Assert.Equal(count, card.Abilities.Select(ability => ability.AbilityId).Distinct().Count());
        foreach (var ability in card.Abilities)
        {
            Assert.Equal(ability.Text.Count(character => character == '<'), ability.Text.Count(character => character == '>'));
            Assert.Equal(ability.Text.Count(character => character == '「'), ability.Text.Count(character => character == '」'));
            Assert.All(ability.Presentations, scene => Assert.Equal(ability.AbilityId, scene.AbilityId));
        }
        if (cardId == "S01-0413")
            Assert.Contains("<反击战术>，本回合无法发动", Assert.Single(card.Abilities, ability => ability.Trigger == "attack").Text);
        if (cardId == "S01-DS03")
            Assert.Contains("打出<反击战术>无需消耗费用", Assert.Single(card.Abilities, ability => ability.Trigger == "static").Text);
        if (cardId == "S01-0202")
            Assert.EndsWith("自选顺序发动其登场时效果", Assert.Single(card.Abilities, ability => ability.Trigger == "enter").Text);
    }

    [Fact]
    public void RealUnquotedTimingsStillSeparateWhileQuotedSentencesStayWhole()
    {
        var card = new L12CardDefinition { Id = "TEST-REAL-TIMINGS", Number = "TEST-REAL-TIMINGS",
            NameZh = "真实时点", CardType = "legion", Product = "test", Faction = "universal",
            Effect = "登场时 此军团获得「进攻时 抽取1张牌。阵亡时 抽取1张牌」。进攻时 抽取1张牌。阵亡时 抽取1张牌。" };
        var abilities = L12AtomicEffectCatalog.Build([card]).Find(card.Id)!.Abilities;
        Assert.Equal(new[] { "enter", "attack", "death" }, abilities.Select(ability => ability.Trigger));
        Assert.Contains("「进攻时 抽取1张牌。阵亡时 抽取1张牌」", abilities[0].Text);
    }
}
