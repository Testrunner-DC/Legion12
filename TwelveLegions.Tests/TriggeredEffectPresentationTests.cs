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
                Assert.Contains(resolved, lines);
                audited++;
            }
        }

        Assert.True(audited >= 100, $"应审计至少100条触发能力，实际仅{audited}条");
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
}
