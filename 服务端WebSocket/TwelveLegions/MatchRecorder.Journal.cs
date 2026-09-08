using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal sealed record L12PersistedCheckpoint(
    long Sequence,
    long Revision,
    string StateHash,
    string StateJson,
    long RandomDrawCount,
    L12RandomState RandomState,
    long CardFactSignalSequence,
    bool AutoPassEmptyResponses,
    bool ConcealHiddenResponseAvailability);

internal sealed record L12PersistedActionRequest(
    int PlayerIndex, string RequestId, bool Accepted, string? Error, long Revision);
internal sealed record L12JournalRecoveryState(
    L12GameEngine Engine, long CommandSequence,
    IReadOnlyList<L12PersistedActionRequest> ProcessedRequests);

public sealed partial class MatchRecorder
{
    internal const int JournalStorageVersion = 2;
    internal const int CheckpointInterval = 32;
    private readonly object _journalCatalogGate = new();
    private L12Catalog? _journalCatalog;
    private bool _journalCatalogExplicitlyAttached;

    internal void AttachCatalog(L12Catalog catalog)
    {
        lock (_journalCatalogGate)
        {
            if (_journalCatalogExplicitlyAttached && !ReferenceEquals(_journalCatalog, catalog))
                throw new InvalidOperationException("对局记录器不能在运行中切换卡牌目录");
            _journalCatalog = catalog;
            _journalCatalogExplicitlyAttached = true;
        }
    }

    private L12Catalog ResolveJournalCatalog()
    {
        lock (_journalCatalogGate)
        {
            if (_journalCatalog is not null) return _journalCatalog;
            var bundledData = Path.Combine(AppContext.BaseDirectory, "Data");
            if (!Directory.Exists(bundledData))
                throw new InvalidOperationException("读取 v2 回放前必须绑定权威卡牌目录");
            return _journalCatalog = L12Catalog.Load(bundledData);
        }
    }

    internal void RegisterRecoveredEngine(L12GameEngine engine)
    {
        if (!UsesJournalV2(engine)) return;
        _factLocationBaselines[engine.State.MatchId] = CaptureLocations(engine.State);
        engine.MarkEventsPersisted(engine.State.EventSequence);
        engine.MarkCardFactsPersisted(engine.CardFactSignalSequence);
    }

