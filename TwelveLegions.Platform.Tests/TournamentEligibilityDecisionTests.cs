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
}
