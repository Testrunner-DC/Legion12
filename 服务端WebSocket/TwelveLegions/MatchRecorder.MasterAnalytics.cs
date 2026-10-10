using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public async Task<L12MasterAnalyticsReport> ReadMasterAnalyticsAsync(L12CardAnalyticsQuery query)
    {
        var selectedMasterId = string.IsNullOrWhiteSpace(query.MasterId) ? null : query.MasterId.Trim();
        var scope = NormalizeAnalyticsQuery(query with { MasterId = null, Page = 1, Search = null });
        IReadOnlyList<L12MasterAnalyticsItem> items;
        IReadOnlyList<L12MasterMatchup> matchups;
        IReadOnlyList<L12MasterAnalyticsTrend> trend;
        IReadOnlyList<L12PopularMasterDeck> popularDecks;
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await PrepareAnalyticsScopeAsync(connection, scope, includeFacts: false);
            var totalSamples = Convert.ToInt64(await ScalarAsync(connection,
                "SELECT COUNT(*) FROM temp.l12_analytics_eligible;"));
            var totalDecks = Convert.ToInt64(await ScalarAsync(connection, """
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
                """));
            items = await ReadMasterItemsAsync(connection, totalSamples, totalDecks);
            matchups = await ReadMasterMatchupsAsync(connection);
            trend = selectedMasterId is null ? [] : await ReadMasterTrendAsync(connection, selectedMasterId);
            popularDecks = selectedMasterId is null ? [] : await ReadPopularMasterDecksAsync(connection, selectedMasterId);
        }
        var cards = selectedMasterId is null ? null : await ListCardAnalyticsAsync(scope with
        {
            MasterId = selectedMasterId,
            Page = 1,
            Limit = 20,
            Sort = "iwd",
            Direction = "desc",
        });
        var orderedItems = string.Equals(query.Sort, "win-rate", StringComparison.OrdinalIgnoreCase)
            ? items.OrderByDescending(item => item.WinRate).ThenByDescending(item => item.ParticipantSamples).ToArray()
            : items.OrderByDescending(item => item.UsageRate).ThenByDescending(item => item.ParticipantSamples).ToArray();
        return new(orderedItems, matchups, trend, selectedMasterId, popularDecks, cards);
    }

    private static async Task<IReadOnlyList<L12MasterAnalyticsItem>> ReadMasterItemsAsync(
        SqliteConnection connection, long totalSamples, long totalDecks)
    {
        var command = connection.CreateCommand();
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

    private static async Task<IReadOnlyList<L12MasterMatchup>> ReadMasterMatchupsAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT master_id,opponent_master_id,COUNT(*),
                   SUM(CASE WHEN winner=player_index THEN 1 ELSE 0 END)
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
                samples < 30 ? null : Rate(wins, samples)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<L12MasterAnalyticsTrend>> ReadMasterTrendAsync(
        SqliteConnection connection, string masterId)
    {
        var command = connection.CreateCommand();
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
        SqliteConnection connection, string masterId)
    {
        var command = connection.CreateCommand();
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

    private static async Task<object> ScalarAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() ?? 0L;
    }
}
