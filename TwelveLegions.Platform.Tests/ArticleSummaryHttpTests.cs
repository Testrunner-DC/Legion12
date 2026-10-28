using System.Net;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ArticleSummaryHttpTests
{
    private readonly ITestOutputHelper _output;
    public ArticleSummaryHttpTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task HomeSummaryPreservesPublishedSelectionWithoutBodiesOrAdminMediaProjection()
    {
        await WithServer(async (store, admin, client) =>
        {
            var cover = Upload(store, admin, "news");
            var bodyImage = Upload(store, admin, "article");
            var body = JsonSerializer.Serialize(new { format = "l12-blocks", version = 1, blocks = new object[] {
                new { id = "paragraph", type = "paragraph", text = "首页公开正文 " + new string('x', 15_000) },
                new { id = "paragraph-2", type = "paragraph", text = new string('y', 15_000) },
                new { id = "paragraph-3", type = "paragraph", text = new string('z', 15_000) },
                new { id = "body-image", type = "image", mediaAssetId = bodyImage.Id, alt = "合成正文图片", caption = "仅详情素材" } } });
            var category = store.PublicSiteCategories("news").First();
            var draft = store.SaveArticleDraft(admin, new(null, "首页公开标题", "", body, category.Name,
                "", "", "home-summary-public", true, null, CategoryId: category.Id, MediaAssetId: cover.Id));
            var published = store.PublishArticle(admin, draft.Id);
            store.SaveArticleDraft(admin, new(published.Id, "隐藏草稿标题", "隐藏草稿摘要", "隐藏草稿正文",
                category.Name, "", "", published.Slug, false, null, ExpectedRevision: published.Revision,
                CategoryId: category.Id, MediaAssetId: cover.Id));
            var future = store.SaveArticleDraft(admin, new(null, "未来标题", "", "尚未公开",
                category.Name, "", "", "home-summary-future", false, DateTimeOffset.UtcNow.AddDays(1), CategoryId: category.Id));
            store.PublishArticle(admin, future.Id);
            var legacyText = await client.GetStringAsync("/api/site/home");
            Assert.Contains("bodyMedia", legacyText);
            _output.WriteLine("Legacy home synthetic UTF8 bytes: " + Encoding.UTF8.GetByteCount(legacyText));
            var scans = 0;
            store.SiteMediaReferenceScanObserver = () => scans++;
            using var response = await client.GetAsync("/api/site/home-summary");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summaryText = await response.Content.ReadAsStringAsync();
            using var summary = JsonDocument.Parse(summaryText);
            using var legacy = JsonDocument.Parse(legacyText);
            Assert.Equal(0, scans);
            foreach (var name in new[] { "news", "videos", "products" })
            {
                Assert.Equal(legacy.RootElement.GetProperty(name).EnumerateArray().Select(row => row.GetProperty("id").GetString()),
                    summary.RootElement.GetProperty(name).EnumerateArray().Select(row => row.GetProperty("id").GetString()));
                foreach (var row in summary.RootElement.GetProperty(name).EnumerateArray())
                {
                    Assert.False(row.TryGetProperty("body", out _));
                    Assert.False(row.TryGetProperty("bodyMedia", out _));
                    Assert.InRange(row.GetProperty("bodyPreview").GetString()!.Length, 0, 261);
                }
            }
            var news = Assert.Single(summary.RootElement.GetProperty("news").EnumerateArray());
            Assert.Equal("首页公开标题", news.GetProperty("title").GetString());
            Assert.Contains("首页公开正文", news.GetProperty("bodyPreview").GetString());
            var image = Assert.Single(summary.RootElement.GetProperty("media").EnumerateArray());
            Assert.Equal(cover.Id, image.GetProperty("id").GetString());
            Assert.Equal(cover.DesktopUrl, image.GetProperty("desktopUrl").GetString());
            Assert.Equal(cover.MobileUrl, image.GetProperty("mobileUrl").GetString());
            foreach (var key in new[] { "referenceCount", "createdBy", "contentHash", "originalBytes" })
                Assert.False(image.TryGetProperty(key, out _));
            Assert.DoesNotContain(bodyImage.Id, summaryText);
            Assert.DoesNotContain("隐藏草稿", summaryText);
            Assert.DoesNotContain(future.Id, summaryText);
            Assert.Equal(legacy.RootElement.GetProperty("composition").GetString(), summary.RootElement.GetProperty("composition").GetString());
            Assert.Equal(legacy.RootElement.GetProperty("legal").GetString(), summary.RootElement.GetProperty("legal").GetString());
            Assert.True(Encoding.UTF8.GetByteCount(summaryText) * 10 < Encoding.UTF8.GetByteCount(legacyText));
            _output.WriteLine("Summary home synthetic UTF8 bytes: " + Encoding.UTF8.GetByteCount(summaryText));
            store.SiteMediaReferenceScanObserver = null;
            using var detail = JsonDocument.Parse(await client.GetStringAsync("/api/articles/by-key/" + published.Id));
            Assert.Equal(body, detail.RootElement.GetProperty("body").GetString());
            store.ChangeArticleStatus(admin, published.Id, "withdraw");
            using var withdrawn = JsonDocument.Parse(await client.GetStringAsync("/api/site/home-summary"));
            Assert.Empty(withdrawn.RootElement.GetProperty("news").EnumerateArray());
            Assert.Empty(withdrawn.RootElement.GetProperty("media").EnumerateArray());
        });
    }

    [Fact]
    public async Task HomeSummaryKeepsAllThreeLimitsOrderingAndHeroCoverSelections()
    {
        await WithServer(async (store, admin, client) =>
        {
            foreach (var (kind, limit) in new[] { ("news", 7), ("video", 6), ("product", 8) })
            {
                var category = store.PublicSiteCategories(kind).First();
                var media = Upload(store, admin, kind);
                for (var index = 0; index < limit + 2; index++)
                {
                    var draft = store.SaveArticleDraft(admin, new(null, kind + index, "公开摘要", "公开正文",
                        category.Name, "", kind == "video" ? "https://example.com/video" : "", "home-" + kind + index,
                        index == 0, null, Kind: kind, CategoryId: category.Id, MediaAssetId: media.Id,
                        VideoAuthorName: kind == "video" ? "合成作者" : ""));
                    store.PublishArticle(admin, draft.Id);
                }
            }
            var hero = Upload(store, admin, "hero");
            var composition = JsonSerializer.Serialize(new { version = 1, heroSlides = new[] {
                new { id = "home-hero", enabled = true, title = "已发布轮播", mediaAssetId = hero.Id, href = "/news" } }, notices = Array.Empty<object>() });
            store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey, composition);
            store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey);
            using var legacy = JsonDocument.Parse(await client.GetStringAsync("/api/site/home"));
            using var summary = JsonDocument.Parse(await client.GetStringAsync("/api/site/home-summary"));
            foreach (var (key, limit) in new[] { ("news", 7), ("videos", 6), ("products", 8) })
            {
                var expected = legacy.RootElement.GetProperty(key).EnumerateArray().ToArray();
                var actual = summary.RootElement.GetProperty(key).EnumerateArray().ToArray();
                Assert.Equal(limit, actual.Length);
                Assert.Equal(expected.Select(row => row.GetProperty("id").GetString()), actual.Select(row => row.GetProperty("id").GetString()));
                Assert.Equal(expected.Select(row => row.GetProperty("title").GetString()), actual.Select(row => row.GetProperty("title").GetString()));
                Assert.All(actual, row => Assert.False(row.TryGetProperty("body", out _)));
            }
            Assert.Equal(legacy.RootElement.GetProperty("composition").GetString(), summary.RootElement.GetProperty("composition").GetString());
            Assert.Equal(legacy.RootElement.GetProperty("media").EnumerateArray().Select(row => row.GetProperty("id").GetString()),
                summary.RootElement.GetProperty("media").EnumerateArray().Select(row => row.GetProperty("id").GetString()));
            Assert.Contains(summary.RootElement.GetProperty("media").EnumerateArray(), row => row.GetProperty("id").GetString() == hero.Id);
        });
    }

    [Theory]
    [InlineData("news")]
    [InlineData("product")]
    [InlineData("video")]
    public async Task SummariesKeepPublishedMetadataAndSearchWithoutFullBodies(string kind)
    {
        await WithServer(async (store, admin, client) =>
        {
            var text = "正文搜索锚点 " + new string('文', 40_000);
            var category = store.PublicSiteCategories(kind).First();
            var media = kind == "news" ? null : Upload(store, admin, kind);
            var draft = store.SaveArticleDraft(admin, new(null, "公开内容", "摘要", text,
                category.Name, "", kind == "video" ? "https://example.com/video" : "", "summary-" + kind,
                true, null, Kind: kind, CategoryId: category.Id, MediaAssetId: media?.Id, VideoAuthorName: kind == "video" ? "作者" : ""));
            var published = store.PublishArticle(admin, draft.Id);
            store.SaveArticleDraft(admin, new(published.Id, "未发布标题", "秘密摘要", "秘密正文",
                published.Category, "", published.Link, published.Slug, false, null,
                ExpectedRevision: published.Revision, Kind: kind, CategoryId: category.Id, MediaAssetId: media?.Id, VideoAuthorName: published.VideoAuthorName));
            var legacy = await client.GetStringAsync("/api/articles?kind=" + kind);
            using var response = await client.GetAsync("/api/articles/summaries?kind=" + kind);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(payload);
            var item = Assert.Single(json.RootElement.EnumerateArray());
            Assert.Equal(published.Id, item.GetProperty("id").GetString());
            Assert.Equal("公开内容", item.GetProperty("title").GetString());
            Assert.Equal(published.Summary, item.GetProperty("summary").GetString());
            Assert.False(item.TryGetProperty("body", out _));
            Assert.False(item.TryGetProperty("bodyMedia", out _));
            Assert.DoesNotContain("秘密", payload);
            if (kind != "video") Assert.True(payload.Length * 10 < legacy.Length);
            Console.WriteLine($"Article summary synthetic UTF8 kind={kind} legacy={Encoding.UTF8.GetByteCount(legacy)} summary={Encoding.UTF8.GetByteCount(payload)}");
            using var search = JsonDocument.Parse(await client.GetStringAsync(
                "/api/articles/summaries?kind=" + kind + "&search=" + Uri.EscapeDataString(kind == "video" ? "作者" : "正文搜索锚点")));
            Assert.Single(search.RootElement.EnumerateArray());
            using var notPublished = JsonDocument.Parse(await client.GetStringAsync(
                "/api/articles/summaries?kind=" + kind + "&search=" + Uri.EscapeDataString("秘密正文")));
            Assert.Empty(notPublished.RootElement.EnumerateArray());
            if (kind == "news")
            {
                using var detail = JsonDocument.Parse(await client.GetStringAsync("/api/articles/by-key/" + published.Id));
                Assert.Equal(text, detail.RootElement.GetProperty("body").GetString());
                store.ChangeArticleStatus(admin, published.Id, "withdraw");
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/articles/by-key/" + published.Id)).StatusCode);
            }
            else Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/articles/by-key/" + published.Id)).StatusCode);
        });
    }

    [Fact]
    public async Task SummaryFallbackIsBoundedAndDetailIsNotLimitedToFirstListPage()
    {
        await WithServer(async (store, admin, client) =>
        {
            var article = store.SaveArticleDraft(admin, new(null, "完整正文", "", new string('甲', 1_000),
                "官方公告", "", "", "summary-fallback", false, null));
            var published = store.PublishArticle(admin, article.Id);
            var pinned = store.SaveArticleDraft(admin, new(null, "置顶记录", "", "置顶正文",
                "官方公告", "", "", "summary-pinned", true, null));
            store.PublishArticle(admin, pinned.Id);
            using var summaries = JsonDocument.Parse(await client.GetStringAsync("/api/articles/summaries?limit=1"));
            Assert.NotEqual(published.Id, Assert.Single(summaries.RootElement.EnumerateArray()).GetProperty("id").GetString());
            using var fallback = JsonDocument.Parse(await client.GetStringAsync("/api/articles/summaries?search=" + Uri.EscapeDataString("完整正文")));
            var preview = Assert.Single(fallback.RootElement.EnumerateArray()).GetProperty("bodyPreview").GetString()!;
            Assert.InRange(preview.Length, 1, 261);
            using var detail = JsonDocument.Parse(await client.GetStringAsync("/api/articles/by-key/" + published.Slug));
            Assert.Equal(1_000, detail.RootElement.GetProperty("body").GetString()!.Length);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/articles/by-key/unknown-article")).StatusCode);
        });
    }

    [Fact]
    public async Task StructuredPreviewKeepsCaptionsButNoHiddenMediaOrFutureDrafts()
    {
        await WithServer(async (store, admin, client) =>
        {
            var body = JsonSerializer.Serialize(new { format = "l12-blocks", version = 1, blocks = new object[] {
                new { id = "p", type = "paragraph", text = "段落正文" },
                new { id = "c", type = "card", cardId = "S01-0101", caption = "单卡说明" },
            } });
            var row = store.SaveArticleDraft(admin, new(null, "结构正文", "", body, "官方公告", "", "", "summary-structured", false, null));
            store.PublishArticle(admin, row.Id);
            var future = store.SaveArticleDraft(admin, new(null, "未来秘密", "", "未公开正文", "官方公告", "", "", "summary-future", false, DateTimeOffset.UtcNow.AddDays(1)));
            store.PublishArticle(admin, future.Id);
            using var summaries = JsonDocument.Parse(await client.GetStringAsync("/api/articles/summaries"));
            var entry = Assert.Single(summaries.RootElement.EnumerateArray());
            Assert.Contains("段落正文", entry.GetProperty("bodyPreview").GetString());
            Assert.Contains("单卡说明", entry.GetProperty("bodyPreview").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/articles/by-key/" + future.Id)).StatusCode);
        });
    }

    private static async Task WithServer(Func<L12PlatformStore, L12AccountView, HttpClient, Task> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-article-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        var passed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new(new(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            await run(store, admin, client);
            passed = true;
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", oldHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic article fixture retained: " + root);
        }
    }

    [Fact]
    public async Task LegalLegacyJsonCannotBreakTheWholeSummaryList()
    {
        await WithServer(async (store, admin, client) =>
        {
            foreach (var body in new[] { "{\"format\":123,\"text\":\"普通JSON示例\"}", "{\"format\":null}", "{\"format\":{}}" })
            {
                var row = store.SaveArticleDraft(admin, new(null, "合法旧正文", "", body, "官方公告", "", "", "", false, null));
                store.PublishArticle(admin, row.Id);
            }
            using var response = await client.GetAsync("/api/articles/summaries");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(3, json.RootElement.GetArrayLength());
        });
    }

    [Fact]
    public async Task DetailKeyDoesNotCollideWithSummaryCollection()
    {
        await WithServer(async (store, admin, client) =>
        {
            var row = store.SaveArticleDraft(admin, new(null, "合法同名路径", "摘要", "不能丢失的正文", "官方公告", "", "", "summaries", false, null));
            store.PublishArticle(admin, row.Id);
            using var collection = JsonDocument.Parse(await client.GetStringAsync("/api/articles/summaries"));
            Assert.Equal(JsonValueKind.Array, collection.RootElement.ValueKind);
            using var detail = JsonDocument.Parse(await client.GetStringAsync("/api/articles/by-key/summaries"));
            Assert.Equal(row.Id, detail.RootElement.GetProperty("id").GetString());
            Assert.Equal("不能丢失的正文", detail.RootElement.GetProperty("body").GetString());
        });
    }

    [Fact]
    public async Task StructuredTextPreviewPreservesLiteralAngleBrackets()
    {
        await WithServer(async (store, admin, client) =>
        {
            var body = JsonSerializer.Serialize(new { format = "l12-blocks", version = 1, blocks = new[] { new { id = "p", type = "paragraph", text = "<value>本回合</value> 兵力 +1000" } } });
            var row = store.SaveArticleDraft(admin, new(null, "结构文本", "", body, "官方公告", "", "", "", false, null));
            store.PublishArticle(admin, row.Id);
            using var json = JsonDocument.Parse(await client.GetStringAsync("/api/articles/summaries"));
            Assert.Equal("<value>本回合</value> 兵力 +1000", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("bodyPreview").GetString());
        });
    }

    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin, string kind)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == kind);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, kind + ".webp", "image/webp",
            Webp(policy.DesktopWidth, policy.DesktopHeight), Webp(policy.DesktopWidth, policy.DesktopHeight),
            Webp(policy.MobileWidth, policy.MobileHeight), Webp(policy.ThumbnailWidth, policy.ThumbnailHeight),
            "合成封面", .5, .5, "桌面", "移动", "缩略", kind == "hero"));
    }

    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        for (var i = 0; i < 3; i++) { bytes[24 + i] = (byte)((width - 1) >> (8 * i)); bytes[27 + i] = (byte)((height - 1) >> (8 * i)); }
        return bytes;
    }
}
