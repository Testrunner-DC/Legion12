using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedWatchdogIsolationTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private readonly ITestOutputHelper _output;

    public RankedWatchdogIsolationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ThirtySecondBusyRoomDoesNotDelayHealthyDurableConclusionAndLateRoomConcludesOnce()
    {
        await RunFixtureAsync(async fixture =>
        {
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
            await RunFixturePhaseAsync(async () =>
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
            }, async () =>
            {
                gate.Release();
                await DrainOwnedTickAsync(tick);
            });
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
        });
    }

    [Fact]
    public async Task DeferredCohortSuppressesWholeOrdinaryCheckpointUntilBothRoomsAreReady()
    {
        await RunFixtureAsync(async fixture =>
        {
            var before = await fixture.GenerationsAsync();
            var blocked = Rooms(fixture.Manager).First();
            var commits = 0;
            fixture.Recorder.StorageFailureInjector = phase =>
            {
                if (phase == "before-ranked-runtime-batch-commit") Interlocked.Increment(ref commits);
            };
            Task<IReadOnlyList<OutgoingMessage>>? tick = null;
            await Gate(blocked).WaitAsync();
            await RunFixturePhaseAsync(async () =>
            {
                fixture.Now += TimeSpan.FromSeconds(1);
                tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                await tick.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(0, commits);
                Assert.Equal(before, await fixture.GenerationsAsync());
            }, async () =>
            {
                Gate(blocked).Release();
                await DrainOwnedTickAsync(tick);
            });
            await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            Assert.Equal(1, commits);
            var after = await fixture.GenerationsAsync();
            Assert.All(Enumerable.Range(0, after.Length), index => Assert.True(after[index] > before[index]));
        });
    }

    [Fact]
    public async Task SkippedRoomKeepsItsBaselineAndNextTickChargesAccumulatedUtcExactlyOnce()
    {
        await RunFixtureAsync(async fixture =>
        {
            var room = Rooms(fixture.Manager).First();
            var match = fixture.Matches.Single(candidate => candidate.RoomCode == Code(room));
            var before = (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!;
            var actor = Enumerable.Range(0, 2).First(index => before.Acting[index]);
            Assert.True(before.OperationRemainingMs[actor] > 5000);
            var clock = room.GetType().GetProperty("RankedClock")!.GetValue(room)!;
            Task<IReadOnlyList<OutgoingMessage>>? tick = null;
            await Gate(room).WaitAsync();
            await RunFixturePhaseAsync(async () =>
            {
                fixture.Now += TimeSpan.FromSeconds(2);
                tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                await tick.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(before.LastSettledAt, clock.GetType().GetProperty("LastSettledAt")!.GetValue(clock));
                Assert.Equal(before.OperationRemainingMs,
                    (long[])clock.GetType().GetProperty("OperationRemainingMs")!.GetValue(clock)!);
            }, async () =>
            {
                Gate(room).Release();
                await DrainOwnedTickAsync(tick);
            });
            fixture.Now += TimeSpan.FromSeconds(3);
            await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            var caughtUp = (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!;
            Assert.Equal(before.OperationRemainingMs[actor] - 5000, caughtUp.OperationRemainingMs[actor]);
            Assert.Equal(fixture.Now, caughtUp.LastSettledAt);
            await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            Assert.Equal(caughtUp.OperationRemainingMs,
                (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(match.MatchId))!.OperationRemainingMs);
        });
    }

    [Fact]
    public async Task CompleteCohortBatchFailureLeavesBothDurableGenerationsUnchangedAndRetryAdvancesBoth()
    {
        await RunFixtureAsync(async fixture =>
        {
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
        });
    }

    [Fact]
    public async Task OverlappingSweepReturnsWithoutQueueingAndLaterSweepCanCommitAgain()
    {
        await RunFixtureAsync(async fixture =>
        {
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
            Task<IReadOnlyList<OutgoingMessage>>? overlappingTick = null;
            var first = Task.Factory.StartNew(() => fixture.Manager.TickRankedClocksAsync(fixture.Now),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            await RunFixturePhaseAsync(async () =>
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
                overlappingTick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                Assert.Empty(await overlappingTick.WaitAsync(TimeSpan.FromSeconds(1)));
                Assert.Equal(1, commits);
            }, async () =>
            {
                await RunFixturePhaseAsync(async () =>
                {
                    release.Set();
                    await first.WaitAsync(TimeSpan.FromSeconds(3));
                }, () => DrainOwnedTickAsync(overlappingTick));
            });
            await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            Assert.Equal(2, commits);
        });
    }

    [Fact]
    public async Task HealthyDurableConclusionAndPendingOutboxSurviveDeferredBatchAndRecovery()
    {
        await RunFixtureAsync(async fixture =>
        {
            var ordered = Rooms(fixture.Manager).ToArray();
            var blocked = ordered[0];
            var healthy = fixture.Matches.Single(match => match.RoomCode == Code(ordered[1]));
            fixture.Manager.Disconnect(healthy.FirstSession);
            fixture.Platform.StorageFailureInjector = phase =>
            {
                if (phase == "before-commit") throw new IOException("synthetic platform settlement failure");
            };
            Task<IReadOnlyList<OutgoingMessage>>? tick = null;
            await Gate(blocked).WaitAsync();
            await RunFixturePhaseAsync(async () =>
            {
                fixture.Now += TimeSpan.FromMinutes(5);
                tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                await tick.WaitAsync(TimeSpan.FromSeconds(1));
                var detail = (await fixture.Recorder.GetMatchAsync(healthy.MatchId))!;
                Assert.NotNull(detail.Match.EndedUtc);
                Assert.Single(detail.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
                Assert.Equal("completed", (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(healthy.MatchId))!.Status);
                Assert.Equal(1, await fixture.Recorder.CountPendingRankedSettlementsAsync());
                Assert.Null(fixture.Platform.RankedSettlement(healthy.MatchId, healthy.First.Id));
            }, async () =>
            {
                Gate(blocked).Release();
                fixture.Platform.StorageFailureInjector = null;
                await DrainOwnedTickAsync(tick);
            });
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
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledMaintenanceStillInvalidatesButImmediateOverrideStillProtectsRunningRooms(bool immediate)
    {
        await RunFixtureAsync(async fixture =>
        {
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
            Task<IReadOnlyList<OutgoingMessage>>? tick = null;
            await Gate(blocked).WaitAsync();
            await RunFixturePhaseAsync(async () =>
            {
                tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                await tick.WaitAsync(TimeSpan.FromSeconds(1));
                var detail = (await fixture.Recorder.GetMatchAsync(healthy.MatchId))!;
                if (immediate) Assert.Null(detail.Match.EndedUtc);
                else
                {
                    Assert.NotNull(detail.Match.EndedUtc);
                    Assert.Null(detail.Match.Winner);
                    Assert.Single(detail.Commands, command => command.Command.GetProperty("type").GetString() == "authorityConclusion");
                }
            }, async () =>
            {
                Gate(blocked).Release();
                await DrainOwnedTickAsync(tick);
                _output.WriteLine("fixture-lifetime: immediate={0}; owned tick drained={1}",
                    immediate, tick?.IsCompletedSuccessfully ?? true);
            });
            await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            foreach (var match in fixture.Matches)
            {
                var detail = (await fixture.Recorder.GetMatchAsync(match.MatchId))!;
                Assert.Equal(immediate, detail.Match.EndedUtc is null);
            }
            _output.WriteLine("fixture-lifetime: immediate={0}; all original business assertions completed", immediate);
        });
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("immediate")]
    [InlineData("frozen")]
    public async Task BusyRoomDoesNotLoseObservedScheduledInvalidationAfterFiniteMaintenanceEnds(string laterPolicy)
    {
        await RunFixtureAsync(async fixture =>
        {
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
            Task<IReadOnlyList<OutgoingMessage>>? tick = null;
            await Gate(blocked).WaitAsync();
            await RunFixturePhaseAsync(async () =>
            {
                tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                await tick.WaitAsync(TimeSpan.FromSeconds(1));
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
            }, async () =>
            {
                Gate(blocked).Release();
                await DrainOwnedTickAsync(tick);
            });
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
        });
    }

    [Fact]
    public async Task BusySettlementRoomCannotDelayHealthyConclusionOrExtendItsRetentionStart()
    {
        await RunFixtureAsync(async fixture =>
        {
            var blockedMatch = fixture.Matches[0];
            var healthyMatch = fixture.Matches[1];
            await fixture.Manager.HandleActionAsync(blockedMatch.FirstSession,
                JsonSerializer.SerializeToElement(new { type = "surrender" }, WireJson));
            Assert.NotNull((await fixture.Recorder.GetMatchAsync(blockedMatch.MatchId))!.Match.EndedUtc);
            var blocked = Rooms(fixture.Manager).Single(room => Code(room) == blockedMatch.RoomCode);
            var retentionStarted = blocked.GetType().GetProperty("SettlementStartedAt")!.GetValue(blocked);
            fixture.Manager.Disconnect(healthyMatch.FirstSession);
            Task<IReadOnlyList<OutgoingMessage>>? tick = null;
            await Gate(blocked).WaitAsync();
            await RunFixturePhaseAsync(async () =>
            {
                fixture.Now += TimeSpan.FromMinutes(5);
                tick = fixture.Manager.TickRankedClocksAsync(fixture.Now);
                await tick.WaitAsync(TimeSpan.FromSeconds(1));
                await AssertConcludedOnceAsync(fixture, healthyMatch);
                Assert.Equal(retentionStarted, blocked.GetType().GetProperty("SettlementStartedAt")!.GetValue(blocked));
            }, async () =>
            {
                Gate(blocked).Release();
                await DrainOwnedTickAsync(tick);
            });
            fixture.Now += TimeSpan.FromMinutes(26);
            var messages = await fixture.Manager.TickRankedClocksAsync(fixture.Now);
            Assert.Contains(messages, message => message.SessionId == blockedMatch.FirstSession
                && JsonSerializer.SerializeToElement(message.Payload, WireJson).GetProperty("type").GetString() == "roomClosed");
            Assert.DoesNotContain(Rooms(fixture.Manager), room => Code(room) == blockedMatch.RoomCode);
        });
    }

    [Fact]
    public async Task BothPreferenceChangesKeepTheCurrentLeaseAndUseReliableRecipientDeltas()
    {
        await RunFixtureAsync(async fixture =>
        {
            var response = await fixture.AddResponseRoomAsync();
            var sessions = SessionIds(response.Room);
            var game = Game(response.Room);
            var originalLease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
            Assert.Equal(TimeSpan.FromSeconds(5), originalLease.DeadlineUtc - fixture.Now);
            var frozenModes = Assert.IsType<string[]>(game.State.ResponseWindow!.FrozenPlayerResponseModes).ToArray();
            var transport = await PrimeRecipientsAsync(fixture.Manager, response.Room, sessions);
            var updatedModes = new[]
            {
                L12GameEngine.DefaultResponseMode,
                L12GameEngine.ValidOnlyResponseMode,
            };

            for (var player = 0; player < sessions.Length; player++)
            {
                var beforeRevision = game.State.Revision;
                var requestId = $"response-preference-delta-{player}";
                var messages = await fixture.Manager.SetResponsePreferenceAsync(
                    sessions[player], updatedModes[player], requestId);
                var games = messages.Where(message => message.IsGameState).ToArray();
                Assert.Equal(2, games.Length);
                Assert.All(games, message =>
                {
                    Assert.False(message.ReplaceableGameState);
                    Assert.False(message.ForceFullGameState);
                });
                foreach (var message in games)
                {
                    await AssertRecipientDeltaAsync(transport, sessions, message);
                    await AssertFullFallbacksAsync(transport.Messages[message.SessionId], message);
                }
                var result = JsonSerializer.SerializeToElement(messages.Single(message =>
                    message.SessionId == sessions[player] && !message.IsGameState).Payload, WireJson);
                Assert.Equal("responsePreferenceResult", result.GetProperty("type").GetString());
                Assert.True(result.GetProperty("matchApplied").GetBoolean());
                Assert.Equal(updatedModes[player], result.GetProperty("confirmedMode").GetString());
                Assert.Equal(requestId, result.GetProperty("requestId").GetString());
                Assert.Equal(beforeRevision + 1, game.State.Revision);
                Assert.Equal(originalLease, game.CaptureResponseAutoCloseLease());
                Assert.Equal(frozenModes, game.State.ResponseWindow!.FrozenPlayerResponseModes);
            }

            Assert.Equal(updatedModes, new[] { game.ResponseModeFor(0), game.ResponseModeFor(1) });
            var restored = Assert.IsType<L12JournalRecoveryState>(
                await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
            Assert.Equal(game.SerializeFullState(), restored.Engine.SerializeFullState());
            Assert.Equal(updatedModes, restored.Engine.State.PlayerResponseModes);

            game.State.PendingPrompts.Clear();
            game.State.EffectStack.Clear();
            game.State.ResponseWindow = null;
            var next = new L12StackItem
            {
                StackItemId = "preference-next-window", Controller = 1,
                SourceInstanceId = "source-next", SourceCardId = "S01-0103",
                SourceName = "next response source", Trigger = "reaction", Text = "下一响应时点",
            };
            game.State.ActivePlayer = 0;
            game.State.EffectStack.Add(next);
            typeof(L12GameEngine).GetMethod("BeginResponseWindow",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [next]);
            Assert.Equal(updatedModes, game.State.ResponseWindow!.FrozenPlayerResponseModes);
            Assert.Null(game.CaptureResponseAutoCloseLease());
        });
    }

    [Theory]
    [InlineData("timer")]
    [InlineData("action")]
    public async Task ResponseExpiryOriginsStayReliableAndUseRecipientDeltasWithStrictRecovery(string origin)
    {
        await RunFixtureAsync(async fixture =>
        {
            var response = await fixture.AddResponseRoomAsync();
            var sessions = SessionIds(response.Room);
            var game = Game(response.Room);
            var original = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
            Assert.Equal(TimeSpan.FromSeconds(5), original.DeadlineUtc - fixture.Now);
            var transport = await PrimeRecipientsAsync(fixture.Manager, response.Room, sessions);
            var beforeRevision = game.State.Revision;
            var beforeSequence = RoomCommandSequence(response.Room);
            var beforeDetail = Assert.IsType<L12MatchDetail>(
                await fixture.Recorder.GetMatchAsync(response.MatchId));
            Assert.Empty(beforeDetail.Commands);
            const string actionRequestId = "response-expiry-action";
            fixture.Now = original.DeadlineUtc;

            var messages = origin == "timer"
                ? await fixture.Manager.TickResponseWindowsAsync(fixture.Now)
                : await fixture.Manager.HandleActionAsync(sessions[0],
                    JsonSerializer.SerializeToElement(new { type = "ignored-after-response-expiry" }, WireJson),
                    actionRequestId);
            Assert.Equal(2, messages.Count);
            var games = messages.Where(message => message.IsGameState).ToArray();
            Assert.Equal(messages.Count, games.Length);
            Assert.All(games, message =>
            {
                Assert.False(message.ReplaceableGameState);
                Assert.False(message.ForceFullGameState);
                var payload = JsonSerializer.SerializeToElement(message.Payload, WireJson);
                var acknowledgement = payload.TryGetProperty("requestId", out var requestId)
                    && requestId.ValueKind != JsonValueKind.Null ? requestId.GetString() : null;
                Assert.Equal(origin == "action" && message.SessionId == sessions[0]
                    ? actionRequestId : null, acknowledgement);
            });
            Assert.Equal(beforeRevision + 1, game.State.Revision);
            Assert.Equal(beforeSequence + 1, RoomCommandSequence(response.Room));
            var detail = Assert.IsType<L12MatchDetail>(
                await fixture.Recorder.GetMatchAsync(response.MatchId));
            var recorded = Assert.Single(detail.Commands);
            Assert.Equal("responseAutoClose", recorded.Command.GetProperty("type").GetString());
            Assert.Equal(1, await CountCloseAsync(fixture.Recorder, response.MatchId, original.PromptId));
            var processed = (IDictionary)response.Room.GetType().GetProperty("ProcessedActionRequests")!
                .GetValue(response.Room)!;
            Assert.DoesNotContain($"0:{actionRequestId}", processed.Keys.Cast<string>());
            var nextLease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
            Assert.NotEqual(original.PromptId, nextLease.PromptId);
            Assert.Equal(1, nextLease.PriorityPlayer);
            Assert.Equal(fixture.Now.AddSeconds(5), nextLease.DeadlineUtc);
            var restored = Assert.IsType<L12JournalRecoveryState>(
                await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
            Assert.Equal(game.SerializeFullState(), restored.Engine.SerializeFullState());
            Assert.DoesNotContain(restored.ProcessedRequests,
                request => request.RequestId == actionRequestId);

            foreach (var message in games)
            {
                await AssertRecipientDeltaAsync(transport, sessions, message);
                await AssertFullFallbacksAsync(transport.Messages[message.SessionId], message);
            }
        });
    }

    [Theory]
    [InlineData("match-commit")]
    [InlineData("account-sync")]
    public async Task PreferenceFailurePublishesOnlyCommittedStateAndRetainsRetryableOutbox(string phase)
    {
        await RunFixtureAsync(async fixture =>
        {
            var response = await fixture.AddResponseRoomAsync();
            var sessions = SessionIds(response.Room);
            var game = Game(response.Room);
            var beforeState = game.SerializeFullState();
            var beforeRevision = game.State.Revision;
            var beforeSequence = RoomCommandSequence(response.Room);
            var lease = game.CaptureResponseAutoCloseLease();
            var transport = await PrimeRecipientsAsync(fixture.Manager, response.Room, sessions);
            if (phase == "match-commit") fixture.Recorder.StorageFailureInjector = stage =>
            {
                if (stage == "before-match-command-commit") throw new IOException("synthetic preference commit failure");
            };
            else fixture.Platform.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("synthetic preference account sync failure");
            };
            try
            {
                var messages = await fixture.Manager.SetResponsePreferenceAsync(sessions[0],
                    L12GameEngine.DefaultResponseMode, "failed-preference-delta");
                var result = JsonSerializer.SerializeToElement(messages.Single(message => !message.IsGameState).Payload, WireJson);
                Assert.Equal("responsePreferenceResult", result.GetProperty("type").GetString());
                Assert.Equal("failed-preference-delta", result.GetProperty("requestId").GetString());
                Assert.False(result.GetProperty("accountSynced").GetBoolean());
                if (phase == "match-commit")
                {
                    Assert.False(result.GetProperty("matchApplied").GetBoolean());
                    Assert.Single(messages);
                    Assert.Equal(beforeState, game.SerializeFullState());
                    Assert.Equal(beforeSequence, RoomCommandSequence(response.Room));
                    Assert.Empty((await fixture.Recorder.GetMatchAsync(response.MatchId))!.Commands);
                    Assert.Empty(await fixture.Recorder.ListPendingResponsePreferencesAsync());
                }
                else
                {
                    Assert.True(result.GetProperty("matchApplied").GetBoolean());
                    Assert.True(result.GetProperty("syncPending").GetBoolean());
                    Assert.Equal(beforeRevision + 1, game.State.Revision);
                    Assert.Equal(beforeSequence + 1, RoomCommandSequence(response.Room));
                    var pending = Assert.Single(await fixture.Recorder.ListPendingResponsePreferencesAsync());
                    Assert.Equal(L12GameEngine.DefaultResponseMode, pending.Payload!.TargetMode);
                    var games = messages.Where(message => message.IsGameState).ToArray();
                    Assert.Equal(2, games.Length);
                    foreach (var message in games)
                    {
                        Assert.False(message.ReplaceableGameState);
                        Assert.False(message.ForceFullGameState);
                        await AssertRecipientDeltaAsync(transport, sessions, message);
                    }
                }
                Assert.Equal(lease, game.CaptureResponseAutoCloseLease());
                var restored = Assert.IsType<L12JournalRecoveryState>(await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
                Assert.Equal(game.SerializeFullState(), restored.Engine.SerializeFullState());
            }
            finally
            {
                fixture.Recorder.StorageFailureInjector = null;
                fixture.Platform.StorageFailureInjector = null;
            }
            if (phase == "match-commit")
            {
                var retry = await fixture.Manager.SetResponsePreferenceAsync(sessions[0],
                    L12GameEngine.DefaultResponseMode, "failed-preference-delta");
                Assert.Equal(2, retry.Count(message => message.IsGameState));
                Assert.Single((await fixture.Recorder.GetMatchAsync(response.MatchId))!.Commands);
            }
            else
            {
                var retriedSync = await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
                Assert.DoesNotContain(retriedSync, message => message.IsGameState);
                var state = JsonSerializer.SerializeToElement(Assert.Single(retriedSync).Payload, WireJson);
                Assert.Equal("responsePreferenceState", state.GetProperty("type").GetString());
                Assert.False(state.GetProperty("syncPending").GetBoolean());
            }
            Assert.Empty(await fixture.Recorder.ListPendingResponsePreferencesAsync());
        });
    }

    [Fact]
    public async Task ThirtySecondBusyResponseRoomKeepsOriginalDeadlineWhileHealthyExpiryIsDurable()
    {
        await RunFixtureAsync(async fixture =>
        {
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
            await RunFixturePhaseAsync(async () =>
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
            }, async () =>
            {
                Gate(blocked.Room).Release();
                await DrainOwnedTickAsync(tick);
            });
            fixture.Now += TimeSpan.FromSeconds(30);
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => fixture.Manager.TickResponseWindowsAsync(fixture.Now)));
            Assert.Equal(1, await CountCloseAsync(fixture.Recorder, blocked.MatchId, originalBlocked.PromptId));
            Assert.Equal(1, await CountCloseAsync(fixture.Recorder, healthy.MatchId, originalHealthy.PromptId));
            var restored = Assert.IsType<L12JournalRecoveryState>(await fixture.Recorder.LoadJournalEngineAsync(blocked.MatchId));
            Assert.Equal(Game(blocked.Room).CaptureResponseAutoCloseLease(), restored.Engine.CaptureResponseAutoCloseLease());
        });
    }

    [Fact]
    public async Task ResponseCommitFailureNotifiesNobodyRestoresOriginalLeaseAndRetryExpiresExactlyOnce()
    {
        await RunFixtureAsync(async fixture =>
        {
            var response = await fixture.AddResponseRoomAsync();
            var game = Game(response.Room);
            var original = game.CaptureResponseAutoCloseLease()!;
            var beforeRevision = game.State.Revision;
            var beforeState = game.SerializeFullState();
            fixture.Now = original.DeadlineUtc;
            var failures = 0;
            fixture.Recorder.StorageFailureInjector = phase =>
            {
                if (phase != "before-match-command-commit") return;
                Interlocked.Increment(ref failures);
                throw new IOException("synthetic response commit failure");
            };
            var failed = await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
            Assert.Empty(failed);
            Assert.Equal(1, failures);
            var reloadedGame = Game(response.Room);
            Assert.Equal(beforeRevision, reloadedGame.State.Revision);
            Assert.Equal(beforeState, reloadedGame.SerializeFullState());
            Assert.Equal(original, reloadedGame.CaptureResponseAutoCloseLease());
            Assert.Equal(0, await CountCloseAsync(fixture.Recorder, response.MatchId, original.PromptId));
            var recovered = Assert.IsType<L12JournalRecoveryState>(await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
            Assert.Equal(original, recovered.Engine.CaptureResponseAutoCloseLease());
            fixture.Recorder.StorageFailureInjector = null;
            var retry = await fixture.Manager.TickResponseWindowsAsync(fixture.Now);
            var games = retry.Where(message => message.IsGameState).ToArray();
            Assert.Equal(2, games.Length);
            Assert.All(games, message =>
            {
                Assert.False(message.ReplaceableGameState);
                Assert.False(message.ForceFullGameState);
            });
            Assert.DoesNotContain(await fixture.Manager.TickResponseWindowsAsync(fixture.Now),
                message => message.IsGameState);
            Assert.Equal(1, await CountCloseAsync(fixture.Recorder, response.MatchId, original.PromptId));
            var afterRetry = Assert.IsType<L12JournalRecoveryState>(
                await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
            Assert.Equal(Game(response.Room).SerializeFullState(), afterRetry.Engine.SerializeFullState());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryExpiryReliablyUpdatesPeerDeltaIncludingPersistenceFailureReload(bool failCommit)
    {
        await RunFixtureAsync(async fixture =>
        {
            var response = await fixture.AddResponseRoomAsync();
            var sessions = ((IEnumerable<Guid>)response.Room.GetType().GetProperty("Sessions")!
                .GetValue(response.Room)!).ToArray();
            var original = Game(response.Room).CaptureResponseAutoCloseLease()!;
            fixture.Now = original.DeadlineUtc.AddMilliseconds(-1);
            var initial = (await fixture.Manager.RecoveryStateWithAckAsync(sessions[0]))
                .Single(message => message.IsGameState && message.SessionId == sessions[1]);
            var codec = RecoverySnapshotFanoutTests.Codec();
            var first = await RecoverySnapshotFanoutTests.SendAsync(codec, initial);
            var beforeRevision = Game(response.Room).State.Revision;
            if (failCommit) fixture.Recorder.StorageFailureInjector = phase =>
            {
                if (phase == "before-match-command-commit") throw new IOException("synthetic recovery commit failure");
            };
            fixture.Now = original.DeadlineUtc;
            var recovered = await fixture.Manager.RecoveryStateWithAckAsync(sessions[0]);
            var games = recovered.Where(message => message.IsGameState).ToArray();
            Assert.Equal(2, games.Length);
            Assert.All(games, message => Assert.False(message.ReplaceableGameState));
            Assert.True(games.Single(message => message.SessionId == sessions[0]).ForceFullGameState);
            var peer = games.Single(message => message.SessionId == sessions[1]);
            Assert.False(peer.ForceFullGameState);
            var wire = await RecoverySnapshotFanoutTests.SendAsync(codec, peer);
            Assert.True(wire.IsDelta);
            var rebuilt = L12SnapshotWireCodec.ApplyDelta(System.Text.Json.Nodes.JsonNode.Parse(first.Payload)!.AsObject(), wire.Payload);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(RecoverySnapshotFanoutTests.Queue(peer).FullPayload), rebuilt));
            Assert.Equal(failCommit ? 0 : 1, await CountCloseAsync(fixture.Recorder, response.MatchId, original.PromptId));
            if (failCommit)
            {
                Assert.Equal(beforeRevision, Game(response.Room).State.Revision);
                Assert.Equal(original, Game(response.Room).CaptureResponseAutoCloseLease());
            }
            else Assert.True(Game(response.Room).State.Revision > beforeRevision);
            fixture.Recorder.StorageFailureInjector = null;
            var restored = Assert.IsType<L12JournalRecoveryState>(await fixture.Recorder.LoadJournalEngineAsync(response.MatchId));
            Assert.Equal(Game(response.Room).SerializeFullState(), restored.Engine.SerializeFullState());
        });
    }

    [Fact]
    public async Task RecoveryRefreshesPendingPreferenceForPeerAtTheSameRevisionWithoutFullFanout()
    {
        await RunFixtureAsync(async fixture =>
        {
            var response = await fixture.AddResponseRoomAsync();
            var sessions = ((IEnumerable<Guid>)response.Room.GetType().GetProperty("Sessions")!
                .GetValue(response.Room)!).ToArray();
            var sessionMap = (IDictionary)typeof(L12RoomManager).GetField("_sessions",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Manager)!;
            var peerAccount = (string)sessionMap[sessions[1]]!.GetType().GetProperty("AccountId")!
                .GetValue(sessionMap[sessions[1]])!;
            var game = Game(response.Room);
            var applied = game.ApplyResponsePreference(1, L12GameEngine.ValidOnlyResponseMode);
            var envelope = new L12ResponsePreferenceOutboxEnvelope("pending-fanout", response.MatchId, 1,
                peerAccount, 1, L12GameEngine.ValidOnlyResponseMode, fixture.Now);
            await fixture.Recorder.AppendResponsePreferenceAsync(game, 1, 1,
                JsonSerializer.Serialize(new { type = "setResponsePreference", responseMode = envelope.TargetMode }),
                applied, envelope, null, "pending-fanout");
            response.Room.GetType().GetProperty("CommandSequence")!.SetValue(response.Room, 1L);
            var baseline = (IReadOnlyList<OutgoingMessage>)typeof(L12RoomManager).GetMethod("BroadcastGame",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Manager, [response.Room, false, null, null, null, false])!;
            var peerBefore = baseline.Single(message => message.SessionId == sessions[1]);
            Assert.False(JsonSerializer.SerializeToElement(peerBefore.Payload, WireJson)
                .GetProperty("responsePreference").GetProperty("syncPending").GetBoolean());
            var codec = RecoverySnapshotFanoutTests.Codec();
            var first = await RecoverySnapshotFanoutTests.SendAsync(codec, peerBefore);
            var revision = game.State.Revision;
            var peer = (await fixture.Manager.RecoveryStateAsync(sessions[0]))
                .Single(message => message.IsGameState && message.SessionId == sessions[1]);
            Assert.Equal(revision, game.State.Revision);
            Assert.False(peer.ForceFullGameState);
            Assert.False(peer.ReplaceableGameState);
            var wire = await RecoverySnapshotFanoutTests.SendAsync(codec, peer);
            Assert.True(wire.IsDelta);
            var rebuilt = L12SnapshotWireCodec.ApplyDelta(System.Text.Json.Nodes.JsonNode.Parse(first.Payload)!.AsObject(), wire.Payload);
            Assert.True(rebuilt["responsePreference"]!["syncPending"]!.GetValue<bool>());
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(RecoverySnapshotFanoutTests.Queue(peer).FullPayload), rebuilt));
        });
    }

    [Fact]
    public async Task FixturePhaseKeepsTheOriginalBusinessFailureWhenCleanupSucceeds()
    {
        var business = new TimeoutException("synthetic original business deadline");
        var cleaned = false;
        var error = await Assert.ThrowsAsync<TimeoutException>(() => RunFixturePhaseAsync(
            () => Task.FromException(business), () => { cleaned = true; return Task.CompletedTask; }));
        Assert.Same(business, error);
        Assert.True(cleaned);
    }

    [Fact]
    public async Task FixturePhasePreservesBothBusinessAndCleanupFailures()
    {
        var business = new InvalidOperationException("synthetic original business assertion");
        var cleanup = new IOException("synthetic fixture file cleanup");
        var error = await Assert.ThrowsAsync<AggregateException>(() => RunFixturePhaseAsync(
            () => Task.FromException(business), () => Task.FromException(cleanup)));
        Assert.Equal(new Exception[] { business, cleanup }, error.InnerExceptions);
    }

    [Fact]
    public async Task OneSecondTickTimeoutStillFailsAfterGateReleaseAndSuccessfulDrainEvenIfDisposalFails()
    {
        using var gate = new SemaphoreSlim(0, 1);
        var tick = Task.Run(async () => await gate.WaitAsync());
        var cleanup = new IOException("synthetic fixture file cleanup after tick drain");
        Exception? deadline = null;
        var error = await Assert.ThrowsAsync<AggregateException>(() => RunFixturePhaseAsync(
            () => RunFixturePhaseAsync(async () =>
            {
                try { await tick.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (TimeoutException observed) { deadline = observed; throw; }
            }, async () =>
            {
                gate.Release();
                await tick.WaitAsync(TimeSpan.FromSeconds(5));
            }), () => Task.FromException(cleanup)));
        Assert.IsType<TimeoutException>(deadline);
        Assert.Equal(new Exception[] { deadline!, cleanup }, error.InnerExceptions);
        Assert.True(tick.IsCompletedSuccessfully);
    }

    private async Task RunFixtureAsync(Func<Fixture, Task> body)
    {
        var fixture = await Fixture.CreateAsync();
        await RunFixturePhaseAsync(async () =>
        {
            await body(fixture);
            _output.WriteLine("fixture-lifetime: all original business assertions completed");
        }, async () =>
        {
            await fixture.DisposeAsync();
            _output.WriteLine("fixture-lifetime: fixture cleanup completed");
        });
    }

    private async Task DrainOwnedTickAsync(Task? tick)
    {
        if (tick is not null) await tick.WaitAsync(TimeSpan.FromSeconds(5));
        _output.WriteLine("fixture-lifetime: owned tick drained={0}", tick?.IsCompletedSuccessfully ?? true);
    }

    private static async Task RunFixturePhaseAsync(Func<Task> body, Func<Task> cleanup)
    {
        Exception? businessFailure = null;
        try { await body(); }
        catch (Exception error) { businessFailure = error; }
        try { await cleanup(); }
        catch (Exception cleanupFailure)
        {
            if (businessFailure is not null)
                throw new AggregateException("Watchdog fixture phase and cleanup both failed",
                    businessFailure, cleanupFailure);
            throw;
        }
        if (businessFailure is not null) ExceptionDispatchInfo.Capture(businessFailure).Throw();
    }

    private static Guid[] SessionIds(object room)
        => ((IEnumerable<Guid>)room.GetType().GetProperty("Sessions")!.GetValue(room)!).ToArray();
    private static long RoomCommandSequence(object room)
        => (long)room.GetType().GetProperty("CommandSequence")!.GetValue(room)!;

    private static IReadOnlyList<OutgoingMessage> BroadcastForTransport(L12RoomManager manager, object room)
        => (IReadOnlyList<OutgoingMessage>)typeof(L12RoomManager).GetMethod("BroadcastGame",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(manager, [room, false, null, null, null, false])!;

    private static async Task<RecipientBaselines> PrimeRecipientsAsync(L12RoomManager manager,
        object room, IReadOnlyList<Guid> sessions)
    {
        var messages = BroadcastForTransport(manager, room).Where(message => message.IsGameState)
            .ToDictionary(message => message.SessionId);
        Assert.Equal(sessions.Count, messages.Count);
        var codecs = sessions.ToDictionary(session => session, _ => RecoverySnapshotFanoutTests.Codec());
        var payloads = new Dictionary<Guid, JsonObject>();
        foreach (var session in sessions)
        {
            var wire = await RecoverySnapshotFanoutTests.SendAsync(codecs[session], messages[session]);
            Assert.False(wire.IsDelta);
            payloads[session] = JsonNode.Parse(wire.Payload)!.AsObject();
        }
        return new(codecs, messages, payloads);
    }

    private static async Task AssertRecipientDeltaAsync(RecipientBaselines transport,
        IReadOnlyList<Guid> sessions, OutgoingMessage message)
    {
        var wire = await RecoverySnapshotFanoutTests.SendAsync(transport.Codecs[message.SessionId], message);
        Assert.True(wire.IsDelta);
        var rebuilt = L12SnapshotWireCodec.ApplyDelta(transport.Payloads[message.SessionId], wire.Payload);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(RecoverySnapshotFanoutTests.Queue(message).FullPayload), rebuilt));
        AssertRecipientPrivacy(rebuilt, Array.IndexOf(sessions.ToArray(), message.SessionId));
        transport.Payloads[message.SessionId] = rebuilt;
    }

    private static async Task AssertFullFallbacksAsync(OutgoingMessage baseline, OutgoingMessage message)
    {
        var expected = RecoverySnapshotFanoutTests.Queue(message).FullPayload;

        var legacy = new L12SnapshotWireCodec();
        Assert.False((await RecoverySnapshotFanoutTests.SendAsync(legacy, baseline)).IsDelta);
        var legacyWire = await RecoverySnapshotFanoutTests.SendAsync(legacy, message);
        Assert.False(legacyWire.IsDelta);
        Assert.Equal(expected, legacyWire.Payload);

        var cold = RecoverySnapshotFanoutTests.Codec();
        var coldWire = await RecoverySnapshotFanoutTests.SendAsync(cold, message);
        Assert.False(coldWire.IsDelta);
        Assert.Equal(expected, coldWire.Payload);

        var noCommit = RecoverySnapshotFanoutTests.Codec();
        Assert.False(noCommit.Prepare(RecoverySnapshotFanoutTests.Queue(baseline)).IsDelta);
        var noCommitWire = await RecoverySnapshotFanoutTests.SendAsync(noCommit, message);
        Assert.False(noCommitWire.IsDelta);
        Assert.Equal(expected, noCommitWire.Payload);
    }

    private static void AssertRecipientPrivacy(JsonObject payload, int recipient)
    {
        Assert.InRange(recipient, 0, 1);
        var state = payload["state"]!.AsObject();
        Assert.Equal(recipient, state["you"]!.GetValue<int>());
        var players = state["players"]!.AsArray();
        var self = players[recipient]!.AsObject();
        var opponent = players[1 - recipient]!.AsObject();
        Assert.True(self.ContainsKey("hand"));
        Assert.False(opponent.ContainsKey("hand"));
        Assert.True(opponent.ContainsKey("handCount"));
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
    private sealed record RecipientBaselines(Dictionary<Guid, L12SnapshotWireCodec> Codecs,
        Dictionary<Guid, OutgoingMessage> Messages, Dictionary<Guid, JsonObject> Payloads);

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
            PlatformTestDirectoryCleanup.Delete(target);
        }
    }
}
