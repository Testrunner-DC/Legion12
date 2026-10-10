using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class HomeNoticeArticleRouteRegressionTests
{
    [Fact]
    public void PublishedHomeNoticeAcceptsCanonicalAndLegacyNewsArticleRoutes()
    {
        WithStore((store, admin) =>
        {
            var article = PublishNews(store, admin, "首页通知资讯");

            SaveAndPublishHome(store, admin, $"/news/{article.Id}");
            SaveAndPublishHome(store, admin, $"/news#article-{article.Id}");
        });
    }

    [Fact]
    public void PublishedHomeNoticeRejectsMalformedRoutesUnpublishedNewsAndNonNewsArticles()
    {
        WithStore((store, admin) =>
        {
            var publishedNews = PublishNews(store, admin, "已发布资讯");
            var draftNews = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "草稿资讯", "", "正文",
                "官方公告", "", "", "draft-news", false, null));
            var videoCategory = store.AdminSiteCategories("video").First();
            var videoMedia = Upload(store, admin, "video");
            var video = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "已发布视频", "", "",
                videoCategory.Name, "", "https://example.com/video", "published-video", false, null,
                Kind: "video", CategoryId: videoCategory.Id, MediaAssetId: videoMedia.Id,
                VideoAuthorName: "十二军团频道"));
            store.PublishArticle(admin, video.Id);

            var invalidRoutes = new[]
            {
                "", "/news", "/news/", "https://example.com/news/" + publishedNews.Id,
                $"/news/{publishedNews.Id}/extra", $"/news/{publishedNews.Id}?preview=1",
                $"/news/{publishedNews.Id}#fragment", $"/news#article-{publishedNews.Id}/extra",
                $"/news#article-{publishedNews.Id}?preview=1", $"/news#article-{publishedNews.Id}#fragment",
                $"/news/{draftNews.Id}", $"/news/{video.Id}",
            };

            foreach (var href in invalidRoutes)
            {
                store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey,
                    HomeComposition(href, enabled: true));
                Assert.Throws<ArgumentException>(() =>
                    store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey));
            }
        });
    }

    [Fact]
    public void HomeNoticeDraftAndDisabledNoticeKeepExistingValidationBehavior()
    {
        WithStore((store, admin) =>
        {
            store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey,
                HomeComposition("https://example.com/not-a-news-article", enabled: true));

            store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey,
                HomeComposition("https://example.com/not-a-news-article", enabled: false));
            store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey);
        });
    }

    private static L12ArticleView PublishNews(L12PlatformStore store, L12AccountView admin, string title)
    {
        var article = store.SaveArticleDraft(admin, new L12ArticleDraft(null, title, "摘要", "正文",
            "官方公告", "", "", title, false, null));
        return store.PublishArticle(admin, article.Id);
    }

    private static void SaveAndPublishHome(L12PlatformStore store, L12AccountView admin, string href)
    {
        store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey,
            HomeComposition(href, enabled: true));
        store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey);
    }

    private static string HomeComposition(string href, bool enabled) => JsonSerializer.Serialize(new
    {
        version = 1,
        heroSlides = Array.Empty<object>(),
        notices = new[] { new { id = "notice", label = "公告", href, enabled } },
    });

    private static void WithStore(Action<L12PlatformStore, L12AccountView> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-home-notice-route-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            action(store, admin);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin, string kind)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == kind);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, $"{kind}.webp", "image/webp",
            Webp(policy.DesktopWidth, policy.DesktopHeight), Webp(policy.DesktopWidth, policy.DesktopHeight),
            Webp(policy.MobileWidth, policy.MobileHeight), Webp(policy.ThumbnailWidth, policy.ThumbnailHeight),
            $"{policy.Label}测试图", .5, .5, $"{policy.Label}桌面测试图", $"{policy.Label}移动测试图",
            $"{policy.Label}缩略测试图", false));
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
