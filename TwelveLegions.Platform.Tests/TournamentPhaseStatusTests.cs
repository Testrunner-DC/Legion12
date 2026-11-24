using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

// B1/B2: a tournament that already ran must not be demoted to registration by a phase
// change, and a start must be refused once rounds exist even if the status bookkeeping
// says otherwise.
[Collection("Platform environment")]
public sealed class TournamentPhaseStatusTests
{
    [Fact]
    public void LeavingResultConfirmationKeepsTheEventRunning()
    {
        using var fixture = new Fixture();
        var tournament = fixture.CompletedTournament();
        tournament = fixture.Store.SetTournamentPhase(fixture.Organizer, tournament.Id,
            new L12TournamentPhasePayload("result-confirmation", "synthetic confirmation"),
            tournament.Version, Context("phase-result-confirmation"), true);
        Assert.Equal("running", tournament.Status);
        Assert.False(tournament.RegistrationOpen);

        tournament = fixture.Store.SetTournamentPhase(fixture.Organizer, tournament.Id,
            new L12TournamentPhasePayload("running", "synthetic rollback"), tournament.Version,
            Context("phase-back-to-running"), true);

        Assert.Equal("running", tournament.Phase);
        Assert.Equal("running", tournament.Status);
        Assert.False(tournament.RegistrationOpen);
    }

    [Fact]
    public void StartIsRefusedWhenRoundsExistEvenIfStatusSaysRegistration()
    {
        using var fixture = new Fixture();
        var tournament = fixture.StartedTournament();
        // Legacy or hand-edited state: the round exists but the status was written back.
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            TournamentRow(fixture.Store, tournament.Id).GetType().GetProperty("Status")!
                .SetValue(TournamentRow(fixture.Store, tournament.Id), "registration");
            global::TwelveLegions.Platform.Tests.RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var roundsBefore = fixture.Store.Tournament(fixture.Organizer, tournament.Id)!.Rounds.Count;

        var failure = Assert.Throws<ArgumentException>(() => fixture.Store.StartTournament(
            fixture.Organizer, tournament.Id,
            fixture.Store.Tournament(fixture.Organizer, tournament.Id)!.Version,
            Context("start-with-rounds"), true));

        Assert.Contains("赛事已有轮次", failure.Message, StringComparison.Ordinal);
        Assert.Equal(roundsBefore, fixture.Store.Tournament(fixture.Organizer, tournament.Id)!.Rounds.Count);
    }

