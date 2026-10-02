using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;

if (args.Length is < 1 or > 2)
    throw new ArgumentException("Pass a D:\\GPT\\Legion12\\artifacts\\platform-growth output directory and optionally --quick or --private-object");
var quick = args.Length == 2 && string.Equals(args[1], "--quick", StringComparison.Ordinal);
var privateObject = args.Length == 2 && string.Equals(args[1], "--private-object", StringComparison.Ordinal);
if (args.Length == 2 && !quick && !privateObject)
    throw new ArgumentException("The optional argument must be --quick or --private-object");
var outputRoot = Path.GetFullPath(args[0]);
var allowedRoot = @"D:\GPT\Legion12\artifacts\platform-growth";
if (!outputRoot.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase)
    && !outputRoot.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Synthetic output must remain under D:\\GPT\\Legion12\\artifacts\\platform-growth");

var runPrefix = privateObject ? "run-f2-private-object" : "run-f2";
var runRoot = Path.Combine(outputRoot, $"{runPrefix}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}");
Directory.CreateDirectory(runRoot);
const string password = "BenchmarkPassword123";
var samplesPerOperation = quick ? 5 : 100;
var repeats = quick ? 1 : 5;
var seedPath = Path.Combine(runRoot, "seed", "platform.json");
var seed = new L12PlatformStore(seedPath);
var registration = seed.Register("BenchOwner", password);
if (!registration.Success || registration.Account is null) throw new InvalidOperationException(registration.Message);
var owner = registration.Account;
var targetDeck = new L12PresetDeckDefinition
{
    Name = "Benchmark Deck",
    MasterId = "S01-01M1",
    CardIds = Enumerable.Repeat("S01-0001", 40).ToList(),
    MoraleIds = Enumerable.Repeat("S01-01C1", 8).ToList(),
};
seed.UpsertDeck(owner.Id, targetDeck);
seed.PublishDeck(owner.Id, targetDeck, null);
seed.CreateTournament(owner, new L12TournamentCreatePayload("Benchmark Tournament", "single", "public", 32,
        DateTimeOffset.UtcNow.AddDays(1), "S01", "synthetic", "after", "season", "", 50, 5),
    new L12AdminAuditContext("benchmark-seed", "tournaments.manage", RequestMethod: "BENCH",
        RequestPath: "/bench"), true);
var template = JsonNode.Parse(File.ReadAllText(seedPath))!.AsObject();
// The compatibility mirror intentionally omits the normalized deck domain. Synthetic
// legacy rows are injected before first open; production data is never read.
template["Decks"] = new JsonArray();
template["PublishedDecks"] = new JsonArray();
var accountTemplate = template["Accounts"]!.AsArray()
    .Single(item => (string?)item?["Username"] == "BenchOwner")!;
var tournamentTemplate = template["Tournaments"]!.AsArray().Single()!;
var deckTemplate = JsonSerializer.SerializeToNode(new
{
    AccountId = owner.Id,
    targetDeck.Name,
    targetDeck.MasterId,
    targetDeck.CardIds,
    targetDeck.MoraleIds,
    targetDeck.SpecialIds,
    BenchIds = Array.Empty<string>(),
    AlternateArtSelections = new Dictionary<string, string>(),
    AlternateArtCopies = new Dictionary<string, string[]>(),
    UpdatedAt = DateTimeOffset.UtcNow,
})!;
var publishedTemplate = JsonSerializer.SerializeToNode(new
{
    Id = "synthetic-seed",
    PublicCode = "23456789ABCD",
    OwnerId = owner.Id,
    targetDeck.Name,
    targetDeck.MasterId,
    targetDeck.CardIds,
    targetDeck.MoraleIds,
    targetDeck.SpecialIds,
    AlternateArtSelections = new Dictionary<string, string>(),
    AlternateArtCopies = new Dictionary<string, string[]>(),
    LikedByAccountIds = Array.Empty<string>(),
    Views = 0,
    Copies = 0,
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow,
})!;

var baseline = new MatrixScale(50, 200, 20, 10, 1);
var axes = quick || privateObject
    ? new[] { new MatrixAxis("privateDeckRows", [20, 1200]) }
    : new[]
    {
        new MatrixAxis("accounts", [10, 50, 150, 500]),
        new MatrixAxis("privateDeckRows", [20, 200, 1200]),
        new MatrixAxis("publications", [5, 20, 60, 250]),
        new MatrixAxis("tournamentDeckRefs", [2, 10, 30, 100]),
        new MatrixAxis("distinctPayloads", [1, 20, 200]),
    };
