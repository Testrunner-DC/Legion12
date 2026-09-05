using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12ArticleDraft(
    string? Id,
    string Title,
    string Summary,
    string Body,
    string Category,
    string CoverUrl,
    string Link,
    string Slug,
    bool Pinned,
    DateTimeOffset? PublishAt,
    long? ExpectedRevision = null,
    string Kind = "news",
    string? CategoryId = null,
    string? MediaAssetId = null,
    int SortOrder = 0);

public sealed record L12ArticleView(
    string Id,
    string Title,
    string Summary,
    string Body,
    string Category,
    string CoverUrl,
    string Link,
    string Slug,
    bool Pinned,
    string Status,
    bool HasUnpublishedChanges,
    DateTimeOffset? PublishAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    string Author,
    string UpdatedBy,
    string? PublishedBy,
    long Revision,
    string Kind = "news",
    string? CategoryId = null,
    string? MediaAssetId = null,
    int SortOrder = 0);

public sealed record L12ArticleRevisionView(
    long Revision,
    string Action,
    string Title,
    string Summary,
    string Body,
    string Category,
    string CoverUrl,
    string Link,
    string Slug,
    bool Pinned,
    DateTimeOffset? PublishAt,
    string Actor,
    DateTimeOffset CreatedAt,
    string Kind = "news",
    string? CategoryId = null,
    string? MediaAssetId = null,
    int SortOrder = 0);

public sealed partial class L12PlatformStore
{

