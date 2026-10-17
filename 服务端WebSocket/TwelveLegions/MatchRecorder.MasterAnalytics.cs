using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public Task<L12MasterAnalyticsReport> ReadMasterAnalyticsAsync(L12CardAnalyticsQuery query,
        CancellationToken cancellationToken = default)
        => ReadMasterAnalyticsAsync(query, cancellationToken, new CardAnalyticsRequestTiming());

    internal async Task<L12MasterAnalyticsReport> ReadMasterAnalyticsAsync(L12CardAnalyticsQuery query,
        CancellationToken cancellationToken, CardAnalyticsRequestTiming timing)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CardAnalyticsHardBudget);
        var epoch = AnalyticsCacheEpoch;
        try
        {
            var normalized = NormalizeAnalyticsQuery(query);
            var report = (await ExecuteCardAnalyticsAsync(AnalyticsResultCacheKey("master", normalized), epoch,
                budget.Token, timing, execution => ComputeMasterAnalytics(execution, normalized)))!;
            // The legacy card appendix is its own bounded query, not part of the core read snapshot.
            var cards = normalized.MasterId is null ? null : await ListCardAnalyticsAsync(normalized with
            {
                Page = 1, Limit = 20, Sort = "iwd", Direction = "desc", Search = null,
            }, budget.Token, timing);
            if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
            return report with { Cards = cards };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
            throw CardAnalyticsUnavailableException.TimedOut();
        }
        finally { timing.SetTotal(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }

    private static L12MasterAnalyticsReport ComputeMasterAnalytics(CardAnalyticsExecution execution,
        L12CardAnalyticsQuery query)
    {
        var selectedMasterId = query.MasterId;
        var scope = query with { MasterId = null, Page = 1, Search = null };
        var connection = execution.Connection;
        using var transaction = connection.BeginTransaction(deferred: true);
        IReadOnlyList<L12MasterAnalyticsItem> items;
        IReadOnlyList<L12MasterMatchup> matchups;
        IReadOnlyList<L12MasterAnalyticsTrend> trend;
        IReadOnlyList<L12PopularMasterDeck> popularDecks;
        {
            execution.Measure(CardAnalyticsStage.ScopeTotal, () =>
                PrepareAnalyticsScopeAsync(connection, scope, includeFacts: false, transaction: transaction).GetAwaiter().GetResult());
            var totalSamples = Convert.ToInt64(ScalarAsync(connection,
                "SELECT COUNT(*) FROM temp.l12_analytics_eligible;", transaction).GetAwaiter().GetResult());
            var totalDecks = Convert.ToInt64(ScalarAsync(connection, """
                WITH ordered AS (
                    SELECT d.match_id,d.player_index,d.section,d.card_id,d.quantity
                    FROM match_deck_cards d JOIN temp.l12_analytics_eligible e
                      ON e.match_id=d.match_id AND e.player_index=d.player_index
                    ORDER BY d.match_id,d.player_index,d.section,d.card_id),
                signatures AS (
                    SELECT match_id,player_index,group_concat(section||':'||card_id||'x'||quantity,'|') signature
                    FROM ordered GROUP BY match_id,player_index)
                SELECT COUNT(DISTINCT e.master_id||':'||s.signature)
                FROM temp.l12_analytics_eligible e JOIN signatures s
                  ON s.match_id=e.match_id AND s.player_index=e.player_index;
                """, transaction).GetAwaiter().GetResult());
            items = ReadMasterItemsAsync(connection, totalSamples, totalDecks, transaction).GetAwaiter().GetResult();
            matchups = ReadMasterMatchupsAsync(connection, transaction).GetAwaiter().GetResult();
            trend = selectedMasterId is null ? [] : ReadMasterTrendAsync(connection, selectedMasterId, transaction).GetAwaiter().GetResult();
            popularDecks = selectedMasterId is null ? [] : ReadPopularMasterDecksAsync(connection, selectedMasterId, transaction).GetAwaiter().GetResult();
        }
        var disasters = execution.Measure(CardAnalyticsStage.Coverage, () =>
            ComputeDisasterAnalytics(execution, transaction, query));
        var orderedItems = string.Equals(query.Sort, "win-rate", StringComparison.OrdinalIgnoreCase)
            ? items.OrderByDescending(item => item.WinRate).ThenByDescending(item => item.ParticipantSamples).ToArray()
            : items.OrderByDescending(item => item.UsageRate).ThenByDescending(item => item.ParticipantSamples).ToArray();
        execution.Check();
        transaction.Commit();
        return new(orderedItems, matchups, trend, selectedMasterId, popularDecks, null, disasters);
    }

    private static async Task<IReadOnlyList<L12MasterAnalyticsItem>> ReadMasterItemsAsync(
        SqliteConnection connection, long totalSamples, long totalDecks, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH ordered AS (
                SELECT d.match_id,d.player_index,d.section,d.card_id,d.quantity
                FROM match_deck_cards d JOIN temp.l12_analytics_eligible e
                  ON e.match_id=d.match_id AND e.player_index=d.player_index
                ORDER BY d.match_id,d.player_index,d.section,d.card_id),
            signatures AS (
                SELECT match_id,player_index,group_concat(section||':'||card_id||'x'||quantity,'|') signature
                FROM ordered GROUP BY match_id,player_index)
            SELECT e.master_id,COUNT(*),COUNT(DISTINCT e.match_id),COUNT(DISTINCT s.signature),
                   SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END),
                   AVG(MAX(0,(julianday(m.ended_utc)-julianday(m.started_utc))*86400.0)),
                   SUM(CASE WHEN e.first_player=e.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN e.first_player=e.player_index AND e.winner=e.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN e.first_player IS NOT NULL AND e.first_player<>e.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN e.first_player IS NOT NULL AND e.first_player<>e.player_index AND e.winner=e.player_index THEN 1 ELSE 0 END)
            FROM temp.l12_analytics_eligible e
            JOIN matches m ON m.match_id=e.match_id
            LEFT JOIN signatures s ON s.match_id=e.match_id AND s.player_index=e.player_index
            WHERE e.master_id IS NOT NULL AND e.master_id<>''
            GROUP BY e.master_id;
            """;
        var result = new List<L12MasterAnalyticsItem>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var samples = reader.GetInt64(1);
            var decks = reader.GetInt64(3);
            var wins = ReadLong(reader, 4);
            var firstSamples = ReadLong(reader, 6);
            var firstWins = ReadLong(reader, 7);
            var secondSamples = ReadLong(reader, 8);
            var secondWins = ReadLong(reader, 9);
            result.Add(new(reader.GetString(0), samples, reader.GetInt64(2), decks,
                Rate(samples, totalSamples), Rate(decks, totalDecks), wins, Rate(wins, samples),
                WilsonInterval(wins, samples), reader.IsDBNull(5) ? 0 : Math.Round(reader.GetDouble(5), 1),
                firstSamples, RateOrNull(firstWins, firstSamples), secondSamples, RateOrNull(secondWins, secondSamples)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<L12MasterMatchup>> ReadMasterMatchupsAsync(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT master_id,opponent_master_id,COUNT(*),
                   SUM(CASE WHEN winner=player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN first_player=player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN first_player=player_index AND winner=player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN first_player IN (0,1) AND first_player<>player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN first_player IN (0,1) AND first_player<>player_index AND winner=player_index THEN 1 ELSE 0 END)
            FROM temp.l12_analytics_eligible
            WHERE master_id IS NOT NULL AND opponent_master_id IS NOT NULL
            GROUP BY master_id,opponent_master_id ORDER BY master_id,opponent_master_id;
            """;
        var result = new List<L12MasterMatchup>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var samples = reader.GetInt64(2);
            var wins = ReadLong(reader, 3);
            result.Add(new(reader.GetString(0), reader.GetString(1), samples, wins,
                samples < 30 ? null : Rate(wins, samples), ReadLong(reader, 4), ReadLong(reader, 5),
                ReadLong(reader, 6), ReadLong(reader, 7)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<L12MasterAnalyticsTrend>> ReadMasterTrendAsync(
        SqliteConnection connection, string masterId, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT substr(started_utc,1,10),COUNT(*),SUM(CASE WHEN winner=player_index THEN 1 ELSE 0 END)
            FROM temp.l12_analytics_eligible WHERE master_id=$master
            GROUP BY substr(started_utc,1,10) ORDER BY substr(started_utc,1,10);
            """;
        command.Parameters.AddWithValue("$master", masterId);
        var result = new List<L12MasterAnalyticsTrend>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var samples = reader.GetInt64(1);
            var wins = ReadLong(reader, 2);
            result.Add(new(reader.GetString(0), samples, wins, Rate(wins, samples)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<L12PopularMasterDeck>> ReadPopularMasterDecksAsync(
        SqliteConnection connection, string masterId, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH ordered AS (
                SELECT d.match_id,d.player_index,d.section,d.card_id,d.quantity
                FROM match_deck_cards d JOIN temp.l12_analytics_eligible e
                  ON e.match_id=d.match_id AND e.player_index=d.player_index
                WHERE e.master_id=$master ORDER BY d.match_id,d.player_index,d.section,d.card_id),
            signatures AS (
                SELECT match_id,player_index,group_concat(section||':'||card_id||'x'||quantity,'|') signature
                FROM ordered GROUP BY match_id,player_index)
            SELECT s.signature,COUNT(*),SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END)
            FROM signatures s JOIN temp.l12_analytics_eligible e
              ON e.match_id=s.match_id AND e.player_index=s.player_index
            GROUP BY s.signature ORDER BY COUNT(*) DESC,3 DESC LIMIT 10;
            """;
        command.Parameters.AddWithValue("$master", masterId);
        var result = new List<L12PopularMasterDeck>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var signature = reader.GetString(0);
            var samples = reader.GetInt64(1);
            var wins = ReadLong(reader, 2);
            result.Add(new(signature, samples, wins, Rate(wins, samples), ParseDeckSignature(signature)));
        }
        return result;
    }

    private static IReadOnlyList<L12DeckCardSnapshot> ParseDeckSignature(string signature)
        => signature.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var colon = part.IndexOf(':');
            var quantityMarker = part.LastIndexOf('x');
            return colon > 0 && quantityMarker > colon
                ? new L12DeckCardSnapshot(part[(colon + 1)..quantityMarker],
                    int.TryParse(part[(quantityMarker + 1)..], out var quantity) ? quantity : 1, part[..colon])
                : new L12DeckCardSnapshot(part, 1, "main");
        }).ToArray();

    private static async Task<object> ScalarAsync(SqliteConnection connection, string sql, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() ?? 0L;
    }
}
