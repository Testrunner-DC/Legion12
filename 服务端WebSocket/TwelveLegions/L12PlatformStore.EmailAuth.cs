using System.Net.Mail;
using System.Security.Cryptography;

namespace TwelveLegions.Server;

public sealed record L12EmailMessage(string To, string Subject, string TextBody, string Purpose);
public sealed record L12EmailSendResult(bool Success, string Code, string Message);

public static class L12EmailFeature
{
    public const string EnvironmentVariable = "L12_EMAIL_FEATURE_ENABLED";

    public static bool EnabledFromEnvironment()
        => false; // Retained for configuration compatibility; email is permanently retired.
}

public interface IL12EmailSender
{
    bool IsConfigured { get; }
    string PublicBaseUrl { get; }
    L12EmailSendResult Send(L12EmailMessage message);
}

public sealed class L12UnavailableEmailSender : IL12EmailSender
{
    public bool IsConfigured => false;
    public string PublicBaseUrl => string.Empty;
    public L12EmailSendResult Send(L12EmailMessage message)
        => new(false, "email_feature_disabled", "邮箱功能已停用");
}

// Compatibility factory only. Never load SMTP credentials or open a mail connection.
public static class L12SmtpEmailSender
{
    public static IL12EmailSender FromEnvironment() => new L12UnavailableEmailSender();
}

public sealed record L12EmailCapabilityView(bool Enabled, bool MailConfigured);
public sealed record L12EmailStatusView(bool Bound, bool Verified, string? MaskedEmail,
    string? PendingMaskedEmail, DateTimeOffset? PendingExpiresAt, bool FeatureEnabled, bool MailConfigured);
public sealed record L12AuthOperationResult(bool Success, string Code, string Message,
    int RetryAfterSeconds = 0);
public sealed record L12AdminPasswordResetView(bool Applied, L12AccountView Account,
    int RevokedSessions, string? TemporaryPassword = null);
public sealed record L12AccountDeletionView(bool Applied, L12AccountView Account,
    int RevokedSessions, int RemovedPrivateRecords, int CleanedMatchRecords = 0);

public sealed partial class L12PlatformStore
{
    private const string PasswordResetPurpose = "password-reset";
    private const string EmailFeatureDisabledMessage = "邮箱功能已停用";

    private sealed class EmailAuthTokenRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AccountId { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public string TargetEmail { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public DateTimeOffset? ConsumedAt { get; set; }
    }

    private sealed class AuthActionThrottleRow
    {
        public string Key { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTimeOffset WindowStartedAt { get; set; }
    }

    // Keep old API shapes fail-closed, without looking up accounts, consuming tokens,
    // taking the platform gate, doing password work, or persisting any changes.
    public L12EmailCapabilityView EmailCapability() => new(false, false);

    public L12EmailStatusView EmailStatus(string accountId)
        => new(false, false, null, null, null, false, false);

    public L12AuthOperationResult RequestEmailBinding(string accountId, string currentPassword, string email,
        string clientKey) => EmailDisabled();

    public L12AuthOperationResult VerifyEmail(string token, string clientKey) => EmailDisabled();

    public L12AuthOperationResult UnbindEmail(string accountId, string currentPassword) => EmailDisabled();

    public L12AuthOperationResult RequestPasswordReset(string email, string clientKey) => EmailDisabled();

    public L12AuthOperationResult ResetPassword(string token, string newPassword, string clientKey)
        => EmailDisabled();

    private static L12AuthOperationResult EmailDisabled()
        => new(false, "email_feature_disabled", EmailFeatureDisabledMessage);

    public L12AdminPasswordResetView AdminResetPassword(L12AccountView actor, string accountId, string reason,
        L12AdminAuditContext context, bool apply)
    {
        EnsureAccountAdministrationAllowed(actor, accountId, reason, "reset");
        string[] revokedIds = [];
        L12AdminPasswordResetView result;
        lock (_gate)
        {
            var account = _data.Accounts.First(row => row.Id == accountId);
            if (!apply)
            {
                if (context.DryRun)
                {
                    AddAdminAudit(actor, "account", "password-admin-reset", account.Username, null,
                        "temporary-password-required", "dry-run", context with { Outcome = "dry-run" });
                    Save(false);
                }
                return new(false, ToView(account) with { MustChangePassword = true }, 0);
            }
            // Return a unique high-entropy one-time credential only in this successful response.
            // Only its salted hash is persisted. The command layer keeps retries idempotent.
            var temporaryPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            SetPassword(account, temporaryPassword);
            account.MustChangePassword = true;
            account.PermissionVersion++;
            revokedIds = RevokeActiveSessions(account.Id, DateTimeOffset.UtcNow);
            ConsumeTokens(account.Id, PasswordResetPurpose, DateTimeOffset.UtcNow);
            AddAdminAudit(actor, "account", "password-admin-reset", account.Username, null,
                "temporary-password-required", reason.Trim(), context with { Reason = reason.Trim() });
            Save();
            result = new(true, ToView(account), revokedIds.Length, temporaryPassword);
        }
        NotifySessionsRevoked(revokedIds);
        return result;
    }

