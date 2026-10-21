using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using TwelveLegions.Server;

if (args.Length != 1 || args[0] is not ("probe" or "analyze"))
    throw new ArgumentException("Only explicit local component probe/analyze modes are allowed");
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("L12_ALLOCATION_PROBE_ROOT")
    ?? throw new InvalidOperationException("Explicit governed output required"));
var repo = Path.GetFullPath(Environment.GetEnvironmentVariable("L12_PROBE_REPO")
    ?? throw new InvalidOperationException("Explicit source root required"));
var accepted = Path.GetFullPath(Environment.GetEnvironmentVariable("L12_ACCEPTED_SERVER_ROOT")
    ?? throw new InvalidOperationException("Explicit accepted server required"));
var guard = new ProcessStartInfo(Environment.GetEnvironmentVariable("L12_PWSH")
    ?? throw new InvalidOperationException("Pinned PowerShell required")) { UseShellExecute = false, CreateNoWindow = true };
// dotnet-trace launches this process with a suspend/connect diagnostic port.
// A nested .NET PowerShell guard must not inherit that ownership or wait for
// the profiler's single target handshake. The actual probe remains traced.
guard.Environment.Remove("DOTNET_DiagnosticPorts");
guard.Environment.Remove("DOTNET_DefaultDiagnosticPortSuspend");
guard.ArgumentList.Add("-NoProfile"); guard.ArgumentList.Add("-Command");
guard.ArgumentList.Add(". (Join-Path $env:L12_PROBE_REPO 'ops/windows/L12GeneratedStorage.ps1'); "
    + "$null=Assert-L12GeneratedPath -Path $env:L12_ALLOCATION_PROBE_ROOT -Purpose 'local component allocation'; "
    + "$null=Assert-L12GeneratedPath -Path $env:TEMP -Purpose 'local allocation temp'");
using (var guarded = Process.Start(guard)!)
{
    if (!guarded.WaitForExit(10000)) { guarded.Kill(entireProcessTree: true); throw new TimeoutException("Output guard"); }
    if (guarded.ExitCode != 0) throw new InvalidOperationException("Output guard rejected path");
}
Directory.CreateDirectory(root);
var expectedSha = Environment.GetEnvironmentVariable("L12_PROBE_EXPECTED_SERVER_SHA256")
    ?? throw new InvalidOperationException("Explicit server SHA binding required");
if (!Regex.IsMatch(expectedSha, "^[0-9A-F]{64}$")) throw new InvalidOperationException("Invalid server SHA binding");
if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(accepted, "GrandUMIServer.dll")))) != expectedSha)
    throw new InvalidOperationException("Accepted server drift");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(accepted, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
if (args[0] == "analyze") Analyze(); else Probe();

void Save(string name, object value)
{
    using var output = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write);
    JsonSerializer.Serialize(output, value, new JsonSerializerOptions { WriteIndented = true });
}

