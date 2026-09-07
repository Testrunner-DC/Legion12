using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public static class L12CorrelationIds
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ContextItemName = "l12.correlation-id";
    public const string OriginalPathItemName = "l12.original-path";

    public static string AcceptOrCreate(string? value)
    {
        var candidate = value?.Trim();
        return IsValid(candidate) ? candidate! : Guid.NewGuid().ToString("N");
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !char.IsLetterOrDigit(value[0])) return false;
        return value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}

public sealed record L12ApiError(string Code, string Message, string CorrelationId);

public enum L12Permission
{
    SessionsReadOwn,
    SessionsRevokeOwn,
    AdminAccountsRead,
    AdminAccountRolesWrite,
    AdminAccountStatusWrite,
    AdminSessionsRead,
    AdminSessionsRevoke,
    AdminBugsRead,
    AdminBugsWrite,
    AdminMatchGovernanceRead,
    AdminMatchGovernanceWrite,
    AdminContentRead,
    AdminContentDraft,
    AdminContentPublish,
    AdminContentRollback,
    AdminEffectsRead,
    AdminEffectsReview,
    AdminMatchesRead,
    AdminAnalyticsRead,
    AdminAuditRead,
    AdminAuditArchive,
    AdminSecurityRead,
    AdminCommandsRead,
    AdminApprovalsRead,
    AdminApprovalsReview,
    AdminOperationsRead,
    AdminOperationsWrite,
    AdminRuntimeRead,
    TournamentsRead,
    TournamentsCreate,
    TournamentsRegister,
    TournamentsManage,
    TournamentRulingsWrite,
    TournamentImportLegacy,
    ReleasesRead,
    ReleasesExecute,
    ReleaseApprovalsReview,
    ReleaseRuntimeRead,
}

public static class L12Authorization
{
    private static readonly IReadOnlyDictionary<L12Permission, string> PermissionKeys =
        new Dictionary<L12Permission, string>
        {
            [L12Permission.SessionsReadOwn] = "sessions.read.own",
            [L12Permission.SessionsRevokeOwn] = "sessions.revoke.own",
            [L12Permission.AdminAccountsRead] = "admin.accounts.read",
            [L12Permission.AdminAccountRolesWrite] = "admin.accounts.roles.write",
            [L12Permission.AdminAccountStatusWrite] = "admin.accounts.status.write",
            [L12Permission.AdminSessionsRead] = "admin.sessions.read",
            [L12Permission.AdminSessionsRevoke] = "admin.sessions.revoke",
            [L12Permission.AdminBugsRead] = "admin.bugs.read",
            [L12Permission.AdminBugsWrite] = "admin.bugs.write",
            [L12Permission.AdminMatchGovernanceRead] = "admin.match-governance.read",
            [L12Permission.AdminMatchGovernanceWrite] = "admin.match-governance.write",
            [L12Permission.AdminContentRead] = "admin.content.read",
            [L12Permission.AdminContentDraft] = "admin.content.draft",
            [L12Permission.AdminContentPublish] = "admin.content.publish",
            [L12Permission.AdminContentRollback] = "admin.content.rollback",
            [L12Permission.AdminEffectsRead] = "admin.effects.read",
            [L12Permission.AdminEffectsReview] = "admin.effects.review",
            [L12Permission.AdminMatchesRead] = "admin.matches.read",
            [L12Permission.AdminAnalyticsRead] = "admin.analytics.read",
            [L12Permission.AdminAuditRead] = "admin.audit.read",
            [L12Permission.AdminAuditArchive] = "admin.audit.archive",
            [L12Permission.AdminSecurityRead] = "admin.security.read",
            [L12Permission.AdminCommandsRead] = "admin.commands.read",
            [L12Permission.AdminApprovalsRead] = "admin.approvals.read",
            [L12Permission.AdminApprovalsReview] = "admin.approvals.review",
            [L12Permission.AdminOperationsRead] = "admin.operations.read",
            [L12Permission.AdminOperationsWrite] = "admin.operations.write",
            [L12Permission.AdminRuntimeRead] = "admin.runtime.read",
            [L12Permission.TournamentsRead] = "tournaments.read",
            [L12Permission.TournamentsCreate] = "tournaments.create",
            [L12Permission.TournamentsRegister] = "tournaments.register",
            [L12Permission.TournamentsManage] = "tournaments.manage",
            [L12Permission.TournamentRulingsWrite] = "tournaments.rulings.write",
            [L12Permission.TournamentImportLegacy] = "tournaments.import-legacy",
            [L12Permission.ReleasesRead] = "releases.read",
            [L12Permission.ReleasesExecute] = "releases.execute",
            [L12Permission.ReleaseApprovalsReview] = "releases.approvals.review",
            [L12Permission.ReleaseRuntimeRead] = "releases.runtime.read",
        };

