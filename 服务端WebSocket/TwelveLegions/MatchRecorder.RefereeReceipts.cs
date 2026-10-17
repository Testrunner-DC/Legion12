using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    // Recover only the authenticated actor's immutable acknowledgement. No board,
    // command payload, audit reason or current tournament privilege is returned.
    internal async Task<long?> ReadAcceptedRefereeReceiptAsync(string matchId,
        string accountId, string requestId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.revision FROM match_action_requests r
            JOIN matches m ON m.match_id=r.match_id
            WHERE r.match_id=$match AND r.player_index=-1 AND r.request_id=$request
              AND r.accepted=1 AND m.mode_id='tournament'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$request", $"referee:{accountId}:{requestId}");
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }
}
