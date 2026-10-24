using System.Runtime.Loader;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveLegions.Server;

// Synthetic private /tmp fixture only: no live-path option, HTTP listener,
// production credential or restore into either running service database.
if (!OperatingSystem.IsLinux() || args.Length != 5 || args[0] is not ("--seed" or "--verify")
    || !Guid.TryParseExact(args[1], "N", out var runId)
    || args[2] != "3a376f17e5a6f9431303b35775977fabd2083b6a"
    || args[3] != "BAEFF29E79B3DE4AF855151D166ABD89EB415D3426D40762E1860A8DA5FFF171"
    || args[4].Length != 64 || args[4].Any(c => !"0123456789abcdef".Contains(c)))
    throw new InvalidOperationException("Synthetic Linux restore fixture identity required");
var root = Path.Combine("/tmp", "l12-restore-business-" + runId.ToString("N"));
var app = Path.Combine(root, "app");
var runtime = Path.Combine(root, args[0] == "--seed" ? "source" : "restored", "runtime");
CheckPlainDirectory("/tmp"); CheckPlainDirectory(root); CheckPlainDirectory(app);
var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
if (File.GetUnixFileMode(root) != privateMode) throw new InvalidOperationException("PRIVATE_OWNER_REQUIRED");
using (var owner = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "owner.json"))))
{
    var value = owner.RootElement;
    if (value.GetProperty("purpose").GetString() != "nonempty-business-restore"
        || value.GetProperty("runId").GetString() != runId.ToString("N")
        || value.GetProperty("commit").GetString() != args[2]
        || value.GetProperty("dllSha256").GetString() != args[3])
        throw new InvalidOperationException("OWNER_IDENTITY_MISMATCH");
}
var assembly = Path.Combine(app, "GrandUMIServer.dll");
if (new FileInfo(assembly).LinkTarget is not null || Hash(assembly) != args[3])
    throw new InvalidOperationException("ACCEPTED_ASSEMBLY_MISMATCH");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var file = Path.Combine(app, name.Name + ".dll");
    return File.Exists(file) && new FileInfo(file).LinkTarget is null
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
};
await RunAsync();