    private static readonly IReadOnlyDictionary<string, L12Permission> PermissionsByKey = PermissionKeys
        .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly L12Permission[] OwnSessionPermissions =
    [
        L12Permission.SessionsReadOwn,
        L12Permission.SessionsRevokeOwn,
    ];

    private static readonly L12Permission[] PlayerPermissions =
    [
        .. OwnSessionPermissions,
        L12Permission.TournamentsRead,
        L12Permission.TournamentsCreate,
        L12Permission.TournamentsRegister,
        L12Permission.TournamentImportLegacy,
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<L12Permission>> RolePermissions =
        new Dictionary<string, IReadOnlySet<L12Permission>>(StringComparer.OrdinalIgnoreCase)
        {
            ["player"] = Set(PlayerPermissions),
            ["admin"] = Set(Enum.GetValues<L12Permission>()),
        };

    public static IReadOnlyList<string> Roles { get; } = RolePermissions.Keys.OrderBy(role => role).ToArray();
    public static IReadOnlyList<string> AllPermissionKeys { get; } = PermissionKeys.Values.OrderBy(key => key).ToArray();

    public static bool IsKnownRole(string? role)
        => !string.IsNullOrWhiteSpace(role) && RolePermissions.ContainsKey(role);

    public static bool HasPermission(L12AccountView? account, L12Permission permission)
        => account is not null && HasPermission(account.Role, permission);

    public static bool HasPermission(string? role, L12Permission permission)
        => !string.IsNullOrWhiteSpace(role)
           && RolePermissions.TryGetValue(role, out var permissions)
           && permissions.Contains(permission);

    public static IReadOnlyList<string> PermissionsForRole(string? role)
        => !string.IsNullOrWhiteSpace(role) && RolePermissions.TryGetValue(role, out var permissions)
            ? permissions.Select(Key).OrderBy(key => key).ToArray()
            : [];

    public static string Key(L12Permission permission) => PermissionKeys[permission];

    public static bool TryFromKey(string? key, out L12Permission permission)
        => PermissionsByKey.TryGetValue(key ?? string.Empty, out permission);

    private static IReadOnlySet<L12Permission> Set(IEnumerable<L12Permission> permissions)
        => permissions.ToHashSet();
}

public sealed record L12AdminAuditContext(
    string CorrelationId,
    string? Permission = null,
    string? CommandId = null,
    string? IdempotencyKey = null,
    long? ExpectedVersion = null,
    bool DryRun = false,
    string? Reason = null,
    string? RequestMethod = null,
    string? RequestPath = null,
    string Outcome = "succeeded");

public sealed record L12AdminCommandEnvelope<TPayload>(
    string CommandId,
    string? IdempotencyKey,
    string Type,
    L12AccountView Actor,
    DateTimeOffset RequestedAt,
    string Scope,
    string? Reason,
    bool DryRun,
    long? ExpectedVersion,
    TPayload Payload,
    L12AdminAuditContext AuditContext);

public enum L12AdminCommandRisk
{
    Low,
    High,
    OfflineBootstrap,
}

public sealed record L12AdminCommandView(
    string Id,
    string? IdempotencyKey,
    string Type,
    string ActorId,
    string ActorName,
    DateTimeOffset RequestedAt,
    string Scope,
    string? Reason,
    bool DryRun,
    long? ExpectedVersion,
    string Risk,
    string Status,
    string Permission,
    JsonElement Payload,
    JsonElement? Result,
    string? ResultCode,
    string? ResultMessage,
    int? ResultStatusCode,
    string? FailureReason,
    string CorrelationId,
    long ResourceVersion,
    DateTimeOffset UpdatedAt);

public sealed record L12AdminApprovalView(
    string CommandId,
    string RequesterId,
    string RequesterName,
    DateTimeOffset RequestedAt,
    string Status,
    string? ReviewerId,
    string? ReviewerName,
    string? Decision,
    string? Reason,
    DateTimeOffset? ReviewedAt);

public sealed record L12AdminApprovalDecision(string Decision, string? Reason = null);

public sealed record L12AdminCommandResult<T>(
    bool Success,
    string Code,
    string Message,
    T? Value,
    int StatusCode,
    bool Replayed = false,
    bool Pending = false,
    L12AdminCommandView? Command = null)
{
    public static L12AdminCommandResult<T> Ok(T value, string message = "操作成功")
        => new(true, "ok", message, value, StatusCodes.Status200OK);

    public static L12AdminCommandResult<T> Fail(string code, string message, int statusCode,
        L12AdminCommandView? command = null)
        => new(false, code, message, default, statusCode, Command: command);
}

public sealed class L12AdminCommandBus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly L12PlatformStore _platform;

