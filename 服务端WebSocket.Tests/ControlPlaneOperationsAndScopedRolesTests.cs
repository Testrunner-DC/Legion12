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
public sealed class ControlPlaneOperationsAndScopedRolesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DurableRolesArePlayerAndAdminAndLegacyRolesMigratePersistently()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var original = new L12PlatformStore(path);
            var account = original.Register("LegacyRoleUser", "password-123").Account!;
            var mirror = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var row = mirror["Accounts"]!.AsArray().OfType<JsonObject>()
                .Single(item => item["Id"]!.GetValue<string>() == account.Id);
            row["Role"] = "release-manager";
            File.WriteAllText(path, mirror.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));

            var migrated = new L12PlatformStore(path);

            Assert.Equal(new[] { "admin", "player" }, L12Authorization.Roles);
            Assert.Equal("player", migrated.Account(account.Id)!.Role);
            Assert.False(L12Authorization.IsKnownRole("support"));
            Assert.False(L12Authorization.IsKnownRole("editor"));
            Assert.False(L12Authorization.IsKnownRole("referee"));
            Assert.False(L12Authorization.IsKnownRole("organizer"));
            Assert.False(L12Authorization.IsKnownRole("release-manager"));
            Assert.Equal("player", JsonNode.Parse(File.ReadAllText(path))!["Accounts"]!.AsArray()
                .OfType<JsonObject>().Single(item => item["Id"]!.GetValue<string>() == account.Id)["Role"]!
                .GetValue<string>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TournamentCreatorAndFriendRefereeReceiveOnlyTournamentScopedAuthority()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var organizer = store.Register("ScopedOrganizer", "password-123").Account!;
            var referee = store.Register("ScopedReferee", "password-123").Account!;
            var outsider = store.Register("ScopedOutsider", "password-123").Account!;

            Assert.True(store.SendFriendRequest(organizer.Id, referee.Id).Success);
            Assert.True(store.ResolveFriendRequest(referee.Id, organizer.Id, true).Success);
            var tournament = store.CreateTournament(organizer, CreateTournamentPayload([referee.Id]),
                Context("scoped-create"), true);

            Assert.Equal(organizer.Id, tournament.OrganizerAccountId);
            Assert.Equal("player", store.Account(referee.Id)!.Role);
            Assert.False(L12Authorization.HasPermission(referee, L12Permission.TournamentRulingsWrite));
            Assert.False(L12Authorization.HasPermission(organizer, L12Permission.TournamentsManage));
            Assert.Equal(referee.Id, Assert.Single(tournament.Referees).AccountId);
            Assert.Throws<L12TournamentScopeException>(() => store.SetTournamentStaff(outsider,
                tournament.Id, new L12TournamentStaffPayload([]), tournament.Version,
                Context("outsider-staff"), true));

            var otherOrganizer = store.Register("OtherOrganizer", "password-123").Account!;
            Assert.Throws<ArgumentException>(() => store.CreateTournament(otherOrganizer,
                CreateTournamentPayload([referee.Id]), Context("not-friends"), true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OperationsConfigPreviewApplyReplayHistoryRollbackAndRestartAreConsistent()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.OperationsConfig(admin);
            var changed = initial.Config with
            {
                Season = initial.Config.Season with { Id = "S02", Name = "S02" },
                CardRestrictions = [new L12CardRestrictionConfig("S01-0001", 0, "season ban")],
                Maintenance = new L12MaintenanceConfig(true, "计划维护", null, null),
            };

            var preview = store.PreviewOperationsConfig(admin, changed, initial.Version,
                Context("ops-preview") with { Permission = "admin.operations.write" });
            Assert.True(preview.Valid);
            Assert.Contains("season", preview.Changes);
            Assert.Equal(initial.Version, store.OperationsConfig(admin).Version);

            var commandId = Guid.NewGuid().ToString("N");
            var envelope = new L12AdminCommandEnvelope<L12OperationsConfigPayload>(commandId,
                "ops-apply-1", "operations.config.apply", admin, DateTimeOffset.UtcNow,
                "operations:config", "activate S02", false, initial.Version, changed,
                Context("ops-apply-1") with
                {
                    Permission = "admin.operations.write",
                    CommandId = commandId,
                    IdempotencyKey = "ops-apply-1",
                    ExpectedVersion = initial.Version,
                });
            var bus = new L12AdminCommandBus(store);
            L12AdminCommandResult<L12OperationsConfigOperationView> Execute()
                => bus.Execute(envelope, L12Permission.AdminOperationsWrite,
                    current => L12AdminCommandResult<L12OperationsConfigOperationView>.Ok(
                        store.ApplyOperationsConfig(current.Actor, current.Payload,
                            current.ExpectedVersion!.Value, current.Reason, current.AuditContext)));

            var applied = Execute();
            var replay = Execute();
            Assert.True(applied.Success);
            Assert.False(applied.Pending);
            Assert.True(replay.Success);
            Assert.True(replay.Replayed);
            Assert.Equal(initial.Version + 1, store.OperationsConfig(admin).Version);
            var appliedVersionId = applied.Value!.HistoryEntry.Id;

            var reloaded = new L12PlatformStore(path);
            var reloadedAdmin = reloaded.Login("Admin", "L12master").Account!;
            var persisted = reloaded.OperationsConfig(reloadedAdmin);
            Assert.Equal("S02", persisted.Config.Season.Id);
            Assert.True(persisted.Config.Maintenance.Enabled);
            Assert.Contains(reloaded.OperationsConfigHistory(reloadedAdmin), item => item.Id == appliedVersionId);

            var rollback = reloaded.RollbackOperationsConfig(reloadedAdmin, initial.VersionId,
                persisted.Version, "restore initial", Context("ops-rollback"));
            Assert.True(rollback.Applied);
            Assert.Equal("S01", rollback.Current.Config.Season.Id);
            Assert.Equal(persisted.Version + 1, rollback.Current.Version);
            Assert.StartsWith("rollback:", rollback.HistoryEntry.Action);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OperationsConfigRejectsUnlockingOrMovingAnnihilation()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);

            var unlocked = current.Config with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(current.Config.DisasterPool.CardIds, false),
            };
            var moved = current.Config with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(
                    [L12PlatformStore.AnnihilationCardId, "S01-DS01"], true),
            };

            Assert.Equal("annihilation_locked", Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewOperationsConfig(admin, unlocked, current.Version, Context("unlock"))).Code);
            Assert.Equal("annihilation_locked", Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewOperationsConfig(admin, moved, current.Version, Context("move"))).Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task HttpAccountChangesOperationsAndRuntimeAreDirectVersionedAndTruthful()
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
            var admin = store.Login("Admin", "L12master");
            var target = store.Register("DirectRoleTarget", "password-123");
            var tournamentOrganizer = store.Register("HttpScopedOrganizer", "password-123");
            var tournamentReferee = store.Register("HttpScopedReferee", "password-123");
            Assert.True(store.SendFriendRequest(tournamentOrganizer.Account!.Id,
                tournamentReferee.Account!.Id).Success);
            Assert.True(store.ResolveFriendRequest(tournamentReferee.Account.Id,
                tournamentOrganizer.Account.Id, true).Success);
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            L12TournamentView createdTournament;
            using (var createTournament = Authorized(HttpMethod.Post, "/api/tournaments",
                       tournamentOrganizer.Token!, "direct-tournament-create",
                       new TournamentCreateRequest(CreateTournamentPayload(), "direct-tournament-create-1",
                           store.Version, false, "creator is organizer")))
            using (var response = await client.SendAsync(createTournament))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                createdTournament = (await response.Content.ReadFromJsonAsync<L12TournamentView>())!;
                Assert.Equal(tournamentOrganizer.Account.Id, createdTournament.OrganizerAccountId);
            }
            using (var setStaff = Authorized(HttpMethod.Put,
                       $"/api/tournaments/{createdTournament.Id}/staff", tournamentOrganizer.Token!,
                       "direct-tournament-staff", new TournamentStaffRequest([tournamentReferee.Account.Id],
                           "direct-tournament-staff-1", createdTournament.Version, false, "friend referee")))
            using (var response = await client.SendAsync(setStaff))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var updated = await response.Content.ReadFromJsonAsync<L12TournamentView>();
                Assert.Equal(tournamentReferee.Account.Id, Assert.Single(updated!.Referees).AccountId);
            }
            Assert.Empty(store.AdminApprovals());
            using (var removedTournamentApprovals = Authorized(HttpMethod.Get,
                       $"/api/tournaments/{createdTournament.Id}/approvals", tournamentOrganizer.Token!,
                       "removed-tournament-approvals"))
            using (var response = await client.SendAsync(removedTournamentApprovals))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            using (var roleRequest = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account!.Id}/role", admin.Token!, "direct-role",
                       new RoleRequest("admin", "direct-role-1", target.Account.PermissionVersion, false, "promote")))
            using (var response = await client.SendAsync(roleRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<RoleCommandResult>();
                Assert.Equal("admin", result!.Role);
                Assert.True(result.Changed);
            }
            using (var sameRole = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/role", admin.Token!, "same-role",
                       new RoleRequest("admin", "same-role-1", store.Account(target.Account.Id)!.PermissionVersion,
                           false, "no change")))
            using (var response = await client.SendAsync(sameRole))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<RoleCommandResult>();
                Assert.False(result!.Changed);
            }

            var legacyApprovalId = Guid.NewGuid().ToString("N");
            var legacyApproval = new L12AdminCommandEnvelope<RoleCommandPayload>(legacyApprovalId,
                "legacy-direct-role-approval", "account.role.set", admin.Account!, DateTimeOffset.UtcNow,
                $"account:{target.Account.Id}", "legacy approval must stay disabled", false,
                store.Account(target.Account.Id)!.PermissionVersion,
                new RoleCommandPayload(target.Account.Id, "player"),
                Context("legacy-direct-role-approval") with
                {
                    CommandId = legacyApprovalId,
                    IdempotencyKey = "legacy-direct-role-approval",
                    ExpectedVersion = store.Account(target.Account.Id)!.PermissionVersion,
                });
            var legacyPending = new L12AdminCommandBus(store).Execute(legacyApproval,
                L12Permission.AdminAccountRolesWrite,
                current => L12AdminCommandResult<RoleCommandResult>.Ok(
                    new RoleCommandResult(current.Payload.AccountId, current.Payload.Role, true)),
                risk: L12AdminCommandRisk.High);
            Assert.True(legacyPending.Pending);
            Assert.Empty(store.AdminApprovals());
            Assert.Equal(0, store.SecurityStatus(admin.Account!).PendingApprovals);
            using (var legacyReview = Authorized(HttpMethod.Post,
                       $"/api/admin/approvals/{legacyApprovalId}", admin.Token!, "legacy-role-review",
                       new { decision = "approve", reason = "must remain disabled" }))
            using (var response = await client.SendAsync(legacyReview))
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("admin", store.Account(target.Account.Id)!.Role);

            using (var rejectedLegacy = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/role", admin.Token!, "legacy-role",
                       new RoleRequest("organizer", "legacy-role-1", store.Account(target.Account.Id)!.PermissionVersion,
                           false, "legacy")))
            using (var response = await client.SendAsync(rejectedLegacy))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using (var statusRequest = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/status", admin.Token!, "direct-status",
                       new AccountStatusRequest(true, "security response contract", "direct-status-1",
                           store.Account(target.Account.Id)!.PermissionVersion)))
            using (var response = await client.SendAsync(statusRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<L12AccountStatusOperationView>();
                Assert.True(result!.Applied);
                Assert.True(result.Account.Disabled);
            }

            L12OperationsConfigView current;
            using (var getConfig = Authorized(HttpMethod.Get, "/api/admin/operations/config", admin.Token!, "ops-get"))
            using (var response = await client.SendAsync(getConfig))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                current = (await response.Content.ReadFromJsonAsync<L12OperationsConfigView>())!;
            }
            var next = current.Config with
            {
                FeatureFlags = new Dictionary<string, bool>(current.Config.FeatureFlags)
                {
                    ["tournaments"] = false,
                },
            };
            using (var apply = Authorized(HttpMethod.Put, "/api/admin/operations/config", admin.Token!, "ops-http",
                       new OperationsConfigApplyRequest(next, "disable temporarily", "ops-http-1", current.Version)))
            using (var response = await client.SendAsync(apply))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<L12OperationsConfigOperationView>();
                Assert.False(result!.Current.Config.FeatureFlags["tournaments"]);
            }

            using (var runtime = Authorized(HttpMethod.Get, "/api/admin/runtime/status", admin.Token!, "runtime"))
            using (var response = await client.SendAsync(runtime))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var status = await response.Content.ReadFromJsonAsync<L12RuntimeStatusView>();
                Assert.Equal(catalog.Cards.Count, status!.CardCount);
                Assert.Equal(0, status.WebSocketConnectionCount);
                Assert.False(status.Cdn.Configured);
                Assert.Equal("unavailable", status.Cdn.State);
                Assert.Equal("no-authoritative-source", status.Cdn.Detail);
                Assert.Equal(2, status.ReleaseEnvironments.Count);
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
    public async Task RuntimeStatsExcludeCompletedGames()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder);
            var sessionId = Guid.NewGuid();
            rooms.Connect(sessionId, "sandbox-account", "统计测试");

            await rooms.CreateSandboxAsync(sessionId, new L12SandboxRequest());
            Assert.Equal(1, rooms.RuntimeStats().ActiveGameCount);

            using var surrender = JsonDocument.Parse("{\"type\":\"surrender\"}");
            await rooms.HandleSandboxActionAsync(sessionId, 0, surrender.RootElement);

            Assert.Equal(0, rooms.RuntimeStats().ActiveGameCount);
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static L12TournamentCreatePayload CreateTournamentPayload(IReadOnlyList<string>? referees = null)
        => new("作用域赛事", "swiss", "public", 16, null, "现行规则", "scoped roles",
            "after", "season", string.Empty, 50, 5, referees);

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, RequestMethod: "TEST", RequestPath: "/test");

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
        var path = Path.Combine(Path.GetTempPath(), $"l12-operations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
