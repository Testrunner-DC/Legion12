using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlaneAuditLifecycleTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "l12-audit-lifecycle-" + Guid.NewGuid().ToString("N"));
        public readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        public string PathName => Path.Combine(Root, "platform.json");
        public L12PlatformStore Store { get; private set; } = null!;
        public void Seed(params (string Id, double Days, string Category, string Target)[] events)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(PathName, JsonSerializer.Serialize(new
            {
                Version = 0, Accounts = Array.Empty<object>(),
                AdminAudit = events.Select(e => new { e.Id, ActorId = "fixture", ActorName = "fixture",
                    e.Category, Action = "fixture", e.Target, CreatedAt = Now.AddDays(-e.Days), Outcome = "succeeded" })
            }));
            Store = new L12PlatformStore(PathName);
        }
        public object? Scalar(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var query = connection.CreateCommand(); query.CommandText = sql; return query.ExecuteScalar();
        }
        public void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Store.TransactionalStoragePath};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        public long Count(string table) => Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM {table};"));
        public void Reload() => Store = new L12PlatformStore(PathName);
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public void ThirtyDayBoundaryAndProtectedReferencesRemainWhileArchiveIsCompressedAndRecoverable()
    {
        using var f = new Fixture();
        f.Seed(("old",31,"content","title"), ("boundary",30,"content","title"),
            ("active",60,"content","match-running"), ("incident",60,"ranked","unknown-settlement"));
        var result = f.Store.RunAuditLifecycle(f.Now, new[] { "match-running" });
        Assert.Equal(1,result.Archived);
        Assert.Equal(1,f.Count("admin_audit_migrations"));
        Assert.Null(f.Scalar("SELECT id FROM admin_audit_events WHERE id='old';"));
        Assert.Equal("boundary",f.Scalar("SELECT id FROM admin_audit_events WHERE id='boundary';"));
        Assert.Equal("active",f.Scalar("SELECT id FROM admin_audit_events WHERE id='active';"));
        Assert.Equal("incident",f.Scalar("SELECT id FROM admin_audit_events WHERE id='incident';"));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(f.Root,"audit-archives"),"*.gz"));
        Assert.NotEmpty(File.ReadAllBytes(file));
        var admin = f.Store.Login("Admin","L12master").Account!;
        Assert.True(f.Store.RehearseAuditArchiveRecovery(admin).Success);
        Assert.Single(f.Store.AuditArchiveSegments(admin));
    }

    [Theory]
    [InlineData("after-file-verified",false)]
    [InlineData("before-source-commit",false)]
    [InlineData("after-source-commit",true)]
    public void InterruptedArchiveNeverLosesSourceOrResurrectsMigratedEvents(string point,bool committed)
    {
        using var f = new Fixture(); f.Seed(("old",40,"content","title"));
        var stale = File.ReadAllText(f.PathName);
        f.Store.AuditLifecycleFailureInjector = stage => { if(stage==point) throw new IOException("injected"); };
        Assert.Throws<IOException>(()=>f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>()));
        Assert.Equal(committed ? 1L : 0L,f.Count("admin_audit_migrations"));
        Assert.Equal(committed ? null : "old",f.Scalar("SELECT id FROM admin_audit_events WHERE id='old';"));
        f.Reload();
        f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>());
        Assert.Equal(1,f.Count("admin_audit_migrations"));
        Assert.Equal(1,f.Count("audit_lifecycle_segments"));
        // Even a newer compatibility mirror containing an old event cannot resurrect its exact ID.
        var tree = System.Text.Json.Nodes.JsonNode.Parse(stale)!;
        tree["Version"] = 100000;
        File.WriteAllText(f.PathName,tree.ToJsonString());
        f.Reload();
        Assert.Null(f.Scalar("SELECT id FROM admin_audit_events WHERE id='old';"));
        Assert.DoesNotContain(f.Store.AdminAudit(),row=>row.Id=="old");
        Assert.DoesNotContain("\"Id\": \"old\"",File.ReadAllText(f.PathName));
    }

    [Fact]
    public void ExpirationUsesEventTimeNotArchiveCreationAndRetainsExactIdFence()
    {
        using var f = new Fixture(); f.Seed(("old",40,"content","title"));
        f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>());
        Assert.Equal(0,f.Store.RunAuditLifecycle(f.Now.AddDays(140),Array.Empty<string>()).ExpiredSegments);
        Assert.Equal(1,f.Store.RunAuditLifecycle(f.Now.AddDays(140).AddTicks(1),Array.Empty<string>()).ExpiredSegments);
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Root,"audit-archives")));
        Assert.Equal(1,f.Count("admin_audit_migrations"));
        Assert.Equal(0,f.Count("audit_lifecycle_segments"));
        f.Execute("INSERT INTO admin_audit_events VALUES('old','2000-01-01','content','succeeded','a',NULL,NULL,'{}','invalid');");
        Assert.Null(f.Scalar("SELECT id FROM admin_audit_events WHERE id='old';"));
        // Unrelated historical data is not rejected by an overly broad age cutoff.
        f.Execute("INSERT INTO admin_audit_events VALUES('unrelated','2000-01-01','content','succeeded','a',NULL,NULL,'{}','invalid');");
        Assert.Equal("unrelated",f.Scalar("SELECT id FROM admin_audit_events WHERE id='unrelated';"));
    }

    [Fact]
    public void CorruptArchiveAndNewEvidenceStopExpiration()
    {
        using var f = new Fixture(); f.Seed(("old",40,"content","match-1"));
        f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>());
        Assert.Equal(0,f.Store.RunAuditLifecycle(f.Now.AddDays(150),new[]{"match-1"}).ExpiredSegments);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(f.Root,"audit-archives"),"*.gz"));
        File.AppendAllText(file,"corrupt");
        Assert.Throws<InvalidDataException>(()=>f.Store.RunAuditLifecycle(f.Now.AddDays(150),Array.Empty<string>()));
        Assert.True(File.Exists(file));
        Assert.Equal(1,f.Count("audit_lifecycle_segments"));
    }

    [Theory]
    [InlineData("before-archive-delete")]
    [InlineData("after-archive-delete")]
    public void InterruptedExpirationCanResumeWithoutDeletingSourceOrAnotherFile(string point)
    {
        using var f = new Fixture(); f.Seed(("old",40,"content","title"));
        f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>());
        var untouched = Path.Combine(f.Root,"audit-archives","unrelated.txt"); File.WriteAllText(untouched,"keep");
        f.Store.AuditLifecycleFailureInjector = stage=>{if(stage==point)throw new IOException("injected");};
        Assert.Throws<IOException>(()=>f.Store.RunAuditLifecycle(f.Now.AddDays(150),Array.Empty<string>()));
        f.Reload();
        f.Store.RunAuditLifecycle(f.Now.AddDays(150),Array.Empty<string>());
        Assert.Equal(0,f.Count("audit_lifecycle_segments")); Assert.Equal("keep",File.ReadAllText(untouched));
    }

    [Fact]
    public void CancellationDoesNotWriteAndCursorEventuallyPassesProtectedPrefix()
    {
        using var f = new Fixture();
        f.Seed(Enumerable.Range(0,1001).Select(i=>($"p{i:0000}",40d,"ranked","protected"))
            .Append(("last",40d,"content","title")).ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>(),cancelled.Token));
        Assert.Equal(0,f.Count("admin_audit_migrations"));
        Assert.Equal(1000,f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>()).Scanned);
        Assert.Equal(1,f.Store.RunAuditLifecycle(f.Now.AddDays(1),Array.Empty<string>()).Archived);
    }

    [Fact]
    public void ProtectedArchivePrefixDoesNotStarveLaterExpiredSegments()
    {
        using var f = new Fixture();
        var targets = Enumerable.Range(0,11).Select(i=>$"match-{i:0000}").ToArray();
        f.Seed(targets.Select(id=>(id,40d,"content",id)).ToArray());
        foreach (var target in targets)
            Assert.Equal(1,f.Store.RunAuditLifecycle(f.Now,targets.Where(id=>id!=target).ToArray()).Archived);
        // Begin one deterministic inventory cycle; source and archive cursors are independent.
        f.Execute("UPDATE storage_meta SET value='0' WHERE key='audit_lifecycle_segment_cursor';");
        Assert.Equal(0,f.Store.RunAuditLifecycle(f.Now.AddDays(150),targets.Take(10).ToArray()).ExpiredSegments);
        Assert.Equal(1,f.Store.RunAuditLifecycle(f.Now.AddDays(151),targets.Take(10).ToArray()).ExpiredSegments);
        Assert.Equal(10,f.Count("audit_lifecycle_segments"));
    }

    [Theory]
    [InlineData("UPDATE admin_audit_events SET created_utc='2000-01-01T00:00:00.0000000+00:00' WHERE id='recent';")]
    [InlineData("UPDATE admin_audit_events SET id='changed-id',created_utc='2000-01-01T00:00:00.0000000+00:00' WHERE id='recent';")]
    public void MetadataCorruptionCannotAgeRecentAuditIntoDeletion(string corruption)
    {
        using var f = new Fixture(); f.Seed(("recent",1,"content","title"));
        f.Execute(corruption);
        Assert.Throws<InvalidDataException>(()=>f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>()));
        Assert.Equal(0,f.Count("admin_audit_migrations"));
        Assert.Equal(0,f.Count("audit_lifecycle_segments"));
    }

    [Fact]
    public void LegacyManualArchivesExpireByContainedEventsAndKeepProtectedEvidence()
    {
        using var f=new Fixture(); f.Seed(("legacy",200,"content","match-legacy"));
        var admin=f.Store.Login("Admin","L12master").Account!;
        var archive=f.Store.ArchiveAudit(admin,new L12AuditArchiveCommandPayload(f.Now.AddDays(-30),30),
            new L12AdminAuditContext("fixture"),true);
        Assert.NotNull(archive.Segment);
        f.Store.RunAuditLifecycle(f.Now,new[]{"match-legacy"});
        Assert.Equal(1,f.Count("audit_archive_segments"));
        Assert.Equal(1,f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>()).ExpiredSegments);
        Assert.Equal(0,f.Count("audit_archive_segments"));
        Assert.Equal(0,f.Count("audit_legacy_expiration_intents"));
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Root,"audit-archives"),"*.jsonl"));
        // Source is independently migrated only after its new compressed file verifies.
        Assert.Equal(1,f.Count("admin_audit_migrations"));
    }

    [Fact]
    public void LegacyDeletionCrashResumesFromDurableIntent()
    {
        using var f=new Fixture(); f.Seed(("legacy",200,"content","title"));
        var admin=f.Store.Login("Admin","L12master").Account!;
        f.Store.ArchiveAudit(admin,new L12AuditArchiveCommandPayload(f.Now.AddDays(-30),30),new L12AdminAuditContext("fixture"),true);
        f.Store.AuditLifecycleFailureInjector=stage=>{if(stage=="after-legacy-delete")throw new IOException("injected");};
        Assert.Throws<IOException>(()=>f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>()));
        Assert.Equal(1,f.Count("audit_legacy_expiration_intents"));
        f.Reload(); f.Store.RunAuditLifecycle(f.Now,Array.Empty<string>());
        Assert.Equal(0,f.Count("audit_archive_segments"));
        Assert.Equal(0,f.Count("audit_legacy_expiration_intents"));
    }
}
