using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LobbyDeckSelectionPolicyTests
{
    [Fact]
    public void DeckSelectionScopesKeepSeasonRestrictionsOutOfCasualFriendlyOptOutAndSandbox()
    {
        var restriction = new L12CardRestrictionConfig("S01-0001", 0, "赛季禁用");
        var policy = new L12OperationsPolicySnapshot(
            7,
            "ops-selection-scope",
            new L12SeasonConfig("S-test", "测试赛季", "active", null, null),
            Enumerable.Range(1, 10).Select(index => $"S01-DS{index:00}").ToArray(),
            [restriction],
            ["S01-01M1"],
            new L12DefaultRoomConfig("casual", "public", "request", "all"),
            [new L12MatchModeConfig("ranked", "排位", true),
                new L12MatchModeConfig("casual", "休闲", true)],
            new Dictionary<string, bool>(),
            new L12MaintenanceConfig(false, string.Empty, null, null));

        var ranked = policy.ForRankedMatch();
        Assert.Equal("ranked", ranked.DefaultRoomConfig.MatchModeId);
        Assert.Equal("season", ranked.DefaultRoomConfig.DisasterMode);
        Assert.Same(restriction, Assert.Single(ranked.CardRestrictions));
        Assert.Equal(10, ranked.DisasterCardIds.Count);

        var casual = policy.ForCasualMatch();
        Assert.Equal("casual", casual.DefaultRoomConfig.MatchModeId);
        Assert.Empty(casual.CardRestrictions);
        Assert.Empty(casual.DisasterCardIds);

        Assert.Same(restriction, Assert.Single(policy.ForFriendlyRoom(true, "random").CardRestrictions));
        Assert.Empty(policy.ForFriendlyRoom(false, "all").CardRestrictions);

        foreach (var disasterMode in new[] { "all", "random", "custom", "none" })
        {
            var sandbox = policy.ForSandbox(disasterMode);
            Assert.Equal("sandbox", sandbox.DefaultRoomConfig.MatchModeId);
            Assert.Equal(disasterMode, sandbox.DefaultRoomConfig.DisasterMode);
            Assert.Empty(sandbox.CardRestrictions);
            Assert.Empty(sandbox.DisasterCardIds);
        }
    }
}
