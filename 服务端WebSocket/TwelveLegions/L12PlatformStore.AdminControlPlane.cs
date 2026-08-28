using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed record L12StoredAdminCommand(string Id, string Signature, L12AdminCommandView View);
internal sealed record L12StoredAdminApproval(string Status, L12AdminApprovalView View);

public sealed record L12ContentPublishItem(string Key, string DraftValue, long EntryVersion);
public sealed record L12ContentPublishCommandPayload(IReadOnlyList<L12ContentPublishItem> Items);
public sealed record L12ContentRollbackItem(string Key, string TargetValue, long EntryVersion,
    string ExpectedPublishedVersionId, string? TargetVersionId);
public sealed record L12ContentRollbackCommandPayload(string BatchId, IReadOnlyList<L12ContentRollbackItem> Items);
public sealed record L12ContentBatchItemView(string Key, string PreviousValue, string PublishedValue,
    string? PreviousVersionId, string PublishedVersionId);
public sealed record L12ContentBatchView(string Id, string Action, string? SourceBatchId, string Status,
    string ActorId, string ActorName, DateTimeOffset CreatedAt, IReadOnlyList<L12ContentBatchItemView> Items);
public sealed record L12ContentPreviewItem(string Key, string DraftValue, string PublishedValue,
    long EntryVersion, bool WouldChange);
public sealed record L12ContentBatchPreviewView(string Action, string? SourceBatchId,
    IReadOnlyList<L12ContentPreviewItem> Items);
public sealed record L12ContentBatchOperationView(bool Applied, L12ContentBatchView? Batch,
    L12ContentBatchPreviewView? Preview);

public sealed class L12ContentStateConflictException : InvalidOperationException
{
    public L12ContentStateConflictException(string message) : base(message) { }
}

