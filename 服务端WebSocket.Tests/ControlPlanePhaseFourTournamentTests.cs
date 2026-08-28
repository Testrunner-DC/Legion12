using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlanePhaseFourTournamentTests
{
    [Fact]
    public void TournamentPermissionsAndAccountScopeAreBothRequired()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var organizer = Promote(store, admin, "ScopeOrganizer", "organizer");
            var referee = Promote(store, admin, "ScopeReferee", "referee");
            var outsider = Promote(store, admin, "ScopeOutsider", "referee");
            var player = store.Register("ScopePlayer", "password-123").Account!;

            Assert.True(L12Authorization.HasPermission(player, L12Permission.TournamentsRegister));
            Assert.False(L12Authorization.HasPermission(player, L12Permission.TournamentsCreate));
            Assert.True(L12Authorization.HasPermission(organizer, L12Permission.TournamentsCreate));
            Assert.True(L12Authorization.HasPermission(referee, L12Permission.TournamentRulingsWrite));
            Assert.Throws<L12TournamentScopeException>(() => store.CreateTournament(player,
                CreatePayload(), Context("player-create"), true));

            var tournament = store.CreateTournament(organizer, CreatePayload([referee.Id]),
                Context("scope-create"), true);
            Assert.Throws<L12TournamentScopeException>(() => store.SetTournamentStaff(outsider, tournament.Id,
                new L12TournamentStaffPayload([outsider.Id]), tournament.Version, Context("scope-denied"), true));
            Assert.Throws<L12TournamentScopeException>(() => store.SetTournamentStaff(admin, tournament.Id,
                new L12TournamentStaffPayload([referee.Id]), tournament.Version, Context("admin-scope-denied"), true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TournamentVersionIgnoresUnrelatedPlatformWritesAndDryRunDoesNotWrite()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var organizer = Promote(store, admin, "VersionOrganizer", "organizer");
            var tournament = store.CreateTournament(organizer, CreatePayload(), Context("version-create"), true);
            var tournamentVersion = tournament.Version;
            store.Register("UnrelatedAccount", "password-123");

            var payload = new L12TournamentRegistrationPayload("版本牌库", "DECK-V1");
            var command = Envelope("tournament.registration.update", organizer,
                $"tournament:{tournament.Id}/registration:{organizer.Id}", tournamentVersion, payload,
                "tournament-version-1", false);
            var result = new L12AdminCommandBus(store).Execute(command, L12Permission.TournamentsRegister,
                current => L12AdminCommandResult<L12TournamentView>.Ok(store.UpdateTournamentRegistration(
                    current.Actor, tournament.Id, current.Payload, tournamentVersion, current.AuditContext, true)),
                current => L12AdminCommandResult<L12TournamentView>.Ok(store.UpdateTournamentRegistration(
                    current.Actor, tournament.Id, current.Payload, tournamentVersion, current.AuditContext, false)));

            Assert.True(result.Success);
            Assert.Equal(tournamentVersion + 1, result.Value!.Version);

            var beforeDryRun = result.Value;
            var dry = Envelope("tournament.registration.update", organizer,
                $"tournament:{tournament.Id}/registration:{organizer.Id}", beforeDryRun.Version,
                new L12TournamentRegistrationPayload("只预览", "DRY-RUN"), "tournament-version-dry", true);
            var dryResult = new L12AdminCommandBus(store).Execute(dry, L12Permission.TournamentsRegister,
                current => L12AdminCommandResult<L12TournamentView>.Ok(store.UpdateTournamentRegistration(
                    current.Actor, tournament.Id, current.Payload, beforeDryRun.Version,
                    current.AuditContext, true)),
                current => L12AdminCommandResult<L12TournamentView>.Ok(store.UpdateTournamentRegistration(
                    current.Actor, tournament.Id, current.Payload, beforeDryRun.Version,
                    current.AuditContext, false)));

            Assert.True(dryResult.Success);
            Assert.Equal("只预览", dryResult.Value!.Participants.Single().Deck!.Name);
            Assert.Equal("版本牌库", store.Tournament(organizer, tournament.Id)!.Participants.Single().Deck!.Name);
            Assert.Equal(beforeDryRun.Version, store.Tournament(organizer, tournament.Id)!.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void HighRiskTournamentCommandCannotSelfReviewOrEscapeTournamentScope()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var organizer = Promote(store, admin, "ApprovalOrganizer", "organizer");
            var reviewer = Promote(store, admin, "ApprovalReferee", "referee");
            var outsider = Promote(store, admin, "ApprovalOutsider", "referee");
            var player = store.Register("ApprovalPlayer", "password-123").Account!;
            var tournament = store.CreateTournament(organizer, CreatePayload([reviewer.Id]),
                Context("approval-create"), true);
            tournament = store.UpdateTournamentRegistration(organizer, tournament.Id,
                new L12TournamentRegistrationPayload("Organizer Deck", "ORG"), tournament.Version,
                Context("approval-deck-owner"), true);
            tournament = store.RegisterTournament(player, tournament.Id,
                new L12TournamentRegistrationPayload("Player Deck", "PLAYER"), tournament.Version,
                Context("approval-register"), true);

            var payload = new TournamentTargetCommandPayload(tournament.Id);
            var command = Envelope("tournament.start", organizer, $"tournament:{tournament.Id}",
                tournament.Version, payload, "tournament-start-approval", false);
            var bus = new L12AdminCommandBus(store);
            var requested = bus.Execute(command, L12Permission.TournamentsManage,
                current => L12AdminCommandResult<L12TournamentView>.Ok(store.StartTournament(current.Actor,
                    current.Payload.TournamentId, tournament.Version, current.AuditContext, true)),
                current => L12AdminCommandResult<L12TournamentView>.Ok(store.StartTournament(current.Actor,
                    current.Payload.TournamentId, tournament.Version, current.AuditContext, false)),
                L12AdminCommandRisk.High);

            Assert.True(requested.Pending);
            Assert.Equal("registration", store.Tournament(organizer, tournament.Id)!.Status);

            var outsiderReview = bus.Review(command.CommandId, outsider, new("approve", "not scoped"),
                Context("outsider-review") with { CommandId = command.CommandId }, ApprovedStart,
                L12Permission.TournamentApprovalsReview, store.CanReviewTournamentCommand);
            Assert.Equal("scope_denied", outsiderReview.Code);

            var selfReview = bus.Review(command.CommandId, organizer, new("approve", "self"),
                Context("self-review") with { CommandId = command.CommandId }, ApprovedStart,
                L12Permission.TournamentApprovalsReview, store.CanReviewTournamentCommand);
            Assert.Equal("self_review_forbidden", selfReview.Code);

            var approved = bus.Review(command.CommandId, reviewer, new("approve", "pairing verified"),
                Context("scoped-review") with { CommandId = command.CommandId }, ApprovedStart,
                L12Permission.TournamentApprovalsReview, store.CanReviewTournamentCommand);

            Assert.True(approved.Success, $"{approved.Code}: {approved.Message}; {approved.Command?.FailureReason}");
            var started = store.Tournament(organizer, tournament.Id)!;
            Assert.Equal("running", started.Status);
            Assert.Single(started.Rounds);
            Assert.Equal(2, started.Rounds[0].Matches.SelectMany(match =>
                new[] { match.PlayerAAccountId, match.PlayerBAccountId }.Where(id => id is not null)).Distinct().Count());
            Assert.Contains(store.AdminAudit("security"), item => item.Reason == "scope-denied");
            Assert.Contains(store.AdminAudit("security"), item => item.Reason == "self-review-forbidden");

            L12AdminCommandResult<JsonElement> ApprovedStart(L12AdminCommandView stored,
                L12AccountView requester, L12AdminAuditContext audit)
            {
                var original = stored.Payload.Deserialize<TournamentTargetCommandPayload>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                var value = store.StartTournament(requester, original.TournamentId, stored.ExpectedVersion!.Value,
                    audit, true);
                return L12AdminCommandResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(value));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PairingPauseExtensionRulingAndDeckVisibilityUseServerSnapshots()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var organizer = Promote(store, admin, "FlowOrganizer", "organizer");
            var player = store.Register("FlowPlayer", "password-123").Account!;
            var tournament = store.CreateTournament(organizer, CreatePayload(deckVisibility: "after"),
                Context("flow-create"), true);
            tournament = store.UpdateTournamentRegistration(organizer, tournament.Id,
                new L12TournamentRegistrationPayload("Secret A", "CODE-A"), tournament.Version,
                Context("flow-deck-a"), true);
            tournament = store.RegisterTournament(player, tournament.Id,
                new L12TournamentRegistrationPayload("Secret B", "CODE-B"), tournament.Version,
                Context("flow-deck-b"), true);

            var playerBefore = store.Tournament(player, tournament.Id)!;
            Assert.Null(playerBefore.Participants.Single(item => item.AccountId == organizer.Id).Deck);
            Assert.Equal("CODE-B", playerBefore.Participants.Single(item => item.AccountId == player.Id).Deck!.Code);

            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("flow-start"), true);
            tournament = store.CheckInTournament(organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(organizer.Id, true), tournament.Version,
                Context("flow-check-a"), true);
            tournament = store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("flow-check-b"), true);
            tournament = store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context("flow-round-start"), true);
            tournament = store.PauseTournamentRound(organizer, tournament.Id, 1,
                new L12TournamentPausePayload(true, "technical pause"), tournament.Version,
                Context("flow-pause"), true);
            tournament = store.PauseTournamentRound(organizer, tournament.Id, 1,
                new L12TournamentPausePayload(false, "issue cleared"), tournament.Version,
                Context("flow-resume"), true);
            var match = tournament.Rounds[0].Matches.Single();
            tournament = store.ExtendTournamentMatch(organizer, tournament.Id, match.Id,
                new L12TournamentTimeExtensionPayload(5, "network delay"), tournament.Version,
                Context("flow-extend"), true);
            tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                new L12TournamentRulingPayload("penalty", player.Id, "warning", "slow play"),
                tournament.Version, Context("flow-penalty"), true);
            tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                new L12TournamentRulingPayload("result", null, "player-a", "signed result"),
                tournament.Version, Context("flow-result"), true);
            tournament = store.CompleteTournament(organizer, tournament.Id, tournament.Version,
                Context("flow-complete"), true);

            var finalMatch = tournament.Rounds[0].Matches.Single();
            Assert.Equal(5, finalMatch.TimeExtensionMinutes);
            Assert.Contains(finalMatch.Rulings, item => item.Kind == "penalty" && item.Reason == "slow play");
            Assert.Equal("completed", tournament.Status);
            var playerAfter = store.Tournament(player, tournament.Id)!;
            Assert.Equal("CODE-A", playerAfter.Participants.Single(item => item.AccountId == organizer.Id).Deck!.Code);
            Assert.All(playerAfter.Participants, item => Assert.NotNull(item.Deck!.LockedAt));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyImportRequiresPreviewHashAndNeverTrustsNicknameStaffAuthority()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var organizer = Promote(store, admin, "ImportOrganizer", "organizer");
            var known = store.Register("KnownLegacyPlayer", "password-123").Account!;
            var input = new L12LegacyTournamentInput(
                Id: "legacy-local-1", Code: "OLD123", Name: "旧本地赛事", Organizer: "FakeOrganizer",
                Referees: ["UnknownReferee"], Status: "registration", Format: "swiss", Visibility: "public",
                MaxPlayers: 16, StartAt: null, Ruleset: "旧规则快照", Description: "preview first",
                DeckVisibility: "private", DisasterMode: "season", BanList: "legacy-ban", RoundMinutes: 50,
                CheckInMinutes: 5,
                Participants:
                [
                    new("KnownLegacyPlayer", "Known Deck", "KNOWN"),
                    new("UnknownLegacyPlayer", "Unknown Deck", "UNKNOWN"),
                ],
                Rounds: [], CreatedAt: null, UpdatedAt: null, CompletedAt: null);
            var payload = new L12TournamentLegacyImportPayload([input]);

            var preview = store.ImportLegacyTournaments(organizer, payload, Context("import-preview"), false);

            Assert.False(preview.Applied);
            Assert.Empty(store.Tournaments(organizer).Items);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.ImportLegacyTournaments(organizer, payload, Context("import-without-confirmation"), true));

            var confirmed = store.ImportLegacyTournaments(organizer, payload with { PreviewHash = preview.PreviewHash },
                Context("import-confirm"), true);

            var imported = Assert.Single(confirmed.Tournaments);
            Assert.True(confirmed.Applied);
            Assert.Equal(organizer.Id, imported.OrganizerAccountId);
            Assert.Empty(imported.Referees);
            Assert.Contains(imported.Participants, item => item.AccountId == known.Id);
            Assert.DoesNotContain(imported.Participants, item => item.Username == "UnknownLegacyPlayer");
            Assert.True(imported.LegacyImported);

            var reloaded = new L12PlatformStore(path);
            var reloadedOrganizer = reloaded.Account(organizer.Id)!;
            Assert.Single(reloaded.Tournaments(reloadedOrganizer).Items);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpContractsRequirePreconditionsReplayAndAuditScopeDenials()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var adminLogin = store.Login("Admin", "L12master");
            var organizerRegistration = store.Register("HttpTourOrganizer", "password-123");
            Assert.True(store.SetRole(adminLogin.Account!, organizerRegistration.Account!.Id, "organizer"));
            var organizerLogin = store.Login("HttpTourOrganizer", "password-123");
            var player = store.Register("HttpTourPlayer", "password-123");
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            long platformVersion;
            using (var list = Authorized(HttpMethod.Get, "/api/tournaments", organizerLogin.Token!, "tour-list"))
            using (var response = await client.SendAsync(list))
            {
                var responseText = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {responseText}");
                platformVersion = JsonSerializer.Deserialize<L12TournamentListView>(responseText,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!.PlatformVersion;
            }

            var createBody = new TournamentCreateRequest(CreatePayload(), "http-tournament-create",
                platformVersion, false, "contract test");
            using (var denied = Authorized(HttpMethod.Post, "/api/tournaments", player.Token!, "tour-denied",
                       createBody))
            using (var response = await client.SendAsync(denied))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            L12TournamentView created;
            using (var create = Authorized(HttpMethod.Post, "/api/tournaments", organizerLogin.Token!,
                       "tour-create", createBody))
            using (var response = await client.SendAsync(create))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                created = (await response.Content.ReadFromJsonAsync<L12TournamentView>())!;
            }
            using (var replay = Authorized(HttpMethod.Post, "/api/tournaments", organizerLogin.Token!,
                       "tour-create-replay", createBody))
            using (var response = await client.SendAsync(replay))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-Idempotent-Replay")));
            }

            using (var missingVersion = Authorized(HttpMethod.Post, $"/api/tournaments/{created.Id}/start",
                       organizerLogin.Token!, "tour-missing-version", new TournamentActionRequest("missing-version")))
            using (var response = await client.SendAsync(missingVersion))
                Assert.Equal((HttpStatusCode)428, response.StatusCode);

            using (var outOfScope = Authorized(HttpMethod.Post, $"/api/tournaments/{created.Id}/start",
                       adminLogin.Token!, "tour-scope-denied",
                       new TournamentActionRequest("scope-denied", created.Version, false, "not staff")))
            using (var response = await client.SendAsync(outOfScope))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            Assert.Single(store.Tournaments(organizerLogin.Account!).Items);
            Assert.Contains(store.AdminAudit("security"), item => item.CorrelationId == "tour-scope-denied"
                && item.Reason == "scope-denied");
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static L12TournamentCreatePayload CreatePayload(IReadOnlyList<string>? referees = null,
        string deckVisibility = "after")
        => new("测试赛事", "swiss", "public", 16, null, "现行规则", "phase four",
            deckVisibility, "season", string.Empty, 50, 5, referees);

    private static L12AccountView Promote(L12PlatformStore store, L12AccountView admin, string username, string role)
    {
        var account = store.Register(username, "password-123").Account!;
        Assert.True(store.SetRole(admin, account.Id, role));
        return store.Account(account.Id)!;
    }

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournaments");

    private static L12AdminCommandEnvelope<T> Envelope<T>(string type, L12AccountView actor, string scope,
        long expectedVersion, T payload, string idempotencyKey, bool dryRun)
    {
        var commandId = Guid.NewGuid().ToString("N");
        return new L12AdminCommandEnvelope<T>(commandId, idempotencyKey, type, actor, DateTimeOffset.UtcNow,
            scope, "test", dryRun, expectedVersion, payload,
            Context(idempotencyKey) with { CommandId = commandId, IdempotencyKey = idempotencyKey,
                ExpectedVersion = expectedVersion, DryRun = dryRun });
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token,
        string correlationId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, correlationId);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-control-plane-phase-four-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
