using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    internal static readonly TimeSpan DetailedCardFactRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan CardFactMaintenanceInterval = TimeSpan.FromDays(1);
    private readonly SemaphoreSlim _cardFactMaintenanceGate = new(1, 1);
    private long _nextCardFactMaintenanceUtcTicks = DateTimeOffset.MinValue.UtcDateTime.Ticks;

    private static async Task InitializeAnalyticsCompactionSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS match_card_fact_summaries (
                match_id TEXT NOT NULL,
                player_index INTEGER NOT NULL,
                card_id TEXT NOT NULL,
                drawn INTEGER NOT NULL,
                played INTEGER NOT NULL,
                activated_sample INTEGER NOT NULL,
                settled_sample INTEGER NOT NULL,
                resolved_sample INTEGER NOT NULL,
                negated_sample INTEGER NOT NULL,
                fizzled_sample INTEGER NOT NULL,
                observed_sample INTEGER NOT NULL,
                first_draw_turn INTEGER,
                first_play_turn INTEGER,
                activated INTEGER NOT NULL,
                resolved INTEGER NOT NULL,
                negated INTEGER NOT NULL,
                fizzled INTEGER NOT NULL,
                exact_facts INTEGER NOT NULL,
                inferred_facts INTEGER NOT NULL,
                partial_facts INTEGER NOT NULL,
                draw_exact INTEGER NOT NULL,
                draw_inferred INTEGER NOT NULL,
                draw_partial INTEGER NOT NULL,
                play_exact INTEGER NOT NULL,
                play_inferred INTEGER NOT NULL,
                play_partial INTEGER NOT NULL,
                activation_exact INTEGER NOT NULL,
                activation_inferred INTEGER NOT NULL,
                activation_partial INTEGER NOT NULL,
                settlement_exact INTEGER NOT NULL,
                settlement_inferred INTEGER NOT NULL,
                settlement_partial INTEGER NOT NULL,
                PRIMARY KEY(match_id,player_index,card_id),
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_match_card_fact_summaries_card
                ON match_card_fact_summaries(card_id,match_id,player_index);
            CREATE TABLE IF NOT EXISTS match_card_fact_compactions (
                match_id TEXT PRIMARY KEY,
                summary_schema_version INTEGER NOT NULL,
                source_fact_count INTEGER NOT NULL,
                source_fact_max_id INTEGER NOT NULL,
                compacted_utc TEXT NOT NULL,
                details_pruned_utc TEXT,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_match_card_fact_compactions_prune
                ON match_card_fact_compactions(details_pruned_utc,compacted_utc,match_id);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CompactCardFactsForMatchAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId, string compactedUtc)
    {
        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM match_card_fact_summaries WHERE match_id=$match;";
        clear.Parameters.AddWithValue("$match", matchId);
        await clear.ExecuteNonQueryAsync();

        var summarize = connection.CreateCommand();
        summarize.Transaction = transaction;
        summarize.CommandText = """
            INSERT INTO match_card_fact_summaries(
                match_id,player_index,card_id,drawn,played,activated_sample,settled_sample,
                resolved_sample,negated_sample,fizzled_sample,observed_sample,first_draw_turn,
                first_play_turn,activated,resolved,negated,fizzled,exact_facts,inferred_facts,
                partial_facts,draw_exact,draw_inferred,draw_partial,play_exact,play_inferred,
                play_partial,activation_exact,activation_inferred,activation_partial,
                settlement_exact,settlement_inferred,settlement_partial)
            SELECT f.match_id,f.player_index,f.card_id,
                   MAX(CASE WHEN f.kind='draw' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind='play' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind='activate' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind='resolve' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind='negate' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind='fizzle' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   MAX(CASE WHEN f.kind IN ('draw','play','activate','resolve','negate','fizzle') THEN 1 ELSE 0 END),
                   MIN(CASE WHEN f.kind='draw' AND f.coverage='exact' THEN f.turn END),
                   MIN(CASE WHEN f.kind='play' AND f.coverage='exact' THEN f.turn END),
                   SUM(CASE WHEN f.kind='activate' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='resolve' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='negate' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='fizzle' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.coverage='exact' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.coverage='inferred' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.coverage='partial' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='draw' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='draw' AND f.coverage='inferred' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='draw' AND f.coverage='partial' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='play' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='play' AND f.coverage='inferred' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='play' AND f.coverage='partial' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='activate' AND f.coverage='exact' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='activate' AND f.coverage='inferred' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind='activate' AND f.coverage='partial' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='exact' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='inferred' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='partial' THEN 1 ELSE 0 END)
            FROM match_card_facts f
            JOIN match_deck_cards d
              ON d.match_id=f.match_id AND d.player_index=f.player_index AND d.card_id=f.card_id
            WHERE f.match_id=$match AND f.player_index IS NOT NULL AND f.card_id IS NOT NULL
              AND f.kind<>'deck-included'
            GROUP BY f.match_id,f.player_index,f.card_id;
            """;
        summarize.Parameters.AddWithValue("$match", matchId);
        await summarize.ExecuteNonQueryAsync();

        var mark = connection.CreateCommand();
        mark.Transaction = transaction;
        mark.CommandText = """
            INSERT INTO match_card_fact_compactions(
                match_id,summary_schema_version,source_fact_count,source_fact_max_id,compacted_utc)
            SELECT $match,1,COUNT(*),COALESCE(MAX(id),0),$utc
            FROM match_card_facts WHERE match_id=$match
            ON CONFLICT(match_id) DO UPDATE SET
                summary_schema_version=excluded.summary_schema_version,
                source_fact_count=excluded.source_fact_count,
                source_fact_max_id=excluded.source_fact_max_id,
                compacted_utc=excluded.compacted_utc,
                details_pruned_utc=NULL;
            """;
        mark.Parameters.AddWithValue("$match", matchId);
        mark.Parameters.AddWithValue("$utc", compactedUtc);
        await mark.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasCardFactCompactionAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM match_card_fact_compactions WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    internal async Task<int> CompactCompletedCardFactsBatchAsync(int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenWriteConnectionAsync(cancellationToken);
        var select = connection.CreateCommand();
        select.CommandText = """
            SELECT m.match_id
            FROM matches m
            WHERE m.mode_id<>'sandbox' AND m.ended_utc IS NOT NULL AND m.error IS NULL
              AND m.winner IN (0,1)
              AND NOT EXISTS(SELECT 1 FROM match_card_fact_compactions c WHERE c.match_id=m.match_id)
            ORDER BY m.ended_utc,m.match_id
            LIMIT $limit;
            """;
        select.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var matchIds = new List<string>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) matchIds.Add(reader.GetString(0));

        var compacted = 0;
        foreach (var matchId in matchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await CompactCardFactsForMatchAsync(connection, transaction, matchId,
                _utcNow().ToUniversalTime().ToString("O"));
            await transaction.CommitAsync(cancellationToken);
            compacted++;
        }
        return compacted;
    }

    internal async Task<int> PruneCompactedCardFactDetailsAsync(DateTimeOffset? utcNow = null,
        int limit = 100, CancellationToken cancellationToken = default)
    {
        var cutoff = (utcNow ?? _utcNow()).ToUniversalTime().Subtract(DetailedCardFactRetention)
            .ToString("O");
        await using var connection = await OpenWriteConnectionAsync(cancellationToken);
        var select = connection.CreateCommand();
        select.CommandText = """
            SELECT c.match_id
            FROM match_card_fact_compactions c
            JOIN matches m ON m.match_id=c.match_id
            WHERE c.details_pruned_utc IS NULL AND m.mode_id<>'sandbox'
              AND m.ended_utc IS NOT NULL AND m.ended_utc<$cutoff
            ORDER BY m.ended_utc,m.match_id
            LIMIT $limit;
            """;
        select.Parameters.AddWithValue("$cutoff", cutoff);
        select.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var matchIds = new List<string>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) matchIds.Add(reader.GetString(0));

        var pruned = 0;
        foreach (var matchId in matchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM match_card_facts WHERE match_id=$match;";
            delete.Parameters.AddWithValue("$match", matchId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            var mark = connection.CreateCommand();
            mark.Transaction = transaction;
            mark.CommandText = """
                UPDATE match_card_fact_compactions SET details_pruned_utc=$utc
                WHERE match_id=$match AND details_pruned_utc IS NULL;
                """;
            mark.Parameters.AddWithValue("$utc", (utcNow ?? _utcNow()).ToUniversalTime().ToString("O"));
            mark.Parameters.AddWithValue("$match", matchId);
            if (await mark.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("单卡事实清理标记冲突");
            await transaction.CommitAsync(cancellationToken);
            pruned++;
        }
        return pruned;
    }

    internal async Task<(int Compacted, int Pruned)> RunCardFactStorageMaintenanceIfDueAsync(
        DateTimeOffset? utcNow = null, CancellationToken cancellationToken = default)
    {
        var now = (utcNow ?? _utcNow()).ToUniversalTime();
        if (now.UtcDateTime.Ticks < Volatile.Read(ref _nextCardFactMaintenanceUtcTicks))
            return (0, 0);

        await _cardFactMaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            if (now.UtcDateTime.Ticks < Volatile.Read(ref _nextCardFactMaintenanceUtcTicks))
                return (0, 0);
            // 历史回填严格有界，避免在大库启动时形成长事务；新结束对局已同步紧凑化。
            var compacted = await CompactCompletedCardFactsBatchAsync(25, cancellationToken);
            var pruned = await PruneCompactedCardFactDetailsAsync(now, 100, cancellationToken);
            Volatile.Write(ref _nextCardFactMaintenanceUtcTicks,
                now.Add(CardFactMaintenanceInterval).UtcDateTime.Ticks);
            return (compacted, pruned);
        }
        finally
        {
            _cardFactMaintenanceGate.Release();
        }
    }
}
