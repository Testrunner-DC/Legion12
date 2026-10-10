using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerResultReceiptTests
{
    [Fact]
    [Trait("L12Evidence", "player-log:composite-cost-identity")]
    public void EqualTextFromTwoCommittedSegmentsRemainsTwoPayments()
    {
        var item = new L12StackItem
        {
            StackItemId = "cost-root", Controller = 0, SourceInstanceId = "source",
            SourceCardId = "S02-0105", SourceName = "来源卡", Trigger = "play", Text = "效果",
        };
        var record = typeof(L12GameEngine).GetMethod("RecordCompositeSegmentPaidCost",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        record.Invoke(null, [item, "first", "消耗1士气"]);
        record.Invoke(null, [item, "first", "消耗1士气"]);
        record.Invoke(null, [item, "second", "消耗1士气"]);

        Assert.Equal("消耗1士气；消耗1士气", item.Data["paidCostSummary"]);
        Assert.Equal(2, item.Data.Keys.Count(key => key.StartsWith("compositePaidCostReceipt:",
            StringComparison.Ordinal)));
    }
}