void Probe()
{
    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(L12GameEngine).Assembly.Location))) != expectedSha)
        throw new InvalidOperationException("Loaded server drift");
    var catalog = L12Catalog.Load(Path.Combine(accepted, "TwelveLegions", "Data"));
    var game = new L12GameEngine(catalog, "synthetic-allocation-component", "SYNTH", 90210,
        ["synthetic-a", "synthetic-b"], [0, 1], skipPreparation: true);
    game.State.Phase = L12Phase.Main;
    game.State.PendingPrompts.Clear();
    var cards = game.State.Players.SelectMany(player => player.Library.Take(12)).ToArray();
    var fixture = Environment.GetEnvironmentVariable("L12_PROBE_FIXTURE") ?? "large-repeated";
    if (fixture is not ("large-repeated" or "representative")) throw new InvalidOperationException("Unknown fixture");
    var add = typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(method => method.Name == "AddEvent" && method.GetParameters().Length == 4
            && method.GetParameters()[3].ParameterType == typeof(L12CardInstance[]));
    for (var index = 0; index < 128; index++)
        add.Invoke(game, ["synthetic-allocation", index % 2, "合成公开展示",
            fixture == "large-repeated" || index < 24
                ? new[] { cards[index % cards.Length], cards[(index + 1) % cards.Length], cards[(index + 2) % cards.Length] }
                : Array.Empty<L12CardInstance>()]);
    var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    object Project() => game.SnapshotForSpectator();
    var projected = Project();
    byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new { type = "gameState", state = projected }, wire);
    var full = Serialize();
    if (fixture == "representative" && (full.Length < 180 * 1024 || full.Length > 300 * 1024))
        throw new InvalidOperationException("Representative frame must remain within the original 180-300 KiB range");
    var assembly = typeof(L12GameEngine).Assembly;
    var codecType = assembly.GetType("TwelveLegions.Server.L12SnapshotWireCodec", true)!;
    var queuedType = assembly.GetType("TwelveLegions.Server.L12QueuedPayload", true)!;
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    object Codec()
    {
        var codec = Activator.CreateInstance(codecType, nonPublic: true)!;
        codecType.GetMethod("SetDeltaEnabled", flags, [typeof(bool), typeof(bool)])!.Invoke(codec, [true, true]);
        codecType.GetMethod("SetCardSnapshotFrameEnabled", flags)!.Invoke(codec, [true]);
        return codec;
    }
    var prepare = codecType.GetMethod("Prepare", flags)!;
    var queuedFull = Activator.CreateInstance(queuedType, flags, null, [full, true, true, false, false], null)!;
    var queuedDelta = Activator.CreateInstance(queuedType, flags, null, [full, true, false, false, false], null)!;
    var fullCodec = Codec(); var deltaCodec = Codec();
    var first = prepare.Invoke(deltaCodec, [queuedFull])!;
    var payload = first.GetType().GetProperty("Payload")!;
    var commit = first.GetType().GetProperty("Commit")!;
    ((Action)commit.GetValue(first)!)();
    object Encode(object codec, object queued)
    {
        var result = prepare.Invoke(codec, [queued])!;
        ((Action)commit.GetValue(result)!)();
        return payload.GetValue(result)!;
    }
    var results = new List<object>();
    void Measure(string phase, Func<object> operation)
    {
        for (var index = 0; index < 4; index++) GC.KeepAlive(operation());
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (var index = 0; index < 128; index++) GC.KeepAlive(operation());
        var elapsed = watch.Elapsed.TotalMilliseconds;
        results.Add(new { phase, iterations = 128, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes,
            elapsedMs = elapsed });
        if (elapsed > 20000) throw new TimeoutException("Component phase exceeded bound");
    }
    Measure("recipient-projection", Project);
    Measure("full-json-serialization", Serialize);
    Measure("full-card-frame-encode", () => Encode(fullCodec, queuedFull));
    Measure("same-state-delta-encode", () => Encode(deltaCodec, queuedDelta));
    Save("component-cost.json", new { schema = 1, scope = "local-synthetic-component-only", expectedSha,
        fullJsonBytes = full.Length, fullJsonSha256 = Convert.ToHexString(SHA256.HashData(full)),
        phases = results, onlineCapacityAccepted = false,
        productFixBenefitProven = false, fixture = fixture == "representative"
            ? "128 public synthetic events; first 24 include three real catalog card snapshots"
            : "128 public synthetic events; three real catalog card snapshots per event",
        fixtureTier = fixture,
        caveat = "Codec calls include QA reflection overhead. Identical-state delta is not a changing-match workload. No network, database, production or private player state is used." });
    Console.WriteLine("Local component allocation probe completed");
}

void Analyze()
{
    var tracePath = Path.Combine(root, "allocation.nettrace");
    var etlx = Path.Combine(root, "allocation.etlx");
    TraceLog.CreateFromEventPipeDataFile(tracePath, etlx, new TraceLogOptions { ConversionLog = TextWriter.Null });
    using var log = new TraceLog(etlx);
    var groups = new Dictionary<string, (long Samples, long Bytes)>();
    long allocations = 0, noStack = 0;
    foreach (var entry in log.Events)
    {
        if (entry is not GCAllocationTickTraceData allocation) continue;
        allocations++;
        var names = new List<string>();
        for (var stack = allocation.CallStack(); stack is not null && names.Count < 32; stack = stack.Caller)
            names.Add(Regex.Replace(stack.CodeAddress.FullMethodName ?? "[unresolved]", @"pMT:\s*[0-9A-Fa-f]+", "pMT:[runtime-type]"));
        if (names.Count == 0) noStack++;
        var key = allocation.TypeName + "\n" + string.Join("\n", names);
        var prior = groups.GetValueOrDefault(key);
        groups[key] = (prior.Samples + 1, prior.Bytes + allocation.AllocationAmount64);
    }
    if (allocations == 0) throw new InvalidDataException("No allocation samples were decoded");
    Save("allocation-stacks.json", new { schema = 1, scope = "sampled-local-component-allocation", allocations,
        noStack, eventsLost = log.EventsLost, onlineCapacityAccepted = false,
        top = groups.OrderByDescending(row => row.Value.Bytes).Take(30)
            .Select(row => new { typeAndCallPath = row.Key, samples = row.Value.Samples, sampledIntervalBytes = row.Value.Bytes }),
        caveat = "Allocation ticks sample intervals, not exact object-by-object allocations. Includes fixture setup and all phases; use call paths and exact phase counters together, not production percentages." });
    Console.WriteLine($"Decoded {allocations} allocation samples; missing stack {noStack}; lost events {log.EventsLost}");
}
