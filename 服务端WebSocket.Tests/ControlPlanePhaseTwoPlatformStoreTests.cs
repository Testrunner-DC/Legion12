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
public sealed class ControlPlanePhaseTwoPlatformStoreTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LowRiskCommandIdempotencyAndResultSurviveRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var supportRegistration = store.Register("PersistentSupport", "password-123");
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.True(store.SetRole(admin, supportRegistration.Account!.Id, "admin"));
            var support = store.AuthenticateToken(supportRegistration.Token)!;
            var bug = store.AddBug(support, "持久幂等", "首次更新只能执行一次", "/test", null, null, "test");
            var expectedVersion = store.Version;
            var firstCommand = BugCommand(support, bug.Id, "persist-bug-1", expectedVersion, "resolved");
            var first = new L12AdminCommandBus(store).Execute(firstCommand, L12Permission.AdminBugsWrite,
                current =>
                {
                    var updated = store.UpdateBug(current.Actor, current.Payload.Id, current.Payload.Status,
                        current.Payload.Priority, current.Payload.Assignee, current.Payload.AdminNotes,
                        current.Payload.Comment, current.AuditContext);
                    return L12AdminCommandResult<L12BugReportView>.Ok(updated!);
                });
            Assert.True(first.Success);
            Assert.Equal("resolved", first.Value!.Status);

            var reloaded = new L12PlatformStore(path);
            var reloadedSupport = reloaded.AuthenticateToken(supportRegistration.Token)!;
            var handlerCalled = false;
            var replay = new L12AdminCommandBus(reloaded).Execute<BugUpdateCommandPayload, L12BugReportView>(
                BugCommand(reloadedSupport, bug.Id, "persist-bug-1", expectedVersion, "resolved"),
                L12Permission.AdminBugsWrite, _ =>
                {
                    handlerCalled = true;
                    throw new InvalidOperationException("replay must not invoke handler");
                });

            Assert.True(replay.Success);
            Assert.True(replay.Replayed);
            Assert.False(handlerCalled);
            Assert.Equal(first.Value.Id, replay.Value!.Id);
            Assert.Equal(first.Value.Status, replay.Value.Status);
            Assert.Equal(first.Value.History.Count, replay.Value.History.Count);
            var stored = Assert.Single(reloaded.AdminCommands(type: "bug.update"));
            Assert.Equal("executed", stored.Status);
            Assert.Equal("persist-bug-1", stored.IdempotencyKey);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RoleChangeExecutesDirectlyAndReplaysAfterRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var target = store.Register("ApprovalTarget", "password-123").Account!;
            var adminLogin = store.Login("Admin", "L12master");
            var expectedVersion = target.PermissionVersion;
            var commandId = Guid.NewGuid().ToString("N");
            var command = RoleCommand(adminLogin.Account!, target.Id, "admin", "role-direct-1",
                expectedVersion, commandId);
            var bus = new L12AdminCommandBus(store);
            var applied = bus.Execute<RoleCommandPayload, RoleCommandResult>(command,
                L12Permission.AdminAccountRolesWrite,
                current => store.SetRole(current.Actor, current.Payload.AccountId, current.Payload.Role,
                        current.AuditContext)
                    ? L12AdminCommandResult<RoleCommandResult>.Ok(
                        new RoleCommandResult(current.Payload.AccountId, current.Payload.Role, true))
                    : L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "invalid", 400));

            Assert.True(applied.Success);
            Assert.False(applied.Pending);
            Assert.Equal("admin", store.Account(target.Id)!.Role);
            Assert.Empty(store.AdminApprovals());

            var reloaded = new L12PlatformStore(path);
            var replayed = new L12AdminCommandBus(reloaded).Execute<RoleCommandPayload, RoleCommandResult>(
                RoleCommand(reloaded.Account(adminLogin.Account!.Id)!, target.Id, "admin", "role-direct-1",
                    expectedVersion, Guid.NewGuid().ToString("N")), L12Permission.AdminAccountRolesWrite,
                _ => throw new InvalidOperationException("direct replay executed again"));
            Assert.True(replayed.Success);
            Assert.True(replayed.Replayed);
            Assert.Equal("admin", replayed.Value!.Role);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PermissionDenialAndIfMatchConflictAreAuditedWithoutMutation()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var player = store.Register("DeniedPlayer", "password-123").Account!;
            var bug = store.AddBug(player, "拒绝", "不应被越权更新", "/test", null, null, "test");
            var deniedCommand = BugCommand(player, bug.Id, "denied-bug-1", store.Version, "closed",
                correlationId: "denied-command-1");
            var bus = new L12AdminCommandBus(store);
            var denied = bus.Execute<BugUpdateCommandPayload, L12BugReportView>(deniedCommand,
                L12Permission.AdminBugsWrite,
                _ => throw new InvalidOperationException("permission denial executed"));
            Assert.False(denied.Success);
            Assert.Equal("permission_denied", denied.Code);
            Assert.Equal("new", store.Bugs(null).Single(item => item.Id == bug.Id).Status);
            Assert.Contains(store.AdminAudit("security"), audit => audit.CorrelationId == "denied-command-1"
                && audit.Permission == "admin.bugs.write" && audit.Outcome == "denied");

            var admin = store.Login("Admin", "L12master").Account!;
            Assert.True(store.SetRole(admin, player.Id, "admin"));
            var support = store.Account(player.Id)!;
            var staleVersion = store.Version;
            store.AddBug(support, "并发变化", "使版本过期", "/test", null, null, "test");
            var conflict = bus.Execute<BugUpdateCommandPayload, L12BugReportView>(
                BugCommand(support, bug.Id, "stale-bug-1", staleVersion, "resolved"),
                L12Permission.AdminBugsWrite, _ => throw new InvalidOperationException("stale command executed"));
            Assert.False(conflict.Success);
            Assert.Equal("version_conflict", conflict.Code);
            Assert.Equal("new", store.Bugs(null).Single(item => item.Id == bug.Id).Status);
            Assert.Contains(store.AdminCommands(status: "failed"), command => command.IdempotencyKey == "stale-bug-1"
                && command.FailureReason == "expected-version-mismatch");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ContentDryRunIsReadOnlyAndBatchPublishIsAtomicAndRollbackable()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var editorRegistration = store.Register("ContentEditor", "password-123");
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.True(store.SetRole(admin, editorRegistration.Account!.Id, "admin"));
            var editor = store.Account(editorRegistration.Account.Id)!;
            store.SetContent("home.headline", "old-headline");
            store.SetContent("rules.notice", "old-rules");
            store.SaveContentDraft(editor, "home.headline", "new-headline");
            store.SaveContentDraft(editor, "rules.notice", "new-rules");

            var dryPayload = store.CaptureContentPublish(["home.headline", "rules.notice"]);
            var versionBeforeDryRun = store.Version;
            var dryCommandId = Guid.NewGuid().ToString("N");
            var dryCommand = new L12AdminCommandEnvelope<L12ContentPublishCommandPayload>(dryCommandId,
                "content-dry-run-1", "content.publish.batch", editor, DateTimeOffset.UtcNow,
                "content-batch:dry", "preview", true, versionBeforeDryRun, dryPayload,
                new L12AdminAuditContext("content-dry-run-1", "admin.content.publish", dryCommandId,
                    "content-dry-run-1", versionBeforeDryRun, true));
            var dryResult = new L12AdminCommandBus(store).Execute(dryCommand, L12Permission.AdminContentPublish,
                current => L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(true, store.PublishContentBatch(current.Actor,
                        current.Payload, current.AuditContext), null)),
                current => L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(false, null, store.PreviewContentPublish(current.Payload))),
                L12AdminCommandRisk.High);
            Assert.True(dryResult.Success);
            Assert.False(dryResult.Pending);
            Assert.False(dryResult.Value!.Applied);
            Assert.Equal(versionBeforeDryRun, store.Version);
            Assert.Empty(store.ContentBatches());
            Assert.Equal("old-headline", store.GetContent("home.headline"));

            var stale = store.CaptureContentPublish(["home.headline", "rules.notice"]);
            store.SaveContentDraft(editor, "rules.notice", "newer-rules");
            var versionBeforeRejectedBatch = store.Version;
            Assert.Throws<L12ContentStateConflictException>(() => store.PublishContentBatch(editor, stale));
            Assert.Equal(versionBeforeRejectedBatch, store.Version);
            Assert.Equal("old-headline", store.GetContent("home.headline"));
            Assert.Equal("old-rules", store.GetContent("rules.notice"));
            Assert.Empty(store.ContentBatches());

            var publishPayload = store.CaptureContentPublish(["home.headline", "rules.notice"]);
            var versionBeforePublish = store.Version;
            var batch = store.PublishContentBatch(editor, publishPayload);
            Assert.Equal(versionBeforePublish + 1, store.Version);
            Assert.Equal("new-headline", store.GetContent("home.headline"));
            Assert.Equal("newer-rules", store.GetContent("rules.notice"));

            var rollbackPayload = store.CaptureContentRollback(batch.Id);
            var versionBeforeRollback = store.Version;
            var rollback = store.RollbackContentBatch(editor, rollbackPayload);
            Assert.Equal(versionBeforeRollback + 1, store.Version);
            Assert.Equal("rollback", rollback.Action);
            Assert.Equal(batch.Id, rollback.SourceBatchId);
            Assert.Equal("old-headline", store.GetContent("home.headline"));
            Assert.Equal("old-rules", store.GetContent("rules.notice"));
            Assert.Equal("rolled-back", store.ContentBatches().Single(item => item.Id == batch.Id).Status);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyPlatformJsonLoadsAndGainsPhaseTwoCollectionsOnNextCommand()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var editorRegistration = store.Register("LegacyEditor", "password-123");
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.True(store.SetRole(admin, editorRegistration.Account!.Id, "admin"));
            var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            foreach (var name in new[] { "BusinessVersion", "AdminCommands", "AdminApprovals", "ContentVersions", "ContentBatches" })
                legacy.Remove(name);
            foreach (var entry in legacy["ContentEntries"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                entry.Remove("Version");
                entry.Remove("PublishedVersionId");
                entry.Remove("RollbackVersionId");
            }
            File.WriteAllText(path, legacy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var reloaded = new L12PlatformStore(path);
            var editor = reloaded.AuthenticateToken(editorRegistration.Token)!;
            var expected = reloaded.Version;
            var commandId = Guid.NewGuid().ToString("N");
            var command = new L12AdminCommandEnvelope<ContentDraftCommandPayload>(commandId, "legacy-draft-1",
                "content.draft.save", editor, DateTimeOffset.UtcNow, "content:home.headline", null, false,
                expected, new ContentDraftCommandPayload("home.headline", "legacy-compatible"),
                new L12AdminAuditContext("legacy-command-1", "admin.content.draft", commandId,
                    "legacy-draft-1", expected));
            var result = new L12AdminCommandBus(reloaded).Execute(command, L12Permission.AdminContentDraft,
                current => L12AdminCommandResult<L12ContentEntryView>.Ok(reloaded.SaveContentDraft(current.Actor,
                    current.Payload.Key, current.Payload.Value, current.AuditContext)));
            Assert.True(result.Success);

            var upgraded = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.NotNull(upgraded["BusinessVersion"]);
            Assert.Single(upgraded["AdminCommands"]!.AsArray());
            Assert.NotNull(upgraded["AdminApprovals"]);
            Assert.Equal("legacy-compatible", reloaded.GetContentEntry("home.headline").DraftValue);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task V1AndLegacyRoutesUseDirectContentCommandsAndAuditContracts()
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
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            var admin = store.Login("Admin", "L12master");
            var editorRegistration = store.Register("HttpPhaseTwoEditor", "password-123");
            var reviewerRegistration = store.Register("HttpPhaseTwoReviewer", "password-456");
            Assert.True(store.SetRole(admin.Account!, editorRegistration.Account!.Id, "admin"));
            Assert.True(store.SetRole(admin.Account!, reviewerRegistration.Account!.Id, "admin"));

            using (var draft = Authorized(HttpMethod.Put, "/api/admin/v1/content/home.headline/draft",
                       editorRegistration.Token!, "v1-draft-1", new { value = "approved-headline" },
                       "v1-draft-idem-1", store.Version))
            using (var response = await client.SendAsync(draft))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var versionForPublish = store.Version;
            using (var publish = Authorized(HttpMethod.Post, "/api/admin/v1/content/publish",
                       editorRegistration.Token!, "v1-publish-1",
                       new { keys = new[] { "home.headline" }, idempotencyKey = "v1-publish-idem-1", expectedVersion = versionForPublish }))
            using (var response = await client.SendAsync(publish))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var publishCommand = Assert.Single(store.AdminCommands(type: "content.publish.batch"),
                item => item.IdempotencyKey == "v1-publish-idem-1");
            var publishCommandId = publishCommand.Id;
            Assert.Equal("executed", publishCommand.Status);
            Assert.Equal("approved-headline", store.GetContent("home.headline"));
            Assert.DoesNotContain(store.AdminApprovals(status: null), item => item.CommandId == publishCommandId);

            using (var legacyApproval = Authorized(HttpMethod.Post,
                       $"/api/admin/v1/approvals/{publishCommandId}", reviewerRegistration.Token!,
                       "v1-content-approval-disabled-1", new { decision = "approve" }))
            using (var response = await client.SendAsync(legacyApproval))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("content_approval_disabled", error!.Code);
            }

            var publishBatch = Assert.Single(store.ContentBatches(), item => item.Action == "publish");
            var batchCountAfterPublish = store.ContentBatches().Count;
            using (var replayPublish = Authorized(HttpMethod.Post, "/api/admin/v1/content/publish",
                       editorRegistration.Token!, "v1-publish-replay-1",
                       new { keys = new[] { "home.headline" }, idempotencyKey = "v1-publish-idem-1", expectedVersion = versionForPublish }))
            using (var response = await client.SendAsync(replayPublish))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-Idempotent-Replay")));
            }
            Assert.Equal(batchCountAfterPublish, store.ContentBatches().Count);

            var versionForRollback = store.Version;
            using (var rollback = Authorized(HttpMethod.Post, "/api/admin/v1/content/rollback",
                       editorRegistration.Token!, "v1-rollback-1",
                       new { batchId = publishBatch.Id, idempotencyKey = "v1-rollback-idem-1", expectedVersion = versionForRollback }))
            using (var response = await client.SendAsync(rollback))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rollbackCommand = Assert.Single(store.AdminCommands(type: "content.rollback.batch"),
                item => item.IdempotencyKey == "v1-rollback-idem-1");
            Assert.Equal("executed", rollbackCommand.Status);
            Assert.DoesNotContain(store.AdminApprovals(status: null), item => item.CommandId == rollbackCommand.Id);
            Assert.Equal("rolled-back", store.ContentBatches().Single(item => item.Id == publishBatch.Id).Status);

            var batchCountAfterRollback = store.ContentBatches().Count;
            using (var replayRollback = Authorized(HttpMethod.Post, "/api/admin/v1/content/rollback",
                       editorRegistration.Token!, "v1-rollback-replay-1",
                       new { batchId = publishBatch.Id, idempotencyKey = "v1-rollback-idem-1", expectedVersion = versionForRollback }))
            using (var response = await client.SendAsync(replayRollback))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-Idempotent-Replay")));
            }
            Assert.Equal(batchCountAfterRollback, store.ContentBatches().Count);

            using (var commands = Authorized(HttpMethod.Get, "/api/admin/v1/commands?status=executed",
                       reviewerRegistration.Token!, "v1-command-list-1"))
            using (var response = await client.SendAsync(commands))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var items = await response.Content.ReadFromJsonAsync<L12AdminCommandView[]>();
                Assert.Contains(items!, item => item.Id == publishCommandId && item.Result is not null);
            }

            var bug = store.AddBug(admin.Account, "旧路由", "仍必须进入命令总线", "/legacy", null, null, "test");
            using (var update = Authorized(HttpMethod.Patch, $"/api/admin/bugs/{bug.Id}", admin.Token!,
                       "legacy-bug-command-1", new { status = "resolved" }, "legacy-bug-idem-1", store.Version))
            using (var response = await client.SendAsync(update))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(store.AdminCommands(type: "bug.update"), item => item.IdempotencyKey == "legacy-bug-idem-1");

            var effect = catalog.AtomicEffects.All.First();
            using (var review = Authorized(HttpMethod.Put,
                       $"/api/admin/v1/effects/{effect.CardId}/review", editorRegistration.Token!,
                       "v1-effect-review-1", new { status = "human-assisted", note = "manual check" },
                       "v1-effect-idem-1", store.Version))
            using (var response = await client.SendAsync(review))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(store.AdminCommands(type: "effect.review.save"),
                item => item.IdempotencyKey == "v1-effect-idem-1" && item.Status == "executed");

            store.SaveContentDraft(admin.Account!, "rules.notice", "legacy-publish-pending");
            using (var legacyPublish = Authorized(HttpMethod.Post, "/api/admin/content/rules.notice/publish",
                       admin.Token!, "legacy-publish-1", null, "legacy-publish-idem-1", store.Version))
            using (var response = await client.SendAsync(legacyPublish))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("legacy-publish-pending", store.GetContent("rules.notice"));

            using (var auditRequest = Authorized(HttpMethod.Get,
                       $"/api/admin/v1/audit?commandId={publishCommandId}", admin.Token!, "v1-audit-filter-1"))
            using (var response = await client.SendAsync(auditRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var audit = await response.Content.ReadFromJsonAsync<L12AdminAuditView[]>();
                Assert.Contains(audit!, item => item.CommandId == publishCommandId && item.Category == "content");
                Assert.DoesNotContain(audit!, item => item.CommandId == publishCommandId && item.Category == "approval");
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

    private static L12AdminCommandEnvelope<BugUpdateCommandPayload> BugCommand(L12AccountView actor,
        string bugId, string key, long version, string status, string correlationId = "bug-command-1")
    {
        var commandId = Guid.NewGuid().ToString("N");
        return new L12AdminCommandEnvelope<BugUpdateCommandPayload>(commandId, key, "bug.update", actor,
            DateTimeOffset.UtcNow, $"bug:{bugId}", null, false, version,
            new BugUpdateCommandPayload(bugId, status, null, null, null, "test"),
            new L12AdminAuditContext(correlationId, "admin.bugs.write", commandId, key, version));
    }

    private static L12AdminCommandEnvelope<RoleCommandPayload> RoleCommand(L12AccountView actor, string accountId,
        string role, string key, long version, string commandId)
        => new(commandId, key, "account.role.set", actor, DateTimeOffset.UtcNow, $"account:{accountId}",
            "role change", false, version, new RoleCommandPayload(accountId, role),
            new L12AdminAuditContext("role-command-1", "admin.accounts.roles.write", commandId, key, version));

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token,
        string correlationId, object? body = null, string? idempotencyKey = null, long? expectedVersion = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, correlationId);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (expectedVersion is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-control-plane-phase-two-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
