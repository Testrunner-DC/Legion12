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
            Assert.True(media.IndependentVariants);
            Assert.NotEqual(media.ContentHash, media.DesktopUrl.Split('/').Last().Replace(".webp", ""));
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

            var articlePolicy = policies.Single(item => item.Kind == "article");
            Assert.True(articlePolicy.FlexibleDimensions);
            var flexible = store.UploadSiteMedia(admin, new L12SiteMediaUpload("article", "inline.webp", "image/webp",
                Webp(997, 331), Webp(997, 331), Webp(421, 777), Webp(137, 59), "任意比例正文插图", .5, .5));
            Assert.Equal((997, 331, 421, 777, 137, 59), (flexible.DesktopWidth, flexible.DesktopHeight,
                flexible.MobileWidth, flexible.MobileHeight, flexible.ThumbnailWidth, flexible.ThumbnailHeight));

        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IccExifOrientationOriginalIsAcceptedAndDeliveryMetadataIsStrippedByRiffChunks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-metadata-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            var original = Convert.FromBase64String(RealJpegWithIccAndExifOrientation6);
            Assert.True(ContainsBytes(original, "ICC_PROFILE"u8));
            Assert.True(ContainsBytes(original, "Exif"u8));
            var desktopWithMetadata = WebpWithMetadata(policy.DesktopWidth, policy.DesktopHeight);
            var mobileWithPayloadMarkers = WebpWithImagePayloadMarkers(policy.MobileWidth, policy.MobileHeight);
            var media = store.UploadSiteMedia(admin, new L12SiteMediaUpload("news", "orientation-6.jpg", "image/jpeg",
                original, desktopWithMetadata, mobileWithPayloadMarkers,
                Webp(policy.ThumbnailWidth, policy.ThumbnailHeight), "带方向与色彩配置的原图", .5, .5));

            Assert.Equal("image/jpeg", media.OriginalFormat);
            var desktop = ReadMedia(store, media, "desktop", media.DesktopUrl);
            Assert.DoesNotContain("ICCP", WebpChunkTypes(desktop));
            Assert.DoesNotContain("EXIF", WebpChunkTypes(desktop));
            Assert.DoesNotContain("XMP ", WebpChunkTypes(desktop));
            Assert.Equal(0, desktop[20] & 0x2e);
            Assert.True(desktop.Length < desktopWithMetadata.Length);

            var mobile = ReadMedia(store, media, "mobile", media.MobileUrl);
            Assert.Contains("VP8 ", WebpChunkTypes(mobile));
            Assert.True(ContainsBytes(mobile, "pixel-EXIF-ICCP-XMP -bytes"u8));
            Assert.DoesNotContain("EXIF", WebpChunkTypes(mobile));
            Assert.DoesNotContain("ICCP", WebpChunkTypes(mobile));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HeroRequiresThreeIndependentVariantsAndCreatesOneAtomicMediaGroup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-hero-group-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "hero");
            var legacyStyleUpload = new L12SiteMediaUpload("hero", "desktop.png", "image/png",
                PngSignature(), Webp(policy.DesktopWidth, policy.DesktopHeight),
                Webp(policy.MobileWidth, policy.MobileHeight), Webp(policy.ThumbnailWidth, policy.ThumbnailHeight),
                "同一原图", .5, .5);
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, legacyStyleUpload));
            Assert.Empty(store.AdminSiteMedia("hero"));

            var invalidThirdVariant = legacyStyleUpload with
            {
                IndependentVariants = true,
                DesktopAltText = "桌面独立构图", MobileAltText = "移动独立构图", ThumbnailAltText = "缩略独立构图",
                ThumbnailWebp = Webp(320, 180),
            };
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, invalidThirdVariant));
            Assert.Empty(store.AdminSiteMedia("hero"));
            Assert.False(Directory.Exists(Path.Combine(root, "site-media")) &&
                Directory.EnumerateFiles(Path.Combine(root, "site-media")).Any());

            var upload = invalidThirdVariant with
            {
                ThumbnailWebp = Webp(policy.ThumbnailWidth, policy.ThumbnailHeight),
            };
            var media = store.UploadSiteMedia(admin, upload);
            Assert.True(media.IndependentVariants);
            Assert.Equal("桌面独立构图", media.DesktopAltText);
            Assert.Equal("移动独立构图", media.MobileAltText);
            Assert.Equal("缩略独立构图", media.ThumbnailAltText);
            Assert.Equal(media.Id, Assert.Single(store.AdminSiteMedia("hero")).Id);
            Assert.NotNull(store.ResolveSiteMediaFile(media.Id, "desktop", media.DesktopUrl.Split('/').Last()));
            Assert.NotNull(store.ResolveSiteMediaFile(media.Id, "mobile", media.MobileUrl.Split('/').Last()));
            Assert.NotNull(store.ResolveSiteMediaFile(media.Id, "thumbnail", media.ThumbnailUrl.Split('/').Last()));
            store.DeleteSiteMedia(admin, media.Id);
            Assert.Empty(store.AdminSiteMedia("hero"));
            Assert.All(Directory.EnumerateFiles(Path.Combine(root, "site-media")), file => Assert.DoesNotContain(".upload", file));
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
                    new { id = "heading", type = "h2", text = "正式标题", align = "center", marks = Array.Empty<object>() },
                    new { id = "paragraph", type = "paragraph", text = "访问官网", align = "justify", marks = new[] { new { type = "bold", from = 0, to = 2, href = (string?)null }, new { type = "underline", from = 0, to = 2, href = (string?)null }, new { type = "strikethrough", from = 1, to = 2, href = (string?)null }, new { type = "link", from = 2, to = 4, href = (string?)"https://example.com" } } },
                    new { id = "divider", type = "divider" },
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
            var invalidAlign = "{\"format\":\"l12-blocks\",\"version\":1,\"blocks\":[{\"id\":\"align\",\"type\":\"paragraph\",\"text\":\"正文\",\"align\":\"absolute\",\"marks\":[]}]}";
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法对齐", "", invalidAlign, "官方公告", "", "", "unsafe-align", false, null)));
            var htmlBlock = "{\"format\":\"l12-blocks\",\"version\":1,\"blocks\":[{\"id\":\"html\",\"type\":\"html\",\"text\":\"<script>alert(1)</script>\",\"marks\":[]}]}";
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法 HTML 块", "", htmlBlock, "官方公告", "", "", "unsafe-html", false, null)));

            var blankIdBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "   ", type = "paragraph", text = "正文", align = "left", marks = Array.Empty<object>() } },
            });
            var blankIdError = Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "空块标识", "", blankIdBody, "官方公告", "", "", "blank-block-id", false, null)));
            Assert.Equal("正文内容块缺少标识或标识为空", blankIdError.Message);

            var longIdBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = new string('x', 81), type = "h3", text = "小标题", align = "left", marks = Array.Empty<object>() } },
            });
            var longIdError = Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "超长块标识", "", longIdBody, "官方公告", "", "", "long-block-id", false, null)));
            Assert.Equal("正文内容块标识不能超过 80 个字符", longIdError.Message);

            var duplicateIdBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new object[]
                {
                    new { id = "same", type = "paragraph", text = "第一段", align = "left", marks = Array.Empty<object>() },
                    new { id = "same", type = "divider" },
                },
            });
            var duplicateIdError = Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "重复块标识", "", duplicateIdBody, "官方公告", "", "", "duplicate-block-id", false, null)));
            Assert.Equal("正文内容块标识不能重复：same", duplicateIdError.Message);

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

            var heroPolicy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "hero");
            using (var request = AuthorizedMedia(admin.Token!, MediaForm("hero", PngSignature(),
                       Webp(heroPolicy.DesktopWidth, heroPolicy.DesktopHeight),
                       Webp(heroPolicy.MobileWidth, heroPolicy.MobileHeight),
                       Webp(heroPolicy.ThumbnailWidth, heroPolicy.ThumbnailHeight), originalName: "hero.png",
                       originalType: "image/png", independentVariants: true,
                       variantAltTexts: ["桌面API构图", "移动API构图", "缩略API构图"])))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<L12SiteMediaView>();
                Assert.True(payload!.IndependentVariants);
                Assert.Equal("移动API构图", payload.MobileAltText);
            }

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("hero", PngSignature(),
                       Webp(heroPolicy.DesktopWidth, heroPolicy.DesktopHeight),
                       Webp(heroPolicy.MobileWidth, heroPolicy.MobileHeight),
                       Webp(heroPolicy.ThumbnailWidth, heroPolicy.ThumbnailHeight), originalName: "legacy-hero.png",
                       originalType: "image/png")))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

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
        var desktop = policy.FlexibleDimensions ? (Width: 997, Height: 331) : (Width: policy.DesktopWidth, Height: policy.DesktopHeight);
        var mobile = policy.FlexibleDimensions ? (Width: 421, Height: 777) : (Width: policy.MobileWidth, Height: policy.MobileHeight);
        var thumbnail = policy.FlexibleDimensions ? (Width: 137, Height: 59) : (Width: policy.ThumbnailWidth, Height: policy.ThumbnailHeight);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, $"{kind}.webp", "image/webp",
            Webp(desktop.Width, desktop.Height), Webp(desktop.Width, desktop.Height),
            Webp(mobile.Width, mobile.Height), Webp(thumbnail.Width, thumbnail.Height),
            $"{policy.Label}测试图", .5, .5, $"{policy.Label}桌面测试图", $"{policy.Label}移动测试图",
            $"{policy.Label}缩略测试图", kind == "hero"));
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
        var bytes = AppendWebpChunk(Webp(width, height), "ICCP", Encoding.ASCII.GetBytes("ICC_PROFILE\0sRGB test profile"));
        bytes = AppendWebpChunk(bytes, "EXIF", ExifOrientation6());
        bytes = AppendWebpChunk(bytes, "XMP ", Encoding.UTF8.GetBytes("<x:xmpmeta>test</x:xmpmeta>"));
        bytes[20] |= 0x2c;
        return bytes;
    }

    private static byte[] WebpWithImagePayloadMarkers(int width, int height)
        => AppendWebpChunk(Webp(width, height), "VP8 ", Encoding.ASCII.GetBytes("pixel-EXIF-ICCP-XMP -bytes"));

    private static byte[] AppendWebpChunk(byte[] source, string type, byte[] payload)
    {
        var paddedLength = payload.Length + (payload.Length & 1);
        var bytes = new byte[source.Length + 8 + paddedLength];
        source.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, source.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(source.Length + 4, 4), (uint)payload.Length);
        payload.CopyTo(bytes, source.Length + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        return bytes;
    }

    private static byte[] ExifOrientation6()
    {
        var bytes = new byte[32];
        Encoding.ASCII.GetBytes("Exif\0\0II").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24, 2), 6);
        return bytes;
    }

    private static IReadOnlyList<string> WebpChunkTypes(byte[] bytes)
    {
        var result = new List<string>();
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            result.Add(Encoding.ASCII.GetString(bytes, offset, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset = checked(offset + 8 + (int)length + ((int)length & 1));
        }
        return result;
    }

    private static byte[] ReadMedia(L12PlatformStore store, L12SiteMediaView media, string variant, string url)
    {
        var file = store.ResolveSiteMediaFile(media.Id, variant, url.Split('/').Last());
        Assert.NotNull(file);
        return File.ReadAllBytes(file!.Path);
    }

    private static bool ContainsBytes(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value)
        => source.IndexOf(value) >= 0;

    private static byte[] PngSignature() => [137, 80, 78, 71, 13, 10, 26, 10];

    // 由 sharp 0.35 生成并复读确认：3×2 JPEG，含标准 sRGB ICC 与 EXIF Orientation=6。
    private const string RealJpegWithIccAndExifOrientation6 = "/9j/4QC8RXhpZgAASUkqAAgAAAAGABIBAwABAAAABgAAABoBBQABAAAAVgAAABsBBQABAAAAXgAAACgBAwABAAAAAgAAABMCAwABAAAAAQAAAGmHBAABAAAAZgAAAAAAAAA4YwAA6AMAADhjAADoAwAABgAAkAcABAAAADAyMTABkQcABAAAAAECAwAAoAcABAAAADAxMDABoAMAAQAAAP//AAACoAQAAQAAAAMAAAADoAQAAQAAAAIAAAAAAAAA/+IB8ElDQ19QUk9GSUxFAAEBAAAB4GxjbXMEIAAAbW50clJHQiBYWVogB+IAAwAUAAkADgAdYWNzcE1TRlQAAAAAc2F3c2N0cmwAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1oYW5keem/Vlo+AbaDI4VVRvdPqgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKZGVzYwAAAPwAAAAkY3BydAAAASAAAAAid3RwdAAAAUQAAAAUY2hhZAAAAVgAAAAsclhZWgAAAYQAAAAUZ1hZWgAAAZgAAAAUYlhZWgAAAawAAAAUclRSQwAAAcAAAAAgZ1RSQwAAAcAAAAAgYlRSQwAAAcAAAAAgbWx1YwAAAAAAAAABAAAADGVuVVMAAAAIAAAAHABzAFIARwBCbWx1YwAAAAAAAAABAAAADGVuVVMAAAAGAAAAHABDAEMAMAAAWFlaIAAAAAAAAPbWAAEAAAAA0y1zZjMyAAAAAAABDD8AAAXd///zJgAAB5AAAP2S///7of///aIAAAPcAADAcVhZWiAAAAAAAABvoAAAOPIAAAOPWFlaIAAAAAAAAGKWAAC3iQAAGNpYWVogAAAAAAAAJKAAAA+FAAC2xHBhcmEAAAAAAAMAAAACZmkAAPKnAAANWQAAE9AAAApb/9sAQwADAgIDAgIDAwMDBAMDBAUIBQUEBAUKBwcGCAwKDAwLCgsLDQ4SEA0OEQ4LCxAWEBETFBUVFQwPFxgWFBgSFBUU/9sAQwEDBAQFBAUJBQUJFA0LDRQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU/8AAEQgAAgADAwEiAAIRAQMRAf/EABUAAQEAAAAAAAAAAAAAAAAAAAAH/8QAHhAAAQQBBQAAAAAAAAAAAAAAAwABAgQFBgcUITH/xAAVAQEBAAAAAAAAAAAAAAAAAAADB//EAB4RAAEBCQAAAAAAAAAAAAAAAAABAgMEBTI0cXKy/9oADAMBAAIRAxEAPwCM7pVAUdXMCsEdcEMdjmgIUWjGLcIHjN0yIiZ3QmCuSuwh9GeUP//Z";

    private static byte[] Pad(byte[] source, long length)
    {
        var bytes = new byte[checked((int)length)];
        source.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent MediaForm(string kind, byte[] original, byte[] desktop,
        byte[] mobile, byte[] thumbnail, string originalName = "cover.webp", string originalType = "image/webp",
        byte[]? extra = null, bool independentVariants = false, string[]? variantAltTexts = null)
    {
        var form = new MultipartFormDataContent($"l12-{Guid.NewGuid():N}");
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent("HTTP 上传测试图"), "altText");
        form.Add(new StringContent((variantAltTexts?.ElementAtOrDefault(0) ?? "HTTP 上传测试图")), "desktopAltText");
        form.Add(new StringContent((variantAltTexts?.ElementAtOrDefault(1) ?? "HTTP 上传测试图")), "mobileAltText");
        form.Add(new StringContent((variantAltTexts?.ElementAtOrDefault(2) ?? "HTTP 上传测试图")), "thumbnailAltText");
        form.Add(new StringContent(independentVariants.ToString()), "independentVariants");
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
