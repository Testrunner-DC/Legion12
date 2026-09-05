using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed record L12RankedRuntimeCheckpoint(
    int Version, string MatchId, string RoomCode, string Status, long CheckpointGeneration,
    long CommandSequence, long StateRevision, string StateHash,
    DateTimeOffset StartedAt, int MeaningfulCommandCount,
    long[] TotalRemainingMs, long[] OperationRemainingMs, bool[] Acting,
    DateTimeOffset LastSettledAt, string? ConclusionKind, bool AuthorityEventRecorded,
    bool[] Connected, DateTimeOffset?[] DisconnectedAt,
    string[] IntegrityClientKeys, long[] ConnectionGenerations, DateTimeOffset UpdatedAt);

internal sealed record L12RankedSettlementEnvelope(
    int Version, string MatchId, string FirstAccountId, string SecondAccountId,
    string FirstMasterId, string SecondMasterId, int? Winner,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, int MeaningfulCommandCount,
    string ConclusionKind, string FirstNetworkFingerprint, string SecondNetworkFingerprint);

internal sealed record L12RankedSettlementOutboxEntry(
    string MatchId, L12RankedSettlementEnvelope? Payload, string PayloadHash, string Status, int Attempts,
    string? LoadError);

internal sealed record L12RankedRecoverySource(
    string MatchId, string RoomCode, int Seed, string[] PlayerNames, string[] AccountIds,
    string InitialStateJson, string StartedUtc, L12PresetDeckDefinition[] Decks,
    IReadOnlyList<L12RecordedCommand> Commands, L12RankedRuntimeCheckpoint? Runtime,
    string? LoadError);

public sealed record L12RankedRecoverySummary(
    int SettlementsApplied, int Restored, int Invalidated, int Failed);

