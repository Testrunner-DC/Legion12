using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentDrainControllerTests
{
    [Fact]
    public async Task EmptyVerifiedStoresProduceOneOwnerBoundStablePermit()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();

        var begin = fixture.Controller.Begin(fixture.Owner);
        Assert.Equal("Applied", begin.Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, begin.Snapshot.Phase);
        Assert.Equal(0, begin.Snapshot.AdmissionLeases);
        Assert.Equal(0, begin.Snapshot.ActivityLeases);

        var sealedResult = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal("Applied", sealedResult.Code);
        Assert.True(sealedResult.Granted);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, sealedResult.Snapshot.Phase);
        Assert.True(sealedResult.Snapshot.FenceSynchronized);
        Assert.False(sealedResult.Snapshot.FenceUnknown);
        Assert.NotNull(sealedResult.Rooms);
        Assert.True(sealedResult.Rooms!.Clear);
        Assert.NotNull(sealedResult.Durability);
        Assert.True(sealedResult.Durability!.Clear);
        Assert.NotNull(sealedResult.Platform);
        Assert.True(sealedResult.Platform!.Clear);
        Assert.False(sealedResult.Rooms.GrantsStopPermit);
        Assert.False(sealedResult.Durability.GrantsStopPermit);
        Assert.False(sealedResult.Platform.GrantsStopPermit);

        var permit = Assert.IsType<L12DeploymentSealPermit>(sealedResult.Permit);
        Assert.Equal(L12DeploymentDrainCoordinator.ProtocolVersion, permit.ProtocolVersion);
        Assert.Equal(DeploymentDrainFixture.OperationId, permit.OperationId);
        Assert.Equal(DeploymentDrainFixture.TargetCommit, permit.TargetCommit);
        Assert.Equal(DeploymentDrainFixture.ProcessInstance, permit.ProcessInstance);
        Assert.Equal(DeploymentDrainFixture.ActiveCommit, permit.ActiveCommit);
        Assert.NotEqual(permit.TargetCommit, permit.ActiveCommit);
        Assert.Equal(sealedResult.Snapshot.Epoch, permit.Epoch);
        Assert.True(fixture.Coordinator.IsCurrentSealPermit(permit));
    }

    [Fact]
    public async Task StatusNeverReturnsPermitAndSameOwnerRetryReturnsTheOriginalPermit()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        Assert.Equal("status", fixture.Controller.Status().Code);
        Assert.Null(fixture.Controller.Status().Permit);

        fixture.Controller.Begin(fixture.Owner);
        var first = await fixture.Controller.SealAsync(fixture.Owner);
        var repeated = await fixture.Controller.SealAsync(fixture.Owner);
        var status = fixture.Controller.Status();

        Assert.True(first.Granted);
        Assert.Equal("Idempotent", repeated.Code);
        Assert.True(repeated.Granted);
        Assert.Equal(first.Permit, repeated.Permit);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, status.Snapshot.Phase);
        Assert.False(status.Granted);
        Assert.Null(status.Permit);
    }

    [Theory]
    [InlineData("pregame", "pregame_rooms")]
    [InlineData("active", "unfinished_rooms")]
    public async Task LiveAndPregameRoomsBlockBeforeTheDurabilityProbe(string state, string blocker)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var account = fixture.CreatePlayer("room");
        var sessionId = Guid.NewGuid();
        await fixture.Rooms.ConnectAsync(sessionId, account.Id, account.Username);
        if (state == "active")
            _ = await fixture.Rooms.CreateSandboxAsync(sessionId, null);
        else
            _ = fixture.Rooms.CreateRoom(sessionId);

        Assert.True(fixture.Rooms.CaptureDeploymentRoomReadiness().Blockers[blocker] > 0);
        fixture.Controller.Begin(fixture.Owner);
        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal("work_remaining", result.Code);
        Assert.False(result.Granted);
        Assert.NotNull(result.Rooms);
        Assert.True(result.Rooms!.Blockers[blocker] > 0);
        Assert.Null(result.Durability);
        Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
    }

    [Theory]
    [InlineData("historical", "unfinished_matches")]
    [InlineData("recovery", "quarantined_recovery")]
    [InlineData("settlement", "ranked_settlement_unresolved")]
    public async Task DurableRecoveryAndSettlementWorkNeverBecomesAPermit(
        string scenario, string blocker)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        switch (scenario)
        {
            case "historical":
                fixture.InsertMatch("friendly", ended: false);
                break;
            case "recovery":
                fixture.InsertMatch("ranked", ended: false);
                fixture.Sql("""
                    INSERT INTO ranked_recovery_quarantine(match_id,reason,created_utc)
                    VALUES('private-match-id','private-reason','2026-10-06T00:00:00Z');
                    """);
                break;
            case "settlement":
                fixture.InsertMatch("ranked", ended: true);
                fixture.Sql("""
                    INSERT INTO ranked_settlement_outbox(
                        match_id,payload_json,payload_hash,status,created_utc,applied_utc)
                    VALUES('private-match-id','private-payload','hash','pending',
                        '2026-10-06T00:00:00Z',NULL);
                    """);
                break;
            default:
                throw new InvalidOperationException("unknown durability scenario");
        }

        fixture.Controller.Begin(fixture.Owner);
        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal("durability_not_clear", result.Code);
        Assert.False(result.Granted);
        Assert.NotNull(result.Durability);
        Assert.True(result.Durability!.Verified);
        Assert.Equal(1L, result.Durability.Blockers[blocker]);
        Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AdmissionAndActivityLeasesMustBothReachZeroBeforeSealing(int leaseKind)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        L12DeploymentDrainLease? lease;
        if (leaseKind == 0)
        {
            Assert.True(fixture.Coordinator.TryAcquireAdmission(out lease));
            fixture.Controller.Begin(fixture.Owner);
        }
        else
        {
            fixture.Controller.Begin(fixture.Owner);
            Assert.True(fixture.Coordinator.TryAcquireActivity(out lease));
        }

        using (lease)
        {
            var blocked = await fixture.Controller.SealAsync(fixture.Owner);
            Assert.Equal("OutstandingLeases", blocked.Code);
            Assert.False(blocked.Granted);
            Assert.Equal(L12DeploymentDrainPhase.Draining, blocked.Snapshot.Phase);
            Assert.Equal(leaseKind == 0 ? 1 : 0, blocked.Snapshot.AdmissionLeases);
            Assert.Equal(leaseKind == 1 ? 1 : 0, blocked.Snapshot.ActivityLeases);
        }

        var sealedResult = await fixture.Controller.SealAsync(fixture.Owner);
        Assert.True(sealedResult.Granted);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, sealedResult.Snapshot.Phase);
    }

    [Fact]
    public async Task RealSqliteWriterLockFailsClosedAndLeavesTheDrainRetryable()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        // InitializeAsync left an idle pooled WAL handle. Clear only this owned
        // fixture's pool before changing journal mode, and verify the actual mode.
        using var ownedPool = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fixture.MatchPath }.ToString());
        SqliteConnection.ClearPool(ownedPool);
        fixture.Sql("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.MatchPath,
            Pooling = false,
        }.ToString());
        writer.Open();
        using var transaction = writer.CreateCommand();
        transaction.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("delete", (string?)transaction.ExecuteScalar());
        transaction.CommandText = "BEGIN EXCLUSIVE;";
        transaction.ExecuteNonQuery();
        try
        {
            fixture.Controller.Begin(fixture.Owner);
            var result = await fixture.Controller.SealAsync(fixture.Owner);

            Assert.Equal("durability_not_clear", result.Code);
            Assert.False(result.Granted);
            Assert.NotNull(result.Durability);
            Assert.False(result.Durability!.Verified);
            Assert.Equal("sqlite_busy", result.Durability.FailureCode);
            Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
        }
        finally
        {
            transaction.CommandText = "ROLLBACK;";
            transaction.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task UnknownRecorderSchemaFailsClosedWithoutLeavingSealing()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        fixture.Sql("DROP TABLE response_preference_outbox;");
        fixture.Controller.Begin(fixture.Owner);

        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal("durability_not_clear", result.Code);
        Assert.False(result.Granted);
        Assert.NotNull(result.Durability);
        Assert.False(result.Durability!.Verified);
        Assert.Equal("schema_invalid", result.Durability.FailureCode);
        Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
    }

    [Theory]
    [InlineData("cancel", "probe_cancelled")]
    [InlineData("timeout", "probe_unknown")]
    [InlineData("exception", "probe_unknown")]
    public async Task FailuresAfterBeginSealReturnToDrainingWithoutAPermit(
        string failure, string expectedCode)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync(stage =>
        {
            if (stage != "after-begin-seal") return;
            if (failure == "cancel") throw new OperationCanceledException("synthetic cancellation");
            if (failure == "timeout") throw new TimeoutException("synthetic timeout");
            throw new InvalidOperationException("synthetic probe failure");
        });
        fixture.Controller.Begin(fixture.Owner);

        var result = await fixture.Controller.SealAsync(fixture.Owner);

        Assert.Equal(expectedCode, result.Code);
        Assert.False(result.Granted);
        Assert.Null(result.Permit);
        Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
        Assert.Equal(0, result.Snapshot.AdmissionLeases);
        Assert.Equal(0, result.Snapshot.ActivityLeases);
    }

    [Fact]
    public async Task StableEpochCheckRejectsAnInterveningSealFailure()
    {
        DeploymentDrainFixture? fixture = null;
        var intervened = false;
        fixture = await DeploymentDrainFixture.CreateAsync(stage =>
        {
            if (stage != "before-complete-seal") return;
            var snapshot = fixture!.Coordinator.Snapshot();
            var transition = fixture.Coordinator.CompleteSeal(fixture.Owner, snapshot.Epoch,
                L12DeploymentExternalReadiness.Unknown);
            intervened = transition.Code == L12DeploymentDrainTransitionCode.ExternalReadinessUnknown;
        });
        await using (fixture)
        {
            fixture.Controller.Begin(fixture.Owner);
            var result = await fixture.Controller.SealAsync(fixture.Owner);

            Assert.True(intervened);
            Assert.Equal("WrongPhase", result.Code);
            Assert.False(result.Granted);
            Assert.Null(result.Permit);
            Assert.Equal(L12DeploymentDrainPhase.Draining, result.Snapshot.Phase);
        }
    }

    [Fact]
    public async Task WrongOwnerAndCancelledEpochCannotChangeOrReuseTheDrain()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var other = fixture.CreateOwner("55555555555555555555555555555555",
            "6666666666666666666666666666666666666666");
        var begin = fixture.Controller.Begin(fixture.Owner);

        var wrongSeal = await fixture.Controller.SealAsync(other);
        var wrongCancel = fixture.Controller.Cancel(other);
        Assert.Equal("OwnerMismatch", wrongSeal.Code);
        Assert.Equal("OwnerMismatch", wrongCancel.Code);
        Assert.Equal(begin.Snapshot.Epoch, fixture.Coordinator.Snapshot().Epoch);
        Assert.Equal(L12DeploymentDrainPhase.Draining, fixture.Coordinator.Snapshot().Phase);

        var cancelled = fixture.Controller.Cancel(fixture.Owner);
        Assert.Equal("Applied", cancelled.Code);
        Assert.Equal(L12DeploymentDrainPhase.Open, cancelled.Snapshot.Phase);
        Assert.Null(cancelled.Snapshot.Owner);
        var stale = await fixture.Controller.SealAsync(fixture.Owner);
        Assert.Equal("OwnerMismatch", stale.Code);
        Assert.False(stale.Granted);
        Assert.Equal(L12DeploymentDrainPhase.Open, stale.Snapshot.Phase);
    }

    [Fact]
    public async Task UnknownOrUnwritableFenceNeverProducesAStopPermit()
    {
        await using (var unknown = await DeploymentDrainFixture.CreateAsync(fenceStore:
                         new SyntheticDeploymentFenceStore(L12DeploymentDrainFenceLoadResult.Unknown)))
        {
            var result = unknown.Controller.Begin(unknown.Owner);
            Assert.Equal("UnknownFence", result.Code);
            Assert.True(result.Snapshot.FenceUnknown);
            Assert.False(result.Snapshot.FenceSynchronized);
            Assert.False(unknown.Controller.Status().Granted);
        }

        var unwritableStore = new SyntheticDeploymentFenceStore { WriteSucceeds = false };
        await using var unwritable = await DeploymentDrainFixture.CreateAsync(fenceStore: unwritableStore);
        var failed = unwritable.Controller.Begin(unwritable.Owner);
        Assert.Equal("FenceFailure", failed.Code);
        Assert.Equal(L12DeploymentDrainPhase.Draining, failed.Snapshot.Phase);
        Assert.False(failed.Snapshot.FenceSynchronized);
        Assert.False(failed.Snapshot.FenceUnknown);
        Assert.False(unwritable.Controller.Status().Granted);
    }
}