    public L12AdminCommandBus(L12PlatformStore platform) => _platform = platform;

    public L12AdminCommandResult<T> Execute<TPayload, T>(
        L12AdminCommandEnvelope<TPayload> command,
        L12Permission permission,
        Func<L12AdminCommandEnvelope<TPayload>, L12AdminCommandResult<T>> execute,
        Func<L12AdminCommandEnvelope<TPayload>, L12AdminCommandResult<T>>? dryRun = null,
        L12AdminCommandRisk risk = L12AdminCommandRisk.Low,
        Func<L12AccountView, bool>? scopedAuthorization = null)
    {
        if (!(scopedAuthorization?.Invoke(command.Actor)
              ?? L12Authorization.HasPermission(command.Actor, permission)))
        {
            _platform.RecordAuthorizationDenied(command.Actor, command.AuditContext,
                L12Authorization.Key(permission), "permission-denied");
            return L12AdminCommandResult<T>.Fail("permission_denied", "当前账号没有执行此操作的权限",
                StatusCodes.Status403Forbidden);
        }

        if (risk != L12AdminCommandRisk.Low && !_platform.HighRiskAuditAvailable())
            return L12AdminCommandResult<T>.Fail("audit_unavailable",
                "独立审计不可用，高风险命令已失败关闭", StatusCodes.Status503ServiceUnavailable);

        var idempotencyKey = NormalizeIdempotencyKey(command.IdempotencyKey);
        if (command.IdempotencyKey is not null && idempotencyKey is null)
        {
            _platform.RecordCommandOutcome(command.Actor, command.AuditContext with { Outcome = "rejected" },
                command.Type, command.Scope, "invalid-idempotency-key");
            return L12AdminCommandResult<T>.Fail("invalid_idempotency_key", "幂等键格式无效",
                StatusCodes.Status400BadRequest);
        }

        var normalized = command with
        {
            IdempotencyKey = idempotencyKey,
            AuditContext = command.AuditContext with { IdempotencyKey = idempotencyKey },
        };
        var payloadJson = JsonSerializer.Serialize(normalized.Payload, JsonOptions);
        var signature = Signature(normalized, payloadJson);

        return _platform.ExecuteAdminTransaction(() =>
        {
            if (idempotencyKey is not null)
            {
                var prior = _platform.FindAdminCommand(normalized.Actor.Id, idempotencyKey);
                if (prior is not null)
                {
                    if (!string.Equals(prior.Signature, signature, StringComparison.Ordinal))
                    {
                        _platform.RecordCommandOutcome(normalized.Actor,
                            normalized.AuditContext with { Outcome = "rejected" }, normalized.Type,
                            normalized.Scope, "idempotency-key-reused");
                        return L12AdminCommandResult<T>.Fail("idempotency_conflict",
                            "同一幂等键已用于不同命令", StatusCodes.Status409Conflict);
                    }
                    return Replay<T>(prior.View);
                }
            }

            if (HasVersionConflict(normalized))
            {
                var failed = _platform.PersistAdminCommand(normalized, L12Authorization.Key(permission), risk,
                    signature, payloadJson, "failed");
                var result = L12AdminCommandResult<T>.Fail("version_conflict", "资源版本已变化，请刷新后重试",
                    StatusCodes.Status409Conflict, failed.View);
                failed = _platform.PersistAdminCommandResult(failed.Id, result, "failed", "expected-version-mismatch");
                _platform.RecordCommandOutcome(normalized.Actor,
                    normalized.AuditContext with { Outcome = "rejected" }, normalized.Type,
                    normalized.Scope, "expected-version-mismatch");
                return result with { Command = failed.View };
            }

            var stored = _platform.PersistAdminCommand(normalized, L12Authorization.Key(permission), risk,
                signature, payloadJson, "requested");
            var invoke = normalized.DryRun
                ? dryRun ?? (_ => L12AdminCommandResult<T>.Fail("dry_run_not_supported", "该命令暂不支持干运行",
                    StatusCodes.Status400BadRequest))
                : execute;
            L12AdminCommandResult<T> outcome;
            try
            {
                outcome = invoke(normalized);
            }
            catch (Exception error)
            {
                outcome = L12AdminCommandResult<T>.Fail("command_failed", "命令执行失败",
                    StatusCodes.Status500InternalServerError);
                stored = _platform.PersistAdminCommandResult(stored.Id, outcome, "failed", error.Message);
                _platform.RecordCommandOutcome(normalized.Actor,
                    normalized.AuditContext with { Outcome = "failed", Reason = error.Message },
                    normalized.Type, normalized.Scope, error.Message);
                return outcome with { Command = stored.View };
            }

            var status = outcome.Success ? "executed" : "failed";
            stored = _platform.PersistAdminCommandResult(stored.Id, outcome, status,
                outcome.Success ? null : outcome.Message);
            _platform.RecordCommandOutcome(normalized.Actor,
                normalized.AuditContext with
                {
                    Outcome = normalized.DryRun ? "dry-run" : outcome.Success ? "succeeded" : "failed",
                    Reason = outcome.Success ? normalized.Reason : outcome.Message,
                }, normalized.Type, normalized.Scope, outcome.Success ? normalized.Reason ?? "completed" : outcome.Message);
            return outcome with { Command = stored.View };
        });
    }

