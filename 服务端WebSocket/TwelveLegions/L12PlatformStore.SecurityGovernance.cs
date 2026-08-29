using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed record L12LoginAttemptContext(string CorrelationId, string ClientKey, string RequestPath);

public sealed record L12AuthenticationResult(bool Success, string Message, L12AccountView? Account,
    string? Token, string Code, int RetryAfterSeconds);

public interface IL12MfaCredentialProtector
{
    string Mode { get; }
    bool Available { get; }
    string Protect(byte[] secret);
    bool TryUnprotect(string protectedValue, out byte[] secret);
}

public sealed class L12UnavailableMfaCredentialProtector : IL12MfaCredentialProtector
{
    public string Mode => "unavailable";
    public bool Available => false;
    public string Protect(byte[] secret) => throw new InvalidOperationException("MFA 凭据保护器未配置");
    public bool TryUnprotect(string protectedValue, out byte[] secret)
    {
        secret = [];
        return false;
    }
}

public sealed record L12MfaCapabilityView(bool CredentialProtectionAvailable, bool EnrollmentEnabled,
    string Mode, bool SecretsPersisted, string Requirement);

public sealed record L12AccountStatusCommandPayload(string AccountId, bool Disabled, string Reason);

public sealed record L12AccountStatusOperationView(bool Applied, L12AccountView Account,
    int RevokedSessions, bool AlreadyApplied);

public sealed record L12SecondApproverBootstrapPayload(string AccountId);

public sealed record L12SecondApproverBootstrapView(bool Applied, string AccountId, string Role,
    bool Replayed = false);

public sealed record L12SecurityAlertView(string Code, string Severity, long Count, string Message);

public sealed record L12SecurityStatusView(
    long PlatformVersion,
    int ActiveApprovers,
    bool SecondApproverReady,
    bool OfflineBootstrapEnabled,
    bool OfflineBootstrapCredentialConfigured,
    bool OfflineBootstrapUsed,
    int DisabledAccounts,
    int ActiveLoginLocks,
    int PendingApprovals,
    DateTimeOffset? OldestPendingApprovalAt,
    bool HighRiskAuditAvailable,
    int AuditRetentionDays,
    int AuditArchiveSegments,
    DateTimeOffset? LastAuditArchiveAt,
    L12MfaCapabilityView Mfa,
    IReadOnlyList<L12SecurityAlertView> Alerts);

public sealed class L12SecurityPolicyException : InvalidOperationException
{
    public string Code { get; }
    public L12SecurityPolicyException(string code, string message) : base(message) => Code = code;
}

public sealed partial class L12PlatformStore
{
    private const int LoginFailureLimit = 5;
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LoginLockDuration = TimeSpan.FromMinutes(15);

    private sealed class LoginThrottleRow
    {
        public string Key { get; set; } = string.Empty;
        public int FailureCount { get; set; }
        public DateTimeOffset WindowStartedAt { get; set; }
        public DateTimeOffset LastFailureAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    private sealed class SecurityStateRow
    {
        public DateTimeOffset? SecondApproverBootstrapUsedAt { get; set; }
        public string? SecondApproverBootstrapAccountId { get; set; }
    }

    private readonly IL12MfaCredentialProtector _mfaCredentialProtector;
    internal Func<bool>? AuditAvailabilityProbeOverride { get; set; }

    public L12MfaCapabilityView MfaCapability()
        => new(_mfaCredentialProtector.Available, false, _mfaCredentialProtector.Mode, false,
            _mfaCredentialProtector.Available
                ? "凭据保护边界已注入，但TOTP双次校验、恢复码hash与撤销会话流程尚未实现，注册保持关闭"
                : "需要由环境密钥或外部密钥系统提供凭据保护器后，才能实现TOTP注册与恢复码生命周期");

