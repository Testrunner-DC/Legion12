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
public sealed class ValidatedCompactDeckPersistenceTests(ITestOutputHelper output)
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Persist = typeof(L12PlatformStore).GetMethod("PersistPayload", Static)!;
    private static readonly MethodInfo Read = typeof(L12PlatformStore).GetMethods(Static)
        .Single(method => method.Name == "ReadReferencedDeckPayload" && method.GetParameters().Length == 2);
    private sealed record Measurement(long Bytes, double Milliseconds);

    [Fact]
    public void AlreadyValidatedBodyDoesNotEncodeAgainBeforeItsRealInsert()
    {
        InStore(1, 1, (_, store) =>
        {
            var data = Data(store);
            var facts = (IDictionary)Property(data, "DeckPayloads")!;
            var hash = Assert.Single(facts.Keys.Cast<string>());
            var fact = facts[hash]!;
            var master = (string)Property(fact, "MasterId")!;
            var format = (int)Property(fact, "Format")!;
            var compact = (string)Property(fact, "PayloadJson")!;
            // The real read performs Decode's canonical byte comparison and complete hash check.
            var normalized = Read.Invoke(null, [data, hash])!;
            var main = (string)Property(normalized, "MainJson")!;
            var morale = (string)Property(normalized, "MoraleJson")!;
            var special = (string)Property(normalized, "SpecialJson")!;
            var argument = PersistenceArgument(data, hash, normalized);
            var created = DateTimeOffset.Parse("2020-01-02T03:04:05+08:00");
            using var oldDb = PayloadDatabase();
            using var candidateDb = PayloadDatabase();
            using var actualDb = PayloadDatabase();
            using var oldTransaction = oldDb.BeginTransaction();
            using var candidateTransaction = candidateDb.BeginTransaction();
            using var actualTransaction = actualDb.BeginTransaction();
            string? encoded = null;
            var encoding = Measure(() => encoded = L12DeckPayloadCodec.EncodeLegacyJson(main, morale, special));
            var old = Measure(() => Insert(oldDb, oldTransaction, hash, master, format,
                L12DeckPayloadCodec.EncodeLegacyJson(main, morale, special), created));
            var candidate = Measure(() => Insert(candidateDb, candidateTransaction, hash, master, format, compact, created));
            var actual = Measure(() => Persist.Invoke(null, [actualDb, actualTransaction, argument, created]));
            Assert.Equal(compact, encoded);
            Print("encode_only_8_runs", encoding);
            Print("old_last_encode_plus_real_sql_8_runs", old);
            Print("validated_string_plus_same_real_sql_control_8_runs", candidate);
            Print("product_last_persistence_step_8_runs", actual);
            oldTransaction.Commit(); candidateTransaction.Commit(); actualTransaction.Commit();
            Assert.Equal(PayloadRows(oldDb), PayloadRows(candidateDb));
            Assert.Equal(PayloadRows(oldDb), PayloadRows(actualDb));
            Assert.True(candidate.Bytes < old.Bytes * 4 / 5, "The exact same SQL control must demonstrate the redundant encoding cost first.");
            Assert.True(actual.Bytes < old.Bytes * 4 / 5,
                $"The product still encodes an already validated body: actual={actual.Bytes}, old={old.Bytes}, validated-control={candidate.Bytes}.");
        });
    }

    [Theory]
    [InlineData(240, 4)]
    [InlineData(240, 240)]
    public void WholeUnchangedPersistenceReportsItsOwnCostAndPreservesExactDatabaseFacts(int references, int uniqueBodies)
    {
        InStore(references, uniqueBodies, (path, store) =>
        {
            var data = Data(store);
            var facts = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data, data.GetType()));
            var mirror = SHA256.HashData(File.ReadAllBytes(path));
            var revision = store.StorageStatus().StorageRevision;
            using var db = Open(store.TransactionalStoragePath);
            var payloads = PayloadRows(db);
            using var measurement = L12PlatformStore.BeginSyntheticStorageMeasurement();
            typeof(L12PlatformStore).GetMethod("AttachSyntheticStorageMeasurement", Static)!.Invoke(null, [db]);
            using var transaction = db.BeginTransaction();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var tick = Stopwatch.GetTimestamp();
            typeof(L12PlatformStore).GetMethod("PersistDeckDomainSnapshot", Static)!.Invoke(null, [db, transaction, data]);
            var duration = Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            transaction.Commit();
            var measured = measurement.Complete();
            output.WriteLine(JsonSerializer.Serialize(new { phase = "whole_unchanged_deck_domain_not_just_encode",
                references, uniqueBodies, allocatedBytes = allocated, elapsedMs = duration,
                measured.DeckDomainStatements, measured.SqlitePagePayloadBytes, measured.MirrorBytes }));
            Assert.Equal(0, measured.InstrumentationErrors);
            Assert.InRange(measured.DeckDomainStatements, uniqueBodies, uniqueBodies + 64);
            Assert.Equal(0, measured.SqlitePagePayloadBytes);
            Assert.Equal(payloads, PayloadRows(db));
            Assert.Equal(facts, SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data, data.GetType())));
            Assert.Equal(mirror, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(revision, store.StorageStatus().StorageRevision);
            Assert.Equal(references, new L12PlatformStore(path).StorageStatus().DeckStorage!.ActiveAccountDecks);
            Assert.True(store.RehearseStorageRecovery().Success);
        });
    }

    [Theory]
    [InlineData("format")]
    [InlineData("json")]
    [InlineData("noncanonical")]
    [InlineData("hash")]
    [InlineData("master")]
    public void EachNewTransactionStillValidatesTheEntireFactAndRollsBackFailures(string damage)
    {
        InStore(2, 1, (path, store) =>
        {
            var actor = new L12AccountView("synthetic-owner", "synthetic", "admin", DateTimeOffset.UnixEpoch, false);
            void Save() => store.RecordCommandOutcome(actor, new L12AdminAuditContext("synthetic-validated-payload"),
                "synthetic", "storage", "succeeded");
            Save();
            var mirror = SHA256.HashData(File.ReadAllBytes(path));
            var revision = store.StorageStatus().StorageRevision;
            using var db = Open(store.TransactionalStoragePath);
            var stored = PayloadRows(db);
            var payloads = (IDictionary)Property(Data(store), "DeckPayloads")!;
            var hash = Assert.Single(payloads.Keys.Cast<string>());
            var fact = payloads[hash]!;
            var json = (string)Property(fact, "PayloadJson")!;
            payloads[hash] = Activator.CreateInstance(fact.GetType(), damage == "master" ? "WRONG" : "M1",
                damage == "format" ? 99 : L12DeckPayloadCodec.CurrentFormat, damage switch
                {
                    "json" => "not-json",
                    "noncanonical" => " " + json,
                    "hash" => L12DeckPayloadCodec.EncodeLegacyJson("[{\"CardId\":\"DIFFERENT\",\"Quantity\":1}]", "[]", "[]"),
                    _ => json,
                })!;
            Assert.Throws<L12PlatformStorageUnavailableException>(Save);
            Assert.Equal(mirror, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(revision, store.StorageStatus().StorageRevision);
            Assert.Equal(stored, PayloadRows(db));
            Save(); // The failed call cannot authorize any later transaction or retain a validation memo.
            Assert.Equal(revision + 1, new L12PlatformStore(path).StorageStatus().StorageRevision);
            Assert.Equal(stored, PayloadRows(db));
        });
    }

    // Exercise old and new real method signatures so the original product produces a causal RED.
    // The branch does not bypass validation: the old read or its new validated out-port always runs.
    private static object PersistenceArgument(object data, string hash, object normalized)
    {
        if (Persist.GetParameters()[2].ParameterType.IsInstanceOfType(normalized)) return normalized;
        var read = typeof(L12PlatformStore).GetMethods(Static)
            .Single(method => method.Name == "ReadReferencedDeckPayload" && method.GetParameters().Length == 3);
        object?[] args = [data, hash, null];
        read.Invoke(null, args);
        return args[2]!;
    }

    private static void Insert(SqliteConnection db, SqliteTransaction transaction, string hash, string master,
        int format, string compact, DateTimeOffset created)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO deck_payloads(payload_hash,master_id,payload_format,payload_json,created_utc)
            VALUES($hash,$master,$format,$payload,$created);
            """;
        command.Parameters.AddWithValue("$hash", hash); command.Parameters.AddWithValue("$master", master);
        command.Parameters.AddWithValue("$format", format); command.Parameters.AddWithValue("$payload", compact);
        command.Parameters.AddWithValue("$created", created.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static SqliteConnection PayloadDatabase()
    {
        var db = Open(":memory:");
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE deck_payloads(payload_hash TEXT PRIMARY KEY,master_id TEXT NOT NULL,payload_format INTEGER NOT NULL,payload_json TEXT NOT NULL,created_utc TEXT NOT NULL);";
        command.ExecuteNonQuery();
        return db;
    }

    private static string PayloadRows(SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT payload_hash,master_id,payload_format,payload_json,created_utc FROM deck_payloads ORDER BY payload_hash;";
        using var reader = command.ExecuteReader();
        var rows = new List<object[]>();
        while (reader.Read()) { var row = new object[reader.FieldCount]; reader.GetValues(row); rows.Add(row); }
        return JsonSerializer.Serialize(rows);
    }

    private static SqliteConnection Open(string path)
    { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); db.Open(); return db; }
    private static object? Property(object row, string name) => row.GetType().GetProperty(name)!.GetValue(row);
    private static object Data(L12PlatformStore store) => typeof(L12PlatformStore).GetProperty("_data", Instance)!.GetValue(store)!;
    private static Measurement Measure(Action action)
    {
        action();
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var tick = Stopwatch.GetTimestamp();
        for (var i = 0; i < 8; i++) action();
        return new(GC.GetAllocatedBytesForCurrentThread() - bytes, Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
    }
    private void Print(string phase, Measurement measurement)
        => output.WriteLine(JsonSerializer.Serialize(new { phase, allocatedBytes = measurement.Bytes, elapsedMs = measurement.Milliseconds }));

    private static void InStore(int references, int uniqueBodies, Action<string, L12PlatformStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "vp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var succeeded = false;
        try
        {
            var path = Path.Combine(root, "platform.json"); var at = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var decks = Enumerable.Range(0, references).Select(index => new
            {
                AccountId = "synthetic-owner", Name = "deck-" + index, MasterId = "M1",
                CardIds = Enumerable.Range(0, 20).SelectMany(card => Enumerable.Repeat($"CARD-{index % uniqueBodies}-{card}", 2)).ToArray(),
                MoraleIds = Enumerable.Range(0, 10).SelectMany(card => Enumerable.Repeat($"MORALE-{card}", 2)).ToArray(),
                SpecialIds = Array.Empty<string>(), UpdatedAt = at.AddSeconds(index),
            }).ToArray();
            File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1L, BusinessVersion = 1L, Decks = decks }));
            action(path, new L12PlatformStore(path)); succeeded = true;
        }
        finally { SqliteConnection.ClearAllPools(); if (succeeded) Directory.Delete(root, true); }
    }
}
