using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal class L12PrivateDeckMutationConflictException(
    string status, long? currentRevision = null)
    : InvalidOperationException(status)
{
    internal string Status { get; } = status;
    internal long? CurrentRevision { get; } = currentRevision;
}

internal sealed class L12PrivateDeckStorageConflictException(long expectedRevision, long currentRevision)
    : L12PrivateDeckMutationConflictException("storage_conflict")
{
    internal long ExpectedStorageRevision { get; } = expectedRevision;
    internal long CurrentStorageRevision { get; } = currentRevision;
}

public sealed partial class L12PlatformStore
{
    private enum PrivateDeckMutationKind
    {
        Create,
        Update,
        Delete,
    }

    private sealed record PrivateDeckMutation(
        PrivateDeckMutationKind Kind,
        string AccountId,
        string DeckId,
        long? ExpectedRevision,
        DeckRow? Deck);

    private sealed record StoredPrivateDeck(string NameKey, long Revision);

    // Deliberately off until the isolated-copy comparisons, fault suite and candidate
    // benchmark have been accepted. This has no production/configuration surface yet.
    internal bool PrivateDeckObjectPersistenceEnabled { get; set; }

    private void SavePrivateDeckCreate(DeckRow deck)
    {
        EnsurePrivateDeckIdentity(deck);
        SavePrivateDeckMutation(new(PrivateDeckMutationKind.Create, deck.AccountId, deck.Id, null, deck));
    }

    private void SavePrivateDeckUpdate(DeckRow deck, long expectedRevision)
    {
        EnsurePrivateDeckIdentity(deck);
        SavePrivateDeckMutation(new(PrivateDeckMutationKind.Update, deck.AccountId, deck.Id,
            expectedRevision, deck));
    }

    private void SavePrivateDeckDelete(string accountId, string deckId, long expectedRevision)
        => SavePrivateDeckMutation(new(PrivateDeckMutationKind.Delete, accountId, deckId,
            expectedRevision, null));

    private void SavePrivateDeckMutation(PrivateDeckMutation mutation)
    {
        if (!PrivateDeckObjectPersistenceEnabled || _adminTransactionDepth > 0)
        {
            Save();
            return;
        }
        PersistTransactionalData(true, (connection, transaction) =>
            PersistPrivateDeckObject(connection, transaction, mutation), privateDeckObjectWrite: true);
    }

    private static void EnsurePrivateDeckIdentity(DeckRow deck)
    {
        if (string.IsNullOrWhiteSpace(deck.Id)) deck.Id = Guid.NewGuid().ToString("N");
        if (deck.Revision < 1) deck.Revision = 1;
    }