internal sealed class DeploymentDrainFixture : IAsyncDisposable
{
    internal const string ProcessInstance = "11111111111111111111111111111111";
    internal const string OperationId = "22222222222222222222222222222222";
    internal const string ActiveCommit = "3333333333333333333333333333333333333333";
    internal const string TargetCommit = "4444444444444444444444444444444444444444";
    internal static readonly string ArtifactRoot = Path.Combine("D:\\GPT\\Legion12\\artifacts",
        "deployment-drain-controller-tests");

    private bool _disposed;

    private DeploymentDrainFixture(string root, L12Catalog catalog, L12PlatformStore platform,
        MatchRecorder recorder, L12RoomManager rooms, L12DeploymentDrainCoordinator coordinator,
        L12DeploymentDrainController? controller, L12DeploymentDrainOwner owner)
    {
        Root = root;
        Catalog = catalog;
        Platform = platform;
        Recorder = recorder;
        Rooms = rooms;
        Coordinator = coordinator;
        ControllerOrNull = controller;
        Owner = owner;
    }

    internal string Root { get; }
    internal string MatchPath => Path.Combine(Root, "matches.db");
    internal L12Catalog Catalog { get; }
    internal L12PlatformStore Platform { get; }
    internal MatchRecorder Recorder { get; }
    internal L12RoomManager Rooms { get; }
    internal L12DeploymentDrainCoordinator Coordinator { get; }
    internal L12DeploymentDrainController? ControllerOrNull { get; }
    internal L12DeploymentDrainController Controller => ControllerOrNull
        ?? throw new InvalidOperationException("deployment controller is disabled");
    internal L12DeploymentDrainOwner Owner { get; }
    internal L12WebSocketServer? Server { get; private set; }
    internal Uri? BaseAddress { get; private set; }

