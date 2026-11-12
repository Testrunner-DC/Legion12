using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwelveLegions.Server;

public sealed record L12RuleItemPublishTarget(string Key, string Collection, string ItemId);
public sealed record L12RuleItemsPublishRequest(IReadOnlyList<L12RuleItemPublishTarget> Items,
    long? RulingsVersion = null, long? CenterVersion = null, string? IdempotencyKey = null,
    bool DryRun = false, string? Reason = null);
public sealed record L12RuleItemsPublishResult(IReadOnlyList<L12ContentEntryView> Entries, int PublishedCount);

public sealed partial class L12PlatformStore
{
    public L12RuleItemsPublishResult PublishRuleItemsBatch(L12AccountView actor,
        L12RuleItemsPublishRequest request, L12AdminAuditContext? context = null)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.AdminContentPublish))
            throw new UnauthorizedAccessException("当前账号没有批量发布规则内容的权限");

        var targets = (request.Items ?? [])
            .Where(item => item is not null)
            .Select(item => new L12RuleItemPublishTarget(item.Key?.Trim() ?? string.Empty,
                item.Collection?.Trim() ?? string.Empty, item.ItemId?.Trim() ?? string.Empty))
            .Where(item => item.Key.Length > 0 && item.Collection.Length > 0 && item.ItemId.Length > 0)
            .DistinctBy(item => $"{item.Key.ToLowerInvariant()}\n{item.Collection.ToLowerInvariant()}\n{item.ItemId.ToLowerInvariant()}")
            .ToArray();
        if (targets.Length == 0) throw new ArgumentException("没有选择待发布的规则条目");
        if (targets.Length > 500) throw new ArgumentException("单次最多发布 500 条规则内容");

        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["rules.rulings"] = ["entries"],
            ["rules.center"] = RuleCenterCollections,
        };
        foreach (var group in targets.GroupBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!allowed.TryGetValue(group.Key, out var collections)
                || group.Any(item => !collections.Contains(item.Collection, StringComparer.Ordinal)))
                throw new ArgumentException("规则条目标识无效");
        }

        lock (_gate)
        {
            var rows = targets.Select(item => (item, row: EnsureContentEntry(item.Key)))
                .GroupBy(item => item.row.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().row, StringComparer.Ordinal);
            if (rows.TryGetValue("rules.rulings", out var rulingsRow)
                && request.RulingsVersion is { } rulingsVersion && rulingsRow.Version != rulingsVersion)
                throw new L12ContentStateConflictException("规则裁定草稿版本已变化，请刷新后重试");
            if (rows.TryGetValue("rules.center", out var centerRow)
                && request.CenterVersion is { } centerVersion && centerRow.Version != centerVersion)
                throw new L12ContentStateConflictException("规则中心草稿版本已变化，请刷新后重试");

            // Validate every selected target before mutating either document. This keeps a
            // mixed rules-center/FAQ batch all-or-nothing even when one item was removed.
            var drafts = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var publishedDocuments = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var (key, row) in rows)
            {
                var draftValue = NormalizeRuleRulingProducts(key, row.DraftValue);
                drafts[key] = JsonNode.Parse(draftValue) as JsonObject
                    ?? throw new ArgumentException("规则草稿不是有效对象");
                publishedDocuments[key] = string.IsNullOrWhiteSpace(row.PublishedValue)
                    ? key == "rules.center"
                        ? CreateEmptyPublishedRuleCenter(drafts[key])
                        : new JsonObject { ["entries"] = new JsonArray() }
                    : JsonNode.Parse(row.PublishedValue) as JsonObject ?? new JsonObject();
            }

            foreach (var target in targets)
            {
                var draft = drafts[target.Key];
                var draftItems = draft[target.Collection] as JsonArray
                    ?? throw new ArgumentException("规则草稿缺少指定分组");
                var selected = draftItems.OfType<JsonObject>().FirstOrDefault(item =>
                    string.Equals(item["id"]?.GetValue<string>(), target.ItemId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new KeyNotFoundException("规则草稿中找不到指定条目");
                selected["status"] = "published";
            }

            var now = DateTimeOffset.UtcNow;
            var batch = new ContentBatchRow
            {
                Action = "rule-item-publish-batch",
                SourceBatchId = string.Join(',', targets.Select(item =>
                    $"{item.Key}/{item.Collection}/{item.ItemId}")),
                ActorId = actor.Id,
                ActorName = actor.Username,
                CreatedAt = now,
            };
            foreach (var group in targets.GroupBy(item => item.Key, StringComparer.Ordinal))
            {
                var key = group.Key;
                var row = rows[key];
                var draft = drafts[key];
                var published = publishedDocuments[key];
                foreach (var target in group)
                {
                    var draftItems = draft[target.Collection]!.AsArray();
                    var selected = draftItems.OfType<JsonObject>().First(item =>
                        string.Equals(item["id"]?.GetValue<string>(), target.ItemId, StringComparison.OrdinalIgnoreCase));
                    var publicItems = published[target.Collection] as JsonArray ?? new JsonArray();
                    published[target.Collection] = publicItems;
                    var existingIndex = -1;
                    for (var index = 0; index < publicItems.Count; index++)
                        if (publicItems[index] is JsonObject current && string.Equals(
                                current["id"]?.GetValue<string>(), target.ItemId, StringComparison.OrdinalIgnoreCase))
                        {
                            existingIndex = index;
                            break;
                        }
                    var publicItem = selected.DeepClone();
                    if (existingIndex >= 0) publicItems[existingIndex] = publicItem;
                    else publicItems.Add(publicItem);
                    if (key == "rules.center")
                        published[target.Collection] = OrderPublishedRuleItems(draftItems, publicItems);
                }

                var previous = row.PublishedValue;
                var publicValue = PreparePublicContentValue(key,
                    published.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                ValidateSiteContentValue(key, publicValue, true);
                var previousVersionId = EnsurePublishedVersion(row, actor, now);
                var version = new ContentVersionRow
                {
                    BatchId = batch.Id,
                    Key = key,
                    Value = publicValue,
                    PreviousVersionId = previousVersionId,
                    Kind = "rule-item-publish-batch",
                    ActorId = actor.Id,
                    ActorName = actor.Username,
                    CreatedAt = now,
                };
                _data.ContentVersions.Add(version);
                batch.Items.Add(new ContentBatchItemRow
                {
                    Key = key,
                    PreviousValue = previous,
                    PublishedValue = publicValue,
                    PreviousVersionId = previousVersionId,
                    PublishedVersionId = version.Id,
                });
                foreach (var target in group)
                    AddAdminAudit(actor, "rule-item", "publish", $"{key}/{target.Collection}/{target.ItemId}",
                        previous, publicValue, batch.Id, context);
                row.DraftValue = draft.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                row.PublishedValue = publicValue;
                row.Status = row.DraftValue == row.PublishedValue ? "published" : "draft";
                row.UpdatedBy = actor.Username;
                row.UpdatedAt = now;
                row.PublishedBy = actor.Username;
                row.PublishedAt = now;
                row.Version++;
                row.PublishedVersionId = version.Id;
                row.RollbackVersionId = previousVersionId;
                _data.Content[key] = publicValue;
            }
            _data.ContentBatches.Add(batch);
            AddAdminAudit(actor, "rule-item", "publish-batch", batch.Id, null,
                string.Join(',', targets.Select(item => $"{item.Key}/{item.Collection}/{item.ItemId}")),
                batch.Id, context);
            Save();
            return new(rows.Values.Select(ToView).ToArray(), targets.Length);
        }
    }

    private static JsonObject CreateEmptyPublishedRuleCenter(JsonObject draft)
    {
        var published = draft.DeepClone().AsObject();
        foreach (var collection in RuleCenterCollections) published[collection] = new JsonArray();
        return published;
    }
}
