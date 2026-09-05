using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedPersistenceRecoveryTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RestartRestoresRankedSeatPromptAndClockBeforeReconnectBoundary()
    {
        await using var fixture = await RankedFixture.CreateAsync("restart-before-boundary");
        var durableClock = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(59);

        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restoredPlatform = fixture.ReloadPlatform();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);

        var result = await restored.RestoreRankedRoomsAsync();
        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Invalidated);

        var replacement = Guid.NewGuid();
        var promptOwner = fixture.InitialState.GetProperty("you").GetInt32();
        var promptAccount = promptOwner == 0 ? fixture.First : fixture.Second;
        var claim = JsonSerializer.SerializeToElement(await restored.ConnectAsync(replacement,
            promptAccount.Id, promptAccount.Username), WebJson);
        Assert.True(claim.GetProperty("recovered").GetBoolean());
        Assert.Equal(fixture.RoomCode, claim.GetProperty("roomCode").GetString());

        var recovery = await restored.RecoveryStateWithAckAsync(replacement, recovered: true);
        var payloads = recovery.Where(message => message.SessionId == replacement).Select(MessageJson).ToArray();
        var state = payloads.Single(payload => payload.GetProperty("type").GetString() == "gameState");
        Assert.Equal(fixture.MatchId, state.GetProperty("state").GetProperty("matchId").GetString());
        Assert.NotEmpty(state.GetProperty("state").GetProperty("prompts").EnumerateArray());
        var clock = state.GetProperty("rankedClock").GetProperty("players").EnumerateArray().ToArray();
        for (var player = 0; player < 2; player++)
        {
            var restoredPlayer = clock.Single(item => item.GetProperty("playerIndex").GetInt32() == player);
            Assert.Equal(durableClock.TotalRemainingMs[player],
                restoredPlayer.GetProperty("totalRemainingMs").GetInt64());
            Assert.Equal(durableClock.OperationRemainingMs[player],
                restoredPlayer.GetProperty("operationRemainingMs").GetInt64());
        }
        Assert.True(clock.Single(player => player.GetProperty("playerIndex").GetInt32() == promptOwner)
            .GetProperty("connected").GetBoolean());
        Assert.InRange(clock.Single(player => player.GetProperty("playerIndex").GetInt32() == 1 - promptOwner)
            .GetProperty("reconnectRemainingMs").GetInt64(), 1, 1_000);
        Assert.True(payloads.Single(payload => payload.GetProperty("type").GetString() == "recoveryComplete")
            .GetProperty("rankedClockRestored").GetBoolean());
    }

    [Fact]
    public async Task RestartAtReconnectBoundaryInvalidatesBothDisconnectedSeatsBeforeClaim()
    {
        await using var fixture = await RankedFixture.CreateAsync("restart-at-boundary");
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);

        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restoredPlatform = fixture.ReloadPlatform();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);

        var result = await restored.RestoreRankedRoomsAsync();
        Assert.Equal(0, result.Restored);
        Assert.Equal(1, result.Invalidated);
        var match = Assert.IsType<L12MatchDetail>(await restoredRecorder.GetMatchAsync(fixture.MatchId));
        Assert.NotNull(match.Match.EndedUtc);
        Assert.Null(match.Match.Winner);
        Assert.Null(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        var admin = restoredPlatform.Login("Admin", "L12master").Account!;
        var audit = Assert.Single(restoredPlatform.RankedIntegrityAudits(admin, matchId: fixture.MatchId));
        Assert.Equal("both-disconnect-timeout", audit.ConclusionKind);
    }

    [Fact]
    public async Task RankedCommandAppendFailureDoesNotExposeUncommittedEngineMutation()
    {
        await using var fixture = await RankedFixture.CreateAsync("command-rollback");
        var prompt = fixture.InitialState.GetProperty("prompts").EnumerateArray().Single();
        var owner = prompt.GetProperty("playerIndex").GetInt32();
        var session = owner == 0 ? fixture.FirstSession : fixture.SecondSession;
        var failedOnce = false;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (!failedOnce && stage == "before-ranked-command-commit")
            {
                failedOnce = true;
                throw new IOException("injected command commit failure");
            }
        };
        var command = JsonSerializer.SerializeToElement(new
        {
            type = "resolvePrompt",
            promptId = prompt.GetProperty("promptId").GetString(),
            choice = "first",
        }, WebJson);

        var rejected = await fixture.Manager.HandleActionAsync(session, command);

        Assert.Contains(rejected.Select(MessageJson), payload =>
            payload.GetProperty("type").GetString() == "actionRejected");
        var recorded = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.Empty(recorded.Commands);
        var recovery = await fixture.Manager.RecoveryStateWithAckAsync(session);
        var state = recovery.Where(message => message.SessionId == session).Select(MessageJson)
            .Single(payload => payload.GetProperty("type").GetString() == "gameState")
            .GetProperty("state");
        Assert.Equal(fixture.InitialState.GetProperty("revision").GetInt64(),
            state.GetProperty("revision").GetInt64());
        Assert.Equal(prompt.GetProperty("promptId").GetString(),
            state.GetProperty("prompts").EnumerateArray().Single()
                .GetProperty("promptId").GetString());
    }

    [Fact]
    public async Task GameOverEventFinalRecordRuntimeAndOutboxCommitAtomically()
    {
        await using var fixture = await RankedFixture.CreateAsync("atomic-gameover");
        fixture.Manager.Disconnect(fixture.SessionFor(fixture.ActingPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
        var failedOnce = false;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (!failedOnce && stage == "before-ranked-final-commit")
            {
                failedOnce = true;
                throw new IOException("injected final commit failure");
            }
        };

        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

        var notEnded = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.Null(notEnded.Match.EndedUtc);
        Assert.DoesNotContain(notEnded.Commands, item =>
            item.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.Equal(0, await fixture.Recorder.CountPendingRankedSettlementsAsync());
        Assert.Null(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.First.Id));

        fixture.Recorder.StorageFailureInjector = null;
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

        var ended = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.NotNull(ended.Match.EndedUtc);
        Assert.Single(ended.Commands, item =>
            item.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.Equal(0, await fixture.Recorder.CountPendingRankedSettlementsAsync());
        Assert.NotNull(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.Second.Id));
    }

    [Fact]
    public async Task PendingSettlementReplaysAfterPlatformCommitOrOutboxAckFailureExactlyOnce()
    {
        await using var fixture = await RankedFixture.CreateAsync("outbox-replay");
        fixture.Manager.Disconnect(fixture.SessionFor(fixture.ActingPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
        var ackFailed = false;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (!ackFailed && stage == "before-ranked-outbox-ack")
            {
                ackFailed = true;
                throw new IOException("injected outbox ack failure");
            }
        };

        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

        Assert.Equal(1, await fixture.Recorder.CountPendingRankedSettlementsAsync());
        Assert.NotNull(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.Second.Id));

        fixture.Recorder.StorageFailureInjector = null;
        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restoredPlatform = fixture.ReloadPlatform();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);
        var result = await restored.RestoreRankedRoomsAsync();

        Assert.Equal(1, result.SettlementsApplied);
        Assert.Equal(0, result.Restored);
        Assert.Equal(0, await restoredRecorder.CountPendingRankedSettlementsAsync());
        Assert.NotNull(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.Second.Id));
    }

    [Fact]
    public async Task FriendlyMatchNeverCreatesRankedRuntimeOrOutbox()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"l12-friendly-runtime-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
            var game = new L12GameEngine(catalog, "friendly-no-runtime", "FRIEND", 991,
                ["甲", "乙"], [0, 1], skipPreparation: true);
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            await recorder.StartAsync(game, "friendly", decks: [catalog.DeckAt(0), catalog.DeckAt(1)]);
            Assert.Equal(0, await recorder.CountActiveRankedRuntimesAsync());
            Assert.Equal(0, await recorder.CountPendingRankedSettlementsAsync());
        }
        finally
        {
            await DeleteTestDirectoryAsync(directory);
        }
    }

    [Fact]
    public async Task RuntimeCheckpointIgnoresControlledOutOfOrderStaleWrite()
    {
        await using var fixture = await RankedFixture.CreateAsync("stale-checkpoint");
        var original = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        var newerTotals = original.TotalRemainingMs.ToArray();
        newerTotals[0] -= 2_000;
        var newer = original with
        {
            CheckpointGeneration = original.CheckpointGeneration + 2,
            TotalRemainingMs = newerTotals,
            UpdatedAt = original.UpdatedAt.AddSeconds(2),
        };
        var staleTotals = original.TotalRemainingMs.ToArray();
        staleTotals[0] -= 1_000;
        var stale = original with
        {
            CheckpointGeneration = original.CheckpointGeneration + 1,
            TotalRemainingMs = staleTotals,
            UpdatedAt = original.UpdatedAt.AddSeconds(1),
        };

        await fixture.Recorder.PersistRankedRuntimeBatchAsync([newer]);
        await fixture.Recorder.PersistRankedRuntimeBatchAsync([stale]);

        var actual = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        Assert.Equal(newer.CheckpointGeneration, actual.CheckpointGeneration);
        Assert.Equal(newerTotals[0], actual.TotalRemainingMs[0]);
    }

    [Fact]
    public async Task WatchdogPersistsMultipleRoomsInOneAtomicCheckpointBatch()
    {
        await using var fixture = await RankedFixture.CreateAsync("clock-batch");
        var second = await fixture.CreateAdditionalMatchAsync("clock-batch-second");
        var beforeFirst = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        var beforeSecond = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(second.MatchId));
        fixture.Clock.UtcNow += TimeSpan.FromSeconds(1);
        var injected = 0;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-ranked-runtime-batch-commit")
            {
                injected++;
                throw new IOException("injected runtime batch failure");
            }
        };

        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

        Assert.Equal(1, injected);
        Assert.Equal(beforeFirst.CheckpointGeneration,
            (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId))!.CheckpointGeneration);
        Assert.Equal(beforeSecond.CheckpointGeneration,
            (await fixture.Recorder.GetRankedRuntimeCheckpointAsync(second.MatchId))!.CheckpointGeneration);

        fixture.Recorder.StorageFailureInjector = null;
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        Assert.True((await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId))!
            .CheckpointGeneration > beforeFirst.CheckpointGeneration);
        Assert.True((await fixture.Recorder.GetRankedRuntimeCheckpointAsync(second.MatchId))!
            .CheckpointGeneration > beforeSecond.CheckpointGeneration);
    }

    [Fact]
    public async Task PlatformCommitFailureRollsBackAndPendingOutboxReplaysOnceBeforeLegacyImport()
    {
        await using var fixture = await RankedFixture.CreateAsync("platform-rollback");
        fixture.Manager.Disconnect(fixture.SessionFor(fixture.ActingPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
        fixture.Platform.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("injected platform commit failure");
        };

        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

        Assert.Equal(1, await fixture.Recorder.CountPendingRankedSettlementsAsync());
        Assert.Null(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.Null(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.Second.Id));
        Assert.Empty(await fixture.Recorder.ListRankingMatchesAsync());

        fixture.Platform.StorageFailureInjector = null;
        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restoredPlatform = fixture.ReloadPlatform();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);
        var recovery = await restored.RestoreRankedRoomsAsync();

        Assert.Equal(1, recovery.SettlementsApplied);
        Assert.Equal(0, await restoredRecorder.CountPendingRankedSettlementsAsync());
        Assert.NotNull(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.Second.Id));
        Assert.Empty(await restoredRecorder.ListRankingMatchesAsync());
        Assert.Equal(0, restoredPlatform.ImportRankedMasterHistory(
            await restoredRecorder.ListRankingMatchesAsync()));
    }

    [Fact]
    public async Task CorruptOutboxIsQuarantinedWithoutBlockingNextValidSettlement()
    {
        await using var fixture = await RankedFixture.CreateAsync("outbox-quarantine");
        var second = await fixture.CreateAdditionalMatchAsync("outbox-quarantine-second");
        fixture.Platform.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("hold all settlement outboxes");
        };
        fixture.Manager.Disconnect(fixture.SessionFor(fixture.ActingPlayer));
        fixture.Manager.Disconnect(second.SessionFor(second.ActingPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        Assert.Equal(2, await fixture.Recorder.CountPendingRankedSettlementsAsync());

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                         $"Data Source={fixture.MatchPath}"))
        {
            await connection.OpenAsync();
            var corrupt = connection.CreateCommand();
            corrupt.CommandText = """
                UPDATE ranked_settlement_outbox SET payload_json='{not-json'
                WHERE match_id=$match;
                """;
            corrupt.Parameters.AddWithValue("$match", fixture.MatchId);
            Assert.Equal(1, await corrupt.ExecuteNonQueryAsync());
        }

        fixture.Platform.StorageFailureInjector = null;
        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restoredPlatform = fixture.ReloadPlatform();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);
        var recovery = await restored.RestoreRankedRoomsAsync();

        Assert.Equal(1, recovery.SettlementsApplied);
        Assert.Equal(1, recovery.Failed);
        Assert.Equal(1, await restoredRecorder.CountQuarantinedRankedSettlementsAsync());
        Assert.Equal(0, await restoredRecorder.CountPendingRankedSettlementsAsync());
        Assert.Null(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(restoredPlatform.RankedSettlement(second.MatchId, second.First.Id));
        Assert.NotNull(restoredPlatform.RankedSettlement(second.MatchId, second.Second.Id));
    }

    [Fact]
    public async Task AppliedOutboxRepairsOlderPlatformSnapshotWhenNoLaterSettlementDependsOnIt()
    {
        await using var fixture = await RankedFixture.CreateAsync("applied-reconcile");
        var oldPlatformJson = File.ReadAllText(fixture.PlatformPath);
        fixture.Manager.Disconnect(fixture.SessionFor(fixture.ActingPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        Assert.Equal(0, await fixture.Recorder.CountPendingRankedSettlementsAsync());

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { fixture.PlatformDbPath, fixture.PlatformDbPath + "-wal",
                     fixture.PlatformDbPath + "-shm" })
            if (File.Exists(path)) File.Delete(path);
        File.WriteAllText(fixture.PlatformPath, oldPlatformJson);
        var restoredPlatform = fixture.ReloadPlatform();
        Assert.Null(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);

        var recovery = await restored.RestoreRankedRoomsAsync();

        Assert.Equal(1, recovery.SettlementsApplied);
        Assert.NotNull(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(restoredPlatform.RankedSettlement(fixture.MatchId, fixture.Second.Id));
    }

    [Fact]
    public async Task AppliedOutboxReconciliationFailureIsDurableAndSuccessfulVerifyClearsError()
    {
        await using var fixture = await RankedFixture.CreateAsync("applied-reconcile-diagnostic");
        fixture.Manager.Disconnect(fixture.SessionFor(fixture.ActingPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        await fixture.Recorder.RecordRankedSettlementFailureAsync(fixture.MatchId,
            "injected applied reconciliation conflict");

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                         $"Data Source={fixture.MatchPath}"))
        {
            await connection.OpenAsync();
            var read = connection.CreateCommand();
            read.CommandText = """
                SELECT status,attempts,last_error FROM ranked_settlement_outbox WHERE match_id=$match;
                """;
            read.Parameters.AddWithValue("$match", fixture.MatchId);
            await using var row = await read.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            Assert.Equal("applied", row.GetString(0));
            Assert.Equal(1, row.GetInt32(1));
            Assert.Contains("injected applied", row.GetString(2));
        }

        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder,
            fixture.ReloadPlatform(), () => fixture.Clock.UtcNow);
        var recovery = await restored.RestoreRankedRoomsAsync();
        Assert.Equal(0, recovery.Failed);

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                         $"Data Source={fixture.MatchPath}"))
        {
            await connection.OpenAsync();
            var read = connection.CreateCommand();
            read.CommandText = """
                SELECT attempts,last_error FROM ranked_settlement_outbox WHERE match_id=$match;
                """;
            read.Parameters.AddWithValue("$match", fixture.MatchId);
            await using var row = await read.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            Assert.Equal(1, row.GetInt32(0));
            Assert.True(row.IsDBNull(1));
        }
    }

    [Fact]
    public async Task FrozenRoomRebuildsFromRecorderAndUnfreezesOnlyAfterReconnectCheckpointCommits()
    {
        await using var fixture = await RankedFixture.CreateAsync("frozen-reconnect");
        var prompt = fixture.InitialState.GetProperty("prompts").EnumerateArray().Single();
        var owner = prompt.GetProperty("playerIndex").GetInt32();
        var oldSession = fixture.SessionFor(owner);
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-ranked-runtime-batch-commit")
                throw new IOException("injected disconnect checkpoint failure");
        };
        fixture.Manager.Disconnect(oldSession);

        var account = owner == 0 ? fixture.First : fixture.Second;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Manager.ConnectAsync(
            Guid.NewGuid(), account.Id, account.Username));
        fixture.Recorder.StorageFailureInjector = null;
        var replacement = Guid.NewGuid();
        var claim = await fixture.Manager.ConnectAsync(replacement, account.Id, account.Username);
        Assert.True(claim.Recovered);
        var command = JsonSerializer.SerializeToElement(new
        {
            type = "resolvePrompt",
            promptId = prompt.GetProperty("promptId").GetString(),
            choice = "first",
        }, WebJson);

        var messages = await fixture.Manager.HandleActionAsync(replacement, command);

        Assert.DoesNotContain(messages.Select(MessageJson), payload =>
            payload.GetProperty("type").GetString() == "actionRejected");
        Assert.Single((await fixture.Recorder.GetMatchAsync(fixture.MatchId))!.Commands);
    }

    [Fact]
    public async Task DuplicateRoomCodeQuarantinesOnlyConflictingMatchAndKeepsFirstRecoveryClaimable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"l12-ranked-duplicate-room-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
            var platformPath = Path.Combine(directory, "platform.json");
            var platform = new L12PlatformStore(platformPath, catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var accounts = Enumerable.Range(0, 4)
                .Select(index => platform.Register($"dr{index}{identity}", "Password123!").Account!)
                .ToArray();
            for (var index = 0; index < accounts.Length; index++)
                platform.SelectRankedFaction(accounts[index].Id, index % 2 == 0 ? "order" : "chaos");
            var matchPath = Path.Combine(directory, "matches.db");
            await using var recorder = new MatchRecorder(matchPath);
            await recorder.InitializeAsync();
            var now = new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.Zero);
            const string roomCode = "DUP777";
            var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };

            static L12RankedRuntimeCheckpoint Runtime(L12GameEngine game, DateTimeOffset at)
                => new(1, game.State.MatchId, game.State.RoomCode, "active", 1, 0,
                    game.State.Revision, game.ComputeStateHash(), at, 0,
                    [(long)TimeSpan.FromMinutes(25).TotalMilliseconds,
                        (long)TimeSpan.FromMinutes(25).TotalMilliseconds],
                    [(long)TimeSpan.FromMinutes(4).TotalMilliseconds,
                        (long)TimeSpan.FromMinutes(4).TotalMilliseconds],
                    [true, false], at, null, false, [false, false], [at, at], ["", ""], [0, 0], at);

            var firstGame = new L12GameEngine(catalog, "duplicate-room-first", roomCode, 101,
                [accounts[0].Username, accounts[1].Username], decks);
            await recorder.StartRankedAsync(firstGame, accounts[0].Id, accounts[1].Id,
                decks, Runtime(firstGame, now));
            await Task.Delay(20);
            var secondGame = new L12GameEngine(catalog, "duplicate-room-second", roomCode, 102,
                [accounts[2].Username, accounts[3].Username], decks);
            await recorder.StartRankedAsync(secondGame, accounts[2].Id, accounts[3].Id,
                decks, Runtime(secondGame, now));

            var restoredPlatform = new L12PlatformStore(platformPath, catalog.PresetDecks,
                officialCards: catalog.Cards);
            var manager = new L12RoomManager(catalog, recorder, restoredPlatform, () => now);
            var recovery = await manager.RestoreRankedRoomsAsync();

            Assert.Equal(1, recovery.Restored);
            Assert.Equal(1, recovery.Invalidated);
            var replacement = Guid.NewGuid();
            var claim = JsonSerializer.SerializeToElement(await manager.ConnectAsync(replacement,
                accounts[0].Id, accounts[0].Username), WebJson);
            Assert.True(claim.GetProperty("recovered").GetBoolean());
            Assert.Equal(roomCode, claim.GetProperty("roomCode").GetString());
            Assert.Equal("duplicate-room-first",
                (await recorder.GetMatchAsync("duplicate-room-first"))!.Match.MatchId);
            Assert.NotNull((await recorder.GetMatchAsync("duplicate-room-second"))!.Match.EndedUtc);
        }
        finally
        {
            await DeleteTestDirectoryAsync(directory);
        }
    }

    [Fact]
    public async Task CorruptRuntimeAndInitialStateAreIsolatedPerRowWithoutBlockingHealthyRecovery()
    {
        await using var fixture = await RankedFixture.CreateAsync("corrupt-runtime-row");
        var badInitial = await fixture.CreateAdditionalMatchAsync("corrupt-initial-row");
        var healthy = await fixture.CreateAdditionalMatchAsync("healthy-row");
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                         $"Data Source={fixture.MatchPath}"))
        {
            await connection.OpenAsync();
            var readRuntime = connection.CreateCommand();
            readRuntime.CommandText = "SELECT checkpoint_json FROM ranked_match_runtime WHERE match_id=$match;";
            readRuntime.Parameters.AddWithValue("$match", fixture.MatchId);
            var runtime = JsonNode.Parse((string)(await readRuntime.ExecuteScalarAsync())!)!.AsObject();
            runtime["TotalRemainingMs"] = null;
            var runtimeJson = runtime.ToJsonString();
            var runtimeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(runtimeJson))).ToLowerInvariant();
            var corruptRuntime = connection.CreateCommand();
            corruptRuntime.CommandText = """
                UPDATE ranked_match_runtime SET checkpoint_json=$json,checkpoint_hash=$hash
                WHERE match_id=$match;
                """;
            corruptRuntime.Parameters.AddWithValue("$json", runtimeJson);
            corruptRuntime.Parameters.AddWithValue("$hash", runtimeHash);
            corruptRuntime.Parameters.AddWithValue("$match", fixture.MatchId);
            Assert.Equal(1, await corruptRuntime.ExecuteNonQueryAsync());

            var readInitial = connection.CreateCommand();
            readInitial.CommandText = "SELECT initial_state_json FROM matches WHERE match_id=$match;";
            readInitial.Parameters.AddWithValue("$match", badInitial.MatchId);
            var initial = JsonNode.Parse((string)(await readInitial.ExecuteScalarAsync())!)!.AsObject();
            initial["Players"]!.AsArray()[0]!.AsObject().Remove("MoraleDeck");
            var corruptInitial = connection.CreateCommand();
            corruptInitial.CommandText = "UPDATE matches SET initial_state_json=$json WHERE match_id=$match;";
            corruptInitial.Parameters.AddWithValue("$json", initial.ToJsonString());
            corruptInitial.Parameters.AddWithValue("$match", badInitial.MatchId);
            Assert.Equal(1, await corruptInitial.ExecuteNonQueryAsync());
        }

        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restoredPlatform = fixture.ReloadPlatform();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, restoredPlatform,
            () => fixture.Clock.UtcNow);
        var recovery = await restored.RestoreRankedRoomsAsync();

        Assert.Equal(1, recovery.Restored);
        Assert.Equal(2, recovery.Invalidated);
        Assert.Equal(0, recovery.Failed);
        Assert.Equal(1, await restoredRecorder.CountActiveRankedRuntimesAsync());
        var claimSession = Guid.NewGuid();
        var claim = JsonSerializer.SerializeToElement(await restored.ConnectAsync(claimSession,
            healthy.First.Id, healthy.First.Username), WebJson);
        Assert.True(claim.GetProperty("recovered").GetBoolean());
        Assert.Equal(healthy.MatchId,
            (await restoredRecorder.GetMatchAsync(healthy.MatchId))!.Match.MatchId);
    }

    private static JsonElement MessageJson(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WebJson);

    private static async Task DeleteTestDirectoryAsync(string directory)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(50 * (attempt + 1));
            }
        }
    }

    private sealed class TestClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class RankedFixture : IAsyncDisposable
    {
        private readonly string _directory;
        public required TestClock Clock { get; init; }
        public required L12Catalog Catalog { get; init; }
        public required L12PlatformStore Platform { get; init; }
        public required MatchRecorder Recorder { get; init; }
        public required L12RoomManager Manager { get; init; }
        public required L12AccountView First { get; init; }
        public required L12AccountView Second { get; init; }
        public required Guid FirstSession { get; init; }
        public required Guid SecondSession { get; init; }
        public required string MatchId { get; init; }
        public required string RoomCode { get; init; }
        public required int ActingPlayer { get; init; }
        public required JsonElement InitialState { get; init; }
        public string MatchPath => Path.Combine(_directory, "matches.db");
        public string PlatformPath => Path.Combine(_directory, "platform.json");
        public string PlatformDbPath => Path.Combine(_directory, "platform.db");

        private RankedFixture(string directory) => _directory = directory;

        public static async Task<RankedFixture> CreateAsync(string suffix)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"l12-ranked-persistence-{suffix}-{Guid.NewGuid():N}");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var first = platform.Register($"pf{identity}", "Password123!").Account!;
            var second = platform.Register($"ps{identity}", "Password123!").Account!;
            platform.SelectRankedFaction(first.Id, "order");
            platform.SelectRankedFaction(second.Id, "chaos");
            var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var clock = new TestClock();
            var manager = new L12RoomManager(catalog, recorder, platform, () => clock.UtcNow);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            manager.Connect(firstSession, first.Id, first.Username);
            manager.Connect(secondSession, second.Id, second.Username);
            await manager.JoinMatchmakingAsync(firstSession, "ranked", null);
            var matched = await manager.JoinMatchmakingAsync(secondSession, "ranked", null);
            var games = matched.Select(message => (message.SessionId, Payload: MessageJson(message)))
                .Where(item => item.Payload.GetProperty("type").GetString() == "gameState").ToArray();
            var game = games.Single(item => item.SessionId == firstSession).Payload;
            var promptOwnerGame = games.Single(item => item.Payload.GetProperty("state")
                .GetProperty("prompts").GetArrayLength() > 0).Payload;
            var clockPlayers = game.GetProperty("rankedClock").GetProperty("players").EnumerateArray().ToArray();
            return new RankedFixture(directory)
            {
                Clock = clock,
                Catalog = catalog,
                Platform = platform,
                Recorder = recorder,
                Manager = manager,
                First = first,
                Second = second,
                FirstSession = firstSession,
                SecondSession = secondSession,
                MatchId = game.GetProperty("state").GetProperty("matchId").GetString()!,
                RoomCode = game.GetProperty("state").GetProperty("roomCode").GetString()!,
                ActingPlayer = clockPlayers.Single(player => player.GetProperty("acting").GetBoolean())
                    .GetProperty("playerIndex").GetInt32(),
                InitialState = promptOwnerGame.GetProperty("state").Clone(),
            };
        }

        public Guid SessionFor(int playerIndex) => playerIndex == 0 ? FirstSession : SecondSession;

        public async Task<AdditionalRankedMatch> CreateAdditionalMatchAsync(string label)
        {
            var identity = Guid.NewGuid().ToString("N")[..8];
            var first = Platform.Register($"af{identity}", "Password123!").Account!;
            var second = Platform.Register($"as{identity}", "Password123!").Account!;
            Platform.SelectRankedFaction(first.Id, "order");
            Platform.SelectRankedFaction(second.Id, "chaos");
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            Manager.Connect(firstSession, first.Id, first.Username);
            Manager.Connect(secondSession, second.Id, second.Username);
            await Manager.JoinMatchmakingAsync(firstSession, "ranked", null);
            var matched = await Manager.JoinMatchmakingAsync(secondSession, "ranked", null);
            var game = matched.Select(message => (message.SessionId, Payload: MessageJson(message)))
                .Where(item => item.Payload.GetProperty("type").GetString() == "gameState")
                .Single(item => item.SessionId == firstSession).Payload;
            var players = game.GetProperty("rankedClock").GetProperty("players")
                .EnumerateArray().ToArray();
            return new AdditionalRankedMatch(
                label,
                first,
                second,
                firstSession,
                secondSession,
                game.GetProperty("state").GetProperty("matchId").GetString()!,
                players.Single(player => player.GetProperty("acting").GetBoolean())
                    .GetProperty("playerIndex").GetInt32());
        }

        public L12PlatformStore ReloadPlatform()
            => new(Path.Combine(_directory, "platform.json"), Catalog.PresetDecks, officialCards: Catalog.Cards);

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            await DeleteTestDirectoryAsync(_directory);
        }

        public sealed record AdditionalRankedMatch(
            string Label,
            L12AccountView First,
            L12AccountView Second,
            Guid FirstSession,
            Guid SecondSession,
            string MatchId,
            int ActingPlayer)
        {
            public Guid SessionFor(int playerIndex) => playerIndex == 0 ? FirstSession : SecondSession;
        }
    }
}
