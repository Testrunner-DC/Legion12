using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12ModianImportPreview(string ProjectId, string ProjectName, int Total,
    IReadOnlyList<L12ModianImportPreviewItem> Items);

public sealed record L12ModianImportPreviewItem(string UpdateId, int Sequence, string Title,
    DateTimeOffset OriginalPublishedAt, string State, string? ArticleId, string? ArticleStatus,
    bool LocalDraftChanged, string? CheckMessage = null);

public sealed record L12ModianImportBatchResult(string ProjectId, int Requested, int Created, int Updated,
    int Unchanged, int NeedsReimport, int Conflicted, int Failed,
    IReadOnlyList<L12ModianImportItemResult> Items);

public sealed record L12ModianImportItemResult(string UpdateId, string Status, string Message,
    string? ArticleId = null, IReadOnlyList<string>? Warnings = null);

internal sealed record L12ModianTextMark(string Type, int From, int To);

internal sealed record L12ModianBlockPlan(string Id, string Type, string Text = "",
    IReadOnlyList<L12ModianTextMark>? Marks = null, string Align = "left", int? SourceImageIndex = null,
    string Alt = "", string Caption = "");

internal sealed record L12ModianPreparedDraft(string UpdateId, string Title, string Summary,
    DateTimeOffset OriginalPublishedAt, string SourceFingerprint, IReadOnlyList<L12ModianBlockPlan> Blocks,
    IReadOnlyList<L12SiteMediaUpload> BodyImages, L12SiteMediaUpload? CoverImage,
    IReadOnlyList<string> Warnings);

internal sealed record L12ModianDraftDecision(string Status, string? ArticleId, bool LocalDraftChanged);

public sealed partial class L12PlatformStore
{
    internal const string ModianImportProvider = "modian";
    internal const string ModianProjectId = "157664";
    internal const string ModianProjectName = "十二军团摩点项目更新";

