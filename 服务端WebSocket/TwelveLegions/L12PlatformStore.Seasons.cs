namespace TwelveLegions.Server;

public sealed record L12SeasonScopedConfig(
    L12SeasonDisasterPoolConfig DisasterPool,
    IReadOnlyList<L12CardRestrictionConfig> CardRestrictions,
    IReadOnlyList<string> DefaultPresetDeckIds,
    L12RankedConfigView Ranked);

public sealed record L12SeasonDefinitionDraft(
    string SeasonId,
    string Name,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    L12SeasonScopedConfig Configuration);

public sealed record L12SeasonDefinitionView(
    string DefinitionId,
    string SeasonId,
    string Name,
    string LifecycleStatus,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    long Revision,
    string? PreviousSeasonId,
    string? NextSeasonId,
    L12SeasonScopedConfig Configuration,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string UpdatedBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt = null,
    L12SeasonActivationPlanView? ActivationPlan = null);

public sealed record L12SeasonActivationPlanView(
    string Status,
    long Generation,
    DateTimeOffset ScheduledAt,
    long ArmedCurrentRevision,
    long ArmedDraftRevision,
    long ArmedOperationsVersion,
    string IntentKey,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? CompletedAt);

internal sealed record L12SeasonActivationClaim(
    string DefinitionId,
    long Generation,
    string IntentKey,
    string LeaseOwner,
    DateTimeOffset LeaseExpiresAt,
    long ExpectedCurrentRevision,
    long ExpectedDraftRevision,
    long ExpectedOperationsVersion);

public sealed record L12SeasonArchiveView(
    string ArchiveId,
    string SourceDefinitionId,
    string SeasonId,
    string Name,
    long DefinitionRevision,
    string? PreviousSeasonId,
    string? NextSeasonId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    L12SeasonScopedConfig Configuration,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset ArchivedAt,
    string ArchivedBy);

public sealed record L12SeasonCatalogView(
    L12SeasonDefinitionView Current,
    L12SeasonDefinitionView? Next,
    IReadOnlyList<L12SeasonArchiveView> Archives,
    bool AutomaticActivationEnabled,
    long OperationsVersion);

public sealed record L12SeasonDefinitionPreviewView(
    bool Valid,
    string Slot,
    long CurrentRevision,
    long NextRevision,
    long OperationsVersion,
    L12SeasonDefinitionDraft Normalized,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Warnings,
    string PreviewToken);

public sealed record L12SeasonDefinitionOperationView(
    bool Applied,
    string Slot,
    L12SeasonDefinitionView Definition,
    long PreviousRevision,
    long OperationsVersion,
    IReadOnlyList<string> Changes);

public sealed record L12RankedSeasonCutoverReadiness(
    string SeasonId,
    int ActiveMatches,
    int PendingSettlements,
    int AppliedReconciliationFailures,
    int QuarantinedSettlements)
{
    public bool Ready => ActiveMatches == 0 && PendingSettlements == 0
        && AppliedReconciliationFailures == 0 && QuarantinedSettlements == 0;
}

public sealed record L12SeasonActivationView(
    bool Activated,
    L12SeasonDefinitionView Current,
    L12SeasonArchiveView Archive,
    L12RankedSeasonCutoverReadiness Readiness,
    long OperationsVersion);

public sealed partial class L12PlatformStore
{
    private const int CurrentSeasonLifecycleMigrationVersion = 1;
    internal Action<string>? SeasonActivationFailureInjector { get; set; }

    private sealed class SeasonScopedConfigRow
    {
        public OperationsDisasterPoolRow DisasterPool { get; set; } = new();
        public List<OperationsCardRestrictionRow> CardRestrictions { get; set; } = [];
        public List<string> DefaultPresetDeckIds { get; set; } = [];
        public RankedConfigRow Ranked { get; set; } = new();
    }

    private sealed class SeasonDefinitionRow
    {
        public string DefinitionId { get; set; } = Guid.NewGuid().ToString("N");
        public string SeasonId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string LifecycleStatus { get; set; } = "draft";
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public long Revision { get; set; } = 1;
        public string? PreviousSeasonId { get; set; }
        public string? NextSeasonId { get; set; }
        public SeasonScopedConfigRow Configuration { get; set; } = new();
        public string CreatedBy { get; set; } = "系统迁移";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string UpdatedBy { get; set; } = "系统迁移";
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ActivatedAt { get; set; }
        public SeasonActivationPlanRow? ActivationPlan { get; set; }
    }

    private sealed class SeasonActivationPlanRow
    {
        public string Status { get; set; } = "armed";
        public long Generation { get; set; } = 1;
        public DateTimeOffset ScheduledAt { get; set; }
        public long ArmedCurrentRevision { get; set; }
        public long ArmedDraftRevision { get; set; }
        public long ArmedOperationsVersion { get; set; }
        public string IntentKey { get; set; } = string.Empty;
        public string? LeaseOwner { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }
        public int AttemptCount { get; set; }
        public DateTimeOffset? LastAttemptAt { get; set; }
        public string? LastErrorCode { get; set; }
        public string? LastErrorMessage { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    private sealed class SeasonArchiveRow
    {
        public string ArchiveId { get; set; } = Guid.NewGuid().ToString("N");
        public string SourceDefinitionId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long DefinitionRevision { get; set; }
        public string? PreviousSeasonId { get; set; }
        public string? NextSeasonId { get; set; }
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public SeasonScopedConfigRow Configuration { get; set; } = new();
        public DateTimeOffset? ActivatedAt { get; set; }
        public DateTimeOffset ArchivedAt { get; set; }
        public string ArchivedBy { get; set; } = string.Empty;
    }

    internal void EnsureSeasonLifecycleState()
    {
        lock (_gate)
        {
            var changed = false;
            _data.SeasonDefinitions ??= [];
            _data.SeasonArchives ??= [];
            var shouldMigrateLegacyPendingGradient = IsLegacySeasonLifecycleMigrationPending();
            var currentOperations = RequireOperationsConfig();
            var active = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "active");
            if (active is null)
            {
                active = new SeasonDefinitionRow
                {
                    SeasonId = currentOperations.Season.Id,
                    Name = currentOperations.Season.Name,
                    LifecycleStatus = "active",
                    StartsAt = currentOperations.Season.StartsAt,
                    EndsAt = currentOperations.Season.EndsAt,
                    Configuration = CaptureCurrentSeasonScope(),
                    CreatedBy = currentOperations.UpdatedBy,
                    CreatedAt = currentOperations.UpdatedAt,
                    UpdatedBy = currentOperations.UpdatedBy,
                    UpdatedAt = currentOperations.UpdatedAt,
                    ActivatedAt = currentOperations.Season.StartsAt ?? currentOperations.UpdatedAt,
                };
                _data.SeasonDefinitions.Add(active);
                changed = true;
            }

            if (shouldMigrateLegacyPendingGradient
                && _data.SeasonDefinitions.All(row => row.LifecycleStatus != "draft")
                && _data.RankedPendingGradient is not null)
            {
                var configuration = CaptureCurrentSeasonScope();
                ApplyPendingGradient(configuration.Ranked, _data.RankedPendingGradient);
                _data.SeasonDefinitions.Add(new SeasonDefinitionRow
                {
                    SeasonId = string.Empty,
                    Name = "下一赛季（待完善）",
                    LifecycleStatus = "draft",
                    PreviousSeasonId = active.SeasonId,
                    Configuration = configuration,
                    CreatedBy = "系统迁移",
                    UpdatedBy = "系统迁移",
                });
                changed = true;
            }
            if (_data.SeasonLifecycleMigrationVersion < CurrentSeasonLifecycleMigrationVersion)
            {
                _data.SeasonLifecycleMigrationVersion = CurrentSeasonLifecycleMigrationVersion;
                changed = true;
            }
            if (changed) Save();
        }
    }

