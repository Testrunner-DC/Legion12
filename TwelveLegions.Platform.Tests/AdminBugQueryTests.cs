using System.Net;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminBugQueryTests
{
    [Theory]
    [InlineData("page=bad")]
    [InlineData("pageSize=bad")]
    [InlineData("search=too-long")]
    public async Task RealQueryRejectsMalformedParametersWithoutEchoingSearch(string query)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        if (query == "search=too-long") query = "search=" + new string('x', 201);
        using var response = await Send(f, "/api/admin/bugs/query?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(new string('x', 201), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RealQueryReturnsStableCompleteBoundedPagesAndRetainsLegacyHistory()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        for (var i = 0; i < 23; i++)
        {
            var bug = f.Store.AddBug(actor, $"query target {i:D2}", "synthetic description", "/synthetic", null, null, "synthetic");
            f.Store.UpdateBug(actor, bug.Id, "new", "normal", null, null, "synthetic history retained");
        }
        var all = f.Store.Bugs(null).OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var mirror = File.ReadAllBytes(f.MirrorPath); var version = f.Store.Version;
        var found = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            using var result = await Get(f, $"/api/admin/bugs/query?page={page}&pageSize=10");
            var root = result.RootElement;
            Assert.Equal(page, root.GetProperty("page").GetInt32());
            Assert.Equal(23, root.GetProperty("total").GetInt32());
            Assert.Equal(version, root.GetProperty("platformVersion").GetInt64());
            Assert.Equal(page < 3 ? 10 : 3, root.GetProperty("items").GetArrayLength());
            foreach (var row in root.GetProperty("items").EnumerateArray())
            {
                found.Add(row.GetProperty("id").GetString()!);
                Assert.Equal(2, row.GetProperty("history").GetArrayLength());
            }
        }
        Assert.Equal(all.Select(row => row.Id), found);
        using var clamped = await Get(f, "/api/admin/bugs/query?page=2147483647&pageSize=10");
        Assert.Equal(3, clamped.RootElement.GetProperty("page").GetInt32());
        using var capped = await Get(f, "/api/admin/bugs/query?page=-1&pageSize=2147483647");
        Assert.Equal(100, capped.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, capped.RootElement.GetProperty("page").GetInt32());
        using var legacy = await Get(f, "/api/admin/bugs");
        Assert.Equal(23, legacy.RootElement.GetArrayLength());
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(mirror, File.ReadAllBytes(f.MirrorPath));
        Assert.Equal(0, f.StorageCalls);
    }

    [Fact]
    public async Task SharedFiltersKeepRawStatusesAndSearchMeaningAcrossPages()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var first = f.Store.AddBug(actor, "Needle title", "description", "/synthetic", "ROOMneedle", null, "synthetic");
        f.Store.UpdateBug(actor, first.Id, "retest", "high", "owner", "note");
        var second = f.Store.AddBug(actor, "other", "description needle", "/synthetic", null, "matchneedle", "synthetic");
        f.Store.UpdateBug(actor, second.Id, "closed", "normal", null, null, "synthetic rejected reason", closureDisposition: "rejected");
        foreach (var query in new[] { "status=retest", "priority=high", "assignee=OWNER", "search=NEEDLE", "search=ROOMneedle", "search=matchneedle", "status=closed", "status=resolved", "search=no-synthetic-match" })
        {
            using var paged = await Get(f, "/api/admin/bugs/query?pageSize=1&" + query);
            using var legacy = await Get(f, "/api/admin/bugs?" + query);
            Assert.Equal(legacy.RootElement.GetArrayLength(), paged.RootElement.GetProperty("total").GetInt32());
            if (legacy.RootElement.GetArrayLength() > 0)
                Assert.Equal(legacy.RootElement[0].GetProperty("id").GetString(), paged.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task PageVersionUsesExistingCasRejectsStaleDraftAndAllowsExplicitRefresh()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var bug = f.Store.AddBug(actor, "cas target", "synthetic description", "/synthetic", null, null, "synthetic");
        using var page = await Get(f, "/api/admin/bugs/query");
        var basis = page.RootElement.GetProperty("platformVersion").GetInt64();
        f.Store.UpdateBug(actor, bug.Id, "new", "high", null, "someone else's newer note");
        using (var stale = await Patch(f, bug.Id, basis, "cas-stale", "my unsaved note"))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var body = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
            Assert.Equal("version_conflict", body.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal("someone else's newer note", Assert.Single(f.Store.Bugs(null)).AdminNotes);
        using var refreshed = await Get(f, "/api/admin/bugs/query");
        using var applied = await Patch(f, bug.Id, refreshed.RootElement.GetProperty("platformVersion").GetInt64(), "cas-refreshed", "explicit refreshed note");
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal("explicit refreshed note", Assert.Single(f.Store.Bugs(null)).AdminNotes);
    }

    [Fact]
    public async Task ClosingWithConfirmedMetadataKeepsAssignmentNotesAndHistory()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var bug = f.Store.AddBug(actor, "metadata target", "original description", "/synthetic", null, null, "synthetic");
        f.Store.UpdateBug(actor, bug.Id, "retest", "high", "confirmed-owner", "confirmed-notes");
        var page = f.Store.QueryBugs(); var confirmed = Assert.Single(page.Items);
        using var request = f.Request(f.AdminToken);
        request.Method = HttpMethod.Patch; request.RequestUri = new Uri($"/api/admin/bugs/{bug.Id}", UriKind.Relative);
        request.Content = new StringContent(JsonSerializer.Serialize(new { status = "closed", closureDisposition = "rejected",
            assignee = confirmed.Assignee, adminNotes = confirmed.AdminNotes, comment = "synthetic bounded rejection",
            expectedVersion = page.PlatformVersion, idempotencyKey = "keep-meta" }), System.Text.Encoding.UTF8, "application/json");
        using var response = await f.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = Assert.Single(f.Store.Bugs("closed"));
        Assert.Equal("confirmed-owner", closed.Assignee); Assert.Equal("confirmed-notes", closed.AdminNotes);
        Assert.Equal("original description", closed.Description); Assert.Equal(bug.CreatedAt, closed.CreatedAt);
        Assert.True(closed.History.Count > confirmed.History.Count);
        Assert.Contains(closed.History, item => item.Action == "comment" && item.Comment == "synthetic bounded rejection");
    }

    [Fact]
    public async Task CancelledQueryChangesNeitherCoreNorMirror()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var mirror = File.ReadAllBytes(f.MirrorPath); var version = f.Store.Version;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => f.Store.QueryBugs(cancellationToken: cancellation.Token));
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(mirror, File.ReadAllBytes(f.MirrorPath));
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("player", 403)]
    [InlineData("revoked", 401)]
    public async Task RealQueryRetainsAuthenticationAndPermission(string identity, int expected)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var response = await Send(f, "/api/admin/bugs/query", identity switch { "player" => f.PlayerToken, "revoked" => f.RevokedToken, "anonymous" => "", _ => f.AdminToken });
        Assert.Equal(expected, (int)response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Send(AdminWorkbenchTimingTests.Fixture f, string path, string? token = null)
    {
        using var request = f.Request(token == "" ? null : token ?? f.AdminToken);
        request.RequestUri = new Uri(path, UriKind.Relative);
        return await f.Client.SendAsync(request);
    }
    private static async Task<JsonDocument> Get(AdminWorkbenchTimingTests.Fixture f, string path)
    {
        using var response = await Send(f, path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
    private static async Task<HttpResponseMessage> Patch(AdminWorkbenchTimingTests.Fixture f, string id, long version, string key, string note)
    {
        using var request = f.Request(f.AdminToken);
        request.Method = HttpMethod.Patch; request.RequestUri = new Uri($"/api/admin/bugs/{id}", UriKind.Relative);
        request.Content = new StringContent(JsonSerializer.Serialize(new { adminNotes = note, expectedVersion = version, idempotencyKey = key }), System.Text.Encoding.UTF8, "application/json");
        return await f.Client.SendAsync(request);
    }
}
