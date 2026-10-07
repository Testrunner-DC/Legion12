using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CompactDeckPayloadMigrationTests
{
    [Fact]
    public void SchemaSevenPayloadsMigrateWithoutChangingReferencesHistoryTombstonesOrMetadata()
    {
        var root = TempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "source", "platform.json");
            var fixture = CreateRichStore(sourcePath);
            var copyPath = Path.Combine(root, "copy", "platform.json");
            CopyStore(sourcePath, copyPath);
            L12PlatformStore.RevertCompactDeckPayloadStorageForRehearsal(
                Path.ChangeExtension(copyPath, ".db"));

            var before = ReferenceProjection(Path.ChangeExtension(copyPath, ".db"));
            using (var legacy = Open(Path.ChangeExtension(copyPath, ".db")))
            {
                Assert.Equal("7", Scalar(legacy, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("7", Scalar(legacy, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal(new[] { "main_cards_json", "morale_cards_json", "special_cards_json" },
                    PayloadColumns(legacy).Where(name => name.EndsWith("_cards_json", StringComparison.Ordinal)).ToArray());
                Assert.DoesNotContain("payload_json", PayloadColumns(legacy));
            }

            SqliteConnection.ClearAllPools();
            var migrated = new L12PlatformStore(copyPath);
            Assert.Equal("schema8-bound", Assert.Single(migrated.Decks(fixture.OwnerId)
                .Where(deck => deck.Name == "schema8-bound")).Name);
            using var current = Open(migrated.TransactionalStoragePath);
            Assert.Equal("8", Scalar(current, "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal("8", Scalar(current, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal("compact-v1", Scalar(current,
                "SELECT value FROM storage_meta WHERE key='deck_payload_format_state';"));
            Assert.Contains("payload_format", PayloadColumns(current));
            Assert.Contains("payload_json", PayloadColumns(current));
            Assert.DoesNotContain("main_cards_json", PayloadColumns(current));
            Assert.Equal("25", Scalar(current,
                $"SELECT COUNT(*) FROM published_deck_versions WHERE publication_id='{fixture.PublicationId}';"));
            Assert.Equal("1", Scalar(current,
                "SELECT COUNT(*) FROM account_decks WHERE is_deleted=1 AND name='schema8-deleted';"));
            Assert.Equal("1", Scalar(current, "SELECT COUNT(*) FROM tournament_deck_refs;"));
            Assert.Equal("1", Scalar(current,
                $"SELECT COUNT(*) FROM deck_payloads WHERE payload_hash='{fixture.OrphanHash}';"));
            Assert.Equal(fixture.PublicationId + "|1", Scalar(current, """
                SELECT publication_id || '|' || publication_version FROM account_decks
                WHERE is_deleted=0 AND name='schema8-bound';
                """));
            Assert.Equal("{\"C00\":\"ALT-A\"}|{\"C00\":[\"ALT-A\",\"\"]}", Scalar(current, """
                SELECT alternate_art_selections_json || '|' || alternate_art_copies_json
                FROM account_decks WHERE is_deleted=0 AND name='schema8-bound';
                """));
            Assert.Contains("\"Quantity\":2", Scalar(current, """
                SELECT bench_cards_json FROM account_decks WHERE is_deleted=0 AND name='schema8-bound';
                """), StringComparison.Ordinal);
            Assert.Equal(before, ReferenceProjection(migrated.TransactionalStoragePath));
            Assert.All(ReadPayloadRows(current), row =>
            {
                Assert.Equal("1", row.Format);
                Assert.DoesNotContain("CardId", row.Body, StringComparison.Ordinal);
                Assert.StartsWith("[[[", row.Body, StringComparison.Ordinal);
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ReverseExitUsesLatestCommittedPayloadsRatherThanAnOldBackup()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var owner = store.Register("texitlatest", "password-123").Account!;
            var first = store.CreateDeck(owner.Id, Deck("before", "C1")).Deck!;
            var published = store.PublishDeck(owner.Id, Deck("published", "C1"), null)!;
            Assert.True(store.UpdateDeck(owner.Id, first.Id, first.Revision, Deck("after", "C2")).Success);
            var deleted = store.CreateDeck(owner.Id, Deck("deleted-latest", "C3")).Deck!;
            Assert.True(store.DeleteDeck(owner.Id, deleted.Id, deleted.Revision).Success);
            published = store.PublishDeck(owner.Id, Deck("published-v2", "C4"), published.Id)!;

            var copyPath = Path.Combine(root, "exit-copy.db");
            CopyDatabase(store.TransactionalStoragePath, copyPath);
            var compactHashes = PayloadHashes(copyPath);
            L12PlatformStore.RevertCompactDeckPayloadStorageForRehearsal(copyPath);

            using var legacy = Open(copyPath);
            Assert.Equal("7", Scalar(legacy, "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal("2", Scalar(legacy,
                $"SELECT COUNT(*) FROM published_deck_versions WHERE publication_id='{published.Id}';"));
            Assert.Equal("1", Scalar(legacy,
                "SELECT COUNT(*) FROM account_decks WHERE is_deleted=1 AND name='deleted-latest';"));
            Assert.Equal(compactHashes, PayloadHashes(copyPath));
            Assert.All(new[] { "main_cards_json", "morale_cards_json", "special_cards_json" },
                column => Assert.Contains(column, PayloadColumns(legacy)));
            Assert.DoesNotContain("payload_json", PayloadColumns(legacy));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InvalidSecondLegacyPayloadRollsBackAllSchemaAndReferenceChanges()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var owner = store.Register("tbadsecond", "password-123").Account!;
            Assert.True(store.CreateDeck(owner.Id, Deck("first", "C1")).Success);
            Assert.True(store.CreateDeck(owner.Id, Deck("second", "C2")).Success);
            var database = store.TransactionalStoragePath;
            L12PlatformStore.RevertCompactDeckPayloadStorageForRehearsal(database);
            using (var corrupt = Open(database))
                Execute(corrupt, """
                    UPDATE deck_payloads SET main_cards_json='[]'
                    WHERE payload_hash=(SELECT payload_hash FROM deck_payloads ORDER BY payload_hash LIMIT 1 OFFSET 1);
                    """);
            var references = ReferenceProjection(database);
            SqliteConnection.ClearAllPools();

            Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(path));
            using var preserved = Open(database);
            Assert.Equal("7", Scalar(preserved, "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal("7", Scalar(preserved, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.DoesNotContain("payload_json", PayloadColumns(preserved));
            Assert.Contains("main_cards_json", PayloadColumns(preserved));
            Assert.Equal(references, ReferenceProjection(database));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("unknown-format")]
    public void UnknownSchemaOrPayloadFormatFailsClosedWithoutMutation(string damage)
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var owner = store.Register("tunknownfmt", "password-123").Account!;
            Assert.True(store.CreateDeck(owner.Id, Deck("stored", "C1")).Success);
            using (var connection = Open(store.TransactionalStoragePath))
            {
                Execute(connection, damage == "future-schema"
                    ? "UPDATE storage_meta SET value='9' WHERE key='schema_version'; UPDATE platform_state SET schema_version=9 WHERE singleton_id=1;"
                    : "UPDATE deck_payloads SET payload_format=99;");
            }
            var before = FullDatabaseProjection(store.TransactionalStoragePath);
            SqliteConnection.ClearAllPools();

            Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(path));
            Assert.Equal(before, FullDatabaseProjection(store.TransactionalStoragePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static (string OwnerId, string PublicationId, string OrphanHash) CreateRichStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
        var owner = store.Register("tmigrateall", "password-123").Account!;
        L12PublishedDeckView? published = null;
        for (var index = 0; index < 25; index++)
            published = store.PublishDeck(owner.Id, Deck("public-" + index, $"C{index:D2}"), published?.Id);
        Assert.NotNull(published);
        var bound = new L12PresetDeckDefinition
        {
            Name = "schema8-bound",
            MasterId = "M1",
            CardIds = ["C00"],
            MoraleIds = ["R1"],
            SpecialIds = [],
            PublicationId = published!.Id,
            PublicationVersion = 1,
            BenchIds = ["BENCH", "BENCH"],
        };
        Assert.True(store.CreateDeck(owner.Id, bound).Success);
        var deleted = store.CreateDeck(owner.Id, Deck("schema8-deleted", "ORPHAN")).Deck!;
        Assert.True(store.DeleteDeck(owner.Id, deleted.Id, deleted.Revision).Success);
        var orphan = store.CreateDeck(owner.Id, Deck("schema8-orphan", "UNREFERENCED")).Deck!;
        Assert.True(store.DeleteDeck(owner.Id, orphan.Id, orphan.Revision).Success);
        string orphanHash;
        using (var connection = Open(store.TransactionalStoragePath))
        {
            orphanHash = Scalar(connection,
                "SELECT payload_hash FROM account_decks WHERE is_deleted=1 AND name='schema8-orphan';");
            Execute(connection, "DELETE FROM account_decks WHERE is_deleted=1 AND name='schema8-orphan';");
            var tournamentHash = Scalar(connection,
                "SELECT payload_hash FROM account_decks WHERE is_deleted=0 AND name='schema8-bound';");
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO tournament_deck_refs(tournament_id,account_id,payload_hash,submitted_utc,locked_utc)
                VALUES('synthetic-tournament',$account,$payload,$submitted,$locked);
                """;
            insert.Parameters.AddWithValue("$account", owner.Id);
            insert.Parameters.AddWithValue("$payload", tournamentHash);
            insert.Parameters.AddWithValue("$submitted", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
            insert.Parameters.AddWithValue("$locked", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
            Execute(connection, """
                UPDATE account_decks SET
                    alternate_art_selections_json='{"C00":"ALT-A"}',
                    alternate_art_copies_json='{"C00":["ALT-A",""]}'
                WHERE name='schema8-bound';
                """);
        }
        return (owner.Id, published.Id, orphanHash);
    }

    private static L12PresetDeckDefinition Deck(string name, string card) => new()
    {
        Name = name,
        MasterId = "M1",
        CardIds = [card],
        MoraleIds = ["R1"],
        SpecialIds = [],
    };

    private static void CopyStore(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        CopyDatabase(Path.ChangeExtension(sourcePath, ".db"), Path.ChangeExtension(destinationPath, ".db"));
        File.Copy(sourcePath, destinationPath);
    }

    private static void CopyDatabase(string sourcePath, string destinationPath)
    {
        using var source = Open(sourcePath, readOnly: true);
        using var destination = Open(destinationPath);
        source.BackupDatabase(destination);
    }

    private static string[] ReferenceProjection(string databasePath) => ReadTables(databasePath,
        ["account_decks", "published_decks", "published_deck_versions", "tournament_deck_refs"]);

    private static string[] FullDatabaseProjection(string databasePath) => ReadTables(databasePath,
        ["platform_state", "storage_meta", "deck_payloads", "account_decks", "published_decks",
         "published_deck_versions", "tournament_deck_refs"]);

    private static string[] ReadTables(string databasePath, string[] tables)
    {
        using var connection = Open(databasePath, readOnly: true);
        var result = new List<string>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM " + table + ";"; // Fixed test-owned table names only.
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(table + ":" + JsonSerializer.Serialize(
                Enumerable.Range(0, reader.FieldCount)
                    .Select(index => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index))).ToArray()));
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static string[] PayloadHashes(string databasePath)
    {
        using var connection = Open(databasePath, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_hash FROM deck_payloads ORDER BY payload_hash;";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result.ToArray();
    }

    private static (string Format, string Body)[] ReadPayloadRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_format,payload_json FROM deck_payloads ORDER BY payload_hash;";
        using var reader = command.ExecuteReader();
        var result = new List<(string, string)>();
        while (reader.Read()) result.Add((Convert.ToString(reader.GetInt32(0))!, reader.GetString(1)));
        return result.ToArray();
    }

    private static string[] PayloadColumns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(deck_payloads);";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(1));
        return result.ToArray();
    }

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

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-compact-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