    private sealed class ArticlePublishedRow
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Category { get; set; } = "官方公告";
        public string CoverUrl { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public bool Pinned { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public string PublishedByAccountId { get; set; } = string.Empty;
        public string Kind { get; set; } = "news";
        public string? CategoryId { get; set; }
        public string? MediaAssetId { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class ArticleRevisionRow
    {
        public long Revision { get; set; }
        public string Action { get; set; } = "save-draft";
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Category { get; set; } = "官方公告";
        public string CoverUrl { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public bool Pinned { get; set; }
        public DateTimeOffset? PublishAt { get; set; }
        public string ActorAccountId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string Kind { get; set; } = "news";
        public string? CategoryId { get; set; }
        public string? MediaAssetId { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class ArticleRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Category { get; set; } = "官方公告";
        public string CoverUrl { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public bool Pinned { get; set; }
        public string Status { get; set; } = "draft";
        public bool HasUnpublishedChanges { get; set; } = true;
        public DateTimeOffset? PublishAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string CreatedByAccountId { get; set; } = string.Empty;
        public string UpdatedByAccountId { get; set; } = string.Empty;
        public long Revision { get; set; }
        public ArticlePublishedRow? Published { get; set; }
        public List<ArticleRevisionRow> Revisions { get; set; } = [];
        public string Kind { get; set; } = "news";
        public string? CategoryId { get; set; }
        public string? MediaAssetId { get; set; }
        public int SortOrder { get; set; }
    }

    private void EnsureArticleState()
    {
        lock (_gate)
        {
            if (_data.Articles.Count > 0) return;
            var legacy = _data.ContentEntries.FirstOrDefault(item =>
                string.Equals(item.Key, "news.entries", StringComparison.OrdinalIgnoreCase));
            var published = legacy?.PublishedValue ?? _data.Content.GetValueOrDefault("news.entries", string.Empty);
            var draft = legacy?.DraftValue ?? published;
            var changed = ImportLegacyArticles(published, true);
            changed |= ImportLegacyArticles(draft, false);
            if (changed) Save();
        }
    }

    private bool ImportLegacyArticles(string json, bool publishedSource)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var changed = false;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var id = LegacyString(element, "id");
                if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
                var row = _data.Articles.FirstOrDefault(item => item.Id == id);
                if (row is null)
                {
                    row = new ArticleRow { Id = id, CreatedAt = DateTimeOffset.UtcNow };
                    _data.Articles.Add(row);
                }
                row.Title = Limit(LegacyString(element, "title"), 180);
                row.Summary = Limit(LegacyString(element, "summary"), 600);
                row.Body = Limit(LegacyString(element, "body"), 100_000);
                row.Kind = "news";
                row.Category = NormalizeLegacyArticleCategory(LegacyString(element, "category"));
                row.CoverUrl = NormalizeOptionalUrl(LegacyString(element, "coverUrl"), "封面图片地址", allowRelative: true);
                row.Pinned = LegacyBool(element, "pinned");
                row.PublishAt = LegacyDate(element, "publishedAt");
                row.Slug = UniqueArticleSlug(row.Id, string.Empty);
                row.UpdatedAt = DateTimeOffset.UtcNow;
                row.Revision = Math.Max(1, row.Revision);
                var shouldPublish = publishedSource && LegacyBool(element, "published");
                if (shouldPublish)
                {
                    var publishedAt = row.PublishAt ?? DateTimeOffset.UtcNow;
                    row.Published = PublishedSnapshot(row, publishedAt, string.Empty);
                    row.Status = publishedAt > DateTimeOffset.UtcNow ? "scheduled" : "published";
                    row.HasUnpublishedChanges = false;
                }
                else if (row.Published is null)
                {
                    row.Status = "draft";
                    row.HasUnpublishedChanges = true;
                }
                else
                {
                    row.HasUnpublishedChanges = !MatchesPublished(row);
                }
                changed = true;
            }
            return changed;
        }
        catch (JsonException) { return false; }
    }

    public IReadOnlyList<L12ArticleView> PublicArticles(string? category = null, string? search = null,
        int limit = 100, string kind = "news")
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var normalizedKind = NormalizeSiteKind(kind);
            return _data.Articles
                .Where(row => row.Published is not null &&
                    (row.Status == "published" || row.Status == "scheduled" && row.PublishAt <= now))
                .Where(row => row.Published!.Kind == normalizedKind)
                .Where(row => string.IsNullOrWhiteSpace(category) ||
                    string.Equals(row.Published!.Category, category.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(row.Published!.CategoryId, category.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(row => MatchesArticleSearch(row.Published!, search))
                .OrderByDescending(row => row.Published!.Pinned)
                .ThenBy(row => row.Published!.SortOrder)
                .ThenByDescending(row => row.Published!.PublishedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .Select(ToPublicArticleView)
                .ToArray();
        }
    }

    public IReadOnlyList<L12ArticleView> AdminArticles(string? status = null, string? category = null,
        string? search = null, int limit = 300, string kind = "news")
    {
        lock (_gate)
        {
            var normalizedKind = NormalizeSiteKind(kind);
            return _data.Articles
                .Where(row => row.Kind == normalizedKind)
                .Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status.Trim())
                .Where(row => string.IsNullOrWhiteSpace(category) || row.Category == category.Trim() ||
                    row.CategoryId == category.Trim())
                .Where(row => MatchesArticleSearch(row, search))
                .OrderBy(row => row.SortOrder).ThenByDescending(row => row.UpdatedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .Select(ToAdminArticleView)
                .ToArray();
        }
    }

    public L12ArticleView? AdminArticle(string id)
    {
        lock (_gate)
        {
            var row = FindArticle(id);
            return row is null ? null : ToAdminArticleView(row);
        }
    }

    public L12ArticleView SaveArticleDraft(L12AccountView actor, L12ArticleDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var kind = RequireSiteKind(draft.Kind);
            var row = string.IsNullOrWhiteSpace(draft.Id) ? null : FindArticle(draft.Id);
            if (row is null)
            {
                row = new ArticleRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = kind,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByAccountId = actor.Id,
                };
                _data.Articles.Add(row);
            }
            else if (draft.ExpectedRevision is not null && row.Revision != draft.ExpectedRevision)
                throw new InvalidOperationException("稿件已被其他操作更新，请刷新后重试");
            else if (row.Kind != kind)
                throw new ArgumentException("稿件建立后不能改变内容类型");

            var category = FindCategory(kind, draft.CategoryId, draft.Category, true)
                ?? throw new ArgumentException("所选分类不存在");
            SiteMediaRow? media = null;
            if (!string.IsNullOrWhiteSpace(draft.MediaAssetId))
            {
                media = ActiveMedia(draft.MediaAssetId) ?? throw new ArgumentException("所选封面素材不存在或已删除");
                if (media.Kind != kind) throw new ArgumentException("封面素材类型与稿件类型不一致");
            }
            else if (!string.IsNullOrWhiteSpace(draft.CoverUrl))
                throw new ArgumentException("封面不能填写图片链接，请通过站点素材上传");

            row.Title = Limit(draft.Title, 180);
            row.Summary = Limit(draft.Summary, 600);
            row.Body = Limit(draft.Body, 100_000);
            row.Kind = kind;
            row.CategoryId = category.Id;
            row.Category = category.Name;
            row.MediaAssetId = media?.Id;
            row.CoverUrl = media is null ? string.Empty : SiteMediaUrl(media.Id);
            row.Link = NormalizeOptionalUrl(draft.Link, "文章链接", allowRelative: true);
            row.Slug = UniqueArticleSlug(row.Id, draft.Slug);
            row.Pinned = draft.Pinned;
            row.SortOrder = Math.Clamp(draft.SortOrder, 0, 100_000);
            row.PublishAt = draft.PublishAt;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAccountId = actor.Id;
            row.Revision++;
            row.HasUnpublishedChanges = row.Published is null || !MatchesPublished(row);
            if (row.Status is "withdrawn" or "archived") row.Status = "draft";
            AppendArticleRevision(row, actor.Id, "save-draft");
            AddAdminAudit(actor, "article", "save-draft", row.Id, null, row.Title, null, context);
            Save();
            return ToAdminArticleView(row);
        }
    }

    public L12ArticleView PublishArticle(L12AccountView actor, string id,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = FindArticle(id) ?? throw new KeyNotFoundException("稿件不存在");
            if (string.IsNullOrWhiteSpace(row.Title)) throw new ArgumentException("发布前必须填写标题");
            if (string.IsNullOrWhiteSpace(row.Body)) throw new ArgumentException("发布前必须填写正文");
            var category = FindCategory(row.Kind, row.CategoryId, row.Category, false)
                ?? throw new ArgumentException("发布前必须选择启用的分类");
            row.CategoryId = category.Id;
            row.Category = category.Name;
            if (row.Kind is "video" or "product" && ActiveMedia(row.MediaAssetId) is null)
                throw new ArgumentException(row.Kind == "video" ? "视频发布前必须上传封面" : "商品发布前必须上传商品图");
            var publishAt = row.PublishAt ?? DateTimeOffset.UtcNow;
            row.PublishAt = publishAt;
            row.Published = PublishedSnapshot(row, publishAt, actor.Id);
            row.Status = publishAt > DateTimeOffset.UtcNow ? "scheduled" : "published";
            row.HasUnpublishedChanges = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAccountId = actor.Id;
            row.Revision++;
            AppendArticleRevision(row, actor.Id, row.Status);
            AddAdminAudit(actor, "article", row.Status, row.Id, null, row.Title, null, context);
            Save();
            return ToAdminArticleView(row);
        }
    }

    public L12ArticleView ChangeArticleStatus(L12AccountView actor, string id, string action,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = FindArticle(id) ?? throw new KeyNotFoundException("稿件不存在");
            var previous = row.Status;
            row.Status = action switch
            {
                "withdraw" => "withdrawn",
                "archive" => "archived",
                "restore" => "draft",
                _ => throw new ArgumentException("无效的稿件状态操作"),
            };
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAccountId = actor.Id;
            row.Revision++;
            AppendArticleRevision(row, actor.Id, action);
            AddAdminAudit(actor, "article", action, row.Id, previous, row.Status, null, context);
            Save();
            return ToAdminArticleView(row);
        }
    }

    public IReadOnlyList<L12ArticleRevisionView> ArticleRevisions(string id)
    {
        lock (_gate)
        {
            var row = FindArticle(id) ?? throw new KeyNotFoundException("稿件不存在");
            return row.Revisions.OrderByDescending(item => item.Revision).Select(item => new L12ArticleRevisionView(
                item.Revision, item.Action, item.Title, item.Summary, item.Body, item.Category, item.CoverUrl,
                item.Link, item.Slug, item.Pinned, item.PublishAt, ArticleAccountName(item.ActorAccountId), item.CreatedAt,
                item.Kind, item.CategoryId, item.MediaAssetId, item.SortOrder)).ToArray();
        }
    }

    public L12ArticleView RestoreArticleRevision(L12AccountView actor, string id, long revision,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = FindArticle(id) ?? throw new KeyNotFoundException("稿件不存在");
            var snapshot = row.Revisions.FirstOrDefault(item => item.Revision == revision)
                ?? throw new KeyNotFoundException("稿件历史版本不存在");
            row.Title = snapshot.Title;
            row.Summary = snapshot.Summary;
            row.Body = snapshot.Body;
            row.Category = snapshot.Category;
            row.CoverUrl = snapshot.CoverUrl;
            row.Link = snapshot.Link;
            row.Slug = UniqueArticleSlug(row.Id, snapshot.Slug);
            row.Pinned = snapshot.Pinned;
            row.PublishAt = snapshot.PublishAt;
            row.Kind = snapshot.Kind;
            row.CategoryId = snapshot.CategoryId;
            row.MediaAssetId = snapshot.MediaAssetId;
            row.SortOrder = snapshot.SortOrder;
            row.CoverUrl = string.IsNullOrWhiteSpace(snapshot.MediaAssetId)
                ? snapshot.CoverUrl : SiteMediaUrl(snapshot.MediaAssetId);
            row.Status = "draft";
            row.HasUnpublishedChanges = true;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByAccountId = actor.Id;
            row.Revision++;
            AppendArticleRevision(row, actor.Id, $"restore:{revision}");
            AddAdminAudit(actor, "article", "restore-revision", row.Id, revision.ToString(), row.Revision.ToString(), null, context);
            Save();
            return ToAdminArticleView(row);
        }
    }

