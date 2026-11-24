using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

// The rehearsal gate may end a live tournament table from the server side. The property that
// matters is that it stays a request to the room, never a second writer of match.Result.
[Collection("Platform environment")]
public sealed class TournamentAuthorityConclusionTests
{
    [Fact]
    public void GateOffRefusesWithoutQueueingAnything()
    {
        using var fixture = new Fixture();
        var versionBefore = fixture.Store.Version;
        WithGate(false, () => Assert.Throws<L12TournamentScopeException>(() =>
            fixture.Store.ConcludeTournamentMatchByAuthority(fixture.Organizer, fixture.Tournament.Id,
                fixture.Match.Id, new L12TournamentAuthorityConclusionPayload("player-a", "synthetic authority"),
                fixture.Tournament.Version, Context("authority-off"), true)));
        Assert.Equal(versionBefore, fixture.Store.Version);
        Assert.Empty(fixture.Store.PendingTournamentRoomCommands(fixture.Tournament.Id));
    }

    [Fact]
    public void GateOnQueuesRoomConclusionAndLeavesTheResultToTheRoom()
    {
        using var fixture = new Fixture();
        WithGate(true, () =>
        {
            var tournament = fixture.Store.ConcludeTournamentMatchByAuthority(fixture.Referee,
                fixture.Tournament.Id, fixture.Match.Id,
                new L12TournamentAuthorityConclusionPayload(" Player-B ", "synthetic authority"),
                fixture.Tournament.Version, Context("authority"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            Assert.Equal("running", match.Status);
            Assert.Null(match.Result);
            Assert.Equal(fixture.Tournament.Version + 1, tournament.Version);
            var command = Assert.Single(
                fixture.Store.PendingTournamentRoomCommands(fixture.Tournament.Id),
                item => item.Kind == "conclude");
            Assert.Equal(fixture.Match.Id, command.MatchId);
            Assert.Equal(fixture.Match.RoomCode, command.RoomCode);
            Assert.Equal("player-b", command.Decision);
            Assert.Equal("synthetic authority", command.Reason);
        });
    }

    [Fact]
    public void DecisionMustNameATableSide()
    {
        using var fixture = new Fixture();
        WithGate(true, () =>
        {
            Assert.Throws<ArgumentException>(() => fixture.Store.ConcludeTournamentMatchByAuthority(
                fixture.Referee, fixture.Tournament.Id, fixture.Match.Id,
                new L12TournamentAuthorityConclusionPayload("draw", "synthetic authority"),
                fixture.Tournament.Version, Context("authority-draw"), true));
            Assert.Empty(fixture.Store.PendingTournamentRoomCommands(fixture.Tournament.Id));
        });
    }

    [Fact]
    public void CompletedTableCannotBeConcludedAgain()
    {
        using var fixture = new Fixture();
        WithGate(true, () =>
        {
            var ruled = fixture.Store.ApplyTournamentRuling(fixture.Referee, fixture.Tournament.Id,
                fixture.Match.Id, new L12TournamentRulingPayload("result", null, "player-a", "synthetic ruling"),
                fixture.Tournament.Version, Context("ruling"), true);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                fixture.Store.ConcludeTournamentMatchByAuthority(fixture.Referee, fixture.Tournament.Id,
                    fixture.Match.Id, new L12TournamentAuthorityConclusionPayload("player-b", "synthetic authority"),
                    ruled.Version, Context("authority-after-ruling"), true));
        });
    }

    // The drainer is where a queued conclusion becomes an engine call. With no live room the
    // table owns no result, so the only correct outcome is "command completed, nothing written".
    [Fact]
    public async Task DrainerCompletesAConclusionForATableWithoutALiveRoom()
    {
        using var fixture = new Fixture();
        await using var recorder = new MatchRecorder(Path.Combine(fixture.Root, "authority-games.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        WithGate(true, () => fixture.Store.ConcludeTournamentMatchByAuthority(fixture.Referee,
            fixture.Tournament.Id, fixture.Match.Id,
            new L12TournamentAuthorityConclusionPayload("player-a", "synthetic authority"),
            fixture.Tournament.Version, Context("authority-drain"), true));

        var drained = await manager.DrainTournamentRoomCommandsAsync(fixture.Tournament.Id);

        Assert.Equal(1, drained.Applied);
        Assert.Equal(0, drained.Failed);
        Assert.Empty(fixture.Store.PendingTournamentRoomCommands(fixture.Tournament.Id));
        var match = Assert.Single(fixture.Store.Tournament(fixture.Referee, fixture.Tournament.Id)!.Rounds[0].Matches);
        Assert.Null(match.Result);
        Assert.Equal("running", match.Status);
    }

    private static void WithGate(bool enabled, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(
            L12PlatformStore.RehearsalAccountsEnvironmentKey);
        Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalAccountsEnvironmentKey,
            enabled ? "1" : null);
        try { body(); }
        finally
        {
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalAccountsEnvironmentKey, previous);
        }
    }

    private static L12AdminAuditContext Context(string correlation)
        => new(correlation, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/authority-conclusion");

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "l12-authority-conclusion", Guid.NewGuid().ToString("N"));
        internal L12PlatformStore Store { get; }
        internal L12Catalog Catalog { get; }
        internal string Root => _root;
        internal L12AccountView Organizer { get; }
        internal L12AccountView Referee { get; }
        internal L12TournamentView Tournament { get; set; }
        internal L12TournamentMatchView Match { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(_root);
            Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            Store = new L12PlatformStore(Path.Combine(_root, "platform.json"), Catalog.PresetDecks,
                officialCards: Catalog.Cards);
            Organizer = Register("authhost");
            Referee = Register("authreferee");
            var player = Register("authplayer");
            Assert.True(Store.SendFriendRequest(Organizer.Id, Referee.Id).Success);
            Assert.True(Store.ResolveFriendRequest(Referee.Id, Organizer.Id, true).Success);
            Tournament = CreateRunningTournament(Organizer, player, "authority tournament");
            Match = Assert.Single(Tournament.Rounds[0].Matches);
        }

        internal L12AccountView Register(string username)
        {
            var result = Store.Register(username, "password-123");
            Assert.True(result.Success, result.Message);
            return Assert.IsType<L12AccountView>(result.Account);
        }

        private L12TournamentView CreateRunningTournament(L12AccountView organizer,
            L12AccountView player, string name)
        {
            var tournament = global::TwelveLegions.Platform.Tests.TournamentTestSetup.CreateWithRegisteredOrganizer(
                Store, organizer,
                new L12TournamentCreatePayload(name, "single", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "authority conclusion",
                    "private", "season", string.Empty, 50, 5, [Referee.Id],
                    TimeControl: new L12RankedTimeControlConfig(900, 90, 120, 30, 45)),
                Context($"create-{name}"), true);
            tournament = SelectDeck(organizer, tournament, update: true);
            tournament = SelectDeck(player, tournament, update: false);
            tournament = Store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context($"start-{name}"), true);
            tournament = Store.CheckInTournament(organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(organizer.Id, true), tournament.Version,
                Context($"ready-a-{name}"), true);
            tournament = Store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context($"ready-b-{name}"), true);
            return Store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context($"round-{name}"), true);
        }

        private L12TournamentView SelectDeck(L12AccountView account, L12TournamentView tournament,
            bool update)
        {
            var deck = Store.Decks(account.Id)[0];
            tournament = update
                ? Store.UpdateTournamentRegistration(account, tournament.Id,
                    new L12TournamentRegistrationPayload(), tournament.Version,
                    Context($"registration-{account.Id}"), true)
                : Store.RegisterTournament(account, tournament.Id,
                    new L12TournamentRegistrationPayload(), tournament.Version,
                    Context($"registration-{account.Id}"), true);
            return Store.PreCheckInTournament(account, tournament.Id,
                new L12TournamentPreCheckInPayload(deck.Name, string.Empty), tournament.Version,
                Context($"precheck-{account.Id}"), true);
        }

        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            Store.DeckPayloadExpansionObserver = null;
            Store.TournamentDeckPayloadReadObserver = null;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
