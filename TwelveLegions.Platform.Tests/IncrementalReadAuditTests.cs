using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class IncrementalReadAuditTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly L12AdminAuditContext Context = new("synthetic-read-audit", "admin.analytics.read");
    private static void Append(L12PlatformStore store, L12AccountView actor, string target = "synthetic")
        => store.RecordAdminRead(actor, L12Permission.AdminAnalyticsRead, "analytics", "read-card-list", target, Context);

    [Fact]
    public void ReadAuditChangesOnlyTheCanonicalLedgerNotCoreMirrorOrRollback()
    {
        using var f = new RankedIdentityFixture();
        var version = f.Store.Version;
        var mirror = File.ReadAllBytes(f.Path);
        var rollback = RankedIdentityTestState.RollbackCache(f.Store);
        var core = Scalar(f.Store, "SELECT snapshot_sha256 || ':' || storage_revision || ':' || business_version || ':' || updated_utc FROM platform_state;");
        Append(f.Store, f.Admin);
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(core, Scalar(f.Store, "SELECT snapshot_sha256 || ':' || storage_revision || ':' || business_version || ':' || updated_utc FROM platform_state;"));
        Assert.Equal(mirror, File.ReadAllBytes(f.Path));
        Assert.Same(rollback, RankedIdentityTestState.RollbackCache(f.Store));
        Assert.Single(f.Store.AdminAudit(category: "analytics"), row => row.Target == "synthetic");
        var restarted = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        Assert.Single(restarted.AdminAudit(category: "analytics"), row => row.Target == "synthetic");
    }

    [Fact]
    public void IndependentReadDoesNotRunTheFullSaveFailureHooks()
    {
        using var f = new RankedIdentityFixture();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage is "before-mirror-serialize" or "after-rollback-serialize")
                throw new IOException("full platform work is forbidden for isolated reads");
        };
        Append(f.Store, f.Admin);
        Assert.Single(f.Store.AdminAudit(category: "analytics"));
    }

    [Fact]
    public async Task BlockedPlatformWriterDoesNotBlockAuditOnThePlatformGate()
    {
        using var f = new RankedIdentityFixture();
        var adminToken = f.Store.Login("Admin", "L12master").Token;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic read audit barrier");
        };
        var writer = Task.Factory.StartNew(() => f.Store.Register("auditwriter", "Password123!"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task? read = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            read = Task.Run(() => Append(f.Store, f.Admin));
            await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(async () =>
                await read.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.NotNull(f.Store.AuthenticateTokenSession(adminToken));
        }
        finally
        {
            release.Set();
            Assert.True((await writer).Success);
            if (read is not null) { try { await read; } catch (L12PlatformStorageUnavailableException) { } }
            f.Store.StorageFailureInjector = null;
        }
    }

    [Fact]
    public void StaleInstanceAndRevokedPermissionCannotAppendAReadAudit()
    {
        using var f = new RankedIdentityFixture();
        var stale = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        f.Store.Register("auditnew", "Password123!");
        Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() => Append(stale, f.Admin));
        f.Store.ExecuteAdminTransaction(() =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(f.Store)!;
            var accounts = (System.Collections.IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
            var account = accounts.Cast<object>().Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == f.Admin.Id);
            account.GetType().GetProperty("Role")!.SetValue(account, "player");
            RankedIdentityTestState.RequestSave(f.Store);
            return true;
        });
        Assert.Throws<UnauthorizedAccessException>(() => Append(f.Store, f.Admin));
    }

    [Fact]
    public void QueryRejectsMetadataThatDisagreesWithChecksummedPayload()
    {
        using var f = new RankedIdentityFixture();
        Append(f.Store, f.Admin);
        Execute(f.Store, "UPDATE admin_audit_events SET category='altered' WHERE category='analytics';");
        Assert.Throws<InvalidDataException>(() => f.Store.AdminAudit(category: "analytics"));
    }

    private static object? Scalar(L12PlatformStore store, string sql)
    {
        using var db = Open(store); using var command = db.CreateCommand();
        command.CommandText = sql; return command.ExecuteScalar();
    }
    private static void Execute(L12PlatformStore store, string sql)
    {
        using var db = Open(store); using var command = db.CreateCommand();
        command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static SqliteConnection Open(L12PlatformStore store)
    {
        var db = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
        db.Open(); return db;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAndAppendFailureDoNotInvalidateTheBusinessGeneration(bool cancel)
    {
        using var f = new RankedIdentityFixture();
        var rollback = RankedIdentityTestState.RollbackCache(f.Store);
        var generation = RankedIdentityTestState.CommittedProjection(f.Store);
        var before = Convert.ToInt64(Scalar(f.Store, "SELECT count(*) FROM admin_audit_events;"));
        using var cancellation = new CancellationTokenSource();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-read-audit-commit") return;
            if (cancel) cancellation.Cancel(); else throw new IOException("synthetic isolated append failure");
        };
        void Read() => f.Store.RecordAdminRead(f.Admin, L12Permission.AdminAnalyticsRead,
            "analytics", "read-card-list", "synthetic", Context, cancellation.Token);
        if (cancel) Assert.Throws<OperationCanceledException>(Read);
        else Assert.Throws<L12PlatformStorageUnavailableException>(Read);
        Assert.Equal(before, Convert.ToInt64(Scalar(f.Store, "SELECT count(*) FROM admin_audit_events;")));
        Assert.Same(rollback, RankedIdentityTestState.RollbackCache(f.Store));
        Assert.Same(generation, RankedIdentityTestState.CommittedProjection(f.Store));
        f.Store.StorageFailureInjector = null;
        Append(f.Store, f.Admin);
    }

    [Fact]
    public void FullSaveAbsorbsIncrementalReadAndArchiveCannotResurrectIt()
    {
        using var f = new RankedIdentityFixture();
        Append(f.Store, f.Admin);
        var row = Assert.Single(f.Store.AdminAudit(category: "analytics"));
        f.Store.Register("auditabsorb", "Password123!");
        Assert.Contains(row.Id, File.ReadAllText(f.Path));
        f.Store.RunAuditLifecycle(DateTimeOffset.UtcNow.AddDays(32), []);
        Assert.DoesNotContain(f.Store.AdminAudit(), item => item.Id == row.Id);
        f.Store.Register("auditagain", "Password123!");
        Assert.DoesNotContain(row.Id, File.ReadAllText(f.Path));
        Assert.DoesNotContain(new L12PlatformStore(f.Path).AdminAudit(), item => item.Id == row.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateOrMigratedReadIdNeverReportsSuccess(bool migrated)
    {
        using var f = new RankedIdentityFixture();
        Append(f.Store, f.Admin);
        var row = Assert.Single(f.Store.AdminAudit(category: "analytics"));
        var payload = (string)Scalar(f.Store, "SELECT payload_json FROM admin_audit_events WHERE category='analytics';")!;
        var rowType = typeof(L12PlatformStore).GetNestedType("AdminAuditRow", BindingFlags.NonPublic)!;
        var body = System.Text.Json.JsonSerializer.Deserialize(payload, rowType)!;
        if (migrated)
        {
            Execute(f.Store, $"INSERT INTO admin_audit_migrations VALUES('{row.Id}'); DELETE FROM admin_audit_events WHERE id='{row.Id}';");
        }
        var method = typeof(L12PlatformStore).GetMethod("AppendCommittedReadAuditRow", Private)!;
        var generation = RankedIdentityTestState.CommittedProjection(f.Store);
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(f.Store,
            [generation, f.Admin, L12Permission.AdminAnalyticsRead, body, CancellationToken.None]));
        Assert.IsAssignableFrom<L12PlatformStorageUnavailableException>(error.InnerException);
        Assert.Same(generation, RankedIdentityTestState.CommittedProjection(f.Store));
    }

    [Theory]
    [InlineData("created")]
    [InlineData("id")]
    public void AuditQueryLimitCannotHideCorruptCreatedOrIdMetadata(string change)
    {
        using var f = new RankedIdentityFixture();
        Append(f.Store, f.Admin, "older");
        Append(f.Store, f.Admin, "latest");
        var latest = f.Store.AdminAudit(category: "analytics", limit: 1).Single();
        Assert.Equal("latest", latest.Target);
        if (change == "created") Execute(f.Store, $"UPDATE admin_audit_events SET created_utc='1900' WHERE id='{latest.Id}';");
        else Execute(f.Store, $"INSERT INTO admin_audit_migrations VALUES('synthetic-migrated'); UPDATE admin_audit_events SET id='synthetic-migrated' WHERE id='{latest.Id}';");
        Assert.Throws<InvalidDataException>(() => f.Store.AdminAudit(category: "analytics", limit: 1));
    }

    [Fact]
    public void UnreadableCanonicalLedgerCannotReturnStaleCompatibilityAudit()
    {
        using var f = new RankedIdentityFixture();
        Append(f.Store, f.Admin);
        File.Move(f.Store.TransactionalStoragePath, f.Store.TransactionalStoragePath + ".synthetic-unavailable");
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.AdminAudit());
    }

    [Fact]
    public void FullSaveRejectsSameIdDifferentBodyOutsideTheLatest5000Window()
    {
        using var f = new RankedIdentityFixture();
        Append(f.Store, f.Admin);
        var original = Assert.Single(f.Store.AdminAudit(category: "analytics"));
        Assert.True(f.Store.Register("audittail", "Password123!").Success);
        var payload = System.Text.Json.Nodes.JsonNode.Parse((string)Scalar(f.Store,
            "SELECT payload_json FROM admin_audit_events WHERE category='analytics';")!)!;
        using (var db = Open(f.Store))
        using (var tx = db.BeginTransaction())
        {
            using var command = db.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "INSERT INTO admin_audit_events VALUES($id,$created,'analytics','succeeded',$actor,NULL,$correlation,$payload,$sha);";
            for (var index = 0; index < 5001; index++)
            {
                var id = $"synthetic-tail-{index}";
                var created = DateTimeOffset.UtcNow.AddMinutes(-index - 1).ToString("O");
                payload["Id"] = id; payload["CreatedAt"] = created;
                var body = payload.ToJsonString();
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$created", created);
                command.Parameters.AddWithValue("$actor", f.Admin.Id); command.Parameters.AddWithValue("$correlation", Context.CorrelationId);
                command.Parameters.AddWithValue("$payload", body);
                command.Parameters.AddWithValue("$sha", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
                command.ExecuteNonQuery();
            }
            payload["Id"] = original.Id; payload["CreatedAt"] = "2000-01-01T00:00:00.0000000+00:00";
            var altered = payload.ToJsonString();
            command.CommandText = "UPDATE admin_audit_events SET created_utc=$created,payload_json=$payload,payload_sha256=$sha WHERE id=$id;";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$id", original.Id);
            command.Parameters.AddWithValue("$created", (string)payload["CreatedAt"]!);
            command.Parameters.AddWithValue("$payload", altered);
            command.Parameters.AddWithValue("$sha", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(altered))).ToLowerInvariant());
            command.ExecuteNonQuery(); tx.Commit();
        }
        var core = Scalar(f.Store, "SELECT snapshot_sha256 FROM platform_state;");
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.Register("auditbad", "Password123!"));
        Assert.Equal(core, Scalar(f.Store, "SELECT snapshot_sha256 FROM platform_state;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeasonFinalizationFullSnapshotAbsorbsIncrementalReadAudit(bool fail)
    {
        using var f = new RankedIdentityFixture();
        var config = f.Store.OperationsConfig(f.Admin);
        var endsAt = DateTimeOffset.UtcNow.AddMinutes(5);
        f.Store.ApplyOperationsConfig(f.Admin, config.Config with
        { Season = config.Config.Season with { EndsAt = endsAt } }, config.Version, "synthetic audit absorption", Context);
        Append(f.Store, f.Admin);
        var row = Assert.Single(f.Store.AdminAudit(category: "analytics"));
        var mirror = File.ReadAllBytes(f.Path);
        f.Store.StorageFailureInjector = stage =>
        {
            if (fail && stage == "before-season-finalization-commit") throw new IOException("synthetic rejected season commit");
        };
        if (fail)
        {
            Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() =>
                f.Store.TryClaimDueSeasonFinalization("synthetic-worker", endsAt, TimeSpan.FromMinutes(1)));
            Assert.Equal(mirror, File.ReadAllBytes(f.Path));
        }
        else
        {
            Assert.NotNull(f.Store.TryClaimDueSeasonFinalization("synthetic-worker", endsAt, TimeSpan.FromMinutes(1)));
            Assert.Contains(row.Id, File.ReadAllText(f.Path));
        }
        f.Store.StorageFailureInjector = null;
        Assert.Single(f.Store.AdminAudit(category: "analytics"));
        Assert.Single(new L12PlatformStore(f.Path).AdminAudit(category: "analytics"));
    }

    [Fact]
    public void IsolatedReadAllocationDoesNotScaleWithFourMiBOfUnrelatedContent()
    {
        using var f = new RankedIdentityFixture();
        f.Store.ExecuteAdminTransaction(() =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(f.Store)!;
            var content = (System.Collections.IDictionary)data.GetType().GetProperty("Content")!.GetValue(data)!;
            var random = new Random(20261013);
            for (var index = 0; index < 16; index++)
            {
                var bytes = new byte[196608]; random.NextBytes(bytes);
                content[$"synthetic-audit-allocation-{index}"] = Convert.ToBase64String(bytes);
            }
            RankedIdentityTestState.RequestSave(f.Store); return true;
        });
        Append(f.Store, f.Admin, "warm");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 4; index++) Append(f.Store, f.Admin, $"isolated-{index}");
        var isolated = GC.GetAllocatedBytesForCurrentThread() - before;
        var gate = RankedIdentityTestState.Gate(f.Store);
        before = GC.GetAllocatedBytesForCurrentThread();
        lock (gate) Append(f.Store, f.Admin, "compatibility-full-save");
        var full = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"synthetic_4mib_audit isolated_avg_bytes={isolated / 4} compatibility_full_bytes={full}");
        Assert.InRange(isolated / 4, 0, 1024 * 1024);
        Assert.True(full > isolated * 4, "isolated ledger append must not reproduce aggregate-sized allocation");
    }
}
