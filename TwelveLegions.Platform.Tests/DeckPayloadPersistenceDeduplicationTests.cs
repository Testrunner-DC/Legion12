using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeckPayloadPersistenceDeduplicationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(240, 4)]
    [InlineData(240, 240)]
    [InlineData(9299, 1196)]
    public void IsolatedUnchangedDeckPersistenceReportsReferenceWorkWithoutChangingFacts(int references, int uniqueBodies)
        => MeasureUnchangedPersistence(references, uniqueBodies);

    [Fact]
    public void IsolatedReferenceFanoutWithFourBodiesReportsReferenceOverhead()
        => MeasureUnchangedPersistence(9299, 4);

    private void MeasureUnchangedPersistence(int references, int uniqueBodies)
    {
        InStore(references, uniqueBodies, (path, store) =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(store)!;
            var factsBefore = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data, data.GetType()));
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(path));
            var revisionBefore = store.StorageStatus().StorageRevision;
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = store.TransactionalStoragePath, Pooling = false }.ToString());
            db.Open();
            using var scope = L12PlatformStore.BeginSyntheticStorageMeasurement();
            typeof(L12PlatformStore).GetMethod("AttachSyntheticStorageMeasurement", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [db]);
            using var transaction = db.BeginTransaction();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            InvokePersistence(db, transaction, data);
            timer.Stop();
            var persistenceAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var commitTimer = Stopwatch.StartNew();
            transaction.Commit();
            commitTimer.Stop();
            var measured = scope.Complete();
            output.WriteLine(JsonSerializer.Serialize(new
            {
                phase = "isolated-unchanged-deck-domain", references, uniqueBodies,
                persistenceMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = persistenceAllocated,
                commitMs = commitTimer.Elapsed.TotalMilliseconds,
                measured.DeckDomainStatements, measured.SqlitePagePayloadBytes, measured.MirrorBytes,
                excludes = "core snapshot, rollback, mirror preparation/write, audit and outer save",
            }));
            Assert.Equal(0, measured.InstrumentationErrors);
            Assert.InRange(measured.DeckDomainStatements, uniqueBodies, uniqueBodies + 64);
            Assert.Equal(0, measured.MirrorBytes);
            Assert.Equal(references, Count(db, "account_decks"));
            Assert.Equal(uniqueBodies, Count(db, "deck_payloads"));
            Assert.Equal(factsBefore, SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data, data.GetType())));
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(revisionBefore, store.StorageStatus().StorageRevision);
            var restarted = new L12PlatformStore(path);
            Assert.Equal(references, restarted.StorageStatus().DeckStorage!.ActiveAccountDecks);
            Assert.True(restarted.RehearseStorageRecovery().Success);
        });
    }

    [Theory]
    [InlineData(240, 4)]
    [InlineData(240, 240)]
    [InlineData(9299, 1196)]
    public void FullSaveValidatesAndInsertsEachDistinctBodyOnce(int references, int uniqueBodies)
    {
        InStore(references, uniqueBodies, (path, store) =>
        {
            var actor = new L12AccountView("synthetic-owner", "synthetic", "admin", DateTimeOffset.UnixEpoch, false);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            L12SyntheticStorageMeasurement measured;
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                store.RecordCommandOutcome(actor, new L12AdminAuditContext("synthetic-payload-save"),
                    "synthetic", "storage", "succeeded");
                measured = scope.Complete();
            }
            timer.Stop();
            output.WriteLine(JsonSerializer.Serialize(new { references, uniqueBodies,
                elapsedMs = timer.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
                measured.DeckDomainStatements, measured.SqlitePagePayloadBytes, measured.MirrorBytes }));
            Assert.Equal(0, measured.InstrumentationErrors);
            // Every reference retains validation; unchanged metadata needs no
            // command. Body work scales with distinct immutable facts.
            Assert.InRange(measured.DeckDomainStatements, uniqueBodies, uniqueBodies + 64);
            using var db = Open(store.TransactionalStoragePath);
            Assert.Equal(references, Count(db, "account_decks"));
            Assert.Equal(uniqueBodies, Count(db, "deck_payloads"));
            var reloaded = new L12PlatformStore(path);
            Assert.Equal(references, reloaded.StorageStatus().DeckStorage!.ActiveAccountDecks);
            Assert.True(reloaded.RehearseStorageRecovery().Success);
        });
    }

    [Fact]
    public void AccountPublicAndTournamentReferencesShareBodyAndKeepFirstCreatedTime()
    {
        InStore(0, 1, (_, store) =>
        {
            const string json = """
                {"Decks":[
                  {"AccountId":"a","Name":"first","MasterId":"M1","CardIds":["C1"],"UpdatedAt":"2026-01-01T00:00:00Z"},
                  {"AccountId":"b","Name":"second","MasterId":"M1","CardIds":["C1"],"UpdatedAt":"2026-02-01T00:00:00Z"}],
                 "PublishedDecks":[{"Id":"publication","OwnerId":"a","Name":"public","MasterId":"M1","CardIds":["C1"],"CreatedAt":"2020-01-01T00:00:00Z"}],
                 "Tournaments":[{"Id":"tournament","Participants":[{"AccountId":"a","Deck":{"MasterId":"M1","CardIds":["C1"],"SubmittedAt":"2019-01-01T00:00:00Z"}}]}]}
                """;
            var dataType = typeof(L12PlatformStore).GetNestedType("DataFile", BindingFlags.NonPublic)!;
            var data = JsonSerializer.Deserialize(json, dataType)!;
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.TransactionalStoragePath, Pooling = false }.ToString());
            db.Open();
            L12SyntheticStorageMeasurement measured;
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                typeof(L12PlatformStore).GetMethod("AttachSyntheticStorageMeasurement", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [db]);
                using var transaction = db.BeginTransaction();
                InvokePersistence(db, transaction, data);
                transaction.Commit();
                measured = scope.Complete();
            }
            Assert.Equal(1, measured.Connections);
            // Two private references, public head/history, tournament reference,
            // and exactly one shared body. No empty measurement may pass this.
            Assert.Equal(6, measured.InsertStatements);
            Assert.Equal(1, Count(db, "deck_payloads"));
            Assert.Equal(2, Count(db, "account_decks"));
            Assert.Equal(1, Count(db, "published_decks"));
            Assert.Equal(1, Count(db, "published_deck_versions"));
            Assert.Equal(1, Count(db, "tournament_deck_refs"));
            using var first = db.CreateCommand();
            first.CommandText = "SELECT created_utc FROM deck_payloads;";
            Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse((string)first.ExecuteScalar()!));
            Assert.Equal(0, measured.InstrumentationErrors);
        });
    }

    [Fact]
    public void LateCapturedTournamentBodyWithEmptyMasterIsStillPersisted()
    {
        InStore(0, 1, (_, store) =>
        {
            const string json = """
                {"Tournaments":[{"Id":"late","Participants":[{"AccountId":"a","Deck":{"MasterId":"","CardIds":["C1"]}}]}]}
                """;
            var data = JsonSerializer.Deserialize(json, typeof(L12PlatformStore).GetNestedType("DataFile", BindingFlags.NonPublic)!)!;
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.TransactionalStoragePath, Pooling = false }.ToString());
            db.Open();
            using var transaction = db.BeginTransaction();
            InvokePersistence(db, transaction, data);
            transaction.Commit();
            Assert.Equal(1, Count(db, "deck_payloads"));
            Assert.Equal(1, Count(db, "tournament_deck_refs"));
        });
    }

    [Fact]
    public void LaterReferenceWithWrongMasterCannotReuseEarlierValidatedHash()
    {
        InStore(2, 1, (path, store) =>
        {
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var revision = store.StorageStatus().StorageRevision;
            var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            var rows = (IList)data.GetType().GetProperty("Decks")!.GetValue(data)!;
            rows[1]!.GetType().GetProperty("MasterId")!.SetValue(rows[1], "WRONG");
            var actor = new L12AccountView("synthetic-owner", "synthetic", "admin", DateTimeOffset.UnixEpoch, false);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.RecordCommandOutcome(actor,
                new L12AdminAuditContext("wrong-master"), "synthetic", "storage", "succeeded"));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(revision, store.StorageStatus().StorageRevision);
            Assert.Equal("M1", store.Decks("synthetic-owner")[1].MasterId);
        });
    }

    private static void InvokePersistence(SqliteConnection db, SqliteTransaction transaction, object data)
        => typeof(L12PlatformStore).GetMethod("PersistDeckDomainSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [db, transaction, data]);

    [Fact]
    public void LaterTransactionRevalidatesSameHashAndRejectsCorruptBody()
    {
        InStore(8, 1, (path, store) =>
        {
            var actor = new L12AccountView("synthetic-owner", "synthetic", "admin", DateTimeOffset.UnixEpoch, false);
            void Save() => store.RecordCommandOutcome(actor, new L12AdminAuditContext("synthetic-revalidate"),
                "synthetic", "storage", "succeeded");
            Save();
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var revision = store.StorageStatus().StorageRevision;
            var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            var facts = (IDictionary)data.GetType().GetProperty("DeckPayloads")!.GetValue(data)!;
            var hash = Assert.Single(facts.Keys.Cast<string>());
            var fact = facts[hash]!;
            var corrupt = L12DeckPayloadCodec.EncodeLegacyJson("[{\"CardId\":\"CORRUPT\",\"Quantity\":1}]", "[]", "[]");
            facts[hash] = Activator.CreateInstance(fact.GetType(), "M1", L12DeckPayloadCodec.CurrentFormat, corrupt)!;
            Assert.Throws<L12PlatformStorageUnavailableException>(Save);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(revision, store.StorageStatus().StorageRevision);
            Assert.Equal("CARD-0-0", store.Decks("synthetic-owner")[0].CardIds[0]);
            Save(); // Failed-call memoization must not outlive its transaction.
            Assert.Equal(revision + 1, new L12PlatformStore(path).StorageStatus().StorageRevision);
        });
    }

    private static void InStore(int references, int uniqueBodies, Action<string, L12PlatformStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-payload-dedup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var succeeded = false;
        try
        {
            var path = Path.Combine(root, "platform.json");
            var at = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var rows = Enumerable.Range(0, references).Select(index => new
            {
                AccountId = "synthetic-owner", Name = "deck-" + index, MasterId = "M1",
                CardIds = Enumerable.Range(0, 20).SelectMany(card => Enumerable.Repeat("CARD-" + index % uniqueBodies + "-" + card, 2)).ToArray(),
                MoraleIds = Enumerable.Range(0, 10).SelectMany(card => Enumerable.Repeat("MORALE-" + card, 2)).ToArray(),
                SpecialIds = Array.Empty<string>(), UpdatedAt = at.AddSeconds(index),
            }).ToArray();
            File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1L, BusinessVersion = 1L, Decks = rows }));
            action(path, new L12PlatformStore(path));
            succeeded = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (succeeded) Directory.Delete(root, true);
        }
    }

    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static long Count(SqliteConnection db, string table)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long)command.ExecuteScalar()!;
    }
}
