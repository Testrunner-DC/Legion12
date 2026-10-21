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
    || !Guid.TryParseExact(owner.RootElement.GetProperty("runId").GetString(), "N", out var runId)
    || new DirectoryInfo(root).LinkTarget is not null
    || !root.StartsWith(Path.GetFullPath(owner.RootElement.GetProperty("cacheParent").GetString()!)
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Owned local root mismatch");
var expectedSha = owner.RootElement.GetProperty("serverSha256").GetString();
var actualSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(accepted, "GrandUMIServer.dll"))));
if (actualSha != expectedSha) throw new InvalidOperationException("Accepted server drift");
if (Environment.GetEnvironmentVariable("L12_LISTEN_HOST") != "127.0.0.1")
    throw new InvalidOperationException("Local host must bind IPv4 loopback only");
var fixtureMode = Environment.GetEnvironmentVariable("L12_LOCAL_EFFECT_FIXTURE")?.Trim();
fixtureMode = string.IsNullOrEmpty(fixtureMode) ? null : fixtureMode;
if (fixtureMode is not null
    && fixtureMode != "merlin-public-target-v1"
    && fixtureMode != "ankh-mandatory-target-v1")
    throw new InvalidOperationException("Unknown local effect fixture");
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
    var syntheticHistoryMatches = fixtureMode is null ? 16 : 0;
    for (var index = 0; index < syntheticHistoryMatches; index++)
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
    object? effectFixture = fixtureMode switch
    {
        "merlin-public-target-v1" =>
            await CreateMerlinPublicTargetFixtureAsync(catalog, platform, recorder, rooms, runId, stateVersion),
        "ankh-mandatory-target-v1" =>
            await CreateAnkhMandatoryTargetFixtureAsync(catalog, platform, recorder, rooms, runId, stateVersion),
        _ => null,
    };
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
    object ready = effectFixture is null
        ? new { schema = 1, kind = "local-real-http-ready", pid = Environment.ProcessId,
            baseUrl = address.ToString(), serverSha256 = actualSha, syntheticHistoryMatches, fixtures }
        : new { schema = 1, kind = "local-real-http-ready", pid = Environment.ProcessId,
            baseUrl = address.ToString(), serverSha256 = actualSha, syntheticHistoryMatches, fixtures,
            effectFixture };
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

static async Task<object> CreateMerlinPublicTargetFixtureAsync(L12Catalog catalog, L12PlatformStore platform,
    MatchRecorder recorder, L12RoomManager rooms, Guid runId, int stateVersion)
{
    var suffix = runId.ToString("N")[..7];
    var password = Environment.GetEnvironmentVariable("L12_LOCAL_EFFECT_PASSWORD");
    Environment.SetEnvironmentVariable("L12_LOCAL_EFFECT_PASSWORD", null);
    if (string.IsNullOrWhiteSpace(password))
        throw new InvalidOperationException("Explicit local effect fixture password is required");
    var hostUsername = "qa" + suffix;
    var guestUsername = "qb" + suffix;
    var hostRegistration = platform.Register(hostUsername, password);
    var guestRegistration = platform.Register(guestUsername, password);
    if (!hostRegistration.Success || hostRegistration.Account is null
        || !guestRegistration.Success || guestRegistration.Account is null)
        throw new InvalidOperationException("Fixture account registration failed");

    var hostSession = Guid.NewGuid();
    var guestSession = Guid.NewGuid();
    rooms.Connect(hostSession, hostRegistration.Account.Id, hostUsername);
    rooms.Connect(guestSession, guestRegistration.Account.Id, guestUsername);
    var created = rooms.CreateRoom(hostSession);
    var roomCode = ReadRoomCode(created, hostSession);
    var joined = rooms.JoinRoom(guestSession, roomCode);
    if (joined.All(message => message.SessionId != guestSession))
        throw new InvalidOperationException("Fixture guest did not join the room");

    var room = FindOwnedRoom(rooms, roomCode);
    var roomType = room.GetType();
    var gate = (SemaphoreSlim)ReadRoomProperty(roomType, room, "Gate")!;
    var sessions = (System.Collections.ICollection)ReadRoomProperty(roomType, room, "Sessions")!;
    await gate.WaitAsync();
    string matchId;
    try
    {
        if ((bool)ReadRoomProperty(roomType, room, "IsSandbox")!
            || sessions.Count != 2
            || ReadRoomProperty(roomType, room, "Game") is not null
            || (long)ReadRoomProperty(roomType, room, "CommandSequence")! != 0)
            throw new InvalidOperationException("Fixture room was not pristine ordinary room state");

        var policy = (L12OperationsPolicySnapshot)ReadRoomProperty(roomType, room, "OperationsPolicy")!;
        var options = (L12RoomOptions)ReadRoomProperty(roomType, room, "Options")!;
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        matchId = "local-merlin-" + runId.ToString("N");
        var engine = new L12GameEngine(catalog, matchId, roomCode, 63082,
            [hostUsername, guestUsername], decks, skipPreparation: true,
            operationsPolicy: policy, stateFormatVersion: stateVersion);
        var first = engine.State.Players[0];
        var second = engine.State.Players[1];
        ClearFixturePlayer(first);
        ClearFixturePlayer(second);
        var merlin = FixtureCard(catalog, "S02-0603", "l12qa-merlin-source", 0);
        merlin.SummonRound = -1;
        var target = FixtureCard(catalog, "S02-0502", "l12qa-merlin-target", 1);
        target.SummonRound = -1;
        var response = FixtureCard(catalog, "S01-0016", "l12qa-merlin-response", 1);
        response.Hidden = true;
        response.SetRound = 0;
        response.SummonRound = -1;
        first.Field[0][0] = merlin;
        first.SpecialZones.Runes = 1;
        second.Field[0][0] = target;
        second.Field[1][0] = response;
        second.Hand.Add(FixtureCard(catalog, "S01-0003", "l12qa-merlin-response-discard", 1));
        engine.State.ActivePlayer = 0;
        engine.State.FirstPlayer = 0;
        engine.State.Round = 2;
        engine.State.TurnSerial = 2;
        engine.State.Phase = L12Phase.Main;

        await recorder.StartAsync(engine, options.MatchModeId, hostRegistration.Account.Id,
            guestRegistration.Account.Id, decks);
        roomType.GetProperty("Game")!.SetValue(room, engine);
        var ready = (bool[])ReadRoomProperty(roomType, room, "Ready")!;
        ready[0] = ready[1] = true;
    }
    finally { gate.Release(); }

    rooms.Disconnect(hostSession);
    rooms.Disconnect(guestSession);
    return new
    {
        schema = 1,
        kind = "merlin-public-target-v1",
        hostUsername,
        guestUsername,
        roomCode,
        matchId,
        sourceInstanceId = "l12qa-merlin-source",
        targetInstanceId = "l12qa-merlin-target",
        responseInstanceId = "l12qa-merlin-response",
        expectedTargetTroopsDelta = -3000,
    };
}

