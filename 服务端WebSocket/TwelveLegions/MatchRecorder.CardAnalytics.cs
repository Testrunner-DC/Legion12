using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private sealed record AnalyticsPopulation(long EligibleMatches, long SampleSize, long Wins)
    {
        public double BaselineWinRate => Rate(Wins, SampleSize);
    }

    private sealed record CardAnalyticsRow(
        string CardId,
        long IncludedSamples,
        long IncludedMatches,
        long Wins,
        long DrawnMatches,
        long PlayedMatches,
        long ActivatedCount,
        long ResolvedCount,
        long NegatedCount,
        long FizzledCount,
        long ExactFacts,
        long InferredFacts,
        long PartialFacts);

    public async Task<L12CardAnalyticsPage> ListCardAnalyticsAsync(L12CardAnalyticsQuery query)
    {
        var normalized = NormalizeAnalyticsQuery(query);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var population = await ReadAnalyticsPopulationAsync(connection, normalized);
        var rows = await ReadCardAnalyticsRowsAsync(connection, normalized, includeCursor: true,
            normalized.Limit + 1);
        var hasMore = rows.Count > normalized.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(row => ToAnalyticsItem(row, population)).ToArray();
        var total = await CountCardAnalyticsRowsAsync(connection, normalized);
        var coverage = await ReadAnalyticsCoverageAsync(connection, normalized, population.SampleSize);
        return new L12CardAnalyticsPage(items, total,
            hasMore && rows.Count > 0 ? Base64UrlEncode(rows[^1].CardId) : null,
            new L12CardAnalyticsPageSummary(population.EligibleMatches, population.SampleSize,
                population.BaselineWinRate, normalized.MinimumSampleSize, coverage));
    }

    public async Task<L12CardAnalyticsDetail?> GetCardAnalyticsAsync(string cardId,
        L12CardAnalyticsQuery query)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        var normalized = NormalizeAnalyticsQuery(query with { Cursor = null, Search = null });
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var population = await ReadAnalyticsPopulationAsync(connection, normalized);
        var row = await ReadSingleCardAnalyticsRowAsync(connection, normalized, cardId.Trim());
        if (row is null || row.IncludedSamples < normalized.MinimumSampleSize) return null;
        var summary = ToAnalyticsItem(row, population);
        var breakdowns = new List<L12CardAnalyticsBreakdown>();
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, cardId.Trim(),
            "mode", "e.mode_id"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, cardId.Trim(),
            "master", "COALESCE(e.master_id,'unknown')"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, cardId.Trim(),
            "opponent-master", "COALESCE(e.opponent_master_id,'unknown')"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, cardId.Trim(),
            "initiative", "CASE WHEN e.first_player IS NULL THEN 'unknown' WHEN e.first_player=e.player_index THEN 'first' ELSE 'second' END"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, cardId.Trim(),
            "rules-version", "COALESCE(e.rules_version,'legacy')"));
        var recent = await ListAdminMatchesAsync(new L12AdminMatchQuery(Limit: 20,
            ModeId: normalized.ModeId, MasterId: normalized.MasterId, FromUtc: normalized.FromUtc,
            ToUtc: normalized.ToUtc, CardId: cardId.Trim()));
        return new L12CardAnalyticsDetail(summary, breakdowns, recent.Items, summary.Coverage);
    }

    private static L12CardAnalyticsQuery NormalizeAnalyticsQuery(L12CardAnalyticsQuery query)
        => query with
        {
            Limit = Math.Clamp(query.Limit, 1, 200),
            MinimumSampleSize = Math.Clamp(query.MinimumSampleSize, 1, 1000),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            CandidateCardIds = query.CandidateCardIds?.Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .Select(cardId => cardId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ModeId = string.IsNullOrWhiteSpace(query.ModeId) ? null : query.ModeId.Trim().ToLowerInvariant(),
            MasterId = string.IsNullOrWhiteSpace(query.MasterId) ? null : query.MasterId.Trim(),
        };

    private static string AnalyticsEligibleCte(L12CardAnalyticsQuery query,
        out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var clauses = new List<string>
        {
            "m.mode_id <> 'sandbox'",
            "m.ended_utc IS NOT NULL",
            "m.error IS NULL",
            "m.winner IN (0,1)",
            "p.deck_snapshot_coverage='exact'",
            "EXISTS(SELECT 1 FROM match_deck_cards snapshot WHERE snapshot.match_id=p.match_id AND snapshot.player_index=p.player_index)",
        };
        if (query.ModeId is not null)
        {
            clauses.Add("m.mode_id=$mode");
            parameters["$mode"] = query.ModeId;
        }
        if (query.MasterId is not null)
        {
            clauses.Add("p.master_id=$master");
            parameters["$master"] = query.MasterId;
        }
        if (query.FromUtc is { } from)
        {
            clauses.Add("m.started_utc >= $from");
            parameters["$from"] = from.ToUniversalTime().ToString("O");
        }
        if (query.ToUtc is { } to)
        {
            clauses.Add("m.started_utc < $to");
            parameters["$to"] = to.ToUniversalTime().ToString("O");
        }
        return $"""
            WITH eligible AS (
                SELECT m.match_id,m.mode_id,m.rules_version,m.winner,m.first_player,m.started_utc,
                       p.player_index,p.master_id,opponent.master_id AS opponent_master_id
                FROM matches m
                JOIN match_participants p ON p.match_id=m.match_id
                JOIN match_participants opponent ON opponent.match_id=p.match_id
                    AND opponent.player_index<>p.player_index
                WHERE {string.Join(" AND ", clauses)}
            ),
            inclusions AS (
                SELECT DISTINCT e.match_id,e.player_index,d.card_id
                FROM eligible e
                JOIN match_deck_cards d ON d.match_id=e.match_id AND d.player_index=e.player_index
            ),
            fact_stats AS (
                SELECT f.match_id,f.player_index,f.card_id,
                       MAX(CASE WHEN f.kind='draw' THEN 1 ELSE 0 END) AS drawn,
                       MAX(CASE WHEN f.kind='play' THEN 1 ELSE 0 END) AS played,
                       SUM(CASE WHEN f.kind='activate' THEN 1 ELSE 0 END) AS activated,
                       SUM(CASE WHEN f.kind='resolve' THEN 1 ELSE 0 END) AS resolved,
                       SUM(CASE WHEN f.kind='negate' THEN 1 ELSE 0 END) AS negated,
                       SUM(CASE WHEN f.kind='fizzle' THEN 1 ELSE 0 END) AS fizzled,
                       SUM(CASE WHEN f.coverage='exact' THEN 1 ELSE 0 END) AS exact_facts,
                       SUM(CASE WHEN f.coverage='inferred' THEN 1 ELSE 0 END) AS inferred_facts,
                       SUM(CASE WHEN f.coverage='partial' THEN 1 ELSE 0 END) AS partial_facts
                FROM match_card_facts f
                JOIN eligible e ON e.match_id=f.match_id AND e.player_index=f.player_index
                WHERE f.card_id IS NOT NULL
                GROUP BY f.match_id,f.player_index,f.card_id
            )
            """;
    }

    private static async Task<AnalyticsPopulation> ReadAnalyticsPopulationAsync(SqliteConnection connection,
        L12CardAnalyticsQuery query)
    {
        var cte = AnalyticsEligibleCte(query, out var parameters);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT COUNT(DISTINCT match_id),COUNT(*),
                   SUM(CASE WHEN winner=player_index THEN 1 ELSE 0 END)
            FROM eligible;
            """;
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new AnalyticsPopulation(0, 0, 0);
        return new AnalyticsPopulation(reader.GetInt64(0), reader.GetInt64(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2));
    }

    private static async Task<List<CardAnalyticsRow>> ReadCardAnalyticsRowsAsync(SqliteConnection connection,
        L12CardAnalyticsQuery query, bool includeCursor, int take)
    {
        var cte = AnalyticsEligibleCte(query, out var parameters);
        var filters = new List<string>();
        if (query.Search is not null)
        {
            filters.Add(CardSearchPredicate("i", query, parameters));
        }
        if (includeCursor && query.Cursor is not null)
        {
            string cursor;
            try { cursor = Base64UrlDecode(query.Cursor); }
            catch (Exception error) when (error is FormatException or ArgumentException)
            {
                throw new ArgumentException("分页游标无效", nameof(query));
            }
            filters.Add("i.card_id > $cursor");
            parameters["$cursor"] = cursor;
        }
        parameters["$minimum"] = query.MinimumSampleSize;
        parameters["$take"] = take;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT i.card_id,COUNT(*),COUNT(DISTINCT i.match_id),
                   SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END),
                   COUNT(DISTINCT CASE WHEN COALESCE(f.drawn,0)=1 THEN i.match_id END),
                   COUNT(DISTINCT CASE WHEN COALESCE(f.played,0)=1 THEN i.match_id END),
                   SUM(COALESCE(f.activated,0)),SUM(COALESCE(f.resolved,0)),
                   SUM(COALESCE(f.negated,0)),SUM(COALESCE(f.fizzled,0)),
                   SUM(COALESCE(f.exact_facts,0)),SUM(COALESCE(f.inferred_facts,0)),
                   SUM(COALESCE(f.partial_facts,0))
            FROM inclusions i
            JOIN eligible e ON e.match_id=i.match_id AND e.player_index=i.player_index
            LEFT JOIN fact_stats f ON f.match_id=i.match_id AND f.player_index=i.player_index
                                  AND f.card_id=i.card_id
            {(filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}")}
            GROUP BY i.card_id
            HAVING COUNT(*) >= $minimum
            ORDER BY i.card_id
            LIMIT $take;
            """;
        AddParameters(command, parameters);
        var rows = new List<CardAnalyticsRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(ReadCardAnalyticsRow(reader));
        return rows;
    }

    private static async Task<long> CountCardAnalyticsRowsAsync(SqliteConnection connection,
        L12CardAnalyticsQuery query)
    {
        var cte = AnalyticsEligibleCte(query, out var parameters);
        var search = string.Empty;
        if (query.Search is not null)
        {
            search = $"WHERE {CardSearchPredicate("i", query, parameters)}";
        }
        parameters["$minimum"] = query.MinimumSampleSize;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT COUNT(*) FROM (
                SELECT i.card_id FROM inclusions i {search}
                GROUP BY i.card_id HAVING COUNT(*) >= $minimum
            );
            """;
        AddParameters(command, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<CardAnalyticsRow?> ReadSingleCardAnalyticsRowAsync(SqliteConnection connection,
        L12CardAnalyticsQuery query, string cardId)
    {
        var cte = AnalyticsEligibleCte(query, out var parameters);
        parameters["$card"] = cardId;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT i.card_id,COUNT(*),COUNT(DISTINCT i.match_id),
                   SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END),
                   COUNT(DISTINCT CASE WHEN COALESCE(f.drawn,0)=1 THEN i.match_id END),
                   COUNT(DISTINCT CASE WHEN COALESCE(f.played,0)=1 THEN i.match_id END),
                   SUM(COALESCE(f.activated,0)),SUM(COALESCE(f.resolved,0)),
                   SUM(COALESCE(f.negated,0)),SUM(COALESCE(f.fizzled,0)),
                   SUM(COALESCE(f.exact_facts,0)),SUM(COALESCE(f.inferred_facts,0)),
                   SUM(COALESCE(f.partial_facts,0))
            FROM inclusions i
            JOIN eligible e ON e.match_id=i.match_id AND e.player_index=i.player_index
            LEFT JOIN fact_stats f ON f.match_id=i.match_id AND f.player_index=i.player_index
                                  AND f.card_id=i.card_id
            WHERE i.card_id=$card
            GROUP BY i.card_id;
            """;
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadCardAnalyticsRow(reader) : null;
    }

    private static CardAnalyticsRow ReadCardAnalyticsRow(SqliteDataReader reader)
        => new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3), reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
            reader.IsDBNull(5) ? 0 : reader.GetInt64(5), reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
            reader.IsDBNull(7) ? 0 : reader.GetInt64(7), reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
            reader.IsDBNull(9) ? 0 : reader.GetInt64(9), reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
            reader.IsDBNull(11) ? 0 : reader.GetInt64(11), reader.IsDBNull(12) ? 0 : reader.GetInt64(12));

    private static async Task<L12AnalyticsCoverage> ReadAnalyticsCoverageAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, long exactDeckSnapshots)
    {
        var cte = AnalyticsEligibleCte(query, out var parameters);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT SUM(CASE WHEN f.coverage='exact' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.coverage='inferred' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.coverage='partial' THEN 1 ELSE 0 END)
            FROM match_card_facts f
            JOIN eligible e ON e.match_id=f.match_id AND e.player_index=f.player_index;
            """;
        AddParameters(command, parameters);
        long exact = 0, inferred = 0, partial = 0;
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            exact = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
            inferred = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            partial = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
        }
        return new L12AnalyticsCoverage(L12CardFactKinds.SchemaVersion, L12CardFactKinds.Supported,
            exact, inferred, partial, exactDeckSnapshots, 0, false, AnalyticsLimitations);
    }

    private static L12CardAnalyticsItem ToAnalyticsItem(CardAnalyticsRow row,
        AnalyticsPopulation population)
    {
        var winRate = Rate(row.Wins, row.IncludedSamples);
        var comparisonSamples = population.SampleSize - row.IncludedSamples;
        var comparisonWins = population.Wins - row.Wins;
        var comparisonWinRate = Rate(comparisonWins, comparisonSamples);
        var coverage = new L12AnalyticsCoverage(L12CardFactKinds.SchemaVersion, L12CardFactKinds.Supported,
            row.ExactFacts, row.InferredFacts, row.PartialFacts, row.IncludedSamples, 0, false,
            AnalyticsLimitations);
        return new L12CardAnalyticsItem(row.CardId, row.IncludedSamples, population.SampleSize,
            row.IncludedMatches, Rate(row.IncludedSamples, population.SampleSize), row.Wins, winRate,
            comparisonWinRate, winRate - comparisonWinRate, row.DrawnMatches, row.PlayedMatches, row.ActivatedCount,
            row.ResolvedCount, row.NegatedCount, row.FizzledCount, coverage);
    }

    private static async Task<IReadOnlyList<L12CardAnalyticsBreakdown>> ReadBreakdownsAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, string cardId,
        string dimension, string valueExpression)
    {
        var cte = AnalyticsEligibleCte(query, out var parameters);
        parameters["$card"] = cardId;
        parameters["$minimum"] = query.MinimumSampleSize;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT {valueExpression} AS value,
                   COUNT(*),
                   SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN i.card_id IS NOT NULL THEN 1 ELSE 0 END),
                   COUNT(DISTINCT CASE WHEN i.card_id IS NOT NULL THEN e.match_id END),
                   SUM(CASE WHEN i.card_id IS NOT NULL AND e.winner=e.player_index THEN 1 ELSE 0 END)
            FROM eligible e
            LEFT JOIN inclusions i ON i.match_id=e.match_id AND i.player_index=e.player_index
                                   AND i.card_id=$card
            GROUP BY value
            HAVING SUM(CASE WHEN i.card_id IS NOT NULL THEN 1 ELSE 0 END) >= $minimum
            ORDER BY value;
            """;
        AddParameters(command, parameters);
        var result = new List<L12CardAnalyticsBreakdown>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sample = reader.GetInt64(1);
            var baselineWins = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            var included = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            var includedMatches = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
            var wins = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
            var winRate = Rate(wins, included);
            var baseline = Rate(baselineWins - wins, sample - included);
            result.Add(new L12CardAnalyticsBreakdown(dimension, reader.GetString(0), included, sample,
                includedMatches,
                wins, winRate, baseline, winRate - baseline));
        }
        return result;
    }

    private static double Rate(long numerator, long denominator)
        => denominator <= 0 ? 0 : Math.Round((double)numerator / denominator, 6,
            MidpointRounding.AwayFromZero);

    private static string CardSearchPredicate(string alias, L12CardAnalyticsQuery query,
        Dictionary<string, object> parameters)
    {
        parameters["$search"] = $"%{EscapeLike(query.Search!)}%";
        var predicates = new List<string> { $"{alias}.card_id LIKE $search ESCAPE '\\'" };
        var candidateIds = query.CandidateCardIds ?? [];
        if (candidateIds.Count > 0)
        {
            var names = new List<string>();
            for (var index = 0; index < candidateIds.Count; index++)
            {
                var name = $"$searchCard{index}";
                names.Add(name);
                parameters[name] = candidateIds[index];
            }
            predicates.Add($"{alias}.card_id IN ({string.Join(',', names)})");
        }
        return $"({string.Join(" OR ", predicates)})";
    }
}
