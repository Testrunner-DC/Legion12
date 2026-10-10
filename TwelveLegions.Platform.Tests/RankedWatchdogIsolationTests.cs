using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedWatchdogIsolationTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ThirtySecondBusyRoomDoesNotDelayHealthyDurableConclusionAndLateRoomConcludesOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var ordered = Rooms(fixture.Manager).ToArray();
        var blocked = ordered[0];
        var healthy = ordered[1];
        var blockedMatch = fixture.Matches.Single(match => match.RoomCode == Code(blocked));
        var healthyMatch = fixture.Matches.Single(match => match.RoomCode == Code(healthy));
        fixture.Manager.Disconnect(blockedMatch.FirstSession);
        fixture.Manager.Disconnect(healthyMatch.FirstSession);
        var baseline = (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(blockedMatch.MatchId))!;
        var gate = Gate(blocked);
        await gate.WaitAsync();
        var held = Stopwatch.StartNew();
        Task<IReadOnlyList<OutgoingMessage>>? tick = null;
        try
        {
            fixture.Now += TimeSpan.FromMinutes(5);
            tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
            var messages = await tick.WaitAsync(TimeSpan.FromSeconds(1));
            await AssertConcludedOnceAsync(fixture, healthyMatch);
            Assert.Contains(messages, message => JsonSerializer.SerializeToElement(message.Payload, WireJson)
                .TryGetProperty("type", out var type) && type.GetString() == "gameState");
            Assert.Null((await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!.Match.EndedUtc);
            Assert.Equal(baseline.CheckpointGeneration,
                (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(blockedMatch.MatchId))!.CheckpointGeneration);
            var remaining = TimeSpan.FromSeconds(30) - held.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            Assert.Null((await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!.Match.EndedUtc);
        }
        finally { gate.Release(); if (tick is not null) await tick.WaitAsync(TimeSpan.FromSeconds(5)); }
        fixture.Now += TimeSpan.FromSeconds(30);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => fixture.Manager.TickRankedClocksAsync(fixture.Now)));
        await AssertConcludedOnceAsync(fixture, blockedMatch);
        await AssertConcludedOnceAsync(fixture, healthyMatch);
        await using var reopened = new MatchRecorder(fixture.MatchPath);
        await reopened.InitializeAsync();
        var restored = new L12RoomManager(fixture.Catalog, reopened, fixture.Platform, () => fixture.Now);
        await restored.RestoreRankedRoomsAsync();
        Assert.Equal(0, await reopened.CountPendingRankedSettlementsAsync());
        foreach (var match in fixture.Matches)
            Assert.Single((await reopened.GetMatchAsync(match.MatchId))!.Commands,
                command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
    }

    [Fact]
    public async Task DeferredCohortSuppressesWholeOrdinaryCheckpointUntilBothRoomsAreReady()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.GenerationsAsync();
        var blocked = Rooms(fixture.Manager).First();
        var commits = 0;
        fixture.Recorder.StorageFailureInjector = phase =>
        {
            if (phase == "before-ranked-runtime-batch-commit") Interlocked.Increment(ref commits);
        };
        await Gate(blocked).WaitAsync();
        try
        {
            fixture.Now += TimeSpan.FromSeconds(1);
            await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(0, commits);
            Assert.Equal(before, await fixture.GenerationsAsync());
        }
        finally { Gate(blocked).Release(); }
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        Assert.Equal(1, commits);
        var after = await fixture.GenerationsAsync();
        Assert.All(Enumerable.Range(0, after.Length), index => Assert.True(after[index] > before[index]));
    }

    [Fact]
    public async Task SkippedRoomKeepsItsBaselineAndNextTickChargesAccumulatedUtcExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var room = Rooms(fixture.Manager).First();
        var match = fixture.Matches.Single(candidate => candidate.RoomCode == Code(room));
        var before = (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!;
        var actor = Enumerable.Range(0, 2).First(index => before.Acting[index]);
        Assert.True(before.OperationRemainingMs[actor] > 5000);
        var clock = room.GetType().GetProperty("RankedClock")!.GetValue(room)!;
        await Gate(room).WaitAsync();
        try
        {
            fixture.Now += TimeSpan.FromSeconds(2);
            await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(before.LastSettledAt, clock.GetType().GetProperty("LastSettledAt")!.GetValue(clock));
            Assert.Equal(before.OperationRemainingMs,
                (long[])clock.GetType().GetProperty("OperationRemainingMs")!.GetValue(clock)!);
        }
        finally { Gate(room).Release(); }
        fixture.Now += TimeSpan.FromSeconds(3);
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        var caughtUp = (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!;
        Assert.Equal(before.OperationRemainingMs[actor] - 5000, caughtUp.OperationRemainingMs[actor]);
        Assert.Equal(fixture.Now, caughtUp.LastSettledAt);
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        Assert.Equal(caughtUp.OperationRemainingMs,
            (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!.OperationRemainingMs);
    }

    [Fact]
    public async Task CompleteCohortBatchFailureLeavesBothDurableGenerationsUnchangedAndRetryAdvancesBoth()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.GenerationsAsync();
        fixture.Now += TimeSpan.FromSeconds(1);
        var failures = 0;
        fixture.Recorder.StorageFailureInjector = phase =>
        {
            if (phase != "before-ranked-runtime-batch-commit") return;
            Interlocked.Increment(ref failures);
            throw new IOException("synthetic all-room checkpoint failure");
        };
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        Assert.Equal(1, failures);
        Assert.Equal(before, await fixture.GenerationsAsync());
        await using (var reopened = new MatchRecorder(fixture.MatchPath))
        {
            await reopened.InitializeAsync();
            foreach (var pair in fixture.Matches.OrderBy(match => match.MatchId, StringComparer.Ordinal)
                         .Select((match, index) => (match, index)))
                Assert.Equal(before[pair.index],
                    (await reopened.GetRankedRuntimeCheckpointAsync(pair.match.MatchId))!.CheckpointGeneration);
        }
        fixture.Recorder.StorageFailureInjector = null;
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        var after = await fixture.GenerationsAsync();
        Assert.All(Enumerable.Range(0, after.Length), index => Assert.True(after[index] > before[index]));
    }

    [Fact]
    public async Task OverlappingSweepReturnsWithoutQueueingAndLaterSweepCanCommitAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var commits = 0;
        fixture.Recorder.StorageFailureInjector = phase =>
        {
            if (phase != "before-ranked-runtime-batch-commit") return;
            if (Interlocked.Increment(ref commits) != 1) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic checkpoint hold");
        };
        fixture.Now += TimeSpan.FromSeconds(1);
        var first = Task.Factory.StartNew(() => fixture.Manager.TickRankedClocksAsync(fixture.Now),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.Empty(await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, commits);
        }
        finally { release.Set(); await first.WaitAsync(TimeSpan.FromSeconds(3)); }
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        Assert.Equal(2, commits);
    }

    [Fact]
    public async Task HealthyDurableConclusionAndPendingOutboxSurviveDeferredBatchAndRecovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var ordered = Rooms(fixture.Manager).ToArray();
        var blocked = ordered[0];
        var healthy = fixture.Matches.Single(match => match.RoomCode == Code(ordered[1]));
        fixture.Manager.Disconnect(healthy.FirstSession);
        fixture.Platform.StorageFailureInjector = phase =>
        {
            if (phase == "before-commit") throw new IOException("synthetic platform settlement failure");
        };
        await Gate(blocked).WaitAsync();
        try
        {
            fixture.Now += TimeSpan.FromMinutes(5);
            await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1));
            var detail = (await fixture.Recorder.GetMatchAsync(healthy.MatchId))!;
            Assert.NotNull(detail.Match.EndedUtc);
            Assert.Single(detail.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
            Assert.Equal("completed", (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(healthy.MatchId))!.Status);
            Assert.Equal(1, await fixture.Recorder.CountPendingRankedSettlementsAsync());
            Assert.Null(fixture.Platform.RankedSettlement(healthy.MatchId, healthy.First.Id));
        }
        finally { Gate(blocked).Release(); fixture.Platform.StorageFailureInjector = null; }
        await using var reopened = new MatchRecorder(fixture.MatchPath);
        await reopened.InitializeAsync();
        var restored = new L12RoomManager(fixture.Catalog, reopened, fixture.Platform, () => fixture.Now);
        await restored.RestoreRankedRoomsAsync();
        await restored.TickRankedClocksAsync(fixture.Now);
        Assert.Equal(0, await reopened.CountPendingRankedSettlementsAsync());
        Assert.Equal("completed", (await reopened.GetRankedRuntimeCheckpointAsync(healthy.MatchId))!.Status);
        Assert.Single((await reopened.GetMatchAsync(healthy.MatchId))!.Commands,
            command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.NotNull(fixture.Platform.RankedSettlement(healthy.MatchId, healthy.First.Id));
        Assert.NotNull(fixture.Platform.RankedSettlement(healthy.MatchId, healthy.Second.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledMaintenanceStillInvalidatesButImmediateOverrideStillProtectsRunningRooms(bool immediate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var ordered = Rooms(fixture.Manager).ToArray();
        var blocked = ordered[0];
        var healthy = fixture.Matches.Single(match => match.RoomCode == Code(ordered[1]));
        var admin = fixture.Platform.Login("Admin", "L12master").Account!;
        var current = fixture.Platform.OperationsConfig(admin);
        var scheduled = fixture.Platform.ApplyOperationsConfig(admin, current.Config with
        {
            Maintenance = new L12MaintenanceConfig(true, "synthetic scheduled maintenance",
                fixture.Now.AddSeconds(-1), null, 2, 2),
        }, current.Version, "C2 synthetic schedule", Context("c2-schedule")).Current;
        if (immediate)
            fixture.Platform.BeginImmediateMaintenance(admin, 3, scheduled.Version,
                "C2 protect existing rooms", Context("c2-immediate"));
        await Gate(blocked).WaitAsync();
        try
        {
            await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1));
            var detail = (await fixture.Recorder.GetMatchAsync(healthy.MatchId))!;
            if (immediate) Assert.Null(detail.Match.EndedUtc);
            else
            {
                Assert.NotNull(detail.Match.EndedUtc);
                Assert.Null(detail.Match.Winner);
                Assert.Single(detail.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
            }
        }
        finally { Gate(blocked).Release(); }
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        foreach (var match in fixture.Matches)
        {
            var detail = (await fixture.Recorder.GetMatchAsync(match.MatchId))!;
            Assert.Equal(immediate, detail.Match.EndedUtc is null);
        }
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("immediate")]
    [InlineData("frozen")]
    public async Task BusyRoomDoesNotLoseObservedScheduledInvalidationAfterFiniteMaintenanceEnds(string laterPolicy)
    {
        await using var fixture = await Fixture.CreateAsync();
        var blocked = Rooms(fixture.Manager).First();
        var blockedMatch = fixture.Matches.Single(match => match.RoomCode == Code(blocked));
        var healthy = fixture.Matches.Single(match => match.RoomCode != Code(blocked));
        var admin = fixture.Platform.Login("Admin", "L12master").Account!;
        var current = fixture.Platform.OperationsConfig(admin);
        fixture.Platform.ApplyOperationsConfig(admin, current.Config with
        {
            Maintenance = new L12MaintenanceConfig(true, "synthetic finite scheduled maintenance",
                fixture.Now.AddSeconds(-1), fixture.Now.AddSeconds(1), 2, 2),
        }, current.Version, "C2 finite schedule", Context("c2-finite-schedule"));
        await Gate(blocked).WaitAsync();
        try
        {
            await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull((await fixture.Recorder.GetMatchAsync(healthy.MatchId))!.Match.EndedUtc);
            Assert.Null((await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!.Match.EndedUtc);
            fixture.Now += TimeSpan.FromSeconds(2);
            // The old awaited gate retained the already-observed active policy;
            // later disabling/overriding it did not cancel that in-flight fact.
            if (laterPolicy == "disabled")
            {
                var latest = fixture.Platform.OperationsConfig(admin);
                fixture.Platform.ApplyOperationsConfig(admin, latest.Config with
                {
                    Maintenance = latest.Config.Maintenance with { Enabled = false },
                }, latest.Version, "C2 disable after observation", Context("c2-disable-observed"));
            }
            else if (laterPolicy == "immediate")
            {
                var latest = fixture.Platform.OperationsConfig(admin);
                fixture.Platform.BeginImmediateMaintenance(admin, 3, latest.Version,
                    "C2 override after observation", Context("c2-override-observed"));
            }
            else if (laterPolicy == "frozen")
            {
                // This is the scheduler's explicit transient-frozen state,
                // not a claimed fault-injection/reconnect integration route.
                blocked.GetType().GetProperty("Closed")!.SetValue(blocked, true);
            }
        }
        finally { Gate(blocked).Release(); }
        if (laterPolicy == "frozen")
        {
            await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            Assert.Null((await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!.Match.EndedUtc);
            await Gate(blocked).WaitAsync();
            try { blocked.GetType().GetProperty("Closed")!.SetValue(blocked, false); }
            finally { Gate(blocked).Release(); }
        }
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        var detail = (await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!;
        Assert.NotNull(detail.Match.EndedUtc);
        Assert.Null(detail.Match.Winner);
        Assert.Single(detail.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion"
            && command.Command.GetProperty("reason").GetString() == "服务器维护开始，当前对局无效");
        await using var reopened = new MatchRecorder(fixture.MatchPath);
        await reopened.InitializeAsync();
        reopened.AttachCatalog(fixture.Catalog);
        var rebuilt = (await reopened.GetMatchAsync(blockedMatch.MatchId))!;
        Assert.NotNull(rebuilt.Match.EndedUtc);
        Assert.Single(rebuilt.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
    }

    [Fact]
    public async Task BusySettlementRoomCannotDelayHealthyConclusionOrExtendItsRetentionStart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var blockedMatch = fixture.Matches[0];
        var healthyMatch = fixture.Matches[1];
        await fixture.Manager.HandleActionAsync(blockedMatch.FirstSession,
            JsonSerializer.SerializeToElement(new { type = "surrender" }, WireJson));
        Assert.NotNull((await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!.Match.EndedUtc);
        var blocked = Rooms(fixture.Manager).Single(room => Code(room) == blockedMatch.RoomCode);
        var retentionStarted = blocked.GetType().GetProperty("SettlementStartedAt")!.GetValue(blocked);
        fixture.Manager.Disconnect(healthyMatch.FirstSession);
        await Gate(blocked).WaitAsync();
        try
        {
            fixture.Now += TimeSpan.FromMinutes(5);
            await fixture.Manager.TickRankedClocksAsync(fixture.Now).WaitAsync(TimeSpan.FromSeconds(1));
            await AssertConcludedOnceAsync(fixture, healthyMatch);
            Assert.Equal(retentionStarted, blocked.GetType().GetProperty("SettlementStartedAt")!.GetValue(blocked));
        }
        finally { Gate(blocked).Release(); }
        fixture.Now += TimeSpan.FromMinutes(26);
        var messages = await fixture.Manager.TickRankedClocksAsync(fixture.Now);
        Assert.Contains(messages, message => message.SessionId == blockedMatch.FirstSession
            && JsonSerializer.SerializeToElement(message.Payload, WireJson).GetProperty("type").GetString() == "roomClosed");
        Assert.DoesNotContain(Rooms(fixture.Manager), room => Code(room) == blockedMatch.RoomCode);
    }

    [Fact]
    public async Task ThirtySecondBusyResponseRoomKeepsOriginalDeadlineWhileHealthyExpiryIsDurable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var blocked = await fixture.AddResponseRoomAsync();
        var healthy = await fixture.AddResponseRoomAsync();
        var originalBlocked = Game(blocked.Room).CaptureResponseAutoCloseLease()!;
        var originalHealthy = Game(healthy.Room).CaptureResponseAutoCloseLease()!;
        fixture.Now = originalHealthy.DeadlineUtc.AddMilliseconds(-1);
        await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
        Assert.Equal(originalBlocked, Game(blocked.Room).CaptureResponseAutoCloseLease());
        Assert.Empty((await fixture.Recorder.GetMatchAsync(healthy.MatchId))!.Commands);
        await Gate(blocked.Room).WaitAsync();
        var held = Stopwatch.StartNew();
        Task<IReadOnlyList<OutgoingMessage>>? tick = null;
        try
        {
            fixture.Now = originalHealthy.DeadlineUtc;
            tick = fixture.Manager.TickResponseWindowsAsync(fixture.Now);
            await tick.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, await CountCloseAsync(fixture.Recorder, healthy.MatchId, originalHealthy.PromptId));
            Assert.Equal(originalBlocked, Game(blocked.Room).CaptureResponseAutoCloseLease());
            var remaining = TimeSpan.FromSeconds(30) - held.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            Assert.Equal(originalBlocked.DeadlineUtc, Game(blocked.Room).CaptureResponseAutoCloseLease()!.DeadlineUtc);
            Assert.Equal(0, await CountCloseAsync(fixture.Recorder, blocked.MatchId, originalBlocked.PromptId));
        }
        finally { Gate(blocked.Room).Release(); if (tick is not null) await tick.WaitAsync(TimeSpan.FromSeconds(5)); }
        fixture.Now += TimeSpan.FromSeconds(30);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => fixture.Manager.TickResponseWindowsAsync(fixture.Now)));
        Assert.Equal(1, await CountCloseAsync(fixture.Recorder, blocked.MatchId, originalBlocked.PromptId));
        Assert.Equal(1, await CountCloseAsync(fixture.Recorder, healthy.MatchId, originalHealthy.PromptId));
        var restored = Assert.IsType<L12JournalRecoveryState>(await fixture.Recorder.LoadJournalEngineAsync(blocked.MatchId));
        Assert.Equal(Game(blocked.Room).CaptureResponseAutoCloseLease(), restored.Engine.CaptureResponseAutoCloseLease());
    }

    [Fact]
    public async Task ResponseCommitFailureRestoresOriginalLeaseAndRetryExpiresThatLeaseExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.AddResponseRoomAsync();
        var original = Game(response.Room).CaptureResponseAutoCloseLease()!;
        fixture.Now = original.DeadlineUtc;
        var failures = 0;
        fixture.Recorder.StorageFailureInjector = phase =>
        {
            if (phase != "before-match-command-commit") return;
            Interlocked.Increment(ref failures);
            throw new IOException("synthetic response commit failure");
        };
        await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
        Assert.Equal(1, failures);
        Assert.Equal(original, Game(response.Room).CaptureResponseAutoCloseLease());
        Assert.Equal(0, await CountCloseAsync(fixture.Recorder, response.MatchId, original.PromptId));
        var recovered = Assert.IsType<L12JournalRecoveryState>(await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
        Assert.Equal(original, recovered.Engine.CaptureResponseAutoCloseLease());
        fixture.Recorder.StorageFailureInjector = null;
        await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
        await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
        Assert.Equal(1, await CountCloseAsync(fixture.Recorder, response.MatchId, original.PromptId));
    }

    private static async Task<int> CountCloseAsync(MatchRecorder recorder, string matchId, string promptId)
        => (await recorder.GetMatchAsync(matchId))!.Commands.Count(command =>
            command.Command.GetProperty("type").GetString() == "responseAutoClose"
            && command.Command.GetProperty("promptId").GetString() == promptId);
    private static L12GameEngine Game(object room) => (L12GameEngine)room.GetType().GetProperty("Game")!.GetValue(room)!;

    private static L12AdminAuditContext Context(string id)
        => new(id, "admin.operations.write", RequestMethod: "TEST", RequestPath: "/synthetic/c2");

    private static async Task AssertConcludedOnceAsync(Fixture fixture, Match match)
    {
        var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(match.MatchId));
        Assert.NotNull(detail.Match.EndedUtc);
        Assert.Single(detail.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.NotNull(fixture.Platform.RankedSettlement(match.MatchId, match.First.Id));
        Assert.NotNull(fixture.Platform.RankedSettlement(match.MatchId, match.Second.Id));
    }

    private static IEnumerable<object> Rooms(L12RoomManager manager)
    {
        var entries = (IEnumerable)typeof(L12RoomManager).GetField("_rooms",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in entries) yield return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
    }
    private static string Code(object room) => (string)room.GetType().GetProperty("Code")!.GetValue(room)!;
    private static SemaphoreSlim Gate(object room) => (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;

    private sealed record Match(L12AccountView First, L12AccountView Second,
        Guid FirstSession, Guid SecondSession, string MatchId, string RoomCode);
    private sealed record ResponseRoom(object Room, string MatchId);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private Fixture(string directory) => _directory = directory;
        internal string MatchPath => Path.Combine(_directory, "matches.db");
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal required L12Catalog Catalog { get; init; }
        internal required L12PlatformStore Platform { get; init; }
        internal required MatchRecorder Recorder { get; init; }
        internal L12RoomManager Manager { get; private set; } = null!;
        internal List<Match> Matches { get; } = [];

        internal static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "l12-c2-synthetic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var fixture = new Fixture(directory) { Catalog = catalog, Platform = platform, Recorder = recorder };
            fixture.Manager = new L12RoomManager(catalog, recorder, platform, () => fixture.Now);
            await fixture.AddMatchAsync();
            await fixture.AddMatchAsync();
            return fixture;
        }

        private async Task AddMatchAsync()
        {
            var identity = Guid.NewGuid().ToString("N")[..8];
            var first = Platform.Register("cf" + identity, "Password123!").Account!;
            var second = Platform.Register("cs" + identity, "Password123!").Account!;
            Platform.SelectRankedFaction(first.Id, "order");
            Platform.SelectRankedFaction(second.Id, "chaos");
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            Manager.Connect(firstSession, first.Id, first.Username);
            Manager.Connect(secondSession, second.Id, second.Username);
            await Manager.JoinMatchmakingAsync(firstSession, "ranked", null);
            var messages = await Manager.JoinMatchmakingAsync(secondSession, "ranked", null);
            var game = messages.Where(message => message.SessionId == firstSession)
                .Select(message => JsonSerializer.SerializeToElement(message.Payload, WireJson))
                .Single(payload => payload.GetProperty("type").GetString() == "gameState");
            Matches.Add(new(first, second, firstSession, secondSession,
                game.GetProperty("state").GetProperty("matchId").GetString()!,
                game.GetProperty("state").GetProperty("roomCode").GetString()!));
        }

        internal async Task<ResponseRoom> AddResponseRoomAsync()
        {
            // Seed a public synthetic kernel fixture, not a second timer/state machine.
            // Public CreateRoom/JoinRoom supplies real membership; only initial Game
            // placement and BeginResponseWindow arrange the otherwise private fixture.
            var identity = Guid.NewGuid().ToString("N")[..8];
            var first = Platform.Register("pf" + identity, "Password123!").Account!;
            var second = Platform.Register("ps" + identity, "Password123!").Account!;
            var host = Guid.NewGuid();
            var guest = Guid.NewGuid();
            Manager.Connect(host, first.Id, first.Username);
            Manager.Connect(guest, second.Id, second.Username);
            var created = Manager.CreateRoom(host);
            var code = JsonSerializer.SerializeToElement(created[0].Payload, WireJson).GetProperty("roomCode").GetString()!;
            Manager.JoinRoom(guest, code);
            var room = Rooms(Manager).Single(candidate => Code(candidate) == code);
            var original = Catalog.DeckAt(0);
            var deck = new L12PresetDeckDefinition
            {
                Name = "C2 synthetic response", MasterId = "S02-03M1", CardIds = [.. original.CardIds],
                MoraleIds = [.. original.MoraleIds], SpecialIds = [],
            };
            var matchId = "c2-response-" + Guid.NewGuid().ToString("N");
            var game = new L12GameEngine(Catalog, matchId, code, 120912,
                [first.Username, second.Username], [deck, deck], skipPreparation: true,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion, utcNow: () => Now,
                responseModes: [L12GameEngine.InvalidFiveSecondsResponseMode, L12GameEngine.InvalidFiveSecondsResponseMode]);
            game.State.PendingPrompts.Clear();
            foreach (var player in game.State.Players)
            {
                player.Hand.Clear();
                player.Field[0] = new L12CardInstance?[3];
                player.Field[1] = new L12CardInstance?[3];
            }
            var stack = new L12StackItem
            {
                StackItemId = "c2-stack-" + identity, Controller = 1, SourceInstanceId = "source",
                SourceCardId = "S01-0103", SourceName = "synthetic source", Trigger = "reaction", Text = "测试响应时点",
            };
            game.State.ActivePlayer = 0;
            game.State.EffectStack.Add(stack);
            typeof(L12GameEngine).GetMethod("BeginResponseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(game, [stack]);
            Assert.NotNull(game.CaptureResponseAutoCloseLease());
            await Gate(room).WaitAsync();
            try
            {
                await Recorder.StartAsync(game, account0: first.Id, account1: second.Id, decks: [deck, deck]);
                room.GetType().GetProperty("Game")!.SetValue(room, game);
            }
            finally { Gate(room).Release(); }
            return new(room, matchId);
        }

        internal async Task<long[]> GenerationsAsync()
        {
            var generations = new List<long>();
            foreach (var match in Matches.OrderBy(match => match.MatchId, StringComparer.Ordinal))
                generations.Add((await Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!.CheckpointGeneration);
            return [.. generations];
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            var target = Path.GetFullPath(_directory);
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("l12-c2-synthetic-", StringComparison.Ordinal))
                throw new InvalidOperationException("synthetic cleanup scope mismatch");
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(target, true); return; }
                catch (IOException) when (attempt < 4) { await Task.Delay(40 * (attempt + 1)); }
            }
        }
    }
}
