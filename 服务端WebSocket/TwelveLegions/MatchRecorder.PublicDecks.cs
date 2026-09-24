using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckBinding(string PublicationId, int Version, string PayloadHash);

public sealed partial class MatchRecorder
{
    private const int PublicDeckStatisticsRecentDays = 90;
    private const int PublicDeckStatisticsMinimumGroupGames = 3;
    private static async Task InitializePublicDeckBindingsAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS match_public_deck_bindings (
                match_id TEXT NOT NULL REFERENCES matches(match_id) ON DELETE CASCADE,
                player_index INTEGER NOT NULL CHECK(player_index IN (0,1)),
                publication_id TEXT NOT NULL,
                version INTEGER NOT NULL CHECK(version>0),
                payload_hash TEXT NOT NULL,
                PRIMARY KEY(match_id,player_index)
            );
            CREATE INDEX IF NOT EXISTS ix_match_public_deck_publication
                ON match_public_deck_bindings(publication_id,match_id);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PersistPublicDeckBindingsAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId, IReadOnlyList<L12PublicDeckBinding?>? bindings)
    {
        if (bindings is null) return;
        if (bindings.Count != 2) throw new ArgumentException("公开版本绑定必须包含双席位置", nameof(bindings));
        for (var index = 0; index < bindings.Count; index++)
        {
            if (bindings[index] is not { } binding) continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO match_public_deck_bindings(match_id,player_index,publication_id,version,payload_hash)
                VALUES($match,$player,$publication,$version,$hash);
                """;
            command.Parameters.AddWithValue("$match", matchId);
            command.Parameters.AddWithValue("$player", index);
            command.Parameters.AddWithValue("$publication", binding.PublicationId);
            command.Parameters.AddWithValue("$version", binding.Version);
            command.Parameters.AddWithValue("$hash", binding.PayloadHash);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<L12PublicDeckMatchStatisticsView> PublicDeckVersionStatisticsAsync(string publicationId,
        IReadOnlyCollection<string>? excludedMatchIds = null, IReadOnlyCollection<string>? excludedAccountIds = null)
    {
        var to = _utcNow().ToUniversalTime();
        var from = to.AddDays(-PublicDeckStatisticsRecentDays);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        // Return only aggregate facts. Match ids and participant identities are used solely for filtering and
        // never leave this query boundary.
        command.CommandText = """
            SELECT b.version,player.master_id,opponent.master_id,COUNT(*),
                   SUM(CASE WHEN m.winner=b.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN m.winner IN (0,1) AND m.winner<>b.player_index THEN 1 ELSE 0 END),
                   SUM(CASE WHEN m.winner NOT IN (0,1) OR m.winner IS NULL THEN 1 ELSE 0 END)
            FROM match_public_deck_bindings b JOIN matches m ON m.match_id=b.match_id
            JOIN match_participants player ON player.match_id=m.match_id
                AND player.player_index=b.player_index
            JOIN match_participants opponent ON opponent.match_id=m.match_id
                AND opponent.player_index=1-b.player_index
            WHERE b.publication_id=$publication
              AND m.ended_utc IS NOT NULL AND COALESCE(m.error,'')=''
              AND COALESCE(m.mode_id,'legacy')<>'sandbox'
              AND m.ended_utc>=$from AND m.ended_utc<=$to
              AND player.master_id<>'' AND opponent.master_id<>''
              AND NOT EXISTS(SELECT 1 FROM json_each($matches) x WHERE x.value=m.match_id)
              AND NOT EXISTS(SELECT 1 FROM json_each($accounts) x
                             WHERE x.value=m.account_0 OR x.value=m.account_1)
            GROUP BY b.version,player.master_id,opponent.master_id
            ORDER BY b.version DESC,COUNT(*) DESC,player.master_id,opponent.master_id;
            """;
        command.Parameters.AddWithValue("$publication", publicationId);
        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToString("O"));
        command.Parameters.AddWithValue("$matches", JsonSerializer.Serialize(excludedMatchIds ?? []));
        command.Parameters.AddWithValue("$accounts", JsonSerializer.Serialize(excludedAccountIds ?? []));
        var candidates = new List<L12PublicDeckVersionStatisticView>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var games = checked((int)reader.GetInt64(3));
                var wins = checked((int)reader.GetInt64(4));
                var losses = checked((int)reader.GetInt64(5));
                var draws = checked((int)reader.GetInt64(6));
                candidates.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), games, wins,
                    losses, draws, Math.Round((double)wins / games, 4, MidpointRounding.AwayFromZero)));
            }
        var groups = candidates.Where(group => group.Games >= PublicDeckStatisticsMinimumGroupGames).ToArray();
        var sampleStatus = groups.Length > 0 ? "available" : candidates.Count > 0 ? "insufficient" : "empty";
        return new(from, to, PublicDeckStatisticsRecentDays, groups.Sum(group => group.Games), sampleStatus, groups);
    }
}
