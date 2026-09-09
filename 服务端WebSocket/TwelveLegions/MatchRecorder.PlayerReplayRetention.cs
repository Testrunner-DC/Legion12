using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public const int PlayerReplayWindowSize = 30;
    public static readonly TimeSpan PlayerReplayCleanupInterval = TimeSpan.FromDays(1);
    internal const int PlayerReplayCleanupBatchSize = 25;
    internal const int PlayerReplayCleanupMaximumMatchesPerRun = 500;
    private static readonly TimeSpan PlayerReplayCleanupLease = TimeSpan.FromHours(2);

    private readonly string _playerReplayRecorderInstanceId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _playerReplayCleanupGate = new(1, 1);
    private long _nextPlayerReplayCleanupUtcTicks = DateTimeOffset.MaxValue.UtcDateTime.Ticks;

    private const string PlayerReplayWindowCte = """
        WITH replay_entries AS (
            SELECT m.match_id,m.ended_utc,m.started_utc,
                   CASE WHEN m.account_0 IS NOT NULL
                        THEN 'account:' || m.account_0 ELSE 'legacy:' || m.player_0 END AS participant_key
            FROM matches m
            WHERE m.ended_utc IS NOT NULL AND m.mode_id <> 'sandbox'
            UNION ALL
            SELECT m.match_id,m.ended_utc,m.started_utc,
                   CASE WHEN m.account_1 IS NOT NULL
                        THEN 'account:' || m.account_1 ELSE 'legacy:' || m.player_1 END AS participant_key
            FROM matches m
            WHERE m.ended_utc IS NOT NULL AND m.mode_id <> 'sandbox'
        ),
        distinct_entries AS (
            SELECT participant_key,match_id,ended_utc,started_utc
            FROM replay_entries
            GROUP BY participant_key,match_id,ended_utc,started_utc
        ),
        ordered_entries AS (
            SELECT participant_key,match_id,
                   ROW_NUMBER() OVER (
                       PARTITION BY participant_key
                       ORDER BY julianday(ended_utc) DESC,ended_utc DESC,
                                julianday(started_utc) DESC,started_utc DESC,match_id DESC
                   ) AS replay_ordinal
            FROM distinct_entries
        ),
        protected_replays AS (
            SELECT DISTINCT match_id FROM ordered_entries WHERE replay_ordinal <= $window
        )
        """;

    private const string TargetPlayerReplayWindowCte = """
        WITH target_identities AS (
            SELECT account_0 AS account_id,
                   CASE WHEN account_0 IS NULL THEN player_0 END AS legacy_name,
                   CASE WHEN account_0 IS NOT NULL
                        THEN 'account:' || account_0 ELSE 'legacy:' || player_0 END AS participant_key
            FROM matches WHERE match_id=$match
            UNION
            SELECT account_1 AS account_id,
                   CASE WHEN account_1 IS NULL THEN player_1 END AS legacy_name,
                   CASE WHEN account_1 IS NOT NULL
                        THEN 'account:' || account_1 ELSE 'legacy:' || player_1 END AS participant_key
            FROM matches WHERE match_id=$match
        ),
        replay_entries AS (
            SELECT m.match_id,m.ended_utc,m.started_utc,target.participant_key
            FROM target_identities target JOIN matches m
              ON (target.account_id IS NOT NULL
                  AND (m.account_0=target.account_id OR m.account_1=target.account_id))
                 OR (target.account_id IS NULL
                     AND ((m.account_0 IS NULL AND m.player_0=target.legacy_name)
                          OR (m.account_1 IS NULL AND m.player_1=target.legacy_name)))
            WHERE m.ended_utc IS NOT NULL AND m.mode_id <> 'sandbox'
        ),
        distinct_entries AS (
            SELECT participant_key,match_id,ended_utc,started_utc
            FROM replay_entries
            GROUP BY participant_key,match_id,ended_utc,started_utc
        ),
        ordered_entries AS (
            SELECT participant_key,match_id,
                   ROW_NUMBER() OVER (
                       PARTITION BY participant_key
                       ORDER BY julianday(ended_utc) DESC,ended_utc DESC,
                                julianday(started_utc) DESC,started_utc DESC,match_id DESC
                   ) AS replay_ordinal
            FROM distinct_entries
        ),
        protected_replays AS (
            SELECT DISTINCT match_id FROM ordered_entries WHERE replay_ordinal <= $window
        )
        """;

    private async Task InitializePlayerReplayRetentionSchemaAsync(
        SqliteConnection connection, DateTimeOffset utcNow)
    {
        var now = utcNow.ToUniversalTime();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS player_replay_cleanup_schedule (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                last_run_utc TEXT NOT NULL,
                next_run_utc TEXT NOT NULL,
                lease_owner TEXT,
                lease_expires_utc TEXT
            );
            CREATE TABLE IF NOT EXISTS player_replay_payload_expirations (
                match_id TEXT PRIMARY KEY,
                expired_utc TEXT NOT NULL,
                retained_command_count INTEGER NOT NULL,
                cleared_payload_bytes INTEGER NOT NULL,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_player_replay_payload_expirations_utc
                ON player_replay_payload_expirations(expired_utc,match_id);
            INSERT OR IGNORE INTO player_replay_cleanup_schedule(
                singleton_id,last_run_utc,next_run_utc)
            VALUES(1,$now,$next);
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$next", now.Add(PlayerReplayCleanupInterval).ToString("O"));
        await command.ExecuteNonQueryAsync();

        var schedule = connection.CreateCommand();
        schedule.CommandText =
            "SELECT next_run_utc FROM player_replay_cleanup_schedule WHERE singleton_id=1;";
        var stored = Convert.ToString(await schedule.ExecuteScalarAsync());
        if (!TryParseUtc(stored, out var nextRun))
            throw new InvalidDataException("玩家回放清理计划时间无效");
        Volatile.Write(ref _nextPlayerReplayCleanupUtcTicks, nextRun.UtcDateTime.Ticks);
    }

    public async Task<IReadOnlyList<L12MatchSummary>> ListRecentPlayerReplayMatchesAsync(
        string accountId, string legacyPlayerName, int limit = PlayerReplayWindowSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.room_code,m.player_0,m.player_1,m.deck_0,m.deck_1,
                   m.started_utc,m.ended_utc,m.winner,m.final_hash,m.error,COUNT(e.id)
            FROM matches m LEFT JOIN match_events e ON e.match_id=m.match_id
            WHERE m.mode_id <> 'sandbox' AND m.ended_utc IS NOT NULL
              AND (
                    m.account_0=$account OR m.account_1=$account
                    OR ((m.account_0 IS NULL AND m.player_0=$player)
                        OR (m.account_1 IS NULL AND m.player_1=$player))
                  )
            GROUP BY m.match_id
            ORDER BY julianday(m.ended_utc) DESC,m.ended_utc DESC,
                     julianday(m.started_utc) DESC,m.started_utc DESC,m.match_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$player", legacyPlayerName);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, PlayerReplayWindowSize));
        var matches = new List<L12MatchSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) matches.Add(ReadSummary(reader));
        return matches;
    }

    public async Task<bool> IsWithinRecentPlayerReplayWindowAsync(
        string matchId, string accountId, string legacyPlayerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(matchId)) return false;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM (
                SELECT m.match_id
                FROM matches m
                WHERE m.mode_id <> 'sandbox' AND m.ended_utc IS NOT NULL
                  AND (
                        m.account_0=$account OR m.account_1=$account
                        OR ((m.account_0 IS NULL AND m.player_0=$player)
                            OR (m.account_1 IS NULL AND m.player_1=$player))
                      )
                ORDER BY julianday(m.ended_utc) DESC,m.ended_utc DESC,
                         julianday(m.started_utc) DESC,m.started_utc DESC,m.match_id DESC
                LIMIT $window
            ) recent WHERE recent.match_id=$match;
            """;
        command.Parameters.AddWithValue("$match", matchId.Trim());
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$player", legacyPlayerName);
        command.Parameters.AddWithValue("$window", PlayerReplayWindowSize);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<bool> IsPlayerReplayPayloadExpiredAsync(
        string matchId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(matchId)) return false;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var table = connection.CreateCommand();
        table.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name='player_replay_payload_expirations';
            """;
        if (Convert.ToInt64(await table.ExecuteScalarAsync(cancellationToken)) != 1) return false;
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM player_replay_payload_expirations WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId.Trim());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<L12PlayerReplayCleanupResult> RunPlayerReplayCleanupIfDueAsync(
        IReadOnlyCollection<string>? activeMatchIds = null,
        IReadOnlyCollection<string>? protectedEvidenceMatchIds = null,
        IReadOnlyCollection<string>? protectedEvidenceRoomCodes = null,
        Func<L12ReplayEvidenceReferences>? evidenceProvider = null,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = (utcNow ?? _utcNow()).ToUniversalTime();
        var cachedNextTicks = Volatile.Read(ref _nextPlayerReplayCleanupUtcTicks);
        if (cachedNextTicks != DateTimeOffset.MaxValue.UtcDateTime.Ticks
            && now.UtcDateTime.Ticks < cachedNextTicks)
            return new L12PlayerReplayCleanupResult(false, 0, 0, 0, false, null,
                new DateTimeOffset(cachedNextTicks, TimeSpan.Zero));

        await _playerReplayCleanupGate.WaitAsync(cancellationToken);
        var leaseClaimed = false;
        try
        {
            await using var connection = await OpenWriteConnectionAsync(cancellationToken);
            await InitializePlayerReplayRetentionSchemaAsync(connection, now);

            DateTimeOffset lastRun;
            DateTimeOffset persistedNext;
            using (var claim = connection.BeginTransaction(deferred: false))
            {
                var schedule = connection.CreateCommand();
                schedule.Transaction = claim;
                schedule.CommandText = """
                    SELECT last_run_utc,next_run_utc,lease_owner,lease_expires_utc
                    FROM player_replay_cleanup_schedule WHERE singleton_id=1;
                    """;
                string lastRunText;
                string nextRunText;
                string? leaseOwner;
                string? leaseExpiresText;
                await using (var reader = await schedule.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                        throw new InvalidDataException("玩家回放清理计划缺失");
                    lastRunText = reader.GetString(0);
                    nextRunText = reader.GetString(1);
                    leaseOwner = reader.IsDBNull(2) ? null : reader.GetString(2);
                    leaseExpiresText = reader.IsDBNull(3) ? null : reader.GetString(3);
                }
                if (!TryParseUtc(lastRunText, out lastRun)
                    || !TryParseUtc(nextRunText, out persistedNext))
                    throw new InvalidDataException("玩家回放清理计划时间无效");
                Volatile.Write(ref _nextPlayerReplayCleanupUtcTicks, persistedNext.UtcDateTime.Ticks);
                if (now < persistedNext)
                {
                    await claim.CommitAsync(cancellationToken);
                    return new L12PlayerReplayCleanupResult(
                        false, 0, 0, 0, false, lastRun, persistedNext);
                }
                if (!string.IsNullOrWhiteSpace(leaseOwner)
                    && TryParseUtc(leaseExpiresText, out var leaseExpires) && leaseExpires > now)
                {
                    await claim.CommitAsync(cancellationToken);
                    return new L12PlayerReplayCleanupResult(
                        false, 0, 0, 0, false, lastRun, persistedNext);
                }

                var acquire = connection.CreateCommand();
                acquire.Transaction = claim;
                acquire.CommandText = """
                    UPDATE player_replay_cleanup_schedule
                    SET lease_owner=$owner,lease_expires_utc=$expires WHERE singleton_id=1;
                    """;
                acquire.Parameters.AddWithValue("$owner", _playerReplayRecorderInstanceId);
                acquire.Parameters.AddWithValue("$expires",
                    now.Add(PlayerReplayCleanupLease).ToString("O"));
                await acquire.ExecuteNonQueryAsync(cancellationToken);
                await claim.CommitAsync(cancellationToken);
                leaseClaimed = true;
            }

            var active = NormalizeReplayHolds(activeMatchIds);
            var evidenceMatches = NormalizeReplayHolds(protectedEvidenceMatchIds);
            var evidenceRooms = NormalizeReplayHolds(protectedEvidenceRoomCodes);
            await PopulateReplayCleanupHoldsAsync(
                connection, active.Concat(evidenceMatches), evidenceRooms, cancellationToken);

            var purgedMatches = 0;
            long retainedCommands = 0;
            long clearedPayloadBytes = 0;
            while (purgedMatches < PlayerReplayCleanupMaximumMatchesPerRun)
            {
                var remainingCapacity = PlayerReplayCleanupMaximumMatchesPerRun - purgedMatches;
                var candidates = await ReadPlayerReplayCleanupCandidatesAsync(connection,
                    Math.Min(PlayerReplayCleanupBatchSize, remainingCapacity), cancellationToken);
                if (candidates.Count == 0) break;

                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (active.Contains(candidate.MatchId)
                        || evidenceMatches.Contains(candidate.MatchId)
                        || evidenceRooms.Contains(candidate.RoomCode))
                        continue;

                    using var purgeTransaction = connection.BeginTransaction(deferred: false);
                    if (evidenceProvider is not null)
                    {
                        var currentEvidence = evidenceProvider()
                            ?? throw new InvalidDataException("Bug 回放证据引用快照缺失");
                        await MergeReplayEvidenceHoldsAsync(connection, purgeTransaction,
                            currentEvidence, evidenceMatches, evidenceRooms, cancellationToken);
                        if (evidenceMatches.Contains(candidate.MatchId)
                            || evidenceRooms.Contains(candidate.RoomCode))
                        {
                            await purgeTransaction.CommitAsync(cancellationToken);
                            continue;
                        }
                    }
                    if (!await IsPlayerReplayPurgeEligibleAsync(
                            connection, purgeTransaction, candidate.MatchId, cancellationToken))
                    {
                        await purgeTransaction.CommitAsync(cancellationToken);
                        continue;
                    }

                    var metrics = await ReadPlayerReplayPayloadMetricsAsync(
                        connection, purgeTransaction, candidate.MatchId, cancellationToken);
                    // Command rows are the compact archival ledger (including authority conclusions).
                    // Remove reconstructable state payloads while keeping command counts and match results stable.
                    await PurgeReplayPayloadAsync(connection, purgeTransaction, candidate.MatchId, now,
                        metrics.CommandCount, metrics.PayloadBytes, cancellationToken);
                    StorageFailureInjector?.Invoke("before-player-replay-purge-commit");

                    var refreshLease = connection.CreateCommand();
                    refreshLease.Transaction = purgeTransaction;
                    refreshLease.CommandText = """
                        UPDATE player_replay_cleanup_schedule SET lease_expires_utc=$expires
                        WHERE singleton_id=1 AND lease_owner=$owner;
                        """;
                    refreshLease.Parameters.AddWithValue("$owner", _playerReplayRecorderInstanceId);
                    var leaseNow = _utcNow().ToUniversalTime();
                    if (leaseNow < now) leaseNow = now;
                    refreshLease.Parameters.AddWithValue("$expires",
                        leaseNow.Add(PlayerReplayCleanupLease).ToString("O"));
                    if (await refreshLease.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException("玩家回放清理租约已丢失");
                    await purgeTransaction.CommitAsync(cancellationToken);

                    purgedMatches++;
                    retainedCommands += metrics.CommandCount;
                    clearedPayloadBytes += metrics.PayloadBytes;
                    if (purgedMatches >= PlayerReplayCleanupMaximumMatchesPerRun) break;
                }
                if (purgedMatches < PlayerReplayCleanupMaximumMatchesPerRun)
                    await Task.Yield();
            }

            var hasMore = await HasPlayerReplayCleanupCandidateAsync(connection, cancellationToken);
            var nextRun = NextStorageCleanupUtc(now);
            Console.WriteLine($"Player replay cleanup: pruned={purgedMatches};backlog={hasMore};nextUtc={nextRun:O}");
            using (var finish = connection.BeginTransaction(deferred: false))
            {
                var updateSchedule = connection.CreateCommand();
                updateSchedule.Transaction = finish;
                updateSchedule.CommandText = """
                    UPDATE player_replay_cleanup_schedule
                    SET last_run_utc=$last,next_run_utc=$next,lease_owner=NULL,lease_expires_utc=NULL
                    WHERE singleton_id=1 AND lease_owner=$owner;
                    """;
                updateSchedule.Parameters.AddWithValue("$last", now.ToString("O"));
                updateSchedule.Parameters.AddWithValue("$next", nextRun.ToString("O"));
                updateSchedule.Parameters.AddWithValue("$owner", _playerReplayRecorderInstanceId);
                if (await updateSchedule.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("玩家回放清理租约已丢失");
                await finish.CommitAsync(cancellationToken);
            }
            leaseClaimed = false;
            Volatile.Write(ref _nextPlayerReplayCleanupUtcTicks, nextRun.UtcDateTime.Ticks);
            return new L12PlayerReplayCleanupResult(true, purgedMatches, retainedCommands,
                clearedPayloadBytes, hasMore, now, nextRun);
        }
        catch
        {
            if (leaseClaimed)
            {
                try
                {
                    await using var recovery = await OpenWriteConnectionAsync(CancellationToken.None);
                    using var release = recovery.BeginTransaction(deferred: false);
                    var command = recovery.CreateCommand();
                    command.Transaction = release;
                    command.CommandText = """
                        UPDATE player_replay_cleanup_schedule
                        SET lease_owner=NULL,lease_expires_utc=NULL
                        WHERE singleton_id=1 AND lease_owner=$owner;
                        """;
                    command.Parameters.AddWithValue("$owner", _playerReplayRecorderInstanceId);
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                    await release.CommitAsync(CancellationToken.None);
                }
                catch (Exception releaseError)
                {
                    Console.Error.WriteLine(
                        $"Player replay cleanup lease release: {releaseError.Message}");
                }
            }
            throw;
        }
        finally
        {
            _playerReplayCleanupGate.Release();
        }
    }

    private static HashSet<string> NormalizeReplayHolds(IReadOnlyCollection<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task PopulateReplayCleanupHoldsAsync(
        SqliteConnection connection, IEnumerable<string> matchIds, IEnumerable<string> roomCodes,
        CancellationToken cancellationToken)
    {
        var reset = connection.CreateCommand();
        reset.CommandText = """
            CREATE TEMP TABLE IF NOT EXISTS player_replay_cleanup_match_holds (
                value TEXT PRIMARY KEY COLLATE NOCASE
            );
            CREATE TEMP TABLE IF NOT EXISTS player_replay_cleanup_room_holds (
                value TEXT PRIMARY KEY COLLATE NOCASE
            );
            DELETE FROM player_replay_cleanup_match_holds;
            DELETE FROM player_replay_cleanup_room_holds;
            """;
        await reset.ExecuteNonQueryAsync(cancellationToken);
        foreach (var matchId in matchIds.Distinct(StringComparer.OrdinalIgnoreCase))
            await InsertReplayCleanupHoldAsync(connection, null,
                "player_replay_cleanup_match_holds", matchId, cancellationToken);
        foreach (var roomCode in roomCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            await InsertReplayCleanupHoldAsync(connection, null,
                "player_replay_cleanup_room_holds", roomCode, cancellationToken);
    }

    private static async Task MergeReplayEvidenceHoldsAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        L12ReplayEvidenceReferences evidence,
        HashSet<string> evidenceMatches, HashSet<string> evidenceRooms,
        CancellationToken cancellationToken)
    {
        foreach (var matchId in NormalizeReplayHolds(evidence.MatchIds))
        {
            if (!evidenceMatches.Add(matchId)) continue;
            await InsertReplayCleanupHoldAsync(connection, transaction,
                "player_replay_cleanup_match_holds", matchId, cancellationToken);
        }
        foreach (var roomCode in NormalizeReplayHolds(evidence.RoomCodes))
        {
            if (!evidenceRooms.Add(roomCode)) continue;
            await InsertReplayCleanupHoldAsync(connection, transaction,
                "player_replay_cleanup_room_holds", roomCode, cancellationToken);
        }
    }

    private static async Task InsertReplayCleanupHoldAsync(
        SqliteConnection connection, SqliteTransaction? transaction,
        string table, string value, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT OR IGNORE INTO {table}(value) VALUES($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<PlayerReplayCleanupCandidate>>
        ReadPlayerReplayCleanupCandidatesAsync(
            SqliteConnection connection, int limit, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = PlayerReplayWindowCte + """
            SELECT m.match_id,m.room_code
            FROM matches m
            WHERE m.mode_id IN ('casual','friendly') AND m.ended_utc IS NOT NULL
              AND NOT EXISTS(
                  SELECT 1 FROM protected_replays p WHERE p.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM player_replay_payload_expirations x WHERE x.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM player_replay_cleanup_match_holds h WHERE h.value=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM player_replay_cleanup_room_holds h WHERE h.value=m.room_code)
              AND NOT EXISTS(
                  SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=m.match_id)
              AND (
                    m.initial_state_json IS NOT NULL
                    OR EXISTS(SELECT 1 FROM match_events e
                              WHERE e.match_id=m.match_id AND e.state_json<>'{}')
                    OR EXISTS(SELECT 1 FROM match_action_requests r WHERE r.match_id=m.match_id)
                    OR EXISTS(SELECT 1 FROM match_action_events e WHERE e.match_id=m.match_id)
                    OR EXISTS(SELECT 1 FROM match_state_checkpoints c WHERE c.match_id=m.match_id)
                  )
            ORDER BY julianday(m.ended_utc),m.ended_utc,
                     julianday(m.started_utc),m.started_utc,m.match_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$window", PlayerReplayWindowSize);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<PlayerReplayCleanupCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new PlayerReplayCleanupCandidate(reader.GetString(0), reader.GetString(1)));
        return result;
    }

    private static async Task<bool> IsPlayerReplayPurgeEligibleAsync(
        SqliteConnection connection, SqliteTransaction transaction, string matchId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = TargetPlayerReplayWindowCte + """
            SELECT COUNT(*)
            FROM matches m
            WHERE m.match_id=$match
              AND m.mode_id IN ('casual','friendly') AND m.ended_utc IS NOT NULL
              AND NOT EXISTS(
                  SELECT 1 FROM protected_replays p WHERE p.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM player_replay_payload_expirations x WHERE x.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM player_replay_cleanup_match_holds h WHERE h.value=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM player_replay_cleanup_room_holds h WHERE h.value=m.room_code)
              AND NOT EXISTS(
                  SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=m.match_id)
              AND NOT EXISTS(
                  SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=m.match_id)
              AND (
                    m.initial_state_json IS NOT NULL
                    OR EXISTS(SELECT 1 FROM match_events e
                              WHERE e.match_id=m.match_id AND e.state_json<>'{}')
                    OR EXISTS(SELECT 1 FROM match_action_requests r WHERE r.match_id=m.match_id)
                    OR EXISTS(SELECT 1 FROM match_action_events e WHERE e.match_id=m.match_id)
                    OR EXISTS(SELECT 1 FROM match_state_checkpoints c WHERE c.match_id=m.match_id)
                  );
            """;
        command.Parameters.AddWithValue("$window", PlayerReplayWindowSize);
        command.Parameters.AddWithValue("$match", matchId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<PlayerReplayPayloadMetrics> ReadPlayerReplayPayloadMetricsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string matchId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM match_events e WHERE e.match_id=$match),
                COALESCE((SELECT length(CAST(initial_state_json AS BLOB))
                          FROM matches WHERE match_id=$match),0)
                + COALESCE((SELECT SUM(CASE WHEN state_json='{}' THEN 0
                                           ELSE length(CAST(state_json AS BLOB)) END)
                            FROM match_events e WHERE e.match_id=$match),0)
                + COALESCE((SELECT SUM(length(CAST(event_json AS BLOB)))
                            FROM match_action_events e WHERE e.match_id=$match),0)
                + COALESCE((SELECT SUM(length(state_blob)+COALESCE(length(random_state_blob),0))
                            FROM match_state_checkpoints c WHERE c.match_id=$match),0)
                + COALESCE((SELECT SUM(length(CAST(request_id AS BLOB))
                                      +length(CAST(state_hash AS BLOB))
                                      +length(CAST(created_utc AS BLOB))
                                      +COALESCE(length(CAST(error AS BLOB)),0))
                            FROM match_action_requests r WHERE r.match_id=$match),0);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("无法量化待清理的玩家回放载荷");
        return new PlayerReplayPayloadMetrics(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task<bool> HasPlayerReplayCleanupCandidateAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var candidates = await ReadPlayerReplayCleanupCandidatesAsync(connection, 1, cancellationToken);
        return candidates.Count != 0;
    }

    private sealed record PlayerReplayCleanupCandidate(string MatchId, string RoomCode);
    private sealed record PlayerReplayPayloadMetrics(long CommandCount, long PayloadBytes);
}

public sealed record L12ReplayEvidenceReferences(
    IReadOnlyCollection<string> MatchIds,
    IReadOnlyCollection<string> RoomCodes);

public sealed record L12PlayerReplayCleanupResult(
    bool Ran,
    int PurgedMatches,
    long RetainedCommandRows,
    long ClearedPayloadBytes,
    bool HasMoreEligibleMatches,
    DateTimeOffset? LastRunUtc,
    DateTimeOffset NextRunUtc);
