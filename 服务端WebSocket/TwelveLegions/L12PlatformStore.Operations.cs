using System.Text.Json;
using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

public sealed record L12SeasonConfig(
    string Id,
    string Name,
    string Status,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public sealed record L12SeasonDisasterPoolConfig(
    IReadOnlyList<string> CardIds,
    bool AnnihilationLocked = true);

public sealed record L12CardRestrictionConfig(
    string CardId,
    int MaxCopies,
    string? Reason = null);

public sealed record L12MatchModeConfig(string Id, string Name, bool Enabled);

public sealed record L12MaintenanceConfig(
    bool Enabled,
    string Message,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public sealed record L12OperationsConfigPayload(
    L12SeasonConfig Season,
    L12SeasonDisasterPoolConfig DisasterPool,
    IReadOnlyList<L12CardRestrictionConfig> CardRestrictions,
    IReadOnlyList<string> DefaultPresetDeckIds,
    IReadOnlyList<L12MatchModeConfig> MatchModes,
    IReadOnlyDictionary<string, bool> FeatureFlags,
    L12MaintenanceConfig Maintenance);

public sealed record L12OperationsConfigView(
    long Version,
    string VersionId,
    L12OperationsConfigPayload Config,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record L12OperationsConfigVersionView(
    string Id,
    long Version,
    string Action,
    L12OperationsConfigPayload Config,
    string ActorId,
    string ActorName,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed record L12OperationsConfigPreviewView(
    bool Valid,
    long CurrentVersion,
    long NextVersion,
    L12OperationsConfigPayload Normalized,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Warnings);

public sealed record L12OperationsConfigOperationView(
    bool Applied,
    L12OperationsConfigView Current,
    L12OperationsConfigVersionView HistoryEntry,
    IReadOnlyList<string> Changes);

public sealed class L12OperationsConfigException : InvalidOperationException
{
    public string Code { get; }

    public L12OperationsConfigException(string code, string message) : base(message) => Code = code;
}

public sealed partial class L12PlatformStore
{
    internal const string AnnihilationCardId = "S01-DS10";
    private const int OperationsHistoryLimit = 200;
    private static readonly Regex OperationsIdPattern = new("^[a-zA-Z0-9_.-]{1,64}$", RegexOptions.Compiled);

    private sealed class OperationsSeasonRow
    {
        public string Id { get; set; } = "S01";
        public string Name { get; set; } = "S01";
        public string Status { get; set; } = "active";
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
    }

    private sealed class OperationsDisasterPoolRow
    {
        public List<string> CardIds { get; set; } = [];
        public bool AnnihilationLocked { get; set; } = true;
    }

    private sealed class OperationsCardRestrictionRow
    {
        public string CardId { get; set; } = string.Empty;
        public int MaxCopies { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class OperationsMatchModeRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    private sealed class OperationsMaintenanceRow
    {
        public bool Enabled { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
    }

    private sealed class OperationsConfigRow
    {
        public long Version { get; set; } = 1;
        public string VersionId { get; set; } = string.Empty;
        public OperationsSeasonRow Season { get; set; } = new();
        public OperationsDisasterPoolRow DisasterPool { get; set; } = new();
        public List<OperationsCardRestrictionRow> CardRestrictions { get; set; } = [];
        public List<string> DefaultPresetDeckIds { get; set; } = [];
        public List<OperationsMatchModeRow> MatchModes { get; set; } = [];
        public Dictionary<string, bool> FeatureFlags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public OperationsMaintenanceRow Maintenance { get; set; } = new();
        public string UpdatedBy { get; set; } = "系统";
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class OperationsConfigVersionRow
    {
        public string Id { get; set; } = string.Empty;
        public long Version { get; set; }
        public string Action { get; set; } = "initialize";
        public OperationsConfigRow Config { get; set; } = new();
        public string ActorId { get; set; } = "system";
        public string ActorName { get; set; } = "系统";
        public string Reason { get; set; } = "initial configuration";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public L12OperationsConfigView OperationsConfig(L12AccountView actor)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate) return ToView(RequireOperationsConfig());
    }

    public IReadOnlyList<L12OperationsConfigVersionView> OperationsConfigHistory(L12AccountView actor,
        int limit = 50)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate) return _data.OperationsConfigHistory
            .OrderByDescending(row => row.Version)
            .ThenByDescending(row => row.CreatedAt)
            .Take(Math.Clamp(limit, 1, OperationsHistoryLimit))
            .Select(ToView).ToArray();
    }

    public L12OperationsConfigPreviewView PreviewOperationsConfig(L12AccountView actor,
        L12OperationsConfigPayload payload, long? expectedVersion, L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        lock (_gate)
        {
            var current = RequireOperationsConfig();
            EnsureOperationsVersion(current, expectedVersion);
            var normalized = NormalizeOperationsPayload(payload);
            var changes = DescribeOperationsChanges(ToPayload(current), normalized);
            var warnings = OperationsWarnings(normalized);
            AddAdminAudit(actor, "operations", "config-preview", "operations:config",
                current.Version.ToString(), (current.Version + 1).ToString(), string.Join(",", changes),
                context with { DryRun = true, Outcome = "dry-run" });
            Save(false);
            return new L12OperationsConfigPreviewView(true, current.Version, current.Version + 1,
                normalized, changes, warnings);
        }
    }

    public L12OperationsConfigOperationView ApplyOperationsConfig(L12AccountView actor,
        L12OperationsConfigPayload payload, long expectedVersion, string? reason,
        L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        lock (_gate)
        {
            var current = RequireOperationsConfig();
            EnsureOperationsVersion(current, expectedVersion);
            var normalized = NormalizeOperationsPayload(payload);
            var changes = DescribeOperationsChanges(ToPayload(current), normalized);
            var next = ToRow(normalized, current.Version + 1, actor.Username);
            _data.OperationsConfig = next;
            var history = NewOperationsHistory(next, "apply", actor, normalizedReason);
            _data.OperationsConfigHistory.Add(history);
            TrimOperationsHistory();
            AddAdminAudit(actor, "operations", "config-apply", "operations:config",
                current.Version.ToString(), next.Version.ToString(), normalizedReason,
                context with { Outcome = "succeeded", Reason = normalizedReason });
            Save();
            return new L12OperationsConfigOperationView(true, ToView(next), ToView(history), changes);
        }
    }

    public L12OperationsConfigOperationView RollbackOperationsConfig(L12AccountView actor,
        string versionId, long expectedVersion, string? reason, L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = RequireOperationsReason(reason);
        var normalizedVersionId = versionId?.Trim() ?? string.Empty;
        lock (_gate)
        {
            var current = RequireOperationsConfig();
            EnsureOperationsVersion(current, expectedVersion);
            var target = _data.OperationsConfigHistory.FirstOrDefault(row => row.Id == normalizedVersionId)
                ?? throw new L12OperationsConfigException("operations_version_not_found", "运营配置历史版本不存在");
            var targetPayload = ToPayload(target.Config);
            var changes = DescribeOperationsChanges(ToPayload(current), targetPayload);
            var next = ToRow(targetPayload, current.Version + 1, actor.Username);
            _data.OperationsConfig = next;
            var history = NewOperationsHistory(next, $"rollback:{target.Id}", actor, normalizedReason);
            _data.OperationsConfigHistory.Add(history);
            TrimOperationsHistory();
            AddAdminAudit(actor, "operations", "config-rollback", "operations:config",
                current.Version.ToString(), next.Version.ToString(), $"{normalizedReason}; target={target.Id}",
                context with { Outcome = "succeeded", Reason = normalizedReason });
            Save();
            return new L12OperationsConfigOperationView(true, ToView(next), ToView(history), changes);
        }
    }

    internal long OperationsConfigVersion()
    {
        lock (_gate) return RequireOperationsConfig().Version;
    }

    private void EnsureOperationsState()
    {
        lock (_gate)
        {
            var changed = false;
            if (_data.OperationsConfig is null)
            {
                _data.OperationsConfig = ToRow(DefaultOperationsPayload(), 1, "系统");
                changed = true;
            }
            else
            {
                NormalizeOperationsRow(_data.OperationsConfig);
            }
            _data.OperationsConfigHistory ??= [];
            if (_data.OperationsConfigHistory.Count == 0)
            {
                _data.OperationsConfigHistory.Add(new OperationsConfigVersionRow
                {
                    Id = _data.OperationsConfig.VersionId,
                    Version = _data.OperationsConfig.Version,
                    Action = "initialize",
                    Config = CloneOperationsRow(_data.OperationsConfig),
                    CreatedAt = _data.OperationsConfig.UpdatedAt,
                });
                changed = true;
            }
            foreach (var history in _data.OperationsConfigHistory)
            {
                history.Config ??= CloneOperationsRow(_data.OperationsConfig);
                NormalizeOperationsRow(history.Config);
                if (string.IsNullOrWhiteSpace(history.Id)) history.Id = history.Config.VersionId;
                if (history.Version < 1) history.Version = history.Config.Version;
            }
            if (changed) Save();
        }
    }

    private L12OperationsConfigPayload DefaultOperationsPayload()
        => new(
            new L12SeasonConfig("S01", "S01", "active", null, null),
            new L12SeasonDisasterPoolConfig(Enumerable.Range(1, 10)
                .Select(number => $"S01-DS{number:00}").ToArray(), true),
            [],
            _officialDecks.Select(deck => deck.MasterId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            [new L12MatchModeConfig("casual", "休闲对战", true),
                new L12MatchModeConfig("ranked", "排位对战", true)],
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["publicDecks"] = true,
                ["tournaments"] = true,
            },
            new L12MaintenanceConfig(false, string.Empty, null, null));

    private L12OperationsConfigPayload NormalizeOperationsPayload(L12OperationsConfigPayload payload)
    {
        if (payload is null || payload.Season is null || payload.DisasterPool is null
            || payload.CardRestrictions is null || payload.DefaultPresetDeckIds is null
            || payload.MatchModes is null || payload.FeatureFlags is null || payload.Maintenance is null)
            throw new L12OperationsConfigException("invalid_operations_config", "运营配置字段不完整");

        var seasonId = RequireOperationsId(payload.Season.Id, "赛季 ID");
        var seasonName = RequireOperationsText(payload.Season.Name, "赛季名称", 100);
        var seasonStatus = payload.Season.Status?.Trim().ToLowerInvariant();
        if (seasonStatus is not ("upcoming" or "active" or "archived"))
            throw new L12OperationsConfigException("invalid_season_status", "赛季状态必须为 upcoming、active 或 archived");
        EnsureTimeRange(payload.Season.StartsAt, payload.Season.EndsAt, "赛季");

        if (!payload.DisasterPool.AnnihilationLocked)
            throw new L12OperationsConfigException("annihilation_locked", "最终天灾〈堙灭〉必须保持锁定");
        var disasterIds = payload.DisasterPool.CardIds.Select(id => RequireCardId(id, "天灾卡号"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (disasterIds.Length is < 1 or > 64 || disasterIds.Length != payload.DisasterPool.CardIds.Count)
            throw new L12OperationsConfigException("invalid_disaster_pool", "天灾池必须包含 1–64 张不重复卡牌");
        if (!string.Equals(disasterIds[^1], AnnihilationCardId, StringComparison.OrdinalIgnoreCase)
            || disasterIds.Count(id => string.Equals(id, AnnihilationCardId,
                StringComparison.OrdinalIgnoreCase)) != 1)
            throw new L12OperationsConfigException("annihilation_locked", "最终天灾〈堙灭〉必须唯一且固定在天灾池末尾");

        var restrictions = payload.CardRestrictions.Select(item =>
        {
            if (item is null)
                throw new L12OperationsConfigException("invalid_card_restriction", "禁限卡条目不能为空");
            if (item.MaxCopies is < 0 or > 3)
                throw new L12OperationsConfigException("invalid_card_restriction", "禁限卡张数必须为 0–3");
            var reason = OptionalOperationsText(item.Reason, 500);
            return new L12CardRestrictionConfig(RequireCardId(item.CardId, "禁限卡卡号"),
                item.MaxCopies, string.IsNullOrEmpty(reason) ? null : reason);
        }).ToArray();
        if (restrictions.GroupBy(item => item.CardId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new L12OperationsConfigException("duplicate_card_restriction", "禁限卡列表包含重复卡号");

        var defaultDecks = payload.DefaultPresetDeckIds.Select(id => RequireOperationsId(id, "默认预组 ID"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (defaultDecks.Length != payload.DefaultPresetDeckIds.Count || defaultDecks.Length > 64)
            throw new L12OperationsConfigException("invalid_default_presets", "默认预组不能重复且最多 64 个");
        if (_officialDecks.Count > 0)
        {
            var known = _officialDecks.Select(deck => deck.MasterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = defaultDecks.FirstOrDefault(id => !known.Contains(id));
            if (unknown is not null)
                throw new L12OperationsConfigException("unknown_default_preset", $"默认预组不存在：{unknown}");
        }

        var modes = payload.MatchModes.Select(item =>
        {
            if (item is null)
                throw new L12OperationsConfigException("invalid_match_mode", "对战模式条目不能为空");
            return new L12MatchModeConfig(RequireOperationsId(item.Id, "对战模式 ID"),
                RequireOperationsText(item.Name, "对战模式名称", 100), item.Enabled);
        }).ToArray();
        if (modes.Length is < 1 or > 32 || modes.GroupBy(item => item.Id,
                StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new L12OperationsConfigException("invalid_match_modes", "对战模式必须包含 1–32 个不重复条目");
        if (modes.All(item => !item.Enabled))
            throw new L12OperationsConfigException("all_match_modes_disabled", "至少需要启用一种对战模式");

        if (payload.FeatureFlags.Count > 128)
            throw new L12OperationsConfigException("too_many_feature_flags", "功能开关不能超过 128 个");
        if (payload.FeatureFlags.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            throw new L12OperationsConfigException("duplicate_feature_flag", "功能开关包含重复键");
        var flags = payload.FeatureFlags.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => RequireOperationsId(item.Key, "功能开关键"), item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        var maintenanceMessage = OptionalOperationsText(payload.Maintenance.Message, 1000);
        if (payload.Maintenance.Enabled && string.IsNullOrWhiteSpace(maintenanceMessage))
            throw new L12OperationsConfigException("maintenance_message_required", "启用维护时必须填写维护提示");
        EnsureTimeRange(payload.Maintenance.StartsAt, payload.Maintenance.EndsAt, "维护窗口");

        return new L12OperationsConfigPayload(
            new L12SeasonConfig(seasonId, seasonName, seasonStatus,
                payload.Season.StartsAt, payload.Season.EndsAt),
            new L12SeasonDisasterPoolConfig(disasterIds, true),
            restrictions.OrderBy(item => item.CardId, StringComparer.OrdinalIgnoreCase).ToArray(),
            defaultDecks,
            modes.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            flags,
            new L12MaintenanceConfig(payload.Maintenance.Enabled, maintenanceMessage,
                payload.Maintenance.StartsAt, payload.Maintenance.EndsAt));
    }

    private static IReadOnlyList<string> DescribeOperationsChanges(L12OperationsConfigPayload current,
        L12OperationsConfigPayload next)
    {
        var changes = new List<string>();
        if (!JsonEqual(current.Season, next.Season)) changes.Add("season");
        if (!JsonEqual(current.DisasterPool, next.DisasterPool)) changes.Add("disasterPool");
        if (!JsonEqual(current.CardRestrictions, next.CardRestrictions)) changes.Add("cardRestrictions");
        if (!JsonEqual(current.DefaultPresetDeckIds, next.DefaultPresetDeckIds)) changes.Add("defaultPresetDeckIds");
        if (!JsonEqual(current.MatchModes, next.MatchModes)) changes.Add("matchModes");
        if (!JsonEqual(current.FeatureFlags, next.FeatureFlags)) changes.Add("featureFlags");
        if (!JsonEqual(current.Maintenance, next.Maintenance)) changes.Add("maintenance");
        return changes;
    }

    private static IReadOnlyList<string> OperationsWarnings(L12OperationsConfigPayload payload)
    {
        var warnings = new List<string>();
        if (payload.Maintenance.Enabled) warnings.Add("maintenance-enabled");
        if (payload.CardRestrictions.Any(item => item.MaxCopies == 0)) warnings.Add("cards-banned");
        if (payload.FeatureFlags.Any(item => !item.Value)) warnings.Add("features-disabled");
        return warnings;
    }

    private static bool JsonEqual<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private static OperationsConfigVersionRow NewOperationsHistory(OperationsConfigRow config, string action,
        L12AccountView actor, string reason)
        => new()
        {
            Id = config.VersionId,
            Version = config.Version,
            Action = action,
            Config = CloneOperationsRow(config),
            ActorId = actor.Id,
            ActorName = actor.Username,
            Reason = reason,
            CreatedAt = config.UpdatedAt,
        };

    private void TrimOperationsHistory()
    {
        if (_data.OperationsConfigHistory.Count <= OperationsHistoryLimit) return;
        _data.OperationsConfigHistory = _data.OperationsConfigHistory
            .OrderByDescending(row => row.Version).ThenByDescending(row => row.CreatedAt)
            .Take(OperationsHistoryLimit).ToList();
    }

    private static OperationsConfigRow ToRow(L12OperationsConfigPayload payload, long version, string actorName)
    {
        var now = DateTimeOffset.UtcNow;
        return new OperationsConfigRow
        {
            Version = version,
            VersionId = $"ops-{version:D8}-{Guid.NewGuid():N}",
            Season = new OperationsSeasonRow
            {
                Id = payload.Season.Id,
                Name = payload.Season.Name,
                Status = payload.Season.Status,
                StartsAt = payload.Season.StartsAt,
                EndsAt = payload.Season.EndsAt,
            },
            DisasterPool = new OperationsDisasterPoolRow
            {
                CardIds = payload.DisasterPool.CardIds.ToList(),
                AnnihilationLocked = true,
            },
            CardRestrictions = payload.CardRestrictions.Select(item => new OperationsCardRestrictionRow
            {
                CardId = item.CardId,
                MaxCopies = item.MaxCopies,
                Reason = item.Reason,
            }).ToList(),
            DefaultPresetDeckIds = payload.DefaultPresetDeckIds.ToList(),
            MatchModes = payload.MatchModes.Select(item => new OperationsMatchModeRow
            {
                Id = item.Id,
                Name = item.Name,
                Enabled = item.Enabled,
            }).ToList(),
            FeatureFlags = new Dictionary<string, bool>(payload.FeatureFlags, StringComparer.OrdinalIgnoreCase),
            Maintenance = new OperationsMaintenanceRow
            {
                Enabled = payload.Maintenance.Enabled,
                Message = payload.Maintenance.Message,
                StartsAt = payload.Maintenance.StartsAt,
                EndsAt = payload.Maintenance.EndsAt,
            },
            UpdatedBy = actorName,
            UpdatedAt = now,
        };
    }

    private static L12OperationsConfigPayload ToPayload(OperationsConfigRow row)
        => new(
            new L12SeasonConfig(row.Season.Id, row.Season.Name, row.Season.Status,
                row.Season.StartsAt, row.Season.EndsAt),
            new L12SeasonDisasterPoolConfig(row.DisasterPool.CardIds.ToArray(), true),
            row.CardRestrictions.Select(item => new L12CardRestrictionConfig(item.CardId,
                item.MaxCopies, item.Reason)).ToArray(),
            row.DefaultPresetDeckIds.ToArray(),
            row.MatchModes.Select(item => new L12MatchModeConfig(item.Id, item.Name, item.Enabled)).ToArray(),
            new Dictionary<string, bool>(row.FeatureFlags, StringComparer.OrdinalIgnoreCase),
            new L12MaintenanceConfig(row.Maintenance.Enabled, row.Maintenance.Message,
                row.Maintenance.StartsAt, row.Maintenance.EndsAt));

    private static L12OperationsConfigView ToView(OperationsConfigRow row)
        => new(row.Version, row.VersionId, ToPayload(row), row.UpdatedBy, row.UpdatedAt);

    private static L12OperationsConfigVersionView ToView(OperationsConfigVersionRow row)
        => new(row.Id, row.Version, row.Action, ToPayload(row.Config), row.ActorId, row.ActorName,
            row.Reason, row.CreatedAt);

    private OperationsConfigRow RequireOperationsConfig()
        => _data.OperationsConfig ?? throw new InvalidOperationException("运营配置尚未初始化");

    private static OperationsConfigRow CloneOperationsRow(OperationsConfigRow row)
        => JsonSerializer.Deserialize<OperationsConfigRow>(JsonSerializer.Serialize(row))!;

    private static void NormalizeOperationsRow(OperationsConfigRow row)
    {
        if (row.Version < 1) row.Version = 1;
        if (string.IsNullOrWhiteSpace(row.VersionId))
            row.VersionId = $"ops-{row.Version:D8}-{Guid.NewGuid():N}";
        row.Season ??= new OperationsSeasonRow();
        row.DisasterPool ??= new OperationsDisasterPoolRow();
        row.DisasterPool.CardIds ??= [];
        row.DisasterPool.AnnihilationLocked = true;
        row.CardRestrictions ??= [];
        row.DefaultPresetDeckIds ??= [];
        row.MatchModes ??= [];
        row.FeatureFlags ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        row.Maintenance ??= new OperationsMaintenanceRow();
        if (string.IsNullOrWhiteSpace(row.UpdatedBy)) row.UpdatedBy = "系统";
        if (row.UpdatedAt == default) row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void EnsureOperationsVersion(OperationsConfigRow current, long? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != current.Version)
            throw new L12OperationsConfigException("operations_version_conflict", "运营配置版本已变化，请刷新后重试");
    }

    private static void EnsureOperationsPermission(L12AccountView actor, L12Permission permission)
    {
        if (!L12Authorization.HasPermission(actor, permission))
            throw new L12OperationsConfigException("permission_denied", "账号缺少运营配置权限");
    }

    private static string RequireOperationsReason(string? reason)
        => RequireOperationsText(reason, "变更理由", 500);

    private static string RequireOperationsId(string? value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!OperationsIdPattern.IsMatch(normalized))
            throw new L12OperationsConfigException("invalid_operations_config", $"{label}格式无效");
        return normalized;
    }

    private static string RequireCardId(string? value, string label)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is < 3 or > 64 || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new L12OperationsConfigException("invalid_operations_config", $"{label}格式无效");
        return normalized;
    }

    private static string RequireOperationsText(string? value, string label, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maxLength || normalized.Any(char.IsControl))
            throw new L12OperationsConfigException("invalid_operations_config", $"{label}长度或字符无效");
        return normalized;
    }

    private static string OptionalOperationsText(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxLength || normalized.Any(char.IsControl))
            throw new L12OperationsConfigException("invalid_operations_config", "配置文本长度或字符无效");
        return normalized;
    }

    private static void EnsureTimeRange(DateTimeOffset? startsAt, DateTimeOffset? endsAt, string label)
    {
        if (startsAt is not null && endsAt is not null && startsAt >= endsAt)
            throw new L12OperationsConfigException("invalid_time_range", $"{label}开始时间必须早于结束时间");
    }
}
