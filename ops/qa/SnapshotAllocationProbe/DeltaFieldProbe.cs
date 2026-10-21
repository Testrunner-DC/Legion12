using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;

// Numeric offline attribution, not a new load runner or a claim about captured wire frames.
internal static class DeltaFieldProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private delegate JsonObject DeltaDecoder(JsonObject baseline, ReadOnlySpan<byte> bytes);

    internal static Task RunAsync(string accepted, string expectedSha,
        Action<string, object> save)
    {
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(L12GameEngine).Assembly.Location))) != expectedSha)
            throw new InvalidOperationException("Loaded server drift");
        var assembly = typeof(L12GameEngine).Assembly;
        var codecType = assembly.GetType("TwelveLegions.Server.L12SnapshotWireCodec", true)!;
        var queuedType = assembly.GetType("TwelveLegions.Server.L12QueuedPayload", true)!;
        var prepare = codecType.GetMethod("Prepare", Instance)!;
        var decode = codecType.GetMethod("ApplyDelta", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<DeltaDecoder>();
        var strictDecoderNegativeCases = ValidateDecoderRefusals(decode);
        var catalog = L12Catalog.Load(Path.Combine(accepted, "TwelveLegions", "Data"));
        var version = (int)assembly.GetType("TwelveLegions.Server.L12PersistenceContract", true)!
            .GetField("CurrentStateFormatVersion", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var add = typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "AddEvent" && method.GetParameters().Length == 4
                && method.GetParameters()[3].ParameterType == typeof(L12CardInstance[]));
        var totals = new Dictionary<string, Totals>(StringComparer.Ordinal);
        var prototypeCases = ValidatePrototype();
        var cases = new List<object>();
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var fixture = 0; fixture < 3; fixture++)
        {
            var now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");
            var engine = new L12GameEngine(catalog, "synthetic-delta-field-" + fixture, "SYNTH", 90210 + fixture,
                ["synthetic-a", "synthetic-b"], [fixture * 2, fixture * 2 + 1],
                skipPreparation: true, disasterMode: "none", stateFormatVersion: version, utcNow: () => now);
            foreach (var owner in new[] { 0, 1 })
            {
                var outcome = engine.Handle(owner, new L12Command("mulligan"));
                if (!outcome.Accepted) throw new InvalidDataException("Synthetic preparation rejected");
            }
            if (engine.State.Phase != L12Phase.Main || engine.State.PendingPrompts.Count != 0)
                throw new InvalidDataException("Fixture did not reach its actual main phase");
            // Same real-catalog public history shape as the previous representative
            // component probe. Synthetic setup only; not an original-load payload floor.
            var cards = engine.State.Players.SelectMany(player => player.Library.Take(12)).ToArray();
            for (var index = 0; index < 128; index++)
                add.Invoke(engine, ["synthetic-allocation", index % 2, "合成公开展示",
                    index < 24 ? new[] { cards[index % cards.Length], cards[(index + 1) % cards.Length],
                        cards[(index + 2) % cards.Length] } : Array.Empty<L12CardInstance>()]);
            var codecs = Enumerable.Range(0, 3).Select(_ =>
            {
                var codec = Activator.CreateInstance(codecType, nonPublic: true)!;
                codecType.GetMethod("SetDeltaEnabled", Instance, [typeof(bool), typeof(bool)])!.Invoke(codec, [true, true]);
                codecType.GetMethod("SetCardSnapshotFrameEnabled", Instance)!.Invoke(codec, [true]);
                return codec;
            }).ToArray();
            var baselines = new byte[3][];
            var frames = new List<object>();
            string Authority() => JsonSerializer.Serialize(new { state = engine.State,
                random = typeof(L12GameEngine).GetProperty("RandomState", Instance)!.GetValue(engine),
                factSequence = typeof(L12GameEngine).GetProperty("CardFactSignalSequence", Instance)!.GetValue(engine) });
            void Capture(string command)
            {
                _ = engine.ComputeStateHash(); // Materialize the same committed projection preparation first.
                var authority = Authority();
                for (var viewer = 0; viewer < 3; viewer++)
                {
                    var snapshot = viewer == 2 ? engine.SnapshotForSpectator() : engine.SnapshotFor(viewer);
                    var full = JsonSerializer.SerializeToUtf8Bytes(new { type = "gameState", state = snapshot }, Web);
                    if (Authority() != authority) throw new InvalidDataException("Projection mutated authority/random/facts");
                    var queued = Activator.CreateInstance(queuedType, Instance, null,
                        [full, true, baselines[viewer] is null, false, false], null)!;
                    var prepared = prepare.Invoke(codecs[viewer], [queued])!;
                    var payload = (byte[])prepared.GetType().GetProperty("Payload")!.GetValue(prepared)!;
                    using var parsed = JsonDocument.Parse(payload);
                    var isDelta = (bool)prepared.GetType().GetProperty("IsDelta")!.GetValue(prepared)!;
                    var key = viewer == 2 ? "public-spectator" : "player";
                    if (!totals.TryGetValue(key, out var total)) totals[key] = total = new();
                    total.Record(payload, parsed.RootElement, isDelta, full.Length, baselines[viewer]);
                    if (isDelta && !JsonNode.DeepEquals(
                        decode(JsonNode.Parse(baselines[viewer])!.AsObject(), payload), JsonNode.Parse(full)))
                        throw new InvalidDataException("Original strict delta decoder reconstruction differs");
                    ((Action)prepared.GetType().GetProperty("Commit")!.GetValue(prepared)!)();
                    baselines[viewer] = full;
                    frames.Add(new { command, viewer, isDelta, logicalBytes = full.Length, wireBytes = payload.Length });
                }
            }
            Capture("initial"); Capture("unchanged-republish");
            for (var turn = 0; turn < 16; turn++)
            {
                if (started.Elapsed > TimeSpan.FromMinutes(1)) throw new TimeoutException("Offline component budget");
                now = now.AddSeconds(2);
                var result = engine.Handle(engine.State.ActivePlayer, new L12Command("endTurn"));
                if (!result.Accepted || engine.State.PendingPrompts.Count != 0)
                    throw new InvalidDataException("Actual endTurn cannot continue the bounded fixture");
                Capture("endTurn");
            }
            cases.Add(new { fixture, acceptedEndTurns = 16, frames });
        }
        save("delta-field-cost.json", new { schema = 1, scope = "offline-real-command-engine-projection-codec",
            serverSha256 = expectedSha, cases,
            totals = totals.ToDictionary(row => row.Key, row => row.Value.Summary()),
            reconstructionChecked = true, authorityRandomFactsUnchanged = true,
            splicePrototypePureCases = prototypeCases,
            strictDecoderNegativeCases,
            onlineCapacityAccepted = false, newPressureRuns = 0, databaseReads = 0, productSourceChanges = 0,
            retainedJournalNotUsed = "All 30 cleaned-up friendly journals are identity_scrubbed=1; their original state hashes cannot validate the original wire. No old hash check was disabled to force a pass.",
            caveat = "Three fixed six-preset component scenarios: 128 synthetic public events, real preparation and endTurn draws. Not a captured workload or original payload-floor qualification. Both players and public spectator use their actual independent codec. Room clock/ACK envelopes, send coalescing and transport scheduling are absent. Change-object bytes exclude commas/wrapper overhead recorded separately. No production percentage, CPU or latency benefit is inferred." });
        Console.WriteLine("Offline actual-command delta-field attribution completed");
        return Task.CompletedTask;
    }

    private static int ValidateDecoderRefusals(DeltaDecoder decode)
    {
        const string baseline = """
            {"type":"gameState","state":{"revision":1,"recentEvents":[{"sequence":1}],"x":[1]}}
            """;
        var invalid = new[]
        {
            """{"changes":[{"path":["state","x",0],"value":null,"remove":true}]}""",
            """{"changes":[{"path":["state","recentEvents"],"value":[],"remove":false}],"eventWindow":{}}""",
            """{"changes":[],"eventWindow":{"baseSequences":[1],"removeFirst":-1,"append":[],"resultSequences":[1]}}""",
            """{"changes":[],"eventWindow":{"baseSequences":[1],"removeFirst":0,"append":[{"sequence":0}],"resultSequences":[1,0]}}""",
        };
        foreach (var text in invalid)
        {
            try { _ = decode(JsonNode.Parse(baseline)!.AsObject(), Encoding.UTF8.GetBytes(text)); }
            catch (InvalidDataException) { continue; }
            throw new InvalidDataException("Original strict delta decoder did not reject the malformed test");
        }
        return invalid.Length;
    }

    private static JsonElement SplicePrototype(JsonElement old, JsonElement next, JsonElement path)
    {
        var prefix = 0;
        while (prefix < Math.Min(old.GetArrayLength(), next.GetArrayLength())
            && JsonElement.DeepEquals(old[prefix], next[prefix])) prefix++;
        var suffix = 0;
        while (suffix < Math.Min(old.GetArrayLength(), next.GetArrayLength()) - prefix
            && JsonElement.DeepEquals(old[old.GetArrayLength() - suffix - 1], next[next.GetArrayLength() - suffix - 1])) suffix++;
        var insert = next.EnumerateArray().Skip(prefix).Take(next.GetArrayLength() - prefix - suffix).ToArray();
        var rebuilt = old.EnumerateArray().Take(prefix).Concat(insert)
            .Concat(old.EnumerateArray().Skip(old.GetArrayLength() - suffix)).ToArray();
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(rebuilt), next))
            throw new InvalidDataException("Prototype splice did not completely restore the array");
        return JsonSerializer.SerializeToElement(new { path = path.Clone(), baseLength = old.GetArrayLength(),
            resultLength = next.GetArrayLength(), index = prefix, deleteCount = old.GetArrayLength() - prefix - suffix, insert });
    }

    private static int ValidatePrototype()
    {
        // Boundary and identical-ID/different-value cases; no live encoding is changed.
        var cases = new[] { ("[]", "[1]"), ("[1]", "[]"), ("[1,2]", "[1,2,3]"),
            ("[1,2]", "[3,1,2]"), ("[1,2]", "[1,3,2]"), ("[1,2,3]", "[2,3]"),
            ("[1,2,3]", "[1,2]"), ("[1,2,3]", "[1,3]"), ("[1,2]", "[2,1]"),
            ("[1,1,1]", "[1,1]"), ("[1,2]", "[3,4]"),
            ("[{\"cardId\":\"same\",\"troops\":1}]", "[{\"cardId\":\"same\",\"troops\":2}]") };
        var path = JsonSerializer.SerializeToElement(new object[] { "state", "players", 0, "hand" });
        foreach (var pair in cases)
        {
            using var before = JsonDocument.Parse(pair.Item1); using var after = JsonDocument.Parse(pair.Item2);
            _ = SplicePrototype(before.RootElement, after.RootElement, path);
        }
        return cases.Length;
    }


    private sealed class Totals
    {
        private long _frames, _delta, _bytes, _deltaBytes, _fullBytes, _logicalBytes, _windowBytes, _changeBytes, _overhead;
        private long _handCandidates, _oldHandChangeBytes, _prototypeSpliceBytes;
        private readonly Dictionary<string, (long Count, long ChangeBytes, long ValueBytes)> _fields = new(StringComparer.Ordinal);
        internal void Record(byte[] payload, JsonElement root, bool delta, int fullBytes, byte[]? baseline)
        {
            _frames++; _bytes += payload.Length; _logicalBytes += fullBytes;
            if (!delta) { _fullBytes += payload.Length; return; }
            _delta++; _deltaBytes += payload.Length;
            long changesBytes = 0;
            foreach (var change in root.GetProperty("changes").EnumerateArray())
            {
                var segments = change.GetProperty("path").EnumerateArray().Select(item =>
                    item.ValueKind == JsonValueKind.Number ? "[]" : item.GetString()!).ToArray();
                var limit = segments.Length > 3 && segments[0] == "state" && segments[1] == "players" ? 4 : 2;
                // Only fixed structural prefixes. Never emit dynamic prompt/instance dictionary keys.
                var key = string.Join('/', segments.Take(limit));
                var changeBytes = Encoding.UTF8.GetByteCount(change.GetRawText());
                var valueBytes = Encoding.UTF8.GetByteCount(change.GetProperty("value").GetRawText());
                var prior = _fields.GetValueOrDefault(key);
                _fields[key] = (prior.Count + 1, prior.ChangeBytes + changeBytes, prior.ValueBytes + valueBytes);
                changesBytes += changeBytes;
                if (segments.Length == 4 && key == "state/players/[]/hand" && baseline is not null
                    && change.GetProperty("value").ValueKind == JsonValueKind.Array)
                {
                    using var before = JsonDocument.Parse(baseline);
                    var path = change.GetProperty("path");
                    var old = before.RootElement.GetProperty("state").GetProperty("players")[path[2].GetInt32()].GetProperty("hand");
                    var next = change.GetProperty("value");
                    var prototypeBytes = JsonSerializer.SerializeToUtf8Bytes(SplicePrototype(old, next, path)).Length;
                    _handCandidates++; _oldHandChangeBytes += changeBytes; _prototypeSpliceBytes += prototypeBytes;
                }
            }
            var windowBytes = root.TryGetProperty("eventWindow", out var window) ? Encoding.UTF8.GetByteCount(window.GetRawText()) : 0;
            _changeBytes += changesBytes; _windowBytes += windowBytes;
            _overhead += payload.Length - changesBytes - windowBytes;
        }
        internal object Summary() => new { frames = _frames, deltaFrames = _delta, wireBytes = _bytes,
            logicalBytes = _logicalBytes, deltaBytes = _deltaBytes, fullOrPackedBytes = _fullBytes,
            eventWindowBytes = _windowBytes, changeObjectBytes = _changeBytes, wrapperAndCommaBytes = _overhead,
            handSplicePrototype = new { cases = _handCandidates, originalChangeObjectBytes = _oldHandChangeBytes,
                completeValueSpliceObjectBytes = _prototypeSpliceBytes, exactReconstructionChecked = true,
                wireProtocolImplemented = false, negotiatedEnvelopeOverheadNotIncluded = true },
            fields = _fields.OrderByDescending(row => row.Value.ChangeBytes).Select(row => new
                { field = row.Key, changes = row.Value.Count, changeObjectBytes = row.Value.ChangeBytes, valueBytes = row.Value.ValueBytes }) };
    }
}
