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
public sealed class ControlPlanePlatformStoreTests
{
    [Fact]
    public void PermissionMatrixUsesRolesWithoutScatteredRoleChecks()
    {
        var player = Account("player");
        var admin = Account("admin");

        Assert.True(L12Authorization.HasPermission(player, L12Permission.SessionsReadOwn));
        Assert.False(L12Authorization.HasPermission(player, L12Permission.AdminBugsRead));
        Assert.True(L12Authorization.HasPermission(player, L12Permission.TournamentsCreate));
        Assert.False(L12Authorization.HasPermission(player, L12Permission.TournamentsManage));
        Assert.False(L12Authorization.IsKnownRole("support"));
        Assert.False(L12Authorization.IsKnownRole("editor"));
        Assert.False(L12Authorization.IsKnownRole("referee"));
        Assert.False(L12Authorization.IsKnownRole("organizer"));
        Assert.False(L12Authorization.IsKnownRole("release-manager"));
        Assert.All(Enum.GetValues<L12Permission>(), permission =>
            Assert.True(L12Authorization.HasPermission(admin, permission)));
        Assert.Equal(new[] { "admin", "player" }, L12Authorization.Roles);
        Assert.Equal(L12Authorization.PermissionsForRole("player"), player.Permissions);
    }

    [Fact]
    public void SessionsAreOwnedImmediatelyRevocableAndDuplicateRevocationIsIdempotent()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var first = store.Register("SessionOwner", "password-123");
            var second = store.Login("SessionOwner", "password-123");
            var outsider = store.Register("SessionOther", "password-456");
            var ownerAuthentication = store.AuthenticateSession($"Bearer {first.Token}")!;
            var outsiderAuthentication = store.AuthenticateSession($"Bearer {outsider.Token}")!;
            var ownerSessions = store.Sessions(first.Account!.Id, ownerAuthentication.SessionId);

            Assert.Equal(2, ownerSessions.Count);
            Assert.Single(ownerSessions, session => session.Current);
            Assert.All(ownerSessions, session => Assert.True(session.ExpiresAt > session.CreatedAt));

            var foreign = store.RevokeOwnSession(ownerAuthentication, outsiderAuthentication.SessionId);
            Assert.False(foreign.Found);
            Assert.NotNull(store.AuthenticateToken(outsider.Token));

            var otherOwnedSession = ownerSessions.Single(session => !session.Current);
            var revoked = store.RevokeOwnSession(ownerAuthentication, otherOwnedSession.Id);
            Assert.True(revoked.Found);
            Assert.Equal(1, revoked.RevokedCount);
            Assert.Null(store.AuthenticateToken(second.Token));

            var duplicate = store.RevokeOwnSession(ownerAuthentication, otherOwnedSession.Id);
            Assert.True(duplicate.Found);
            Assert.True(duplicate.AlreadyRevoked);
            Assert.Equal(0, duplicate.RevokedCount);

            var admin = store.Login("Admin", "L12master").Account!;
            var adminRevocation = store.RevokeAccountSession(admin, outsider.Account!.Id,
                outsiderAuthentication.SessionId);
            Assert.True(adminRevocation.Found);
            Assert.Null(store.AuthenticateToken(outsider.Token));

            var current = store.RevokeOwnSession(ownerAuthentication, ownerAuthentication.SessionId);
            Assert.Equal(1, current.RevokedCount);
            Assert.Null(store.AuthenticateToken(first.Token));

