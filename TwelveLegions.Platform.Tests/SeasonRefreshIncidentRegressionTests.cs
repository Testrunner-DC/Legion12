using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SeasonRefreshIncidentRegressionTests(ITestOutputHelper output)
{
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