static async Task<object> CreateAnkhMandatoryTargetFixtureAsync(L12Catalog catalog, L12PlatformStore platform,
    MatchRecorder recorder, L12RoomManager rooms, Guid runId, int stateVersion)
{
    var suffix = runId.ToString("N")[..7];
    var password = Environment.GetEnvironmentVariable("L12_LOCAL_EFFECT_PASSWORD");
    Environment.SetEnvironmentVariable("L12_LOCAL_EFFECT_PASSWORD", null);
    if (string.IsNullOrWhiteSpace(password))
        throw new InvalidOperationException("Explicit local effect fixture password is required");
    var hostUsername = "qc" + suffix;
    var guestUsername = "qd" + suffix;
    var hostRegistration = platform.Register(hostUsername, password);
    var guestRegistration = platform.Register(guestUsername, password);
    if (!hostRegistration.Success || hostRegistration.Account is null
        || !guestRegistration.Success || guestRegistration.Account is null)
        throw new InvalidOperationException("Fixture account registration failed");

    var hostSession = Guid.NewGuid();
    var guestSession = Guid.NewGuid();
    rooms.Connect(hostSession, hostRegistration.Account.Id, hostUsername);
    rooms.Connect(guestSession, guestRegistration.Account.Id, guestUsername);
    var created = rooms.CreateRoom(hostSession);
    var roomCode = ReadRoomCode(created, hostSession);
    var joined = rooms.JoinRoom(guestSession, roomCode);
    if (joined.All(message => message.SessionId != guestSession))
        throw new InvalidOperationException("Fixture guest did not join the room");

    var room = FindOwnedRoom(rooms, roomCode);
    var roomType = room.GetType();
    var gate = (SemaphoreSlim)ReadRoomProperty(roomType, room, "Gate")!;
    var sessions = (System.Collections.ICollection)ReadRoomProperty(roomType, room, "Sessions")!;
    await gate.WaitAsync();
    string matchId;
    try
    {
        if ((bool)ReadRoomProperty(roomType, room, "IsSandbox")!
            || sessions.Count != 2
            || ReadRoomProperty(roomType, room, "Game") is not null
            || (long)ReadRoomProperty(roomType, room, "CommandSequence")! != 0)
            throw new InvalidOperationException("Fixture room was not pristine ordinary room state");

        var policy = (L12OperationsPolicySnapshot)ReadRoomProperty(roomType, room, "OperationsPolicy")!;
        var options = (L12RoomOptions)ReadRoomProperty(roomType, room, "Options")!;
        var decks = new[] { catalog.DeckAt(2), catalog.DeckAt(1) };
        matchId = "local-ankh-" + runId.ToString("N");
        var engine = new L12GameEngine(catalog, matchId, roomCode, 63083,
            [hostUsername, guestUsername], decks, skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            operationsPolicy: policy, stateFormatVersion: stateVersion);
        var first = engine.State.Players[0];
        var second = engine.State.Players[1];
        ClearFixturePlayer(first);
        ClearFixturePlayer(second);
        first.Library.Clear();
        first.Morale.Clear();
        first.MoraleDeck.Clear();
        second.Library.Clear();
        second.Morale.Clear();
        second.MoraleDeck.Clear();

        var source = FixtureCard(catalog, "S01-0215", "l12qa-ankh-source", 0);
        var target = FixtureCard(catalog, "S01-0212", "l12qa-ankh-guard", 0);
        target.SummonRound = -1;
        first.Hand.Add(source);
        first.Field[0][0] = target;
        var ordinaryMoraleInstanceIds = new[] { "l12qa-ankh-morale-1", "l12qa-ankh-morale-2" };
        foreach (var instanceId in ordinaryMoraleInstanceIds)
            first.Morale.Add(new L12MoraleCard { CardId = "S01-02C1", InstanceId = instanceId });
        engine.State.ActivePlayer = 0;
        engine.State.FirstPlayer = 0;
        engine.State.Round = 2;
        engine.State.TurnSerial = 2;
        engine.State.Phase = L12Phase.Main;

        await recorder.StartAsync(engine, options.MatchModeId, hostRegistration.Account.Id,
            guestRegistration.Account.Id, decks);
        roomType.GetProperty("Game")!.SetValue(room, engine);
        var ready = (bool[])ReadRoomProperty(roomType, room, "Ready")!;
        ready[0] = ready[1] = true;
    }
    finally { gate.Release(); }

    rooms.Disconnect(hostSession);
    rooms.Disconnect(guestSession);
    return new
    {
        schema = 1,
        kind = "ankh-mandatory-target-v1",
        hostUsername,
        guestUsername,
        roomCode,
        matchId,
        sourceInstanceId = "l12qa-ankh-source",
        targetInstanceId = "l12qa-ankh-guard",
        ordinaryMoraleInstanceIds = new[] { "l12qa-ankh-morale-1", "l12qa-ankh-morale-2" },
        sourcePrintedCost = 2,
        expectedTargetTroopsDelta = 2000,
        forcedManualResponsePasses = true,
    };
}

