using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class BugClosureDispositionTests
{
    [Fact]
    public void StoreEnforcesClosureClassificationBeforeMutationAndPersistsServerVerification()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var primary = store.AddBug(admin, "主问题", "用于重复关联", "/test", null, null, "test");
            var candidate = store.AddBug(admin, "待关闭", "验证分类约束", "/test", null, null, "test");

            var versionBeforeRejectedClosures = store.Version;
            AssertClosureError("bug_closure_disposition_required", () =>
                store.UpdateBug(admin, candidate.Id, "closed", null, null, null));
            AssertClosureError("bug_closure_evidence_required", () =>
                store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                    fixCommit: "abc1234", closureDisposition: "fixed_verified"));
            AssertClosureError("bug_duplicate_target_invalid", () =>
                store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                    duplicateOf: candidate.Id, closureDisposition: "duplicate"));
            AssertClosureError("bug_duplicate_target_invalid", () =>
                store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                    duplicateOf: "BUG-MISSING", closureDisposition: "duplicate"));
            AssertClosureError("bug_closure_reason_required", () =>
                store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                    closureDisposition: "rejected"));
            AssertClosureError("bug_closure_disposition_without_closure", () =>
                store.UpdateBug(admin, candidate.Id, "retest", null, null, null,
                    closureDisposition: "fixed_verified"));
            Assert.Equal("new", store.Bugs(null).Single(item => item.Id == candidate.Id).Status);
            Assert.Equal(versionBeforeRejectedClosures, store.Version);

            var before = DateTimeOffset.UtcNow;
            var fixedReport = store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                fixCommit: "abc1234", regressionTest: "NamedRegression", deployedVersion: "feed1234",
                closureDisposition: "fixed_verified")!;
            Assert.Equal("fixed_verified", fixedReport.ClosureDisposition);
            Assert.Equal(admin.Username, fixedReport.VerifiedBy);
            Assert.InRange(fixedReport.VerifiedAt!.Value, before, DateTimeOffset.UtcNow);
            Assert.Contains(fixedReport.History, item => item.Action == "closure-disposition"
                && item.ToValue == "fixed_verified");
            Assert.Contains(fixedReport.History, item => item.Action == "verified-by"
                && item.ToValue == admin.Username);
            Assert.Contains(fixedReport.History, item => item.Action == "verified-at");

            var reopened = store.UpdateBug(admin, candidate.Id, "retest", null, null, null)!;
            Assert.Null(reopened.ClosureDisposition);
            Assert.Null(reopened.VerifiedBy);
            Assert.Null(reopened.VerifiedAt);
            var duplicate = store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                duplicateOf: primary.Id, closureDisposition: "duplicate")!;
            Assert.Equal("duplicate", duplicate.ClosureDisposition);
            Assert.Equal(primary.Id, duplicate.DuplicateOf);
            Assert.Null(duplicate.VerifiedBy);

            store.UpdateBug(admin, candidate.Id, "new", null, null, null);
            var rejected = store.UpdateBug(admin, candidate.Id, "closed", null, null, null,
                "复现步骤不足，无法确认问题", duplicateOf: string.Empty, closureDisposition: "rejected")!;
            Assert.Equal("rejected", rejected.ClosureDisposition);
            Assert.Contains(rejected.History, item => item.Action == "comment"
                && item.Comment == "复现步骤不足，无法确认问题");
            Assert.Null(rejected.VerifiedBy);
            Assert.Null(rejected.VerifiedAt);

            var reloaded = new L12PlatformStore(path);
            var persisted = reloaded.Bugs(null).Single(item => item.Id == candidate.Id);
            Assert.Equal("rejected", persisted.ClosureDisposition);
            Assert.Equal("closed", persisted.Status);

            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var legacyRow = document["BugReports"]!.AsArray().OfType<JsonObject>()
                .Single(item => item["Id"]!.GetValue<string>() == candidate.Id);
            legacyRow.Remove("ClosureDisposition");
            File.WriteAllText(path, document.ToJsonString());
            var legacyStore = new L12PlatformStore(path);
            var legacyAdmin = legacyStore.Login("Admin", "L12master").Account!;
            var legacyUpdated = legacyStore.UpdateBug(legacyAdmin, candidate.Id, "closed", "high", null, null)!;
            Assert.Null(legacyUpdated.ClosureDisposition);
            Assert.Equal("high", legacyUpdated.Priority);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpClosureRejectsForgeryMissingEvidenceUnauthorizedAndStaleRequests()
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
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("tbugc12345", "password-123");
            var primary = store.AddBug(player.Account, "主问题", "重复关联目标", "/test", null, null, "test");
            var candidate = store.AddBug(player.Account, "关闭候选", "HTTP 分类验证", "/test", null, null, "test");

            using (var request = Authorized(HttpMethod.Patch, $"/api/admin/bugs/{candidate.Id}", player.Token!,
                       new { status = "closed", closureDisposition = "rejected", comment = "无效反馈" }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("new", store.Bugs(null).Single(item => item.Id == candidate.Id).Status);

            await AssertApiError(client, admin.Token!, candidate.Id,
                new { status = "closed", closureDisposition = "fixed_verified", fixCommit = "abc1234",
                    regressionTest = "NamedRegression", deployedVersion = "feed1234",
                    verifiedBy = "forged", verifiedAt = DateTimeOffset.UtcNow },
                HttpStatusCode.BadRequest, "bug_verification_identity_client_forbidden");
            await AssertApiError(client, admin.Token!, candidate.Id,
                new { status = "closed", closureDisposition = "fixed_verified", fixCommit = "abc1234" },
                HttpStatusCode.BadRequest, "bug_closure_evidence_required");
            await AssertApiError(client, admin.Token!, candidate.Id,
                new { status = "closed", closureDisposition = "duplicate", duplicateOf = candidate.Id },
                HttpStatusCode.BadRequest, "bug_duplicate_target_invalid");
            await AssertApiError(client, admin.Token!, candidate.Id,
                new { status = "closed", closureDisposition = "rejected" },
                HttpStatusCode.BadRequest, "bug_closure_reason_required");
            await AssertApiError(client, admin.Token!, candidate.Id,
                new { status = "closed", closureDisposition = "rejected", dryRun = true },
                HttpStatusCode.BadRequest, "bug_closure_reason_required");

            using (var dryRun = Authorized(HttpMethod.Patch, $"/api/admin/bugs/{candidate.Id}", admin.Token!,
                       new { status = "closed", closureDisposition = "fixed_verified", fixCommit = "abc1234",
                           regressionTest = "NamedRegression", deployedVersion = "feed1234", dryRun = true,
                           expectedVersion = store.Version, idempotencyKey = "bug-close-dry-run-1" }))
            using (var response = await client.SendAsync(dryRun))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var afterDryRun = store.Bugs(null).Single(item => item.Id == candidate.Id);
            Assert.Equal("new", afterDryRun.Status);
            Assert.Null(afterDryRun.ClosureDisposition);
            Assert.Null(afterDryRun.VerifiedBy);

            var staleVersion = store.Version;
            store.AddBug(player.Account, "并发变化", "让版本过期", "/test", null, null, "test");
            await AssertApiError(client, admin.Token!, candidate.Id,
                new { status = "closed", closureDisposition = "duplicate", duplicateOf = primary.Id,
                    expectedVersion = staleVersion, idempotencyKey = "bug-close-stale-1" },
                HttpStatusCode.Conflict, "version_conflict");

            var expectedVersion = store.Version;
            var body = new { status = "closed", closureDisposition = "fixed_verified", fixCommit = "abc1234",
                regressionTest = "NamedRegression", deployedVersion = "feed1234",
                expectedVersion, idempotencyKey = "bug-close-fixed-1" };
            using var firstRequest = Authorized(HttpMethod.Patch, $"/api/admin/bugs/{candidate.Id}", admin.Token!, body);
            using var firstResponse = await client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var closed = await firstResponse.Content.ReadFromJsonAsync<L12BugReportView>();
            Assert.Equal("fixed_verified", closed!.ClosureDisposition);
            Assert.Equal(admin.Account!.Username, closed.VerifiedBy);
            Assert.NotNull(closed.VerifiedAt);
            var historyCount = closed.History.Count;

            using var replayRequest = Authorized(HttpMethod.Patch, $"/api/admin/bugs/{candidate.Id}", admin.Token!, body);
            using var replayResponse = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            Assert.Equal("true", Assert.Single(replayResponse.Headers.GetValues("X-Idempotent-Replay")));
            var replayed = await replayResponse.Content.ReadFromJsonAsync<L12BugReportView>();
            Assert.Equal(historyCount, replayed!.History.Count);
            Assert.Contains(store.AdminCommands(type: "bug.update"), command =>
                command.IdempotencyKey == "bug-close-fixed-1" && command.Status == "executed");
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

    private static void AssertClosureError(string code, Action action)
    {
        var error = Assert.Throws<L12BugClosureValidationException>(action);
        Assert.Equal(code, error.Code);
    }

    private static async Task AssertApiError(HttpClient client, string token, string id, object body,
        HttpStatusCode status, string code)
    {
        using var request = Authorized(HttpMethod.Patch, $"/api/admin/bugs/{id}", token, body);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
        Assert.Equal(code, error!.Code);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-bug-close-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