public sealed partial class L12PlatformStore
{
    private sealed class AdminCommandRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string? IdempotencyKey { get; set; }
        public string Signature { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public DateTimeOffset RequestedAt { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public bool DryRun { get; set; }
        public long? ExpectedVersion { get; set; }
        public string Risk { get; set; } = "low";
        public string Status { get; set; } = "requested";
        public string Permission { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
        public string? ResultJson { get; set; }
        public string? ResultCode { get; set; }
        public string? ResultMessage { get; set; }
        public int? ResultStatusCode { get; set; }
        public string? FailureReason { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public long ResourceVersion { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class AdminApprovalRow
    {
        public string CommandId { get; set; } = string.Empty;
        public string RequesterId { get; set; } = string.Empty;
        public string RequesterName { get; set; } = string.Empty;
        public DateTimeOffset RequestedAt { get; set; }
        public string Status { get; set; } = "requested";
        public string? ReviewerId { get; set; }
        public string? ReviewerName { get; set; }
        public string? Decision { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
    }

    private sealed class ContentVersionRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string? BatchId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? PreviousVersionId { get; set; }
        public string Kind { get; set; } = "publish";
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class ContentBatchItemRow
    {
        public string Key { get; set; } = string.Empty;
        public string PreviousValue { get; set; } = string.Empty;
        public string PublishedValue { get; set; } = string.Empty;
        public string? PreviousVersionId { get; set; }
        public string PublishedVersionId { get; set; } = string.Empty;
    }

    private sealed class ContentBatchRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Action { get; set; } = "publish";
        public string? SourceBatchId { get; set; }
        public string Status { get; set; } = "published";
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public List<ContentBatchItemRow> Items { get; set; } = [];
    }

    private static readonly JsonSerializerOptions AdminJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> AllowedContentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "home.heroEyebrow", "home.headline", "home.introduction", "home.primaryCta", "home.secondaryCta",
        "home.featureLabels", "home.playTitle", "home.playText", "home.cardsTitle", "home.cardsText",
        "home.decksTitle", "home.decksText", "home.recordsTitle", "home.recordsText", "home.newsTitle",
        "home.latestNews", "home.newsEmptyTitle", "home.newsEmptyText", "home.rulesTitle", "home.cardLinkLabel",
        "home.rulesLinkLabel", "home.replayLinkLabel", "home.developmentTitle", "home.battleStatus",
        "home.s1Status", "home.s2Status", "home.mobileStatus", "rules.notice",
        // 兼容旧 platform.json 和既有平台持久化测试中的早期首页键。
        "home.hero.title",
    };

    private int _adminTransactionDepth;
    private bool _adminTransactionSaveRequested;
    private bool _adminTransactionBusinessChanged;

    internal T ExecuteAdminTransaction<T>(Func<T> action)
    {
        lock (_gate)
        {
            var outer = _adminTransactionDepth == 0;
            string? snapshot = null;
            if (outer)
            {
                snapshot = JsonSerializer.Serialize(_data);
                _adminTransactionSaveRequested = false;
                _adminTransactionBusinessChanged = false;
            }

            _adminTransactionDepth++;
            T result;
            try
            {
                result = action();
            }
            catch
            {
                _adminTransactionDepth--;
                if (outer)
                {
                    _data = JsonSerializer.Deserialize<DataFile>(snapshot!) ?? new DataFile();
                    _adminTransactionSaveRequested = false;
                    _adminTransactionBusinessChanged = false;
                }
                throw;
            }

            _adminTransactionDepth--;
            if (!outer) return result;
            try
            {
                if (_adminTransactionSaveRequested) PersistData(_adminTransactionBusinessChanged);
                return result;
            }
            catch
            {
                _data = JsonSerializer.Deserialize<DataFile>(snapshot!) ?? new DataFile();
                throw;
            }
            finally
            {
                _adminTransactionSaveRequested = false;
                _adminTransactionBusinessChanged = false;
            }
        }
    }

    internal L12StoredAdminCommand? FindAdminCommand(string actorId, string idempotencyKey)
    {
        lock (_gate)
        {
            var row = _data.AdminCommands.LastOrDefault(item => item.ActorId == actorId
                && string.Equals(item.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
            return row is null ? null : Stored(row);
        }
    }

    internal L12StoredAdminCommand? AdminCommandRecord(string commandId)
    {
        lock (_gate)
        {
            var row = _data.AdminCommands.FirstOrDefault(item => item.Id == commandId);
            return row is null ? null : Stored(row);
        }
    }

    public L12AdminCommandView? AdminCommand(string commandId) => AdminCommandRecord(commandId)?.View;

    public IReadOnlyList<L12AdminCommandView> AdminCommands(string? status = null, string? type = null,
        string? actorId = null, int limit = 200)
    {
        lock (_gate) return _data.AdminCommands
            .Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status)
            .Where(row => string.IsNullOrWhiteSpace(type) || row.Type == type)
            .Where(row => string.IsNullOrWhiteSpace(actorId) || row.ActorId == actorId)
            .OrderByDescending(row => row.RequestedAt).Take(Math.Clamp(limit, 1, 1000))
            .Select(ToView).ToArray();
    }

    public IReadOnlyList<L12AdminApprovalView> AdminApprovals(string? status = "requested", int limit = 200)
    {
        lock (_gate) return _data.AdminApprovals
            .Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status)
            .OrderByDescending(row => row.RequestedAt).Take(Math.Clamp(limit, 1, 1000))
            .Select(ToView).ToArray();
    }

    internal L12StoredAdminApproval? AdminApproval(string commandId)
    {
        lock (_gate)
        {
            var row = _data.AdminApprovals.FirstOrDefault(item => item.CommandId == commandId);
            return row is null ? null : new L12StoredAdminApproval(row.Status, ToView(row));
        }
    }

    internal L12StoredAdminCommand PersistAdminCommand<TPayload>(L12AdminCommandEnvelope<TPayload> command,
        string permission, L12AdminCommandRisk risk, string signature, string payloadJson, string status)
    {
        lock (_gate)
        {
            var row = new AdminCommandRow
            {
                Id = command.CommandId,
                IdempotencyKey = command.IdempotencyKey,
                Signature = signature,
                Type = command.Type,
                ActorId = command.Actor.Id,
                ActorName = command.Actor.Username,
                RequestedAt = command.RequestedAt,
                Scope = command.Scope,
                Reason = command.Reason,
                DryRun = command.DryRun,
                ExpectedVersion = command.ExpectedVersion,
                Risk = risk.ToString().ToLowerInvariant(),
                Status = status,
                Permission = permission,
                PayloadJson = payloadJson,
                CorrelationId = command.AuditContext.CorrelationId,
                ResourceVersion = Version,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _data.AdminCommands.Add(row);
            Save(false);
            return Stored(row);
        }
    }

    internal L12StoredAdminCommand PersistAdminCommandResult<T>(string commandId, L12AdminCommandResult<T> result,
        string status, string? failureReason)
    {
        lock (_gate)
        {
            var row = _data.AdminCommands.First(item => item.Id == commandId);
            row.Status = status;
            row.ResultJson = result.Value is null
                || result.Value is JsonElement element && element.ValueKind == JsonValueKind.Undefined
                ? null
                : JsonSerializer.Serialize(result.Value, AdminJsonOptions);
            row.ResultCode = result.Code;
            row.ResultMessage = result.Message;
            row.ResultStatusCode = result.StatusCode;
            row.FailureReason = failureReason;
            row.ResourceVersion = Version + (_adminTransactionBusinessChanged ? 1 : 0);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            Save(false);
            return Stored(row);
        }
    }

    internal void PersistAdminApprovalRequest(string commandId, L12AccountView requester)
    {
        lock (_gate)
        {
            if (_data.AdminApprovals.Any(row => row.CommandId == commandId)) return;
            _data.AdminApprovals.Add(new AdminApprovalRow
            {
                CommandId = commandId,
                RequesterId = requester.Id,
                RequesterName = requester.Username,
                RequestedAt = DateTimeOffset.UtcNow,
            });
            Save(false);
        }
    }

    internal void PersistAdminApprovalDecision(string commandId, L12AccountView reviewer, string status,
        string? reason)
    {
        lock (_gate)
        {
            var row = _data.AdminApprovals.First(item => item.CommandId == commandId);
            row.Status = status;
            row.ReviewerId = reviewer.Id;
            row.ReviewerName = reviewer.Username;
            row.Decision = status;
            row.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            row.ReviewedAt = DateTimeOffset.UtcNow;
            Save(false);
        }
    }

    internal void RecordApprovalOutcome(L12AccountView reviewer, L12AdminCommandView command,
        L12AdminAuditContext context, string outcome, string? reason)
    {
        lock (_gate)
        {
            AddAdminAudit(reviewer, "approval", outcome, command.Id, "requested", outcome, reason,
                context with
                {
                    CommandId = command.Id,
                    Permission = L12Authorization.Key(L12Permission.AdminApprovalsReview),
                    Outcome = outcome,
                    Reason = reason,
                });
            Save(false);
        }
    }

    public IReadOnlyList<string> ContentKeys() => AllowedContentKeys.OrderBy(key => key).ToArray();

    public bool IsContentKeyAllowed(string? key)
        => !string.IsNullOrWhiteSpace(key) && AllowedContentKeys.Contains(key.Trim());

    public L12ContentPublishCommandPayload CaptureContentPublish(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            var normalized = NormalizeContentKeys(keys);
            return new L12ContentPublishCommandPayload(normalized.Select(key =>
            {
                var row = FindContentEntry(key);
                var published = _data.Content.GetValueOrDefault(key, string.Empty);
                return new L12ContentPublishItem(key, row?.DraftValue ?? published, row?.Version ?? 0);
            }).ToArray());
        }
    }

    public L12ContentBatchPreviewView PreviewContentPublish(L12ContentPublishCommandPayload payload)
    {
        lock (_gate)
        {
            ValidatePublishSnapshot(payload.Items);
            return new L12ContentBatchPreviewView("publish", null, payload.Items.Select(item =>
            {
                var row = FindContentEntry(item.Key);
                var published = row?.PublishedValue ?? _data.Content.GetValueOrDefault(item.Key, string.Empty);
                return new L12ContentPreviewItem(item.Key, item.DraftValue, published, item.EntryVersion,
                    item.DraftValue != published);
            }).ToArray());
        }
    }

    public L12ContentBatchView PublishContentBatch(L12AccountView actor, L12ContentPublishCommandPayload payload,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            ValidatePublishSnapshot(payload.Items);
            var now = DateTimeOffset.UtcNow;
            var batch = new ContentBatchRow
            {
                Action = "publish",
                ActorId = actor.Id,
                ActorName = actor.Username,
                CreatedAt = now,
            };
            foreach (var item in payload.Items)
            {
                var row = EnsureContentEntry(item.Key);
                var previousVersionId = EnsurePublishedVersion(row, actor, now);
                var version = new ContentVersionRow
                {
                    BatchId = batch.Id,
                    Key = row.Key,
                    Value = item.DraftValue,
                    PreviousVersionId = previousVersionId,
                    Kind = "publish",
                    ActorId = actor.Id,
                    ActorName = actor.Username,
                    CreatedAt = now,
                };
                _data.ContentVersions.Add(version);
                batch.Items.Add(new ContentBatchItemRow
                {
                    Key = row.Key,
                    PreviousValue = row.PublishedValue,
                    PublishedValue = item.DraftValue,
                    PreviousVersionId = previousVersionId,
                    PublishedVersionId = version.Id,
                });
                AddAdminAudit(actor, "content", "publish", row.Key, row.PublishedValue, item.DraftValue,
                    batch.Id, context);
                row.PublishedValue = item.DraftValue;
                row.Status = "published";
                row.PublishedBy = actor.Username;
                row.PublishedAt = now;
                row.PublishedVersionId = version.Id;
                row.RollbackVersionId = previousVersionId;
                row.Version++;
                _data.Content[row.Key] = row.PublishedValue;
            }
            _data.ContentBatches.Add(batch);
            AddAdminAudit(actor, "content", "publish-batch", batch.Id, null,
                string.Join(',', batch.Items.Select(item => item.Key)), null, context);
            Save();
            return ToView(batch);
        }
    }

    public L12ContentRollbackCommandPayload CaptureContentRollback(string batchId)
    {
        lock (_gate)
        {
            var batch = _data.ContentBatches.FirstOrDefault(row => row.Id == batchId && row.Action == "publish");
            if (batch is null) throw new KeyNotFoundException("发布批次不存在");
            if (batch.Status != "published") throw new L12ContentStateConflictException("发布批次已经回滚或不可回滚");
            var items = batch.Items.Select(item =>
            {
                var row = FindContentEntry(item.Key)
                    ?? throw new L12ContentStateConflictException($"内容 {item.Key} 已不存在");
                if (row.PublishedVersionId != item.PublishedVersionId)
                    throw new L12ContentStateConflictException($"内容 {item.Key} 已在后续批次中变化");
                return new L12ContentRollbackItem(item.Key, item.PreviousValue, row.Version,
                    item.PublishedVersionId, item.PreviousVersionId);
            }).ToArray();
            return new L12ContentRollbackCommandPayload(batch.Id, items);
        }
    }

    public L12ContentBatchPreviewView PreviewContentRollback(L12ContentRollbackCommandPayload payload)
    {
        lock (_gate)
        {
            ValidateRollbackSnapshot(payload);
            return new L12ContentBatchPreviewView("rollback", payload.BatchId, payload.Items.Select(item =>
            {
                var row = FindContentEntry(item.Key)!;
                return new L12ContentPreviewItem(item.Key, item.TargetValue, row.PublishedValue,
                    item.EntryVersion, item.TargetValue != row.PublishedValue);
            }).ToArray());
        }
    }

    public L12ContentBatchView RollbackContentBatch(L12AccountView actor, L12ContentRollbackCommandPayload payload,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var source = ValidateRollbackSnapshot(payload);
            var now = DateTimeOffset.UtcNow;
            var batch = new ContentBatchRow
            {
                Action = "rollback",
                SourceBatchId = source.Id,
                ActorId = actor.Id,
                ActorName = actor.Username,
                CreatedAt = now,
            };
            foreach (var item in payload.Items)
            {
                var row = FindContentEntry(item.Key)!;
                var version = new ContentVersionRow
                {
                    BatchId = batch.Id,
                    Key = row.Key,
                    Value = item.TargetValue,
                    PreviousVersionId = row.PublishedVersionId,
                    Kind = "rollback",
                    ActorId = actor.Id,
                    ActorName = actor.Username,
                    CreatedAt = now,
                };
                _data.ContentVersions.Add(version);
                batch.Items.Add(new ContentBatchItemRow
                {
                    Key = row.Key,
                    PreviousValue = row.PublishedValue,
                    PublishedValue = item.TargetValue,
                    PreviousVersionId = row.PublishedVersionId,
                    PublishedVersionId = version.Id,
                });
                AddAdminAudit(actor, "content", "rollback", row.Key, row.PublishedValue, item.TargetValue,
                    source.Id, context);
                row.PublishedValue = item.TargetValue;
                row.DraftValue = item.TargetValue;
                row.Status = "published";
                row.PublishedBy = actor.Username;
                row.PublishedAt = now;
                row.PublishedVersionId = version.Id;
                row.RollbackVersionId = item.TargetVersionId;
                row.Version++;
                _data.Content[row.Key] = row.PublishedValue;
            }
            source.Status = "rolled-back";
            _data.ContentBatches.Add(batch);
            AddAdminAudit(actor, "content", "rollback-batch", batch.Id, source.Id,
                string.Join(',', batch.Items.Select(item => item.Key)), null, context);
            Save();
            return ToView(batch);
        }
    }

    public IReadOnlyList<L12ContentBatchView> ContentBatches(int limit = 100)
    {
        lock (_gate) return _data.ContentBatches.OrderByDescending(row => row.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500)).Select(ToView).ToArray();
    }

    private void ValidatePublishSnapshot(IReadOnlyList<L12ContentPublishItem> items)
    {
        if (items.Count is < 1 or > 100) throw new ArgumentException("发布批次必须包含 1–100 个内容键");
        var normalized = NormalizeContentKeys(items.Select(item => item.Key));
        if (normalized.Count != items.Count) throw new ArgumentException("发布批次包含重复内容键");
        foreach (var item in items)
        {
            var row = FindContentEntry(item.Key);
            var currentDraft = row?.DraftValue ?? _data.Content.GetValueOrDefault(item.Key, string.Empty);
            var currentVersion = row?.Version ?? 0;
            if (currentVersion != item.EntryVersion || currentDraft != item.DraftValue)
                throw new L12ContentStateConflictException($"内容 {item.Key} 的草稿已变化，请重新预览");
        }
    }

    private ContentBatchRow ValidateRollbackSnapshot(L12ContentRollbackCommandPayload payload)
    {
        var source = _data.ContentBatches.FirstOrDefault(row => row.Id == payload.BatchId && row.Action == "publish")
            ?? throw new KeyNotFoundException("发布批次不存在");
        if (source.Status != "published") throw new L12ContentStateConflictException("发布批次已经回滚或不可回滚");
        if (payload.Items.Count != source.Items.Count || payload.Items.Count == 0)
            throw new L12ContentStateConflictException("回滚批次内容不完整");
        NormalizeContentKeys(payload.Items.Select(item => item.Key));
        foreach (var item in payload.Items)
        {
            var sourceItem = source.Items.FirstOrDefault(candidate => string.Equals(candidate.Key, item.Key,
                StringComparison.OrdinalIgnoreCase));
            if (sourceItem is null || item.ExpectedPublishedVersionId != sourceItem.PublishedVersionId
                || item.TargetVersionId != sourceItem.PreviousVersionId || item.TargetValue != sourceItem.PreviousValue)
                throw new L12ContentStateConflictException($"回滚内容 {item.Key} 与原发布批次不一致");
            var row = FindContentEntry(item.Key)
                ?? throw new L12ContentStateConflictException($"内容 {item.Key} 已不存在");
            if (row.Version != item.EntryVersion || row.PublishedVersionId != item.ExpectedPublishedVersionId)
                throw new L12ContentStateConflictException($"内容 {item.Key} 已在后续批次中变化");
        }
        return source;
    }

    private static JsonElement ParseElement(string? json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

    private static JsonElement? ParseNullableElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return ParseElement(json);
    }

    private L12StoredAdminCommand Stored(AdminCommandRow row) => new(row.Id, row.Signature, ToView(row));

    private static L12AdminCommandView ToView(AdminCommandRow row) => new(row.Id, row.IdempotencyKey, row.Type,
        row.ActorId, row.ActorName, row.RequestedAt, row.Scope, row.Reason, row.DryRun, row.ExpectedVersion,
        row.Risk, row.Status, row.Permission, ParseElement(row.PayloadJson), ParseNullableElement(row.ResultJson),
        row.ResultCode, row.ResultMessage, row.ResultStatusCode, row.FailureReason, row.CorrelationId,
        row.ResourceVersion, row.UpdatedAt);

    private static L12AdminApprovalView ToView(AdminApprovalRow row) => new(row.CommandId, row.RequesterId,
        row.RequesterName, row.RequestedAt, row.Status, row.ReviewerId, row.ReviewerName, row.Decision,
        row.Reason, row.ReviewedAt);

    private static L12ContentBatchView ToView(ContentBatchRow row) => new(row.Id, row.Action, row.SourceBatchId,
        row.Status, row.ActorId, row.ActorName, row.CreatedAt, row.Items.Select(item =>
            new L12ContentBatchItemView(item.Key, item.PreviousValue, item.PublishedValue,
                item.PreviousVersionId, item.PublishedVersionId)).ToArray());

    private ContentRow? FindContentEntry(string key) => _data.ContentEntries.FirstOrDefault(item =>
        string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

    private string EnsurePublishedVersion(ContentRow row, L12AccountView actor, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(row.PublishedVersionId)) return row.PublishedVersionId;
        var baseline = new ContentVersionRow
        {
            Key = row.Key,
            Value = row.PublishedValue,
            Kind = "baseline",
            ActorId = actor.Id,
            ActorName = actor.Username,
            CreatedAt = now,
        };
        _data.ContentVersions.Add(baseline);
        row.PublishedVersionId = baseline.Id;
        return baseline.Id;
    }

    private static IReadOnlyList<string> NormalizeContentKeys(IEnumerable<string> keys)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in keys)
        {
            var key = raw?.Trim() ?? string.Empty;
            var canonical = AllowedContentKeys.FirstOrDefault(item => string.Equals(item, key,
                StringComparison.OrdinalIgnoreCase));
            if (canonical is null) throw new ArgumentException($"内容键不在白名单中：{key}");
            if (!seen.Add(canonical)) throw new ArgumentException($"内容键重复：{canonical}");
            result.Add(canonical);
        }
        if (result.Count is < 1 or > 100) throw new ArgumentException("内容批次必须包含 1–100 个键");
        return result;
    }
}
