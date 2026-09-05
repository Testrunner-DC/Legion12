using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
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

            var disabled = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(target.Id, target.Kind,
                target.Name, target.Slug, target.SortOrder, false, target.Version));
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
}
