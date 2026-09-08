using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private const int MaximumAnalyticsBreakdowns = 200;
    private const int MaximumAnalyticsTimingBuckets = 200;
    private const int MaximumAnalyticsQuantityBuckets = 20;
    private const int MaximumAnalyticsMatchups = 200;

    private sealed record AnalyticsPopulation(long EligibleMatches, long SampleSize, long Wins)
    {
        public double? BaselineWinRate => RateOrNull(Wins, SampleSize);
    }

    private sealed record MetricCoverageCounts(long Exact, long Inferred, long Partial);

    private sealed record CardAnalyticsRow(
        string CardId,
        long IncludedSamples,
        long IncludedMatches,
        long Wins,
        long DrawnMatches,
        long PlayedMatches,
        long DrawnSamples,
        long PlayedSamples,
        long ActivatedSamples,
        long SettledSamples,
        long ResolvedSamples,
        long NegatedSamples,
        long FizzledSamples,
        long ActivatedCount,
        long ResolvedCount,
        long NegatedCount,
        long FizzledCount,
        long ExactFacts,
        long InferredFacts,
        long PartialFacts,
        long TotalQuantity,
        MetricCoverageCounts DrawCoverage,
        MetricCoverageCounts PlayCoverage,
        MetricCoverageCounts ActivationCoverage,
        MetricCoverageCounts SettlementCoverage);

    public async Task<L12CardAnalyticsPage> ListCardAnalyticsAsync(L12CardAnalyticsQuery query)
    {
        var normalized = NormalizeAnalyticsQuery(query);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await PrepareAnalyticsScopeAsync(connection, normalized);
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
                population.BaselineWinRate, normalized.MinimumSampleSize, "participant", coverage));
    }

    public async Task<L12CardAnalyticsDetail?> GetCardAnalyticsAsync(string cardId,
        L12CardAnalyticsQuery query, bool includeRecentMatches = true)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        var normalizedCardId = cardId.Trim();
        var normalized = NormalizeAnalyticsQuery(query with { Cursor = null, Search = null });
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await PrepareAnalyticsScopeAsync(connection, normalized);
        var population = await ReadAnalyticsPopulationAsync(connection, normalized);
        var row = await ReadSingleCardAnalyticsRowAsync(connection, normalized, normalizedCardId);
        if (row is null || row.IncludedSamples < normalized.MinimumSampleSize) return null;
        var summary = ToAnalyticsItem(row, population);
        var breakdowns = new List<L12CardAnalyticsBreakdown>();
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, normalizedCardId,
            "mode", "e.mode_id"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, normalizedCardId,
            "master", "COALESCE(e.master_id,'unknown')"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, normalizedCardId,
            "opponent-master", "COALESCE(e.opponent_master_id,'unknown')"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, normalizedCardId,
            "initiative", InitiativeExpression("e")));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, normalizedCardId,
            "rules-version", "COALESCE(e.rules_version,'legacy')"));
        breakdowns.AddRange(await ReadBreakdownsAsync(connection, normalized, normalizedCardId,
            "season", "COALESCE(e.season_id,'unassigned')"));
        var quantities = await ReadQuantityDistributionAsync(connection, normalized, normalizedCardId);
        var turns = await ReadTurnDistributionAsync(connection, normalized, normalizedCardId);
        var matchups = await ReadMatchupsAsync(connection, normalized, normalizedCardId);
        var privacySafeRecent = Array.Empty<L12AdminMatchSummary>();
        if (includeRecentMatches)
        {
            var recent = await ListAdminMatchesAsync(new L12AdminMatchQuery(
                Limit: 20,
                ModeId: normalized.ModeId,
                Status: "completed",
                FromUtc: normalized.FromUtc,
                ToUtc: normalized.ToUtc,
                CardId: normalizedCardId,
                CardOwnerMasterId: normalized.MasterId,
                CardOwnerOpponentMasterId: normalized.OpponentMasterId,
                CardOwnerInitiative: normalized.Initiative,
                RulesVersion: normalized.RulesVersion,
                SeasonId: normalized.SeasonId,
                RequireDecisiveResult: true));
            privacySafeRecent = recent.Items.Select(SanitizeAnalyticsRecentMatch).ToArray();
        }
        return new L12CardAnalyticsDetail(summary, breakdowns, quantities, turns, matchups,
            privacySafeRecent, summary.Coverage);
    }

    private static L12CardAnalyticsQuery NormalizeAnalyticsQuery(L12CardAnalyticsQuery query)
    {
        var initiative = string.IsNullOrWhiteSpace(query.Initiative)
            ? null : query.Initiative.Trim().ToLowerInvariant();
        if (initiative is not null and not ("first" or "second"))
            throw new ArgumentException("先后手筛选必须是 first 或 second", nameof(query));
        var normalized = query with
        {
            Limit = Math.Clamp(query.Limit, 1, 200),
            MinimumSampleSize = Math.Clamp(query.MinimumSampleSize, 1, 1000),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            CandidateCardIds = query.CandidateCardIds?.Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .Select(cardId => cardId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ModeId = string.IsNullOrWhiteSpace(query.ModeId) ? null : query.ModeId.Trim().ToLowerInvariant(),
            MasterId = string.IsNullOrWhiteSpace(query.MasterId) ? null : query.MasterId.Trim(),
            OpponentMasterId = string.IsNullOrWhiteSpace(query.OpponentMasterId)
                ? null : query.OpponentMasterId.Trim(),
            Initiative = initiative,
            RulesVersion = string.IsNullOrWhiteSpace(query.RulesVersion) ? null : query.RulesVersion.Trim(),
            SeasonId = string.IsNullOrWhiteSpace(query.SeasonId) ? null : query.SeasonId.Trim(),
        };
        if (normalized.FromUtc is { } from && normalized.ToUtc is { } to && from >= to)
            throw new ArgumentException("开始时间必须早于结束时间", nameof(query));
        return normalized;
    }

    private const string MaterializedAnalyticsCte = """
        WITH eligible AS (
            SELECT * FROM temp.l12_analytics_eligible
        ),
        inclusions AS (
            SELECT * FROM temp.l12_analytics_inclusions
        ),
        fact_stats AS (
            SELECT * FROM temp.l12_analytics_fact_stats
        )
        """;

    private static string AnalyticsEligibleSelect(L12CardAnalyticsQuery query,
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
        if (query.OpponentMasterId is not null)
        {
            clauses.Add("opponent.master_id=$opponentMaster");
            parameters["$opponentMaster"] = query.OpponentMasterId;
        }
        if (query.Initiative is not null)
        {
            clauses.Add(query.Initiative == "first"
                ? "m.first_player=p.player_index"
                : "m.first_player IS NOT NULL AND m.first_player<>p.player_index");
        }
        if (query.RulesVersion is not null)
        {
            clauses.Add("m.rules_version=$rulesVersion");
            parameters["$rulesVersion"] = query.RulesVersion;
        }
        if (query.SeasonId is not null)
        {
            clauses.Add("m.season_id=$seasonId");
            parameters["$seasonId"] = query.SeasonId;
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
            SELECT m.match_id,m.mode_id,m.rules_version,m.season_id,m.fact_schema_version,
                   m.winner,m.first_player,m.started_utc,p.player_index,p.master_id,
                   opponent.master_id AS opponent_master_id
            FROM matches m
            JOIN match_participants p ON p.match_id=m.match_id
            JOIN match_participants opponent ON opponent.match_id=p.match_id
                AND opponent.player_index<>p.player_index
            WHERE {string.Join(" AND ", clauses)}
            """;
    }

    private static async Task PrepareAnalyticsScopeAsync(SqliteConnection connection,
        L12CardAnalyticsQuery query)
    {
        var eligibleSelect = AnalyticsEligibleSelect(query, out var parameters);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            DROP TABLE IF EXISTS temp.l12_analytics_fact_stats;
            DROP TABLE IF EXISTS temp.l12_analytics_inclusions;
            DROP TABLE IF EXISTS temp.l12_analytics_eligible;

            CREATE TEMP TABLE l12_analytics_eligible AS
            {eligibleSelect};
            CREATE UNIQUE INDEX temp.ix_l12_analytics_eligible_owner
                ON l12_analytics_eligible(match_id,player_index);
            CREATE INDEX temp.ix_l12_analytics_eligible_dimensions
                ON l12_analytics_eligible(mode_id,started_utc,master_id,opponent_master_id);

            CREATE TEMP TABLE l12_analytics_inclusions AS
            SELECT e.match_id,e.player_index,d.card_id,SUM(d.quantity) AS quantity
            FROM temp.l12_analytics_eligible e
            JOIN match_deck_cards d ON d.match_id=e.match_id AND d.player_index=e.player_index
            GROUP BY e.match_id,e.player_index,d.card_id;
            CREATE UNIQUE INDEX temp.ix_l12_analytics_inclusions_card
                ON l12_analytics_inclusions(card_id,match_id,player_index);

            CREATE TEMP TABLE l12_analytics_fact_stats AS
            SELECT f.match_id,f.player_index,f.card_id,
                   MAX(CASE WHEN f.kind='draw' AND f.coverage='exact' THEN 1 ELSE 0 END) AS drawn,
                   MAX(CASE WHEN f.kind='play' AND f.coverage='exact' THEN 1 ELSE 0 END) AS played,
                   MAX(CASE WHEN f.kind='activate' AND f.coverage='exact' THEN 1 ELSE 0 END) AS activated_sample,
                   MAX(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='exact' THEN 1 ELSE 0 END) AS settled_sample,
                   MAX(CASE WHEN f.kind='resolve' AND f.coverage='exact' THEN 1 ELSE 0 END) AS resolved_sample,
                   MAX(CASE WHEN f.kind='negate' AND f.coverage='exact' THEN 1 ELSE 0 END) AS negated_sample,
                   MAX(CASE WHEN f.kind='fizzle' AND f.coverage='exact' THEN 1 ELSE 0 END) AS fizzled_sample,
                   MAX(CASE WHEN f.kind IN ('draw','play','activate','resolve','negate','fizzle') THEN 1 ELSE 0 END) AS observed_sample,
                   MIN(CASE WHEN f.kind='draw' AND f.coverage='exact' THEN f.turn END) AS first_draw_turn,
                   MIN(CASE WHEN f.kind='play' AND f.coverage='exact' THEN f.turn END) AS first_play_turn,
                   SUM(CASE WHEN f.kind='activate' THEN 1 ELSE 0 END) AS activated,
                   SUM(CASE WHEN f.kind='resolve' THEN 1 ELSE 0 END) AS resolved,
                   SUM(CASE WHEN f.kind='negate' THEN 1 ELSE 0 END) AS negated,
                   SUM(CASE WHEN f.kind='fizzle' THEN 1 ELSE 0 END) AS fizzled,
                   SUM(CASE WHEN f.coverage='exact' THEN 1 ELSE 0 END) AS exact_facts,
                   SUM(CASE WHEN f.coverage='inferred' THEN 1 ELSE 0 END) AS inferred_facts,
                   SUM(CASE WHEN f.coverage='partial' THEN 1 ELSE 0 END) AS partial_facts,
                   SUM(CASE WHEN f.kind='draw' AND f.coverage='exact' THEN 1 ELSE 0 END) AS draw_exact,
                   SUM(CASE WHEN f.kind='draw' AND f.coverage='inferred' THEN 1 ELSE 0 END) AS draw_inferred,
                   SUM(CASE WHEN f.kind='draw' AND f.coverage='partial' THEN 1 ELSE 0 END) AS draw_partial,
                   SUM(CASE WHEN f.kind='play' AND f.coverage='exact' THEN 1 ELSE 0 END) AS play_exact,
                   SUM(CASE WHEN f.kind='play' AND f.coverage='inferred' THEN 1 ELSE 0 END) AS play_inferred,
                   SUM(CASE WHEN f.kind='play' AND f.coverage='partial' THEN 1 ELSE 0 END) AS play_partial,
                   SUM(CASE WHEN f.kind='activate' AND f.coverage='exact' THEN 1 ELSE 0 END) AS activation_exact,
                   SUM(CASE WHEN f.kind='activate' AND f.coverage='inferred' THEN 1 ELSE 0 END) AS activation_inferred,
                   SUM(CASE WHEN f.kind='activate' AND f.coverage='partial' THEN 1 ELSE 0 END) AS activation_partial,
                   SUM(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='exact' THEN 1 ELSE 0 END) AS settlement_exact,
                   SUM(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='inferred' THEN 1 ELSE 0 END) AS settlement_inferred,
                   SUM(CASE WHEN f.kind IN ('resolve','negate','fizzle') AND f.coverage='partial' THEN 1 ELSE 0 END) AS settlement_partial
            FROM match_card_facts f
            JOIN temp.l12_analytics_eligible e
              ON e.match_id=f.match_id AND e.player_index=f.player_index
            JOIN temp.l12_analytics_inclusions i
              ON i.match_id=f.match_id AND i.player_index=f.player_index AND i.card_id=f.card_id
            WHERE f.card_id IS NOT NULL
            GROUP BY f.match_id,f.player_index,f.card_id;
            CREATE UNIQUE INDEX temp.ix_l12_analytics_fact_stats_card
                ON l12_analytics_fact_stats(card_id,match_id,player_index);
            """;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<AnalyticsPopulation> ReadAnalyticsPopulationAsync(SqliteConnection connection,
        L12CardAnalyticsQuery query)
    {
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
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
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var filters = new List<string>();
        if (query.Search is not null) filters.Add(CardSearchPredicate("i", query, parameters));
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
                   SUM(COALESCE(f.drawn,0)),SUM(COALESCE(f.played,0)),
                   SUM(COALESCE(f.activated_sample,0)),SUM(COALESCE(f.settled_sample,0)),
                   SUM(COALESCE(f.resolved_sample,0)),SUM(COALESCE(f.negated_sample,0)),
                   SUM(COALESCE(f.fizzled_sample,0)),SUM(COALESCE(f.activated,0)),
                   SUM(COALESCE(f.resolved,0)),SUM(COALESCE(f.negated,0)),
                   SUM(COALESCE(f.fizzled,0)),SUM(COALESCE(f.exact_facts,0)),
                   SUM(COALESCE(f.inferred_facts,0)),SUM(COALESCE(f.partial_facts,0)),
                   SUM(COALESCE(f.draw_exact,0)),SUM(COALESCE(f.draw_inferred,0)),SUM(COALESCE(f.draw_partial,0)),
                   SUM(COALESCE(f.play_exact,0)),SUM(COALESCE(f.play_inferred,0)),SUM(COALESCE(f.play_partial,0)),
                   SUM(COALESCE(f.activation_exact,0)),SUM(COALESCE(f.activation_inferred,0)),SUM(COALESCE(f.activation_partial,0)),
                   SUM(COALESCE(f.settlement_exact,0)),SUM(COALESCE(f.settlement_inferred,0)),SUM(COALESCE(f.settlement_partial,0)),
                   SUM(i.quantity)
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
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var search = string.Empty;
        if (query.Search is not null) search = $"WHERE {CardSearchPredicate("i", query, parameters)}";
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
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        parameters["$card"] = cardId;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT i.card_id,COUNT(*),COUNT(DISTINCT i.match_id),
                   SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END),
                   COUNT(DISTINCT CASE WHEN COALESCE(f.drawn,0)=1 THEN i.match_id END),
                   COUNT(DISTINCT CASE WHEN COALESCE(f.played,0)=1 THEN i.match_id END),
                   SUM(COALESCE(f.drawn,0)),SUM(COALESCE(f.played,0)),
                   SUM(COALESCE(f.activated_sample,0)),SUM(COALESCE(f.settled_sample,0)),
                   SUM(COALESCE(f.resolved_sample,0)),SUM(COALESCE(f.negated_sample,0)),
                   SUM(COALESCE(f.fizzled_sample,0)),SUM(COALESCE(f.activated,0)),
                   SUM(COALESCE(f.resolved,0)),SUM(COALESCE(f.negated,0)),
                   SUM(COALESCE(f.fizzled,0)),SUM(COALESCE(f.exact_facts,0)),
                   SUM(COALESCE(f.inferred_facts,0)),SUM(COALESCE(f.partial_facts,0)),
                   SUM(COALESCE(f.draw_exact,0)),SUM(COALESCE(f.draw_inferred,0)),SUM(COALESCE(f.draw_partial,0)),
                   SUM(COALESCE(f.play_exact,0)),SUM(COALESCE(f.play_inferred,0)),SUM(COALESCE(f.play_partial,0)),
                   SUM(COALESCE(f.activation_exact,0)),SUM(COALESCE(f.activation_inferred,0)),SUM(COALESCE(f.activation_partial,0)),
                   SUM(COALESCE(f.settlement_exact,0)),SUM(COALESCE(f.settlement_inferred,0)),SUM(COALESCE(f.settlement_partial,0)),
                   SUM(i.quantity)
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
        => new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), ReadLong(reader, 3),
            ReadLong(reader, 4), ReadLong(reader, 5), ReadLong(reader, 6), ReadLong(reader, 7),
            ReadLong(reader, 8), ReadLong(reader, 9), ReadLong(reader, 10), ReadLong(reader, 11),
            ReadLong(reader, 12), ReadLong(reader, 13), ReadLong(reader, 14), ReadLong(reader, 15),
            ReadLong(reader, 16), ReadLong(reader, 17), ReadLong(reader, 18), ReadLong(reader, 19),
            ReadLong(reader, 32),
            new MetricCoverageCounts(ReadLong(reader, 20), ReadLong(reader, 21), ReadLong(reader, 22)),
            new MetricCoverageCounts(ReadLong(reader, 23), ReadLong(reader, 24), ReadLong(reader, 25)),
            new MetricCoverageCounts(ReadLong(reader, 26), ReadLong(reader, 27), ReadLong(reader, 28)),
            new MetricCoverageCounts(ReadLong(reader, 29), ReadLong(reader, 30), ReadLong(reader, 31)));

    private static long ReadLong(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);

    private static async Task<L12AnalyticsCoverage> ReadAnalyticsCoverageAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, long exactDeckSnapshots)
    {
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT SUM(f.exact_facts),SUM(f.inferred_facts),SUM(f.partial_facts),
                   COUNT(DISTINCT CASE WHEN f.observed_sample=1
                         THEN printf('%s:%d',f.match_id,f.player_index) END)
            FROM fact_stats f;
            """;
        AddParameters(command, parameters);
        long exact = 0, inferred = 0, partial = 0, observed = 0;
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            exact = ReadLong(reader, 0);
            inferred = ReadLong(reader, 1);
            partial = ReadLong(reader, 2);
            observed = ReadLong(reader, 3);
        }
        var metrics = new[]
        {
            new L12AnalyticsMetricCoverage("inclusion", "participant", exactDeckSnapshots,
                exactDeckSnapshots, exactDeckSnapshots, 0, 0),
            new L12AnalyticsMetricCoverage("all-card-facts", "fact", exactDeckSnapshots,
                observed, exact, inferred, partial),
        };
        return new L12AnalyticsCoverage(L12CardFactKinds.SchemaVersion, L12CardFactKinds.Supported,
            exact, inferred, partial, exactDeckSnapshots, 0, false, metrics, AnalyticsLimitations);
    }

    private static L12CardAnalyticsItem ToAnalyticsItem(CardAnalyticsRow row,
        AnalyticsPopulation population)
    {
        var winRate = Rate(row.Wins, row.IncludedSamples);
        var comparisonSamples = population.SampleSize - row.IncludedSamples;
        var comparisonWins = population.Wins - row.Wins;
        var comparisonWinRate = RateOrNull(comparisonWins, comparisonSamples);
        var winRateConfidence = WilsonInterval(row.Wins, row.IncludedSamples);
        var baselineConfidence = comparisonSamples > 0
            ? WilsonInterval(comparisonWins, comparisonSamples) : null;
        var deltaConfidence = baselineConfidence is null
            ? null : DifferenceInterval(winRateConfidence, baselineConfidence);
        var metrics = new[]
        {
            new L12AnalyticsMetricCoverage("inclusion", "participant", population.SampleSize,
                row.IncludedSamples, row.IncludedSamples, 0, 0),
            MetricCoverage("draw", row.IncludedSamples, row.DrawnSamples, row.DrawCoverage),
            MetricCoverage("play", row.IncludedSamples, row.PlayedSamples, row.PlayCoverage),
            MetricCoverage("activation", row.IncludedSamples, row.ActivatedSamples, row.ActivationCoverage),
            MetricCoverage("settlement", row.ActivatedSamples, row.SettledSamples, row.SettlementCoverage),
        };
        var coverage = new L12AnalyticsCoverage(L12CardFactKinds.SchemaVersion, L12CardFactKinds.Supported,
            row.ExactFacts, row.InferredFacts, row.PartialFacts, row.IncludedSamples, 0, false,
            metrics, AnalyticsLimitations);
        return new L12CardAnalyticsItem(row.CardId, row.IncludedSamples, population.SampleSize,
            row.IncludedMatches, Rate(row.TotalQuantity, row.IncludedSamples),
            Rate(row.IncludedSamples, population.SampleSize), row.Wins, winRate, winRateConfidence,
            comparisonWinRate, baselineConfidence,
            comparisonWinRate is null ? null : winRate - comparisonWinRate.Value, deltaConfidence,
            row.DrawnMatches, row.PlayedMatches, row.DrawnSamples, row.PlayedSamples,
            row.ActivatedSamples, row.SettledSamples, row.ResolvedSamples, row.NegatedSamples,
            row.FizzledSamples, row.ActivatedCount, row.ResolvedCount, row.NegatedCount,
            row.FizzledCount, coverage);
    }

    private static L12AnalyticsMetricCoverage MetricCoverage(string metric, long eligible,
        long observed, MetricCoverageCounts coverage)
        => new(metric, "participant", eligible, observed, coverage.Exact, coverage.Inferred,
            coverage.Partial);

    private static async Task<IReadOnlyList<L12CardAnalyticsBreakdown>> ReadBreakdownsAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, string cardId,
        string dimension, string valueExpression)
    {
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        parameters["$card"] = cardId;
        parameters["$minimum"] = query.MinimumSampleSize;
        parameters["$take"] = MaximumAnalyticsBreakdowns;
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
            ORDER BY value
            LIMIT $take;
            """;
        AddParameters(command, parameters);
        var result = new List<L12CardAnalyticsBreakdown>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var populationSamples = reader.GetInt64(1);
            var populationWins = ReadLong(reader, 2);
            var included = ReadLong(reader, 3);
            var includedMatches = ReadLong(reader, 4);
            var wins = ReadLong(reader, 5);
            var winRate = Rate(wins, included);
            var comparisonSamples = populationSamples - included;
            var comparisonWins = populationWins - wins;
            var baseline = RateOrNull(comparisonWins, comparisonSamples);
            var winRateConfidence = WilsonInterval(wins, included);
            var baselineConfidence = comparisonSamples > 0
                ? WilsonInterval(comparisonWins, comparisonSamples) : null;
            result.Add(new L12CardAnalyticsBreakdown(dimension, reader.GetString(0), included,
                populationSamples, includedMatches, wins, winRate, winRateConfidence, baseline,
                baselineConfidence, baseline is null ? null : winRate - baseline.Value,
                baselineConfidence is null ? null : DifferenceInterval(winRateConfidence,
                    baselineConfidence)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<L12CardAnalyticsQuantityBucket>> ReadQuantityDistributionAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, string cardId)
    {
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        parameters["$card"] = cardId;
        parameters["$take"] = MaximumAnalyticsQuantityBuckets;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT CAST(i.quantity AS INTEGER),COUNT(*),
                   SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END)
            FROM inclusions i
            JOIN eligible e ON e.match_id=i.match_id AND e.player_index=i.player_index
            WHERE i.card_id=$card
            GROUP BY i.quantity
            ORDER BY i.quantity
            LIMIT $take;
            """;
        AddParameters(command, parameters);
        var result = new List<L12CardAnalyticsQuantityBucket>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var samples = reader.GetInt64(1);
            var wins = ReadLong(reader, 2);
            result.Add(new L12CardAnalyticsQuantityBucket(reader.GetInt32(0), samples, wins,
                Rate(wins, samples)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<L12CardAnalyticsTurnBucket>> ReadTurnDistributionAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, string cardId)
    {
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        parameters["$card"] = cardId;
        parameters["$take"] = MaximumAnalyticsTimingBuckets;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte},
            first_occurrence AS (
                SELECT e.match_id,e.player_index,f.first_draw_turn,f.first_play_turn
                FROM eligible e
                JOIN inclusions i ON i.match_id=e.match_id AND i.player_index=e.player_index
                                  AND i.card_id=$card
                LEFT JOIN fact_stats f ON f.match_id=e.match_id AND f.player_index=e.player_index
                                      AND f.card_id=$card
            ),
            timing AS (
                SELECT first_draw_turn AS turn,1 AS drawn,0 AS played
                FROM first_occurrence WHERE first_draw_turn IS NOT NULL
                UNION ALL
                SELECT first_play_turn AS turn,0 AS drawn,1 AS played
                FROM first_occurrence WHERE first_play_turn IS NOT NULL
            )
            SELECT turn,SUM(drawn),SUM(played)
            FROM timing
            GROUP BY turn
            ORDER BY turn
            LIMIT $take;
            """;
        AddParameters(command, parameters);
        var result = new List<L12CardAnalyticsTurnBucket>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new L12CardAnalyticsTurnBucket(reader.GetInt32(0), ReadLong(reader, 1),
                ReadLong(reader, 2)));
        return result;
    }

    private static async Task<IReadOnlyList<L12CardAnalyticsMatchup>> ReadMatchupsAsync(
        SqliteConnection connection, L12CardAnalyticsQuery query, string cardId)
    {
        var cte = MaterializedAnalyticsCte;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        parameters["$card"] = cardId;
        parameters["$minimum"] = query.MinimumSampleSize;
        parameters["$take"] = MaximumAnalyticsMatchups;
        var command = connection.CreateCommand();
        command.CommandText = $"""
            {cte}
            SELECT COALESCE(e.master_id,'unknown'),COALESCE(e.opponent_master_id,'unknown'),
                   COUNT(*),SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN i.card_id IS NOT NULL THEN 1 ELSE 0 END),
                   SUM(CASE WHEN i.card_id IS NOT NULL AND e.winner=e.player_index THEN 1 ELSE 0 END)
            FROM eligible e
            LEFT JOIN inclusions i ON i.match_id=e.match_id AND i.player_index=e.player_index
                                   AND i.card_id=$card
            GROUP BY e.master_id,e.opponent_master_id
            HAVING SUM(CASE WHEN i.card_id IS NOT NULL THEN 1 ELSE 0 END) >= $minimum
            ORDER BY e.master_id,e.opponent_master_id
            LIMIT $take;
            """;
        AddParameters(command, parameters);
        var result = new List<L12CardAnalyticsMatchup>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var eligible = reader.GetInt64(2);
            var eligibleWins = ReadLong(reader, 3);
            var included = ReadLong(reader, 4);
            var wins = ReadLong(reader, 5);
            var winRate = Rate(wins, included);
            var comparisonSamples = eligible - included;
            var comparisonWins = eligibleWins - wins;
            var baseline = RateOrNull(comparisonWins, comparisonSamples);
            var winRateConfidence = WilsonInterval(wins, included);
            var baselineConfidence = comparisonSamples > 0
                ? WilsonInterval(comparisonWins, comparisonSamples) : null;
            result.Add(new L12CardAnalyticsMatchup(reader.GetString(0), reader.GetString(1),
                included, eligible, wins, winRate, winRateConfidence, baseline,
                baselineConfidence, baseline is null ? null : winRate - baseline.Value,
                baselineConfidence is null ? null : DifferenceInterval(winRateConfidence,
                    baselineConfidence)));
        }
        return result;
    }

    private static L12AdminMatchSummary SanitizeAnalyticsRecentMatch(L12AdminMatchSummary match)
        => match with
        {
            Players = match.Players.Select(player => player with
            {
                AccountId = null,
                DisplayName = $"参赛方{player.PlayerIndex + 1}",
                DeckName = null,
            }).ToArray(),
            Error = null,
        };

    private static string InitiativeExpression(string alias)
        => $"CASE WHEN {alias}.first_player IS NULL THEN 'unknown' "
           + $"WHEN {alias}.first_player={alias}.player_index THEN 'first' ELSE 'second' END";

    private static double Rate(long numerator, long denominator)
        => denominator <= 0 ? 0 : Math.Round((double)numerator / denominator, 6,
            MidpointRounding.AwayFromZero);

    private static double? RateOrNull(long numerator, long denominator)
        => denominator <= 0 ? null : Rate(numerator, denominator);

    private static L12AnalyticsConfidenceInterval WilsonInterval(long successes, long samples)
    {
        if (samples <= 0) return new L12AnalyticsConfidenceInterval(0, 1);
        const double z = 1.959963984540054;
        var proportion = Math.Clamp((double)successes / samples, 0, 1);
        var zSquared = z * z;
        var denominator = 1 + zSquared / samples;
        var center = (proportion + zSquared / (2d * samples)) / denominator;
        var margin = z * Math.Sqrt(proportion * (1 - proportion) / samples
                                   + zSquared / (4d * samples * samples)) / denominator;
        return new L12AnalyticsConfidenceInterval(RoundRate(Math.Max(0, center - margin)),
            RoundRate(Math.Min(1, center + margin)));
    }

    private static L12AnalyticsConfidenceInterval DifferenceInterval(
        L12AnalyticsConfidenceInterval included, L12AnalyticsConfidenceInterval baseline)
        => new(RoundRate(Math.Max(-1, included.Low - baseline.High)),
            RoundRate(Math.Min(1, included.High - baseline.Low)));

    private static double RoundRate(double value)
        => Math.Round(value, 6, MidpointRounding.AwayFromZero);

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
