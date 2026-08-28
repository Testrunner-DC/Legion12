using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12AccountView(string Id, string Username, string Role, DateTimeOffset CreatedAt,
    bool PublicHistory, int PermissionVersion = 1, bool Disabled = false,
    DateTimeOffset? DisabledAt = null, string? DisabledReason = null)
{
    public IReadOnlyList<string> Permissions => L12Authorization.PermissionsForRole(Role);
}
public sealed record L12FriendView(string AccountId, string Username, string Status, string Direction,
    DateTimeOffset CreatedAt);
public sealed record L12SessionView(string Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Current,
    string AuthStrength, int PermissionVersion);
public sealed record L12AuthenticatedSession(L12AccountView Account, string SessionId);
public sealed record L12SessionRevocationResult(bool Found, string? SessionId, int RevokedCount,
    bool AlreadyRevoked, IReadOnlyList<string> RevokedSessionIds);
public sealed record L12AccountDeckView(string Name, string MasterId, IReadOnlyList<string> CardIds,
    IReadOnlyList<string> MoraleIds, IReadOnlyList<string> SpecialIds, DateTimeOffset UpdatedAt);
public sealed record L12PublishedDeckView(string Id, string OwnerId, string Author, L12AccountDeckView Deck,
    int Likes, int Copies, bool Liked, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12BugReportView(string Id, string? ReporterId, string ReporterName, string Title, string Description,
    string Page, string? RoomCode, string? MatchId, string Version, string Status, string Priority, string? Assignee,
    string? AdminNotes, IReadOnlyList<L12BugAuditView> History, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12BugAuditView(string Id, string? ActorId, string ActorName, string Action,
    string? FromValue, string? ToValue, string? Comment, DateTimeOffset CreatedAt);
public sealed record L12AdminAuditView(string Id, string ActorId, string ActorName, string Category, string Action,
    string Target, string? FromValue, string? ToValue, string? Comment, DateTimeOffset CreatedAt,
    string? CorrelationId = null, string Outcome = "succeeded", string? Permission = null, string? Reason = null,
    string? CommandId = null, string? IdempotencyKey = null, bool DryRun = false, long? ExpectedVersion = null,
    string? RequestMethod = null, string? RequestPath = null);
public sealed record L12ContentEntryView(string Key, string DraftValue, string PublishedValue, string Status,
    string? UpdatedBy, DateTimeOffset? UpdatedAt, string? PublishedBy, DateTimeOffset? PublishedAt,
    long Version = 0, string? PublishedVersionId = null, string? RollbackVersionId = null);
public sealed record L12EffectReviewView(string CardId, string? AbilityId, string Status, string Note,
    string Reviewer, DateTimeOffset UpdatedAt, string StructureHash = "");

public sealed partial class L12PlatformStore
{
    private sealed class AccountRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string Role { get; set; } = "player";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public bool PublicHistory { get; set; } = true;
        public int PermissionVersion { get; set; } = 1;
        public bool Disabled { get; set; }
        public DateTimeOffset? DisabledAt { get; set; }
        public string? DisabledByAccountId { get; set; }
        public string? DisabledReason { get; set; }
    }

    private sealed class BugRow
    {
        public string Id { get; set; } = $"BUG-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..21];
        public string? ReporterId { get; set; }
        public string ReporterName { get; set; } = "匿名玩家";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Page { get; set; } = string.Empty;
        public string? RoomCode { get; set; }
        public string? MatchId { get; set; }
        public string Version { get; set; } = "dev";
        public string Status { get; set; } = "new";
        public string Priority { get; set; } = "normal";
        public string? Assignee { get; set; }
        public string? AdminNotes { get; set; }
        public List<BugAuditRow> History { get; set; } = [];
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class BugAuditRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string? ActorId { get; set; }
        public string ActorName { get; set; } = "系统";
        public string Action { get; set; } = string.Empty;
        public string? FromValue { get; set; }
        public string? ToValue { get; set; }
        public string? Comment { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class AdminAuditRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = "系统";
        public string Category { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string? FromValue { get; set; }
        public string? ToValue { get; set; }
        public string? Comment { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string Outcome { get; set; } = "succeeded";
        public string? Permission { get; set; }
        public string? Reason { get; set; }
        public string? CommandId { get; set; }
        public string? IdempotencyKey { get; set; }
        public bool DryRun { get; set; }
        public long? ExpectedVersion { get; set; }
        public string? RequestMethod { get; set; }
        public string? RequestPath { get; set; }
    }

    private sealed class ContentRow
    {
        public string Key { get; set; } = string.Empty;
        public string DraftValue { get; set; } = string.Empty;
        public string PublishedValue { get; set; } = string.Empty;
        public string Status { get; set; } = "published";
        public string? UpdatedBy { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? PublishedBy { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public long Version { get; set; }
        public string? PublishedVersionId { get; set; }
        public string? RollbackVersionId { get; set; }
    }

    private sealed class EffectReviewRow
    {
        public string CardId { get; set; } = string.Empty;
        public string? AbilityId { get; set; }
        public string Status { get; set; } = "unreviewed";
        public string Note { get; set; } = string.Empty;
        public string Reviewer { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string StructureHash { get; set; } = string.Empty;
    }

    private sealed class SessionRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TokenHash { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string AuthStrength { get; set; } = "password";
        public int PermissionVersion { get; set; } = 1;
    }

    private sealed class DeckRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MasterId { get; set; } = string.Empty;
        public List<string> CardIds { get; set; } = [];
        public List<string> MoraleIds { get; set; } = [];
        public List<string> SpecialIds { get; set; } = [];
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class PublishedDeckRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string OwnerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MasterId { get; set; } = string.Empty;
        public List<string> CardIds { get; set; } = [];
        public List<string> MoraleIds { get; set; } = [];
        public List<string> SpecialIds { get; set; } = [];
        public List<string> LikedByAccountIds { get; set; } = [];
        public int Copies { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class FriendRow
    {
        public string RequesterId { get; set; } = string.Empty;
        public string AddresseeId { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class DataFile
    {
        public long Version { get; set; }
        public long? BusinessVersion { get; set; }
        public List<AccountRow> Accounts { get; set; } = [];
        public List<SessionRow> Sessions { get; set; } = [];
        public List<DeckRow> Decks { get; set; } = [];
        public List<PublishedDeckRow> PublishedDecks { get; set; } = [];
        public List<FriendRow> Friends { get; set; } = [];
        public List<BugRow> BugReports { get; set; } = [];
        public Dictionary<string, string> Content { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ContentRow> ContentEntries { get; set; } = [];
        public List<EffectReviewRow> EffectReviews { get; set; } = [];
        public List<AdminAuditRow> AdminAudit { get; set; } = [];
        public List<AdminCommandRow> AdminCommands { get; set; } = [];
        public List<AdminApprovalRow> AdminApprovals { get; set; } = [];
        public List<ContentVersionRow> ContentVersions { get; set; } = [];
        public List<ContentBatchRow> ContentBatches { get; set; } = [];
        public List<TournamentRow> Tournaments { get; set; } = [];
        public List<ReleaseEnvironmentRow> ReleaseEnvironments { get; set; } = [];
        public List<ReleaseRunRow> ReleaseRuns { get; set; } = [];
        public List<LoginThrottleRow> LoginThrottles { get; set; } = [];
        public SecurityStateRow Security { get; set; } = new();
        public OperationsConfigRow? OperationsConfig { get; set; }
        public List<OperationsConfigVersionRow> OperationsConfigHistory { get; set; } = [];
    }

    private static readonly string[] ForbiddenNames =
    [
        "管理员", "administer", "administrator", "system", "官方", "客服", "裁判", "gm", "fuck", "shit",
    ];
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IReadOnlyList<L12PresetDeckDefinition> _officialDecks;
    private DataFile _data;

    public event Action<IReadOnlyList<string>>? SessionsRevoked;

    public long Version
    {
        get { lock (_gate) return _data.BusinessVersion ?? _data.Version; }
    }

    public L12PlatformStore(string path, IReadOnlyList<L12PresetDeckDefinition>? officialDecks = null,
        IL12MfaCredentialProtector? mfaCredentialProtector = null)
    {
        _path = path;
        _officialDecks = officialDecks ?? [];
        _mfaCredentialProtector = mfaCredentialProtector ?? new L12UnavailableMfaCredentialProtector();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _databasePath = PlatformDatabasePath(path);
        _data = LoadTransactionalState();
        EnsureRootAdmin();
        EnsureOperationsState();
    }

    public (bool Success, string Message, L12AccountView? Account, string? Token) Register(string username, string password)
    {
        username = username.Trim();
        var validation = ValidateCredentials(username, password);
        if (validation is not null) return (false, validation, null, null);
        lock (_gate)
        {
            if (_data.Accounts.Any(row => string.Equals(row.Username, username, StringComparison.OrdinalIgnoreCase)))
                return (false, "用户名已存在", null, null);
            var row = CreateAccount(username, password, "player");
            _data.Accounts.Add(row);
            SeedOfficialDecks(row.Id);
            var token = IssueToken(row.Id);
            Save();
            return (true, "注册成功", ToView(row), token);
        }
    }

    public L12AuthenticationResult Login(string username, string password, L12LoginAttemptContext? context = null)
    {
        context ??= new L12LoginAttemptContext("store-login", "local", "/store/login");
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var normalizedUsername = username.Trim().ToLowerInvariant();
            var throttleKeys = LoginThrottleKeys(normalizedUsername, context.ClientKey);
            PruneLoginThrottles(now);
            var retryAfter = LoginRetryAfterSeconds(throttleKeys, now);
            if (retryAfter > 0)
            {
                AddAuthenticationAudit(null, normalizedUsername, "rate-limited", context, "denied");
                Save(false);
                return new(false, "登录尝试过多，请稍后重试", null, null,
                    "login_rate_limited", retryAfter);
            }

            var row = _data.Accounts.FirstOrDefault(item =>
                string.Equals(item.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
            if (row is null || !Verify(password, row) || row.Disabled)
            {
                retryAfter = RegisterLoginFailure(throttleKeys, now);
                AddAuthenticationAudit(row is null ? null : ToView(row), normalizedUsername,
                    retryAfter > 0 ? "locked" : row?.Disabled == true ? "account-disabled" : "invalid-credentials",
                    context, "denied");
                Save(false);
                return retryAfter > 0
                    ? new(false, "登录尝试过多，请稍后重试", null, null,
                        "login_rate_limited", retryAfter)
                    : new(false, "用户名或密码错误", null, null, "authentication_failed", 0);
            }

            ClearLoginFailures(throttleKeys);
            var token = IssueToken(row.Id);
            AddAuthenticationAudit(ToView(row), normalizedUsername, "authenticated", context, "succeeded");
            Save();
            return new(true, "登录成功", ToView(row), token, "ok", 0);
        }
    }

    public L12AccountView? Authenticate(string? authorization)
        => AuthenticateSession(authorization)?.Account;

    public L12AuthenticatedSession? AuthenticateSession(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        return AuthenticateTokenSession(authorization[7..].Trim());
    }

    public L12AccountView? AuthenticateToken(string? token)
        => AuthenticateTokenSession(token)?.Account;

    public L12AuthenticatedSession? AuthenticateTokenSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = HashToken(token.Trim());
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var session = _data.Sessions.FirstOrDefault(item => item.TokenHash == hash
                && item.RevokedAt is null && item.ExpiresAt > now);
            var account = session is null ? null : _data.Accounts.FirstOrDefault(row => row.Id == session.AccountId);
            return account is null || account.Disabled ? null : new L12AuthenticatedSession(ToView(account), session!.Id);
        }
    }

    public bool IsSessionActive(string sessionId)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return _data.Sessions.Any(row => row.Id == sessionId && row.RevokedAt is null && row.ExpiresAt > now
                && _data.Accounts.Any(account => account.Id == row.AccountId && !account.Disabled));
        }
    }

    public bool AccountExists(string accountId)
    {
        lock (_gate) return _data.Accounts.Any(row => row.Id == accountId);
    }

    public IReadOnlyList<L12SessionView> Sessions(string accountId, string? currentSessionId = null)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return _data.Sessions.Where(row => row.AccountId == accountId
                    && row.RevokedAt is null && row.ExpiresAt > now)
                .OrderByDescending(row => row.CreatedAt)
                .Select(row => ToView(row, row.Id == currentSessionId)).ToArray();
        }
    }

    public L12SessionRevocationResult RevokeOwnSession(L12AuthenticatedSession actor, string sessionId,
        L12AdminAuditContext? context = null, bool dryRun = false)
        => RevokeSessionCore(actor.Account, actor.Account.Id, sessionId, context, dryRun);

    public L12SessionRevocationResult RevokeOwnSessions(L12AuthenticatedSession actor,
        L12AdminAuditContext? context = null, bool dryRun = false)
        => RevokeSessionsCore(actor.Account, actor.Account.Id, context, dryRun);

    public L12SessionRevocationResult RevokeAccountSession(L12AccountView actor, string accountId, string sessionId,
        L12AdminAuditContext? context = null, bool dryRun = false)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.AdminSessionsRevoke))
            return new L12SessionRevocationResult(false, sessionId, 0, false, []);
        return RevokeSessionCore(actor, accountId, sessionId, context, dryRun);
    }

    public L12SessionRevocationResult RevokeAccountSessions(L12AccountView actor, string accountId,
        L12AdminAuditContext? context = null, bool dryRun = false)
    {
        if (!L12Authorization.HasPermission(actor, L12Permission.AdminSessionsRevoke))
            return new L12SessionRevocationResult(false, null, 0, false, []);
        return RevokeSessionsCore(actor, accountId, context, dryRun);
    }

    public (bool Success, string Message) ChangePassword(string accountId, string currentPassword, string newPassword,
        string? currentSessionId = null)
    {
        if (newPassword.Length is < 8 or > 128) return (false, "新密码长度需为 8–128 个字符");
        string[] revokedSessionIds;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (row is null || !Verify(currentPassword, row)) return (false, "当前密码不正确");
            SetPassword(row, newPassword);
            var now = DateTimeOffset.UtcNow;
            var sessionsToRevoke = _data.Sessions.Where(session => session.AccountId == accountId
                && (string.IsNullOrWhiteSpace(currentSessionId) || session.Id != currentSessionId)
                && session.RevokedAt is null && session.ExpiresAt > now).ToArray();
            foreach (var session in sessionsToRevoke) session.RevokedAt = now;
            revokedSessionIds = sessionsToRevoke.Select(session => session.Id).ToArray();
            Save();
        }
        NotifySessionsRevoked(revokedSessionIds);
        return (true, revokedSessionIds.Length == 0 ? "密码已修改" : $"密码已修改，已撤销其他 {revokedSessionIds.Length} 个会话");
    }

    public IReadOnlyList<L12AccountView> Accounts()
    {
        lock (_gate) return _data.Accounts.OrderBy(row => row.CreatedAt).Select(ToView).ToArray();
    }

    public L12AccountView? Account(string accountId)
    {
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            return row is null ? null : ToView(row);
        }
    }

    public IReadOnlyList<L12FriendView> FindPlayers(string accountId, string? search)
    {
        var query = (search ?? string.Empty).Trim();
        lock (_gate) return _data.Accounts.Where(row => row.Id != accountId && !row.Disabled)
            .Where(row => query.Length == 0 || row.Username.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Username).Take(30).Select(row => ToFriendView(accountId, row)).ToArray();
    }

    public IReadOnlyList<L12FriendView> Friends(string accountId)
    {
        lock (_gate) return _data.Friends.Where(row => row.Status == "accepted"
                && (row.RequesterId == accountId || row.AddresseeId == accountId))
            .Select(row => row.RequesterId == accountId ? row.AddresseeId : row.RequesterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => _data.Accounts.FirstOrDefault(account => account.Id == id))
            .Where(row => row is not null).Select(row => ToFriendView(accountId, row!))
            .OrderBy(row => row.Username).ToArray();
    }

    public IReadOnlyList<L12FriendView> FriendRequests(string accountId)
    {
        lock (_gate) return _data.Friends.Where(row => row.Status == "pending"
                && (row.RequesterId == accountId || row.AddresseeId == accountId))
            .OrderByDescending(row => row.CreatedAt).Select(row =>
            {
                var otherId = row.RequesterId == accountId ? row.AddresseeId : row.RequesterId;
                var other = _data.Accounts.First(account => account.Id == otherId);
                return new L12FriendView(other.Id, other.Username, "pending",
                    row.AddresseeId == accountId ? "incoming" : "outgoing", row.CreatedAt);
            }).ToArray();
    }

    public (bool Success, string Message) SendFriendRequest(string accountId, string targetAccountId)
    {
        if (accountId == targetAccountId) return (false, "不能添加自己为好友");
        lock (_gate)
        {
            if (_data.Accounts.All(row => row.Id != targetAccountId)) return (false, "玩家不存在");
            var existing = FindFriendRow(accountId, targetAccountId);
            if (existing?.Status == "accepted") return (false, "你们已经是好友");
            if (existing?.Status == "pending") return (false, "好友申请已存在");
            _data.Friends.Add(new FriendRow { RequesterId = accountId, AddresseeId = targetAccountId });
            Save();
            return (true, "好友申请已发送");
        }
    }

    public (bool Success, string Message) ResolveFriendRequest(string accountId, string requesterId, bool accept)
    {
        lock (_gate)
        {
            var row = _data.Friends.FirstOrDefault(item => item.Status == "pending"
                && item.RequesterId == requesterId && item.AddresseeId == accountId);
            if (row is null) return (false, "好友申请不存在或已处理");
            if (accept)
            {
                row.Status = "accepted";
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else _data.Friends.Remove(row);
            Save();
            return (true, accept ? "已成为好友" : "已拒绝好友申请");
        }
    }

    public bool RemoveFriend(string accountId, string friendId)
    {
        lock (_gate)
        {
            var row = FindFriendRow(accountId, friendId);
            if (row is null) return false;
            _data.Friends.Remove(row);
            Save();
            return true;
        }
    }

    public bool AreFriends(string firstAccountId, string secondAccountId)
    {
        lock (_gate) return FindFriendRow(firstAccountId, secondAccountId)?.Status == "accepted";
    }

    public IReadOnlyList<L12AccountDeckView> Decks(string accountId)
    {
        lock (_gate) return _data.Decks.Where(row => row.AccountId == accountId)
            .OrderByDescending(row => row.UpdatedAt).Select(ToView).ToArray();
    }

    public L12AccountDeckView UpsertDeck(string accountId, L12PresetDeckDefinition deck)
    {
        lock (_gate)
        {
            var row = _data.Decks.FirstOrDefault(item => item.AccountId == accountId
                && string.Equals(item.Name, deck.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new DeckRow { AccountId = accountId, Name = deck.Name };
                _data.Decks.Add(row);
            }
            row.Name = deck.Name;
            row.MasterId = deck.MasterId;
            row.CardIds = deck.CardIds.ToList();
            row.MoraleIds = deck.MoraleIds.ToList();
            row.SpecialIds = deck.SpecialIds.ToList();
            row.UpdatedAt = DateTimeOffset.UtcNow;
            Save();
            return ToView(row);
        }
    }

    public bool DeleteDeck(string accountId, string name)
    {
        lock (_gate)
        {
            var removed = _data.Decks.RemoveAll(row => row.AccountId == accountId
                && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public bool SetRole(L12AccountView actor, string accountId, string role, L12AdminAuditContext? context = null)
    {
        role = role.Trim().ToLowerInvariant();
        if (!L12Authorization.IsKnownRole(role)) return false;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (IsLastAdminDemotion(row, role))
            {
                AddAdminAudit(actor, "account", "role-denied", row!.Username, row.Role, role,
                    "必须至少保留一个启用的管理员账号", context);
                Save(false);
                return false;
            }
            if (row is null || !CanSetRoleLocked(row, role)) return false;
            var previous = row.Role;
            row.Role = role;
            if (!string.Equals(previous, role, StringComparison.OrdinalIgnoreCase)) row.PermissionVersion++;
            AddAdminAudit(actor, "account", "role", row.Username, previous, role, null, context);
            Save();
            return true;
        }
    }

    public bool CanSetRole(string accountId, string role)
    {
        role = role.Trim().ToLowerInvariant();
        if (!L12Authorization.IsKnownRole(role)) return false;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            return CanSetRoleLocked(row, role);
        }
    }

    private bool CanSetRoleLocked(AccountRow? row, string role)
        => row is not null
           && !string.Equals(row.Username, "Admin", StringComparison.Ordinal)
           && !IsLastAdminDemotion(row, role);

    private bool IsLastAdminDemotion(AccountRow? row, string role)
        => row is not null
           && string.Equals(row.Role, "admin", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
           && _data.Accounts.Count(item => !item.Disabled
               && string.Equals(item.Role, "admin", StringComparison.OrdinalIgnoreCase)) <= 1;

    public IReadOnlyList<L12PublishedDeckView> PublishedDecks(string? viewerAccountId)
    {
        lock (_gate) return _data.PublishedDecks
            .OrderByDescending(row => row.UpdatedAt)
            .Select(row => ToView(row, viewerAccountId)).ToArray();
    }

    public L12PublishedDeckView? PublishDeck(string accountId, L12PresetDeckDefinition deck, string? publicationId)
    {
        lock (_gate)
        {
            var row = string.IsNullOrWhiteSpace(publicationId) ? null
                : _data.PublishedDecks.FirstOrDefault(item => item.Id == publicationId && item.OwnerId == accountId);
            if (!string.IsNullOrWhiteSpace(publicationId) && row is null) return null;
            row ??= _data.PublishedDecks.FirstOrDefault(item => item.OwnerId == accountId
                && string.Equals(item.Name, deck.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new PublishedDeckRow { OwnerId = accountId, CreatedAt = DateTimeOffset.UtcNow };
                _data.PublishedDecks.Add(row);
            }
            row.Name = deck.Name;
            row.MasterId = deck.MasterId;
            row.CardIds = deck.CardIds.ToList();
            row.MoraleIds = deck.MoraleIds.ToList();
            row.SpecialIds = deck.SpecialIds.ToList();
            row.UpdatedAt = DateTimeOffset.UtcNow;
            Save();
            return ToView(row, accountId);
        }
    }

    public bool DeletePublishedDeck(string accountId, string publicationId)
    {
        lock (_gate)
        {
            var removed = _data.PublishedDecks.RemoveAll(row => row.Id == publicationId && row.OwnerId == accountId) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public L12PublishedDeckView? TogglePublishedDeckLike(string accountId, string publicationId)
    {
        lock (_gate)
        {
            var row = _data.PublishedDecks.FirstOrDefault(item => item.Id == publicationId);
            if (row is null) return null;
            if (!row.LikedByAccountIds.Remove(accountId)) row.LikedByAccountIds.Add(accountId);
            Save();
            return ToView(row, accountId);
        }
    }

    public L12PublishedDeckView? RecordPublishedDeckCopy(string publicationId, string? viewerAccountId)
    {
        lock (_gate)
        {
            var row = _data.PublishedDecks.FirstOrDefault(item => item.Id == publicationId);
            if (row is null) return null;
            row.Copies++;
            Save();
            return ToView(row, viewerAccountId);
        }
    }

    public bool SetRole(string accountId, string role)
        => SetRole(new L12AccountView("system", "系统", "admin", DateTimeOffset.UtcNow, false), accountId, role);

    public L12BugReportView AddBug(L12AccountView? account, string title, string description, string page,
        string? roomCode, string? matchId, string version)
    {
        var row = new BugRow
        {
            ReporterId = account?.Id,
            ReporterName = account?.Username ?? "匿名玩家",
            Title = string.IsNullOrWhiteSpace(title) ? "未命名问题" : title.Trim()[..Math.Min(title.Trim().Length, 100)],
            Description = description.Trim()[..Math.Min(description.Trim().Length, 5000)],
            Page = page.Trim()[..Math.Min(page.Trim().Length, 300)],
            RoomCode = roomCode,
            MatchId = matchId,
            Version = string.IsNullOrWhiteSpace(version) ? "dev" : version.Trim(),
        };
        row.History.Add(NewBugAudit(account, "created", null, "new", "提交 Bug 反馈"));
        lock (_gate) { _data.BugReports.Insert(0, row); Save(); }
        return ToView(row);
    }

    public IReadOnlyList<L12BugReportView> Bugs(string? status, string? priority = null, string? assignee = null, string? search = null)
    {
        lock (_gate) return _data.BugReports
            .Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status)
            .Where(row => string.IsNullOrWhiteSpace(priority) || row.Priority == priority)
            .Where(row => string.IsNullOrWhiteSpace(assignee)
                || string.Equals(row.Assignee, assignee.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(row => MatchesBugSearch(row, search))
            .OrderByDescending(row => row.CreatedAt).Select(ToView).ToArray();
    }

    public L12BugReportView? UpdateBug(L12AccountView actor, string id, string? status, string? priority,
        string? assignee, string? notes, string? comment = null, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = _data.BugReports.FirstOrDefault(item => item.Id == id);
            if (row is null) return null;
            var changed = false;
            if (status is "new" or "confirmed" or "in-progress" or "resolved" or "closed"
                && row.Status != status)
            {
                row.History.Add(NewBugAudit(actor, "status", row.Status, status, null));
                row.Status = status;
                changed = true;
            }
            if (priority is "low" or "normal" or "high" or "critical"
                && row.Priority != priority)
            {
                row.History.Add(NewBugAudit(actor, "priority", row.Priority, priority, null));
                row.Priority = priority;
                changed = true;
            }
            var nextAssignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim();
            if (!string.Equals(row.Assignee, nextAssignee, StringComparison.Ordinal))
            {
                row.History.Add(NewBugAudit(actor, "assignee", row.Assignee, nextAssignee, null));
                row.Assignee = nextAssignee;
                changed = true;
            }
            var nextNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (!string.Equals(row.AdminNotes, nextNotes, StringComparison.Ordinal))
            {
                row.History.Add(NewBugAudit(actor, "notes", row.AdminNotes, nextNotes, null));
                row.AdminNotes = nextNotes;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(comment))
            {
                row.History.Add(NewBugAudit(actor, "comment", null, null,
                    comment.Trim()[..Math.Min(comment.Trim().Length, 2000)]));
                changed = true;
            }
            if (!changed) return ToView(row);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "bug", "update", row.Id, null, row.Status,
                string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), context);
            Save();
            return ToView(row);
        }
    }

    public string GetContent(string key, string fallback = "")
    {
        lock (_gate)
        {
            var entry = _data.ContentEntries.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
            return entry?.PublishedValue ?? _data.Content.GetValueOrDefault(key, fallback);
        }
    }

    public void SetContent(string key, string value)
    {
        lock (_gate)
        {
            _data.Content[key] = value;
            var entry = EnsureContentEntry(key);
            entry.DraftValue = value;
            entry.PublishedValue = value;
            entry.Status = "published";
            Save();
        }
    }

    public L12ContentEntryView GetContentEntry(string key)
    {
        lock (_gate)
        {
            if (!IsContentKeyAllowed(key)) throw new ArgumentException($"内容键不在白名单中：{key}");
            var canonical = ContentKeys().First(item => string.Equals(item, key.Trim(), StringComparison.OrdinalIgnoreCase));
            var row = FindContentEntry(canonical);
            if (row is not null) return ToView(row);
            var published = _data.Content.GetValueOrDefault(canonical, string.Empty);
            return new L12ContentEntryView(canonical, published, published, "published", null, null, null, null);
        }
    }

    public L12ContentEntryView SaveContentDraft(L12AccountView actor, string key, string value,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            if (!IsContentKeyAllowed(key)) throw new ArgumentException($"内容键不在白名单中：{key}");
            var canonical = ContentKeys().First(item => string.Equals(item, key.Trim(), StringComparison.OrdinalIgnoreCase));
            var row = EnsureContentEntry(canonical);
            var previous = row.DraftValue;
            row.DraftValue = value;
            row.Status = row.DraftValue == row.PublishedValue ? "published" : "draft";
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.Version++;
            AddAdminAudit(actor, "content", "save-draft", canonical, previous, value, null, context);
            Save();
            return ToView(row);
        }
    }

    public L12ContentEntryView PublishContent(L12AccountView actor, string key,
        L12AdminAuditContext? context = null)
    {
        var payload = CaptureContentPublish([key]);
        PublishContentBatch(actor, payload, context);
        return GetContentEntry(key);
    }

    public L12EffectReviewView SaveEffectReview(L12AccountView actor, string cardId, string? abilityId,
        string status, string? note, string? structureHash = null, L12AdminAuditContext? context = null)
    {
        if (status is not ("unreviewed" or "human-assisted" or "confirmed" or "rejected"))
            throw new ArgumentException("无效的审查状态", nameof(status));
        lock (_gate)
        {
            var row = _data.EffectReviews.FirstOrDefault(item =>
                string.Equals(item.CardId, cardId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));
            var previous = row?.Status;
            if (row is null)
            {
                row = new EffectReviewRow { CardId = cardId, AbilityId = abilityId };
                _data.EffectReviews.Add(row);
            }
            row.Status = status;
            row.Note = note?.Trim() ?? string.Empty;
            row.Reviewer = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.StructureHash = structureHash?.Trim() ?? string.Empty;
            AddAdminAudit(actor, "effect", "review", abilityId is null ? cardId : $"{cardId}/{abilityId}",
                previous, status, row.Note, context);
            Save();
            return ToView(row);
        }
    }

    public L12AtomicCardEffect ApplyEffectReviews(L12AtomicCardEffect effect)
    {
        lock (_gate)
        {
            var cardReview = _data.EffectReviews.LastOrDefault(row => row.AbilityId is null
                && string.Equals(row.CardId, effect.CardId, StringComparison.OrdinalIgnoreCase));
            var migrated = false;
            var abilities = effect.Abilities.Select(ability =>
            {
                var review = _data.EffectReviews.LastOrDefault(row => string.Equals(row.CardId, effect.CardId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.AbilityId, ability.AbilityId, StringComparison.OrdinalIgnoreCase));
                if (review is null && !string.IsNullOrWhiteSpace(ability.LegacyAbilityId))
                {
                    review = _data.EffectReviews.LastOrDefault(row => string.Equals(row.CardId, effect.CardId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(row.AbilityId, ability.LegacyAbilityId, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(row.StructureHash));
                    if (review is not null)
                    {
                        review.AbilityId = ability.AbilityId;
                        review.StructureHash = ability.StructureHash;
                        migrated = true;
                    }
                }
                if (review is not null && !string.IsNullOrWhiteSpace(review.StructureHash)
                    && !string.Equals(review.StructureHash, ability.StructureHash, StringComparison.OrdinalIgnoreCase))
                    review = null;
                return review is null ? ability : ability with { ReviewStatus = review.Status, ReviewSource = $"后台人工确认：{review.Reviewer}" };
            }).ToArray();
            if (migrated) Save();
            var status = cardReview?.Status ?? L12EffectReviewAggregation.CardStatus(abilities, effect.ReviewStatus);
            var source = cardReview is null
                ? L12EffectReviewAggregation.CardSource(abilities, effect.ReviewSource)
                : $"后台人工确认：{cardReview.Reviewer}";
            return effect with { Abilities = abilities, ReviewStatus = status, ReviewSource = source };
        }
    }

    public L12AtomicEffectPage ApplyEffectReviews(L12AtomicEffectPage page)
        => page with { Items = page.Items.Select(ApplyEffectReviews).ToArray() };

    public IReadOnlyList<L12AdminAuditView> AdminAudit(string? category = null, int limit = 200,
        string? outcome = null, string? actorId = null, string? commandId = null, string? correlationId = null)
    {
        lock (_gate) return QueryAdminAudit(category, limit, outcome, actorId, commandId, correlationId);
    }

    public void RecordAuthorizationDenied(L12AccountView? actor, L12AdminAuditContext context,
        string permission, string reason)
    {
        lock (_gate)
        {
            AddAdminAudit(actor, "security", "denied", context.RequestPath ?? "admin-api", null, null,
                reason, context with { Permission = permission, Reason = reason, Outcome = "denied" });
            Save(false);
        }
    }

    public void RecordCommandOutcome(L12AccountView actor, L12AdminAuditContext context,
        string commandType, string scope, string reason)
    {
        lock (_gate)
        {
            AddAdminAudit(actor, "command", commandType, scope, null, null, reason,
                context with { Reason = reason });
            Save(false);
        }
    }

    private void EnsureRootAdmin()
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var account in _data.Accounts)
            {
                var migratedRole = string.Equals(account.Role, "admin", StringComparison.OrdinalIgnoreCase)
                    ? "admin"
                    : "player";
                if (string.Equals(account.Role, migratedRole, StringComparison.Ordinal)) continue;
                account.Role = migratedRole;
                account.PermissionVersion++;
                changed = true;
            }
            var configuredPassword = Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD");
            var row = _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, "Admin", StringComparison.Ordinal));
            if (row is null)
            {
                _data.Accounts.Add(CreateAccount("Admin",
                    string.IsNullOrWhiteSpace(configuredPassword) ? "L12master" : configuredPassword, "admin"));
                Save();
            }
            else
            {
                if (!string.Equals(row.Role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    row.Role = "admin";
                    row.PermissionVersion++;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(configuredPassword) && !Verify(configuredPassword, row))
                {
                    SetPassword(row, configuredPassword);
                    changed = true;
                }
                if (changed) Save();
            }
        }
    }

    private static string? ValidateCredentials(string username, string password)
    {
        if (username.Length is < 2 or > 20) return "用户名长度需为 2–20 个字符";
        if (username.Any(char.IsControl) || ForbiddenNames.Any(word => username.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return "用户名包含不允许使用的词语";
        if (password.Length is < 8 or > 128) return "密码长度需为 8–128 个字符";
        return null;
    }

    private static AccountRow CreateAccount(string username, string password, string role)
    {
        var row = new AccountRow { Username = username, Role = role };
        SetPassword(row, password);
        return row;
    }

    private static void SetPassword(AccountRow row, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 120_000, HashAlgorithmName.SHA256, 32);
        row.Salt = Convert.ToBase64String(salt);
        row.PasswordHash = Convert.ToBase64String(hash);
    }

    private static bool Verify(string password, AccountRow row)
    {
        try
        {
            var salt = Convert.FromBase64String(row.Salt);
            var expected = Convert.FromBase64String(row.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 120_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    private string IssueToken(string accountId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        PruneSessions(now);
        var existing = _data.Sessions.Where(row => row.AccountId == accountId
                && row.RevokedAt is null && row.ExpiresAt > now)
            .OrderByDescending(row => row.CreatedAt).Skip(3).ToHashSet();
        _data.Sessions.RemoveAll(existing.Contains);
        var permissionVersion = _data.Accounts.FirstOrDefault(row => row.Id == accountId)?.PermissionVersion ?? 1;
        _data.Sessions.Add(new SessionRow
        {
            TokenHash = HashToken(token),
            AccountId = accountId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
            PermissionVersion = permissionVersion,
        });
        return token;
    }

    private L12SessionRevocationResult RevokeSessionCore(L12AccountView actor, string accountId, string sessionId,
        L12AdminAuditContext? context, bool dryRun)
    {
        L12SessionRevocationResult result;
        lock (_gate)
        {
            var audit = (context ?? new L12AdminAuditContext("store")) with
            {
                Outcome = dryRun ? "dry-run" : "succeeded",
            };
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId);
            var row = _data.Sessions.FirstOrDefault(item => item.Id == sessionId && item.AccountId == accountId);
            if (account is null || row is null)
            {
                AddAdminAudit(actor, "session", "revoke", $"{accountId}/{sessionId}", null, null,
                    "session-not-found", audit with { Outcome = "rejected", Reason = "session-not-found" });
                Save();
                return new L12SessionRevocationResult(false, sessionId, 0, false, []);
            }

            var inactive = row.RevokedAt is not null || row.ExpiresAt <= DateTimeOffset.UtcNow;
            if (inactive)
            {
                AddAdminAudit(actor, "session", "revoke", $"{accountId}/{sessionId}", null, null,
                    "already-revoked", audit with { Reason = "already-revoked" });
                Save();
                return new L12SessionRevocationResult(true, sessionId, 0, true, []);
            }

            if (dryRun)
            {
                AddAdminAudit(actor, "session", "revoke", $"{account.Username}/{sessionId}", "active", "active",
                    "dry-run", audit with { Reason = "dry-run" });
                Save();
                return new L12SessionRevocationResult(true, sessionId, 0, false, []);
            }

            row.RevokedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "session", "revoke", $"{account.Username}/{sessionId}", "active", "revoked",
                context?.Reason, audit);
            Save();
            result = new L12SessionRevocationResult(true, sessionId, 1, false, [sessionId]);
        }
        NotifySessionsRevoked(result.RevokedSessionIds);
        return result;
    }

    private L12SessionRevocationResult RevokeSessionsCore(L12AccountView actor, string accountId,
        L12AdminAuditContext? context, bool dryRun)
    {
        L12SessionRevocationResult result;
        lock (_gate)
        {
            var audit = (context ?? new L12AdminAuditContext("store")) with
            {
                Outcome = dryRun ? "dry-run" : "succeeded",
            };
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId);
            if (account is null)
            {
                AddAdminAudit(actor, "session", "revoke-all", accountId, null, null, "account-not-found",
                    audit with { Outcome = "rejected", Reason = "account-not-found" });
                Save();
                return new L12SessionRevocationResult(false, null, 0, false, []);
            }

            var now = DateTimeOffset.UtcNow;
            var active = _data.Sessions.Where(row => row.AccountId == accountId
                && row.RevokedAt is null && row.ExpiresAt > now).ToArray();
            if (dryRun)
            {
                AddAdminAudit(actor, "session", "revoke-all", account.Username, null, null,
                    $"dry-run:{active.Length}", audit with { Reason = "dry-run" });
                Save();
                return new L12SessionRevocationResult(true, null, 0, active.Length == 0, []);
            }

            foreach (var session in active) session.RevokedAt = now;
            var revokedIds = active.Select(row => row.Id).ToArray();
            AddAdminAudit(actor, "session", "revoke-all", account.Username, active.Length.ToString(), "0",
                active.Length == 0 ? "already-revoked" : context?.Reason,
                active.Length == 0 ? audit with { Reason = "already-revoked" } : audit);
            Save();
            result = new L12SessionRevocationResult(true, null, revokedIds.Length, revokedIds.Length == 0, revokedIds);
        }
        NotifySessionsRevoked(result.RevokedSessionIds);
        return result;
    }

    private void NotifySessionsRevoked(IReadOnlyList<string> sessionIds)
    {
        if (sessionIds.Count == 0) return;
        try { SessionsRevoked?.Invoke(sessionIds); }
        catch { }
    }

    private void PruneSessions(DateTimeOffset now)
    {
        var revokedCutoff = now.AddDays(-30);
        _data.Sessions.RemoveAll(row => row.ExpiresAt <= now
            || row.RevokedAt is { } revokedAt && revokedAt < revokedCutoff);
    }

    private void SeedOfficialDecks(string accountId)
    {
        foreach (var deck in _officialDecks)
        {
            _data.Decks.Add(new DeckRow
            {
                AccountId = accountId,
                Name = deck.Name,
                MasterId = deck.MasterId,
                CardIds = deck.CardIds.ToList(),
                MoraleIds = deck.MoraleIds.ToList(),
                SpecialIds = deck.SpecialIds.ToList(),
            });
        }
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static L12AccountView ToView(AccountRow row) => new(row.Id, row.Username, row.Role, row.CreatedAt,
        row.PublicHistory, row.PermissionVersion, row.Disabled, row.DisabledAt, row.DisabledReason);
    private L12FriendView ToFriendView(string viewerId, AccountRow row)
    {
        var relation = FindFriendRow(viewerId, row.Id);
        var direction = relation is null ? "none" : relation.AddresseeId == viewerId ? "incoming" : "outgoing";
        return new L12FriendView(row.Id, row.Username, relation?.Status ?? "none", direction,
            relation?.CreatedAt ?? row.CreatedAt);
    }

    private FriendRow? FindFriendRow(string firstAccountId, string secondAccountId)
        => _data.Friends.FirstOrDefault(row => (row.RequesterId == firstAccountId && row.AddresseeId == secondAccountId)
            || (row.RequesterId == secondAccountId && row.AddresseeId == firstAccountId));
    private static L12AccountDeckView ToView(DeckRow row) => new(row.Name, row.MasterId, row.CardIds.ToArray(),
        row.MoraleIds.ToArray(), row.SpecialIds.ToArray(), row.UpdatedAt);
    private L12PublishedDeckView ToView(PublishedDeckRow row, string? viewerAccountId)
    {
        var author = _data.Accounts.FirstOrDefault(account => account.Id == row.OwnerId)?.Username ?? "已注销玩家";
        var deck = new L12AccountDeckView(row.Name, row.MasterId, row.CardIds.ToArray(), row.MoraleIds.ToArray(),
            row.SpecialIds.ToArray(), row.UpdatedAt);
        return new L12PublishedDeckView(row.Id, row.OwnerId, author, deck, row.LikedByAccountIds.Count, row.Copies,
            viewerAccountId is not null && row.LikedByAccountIds.Contains(viewerAccountId), row.CreatedAt, row.UpdatedAt);
    }
    private static L12BugReportView ToView(BugRow row) => new(row.Id, row.ReporterId, row.ReporterName, row.Title, row.Description,
        row.Page, row.RoomCode, row.MatchId, row.Version, row.Status, row.Priority, row.Assignee, row.AdminNotes,
        row.History.OrderByDescending(item => item.CreatedAt).Select(ToView).ToArray(), row.CreatedAt, row.UpdatedAt);
    private static L12BugAuditView ToView(BugAuditRow row) => new(row.Id, row.ActorId, row.ActorName, row.Action,
        row.FromValue, row.ToValue, row.Comment, row.CreatedAt);
    private static L12AdminAuditView ToView(AdminAuditRow row) => new(row.Id, row.ActorId, row.ActorName,
        row.Category, row.Action, row.Target, row.FromValue, row.ToValue, row.Comment, row.CreatedAt,
        row.CorrelationId, row.Outcome, row.Permission, row.Reason, row.CommandId, row.IdempotencyKey,
        row.DryRun, row.ExpectedVersion, row.RequestMethod, row.RequestPath);
    private static L12SessionView ToView(SessionRow row, bool current) => new(row.Id, row.CreatedAt,
        row.ExpiresAt, current, row.AuthStrength, row.PermissionVersion);
    private static L12ContentEntryView ToView(ContentRow row) => new(row.Key, row.DraftValue, row.PublishedValue,
        row.Status, row.UpdatedBy, row.UpdatedAt, row.PublishedBy, row.PublishedAt, row.Version,
        row.PublishedVersionId, row.RollbackVersionId);
    private static L12EffectReviewView ToView(EffectReviewRow row) => new(row.CardId, row.AbilityId, row.Status,
        row.Note, row.Reviewer, row.UpdatedAt, row.StructureHash);

    private ContentRow EnsureContentEntry(string key)
    {
        var row = _data.ContentEntries.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (row is not null) return row;
        var published = _data.Content.GetValueOrDefault(key, string.Empty);
        row = new ContentRow { Key = key, DraftValue = published, PublishedValue = published, Status = "published" };
        _data.ContentEntries.Add(row);
        return row;
    }

    private void AddAdminAudit(L12AccountView? actor, string category, string action, string target,
        string? fromValue, string? toValue, string? comment, L12AdminAuditContext? context = null)
    {
        _data.AdminAudit.Add(new AdminAuditRow
        {
            ActorId = actor?.Id ?? string.Empty,
            ActorName = actor?.Username ?? "匿名请求",
            Category = category,
            Action = action,
            Target = target,
            FromValue = fromValue,
            ToValue = toValue,
            Comment = comment,
            CorrelationId = context?.CorrelationId,
            Outcome = context?.Outcome ?? "succeeded",
            Permission = context?.Permission,
            Reason = context?.Reason,
            CommandId = context?.CommandId,
            IdempotencyKey = context?.IdempotencyKey,
            DryRun = context?.DryRun ?? false,
            ExpectedVersion = context?.ExpectedVersion,
            RequestMethod = context?.RequestMethod,
            RequestPath = context?.RequestPath,
        });
        if (_data.AdminAudit.Count > 5000)
            _data.AdminAudit = _data.AdminAudit.OrderByDescending(row => row.CreatedAt).Take(5000).ToList();
    }

    private static BugAuditRow NewBugAudit(L12AccountView? actor, string action, string? fromValue,
        string? toValue, string? comment) => new()
    {
        ActorId = actor?.Id,
        ActorName = actor?.Username ?? "匿名玩家",
        Action = action,
        FromValue = fromValue,
        ToValue = toValue,
        Comment = comment,
    };

    private static bool MatchesBugSearch(BugRow row, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var query = search.Trim();
        return new[] { row.Id, row.ReporterName, row.Title, row.Description, row.Page, row.RoomCode, row.MatchId, row.Assignee }
            .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static DataFile Load(string path)
    {
        try
        {
            return DeserializeData(File.ReadAllText(path));
        }
        catch { return new DataFile(); }
    }

    private void Save(bool businessChange = true)
    {
        if (_adminTransactionDepth > 0)
        {
            _adminTransactionSaveRequested = true;
            _adminTransactionBusinessChanged |= businessChange;
            return;
        }
        PersistData(businessChange);
    }

    private void PersistData(bool businessChange)
        => PersistTransactionalData(businessChange);
}
