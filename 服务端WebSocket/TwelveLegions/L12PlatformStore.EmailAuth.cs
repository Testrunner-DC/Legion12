using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace TwelveLegions.Server;

public sealed record L12EmailMessage(string To, string Subject, string TextBody, string Purpose);
public sealed record L12EmailSendResult(bool Success, string Code, string Message);

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
        => new(false, "mail_unavailable", "邮件服务未配置");
}

public sealed class L12SmtpEmailSender : IL12EmailSender
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly string _fromAddress;
    private readonly string _fromName;
    private readonly bool _enableSsl;

    private L12SmtpEmailSender(string host, int port, string username, string password, string fromAddress,
        string fromName, bool enableSsl, string publicBaseUrl)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _fromAddress = fromAddress;
        _fromName = fromName;
        _enableSsl = enableSsl;
        PublicBaseUrl = publicBaseUrl.TrimEnd('/');
    }

    public bool IsConfigured => true;
    public string PublicBaseUrl { get; }

    public static IL12EmailSender FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("L12_SMTP_HOST")?.Trim() ?? string.Empty;
        var from = Environment.GetEnvironmentVariable("L12_SMTP_FROM_ADDRESS")?.Trim() ?? string.Empty;
        var baseUrl = Environment.GetEnvironmentVariable("L12_PUBLIC_BASE_URL")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host) || !IsEmail(from)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            return new L12UnavailableEmailSender();
        var port = int.TryParse(Environment.GetEnvironmentVariable("L12_SMTP_PORT"), out var parsedPort)
            && parsedPort is > 0 and <= 65535 ? parsedPort : 587;
        var enableSsl = !bool.TryParse(Environment.GetEnvironmentVariable("L12_SMTP_ENABLE_SSL"), out var parsedSsl)
            || parsedSsl;
        return new L12SmtpEmailSender(host, port,
            Environment.GetEnvironmentVariable("L12_SMTP_USERNAME") ?? string.Empty,
            Environment.GetEnvironmentVariable("L12_SMTP_PASSWORD") ?? string.Empty,
            from, Environment.GetEnvironmentVariable("L12_SMTP_FROM_NAME")?.Trim() ?? "十二军团",
            enableSsl, baseUrl);
    }

    public L12EmailSendResult Send(L12EmailMessage message)
    {
        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(_fromAddress, _fromName, Encoding.UTF8),
                Subject = message.Subject,
                Body = message.TextBody,
                IsBodyHtml = false,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
            };
            mail.To.Add(new MailAddress(message.To));
            using var client = new SmtpClient(_host, _port) { EnableSsl = _enableSsl, Timeout = 15_000 };
            if (!string.IsNullOrWhiteSpace(_username))
                client.Credentials = new NetworkCredential(_username, _password);
            client.Send(mail);
            return new(true, "sent", "邮件已发送");
        }
        catch
        {
            return new(false, "mail_delivery_failed", "邮件发送失败");
        }
    }

    private static bool IsEmail(string value)
    {
        try { return new MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

public sealed record L12EmailStatusView(bool Bound, bool Verified, string? MaskedEmail,
    string? PendingMaskedEmail, DateTimeOffset? PendingExpiresAt, bool MailConfigured);
public sealed record L12AuthOperationResult(bool Success, string Code, string Message,
    int RetryAfterSeconds = 0);
public sealed record L12AdminPasswordResetView(bool Applied, L12AccountView Account,
    int RevokedSessions);
public sealed record L12AccountDeletionView(bool Applied, L12AccountView Account,
    int RevokedSessions, int RemovedPrivateRecords, int CleanedMatchRecords = 0);

public sealed partial class L12PlatformStore
{
    private const string EmailVerifyPurpose = "email-verify";
    private const string PasswordResetPurpose = "password-reset";
    private static readonly TimeSpan EmailVerifyLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AuthActionWindow = TimeSpan.FromMinutes(15);
    private const string PasswordResetGenericMessage = "如果该邮箱已绑定并完成验证，我们会发送密码重置邮件";

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

    public L12EmailStatusView EmailStatus(string accountId)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在");
            var pending = _data.EmailAuthTokens.Where(row => row.AccountId == accountId
                    && row.Purpose == EmailVerifyPurpose && row.ConsumedAt is null && row.ExpiresAt > now
                    && row.DeliveredAt is not null)
                .OrderByDescending(row => row.CreatedAt).FirstOrDefault();
            return new(account.EmailVerifiedAt is not null, account.EmailVerifiedAt is not null,
                MaskEmail(account.Email), MaskEmail(pending?.TargetEmail), pending?.ExpiresAt,
                _emailSender.IsConfigured);
        }
    }

    public L12AuthOperationResult RequestEmailBinding(string accountId, string currentPassword, string email,
        string clientKey)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
            return new(false, "invalid_email", "请输入有效邮箱地址");
        if (!_emailSender.IsConfigured)
            return new(false, "mail_unavailable", "邮件服务未配置，暂时不能绑定邮箱");

        string rawToken;
        EmailAuthTokenRow issued;
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted);
            if (account is null) return new(false, "account_not_found", "账号不存在");
            if (!Verify(currentPassword, account))
                return new(false, "invalid_current_password", "当前密码不正确");
            if (_data.Accounts.Any(row => row.Id != accountId && !row.Deleted
                    && row.EmailVerifiedAt is not null && row.NormalizedEmail == normalized)
                || _data.EmailAuthTokens.Any(row => row.AccountId != accountId
                    && row.Purpose == EmailVerifyPurpose && row.TargetEmail == normalized
                    && row.ConsumedAt is null && row.ExpiresAt > DateTimeOffset.UtcNow))
                return new(false, "email_in_use", "该邮箱已被其他账号使用");
            if (!TryConsumeAuthAction("email-bind", accountId, 3, out var retryAfter))
                return new(false, "email_rate_limited", "请求过于频繁，请稍后重试", retryAfter);
            (issued, rawToken) = IssueEmailToken(account.Id, EmailVerifyPurpose, normalized, EmailVerifyLifetime);
            Save(false);
        }

        var sent = SendTokenMessage(issued, rawToken);
        FinalizeDelivery(issued.Id, sent.Success);
        return sent.Success
            ? new(true, "verification_sent", "验证邮件已发送；验证完成前保留原邮箱")
            : new(false, sent.Code, "验证邮件发送失败，请稍后重试");
    }

    public L12AuthOperationResult VerifyEmail(string token, string clientKey)
    {
        lock (_gate)
        {
            if (!TryConsumeAuthAction("email-verify", clientKey, 10, out var retryAfter))
                return new(false, "email_rate_limited", "请求过于频繁，请稍后重试", retryAfter);
            var now = DateTimeOffset.UtcNow;
            var row = FindUsableToken(token, EmailVerifyPurpose, now);
            if (row is null)
            {
                Save(false);
                return new(false, "invalid_or_expired_token", "验证链接无效、已使用或已过期");
            }
            var account = _data.Accounts.FirstOrDefault(item => item.Id == row.AccountId
                && !item.Deleted && !item.Disabled);
            if (account is null || _data.Accounts.Any(item => item.Id != account.Id && !item.Deleted
                    && item.EmailVerifiedAt is not null && item.NormalizedEmail == row.TargetEmail))
            {
                row.ConsumedAt = now;
                Save(false);
                return new(false, "email_in_use", "该邮箱已被其他账号使用");
            }
            account.Email = row.TargetEmail;
            account.NormalizedEmail = row.TargetEmail;
            account.EmailVerifiedAt = now;
            ConsumeTokens(account.Id, EmailVerifyPurpose, now);
            ConsumeTokens(account.Id, PasswordResetPurpose, now);
            AddAdminAudit(ToView(account), "account", "email-verified", account.Username, null,
                FingerprintValue(row.TargetEmail), "self-service", new L12AdminAuditContext("email-verify"));
            Save();
            return new(true, "email_verified", "邮箱验证成功");
        }
    }

    public L12AuthOperationResult UnbindEmail(string accountId, string currentPassword)
    {
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted);
            if (account is null) return new(false, "account_not_found", "账号不存在");
            if (!Verify(currentPassword, account))
                return new(false, "invalid_current_password", "当前密码不正确");
            var previous = account.NormalizedEmail;
            account.Email = null;
            account.NormalizedEmail = null;
            account.EmailVerifiedAt = null;
            ConsumeTokens(account.Id, null, DateTimeOffset.UtcNow);
            AddAdminAudit(ToView(account), "account", "email-unbound", account.Username,
                FingerprintValue(previous), null, "self-service", new L12AdminAuditContext("email-unbind"));
            Save();
            return new(true, "email_unbound", "邮箱已解绑；忘记密码功能将不可用");
        }
    }

    public L12AuthOperationResult RequestPasswordReset(string email, string clientKey)
    {
        var normalized = NormalizeEmail(email);
        EmailAuthTokenRow? issued = null;
        string? rawToken = null;
        lock (_gate)
        {
            var emailSubject = normalized ?? email.Trim().ToLowerInvariant();
            var emailAllowed = TryConsumeAuthAction("password-reset-email", emailSubject, 3,
                out var emailRetryAfter);
            var clientAllowed = TryConsumeAuthAction("password-reset-client", clientKey, 10,
                out var clientRetryAfter);
            if (!emailAllowed || !clientAllowed)
            {
                Save(false);
                return new(false, "password_reset_rate_limited", PasswordResetGenericMessage,
                    Math.Max(emailRetryAfter, clientRetryAfter));
            }
            if (normalized is not null && _emailSender.IsConfigured)
            {
                var account = _data.Accounts.FirstOrDefault(row => !row.Deleted && !row.Disabled
                    && row.EmailVerifiedAt is not null && row.NormalizedEmail == normalized);
                if (account is not null)
                    (issued, rawToken) = IssueEmailToken(account.Id, PasswordResetPurpose, normalized,
                        PasswordResetLifetime);
            }
            Save(false);
        }
        if (issued is not null && rawToken is not null)
            QueueTokenMessage(issued, rawToken);
        return new(true, "password_reset_accepted", PasswordResetGenericMessage);
    }

    public L12AuthOperationResult ResetPassword(string token, string newPassword, string clientKey)
    {
        if (newPassword.Length is < 8 or > 128)
            return new(false, "invalid_password", "新密码长度需为 8–128 个字符");
        string[] revokedIds;
        lock (_gate)
        {
            if (!TryConsumeAuthAction("password-reset-consume", clientKey, 10, out var retryAfter))
                return new(false, "password_reset_rate_limited", "请求过于频繁，请稍后重试", retryAfter);
            var now = DateTimeOffset.UtcNow;
            var row = FindUsableToken(token, PasswordResetPurpose, now);
            var account = row is null ? null : _data.Accounts.FirstOrDefault(item => item.Id == row.AccountId
                && !item.Deleted && !item.Disabled && item.EmailVerifiedAt is not null
                && item.NormalizedEmail == row.TargetEmail);
            if (row is null || account is null)
            {
                Save(false);
                return new(false, "invalid_or_expired_token", "重置链接无效、已使用或已过期");
            }
            SetPassword(account, newPassword);
            account.MustChangePassword = false;
            ConsumeTokens(account.Id, PasswordResetPurpose, now);
            revokedIds = RevokeActiveSessions(account.Id, now);
            AddAdminAudit(ToView(account), "account", "password-reset", account.Username, null, null,
                "verified-email", new L12AdminAuditContext("password-reset"));
            Save();
        }
        NotifySessionsRevoked(revokedIds);
        return new(true, "password_reset", "密码已重置，所有设备需要重新登录");
    }

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
            SetPassword(account, "123456");
            account.MustChangePassword = true;
            account.PermissionVersion++;
            revokedIds = RevokeActiveSessions(account.Id, DateTimeOffset.UtcNow);
            ConsumeTokens(account.Id, PasswordResetPurpose, DateTimeOffset.UtcNow);
            AddAdminAudit(actor, "account", "password-admin-reset", account.Username, null,
                "temporary-password-required", reason.Trim(), context with { Reason = reason.Trim() });
            Save();
            result = new(true, ToView(account), revokedIds.Length);
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
                + _data.Friends.Count(row => row.RequesterId == account.Id || row.AddresseeId == account.Id);
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

    private (EmailAuthTokenRow Row, string RawToken) IssueEmailToken(string accountId, string purpose,
        string targetEmail, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        ConsumeTokens(accountId, purpose, now);
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var row = new EmailAuthTokenRow
        {
            AccountId = accountId,
            Purpose = purpose,
            TargetEmail = targetEmail,
            TokenHash = HashToken(raw),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
        _data.EmailAuthTokens.Add(row);
        PruneEmailAuthState(now);
        return (row, raw);
    }

    private EmailAuthTokenRow? FindUsableToken(string token, string purpose, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return null;
        var hash = HashToken(token.Trim());
        return _data.EmailAuthTokens.FirstOrDefault(row => row.Purpose == purpose
            && TokenHashesEqual(row.TokenHash, hash)
            && row.DeliveredAt is not null && row.ConsumedAt is null && row.ExpiresAt > now);
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

    private L12EmailSendResult SendTokenMessage(EmailAuthTokenRow row, string rawToken)
    {
        var mode = row.Purpose == EmailVerifyPurpose ? "verify-email" : "reset-password";
        var link = $"{_emailSender.PublicBaseUrl}/auth/recovery?mode={mode}#token={Uri.EscapeDataString(rawToken)}";
        var subject = row.Purpose == EmailVerifyPurpose ? "验证十二军团账号邮箱" : "重置十二军团账号密码";
        var lifetime = row.Purpose == EmailVerifyPurpose ? "24 小时" : "30 分钟";
        var body = $"请在 {lifetime}内打开以下链接完成操作：\n{link}\n\n如果不是你发起的请求，请忽略此邮件。";
        return _emailSender.Send(new L12EmailMessage(row.TargetEmail, subject, body, row.Purpose));
    }

    private void QueueTokenMessage(EmailAuthTokenRow row, string rawToken)
    {
        _ = Task.Run(() =>
        {
            var delivered = false;
            try { delivered = SendTokenMessage(row, rawToken).Success; }
            catch { /* 统一按未送达处理，避免泄露邮箱存在性。 */ }
            try { FinalizeDelivery(row.Id, delivered); }
            catch { /* 后台投递不得产生未观察任务异常。 */ }
        });
    }

    private void FinalizeDelivery(string tokenId, bool delivered)
    {
        lock (_gate)
        {
            var row = _data.EmailAuthTokens.FirstOrDefault(item => item.Id == tokenId);
            if (row is null) return;
            if (delivered) row.DeliveredAt = DateTimeOffset.UtcNow;
            else row.ConsumedAt = DateTimeOffset.UtcNow;
            Save(false);
        }
    }

    private bool TryConsumeAuthAction(string action, string subject, int limit, out int retryAfterSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        PruneEmailAuthState(now);
        var key = HashToken($"{action}|{subject.Trim().ToLowerInvariant()}");
        var row = _data.AuthActionThrottles.FirstOrDefault(item => item.Key == key);
        if (row is null)
        {
            _data.AuthActionThrottles.Add(new AuthActionThrottleRow
                { Key = key, Count = 1, WindowStartedAt = now });
            retryAfterSeconds = 0;
            return true;
        }
        if (now - row.WindowStartedAt >= AuthActionWindow)
        {
            row.WindowStartedAt = now;
            row.Count = 1;
            retryAfterSeconds = 0;
            return true;
        }
        if (row.Count >= limit)
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(
                (row.WindowStartedAt + AuthActionWindow - now).TotalSeconds));
            return false;
        }
        row.Count++;
        retryAfterSeconds = 0;
        return true;
    }

    private void PruneEmailAuthState(DateTimeOffset now)
    {
        _data.EmailAuthTokens.RemoveAll(row => row.ExpiresAt < now.AddDays(-7));
        _data.AuthActionThrottles.RemoveAll(row => row.WindowStartedAt < now.AddHours(-1));
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

    private static bool TokenHashesEqual(string first, string second)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
}
