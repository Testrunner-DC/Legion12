namespace TwelveLegions.Server;

public sealed record L12UsernameChangeRequestView(string Id, string AccountId, string CurrentUsername,
    string RequestedUsername, string Reason, string Status, string? ReviewedByUsername, string? ReviewNote,
    DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record L12UsernameChangeStatusView(bool FreeRenameAvailable, int FreeRenameUsed,
    L12UsernameChangeRequestView? LatestRequest);

public sealed partial class L12PlatformStore
{
    private void EnsureUsernameChangeState()
    {
        lock (_gate)
        {
            _data.UsernameChangeRequests ??= [];
            foreach (var request in _data.UsernameChangeRequests)
                request.Status = request.Status is "pending" or "approved" or "rejected" ? request.Status : "rejected";
        }
    }

    public L12UsernameChangeStatusView UsernameChangeStatus(string accountId)
    {
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在");
            var latest = _data.UsernameChangeRequests.Where(row => row.AccountId == accountId)
                .OrderByDescending(row => row.CreatedAt).FirstOrDefault();
            return new(account.Role == "player" && account.SelfServiceUsernameChangeCount == 0 && !account.MustChangeUsername,
                account.SelfServiceUsernameChangeCount, latest is null ? null : ToUsernameChangeRequestView(latest));
        }
    }

    public (bool Success, string Message, L12AccountView? Account) SelfServiceChangeUsername(string accountId,
        string currentPassword, string newUsername, string? currentSessionId = null)
    {
        newUsername = newUsername.Trim();
        var validation = L12UsernamePolicy.Validate(newUsername);
        if (validation is not null) return (false, validation, null);
        string[] revokedSessionIds;
        L12AccountView changed;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId && !item.Disabled && !item.Deleted);
            if (row is null || !Verify(currentPassword, row)) return (false, "当前密码不正确", null);
            if (row.Role != "player") return (false, "管理员账号不支持自助改名", null);
            if (row.MustChangeUsername) return (false, "当前账号须先完成强制用户名修改", null);
            if (row.SelfServiceUsernameChangeCount > 0) return (false, "免费改名机会已使用，请提交管理员审批申请", null);
            if (string.Equals(row.Username, newUsername, StringComparison.OrdinalIgnoreCase)) return (false, "新用户名与当前用户名相同", null);
            if (_data.Accounts.Any(item => item.Id != accountId && !item.Deleted
                    && string.Equals(item.Username, newUsername, StringComparison.OrdinalIgnoreCase)))
                return (false, "用户名已存在", null);
            var previous = PublicUsername(row);
            row.Username = newUsername;
            row.SelfServiceUsernameChangeCount++;
            changed = ToView(row);
            AddAdminAudit(changed, "account", "username-self-change", row.Id, previous, newUsername,
                "玩家使用一次自助改名机会");
            var now = DateTimeOffset.UtcNow;
            var sessions = _data.Sessions.Where(session => session.AccountId == accountId
                && (string.IsNullOrWhiteSpace(currentSessionId) || session.Id != currentSessionId)
                && session.RevokedAt is null && session.ExpiresAt > now).ToArray();
            foreach (var session in sessions) session.RevokedAt = now;
            revokedSessionIds = sessions.Select(session => session.Id).ToArray();
            Save();
        }
        NotifySessionsRevoked(revokedSessionIds);
        return (true, revokedSessionIds.Length == 0 ? "用户名已修改，已使用一次自助改名机会"
            : $"用户名已修改，已使用一次自助改名机会；已撤销其他 {revokedSessionIds.Length} 个会话", changed);
    }

    public L12UsernameChangeRequestView SubmitUsernameChangeRequest(string accountId, string requestedUsername, string reason)
    {
        requestedUsername = requestedUsername.Trim();
        reason = reason.Trim();
        var validation = L12UsernamePolicy.Validate(requestedUsername);
        if (validation is not null) throw new ArgumentException(validation);
        if (reason.Length is < 4 or > 400) throw new ArgumentException("改名原因须为 4–400 个字符");
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Disabled && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在");
            if (account.Role != "player") throw new InvalidOperationException("管理员账号不支持提交改名申请");
            if (account.SelfServiceUsernameChangeCount == 0) throw new InvalidOperationException("你仍有一次自助改名机会，无需提交申请");
            if (string.Equals(account.Username, requestedUsername, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("新用户名与当前用户名相同");
            if (_data.UsernameChangeRequests.Any(row => row.AccountId == accountId && row.Status == "pending"))
                throw new InvalidOperationException("已有待审核的改名申请，请等待管理员处理");
            if (_data.Accounts.Any(row => row.Id != accountId && !row.Deleted
                    && string.Equals(row.Username, requestedUsername, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("用户名已存在");
            var request = new UsernameChangeRequestRow { AccountId = accountId, CurrentUsername = PublicUsername(account),
                RequestedUsername = requestedUsername, Reason = reason };
            _data.UsernameChangeRequests.Add(request);
            AddAdminAudit(ToView(account), "username-change-request", "submit", request.Id, null,
                requestedUsername, reason);
            Save();
            return ToUsernameChangeRequestView(request);
        }
    }

    public IReadOnlyList<L12UsernameChangeRequestView> UsernameChangeRequests(string? status = null)
    {
        status = status?.Trim().ToLowerInvariant();
        lock (_gate) return _data.UsernameChangeRequests.Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status)
            .OrderBy(row => row.Status == "pending" ? 0 : 1).ThenByDescending(row => row.CreatedAt)
            .Select(ToUsernameChangeRequestView).ToArray();
    }

    public L12UsernameChangeRequestView ReviewUsernameChangeRequest(L12AccountView actor, string requestId,
        bool approve, string? note, L12AdminAuditContext? context = null)
    {
        note = note?.Trim();
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new ArgumentException("驳回申请时必须填写处理说明");
        if ((note?.Length ?? 0) > 400) throw new ArgumentException("处理说明不能超过 400 个字符");
        lock (_gate)
        {
            var request = _data.UsernameChangeRequests.FirstOrDefault(row => row.Id == requestId)
                ?? throw new KeyNotFoundException("改名申请不存在");
            if (request.Status != "pending") throw new InvalidOperationException("该改名申请已处理");
            var account = _data.Accounts.FirstOrDefault(row => row.Id == request.AccountId && !row.Deleted)
                ?? throw new InvalidOperationException("申请账号已不存在");
            if (approve)
            {
                var validation = L12UsernamePolicy.Validate(request.RequestedUsername);
                if (validation is not null) throw new ArgumentException(validation);
                if (_data.Accounts.Any(row => row.Id != account.Id && !row.Deleted
                        && string.Equals(row.Username, request.RequestedUsername, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("该用户名已被占用，请驳回或联系玩家另行申请");
                var previous = PublicUsername(account);
                account.Username = request.RequestedUsername;
                AddAdminAudit(actor, "account", "username-request-approved", account.Id, previous,
                    request.RequestedUsername, note, context);
            }
            request.Status = approve ? "approved" : "rejected";
            request.ReviewedByAccountId = actor.Id;
            request.ReviewNote = note;
            request.ReviewedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "username-change-request", approve ? "approve" : "reject", request.Id,
                request.CurrentUsername, request.RequestedUsername, note, context);
            Save();
            return ToUsernameChangeRequestView(request);
        }
    }

    private L12UsernameChangeRequestView ToUsernameChangeRequestView(UsernameChangeRequestRow row)
        => new(row.Id, row.AccountId, row.CurrentUsername, row.RequestedUsername, row.Reason, row.Status,
            row.ReviewedByAccountId is null ? null : _data.Accounts.FirstOrDefault(account => account.Id == row.ReviewedByAccountId)?.Username,
            row.ReviewNote, row.CreatedAt, row.ReviewedAt);
}
