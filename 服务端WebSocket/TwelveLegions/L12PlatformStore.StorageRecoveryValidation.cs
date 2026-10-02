using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Online rehearsal budgets, not deck-construction/storage limits. Oversized
    // valid stores require an explicitly resourced offline recovery rehearsal.
    private const long RecoveryCompressedRowByteLimit = 1_048_576;
    private const long RecoveryExpandedRowCardLimit = 100_000;
    private const long RecoveryExpandedTotalCardLimit = 1_000_000;
    // This validates only the isolated backup. It does not initialize a schema,
    // import a mirror, repair a row, or change the active storage mode.
    private static void ValidateRecoveredDeckDomain(SqliteConnection connection,
        SqliteTransaction transaction, DataFile data)
    {
        if (data.Version != ReadStorageRevision(connection, transaction))
            throw new InvalidDataException("恢复副本平台版本不一致");
        using (var business = connection.CreateCommand())
        {
            business.Transaction = transaction;
            business.CommandText = "SELECT business_version FROM platform_state WHERE singleton_id=1;";
            if (Convert.ToInt64(business.ExecuteScalar()) != (data.BusinessVersion ?? data.Version))
                throw new InvalidDataException("恢复副本平台业务版本不一致");
        }

        using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.Transaction = transaction;
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var reader = foreignKeys.ExecuteReader();
            if (reader.Read()) throw new InvalidDataException("恢复副本存在失效的规范化引用");
        }

        // Retained/tombstoned and historical-publication payloads matter too: they
        // need not appear in the current API projection, but must remain recoverable.
        var expansionCards = 0L;
        var payloadCardCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var payloads = connection.CreateCommand())
        {
            payloads.Transaction = transaction;
            payloads.CommandText = """
                SELECT payload_hash,master_id,main_cards_json,morale_cards_json,special_cards_json,
                       length(CAST(main_cards_json AS BLOB)),length(CAST(morale_cards_json AS BLOB)),
                       length(CAST(special_cards_json AS BLOB)) FROM deck_payloads;
                """;
            using var reader = payloads.ExecuteReader();
            while (reader.Read())
            {
                if (checked(reader.GetInt64(5) + reader.GetInt64(6) + reader.GetInt64(7)) > RecoveryCompressedRowByteLimit)
                    throw new InvalidDataException("恢复演练安全预算超限：压缩正文过大");
                var master = reader.GetString(1);
                var main = ReadRecoveryCardCounts(reader.GetString(2));
                var morale = ReadRecoveryCardCounts(reader.GetString(3));
                var special = ReadRecoveryCardCounts(reader.GetString(4));
                // Check compressed counts before hydration expands them. Corruption
                // such as int.MaxValue quantity must not allocate billions of cards.
                var canonical = JsonSerializer.Serialize(new { schema = 1, master, main, morale, special });
                if (master != master.Trim().ToUpperInvariant()
                    || !FixedEquals(reader.GetString(0), Sha256(canonical)))
                    throw new InvalidDataException("恢复副本构筑正文哈希或规范格式不一致");
                var cards = checked(CountRecoveryCards(main) + CountRecoveryCards(morale) + CountRecoveryCards(special));
                AddRecoveryExpansionBudget(ref expansionCards, cards);
                payloadCardCounts.Add(reader.GetString(0), cards);
            }
        }

        using (var benches = connection.CreateCommand())
        {
            benches.Transaction = transaction;
            benches.CommandText = "SELECT bench_cards_json,length(CAST(bench_cards_json AS BLOB)) FROM account_decks WHERE is_deleted=0;";
            using var reader = benches.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt64(1) > RecoveryCompressedRowByteLimit)
                    throw new InvalidDataException("恢复演练安全预算超限：压缩备选区过大");
                AddRecoveryExpansionBudget(ref expansionCards, CountRecoveryCards(ReadRecoveryCardCounts(reader.GetString(0))));
            }
        }

        // Hydration duplicates a shared payload into each active consumer. Budget
        // those copies, not just the number of unique content-addressed payloads.
        using (var consumers = connection.CreateCommand())
        {
            consumers.Transaction = transaction;
            consumers.CommandText = """
                SELECT payload_hash FROM account_decks WHERE is_deleted=0
                UNION ALL SELECT current_payload_hash FROM published_decks WHERE is_deleted=0
                UNION ALL SELECT payload_hash FROM tournament_deck_refs;
                """;
            using var reader = consumers.ExecuteReader();
            while (reader.Read()) AddRecoveryExpansionBudget(ref expansionCards, payloadCardCounts[reader.GetString(0)]);
        }

        using (var identities = connection.CreateCommand())
        {
            identities.Transaction = transaction;
            identities.CommandText = """
                SELECT 1 FROM account_decks
                WHERE is_deleted=0 AND (deck_id IS NULL OR trim(deck_id)='' OR revision<1)
                UNION ALL
                SELECT 1 FROM account_decks WHERE is_deleted=0
                GROUP BY account_id,deck_id HAVING COUNT(*)<>1;
                """;
            if (identities.ExecuteScalar() is not null)
                throw new InvalidDataException("恢复副本活动牌库身份无效或重复");
        }

        using (var versions = connection.CreateCommand())
        {
            versions.Transaction = transaction;
            versions.CommandText = """
                SELECT 1 FROM published_decks d
                LEFT JOIN published_deck_versions v
                  ON v.publication_id=d.publication_id AND v.version=d.current_version
                WHERE v.publication_id IS NULL OR v.payload_hash<>d.current_payload_hash
                LIMIT 1;
                """;
            if (versions.ExecuteScalar() is not null)
                throw new InvalidDataException("恢复副本公开牌库当前版本引用不一致");
        }

        using (var sources = connection.CreateCommand())
        {
            sources.Transaction = transaction;
            sources.CommandText = """
                SELECT 1 FROM account_decks d
                LEFT JOIN published_deck_versions v
                  ON v.publication_id=d.publication_id AND v.version=d.publication_version
                WHERE (d.publication_id IS NULL AND d.publication_version IS NOT NULL)
                   OR (d.publication_id IS NOT NULL AND
                       (d.publication_version IS NULL OR v.publication_id IS NULL OR v.payload_hash<>d.payload_hash))
                LIMIT 1;
                """;
            if (sources.ExecuteScalar() is not null)
                throw new InvalidDataException("恢复副本私人牌库公开来源版本不一致");
        }

        // Hydration must not disguise a changed tournament reference by overwriting
        // the authoritative payload hash retained in the compact platform snapshot.
        var expectedTournamentRefs = data.Tournaments.SelectMany(tournament => tournament.Participants
                .Where(participant => !string.IsNullOrWhiteSpace(participant.Deck.PayloadHash))
                .Select(participant => new
                {
                    Key = $"{tournament.Id}\n{participant.AccountId}",
                    Hash = participant.Deck.PayloadHash,
                }))
            .ToDictionary(row => row.Key, row => row.Hash, StringComparer.Ordinal);
        using (var references = connection.CreateCommand())
        {
            references.Transaction = transaction;
            references.CommandText = "SELECT tournament_id,account_id,payload_hash FROM tournament_deck_refs;";
            using var reader = references.ExecuteReader();
            while (reader.Read())
            {
                var key = $"{reader.GetString(0)}\n{reader.GetString(1)}";
                if (!expectedTournamentRefs.Remove(key, out var expected)
                    || !FixedEquals(expected, reader.GetString(2)))
                    throw new InvalidDataException("恢复副本赛事牌库引用与平台快照不一致");
            }
        }
        if (expectedTournamentRefs.Count != 0)
            throw new InvalidDataException("恢复副本缺少赛事牌库引用");

        HydrateDeckDomain(connection, data, transaction);
        VerifyDeckDomainSnapshot(connection, transaction, data);
    }

    private static List<DeckCardCount> ReadRecoveryCardCounts(string json)
    {
        var cards = JsonSerializer.Deserialize<List<DeckCardCount>>(json)
            ?? throw new InvalidDataException("恢复副本构筑正文为空");
        if (cards.Any(card => card is null || string.IsNullOrWhiteSpace(card.CardId) || card.Quantity < 1
                || card.CardId != card.CardId.Trim().ToUpperInvariant())
            || cards.Select(card => card.CardId).Distinct(StringComparer.Ordinal).Count() != cards.Count
            || !cards.Select(card => card.CardId).SequenceEqual(cards.Select(card => card.CardId).Order(StringComparer.Ordinal))
            || JsonSerializer.Serialize(cards) != json)
            throw new InvalidDataException("恢复副本构筑正文计数格式无效");
        return cards;
    }

    private static long CountRecoveryCards(IEnumerable<DeckCardCount> cards)
    {
        var total = 0L;
        foreach (var card in cards) total = checked(total + card.Quantity);
        return total;
    }

    private static void AddRecoveryExpansionBudget(ref long total, long cards)
    {
        if (cards > RecoveryExpandedRowCardLimit || cards > RecoveryExpandedTotalCardLimit - total)
            throw new InvalidDataException("恢复演练安全预算超限：牌库展开副本过多");
        total = checked(total + cards);
    }
}
