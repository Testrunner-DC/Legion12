using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ResponseScopeCatalogTests
{
    private static readonly string DataPath = Path.Combine(AppContext.BaseDirectory, "Data");

    [Fact]
    public void ReviewedS1LineBreaksProduceIndependentResponseScopes()
    {
        var catalog = L12Catalog.Load(DataPath);

        var scout = catalog.AtomicEffects.Find("S01-0013")!;
        Assert.Collection(scout.ResponseScopes,
            scope =>
            {
                Assert.Null(scope.CostText);
                Assert.Equal("查看对方所有手牌。", scope.ResolutionText);
            },
            scope =>
            {
                Assert.Equal("可消耗1士气：", scope.CostText);
                Assert.Equal("对方选择其1张手牌洗回牌库。", scope.ResolutionText);
            });

        var amaterasu = catalog.AtomicEffects.Find("S01-04M1")!;
        Assert.Collection(amaterasu.ResponseScopes,
            scope =>
            {
                Assert.Equal("我方 回合1次 可消耗1士气：", scope.CostText);
                Assert.Equal("选择对方1张军团，本回合费用-1。随后击杀对方1张费用为0的军团。", scope.ResolutionText);
            },
            scope =>
            {
                Assert.Equal("我方 回合1次 可弃置1张手牌：", scope.CostText);
                Assert.Equal("将我方最多2张士气转为活跃。我方前排所有【高天原】军团本回合兵力+1000。", scope.ResolutionText);
            });
    }

    [Fact]
    public void OptionBulletsStayTogetherUnlessEachBranchHasItsOwnCost()
    {
        var catalog = L12Catalog.Load(DataPath);

        var arrowVolley = catalog.AtomicEffects.Find("S01-0005")!;
        var scope = Assert.Single(arrowVolley.ResponseScopes);
        Assert.Null(scope.CostText);
        Assert.Contains("·选择对方前排或后排所有军团", scope.ResolutionText, StringComparison.Ordinal);
        Assert.Contains("·选择对方1张军团", scope.ResolutionText, StringComparison.Ordinal);

        var landscape = catalog.AtomicEffects.Find("S01-0117")!;
        Assert.Equal(3, landscape.ResponseScopes.Count);
        Assert.Null(landscape.ResponseScopes[0].CostText);
        Assert.Equal("主动休整 选择以下一项。\n·返还1活跃士气：", landscape.ResponseScopes[1].CostText);
        Assert.Equal("抽取1张牌。", landscape.ResponseScopes[1].ResolutionText);
        Assert.Equal("主动休整 选择以下一项。\n·弃置1张手牌：", landscape.ResponseScopes[2].CostText);
    }

    [Fact]
    public void SameLineTriggersAndSubsequentTextDoNotCreateExtraScopes()
    {
        var catalog = L12Catalog.Load(DataPath);
        var zhuge = catalog.AtomicEffects.Find("S01-0111")!;

        Assert.Equal(3, zhuge.ResponseScopes.Count);
        Assert.Equal("进攻时/阵亡时 可返还1士气：", zhuge.ResponseScopes[2].CostText);
        Assert.Contains("若展示的是【圣物】", zhuge.ResponseScopes[2].ResolutionText, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategicTransferPreservesApprovedColonCostBoundary()
    {
        var catalog = L12Catalog.Load(DataPath);
        var scope = Assert.Single(catalog.AtomicEffects.Find("S01-0009")!.ResponseScopes);

        Assert.Equal("选择我方1张军团回到所有者手牌：", scope.CostText);
        Assert.Equal("选择我方1张军团，本回合兵力+2000。", scope.ResolutionText);
    }

    [Fact]
    public void DisastersNeverExposeResponseScopes()
    {
        var catalog = L12Catalog.Load(DataPath);
        var disasters = catalog.AtomicEffects.All
            .Where(card => card.CardType is "disaster" or "destruction").ToArray();
        var nonDisasters = catalog.AtomicEffects.All
            .Where(card => card.CardType is not ("disaster" or "destruction")).ToArray();

        Assert.Equal(19, disasters.Length);
        Assert.Equal(305, nonDisasters.Length);
        Assert.All(disasters, card => Assert.Empty(card.ResponseScopes));
        Assert.All(nonDisasters.Where(card => card.EffectText != "无效果"), card => Assert.NotEmpty(card.ResponseScopes));
        Assert.All(nonDisasters.SelectMany(card => card.ResponseScopes), scope =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scope.ResolutionText));
            Assert.Equal(scope.Sequence, int.Parse(scope.ScopeId.Split(':')[^1]));
        });
    }
}
