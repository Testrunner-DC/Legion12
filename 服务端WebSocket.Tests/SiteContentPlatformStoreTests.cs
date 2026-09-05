using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class SiteContentPlatformStoreTests
{
    [Fact]
    public void UploadedMediaUsesContentHashesAndCannotBeDeletedWhileAnyVersionReferencesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-content-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "hero");

            Assert.Equal(64, media.ContentHash.Length);
            Assert.Contains(media.ContentHash, media.DesktopUrl);
            var fileName = media.DesktopUrl.Split('/').Last();
            var file = store.ResolveSiteMediaFile(media.Id, "desktop", fileName);
            Assert.NotNull(file);
            Assert.True(File.Exists(file!.Path));

            var composition = JsonSerializer.Serialize(new
            {
                version = 1,
                heroSlides = new[] { new { id = "launch", title = "启航", summary = "测试", href = "/battle", mediaAssetId = media.Id, enabled = true } },
                notices = new[] { new { id = "notice", label = "公告", href = "/news", enabled = true } },
            });
            store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey, composition);
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));
            store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey);

            var reloaded = new L12PlatformStore(path);
            var home = reloaded.PublicSiteHome();
            Assert.Equal(media.Id, Assert.Single(home.Media).Id);
            Assert.Contains("launch", home.Composition);
            Assert.Throws<L12SiteContentConflictException>(() => reloaded.DeleteSiteMedia(admin, media.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProductsReuseArticlePublishingAndNonEmptyCategoryRequiresAtomicMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-category-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var source = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(null, "product", "限定商品", "limited", 90, true));
            var target = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(null, "product", "常规商品", "regular", 91, true));
            Assert.Throws<ArgumentException>(() => store.SaveSiteCategory(admin,
                new L12SiteCategoryDraft(null, "unknown", "错误分类", "invalid", 92, true)));
            var media = Upload(store, admin, "product");
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "测试商品", "摘要", "商品正文",
                source.Name, "", "/cards", "test-product", false, null, Kind: "product",
                CategoryId: source.Id, MediaAssetId: media.Id, SortOrder: 4));
            store.PublishArticle(admin, draft.Id);

            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteCategory(admin, source.Id, null));
            store.DeleteSiteCategory(admin, source.Id, target.Id);
            var published = Assert.Single(store.PublicArticles(kind: "product"));
            Assert.Equal(target.Id, published.CategoryId);
            Assert.Equal(target.Name, published.Category);
            Assert.DoesNotContain(store.AdminSiteCategories("product"), item => item.Id == source.Id);

            var renamed = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(target.Id, target.Kind,
                "常规商品（更新）", target.Slug, target.SortOrder, true, target.Version));
            Assert.Equal(renamed.Name, Assert.Single(store.PublicArticles(kind: "product")).Category);
            Assert.All(store.ArticleRevisions(draft.Id), revision => Assert.Equal(renamed.Name, revision.Category));

            var disabled = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(renamed.Id, renamed.Kind,
                renamed.Name, renamed.Slug, renamed.SortOrder, false, renamed.Version));
            Assert.False(disabled.Active);
            var changed = store.SaveArticleDraft(admin, new L12ArticleDraft(draft.Id, "测试商品", "摘要", "修改正文",
                target.Name, "", "/cards", "test-product", false, null, published.Revision, "product",
                target.Id, media.Id, 4));
            Assert.Throws<ArgumentException>(() => store.PublishArticle(admin, changed.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UploadRejectsActiveFormatsAndWrongDerivativeDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-upload-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            var invalidOriginal = new L12SiteMediaUpload("news", "cover.svg", "image/svg+xml",
                Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>"),
                Webp(policy.DesktopWidth, policy.DesktopHeight), Webp(policy.MobileWidth, policy.MobileHeight),
                Webp(policy.ThumbnailWidth, policy.ThumbnailHeight), "封面", .5, .5);
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, invalidOriginal));

            var wrongDesktop = invalidOriginal with
            {
                OriginalFileName = "cover.webp", OriginalContentType = "image/webp",
                Original = Webp(800, 600), DesktopWebp = Webp(800, 600),
            };
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, wrongDesktop));

            var metadataDesktop = wrongDesktop with
            {
                DesktopWebp = WebpWithMetadata(policy.DesktopWidth, policy.DesktopHeight),
            };
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, metadataDesktop));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SiteContentHttpMutationsRejectPlayerTokensAndAcceptAdminPermission()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-http-{Guid.NewGuid():N}");
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
            var player = store.Register("SitePlayer", "password-123");
            var admin = store.Login("Admin", "L12master");

            using (var request = Authorized(HttpMethod.Post, "/api/admin/articles", player.Token!,
                       new { title = "越权稿件", body = "不应保存", category = "官方公告", kind = "news" }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/site/categories", player.Token!,
                       new { kind = "news", name = "越权分类", slug = "forbidden", sortOrder = 90, active = true }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/site/media", player.Token!, null))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/site/categories", admin.Token!,
                       new { kind = "news", name = "HTTP 分类", slug = "http-category", sortOrder = 90, active = true }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin, string kind)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == kind);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, $"{kind}.webp", "image/webp",
            Webp(policy.DesktopWidth, policy.DesktopHeight), Webp(policy.DesktopWidth, policy.DesktopHeight),
            Webp(policy.MobileWidth, policy.MobileHeight), Webp(policy.ThumbnailWidth, policy.ThumbnailHeight),
            $"{policy.Label}测试图", .5, .5));
    }

    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        var encodedWidth = width - 1; var encodedHeight = height - 1;
        bytes[24] = (byte)encodedWidth; bytes[25] = (byte)(encodedWidth >> 8); bytes[26] = (byte)(encodedWidth >> 16);
        bytes[27] = (byte)encodedHeight; bytes[28] = (byte)(encodedHeight >> 8); bytes[29] = (byte)(encodedHeight >> 16);
        return bytes;
    }

    private static byte[] WebpWithMetadata(int width, int height)
    {
        var source = Webp(width, height);
        var bytes = new byte[source.Length + 8];
        source.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        Encoding.ASCII.GetBytes("EXIF").CopyTo(bytes, source.Length);
        return bytes;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, $"site-{Guid.NewGuid():N}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
