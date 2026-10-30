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

    [Theory]
    [InlineData("save")]
    [InlineData("public")]
    [InlineData("admin")]
    [InlineData("search")]
    public async Task StandaloneCardInformationViewsCannotMutateStoredTraits(string consumer)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var media = Upload(f.Store, actor, "card-art");
        var inputTraits = new[] { "人类", "英雄" };
        var saved = f.Store.SaveAlternateArt(actor, new(null, "COLLAB-ISOLATION", "", "资料隔离卡", media.Id,
            Kind: "gallery-card", CardInformation: new(CardType: "legion", Traits: inputTraits)));
        inputTraits[0] = "外部输入变更";
        var sibling = f.Store.AlternateArts().Single(row => row.Id == saved.Id);
        var mirror = File.ReadAllBytes(f.MirrorPath);
        Assert.Equal(new[] { "人类", "英雄" }, sibling.CardInformation!.Traits);
        var exposed = consumer switch
        {
            "save" => saved,
            "public" => f.Store.AlternateArts().Single(row => row.Id == saved.Id),
            "admin" => f.Store.AdminAlternateArts().Single(row => row.Id == saved.Id),
            _ => Assert.Single(f.Store.SearchAlternateArts(null, "COLLAB-ISOLATION", null).Items),
        };
        // Arrays advertise a fixed-size ICollection as read-only but still allow
        // index replacement, so do not mistake IsReadOnly for immutability.
        if (exposed.CardInformation!.Traits is string[] array) array[0] = "输出副本变更";
        else if (exposed.CardInformation.Traits is IList<string> { IsReadOnly: false } mutable)
            mutable[0] = "输出副本变更";
        Assert.Equal(new[] { "人类", "英雄" }, sibling.CardInformation.Traits);
        Assert.Equal(new[] { "人类", "英雄" }, f.Store.AlternateArts().Single(row => row.Id == saved.Id).CardInformation!.Traits);
        Assert.Equal(mirror, File.ReadAllBytes(f.MirrorPath));
        // A later unrelated legitimate save must not persist a caller's mutation.
        f.Store.SaveAlternateArtProduct(actor, new(null, "无关产品保存"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var reopened = new L12PlatformStore(f.MirrorPath, catalog.PresetDecks, officialCards: catalog.Cards,
            officialAlternateArts: catalog.OfficialAlternateArts);
        Assert.Equal(new[] { "人类", "英雄" }, reopened.AlternateArts().Single(row => row.Id == saved.Id).CardInformation!.Traits);
    }

    [Fact]
    public async Task StandaloneCardInformationIsOptionalRoundTripsAndCanBeCleared()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var media = Upload(f.Store, actor, "card-art");
        var information = new { cardType = "legion", faction = "联动阵营", cost = 0, troops = 5432, hp = 9,
            disasterLevel = 2, trialValue = 3, profession = "战士", traits = new[] { "人类", "英雄" }, deckLimit = 2, isCounterTactic = false };
        var body = new { artCode = "COLLAB-OPTIONAL", baseCardId = "", displayName = "选填联动卡", mediaAssetId = media.Id,
            kind = "gallery-card", effect = "", cardInformation = information };
        using var denied = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts", f.PlayerToken, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var created = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts", f.AdminToken, body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString()!;
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(information), json.RootElement.GetProperty("cardInformation")));
        using var publicly = JsonDocument.Parse(await f.Client.GetStringAsync("/api/alternate-arts"));
        Assert.All(publicly.RootElement.EnumerateArray().Where(row => row.GetProperty("builtIn").GetBoolean()),
            row => Assert.False(row.TryGetProperty("cardInformation", out _)));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(information), publicly.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == id).GetProperty("cardInformation")));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var reopened = new L12PlatformStore(f.MirrorPath, catalog.PresetDecks, officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
        Assert.Equal(5432, reopened.AlternateArts().Single(row => row.Id == id).CardInformation!.Troops);
        Assert.Equal(0, reopened.AlternateArts().Single(row => row.Id == id).CardInformation!.Cost);
        // A legacy client omitting the new object must preserve previously entered information.
        using var legacy = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts", f.AdminToken,
            new { id, artCode = body.artCode, baseCardId = "", body.displayName, mediaAssetId = media.Id, kind = "gallery-card", effect = "" });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        using var legacyJson = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(information), legacyJson.RootElement.GetProperty("cardInformation")));
        using var cleared = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts", f.AdminToken,
            new { id, artCode = body.artCode, baseCardId = "", body.displayName, mediaAssetId = media.Id, kind = "gallery-card", effect = "", cardInformation = new { } });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var clearJson = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        Assert.False(clearJson.RootElement.TryGetProperty("cardInformation", out _));
        // No optional information, effect, product, rarity, artist or collaboration is required.
        using var blank = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts", f.AdminToken,
            new { artCode = "COLLAB-BLANK", baseCardId = "", displayName = "空白资料", mediaAssetId = media.Id, kind = "gallery-card" });
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        using var blankJson = JsonDocument.Parse(await blank.Content.ReadAsStringAsync());
        Assert.False(blankJson.RootElement.TryGetProperty("cardInformation", out _));
        Assert.DoesNotContain(f.Store.OwnedAlternateArts(actor.Id), row => row.Id == id);
    }

    [Theory]
    [InlineData("cost")]
    [InlineData("troops")]
    [InlineData("hp")]
    [InlineData("disasterLevel")]
    [InlineData("trialValue")]
    [InlineData("deckLimit")]
    public async Task InvalidOptionalCardNumbersRejectWithoutCreatingEntries(string field)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var media = Upload(f.Store, actor, "card-art");
        var before = JsonSerializer.Serialize(f.Store.AdminAlternateArts());
        using var response = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts", f.AdminToken,
            new { artCode = "COLLAB-INVALID", baseCardId = "", displayName = "无效数值", mediaAssetId = media.Id,
                kind = "gallery-card", cardInformation = new Dictionary<string, int> { [field] = -1 } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, JsonSerializer.Serialize(f.Store.AdminAlternateArts()));
    }

    [Fact]
    public async Task ProductManagementAndOrderUseAuthenticatedRoutesAndKeepMetadataIndependent()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        foreach (var endpoint in new[] { "/api/admin/alternate-art-products", "/api/admin/alternate-art-products/filter-visibility" })
        {
            foreach (var (token, status) in new[] { ((string?)null, HttpStatusCode.Unauthorized),
                         (f.PlayerToken, HttpStatusCode.Forbidden), (f.RevokedToken, HttpStatusCode.Unauthorized) })
            {
                using var denied = await Send(f, HttpMethod.Put, endpoint, token,
                    new { name = "越权", productId = "catalog:missing", showInGalleryFilter = true, releaseOrder = 5 });
                Assert.Equal(status, denied.StatusCode);
            }
        }
        var art = f.Store.AdminAlternateArts().First(item => item.Products!.Count > 0);
        var product = art.Products![0];
        using var renamed = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products", f.AdminToken,
            new { id = product.Id, name = "HTTP产品名", active = true });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var ordered = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products/filter-visibility", f.AdminToken,
            new { productId = product.Id, showInGalleryFilter = true, releaseOrder = 7 });
        Assert.Equal(HttpStatusCode.OK, ordered.StatusCode);
        Assert.Equal(7, (await ordered.Content.ReadFromJsonAsync<L12AlternateArtProductView>())!.ReleaseOrder);
        using var invalid = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products/filter-visibility", f.AdminToken,
            new { productId = product.Id, showInGalleryFilter = false, releaseOrder = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var metadata = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts/metadata", f.AdminToken,
            new { alternateArtId = art.Id, productIds = new[] { product.Id }, rarityIds = Array.Empty<string>(), artist = "独立画师", collaboration = "HTTP联动" });
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        using var legacy = await Send(f, HttpMethod.Put, "/api/admin/alternate-arts/metadata", f.AdminToken,
            new { alternateArtId = art.Id, productIds = new[] { product.Id }, rarityIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var saved = (await legacy.Content.ReadFromJsonAsync<L12AlternateArtView>())!;
        Assert.Equal("HTTP联动", saved.Collaboration);
        Assert.Equal("独立画师", saved.Artist);
        var publicArts = await f.Client.GetFromJsonAsync<L12AlternateArtView[]>("/api/alternate-arts");
        var result = publicArts!.Single(item => item.Id == art.Id);
        Assert.Equal("HTTP联动", result.Collaboration);
        Assert.Equal("HTTP产品名", result.Products![0].Name);
        Assert.Equal(7, result.Products![0].ReleaseOrder);
    }

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
    public async Task NewGalleryProductUsesIndependentMetadataRouteAndAppearsWithoutMerchandisePublishing()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var art = f.Store.AlternateArts().First(row => row.BuiltIn);
        var articles = JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product"));
        var body = new { name = "合成画廊产品", active = true };
        using var denied = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products", f.PlayerToken, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var created = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products", f.AdminToken, body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var productId = createdJson.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("legacy:", productId);
        Assert.Equal("active", createdJson.RootElement.GetProperty("status").GetString());

        using var products = await Send(f, HttpMethod.Get, "/api/admin/alternate-art-products", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        using var productsJson = JsonDocument.Parse(await products.Content.ReadAsStringAsync());
        var choice = productsJson.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetString() == productId);
        Assert.Equal("active", choice.GetProperty("status").GetString());
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
        Assert.Contains(current.GetProperty("products").EnumerateArray(), row => row.GetProperty("id").GetString() == productId);
        Assert.Equal(articles, JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product")));
    }

    [Fact]
    public async Task GalleryProductMergeRequiresAdminAndLeavesMerchandiseUntouched()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var articles = JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product"));
        async Task<string> Create(string title)
        {
            using var response = await Send(f, HttpMethod.Put, "/api/admin/alternate-art-products", f.AdminToken,
                new { name = title, active = true });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("id").GetString()!;
        }

        var targetId = await Create("保留画廊产品");
        var sourceId = await Create("重复画廊产品");
        var body = new { targetProductId = targetId, sourceProductIds = new[] { sourceId } };
        using var denied = await Send(f, HttpMethod.Post, "/api/admin/alternate-art-products/merge", f.PlayerToken, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var merged = await Send(f, HttpMethod.Post, "/api/admin/alternate-art-products/merge", f.AdminToken, body);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        using var result = JsonDocument.Parse(await merged.Content.ReadAsStringAsync());
        Assert.Equal(targetId, result.RootElement.GetProperty("target").GetProperty("id").GetString());
        Assert.Equal(0, result.RootElement.GetProperty("archivedSiteProducts").GetInt32());
        Assert.Equal(articles, JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product")));
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

    [Fact]
    public async Task GrantHistorySearchIsServerFilteredPagedAndAuthorized()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var admin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var art = f.Store.AlternateArts().First(row => row.Active && row.Kind == "alternate-art");
        for (var index = 0; index < 23; index++)
        {
            var account = f.Store.Register($"grantuser{index:00}", "password-123").Account!;
            var grant = f.Store.GrantAlternateArt(admin, new(art.Id, account.Id,
                index % 2 == 0 ? "manual" : "event", $"批次-{index:00}"));
            if (index == 0) f.Store.RevokeAlternateArtGrant(admin, grant.Id);
        }

        const string endpoint = "/api/admin/alternate-art-grants/search?sourceKind=manual&state=active&page=2&pageSize=5";
        using var anonymous = await Send(f, HttpMethod.Get, endpoint, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var player = await Send(f, HttpMethod.Get, endpoint, f.PlayerToken);
        Assert.Equal(HttpStatusCode.Forbidden, player.StatusCode);
        using var response = await Send(f, HttpMethod.Get, endpoint, f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<L12AlternateArtGrantSearchPage>())!;
        Assert.Equal(11, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(5, page.Items.Count);
        Assert.All(page.Items, item =>
        {
            Assert.Equal("manual", item.SourceKind);
            Assert.Null(item.RevokedAt);
        });

        using var query = await Send(f, HttpMethod.Get,
            "/api/admin/alternate-art-grants/search?query=grantuser22&page=1&pageSize=20", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        var match = (await query.Content.ReadFromJsonAsync<L12AlternateArtGrantSearchPage>())!;
        Assert.Equal("grantuser22", Assert.Single(match.Items).Username);
        using var beyondLast = await Send(f, HttpMethod.Get,
            "/api/admin/alternate-art-grants/search?sourceKind=manual&state=active&page=2147483647&pageSize=5",
            f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, beyondLast.StatusCode);
        var last = (await beyondLast.Content.ReadFromJsonAsync<L12AlternateArtGrantSearchPage>())!;
        Assert.Equal(3, last.Page);
        Assert.Single(last.Items);
        using var invalid = await Send(f, HttpMethod.Get,
            "/api/admin/alternate-art-grants/search?state=unknown", f.AdminToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task OneCardArtUploadCanBackRegularAndAlternateImagesThroughTheSameMediaStore()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var admin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var media = Upload(f.Store, admin, "card-art");
        var alternate = f.Store.AlternateArts().First(row => row.BuiltIn);
        var regular = f.Store.GalleryPresentationCatalog().RegularCards.First();

        using var alternateResponse = await Send(f, HttpMethod.Put, ImageEndpoint, f.AdminToken,
            new { alternateArtId = alternate.Id, mediaAssetId = media.Id });
        using var regularResponse = await Send(f, HttpMethod.Put, "/api/admin/gallery/regular-cards/image", f.AdminToken,
            new { cardId = regular.Id, mediaAssetId = media.Id });
        Assert.Equal(HttpStatusCode.OK, alternateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, regularResponse.StatusCode);

        var savedAlternate = (await alternateResponse.Content.ReadFromJsonAsync<L12AlternateArtView>())!;
        var savedRegular = (await regularResponse.Content.ReadFromJsonAsync<L12AlternateArtView>())!;
        Assert.Equal(media.Id, savedAlternate.MediaAssetId);
        Assert.Equal(media.Id, savedRegular.MediaAssetId);
        Assert.Equal(media.DesktopUrl, savedAlternate.ImageUrl);
        Assert.Equal(media.DesktopUrl, savedRegular.ImageUrl);
        var stored = Assert.Single(f.Store.AdminSiteMedia("card-art").Where(item => item.Id == media.Id));
        Assert.Equal(2, stored.ReferenceCount);
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