    public L12AccountStatusOperationView SetAccountDisabled(L12AccountView actor, string accountId,
        bool disabled, string reason, L12AdminAuditContext context, bool apply)
    {
        EnsurePermission(actor, L12Permission.AdminAccountStatusWrite);
        var normalizedReason = NormalizeSecurityReason(reason);
        string[] revokedIds = [];
        L12AccountStatusOperationView result;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new KeyNotFoundException("账号不存在");
            if (row.Deleted)
                throw new L12SecurityPolicyException("account_deleted", "已逻辑删除的账号不能恢复或变更状态");
            if (string.Equals(row.Username, "Admin", StringComparison.Ordinal))
                throw new L12SecurityPolicyException("root_admin_protected", "根 Admin 账号不能被禁用或通过该入口改状态");
            if (row.Id == actor.Id)
                throw new L12SecurityPolicyException("self_status_change_forbidden", "不能通过后台命令禁用或启用自己的账号");
            if (disabled && string.Equals(row.Role, "admin", StringComparison.OrdinalIgnoreCase)
                && _data.Accounts.Count(item => !item.Disabled && !item.Deleted
                    && string.Equals(item.Role, "admin", StringComparison.OrdinalIgnoreCase)) <= 1)
                throw new L12SecurityPolicyException("last_admin_protected", "不能禁用最后一个可用管理员账号");

            var alreadyApplied = row.Disabled == disabled;
            if (!apply || alreadyApplied)
            {
                var preview = ToView(row) with
                {
                    Disabled = disabled,
                    DisabledAt = disabled ? row.DisabledAt ?? DateTimeOffset.UtcNow : null,
                    DisabledReason = disabled ? normalizedReason : null,
                };
                return new(false, preview, 0, alreadyApplied);
            }

            var now = DateTimeOffset.UtcNow;
            row.Disabled = disabled;
            row.DisabledAt = disabled ? now : null;
            row.DisabledByAccountId = disabled ? actor.Id : null;
            row.DisabledReason = disabled ? normalizedReason : null;
            row.PermissionVersion++;
            if (disabled)
            {
                var sessions = _data.Sessions.Where(item => item.AccountId == row.Id
                    && item.RevokedAt is null && item.ExpiresAt > now).ToArray();
                foreach (var session in sessions) session.RevokedAt = now;
                revokedIds = sessions.Select(item => item.Id).ToArray();
            }
            AddAdminAudit(actor, "account", disabled ? "disable" : "enable", row.Username,
                disabled ? "enabled" : "disabled", disabled ? "disabled" : "enabled", normalizedReason,
                context with { Outcome = "succeeded", Reason = normalizedReason });
            Save();
            result = new(true, ToView(row), revokedIds.Length, false);
        }
        NotifySessionsRevoked(revokedIds);
        return result;
    }

    public L12AdminCommandResult<L12SecondApproverBootstrapView> BootstrapSecondApprover(
        string accountId, string suppliedCredential)
    {
        var actor = new L12AccountView("offline-bootstrap", "离线引导", "admin", DateTimeOffset.UtcNow, false);
        var context = new L12AdminAuditContext("offline-bootstrap", "admin.accounts.roles.write",
            RequestMethod: "OFFLINE", RequestPath: "--bootstrap-second-approver");
        if (!OfflineBootstrapEnabled())
            return BootstrapRejected(actor, context, "bootstrap_disabled", "离线第二审批人引导未显式开启");

        var expectedCredential = Environment.GetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN") ?? string.Empty;
        if (expectedCredential.Length < 32)
            return BootstrapRejected(actor, context, "bootstrap_credential_unavailable",
                "离线引导一次性凭据未配置或强度不足");
        if (!FixedCredentialEquals(expectedCredential, suppliedCredential))
            return BootstrapRejected(actor, context, "bootstrap_credential_invalid", "离线引导凭据无效");

        var credentialFingerprint = Fingerprint(suppliedCredential);
        var idempotencyKey = $"bootstrap-second-approver-{credentialFingerprint[..24]}";
        var prior = FindAdminCommand(actor.Id, idempotencyKey);
        var expectedVersion = prior?.View.ExpectedVersion ?? Version;
        var commandId = Guid.NewGuid().ToString("N");
        var payload = new L12SecondApproverBootstrapPayload(accountId.Trim());
        var command = new L12AdminCommandEnvelope<L12SecondApproverBootstrapPayload>(commandId, idempotencyKey,
            "security.bootstrap.second-approver", actor, DateTimeOffset.UtcNow, $"account:{payload.AccountId}",
            "受控离线建立第二审批人", false, expectedVersion, payload,
            context with { CommandId = commandId, IdempotencyKey = idempotencyKey,
                ExpectedVersion = expectedVersion, Reason = "controlled-offline-bootstrap" });
        return new L12AdminCommandBus(this).Execute(command, L12Permission.AdminAccountRolesWrite,
            current => BootstrapSecondApproverCore(current.Actor, current.Payload.AccountId,
                current.AuditContext), risk: L12AdminCommandRisk.OfflineBootstrap);
    }