var results = new List<MatrixResult>();
foreach (var axis in axes)
foreach (var level in axis.Levels)
foreach (var repeat in Enumerable.Range(1, repeats))
{
    var scale = axis.Name switch
    {
        "accounts" => baseline with { Accounts = level },
        "privateDeckRows" => baseline with { PrivateDeckRows = level },
        "publications" => baseline with { Publications = level },
        "tournamentDeckRefs" => baseline with { TournamentDeckRefs = level },
        "distinctPayloads" => baseline with { DistinctPayloads = level },
        _ => throw new InvalidOperationException($"Unknown axis {axis.Name}"),
    };
    var caseRoot = Path.Combine(runRoot, $"{axis.Name}-{level}-r{repeat}");
    var data = BuildData(template, accountTemplate, deckTemplate, publishedTemplate, tournamentTemplate, scale);
    var login = RunOperation(caseRoot, "login", data, scale, samplesPerOperation, false, store =>
    {
        if (!store.Login("Bench000000", password).Success) throw new InvalidOperationException("Login failed");
    });
    var save = RunOperation(caseRoot, "save", data, scale, samplesPerOperation, privateObject,
        store => store.UpsertDeck("synthetic-account-000000", targetDeck));
    var like = RunOperation(caseRoot, "like", data, scale, samplesPerOperation, false, store =>
    {
        if (store.TogglePublishedDeckLike("synthetic-account-000000", "synthetic-public-000000") is null)
            throw new InvalidOperationException("Like failed");
    });
    var actor = new L12AccountView("benchmark-admin", "Benchmark Admin", "admin", DateTimeOffset.UtcNow, true);
    var admin = RunOperation(caseRoot, "admin-audit", data, scale, samplesPerOperation, false, store =>
    {
        if (!store.SetRole(actor, "synthetic-account-000000", "player"))
            throw new InvalidOperationException("Admin mutation failed");
    });
    results.Add(new MatrixResult(axis.Name, level, repeat, scale, login.Summary, save.Summary, like.Summary,
        admin.Summary, save.RecoveryMs, save.DatabaseBytes, save.MirrorBytes, save.WalBytes));
    Console.WriteLine($"{axis.Name}={level} r{repeat}: save P95={save.Summary.LatencyMs.P95} ms; "
                      + $"deck statements P50={save.Summary.DeckDomainStatements.P50}; "
                      + $"affected rows P50={save.Summary.InclusiveAffectedRows.P50}");
}

