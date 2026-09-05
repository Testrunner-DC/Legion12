using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TriggeredEffectPresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EveryCatalogTriggerFamilyResolvesToOnePrintedAbilityLine()
    {
        var audited = 0;
        foreach (var definition in Catalog.Cards.Values.Where(card => !string.IsNullOrWhiteSpace(card.Effect)))
        {
            var source = CreateInstance(definition);
            var lines = definition.Effect!.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var (trigger, fallback) in TriggerFamilies(lines))
            {
                var resolved = L12GameEngine.ResolveTriggeredEffectDisplayText(source, trigger, fallback);
                Assert.DoesNotContain('\n', resolved);
                if (definition.Id == "ST06-05")
                    Assert.EndsWith("可抽取1张牌。", resolved, StringComparison.Ordinal);
                else
                    Assert.Contains(Normalize(resolved), Normalize(definition.Effect!), StringComparison.Ordinal);
                audited++;
            }
        }

        Assert.True(audited >= 100, $"应审计至少100条触发能力，实际仅{audited}条");
    }

    [Fact]
    public void SameLineMultiAbilityCardShowsOnlyCurrentTriggeredAbility()
    {
        var source = CreateInstance(Catalog.Cards["S01-0102"]);

        var resolved = L12GameEngine.ResolveTriggeredEffectDisplayText(source, "death", "【阵亡时】效果");

        Assert.Equal("阵亡时 抽取1张牌，我方主宰增加1点血量。", resolved);
        Assert.DoesNotContain("登场时", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void PerchFalconSharedPrintedLineShowsOnlyTheTriggerThatActuallyFired()
    {
        var source = CreateInstance(Catalog.Cards["ST06-05"]);

        Assert.Equal("登场时 可抽取1张牌。",
            L12GameEngine.ResolveTriggeredEffectDisplayText(source, "enter", "【登场时】效果"));
        Assert.Equal("进攻时 可抽取1张牌。",
            L12GameEngine.ResolveTriggeredEffectDisplayText(source, "attack", "【进攻时】效果"));
    }

    [Fact]
    public void UnmatchedTriggerFallsBackToCurrentTriggerDescriptionNotWholeCardText()
    {
        var source = new L12CardInstance
        {
            InstanceId = "presentation-fallback",
            CardId = "TEST-001",
            Name = "测试卡",
            CardType = "legion",
            Faction = "neutral",
            Cost = 1,
            EffectText = "登场时 抽取1张牌。\n阵亡时 返还1张士气。",
            BaseTroops = 1000,
            Troops = 1000,
        };

        var resolved = L12GameEngine.ResolveTriggeredEffectDisplayText(
            source, "forge-ready-after-kill", "匠神锻造炉赋予的击杀后转为活跃效果");

        Assert.Equal("匠神锻造炉赋予的击杀后转为活跃效果", resolved);
        Assert.DoesNotContain('\n', resolved);
    }

    [Fact]
    public void EveryDirectResponseCardResolvesOnlyItsCurrentResponseAbilityBlock()
    {
        var responseMarkers = new Dictionary<string, string>
        {
            ["S01-0002"] = "进攻我方军团时",
            ["S02-0005"] = "进攻我方主宰时",
            ["S01-0016"] = "进攻或发动效果时",
            ["S01-0018"] = "军团登场时",
            ["S01-0019"] = "进攻或发动效果时",
            ["S01-0020"] = "进攻时",
            ["S01-0120"] = "进攻时",
            ["S01-0224"] = "发动战术效果或圣物效果时",
            ["S02-0015"] = "进行抵挡/支援时",
            ["S02-0016"] = "以手牌以外的方式登场时",
            ["S02-0017"] = "因效果将1张卡牌加入手牌时",
            ["S02-0018"] = "休整的卡牌因效果转为活跃时",
            ["S02-0106"] = "进攻或发动效果时",
        };

        foreach (var (cardId, marker) in responseMarkers)
        {
            var definition = Catalog.Cards[cardId];
            var source = CreateInstance(definition);
            var resolved = L12GameEngine.ResolveResponseEffectDisplayText(source, "响应效果");
            Assert.Contains(marker, resolved, StringComparison.Ordinal);
            Assert.DoesNotContain('\n', resolved);
            Assert.Contains(Normalize(resolved), Normalize(definition.Effect ?? string.Empty), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("可进行1次位移", L12GameEngine.ResolveResponseEffectDisplayText(
            CreateInstance(Catalog.Cards["S01-0002"]), "响应效果"));
        Assert.DoesNotContain("无法进攻", L12GameEngine.ResolveResponseEffectDisplayText(
            CreateInstance(Catalog.Cards["S02-0005"]), "响应效果"));
        Assert.Contains("• 弃置对方1张手牌", L12GameEngine.ResolveResponseEffectDisplayText(
            CreateInstance(Catalog.Cards["S02-0016"]), "响应效果"));
    }

    private static IEnumerable<(string Trigger, string Fallback)> TriggerFamilies(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains("晋升登场", StringComparison.Ordinal))
                yield return ("promotion-enter", "【晋升登场】效果");
            else if (line.Contains("登场时", StringComparison.Ordinal))
                yield return ("enter", "【登场时】效果");
            if (line.Contains("进攻时", StringComparison.Ordinal))
                yield return ("attack", "【进攻时】效果");
            if (line.Contains("进攻后", StringComparison.Ordinal))
                yield return ("after-attack", "【进攻后】效果");
            if (line.Contains("阵亡时", StringComparison.Ordinal))
                yield return ("death", "【阵亡时】效果");
            if (line.Contains("离场时", StringComparison.Ordinal))
                yield return ("leave", "【离场时】效果");
            if (line.Contains("对主宰造成伤害时", StringComparison.Ordinal))
                yield return ("after-damage", "【对主宰造成伤害时】效果");
        }
    }

    private static L12CardInstance CreateInstance(L12CardDefinition definition)
        => new()
        {
            InstanceId = $"presentation-{definition.Id}",
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Profession = definition.Profession,
            Traits = [.. definition.Traits],
        };

    private static string Normalize(string value)
        => value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
