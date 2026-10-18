using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private const int MaximumStrategyStrata = 10_000;
    private const int MaximumStrategyConditions = 30;
    public Task<L12DisasterStrategyReport> ReadDisasterStrategyAsync(L12CardAnalyticsQuery query,
        CancellationToken cancellationToken = default)
        => ReadDisasterStrategyAsync(query, cancellationToken, new CardAnalyticsRequestTiming());

    internal async Task<L12DisasterStrategyReport> ReadDisasterStrategyAsync(L12CardAnalyticsQuery query,
        CancellationToken cancellationToken, CardAnalyticsRequestTiming timing)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CardAnalyticsHardBudget);
        var epoch = AnalyticsCacheEpoch;
        try
        {
            var normalized = NormalizeAnalyticsQuery(query);
            normalized = normalized with { DisasterOrigin = normalized.DisasterOrigin ?? "player" };
            if (normalized.MasterId is null || normalized.OpponentMasterId is null || normalized.Initiative is null)
                return new(normalized.MasterId, normalized.OpponentMasterId, normalized.Initiative,
                    normalized.MasterId is null ? "choose-master" : normalized.OpponentMasterId is null ? "choose-opponent" : "choose-initiative",
                    new(0, 0, 0, 0, 0, 0, 0), [], [], [], normalized.DisasterMode, normalized.DisasterPoolId, normalized.DisasterOrigin);
            return (await ExecuteCardAnalyticsAsync(AnalyticsResultCacheKey("disaster-strategy-v1", normalized),
                epoch, budget.Token, timing, execution => ComputeDisasterStrategy(execution, normalized)))!;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
            throw CardAnalyticsUnavailableException.TimedOut();
        }
        finally { timing.SetTotal(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }

    private sealed record StrategyStratum(string Party, int Step, string Origin, string Card,
        long TargetOpportunities, long TargetSelections, long TargetMatches, long ReferenceOpportunities,
        long ReferenceSelections);

    private static L12DisasterStrategyReport ComputeDisasterStrategy(CardAnalyticsExecution execution,
        L12CardAnalyticsQuery query)
    {
        var connection = execution.Connection;
        using var transaction = connection.BeginTransaction(deferred: true);
        // Read both cohorts once in the same snapshot. Exclusions, dates, season, version and A-side
        // initiative remain identical. B is applied below, not accidentally to the reference cohort.
        var baseQuery = query with { OpponentMasterId = null };
        execution.Measure(CardAnalyticsStage.ScopeTotal, () =>
        {
            PrepareAnalyticsScopeAsync(connection, baseQuery, includeFacts: false, transaction: transaction).GetAwaiter().GetResult();
            PrepareDisasterPopulation(execution, transaction, baseQuery);
        });
        using (var invalid = DisasterCommand(connection, transaction, """
            SELECT COUNT(*) FROM temp.l12_disaster_population p WHERE p.sample_status='invalid';
            """))
            if (Convert.ToInt64(invalid.ExecuteScalar()) != 0) throw DisasterConflict();
        ValidateStrategyChains(execution, transaction);
        L12DisasterAnalyticsCoverage coverage;
        using (var command = DisasterCommand(connection, transaction, """
            SELECT COUNT(*),COUNT(DISTINCT match_id),COALESCE(SUM(sample_status='usable'),0),
              COUNT(DISTINCT CASE WHEN sample_status='usable' THEN match_id END),
              COALESCE(SUM(sample_status='unknown'),0),COALESCE(SUM(sample_status='incomplete'),0),
              COALESCE(SUM(sample_status='not-applicable'),0)
            FROM temp.l12_disaster_population WHERE opponent_master_id=$opponent;
            """, ("$opponent", query.OpponentMasterId)))
        using (var reader = command.ExecuteReader())
        {
            reader.Read(); coverage = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6));
        }
        var pools = new List<L12DisasterAnalyticsPool>();
        using (var command = DisasterCommand(connection, transaction, """
            SELECT seed_hash,disaster_mode,pool_json FROM temp.l12_disaster_population
            WHERE opponent_master_id=$opponent AND seed_hash IS NOT NULL
            GROUP BY seed_hash,disaster_mode,pool_json ORDER BY MAX(started_utc) DESC,seed_hash LIMIT 101;
            """, ("$opponent", query.OpponentMasterId)))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) { execution.Check(); pools.Add(new(reader.GetString(0), reader.GetString(1), ReadDisasterJson<string[]>(reader.GetString(2)))); }
        var poolsTruncated = pools.Count > 100;
        if (poolsTruncated) pools.RemoveAt(100);

        using (var command = DisasterCommand(connection, transaction, """
            DROP TABLE IF EXISTS temp.l12_strategy_decisions;
            CREATE TEMP TABLE l12_strategy_decisions AS
            SELECT p.match_id,p.opponent_master_id=$opponent AS target,p.effect_version,p.seed_hash,
              CASE WHEN d.actor_index=p.player_index THEN 'self' ELSE 'opponent' END AS party,
              d.step,d.action,d.origin,d.chosen_card_id,
              CASE WHEN d.step>=4 THEN c.public_card_id END AS known_public,
              (SELECT json_group_array(json_object('Step',b.step,'CardId',b.chosen_card_id)) FROM
                (SELECT step,chosen_card_id FROM match_disaster_decisions WHERE match_id=p.match_id
                  AND step<3 AND step<d.step ORDER BY step) b) AS prefix_json,
              (SELECT json_group_array(card_id) FROM
                (SELECT card_id FROM match_disaster_candidates WHERE match_id=p.match_id AND step=d.step ORDER BY card_id)) AS candidates_json
            FROM temp.l12_disaster_population p
            JOIN match_disaster_captures c ON c.match_id=p.match_id
            JOIN match_disaster_decisions d ON d.match_id=p.match_id
            WHERE p.sample_status='usable' AND d.origin=$origin
              AND ($mode IS NULL OR p.disaster_mode=$mode) AND ($pool IS NULL OR p.seed_hash=$pool);
            CREATE INDEX temp.ix_l12_strategy_decisions_scope ON l12_strategy_decisions(target,party,step);
            """, ("$opponent", query.OpponentMasterId), ("$origin", query.DisasterOrigin),
            ("$mode", query.DisasterMode), ("$pool", query.DisasterPoolId)))
            command.ExecuteNonQuery();
        execution.Check();

        var strata = new List<StrategyStratum>();
        using (var command = DisasterCommand(connection, transaction, """
            SELECT party,step,origin,k.value,
              SUM(target),SUM(target AND chosen_card_id=k.value),COUNT(DISTINCT CASE WHEN target THEN match_id END),
              SUM(NOT target),SUM(NOT target AND chosen_card_id=k.value)
            FROM temp.l12_strategy_decisions d JOIN json_each(d.candidates_json) k
            GROUP BY party,step,origin,k.value,effect_version,seed_hash ORDER BY party,step,k.value,effect_version,seed_hash LIMIT 10001;
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                execution.Check(); strata.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8)));
            }
        if (strata.Count > MaximumStrategyStrata) throw new ArgumentException("对阵分层过多，请缩小日期、版本或卡池范围");
        var items = strata.GroupBy(s => (s.Party, s.Step, s.Origin, s.Card)).Where(g => g.Sum(s => s.TargetOpportunities) > 0)
            .Select(g =>
            {
                var opportunities = g.Sum(s => s.TargetOpportunities);
                var selected = g.Sum(s => s.TargetSelections);
                var common = g.Where(s => s.TargetOpportunities > 0 && s.ReferenceOpportunities > 0).ToArray();
                var matched = common.Sum(s => s.TargetOpportunities);
                var reference = common.Sum(s => s.ReferenceOpportunities);
                double? targetRate = matched == 0 ? null : (double)common.Sum(s => s.TargetSelections) / matched;
                double? referenceRate = matched == 0 ? null : common.Sum(s => s.TargetOpportunities * (double)s.ReferenceSelections / s.ReferenceOpportunities) / matched;
                var enough = matched >= 30 && reference >= 30;
                var comparison = new L12DisasterStrategyComparison(matched == 0 ? "unknown" : !enough ? "insufficient" : matched < opportunities ? "partial" : "available",
                    matched, opportunities, reference, common.Length, g.Count(s => s.TargetOpportunities > 0 && s.ReferenceOpportunities == 0),
                    targetRate, referenceRate, enough ? targetRate - referenceRate : null);
                return new L12DisasterStrategyItem(g.Key.Party, g.Key.Step, g.Key.Step < 3 ? "ban" : "pick", g.Key.Origin, g.Key.Card,
                    opportunities, selected, g.Sum(s => s.TargetMatches), RateOrNull(selected, opportunities), comparison);
            }).ToArray();

        // Each condition is an actually observed information set at ONE decision, not a fabricated
        // cross-step route. Never include a future public card, another player's private candidates,
        // their private pick, or even the current player's later choice in the condition key.
        var conditions = new List<L12DisasterStrategyCondition>();
        using (var command = DisasterCommand(connection, transaction, """
            WITH grouped AS (
              SELECT party,step,origin,effect_version,seed_hash,prefix_json,known_public,candidates_json,
                COUNT(*) AS samples,COUNT(DISTINCT match_id) AS matches,
                (SELECT json_group_array(json_object('CardId',chosen_card_id,'Selections',selections)) FROM
                  (SELECT x.chosen_card_id,COUNT(*) AS selections FROM temp.l12_strategy_decisions x
                    WHERE x.target=1 AND x.party=d.party AND x.step=d.step AND x.origin=d.origin
                      AND x.effect_version=d.effect_version AND x.seed_hash=d.seed_hash
                      AND x.prefix_json=d.prefix_json AND x.known_public IS d.known_public AND x.candidates_json=d.candidates_json
                    GROUP BY x.chosen_card_id ORDER BY selections DESC,x.chosen_card_id)) AS choices_json
              FROM temp.l12_strategy_decisions d WHERE target=1
              GROUP BY party,step,origin,effect_version,seed_hash,prefix_json,known_public,candidates_json)
            SELECT * FROM grouped ORDER BY samples DESC,party,step,effect_version,seed_hash,prefix_json,candidates_json LIMIT 31;
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                execution.Check();
                var samples = reader.GetInt64(8);
                var choices = ReadDisasterJson<StrategyChoiceCount[]>(reader.GetString(10));
                var identity = string.Join('\n', Enumerable.Range(0, 8).Select(i => reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()));
                conditions.Add(new(DisasterHash(identity), reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), ReadDisasterJson<L12DisasterStrategyPriorBan[]>(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6), ReadDisasterJson<string[]>(reader.GetString(7)), samples, reader.GetInt64(9),
                    choices.Select(c => new L12DisasterStrategyChoice(c.CardId, c.Selections, RateOrNull(c.Selections, samples))).ToArray()));
            }
        var truncated = conditions.Count > MaximumStrategyConditions;
        if (truncated) conditions.RemoveAt(MaximumStrategyConditions);
        execution.Check(); transaction.Commit();
        return new(query.MasterId, query.OpponentMasterId, query.Initiative,
            items.Length == 0 ? "no-observations" : coverage.UsableMatches < 30 ? "insufficient" : "observations",
            coverage, pools, items, conditions, query.DisasterMode, query.DisasterPoolId, query.DisasterOrigin!,
            ConditionsTruncated: truncated, PoolsTruncated: poolsTruncated);
    }

    private sealed record StrategyChoiceCount(string CardId, long Selections);

    private static void ValidateStrategyChains(CardAnalyticsExecution execution, SqliteTransaction transaction)
    {
        // Both cohorts must bind to their own native pool before seed_hash can define a stratum.
        // Validate distinct seeds once in this read snapshot, not one diagnostic query per match.
        using (var seeds = DisasterCommand(execution.Connection, transaction, """
            SELECT DISTINCT disaster_mode,effect_version,pool_json,seed_hash
            FROM temp.l12_disaster_population WHERE sample_status='usable';
            """))
        using (var reader = seeds.ExecuteReader())
            while (reader.Read())
            {
                execution.Check();
                if (reader.GetString(3) != DisasterSeedHash(reader.GetString(0), reader.GetString(1), reader.GetString(2)))
                    throw DisasterConflict();
            }
        using var command = DisasterCommand(execution.Connection, transaction, """
            SELECT COUNT(*) FROM temp.l12_disaster_population p JOIN match_disaster_captures c ON c.match_id=p.match_id
            WHERE p.sample_status='usable' AND (
              NOT EXISTS(SELECT 1 FROM json_each(c.pool_json) k WHERE k.value=c.public_card_id)
              OR EXISTS(SELECT 1 FROM match_disaster_decisions d WHERE d.match_id=p.match_id AND (
                d.offered_sequence<0 OR d.resolved_sequence<d.offered_sequence OR d.offered_revision<0 OR d.resolved_revision<d.offered_revision
                OR EXISTS(SELECT 1 FROM match_disaster_decisions prior WHERE prior.match_id=d.match_id AND prior.step<d.step
                  AND (prior.resolved_sequence>d.offered_sequence OR prior.resolved_revision>d.offered_revision))
                OR EXISTS(SELECT 1 FROM match_disaster_candidates k JOIN match_disaster_decisions b ON b.match_id=k.match_id
                  AND b.step<3 AND b.step<d.step AND b.chosen_card_id=k.card_id WHERE k.match_id=d.match_id AND k.step=d.step)
                OR d.step<3 AND d.chosen_card_id=c.public_card_id
                OR d.step>=4 AND (c.public_sequence IS NULL OR c.public_revision IS NULL
                  OR c.public_sequence>d.offered_sequence OR c.public_revision>d.offered_revision
                  OR EXISTS(SELECT 1 FROM match_disaster_candidates k WHERE k.match_id=d.match_id AND k.step=d.step AND k.card_id=c.public_card_id))
                OR d.step=5 AND EXISTS(SELECT 1 FROM match_disaster_candidates a JOIN match_disaster_candidates b
                  ON b.match_id=a.match_id AND b.card_id=a.card_id AND b.step=4 WHERE a.match_id=d.match_id AND a.step=5)))
              OR EXISTS(SELECT 1 FROM match_disaster_decisions b WHERE b.match_id=p.match_id AND b.step=2
                AND (b.resolved_sequence>c.public_sequence OR b.resolved_revision>c.public_revision))
              OR NOT EXISTS(SELECT 1 FROM json_each(c.composition_json) k WHERE json_extract(k.value,'$.Source')='fixed'
                AND json_extract(k.value,'$.CardId')=$fixed AND json_extract(k.value,'$.OwnerIndex') IS NULL)
              OR NOT EXISTS(SELECT 1 FROM json_each(c.composition_json) k WHERE json_extract(k.value,'$.Source')='random-public'
                AND json_extract(k.value,'$.CardId')=c.public_card_id AND json_extract(k.value,'$.OwnerIndex') IS NULL)
              OR EXISTS(SELECT 1 FROM match_disaster_decisions d WHERE d.match_id=p.match_id AND d.action='pick'
                AND NOT EXISTS(SELECT 1 FROM json_each(c.composition_json) k WHERE json_extract(k.value,'$.Source')='player-pick'
                  AND json_extract(k.value,'$.CardId')=d.chosen_card_id AND json_extract(k.value,'$.OwnerIndex')=d.actor_index)));
            """, ("$fixed", L12ActiveDisasterRules.AnnihilationCardId));
        if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw DisasterConflict();
        execution.Check();
    }
}
