using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private const string DeckDomainStateKey = "deck_domain_state";
    private const string DeckDomainActiveState = "active-v1";

    private sealed record DeckCardCount(string CardId, int Quantity);
    private sealed record NormalizedDeckPayload(string Hash, string MasterId, string MainJson,
        string MoraleJson, string SpecialJson, IReadOnlyList<string> MainCards,
        IReadOnlyList<string> MoraleCards, IReadOnlyList<string> SpecialCards);

    private static void InitializeDeckDomainSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS deck_payloads (
                payload_hash TEXT PRIMARY KEY,
                master_id TEXT NOT NULL,
                main_cards_json TEXT NOT NULL,
                morale_cards_json TEXT NOT NULL,
                special_cards_json TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS account_decks (
                account_id TEXT NOT NULL,
                name_key TEXT NOT NULL,
                name TEXT NOT NULL,
                payload_hash TEXT NOT NULL REFERENCES deck_payloads(payload_hash),
                alternate_art_selections_json TEXT NOT NULL,
                alternate_art_copies_json TEXT NOT NULL,
                bench_cards_json TEXT NOT NULL DEFAULT '[]',
                updated_utc TEXT NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0 CHECK(is_deleted IN (0,1)),
                PRIMARY KEY(account_id,name_key)
            );
            CREATE INDEX IF NOT EXISTS ix_account_decks_updated
                ON account_decks(account_id,updated_utc DESC);
            CREATE TABLE IF NOT EXISTS published_decks (
                publication_id TEXT PRIMARY KEY,
                public_code TEXT,
                owner_id TEXT NOT NULL,
                name TEXT NOT NULL,
                current_version INTEGER NOT NULL,
                current_payload_hash TEXT NOT NULL REFERENCES deck_payloads(payload_hash),
                alternate_art_selections_json TEXT NOT NULL DEFAULT '{}',
                alternate_art_copies_json TEXT NOT NULL DEFAULT '{}',
                views INTEGER NOT NULL DEFAULT 0 CHECK(views >= 0),
                copies INTEGER NOT NULL DEFAULT 0 CHECK(copies >= 0),
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0 CHECK(is_deleted IN (0,1))
            );
            CREATE INDEX IF NOT EXISTS ix_published_decks_updated ON published_decks(updated_utc DESC);
            CREATE TABLE IF NOT EXISTS published_deck_versions (
                publication_id TEXT NOT NULL REFERENCES published_decks(publication_id),
                version INTEGER NOT NULL,
                payload_hash TEXT NOT NULL REFERENCES deck_payloads(payload_hash),
                name TEXT NOT NULL DEFAULT '',
                created_utc TEXT NOT NULL,
                PRIMARY KEY(publication_id,version)
            );
            CREATE INDEX IF NOT EXISTS ix_published_deck_versions_payload
                ON published_deck_versions(payload_hash);
            CREATE TABLE IF NOT EXISTS published_deck_likes (
                publication_id TEXT NOT NULL REFERENCES published_decks(publication_id),
                account_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(publication_id,account_id)
            );
            CREATE TABLE IF NOT EXISTS tournament_deck_refs (
                tournament_id TEXT NOT NULL,
                account_id TEXT NOT NULL,
                payload_hash TEXT NOT NULL REFERENCES deck_payloads(payload_hash),
                submitted_utc TEXT NOT NULL,
                locked_utc TEXT,
                PRIMARY KEY(tournament_id,account_id)
            );
            CREATE INDEX IF NOT EXISTS ix_tournament_deck_refs_payload
                ON tournament_deck_refs(payload_hash);
            CREATE TABLE IF NOT EXISTS published_deck_content_payloads (
                content_hash TEXT PRIMARY KEY,
                guide_json TEXT NOT NULL,
                matchups_json TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS published_deck_content_heads (
                publication_id TEXT PRIMARY KEY REFERENCES published_decks(publication_id),
                revision INTEGER NOT NULL,
                content_hash TEXT NOT NULL REFERENCES published_deck_content_payloads(content_hash),
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS published_deck_content_revisions (
                publication_id TEXT NOT NULL REFERENCES published_decks(publication_id),
                revision INTEGER NOT NULL,
                content_hash TEXT NOT NULL REFERENCES published_deck_content_payloads(content_hash),
                author_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(publication_id,revision)
            );
            CREATE INDEX IF NOT EXISTS ix_published_deck_content_revisions_hash
                ON published_deck_content_revisions(content_hash);
            """;
        command.ExecuteNonQuery();
        EnsureDeckColumn(connection, "account_decks", "bench_cards_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureDeckColumn(connection, "account_decks", "publication_id", "TEXT");
        EnsureDeckColumn(connection, "account_decks", "publication_version", "INTEGER");
        EnsureDeckColumn(connection, "published_decks", "public_code", "TEXT");
        EnsureDeckColumn(connection, "published_decks", "alternate_art_selections_json", "TEXT NOT NULL DEFAULT '{}'");
        EnsureDeckColumn(connection, "published_decks", "alternate_art_copies_json", "TEXT NOT NULL DEFAULT '{}'");
        EnsureDeckColumn(connection, "published_deck_versions", "name", "TEXT NOT NULL DEFAULT ''");
        BackfillPublicDeckCodes(connection);
        using (var publicCodeIndex = connection.CreateCommand())
        {
            publicCodeIndex.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS ux_published_decks_public_code
                    ON published_decks(public_code COLLATE NOCASE) WHERE public_code IS NOT NULL;
                """;
            publicCodeIndex.ExecuteNonQuery();
        }
        using var backfill = connection.CreateCommand();
        backfill.CommandText = """
            UPDATE published_deck_versions
            SET name=COALESCE((SELECT name FROM published_decks
                WHERE published_decks.publication_id=published_deck_versions.publication_id),'')
            WHERE name='';
            """;
        backfill.ExecuteNonQuery();
    }

    private static void EnsureDeckColumn(SqliteConnection connection, string table, string column, string declaration)
    {
        using var query = connection.CreateCommand();
        query.CommandText = $"PRAGMA table_info({table});";
        using var reader = query.ExecuteReader();
        while (reader.Read()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        alter.ExecuteNonQuery();
    }

    private static void BackfillPublicDeckCodes(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT publication_id,public_code FROM published_decks;";
            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1))) missing.Add(reader.GetString(0));
                else if (!existing.Add(reader.GetString(1))) throw new InvalidDataException("公开牌库短码重复");
            }
        }
        foreach (var publicationId in missing)
        {
            var code = CreateUniquePublicDeckCode(existing);
            existing.Add(code);
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE published_decks SET public_code=$code WHERE publication_id=$id;";
            update.Parameters.AddWithValue("$code", code);
            update.Parameters.AddWithValue("$id", publicationId);
            update.ExecuteNonQuery();
        }
    }

    private void EnsureAndHydrateDeckDomainStorage(SqliteConnection connection, DataFile data)
    {
        var state = ReadMeta(connection, DeckDomainStateKey);
        if (!string.Equals(state, DeckDomainActiveState, StringComparison.Ordinal))
        {
            WriteDeckMigrationBackup(data);
            string mirrorJson;
            string snapshotJson;
            using var transaction = connection.BeginTransaction();
            PersistDeckDomainSnapshot(connection, transaction, data);
            VerifyDeckDomainSnapshot(connection, transaction, data);
            SetStorageMeta(connection, transaction, DeckDomainStateKey, DeckDomainActiveState);
            snapshotJson = SerializeSnapshot(data);
            mirrorJson = JsonSerializer.Serialize(data, PlatformMirrorJsonOptions);
            UpsertSnapshot(connection, transaction, snapshotJson, Sha256(snapshotJson), Sha256(mirrorJson), data);
            transaction.Commit();
            _lastCommittedSnapshot = snapshotJson;
            try
            {
                WriteFallbackMirror(mirrorJson);
                _fallbackMirrorHealthy = true;
            }
            catch (Exception error)
            {
                _fallbackMirrorHealthy = false;
                _storageIssue = $"牌库存储迁移已提交，但 JSON 兼容镜像更新失败：{error.Message}";
            }
        }
        else if (HasLegacyDeckPayloads(data))
        {
            using var transaction = connection.BeginTransaction();
            PersistDeckDomainSnapshot(connection, transaction, data);
            transaction.Commit();
        }

        HydrateDeckDomain(connection, data);
    }

    private static bool HasLegacyDeckPayloads(DataFile data)
        => data.Decks.Count > 0 || data.PublishedDecks.Count > 0
           || data.Tournaments.Any(tournament => tournament.Participants.Any(participant =>
               participant.Deck.CardIds.Count > 0 || participant.Deck.MoraleIds.Count > 0
               || participant.Deck.SpecialIds.Count > 0));

    private void WriteDeckMigrationBackup(DataFile data)
    {
        if (!HasLegacyDeckPayloads(data)) return;
        var path = _databasePath + ".pre-deck-domain-v1.json.gz";
        _migrationBackupPath = path;
        if (File.Exists(path)) return;
        var json = JsonSerializer.Serialize(data, PlatformMigrationJsonOptions);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
        writer.Write(json);
    }

    private static void SetStorageMeta(SqliteConnection connection, SqliteTransaction transaction,
        string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO storage_meta(key,value) VALUES($key,$value)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<DeckCardCount> NormalizeDeckCardCounts(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim().ToUpperInvariant())
        .GroupBy(value => value, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new DeckCardCount(group.Key, group.Count()))
        .ToArray();

    private static string CompactDeckCardsJson(IEnumerable<string> values)
        => JsonSerializer.Serialize(NormalizeDeckCardCounts(values));

    private static NormalizedDeckPayload NormalizeDeckPayload(string masterId, IEnumerable<string> cardIds,
        IEnumerable<string> moraleIds, IEnumerable<string> specialIds)
    {
        static string CardsJson(IReadOnlyList<DeckCardCount> cards) => JsonSerializer.Serialize(cards);
        static IReadOnlyList<string> Expand(IReadOnlyList<DeckCardCount> cards) => cards
            .SelectMany(card => Enumerable.Repeat(card.CardId, card.Quantity)).ToArray();

        var normalizedMaster = (masterId ?? string.Empty).Trim().ToUpperInvariant();
        var main = NormalizeDeckCardCounts(cardIds);
        var morale = NormalizeDeckCardCounts(moraleIds);
        var special = NormalizeDeckCardCounts(specialIds);
        var mainJson = CardsJson(main);
        var moraleJson = CardsJson(morale);
        var specialJson = CardsJson(special);
        var canonical = JsonSerializer.Serialize(new
        {
            schema = 1,
            master = normalizedMaster,
            main,
            morale,
            special,
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(hash, normalizedMaster, mainJson, moraleJson, specialJson,
            Expand(main), Expand(morale), Expand(special));
    }

    private static void PersistPayload(SqliteConnection connection, SqliteTransaction transaction,
        NormalizedDeckPayload payload, DateTimeOffset createdAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO deck_payloads(
                payload_hash,master_id,main_cards_json,morale_cards_json,special_cards_json,created_utc)
            VALUES($hash,$master,$main,$morale,$special,$created);
            """;
        command.Parameters.AddWithValue("$hash", payload.Hash);
        command.Parameters.AddWithValue("$master", payload.MasterId);
        command.Parameters.AddWithValue("$main", payload.MainJson);
        command.Parameters.AddWithValue("$morale", payload.MoraleJson);
        command.Parameters.AddWithValue("$special", payload.SpecialJson);
        command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void PersistDeckDomainSnapshot(SqliteConnection connection, SqliteTransaction transaction,
        DataFile data)
    {
        DeleteStaleDeckDomainRows(connection, transaction, data);
        foreach (var deck in data.Decks)
        {
            var payload = NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds);
            PersistPayload(connection, transaction, payload, deck.UpdatedAt);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO account_decks(account_id,name_key,name,payload_hash,
                    alternate_art_selections_json,alternate_art_copies_json,bench_cards_json,updated_utc,is_deleted,
                    publication_id,publication_version)
                VALUES($account,$key,$name,$payload,$selections,$copies,$bench,$updated,0,$publication,$version)
                ON CONFLICT(account_id,name_key) DO UPDATE SET
                    name=excluded.name,payload_hash=excluded.payload_hash,
                    alternate_art_selections_json=excluded.alternate_art_selections_json,
                    alternate_art_copies_json=excluded.alternate_art_copies_json,
                    bench_cards_json=excluded.bench_cards_json,updated_utc=excluded.updated_utc,
                    publication_id=excluded.publication_id,publication_version=excluded.publication_version,is_deleted=0;
                """;
            command.Parameters.AddWithValue("$account", deck.AccountId);
            command.Parameters.AddWithValue("$key", DeckNameKey(deck.Name));
            command.Parameters.AddWithValue("$name", deck.Name);
            command.Parameters.AddWithValue("$payload", payload.Hash);
            command.Parameters.AddWithValue("$selections", JsonSerializer.Serialize(deck.AlternateArtSelections));
            command.Parameters.AddWithValue("$copies", JsonSerializer.Serialize(deck.AlternateArtCopies));
            command.Parameters.AddWithValue("$bench", CompactDeckCardsJson(deck.BenchIds));
            command.Parameters.AddWithValue("$publication", (object?)deck.PublicationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$version", (object?)deck.PublicationVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated", deck.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }

        foreach (var deck in data.PublishedDecks)
        {
            var payload = NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds);
            PersistPayload(connection, transaction, payload, deck.CreatedAt);
            var version = CurrentPublishedDeckVersion(connection, transaction, deck.Id, payload.Hash, deck.Name);
            deck.Version = version;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO published_decks(publication_id,public_code,owner_id,name,current_version,current_payload_hash,
                        alternate_art_selections_json,alternate_art_copies_json,views,copies,created_utc,updated_utc,is_deleted)
                    VALUES($id,$code,$owner,$name,$version,$payload,$selections,$artCopies,$views,$copies,$created,$updated,0)
                    ON CONFLICT(publication_id) DO UPDATE SET
                        public_code=excluded.public_code,owner_id=excluded.owner_id,name=excluded.name,current_version=excluded.current_version,
                        current_payload_hash=excluded.current_payload_hash,
                        alternate_art_selections_json=excluded.alternate_art_selections_json,
                        alternate_art_copies_json=excluded.alternate_art_copies_json,
                        views=excluded.views,copies=excluded.copies,
                        updated_utc=excluded.updated_utc,is_deleted=0;
                    """;
                command.Parameters.AddWithValue("$id", deck.Id);
                command.Parameters.AddWithValue("$code", deck.PublicCode);
                command.Parameters.AddWithValue("$owner", deck.OwnerId);
                command.Parameters.AddWithValue("$name", deck.Name);
                command.Parameters.AddWithValue("$version", version);
                command.Parameters.AddWithValue("$payload", payload.Hash);
                command.Parameters.AddWithValue("$selections", JsonSerializer.Serialize(deck.AlternateArtSelections));
                command.Parameters.AddWithValue("$artCopies", JsonSerializer.Serialize(deck.AlternateArtCopies));
                command.Parameters.AddWithValue("$views", Math.Max(0, deck.Views));
                command.Parameters.AddWithValue("$copies", Math.Max(0, deck.Copies));
                command.Parameters.AddWithValue("$created", deck.CreatedAt.ToString("O"));
                command.Parameters.AddWithValue("$updated", deck.UpdatedAt.ToString("O"));
                command.ExecuteNonQuery();
            }
            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = """
                    INSERT OR IGNORE INTO published_deck_versions(publication_id,version,payload_hash,name,created_utc)
                    VALUES($id,$version,$payload,$name,$created);
                    """;
                versionCommand.Parameters.AddWithValue("$id", deck.Id);
                versionCommand.Parameters.AddWithValue("$version", version);
                versionCommand.Parameters.AddWithValue("$payload", payload.Hash);
                versionCommand.Parameters.AddWithValue("$name", deck.Name);
                versionCommand.Parameters.AddWithValue("$created", deck.UpdatedAt.ToString("O"));
                versionCommand.ExecuteNonQuery();
            }
            foreach (var accountId in deck.LikedByAccountIds.Distinct(StringComparer.Ordinal))
            {
                using var likeCommand = connection.CreateCommand();
                likeCommand.Transaction = transaction;
                likeCommand.CommandText = """
                    INSERT OR IGNORE INTO published_deck_likes(publication_id,account_id,created_utc)
                    VALUES($id,$account,$created);
                    """;
                likeCommand.Parameters.AddWithValue("$id", deck.Id);
                likeCommand.Parameters.AddWithValue("$account", accountId);
                likeCommand.Parameters.AddWithValue("$created", deck.UpdatedAt.ToString("O"));
                likeCommand.ExecuteNonQuery();
            }
        }

        foreach (var tournament in data.Tournaments)
        foreach (var participant in tournament.Participants)
        {
            var deck = participant.Deck;
            if (string.IsNullOrWhiteSpace(deck.MasterId) && deck.CardIds.Count == 0
                && deck.MoraleIds.Count == 0 && deck.SpecialIds.Count == 0) continue;
            var payload = NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds);
            deck.PayloadHash = payload.Hash;
            PersistPayload(connection, transaction, payload, deck.SubmittedAt);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tournament_deck_refs(tournament_id,account_id,payload_hash,submitted_utc,locked_utc)
                VALUES($tournament,$account,$payload,$submitted,$locked)
                ON CONFLICT(tournament_id,account_id) DO UPDATE SET
                    payload_hash=excluded.payload_hash,submitted_utc=excluded.submitted_utc,locked_utc=excluded.locked_utc;
                """;
            command.Parameters.AddWithValue("$tournament", tournament.Id);
            command.Parameters.AddWithValue("$account", participant.AccountId);
            command.Parameters.AddWithValue("$payload", payload.Hash);
            command.Parameters.AddWithValue("$submitted", deck.SubmittedAt.ToString("O"));
            command.Parameters.AddWithValue("$locked", deck.LockedAt is null ? DBNull.Value : deck.LockedAt.Value.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static void DeleteStaleDeckDomainRows(SqliteConnection connection, SqliteTransaction transaction,
        DataFile data)
    {
        var accountDecks = data.Decks.Select(deck => $"{deck.AccountId}\n{DeckNameKey(deck.Name)}")
            .ToHashSet(StringComparer.Ordinal);
        var storedAccountDecks = ReadStoredKeys(connection, transaction,
            "SELECT account_id || char(10) || name_key FROM account_decks;");
        foreach (var key in storedAccountDecks.Where(key => !accountDecks.Contains(key)))
        {
            var parts = key.Split('\n', 2);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE account_decks SET is_deleted=1 WHERE account_id=$account AND name_key=$key;";
            command.Parameters.AddWithValue("$account", parts[0]);
            command.Parameters.AddWithValue("$key", parts[1]);
            command.ExecuteNonQuery();
        }

        var publications = data.PublishedDecks.Select(deck => deck.Id).ToHashSet(StringComparer.Ordinal);
        var storedPublications = ReadStoredKeys(connection, transaction,
            "SELECT publication_id FROM published_decks;");
        foreach (var id in storedPublications.Where(id => !publications.Contains(id)))
        {
            ExecuteDeckDelete(connection, transaction,
                "UPDATE published_decks SET is_deleted=1 WHERE publication_id=$id;", id, null);
        }

        var likes = data.PublishedDecks.SelectMany(deck => deck.LikedByAccountIds
                .Select(accountId => $"{deck.Id}\n{accountId}"))
            .ToHashSet(StringComparer.Ordinal);
        var storedLikes = ReadStoredKeys(connection, transaction, """
            SELECT likes.publication_id || char(10) || likes.account_id
            FROM published_deck_likes likes
            JOIN published_decks decks ON decks.publication_id=likes.publication_id
            WHERE decks.is_deleted=0;
            """);
        foreach (var key in storedLikes.Where(key => !likes.Contains(key)))
        {
            var parts = key.Split('\n', 2);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM published_deck_likes WHERE publication_id=$id AND account_id=$account;";
            command.Parameters.AddWithValue("$id", parts[0]);
            command.Parameters.AddWithValue("$account", parts[1]);
            command.ExecuteNonQuery();
        }
        foreach (var accountId in data.Accounts.Where(account => account.Deleted).Select(account => account.Id))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM published_deck_likes WHERE account_id=$account;";
            command.Parameters.AddWithValue("$account", accountId);
            command.ExecuteNonQuery();
        }
    }

    private static HashSet<string> ReadStoredKeys(SqliteConnection connection, SqliteTransaction transaction,
        string sql)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static void VerifyDeckDomainSnapshot(SqliteConnection connection, SqliteTransaction transaction,
        DataFile data)
    {
        static Dictionary<string, string> ReadReferences(SqliteConnection connection,
            SqliteTransaction transaction, string sql)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
            return result;
        }

        var expectedAccounts = data.Decks.ToDictionary(
            deck => $"{deck.AccountId}\n{DeckNameKey(deck.Name)}",
            deck => NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds).Hash,
            StringComparer.Ordinal);
        var storedAccounts = ReadReferences(connection, transaction,
            "SELECT account_id || char(10) || name_key,payload_hash FROM account_decks WHERE is_deleted=0;");
        if (storedAccounts.Count != expectedAccounts.Count)
            throw new InvalidDataException("账号牌库规范化引用数量校验失败");
        foreach (var pair in expectedAccounts)
            if (!storedAccounts.TryGetValue(pair.Key, out var hash) || hash != pair.Value)
                throw new InvalidDataException("账号牌库规范化哈希校验失败");

        var expectedPublished = data.PublishedDecks.ToDictionary(deck => deck.Id,
            deck => NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds).Hash,
            StringComparer.Ordinal);
        var storedPublished = ReadReferences(connection, transaction,
            "SELECT publication_id,current_payload_hash FROM published_decks WHERE is_deleted=0;");
        if (storedPublished.Count != expectedPublished.Count)
            throw new InvalidDataException("公开牌库规范化引用数量校验失败");
        foreach (var pair in expectedPublished)
            if (!storedPublished.TryGetValue(pair.Key, out var hash) || hash != pair.Value)
                throw new InvalidDataException("公开牌库规范化哈希校验失败");

        var expectedTournaments = data.Tournaments.SelectMany(tournament => tournament.Participants
                .Where(participant => !string.IsNullOrWhiteSpace(participant.Deck.MasterId)
                    || participant.Deck.CardIds.Count > 0 || participant.Deck.MoraleIds.Count > 0
                    || participant.Deck.SpecialIds.Count > 0)
                .Select(participant => new
                {
                    Key = $"{tournament.Id}\n{participant.AccountId}",
                    Hash = NormalizeDeckPayload(participant.Deck.MasterId, participant.Deck.CardIds,
                        participant.Deck.MoraleIds, participant.Deck.SpecialIds).Hash,
                }))
            .ToDictionary(item => item.Key, item => item.Hash, StringComparer.Ordinal);
        var storedTournaments = ReadReferences(connection, transaction,
            "SELECT tournament_id || char(10) || account_id,payload_hash FROM tournament_deck_refs;");
        if (storedTournaments.Count != expectedTournaments.Count)
            throw new InvalidDataException("赛事牌库规范化引用数量校验失败");
        foreach (var pair in expectedTournaments)
            if (!storedTournaments.TryGetValue(pair.Key, out var hash) || hash != pair.Value)
                throw new InvalidDataException("赛事牌库规范化哈希校验失败");
    }

    private static int CurrentPublishedDeckVersion(SqliteConnection connection, SqliteTransaction transaction,
        string publicationId, string payloadHash, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT current_version,current_payload_hash,name FROM published_decks WHERE publication_id=$id;
            """;
        command.Parameters.AddWithValue("$id", publicationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return 1;
        var current = reader.GetInt32(0);
        return string.Equals(reader.GetString(1), payloadHash, StringComparison.Ordinal)
               && string.Equals(reader.GetString(2), name, StringComparison.Ordinal)
            ? current : current + 1;
    }

    private static string DeckNameKey(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static void HydrateDeckDomain(SqliteConnection connection, DataFile data)
    {
        var payloads = ReadPayloads(connection);
        data.Decks = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT account_id,name,payload_hash,alternate_art_selections_json,
                       alternate_art_copies_json,bench_cards_json,updated_utc,publication_id,publication_version
                FROM account_decks WHERE is_deleted=0 ORDER BY updated_utc DESC;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!payloads.TryGetValue(reader.GetString(2), out var payload))
                    throw new InvalidDataException("账号牌库引用了不存在的构筑正文");
                data.Decks.Add(new DeckRow
                {
                    AccountId = reader.GetString(0), Name = reader.GetString(1), MasterId = payload.MasterId,
                    CardIds = payload.MainCards.ToList(), MoraleIds = payload.MoraleCards.ToList(),
                    SpecialIds = payload.SpecialCards.ToList(),
                    AlternateArtSelections = DeserializeDictionary(reader.GetString(3)),
                    AlternateArtCopies = DeserializeDictionaryOfLists(reader.GetString(4)),
                    BenchIds = ExpandCards(reader.GetString(5)).ToList(),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                    PublicationId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    PublicationVersion = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                });
            }
        }

        var likes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT publication_id,account_id FROM published_deck_likes;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                if (!likes.TryGetValue(id, out var accounts)) likes[id] = accounts = [];
                accounts.Add(reader.GetString(1));
            }
        }
        data.PublishedDecks = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT publication_id,public_code,owner_id,name,current_payload_hash,
                       alternate_art_selections_json,alternate_art_copies_json,
                       views,copies,created_utc,updated_utc,current_version
                FROM published_decks WHERE is_deleted=0 ORDER BY updated_utc DESC;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!payloads.TryGetValue(reader.GetString(4), out var payload))
                    throw new InvalidDataException("公开牌库引用了不存在的构筑正文");
                data.PublishedDecks.Add(new PublishedDeckRow
                {
                    Id = reader.GetString(0), PublicCode = reader.GetString(1), OwnerId = reader.GetString(2), Name = reader.GetString(3),
                    MasterId = payload.MasterId, CardIds = payload.MainCards.ToList(),
                    MoraleIds = payload.MoraleCards.ToList(), SpecialIds = payload.SpecialCards.ToList(),
                    AlternateArtSelections = DeserializeDictionary(reader.GetString(5)),
                    AlternateArtCopies = DeserializeDictionaryOfLists(reader.GetString(6)),
                    Views = reader.GetInt32(7), Copies = reader.GetInt32(8),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(10)),
                    Version = reader.GetInt32(11),
                    LikedByAccountIds = likes.GetValueOrDefault(reader.GetString(0)) ?? [],
                });
            }
        }

        var tournamentRefs = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT tournament_id,account_id,payload_hash FROM tournament_deck_refs;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) tournamentRefs[$"{reader.GetString(0)}\n{reader.GetString(1)}"] = reader.GetString(2);
        }
        foreach (var tournament in data.Tournaments)
        foreach (var participant in tournament.Participants)
        {
            if (!tournamentRefs.TryGetValue($"{tournament.Id}\n{participant.AccountId}", out var payloadHash))
                payloadHash = participant.Deck.PayloadHash;
            if (string.IsNullOrWhiteSpace(payloadHash)) continue;
            if (!payloads.TryGetValue(payloadHash, out var payload))
                throw new InvalidDataException("赛事牌库引用了不存在的构筑正文");
            participant.Deck.PayloadHash = payloadHash;
            participant.Deck.MasterId = payload.MasterId;
            participant.Deck.CardIds = payload.MainCards.ToList();
            participant.Deck.MoraleIds = payload.MoraleCards.ToList();
            participant.Deck.SpecialIds = payload.SpecialCards.ToList();
        }
    }

    private static Dictionary<string, NormalizedDeckPayload> ReadPayloads(SqliteConnection connection)
    {
        var result = new Dictionary<string, NormalizedDeckPayload>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_hash,master_id,main_cards_json,morale_cards_json,special_cards_json FROM deck_payloads;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var main = ExpandCards(reader.GetString(2));
            var morale = ExpandCards(reader.GetString(3));
            var special = ExpandCards(reader.GetString(4));
            result[reader.GetString(0)] = new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), main, morale, special);
        }
        return result;
    }

    private L12DeckStorageStatusView ReadDeckStorageStatus()
    {
        using var connection = OpenDatabase(_databasePath, readOnly: true);
        static long Count(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }
        return new L12DeckStorageStatusView(
            Count(connection, "SELECT COUNT(*) FROM deck_payloads;"),
            Count(connection, "SELECT COUNT(*) FROM account_decks WHERE is_deleted=0;"),
            Count(connection, "SELECT COUNT(*) FROM account_decks WHERE is_deleted=1;"),
            Count(connection, "SELECT COUNT(*) FROM published_decks WHERE is_deleted=0;"),
            Count(connection, "SELECT COUNT(*) FROM published_deck_versions;"),
            Count(connection, "SELECT COUNT(*) FROM published_deck_likes;"),
            Count(connection, "SELECT COUNT(*) FROM tournament_deck_refs;"),
            Count(connection, "SELECT COUNT(*) FROM published_deck_content_payloads;"),
            Count(connection, "SELECT COUNT(*) FROM published_deck_content_revisions;"),
            File.Exists(_databasePath) ? new FileInfo(_databasePath).Length : 0,
            File.Exists(_databasePath + "-wal") ? new FileInfo(_databasePath + "-wal").Length : 0);
    }

    private static IReadOnlyList<string> ExpandCards(string json) =>
        (JsonSerializer.Deserialize<List<DeckCardCount>>(json) ?? [])
        .SelectMany(card => Enumerable.Repeat(card.CardId, Math.Max(0, card.Quantity))).ToArray();

    private static Dictionary<string, string> DeserializeDictionary(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json)
           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, List<string>> DeserializeDictionaryOfLists(string json)
        => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
           ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    private static void ExecuteDeckDelete(SqliteConnection connection, SqliteTransaction transaction,
        string sql, string id, string? accountId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        if (accountId is not null) command.Parameters.AddWithValue("$account", accountId);
        command.ExecuteNonQuery();
    }

    private bool ToggleStoredPublishedDeckLike(string accountId, string publicationId)
    {
        using var connection = OpenDatabase(_databasePath, readOnly: false);
        using var transaction = connection.BeginTransaction();
        bool exists;
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT EXISTS(SELECT 1 FROM published_deck_likes likes JOIN published_decks decks ON decks.publication_id=likes.publication_id WHERE likes.publication_id=$id AND likes.account_id=$account AND decks.is_deleted=0);";
            query.Parameters.AddWithValue("$id", publicationId);
            query.Parameters.AddWithValue("$account", accountId);
            exists = Convert.ToInt32(query.ExecuteScalar()) != 0;
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = exists
                ? "DELETE FROM published_deck_likes WHERE publication_id=$id AND account_id=$account;"
                : "INSERT INTO published_deck_likes(publication_id,account_id,created_utc) VALUES($id,$account,$created);";
            command.Parameters.AddWithValue("$id", publicationId);
            command.Parameters.AddWithValue("$account", accountId);
            if (!exists) command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return !exists;
    }

    private int IncrementStoredPublishedDeckCounter(string publicationId, string column)
    {
        if (column is not ("views" or "copies")) throw new ArgumentOutOfRangeException(nameof(column));
        using var connection = OpenDatabase(_databasePath, readOnly: false);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"UPDATE published_decks SET {column}=CASE WHEN {column}<2147483647 THEN {column}+1 ELSE {column} END WHERE publication_id=$id AND is_deleted=0;";
            command.Parameters.AddWithValue("$id", publicationId);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("公开牌库不存在");
        }
        int value;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"SELECT {column} FROM published_decks WHERE publication_id=$id;";
            command.Parameters.AddWithValue("$id", publicationId);
            value = Convert.ToInt32(command.ExecuteScalar());
        }
        transaction.Commit();
        return value;
    }
}
