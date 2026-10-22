using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AlternateArtGalleryWorkflowHttpTests
{
    private const string ImageEndpoint = "/api/admin/alternate-arts/image";

    [Fact]
    public async Task ImageReplacementRequiresTheRealAuthenticatedAdminRoute()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First(row => row.BuiltIn);
        var before = JsonSerializer.Serialize(f.Store.AlternateArts());
        var body = new { alternateArtId = art.Id, mediaAssetId = "not-an-upload" };
        using var anonymous = await Send(f, HttpMethod.Put, ImageEndpoint, null, body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var player = await Send(f, HttpMethod.Put, ImageEndpoint, f.PlayerToken, body);
        Assert.Equal(HttpStatusCode.Forbidden, player.StatusCode);
        using var revoked = await Send(f, HttpMethod.Put, ImageEndpoint, f.RevokedToken, body);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(f.Store.AlternateArts()));
    }

    [Fact]
    public async Task BuiltInImageOverrideFlowsThroughPublicOwnedAndMediaHttpThenRestores()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var baseline = f.Store.AlternateArts().First(row => row.BuiltIn && row.Products is { Count: > 0 });
        var player = f.Store.AuthenticateTokenSession(f.PlayerToken)!.Account;
        var admin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var media = Upload(f.Store, admin, "card-art");
        using var grant = await Send(f, HttpMethod.Post, "/api/admin/alternate-art-grants", f.AdminToken,
            new { alternateArtId = baseline.Id, accountId = player.Id, sourceKind = "manual", sourceReference = "synthetic-gallery-image" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        using var changed = await Send(f, HttpMethod.Put, ImageEndpoint, f.AdminToken,
            new { alternateArtId = baseline.Id, mediaAssetId = media.Id });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var changedJson = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        AssertStableIdentity(baseline, changedJson.RootElement);
        Assert.Equal(media.Id, changedJson.RootElement.GetProperty("mediaAssetId").GetString());
        Assert.Equal(media.DesktopUrl, changedJson.RootElement.GetProperty("imageUrl").GetString());

        foreach (var (endpoint, token) in new[]
                 { ("/api/alternate-arts", (string?)null), ("/api/me/alternate-arts", f.PlayerToken) })
        {
            using var response = await Send(f, HttpMethod.Get, endpoint, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var current = json.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == baseline.Id);
            AssertStableIdentity(baseline, current);
            Assert.Equal(media.DesktopUrl, current.GetProperty("imageUrl").GetString());
        }

        using var actualMedia = await Send(f, HttpMethod.Get, media.DesktopUrl, null);
        Assert.Equal(HttpStatusCode.OK, actualMedia.StatusCode);
        Assert.Equal("image/webp", actualMedia.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await actualMedia.Content.ReadAsByteArrayAsync());
        using var referenced = await Send(f, HttpMethod.Delete, "/api/admin/site/media/" + media.Id, f.AdminToken);
        Assert.Equal(HttpStatusCode.Conflict, referenced.StatusCode);

        using var restored = await Send(f, HttpMethod.Put, ImageEndpoint, f.AdminToken,
            new { alternateArtId = baseline.Id, mediaAssetId = "" });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var restoredJson = JsonDocument.Parse(await restored.Content.ReadAsStringAsync());
        AssertStableIdentity(baseline, restoredJson.RootElement);
        Assert.Equal(baseline.ImageUrl, restoredJson.RootElement.GetProperty("imageUrl").GetString());
        Assert.Equal(baseline.ThumbnailUrl, restoredJson.RootElement.GetProperty("thumbnailUrl").GetString());
        using var released = await Send(f, HttpMethod.Delete, "/api/admin/site/media/" + media.Id, f.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        using var oldMedia = await Send(f, HttpMethod.Get, media.DesktopUrl, null);
        Assert.Equal(HttpStatusCode.NotFound, oldMedia.StatusCode);
    }

    [Fact]
    public async Task CustomImageReplacementRejectsEmptyWrongKindAndMissingTargetWithoutChangingTheArt()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var admin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var original = Upload(f.Store, admin, "card-art");
        var wrongKind = Upload(f.Store, admin, "product");
        var baseCard = f.Store.AlternateArts().First(row => row.BuiltIn).BaseCardId;
        var art = f.Store.SaveAlternateArt(admin, new(null, "ALT-HTTP-IMAGE", baseCard, "原卡名称优先", original.Id));
        var before = JsonSerializer.Serialize(f.Store.AlternateArts().Single(row => row.Id == art.Id));
        foreach (var mediaId in new[] { "", "missing-media", wrongKind.Id })
        {
            using var response = await Send(f, HttpMethod.Put, ImageEndpoint, f.AdminToken,
                new { alternateArtId = art.Id, mediaAssetId = mediaId });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, JsonSerializer.Serialize(f.Store.AlternateArts().Single(row => row.Id == art.Id)));
        }
        using var missing = await Send(f, HttpMethod.Put, ImageEndpoint, f.AdminToken,
            new { alternateArtId = "missing-art", mediaAssetId = original.Id });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(f.Store.AlternateArts().Single(row => row.Id == art.Id)));
    }

    [Fact]
    public async Task NewProductUsesTheExistingDraftRouteAndRemainsHiddenFromThePublicGallery()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var category = f.Store.AdminSiteCategories("product").First(row => row.Active);
        var art = f.Store.AlternateArts().First(row => row.BuiltIn);
        var body = new { kind = "product", title = "合成画廊产品", categoryId = category.Id,
            summary = "", body = "", coverUrl = "", link = "", slug = "synthetic-gallery-product", pinned = false };
        using var denied = await Send(f, HttpMethod.Post, "/api/admin/articles", f.PlayerToken, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var created = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken, body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var productId = "site:" + createdJson.RootElement.GetProperty("id").GetString();
        Assert.Equal("product", createdJson.RootElement.GetProperty("kind").GetString());
        Assert.Equal("draft", createdJson.RootElement.GetProperty("status").GetString());

        using var products = await Send(f, HttpMethod.Get, "/api/admin/alternate-art-products", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        using var productsJson = JsonDocument.Parse(await products.Content.ReadAsStringAsync());
        var choice = productsJson.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == productId);
        Assert.Equal("draft", choice.GetProperty("status").GetString());
        using var associated = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts/metadata", f.AdminToken,
            new { alternateArtId = art.Id, productIds = new[] { productId }, rarityIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, associated.StatusCode);
        using var associatedJson = JsonDocument.Parse(await associated.Content.ReadAsStringAsync());
        Assert.Contains(associatedJson.RootElement.GetProperty("products").EnumerateArray(),
            row => row.GetProperty("id").GetString() == productId);
        using var publicly = await Send(f, HttpMethod.Get, "/api/alternate-arts", null);
        Assert.Equal(HttpStatusCode.OK, publicly.StatusCode);
        using var publicJson = JsonDocument.Parse(await publicly.Content.ReadAsStringAsync());
        var current = publicJson.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == art.Id);
        Assert.DoesNotContain(current.GetProperty("products").EnumerateArray(), row => row.GetProperty("id").GetString() == productId);
        Assert.Equal("draft", f.Store.AdminArticles(kind: "product").Single(row => "site:" + row.Id == productId).Status);
    }

    [Fact]
    public async Task ProductMergeRequiresAdminAndArchivesTheDuplicateSource()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var category = f.Store.AdminSiteCategories("product").First(row => row.Active);
        async Task<string> Create(string title, string slug)
        {
            using var response = await Send(f, HttpMethod.Post, "/api/admin/articles", f.AdminToken,
                new { kind = "product", title, categoryId = category.Id, summary = "", body = "",
                    coverUrl = "", link = "", slug, pinned = false });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return "site:" + json.RootElement.GetProperty("id").GetString();
        }

        var targetId = await Create("正式产品", "merge-target");
        var sourceId = await Create("误建产品", "merge-source");
        var body = new { targetProductId = targetId, sourceProductIds = new[] { sourceId } };
        using var denied = await Send(f, HttpMethod.Post, "/api/admin/alternate-art-products/merge", f.PlayerToken, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var merged = await Send(f, HttpMethod.Post, "/api/admin/alternate-art-products/merge", f.AdminToken, body);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        using var result = JsonDocument.Parse(await merged.Content.ReadAsStringAsync());
        Assert.Equal(targetId, result.RootElement.GetProperty("target").GetProperty("id").GetString());
        Assert.Equal(1, result.RootElement.GetProperty("archivedSiteProducts").GetInt32());
        using var products = await Send(f, HttpMethod.Get, "/api/admin/alternate-art-products", f.AdminToken);
        using var productsJson = JsonDocument.Parse(await products.Content.ReadAsStringAsync());
        Assert.Contains(productsJson.RootElement.EnumerateArray(), row => row.GetProperty("id").GetString() == targetId);
        Assert.DoesNotContain(productsJson.RootElement.EnumerateArray(), row => row.GetProperty("id").GetString() == sourceId);
    }

    [Fact]
    public async Task RegularCardsUseSeparateAdminRoutesAndPublicPresentationKeepsHiddenProductAssociations()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var admin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var product = f.Store.SaveAlternateArtProduct(admin, new(null, "常规卡测试产品"));
        var rarity = f.Store.SaveAlternateArtRarity(admin, new(null, "RG", "常规卡测试罕贵"));
        var media = Upload(f.Store, admin, "card-art");

        using var publicBefore = await Send(f, HttpMethod.Get, "/api/gallery/presentation", null);
        Assert.Equal(HttpStatusCode.OK, publicBefore.StatusCode);
        using var beforeJson = JsonDocument.Parse(await publicBefore.Content.ReadAsStringAsync());
        var card = beforeJson.RootElement.GetProperty("regularCards").EnumerateArray().First();
        var cardId = card.GetProperty("id").GetString()!;

        using var deniedSearch = await Send(f, HttpMethod.Get,
            "/api/admin/gallery/regular-cards/search?page=1&pageSize=20", f.PlayerToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSearch.StatusCode);
        using var search = await Send(f, HttpMethod.Get,
            $"/api/admin/gallery/regular-cards/search?cardCode={Uri.EscapeDataString(cardId)}&page=1&pageSize=20",
            f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var metadataBody = new { cardId, productIds = new[] { product.Id }, rarityIds = new[] { rarity.Id }, artist = "HTTP画师" };
        using var deniedSave = await Send(f, HttpMethod.Put, "/api/admin/gallery/regular-cards/metadata",
            f.PlayerToken, metadataBody);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSave.StatusCode);
        using var saved = await Send(f, HttpMethod.Put, "/api/admin/gallery/regular-cards/metadata",
            f.AdminToken, metadataBody);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var image = await Send(f, HttpMethod.Put, "/api/admin/gallery/regular-cards/image", f.AdminToken,
            new { cardId, mediaAssetId = media.Id });
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        using var hidden = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products/filter-visibility",
            f.AdminToken, new { productId = product.Id, showInGalleryFilter = false });
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);

        using var publicAfter = await Send(f, HttpMethod.Get, "/api/gallery/presentation", null);
        Assert.Equal(HttpStatusCode.OK, publicAfter.StatusCode);
        using var afterJson = JsonDocument.Parse(await publicAfter.Content.ReadAsStringAsync());
        var updated = afterJson.RootElement.GetProperty("regularCards").EnumerateArray()
            .Single(row => row.GetProperty("id").GetString() == cardId);
        Assert.Equal("regular-card", updated.GetProperty("kind").GetString());
        Assert.Equal("HTTP画师", updated.GetProperty("artist").GetString());
        Assert.Equal(media.DesktopUrl, updated.GetProperty("imageUrl").GetString());
        Assert.Contains(updated.GetProperty("products").EnumerateArray(),
            row => row.GetProperty("id").GetString() == product.Id);
        Assert.Contains(afterJson.RootElement.GetProperty("filterProducts").EnumerateArray(),
            row => row.GetProperty("id").GetString() == product.Id
                && !row.GetProperty("showInGalleryFilter").GetBoolean());
    }

    private static void AssertStableIdentity(L12AlternateArtView baseline, JsonElement actual)
    {
        Assert.Equal(baseline.Id, actual.GetProperty("id").GetString());
        Assert.Equal(baseline.ArtCode, actual.GetProperty("artCode").GetString());
        Assert.Equal(baseline.BaseCardId, actual.GetProperty("baseCardId").GetString());
        Assert.Equal(baseline.CardImageId, actual.GetProperty("cardImageId").GetString());
        Assert.Equal(baseline.DisplayName, actual.GetProperty("displayName").GetString());
        Assert.Equal(baseline.BuiltIn, actual.GetProperty("builtIn").GetBoolean());
        Assert.Equal(baseline.Kind, actual.GetProperty("kind").GetString());
        Assert.Equal(baseline.Effect, actual.GetProperty("effect").GetString());
        Assert.Equal((baseline.Products ?? []).Select(row => row.Id),
            actual.GetProperty("products").EnumerateArray().Select(row => row.GetProperty("id").GetString()));
        Assert.Equal((baseline.Rarities ?? []).Select(row => row.Id),
            actual.GetProperty("rarities").EnumerateArray().Select(row => row.GetProperty("id").GetString()));
    }

    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin, string kind)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(row => row.Kind == kind);
        var desktop = policy.FlexibleDimensions ? (997, 331) : (policy.DesktopWidth, policy.DesktopHeight);
        var mobile = policy.FlexibleDimensions ? (421, 777) : (policy.MobileWidth, policy.MobileHeight);
        var thumb = policy.FlexibleDimensions ? (137, 59) : (policy.ThumbnailWidth, policy.ThumbnailHeight);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, "synthetic.webp", "image/webp",
            Webp(desktop.Item1, desktop.Item2), Webp(desktop.Item1, desktop.Item2),
            Webp(mobile.Item1, mobile.Item2), Webp(thumb.Item1, thumb.Item2), "合成HTTP卡图", .5, .5));
    }

    // Existing media-test metadata fixture, not evidence of decoded browser pixels.
    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        var w = width - 1; var h = height - 1;
        bytes[24] = (byte)w; bytes[25] = (byte)(w >> 8); bytes[26] = (byte)(w >> 16);
        bytes[27] = (byte)h; bytes[28] = (byte)(h >> 8); bytes[29] = (byte)(h >> 16);
        return bytes;
    }

    private static async Task<HttpResponseMessage> Send(AdminWorkbenchTimingTests.Fixture f, HttpMethod method,
        string endpoint, string? token, object? body = null)
    {
        using var request = f.Request(token); request.Method = method; request.RequestUri = new Uri(endpoint, UriKind.Relative);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Client.SendAsync(request);
    }
}
