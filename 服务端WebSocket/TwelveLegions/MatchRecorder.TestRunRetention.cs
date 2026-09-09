using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public const string TestRunRuntimePath = "/opt/legion12-testrun-runtime";
    public const string ProductionRuntimePath = "/opt/legion12-runtime";
    private static StringComparison RetentionPathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Only the existing daily player-replay slice calls this method: no independent timer/retry.
    public async Task<L12PlayerReplayCleanupResult> RunTestRunReplayRetentionAsync(
        string environmentMarker, string publicBaseUrl, string testRuntimePath, string productionRuntimePath,
        IReadOnlyCollection<string>? activeMatchIds = null,
        Func<L12ReplayEvidenceReferences>? evidenceProvider = null,
        DateTimeOffset? utcNow = null, TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
        => await RunTestRunRetentionCoreAsync(environmentMarker, publicBaseUrl, testRuntimePath, productionRuntimePath,
            activeMatchIds, evidenceProvider, utcNow, budget, cancellationToken, enforceInstalledPaths: true);

    // Internal dependency boundary for cross-platform isolated fixtures. The service entry
    // above always enforces the installed Linux paths; no environment flag can disable it.
    internal async Task<L12PlayerReplayCleanupResult> RunTestRunRetentionCoreAsync(
        string environmentMarker, string publicBaseUrl, string testRuntimePath, string productionRuntimePath,
        IReadOnlyCollection<string>? activeMatchIds, Func<L12ReplayEvidenceReferences>? evidenceProvider,
        DateTimeOffset? utcNow, TimeSpan? budget, CancellationToken cancellationToken, bool enforceInstalledPaths)
    {
        var now = (utcNow ?? _utcNow()).ToUniversalTime();
        var next = NextStorageCleanupUtc(now);
        var database = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        if (!IsTestRunRetentionIsolatedCore(environmentMarker, publicBaseUrl,
                testRuntimePath, productionRuntimePath, database, enforceInstalledPaths)
            || evidenceProvider is null || activeMatchIds is null)
            return new(false, 0, 0, 0, false, null, next);
        var limit = budget ?? TimeSpan.FromSeconds(3);
        if (limit <= TimeSpan.Zero) return new(false, 0, 0, 0, true, null, next);
        if (limit > TimeSpan.FromSeconds(3)) limit = TimeSpan.FromSeconds(3);
        var elapsed = Stopwatch.StartNew();
        await _playerReplayCleanupGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenWriteConnectionAsync(cancellationToken);
            var schema = connection.CreateCommand();
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS testrun_replay_retention_cursor (
                    singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1), last_rowid INTEGER NOT NULL DEFAULT 0);
                INSERT OR IGNORE INTO testrun_replay_retention_cursor(singleton_id) VALUES(1);
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
            var evidence = evidenceProvider() ?? throw new InvalidDataException("Bug 回放证据引用快照缺失");
            var heldMatches = NormalizeReplayHolds(evidence.MatchIds);
            var heldRooms = NormalizeReplayHolds(evidence.RoomCodes);
            await PopulateReplayCleanupHoldsAsync(connection,
                NormalizeReplayHolds(activeMatchIds).Concat(heldMatches), heldRooms, cancellationToken);
            int visited = 0, purged = 0;
            long commands = 0, bytes = 0;
            bool more = true;
            while (visited < PlayerReplayCleanupMaximumMatchesPerRun && elapsed.Elapsed < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = connection.CreateCommand();
                // Bounded indexed metadata walk, including protected/recent rows; no payload scan.
                read.CommandText = """
                    SELECT rowid,match_id FROM matches
                    WHERE rowid > (SELECT last_rowid FROM testrun_replay_retention_cursor WHERE singleton_id=1)
                    ORDER BY rowid LIMIT 25;
                    """;
                var batch = new List<(long RowId, string MatchId)>();
                await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
                    while (await reader.ReadAsync(cancellationToken)) batch.Add((reader.GetInt64(0), reader.GetString(1)));
                if (batch.Count == 0)
                {
                    var reset = connection.CreateCommand();
                    reset.CommandText = "UPDATE testrun_replay_retention_cursor SET last_rowid=0 WHERE singleton_id=1;";
                    await reset.ExecuteNonQueryAsync(cancellationToken);
                    more = false;
                    break;
                }
                foreach (var item in batch)
                {
                    if (elapsed.Elapsed >= limit) break;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsTestRunRetentionIsolatedCore(environmentMarker, publicBaseUrl,
                            testRuntimePath, productionRuntimePath, database, enforceInstalledPaths))
                        throw new InvalidDataException("测试回放存储物理隔离已改变");
                    using var transaction = connection.BeginTransaction(deferred: false);
                    await MergeReplayEvidenceHoldsAsync(connection, transaction,
                        evidenceProvider() ?? throw new InvalidDataException("Bug 回放证据引用快照缺失"),
                        heldMatches, heldRooms, cancellationToken);
                    if (await IsTestRunPurgeEligibleAsync(connection, transaction, item.MatchId,
                            now.AddDays(-14), cancellationToken))
                    {
                        var metrics = await ReadPlayerReplayPayloadMetricsAsync(connection, transaction,
                            item.MatchId, cancellationToken);
                        await PurgeReplayPayloadAsync(connection, transaction, item.MatchId, now,
                            metrics.CommandCount, metrics.PayloadBytes, cancellationToken);
                        var facts = connection.CreateCommand();
                        facts.Transaction = transaction;
                        facts.CommandText = "DELETE FROM match_card_facts WHERE match_id=$match;";
                        facts.Parameters.AddWithValue("$match", item.MatchId);
                        await facts.ExecuteNonQueryAsync(cancellationToken);
                        StorageFailureInjector?.Invoke("before-testrun-replay-purge-commit");
                        purged++;
                        commands += metrics.CommandCount;
                        bytes += metrics.PayloadBytes;
                    }
                    var advance = connection.CreateCommand();
                    advance.Transaction = transaction;
                    advance.CommandText = "UPDATE testrun_replay_retention_cursor SET last_rowid=MAX(last_rowid,$row) WHERE singleton_id=1;";
                    advance.Parameters.AddWithValue("$row", item.RowId);
                    await advance.ExecuteNonQueryAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    visited++;
                }
            }
            Console.WriteLine($"Test replay retention: visited={visited};pruned={purged};scanRemaining={more};budgetReached={elapsed.Elapsed >= limit}");
            // Conservative metadata backlog, not a claim all unvisited matches are eligible.
            return new(true, purged, commands, bytes, more, now, next);
        }
        finally { _playerReplayCleanupGate.Release(); }
    }

    internal static bool IsTestRunRetentionIsolated(string marker, string publicUrl,
        string testRuntime, string productionRuntime, string database)
        => IsTestRunRetentionIsolatedCore(marker,publicUrl,testRuntime,productionRuntime,database,true);

    internal static bool IsTestRunRetentionIsolatedCore(string marker, string publicUrl,
        string testRuntime, string productionRuntime, string database, bool enforceInstalledPaths)
    {
        if (marker != "testrun" || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "testrun.legion-12.com"
            || !uri.IsDefaultPort || uri.AbsolutePath != "/" || uri.Query != ""
            || uri.Fragment != "" || uri.UserInfo != "") return false;
        try
        {
            // Fixed production Linux paths; Windows isolated fixture directories keep the same leaf names.
            if (enforceInstalledPaths && !OperatingSystem.IsWindows()
                && (testRuntime != TestRunRuntimePath || productionRuntime != ProductionRuntimePath)) return false;
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(testRuntime)) != "legion12-testrun-runtime"
                || Path.GetFileName(Path.TrimEndingDirectorySeparator(productionRuntime)) != "legion12-runtime") return false;
            var physical = ResolveRetentionPhysicalPath(testRuntime);
            // Reject runtime/ancestor redirection, even to a different non-production directory.
            if (!string.Equals(Path.GetFullPath(testRuntime), physical, RetentionPathComparison)) return false;
            var db = ResolveRetentionPhysicalPath(database);
            // The installed release's publish/runtime is deliberately a link into this
            // isolated runtime. Resolve that alias, but never accept a DB-file symlink.
            if (new FileInfo(database).LinkTarget is not null || Path.GetFileName(db) != "matches.db") return false;
            var production = Path.GetFullPath(productionRuntime);
            // The test service deliberately has InaccessiblePaths for production runtime.
            // Its lexical exclusion remains mandatory even when metadata is inaccessible.
            if (Directory.Exists(productionRuntime)) production = ResolveRetentionPhysicalPath(productionRuntime);
            return IsRetentionDescendant(db, physical) && !IsRetentionDescendant(db, production)
                && !IsRetentionDescendant(physical, production) && !IsRetentionDescendant(production, physical)
                && !string.Equals(physical, production, RetentionPathComparison);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                     or NotSupportedException or System.Security.SecurityException)
        { return false; }
    }

    private static bool IsRetentionDescendant(string child, string parent)
        => child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar,
            RetentionPathComparison);

    private static string ResolveRetentionPhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists) throw new IOException("存储隔离路径不存在");
            if (info.LinkTarget is not null)
                current = (info.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException("存储隔离链接无法解析")).FullName;
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static async Task<bool> IsTestRunPurgeEligibleAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT m.ended_utc,o.payload_json,o.payload_hash,m.mode_id
            FROM matches m LEFT JOIN ranked_settlement_outbox o ON o.match_id=m.match_id
            WHERE m.match_id=$match AND m.ended_utc IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM player_replay_payload_expirations x WHERE x.match_id=m.match_id)
              AND NOT EXISTS(SELECT 1 FROM player_replay_cleanup_match_holds h WHERE h.value=m.match_id)
              AND NOT EXISTS(SELECT 1 FROM player_replay_cleanup_room_holds h WHERE h.value=m.room_code)
              AND NOT EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=m.match_id AND r.status<>'completed')
              AND NOT EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=m.match_id AND o.match_id IS NULL)
              AND NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox p WHERE p.match_id=m.match_id AND p.status<>'applied')
              AND NOT EXISTS(SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=m.match_id)
              AND NOT EXISTS(SELECT 1 FROM sandbox_recordings s WHERE s.match_id=m.match_id AND s.status NOT IN ('completed','abandoned'));
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !TryParseUtc(reader.GetString(0), out var ended)
            || ended >= cutoff) return false;
        if (reader.IsDBNull(1)) return reader.GetString(3) != "ranked";
        try
        {
            var json = reader.GetString(1);
            if (!string.Equals(PersistenceHash(json), reader.GetString(2), StringComparison.OrdinalIgnoreCase)) return false;
            var payload = JsonSerializer.Deserialize<L12RankedSettlementEnvelope>(json, RankedPersistenceJson);
            if (payload is null || payload.MatchId != matchId || payload.FinalRound <= 0) return false;
            ValidateSettlement(payload);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
        { return false; }
    }

    // Shared atomic mutation; callers own their different eligibility policies and transaction.
    private static async Task PurgeReplayPayloadAsync(SqliteConnection connection, SqliteTransaction transaction,
        string matchId, DateTimeOffset now, long commands, long bytes, CancellationToken cancellationToken)
    {
        var purge = connection.CreateCommand();
        purge.Transaction = transaction;
        purge.CommandText = """
            UPDATE matches SET initial_state_json=NULL WHERE match_id=$match;
            UPDATE match_events SET state_json='{}' WHERE match_id=$match AND state_json<>'{}';
            DELETE FROM match_action_requests WHERE match_id=$match;
            DELETE FROM match_action_events WHERE match_id=$match;
            DELETE FROM match_state_checkpoints WHERE match_id=$match;
            INSERT INTO player_replay_payload_expirations(match_id,expired_utc,retained_command_count,cleared_payload_bytes)
            VALUES($match,$utc,$commands,$bytes);
            """;
        purge.Parameters.AddWithValue("$match", matchId);
        purge.Parameters.AddWithValue("$utc", now.ToString("O"));
        purge.Parameters.AddWithValue("$commands", commands);
        purge.Parameters.AddWithValue("$bytes", bytes);
        await purge.ExecuteNonQueryAsync(cancellationToken);
    }
}
