using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private sealed record AnalyticsSampleStructureCounts(
        long ParticipantSamples,
        long DistinctMatches,
        long DistinctPlayers,
        long KnownPlayerSamples,
        long AnonymousPlayerSamples,
        long MaximumPlayerContribution);

    private static async Task<IReadOnlyDictionary<string, AnalyticsSampleStructureCounts>>
        ReadAnalyticsSampleStructuresAsync(SqliteConnection connection, IReadOnlyCollection<string> cardIds)
    {
        if (cardIds.Count == 0)
            return new Dictionary<string, AnalyticsSampleStructureCounts>(StringComparer.OrdinalIgnoreCase);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var cardFilter = AnalyticsCardFilter(cardIds, parameters);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH samples AS (
                SELECT i.card_id,i.match_id,e.player_index,
                       CASE WHEN e.account_id IS NULL OR trim(e.account_id)=''
                            THEN printf('anonymous:%s:%d',i.match_id,e.player_index)
                            ELSE 'account:' || e.account_id END AS player_cluster,
                       CASE WHEN e.account_id IS NULL OR trim(e.account_id)='' THEN 0 ELSE 1 END AS known_player
                FROM temp.l12_analytics_inclusions i
                JOIN temp.l12_analytics_eligible e
                  ON e.match_id=i.match_id AND e.player_index=i.player_index
                WHERE i.card_id IN ({cardFilter})
            ), contributions AS (
                SELECT card_id,player_cluster,MAX(known_player) AS known_player,COUNT(*) AS sample_count
                FROM samples GROUP BY card_id,player_cluster
            ), maxima AS (
                SELECT card_id,MAX(CASE WHEN known_player=1 THEN sample_count ELSE 0 END)
                    AS maximum_contribution
                FROM contributions GROUP BY card_id
            )
            SELECT s.card_id,COUNT(*),COUNT(DISTINCT s.match_id),
                   COUNT(DISTINCT CASE WHEN s.known_player=1 THEN s.player_cluster END),
                   SUM(s.known_player),SUM(CASE WHEN s.known_player=0 THEN 1 ELSE 0 END),
                   COALESCE(m.maximum_contribution,0)
            FROM samples s
            JOIN maxima m ON m.card_id=s.card_id
            GROUP BY s.card_id;
            """;
        AddParameters(command, parameters);
        var result = new Dictionary<string, AnalyticsSampleStructureCounts>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = new AnalyticsSampleStructureCounts(reader.GetInt64(1),
                reader.GetInt64(2), reader.GetInt64(3), ReadLong(reader, 4), ReadLong(reader, 5),
                ReadLong(reader, 6));
        return result;
    }

    private static async Task<IReadOnlyDictionary<string, L12AnalyticsStratifiedComparison>>
        ReadStratifiedComparisonsAsync(SqliteConnection connection, IReadOnlyCollection<string> cardIds)
    {
        if (cardIds.Count == 0)
            return new Dictionary<string, L12AnalyticsStratifiedComparison>(StringComparer.OrdinalIgnoreCase);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var cardFilter = AnalyticsCardFilter(cardIds, parameters);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH included_strata AS (
                SELECT i.card_id,e.master_id,e.opponent_master_id,
                       CASE WHEN e.first_player=e.player_index THEN 'first'
                            WHEN e.first_player IN (0,1) THEN 'second' ELSE NULL END AS initiative,
                       e.effect_version,
                       CASE WHEN e.master_id IS NOT NULL AND trim(e.master_id)<>''
                                  AND e.opponent_master_id IS NOT NULL AND trim(e.opponent_master_id)<>''
                                  AND e.first_player IN (0,1) THEN 1 ELSE 0 END AS reliable,
                       COUNT(*) AS included_samples,
                       SUM(CASE WHEN e.winner=e.player_index THEN 1 ELSE 0 END) AS included_wins
                FROM temp.l12_analytics_inclusions i
                JOIN temp.l12_analytics_eligible e
                  ON e.match_id=i.match_id AND e.player_index=i.player_index
                WHERE i.card_id IN ({cardFilter})
                GROUP BY i.card_id,e.master_id,e.opponent_master_id,initiative,e.effect_version,reliable
            ), strata AS (
                SELECT s.card_id,s.master_id,s.opponent_master_id,s.initiative,s.effect_version,s.reliable,
                       s.included_samples,s.included_wins,
                       SUM(CASE WHEN not_including.card_id IS NULL AND e.match_id IS NOT NULL THEN 1 ELSE 0 END) AS comparison_samples,
                       SUM(CASE WHEN not_including.card_id IS NULL AND e.match_id IS NOT NULL
                                     AND e.winner=e.player_index THEN 1 ELSE 0 END)
                           AS comparison_wins
                FROM included_strata s
                LEFT JOIN temp.l12_analytics_eligible e
                  ON s.reliable=1
                 AND e.master_id=s.master_id AND e.opponent_master_id=s.opponent_master_id
                 AND e.effect_version=s.effect_version
                 AND CASE WHEN e.first_player=e.player_index THEN 'first'
                          WHEN e.first_player IN (0,1) THEN 'second' ELSE NULL END=s.initiative
                LEFT JOIN temp.l12_analytics_inclusions not_including
                  ON not_including.match_id=e.match_id AND not_including.player_index=e.player_index
                 AND not_including.card_id=s.card_id
                GROUP BY s.card_id,s.master_id,s.opponent_master_id,s.initiative,s.effect_version,
                         s.reliable,s.included_samples,s.included_wins
            )
            SELECT card_id,
                   SUM(CASE WHEN reliable=1 AND comparison_samples>0 THEN included_samples ELSE 0 END),
                   SUM(CASE WHEN reliable=1 AND comparison_samples>0 THEN comparison_samples ELSE 0 END),
                   SUM(CASE WHEN reliable=0 OR comparison_samples=0 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN reliable=0 OR comparison_samples=0 THEN included_samples ELSE 0 END),
                   SUM(CASE WHEN reliable=1 AND comparison_samples>0
                            THEN included_samples * (1.0 * comparison_wins / comparison_samples)
                            ELSE 0 END),
                   SUM(CASE WHEN reliable=1 AND comparison_samples>0 THEN included_wins ELSE 0 END)
            FROM strata GROUP BY card_id;
            """;
        AddParameters(command, parameters);
        var result = new Dictionary<string, L12AnalyticsStratifiedComparison>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var cardId = reader.GetString(0);
            var carried = ReadLong(reader, 1);
            var comparisons = ReadLong(reader, 2);
            var insufficient = ReadLong(reader, 3);
            var excluded = ReadLong(reader, 4);
            var weightedComparisonWins = reader.IsDBNull(5) ? 0d : reader.GetDouble(5);
            var carriedWins = ReadLong(reader, 6);
            double? baseline = carried <= 0 ? null : RoundRate(weightedComparisonWins / carried);
            var includedRate = carried <= 0 ? null : RateOrNull(carriedWins, carried);
            double? delta = baseline is null || includedRate is null
                ? null : RoundRate(includedRate.Value - baseline.Value);
            var uncertainty = carried <= 0
                ? new L12AnalyticsUncertainty("unknown", "not-estimated", Reason:
                    "No reliable stratum carried both included and non-included samples.")
                : new L12AnalyticsUncertainty("unknown", "not-estimated", Reason:
                    "Participants share matches and players may recur; no independent 95% interval is asserted.");
            result[cardId] = new L12AnalyticsStratifiedComparison(carried, comparisons, insufficient,
                excluded, baseline, delta, "included-sample-weighted-strata", uncertainty);
        }
        return result;
    }

    private static string AnalyticsCardFilter(IReadOnlyCollection<string> cardIds,
        IDictionary<string, object> parameters)
    {
        var names = new List<string>(cardIds.Count);
        var index = 0;
        foreach (var cardId in cardIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = $"$analyticsCard{index++}";
            names.Add(name);
            parameters[name] = cardId;
        }
        return string.Join(',', names);
    }

    private static L12AnalyticsSampleStructure ToSampleStructure(CardAnalyticsRow row,
        AnalyticsSampleStructureCounts? counts)
    {
        counts ??= new AnalyticsSampleStructureCounts(row.IncludedSamples, row.IncludedMatches,
            0, 0, row.IncludedSamples, 0);
        var matchClustered = counts.ParticipantSamples > counts.DistinctMatches;
        var playerClustered = counts.MaximumPlayerContribution > 1;
        var dependency = counts.AnonymousPlayerSamples > 0 ? "anonymous-player-dependency-unknown"
            : matchClustered && playerClustered ? "match-and-player-clustered"
            : matchClustered ? "match-clustered"
            : playerClustered ? "player-clustered"
            : "no-repeated-cluster-observed";
        var descriptive = WilsonInterval(row.Wins, row.IncludedSamples);
        var uncertainty = new L12AnalyticsUncertainty("approximation",
            "participant-wilson-independence-assumption", descriptive.Low, descriptive.High,
            "Descriptive only; repeated players and paired match outcomes are not independent.");
        return new L12AnalyticsSampleStructure(counts.ParticipantSamples, counts.DistinctMatches,
            counts.DistinctPlayers, counts.KnownPlayerSamples, counts.AnonymousPlayerSamples,
            counts.MaximumPlayerContribution, Rate(counts.MaximumPlayerContribution,
                counts.ParticipantSamples), dependency, uncertainty);
    }

    private static L12CardAnalyticsUsage ToUsage(CardAnalyticsRow row)
        => new([
            Usage("draw", row.DrawnSamples, row.DrawCoverage),
            Usage("play", row.PlayedSamples, row.PlayCoverage),
            Usage("activation", row.ActivatedSamples, row.ActivationCoverage),
            Usage("settlement", row.SettledSamples, row.SettlementCoverage),
        ]);

    private static L12AnalyticsUsageMetric Usage(string metric, long observedSamples,
        MetricCoverageCounts coverage)
        => new(metric, observedSamples, coverage.Exact + coverage.Inferred + coverage.Partial,
            coverage.Exact, coverage.Inferred, coverage.Partial, null, "event-observed-only");
}
