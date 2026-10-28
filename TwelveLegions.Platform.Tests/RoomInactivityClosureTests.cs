using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Fixture = TwelveLegions.Platform.Tests.RoomGameplayActivityTests.Fixture;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RoomInactivityClosureTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UncertainCommitRecoversOnlyDurableStateThroughReconnectOrMaintenance(bool committed, bool reconnect)
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var original = f.Game;
        f.Now += TimeSpan.FromMinutes(41);
        var phase = committed ? "after-inactivity-native-commit" : "before-inactivity-native-commit";
        f.Recorder.StorageFailureInjector = current => { if (current == phase) throw new IOException("synthetic uncertain outcome"); };
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.True((bool)f.RoomProperty("Closed")!);
        Assert.True((bool)f.RoomProperty("InactivityRecoveryPending")!);
        Assert.Same(original, f.Game);
        f.Recorder.StorageFailureInjector = null;
        if (reconnect) await f.Manager.ConnectAsync(Guid.NewGuid(), f.Accounts[0], "activityone");
        else Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.False((bool)f.RoomProperty("Closed")!);
        Assert.False((bool)f.RoomProperty("InactivityRecoveryPending")!);
        Assert.NotSame(original, f.Game);
        Assert.Equal(committed, f.Game.State.Phase == L12Phase.GameOver);
        Assert.Equal(committed, (bool)f.RoomProperty("CompletionRecorded")!);
        var record = (await f.Recorder.GetMatchAsync(original.State.MatchId))!;
        Assert.Equal(committed, record.Match.EndedUtc is not null);
        Assert.Equal(committed ? 1 : 0, record.Commands.Count(command => command.Command.GetProperty("type").GetString() == "authorityConclusion"));
        if (!committed) Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Equal(L12Phase.GameOver, f.Game.State.Phase);
        Assert.False(f.Game.State.EndedByAgreedDraw);
    }

    [Fact]
    public async Task PregameUpdatesShareTheRetirementGateAndQueuedReadyRechecksMembership()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var room = f.Room;
        var code = f.Code;
        var gate = (SemaphoreSlim)f.RoomProperty("Gate")!;
        await gate.WaitAsync();
        Task<IReadOnlyList<OutgoingMessage>>? ready = null;
        try
        {
            Assert.Contains(f.Manager.JoinRoom(f.Second, code), Fixture.IsError);
            Assert.Contains(f.Manager.UpdateRoomOptions(f.First, new()), Fixture.IsError);
            Assert.Contains(f.Manager.SelectDeck(f.First, 0), Fixture.IsError);
            ready = f.Manager.SetReadyAsync(f.First, true);
            Assert.False(ready.IsCompleted);
            typeof(L12RoomManager).GetMethod("CloseSettlementLocked", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(f.Manager, [room, "synthetic retirement interleaving"]);
        }
        finally { gate.Release(); }
        Assert.Contains(await ready!, Fixture.IsError);
        Assert.Null(f.Manager.ObserveRoomInactivity(code));
        Assert.Contains(f.Manager.JoinRoom(f.Second, code), Fixture.IsError);
        Fixture.Success(f.Manager.CreateRoom(f.Second, new())); // No ghost membership after the refused join.
    }

    [Fact]
    public async Task DrawRecoversFromTerminalCheckpointAndFromThePreviousCheckpointJournalTail()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        f.Now += TimeSpan.FromMinutes(41);
        Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        var match = f.Game.State.MatchId;
        var hash = f.Game.ComputeStateHash();
        var checkpoint = (await f.Recorder.LoadLatestCheckpointAsync(match))!;
        var recovered = (await f.Recorder.LoadJournalEngineAsync(match))!;
        Assert.Equal(hash, recovered.Engine.ComputeStateHash());
        Assert.False(recovered.Engine.State.EndedByAgreedDraw);
        var connectionString = (string)typeof(MatchRecorder).GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Recorder)!;
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            var remove = connection.CreateCommand();
            remove.CommandText = "DELETE FROM match_state_checkpoints WHERE match_id=$match AND sequence=$sequence;";
            remove.Parameters.AddWithValue("$match", match);
            remove.Parameters.AddWithValue("$sequence", checkpoint.Sequence);
            Assert.Equal(1, await remove.ExecuteNonQueryAsync());
        }
        recovered = (await f.Recorder.LoadJournalEngineAsync(match))!;
        Assert.Equal(hash, recovered.Engine.ComputeStateHash());
        Assert.Equal(L12Phase.GameOver, recovered.Engine.State.Phase);
        Assert.Null(recovered.Engine.State.Winner);
        Assert.False(recovered.Engine.State.EndedByAgreedDraw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpensiveNativeCompletionRollsBackOnItsBudgetOrForegroundPreemption(bool foreground)
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var original = f.Game;
        var connectionString = (string)typeof(MatchRecorder).GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Recorder)!;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var trigger = connection.CreateCommand();
        trigger.CommandText = """
            CREATE TRIGGER synthetic_slow_completion BEFORE UPDATE OF ended_utc ON matches
            BEGIN
              SELECT (WITH RECURSIVE numbers(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM numbers WHERE x<100000000) SELECT sum(x) FROM numbers);
            END;
            """;
        await trigger.ExecuteNonQueryAsync();
        using var entered = new ManualResetEventSlim();
        f.Recorder.StorageFailureInjector = phase => { if (phase == "after-match-checkpoint-write") entered.Set(); };
        f.Now += TimeSpan.FromMinutes(41);
        var run = Task.Run(() => f.Manager.RunInactiveRoomMaintenanceAsync());
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            if (foreground)
            {
                await using var normal = await f.Recorder.OpenWriteConnectionAsync();
            }
            Assert.Empty(await run.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Same(original, f.Game);
            Assert.False((bool)f.RoomProperty("Closed")!);
            Assert.Null((await f.Recorder.GetMatchAsync(original.State.MatchId))!.Match.EndedUtc);
        }
        finally
        {
            await run;
            f.Recorder.StorageFailureInjector = null;
            trigger.CommandText = "DROP TRIGGER synthetic_slow_completion;";
            await trigger.ExecuteNonQueryAsync();
        }
        Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
    }

    [Fact]
    public async Task ActualServerStopCancelsAndJoinsItsExistingMaintenanceOwner()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var platform = (L12PlatformStore)typeof(L12RoomManager).GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!;
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
        await using var server = new L12WebSocketServer(f.Manager, f.Recorder, platform, f.Catalog,
            sandboxReplayMaintenanceInterval: TimeSpan.FromMilliseconds(20));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task? stop = null;
        try
        {
            await server.StartAsync(0);
            f.Recorder.StorageFailureInjector = phase =>
            { if (phase == "before-inactivity-write-admission") { entered.Set(); release.Wait(); } };
            f.Now += TimeSpan.FromMinutes(41);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            stop = Task.Run(() => server.StopAsync());
            await Task.Delay(50);
            Assert.False(stop.IsCompleted);
            release.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(L12Phase.GameOver, f.Game.State.Phase);
            Assert.Null(typeof(L12WebSocketServer).GetField("_sandboxReplayMaintenanceTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server));
        }
        finally
        {
            release.Set();
            if (stop is not null) await stop;
            await server.StopAsync();
            f.Recorder.StorageFailureInjector = null;
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
        }
    }

    [Theory]
    [InlineData("friendly")]
    [InlineData("casual")]
    public async Task StrictlyOverFortyMinutesEndsOnceAsADurableDrawWithoutInventingAgreement(string mode)
    {
        await using var f = await Fixture.CreateAsync();
        if (mode == "friendly") await f.StartFriendlyGameAsync();
        else await f.CreateRoomAsync("casual");
        var original = f.Game;
        var match = original.State.MatchId;
        var random = original.RandomState;
        var sequence = (long)f.RoomProperty("CommandSequence")!;
        f.Now += TimeSpan.FromMinutes(40);
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Same(original, f.Game);
        f.Now += TimeSpan.FromTicks(1);
        Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.NotSame(original, f.Game);
        Assert.NotEqual(L12Phase.GameOver, original.State.Phase);
        Assert.Equal(L12Phase.GameOver, f.Game.State.Phase);
        Assert.Null(f.Game.State.Winner);
        Assert.False(f.Game.State.EndedByAgreedDraw);
        Assert.Equal(random, f.Game.RandomState);
        Assert.Equal(sequence + 1, (long)f.RoomProperty("CommandSequence")!);
        Assert.True((bool)f.RoomProperty("CompletionRecorded")!);
        Assert.False((bool)f.RoomProperty("Closed")!); // The normal result-retention room is not an active game.
        Assert.Equal("game-draw", f.Game.State.Events.Last().Type);
        var recorded = (await f.Recorder.GetMatchAsync(match))!;
        Assert.NotNull(recorded.Match.EndedUtc);
        Assert.Null(recorded.Match.Winner);
        Assert.Equal(f.Game.ComputeStateHash(), recorded.Match.FinalHash);
        var terminal = Assert.Single(recorded.Commands.Where(command =>
            command.Command.GetProperty("type").GetString() == "authorityConclusion"));
        Assert.True(terminal.Command.GetProperty("inactivityDraw").GetBoolean());
        Assert.False(terminal.Command.GetProperty("agreedDraw").GetBoolean());
        Assert.Equal("game-draw", terminal.State.GetProperty("Events").EnumerateArray().Last().GetProperty("Type").GetString());
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Single((await f.Recorder.GetMatchAsync(match))!.Commands.Where(command =>
            command.Command.GetProperty("type").GetString() == "authorityConclusion"));
        var statistics = await f.Recorder.PlayerStatisticsAsync(f.Accounts[0], "activityone");
        Assert.Equal(1, statistics.Overall.Draws);
        Assert.Equal(0, statistics.Overall.Wins);
        Assert.Equal(0, statistics.Overall.Losses);
    }

    [Fact]
    public async Task EmptyFriendlyLobbyRetiresWithoutInventingAMatchOrResult()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var code = f.Code;
        f.Now += TimeSpan.FromMinutes(41);
        var messages = await f.Manager.RunInactiveRoomMaintenanceAsync();
        Assert.Contains(messages, message => JsonSerializer.SerializeToElement(message.Payload)
            .GetProperty("type").GetString() == "roomClosed");
        Assert.Null(f.Manager.ObserveRoomInactivity(code));
        Assert.Empty(await f.Recorder.ListMatchesAsync());
    }

    [Fact]
    public async Task EachSweepRetiresAtMostFourExpiredLobbiesAndTheNextSweepMakesProgress()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var platform = (L12PlatformStore)typeof(L12RoomManager).GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!;
        for (var index = 0; index < 4; index++)
        {
            var account = platform.Register("idlehost" + index, "Password123!").Account!;
            var id = Guid.NewGuid();
            f.Manager.Connect(id, account.Id, account.Username);
            Fixture.Success(f.Manager.CreateRoom(id, new()));
        }
        var rooms = (IDictionary)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!;
        f.Now += TimeSpan.FromMinutes(41);
        Assert.Equal(4, (await f.Manager.RunInactiveRoomMaintenanceAsync()).Count);
        Assert.Single(rooms.Cast<object>());
        Assert.Single(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Empty(rooms.Cast<object>());
        Assert.Empty(await f.Recorder.ListMatchesAsync());
    }

    [Theory]
    [InlineData("ranked")]
    [InlineData("tournament")]
    [InlineData("sandbox")]
    [InlineData("unknown")]
    public async Task TheActualConsumerCannotRetireAnOutOfScopeMode(string mode)
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var original = f.Game;
        f.Room.GetType().GetProperty("Options")!.SetValue(f.Room, new L12RoomOptions { MatchModeId = mode });
        f.Now += TimeSpan.FromHours(2);
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Same(original, f.Game);
        Assert.Null((await f.Recorder.GetMatchAsync(original.State.MatchId))!.Match.EndedUtc);
    }

    [Fact]
    public async Task BusyRoomAndUnknownOrNewerActivityRemainUntouched()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var original = f.Game;
        var gate = (SemaphoreSlim)f.RoomProperty("Gate")!;
        f.Now += TimeSpan.FromMinutes(41);
        await gate.WaitAsync();
        try { Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(1))); }
        finally { gate.Release(); }
        var ticks = f.Room.GetType().GetField("LastGameplayActivityUtcTicks")!;
        ticks.SetValue(f.Room, 0L);
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        ticks.SetValue(f.Room, f.Now.AddMinutes(1).UtcTicks);
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        ticks.SetValue(f.Room, f.Now.UtcTicks);
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Same(original, f.Game);
    }

    [Theory]
    [InlineData("after-match-command-write")]
    [InlineData("after-match-checkpoint-write")]
    [InlineData("before-match-complete-commit")]
    [InlineData("before-match-command-commit")]
    public async Task EveryPreCommitFailureLeavesTheLiveEngineAndWholeJournalUnchanged(string phase)
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var original = f.Game;
        var before = original.ComputeStateHash();
        var sequence = (long)f.RoomProperty("CommandSequence")!;
        f.Now += TimeSpan.FromMinutes(41);
        f.Recorder.StorageFailureInjector = current => { if (current == phase) throw new IOException("synthetic rollback"); };
        Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.Same(original, f.Game);
        Assert.False((bool)f.RoomProperty("Closed")!);
        Assert.Equal(before, original.ComputeStateHash());
        Assert.Equal(sequence, (long)f.RoomProperty("CommandSequence")!);
        var journal = (await f.Recorder.GetMatchAsync(original.State.MatchId))!;
        Assert.Null(journal.Match.EndedUtc);
        Assert.DoesNotContain(journal.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.Equal(sequence, (await f.Recorder.LoadLatestCheckpointAsync(original.State.MatchId))!.Sequence);
        f.Recorder.StorageFailureInjector = null;
        Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
        Assert.NotNull((await f.Recorder.GetMatchAsync(original.State.MatchId))!.Match.EndedUtc);
    }

    [Fact]
    public async Task BusyRecorderAndNativeSqliteWriterAreSkippedWithoutAQueuedMaintenanceWrite()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var original = f.Game;
        f.Now += TimeSpan.FromMinutes(41);
        var gate = (SemaphoreSlim)typeof(MatchRecorder).GetField("_appendWriteGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Recorder)!;
        await gate.WaitAsync();
        try { Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(1))); }
        finally { gate.Release(); }
        var connectionString = (string)typeof(MatchRecorder).GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Recorder)!;
        await using (var external = new SqliteConnection(connectionString))
        {
            await external.OpenAsync();
            await using var writer = external.BeginTransaction();
            Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Same(original, f.Game);
        }
        Assert.NotEmpty(await f.Manager.RunInactiveRoomMaintenanceAsync());
    }

    [Fact]
    public async Task SlowPreparationHasOneOwnerAndDoesNotBlockNormalDurableActionsOrClockTicks()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var firstCode = f.Code;
        var platform = (L12PlatformStore)typeof(L12RoomManager).GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!;
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        for (var i = 0; i < ids.Length; i++)
        {
            var account = platform.Register("healthy" + i, "Password123!").Account!;
            f.Manager.Connect(ids[i], account.Id, account.Username);
        }
        Fixture.Success(f.Manager.CreateRoom(ids[0], new L12RoomOptions { DisasterMode = "none" }));
        var rooms = ((IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!).Cast<object>()
            .Select(entry => entry.GetType().GetProperty("Value")!.GetValue(entry)!).ToArray();
        var healthyRoom = rooms.Single(room => (string)room.GetType().GetProperty("Code")!.GetValue(room)! != firstCode);
        var code = (string)healthyRoom.GetType().GetProperty("Code")!.GetValue(healthyRoom)!;
        Fixture.Success(f.Manager.JoinRoom(ids[1], code));
        Fixture.Success(await f.Manager.SetReadyAsync(ids[0], true));
        Fixture.Success(await f.Manager.SetReadyAsync(ids[1], true));
        var healthy = (L12GameEngine)healthyRoom.GetType().GetProperty("Game")!.GetValue(healthyRoom)!;
        f.Now += TimeSpan.FromMinutes(41);
        healthyRoom.GetType().GetField("LastGameplayActivityUtcTicks")!.SetValue(healthyRoom, f.Now.UtcTicks);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Recorder.StorageFailureInjector = phase =>
        { if (phase == "before-inactivity-write-admission") { entered.Set(); release.Wait(); } };
        using var cancellation = new CancellationTokenSource();
        var maintenance = Task.Run(() => f.Manager.RunInactiveRoomMaintenanceAsync(cancellation.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Empty(await f.Manager.RunInactiveRoomMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(1)));
            var prompt = healthy.State.PendingPrompts.Single();
            Fixture.Success(await f.Manager.HandleActionAsync(ids[prompt.PlayerIndex], JsonSerializer.SerializeToElement(
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["first"])), "healthy-during-idle")
                .WaitAsync(TimeSpan.FromSeconds(1)));
            await f.Manager.TickRankedClocksAsync().WaitAsync(TimeSpan.FromSeconds(1));
            await f.Manager.TickResponseWindowsAsync().WaitAsync(TimeSpan.FromSeconds(1));
            cancellation.Cancel();
            Assert.False(maintenance.IsCompleted); // Cancellation never abandons the still-owned work.
        }
        finally
        {
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => maintenance);
            f.Recorder.StorageFailureInjector = null;
        }
    }
}