    public L12AccountDeletionView DeleteAccountPersonalData(L12AccountView actor, string accountId, string reason,
        L12AdminAuditContext context, bool apply)
    {
        EnsureAccountAdministrationAllowed(actor, accountId, reason, "delete");
        string[] revokedIds = [];
        L12AccountDeletionView result;
        lock (_gate)
        {
            var account = _data.Accounts.First(row => row.Id == accountId);
            if (account.Deleted) return new(false, ToView(account), 0, 0);
            if (string.Equals(account.Role, "admin", StringComparison.OrdinalIgnoreCase)
                && _data.Accounts.Count(item => !item.Deleted && !item.Disabled
                    && string.Equals(item.Role, "admin", StringComparison.OrdinalIgnoreCase)) <= 1)
                throw new L12SecurityPolicyException("last_admin_protected", "不能删除最后一个可用管理员账号");
            var privateRecords = _data.Decks.Count(row => row.AccountId == account.Id)
                + _data.PublishedDecks.Count(row => row.OwnerId == account.Id)
                + _data.Friends.Count(row => row.RequesterId == account.Id || row.AddresseeId == account.Id)
                + _data.BlockedAccounts.Count(row => row.AccountId == account.Id || row.BlockedAccountId == account.Id);
            if (!apply)
            {
                if (context.DryRun)
                {
                    AddAdminAudit(actor, "account", "logical-delete", account.Id, null,
                        $"private-records:{privateRecords}", "dry-run", context with { Outcome = "dry-run" });
                    Save(false);
                }
                return new(false, ToView(account) with { Deleted = true, Disabled = true }, 0, privateRecords);
            }
            var now = DateTimeOffset.UtcNow;
            var oldUsername = account.Username;
            revokedIds = RevokeActiveSessions(account.Id, now);
            _data.Decks.RemoveAll(row => row.AccountId == account.Id);
            _data.PublishedDecks.RemoveAll(row => row.OwnerId == account.Id);
            foreach (var deck in _data.PublishedDecks) deck.LikedByAccountIds.RemoveAll(id => id == account.Id);
            _data.Friends.RemoveAll(row => row.RequesterId == account.Id || row.AddresseeId == account.Id);
            _data.BlockedAccounts.RemoveAll(row => row.AccountId == account.Id || row.BlockedAccountId == account.Id);
            foreach (var bug in _data.BugReports.Where(row => row.ReporterId == account.Id))
            {
                bug.ReporterId = null;
                bug.ReporterName = "已注销玩家";
            }
            foreach (var audit in _data.BugReports.SelectMany(row => row.History)
                         .Where(row => row.ActorId == account.Id))
            {
                audit.ActorId = null;
                audit.ActorName = "已注销玩家";
            }
            foreach (var entry in _data.ContentEntries)
            {
                if (string.Equals(entry.UpdatedBy, oldUsername, StringComparison.Ordinal))
                    entry.UpdatedBy = "已注销管理员";
                if (string.Equals(entry.PublishedBy, oldUsername, StringComparison.Ordinal))
                    entry.PublishedBy = "已注销管理员";
            }
            foreach (var review in _data.EffectReviews.Where(row =>
                         string.Equals(row.Reviewer, oldUsername, StringComparison.Ordinal)))
                review.Reviewer = "已注销管理员";
            foreach (var presentation in _data.EffectPresentationOverrides.Where(row =>
                         string.Equals(row.UpdatedBy, oldUsername, StringComparison.Ordinal)))
                presentation.UpdatedBy = "已注销管理员";
            foreach (var workbench in _data.EffectWorkbenchDrafts)
            {
                if (string.Equals(workbench.UpdatedBy, oldUsername, StringComparison.Ordinal))
                    workbench.UpdatedBy = "已注销管理员";
                if (string.Equals(workbench.ReviewedBy, oldUsername, StringComparison.Ordinal))
                    workbench.ReviewedBy = "已注销管理员";
            }
            foreach (var version in _data.EffectWorkbenchVersions.Where(row =>
                         string.Equals(row.PublishedBy, oldUsername, StringComparison.Ordinal)))
                version.PublishedBy = "已注销管理员";
            foreach (var ruling in _data.Tournaments.SelectMany(t => t.Rounds).SelectMany(r => r.Matches)
                         .SelectMany(m => m.Rulings).Where(r => r.ActorId == account.Id))
                ruling.ActorName = "已注销管理员";
            foreach (var tournament in _data.Tournaments)
            {
                if (tournament.OrganizerAccountId == account.Id) tournament.OrganizerAccountId = actor.Id;
                tournament.RefereeAccountIds.RemoveAll(id => id == account.Id);
            }
            ConsumeTokens(account.Id, null, now);
            account.Email = null;
            account.NormalizedEmail = null;
            account.EmailVerifiedAt = null;
            // 墓碑名使用完整且平台内唯一的账号 ID。不能截断后再依赖概率避碰，
            // 否则恶意预注册同名账号会让快照唯一性校验在删除落盘时失败。
            account.Username = DeletedAccountName(account.Id);
            SetPassword(account, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            account.MustChangePassword = false;
            account.Disabled = true;
            account.DisabledAt = now;
            account.DisabledByAccountId = actor.Id;
            account.DisabledReason = "account-deleted";
            account.Deleted = true;
            account.DeletedAt = now;
            account.DeletedByAccountId = actor.Id;
            account.DeletedReason = reason.Trim();
            account.PermissionVersion++;
            AddAdminAudit(actor, "account", "logical-delete", account.Id, FingerprintValue(oldUsername),
                account.Username, reason.Trim(), context with { Reason = reason.Trim() });
            _pendingChatAccountDeletions.Add(account.Id);
            Save();
            result = new(true, ToView(account), revokedIds.Length, privateRecords);
        }
        NotifySessionsRevoked(revokedIds);
        return result;
    }

    private void EnsureAccountAdministrationAllowed(L12AccountView actor, string accountId, string reason,
        string action)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.AdminAccountStatusWrite))
            throw new L12SecurityPolicyException("permission_denied", "没有账号安全管理权限");
        if (string.IsNullOrWhiteSpace(reason))
            throw new L12SecurityPolicyException("security_reason_required", "必须填写操作理由");
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId)
                ?? throw new KeyNotFoundException("账号不存在");
            if (string.Equals(account.Username, "Admin", StringComparison.Ordinal))
                throw new L12SecurityPolicyException("root_admin_protected", $"根 Admin 账号不能执行{action}操作");
            if (account.Id == actor.Id)
                throw new L12SecurityPolicyException("self_account_change_forbidden", "不能通过后台处置自己的账号");
            if (account.Deleted)
                throw new L12SecurityPolicyException("account_deleted", "账号已逻辑删除");
        }
    }

    private void ConsumeTokens(string accountId, string? purpose, DateTimeOffset now)
    {
        foreach (var token in _data.EmailAuthTokens.Where(row => row.AccountId == accountId
                     && (purpose is null || row.Purpose == purpose) && row.ConsumedAt is null))
            token.ConsumedAt = now;
    }

    private string[] RevokeActiveSessions(string accountId, DateTimeOffset now)
    {
        var sessions = _data.Sessions.Where(row => row.AccountId == accountId && row.RevokedAt is null
            && row.ExpiresAt > now).ToArray();
        foreach (var session in sessions) session.RevokedAt = now;
        return sessions.Select(row => row.Id).ToArray();
    }

    private static string? NormalizeEmail(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 254) return null;
        try
        {
            var parsed = new MailAddress(candidate);
            return parsed.Address.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                ? parsed.Address.ToLowerInvariant() : null;
        }
        catch { return null; }
    }

    private static string? MaskEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var at = value.IndexOf('@');
        if (at <= 0) return "***";
        var local = value[..at];
        var visible = local[..Math.Min(2, local.Length)];
        return $"{visible}***{value[at..]}";
    }

    private static string? FingerprintValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : HashToken(value)[..16];

    internal static string DeletedAccountName(string accountId) => $"deleted-{accountId}";

}
