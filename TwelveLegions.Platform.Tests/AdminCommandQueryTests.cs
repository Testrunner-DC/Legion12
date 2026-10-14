using System.Net;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminCommandQueryTests
{
    [Fact]
    public async Task FullHistoryPaginationRetainsLegacyWindowAndStableOrdering()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var bus = new L12AdminCommandBus(f.Store);
        var at = DateTimeOffset.UtcNow;
        for (var i = 0; i < 205; i++)
        {
            var id = $"page-command-{i:D3}";
            var envelope = new L12AdminCommandEnvelope<object>(id, id, "bug.update", actor, at,
                "bug:synthetic", null, false, null, new { value = i }, new(id, "admin.bugs.write"));
            Assert.True(bus.Execute(envelope, L12Permission.AdminBugsWrite,
                _ => L12AdminCommandResult<int>.Ok(i)).Success);
        }
        var version = f.Store.Version; var mirror = File.ReadAllBytes(f.MirrorPath);
        var expected = f.Store.AdminCommands(limit: 1000).OrderByDescending(row => row.RequestedAt).ThenBy(row => row.Id, StringComparer.Ordinal).Select(row => row.Id).ToArray();
        var found = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            using var result = await Get(f, $"/api/admin/v1/commands/query?page={page}&pageSize=100");
            Assert.Equal(205, result.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(page < 3 ? 100 : 5, result.RootElement.GetProperty("items").GetArrayLength());
            found.AddRange(result.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString()!));
        }
        Assert.Equal(expected, found);
        using var legacy = await Get(f, "/api/admin/v1/commands");
        Assert.Equal(200, legacy.RootElement.GetArrayLength());
        using var clamped = await Get(f, "/api/admin/v1/commands/query?page=2147483647&pageSize=10");
        Assert.Equal(21, clamped.RootElement.GetProperty("page").GetInt32());
        using var capped = await Get(f, "/api/admin/commands/query?page=-1&pageSize=2147483647");
        Assert.Equal(1, capped.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, capped.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllBytes(f.MirrorPath));
    }

    [Fact]
    public async Task FilteringDetailAndCancellationKeepOriginalActorAndResultSemantics()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var other = f.Store.Register("cmdreader", "password-123").Account!;
        Assert.True(f.Store.SetRole(actor, other.Id, "admin")); other = f.Store.Account(other.Id)!;
        var bus = new L12AdminCommandBus(f.Store);
        foreach (var (id, who, type, success) in new[] { ("cmd-own", actor, "bug.update", true), ("cmd-other", other, "content.draft.save", false) })
        {
            var envelope = new L12AdminCommandEnvelope<object>(id, id, type, who, DateTimeOffset.UtcNow,
                "synthetic", null, false, null, new { synthetic = id }, new(id));
            var result = bus.Execute(envelope, L12Permission.AdminBugsWrite,
                _ => success ? L12AdminCommandResult<string>.Ok("synthetic outcome")
                    : L12AdminCommandResult<string>.Fail("synthetic_reject", "synthetic failure", 400));
            Assert.Equal(success, result.Success);
        }
        foreach (var query in new[] { "status=executed", "status=failed", "status=requested", "type=bug.update", "actorId=" + other.Id, "type=not-found" })
        {
            using var paged = await Get(f, "/api/admin/commands/query?" + query);
            using var legacy = await Get(f, "/api/admin/commands?" + query);
            Assert.Equal(legacy.RootElement.GetArrayLength(), paged.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(legacy.RootElement.GetRawText(), paged.RootElement.GetProperty("items").GetRawText());
        }
        using var detail = await Get(f, "/api/admin/commands/cmd-other");
        Assert.Equal("synthetic failure", detail.RootElement.GetProperty("failureReason").GetString());
        Assert.Equal("cmd-other", detail.RootElement.GetProperty("payload").GetProperty("synthetic").GetString());
        var version = f.Store.Version; var mirror = File.ReadAllBytes(f.MirrorPath);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => f.Store.QueryAdminCommands(cancellationToken: cancelled.Token));
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllBytes(f.MirrorPath));
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("player", 403)]
    [InlineData("revoked", 401)]
    public async Task RealQueryDoesNotExposeCommandsWithoutOriginalPermission(string identity, int expected)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var request = f.Request(identity switch { "player" => f.PlayerToken, "revoked" => f.RevokedToken, _ => null });
        request.RequestUri = new Uri("/api/admin/v1/commands/query?actorId=someone-else", UriKind.Relative);
        using var response = await f.Client.SendAsync(request); Assert.Equal(expected, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("page=invalid")]
    [InlineData("pageSize=invalid")]
    public async Task RealQueryRejectsInvalidNumericParameters(string query)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var request = f.Request(f.AdminToken); request.RequestUri = new Uri("/api/admin/commands/query?" + query, UriKind.Relative);
        using var response = await f.Client.SendAsync(request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<JsonDocument> Get(AdminWorkbenchTimingTests.Fixture f, string path)
    {
        using var request = f.Request(f.AdminToken); request.RequestUri = new Uri(path, UriKind.Relative);
        using var response = await f.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
