using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentPlatformGuardTests
{
    [Fact]
    public void UnconfiguredIsNotClearAndAttachmentCannotChangeAuthority()
    {
        using var f = new Fixture();
        Assert.False(f.Store.CaptureDeploymentPlatformReadiness().Verified);
        f.Store.AttachDeploymentDrain(f.Coordinator);
        var readiness = f.Store.CaptureDeploymentPlatformReadiness();
        Assert.True(readiness.Clear);
        Assert.False(readiness.GrantsStopPermit);
        f.Store.AttachDeploymentDrain(f.Coordinator);
        Assert.Throws<InvalidOperationException>(() => f.Store.AttachDeploymentDrain(new(
            Guid.NewGuid().ToString("N"), new string('a', 40), new FenceStore())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OuterTransactionHoldsLeaseThroughCommitAndPublishesOnlyCommittedCommands(bool fail)
    {
        using var f = new Fixture();
        f.Store.AttachDeploymentDrain(f.Coordinator);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic commit barrier");
            if (fail) throw new IOException("synthetic failure");
        };
        var writer = Task.Factory.StartNew(() =>
        {
            try
            {
                f.Store.ExecuteAdminTransaction(() =>
                {
                    f.AddPendingCommand();
                    Assert.False(f.Store.CaptureDeploymentPlatformReadiness().Verified);
                    return true;
                });
                return true;
            }
            catch (L12PlatformStorageUnavailableException) { return false; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(3))) await writer;
            Assert.True(entered.IsSet);
            var readiness = f.Store.CaptureDeploymentPlatformReadiness();
            Assert.False(readiness.Verified);
            Assert.Equal("platform_busy", readiness.FailureCode);
            Assert.True(f.Coordinator.BeginDrain(f.Owner).Succeeded);
            Assert.Equal(L12DeploymentDrainTransitionCode.OutstandingLeases, f.Coordinator.TryBeginSeal(f.Owner).Code);
        }
        finally { release.Set(); }
        Assert.Equal(!fail, await writer.WaitAsync(TimeSpan.FromSeconds(6)));
        f.Store.StorageFailureInjector = null;
        var committed = f.Store.CaptureDeploymentPlatformReadiness();
        Assert.True(committed.Verified);
        Assert.Equal(fail ? 0 : 1, committed.PendingRoomCommands);
        Assert.Equal(fail, committed.Clear);
        Assert.Equal(0, f.Coordinator.Snapshot().ActivityLeases);
    }

    [Fact]
    public void PendingCommandAttemptsAreActivityAndCompletionIsDurable()
    {
        using var f = new Fixture();
        f.AddPendingCommand();
        f.Store.AttachDeploymentDrain(f.Coordinator);
        Assert.False(f.Store.CaptureDeploymentPlatformReadiness().Clear);
        var command = Assert.Single(f.Store.PendingTournamentRoomCommands());
        f.Coordinator.BeginDrain(f.Owner);
        f.Store.RecordTournamentRoomCommandAttempt(f.TournamentId, command.Id, false, "synthetic retry");
        Assert.Equal(1, f.Store.CaptureDeploymentPlatformReadiness().PendingRoomCommands);
        f.Store.RecordTournamentRoomCommandAttempt(f.TournamentId, command.Id, true, null);
        Assert.True(f.Store.CaptureDeploymentPlatformReadiness().Clear);
        var reloaded = new L12PlatformStore(f.PlatformPath);
        reloaded.AttachDeploymentDrain(new(Guid.NewGuid().ToString("N"), new string('a', 40), new FenceStore()));
        Assert.True(reloaded.CaptureDeploymentPlatformReadiness().Clear);
        var seal = f.Coordinator.TryBeginSeal(f.Owner);
        Assert.True(seal.Succeeded);
        Assert.Throws<L12DeploymentBarrierClosedException>(() =>
            f.Store.RecordTournamentRoomCommandAttempt(f.TournamentId, command.Id, true, null));
    }

    [Fact]
    public void SealingRejectsOuterWritesAndDirectSaveRollsBackWithoutDatabaseMutation()
    {
        using var f = new Fixture();
        f.Store.AttachDeploymentDrain(f.Coordinator);
        var version = f.Store.Version;
        f.Coordinator.BeginDrain(f.Owner);
        Assert.True(f.Coordinator.TryBeginSeal(f.Owner).Succeeded);
        var called = false;
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.ExecuteAdminTransaction(() => called = true));
        Assert.False(called);
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.Register("sealednew", "synthetic-password"));
        Assert.Equal(version, f.Store.Version);
        Assert.DoesNotContain(new L12PlatformStore(f.PlatformPath).Accounts(), account => account.Username == "sealednew");
        Assert.Equal(0, f.Coordinator.Snapshot().ActivityLeases);
    }

    [Theory]
    [InlineData("future-command")]
    [InlineData("pause")]
    public void UnknownCommandShapeNeverMeansZero(string kind)
    {
        using var f = new Fixture();
        f.AddPendingCommand(kind, matchId: "missing-match");
        f.Store.AttachDeploymentDrain(f.Coordinator);
        var readiness = f.Store.CaptureDeploymentPlatformReadiness();
        Assert.False(readiness.Verified);
        Assert.False(readiness.Clear);
        Assert.Equal(-1, readiness.PendingRoomCommands);
        Assert.Equal("invalid_room_command", readiness.FailureCode);
    }

    [Fact]
    public void DirectObjectTransactionsAndMaintenanceCannotBypassSeal()
    {
        using var f = new Fixture();
        f.Store.AttachDeploymentDrain(f.Coordinator);
        f.Coordinator.BeginDrain(f.Owner);
        Assert.True(f.Coordinator.TryBeginSeal(f.Owner).Succeeded);
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.TogglePublishedDeckLike(f.Admin.Id, "missing"));
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.RecordPublishedDeckCopy("missing", f.Admin.Id));
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.RecordPublishedDeckView("missing", f.Admin.Id));
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.UpdatePublicDeckContent(f.Admin.Id, "missing", new(null, [])));
        // Guard runs before validating/processing upload bytes or creating an immutable media directory.
        Assert.Throws<L12DeploymentBarrierClosedException>(() => f.Store.UploadSiteMedia(f.Admin, null!));
        var maintenance = f.Store.RunAuditLifecycle(DateTimeOffset.UtcNow, []);
        Assert.Equal(0, maintenance.Archived);
        Assert.Equal(0, maintenance.ExpiredSegments);
        Assert.Equal(0, f.Coordinator.Snapshot().ActivityLeases);
    }

    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine("D:/GPT/Legion12/artifacts/deployment-platform-guard-tests", Guid.NewGuid().ToString("N"));
        internal string PlatformPath => Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal string TournamentId { get; }
        internal L12DeploymentDrainCoordinator Coordinator { get; } = new(
            "11111111111111111111111111111111", new string('a', 40), new FenceStore());
        internal L12DeploymentDrainOwner Owner { get; }
        internal Fixture()
        {
            Store = new(PlatformPath);
            Admin = Store.Login("Admin", "L12master").Account!;
            TournamentId = Store.CreateTournament(Admin, new("synthetic", "swiss", "public", 16,
                null, "现行规则", "synthetic", "after", "season", "", 50, 5),
                new("synthetic", "tournaments.manage"), true).Id;
            Assert.True(L12DeploymentDrainOwner.TryCreate("22222222222222222222222222222222",
                new string('b', 40), Coordinator.Snapshot().ProcessInstance, out var owner));
            Owner = owner!;
        }
        internal void AddPendingCommand(string kind = "pause", string? matchId = null)
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", Hidden)!.GetValue(Store)!;
            var tournaments = List(data, "Tournaments");
            var tournament = tournaments[0]!;
            if (List(tournament, "Rounds").Count == 0)
            {
                var round = Add(List(tournament, "Rounds"));
                Set(round, "Number", 1);
                var match = Add(List(round, "Matches"));
                Set(match, "Id", "synthetic-match");
                Set(match, "RoomCode", "SYNTHETIC");
            }
            var command = Add(List(tournament, "RoomCommands"));
            Set(command, "Kind", kind);
            Set(command, "MatchId", matchId ?? "synthetic-match");
            typeof(L12PlatformStore).GetMethod("Save", Hidden)!.Invoke(Store, [false]);
        }
        private static IList List(object row, string name) => (IList)row.GetType().GetProperty(name)!.GetValue(row)!;
        private static object Add(IList list)
        {
            var row = Activator.CreateInstance(list.GetType().GenericTypeArguments[0], nonPublic: true)!;
            list.Add(row);
            return row;
        }
        private static void Set(object row, string name, object value) => row.GetType().GetProperty(name)!.SetValue(row, value);
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class FenceStore : IL12DeploymentDrainFenceStore
    {
        public L12DeploymentDrainFenceLoadResult Load() => L12DeploymentDrainFenceLoadResult.Missing;
        public bool TryWrite(L12DeploymentPersistedFence fence) => true;
        public bool TryClear(string operationId, string targetCommit) => true;
    }
}
