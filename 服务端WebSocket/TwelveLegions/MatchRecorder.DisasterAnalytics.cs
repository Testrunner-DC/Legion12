using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    // Operates on the same bounded worker/transaction and eligible population as the master report.
    // No replay loading and no per-match diagnostic traversal on this HTTP path.
    private static L12DisasterAnalyticsReport ComputeDisasterAnalytics(CardAnalyticsExecution execution,
        SqliteTransaction transaction, L12CardAnalyticsQuery query)
    {
        var connection = execution.Connection;
        using (var scope = DisasterCommand(connection, transaction, """
            DROP TABLE IF EXISTS temp.l12_disaster_population;
            CREATE TEMP TABLE l12_disaster_population AS
            SELECT e.*,c.disaster_mode,c.seed_hash,c.pool_json,
              CASE
                WHEN c.match_id IS NULL OR c.capture_version<>1 OR c.source<>'native'
                  OR e.first_player IS NULL OR e.first_player NOT IN (0,1) THEN 'unknown'
                WHEN c.setup_status<>'complete' THEN 'incomplete'
                WHEN c.disaster_mode IN ('none','random') THEN 'not-applicable'
                WHEN c.disaster_mode IN ('all','season') AND c.coverage='exact'
                  AND c.effect_version=e.effect_version
                  AND json_valid(c.pool_json)=1 AND json_valid(c.composition_json)=1
                  AND json_array_length(c.composition_json)=4 AND c.public_card_id IS NOT NULL
                  AND (SELECT COUNT(*) FROM match_disaster_decisions d WHERE d.match_id=e.match_id)=5
                  AND NOT EXISTS(SELECT 1 FROM match_disaster_decisions d WHERE d.match_id=e.match_id
                    AND (d.resolved_sequence IS NULL OR d.resolved_revision IS NULL
                      OR d.origin IS NULL OR d.chosen_card_id IS NULL
                      OR d.actor_index<>CASE WHEN d.step IN (1,5) THEN 1-e.first_player ELSE e.first_player END
                      OR d.action<>CASE WHEN d.step<3 THEN 'ban' ELSE 'pick' END
                      OR NOT EXISTS(SELECT 1 FROM match_disaster_candidates k WHERE k.match_id=d.match_id
                        AND k.step=d.step AND k.card_id=d.chosen_card_id)
                      OR (SELECT COUNT(*) FROM match_disaster_candidates k WHERE k.match_id=d.match_id AND k.step=d.step)
                        <>CASE WHEN d.step<3 THEN json_array_length(c.pool_json)-d.step WHEN d.step=4 THEN 3 ELSE 2 END))
                  AND NOT EXISTS(SELECT 1 FROM match_disaster_candidates k WHERE k.match_id=e.match_id
                    AND NOT EXISTS(SELECT 1 FROM json_each(c.pool_json) pool WHERE pool.value=k.card_id))
                THEN 'usable' ELSE 'invalid'
              END AS sample_status
            FROM temp.l12_analytics_eligible e
            LEFT JOIN match_disaster_captures c ON c.match_id=e.match_id
            WHERE $master IS NULL OR e.master_id=$master;
            CREATE UNIQUE INDEX temp.ix_l12_disaster_population_owner
              ON l12_disaster_population(match_id,player_index);
            """, ("$master", query.MasterId)))
            scope.ExecuteNonQuery();
        execution.Check();

        L12DisasterAnalyticsCoverage coverage;
        using (var command = DisasterCommand(connection, transaction, """
            SELECT COUNT(*),COUNT(DISTINCT match_id),
              COALESCE(SUM(sample_status='usable'),0),COUNT(DISTINCT CASE WHEN sample_status='usable' THEN match_id END),
              COALESCE(SUM(sample_status='unknown'),0),COALESCE(SUM(sample_status='incomplete'),0),
              COALESCE(SUM(sample_status='not-applicable'),0),COALESCE(SUM(sample_status='invalid'),0)
            FROM temp.l12_disaster_population;
            """))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetInt64(7) != 0)
                throw new InvalidDataException("天灾禁选统计事实不完整，不能展示确定结果");
            coverage = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6));
        }

        var pools = new List<L12DisasterAnalyticsPool>();
        using (var command = DisasterCommand(connection, transaction, """
            SELECT seed_hash,disaster_mode,pool_json FROM temp.l12_disaster_population
            WHERE seed_hash IS NOT NULL GROUP BY seed_hash,disaster_mode,pool_json
            ORDER BY MAX(started_utc) DESC,seed_hash LIMIT 101;
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                execution.Check();
                pools.Add(new(reader.GetString(0), reader.GetString(1), ReadDisasterJson<string[]>(reader.GetString(2))));
            }
        var poolsTruncated = pools.Count > 100;
        if (poolsTruncated) pools.RemoveAt(100);

        var items = new List<L12DisasterAnalyticsRow>();
        using (var command = DisasterCommand(connection, transaction, """
            SELECT k.card_id,CASE WHEN p.first_player=p.player_index THEN 'first' ELSE 'second' END AS initiative,
              d.action,d.step,d.origin,COUNT(*),SUM(d.chosen_card_id=k.card_id),
              SUM(d.chosen_card_id=k.card_id AND p.winner=p.player_index),COUNT(DISTINCT p.match_id),
              COUNT(DISTINCT CASE WHEN d.chosen_card_id=k.card_id THEN p.match_id END)
            FROM temp.l12_disaster_population p
            JOIN match_disaster_decisions d ON d.match_id=p.match_id AND d.actor_index=p.player_index
            JOIN match_disaster_candidates k ON k.match_id=d.match_id AND k.step=d.step
            WHERE p.sample_status='usable' AND $master IS NOT NULL
              AND ($mode IS NULL OR p.disaster_mode=$mode)
              AND ($pool IS NULL OR p.seed_hash=$pool)
              AND ($origin IS NULL OR d.origin=$origin)
            GROUP BY k.card_id,initiative,d.action,d.step,d.origin
            ORDER BY initiative,d.step,d.origin,k.card_id LIMIT 1001;
            """, ("$master", query.MasterId), ("$mode", query.DisasterMode),
            ("$pool", query.DisasterPoolId), ("$origin", query.DisasterOrigin)))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                execution.Check();
                var opportunities = reader.GetInt64(5);
                var selections = reader.GetInt64(6);
                var wins = reader.GetInt64(7);
                items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetString(4), opportunities, selections, wins, reader.GetInt64(8), reader.GetInt64(9),
                    RateOrNull(selections, opportunities), selections < 30 ? null : RateOrNull(wins, selections)));
            }
        if (items.Count > 1000) throw new ArgumentException("天灾禁选分组过多，请缩小主宰或卡池范围");
        return new(coverage, items, pools, query.MasterId, query.DisasterMode, query.DisasterPoolId,
            query.DisasterOrigin, PoolsTruncated: poolsTruncated);
    }

    private static async Task<L12DisasterSetupSummary> ReadDisasterSummaryInSnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, L12AdminMatchSummary summary,
        CancellationToken cancellationToken)
    {
        if (summary.EndedUtc is null)
            return new("private", "active", null, null, null, [], [], null);
        using var command = DisasterCommand(connection, transaction,
            "SELECT first_player FROM matches WHERE match_id=$id;", ("$id", summary.MatchId));
        var firstValue = await command.ExecuteScalarAsync(cancellationToken);
        var first = firstValue is long index && index is 0 or 1 ? (int)index : -1;
        var facts = await ReadDisasterSetupFactsInSnapshotAsync(connection, transaction, summary.MatchId,
            first, cancellationToken);
        return new(facts.Coverage, facts.Status, facts.Mode, first >= 0 ? first : null, facts.PublicCardId,
            facts.Composition.Select(card => new L12DisasterSetupCard(card.CardId, card.Source, card.OwnerIndex)).ToArray(),
            facts.Decisions.Select(decision => new L12DisasterSetupDecision(decision.Step, decision.Action,
                decision.ActorIndex, first < 0 ? null : decision.ActorIndex == first ? "first" : "second",
                decision.Candidates, decision.ChosenCardId, decision.Origin, decision.OfferedSequence,
                decision.ResolvedSequence)).ToArray(), facts.EffectVersion);
    }
}
