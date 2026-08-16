using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed class MatchRecorder : IAsyncDisposable
{
    private readonly string _connectionString;

    public MatchRecorder(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS matches (
                match_id TEXT PRIMARY KEY, room_code TEXT NOT NULL, seed INTEGER NOT NULL,
                player_0 TEXT NOT NULL, player_1 TEXT NOT NULL, deck_0 TEXT NOT NULL, deck_1 TEXT NOT NULL,
                started_utc TEXT NOT NULL, ended_utc TEXT, winner INTEGER, final_hash TEXT, error TEXT
            );
            CREATE TABLE IF NOT EXISTS match_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT, match_id TEXT NOT NULL, sequence INTEGER NOT NULL,
                received_utc TEXT NOT NULL, player_index INTEGER, command_json TEXT NOT NULL,
                accepted INTEGER NOT NULL, error TEXT, revision INTEGER NOT NULL,
                state_hash TEXT NOT NULL, state_json TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_match_events_sequence ON match_events(match_id, sequence);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task StartAsync(L12GameState state)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO matches(match_id, room_code, seed, player_0, player_1, deck_0, deck_1, started_utc)
            VALUES($id,$room,$seed,$p0,$p1,$d0,$d1,$utc);
            """;
        command.Parameters.AddWithValue("$id", state.MatchId);
        command.Parameters.AddWithValue("$room", state.RoomCode);
        command.Parameters.AddWithValue("$seed", state.Seed);
        command.Parameters.AddWithValue("$p0", state.Players[0].Name);
        command.Parameters.AddWithValue("$p1", state.Players[1].Name);
        command.Parameters.AddWithValue("$d0", state.Players[0].DeckName);
        command.Parameters.AddWithValue("$d1", state.Players[1].DeckName);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task AppendAsync(L12GameEngine engine, long sequence, int playerIndex, string commandJson, CommandResult result)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO match_events(match_id, sequence, received_utc, player_index, command_json, accepted, error, revision, state_hash, state_json)
            VALUES($id,$seq,$utc,$player,$json,$accepted,$error,$revision,$hash,$state);
            """;
        command.Parameters.AddWithValue("$id", engine.State.MatchId);
        command.Parameters.AddWithValue("$seq", sequence);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$player", playerIndex);
        command.Parameters.AddWithValue("$json", commandJson);
        command.Parameters.AddWithValue("$accepted", result.Accepted ? 1 : 0);
        command.Parameters.AddWithValue("$error", (object?)result.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$revision", engine.State.Revision);
        command.Parameters.AddWithValue("$hash", engine.ComputeStateHash());
        command.Parameters.AddWithValue("$state", engine.SerializeFullState());
        await command.ExecuteNonQueryAsync();
    }

    public async Task CompleteAsync(L12GameEngine engine)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE matches SET ended_utc=$utc,winner=$winner,final_hash=$hash WHERE match_id=$id";
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$winner", (object?)engine.State.Winner ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", engine.ComputeStateHash());
        command.Parameters.AddWithValue("$id", engine.State.MatchId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<L12MatchSummary>> ListMatchesAsync(int limit = 50)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.room_code,m.player_0,m.player_1,m.deck_0,m.deck_1,
                   m.started_utc,m.ended_utc,m.winner,m.final_hash,m.error,COUNT(e.id)
            FROM matches m LEFT JOIN match_events e ON e.match_id=m.match_id
            GROUP BY m.match_id ORDER BY m.started_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        var matches = new List<L12MatchSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) matches.Add(ReadSummary(reader));
        return matches;
    }

    public async Task<L12MatchDetail?> GetMatchAsync(string matchId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = """
            SELECT m.match_id,m.room_code,m.player_0,m.player_1,m.deck_0,m.deck_1,
                   m.started_utc,m.ended_utc,m.winner,m.final_hash,m.error,COUNT(e.id)
            FROM matches m LEFT JOIN match_events e ON e.match_id=m.match_id
            WHERE m.match_id=$id GROUP BY m.match_id;
            """;
        summaryCommand.Parameters.AddWithValue("$id", matchId);
        L12MatchSummary summary;
        await using (var reader = await summaryCommand.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return null;
            summary = ReadSummary(reader);
        }

        var eventCommand = connection.CreateCommand();
        eventCommand.CommandText = """
            SELECT sequence,received_utc,player_index,command_json,accepted,error,revision,state_hash,state_json
            FROM match_events WHERE match_id=$id ORDER BY sequence;
            """;
        eventCommand.Parameters.AddWithValue("$id", matchId);
        var events = new List<L12RecordedCommand>();
        await using (var reader = await eventCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                events.Add(new L12RecordedCommand(
                    reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                    JsonDocument.Parse(reader.GetString(3)).RootElement.Clone(), reader.GetInt32(4) == 1,
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), reader.GetString(7),
                    JsonDocument.Parse(reader.GetString(8)).RootElement.Clone()));
            }
        }
        return new L12MatchDetail(summary, events);
    }

    private static L12MatchSummary ReadSummary(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
        reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.GetInt32(11));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record L12MatchSummary(
    string MatchId, string RoomCode, string Player0, string Player1, string Deck0, string Deck1,
    string StartedUtc, string? EndedUtc, int? Winner, string? FinalHash, string? Error, int CommandCount);

public sealed record L12RecordedCommand(
    long Sequence, string ReceivedUtc, int PlayerIndex, JsonElement Command, bool Accepted,
    string? Error, long Revision, string StateHash, JsonElement State);

public sealed record L12MatchDetail(L12MatchSummary Match, IReadOnlyList<L12RecordedCommand> Commands);
