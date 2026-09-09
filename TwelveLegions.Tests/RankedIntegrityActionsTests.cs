using System.Collections;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedIntegrityActionsTests
{
    [Fact]
    public void OrdinarySurrenderIsNotAnAbnormalSignal()
    {
        var fixture = Create("ordinary-surrender");
        var endedAt = DateTimeOffset.UtcNow;

        fixture.Store.SettleRankedMatch("ordinary-surrender-match", fixture.First.Id,
            fixture.Second.Id, 1, integrity: new L12RankedIntegrityContext(
                endedAt.AddMinutes(-8), endedAt, 12, "surrender", null, null, 8));

        var audit = Assert.Single(fixture.Store.RankedIntegrityAudits(fixture.Admin,
            matchId: "ordinary-surrender-match"));
        Assert.DoesNotContain(audit.Signals, signal => signal.Code == "abnormal-surrender");
        Assert.False(audit.ReviewRecommended);
        Assert.Equal("none", audit.Enforcement);
    }

    [Fact]
    public void ThirdRepeatedUnilateralExtremeZeroActionMatchIsHeldAndNotified()
    {
        var fixture = Create("automatic-hold");
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-3);
        for (var index = 0; index < 2; index++)
        {
            var endedAt = baseTime.AddMinutes(index);
            var result = fixture.Store.SettleRankedMatch($"hold-sequence-{index}", fixture.First.Id,
                fixture.Second.Id, 0, integrity: new L12RankedIntegrityContext(
                    endedAt.AddSeconds(-45), endedAt, 0, "surrender", null, null, 1));
            Assert.Equal("applied", result.First.RewardStatus);
        }
        var before = fixture.Store.RankedProfile(fixture.First.Id);
        var thirdEndedAt = baseTime.AddMinutes(2);

        var held = fixture.Store.SettleRankedMatch("hold-sequence-2", fixture.First.Id,
            fixture.Second.Id, 0, integrity: new L12RankedIntegrityContext(
                thirdEndedAt.AddSeconds(-45), thirdEndedAt, 0, "surrender", null, null, 1));

        Assert.Equal("held", held.First.RewardStatus);
        Assert.Equal(0, held.First.EffectiveDelta);
        Assert.Equal(held.First.Delta, held.First.PendingDelta);
        Assert.Equal(before.PlacementPlayed, fixture.Store.RankedProfile(fixture.First.Id).PlacementPlayed);
        Assert.Contains("hold-sequence-2", fixture.Store.RankedIntegrityExcludedMatchIds());
        Assert.DoesNotContain("hold-sequence-1", fixture.Store.RankedIntegrityExcludedMatchIds());
        Assert.NotNull(fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
        var notification = Assert.Single(fixture.Store.RankedIntegrityNotifications(fixture.First,
            unreadOnly: true).Items);
        Assert.Equal("review", notification.Outcome);
        Assert.Contains("暂扣", notification.Reason);
        Assert.True(fixture.Store.AcknowledgeRankedIntegrityNotification(fixture.First, notification.Id));
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.First, unreadOnly: true).Items);
        Assert.Single(fixture.Store.RankedIntegrityNotifications(fixture.First).Items);

        var reloaded = Reload(fixture);
        Assert.Equal("held", reloaded.Store.RankedSettlement("hold-sequence-2", fixture.First.Id)!.RewardStatus);
        Assert.Contains("hold-sequence-2", reloaded.Store.RankedIntegrityProtectedMatchIds());
    }

    [Fact]
    public void ReviewThenConfirmedVoidsExactLatestSuffixAndRequiresExplicitRevocation()
    {
        var fixture = Create("review-confirm");
        CompletePlacement(fixture);
        var beforeFirst = fixture.Store.RankedProfile(fixture.First.Id);
        var beforeSecond = fixture.Store.RankedProfile(fixture.Second.Id);
        var beforeFirstRating = fixture.Store.HiddenRating(fixture.First.Id);
        var beforeSecondRating = fixture.Store.HiddenRating(fixture.Second.Id);
        fixture.Store.SettleRankedMatch("review-confirm-5", fixture.First.Id, fixture.Second.Id, 0);
        fixture.Store.SettleRankedMatch("review-confirm-6", fixture.First.Id, fixture.Second.Id, 0);

        var reviewInput = Input("review-request", "review",
            ["review-confirm-5", "review-confirm-6"]);
        var reviewPreview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, reviewInput);
        Assert.True(reviewPreview.CanConfirm);
        fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, reviewInput,
            reviewPreview.Revision, Audit("review-request"));
        Assert.DoesNotContain("review-confirm-5", fixture.Store.RankedIntegrityExcludedMatchIds());

        var confirmedInput = Input("confirmed-request", "confirmed",
            ["review-confirm-5", "review-confirm-6"], [fixture.First.Id], 14);
        var confirmedPreview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, confirmedInput);
        Assert.True(confirmedPreview.CanConfirm, string.Join(" | ", confirmedPreview.BlockingReasons));
        var decision = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, confirmedInput,
            confirmedPreview.Revision, Audit("confirmed-request"));

        Assert.Equal("confirmed", decision.EffectiveDisposition);
        AssertProfilesEqual(beforeFirst, fixture.Store.RankedProfile(fixture.First.Id));
        AssertProfilesEqual(beforeSecond, fixture.Store.RankedProfile(fixture.Second.Id));
        Assert.Equal(beforeFirstRating, fixture.Store.HiddenRating(fixture.First.Id), 8);
        Assert.Equal(beforeSecondRating, fixture.Store.HiddenRating(fixture.Second.Id), 8);
        Assert.Equal("voided", fixture.Store.RankedSettlement("review-confirm-6", fixture.First.Id)!.RewardStatus);
        Assert.NotNull(fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
        Assert.Null(fixture.Store.RankedEntryBlock(fixture.Second.Id, DateTimeOffset.UtcNow));

        var forbiddenNormal = Input("normal-after-confirmed", "normal", ["review-confirm-5", "review-confirm-6"]);
        var forbiddenPreview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, forbiddenNormal);
        Assert.False(forbiddenPreview.CanConfirm);
        Assert.Contains(forbiddenPreview.BlockingReasons, reason => reason.Contains("已有生效终局处置"));
    }

    [Fact]
    public void RevocationAppendsInverseCorrectionLiftsRestrictionAndKeepsHistory()
    {
        var fixture = Create("revoke");
        CompletePlacement(fixture);
        fixture.Store.SettleRankedMatch("revoke-latest", fixture.First.Id, fixture.Second.Id, 0);
        var appliedFirst = fixture.Store.RankedProfile(fixture.First.Id);
        var appliedSecond = fixture.Store.RankedProfile(fixture.Second.Id);
        var appliedFirstRating = fixture.Store.HiddenRating(fixture.First.Id);
        var action = Input("confirm-before-revoke", "confirmed", ["revoke-latest"],
            [fixture.First.Id], 7);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, action);
        var original = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, action,
            preview.Revision, Audit("confirm-before-revoke"));

        var revoke = Input("explicit-revoke", "revoked", ["revoke-latest"],
            revokesDecisionId: original.DecisionId);
        var revokePreview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, revoke);
        Assert.True(revokePreview.CanConfirm);
        fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, revoke,
            revokePreview.Revision, Audit("explicit-revoke"));

        AssertProfilesEqual(appliedFirst, fixture.Store.RankedProfile(fixture.First.Id));
        AssertProfilesEqual(appliedSecond, fixture.Store.RankedProfile(fixture.Second.Id));
        Assert.Equal(appliedFirstRating, fixture.Store.HiddenRating(fixture.First.Id), 8);
        Assert.Null(fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
        Assert.Equal("applied", fixture.Store.RankedSettlement("revoke-latest", fixture.First.Id)!.RewardStatus);
        var originalAfter = fixture.Store.RankedIntegrityDecisions(fixture.Admin).Items
            .Single(row => row.DecisionId == original.DecisionId);
        Assert.Equal("confirmed", originalAfter.Disposition);
        Assert.Equal("revoked", originalAfter.EffectiveDisposition);
        var notices = fixture.Store.RankedIntegrityNotifications(fixture.First).Items;
        Assert.Equal(2, notices.Count);
        Assert.All(notices, notice => Assert.Equal("revoked", notice.Outcome));
        Assert.Contains(notices, notice => notice.RelatedDecisionId == original.DecisionId);
    }

    [Fact]
    public void RequestIdsRevisionAndAppealRepliesAreDurableAndScoped()
    {
        var fixture = Create("appeal");
        CompletePlacement(fixture);
        fixture.Store.SettleRankedMatch("appeal-match", fixture.First.Id, fixture.Second.Id, 0);
        var input = Input("idempotent-confirm", "confirmed", ["appeal-match"],
            [fixture.First.Id], 30);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        var decision = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, input,
            preview.Revision, Audit("idempotent-confirm"));
        Assert.Equal(decision.DecisionId, fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin,
            input, -1, Audit("idempotent-replay")).DecisionId);
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.ConfirmRankedIntegrityAction(
            fixture.Admin, input with { Reason = "冲突复用" }, preview.Revision, Audit("conflict")));

        var appeal = fixture.Store.SubmitRankedIntegrityAppeal(fixture.First, decision.DecisionId,
            "appeal-request", "我对该处置有异议，请复核证据。");
        Assert.Equal("open", appeal.Status);
        Assert.Equal(appeal.Id, fixture.Store.SubmitRankedIntegrityAppeal(fixture.First,
            decision.DecisionId, "appeal-request", "我对该处置有异议，请复核证据。").Id);
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.SubmitRankedIntegrityAppeal(
            fixture.First, decision.DecisionId, "second-open-appeal", "重复申诉"));
        var unrelated = fixture.Store.Register("unrelatedplayer", "Password123!").Account!;
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.SubmitRankedIntegrityAppeal(
            unrelated, decision.DecisionId, "unrelated-appeal", "不属于我的处置"));

        var reviewed = fixture.Store.ReviewRankedIntegrityAppeal(fixture.Admin, appeal.Id,
            new L12RankedIntegrityAppealReviewInput("appeal-answer", appeal.Revision, "answered",
                "已完成复核；如需解除限制，仍须另行撤销原处置。"), Audit("appeal-answer"));
        Assert.Equal("answered", reviewed.Status);
        Assert.Contains("另行撤销", reviewed.Reply);
        Assert.NotNull(fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
        Assert.Contains(fixture.Store.RankedIntegrityNotifications(fixture.First, unreadOnly: true).Items,
            row => row.Outcome == "appeal-answered" && row.DecisionId == decision.DecisionId);

        var reloaded = Reload(fixture);
        Assert.Equal("answered", Assert.Single(reloaded.Store.RankedIntegrityAppealsForPlayer(
            fixture.First).Items).Status);
    }

    [Fact]
    public void NonLatestOrResetProfileCorrectionsAreBlockedInsteadOfGuessed()
    {
        var fixture = Create("blocked-chain");
        CompletePlacement(fixture);
        fixture.Store.SettleRankedMatch("blocked-old", fixture.First.Id, fixture.Second.Id, 0);
        fixture.Store.SettleRankedMatch("blocked-new", fixture.First.Id, fixture.Second.Id, 1);

        var nonLatest = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin,
            Input("blocked-non-latest", "confirmed", ["blocked-old"]));
        Assert.False(nonLatest.CanConfirm);
        Assert.Contains(nonLatest.BlockingReasons, reason => reason.Contains("连续最新结算后缀"));

        fixture.Store.SelectRankedFaction(fixture.First.Id, "fate");
        var reset = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin,
            Input("blocked-reset", "confirmed", ["blocked-new"]));
        Assert.False(reset.CanConfirm);
        Assert.Contains(reset.BlockingReasons, reason => reason.Contains("跨赛季或切换派系"));
    }

    [Theory]
    [InlineData(1500d, 1500d, 0)]
    [InlineData(1175.25d, 1920.75d, 1)]
    [InlineData(2310.125d, 610.875d, 0)]
    public void HistoricalEloInverseIsUniqueAndRoundTripsWithoutRounding(double firstBefore,
        double secondBefore, int winner)
    {
        var expectedFirst = 1d / (1d + Math.Pow(10d, (secondBefore - firstBefore) / 400d));
        var firstAfter = Math.Clamp(firstBefore + 24d * ((winner == 0 ? 1d : 0d) - expectedFirst),
            500d, 2500d);
        var secondAfter = Math.Clamp(secondBefore + 24d * ((winner == 1 ? 1d : 0d)
            - (1d - expectedFirst)), 500d, 2500d);

        Assert.True(L12PlatformStore.TryReverseRankedElo(firstAfter, secondAfter, winner,
            out var recoveredFirst, out var recoveredSecond));
        Assert.Equal(firstBefore, recoveredFirst, 7);
        Assert.Equal(secondBefore, recoveredSecond, 7);
        Assert.False(L12PlatformStore.TryReverseRankedElo(500d, secondAfter, winner,
            out _, out _));
    }

    [Fact]
    public void LegacyLatestSuffixUsesExactEloInverseWhenProfileFactsAreUnavailable()
    {
        var fixture = Create("legacy-inverse");
        CompletePlacement(fixture);
        var beforeFirstRating = fixture.Store.HiddenRating(fixture.First.Id);
        var beforeSecondRating = fixture.Store.HiddenRating(fixture.Second.Id);
        fixture.Store.SettleRankedMatch("legacy-inverse-5", fixture.First.Id, fixture.Second.Id, 0);
        fixture.Store.SettleRankedMatch("legacy-inverse-6", fixture.First.Id, fixture.Second.Id, 0);
        ClearProfileFacts(fixture.Store);

        var action = Input("legacy-inverse-confirm", "confirmed",
            ["legacy-inverse-5", "legacy-inverse-6"]);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, action);
        Assert.True(preview.CanConfirm);
        fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, action, preview.Revision,
            Audit("legacy-inverse-confirm"));

        Assert.Equal(beforeFirstRating, fixture.Store.HiddenRating(fixture.First.Id), 7);
        Assert.Equal(beforeSecondRating, fixture.Store.HiddenRating(fixture.Second.Id), 7);
    }

    private static Fixture Create(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-integrity-actions",
            $"{name}-{Guid.NewGuid():N}");
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var first = store.Register("fir" + Guid.NewGuid().ToString("N")[..8], "Password123!").Account!;
        var second = store.Register("sec" + Guid.NewGuid().ToString("N")[..8], "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        return new(directory, store, first, second, store.Login("Admin", "L12master").Account!);
    }

    private static Fixture Reload(Fixture fixture)
        => fixture with { Store = new L12PlatformStore(Path.Combine(fixture.Directory, "platform.json")) };

    private static void CompletePlacement(Fixture fixture)
    {
        for (var index = 0; index < 5; index++)
            fixture.Store.SettleRankedMatch($"{Path.GetFileName(fixture.Directory)}-placement-{index}",
                fixture.First.Id, fixture.Second.Id, index % 2);
    }

    private static L12RankedIntegrityActionInput Input(string requestId, string disposition,
        IReadOnlyList<string> matchIds, IReadOnlyList<string>? restricted = null,
        int? days = null, string? revokesDecisionId = null)
        => new(requestId, disposition, matchIds, restricted ?? [], days,
            "服务器结算、完整性信号与对局证据已交叉核对。", "依据排位完整性规则作出本次处置。",
            revokesDecisionId);

    private static L12AdminAuditContext Audit(string id) => new(id);

    private static void AssertProfilesEqual(L12RankedProfileView expected, L12RankedProfileView actual)
    {
        Assert.Equal(expected.SevenValue, actual.SevenValue);
        Assert.Equal(expected.PlacementPlayed, actual.PlacementPlayed);
        Assert.Equal(expected.PlacementWins, actual.PlacementWins);
        Assert.Equal(expected.Wins, actual.Wins);
        Assert.Equal(expected.Losses, actual.Losses);
        Assert.Equal(expected.WinStreak, actual.WinStreak);
        Assert.Equal(expected.LossStreak, actual.LossStreak);
    }

    private static void ClearProfileFacts(L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        var facts = (IList)data.GetType().GetProperty("RankedSettlementProfileFacts")!.GetValue(data)!;
        facts.Clear();
    }

    private sealed record Fixture(string Directory, L12PlatformStore Store,
        L12AccountView First, L12AccountView Second, L12AccountView Admin);
}
