using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal sealed record L12TournamentResultOutboxEntry(
    string MatchId, string TournamentId, string TournamentMatchId, int Winner, int Attempts);

public sealed partial class MatchRecorder
{
    private static async Task InitializeTournamentResultOutboxAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tournament_result_outbox (
                match_id TEXT PRIMARY KEY,
                tournament_id TEXT NOT NULL,
                tournament_match_id TEXT NOT NULL,
                winner INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                created_utc TEXT NOT NULL,
                applied_utc TEXT,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_tournament_result_outbox_pending
                ON tournament_result_outbox(status,created_utc);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertTournamentResultOutboxAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId, string tournamentId, string tournamentMatchId,
        int winner, string createdUtc)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tournament_result_outbox(
                match_id,tournament_id,tournament_match_id,winner,status,created_utc)
            VALUES($match,$tournament,$table,$winner,'pending',$created)
            ON CONFLICT(match_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$tournament", tournamentId);
        command.Parameters.AddWithValue("$table", tournamentMatchId);
        command.Parameters.AddWithValue("$winner", winner);
        command.Parameters.AddWithValue("$created", createdUtc);
        await command.ExecuteNonQueryAsync();

        var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = """
            SELECT tournament_id,tournament_match_id,winner
            FROM tournament_result_outbox WHERE match_id=$match;
            """;
        verify.Parameters.AddWithValue("$match", matchId);
        await using var reader = await verify.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetString(0) != tournamentId
            || reader.GetString(1) != tournamentMatchId || reader.GetInt32(2) != winner)
            throw new InvalidOperationException("赛事赛果 outbox 与既有记录冲突");
    }

    internal async Task<IReadOnlyList<L12TournamentResultOutboxEntry>> ListPendingTournamentResultsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_id,tournament_id,tournament_match_id,winner,attempts
            FROM tournament_result_outbox WHERE status='pending'
            ORDER BY created_utc,match_id;
            """;
        var result = new List<L12TournamentResultOutboxEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new L12TournamentResultOutboxEntry(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4)));
        return result;
    }

    internal async Task MarkTournamentResultAppliedAsync(string matchId)
    {
        await using var connection = await OpenWriteConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tournament_result_outbox SET status='applied',applied_utc=$utc,last_error=NULL
            WHERE match_id=$match AND status='pending';
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$utc", _utcNow().ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    internal async Task RecordTournamentResultFailureAsync(string matchId, string error)
    {
        await using var connection = await OpenWriteConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tournament_result_outbox SET attempts=attempts+1,last_error=$error
            WHERE match_id=$match AND status='pending';
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$error", error.Length > 1000 ? error[..1000] : error);
        await command.ExecuteNonQueryAsync();
    }
}
