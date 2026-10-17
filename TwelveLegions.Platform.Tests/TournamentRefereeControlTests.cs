using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class TournamentRefereeControlTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task OnlyIndependentRefereeGetsCapabilityWithoutSandboxOrPlayerActions()
    {
        await using var f = await Fixture.Create();
        var judge = State(f.Rooms.SpectateTournamentMatch(f.JudgeSession, f.Tournament.Id, f.Match.Id), f.JudgeSession);
        Assert.True(judge.GetProperty("refereeControlsEnabled").GetBoolean());
        Assert.False(judge.GetProperty("gmEnabled").GetBoolean());
        Assert.Equal("referee", judge.GetProperty("observerView").GetString());
        Assert.Empty(judge.GetProperty("state").GetProperty("prompts").EnumerateArray());
        Assert.Empty(judge.GetProperty("state").GetProperty("legalAttackTargets").EnumerateObject());
        f.Rooms.Connect(f.ViewerSession, f.Viewer.Id, f.Viewer.Username);
        var ordinary = State(f.Rooms.SpectateTournamentMatch(f.ViewerSession, f.Tournament.Id, f.Match.Id), f.ViewerSession);
        Assert.False(ordinary.GetProperty("refereeControlsEnabled").GetBoolean());
        var before = f.Engine.ComputeStateHash();
        AssertRejected(await f.Gm(f.ViewerSession, "outsider"));
        AssertRejected(await f.Gm(f.HostSession, "own-table"));
        AssertRejected(await f.Rooms.HandleSandboxActionAsync(f.JudgeSession, 0,
            JsonSerializer.SerializeToElement(new { type = "endTurn" }), "no-sandbox"));
        Assert.Equal(before, f.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task PauseAndReasonAreRequiredAndActorCannotBeForged()
    {
        await using var f = await Fixture.Create();
        f.Observe();
        AssertRejected(await f.Gm(f.JudgeSession, "not-paused"));
        await f.Pause();
        AssertRejected(await f.Gm(f.JudgeSession, "no-reason", reason: " "));
        AssertRejected(await f.Gm(f.JudgeSession, "long-reason", reason: new string('x', 501)));
        var result = await f.Gm(f.JudgeSession, "accepted");
        Assert.Contains(result, message => message.SessionId == f.HostSession && message.IsGameState);
        Assert.Equal("accepted", State(result, f.JudgeSession).GetProperty("requestId").GetString());
        foreach (var other in result.Where(message => message.SessionId != f.JudgeSession && message.IsGameState))
        {
            var payload = JsonSerializer.SerializeToElement(other.Payload, Wire);
            Assert.True(!payload.TryGetProperty("requestId", out var id) || id.ValueKind == JsonValueKind.Null);
        }
        Assert.Equal(18, f.Engine.State.Players[0].Hp);
        var audit = f.Engine.State.Events.Last(item => item.Type == "gm");
        Assert.Equal("裁判已调整本桌场面", audit.Text);
        Assert.DoesNotContain("synthetic correction", audit.Text);
        Assert.DoesNotContain("forged", audit.Text);
        var recorded = (await f.Recorder.LoadActiveRankedMatchAsync(f.Engine.State.MatchId))!.Commands.Last();
        using var recordedJson = JsonDocument.Parse(recorded.CommandJson);
        Assert.Equal("setLife", recordedJson.RootElement.GetProperty("type").GetString());
        Assert.Equal(f.Judge.Id, recordedJson.RootElement.GetProperty("refereeAccountId").GetString());
        Assert.Equal(f.Judge.Username, recordedJson.RootElement.GetProperty("refereeName").GetString());
        Assert.Equal("synthetic correction", recordedJson.RootElement.GetProperty("refereeReason").GetString());
    }

    [Fact]
    public async Task HiddenCorrectionsKeepOnlyNeutralEventsButDurableAuditAndBothRecoveriesRemainComplete()
    {
        await using var f = await Fixture.Create(); f.Observe(); await f.Pause();
        var matchId = f.Engine.State.MatchId;
        foreach (var destination in new[] { "hand", "library-top", "library-bottom" })
        {
            var sequence = f.Engine.State.EventSequence;
            var result = await f.Rooms.HandleGmActionAsync(f.JudgeSession,
                JsonSerializer.SerializeToElement(new L12GmCommand("addCard", 0, "S01-0103",
                    Value: 2, Destination: destination, RefereeReason: "synthetic hidden correction")),
                "private-" + destination);
            Assert.Contains(result, message => message.IsGameState);
            var notice = Assert.Single(f.Engine.State.Events.Where(item => item.Sequence > sequence));
            Assert.Equal("gm", notice.Type);
            Assert.Equal("裁判已调整本桌场面", notice.Text);
            Assert.Empty(notice.Cards);
        }
        var hiddenCard = f.Engine.State.Players[0].Hand.Last(card => card.CardId == "S01-0103");
        var beforeMove = f.Engine.State.EventSequence;
        await f.Rooms.HandleGmActionAsync(f.JudgeSession,
            JsonSerializer.SerializeToElement(new L12GmCommand("moveHandCard", 0,
                CardInstanceId: hiddenCard.InstanceId, Destination: "library-top",
                RefereeReason: "synthetic private return")), "private-return");
        Assert.Empty(Assert.Single(f.Engine.State.Events.Where(item => item.Sequence > beforeMove)).Cards);
        foreach (var snapshot in new[] { f.Engine.SnapshotFor(0), f.Engine.SnapshotFor(1),
            f.Engine.SnapshotForSpectator(), f.Engine.SnapshotForReferee() })
        foreach (var action in snapshot.RecentEvents.Where(item => item.Type == "gm"))
        {
            Assert.Equal("裁判已调整本桌场面", action.Text);
            Assert.Empty(action.Cards);
        }
        var source = (await f.Recorder.LoadActiveRankedMatchAsync(matchId))!;
        Assert.Contains(source.Commands, item => item.CommandJson.Contains("synthetic hidden correction", StringComparison.Ordinal)
            && item.CommandJson.Contains(f.Judge.Id, StringComparison.Ordinal));
        var hash = f.Engine.ComputeStateHash();
        Assert.Equal(hash, (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());
        var recovered = (L12GameEngine)typeof(L12RoomManager).GetMethod("ReplayRankedEngine",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Rooms, [source])!;
        Assert.Equal(hash, recovered.ComputeStateHash());
        await f.Gm(f.JudgeSession, "private-finish", value: 0);
        foreach (var name in new[] { f.Host.Username, f.Player.Username })
        {
            var replay = await f.Recorder.GetMatchForPlayerAsync(matchId, name);
            Assert.NotNull(replay);
            foreach (var command in replay.Commands)
            foreach (var action in command.State.GetProperty("Events").EnumerateArray()
                .Where(item => item.GetProperty("Type").GetString() == "gm"))
            {
                Assert.Equal("裁判已调整本桌场面", action.GetProperty("Text").GetString());
                Assert.Empty(action.GetProperty("Cards").EnumerateArray());
            }
        }
    }

    [Fact]
    public async Task DuplicateAndJournalRecoveryPreserveExactlyOneCorrection()
    {
        await using var f = await Fixture.Create();
        f.Observe(); await f.Pause();
        await f.Gm(f.JudgeSession, "same");
        var hash = f.Engine.ComputeStateHash();
        var revision = f.Engine.State.Revision;
        await f.Gm(f.JudgeSession, "same", value: 2);
        Assert.Equal(hash, f.Engine.ComputeStateHash());
        Assert.Equal(revision, f.Engine.State.Revision);
        var recovered = await f.Recorder.LoadJournalEngineAsync(f.Engine.State.MatchId);
        Assert.NotNull(recovered);
        Assert.Equal(hash, recovered.Engine.ComputeStateHash());
        var source = await f.Recorder.LoadActiveRankedMatchAsync(f.Engine.State.MatchId);
        Assert.NotNull(source);
        var replayed = (L12GameEngine)typeof(L12RoomManager).GetMethod("ReplayRankedEngine",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Rooms, [source])!;
        Assert.Equal(hash, replayed.ComputeStateHash());
        Assert.True(source.Runtime!.Paused);
    }

    [Fact]
    public async Task PersistenceFailureRollsBackBothGameAndClockThenRetryIsSafe()
    {
        await using var f = await Fixture.Create();
        f.Observe(); await f.Pause();
        var hash = f.Engine.ComputeStateHash();
        f.Recorder.StorageFailureInjector = stage =>
        { if (stage == "before-ranked-command-commit") throw new IOException("synthetic failure"); };
        AssertRejected(await f.Gm(f.JudgeSession, "retry"));
        f.Recorder.StorageFailureInjector = null;
        Assert.Equal(hash, f.Engine.ComputeStateHash());
        Assert.True(State(await f.Rooms.RecoveryStateWithAckAsync(f.HostSession), f.HostSession)
            .GetProperty("rankedClock").GetProperty("paused").GetBoolean());
        await f.Gm(f.JudgeSession, "retry");
        Assert.Equal(18, f.Engine.State.Players[0].Hp);
    }

    [Fact]
    public async Task RevokedStaffIsRejectedBeforeEvenARepeatedRequest()
    {
        await using var f = await Fixture.Create();
        f.Observe(); await f.Pause(); await f.Gm(f.JudgeSession, "old");
        var hash = f.Engine.ComputeStateHash();
        f.Tournament = f.Store.SetTournamentStaff(f.Host, f.Tournament.Id,
            new L12TournamentStaffPayload([]), f.Tournament.Version, Context("revoke"), true);
        AssertRejected(await f.Gm(f.JudgeSession, "old"));
        AssertRejected(await f.Gm(f.JudgeSession, "new"));
        Assert.Equal(hash, f.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task CompletedCorrectionCanRecoverOnlyItsOwnReceiptWithoutAnyRoomOrSnapshot()
    {
        await using var f = await Fixture.Create(); f.Observe(); await f.Pause();
        var matchId = f.Engine.State.MatchId;
        var command = JsonSerializer.SerializeToElement(new { type = "setLife", targetPlayer = 0,
            value = 0, refereeReason = "synthetic terminal correction", refereeMatchId = matchId });
        await f.Rooms.HandleGmActionAsync(f.JudgeSession, command, "terminal-once");
        Assert.Equal(L12Phase.GameOver, f.Engine.State.Phase);
        var finalHash = f.Engine.ComputeStateHash();
        f.Rooms.Disconnect(f.JudgeSession);
        var replacement = Guid.NewGuid();
        await f.Rooms.ConnectAsync(replacement, f.Judge.Id, f.Judge.Username);
        var receipt = Assert.Single(await f.Rooms.HandleGmActionAsync(replacement, command, "terminal-once"));
        var payload = JsonSerializer.SerializeToElement(receipt.Payload, Wire);
        Assert.Equal("refereeActionReceipt", payload.GetProperty("type").GetString());
        Assert.True(payload.GetProperty("accepted").GetBoolean());
        Assert.False(payload.TryGetProperty("state", out _));
        Assert.False(payload.TryGetProperty("refereeReason", out _));
        AssertRejected(await f.Rooms.HandleGmActionAsync(replacement, command, "different-request"));
        f.Rooms.Connect(f.ViewerSession, f.Viewer.Id, f.Viewer.Username);
        AssertRejected(await f.Rooms.HandleGmActionAsync(f.ViewerSession, command, "terminal-once"));
        Assert.Equal(finalHash, f.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task ExtensionOutboxRetriesAbsoluteCommittedBudgetWithoutDoubleAdding()
    {
        await using var f = await Fixture.Create(); f.Observe();
        f.Tournament = f.Store.ExtendTournamentMatch(f.Host, f.Tournament.Id, f.Match.Id,
            new L12TournamentTimeExtensionPayload(1, "synthetic extension"), f.Tournament.Version, Context("extend"), true);
        Assert.Single(f.Store.PendingTournamentRoomCommands(f.Tournament.Id));
        f.Recorder.StorageFailureInjector = stage =>
        { if (stage == "before-ranked-runtime-batch-commit") throw new IOException("synthetic extension failure"); };
        var failed = await f.Rooms.DrainTournamentRoomCommandsAsync(f.Tournament.Id);
        Assert.Equal(1, failed.Failed); Assert.Equal(1, failed.Pending);
        f.Recorder.StorageFailureInjector = null;
        var rolledBack = State(await f.Rooms.RecoveryStateWithAckAsync(f.HostSession), f.HostSession);
        Assert.Equal(900_000, rolledBack.GetProperty("rankedClock").GetProperty("totalLimitMs").GetInt64());
        var retried = await f.Rooms.DrainTournamentRoomCommandsAsync(f.Tournament.Id);
        Assert.Equal(1, retried.Applied); Assert.Equal(0, retried.Pending);
        var before = State(retried.Messages, f.HostSession).GetProperty("rankedClock");
        Assert.Equal(960_000, before.GetProperty("totalLimitMs").GetInt64());
        var repeated = State(await f.Rooms.ExtendTournamentClockAsync(f.Tournament.Id, f.Match.Id, 1), f.HostSession)
            .GetProperty("rankedClock");
        Assert.Equal(before.GetProperty("players")[0].GetProperty("totalRemainingMs").GetInt64(),
            repeated.GetProperty("players")[0].GetProperty("totalRemainingMs").GetInt64());
    }

    [Fact]
    public async Task FailedPauseCannotBeOvertakenByQueuedResumeEvenWhenClockOrIdsSortBackwards()
    {
        await using var f = await Fixture.Create(); f.Observe();
        f.Tournament = f.Store.PauseTournamentMatch(f.Judge, f.Tournament.Id, f.Match.Id,
            new L12TournamentMatchPausePayload(true, "pause first"), f.Tournament.Version, Context("pause-first"), true);
        f.Tournament = f.Store.PauseTournamentMatch(f.Judge, f.Tournament.Id, f.Match.Id,
            new L12TournamentMatchPausePayload(false, "resume second"), f.Tournament.Version, Context("resume-second"), true);
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Store)!;
        var tournament = ((IEnumerable)data.GetType().GetProperty("Tournaments")!.GetValue(data)!).Cast<object>().Single();
        var queue = ((IEnumerable)tournament.GetType().GetProperty("RoomCommands")!.GetValue(tournament)!).Cast<object>().ToArray();
        Assert.Equal(2, queue.Length);
        queue[0].GetType().GetProperty("CreatedAt")!.SetValue(queue[0], f.Now.AddMinutes(1));
        queue[1].GetType().GetProperty("CreatedAt")!.SetValue(queue[1], f.Now);
        queue[0].GetType().GetProperty("Id")!.SetValue(queue[0], "zz-first");
        queue[1].GetType().GetProperty("Id")!.SetValue(queue[1], "aa-second");
        f.Recorder.StorageFailureInjector = stage =>
        { if (stage == "before-ranked-runtime-batch-commit") throw new IOException("synthetic pause failure"); };
        var failed = await f.Rooms.DrainTournamentRoomCommandsAsync(f.Tournament.Id);
        Assert.Equal(0, failed.Applied); Assert.Equal(1, failed.Failed); Assert.Equal(2, failed.Pending);
        Assert.All(f.Store.PendingTournamentRoomCommands(f.Tournament.Id), command =>
            Assert.Equal(command.Id == "zz-first" ? 1 : 0, command.Attempts));
        f.Recorder.StorageFailureInjector = null;
        var retry = await f.Rooms.DrainTournamentRoomCommandsAsync(f.Tournament.Id);
        Assert.Equal(2, retry.Applied); Assert.Equal(0, retry.Pending);
        Assert.False(State(retry.Messages.Reverse().ToArray(), f.HostSession)
            .GetProperty("rankedClock").GetProperty("paused").GetBoolean());
    }

    private static JsonElement State(IReadOnlyList<OutgoingMessage> messages, Guid session)
        => messages.Where(item => item.SessionId == session && item.IsGameState)
            .Select(item => JsonSerializer.SerializeToElement(item.Payload, Wire)).First();
    private static void AssertRejected(IReadOnlyList<OutgoingMessage> messages)
        => Assert.Contains(messages, item => JsonSerializer.SerializeToElement(item.Payload, Wire)
            .GetProperty("type").GetString() is "actionRejected" or "error");
    private static L12AdminAuditContext Context(string id)
        => new(id, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/referee-control");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "l12-referee-control", Guid.NewGuid().ToString("N"));
        internal L12PlatformStore Store = null!;
        internal L12RoomManager Rooms = null!;
        internal MatchRecorder Recorder = null!;
        internal L12AccountView Host = null!, Player = null!, Judge = null!, Viewer = null!;
        internal L12TournamentView Tournament = null!;
        internal L12TournamentMatchView Match = null!;
        internal Guid HostSession = Guid.NewGuid(), PlayerSession = Guid.NewGuid(), JudgeSession = Guid.NewGuid(), ViewerSession = Guid.NewGuid();
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        private object Room => ((IEnumerable)typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Rooms)!).Cast<object>().Select(entry => entry.GetType().GetProperty("Value")!.GetValue(entry)!)
            .Single(room => (string)room.GetType().GetProperty("Code")!.GetValue(room)! == Match.RoomCode);
        internal L12GameEngine Engine => (L12GameEngine)Room.GetType().GetProperty("Game")!.GetValue(Room)!;

        internal static async Task<Fixture> Create()
        {
            var f = new Fixture(); Directory.CreateDirectory(f._root);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            f.Store = new L12PlatformStore(Path.Combine(f._root, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards);
            L12AccountView Register(string username)
            {
                var registered = f.Store.Register(username, "password-123");
                Assert.True(registered.Success, registered.Message);
                return Assert.IsType<L12AccountView>(registered.Account);
            }
            f.Host = Register("rfhost");
            f.Player = Register("rfplayer");
            f.Judge = Register("rfjudge");
            f.Viewer = Register("rfviewer");
            f.Store.SendFriendRequest(f.Host.Id, f.Judge.Id);
            f.Store.ResolveFriendRequest(f.Judge.Id, f.Host.Id, true);
            f.Tournament = f.Store.CreateTournament(f.Host, new L12TournamentCreatePayload(
                "GM synthetic", "single", "public", 8, DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "synthetic",
                "private", "season", string.Empty, 50, 5, [f.Judge.Id],
                TimeControl: new L12RankedTimeControlConfig(900, 90, 120, 30, 45)), Context("create"), true);
            foreach (var account in new[] { f.Host, f.Player })
            {
                var payload = new L12TournamentRegistrationPayload();
                f.Tournament = account == f.Host
                    ? f.Store.UpdateTournamentRegistration(account, f.Tournament.Id, payload, f.Tournament.Version, Context("host-register"), true)
                    : f.Store.RegisterTournament(account, f.Tournament.Id, payload, f.Tournament.Version, Context("player-register"), true);
                f.Tournament = f.Store.PreCheckInTournament(account, f.Tournament.Id,
                    new L12TournamentPreCheckInPayload(f.Store.Decks(account.Id)[0].Name, string.Empty),
                    f.Tournament.Version, Context("precheck" + account.Id), true);
            }
            f.Tournament = f.Store.StartTournament(f.Host, f.Tournament.Id, f.Tournament.Version, Context("start"), true);
            foreach (var account in new[] { f.Host, f.Player })
                f.Tournament = f.Store.CheckInTournament(account, f.Tournament.Id, 1,
                    new L12TournamentCheckInPayload(account.Id, true), f.Tournament.Version, Context("ready" + account.Id), true);
            f.Tournament = f.Store.StartTournamentRound(f.Host, f.Tournament.Id, 1, f.Tournament.Version, Context("round"), true);
            f.Match = Assert.Single(f.Tournament.Rounds[0].Matches);
            f.Recorder = new MatchRecorder(Path.Combine(f._root, "matches.db")); await f.Recorder.InitializeAsync();
            f.Rooms = new L12RoomManager(catalog, f.Recorder, f.Store, utcNow: () => f.Now);
            f.Rooms.Connect(f.HostSession, f.Host.Id, f.Host.Username);
            f.Rooms.Connect(f.PlayerSession, f.Player.Id, f.Player.Username);
            f.Rooms.Connect(f.JudgeSession, f.Judge.Id, f.Judge.Username);
            await f.Rooms.EnterTournamentMatchAsync(f.HostSession, f.Tournament.Id, f.Match.Id);
            await f.Rooms.EnterTournamentMatchAsync(f.PlayerSession, f.Tournament.Id, f.Match.Id);
            return f;
        }
        internal void Observe() => Rooms.SpectateTournamentMatch(JudgeSession, Tournament.Id, Match.Id);
        internal async Task Pause()
        {
            Tournament = Store.PauseTournamentMatch(Judge, Tournament.Id, Match.Id,
                new L12TournamentMatchPausePayload(true, "synthetic pause"), Tournament.Version, Context("pause"), true);
            Assert.Equal(1, (await Rooms.DrainTournamentRoomCommandsAsync(Tournament.Id)).Applied);
        }
        internal Task<IReadOnlyList<OutgoingMessage>> Gm(Guid session, string request, int value = 18, string reason = "synthetic correction")
            => Rooms.HandleGmActionAsync(session, JsonSerializer.SerializeToElement(new L12GmCommand(
                "setLife", Value: value, RefereeReason: reason, RefereeName: "forged", RefereeAccountId: "forged")), request);
        public async ValueTask DisposeAsync()
        {
            Recorder.StorageFailureInjector = null;
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, true);
        }
    }
}
