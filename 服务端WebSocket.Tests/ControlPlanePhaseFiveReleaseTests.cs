using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlanePhaseFiveReleaseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ArtifactCatalogAndRuntimeAreOnlyTrustedSanitizedAdapterSnapshots()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var manager = Promote(store, admin, "RlsMgr01", "admin");
            var adapter = new FakeReleaseAdapter
            {
                Artifacts =
                [
                    Artifact("release-safe", 'a', '1', "production"),
                    Artifact("../unsafe-path", 'b', '2', "production"),
                ],
                Observations =
                [
                    new("production", true, "healthy", "release-safe", new string('a', 40),
                        new(true, "health.ok", 12), new(true, "ws.ok", 8), DateTimeOffset.UtcNow),
                    new("staging", true, "secret-token", "../secret", "not-a-commit",
                        new(false, "token=do-not-return", -1), new(false, "ws bad", 999_999),
                        DateTimeOffset.UtcNow),
                ],
            };

            var artifacts = store.ReleaseArtifacts(manager, adapter);
            var artifact = Assert.Single(artifacts);
            Assert.Equal("release-safe", artifact.Id);
            Assert.DoesNotContain("../", JsonSerializer.Serialize(artifacts, JsonOptions));

            var environments = store.ReleaseEnvironments(manager, adapter);
            var production = Assert.Single(environments, item => item.Environment == "production");
            Assert.True(production.AdapterConfigured);
            Assert.Equal("release-safe", production.ActiveArtifactId);
            Assert.Equal("health.ok", production.Health.Code);
            var staging = Assert.Single(environments, item => item.Environment == "staging");
            Assert.Equal("idle", staging.State);
            Assert.Null(staging.ActiveArtifactId);
            Assert.Equal("adapter-invalid-code", staging.Health.Code);
            Assert.Equal(300_000, staging.WebSocket.DurationMs);
            Assert.DoesNotContain("do-not-return", JsonSerializer.Serialize(environments, JsonOptions));

            var disabled = new L12DisabledReleaseControlAdapter();
            Assert.Empty(store.ReleaseArtifacts(manager, disabled));
            Assert.All(store.ReleaseEnvironments(manager, disabled), item =>
            {
                Assert.False(item.AdapterConfigured);
                Assert.Equal("unconfigured", item.State);
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DryRunVerifiesHashButNeverExecutesOrMutatesEnvironment()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var manager = Promote(store, admin, "RelDryRunMgr", "admin");
            var adapter = new FakeReleaseAdapter { Artifacts = [Artifact("release-dry", 'a', '1', "production")] };
            var payload = store.CaptureReleaseDeploy(manager, "release-dry", "production", adapter);

            var outcome = Submit(store, adapter, manager, payload, 0, "release-dry-run", true);

            Assert.True(outcome.Success, $"{outcome.Code}: {outcome.Message}");
            Assert.False(outcome.Pending);
            Assert.False(outcome.Value!.Applied);
            Assert.False(outcome.Value.Plan.WillExecute);
            Assert.Equal(0, adapter.ExecuteCount);
            Assert.True(adapter.HashVerificationCount >= 2);
            Assert.Equal(0, store.ReleaseEnvironmentVersion("production"));
            Assert.Empty(store.ReleaseRuns(manager));
            Assert.Contains(store.AdminCommands(type: "release.deploy"), item =>
                item.IdempotencyKey == "release-dry-run" && item.DryRun && item.Status == "executed");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ApprovalAndIdempotentResultSurviveRestartWithoutSelfReviewOrReexecution()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var requester = Promote(store, admin, "ReleaseRequester", "admin");
            var reviewer = Promote(store, admin, "ReleaseReviewer", "admin");
            var adapter = new FakeReleaseAdapter { Artifacts = [Artifact("release-restart", 'a', '1', "production")] };
            var payload = store.CaptureReleaseDeploy(requester, "release-restart", "production", adapter);
            var requested = Submit(store, adapter, requester, payload, 0, "release-restart-key");

            Assert.True(requested.Pending);
            Assert.Equal(0, adapter.ExecuteCount);
            var commandId = requested.Command!.Id;
            var selfReview = Review(store, adapter, commandId, requester);
            Assert.Equal("self_review_forbidden", selfReview.Code);
            Assert.Equal(0, adapter.ExecuteCount);

            var reloaded = new L12PlatformStore(path);
            requester = reloaded.Account(requester.Id)!;
            reviewer = reloaded.Account(reviewer.Id)!;
            var approved = Review(reloaded, adapter, commandId, reviewer);

            Assert.True(approved.Success, $"{approved.Code}: {approved.Message}; {approved.Command?.FailureReason}");
            Assert.Equal(1, adapter.ExecuteCount);
            var run = Assert.Single(reloaded.ReleaseRuns(requester));
            Assert.Equal("succeeded", run.Status);
            Assert.Contains(run.Checks, item => item.Kind == "artifact-hash" && item.Success);
            Assert.Contains(run.Checks, item => item.Kind == "health" && item.Success);
            Assert.Contains(run.Checks, item => item.Kind == "websocket-smoke" && item.Success);

            var restarted = new L12PlatformStore(path);
            requester = restarted.Account(requester.Id)!;
            var replay = Submit(restarted, adapter, requester, payload, 0, "release-restart-key");

            Assert.True(replay.Success, $"{replay.Code}: {replay.Message}");
            Assert.True(replay.Replayed);
            Assert.Equal(1, adapter.ExecuteCount);
            Assert.Single(restarted.ReleaseRuns(requester));
            Assert.Contains(restarted.AdminAudit("security"), item => item.CommandId == commandId
                && item.Reason == "self-review-forbidden");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EnvironmentVersionAndApprovalTimeHashCheckBlockStaleExecution()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var requester = Promote(store, admin, "RelConcReq", "admin");
            var reviewer = Promote(store, admin, "RelConcRev", "admin");
            var adapter = new FakeReleaseAdapter
            {
                Artifacts =
                [
                    Artifact("release-concurrent-a", 'a', '1', "production"),
                    Artifact("release-concurrent-b", 'b', '2', "production"),
                    Artifact("release-hash-change", 'c', '3', "staging"),
                ],
            };
            var first = Submit(store, adapter, requester,
                store.CaptureReleaseDeploy(requester, "release-concurrent-a", "production", adapter),
                0, "release-concurrent-1");
            var stale = Submit(store, adapter, requester,
                store.CaptureReleaseDeploy(requester, "release-concurrent-b", "production", adapter),
                0, "release-concurrent-2");

            Assert.True(Review(store, adapter, first.Command!.Id, reviewer).Success);
            var staleReview = Review(store, adapter, stale.Command!.Id, reviewer);
            Assert.Equal("version_conflict", staleReview.Code);
            Assert.Equal(1, adapter.ExecuteCount);

            var hashPending = Submit(store, adapter, requester,
                store.CaptureReleaseDeploy(requester, "release-hash-change", "staging", adapter),
                0, "release-hash-change-key");
            adapter.HashValidity["release-hash-change"] = false;
            var hashReview = Review(store, adapter, hashPending.Command!.Id, reviewer);

            Assert.Equal("artifact_hash_mismatch", hashReview.Code);
            Assert.Equal(1, adapter.ExecuteCount);
            Assert.Equal(0, store.ReleaseEnvironmentVersion("staging"));
            Assert.DoesNotContain(store.ReleaseRuns(requester), item => item.Environment == "staging");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedHealthAndWebSocketSmokePersistARecoverableRollbackRecord()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var requester = Promote(store, admin, "RelFailReq", "admin");
            var reviewer = Promote(store, admin, "RelFailRev", "admin");
            var adapter = new FakeReleaseAdapter { Artifacts = [Artifact("release-failure", 'a', '1', "production")] };
            adapter.ExecutionResults.Enqueue(new(true, new(false, "health.failed", 25),
                new(false, "ws.failed", 30), true, true, "smoke-failed"));
            var requested = Submit(store, adapter, requester,
                store.CaptureReleaseDeploy(requester, "release-failure", "production", adapter),
                0, "release-failure-key");

            var reviewed = Review(store, adapter, requested.Command!.Id, reviewer);

            Assert.False(reviewed.Success);
            Assert.Equal("release_validation_failed", reviewed.Code);
            var run = Assert.Single(store.ReleaseRuns(requester));
            Assert.Equal("rolled-back", run.Status);
            Assert.True(run.RollbackAttempted);
            Assert.True(run.RollbackSucceeded);
            Assert.Contains(run.Checks, item => item.Kind == "health" && !item.Success);
            Assert.Contains(run.Checks, item => item.Kind == "websocket-smoke" && !item.Success);
            Assert.Contains(run.Checks, item => item.Kind == "rollback" && item.Success);
            Assert.Equal(1, store.ReleaseEnvironmentVersion("production"));

            var reloaded = new L12PlatformStore(path);
            requester = reloaded.Account(requester.Id)!;
            Assert.Equal("rolled-back", Assert.Single(reloaded.ReleaseRuns(requester)).Status);
            Assert.Equal("unconfigured", Assert.Single(reloaded.ReleaseEnvironments(requester,
                new L12DisabledReleaseControlAdapter()), item => item.Environment == "production").State);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SuccessfulRollbackUsesHistoricalVerifiedSnapshotAndCreatesItsOwnRun()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var requester = Promote(store, admin, "RelRollbackReq", "admin");
            var reviewer = Promote(store, admin, "RelRollbackRev", "admin");
            var adapter = new FakeReleaseAdapter
            {
                Artifacts =
                [
                    Artifact("release-rollback-a", 'a', '1', "production"),
                    Artifact("release-rollback-b", 'b', '2', "production"),
                ],
            };
            var first = Submit(store, adapter, requester,
                store.CaptureReleaseDeploy(requester, "release-rollback-a", "production", adapter),
                0, "release-rollback-deploy-a");
            Assert.True(Review(store, adapter, first.Command!.Id, reviewer).Success);
            var firstRun = Assert.Single(store.ReleaseRuns(requester));
            var second = Submit(store, adapter, requester,
                store.CaptureReleaseDeploy(requester, "release-rollback-b", "production", adapter),
                1, "release-rollback-deploy-b");
            Assert.True(Review(store, adapter, second.Command!.Id, reviewer).Success);

            var rollbackPayload = store.CaptureReleaseRollback(requester, firstRun.Id, adapter);
            var rollback = Submit(store, adapter, requester, rollbackPayload, 2, "release-rollback-command");
            Assert.True(Review(store, adapter, rollback.Command!.Id, reviewer).Success);

            var rollbackRun = store.ReleaseRuns(requester).Single(item => item.Action == "rollback");
            Assert.Equal(firstRun.Id, rollbackRun.RollbackTargetRunId);
            Assert.Equal("release-rollback-a", rollbackRun.ArtifactId);
            Assert.Equal("succeeded", rollbackRun.Status);
            var environment = Assert.Single(store.ReleaseEnvironments(requester,
                new L12DisabledReleaseControlAdapter()), item => item.Environment == "production");
            Assert.Equal("release-rollback-a", environment.ActiveArtifactId);
            Assert.Equal(3, environment.Version);
            Assert.Equal(3, adapter.ExecuteCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpContractRejectsRegistrationAndUnauthorizedScopeWhileSupportingSafeReplay()
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
            var requesterRegistration = store.Register("HttpReleaseRequester", "password-123");
            var reviewerRegistration = store.Register("HttpReleaseReviewer", "password-456");
            var playerRegistration = store.Register("HttpReleasePlayer", "password-789");
            Assert.True(store.SetRole(admin.Account!, requesterRegistration.Account!.Id, "admin"));
            Assert.True(store.SetRole(admin.Account!, reviewerRegistration.Account!.Id, "admin"));
            var adapter = new FakeReleaseAdapter
            {
                Artifacts =
                [
                    Artifact("release-http", 'a', '1', "production"),
                    Artifact("release-staging-only", 'b', '2', "staging"),
                ],
                Observations =
                [
                    new("production", true, "healthy", null, null,
                        new(true, "health.ok", 3), new(true, "ws.ok", 4), DateTimeOffset.UtcNow),
                ],
            };
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog, adapter);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            using (var denied = Authorized(HttpMethod.Get, "/api/admin/v1/releases/artifacts",
                       playerRegistration.Token!, "release-http-denied"))
            using (var response = await client.SendAsync(denied))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var list = Authorized(HttpMethod.Get, "/api/admin/v1/releases/artifacts",
                       requesterRegistration.Token!, "release-http-artifacts"))
            using (var response = await client.SendAsync(list))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var json = await response.Content.ReadAsStringAsync();
                Assert.Contains("release-http", json);
                Assert.DoesNotContain("\"verified\"", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("command", json, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("release-http-artifacts",
                    Assert.Single(response.Headers.GetValues(L12CorrelationIds.HeaderName)));
            }

            using (var registration = Authorized(HttpMethod.Post, "/api/admin/v1/releases/artifacts",
                       requesterRegistration.Token!, "release-http-register",
                       new { id = "client-artifact", verified = true, path = "C:\\secret" }))
            using (var response = await client.SendAsync(registration))
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

            using (var runtime = Authorized(HttpMethod.Get, "/api/admin/v1/releases/environments",
                       requesterRegistration.Token!, "release-http-runtime"))
            using (var response = await client.SendAsync(runtime))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var environments = await response.Content.ReadFromJsonAsync<L12ReleaseEnvironmentView[]>();
                Assert.True(Assert.Single(environments!, item => item.Environment == "production").AdapterConfigured);
            }

            var dryBody = new { artifactId = "release-http", environment = "production",
                idempotencyKey = "release-http-dry", expectedVersion = 0, dryRun = true, reason = "preview" };
            using (var dry = Authorized(HttpMethod.Post, "/api/admin/v1/releases/deploy",
                       requesterRegistration.Token!, "release-http-dry", dryBody))
            using (var response = await client.SendAsync(dry))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var operation = await response.Content.ReadFromJsonAsync<L12ReleaseOperationView>();
                Assert.False(operation!.Applied);
            }
            Assert.Equal(0, adapter.ExecuteCount);
            Assert.Empty(store.ReleaseRuns(store.Account(requesterRegistration.Account.Id)!));

            var deployBody = new { artifactId = "release-http", environment = "production",
                idempotencyKey = "release-http-deploy", expectedVersion = 0, dryRun = false, reason = "release" };
            string commandId;
            using (var deploy = Authorized(HttpMethod.Post, "/api/admin/v1/releases/deploy",
                       requesterRegistration.Token!, "release-http-request", deployBody))
            using (var response = await client.SendAsync(deploy))
            {
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                commandId = (await response.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("commandId").GetString()!;
            }
            Assert.Equal(0, adapter.ExecuteCount);

            using (var self = Authorized(HttpMethod.Post, $"/api/admin/v1/approvals/{commandId}",
                       requesterRegistration.Token!, "release-http-self-review",
                       new { decision = "approve", reason = "must fail" }))
            using (var response = await client.SendAsync(self))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, adapter.ExecuteCount);

            using (var approval = Authorized(HttpMethod.Post, $"/api/admin/v1/approvals/{commandId}",
                       reviewerRegistration.Token!, "release-http-approve",
                       new { decision = "approve", reason = "verified" }))
            using (var response = await client.SendAsync(approval))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, adapter.ExecuteCount);

            using (var replay = Authorized(HttpMethod.Post, "/api/admin/v1/releases/deploy",
                       requesterRegistration.Token!, "release-http-replay", deployBody))
            using (var response = await client.SendAsync(replay))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-Idempotent-Replay")));
            }
            Assert.Equal(1, adapter.ExecuteCount);

            using (var conflict = Authorized(HttpMethod.Post, "/api/admin/v1/releases/deploy",
                       requesterRegistration.Token!, "release-http-idempotency-conflict",
                       new { artifactId = "release-staging-only", environment = "staging",
                           idempotencyKey = "release-http-deploy", expectedVersion = 0, dryRun = false }))
            using (var response = await client.SendAsync(conflict))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("idempotency_conflict", (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }

            using (var scopeDenied = Authorized(HttpMethod.Post, "/api/admin/v1/releases/deploy",
                       requesterRegistration.Token!, "release-http-scope-denied",
                       new { artifactId = "release-staging-only", environment = "production",
                           idempotencyKey = "release-http-scope", expectedVersion = 1, dryRun = true }))
            using (var response = await client.SendAsync(scopeDenied))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var runs = Authorized(HttpMethod.Get, "/api/admin/v1/releases/runs?environment=production",
                       requesterRegistration.Token!, "release-http-runs"))
            using (var response = await client.SendAsync(runs))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var items = await response.Content.ReadFromJsonAsync<L12ReleaseRunView[]>();
                Assert.Single(items!);
                Assert.Equal("succeeded", items![0].Status);
            }

            Assert.Contains(store.AdminAudit("security"), item => item.CorrelationId == "release-http-denied"
                && item.Reason == "permission-denied");
            Assert.Contains(store.AdminAudit("security"), item => item.CorrelationId == "release-http-self-review"
                && item.Reason == "self-review-forbidden");
            Assert.Contains(store.AdminAudit("security"), item => item.CorrelationId == "release-http-scope-denied"
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

    private static L12AdminCommandResult<L12ReleaseOperationView> Submit(L12PlatformStore store,
        IL12ReleaseControlAdapter adapter, L12AccountView actor, L12ReleaseCommandPayload payload,
        long expectedVersion, string idempotencyKey, bool dryRun = false)
    {
        var command = Envelope(actor, payload, expectedVersion, idempotencyKey, dryRun);
        return new L12AdminCommandBus(store).Execute(command, L12Permission.ReleasesExecute,
            current => ExecuteRelease(store, adapter, current.Actor, current.Payload,
                current.ExpectedVersion!.Value, current.AuditContext, true),
            current => ExecuteRelease(store, adapter, current.Actor, current.Payload,
                current.ExpectedVersion!.Value, current.AuditContext, false), L12AdminCommandRisk.High);
    }

    private static L12AdminCommandResult<L12AdminCommandView> Review(L12PlatformStore store,
        IL12ReleaseControlAdapter adapter, string commandId, L12AccountView reviewer)
        => new L12AdminCommandBus(store).Review(commandId, reviewer, new("approve", "verified by second operator"),
            Context($"review-{commandId}") with { CommandId = commandId },
            (command, requester, audit) =>
            {
                var payload = command.Payload.Deserialize<L12ReleaseCommandPayload>(JsonOptions)!;
                var outcome = ExecuteRelease(store, adapter, requester, payload,
                    command.ExpectedVersion!.Value, audit, true);
                return outcome.Success
                    ? L12AdminCommandResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(outcome.Value, JsonOptions))
                    : L12AdminCommandResult<JsonElement>.Fail(outcome.Code, outcome.Message, outcome.StatusCode);
            }, L12Permission.ReleaseApprovalsReview, L12PlatformStore.CanReviewReleaseCommand);

    private static L12AdminCommandResult<L12ReleaseOperationView> ExecuteRelease(L12PlatformStore store,
        IL12ReleaseControlAdapter adapter, L12AccountView actor, L12ReleaseCommandPayload payload,
        long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        try
        {
            var operation = apply
                ? store.ExecuteRelease(actor, payload, expectedVersion, adapter, context)
                : store.PlanRelease(actor, payload, expectedVersion, adapter);
            return apply && operation.Run?.Status != "succeeded"
                ? L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_validation_failed",
                    "发布验证失败", StatusCodes.Status502BadGateway)
                : L12AdminCommandResult<L12ReleaseOperationView>.Ok(operation);
        }
        catch (L12ReleaseVersionConflictException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_version_conflict",
                error.Message, StatusCodes.Status409Conflict);
        }
        catch (L12ReleaseArtifactException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail(error.Code.Replace('-', '_'),
                error.Message, StatusCodes.Status409Conflict);
        }
        catch (L12ReleaseScopeException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_scope_denied",
                error.Message, StatusCodes.Status403Forbidden);
        }
    }

    private static L12AdminCommandEnvelope<L12ReleaseCommandPayload> Envelope(L12AccountView actor,
        L12ReleaseCommandPayload payload, long expectedVersion, string idempotencyKey, bool dryRun)
    {
        var commandId = Guid.NewGuid().ToString("N");
        return new(commandId, idempotencyKey, $"release.{payload.Action}", actor, DateTimeOffset.UtcNow,
            $"release:{payload.Environment}", "test", dryRun, expectedVersion, payload,
            Context(idempotencyKey) with { CommandId = commandId, IdempotencyKey = idempotencyKey,
                ExpectedVersion = expectedVersion, DryRun = dryRun });
    }

    private static L12VerifiedReleaseArtifactView Artifact(string id, char commit, char releaseHash,
        params string[] environments)
        => new(id, new string(commit, 40), new string(releaseHash, 64), new string('c', 40),
            new string('d', 64), DateTimeOffset.UtcNow, ["backend-tests", "ui-contracts", "release-build"],
            environments);

    private static L12AccountView Promote(L12PlatformStore store, L12AccountView admin, string username, string role)
    {
        var registration = store.Register(username, "password-123");
        Assert.True(registration.Success, registration.Message);
        var account = registration.Account!;
        Assert.True(store.SetRole(admin, account.Id, role));
        return store.Account(account.Id)!;
    }

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "releases.execute", RequestMethod: "TEST", RequestPath: "/test/releases");

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
        var path = Path.Combine(Path.GetTempPath(), $"l12-control-plane-phase-five-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeReleaseAdapter : IL12ReleaseControlAdapter
    {
        public List<L12VerifiedReleaseArtifactView> Artifacts { get; set; } = [];
        public List<L12ReleaseRuntimeObservation> Observations { get; set; } = [];
        public Dictionary<string, bool> HashValidity { get; } = new(StringComparer.Ordinal);
        public Queue<L12ReleaseAdapterExecutionResult> ExecutionResults { get; } = new();
        public List<L12ReleaseAdapterRequest> Requests { get; } = [];
        public int HashVerificationCount { get; private set; }
        public int ExecuteCount => Requests.Count;

        public IReadOnlyList<L12VerifiedReleaseArtifactView> VerifiedArtifacts => Artifacts.ToArray();

        public bool VerifyArtifactHash(string artifactId)
        {
            HashVerificationCount++;
            return HashValidity.TryGetValue(artifactId, out var valid) ? valid
                : Artifacts.Any(item => item.Id == artifactId);
        }

        public L12ReleaseAdapterExecutionResult Execute(L12ReleaseAdapterRequest request)
        {
            Requests.Add(request);
            return ExecutionResults.Count > 0 ? ExecutionResults.Dequeue()
                : new(true, new(true, "health.ok", 10), new(true, "ws.ok", 10),
                    false, false, "release-succeeded");
        }

        public IReadOnlyList<L12ReleaseRuntimeObservation> ObserveRuntime() => Observations.ToArray();
    }
}
