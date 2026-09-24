using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class DeckDomainStorageTests
{
    [Fact]
    public void LegacyDeckPayloadsMigrateToNormalizedTablesWithOneCompressedBackup()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var legacy = JsonSerializer.Serialize(new
            {
                Version = 7L,
                BusinessVersion = 7L,
                Decks = new[]
                {
                    new
                    {
                        AccountId = "account-a", Name = "旧个人牌库", MasterId = "m1",
                        CardIds = new[] { "c2", "c1", "c1" }, MoraleIds = new[] { "r1" },
                        SpecialIds = Array.Empty<string>(), AlternateArtSelections = new Dictionary<string, string>(),
                        AlternateArtCopies = new Dictionary<string, string[]>(), UpdatedAt = now,
                    },
                },
                PublishedDecks = new[]
                {
                    new
                    {
                        Id = "published-a", OwnerId = "account-a", Name = "旧公开牌库", MasterId = "M1",
                        CardIds = new[] { "C1", "C1", "C2" }, MoraleIds = new[] { "R1" },
                        SpecialIds = Array.Empty<string>(), LikedByAccountIds = new[] { "reader-a" },
                        Views = 4, Copies = 2, CreatedAt = now, UpdatedAt = now,
                    },
                },
            });
            File.WriteAllText(path, legacy);

            var store = new L12PlatformStore(path);
            var storageStatus = store.StorageStatus();
            var backup = storageStatus.MigrationBackupPath;
            Assert.NotNull(backup);
            Assert.EndsWith(".json.gz", backup, StringComparison.Ordinal);
            Assert.True(File.Exists(backup));
            Assert.Single(Directory.EnumerateFiles(root, "*.pre-deck-domain-v1.json.gz"));
            Assert.NotNull(storageStatus.DeckStorage);
            Assert.Equal(1, storageStatus.DeckStorage!.Payloads);
            Assert.Equal(1, storageStatus.DeckStorage.ActiveAccountDecks);
            Assert.Equal(1, storageStatus.DeckStorage.ActivePublishedDecks);
            Assert.Equal(1, storageStatus.DeckStorage.PublishedVersions);
            Assert.True(storageStatus.DeckStorage.DatabaseBytes > 0);

            using (var compressed = File.OpenRead(backup!))
            using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                var restored = reader.ReadToEnd();
                Assert.Contains("account-a", restored, StringComparison.Ordinal);
                Assert.Contains("published-a", restored, StringComparison.Ordinal);
            }

            using (var connection = Open(store.TransactionalStoragePath))
            {
                Assert.Equal("active-v1", Scalar(connection,
                    "SELECT value FROM storage_meta WHERE key='deck_domain_state';"));
                Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM deck_payloads;"));
                Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM account_decks;"));
                Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM published_decks;"));
                Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM published_deck_versions;"));
                Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM published_deck_likes;"));
                var snapshot = Scalar(connection,
                    "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;");
                using var document = JsonDocument.Parse(snapshot);
                Assert.False(document.RootElement.TryGetProperty("Decks", out _));
                Assert.False(document.RootElement.TryGetProperty("PublishedDecks", out _));
            }

            var reloaded = new L12PlatformStore(path);
            Assert.Equal(3, Assert.Single(reloaded.Decks("account-a")).CardIds.Count);
            var published = Assert.Single(reloaded.PublishedDecks("reader-a"));
            Assert.Equal(4, published.Views);
            Assert.Equal(2, published.Copies);
            Assert.True(published.Liked);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AccountPublicAndTournamentReferencesReuseTheSameCanonicalPayload()
    {
        var root = TempRoot();
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var organizer = store.Register("tdeckstoreH", "password-123").Account!;
            var player = store.Register("tdeckstoreP", "password-123").Account!;
            var source = catalog.PresetDecks[0];
            var first = CopyDeck(source, "同构筑甲");
            var second = CopyDeck(source, "同构筑乙");
            store.UpsertDeck(organizer.Id, first);
            store.UpsertDeck(player.Id, second);
            var published = store.PublishDeck(organizer.Id, first, null)!;

            var tournament = store.CreateTournament(organizer,
                new L12TournamentCreatePayload("存储验收赛", "swiss", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "storage", "after", "season", string.Empty,
                    50, 5, RegistrationVisibility: "public", LateGraceMinutes: 5),
                Context("create-storage-tournament"), true);
            tournament = store.PreCheckInTournament(organizer, tournament.Id,
                new L12TournamentPreCheckInPayload(first.Name, string.Empty), tournament.Version,
                Context("organizer-deck"), true);
            tournament = store.RegisterTournament(player, tournament.Id,
                new L12TournamentRegistrationPayload(), tournament.Version,
                Context("player-deck"), true);
            tournament = store.PreCheckInTournament(player, tournament.Id,
                new L12TournamentPreCheckInPayload(second.Name, string.Empty), tournament.Version,
                Context("player-check-in"), true);

            using var connection = Open(store.TransactionalStoragePath);
            var accountHash = Scalar(connection,
                $"SELECT payload_hash FROM account_decks WHERE account_id='{organizer.Id}' AND name='同构筑甲';");
            Assert.Equal(accountHash, Scalar(connection,
                $"SELECT current_payload_hash FROM published_decks WHERE publication_id='{published.Id}';"));
            Assert.Equal(accountHash, Scalar(connection,
                $"SELECT payload_hash FROM tournament_deck_refs WHERE tournament_id='{tournament.Id}' AND account_id='{organizer.Id}';"));
            Assert.Equal(accountHash, Scalar(connection,
                $"SELECT payload_hash FROM tournament_deck_refs WHERE tournament_id='{tournament.Id}' AND account_id='{player.Id}';"));
            Assert.Equal("1", Scalar(connection,
                $"SELECT COUNT(*) FROM deck_payloads WHERE payload_hash='{accountHash}';"));

            var snapshot = Scalar(connection, "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;");
            using var document = JsonDocument.Parse(snapshot);
            var tournamentJson = document.RootElement.GetProperty("Tournaments").EnumerateArray()
                .Single(item => item.GetProperty("Id").GetString() == tournament.Id);
            foreach (var participant in tournamentJson.GetProperty("Participants").EnumerateArray())
            {
                var deck = participant.GetProperty("Deck");
                Assert.False(deck.TryGetProperty("CardIds", out _));
                Assert.False(deck.TryGetProperty("MoraleIds", out _));
                Assert.False(deck.TryGetProperty("SpecialIds", out _));
            }

            var reloaded = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var restoredTournament = reloaded.Tournament(organizer, tournament.Id)!;
            Assert.All(restoredTournament.Participants, participant =>
                Assert.NotEmpty(participant.Deck!.CardIds));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PrivateBenchUsesCompactCountsAndDoesNotEnterPublishedPayload()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var owner = store.Register("tdeckbench", "password-123").Account!;
            var source = catalog.PresetDecks[0];
            var benchCard = source.CardIds[0];
            var deck = new L12PresetDeckDefinition
            {
                Name = "带备选区牌库", MasterId = source.MasterId, CardIds = [.. source.CardIds],
                MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
                BenchIds = [benchCard, benchCard],
            };

            var saved = store.UpsertDeck(owner.Id, deck);
            Assert.Equal([benchCard, benchCard], saved.BenchIds);
            var published = store.PublishDeck(owner.Id, deck, null)!;
            Assert.Null(published.Deck.BenchIds);

            using (var connection = Open(store.TransactionalStoragePath))
            {
                var compact = Scalar(connection,
                    $"SELECT bench_cards_json FROM account_decks WHERE account_id='{owner.Id}' AND name='带备选区牌库';");
                Assert.Contains($"\"CardId\":\"{benchCard}\"", compact, StringComparison.Ordinal);
                Assert.Contains("\"Quantity\":2", compact, StringComparison.Ordinal);
                Assert.Equal(1, compact.Split(benchCard, StringSplitOptions.None).Length - 1);
                var accountHash = Scalar(connection,
                    $"SELECT payload_hash FROM account_decks WHERE account_id='{owner.Id}' AND name='带备选区牌库';");
                Assert.Equal(accountHash, Scalar(connection,
                    $"SELECT current_payload_hash FROM published_decks WHERE publication_id='{published.Id}';"));
                Assert.Equal("1", Scalar(connection,
                    $"SELECT COUNT(*) FROM deck_payloads WHERE payload_hash='{accountHash}';"));
            }

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.Equal([benchCard, benchCard], Assert.Single(reloaded.Decks(owner.Id)
                .Where(item => item.Name == deck.Name)).BenchIds);
            Assert.Null(Assert.Single(reloaded.PublishedDecks(owner.Id)).Deck.BenchIds);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PublicCountersAreAtomicAndDoNotRewritePlatformSnapshotOrMirror()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tcountown", "password-123").Account!;
            var reader = store.Register("tcountread", "password-123").Account!;
            var deck = new L12PresetDeckDefinition
            {
                Name = "计数牌库", MasterId = "M1", CardIds = ["C1", "C1"],
                MoraleIds = ["R1"], SpecialIds = [],
            };
            var published = store.PublishDeck(owner.Id, deck, null)!;
            using (var checkpoint = Open(store.TransactionalStoragePath))
                Execute(checkpoint, "PRAGMA wal_checkpoint(TRUNCATE);");
            var snapshotBefore = PlatformSnapshotIdentity(store.TransactionalStoragePath);
            var mirrorBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            var walPath = store.TransactionalStoragePath + "-wal";
            var walBefore = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;

            Parallel.For(0, 128, _ => Assert.NotNull(store.RecordPublishedDeckView(published.Id, reader.Id)));
            for (var index = 0; index < 7; index++) store.RecordPublishedDeckCopy(published.Id, reader.Id);
            Assert.True(store.TogglePublishedDeckLike(reader.Id, published.Id)!.Liked);

            var snapshotAfter = PlatformSnapshotIdentity(store.TransactionalStoragePath);
            var mirrorAfter = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(snapshotBefore, snapshotAfter);
            Assert.Equal(mirrorBefore, mirrorAfter);
            var walAfter = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;
            Assert.InRange(Math.Max(0, walAfter - walBefore), 0, 4 * 1024 * 1024);

            var reloaded = new L12PlatformStore(path);
            var restored = Assert.Single(reloaded.PublishedDecks(reader.Id));
            Assert.Equal(128, restored.Views);
            Assert.Equal(7, restored.Copies);
            Assert.Equal(1, restored.Likes);
            Assert.True(restored.Liked);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PublicVersionsAndUnreferencedPayloadsAreNeverPruned()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tversion", "password-123").Account!;
            L12PublishedDeckView? published = null;
            for (var index = 0; index < 25; index++)
            {
                published = store.PublishDeck(owner.Id, new L12PresetDeckDefinition
                {
                    Name = "长期版本牌库", MasterId = "M1", CardIds = [$"C{index:D2}"],
                    MoraleIds = ["R1"], SpecialIds = [],
                }, published?.Id);
            }
            Assert.NotNull(published);
            var orphan = new L12PresetDeckDefinition
            {
                Name = "待删除引用", MasterId = "M9", CardIds = ["ORPHAN"], MoraleIds = ["R9"], SpecialIds = [],
            };
            store.UpsertDeck(owner.Id, orphan);
            string orphanHash;
            using (var connection = Open(store.TransactionalStoragePath))
                orphanHash = Scalar(connection, $"SELECT payload_hash FROM account_decks WHERE account_id='{owner.Id}' AND name='待删除引用';");
            Assert.True(store.DeleteDeck(owner.Id, orphan.Name));

            var reloaded = new L12PlatformStore(path);
            using var verified = Open(reloaded.TransactionalStoragePath);
            Assert.Equal("25", Scalar(verified,
                $"SELECT COUNT(*) FROM published_deck_versions WHERE publication_id='{published!.Id}';"));
            Assert.Equal("1", Scalar(verified,
                $"SELECT COUNT(*) FROM deck_payloads WHERE payload_hash='{orphanHash}';"));
            Assert.Equal("1", Scalar(verified,
                $"SELECT is_deleted FROM account_decks WHERE account_id='{owner.Id}' AND name='待删除引用';"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static L12PresetDeckDefinition CopyDeck(L12PresetDeckDefinition source, string name) => new()
    {
        Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds],
        MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
    };

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournaments");

    private static string PlatformSnapshotIdentity(string path)
    {
        using var connection = Open(path);
        return Scalar(connection,
            "SELECT storage_revision || '|' || snapshot_sha256 || '|' || updated_utc FROM platform_state WHERE singleton_id=1;");
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        connection.Open();
        return connection;
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-deck-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
