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
public sealed class SeasonRefreshIncidentRegressionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> SeasonSnapshotEncodingCases()
    {
        foreach (var encoding in new[] { "UTF-8", "UTF-16le", "UTF-16be" })
            foreach (var scenario in new[] { "unicode", "empty", "whitespace", "null", "zero", "bad-json",
                         "trailing", "bom", "blob-body", "bad-checksum", "invalid-version", "duplicate-session", "missing", "null-body" })
                yield return new object[] { encoding, scenario };
    }

    [Theory]
    [MemberData(nameof(SeasonSnapshotEncodingCases))]
    public void PreparedSeasonSnapshotPreservesEncodingValidationAndClearsInput(string encoding, string scenario)
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA encoding='{encoding}'; CREATE TABLE platform_state(singleton_id INTEGER PRIMARY KEY,snapshot_json TEXT,snapshot_sha256 TEXT);";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA encoding;";
        Assert.Equal(encoding, command.ExecuteScalar());
        var json = scenario switch
        {
            "empty" => "",
            "whitespace" => " \r\n\t\u2003",
            "null" => "null",
            "zero" => "0",
            "bad-json" or "bad-checksum" => "{\"Version\":",
            "trailing" => "{}{}",
            "bom" => "\uFEFF{}",
            "invalid-version" => "{\"Version\":-1}",
            "duplicate-session" => "{\"Sessions\":[{\"Id\":\"duplicate\"},{\"Id\":\"duplicate\"}]}",
            _ => "{\"Version\":0,\"BusinessVersion\":0,\"Content\":{\"unicode\":\"汉字é é 😀\\u0000\\u2028\\ud83d\\ude00\"}}",
        };
        command.CommandText = "INSERT INTO platform_state VALUES(1,$json,$sha);";
        command.Parameters.AddWithValue("$json", scenario == "null-body" ? DBNull.Value
            : scenario == "blob-body" ? System.Text.Encoding.UTF8.GetBytes(json) : json);
        command.Parameters.AddWithValue("$sha", scenario == "bad-checksum" ? "invalid" : CallPrivate("Sha256", json)!);
        command.ExecuteNonQuery();
        if (scenario == "missing")
        {
            command.CommandText = "DELETE FROM platform_state;";
            command.ExecuteNonQuery();
        }
        command.CommandText = "PRAGMA query_only=ON;";
        command.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction(deferred: true);
        object? oldData = null, newData = null;
        var oldError = Record.Exception(() =>
        {
            var stored = CallPrivate("ReadSnapshot", connection, transaction)
                ?? throw new InvalidDataException("事务存储缺少平台快照");
            var text = (string)stored.GetType().GetField("Item1")!.GetValue(stored)!;
            var checksum = (string)stored.GetType().GetField("Item2")!.GetValue(stored)!;
            if (!string.Equals(checksum, (string)CallPrivate("Sha256", text)!, StringComparison.Ordinal))
                throw new InvalidDataException("SQLite 平台快照校验和不匹配");
            oldData = CallPrivate("DeserializeDataAndValidate", text);
        });
        byte[]? inputBytes = null;
        IDisposable? input = null;
        var newError = Record.Exception(() =>
        {
            input = (IDisposable)CallPrivate("PrepareSeasonSnapshotInput", connection, transaction)!;
            inputBytes = (byte[]?)input.GetType().GetField("_utf8", PrivateInstance)!.GetValue(input);
            Assert.Equal(encoding == "UTF-8" && scenario != "blob-body", inputBytes is not null);
            newData = CallInput(input);
        });
        if (oldError is null)
        {
            Assert.Null(newError);
            Assert.NotNull(oldData); Assert.NotNull(newData);
            var options = (JsonSerializerOptions)typeof(L12PlatformStore)
                .GetField("PlatformMigrationJsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(oldData, options), JsonSerializer.SerializeToUtf8Bytes(newData, options));
            Assert.True(scenario is "unicode" or "null" or "blob-body"); // Numeric zero must not become JSON null/default data.
        }
        else
        {
            Assert.NotNull(newError);
            Assert.Equal(oldError.GetType(), newError.GetType());
            if (scenario == "bad-checksum") Assert.IsType<InvalidDataException>(newError); // Before malformed JSON parsing.
        }
        if (input is not null)
        {
            Assert.Null(input.GetType().GetField("_utf8", PrivateInstance)!.GetValue(input));
            if (inputBytes is not null) Assert.All(inputBytes, value => Assert.Equal(0, value));
            input.Dispose(); input.Dispose();
            Assert.Throws<ObjectDisposedException>(() => CallInput(input));
        }
    }

    [Theory]
    [InlineData(false, "before-season-finalization-commit")]
    [InlineData(true, "before-season-finalization-commit")]
    [InlineData(false, "after-season-rollback-serialize")]
    [InlineData(true, "after-season-rollback-serialize")]
    public void DueReaderRollbackAndRetryPreserveDurableAuthority(bool newerAuthority, string stage)
    {
        using var fixture = new Fixture();
        var now = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        fixture.SetDeadline(now);
        var reader = new L12PlatformStore(fixture.Path);
        var oldCache = Cache(reader);
        if (newerAuthority) fixture.Store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        var facts = Facts(fixture.Path, reader);
        var reached = false;
        reader.StorageFailureInjector = current =>
        {
            if (current != stage) return;
            reached = true;
            throw new OutOfMemoryException("synthetic bounded due-reader failure");
        };
        try
        {
            var failure = Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                reader.TryClaimDueSeasonFinalization("retry", now, TimeSpan.FromMinutes(1)));
            Assert.IsType<OutOfMemoryException>(failure.InnerException);
        }
        finally { reader.StorageFailureInjector = null; }
        Assert.True(reached);
        Assert.Equal(facts, Facts(fixture.Path, reader));
        Assert.Same(oldCache, Cache(reader));
        if (newerAuthority)
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() => reader.EffectiveOperationsPolicy());
            Assert.Throws<L12PlatformStorageUnavailableException>(() => reader.IsSessionActive(fixture.Owner.SessionId));
        }
        else Assert.True(reader.IsSessionActive(fixture.Owner.SessionId));
        Assert.NotNull(reader.TryClaimDueSeasonFinalization("retry", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(facts.Revision + 1, Facts(fixture.Path, reader).Revision);
        Assert.Equal(!newerAuthority, reader.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(Facts(fixture.Path, reader), Facts(fixture.Path, new L12PlatformStore(fixture.Path)));
    }

    [Fact]
    public void PreparedSeasonSnapshotKeepsCompleteValidationAndReducesUtf8Allocations()
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var state = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(fixture.Store)!;
            var content = (IDictionary)state.GetType().GetProperty("Content")!.GetValue(state)!;
            content["synthetic-complete-read"] = new string('x', 4 * 1024 * 1024);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var prepare = typeof(L12PlatformStore).GetMethod("PrepareSeasonSnapshotInput", BindingFlags.Static | BindingFlags.NonPublic);
        var completeOptions = (JsonSerializerOptions)typeof(L12PlatformStore)
            .GetField("PlatformMigrationJsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object Legacy()
        {
            var stored = CallPrivate("ReadSnapshot", connection, transaction)!;
            var json = (string)stored.GetType().GetField("Item1")!.GetValue(stored)!;
            Assert.Equal(stored.GetType().GetField("Item2")!.GetValue(stored), CallPrivate("Sha256", json));
            return CallPrivate("DeserializeDataAndValidate", json)!;
        }
        object Prepared()
        {
            // RED on the original implementation measures its real complete read,
            // not a missing-method assertion or a production reader switch.
            if (prepare is null) return Legacy();
            using var input = (IDisposable)prepare.Invoke(null, new object[] { connection, transaction })!;
            return input.GetType().GetMethod("DeserializeAndValidate")!.Invoke(input, null)!;
        }
        _ = Legacy(); _ = Prepared();
        var expected = JsonSerializer.SerializeToUtf8Bytes(Legacy(), completeOptions);
        long legacyBytes = 0, preparedBytes = 0;
        for (var sample = 0; sample < 3; sample++)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            var oldData = Legacy();
            legacyBytes += GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            var newData = Prepared();
            preparedBytes += GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.Equal(expected, JsonSerializer.SerializeToUtf8Bytes(oldData, completeOptions));
            Assert.Equal(expected, JsonSerializer.SerializeToUtf8Bytes(newData, completeOptions));
        }
        output.WriteLine("season_complete_read samples=3 payload_bytes=4194304 legacy_allocated_bytes={0} prepared_allocated_bytes={1} prepared_entry_present={2}",
            legacyBytes, preparedBytes, prepare is not null);
        Assert.True(preparedBytes * 100 < legacyBytes * 85,
            $"Expected bounded complete-reader allocation improvement: old={legacyBytes}, candidate={preparedBytes}");
    }

    [Theory]
    [InlineData(16 * 1024, 1)]
    [InlineData(4 * 1024 * 1024, 1)]
    [InlineData(4 * 1024 * 1024, 128)]
    public void DueReadAttributionPreservesAuthorityAndComparesOnlyEquivalentCodecs(int payloadBytes, int parts)
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var state = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(fixture.Store)!;
            var content = (IDictionary)state.GetType().GetProperty("Content")!.GetValue(state)!;
            var random = new Random(20261030);
            for (var part = 0; part < parts; part++)
            {
                var bytes = new byte[payloadBytes / parts * 3 / 4];
                random.NextBytes(bytes);
                content["synthetic-due-" + part] = Convert.ToBase64String(bytes);
            }
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var now = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        fixture.SetDeadline(now);
        var before = Facts(fixture.Path, fixture.Store);
        using (var measurement = L12PlatformStore.BeginSyntheticStorageMeasurement())
        {
            Assert.NotNull(Measure("real-transaction", "due-claim", () =>
                fixture.Store.TryClaimDueSeasonFinalization("probe", now, TimeSpan.FromMinutes(1))));
            var native = measurement.Complete();
            Assert.Equal(0, native.InstrumentationErrors);
            Assert.True(native.SqlitePageWrites > 0);
            Assert.True(native.MirrorBytes > 0);
            output.WriteLine("due_native payload_bytes={0} parts={1} data={2}", payloadBytes, parts, JsonSerializer.Serialize(native));
        }
        var durable = Facts(fixture.Path, fixture.Store);
        Assert.Equal(before.Revision + 1, durable.Revision);
        var cache = Cache(fixture.Store);
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var options = (JsonSerializerOptions)typeof(L12PlatformStore).GetField("PlatformSnapshotJsonOptions", statics)!.GetValue(null)!;
        var completeOptions = (JsonSerializerOptions)typeof(L12PlatformStore).GetField("PlatformMigrationJsonOptions", statics)!.GetValue(null)!;
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            using var readMeasurement = L12PlatformStore.BeginSyntheticStorageMeasurement();
            var row = Measure("isolated", "read-text", () => Invoke("ReadSnapshot", connection, transaction))!;
            var json = (string)row.GetType().GetField("Item1")!.GetValue(row)!;
            var checksum = (string)row.GetType().GetField("Item2")!.GetValue(row)!;
            Assert.Equal(checksum, (string)Measure("isolated", "incremental-sha", () => Invoke("Sha256", json))!);
            var data = Measure("isolated", "deserialize-normalize-validate", () => Invoke("DeserializeDataAndValidate", json))!;
            var expectedCoreHash = Hash(data);
            Measure("isolated", "hydrate-decks", () => Invoke("HydrateDeckDomain", connection, data, transaction));
            Measure("isolated", "hydrate-broadcasts", () => Invoke("HydrateRankedBroadcastObjects", connection, data, transaction));
            Measure("isolated", "merge-audit", () => Invoke("MergeIndependentAudit", connection, data, transaction, true));
            var rollback = (byte[])Measure("isolated", "rollback-gzip", () => Invoke("SerializeRollbackState", data))!;
            Measure("isolated", "indexes", () => typeof(L12PlatformStore).GetMethod("PrepareCommittedSessionActivityMeasured", PrivateInstance)!
                .Invoke(fixture.Store, new[] { data, rollback }));
            // Test-only codecs: ALL normalize, NONE runs full validation.
            // Their comparison is not a qualified replacement authority reader.
            for (var sample = 0; sample < 3; sample++)
            {
                var text = Measure("codec-only", "text-sha-normalize", () =>
                {
                    var stored = Invoke("ReadSnapshot", connection, transaction)!;
                    var body = (string)stored.GetType().GetField("Item1")!.GetValue(stored)!;
                    Assert.Equal(checksum, (string)Invoke("Sha256", body)!);
                    return Invoke("DeserializeData", body);
                }, sample)!;
                var streamed = Measure("codec-only", "blob-sha-normalize", () =>
                {
                    using var header = connection.CreateCommand();
                    header.Transaction = transaction;
                    header.CommandText = "SELECT snapshot_sha256 FROM platform_state WHERE singleton_id=1;";
                    var expected = Assert.IsType<string>(header.ExecuteScalar());
                    using (var blob = new SqliteBlob(connection, "platform_state", "snapshot_json", 1, readOnly: true))
                        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant());
                    using var body = new SqliteBlob(connection, "platform_state", "snapshot_json", 1, readOnly: true);
                    var decoded = JsonSerializer.Deserialize(body, data.GetType(), options)!;
                    return Invoke("NormalizeDeserializedData", decoded);
                }, sample)!;
                var buffered = Measure("codec-only", "exact-utf8-sha-normalize", () =>
                {
                    using var header = connection.CreateCommand();
                    header.Transaction = transaction;
                    header.CommandText = "SELECT snapshot_sha256 FROM platform_state WHERE singleton_id=1;";
                    var expected = Assert.IsType<string>(header.ExecuteScalar());
                    using var body = new SqliteBlob(connection, "platform_state", "snapshot_json", 1, readOnly: true);
                    var utf8 = new byte[checked((int)body.Length)];
                    try
                    {
                        body.ReadExactly(utf8);
                        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(utf8)).ToLowerInvariant());
                        var decoded = JsonSerializer.Deserialize(utf8.AsSpan(), data.GetType(), options)!;
                        return Invoke("NormalizeDeserializedData", decoded);
                    }
                    finally { CryptographicOperations.ZeroMemory(utf8); }
                }, sample)!;
                Assert.Equal(expectedCoreHash, Hash(text));
                Assert.Equal(expectedCoreHash, Hash(streamed));
                Assert.Equal(expectedCoreHash, Hash(buffered));
            }
            Assert.Equal(0, readMeasurement.Complete().SqlitePageWrites);
            GC.KeepAlive(json); // Allocation only, not live-heap/RSS benefit.
        }
        Assert.Equal(durable, Facts(fixture.Path, fixture.Store));
        Assert.Same(cache, Cache(fixture.Store));
        Assert.True(fixture.Store.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(durable, Facts(fixture.Path, new L12PlatformStore(fixture.Path)));

        string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), completeOptions)));
        object? Invoke(string name, params object?[] args) => typeof(L12PlatformStore).GetMethods(statics)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(null, args);
        object? Measure(string scope, string phase, Func<object?> action, int sample = 0)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var tick = Stopwatch.GetTimestamp();
            var result = action();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            var elapsed = Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
            output.WriteLine("due_phase scope={0} payload_bytes={1} parts={2} sample={3} phase={4} allocated_bytes={5} elapsed_ms={6:F3}",
                scope, payloadBytes, parts, sample, phase, allocated, elapsed);
            GC.KeepAlive(result);
            return result;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnotherStoresDeadlineChangeCannotBeHiddenByWarmLocalSeasonState(bool becomesDue)
    {
        using var fixture = new Fixture();
        var now = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        fixture.SetDeadline(becomesDue ? now.AddMinutes(1) : now.AddSeconds(-1));
        var writer = new L12PlatformStore(fixture.Path);
        var admin = writer.Login("Admin", "L12master").Account!;
        var operations = writer.OperationsConfig(admin);
        var target = becomesDue ? now.AddSeconds(-1) : now.AddMinutes(1);
        writer.ApplyOperationsConfig(admin, operations.Config with
        {
            Season = operations.Config.Season with { EndsAt = target },
        }, operations.Version, "synthetic concurrent deadline",
            new L12AdminAuditContext("synthetic-season-deadline", "admin.operations.write"));
        var claim = fixture.Store.TryClaimDueSeasonFinalization("probe", now, TimeSpan.FromMinutes(1));
        Assert.Equal(becomesDue, claim is not null);
        Assert.Equal(target, fixture.Store.OperationsConfig(admin).Config.Season.EndsAt);
        Assert.True(fixture.Store.IsSessionActive(fixture.Owner.SessionId));
    }

    [Fact]
    public void VerifiedWarmNotDueCheckPreservesCacheAndAvoidsGlobalRebuild()
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var state = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(fixture.Store)!;
            var content = (IDictionary)state.GetType().GetProperty("Content")!.GetValue(state)!;
            content["synthetic-noop-comparison"] = new string('x', 4 * 1024 * 1024);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        Assert.Null(fixture.Store.TryClaimDueSeasonFinalization("probe", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
        var cache = Cache(fixture.Store);
        var before = Facts(fixture.Path, fixture.Store);
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-session-index-build")
                throw new InvalidOperationException("verified no-op must not rebuild global state");
        };
        var warmStart = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                Assert.Null(fixture.Store.TryClaimDueSeasonFinalization("probe", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
                Assert.Same(cache, Cache(fixture.Store));
            }
        }
        finally { fixture.Store.StorageFailureInjector = null; }
        var warm = GC.GetAllocatedBytesForCurrentThread() - warmStart;
        var anchor = typeof(L12PlatformStore).GetField("_rankedBroadcastReadAnchor", PrivateInstance)!;
        var coldStart = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 4; i++)
        {
            anchor.SetValue(fixture.Store, null); // Force the original full-authority path in the same binary.
            Assert.Null(fixture.Store.TryClaimDueSeasonFinalization("probe", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
        }
        var cold = GC.GetAllocatedBytesForCurrentThread() - coldStart;
        Assert.Equal(before, Facts(fixture.Path, fixture.Store));
        output.WriteLine("season_warm_comparison iterations=4 payload_bytes=4194304 warm_allocated_bytes={0} forced_full_allocated_bytes={1}", warm, cold);
        Assert.True(cold > 4 * 1024 * 1024);
        Assert.True(warm * 10 < cold * 3);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("checksum")]
    [InlineData("missing")]
    [InlineData("newer-revocation")]
    public void WarmNotDueCheckCannotHideChangedOrUnreadableAuthority(string fault)
    {
        using var fixture = new Fixture();
        Assert.Null(fixture.Store.TryClaimDueSeasonFinalization("probe", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
        if (fault == "newer-revocation")
        {
            var writer = new L12PlatformStore(fixture.Path);
            Assert.True(writer.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId).Found);
            Assert.Null(fixture.Store.TryClaimDueSeasonFinalization("probe", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
            Assert.False(fixture.Store.IsSessionActive(fixture.Owner.SessionId));
            return;
        }
        SqliteConnection.ClearAllPools();
        if (fault == "missing")
            File.Move(fixture.Store.TransactionalStoragePath, fixture.Store.TransactionalStoragePath + ".held");
        else
        {
            using var connection = new SqliteConnection($"Data Source={fixture.Store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = fault == "body"
                ? "UPDATE platform_state SET snapshot_json=snapshot_json||' ';"
                : "UPDATE platform_state SET snapshot_sha256='invalid';";
            command.ExecuteNonQuery();
        }
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.TryClaimDueSeasonFinalization(
            "probe", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.IsSessionActive(fixture.Owner.SessionId));
    }

    // Injected allocation failure, NOT a claim that physical OOM was recreated.
    [Theory]
    [InlineData(false, "before-session-index-build")]
    [InlineData(false, "after-session-index-build")]
    [InlineData(true, "before-session-index-build")]
    [InlineData(true, "after-session-index-build")]
    [InlineData(false, "before-ranked-broadcast-read-anchor")]
    [InlineData(true, "before-ranked-broadcast-read-anchor")]
    public void FailedNoOpRefreshAndVerifiedRetryKeepHealthAndAuthenticationConsistent(
        bool newerAuthority, string stage)
    {
        using var fixture = new Fixture();
        var now = fixture.PrepareClaimedSeason();
        var reader = new L12PlatformStore(fixture.Path);
        var originalCache = Cache(reader);
        if (newerAuthority) fixture.Store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        var facts = Facts(fixture.Path, reader);
        var reached = false;
        reader.StorageFailureInjector = current =>
        {
            if (current != stage) return;
            reached = true;
            throw new OutOfMemoryException("synthetic bounded allocation-failure injection");
        };
        try
        {
            var failure = Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                reader.TryClaimDueSeasonFinalization("other", now.AddSeconds(1), TimeSpan.FromMinutes(1)));
            Assert.IsType<OutOfMemoryException>(failure.InnerException);
        }
        finally { reader.StorageFailureInjector = null; }
        Assert.True(reached);
        Assert.Same(originalCache, Cache(reader));
        Assert.Equal(facts, Facts(fixture.Path, reader));
        if (newerAuthority)
        {
            // Both paths use the same unavailable generation; no health-only success claim.
            Assert.Throws<L12PlatformStorageUnavailableException>(() => reader.EffectiveOperationsPolicy());
            Assert.Throws<L12PlatformStorageUnavailableException>(() => reader.IsSessionActive(fixture.Owner.SessionId));
        }
        else
        {
            _ = reader.EffectiveOperationsPolicy();
            Assert.True(reader.IsSessionActive(fixture.Owner.SessionId));
        }
        Assert.Null(reader.TryClaimDueSeasonFinalization("other", now.AddSeconds(1), TimeSpan.FromMinutes(1)));
        _ = reader.EffectiveOperationsPolicy();
        Assert.Equal(!newerAuthority, reader.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(facts, Facts(fixture.Path, reader));
        var restarted = new L12PlatformStore(fixture.Path);
        Assert.Equal(!newerAuthority, restarted.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData(16 * 1024)]
    [InlineData(4 * 1024 * 1024)]
    public void RealNotDueAndContendedLeaseTransactionsReportNoOpCostWithoutDurableChanges(int payloadBytes)
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var state = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(fixture.Store)!;
            var content = (IDictionary)state.GetType().GetProperty("Content")!.GetValue(state)!;
            var bytes = new byte[payloadBytes * 3 / 4];
            new Random(20261030).NextBytes(bytes);
            content["synthetic-season-noop-probe"] = Convert.ToBase64String(bytes);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var now = fixture.PrepareClaimedSeason();
        foreach (var due in new[] { false, true })
        {
            // Before the deadline: pure no-op. After it: another worker owns the live lease.
            var observedAt = due ? now.AddSeconds(1) : now.AddSeconds(-1);
            var before = Facts(fixture.Path, fixture.Store);
            for (var sample = 0; sample < 3; sample++)
            {
                var points = new List<(string Stage, long Bytes, long Tick)>(8);
                var cache = Cache(fixture.Store);
                using var measurement = L12PlatformStore.BeginSyntheticStorageMeasurement();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var tick = Stopwatch.GetTimestamp();
                fixture.Store.StorageFailureInjector = stage => points.Add((stage,
                    GC.GetAllocatedBytesForCurrentThread(), Stopwatch.GetTimestamp()));
                try
                {
                    Assert.Null(fixture.Store.TryClaimDueSeasonFinalization(
                        "other", observedAt, TimeSpan.FromMinutes(1)));
                }
                finally { fixture.Store.StorageFailureInjector = null; }
                var elapsed = Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                var native = measurement.Complete();
                Assert.Equal(0, native.InstrumentationErrors);
                Assert.Equal(before, Facts(fixture.Path, fixture.Store));
                Assert.True(fixture.Store.IsSessionActive(fixture.Owner.SessionId));
                output.WriteLine("season_noop payload_bytes={0} lease_contended={1} sample={2} elapsed_ms={3:F3} allocated_bytes={4} cache_replaced={5} sqlite_page_writes={6}",
                    payloadBytes, due, sample, elapsed, allocated, !ReferenceEquals(cache, Cache(fixture.Store)),
                    native.SqlitePageWrites);
                foreach (var point in points)
                    output.WriteLine("season_noop_point stage={0} allocated_from_start={1} elapsed_ms={2:F3}",
                        point.Stage, point.Bytes - bytes, Stopwatch.GetElapsedTime(tick, point.Tick).TotalMilliseconds);
            }
        }
        var restarted = new L12PlatformStore(fixture.Path);
        Assert.True(restarted.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(Facts(fixture.Path, fixture.Store), Facts(fixture.Path, restarted));
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? CallInput(IDisposable input)
    {
        try { return input.GetType().GetMethod("DeserializeAndValidate")!.Invoke(input, null); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    private static object? CallPrivate(string name, params object?[] args)
    {
        try
        {
            return typeof(L12PlatformStore).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(null, args);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    private static byte[] Cache(L12PlatformStore store) => (byte[])typeof(L12PlatformStore)
        .GetField("_lastCommittedSnapshot", PrivateInstance)!.GetValue(store)!;

    private static (long Revision, string CoreChecksum, string MirrorChecksum, string Lease) Facts(
        string mirrorPath, L12PlatformStore store)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT storage_revision,snapshot_sha256 FROM platform_state WHERE singleton_id=1;";
        long revision; string core;
        using (var row = command.ExecuteReader())
        {
            Assert.True(row.Read()); revision = row.GetInt64(0); core = row.GetString(1);
        }
        command.CommandText = "SELECT group_concat(definition_id || ':' || season_id || ':' || status || ':' || ifnull(lease_owner,'') || ':' || ifnull(lease_expires_utc,''),'|') FROM season_finalization_coordination;";
        var lease = command.ExecuteScalar() as string ?? "";
        using var mirror = File.OpenRead(mirrorPath);
        return (revision, core, Convert.ToHexString(SHA256.HashData(mirror)), lease);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-season-refresh", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AuthenticatedSession Owner { get; }
        private L12AccountView Admin { get; }
        internal Fixture()
        {
            Store = new L12PlatformStore(Path);
            Admin = Store.Login("Admin", "L12master").Account!;
            Owner = Store.AuthenticateTokenSession(Store.Register("seasonprobe", "password-123").Token)!;
        }
        internal DateTimeOffset PrepareClaimedSeason()
        {
            var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            SetDeadline(endsAt);
            Assert.NotNull(Store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1)));
            return endsAt;
        }
        internal void SetDeadline(DateTimeOffset endsAt)
        {
            var operations = Store.OperationsConfig(Admin);
            Store.ApplyOperationsConfig(Admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = endsAt },
            }, operations.Version, "synthetic season deadline",
                new L12AdminAuditContext("synthetic-season-probe", "admin.operations.write"));
        }
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
