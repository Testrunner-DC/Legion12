using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

// 赛事房间结束后，该账号重连必须落回大厅，而不是让整个 hello 失败。
// 线上表现：L12TournamentVersionConflictException「本桌赛事房间尚未启动或已结束」→ commandFailed。
[Collection("Platform environment")]
public sealed class TournamentRoomReconnectTests
{
    [Fact]
    public async Task ReconnectAfterTheTableFinishedRecoversIntoTheLobby()
    {
        using var fixture = new Fixture();
        await using var recorder = new MatchRecorder(Path.Combine(fixture.Root, "games.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);

        var firstSession = Guid.NewGuid();
        var claim = await manager.ConnectAsync(firstSession, fixture.Player.Id, "pty");
        Assert.NotNull(claim);
        await manager.EnterTournamentMatchAsync(firstSession, fixture.Tournament.Id, fixture.Match.Id);

        // 用独立裁判裁掉这一桌：赛事桌次因此不再是 running。
        fixture.Store.ApplyTournamentRuling(fixture.Referee, fixture.Tournament.Id, fixture.Match.Id,
            new L12TournamentRulingPayload("result", null, "player-a", "synthetic result"),
            fixture.Tournament.Version, Context("ruling"), true);

        // 同一账号换一个新会话重连：必须成功，并且落在大厅。
        var reconnect = await manager.ConnectAsync(Guid.NewGuid(), fixture.Player.Id, "pty");

        Assert.Null(reconnect.RoomCode);
        Assert.False(reconnect.Recovered);
        Assert.Equal("fenced-active-session", reconnect.ClaimDecision);
        Assert.Null(reconnect.RejectionReason);
    }

    private static L12AdminAuditContext Context(string correlation)
        => new(correlation, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournament-reconnect");

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "l12-tournament-reconnect", Guid.NewGuid().ToString("N"));
        internal string Root => _root;
        internal L12Catalog Catalog { get; }
        internal L12PlatformStore Store { get; }
        internal L12AccountView Organizer { get; }
        internal L12AccountView Referee { get; }
        internal L12AccountView Player { get; }
        internal L12TournamentView Tournament { get; }
        internal L12TournamentMatchView Match { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(_root);
            Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            Store = new L12PlatformStore(Path.Combine(_root, "platform.json"), Catalog.PresetDecks,
                officialCards: Catalog.Cards);
            Organizer = Register("trhost");
            Referee = Register("trreferee");
            Player = Register("trplayer");
            Assert.True(Store.SendFriendRequest(Organizer.Id, Referee.Id).Success);
            Assert.True(Store.ResolveFriendRequest(Referee.Id, Organizer.Id, true).Success);
            Tournament = CreateRunningTournament();
            Match = Assert.Single(Tournament.Rounds[0].Matches);
        }

        internal L12AccountView Register(string username)
        {
            var result = Store.Register(username, "password-123");
            Assert.True(result.Success, result.Message);
            return Assert.IsType<L12AccountView>(result.Account);
        }

        private L12TournamentView CreateRunningTournament()
        {
            var tournament = global::TwelveLegions.Platform.Tests.TournamentTestSetup.CreateWithRegisteredOrganizer(
                Store, Organizer,
                new L12TournamentCreatePayload("reconnect tournament", "single", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "reconnect",
                    "private", "season", string.Empty, 50, 5, [Referee.Id],
                    TimeControl: new L12RankedTimeControlConfig(900, 90, 120, 30, 45)),
                Context("create"), true);
            tournament = SelectDeck(Organizer, tournament, update: true);
            tournament = SelectDeck(Player, tournament, update: false);
            tournament = Store.StartTournament(Organizer, tournament.Id, tournament.Version,
                Context("start"), true);
            tournament = Store.CheckInTournament(Organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(Organizer.Id, true), tournament.Version,
                Context("ready-a"), true);
            tournament = Store.CheckInTournament(Player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("ready-b"), true);
            return Store.StartTournamentRound(Organizer, tournament.Id, 1, tournament.Version,
                Context("round"), true);
        }

        private L12TournamentView SelectDeck(L12AccountView account, L12TournamentView tournament,
            bool update)
        {
            var deck = Store.Decks(account.Id)[0];
            tournament = update
                ? Store.UpdateTournamentRegistration(account, tournament.Id,
                    new L12TournamentRegistrationPayload(), tournament.Version,
                    Context("registration-" + account.Id), true)
                : Store.RegisterTournament(account, tournament.Id,
                    new L12TournamentRegistrationPayload(), tournament.Version,
                    Context("registration-" + account.Id), true);
            return Store.PreCheckInTournament(account, tournament.Id,
                new L12TournamentPreCheckInPayload(deck.Name, string.Empty), tournament.Version,
                Context("precheck-" + account.Id), true);
        }

        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            Store.DeckPayloadExpansionObserver = null;
            Store.TournamentDeckPayloadReadObserver = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