    private static async Task InitializeJournalSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS match_state_checkpoints (
                match_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                revision INTEGER NOT NULL,
                state_hash TEXT NOT NULL,
                state_encoding TEXT NOT NULL,
                state_blob BLOB NOT NULL,
                uncompressed_bytes INTEGER NOT NULL,
                random_draw_count INTEGER NOT NULL,
                random_state_version INTEGER NOT NULL DEFAULT 0,
                random_state_blob BLOB,
                card_fact_signal_sequence INTEGER NOT NULL,
                auto_pass_empty_responses INTEGER NOT NULL,
                conceal_hidden_response_availability INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(match_id, sequence),
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_match_state_checkpoints_latest
                ON match_state_checkpoints(match_id, sequence DESC);
            CREATE TABLE IF NOT EXISTS match_action_events (
                match_id TEXT NOT NULL,
                event_sequence INTEGER NOT NULL,
                command_sequence INTEGER NOT NULL,
                revision INTEGER NOT NULL,
                event_json TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(match_id, event_sequence),
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_match_action_events_command
                ON match_action_events(match_id, command_sequence);
            CREATE TABLE IF NOT EXISTS match_action_requests (
                match_id TEXT NOT NULL,
                player_index INTEGER NOT NULL,
                request_id TEXT NOT NULL,
                command_sequence INTEGER NOT NULL,
                accepted INTEGER NOT NULL,
                error TEXT,
                revision INTEGER NOT NULL,
                state_hash TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(match_id, player_index, request_id),
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_match_action_requests_recent
                ON match_action_requests(match_id, command_sequence DESC);
            CREATE TRIGGER IF NOT EXISTS trg_match_events_v2_sparse_state
            BEFORE INSERT ON match_events
            WHEN NEW.state_json <> '{}'
              AND COALESCE((SELECT storage_version FROM matches WHERE match_id=NEW.match_id),1) >= 2
            BEGIN
                SELECT RAISE(ABORT, 'v2 match_events.state_json must remain {}');
            END;
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "match_state_checkpoints", "random_state_version",
            "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "match_state_checkpoints", "random_state_blob", "BLOB");
    }

    private static bool UsesJournalV2(L12GameEngine? engine)
        => engine?.State.StateFormatVersion >= JournalStorageVersion;

    internal static void ValidateJournalV2CommandStateJson(string stateJson)
    {
        if (string.Equals(stateJson, "{}", StringComparison.Ordinal)) return;
        L12PerformanceMetrics.Value("persistence.v2-state-json-anomaly", 1);
        Console.Error.WriteLine("[L12存储异常] v2 普通命令 state_json 非空对象，已拒绝持久化。");
        throw new InvalidDataException("v2 普通命令 state_json 必须保持为 '{}'");
    }

    private static async Task PersistActionEventsAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12GameEngine engine, long commandSequence,
        string occurredUtc, IReadOnlyList<(string Value, string Replacement)> identityReplacements)
    {
        foreach (var actionEvent in engine.UnpersistedEvents)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO match_action_events(
                    match_id,event_sequence,command_sequence,revision,event_json,created_utc)
                VALUES($match,$event,$command,$revision,$json,$utc);
                """;
            insert.Parameters.AddWithValue("$match", engine.State.MatchId);
            insert.Parameters.AddWithValue("$event", actionEvent.Sequence);
            insert.Parameters.AddWithValue("$command", commandSequence);
            insert.Parameters.AddWithValue("$revision", engine.State.Revision);
            insert.Parameters.AddWithValue("$json", ApplyIdentityReplacements(
                JsonSerializer.Serialize(new
                {
                    actionEvent.Sequence,
                    actionEvent.Type,
                    actionEvent.PlayerIndex,
                    actionEvent.Text,
                    Cards = actionEvent.Cards.Select(card => new
                    {
                        card.CardId,
                        card.InstanceId,
                    }).ToArray(),
                    actionEvent.EffectText,
                }), identityReplacements));
            insert.Parameters.AddWithValue("$utc", occurredUtc);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static async Task PersistCheckpointAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12GameEngine engine, long commandSequence,
        string stateHash, string stateJson, string occurredUtc)
    {
        var raw = Encoding.UTF8.GetBytes(stateJson);
        var randomState = engine.RandomState
            ?? throw new InvalidOperationException("v2 检查点缺少可持久化随机状态");
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
                await brotli.WriteAsync(raw);
            compressed = output.ToArray();
        }
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO match_state_checkpoints(
                match_id,sequence,revision,state_hash,state_encoding,state_blob,uncompressed_bytes,
                random_draw_count,card_fact_signal_sequence,auto_pass_empty_responses,
                conceal_hidden_response_availability,created_utc,random_state_version,random_state_blob)
            VALUES($match,$sequence,$revision,$hash,'json-br-v1',$state,$bytes,$random,$facts,$autoPass,$conceal,$utc,
                   $randomStateVersion,$randomState)
            ON CONFLICT(match_id,sequence) DO UPDATE SET
                revision=excluded.revision,state_hash=excluded.state_hash,
                state_encoding=excluded.state_encoding,state_blob=excluded.state_blob,
                uncompressed_bytes=excluded.uncompressed_bytes,
                random_draw_count=excluded.random_draw_count,
                card_fact_signal_sequence=excluded.card_fact_signal_sequence,
                auto_pass_empty_responses=excluded.auto_pass_empty_responses,
                conceal_hidden_response_availability=excluded.conceal_hidden_response_availability,
                created_utc=excluded.created_utc,
                random_state_version=excluded.random_state_version,
                random_state_blob=excluded.random_state_blob;
            """;
        insert.Parameters.AddWithValue("$match", engine.State.MatchId);
        insert.Parameters.AddWithValue("$sequence", commandSequence);
        insert.Parameters.AddWithValue("$revision", engine.State.Revision);
        insert.Parameters.AddWithValue("$hash", stateHash);
        insert.Parameters.AddWithValue("$state", compressed);
        insert.Parameters.AddWithValue("$bytes", raw.Length);
        insert.Parameters.AddWithValue("$random", engine.RandomDrawCount);
        insert.Parameters.AddWithValue("$randomStateVersion", randomState.Version);
        insert.Parameters.AddWithValue("$randomState", L12DeterministicRandom.EncodeState(randomState));
        insert.Parameters.AddWithValue("$facts", engine.CardFactSignalSequence);
        insert.Parameters.AddWithValue("$autoPass", engine.AutoPassEmptyResponses ? 1 : 0);
        insert.Parameters.AddWithValue("$conceal", engine.ConcealHiddenResponseAvailability ? 1 : 0);
        insert.Parameters.AddWithValue("$utc", occurredUtc);
        await insert.ExecuteNonQueryAsync();
    }

    internal async Task<L12PersistedCheckpoint?> LoadLatestCheckpointAsync(string matchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence,revision,state_hash,state_encoding,state_blob,uncompressed_bytes,
                   random_draw_count,card_fact_signal_sequence,auto_pass_empty_responses,
                   conceal_hidden_response_availability,random_state_version,random_state_blob
            FROM match_state_checkpoints WHERE match_id=$match
            ORDER BY sequence DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!string.Equals(reader.GetString(3), "json-br-v1", StringComparison.Ordinal))
            throw new InvalidDataException("对局检查点编码版本不受支持");
        var expectedBytes = reader.GetInt32(5);
        await using var input = new MemoryStream((byte[])reader[4], writable: false);
        await using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedBytes);
        await brotli.CopyToAsync(output, cancellationToken);
        if (output.Length != expectedBytes) throw new InvalidDataException("对局检查点长度校验失败");
        return new L12PersistedCheckpoint(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
            Encoding.UTF8.GetString(output.ToArray()), reader.GetInt64(6), ReadRandomState(reader),
            reader.GetInt64(7),
            reader.GetInt32(8) == 1, reader.GetInt32(9) == 1);
    }

    internal async Task<IReadOnlyList<L12PersistedActionRequest>> LoadProcessedActionRequestsAsync(
        string matchId, int limit = 256, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT player_index,request_id,accepted,error,revision
            FROM match_action_requests
            WHERE match_id=$match
            ORDER BY command_sequence DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1024));
        var result = new List<L12PersistedActionRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new L12PersistedActionRequest(reader.GetInt32(0), reader.GetString(1),
                reader.GetInt32(2) == 1, reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt64(4)));
        result.Reverse();
        return result;
    }

    private async Task<IReadOnlyList<L12RecordedCommand>> ReconstructJournalCommandsAsync(
        SqliteConnection connection, string matchId, long lastSequence = long.MaxValue,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        var catalog = ResolveJournalCatalog();
        // 账号匿名化会按隐私要求改写历史快照中的显示名。旧 hash 仍作为脱敏前的
        // 审计锚点保留，但不能再用来验证已明确标记过的脱敏副本。
        var verifyStateHashes = !await IsIdentityScrubbedAsync(connection, matchId, cancellationToken);
        var checkpointCommand = connection.CreateCommand();
        checkpointCommand.CommandText = """
            SELECT sequence,revision,state_hash,state_encoding,state_blob,uncompressed_bytes,
                   random_draw_count,card_fact_signal_sequence,auto_pass_empty_responses,
                   conceal_hidden_response_availability,random_state_version,random_state_blob
            FROM match_state_checkpoints
            WHERE match_id=$match AND sequence<=$after
            ORDER BY sequence DESC LIMIT 1;
            """;
        checkpointCommand.Parameters.AddWithValue("$match", matchId);
        checkpointCommand.Parameters.AddWithValue("$after", Math.Max(0, afterSequence));
        L12PersistedCheckpoint initial;
        await using (var reader = await checkpointCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("v2 对局缺少可用检查点");
            initial = await ReadCheckpointAsync(reader, cancellationToken);
        }
        var engine = L12GameEngine.RestoreCheckpoint(catalog, initial.StateJson,
            initial.RandomState, initial.CardFactSignalSequence,
            initial.AutoPassEmptyResponses, initial.ConcealHiddenResponseAvailability);
        if (engine.State.Revision != initial.Revision
            || (verifyStateHashes
                && !string.Equals(engine.ComputeStateHash(), initial.StateHash, StringComparison.Ordinal)))
            throw new InvalidDataException("v2 初始检查点哈希不一致");

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence,received_utc,player_index,command_json,accepted,error,revision,state_hash
            FROM match_events
            WHERE match_id=$match AND sequence>$checkpoint AND sequence<=$last
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$checkpoint", initial.Sequence);
        command.Parameters.AddWithValue("$last", lastSequence);
        var result = new List<L12RecordedCommand>();
        var expectedSequence = initial.Sequence;
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequence = rows.GetInt64(0);
            if (sequence != ++expectedSequence) throw new InvalidDataException("v2 命令序号不连续");
            var commandJson = rows.GetString(3);
            using var commandDocument = JsonDocument.Parse(commandJson);
            var commandRoot = commandDocument.RootElement;
            var type = commandRoot.TryGetProperty("type", out var lowerType)
                ? lowerType.GetString()
                : commandRoot.TryGetProperty("Type", out var upperType) ? upperType.GetString() : null;
            if (string.IsNullOrWhiteSpace(type)) throw new InvalidDataException("v2 命令缺少类型");
            var outcome = ReplayJournalCommand(engine, rows.GetInt32(2), type, commandRoot);
            var accepted = rows.GetInt32(4) == 1;
            var revision = rows.GetInt64(6);
            var stateHash = rows.GetString(7);
            if (outcome.Accepted != accepted || engine.State.Revision != revision
                || (verifyStateHashes
                    && !string.Equals(engine.ComputeStateHash(), stateHash, StringComparison.Ordinal)))
                throw new InvalidDataException($"v2 命令重放校验失败：{sequence}");
            result.Add(new L12RecordedCommand(sequence, rows.GetString(1), rows.GetInt32(2),
                commandRoot.Clone(), accepted, rows.IsDBNull(5) ? null : rows.GetString(5),
                revision, stateHash, JsonSerializer.SerializeToElement(engine.State)));
        }
        return result;
    }

    internal async Task<L12JournalRecoveryState?> LoadJournalEngineAsync(string matchId,
        CancellationToken cancellationToken = default)
    {
        var catalog = ResolveJournalCatalog();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (await ReadStorageVersionAsync(connection, matchId, cancellationToken) < JournalStorageVersion)
            return null;
        var verifyStateHashes = !await IsIdentityScrubbedAsync(connection, matchId, cancellationToken);
        var checkpoint = await LoadLatestCheckpointAsync(matchId, cancellationToken)
            ?? throw new InvalidDataException("v2 对局缺少状态检查点");
        var engine = L12GameEngine.RestoreCheckpoint(catalog, checkpoint.StateJson,
            checkpoint.RandomState, checkpoint.CardFactSignalSequence,
            checkpoint.AutoPassEmptyResponses, checkpoint.ConcealHiddenResponseAvailability);
        if (engine.State.Revision != checkpoint.Revision
            || (verifyStateHashes
                && !string.Equals(engine.ComputeStateHash(), checkpoint.StateHash, StringComparison.Ordinal)))
            throw new InvalidDataException("v2 恢复检查点哈希不一致");
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence,player_index,command_json,accepted,revision,state_hash
            FROM match_events WHERE match_id=$match AND sequence>$after ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$after", checkpoint.Sequence);
        var expectedSequence = checkpoint.Sequence;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sequence = reader.GetInt64(0);
            if (sequence != ++expectedSequence) throw new InvalidDataException("v2 恢复尾部命令序号不连续");
            using var document = JsonDocument.Parse(reader.GetString(2));
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var lowerType) ? lowerType.GetString()
                : root.TryGetProperty("Type", out var upperType) ? upperType.GetString() : null;
            if (string.IsNullOrWhiteSpace(type)) throw new InvalidDataException("v2 恢复命令缺少类型");
            var outcome = ReplayJournalCommand(engine, reader.GetInt32(1), type, root);
            if (outcome.Accepted != (reader.GetInt32(3) == 1)
                || engine.State.Revision != reader.GetInt64(4)
                || (verifyStateHashes
                    && !string.Equals(engine.ComputeStateHash(), reader.GetString(5), StringComparison.Ordinal)))
                throw new InvalidDataException($"v2 恢复尾部校验失败：{sequence}");
        }
        RegisterRecoveredEngine(engine);
        return new L12JournalRecoveryState(engine, expectedSequence,
            await LoadProcessedActionRequestsAsync(matchId, cancellationToken: cancellationToken));
    }

    private static CommandResult ReplayJournalCommand(L12GameEngine engine, int playerIndex,
        string type, JsonElement commandRoot)
    {
        if (string.Equals(type, "authorityConclusion", StringComparison.OrdinalIgnoreCase))
        {
            var winner = commandRoot.TryGetProperty("winner", out var winnerElement)
                && winnerElement.ValueKind == JsonValueKind.Number ? winnerElement.GetInt32() : (int?)null;
            var reason = commandRoot.TryGetProperty("reason", out var reasonElement)
                ? reasonElement.GetString() ?? "服务端权威裁决" : "服务端权威裁决";
            var agreedDraw = commandRoot.TryGetProperty("agreedDraw", out var drawElement)
                && drawElement.ValueKind == JsonValueKind.True;
            if (agreedDraw) engine.ConcludeAgreedDrawByAuthority(reason);
            else engine.ConcludeByAuthority(winner, reason);
            return CommandResult.Ok();
        }
        if (playerIndex == -1)
        {
            var gm = commandRoot.Deserialize<L12GmCommand>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException("v2 GM 命令载荷为空");
            return engine.HandleGm(gm);
        }
        var gameCommand = commandRoot.Deserialize<L12Command>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("v2 对局命令载荷为空");
        return engine.Handle(playerIndex, gameCommand);
    }

    private static async Task<int> ReadStorageVersionAsync(SqliteConnection connection,
        string matchId, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT storage_version FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? 1 : Convert.ToInt32(value);
    }

    private static async Task<bool> IsIdentityScrubbedAsync(SqliteConnection connection,
        string matchId, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT identity_scrubbed FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    private static async Task ScrubJournalIdentityAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId, string playerName, string anonymousName)
    {
        var events = new List<(long Sequence, string Json)>();
        var selectEvents = connection.CreateCommand();
        selectEvents.Transaction = transaction;
        selectEvents.CommandText = "SELECT event_sequence,event_json FROM match_action_events WHERE match_id=$match;";
        selectEvents.Parameters.AddWithValue("$match", matchId);
        await using (var reader = await selectEvents.ExecuteReaderAsync())
            while (await reader.ReadAsync()) events.Add((reader.GetInt64(0), reader.GetString(1)));
        foreach (var actionEvent in events)
        {
            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE match_action_events SET event_json=$json
                WHERE match_id=$match AND event_sequence=$sequence;
                """;
            update.Parameters.AddWithValue("$json",
                ScrubJsonString(actionEvent.Json, playerName, anonymousName));
            update.Parameters.AddWithValue("$match", matchId);
            update.Parameters.AddWithValue("$sequence", actionEvent.Sequence);
            await update.ExecuteNonQueryAsync();
        }

        var checkpoints = new List<(long Sequence, byte[] Blob, int Bytes)>();
        var selectCheckpoints = connection.CreateCommand();
        selectCheckpoints.Transaction = transaction;
        selectCheckpoints.CommandText = """
            SELECT sequence,state_blob,uncompressed_bytes,state_encoding
            FROM match_state_checkpoints WHERE match_id=$match;
            """;
        selectCheckpoints.Parameters.AddWithValue("$match", matchId);
        await using (var reader = await selectCheckpoints.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (!string.Equals(reader.GetString(3), "json-br-v1", StringComparison.Ordinal))
                    throw new InvalidDataException("匿名化遇到不支持的检查点编码");
                checkpoints.Add((reader.GetInt64(0), (byte[])reader[1], reader.GetInt32(2)));
            }
        }
        foreach (var checkpoint in checkpoints)
        {
            var json = await DecompressStateAsync(checkpoint.Blob, checkpoint.Bytes);
            var scrubbed = ScrubJsonString(json, playerName, anonymousName);
            var raw = Encoding.UTF8.GetBytes(scrubbed);
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    await brotli.WriteAsync(raw);
                compressed = output.ToArray();
            }
            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE match_state_checkpoints SET state_blob=$state,uncompressed_bytes=$bytes
                WHERE match_id=$match AND sequence=$sequence;
                """;
            update.Parameters.AddWithValue("$state", compressed);
            update.Parameters.AddWithValue("$bytes", raw.Length);
            update.Parameters.AddWithValue("$match", matchId);
            update.Parameters.AddWithValue("$sequence", checkpoint.Sequence);
            await update.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> DecompressStateAsync(byte[] blob, int expectedBytes,
        CancellationToken cancellationToken = default)
    {
        await using var input = new MemoryStream(blob, writable: false);
        await using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedBytes);
        await brotli.CopyToAsync(output, cancellationToken);
        if (output.Length != expectedBytes) throw new InvalidDataException("对局检查点长度校验失败");
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task<L12PersistedCheckpoint> ReadCheckpointAsync(SqliteDataReader reader,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(reader.GetString(3), "json-br-v1", StringComparison.Ordinal))
            throw new InvalidDataException("对局检查点编码版本不受支持");
        var expectedBytes = reader.GetInt32(5);
        await using var input = new MemoryStream((byte[])reader[4], writable: false);
        await using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedBytes);
        await brotli.CopyToAsync(output, cancellationToken);
        if (output.Length != expectedBytes) throw new InvalidDataException("对局检查点长度校验失败");
        return new L12PersistedCheckpoint(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
            Encoding.UTF8.GetString(output.ToArray()), reader.GetInt64(6), ReadRandomState(reader),
            reader.GetInt64(7),
            reader.GetInt32(8) == 1, reader.GetInt32(9) == 1);
    }

    private static L12RandomState ReadRandomState(SqliteDataReader reader)
    {
        var version = reader.GetInt32(10);
        if (version == 0)
            throw new InvalidDataException("v2 检查点缺少完整随机状态，已拒绝不确定恢复");
        if (reader.IsDBNull(11)) throw new InvalidDataException("随机状态载荷缺失");
        return L12DeterministicRandom.DecodeState(version, (byte[])reader[11], reader.GetInt64(6));
    }
}