    internal static async Task<DeploymentDrainFixture> CreateAsync(
        Action<string>? observer = null, bool attachCoordinator = true,
        IL12DeploymentDrainFenceStore? fenceStore = null)
    {
        Directory.CreateDirectory(ArtifactRoot);
        var root = Path.Combine(ArtifactRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MatchRecorder? recorder = null;
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory,
                "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(root, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"),
                () => DateTimeOffset.Parse("2026-10-06T00:00:00Z"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, platform,
                () => DateTimeOffset.Parse("2026-10-06T00:00:00Z"));
            var coordinator = new L12DeploymentDrainCoordinator(ProcessInstance, ActiveCommit,
                fenceStore ?? new SyntheticDeploymentFenceStore());
            L12DeploymentDrainController? controller = null;
            if (attachCoordinator)
            {
                rooms.AttachDeploymentDrain(coordinator);
                controller = new L12DeploymentDrainController(rooms, recorder, platform,
                    coordinator, observer);
            }
            Assert.True(L12DeploymentDrainOwner.TryCreate(OperationId, TargetCommit,
                ProcessInstance, out var owner));
            return new DeploymentDrainFixture(root, catalog, platform, recorder, rooms,
                coordinator, controller, owner!);
        }
        catch
        {
            if (recorder is not null) await recorder.DisposeAsync();
            DeleteArtifactDirectory(root);
            throw;
        }
    }

    internal L12DeploymentDrainOwner CreateOwner(string operationId, string targetCommit,
        string processInstance = ProcessInstance)
    {
        Assert.True(L12DeploymentDrainOwner.TryCreate(operationId, targetCommit,
            processInstance, out var owner));
        return owner!;
    }

    internal L12AccountView CreatePlayer(string prefix)
    {
        var username = prefix[..Math.Min(2, prefix.Length)] + Guid.NewGuid().ToString("N")[..8];
        var registration = Platform.Register(username, "Password123!");
        Assert.True(registration.Success, registration.Message);
        return registration.Account!;
    }

    internal L12AuthenticationResult LoginAdmin()
    {
        var login = Platform.Login("Admin", "L12master");
        Assert.True(login.Success, login.Message);
        Assert.NotNull(login.Token);
        return login;
    }

    internal void InsertMatch(string mode, bool ended)
        => Sql("""
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                started_utc,ended_utc,mode_id)
            VALUES('private-match-id','private-room',1,'private-player-0','private-player-1',
                'private-deck-0','private-deck-1','2026-10-06T00:00:00Z',$ended,$mode);
            """, ("$ended", ended ? "2026-10-06T00:01:00Z" : DBNull.Value), ("$mode", mode));

    internal void Sql(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = MatchPath,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    internal async Task StartServerAsync()
    {
        if (Server is not null) throw new InvalidOperationException("server already created");
        Server = new L12WebSocketServer(Rooms, Recorder, Platform, Catalog,
            rankedClockWatchdogInterval: TimeSpan.FromHours(1),
            sandboxReplayMaintenanceInterval: TimeSpan.FromHours(1));
        await Server.StartAsync(0);
        var address = new UriBuilder(Assert.Single(Server.Addresses)) { Host = "127.0.0.1" };
        BaseAddress = address.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Server is not null)
            {
                if (BaseAddress is not null)
                    await Server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            await Recorder.DisposeAsync();
            DeleteArtifactDirectory(Root);
        }
    }

    private static void DeleteArtifactDirectory(string directory)
    {
        var target = new DirectoryInfo(Path.GetFullPath(directory));
        var expectedParent = Path.GetFullPath(ArtifactRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (target.Parent is null
            || !string.Equals(target.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
                expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(target.Name, "N", out _))
            throw new InvalidOperationException($"refusing to delete unexpected test directory: {target.FullName}");
        SqliteConnection.ClearAllPools();
        if (target.Exists) target.Delete(recursive: true);
    }
}

internal sealed class SyntheticDeploymentFenceStore : IL12DeploymentDrainFenceStore
{
    private readonly object _gate = new();
    private readonly L12DeploymentDrainFenceLoadResult _initial;
    private L12DeploymentPersistedFence? _fence;

    internal SyntheticDeploymentFenceStore(L12DeploymentDrainFenceLoadResult? initial = null)
    {
        _initial = initial ?? L12DeploymentDrainFenceLoadResult.Missing;
    }

    internal bool WriteSucceeds { get; set; } = true;
    internal bool ClearSucceeds { get; set; } = true;

    public L12DeploymentDrainFenceLoadResult Load() => _initial;

    public bool TryWrite(L12DeploymentPersistedFence fence)
    {
        lock (_gate)
        {
            if (!WriteSucceeds) return false;
            _fence = fence;
            return true;
        }
    }

    public bool TryClear(string operationId, string targetCommit)
    {
        lock (_gate)
        {
            if (!ClearSucceeds) return false;
            if (_fence is not null && (_fence.OperationId != operationId
                || _fence.TargetCommit != targetCommit)) return false;
            _fence = null;
            return true;
        }
    }
}