static string ReadRoomCode(IReadOnlyList<OutgoingMessage> messages, Guid sessionId)
{
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    foreach (var message in messages.Where(item => item.SessionId == sessionId))
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message.Payload, options));
        if (document.RootElement.TryGetProperty("type", out var type)
            && type.GetString() == "roomState"
            && document.RootElement.TryGetProperty("roomCode", out var roomCode))
            return roomCode.GetString() ?? throw new InvalidOperationException("Fixture room code was empty");
    }
    throw new InvalidOperationException("Fixture room creation did not return roomState");
}

static object FindOwnedRoom(L12RoomManager rooms, string roomCode)
{
    var roomMap = typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(rooms) as System.Collections.IEnumerable
        ?? throw new InvalidOperationException("Fixture room map unavailable");
    foreach (var entry in roomMap)
    {
        var entryType = entry!.GetType();
        if (string.Equals(entryType.GetProperty("Key")!.GetValue(entry) as string, roomCode,
                StringComparison.Ordinal))
            return entryType.GetProperty("Value")!.GetValue(entry)
                   ?? throw new InvalidOperationException("Fixture room value unavailable");
    }
    throw new InvalidOperationException("Owned fixture room was not found");
}

static object? ReadRoomProperty(Type roomType, object room, string name)
    => roomType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
        .GetValue(room);

static void ClearFixturePlayer(L12PlayerState player)
{
    player.Hand.Clear();
    player.Relic = null;
    player.ExtraRelics.Clear();
    player.Resolving.Clear();
    player.Graveyard.Clear();
    player.Removed.Clear();
    player.SpecialZones.Runes = 0;
    player.SpecialZones.GodPower.Clear();
    player.SpecialZones.Trials.Clear();
    player.SpecialZones.CanopicProgress.Clear();
    for (var row = 0; row < player.Field.Length; row++)
        Array.Clear(player.Field[row]);
}

static L12CardInstance FixtureCard(L12Catalog catalog, string cardId, string instanceId, int ownerIndex)
{
    var definition = catalog.Cards[cardId];
    return new L12CardInstance
    {
        InstanceId = instanceId,
        CardId = definition.Id,
        Name = definition.NameZh,
        CardType = definition.CardType,
        IsCounterTactic = definition.IsCounterTactic,
        Faction = definition.Faction,
        ImageUrl = definition.ImageUrl,
        Cost = definition.Cost ?? 0,
        HasPrintedCost = definition.Cost.HasValue,
        EffectText = definition.Effect,
        Traits = [.. definition.Traits],
        Profession = definition.Profession,
        EffectiveProfession = definition.Profession,
        BaseTroops = definition.Troops ?? 0,
        Troops = definition.Troops ?? 0,
        DisasterLevel = definition.DisasterLevel ?? 0,
        TrialValue = definition.TrialValue ?? 0,
        CannotAttack = definition.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
        OwnerIndex = ownerIndex,
    };
}

static string AssertLoopback(string value)
{
    var uri = new Uri(value);
    if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port <= 0)
        throw new InvalidOperationException("Non-loopback listener rejected");
    return value;
}
