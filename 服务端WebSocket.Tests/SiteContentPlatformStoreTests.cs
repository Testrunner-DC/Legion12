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

            var noticeArticle = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "首页通知资讯", "摘要", "正文",
                "官方公告", "", "", "home-notice", false, null));
            store.PublishArticle(admin, noticeArticle.Id);

            var composition = JsonSerializer.Serialize(new
            {
                version = 1,
                heroSlides = new[] { new { id = "launch", eyebrow = "STC-01", title = "启航", summary = "第一行\n第二行", footer = "2026.09.05 发布", href = "/battle", mediaAssetId = media.Id, enabled = true } },
                notices = new[] { new { id = "notice", label = "公告", href = $"/news#article-{noticeArticle.Id}", enabled = true } },
            });
            store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey, composition);
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));
            store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey);

            var reloaded = new L12PlatformStore(path);
            var home = reloaded.PublicSiteHome();
            Assert.Equal(media.Id, Assert.Single(home.Media).Id);
            Assert.Contains("launch", home.Composition);
            using (var homeJson = JsonDocument.Parse(home.Composition))
            {
                var slide = homeJson.RootElement.GetProperty("heroSlides")[0];
                Assert.Equal("第一行\n第二行", slide.GetProperty("summary").GetString());
                Assert.Equal("2026.09.05 发布", slide.GetProperty("footer").GetString());
            }
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
            var policies = L12PlatformStore.SiteMediaPolicies();
            var videoPolicy = policies.Single(item => item.Kind == "video");
            Assert.Equal((1280, 720, 1280, 720, 480, 270), (videoPolicy.DesktopWidth, videoPolicy.DesktopHeight,
                videoPolicy.MobileWidth, videoPolicy.MobileHeight, videoPolicy.ThumbnailWidth, videoPolicy.ThumbnailHeight));
            var productPolicy = policies.Single(item => item.Kind == "product");
            Assert.Equal((1600, 1200, 1200, 900, 480, 360), (productPolicy.DesktopWidth, productPolicy.DesktopHeight,
                productPolicy.MobileWidth, productPolicy.MobileHeight, productPolicy.ThumbnailWidth, productPolicy.ThumbnailHeight));
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            Assert.Equal((1600, 900, 1280, 720, 480, 270), (policy.DesktopWidth, policy.DesktopHeight,
                policy.MobileWidth, policy.MobileHeight, policy.ThumbnailWidth, policy.ThumbnailHeight));
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
    public void StructuredNewsBodyUsesWhitelistEmbedsMediaAndKeepsPlainTextCompatible()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-article-blocks-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "article");
            var body = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new object[]
                {
                    new { id = "heading", type = "h2", text = "正式标题", marks = Array.Empty<object>() },
                    new { id = "paragraph", type = "paragraph", text = "访问官网", marks = new[] { new { type = "bold", from = 0, to = 2, href = (string?)null }, new { type = "link", from = 2, to = 4, href = (string?)"https://example.com" } } },
                    new { id = "image", type = "image", mediaAssetId = media.Id, alt = "正文插图内容", caption = "图片说明" },
                },
            });
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "结构化资讯", "摘要", body,
                "官方公告", "", "", "structured-news", false, null));
            Assert.Equal(media.Id, Assert.Single(draft.BodyMedia!).Id);
            var published = store.PublishArticle(admin, draft.Id);
            Assert.Equal(media.Id, Assert.Single(published.BodyMedia!).Id);
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));

            var unsafeLink = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "unsafe", type = "paragraph", text = "危险链接", marks = new[] { new { type = "link", from = 0, to = 4, href = "javascript:alert(1)" } } } },
            });
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法链接", "", unsafeLink, "官方公告", "", "", "unsafe-link", false, null)));
            var htmlBlock = "{\"format\":\"l12-blocks\",\"version\":1,\"blocks\":[{\"id\":\"html\",\"type\":\"html\",\"text\":\"<script>alert(1)</script>\",\"marks\":[]}]}";
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法 HTML 块", "", htmlBlock, "官方公告", "", "", "unsafe-html", false, null)));

            var legacy = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "旧正文", "", "第一段\n第二行",
                "官方公告", "", "", "legacy-body", false, null));
            Assert.Equal("第一段\n第二行", store.PublishArticle(admin, legacy.Id).Body);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void VideoDraftKeepsOnlyCoverTitleAuthorAndLinkWhileLegacyAuthorMayStayEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-video-model-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("video").First();
            var media = Upload(store, admin, "video");
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "视频标题", "应被清空的摘要", "应被清空的正文",
                category.Name, "", "https://example.com/video", "ignored-video-slug", false, null,
                Kind: "video", CategoryId: category.Id, MediaAssetId: media.Id));
            Assert.Empty(draft.Summary);
            Assert.Empty(draft.Body);
            Assert.Throws<ArgumentException>(() => store.PublishArticle(admin, draft.Id));

            var authored = store.SaveArticleDraft(admin, new L12ArticleDraft(draft.Id, draft.Title, "仍会清空", "仍会清空",
                category.Name, "", draft.Link, draft.Slug, false, null, draft.Revision, "video", category.Id,
                media.Id, VideoAuthorName: "十二军团频道"));
            var published = store.PublishArticle(admin, authored.Id);
            Assert.Equal("十二军团频道", published.VideoAuthorName);
            Assert.Empty(published.Summary);
            Assert.Empty(published.Body);
            Assert.Equal("https://example.com/video", published.Link);

            var legacy = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "旧视频", "", "", category.Name,
                "", "/legacy-video", "legacy-video", false, null, Kind: "video", CategoryId: category.Id,
                MediaAssetId: media.Id));
            MarkVideoAuthorOptionalForLegacyFixture(store, legacy.Id);
            Assert.Empty(store.PublishArticle(admin, legacy.Id).VideoAuthorName);
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法作者", "", "", category.Name, "", "/bad-author", "bad-author", false, null,
                Kind: "video", CategoryId: category.Id, MediaAssetId: media.Id, VideoAuthorName: "作者\n伪造")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PublicContentUsesScheduledTimeAndPinnedThenNewestStableOrdering()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-publish-order-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var olderAt = DateTimeOffset.UtcNow.AddDays(-2);
            var newerAt = DateTimeOffset.UtcNow.AddDays(-1);
            var pinned = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "置顶旧稿", "", "正文", "官方公告",
                "", "", "pinned-old", true, olderAt));
            var older = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "普通旧稿", "", "正文", "官方公告",
                "", "", "normal-old", false, olderAt));
            var newer = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "普通新稿", "", "正文", "官方公告",
                "", "", "normal-new", false, newerAt));
            store.PublishArticle(admin, older.Id);
            store.PublishArticle(admin, newer.Id);
            store.PublishArticle(admin, pinned.Id);
            Assert.Equal(new[] { "置顶旧稿", "普通新稿", "普通旧稿" },
                store.PublicArticles(kind: "news").Select(item => item.Title).ToArray());

            var dueAt = DateTimeOffset.UtcNow.AddMilliseconds(350);
            var due = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "到点公开", "", "正文", "官方公告",
                "", "", "scheduled-due", false, dueAt));
            var scheduled = store.PublishArticle(admin, due.Id);
            Assert.Equal("scheduled", scheduled.Status);
            Assert.Equal(new[] { "置顶旧稿", "到点公开", "普通新稿", "普通旧稿" },
                store.AdminArticles(kind: "news").Select(item => item.Title).ToArray());
            Assert.DoesNotContain(store.PublicArticles(kind: "news"), item => item.Id == due.Id);
            Thread.Sleep(550);
            Assert.Equal(new[] { "置顶旧稿", "到点公开", "普通新稿", "普通旧稿" },
                store.PublicArticles(kind: "news").Select(item => item.Title).ToArray());
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

            var newsPolicy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Webp(1600, 900),
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight))))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Encoding.UTF8.GetBytes("<svg><script/></svg>"),
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight), originalName: "bad.svg", originalType: "image/svg+xml")))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Webp(1600, 900),
                       Webp(800, 600), Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight))))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var oversizedOriginal = Pad(Webp(1600, 900), L12PlatformStore.SiteMediaOriginalMaxBytes + 1);
            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", oversizedOriginal,
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight))))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("media_upload_too_large", payload.GetProperty("code").GetString());
                Assert.Contains("16MB", payload.GetProperty("message").GetString());
            }

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Webp(1600, 900),
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight),
                       extra: new byte[checked((int)L12PlatformStore.SiteMediaRequestMaxBytes)])))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("media_upload_too_large", payload.GetProperty("code").GetString());
                Assert.Contains("32MB", payload.GetProperty("message").GetString());
            }
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

    private static byte[] Pad(byte[] source, long length)
    {
        var bytes = new byte[checked((int)length)];
        source.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent MediaForm(string kind, byte[] original, byte[] desktop,
        byte[] mobile, byte[] thumbnail, string originalName = "cover.webp", string originalType = "image/webp",
        byte[]? extra = null)
    {
        var form = new MultipartFormDataContent($"l12-{Guid.NewGuid():N}");
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent("HTTP 上传测试图"), "altText");
        form.Add(new StringContent("0.5"), "focalX");
        form.Add(new StringContent("0.5"), "focalY");
        AddFile(form, original, "original", originalName, originalType);
        AddFile(form, desktop, "desktop", "desktop.webp", "image/webp");
        AddFile(form, mobile, "mobile", "mobile.webp", "image/webp");
        AddFile(form, thumbnail, "thumbnail", "thumbnail.webp", "image/webp");
        if (extra is not null) AddFile(form, extra, "unused", "oversized.bin", "application/octet-stream");
        return form;
    }

    private static void AddFile(MultipartFormDataContent form, byte[] bytes, string name, string fileName,
        string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(content, name, fileName);
    }

    private static HttpRequestMessage AuthorizedMedia(string token, MultipartFormDataContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/site/media") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, $"site-media-{Guid.NewGuid():N}");
        if (content.Headers.ContentLength > L12PlatformStore.SiteMediaRequestMaxBytes)
            request.Headers.ExpectContinue = true;
        return request;
    }

    private static void MarkVideoAuthorOptionalForLegacyFixture(L12PlatformStore store, string id)
    {
        var data = typeof(L12PlatformStore).GetField("_data", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        var rows = (System.Collections.IEnumerable)data.GetType().GetProperty("Articles")!.GetValue(data)!;
        foreach (var row in rows)
        {
            if (!string.Equals((string?)row.GetType().GetProperty("Id")!.GetValue(row), id, StringComparison.Ordinal)) continue;
            row.GetType().GetProperty("VideoAuthorRequired")!.SetValue(row, false);
            return;
        }
        throw new InvalidOperationException("Legacy video fixture was not found.");
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
