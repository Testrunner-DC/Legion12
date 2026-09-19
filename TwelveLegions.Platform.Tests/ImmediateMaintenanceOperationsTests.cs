using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlaneImmediateMaintenanceOperationsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ImmediateMaintenanceIsIndependentVersionedPersistentAndBackwardCompatible()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.OperationsConfig(admin);
            var now = DateTimeOffset.UtcNow;
            var scheduled = store.ApplyOperationsConfig(admin, initial.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "预约维护",
                    now.AddHours(2), now.AddHours(4), 2, 3),
            }, initial.Version, "configure scheduled maintenance", Context("schedule")).Current;

            var player = store.Register("MaintStoreP", "password-123").Account!;
            Assert.Equal("permission_denied", Assert.Throws<L12OperationsConfigException>(() =>
                store.BeginImmediateMaintenance(player, 6, scheduled.Version,
                    "player cannot start maintenance", Context("permission-denied"))).Code);
            Assert.Equal("operations_version_conflict", Assert.Throws<L12OperationsConfigException>(() =>
                store.BeginImmediateMaintenance(admin, 6, scheduled.Version - 1,
                    "stale version", Context("stale-start"))).Code);

            var started = store.BeginImmediateMaintenance(admin, 6, scheduled.Version,
                "start immediate maintenance", Context("immediate-start"));

            Assert.True(started.Applied);
            Assert.False(started.AlreadyApplied);
            Assert.Equal(scheduled.Config.Maintenance, started.Current.Config.Maintenance);
            Assert.True(started.Current.ImmediateMaintenance!.Enabled);
            Assert.Equal(6, started.Current.ImmediateMaintenance.ExpectedDurationHours);
            Assert.NotNull(started.Current.ImmediateMaintenance.StartedAt);
            var effective = store.EffectiveOperationsPolicy(now);
            Assert.True(effective.Maintenance.Enabled);
            Assert.True(effective.Maintenance.Active);
            Assert.True(effective.Maintenance.EntryBlocked);
            Assert.True(effective.Maintenance.ImmediateActive);
            Assert.Equal("当前服务器维护中，预计维护时间为6小时。", effective.Maintenance.BroadcastMessage);
            var farBeyondEstimate = store.EffectiveOperationsPolicy(
                started.Current.ImmediateMaintenance.StartedAt!.Value.AddHours(200));
            Assert.True(farBeyondEstimate.Maintenance.Active);
            Assert.True(farBeyondEstimate.Maintenance.EntryBlocked);

            var duplicateWithStaleVersion = store.BeginImmediateMaintenance(admin, 7, scheduled.Version,
                "retry after timeout", Context("immediate-retry"));
            Assert.False(duplicateWithStaleVersion.Applied);
            Assert.True(duplicateWithStaleVersion.AlreadyApplied);
            Assert.Equal(started.Current.Version, duplicateWithStaleVersion.Current.Version);
            Assert.Equal(6, duplicateWithStaleVersion.Current.ImmediateMaintenance!.ExpectedDurationHours);

            var legacyStart = store.StartServer(admin, started.Current.Version,
                "legacy scheduled start must preserve immediate", Context("legacy-start"));
            Assert.True(legacyStart.Applied);
            Assert.False(legacyStart.Current.Config.Maintenance.Enabled);
            Assert.True(legacyStart.Current.ImmediateMaintenance!.Enabled);
            Assert.True(store.EffectiveOperationsPolicy(now).Maintenance.Active);

            var genericApply = store.ApplyOperationsConfig(admin, legacyStart.Current.Config with
            {
                Season = legacyStart.Current.Config.Season with { Name = "即时维护期间配置变更" },
            }, legacyStart.Current.Version, "generic apply preserves immediate state", Context("generic-apply"));
            Assert.True(genericApply.Current.ImmediateMaintenance!.Enabled);
            Assert.Equal(6, genericApply.Current.ImmediateMaintenance.ExpectedDurationHours);

            var rollback = store.RollbackOperationsConfig(admin, initial.VersionId, genericApply.Current.Version,
                "rollback preserves immediate state", Context("rollback"));
            Assert.True(rollback.Current.ImmediateMaintenance!.Enabled);
            Assert.Equal(6, rollback.Current.ImmediateMaintenance.ExpectedDurationHours);
            Assert.False(rollback.Current.Config.Maintenance.Enabled);

            SqliteConnection.ClearAllPools();
            var reloaded = new L12PlatformStore(path);
            var reloadedAdmin = reloaded.Login("Admin", "L12master").Account!;
            var persisted = reloaded.OperationsConfig(reloadedAdmin);
            Assert.True(persisted.ImmediateMaintenance!.Enabled);
            Assert.Equal(6, persisted.ImmediateMaintenance.ExpectedDurationHours);

            var activeSchedule = reloaded.ApplyOperationsConfig(reloadedAdmin, persisted.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "即时维护结束后继续预约维护",
                    now.AddMinutes(-1), now.AddHours(1), 2, 4),
            }, persisted.Version, "activate overlapping schedule", Context("active-schedule")).Current;
            Assert.True(activeSchedule.ImmediateMaintenance!.Enabled);
            var ended = reloaded.EndImmediateMaintenance(reloadedAdmin, activeSchedule.Version,
                "end immediate maintenance", Context("immediate-end"));
            Assert.True(ended.Applied);
            Assert.False(ended.Current.ImmediateMaintenance!.Enabled);
            Assert.Null(ended.Current.ImmediateMaintenance.StartedAt);
            var scheduledStillActive = reloaded.EffectiveOperationsPolicy(now);
            Assert.True(scheduledStillActive.Maintenance.Active);
            Assert.True(scheduledStillActive.Maintenance.EntryBlocked);
            Assert.False(scheduledStillActive.Maintenance.ImmediateActive);
            var duplicateEnd = reloaded.EndImmediateMaintenance(reloadedAdmin, activeSchedule.Version,
                "retry end after timeout", Context("immediate-end-retry"));
            Assert.True(duplicateEnd.AlreadyApplied);
            Assert.Equal(ended.Current.Version, duplicateEnd.Current.Version);
            Assert.Equal("maintenance_duration_invalid", Assert.Throws<L12OperationsConfigException>(() =>
                reloaded.BeginImmediateMaintenance(reloadedAdmin, 0, ended.Current.Version,
                    "invalid duration", Context("invalid-duration"))).Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacyOperationsSnapshotWithoutImmediateStateLoadsClosedAndCanTransition()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            _ = new L12PlatformStore(path);
            var mirror = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            mirror["OperationsConfig"]!.AsObject().Remove("ImmediateMaintenance");
            foreach (var history in mirror["OperationsConfigHistory"]!.AsArray().OfType<JsonObject>())
                history["Config"]!.AsObject().Remove("ImmediateMaintenance");
            File.WriteAllText(path, mirror.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));

            var migrated = new L12PlatformStore(path);
            var admin = migrated.Login("Admin", "L12master").Account!;
            var current = migrated.OperationsConfig(admin);
            Assert.NotNull(current.ImmediateMaintenance);
            Assert.False(current.ImmediateMaintenance!.Enabled);
            Assert.Equal(2, current.ImmediateMaintenance.ExpectedDurationHours);

            var started = migrated.BeginImmediateMaintenance(admin, 8, current.Version,
                "transition legacy snapshot", Context("legacy-transition"));
            Assert.True(started.Current.ImmediateMaintenance!.Enabled);
            Assert.Equal(8, started.Current.ImmediateMaintenance.ExpectedDurationHours);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task HttpActionsRequirePermissionVersionAndIdempotencyAndExposePublicPolicy()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = Catalog();
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = store.Login("Admin", "L12master");
            store.Register("uaf7e9ae33f", "password-123");
            var player = store.Login("uaf7e9ae33f", "password-123");
            Assert.True(player.Success);
            Assert.NotNull(player.Token);
            Assert.NotNull(store.AuthenticateToken(player.Token));
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var version = store.OperationsConfig(admin.Account!).Version;

            using (var forbidden = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance",
                       player.Token!, "forbidden", new OperationsImmediateMaintenanceRequest(4,
                           "player forbidden", "forbidden-key", version)))
            using (var response = await client.SendAsync(forbidden))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var missingVersion = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance",
                       admin.Token!, "missing-version", new OperationsImmediateMaintenanceRequest(4,
                           "missing version", "missing-version-key")))
            using (var response = await client.SendAsync(missingVersion))
                Assert.Equal((HttpStatusCode)428, response.StatusCode);

            L12ImmediateMaintenanceOperationView started;
            var startBody = new OperationsImmediateMaintenanceRequest(4, "http start", "start-key", version);
            using (var start = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance",
                       admin.Token!, "start", startBody))
            using (var response = await client.SendAsync(start))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                started = (await response.Content.ReadFromJsonAsync<L12ImmediateMaintenanceOperationView>())!;
                Assert.True(started.Applied);
                Assert.True(started.Current.ImmediateMaintenance!.Enabled);
            }
            using (var replay = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance",
                       admin.Token!, "start-replay", startBody))
            using (var response = await client.SendAsync(replay))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-Idempotent-Replay")));
                Assert.True((await response.Content.ReadFromJsonAsync<L12ImmediateMaintenanceOperationView>())!.Applied);
            }
            using (var stateRetry = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance",
                       admin.Token!, "start-state-retry", new OperationsImmediateMaintenanceRequest(5,
                           "state retry", "start-key-2", version)))
            using (var response = await client.SendAsync(stateRetry))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var retry = (await response.Content.ReadFromJsonAsync<L12ImmediateMaintenanceOperationView>())!;
                Assert.True(retry.AlreadyApplied);
                Assert.Equal(4, retry.Current.ImmediateMaintenance!.ExpectedDurationHours);
            }
            using (var invalid = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance",
                       admin.Token!, "invalid", new OperationsImmediateMaintenanceRequest(169,
                           "invalid duration", "invalid-key", started.Current.Version)))
            using (var response = await client.SendAsync(invalid))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using (var legacyStart = Authorized(HttpMethod.Post, "/api/admin/operations/server/start",
                       admin.Token!, "legacy-start", new OperationsServerStartRequest(
                           "legacy start cannot end immediate", "legacy-start-key", started.Current.Version)))
            using (var response = await client.SendAsync(legacyStart))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var legacy = (await response.Content.ReadFromJsonAsync<L12ServerStartOperationView>())!;
                Assert.True(legacy.AlreadyStarted);
                Assert.True(legacy.Current.ImmediateMaintenance!.Enabled);
            }

            using (var publicPolicy = await client.GetAsync("/api/operations/effective-policy"))
            {
                Assert.Equal(HttpStatusCode.OK, publicPolicy.StatusCode);
                var policy = JsonNode.Parse(await publicPolicy.Content.ReadAsStringAsync())!.AsObject();
                var maintenance = policy["maintenance"]!.AsObject();
                Assert.True(maintenance["active"]!.GetValue<bool>());
                Assert.True(maintenance["entryBlocked"]!.GetValue<bool>());
                Assert.True(maintenance["immediateActive"]!.GetValue<bool>());
                Assert.Equal(4, maintenance["immediateExpectedDurationHours"]!.GetValue<int>());
                Assert.Equal("当前服务器维护中，预计维护时间为4小时。",
                    maintenance["broadcastMessage"]!.GetValue<string>());
            }

            using (var end = Authorized(HttpMethod.Post, "/api/admin/operations/server/maintenance/end",
                       admin.Token!, "end", new OperationsImmediateMaintenanceEndRequest(
                           "http end", "end-key", started.Current.Version)))
            using (var response = await client.SendAsync(end))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var ended = (await response.Content.ReadFromJsonAsync<L12ImmediateMaintenanceOperationView>())!;
                Assert.True(ended.Applied);
                Assert.False(ended.Current.ImmediateMaintenance!.Enabled);
            }
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

    [Fact]
    public async Task ImmediateMaintenanceBlocksNewGamesButRunningGameCanReconnectAndFinish()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = Catalog();
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = store.Login("Admin", "L12master").Account!;
            var host = store.Register("timmed8ad19", "password-123").Account!;
            var guest = store.Register("timmede60be", "password-123").Account!;
            var waiting = store.Register("timmed693af", "password-123").Account!;
            var rooms = new L12RoomManager(catalog, recorder, store);
            var hostSession = Guid.NewGuid();
            var guestSession = Guid.NewGuid();
            rooms.Connect(hostSession, host.Id, host.Username);
            rooms.Connect(guestSession, guest.Id, guest.Username);
            var created = rooms.CreateRoom(hostSession);
            var roomCode = Payload(created[0])["roomCode"]!.GetValue<string>();
            rooms.JoinRoom(guestSession, roomCode);
            await rooms.SetReadyAsync(hostSession, true);
            var gameStarted = await rooms.SetReadyAsync(guestSession, true);
            Assert.Contains(gameStarted, message => MessageType(message) == "gameState");

            var current = store.OperationsConfig(admin);
            var schedule = store.ApplyOperationsConfig(admin, current.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "预约维护",
                    DateTimeOffset.UtcNow.AddMinutes(-1), null, 2, 2),
            }, current.Version, "overlapping scheduled maintenance", Context("overlap-schedule")).Current;
            store.BeginImmediateMaintenance(admin, 3, schedule.Version,
                "protect running game", Context("overlap-immediate"));

            Assert.Empty(await rooms.TickRankedClocksAsync(DateTimeOffset.UtcNow));
            var waitingSession = Guid.NewGuid();
            rooms.Connect(waitingSession, waiting.Id, waiting.Username);
            var blocked = Assert.Single(rooms.CreateRoom(waitingSession));
            Assert.Equal("maintenance_active", Payload(blocked)["code"]!.GetValue<string>());

            rooms.Disconnect(hostSession);
            var reconnectedSession = Guid.NewGuid();
            var connection = JsonSerializer.SerializeToNode(
                rooms.Connect(reconnectedSession, host.Id, host.Username), JsonOptions)!.AsObject();
            Assert.True(connection["recovered"]!.GetValue<bool>());
            var recovered = rooms.RecoveryState(reconnectedSession);
            var recoveredGame = recovered.First(message => MessageType(message) == "gameState");
            Assert.NotEqual("GameOver", Payload(recoveredGame)["state"]!["phase"]!.GetValue<string>());

            using var surrender = JsonDocument.Parse("{\"type\":\"surrender\"}");
            var finished = await rooms.HandleActionAsync(reconnectedSession, surrender.RootElement);
            var gameOver = finished.First(message => MessageType(message) == "gameState");
            Assert.Equal("GameOver", Payload(gameOver)["state"]!["phase"]!.GetValue<string>());
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ImmediateMaintenanceBlocksTournamentStartButAllowsRunningTournamentReentry()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var organizer = store.Register("MaintTOrg", "password-123").Account!;
            var player = store.Register("MaintTP2", "password-123").Account!;
            var tournament = store.CreateTournament(organizer,
                new L12TournamentCreatePayload("维护赛事", "single", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "现行规则", "maintenance gate", "after", "season",
                    string.Empty, 50, 5, RegistrationVisibility: "public", LateGraceMinutes: 5),
                TournamentContext("create"), true);
            var organizerDeck = store.Decks(organizer.Id)[0];
            tournament = store.UpdateTournamentRegistration(organizer, tournament.Id,
                new L12TournamentRegistrationPayload(organizerDeck.Name, string.Empty), tournament.Version,
                TournamentContext("organizer-deck"), true);
            var playerDeck = store.Decks(player.Id)[0];
            tournament = store.RegisterTournament(player, tournament.Id,
                new L12TournamentRegistrationPayload(playerDeck.Name, string.Empty), tournament.Version,
                TournamentContext("player-deck"), true);
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                TournamentContext("start"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            tournament = store.CheckInTournament(organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(organizer.Id, true), tournament.Version,
                TournamentContext("check-in-organizer"), true);
            tournament = store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                TournamentContext("check-in-player"), true);
            tournament = store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                TournamentContext("round-start"), true);

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            var organizerSession = Guid.NewGuid();
            var playerSession = Guid.NewGuid();
            rooms.Connect(organizerSession, organizer.Id, organizer.Username);
            rooms.Connect(playerSession, player.Id, player.Username);
            var firstEntry = await rooms.EnterTournamentMatchAsync(organizerSession, tournament.Id, match.Id);
            Assert.Contains(firstEntry, message => MessageType(message) == "roomState");

            var admin = store.Login("Admin", "L12master").Account!;
            var beforeMaintenance = store.OperationsConfig(admin);
            var active = store.BeginImmediateMaintenance(admin, 2, beforeMaintenance.Version,
                "block tournament start", Context("tournament-maintenance"));
            var deniedStart = Assert.Single(await rooms.EnterTournamentMatchAsync(
                playerSession, tournament.Id, match.Id));
            Assert.Equal("operationsBlocked", MessageType(deniedStart));
            Assert.Equal("maintenance_active", Payload(deniedStart)["code"]!.GetValue<string>());
            Assert.DoesNotContain(rooms.RecoveryState(organizerSession),
                message => MessageType(message) == "gameState");

            store.EndImmediateMaintenance(admin, active.Current.Version,
                "allow tournament start", Context("tournament-open"));
            var started = await rooms.EnterTournamentMatchAsync(playerSession, tournament.Id, match.Id);
            Assert.Contains(started, message => MessageType(message) == "gameState");

            var current = store.OperationsConfig(admin);
            store.BeginImmediateMaintenance(admin, 3, current.Version,
                "protect running tournament", Context("tournament-running"));
            rooms.Disconnect(organizerSession);
            var reconnectedSession = Guid.NewGuid();
            var connection = JsonSerializer.SerializeToNode(
                rooms.Connect(reconnectedSession, organizer.Id, organizer.Username), JsonOptions)!.AsObject();
            Assert.True(connection["recovered"]!.GetValue<bool>());
            var recovered = rooms.RecoveryState(reconnectedSession);
            Assert.DoesNotContain(recovered, message => MessageType(message) == "operationsBlocked");
            var game = recovered.First(message => MessageType(message) == "gameState");
            Assert.NotEqual("GameOver", Payload(game)["state"]!["phase"]!.GetValue<string>());
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "admin.operations.write", RequestMethod: "TEST",
            RequestPath: "/test/immediate-maintenance");

    private static L12AdminAuditContext TournamentContext(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST",
            RequestPath: "/test/tournaments");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token,
        string correlationId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, correlationId);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static JsonObject Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToNode(message.Payload, JsonOptions)!.AsObject();

    private static string MessageType(OutgoingMessage message)
        => Payload(message)["type"]!.GetValue<string>();

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-immediate-maintenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
