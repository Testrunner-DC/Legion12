using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterSetupFactPersistenceTests
{
    [Theory]
    [InlineData("friendly", 0)] [InlineData("casual", 1)]
    [InlineData("ranked", 1)] [InlineData("tournament", 0)]
    public async Task NativePreparationPersistsInAllSupportedModes(string mode, int first)
    {
        await using var f = await DisasterFactFixture.Create(mode);
        await f.Prepare(first);
        await f.End();
        var facts = await f.Read();
        Assert.Equal("exact", facts.Coverage);
        Assert.Equal("complete", facts.Status);
        Assert.Equal(5, facts.Decisions.Length);
        Assert.Equal(new[] { first, 1 - first, first, first, 1 - first }, facts.Decisions.Select(row => row.ActorIndex));
        Assert.Equal(3 * facts.Pool.Length + 2, facts.Decisions.Sum(row => row.Candidates.Length));
        Assert.All(facts.Decisions, row => Assert.Contains(row.ChosenCardId!, row.Candidates));
        Assert.Equal(4, facts.Composition.Length);
        Assert.Empty(f.Game.DisasterSetupObservations);
    }

    [Fact]
    public async Task MigrationFailureIsAtomicAndInitializationIsReentrant()
    {
        await using var f = new DisasterFactFixture();
        f.Recorder.StorageFailureInjector = point => { if (point == "before-disaster-schema-marker") throw new IOException("synthetic-schema"); };
        await Assert.ThrowsAsync<IOException>(() => f.Recorder.InitializeAsync());
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'match_disaster_%';"));
        f.Recorder.StorageFailureInjector = null;
        await f.Recorder.InitializeAsync();
        await f.Recorder.InitializeAsync();
        Assert.Equal(1, await f.Scalar("SELECT version FROM match_recorder_schema WHERE component='disaster-setup-analytics';"));
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM match_disaster_captures;"));
    }

    [Fact]
    public async Task StartFailureRollsBackBothMatchAndCaptureThenRetryWorks()
    {
        await using var f = new DisasterFactFixture();
        await f.Recorder.InitializeAsync();
        f.Recorder.StorageFailureInjector = point => { if (point == "after-disaster-start-write") throw new IOException("synthetic-start"); };
        await Assert.ThrowsAsync<IOException>(() => f.Start());
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM matches;"));
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM match_disaster_captures;"));
        f.Recorder.StorageFailureInjector = null;
        await f.Start();
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_disaster_captures;"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedAppendAndFinalPickLeaveNoHalfFactsAndRecoverSafely(bool finalPick)
    {
        await using var f = await DisasterFactFixture.Create();
        if (finalPick)
            while (!(f.Game.State.DisasterPreparationStep == 5 && f.Game.State.Phase == L12Phase.DisasterPreparation)) await f.Step();
        var before = await f.Scalar("SELECT COUNT(*) FROM match_events;");
        var candidateBefore = await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;");
        f.Recorder.StorageFailureInjector = point => { if (point == "after-disaster-facts-write") throw new IOException("synthetic-append"); };
        await Assert.ThrowsAsync<IOException>(() => f.Step());
        Assert.NotEmpty(f.Game.DisasterSetupObservations);
        Assert.Equal(before, await f.Scalar("SELECT COUNT(*) FROM match_events;"));
        Assert.Equal(candidateBefore, await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;"));
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM match_disaster_captures WHERE setup_status='complete';"));
        f.Recorder.StorageFailureInjector = null;
        await f.Restore();
        Assert.Empty(f.Game.DisasterSetupObservations);
        await f.Prepare();
        await f.End();
        Assert.Equal("exact", (await f.Read()).Coverage);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public async Task CheckpointAndTailRecoveryAtEveryPreparationBoundaryMatchesDirectFacts(int restoreAfter)
    {
        await using var direct = await DisasterFactFixture.Create();
        await direct.Prepare(1);
        await direct.End();
        var expected = JsonSerializer.Serialize(await direct.Read());
        await using var restored = await DisasterFactFixture.Create();
        while (restored.Game.State.Phase is L12Phase.Initiative or L12Phase.DisasterPreparation)
        {
            await restored.Step(1);
            if (restored.Sequence == restoreAfter)
            {
                var hash = restored.Game.ComputeStateHash();
                var eventsBefore = await restored.Scalar("SELECT COUNT(*) FROM match_events;");
                await restored.Restore();
                Assert.Equal(hash, restored.Game.ComputeStateHash());
                Assert.Empty(restored.Game.DisasterSetupObservations);
                Assert.Equal(eventsBefore, await restored.Scalar("SELECT COUNT(*) FROM match_events;"));
            }
        }
        await restored.End();
        Assert.Equal(expected, JsonSerializer.Serialize(await restored.Read()));
    }

    [Fact]
    public async Task DuplicateSequenceChecksThePersistedFactsEvenAfterBufferWasCleared()
    {
        await using var f = await DisasterFactFixture.Create();
        var last = await f.Step();
        var count = await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;");
        await f.Recorder.AppendAsync(f.Game, f.Sequence, last.Actor, last.Json, last.Result);
        Assert.Equal(count, await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;"));
        await f.Execute("DELETE FROM match_disaster_candidates WHERE card_id=(SELECT MIN(card_id) FROM match_disaster_candidates);");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.AppendAsync(f.Game, f.Sequence, last.Actor, last.Json, last.Result));
    }

    [Fact]
    public async Task WrongPlayerOldPromptAndInvalidChoiceDoNotAddOpportunities()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Step();
        var prompt = f.Game.State.PendingPrompts[0];
        var count = await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;");
        await f.Record(1 - prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: prompt.ValidChoices[0]), accepted: false);
        await f.Record(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "not-a-candidate"), accepted: false);
        var last = await f.Step();
        var after = await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;");
        Assert.True(after > count);
        await f.Record(last.Actor, JsonSerializer.Deserialize<L12Command>(last.Json)!, accepted: false);
        Assert.Equal(after, await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;"));
    }

    [Fact]
    public async Task RealRoomClockSuppliesTrustedOriginWhileForgedClientMarkerStaysPlayer()
    {
        await using var f = new DisasterFactFixture();
        await f.Recorder.InitializeAsync();
        var platform = new L12PlatformStore(Path.Combine(f.Root, "synthetic-platform.json"),
            f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        var first = platform.Register("dscplayera", "Synthetic123!").Account!;
        var second = platform.Register("dscplayerb", "Synthetic123!").Account!;
        platform.SelectRankedFaction(first.Id, "order");
        platform.SelectRankedFaction(second.Id, "chaos");
        var manager = new L12RoomManager(f.Catalog, f.Recorder, platform, () => f.Now);
        var sessions = new[] { Guid.NewGuid(), Guid.NewGuid() };
        manager.Connect(sessions[0], first.Id, first.Username);
        manager.Connect(sessions[1], second.Id, second.Username);
        await manager.JoinMatchmakingAsync(sessions[0], "ranked", null);
        var matched = await manager.JoinMatchmakingAsync(sessions[1], "ranked", null);
        var firstState = matched.Where(message => message.SessionId == sessions[0])
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .Single(message => message.GetProperty("type").GetString() == "gameState");
        var id = firstState.GetProperty("state").GetProperty("matchId").GetString()!;
        for (var index = 0; index < 2; index++) // actual initiative then first ban, via the user entry
        {
            var submitted = false;
            foreach (var session in sessions)
            {
                var messages = await manager.RecoveryStateWithAckAsync(session);
                var state = messages.Where(message => message.SessionId == session)
                    .Select(message => JsonSerializer.SerializeToElement(message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                    .Single(message => message.GetProperty("type").GetString() == "gameState").GetProperty("state");
                var prompt = state.GetProperty("prompts").EnumerateArray().FirstOrDefault();
                if (prompt.ValueKind == JsonValueKind.Undefined) continue;
                var choice = prompt.GetProperty("kind").GetString() == "initiative" ? "first"
                    : prompt.GetProperty("validChoices")[0].GetString()!;
                var result = await manager.HandleActionAsync(session, JsonSerializer.SerializeToElement(new
                {
                    type = "resolvePrompt", promptId = prompt.GetProperty("promptId").GetString(),
                    cardInstanceIds = new[] { choice }, destination = "ranked-setup-timeout",
                }));
                Assert.DoesNotContain(result, message => JsonSerializer.SerializeToElement(message.Payload)
                    .GetProperty("type").GetString() == "actionRejected");
                submitted = true;
                break;
            }
            Assert.True(submitted);
        }
        f.Now = f.Now.AddSeconds(61);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => manager.TickRankedClocksAsync(f.Now)));
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_disaster_decisions WHERE step=0 AND origin='player';"));
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_disaster_decisions WHERE step=1 AND origin='server-setup-timeout';"));
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_disaster_decisions WHERE step=2 AND resolved_sequence IS NULL;"));
        Assert.Empty((await f.Recorder.ReadDisasterSetupFactsAsync(id, DisasterFactFixture.Staff)).Decisions);
    }

    [Fact]
    public async Task TrustedTimeoutAndClientDestinationAreDifferentOrigins()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Step();
        var prompt = f.Game.State.PendingPrompts[0];
        await f.Record(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId,
            Choice: prompt.ValidChoices[0], Destination: "ranked-setup-timeout"));
        Assert.True(f.Game.TryCreateRankedSetupTimeoutCommand(f.Game.State.PendingPrompts[0].PlayerIndex, out var timeout));
        await f.Record(f.Game.State.PendingPrompts[0].PlayerIndex, timeout, serverTimeout: true);
        await f.Prepare();
        await f.End();
        var facts = await f.Read();
        Assert.Equal("player", facts.Decisions[0].Origin);
        Assert.Equal("server-setup-timeout", facts.Decisions[1].Origin);
        Assert.Equal(5, facts.Decisions.Length);
    }
}

