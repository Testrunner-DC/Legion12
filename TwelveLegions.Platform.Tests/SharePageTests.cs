using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class SharePageTests
{
    [Fact]
    public void HomeMetadataUsesPublicBaseAndDefaultSquareImage()
    {
        var root = TempRoot("home");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var metadata = L12SharePage.Metadata(store, "/testrun/?from=wechat",
                "https://legion-12.com/testrun");
            Assert.Equal("十二军团", metadata.Title);
            Assert.Equal("十二军团服务器权威制网页对战版", metadata.Description);
            Assert.Equal("https://legion-12.com/testrun/favicon.png", metadata.ImageUrl);
            Assert.Equal("https://legion-12.com/testrun/", metadata.PageUrl);
            Assert.Equal("website", metadata.Type);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PublishedArticleUsesSnapshotBodyAndCoverWithoutLeakingDraftChanges()
    {
        var root = TempRoot("article");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "news");
            var category = store.AdminSiteCategories("news").First();
            var longText = string.Concat(Enumerable.Repeat("正文内容", 30));
            var body = JsonSerializer.Serialize(new
            {
                format = "l12-blocks",
                version = 1,
                blocks = new object[]
                {
                    new { id = "p1", type = "h2", text = "分享标题段", align = "left", marks = Array.Empty<object>() },
                    new { id = "p2", type = "paragraph", text = longText, align = "left", marks = Array.Empty<object>() },
                },
            });
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "发布标题 <测试>", "摘要不应优先",
                body, category.Name, "", "", "share-article", false, null, Kind: "news",
                CategoryId: category.Id, MediaAssetId: media.Id));
            var published = store.PublishArticle(admin, draft.Id);

            var changed = store.SaveArticleDraft(admin, new L12ArticleDraft(draft.Id, "未发布标题", "未发布摘要",
                "未发布正文", category.Name, "", "", draft.Slug, false, null, published.Revision,
                "news", category.Id, media.Id));
            Assert.True(changed.HasUnpublishedChanges);

            var metadata = L12SharePage.Metadata(store, $"/news/{draft.Id}?cache=2", "https://legion-12.com");
            Assert.Equal("发布标题 <测试>", metadata.Title);
            Assert.StartsWith("分享标题段 正文内容", metadata.Description);
            Assert.EndsWith("…", metadata.Description);
            Assert.DoesNotContain("未发布", metadata.Description);
            Assert.Equal($"https://legion-12.com/news/{draft.Id}", metadata.PageUrl);
            Assert.StartsWith("https://legion-12.com/api/site/media/", metadata.ImageUrl);
            Assert.Equal("article", metadata.Type);

            var template = "<html><head><!-- l12-share-meta:start --><title>旧</title><!-- l12-share-meta:end --></head></html>";
            var html = L12SharePage.Render(template, metadata);
            Assert.Contains("<title>发布标题 &lt;测试&gt;</title>", html);
            Assert.Contains("property=\"og:description\"", html);
            Assert.Contains("rel=\"canonical\"", html);
            Assert.DoesNotContain("<title>旧</title>", html);

            store.PublishArticle(admin, changed.Id);
            var refreshed = L12SharePage.Metadata(store, $"/news/{draft.Id}", "https://legion-12.com");
            Assert.Equal("未发布标题", refreshed.Title);
            Assert.Equal("未发布正文", refreshed.Description);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ArticleWithoutCoverUsesFirstBodyImageThumbnail()
    {
        var root = TempRoot("body-image");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "article");
            var body = JsonSerializer.Serialize(new
            {
                format = "l12-blocks",
                version = 1,
                blocks = new object[]
                {
                    new { id = "image", type = "image", mediaAssetId = media.Id, alt = "正文首图", caption = "" },
                    new { id = "text", type = "paragraph", text = "正文说明", align = "left", marks = Array.Empty<object>() },
                },
            });
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "正文图资讯", "", body,
                "官方公告", "", "", "body-image-share", false, null));
            store.PublishArticle(admin, draft.Id);

            var metadata = L12SharePage.Metadata(store, $"/news/{draft.Id}", "https://legion-12.com");
            Assert.Contains($"/api/site/media/{media.Id}/desktop/", metadata.ImageUrl);
            Assert.Equal("正文说明", metadata.Description);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DraftWithdrawnAndUnknownArticlesFallBackWithoutLeakingContent()
    {
        var root = TempRoot("private");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "私有标题", "私有摘要", "私有正文",
                "官方公告", "", "", "private-share", false, null));
            var draftMetadata = L12SharePage.Metadata(store, $"/news/{draft.Id}", "https://legion-12.com");
            Assert.Equal("十二军团", draftMetadata.Title);

            store.PublishArticle(admin, draft.Id);
            store.ChangeArticleStatus(admin, draft.Id, "withdraw");
            var withdrawn = L12SharePage.Metadata(store, "/news/private-share", "https://legion-12.com");
            Assert.Equal("十二军团", withdrawn.Title);
            Assert.DoesNotContain("私有", withdrawn.Description);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyHtmlBodyBecomesPlainShareDescriptionAndHttpImagesAreRejected()
    {
        var root = TempRoot("legacy");
        try
        {
            var path = Path.Combine(root, "platform.json");
            File.WriteAllText(path, """
            {
              "content": {
                "news.entries": "[{\"id\":\"legacy-share\",\"title\":\"旧资讯\",\"summary\":\"旧摘要\",\"body\":\"<p>第一段 &amp; 内容</p><p>第二段</p>\",\"category\":\"官方公告\",\"coverUrl\":\"http://unsafe.example/cover.png\",\"published\":true}]"
              }
            }
            """, Encoding.UTF8);
            var store = new L12PlatformStore(path);
            var metadata = L12SharePage.Metadata(store, "/news/legacy-share", "https://legion-12.com");
            Assert.Equal("第一段 & 内容 第二段", metadata.Description);
            Assert.Equal("https://legion-12.com/favicon.png", metadata.ImageUrl);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpEndpointReturnsRenderedHtmlWithCacheAndSecurityHeaders()
    {
        var root = TempRoot("http");
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        var previousBase = Environment.GetEnvironmentVariable("L12_PUBLIC_BASE_URL");
        var previousTemplate = Environment.GetEnvironmentVariable(L12SharePage.TemplatePathEnvironmentKey);
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            var templatePath = Path.Combine(root, "index.html");
            File.WriteAllText(templatePath,
                "<html><head><!-- l12-share-meta:start --><title>旧</title><!-- l12-share-meta:end --></head><body></body></html>",
                Encoding.UTF8);
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            Environment.SetEnvironmentVariable("L12_PUBLIC_BASE_URL", "https://legion-12.com");
            Environment.SetEnvironmentVariable(L12SharePage.TemplatePathEnvironmentKey, templatePath);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            using var request = new HttpRequestMessage(HttpMethod.Get, L12SharePage.Endpoint);
            request.Headers.Add(L12SharePage.OriginalPathHeader, "/");
            using var response = await client.SendAsync(request);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("max-age=60", response.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Contains("<title>十二军团</title>", html);
            Assert.Contains("https://legion-12.com/favicon.png", html);
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
            Environment.SetEnvironmentVariable("L12_PUBLIC_BASE_URL", previousBase);
            Environment.SetEnvironmentVariable(L12SharePage.TemplatePathEnvironmentKey, previousTemplate);
            Directory.Delete(root, true);
        }
    }

    private static string TempRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-share-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin, string kind)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == kind);
        (int Width, int Height) desktop = policy.FlexibleDimensions
            ? (997, 331) : (policy.DesktopWidth, policy.DesktopHeight);
        (int Width, int Height) mobile = policy.FlexibleDimensions
            ? (421, 777) : (policy.MobileWidth, policy.MobileHeight);
        (int Width, int Height) thumbnail = policy.FlexibleDimensions
            ? (137, 59) : (policy.ThumbnailWidth, policy.ThumbnailHeight);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, "share.webp", "image/webp",
            Webp(desktop.Width, desktop.Height), Webp(desktop.Width, desktop.Height),
            Webp(mobile.Width, mobile.Height), Webp(thumbnail.Width, thumbnail.Height),
            "分享封面", .5, .5));
    }

    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        var encodedWidth = width - 1;
        var encodedHeight = height - 1;
        bytes[24] = (byte)encodedWidth;
        bytes[25] = (byte)(encodedWidth >> 8);
        bytes[26] = (byte)(encodedWidth >> 16);
        bytes[27] = (byte)encodedHeight;
        bytes[28] = (byte)(encodedHeight >> 8);
        bytes[29] = (byte)(encodedHeight >> 16);
        return bytes;
    }
}