    [Fact]
    public void AutomationCoreRefusesASecondStart()
    {
        using var fixture = new Fixture();
        var started = fixture.StartedTournament();
        var method = typeof(L12PlatformStore).GetMethod("StartTournamentCore",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var row = TournamentRow(fixture.Store, started.Id);
        var failure = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(fixture.Store, [row, DateTimeOffset.UtcNow, null, null]));
        var inner = Assert.IsType<L12TournamentVersionConflictException>(failure.InnerException);
        Assert.Contains("赛事已有轮次", inner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpPhaseRollbackThenStartIsRefusedAndRoundsAreUnchanged()
    {
        using var fixture = new Fixture();
        var completed = fixture.CompletedTournament();
        using var client = await fixture.StartServerAsync();
        var token = fixture.OrganizerToken;

        var confirmed = await SendAsync(client, HttpMethod.Post,
            $"/api/tournaments/{completed.Id}/phase", token,
            new { phase = "result-confirmation", reason = "synthetic confirmation", expectedVersion = completed.Version },
            "phase-confirm");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var afterConfirm = await ReadTournamentAsync(client, completed.Id, token);
        Assert.Equal("running", afterConfirm.GetProperty("status").GetString());

        var rolledBack = await SendAsync(client, HttpMethod.Post,
            $"/api/tournaments/{completed.Id}/phase", token,
            new
            {
                phase = "running", reason = "synthetic rollback",
                expectedVersion = afterConfirm.GetProperty("version").GetInt64(),
            }, "phase-rollback");
        Assert.Equal(HttpStatusCode.OK, rolledBack.StatusCode);
        var afterRollback = await ReadTournamentAsync(client, completed.Id, token);
        Assert.Equal("running", afterRollback.GetProperty("status").GetString());
        Assert.Equal(1, afterRollback.GetProperty("rounds").GetArrayLength());

        var restart = await SendAsync(client, HttpMethod.Post,
            $"/api/tournaments/{completed.Id}/start", token, new { },
            "restart", afterRollback.GetProperty("version").GetInt64());
        Assert.True((int)restart.StatusCode is >= 400 and < 500,
            "restart must be refused with a 4xx, got " + (int)restart.StatusCode);

        var final = await ReadTournamentAsync(client, completed.Id, token);
        Assert.Equal(1, final.GetProperty("rounds").GetArrayLength());
        Assert.Equal("running", final.GetProperty("status").GetString());
    }

    // B4: cancel and completion must move Status and Phase together; a reader that trusts
    // either one must never see two different lifecycles.
    [Fact]
    public void CancelAndCompletionKeepStatusAndPhaseInStep()
    {
        using (var fixture = new Fixture())
        {
            var completed = fixture.CompletedTournament();
            var finished = fixture.Store.CompleteTournament(fixture.Organizer, completed.Id,
                completed.Version, Context("complete"), true);
            Assert.Equal("completed", finished.Status);
            Assert.Equal("completed", finished.Phase);
            Assert.False(finished.RegistrationOpen);
        }

        using (var fixture = new Fixture())
        {
            var started = fixture.StartedTournament();
            var canceled = fixture.Store.CancelTournament(fixture.Organizer, started.Id,
                new L12TournamentCancelPayload("synthetic cancel"), started.Version,
                Context("cancel"), true);
            Assert.Equal("canceled", canceled.Status);
            Assert.Equal("canceled", canceled.Phase);
            Assert.False(canceled.RegistrationOpen);
        }
    }

    private static L12AdminAuditContext Context(string correlation)
        => new(correlation, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournament-phase");

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string path, string token, object body, string key, long? expectedVersion = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key + "-" + Guid.NewGuid().ToString("N"));
        if (expectedVersion is { } version) request.Headers.Add("If-Match", '"' + version.ToString() + '"');
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadTournamentAsync(HttpClient client, string id, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tournaments/" + id);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static object TournamentRow(L12PlatformStore store, string id)
    {
        // _data is a guarded property over the committed state, not a field.
        var member = (MemberInfo?)typeof(L12PlatformStore).GetProperty("_data",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_data property missing");
        var data = ((PropertyInfo)member).GetValue(store)
            ?? throw new InvalidOperationException("_data is null");
        var tournamentsProperty = data.GetType().GetProperty("Tournaments")
            ?? throw new InvalidOperationException("Tournaments property missing on " + data.GetType().FullName);
        var tournaments = (System.Collections.IEnumerable?)tournamentsProperty.GetValue(data)
            ?? throw new InvalidOperationException("Tournaments collection is null");
        foreach (var row in tournaments)
        {
            var idProperty = row!.GetType().GetProperty("Id")
                ?? throw new InvalidOperationException("Id property missing on " + row.GetType().FullName);
            if ((string?)idProperty.GetValue(row) == id) return row;
        }
        throw new InvalidOperationException("tournament row missing: " + id);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "l12-phase-status", Guid.NewGuid().ToString("N"));
        internal L12Catalog Catalog { get; }
        internal L12PlatformStore Store { get; }
        internal L12AccountView Organizer { get; }
        internal L12AccountView Referee { get; }
        internal string OrganizerToken { get; }
        private readonly string _playerUsername = "phplayer";

        internal Fixture()
        {
            Directory.CreateDirectory(_root);
            Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            Store = new L12PlatformStore(Path.Combine(_root, "platform.json"), Catalog.PresetDecks,
                officialCards: Catalog.Cards);
            var organizer = Store.Register("phhost", "password-123");
            Assert.True(organizer.Success, organizer.Message);
            Organizer = organizer.Account!;
            OrganizerToken = organizer.Token!;
            var player = Store.Register(_playerUsername, "password-123");
            Assert.True(player.Success, player.Message);
            var referee = Store.Register("phreferee", "password-123");
            Assert.True(referee.Success, referee.Message);
            Referee = referee.Account!;
            Assert.True(Store.SendFriendRequest(Organizer.Id, Referee.Id).Success);
            Assert.True(Store.ResolveFriendRequest(Referee.Id, Organizer.Id, true).Success);
        }

        internal L12TournamentView StartedTournament()
        {
            var player = Store.Authenticate("Bearer " + Store.Login(_playerUsername, "password-123").Token)!;
            var tournament = global::TwelveLegions.Platform.Tests.TournamentTestSetup.CreateWithRegisteredOrganizer(
                Store, Organizer,
                new L12TournamentCreatePayload("phase tournament " + Guid.NewGuid().ToString("N")[..6],
                    "single", "public", 8, DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "phase status",
                    "private", "season", string.Empty, 50, 5, [Referee.Id],
                    TimeControl: new L12RankedTimeControlConfig(900, 90, 120, 30, 45)),
                Context("create"), true);
            tournament = SelectDeck(Organizer, tournament, update: true);
            tournament = SelectDeck(player, tournament, update: false);
            tournament = Store.StartTournament(Organizer, tournament.Id, tournament.Version,
                Context("start"), true);
            tournament = Store.CheckInTournament(Organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(Organizer.Id, true), tournament.Version,
                Context("ready-a"), true);
            tournament = Store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("ready-b"), true);
            return Store.StartTournamentRound(Organizer, tournament.Id, 1, tournament.Version,
                Context("round"), true);
        }

        internal L12TournamentView CompletedTournament()
        {
            var started = StartedTournament();
            var match = Assert.Single(started.Rounds[0].Matches);
            return Store.ApplyTournamentRuling(Referee, started.Id, match.Id,
                new L12TournamentRulingPayload("result", null, "player-a", "synthetic result"),
                started.Version, Context("ruling"), true);
        }

        internal async Task<HttpClient> StartServerAsync()
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var recorder = new MatchRecorder(Path.Combine(_root, "matches.db"));
            await recorder.InitializeAsync();
            _recorder = recorder;
            _server = new L12WebSocketServer(new L12RoomManager(Catalog, recorder, Store), recorder,
                Store, Catalog);
            await _server.StartAsync(0);
            return new HttpClient { BaseAddress = new Uri(Assert.Single(_server.Addresses)) };
        }

        private MatchRecorder? _recorder;
        private L12WebSocketServer? _server;

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
            if (_server is not null)
            {
                _server.StopAsync().GetAwaiter().GetResult();
                _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            if (_recorder is not null) _recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
