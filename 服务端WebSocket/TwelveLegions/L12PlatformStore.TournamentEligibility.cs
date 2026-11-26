namespace TwelveLegions.Server;

// 赛事资质（用户 2026-11-26）：普通账号需先申请并由管理员审核，获得资质才能创建赛事；
// 管理员自带该资质。授予入口有三处（自助申请 / 后台审核 / 账号权限页直接授予），
// 共用同一条记录与同一条审计链，判定只在 TournamentEligibilityAllows 一处。
public sealed record L12TournamentEligibilityView(
    string AccountId,
    string Username,
    string Status,
    string? Reason,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedByAccountId,
    string? ReviewedByUsername,
    string? ReviewNote,
    bool Effective);

public sealed record L12TournamentEligibilityApplyPayload(string Reason);
public sealed record L12TournamentEligibilityReviewPayload(bool Approve, string? Note);
public sealed record L12TournamentEligibilityGrantPayload(string? Note);
public sealed record L12TournamentEligibilityRevokePayload(string Reason);

public sealed partial class L12PlatformStore
{
    private const string TournamentEligibilityCategory = "tournament-eligibility";

    /// <summary>判定唯一入口：管理员角色自带资质，其余账号要求已批准。</summary>
    public static bool TournamentEligibilityAllows(string? role, string? status)
        => string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase);

    // 测试接缝：既有平台测试大量直接以 player 账号建赛（历史上不需要资质）。
    // 测试程序集在模块初始化里关掉这道门，资质用例按需打开；产品默认开启。
    internal static bool EligibilityGateEnabled { get; set; } = true;

    public bool HasTournamentEligibility(L12AccountView actor)
    {
        if (!EligibilityGateEnabled) return true;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == actor.Id);
            return row is null || TournamentEligibilityAllows(row.Role, row.TournamentEligibility?.Status);
        }
    }

    public L12TournamentEligibilityView ReadTournamentEligibility(string accountId)
    {
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new KeyNotFoundException("账号不存在");
            return ToTournamentEligibilityView(row);
        }
    }

    public IReadOnlyList<L12TournamentEligibilityView> PendingTournamentEligibility()
    {
        lock (_gate)
        {
            return _data.Accounts
                .Where(item => !item.Deleted && item.TournamentEligibility?.Status == "pending")
                .OrderBy(item => item.TournamentEligibility!.AppliedAt ?? DateTimeOffset.MinValue)
                .Select(ToTournamentEligibilityView)
                .ToArray();
        }
    }

    public L12TournamentEligibilityView ApplyTournamentEligibility(L12AccountView actor,
        L12TournamentEligibilityApplyPayload payload, L12AdminAuditContext context)
    {
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == actor.Id)
                ?? throw new KeyNotFoundException("账号不存在");
            var reason = RequireText(payload.Reason, "申请原因", 500);
            if (reason.Length < 10) throw new ArgumentException("申请原因至少 10 个字");
            var state = row.TournamentEligibility ??= new TournamentEligibilityRow();
            if (state.Status == "approved") throw new L12TournamentScopeException("账号已具备赛事资质");
            if (state.Status == "pending") throw new L12TournamentVersionConflictException("已有待审核的赛事资质申请");
            state.Status = "pending";
            state.Reason = reason;
            state.AppliedAt = DateTimeOffset.UtcNow;
            state.ReviewedAt = null;
            state.ReviewedByAccountId = null;
            state.ReviewNote = null;
            row.PermissionVersion++;
            AddAdminAudit(actor, TournamentEligibilityCategory, "apply", row.Id, null, "pending", reason, context);
            Save();
            return ToTournamentEligibilityView(row);
        }
    }

    public L12TournamentEligibilityView ReviewTournamentEligibility(L12AccountView actor, string accountId,
        L12TournamentEligibilityReviewPayload payload, long? expectedVersion, L12AdminAuditContext context)
    {
        lock (_gate)
        {
            RequireTournamentEligibilityStaff(actor);
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new KeyNotFoundException("账号不存在");
            var state = row.TournamentEligibility
                ?? throw new L12TournamentVersionConflictException("该账号没有待审核的赛事资质申请");
            if (state.Status != "pending")
                throw new L12TournamentVersionConflictException("该账号没有待审核的赛事资质申请");
            if (expectedVersion is { } version && version != row.PermissionVersion)
                throw new L12TournamentVersionConflictException("账号状态已变化，请重新读取后再审核");
            var before = state.Status;
            state.Status = payload.Approve ? "approved" : "rejected";
            state.ReviewedAt = DateTimeOffset.UtcNow;
            state.ReviewedByAccountId = actor.Id;
            state.ReviewNote = OptionalText(payload.Note, 500);
            row.PermissionVersion++;
            AddAdminAudit(actor, TournamentEligibilityCategory, "review", row.Id, before, state.Status,
                state.ReviewNote, context);
            Save();
            return ToTournamentEligibilityView(row);
        }
    }

    // 管理员在「账号、权限与会话」直接授予，不需要走申请。
    public L12TournamentEligibilityView GrantTournamentEligibility(L12AccountView actor, string accountId,
        L12TournamentEligibilityGrantPayload payload, long? expectedVersion, L12AdminAuditContext context)
    {
        lock (_gate)
        {
            RequireTournamentEligibilityStaff(actor);
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new KeyNotFoundException("账号不存在");
            if (expectedVersion is { } version && version != row.PermissionVersion)
                throw new L12TournamentVersionConflictException("账号状态已变化，请重新读取后再授予");
            var state = row.TournamentEligibility ??= new TournamentEligibilityRow();
            var before = state.Status;
            state.Status = "approved";
            state.ReviewedAt = DateTimeOffset.UtcNow;
            state.ReviewedByAccountId = actor.Id;
            state.ReviewNote = OptionalText(payload.Note, 500);
            row.PermissionVersion++;
            AddAdminAudit(actor, TournamentEligibilityCategory, "grant", row.Id, before, "approved",
                state.ReviewNote, context);
            Save();
            return ToTournamentEligibilityView(row);
        }
    }

    public L12TournamentEligibilityView RevokeTournamentEligibility(L12AccountView actor, string accountId,
        L12TournamentEligibilityRevokePayload payload, long? expectedVersion, L12AdminAuditContext context)
    {
        lock (_gate)
        {
            RequireTournamentEligibilityStaff(actor);
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId)
                ?? throw new KeyNotFoundException("账号不存在");
            var state = row.TournamentEligibility
                ?? throw new L12TournamentVersionConflictException("该账号当前没有赛事资质");
            if (state.Status != "approved")
                throw new L12TournamentVersionConflictException("该账号当前没有可撤销的赛事资质");
            if (expectedVersion is { } version && version != row.PermissionVersion)
                throw new L12TournamentVersionConflictException("账号状态已变化，请重新读取后再撤销");
            var reason = RequireText(payload.Reason, "撤销理由", 500);
            var before = state.Status;
            state.Status = "none";
            state.ReviewedAt = DateTimeOffset.UtcNow;
            state.ReviewedByAccountId = actor.Id;
            state.ReviewNote = reason;
            row.PermissionVersion++;
            AddAdminAudit(actor, TournamentEligibilityCategory, "revoke", row.Id, before, "none", reason, context);
            Save();
            return ToTournamentEligibilityView(row);
        }
    }

    private static void RequireTournamentEligibilityStaff(L12AccountView actor)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.TournamentsManage))
            throw new L12TournamentScopeException("仅赛事管理员可审核或授予赛事资质");
    }

    private L12TournamentEligibilityView ToTournamentEligibilityView(AccountRow row)
    {
        var state = row.TournamentEligibility;
        var reviewer = state?.ReviewedByAccountId is { } id
            ? _data.Accounts.FirstOrDefault(item => item.Id == id)?.Username
            : null;
        return new L12TournamentEligibilityView(row.Id, row.Username, state?.Status ?? "none", state?.Reason,
            state?.AppliedAt, state?.ReviewedAt, state?.ReviewedByAccountId, reviewer, state?.ReviewNote,
            TournamentEligibilityAllows(row.Role, state?.Status));
    }
}
