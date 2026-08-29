using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlanePhaseSixSecurityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DisabledAccountRevokesOldTokensAndWebSocketWithoutSecondApproval()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        ClientWebSocket? socket = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = store.Login("Admin", "L12master");
            var reviewerRegistration = store.Register("SecurityReviewer", "password-456");
            var target = store.Register("DisabledTarget", "password-789");
            var targetOther = store.Login("DisabledTarget", "password-789");
            var outsider = store.Register("SecurityOutsider", "password-000");
            Assert.True(store.SetRole(admin.Account!, reviewerRegistration.Account!.Id, "admin"));

            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            var address = Assert.Single(server.Addresses);
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using (var capabilityResponse = await client.GetAsync("/api/auth/mfa/capability"))
            {
                Assert.Equal(HttpStatusCode.OK, capabilityResponse.StatusCode);
                var capability = await capabilityResponse.Content.ReadFromJsonAsync<L12MfaCapabilityView>();
                Assert.False(capability!.EnrollmentEnabled);
                Assert.False(capability.SecretsPersisted);
            }
            using (var deniedStatus = Authorized(HttpMethod.Get, "/api/admin/security/status",
                       outsider.Token!, "security-status-read-denied"))
            using (var response = await client.SendAsync(deniedStatus))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var statusRequest = Authorized(HttpMethod.Get, "/api/admin/v1/security/status",
                       reviewerRegistration.Token!, "security-status-read"))
            using (var response = await client.SendAsync(statusRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var status = await response.Content.ReadFromJsonAsync<L12SecurityStatusView>();
                Assert.Equal(store.Version, status!.PlatformVersion);
            }
            using (var deniedArchive = Authorized(HttpMethod.Post, "/api/admin/security/audit-archives",
                       outsider.Token!, "security-archive-denied", new { }))
            using (var response = await client.SendAsync(deniedArchive))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(store.AdminAudit("security"), item => item.CorrelationId == "security-archive-denied"
                && item.Permission == "admin.audit.archive" && item.Outcome == "denied");
            using (var archiveDry = Authorized(HttpMethod.Post, "/api/admin/v1/security/audit-archives",
                       admin.Token!, "security-archive-http-dry", new
                       {
                           retentionDays = 365,
                           idempotencyKey = "security-archive-http-dry",
                           expectedVersion = store.Version,
                           dryRun = true,
                           reason = "http dry-run contract",
                       }))
            using (var response = await client.SendAsync(archiveDry))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var operation = await response.Content.ReadFromJsonAsync<L12AuditArchiveOperationView>();
                Assert.False(operation!.Applied);
            }
            socket = new ClientWebSocket();
            var socketUri = new UriBuilder(address) { Scheme = "ws", Path = "/ws" }.Uri;
            await socket.ConnectAsync(socketUri, CancellationToken.None);
            await SendWebSocketAsync(socket, new { type = "hello", authToken = target.Token });
            Assert.Contains("session", await ReceiveWebSocketTextAsync(socket, TimeSpan.FromSeconds(3)),
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("effectiveOperationsPolicy",
                await ReceiveWebSocketTextAsync(socket, TimeSpan.FromSeconds(3)),
                StringComparison.OrdinalIgnoreCase);

            var outsiderBody = new
            {
                disabled = true,
                reason = "unauthorized status change",
                idempotencyKey = "security-status-outsider",
                expectedVersion = target.Account!.PermissionVersion,
                dryRun = true,
            };
            using (var denied = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account!.Id}/status", outsider.Token!,
                       "security-status-denied", outsiderBody))
            using (var response = await client.SendAsync(denied))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(store.AdminAudit("security"), item => item.CorrelationId == "security-status-denied"
                && item.Permission == "admin.accounts.status.write" && item.Outcome == "denied");

            var rootBody = new
            {
                disabled = true,
                reason = "root must stay recoverable",
                idempotencyKey = "security-status-root",
                expectedVersion = admin.Account!.PermissionVersion,
                dryRun = true,
            };
            using (var rootRequest = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{admin.Account!.Id}/status", admin.Token!,
                       "security-status-root", rootBody))
            using (var response = await client.SendAsync(rootRequest))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("root_admin_protected", error!.Code);
            }

            var disableBody = new
            {
                disabled = true,
                reason = "confirmed account compromise",
                idempotencyKey = "security-status-disable",
                expectedVersion = store.Account(target.Account!.Id)!.PermissionVersion,
            };
            using (var disable = Authorized(HttpMethod.Put,
                       $"/api/admin/v1/accounts/{target.Account.Id}/status", admin.Token!,
                       "security-status-request", disableBody))
            using (var response = await client.SendAsync(disable))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var operation = await response.Content.ReadFromJsonAsync<L12AccountStatusOperationView>();
                Assert.True(operation!.Applied);
                Assert.True(operation.Account.Disabled);
            }

            Assert.True(store.Account(target.Account.Id)!.Disabled);
            Assert.Null(store.AuthenticateToken(target.Token));
            Assert.Null(store.AuthenticateToken(targetOther.Token));
            Assert.Equal("authentication_failed", store.Login("DisabledTarget", "password-789",
                new L12LoginAttemptContext("disabled-login", "disabled-client", "/api/auth/login")).Code);
            await AssertWebSocketInvalidatedAsync(socket);

            var enableBody = new
            {
                disabled = false,
                reason = "security review completed",
                idempotencyKey = "security-status-enable",
                expectedVersion = store.Account(target.Account.Id)!.PermissionVersion,
            };
            using (var enable = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/status", admin.Token!,
                       "security-status-enable", enableBody))
            using (var response = await client.SendAsync(enable))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var operation = await response.Content.ReadFromJsonAsync<L12AccountStatusOperationView>();
                Assert.True(operation!.Applied);
                Assert.False(operation.Account.Disabled);
            }

            Assert.False(store.Account(target.Account.Id)!.Disabled);
            Assert.Null(store.AuthenticateToken(target.Token));
            Assert.True(store.Login("DisabledTarget", "password-789",
                new L12LoginAttemptContext("reenabled-login", "reenabled-client", "/api/auth/login")).Success);
        }
        finally
        {
            socket?.Dispose();
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
    public void LoginThrottleUsesPersistedPrincipalAndClientBucketsWithoutCaseBypass()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            store.Register("RateLimitTarget", "password-123");
            store.Register("RateLimitOther", "password-456");
            Assert.True(store.Login("RateLimitOther", "password-456",
                new L12LoginAttemptContext("login-success", "client-success", "/api/auth/login")).Success);

            for (var index = 0; index < 4; index++)
            {
                var failed = store.Login("RateLimitTarget", "wrong-password",
                    new L12LoginAttemptContext($"login-fail-{index}", "client-a", "/api/auth/login"));
                Assert.Equal("authentication_failed", failed.Code);
            }
            var locked = store.Login("RateLimitTarget", "wrong-password",
                new L12LoginAttemptContext("login-lock", "client-a", "/api/auth/login"));
            Assert.Equal("login_rate_limited", locked.Code);
            Assert.True(locked.RetryAfterSeconds > 0);

            var principalBypass = store.Login("rAtElImItTaRgEt", "password-123",
                new L12LoginAttemptContext("login-case-bypass", "client-b", "/api/auth/login"));
            Assert.Equal("login_rate_limited", principalBypass.Code);
            var clientBypass = store.Login("RateLimitOther", "password-456",
                new L12LoginAttemptContext("login-client-bypass", "client-a", "/api/auth/login"));
            Assert.Equal("login_rate_limited", clientBypass.Code);

            var reloaded = new L12PlatformStore(path);
            var persisted = reloaded.Login("RateLimitTarget", "password-123",
                new L12LoginAttemptContext("login-restart-bypass", "client-c", "/api/auth/login"));
            Assert.Equal("login_rate_limited", persisted.Code);
            var audits = reloaded.AdminAudit("authentication", 100);
            Assert.Contains(audits, item => item.Reason == "invalid-credentials" && item.Outcome == "denied");
            Assert.Contains(audits, item => item.Reason == "locked" && item.Outcome == "denied");
            Assert.Contains(audits, item => item.Reason == "rate-limited" && item.Outcome == "denied");
            Assert.Contains(audits, item => item.Reason == "authenticated" && item.Outcome == "succeeded");
            Assert.Contains(reloaded.SecurityStatus(reloaded.Login("Admin", "L12master").Account!).Alerts,
                item => item.Code == "login-lockout-active");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpLoginReturnsUnauthorizedThenRateLimitedWithRetryAfter()
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
            store.Register("HttpRateLimit", "password-123");
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            for (var index = 0; index < 4; index++)
            {
                using var request = CorrelatedLogin("HttpRateLimit", "wrong", $"http-login-{index}");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal($"http-login-{index}", Assert.Single(response.Headers.GetValues(
                    L12CorrelationIds.HeaderName)));
            }
            using (var request = CorrelatedLogin("HttpRateLimit", "wrong", "http-login-locked"))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.True(response.Headers.RetryAfter?.Delta is not null
                    || response.Headers.TryGetValues("Retry-After", out var values)
                    && int.TryParse(values.Single(), out var seconds) && seconds > 0);
                var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("login_rate_limited", error!.Code);
                Assert.Equal("http-login-locked", error.CorrelationId);
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
    public void OfflineBootstrapIsFeatureGatedOneTimeAndIdempotentAcrossRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        var previousEnabled = Environment.GetEnvironmentVariable("L12_ENABLE_SECOND_APPROVER_BOOTSTRAP");
        var previousToken = Environment.GetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN");
        try
        {
            var store = new L12PlatformStore(path);
            var target = store.Register("BootstrapTarget", "password-123");
            var alternate = store.Register("BootstrapAlternate", "password-456");
            Environment.SetEnvironmentVariable("L12_ENABLE_SECOND_APPROVER_BOOTSTRAP", null);
            Environment.SetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN", null);
            Assert.Equal("bootstrap_disabled", store.BootstrapSecondApprover(target.Account!.Id, "x").Code);

            Environment.SetEnvironmentVariable("L12_ENABLE_SECOND_APPROVER_BOOTSTRAP", "true");
            Environment.SetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN", "too-short");
            Assert.Equal("bootstrap_credential_unavailable",
                store.BootstrapSecondApprover(target.Account.Id, "too-short").Code);

            var credential = new string('S', 40) + "-one-time";
            Environment.SetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN", credential);
            Assert.Equal("bootstrap_credential_invalid",
                store.BootstrapSecondApprover(target.Account.Id, new string('W', 48)).Code);

            var success = store.BootstrapSecondApprover(target.Account.Id, credential);
            Assert.True(success.Success, $"{success.Code}: {success.Message}");
            Assert.Equal("admin", store.Account(target.Account.Id)!.Role);
            Assert.Null(store.AuthenticateToken(target.Token));
            Assert.DoesNotContain(credential, JsonSerializer.Serialize(store.AdminCommands(), JsonOptions));

            var reloaded = new L12PlatformStore(path);
            var replay = reloaded.BootstrapSecondApprover(target.Account.Id, credential);
            Assert.True(replay.Success);
            Assert.True(replay.Replayed);
            Assert.Equal(success.Value!.AccountId, replay.Value!.AccountId);
            var conflict = reloaded.BootstrapSecondApprover(alternate.Account!.Id, credential);
            Assert.Equal("idempotency_conflict", conflict.Code);

            var otherRoot = TempRoot();
            try
            {
                var readyStore = new L12PlatformStore(Path.Combine(otherRoot, "platform.json"));
                var admin = readyStore.Login("Admin", "L12master").Account!;
                var existing = readyStore.Register("ExistingApprover", "password-789").Account!;
                var candidate = readyStore.Register("BootstrapCandidate", "password-000").Account!;
                Assert.True(readyStore.SetRole(admin, existing.Id, "admin"));
                Assert.Equal("second_approver_already_ready",
                    readyStore.BootstrapSecondApprover(candidate.Id, credential).Code);
            }
            finally { Directory.Delete(otherRoot, true); }
        }
        finally
        {
            Environment.SetEnvironmentVariable("L12_ENABLE_SECOND_APPROVER_BOOTSTRAP", previousEnabled);
            Environment.SetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN", previousToken);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AuditFailureClosesHighRiskSubmissionAndApprovalAndRaisesAlert()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var reviewer = Promote(store, admin, "AuditReviewer", "admin");
            store.RecordAuthorizationDenied(null, new L12AdminAuditContext("audit-failure-source",
                    "admin.audit.read", RequestMethod: "GET", RequestPath: "/api/admin/audit",
                    Outcome: "denied", Reason: "authentication-required"),
                "admin.audit.read", "authentication-required");
            AgeIndependentAuditEvents(store.TransactionalStoragePath, DateTimeOffset.UtcNow.AddDays(-60));
            var payload = store.CaptureAuditArchive(30);

            store.AuditAvailabilityProbeOverride = () => false;
            var rejected = SubmitArchive(store, admin, payload, "audit-fail-submit", store.Version);
            Assert.Equal("audit_unavailable", rejected.Code);
            Assert.DoesNotContain(store.AdminCommands(), item => item.IdempotencyKey == "audit-fail-submit");
            var status = store.SecurityStatus(admin);
            Assert.False(status.HighRiskAuditAvailable);
            Assert.Contains(status.Alerts, item => item.Code == "audit-unavailable" && item.Severity == "critical");
            Assert.False(status.Mfa.EnrollmentEnabled);
            Assert.False(status.Mfa.SecretsPersisted);

            store.AuditAvailabilityProbeOverride = () => true;
            var pending = SubmitArchive(store, admin, payload, "audit-fail-review", store.Version);
            Assert.True(pending.Pending);
            store.AuditAvailabilityProbeOverride = () => false;
            var reviewed = ReviewArchive(store, pending.Command!.Id, reviewer);
            Assert.Equal("audit_unavailable", reviewed.Code);
            Assert.Equal("requested", store.AdminApprovals().Single(item => item.CommandId == pending.Command.Id).Status);

            Assert.True(L12Authorization.HasPermission(reviewer, L12Permission.AdminSecurityRead));
            Assert.True(L12Authorization.HasPermission(reviewer, L12Permission.AdminAuditArchive));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AuditArchiveDryRunApprovalRecoveryAndTamperDetectionAreNonDestructive()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var reviewer = Promote(store, admin, "ArchiveReviewer", "admin");
            store.RecordAuthorizationDenied(null, new L12AdminAuditContext("archive-old-event",
                    "admin.audit.read", RequestMethod: "GET", RequestPath: "/api/admin/audit",
                    Outcome: "denied", Reason: "authentication-required"),
                "admin.audit.read", "authentication-required");
            AgeIndependentAuditEvents(store.TransactionalStoragePath, DateTimeOffset.UtcNow.AddDays(-60));

            var payload = store.CaptureAuditArchive(30);
            var dry = SubmitArchive(store, admin, payload, "audit-archive-dry", store.Version, true);
            Assert.True(dry.Success, $"{dry.Code}: {dry.Message}");
            Assert.False(dry.Pending);
            Assert.False(dry.Value!.Applied);
            Assert.True(dry.Value.EligibleEvents > 0);
            Assert.Empty(store.AuditArchiveSegments(admin));
            Assert.False(Directory.Exists(Path.Combine(root, "audit-archives")));

            var retainedBefore = store.StorageStatus().RetainedAuditEvents;
            var requested = SubmitArchive(store, admin, payload, "audit-archive-apply", store.Version);
            Assert.True(requested.Pending);
            Assert.Equal("self_review_forbidden", ReviewArchive(store, requested.Command!.Id, admin).Code);
            var approved = ReviewArchive(store, requested.Command.Id, reviewer);
            Assert.True(approved.Success, $"{approved.Code}: {approved.Message}");
            Assert.True(store.StorageStatus().RetainedAuditEvents >= retainedBefore);
            var segment = Assert.Single(store.AuditArchiveSegments(admin));
            Assert.True(segment.EventCount > 0);
            Assert.Equal(64, segment.Sha256.Length);

            var reloaded = new L12PlatformStore(path);
            admin = reloaded.Login("Admin", "L12master").Account!;
            Assert.Single(reloaded.AuditArchiveSegments(admin));
            var rehearsal = reloaded.RehearseAuditArchiveRecovery(admin);
            Assert.True(rehearsal.Success, rehearsal.Error);
            Assert.Equal(segment.EventCount, rehearsal.Events);

            var archivePath = Assert.Single(Directory.GetFiles(Path.Combine(root, "audit-archives"), "*.jsonl"));
            File.AppendAllText(archivePath, "tampered", Encoding.UTF8);
            var tampered = reloaded.RehearseAuditArchiveRecovery(admin);
            Assert.False(tampered.Success);
            Assert.Contains("校验和", tampered.Error);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyJsonWithoutSecurityFieldsLoadsEnabledAccountsAndKeepsMfaOff()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var account = store.Register("LegacySecurity", "password-123");
            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            document.Remove("LoginThrottles");
            document.Remove("Security");
            foreach (var item in document["Accounts"]!.AsArray().OfType<JsonObject>())
            {
                item.Remove("Disabled");
                item.Remove("DisabledAt");
                item.Remove("DisabledByAccountId");
                item.Remove("DisabledReason");
            }
            File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));

            var reloaded = new L12PlatformStore(path);
            Assert.NotNull(reloaded.AuthenticateToken(account.Token));
            Assert.False(reloaded.Account(account.Account!.Id)!.Disabled);
            var capability = reloaded.MfaCapability();
            Assert.False(capability.EnrollmentEnabled);
            Assert.False(capability.SecretsPersisted);
            Assert.DoesNotContain("secret", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    private static L12AccountView Promote(L12PlatformStore store, L12AccountView admin,
        string username, string role)
    {
        var registered = store.Register(username, "password-123").Account!;
        Assert.True(store.SetRole(admin, registered.Id, role));
        return store.Account(registered.Id)!;
    }

    private static L12AdminCommandResult<L12AuditArchiveOperationView> SubmitArchive(L12PlatformStore store,
        L12AccountView actor, L12AuditArchiveCommandPayload payload, string key, long expectedVersion,
        bool dryRun = false)
    {
        var commandId = Guid.NewGuid().ToString("N");
        var audit = new L12AdminAuditContext("archive-" + key,
            L12Authorization.Key(L12Permission.AdminAuditArchive), commandId, key, expectedVersion,
            dryRun, "retention-archive", "POST", "/api/admin/security/audit-archives");
        var command = new L12AdminCommandEnvelope<L12AuditArchiveCommandPayload>(commandId, key,
            "security.audit.archive", actor, DateTimeOffset.UtcNow, "security:audit", "retention-archive",
            dryRun, expectedVersion, payload, audit);
        return new L12AdminCommandBus(store).Execute(command, L12Permission.AdminAuditArchive,
            current => L12AdminCommandResult<L12AuditArchiveOperationView>.Ok(store.ArchiveAudit(current.Actor,
                current.Payload, current.AuditContext, true)),
            current => L12AdminCommandResult<L12AuditArchiveOperationView>.Ok(store.ArchiveAudit(current.Actor,
                current.Payload, current.AuditContext, false)), L12AdminCommandRisk.High);
    }

    private static L12AdminCommandResult<L12AdminCommandView> ReviewArchive(L12PlatformStore store,
        string commandId, L12AccountView reviewer)
        => new L12AdminCommandBus(store).Review(commandId, reviewer, new("approve", "archive-reviewed"),
            new L12AdminAuditContext("review-" + commandId,
                L12Authorization.Key(L12Permission.AdminApprovalsReview), CommandId: commandId),
            (view, requester, audit) =>
            {
                var payload = view.Payload.Deserialize<L12AuditArchiveCommandPayload>(JsonOptions)!;
                return JsonOk(store.ArchiveAudit(requester, payload, audit, true));
            });

    private static L12AdminCommandResult<JsonElement> JsonOk<T>(T value)
        => new(true, "ok", "操作成功", JsonSerializer.SerializeToElement(value, JsonOptions),
            StatusCodes.Status200OK);

    private static void AgeIndependentAuditEvents(string databasePath, DateTimeOffset start)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        var rows = new List<(string Id, string Payload)>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT id,payload_json FROM admin_audit_events ORDER BY created_utc,id;";
            using var reader = read.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var createdAt = start.AddTicks(index);
            var payload = JsonNode.Parse(rows[index].Payload)!.AsObject();
            payload["CreatedAt"] = createdAt;
            var payloadJson = payload.ToJsonString();
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)))
                .ToLowerInvariant();
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE admin_audit_events
                SET created_utc=$created,payload_json=$payload,payload_sha256=$checksum
                WHERE id=$id;
                """;
            update.Parameters.AddWithValue("$created", createdAt.ToString("O"));
            update.Parameters.AddWithValue("$payload", payloadJson);
            update.Parameters.AddWithValue("$checksum", checksum);
            update.Parameters.AddWithValue("$id", rows[index].Id);
            update.ExecuteNonQuery();
        }
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

    private static HttpRequestMessage CorrelatedLogin(string username, string password, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username, password }),
        };
        request.Headers.Add(L12CorrelationIds.HeaderName, correlationId);
        return request;
    }

    private static async Task SendWebSocketAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveWebSocketTextAsync(ClientWebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, cancellation.Token);
        return result.MessageType == WebSocketMessageType.Close
            ? "closed"
            : Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task AssertWebSocketInvalidatedAsync(ClientWebSocket socket)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await SendWebSocketAsync(socket, new { type = "ping" });
            var response = await ReceiveWebSocketTextAsync(socket, TimeSpan.FromSeconds(3));
            Assert.True(response == "closed" || response.Contains("authenticationRequired",
                StringComparison.OrdinalIgnoreCase), $"unexpected websocket response: {response}");
        }
        catch (WebSocketException) { }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-phase6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
