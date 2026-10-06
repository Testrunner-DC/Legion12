using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentRoomReadinessTests
{
    [Fact]
    public async Task PregameAndBusyRoomBlockWithoutWaitingAndDrainingAllowsLeave()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.True(f.Rooms.CaptureDeploymentRoomReadiness().Clear);
        _ = f.Rooms.CreateRoom(f.SessionId);
        var before = f.Rooms.CaptureDeploymentRoomReadiness();
        Assert.False(before.Clear);
        Assert.Equal(1, before.Blockers["pregame_rooms"]);
        var room = f.SingleRoom();
        var gate = (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        await gate.WaitAsync();
        try
        {
            var capture = Task.Run(f.Rooms.CaptureDeploymentRoomReadiness);
            var busy = await capture.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(busy.Clear);
            Assert.Equal(1, busy.Blockers["room_lock_busy"]);
        }
        finally { gate.Release(); }
        f.Coordinator.BeginDrain(f.Owner);
        _ = f.Rooms.LeaveRoom(f.SessionId);
        Assert.True(f.Rooms.CaptureDeploymentRoomReadiness().Clear);
        var refused = Assert.Single(f.Rooms.CreateRoom(f.SessionId));
        Assert.Equal("deploymentDrainActive", JsonSerializer.SerializeToElement(refused.Payload).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ActualSandboxSurrenderCompletesDuringDrainThenSealedTransportDoesNotCheckpoint()
    {
        await using var f = await Fixture.CreateAsync();
        _ = await f.Rooms.CreateSandboxAsync(f.SessionId, new L12SandboxRequest());
        var active = f.Rooms.CaptureDeploymentRoomReadiness();
        Assert.False(active.Clear);
        Assert.Equal(1, active.Blockers["unfinished_rooms"]);
        f.Coordinator.BeginDrain(f.Owner);
        var outgoing = await f.Rooms.HandleSandboxActionAsync(f.SessionId, 0,
            JsonSerializer.SerializeToElement(new { type = "surrender" }), "synthetic-finish");
        Assert.DoesNotContain(outgoing, message =>
            JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString() == "actionRejected");
        Assert.True(f.Rooms.CaptureDeploymentRoomReadiness().Clear);
        Assert.True((await f.Recorder.DeploymentReadinessAsync()).Clear);
        var result = await new L12DeploymentDrainController(f.Rooms, f.Recorder, f.Platform, f.Coordinator).SealAsync(f.Owner);
        Assert.True(result.Granted);
        var before = f.JournalCounts();
        var session = f.Session();
        var disconnectedAt = session.GetType().GetProperty("DisconnectedAt")!.GetValue(session);
        Assert.Empty(f.Rooms.DisconnectTransportAfterDeploymentSeal(f.SessionId));
        Assert.Equal(before, f.JournalCounts());
        Assert.Equal(disconnectedAt, session.GetType().GetProperty("DisconnectedAt")!.GetValue(session));
        Assert.False((bool)session.GetType().GetProperty("Connected")!.GetValue(session)!);
        Assert.True(f.Coordinator.IsCurrentSealPermit(result.Permit));
    }

    [Fact]
    public async Task DirectPublicClockMutationsRejectSealEvenWithoutMatchingRoom()
    {
        await using var f = await Fixture.CreateAsync();
        f.Coordinator.BeginDrain(f.Owner);
        Assert.True(f.Coordinator.TryBeginSeal(f.Owner).Succeeded);
        await Assert.ThrowsAsync<L12DeploymentBarrierClosedException>(() => f.Rooms.ExtendTournamentClockAsync("missing", "missing", 1));
        await Assert.ThrowsAsync<L12DeploymentBarrierClosedException>(() => f.Rooms.PauseTournamentClockAsync("missing", "missing", true, "synthetic"));
        await Assert.ThrowsAsync<L12DeploymentBarrierClosedException>(() => f.Rooms.CancelTournamentRoomsAsync("missing", "synthetic"));
        Assert.Equal(0, f.Coordinator.Snapshot().ActivityLeases);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine("D:/GPT/Legion12/artifacts/deployment-room-readiness-tests", Guid.NewGuid().ToString("N"));
        internal Guid SessionId { get; } = Guid.NewGuid();
        internal L12PlatformStore Platform { get; private set; } = null!;
        internal MatchRecorder Recorder { get; private set; } = null!;
        internal L12RoomManager Rooms { get; private set; } = null!;
        internal L12DeploymentDrainCoordinator Coordinator { get; } = new("11111111111111111111111111111111", new string('a', 40), new FenceStore());
        internal L12DeploymentDrainOwner Owner { get; private set; } = null!;
        internal static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            f.Platform = new(Path.Combine(f._root, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards);
            var account = f.Platform.Register("drainroom01", "synthetic-password").Account!;
            Assert.NotNull(account);
            f.Recorder = new(Path.Combine(f._root, "matches.db"));
            await f.Recorder.InitializeAsync();
            f.Rooms = new(catalog, f.Recorder, f.Platform);
            f.Rooms.AttachDeploymentDrain(f.Coordinator);
            _ = await f.Rooms.ConnectAsync(f.SessionId, account.Id, account.Username);
            Assert.True(L12DeploymentDrainOwner.TryCreate("22222222222222222222222222222222", new string('b', 40), f.Coordinator.Snapshot().ProcessInstance, out var owner));
            f.Owner = owner!;
            return f;
        }
        internal object SingleRoom()
        {
            var dictionary = typeof(L12RoomManager).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Rooms)!;
            return ((IEnumerable)dictionary.GetType().GetProperty("Values")!.GetValue(dictionary)!).Cast<object>().Single();
        }
        internal object Session()
        {
            var dictionary = typeof(L12RoomManager).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Rooms)!;
            return dictionary.GetType().GetProperty("Item")!.GetValue(dictionary, [SessionId])!;
        }
        internal string JournalCounts()
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, "matches.db"), Mode = SqliteOpenMode.ReadOnly }.ToString());
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT (SELECT COUNT(*) FROM match_events)||':'||(SELECT COUNT(*) FROM match_state_checkpoints)||':'||(SELECT COUNT(*) FROM match_action_events);";
            return (string)command.ExecuteScalar()!;
        }
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
    private sealed class FenceStore : IL12DeploymentDrainFenceStore
    {
        public L12DeploymentDrainFenceLoadResult Load() => L12DeploymentDrainFenceLoadResult.Missing;
        public bool TryWrite(L12DeploymentPersistedFence fence) => true;
        public bool TryClear(string operationId, string targetCommit) => true;
    }
}
