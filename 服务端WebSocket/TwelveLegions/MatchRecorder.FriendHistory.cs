using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12FriendHistoryMatch(string StartedUtc, string EndedUtc, string OpponentName,
    string MasterName, string OpponentMasterName, string ModeId, string Result);
public sealed record L12FriendHistoryView(string AccountId, string Username, L12PlayerStatisticsView Statistics,
    IReadOnlyList<L12FriendHistoryMatch> RecentMatches, int RecentLimit = 10);

public sealed partial class MatchRecorder
{
    internal async Task<L12FriendHistoryView> ReadFriendHistoryAsync(L12FriendHistoryReadLease lease,
        string range, CancellationToken cancellationToken)
    {
        var now = _utcNow().ToUniversalTime();
        DateTimeOffset? from = range switch
        {
            "7d" => now.AddDays(-7), "30d" => now.AddDays(-30),
            "season" => lease.Season.StartsAt?.ToUniversalTime(),
            _ => throw new ArgumentException("战绩时间范围只支持 7d、30d 或 season"),
        };
        if (range == "season" && string.IsNullOrWhiteSpace(lease.Season.Id)) throw new ArgumentException("当前赛季定义不可用");
        var until = range == "season" && lease.Season.EndsAt is { } end && end < now ? end.ToUniversalTime() : now;
        var seasonId = range == "season" ? lease.Season.Id : null;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // Both queries use one read snapshot. Neither reads a replay, journal, deck or command payload.
        using var transaction = connection.BeginTransaction(deferred: true);
        SqliteCommand Query(string projection, string joins, string suffix)
        {
            var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = $"""
                SELECT {projection} FROM matches m {joins}
                WHERE m.ended_utc IS NOT NULL AND COALESCE(m.error,'')=''
                  AND COALESCE(m.mode_id,'legacy')<>'sandbox'
                  AND (m.account_0=$account OR m.account_1=$account)
                  AND julianday(m.ended_utc)<=julianday($until)
                  AND NOT EXISTS(SELECT 1 FROM json_each($excludedMatches) e WHERE e.value=m.match_id)
                  AND NOT EXISTS(SELECT 1 FROM json_each($excludedAccounts) e WHERE e.value=m.account_0 OR e.value=m.account_1)
                """ + (from.HasValue ? " AND julianday(m.ended_utc)>=julianday($from)" : "")
                + (seasonId is not null ? " AND m.season_id=$season" : "") + suffix;
            query.Parameters.AddWithValue("$account", lease.TargetId);
            query.Parameters.AddWithValue("$until", until.ToString("O"));
            query.Parameters.AddWithValue("$excludedMatches", System.Text.Json.JsonSerializer.Serialize(lease.ExcludedMatches));
            query.Parameters.AddWithValue("$excludedAccounts", System.Text.Json.JsonSerializer.Serialize(lease.ExcludedAccounts));
            if (from.HasValue) query.Parameters.AddWithValue("$from", from.Value.ToString("O"));
            if (seasonId is not null) query.Parameters.AddWithValue("$season", seasonId);
            return query;
        }
        var ownIndex = "CASE WHEN m.account_0=$account THEN 0 ELSE 1 END";
        var overall = new MutableStatLine();
        var ranked = new MutableStatLine();
        var masters = new Dictionary<string, (string Name, MutableStatLine Overall, MutableStatLine Ranked)>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? updatedAt = null;
        using (var query = Query($"m.mode_id,m.winner,m.first_player,m.ended_utc,{ownIndex},COALESCE(p.master_id,''),COALESCE(p.master_name,'')",
            $"LEFT JOIN match_participants p ON p.match_id=m.match_id AND p.player_index={ownIndex}", " ORDER BY m.ended_utc DESC"))
        {
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var mode = reader.GetString(0);
                var winner = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
                var first = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                if (!updatedAt.HasValue && DateTimeOffset.TryParse(reader.GetString(3), out var parsed)) updatedAt = parsed;
                var player = reader.GetInt32(4);
                var masterId = reader.GetString(5);
                var masterName = reader.GetString(6);
                overall.Add(player, winner, first);
                var isRanked = mode.Equals("ranked", StringComparison.OrdinalIgnoreCase);
                if (isRanked) ranked.Add(player, winner, first);
                if (string.IsNullOrWhiteSpace(masterId)) continue;
                if (!masters.TryGetValue(masterId, out var master)) master = (string.IsNullOrWhiteSpace(masterName) ? masterId : masterName, new(), new());
                master.Overall.Add(player, winner, first);
                if (isRanked) master.Ranked.Add(player, winner, first);
                masters[masterId] = master;
            }
        }
        var recent = new List<L12FriendHistoryMatch>(10);
        using (var query = Query($"m.started_utc,m.ended_utc,CASE WHEN m.account_0=$account THEN m.player_1 ELSE m.player_0 END,COALESCE(p.master_name,''),COALESCE(o.master_name,''),m.mode_id,m.winner,{ownIndex}",
            $"LEFT JOIN match_participants p ON p.match_id=m.match_id AND p.player_index={ownIndex} LEFT JOIN match_participants o ON o.match_id=m.match_id AND o.player_index=1-({ownIndex})",
            " ORDER BY m.ended_utc DESC,m.match_id DESC LIMIT 10"))
        {
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var winner = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                recent.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), winner == reader.GetInt32(7) ? "win" : winner is 0 or 1 ? "loss" : "draw"));
            }
        }
        transaction.Commit();
        var stats = new L12PlayerStatisticsView(overall.View(), ranked.View(), masters.Select(pair =>
            new L12PlayerMasterStatistics(pair.Key, pair.Value.Name, pair.Value.Overall.View(), pair.Value.Ranked.View()))
            .OrderByDescending(item => item.Ranked.Games).ThenByDescending(item => item.Overall.Games).ToArray(), updatedAt, range, from, until, seasonId);
        return new(lease.TargetId, lease.TargetName, stats, recent);
    }
}
