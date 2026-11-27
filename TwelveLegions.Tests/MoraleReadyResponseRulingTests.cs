using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// 用户 2026-11-27 裁定：士气本质也是卡牌，「由效果把休整士气转为活跃」属于「卡牌重置为活跃」，
// 必须能被响应（如〈毒药发作〉S02-0018 对 effect-ready 事件的响应）；但回合／阶段的规则性重置
// （GameEngine.Untap）不算。这里锁住这两条边界。
public sealed class MoraleReadyResponseRulingTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "morale-ready-ruling", "MORALE-READY", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static object? Invoke(L12GameEngine game, string method, params object?[] args)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, args);

    [Fact]
    public void RuleResetDoesNotQueueAuthorityEventOrResponseWindow()
    {
        var game = Create(20261127);
        var player = game.State.Players[0];
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "ruling-rested-morale", Tapped = true,
        });

        Invoke(game, "Untap", player);

        Assert.False(player.Morale.Single(card => card.InstanceId == "ruling-rested-morale").Tapped);
        // 规则重置：不发权威事件、不排延迟项、不开响应窗。
        Assert.Empty(game.State.AuthorityEvents);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
    }

    [Fact]
    public void ResponsePromptNamesTheDeclaredTarget()
    {
        var game = Create(20261128);
        var player = game.State.Players[0];
        var legion = player.Library.First();
        player.Library.Remove(legion);
        player.Field[0][0] = legion;

        var top = new L12StackItem
        {
            StackItemId = "ruling-medjed-debuff", Controller = 0,
            SourceInstanceId = "ruling-medjed-source", SourceCardId = "S01-02M3", SourceName = "梅杰德",
            Trigger = "active", Text = "梅杰德使对方1张军团本回合兵力-3000",
        };
        // 声明阶段在权威侧写入展示串（提示侧只读它，不做权威查询）。
        top.Data["responseTargetSummary"] = "〈" + legion.Name + "〉";

        var text = (string)Invoke(game, "BuildResponsePromptText", top)!;
        Assert.Contains(legion.Name, text, StringComparison.Ordinal);
        Assert.Contains("目标：", text, StringComparison.Ordinal);
    }
}