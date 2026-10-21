using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveLegions.Server;

// This executable has no remote mode. It owns a new disposable database, not either live service.
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("L12_LOCAL_QUALIFICATION_ROOT")
    ?? throw new InvalidOperationException("Explicit local root is required"));
var accepted = Path.GetFullPath(Environment.GetEnvironmentVariable("L12_ACCEPTED_SERVER_ROOT")
    ?? throw new InvalidOperationException("Explicit accepted server is required"));
var ownerPath = Path.Combine(root, "owner.json");
using var owner = JsonDocument.Parse(File.ReadAllText(ownerPath));
if (owner.RootElement.GetProperty("kind").GetString() != "l12-local-real-pressure-v1"
    || !Guid.TryParseExact(owner.RootElement.GetProperty("runId").GetString(), "N", out _)
    || new DirectoryInfo(root).LinkTarget is not null
    || !root.StartsWith(Path.GetFullPath(owner.RootElement.GetProperty("cacheParent").GetString()!)
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Owned local root mismatch");
var expectedSha = owner.RootElement.GetProperty("serverSha256").GetString();
var actualSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(accepted, "GrandUMIServer.dll"))));
if (actualSha != expectedSha) throw new InvalidOperationException("Accepted server drift");
if (Environment.GetEnvironmentVariable("L12_LISTEN_HOST") != "127.0.0.1")
    throw new InvalidOperationException("Local host must bind IPv4 loopback only");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(accepted, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
await RunAsync();

async Task RunAsync()
{
    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(L12GameEngine).Assembly.Location))) != expectedSha)
        throw new InvalidOperationException("Loaded server differs from the accepted assembly");
    var runtime = Path.Combine(root, "runtime");
    if (Directory.Exists(runtime)) throw new InvalidOperationException("Existing data is never reused");
    Directory.CreateDirectory(runtime);
    var catalog = L12Catalog.Load(Path.Combine(accepted, "TwelveLegions", "Data"));
    var platform = new L12PlatformStore(Path.Combine(runtime, "platform.json"), catalog.PresetDecks,
        officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
        officialCardProducts: catalog.CardProducts);
    var fixtures = typeof(L12PlatformStore).GetMethod("EnsureTestRunAcceptanceFixtures",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(platform, null);
    await using var recorder = new MatchRecorder(Path.Combine(runtime, "matches.db"));
    typeof(MatchRecorder).GetMethod("AttachCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(recorder, [catalog]);
    await recorder.InitializeAsync();
    // Real catalog/engine facts, not strings used to pad network frames.
    var conclude = typeof(L12GameEngine).GetMethod("ConcludeByAuthority",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    var stateVersion = (int)typeof(L12GameEngine).Assembly.GetType("TwelveLegions.Server.L12PersistenceContract", true)!
        .GetField("CurrentStateFormatVersion", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    for (var index = 0; index < 16; index++)
    {
        var engine = new L12GameEngine(catalog, $"local-history-{index}", $"LH{index:000}", 71000 + index,
            ["synthetic-history-a", "synthetic-history-b"], [index % catalog.PresetDecks.Count,
                (index + 1) % catalog.PresetDecks.Count], skipPreparation: true,
            stateFormatVersion: stateVersion);
        await recorder.StartAsync(engine, "ranked", $"local-seed-{index}-a", $"local-seed-{index}-b",
            [catalog.DeckAt(index % catalog.PresetDecks.Count), catalog.DeckAt((index + 1) % catalog.PresetDecks.Count)]);
        const string reason = "local synthetic qualification history";
        conclude.Invoke(engine, [0, reason]);
        await recorder.AppendAsync(engine, 1, -1, JsonSerializer.Serialize(new
            { type = "authorityConclusion", reason, winner = engine.State.Winner,
                agreedDraw = engine.State.EndedByAgreedDraw }), CommandResult.Ok(), $"local-seed-{index}");
        await recorder.CompleteAsync(engine);
    }
    var rooms = new L12RoomManager(catalog, recorder, platform);
    var assembly = typeof(L12WebSocketServer).Assembly;
    var diagnosticType = assembly.GetType("TwelveLegions.Server.L12SyntheticTransportDiagnostics", true)!;
    var nonce = Environment.GetEnvironmentVariable("L12_LOCAL_DIAGNOSTIC_NONCE")!;
    Environment.SetEnvironmentVariable("L12_LOCAL_DIAGNOSTIC_NONCE", null);
    var diagnostic = diagnosticType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
        .Invoke(null, [true, "terminal-recorder-v3:6:" + nonce, null, null, null]);
    // Reuse the same isolated diagnostic seam as the platform regressions; no production startup guard is changed.
    var constructor = typeof(L12WebSocketServer).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(item => item.GetParameters().Length == 13);
    await using var server = (L12WebSocketServer)constructor.Invoke(
        [rooms, recorder, platform, catalog, null, null, null, null, null, null, null, diagnostic, true]);
    await server.StartAsync(0);
    var address = new Uri(AssertLoopback(server.Addresses.Single()));
    var ready = new { schema = 1, kind = "local-real-http-ready", pid = Environment.ProcessId,
        baseUrl = address.ToString(), serverSha256 = actualSha, syntheticHistoryMatches = 16, fixtures };
    await File.WriteAllTextAsync(Path.Combine(root, "ready.json"), JsonSerializer.Serialize(ready));
    var samples = new List<object>();
    using var process = System.Diagnostics.Process.GetCurrentProcess();
    var nextSample = DateTimeOffset.MinValue;
    try
    {
        var until = DateTimeOffset.UtcNow.AddMinutes(16);
        while (!File.Exists(Path.Combine(root, "stop")) && DateTimeOffset.UtcNow < until)
        {
            if (DateTimeOffset.UtcNow >= nextSample)
            {
                process.Refresh();
                samples.Add(new { utcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    cpuMs = process.TotalProcessorTime.TotalMilliseconds, workingSet = process.WorkingSet64,
                    allocatedBytes = GC.GetTotalAllocatedBytes(), heapBytes = GC.GetTotalMemory(false),
                    gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                    threadPoolPending = ThreadPool.PendingWorkItemCount });
                nextSample = DateTimeOffset.UtcNow.AddSeconds(1);
            }
            await Task.Delay(100);
        }
    }
    finally
    {
        await server.StopAsync();
        (diagnostic as IDisposable)?.Dispose();
        await File.WriteAllTextAsync(Path.Combine(root, "numeric-process-samples.json"), JsonSerializer.Serialize(samples));
        await File.WriteAllTextAsync(Path.Combine(root, "stopped.json"), "{\"graceful\":true}");
    }
}

static string AssertLoopback(string value)
{
    var uri = new Uri(value);
    if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port <= 0)
        throw new InvalidOperationException("Non-loopback listener rejected");
    return value;
}
