using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SpectatorMatchmakingTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CancelAndLeaveKeepObservationAndQueueAsIndependentIntentions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("independent");
        var observer = fixture.Connect("independent-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);

        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "casual", null),
            observer.SessionId, true);
        AssertQueued(fixture.Manager.CancelMatchmaking(observer.SessionId), observer.SessionId, false);

        var stillWatching = PayloadFor(await fixture.Manager.RecoveryStateAsync(observer.SessionId),
            observer.SessionId, "gameState");
        Assert.True(stillWatching.GetProperty("spectating").GetBoolean());
        Assert.Equal(observed.MatchId, stillWatching.GetProperty("state").GetProperty("matchId").GetString());

        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "casual", null),
            observer.SessionId, true);
        var left = PayloadFor(fixture.Manager.LeaveRoom(observer.SessionId), observer.SessionId, "roomLeft");
        Assert.Equal(observed.RoomCode, left.GetProperty("roomCode").GetString());
        Assert.Empty(await fixture.Manager.RecoveryStateAsync(observer.SessionId));
        AssertQueued(await fixture.Manager.PollMatchmakingAsync(observer.SessionId), observer.SessionId, true);
    }

    [Fact]
    public async Task MatchFoundRetiresOldObservationBeforeNewFullPlayerBaseline()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("handover");
        var first = fixture.Connect("handover-first");
        var second = fixture.Connect("handover-second");
        fixture.Manager.SpectateRoom(first.SessionId, observed.RoomCode);
        fixture.Manager.SpectateRoom(second.SessionId, observed.RoomCode);

        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(first.SessionId, "casual", null),
            first.SessionId, true);
        var found = await fixture.Manager.JoinMatchmakingAsync(second.SessionId, "casual", null);

        foreach (var player in new[] { first, second })
        {
            var foundIndex = MessageIndex(found, player.SessionId, "matchmakingFound");
            var roomIndex = MessageIndex(found, player.SessionId, "roomState");
            var gameIndex = MessageIndex(found, player.SessionId, "gameState");
            Assert.True(foundIndex >= 0 && foundIndex < roomIndex && roomIndex < gameIndex);

            var handover = Payload(found[foundIndex]);
            Assert.Equal(observed.RoomCode, handover.GetProperty("retiredObservationRoomCode").GetString());
            Assert.Equal(observed.MatchId, handover.GetProperty("retiredObservationMatchId").GetString());
            Assert.NotEqual(observed.RoomCode, handover.GetProperty("roomCode").GetString());
            Assert.NotEqual(observed.MatchId, handover.GetProperty("matchId").GetString());

            var roomState = Payload(found[roomIndex]);
            Assert.Equal(handover.GetProperty("roomCode").GetString(),
                roomState.GetProperty("roomCode").GetString());
            Assert.Equal(JsonValueKind.Number, roomState.GetProperty("yourPlayerIndex").ValueKind);

            var gameMessage = found[gameIndex];
            Assert.True(gameMessage.IsGameState);
            Assert.True(gameMessage.ForceFullGameState);
            var gameState = Payload(gameMessage);
            Assert.False(gameState.TryGetProperty("spectating", out _));
            Assert.Equal(handover.GetProperty("matchId").GetString(),
                gameState.GetProperty("state").GetProperty("matchId").GetString());
        }

        var oldRoomRecovery = await fixture.Manager.RecoveryStateAsync(observed.Host.SessionId);
        Assert.DoesNotContain(oldRoomRecovery,
            message => message.SessionId == first.SessionId || message.SessionId == second.SessionId);
        foreach (var player in new[] { first, second })
        {
            var recovered = PayloadFor(await fixture.Manager.RecoveryStateAsync(player.SessionId),
                player.SessionId, "gameState");
            Assert.False(recovered.TryGetProperty("spectating", out _));
        }
    }

    [Fact]
    public async Task QueuedObserverReceivesPrivatePlayerBaselineAfterMatchHandover()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("queued-then-observe");
        var observer = fixture.Connect("queued-observer");
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "casual", null),
            observer.SessionId, true);
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);

        var opponent = fixture.Connect("queued-observer-opponent");
        var found = await fixture.Manager.JoinMatchmakingAsync(opponent.SessionId, "casual", null);
        var players = new[] { observer, opponent };
        var playerIndexes = new Dictionary<Guid, int>();

        foreach (var player in players)
        {
            var gameMessage = Assert.Single(found.Where(message =>
                message.SessionId == player.SessionId && MessageType(message) == "gameState"));
            Assert.True(gameMessage.IsGameState);
            Assert.True(gameMessage.ForceFullGameState);
            var gamePayload = Payload(gameMessage);
            Assert.False(gamePayload.TryGetProperty("spectating", out _));
            var state = gamePayload.GetProperty("state");
            var roomState = PayloadFor(found, player.SessionId, "roomState");
            var yourPlayerIndex = roomState.GetProperty("yourPlayerIndex").GetInt32();
            playerIndexes[player.SessionId] = yourPlayerIndex;
            AssertPlayerHandPrivacy(state, yourPlayerIndex, expectedHandCount: 0);
        }

        var handover = PayloadFor(found, observer.SessionId, "matchmakingFound");
        var matchedRoom = fixture.RuntimeRoom(handover.GetProperty("roomCode").GetString()!);
        await AdvanceSetupToMulliganAsync(fixture, matchedRoom, players, playerIndexes);

        foreach (var player in players)
        {
            var recovered = PayloadFor(await fixture.Manager.RecoveryStateAsync(player.SessionId),
                player.SessionId, "gameState");
            AssertPlayerHandPrivacy(recovered.GetProperty("state"), playerIndexes[player.SessionId],
                expectNonEmptyHand: true);
        }
    }

    [Fact]
    public async Task BusyObservationRoomRequeuesBothPlayersWithoutChangingObservation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("busy-observation");
        var first = fixture.Connect("busy-first");
        var second = fixture.Connect("busy-second");
        fixture.Manager.SpectateRoom(first.SessionId, observed.RoomCode);
        fixture.Manager.SpectateRoom(second.SessionId, observed.RoomCode);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(first.SessionId, "casual", null),
            first.SessionId, true);

        var roomGate = RoomGate(observed.RuntimeRoom);
        roomGate.Wait();
        Task<IReadOnlyList<OutgoingMessage>>? attempt = null;
        try
        {
            attempt = fixture.Manager.JoinMatchmakingAsync(second.SessionId, "casual", null);
            var completed = await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(attempt, completed);
            var busy = await attempt;
            Assert.Equal(2, busy.Count);
            AssertQueued(busy, first.SessionId, true);
            AssertQueued(busy, second.SessionId, true);
            Assert.DoesNotContain(busy, message => MessageType(message) == "matchmakingFound");
        }
        finally
        {
            roomGate.Release();
            if (attempt is not null) await attempt;
        }

        foreach (var player in new[] { first, second })
        {
            var stillWatching = PayloadFor(await fixture.Manager.RecoveryStateAsync(player.SessionId),
                player.SessionId, "gameState");
            Assert.True(stillWatching.GetProperty("spectating").GetBoolean());
            Assert.Equal(observed.MatchId,
                stillWatching.GetProperty("state").GetProperty("matchId").GetString());
        }

        var retried = await fixture.Manager.PollMatchmakingAsync(first.SessionId);
        Assert.NotEqual(-1, MessageIndex(retried, first.SessionId, "matchmakingFound"));
        Assert.NotEqual(-1, MessageIndex(retried, second.SessionId, "matchmakingFound"));
    }

    [Fact]
    public async Task PairCreationFailureRestoresBothObservationAndOriginalQueueEntries()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("rollback");
        var first = fixture.Connect("rollback-first");
        var second = fixture.Connect("rollback-second");
        fixture.Manager.SpectateRoom(first.SessionId, observed.RoomCode);
        fixture.Manager.SpectateRoom(second.SessionId, observed.RoomCode);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(first.SessionId, "casual", null),
            first.SessionId, true);

        fixture.Recorder.StorageFailureInjector = _ => throw new IOException("synthetic matchmaking start failure");
        IReadOnlyList<OutgoingMessage> failed;
        try
        {
            failed = await fixture.Manager.JoinMatchmakingAsync(second.SessionId, "casual", null);
        }
        finally
        {
            fixture.Recorder.StorageFailureInjector = null;
        }
        Assert.Equal(2, failed.Count(message => MessageType(message) == "matchmakingRejected"));
        foreach (var player in new[] { first, second })
        {
            AssertQueued(failed, player.SessionId, true);
            Assert.True(MessageIndex(failed, player.SessionId, "matchmakingRejected")
                        < MessageIndex(failed, player.SessionId, "matchmakingState"));
        }

        var recovery = await fixture.Manager.RecoveryStateAsync(first.SessionId);
        foreach (var player in new[] { first, second })
        {
            var game = PayloadFor(recovery, player.SessionId, "gameState");
            Assert.True(game.GetProperty("spectating").GetBoolean());
            Assert.Equal(observed.MatchId, game.GetProperty("state").GetProperty("matchId").GetString());
        }

        var retried = await fixture.Manager.PollMatchmakingAsync(first.SessionId);
        foreach (var player in new[] { first, second })
        {
            var handover = PayloadFor(retried, player.SessionId, "matchmakingFound");
            Assert.Equal(observed.RoomCode, handover.GetProperty("retiredObservationRoomCode").GetString());
            Assert.Equal(observed.MatchId, handover.GetProperty("retiredObservationMatchId").GetString());
        }
    }

    [Fact]
    public async Task SlowFailedPairDoesNotHoldTheOldObservationGateAndRequiresAResync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("slow-rollback");
        var first = fixture.Connect("slow-rollback-first");
        var second = fixture.Connect("slow-rollback-second");
        fixture.Manager.SpectateRoom(first.SessionId, observed.RoomCode);
        fixture.Manager.SpectateRoom(second.SessionId, observed.RoomCode);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(first.SessionId, "casual", null),
            first.SessionId, true);

        var recorderEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRecorder = new ManualResetEventSlim();
        Task<IReadOnlyList<OutgoingMessage>>? pairing = null;
        IReadOnlyList<OutgoingMessage>? failed = null;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-match-start-commit") return;
            recorderEntered.TrySetResult(true);
            if (!releaseRecorder.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("synthetic matchmaking recorder was not released");
            throw new IOException("synthetic slow matchmaking start failure");
        };
        try
        {
            pairing = fixture.Manager.JoinMatchmakingAsync(second.SessionId, "casual", null);
            var entered = await Task.WhenAny(recorderEntered.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(recorderEntered.Task, entered);

            var oldRoomGate = RoomGate(observed.RuntimeRoom);
            var acquired = oldRoomGate.Wait(0);
            if (acquired) oldRoomGate.Release();
            Assert.True(acquired, "new-match persistence must not hold the retired observation room gate");
        }
        finally
        {
            releaseRecorder.Set();
            if (pairing is not null) failed = await pairing;
            fixture.Recorder.StorageFailureInjector = null;
        }

        Assert.NotNull(failed);
        foreach (var player in new[] { first, second })
        {
            AssertQueued(failed, player.SessionId, true);
            var queueState = PayloadFor(failed, player.SessionId, "matchmakingState");
            Assert.True(queueState.GetProperty("observationResyncRequired").GetBoolean());
            var recovered = PayloadFor(await fixture.Manager.RecoveryStateAsync(player.SessionId),
                player.SessionId, "gameState");
            Assert.True(recovered.GetProperty("spectating").GetBoolean());
            Assert.Equal(observed.MatchId,
                recovered.GetProperty("state").GetProperty("matchId").GetString());
        }
    }

    [Fact]
    public async Task FailedPairCleanupDoesNotRemoveAReplacedRoomOwner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Connect("cleanup-first");
        var second = fixture.Connect("cleanup-second");
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(first.SessionId, "casual", null), first.SessionId, true);
        string? competingCode = null;
        object? competingOwner = null;
        fixture.Recorder.StorageFailureInjector = _ =>
        {
            (competingCode, competingOwner) = fixture.ReplacePendingMatchRoomOwner();
            throw new IOException("synthetic owner replacement during failed pair");
        };
        try { await fixture.Manager.JoinMatchmakingAsync(second.SessionId, "casual", null); }
        finally { fixture.Recorder.StorageFailureInjector = null; }
        Assert.NotNull(competingCode);
        Assert.Same(competingOwner, fixture.RuntimeRoom(competingCode));
    }

    [Fact]
    public async Task TournamentObservationAndMatchmakingRemainMutuallyExclusive()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("tournament-boundary");
        var tournamentObserver = fixture.Connect("tournament-observer");
        fixture.Manager.SpectateRoom(tournamentObserver.SessionId, observed.RoomCode);
        SetRoomProperty(observed.RuntimeRoom, "TournamentId", "synthetic-tournament");
        SetRoomProperty(observed.RuntimeRoom, "TournamentMatchId", "synthetic-match");

        var observerRejected = PayloadFor(
            await fixture.Manager.JoinMatchmakingAsync(tournamentObserver.SessionId, "casual", null),
            tournamentObserver.SessionId, "matchmakingRejected");
        Assert.Contains("赛事观战", observerRejected.GetProperty("message").GetString(),
            StringComparison.Ordinal);

        var queued = fixture.Connect("queued-tournament");
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(queued.SessionId, "casual", null),
            queued.SessionId, true);
        var directRoomRejected = PayloadFor(
            fixture.Manager.SpectateRoom(queued.SessionId, observed.RoomCode),
            queued.SessionId, "tournamentRoomRejected");
        Assert.Contains("取消匹配", directRoomRejected.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        var tournamentRejected = PayloadFor(
            fixture.Manager.SpectateTournamentMatch(queued.SessionId, "missing-tournament", "missing-match"),
            queued.SessionId, "tournamentRoomRejected");
        Assert.Contains("取消匹配", tournamentRejected.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedAdmissionStillAppliesWhileOrdinaryObservationContinues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("ranked-admission");
        var observer = fixture.Connect("ranked-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);

        var rejected = PayloadFor(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "ranked", null),
            observer.SessionId, "matchmakingRejected");
        Assert.Contains("选择本赛季派系", rejected.GetProperty("message").GetString(),
            StringComparison.Ordinal);
        var stillWatching = PayloadFor(await fixture.Manager.RecoveryStateAsync(observer.SessionId),
            observer.SessionId, "gameState");
        Assert.True(stillWatching.GetProperty("spectating").GetBoolean());

        fixture.Store.SelectRankedFaction(observer.AccountId, "order");
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "ranked", null),
            observer.SessionId, true);
    }

    [Fact]
    public async Task DisconnectRecoversObservationButDoesNotReviveTheOldQueueGeneration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("generation");
        var observer = fixture.Connect("generation-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "casual", null),
            observer.SessionId, true);

        fixture.Manager.Disconnect(observer.SessionId);
        var replacementSession = Guid.NewGuid();
        var claim = await fixture.Manager.ConnectAsync(replacementSession, observer.AccountId, observer.Name);
        Assert.True(claim.Recovered);
        Assert.Equal(observed.RoomCode, claim.RoomCode);
        AssertQueued(await fixture.Manager.PollMatchmakingAsync(replacementSession), replacementSession, false);

        var recovered = PayloadFor(await fixture.Manager.RecoveryStateAsync(replacementSession),
            replacementSession, "gameState");
        Assert.True(recovered.GetProperty("spectating").GetBoolean());
        Assert.Equal(observed.MatchId, recovered.GetProperty("state").GetProperty("matchId").GetString());
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(replacementSession, "casual", null),
            replacementSession, true);
    }

    [Fact]
    public async Task ConcurrentLeavePreventsAReplacementConnectionFromClaimingGhostObservationRecovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("leave-connect-race");
        var observer = fixture.Connect("leave-connect-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);
        var replacementSession = Guid.NewGuid();
        var roomGate = RoomGate(observed.RuntimeRoom);
        Task<L12SessionClaimResult>? replacement = null;
        L12SessionClaimResult? claim = null;
        IReadOnlyList<OutgoingMessage>? left = null;

        roomGate.Wait();
        try
        {
            replacement = fixture.Manager.ConnectAsync(replacementSession,
                observer.AccountId, observer.Name);
            Assert.False(replacement.IsCompleted);
            left = InvokeLeaveRoomLocked(fixture.Manager, observer.SessionId);
        }
        finally
        {
            roomGate.Release();
            if (replacement is not null) claim = await replacement;
        }

        Assert.NotNull(left);
        Assert.Equal(observed.RoomCode,
            PayloadFor(left, observer.SessionId, "roomLeft").GetProperty("roomCode").GetString());
        Assert.NotNull(claim);
        Assert.False(claim.Recovered);
        Assert.Null(claim.RoomCode);
        Assert.Empty(await fixture.Manager.RecoveryStateAsync(replacementSession));
        var spectators = RoomSpectators(observed.RuntimeRoom);
        Assert.DoesNotContain(observer.SessionId, spectators);
        Assert.DoesNotContain(replacementSession, spectators);
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("leave")]
    public async Task SpectatorRoomWaitDoesNotBlockIndependentConnection(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("isolated-room-wait");
        var observer = fixture.Connect("isolated-wait-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);
        var independent = fixture.Register("isolated-independent");
        var roomGate = RoomGate(observed.RuntimeRoom);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IReadOnlyList<OutgoingMessage>>? roomAction = null;
        Task<L12SessionClaimResult>? independentConnect = null;

        roomGate.Wait();
        try
        {
            roomAction = Task.Run(() =>
            {
                started.TrySetResult(true);
                return operation == "disconnect"
                    ? fixture.Manager.Disconnect(observer.SessionId)
                    : fixture.Manager.LeaveRoom(observer.SessionId);
            });
            await started.Task;
            await Task.Delay(50);
            Assert.False(roomAction.IsCompleted);

            independentConnect = fixture.Manager.ConnectAsync(independent.SessionId,
                independent.AccountId, independent.Name);
            var completed = await Task.WhenAny(independentConnect, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(independentConnect, completed);
            var claim = await independentConnect;
            Assert.False(claim.Recovered);
            Assert.Null(claim.RoomCode);
        }
        finally
        {
            roomGate.Release();
            try
            {
                if (roomAction is not null) await roomAction;
            }
            finally
            {
                if (independentConnect is not null) await independentConnect;
            }
        }
    }

    [Fact]
    public async Task DepartingClosedObservationStaysQueuedButCannotPairUntilLeave()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("departing-observation");
        var observer = fixture.Connect("departing-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "casual", null),
            observer.SessionId, true);
        SetRoomProperty(observed.RuntimeRoom, "Closed", true);

        AssertQueued(await fixture.Manager.PollMatchmakingAsync(observer.SessionId), observer.SessionId, true);
        var candidate = fixture.Connect("departing-candidate");
        var candidateQueued = await fixture.Manager.JoinMatchmakingAsync(candidate.SessionId, "casual", null);
        AssertQueued(candidateQueued, candidate.SessionId, true);
        Assert.DoesNotContain(candidateQueued, message => MessageType(message) == "matchmakingFound");

        var left = PayloadFor(fixture.Manager.LeaveRoom(observer.SessionId),
            observer.SessionId, "roomLeft");
        Assert.Equal(observed.RoomCode, left.GetProperty("roomCode").GetString());
        var found = await fixture.Manager.PollMatchmakingAsync(observer.SessionId);
        Assert.NotEqual(-1, MessageIndex(found, observer.SessionId, "matchmakingFound"));
        Assert.NotEqual(-1, MessageIndex(found, candidate.SessionId, "matchmakingFound"));
    }

    [Fact]
    public async Task SpectatorClosureAndAuthorizationExitMessagesCarryTheirRoomIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var closing = await fixture.CreateObservationRoomAsync("closing");
        var closingObserver = fixture.Connect("closing-observer");
        fixture.Manager.SpectateRoom(closingObserver.SessionId, closing.RoomCode);
        SetRoomProperty(closing.RuntimeRoom, "Game", null);

        var closed = PayloadFor(fixture.Manager.LeaveRoom(closing.Host.SessionId),
            closingObserver.SessionId, "roomClosed");
        Assert.Equal(closing.RoomCode, closed.GetProperty("roomCode").GetString());

        var revoked = await fixture.CreateObservationRoomAsync("revoked");
        var revokedObserver = fixture.Connect("revoked-observer");
        fixture.Manager.SpectateRoom(revokedObserver.SessionId, revoked.RoomCode);
        SetRoomProperty(revoked.RuntimeRoom, "TournamentId", "missing-tournament");
        SetRoomProperty(revoked.RuntimeRoom, "TournamentMatchId", "missing-match");

        var revokedMessages = await fixture.Manager.RecoveryStateAsync(revoked.Host.SessionId);
        var roomLeft = PayloadFor(revokedMessages, revokedObserver.SessionId, "roomLeft");
        Assert.Equal(revoked.RoomCode, roomLeft.GetProperty("roomCode").GetString());
    }

    [Fact]
    public async Task IncompleteObservationExitRetainsQueueButCannotPairUntilCleanupFinishes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observed = await fixture.CreateObservationRoomAsync("partial-exit");
        var observer = fixture.Connect("partial-exit-observer");
        fixture.Manager.SpectateRoom(observer.SessionId, observed.RoomCode);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(observer.SessionId, "casual", null),
            observer.SessionId, true);
        var sessions = (IEnumerable)typeof(L12RoomManager)
            .GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Manager)!;
        var sessionEntry = sessions.Cast<object>()
            .Single(item => (Guid)item.GetType().GetProperty("Key")!.GetValue(item)! == observer.SessionId);
        var session = sessionEntry.GetType().GetProperty("Value")!.GetValue(sessionEntry)!;
        var spectators = (IList)observed.RuntimeRoom.GetType().GetProperty("Spectators")!
            .GetValue(observed.RuntimeRoom)!;
        lock (spectators) spectators.Remove(observer.SessionId);
        // Pause the existing ClearRoomMembership transition after RoomCode was cleared,
        // but before its final IsSpectator write publishes a complete lobby membership.
        SetRoomProperty(session, "RoomCode", null);
        AssertQueued(await fixture.Manager.PollMatchmakingAsync(observer.SessionId), observer.SessionId, true);
        var candidate = fixture.Connect("partial-exit-candidate");
        var waiting = await fixture.Manager.JoinMatchmakingAsync(candidate.SessionId, "casual", null);
        AssertQueued(waiting, candidate.SessionId, true);
        Assert.DoesNotContain(waiting, message => MessageType(message) == "matchmakingFound");
        SetRoomProperty(session, "IsSpectator", false);
        var paired = await fixture.Manager.PollMatchmakingAsync(observer.SessionId);
        Assert.NotEqual(-1, MessageIndex(paired, observer.SessionId, "matchmakingFound"));
        Assert.NotEqual(-1, MessageIndex(paired, candidate.SessionId, "matchmakingFound"));
    }

    private static void AssertQueued(IReadOnlyList<OutgoingMessage> messages, Guid sessionId, bool expected)
    {
        var state = PayloadFor(messages, sessionId, "matchmakingState");
        Assert.Equal(expected, state.GetProperty("queued").GetBoolean());
    }

    private static void AssertPlayerHandPrivacy(JsonElement state, int yourPlayerIndex,
        int? expectedHandCount = null, bool expectNonEmptyHand = false)
    {
        Assert.Equal(yourPlayerIndex, state.GetProperty("you").GetInt32());
        var viewedPlayers = state.GetProperty("players").EnumerateArray().ToArray();
        var own = Assert.Single(viewedPlayers,
            viewed => viewed.GetProperty("playerIndex").GetInt32() == yourPlayerIndex);
        var rival = Assert.Single(viewedPlayers,
            viewed => viewed.GetProperty("playerIndex").GetInt32() != yourPlayerIndex);
        Assert.True(own.TryGetProperty("hand", out var privateHand));
        Assert.Equal(JsonValueKind.Array, privateHand.ValueKind);
        if (expectedHandCount is not null)
        {
            Assert.Equal(expectedHandCount.Value, privateHand.GetArrayLength());
            Assert.Equal(expectedHandCount.Value, rival.GetProperty("handCount").GetInt32());
        }
        if (expectNonEmptyHand)
        {
            Assert.True(privateHand.GetArrayLength() > 0);
            Assert.True(rival.GetProperty("handCount").GetInt32() > 0);
        }
        Assert.False(rival.TryGetProperty("hand", out _));
    }

    private static async Task AdvanceSetupToMulliganAsync(Fixture fixture, object runtimeRoom,
        IReadOnlyList<Peer> players, IReadOnlyDictionary<Guid, int> playerIndexes)
    {
        var sessions = new Guid[2];
        foreach (var player in players) sessions[playerIndexes[player.SessionId]] = player.SessionId;
        var game = (L12GameEngine)runtimeRoom.GetType()
            .GetProperty("Game", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(runtimeRoom)!;

        for (var step = 0; game.State.Phase != L12Phase.Mulligan && step < 32; step++)
        {
            var prompt = game.State.PendingPrompts
                .OrderBy(candidate => candidate.PlayerIndex)
                .ThenBy(candidate => candidate.PromptId, StringComparer.Ordinal)
                .FirstOrDefault();
            Assert.NotNull(prompt);
            var choices = prompt.Continuation == "setup-initiative"
                ? new List<string> { "first" }
                : prompt.ValidChoices.Contains("no", StringComparer.OrdinalIgnoreCase)
                    ? new List<string> { "no" }
                    : prompt.ValidChoices.Take(prompt.MinChoose).ToList();
            var messages = await fixture.Manager.HandleActionAsync(sessions[prompt.PlayerIndex],
                JsonSerializer.SerializeToElement(new L12Command("resolvePrompt",
                    CardInstanceIds: choices, PromptId: prompt.PromptId), WireJson),
                $"spectator-handover-setup-{step}");
            Assert.DoesNotContain(messages, message => MessageType(message) == "actionRejected");
        }
        Assert.Equal(L12Phase.Mulligan, game.State.Phase);
    }

    private static int MessageIndex(IReadOnlyList<OutgoingMessage> messages, Guid sessionId, string type)
    {
        for (var index = 0; index < messages.Count; index++)
            if (messages[index].SessionId == sessionId && MessageType(messages[index]) == type)
                return index;
        return -1;
    }

    private static JsonElement PayloadFor(IEnumerable<OutgoingMessage> messages, Guid sessionId, string type)
        => Payload(Assert.Single(messages.Where(message =>
            message.SessionId == sessionId && MessageType(message) == type)));

    private static string? MessageType(OutgoingMessage message)
    {
        var payload = Payload(message);
        return payload.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WireJson);

    private static void SetRoomProperty(object room, string name, object? value)
        => room.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(room, value);

    private static SemaphoreSlim RoomGate(object room)
        => (SemaphoreSlim)room.GetType()
            .GetProperty("Gate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(room)!;

    private static Guid[] RoomSpectators(object room)
        => ((IEnumerable)room.GetType()
                .GetProperty("Spectators", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(room)!)
            .Cast<Guid>().ToArray();

    private static IReadOnlyList<OutgoingMessage> InvokeLeaveRoomLocked(L12RoomManager manager, Guid sessionId)
        => (IReadOnlyList<OutgoingMessage>)typeof(L12RoomManager)
            .GetMethod("LeaveRoomLocked", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, [sessionId])!;

    private sealed record Peer(string AccountId, string Name, Guid SessionId);
    private sealed record Observation(Peer Host, Peer Guest, string RoomCode, string MatchId, object RuntimeRoom);

    private sealed class Fixture : IAsyncDisposable
    {
        private int _accountOrdinal;

        private Fixture(string root, L12Catalog catalog, L12PlatformStore store, MatchRecorder recorder,
            L12RoomManager manager)
        {
            Root = root;
            Catalog = catalog;
            Store = store;
            Recorder = recorder;
            Manager = manager;
        }

        public string Root { get; }
        public L12Catalog Catalog { get; }
        public L12PlatformStore Store { get; }
        public MatchRecorder Recorder { get; }
        public L12RoomManager Manager { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(),
                "l12-spectator-matchmaking-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            return new Fixture(root, catalog, store, recorder,
                new L12RoomManager(catalog, recorder, store));
        }

        public Peer Register(string stem)
        {
            var suffix = (++_accountOrdinal).ToString("D2");
            var username = "sm" + suffix;
            var registration = Store.Register(username, "password-123");
            Assert.True(registration.Success, $"{stem}: {registration.Message}");
            var account = registration.Account!;
            return new Peer(account.Id, account.Username, Guid.NewGuid());
        }

        public Peer Connect(string stem)
        {
            var peer = Register(stem);
            Manager.Connect(peer.SessionId, peer.AccountId, peer.Name);
            return peer;
        }

        public async Task<Observation> CreateObservationRoomAsync(string stem)
        {
            var host = Connect(stem + "-host");
            var guest = Connect(stem + "-guest");
            var created = Manager.CreateRoom(host.SessionId,
                new L12RoomOptions { Spectating = "public", DisasterMode = "none" });
            var roomCode = Payload(created[0]).GetProperty("roomCode").GetString()!;
            Manager.JoinRoom(guest.SessionId, roomCode);
            await Manager.SetReadyAsync(host.SessionId, true);
            var started = await Manager.SetReadyAsync(guest.SessionId, true);
            var game = PayloadFor(started, host.SessionId, "gameState");
            var matchId = game.GetProperty("state").GetProperty("matchId").GetString()!;
            return new Observation(host, guest, roomCode, matchId, RuntimeRoom(roomCode));
        }

        public (string Code, object Owner) ReplacePendingMatchRoomOwner()
        {
            var rooms = typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Manager)!;
            var entry = ((IEnumerable)rooms).Cast<object>().Single();
            var code = (string)entry.GetType().GetProperty("Key")!.GetValue(entry)!;
            var candidate = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
            var replacement = Activator.CreateInstance(candidate.GetType(), nonPublic: true)!;
            SetRoomProperty(replacement, "Code", code);
            Assert.True((bool)rooms.GetType().GetMethod("TryUpdate")!.Invoke(rooms, [code, replacement, candidate])!);
            return (code, replacement);
        }

        public object RuntimeRoom(string roomCode)
        {
            var rooms = (IEnumerable)typeof(L12RoomManager)
                .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Manager)!;
            return rooms.Cast<object>().Select(entry => new
                {
                    Key = (string)entry.GetType().GetProperty("Key")!.GetValue(entry)!,
                    Value = entry.GetType().GetProperty("Value")!.GetValue(entry)!,
                })
                .Single(entry => entry.Key == roomCode).Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(Root, "matches.db") }.ToString());
            SqliteConnection.ClearPool(pool);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
