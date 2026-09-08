using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TournamentGlobalAdministrationTests
{
    [Fact]
    public void GlobalAdministratorCanManagePrivatePlayerTournamentWithoutBecomingEventStaff()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-global-tournament-admin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var organizer = store.Register("tgloba0eac8", "password-123").Account!;
            var player = store.Register("tgloba09863", "password-123").Account!;
            var outsider = store.Register("tgloba8c792", "password-123").Account!;
            var tournament = store.CreateTournament(organizer, Payload(), Context("create"), true);

            tournament = store.UpdateTournamentRegistration(organizer, tournament.Id,
                new L12TournamentRegistrationPayload("Organizer Deck", "ORGANIZER"), tournament.Version,
                Context("organizer-deck"), true);
            tournament = store.RegisterTournament(player, tournament.Id,
                new L12TournamentRegistrationPayload("Player Deck", "PLAYER"), tournament.Version,
                Context("player-deck"), true);

            Assert.DoesNotContain(store.Tournaments(outsider).Items, item => item.Id == tournament.Id);
            Assert.Null(store.Tournament(outsider, tournament.Id));
            Assert.Contains(store.Tournaments(admin).Items, item => item.Id == tournament.Id);
            var adminDetail = store.Tournament(admin, tournament.Id)!;
            Assert.Equal(2, adminDetail.Participants.Count);
            Assert.All(adminDetail.Participants, item => Assert.NotNull(item.Deck));
            Assert.Throws<L12TournamentScopeException>(() => store.SetTournamentStaff(admin, tournament.Id,
                new L12TournamentStaffPayload([]), tournament.Version, Context("staff-denied"), true));

            tournament = store.StartTournament(admin, tournament.Id, tournament.Version,
                Context("start"), true);
            foreach (var participant in tournament.Participants)
                tournament = store.CheckInTournament(admin, tournament.Id, 1,
                    new L12TournamentCheckInPayload(participant.AccountId, true), tournament.Version,
                    Context($"check-in-{participant.AccountId}"), true);
            tournament = store.StartTournamentRound(admin, tournament.Id, 1, tournament.Version,
                Context("round-start"), true);
            tournament = store.PauseTournamentRound(admin, tournament.Id, 1,
                new L12TournamentPausePayload(true, "全局管理员暂停"), tournament.Version,
                Context("pause"), true);
            tournament = store.PauseTournamentRound(admin, tournament.Id, 1,
                new L12TournamentPausePayload(false, "全局管理员恢复"), tournament.Version,
                Context("resume"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            tournament = store.ExtendTournamentMatch(admin, tournament.Id, match.Id,
                new L12TournamentTimeExtensionPayload(5, "全局管理员补时"), tournament.Version,
                Context("extend"), true);
            tournament = store.ApplyTournamentRuling(admin, tournament.Id, match.Id,
                new L12TournamentRulingPayload("penalty", player.Id, "warning", "全局管理员判罚"),
                tournament.Version, Context("penalty"), true);
            tournament = store.ApplyTournamentRuling(admin, tournament.Id, match.Id,
                new L12TournamentRulingPayload("result", null, "player-a", "全局管理员赛果"),
                tournament.Version, Context("result"), true);
            tournament = store.CompleteTournament(admin, tournament.Id, tournament.Version,
                Context("archive"), true);

            Assert.Equal("completed", tournament.Status);
            Assert.Equal(5, tournament.Rounds[0].Matches[0].TimeExtensionMinutes);
            Assert.Contains(tournament.Rounds[0].Matches[0].Rulings,
                item => item.ActorName == "Admin" && item.Reason == "全局管理员判罚");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static L12TournamentCreatePayload Payload()
        => new("玩家私密赛事", "single", "code", 16, null, "现行规则", "仅分享链接赛事",
            "private", "season", string.Empty, 50, 5, RegistrationVisibility: "staff");

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST",
            RequestPath: "/test/tournaments/global-admin");
}