    internal L12ModianImportPreview DescribeModianUpdates(IReadOnlyList<L12ModianSourceSummary> source)
    {
        lock (_gate)
        {
            var items = source.Select(item =>
            {
                var row = FindModianArticle(item.UpdateId);
                var localChanged = row is not null && !string.IsNullOrWhiteSpace(row.ImportProjectionFingerprint)
                    && !CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(row.ImportProjectionFingerprint),
                        Encoding.ASCII.GetBytes(ArticleImportProjectionFingerprint(row)));
                var visibleTitle = L12ModianImportService.ScrubVisibleModianUrl(item.Title);
                return new L12ModianImportPreviewItem(item.UpdateId, item.Sequence, Limit(visibleTitle, 180),
                    item.OriginalPublishedAt, row is null ? "new" : localChanged ? "local-edited" : "imported",
                    row?.Id, row?.Status, localChanged);
            }).OrderByDescending(item => item.Sequence).ThenByDescending(item => item.OriginalPublishedAt).ToArray();
            return new L12ModianImportPreview(ModianProjectId, ModianProjectName, items.Length, items);
        }
    }

    internal L12ModianDraftDecision DecideModianDraft(string updateId, string sourceFingerprint,
        bool allowReimport, bool overwriteLocalChanges)
    {
        lock (_gate)
        {
            var row = FindModianArticle(updateId);
            if (row is null) return new("apply", null, false);
            var localChanged = !string.IsNullOrWhiteSpace(row.ImportProjectionFingerprint)
                && !string.Equals(row.ImportProjectionFingerprint, ArticleImportProjectionFingerprint(row),
                    StringComparison.Ordinal);
            if (string.Equals(row.ImportSourceFingerprint, sourceFingerprint, StringComparison.Ordinal))
                return new("unchanged", row.Id, localChanged);
            if (!allowReimport) return new("changed-requires-reimport", row.Id, localChanged);
            if (localChanged && !overwriteLocalChanges) return new("local-conflict", row.Id, true);
            return new("apply", row.Id, localChanged);
        }
    }

    internal L12ModianImportItemResult ApplyModianDraft(L12AccountView actor, string categoryId,
        L12ModianPreparedDraft draft, bool allowReimport, bool overwriteLocalChanges,
        L12AdminAuditContext? context = null)
    {
        var createdFiles = new List<string>();
        try
        {
            return ExecuteAdminTransaction(() =>
            {
                var decision = DecideModianDraft(draft.UpdateId, draft.SourceFingerprint, allowReimport,
                    overwriteLocalChanges);
                if (decision.Status != "apply")
                    return DecisionResult(draft.UpdateId, decision);

                var category = FindCategory("news", categoryId, null, false)
                    ?? throw new ArgumentException("导入前必须选择一个启用中的资讯分类");
                ValidatePreparedModianDraft(draft);
                var diskBefore = Directory.Exists(SiteMediaRoot)
                    ? Directory.EnumerateFiles(SiteMediaRoot).Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!)
                        .ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);

                var bodyMediaIds = new List<string>(draft.BodyImages.Count);
                foreach (var upload in draft.BodyImages)
                    bodyMediaIds.Add(ReuseOrUploadImportedMedia(actor, upload, "article", context, diskBefore,
                        createdFiles));
                var coverMediaId = draft.CoverImage is null ? null
                    : ReuseOrUploadImportedMedia(actor, draft.CoverImage, "news", context, diskBefore, createdFiles);

                var body = SerializeImportedArticleBody(draft.Blocks, bodyMediaIds);
                if (body.Length > 100_000) throw new ArgumentException("导入正文超过 100000 字符限制");
                ValidateStructuredArticleBody(body, publishing: false);

                var row = FindModianArticle(draft.UpdateId);
                var created = row is null;
                if (row is null)
                {
                    row = new ArticleRow
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = "news",
                        Status = "draft",
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedByAccountId = actor.Id,
                    };
                    _data.Articles.Add(row);
                    row.Slug = UniqueArticleSlug(row.Id, string.Empty);
                    row.CategoryId = category.Id;
                    row.Category = category.Name;
                    row.Pinned = false;
                    row.SortOrder = 0;
                }

                var previousFingerprint = row.ImportSourceFingerprint;
                row.Title = draft.Title.Trim();
                row.Summary = draft.Summary.Trim();
                row.Body = body;
                row.MediaAssetId = coverMediaId;
                row.CoverUrl = string.IsNullOrWhiteSpace(coverMediaId) ? string.Empty : SiteMediaUrl(coverMediaId);
                // 用户明确要求不插入原链接；来源 URL 既不持久化也不进入任何文章投影。
                row.Link = string.Empty;
                row.PublishAt = draft.OriginalPublishedAt;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                row.UpdatedByAccountId = actor.Id;
                row.Revision++;
                row.HasUnpublishedChanges = row.Published is null || !MatchesPublished(row);
                row.ImportProvider = ModianImportProvider;
                row.ImportProjectId = ModianProjectId;
                row.ImportItemId = draft.UpdateId;
                row.ImportSourceFingerprint = draft.SourceFingerprint;
                row.ImportedAt = DateTimeOffset.UtcNow;
                row.ImportProjectionFingerprint = ArticleImportProjectionFingerprint(row);
                AppendArticleRevision(row, actor.Id, created ? "modian-import" : "modian-reimport");
                AddAdminAudit(actor, "article-import", created ? "create-draft" : "replace-draft", row.Id,
                    previousFingerprint is null ? null : "not-imported", "draft-imported",
                    $"images={bodyMediaIds.Count};warnings={draft.Warnings.Count}",
                    context);
                Save();
                return new L12ModianImportItemResult(draft.UpdateId, created ? "created" : "updated",
                    created ? "已生成资讯草稿，未发布" : "已显式更新草稿，已发布快照未改变", row.Id,
                    draft.Warnings);
            });
        }
        catch
        {
            lock (_gate) RollBackNewMediaFiles(createdFiles);
            throw;
        }
    }

    internal void AuditModianImportFailure(L12AccountView actor, string updateId, string message,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var safeMessage = new string((message ?? string.Empty).Where(character => !char.IsControl(character))
                .Take(240).ToArray());
            AddAdminAudit(actor, "article-import", "item-failed", "fixed-project-item", null, null,
                safeMessage, context is null ? null : context with { Outcome = "failed" });
            Save(false);
        }
    }

    private ArticleRow? FindModianArticle(string updateId)
    {
        var matches = _data.Articles.Where(row => row.ImportProvider == ModianImportProvider
            && row.ImportProjectId == ModianProjectId && row.ImportItemId == updateId).Take(2).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("检测到重复的摩点导入标识，请先修复数据一致性");
        return matches.SingleOrDefault();
    }

    private static L12ModianImportItemResult DecisionResult(string updateId, L12ModianDraftDecision decision)
        => decision.Status switch
        {
            "unchanged" => new(updateId, "unchanged", "来源内容未变化，未改动草稿", decision.ArticleId),
            "changed-requires-reimport" => new(updateId, "changed-requires-reimport",
                "检测到来源变化；必须显式选择重新导入才会更新草稿", decision.ArticleId),
            "local-conflict" => new(updateId, "local-conflict",
                "草稿已有本地编辑；必须显式确认覆盖本地草稿后才能重新导入", decision.ArticleId),
            _ => throw new InvalidOperationException("未知导入决策"),
        };

    private string ReuseOrUploadImportedMedia(L12AccountView actor, L12SiteMediaUpload upload, string kind,
        L12AdminAuditContext? context, HashSet<string> diskBefore, List<string> createdFiles)
    {
        var originalHash = ContentHash(upload.Original);
        var existing = _data.SiteMedia.FirstOrDefault(row => row.DeletedAt is null && row.Kind == kind
            && row.OriginalHash == originalHash);
        if (existing is not null) return existing.Id;
        var result = UploadSiteMedia(actor, upload, context);
        var row = ActiveMedia(result.Id) ?? throw new InvalidOperationException("导入素材保存后无法读取");
        foreach (var file in new[] { row.OriginalFile, row.DesktopFile, row.MobileFile, row.ThumbnailFile })
            if (!diskBefore.Contains(file)) createdFiles.Add(file);
        return row.Id;
    }

    private static void ValidatePreparedModianDraft(L12ModianPreparedDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.UpdateId) || draft.UpdateId.Length > 32
            || draft.UpdateId.Any(character => !char.IsAsciiDigit(character)))
            throw new ArgumentException("摩点更新标识无效");
        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Title.Length > 180)
            throw new ArgumentException("导入标题不能为空且不能超过 180 字符");
        if (draft.Summary.Length > 600) throw new ArgumentException("导入摘要不能超过 600 字符");
        if (draft.Blocks.Count is 0 or > 200) throw new ArgumentException("导入正文必须包含 1 至 200 个内容块");
        if (draft.BodyImages.Count > 30) throw new ArgumentException("单篇更新最多导入 30 张正文图片");
        if (draft.SourceFingerprint.Length != 64 || draft.SourceFingerprint.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("来源指纹无效");
    }

    private static string SerializeImportedArticleBody(IReadOnlyList<L12ModianBlockPlan> blocks,
        IReadOnlyList<string> bodyMediaIds)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "l12-blocks");
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("blocks");
            foreach (var block in blocks)
            {
                writer.WriteStartObject();
                writer.WriteString("id", block.Id);
                writer.WriteString("type", block.Type);
                if (block.Type == "image")
                {
                    if (block.SourceImageIndex is not int index || index < 0 || index >= bodyMediaIds.Count)
                        throw new ArgumentException("正文图片映射无效");
                    writer.WriteString("mediaAssetId", bodyMediaIds[index]);
                    writer.WriteString("alt", block.Alt);
                    writer.WriteString("caption", block.Caption);
                }
                else if (block.Type != "divider")
                {
                    writer.WriteString("text", block.Text);
                    writer.WriteStartArray("marks");
                    foreach (var mark in block.Marks ?? [])
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", mark.Type);
                        writer.WriteNumber("from", mark.From);
                        writer.WriteNumber("to", mark.To);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteString("align", block.Align);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ArticleImportProjectionFingerprint(ArticleRow row)
    {
        var projection = JsonSerializer.Serialize(new
        {
            row.Title, row.Summary, row.Body, row.MediaAssetId, row.Link, row.PublishAt,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projection))).ToLowerInvariant();
    }
}
