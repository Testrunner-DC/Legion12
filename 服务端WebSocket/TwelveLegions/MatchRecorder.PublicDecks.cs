using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckBinding(string PublicationId, int Version, string PayloadHash);

public sealed partial class MatchRecorder
{
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

    public async Task<IReadOnlyList<L12PublicDeckMatchView>> PublicDeckMatchesAsync(string publicationId,
        IReadOnlyCollection<string>? excludedMatchIds = null, IReadOnlyCollection<string>? excludedAccountIds = null,
        string? viewerAccountId = null, string? viewerName = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        // Consume the same completed facts and exclusion sets as PlayerStatisticsAsync.
        // No author/name/content matching and no replay payload are involved in association.
        command.CommandText = """
            SELECT m.match_id,b.version,m.ended_utc,COALESCE(opponent.master_id,''),
                   CASE WHEN m.winner=b.player_index THEN '胜'
                        WHEN m.winner IN (0,1) THEN '负' ELSE '平' END
            FROM match_public_deck_bindings b JOIN matches m ON m.match_id=b.match_id
            LEFT JOIN match_participants opponent ON opponent.match_id=m.match_id
                AND opponent.player_index=1-b.player_index
            WHERE b.publication_id=$publication
              AND m.ended_utc IS NOT NULL AND COALESCE(m.error,'')=''
              AND COALESCE(m.mode_id,'legacy')<>'sandbox'
              AND NOT EXISTS(SELECT 1 FROM json_each($matches) x WHERE x.value=m.match_id)
              AND NOT EXISTS(SELECT 1 FROM json_each($accounts) x
                             WHERE x.value=m.account_0 OR x.value=m.account_1)
            ORDER BY m.ended_utc DESC,m.match_id DESC,b.player_index;
            """;
        command.Parameters.AddWithValue("$publication", publicationId);
        command.Parameters.AddWithValue("$matches", JsonSerializer.Serialize(excludedMatchIds ?? []));
        command.Parameters.AddWithValue("$accounts", JsonSerializer.Serialize(excludedAccountIds ?? []));
        var result = new List<L12PublicDeckMatchView>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) result.Add(new(reader.GetString(0), reader.GetInt32(1),
                DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3), reader.GetString(4), null));
        // Replay access remains participant-only and obeys the existing 7-day/10-match window.
        if (!string.IsNullOrWhiteSpace(viewerAccountId))
        {
            var recent = (await ListRecentPlayerReplayMatchesAsync(viewerAccountId, viewerName ?? ""))
                .Select(match => match.MatchId).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < result.Count; i++)
                if (recent.Contains(result[i].MatchId))
                    result[i] = result[i] with { ReplayPath = "/battle/records/replay/" + Uri.EscapeDataString(result[i].MatchId) };
        }
        return result;
    }
}
