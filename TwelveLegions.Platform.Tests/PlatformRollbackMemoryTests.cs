using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformRollbackMemoryTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Theory]
    [InlineData("")]
    [InlineData("ascii")]
    [InlineData("卡牌牌库🙂\r\n")]
    public void StreamHashExactlyMatchesExistingUtf8Hash(string value) => AssertHash(value);

    [Fact]
    public void StreamHashPreservesUnpairedSurrogateReplacement()
    {
        // Construct at runtime: attribute/test-discovery serialization normalizes
        // invalid Unicode and would silently collapse two distinct test IDs.
        AssertHash(new string((char)0xd800, 1));
        AssertHash(new string((char)0xdc00, 1));
        AssertHash("a" + (char)0xd800 + "b" + (char)0xdc00 + "c");
    }

    [Fact]
    public void StreamHashPreservesUnicodeAcrossEveryBufferBoundary()
    {
        for (var offset = 4088; offset <= 4100; offset++)
        {
            AssertHash(new string('a', offset) + "🙂卡\ud800b\udc00" + new string('界', 5000));
            AssertHash(new string('界', offset) + "🙂\ud800\ud800\udc00");
        }
    }

    [Fact]
    public void CompressedCacheIsCompleteAndByteEquivalentToOriginalFullJson()
    {
        InStore((path, store, owner) =>
        {
            Assert.Equal(FullJson(store), CacheJson(store));
            var json = JsonDocument.Parse(CacheJson(store));
            Assert.Single(json.RootElement.GetProperty("Decks").EnumerateArray());
            Assert.Single(json.RootElement.GetProperty("PublishedDecks").EnumerateArray());
            var restarted = new L12PlatformStore(path);
            Assert.Equal(FullJson(restarted), CacheJson(restarted));
            Assert.Equal("saved", Assert.Single(restarted.Decks(owner)).Name);
            Assert.Equal("published", Assert.Single(restarted.PublishedDecks(owner)).Deck.Name);
            Assert.True(restarted.RehearseStorageRecovery().Success);
        });
    }

    [Theory]
    [InlineData(false, "create")]
    [InlineData(false, "update")]
    [InlineData(false, "delete")]
    [InlineData(true, "create")]
    [InlineData(true, "update")]
    [InlineData(true, "delete")]
    public void FailureAfterCompleteEncodingKeepsOldCacheAndAllCommittedFacts(bool objectWrite, string operation)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            var saved = Assert.Single(store.Decks(owner));
            var cache = Cache(store);
            var before = FullJson(store);
            var status = store.StorageStatus();
            var mirror = SHA256.HashData(File.ReadAllBytes(path));
            var reached = false;
            store.StorageFailureInjector = stage =>
            {
                if (stage != "after-rollback-serialize") return;
                reached = true;
                Assert.Same(cache, Cache(store));
                throw new IOException("injected-after-complete-encoding");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
            {
                if (operation == "create") store.CreateDeck(owner, Deck("new", "C2"));
                else if (operation == "update") store.UpdateDeck(owner, saved.Id, saved.Revision, Deck("changed", "C2"));
                else store.DeleteDeck(owner, saved.Id, saved.Revision);
            });
            store.StorageFailureInjector = null;
            Assert.True(reached);
            Assert.Same(cache, Cache(store));
            Assert.Equal(before, FullJson(store));
            Assert.Equal(status.StorageRevision, store.StorageStatus().StorageRevision);
            Assert.Equal(mirror, SHA256.HashData(File.ReadAllBytes(path)));
            var restarted = new L12PlatformStore(path);
            Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(Assert.Single(restarted.Decks(owner))));
            Assert.Equal("published", Assert.Single(restarted.PublishedDecks(owner)).Deck.Name);
            Assert.True(restarted.RehearseStorageRecovery().Success);
            Assert.True(store.UpdateDeck(owner, saved.Id, saved.Revision, Deck("retry", "C2")).Success);
            Assert.NotSame(cache, Cache(store));
            Assert.Equal("retry", Assert.Single(new L12PlatformStore(path).Decks(owner)).Name);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackUsesItsOwnCompleteGenerationWithoutReadingDatabase(bool missingDatabase)
    {
        InStore((path, store, owner) =>
        {
            var before = FullJson(store);
            SqliteConnection.ClearAllPools();
            if (missingDatabase) File.Move(store.TransactionalStoragePath, store.TransactionalStoragePath + ".held");
            else
            {
                var writer = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
                var deck = Assert.Single(writer.Decks(owner));
                Assert.True(writer.UpdateDeck(owner, deck.Id, deck.Revision, Deck("newer-generation", "C2")).Success);
            }
            typeof(L12PlatformStore).GetMethod("RestoreLastCommittedSnapshot", PrivateInstance)!.Invoke(store, null);
            Assert.Equal(before, FullJson(store));
            Assert.Equal("saved", Assert.Single(store.Decks(owner)).Name);
            Assert.Equal("published", Assert.Single(store.PublishedDecks(owner)).Deck.Name);
        });
    }

    [Theory]
    [InlineData("header")]
    [InlineData("middle")]
    [InlineData("truncated")]
    [InlineData("empty")]
    public void DamagedCacheReloadsOnlyCommittedDatabaseFacts(string damage)
    {
        InStore((path, store, owner) =>
        {
            var damaged = (byte[])Cache(store).Clone();
            if (damage == "header") damaged = [1, 2, 3];
            else if (damage == "middle") damaged[damaged.Length / 2] ^= 0xff;
            else if (damage == "truncated") damaged = damaged[..^8];
            else damaged = [];
            typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!.SetValue(store, damaged);
            var before = store.StorageStatus();
            store.StorageFailureInjector = stage => { if (stage == "before-commit") throw new IOException("injected"); };
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Register("failuser", "password-123"));
            Assert.DoesNotContain(store.Accounts(), row => row.Username == "failuser");
            Assert.Equal(before.StorageRevision, store.StorageStatus().StorageRevision);
            Assert.Equal(before.BusinessVersion, store.StorageStatus().BusinessVersion);
            Assert.Equal("saved", Assert.Single(store.Decks(owner)).Name);
            Assert.Equal("published", Assert.Single(store.PublishedDecks(owner)).Deck.Name);
            Assert.Equal(FullJson(store), CacheJson(store));
            store.StorageFailureInjector = null;
            Assert.True(store.Register("retryuser", "password-123").Success);
            var restarted = new L12PlatformStore(path);
            Assert.DoesNotContain(restarted.Accounts(), row => row.Username == "failuser");
            Assert.Contains(restarted.Accounts(), row => row.Username == "retryuser");
            Assert.Equal("saved", Assert.Single(restarted.Decks(owner)).Name);
        });
    }

    private static byte[] Cache(L12PlatformStore store) => (byte[])typeof(L12PlatformStore)
        .GetField("_lastCommittedSnapshot", PrivateInstance)!.GetValue(store)!;

    internal static void AssertCompleteCache(L12PlatformStore store) => Assert.Equal(FullJson(store), CacheJson(store));

    [Fact]
    public void SeasonChangedAndNoOpTransactionsKeepCompleteCacheAndRetryAfterEncodedFailure()
    {
        InStore((path, store, owner) =>
        {
            var admin = store.Login("Admin", "L12master").Account!;
            var operations = store.OperationsConfig(admin);
            var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = endsAt },
            }, operations.Version, "deterministic end", new L12AdminAuditContext("rollback-season-prepare"));
            var before = FullJson(store);
            var cache = Cache(store);
            var mirror = SHA256.HashData(File.ReadAllBytes(path));
            var reached = false;
            store.StorageFailureInjector = stage =>
            {
                if (stage != "after-season-rollback-serialize") return;
                reached = true;
                Assert.Same(cache, Cache(store));
                throw new IOException("injected after complete season encoding");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1)));
            Assert.True(reached);
            store.StorageFailureInjector = null;
            Assert.Same(cache, Cache(store));
            Assert.Equal(before, FullJson(store));
            Assert.Equal(mirror, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.NotNull(store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1)));
            Assert.NotSame(cache, Cache(store));
            AssertCompleteCache(store);
            Assert.Null(store.TryClaimDueSeasonFinalization("other", endsAt.AddSeconds(1), TimeSpan.FromMinutes(1)));
            AssertCompleteCache(store);
            Assert.Equal("saved", Assert.Single(new L12PlatformStore(path).Decks(owner)).Name);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("checksum")]
    [InlineData("payload")]
    public void CorruptCacheAndUnusableDatabaseBlockCurrentReadsAuthenticationAndWrites(string damage)
    {
        InStore((path, store, owner) =>
        {
            typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!.SetValue(store, Array.Empty<byte>());
            SqliteConnection.ClearAllPools();
            if (damage == "missing") File.Move(store.TransactionalStoragePath, store.TransactionalStoragePath + ".held");
            else
            {
                using var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = damage == "checksum"
                    ? "UPDATE platform_state SET snapshot_sha256='invalid';"
                    : "UPDATE deck_payloads SET master_id='TAMPERED';";
                command.ExecuteNonQuery();
            }
            store.StorageFailureInjector = stage => { if (stage == "before-commit") throw new IOException("injected"); };
            Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() => store.Register("failuser", "password-123"));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Accounts());
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Decks(owner));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.PublishedDecks(owner));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Login("rolluser", "password-123"));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Register("retryuser", "password-123"));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Version);
        });
    }

    [Fact]
    public void SuccessfulConflictRefreshCachesTheLatestCompleteGeneration()
    {
        InStore((path, stale, owner) =>
        {
            var writer = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var deck = Assert.Single(writer.Decks(owner));
            Assert.True(writer.UpdateDeck(owner, deck.Id, deck.Revision, Deck("latest", "C2")).Success);
            Assert.Throws<L12PlatformStorageConflictException>(() => stale.Register("staleuser", "password-123"));
            Assert.Equal("latest", Assert.Single(stale.Decks(owner)).Name);
            Assert.Equal(FullJson(stale), CacheJson(stale));
            Assert.DoesNotContain(stale.Accounts(), row => row.Username == "staleuser");
        });
    }

    [Fact]
    public void LegacyFullJsonReadonlyFallbackProducesCompleteRecoverableCache()
    {
        InStore((path, store, ownerId) =>
        {
            var owner = store.Accounts().Single(account => account.Id == ownerId);
            var saved = Assert.Single(store.Decks(ownerId));
            var tournament = store.CreateTournament(owner,
                new L12TournamentCreatePayload("完整回退赛", "swiss", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "fallback", "after", "season",
                    string.Empty, 50, 5, RegistrationVisibility: "public", LateGraceMinutes: 5),
                new L12AdminAuditContext("full-json-fallback-create"), true);
            tournament = store.PreCheckInTournament(owner, tournament.Id,
                new L12TournamentPreCheckInPayload(saved.Name, string.Empty, saved.Id), tournament.Version,
                new L12AdminAuditContext("full-json-fallback-lock"), true);
            var expectedTournamentDeck = Assert.Single(store.Tournament(owner, tournament.Id)!.Participants).Deck!;
            var expectedFullJson = FullJson(store);

            var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, "legacy", "platform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, expectedFullJson);
            File.WriteAllBytes(Path.ChangeExtension(legacyPath, ".db"), [1, 2, 3]);
            var fallback = new L12PlatformStore(legacyPath);
            Assert.Equal("json-fallback-readonly", fallback.StorageStatus().Mode);
            Assert.Equal(FullJson(fallback), CacheJson(fallback));
            var privateDeck = Assert.Single(fallback.Decks(ownerId));
            Assert.Equal("saved", privateDeck.Name);
            Assert.NotEmpty(privateDeck.CardIds);
            var publicDeck = Assert.Single(fallback.PublishedDecks(ownerId)).Deck;
            Assert.Equal("published", publicDeck.Name);
            Assert.NotEmpty(publicDeck.CardIds);
            var restoredTournament = fallback.Tournament(owner, tournament.Id)!;
            var tournamentDeck = Assert.Single(restoredTournament.Participants).Deck!;
            Assert.Equal(expectedTournamentDeck.Name, tournamentDeck.Name);
            Assert.Equal(expectedTournamentDeck.Code, tournamentDeck.Code);
            Assert.Equal(expectedTournamentDeck.Hash, tournamentDeck.Hash);
            Assert.Equal(expectedTournamentDeck.SubmittedAt, tournamentDeck.SubmittedAt);
            Assert.Equal(expectedTournamentDeck.LockedAt, tournamentDeck.LockedAt);
            Assert.Equal(expectedTournamentDeck.MasterId, tournamentDeck.MasterId);
            Assert.Equal(expectedTournamentDeck.CardIds, tournamentDeck.CardIds);
            Assert.Equal(expectedTournamentDeck.MoraleIds, tournamentDeck.MoraleIds);
            Assert.Equal(expectedTournamentDeck.SpecialIds, tournamentDeck.SpecialIds);
            // The private stable ID/revision, publication source/version and any
            // alternate-art metadata are private rows; exact full JSON equality
            // binds those facts in addition to the public tournament projection.
            Assert.Equal(expectedFullJson, FullJson(fallback));
            var before = FullJson(fallback);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fallback.Register("failuser", "password-123"));
            Assert.Equal(before, FullJson(fallback));
        });
    }

    [Fact]
    public void CompactMirrorCannotBecomeAReadonlyEmptyDeckDomain()
    {
        InStore((path, store, _) =>
        {
            SqliteConnection.ClearAllPools();
            File.WriteAllBytes(store.TransactionalStoragePath, [1, 2, 3]);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(path));
        });
    }

    [Fact]
    public void ChangedSameVersionCompactMirrorCannotTombstoneCommittedDeckFacts()
    {
        InStore((path, store, owner) =>
        {
            File.AppendAllText(path, Environment.NewLine);
            var beforeRevision = store.StorageStatus().StorageRevision;
            var restarted = new L12PlatformStore(path);
            Assert.False(restarted.StorageStatus().FallbackMirrorHealthy);
            Assert.Equal(beforeRevision, restarted.StorageStatus().StorageRevision);
            Assert.Equal("saved", Assert.Single(restarted.Decks(owner)).Name);
            Assert.Equal("published", Assert.Single(restarted.PublishedDecks(owner)).Deck.Name);
            using var connection = new SqliteConnection(
                $"Data Source={restarted.TransactionalStoragePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM account_decks WHERE is_deleted=0),
                  (SELECT COUNT(*) FROM published_decks WHERE is_deleted=0);
                """;
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        });
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("unknown-format")]
    public void CompleteLegacyJsonCannotMaskUnsupportedDatabaseSchemaOrPayloadFormat(string damage)
    {
        InStore((path, store, _) =>
        {
            var fullJson = FullJson(store);
            using (var connection = new SqliteConnection(
                       $"Data Source={store.TransactionalStoragePath};Mode=ReadWrite;Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = damage == "future-schema"
                    ? "UPDATE storage_meta SET value='10' WHERE key='schema_version'; UPDATE platform_state SET schema_version=10 WHERE singleton_id=1;"
                    : "UPDATE deck_payloads SET payload_format=99;";
                command.ExecuteNonQuery();
            }
            File.WriteAllText(path, fullJson);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(path));
        });
    }

    [Fact]
    public void PartialLegacyDeckBodyCannotNormalizeIntoReadonlyEmptyCards()
    {
        InStore((path, store, _) =>
        {
            var full = JsonNode.Parse(FullJson(store))!.AsObject();
            full["Decks"]!.AsArray()[0]!.AsObject().Remove("CardIds");
            File.WriteAllText(path, full.ToJsonString());
            SqliteConnection.ClearAllPools();
            File.WriteAllBytes(store.TransactionalStoragePath, [1, 2, 3]);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(path));
        });
    }

    private static string CacheJson(L12PlatformStore store)
    {
        var cache = Cache(store);
        using var bytes = new MemoryStream(cache, 32, cache.Length - 32);
        using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
        using var text = new StreamReader(gzip, Encoding.UTF8);
        return text.ReadToEnd();
    }

    private static string FullJson(L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        var options = (JsonSerializerOptions)typeof(L12PlatformStore)
            .GetField("PlatformMigrationJsonOptions", PrivateStatic)!.GetValue(null)!;
        return JsonSerializer.Serialize(data, data.GetType(), options);
    }

    private static void AssertHash(string value)
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var actual = typeof(L12PlatformStore).GetMethod("Sha256", PrivateStatic)!.Invoke(null, [value]);
        Assert.Equal(expected, actual);
    }

    private static L12PresetDeckDefinition Deck(string name, string card) => new()
        { Name = name, MasterId = "M1", CardIds = [card], MoraleIds = [], SpecialIds = [] };

    private static void InStore(Action<string, L12PlatformStore, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-rollback-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var registration = store.Register("rolluser", "password-123");
            Assert.True(registration.Success, registration.Message);
            var owner = registration.Account!.Id;
            Assert.True(store.CreateDeck(owner, Deck("saved", "C1")).Success);
            Assert.NotNull(store.PublishDeck(owner, Deck("published", "C1"), null));
            action(path, store, owner);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
