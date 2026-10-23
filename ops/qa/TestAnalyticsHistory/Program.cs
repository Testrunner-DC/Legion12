using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveLegions.Server;

// Offline fixture only: no HTTP listener and no option to open either service database.
if (!OperatingSystem.IsLinux() || args.Length != 4 || args[0] != "--generate"
    || !Guid.TryParseExact(args[1], "N", out var runId)
    || args[2].Length != 40 || args[2].Any(c => !"0123456789abcdef".Contains(c))
    || args[3].Length != 64 || args[3].Any(c => !"0123456789ABCDEF".Contains(c)))
    throw new InvalidOperationException("Explicit Linux offline fixture and candidate identity required");
// The caller supplies the already-verified test candidate, not a source path or
// a default identity from an older release. Both metadata and loaded DLL must match.
var expectedCommit = args[2];
var expectedSha = args[3];
var root = Path.Combine("/tmp", "l12-analytics-fixture-" + runId.ToString("N"));
var accepted = "/opt/legion12-testrun/publish";
var allowedInputs = new HashSet<string>(StringComparer.Ordinal)
    { "TestAnalyticsHistory.dll", "TestAnalyticsHistory.deps.json", "TestAnalyticsHistory.runtimeconfig.json", "libe_sqlite3.so" };
if (!Directory.Exists(root) || new DirectoryInfo(root).LinkTarget is not null
    || new DirectoryInfo("/tmp").LinkTarget is not null
    || File.GetUnixFileMode(root) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)
    || Directory.EnumerateFileSystemEntries(root).Any(entry => !allowedInputs.Contains(Path.GetFileName(entry))
        || !File.Exists(entry) || Directory.Exists(entry) || new FileInfo(entry).LinkTarget is not null)
    || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(accepted, "GrandUMIServer.dll")))) != expectedSha
    || File.ReadAllText("/opt/legion12-testrun/.deployment-commit").Trim() != expectedCommit)
    throw new InvalidOperationException("Offline fixture identity/root mismatch");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var candidate = Path.Combine(accepted, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};
await GenerateAsync();

async Task GenerateAsync()
{
    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(L12GameEngine).Assembly.Location))) != expectedSha)
        throw new InvalidOperationException("Loaded accepted assembly mismatch");
    var database = Path.Combine(root, "fixture-matches.db");
    if (File.Exists(database)) throw new InvalidOperationException("Existing fixture is never reused");
    var catalog = L12Catalog.Load(Path.Combine(accepted, "TwelveLegions", "Data"));
    var attach = typeof(MatchRecorder).GetMethod("AttachCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var conclude = typeof(L12GameEngine).GetMethod("ConcludeByAuthority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    var version = (int)typeof(L12GameEngine).Assembly.GetType("TwelveLegions.Server.L12PersistenceContract", true)!
        .GetField("CurrentStateFormatVersion", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    var ids = Enumerable.Range(0, 16).Select(index => $"qa-history-{runId:N}-{index:00}").ToArray();
    string? effectVersion;
    long total;
    long minimumSamples;
    await using (var recorder = new MatchRecorder(database))
    {
        attach.Invoke(recorder, [catalog]);
        await recorder.InitializeAsync();
        for (var index = 0; index < ids.Length; index++)
        {
            var engine = new L12GameEngine(catalog, ids[index], $"QH{index:000}", 71000 + index,
                ["合成资料甲", "合成资料乙"], [index % catalog.PresetDecks.Count,
                    (index + 1) % catalog.PresetDecks.Count], skipPreparation: true, stateFormatVersion: version);
            await recorder.StartAsync(engine, "ranked", $"qa-history-{runId:N}-{index}-a", $"qa-history-{runId:N}-{index}-b",
                [catalog.DeckAt(index % catalog.PresetDecks.Count), catalog.DeckAt((index + 1) % catalog.PresetDecks.Count)]);
            const string reason = "synthetic analytics preparation; not a player match";
            conclude.Invoke(engine, [0, reason]);
            await recorder.AppendAsync(engine, 1, -1, JsonSerializer.Serialize(new
                { type = "authorityConclusion", reason, winner = engine.State.Winner,
                    agreedDraw = engine.State.EndedByAgreedDraw }), CommandResult.Ok(), $"qa-history-{runId:N}-{index}");
            await recorder.CompleteAsync(engine);
        }
        effectVersion = (string?)typeof(MatchRecorder).GetMethod("ResolveAnalyticsEffectVersion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(recorder, [null]);
        var page = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(EffectVersion: effectVersion));
        if (page.Items.Count == 0 || page.Total <= 0 || page.Items.Any(item => item.SampleSize < 5))
            throw new InvalidOperationException("Original default analytics query must be nonempty");
        var matches = await recorder.ListMatchesAsync(50);
        if (matches.Count != ids.Length || matches.Any(match => !ids.Contains(match.MatchId) || match.EndedUtc is null))
            throw new InvalidOperationException("Offline synthetic dependency closure mismatch");
        total = page.Total;
        minimumSamples = page.Items.Min(item => item.SampleSize);
    }
    var proof = new { schema = 1, kind = "offline-synthetic-analytics-history", runId, database,
        assemblySha256 = expectedSha, effectVersion, matchIds = ids, matches = ids.Length,
        defaultQueryTotal = total, minimumReturnedSamples = minimumSamples,
        serviceDatabaseWrites = 0, playerMatchesClaimed = false, onlineCapacityAccepted = false };
    var json = JsonSerializer.Serialize(proof);
    await using (var file = new FileStream(Path.Combine(root, "fixture-proof.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
    { await file.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json)); file.Flush(true); }
    Console.WriteLine(json);
}
