using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

/// <summary>One bounded read snapshot. It never grants a stop permit by itself.</summary>
internal sealed record L12DeploymentDurabilityReadiness(bool Verified,
    IReadOnlyDictionary<string, long> Blockers, string FailureCode, int Queries)
{
    internal bool Clear => Verified && Blockers.Values.All(value => value == 0);
    internal bool GrantsStopPermit => false;
}

public sealed partial class MatchRecorder
{
    private static readonly IReadOnlyDictionary<string, string> DeploymentSchema =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["matches"] = "match_id:TEXT room_code:TEXT mode_id:TEXT started_utc:TEXT ended_utc:TEXT winner:INTEGER storage_version:INTEGER hash_version:INTEGER",
            ["ranked_match_runtime"] = "match_id:TEXT room_code:TEXT status:TEXT",
            ["ranked_settlement_outbox"] = "match_id:TEXT status:TEXT last_error:TEXT applied_utc:TEXT",
            ["ranked_recovery_quarantine"] = "match_id:TEXT",
            ["tournament_result_outbox"] = "match_id:TEXT status:TEXT last_error:TEXT applied_utc:TEXT tournament_id:TEXT tournament_match_id:TEXT winner:INTEGER",
            ["response_preference_outbox"] = "match_id:TEXT journal_sequence:INTEGER status:TEXT last_error:TEXT applied_utc:TEXT",
            ["sandbox_recordings"] = "match_id:TEXT status:TEXT retention_anchor_utc:TEXT",
            ["match_state_checkpoints"] = "match_id:TEXT sequence:INTEGER created_utc:TEXT",
            ["match_action_events"] = "match_id:TEXT command_sequence:INTEGER created_utc:TEXT",
            ["match_action_requests"] = "match_id:TEXT command_sequence:INTEGER accepted:INTEGER created_utc:TEXT",
            ["match_events"] = "match_id:TEXT sequence:INTEGER received_utc:TEXT",
            ["player_replay_payload_expirations"] = "match_id:TEXT expired_utc:TEXT retained_command_count:INTEGER cleared_payload_bytes:INTEGER",
        });

    private static readonly IReadOnlyDictionary<string, string> DeploymentQueries =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unfinished_matches"] = "SELECT COUNT(*) FROM matches WHERE ended_utc IS NULL OR trim(ended_utc)='';",
            ["invalid_metadata"] = """
                SELECT COUNT(*) FROM matches WHERE match_id IS NULL OR trim(match_id)=''
                  OR room_code IS NULL OR trim(room_code)='' OR julianday(started_utc) IS NULL
                  OR (ended_utc IS NOT NULL AND (julianday(ended_utc) IS NULL
                    OR julianday(ended_utc)<julianday(started_utc)));
                """,
            ["unknown_modes"] = "SELECT COUNT(*) FROM matches WHERE mode_id IS NULL OR mode_id NOT IN ('legacy','friendly','casual','ranked','tournament','sandbox');",
            ["unsupported_versions"] = "SELECT COUNT(*) FROM matches WHERE storage_version IS NULL OR hash_version IS NULL OR NOT ((storage_version=1 AND hash_version=1) OR (storage_version=2 AND hash_version=2));",
            ["active_or_unknown_runtime"] = "SELECT COUNT(*) FROM ranked_match_runtime WHERE status IS NULL OR status<>'completed';",
            ["runtime_inconsistent"] = """
                SELECT COUNT(*) FROM ranked_match_runtime r LEFT JOIN matches m ON m.match_id=r.match_id
                WHERE m.match_id IS NULL OR r.room_code IS NULL OR trim(r.room_code)=''
                  OR r.room_code<>m.room_code OR m.mode_id NOT IN ('ranked','tournament')
                  OR (r.status='completed' AND (m.ended_utc IS NULL OR trim(m.ended_utc)=''
                    OR (m.mode_id='ranked' AND NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=r.match_id))
                    OR (m.mode_id='tournament' AND NOT EXISTS(SELECT 1 FROM tournament_result_outbox o WHERE o.match_id=r.match_id))));
                """,
            // A waived row remains blocked until its terminal metadata has a
            // separately implemented, versioned product contract. No blanket
            // historical exclusion is inferred from a past manual release.
            ["ranked_settlement_unresolved"] = """
                SELECT COUNT(*) FROM ranked_settlement_outbox o LEFT JOIN matches m ON m.match_id=o.match_id
                WHERE o.status IS NULL OR o.status<>'applied' OR o.last_error IS NOT NULL
                  OR julianday(o.applied_utc) IS NULL OR m.match_id IS NULL
                  OR m.mode_id<>'ranked' OR m.ended_utc IS NULL OR trim(m.ended_utc)='';
                """,
            ["quarantined_recovery"] = "SELECT COUNT(*) FROM ranked_recovery_quarantine;",
            ["tournament_settlement_unresolved"] = """
                SELECT COUNT(*) FROM tournament_result_outbox o LEFT JOIN matches m ON m.match_id=o.match_id
                WHERE o.status IS NULL OR o.status<>'applied' OR o.last_error IS NOT NULL
                  OR julianday(o.applied_utc) IS NULL OR m.match_id IS NULL
                  OR m.mode_id<>'tournament' OR m.ended_utc IS NULL OR trim(m.ended_utc)=''
                  OR trim(o.tournament_id)='' OR trim(o.tournament_match_id)=''
                  OR m.winner IS NULL OR o.winner<>m.winner OR o.winner NOT IN (0,1);
                """,
            ["response_outbox_unresolved"] = """
                SELECT COUNT(*) FROM response_preference_outbox o LEFT JOIN matches m ON m.match_id=o.match_id
                WHERE o.status IS NULL OR o.status<>'applied' OR o.last_error IS NOT NULL
                  OR julianday(o.applied_utc) IS NULL OR m.match_id IS NULL
                  OR o.journal_sequence IS NULL OR o.journal_sequence<0 OR (
                    NOT EXISTS(SELECT 1 FROM match_action_requests r WHERE r.match_id=o.match_id AND r.command_sequence=o.journal_sequence)
                    AND NOT EXISTS(SELECT 1 FROM match_action_events e WHERE e.match_id=o.match_id AND e.command_sequence=o.journal_sequence)
                    AND NOT EXISTS(SELECT 1 FROM match_events e WHERE e.match_id=o.match_id AND e.sequence=o.journal_sequence));
                """,
            ["sandbox_unresolved"] = """
                SELECT COUNT(*) FROM sandbox_recordings s LEFT JOIN matches m ON m.match_id=s.match_id
                WHERE s.status IS NULL OR s.status NOT IN ('completed','abandoned')
                  OR m.match_id IS NULL OR m.mode_id<>'sandbox'
                  OR m.ended_utc IS NULL OR trim(m.ended_utc)='' OR julianday(s.retention_anchor_utc) IS NULL
                  OR julianday(s.retention_anchor_utc)<>julianday(m.ended_utc);
                """,
            ["journal_orphans"] = """
                SELECT (SELECT COUNT(*) FROM match_state_checkpoints c WHERE c.sequence<0 OR NOT EXISTS(SELECT 1 FROM matches m WHERE m.match_id=c.match_id))
                  +(SELECT COUNT(*) FROM match_action_events e WHERE e.command_sequence<0 OR NOT EXISTS(SELECT 1 FROM matches m WHERE m.match_id=e.match_id))
                  +(SELECT COUNT(*) FROM match_action_requests r WHERE r.command_sequence<0 OR r.accepted NOT IN (0,1) OR NOT EXISTS(SELECT 1 FROM matches m WHERE m.match_id=r.match_id))
                  +(SELECT COUNT(*) FROM match_events e WHERE e.sequence<0 OR NOT EXISTS(SELECT 1 FROM matches m WHERE m.match_id=e.match_id));
                """,
            ["missing_v2_checkpoint"] = """
                SELECT COUNT(*) FROM matches m WHERE m.storage_version=2
                  AND NOT EXISTS(SELECT 1 FROM match_state_checkpoints c WHERE c.match_id=m.match_id)
                  AND NOT EXISTS(SELECT 1 FROM player_replay_payload_expirations x WHERE x.match_id=m.match_id
                    AND julianday(x.expired_utc)>=julianday(m.ended_utc) AND m.mode_id<>'sandbox'
                    AND x.retained_command_count>=0 AND x.cleared_payload_bytes>=0);
                """,
            ["expiration_inconsistent"] = """
                SELECT COUNT(*) FROM player_replay_payload_expirations x LEFT JOIN matches m ON m.match_id=x.match_id
                WHERE m.match_id IS NULL OR m.mode_id='sandbox' OR julianday(m.ended_utc) IS NULL
                  OR julianday(x.expired_utc) IS NULL OR julianday(x.expired_utc)<julianday(m.ended_utc)
                  OR x.retained_command_count<0 OR x.cleared_payload_bytes<0;
                """,
        });

    internal async Task<L12DeploymentDurabilityReadiness> DeploymentReadinessAsync(
        CancellationToken cancellationToken = default, TimeSpan? budget = null,
        Action<string>? testObserver = null)
    {
        var limit = budget ?? TimeSpan.FromSeconds(5);
        if (limit < TimeSpan.Zero || limit > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(budget));
        var clock = Stopwatch.StartNew();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var queries = 0;
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.Elapsed >= limit) throw new TimeoutException();
        }
        L12DeploymentDurabilityReadiness Result(bool verified, string code)
            => new(verified, new ReadOnlyDictionary<string, long>(counts), code, queries);
        try
        {
            CheckBudget();
            // This path does not Initialize, upgrade, repair or checkpoint the
            // database, and it refuses to create a missing database.
            var options = new SqliteConnectionStringBuilder(_connectionString)
            {
                Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private,
                Pooling = false, DefaultTimeout = 1,
            };
            await using var connection = new SqliteConnection(options.ToString());
            await connection.OpenAsync(cancellationToken);
            CheckBudget();
            SQLitePCL.delegate_progress progress = _ =>
                cancellationToken.IsCancellationRequested || clock.Elapsed >= limit ? 1 : 0;
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1000, progress, null!);
            try
            {
                using var transaction = connection.BeginTransaction(deferred: true);
                SqliteCommand Command(string sql)
                {
                    CheckBudget();
                    var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    command.CommandTimeout = 1;
                    queries++;
                    return command;
                }
                using (var version = Command("PRAGMA user_version;"))
                    if (Convert.ToInt64(await version.ExecuteScalarAsync(cancellationToken)) != 0)
                        return Result(false, "schema_invalid");
                foreach (var (table, columns) in DeploymentSchema)
                {
                    using var kind = Command("SELECT type FROM sqlite_schema WHERE name=$table;");
                    kind.Parameters.AddWithValue("$table", table);
                    if ((string?)await kind.ExecuteScalarAsync(cancellationToken) != "table")
                        return Result(false, "schema_invalid");
                    // Only fixed internal schema names are interpolated; no
                    // operator/user input ever becomes a SQL identifier.
                    using var schema = Command($"PRAGMA table_info('{table}');");
                    await using var fields = await schema.ExecuteReaderAsync(cancellationToken);
                    var actual = new Dictionary<string, string>(StringComparer.Ordinal);
                    while (await fields.ReadAsync(cancellationToken))
                        actual[fields.GetString(1)] = fields.GetString(2).ToUpperInvariant();
                    foreach (var column in columns.Split(' '))
                    {
                        var pair = column.Split(':');
                        if (!actual.TryGetValue(pair[0], out var type) || type != pair[1])
                            return Result(false, "schema_invalid");
                    }
                }
                testObserver?.Invoke("schema_verified");
                foreach (var (key, sql) in DeploymentQueries)
                {
                    using var command = Command(sql);
                    counts.Add(key, Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)));
                    CheckBudget();
                    testObserver?.Invoke(key);
                }
                CheckBudget();
                // Dispose rolls back this read-only transaction. There is no
                // write commit, no migration and no second read snapshot.
                return Result(true, "none");
            }
            finally
            {
                SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 0, null!, null!);
                GC.KeepAlive(progress);
            }
        }
        catch (OperationCanceledException) { return Result(false, "cancelled"); }
        catch (TimeoutException) { return Result(false, "query_budget"); }
        catch (SqliteException error)
        {
            return Result(false, cancellationToken.IsCancellationRequested ? "cancelled"
                : clock.Elapsed >= limit ? "query_budget"
                : error.SqliteErrorCode is 5 or 6 ? "sqlite_busy" : "read_failed");
        }
        catch (Exception) { return Result(false, "read_failed"); }
    }
}