            var allFirst = store.Register("SessionAll", "password-789");
            var allSecond = store.Login("SessionAll", "password-789");
            var allAuthentication = store.AuthenticateTokenSession(allFirst.Token)!;
            var allRevoked = store.RevokeOwnSessions(allAuthentication);
            Assert.Equal(2, allRevoked.RevokedCount);
            Assert.Null(store.AuthenticateToken(allFirst.Token));
            Assert.Null(store.AuthenticateToken(allSecond.Token));
            var allDuplicate = store.RevokeOwnSessions(allAuthentication);
            Assert.True(allDuplicate.AlreadyRevoked);
            Assert.Equal(0, allDuplicate.RevokedCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PasswordChangeKeepsCurrentSessionAndRevokesOtherSessions()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var current = store.Register("PasswordOwner", "password-123");
            var oldDevice = store.Login("PasswordOwner", "password-123");
            var currentAuthentication = store.AuthenticateSession($"Bearer {current.Token}")!;

            var changed = store.ChangePassword(current.Account!.Id, "password-123", "new-password-456",
                currentAuthentication.SessionId);

            Assert.True(changed.Success);
            Assert.NotNull(store.AuthenticateToken(current.Token));
            Assert.Null(store.AuthenticateToken(oldDevice.Token));
            Assert.False(store.Login("PasswordOwner", "password-123").Success);
            Assert.True(store.Login("PasswordOwner", "new-password-456").Success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyPlatformJsonSessionsGainStableMetadataWithoutInvalidatingTokens()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var registered = store.Register("LegacySession", "password-123");
            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            document.Remove("Version");
            foreach (var account in document["Accounts"]!.AsArray().OfType<JsonObject>())
                account.Remove("PermissionVersion");
            foreach (var session in document["Sessions"]!.AsArray().OfType<JsonObject>())
            {
                session.Remove("Id");
                session.Remove("ExpiresAt");
                session.Remove("RevokedAt");
                session.Remove("AuthStrength");
                session.Remove("PermissionVersion");
            }
            File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var reloaded = new L12PlatformStore(path);
            var authenticated = reloaded.AuthenticateSession($"Bearer {registered.Token}");
            Assert.NotNull(authenticated);
            var sessionView = Assert.Single(reloaded.Sessions(registered.Account!.Id, authenticated!.SessionId));
            Assert.Equal(32, sessionView.Id.Length);
            Assert.Equal("password", sessionView.AuthStrength);
            Assert.True(sessionView.ExpiresAt > sessionView.CreatedAt);
            Assert.True(sessionView.Current);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AuditContextPersistsCorrelationPermissionAndDenials()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var registered = store.Register("AuditedUser", "password-123");
            var user = registered.Account!;
            var writeContext = new L12AdminAuditContext("write-correlation-1",
                L12Authorization.Key(L12Permission.AdminAccountRolesWrite), RequestMethod: "PUT",
                RequestPath: $"/api/admin/accounts/{user.Id}/role");
            Assert.True(store.SetRole(admin, user.Id, "admin", writeContext));
            var refreshed = store.AuthenticateToken(registered.Token);
            Assert.NotNull(refreshed);
            Assert.Equal("admin", refreshed!.Role);
            Assert.True(L12Authorization.HasPermission(refreshed, L12Permission.AdminBugsRead));
            Assert.True(L12Authorization.HasPermission(refreshed, L12Permission.AdminAccountsRead));
            store.RecordAuthorizationDenied(user,
                new L12AdminAuditContext("deny-correlation-1",
                    L12Authorization.Key(L12Permission.AdminAccountsRead), RequestMethod: "GET",
                    RequestPath: "/api/admin/accounts", Outcome: "denied"),
                L12Authorization.Key(L12Permission.AdminAccountsRead), "permission-denied");

            var reloaded = new L12PlatformStore(path);
            Assert.Contains(reloaded.AdminAudit("account"), audit => audit.CorrelationId == "write-correlation-1"
                && audit.Permission == "admin.accounts.roles.write" && audit.Outcome == "succeeded");
            Assert.Contains(reloaded.AdminAudit("security"), audit => audit.CorrelationId == "deny-correlation-1"
                && audit.Permission == "admin.accounts.read" && audit.Outcome == "denied"
                && audit.Reason == "permission-denied");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CommandBoundaryReplaysBeforeVersionCheckAndRejectsConflicts()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var target = store.Register("CommandTarget", "password-123").Account!;
            var admin = store.Login("Admin", "L12master").Account!;
            var bus = new L12AdminCommandBus(store);
            var expectedVersion = target.PermissionVersion;

            L12AdminCommandEnvelope<RoleCommandPayload> Command(string role, string key, long version)
            {
                var commandId = Guid.NewGuid().ToString("N");
                var context = new L12AdminAuditContext("command-boundary-1",
                    L12Authorization.Key(L12Permission.AdminAccountRolesWrite), commandId, key, version,
                    RequestMethod: "PUT", RequestPath: $"/api/admin/accounts/{target.Id}/role");
                return new L12AdminCommandEnvelope<RoleCommandPayload>(commandId, key, "account.role.set", admin,
                    DateTimeOffset.UtcNow, $"account:{target.Id}", null, false, version,
                    new RoleCommandPayload(target.Id, role), context);
            }

            L12AdminCommandResult<RoleCommandResult> Execute(L12AdminCommandEnvelope<RoleCommandPayload> command)
                => bus.Execute(command, L12Permission.AdminAccountRolesWrite,
                    current => store.SetRole(current.Actor, current.Payload.AccountId, current.Payload.Role,
                            current.AuditContext)
                        ? L12AdminCommandResult<RoleCommandResult>.Ok(
                            new RoleCommandResult(current.Payload.AccountId, current.Payload.Role, true))
                        : L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "账号或角色无效", 400));

            var first = Execute(Command("admin", "role-command-1", expectedVersion));
            var replay = Execute(Command("admin", "role-command-1", expectedVersion));
            var reused = Execute(Command("player", "role-command-1", expectedVersion));
            var stale = Execute(Command("player", "role-command-2", expectedVersion));

            Assert.True(first.Success);
            Assert.True(replay.Success);
            Assert.True(replay.Replayed);
            Assert.False(reused.Success);
            Assert.Equal("idempotency_conflict", reused.Code);
            Assert.False(stale.Success);
            Assert.Equal("version_conflict", stale.Code);
            Assert.Equal("admin", store.Accounts().Single(account => account.Id == target.Id).Role);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpCorrelationAuthorizationAndSessionContractsAreEnforced()
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
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            var address = Assert.Single(server.Addresses);
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using (var validCorrelation = new HttpRequestMessage(HttpMethod.Get, "/health"))
            {
                validCorrelation.Headers.Add(L12CorrelationIds.HeaderName, "client-correlation_1");
                using var response = await client.SendAsync(validCorrelation);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("client-correlation_1", Assert.Single(response.Headers.GetValues(L12CorrelationIds.HeaderName)));
            }

            using (var invalidCorrelation = new HttpRequestMessage(HttpMethod.Get, "/health"))
            {
                invalidCorrelation.Headers.TryAddWithoutValidation(L12CorrelationIds.HeaderName, "bad id!");
                using var response = await client.SendAsync(invalidCorrelation);
                var generated = Assert.Single(response.Headers.GetValues(L12CorrelationIds.HeaderName));
                Assert.True(L12CorrelationIds.IsValid(generated));
                Assert.NotEqual("bad id!", generated);
            }

            var owner = store.Register("HttpOwner", "password-123");
            var ownerOther = store.Login("HttpOwner", "password-123");
            var outsider = store.Register("HttpOther", "password-456");
            var outsiderSession = store.AuthenticateTokenSession(outsider.Token)!.SessionId;

            using (var forbidden = Authorized(HttpMethod.Get, "/api/admin/accounts", owner.Token!, "deny-http-1"))
            using (var response = await client.SendAsync(forbidden))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("permission_denied", error!.Code);
                Assert.Equal("deny-http-1", error.CorrelationId);
            }
            Assert.Contains(store.AdminAudit("security"), audit => audit.CorrelationId == "deny-http-1"
                && audit.Permission == "admin.accounts.read" && audit.Outcome == "denied");