    public L12AdminCommandResult<L12AdminCommandView> Review(
        string commandId,
        L12AccountView reviewer,
        L12AdminAuditContext reviewContext,
        L12Permission reviewPermission = L12Permission.AdminApprovalsReview,
        Func<L12AdminCommandView, L12AccountView, bool>? canReviewScope = null)
    {
        if (!L12Authorization.HasPermission(reviewer, reviewPermission))
        {
            _platform.RecordAuthorizationDenied(reviewer, reviewContext, L12Authorization.Key(reviewPermission),
                "permission-denied");
            return L12AdminCommandResult<L12AdminCommandView>.Fail("permission_denied",
                "当前账号没有审批权限", StatusCodes.Status403Forbidden);
        }

        var stored = _platform.AdminCommandRecord(commandId);
        if (stored is null)
            return L12AdminCommandResult<L12AdminCommandView>.Fail("command_not_found", "命令不存在",
                StatusCodes.Status404NotFound);
        if (canReviewScope is not null && !canReviewScope(stored.View, reviewer))
        {
            _platform.RecordAuthorizationDenied(reviewer,
                reviewContext with { CommandId = commandId, Outcome = "denied", Reason = "scope-denied" },
                L12Authorization.Key(reviewPermission), "scope-denied");
            return L12AdminCommandResult<L12AdminCommandView>.Fail("scope_denied",
                "当前账号不在该命令的作用域内", StatusCodes.Status403Forbidden, stored.View);
        }

        _platform.RecordCommandOutcome(reviewer,
            reviewContext with { CommandId = commandId, Outcome = "rejected", Reason = "approval-disabled" },
            "approval.review", stored.View.Scope, "approval-disabled");
        return L12AdminCommandResult<L12AdminCommandView>.Fail("approval_disabled",
            "后台命令已改为有权限管理员直接执行；历史待审批命令不会执行，请使用新幂等键重新提交",
            StatusCodes.Status409Conflict, stored.View);
    }

    private bool HasVersionConflict<TPayload>(L12AdminCommandEnvelope<TPayload> command)
        => command.ExpectedVersion is { } expectedVersion
           && _platform.AdminCommandResourceVersion(command.Type, command.Scope) is { } currentVersion
           && expectedVersion != currentVersion;

    private static string Signature<TPayload>(L12AdminCommandEnvelope<TPayload> command, string payloadJson)
    {
        var signaturePayload = command.Payload switch
        {
            L12ContentPublishCommandPayload publish => string.Join(',', publish.Items.Select(item => item.Key)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)),
            L12ContentRollbackCommandPayload rollback => rollback.BatchId,
            _ => payloadJson,
        };
        var source = $"{command.Type}\n{command.Scope}\n{command.ExpectedVersion}\n{command.DryRun}\n{signaturePayload}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static L12AdminCommandResult<T> Replay<T>(L12AdminCommandView command)
    {
        if (command.Status == "requested")
            return new L12AdminCommandResult<T>(false, "approval_request_retired",
                "该命令来自已停用的审批流程，不会执行；请使用新幂等键重新提交", default,
                StatusCodes.Status409Conflict, true, false, command);
        T? value = default;
        if (command.Result is { } result && result.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            value = result.Deserialize<T>(JsonOptions);
        var success = command.Status == "executed" && (command.ResultStatusCode ?? 200) < 400;
        return new L12AdminCommandResult<T>(success, command.ResultCode ?? (success ? "ok" : "command_failed"),
            command.ResultMessage ?? (success ? "操作成功" : "命令执行失败"), value,
            command.ResultStatusCode ?? (success ? StatusCodes.Status200OK : StatusCodes.Status409Conflict),
            true, false, command);
    }

    private static string? NormalizeIdempotencyKey(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length is < 1 or > 128) return null;
        return trimmed.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            ? trimmed
            : null;
    }
}