var reportPath = Path.Combine(runRoot, "report.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(new
{
    Schema = 3,
    AtUtc = DateTimeOffset.UtcNow,
    GitCommit = Environment.GetEnvironmentVariable("L12_BENCH_COMMIT") ?? "uncommitted-candidate",
    Machine = new { Environment.MachineName, OS = Environment.OSVersion.ToString(), Environment.ProcessorCount },
    Profile = privateObject ? "private-object-candidate" : quick ? "quick-non-decision" : "full",
    PrivateDeckObjectPersistence = privateObject,
    SamplesPerOperation = samplesPerOperation,
    IndependentRuns = repeats,
    SyntheticOnly = true,
    Measurement = new
    {
        Sql = "sqlite3_profile classifies completed unexpanded statements in memory; SQL text and values are discarded",
        Rows = "sqlite3_changes by DML verb plus sqlite3_total_changes inclusive connection total",
        SqliteBytes = "SQLITE_DBSTATUS_CACHE_WRITE pages multiplied by PRAGMA page_size; WAL page payload, not host/device IO",
        MirrorBytes = "UTF-8 byte count of each successfully replaced compatibility mirror",
    },
    Threshold = new
    {
        Axis = "privateDeckRows",
        Compare = "1200 versus 20",
        Latency = privateObject
            ? "candidate 1200-row save P95 must be no more than 1.25x the 20-row P95 and no more than 15 ms on this reference host"
            : "isolated save P95 must be at least 2x and 10 ms higher in at least 4 of 5 runs",
        Amplification = privateObject
            ? "candidate deck-domain SQL and inclusive affected-row counts must have zero slope from 20 to 1200 rows"
            : "at least 0.8 extra affected deck row per added non-target row, or one save rewrites at least 50% of untouched private rows",
        Decision = privateObject
            ? "Candidate acceptance requires zero private-row/SQL slope and bounded latency; feature remains disabled by default"
            : "Only both latency and amplification authorize a narrow migration proposal; this report never performs migration",
    },
    Results = results,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(reportPath);

static JsonObject BuildData(JsonObject source, JsonNode accountTemplate, JsonNode deckTemplate,
    JsonNode publishedTemplate, JsonNode tournamentTemplate, MatrixScale scale)
{
    var data = (JsonObject)source.DeepClone();
    var accounts = data["Accounts"]!.AsArray();
    var decks = data["Decks"]!.AsArray();
    var publications = data["PublishedDecks"]!.AsArray();
    var tournaments = data["Tournaments"]!.AsArray();
    accounts.Clear();
    decks.Clear();
    publications.Clear();
    tournaments.Clear();
    for (var index = 0; index < scale.Accounts; index++)
    {
        var account = (JsonObject)accountTemplate.DeepClone();
        account["Id"] = $"synthetic-account-{index:000000}";
        account["Username"] = $"Bench{index:000000}";
        accounts.Add(account);
    }
    for (var index = 0; index < scale.PrivateDeckRows; index++)
    {
        var row = (JsonObject)deckTemplate.DeepClone();
        row["AccountId"] = $"synthetic-account-{index % scale.Accounts:000000}";
        row["Name"] = index == 0 ? "Benchmark Deck" : $"Deck {index:000000}";
        ApplyPayload(row, index % scale.DistinctPayloads);
        decks.Add(row);
    }
    for (var index = 0; index < scale.Publications; index++)
    {
        var row = (JsonObject)publishedTemplate.DeepClone();
        row["Id"] = $"synthetic-public-{index:000000}";
        row["PublicCode"] = $"23{index + 1:0000000000}";
        row["OwnerId"] = $"synthetic-account-{index % scale.Accounts:000000}";
        row["Name"] = $"Public {index:000000}";
        ApplyPayload(row, index % scale.DistinctPayloads);
        publications.Add(row);
    }
    for (var index = 0; index < scale.TournamentDeckRefs; index++)
    {
        var row = (JsonObject)tournamentTemplate.DeepClone();
        row["Id"] = $"synthetic-tournament-{index:000000}";
        row["Code"] = $"BENCH{index:000000}";
        row["Name"] = $"Tournament {index:000000}";
        row["OrganizerAccountId"] = $"synthetic-account-{index % scale.Accounts:000000}";
        var participant = row["Participants"]!.AsArray()[0]!.AsObject();
        participant["AccountId"] = $"synthetic-account-{index % scale.Accounts:000000}";
        var tournamentDeck = participant["Deck"]!.AsObject();
        tournamentDeck["MasterId"] = deckTemplate["MasterId"]!.DeepClone();
        tournamentDeck["CardIds"] = deckTemplate["CardIds"]!.DeepClone();
        tournamentDeck["MoraleIds"] = deckTemplate["MoraleIds"]!.DeepClone();
        tournamentDeck["SpecialIds"] = deckTemplate["SpecialIds"]!.DeepClone();
        ApplyPayload(tournamentDeck, index % scale.DistinctPayloads);
        tournaments.Add(row);
    }
    return data;
}

static void ApplyPayload(JsonObject row, int payloadIndex)
{
    if (payloadIndex == 0) return;
    row["MasterId"] = $"SYN-M-{payloadIndex:000000}";
    row["CardIds"] = new JsonArray(Enumerable.Range(0, 40)
        .Select(index => JsonValue.Create($"SYN-C-{payloadIndex:000000}-{index % 4}"))
        .ToArray<JsonNode?>());
    row["MoraleIds"] = new JsonArray(Enumerable.Range(0, 8)
        .Select(index => JsonValue.Create($"SYN-R-{payloadIndex:000000}-{index % 2}"))
        .ToArray<JsonNode?>());
}

static OperationRun RunOperation(string caseRoot, string operation, JsonObject source, MatrixScale scale, int samples,
    bool privateDeckObjectPersistence, Action<L12PlatformStore> action)
{
    var root = Path.Combine(caseRoot, operation);
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "platform.json");
    File.WriteAllText(path, source.ToJsonString());
    var store = new L12PlatformStore(path);
    store.PrivateDeckObjectPersistenceEnabled = privateDeckObjectPersistence;
    var storage = store.StorageStatus().DeckStorage
        ?? throw new InvalidDataException("Synthetic deck storage status is unavailable");
    if (storage.ActiveAccountDecks != scale.PrivateDeckRows
        || storage.ActivePublishedDecks != scale.Publications
        || storage.TournamentReferences != scale.TournamentDeckRefs)
        throw new InvalidDataException($"Synthetic scale mismatch: account decks={storage.ActiveAccountDecks}, "
            + $"publications={storage.ActivePublishedDecks}, tournament refs={storage.TournamentReferences}");
    action(store);
    var measured = new List<OperationSample>(samples);
    for (var index = 0; index < samples; index++)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        L12SyntheticStorageMeasurement measurement;
        string? error = null;
        using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
        {
            try { action(store); }
            catch (Exception exception) { error = exception.GetType().Name; }
            measurement = scope.Complete();
        }
        if (measurement.InstrumentationErrors > 0)
            error ??= $"InstrumentationErrors:{measurement.InstrumentationErrors}";
        clock.Stop();
        measured.Add(new OperationSample(clock.Elapsed.TotalMilliseconds,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore), error, measurement));
    }
    var database = store.TransactionalStoragePath;
    var recoveryClock = Stopwatch.StartNew();
    var reloaded = new L12PlatformStore(path);
    recoveryClock.Stop();
    if (reloaded.Decks("synthetic-account-000000").Count == 0)
        throw new InvalidDataException("Recovery lost the target account deck");
    return new OperationRun(Summarize(measured), Math.Round(recoveryClock.Elapsed.TotalMilliseconds, 3),
        new FileInfo(database).Length, new FileInfo(path).Length,
        File.Exists(database + "-wal") ? new FileInfo(database + "-wal").Length : 0);
}

