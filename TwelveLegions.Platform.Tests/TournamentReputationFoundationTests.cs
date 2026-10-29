using SchemaContract = TwelveLegions.Platform.Tests.PlatformSchemaTestContract;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentReputationFoundationTests
{
    // Baseline RED intentions: pre-slice storage has no v1 policy, immutable
    // opening fact or schema-11 boundary. Execution is scheduled by Main.
    [Fact]
    public void NewPolicyPlansAreExplicitAndPreviewHasNoOpeningSideEffect()
    {
        using var f = new Fixture();
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var t = f.Create(start);
        var facts = f.Store.TournamentCheckInFacts(f.Admin, t.Id);
        Assert.Equal(1, facts.RuleVersion);
        Assert.Equal(start.AddMinutes(-5), Assert.Single(facts.Plans).OpensAt);
        Assert.Null(facts.Start);
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.Path);
        var preview = f.Store.SetTournamentPhase(f.Admin, t.Id, new("pre-check-in", "synthetic preview"),
            t.Version, f.Audit, false);
        Assert.Equal("pre-check-in", preview.Phase);
        Assert.Null(f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start);
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(mirror, File.ReadAllText(f.Path));
    }

    [Fact]
    public void UnstartedPostponementMovesPlanButManualOpeningSurvivesReopenAndCancellation()
    {
        using var f = new Fixture();
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var t = f.Create(start);
        t = f.Store.PostponeTournament(f.Admin, t.Id, new(start.AddHours(1), "synthetic postpone"), t.Version, f.Audit, true);
        var facts = f.Store.TournamentCheckInFacts(f.Admin, t.Id);
        Assert.Null(facts.Start);
        Assert.Equal(start.AddHours(1).AddMinutes(-5), facts.Plans[1].OpensAt);
        var before = DateTimeOffset.UtcNow;
        t = f.Store.SetTournamentPhase(f.Admin, t.Id, new("pre-check-in", "synthetic open"), t.Version, f.Audit, true);
        var opened = f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start!;
        Assert.Equal("manual", opened.Source);
        Assert.InRange(opened.EffectiveAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(opened.EffectiveAt, opened.RecordedAt);
        t = f.Store.SetTournamentPhase(f.Admin, t.Id, new("registration-open", "synthetic reopen"), t.Version, f.Audit, true);
        t = f.Store.PostponeTournament(f.Admin, t.Id, new(start.AddHours(2), "synthetic postpone again"), t.Version, f.Audit, true);
        t = f.Store.CancelTournament(f.Admin, t.Id, new("synthetic cancel"), t.Version, f.Audit, true);
        Assert.Equal(opened, f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start);
        var restored = new L12PlatformStore(f.Path);
        Assert.Equal(opened, restored.TournamentCheckInFacts(f.Admin, t.Id).Start);
        Assert.Equal(3, restored.TournamentCheckInFacts(f.Admin, t.Id).Plans.Count);
        Assert.Equal(100, restored.TournamentReputationProjection(f.Admin, f.Player.Id, DateTimeOffset.UtcNow).Score);
    }

    [Fact]
    public void DelayedWorkerRecordsScheduledBoundaryRatherThanTickTime()
    {
        using var f = new Fixture();
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var t = f.Create(start, automated: true);
        var tick = start.AddMinutes(9);
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(tick).Changed);
        var opened = f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start!;
        Assert.Equal(start.AddMinutes(-5), opened.EffectiveAt);
        Assert.Equal(tick, opened.RecordedAt);
        Assert.Equal("scheduled", opened.Source);
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(tick).Changed); // Existing insufficient-player attention.
        Assert.Equal(opened, f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start);
    }

    [Fact]
    public void WorkerNoActionAndFailedSaveCannotPublishAnUncommittedOpening()
    {
        using var f = new Fixture();
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var t = f.Create(start, automated: true);
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.Path);
        Assert.Equal(0, f.Store.AdvanceAutomaticTournaments(start.AddMinutes(-6)).Changed);
        Assert.Equal(version, f.Store.Version);
        Assert.Null(f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start);
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic worker opening commit failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.AdvanceAutomaticTournaments(start.AddMinutes(-4)));
        f.Store.StorageFailureInjector = null;
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.Path));
        Assert.Null(f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start);
        Assert.Null(new L12PlatformStore(f.Path).TournamentCheckInFacts(f.Admin, t.Id).Start);
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(start.AddMinutes(-4)).Changed);
        var opened = f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start;
        version = f.Store.Version;
        Assert.Equal(0, f.Store.AdvanceAutomaticTournaments(start.AddMinutes(-3)).Changed);
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(opened, f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start);
    }

    [Fact]
    public void APlanPublishedInsideItsWindowDoesNotBackdateAnOpening()
    {
        using var f = new Fixture();
        var before = DateTimeOffset.UtcNow;
        var t = f.Create(before.AddMinutes(2));
        var plan = Assert.Single(f.Store.TournamentCheckInFacts(f.Admin, t.Id).Plans);
        Assert.True(plan.OpensAt >= before);
        t = f.Store.PostponeTournament(f.Admin, t.Id, new(before.AddHours(2), "synthetic move after due"), t.Version, f.Audit, true);
        var facts = f.Store.TournamentCheckInFacts(f.Admin, t.Id);
        Assert.Equal(plan.OpensAt, facts.Start!.EffectiveAt);
        Assert.Equal(1, facts.Start.PlanRevision);
        Assert.Equal(2, facts.Plans.Count);
    }

    [Fact]
    public void LegacyImportNeverEnablesReputationAndSchemaTenAdvancesWithoutRewritingSnapshot()
    {
        using var f = new Fixture();
        var legacy = JsonSerializer.Deserialize<L12LegacyTournamentInput>("""
            {"Id":"synthetic-old","Name":"旧赛事","Status":"registration","Format":"single",
             "Visibility":"public","MaxPlayers":8,"CheckInMinutes":5,"RoundMinutes":50}
            """)!;
        var preview = f.Store.ImportLegacyTournaments(f.Admin, new([legacy]), f.Audit, false);
        var imported = f.Store.ImportLegacyTournaments(f.Admin, new([legacy], preview.PreviewHash), f.Audit, true);
        var facts = f.Store.TournamentCheckInFacts(f.Admin, Assert.Single(imported.Tournaments).Id);
        Assert.Equal(0, facts.RuleVersion);
        Assert.Empty(facts.Plans); Assert.Null(facts.Start);
        using var connection = new SqliteConnection($"Data Source={f.Store.TransactionalStoragePath};Pooling=False");
        connection.Open();
        string Scalar(string sql) { using var c = connection.CreateCommand(); c.CommandText = sql; return Convert.ToString(c.ExecuteScalar())!; }
        var snapshot = Scalar("SELECT snapshot_json FROM platform_state WHERE singleton_id=1");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE platform_state SET schema_version=10; UPDATE storage_meta SET value='10' WHERE key='schema_version';";
            command.ExecuteNonQuery();
        }
        var restored = new L12PlatformStore(f.Path);
        Assert.Equal(SchemaContract.Current, restored.StorageStatus().SchemaVersion);
        Assert.Equal(snapshot, Scalar("SELECT snapshot_json FROM platform_state WHERE singleton_id=1"));
        Assert.Equal(0, restored.TournamentCheckInFacts(f.Admin, Assert.Single(imported.Tournaments).Id).RuleVersion);
    }

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-rp-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "platform.json");
        public L12PlatformStore Store { get; }
        public L12AccountView Admin { get; }
        public L12AccountView Player { get; }
        public L12AdminAuditContext Audit { get; } = new("reputation-foundation-test", "admin.accounts.status.write",
            RequestMethod: "TEST", RequestPath: "/test/reputation-foundation");
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new(Path);
            var admin = Store.Register("rphost", "password-123").Account!;
            Assert.True(Store.SetRole(admin.Id, "admin"));
            Admin = Store.Accounts().Single(item => item.Id == admin.Id);
            Player = Store.Register("rpplayer", "password-123").Account!;
        }
        public L12TournamentView Create(DateTimeOffset? start = null, bool automated = false)
            => Store.CreateTournament(Admin, new("信誉合成", "single", "public", 2, start, "现行规则", "",
                "after", "none", "", 50, 5, OperationMode: automated ? "automated" : "managed"), Audit, true);
        public L12TournamentView RegisteredAndOpened()
        {
            var t = Create(DateTimeOffset.UtcNow.AddHours(2));
            t = Store.RegisterTournament(Player, t.Id, new(), t.Version, Audit, true);
            return Store.SetTournamentPhase(Admin, t.Id, new("pre-check-in", "synthetic opening"), t.Version, Audit, true);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
