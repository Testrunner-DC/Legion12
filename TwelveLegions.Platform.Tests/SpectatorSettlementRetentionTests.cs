using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class SpectatorSettlementRetentionTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothPlayerExitOrdersRetainTheSamePublicSpectatorSettlement(bool hostFirst)
    {
        await using var fixture = await Fixture.CreateAsync();
        var terminal = await fixture.FinishAsync();
        var first = hostFirst ? fixture.Host : fixture.Guest;
        var second = hostFirst ? fixture.Guest : fixture.Host;
        Assert.DoesNotContain(fixture.Manager.LeaveRoom(first), IsRoomClosed);
        Assert.DoesNotContain(fixture.Manager.LeaveRoom(second), IsRoomClosed);
        Assert.Equal(1, fixture.Manager.RuntimeStats().RoomCount);
        Assert.DoesNotContain(await fixture.TickAsync(), IsRoomClosed);
        Assert.Equal(terminal.GetProperty("stateHash").GetString(),
            (await fixture.SpectatorStateAsync(fixture.Observer)).GetProperty("stateHash").GetString());

        var hostRoom = fixture.CreateRoom(fixture.Host);
        var guestRoom = fixture.CreateRoom(fixture.Guest);
        Assert.NotEqual(fixture.RoomCode, hostRoom);
        Assert.NotEqual(fixture.RoomCode, guestRoom);
        fixture.Now += TimeSpan.FromMinutes(30);
        Assert.Single((await fixture.TickAsync()).Where(message => message.SessionId == fixture.Observer && IsRoomClosed(message)));
        Assert.Equal(hostRoom, PayloadFor(await fixture.Manager.RecoveryStateAsync(fixture.Host), fixture.Host, "roomState")
            .GetProperty("roomCode").GetString());
        Assert.Equal(guestRoom, PayloadFor(await fixture.Manager.RecoveryStateAsync(fixture.Guest), fixture.Guest, "roomState")
            .GetProperty("roomCode").GetString());
    }

    [Fact]
    public async Task DisconnectedSpectatorRecoversWithoutExtendingTheThirtyMinuteDeadline()
    {
        await using var fixture = await Fixture.CreateAsync();
        var terminal = await fixture.FinishAsync();
        fixture.LeaveBothPlayers();
        fixture.Manager.Disconnect(fixture.Observer);
        Assert.DoesNotContain(await fixture.TickAsync(), IsRoomClosed);
        fixture.Now += TimeSpan.FromMinutes(29);
        var replacement = Guid.NewGuid();
        var claim = await fixture.Manager.ConnectAsync(replacement, Fixture.ObserverAccount, "observer-reconnected");
        Assert.True(claim.Recovered);
        Assert.Equal(fixture.RoomCode, claim.RoomCode);
        for (var index = 0; index < 2; index++)
            Assert.Equal(terminal.GetProperty("stateHash").GetString(),
                (await fixture.SpectatorStateAsync(replacement)).GetProperty("stateHash").GetString());
        fixture.Now += TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(1);
        Assert.DoesNotContain(await fixture.TickAsync(), IsRoomClosed);
        fixture.Now += TimeSpan.FromMilliseconds(1);
        Assert.Single((await fixture.TickAsync()).Where(message => message.SessionId == replacement && IsRoomClosed(message)));
        Assert.Empty(await fixture.Manager.RecoveryStateAsync(replacement));
        Assert.Equal(0, fixture.Manager.RuntimeStats().RoomCount);
    }

    [Fact]
    public async Task ExplicitExitReleasesOnlyThatObserverAndStaleIdsCannotRetainOrClearAnotherRoom()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = fixture.AddObserver("other-observer");
        await fixture.FinishAsync();
        fixture.LeaveBothPlayers();
        var oldRoom = fixture.RuntimeRoom();
        var left = fixture.Manager.LeaveRoom(fixture.Observer);
        Assert.Equal(fixture.RoomCode, PayloadFor(left, fixture.Observer, "roomLeft").GetProperty("roomCode").GetString());
        Assert.DoesNotContain(left, message => message.SessionId == other);
        await fixture.SpectatorStateAsync(other);
        var newRoom = fixture.CreateRoom(fixture.Observer);

        // A stale index entry is not membership: this session now owns a different room.
        var spectators = Spectators(oldRoom);
        lock (spectators)
        {
            spectators.Add(fixture.Observer);
            spectators.Add(Guid.NewGuid());
        }
        Assert.Single(fixture.Manager.LeaveRoom(other));
        var tick = await fixture.TickAsync();
        Assert.DoesNotContain(tick, message => message.SessionId == fixture.Observer && IsRoomClosed(message));
        Assert.Equal(1, fixture.Manager.RuntimeStats().RoomCount);
        Assert.Empty(await fixture.Manager.RecoveryStateAsync(other));
        Assert.Equal(newRoom, PayloadFor(await fixture.Manager.RecoveryStateAsync(fixture.Observer), fixture.Observer, "roomState")
            .GetProperty("roomCode").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiryBeforeOrAfterReplacementClaimCannotResurrectSettlementMembership(bool claimBeforeExpiry)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.FinishAsync();
        fixture.LeaveBothPlayers();
        fixture.Manager.Disconnect(fixture.Observer);
        fixture.Now += TimeSpan.FromMinutes(30);
        var replacement = Guid.NewGuid();
        L12SessionClaimResult claim;
        if (claimBeforeExpiry)
        {
            claim = await fixture.Manager.ConnectAsync(replacement, Fixture.ObserverAccount, "expiry-observer");
            Assert.True(claim.Recovered);
            Assert.Single((await fixture.TickAsync()).Where(message => message.SessionId == replacement && IsRoomClosed(message)));
        }
        else
        {
            await fixture.TickAsync();
            claim = await fixture.Manager.ConnectAsync(replacement, Fixture.ObserverAccount, "expiry-observer");
            Assert.False(claim.Recovered);
            Assert.Null(claim.RoomCode);
        }
        Assert.Equal(0, fixture.Manager.RuntimeStats().RoomCount);
        Assert.Empty(await fixture.Manager.RecoveryStateAsync(replacement));
        Assert.DoesNotContain(await fixture.TickAsync(), IsRoomClosed);
    }

    [Fact]
    public async Task ReplacementWaitingBehindTheFinalPlayerExitKeepsTheRealSpectatorMembership()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.FinishAsync();
        fixture.Manager.LeaveRoom(fixture.Host);
        fixture.Manager.Disconnect(fixture.Observer);
        var replacement = Guid.NewGuid();
        var room = fixture.RuntimeRoom();
        var gate = (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        Task<L12SessionClaimResult>? pendingClaim = null;
        L12SessionClaimResult? claim = null;
        gate.Wait();
        try
        {
            pendingClaim = fixture.Manager.ConnectAsync(replacement, Fixture.ObserverAccount, "queued-observer");
            Assert.False(pendingClaim.IsCompleted);
            var left = (IReadOnlyList<OutgoingMessage>)typeof(L12RoomManager)
                .GetMethod("LeaveRoomLocked", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(fixture.Manager, [fixture.Guest])!;
            Assert.DoesNotContain(left, IsRoomClosed);
        }
        finally
        {
            gate.Release();
            if (pendingClaim is not null) claim = await pendingClaim;
        }
        Assert.NotNull(claim);
        Assert.True(claim.Recovered);
        Assert.Equal(fixture.RoomCode, claim.RoomCode);
        Assert.Contains(replacement, Spectators(room));
        Assert.DoesNotContain(fixture.Observer, Spectators(room));
        await fixture.SpectatorStateAsync(replacement);
        Assert.DoesNotContain(await fixture.TickAsync(), IsRoomClosed);
    }

    private static JsonElement Payload(OutgoingMessage message) => JsonSerializer.SerializeToElement(message.Payload, Wire);
    private static bool IsRoomClosed(OutgoingMessage message) => Payload(message).GetProperty("type").GetString() == "roomClosed";
    private static JsonElement PayloadFor(IEnumerable<OutgoingMessage> messages, Guid sessionId, string type)
        => Payload(Assert.Single(messages.Where(message => message.SessionId == sessionId
            && Payload(message).GetProperty("type").GetString() == type)));
    private static List<Guid> Spectators(object room) => (List<Guid>)room.GetType().GetProperty("Spectators")!.GetValue(room)!;

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string ObserverAccount = "settlement-observer-account";
        private readonly string _root;
        private readonly MatchRecorder _recorder;
        public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        public L12RoomManager Manager { get; }
        public Guid Host { get; } = Guid.NewGuid();
        public Guid Guest { get; } = Guid.NewGuid();
        public Guid Observer { get; private set; }
        public string RoomCode { get; private set; } = "";

        private Fixture(string root, MatchRecorder recorder)
        {
            _root = root;
            _recorder = recorder;
            Manager = new L12RoomManager(L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data")),
                recorder, utcNow: () => Now);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-spectator-settlement-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            try
            {
                await recorder.InitializeAsync();
                var fixture = new Fixture(root, recorder);
                fixture.Manager.Connect(fixture.Host, "settlement-host", "host");
                fixture.Manager.Connect(fixture.Guest, "settlement-guest", "guest");
                fixture.RoomCode = fixture.CreateRoom(fixture.Host);
                fixture.Manager.JoinRoom(fixture.Guest, fixture.RoomCode);
                await fixture.Manager.SetReadyAsync(fixture.Host, true);
                await fixture.Manager.SetReadyAsync(fixture.Guest, true);
                fixture.Observer = fixture.AddObserver("observer", ObserverAccount);
                return fixture;
            }
            catch
            {
                await recorder.DisposeAsync();
                PlatformTestDirectoryCleanup.Delete(root);
                throw;
            }
        }

        public Guid AddObserver(string name, string? account = null)
        {
            var id = Guid.NewGuid();
            Manager.Connect(id, account ?? name, name);
            Assert.True(PayloadFor(Manager.SpectateRoom(id, RoomCode), id, "gameState").GetProperty("spectating").GetBoolean());
            return id;
        }

        public string CreateRoom(Guid session)
            => PayloadFor(Manager.CreateRoom(session), session, "roomState").GetProperty("roomCode").GetString()!;

        public async Task<JsonElement> FinishAsync()
        {
            var messages = await Manager.HandleActionAsync(Host, JsonSerializer.SerializeToElement(new { type = "surrender" }));
            return AssertPublicTerminal(PayloadFor(messages, Observer, "gameState"));
        }

        public void LeaveBothPlayers()
        {
            Manager.LeaveRoom(Host);
            Manager.LeaveRoom(Guest);
        }

        public Task<IReadOnlyList<OutgoingMessage>> TickAsync() => Manager.TickRankedClocksAsync(Now);

        public async Task<JsonElement> SpectatorStateAsync(Guid session)
            => AssertPublicTerminal(PayloadFor(await Manager.RecoveryStateAsync(session), session, "gameState"));

        private static JsonElement AssertPublicTerminal(JsonElement message)
        {
            Assert.True(message.GetProperty("spectating").GetBoolean());
            var state = message.GetProperty("state");
            Assert.Equal(nameof(L12Phase.GameOver), state.GetProperty("phase").GetString());
            // Public spectators keep the existing seat-zero presentation orientation.
            Assert.Equal(0, state.GetProperty("you").GetInt32());
            Assert.Equal(1, state.GetProperty("winner").GetInt32());
            Assert.Empty(state.GetProperty("prompts").EnumerateArray());
            foreach (var player in state.GetProperty("players").EnumerateArray())
                Assert.False(player.TryGetProperty("hand", out _));
            return state;
        }

        public object RuntimeRoom()
        {
            var rooms = (IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Manager)!;
            var entry = rooms.Cast<object>().Single(row => (string)row.GetType().GetProperty("Key")!.GetValue(row)! == RoomCode);
            return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        }

        public async ValueTask DisposeAsync()
        {
            await _recorder.DisposeAsync();
            PlatformTestDirectoryCleanup.Delete(_root);
        }
    }
}
