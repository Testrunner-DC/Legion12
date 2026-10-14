using System.Net;
using System.Text.Json;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminAccountQueryTests
{
    [Theory]
    [InlineData("page=not-an-integer")]
    [InlineData("pageSize=not-an-integer")]
    [InlineData("deleted=invalid")]
    [InlineData("search=too-long")]
    public async Task RealQueryRejectsMalformedParametersWithoutEchoingSearch(string query)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        if (query == "search=too-long") query = "search=" + new string('x', 201);
        using var request = f.Request(f.AdminToken);
        request.RequestUri = new Uri("/api/admin/accounts/query?" + query, UriKind.Relative);
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(new string('x', 201), await response.Content.ReadAsStringAsync());
        Assert.Equal(0, f.StorageCalls);
    }

    [Fact]
    public async Task CancelledQueryDoesNotChangeCoreOrMirror()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var mirror = File.ReadAllBytes(f.MirrorPath);
        var version = f.Store.Version;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => f.Store.QueryAccounts(cancellationToken: cancellation.Token));
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(mirror, File.ReadAllBytes(f.MirrorPath));
    }

    [Fact]
    public async Task RealQueryUsesBoundedStablePagesSearchAndClampsWhileLegacyStaysComplete()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        for (var i = 0; i < 17; i++) Assert.True(f.Store.Register($"pageuser{i:D2}", "Password123!").Success);
        var all = f.Store.Accounts().Where(a => !a.Deleted).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => a.Id).ToArray();
        var found = new List<string>();
        for (var page = 1; page <= (int)Math.Ceiling(all.Length / 10d); page++)
        {
            using var result = await Get(f, $"/api/admin/accounts/query?page={page}&pageSize=10");
            var root = result.RootElement;
            Assert.Equal(page, root.GetProperty("page").GetInt32());
            Assert.Equal(all.Length, root.GetProperty("total").GetInt32());
            var items = root.GetProperty("items"); Assert.InRange(items.GetArrayLength(), 1, 10);
            found.AddRange(items.EnumerateArray().Select(a => a.GetProperty("id").GetString()!));
        }
        Assert.Equal(all, found);
        using var searched = await Get(f, "/api/admin/accounts/query?page=2147483647&pageSize=10&search=PAGEUSER");
        Assert.Equal(17, searched.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, searched.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(7, searched.RootElement.GetProperty("items").GetArrayLength());
        using var cap = await Get(f, "/api/admin/accounts/query?page=-1&pageSize=2147483647");
        Assert.Equal(1, cap.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, cap.RootElement.GetProperty("pageSize").GetInt32());
        using var empty = await Get(f, "/api/admin/accounts/query?search=not-found-synthetic");
        Assert.Equal(0, empty.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, empty.RootElement.GetProperty("items").GetArrayLength());
        using var legacy = await Get(f, "/api/admin/accounts");
        Assert.Equal(all.Length, legacy.RootElement.GetArrayLength());
        Assert.Equal(0, f.StorageCalls);
    }

    [Fact]
    public async Task RealDeletedPageIsSeparateAndReturnsGlobalCounts()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var created = f.Store.Register("pagegone", "Password123!"); Assert.True(created.Success);
        f.Store.DeleteAccountPersonalData(f.Store.AuthenticateTokenSession(f.AdminToken)!.Account, created.Account!.Id,
            "synthetic query deletion", new TwelveLegions.Server.L12AdminAuditContext("synthetic-query-delete", "admin.accounts.status.write"), true);
        using var active = await Get(f, "/api/admin/accounts/query");
        Assert.Equal(1, active.RootElement.GetProperty("deletedCount").GetInt32());
        Assert.DoesNotContain(active.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("deleted").GetBoolean());
        using var deleted = await Get(f, "/api/admin/accounts/query?deleted=true");
        Assert.Equal(1, deleted.RootElement.GetProperty("total").GetInt32());
        Assert.True(deleted.RootElement.GetProperty("items")[0].GetProperty("deleted").GetBoolean());
        Assert.Equal(active.RootElement.GetProperty("activeCount").GetInt32(), deleted.RootElement.GetProperty("activeCount").GetInt32());
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("player", 403)]
    [InlineData("revoked", 401)]
    public async Task RealQueryRetainsAuthenticationAndPermission(string identity, int status)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var request = f.Request(identity switch { "player" => f.PlayerToken, "revoked" => f.RevokedToken, _ => null });
        request.RequestUri = new Uri("/api/admin/accounts/query", UriKind.Relative);
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
    }

    private static async Task<JsonDocument> Get(AdminWorkbenchTimingTests.Fixture f, string path)
    {
        using var request = f.Request(f.AdminToken); request.RequestUri = new Uri(path, UriKind.Relative);
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