async Task RunAsync()
{
    if (Hash(typeof(L12PlatformStore).Assembly.Location) != args[3])
        throw new InvalidOperationException("LOADED_ASSEMBLY_MISMATCH");
    var password = Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD");
    if (password is null || password.Length < 32) throw new InvalidOperationException("SYNTHETIC_PRIVATE_PASSWORD_REQUIRED");
    var seeding = args[0] == "--seed";
    CheckPlainDirectory(Path.GetDirectoryName(runtime)!);
    if (seeding)
    {
        if (Directory.Exists(runtime) || File.Exists(runtime)) throw new InvalidOperationException("FRESH_RUNTIME_REQUIRED");
        Directory.CreateDirectory(runtime); File.SetUnixFileMode(runtime, privateMode);
    }
    else
    {
        CheckPlainDirectory(runtime);
        var pending = new Stack<string>(); pending.Push(runtime);
        var entries = 0;
        while (pending.TryPop(out var directory))
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (++entries > 10000 || File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("RESTORED_TREE_NOT_PLAIN_OR_BOUNDED");
            if (Directory.Exists(entry)) pending.Push(entry);
        }
    }
    var catalog = L12Catalog.Load(Path.Combine(app, "TwelveLegions", "Data"));
    var store = new L12PlatformStore(Path.Combine(runtime, "platform.json"), catalog.PresetDecks,
        officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
        officialCardProducts: catalog.CardProducts, emailFeatureEnabled: false);
    // Reuse Program's existing internal one-shot startup hook; never add a
    // public setter or bypass the store's startup/readonly checks for a fixture.
    var startup = typeof(L12PlatformStore).GetMethod("ApplyPrivateDeckPersistenceStartup", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PINNED_STARTUP_HOOK_MISSING");
    if (startup.GetParameters() is not [{ ParameterType: var parameter }] || parameter != typeof(bool))
        throw new InvalidOperationException("PINNED_STARTUP_HOOK_CHANGED");
    startup.Invoke(store, [true]);
    if (seeding)
    {
        var admin = store.Login("Admin", password).Account ?? throw new InvalidOperationException("SYNTHETIC_ADMIN_SEED_FAILED");
        var one = store.Register("恢复甲" + runId.ToString("N")[..8], password);
        var two = store.Register("恢复乙" + runId.ToString("N")[..8], password);
        if (!one.Success || !two.Success || one.Account is null || two.Account is null)
            throw new InvalidOperationException("SYNTHETIC_ACCOUNT_SEED_FAILED");
        if (!store.SendFriendRequest(one.Account.Id, two.Account.Id).Success
            || !store.ResolveFriendRequest(two.Account.Id, one.Account.Id, true).Success)
            throw new InvalidOperationException("SYNTHETIC_FRIEND_SEED_FAILED");
        var bug = store.AddBug(one.Account, "恢复演练反馈甲", "纯合成：中文、引号\"与换行\n均应保留。", "/qa/restore", null, null, args[2]);
        store.UpdateBug(admin, bug.Id, "confirmed", "high", null, "合成处理记录", "合成审计，不关闭真实反馈");
        store.AddBug(two.Account, "恢复演练反馈乙", "第二条合成反馈", "/qa/restore", null, null, args[2]);
        var imagePath = Path.Combine(root, "fixture.webp");
        if (new FileInfo(imagePath).LinkTarget is not null || new FileInfo(imagePath).Length > 256 * 1024
            || Hash(imagePath).ToLowerInvariant() != args[4]) throw new InvalidOperationException("PUBLIC_FIXTURE_IMAGE_MISMATCH");
        var image = File.ReadAllBytes(imagePath);
        var media = store.UploadSiteMedia(admin, new L12SiteMediaUpload("news", "synthetic.webp", "image/webp",
            image, image, image, image, "仅恢复演练使用的公开卡图样本", .5, .5,
            "演练桌面图", "演练移动图", "演练缩略图", false));
        var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "恢复演练已发布资讯", "合成摘要",
            "合成正文，不属于任何真实玩家。", "官方公告", "", "", "restore-published", false, null, MediaAssetId: media.Id));
        store.PublishArticle(admin, draft.Id);
        store.SaveArticleDraft(admin, new L12ArticleDraft(null, "恢复演练草稿资讯", "草稿摘要",
            "未发布正文也应保留。", "官方公告", "", "", "restore-draft", false, null));
        if (store.PublishDeck(one.Account.Id, catalog.PresetDecks.First(), null) is null)
            throw new InvalidOperationException("SYNTHETIC_PUBLIC_DECK_FAILED");
        await using var recorder = new MatchRecorder(Path.Combine(runtime, "matches.db"));
        await recorder.InitializeAsync();
    }
    var articles = store.AdminArticles();
    var bugs = store.Bugs(null);
    if (articles.Count != 2 || store.PublicArticles().Count != 1 || bugs.Count != 2
        || store.AdminSiteMedia().Count != 1 || articles.Count(row => row.Status == "published") != 1
        || bugs.Count(row => row.Status == "confirmed") != 1
        || !bugs.Any(row => row.Description.Contains("\n", StringComparison.Ordinal))
        || !articles.Any(row => row.Status == "draft" && row.Body.Contains("未发布", StringComparison.Ordinal)))
        throw new InvalidOperationException("NONEMPTY_BUSINESS_READBACK_FAILED");
    Console.WriteLine(JsonSerializer.Serialize(new { schema = 1, status = seeding ? "SYNTHETIC_BUSINESS_SEEDED" : "RESTORED_BUSINESS_API_READBACK_VERIFIED",
        articles = articles.Count, publishedArticles = 1, draftArticles = 1, bugReports = bugs.Count,
        confirmedBugReports = 1, media = 1, formalWrites = 0, loopbackListenersStarted = 0 }));
}

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static void CheckPlainDirectory(string path)
{
    if (!Directory.Exists(path) || new DirectoryInfo(path).LinkTarget is not null)
        throw new InvalidOperationException("PLAIN_OWNED_DIRECTORY_REQUIRED");
}