    private void PersistPrivateDeckObject(SqliteConnection connection, SqliteTransaction transaction,
        PrivateDeckMutation mutation)
    {
        if (string.IsNullOrWhiteSpace(mutation.AccountId) || string.IsNullOrWhiteSpace(mutation.DeckId)
            || mutation.Deck is not null && DeckNameKey(mutation.Deck.Name).Length == 0)
            throw new InvalidDataException("私人牌库对象写入包含空账号、ID 或名称");
        var stored = ReadStoredPrivateDeck(connection, transaction, mutation.AccountId, mutation.DeckId);
        switch (mutation.Kind)
        {
            case PrivateDeckMutationKind.Create:
                if (stored is not null) throw new L12PrivateDeckMutationConflictException("name_conflict");
                if (HasActivePrivateDeckName(connection, transaction, mutation.AccountId,
                        DeckNameKey(mutation.Deck!.Name)))
                    throw new L12PrivateDeckMutationConflictException("name_conflict");
                PersistPrivateDeckRow(connection, transaction, mutation.Deck, null);
                break;
            case PrivateDeckMutationKind.Update:
                RequireExpectedRevision(stored, mutation.ExpectedRevision);
                PersistPrivateDeckRow(connection, transaction, mutation.Deck!, stored!.NameKey);
                break;
            case PrivateDeckMutationKind.Delete:
                RequireExpectedRevision(stored, mutation.ExpectedRevision);
                StorageFailureInjector?.Invoke("before-private-deck-row");
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE account_decks SET is_deleted=1
                        WHERE account_id=$account AND deck_id=$id AND revision=$revision AND is_deleted=0;
                        """;
                    command.Parameters.AddWithValue("$account", mutation.AccountId);
                    command.Parameters.AddWithValue("$id", mutation.DeckId);
                    command.Parameters.AddWithValue("$revision", mutation.ExpectedRevision!.Value);
                    if (command.ExecuteNonQuery() != 1)
                        throw ReadPrivateDeckConflict(connection, transaction, mutation);
                }
                StorageFailureInjector?.Invoke("after-private-deck-row");
                VerifyDeletedPrivateDeck(connection, transaction, mutation);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation.Kind));
        }
    }

    private void PersistPrivateDeckRow(SqliteConnection connection, SqliteTransaction transaction,
        DeckRow deck, string? storedNameKey)
    {
        var nameKey = DeckNameKey(deck.Name);
        var payload = NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds);
        StorageFailureInjector?.Invoke("before-private-deck-payload");
        PersistPayload(connection, transaction, payload, deck.UpdatedAt);
        StorageFailureInjector?.Invoke("after-private-deck-payload");
        StorageFailureInjector?.Invoke("before-private-deck-row");

        if (storedNameKey is not null && !string.Equals(storedNameKey, nameKey, StringComparison.Ordinal))
        {
            if (HasActivePrivateDeckName(connection, transaction, deck.AccountId, nameKey))
                throw new L12PrivateDeckMutationConflictException("name_conflict");
            using var tombstone = connection.CreateCommand();
            tombstone.Transaction = transaction;
            tombstone.CommandText = """
                UPDATE account_decks SET is_deleted=1
                WHERE account_id=$account AND deck_id=$id AND revision=$expected AND is_deleted=0;
                """;
            tombstone.Parameters.AddWithValue("$account", deck.AccountId);
            tombstone.Parameters.AddWithValue("$id", deck.Id);
            tombstone.Parameters.AddWithValue("$expected", deck.Revision - 1);
            if (tombstone.ExecuteNonQuery() != 1)
                throw ReadPrivateDeckConflict(connection, transaction,
                    new(PrivateDeckMutationKind.Update, deck.AccountId, deck.Id, deck.Revision - 1, deck));
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO account_decks(account_id,name_key,name,deck_id,revision,payload_hash,
                    alternate_art_selections_json,alternate_art_copies_json,bench_cards_json,updated_utc,is_deleted,
                    publication_id,publication_version)
                VALUES($account,$key,$name,$id,$revision,$payload,$selections,$copies,$bench,$updated,0,$publication,$version)
                ON CONFLICT(account_id,name_key) DO UPDATE SET
                    name=excluded.name,deck_id=excluded.deck_id,revision=excluded.revision,
                    payload_hash=excluded.payload_hash,
                    alternate_art_selections_json=excluded.alternate_art_selections_json,
                    alternate_art_copies_json=excluded.alternate_art_copies_json,
                    bench_cards_json=excluded.bench_cards_json,updated_utc=excluded.updated_utc,
                    publication_id=excluded.publication_id,publication_version=excluded.publication_version,is_deleted=0
                WHERE account_decks.is_deleted=1
                   OR (account_decks.deck_id=excluded.deck_id AND account_decks.revision=excluded.revision-1);
                """;
            command.Parameters.AddWithValue("$account", deck.AccountId);
            command.Parameters.AddWithValue("$key", nameKey);
            command.Parameters.AddWithValue("$name", deck.Name);
            command.Parameters.AddWithValue("$id", deck.Id);
            command.Parameters.AddWithValue("$revision", deck.Revision);
            command.Parameters.AddWithValue("$payload", payload.Hash);
            command.Parameters.AddWithValue("$selections", JsonSerializer.Serialize(deck.AlternateArtSelections));
            command.Parameters.AddWithValue("$copies", JsonSerializer.Serialize(deck.AlternateArtCopies));
            command.Parameters.AddWithValue("$bench", CompactDeckCardsJson(deck.BenchIds));
            command.Parameters.AddWithValue("$updated", deck.UpdatedAt.ToString("O"));
            command.Parameters.AddWithValue("$publication", (object?)deck.PublicationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$version", (object?)deck.PublicationVersion ?? DBNull.Value);
            if (command.ExecuteNonQuery() != 1)
                throw ReadPrivateDeckConflict(connection, transaction,
                    new(storedNameKey is null ? PrivateDeckMutationKind.Create : PrivateDeckMutationKind.Update,
                        deck.AccountId, deck.Id, storedNameKey is null ? null : deck.Revision - 1, deck));
        }
        StorageFailureInjector?.Invoke("after-private-deck-row");
        VerifyPrivateDeckRow(connection, transaction, deck, payload);
    }

    private static StoredPrivateDeck? ReadStoredPrivateDeck(SqliteConnection connection,
        SqliteTransaction transaction, string accountId, string deckId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name_key,revision FROM account_decks
            WHERE account_id=$account AND deck_id=$id AND is_deleted=0;
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$id", deckId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetInt64(1)) : null;
    }

    private static bool HasActivePrivateDeckName(SqliteConnection connection, SqliteTransaction transaction,
        string accountId, string nameKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM account_decks
                WHERE account_id=$account AND name_key=$key COLLATE NOCASE AND is_deleted=0);
            """;
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$key", nameKey);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    private static void RequireExpectedRevision(StoredPrivateDeck? stored, long? expectedRevision)
    {
        if (stored is null) throw new L12PrivateDeckMutationConflictException("not_found");
        if (stored.Revision != expectedRevision)
            throw new L12PrivateDeckMutationConflictException("revision_conflict", stored.Revision);
    }

    private static L12PrivateDeckMutationConflictException ReadPrivateDeckConflict(SqliteConnection connection,
        SqliteTransaction transaction, PrivateDeckMutation mutation)
    {
        var current = ReadStoredPrivateDeck(connection, transaction, mutation.AccountId, mutation.DeckId);
        return current is null
            ? new("not_found")
            : new("revision_conflict", current.Revision);
    }

    private static void VerifyPrivateDeckRow(SqliteConnection connection, SqliteTransaction transaction,
        DeckRow deck, NormalizedDeckPayload payload)
    {
        using (var payloadCommand = connection.CreateCommand())
        {
            payloadCommand.Transaction = transaction;
            payloadCommand.CommandText = """
                SELECT master_id,main_cards_json,morale_cards_json,special_cards_json
                FROM deck_payloads WHERE payload_hash=$hash;
                """;
            payloadCommand.Parameters.AddWithValue("$hash", payload.Hash);
            using var payloadReader = payloadCommand.ExecuteReader();
            if (!payloadReader.Read()
                || payloadReader.GetString(0) != payload.MasterId
                || payloadReader.GetString(1) != payload.MainJson
                || payloadReader.GetString(2) != payload.MoraleJson
                || payloadReader.GetString(3) != payload.SpecialJson)
                throw new InvalidDataException("私人牌库对象写入正文读回校验失败");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name_key,name,revision,payload_hash,alternate_art_selections_json,
                   alternate_art_copies_json,bench_cards_json,updated_utc,publication_id,publication_version
            FROM account_decks
            WHERE account_id=$account AND deck_id=$id AND is_deleted=0;
            """;
        command.Parameters.AddWithValue("$account", deck.AccountId);
        command.Parameters.AddWithValue("$id", deck.Id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()
            || reader.GetString(0) != DeckNameKey(deck.Name)
            || reader.GetString(1) != deck.Name
            || reader.GetInt64(2) != deck.Revision
            || reader.GetString(3) != payload.Hash
            || reader.GetString(4) != JsonSerializer.Serialize(deck.AlternateArtSelections)
            || reader.GetString(5) != JsonSerializer.Serialize(deck.AlternateArtCopies)
            || reader.GetString(6) != CompactDeckCardsJson(deck.BenchIds)
            || reader.GetString(7) != deck.UpdatedAt.ToString("O")
            || ReadNullableString(reader, 8) != deck.PublicationId
            || ReadNullableInt(reader, 9) != deck.PublicationVersion
            || reader.Read())
            throw new InvalidDataException("私人牌库对象写入引用读回校验失败");
    }

