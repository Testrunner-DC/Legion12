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

    public static L12AdminCommandResult<T> Accepted(L12AdminCommandView command, bool replayed = false)
        => new(true, "approval_required", "命令已提交审批", default, StatusCodes.Status202Accepted,
            replayed, true, command);

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
            if (risk == L12AdminCommandRisk.High && !normalized.DryRun)
            {
                _platform.PersistAdminApprovalRequest(stored.Id, normalized.Actor);
                _platform.RecordCommandOutcome(normalized.Actor,
                    normalized.AuditContext with { Outcome = "pending", Reason = "approval-required" },
                    normalized.Type, normalized.Scope, "approval-required");
                return L12AdminCommandResult<T>.Accepted(stored.View);
            }

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
        L12AdminApprovalDecision decision,
        L12AdminAuditContext reviewContext,
        Func<L12AdminCommandView, L12AccountView, L12AdminAuditContext,
            L12AdminCommandResult<JsonElement>> execute,
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

        var normalizedDecision = decision.Decision?.Trim().ToLowerInvariant();
        if (normalizedDecision is not ("approve" or "reject"))
            return L12AdminCommandResult<L12AdminCommandView>.Fail("invalid_approval_decision",
                "审批决定必须为 approve 或 reject", StatusCodes.Status400BadRequest);

        return _platform.ExecuteAdminTransaction(() =>
        {
            var stored = _platform.AdminCommandRecord(commandId);
            var approval = _platform.AdminApproval(commandId);
            if (stored is null || approval is null)
                return L12AdminCommandResult<L12AdminCommandView>.Fail("command_not_found", "待审批命令不存在",
                    StatusCodes.Status404NotFound);

            if (stored.View.Risk == "high" && !_platform.HighRiskAuditAvailable())
                return L12AdminCommandResult<L12AdminCommandView>.Fail("audit_unavailable",
                    "独立审计不可用，高风险审批已失败关闭", StatusCodes.Status503ServiceUnavailable, stored.View);

            if (canReviewScope is not null && !canReviewScope(stored.View, reviewer))
            {
                _platform.RecordAuthorizationDenied(reviewer,
                    reviewContext with { CommandId = commandId, Outcome = "denied", Reason = "scope-denied" },
                    L12Authorization.Key(reviewPermission), "scope-denied");
                return L12AdminCommandResult<L12AdminCommandView>.Fail("scope_denied",
                    "当前账号不在该命令的审批作用域内", StatusCodes.Status403Forbidden, stored.View);
            }

            if (stored.View.Status != "requested" || approval.Status != "requested")
                return ReplayReview(stored.View);

            if (stored.View.ActorId == reviewer.Id)
            {
                _platform.RecordAuthorizationDenied(reviewer,
                    reviewContext with { CommandId = commandId, Outcome = "denied", Reason = "self-review-forbidden" },
                    L12Authorization.Key(reviewPermission), "self-review-forbidden");
                return L12AdminCommandResult<L12AdminCommandView>.Fail("self_review_forbidden",
                    "命令申请人不能审批自己的命令", StatusCodes.Status403Forbidden, stored.View);
            }

            if (normalizedDecision == "reject")
            {
                _platform.PersistAdminApprovalDecision(commandId, reviewer, "rejected", decision.Reason);
                var rejection = L12AdminCommandResult<JsonElement>.Fail("approval_rejected",
                    "命令已被审批人拒绝", StatusCodes.Status409Conflict);
                stored = _platform.PersistAdminCommandResult(commandId, rejection, "rejected",
                    string.IsNullOrWhiteSpace(decision.Reason) ? "approval-rejected" : decision.Reason);
                _platform.RecordApprovalOutcome(reviewer, stored.View, reviewContext, "rejected", decision.Reason);
                return L12AdminCommandResult<L12AdminCommandView>.Ok(stored.View, "命令已拒绝");
            }

            var requester = _platform.Account(stored.View.ActorId);
            if (requester is null || !L12Authorization.TryFromKey(stored.View.Permission, out var requestedPermission)
                || !L12Authorization.HasPermission(requester, requestedPermission))
            {
                _platform.PersistAdminApprovalDecision(commandId, reviewer, "approved", decision.Reason);
                var revoked = L12AdminCommandResult<JsonElement>.Fail("requester_permission_revoked",
                    "申请人的原始权限已失效", StatusCodes.Status409Conflict);
                stored = _platform.PersistAdminCommandResult(commandId, revoked, "failed",
                    "requester-permission-revoked");
                _platform.RecordApprovalOutcome(reviewer, stored.View, reviewContext, "failed",
                    "requester-permission-revoked");
                return L12AdminCommandResult<L12AdminCommandView>.Fail("requester_permission_revoked",
                    "申请人的原始权限已失效", StatusCodes.Status409Conflict, stored.View);
            }

            if (stored.View.ExpectedVersion is { } expectedVersion
                && _platform.AdminCommandResourceVersion(stored.View.Type, stored.View.Scope) is { } currentVersion
                && expectedVersion != currentVersion)
            {
                _platform.PersistAdminApprovalDecision(commandId, reviewer, "approved", decision.Reason);
                var conflict = L12AdminCommandResult<JsonElement>.Fail("version_conflict",
                    "资源版本已变化，请重新提交命令", StatusCodes.Status409Conflict);
                stored = _platform.PersistAdminCommandResult(commandId, conflict, "failed",
                    "expected-version-mismatch");
                _platform.RecordApprovalOutcome(reviewer, stored.View, reviewContext, "failed",
                    "expected-version-mismatch");
                return L12AdminCommandResult<L12AdminCommandView>.Fail("version_conflict",
                    "资源版本已变化，请重新提交命令", StatusCodes.Status409Conflict, stored.View);
            }

            _platform.PersistAdminApprovalDecision(commandId, reviewer, "approved", decision.Reason);
            var executionAudit = reviewContext with
            {
                Permission = stored.View.Permission,
                CommandId = stored.View.Id,
                IdempotencyKey = stored.View.IdempotencyKey,
                ExpectedVersion = stored.View.ExpectedVersion,
                DryRun = stored.View.DryRun,
                Reason = stored.View.Reason,
                Outcome = "succeeded",
            };
            L12AdminCommandResult<JsonElement> outcome;
            try
            {
                outcome = execute(stored.View, requester, executionAudit);
            }
            catch (Exception error)
            {
                outcome = L12AdminCommandResult<JsonElement>.Fail("command_failed", "命令执行失败",
                    StatusCodes.Status500InternalServerError);
                stored = _platform.PersistAdminCommandResult(commandId, outcome, "failed", error.Message);
                _platform.RecordApprovalOutcome(reviewer, stored.View, reviewContext, "failed", error.Message);
                return L12AdminCommandResult<L12AdminCommandView>.Fail("command_failed", "命令执行失败",
                    StatusCodes.Status500InternalServerError, stored.View);
            }

            stored = _platform.PersistAdminCommandResult(commandId, outcome,
                outcome.Success ? "executed" : "failed", outcome.Success ? null : outcome.Message);
            _platform.RecordApprovalOutcome(reviewer, stored.View, reviewContext,
                outcome.Success ? "executed" : "failed", outcome.Success ? decision.Reason : outcome.Message);
            return outcome.Success
                ? L12AdminCommandResult<L12AdminCommandView>.Ok(stored.View, "命令已批准并执行")
                : L12AdminCommandResult<L12AdminCommandView>.Fail(outcome.Code, outcome.Message,
                    outcome.StatusCode, stored.View);
        });
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
        if (command.Status == "requested") return L12AdminCommandResult<T>.Accepted(command, true);
        T? value = default;
        if (command.Result is { } result && result.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            value = result.Deserialize<T>(JsonOptions);
        var success = command.Status == "executed" && (command.ResultStatusCode ?? 200) < 400;
        return new L12AdminCommandResult<T>(success, command.ResultCode ?? (success ? "ok" : "command_failed"),
            command.ResultMessage ?? (success ? "操作成功" : "命令执行失败"), value,
            command.ResultStatusCode ?? (success ? StatusCodes.Status200OK : StatusCodes.Status409Conflict),
            true, false, command);
    }

    private static L12AdminCommandResult<L12AdminCommandView> ReplayReview(L12AdminCommandView command)
    {
        if (command.Status == "executed")
            return new L12AdminCommandResult<L12AdminCommandView>(true, "ok", "命令已经执行", command,
                StatusCodes.Status200OK, true, Command: command);
        return new L12AdminCommandResult<L12AdminCommandView>(false,
            command.ResultCode ?? "command_not_pending", command.ResultMessage ?? "命令已不在待审批状态", default,
            command.ResultStatusCode ?? StatusCodes.Status409Conflict, true, Command: command);
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
