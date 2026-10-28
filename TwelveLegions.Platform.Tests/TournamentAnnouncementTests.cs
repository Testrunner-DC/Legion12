using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentAnnouncementTests
{
    [Fact]
    public async Task OrdinaryOrganizerPublishesAnnouncementThroughRealHttp()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        var payload = new L12TournamentCreatePayload("公告赛事", "single", "public", 16,
            DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5);
        using var created = await client.PostAsJsonAsync("/api/tournaments",
            new TournamentCreateRequest(payload, "announcement-create", f.Store.Version));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var organizer = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var tournament = Assert.Single(f.Store.Tournaments(organizer).Items);
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        var socketUri = new UriBuilder(f.Client.BaseAddress!) { Scheme = "ws", Path = "/ws" }.Uri;
        await socket.ConnectAsync(socketUri, CancellationToken.None);
        var hello = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { type = "hello", authToken = f.PlayerToken });
        await socket.SendAsync(hello, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await ReceiveMessage(socket, "session", null);
        const string announcement = "联系 QQ 123\n奖励：卡包";
        using var published = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/announcement",
            new { announcement, reason = "发布公告", idempotencyKey = "announcement-publish", expectedVersion = tournament.Version });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var change = await ReceiveMessage(socket, "resourceChanged", "tournaments");
        Assert.DoesNotContain("QQ 123", change.ToString(), StringComparison.Ordinal);
        Assert.Equal(new[] { "epoch", "resource", "revision", "type" },
            change.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
        var audits = f.Store.AdminAudit(limit: 1000);
        Assert.Contains(audits, item => item.Category == "tournament" && item.Action == "announcement" && item.Target == tournament.Id);
        Assert.DoesNotContain("QQ 123", System.Text.Json.JsonSerializer.Serialize(audits), StringComparison.Ordinal);
        using var listing = await client.GetAsync("/api/tournaments");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var listJson = System.Text.Json.JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        Assert.Equal("", Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray()).GetProperty("announcement").GetString());
        using var detailRead = await client.GetAsync($"/api/tournaments/{tournament.Id}");
        using var detailJson = System.Text.Json.JsonDocument.Parse(await detailRead.Content.ReadAsStringAsync());
        Assert.Equal(announcement, detailJson.RootElement.GetProperty("announcement").GetString());
        using var publicRead = await f.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.OK, publicRead.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await publicRead.Content.ReadAsStringAsync());
        Assert.Equal(announcement, json.RootElement.GetProperty("announcement").GetString());
    }

    [Fact]
    public async Task AnnouncementPreservesTextPermissionVersionPreviewAndVisibility()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var outsiderRegistration = f.Store.Register("annother", "password-123");
        Assert.True(outsiderRegistration.Success, outsiderRegistration.Message);
        var outsider = outsiderRegistration.Account!;
        using var ownerClient = new HttpClient { BaseAddress = f.Client.BaseAddress };
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        using var otherClient = new HttpClient { BaseAddress = f.Client.BaseAddress };
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsiderRegistration.Token);
        var context = new L12AdminAuditContext("announcement-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var payload = new L12TournamentCreatePayload("公告赛事", "single", "public", 16,
            DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5);
        var tournament = f.Store.CreateTournament(host, payload, context, true);
        Assert.Equal("", tournament.Announcement);
        var path = $"/api/tournaments/{tournament.Id}/announcement";
        const string text = "  联系 QQ 123\n奖励：卡包  ";
        using var denied = await otherClient.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest(text, "无权", "announcement-denied", tournament.Version));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var preview = await ownerClient.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest(text, "预览", "announcement-preview", tournament.Version, true));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("", f.Store.Tournament(host, tournament.Id)!.Announcement);
        var request = new TournamentAnnouncementRequest(text, "发布", "announcement-commit", tournament.Version);
        using var commit = await ownerClient.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        var saved = f.Store.Tournament(host, tournament.Id)!;
        Assert.Equal(text, saved.Announcement);
        Assert.Equal(text, f.Store.PublicTournamentByCode(saved.Code)!.Announcement);
        var restored = new L12PlatformStore(f.MirrorPath);
        Assert.Equal(text, restored.Tournament(host, saved.Id)!.Announcement);
        using var repeated = await ownerClient.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(saved.Version, f.Store.Tournament(host, saved.Id)!.Version);
        using var stale = await ownerClient.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest("", "过期版本", "announcement-stale", tournament.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var oversized = await ownerClient.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest(new string('x', 2001), "超长", "announcement-oversize", saved.Version));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal(text, f.Store.Tournament(host, saved.Id)!.Announcement);
        using var maximum = await ownerClient.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest(new string('x', 2000), "上限", "announcement-max", saved.Version));
        Assert.Equal(HttpStatusCode.OK, maximum.StatusCode);
        saved = f.Store.Tournament(host, saved.Id)!;
        Assert.Equal(2000, saved.Announcement.Length);
        using var clear = await ownerClient.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest("", "清除", "announcement-clear", saved.Version));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        saved = f.Store.Tournament(host, saved.Id)!;
        Assert.Equal("", saved.Announcement);
        Assert.Equal("", f.Store.PublicTournamentByCode(saved.Code)!.Announcement);

        var hidden = f.Store.CreateTournament(host, payload with { Visibility = "code" }, context, true);
        hidden = f.Store.SetTournamentAnnouncement(host, hidden.Id, "凭码赛事公告", hidden.Version, context, true);
        Assert.Null(f.Store.PublicTournamentByCode(hidden.Code));
        using var hiddenPublic = await f.Client.GetAsync($"/api/public/tournaments/code/{hidden.Code}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenPublic.StatusCode);
        using var hiddenById = await otherClient.GetAsync($"/api/tournaments/{hidden.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenById.StatusCode);
        using var hiddenByCode = await otherClient.GetAsync($"/api/tournaments/code/{hidden.Code}");
        Assert.Equal(HttpStatusCode.OK, hiddenByCode.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await hiddenByCode.Content.ReadAsStringAsync());
        Assert.Equal("凭码赛事公告", json.RootElement.GetProperty("announcement").GetString());
    }
    private static async Task<System.Text.Json.JsonElement> ReceiveMessage(
        System.Net.WebSockets.ClientWebSocket socket, string type, string? resource)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[32768];
        while (true)
        {
            using var bytes = new MemoryStream();
            System.Net.WebSockets.WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(buffer, cancellation.Token);
                if (frame.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    throw new InvalidOperationException("Socket closed before resource message");
                bytes.Write(buffer, 0, frame.Count);
            } while (!frame.EndOfMessage);
            using var json = System.Text.Json.JsonDocument.Parse(bytes.ToArray());
            var value = json.RootElement;
            if (!value.TryGetProperty("type", out var actualType) || actualType.GetString() != type) continue;
            if (resource is not null && (!value.TryGetProperty("resource", out var actualResource)
                || actualResource.GetString() != resource)) continue;
            return value.Clone();
        }
    }}