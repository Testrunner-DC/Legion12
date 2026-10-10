using System.Text;
using TwelveLegions.Server;

Console.Title = "Twelve Legions WebSocket Server";
Console.OutputEncoding = Encoding.UTF8;

// Reject an invalid storage mode before creating runtime directories or opening a database.
var privateDeckObjectPersistenceEnabled = L12PrivateDeckPersistenceStartup.Parse(
    Environment.GetEnvironmentVariable(L12PrivateDeckPersistenceStartup.EnvironmentKey));
var deploymentDrainEnabled = L12DeploymentDrainStartup.Parse(
    Environment.GetEnvironmentVariable(L12DeploymentDrainStartup.EnvironmentKey));

var port = args.FirstOrDefault(argument => int.TryParse(argument, out _)) is { } portArgument
    && int.TryParse(portArgument, out var parsedPort) ? parsedPort : 8080;
var dataPath = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data");
var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime");
var runtimeBuild = L12RuntimeBuildVersion.Capture();
var processMetricInstance = Guid.NewGuid();
Directory.CreateDirectory(runtimePath);
var deploymentDrain = L12DeploymentDrainStartup.Create(deploymentDrainEnabled, runtimePath,
    runtimeBuild);

var ephemeralTestMatches = L12TestRunStorageProfile.Prepare(runtimePath,
    Environment.GetEnvironmentVariable(L12TestRunStorageProfile.EnvironmentKey),
    Environment.GetEnvironmentVariable("L12_PUBLIC_BASE_URL"));
if (ephemeralTestMatches)
    Console.WriteLine("Test-run match storage: ephemeral; previous match, replay and analytics data cleared.");

var catalog = L12Catalog.Load(dataPath);
var platform = new L12PlatformStore(Path.Combine(runtimePath, "platform.json"), catalog.PresetDecks,
    officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
    officialCardProducts: catalog.CardProducts);
platform.ApplyPrivateDeckPersistenceStartup(privateDeckObjectPersistenceEnabled);
if (L12TestRunStorageProfile.AcceptanceDataEnabled(ephemeralTestMatches,
        Environment.GetEnvironmentVariable(L12TestRunStorageProfile.AcceptanceDataEnvironmentKey)))
{
    var fixtures = platform.EnsureTestRunAcceptanceFixtures();
    Console.WriteLine($"Test-run acceptance data: owner={fixtures.Owner}; "
                      + $"decks={fixtures.Decks}; publicDecks={fixtures.PublicDecks}; "
                      + $"guidedDecks={fixtures.GuidedDecks}; rankedPlayers={fixtures.RankedPlayers}; "
                      + $"rankedMatches={fixtures.RankedMatches}; activeMasters={fixtures.ActiveMasters}; "
                      + $"historicalHonors={fixtures.HistoricalHonors}.");
}

var bootstrapIndex = Array.FindIndex(args,
    argument => string.Equals(argument, "--bootstrap-second-approver", StringComparison.Ordinal));
if (bootstrapIndex >= 0)
{
    if (bootstrapIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[bootstrapIndex + 1]))
    {
        Console.Error.WriteLine("bootstrap_target_required: 请提供目标账号 ID");
        Environment.ExitCode = 2;
        return;
    }
    var credential = Environment.GetEnvironmentVariable("L12_SECOND_APPROVER_BOOTSTRAP_TOKEN") ?? string.Empty;
    var outcome = platform.BootstrapSecondApprover(args[bootstrapIndex + 1], credential);
    if (outcome.Success && outcome.Value is { } value)
        Console.WriteLine($"{outcome.Code}: {outcome.Message}; account={value.AccountId}; role={value.Role}; replayed={outcome.Replayed}");
    else
        Console.Error.WriteLine($"{outcome.Code}: {outcome.Message}");
    Environment.ExitCode = outcome.Success ? 0 : 3;
    return;
}

await using var recorder = new MatchRecorder(Path.Combine(runtimePath, "matches.db"));
await recorder.InitializeAsync();

var rooms = new L12RoomManager(catalog, recorder, platform);
if (deploymentDrain is not null) rooms.AttachDeploymentDrain(deploymentDrain);
var rankedRecovery = await rooms.RestoreRankedRoomsAsync();
Console.WriteLine($"Ranked recovery: settlements={rankedRecovery.SettlementsApplied}, "
                  + $"rooms={rankedRecovery.Restored}, invalid={rankedRecovery.Invalidated}, "
                  + $"failed={rankedRecovery.Failed}");
var rankedMasterTitleFacts = await recorder.ListRankedMasterTitleFactsAsync(DateTimeOffset.UtcNow);
var importedRankedMasterTitleFacts = platform.ImportRankedMasterTitleFacts(rankedMasterTitleFacts);
Console.WriteLine($"Ranked master title facts: authoritative={rankedMasterTitleFacts.Count}, "
                  + $"imported={importedRankedMasterTitleFacts}");
platform.ImportRankedMasterHistory(await recorder.ListRankingMatchesAsync(2000));
await using var server = new L12WebSocketServer(rooms, recorder, platform, catalog);

Console.WriteLine("Twelve Legions online battle server");
Console.WriteLine($"Loaded {catalog.Cards.Count} S1-S2 cards and {catalog.PresetDecks.Count} preset decks.");
await server.StartAsync(port);
var processMetricStart = L12ProcessMetricArchiveStartup.TryStart(runtimePath,
    runtimeBuild.ServerRelease, processMetricInstance,
    reason => Console.Error.WriteLine($"Process metrics degraded: {reason}."));
var processMetricRuntime = processMetricStart.Runtime;
if (!processMetricStart.Started)
    Console.Error.WriteLine($"Process metrics unavailable: {processMetricStart.Reason}.");

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;
Console.WriteLine("Stopping server...");
var processMetricStop = processMetricRuntime?.StopAsync(TimeSpan.FromSeconds(2));
try { await server.StopAsync(); }
finally
{
    if (processMetricStop is not null && !await processMetricStop)
        Console.Error.WriteLine($"Process metrics stopped: {L12ProcessMetricStartupReason.ShutdownIncomplete}.");
}
