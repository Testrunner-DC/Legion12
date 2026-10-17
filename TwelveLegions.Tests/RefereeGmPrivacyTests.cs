using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RefereeGmPrivacyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static L12GameEngine Game(string mode = "all")
        => new(Catalog, "referee-privacy", "REFPRIV", 18821, ["甲", "乙"], [0, 1],
            skipPreparation: true, disasterMode: mode);
    private static L12GmCommand Judge(L12GmCommand command)
        => command with { RefereeAccountId = "synthetic-judge", RefereeName = "合成裁判",
            RefereeReason = "synthetic private correction" };

    [Fact]
    public void ReplacingDisasterHasOneNeutralNoticeAndSandboxDetailStillWorks()
    {
        var game = Game("custom"); game.InitializeGmDisasters();
        var replacement = Catalog.Cards.Values.First(card => card.CardType == "destruction"
            && card.Id != "S01-DS10" && game.State.CustomDisasters.All(current => current.CardId != card.Id));
        var sequence = game.State.EventSequence;
        Assert.True(game.HandleGm(Judge(new L12GmCommand("replaceDisaster", CardId: replacement.Id, Slot: 0))).Accepted);
        var notice = Assert.Single(game.State.Events, item => item.Sequence > sequence);
        Assert.Equal("裁判已调整本桌场面", notice.Text);
        Assert.Empty(notice.Cards);
        Assert.True(game.HandleGm(new L12GmCommand("addCard", CardId: "S01-0103", Destination: "hand")).Accepted);
        Assert.Contains("加入", game.State.LastAction!.Text);
        Assert.Single(game.State.LastAction.Cards);
    }

    [Fact]
    public void MillKeepsItsRealPublicDiscardButNotSandboxDetails()
    {
        var game = Game();
        Assert.True(game.HandleGm(Judge(new L12GmCommand("addCard", CardId: "S01-0103", Destination: "library-top"))).Accepted);
        var sequence = game.State.EventSequence;
        Assert.True(game.HandleGm(Judge(new L12GmCommand("mill", Value: 1))).Accepted);
        var events = game.State.Events.Where(item => item.Sequence > sequence).ToArray();
        Assert.Contains(events, item => item.Type == "mill" && item.Cards.Any(card => card.CardId == "S01-0103"));
        var notice = Assert.Single(events, item => item.Type == "gm");
        Assert.Equal("裁判已调整本桌场面", notice.Text);
        Assert.Empty(notice.Cards);
    }

    [Fact]
    public void RejectionAndExceptionCannotLeakTheTransientRefereeContextIntoSandbox()
    {
        var game = Game(); var sequence = game.State.EventSequence;
        Assert.False(game.HandleGm(Judge(new L12GmCommand("unknown"))).Accepted);
        Assert.Equal(sequence, game.State.EventSequence);
        var player = game.State.Players[0]; game.State.Players[0] = null!;
        try { Assert.Throws<NullReferenceException>(() => game.HandleGm(Judge(new L12GmCommand("setLife", Value: 18)))); }
        finally { game.State.Players[0] = player; }
        Assert.True(game.HandleGm(new L12GmCommand("addCard", CardId: "S01-0103", Destination: "hand")).Accepted);
        Assert.Single(game.State.LastAction!.Cards);
        Assert.Contains("加入", game.State.LastAction.Text);
    }
}
