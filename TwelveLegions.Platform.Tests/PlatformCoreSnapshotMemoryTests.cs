using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformCoreSnapshotMemoryTests(ITestOutputHelper output)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public void ProductUtf8PreparationDoesNotRetainAWholeDocumentBuffer()
    {
        using var fixture = new RankedIdentityFixture();
        var data = Data(fixture.Store);
        var part = new string('x', 64 * 1024);
        for (var i = 0; i < 256; i++) Content(data)["synthetic-stream-part-" + i] = part;
        using var connection = Open(fixture.Store.TransactionalStoragePath);
        using var transaction = connection.BeginTransaction();
        var expectedJson = LegacySnapshot(data);
        var expectedBytes = Encoding.UTF8.GetByteCount(expectedJson);
        var warm = PrepareCoreSnapshot(data, connection, transaction);
        GC.KeepAlive(warm);
        var started = GC.GetAllocatedBytesForCurrentThread();
        var prepared = PrepareCoreSnapshot(data, connection, transaction);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - started;
        output.WriteLine("stream_prepare_payload_bytes={0} allocated_bytes={1}", expectedBytes, allocated);
        Assert.False(JsonValue(prepared) is byte[] or string,
            "Product preparation must not retain a whole-document byte array or string");
        Assert.Equal(LegacyChecksum(expectedJson), Checksum(prepared));
        Assert.True(allocated < expectedBytes / 4,
            $"Stream preparation allocated {allocated} bytes for {expectedBytes} document bytes");
        transaction.Rollback();
    }

    [Fact]
    public void Utf8SnapshotIsByteExactWithLegacyWhileUtf16AndUnknownRemainStrings()
    {
        using var fixture = new RankedIdentityFixture();
        var data = Data(fixture.Store);
        Content(data)["合成键-引号\"-反斜线\\-emoji🙂-nul\0"] =
            "中文🙂 \"quoted\" \\ slash\r\n\t nul:\0 end";
        Content(data)["synthetic-empty-value"] = string.Empty;
        var inputBefore = RawJson(data);
        var legacyJson = LegacySnapshot(data);
        var expectedUtf8 = Encoding.UTF8.GetBytes(legacyJson);
        var expectedChecksum = Utf8Sha256(expectedUtf8);

        var utf8 = SerializeCoreSnapshot(data, "UTF-8");
        Assert.Equal(expectedUtf8, Assert.IsType<byte[]>(JsonValue(utf8)));
        Assert.Equal(expectedChecksum, Checksum(utf8));
        Assert.Equal(expectedChecksum, LegacyChecksum(legacyJson));

        foreach (var encoding in new[] { "UTF-16le", "UTF-16be", "synthetic-unknown" })
        {
            var compatible = SerializeCoreSnapshot(data, encoding);
            Assert.Equal(legacyJson, Assert.IsType<string>(JsonValue(compatible)));
            Assert.Equal(expectedChecksum, Checksum(compatible));
        }

        Assert.Equal(inputBefore, RawJson(data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedPayloadBetweenStreamingPassesRollsBackTheWholeSqliteWrite(bool changeLength)
    {
        using var fixture = new RankedIdentityFixture();
        var data = Data(fixture.Store);
        using var connection = Open(fixture.Store.TransactionalStoragePath);
        var beforeJson = RequiredScalarText(connection, "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;");
        var beforeHash = RequiredScalarText(connection, "SELECT snapshot_sha256 FROM platform_state WHERE singleton_id=1;");
        var beforeMirror = RequiredScalarText(connection, "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';");
        Content(data)["synthetic-between-passes"] = "AAAA";
        using (var transaction = connection.BeginTransaction())
        {
            var prepared = PrepareCoreSnapshot(data, connection, transaction);
            Content(data)["synthetic-between-passes"] = changeLength ? new string('B', 1024 * 1024) : "BBBB";
            var failure = Assert.Throws<TargetInvocationException>(() =>
                typeof(L12PlatformStore).GetMethod("UpsertSnapshot", PrivateStatic)!
                    .Invoke(null, [connection, transaction, prepared, Checksum(prepared), "synthetic-mirror", data]));
            Assert.NotNull(failure.InnerException);
            if (!changeLength) Assert.IsType<InvalidDataException>(failure.InnerException);
            transaction.Rollback();
        }
        Assert.Equal(beforeJson, RequiredScalarText(connection, "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(beforeHash, RequiredScalarText(connection, "SELECT snapshot_sha256 FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(beforeMirror, RequiredScalarText(connection, "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));
    }

    [Fact]
    public void WarmTwelveMiBUtf8PreparationAllocatesLessThanSeventyPercentOfLegacyStringAndHash()
    {
        using var fixture = new RankedIdentityFixture();
        var data = Data(fixture.Store);
        Content(data)["synthetic-twelve-mib-core"] = new string('a', 12 * 1024 * 1024);
        WarmSerialization(data);

        var legacyStart = GC.GetAllocatedBytesForCurrentThread();
        var legacyJson = LegacySnapshot(data);
        var legacyChecksum = LegacyChecksum(legacyJson);
        var legacyAllocated = GC.GetAllocatedBytesForCurrentThread() - legacyStart;

        var utf8Start = GC.GetAllocatedBytesForCurrentThread();
        var prepared = SerializeCoreSnapshot(data, "UTF-8");
        var utf8Allocated = GC.GetAllocatedBytesForCurrentThread() - utf8Start;
        var utf8 = Assert.IsType<byte[]>(JsonValue(prepared));
        GC.KeepAlive(legacyJson);
        GC.KeepAlive(prepared);

        output.WriteLine(
            "synthetic_payload_bytes={0} legacy_string_hash_allocated_bytes={1} utf8_snapshot_allocated_bytes={2} ratio={3:F4}",
            12 * 1024 * 1024, legacyAllocated, utf8Allocated,
            legacyAllocated == 0 ? double.PositiveInfinity : (double)utf8Allocated / legacyAllocated);
        Assert.True(legacyAllocated >= 24L * 1024 * 1024);
        Assert.Equal(Encoding.UTF8.GetByteCount(legacyJson), utf8.Length);
        Assert.Equal(legacyChecksum, Checksum(prepared));
        Assert.True(utf8Allocated * 10 < legacyAllocated * 7,
            $"UTF-8 preparation allocated {utf8Allocated} bytes versus legacy {legacyAllocated} bytes");
    }

    [Theory]
    [InlineData("UTF-8", true)]
    [InlineData("UTF-16le", false)]
    [InlineData("UTF-16be", false)]
    public void RealSqliteEncodingStoresCanonicalJsonAsTextAndRoundTripsThroughGetString(
        string databaseEncoding, bool expectsBytes)
    {
        using var fixture = new RankedIdentityFixture();
        var data = Data(fixture.Store);
        Content(data)["synthetic-encoding"] = "中文🙂 \"escape\" \\ line\r\n nul:\0 end";
        var legacyJson = LegacySnapshot(data);
        var expectedChecksum = Utf8Sha256(Encoding.UTF8.GetBytes(legacyJson));
        var databasePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!,
            "core-snapshot-" + databaseEncoding.ToLowerInvariant() + ".db");

        using (var connection = Open(databasePath))
        {
            Execute(connection, $"PRAGMA encoding='{databaseEncoding}';");
            Execute(connection, "CREATE TABLE snapshot(json TEXT NOT NULL, checksum TEXT NOT NULL);");
            using var transaction = connection.BeginTransaction();
            // Retain the materialized formatter/encoding compatibility contract. Actual product
            // streaming upserts and private-deck CAS are verified across two reopens below.
            var prepared = SerializeCoreSnapshot(data, databaseEncoding);
            if (expectsBytes) Assert.IsType<byte[]>(JsonValue(prepared));
            else Assert.IsType<string>(JsonValue(prepared));
            Assert.Equal(expectedChecksum, Checksum(prepared));

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO snapshot(json,checksum) VALUES(CAST($json AS TEXT),$checksum);";
                insert.Parameters.AddWithValue("$json", JsonValue(prepared));
                insert.Parameters.AddWithValue("$checksum", Checksum(prepared));
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
            VerifyStoredSnapshot(connection, transaction, legacyJson, expectedChecksum);
            transaction.Commit();
        }

        using (var reopened = Open(databasePath))
        {
            using var pragma = reopened.CreateCommand();
            pragma.CommandText = "PRAGMA encoding;";
            Assert.Equal(databaseEncoding.ToUpperInvariant(),
                Assert.IsType<string>(pragma.ExecuteScalar()).ToUpperInvariant());
            VerifyStoredSnapshot(reopened, null, legacyJson, expectedChecksum);
        }
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16le")]
    [InlineData("UTF-16be")]
    public void ProductUpsertAndPrivateDeckCasKeepEncodedPlatformStateCanonicalAcrossTwoReopens(
        string databaseEncoding)
    {
        using var fixture = new RankedIdentityFixture();
        var directory = System.IO.Path.GetDirectoryName(fixture.Path)!;
        var platformPath = System.IO.Path.Combine(directory,
            "product-core-" + databaseEncoding.ToLowerInvariant() + ".json");
        var databasePath = System.IO.Path.ChangeExtension(platformPath, ".db");
        using (var seeded = Open(databasePath))
        {
            Execute(seeded, $"PRAGMA encoding='{databaseEncoding}';");
            Execute(seeded, "CREATE TABLE encoding_guard(value TEXT NOT NULL);");
            Execute(seeded, "INSERT INTO encoding_guard(value) VALUES('preserve');");
        }

        var store = NewStore(platformPath, fixture);
        store.PrivateDeckObjectPersistenceEnabled = true;
        AssertProductPlatformState(store, databaseEncoding);
        var registration = store.Register("encowner", "Password123!");
        var account = Assert.IsType<L12AccountView>(registration.Account);
        var session = store.AuthenticateTokenSession(registration.Token!)!;
        AssertProductPlatformState(store, databaseEncoding);

        store.ExecuteAdminTransaction(() =>
        {
            Content(Data(store))["synthetic-product-encoding"] = "已提交🙂";
            RankedIdentityTestState.RequestSave(store);
            return true;
        });
        AssertProductPlatformState(store, databaseEncoding);

        var source = fixture.Catalog.PresetDecks[0];
        var deck = new L12PresetDeckDefinition
        {
            Name = "编码CAS🙂",
            MasterId = source.MasterId,
            CardIds = [.. source.CardIds],
            MoraleIds = [.. source.MoraleIds],
            SpecialIds = [.. source.SpecialIds],
            BenchIds = [.. source.BenchIds ?? []],
        };
        var saved = store.UpsertDeck(account.Id, deck);
        Assert.Equal(deck.Name, saved.Name);
        var committedVersion = store.Version;
        AssertProductPlatformState(store, databaseEncoding);

        var firstReopen = NewStore(platformPath, fixture);
        firstReopen.PrivateDeckObjectPersistenceEnabled = true;
        Assert.Equal(committedVersion, firstReopen.Version);
        Assert.True(firstReopen.IsSessionActive(session.SessionId));
        Assert.Equal("已提交🙂",
            Assert.IsType<string>(Content(Data(firstReopen))["synthetic-product-encoding"]));
        Assert.Contains(firstReopen.Decks(account.Id), item => item.Name == deck.Name);
        AssertProductPlatformState(firstReopen, databaseEncoding);

        var secondReopen = NewStore(platformPath, fixture);
        secondReopen.PrivateDeckObjectPersistenceEnabled = true;
        Assert.Equal(committedVersion, secondReopen.Version);
        Assert.True(secondReopen.IsSessionActive(session.SessionId));
        Assert.Equal("已提交🙂",
            Assert.IsType<string>(Content(Data(secondReopen))["synthetic-product-encoding"]));
        Assert.Contains(secondReopen.Decks(account.Id), item => item.Name == deck.Name);
        AssertProductPlatformState(secondReopen, databaseEncoding);
    }

    [Theory]
    [InlineData("UTF-16le")]
    [InlineData("UTF-16be")]
    public void Utf16RankedBroadcastReverseMigrationRenameRetryPreservesEncodingAndState(
        string databaseEncoding)
    {
        using var fixture = new RankedIdentityFixture();
        var directory = System.IO.Path.GetDirectoryName(fixture.Path)!;
        var platformPath = System.IO.Path.Combine(directory,
            "utf16-ranked-reverse-" + databaseEncoding.ToLowerInvariant() + ".json");
        var databasePath = System.IO.Path.ChangeExtension(platformPath, ".db");
        using (var seeded = Open(databasePath))
        {
            Execute(seeded, $"PRAGMA encoding='{databaseEncoding}';");
            Execute(seeded, "CREATE TABLE encoding_guard(value TEXT NOT NULL);");
            Execute(seeded, "INSERT INTO encoding_guard(value) VALUES('preserve');");
        }

        var store = NewStore(platformPath, fixture);
        var registration = store.Register("encmig", "Password123!");
        var session = store.AuthenticateTokenSession(registration.Token!)!;
        const string expectedValue = "反向迁移🙂 \"quoted\" \\ nul:\0 end";
        store.ExecuteAdminTransaction(() =>
        {
            Content(Data(store))["synthetic-utf16-reverse"] = expectedValue;
            RankedIdentityTestState.RequestSave(store);
            return true;
        });
        var committedVersion = store.Version;
        Assert.True(store.IsSessionActive(session.SessionId));
        AssertProductPlatformState(store, databaseEncoding);
        var schemaNine = AssertEncodedSchemaState(databasePath, databaseEncoding,
            expectedSchema: 9, expectedRankedTableCount: 2);
        var originalSnapshotChecksum = schemaNine.Checksum;
        var oldFinalBytes = File.ReadAllBytes(platformPath);
        var oldFinalChecksum = Utf8Sha256(oldFinalBytes);
        using (var initial = Open(databasePath))
            Assert.Equal(oldFinalChecksum, RequiredScalarText(initial,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));

        var pendingPath = platformPath + ".ranked-broadcast-schema8.pending";
        Exception? failure;
        using (var blocked = new MirrorPublicationFailureScope(platformPath))
            failure = Record.Exception(() =>
                L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(databasePath));
        Assert.True(failure is IOException or UnauthorizedAccessException,
            failure?.ToString() ?? "missing filesystem publication failure");

        var schemaEight = AssertEncodedSchemaState(databasePath, databaseEncoding,
            expectedSchema: 8, expectedRankedTableCount: 0);
        Assert.Equal(schemaNine.Json, schemaEight.Json);
        Assert.Equal(originalSnapshotChecksum, schemaEight.Checksum);
        Assert.True(File.Exists(pendingPath));
        var pendingChecksum = Utf8Sha256(File.ReadAllBytes(pendingPath));
        Assert.Equal(oldFinalBytes, File.ReadAllBytes(platformPath));
        using (var committed = Open(databasePath))
        {
            Assert.Equal(pendingChecksum, RequiredScalarText(committed,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));
            Assert.Equal(oldFinalChecksum, RequiredScalarText(committed, """
                SELECT value FROM storage_meta
                WHERE key='ranked_broadcast_schema8_previous_mirror_sha256';
                """));
        }

        L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(databasePath);
        Assert.False(File.Exists(pendingPath));
        var retriedSchemaEight = AssertEncodedSchemaState(databasePath, databaseEncoding,
            expectedSchema: 8, expectedRankedTableCount: 0);
        Assert.Equal(schemaEight, retriedSchemaEight);
        Assert.Equal(pendingChecksum, Utf8Sha256(File.ReadAllBytes(platformPath)));
        using (var published = Open(databasePath))
        {
            Assert.Equal(pendingChecksum, RequiredScalarText(published,
                "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));
            Assert.Null(OptionalScalarText(published, """
                SELECT value FROM storage_meta
                WHERE key='ranked_broadcast_schema8_previous_mirror_sha256';
                """));
        }

        SqliteConnection.ClearAllPools();
        var firstReopen = NewStore(platformPath, fixture);
        AssertMigrationStoreState(firstReopen, committedVersion, session.SessionId, expectedValue);
        AssertProductPlatformState(firstReopen, databaseEncoding);
        var firstSchemaNine = AssertEncodedSchemaState(databasePath, databaseEncoding,
            expectedSchema: 9, expectedRankedTableCount: 2);
        Assert.Equal(originalSnapshotChecksum, firstSchemaNine.Checksum);

        var secondReopen = NewStore(platformPath, fixture);
        AssertMigrationStoreState(secondReopen, committedVersion, session.SessionId, expectedValue);
        AssertProductPlatformState(secondReopen, databaseEncoding);
        var secondSchemaNine = AssertEncodedSchemaState(databasePath, databaseEncoding,
            expectedSchema: 9, expectedRankedTableCount: 2);
        Assert.Equal(firstSchemaNine, secondSchemaNine);
    }

    [Fact]
    public void SuccessfulSaveAndBeforeCommitFailurePreserveStateSessionAndVersionAfterReopen()
    {
        using var fixture = new RankedIdentityFixture();
        var registration = fixture.Store.Register("corestate", "Password123!");
        var session = fixture.Store.AuthenticateTokenSession(registration.Token!)!;
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            Content(Data(fixture.Store))["synthetic-core-state"] = "committed🙂";
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var committedVersion = fixture.Store.Version;
        Assert.True(fixture.Store.IsSessionActive(session.SessionId));
        AssertStoreState(Reopen(fixture), committedVersion, session.SessionId, "committed🙂");

        var reached = false;
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            reached = true;
            throw new IOException("synthetic core snapshot commit failure");
        };
        try
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                fixture.Store.ExecuteAdminTransaction(() =>
                {
                    Content(Data(fixture.Store))["synthetic-core-state"] = "must-not-commit";
                    RankedIdentityTestState.RequestSave(fixture.Store);
                    return true;
                }));
        }
        finally { fixture.Store.StorageFailureInjector = null; }

        Assert.True(reached);
        AssertStoreState(fixture.Store, committedVersion, session.SessionId, "committed🙂");
        AssertStoreState(Reopen(fixture), committedVersion, session.SessionId, "committed🙂");
    }

    private static void WarmSerialization(object data)
    {
        var legacy = LegacySnapshot(data);
        var legacyHash = LegacyChecksum(legacy);
        var prepared = SerializeCoreSnapshot(data, "UTF-8");
        GC.KeepAlive(legacyHash);
        GC.KeepAlive(prepared);
    }

    private static void VerifyStoredSnapshot(SqliteConnection connection, SqliteTransaction? transaction,
        string expectedJson, string expectedChecksum)
    {
        using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT typeof(json),json,checksum,json_valid(json) FROM snapshot;";
        using var reader = query.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal(expectedJson, reader.GetString(1));
        Assert.Equal(expectedChecksum, reader.GetString(2));
        Assert.Equal(1L, reader.GetInt64(3));
        Assert.False(reader.Read());
    }

    private static void AssertStoreState(L12PlatformStore store, long expectedVersion,
        string sessionId, string expectedValue)
    {
        Assert.Equal(expectedVersion, store.Version);
        Assert.True(store.IsSessionActive(sessionId));
        Assert.Equal(expectedValue,
            Assert.IsType<string>(Content(Data(store))["synthetic-core-state"]));
    }

    private static void AssertMigrationStoreState(L12PlatformStore store, long expectedVersion,
        string sessionId, string expectedValue)
    {
        Assert.Equal(expectedVersion, store.Version);
        Assert.True(store.IsSessionActive(sessionId));
        Assert.Equal(expectedValue,
            Assert.IsType<string>(Content(Data(store))["synthetic-utf16-reverse"]));
    }

    private static (string Json, string Checksum) AssertEncodedSchemaState(string databasePath,
        string expectedEncoding, long expectedSchema, long expectedRankedTableCount)
    {
        using var connection = Open(databasePath);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA encoding;";
            Assert.Equal(expectedEncoding.ToUpperInvariant(),
                Assert.IsType<string>(pragma.ExecuteScalar()).ToUpperInvariant());
        }
        using (var guard = connection.CreateCommand())
        {
            guard.CommandText = "SELECT value FROM encoding_guard;";
            Assert.Equal("preserve", Assert.IsType<string>(guard.ExecuteScalar()));
        }
        using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT schema_version,typeof(snapshot_json),snapshot_json,snapshot_sha256,
                   json_valid(snapshot_json)
            FROM platform_state WHERE singleton_id=1;
            """;
        using var reader = query.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(expectedSchema, reader.GetInt64(0));
        Assert.Equal("text", reader.GetString(1));
        var json = reader.GetString(2);
        var checksum = reader.GetString(3);
        Assert.Equal(1L, reader.GetInt64(4));
        Assert.False(reader.Read());
        Assert.Equal(Utf8Sha256(Encoding.UTF8.GetBytes(json)), checksum);
        reader.Close();
        Assert.Equal(expectedSchema.ToString(), RequiredScalarText(connection,
            "SELECT value FROM storage_meta WHERE key='schema_version';"));
        using var tables = connection.CreateCommand();
        tables.CommandText = """
            SELECT COUNT(*) FROM sqlite_schema
            WHERE type='table' AND name IN
                ('ranked_broadcast_definitions','ranked_broadcast_deliveries');
            """;
        Assert.Equal(expectedRankedTableCount, Convert.ToInt64(tables.ExecuteScalar()));
        return (json, checksum);
    }

    private static string RequiredScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private static string? OptionalScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Assert.IsType<string>(value);
    }

    private static void AssertProductPlatformState(L12PlatformStore store, string expectedEncoding)
    {
        using var connection = Open(store.TransactionalStoragePath);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA encoding;";
            Assert.Equal(expectedEncoding.ToUpperInvariant(),
                Assert.IsType<string>(pragma.ExecuteScalar()).ToUpperInvariant());
        }
        using (var guard = connection.CreateCommand())
        {
            guard.CommandText = "SELECT value FROM encoding_guard;";
            Assert.Equal("preserve", Assert.IsType<string>(guard.ExecuteScalar()));
        }
        using var query = connection.CreateCommand();
        query.CommandText =
            "SELECT typeof(snapshot_json),snapshot_json,snapshot_sha256,json_valid(snapshot_json) " +
            "FROM platform_state WHERE singleton_id=1;";
        using var reader = query.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        var json = reader.GetString(1);
        var checksum = reader.GetString(2);
        Assert.Equal(1L, reader.GetInt64(3));
        Assert.False(reader.Read());
        Assert.Equal(Utf8Sha256(Encoding.UTF8.GetBytes(json)), checksum);
        Assert.Equal(LegacyChecksum(json), checksum);
        Assert.Equal(LegacySnapshot(Data(store)), json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(store.Version, document.RootElement.GetProperty("Version").GetInt64());
    }

    private static L12PlatformStore Reopen(RankedIdentityFixture fixture)
        => new(fixture.Path, fixture.Catalog.PresetDecks, officialCards: fixture.Catalog.Cards);

    private static L12PlatformStore NewStore(string path, RankedIdentityFixture fixture)
        => new(path, fixture.Catalog.PresetDecks, officialCards: fixture.Catalog.Cards);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;

    private static IDictionary Content(object data)
        => (IDictionary)data.GetType().GetProperty("Content")!.GetValue(data)!;

    private static string RawJson(object data) => JsonSerializer.Serialize(data, data.GetType());

    private static string LegacySnapshot(object data)
        => (string)StaticMethod("SerializeSnapshot", 1).Invoke(null, new[] { data })!;

    private static string LegacyChecksum(string json)
        => (string)StaticMethod("Sha256", 1).Invoke(null, new object[] { json })!;

    private static object SerializeCoreSnapshot(object data, string databaseEncoding)
        => StaticMethod("SerializeCoreSnapshot", 2).Invoke(null, new[] { data, databaseEncoding })!;

    private static object PrepareCoreSnapshot(object data, SqliteConnection connection,
        SqliteTransaction transaction)
        => StaticMethod("PrepareCoreSnapshot", 3).Invoke(null, new object[] { data, connection, transaction })!;

    private static MethodInfo StaticMethod(string name, int parameterCount)
        => typeof(L12PlatformStore).GetMethods(PrivateStatic).Single(method => method.Name == name
            && method.GetParameters().Length == parameterCount);

    private static object JsonValue(object snapshot)
        => snapshot.GetType().GetProperty("JsonValue",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(snapshot)!;

    private static string Checksum(object snapshot)
        => (string)snapshot.GetType().GetProperty("Checksum",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(snapshot)!;

    private static string Utf8Sha256(byte[] utf8)
        => Convert.ToHexString(SHA256.HashData(utf8)).ToLowerInvariant();
}
