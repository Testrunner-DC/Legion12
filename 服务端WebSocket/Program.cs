using System.Text;
using TwelveLegions.Server;

Console.Title = "Twelve Legions WebSocket Server";
Console.OutputEncoding = Encoding.UTF8;

var port = args.FirstOrDefault(argument => int.TryParse(argument, out _)) is { } portArgument
    && int.TryParse(portArgument, out var parsedPort) ? parsedPort : 8080;
var dataPath = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data");
var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime");
Directory.CreateDirectory(runtimePath);

var catalog = L12Catalog.Load(dataPath);
var platform = new L12PlatformStore(Path.Combine(runtimePath, "platform.json"), catalog.PresetDecks,
    officialCards: catalog.Cards);

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
var rankedRecovery = await rooms.RestoreRankedRoomsAsync();
Console.WriteLine($"Ranked recovery: settlements={rankedRecovery.SettlementsApplied}, "
                  + $"rooms={rankedRecovery.Restored}, invalid={rankedRecovery.Invalidated}, "
                  + $"failed={rankedRecovery.Failed}");
platform.ImportRankedMasterHistory(await recorder.ListRankingMatchesAsync(2000));
await using var server = new L12WebSocketServer(rooms, recorder, platform, catalog);

Console.WriteLine("Twelve Legions online battle server");
Console.WriteLine($"Loaded {catalog.Cards.Count} S1-S2 cards and {catalog.PresetDecks.Count} preset decks.");
await server.StartAsync(port);

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;
Console.WriteLine("Stopping server...");
await server.StopAsync();
