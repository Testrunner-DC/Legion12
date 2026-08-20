using System.Text;
using TwelveLegions.Server;

Console.Title = "Twelve Legions WebSocket Server";
Console.OutputEncoding = Encoding.UTF8;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 8080;
var dataPath = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data");
var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime");
Directory.CreateDirectory(runtimePath);

var catalog = L12Catalog.Load(dataPath);
await using var recorder = new MatchRecorder(Path.Combine(runtimePath, "matches.db"));
await recorder.InitializeAsync();
var platform = new L12PlatformStore(Path.Combine(runtimePath, "platform.json"));

var rooms = new L12RoomManager(catalog, recorder);
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
