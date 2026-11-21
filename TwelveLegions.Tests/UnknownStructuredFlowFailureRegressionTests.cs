using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// A2 回归守卫：没有匹配结算流程的卡效必须留下可见失败，而不是静默完成本段；
/// 由响应管线结算的触发器落到同一 default 分支时仍按正常结束处理。
/// </summary>
public sealed class UnknownStructuredFlowFailureRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void UnknownCardEffectFlowRecordsVisibleFailureInsteadOfFinishingSilently()
    {
        // 前置：该卡在该触发器下没有细原子程序，否则请求会被原子程序接管而不是落到默认分支。
        Assert.Null(L12VerifiedAtomicPrograms.Find(SourceCardId, "effect"));
        var game = Create(20261121);
        var item = UnknownFlowItem("effect");
        game.State.EffectStack.Add(item);

        ResolveCardEffect(game, item);

        Assert.Equal("failed", item.Data.GetValueOrDefault("effectResultStatus"));
        var failed = Assert.Single(game.State.Events, entry => entry.Type == "effect-failed");
        Assert.Contains("没有匹配的结算流程", failed.Text, StringComparison.Ordinal);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void PipelineCompletionTriggersStillFinishWithoutAFailure()
    {
        var game = Create(20261122);
        var item = UnknownFlowItem("response-block");
        game.State.EffectStack.Add(item);

        ResolveCardEffect(game, item);

        Assert.Null(item.Data.GetValueOrDefault("effectResultStatus"));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-failed");
        Assert.Empty(game.State.EffectStack);
    }

    private const string SourceCardId = "S01-0111";
    private const string SourceName = "诸葛亮";

    private static L12StackItem UnknownFlowItem(string trigger) => new()
    {
        StackItemId = "unknown-flow-stack-item",
        Controller = 0,
        SourceInstanceId = "unknown-flow-source",
        SourceCardId = SourceCardId,
        SourceName = SourceName,
        Trigger = trigger,
        Text = "用于验证未知结算流程的可见失败",
    };

    private static void ResolveCardEffect(L12GameEngine game, L12StackItem item)
    {
        var method = typeof(L12GameEngine).GetMethod("ResolveCardEffect",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ResolveCardEffect not found");
        method.Invoke(game, [item]);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "unknown-structured-flow", "UNKNOWN-FLOW", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        return game;
    }
}