    private static void VerifyDeletedPrivateDeck(SqliteConnection connection, SqliteTransaction transaction,
        PrivateDeckMutation mutation)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM account_decks
            WHERE account_id=$account AND deck_id=$id AND is_deleted=0;
            """;
        command.Parameters.AddWithValue("$account", mutation.AccountId);
        command.Parameters.AddWithValue("$id", mutation.DeckId);
        if (Convert.ToInt32(command.ExecuteScalar()) != 0)
            throw new InvalidDataException("私人牌库对象删除读回校验失败");
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? ReadNullableInt(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static long ReadPrivateDeckStorageRevision(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT storage_revision FROM platform_state WHERE singleton_id=1;";
        var value = command.ExecuteScalar();
        if (value is null || value is DBNull)
            throw new InvalidDataException("私人牌库对象写入缺少平台状态");
        return Convert.ToInt64(value);
    }

    private static void UpsertPrivateDeckSnapshotCas(SqliteConnection connection, SqliteTransaction transaction,
        string snapshotJson, string snapshotChecksum, string mirrorChecksum, DataFile data,
        long expectedStorageRevision)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE platform_state SET
                    schema_version=$schema,
                    storage_revision=$revision,
                    business_version=$business,
                    snapshot_json=$json,
                    snapshot_sha256=$checksum,
                    updated_utc=$updated
                WHERE singleton_id=1 AND storage_revision=$expected;
                """;
            command.Parameters.AddWithValue("$schema", PlatformStorageSchemaVersion);
            command.Parameters.AddWithValue("$revision", data.Version);
            command.Parameters.AddWithValue("$business", data.BusinessVersion ?? data.Version);
            command.Parameters.AddWithValue("$json", snapshotJson);
            command.Parameters.AddWithValue("$checksum", snapshotChecksum);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$expected", expectedStorageRevision);
            if (command.ExecuteNonQuery() != 1)
                throw new L12PrivateDeckStorageConflictException(expectedStorageRevision,
                    ReadPrivateDeckStorageRevision(connection, transaction));
        }

        using var meta = connection.CreateCommand();
        meta.Transaction = transaction;
        meta.CommandText = """
            INSERT INTO storage_meta(key,value) VALUES('fallback_json_sha256',$mirror)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        meta.Parameters.AddWithValue("$mirror", mirrorChecksum);
        meta.ExecuteNonQuery();
    }

}