static OperationSummary Summarize(IReadOnlyList<OperationSample> samples)
{
    var succeeded = samples.Where(sample => sample.Error is null).ToArray();
    if (succeeded.Length == 0) throw new InvalidOperationException("All benchmark samples failed");
    return new OperationSummary(samples.Count - succeeded.Length,
        Distribution(succeeded.Select(sample => sample.LatencyMs)),
        Distribution(succeeded.Select(sample => (double)sample.AllocatedBytes)),
        Distribution(succeeded.Select(sample => (double)(sample.Measurement.SelectStatements
            + sample.Measurement.InsertStatements + sample.Measurement.UpdateStatements
            + sample.Measurement.DeleteStatements + sample.Measurement.OtherStatements))),
        Distribution(succeeded.Select(sample => (double)sample.Measurement.DeckDomainStatements)),
        Distribution(succeeded.Select(sample => (double)sample.Measurement.AuditDomainStatements)),
        Distribution(succeeded.Select(sample => (double)sample.Measurement.InclusiveAffectedRows)),
        Distribution(succeeded.Select(sample => sample.Measurement.TransactionNanoseconds / 1_000_000d)),
        Distribution(succeeded.Select(sample => (double)sample.Measurement.SqlitePageWrites)),
        Distribution(succeeded.Select(sample => (double)sample.Measurement.SqlitePagePayloadBytes)),
        Distribution(succeeded.Select(sample => (double)sample.Measurement.MirrorBytes)));
}

static MetricDistribution Distribution(IEnumerable<double> values)
{
    var ordered = values.Order().ToArray();
    double At(double percentile) => Math.Round(ordered[Math.Clamp(
        (int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1)], 3);
    return new(Math.Round(ordered[0], 3), At(.5), At(.95), At(.99), Math.Round(ordered[^1], 3));
}

internal sealed record MatrixAxis(string Name, int[] Levels);
internal sealed record MatrixScale(int Accounts, int PrivateDeckRows, int Publications,
    int TournamentDeckRefs, int DistinctPayloads);
internal sealed record MetricDistribution(double Min, double P50, double P95, double P99, double Max);
internal sealed record OperationSummary(int Errors, MetricDistribution LatencyMs,
    MetricDistribution AllocatedBytes, MetricDistribution SqlStatements,
    MetricDistribution DeckDomainStatements, MetricDistribution AuditDomainStatements,
    MetricDistribution InclusiveAffectedRows, MetricDistribution TransactionMs,
    MetricDistribution SqlitePageWrites, MetricDistribution SqlitePagePayloadBytes,
    MetricDistribution MirrorBytesWritten);
internal sealed record OperationSample(double LatencyMs, long AllocatedBytes, string? Error,
    L12SyntheticStorageMeasurement Measurement);
internal sealed record OperationRun(OperationSummary Summary, double RecoveryMs,
    long DatabaseBytes, long MirrorBytes, long WalBytes);
internal sealed record MatrixResult(string Axis, int Level, int Repeat, MatrixScale Scale,
    OperationSummary Login, OperationSummary SaveDeck, OperationSummary Like,
    OperationSummary AdminAudit, double RecoveryMs, long DatabaseBytes, long MirrorBytes, long WalBytes);
