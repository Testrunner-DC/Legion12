using System.Text.RegularExpressions;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// ST01-ST06逐卡能力边界门禁。这里锁定产品数据库的人工拆分结果，防止后续导入、
/// 卡图批处理或旧卡池生成脚本悄悄丢失某张卡、某条Ability或三张ST天灾。
/// 实战行为仍由各Starter批次的专项回归负责，本类不以“能解析”冒充“已结算”。
/// </summary>
public sealed class StarterFullCatalogAuditTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> ExpectedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ST01-01"] = 5, ["ST01-02"] = 1, ["ST01-03"] = 1, ["ST01-04"] = 2,
            ["ST01-05"] = 0, ["ST01-06"] = 1, ["ST01-07"] = 2, ["ST01-08"] = 2,
            ["ST01-09"] = 2, ["ST01-10"] = 1, ["ST01-M1"] = 1, ["ST01-C1"] = 2,
            ["ST02-01"] = 2, ["ST02-02"] = 2, ["ST02-03"] = 1, ["ST02-04"] = 1,
            ["ST02-05"] = 1, ["ST02-06"] = 1, ["ST02-07"] = 1, ["ST02-08"] = 3,
            ["ST02-09"] = 0, ["ST02-10"] = 1, ["ST02-M1"] = 1, ["ST02-C1"] = 2,
            ["ST03-01"] = 2, ["ST03-02"] = 1, ["ST03-03"] = 1, ["ST03-04"] = 1,
            ["ST03-05"] = 2, ["ST03-06"] = 0, ["ST03-07"] = 2, ["ST03-08"] = 1,
            ["ST03-09"] = 0, ["ST03-10"] = 1, ["ST03-M1"] = 1, ["ST03-C1"] = 1,
            ["ST04-01"] = 3, ["ST04-02"] = 2, ["ST04-03"] = 1, ["ST04-04"] = 1,
            ["ST04-05"] = 1, ["ST04-06"] = 1, ["ST04-07"] = 3, ["ST04-08"] = 0,
            ["ST04-09"] = 1, ["ST04-10"] = 4, ["ST04-M1"] = 1, ["ST04-C1"] = 1,
            ["ST05-01"] = 2, ["ST05-02"] = 0, ["ST05-03"] = 2, ["ST05-04"] = 2,
            ["ST05-05"] = 0, ["ST05-06"] = 1, ["ST05-07"] = 1, ["ST05-08"] = 3,
            ["ST05-09"] = 3, ["ST05-10"] = 3, ["ST05-M1"] = 1, ["ST05-C1"] = 1,
            ["ST06-01"] = 2, ["ST06-02"] = 2, ["ST06-03"] = 2, ["ST06-04"] = 6,
            ["ST06-05"] = 1, ["ST06-06"] = 3, ["ST06-07"] = 3, ["ST06-08"] = 2,
            ["ST06-09"] = 4, ["ST06-10"] = 1, ["ST06-M1"] = 2, ["ST06-S1"] = 2,
            ["ST06-C1"] = 1, ["ST-DS01"] = 1, ["ST-DS02"] = 1, ["ST-DS03"] = 1,
        };

    [Fact]
    public void StarterCatalogKeepsAllSeventySixCardsAndOneHundredTwentyOneAbilityBoundaries()
    {
        var starter = Catalog.Cards.Values.Where(card => card.Id.StartsWith("ST", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(76, starter.Count);
        Assert.Equal(ExpectedAbilityCounts.Keys.Order(), starter.Keys.Order());

        var total = 0;
        foreach (var (cardId, expected) in ExpectedAbilityCounts)
        {
            var definition = starter[cardId];
            var actual = Regex.Matches(definition.AtomicReference ?? string.Empty,
                @"(?im)^\s*Ability\s+\d+").Count;
            Assert.Equal(expected, actual);
            total += actual;
            var effect = definition.Effect?.Trim() ?? string.Empty;
            if (expected == 0) Assert.Equal("无效果", effect);
            else Assert.NotEqual("无效果", effect);
        }
        Assert.Equal(121, total);
    }

    [Fact]
    public void PreviouslyUntestedStarterEntriesRemainExplicitlyReachableThroughSharedRules()
    {
        string[] ranged = ["ST01-07", "ST01-08", "ST01-09", "ST02-08", "ST03-05", "ST05-04"];
        foreach (var cardId in ranged)
            Assert.True(L12StructuredCardRules.HasAnyRowRangeBonus(Create(cardId)), cardId);

        string[] activeRest = ["ST03-05", "ST03-07", "ST05-06"];
        foreach (var cardId in activeRest)
            Assert.True(L12StructuredCardRules.HasActiveRestAbility(cardId), cardId);

        Assert.Contains("ST01-07", L12GameEngine.AttackerAfterAttackCards);
        Assert.Contains("ST01-10", L12GameEngine.DefenderAfterAttackCards);
        Assert.True(L12StructuredCardRules.HasTriggeredDisasterEffect("ST-DS01"));
        Assert.True(L12StructuredCardRules.HasTriggeredDisasterEffect("ST-DS03"));

        // These ids intentionally appear here so runtime-evidence export cannot again report
        // them as silently untested: ST01-10, ST02-08, ST05-04 also retain parsed trigger entries.
        foreach (var cardId in new[] { "ST01-10", "ST02-08", "ST05-04" })
            Assert.NotEmpty(Catalog.AtomicEffects.Find(cardId)!.Abilities);
    }

    [Fact]
    public void StarterPlayerFacingAbilityButtonsUsePrintedEffectLanguageInsteadOfDeveloperSummaries()
    {
        var game = new L12GameEngine(Catalog, "starter-full-audit", "STARTERFULL", 20450,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var method = typeof(L12GameEngine).GetMethod("GetAbilities",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var abilities = Assert.IsAssignableFrom<IEnumerable<L12AbilityView>>(method.Invoke(game, ["ST05-M1"]));
        var athena = Assert.Single(abilities, ability => ability.Id == "athenaFrontBuff");
        Assert.Contains("本回合兵力+1000，且对对方主宰造成的伤害+1", athena.Label);
        Assert.DoesNotContain("强化", athena.Label);

        string[] forbidden = ["减益", "模式一", "模式二", "效果一", "效果二", "预先声明", "预先选择", "公开区域", "私密区域", "公开资源"];
        foreach (var card in Catalog.Cards.Values.Where(card => card.Id.StartsWith("ST", StringComparison.OrdinalIgnoreCase)))
        foreach (var ability in Assert.IsAssignableFrom<IEnumerable<L12AbilityView>>(method.Invoke(game, [card.Id])))
        foreach (var term in forbidden)
            Assert.DoesNotContain(term, ability.Label);
    }

    private static L12CardInstance Create(string cardId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = $"audit-{cardId}", CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
        };
    }
}
