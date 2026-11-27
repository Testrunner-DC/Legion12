using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// 赛事资质判定（用户 2026-11-26）：管理员角色自带，其余账号要求 approved。
[Collection("Platform environment")]
public sealed class TournamentEligibilityDecisionTests
{
    [Theory]
    [InlineData("admin", null, true)]
    [InlineData("admin", "none", true)]
    [InlineData("player", "approved", true)]
    [InlineData("player", "pending", false)]
    [InlineData("player", "rejected", false)]
    [InlineData("player", null, false)]
    [InlineData("player", "none", false)]
    [InlineData("", "approved", true)]
    public void AdminIsAlwaysEligibleAndPlayersNeedApproval(string role, string? status, bool expected)
        => Assert.Equal(expected, L12PlatformStore.TournamentEligibilityAllows(role, status));

    // 用户 2026-11-27：申请并通过之后，必须在「账号、权限与会话」里看出这个账号到底有没有资质。
    [Fact]
    public void AccountViewExposesTheStoredTournamentEligibilityStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-eligibility-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
        var admin = store.Login("Admin", "L12master").Account!;
        var player = store.Register("资质显示", "Password123!").Account!;

        Assert.Equal("none", store.Account(player.Id)!.TournamentEligibilityStatus);

        var audit = new L12AdminAuditContext("eligibility-view-test");
        store.ApplyTournamentEligibility(player, new L12TournamentEligibilityApplyPayload("想在本赛季创建并组织一场公开赛事"), audit);
        Assert.Equal("pending", store.Account(player.Id)!.TournamentEligibilityStatus);

        store.ReviewTournamentEligibility(admin, player.Id, new L12TournamentEligibilityReviewPayload(true, "资料齐全"), null, audit);
        Assert.Equal("approved", store.Account(player.Id)!.TournamentEligibilityStatus);
        Assert.Equal("approved", store.ReadTournamentEligibility(player.Id).Status);
        // 列表接口走同一个 ToView，因此列表与详情都能看到状态。
        Assert.Equal("approved", store.Accounts().Single(row => row.Id == player.Id).TournamentEligibilityStatus);

        store.RevokeTournamentEligibility(admin, player.Id, new L12TournamentEligibilityRevokePayload("验收撤销"), null, audit);
        Assert.Equal("none", store.Account(player.Id)!.TournamentEligibilityStatus);
    }
}