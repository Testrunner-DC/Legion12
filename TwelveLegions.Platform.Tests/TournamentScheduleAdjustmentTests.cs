using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// 玩家反馈（2026-11-26）：办赛前就得定好人数与瑞士轮数，报名人数不达预期时要能改。
// 规则：只在还没开始任何一轮的赛前阶段可改；人数上限不得小于已报名人数；单败赛制没有瑞士轮数。
[Collection("Platform environment")]
public sealed class TournamentScheduleAdjustmentTests
{
    private static (L12PlatformStore Store, L12AccountView Host, L12AdminAuditContext Context, string Root) Create(string format)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-schedule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
        var host = store.Register("schedulehost", "password-123").Account!;
        var context = new L12AdminAuditContext("schedule-test", "tournaments.manage",
            RequestMethod: "TEST", RequestPath: "/test");
        _ = format;
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
        var (store, host, context, root) = Create("swiss");
        try
        {
            var stage = "create";
            L12TournamentView tournament = default!;
            try
            {
            tournament = store.CreateTournament(host, Swiss(16, 4), context, true);
            stage = "register";
            store.RegisterTournament(host, tournament.Id, new L12TournamentRegistrationPayload(), tournament.Version, context, true);
            stage = "read"; var current = store.Tournament(host, tournament.Id)!;
            stage = "set"; var updated = store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 8, SwissRounds: 3), current.Version, context, true);
            stage = "assert"; Assert.Equal(8, updated.MaxPlayers);
            Assert.Equal(3, updated.SwissRounds);
            Assert.True(updated.Version > current.Version);
            stage = "preview"; // 只是预览时不落盘，也不推进版本。
            var previewVersion = updated.Version;
            var preview = store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 12, SwissRounds: 5), previewVersion, context, false);
            Assert.Equal(12, preview.MaxPlayers);
            Assert.Equal(previewVersion, store.Tournament(host, tournament.Id)!.Version);
            }
            catch (Exception error) { Assert.Fail($"DIAG stage={stage} {error.GetType().Name}: {error.Message}"); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PlayerCapCannotDropBelowRegistrations()
    {
        var (store, host, context, root) = Create("swiss");
        try
        {
            var tournament = store.CreateTournament(host, Swiss(16, 4) with { OperationMode = "managed" }, context, true);
            store.RegisterTournament(host, tournament.Id, new L12TournamentRegistrationPayload(), tournament.Version, context, true);
            var current = store.Tournament(host, tournament.Id)!;
            var error = Assert.Throws<ArgumentException>(() => store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 1, SwissRounds: null), current.Version, context, true));
            Assert.Contains("已报名人数", error.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SingleEliminationRejectsRoundAdjustmentsAndClosedTournamentIsLocked()
    {
        var (store, host, context, root) = Create("single");
        try
        {
            var payload = Swiss(16, 4) with { Format = "single" };
            var tournament = store.CreateTournament(host, payload, context, true);
            var current = store.Tournament(host, tournament.Id)!;
            Assert.Throws<ArgumentException>(() => store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: null, SwissRounds: 3), current.Version, context, true));
            // 人数仍可调整（单败也要按实际人数收口）。
            var resized = store.SetTournamentSchedule(host, tournament.Id,
                new L12TournamentSchedulePayload(MaxPlayers: 8, SwissRounds: null), current.Version, context, true);
            Assert.Equal(8, resized.MaxPlayers);
            // 赛事一旦离开赛前阶段（这里用取消模拟），两项都不再可改。
            store.CancelTournament(host, tournament.Id, new L12TournamentCancelPayload("演练结束"),
                resized.Version, context, true);
            var closed = store.Tournament(host, tournament.Id)!;
            Assert.Throws<L12TournamentVersionConflictException>(() => store.SetTournamentSchedule(host,
                tournament.Id, new L12TournamentSchedulePayload(MaxPlayers: 12, SwissRounds: null),
                closed.Version, context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    [InlineData(64, 6)]
    [InlineData(1, 1)]
    public void SuggestedRoundsFollowCeilingLog2(int participants, int expected)
        => Assert.Equal(expected, L12PlatformStore.SuggestedSwissRounds(participants));
}