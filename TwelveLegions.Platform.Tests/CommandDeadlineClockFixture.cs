using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;

namespace TwelveLegions.Platform.Tests;

internal sealed class CommandDeadlineClockFixture : IAsyncDisposable
{
    internal static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    internal DateTimeOffset Now = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
    internal readonly int RecorderDelayMs;
    internal readonly int FirstPlayer;
    internal readonly L12Catalog Catalog;
    internal readonly L12PresetDeckDefinition Deck;
    internal readonly L12GameEngine Engine;
    internal readonly MatchRecorder Recorder;

    private CommandDeadlineClockFixture(int firstPlayer, int recorderDelayMs)
    {
        FirstPlayer = firstPlayer;
        RecorderDelayMs = recorderDelayMs;
        Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var preset = Catalog.DeckAt(0);
        Deck = new L12PresetDeckDefinition
        {
            Name = "synthetic-clock-boundary", MasterId = "S02-03M1",
            CardIds = [.. preset.CardIds], MoraleIds = [.. preset.MoraleIds], SpecialIds = [],
        };
        Engine = new L12GameEngine(Catalog, "clock-" + Guid.NewGuid().ToString("N"), "CLOCK01", 810876,
            ["synthetic-first", "synthetic-second"], [Deck, Deck], skipPreparation: true,
            stateFormatVersion: 2, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => Now);
        Engine.State.Phase = L12Phase.Main;
        Engine.State.Round = 1;
        foreach (var player in Engine.State.Players)
        {
            player.Library.AddRange(player.Hand);
            player.Hand.Clear();
            foreach (var row in player.Field) player.Library.AddRange(row.OfType<L12CardInstance>());
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        SeedFreshWindow(Engine, firstPlayer);
        var directory = Path.Combine(Path.GetTempPath(), "l12-command-clock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Recorder = new MatchRecorder(Path.Combine(directory, "matches.db"), () => RecorderNow);
        Recorder.AttachCatalog(Catalog);
    }

    internal DateTimeOffset RecorderNow => Now.AddMilliseconds(RecorderDelayMs);

    internal static async Task<CommandDeadlineClockFixture> CreateAsync(int firstPlayer, int delayMs,
        bool ranked = false)
    {
        var fixture = new CommandDeadlineClockFixture(firstPlayer, delayMs);
        await fixture.Recorder.InitializeAsync();
        if (ranked)
            await fixture.Recorder.StartRankedAsync(fixture.Engine, "clock-account-a", "clock-account-b",
                [fixture.Deck, fixture.Deck], fixture.Runtime(0));
        else
            await fixture.Recorder.StartAsync(fixture.Engine, "friendly", "clock-account-a", "clock-account-b",
                [fixture.Deck, fixture.Deck]);
        return fixture;
    }

    // Seed an initial state, not a second rule implementation. Every transition
    // under test is the real public Handle -> ResolvePrompt -> PassPriority flow.
    internal static void SeedFreshWindow(L12GameEngine engine, int firstPlayer)
    {
        engine.State.PendingPrompts.Clear();
        engine.State.EffectStack.Clear();
        engine.State.ActivePlayer = firstPlayer;
        var modes = new[] { L12GameEngine.DefaultResponseMode, L12GameEngine.DefaultResponseMode };
        modes[1 - firstPlayer] = L12GameEngine.InvalidFiveSecondsResponseMode;
        engine.State.PlayerResponseModes = modes;
        engine.State.ResponseWindow = new L12ResponseWindow
        {
            PriorityPlayer = firstPlayer, ConsecutivePasses = 0, FrozenPlayerResponseModes = [.. modes],
        };
        var stack = new L12StackItem
        {
            StackItemId = "seeded-clock-stack", Controller = firstPlayer,
            SourceInstanceId = "seeded-clock-source", SourceCardId = "S01-0103",
            SourceName = "synthetic-source", Trigger = "reaction", Text = "synthetic initial response",
        };
        engine.State.EffectStack.Add(stack);
        engine.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "seeded-clock-prompt", PlayerIndex = firstPlayer, Kind = "response",
            Text = "synthetic initial response", ValidChoices = ["pass"], MinChoose = 1, MaxChoose = 1,
            Continuation = "stack-response", StackItemId = stack.StackItemId, IsPrivate = true,
        });
    }

    internal L12Command PassCommand(L12GameEngine? engine = null)
    {
        var prompt = (engine ?? Engine).State.PendingPrompts.Single();
        return new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass");
    }

    internal async Task AppendAsync(long sequence, L12Command command, CommandResult result, bool ranked = false)
    {
        var json = JsonSerializer.Serialize(command, WireJson);
        if (ranked)
            await Recorder.AppendRankedAsync(Engine, sequence, FirstPlayer, json, result,
                Runtime(sequence), settlement: null);
        else
            await Recorder.AppendAsync(Engine, sequence, FirstPlayer, json, result);
    }

    private L12RankedRuntimeCheckpoint Runtime(long sequence)
        => new(1, Engine.State.MatchId, Engine.State.RoomCode, "active", sequence + 1,
            sequence, Engine.State.Revision, Engine.ComputeStateHash(), RecorderNow, 0,
            [900_000, 900_000], [120_000, 120_000], [false, false], RecorderNow, null, false,
            [true, true], [null, null], ["clock-integrity-a", "clock-integrity-b"], [1, 1], RecorderNow,
            L12PlatformStore.DefaultRankedTimeControl(), ["clock-browser-a", "clock-browser-b"]);

    internal static L12GameEngine RestoredEngine(L12RoomManager manager)
    {
        var rooms = typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        var values = (System.Collections.IEnumerable)rooms.GetType().GetProperty("Values")!.GetValue(rooms)!;
        var room = values.Cast<object>().Single();
        return (L12GameEngine)room.GetType().GetProperty("Game")!.GetValue(room)!;
    }

    public ValueTask DisposeAsync() => Recorder.DisposeAsync();
}
