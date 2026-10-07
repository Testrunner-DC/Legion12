using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PrivateDeckRecoveryAndRollbackTests
{
    private sealed record Fixture(string Path, L12PlatformStore Store, L12Catalog Catalog,
        L12AccountView Owner, L12AccountDeckView Active, string PublicationId, string TournamentId);

    [Fact]
    public void RecoveryRehearsalRejectsDeckPayloadTamperingDespiteValidPlatformChecksum()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var registered = store.Register("tf2recover", "password-123");
            Assert.True(registered.Success, registered.Message);
            var owner = registered.Account!;
            store.PrivateDeckObjectPersistenceEnabled = true;
            Assert.True(store.CreateDeck(owner.Id, Deck("saved", "C1")).Success);
            using (var connection = Open(store.TransactionalStoragePath))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE deck_payloads SET master_id='TAMPERED';";
                command.ExecuteNonQuery();
            }

            var result = store.RehearseStorageRecovery();
            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("active-payload")]
    [InlineData("retained-payload")]
    [InlineData("extreme-count")]
    [InlineData("extreme-count-validhash")]
    [InlineData("extreme-bench-count")]
    [InlineData("expanded-aggregate")]
    [InlineData("compressed-body")]
    [InlineData("active-identity")]
    [InlineData("missing-payload")]
    [InlineData("published-head")]
    [InlineData("published-source")]
    [InlineData("tournament-reference")]
    [InlineData("storage-revision")]
    [InlineData("business-version")]
    public void DamagedIsolatedCopyFailsClosedWithoutRepairingSourceOrCopy(string damage)
    {
        var root = TempRoot();
        try
        {
            var fixture = CreateFixture(Path.Combine(root, "source", "platform.json"));
            var sourceBefore = Projection(fixture.Store.TransactionalStoragePath);
            var sourceMirrorBefore = SHA256.HashData(File.ReadAllBytes(fixture.Path));
            var copyPath = Path.Combine(root, "damaged", "platform.json");
            CopyStore(fixture.Path, copyPath);
            var copy = new L12PlatformStore(copyPath, officialCards: fixture.Catalog.Cards);
            using (var connection = Open(copy.TransactionalStoragePath))
            {
                Execute(connection, "PRAGMA foreign_keys=OFF;");
                if (damage is "extreme-count" or "extreme-count-validhash")
                    CorruptQuantityWithMatchingHash(connection,
                        damage == "extreme-count" ? "CORRUPT" : "RETAINED");
                else if (damage == "expanded-aggregate") CorruptAggregateBenchCopies(connection);
                else Execute(connection, damage switch
                {
                    "active-payload" => "UPDATE deck_payloads SET master_id='TAMPERED' WHERE payload_hash IN (SELECT payload_hash FROM account_decks WHERE is_deleted=0);",
                    "retained-payload" => "UPDATE deck_payloads SET master_id='TAMPERED' WHERE master_id='M1';",
                    "extreme-bench-count" => "UPDATE account_decks SET bench_cards_json='[{\"CardId\":\"CORRUPT\",\"Quantity\":2147483647}]' WHERE is_deleted=0;",
                    "compressed-body" => "UPDATE deck_payloads SET payload_json=CAST(zeroblob(1048577) AS TEXT) WHERE master_id='M1';",
                    "active-identity" => "UPDATE account_decks SET deck_id=NULL WHERE is_deleted=0;",
                    "missing-payload" => "DELETE FROM deck_payloads WHERE master_id='M1';",
                    "published-head" => "UPDATE published_decks SET current_version=999;",
                    "published-source" => "UPDATE account_decks SET publication_version=999 WHERE publication_id IS NOT NULL;",
                    "tournament-reference" => "UPDATE tournament_deck_refs SET payload_hash=(SELECT payload_hash FROM deck_payloads WHERE master_id='M1' LIMIT 1);",
                    "business-version" => "UPDATE platform_state SET business_version=business_version+1;",
                    _ => "UPDATE platform_state SET storage_revision=storage_revision+1;",
                });
                Assert.Equal("ok", Scalar(connection, "PRAGMA quick_check;"));
            }
            var damagedBefore = Projection(copy.TransactionalStoragePath);
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(copyPath));

            var result = copy.RehearseStorageRecovery();
            Assert.False(result.Success);
            Assert.NotNull(result.Error);
            if (damage is "extreme-count-validhash" or "extreme-bench-count" or "expanded-aggregate" or "compressed-body")
                Assert.Contains("安全预算超限", result.Error);
            Assert.Equal(damagedBefore, Projection(copy.TransactionalStoragePath));
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(copyPath)));
            Assert.Equal(sourceBefore, Projection(fixture.Store.TransactionalStoragePath));
            Assert.Equal(sourceMirrorBefore, SHA256.HashData(File.ReadAllBytes(fixture.Path)));
            AssertNoRehearsalFiles(root);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void ObjectFactsRecoverOnBackupThenDefaultOffFullSavePreservesHistoryAndReferences()
    {
        var root = TempRoot();
        try
        {
            var fixture = CreateFixture(Path.Combine(root, "source", "platform.json"));
            var before = Projection(fixture.Store.TransactionalStoragePath);
            var rehearsal = fixture.Store.RehearseStorageRecovery();
            Assert.True(rehearsal.Success, rehearsal.Error);
            Assert.Equal(fixture.Store.StorageStatus().StorageRevision, rehearsal.StorageRevision);
            Assert.Equal(before, Projection(fixture.Store.TransactionalStoragePath));
            AssertNoRehearsalFiles(root);

            var recoveredPath = Path.Combine(root, "recovered", "platform.json");
            CopyStore(fixture.Path, recoveredPath);
            var recovered = new L12PlatformStore(recoveredPath, officialCards: fixture.Catalog.Cards);
            Assert.False(recovered.PrivateDeckObjectPersistenceEnabled);
            Assert.Equal(before, Projection(recovered.TransactionalStoragePath));
            var active = Assert.Single(recovered.Decks(fixture.Owner.Id));
            Assert.Equal(fixture.Active.Id, active.Id);
            Assert.Equal(fixture.Active.Revision, active.Revision);
            Assert.Equal(fixture.PublicationId, active.PublicationId);
            Assert.Equal(1, active.PublicationVersion);
            var lockedBefore = SnapshotSignature(fixture.Store.Tournament(fixture.Owner, fixture.TournamentId)!
                .Participants.Single(row => row.AccountId == fixture.Owner.Id).Deck);
            Assert.Equal(lockedBefore, SnapshotSignature(recovered.Tournament(fixture.Owner, fixture.TournamentId)!
                .Participants.Single(row => row.AccountId == fixture.Owner.Id).Deck));

            var historyBefore = HistoryProjection(recovered.TransactionalStoragePath);
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                Assert.True(recovered.Register("tf2after", "password-123").Success);
                var measurement = scope.Complete();
                Assert.True(measurement.DeckDomainStatements > 5);
            }
            Assert.Equal(historyBefore, HistoryProjection(recovered.TransactionalStoragePath));
            Assert.Equal(before, Projection(fixture.Store.TransactionalStoragePath));
            Assert.Equal(lockedBefore, SnapshotSignature(recovered.Tournament(fixture.Owner, fixture.TournamentId)!
                .Participants.Single(row => row.AccountId == fixture.Owner.Id).Deck));
            var restarted = new L12PlatformStore(recoveredPath, officialCards: fixture.Catalog.Cards);
            Assert.False(restarted.PrivateDeckObjectPersistenceEnabled);
            Assert.Equal(active.Id, Assert.Single(restarted.Decks(fixture.Owner.Id)).Id);
            Assert.True(restarted.RehearseStorageRecovery().Success);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public void CommittedObjectResponseLostThenRestartAndRetryDoesNotDuplicateFacts(string operation)
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var registered = store.Register("tf2lost", "password-123");
            Assert.True(registered.Success, registered.Message);
            var owner = registered.Account!;
            var original = operation == "create" ? null : store.CreateDeck(owner.Id, Deck("saved", "C1")).Deck!;
            var committed = operation switch
            {
                "create" => store.CreateDeck(owner.Id, Deck("saved", "C2")),
                "update" => store.UpdateDeck(owner.Id, original!.Id, original.Revision, Deck("saved", "C2")),
                _ => store.DeleteDeck(owner.Id, original!.Id, original.Revision),
            };
            Assert.True(committed.Success); // Deliberately discard the successful response.
            var before = Projection(store.TransactionalStoragePath);
            var restarted = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var retry = operation switch
            {
                "create" => restarted.CreateDeck(owner.Id, Deck("saved", "C2")),
                "update" => restarted.UpdateDeck(owner.Id, original!.Id, original.Revision, Deck("saved", "C2")),
                _ => restarted.DeleteDeck(owner.Id, original!.Id, original.Revision),
            };
            Assert.Equal(operation == "create" ? "name_conflict" : operation == "update" ? "revision_conflict" : "not_found", retry.Status);
            Assert.Equal(before, Projection(restarted.TransactionalStoragePath));
            if (operation == "delete") Assert.Empty(restarted.Decks(owner.Id));
            else
            {
                var saved = Assert.Single(restarted.Decks(owner.Id));
                Assert.Equal("C2", Assert.Single(saved.CardIds));
                Assert.Equal(operation == "create" ? 1 : 2, saved.Revision);
                Assert.Equal(operation == "create" ? committed.Deck!.Id : original!.Id, saved.Id);
            }
            Assert.True(restarted.RehearseStorageRecovery().Success);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static Fixture CreateFixture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(path, officialCards: catalog.Cards)
            { PrivateDeckObjectPersistenceEnabled = true };
        var registered = store.Register("tf2fixture", "password-123");
        Assert.True(registered.Success, registered.Message);
        var owner = registered.Account!;
        var first = catalog.PresetDecks[0];
        var published = store.PublishDeck(owner.Id, CopyDeck(first, "published"), null)!;
        var saved = store.CreateDeck(owner.Id, CopyDeck(first, "before rename", published.Id, 1)).Deck!;
        var tournament = store.CreateTournament(owner,
            new L12TournamentCreatePayload("F2恢复赛", "swiss", "public", 8, DateTimeOffset.UtcNow.AddHours(1),
                "S01/S02", "storage", "after", "season", string.Empty, 50, 5,
                RegistrationVisibility: "public", LateGraceMinutes: 5), Context("create"), true);
        tournament = store.PreCheckInTournament(owner, tournament.Id,
            new L12TournamentPreCheckInPayload("ignored", string.Empty, saved.Id), tournament.Version, Context("lock"), true);
        var renamed = store.UpdateDeck(owner.Id, saved.Id, saved.Revision,
            CopyDeck(first, "after rename", published.Id, 1));
        Assert.True(renamed.Success);
        var deleted = store.CreateDeck(owner.Id, Deck("deleted", "RETAINED")).Deck!;
        Assert.True(store.DeleteDeck(owner.Id, deleted.Id, deleted.Revision).Success);
        Assert.NotNull(store.PublishDeck(owner.Id,
            CopyDeck(catalog.PresetDecks[Math.Min(1, catalog.PresetDecks.Count - 1)], "published version2"), published.Id));
        using var connection = Open(store.TransactionalStoragePath);
        Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM account_decks WHERE is_deleted=0;"));
        Assert.Equal("2", Scalar(connection, "SELECT COUNT(*) FROM account_decks WHERE is_deleted=1;"));
        Assert.Equal("2", Scalar(connection, "SELECT COUNT(*) FROM published_deck_versions;"));
        Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM tournament_deck_refs;"));
        return new(path, store, catalog, owner, renamed.Deck!, published.Id, tournament.Id);
    }

    private static L12PresetDeckDefinition CopyDeck(L12PresetDeckDefinition source, string name,
        string? publicationId = null, int? publicationVersion = null) => new()
    {
        Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds], MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds], PublicationId = publicationId, PublicationVersion = publicationVersion,
    };

    private static L12AdminAuditContext Context(string id) => new("f2-recovery-" + id);

    private static string SnapshotSignature(L12TournamentDeckSnapshotView? deck)
    {
        Assert.NotNull(deck);
        return JsonSerializer.Serialize(new
        {
            deck.Name, deck.Hash, deck.MasterId, deck.Code,
            Main = deck.CardIds.Order(StringComparer.Ordinal).ToArray(),
            Morale = deck.MoraleIds.Order(StringComparer.Ordinal).ToArray(),
            Special = deck.SpecialIds.Order(StringComparer.Ordinal).ToArray(), deck.SubmittedAt, deck.LockedAt,
        });
    }

    private static void CopyStore(string sourcePath, string copyPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        using (var source = Open(Path.ChangeExtension(sourcePath, ".db"), readOnly: true))
        using (var destination = Open(Path.ChangeExtension(copyPath, ".db"))) source.BackupDatabase(destination);
        File.Copy(sourcePath, copyPath);
    }

    private static string[] Projection(string databasePath) => ReadTables(databasePath,
        ["platform_state", "storage_meta", "deck_payloads", "account_decks", "published_decks", "published_deck_versions",
         "published_deck_likes", "tournament_deck_refs", "admin_audit_events"]);

    private static string[] HistoryProjection(string databasePath) => ReadTables(databasePath,
        ["deck_payloads", "account_decks", "published_decks", "published_deck_versions", "published_deck_likes", "tournament_deck_refs"]);

    private static string[] ReadTables(string databasePath, string[] tables)
    {
        using var connection = Open(databasePath, readOnly: true);
        var rows = new List<string>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM " + table + ";"; // Fixed test-owned table names only.
            using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(table + ":" + JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount)
                .Select(index => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index))).ToArray()));
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AssertNoRehearsalFiles(string root) => Assert.Empty(Directory.EnumerateFiles(root,
        "*.rehearsal-*", SearchOption.AllDirectories));

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void CorruptQuantityWithMatchingHash(SqliteConnection connection, string cardId)
    {
        var main = new[] { new { CardId = cardId, Quantity = int.MaxValue } };
        var morale = new[] { new { CardId = "R1", Quantity = 1 } };
        var special = Array.Empty<object>();
        var canonical = JsonSerializer.Serialize(new { schema = 1, master = "M1", main, morale, special });
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var oldHash = Scalar(connection, "SELECT payload_hash FROM deck_payloads WHERE master_id='M1';");
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE deck_payloads SET payload_hash=$new,payload_json=$payload WHERE payload_hash=$old;
            UPDATE account_decks SET payload_hash=$new WHERE payload_hash=$old;
            """;
        command.Parameters.AddWithValue("$new", hash);
        command.Parameters.AddWithValue("$old", oldHash);
        command.Parameters.AddWithValue("$payload",
            $"[[[\"{cardId}\",{int.MaxValue}]],[[\"R1\",1]],[]]");
        command.ExecuteNonQuery();
    }

    private static void CorruptAggregateBenchCopies(SqliteConnection connection)
    {
        Execute(connection, "UPDATE account_decks SET bench_cards_json='[{\"CardId\":\"CORRUPT\",\"Quantity\":60000}]' WHERE is_deleted=0;");
        for (var index = 0; index < 17; index++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO account_decks(account_id,name_key,name,deck_id,revision,payload_hash,
                    alternate_art_selections_json,alternate_art_copies_json,bench_cards_json,updated_utc,
                    is_deleted,publication_id,publication_version)
                SELECT account_id,$key,$name,$id,revision,payload_hash,alternate_art_selections_json,
                    alternate_art_copies_json,bench_cards_json,updated_utc,0,publication_id,publication_version
                FROM account_decks WHERE is_deleted=0 LIMIT 1;
                """;
            command.Parameters.AddWithValue("$key", "BUDGET-" + index);
            command.Parameters.AddWithValue("$name", "budget-" + index);
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.ExecuteNonQuery();
        }
    }

    private static L12PresetDeckDefinition Deck(string name, string card) => new()
        { Name = name, MasterId = "M1", CardIds = [card], MoraleIds = ["R1"] };

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false,
              Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate }.ToString());
        connection.Open();
        return connection;
    }

    private static string TempRoot()
    {
        var root = Path.Combine(@"D:\GPT\Legion12\artifacts\f2-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
