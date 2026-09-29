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
    DateTimeOffset? ActivatedAt = null);

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
    bool AutomaticActivationEnabled);

public sealed partial class L12PlatformStore
{
    private const int CurrentSeasonLifecycleMigrationVersion = 1;

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
                    .Select(ToSeasonArchiveView).ToArray(), false);
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
                    item.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase))
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
            EnsureSeasonDefinitionRevision(row, expectedRevision);
            _data.SeasonDefinitions.Remove(row);
            _data.SeasonDefinitions.Single(item => item.LifecycleStatus == "active").NextSeasonId = null;
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

    private SeasonDefinitionRow NormalizeSeasonDraft(L12SeasonDefinitionDraft draft, string definitionId)
    {
        if (draft is null || draft.Configuration is null || draft.Configuration.Ranked is null)
            throw new L12OperationsConfigException("invalid_season_definition", "下一赛季草稿字段不完整");
        var seasonId = RequireOperationsId(draft.SeasonId, "赛季 ID");
        var name = RequireOperationsText(draft.Name, "赛季名称", 100);
        if (_data.SeasonDefinitions.Any(row => row.DefinitionId != definitionId
                && row.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase))
            || _data.SeasonArchives.Any(row => row.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase)))
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
            row.CreatedBy, row.CreatedAt, row.UpdatedBy, row.UpdatedAt, row.ActivatedAt);

    private L12SeasonArchiveView ToSeasonArchiveView(SeasonArchiveRow row)
        => new(row.ArchiveId, row.SourceDefinitionId, row.SeasonId, row.Name,
            row.DefinitionRevision, row.PreviousSeasonId, row.NextSeasonId, row.StartsAt, row.EndsAt,
            ToSeasonScopeView(row.Configuration), row.ActivatedAt, row.ArchivedAt, row.ArchivedBy);
}
