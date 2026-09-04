using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private sealed record PendingCardFact(
        string MatchId,
        string FactKey,
        string Kind,
        long CommandSequence,
        long Revision,
        int Round,
        int Turn,
        string Phase,
        string OccurredUtc,
        int? PlayerIndex,
        string? AccountId,
        string? CardId,
        string? CardInstanceId,
        string? RelatedCardId,
        string? RelatedInstanceId,
        string? SourceZone,
        string? DestinationZone,
        int? Amount,
        string Coverage,
        string MetadataJson);

    private sealed record CardLocation(
        int PlayerIndex,
        string CardId,
        string InstanceId,
        string Zone,
        int Troops);

    private sealed record ParticipantIdentity(int PlayerIndex, string? AccountId);

    private static async Task InitializeAnalyticsSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS match_recorder_schema (
                component TEXT PRIMARY KEY,
                version INTEGER NOT NULL,
                migrated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS match_participants (
                match_id TEXT NOT NULL,
                player_index INTEGER NOT NULL,
                account_id TEXT,
                display_name TEXT NOT NULL,
                master_id TEXT,
                master_name TEXT,
                deck_name TEXT NOT NULL,
                deck_snapshot_coverage TEXT NOT NULL DEFAULT 'legacy-unavailable',
                PRIMARY KEY(match_id, player_index),
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS match_deck_cards (
                match_id TEXT NOT NULL,
                player_index INTEGER NOT NULL,
                section TEXT NOT NULL,
                card_id TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                PRIMARY KEY(match_id, player_index, section, card_id),
                FOREIGN KEY(match_id, player_index) REFERENCES match_participants(match_id, player_index)
                    ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS match_card_facts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                match_id TEXT NOT NULL,
                fact_key TEXT NOT NULL,
                command_sequence INTEGER NOT NULL,
                revision INTEGER NOT NULL,
                round INTEGER NOT NULL,
                turn INTEGER NOT NULL,
                phase TEXT NOT NULL,
                occurred_utc TEXT NOT NULL,
                kind TEXT NOT NULL,
                player_index INTEGER,
                account_id TEXT,
                card_id TEXT,
                card_instance_id TEXT,
                related_card_id TEXT,
                related_instance_id TEXT,
                source_zone TEXT,
                destination_zone TEXT,
                amount INTEGER,
                coverage TEXT NOT NULL,
                metadata_json TEXT NOT NULL DEFAULT '{}',
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE,
                UNIQUE(match_id, fact_key)
            );
            CREATE INDEX IF NOT EXISTS ix_matches_admin_started
                ON matches(mode_id, started_utc DESC, match_id DESC);
            CREATE INDEX IF NOT EXISTS ix_matches_admin_status
                ON matches(ended_utc, started_utc DESC, match_id DESC);
            CREATE INDEX IF NOT EXISTS ix_match_participants_account
                ON match_participants(account_id, match_id, player_index);
            CREATE INDEX IF NOT EXISTS ix_match_participants_name
                ON match_participants(display_name, match_id, player_index);
            CREATE INDEX IF NOT EXISTS ix_match_participants_master
                ON match_participants(master_id, match_id, player_index);
            CREATE INDEX IF NOT EXISTS ix_match_deck_cards_card
                ON match_deck_cards(card_id, match_id, player_index);
            CREATE INDEX IF NOT EXISTS ix_match_card_facts_match_sequence
                ON match_card_facts(match_id, command_sequence, id);
            CREATE INDEX IF NOT EXISTS ix_match_card_facts_card_kind
                ON match_card_facts(card_id, kind, match_id, player_index);
            INSERT OR IGNORE INTO match_participants(
                match_id,player_index,account_id,display_name,master_id,master_name,deck_name,deck_snapshot_coverage)
            SELECT match_id,0,account_0,player_0,NULL,NULL,deck_0,'legacy-unavailable' FROM matches;
            INSERT OR IGNORE INTO match_participants(
                match_id,player_index,account_id,display_name,master_id,master_name,deck_name,deck_snapshot_coverage)
            SELECT match_id,1,account_1,player_1,NULL,NULL,deck_1,'legacy-unavailable' FROM matches;
            INSERT INTO match_recorder_schema(component,version,migrated_utc)
            VALUES('match-analytics',1,$utc)
            ON CONFLICT(component) DO UPDATE SET
                version=MAX(version,excluded.version), migrated_utc=excluded.migrated_utc;
            """;
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PersistMatchStartAnalyticsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        L12GameState state,
        IReadOnlyList<L12PresetDeckDefinition>? decks,
        string? account0,
        string? account1,
        string occurredUtc,
        IReadOnlyList<L12CardFactSignal> initialSignals)
    {
        if (decks is not null && decks.Count != 2)
            throw new ArgumentException("正式对局构筑快照必须恰好包含两名玩家", nameof(decks));

        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var player = state.Players[playerIndex];
            var accountId = playerIndex == 0 ? account0 : account1;
            var coverage = decks is null ? "inferred" : "exact";
            var participant = connection.CreateCommand();
            participant.Transaction = transaction;
            participant.CommandText = """
                INSERT INTO match_participants(
                    match_id,player_index,account_id,display_name,master_id,master_name,deck_name,deck_snapshot_coverage)
                VALUES($match,$player,$account,$name,$master,$masterName,$deck,$coverage);
                """;
            participant.Parameters.AddWithValue("$match", state.MatchId);
            participant.Parameters.AddWithValue("$player", playerIndex);
            participant.Parameters.AddWithValue("$account", (object?)accountId ?? DBNull.Value);
            participant.Parameters.AddWithValue("$name", player.Name);
            participant.Parameters.AddWithValue("$master", player.MasterId);
            participant.Parameters.AddWithValue("$masterName", player.MasterName);
            participant.Parameters.AddWithValue("$deck", player.DeckName);
            participant.Parameters.AddWithValue("$coverage", coverage);
            await participant.ExecuteNonQueryAsync();

            var sections = decks is null
                ? InferDeckSections(player)
                : DeckSections(decks[playerIndex]);
            foreach (var section in sections)
            {
                foreach (var group in section.Value
                             .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                             .GroupBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var deckCard = connection.CreateCommand();
                    deckCard.Transaction = transaction;
                    deckCard.CommandText = """
                        INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
                        VALUES($match,$player,$section,$card,$quantity);
                        """;
                    deckCard.Parameters.AddWithValue("$match", state.MatchId);
                    deckCard.Parameters.AddWithValue("$player", playerIndex);
                    deckCard.Parameters.AddWithValue("$section", section.Key);
                    deckCard.Parameters.AddWithValue("$card", group.Key);
                    deckCard.Parameters.AddWithValue("$quantity", group.Count());
                    await deckCard.ExecuteNonQueryAsync();

                    await InsertFactAsync(connection, transaction, new PendingCardFact(
                        state.MatchId, $"start:deck:{playerIndex}:{section.Key}:{group.Key.ToUpperInvariant()}",
                        "deck-included", 0, state.Revision, state.Round, state.TurnSerial,
                        state.Phase.ToString(), occurredUtc, playerIndex, accountId,
                        group.Key, null, null, null, "deck", section.Key, group.Count(), coverage,
                        JsonSerializer.Serialize(new { section = section.Key, quantity = group.Count() })));
                }
            }
        }

        foreach (var signal in initialSignals)
        {
            var accountId = signal.PlayerIndex switch
            {
                0 => account0,
                1 => account1,
                _ => null,
            };
            await InsertFactAsync(connection, transaction, new PendingCardFact(
                state.MatchId, $"signal:{signal.Sequence}", signal.Kind, 0, signal.Revision,
                signal.Round, signal.Turn, signal.Phase, occurredUtc,
                signal.PlayerIndex, accountId, signal.CardId, signal.CardInstanceId, null, null,
                signal.SourceZone, signal.DestinationZone, signal.Amount, signal.Coverage,
                JsonSerializer.Serialize(signal.Data ?? new Dictionary<string, string>())));
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DeckSections(L12PresetDeckDefinition deck)
        => new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["main"] = deck.CardIds,
            ["morale"] = deck.MoraleIds,
            ["special"] = deck.SpecialIds,
        };

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> InferDeckSections(L12PlayerState player)
    {
        var prefix = $"p{player.PlayerIndex}-c";
        var main = EnumeratePlayerCards(player)
            .Where(card => card.InstanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(card => card.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().CardId)
            .ToArray();
        var morale = player.MoraleDeck.Concat(player.Morale)
            .GroupBy(card => card.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().CardId)
            .ToArray();
        var special = player.SpecialZones.Trials
            .Where(card => card.InstanceId.StartsWith($"p{player.PlayerIndex}-special-",
                StringComparison.OrdinalIgnoreCase))
            .Select(card => card.CardId)
            .ToArray();
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["main"] = main,
            ["morale"] = morale,
            ["special"] = special,
        };
    }

    private async Task AppendWithCardFactsAsync(L12GameEngine engine, long sequence, int playerIndex,
        string commandJson, CommandResult result)
    {
        var stateJson = engine.SerializeFullState();
        var stateHash = engine.ComputeStateHash();
        var occurredUtc = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var duplicate = connection.CreateCommand();
        duplicate.Transaction = transaction;
        duplicate.CommandText = """
            SELECT player_index,accepted,revision,state_hash
            FROM match_events WHERE match_id=$match AND sequence=$sequence;
            """;
        duplicate.Parameters.AddWithValue("$match", engine.State.MatchId);
        duplicate.Parameters.AddWithValue("$sequence", sequence);
        await using (var reader = await duplicate.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                var same = reader.GetInt32(0) == playerIndex
                    && (reader.GetInt32(1) == 1) == result.Accepted
                    && reader.GetInt64(2) == engine.State.Revision
                    && string.Equals(reader.GetString(3), stateHash, StringComparison.Ordinal);
                if (!same)
                    throw new InvalidOperationException("同一对局命令序号的重复写入与已记录状态冲突");
                await transaction.CommitAsync();
                return;
            }
        }

        var context = connection.CreateCommand();
        context.Transaction = transaction;
        context.CommandText = """
            SELECT COALESCE(
                       (SELECT state_json FROM match_events
                        WHERE match_id=$match ORDER BY sequence DESC LIMIT 1),
                       initial_state_json,
                       '{}'),
                   last_fact_signal_sequence
            FROM matches WHERE match_id=$match;
            """;
        context.Parameters.AddWithValue("$match", engine.State.MatchId);
        string previousStateJson;
        long lastSignalSequence;
        await using (var reader = await context.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new KeyNotFoundException("找不到待追加的正式对局记录");
            previousStateJson = reader.IsDBNull(0) ? "{}" : reader.GetString(0);
            lastSignalSequence = reader.GetInt64(1);
        }

        var append = connection.CreateCommand();
        append.Transaction = transaction;
        append.CommandText = """
            INSERT INTO match_events(match_id, sequence, received_utc, player_index, command_json, accepted,
                                     error, revision, state_hash, state_json)
            VALUES($id,$seq,$utc,$player,$json,$accepted,$error,$revision,$hash,$state);
            """;
        append.Parameters.AddWithValue("$id", engine.State.MatchId);
        append.Parameters.AddWithValue("$seq", sequence);
        append.Parameters.AddWithValue("$utc", occurredUtc);
        append.Parameters.AddWithValue("$player", playerIndex);
        append.Parameters.AddWithValue("$json", commandJson);
        append.Parameters.AddWithValue("$accepted", result.Accepted ? 1 : 0);
        append.Parameters.AddWithValue("$error", (object?)result.Error ?? DBNull.Value);
        append.Parameters.AddWithValue("$revision", engine.State.Revision);
        append.Parameters.AddWithValue("$hash", stateHash);
        append.Parameters.AddWithValue("$state", stateJson);
        await append.ExecuteNonQueryAsync();

        var identities = await ReadParticipantIdentitiesAsync(connection, transaction, engine.State.MatchId);
        var newSignals = engine.CardFactSignals.Where(signal => signal.Sequence > lastSignalSequence).ToArray();
        var explicitFacts = newSignals
            .Where(signal => signal.CardInstanceId is not null)
            .Select(signal => (signal.Kind, signal.CardInstanceId!))
            .ToHashSet();
        foreach (var fact in ExtractDeltaFacts(previousStateJson, engine.State, sequence, occurredUtc,
                     identities, explicitFacts))
            await InsertFactAsync(connection, transaction, fact);

        foreach (var signal in newSignals)
        {
            var accountId = signal.PlayerIndex is { } signalPlayer
                ? identities.GetValueOrDefault(signalPlayer)?.AccountId
                : null;
            await InsertFactAsync(connection, transaction, new PendingCardFact(
                engine.State.MatchId, $"signal:{signal.Sequence}", signal.Kind, sequence, signal.Revision,
                signal.Round, signal.Turn, signal.Phase, occurredUtc,
                signal.PlayerIndex, accountId, signal.CardId, signal.CardInstanceId, null, null,
                signal.SourceZone, signal.DestinationZone, signal.Amount, signal.Coverage,
                JsonSerializer.Serialize(signal.Data ?? new Dictionary<string, string>())));
        }

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE matches SET first_player=$first,last_fact_signal_sequence=$signal
            WHERE match_id=$match;
            """;
        update.Parameters.AddWithValue("$first", engine.State.FirstPlayer);
        update.Parameters.AddWithValue("$signal", newSignals.Length == 0
            ? lastSignalSequence : newSignals.Max(signal => signal.Sequence));
        update.Parameters.AddWithValue("$match", engine.State.MatchId);
        await update.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<Dictionary<int, ParticipantIdentity>> ReadParticipantIdentitiesAsync(
        SqliteConnection connection, SqliteTransaction transaction, string matchId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT player_index,account_id FROM match_participants WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", matchId);
        var result = new Dictionary<int, ParticipantIdentity>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var playerIndex = reader.GetInt32(0);
            result[playerIndex] = new ParticipantIdentity(playerIndex,
                reader.IsDBNull(1) ? null : reader.GetString(1));
        }
        return result;
    }

    private static async Task InsertFactAsync(SqliteConnection connection, SqliteTransaction transaction,
        PendingCardFact fact)
    {
        if (!L12CardFactKinds.Supported.Contains(fact.Kind, StringComparer.Ordinal))
            throw new InvalidDataException($"未登记的对局卡牌事实类型：{fact.Kind}");
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO match_card_facts(
                match_id,fact_key,command_sequence,revision,round,turn,phase,occurred_utc,kind,player_index,account_id,
                card_id,card_instance_id,related_card_id,related_instance_id,source_zone,destination_zone,
                amount,coverage,metadata_json)
            VALUES($match,$key,$sequence,$revision,$round,$turn,$phase,$utc,$kind,$player,$account,$card,$instance,
                   $relatedCard,$relatedInstance,$source,$destination,$amount,$coverage,$metadata);
            """;
        insert.Parameters.AddWithValue("$match", fact.MatchId);
        insert.Parameters.AddWithValue("$key", fact.FactKey);
        insert.Parameters.AddWithValue("$sequence", fact.CommandSequence);
        insert.Parameters.AddWithValue("$revision", fact.Revision);
        insert.Parameters.AddWithValue("$round", fact.Round);
        insert.Parameters.AddWithValue("$turn", fact.Turn);
        insert.Parameters.AddWithValue("$phase", fact.Phase);
        insert.Parameters.AddWithValue("$utc", fact.OccurredUtc);
        insert.Parameters.AddWithValue("$kind", fact.Kind);
        insert.Parameters.AddWithValue("$player", (object?)fact.PlayerIndex ?? DBNull.Value);
        insert.Parameters.AddWithValue("$account", (object?)fact.AccountId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$card", (object?)fact.CardId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$instance", (object?)fact.CardInstanceId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$relatedCard", (object?)fact.RelatedCardId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$relatedInstance", (object?)fact.RelatedInstanceId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$source", (object?)fact.SourceZone ?? DBNull.Value);
        insert.Parameters.AddWithValue("$destination", (object?)fact.DestinationZone ?? DBNull.Value);
        insert.Parameters.AddWithValue("$amount", (object?)fact.Amount ?? DBNull.Value);
        insert.Parameters.AddWithValue("$coverage", fact.Coverage);
        insert.Parameters.AddWithValue("$metadata", fact.MetadataJson);
        await insert.ExecuteNonQueryAsync();
    }

    private static IReadOnlyList<PendingCardFact> ExtractDeltaFacts(
        string previousStateJson,
        L12GameState currentState,
        long commandSequence,
        string occurredUtc,
        IReadOnlyDictionary<int, ParticipantIdentity> identities,
        IReadOnlySet<(string Kind, string InstanceId)> explicitFacts)
    {
        var previous = CaptureLocations(previousStateJson);
        var current = CaptureLocations(currentState);
        var facts = new List<PendingCardFact>();
        var ordinal = 0;

        foreach (var pair in current.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var currentCard = pair.Value;
            var accountId = identities.GetValueOrDefault(currentCard.PlayerIndex)?.AccountId;
            if (!previous.TryGetValue(pair.Key, out var previousCard))
            {
                facts.Add(DeltaFact("zone-move", ++ordinal, currentState, commandSequence,
                    occurredUtc, currentCard, accountId, null, currentCard.Zone,
                    coverage: "partial", metadata: new { origin = "generated", timing = "command-boundary" }));
                continue;
            }

            if (!string.Equals(previousCard.Zone, currentCard.Zone, StringComparison.Ordinal))
            {
                facts.Add(DeltaFact("zone-move", ++ordinal, currentState, commandSequence,
                    occurredUtc, currentCard, accountId, previousCard.Zone,
                    currentCard.Zone, coverage: "partial", metadata: new { timing = "command-boundary" }));
                if (currentCard.Zone == "hand"
                    && !explicitFacts.Contains(("draw", currentCard.InstanceId))
                    && !explicitFacts.Contains(("search-or-hand-add", currentCard.InstanceId)))
                {
                    facts.Add(DeltaFact("search-or-hand-add", ++ordinal, currentState,
                        commandSequence, occurredUtc, currentCard, accountId,
                        previousCard.Zone, "hand", coverage: "partial",
                        metadata: new { reason = "authoritative-zone-transition" }));
                }
            }

            if (currentCard.Troops < previousCard.Troops)
            {
                facts.Add(DeltaFact("damage", ++ordinal, currentState, commandSequence,
                    occurredUtc, currentCard, accountId, currentCard.Zone,
                    currentCard.Zone, previousCard.Troops - currentCard.Troops, "partial",
                    new { role = "target", sourceAttribution = "unavailable" }));
            }
        }

        foreach (var pair in previous.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (current.ContainsKey(pair.Key)) continue;
            var card = pair.Value;
            var accountId = identities.GetValueOrDefault(card.PlayerIndex)?.AccountId;
            facts.Add(DeltaFact("zone-move", ++ordinal, currentState, commandSequence,
                occurredUtc, card, accountId, card.Zone, "vanished",
                coverage: "partial", metadata: new { destination = "untracked-or-vanished", timing = "command-boundary" }));
        }
        return facts;
    }

    private static PendingCardFact DeltaFact(string kind, int ordinal, L12GameState state,
        long commandSequence, string occurredUtc, CardLocation card, string? accountId,
        string? sourceZone, string? destinationZone, int? amount = null, string coverage = "exact",
        object? metadata = null)
        => new(state.MatchId, $"delta:{commandSequence}:{ordinal}", kind, commandSequence, state.Revision,
            state.Round, state.TurnSerial, state.Phase.ToString(), occurredUtc, card.PlayerIndex, accountId,
            card.CardId, card.InstanceId, null, null,
            sourceZone, destinationZone, amount, coverage,
            JsonSerializer.Serialize(metadata ?? new { }));

    private static Dictionary<string, CardLocation> CaptureLocations(L12GameState state)
    {
        var result = new Dictionary<string, CardLocation>(StringComparer.Ordinal);
        foreach (var player in state.Players)
        {
            AddCards(result, player.PlayerIndex, "library", player.Library);
            AddCards(result, player.PlayerIndex, "hand", player.Hand);
            for (var row = 0; row < player.Field.Length; row++)
                for (var slot = 0; slot < player.Field[row].Length; slot++)
                    if (player.Field[row][slot] is { } card)
                        AddCard(result, player.PlayerIndex, $"field:{row}:{slot}", card);
            if (player.Relic is { } relic) AddCard(result, player.PlayerIndex, "relic", relic);
            AddCards(result, player.PlayerIndex, "extra-relic", player.ExtraRelics);
            AddCards(result, player.PlayerIndex, "resolving", player.Resolving);
            AddCards(result, player.PlayerIndex, "graveyard", player.Graveyard);
            AddCards(result, player.PlayerIndex, "removed", player.Removed);
            AddCards(result, player.PlayerIndex, "special:god-power", player.SpecialZones.GodPower);
            AddCards(result, player.PlayerIndex, "special:trials", player.SpecialZones.Trials);
            AddCards(result, player.PlayerIndex, "special:canopic", player.SpecialZones.CanopicProgress);
        }
        return result;
    }

    private static Dictionary<string, CardLocation> CaptureLocations(string stateJson)
    {
        var result = new Dictionary<string, CardLocation>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(stateJson);
            if (!TryProperty(document.RootElement, "Players", "players", out var players)
                || players.ValueKind != JsonValueKind.Array) return result;
            var fallbackIndex = 0;
            foreach (var player in players.EnumerateArray())
            {
                var playerIndex = TryProperty(player, "PlayerIndex", "playerIndex", out var indexElement)
                                  && indexElement.TryGetInt32(out var parsedIndex)
                    ? parsedIndex : fallbackIndex;
                AddJsonArray(result, playerIndex, player, "Library", "library", "library");
                AddJsonArray(result, playerIndex, player, "Hand", "hand", "hand");
                AddJsonArray(result, playerIndex, player, "ExtraRelics", "extraRelics", "extra-relic");
                AddJsonArray(result, playerIndex, player, "Resolving", "resolving", "resolving");
                AddJsonArray(result, playerIndex, player, "Graveyard", "graveyard", "graveyard");
                AddJsonArray(result, playerIndex, player, "Removed", "removed", "removed");
                if (TryProperty(player, "Relic", "relic", out var relic)
                    && relic.ValueKind == JsonValueKind.Object)
                    AddJsonCard(result, playerIndex, "relic", relic);
                if (TryProperty(player, "Field", "field", out var field)
                    && field.ValueKind == JsonValueKind.Array)
                {
                    var row = 0;
                    foreach (var rowElement in field.EnumerateArray())
                    {
                        var slot = 0;
                        if (rowElement.ValueKind == JsonValueKind.Array)
                            foreach (var card in rowElement.EnumerateArray())
                            {
                                if (card.ValueKind == JsonValueKind.Object)
                                    AddJsonCard(result, playerIndex, $"field:{row}:{slot}", card);
                                slot++;
                            }
                        row++;
                    }
                }
                if (TryProperty(player, "SpecialZones", "specialZones", out var special)
                    && special.ValueKind == JsonValueKind.Object)
                {
                    AddJsonArray(result, playerIndex, special, "GodPower", "godPower", "special:god-power");
                    AddJsonArray(result, playerIndex, special, "Trials", "trials", "special:trials");
                    AddJsonArray(result, playerIndex, special, "CanopicProgress", "canopicProgress", "special:canopic");
                }
                fallbackIndex++;
            }
        }
        catch (JsonException)
        {
            // A legacy row may not contain a compatible initial snapshot. New snapshots remain exact;
            // this command's delta is then intentionally reported as inferred.
        }
        return result;
    }

    private static void AddJsonArray(Dictionary<string, CardLocation> result, int playerIndex,
        JsonElement parent, string pascal, string camel, string zone)
    {
        if (!TryProperty(parent, pascal, camel, out var array) || array.ValueKind != JsonValueKind.Array) return;
        foreach (var card in array.EnumerateArray())
            if (card.ValueKind == JsonValueKind.Object) AddJsonCard(result, playerIndex, zone, card);
    }

    private static void AddJsonCard(Dictionary<string, CardLocation> result, int playerIndex,
        string zone, JsonElement card)
    {
        var instanceId = ReadString(card, "InstanceId", "instanceId");
        var cardId = ReadString(card, "CardId", "cardId");
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(cardId)) return;
        var troops = ReadInt(card, "Troops", "troops");
        result[instanceId] = new CardLocation(playerIndex, cardId, instanceId, zone, troops);
        if (TryProperty(card, "AttachedCards", "attachedCards", out var attached)
            && attached.ValueKind == JsonValueKind.Array)
            foreach (var child in attached.EnumerateArray())
                if (child.ValueKind == JsonValueKind.Object)
                    AddJsonCard(result, playerIndex, $"{zone}:attached:{instanceId}", child);
    }

    private static void AddCards(Dictionary<string, CardLocation> result, int playerIndex,
        string zone, IEnumerable<L12CardInstance> cards)
    {
        foreach (var card in cards) AddCard(result, playerIndex, zone, card);
    }

    private static void AddCard(Dictionary<string, CardLocation> result, int playerIndex,
        string zone, L12CardInstance card)
    {
        result[card.InstanceId] = new CardLocation(playerIndex, card.CardId, card.InstanceId, zone, card.Troops);
        foreach (var attached in card.AttachedCards)
            AddCard(result, attached.OwnerIndex ?? playerIndex, $"{zone}:attached:{card.InstanceId}", attached);
    }

    private static IEnumerable<L12CardInstance> EnumeratePlayerCards(L12PlayerState player)
    {
        IEnumerable<L12CardInstance> roots = player.Library.Concat(player.Hand)
            .Concat(player.Field.SelectMany(row => row).OfType<L12CardInstance>())
            .Concat(player.Relic is null ? [] : [player.Relic])
            .Concat(player.ExtraRelics).Concat(player.Resolving).Concat(player.Graveyard).Concat(player.Removed)
            .Concat(player.SpecialZones.GodPower).Concat(player.SpecialZones.Trials)
            .Concat(player.SpecialZones.CanopicProgress);
        foreach (var card in roots)
        {
            yield return card;
            foreach (var attached in EnumerateAttached(card)) yield return attached;
        }
    }

    private static IEnumerable<L12CardInstance> EnumerateAttached(L12CardInstance card)
    {
        foreach (var attached in card.AttachedCards)
        {
            yield return attached;
            foreach (var nested in EnumerateAttached(attached)) yield return nested;
        }
    }

    private async Task<int> AnonymizeIdentityAsync(string? accountId, string playerName, string anonymousName)
    {
        if (string.IsNullOrWhiteSpace(accountId) && string.IsNullOrWhiteSpace(playerName)) return 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var targets = new List<(string MatchId, int PlayerIndex)>();
        var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT m.match_id,0
            FROM matches m
            LEFT JOIN match_participants p ON p.match_id=m.match_id AND p.player_index=0
            WHERE ($account IS NOT NULL AND COALESCE(p.account_id,m.account_0)=$account)
               OR ($name <> '' AND COALESCE(p.display_name,m.player_0)=$name
                   AND ($account IS NULL OR COALESCE(p.account_id,m.account_0) IS NULL))
            UNION
            SELECT m.match_id,1
            FROM matches m
            LEFT JOIN match_participants p ON p.match_id=m.match_id AND p.player_index=1
            WHERE ($account IS NOT NULL AND COALESCE(p.account_id,m.account_1)=$account)
               OR ($name <> '' AND COALESCE(p.display_name,m.player_1)=$name
                   AND ($account IS NULL OR COALESCE(p.account_id,m.account_1) IS NULL));
            """;
        select.Parameters.AddWithValue("$account", (object?)accountId ?? DBNull.Value);
        select.Parameters.AddWithValue("$name", playerName);
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync()) targets.Add((reader.GetString(0), reader.GetInt32(1)));

        foreach (var match in targets.Select(target => target.MatchId).Distinct(StringComparer.Ordinal))
        {
            var playerIndexes = targets.Where(target => target.MatchId == match)
                .Select(target => target.PlayerIndex).Distinct().ToArray();
            foreach (var playerIndex in playerIndexes)
            {
                var updateMatch = connection.CreateCommand();
                updateMatch.Transaction = transaction;
                updateMatch.CommandText = playerIndex == 0
                    ? "UPDATE matches SET player_0=$anonymous,deck_0='已清理牌库',account_0=NULL WHERE match_id=$match;"
                    : "UPDATE matches SET player_1=$anonymous,deck_1='已清理牌库',account_1=NULL WHERE match_id=$match;";
                updateMatch.Parameters.AddWithValue("$anonymous", anonymousName);
                updateMatch.Parameters.AddWithValue("$match", match);
                await updateMatch.ExecuteNonQueryAsync();

                var updateParticipant = connection.CreateCommand();
                updateParticipant.Transaction = transaction;
                updateParticipant.CommandText = """
                    UPDATE match_participants
                    SET account_id=NULL,display_name=$anonymous,deck_name='已清理牌库'
                    WHERE match_id=$match AND player_index=$player;
                    UPDATE match_card_facts SET account_id=NULL
                    WHERE match_id=$match AND player_index=$player;
                    """;
                updateParticipant.Parameters.AddWithValue("$anonymous", anonymousName);
                updateParticipant.Parameters.AddWithValue("$match", match);
                updateParticipant.Parameters.AddWithValue("$player", playerIndex);
                await updateParticipant.ExecuteNonQueryAsync();
            }

            var snapshots = new List<(long Id, string Command, string State)>();
            var selectEvents = connection.CreateCommand();
            selectEvents.Transaction = transaction;
            selectEvents.CommandText = "SELECT id,command_json,state_json FROM match_events WHERE match_id=$match;";
            selectEvents.Parameters.AddWithValue("$match", match);
            await using (var reader = await selectEvents.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    snapshots.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            foreach (var recorded in snapshots)
            {
                var updateEvent = connection.CreateCommand();
                updateEvent.Transaction = transaction;
                updateEvent.CommandText = "UPDATE match_events SET command_json=$command,state_json=$state WHERE id=$id;";
                updateEvent.Parameters.AddWithValue("$command", ScrubJsonString(recorded.Command, playerName, anonymousName));
                updateEvent.Parameters.AddWithValue("$state", ScrubJsonString(recorded.State, playerName, anonymousName));
                updateEvent.Parameters.AddWithValue("$id", recorded.Id);
                await updateEvent.ExecuteNonQueryAsync();
            }
            var scrubInitial = connection.CreateCommand();
            scrubInitial.Transaction = transaction;
            scrubInitial.CommandText = "SELECT initial_state_json FROM matches WHERE match_id=$match;";
            scrubInitial.Parameters.AddWithValue("$match", match);
            var initial = await scrubInitial.ExecuteScalarAsync() as string;
            if (!string.IsNullOrWhiteSpace(initial))
            {
                var updateInitial = connection.CreateCommand();
                updateInitial.Transaction = transaction;
                updateInitial.CommandText = "UPDATE matches SET initial_state_json=$state WHERE match_id=$match;";
                updateInitial.Parameters.AddWithValue("$state", ScrubJsonString(initial, playerName, anonymousName));
                updateInitial.Parameters.AddWithValue("$match", match);
                await updateInitial.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();
        return targets.Select(target => target.MatchId).Distinct(StringComparer.Ordinal).Count();
    }
}
