using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ArticleAlternateArtTests
{
    [Fact]
    public async Task HttpDraftPublishEditReloadAndWithdrawPreserveIndependentArtworkIdentity()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First(item => item.BuiltIn && item.Kind == "alternate-art");
        var body = Body(art.BaseCardId, art.Id);
        using var saved = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(body));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var row = (await saved.Content.ReadFromJsonAsync<L12ArticleView>())!;
        Assert.Equal(body, row.Body);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync("/api/articles/by-key/" + row.Id)).StatusCode);
        using var publish = await Send(f, HttpMethod.Post, $"/api/admin/articles/{row.Id}/publish", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using var detail = await f.Client.GetAsync("/api/articles/by-key/" + row.Id);
        Assert.Equal(body, (await detail.Content.ReadFromJsonAsync<L12ArticleView>())!.Body);
        var published = f.Store.AdminArticle(row.Id)!;
        var editedBody = Body(art.BaseCardId, art.Id, "更新后备注");
        using var edit = await Send(f, HttpMethod.Put, "/api/admin/articles/" + row.Id, f.AdminToken,
            Draft(editedBody, published.Revision));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var stillPublished = await f.Client.GetAsync("/api/articles/by-key/" + row.Id);
        Assert.Equal(body, (await stillPublished.Content.ReadFromJsonAsync<L12ArticleView>())!.Body);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var reopened = new L12PlatformStore(f.MirrorPath, catalog.PresetDecks, officialCards: catalog.Cards,
            officialAlternateArts: catalog.OfficialAlternateArts);
        Assert.Equal(editedBody, reopened.AdminArticle(row.Id)!.Body);
        Assert.Contains(reopened.ArticleRevisions(row.Id), revision => revision.Body == body);
        using var republish = await Send(f, HttpMethod.Post, $"/api/admin/articles/{row.Id}/publish", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, republish.StatusCode);
        using var reread = await f.Client.GetAsync("/api/articles/by-key/" + row.Id);
        Assert.Equal(editedBody, (await reread.Content.ReadFromJsonAsync<L12ArticleView>())!.Body);
        using var withdraw = await Send(f, HttpMethod.Post, $"/api/admin/articles/{row.Id}/withdraw", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync("/api/articles/by-key/" + row.Id)).StatusCode);
    }

    [Fact]
    public async Task HttpRejectsMalformedUnknownMismatchedAndUrlFieldsWithoutCreatingDrafts()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First();
        var other = f.Store.AlternateArts().First(item => item.BaseCardId != art.BaseCardId);
        var before = f.Store.AdminArticles().Count;
        foreach (var bad in new object?[] { null, 123, true, new { id = art.Id }, new[] { art.Id }, "", " ",
                     "unknown", " " + art.Id, art.Id + "\n", new string('x', 101), other.Id,
                     "https://tracker.invalid/art.webp" })
        {
            var block = new { id = "card", type = "card", cardId = art.BaseCardId, alternateArtId = bad };
            using var rejected = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(Serialize(block)));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, f.Store.AdminArticles().Count);
        }
        foreach (var block in new object[] {
            new { id = "card", type = "card", cardId = art.Id, alternateArtId = art.Id },
            new { id = "card", type = "card", cardId = art.BaseCardId, alternateArtId = art.Id, imageUrl = "/api/site/media/arbitrary" },
        })
        {
            using var rejected = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(Serialize(block)));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, f.Store.AdminArticles().Count);
        }
        using var valid = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(Body(art.BaseCardId, art.Id)));
        var original = (await valid.Content.ReadFromJsonAsync<L12ArticleView>())!;
        using var invalidEdit = await Send(f, HttpMethod.Put, "/api/admin/articles/" + original.Id, f.AdminToken,
            Draft(Body(art.BaseCardId, "unknown-art"), original.Revision));
        Assert.Equal(HttpStatusCode.BadRequest, invalidEdit.StatusCode);
        Assert.Equal(original.Body, f.Store.AdminArticle(original.Id)!.Body);
        Assert.Equal(original.Revision, f.Store.AdminArticle(original.Id)!.Revision);
    }

    [Fact]
    public async Task ActiveCustomArtIsAcceptedButDisabledOrStandaloneArtCannotPublish()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var admin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var baseCardId = f.Store.AlternateArts().First().BaseCardId;
        var media = Upload(f.Store, admin);
        var active = f.Store.SaveAlternateArt(admin, new(null, "ARTICLE-ART", baseCardId, "公开合成异画", media.Id));
        Assert.Contains(f.Store.AlternateArts(), item => item.Id == active.Id);
        using var save = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(Body(baseCardId, active.Id)));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var row = (await save.Content.ReadFromJsonAsync<L12ArticleView>())!;
        f.Store.SaveAlternateArt(admin, new(active.Id, active.ArtCode, baseCardId, active.DisplayName, media.Id, Active: false));
        using var blockedPublish = await Send(f, HttpMethod.Post, $"/api/admin/articles/{row.Id}/publish", f.AdminToken);
        Assert.Equal(HttpStatusCode.BadRequest, blockedPublish.StatusCode);
        Assert.Equal("draft", f.Store.AdminArticle(row.Id)!.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync("/api/articles/by-key/" + row.Id)).StatusCode);
        using var disabled = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(Body(baseCardId, active.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, disabled.StatusCode);
        var standalone = f.Store.SaveAlternateArt(admin, new(null, "ARTICLE-SHOW", "", "独立展示卡", media.Id, Kind: "gallery-card"));
        using var invalid = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(Body(baseCardId, standalone.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task LegacyCardAndPlainTextRemainValidAndArtworkDoesNotGrantRights()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First();
        foreach (var body in new[] { "旧纯文本正文", Serialize(new { id = "legacy", type = "card", cardId = art.BaseCardId }), Body(art.BaseCardId, art.Id) })
        {
            using var saved = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, Draft(body));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var row = (await saved.Content.ReadFromJsonAsync<L12ArticleView>())!;
            using var published = await Send(f, HttpMethod.Post, $"/api/admin/articles/{row.Id}/publish", f.AdminToken);
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }
        Assert.Empty(f.Store.AlternateArtGrants());
        Assert.Empty(f.Store.OwnedAlternateArts(f.Store.AuthenticateTokenSession(f.PlayerToken)!.Account.Id));
    }

    [Fact]
    public async Task ArticleArtworkKeepsExistingAuthenticationAndDraftPermissionBoundaries()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First();
        var before = f.Store.AdminArticles().Count;
        foreach (var (token, status) in new[] { ((string?)null, HttpStatusCode.Unauthorized),
                     (f.PlayerToken, HttpStatusCode.Forbidden), (f.RevokedToken, HttpStatusCode.Unauthorized) })
        {
            using var denied = await Send(f, HttpMethod.Post, "/api/admin/articles", token, Draft(Body(art.BaseCardId, art.Id)));
            Assert.Equal(status, denied.StatusCode);
        }
        Assert.Equal(before, f.Store.AdminArticles().Count);
    }

    private static object Draft(string body, long? revision = null) => new { title = "合成异画资讯", body,
        category = "官方公告", kind = "news", expectedRevision = revision };
    private static string Body(string baseCardId, string artId, string caption = "异画备注")
        => Serialize(new { id = "card", type = "card", cardId = baseCardId, alternateArtId = artId, caption });
    private static string Serialize(object block) => JsonSerializer.Serialize(new { format = "l12-blocks", version = 1, blocks = new[] { block } });
    private static async Task<HttpResponseMessage> Send(AdminWorkbenchTimingTests.Fixture f, HttpMethod method,
        string endpoint, string? token, object? body = null)
    {
        using var request = f.Request(token); request.Method = method; request.RequestUri = new Uri(endpoint, UriKind.Relative);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Client.SendAsync(request);
    }
    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "card-art");
        var desktop = Webp(policy.FlexibleDimensions ? 997 : policy.DesktopWidth, policy.FlexibleDimensions ? 331 : policy.DesktopHeight);
        return store.UploadSiteMedia(admin, new("card-art", "synthetic.webp", "image/webp", desktop, desktop,
            Webp(policy.FlexibleDimensions ? 421 : policy.MobileWidth, policy.FlexibleDimensions ? 777 : policy.MobileHeight),
            Webp(policy.FlexibleDimensions ? 137 : policy.ThumbnailWidth, policy.FlexibleDimensions ? 59 : policy.ThumbnailHeight), "合成卡图", .5, .5));
    }
    // Metadata fixture only: not decoded browser-pixel evidence.
    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        for (var i = 0; i < 3; i++) { bytes[24 + i] = (byte)((width - 1) >> (8 * i)); bytes[27 + i] = (byte)((height - 1) >> (8 * i)); }
        return bytes;
    }
}