    private ArticleRow? FindArticle(string id) => _data.Articles.FirstOrDefault(item => item.Id == id.Trim());

    private L12ArticleView ToAdminArticleView(ArticleRow row) => new(row.Id, row.Title, row.Summary, row.Body,
        row.Category, string.IsNullOrWhiteSpace(row.MediaAssetId) ? row.CoverUrl : SiteMediaUrl(row.MediaAssetId),
        row.Link, row.Slug, row.Pinned, row.Status, row.HasUnpublishedChanges,
        row.PublishAt, row.CreatedAt, row.UpdatedAt, row.Published?.PublishedAt, ArticleAccountName(row.CreatedByAccountId),
        ArticleAccountName(row.UpdatedByAccountId), row.Published is null ? null : ArticleAccountName(row.Published.PublishedByAccountId),
        row.Revision, row.Kind, row.CategoryId, row.MediaAssetId, row.SortOrder);

    private L12ArticleView ToPublicArticleView(ArticleRow row)
    {
        var value = row.Published!;
        var coverUrl = string.IsNullOrWhiteSpace(value.MediaAssetId) ? value.CoverUrl : SiteMediaUrl(value.MediaAssetId);
        return new L12ArticleView(row.Id, value.Title, value.Summary, value.Body, value.Category, coverUrl,
            value.Link, value.Slug, value.Pinned, "published", false, value.PublishedAt, row.CreatedAt,
            value.PublishedAt, value.PublishedAt, ArticleAccountName(row.CreatedByAccountId), ArticleAccountName(value.PublishedByAccountId),
            ArticleAccountName(value.PublishedByAccountId), row.Revision, value.Kind, value.CategoryId,
            value.MediaAssetId, value.SortOrder);
    }

