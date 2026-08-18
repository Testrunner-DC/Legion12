using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12AccountView(string Id, string Username, string Role, DateTimeOffset CreatedAt, bool PublicHistory);
public sealed record L12BugReportView(string Id, string? ReporterId, string ReporterName, string Title, string Description,
    string Page, string? RoomCode, string? MatchId, string Version, string Status, string Priority, string? Assignee,
    string? AdminNotes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

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
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class DataFile
    {
        public List<AccountRow> Accounts { get; set; } = [];
        public List<BugRow> BugReports { get; set; } = [];
        public Dictionary<string, string> Content { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] ForbiddenNames =
    [
        "管理员", "administer", "administrator", "system", "官方", "客服", "裁判", "gm", "fuck", "shit",
    ];
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private DataFile _data;

    public L12PlatformStore(string path)
    {
        _path = path;
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
            Save();
            var token = IssueToken(row.Id);
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
            return (true, "登录成功", ToView(row), token);
        }
    }

    public L12AccountView? Authenticate(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        lock (_gate)
            return _tokens.TryGetValue(token, out var id) ? _data.Accounts.Where(row => row.Id == id).Select(ToView).FirstOrDefault() : null;
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

    public bool SetRole(string accountId, string role)
    {
        if (role is not ("player" or "referee" or "organizer" or "editor" or "admin")) return false;
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => item.Id == accountId);
            if (row is null || string.Equals(row.Username, "Admin", StringComparison.Ordinal)) return false;
            row.Role = role;
            Save();
            return true;
        }
    }

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
        lock (_gate) { _data.BugReports.Insert(0, row); Save(); }
        return ToView(row);
    }

    public IReadOnlyList<L12BugReportView> Bugs(string? status)
    {
        lock (_gate) return _data.BugReports
            .Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status)
            .OrderByDescending(row => row.CreatedAt).Select(ToView).ToArray();
    }

    public L12BugReportView? UpdateBug(string id, string? status, string? priority, string? assignee, string? notes)
    {
        lock (_gate)
        {
            var row = _data.BugReports.FirstOrDefault(item => item.Id == id);
            if (row is null) return null;
            if (status is "new" or "confirmed" or "in-progress" or "resolved" or "closed") row.Status = status;
            if (priority is "low" or "normal" or "high" or "critical") row.Priority = priority;
            row.Assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim();
            row.AdminNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            row.UpdatedAt = DateTimeOffset.UtcNow;
            Save();
            return ToView(row);
        }
    }

    public string GetContent(string key, string fallback = "")
    {
        lock (_gate) return _data.Content.GetValueOrDefault(key, fallback);
    }

    public void SetContent(string key, string value)
    {
        lock (_gate) { _data.Content[key] = value; Save(); }
    }

    private void EnsureRootAdmin()
    {
        lock (_gate)
        {
            var row = _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, "Admin", StringComparison.Ordinal));
            if (row is null) _data.Accounts.Add(CreateAccount("Admin", "L12master", "admin"));
            else row.Role = "admin";
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
        _tokens[token] = accountId;
        return token;
    }

    private static L12AccountView ToView(AccountRow row) => new(row.Id, row.Username, row.Role, row.CreatedAt, row.PublicHistory);
    private static L12BugReportView ToView(BugRow row) => new(row.Id, row.ReporterId, row.ReporterName, row.Title, row.Description,
        row.Page, row.RoomCode, row.MatchId, row.Version, row.Status, row.Priority, row.Assignee, row.AdminNotes, row.CreatedAt, row.UpdatedAt);

    private static DataFile Load(string path)
    {
        try { return JsonSerializer.Deserialize<DataFile>(File.ReadAllText(path)) ?? new DataFile(); }
        catch { return new DataFile(); }
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