internal sealed class DisasterFactFixture : IAsyncDisposable
{
    internal static readonly L12AccountView Staff = new("synthetic-staff", "synthetic", "admin", DateTimeOffset.UnixEpoch, false);
    internal DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");
    internal readonly string Root;
    internal string Database => Path.Combine(Root, "matches.db");
    internal readonly L12Catalog Catalog;
    internal L12GameEngine Game;
    internal MatchRecorder Recorder;
    internal long Sequence;
    internal string Mode;

    internal DisasterFactFixture(string mode = "friendly", string disaster = "all", bool skip = false, bool testrun = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "l12-disaster-facts", Guid.NewGuid().ToString("N"));
        if (testrun) Root = Path.Combine(Root, "legion12-testrun-runtime");
        Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        Mode = mode;
        Game = new L12GameEngine(Catalog, "synthetic-dsc", "DSC001", 4071,
            ["synthetic-a", "synthetic-b"], [0, 0], skipPreparation: skip, disasterMode: disaster,
            stateFormatVersion: 2, utcNow: () => Now);
        Recorder = new MatchRecorder(Database, () => Now);
        Recorder.AttachCatalog(Catalog);
    }

    internal static async Task<DisasterFactFixture> Create(string mode = "friendly", string disaster = "all", bool skip = false, bool testrun = false)
    {
        var fixture = new DisasterFactFixture(mode, disaster, skip, testrun);
        await fixture.Recorder.InitializeAsync();
        await fixture.Start();
        return fixture;
    }

    internal Task Start() => Recorder.StartAsync(Game, Mode, "synthetic-account-a", "synthetic-account-b", [Catalog.DeckAt(0), Catalog.DeckAt(0)]);
    internal Task<L12DisasterSetupFactsView> Read() => Recorder.ReadDisasterSetupFactsAsync(Game.State.MatchId, Staff);

    internal async Task<(int Actor, string Json, CommandResult Result)> Step(int first = 0)
    {
        var prompt = Game.State.PendingPrompts[0];
        return await Record(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: prompt.Kind == "initiative" ? [first == Game.State.DiceWinner ? "first" : "second"]
                : prompt.MinChoose == 0 ? [] : [prompt.ValidChoices[0]]));
    }

    internal async Task<(int Actor, string Json, CommandResult Result)> Record(int actor, L12Command command,
        bool accepted = true, bool serverTimeout = false)
    {
        var result = Game.Handle(actor, command);
        Assert.Equal(accepted, result.Accepted);
        Sequence++;
        var json = JsonSerializer.Serialize(command);
        if (serverTimeout)
            await Recorder.AppendRankedAsync(Game, Sequence, actor, json, result, Runtime(), null,
                disasterOrigin: L12DisasterDecisionOrigin.ServerSetupTimeout);
        else await Recorder.AppendAsync(Game, Sequence, actor, json, result);
        return (actor, json, result);
    }

    private L12RankedRuntimeCheckpoint Runtime() => new(2, Game.State.MatchId, Game.State.RoomCode, "active",
        Sequence + 1, Sequence, Game.State.Revision, Game.ComputeStateHash(), Now, 0,
        [600000, 600000], [60000, 60000], [true, true], Now, null, false, [true, true], [null, null],
        ["synthetic-a", "synthetic-b"], [1, 1], Now);

    internal async Task Prepare(int first = 0)
    {
        var steps = 0;
        while (Game.State.Phase is L12Phase.Initiative or L12Phase.DisasterPreparation)
        {
            Assert.True(steps++ < 12);
            await Step(first);
        }
    }

    internal async Task Restore()
    {
        var recovered = Assert.IsType<L12JournalRecoveryState>(await Recorder.LoadJournalEngineAsync(Game.State.MatchId));
        Game = recovered.Engine;
        Sequence = recovered.CommandSequence;
    }

    internal async Task End()
    {
        Game.ConcludeByAuthority(0, "synthetic-result");
        Sequence++;
        await Recorder.AppendAuthorityAsync(Game, Sequence, "synthetic-result");
        await Recorder.CompleteAsync(Game);
    }

    internal async Task Execute(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    internal async Task<long> Scalar(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    public async ValueTask DisposeAsync()
    {
        await Recorder.DisposeAsync();
        using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database }.ToString());
        SqliteConnection.ClearPool(pool);
        // The existing test supervisor owns cleanup after all assertions and host exit.
    }
}