    private string ArticleAccountName(string accountId) => string.IsNullOrWhiteSpace(accountId) ? "系统迁移"
        : _data.Accounts.FirstOrDefault(item => item.Id == accountId)?.Username ?? "已注销管理员";

    private void AppendArticleRevision(ArticleRow row, string actorId, string action)
    {
        row.Revisions.Add(new ArticleRevisionRow
        {
            Revision = row.Revision,
            Action = action,
            Title = row.Title,
            Summary = row.Summary,
            Body = row.Body,
            Category = row.Category,
            CoverUrl = row.CoverUrl,
            Link = row.Link,
            Slug = row.Slug,
            Pinned = row.Pinned,
            PublishAt = row.PublishAt,
            Kind = row.Kind,
            CategoryId = row.CategoryId,
            MediaAssetId = row.MediaAssetId,
            SortOrder = row.SortOrder,
            ActorAccountId = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        if (row.Revisions.Count > 50)
            row.Revisions = row.Revisions.OrderByDescending(item => item.Revision).Take(50).OrderBy(item => item.Revision).ToList();
    }

    private static ArticlePublishedRow PublishedSnapshot(ArticleRow row, DateTimeOffset publishedAt, string actorId) => new()
    {
        Title = row.Title,
        Summary = row.Summary,
        Body = row.Body,
        Category = row.Category,
        CoverUrl = row.CoverUrl,
        Link = row.Link,
        Slug = row.Slug,
        Pinned = row.Pinned,
        PublishedAt = publishedAt,
        PublishedByAccountId = actorId,
        Kind = row.Kind,
        CategoryId = row.CategoryId,
        MediaAssetId = row.MediaAssetId,
        SortOrder = row.SortOrder,
    };

    private static bool MatchesPublished(ArticleRow row) => row.Published is { } value
        && row.Title == value.Title && row.Summary == value.Summary && row.Body == value.Body
        && row.Category == value.Category && row.CoverUrl == value.CoverUrl && row.Link == value.Link
        && row.Slug == value.Slug && row.Pinned == value.Pinned && row.PublishAt == value.PublishedAt
        && row.Kind == value.Kind && row.CategoryId == value.CategoryId && row.MediaAssetId == value.MediaAssetId
        && row.SortOrder == value.SortOrder;

    private static bool MatchesArticleSearch(ArticleRow row, string? search)
        => string.IsNullOrWhiteSpace(search) || new[] { row.Title, row.Summary, row.Body, row.Category, row.Slug }
            .Any(value => value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool MatchesArticleSearch(ArticlePublishedRow row, string? search)
        => string.IsNullOrWhiteSpace(search) || new[] { row.Title, row.Summary, row.Body, row.Category, row.Slug }
            .Any(value => value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    private string UniqueArticleSlug(string articleId, string slug)
    {
        var normalized = new string((slug ?? string.Empty).Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-')
            .ToArray()).Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal)) normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized)) normalized = $"article-{articleId[..Math.Min(12, articleId.Length)]}";
        normalized = Limit(normalized, 100);
        if (_data.Articles.Any(item => item.Id != articleId && string.Equals(item.Slug, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("文章链接标识已被使用");
        return normalized;
    }

    private static string NormalizeLegacyArticleCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? "官方公告" : Limit(category, 40);

    private static string NormalizeOptionalUrl(string? value, string label, bool allowRelative = false)
    {
        var normalized = Limit(value, 2000);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
        if (allowRelative && normalized.StartsWith('/') && !normalized.StartsWith("//", StringComparison.Ordinal)) return normalized;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException($"{label}必须是 http(s) 地址{(allowRelative ? "或站内路径" : string.Empty)}");
        return normalized;
    }

    private static string Limit(string? value, int length)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized[..Math.Min(normalized.Length, length)];
    }

    private static string LegacyString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static bool LegacyBool(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True;
    private static DateTimeOffset? LegacyDate(JsonElement element, string property)
        => DateTimeOffset.TryParse(LegacyString(element, property), out var value) ? value : null;

    private static void NormalizeArticle(ArticleRow row)
    {
        row.Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id;
        row.Title ??= string.Empty;
        row.Summary ??= string.Empty;
        row.Body ??= string.Empty;
        row.Kind = NormalizeSiteKind(row.Kind);
        row.Category = NormalizeLegacyArticleCategory(row.Category);
        row.CoverUrl ??= string.Empty;
        row.MediaAssetId = string.IsNullOrWhiteSpace(row.MediaAssetId) ? null : row.MediaAssetId.Trim();
        row.CategoryId = string.IsNullOrWhiteSpace(row.CategoryId) ? null : row.CategoryId.Trim();
        row.Link ??= string.Empty;
        row.Slug = string.IsNullOrWhiteSpace(row.Slug) ? $"article-{row.Id[..Math.Min(12, row.Id.Length)]}" : row.Slug;
        row.Status = row.Status is "draft" or "published" or "scheduled" or "withdrawn" or "archived" ? row.Status : "draft";
        row.Revisions ??= [];
        foreach (var revision in row.Revisions)
        {
            revision.Kind = NormalizeSiteKind(revision.Kind);
            revision.Category = NormalizeLegacyArticleCategory(revision.Category);
            revision.CoverUrl ??= string.Empty;
            revision.MediaAssetId = string.IsNullOrWhiteSpace(revision.MediaAssetId) ? null : revision.MediaAssetId.Trim();
            revision.CategoryId = string.IsNullOrWhiteSpace(revision.CategoryId) ? null : revision.CategoryId.Trim();
        }
        if (row.Published is { } published)
        {
            published.Kind = NormalizeSiteKind(published.Kind);
            published.Category = NormalizeLegacyArticleCategory(published.Category);
            published.CoverUrl ??= string.Empty;
            published.MediaAssetId = string.IsNullOrWhiteSpace(published.MediaAssetId) ? null : published.MediaAssetId.Trim();
            published.CategoryId = string.IsNullOrWhiteSpace(published.CategoryId) ? null : published.CategoryId.Trim();
        }
        if (row.CreatedAt == default) row.CreatedAt = DateTimeOffset.UtcNow;
        if (row.UpdatedAt == default) row.UpdatedAt = row.CreatedAt;
    }
}
