using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveLegions.Server;

// An in-memory, stdin-only component probe. It has no listener, account store or recorder.
if (args.Length is not (2 or 3) || !Path.IsPathFullyQualified(args[0]) || args[1].Length != 64
    || args.Length == 3 && args[2] != "property-frame-v1")
    throw new ArgumentException("Explicit catalog root and expected loaded assembly SHA256 are required");
var assembly = typeof(L12GameEngine).Assembly;
var propertyMode = args.Length == 3;
if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) != args[1])
    throw new InvalidOperationException("Loaded assembly differs from the accepted test package");
const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
var wire = (JsonSerializerOptions)assembly.GetType("TwelveLegions.Server.L12SnapshotJson", true)!
    .GetProperty("Options", statics)!.GetValue(null)!;
var codecType = assembly.GetType("TwelveLegions.Server.L12SnapshotWireCodec", true)!;
var queuedType = assembly.GetType("TwelveLegions.Server.L12QueuedPayload", true)!;
var prepare = codecType.GetMethod("Prepare", instance)!;
var catalog = L12Catalog.Load(args[0]);
var stateVersion = (int)assembly.GetType("TwelveLegions.Server.L12PersistenceContract", true)!
    .GetField("CurrentStateFormatVersion", statics)!.GetRawConstantValue()!;
var clock = DateTimeOffset.Parse("2026-10-24T00:00:00Z");
var watch = Stopwatch.StartNew();
L12GameEngine? game = null;
object[] codecs = [];
object[] comparisonCodecs = [];
var commands = 0;
var requests = 0;
while (Console.ReadLine() is { } line)
{
    if (++requests > 1024 || watch.Elapsed > TimeSpan.FromMinutes(2) || line.Length > 65536)
        throw new InvalidOperationException("Offline probe budget exceeded");
    using var request = JsonDocument.Parse(line);
    var action = request.RootElement.GetProperty("op").GetString();
    if (action == "stop") break;
    if (action == "start")
    {
        var seed = request.RootElement.GetProperty("seed").GetInt32();
        game = new L12GameEngine(catalog, "synthetic-wire-" + seed, "SYNTH", seed,
            ["Synthetic A", "Synthetic B"], [0, 1], skipPreparation: true,
            disasterMode: "none", stateFormatVersion: stateVersion, utcNow: () => clock);
        object Codec(bool enableProperty)
        {
            var codec = Activator.CreateInstance(codecType, true)!;
            codecType.GetMethod("SetDeltaEnabled", instance, [typeof(bool), typeof(bool), typeof(bool)])!
                .Invoke(codec, [true, true, true]);
            codecType.GetMethod("SetCardSnapshotFrameEnabled", instance)!.Invoke(codec, [true]);
            if (enableProperty)
                (codecType.GetMethod("SetPropertyFrameEnabled", instance)
                    ?? throw new InvalidOperationException("Requested property framing is absent"))
                    .Invoke(codec, [true]);
            return codec;
        }
        codecs = Enumerable.Range(0, 3).Select(_ => Codec(propertyMode)).ToArray();
        comparisonCodecs = propertyMode ? Enumerable.Range(0, 3).Select(_ => Codec(false)).ToArray() : [];
        commands = 0;
    }
    else if (action == "command" && game is not null)
    {
        var command = request.RootElement.GetProperty("command").Deserialize<L12Command>(wire)!;
        var player = request.RootElement.GetProperty("player").GetInt32();
        var outcome = game.Handle(player, command);
        if (!outcome.Accepted)
            throw new InvalidDataException("Selected real command was rejected: " + command.Type);
        commands++;
        clock = clock.AddSeconds(2);
    }
    else throw new InvalidOperationException("Unknown operation or missing synthetic game");
    _ = game!.ComputeStateHash();
    var authority = game.SerializeFullState();
    var snapshots = new List<object>();
    for (var viewer = 0; viewer < 3; viewer++)
    {
        var snapshot = viewer == 2 ? game.SnapshotForSpectator() : game.SnapshotFor(viewer);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "gameState", state = snapshot }, wire);
        if (game.SerializeFullState() != authority)
            throw new InvalidDataException("Recipient projection mutated the synthetic authority");
        var queued = Activator.CreateInstance(queuedType, instance, null,
            [bytes, true, game.State.Phase == L12Phase.GameOver, false, false], null)!;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var prepared = prepare.Invoke(codecs[viewer], [queued])!;
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var payload = (byte[])prepared.GetType().GetProperty("Payload")!.GetValue(prepared)!;
        ((Action)prepared.GetType().GetProperty("Commit")!.GetValue(prepared)!)();
        byte[]? comparisonPayload = null;
        double? comparisonMs = null;
        long? comparisonAllocated = null;
        if (propertyMode)
        {
            var comparisonAllocationStart = GC.GetAllocatedBytesForCurrentThread();
            var comparisonStart = Stopwatch.GetTimestamp();
            var comparison = prepare.Invoke(comparisonCodecs[viewer], [queued])!;
            comparisonMs = Stopwatch.GetElapsedTime(comparisonStart).TotalMilliseconds;
            comparisonAllocated = GC.GetAllocatedBytesForCurrentThread() - comparisonAllocationStart;
            comparisonPayload = (byte[])comparison.GetType().GetProperty("Payload")!.GetValue(comparison)!;
            ((Action)comparison.GetType().GetProperty("Commit")!.GetValue(comparison)!)();
        }
        snapshots.Add(new { viewer, logical = Convert.ToBase64String(bytes),
            wire = Convert.ToBase64String(payload), comparisonWire = comparisonPayload is null ? null : Convert.ToBase64String(comparisonPayload),
            prepareMs = elapsed, prepareAllocatedBytes = allocated,
            comparisonPrepareMs = comparisonMs, comparisonPrepareAllocatedBytes = comparisonAllocated });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { commands, snapshots }, wire));
}
