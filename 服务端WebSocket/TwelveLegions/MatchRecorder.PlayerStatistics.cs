using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12PlayerStatLine(int Games, int Wins, int Losses, int Draws,
    int FirstGames, int FirstWins, int SecondGames, int SecondWins);
public sealed record L12PlayerMasterStatistics(string MasterId, string MasterName,
    L12PlayerStatLine Overall, L12PlayerStatLine Ranked);
public sealed record L12PlayerStatisticsView(L12PlayerStatLine Overall, L12PlayerStatLine Ranked,
    IReadOnlyList<L12PlayerMasterStatistics> Masters, DateTimeOffset? UpdatedAt,
    string Range = "all", DateTimeOffset? FromUtc = null, DateTimeOffset? UntilUtc = null,
    string? SeasonId = null);

public sealed partial class MatchRecorder
{
    private sealed class MutableStatLine
    {
        public int Games;
        public int Wins;
        public int Losses;
        public int Draws;
        public int FirstGames;
        public int FirstWins;
        public int SecondGames;
        public int SecondWins;

        public void Add(int playerIndex, int? winner, int? firstPlayer)
        {
            Games++;
            if (winner == playerIndex) Wins++;
            else if (winner is 0 or 1) Losses++;
            else Draws++;
            if (firstPlayer == playerIndex) { FirstGames++; if (winner == playerIndex) FirstWins++; }
            else if (firstPlayer is 0 or 1) { SecondGames++; if (winner == playerIndex) SecondWins++; }
        }

        public L12PlayerStatLine View() => new(Games, Wins, Losses, Draws, FirstGames, FirstWins, SecondGames, SecondWins);
    }

    public async Task<L12PlayerStatisticsView> PlayerStatisticsAsync(string accountId, string legacyPlayerName,
        IReadOnlyCollection<string>? excludedMatchIds = null,
        IReadOnlyCollection<string>? excludedAccountIds = null,
        string range = "all",
        L12SeasonConfig? currentSeason = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedRange = string.IsNullOrWhiteSpace(range) ? "all" : range.Trim().ToLowerInvariant();
        var now = _utcNow().ToUniversalTime();
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? untilUtc = null;
        string? seasonId = null;
        switch (normalizedRange)
        {
            case "all": break;
            case "7d": fromUtc = now.AddDays(-7); untilUtc = now; break;
            case "30d": fromUtc = now.AddDays(-30); untilUtc = now; break;
            case "season":
                if (currentSeason is null || string.IsNullOrWhiteSpace(currentSeason.Id))
                    throw new ArgumentException("当前赛季定义不可用", nameof(currentSeason));
                seasonId = currentSeason.Id;
                fromUtc = currentSeason.StartsAt?.ToUniversalTime();
                var seasonEnd = currentSeason.EndsAt?.ToUniversalTime();
                untilUtc = seasonEnd.HasValue && seasonEnd.Value < now ? seasonEnd : now;
                break;
            default: throw new ArgumentException("战绩时间范围只支持 7d、30d 或 season", nameof(range));
        }
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        var exclusions = string.Empty;
        if (excludedMatchIds is { Count: > 0 })
        {
            exclusions += "\n  AND NOT EXISTS(SELECT 1 FROM json_each($excludedMatches) excluded WHERE excluded.value=m.match_id)";
            command.Parameters.AddWithValue("$excludedMatches", System.Text.Json.JsonSerializer.Serialize(
                excludedMatchIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)));
        }
        if (excludedAccountIds is { Count: > 0 })
        {
            exclusions += "\n  AND NOT EXISTS(SELECT 1 FROM json_each($excludedAccounts) excluded "
                + "WHERE excluded.value=m.account_0 OR excluded.value=m.account_1)";
            command.Parameters.AddWithValue("$excludedAccounts", System.Text.Json.JsonSerializer.Serialize(
                excludedAccountIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)));
        }
        if (fromUtc.HasValue)
        {
            exclusions += "\n  AND julianday(m.ended_utc)>=julianday($fromUtc)";
            command.Parameters.AddWithValue("$fromUtc", fromUtc.Value.ToString("O"));
        }
        if (untilUtc.HasValue)
        {
            exclusions += "\n  AND julianday(m.ended_utc)<=julianday($untilUtc)";
            command.Parameters.AddWithValue("$untilUtc", untilUtc.Value.ToString("O"));
        }
        if (!string.IsNullOrWhiteSpace(seasonId))
        {
            exclusions += "\n  AND m.season_id=$seasonId";
            command.Parameters.AddWithValue("$seasonId", seasonId);
        }
        command.CommandText = """
            SELECT m.mode_id,m.winner,m.first_player,m.ended_utc,
                   CASE WHEN m.account_0=$account OR (m.account_0 IS NULL AND m.player_0=$player) THEN 0 ELSE 1 END AS player_index,
                   COALESCE(p.master_id,''),COALESCE(p.master_name,'')
            FROM matches m
            LEFT JOIN match_participants p ON p.match_id=m.match_id
              AND p.player_index=CASE WHEN m.account_0=$account OR (m.account_0 IS NULL AND m.player_0=$player) THEN 0 ELSE 1 END
            WHERE m.ended_utc IS NOT NULL AND COALESCE(m.error,'')='' AND COALESCE(m.mode_id,'legacy')<>'sandbox'
              AND (m.account_0=$account OR m.account_1=$account
                   OR (m.account_0 IS NULL AND m.player_0=$player)
                   OR (m.account_1 IS NULL AND m.player_1=$player))
            """ + exclusions + "\nORDER BY m.ended_utc DESC;";
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$player", legacyPlayerName);
        var overall = new MutableStatLine();
        var ranked = new MutableStatLine();
        var masters = new Dictionary<string, (string Name, MutableStatLine Overall, MutableStatLine Ranked)>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? updatedAt = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mode = reader.GetString(0);
            var winner = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
            var first = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
            if (!updatedAt.HasValue && DateTimeOffset.TryParse(reader.GetString(3), out var parsed)) updatedAt = parsed;
            var playerIndex = reader.GetInt32(4);
            var masterId = reader.GetString(5);
            var masterName = reader.GetString(6);
            overall.Add(playerIndex, winner, first);
            var isRanked = string.Equals(mode, "ranked", StringComparison.OrdinalIgnoreCase);
            if (isRanked) ranked.Add(playerIndex, winner, first);
            if (string.IsNullOrWhiteSpace(masterId)) continue;
            if (!masters.TryGetValue(masterId, out var master))
                master = (string.IsNullOrWhiteSpace(masterName) ? masterId : masterName, new MutableStatLine(), new MutableStatLine());
            master.Overall.Add(playerIndex, winner, first);
            if (isRanked) master.Ranked.Add(playerIndex, winner, first);
            masters[masterId] = master;
        }
        return new(overall.View(), ranked.View(), masters.Select(pair => new L12PlayerMasterStatistics(pair.Key,
                pair.Value.Name, pair.Value.Overall.View(), pair.Value.Ranked.View()))
            .OrderByDescending(item => item.Ranked.Games).ThenByDescending(item => item.Overall.Games).ToArray(),
            updatedAt, normalizedRange, fromUtc, untilUtc, seasonId);
    }
}
