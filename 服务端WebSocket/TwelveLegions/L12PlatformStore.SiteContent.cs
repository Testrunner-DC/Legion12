using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12SiteMediaPolicyView(string Kind, string Label, int DesktopWidth, int DesktopHeight,
    int MobileWidth, int MobileHeight, int ThumbnailWidth, int ThumbnailHeight, string SafeArea,
    IReadOnlyList<string> AcceptedOriginalFormats);

public sealed record L12SiteMediaUpload(string Kind, string OriginalFileName, string OriginalContentType,
    byte[] Original, byte[] DesktopWebp, byte[] MobileWebp, byte[] ThumbnailWebp, string AltText,
    double FocalX, double FocalY);

public sealed record L12SiteMediaView(string Id, string Kind, string AltText, double FocalX, double FocalY,
    string OriginalFormat, string ContentHash, string DesktopUrl, string MobileUrl, string ThumbnailUrl,
    int DesktopWidth, int DesktopHeight, int MobileWidth, int MobileHeight, int ThumbnailWidth,
    int ThumbnailHeight, long OriginalBytes, long DeliveryBytes, string CreatedBy, DateTimeOffset CreatedAt,
    int ReferenceCount);

public sealed record L12SiteMediaEmbedView(string Id, string AltText, string DesktopUrl, string MobileUrl,
    string ThumbnailUrl, int DesktopWidth, int DesktopHeight, int MobileWidth, int MobileHeight);

public sealed record L12SiteMediaFile(string Path, string ContentType, string Hash, string FileName);

public sealed record L12SiteCategoryDraft(string? Id, string Kind, string Name, string Slug, int SortOrder,
    bool Active, long? ExpectedVersion = null);

public sealed record L12SiteCategoryView(string Id, string Kind, string Name, string Slug, int SortOrder,
    bool Active, long Version, int ItemCount);

public sealed record L12SiteHomeView(string Composition, string Legal, IReadOnlyList<L12ArticleView> News,
    IReadOnlyList<L12ArticleView> Videos, IReadOnlyList<L12ArticleView> Products,
    IReadOnlyList<L12SiteMediaView> Media);

public sealed class L12SiteContentConflictException(string message) : InvalidOperationException(message);

public sealed partial class L12PlatformStore
{
    public const string HomeCompositionContentKey = "home.composition";
    public const string SiteLegalContentKey = "site.footer";
    public const int SiteMediaOriginalMaxBytes = 16 * 1024 * 1024;
    public const int SiteMediaDesktopMaxBytes = 5 * 1024 * 1024;
    public const int SiteMediaMobileMaxBytes = 5 * 1024 * 1024;
    public const int SiteMediaThumbnailMaxBytes = 2 * 1024 * 1024;
    public const long SiteMediaRequestMaxBytes = 32L * 1024 * 1024;

