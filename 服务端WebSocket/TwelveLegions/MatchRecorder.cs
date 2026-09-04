using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        await EnsureColumnAsync(connection, "matches", "mode_id", "TEXT NOT NULL DEFAULT 'legacy'");
        await EnsureColumnAsync(connection, "matches", "account_0", "TEXT");
        await EnsureColumnAsync(connection, "matches", "account_1", "TEXT");
        var closeSandboxResidue = connection.CreateCommand();
        closeSandboxResidue.CommandText = """
            UPDATE matches SET ended_utc=$utc,
                error=COALESCE(error,'历史沙盒对局清理关闭')
            WHERE ended_utc IS NULL AND mode_id='sandbox';
            """;
        closeSandboxResidue.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await closeSandboxResidue.ExecuteNonQueryAsync();
    }

    public async Task StartAsync(L12GameState state, string modeId = "friendly",
        string? account0 = null, string? account1 = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO matches(match_id, room_code, seed, player_0, player_1, deck_0, deck_1, started_utc, mode_id, account_0, account_1)
            VALUES($id,$room,$seed,$p0,$p1,$d0,$d1,$utc,$mode,$a0,$a1);
            """;
        command.Parameters.AddWithValue("$id", state.MatchId);
        command.Parameters.AddWithValue("$room", state.RoomCode);
        command.Parameters.AddWithValue("$seed", state.Seed);
        command.Parameters.AddWithValue("$p0", state.Players[0].Name);
        command.Parameters.AddWithValue("$p1", state.Players[1].Name);
        command.Parameters.AddWithValue("$d0", state.Players[0].DeckName);
        command.Parameters.AddWithValue("$d1", state.Players[1].DeckName);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$mode", string.IsNullOrWhiteSpace(modeId)
            ? "friendly" : modeId.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$a0", (object?)account0 ?? DBNull.Value);
        command.Parameters.AddWithValue("$a1", (object?)account1 ?? DBNull.Value);
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

    public Task AppendAuthorityAsync(L12GameEngine engine, long sequence, string reason)
        => AppendAsync(engine, sequence, -1,
            JsonSerializer.Serialize(new { type = "authorityConclusion", reason }), CommandResult.Ok());

    public async Task<bool> CompleteAsync(L12GameEngine engine)
    {
        if (engine.State.Phase != L12Phase.GameOver)
            throw new InvalidOperationException("只能结束已经进入 GameOver 的正式对局记录");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var finalHash = engine.ComputeStateHash();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE matches SET ended_utc=$utc,winner=$winner,final_hash=$hash
            WHERE match_id=$id AND ended_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$winner", (object?)engine.State.Winner ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", finalHash);
        command.Parameters.AddWithValue("$id", engine.State.MatchId);
        if (await command.ExecuteNonQueryAsync() == 1) return true;

        var existing = connection.CreateCommand();
        existing.CommandText = "SELECT winner,final_hash,ended_utc FROM matches WHERE match_id=$id;";
        existing.Parameters.AddWithValue("$id", engine.State.MatchId);
        await using var reader = await existing.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("找不到待结束的正式对局记录");
        var recordedWinner = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0);
        var recordedHash = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (reader.IsDBNull(2) || recordedWinner != engine.State.Winner
            || !string.Equals(recordedHash, finalHash, StringComparison.Ordinal))
            throw new InvalidOperationException("重复结束请求与已记录的正式赛果冲突");
        return false;
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

    public async Task<IReadOnlyList<L12MatchSummary>> ListMatchesForPlayerAsync(string playerName, int limit = 50)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.room_code,m.player_0,m.player_1,m.deck_0,m.deck_1,
                   m.started_utc,m.ended_utc,m.winner,m.final_hash,m.error,COUNT(e.id)
            FROM matches m LEFT JOIN match_events e ON e.match_id=m.match_id
            WHERE (m.player_0=$player OR m.player_1=$player) AND m.mode_id <> 'sandbox'
              AND m.ended_utc IS NOT NULL
            GROUP BY m.match_id ORDER BY m.started_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$player", playerName);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        var matches = new List<L12MatchSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) matches.Add(ReadSummary(reader));
        return matches;
    }

    public async Task<IReadOnlyList<L12RankingMatch>> ListRankingMatchesAsync(int limit = 500)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.match_id,m.player_0,m.player_1,m.started_utc,m.ended_utc,m.winner,e.state_json
            FROM matches m
            LEFT JOIN match_events e ON e.id=(
                SELECT latest.id FROM match_events latest
                WHERE latest.match_id=m.match_id ORDER BY latest.sequence DESC LIMIT 1
            )
            WHERE m.ended_utc IS NOT NULL AND m.mode_id='ranked'
            ORDER BY m.started_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20_000));
        var matches = new List<L12RankingMatch>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var master0 = string.Empty;
            var master1 = string.Empty;
            var firstPlayer = 0;
            if (!reader.IsDBNull(6))
            {
                using var state = JsonDocument.Parse(reader.GetString(6));
                var root = state.RootElement;
                firstPlayer = ReadInt(root, "FirstPlayer", "firstPlayer");
                if (TryProperty(root, "Players", "players", out var players) && players.ValueKind == JsonValueKind.Array)
                {
                    if (players.GetArrayLength() > 0) master0 = ReadString(players[0], "MasterName", "masterName");
                    if (players.GetArrayLength() > 1) master1 = ReadString(players[1], "MasterName", "masterName");
                }
            }
            matches.Add(new L12RankingMatch(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                master0, master1, firstPlayer));
        }
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

    public async Task<L12MatchDetail?> GetMatchForPlayerAsync(string matchId, string playerName)
    {
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            var visible = connection.CreateCommand();
            visible.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM matches
                    WHERE match_id=$id AND ended_utc IS NOT NULL AND mode_id <> 'sandbox'
                      AND (player_0=$player OR player_1=$player)
                );
                """;
            visible.Parameters.AddWithValue("$id", matchId);
            visible.Parameters.AddWithValue("$player", playerName);
            if (Convert.ToInt32(await visible.ExecuteScalarAsync()) != 1) return null;
        }
        var detail = await GetMatchAsync(matchId);
        if (detail is null) return null;
        var viewer = string.Equals(detail.Match.Player0, playerName, StringComparison.OrdinalIgnoreCase) ? 0
            : string.Equals(detail.Match.Player1, playerName, StringComparison.OrdinalIgnoreCase) ? 1
            : -1;
        if (viewer < 0) return null;
        var commands = detail.Commands.Select(command => command with
        {
            Command = SanitizeRecordedCommand(command.Command, command.PlayerIndex == viewer),
            State = SanitizeRecordedState(command.State, viewer),
        }).ToArray();
        return new L12MatchDetail(detail.Match, commands, viewer);
    }

    public async Task<int> AnonymizePlayerAsync(string playerName, string anonymousName)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var matches = new List<string>();
        var selectMatches = connection.CreateCommand();
        selectMatches.Transaction = (SqliteTransaction)transaction;
        selectMatches.CommandText = "SELECT match_id,player_0,player_1 FROM matches WHERE player_0=$player OR player_1=$player;";
        selectMatches.Parameters.AddWithValue("$player", playerName);
        await using (var reader = await selectMatches.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                matches.Add(reader.GetString(0));
        }

        foreach (var match in matches)
        {
            var updateMatch = connection.CreateCommand();
            updateMatch.Transaction = (SqliteTransaction)transaction;
            updateMatch.CommandText = """
                UPDATE matches SET
                    player_0=CASE WHEN player_0=$player THEN $anonymous ELSE player_0 END,
                    player_1=CASE WHEN player_1=$player THEN $anonymous ELSE player_1 END,
                    deck_0=CASE WHEN player_0=$player THEN '已清理牌库' ELSE deck_0 END,
                    deck_1=CASE WHEN player_1=$player THEN '已清理牌库' ELSE deck_1 END
                WHERE match_id=$id;
                """;
            updateMatch.Parameters.AddWithValue("$player", playerName);
            updateMatch.Parameters.AddWithValue("$anonymous", anonymousName);
            updateMatch.Parameters.AddWithValue("$id", match);
            await updateMatch.ExecuteNonQueryAsync();

            var events = new List<(long Id, string Command, string State)>();
            var selectEvents = connection.CreateCommand();
            selectEvents.Transaction = (SqliteTransaction)transaction;
            selectEvents.CommandText = "SELECT id,command_json,state_json FROM match_events WHERE match_id=$id;";
            selectEvents.Parameters.AddWithValue("$id", match);
            await using (var reader = await selectEvents.ExecuteReaderAsync())
                while (await reader.ReadAsync()) events.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            foreach (var recorded in events)
            {
                var updateEvent = connection.CreateCommand();
                updateEvent.Transaction = (SqliteTransaction)transaction;
                updateEvent.CommandText = "UPDATE match_events SET command_json=$command,state_json=$state WHERE id=$id;";
                updateEvent.Parameters.AddWithValue("$command", ScrubJsonString(recorded.Command, playerName, anonymousName));
                updateEvent.Parameters.AddWithValue("$state", ScrubJsonString(recorded.State, playerName, anonymousName));
                updateEvent.Parameters.AddWithValue("$id", recorded.Id);
                await updateEvent.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
        return matches.Count;
    }

    private static string ScrubJsonString(string json, string value, string replacement)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return ScrubNode(node, value, replacement)?.ToJsonString() ?? json;
        }
        catch { return json.Replace(value, replacement, StringComparison.Ordinal); }
    }

    private static JsonNode? ScrubNode(JsonNode? node, string value, string replacement)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(item => item.Key).ToArray())
            {
                var current = obj[key];
                var scrubbed = ScrubNode(current, value, replacement);
                if (!ReferenceEquals(current, scrubbed)) obj[key] = scrubbed;
            }
            return obj;
        }
        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var current = array[index];
                var scrubbed = ScrubNode(current, value, replacement);
                if (!ReferenceEquals(current, scrubbed)) array[index] = scrubbed;
            }
            return array;
        }
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            return JsonValue.Create(text.Replace(value, replacement, StringComparison.Ordinal));
        return node;
    }

    private static JsonElement SanitizeRecordedCommand(JsonElement command, bool ownCommand)
    {
        if (ownCommand) return command;
        var type = command.TryGetProperty("type", out var camel) ? camel.GetString()
            : command.TryGetProperty("Type", out var pascal) ? pascal.GetString() : string.Empty;
        return JsonSerializer.SerializeToElement(new { type });
    }

    private static JsonElement SanitizeRecordedState(JsonElement state, int viewer)
    {
        var root = JsonNode.Parse(state.GetRawText())?.AsObject();
        if (root is null) return state;
        var players = root["Players"] as JsonArray;
        if (players is not null)
        {
            for (var playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                if (players[playerIndex] is not JsonObject player) continue;
                RedactCardArray(player["Library"] as JsonArray, "牌库");
                if (playerIndex != viewer) RedactCardArray(player["Hand"] as JsonArray, "对方手牌");
                RedactCoveredField(player["Field"] as JsonArray, playerIndex, viewer);
                if (playerIndex != viewer && player["SpecialZones"] is JsonObject zones
                    && zones["Trials"] is JsonArray trials)
                {
                    for (var index = 0; index < trials.Count; index++)
                    {
                        if (trials[index] is JsonObject trial && trial["TrialCompleted"]?.GetValue<bool>() != true)
                            trials[index] = HiddenCard($"hidden-trial-{index}", "未揭示试炼");
                    }
                }
            }
        }
        RedactCardArray(root["DisasterDeck"] as JsonArray, "天灾牌库");
        if (root["OperationsPolicy"] is JsonObject operationsPolicy)
        {
            operationsPolicy.Remove("VersionId");
            operationsPolicy.Remove("DisasterCardIds");
            operationsPolicy.Remove("FeatureFlags");
        }
        RedactEffectHandAddStack(root["EffectStack"] as JsonArray, players, viewer);
        RedactEffectHandAddStack(root["DeferredEffectStack"] as JsonArray, players, viewer);
        if (root["AuthorityEvents"] is JsonArray authorityEvents)
        {
            foreach (var node in authorityEvents)
            {
                if (node is not JsonObject authority || authority["Type"]?.GetValue<string>() != "effect-hand-add") continue;
                authority["SourceInstanceId"] = string.Empty;
                authority["TargetInstanceId"] = string.Empty;
            }
        }
        root["PendingPrompts"] = new JsonArray();
        root["PendingActivations"] = new JsonArray();
        return JsonSerializer.SerializeToElement(root);
    }

    private static void RedactCardArray(JsonArray? cards, string label)
    {
        if (cards is null) return;
        for (var index = 0; index < cards.Count; index++)
            cards[index] = HiddenCard($"hidden-{label}-{index}", label);
    }

    private static void RedactCoveredField(JsonArray? field, int controller, int viewer)
    {
        if (field is null) return;
        foreach (var rowNode in field)
        {
            if (rowNode is not JsonArray row) continue;
            for (var slot = 0; slot < row.Count; slot++)
            {
                if (row[slot] is not JsonObject card || card["Hidden"]?.GetValue<bool>() != true) continue;
                var owner = card["OwnerIndex"] is JsonValue ownerValue && ownerValue.TryGetValue<int>(out var parsed)
                    ? parsed : controller;
                if (owner == viewer)
                {
                    card["IdentityKnown"] = true;
                    continue;
                }
                var instanceId = card["InstanceId"]?.GetValue<string>() ?? $"hidden-field-{controller}-{slot}";
                row[slot] = HiddenCard(instanceId, "覆盖的卡牌");
            }
        }
    }

    private static JsonObject HiddenCard(string instanceId, string label) => new()
    {
        ["InstanceId"] = instanceId,
        ["CardId"] = "hidden-card",
        ["Name"] = label,
        ["CardType"] = "covered",
        ["Faction"] = "hidden",
        ["ImageUrl"] = "/assets/l12/card-back-official.png",
        ["Cost"] = 0,
        ["BaseTroops"] = 0,
        ["Troops"] = 0,
        ["DisasterLevel"] = 0,
        ["Hidden"] = true,
        ["IdentityKnown"] = false,
        ["Tapped"] = false,
        ["SummonRound"] = 0,
    };

    private static void RedactEffectHandAddStack(JsonArray? stack, JsonArray? players, int viewer)
    {
        if (stack is null) return;
        foreach (var node in stack)
        {
            if (node is not JsonObject item || item["Data"] is not JsonObject data
                || data["eventType"]?.GetValue<string>() != "effect-hand-add") continue;
            item["SourceInstanceId"] = string.Empty;
            item["SourceCardId"] = string.Empty;
            item["SourceName"] = "加入手牌事件";
            var controller = item["Controller"]?.GetValue<int>() ?? viewer;
            var playerName = players?[controller]?["Name"]?.GetValue<string>() ?? "玩家";
            item["Text"] = $"{playerName}因效果将1张牌加入手牌";
            item["Targets"] = new JsonArray();
            data["targetInstanceId"] = string.Empty;
        }
    }

    private static bool TryProperty(JsonElement element, string pascal, string camel, out JsonElement value)
        => element.TryGetProperty(pascal, out value) || element.TryGetProperty(camel, out value);

    private static int ReadInt(JsonElement element, string pascal, string camel)
        => TryProperty(element, pascal, camel, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static string ReadString(JsonElement element, string pascal, string camel)
        => TryProperty(element, pascal, camel, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static L12MatchSummary ReadSummary(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
        reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.GetInt32(11));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column,
        string declaration)
    {
        var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        await using (var reader = await inspect.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        await alter.ExecuteNonQueryAsync();
    }
}

public sealed record L12MatchSummary(
    string MatchId, string RoomCode, string Player0, string Player1, string Deck0, string Deck1,
    string StartedUtc, string? EndedUtc, int? Winner, string? FinalHash, string? Error, int CommandCount);

public sealed record L12RecordedCommand(
    long Sequence, string ReceivedUtc, int PlayerIndex, JsonElement Command, bool Accepted,
    string? Error, long Revision, string StateHash, JsonElement State);

public sealed record L12MatchDetail(L12MatchSummary Match, IReadOnlyList<L12RecordedCommand> Commands,
    int? ViewerPlayerIndex = null);

public sealed record L12RankingMatch(
    string MatchId, string Player0, string Player1, string StartedUtc, string EndedUtc, int? Winner,
    string Master0, string Master1, int FirstPlayer);
