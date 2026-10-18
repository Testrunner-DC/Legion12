using System.Text.Json;
using System.Reflection;
using System.Net.WebSockets;
using System.Text;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TutorialRoomLifecycleTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DeployUsesLegalPaymentDurableJournalAndOnlyThenCompletes()
    {
        await using var f = await Fixture.Create();
        var initial = Message(await f.Manager.CreateTutorialAsync(f.Student, "deploy", "create-1"), "gameState");
        Assert.Equal("deploy", initial.GetProperty("tutorial").GetProperty("stepId").GetString());
        Assert.False(initial.GetProperty("gmEnabled").GetBoolean());
        var state = initial.GetProperty("state");
        var self = state.GetProperty("players")[0];
        var card = self.GetProperty("hand").EnumerateArray().First(card => card.GetProperty("cardId").GetString() == "S01-0116");
        var result = await f.Manager.HandleActionAsync(f.Student, JsonSerializer.SerializeToElement(new
        {
            type = "playCard", cardInstanceId = card.GetProperty("instanceId").GetString(), row = 0, slot = 0,
            cardInstanceIds = new[] { self.GetProperty("morale")[0].GetProperty("instanceId").GetString() },
        }), "paid-deploy-1");
        var final = Message(result, "gameState");
        Assert.Equal("completed", final.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal("paid-deploy-1", final.GetProperty("requestId").GetString());
        var loaded = await f.Recorder.LoadJournalEngineAsync(state.GetProperty("matchId").GetString()!);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.CommandSequence);
        Assert.NotNull(loaded.Engine.State.Players[0].Field[0][0]);
        Assert.Single(loaded.Engine.State.Players[0].Morale.Where(resource => resource.Tapped));
        Assert.Empty(await f.Recorder.ListMatchesForPlayerAsync("learner"));
    }

    [Fact]
    public async Task OrdinaryMoveReallyPaysAndDuplicateDoesNotPayOrAdvanceAgain()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start("move");
        var self = initial.GetProperty("state").GetProperty("players")[0];
        var card = self.GetProperty("field")[1][1];
        Assert.Equal("S01-0116", card.GetProperty("cardId").GetString());
        var command = new L12Command("move", card.GetProperty("instanceId").GetString(), 0, 1,
            CardInstanceIds: [self.GetProperty("morale").EnumerateArray()
                .First(resource => !resource.GetProperty("tapped").GetBoolean()).GetProperty("instanceId").GetString()!]);
        var final = Message(await f.Act(command, "move-1"), "gameState");
        Assert.Equal("completed", final.GetProperty("tutorial").GetProperty("status").GetString());
        var match = final.GetProperty("state").GetProperty("matchId").GetString()!;
        var loaded = (await f.Recorder.LoadJournalEngineAsync(match))!;
        Assert.Equal(1, loaded.CommandSequence);
        Assert.Single(loaded.Engine.State.Players[0].Morale.Where(card => card.Tapped));
        var hash = loaded.Engine.ComputeStateHash();
        var duplicate = Message(await f.Act(command, "move-1"), "gameState");
        Assert.Equal("move-1", duplicate.GetProperty("requestId").GetString());
        Assert.Equal(hash, (await f.Recorder.LoadJournalEngineAsync(match))!.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task CombatRequiresOwnAttackThenEndTurnThenAnActualDefenseChoice()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start("combat");
        var card = initial.GetProperty("state").GetProperty("players")[0].GetProperty("field")[0][0];
        var attack = new L12Command("attack", card.GetProperty("instanceId").GetString(), Target: new("master"));
        var afterAttack = await f.PassOwnResponses(Message(await f.Act(attack, "attack-1"), "gameState"));
        Assert.Equal("end-turn", afterAttack.GetProperty("tutorial").GetProperty("stepId").GetString());
        Assert.Equal("active", afterAttack.GetProperty("tutorial").GetProperty("status").GetString());
        var defense = await f.PassOwnResponses(Message(await f.Act(new("endTurn"), "end-1"), "gameState"));
        Assert.Equal("defend", defense.GetProperty("tutorial").GetProperty("stepId").GetString());
        Assert.Equal("active", defense.GetProperty("tutorial").GetProperty("status").GetString());
        var duplicate = Message(await f.Act(new("endTurn"), "end-1"), "gameState");
        Assert.Equal(defense.GetProperty("state").GetProperty("revision").GetInt64(),
            duplicate.GetProperty("state").GetProperty("revision").GetInt64());
        var final = await f.PassOwnResponses(Message(await f.Act(new("resolveDefense", CardInstanceIds: []), "defense-1"), "gameState"));
        Assert.Equal("completed", final.GetProperty("tutorial").GetProperty("status").GetString());
        var loaded = (await f.Recorder.LoadJournalEngineAsync(final.GetProperty("state").GetProperty("matchId").GetString()!))!;
        Assert.Null(loaded.Engine.State.PendingDefense);
        Assert.True(loaded.CommandSequence >= 5); // Own attack + opponent defense + turn + opponent attack + own defense.
        Assert.Equal(0, loaded.ProcessedRequests.Single(request => request.RequestId == "defense-1").PlayerIndex);
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("move")]
    [InlineData("combat")]
    public async Task TutorialKeepsOrdinaryPrivacyDeniesGmAndSurrenderDoesNotComplete(string lesson)
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start(lesson);
        Assert.False(initial.GetProperty("gmEnabled").GetBoolean());
        Assert.False(initial.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        Assert.DoesNotContain(await f.Manager.HandleGmActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "addMorale", targetPlayer = 0, value = 9 })), IsGame);
        Assert.DoesNotContain(await f.Manager.HandleSandboxActionAsync(f.Student, 1,
            JsonSerializer.SerializeToElement(new { type = "surrender" })), IsGame);
        var final = Message(await f.Act(new("surrender"), "surrender"), "gameState");
        Assert.Equal("active", final.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal(nameof(L12Phase.GameOver), final.GetProperty("state").GetProperty("phase").GetString());
        Assert.Empty(await f.Recorder.ListMatchesForAccountAsync(f.AccountId, "learner"));
        var ordinary = new L12GameEngine(f.Catalog, "normal-positive", "NORMAL", 12,
            ["learner", "ordinary-opponent"], [0, 1], skipPreparation: true, disasterMode: "none",
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        Assert.True(ordinary.Handle(0, new("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(ordinary.Handle(1, new("mulligan", CardInstanceIds: [])).Accepted);
        await f.Recorder.StartAsync(ordinary, "friendly", f.AccountId, "normal-opponent", [f.Catalog.DeckAt(0), f.Catalog.DeckAt(1)]);
        var surrender = new L12Command("surrender");
        var result = ordinary.Handle(0, surrender);
        await f.Recorder.AppendAsync(ordinary, 1, 0, JsonSerializer.Serialize(surrender), result);
        await f.Recorder.CompleteAsync(ordinary);
        Assert.Equal("normal-positive", Assert.Single(await f.Recorder.ListMatchesForAccountAsync(f.AccountId, "learner")).MatchId);
        Assert.Equal("normal-positive", Assert.Single(await f.Recorder.ListMatchesForPlayerAsync("learner")).MatchId);
    }

    [Fact]
    public async Task ConcurrentCreateReconnectAndLeaveKeepOneOwnedLessonAndExpireOldCreate()
    {
        await using var f = await Fixture.Create();
        var both = await Task.WhenAll(f.Manager.CreateTutorialAsync(f.Student, "move", "same-create"),
            f.Manager.CreateTutorialAsync(f.Student, "move", "same-create"));
        var initial = Message(both[0], "gameState");
        var matchId = initial.GetProperty("state").GetProperty("matchId").GetString();
        Assert.Equal(matchId, Message(both[1], "gameState").GetProperty("state").GetProperty("matchId").GetString());
        Assert.DoesNotContain(await f.Manager.CreateTutorialAsync(f.Student, "combat", "same-create"), IsGame);
        Assert.Equal(1, f.Manager.RuntimeStats().RoomCount);
        f.Manager.Disconnect(f.Student);
        var replacement = Guid.NewGuid();
        Assert.True((await f.Manager.ConnectAsync(replacement, f.AccountId, "learner")).Recovered);
        var recovered = Message(await f.Manager.RecoveryStateAsync(replacement), "gameState");
        Assert.Equal(initial.GetProperty("tutorial").GetRawText(), recovered.GetProperty("tutorial").GetRawText());
        Assert.Equal(matchId, recovered.GetProperty("state").GetProperty("matchId").GetString());
        Assert.DoesNotContain(await f.Manager.HandleActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "surrender" })), IsGame);
        Assert.Contains(f.Manager.LeaveRoom(replacement), message => Type(message) == "roomLeft");
        Assert.Equal(0, f.Manager.RuntimeStats().RoomCount);
        Assert.Equal("tutorialRequestExpired", Type(Assert.Single(await f.Manager.CreateTutorialAsync(replacement, "move", "same-create"))));
        var next = Message(await f.Manager.CreateTutorialAsync(replacement, "move", "new-create"), "gameState");
        Assert.NotEqual(matchId, next.GetProperty("state").GetProperty("matchId").GetString());
        var sessions = typeof(L12RoomManager).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Manager)!;
        var member = sessions.GetType().GetProperty("Item")!.GetValue(sessions, [replacement])!;
        var requests = (System.Collections.IDictionary)member.GetType().GetProperty("TutorialRequests")!.GetValue(member)!;
        var oldReceipt = requests["same-create"]!;
        // Deterministically simulate recycling the old code; the immutable match binding still differs.
        oldReceipt.GetType().GetProperty("RoomCode")!.SetValue(oldReceipt, next.GetProperty("state").GetProperty("roomCode").GetString());
        Assert.Equal("tutorialRequestExpired", Type(Assert.Single(await f.Manager.CreateTutorialAsync(replacement, "move", "same-create"))));
    }

    [Fact]
    public async Task QueuedOldTutorialActionCannotWriteAfterNewSessionTakesTheExactRoomSeat()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start("move");
        var match = initial.GetProperty("state").GetProperty("matchId").GetString()!;
        var source = initial.GetProperty("state").GetProperty("players")[0].GetProperty("field")[1][1].GetProperty("instanceId").GetString();
        var gate = f.RoomGate();
        var replacement = Guid.NewGuid();
        Task<L12SessionClaimResult>? claim = null;
        Task<IReadOnlyList<OutgoingMessage>>? stale = null;
        await gate.WaitAsync();
        try
        {
            // Both methods run synchronously to their first unavailable gate. Claim is queued first.
            claim = f.Manager.ConnectAsync(replacement, f.AccountId, "learner");
            Assert.False(claim.IsCompleted);
            stale = f.Act(new("move", source, 0, 1), "stale-after-claim");
            Assert.False(stale.IsCompleted);
        }
        finally
        {
            gate.Release();
            await Task.WhenAll(new Task?[] { claim, stale }.OfType<Task>());
        }
        Assert.True((await claim!).Recovered);
        Assert.DoesNotContain(await stale!, IsGame);
        Assert.Equal(0, (await f.Recorder.LoadJournalEngineAsync(match))!.CommandSequence);
        var recovered = Message(await f.Manager.RecoveryStateAsync(replacement), "gameState");
        Assert.Equal("move", recovered.GetProperty("tutorial").GetProperty("stepId").GetString());
        Assert.Equal(initial.GetProperty("state").GetProperty("revision").GetInt64(), recovered.GetProperty("state").GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task BusyCreateRetryRejectsWithoutBlockingUnrelatedConnectOrProjectingUncommittedMaintenance()
    {
        await using var f = await Fixture.Create();
        f.Now = DateTimeOffset.UtcNow;
        await f.Start("move");
        var other = f.Platform.Register("other", "Password123!").Account!;
        f.ScheduledMaintenance();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-match-command-commit") return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("authority fixture barrier");
        };
        var tick = Task.Run(() => f.Manager.TickRankedClocksAsync(f.Now));
        Task<IReadOnlyList<OutgoingMessage>>? retry = null;
        Task<L12SessionClaimResult>? unrelated = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            retry = f.Manager.CreateTutorialAsync(f.Student, "move", "start-move");
            unrelated = f.Manager.ConnectAsync(Guid.NewGuid(), other.Id, other.Username);
            Assert.True(retry.IsCompleted); // A busy room never holds the global session gate waiting.
            var busy = Assert.Single(await retry);
            Assert.Equal("tutorialRejected", Type(busy));
            Assert.Equal("start-move", JsonSerializer.SerializeToElement(busy.Payload).GetProperty("requestId").GetString());
            Assert.DoesNotContain(await retry, IsGame); // Never snapshot speculative GameOver.
            Assert.True(unrelated.IsCompleted);
            Assert.False((await unrelated).Recovered);
        }
        finally
        {
            release.Set();
            await tick;
            if (retry is not null) await retry;
            if (unrelated is not null) await unrelated;
            f.Recorder.StorageFailureInjector = null;
        }
        // After the authority commit releases the room, the original request can read it safely.
        var snapshot = Message(await f.Manager.CreateTutorialAsync(f.Student, "move", "start-move"), "gameState");
        var loaded = (await f.Recorder.LoadJournalEngineAsync(snapshot.GetProperty("state").GetProperty("matchId").GetString()!))!;
        Assert.Equal(L12Phase.GameOver, loaded.Engine.State.Phase);
        Assert.Equal(loaded.Engine.State.Revision, snapshot.GetProperty("state").GetProperty("revision").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedMaintenanceAuthorityAppendCannotPublishOrFinalizeAnUnjournaledTutorial(bool failReload)
    {
        await using var f = await Fixture.Create();
        f.Now = DateTimeOffset.UtcNow;
        var initial = await f.Start("move");
        var match = initial.GetProperty("state").GetProperty("matchId").GetString()!;
        var before = (await f.Recorder.LoadJournalEngineAsync(match))!.Engine.ComputeStateHash();
        f.ScheduledMaintenance();
        var connection = typeof(MatchRecorder).GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = connection.GetValue(f.Recorder);
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-match-command-commit") return;
            if (failReload) connection.SetValue(f.Recorder, $"Data Source={Path.Combine(f.Root, "missing", "unavailable.db")};Mode=ReadOnly");
            throw new IOException("synthetic authority append failure");
        };
        IReadOnlyList<OutgoingMessage> tickMessages = [];
        Exception? failure;
        try
        {
            failure = await Record.ExceptionAsync(async () => tickMessages = await f.Manager.TickRankedClocksAsync(f.Now));
        }
        finally { f.Recorder.StorageFailureInjector = null; connection.SetValue(f.Recorder, original); }
        var room = f.RuntimeRoom();
        var lesson = room.GetType().GetProperty("Tutorial")!.GetValue(room)!;
        Assert.True((bool)lesson.GetType().GetProperty("Frozen")!.GetValue(lesson)!);
        Assert.Equal(failReload, (bool)room.GetType().GetProperty("Closed")!.GetValue(room)!);
        // Exercise the previously reachable GameOver completion branch, not just the failed tick.
        var nextTick = await f.Manager.TickRankedClocksAsync(f.Now);
        var followUp = await f.Act(new("surrender"), "after-failed-authority");
        Assert.DoesNotContain(tickMessages, IsGame);
        Assert.DoesNotContain(nextTick, IsGame);
        Assert.DoesNotContain(followUp, IsGame);
        var preference = await f.Manager.SetResponsePreferenceAsync(f.Student,
            L12GameEngine.ValidOnlyResponseMode, "after-failed-maintenance-preference");
        Assert.DoesNotContain(preference, IsGame);
        Assert.DoesNotContain(preference, message => Type(message) == "responsePreferenceResult");
        Assert.Equal(L12GameEngine.DefaultResponseMode, f.Platform.ResponsePreference(f.AccountId));
        Assert.Null(await f.Recorder.LatestPendingResponsePreferenceAsync(f.AccountId));
        if (!failReload)
        {
            Assert.Contains(await f.Manager.GetResponsePreferenceAsync(f.Student),
                message => Type(message) == "responsePreferenceState");
            Assert.Contains(await f.Manager.RecoveryStateAsync(f.Student), IsGame);
        }
        var durable = (await f.Recorder.LoadJournalEngineAsync(match))!;
        Assert.Equal(0, durable.CommandSequence);
        Assert.Equal(before, durable.Engine.ComputeStateHash());
        Assert.NotEqual(L12Phase.GameOver, durable.Engine.State.Phase);
        if (failReload)
        {
            // The recorder-wide outage also reaches the later, independent tournament outbox.
            // Preserve that read failure; do not require the tutorial fix to swallow unrelated work.
            Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(failure);
            Assert.Contains(nameof(MatchRecorder.ListPendingTournamentResultsAsync), failure!.StackTrace);
        }
        else Assert.Null(failure);
        Assert.Contains(f.Manager.LeaveRoom(f.Student), message => Type(message) == "roomLeft");
        Assert.Equal(before, (await f.Recorder.LoadJournalEngineAsync(match))!.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task FrozenTutorialDoesNotExpireAnOtherwiseDueResponseLeaseButKeepsReadAndExit()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start("move");
        var room = f.RuntimeRoom();
        var lesson = room.GetType().GetProperty("Tutorial")!.GetValue(room)!;
        var deck = f.Catalog.DeckAt(0);
        // Same public synthetic kernel arrangement as the existing response-watchdog guards.
        // This isolates the Frozen write barrier; it is not evidence of a playable lesson recipe.
        var game = new L12GameEngine(f.Catalog, Guid.NewGuid().ToString("N"),
            initial.GetProperty("state").GetProperty("roomCode").GetString()!, 120912,
            ["learner", "教学对手"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion, utcNow: () => f.Now,
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
            StackItemId = "tutorial-frozen-response", Controller = 1, SourceInstanceId = "source",
            SourceCardId = "S01-0103", SourceName = "synthetic source", Trigger = "reaction", Text = "测试响应时点",
        };
        game.State.ActivePlayer = 0;
        game.State.EffectStack.Add(stack);
        typeof(L12GameEngine).GetMethod("BeginResponseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [stack]);
        var lease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
        await f.Recorder.StartAsync(game, "sandbox", f.AccountId, null, [deck, deck]);
        room.GetType().GetProperty("Game")!.SetValue(room, game);
        lesson.GetType().GetProperty("Frozen")!.SetValue(lesson, true);
        var before = game.ComputeStateHash();
        Assert.Empty(await f.Manager.TickResponseWindowsAsync(lease.DeadlineUtc));
        Assert.Equal(lease, game.CaptureResponseAutoCloseLease());
        Assert.Equal(before, game.ComputeStateHash());
        var durable = (await f.Recorder.LoadJournalEngineAsync(game.State.MatchId))!;
        Assert.Equal(0, durable.CommandSequence);
        Assert.Equal(before, durable.Engine.ComputeStateHash());
        Assert.Contains(await f.Manager.GetResponsePreferenceAsync(f.Student),
            message => Type(message) == "responsePreferenceState");
        Assert.Contains(f.Manager.LeaveRoom(f.Student), message => Type(message) == "roomLeft");
        Assert.Equal(before, (await f.Recorder.LoadJournalEngineAsync(game.State.MatchId))!.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task TutorialDisconnectGraceRetiresItsOwnHolderWithoutGm()
    {
        await using var f = await Fixture.Create();
        await f.Start("move");
        f.Manager.Disconnect(f.Student);
        f.Now += L12RoomManager.SandboxReconnectGrace;
        await f.Manager.RunSandboxReplayMaintenanceAsync();
        Assert.Equal(0, f.Manager.RuntimeStats().RoomCount);
    }

    [Fact]
    public async Task QueuedPlayerMustExplicitlyCancelBeforeEnteringTutorial()
    {
        await using var f = await Fixture.Create();
        Assert.True(Message(await f.Manager.JoinMatchmakingAsync(f.Student, "casual", null), "matchmakingState").GetProperty("queued").GetBoolean());
        var rejected = Assert.Single(await f.Manager.CreateTutorialAsync(f.Student, "deploy", "queued-create"));
        Assert.Equal("tutorialRejected", Type(rejected));
        Assert.Equal("queued-create", JsonSerializer.SerializeToElement(rejected.Payload).GetProperty("requestId").GetString());
        Assert.Equal(0, f.Manager.RuntimeStats().RoomCount);
        Assert.False(Message(f.Manager.CancelMatchmaking(f.Student), "matchmakingState").GetProperty("queued").GetBoolean());
        Assert.Equal("deploy", (await f.Start("deploy")).GetProperty("tutorial").GetProperty("stepId").GetString());
    }

    [Fact]
    public async Task MaintenanceRejectionEchoesRequestAndSameCreateCanSucceedAfterMaintenanceEnds()
    {
        await using var f = await Fixture.Create();
        Assert.Equal("deploy", (await f.Start("deploy")).GetProperty("tutorial").GetProperty("stepId").GetString());
        f.Manager.LeaveRoom(f.Student);
        var admin = f.Platform.Login("Admin", "L12master").Account!;
        var current = f.Platform.OperationsConfig(admin);
        var maintenance = f.Platform.BeginImmediateMaintenance(admin, 3, current.Version,
            "synthetic tutorial maintenance", new("tutorial-maintenance-start"));
        var rejected = Assert.Single(await f.Manager.CreateTutorialAsync(f.Student, "move", "maintenance-create"));
        Assert.Equal("tutorialRejected", Type(rejected));
        Assert.Equal("maintenance-create", JsonSerializer.SerializeToElement(rejected.Payload).GetProperty("requestId").GetString());
        f.Platform.EndImmediateMaintenance(admin, maintenance.Current.Version,
            "synthetic tutorial maintenance finished", new("tutorial-maintenance-end"));
        Assert.Equal("move", Message(await f.Manager.CreateTutorialAsync(f.Student, "move", "maintenance-create"), "gameState")
            .GetProperty("tutorial").GetProperty("stepId").GetString());
    }

    [Fact]
    public async Task HumanAppendFailureDoesNotCompleteAndSameRequestCanRetryAfterRestore()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start("move");
        var source = initial.GetProperty("state").GetProperty("players")[0].GetProperty("field")[1][1].GetProperty("instanceId").GetString();
        var command = new L12Command("move", source, 0, 1);
        f.Recorder.StorageFailureInjector = stage => { if (stage == "before-match-command-commit") throw new IOException("synthetic write failure"); };
        Assert.DoesNotContain(await f.Act(command, "retry-move"), IsGame);
        f.Recorder.StorageFailureInjector = null;
        var recovered = Message(await f.Manager.RecoveryStateAsync(f.Student), "gameState");
        Assert.Equal("active", recovered.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal(initial.GetProperty("state").GetProperty("revision").GetInt64(), recovered.GetProperty("state").GetProperty("revision").GetInt64());
        Assert.Equal("completed", Message(await f.Act(command, "retry-move"), "gameState").GetProperty("tutorial").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpponentAppendFailureNeverReplaysHumanOrPublishesUncommittedStateAndCanSafelyLeave(bool failReload)
    {
        await using var f = await Fixture.Create();
        var initial = await f.Start("combat");
        var source = initial.GetProperty("state").GetProperty("players")[0].GetProperty("field")[0][0].GetProperty("instanceId").GetString();
        var connection = typeof(MatchRecorder).GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = connection.GetValue(f.Recorder);
        var writes = 0;
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-match-command-commit" || ++writes != 2) return;
            if (failReload) connection.SetValue(f.Recorder, $"Data Source={Path.Combine(f.Root, "missing", "unavailable.db")};Mode=ReadOnly");
            throw new IOException("synthetic opponent failure");
        };
        try
        {
            var failed = await f.Act(new("attack", source, Target: new("master")), "committed-attack");
            Assert.Contains(failed, message => Type(message) == "tutorialRestartRequired");
            Assert.DoesNotContain(await f.Manager.CreateTutorialAsync(f.Student, "combat", "start-combat"), IsGame);
            if (failReload)
            {
                Assert.DoesNotContain(failed, IsGame);
                Assert.DoesNotContain(await f.Manager.RecoveryStateAsync(f.Student), IsGame);
                Assert.DoesNotContain(f.Manager.LeaveRoom(f.Student), message => Type(message) == "roomLeft");
            }
            f.Recorder.StorageFailureInjector = null;
            connection.SetValue(f.Recorder, original);
            var durable = (await f.Recorder.LoadJournalEngineAsync(initial.GetProperty("state").GetProperty("matchId").GetString()!))!;
            Assert.Equal(1, durable.CommandSequence);
            Assert.Equal(0, durable.Engine.State.PendingDefense!.AttackerPlayer);
            var hash = durable.Engine.ComputeStateHash();
            if (!failReload)
            {
                var readOnly = await f.Manager.RecoveryStateAsync(f.Student);
                Assert.Contains(readOnly, message => Type(message) == "tutorialRestartRequired");
                Assert.Equal("attack", Message(readOnly, "gameState").GetProperty("tutorial").GetProperty("stepId").GetString());
                Assert.Equal(1, (await f.Recorder.LoadJournalEngineAsync(durable.Engine.State.MatchId))!.CommandSequence);
                Assert.Equal(hash, (await f.Recorder.LoadJournalEngineAsync(durable.Engine.State.MatchId))!.Engine.ComputeStateHash());
            }
            Assert.Contains(f.Manager.LeaveRoom(f.Student), message => Type(message) == "roomLeft");
            Assert.Equal(hash, (await f.Recorder.LoadJournalEngineAsync(durable.Engine.State.MatchId))!.Engine.ComputeStateHash());
            Assert.Equal(0, f.Manager.RuntimeStats().RoomCount);
        }
        finally { f.Recorder.StorageFailureInjector = null; connection.SetValue(f.Recorder, original); }
    }

    [Fact]
    public async Task DisabledTutorialAdvertisesFalseAndRejectsWithoutCreatingRoom()
    {
        await using var f = await Fixture.Create(enabled: false);
        Assert.False(f.Manager.TutorialAvailable);
        Assert.False(JsonSerializer.SerializeToElement(f.Manager.Connect(Guid.NewGuid(), "unused", "unused"))
            .GetProperty("tutorialAvailable").GetBoolean());
        Assert.Equal("tutorialUnavailable", Type(Assert.Single(await f.Manager.CreateTutorialAsync(f.Student, "deploy", "no"))));
        Assert.Equal(0, f.Manager.RuntimeStats().RoomCount);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("true", true)]
    public async Task ServerFlagDefaultsOffAndOnlyExplicitTrueEnables(string? setting, bool expected)
    {
        var previous = Environment.GetEnvironmentVariable("L12_TUTORIAL_ENABLED");
        try
        {
            Environment.SetEnvironmentVariable("L12_TUTORIAL_ENABLED", setting);
            await using var f = await Fixture.Create();
            Assert.Equal(expected, new L12RoomManager(f.Catalog, f.Recorder, f.Platform).TutorialAvailable);
        }
        finally { Environment.SetEnvironmentVariable("L12_TUTORIAL_ENABLED", previous); }
    }

    [Fact]
    public async Task AtomicClosedReservationCannotBeJoinedOrSpectatedAndFailedStartRemovesOnlyItself()
    {
        await using var f = await Fixture.Create();
        var outsider = Guid.NewGuid();
        f.Manager.Connect(outsider, "outsider", "outsider");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-match-start-commit") return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("fixture barrier");
            throw new IOException("synthetic start failure");
        };
        var create = Task.Run(() => f.Manager.CreateTutorialAsync(f.Student, "deploy", "reserved"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var dictionary = (System.Collections.IEnumerable)typeof(L12RoomManager)
                .GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Manager)!;
            var entry = dictionary.Cast<object>().Single();
            var code = (string)entry.GetType().GetProperty("Key")!.GetValue(entry)!;
            var room = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
            Assert.True((bool)room.GetType().GetProperty("Closed")!.GetValue(room)!);
            Assert.DoesNotContain(f.Manager.JoinRoom(outsider, code), IsGame);
            Assert.Equal("error", Type(Assert.Single(f.Manager.JoinRoom(outsider, code))));
            Assert.Equal("error", Type(Assert.Single(f.Manager.SpectateRoom(outsider, code))));
        }
        finally { release.Set(); await create; f.Recorder.StorageFailureInjector = null; }
        Assert.Equal(0, f.Manager.RuntimeStats().RoomCount);
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "服务端WebSocket", "TwelveLegions", "L12RoomManager.Tutorial.cs"));
        Assert.Contains("while (!_rooms.TryAdd(room.Code, room))", source);
        Assert.DoesNotContain("_rooms[room.Code] =", source);
    }

    [Fact]
    public async Task RealAuthenticatedWebSocketAdvertisesTutorialAndBindsPaidCompletionToRequest()
    {
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
        try
        {
            await using var f = await Fixture.Create();
            await using var server = new L12WebSocketServer(f.Manager, f.Recorder, f.Platform, f.Catalog);
            await server.StartAsync(0);
            using var socket = new ClientWebSocket();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Scheme = "ws", Path = "/ws" }.Uri;
            await socket.ConnectAsync(endpoint, deadline.Token);
            var token = f.Platform.Login("learner", "Password123!").Token!;
            await SendWire(socket, new { type = "hello", authToken = token, capabilities = new { requestIds = true } }, deadline.Token);
            Assert.True((await ReceiveWire(socket, "session", deadline.Token)).GetProperty("tutorialAvailable").GetBoolean());
            await SendWire(socket, new { type = "createTutorial", lessonId = "deploy", requestId = "wire-create", seed = 1 }, deadline.Token);
            Assert.Equal("wire-create", (await ReceiveWire(socket, "tutorialRejected", deadline.Token)).GetProperty("requestId").GetString());
            await SendWire(socket, new { type = "createSandbox" }, deadline.Token);
            await ReceiveWire(socket, "sandboxForbidden", deadline.Token);
            await SendWire(socket, new { type = "createTutorial", lessonId = "deploy", requestId = "wire-create" }, deadline.Token);
            var room = await ReceiveWire(socket, "roomState", deadline.Token);
            Assert.False(room.GetProperty("sandbox").GetBoolean());
            Assert.Equal("deploy", room.GetProperty("tutorial").GetProperty("lessonId").GetString());
            var initial = await ReceiveWire(socket, "gameState", deadline.Token);
            var self = initial.GetProperty("state").GetProperty("players")[0];
            var card = self.GetProperty("hand").EnumerateArray().First(card => card.GetProperty("cardId").GetString() == "S01-0116");
            await SendWire(socket, new { type = "gameAction", requestId = "wire-pay", command = new
            {
                type = "playCard", cardInstanceId = card.GetProperty("instanceId").GetString(), row = 0, slot = 0,
                cardInstanceIds = new[] { self.GetProperty("morale")[0].GetProperty("instanceId").GetString() },
            } }, deadline.Token);
            var final = await ReceiveWire(socket, "gameState", deadline.Token);
            Assert.Equal("wire-pay", final.GetProperty("requestId").GetString());
            Assert.Equal("completed", final.GetProperty("tutorial").GetProperty("status").GetString());
            Assert.False(final.GetProperty("gmEnabled").GetBoolean());
            socket.Abort();
        }
        finally { Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost); }
    }

    private static async Task SendWire(ClientWebSocket socket, object value, CancellationToken token)
        => await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))),
            WebSocketMessageType.Text, true, token);

    private static async Task<JsonElement> ReceiveWire(ClientWebSocket socket, string type, CancellationToken token)
    {
        for (var i = 0; i < 64; i++)
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                Assert.Equal(WebSocketMessageType.Text, received.MessageType);
                bytes.Write(buffer, 0, received.Count);
                Assert.True(bytes.Length <= 2 * 1024 * 1024);
            } while (!received.EndOfMessage);
            var message = JsonSerializer.Deserialize<JsonElement>(bytes.ToArray());
            if (message.GetProperty("type").GetString() == type) return message;
        }
        throw new Xunit.Sdk.XunitException("Expected bounded wire message " + type);
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Source root unavailable");
    }

    [Theory]
    [InlineData("seed", "42")]
    [InlineData("actor", "1")]
    [InlineData("checkpoint", "{}")]
    [InlineData("deck", "{}")]
    public void WireRejectsExtraClientRecipeFields(string key, string value)
        => Assert.False(L12WebSocketServer.TryReadTutorialRequest(JsonDocument.Parse(
            "{\"type\":\"createTutorial\",\"lessonId\":\"deploy\",\"requestId\":\"ok\",\"" + key + "\":" + value + "}").RootElement, out _, out _));

    [Fact]
    public void WireAcceptsOnlyStrictKnownLessonAndUniqueTopLevelFields()
    {
        Assert.True(L12WebSocketServer.TryReadTutorialRequest(JsonSerializer.SerializeToElement(new { type = "createTutorial", lessonId = "deploy", requestId = "known" }), out var lesson, out var request));
        Assert.Equal("deploy", lesson); Assert.Equal("known", request);
        Assert.False(L12WebSocketServer.TryReadTutorialRequest(JsonDocument.Parse("{\"type\":\"createTutorial\",\"lessonId\":\"deploy\",\"lessonId\":\"combat\",\"requestId\":\"x\"}").RootElement, out _, out _));
    }

    private static string? Type(OutgoingMessage message) => JsonSerializer.SerializeToElement(message.Payload, Wire).GetProperty("type").GetString();
    private static bool IsGame(OutgoingMessage message) => Type(message) == "gameState";

    internal static JsonElement Message(IReadOnlyList<OutgoingMessage> messages, string type)
        => messages.Select(m => JsonSerializer.SerializeToElement(m.Payload, Wire))
            .FirstOrDefault(m => m.GetProperty("type").GetString() == type) is var found
            && found.ValueKind != JsonValueKind.Undefined ? found
            : throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(messages.Select(m => m.Payload), Wire));

    internal sealed class Fixture : IAsyncDisposable
    {
        internal readonly string Root;
        internal readonly L12Catalog Catalog;
        internal readonly MatchRecorder Recorder;
        internal readonly L12PlatformStore Platform;
        internal readonly L12RoomManager Manager;
        internal readonly Guid Student = Guid.NewGuid();
        internal readonly string AccountId;
        internal DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        private Fixture(string root, L12Catalog catalog, MatchRecorder recorder, bool enabled)
        {
            Root = root; Catalog = catalog; Recorder = recorder;
            Platform = new(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var account = Platform.Register("learner", "Password123!").Account!;
            AccountId = account.Id;
            Manager = new(catalog, recorder, Platform, utcNow: () => Now,
                maintenanceSandboxFenceActive: () => false, tutorialEnabled: enabled);
            Manager.Connect(Student, AccountId, "learner");
        }
        internal static async Task<Fixture> Create(bool enabled = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-tutorial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            return new(root, catalog, recorder, enabled);
        }
        internal async Task<JsonElement> Start(string lesson) => Message(await Manager.CreateTutorialAsync(Student, lesson, "start-" + lesson), "gameState");
        internal object RuntimeRoom()
        {
            var rooms = (System.Collections.IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Manager)!;
            var entry = rooms.Cast<object>().Single();
            return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        }
        internal SemaphoreSlim RoomGate() => (SemaphoreSlim)RuntimeRoom().GetType().GetProperty("Gate")!.GetValue(RuntimeRoom())!;
        internal void ScheduledMaintenance()
        {
            var admin = Platform.Login("Admin", "L12master").Account!;
            var current = Platform.OperationsConfig(admin);
            Platform.ApplyOperationsConfig(admin, current.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "synthetic scheduled tutorial maintenance",
                    Now.AddMinutes(-1), Now.AddHours(1), 2, 2),
            }, current.Version, "synthetic tutorial maintenance regression", new("tutorial-scheduled-maintenance"));
        }
        internal Task<IReadOnlyList<OutgoingMessage>> Act(L12Command command, string requestId)
            => Manager.HandleActionAsync(Student, JsonSerializer.SerializeToElement(command), requestId);
        internal async Task<JsonElement> PassOwnResponses(JsonElement message)
        {
            for (var i = 0; i < 8; i++)
            {
                var prompts = message.GetProperty("state").GetProperty("prompts").EnumerateArray().ToArray();
                if (prompts.Length == 0) return message;
                var prompt = Assert.Single(prompts);
                Assert.Equal("response", prompt.GetProperty("kind").GetString());
                message = Message(await Act(new("resolvePrompt", PromptId: prompt.GetProperty("promptId").GetString(), CardInstanceIds: ["pass"]), "response-" + Guid.NewGuid().ToString("N")), "gameState");
            }
            throw new Xunit.Sdk.XunitException("Response budget exceeded");
        }
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            PlatformTestDirectoryCleanup.Delete(Root);
        }
    }
}
