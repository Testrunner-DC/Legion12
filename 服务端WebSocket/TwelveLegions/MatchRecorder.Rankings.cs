using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    // The legacy-import query deliberately excludes outboxes. Analytics must not share that filter.
    public async Task<IReadOnlyList<L12RankingMatch>> ListRankedAnalyticsMatchesAsync(int limit = 20_000)
    {
        limit = Math.Clamp(limit, 1, 20_000);
        var result = (await ListRankingMatchesAsync(limit))
            .Where(item => DateTimeOffset.TryParse(item.StartedUtc, out _)
                && DateTimeOffset.TryParse(item.EndedUtc, out _))
            .DistinctBy(item => item.MatchId, StringComparer.OrdinalIgnoreCase).ToDictionary(item => item.MatchId,
            StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.player_0,m.player_1,m.started_utc,m.ended_utc,m.winner,m.first_player,
                   p0.master_id,p0.master_name,p1.master_id,p1.master_name,
                   m.account_0,m.account_1,o.payload_json,o.payload_hash
            FROM matches m
            JOIN ranked_settlement_outbox o ON o.match_id=m.match_id
            JOIN match_participants p0 ON p0.match_id=m.match_id AND p0.player_index=0
            JOIN match_participants p1 ON p1.match_id=m.match_id AND p1.player_index=1
            WHERE m.mode_id='ranked' AND m.ended_utc IS NOT NULL AND m.winner IN (0,1)
              AND COALESCE(m.error,'')='' AND o.status='applied' AND COALESCE(o.last_error,'')=''
              AND NOT EXISTS (SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=m.match_id)
            ORDER BY m.started_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            try
            {
                var json = reader.GetString(13);
                if (!string.Equals(PersistenceHash(json), reader.GetString(14), StringComparison.Ordinal)) continue;
                var payload = JsonSerializer.Deserialize<L12RankedSettlementEnvelope>(json, RankedPersistenceJson);
                if (payload is null) continue;
                ValidateSettlement(payload);
                if (payload.MatchId != reader.GetString(0) || payload.Winner != reader.GetInt32(5)
                    || payload.FirstAccountId != reader.GetString(11) || payload.SecondAccountId != reader.GetString(12)
                    || payload.FirstMasterId != reader.GetString(7) || payload.SecondMasterId != reader.GetString(9)
                    || !DateTimeOffset.TryParse(reader.GetString(3), out var started)
                    || !DateTimeOffset.TryParse(reader.GetString(4), out var ended)
                    || started > ended || payload.EndedAt != ended
                    || reader.IsDBNull(6) || reader.GetInt32(6) is not (0 or 1)
                    || payload.ConclusionKind.Contains("invalid", StringComparison.OrdinalIgnoreCase)) continue;
                result[payload.MatchId] = new L12RankingMatch(payload.MatchId, reader.GetString(1), reader.GetString(2),
                    payload.StartedAt.ToString("O"), reader.GetString(4), payload.Winner,
                    reader.IsDBNull(8) ? "" : reader.GetString(8), reader.IsDBNull(10) ? "" : reader.GetString(10),
                    reader.GetInt32(6), payload.FirstMasterId, payload.SecondMasterId);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException
                                          or FormatException)
            {
                // Isolate one damaged row; do not reinterpret it as an eligible legacy result.
            }
        }
        return result.Values.OrderByDescending(item => DateTimeOffset.Parse(item.StartedUtc)).Take(limit).ToArray();
    }
}
