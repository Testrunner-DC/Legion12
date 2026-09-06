using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptAuditLogTests
{
    private static L12GameEngine Create() => new(
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data")), "log-audit", "LOGTEST", 20260906,
        ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: true);

    private static void Log(L12GameEngine game, L12Prompt prompt, params string[] choices)
        => typeof(L12GameEngine).GetMethod("AddResolvedPromptLog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [prompt, choices]);

    [Fact]
    public void RejectedContinuationDoesNotPublishResolvedAudit()
    {
        var game = Create();
        var prompt = Prompt(false);
        game.State.PendingPrompts.Add(prompt);
        var result = (CommandResult)typeof(L12GameEngine).GetMethod("ResolvePrompt", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:draw")])!;
        Assert.False(result.Accepted);
        Assert.DoesNotContain(game.State.Events, item => item.Type == "prompt-resolved");
    }

    [Theory]
    [InlineData("setup-ban", 1)]
    [InlineData("setup-initiative", 1)]
    [InlineData("trigger-batch-order", 1)]
    [InlineData("s2-prayer-public-confirm", 0)]
    public void DedicatedEventsAndAcknowledgementsDoNotGetDuplicateChoiceLog(string continuation, int max)
    {
        var game = Create();
        var before = game.State.Events.Count;
        var prompt = new L12Prompt { PromptId = "dedicated", PlayerIndex = 0, Kind = "test",
            Text = "已经有专门事件", Continuation = continuation, ValidChoices = ["one"], MaxChoose = max };
        Log(game, prompt, max == 0 ? [] : ["one"]);
        Assert.Equal(before, game.State.Events.Count);
    }

    private static L12Prompt Prompt(bool hidden) => new()
    {
        PromptId = "test-prompt", PlayerIndex = 0, Kind = "test", Continuation = "test",
        Text = hidden ? "隐藏的牌库内容" : "选择一个公开效果", IsPrivate = hidden,
        ValidChoices = ["mode:draw"], MinChoose = 1, MaxChoose = 1,
        ChoiceLabels = new() { ["mode:draw"] = "抽取1张牌" },
        Data = new() { ["mode:draw"] = "internal-secret-data" },
    };

    [Fact]
    public void PrivateChoiceDoesNotPublishPromptTextChosenIdentityOrChoiceLabels()
    {
        var game = Create();
        Log(game, Prompt(true), "private-hand-instance");
        var entry = game.State.Events.Last();
        Assert.Equal("prompt-resolved", entry.Type);
        Assert.Equal("甲 已完成非公开选择", entry.Text);
        Assert.Empty(entry.Cards);
        Assert.DoesNotContain("隐藏", entry.Text);
        Assert.DoesNotContain("private-hand-instance", entry.Text);
    }

    [Fact]
    public void PublicChoiceUsesReadableChoiceLabelsInsteadOfInternalData()
    {
        var game = Create();
        Log(game, Prompt(false), "mode:draw");
        var entry = game.State.Events.Last();
        Assert.Contains("选择一个公开效果 → 抽取1张牌", entry.Text);
        Assert.DoesNotContain("internal-secret-data", entry.Text);
    }

    [Fact]
    public void HiddenLegionIsNeverAttachedToPublicChoiceLog()
    {
        var game = Create();
        var hidden = new L12CardInstance { InstanceId = "hidden-unit", CardId = "TEST-SECRET",
            Name = "隐藏军团", CardType = "legion", Faction = "neutral", Hidden = true };
        game.State.Players[0].Field[0][0] = hidden;
        Log(game, Prompt(false), hidden.InstanceId);
        var entry = game.State.Events.Last();
        Assert.Empty(entry.Cards);
        Assert.DoesNotContain(hidden.Name, entry.Text);
        Assert.DoesNotContain(hidden.InstanceId, entry.Text);
    }

    [Fact]
    public void PublicGraveyardSelectionIncludesCardIdentityAndSkipIsReadable()
    {
        var game = Create();
        var card = new L12CardInstance { InstanceId = "public-unit", CardId = "TEST-OPEN",
            Name = "公开军团", CardType = "legion", Faction = "neutral" };
        game.State.Players[0].Graveyard.Add(card);
        Log(game, Prompt(false), card.InstanceId);
        Assert.Contains("〈公开军团〉", game.State.Events.Last().Text);
        Assert.Equal("TEST-OPEN", Assert.Single(game.State.Events.Last().Cards).CardId);
        Log(game, Prompt(false), "skip");
        Assert.Contains("不发动 / 跳过", game.State.Events.Last().Text);
    }
}
