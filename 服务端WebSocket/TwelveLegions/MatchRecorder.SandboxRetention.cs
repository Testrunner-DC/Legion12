using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public static readonly TimeSpan SandboxReplayRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan SandboxReplayCleanupInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan SandboxReplayCleanupLease = TimeSpan.FromHours(2);

    private readonly string _sandboxRecorderInstanceId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _sandboxCleanupGate = new(1, 1);
    private long _nextSandboxCleanupUtcTicks = DateTimeOffset.MaxValue.UtcDateTime.Ticks;

    private async Task InitializeSandboxRecordingSchemaAsync(SqliteConnection connection, DateTimeOffset utcNow)
    {
        var now = utcNow.ToUniversalTime();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sandbox_recordings (
                match_id TEXT PRIMARY KEY,
                owner_instance_id TEXT NOT NULL,
                status TEXT NOT NULL,
                last_activity_utc TEXT NOT NULL,
                retention_anchor_utc TEXT,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_sandbox_recordings_retention
                ON sandbox_recordings(status, retention_anchor_utc, match_id);
            CREATE TABLE IF NOT EXISTS sandbox_replay_cleanup_schedule (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                last_run_utc TEXT NOT NULL,
                next_run_utc TEXT NOT NULL,
                lease_owner TEXT,
                lease_expires_utc TEXT
            );
            CREATE TABLE IF NOT EXISTS sandbox_replay_expirations (
                match_id TEXT PRIMARY KEY,
                expired_utc TEXT NOT NULL
            );
            INSERT OR IGNORE INTO sandbox_replay_cleanup_schedule(singleton_id,last_run_utc,next_run_utc)
            VALUES(1,$now,$next);
            INSERT OR IGNORE INTO sandbox_recordings(
                match_id,owner_instance_id,status,last_activity_utc,retention_anchor_utc)
            SELECT match_id,'',
                   CASE WHEN ended_utc IS NULL THEN 'abandoned' ELSE 'completed' END,
                   COALESCE(ended_utc,
                       (SELECT MAX(received_utc) FROM match_events e WHERE e.match_id=matches.match_id),
                       started_utc),
                   COALESCE(ended_utc,
                       (SELECT MAX(received_utc) FROM match_events e WHERE e.match_id=matches.match_id),
                       started_utc)
            FROM matches WHERE mode_id='sandbox';
            UPDATE matches
            SET ended_utc=COALESCE((
                    SELECT last_activity_utc FROM sandbox_recordings s WHERE s.match_id=matches.match_id
                ),$now),
                error=COALESCE(error,'沙盒录像因服务重启结束')
            WHERE mode_id='sandbox' AND ended_utc IS NULL
              AND EXISTS(
                  SELECT 1 FROM sandbox_recordings s
                  WHERE s.match_id=matches.match_id AND s.owner_instance_id<>$owner
              );
            UPDATE sandbox_recordings
            SET status='abandoned',owner_instance_id='',
                retention_anchor_utc=COALESCE(retention_anchor_utc,last_activity_utc)
            WHERE status='active' AND owner_instance_id<>$owner;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$next", now.Add(SandboxReplayCleanupInterval).ToString("O"));
        command.Parameters.AddWithValue("$owner", _sandboxRecorderInstanceId);
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "sandbox_replay_cleanup_schedule", "lease_owner", "TEXT");
        await EnsureColumnAsync(connection, "sandbox_replay_cleanup_schedule", "lease_expires_utc", "TEXT");

        var schedule = connection.CreateCommand();
        schedule.CommandText = "SELECT next_run_utc FROM sandbox_replay_cleanup_schedule WHERE singleton_id=1;";
        var stored = Convert.ToString(await schedule.ExecuteScalarAsync());
        if (!DateTimeOffset.TryParse(stored, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var nextRun))
            throw new InvalidDataException("沙盒录像清理计划时间无效");
        Volatile.Write(ref _nextSandboxCleanupUtcTicks, nextRun.UtcDateTime.Ticks);
    }

    private async Task InsertSandboxRecordingAsync(SqliteConnection connection, SqliteTransaction transaction,
        string matchId, string startedUtc)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sandbox_replay_expirations WHERE match_id=$match;
            INSERT INTO sandbox_recordings(
                match_id,owner_instance_id,status,last_activity_utc,retention_anchor_utc)
            VALUES($match,$owner,'active',$utc,NULL);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$owner", _sandboxRecorderInstanceId);
        command.Parameters.AddWithValue("$utc", startedUtc);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task TouchSandboxRecordingAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId, string occurredUtc)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE sandbox_recordings SET last_activity_utc=$utc
            WHERE match_id=$match AND status='active';
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$utc", occurredUtc);
        await command.ExecuteNonQueryAsync();
    }

    internal Task<bool> CompleteSandboxAsync(L12GameEngine engine)
    {
        if (engine.State.Phase != L12Phase.GameOver)
            throw new InvalidOperationException("只能以已结束状态完成沙盒录像");
        return FinalizeSandboxAsync(engine);
    }

    internal Task<bool> CloseSandboxAsync(L12GameEngine engine) => FinalizeSandboxAsync(engine);

    internal async Task<bool> AbandonSandboxAsync(L12GameEngine engine, string reason)
    {
        var now = _utcNow().ToUniversalTime().ToString("O");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var metadata = connection.CreateCommand();
        metadata.Transaction = transaction;
        metadata.CommandText = """
            UPDATE sandbox_recordings
            SET status='abandoned',owner_instance_id='',
                retention_anchor_utc=last_activity_utc
            WHERE match_id=$match AND status='active';
            """;
        metadata.Parameters.AddWithValue("$match", engine.State.MatchId);
        var changed = await metadata.ExecuteNonQueryAsync();
        if (changed == 1)
        {
            var match = connection.CreateCommand();
            match.Transaction = transaction;
            match.CommandText = """
                UPDATE matches
                SET ended_utc=COALESCE((
                    SELECT retention_anchor_utc FROM sandbox_recordings s WHERE s.match_id=matches.match_id
                    ),$now),
                    winner=$winner,
                    final_hash=(SELECT state_hash FROM match_events e
                                WHERE e.match_id=matches.match_id ORDER BY sequence DESC LIMIT 1),
                    first_player=$first,error=COALESCE(error,$reason)
                WHERE match_id=$match AND mode_id='sandbox' AND ended_utc IS NULL;
                """;
            match.Parameters.AddWithValue("$now", now);
            match.Parameters.AddWithValue("$winner", (object?)engine.State.Winner ?? DBNull.Value);
            match.Parameters.AddWithValue("$first", engine.State.FirstPlayer);
            match.Parameters.AddWithValue("$reason", reason);
            match.Parameters.AddWithValue("$match", engine.State.MatchId);
            await match.ExecuteNonQueryAsync();
        }
        StorageFailureInjector?.Invoke("before-sandbox-abandon-commit");
        await transaction.CommitAsync();
        return changed == 1;
    }

    private async Task<bool> FinalizeSandboxAsync(L12GameEngine engine)
    {
        var now = _utcNow().ToUniversalTime().ToString("O");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var metadata = connection.CreateCommand();
        metadata.Transaction = transaction;
        metadata.CommandText = """
            UPDATE sandbox_recordings
            SET status='completed',owner_instance_id='',last_activity_utc=$utc,retention_anchor_utc=$utc
            WHERE match_id=$match AND status='active';
            """;
        metadata.Parameters.AddWithValue("$match", engine.State.MatchId);
        metadata.Parameters.AddWithValue("$utc", now);
        var changed = await metadata.ExecuteNonQueryAsync();

        var match = connection.CreateCommand();
        match.Transaction = transaction;
        match.CommandText = """
            UPDATE matches SET ended_utc=$utc,winner=$winner,final_hash=$hash,first_player=$first
            WHERE match_id=$match AND mode_id='sandbox' AND ended_utc IS NULL;
            """;
        match.Parameters.AddWithValue("$utc", now);
        match.Parameters.AddWithValue("$winner", (object?)engine.State.Winner ?? DBNull.Value);
        match.Parameters.AddWithValue("$hash", engine.ComputeStateHash());
        match.Parameters.AddWithValue("$first", engine.State.FirstPlayer);
        match.Parameters.AddWithValue("$match", engine.State.MatchId);
        await match.ExecuteNonQueryAsync();

        if (changed == 0)
        {
            var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = """
                SELECT COUNT(*) FROM sandbox_recordings s
                JOIN matches m ON m.match_id=s.match_id
                WHERE s.match_id=$match AND m.mode_id='sandbox';
                """;
            existing.Parameters.AddWithValue("$match", engine.State.MatchId);
            if (Convert.ToInt64(await existing.ExecuteScalarAsync()) != 1)
                throw new KeyNotFoundException("找不到待结束的沙盒录像");
        }

        await transaction.CommitAsync();
        return changed == 1;
    }

    public async Task<L12SandboxReplayCleanupResult> RunSandboxReplayCleanupIfDueAsync(
        IReadOnlyCollection<string>? activeMatchIds = null, DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = (utcNow ?? _utcNow()).ToUniversalTime();
        var cachedNextTicks = Volatile.Read(ref _nextSandboxCleanupUtcTicks);
        if (now.UtcDateTime.Ticks < cachedNextTicks)
            return new L12SandboxReplayCleanupResult(false, 0, null,
                new DateTimeOffset(cachedNextTicks, TimeSpan.Zero));

        await _sandboxCleanupGate.WaitAsync(cancellationToken);
        var leaseClaimed = false;
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            DateTimeOffset lastRun;
            DateTimeOffset persistedNext;
            using (var claim = connection.BeginTransaction(deferred: false))
            {
                var schedule = connection.CreateCommand();
                schedule.Transaction = claim;
                schedule.CommandText = """
                    SELECT last_run_utc,next_run_utc,lease_owner,lease_expires_utc
                    FROM sandbox_replay_cleanup_schedule WHERE singleton_id=1;
                    """;
                string lastRunText;
                string nextRunText;
                string? leaseOwner;
                string? leaseExpiresText;
                await using (var reader = await schedule.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                        throw new InvalidDataException("沙盒录像清理计划缺失");
                    lastRunText = reader.GetString(0);
                    nextRunText = reader.GetString(1);
                    leaseOwner = reader.IsDBNull(2) ? null : reader.GetString(2);
                    leaseExpiresText = reader.IsDBNull(3) ? null : reader.GetString(3);
                }
                if (!TryParseUtc(lastRunText, out lastRun) || !TryParseUtc(nextRunText, out persistedNext))
                    throw new InvalidDataException("沙盒录像清理计划时间无效");
                Volatile.Write(ref _nextSandboxCleanupUtcTicks, persistedNext.UtcDateTime.Ticks);
                if (now < persistedNext)
                {
                    await claim.CommitAsync(cancellationToken);
                    return new L12SandboxReplayCleanupResult(false, 0, lastRun, persistedNext);
                }

                if (!string.IsNullOrWhiteSpace(leaseOwner) && TryParseUtc(leaseExpiresText, out var leaseExpires)
                    && leaseExpires > now)
                {
                    await claim.CommitAsync(cancellationToken);
                    return new L12SandboxReplayCleanupResult(false, 0, lastRun, persistedNext);
                }

                var acquire = connection.CreateCommand();
                acquire.Transaction = claim;
                acquire.CommandText = """
                    UPDATE sandbox_replay_cleanup_schedule
                    SET lease_owner=$owner,lease_expires_utc=$expires WHERE singleton_id=1;
                    """;
                acquire.Parameters.AddWithValue("$owner", _sandboxRecorderInstanceId);
                acquire.Parameters.AddWithValue("$expires", now.Add(SandboxReplayCleanupLease).ToString("O"));
                await acquire.ExecuteNonQueryAsync(cancellationToken);
                await claim.CommitAsync(cancellationToken);
                leaseClaimed = true;
            }

            var active = (activeMatchIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);
            var cutoff = now.Subtract(SandboxReplayRetention).ToString("O");
            var candidates = connection.CreateCommand();
            candidates.CommandText = """
                SELECT s.match_id
                FROM sandbox_recordings s
                JOIN matches m ON m.match_id=s.match_id AND m.mode_id='sandbox'
                WHERE s.status IN ('completed','abandoned')
                  AND s.retention_anchor_utc IS NOT NULL
                  AND julianday(s.retention_anchor_utc) < julianday($cutoff)
                  AND NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=s.match_id)
                  AND NOT EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=s.match_id)
                  AND NOT EXISTS(SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=s.match_id)
                ORDER BY s.match_id;
                """;
            candidates.Parameters.AddWithValue("$cutoff", cutoff);
            var expired = new List<string>();
            await using (var reader = await candidates.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken))
                    if (!active.Contains(reader.GetString(0))) expired.Add(reader.GetString(0));

            var anomalies = connection.CreateCommand();
            anomalies.CommandText = """
                SELECT COUNT(*)
                FROM sandbox_recordings s
                JOIN matches m ON m.match_id=s.match_id AND m.mode_id='sandbox'
                WHERE s.status IN ('completed','abandoned')
                  AND s.retention_anchor_utc IS NOT NULL
                  AND julianday(s.retention_anchor_utc) < julianday($cutoff)
                  AND (EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=s.match_id)
                       OR EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=s.match_id)
                       OR EXISTS(SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=s.match_id));
                """;
            anomalies.Parameters.AddWithValue("$cutoff", cutoff);
            var anomalyCount = Convert.ToInt64(await anomalies.ExecuteScalarAsync(cancellationToken));
            if (anomalyCount > 0)
                Console.Error.WriteLine($"Sandbox replay cleanup preserved {anomalyCount} row(s) with ranked persistence references.");

            var deleted = 0;
            foreach (var matchId in expired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var purgeTransaction = connection.BeginTransaction(deferred: false);
                var stillEligible = connection.CreateCommand();
                stillEligible.Transaction = purgeTransaction;
                stillEligible.CommandText = """
                    SELECT COUNT(*) FROM sandbox_recordings s
                    JOIN matches m ON m.match_id=s.match_id AND m.mode_id='sandbox'
                    WHERE s.match_id=$match AND s.status IN ('completed','abandoned')
                      AND s.retention_anchor_utc IS NOT NULL
                      AND julianday(s.retention_anchor_utc) < julianday($cutoff)
                      AND NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=s.match_id)
                      AND NOT EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=s.match_id)
                      AND NOT EXISTS(SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=s.match_id);
                    """;
                stillEligible.Parameters.AddWithValue("$match", matchId);
                stillEligible.Parameters.AddWithValue("$cutoff", cutoff);
                if (active.Contains(matchId) || Convert.ToInt64(
                        await stillEligible.ExecuteScalarAsync(cancellationToken)) != 1)
                {
                    await purgeTransaction.CommitAsync(cancellationToken);
                    continue;
                }

                var purge = connection.CreateCommand();
                purge.Transaction = purgeTransaction;
                purge.CommandText = """
                    INSERT INTO sandbox_replay_expirations(match_id,expired_utc) VALUES($match,$utc)
                    ON CONFLICT(match_id) DO UPDATE SET expired_utc=excluded.expired_utc;
                    DELETE FROM match_card_facts WHERE match_id=$match;
                    DELETE FROM match_deck_cards WHERE match_id=$match;
                    DELETE FROM match_participants WHERE match_id=$match;
                    DELETE FROM match_events WHERE match_id=$match;
                    DELETE FROM sandbox_recordings WHERE match_id=$match;
                    """;
                purge.Parameters.AddWithValue("$match", matchId);
                purge.Parameters.AddWithValue("$utc", now.ToString("O"));
                await purge.ExecuteNonQueryAsync(cancellationToken);
                var deleteMatch = connection.CreateCommand();
                deleteMatch.Transaction = purgeTransaction;
                deleteMatch.CommandText = "DELETE FROM matches WHERE match_id=$match AND mode_id='sandbox';";
                deleteMatch.Parameters.AddWithValue("$match", matchId);
                deleted += await deleteMatch.ExecuteNonQueryAsync(cancellationToken);

                var refreshLease = connection.CreateCommand();
                refreshLease.Transaction = purgeTransaction;
                refreshLease.CommandText = """
                    UPDATE sandbox_replay_cleanup_schedule SET lease_expires_utc=$expires
                    WHERE singleton_id=1 AND lease_owner=$owner;
                    """;
                refreshLease.Parameters.AddWithValue("$owner", _sandboxRecorderInstanceId);
                var leaseNow = _utcNow().ToUniversalTime();
                if (leaseNow < now) leaseNow = now;
                refreshLease.Parameters.AddWithValue("$expires",
                    leaseNow.Add(SandboxReplayCleanupLease).ToString("O"));
                if (await refreshLease.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("沙盒录像清理租约已丢失");
                await purgeTransaction.CommitAsync(cancellationToken);
            }

            var nextRun = now.Add(SandboxReplayCleanupInterval);
            using (var finish = connection.BeginTransaction(deferred: false))
            {
                var updateSchedule = connection.CreateCommand();
                updateSchedule.Transaction = finish;
                updateSchedule.CommandText = """
                    UPDATE sandbox_replay_cleanup_schedule
                    SET last_run_utc=$last,next_run_utc=$next,lease_owner=NULL,lease_expires_utc=NULL
                    WHERE singleton_id=1 AND lease_owner=$owner;
                    """;
                updateSchedule.Parameters.AddWithValue("$last", now.ToString("O"));
                updateSchedule.Parameters.AddWithValue("$next", nextRun.ToString("O"));
                updateSchedule.Parameters.AddWithValue("$owner", _sandboxRecorderInstanceId);
                if (await updateSchedule.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("沙盒录像清理租约已丢失");
                await finish.CommitAsync(cancellationToken);
            }
            leaseClaimed = false;
            Volatile.Write(ref _nextSandboxCleanupUtcTicks, nextRun.UtcDateTime.Ticks);
            return new L12SandboxReplayCleanupResult(true, deleted, now, nextRun);
        }
        catch
        {
            if (leaseClaimed)
            {
                try
                {
                    await using var recovery = new SqliteConnection(_connectionString);
                    await recovery.OpenAsync(CancellationToken.None);
                    using var release = recovery.BeginTransaction(deferred: false);
                    var command = recovery.CreateCommand();
                    command.Transaction = release;
                    command.CommandText = """
                        UPDATE sandbox_replay_cleanup_schedule
                        SET lease_owner=NULL,lease_expires_utc=NULL
                        WHERE singleton_id=1 AND lease_owner=$owner;
                        """;
                    command.Parameters.AddWithValue("$owner", _sandboxRecorderInstanceId);
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                    await release.CommitAsync(CancellationToken.None);
                    Volatile.Write(ref _nextSandboxCleanupUtcTicks, now.UtcDateTime.Ticks);
                }
                catch (Exception releaseError)
                {
                    Console.Error.WriteLine($"Sandbox replay cleanup lease release: {releaseError.Message}");
                }
            }
            throw;
        }
        finally
        {
            _sandboxCleanupGate.Release();
        }
    }

    private static bool TryParseUtc(string? value, out DateTimeOffset parsed)
    {
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
        {
            parsed = parsed.ToUniversalTime();
            return true;
        }
        return false;
    }

    public async Task<bool> IsSandboxReplayExpiredAsync(string matchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(matchId)) return false;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sandbox_replay_expirations WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId.Trim());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}

public sealed record L12SandboxReplayCleanupResult(
    bool Ran, int Deleted, DateTimeOffset? LastRunUtc, DateTimeOffset NextRunUtc);
