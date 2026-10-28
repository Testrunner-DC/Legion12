using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RoomGameplayActivityTests
{
    [Fact]
    public async Task EmptyGmDestroyAllDoesNotRefreshActivity()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("sandbox");
        Assert.All(f.Game.State.Players[0].Field, row => Assert.All(row, Assert.Null));
        var baseline = f.Observe().LastGameplayActivityUtc;
        f.Now += TimeSpan.FromMinutes(1);
        var events = f.Game.State.EventSequence;
        Fixture.Success(await f.Gm(new("destroyAll", TargetPlayer: 0), "empty-destroy"));
        Assert.True(f.Game.State.EventSequence > events); // A GM log alone is not gameplay activity.
        Assert.Equal(baseline, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task NonEmptyGmDestroyAllStillRefreshesForTheCommittedRemoval()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("sandbox");
        Fixture.Success(await f.Gm(new("placeCard", TargetPlayer: 0, CardId: "ST01-05",
            Row: 0, Slot: 0, TriggerEffects: false), "place-control"));
        Assert.NotNull(f.Game.State.Players[0].Field[0][0]);
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Gm(new("destroyAll", TargetPlayer: 0), "nonempty-destroy"));
        Assert.Null(f.Game.State.Players[0].Field[0][0]);
        Assert.Equal(f.Now, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task ResolvingCardGmStateChangeRefreshesOnlyWhenChanged()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("sandbox");
        // Arrange one synthetic public journal origin with a card in Resolving.
        // Both tested mutations still use the actual authorized GM + Recorder path.
        var deck = f.Catalog.DeckAt(0);
        var game = new L12GameEngine(f.Catalog, Guid.NewGuid().ToString("N"), f.Code, 281028,
            ["activityone", "activitytwo"], [deck, deck], skipPreparation: true, disasterMode: "none",
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion, utcNow: () => f.Now);
        var card = game.State.Players[0].Hand.First();
        game.State.Players[0].Hand.Remove(card);
        card.Tapped = false;
        game.State.Players[0].Resolving.Add(card);
        await f.Recorder.StartAsync(game, "sandbox", f.Accounts[0], null, [deck, deck]);
        f.Room.GetType().GetProperty("Game")!.SetValue(f.Room, game);
        f.Room.GetType().GetProperty("CommandSequence")!.SetValue(f.Room, 0L);
        f.Now += TimeSpan.FromMinutes(1);
        var command = new L12GmCommand("setCardState", TargetPlayer: 0,
            CardInstanceId: card.InstanceId, Destination: "rested");
        Fixture.Success(await f.Gm(command, "rest-resolving"));
        Assert.True(card.Tapped);
        Assert.Equal(f.Now, f.Observe().LastGameplayActivityUtc);
        var changed = f.Now;
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Gm(command, "same-rest-resolving"));
        Assert.Equal(changed, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task ConcurrentActivityWritersRetainTheMaximumUtcBaseline()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var localTime = new AsyncLocal<DateTimeOffset?>();
        f.ClockOverride = () => localTime.Value ?? f.Now;
        var record = typeof(L12RoomManager).GetMethod("RecordRoomGameplayActivity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var room = f.Room;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
        {
            await start.Task;
            // Deliberately unordered, with earlier timestamps racing later ones.
            localTime.Value = f.Start.AddTicks(1 + index * 17 % 64);
            record.Invoke(f.Manager, [room]);
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(writers);
        Assert.Equal(f.Start.AddTicks(64), f.Observe().LastGameplayActivityUtc);
        localTime.Value = f.Start.AddTicks(1);
        record.Invoke(f.Manager, [room]);
        Assert.Equal(f.Start.AddTicks(64), f.Observe().LastGameplayActivityUtc);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("all")]
    public async Task RealDisasterPreparationAndMulliganRefreshOnlyForCommittedChanges(string disasterMode)
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync(disasterMode);
        var disasterSteps = 0;
        for (var step = 0; f.Game.State.Phase is L12Phase.Initiative or L12Phase.DisasterPreparation; step++)
        {
            Assert.InRange(step, 0, 15);
            var prompt = f.Game.State.PendingPrompts.First();
            if (f.Game.State.Phase == L12Phase.DisasterPreparation) disasterSteps++;
            var choices = prompt.Kind == "initiative" ? new List<string> { "first" }
                : prompt.MinChoose == 0 ? [] : prompt.ValidChoices.Take(prompt.MinChoose).ToList();
            var command = JsonSerializer.SerializeToElement(new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, CardInstanceIds: choices));
            var actor = f.Players[prompt.PlayerIndex];
            var request = "actual-setup-" + step;
            f.Now += TimeSpan.FromSeconds(1);
            Fixture.Success(await f.Manager.HandleActionAsync(actor, command, request));
            var committed = f.Now;
            Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
            f.Now += TimeSpan.FromSeconds(1);
            Fixture.Success(await f.Manager.HandleActionAsync(actor, command, request));
            Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        }
        if (disasterMode == "none") Assert.Equal(0, disasterSteps);
        else Assert.InRange(disasterSteps, 1, 15);
        Assert.Equal(L12Phase.Mulligan, f.Game.State.Phase);
        foreach (var actor in f.Players)
        {
            f.Now += TimeSpan.FromSeconds(1);
            var command = JsonSerializer.SerializeToElement(new L12Command("mulligan", CardInstanceIds: []));
            Fixture.Success(await f.Manager.HandleActionAsync(actor, command, "actual-mulligan"));
            var committed = f.Now;
            Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
            f.Now += TimeSpan.FromSeconds(1);
            Fixture.Success(await f.Manager.HandleActionAsync(actor, command, "actual-mulligan"));
            Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
            var rejected = await f.Manager.HandleActionAsync(actor, command, "repeat-mulligan-new-request");
            Assert.Contains(rejected, Fixture.IsError);
            Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        }
    }

    [Theory]
    [InlineData("friendly", 2399, false, false)]
    [InlineData("friendly", 2400, false, false)]
    [InlineData("friendly", 2401, true, false)]
    [InlineData("friendly", 3599, true, false)]
    [InlineData("friendly", 3600, true, true)]
    [InlineData("casual", 2400, false, false)]
    [InlineData("casual", 2401, true, false)]
    [InlineData("casual", 3600, true, false)]
    [InlineData("sandbox", 2401, false, false)]
    [InlineData("sandbox", 3599, false, false)]
    [InlineData("sandbox", 3600, false, true)]
    public async Task ActualCreationSeparatesOrdinaryAndDeploymentThresholds(string mode, int seconds,
        bool ordinary, bool deployment)
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync(mode);
        var created = f.Now;
        f.Now += TimeSpan.FromSeconds(seconds);
        var observed = f.Observe();
        Assert.Equal(created, observed.LastGameplayActivityUtc);
        Assert.Equal(ordinary, observed.AutomaticCloseCandidate);
        Assert.Equal(deployment, observed.DeploymentIdleCandidate);
        Assert.False((bool)f.RoomProperty("Closed")!);
        if (mode != "friendly")
            Assert.Null((await f.Recorder.GetMatchAsync(f.Game.State.MatchId))!.Match.EndedUtc);
    }

    [Fact]
    public async Task FortyMinutesPlusOneTickQualifiesWithoutClosingOrChangingCounters()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var before = f.Manager.RuntimeStats();
        f.Now += TimeSpan.FromMinutes(40) + TimeSpan.FromTicks(1);
        Assert.True(f.Observe().AutomaticCloseCandidate);
        Assert.Equal(before, f.Manager.RuntimeStats());
        Assert.Null(f.RoomProperty("Game"));
    }

    [Fact]
    public async Task PregameChangesRefreshButSameSettingsDeckAndReadinessDoNot()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var first = f.Observe().LastGameplayActivityUtc;
        var options = (L12RoomOptions)f.RoomProperty("Options")!;
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(f.Manager.UpdateRoomOptions(f.First, options));
        Fixture.Success(f.Manager.SelectDeck(f.First, f.SelectedDeckIndex(f.First)));
        Fixture.Success(await f.Manager.SetReadyAsync(f.First, false));
        Assert.Equal(first, f.Observe().LastGameplayActivityUtc);
        Fixture.Success(f.Manager.UpdateRoomOptions(f.First, new L12RoomOptions
        {
            DisasterMode = "none", Spectating = "disabled", HandVisibility = options.HandVisibility,
        }));
        Assert.Equal(f.Now, f.Observe().LastGameplayActivityUtc);
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Manager.SetReadyAsync(f.First, true));
        var ready = f.Now;
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Manager.SetReadyAsync(f.First, true));
        Assert.Equal(ready, f.Observe().LastGameplayActivityUtc);
        Fixture.Success(f.Manager.JoinRoom(f.Second, f.Code));
        Assert.Equal(f.Now, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task SameCustomDeckContentAndNameOnlyChangeDoNotRefresh()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        var deck = f.Catalog.DeckAt(f.SelectedDeckIndex(f.First));
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(f.Manager.SelectCustomDeck(f.First, new L12CustomDeckSubmission
        {
            Name = "same playable content", MasterId = deck.MasterId,
            CardIds = [.. deck.CardIds], MoraleIds = [.. deck.MoraleIds], SpecialIds = [.. deck.SpecialIds],
        }));
        Assert.Equal(f.Start, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task DurablePlayerCommandRefreshesButDuplicateRejectionRecoveryAndWatchdogsDoNot()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var prompt = Assert.Single(f.Game.State.PendingPrompts);
        var actor = f.Players[prompt.PlayerIndex];
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["first"]);
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Manager.HandleActionAsync(actor, JsonSerializer.SerializeToElement(command), "first-choice"));
        var committed = f.Now;
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Manager.HandleActionAsync(actor, JsonSerializer.SerializeToElement(command), "first-choice"));
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        var rejected = await f.Manager.HandleActionAsync(actor,
            JsonSerializer.SerializeToElement(new { type = "unknown-activity-command" }), "reject");
        Assert.Contains(rejected, Fixture.IsError);
        await f.Manager.TickRankedClocksAsync(f.Now);
        await f.Manager.TickResponseWindowsAsync(f.Now);
        await f.Manager.RecoveryStateAsync(actor);
        var spectator = Guid.NewGuid();
        f.Manager.Connect(spectator, "activity-spectator", "activity-spectator");
        Fixture.Success(f.Manager.SpectateRoom(spectator, f.Code));
        await f.Manager.RecoveryStateAsync(spectator);
        f.Manager.Disconnect(actor);
        var replacement = Guid.NewGuid();
        await f.Manager.ConnectAsync(replacement, f.Accounts[Array.IndexOf(f.Players, actor)], "activity-reconnected");
        await f.Manager.RecoveryStateAsync(replacement);
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task FailedAppendRestoresGameWithoutRefreshingBaselineOrAddingRecordedCommand()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var game = f.Game;
        var prompt = Assert.Single(game.State.PendingPrompts);
        var before = f.Observe().LastGameplayActivityUtc;
        var count = (await f.Recorder.GetMatchAsync(game.State.MatchId))!.Commands.Count;
        f.Now += TimeSpan.FromMinutes(1);
        f.Recorder.StorageFailureInjector = phase =>
        {
            if (phase == "before-match-command-commit") throw new InvalidOperationException("synthetic rollback");
        };
        try
        {
            var messages = await f.Manager.HandleActionAsync(f.Players[prompt.PlayerIndex],
                JsonSerializer.SerializeToElement(new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                    CardInstanceIds: ["first"])), "fail-append");
            Assert.Contains(messages, Fixture.IsError);
        }
        finally { f.Recorder.StorageFailureInjector = null; }
        Assert.NotSame(game, f.Game);
        Assert.Equal(before, f.Observe().LastGameplayActivityUtc);
        Assert.Equal(count, (await f.Recorder.GetMatchAsync(game.State.MatchId))!.Commands.Count);
        Assert.False((bool)f.RoomProperty("Closed")!);
    }

    [Fact]
    public async Task SandboxGmSetterNeedsRealValueChangeAndSuccessfulCommit()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("sandbox");
        Fixture.Success(await f.Gm(new("readyAll", TargetPlayer: 0), "initial-ready"));
        Fixture.Success(await f.Gm(new("readyMorale", TargetPlayer: 0), "initial-morale"));
        var baseline = f.Observe().LastGameplayActivityUtc;
        var hp = f.Game.State.Players[0].Hp;
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Gm(new("setLife", TargetPlayer: 0, Value: hp), "same-hp"));
        Fixture.Success(await f.Gm(new("readyAll", TargetPlayer: 0), "same-ready"));
        Fixture.Success(await f.Gm(new("readyMorale", TargetPlayer: 0), "same-morale"));
        Assert.Equal(baseline, f.Observe().LastGameplayActivityUtc);
        Fixture.Success(await f.Gm(new("setLife", TargetPlayer: 0, Value: hp - 1), "new-hp"));
        var committed = f.Now;
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        f.Now += TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Gm(new("setLife", TargetPlayer: 0, Value: hp - 1), "new-hp"));
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        f.Recorder.StorageFailureInjector = phase =>
        {
            if (phase == "before-match-command-commit") throw new InvalidOperationException("synthetic GM rollback");
        };
        try { Assert.Contains(await f.Gm(new("setLife", TargetPlayer: 0, Value: hp - 2), "fail-gm"), Fixture.IsError); }
        finally { f.Recorder.StorageFailureInjector = null; }
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        Assert.Equal(hp - 1, f.Game.State.Players[0].Hp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SandboxOrdinaryAndDualSeatCommandsRefreshOnlyAfterCommit(bool dualSeat)
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("sandbox");
        Fixture.Success(await f.Gm(new("setPhase", TargetPlayer: 0, Phase: "Main"), "main"));
        f.Now += TimeSpan.FromMinutes(1);
        var command = JsonSerializer.SerializeToElement(new L12Command("endTurn"));
        var messages = dualSeat
            ? await f.Manager.HandleSandboxActionAsync(f.First, 0, command, "end")
            : await f.Manager.HandleActionAsync(f.First, command, "end");
        Fixture.Success(messages);
        var committed = f.Now;
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
        f.Now += TimeSpan.FromMinutes(1);
        if (dualSeat) Fixture.Success(await f.Manager.HandleSandboxActionAsync(f.First, 0, command, "end"));
        else Fixture.Success(await f.Manager.HandleActionAsync(f.First, command, "end"));
        Assert.Equal(committed, f.Observe().LastGameplayActivityUtc);
    }

    [Fact]
    public async Task BusyRoomReturnsUnknownWithoutWaitingAndObservationNeverWritesDatabase()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StartFriendlyGameAsync();
        var revision = f.Game.State.Revision;
        var commands = (await f.Recorder.GetMatchAsync(f.Game.State.MatchId))!.Commands.Count;
        var gate = (SemaphoreSlim)f.RoomProperty("Gate")!;
        await gate.WaitAsync();
        Task<L12RoomInactivityObservation?>? read = null;
        try
        {
            read = Task.Run(() => f.Manager.ObserveRoomInactivity(f.Code));
            Assert.Null(await read.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            gate.Release();
            if (read is not null) await read;
        }
        f.Now += TimeSpan.FromHours(2);
        for (var index = 0; index < 64; index++) Assert.True(f.Observe().DeploymentIdleCandidate);
        Assert.Equal(revision, f.Game.State.Revision);
        Assert.Equal(commands, (await f.Recorder.GetMatchAsync(f.Game.State.MatchId))!.Commands.Count);
        Assert.False((bool)f.RoomProperty("CompletionRecorded")!);
    }

    [Fact]
    public async Task UnknownFutureAndBackwardsClockNeverInventOldActivity()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        f.Now -= TimeSpan.FromMinutes(1);
        Fixture.Success(await f.Manager.SetReadyAsync(f.First, true));
        Assert.Equal(f.Start, f.Observe().LastGameplayActivityUtc);
        Assert.False(f.Observe().AutomaticCloseCandidate);
        Assert.False(f.Observe().DeploymentIdleCandidate);
        f.Room.GetType().GetField("LastGameplayActivityUtcTicks")!.SetValue(f.Room, 0L);
        f.Now += TimeSpan.FromHours(3);
        var unknown = f.Observe();
        Assert.Null(unknown.LastGameplayActivityUtc);
        Assert.False(unknown.AutomaticCloseCandidate);
        Assert.False(unknown.DeploymentIdleCandidate);
    }

    [Theory]
    [InlineData("ranked")]
    [InlineData("tournament")]
    [InlineData("legacy")]
    [InlineData("sandbox")]
    public async Task NeitherClassificationAcceptsAnOutOfScopeMode(string mode)
    {
        await using var f = await Fixture.CreateAsync();
        await f.CreateRoomAsync("friendly");
        // Deliberately adversarial mode metadata on a non-sandbox room, not a second game engine.
        f.Room.GetType().GetProperty("Options")!.SetValue(f.Room, new L12RoomOptions { MatchModeId = mode });
        f.Now += TimeSpan.FromHours(2);
        Assert.False(f.Observe().AutomaticCloseCandidate);
        Assert.False(f.Observe().DeploymentIdleCandidate);
    }

    [Fact]
    public async Task TournamentIdentityTutorialAndTerminalRoomsStayExcluded()
    {
        await using (var f = await Fixture.CreateAsync())
        {
            await f.CreateRoomAsync("friendly");
            f.Room.GetType().GetProperty("TournamentId")!.SetValue(f.Room, "synthetic-tournament");
            f.Now += TimeSpan.FromHours(2);
            Assert.False(f.Observe().AutomaticCloseCandidate);
            Assert.False(f.Observe().DeploymentIdleCandidate);
        }
        await using (var f = await Fixture.CreateAsync())
        {
            Fixture.Success(await f.Manager.CreateTutorialAsync(f.First, "deploy", "activity-tutorial"));
            f.Now += TimeSpan.FromHours(2);
            Assert.Null(f.Observe().LastGameplayActivityUtc);
            Assert.False(f.Observe().AutomaticCloseCandidate);
            Assert.False(f.Observe().DeploymentIdleCandidate);
        }
        await using (var f = await Fixture.CreateAsync())
        {
            await f.CreateRoomAsync("sandbox");
            Fixture.Success(await f.Manager.HandleSandboxActionAsync(f.First, 0,
                JsonSerializer.SerializeToElement(new L12Command("surrender")), "terminal"));
            f.Now += TimeSpan.FromHours(2);
            Assert.False(f.Observe().AutomaticCloseCandidate);
            Assert.False(f.Observe().DeploymentIdleCandidate);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
        internal DateTimeOffset Start => new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
        internal Func<DateTimeOffset>? ClockOverride;
        internal readonly Guid First = Guid.NewGuid();
        internal readonly Guid Second = Guid.NewGuid();
        internal Guid[] Players => [First, Second];
        internal readonly string[] Accounts;
        internal readonly L12Catalog Catalog;
        internal readonly MatchRecorder Recorder;
        internal readonly L12RoomManager Manager;
        private readonly string _root;
        private Fixture(string root, L12Catalog catalog, MatchRecorder recorder)
        {
            _root = root; Catalog = catalog; Recorder = recorder;
            var platform = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var first = platform.Register("activityone", "Password123!").Account!;
            var second = platform.Register("activitytwo", "Password123!").Account!;
            Accounts = [first.Id, second.Id];
            Manager = new(catalog, recorder, platform, () => ClockOverride?.Invoke() ?? Now,
                maintenanceSandboxFenceActive: () => false, tutorialEnabled: true);
            Manager.Connect(First, first.Id, first.Username);
            Manager.Connect(Second, second.Id, second.Username);
        }
        internal static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-room-activity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            return new(root, catalog, recorder);
        }
        internal object Room
        {
            get
            {
                var rooms = (IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Manager)!;
                var entry = rooms.Cast<object>().Single();
                return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
            }
        }
        internal object? RoomProperty(string name) => Room.GetType().GetProperty(name)!.GetValue(Room);
        internal string Code => (string)RoomProperty("Code")!;
        internal L12GameEngine Game => (L12GameEngine)RoomProperty("Game")!;
        internal L12RoomInactivityObservation Observe() => Assert.IsType<L12RoomInactivityObservation>(Manager.ObserveRoomInactivity(Code));
        internal int SelectedDeckIndex(Guid id)
        {
            var sessions = typeof(L12RoomManager).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Manager)!;
            var session = sessions.GetType().GetProperty("Item")!.GetValue(sessions, [id])!;
            return (int)session.GetType().GetProperty("SelectedDeckIndex")!.GetValue(session)!;
        }
        internal async Task CreateRoomAsync(string mode, string disasterMode = "none")
        {
            if (mode == "friendly") Success(Manager.CreateRoom(First,
                new L12RoomOptions { DisasterMode = disasterMode, Spectating = "public", HandVisibility = "request" }));
            else if (mode == "sandbox") Success(await Manager.CreateSandboxAsync(First, new(DisasterMode: "none")));
            else
            {
                Success(await Manager.JoinMatchmakingAsync(First, "casual", null));
                Success(await Manager.JoinMatchmakingAsync(Second, "casual", null));
                Assert.Equal("casual", ((L12RoomOptions)RoomProperty("Options")!).MatchModeId);
            }
        }
        internal async Task StartFriendlyGameAsync(string disasterMode = "none")
        {
            await CreateRoomAsync("friendly", disasterMode);
            Success(Manager.JoinRoom(Second, Code));
            Success(await Manager.SetReadyAsync(First, true));
            Success(await Manager.SetReadyAsync(Second, true));
            Assert.Equal(L12Phase.Initiative, Game.State.Phase);
        }
        internal Task<IReadOnlyList<OutgoingMessage>> Gm(L12GmCommand command, string id)
            => Manager.HandleGmActionAsync(First, JsonSerializer.SerializeToElement(command), id);
        internal static bool IsError(OutgoingMessage message)
            => JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString() is "error" or "actionRejected" or "matchmakingRejected";
        internal static void Success(IReadOnlyList<OutgoingMessage> messages)
        {
            Assert.NotEmpty(messages);
            Assert.DoesNotContain(messages, IsError);
        }
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            PlatformTestDirectoryCleanup.Delete(_root);
        }
    }
}
