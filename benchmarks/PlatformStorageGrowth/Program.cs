using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;

if (args.Length != 1) throw new ArgumentException("Pass a D:\\GPT\\Legion12\\artifacts\\platform-growth output directory");
var outputRoot = Path.GetFullPath(args[0]);
var allowedRoot = @"D:\GPT\Legion12\artifacts\platform-growth";
if (!outputRoot.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase)
    && !outputRoot.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Synthetic output must remain under D:\\GPT\\Legion12\\artifacts\\platform-growth");
var runRoot = Path.Combine(outputRoot, $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}");
Directory.CreateDirectory(runRoot);
const string password = "BenchmarkPassword123";
var seedPath = Path.Combine(runRoot, "seed", "platform.json");
var seed = new L12PlatformStore(seedPath);
var registration = seed.Register("BenchOwner", password);
if (!registration.Success || registration.Account is null) throw new InvalidOperationException(registration.Message);
var owner = registration.Account;
var deck = new L12PresetDeckDefinition { Name = "Benchmark Deck", MasterId = "S01-01M1",
    CardIds = Enumerable.Repeat("S01-0001", 40).ToList(), MoraleIds = Enumerable.Repeat("S01-01C1", 8).ToList() };
seed.UpsertDeck(owner.Id, deck);
seed.PublishDeck(owner.Id, deck, null);
seed.CreateTournament(owner, new L12TournamentCreatePayload("Benchmark Tournament", "single", "public", 32,
    DateTimeOffset.UtcNow.AddDays(1), "S01", "synthetic", "after", "season", "", 50, 5),
    new L12AdminAuditContext("benchmark-seed", "tournaments.manage", RequestMethod: "BENCH", RequestPath: "/bench"), true);
var template = JsonNode.Parse(File.ReadAllText(seedPath))!.AsObject();
// The compatibility mirror intentionally omits the normalized deck domain. For the
// synthetic import fixture, add explicit legacy rows; no production data is read.
template["Decks"] = new JsonArray();
template["PublishedDecks"] = new JsonArray();
var deckTemplate = JsonSerializer.SerializeToNode(new {
    AccountId = owner.Id, deck.Name, deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds,
    BenchIds = Array.Empty<string>(), AlternateArtSelections = new Dictionary<string, string>(),
    AlternateArtCopies = new Dictionary<string, string[]>(), UpdatedAt = DateTimeOffset.UtcNow,
})!;
var publishedTemplate = JsonSerializer.SerializeToNode(new {
    Id = "synthetic-seed", PublicCode = "23456789ABCD", OwnerId = owner.Id, deck.Name,
    deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds,
    AlternateArtSelections = new Dictionary<string, string>(), AlternateArtCopies = new Dictionary<string, string[]>(),
    LikedByAccountIds = Array.Empty<string>(), Views = 0, Copies = 0,
    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
})!;
var gateField = typeof(L12PlatformStore).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new MissingFieldException("Platform store gate changed; revise lock timing");
var scales = new[] { (Accounts: 10, Decks: 2, Publications: 5, Tournaments: 2),
    (Accounts: 50, Decks: 4, Publications: 20, Tournaments: 10),
    (Accounts: 150, Decks: 8, Publications: 60, Tournaments: 30) };
