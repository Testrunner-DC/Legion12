using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// Only the previously missing HTTP links, using isolated synthetic accounts/content.
[Collection("Platform environment")]
public sealed class AdminBusinessFlowLinkTests
{
    [Fact]
    public async Task AlternateArtGrantPlayerReadAndRevokeUseTheRealHttpChain()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First();
        var playerId = f.Store.AuthenticateTokenSession(f.PlayerToken)!.Account.Id;
        using var granted = await Send(f, HttpMethod.Post, "/api/admin/alternate-art-grants", f.AdminToken,
            new { alternateArtId = art.Id, accountId = playerId, sourceKind = "manual", sourceReference = "合成端到端验收" });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using var grantBody = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
        var grantId = grantBody.RootElement.GetProperty("id").GetString()!;
        using var own = await Send(f, HttpMethod.Get, "/api/me/alternate-arts", f.PlayerToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var ownBody = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
        Assert.Contains(ownBody.RootElement.EnumerateArray(), row => row.GetProperty("id").GetString() == art.Id);
        using var notOther = await Send(f, HttpMethod.Get, "/api/me/alternate-arts", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, notOther.StatusCode);
        using var otherBody = JsonDocument.Parse(await notOther.Content.ReadAsStringAsync());
        Assert.DoesNotContain(otherBody.RootElement.EnumerateArray(), row => row.GetProperty("id").GetString() == art.Id);
        using var denied = await Send(f, HttpMethod.Delete, $"/api/admin/alternate-art-grants/{grantId}", f.PlayerToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var revoked = await Send(f, HttpMethod.Delete, $"/api/admin/alternate-art-grants/{grantId}", f.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var after = await Send(f, HttpMethod.Get, "/api/me/alternate-arts", f.PlayerToken);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var afterBody = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.DoesNotContain(afterBody.RootElement.EnumerateArray(), row => row.GetProperty("id").GetString() == art.Id);
        using var ledger = await Send(f, HttpMethod.Get, "/api/admin/alternate-art-grants", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
        using var ledgerBody = JsonDocument.Parse(await ledger.Content.ReadAsStringAsync());
        var retained = ledgerBody.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == grantId);
        Assert.Equal(JsonValueKind.String, retained.GetProperty("revokedAt").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenameApplicationReviewAndPlayerReadbackUseTheRealHttpChain(bool approve)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var changed = await Send(f, HttpMethod.Post, "/api/auth/username-change", f.PlayerToken,
            new { currentPassword = "password-123", newUsername = "wbrenamed" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var applied = await Send(f, HttpMethod.Post, "/api/auth/username-change-requests", f.PlayerToken,
            new { newUsername = "wbapproved", reason = "synthetic rename approval" });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using var body = JsonDocument.Parse(await applied.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;
        using var pending = await Send(f, HttpMethod.Get, "/api/admin/username-change-requests?status=pending", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        using var pendingBody = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
        Assert.Equal(id, pendingBody.RootElement[0].GetProperty("id").GetString());
        using var denied = await Send(f, HttpMethod.Post, $"/api/admin/username-change-requests/{id}/review", f.PlayerToken,
            new { approve, note = "synthetic reviewer" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        if (!approve)
        {
            using var invalid = await Send(f, HttpMethod.Post, $"/api/admin/username-change-requests/{id}/review", f.AdminToken,
                new { approve, note = "" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var reviewed = await Send(f, HttpMethod.Post, $"/api/admin/username-change-requests/{id}/review", f.AdminToken,
            new { approve, note = "synthetic reviewer" });
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        using var reviewBody = JsonDocument.Parse(await reviewed.Content.ReadAsStringAsync());
        Assert.Equal(approve ? "approved" : "rejected", reviewBody.RootElement.GetProperty("status").GetString());
        using var me = await Send(f, HttpMethod.Get, "/api/auth/me", f.PlayerToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meBody = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(approve ? "wbapproved" : "wbrenamed", meBody.RootElement.GetProperty("username").GetString());
        using var ownStatus = await Send(f, HttpMethod.Get, "/api/auth/username-change-status", f.PlayerToken);
        Assert.Equal(HttpStatusCode.OK, ownStatus.StatusCode);
        using var ownBody = JsonDocument.Parse(await ownStatus.Content.ReadAsStringAsync());
        Assert.Equal(approve ? "approved" : "rejected", ownBody.RootElement.GetProperty("latestRequest").GetProperty("status").GetString());
        using var repeated = await Send(f, HttpMethod.Post, $"/api/admin/username-change-requests/{id}/review", f.AdminToken,
            new { approve, note = "synthetic repeat" });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }

    [Fact]
    public async Task ArticleDraftPublishWithdrawRestoreAndPublicReadbackUseTheRealHttpChain()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var draft = new { title = "合成验收资讯", summary = "合成摘要", body = "合成第一版正文", category = "赛季更新", coverUrl = "", link = "/battle", slug = "synthetic-flow", pinned = false };
        using var denied = await Send(f, HttpMethod.Post, "/api/admin/articles", f.PlayerToken, draft);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var saved = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, draft);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var savedBody = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
        var id = savedBody.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("draft", savedBody.RootElement.GetProperty("status").GetString());
        await AssertPublicCount(f, 0);
        using var published = await Send(f, HttpMethod.Post, $"/api/admin/articles/{id}/publish", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        await AssertPublicCount(f, 1);
        using var deniedWithdraw = await Send(f, HttpMethod.Post, $"/api/admin/articles/{id}/withdraw", f.PlayerToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedWithdraw.StatusCode);
        await AssertPublicCount(f, 1);
        using var withdrawn = await Send(f, HttpMethod.Post, $"/api/admin/articles/{id}/withdraw", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        await AssertPublicCount(f, 0);
        using var restored = await Send(f, HttpMethod.Post, $"/api/admin/articles/{id}/restore", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var restoredBody = JsonDocument.Parse(await restored.Content.ReadAsStringAsync());
        Assert.Equal("draft", restoredBody.RootElement.GetProperty("status").GetString());
        await AssertPublicCount(f, 0); // Restore is draft, not an implicit public re-publish.
        using var republished = await Send(f, HttpMethod.Post, $"/api/admin/articles/{id}/publish", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, republished.StatusCode);
        await AssertPublicCount(f, 1);
    }

    private static async Task AssertPublicCount(AdminWorkbenchTimingTests.Fixture f, int count)
    {
        using var response = await Send(f, HttpMethod.Get, "/api/articles", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(count, json.RootElement.GetArrayLength());
        if (count == 1) Assert.Equal("合成第一版正文", json.RootElement[0].GetProperty("body").GetString());
    }
    private static async Task<HttpResponseMessage> Send(AdminWorkbenchTimingTests.Fixture f, HttpMethod method,
        string path, string? token, object? body = null)
    {
        using var request = f.Request(token); request.Method = method; request.RequestUri = new Uri(path, UriKind.Relative);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Client.SendAsync(request);
    }
}