    public L12SecurityStatusView SecurityStatus(L12AccountView actor)
    {
        EnsurePermission(actor, L12Permission.AdminSecurityRead);
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var activeApprovers = _data.Accounts.Count(item => !item.Disabled
                && L12Authorization.HasPermission(item.Role, L12Permission.AdminApprovalsReview));
            var activeLocks = _data.LoginThrottles.Count(item => item.LockedUntil > now);
            var directCommandIds = _data.AdminCommands
                .Where(command => IsDirectExecutionCommandType(command.Type))
                .Select(command => command.Id)
                .ToHashSet(StringComparer.Ordinal);
            var pending = _data.AdminApprovals
                .Where(item => item.Status == "requested" && !directCommandIds.Contains(item.CommandId))
                .ToArray();
            var archiveSegments = AuditArchiveSegmentsInternalSafe();
            var auditAvailable = HighRiskAuditAvailable();
            var retentionDays = AuditRetentionDays();
            var alerts = new List<L12SecurityAlertView>();
            if (activeApprovers < 2)
                alerts.Add(new("second-approver-missing", "critical", 2 - activeApprovers,
                    "有效审批人少于2名，高风险命令无法形成可靠双人复核"));
            if (!auditAvailable)
                alerts.Add(new("audit-unavailable", "critical", 1,
                    "独立审计不可用，高风险命令与审批已失败关闭"));
            if (activeLocks > 0)
                alerts.Add(new("login-lockout-active", "warning", activeLocks, "存在生效中的登录限流锁定"));
            var oldestPending = pending.OrderBy(item => item.RequestedAt).FirstOrDefault()?.RequestedAt;
            if (pending.Length >= 10 || oldestPending is { } oldest && now - oldest > TimeSpan.FromHours(24))
                alerts.Add(new("approval-backlog", "warning", pending.Length, "待审批命令达到积压阈值"));
            var releaseFailures = _data.ReleaseRuns.Count(item => item.CompletedAt >= now.AddHours(-24)
                && item.Status is "failed" or "rolled-back");
            if (releaseFailures > 0)
                alerts.Add(new("release-validation-failures", "warning", releaseFailures,
                    "最近24小时存在发布验证失败或自动回滚"));
            var lastArchive = archiveSegments.OrderByDescending(item => item.CreatedAt).FirstOrDefault()?.CreatedAt;
            if (CountArchivableAuditEventsSafe(now.AddDays(-retentionDays)) > 0
                && (lastArchive is null || now - lastArchive > TimeSpan.FromDays(30)))
                alerts.Add(new("audit-archive-overdue", "warning", 1, "独立审计尚无近期校验归档段"));
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD")))
                alerts.Add(new("root-password-default-source", "critical", 1,
                    "根管理员密码未由部署环境显式提供"));

            return new(Version, activeApprovers, activeApprovers >= 2, OfflineBootstrapEnabled(),
                OfflineBootstrapCredentialConfigured(), _data.Security.SecondApproverBootstrapUsedAt is not null,
                _data.Accounts.Count(item => item.Disabled), activeLocks, pending.Length, oldestPending,
                auditAvailable, retentionDays, archiveSegments.Count, lastArchive, MfaCapability(), alerts);
        }
    }

    internal bool HighRiskAuditAvailable()
    {
        lock (_gate)
        {
            if (!_storageWritable) return false;
            if (AuditAvailabilityProbeOverride is not null)
            {
                try { return AuditAvailabilityProbeOverride(); }
                catch { return false; }
            }
            try
            {
                using var connection = OpenDatabase(_databasePath, readOnly: true);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM admin_audit_events LIMIT 1;";
                _ = command.ExecuteScalar();
                return true;
            }
            catch { return false; }
        }
    }

    private L12AdminCommandResult<L12SecondApproverBootstrapView> BootstrapSecondApproverCore(
        L12AccountView actor, string accountId, L12AdminAuditContext context)
    {
        lock (_gate)
        {
            if (_data.Security.SecondApproverBootstrapUsedAt is not null)
                return L12AdminCommandResult<L12SecondApproverBootstrapView>.Fail("bootstrap_already_used",
                    "离线第二审批人引导已经使用", StatusCodes.Status409Conflict);
            var activeApprovers = _data.Accounts.Count(item => !item.Disabled
                && L12Authorization.HasPermission(item.Role, L12Permission.AdminApprovalsReview));
            if (activeApprovers >= 2)
                return L12AdminCommandResult<L12SecondApproverBootstrapView>.Fail("second_approver_already_ready",
                    "系统已经具备第二审批人", StatusCodes.Status409Conflict);
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (row is null || row.Disabled || string.Equals(row.Username, "Admin", StringComparison.Ordinal))
                return L12AdminCommandResult<L12SecondApproverBootstrapView>.Fail("bootstrap_target_invalid",
                    "引导目标账号不存在、已禁用或为根管理员", StatusCodes.Status400BadRequest);
            if (L12Authorization.HasPermission(row.Role, L12Permission.AdminApprovalsReview))
                return L12AdminCommandResult<L12SecondApproverBootstrapView>.Fail("bootstrap_target_already_approver",
                    "目标账号已经具有审批权限", StatusCodes.Status409Conflict);

            var previousRole = row.Role;
            row.Role = "admin";
            row.PermissionVersion++;
            var now = DateTimeOffset.UtcNow;
            var sessions = _data.Sessions.Where(item => item.AccountId == row.Id
                && item.RevokedAt is null && item.ExpiresAt > now).ToArray();
            foreach (var session in sessions) session.RevokedAt = now;
            _data.Security.SecondApproverBootstrapUsedAt = now;
            _data.Security.SecondApproverBootstrapAccountId = row.Id;
            AddAdminAudit(actor, "security", "bootstrap-second-approver", row.Username, previousRole,
                row.Role, "controlled-offline-bootstrap", context with { Outcome = "succeeded" });
            Save();
            NotifySessionsRevoked(sessions.Select(item => item.Id).ToArray());
            return L12AdminCommandResult<L12SecondApproverBootstrapView>.Ok(
                new(true, row.Id, row.Role), "第二审批人已建立，目标账号旧会话已撤销");
        }
    }

    private L12AdminCommandResult<L12SecondApproverBootstrapView> BootstrapRejected(L12AccountView actor,
        L12AdminAuditContext context, string code, string message)
    {
        lock (_gate)
        {
            AddAdminAudit(actor, "security", "bootstrap-second-approver", "offline-bootstrap", null, null,
                code, context with { Outcome = "denied", Reason = code });
            Save(false);
        }
        return L12AdminCommandResult<L12SecondApproverBootstrapView>.Fail(code, message,
            StatusCodes.Status403Forbidden);
    }

    private void AddAuthenticationAudit(L12AccountView? actor, string normalizedUsername, string reason,
        L12LoginAttemptContext context, string outcome)
    {
        var principalFingerprint = Fingerprint(normalizedUsername)[..16];
        AddAdminAudit(actor, "authentication", "login", $"principal:{principalFingerprint}", null, null,
            reason, new L12AdminAuditContext(context.CorrelationId, RequestMethod: "POST",
                RequestPath: context.RequestPath, Outcome: outcome, Reason: reason));
    }

    private static string[] LoginThrottleKeys(string normalizedUsername, string clientKey)
        => [$"u:{Fingerprint(normalizedUsername)}", $"c:{Fingerprint(clientKey.Trim().ToLowerInvariant())}"];

    private int LoginRetryAfterSeconds(IEnumerable<string> keys, DateTimeOffset now)
    {
        var lockedUntil = _data.LoginThrottles.Where(item => keys.Contains(item.Key, StringComparer.Ordinal))
            .Select(item => item.LockedUntil).Where(item => item is not null && item > now)
            .OrderByDescending(item => item).FirstOrDefault();
        return lockedUntil is null ? 0 : Math.Max(1, (int)Math.Ceiling((lockedUntil.Value - now).TotalSeconds));
    }

    private int RegisterLoginFailure(IEnumerable<string> keys, DateTimeOffset now)
    {
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            var row = _data.LoginThrottles.FirstOrDefault(item => item.Key == key);
            if (row is null)
            {
                row = new LoginThrottleRow { Key = key, WindowStartedAt = now };
                _data.LoginThrottles.Add(row);
            }
            if (row.WindowStartedAt == default || now - row.WindowStartedAt > LoginFailureWindow)
            {
                row.WindowStartedAt = now;
                row.FailureCount = 0;
                row.LockedUntil = null;
            }
            row.FailureCount++;
            row.LastFailureAt = now;
            if (row.FailureCount >= LoginFailureLimit) row.LockedUntil = now.Add(LoginLockDuration);
        }
        return LoginRetryAfterSeconds(keys, now);
    }

    private void ClearLoginFailures(IEnumerable<string> keys)
    {
        var values = keys.ToHashSet(StringComparer.Ordinal);
        _data.LoginThrottles.RemoveAll(item => values.Contains(item.Key));
    }

    private void PruneLoginThrottles(DateTimeOffset now)
        => _data.LoginThrottles.RemoveAll(item => (item.LockedUntil is null || item.LockedUntil <= now)
            && item.LastFailureAt < now.AddDays(-1));

    private static void NormalizeLoginThrottle(LoginThrottleRow row)
    {
        row.Key = row.Key?.Trim() ?? string.Empty;
        var valid = row.Key.Length == 66 && row.Key[1] == ':' && row.Key[0] is 'u' or 'c'
            && row.Key[2..].All(Uri.IsHexDigit);
        if (!valid) row.Key = string.Empty;
        row.FailureCount = Math.Clamp(row.FailureCount, 0, LoginFailureLimit);
        if (row.LockedUntil < row.LastFailureAt) row.LockedUntil = null;
    }

    private static void NormalizeSecurityState(SecurityStateRow row)
    {
        if (string.IsNullOrWhiteSpace(row.SecondApproverBootstrapAccountId))
            row.SecondApproverBootstrapAccountId = null;
    }

    private static string NormalizeSecurityReason(string reason)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is < 3 or > 500)
            throw new L12SecurityPolicyException("security_reason_required", "安全状态变更理由长度需为3–500个字符");
        return normalized;
    }

    private static bool OfflineBootstrapEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("L12_ENABLE_SECOND_APPROVER_BOOTSTRAP"), "true",
               StringComparison.OrdinalIgnoreCase)
           || Environment.GetEnvironmentVariable("L12_ENABLE_SECOND_APPROVER_BOOTSTRAP") == "1";

    private static bool OfflineBootstrapCredentialConfigured()
        => (Environment.GetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN") ?? string.Empty).Length >= 32;

    private static bool FixedCredentialEquals(string expected, string supplied)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static string Fingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

}
