using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12AccountView(string Id, string Username, string Role, DateTimeOffset CreatedAt, bool PublicHistory);
public sealed record L12AccountDeckView(string Name, string MasterId, IReadOnlyList<string> CardIds,
    IReadOnlyList<string> MoraleIds, IReadOnlyList<string> SpecialIds, DateTimeOffset UpdatedAt);
public sealed record L12BugReportView(string Id, string? ReporterId, string ReporterName, string Title, string Description,
    string Page, string? RoomCode, string? MatchId, string Version, string Status, string Priority, string? Assignee,
    string? AdminNotes, IReadOnlyList<L12BugAuditView> History, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12BugAuditView(string Id, string? ActorId, string ActorName, string Action,
    string? FromValue, string? ToValue, string? Comment, DateTimeOffset CreatedAt);
public sealed record L12AdminAuditView(string Id, string ActorId, string ActorName, string Category, string Action,
    string Target, string? FromValue, string? ToValue, string? Comment, DateTimeOffset CreatedAt);
public sealed record L12ContentEntryView(string Key, string DraftValue, string PublishedValue, string Status,
    string? UpdatedBy, DateTimeOffset? UpdatedAt, string? PublishedBy, DateTimeOffset? PublishedAt);
public sealed record L12EffectReviewView(string CardId, string? AbilityId, string Status, string Note,
    string Reviewer, DateTimeOffset UpdatedAt, string StructureHash = "");

public sealed class L12PlatformStore
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
        public string TokenHash { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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

    private sealed class DataFile
    {
        public List<AccountRow> Accounts { get; set; } = [];
        public List<SessionRow> Sessions { get; set; } = [];
        public List<DeckRow> Decks { get; set; } = [];
        public List<BugRow> BugReports { get; set; } = [];
        public Dictionary<string, string> Content { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ContentRow> ContentEntries { get; set; } = [];
        public List<EffectReviewRow> EffectReviews { get; set; } = [];
        public List<AdminAuditRow> AdminAudit { get; set; } = [];
    }

    private static readonly string[] ForbiddenNames =
    [
        "管理员", "administer", "administrator", "system", "官方", "客服", "裁判", "gm", "fuck", "shit",
    ];
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IReadOnlyList<L12PresetDeckDefinition> _officialDecks;
    private DataFile _data;

    public L12PlatformStore(string path, IReadOnlyList<L12PresetDeckDefinition>? officialDecks = null)
    {
        _path = path;
        _officialDecks = officialDecks ?? [];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _data = Load(path);
        EnsureRootAdmin();
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

    public (bool Success, string Message, L12AccountView? Account, string? Token) Login(string username, string password)
    {
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (row is null || !Verify(password, row)) return (false, "用户名或密码错误", null, null);
            var token = IssueToken(row.Id);
            Save();
            return (true, "登录成功", ToView(row), token);
        }
    }

    public L12AccountView? Authenticate(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        return AuthenticateToken(authorization[7..].Trim());
    }

    public L12AccountView? AuthenticateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = HashToken(token.Trim());
        lock (_gate)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
            var session = _data.Sessions.FirstOrDefault(item => item.TokenHash == hash && item.CreatedAt >= cutoff);
            return session is null ? null : _data.Accounts.Where(row => row.Id == session.AccountId).Select(ToView).FirstOrDefault();
        }
    }

    public (bool Success, string Message) ChangePassword(string accountId, string currentPassword, string newPassword)
    {
        if (newPassword.Length is < 8 or > 128) return (false, "新密码长度需为 8–128 个字符");
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (row is null || !Verify(currentPassword, row)) return (false, "当前密码不正确");
            SetPassword(row, newPassword);
            Save();
            return (true, "密码已修改");
        }
    }

    public IReadOnlyList<L12AccountView> Accounts()
    {
        lock (_gate) return _data.Accounts.OrderBy(row => row.CreatedAt).Select(ToView).ToArray();
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

    public bool SetRole(L12AccountView actor, string accountId, string role)
    {
        if (role is not ("player" or "referee" or "organizer" or "editor" or "admin")) return false;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (row is null || string.Equals(row.Username, "Admin", StringComparison.Ordinal)) return false;
            var previous = row.Role;
            row.Role = role;
            AddAdminAudit(actor, "account", "role", row.Username, previous, role, null);
            Save();
            return true;
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
        string? assignee, string? notes, string? comment = null)
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
        lock (_gate) return ToView(EnsureContentEntry(key));
    }

    public L12ContentEntryView SaveContentDraft(L12AccountView actor, string key, string value)
    {
        lock (_gate)
        {
            var row = EnsureContentEntry(key);
            var previous = row.DraftValue;
            row.DraftValue = value;
            row.Status = row.DraftValue == row.PublishedValue ? "published" : "draft";
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "content", "save-draft", key, previous, value, null);
            Save();
            return ToView(row);
        }
    }

    public L12ContentEntryView PublishContent(L12AccountView actor, string key)
    {
        lock (_gate)
        {
            var row = EnsureContentEntry(key);
            var previous = row.PublishedValue;
            row.PublishedValue = row.DraftValue;
            row.Status = "published";
            row.PublishedBy = actor.Username;
            row.PublishedAt = DateTimeOffset.UtcNow;
            _data.Content[key] = row.PublishedValue;
            AddAdminAudit(actor, "content", "publish", key, previous, row.PublishedValue, null);
            Save();
            return ToView(row);
        }
    }

    public L12EffectReviewView SaveEffectReview(L12AccountView actor, string cardId, string? abilityId,
        string status, string? note, string? structureHash = null)
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
                previous, status, row.Note);
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

    public IReadOnlyList<L12AdminAuditView> AdminAudit(string? category = null, int limit = 200)
    {
        lock (_gate) return _data.AdminAudit
            .Where(row => string.IsNullOrWhiteSpace(category) || row.Category == category)
            .OrderByDescending(row => row.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).Select(ToView).ToArray();
    }

    private void EnsureRootAdmin()
    {
        lock (_gate)
        {
            var configuredPassword = Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD");
            var row = _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, "Admin", StringComparison.Ordinal));
            if (row is null)
                _data.Accounts.Add(CreateAccount("Admin",
                    string.IsNullOrWhiteSpace(configuredPassword) ? "L12master" : configuredPassword, "admin"));
            else
            {
                row.Role = "admin";
                if (!string.IsNullOrWhiteSpace(configuredPassword)) SetPassword(row, configuredPassword);
            }
            Save();
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
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        _data.Sessions.RemoveAll(row => row.CreatedAt < cutoff);
        var existing = _data.Sessions.Where(row => row.AccountId == accountId)
            .OrderByDescending(row => row.CreatedAt).Skip(4).ToHashSet();
        _data.Sessions.RemoveAll(existing.Contains);
        _data.Sessions.Add(new SessionRow { TokenHash = HashToken(token), AccountId = accountId });
        return token;
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

    private static L12AccountView ToView(AccountRow row) => new(row.Id, row.Username, row.Role, row.CreatedAt, row.PublicHistory);
    private static L12AccountDeckView ToView(DeckRow row) => new(row.Name, row.MasterId, row.CardIds.ToArray(),
        row.MoraleIds.ToArray(), row.SpecialIds.ToArray(), row.UpdatedAt);
    private static L12BugReportView ToView(BugRow row) => new(row.Id, row.ReporterId, row.ReporterName, row.Title, row.Description,
        row.Page, row.RoomCode, row.MatchId, row.Version, row.Status, row.Priority, row.Assignee, row.AdminNotes,
        row.History.OrderByDescending(item => item.CreatedAt).Select(ToView).ToArray(), row.CreatedAt, row.UpdatedAt);
    private static L12BugAuditView ToView(BugAuditRow row) => new(row.Id, row.ActorId, row.ActorName, row.Action,
        row.FromValue, row.ToValue, row.Comment, row.CreatedAt);
    private static L12AdminAuditView ToView(AdminAuditRow row) => new(row.Id, row.ActorId, row.ActorName,
        row.Category, row.Action, row.Target, row.FromValue, row.ToValue, row.Comment, row.CreatedAt);
    private static L12ContentEntryView ToView(ContentRow row) => new(row.Key, row.DraftValue, row.PublishedValue,
        row.Status, row.UpdatedBy, row.UpdatedAt, row.PublishedBy, row.PublishedAt);
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

    private void AddAdminAudit(L12AccountView actor, string category, string action, string target,
        string? fromValue, string? toValue, string? comment)
    {
        _data.AdminAudit.Add(new AdminAuditRow
        {
            ActorId = actor.Id,
            ActorName = actor.Username,
            Category = category,
            Action = action,
            Target = target,
            FromValue = fromValue,
            ToValue = toValue,
            Comment = comment,
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
            var data = JsonSerializer.Deserialize<DataFile>(File.ReadAllText(path)) ?? new DataFile();
            data.Accounts ??= [];
            data.Sessions ??= [];
            data.Decks ??= [];
            data.BugReports ??= [];
            foreach (var bug in data.BugReports) bug.History ??= [];
            data.Content ??= new(StringComparer.OrdinalIgnoreCase);
            data.ContentEntries ??= [];
            data.EffectReviews ??= [];
            data.AdminAudit ??= [];
            return data;
        }
        catch { return new DataFile(); }
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