            using (var unauthenticated = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit"))
            {
                unauthenticated.Headers.Add(L12CorrelationIds.HeaderName, "deny-http-unauth-1");
                using var response = await client.SendAsync(unauthenticated);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            Assert.Contains(store.AdminAudit("security"), audit => audit.CorrelationId == "deny-http-unauth-1"
                && audit.Permission == "admin.audit.read" && audit.Reason == "authentication-required");

            using (var list = Authorized(HttpMethod.Get, "/api/auth/sessions", owner.Token!, "sessions-list-1"))
            using (var response = await client.SendAsync(list))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var sessions = await response.Content.ReadFromJsonAsync<L12SessionView[]>();
                Assert.Equal(2, sessions!.Length);
                Assert.Single(sessions, session => session.Current);
            }

            using (var foreign = Authorized(HttpMethod.Delete,
                       $"/api/auth/sessions/{outsiderSession}", owner.Token!, "foreign-session-1"))
            using (var response = await client.SendAsync(foreign))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotNull(store.AuthenticateToken(outsider.Token));

            var ownerAuthentication = store.AuthenticateTokenSession(owner.Token)!;
            var ownerOtherSession = store.Sessions(owner.Account!.Id, ownerAuthentication.SessionId)
                .Single(session => !session.Current);
            using (var revoke = Authorized(HttpMethod.Delete,
                       $"/api/auth/sessions/{ownerOtherSession.Id}", owner.Token!, "revoke-session-1"))
            using (var response = await client.SendAsync(revoke))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<RevocationResponse>();
                Assert.Equal(1, result!.RevokedCount);
                Assert.False(result.AlreadyRevoked);
            }
            Assert.Null(store.AuthenticateToken(ownerOther.Token));

            using (var duplicate = Authorized(HttpMethod.Delete,
                       $"/api/auth/sessions/{ownerOtherSession.Id}", owner.Token!, "revoke-session-2"))
            using (var response = await client.SendAsync(duplicate))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<RevocationResponse>();
                Assert.True(result!.AlreadyRevoked);
                Assert.Equal(0, result.RevokedCount);
            }

            var admin = store.Login("Admin", "L12master");
            using (var supportBugs = Authorized(HttpMethod.Get, "/api/admin/bugs", owner.Token!, "support-bugs-1"))
            using (var response = await client.SendAsync(supportBugs))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var supportAccounts = Authorized(HttpMethod.Get, "/api/admin/accounts", owner.Token!,
                       "support-accounts-1"))
            using (var response = await client.SendAsync(supportAccounts))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var adminRevoke = Authorized(HttpMethod.Delete,
                       $"/api/admin/accounts/{outsider.Account!.Id}/sessions/{outsiderSession}", admin.Token!,
                       "admin-revoke-1"))
            using (var response = await client.SendAsync(adminRevoke))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(store.AuthenticateToken(outsider.Token));

            using (var write = Authorized(HttpMethod.Put, "/api/admin/content/home.headline/draft", admin.Token!,
                       "write-http-1", new { value = "draft" }))
            using (var response = await client.SendAsync(write))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(store.AdminAudit("content"), audit => audit.CorrelationId == "write-http-1"
                && audit.Permission == "admin.content.draft");
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

    private static L12AccountView Account(string role)
        => new(Guid.NewGuid().ToString("N"), role, role, DateTimeOffset.UtcNow, true);

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-control-plane-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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

    private sealed record RevocationResponse(string? SessionId, int RevokedCount, bool AlreadyRevoked);
}
