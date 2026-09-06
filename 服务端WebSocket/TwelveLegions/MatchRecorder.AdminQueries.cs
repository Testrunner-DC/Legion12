using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public const long MaximumInlineReplayCommands = 1_500;
    public const long MaximumInlineReplayBytes = 32L * 1024 * 1024;
    public const int MaximumReplayPageCommands = 100;
    public const long MaximumReplayPageBytes = 4L * 1024 * 1024;
    private const string AdminMatchSelect = """
        SELECT m.match_id,m.mode_id,m.started_utc,m.ended_utc,m.winner,m.error,
               (SELECT COUNT(*) FROM match_events e WHERE e.match_id=m.match_id),
               p0.account_id,p0.display_name,p0.master_id,p0.deck_name,
               p1.account_id,p1.display_name,p1.master_id,p1.deck_name
        FROM matches m
        JOIN match_participants p0 ON p0.match_id=m.match_id AND p0.player_index=0
        JOIN match_participants p1 ON p1.match_id=m.match_id AND p1.player_index=1
        """;

    private static readonly string[] AnalyticsLimitations =
    [
        "zone-move and legion damage use command-boundary state deltas; intermediate transitions are partial",
        "damage facts without an authoritative source are target-only and are marked partial",
        "search-or-hand-add is exact on the centralized effect helper; direct or legacy hand moves are command-boundary partial",
        "fizzle is partial when a structured failure cannot be correlated with a live stack item",
        "kill is exact for typed kill-source resolution; other destruction paths may only appear as partial zone moves",
        "legacy matches without immutable deck snapshots are excluded from card analytics",
        "win-rate differences are observational and do not establish causal card impact",
        "baselineWinRate uses non-including player samples in the same filter or breakdown stratum; it is not a multivariate adjustment",
        "rank/tier and opponent hidden rating are not stored in matches.db and are unavailable for strength adjustment",
    ];

    public async Task<L12AdminMatchPage> ListAdminMatchesAsync(L12AdminMatchQuery query)
    {
        var normalized = query with { Limit = Math.Clamp(query.Limit, 1, 200) };
        var totalWhere = BuildAdminMatchWhere(normalized with { Cursor = null }, includeCursor: false,
            out var totalParameters);
        var pageWhere = BuildAdminMatchWhere(normalized, includeCursor: true, out var pageParameters);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM matches m WHERE {totalWhere};";
        AddParameters(count, totalParameters);
        var total = Convert.ToInt64(await count.ExecuteScalarAsync());

        var command = connection.CreateCommand();
        command.CommandText = $"""
            {AdminMatchSelect}
            WHERE {pageWhere}
            ORDER BY m.started_utc DESC,m.match_id DESC
            LIMIT $take;
            """;
        AddParameters(command, pageParameters);
        command.Parameters.AddWithValue("$take", normalized.Limit + 1);
        var items = new List<L12AdminMatchSummary>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) items.Add(ReadAdminMatchSummary(reader));

        var hasMore = items.Count > normalized.Limit;
        if (hasMore) items.RemoveAt(items.Count - 1);
        var nextCursor = hasMore && items.Count > 0
            ? EncodeCursor(items[^1].StartedUtc, items[^1].MatchId)
            : null;
        return new L12AdminMatchPage(items, total, nextCursor);
    }

    public Task<L12AdminMatchPage> ListAdminMatchesForAccountAsync(string accountId,
        L12AdminMatchQuery query)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("账号 ID 不能为空", nameof(accountId));
        return ListAdminMatchesAsync(query with { AccountId = accountId.Trim(), Player = null });
    }

    public async Task<L12AdminMatchDetail?> GetAdminMatchAsync(string matchId, bool includeReplay = false)
    {
        if (string.IsNullOrWhiteSpace(matchId)) return null;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = $"""
            {AdminMatchSelect}
            WHERE m.match_id=$match AND m.mode_id <> 'sandbox';
            """;
        summaryCommand.Parameters.AddWithValue("$match", matchId);
        L12AdminMatchSummary summary;
        await using (var reader = await summaryCommand.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return null;
            summary = ReadAdminMatchSummary(reader);
        }

        var completed = summary.EndedUtc is not null;
        var participants = await ReadParticipantDetailsAsync(connection, matchId, summary, completed);
        if (!completed)
        {
            return new L12AdminMatchDetail(summary, participants, [], [],
                EmptyCoverage(privateDuringActiveMatch: true));
        }

        if (includeReplay) await EnsureInlineReplayWithinLimitsAsync(connection, matchId);
        var replay = includeReplay ? (await GetMatchAsync(matchId))?.Commands ?? [] : [];
        var facts = await ReadCardFactsAsync(connection, matchId);
        var coverage = await ReadCoverageAsync(connection, matchId, privateDuringActiveMatch: false);
        return new L12AdminMatchDetail(summary, participants, replay, facts, coverage);
    }

    private static async Task EnsureInlineReplayWithinLimitsAsync(SqliteConnection connection, string matchId)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),COALESCE(SUM(
                length(CAST(command_json AS BLOB))+length(CAST(state_json AS BLOB))
                +length(CAST(state_hash AS BLOB))+length(CAST(received_utc AS BLOB))
                +COALESCE(length(CAST(error AS BLOB)),0)
            ),0)
            FROM match_events WHERE match_id=$match;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return;
        var count = reader.GetInt64(0);
        var bytes = reader.GetInt64(1);
        if (count > MaximumInlineReplayCommands || bytes > MaximumInlineReplayBytes)
            throw new L12ReplayPayloadTooLargeException(matchId, count, bytes,
                MaximumInlineReplayCommands, MaximumInlineReplayBytes);
    }

    public async Task<L12AdminReplayPage?> GetAdminReplayPageAsync(string matchId, string? cursor,
        int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(matchId)) return null;
        var normalizedMatchId = matchId.Trim();
        var take = Math.Clamp(limit, 1, MaximumReplayPageCommands);
        var afterSequence = DecodeReplayCursor(normalizedMatchId, cursor);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var metadata = connection.CreateCommand();
        metadata.CommandText = """
            SELECT COUNT(e.id),COALESCE(SUM(
                length(CAST(e.command_json AS BLOB))+length(CAST(e.state_json AS BLOB))
                +length(CAST(e.state_hash AS BLOB))+length(CAST(e.received_utc AS BLOB))
                +COALESCE(length(CAST(e.error AS BLOB)),0)
            ),0)
            FROM matches m LEFT JOIN match_events e ON e.match_id=m.match_id
            WHERE m.match_id=$match AND m.mode_id<>'sandbox' AND m.ended_utc IS NOT NULL
            GROUP BY m.match_id;
            """;
        metadata.Parameters.AddWithValue("$match", normalizedMatchId);
        long totalCommands;
        long totalBytes;
        await using (var metadataReader = await metadata.ExecuteReaderAsync(cancellationToken))
        {
            if (!await metadataReader.ReadAsync(cancellationToken)) return null;
            totalCommands = metadataReader.GetInt64(0);
            totalBytes = metadataReader.GetInt64(1);
        }

        // Size the candidate rows without selecting either JSON payload. This keeps an
        // oversized state out of the managed process entirely and lets us reject it
        // before GetString/JsonDocument.Parse. Only the accepted <=4 MiB sequence range
        // is selected by the second query below.
        var sizing = connection.CreateCommand();
        sizing.CommandText = """
            SELECT length(CAST(command_json AS BLOB))+length(CAST(state_json AS BLOB))
                     +length(CAST(state_hash AS BLOB))+length(CAST(received_utc AS BLOB))
                     +COALESCE(length(CAST(error AS BLOB)),0),sequence
            FROM match_events
            WHERE match_id=$match AND sequence>$after
            ORDER BY sequence
            LIMIT $take;
            """;
        sizing.Parameters.AddWithValue("$match", normalizedMatchId);
        sizing.Parameters.AddWithValue("$after", afterSequence);
        sizing.Parameters.AddWithValue("$take", take + 1);
        var sequences = new List<long>(take);
        long pageBytes = 0;
        var hasMore = false;
        await using (var sizingReader = await sizing.ExecuteReaderAsync(cancellationToken))
        {
            while (await sizingReader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowBytes = sizingReader.GetInt64(0);
                if (sequences.Count >= take || pageBytes + rowBytes > MaximumReplayPageBytes)
                {
                    hasMore = true;
                    if (sequences.Count == 0)
                        throw new L12ReplayPayloadTooLargeException(matchId, totalCommands, rowBytes,
                            MaximumReplayPageCommands, MaximumReplayPageBytes);
                    break;
                }
                pageBytes += rowBytes;
                sequences.Add(sizingReader.GetInt64(1));
            }
        }

        var items = new List<L12RecordedCommand>(sequences.Count);
        if (sequences.Count > 0)
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT sequence,received_utc,player_index,command_json,accepted,error,revision,state_hash,state_json
                FROM match_events
                WHERE match_id=$match AND sequence>$after AND sequence<=$last
                ORDER BY sequence;
                """;
            command.Parameters.AddWithValue("$match", normalizedMatchId);
            command.Parameters.AddWithValue("$after", afterSequence);
            command.Parameters.AddWithValue("$last", sequences[^1]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var commandDocument = JsonDocument.Parse(reader.GetString(3));
                using var stateDocument = JsonDocument.Parse(reader.GetString(8));
                items.Add(new L12RecordedCommand(
                    reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? -1 : reader.GetInt32(2),
                    commandDocument.RootElement.Clone(), reader.GetInt32(4) == 1,
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), reader.GetString(7),
                    stateDocument.RootElement.Clone()));
            }
        }
        var nextCursor = hasMore && sequences.Count > 0
            ? EncodeReplayCursor(normalizedMatchId, sequences[^1])
            : null;
        return new L12AdminReplayPage(items, nextCursor, take, pageBytes, totalCommands, totalBytes);
    }

    private static string EncodeReplayCursor(string matchId, long sequence)
        => Base64UrlEncode($"{matchId}\n{sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    private static long DecodeReplayCursor(string matchId, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return -1;
        try
        {
            var value = Base64UrlDecode(cursor.Trim());
            var separator = value.LastIndexOf('\n');
            if (separator > 0 && string.Equals(value[..separator], matchId, StringComparison.Ordinal)
                && long.TryParse(value[(separator + 1)..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var sequence) && sequence >= 0)
                return sequence;
        }
        catch (Exception error) when (error is FormatException or ArgumentException) { }
        throw new ArgumentException("回放分页游标无效", nameof(cursor));
    }

    public async Task<IReadOnlyList<L12MatchSummary>> ListMatchesForAccountAsync(
        string accountId, string legacyPlayerName, int limit = 50)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.room_code,m.player_0,m.player_1,m.deck_0,m.deck_1,
                   m.started_utc,m.ended_utc,m.winner,m.final_hash,m.error,COUNT(e.id)
            FROM matches m LEFT JOIN match_events e ON e.match_id=m.match_id
            WHERE m.mode_id <> 'sandbox' AND m.ended_utc IS NOT NULL
              AND (
                    m.account_0=$account OR m.account_1=$account
                    OR ((m.account_0 IS NULL AND m.player_0=$player)
                        OR (m.account_1 IS NULL AND m.player_1=$player))
                  )
            GROUP BY m.match_id ORDER BY m.started_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$player", legacyPlayerName);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        var matches = new List<L12MatchSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) matches.Add(ReadSummary(reader));
        return matches;
    }

    public async Task<L12MatchDetail?> GetMatchForAccountAsync(
        string matchId, string accountId, string legacyPlayerName)
    {
        var viewer = -1;
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            var visible = connection.CreateCommand();
            visible.CommandText = """
                SELECT CASE
                    WHEN account_0=$account OR (account_0 IS NULL AND player_0=$player) THEN 0
                    WHEN account_1=$account OR (account_1 IS NULL AND player_1=$player) THEN 1
                    ELSE -1 END
                FROM matches
                WHERE match_id=$match AND ended_utc IS NOT NULL AND mode_id <> 'sandbox';
                """;
            visible.Parameters.AddWithValue("$match", matchId);
            visible.Parameters.AddWithValue("$account", accountId);
            visible.Parameters.AddWithValue("$player", legacyPlayerName);
            var scalar = await visible.ExecuteScalarAsync();
            if (scalar is null) return null;
            viewer = Convert.ToInt32(scalar);
        }
        if (viewer is < 0 or > 1) return null;
        var detail = await GetMatchAsync(matchId);
        if (detail is null) return null;
        var commands = detail.Commands.Select(command => command with
        {
            Command = SanitizeRecordedCommand(command.Command, command.PlayerIndex == viewer),
            State = SanitizeRecordedState(command.State, viewer),
        }).ToArray();
        return new L12MatchDetail(detail.Match, commands, viewer);
    }

    private static string BuildAdminMatchWhere(L12AdminMatchQuery query, bool includeCursor,
        out Dictionary<string, object> parameters)
    {
        parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        var clauses = new List<string> { "m.mode_id <> 'sandbox'" };
        if (!string.IsNullOrWhiteSpace(query.ModeId))
        {
            clauses.Add("m.mode_id=$mode");
            parameters["$mode"] = query.ModeId.Trim().ToLowerInvariant();
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            switch (query.Status.Trim().ToLowerInvariant())
            {
                case "ongoing": clauses.Add("m.ended_utc IS NULL"); break;
                case "completed": clauses.Add("m.ended_utc IS NOT NULL AND m.error IS NULL AND m.winner IN (0,1)"); break;
                case "invalid": clauses.Add("m.ended_utc IS NOT NULL AND (m.error IS NOT NULL OR m.winner IS NULL)"); break;
                default: throw new ArgumentException("对局状态筛选无效", nameof(query));
            }
        }
        if (!string.IsNullOrWhiteSpace(query.AccountId))
        {
            clauses.Add("EXISTS(SELECT 1 FROM match_participants pa WHERE pa.match_id=m.match_id AND pa.account_id=$account)");
            parameters["$account"] = query.AccountId.Trim();
        }
        if (!string.IsNullOrWhiteSpace(query.Player))
        {
            clauses.Add("EXISTS(SELECT 1 FROM match_participants pp WHERE pp.match_id=m.match_id AND (pp.account_id LIKE $player ESCAPE '\\' OR pp.display_name LIKE $player ESCAPE '\\'))");
            parameters["$player"] = $"%{EscapeLike(query.Player.Trim())}%";
        }
        if (!string.IsNullOrWhiteSpace(query.MasterId))
        {
            clauses.Add("EXISTS(SELECT 1 FROM match_participants pm WHERE pm.match_id=m.match_id AND pm.master_id=$master)");
            parameters["$master"] = query.MasterId.Trim();
        }
        if (query.Winner is { } winner)
        {
            if (winner is < 0 or > 1) throw new ArgumentException("胜者序号筛选无效", nameof(query));
            clauses.Add("m.winner=$winner");
            parameters["$winner"] = winner;
        }
        if (query.FromUtc is { } from)
        {
            clauses.Add("m.started_utc >= $from");
            parameters["$from"] = from.ToUniversalTime().ToString("O");
        }
        if (query.ToUtc is { } to)
        {
            clauses.Add("m.started_utc < $to");
            parameters["$to"] = to.ToUniversalTime().ToString("O");
        }
        if (!string.IsNullOrWhiteSpace(query.CardId))
        {
            var ownerClauses = new List<string> { "dc.match_id=m.match_id", "dc.card_id=$card" };
            if (!string.IsNullOrWhiteSpace(query.CardOwnerMasterId))
            {
                ownerClauses.Add("owner.master_id=$cardOwnerMaster");
                parameters["$cardOwnerMaster"] = query.CardOwnerMasterId.Trim();
            }
            if (!string.IsNullOrWhiteSpace(query.CardOwnerOpponentMasterId))
            {
                ownerClauses.Add("opponent.master_id=$cardOwnerOpponentMaster");
                parameters["$cardOwnerOpponentMaster"] = query.CardOwnerOpponentMasterId.Trim();
            }
            clauses.Add($"EXISTS(SELECT 1 FROM match_deck_cards dc JOIN match_participants owner ON owner.match_id=dc.match_id AND owner.player_index=dc.player_index JOIN match_participants opponent ON opponent.match_id=owner.match_id AND opponent.player_index<>owner.player_index WHERE {string.Join(" AND ", ownerClauses)})");
            parameters["$card"] = query.CardId.Trim();
        }
        if (includeCursor && !string.IsNullOrWhiteSpace(query.Cursor))
        {
            var cursor = DecodeCursor(query.Cursor);
            clauses.Add("(m.started_utc < $cursorUtc OR (m.started_utc=$cursorUtc AND m.match_id < $cursorId))");
            parameters["$cursorUtc"] = cursor.StartedUtc;
            parameters["$cursorId"] = cursor.MatchId;
        }
        return string.Join(" AND ", clauses);
    }

    private static void AddParameters(SqliteCommand command, IReadOnlyDictionary<string, object> parameters)
    {
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
    }

    private static L12AdminMatchSummary ReadAdminMatchSummary(SqliteDataReader reader)
    {
        var startedUtc = reader.GetString(2);
        var endedUtc = reader.IsDBNull(3) ? null : reader.GetString(3);
        var winner = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
        var error = reader.IsDBNull(5) ? null : reader.GetString(5);
        var status = endedUtc is null ? "ongoing"
            : error is not null || winner is null ? "invalid" : "completed";
        var hideDeck = endedUtc is null;
        var players = new[]
        {
            new L12AdminMatchPlayer(0, reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), hideDeck || reader.IsDBNull(10) ? null : reader.GetString(10),
                MatchResult(status, winner, 0)),
            new L12AdminMatchPlayer(1, reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13), hideDeck || reader.IsDBNull(14) ? null : reader.GetString(14),
                MatchResult(status, winner, 1)),
        };
        return new L12AdminMatchSummary(reader.GetString(0), reader.GetString(1), status, players,
            startedUtc, endedUtc, DurationSeconds(startedUtc, endedUtc), reader.GetInt32(6), error);
    }

    private static string MatchResult(string status, int? winner, int playerIndex)
        => status switch
        {
            "ongoing" => "ongoing",
            "invalid" => "invalid",
            _ when winner == playerIndex => "win",
            _ when winner is 0 or 1 => "loss",
            _ => "draw",
        };

    private static int? DurationSeconds(string startedUtc, string? endedUtc)
        => endedUtc is not null
           && DateTimeOffset.TryParse(startedUtc, out var started)
           && DateTimeOffset.TryParse(endedUtc, out var ended)
            ? Math.Max(0, (int)Math.Round((ended - started).TotalSeconds))
            : null;

    private static async Task<IReadOnlyList<L12MatchParticipantDetail>> ReadParticipantDetailsAsync(
        SqliteConnection connection, string matchId, L12AdminMatchSummary summary, bool includePrivate)
    {
        var cards = new Dictionary<int, List<L12DeckCardSnapshot>>();
        if (includePrivate)
        {
            var deckCommand = connection.CreateCommand();
            deckCommand.CommandText = """
                SELECT player_index,card_id,quantity,section FROM match_deck_cards
                WHERE match_id=$match ORDER BY player_index,section,card_id;
                """;
            deckCommand.Parameters.AddWithValue("$match", matchId);
            await using var deckReader = await deckCommand.ExecuteReaderAsync();
            while (await deckReader.ReadAsync())
            {
                var playerIndex = deckReader.GetInt32(0);
                if (!cards.TryGetValue(playerIndex, out var list)) cards[playerIndex] = list = [];
                list.Add(new L12DeckCardSnapshot(deckReader.GetString(1), deckReader.GetInt32(2),
                    deckReader.GetString(3)));
            }
        }

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT player_index,account_id,display_name,master_id,master_name,deck_name,deck_snapshot_coverage
            FROM match_participants WHERE match_id=$match ORDER BY player_index;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        var participants = new List<L12MatchParticipantDetail>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var playerIndex = reader.GetInt32(0);
            var player = summary.Players.Single(item => item.PlayerIndex == playerIndex);
            participants.Add(new L12MatchParticipantDetail(playerIndex,
                reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                includePrivate && !reader.IsDBNull(5) ? reader.GetString(5) : null,
                player.Result, includePrivate && cards.TryGetValue(playerIndex, out var deck) ? deck : [],
                reader.GetString(6)));
        }
        return participants;
    }

    private static async Task<IReadOnlyList<L12CardFactView>> ReadCardFactsAsync(
        SqliteConnection connection, string matchId)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind,command_sequence,revision,round,turn,phase,occurred_utc,player_index,account_id,
                   card_id,card_instance_id,related_card_id,related_instance_id,source_zone,destination_zone,
                   amount,coverage,metadata_json
            FROM match_card_facts WHERE match_id=$match ORDER BY command_sequence,id;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        var facts = new List<L12CardFactView>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            facts.Add(new L12CardFactView(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetInt32(15), reader.GetString(16),
                ParseJsonElement(reader.GetString(17))));
        }
        return facts;
    }

    private static async Task<L12AnalyticsCoverage> ReadCoverageAsync(SqliteConnection connection,
        string? matchId = null, bool privateDuringActiveMatch = false)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                SUM(CASE WHEN coverage='exact' THEN 1 ELSE 0 END),
                SUM(CASE WHEN coverage='inferred' THEN 1 ELSE 0 END),
                SUM(CASE WHEN coverage='partial' THEN 1 ELSE 0 END)
            FROM match_card_facts
            {(matchId is null ? string.Empty : "WHERE match_id=$match")};
            """;
        if (matchId is not null) command.Parameters.AddWithValue("$match", matchId);
        long exact = 0, inferred = 0, partial = 0;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                exact = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                inferred = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                partial = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            }
        }
        var decks = connection.CreateCommand();
        decks.CommandText = $"""
            SELECT
                SUM(CASE WHEN deck_snapshot_coverage='exact' THEN 1 ELSE 0 END),
                SUM(CASE WHEN deck_snapshot_coverage<>'exact' THEN 1 ELSE 0 END)
            FROM match_participants
            {(matchId is null ? string.Empty : "WHERE match_id=$match")};
            """;
        if (matchId is not null) decks.Parameters.AddWithValue("$match", matchId);
        long exactDecks = 0, inferredDecks = 0;
        await using (var reader = await decks.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                exactDecks = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                inferredDecks = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            }
        }
        return new L12AnalyticsCoverage(L12CardFactKinds.SchemaVersion, L12CardFactKinds.Supported,
            exact, inferred, partial, exactDecks, inferredDecks, privateDuringActiveMatch,
            AnalyticsLimitations);
    }

    private static L12AnalyticsCoverage EmptyCoverage(bool privateDuringActiveMatch)
        => new(L12CardFactKinds.SchemaVersion, L12CardFactKinds.Supported, 0, 0, 0, 0, 0,
            privateDuringActiveMatch, AnalyticsLimitations);

    private static JsonElement ParseJsonElement(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException) { return JsonSerializer.SerializeToElement(new { invalid = true }); }
    }

    private static string EncodeCursor(string startedUtc, string matchId)
        => Base64UrlEncode($"{startedUtc}\n{matchId}");

    private static (string StartedUtc, string MatchId) DecodeCursor(string cursor)
    {
        try
        {
            var decoded = Base64UrlDecode(cursor);
            var separator = decoded.IndexOf('\n');
            if (separator <= 0 || separator == decoded.Length - 1)
                throw new FormatException();
            return (decoded[..separator], decoded[(separator + 1)..]);
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            throw new ArgumentException("分页游标无效", nameof(cursor));
        }
    }

    private static string Base64UrlEncode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
