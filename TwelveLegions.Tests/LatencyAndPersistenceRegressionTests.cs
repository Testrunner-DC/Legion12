using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LatencyAndPersistenceRegressionTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public async Task 单连接发送队列只合并连续普通快照并保持关键消息顺序()
    {
        var received = new List<string>();
        var senderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new L12OutboundConnection(async (payload, _) =>
        {
            var value = Assert.IsType<string>(payload);
            received.Add(value);
            if (value != "gate") return;
            senderEntered.TrySetResult();
            await releaseSender.Task;
        });

        Assert.True(connection.TryEnqueue("gate"));
        await senderEntered.Task;
        Assert.True(connection.TryEnqueue("state-1", replaceableGameState: true));
        Assert.True(connection.TryEnqueue("state-2", replaceableGameState: true));
        Assert.True(connection.TryEnqueue("prompt"));
        Assert.True(connection.TryEnqueue("state-3", replaceableGameState: true));
        Assert.True(connection.TryEnqueue("state-4", replaceableGameState: true));
        releaseSender.TrySetResult();
        await connection.CompleteAsync();

        Assert.Equal(["gate", "state-2", "prompt", "state-4"], received);
        Assert.Equal(2, connection.MergedCount);
    }

    [Fact]
    public async Task 慢连接队列始终有界且关键消息可淘汰普通快照()
    {
        var senderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<string>();
        await using var connection = new L12OutboundConnection(async (payload, _) =>
        {
            var value = Assert.IsType<string>(payload);
            received.Add(value);
            if (value != "gate") return;
            senderEntered.TrySetResult();
            await releaseSender.Task;
        });

        Assert.True(connection.TryEnqueue("gate"));
        await senderEntered.Task;
        Assert.True(connection.TryEnqueue("replaceable", replaceableGameState: true));
        Assert.True(connection.TryEnqueue("barrier"));
        for (var index = 0; index < L12OutboundConnection.MaximumQueuedMessages - 2; index++)
            Assert.True(connection.TryEnqueue($"critical-{index}"));

        Assert.Equal(L12OutboundConnection.MaximumQueuedMessages, connection.QueuedCount);
        Assert.True(connection.TryEnqueue("final-critical"));
        Assert.Equal(L12OutboundConnection.MaximumQueuedMessages, connection.QueuedCount);
        Assert.True(connection.MaximumObservedDepth <= L12OutboundConnection.MaximumQueuedMessages);
        Assert.Equal(1, connection.DroppedCount);

        releaseSender.TrySetResult();
        await connection.CompleteAsync();
        Assert.Contains("final-critical", received);
        Assert.DoesNotContain("replaceable", received);
    }

    [Fact]
    public async Task 单个慢观战连接发送超时只隔离自身而不阻塞其他连接()
    {
        var slowFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fastDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slow = new L12OutboundConnection(
            async (_, cancellationToken) => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
            error => slowFault.TrySetResult(error), TimeSpan.FromMilliseconds(30));
        await using var fast = new L12OutboundConnection((_, _) =>
        {
            fastDelivered.TrySetResult();
            return Task.CompletedTask;
        });

        Assert.True(slow.TryEnqueue("spectator-snapshot"));
        Assert.True(fast.TryEnqueue("player-critical"));
        await fastDelivered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var isolated = await slowFault.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsAssignableFrom<OperationCanceledException>(isolated);
        Assert.Equal(0, fast.QueuedCount);
    }

    [Fact]
    public async Task 对局开始的首批快照不可替换且强制完整()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-initial-snapshot",
            Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        manager.Connect(first, "initial-a", "甲");
        manager.Connect(second, "initial-b", "乙");
        var roomCode = Payload(manager.CreateRoom(first)[0]).GetProperty("roomCode").GetString();
        manager.JoinRoom(second, roomCode);
        await manager.SetReadyAsync(first, true);
        var started = await manager.SetReadyAsync(second, true);
        var initialStates = started.Where(message =>
            Payload(message).GetProperty("type").GetString() == "gameState").ToArray();

        Assert.Equal(2, initialStates.Length);
        Assert.All(initialStates, message =>
        {
            Assert.False(message.ReplaceableGameState);
            Assert.True(message.IsGameState);
            Assert.True(message.ForceFullGameState);
        });

        var sandboxSession = Guid.NewGuid();
        manager.Connect(sandboxSession, "sandbox-initial", "沙盒");
        var sandbox = await manager.CreateSandboxAsync(sandboxSession, new L12SandboxRequest());
        var sandboxStates = sandbox.Where(message =>
            Payload(message).GetProperty("type").GetString() == "gameState").ToArray();
        Assert.Equal(2, sandboxStates.Length);
        Assert.All(sandboxStates, message =>
        {
            Assert.False(message.ReplaceableGameState);
            Assert.True(message.ForceFullGameState);
        });
    }

    [Fact]
    public void 对手Prompt与防御交互按视角进入不可丢队列分类()
    {
        var engine = new L12GameEngine(Catalog, "critical-view", "CRITICAL", 104729,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        engine.State.Phase = L12Phase.Reset;
        engine.State.PendingPrompts.Clear();
        engine.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "opponent-prompt",
            PlayerIndex = 1,
            Kind = "fixture",
            Text = "由对手处理",
            ValidChoices = ["confirm"],
            Continuation = "fixture",
        });

        var actorWaiting = engine.SnapshotFor(0);
        var opponentPrompt = engine.SnapshotFor(1);
        Assert.Empty(actorWaiting.Prompts);
        Assert.NotNull(actorWaiting.WaitingPrompt);
        Assert.False(L12RoomManager.SnapshotRequiresCriticalDelivery(actorWaiting));
        Assert.Single(opponentPrompt.Prompts);
        Assert.True(L12RoomManager.SnapshotRequiresCriticalDelivery(opponentPrompt));

        engine.State.PendingPrompts.Clear();
        engine.State.Phase = L12Phase.Defense;
        engine.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0,
            AttackerInstanceId = "attacker",
            Target = new L12AttackTarget("master"),
            Stage = L12CombatStage.DefenseChoice,
        };
        Assert.False(L12RoomManager.SnapshotRequiresCriticalDelivery(engine.SnapshotFor(0)));
        Assert.True(L12RoomManager.SnapshotRequiresCriticalDelivery(engine.SnapshotFor(1)));
    }

    [Fact]
    public async Task 重复请求和断线重发不会重复执行或重复持久化()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-request-id", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        manager.Connect(first, "account-a", "甲");
        manager.Connect(second, "account-b", "乙");
        var roomCode = Payload(manager.CreateRoom(first)[0]).GetProperty("roomCode").GetString();
        manager.JoinRoom(second, roomCode);
        await manager.SetReadyAsync(first, true);
        var started = await manager.SetReadyAsync(second, true);
        var matchId = Payload(started.Single(message => message.SessionId == first
            && Payload(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state").GetProperty("matchId").GetString()!;

        var command = JsonSerializer.SerializeToElement(new { type = "surrender" });
        var requestId = Guid.NewGuid().ToString("N");
        var accepted = await manager.HandleActionAsync(first, command, requestId);
        Assert.Equal(requestId, Payload(accepted.Single(message => message.SessionId == first
            && Payload(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("requestId").GetString());

        var replacement = Guid.NewGuid();
        var claim = await manager.ConnectAsync(replacement, "account-a", "甲");
        Assert.True(claim.Recovered);
        var duplicate = await manager.HandleActionAsync(replacement, command, requestId);
        Assert.Equal(requestId, Payload(duplicate.Single(message => message.SessionId == replacement
            && Payload(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("requestId").GetString());

        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        Assert.Single(detail.Commands);
    }

    [Fact]
    public async Task 非法Prompt请求结束后合法动作不被旧请求响应解除或阻塞()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-request-order", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        manager.Connect(first, "account-a", "甲");
        manager.Connect(second, "account-b", "乙");
        var roomCode = Payload(manager.CreateRoom(first)[0]).GetProperty("roomCode").GetString();
        manager.JoinRoom(second, roomCode);
        await manager.SetReadyAsync(first, true);
        var started = await manager.SetReadyAsync(second, true);
        var matchId = Payload(started.Single(message => message.SessionId == first
            && Payload(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state").GetProperty("matchId").GetString()!;

        var rejectedId = "request-rejected";
        var rejected = await manager.HandleActionAsync(first, JsonSerializer.SerializeToElement(new
        {
            type = "resolvePrompt", promptId = "missing-prompt", option = "invalid",
        }), rejectedId);
        var rejection = Payload(Assert.Single(rejected));
        Assert.Equal("actionRejected", rejection.GetProperty("type").GetString());
        Assert.Equal(rejectedId, rejection.GetProperty("requestId").GetString());
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var audit = connection.CreateCommand();
            audit.CommandText = """
                SELECT length(state_json),
                       (SELECT COUNT(*) FROM match_state_checkpoints WHERE match_id=$match)
                FROM match_events WHERE match_id=$match AND sequence=1;
                """;
            audit.Parameters.AddWithValue("$match", matchId);
            await using var reader = await audit.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        }

        var acceptedId = "request-accepted";
        var accepted = await manager.HandleActionAsync(first,
            JsonSerializer.SerializeToElement(new { type = "surrender" }), acceptedId);
        Assert.Contains(accepted, message => message.SessionId == first
            && Payload(message).GetProperty("type").GetString() == "gameState"
            && Payload(message).GetProperty("requestId").GetString() == acceptedId);
    }

    [Fact]
    public async Task 纯拒绝跨过检查点周期仍只追加轻量审计且可连续重放()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-rejection-boundary",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        var engine = new L12GameEngine(Catalog, "rejection-boundary", "REJECT", 4402,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine);
        var initialHash = engine.ComputeStateHash();

        for (var sequence = 1; sequence <= MatchRecorder.CheckpointInterval; sequence++)
        {
            var command = new L12Command("unknown-action");
            var result = engine.Handle(0, command);
            Assert.False(result.Accepted);
            await recorder.AppendAsync(engine, sequence, 0, JsonSerializer.Serialize(command), result,
                $"rejected-{sequence}");
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var inspect = connection.CreateCommand();
            inspect.CommandText = """
                SELECT (SELECT COUNT(*) FROM match_state_checkpoints WHERE match_id=$match),
                       COUNT(*),SUM(length(state_json))
                FROM match_events WHERE match_id=$match;
                """;
            inspect.Parameters.AddWithValue("$match", engine.State.MatchId);
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(MatchRecorder.CheckpointInterval, reader.GetInt32(1));
            Assert.Equal(MatchRecorder.CheckpointInterval * 2, reader.GetInt32(2));
        }

        var recovery = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(engine.State.MatchId));
        Assert.Equal(MatchRecorder.CheckpointInterval, recovery.CommandSequence);
        Assert.Equal(initialHash, recovery.Engine.ComputeStateHash());
    }

    [Fact]
    public void 一千二百事件长局的内存与网络表现窗口均保持有界且序号连续()
    {
        var engine = new L12GameEngine(Catalog, "long-events", "EVENTS", 20260908,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        for (var sequence = 1; sequence <= 1201; sequence++)
        {
            var result = engine.HandleGm(new L12GmCommand("setLife", 0, Value: 10 + sequence % 2));
            Assert.True(result.Accepted);
            engine.MarkEventsPersisted(engine.State.EventSequence);
        }

        var snapshot = engine.SnapshotFor(0);

        Assert.Equal(L12GameEngine.MaximumSnapshotEvents, engine.State.Events.Count);
        Assert.Empty(engine.UnpersistedEvents);
        Assert.Equal(L12GameEngine.MaximumSnapshotEvents, snapshot.RecentEvents.Length);
        Assert.Equal(engine.State.EventSequence - L12GameEngine.MaximumSnapshotEvents + 1,
            snapshot.RecentEvents[0].Sequence);
        Assert.Equal(engine.State.EventSequence, snapshot.RecentEvents[^1].Sequence);
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(-1L, false)]
    [InlineData(0L, true)]
    [InlineData(-1L, true)]
    public async Task 非正常命令序号在任何写入前被拒绝且不会覆盖初始检查点(
        long invalidSequence, bool terminal)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-initial-checkpoint-guard",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var engine = new L12GameEngine(Catalog, $"checkpoint-guard-{invalidSequence}-{terminal}",
            "SEQ000", 20260909, ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine);

        async Task<(string Hash, byte[] Blob, long CommandRows, long CheckpointRows)> ReadBoundaryAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var inspect = connection.CreateCommand();
            inspect.CommandText = """
                SELECT state_hash,state_blob,
                       (SELECT COUNT(*) FROM match_events WHERE match_id=$match),
                       (SELECT COUNT(*) FROM match_state_checkpoints WHERE match_id=$match)
                FROM match_state_checkpoints WHERE match_id=$match AND sequence=0;
                """;
            inspect.Parameters.AddWithValue("$match", engine.State.MatchId);
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), (byte[])reader[1], reader.GetInt64(2), reader.GetInt64(3));
        }

        var before = await ReadBoundaryAsync();
        string commandJson;
        CommandResult result;
        if (terminal)
        {
            engine.ConcludeByAuthority(0, "序号守卫终局");
            commandJson = """{"type":"authorityConclusion","winner":0,"reason":"序号守卫终局"}""";
            result = CommandResult.Ok();
        }
        else
        {
            var command = new L12GmCommand("setLife", 0, Value: 17);
            result = engine.HandleGm(command);
            Assert.True(result.Accepted);
            commandJson = JsonSerializer.Serialize(command);
        }

        var failure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            recorder.AppendAsync(engine, invalidSequence, -1, commandJson, result));
        Assert.Equal("sequence", failure.ParamName);
        var after = await ReadBoundaryAsync();
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(before.Blob, after.Blob);
        Assert.Equal(0, after.CommandRows);
        Assert.Equal(1, after.CheckpointRows);
    }

    [Fact]
    public async Task 新日志只追加小命令并可从检查点重放尾部到相同哈希()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-journal-v2", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var session = Guid.NewGuid();
        manager.Connect(session, "sandbox-owner", "沙盒");
        var created = await manager.CreateSandboxAsync(session, new L12SandboxRequest());
        var matchId = Payload(created.Single(message => message.SessionId == session
            && Payload(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state").GetProperty("matchId").GetString()!;
        string? finalHash = null;
        for (var sequence = 1; sequence <= 33; sequence++)
        {
            var messages = await manager.HandleGmActionAsync(session,
                JsonSerializer.SerializeToElement(new
                {
                    type = "setLife", targetPlayer = 0, value = 10 + sequence % 2,
                }), $"gm-{sequence}");
            finalHash = Payload(messages.Single(message => message.SessionId == session
                && Payload(message).GetProperty("type").GetString() == "gameState"))
                .GetProperty("state").GetProperty("stateHash").GetString();
        }

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                     { DataSource = path }.ToString()))
        {
            await connection.OpenAsync();
            var inspect = connection.CreateCommand();
            inspect.CommandText = """
                SELECT m.storage_version,
                       m.initial_state_json,
                       (SELECT COUNT(*) FROM match_events e WHERE e.match_id=m.match_id),
                       (SELECT COALESCE(SUM(length(state_json)),0) FROM match_events e WHERE e.match_id=m.match_id),
                       (SELECT COUNT(*) FROM match_state_checkpoints c WHERE c.match_id=m.match_id),
                       (SELECT COUNT(*) FROM match_action_events a WHERE a.match_id=m.match_id),
                       (SELECT COUNT(*) FROM match_state_checkpoints c
                        WHERE c.match_id=m.match_id AND c.sequence=0),
                       (SELECT state_encoding FROM match_state_checkpoints c
                        WHERE c.match_id=m.match_id AND c.sequence=0),
                       (SELECT length(state_blob) < uncompressed_bytes FROM match_state_checkpoints c
                        WHERE c.match_id=m.match_id AND c.sequence=0)
                FROM matches m WHERE m.match_id=$match;
                """;
            inspect.Parameters.AddWithValue("$match", matchId);
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(MatchRecorder.JournalStorageVersion, reader.GetInt32(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(33, reader.GetInt32(2));
            Assert.Equal(66, reader.GetInt64(3));
            Assert.Equal(2, reader.GetInt32(4));
            Assert.True(reader.GetInt32(5) >= 33);
            Assert.Equal(1, reader.GetInt32(6));
            Assert.Equal("json-br-v1", reader.GetString(7));
            Assert.Equal(1, reader.GetInt32(8));
        }

        var checkpoint = Assert.IsType<L12PersistedCheckpoint>(
            await recorder.LoadLatestCheckpointAsync(matchId));
        Assert.Equal(32, checkpoint.Sequence);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.StateJson,
            checkpoint.RandomState, checkpoint.CardFactSignalSequence,
            checkpoint.AutoPassEmptyResponses, checkpoint.ConcealHiddenResponseAvailability);
        Assert.Equal(checkpoint.StateHash, restored.ComputeStateHash());
        var tail = restored.HandleGm(new L12GmCommand("setLife", 0, Value: 11));
        Assert.True(tail.Accepted);
        Assert.Equal(finalHash, restored.ComputeStateHash());

        var replay = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        Assert.Equal(33, replay.Commands.Count);
        Assert.Equal(finalHash, replay.Commands[^1].StateHash);
        Assert.Equal(checkpoint.Revision + 1,
            replay.Commands[^1].State.GetProperty("Revision").GetInt64());
        var requests = await recorder.LoadProcessedActionRequestsAsync(matchId);
        Assert.Equal(33, requests.Count);
    }

    [Fact]
    public async Task RecorderWriteConnectionsBoundRetainedJournalSizeWithoutLeavingWalMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-journal-size-limit",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var engine = new L12GameEngine(Catalog, "journal-size-limit", "WAL001", 20260908,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine, "sandbox");
        var result = engine.HandleGm(new L12GmCommand("setLife", 0, Value: 19));
        Assert.True(result.Accepted);
        await recorder.AppendAsync(engine, 1, -1,
            JsonSerializer.Serialize(new L12GmCommand("setLife", 0, Value: 19)), result,
            "journal-limit-request");

        await using var connection = await recorder.OpenWriteConnectionAsync();
        var mode = connection.CreateCommand();
        mode.CommandText = "PRAGMA main.journal_mode;";
        Assert.Equal("wal", Convert.ToString(await mode.ExecuteScalarAsync()),
            ignoreCase: true);
        var limit = connection.CreateCommand();
        limit.CommandText = "PRAGMA main.journal_size_limit;";
        Assert.Equal(MatchRecorder.JournalSizeLimitBytes,
            Convert.ToInt64(await limit.ExecuteScalarAsync()));
        var stored = connection.CreateCommand();
        stored.CommandText = "SELECT state_json FROM match_events WHERE match_id=$match;";
        stored.Parameters.AddWithValue("$match", engine.State.MatchId);
        Assert.Equal("{}", Convert.ToString(await stored.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task V2CommandStateAnomalyIsReportedAndRejectedByApplicationAndSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-v2-state-anomaly",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var engine = new L12GameEngine(Catalog, "v2-state-anomaly", "ANOM02", 20260908,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine, "sandbox");

        var validation = Assert.Throws<InvalidDataException>(() =>
            MatchRecorder.ValidateJournalV2CommandStateJson("{\"unexpected\":true}"));
        Assert.Contains("必须保持", validation.Message, StringComparison.Ordinal);
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var invalid = connection.CreateCommand();
            invalid.CommandText = """
                INSERT INTO match_events(
                    match_id,sequence,received_utc,player_index,command_json,accepted,error,
                    revision,state_hash,state_json,request_id)
                VALUES($match,1,$utc,0,'{}',1,NULL,1,'invalid-hash',
                       '{"unexpected":true}','invalid-request');
                """;
            invalid.Parameters.AddWithValue("$match", engine.State.MatchId);
            invalid.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            var schemaError = await Assert.ThrowsAsync<SqliteException>(() =>
                invalid.ExecuteNonQueryAsync());
            Assert.Equal(19, schemaError.SqliteErrorCode);
            Assert.Contains("v2 match_events.state_json", schemaError.Message,
                StringComparison.Ordinal);
        }

        var result = engine.HandleGm(new L12GmCommand("setLife", 0, Value: 18));
        Assert.True(result.Accepted);
        await recorder.AppendAsync(engine, 1, -1,
            JsonSerializer.Serialize(new L12GmCommand("setLife", 0, Value: 18)), result,
            "valid-request");
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(engine.State.MatchId));
        Assert.Single(detail.Commands);
    }

    [Fact]
    public void 协商增量可重建完整视角且不会跨连接泄露隐藏信息并定期全量()
    {
        var playerCodec = new L12SnapshotWireCodec();
        var opponentCodec = new L12SnapshotWireCodec();
        playerCodec.SetDeltaEnabled(true);
        opponentCodec.SetDeltaEnabled(true);
        var playerBase = SnapshotBytes(1, 20, new { hand = new[] { "secret-card" } });
        var opponentBase = SnapshotBytes(1, 20, new { handCount = 1 });
        var firstPlayer = playerCodec.Prepare(new L12QueuedPayload(playerBase, true, false));
        var firstOpponent = opponentCodec.Prepare(new L12QueuedPayload(opponentBase, true, false));
        Assert.False(firstPlayer.IsDelta);
        Assert.False(firstOpponent.IsDelta);
        firstPlayer.Commit();
        firstOpponent.Commit();

        var playerNext = SnapshotBytes(2, 19, new { hand = new[] { "secret-card" } });
        var opponentNext = SnapshotBytes(2, 19, new { handCount = 1 });
        var playerDelta = playerCodec.Prepare(new L12QueuedPayload(playerNext, true, false));
        var opponentDelta = opponentCodec.Prepare(new L12QueuedPayload(opponentNext, true, false));
        Assert.True(playerDelta.IsDelta);
        Assert.True(opponentDelta.IsDelta);
        Assert.DoesNotContain("secret-card", Encoding.UTF8.GetString(opponentDelta.Payload),
            StringComparison.Ordinal);
        var rebuilt = L12SnapshotWireCodec.ApplyDelta(JsonNode.Parse(playerBase)!.AsObject(),
            playerDelta.Payload);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(playerNext), rebuilt));
        playerDelta.Commit();
        opponentDelta.Commit();

        L12PreparedWirePayload periodic = null!;
        for (var revision = 3; revision <= L12SnapshotWireCodec.FullSnapshotInterval + 1; revision++)
        {
            periodic = playerCodec.Prepare(new L12QueuedPayload(
                SnapshotBytes(revision, 19, new { hand = new[] { "secret-card" } }), true, false));
            periodic.Commit();
        }
        Assert.False(periodic.IsDelta);

        var forced = playerCodec.Prepare(new L12QueuedPayload(
            SnapshotBytes(100, 18, new { hand = new[] { "secret-card" } }), true, true));
        Assert.False(forced.IsDelta);

        var boundedCodec = new L12SnapshotWireCodec();
        boundedCodec.SetDeltaEnabled(true);
        var largeBase = SnapshotBytes(1, 20, new { values = Enumerable.Repeat(0, 5000).ToArray() });
        var largeFirst = boundedCodec.Prepare(new L12QueuedPayload(largeBase, true, false));
        largeFirst.Commit();
        var largeNext = SnapshotBytes(2, 20, new { values = Enumerable.Repeat(1, 5000).ToArray() });
        Assert.False(boundedCodec.Prepare(new L12QueuedPayload(largeNext, true, false)).IsDelta);
    }

    [Theory]
    [InlineData("after-match-command-write")]
    [InlineData("after-match-fact-write")]
    [InlineData("after-match-action-event-write")]
    [InlineData("after-match-checkpoint-write")]
    [InlineData("before-match-command-commit")]
    public async Task 事务各阶段失败会恢复最后确认检查点且同一请求可安全重试(string failureStage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-journal-rollback", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var session = Guid.NewGuid();
        manager.Connect(session, "sandbox-owner", "沙盒");
        var created = await manager.CreateSandboxAsync(session, new L12SandboxRequest());
        var initial = Payload(created.Single(message => message.SessionId == session
            && Payload(message).GetProperty("type").GetString() == "gameState")).GetProperty("state");
        var initialRevision = initial.GetProperty("revision").GetInt64();
        var initialHp = initial.GetProperty("players")[0].GetProperty("master").GetProperty("hp").GetInt32();
        var injected = false;
        recorder.StorageFailureInjector = stage =>
        {
            if (stage != failureStage || injected) return;
            injected = true;
            throw new IOException("测试注入提交失败");
        };

        var targetHp = failureStage == "after-match-checkpoint-write" ? 0 : initialHp + 7;
        var command = JsonSerializer.SerializeToElement(new
        {
            type = "setLife", targetPlayer = 0, value = targetHp,
        });
        var requestId = "retry-after-rollback";
        var failed = await manager.HandleGmActionAsync(session, command, requestId);
        Assert.Equal("actionRejected", Payload(Assert.Single(failed)).GetProperty("type").GetString());
        var recovered = Payload(manager.RecoveryState(session)
            .Single(message => message.SessionId == session
                && Payload(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state");
        Assert.Equal(initialRevision, recovered.GetProperty("revision").GetInt64());
        Assert.Equal(initialHp,
            recovered.GetProperty("players")[0].GetProperty("master").GetProperty("hp").GetInt32());

        recorder.StorageFailureInjector = null;
        var retried = await manager.HandleGmActionAsync(session, command, requestId);
        var committed = Payload(retried.Single(message => message.SessionId == session
            && Payload(message).GetProperty("type").GetString() == "gameState")).GetProperty("state");
        Assert.Equal(targetHp,
            committed.GetProperty("players")[0].GetProperty("master").GetProperty("hp").GetInt32());
        var matchId = committed.GetProperty("matchId").GetString()!;
        Assert.Single((await recorder.GetMatchAsync(matchId))!.Commands);
    }

    [Fact]
    public async Task 请求标识会进入命令日志并在进程重启后恢复去重基线()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-request-restart", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        const string requestId = "restart-safe-request";
        string expectedHash;
        await using (var recorder = new MatchRecorder(path))
        {
            await recorder.InitializeAsync();
            recorder.AttachCatalog(Catalog);
            var engine = new L12GameEngine(Catalog, "restart-match", "RESTART", 9182,
                ["甲", "乙"], [0, 1], skipPreparation: true,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: 2);
            await recorder.StartAsync(engine);
            var command = new L12GmCommand("setLife", 0, Value: 17);
            var result = engine.HandleGm(command);
            Assert.True(result.Accepted);
            await recorder.AppendAsync(engine, 1, -1, JsonSerializer.Serialize(command), result, requestId);
            expectedHash = engine.ComputeStateHash();
        }

        await using var restoredRecorder = new MatchRecorder(path);
        await restoredRecorder.InitializeAsync();
        restoredRecorder.AttachCatalog(Catalog);
        var recovery = Assert.IsType<L12JournalRecoveryState>(
            await restoredRecorder.LoadJournalEngineAsync("restart-match"));
        Assert.Equal(expectedHash, recovery.Engine.ComputeStateHash());
        Assert.False(recovery.Engine.AutoPassEmptyResponses);
        Assert.False(recovery.Engine.ConcealHiddenResponseAvailability);
        var persisted = Assert.Single(recovery.ProcessedRequests);
        Assert.Equal(requestId, persisted.RequestId);
        Assert.True(persisted.Accepted);
        Assert.Equal(1, recovery.CommandSequence);
    }

    [Fact]
    public async Task 旧v1排行记录在事实回填缺主宰时仍从完整状态恢复原字段()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-v1-ranking",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var engine = new L12GameEngine(Catalog, "v1-ranking", "V1RANK", 90210,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        await recorder.StartAsync(engine, "ranked", "account-a", "account-b");
        engine.ConcludeByAuthority(0, "旧记录结束");
        await recorder.AppendAuthorityAsync(engine, 1, "旧记录结束");
        await recorder.CompleteAsync(engine);
        var expectedMaster0 = engine.State.Players[0].MasterName;
        var expectedMaster1 = engine.State.Players[1].MasterName;

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var emulateLegacyBackfill = connection.CreateCommand();
            emulateLegacyBackfill.CommandText = """
                UPDATE match_participants SET master_name=NULL WHERE match_id='v1-ranking';
                UPDATE matches SET first_player=NULL WHERE match_id='v1-ranking';
                """;
            await emulateLegacyBackfill.ExecuteNonQueryAsync();
        }

        var ranking = Assert.Single(await recorder.ListRankingMatchesAsync());
        Assert.Equal(expectedMaster0, ranking.Master0);
        Assert.Equal(expectedMaster1, ranking.Master1);
        Assert.Equal(engine.State.FirstPlayer, ranking.FirstPlayer);
        await using var verify = new SqliteConnection($"Data Source={path}");
        await verify.OpenAsync();
        var initial = verify.CreateCommand();
        initial.CommandText = "SELECT initial_state_json FROM matches WHERE match_id='v1-ranking';";
        Assert.False(string.IsNullOrWhiteSpace((string?)await initial.ExecuteScalarAsync()));
    }

    [Fact]
    public void 检查点随机完整状态可跨混合调用恢复后续长序列()
    {
        var original = new L12DeterministicRandom(20260908);
        for (var index = 0; index < 257; index++)
        {
            _ = index % 3 switch
            {
                0 => original.Next(97),
                1 => original.Next(1, 7),
                _ => original.Next(),
            };
        }
        var restored = new L12DeterministicRandom(original.CaptureState());

        for (var index = 0; index < 2048; index++)
        {
            var expected = index % 3 switch
            {
                0 => original.Next(53),
                1 => original.Next(-17, 29),
                _ => original.Next(),
            };
            var actual = index % 3 switch
            {
                0 => restored.Next(53),
                1 => restored.Next(-17, 29),
                _ => restored.Next(),
            };
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task 检查点后的真实洗牌命令可用完整随机状态精确重放()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-random-tail", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        var engine = new L12GameEngine(Catalog, "random-tail", "RANDOM", 20260908,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine);

        for (var sequence = 1; sequence <= MatchRecorder.CheckpointInterval; sequence++)
        {
            var command = new L12GmCommand("setLife", 0, Value: 20 + sequence % 7);
            var result = engine.HandleGm(command);
            Assert.True(result.Accepted);
            await recorder.AppendAsync(engine, sequence, -1, JsonSerializer.Serialize(command), result,
                $"random-boundary-{sequence}");
        }
        var checkpoint = Assert.IsType<L12PersistedCheckpoint>(
            await recorder.LoadLatestCheckpointAsync(engine.State.MatchId));
        Assert.Equal(MatchRecorder.CheckpointInterval, checkpoint.Sequence);
        Assert.Equal(L12DeterministicRandom.StateVersion, checkpoint.RandomState.Version);

        var shuffle = new L12GmCommand("shuffleLibrary", 0);
        var shuffleResult = engine.HandleGm(shuffle);
        Assert.True(shuffleResult.Accepted);
        await recorder.AppendAsync(engine, MatchRecorder.CheckpointInterval + 1, -1,
            JsonSerializer.Serialize(shuffle), shuffleResult, "random-tail-shuffle");
        var expectedOrder = engine.State.Players[0].Library.Select(card => card.InstanceId).ToArray();
        var expectedHash = engine.ComputeStateHash();

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(engine.State.MatchId));
        Assert.Equal(expectedHash, recovered.Engine.ComputeStateHash());
        Assert.Equal(expectedOrder,
            recovered.Engine.State.Players[0].Library.Select(card => card.InstanceId));
    }

    [Fact]
    public async Task v2检查点缺少完整随机状态时拒绝不确定恢复()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-random-state-missing",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        var engine = new L12GameEngine(Catalog, "random-state-missing", "RANDOM", 31077,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var removeRandomState = connection.CreateCommand();
            removeRandomState.CommandText = """
                UPDATE match_state_checkpoints
                SET random_state_version=0,random_state_blob=NULL
                WHERE match_id='random-state-missing';
                """;
            Assert.Equal(1, await removeRandomState.ExecuteNonQueryAsync());
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => recorder.LoadJournalEngineAsync(engine.State.MatchId));
        Assert.Contains("随机状态", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 拒绝动作伴随权威自愈时仍持久化完整变更并可重放()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-rejection-heal", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        var engine = new L12GameEngine(Catalog, "healed-match", "HEALED", 8877,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        engine.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "orphan-prompt",
            PlayerIndex = 0,
            Kind = "fixture",
            Text = "孤立提示",
            ValidChoices = ["fixture"],
            Continuation = "pending-activation",
        });
        await recorder.StartAsync(engine);
        var beforeRevision = engine.State.Revision;
        var result = engine.Handle(0, new L12Command("unknown-command"));
        Assert.False(result.Accepted);
        Assert.Empty(engine.State.PendingPrompts);
        Assert.Equal(beforeRevision + 1, engine.State.Revision);
        await recorder.AppendAsync(engine, 1, 0,
            JsonSerializer.Serialize(new L12Command("unknown-command")), result,
            "rejected-with-heal", stateChangedOnRejection: true);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM match_action_events
                WHERE match_id='healed-match' AND command_sequence=1;
                """;
            Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) > 0);
        }
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync("healed-match"));
        var recorded = Assert.Single(detail.Commands);
        Assert.False(recorded.Accepted);
        Assert.Equal(engine.ComputeStateHash(), recorded.StateHash);
        Assert.Empty(recorded.State.GetProperty("PendingPrompts").EnumerateArray());
    }

    [Fact]
    public async Task 匿名化会覆盖v2命令事件与压缩检查点且管理员回放不读取空状态旁路()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-v2-anonymize", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        var engine = new L12GameEngine(Catalog, "anonymous-v2", "ANONV2", 7722,
            ["隐私玩家", "保留玩家"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(engine, "ranked", "privacy-account", "keep-account");
        Assert.Equal(1, await recorder.AnonymizeAccountAsync(
            "privacy-account", "隐私玩家", "已删除账号"));
        // 模拟匿名化事务完成后已经在途的房间动作迟到落盘；不能重新写回旧身份。
        engine.ConcludeByAuthority(1, "隐私玩家请求终止");
        await recorder.AppendAuthorityAsync(engine, 1, "隐私玩家请求终止");
        await recorder.CompleteAsync(engine);
        Assert.Equal(0, recorder.FactLocationBaselineCount);

        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync("anonymous-v2"));
        Assert.Equal("已删除账号", detail.Match.Player0);
        var finalState = Assert.Single(detail.Commands).State;
        Assert.Equal("已删除账号",
            finalState.GetProperty("Players")[0].GetProperty("Name").GetString());

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var inspect = connection.CreateCommand();
        inspect.CommandText = """
            SELECT m.identity_scrubbed,
                   (SELECT group_concat(json_extract(event_json,'$.Text'),'')
                    FROM match_action_events WHERE match_id=m.match_id),
                   (SELECT state_json FROM match_events WHERE match_id=m.match_id LIMIT 1),
                   (SELECT command_json FROM match_events WHERE match_id=m.match_id LIMIT 1)
            FROM matches m WHERE m.match_id='anonymous-v2';
            """;
        await using var reader = await inspect.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.DoesNotContain("隐私玩家", reader.GetString(1), StringComparison.Ordinal);
        Assert.Contains("已删除账号", reader.GetString(1), StringComparison.Ordinal);
        Assert.Equal("{}", reader.GetString(2));
        using var commandDocument = JsonDocument.Parse(reader.GetString(3));
        Assert.Equal("已删除账号请求终止",
            commandDocument.RootElement.GetProperty("reason").GetString());
        var checkpoint = Assert.IsType<L12PersistedCheckpoint>(
            await recorder.LoadLatestCheckpointAsync("anonymous-v2"));
        using var checkpointDocument = JsonDocument.Parse(checkpoint.StateJson);
        Assert.Equal("已删除账号", checkpointDocument.RootElement.GetProperty("Players")[0]
            .GetProperty("Name").GetString());
        Assert.Equal("已删除账号请求终止",
            checkpointDocument.RootElement.GetProperty("WinnerReason").GetString());
    }

    private static byte[] SnapshotBytes(long revision, int hp, object privateView)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "gameState",
            state = new
            {
                matchId = "delta-match", revision, hp, privateView,
                stable = new string('稳', 2000),
            },
        }, WebJson);

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WebJson);
}