public sealed partial class MatchRecorder
{
    private static readonly JsonSerializerOptions RankedPersistenceJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task InitializeRankedPersistenceSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ranked_match_runtime (
                match_id TEXT PRIMARY KEY,
                room_code TEXT NOT NULL,
                status TEXT NOT NULL,
                checkpoint_json TEXT NOT NULL,
                checkpoint_hash TEXT NOT NULL,
                checkpoint_generation INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_ranked_match_runtime_status
                ON ranked_match_runtime(status, updated_utc);
            CREATE TABLE IF NOT EXISTS ranked_settlement_outbox (
                match_id TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                created_utc TEXT NOT NULL,
                applied_utc TEXT,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_ranked_settlement_outbox_pending
                ON ranked_settlement_outbox(status, created_utc);
            CREATE TABLE IF NOT EXISTS ranked_recovery_quarantine (
                match_id TEXT PRIMARY KEY,
                reason TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "ranked_match_runtime", "checkpoint_generation",
            "INTEGER NOT NULL DEFAULT 0");
    }

    private static void ValidateRuntime(L12RankedRuntimeCheckpoint runtime)
    {
        if (runtime.Version != 1 || string.IsNullOrWhiteSpace(runtime.MatchId)
            || string.IsNullOrWhiteSpace(runtime.RoomCode)
            || runtime.Status is not ("active" or "completed")
            || runtime.CheckpointGeneration <= 0
            || runtime.TotalRemainingMs is not { Length: 2 }
            || runtime.OperationRemainingMs is not { Length: 2 }
            || runtime.Acting is not { Length: 2 } || runtime.Connected is not { Length: 2 }
            || runtime.DisconnectedAt is not { Length: 2 }
            || runtime.IntegrityClientKeys is not { Length: 2 }
            || runtime.ConnectionGenerations is not { Length: 2 }
            || runtime.IntegrityClientKeys.Any(value => value is null))
            throw new InvalidDataException("排位运行快照结构无效");
        if (runtime.TotalRemainingMs.Any(value => value < 0)
            || runtime.OperationRemainingMs.Any(value => value < 0))
            throw new InvalidDataException("排位计时快照不能为负数");
    }

    private static void ValidateSettlement(L12RankedSettlementEnvelope payload)
    {
        if (payload.Version != 1 || string.IsNullOrWhiteSpace(payload.MatchId)
            || string.IsNullOrWhiteSpace(payload.FirstAccountId)
            || string.IsNullOrWhiteSpace(payload.SecondAccountId)
            || string.Equals(payload.FirstAccountId, payload.SecondAccountId,
                StringComparison.OrdinalIgnoreCase)
            || payload.Winner is not null and not (0 or 1)
            || payload.EndedAt < payload.StartedAt || payload.MeaningfulCommandCount < 0
            || string.IsNullOrWhiteSpace(payload.ConclusionKind)
            || payload.FirstMasterId is null || payload.SecondMasterId is null
            || payload.FirstNetworkFingerprint is null || payload.SecondNetworkFingerprint is null)
            throw new InvalidDataException("排位结算 outbox 载荷结构无效");
    }

    private static async Task InsertRankedRuntimeAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12RankedRuntimeCheckpoint runtime)
    {
        ValidateRuntime(runtime);
        var json = JsonSerializer.Serialize(runtime, RankedPersistenceJson);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ranked_match_runtime(
                match_id,room_code,status,checkpoint_json,checkpoint_hash,checkpoint_generation,updated_utc)
            VALUES($match,$room,$status,$json,$hash,$generation,$updated);
            """;
        command.Parameters.AddWithValue("$match", runtime.MatchId);
        command.Parameters.AddWithValue("$room", runtime.RoomCode);
        command.Parameters.AddWithValue("$status", runtime.Status);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$hash", PersistenceHash(json));
        command.Parameters.AddWithValue("$generation", runtime.CheckpointGeneration);
        command.Parameters.AddWithValue("$updated", runtime.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertRankedRuntimeAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12RankedRuntimeCheckpoint runtime)
    {
        ValidateRuntime(runtime);
        var json = JsonSerializer.Serialize(runtime, RankedPersistenceJson);
        var hash = PersistenceHash(json);
        var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT room_code,status,checkpoint_generation,checkpoint_hash
            FROM ranked_match_runtime WHERE match_id=$match;
            """;
        existing.Parameters.AddWithValue("$match", runtime.MatchId);
        await using (var reader = await existing.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                if (!string.Equals(reader.GetString(0), runtime.RoomCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("排位运行快照房间身份冲突");
                var currentGeneration = reader.GetInt64(2);
                if (currentGeneration > runtime.CheckpointGeneration) return;
                if (currentGeneration == runtime.CheckpointGeneration)
                {
                    if (string.Equals(reader.GetString(1), runtime.Status, StringComparison.Ordinal)
                        && string.Equals(reader.GetString(3), hash, StringComparison.Ordinal)) return;
                    throw new InvalidOperationException("相同 generation 的排位运行快照载荷冲突");
                }
                if (reader.GetString(1) == "completed" && runtime.Status != "completed")
                    throw new InvalidOperationException("已完成排位不能回退为活跃状态");
            }
        }
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ranked_match_runtime(
                match_id,room_code,status,checkpoint_json,checkpoint_hash,checkpoint_generation,updated_utc)
            VALUES($match,$room,$status,$json,$hash,$generation,$updated)
            ON CONFLICT(match_id) DO UPDATE SET
                status=excluded.status,
                checkpoint_json=excluded.checkpoint_json,
                checkpoint_hash=excluded.checkpoint_hash,
                checkpoint_generation=excluded.checkpoint_generation,
                updated_utc=excluded.updated_utc
            WHERE excluded.checkpoint_generation > ranked_match_runtime.checkpoint_generation;
            """;
        command.Parameters.AddWithValue("$match", runtime.MatchId);
        command.Parameters.AddWithValue("$room", runtime.RoomCode);
        command.Parameters.AddWithValue("$status", runtime.Status);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$generation", runtime.CheckpointGeneration);
        command.Parameters.AddWithValue("$updated", runtime.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();

        var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = """
            SELECT checkpoint_generation,checkpoint_hash,status
            FROM ranked_match_runtime WHERE match_id=$match;
            """;
        verify.Parameters.AddWithValue("$match", runtime.MatchId);
        await using var verification = await verify.ExecuteReaderAsync();
        if (!await verification.ReadAsync())
            throw new InvalidOperationException("排位运行快照写入后丢失");
        var generation = verification.GetInt64(0);
        if (generation < runtime.CheckpointGeneration)
            throw new InvalidOperationException("排位运行快照单调写入失败");
        if (generation == runtime.CheckpointGeneration
            && (!string.Equals(verification.GetString(1), hash, StringComparison.Ordinal)
                || !string.Equals(verification.GetString(2), runtime.Status, StringComparison.Ordinal)))
            throw new InvalidOperationException("相同 generation 的排位运行快照载荷冲突");
    }

    internal async Task PersistRankedRuntimeBatchAsync(IReadOnlyList<L12RankedRuntimeCheckpoint> checkpoints)
    {
        if (checkpoints.Count == 0) return;
        var unique = checkpoints.GroupBy(item => item.MatchId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).ToArray();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        foreach (var checkpoint in unique)
            await UpsertRankedRuntimeAsync(connection, transaction, checkpoint);
        StorageFailureInjector?.Invoke("before-ranked-runtime-batch-commit");
        await transaction.CommitAsync();
    }

    private async Task CompleteRankedMatchAndEnqueueAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12GameEngine engine, L12RankedSettlementEnvelope settlement)
    {
        if (engine.State.Phase != L12Phase.GameOver || settlement.MatchId != engine.State.MatchId
            || settlement.Winner != engine.State.Winner)
            throw new InvalidOperationException("排位最终事件与结算载荷不一致");
        var finalHash = engine.ComputeStateHash();
        var complete = connection.CreateCommand();
        complete.Transaction = transaction;
        complete.CommandText = """
            UPDATE matches SET ended_utc=$utc,winner=$winner,final_hash=$hash,first_player=$first
            WHERE match_id=$match AND ended_utc IS NULL AND mode_id='ranked';
            """;
        complete.Parameters.AddWithValue("$utc", settlement.EndedAt.ToString("O"));
        complete.Parameters.AddWithValue("$winner", (object?)settlement.Winner ?? DBNull.Value);
        complete.Parameters.AddWithValue("$hash", finalHash);
        complete.Parameters.AddWithValue("$first", engine.State.FirstPlayer);
        complete.Parameters.AddWithValue("$match", settlement.MatchId);
        var changed = await complete.ExecuteNonQueryAsync();
        if (changed == 0)
            await VerifyRankedCompletionAsync(connection, transaction, settlement, finalHash);
        else
            await InsertOrVerifyOutboxAsync(connection, transaction, settlement);
    }

    private static async Task VerifyRankedCompletionAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12RankedSettlementEnvelope settlement, string finalHash)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT winner,final_hash,ended_utc FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", settlement.MatchId);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync() || reader.IsDBNull(2)
                || (reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0)) != settlement.Winner
                || !string.Equals(reader.IsDBNull(1) ? null : reader.GetString(1), finalHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("排位最终记录与幂等重放冲突");
        }
        await InsertOrVerifyOutboxAsync(connection, transaction, settlement);
    }

    private static async Task InsertOrVerifyOutboxAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12RankedSettlementEnvelope settlement)
    {
        var json = JsonSerializer.Serialize(settlement, RankedPersistenceJson);
        var hash = PersistenceHash(json);
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO ranked_settlement_outbox(
                match_id,payload_json,payload_hash,status,attempts,created_utc)
            VALUES($match,$json,$hash,'pending',0,$created);
            """;
        insert.Parameters.AddWithValue("$match", settlement.MatchId);
        insert.Parameters.AddWithValue("$json", json);
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$created", settlement.EndedAt.ToString("O"));
        await insert.ExecuteNonQueryAsync();
        var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = "SELECT payload_hash FROM ranked_settlement_outbox WHERE match_id=$match;";
        verify.Parameters.AddWithValue("$match", settlement.MatchId);
        var existing = (string?)await verify.ExecuteScalarAsync();
        if (!string.Equals(existing, hash, StringComparison.Ordinal))
            throw new InvalidOperationException("排位结算 outbox 幂等载荷冲突");
    }

    internal Task<IReadOnlyList<L12RankedSettlementOutboxEntry>> ListPendingRankedSettlementsAsync()
        => ListRankedSettlementOutboxAsync(includeApplied: false);

    internal Task<IReadOnlyList<L12RankedSettlementOutboxEntry>> ListRankedSettlementReconciliationAsync()
        => ListRankedSettlementOutboxAsync(includeApplied: true);

    private async Task<IReadOnlyList<L12RankedSettlementOutboxEntry>> ListRankedSettlementOutboxAsync(
        bool includeApplied)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_id,payload_json,payload_hash,status,attempts
            FROM ranked_settlement_outbox
            WHERE status='pending' OR ($includeApplied=1 AND status='applied')
            ORDER BY created_utc,match_id;
            """;
        command.Parameters.AddWithValue("$includeApplied", includeApplied ? 1 : 0);
        var result = new List<L12RankedSettlementOutboxEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(1);
            var hash = reader.GetString(2);
            L12RankedSettlementEnvelope? payload = null;
            string? loadError = null;
            try
            {
                if (!string.Equals(PersistenceHash(json), hash, StringComparison.Ordinal))
                    throw new InvalidDataException("payload hash mismatch");
                payload = JsonSerializer.Deserialize<L12RankedSettlementEnvelope>(json, RankedPersistenceJson)
                    ?? throw new InvalidDataException("payload is empty");
                ValidateSettlement(payload);
                if (!string.Equals(payload.MatchId, reader.GetString(0), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("match identity mismatch");
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                loadError = SafePersistenceError(error.Message);
            }
            result.Add(new L12RankedSettlementOutboxEntry(reader.GetString(0), payload, hash,
                reader.GetString(3), reader.GetInt32(4), loadError));
        }
        return result;
    }

    internal async Task QuarantineRankedSettlementAsync(string matchId, string payloadHash, string reason)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ranked_settlement_outbox
            SET status='quarantined',attempts=attempts+1,last_error=$error
            WHERE match_id=$match AND payload_hash=$hash AND status IN ('pending','applied');
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.Parameters.AddWithValue("$error", SafePersistenceError(reason));
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("排位结算 outbox 隔离状态冲突");
    }

    internal async Task MarkRankedSettlementAppliedAsync(string matchId, string payloadHash)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ranked_settlement_outbox
            SET status='applied',applied_utc=$utc,last_error=NULL
            WHERE match_id=$match AND payload_hash=$hash AND status='pending';
            """;
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$hash", payloadHash);
        if (await command.ExecuteNonQueryAsync() == 0)
        {
            var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = "SELECT status,payload_hash FROM ranked_settlement_outbox WHERE match_id=$match;";
            verify.Parameters.AddWithValue("$match", matchId);
            await using var reader = await verify.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetString(0) != "applied"
                || !string.Equals(reader.GetString(1), payloadHash, StringComparison.Ordinal))
                throw new InvalidOperationException("排位结算 outbox 确认冲突");
        }
        StorageFailureInjector?.Invoke("before-ranked-outbox-ack");
        await transaction.CommitAsync();
    }

    internal async Task RecordRankedSettlementFailureAsync(string matchId, string error)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ranked_settlement_outbox
            SET attempts=attempts+1,last_error=$error
            WHERE match_id=$match AND status IN ('pending','applied');
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$error", SafePersistenceError(error));
        await command.ExecuteNonQueryAsync();
    }

    internal async Task ClearAppliedRankedSettlementErrorAsync(string matchId, string payloadHash)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ranked_settlement_outbox SET last_error=NULL
            WHERE match_id=$match AND payload_hash=$hash AND status='applied';
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$hash", payloadHash);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<int> CountPendingRankedSettlementsAsync()
        => await ScalarCountAsync("SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='pending';");

    internal async Task<int> CountQuarantinedRankedSettlementsAsync()
        => await ScalarCountAsync("SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='quarantined';");

    internal async Task<int> CountActiveRankedRuntimesAsync()
        => await ScalarCountAsync("SELECT COUNT(*) FROM ranked_match_runtime WHERE status='active';");

    internal async Task<L12RankedRuntimeCheckpoint?> GetRankedRuntimeCheckpointAsync(string matchId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT checkpoint_json,checkpoint_hash,checkpoint_generation
            FROM ranked_match_runtime WHERE match_id=$match;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var json = reader.GetString(0);
        if (!string.Equals(PersistenceHash(json), reader.GetString(1), StringComparison.Ordinal))
            throw new InvalidDataException("排位运行快照校验失败");
        var runtime = JsonSerializer.Deserialize<L12RankedRuntimeCheckpoint>(json, RankedPersistenceJson)
            ?? throw new InvalidDataException("排位运行快照为空");
        ValidateRuntime(runtime);
        if (runtime.CheckpointGeneration != reader.GetInt64(2))
            throw new InvalidDataException("排位运行快照 generation 与索引列不一致");
        return runtime;
    }

    private async Task<int> ScalarCountAsync(string sql)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    internal async Task<IReadOnlyList<L12RankedRecoverySource>> LoadActiveRankedMatchesAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.room_code,m.seed,m.player_0,m.player_1,m.account_0,m.account_1,
                   COALESCE(m.initial_state_json,''),m.started_utc,
                   r.checkpoint_json,r.checkpoint_hash,r.checkpoint_generation
            FROM matches m
            LEFT JOIN ranked_match_runtime r ON r.match_id=m.match_id AND r.status='active'
            WHERE m.mode_id='ranked' AND m.ended_utc IS NULL
            ORDER BY m.started_utc,m.match_id;
            """;
        var rows = new List<(string MatchId, string RoomCode, int Seed, string[] Names, string[] Accounts,
            string Initial, string Started, string? RuntimeJson, string? RuntimeHash, long? RuntimeGeneration)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                    [reader.GetString(3), reader.GetString(4)],
                    [reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        reader.IsDBNull(6) ? string.Empty : reader.GetString(6)],
                    reader.GetString(7), reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11)));
        }
        var result = new List<L12RankedRecoverySource>();
        foreach (var row in rows)
        {
            L12PresetDeckDefinition[] decks = [];
            IReadOnlyList<L12RecordedCommand> events = [];
            L12RankedRuntimeCheckpoint? runtime = null;
            string? error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(row.Initial))
                    throw new InvalidDataException("缺少初始权威状态");
                if (row.Accounts.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("缺少排位账号席位");
                if (row.RuntimeJson is null || row.RuntimeHash is null)
                    throw new InvalidDataException("缺少排位运行快照");
                if (!string.Equals(PersistenceHash(row.RuntimeJson), row.RuntimeHash, StringComparison.Ordinal))
                    throw new InvalidDataException("排位运行快照校验失败");
                var parsedRuntime = JsonSerializer.Deserialize<L12RankedRuntimeCheckpoint>(row.RuntimeJson,
                    RankedPersistenceJson) ?? throw new InvalidDataException("排位运行快照为空");
                ValidateRuntime(parsedRuntime);
                if (parsedRuntime.CheckpointGeneration != row.RuntimeGeneration)
                    throw new InvalidDataException("排位运行快照 generation 与索引列不一致");
                if (!string.Equals(parsedRuntime.MatchId, row.MatchId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(parsedRuntime.RoomCode, row.RoomCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("排位运行快照身份不一致");
                runtime = parsedRuntime;
                decks = await ReadRecoveryDecksAsync(connection, row.MatchId, row.Initial);
                events = await ReadRecoveryEventsAsync(connection, row.MatchId);
            }
            catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException
                                                   or JsonException or KeyNotFoundException
                                                   or FormatException or OverflowException)
            {
                error = SafePersistenceError(failure.Message);
            }
            result.Add(new L12RankedRecoverySource(row.MatchId, row.RoomCode, row.Seed, row.Names,
                row.Accounts, row.Initial, row.Started, decks, events, runtime, error));
        }
        return result;
    }

    private static async Task<L12PresetDeckDefinition[]> ReadRecoveryDecksAsync(
        SqliteConnection connection, string matchId, string initialStateJson)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.player_index,p.deck_name,p.master_id,p.deck_snapshot_coverage,
                   d.section,d.card_id,d.quantity
            FROM match_participants p
            LEFT JOIN match_deck_cards d ON d.match_id=p.match_id AND d.player_index=p.player_index
            WHERE p.match_id=$match
            ORDER BY p.player_index,d.section,d.card_id;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        var names = new string[2];
        var masters = new string[2];
        var sections = Enumerable.Range(0, 2).Select(_ => new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["main"] = [], ["morale"] = [], ["special"] = [],
        }).ToArray();
        var seen = new bool[2];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var player = reader.GetInt32(0);
            if (player is < 0 or > 1) throw new InvalidDataException("排位牌库席位无效");
            if (!string.Equals(reader.GetString(3), "exact", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("排位牌库不是精确快照");
            seen[player] = true;
            names[player] = reader.GetString(1);
            masters[player] = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            if (reader.IsDBNull(4)) continue;
            var section = reader.GetString(4);
            if (!sections[player].TryGetValue(section, out var cards))
                throw new InvalidDataException("排位牌库分区无效");
            var card = reader.GetString(5);
            var quantity = reader.GetInt32(6);
            if (quantity is < 1 or > 100) throw new InvalidDataException("排位牌库数量无效");
            cards.AddRange(Enumerable.Repeat(card, quantity));
        }
        if (!seen.All(value => value) || masters.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("排位牌库快照不完整");
        var persisted = Enumerable.Range(0, 2).Select(player => new L12PresetDeckDefinition
        {
            Name = names[player], MasterId = masters[player],
            CardIds = sections[player]["main"], MoraleIds = sections[player]["morale"],
            SpecialIds = sections[player]["special"],
        }).ToArray();
        return RestoreDeckOrderFromInitialState(initialStateJson, persisted);
    }

    private static L12PresetDeckDefinition[] RestoreDeckOrderFromInitialState(string initialStateJson,
        IReadOnlyList<L12PresetDeckDefinition> persisted)
    {
        using var document = JsonDocument.Parse(initialStateJson);
        if (!document.RootElement.TryGetProperty("Players", out var players)
            || players.ValueKind != JsonValueKind.Array || players.GetArrayLength() != 2)
            throw new InvalidDataException("初始状态玩家结构无效");
        var result = new L12PresetDeckDefinition[2];
        for (var player = 0; player < 2; player++)
        {
            var state = players[player];
            var prefix = $"p{player}-c";
            var main = new List<(int Order, string CardId)>();
            foreach (var zoneName in new[] { "Library", "Graveyard" })
            {
                if (!state.TryGetProperty(zoneName, out var zone) || zone.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"初始状态缺少 {zoneName}");
                foreach (var card in zone.EnumerateArray())
                {
                    var instance = card.GetProperty("InstanceId").GetString() ?? string.Empty;
                    if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || !int.TryParse(instance[prefix.Length..], out var order)) continue;
                    main.Add((order, card.GetProperty("CardId").GetString()
                                     ?? throw new InvalidDataException("初始牌库卡牌身份为空")));
                }
            }
            var morale = state.GetProperty("MoraleDeck").EnumerateArray()
                .Select(card => (Order: InstanceOrder(card.GetProperty("InstanceId").GetString(), $"p{player}-m"),
                    CardId: card.GetProperty("CardId").GetString() ?? string.Empty))
                .OrderBy(item => item.Order).Select(item => item.CardId).ToList();
            var trials = state.GetProperty("SpecialZones").GetProperty("Trials").EnumerateArray()
                .Select(card => (Order: InstanceOrder(card.GetProperty("InstanceId").GetString(),
                        $"p{player}-special-"),
                    CardId: card.GetProperty("CardId").GetString() ?? string.Empty))
                .OrderBy(item => item.Order).Select(item => item.CardId).ToList();
            var orderedMain = main.OrderBy(item => item.Order).Select(item => item.CardId).ToList();
            ValidateDeckMultiset(persisted[player].CardIds, orderedMain, "主牌库");
            ValidateDeckMultiset(persisted[player].MoraleIds, morale, "士气牌库");
            ValidateDeckMultiset(persisted[player].SpecialIds, trials, "特殊牌库");
            result[player] = new L12PresetDeckDefinition
            {
                Name = persisted[player].Name,
                MasterId = persisted[player].MasterId,
                CardIds = orderedMain,
                MoraleIds = morale,
                SpecialIds = trials,
            };
        }
        return result;
    }

    private static int InstanceOrder(string? instanceId, string prefix)
        => instanceId is not null && instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
           && int.TryParse(instanceId[prefix.Length..], out var order)
            ? order : throw new InvalidDataException("初始牌库实例序号无效");

    private static void ValidateDeckMultiset(IReadOnlyList<string> persisted,
        IReadOnlyList<string> restored, string label)
    {
        static string[] Normalize(IReadOnlyList<string> values)
            => values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!Normalize(persisted).SequenceEqual(Normalize(restored), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label}与精确构筑快照不一致");
    }

    private static async Task<IReadOnlyList<L12RecordedCommand>> ReadRecoveryEventsAsync(
        SqliteConnection connection, string matchId)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence,received_utc,player_index,command_json,accepted,error,revision,state_hash,state_json
            FROM match_events WHERE match_id=$match ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        var events = new List<L12RecordedCommand>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            events.Add(new L12RecordedCommand(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                JsonDocument.Parse(reader.GetString(3)).RootElement.Clone(), reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), reader.GetString(7),
                JsonDocument.Parse(reader.GetString(8)).RootElement.Clone()));
        return events;
    }

    internal async Task FinalizeIncompatibleRankedAsync(L12RankedRecoverySource source,
        L12RankedSettlementEnvelope settlement, string reason)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var latestHash = source.Commands.LastOrDefault()?.StateHash;
        var complete = connection.CreateCommand();
        complete.Transaction = transaction;
        complete.CommandText = """
            UPDATE matches SET ended_utc=$utc,winner=NULL,final_hash=$hash,error=$error
            WHERE match_id=$match AND ended_utc IS NULL AND mode_id='ranked';
            """;
        complete.Parameters.AddWithValue("$utc", settlement.EndedAt.ToString("O"));
        complete.Parameters.AddWithValue("$hash", (object?)latestHash ?? DBNull.Value);
        complete.Parameters.AddWithValue("$error", SafePersistenceError(reason));
        complete.Parameters.AddWithValue("$match", source.MatchId);
        if (await complete.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("待隔离排位已经结束或身份冲突");
        if (source.Runtime is { } currentRuntime)
        {
            var completedRuntime = currentRuntime with
            {
                Status = "completed",
                CheckpointGeneration = checked(currentRuntime.CheckpointGeneration + 1),
                UpdatedAt = settlement.EndedAt,
            };
            await UpsertRankedRuntimeAsync(connection, transaction, completedRuntime);
        }
        else
        {
            var quarantineRuntime = connection.CreateCommand();
            quarantineRuntime.Transaction = transaction;
            quarantineRuntime.CommandText = """
                UPDATE ranked_match_runtime SET status='quarantined',updated_utc=$utc
                WHERE match_id=$match;
                """;
            quarantineRuntime.Parameters.AddWithValue("$match", source.MatchId);
            quarantineRuntime.Parameters.AddWithValue("$utc", settlement.EndedAt.ToString("O"));
            await quarantineRuntime.ExecuteNonQueryAsync();
        }
        var quarantine = connection.CreateCommand();
        quarantine.Transaction = transaction;
        quarantine.CommandText = """
            INSERT INTO ranked_recovery_quarantine(match_id,reason,created_utc)
            VALUES($match,$reason,$utc)
            ON CONFLICT(match_id) DO UPDATE SET reason=excluded.reason,created_utc=excluded.created_utc;
            """;
        quarantine.Parameters.AddWithValue("$match", source.MatchId);
        quarantine.Parameters.AddWithValue("$reason", SafePersistenceError(reason));
        quarantine.Parameters.AddWithValue("$utc", settlement.EndedAt.ToString("O"));
        await quarantine.ExecuteNonQueryAsync();
        var auditableIdentity = !string.IsNullOrWhiteSpace(settlement.FirstAccountId)
            && !string.IsNullOrWhiteSpace(settlement.SecondAccountId)
            && !string.Equals(settlement.FirstAccountId, settlement.SecondAccountId,
                StringComparison.OrdinalIgnoreCase);
        if (auditableIdentity)
        {
            await InsertOrVerifyOutboxAsync(connection, transaction, settlement);
        }
        else
        {
            // 缺少合法双席身份时只能本地隔离，不能制造永远无法投递的平台 outbox。
        }
        StorageFailureInjector?.Invoke("before-ranked-incompatible-commit");
        await transaction.CommitAsync();
    }

    private static string PersistenceHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SafePersistenceError(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        return normalized[..Math.Min(normalized.Length, 240)];
    }
}
