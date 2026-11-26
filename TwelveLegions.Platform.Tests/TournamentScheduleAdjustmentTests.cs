using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// 玩家反馈（2026-11-26）：办赛前就得定好人数与瑞士轮数，报名人数不达预期时要能改。
// 这里用内建的 Admin 账号做主办者（新建 store 会种下它），只验赛前调整的语义本身。
[Collection("Platform environment")]
public sealed class TournamentScheduleAdjustmentTests
{
    private static (L12PlatformStore Store, L12AccountView Host, L12AdminAuditContext Context, string Root) Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-schedule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
        var host = store.Login("Admin", "L12master").Account!;
        var context = new L12AdminAuditContext("schedule-test", "tournaments.manage",
            RequestMethod: "TEST", RequestPath: "/test");
        return (store, host, context, root);
    }

    private static L12TournamentCreatePayload Swiss(int maxPlayers, int swissRounds) => new(
        Name: "赛前调整演练", Format: "swiss", Visibility: "public", MaxPlayers: maxPlayers,
        StartAt: DateTimeOffset.UtcNow.AddHours(2), Ruleset: "official", Description: "",
        DeckVisibility: "after", DisasterMode: "none", BanList: "", RoundMinutes: 50, CheckInMinutes: 5,
        SwissRounds: swissRounds);

    [Fact]
    public void OrganizerCanAdjustPlayersAndRoundsBeforeTheFirstRound()
    {
        var (store, host, context, root) = Create();
        try
        {
            var tournament = store.CreateTournament(host, Swiss(16, 4), context, true);
            var current = store.Tournament(host, tournament.Id)!;
            var updated = store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 8, SwissRounds: 3), current.Version, context, true);
            Assert.Equal(8, updated.MaxPlayers);
            Assert.Equal(3, updated.SwissRounds);
            Assert.True(updated.Version > current.Version);
            // 预览不落盘、不推进版本。
            var previewVersion = updated.Version;
            var preview = store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 12, SwissRounds: 5), previewVersion, context, false);
            Assert.Equal(12, preview.MaxPlayers);
            Assert.Equal(5, preview.SwissRounds);
            Assert.Equal(previewVersion, store.Tournament(host, tournament.Id)!.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PlayerCapMustStayInRangeAndRejectStaleVersion()
    {
        var (store, host, context, root) = Create();
        try
        {
            var tournament = store.CreateTournament(host, Swiss(16, 4), context, true);
            var current = store.Tournament(host, tournament.Id)!;
            var below = Assert.Throws<ArgumentException>(() => store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 1, SwissRounds: null), current.Version, context, true));
            Assert.Contains("2–256", below.Message);
            Assert.Throws<L12TournamentVersionConflictException>(() => store.SetTournamentSchedule(host,
                tournament.Id, new L12TournamentSchedulePayload(MaxPlayers: 8, SwissRounds: null),
                current.Version - 1, context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SingleEliminationRejectsRoundAdjustments()
    {
        var (store, host, context, root) = Create();
        try
        {
            var tournament = store.CreateTournament(host, Swiss(16, 4) with { Format = "single" }, context, true);
            var current = store.Tournament(host, tournament.Id)!;
            Assert.Throws<ArgumentException>(() => store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: null, SwissRounds: 3), current.Version, context, true));
            var resized = store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 8, SwissRounds: null), current.Version, context, true);
            Assert.Equal(8, resized.MaxPlayers);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    [InlineData(64, 6)]
    public void SuggestedRoundsFollowCeilingLog2(int participants, int expected)
        => Assert.Equal(expected, L12PlatformStore.SuggestedSwissRounds(participants));
}
