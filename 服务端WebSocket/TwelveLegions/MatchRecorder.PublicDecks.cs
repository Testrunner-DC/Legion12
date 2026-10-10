using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckBinding(string PublicationId, int Version, string PayloadHash);
public sealed record L12PublicDeckMatchStatisticsPageSnapshot(DateTimeOffset From, DateTimeOffset To, int RecentDays,
    int Games, string SampleStatus, IReadOnlyList<L12PublicDeckVersionStatisticView> Groups,
    int Total, int Page, int PageSize);

public sealed partial class MatchRecorder
{
    private const int PublicDeckStatisticsRecentDays = 90;
    private const int PublicDeckStatisticsMinimumGroupGames = 3;
    private const string PublicDeckStatisticsGroupedSql = """
        SELECT b.version AS version,player.master_id AS master_id,opponent.master_id AS opponent_master_id,
               COUNT(*) AS games,
               SUM(CASE WHEN m.winner=b.player_index THEN 1 ELSE 0 END) AS wins,
               SUM(CASE WHEN m.winner IN (0,1) AND m.winner<>b.player_index THEN 1 ELSE 0 END) AS losses,
               SUM(CASE WHEN m.winner NOT IN (0,1) OR m.winner IS NULL THEN 1 ELSE 0 END) AS draws
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
        """;

    internal Func<CancellationToken, Task>? PublicDeckStatisticsReadPauseHook { get; set; }
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
        command.CommandText = PublicDeckStatisticsGroupedSql
            + "\nORDER BY b.version DESC,games DESC,player.master_id,opponent.master_id;";
        BindPublicDeckStatisticsParameters(command, publicationId, from, to, excludedMatchIds, excludedAccountIds);
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

    public async Task<L12PublicDeckMatchStatisticsPageSnapshot> PublicDeckVersionStatisticsPageAsync(
        string publicationId, int page, int pageSize, IReadOnlyCollection<string>? excludedMatchIds = null,
        IReadOnlyCollection<string>? excludedAccountIds = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicationId) || publicationId.Length > 64)
            throw new ArgumentException("公开牌库统计引用无效", nameof(publicationId));
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        cancellationToken.ThrowIfCancellationRequested();
        var to = _utcNow().ToUniversalTime();
        var from = to.AddDays(-PublicDeckStatisticsRecentDays);
        var readOnly = new SqliteConnectionStringBuilder(_connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 2,
        }.ToString();
        await using var connection = new SqliteConnection(readOnly);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandTimeout = 2;
            schema.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt64(await schema.ExecuteScalarAsync(cancellationToken)) != 0)
                throw new InvalidDataException("公开牌库统计存储格式无效");
        }
        using (var marker = connection.CreateCommand())
        {
            marker.Transaction = transaction;
            marker.CommandTimeout = 2;
            marker.CommandText = "SELECT version FROM match_recorder_schema WHERE component='match-analytics';";
            await using var reader = await marker.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != 2
                || await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("公开牌库统计存储标记无效");
        }
        using (var validity = connection.CreateCommand())
        {
            validity.Transaction = transaction;
            validity.CommandTimeout = 2;
            validity.CommandText = """
                SELECT COUNT(*)
                FROM match_public_deck_bindings b
                LEFT JOIN matches m ON m.match_id=b.match_id
                WHERE b.publication_id=$publication AND (
                    typeof(b.player_index)<>'integer' OR b.player_index NOT IN (0,1)
                    OR typeof(b.version)<>'integer' OR b.version<=0
                    OR typeof(b.payload_hash)<>'text' OR b.payload_hash=''
                    OR m.match_id IS NULL
                    OR (m.ended_utc IS NOT NULL AND COALESCE(m.error,'')=''
                        AND COALESCE(m.mode_id,'legacy')<>'sandbox'
                        AND m.ended_utc>=$from AND m.ended_utc<=$to
                        AND (typeof(m.ended_utc)<>'text' OR julianday(m.ended_utc) IS NULL))
                );
                """;
            validity.Parameters.AddWithValue("$publication", publicationId);
            validity.Parameters.AddWithValue("$from", from.ToString("O"));
            validity.Parameters.AddWithValue("$to", to.ToString("O"));
            if (Convert.ToInt64(await validity.ExecuteScalarAsync(cancellationToken)) != 0)
                throw new InvalidDataException("公开牌库统计已提交头无效");
        }
        long candidateCount;
        long total;
        long games;
        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandTimeout = 2;
            metadata.CommandText = "WITH candidates AS (\n" + PublicDeckStatisticsGroupedSql + """
                )
                SELECT COUNT(*),
                       COALESCE(SUM(CASE WHEN games >= $minimum THEN 1 ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN games >= $minimum THEN games ELSE 0 END),0)
                FROM candidates;
                """;
            BindPublicDeckStatisticsParameters(metadata, publicationId, from, to, excludedMatchIds, excludedAccountIds);
            metadata.Parameters.AddWithValue("$minimum", PublicDeckStatisticsMinimumGroupGames);
            await using var reader = await metadata.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("公开牌库统计摘要缺失");
            candidateCount = reader.GetInt64(0);
            total = reader.GetInt64(1);
            games = reader.GetInt64(2);
            if (await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("公开牌库统计摘要重复");
        }
        if (PublicDeckStatisticsReadPauseHook is { } pause)
            await pause(cancellationToken);
        var groups = new List<L12PublicDeckVersionStatisticView>(pageSize);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = 2;
            command.CommandText = "WITH candidates AS (\n" + PublicDeckStatisticsGroupedSql + """
                ), eligible AS (
                    SELECT * FROM candidates WHERE games >= $minimum
                )
                SELECT version,master_id,opponent_master_id,games,wins,losses,draws
                FROM eligible
                ORDER BY version DESC,games DESC,master_id,opponent_master_id
                LIMIT $limit OFFSET $offset;
                """;
            BindPublicDeckStatisticsParameters(command, publicationId, from, to, excludedMatchIds, excludedAccountIds);
            command.Parameters.AddWithValue("$minimum", PublicDeckStatisticsMinimumGroupGames);
            command.Parameters.AddWithValue("$limit", pageSize);
            command.Parameters.AddWithValue("$offset", ((long)page - 1) * pageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var groupGames = checked((int)reader.GetInt64(3));
                var wins = checked((int)reader.GetInt64(4));
                var losses = checked((int)reader.GetInt64(5));
                var draws = checked((int)reader.GetInt64(6));
                groups.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), groupGames, wins,
                    losses, draws, Math.Round((double)wins / groupGames, 4, MidpointRounding.AwayFromZero)));
            }
        }
        transaction.Commit();
        var sampleStatus = total > 0 ? "available" : candidateCount > 0 ? "insufficient" : "empty";
        return new(from, to, PublicDeckStatisticsRecentDays, checked((int)games), sampleStatus, groups,
            checked((int)total), page, pageSize);
    }

    private static void BindPublicDeckStatisticsParameters(SqliteCommand command, string publicationId,
        DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<string>? excludedMatchIds,
        IReadOnlyCollection<string>? excludedAccountIds)
    {
        command.Parameters.AddWithValue("$publication", publicationId);
        command.Parameters.AddWithValue("$from", from.ToString("O"));
        command.Parameters.AddWithValue("$to", to.ToString("O"));
        command.Parameters.AddWithValue("$matches", JsonSerializer.Serialize(excludedMatchIds ?? []));
        command.Parameters.AddWithValue("$accounts", JsonSerializer.Serialize(excludedAccountIds ?? []));
    }
}