var results = new List<object>();
foreach (var scale in scales)
{
    var levelRoot = Path.Combine(runRoot, $"a{scale.Accounts}-d{scale.Decks}-p{scale.Publications}-t{scale.Tournaments}");
    Directory.CreateDirectory(levelRoot);
    var path = Path.Combine(levelRoot, "platform.json");
    var data = (JsonObject)template.DeepClone();
    var accountTemplate = data["Accounts"]!.AsArray().Single(item => (string?)item?["Username"] == "BenchOwner")!;
    var tournamentTemplate = data["Tournaments"]!.AsArray().Single()!;
    var accounts = data["Accounts"]!.AsArray(); var decks = data["Decks"]!.AsArray();
    var publications = data["PublishedDecks"]!.AsArray(); var tournaments = data["Tournaments"]!.AsArray();
    accounts.Clear(); decks.Clear(); publications.Clear(); tournaments.Clear();
    for (var i = 0; i < scale.Accounts; i++)
    {
        var id = $"synthetic-account-{i:000000}";
        var account = (JsonObject)accountTemplate.DeepClone(); account["Id"] = id; account["Username"] = $"Bench{i:000000}";
        accounts.Add(account);
        for (var j = 0; j < scale.Decks; j++)
        {
            var row = (JsonObject)deckTemplate.DeepClone(); row["AccountId"] = id;
            row["Name"] = i == 0 && j == 0 ? "Benchmark Deck" : $"Deck {i:000000}-{j:00}";
            decks.Add(row);
        }
    }
    for (var i = 0; i < scale.Publications; i++)
    {
        var row = (JsonObject)publishedTemplate.DeepClone(); row["Id"] = $"synthetic-public-{i:000000}";
        row["PublicCode"] = $"23{(i + 1):0000000000}";
        row["OwnerId"] = $"synthetic-account-{i % scale.Accounts:000000}"; row["Name"] = $"Public {i:000000}";
        publications.Add(row);
    }
    for (var i = 0; i < scale.Tournaments; i++)
    {
        var row = (JsonObject)tournamentTemplate.DeepClone(); row["Id"] = $"synthetic-tournament-{i:000000}";
        row["Code"] = $"BENCH{i:000000}"; row["Name"] = $"Tournament {i:000000}";
        row["OrganizerAccountId"] = $"synthetic-account-{i % scale.Accounts:000000}"; tournaments.Add(row);
        row["Participants"]!.AsArray()[0]!["AccountId"] = $"synthetic-account-{i % scale.Accounts:000000}";
    }
    File.WriteAllText(path, data.ToJsonString());
    var open = Stopwatch.StartNew(); var store = new L12PlatformStore(path); open.Stop();
    var actor = new L12AccountView("benchmark-admin", "Benchmark Admin", "admin", DateTimeOffset.UtcNow, true);
    const string target = "synthetic-account-000000"; const string publicationId = "synthetic-public-000000";
    if (store.Decks(target).Count != scale.Decks || store.PublishedDecks(target).Count != scale.Publications
        || store.Tournaments(actor).Items.Count != scale.Tournaments) throw new InvalidDataException("Scale import mismatch");
    var login = Measure(100, () => { if (!store.Login("Bench000000", password).Success) throw new InvalidOperationException("Login failed"); });
    var save = Measure(100, () => store.UpsertDeck(target, deck));
    var like = Measure(100, () => { if (store.TogglePublishedDeckLike(target, publicationId) is null) throw new InvalidOperationException("Like failed"); });
    var admin = Measure(100, () => { if (!store.SetRole(actor, target, "player")) throw new InvalidOperationException("Admin mutation failed"); });
    var gate = gateField.GetValue(store)!;
    var start = new ManualResetEventSlim(false);
    var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
    {
        start.Wait(); var waits = new double[25];
        for (var j = 0; j < waits.Length; j++)
        {
            var begin = Stopwatch.GetTimestamp(); Monitor.Enter(gate); waits[j] = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
            try { if (!store.Login("Bench000000", password).Success) throw new InvalidOperationException("Contended login failed"); }
            finally { Monitor.Exit(gate); }
        }
        return waits;
    })).ToArray();
    start.Set(); var lockWait = (await Task.WhenAll(workers)).SelectMany(row => row).ToArray();
    var db = store.TransactionalStoragePath;
    var mirrorBytes = new FileInfo(path).Length; var databaseBytes = new FileInfo(db).Length;
    var walBytes = File.Exists(db + "-wal") ? new FileInfo(db + "-wal").Length : 0;
    var recovery = Stopwatch.StartNew(); var reloaded = new L12PlatformStore(path); recovery.Stop();
    if (reloaded.Decks(target).Count != scale.Decks) throw new InvalidDataException("Recovery mismatch");
    var metrics = new { scale.Accounts, DecksPerAccount = scale.Decks, scale.Publications, scale.Tournaments,
        AccountDeckRows = scale.Accounts * scale.Decks, ImportMs = Math.Round(open.Elapsed.TotalMilliseconds, 2),
        LoginMs = Percentiles(login), SaveDeckMs = Percentiles(save), LikeMs = Percentiles(like),
        AdminMutationMs = Percentiles(admin), ContendedGateWaitMs = Percentiles(lockWait),
        RecoveryMs = Math.Round(recovery.Elapsed.TotalMilliseconds, 2), MirrorBytes = mirrorBytes,
        DatabaseBytes = databaseBytes, WalBytes = walBytes, SqlStatementsExact = (int?)null,
        SqlStatementLowerBoundPerSnapshotSave = scale.Accounts * scale.Decks + scale.Publications + 1 };
    results.Add(metrics);
    Console.WriteLine($"{scale.Accounts} accounts / {metrics.AccountDeckRows} decks: save P95 {metrics.SaveDeckMs.P95} ms; lock wait P95 {metrics.ContendedGateWaitMs.P95} ms");
}
var reportPath = Path.Combine(runRoot, "report.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Schema = 1, AtUtc = DateTimeOffset.UtcNow,
    GitCommit = Environment.GetEnvironmentVariable("L12_BENCH_COMMIT") ?? "uncommitted-candidate",
    Machine = new { Environment.MachineName, OS = Environment.OSVersion.ToString(), Environment.ProcessorCount },
    SamplesPerOperation = 100, Concurrency = 4, SyntheticOnly = true,
    SqlStatementNote = "Exact SQL statement count is unavailable without store-level tracing. The lower bound includes per-deck/per-publication upserts and platform_state snapshot, but excludes schema checks, stale deletes, audit and lookups.",
    Results = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(reportPath);

static double[] Measure(int count, Action action)
{
    action(); var times = new double[count];
    for (var i = 0; i < count; i++) { var clock = Stopwatch.StartNew(); action(); clock.Stop(); times[i] = clock.Elapsed.TotalMilliseconds; }
    return times;
}
static Latency Percentiles(double[] values)
{
    var ordered = values.OrderBy(value => value).ToArray();
    double At(double p) => Math.Round(ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * p) - 1, 0, ordered.Length - 1)], 2);
    return new(At(.5), At(.95), At(.99));
}

readonly record struct Latency(double P50, double P95, double P99);