    private sealed class SiteMediaRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "news";
        public string AltText { get; set; } = string.Empty;
        public double FocalX { get; set; } = .5;
        public double FocalY { get; set; } = .5;
        public string OriginalFileName { get; set; } = string.Empty;
        public string OriginalFormat { get; set; } = "image/webp";
        public string OriginalFile { get; set; } = string.Empty;
        public string OriginalHash { get; set; } = string.Empty;
        public string DesktopFile { get; set; } = string.Empty;
        public string DesktopHash { get; set; } = string.Empty;
        public string MobileFile { get; set; } = string.Empty;
        public string MobileHash { get; set; } = string.Empty;
        public string ThumbnailFile { get; set; } = string.Empty;
        public string ThumbnailHash { get; set; } = string.Empty;
        public int DesktopWidth { get; set; }
        public int DesktopHeight { get; set; }
        public int MobileWidth { get; set; }
        public int MobileHeight { get; set; }
        public int ThumbnailWidth { get; set; }
        public int ThumbnailHeight { get; set; }
        public long OriginalBytes { get; set; }
        public long DeliveryBytes { get; set; }
        public string CreatedByAccountId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DeletedAt { get; set; }
        public string? DeletedByAccountId { get; set; }
    }

    private sealed class SiteCategoryRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "news";
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool Active { get; set; } = true;
        public long Version { get; set; } = 1;
    }

    private static readonly IReadOnlyDictionary<string, L12SiteMediaPolicyView> MediaPolicies =
        new Dictionary<string, L12SiteMediaPolicyView>(StringComparer.OrdinalIgnoreCase)
        {
            ["hero"] = new("hero", "首页轮播", 2460, 1440, 1080, 1440, 600, 351,
                "桌面中央 70% × 70%；移动端中央 76% × 78%，标题与人物主体不得贴边",
                ["image/jpeg", "image/png", "image/webp", "image/avif"]),
            ["news"] = new("news", "资讯封面", 1600, 900, 1280, 720, 480, 270,
                "全端固定 16:9；建议原图 1600×900 或更高同等比例，标题与主体保持在中央 76% × 76%",
                ["image/jpeg", "image/png", "image/webp", "image/avif"]),
            ["article"] = new("article", "资讯正文图片", 1600, 1000, 1080, 1350, 600, 375,
                "正文主体保持在中央 84% × 82%，重要文字与人物面部不得贴边",
                ["image/jpeg", "image/png", "image/webp", "image/avif"]),
            ["video"] = new("video", "视频封面", 1280, 720, 1280, 720, 480, 270,
                "全端固定 16:9；建议原图 1280×720 或更高同等比例，播放主体避开四角控件区域",
                ["image/jpeg", "image/png", "image/webp", "image/avif"]),
            ["product"] = new("product", "商品图片", 1600, 1200, 1200, 900, 480, 360,
                "全端固定 4:3；建议原图 1600×1200 或更高同等比例，商品主体保持在中央 78% × 78%，包装文字不得贴边",
                ["image/jpeg", "image/png", "image/webp", "image/avif"]),
        };

    private static readonly (string Kind, string Name, string Slug)[] DefaultSiteCategories =
    [
        ("news", "官方公告", "official"), ("news", "规则勘误", "rules"),
        ("news", "赛季更新", "season"), ("news", "赛事信息", "events"),
        ("video", "规则教学", "tutorial"), ("video", "赛事回顾", "tournament"),
        ("video", "开发日志", "development"),
        ("product", "卡牌系列", "card-series"), ("product", "预组套牌", "starter-decks"),
        ("product", "周边商品", "accessories"),
    ];

    public static IReadOnlyList<L12SiteMediaPolicyView> SiteMediaPolicies()
        => MediaPolicies.Values.OrderBy(item => item.Kind).ToArray();

    private string SiteMediaRoot => Path.Combine(Path.GetDirectoryName(_path)!, "site-media");

    private void EnsureSiteContentState()
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var group in DefaultSiteCategories.GroupBy(item => item.Kind))
            {
                if (_data.SiteCategories.Any(item => item.Kind == group.Key)) continue;
                var order = 0;
                foreach (var item in group)
                    _data.SiteCategories.Add(new SiteCategoryRow
                    {
                        Kind = item.Kind, Name = item.Name, Slug = item.Slug, SortOrder = order++,
                    });
                changed = true;
            }

            foreach (var article in _data.Articles)
            {
                var normalizedArticleKind = NormalizeSiteKind(article.Kind);
                if (article.Kind != normalizedArticleKind) changed = true;
                article.Kind = normalizedArticleKind;
                var category = FindCategory(article.Kind, article.CategoryId, article.Category, true);
                if (category is not null)
                {
                    if (article.CategoryId != category.Id || article.Category != category.Name) changed = true;
                    article.CategoryId = category.Id;
                    article.Category = category.Name;
                }
                if (article.Published is { } published)
                {
                    var normalizedPublishedKind = NormalizeSiteKind(published.Kind);
                    if (published.Kind != normalizedPublishedKind) changed = true;
                    published.Kind = normalizedPublishedKind;
                    var publishedCategory = FindCategory(published.Kind, published.CategoryId,
                        published.Category, true) ?? category;
                    if (publishedCategory is not null)
                    {
                        if (published.CategoryId != publishedCategory.Id ||
                            published.Category != publishedCategory.Name) changed = true;
                        published.CategoryId = publishedCategory.Id;
                        published.Category = publishedCategory.Name;
                    }
                }
                foreach (var revision in article.Revisions)
                {
                    var normalizedRevisionKind = NormalizeSiteKind(revision.Kind);
                    if (revision.Kind != normalizedRevisionKind) changed = true;
                    revision.Kind = normalizedRevisionKind;
                    var revisionCategory = FindCategory(revision.Kind, revision.CategoryId,
                        revision.Category, true) ?? category;
                    if (revisionCategory is not null)
                    {
                        if (revision.CategoryId != revisionCategory.Id ||
                            revision.Category != revisionCategory.Name) changed = true;
                        revision.CategoryId = revisionCategory.Id;
                        revision.Category = revisionCategory.Name;
                    }
                }
            }
            if (changed) Save();
        }
    }

    public IReadOnlyList<L12SiteCategoryView> PublicSiteCategories(string? kind = null)
    {
        lock (_gate)
        {
            var normalizedKind = string.IsNullOrWhiteSpace(kind) ? null : NormalizeSiteKind(kind);
            return _data.SiteCategories.Where(row => row.Active && (normalizedKind is null || row.Kind == normalizedKind))
                .OrderBy(row => row.Kind).ThenBy(row => row.SortOrder).ThenBy(row => row.Name)
                .Select(ToSiteCategoryView).ToArray();
        }
    }

    public IReadOnlyList<L12SiteCategoryView> AdminSiteCategories(string? kind = null)
    {
        lock (_gate)
        {
            var normalizedKind = string.IsNullOrWhiteSpace(kind) ? null : NormalizeSiteKind(kind);
            return _data.SiteCategories.Where(row => normalizedKind is null || row.Kind == normalizedKind)
                .OrderBy(row => row.Kind).ThenBy(row => row.SortOrder).ThenBy(row => row.Name)
                .Select(ToSiteCategoryView).ToArray();
        }
    }

    public L12SiteCategoryView SaveSiteCategory(L12AccountView actor, L12SiteCategoryDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var kind = RequireSiteKind(draft.Kind);
            var name = LimitSiteText(draft.Name, 40);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("分类名称不能为空");
            var slug = NormalizeSiteSlug(draft.Slug, name);
            var row = string.IsNullOrWhiteSpace(draft.Id)
                ? null : _data.SiteCategories.FirstOrDefault(item => item.Id == draft.Id.Trim());
            if (row is not null && row.Kind != kind) throw new ArgumentException("分类所属内容类型不能修改");
            if (row is not null && draft.ExpectedVersion is not null && row.Version != draft.ExpectedVersion)
                throw new L12SiteContentConflictException("分类已被其他操作更新，请刷新后重试");
            if (_data.SiteCategories.Any(item => item.Id != row?.Id && item.Kind == kind &&
                (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("同一内容类型下的分类名称或标识不能重复");
            var previous = row is null ? null : JsonSerializer.Serialize(ToSiteCategoryView(row));
            if (row is null)
            {
                row = new SiteCategoryRow { Kind = kind };
                _data.SiteCategories.Add(row);
            }
            var previousName = row.Name;
            row.Name = name;
            row.Slug = slug;
            row.SortOrder = Math.Clamp(draft.SortOrder, 0, 10_000);
            row.Active = draft.Active;
            row.Version++;
            if (!string.Equals(previousName, row.Name, StringComparison.Ordinal))
                RefreshCategoryReferenceNames(row);
            var result = ToSiteCategoryView(row);
            AddAdminAudit(actor, "site-category", previous is null ? "create" : "update", row.Id,
                previous, JsonSerializer.Serialize(result), null, context);
            Save();
            return result;
        }
    }

    public IReadOnlyList<L12SiteCategoryView> ReorderSiteCategories(L12AccountView actor, string kind,
        IReadOnlyList<string> ids, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var normalizedKind = RequireSiteKind(kind);
            var rows = _data.SiteCategories.Where(item => item.Kind == normalizedKind)
                .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ToArray();
            if (ids.Count != rows.Length || ids.Distinct(StringComparer.Ordinal).Count() != rows.Length ||
                rows.Any(row => !ids.Contains(row.Id, StringComparer.Ordinal)))
                throw new ArgumentException("排序必须包含该内容类型下的全部分类且不能重复");
            for (var index = 0; index < ids.Count; index++)
            {
                var row = rows.First(item => item.Id == ids[index]);
                row.SortOrder = index;
                row.Version++;
            }
            AddAdminAudit(actor, "site-category", "reorder", normalizedKind, null,
                string.Join(',', ids), null, context);
            Save();
            return rows.OrderBy(item => item.SortOrder).Select(ToSiteCategoryView).ToArray();
        }
    }

    public void DeleteSiteCategory(L12AccountView actor, string id, string? migrateToId,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = _data.SiteCategories.FirstOrDefault(item => item.Id == id.Trim())
                ?? throw new KeyNotFoundException("分类不存在");
            var references = SiteCategoryReferenceCount(row.Id);
            SiteCategoryRow? target = null;
            if (references > 0)
            {
                if (string.IsNullOrWhiteSpace(migrateToId))
                    throw new L12SiteContentConflictException($"分类仍被 {references} 个内容快照引用，请选择迁移目标后再删除");
                target = _data.SiteCategories.FirstOrDefault(item => item.Id == migrateToId.Trim())
                    ?? throw new KeyNotFoundException("迁移目标分类不存在");
                if (target.Id == row.Id || target.Kind != row.Kind || !target.Active)
                    throw new ArgumentException("迁移目标必须是同一内容类型下的其他启用分类");
                MigrateCategoryReferences(row, target);
            }
            _data.SiteCategories.Remove(row);
            AddAdminAudit(actor, "site-category", "delete", row.Id, row.Name,
                target is null ? null : $"{target.Id}:{target.Name}", $"references={references}", context);
            Save();
        }
    }

    public L12SiteMediaView UploadSiteMedia(L12AccountView actor, L12SiteMediaUpload upload,
        L12AdminAuditContext? context = null)
    {
        var kind = NormalizeMediaKind(upload.Kind);
        var policy = MediaPolicies[kind];
        ValidateUploadBytes(upload.Original, "原图", SiteMediaOriginalMaxBytes);
        ValidateUploadBytes(upload.DesktopWebp, "桌面 WebP", SiteMediaDesktopMaxBytes);
        ValidateUploadBytes(upload.MobileWebp, "移动 WebP", SiteMediaMobileMaxBytes);
        ValidateUploadBytes(upload.ThumbnailWebp, "缩略图 WebP", SiteMediaThumbnailMaxBytes);
        var originalFormat = DetectImageFormat(upload.Original);
        if (!policy.AcceptedOriginalFormats.Contains(originalFormat, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("原图只允许 JPEG、PNG、WebP 或 AVIF，禁止 SVG 与其他主动内容格式");
        RequireWebpDimensions(upload.DesktopWebp, policy.DesktopWidth, policy.DesktopHeight, "桌面 WebP");
        RequireWebpDimensions(upload.MobileWebp, policy.MobileWidth, policy.MobileHeight, "移动 WebP");
        RequireWebpDimensions(upload.ThumbnailWebp, policy.ThumbnailWidth, policy.ThumbnailHeight, "缩略图 WebP");
        if (!double.IsFinite(upload.FocalX) || !double.IsFinite(upload.FocalY) ||
            upload.FocalX is < 0 or > 1 || upload.FocalY is < 0 or > 1)
            throw new ArgumentException("裁切焦点必须位于图片范围内");

        var originalHash = ContentHash(upload.Original);
        var desktopHash = ContentHash(upload.DesktopWebp);
        var mobileHash = ContentHash(upload.MobileWebp);
        var thumbnailHash = ContentHash(upload.ThumbnailWebp);
        var originalExtension = originalFormat switch
        {
            "image/jpeg" => ".jpg", "image/png" => ".png", "image/avif" => ".avif", _ => ".webp",
        };
        var originalFile = StoreImmutableMedia(upload.Original, originalHash, originalExtension);
        var desktopFile = StoreImmutableMedia(upload.DesktopWebp, desktopHash, ".webp");
        var mobileFile = StoreImmutableMedia(upload.MobileWebp, mobileHash, ".webp");
        var thumbnailFile = StoreImmutableMedia(upload.ThumbnailWebp, thumbnailHash, ".webp");

        lock (_gate)
        {
            var row = new SiteMediaRow
            {
                Kind = kind,
                AltText = LimitSiteText(upload.AltText, 180),
                FocalX = upload.FocalX,
                FocalY = upload.FocalY,
                OriginalFileName = LimitSiteText(Path.GetFileName(upload.OriginalFileName), 180),
                OriginalFormat = originalFormat,
                OriginalFile = originalFile,
                OriginalHash = originalHash,
                DesktopFile = desktopFile,
                DesktopHash = desktopHash,
                MobileFile = mobileFile,
                MobileHash = mobileHash,
                ThumbnailFile = thumbnailFile,
                ThumbnailHash = thumbnailHash,
                DesktopWidth = policy.DesktopWidth,
                DesktopHeight = policy.DesktopHeight,
                MobileWidth = policy.MobileWidth,
                MobileHeight = policy.MobileHeight,
                ThumbnailWidth = policy.ThumbnailWidth,
                ThumbnailHeight = policy.ThumbnailHeight,
                OriginalBytes = upload.Original.LongLength,
                DeliveryBytes = upload.DesktopWebp.LongLength + upload.MobileWebp.LongLength +
                    upload.ThumbnailWebp.LongLength,
                CreatedByAccountId = actor.Id,
            };
            _data.SiteMedia.Add(row);
            AddAdminAudit(actor, "site-media", "upload", row.Id, null,
                $"{row.Kind}:{row.OriginalHash}", $"desktop={desktopHash};mobile={mobileHash};thumb={thumbnailHash}", context);
            Save();
            return ToSiteMediaView(row);
        }
    }

    public IReadOnlyList<L12SiteMediaView> AdminSiteMedia(string? kind = null, int limit = 500)
    {
        lock (_gate)
        {
            var normalizedKind = string.IsNullOrWhiteSpace(kind) ? null : NormalizeMediaKind(kind);
            return _data.SiteMedia.Where(row => row.DeletedAt is null && (normalizedKind is null || row.Kind == normalizedKind))
                .OrderByDescending(row => row.CreatedAt).Take(Math.Clamp(limit, 1, 1000))
                .Select(ToSiteMediaView).ToArray();
        }
    }

    public void DeleteSiteMedia(L12AccountView actor, string id, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = ActiveMedia(id) ?? throw new KeyNotFoundException("素材不存在");
            var references = SiteMediaReferenceCount(row.Id);
            if (references > 0)
                throw new L12SiteContentConflictException($"素材仍被 {references} 个草稿、发布快照或历史版本引用，必须先解除引用");
            row.DeletedAt = DateTimeOffset.UtcNow;
            row.DeletedByAccountId = actor.Id;
            AddAdminAudit(actor, "site-media", "soft-delete", row.Id, row.OriginalHash, null,
                "内容寻址文件保留用于恢复与审计", context);
            Save();
        }
    }

    public L12SiteMediaFile? ResolveSiteMediaFile(string id, string variant, string fileName)
    {
        lock (_gate)
        {
            var row = ActiveMedia(id);
            if (row is null) return null;
            var resolved = variant.ToLowerInvariant() switch
            {
                "desktop" => (row.DesktopFile, row.DesktopHash),
                "mobile" => (row.MobileFile, row.MobileHash),
                "thumbnail" => (row.ThumbnailFile, row.ThumbnailHash),
                _ => default,
            };
            if (string.IsNullOrWhiteSpace(resolved.Item1)) return null;
            var expectedName = $"{resolved.Item2}.webp";
            if (!string.Equals(fileName, expectedName, StringComparison.OrdinalIgnoreCase)) return null;
            var path = Path.GetFullPath(Path.Combine(SiteMediaRoot, resolved.Item1));
            var root = Path.GetFullPath(SiteMediaRoot) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            return new L12SiteMediaFile(path, "image/webp", resolved.Item2, expectedName);
        }
    }

    public L12SiteHomeView PublicSiteHome()
    {
        lock (_gate)
        {
            var news = PublicArticles(null, null, 7, "news");
            var videos = PublicArticles(null, null, 6, "video");
            var products = PublicArticles(null, null, 8, "product");
            var mediaIds = SiteContentMediaIds(GetContent(HomeCompositionContentKey)).ToHashSet(StringComparer.Ordinal);
            foreach (var article in news.Concat(videos).Concat(products))
                if (!string.IsNullOrWhiteSpace(article.MediaAssetId)) mediaIds.Add(article.MediaAssetId!);
            var media = _data.SiteMedia.Where(row => row.DeletedAt is null && mediaIds.Contains(row.Id))
                .Select(ToSiteMediaView).ToArray();
            return new L12SiteHomeView(GetContent(HomeCompositionContentKey, "{}"),
                GetContent(SiteLegalContentKey, "{}"), news, videos, products, media);
        }
    }

    internal void ValidateSiteContentValue(string key, string value, bool publishing)
    {
        if (!string.Equals(key, HomeCompositionContentKey, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(key, SiteLegalContentKey, StringComparison.OrdinalIgnoreCase)) return;
        if (value.Length > 250_000) throw new ArgumentException("站点编排内容超过 250KB 限制");
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("站点编排必须是 JSON 对象");
            if (string.Equals(key, SiteLegalContentKey, StringComparison.OrdinalIgnoreCase))
            {
                ValidateJsonString(document.RootElement, "copyright", 300);
                ValidateJsonString(document.RootElement, "trademark", 1200);
                ValidateJsonString(document.RootElement, "registration", 200);
                ValidateOptionalSiteUrl(JsonString(document.RootElement, "contactHref"), "联系链接");
                return;
            }

            if (document.RootElement.TryGetProperty("heroSlides", out var slides))
            {
                if (slides.ValueKind != JsonValueKind.Array || slides.GetArrayLength() > 24)
                    throw new ArgumentException("首页轮播最多 24 张且必须是数组");
                foreach (var slide in slides.EnumerateArray())
                {
                    ValidateJsonString(slide, "id", 80);
                    ValidateHeroCopyString(slide, "eyebrow", 80);
                    ValidateHeroCopyString(slide, "title", 180);
                    ValidateHeroCopyString(slide, "summary", 600);
                    ValidateHeroCopyString(slide, "footer", 180);
                    ValidateOptionalSiteUrl(JsonString(slide, "href"), "轮播链接");
                    var enabled = !slide.TryGetProperty("enabled", out var enabledValue) || enabledValue.ValueKind != JsonValueKind.False;
                    var mediaId = JsonString(slide, "mediaAssetId");
                    if (publishing && enabled)
                    {
                        var media = ActiveMedia(mediaId);
                        if (media is null || media.Kind != "hero")
                            throw new ArgumentException("每个启用的轮播项都必须引用已上传的首页轮播素材");
                    }
                }
            }
            if (document.RootElement.TryGetProperty("notices", out var notices))
            {
                if (notices.ValueKind != JsonValueKind.Array || notices.GetArrayLength() > 50)
                    throw new ArgumentException("首页通知按钮最多 50 条且必须是数组");
                foreach (var notice in notices.EnumerateArray())
                {
                    ValidateJsonString(notice, "label", 80);
                    var href = JsonString(notice, "href");
                    ValidateOptionalSiteUrl(href, "通知按钮链接");
                    var enabled = !notice.TryGetProperty("enabled", out var enabledValue) || enabledValue.ValueKind != JsonValueKind.False;
                    if (!publishing || !enabled) continue;
                    const string prefix = "/news#article-";
                    if (!href.StartsWith(prefix, StringComparison.Ordinal) || href.Length <= prefix.Length)
                        throw new ArgumentException("启用的首页通知按钮必须选择一篇已发布资讯");
                    var articleId = href[prefix.Length..];
                    var article = _data.Articles.FirstOrDefault(row => row.Id == articleId && row.Published is not null &&
                        row.Published.Kind == "news" && row.Status == "published");
                    if (article is null) throw new ArgumentException("首页通知按钮引用的资讯不存在或尚未发布");
                }
            }
        }
        catch (JsonException error) { throw new ArgumentException($"站点编排 JSON 无效：{error.Message}"); }
    }

    internal string SiteMediaUrl(string? id, string variant = "desktop")
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        var row = ActiveMedia(id);
        if (row is null) return string.Empty;
        var hash = variant switch
        {
            "mobile" => row.MobileHash, "thumbnail" => row.ThumbnailHash, _ => row.DesktopHash,
        };
        return $"/api/site/media/{Uri.EscapeDataString(row.Id)}/{variant}/{hash}.webp";
    }

    private SiteCategoryRow? FindCategory(string kind, string? id, string? name, bool includeInactive)
    {
        var query = _data.SiteCategories.Where(item => item.Kind == NormalizeSiteKind(kind) &&
            (includeInactive || item.Active));
        if (!string.IsNullOrWhiteSpace(id))
            return query.FirstOrDefault(item => item.Id == id.Trim());
        if (!string.IsNullOrWhiteSpace(name))
            return query.FirstOrDefault(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return query.OrderBy(item => item.SortOrder).ThenBy(item => item.Name).FirstOrDefault();
    }

    private SiteMediaRow? ActiveMedia(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : _data.SiteMedia.FirstOrDefault(item => item.Id == id.Trim() && item.DeletedAt is null);

    internal static string NormalizeSiteKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "news" or "video" or "product" ? normalized : "news";
    }

    internal static string RequireSiteKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "news" or "video" or "product" ? normalized :
            throw new ArgumentException("内容类型必须是 news、video 或 product");
    }

    private static string NormalizeMediaKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return MediaPolicies.ContainsKey(normalized ?? string.Empty) ? normalized! :
            throw new ArgumentException("素材类型必须是 hero、news、article、video 或 product");
    }

    private L12SiteCategoryView ToSiteCategoryView(SiteCategoryRow row) => new(row.Id, row.Kind, row.Name,
        row.Slug, row.SortOrder, row.Active, row.Version, SiteCategoryReferenceCount(row.Id));

    private L12SiteMediaView ToSiteMediaView(SiteMediaRow row)
        => new(row.Id, row.Kind, row.AltText, row.FocalX, row.FocalY, row.OriginalFormat, row.OriginalHash,
            SiteMediaUrl(row.Id), SiteMediaUrl(row.Id, "mobile"), SiteMediaUrl(row.Id, "thumbnail"),
            row.DesktopWidth, row.DesktopHeight, row.MobileWidth, row.MobileHeight, row.ThumbnailWidth,
            row.ThumbnailHeight, row.OriginalBytes, row.DeliveryBytes, ArticleAccountName(row.CreatedByAccountId),
            row.CreatedAt, SiteMediaReferenceCount(row.Id));

    private L12SiteMediaEmbedView ToSiteMediaEmbedView(SiteMediaRow row)
        => new(row.Id, row.AltText, SiteMediaUrl(row.Id), SiteMediaUrl(row.Id, "mobile"),
            SiteMediaUrl(row.Id, "thumbnail"), row.DesktopWidth, row.DesktopHeight,
            row.MobileWidth, row.MobileHeight);

    private int SiteCategoryReferenceCount(string id)
        => _data.Articles.Sum(row => (row.CategoryId == id ? 1 : 0) + (row.Published?.CategoryId == id ? 1 : 0) +
            row.Revisions.Count(revision => revision.CategoryId == id));

    private int SiteMediaReferenceCount(string id)
    {
        var count = _data.Articles.Sum(row => (row.MediaAssetId == id ? 1 : 0) +
            CountJsonString(row.Body, id) + (row.Published?.MediaAssetId == id ? 1 : 0) +
            CountJsonString(row.Published?.Body, id) + row.Revisions.Sum(revision =>
                (revision.MediaAssetId == id ? 1 : 0) + CountJsonString(revision.Body, id)));
        count += _data.ContentEntries.Sum(entry => CountJsonString(entry.DraftValue, id) +
            CountJsonString(entry.PublishedValue, id));
        count += _data.ContentVersions.Sum(version => CountJsonString(version.Value, id));
        return count;
    }

    private void MigrateCategoryReferences(SiteCategoryRow source, SiteCategoryRow target)
    {
        foreach (var row in _data.Articles)
        {
            if (row.CategoryId == source.Id) { row.CategoryId = target.Id; row.Category = target.Name; row.Revision++; }
            if (row.Published?.CategoryId == source.Id)
            {
                row.Published.CategoryId = target.Id;
                row.Published.Category = target.Name;
            }
            foreach (var revision in row.Revisions.Where(item => item.CategoryId == source.Id))
            {
                revision.CategoryId = target.Id;
                revision.Category = target.Name;
            }
        }
    }

    private void RefreshCategoryReferenceNames(SiteCategoryRow category)
    {
        foreach (var row in _data.Articles)
        {
            if (row.CategoryId == category.Id) row.Category = category.Name;
            if (row.Published?.CategoryId == category.Id) row.Published.Category = category.Name;
            foreach (var revision in row.Revisions.Where(item => item.CategoryId == category.Id))
                revision.Category = category.Name;
        }
    }

    private static int CountJsonString(string? json, string expected)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.Contains(expected, StringComparison.Ordinal)) return 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            return CountJsonString(document.RootElement, expected);
        }
        catch (JsonException) { return 0; }
    }

    private static int CountJsonString(JsonElement element, string expected)
    {
        if (element.ValueKind == JsonValueKind.String)
            return string.Equals(element.GetString(), expected, StringComparison.Ordinal) ? 1 : 0;
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Sum(item => CountJsonString(item, expected));
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Sum(item => CountJsonString(item.Value, expected));
        return 0;
    }

    private static IEnumerable<string> SiteContentMediaIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("heroSlides", out var slides) || slides.ValueKind != JsonValueKind.Array)
                return [];
            return slides.EnumerateArray().Select(item => JsonString(item, "mediaAssetId"))
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private string StoreImmutableMedia(byte[] bytes, string hash, string extension)
    {
        Directory.CreateDirectory(SiteMediaRoot);
        var fileName = hash + extension;
        var path = Path.Combine(SiteMediaRoot, fileName);
        if (File.Exists(path)) return fileName;
        var temp = Path.Combine(SiteMediaRoot, $".{Guid.NewGuid():N}.upload");
        File.WriteAllBytes(temp, bytes);
        try { File.Move(temp, path, false); }
        catch (IOException) when (File.Exists(path)) { File.Delete(temp); }
        return fileName;
    }

    private static string ContentHash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidateUploadBytes(byte[] bytes, string label, int maxBytes)
    {
        if (bytes.Length == 0) throw new ArgumentException($"{label}不能为空");
        if (bytes.Length > maxBytes) throw new ArgumentException($"{label}超过 {maxBytes / 1024 / 1024}MB 限制");
    }

    private static string DetectImageFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        if (bytes.Length >= 16 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8) &&
            (bytes.Slice(8, 4).SequenceEqual("avif"u8) || bytes.Slice(8, 4).SequenceEqual("avis"u8)))
            return "image/avif";
        return "application/octet-stream";
    }

    private static void RequireWebpDimensions(byte[] bytes, int expectedWidth, int expectedHeight, string label)
    {
        if (DetectImageFormat(bytes) != "image/webp") throw new ArgumentException($"{label}必须是真实 WebP 文件");
        ValidateWebpContainer(bytes, label);
        var dimensions = ReadWebpDimensions(bytes);
        if (dimensions.Width != expectedWidth || dimensions.Height != expectedHeight)
            throw new ArgumentException($"{label}尺寸必须为 {expectedWidth}×{expectedHeight}，实际为 {dimensions.Width}×{dimensions.Height}");
    }

    private static void ValidateWebpContainer(ReadOnlySpan<byte> bytes, string label)
    {
        var declaredLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8;
        if (declaredLength != bytes.Length) throw new ArgumentException($"{label}的 RIFF 长度无效");
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (offset + 8 > bytes.Length) throw new ArgumentException($"{label}的 WebP 区块头不完整");
            var chunk = bytes.Slice(offset, 4);
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            var next = (long)offset + 8 + chunkLength + (chunkLength & 1);
            if (next > bytes.Length) throw new ArgumentException($"{label}的 WebP 区块长度无效");
            if (chunk.SequenceEqual("EXIF"u8) || chunk.SequenceEqual("XMP "u8) ||
                chunk.SequenceEqual("ICCP"u8) || chunk.SequenceEqual("ANIM"u8) ||
                chunk.SequenceEqual("ANMF"u8))
                throw new ArgumentException($"{label}不得携带元数据、色彩配置或动画区块");
            offset = checked((int)next);
        }
    }

    private static (int Width, int Height) ReadWebpDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30) throw new ArgumentException("WebP 文件头不完整");
        var chunk = bytes.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var width = 1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16);
            var height = 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16);
            return (width, height);
        }
        if (chunk.SequenceEqual("VP8 "u8) && bytes.Length >= 30 && bytes.Slice(23, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
            return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3fff,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3fff);
        if (chunk.SequenceEqual("VP8L"u8) && bytes[20] == 0x2f)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(21, 4));
            return ((int)(bits & 0x3fff) + 1, (int)((bits >> 14) & 0x3fff) + 1);
        }
        throw new ArgumentException("无法读取 WebP 尺寸");
    }

    private static void ValidateJsonString(JsonElement element, string property, int maxLength)
    {
        if (!element.TryGetProperty(property, out var value)) return;
        if (value.ValueKind != JsonValueKind.String || (value.GetString()?.Length ?? 0) > maxLength)
            throw new ArgumentException($"字段 {property} 必须是长度不超过 {maxLength} 的文本");
    }

    private static void ValidateHeroCopyString(JsonElement element, string property, int maxLength)
    {
        ValidateJsonString(element, property, maxLength);
        var value = JsonString(element, property);
        if (value.Any(character => char.IsControl(character) && character is not ('\r' or '\n')))
            throw new ArgumentException($"轮播字段 {property} 只能使用可见字符和手动换行");
    }

    private static string JsonString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static void ValidateOptionalSiteUrl(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.StartsWith('/') && !value.StartsWith("//")) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException($"{label}必须是站内路径或 http(s) 地址");
    }

    private static string NormalizeSiteSlug(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var slug = new string(source.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) || character == '-' ? character : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N")[..12] : LimitSiteText(slug, 80);
    }

    private static string LimitSiteText(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static void NormalizeSiteMedia(SiteMediaRow row)
    {
        row.Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id;
        row.Kind = row.Kind is { } kind && MediaPolicies.ContainsKey(kind) ? kind : "news";
        row.AltText ??= string.Empty;
        row.OriginalFileName ??= string.Empty;
        row.OriginalFormat ??= "image/webp";
        row.OriginalFile ??= string.Empty;
        row.OriginalHash ??= string.Empty;
        row.DesktopFile ??= string.Empty;
        row.DesktopHash ??= string.Empty;
        row.MobileFile ??= string.Empty;
        row.MobileHash ??= string.Empty;
        row.ThumbnailFile ??= string.Empty;
        row.ThumbnailHash ??= string.Empty;
        if (row.CreatedAt == default) row.CreatedAt = DateTimeOffset.UtcNow;
    }

    private static void NormalizeSiteCategory(SiteCategoryRow row)
    {
        row.Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id;
        row.Kind = NormalizeSiteKind(row.Kind);
        row.Name ??= string.Empty;
        row.Slug = NormalizeSiteSlug(row.Slug, row.Name);
        if (row.Version < 1) row.Version = 1;
    }
}