    public L12SeasonCatalogView SeasonCatalog(L12AccountView actor)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate)
        {
            var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
            var next = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "draft");
            return new L12SeasonCatalogView(ToSeasonDefinitionView(current),
                next is null ? null : ToSeasonDefinitionView(next),
                _data.SeasonArchives.OrderByDescending(row => row.ArchivedAt)
                    .Select(ToSeasonArchiveView).ToArray(), true,
                RequireOperationsConfig().Version);
        }
    }

    public L12SeasonDefinitionPreviewView PreviewSeasonDefinition(L12AccountView actor,
        string definitionId, L12SeasonDefinitionDraft draft, long expectedRevision,
        long expectedOperationsVersion, L12AdminAuditContext context)
        => PreviewSeasonDefinitionCore(actor, definitionId, draft, expectedRevision,
            expectedOperationsVersion, context, null);

    public L12SeasonDefinitionPreviewView PreviewNextSeasonDefinition(L12AccountView actor,
        string definitionId, L12SeasonDefinitionDraft draft, long expectedRevision,
        long expectedOperationsVersion, L12AdminAuditContext context)
        => PreviewSeasonDefinitionCore(actor, definitionId, draft, expectedRevision,
            expectedOperationsVersion, context, "next");

    private L12SeasonDefinitionPreviewView PreviewSeasonDefinitionCore(L12AccountView actor,
        string definitionId, L12SeasonDefinitionDraft draft, long expectedRevision,
        long expectedOperationsVersion, L12AdminAuditContext context, string? expectedSlot)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        lock (_gate)
        {
            var row = RequireMutableSeasonDefinition(definitionId);
            EnsureSeasonSlot(row, expectedSlot);
            var operations = RequireOperationsConfig();
            EnsureSeasonDefinitionRevision(row, expectedRevision);
            EnsureOperationsVersion(operations, expectedOperationsVersion);
            var normalized = NormalizeSeasonDefinitionCandidate(row, draft);
            var changes = DescribeSeasonDefinitionChanges(row, normalized);
            var warnings = SeasonDefinitionWarnings(row, changes);
            var previewToken = CreateSeasonPreviewToken(row, operations.Version, normalized);
            AddAdminAudit(actor, "operations", "season-definition-preview",
                $"season-definition:{row.DefinitionId}", row.Revision.ToString(),
                (row.Revision + 1).ToString(), string.Join(',', changes),
                context with
                {
                    DryRun = true,
                    ExpectedVersion = expectedOperationsVersion,
                    Outcome = "dry-run",
                });
            Save(false);
            var normalizedDraft = new L12SeasonDefinitionDraft(normalized.SeasonId, normalized.Name,
                normalized.StartsAt, normalized.EndsAt, ToSeasonScopeView(normalized.Configuration));
            return new L12SeasonDefinitionPreviewView(true, SeasonSlot(row), row.Revision,
                row.Revision + 1, operations.Version, normalizedDraft, changes, warnings, previewToken);
        }
    }

    public L12SeasonDefinitionOperationView ApplySeasonDefinition(L12AccountView actor,
        string definitionId, L12SeasonDefinitionDraft draft, long expectedRevision,
        long expectedOperationsVersion, string previewToken, string reason,
        L12AdminAuditContext context)
        => ExecuteAdminTransaction(() => ApplySeasonDefinitionCore(actor, definitionId, draft,
            expectedRevision, expectedOperationsVersion, previewToken, reason, context, null));

    public L12SeasonDefinitionView ApplyNextSeasonDefinition(L12AccountView actor,
        string definitionId, L12SeasonDefinitionDraft draft, long expectedRevision,
        long expectedOperationsVersion, string previewToken, string reason,
        L12AdminAuditContext context)
        => ExecuteAdminTransaction(() => ApplySeasonDefinitionCore(actor, definitionId, draft,
            expectedRevision, expectedOperationsVersion, previewToken, reason, context, "next").Definition);

    private L12SeasonDefinitionOperationView ApplySeasonDefinitionCore(L12AccountView actor,
        string definitionId, L12SeasonDefinitionDraft draft, long expectedRevision,
        long expectedOperationsVersion, string previewToken, string reason,
        L12AdminAuditContext context, string? expectedSlot)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        lock (_gate)
        {
            var row = _data.SeasonDefinitions.FirstOrDefault(item => item.DefinitionId == definitionId);
            if (row is null && !string.IsNullOrWhiteSpace(previewToken))
                throw new L12OperationsConfigException("season_preview_stale",
                    "赛季配置预览已失效，请重新预览后再保存");
            if (row is null)
                throw new L12OperationsConfigException("season_definition_not_found", "赛季定义不存在");
            if (row.LifecycleStatus is not ("active" or "draft"))
                throw new L12OperationsConfigException("season_definition_read_only", "历史赛季不可修改");
            EnsureSeasonSlot(row, expectedSlot);
            if (row.LifecycleStatus == "draft") EnsureSeasonActivationPlanAllowsMutation(row);
            var operations = RequireOperationsConfig();
            var normalized = NormalizeSeasonDefinitionCandidate(row, draft);
            var expectedPreviewToken = CreateSeasonPreviewToken(row, operations.Version, normalized);
            if (string.IsNullOrWhiteSpace(previewToken)
                || !string.Equals(previewToken.Trim(), expectedPreviewToken, StringComparison.Ordinal))
                throw new L12OperationsConfigException("season_preview_stale",
                    "赛季配置预览已失效，请重新预览后再保存");
            EnsureSeasonDefinitionRevision(row, expectedRevision);
            EnsureOperationsVersion(operations, expectedOperationsVersion);

            var previousRevision = row.Revision;
            var changes = DescribeSeasonDefinitionChanges(row, normalized);
            if (row.LifecycleStatus == "active")
            {
                var normalizedScope = ToSeasonScopeView(normalized.Configuration);
                var normalizedOperations = NormalizeOperationsPayload(ToPayload(operations) with
                {
                    Season = new L12SeasonConfig(row.SeasonId, normalized.Name, "active",
                        row.StartsAt, normalized.EndsAt),
                    DisasterPool = normalizedScope.DisasterPool,
                    CardRestrictions = normalizedScope.CardRestrictions,
                    DefaultPresetDeckIds = normalizedScope.DefaultPresetDeckIds,
                });
                var nextOperations = ToRow(normalizedOperations, operations.Version + 1,
                    actor.Username, operations.ImmediateMaintenance);
                _data.RankedConfig = CloneSeasonScope(normalized.Configuration).Ranked;
                _data.OperationsConfig = nextOperations;
                var history = NewOperationsHistory(nextOperations, "season-current-apply",
                    actor, normalizedReason);
                _data.OperationsConfigHistory.Add(history);
                TrimOperationsHistory();

                row.Name = normalized.Name;
                row.EndsAt = normalized.EndsAt;
                row.Configuration = CloneSeasonScope(normalized.Configuration);
                row.Revision++;
                row.UpdatedBy = actor.Username;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                row.SeasonId = normalized.SeasonId;
                row.Name = normalized.Name;
                row.StartsAt = normalized.StartsAt;
                row.EndsAt = normalized.EndsAt;
                row.Configuration = CloneSeasonScope(normalized.Configuration);
                row.Revision++;
                row.UpdatedBy = actor.Username;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                var current = _data.SeasonDefinitions.Single(item => item.LifecycleStatus == "active");
                current.NextSeasonId = row.SeasonId;
                SyncPendingGradientFromDraft(current.SeasonId, row.Configuration.Ranked);
            }

            AddAdminAudit(actor, "operations", "season-definition-apply",
                $"season-definition:{row.DefinitionId}", previousRevision.ToString(),
                row.Revision.ToString(), normalizedReason,
                context with
                {
                    ExpectedVersion = expectedOperationsVersion,
                    Reason = normalizedReason,
                    Outcome = "succeeded",
                });
            Save();
            return new L12SeasonDefinitionOperationView(true, SeasonSlot(row),
                ToSeasonDefinitionView(row), previousRevision,
                RequireOperationsConfig().Version, changes);
        }
    }

    public L12SeasonDefinitionView SeasonDefinition(L12AccountView actor, string definitionId)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate)
        {
            var row = _data.SeasonDefinitions.FirstOrDefault(item => item.DefinitionId == definitionId)
                ?? throw new L12OperationsConfigException("season_definition_not_found", "赛季定义不存在");
            return ToSeasonDefinitionView(row);
        }
    }

    public IReadOnlyList<L12SeasonArchiveView> SeasonArchives(L12AccountView actor)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate) return _data.SeasonArchives.OrderByDescending(row => row.ArchivedAt)
            .Select(ToSeasonArchiveView).ToArray();
    }

    public L12SeasonArchiveView SeasonArchive(L12AccountView actor, string seasonId)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate)
        {
            var row = _data.SeasonArchives.FirstOrDefault(item =>
                    SeasonIdsEqual(item.SeasonId, seasonId))
                ?? throw new L12OperationsConfigException("season_archive_not_found", "赛季档案不存在");
            return ToSeasonArchiveView(row);
        }
    }

    public L12SeasonDefinitionView CreateSeasonDraft(L12AccountView actor, long expectedCurrentRevision,
        string reason, L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        lock (_gate)
        {
            if (_data.SeasonDefinitions.Any(row => row.LifecycleStatus == "draft"))
                throw new L12OperationsConfigException("season_draft_exists", "下一赛季草稿已存在");
            var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
            EnsureSeasonDefinitionRevision(current, expectedCurrentRevision);
            var row = new SeasonDefinitionRow
            {
                Name = "下一赛季（待完善）",
                PreviousSeasonId = current.SeasonId,
                Configuration = CloneSeasonScope(current.Configuration),
                CreatedBy = actor.Username,
                UpdatedBy = actor.Username,
            };
            _data.SeasonDefinitions.Add(row);
            AddAdminAudit(actor, "operations", "season-draft-create",
                $"season-definition:{row.DefinitionId}", null, row.Revision.ToString(), normalizedReason,
                context with { ExpectedVersion = expectedCurrentRevision, Reason = normalizedReason,
                    Outcome = "succeeded" });
            Save();
            return ToSeasonDefinitionView(row);
        }
    }

    public L12SeasonDefinitionView UpdateSeasonDraft(L12AccountView actor, string definitionId,
        L12SeasonDefinitionDraft draft, long expectedRevision, string reason,
        L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        lock (_gate)
        {
            var row = _data.SeasonDefinitions.FirstOrDefault(item => item.DefinitionId == definitionId)
                ?? throw new L12OperationsConfigException("season_definition_not_found", "赛季定义不存在");
            if (row.LifecycleStatus != "draft")
                throw new L12OperationsConfigException("season_definition_read_only", "只有下一赛季草稿可以修改");
            EnsureSeasonActivationPlanAllowsMutation(row);
            EnsureSeasonDefinitionRevision(row, expectedRevision);
            var normalized = NormalizeSeasonDraft(draft, definitionId);
            var previousRevision = row.Revision;
            row.SeasonId = normalized.SeasonId;
            row.Name = normalized.Name;
            row.StartsAt = normalized.StartsAt;
            row.EndsAt = normalized.EndsAt;
            row.Configuration = normalized.Configuration;
            row.Revision++;
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var current = _data.SeasonDefinitions.Single(item => item.LifecycleStatus == "active");
            current.NextSeasonId = row.SeasonId;
            SyncPendingGradientFromDraft(current.SeasonId, row.Configuration.Ranked);
            AddAdminAudit(actor, "operations", "season-draft-update",
                $"season-definition:{row.DefinitionId}", previousRevision.ToString(), row.Revision.ToString(),
                normalizedReason, context with { ExpectedVersion = expectedRevision, Reason = normalizedReason,
                    Outcome = "succeeded" });
            Save();
            return ToSeasonDefinitionView(row);
        }
    }

    public bool DeleteSeasonDraft(L12AccountView actor, string definitionId, long expectedRevision,
        string reason, L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        lock (_gate)
        {
            var row = _data.SeasonDefinitions.FirstOrDefault(item => item.DefinitionId == definitionId)
                ?? throw new L12OperationsConfigException("season_definition_not_found", "赛季定义不存在");
            if (row.LifecycleStatus != "draft")
                throw new L12OperationsConfigException("season_definition_read_only", "当前赛季和历史赛季不可删除");
            EnsureSeasonActivationPlanAllowsMutation(row);
            EnsureSeasonDefinitionRevision(row, expectedRevision);
            _data.SeasonDefinitions.Remove(row);
            var current = _data.SeasonDefinitions.Single(item => item.LifecycleStatus == "active");
            current.NextSeasonId = null;
            current.Revision++;
            current.UpdatedBy = actor.Username;
            current.UpdatedAt = DateTimeOffset.UtcNow;
            _data.SeasonLifecycleMigrationVersion = Math.Max(_data.SeasonLifecycleMigrationVersion,
                CurrentSeasonLifecycleMigrationVersion);
            _data.RankedPendingGradient = null;
            AddAdminAudit(actor, "operations", "season-draft-delete",
                $"season-definition:{row.DefinitionId}", row.Revision.ToString(), null, normalizedReason,
                context with { ExpectedVersion = expectedRevision, Reason = normalizedReason,
                    Outcome = "succeeded" });
            Save();
            return true;
        }
    }

    public L12SeasonDefinitionView ArmSeasonActivation(L12AccountView actor, string definitionId,
        long expectedCurrentRevision, long expectedDraftRevision, long expectedOperationsVersion,
        string reason, DateTimeOffset now, L12AdminAuditContext context)
        => ExecuteAdminTransaction(() =>
        {
            EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
            var normalizedReason = RequireOperationsReason(reason);
            lock (_gate)
            {
                var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
                var draft = _data.SeasonDefinitions.FirstOrDefault(row => row.DefinitionId == definitionId)
                    ?? throw new L12OperationsConfigException("season_definition_not_found", "下一赛季草稿不存在");
                if (draft.LifecycleStatus != "draft")
                    throw new L12OperationsConfigException("season_definition_read_only", "只有下一赛季草稿可以预约生效");
                EnsureSeasonActivationPlanAllowsMutation(draft);
                EnsureSeasonDefinitionRevision(current, expectedCurrentRevision);
                EnsureSeasonDefinitionRevision(draft, expectedDraftRevision);
                var operations = RequireOperationsConfig();
                EnsureOperationsVersion(operations, expectedOperationsVersion);
                if (string.IsNullOrWhiteSpace(draft.SeasonId) || string.IsNullOrWhiteSpace(draft.Name))
                    throw new L12OperationsConfigException("season_draft_incomplete", "下一赛季草稿尚未填写完整");
                if (draft.StartsAt is not { } scheduledAt || scheduledAt <= now)
                    throw new L12OperationsConfigException("season_activation_time_invalid", "自动切季时间必须晚于当前权威时间");
                if (!SeasonIdsEqual(draft.PreviousSeasonId, current.SeasonId)
                    || !SeasonIdsEqual(current.NextSeasonId, draft.SeasonId))
                    throw new L12OperationsConfigException("season_link_conflict", "赛季衔接关系已变化，请刷新后重试");

                var generation = (draft.ActivationPlan?.Generation ?? 0) + 1;
                var previousRevision = draft.Revision;
                draft.Revision++;
                draft.UpdatedBy = actor.Username;
                draft.UpdatedAt = now;
                var intentKey = CreateSeasonActivationIntentKey(draft.DefinitionId, generation,
                    current.Revision, draft.Revision, operations.Version, scheduledAt);
                draft.ActivationPlan = new SeasonActivationPlanRow
                {
                    Status = "armed",
                    Generation = generation,
                    ScheduledAt = scheduledAt,
                    ArmedCurrentRevision = current.Revision,
                    ArmedDraftRevision = draft.Revision,
                    ArmedOperationsVersion = operations.Version,
                    IntentKey = intentKey,
                };
                AddAdminAudit(actor, "operations", "season-activation-arm",
                    $"season-definition:{draft.DefinitionId}", previousRevision.ToString(),
                    draft.Revision.ToString(), normalizedReason,
                    context with { ExpectedVersion = expectedOperationsVersion, Reason = normalizedReason,
                        Outcome = "succeeded" });
                Save();
                return ToSeasonDefinitionView(draft);
            }
        });

    public L12SeasonDefinitionView DisarmSeasonActivation(L12AccountView actor, string definitionId,
        long expectedDraftRevision, string reason, DateTimeOffset now, L12AdminAuditContext context)
        => ExecuteAdminTransaction(() =>
        {
            EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
            var normalizedReason = RequireOperationsReason(reason);
            lock (_gate)
            {
                var draft = _data.SeasonDefinitions.FirstOrDefault(row => row.DefinitionId == definitionId)
                    ?? throw new L12OperationsConfigException("season_definition_not_found", "下一赛季草稿不存在");
                if (draft.LifecycleStatus != "draft")
                    throw new L12OperationsConfigException("season_definition_read_only", "只有下一赛季草稿可以取消预约");
                EnsureSeasonDefinitionRevision(draft, expectedDraftRevision);
                if (draft.ActivationPlan is not { Status: not "disarmed" and not "completed" } plan)
                    throw new L12OperationsConfigException("season_activation_not_armed", "下一赛季尚未预约自动生效");
                var previousRevision = draft.Revision;
                draft.Revision++;
                draft.UpdatedBy = actor.Username;
                draft.UpdatedAt = now;
                plan.Status = "disarmed";
                plan.Generation++;
                plan.LeaseOwner = null;
                plan.LeaseExpiresAt = null;
                plan.LastErrorCode = null;
                plan.LastErrorMessage = null;
                AddAdminAudit(actor, "operations", "season-activation-disarm",
                    $"season-definition:{draft.DefinitionId}", previousRevision.ToString(),
                    draft.Revision.ToString(), normalizedReason,
                    context with { Reason = normalizedReason, Outcome = "succeeded" });
                Save();
                return ToSeasonDefinitionView(draft);
            }
        });

    internal L12SeasonActivationClaim? TryClaimDueSeasonActivation(string workerId,
        DateTimeOffset now, TimeSpan leaseDuration)
        => ExecuteAdminTransaction(() =>
        {
            lock (_gate)
            {
                var draft = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "draft");
                var plan = draft?.ActivationPlan;
                if (draft is null || plan is null || plan.ScheduledAt > now
                    || plan.Status is "disarmed" or "completed" or "failed") return null;
                if (plan.Status is not ("armed" or "waiting" or "executing"))
                {
                    FailSeasonActivationPlan(plan, now, "season_activation_state_invalid",
                        "自动切季状态无效；请取消预约并重新确认");
                    Save();
                    return null;
                }
                if (plan.Status == "executing" && plan.LeaseExpiresAt > now) return null;
                var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
                var operations = RequireOperationsConfig();
                if (plan.ArmedCurrentRevision != current.Revision
                    || plan.ArmedDraftRevision != draft.Revision
                    || plan.ArmedOperationsVersion != operations.Version
                    || !SeasonIdsEqual(draft.PreviousSeasonId, current.SeasonId)
                    || !SeasonIdsEqual(current.NextSeasonId, draft.SeasonId)
                    || draft.StartsAt != plan.ScheduledAt)
                {
                    FailSeasonActivationPlan(plan, now, "season_activation_plan_expired",
                        "预约后赛季定义、衔接关系或运行配置已变化；请取消预约并重新确认");
                    Save();
                    return null;
                }

                plan.Status = "executing";
                plan.LeaseOwner = workerId;
                plan.LeaseExpiresAt = now + leaseDuration;
                plan.AttemptCount++;
                plan.LastAttemptAt = now;
                plan.LastErrorCode = null;
                plan.LastErrorMessage = null;
                Save();
                return new L12SeasonActivationClaim(draft.DefinitionId, plan.Generation,
                    plan.IntentKey, workerId, plan.LeaseExpiresAt.Value, current.Revision,
                    draft.Revision, operations.Version);
            }
        });

    internal void RecordSeasonActivationWaiting(L12SeasonActivationClaim claim, DateTimeOffset now,
        string code, string message)
        => ExecuteAdminTransaction(() =>
        {
            lock (_gate)
            {
                var plan = RequireOwnedSeasonActivationPlan(claim, now, allowExpiredLease: true);
                plan.Status = "waiting";
                plan.LeaseOwner = null;
                plan.LeaseExpiresAt = null;
                plan.LastErrorCode = code;
                plan.LastErrorMessage = message;
                Save();
            }
            return true;
        });

    internal DateTimeOffset? NextSeasonActivationWakeAt(DateTimeOffset now, TimeSpan retryDelay)
    {
        lock (_gate)
        {
            var plan = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "draft")
                ?.ActivationPlan;
            if (plan is null || plan.Status is "disarmed" or "completed" or "failed") return null;
            if (plan.Status == "executing" && plan.LeaseExpiresAt is { } lease && lease > now) return lease;
            if (plan.Status == "waiting" && plan.LastAttemptAt is { } attempted)
                return attempted + retryDelay > now ? attempted + retryDelay : now;
            return plan.ScheduledAt > now ? plan.ScheduledAt : now;
        }
    }

    internal bool IsRankedSeasonCutoverFenced(DateTimeOffset now)
    {
        lock (_gate)
        {
            var plan = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "draft")
                ?.ActivationPlan;
            return plan is { Status: not "disarmed" and not "completed" }
                && plan.ScheduledAt <= now;
        }
    }

    public L12SeasonActivationView ActivateSeason(L12AccountView actor, string definitionId,
        long expectedCurrentRevision, long expectedDraftRevision, string reason,
        L12RankedSeasonCutoverReadiness readiness, L12AdminAuditContext context)
        => ExecuteAdminTransaction(() => ActivateSeasonCore(actor, definitionId,
            expectedCurrentRevision, expectedDraftRevision, reason, readiness, context,
            null, DateTimeOffset.UtcNow));

    internal L12SeasonActivationView ActivateClaimedSeason(L12AccountView actor,
        L12SeasonActivationClaim claim, string reason, L12RankedSeasonCutoverReadiness readiness,
        DateTimeOffset now, L12AdminAuditContext context)
        => ExecuteAdminTransaction(() => ActivateSeasonCore(actor, claim.DefinitionId,
            claim.ExpectedCurrentRevision, claim.ExpectedDraftRevision, reason, readiness, context,
            claim, now));

    private L12SeasonActivationView ActivateSeasonCore(L12AccountView actor, string definitionId,
        long expectedCurrentRevision, long expectedDraftRevision, string reason,
        L12RankedSeasonCutoverReadiness readiness, L12AdminAuditContext context,
        L12SeasonActivationClaim? claim, DateTimeOffset now)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        lock (_gate)
        {
            var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
            var draft = _data.SeasonDefinitions.FirstOrDefault(row => row.DefinitionId == definitionId)
                ?? throw new L12OperationsConfigException("season_definition_not_found", "下一赛季草稿不存在");
            if (draft.LifecycleStatus != "draft")
                throw new L12OperationsConfigException("season_definition_read_only", "只有下一赛季草稿可以生效");
            EnsureSeasonDefinitionRevision(current, expectedCurrentRevision);
            EnsureSeasonDefinitionRevision(draft, expectedDraftRevision);
            if (claim is not null)
            {
                var claimedPlan = RequireOwnedSeasonActivationPlan(claim, now, allowExpiredLease: false);
                if (claimedPlan.ArmedOperationsVersion != claim.ExpectedOperationsVersion)
                    throw new L12OperationsConfigException("season_activation_claim_stale", "自动切季执行权已失效");
                if (!HighRiskAuditAvailable())
                    throw new L12OperationsConfigException("audit_unavailable",
                        "独立审计不可用，自动切季已失败关闭");
            }
            else if (draft.ActivationPlan is { Status: "executing", LeaseExpiresAt: { } lease }
                     && lease > now)
            {
                throw new L12OperationsConfigException("season_activation_in_progress", "自动切季正在执行，请稍后刷新");
            }
            if (!SeasonIdsEqual(draft.PreviousSeasonId, current.SeasonId)
                || !SeasonIdsEqual(current.NextSeasonId, draft.SeasonId))
                throw new L12OperationsConfigException("season_link_conflict", "赛季衔接关系已变化，请刷新后重试");
            if (string.IsNullOrWhiteSpace(draft.SeasonId) || string.IsNullOrWhiteSpace(draft.Name))
                throw new L12OperationsConfigException("season_draft_incomplete", "下一赛季草稿尚未填写完整");
            if (!SeasonIdsEqual(readiness.SeasonId, current.SeasonId)
                || !readiness.Ready)
                throw new L12OperationsConfigException("season_cutover_not_ready", "当前赛季仍有未完成或未对账的排位对局");

            var operations = RequireOperationsConfig();
            if (claim is not null) EnsureOperationsVersion(operations, claim.ExpectedOperationsVersion);
            if (!SeasonIdsEqual(operations.Season.Id, current.SeasonId))
                throw new L12OperationsConfigException("season_runtime_conflict", "当前赛季定义与运行配置不一致");
            if (_data.SeasonArchives.Any(row => row.SourceDefinitionId == current.DefinitionId))
                throw new L12OperationsConfigException("season_archive_conflict", "当前赛季已经归档，不能再次切换");

            var incomingScope = ToSeasonScopeView(draft.Configuration);
            var incomingRanked = CloneSeasonScope(draft.Configuration).Ranked;
            var previousOperations = ToPayload(operations);
            // Finish validation and construction before the first mutable season write. The nested
            // transaction savepoint remains the authority for failures from later reconciliation/storage.
            var nextPayload = NormalizeOperationsPayload(previousOperations with
            {
                Season = new L12SeasonConfig(draft.SeasonId, draft.Name, "active",
                    draft.StartsAt, draft.EndsAt),
                DisasterPool = incomingScope.DisasterPool,
                CardRestrictions = incomingScope.CardRestrictions,
                DefaultPresetDeckIds = draft.Configuration.DefaultPresetDeckIds.ToArray(),
            });
            var nextOperations = ToRow(nextPayload, operations.Version + 1, actor.Username,
                operations.ImmediateMaintenance);

            FinalizeOutgoingRankedSeason(current.SeasonId, current.Name, draft.SeasonId, now);
            SeasonActivationFailureInjector?.Invoke("after-season-finalization");
            var archive = new SeasonArchiveRow
            {
                SourceDefinitionId = current.DefinitionId,
                SeasonId = current.SeasonId,
                Name = current.Name,
                DefinitionRevision = current.Revision,
                PreviousSeasonId = current.PreviousSeasonId,
                NextSeasonId = draft.SeasonId,
                StartsAt = current.StartsAt,
                EndsAt = current.EndsAt,
                Configuration = CloneSeasonScope(current.Configuration),
                ActivatedAt = current.ActivatedAt,
                ArchivedAt = now,
                ArchivedBy = actor.Username,
            };
            _data.SeasonArchives.Add(archive);
            SeasonActivationFailureInjector?.Invoke("after-season-archive");

            _data.RankedConfig = incomingRanked;
            CarryRankedProfilesIntoSeason(current.SeasonId, draft.SeasonId,
                _data.RankedConfig.PlacementMatches);
            SeasonActivationFailureInjector?.Invoke("after-season-profile-carry");
            _data.RankedPendingGradient = null;
            _data.RankedGradientVersion = Math.Max(1, _data.RankedGradientVersion + 1);

            _data.OperationsConfig = nextOperations;
            var history = NewOperationsHistory(nextOperations, $"season-activate:{draft.SeasonId}",
                actor, normalizedReason);
            _data.OperationsConfigHistory.Add(history);
            TrimOperationsHistory();

            _data.SeasonDefinitions.Remove(current);
            draft.LifecycleStatus = "active";
            draft.PreviousSeasonId = current.SeasonId;
            draft.NextSeasonId = null;
            draft.Revision++;
            draft.UpdatedBy = actor.Username;
            draft.UpdatedAt = now;
            draft.ActivatedAt = now;
            if (draft.ActivationPlan is { } activationPlan)
            {
                activationPlan.Status = "completed";
                activationPlan.LeaseOwner = null;
                activationPlan.LeaseExpiresAt = null;
                activationPlan.LastErrorCode = null;
                activationPlan.LastErrorMessage = null;
                activationPlan.CompletedAt = now;
            }
            SeasonActivationFailureInjector?.Invoke("after-season-runtime-swap");

            AddAdminAudit(actor, "operations", "season-activate",
                $"season-definition:{draft.DefinitionId}", current.SeasonId, draft.SeasonId,
                normalizedReason, context with { ExpectedVersion = operations.Version,
                    Reason = normalizedReason, Outcome = "succeeded" });
            SeasonActivationFailureInjector?.Invoke("after-season-audit");
            Save();
            return new L12SeasonActivationView(true, ToSeasonDefinitionView(draft),
                ToSeasonArchiveView(archive), readiness, nextOperations.Version);
        }
    }

    private void SyncActiveSeasonDefinitionFromRuntime(L12AccountView actor)
    {
        var active = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "active");
        var operations = RequireOperationsConfig();
        if (active is null || !SeasonIdsEqual(active.SeasonId, operations.Season.Id)) return;
        active.Name = operations.Season.Name;
        active.StartsAt = operations.Season.StartsAt;
        active.EndsAt = operations.Season.EndsAt;
        active.Configuration = CaptureCurrentSeasonScope();
        active.Revision++;
        active.UpdatedBy = actor.Username;
        active.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private SeasonDefinitionRow NormalizeSeasonDraft(L12SeasonDefinitionDraft draft, string definitionId)
    {
        if (draft is null || draft.Configuration is null || draft.Configuration.Ranked is null)
            throw new L12OperationsConfigException("invalid_season_definition", "下一赛季草稿字段不完整");
        var seasonId = RequireSeasonId(draft.SeasonId);
        var name = RequireOperationsText(draft.Name, "赛季名称", 100);
        if (_data.SeasonDefinitions.Any(row => row.DefinitionId != definitionId
                && SeasonIdsEqual(row.SeasonId, seasonId))
            || _data.SeasonArchives.Any(row => SeasonIdsEqual(row.SeasonId, seasonId)))
            throw new L12OperationsConfigException("duplicate_season_id", "赛季 ID 已存在");

        var currentPayload = ToPayload(RequireOperationsConfig());
        var normalizedOperations = NormalizeOperationsPayload(currentPayload with
        {
            Season = new L12SeasonConfig(seasonId, name, "upcoming", draft.StartsAt, draft.EndsAt),
            DisasterPool = draft.Configuration.DisasterPool,
            CardRestrictions = draft.Configuration.CardRestrictions,
            DefaultPresetDeckIds = draft.Configuration.DefaultPresetDeckIds,
        });
        var normalizedRanked = NormalizeRankedConfig(draft.Configuration.Ranked with { PendingGradient = null });
        return new SeasonDefinitionRow
        {
            DefinitionId = definitionId,
            SeasonId = normalizedOperations.Season.Id,
            Name = normalizedOperations.Season.Name,
            StartsAt = normalizedOperations.Season.StartsAt,
            EndsAt = normalizedOperations.Season.EndsAt,
            Configuration = new SeasonScopedConfigRow
            {
                DisasterPool = new OperationsDisasterPoolRow
                {
                    CardIds = normalizedOperations.DisasterPool.CardIds.ToList(),
                    AnnihilationLocked = true,
                },
                CardRestrictions = normalizedOperations.CardRestrictions.Select(item =>
                    new OperationsCardRestrictionRow
                    {
                        CardId = item.CardId,
                        MaxCopies = item.MaxCopies,
                        Reason = item.Reason,
                        MasterId = item.MasterId,
                    }).ToList(),
                DefaultPresetDeckIds = normalizedOperations.DefaultPresetDeckIds.ToList(),
                Ranked = normalizedRanked,
            },
        };
    }

    private SeasonDefinitionRow RequireMutableSeasonDefinition(string definitionId)
    {
        var row = _data.SeasonDefinitions.FirstOrDefault(item => item.DefinitionId == definitionId)
            ?? throw new L12OperationsConfigException("season_definition_not_found", "赛季定义不存在");
        if (row.LifecycleStatus is not ("active" or "draft"))
            throw new L12OperationsConfigException("season_definition_read_only", "历史赛季不可修改");
        return row;
    }

    private SeasonDefinitionRow NormalizeSeasonDefinitionCandidate(SeasonDefinitionRow row,
        L12SeasonDefinitionDraft draft)
    {
        var normalized = NormalizeSeasonDraft(draft, row.DefinitionId);
        if (row.LifecycleStatus != "active") return normalized;
        if (!SeasonIdsEqual(row.SeasonId, normalized.SeasonId))
            throw new L12OperationsConfigException("season_identity_read_only",
                "当前赛季 ID 不可通过配置修改；切换赛季必须使用赛季生效命令");
        if (row.StartsAt != normalized.StartsAt)
            throw new L12OperationsConfigException("season_start_read_only", "当前赛季实际开始时间不可修改");
        normalized.SeasonId = row.SeasonId;
        normalized.StartsAt = row.StartsAt;
        return normalized;
    }

    private static string SeasonSlot(SeasonDefinitionRow row)
        => row.LifecycleStatus == "active" ? "current" : "next";

    private static void EnsureSeasonSlot(SeasonDefinitionRow row, string? expectedSlot)
    {
        if (expectedSlot is null || string.Equals(SeasonSlot(row), expectedSlot, StringComparison.Ordinal)) return;
        throw new L12OperationsConfigException("season_definition_read_only", "旧版草稿接口只能修改下一赛季草稿");
    }

    private IReadOnlyList<string> DescribeSeasonDefinitionChanges(SeasonDefinitionRow current,
        SeasonDefinitionRow next)
    {
        var changes = new List<string>();
        if (!SeasonIdsEqual(current.SeasonId, next.SeasonId)) changes.Add("seasonId");
        if (!string.Equals(current.Name, next.Name, StringComparison.Ordinal)) changes.Add("name");
        if (current.StartsAt != next.StartsAt) changes.Add("startsAt");
        if (current.EndsAt != next.EndsAt) changes.Add("endsAt");
        var currentScope = ToSeasonScopeView(current.Configuration);
        var nextScope = ToSeasonScopeView(next.Configuration);
        if (!JsonEqual(currentScope.DisasterPool, nextScope.DisasterPool)) changes.Add("disasterPool");
        if (!JsonEqual(currentScope.CardRestrictions, nextScope.CardRestrictions)) changes.Add("cardRestrictions");
        if (!JsonEqual(currentScope.DefaultPresetDeckIds, nextScope.DefaultPresetDeckIds))
            changes.Add("defaultPresetDeckIds");
        if (!JsonEqual(currentScope.Ranked, nextScope.Ranked)) changes.Add("ranked");
        return changes;
    }

    private static IReadOnlyList<string> SeasonDefinitionWarnings(SeasonDefinitionRow row,
        IReadOnlyList<string> changes)
    {
        var warnings = new List<string>();
        if (row.LifecycleStatus == "active")
        {
            warnings.Add("current-season-changes-apply-immediately");
            if (changes.Contains("ranked", StringComparer.Ordinal))
                warnings.Add("current-ranked-config-affects-subsequent-settlements");
        }
        else
        {
            warnings.Add("next-season-draft-only");
        }
        return warnings;
    }

    private string CreateSeasonPreviewToken(SeasonDefinitionRow row, long operationsVersion,
        SeasonDefinitionRow normalized)
    {
        var current = _data.SeasonDefinitions.Single(item => item.LifecycleStatus == "active");
        var next = _data.SeasonDefinitions.SingleOrDefault(item => item.LifecycleStatus == "draft");
        var binding = new
        {
            row.DefinitionId,
            Slot = SeasonSlot(row),
            row.Revision,
            row.PreviousSeasonId,
            row.NextSeasonId,
            Current = new { current.DefinitionId, current.Revision },
            Next = next is null ? null : new { next.DefinitionId, next.Revision },
            OperationsVersion = operationsVersion,
            Draft = new L12SeasonDefinitionDraft(normalized.SeasonId, normalized.Name,
                normalized.StartsAt, normalized.EndsAt, ToSeasonScopeView(normalized.Configuration)),
        };
        var bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(binding));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private SeasonScopedConfigRow CaptureCurrentSeasonScope()
    {
        var operations = RequireOperationsConfig();
        return new SeasonScopedConfigRow
        {
            DisasterPool = new OperationsDisasterPoolRow
            {
                CardIds = operations.DisasterPool.CardIds.ToList(),
                AnnihilationLocked = true,
            },
            CardRestrictions = operations.CardRestrictions.Select(item => new OperationsCardRestrictionRow
            {
                CardId = item.CardId,
                MaxCopies = item.MaxCopies,
                Reason = item.Reason,
                MasterId = item.MasterId,
            }).ToList(),
            DefaultPresetDeckIds = operations.DefaultPresetDeckIds.ToList(),
            Ranked = NormalizeRankedConfig(ToView(_data.RankedConfig!) with { PendingGradient = null }),
        };
    }

    private SeasonScopedConfigRow CloneSeasonScope(SeasonScopedConfigRow source)
        => new()
        {
            DisasterPool = new OperationsDisasterPoolRow
            {
                CardIds = source.DisasterPool.CardIds.ToList(),
                AnnihilationLocked = true,
            },
            CardRestrictions = source.CardRestrictions.Select(item => new OperationsCardRestrictionRow
            {
                CardId = item.CardId,
                MaxCopies = item.MaxCopies,
                Reason = item.Reason,
                MasterId = item.MasterId,
            }).ToList(),
            DefaultPresetDeckIds = source.DefaultPresetDeckIds.ToList(),
            Ranked = NormalizeRankedConfig(ToView(source.Ranked) with { PendingGradient = null }),
        };

    private static void ApplyPendingGradient(RankedConfigRow ranked, RankedPendingGradientRow pending)
    {
        if (pending.Tiers.Count != 5 || ranked.Factions.Any(faction => faction.Tiers.Count != 5)) return;
        foreach (var faction in ranked.Factions)
            for (var index = 0; index < pending.Tiers.Count; index++)
            {
                var source = pending.Tiers[index];
                var target = faction.Tiers[index];
                target.Name = source.Name;
                target.Minimum = source.Minimum;
                target.BaseDelta = source.BaseDelta;
                target.WinStreakCap = source.WinStreakCap;
                target.LossProtectionCap = source.LossProtectionCap;
                target.RatingGapCap = source.RatingGapCap;
                target.StreakTerminationReward = source.StreakTerminationReward;
            }
    }

    private void SyncPendingGradientFromDraft(string currentSeasonId, RankedConfigRow ranked)
    {
        var version = _data.RankedPendingGradient?.Version ?? Math.Max(2, _data.RankedGradientVersion + 1);
        _data.RankedPendingGradient = new RankedPendingGradientRow
        {
            AfterSeasonId = currentSeasonId,
            Version = version,
            Tiers = ranked.Factions[0].Tiers.Select(tier => new RankedTierGradientRow
            {
                Name = tier.Name,
                Minimum = tier.Minimum,
                BaseDelta = tier.BaseDelta,
                WinStreakCap = tier.WinStreakCap,
                LossProtectionCap = tier.LossProtectionCap,
                RatingGapCap = tier.RatingGapCap,
                StreakTerminationReward = tier.StreakTerminationReward,
            }).ToList(),
        };
    }

    private static void EnsureSeasonDefinitionRevision(SeasonDefinitionRow row, long expectedRevision)
    {
        if (row.Revision != expectedRevision)
            throw new L12OperationsConfigException("season_definition_revision_conflict",
                "赛季草稿版本已变化，请刷新后重试");
    }

    private static void EnsureSeasonActivationPlanAllowsMutation(SeasonDefinitionRow row)
    {
        if (row.ActivationPlan is { Status: not "disarmed" and not "completed" })
            throw new L12OperationsConfigException("season_activation_armed",
                "下一赛季已预约自动生效；请先取消预约再修改或删除");
    }

    private SeasonActivationPlanRow RequireOwnedSeasonActivationPlan(
        L12SeasonActivationClaim claim, DateTimeOffset now, bool allowExpiredLease)
    {
        var draft = _data.SeasonDefinitions.FirstOrDefault(row =>
            row.LifecycleStatus == "draft" && row.DefinitionId == claim.DefinitionId)
            ?? throw new L12OperationsConfigException("season_activation_claim_stale", "自动切季草稿已变化");
        var plan = draft.ActivationPlan;
        if (plan is null || plan.Status != "executing" || plan.Generation != claim.Generation
            || !string.Equals(plan.IntentKey, claim.IntentKey, StringComparison.Ordinal)
            || !string.Equals(plan.LeaseOwner, claim.LeaseOwner, StringComparison.Ordinal)
            || plan.LeaseExpiresAt != claim.LeaseExpiresAt
            || (!allowExpiredLease && plan.LeaseExpiresAt <= now))
            throw new L12OperationsConfigException("season_activation_claim_stale", "自动切季执行权已失效");
        return plan;
    }

    private static void FailSeasonActivationPlan(SeasonActivationPlanRow plan, DateTimeOffset now,
        string code, string message)
    {
        plan.Status = "failed";
        plan.LeaseOwner = null;
        plan.LeaseExpiresAt = null;
        plan.LastAttemptAt = now;
        plan.LastErrorCode = code;
        plan.LastErrorMessage = message;
    }

    private static string CreateSeasonActivationIntentKey(string definitionId, long generation,
        long currentRevision, long draftRevision, long operationsVersion, DateTimeOffset scheduledAt)
    {
        var value = string.Join('|', definitionId, generation, currentRevision, draftRevision,
            operationsVersion, scheduledAt.ToUniversalTime().ToString("O"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private bool IsLegacySeasonLifecycleMigrationPending()
        => _data.SeasonLifecycleMigrationVersion < CurrentSeasonLifecycleMigrationVersion
            && (_data.SeasonDefinitions?.Count ?? 0) == 0
            && (_data.SeasonArchives?.Count ?? 0) == 0;

    private L12SeasonScopedConfig ToSeasonScopeView(SeasonScopedConfigRow row)
        => new(new L12SeasonDisasterPoolConfig(row.DisasterPool.CardIds.ToArray(), true),
            row.CardRestrictions.Select(item => new L12CardRestrictionConfig(item.CardId,
                item.MaxCopies, item.Reason, item.MasterId)).ToArray(),
            row.DefaultPresetDeckIds.ToArray(),
            ToView(row.Ranked) with { PendingGradient = null });

    private L12SeasonDefinitionView ToSeasonDefinitionView(SeasonDefinitionRow row)
        => new(row.DefinitionId, row.SeasonId, row.Name, row.LifecycleStatus, row.StartsAt, row.EndsAt,
            row.Revision, row.PreviousSeasonId, row.NextSeasonId, ToSeasonScopeView(row.Configuration),
            row.CreatedBy, row.CreatedAt, row.UpdatedBy, row.UpdatedAt, row.ActivatedAt,
            row.ActivationPlan is null ? null : new L12SeasonActivationPlanView(
                row.ActivationPlan.Status, row.ActivationPlan.Generation,
                row.ActivationPlan.ScheduledAt, row.ActivationPlan.ArmedCurrentRevision,
                row.ActivationPlan.ArmedDraftRevision, row.ActivationPlan.ArmedOperationsVersion,
                row.ActivationPlan.IntentKey, row.ActivationPlan.LeaseOwner,
                row.ActivationPlan.LeaseExpiresAt, row.ActivationPlan.AttemptCount,
                row.ActivationPlan.LastAttemptAt, row.ActivationPlan.LastErrorCode,
                row.ActivationPlan.LastErrorMessage, row.ActivationPlan.CompletedAt));

    private L12SeasonArchiveView ToSeasonArchiveView(SeasonArchiveRow row)
        => new(row.ArchiveId, row.SourceDefinitionId, row.SeasonId, row.Name,
            row.DefinitionRevision, row.PreviousSeasonId, row.NextSeasonId, row.StartsAt, row.EndsAt,
            ToSeasonScopeView(row.Configuration), row.ActivatedAt, row.ArchivedAt, row.ArchivedBy);
}
