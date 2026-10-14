using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedChatAdmissionReadsTests
{
    private readonly ITestOutputHelper _output;
    public CommittedChatAdmissionReadsTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("accepted", "pending", true)]
    [InlineData("pending", "accepted", false)]
    [InlineData("suppressed", "accepted", false)]
    [InlineData("unknown", "accepted", false)]
    [InlineData("ACCEPTED", "accepted", false)]
    [InlineData("accepted ", "accepted", false)]
    [InlineData(null, "accepted", false)]
    public void R01_FirstUnorderedPairAuthorityIsExactAndHistoricalRowsRemainSaveable(
        string? firstStatus, string? laterStatus, bool accepted)
    {
        using var f = new Fixture();
        f.Mutate(() =>
        {
            AddFriend(f.Store, f.Recipient.Id, f.Sender.Id, firstStatus);
            AddFriend(f.Store, f.Sender.Id, f.Recipient.Id, laterStatus);
            AddFriend(f.Store, null, f.Recipient.Id, "accepted");
            AddFriend(f.Store, f.Sender.Id, f.Sender.Id, "accepted");
        });
        var view = f.Read();
        Assert.True(view.Available);
        Assert.Equal(accepted, view.AcceptedAndUnblocked);
        Assert.Equal(f.Store.AreFriends(f.Sender.Id, f.Recipient.Id), view.AcceptedAndUnblocked);
        Assert.Equal(view, f.Store.ReadCommittedChatRelationship(f.Recipient.Id, f.Sender.Id));
        Assert.Equal(f.Store.Version, view.Revision);
        Assert.InRange(Index(f.Store).Count, 0, Rows(f.Store, "Friends").Count);
        Assert.Equal(accepted, new L12PlatformStore(f.Path)
            .ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id).AcceptedAndUnblocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R02_EitherDirectionalBlockDeniesAnOtherwiseAcceptedPairAndInvalidIdsFailClosed(bool reverse)
    {
        using var f = new Fixture();
        f.Accept();
        f.Mutate(() => AddBlock(f.Store, reverse ? f.Recipient.Id : f.Sender.Id,
            reverse ? f.Sender.Id : f.Recipient.Id));
        Assert.True(f.Store.AreFriends(f.Sender.Id, f.Recipient.Id));
        Assert.False(f.Read().AcceptedAndUnblocked);
        Assert.False(f.Store.ReadCommittedChatRelationship(f.Recipient.Id, f.Sender.Id).AcceptedAndUnblocked);
        Assert.True(f.Store.UnblockAccount(reverse ? f.Recipient.Id : f.Sender.Id,
            reverse ? f.Sender.Id : f.Recipient.Id));
        Assert.True(f.Read().AcceptedAndUnblocked);
        foreach (var malformed in new string?[] { null, "", " ", "\0", "missing-account", " " + f.Sender.Id })
        {
            Assert.False(f.Store.ReadCommittedChatRelationship(malformed!, f.Recipient.Id).AcceptedAndUnblocked);
            Assert.False(f.Store.ReadCommittedChatRelationship(f.Sender.Id, malformed!).AcceptedAndUnblocked);
        }
        Assert.False(f.Store.ReadCommittedChatRelationship(f.Sender.Id, f.Sender.Id).AcceptedAndUnblocked);
        var upper = f.Sender.Id.ToUpperInvariant();
        if (upper != f.Sender.Id)
            Assert.False(f.Store.ReadCommittedChatRelationship(upper, f.Recipient.Id).AcceptedAndUnblocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R03_BeforeCommitReadersDoNotWaitOrSeeStagedBlockEvenWhenCallerOwnsGate(bool fail)
    {
        using var f = new Fixture();
        f.Accept();
        var old = f.Read();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            // Runs while the writer owns _gate and has already staged a block.
            Assert.Equal(old, f.Read());
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic chat barrier");
            if (fail) throw new IOException("synthetic rejected block");
        };
        var writer = Task.Factory.StartNew(() => f.Store.BlockAccount(f.Sender.Id, f.Recipient.Id),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var reader = Task.Factory.StartNew(f.Read, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.Equal(old, await reader.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            release.Set();
            try
            {
                if (fail) await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(
                    async () => await writer.WaitAsync(TimeSpan.FromSeconds(5)));
                else Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(5))).Success);
            }
            finally { f.Store.StorageFailureInjector = null; }
        }
        Assert.Equal(fail, f.Read().AcceptedAndUnblocked);
        if (fail) Assert.Equal(old, f.Read());
        else Assert.True(f.Read().Revision > old.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R04_NestedAndOuterTransactionsPublishOnlySuccessfulCommittedFacts(bool failOuter)
    {
        using var f = new Fixture();
        var old = f.Read();
        var index = Index(f.Store);
        bool Change() => f.Store.ExecuteAdminTransaction(() =>
        {
            f.Accept();
            Assert.False(f.Read().AcceptedAndUnblocked);
            Assert.Throws<InvalidOperationException>(() => f.Store.ExecuteAdminTransaction<bool>(() =>
            {
                Assert.True(f.Store.BlockAccount(f.Sender.Id, f.Recipient.Id).Success);
                Assert.Equal(old, f.Read());
                throw new InvalidOperationException("synthetic nested rejection");
            }));
            Assert.True(f.Store.AreFriends(f.Sender.Id, f.Recipient.Id));
            Assert.Equal(old, f.Read());
            if (failOuter) throw new InvalidOperationException("synthetic outer rejection");
            return true;
        });
        if (failOuter) Assert.Throws<InvalidOperationException>(() => Change());
        else Assert.True(Change());
        Assert.Equal(!failOuter, f.Read().AcceptedAndUnblocked);
        if (failOuter)
        {
            Assert.Equal(old, f.Read());
            Assert.Same(index, Index(f.Store));
        }
        Assert.Equal(!failOuter, new L12PlatformStore(f.Path)
            .ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id).AcceptedAndUnblocked);
    }

    [Fact]
    public void R05_MirrorFailureDoesNotUndoTheDatabaseCommittedBlock()
    {
        using var f = new Fixture();
        f.Accept();
        var old = f.Read();
        Directory.CreateDirectory(f.Path + ".tmp"); // Owned fixture blocks only the mirror writer.
        Assert.True(f.Store.BlockAccount(f.Sender.Id, f.Recipient.Id).Success);
        var current = f.Read();
        Assert.True(current.Available);
        Assert.False(current.AcceptedAndUnblocked);
        Assert.True(current.Revision > old.Revision);
        Assert.True(old.AcceptedAndUnblocked);
        Assert.False(new L12PlatformStore(f.Path)
            .ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id).AcceptedAndUnblocked);
    }

    [Fact]
    public void R06_LegacyMigrationRestartAndVerifiedReadonlyFallbackRebuildRelationshipFacts()
    {
        using var f = new Fixture();
        f.Accept();
        var data = Data(f.Store);
        var options = (JsonSerializerOptions)typeof(L12PlatformStore)
            .GetField("PlatformMigrationJsonOptions", StaticPrivate)!.GetValue(null)!;
        var legacy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f.Path)!, "legacy", "platform.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(legacy)!);
        var full = JsonSerializer.Serialize(data, data.GetType(), options);
        File.WriteAllText(legacy, full);
        var migrated = new L12PlatformStore(legacy);
        Assert.True(migrated.ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id).AcceptedAndUnblocked);
        Assert.True(new L12PlatformStore(legacy)
            .ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id).AcceptedAndUnblocked);
        File.WriteAllText(legacy, full);
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(migrated.TransactionalStoragePath, new byte[] { 1, 2, 3 });
        var fallback = new L12PlatformStore(legacy);
        Assert.Equal("json-fallback-readonly", fallback.StorageStatus().Mode);
        var snapshot = fallback.ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id);
        // The verified relationship snapshot is not a writable-storage/send permit.
        Assert.True(snapshot.Available);
        Assert.True(snapshot.AcceptedAndUnblocked);
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fallback.BlockAccount(f.Sender.Id, f.Recipient.Id));
        Assert.Equal(snapshot, fallback.ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("before-conflict-refresh")]
    [InlineData("after-conflict-snapshot-read")]
    public void R07_ConflictRefreshAdoptsNewBlockOrKeepsUnavailableClosed(string? fault)
    {
        using var f = new Fixture();
        f.Accept();
        var old = f.Read();
        var writer = new L12PlatformStore(f.Path);
        Assert.True(writer.BlockAccount(f.Sender.Id, f.Recipient.Id).Success);
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == fault) throw new IOException("synthetic refresh failure");
        };
        try
        {
            if (fault is null)
                Assert.Throws<L12PlatformStorageConflictException>(() => f.Store.ExecuteAdminTransaction(() =>
                    f.Store.Register("chatprobe", "Password123!")));
            else
                Assert.Throws<L12PlatformStorageRefreshException>(() => f.Store.ExecuteAdminTransaction(() =>
                    f.Store.Register("chatprobe", "Password123!")));
        }
        finally { f.Store.StorageFailureInjector = null; }
        var view = f.Read();
        Assert.False(view.AcceptedAndUnblocked);
        Assert.Equal(fault is null, view.Available);
        Assert.True(old.AcceptedAndUnblocked);
        if (fault is null) Assert.True(view.Revision > old.Revision);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("cache-damaged")]
    [InlineData("database-damaged")]
    public void R08_NormalAndExceptionalRollbackRestoreVerifiedFactsOrFailClosed(string mode)
    {
        using var f = new Fixture();
        f.Accept();
        var old = f.Read();
        var oldIndex = Index(f.Store);
        if (mode != "normal")
            typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", InstancePrivate)!
                .SetValue(f.Store, Array.Empty<byte>());
        if (mode == "database-damaged")
        {
            using var connection = new SqliteConnection($"Data Source={f.Store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE platform_state SET snapshot_sha256='invalid';";
            command.ExecuteNonQuery();
        }
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic rollback");
        };
        try { Assert.ThrowsAny<L12PlatformStorageUnavailableException>(
            () => f.Store.RemoveFriend(f.Sender.Id, f.Recipient.Id)); }
        finally { f.Store.StorageFailureInjector = null; }
        var view = f.Read();
        Assert.Equal(mode != "database-damaged", view.Available);
        Assert.Equal(mode != "database-damaged", view.AcceptedAndUnblocked);
        if (mode == "normal")
        {
            Assert.Equal(old, view);
            Assert.Same(oldIndex, Index(f.Store));
        }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("delete")]
    [InlineData("revoke")]
    [InlineData("expire")]
    [InlineData("relogin")]
    public void R09_AccountAndSessionLifecyclesDoNotTurnRelationshipSnapshotIntoSendPermission(string operation)
    {
        using var f = new Fixture();
        f.Accept();
        Assert.True(f.Store.IsSessionActive(f.Owner.SessionId));
        switch (operation)
        {
            case "disable":
                f.Store.SetAccountDisabled(f.Admin, f.Sender.Id, true, "synthetic", f.Context, true);
                break;
            case "delete":
                f.Store.DeleteAccountPersonalData(f.Admin, f.Sender.Id, "synthetic", f.Context, true);
                break;
            case "expire":
                f.Mutate(() =>
                {
                    var session = Rows(f.Store, "Sessions").Cast<object>().Single(row =>
                        (string)row.GetType().GetProperty("Id")!.GetValue(row)! == f.Owner.SessionId);
                    session.GetType().GetProperty("ExpiresAt")!.SetValue(session, DateTimeOffset.UtcNow.AddMinutes(-1));
                });
                break;
            default:
                f.Store.RevokeOwnSession(f.Owner, f.Owner.SessionId);
                break;
        }
        Assert.False(f.Store.IsSessionActive(f.Owner.SessionId));
        var relationshipSurvives = operation is "revoke" or "expire" or "relogin";
        Assert.Equal(relationshipSurvives, f.Read().AcceptedAndUnblocked);
        if (operation == "relogin")
        {
            var fresh = f.Store.AuthenticateTokenSession(f.Store.Login("chatone", "Password123!").Token)!;
            Assert.NotEqual(f.Owner.SessionId, fresh.SessionId);
            Assert.True(f.Store.IsSessionActive(fresh.SessionId));
            Assert.True(f.Read().AcceptedAndUnblocked);
        }
    }

    [Fact]
    public void R10_OldValueStaysASnapshotAndMissingOrUnavailableHolderDoesNotAuthorize()
    {
        using var f = new Fixture();
        f.Accept();
        var old = f.Read();
        Assert.True(typeof(L12CommittedChatRelationshipView).IsValueType);
        Assert.True(typeof(L12CommittedChatRelationshipView).IsDefined(typeof(IsReadOnlyAttribute), false));
        Assert.Equal(new[] { "AcceptedAndUnblocked", "Available", "Revision" },
            typeof(L12CommittedChatRelationshipView).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.True(f.Store.BlockAccount(f.Sender.Id, f.Recipient.Id).Success);
        Assert.True(old.AcceptedAndUnblocked);
        Assert.False(f.Read().AcceptedAndUnblocked);
        Assert.True(f.Read().Revision > old.Revision);
        var holder = Holder(f.Store);
        holder.GetType().GetProperty("PresenceReads")!.SetValue(holder, null);
        Assert.False(f.Read().Available);
        Assert.False(f.Read().AcceptedAndUnblocked);
        var unavailable = typeof(L12PlatformStore).GetField("UnavailableSessionActivity", StaticPrivate)!.GetValue(null)!;
        typeof(L12PlatformStore).GetField("_committedSessionActivity", InstancePrivate)!.SetValue(f.Store, unavailable);
        Assert.False(f.Read().Available);
        Assert.False(f.Read().AcceptedAndUnblocked);
    }

    [Fact]
    public void R11_TenThousandWarmedPointReadsAllocateZeroAndPerformNoStorageOrProjectionWork()
    {
        using var f = new Fixture();
        f.Accept();
        f.Mutate(() => AddUnrelated(f.Store, 4000));
        var presence = Presence(f.Store);
        var index = Index(f.Store);
        var version = f.Store.Version;
        for (var iteration = 0; iteration < 10000; iteration++) _ = f.Read();
        using var storage = L12PlatformStore.BeginSyntheticStorageMeasurement();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = 0;
        for (var iteration = 0; iteration < 10000; iteration++)
            if (f.Store.ReadCommittedChatRelationship(f.Sender.Id, f.Recipient.Id).AcceptedAndUnblocked) accepted++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var measurement = storage.Complete();
        Assert.Equal(10000, accepted);
        Assert.Equal(0L, allocated);
        Assert.Equal(0, measurement.Connections);
        Assert.Equal(0, measurement.SelectStatements + measurement.InsertStatements
            + measurement.UpdateStatements + measurement.DeleteStatements + measurement.OtherStatements);
        Assert.Equal(0L, measurement.MirrorBytes);
        Assert.Equal(version, f.Store.Version);
        Assert.Same(presence, Presence(f.Store));
        Assert.Same(index, Index(f.Store));
        Assert.InRange(index.Count, 0, Rows(f.Store, "Friends").Count);
        _output.WriteLine($"C1A R11 warmup=10000 reads=10000 allocatedBytes={allocated} sqlConnections={measurement.Connections} indexEntries={index.Count}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(4096)]
    public void R12_IndexPreparationIsSeparatelyBoundedAndUnchangedOrderedFactsReuseTheSameIndex(int rowCount)
    {
        using var f = new Fixture();
        f.Mutate(() => AddUnrelated(f.Store, rowCount));
        var presence = Presence(f.Store);
        var friends = presence.GetType().GetProperty("Friends")!.GetValue(presence)!;
        var prepare = typeof(L12PlatformStore).GetMethod("PrepareCommittedChatPairs", StaticPrivate)!;
        var freshArguments = new object?[] { friends, null };
        _ = prepare.Invoke(null, freshArguments); // Warm reflection/JIT outside the measurement.
        var start = GC.GetAllocatedBytesForCurrentThread();
        var fresh = (IReadOnlyDictionary<(string, string), bool>)prepare.Invoke(null, freshArguments)!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        var ceiling = 256L * rowCount + 65536;
        Assert.InRange(allocated, 0, ceiling);
        Assert.InRange(fresh.Count, 0, rowCount);
        var reused = prepare.Invoke(null, new[] { friends, presence });
        Assert.Same(Index(f.Store), reused);
        var oldIndex = Index(f.Store);
        f.Mutate(() =>
        {
            if (rowCount > 0)
            {
                var row = Rows(f.Store, "Friends")[0]!;
                var property = row.GetType().GetProperty("CreatedAt")!;
                property.SetValue(row, ((DateTimeOffset)property.GetValue(row)!).AddTicks(1));
            }
            RankedIdentityTestState.SetAccountUsername(f.Store, f.Recipient.Id, "chatrenamed");
        });
        Assert.Same(oldIndex, Index(f.Store));
        var oldHolder = Holder(f.Store);
        // Blocking changes its own committed set, not the ordered relationship index.
        Assert.True(f.Store.BlockAccount(f.Sender.Id, f.Recipient.Id).Success);
        Assert.Same(oldIndex, Index(f.Store));
        Assert.NotSame(oldHolder, Holder(f.Store));
        if (rowCount > 0)
        {
            f.Mutate(() =>
            {
                var row = Rows(f.Store, "Friends")[0]!;
                row.GetType().GetProperty("Status")!.SetValue(row, "pending");
            });
            Assert.NotSame(oldIndex, Index(f.Store));
        }
        _output.WriteLine($"C1A R12 sourceRows={rowCount} indexEntries={fresh.Count} newIndexPreparationAllocatedBytes={allocated} ceilingBytes={ceiling} unchangedIndexRebuilds=0");
    }

    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static object Data(L12PlatformStore store) =>
        typeof(L12PlatformStore).GetProperty("_data", InstancePrivate)!.GetValue(store)!;
    private static IList Rows(L12PlatformStore store, string property) =>
        (IList)Data(store).GetType().GetProperty(property)!.GetValue(Data(store))!;
    private static object Holder(L12PlatformStore store) =>
        typeof(L12PlatformStore).GetField("_committedSessionActivity", InstancePrivate)!.GetValue(store)!;
    private static object Presence(L12PlatformStore store) =>
        Holder(store).GetType().GetProperty("PresenceReads")!.GetValue(Holder(store))!;
    private static IReadOnlyDictionary<(string, string), bool> Index(L12PlatformStore store) =>
        (IReadOnlyDictionary<(string, string), bool>)Presence(store).GetType()
            .GetProperty("ChatRelationships")!.GetValue(Presence(store))!;

    private static void AddFriend(L12PlatformStore store, string? first, string? second, string? status)
    {
        var rows = Rows(store, "Friends");
        var row = Activator.CreateInstance(rows.GetType().GetGenericArguments()[0], nonPublic: true)!;
        row.GetType().GetProperty("RequesterId")!.SetValue(row, first);
        row.GetType().GetProperty("AddresseeId")!.SetValue(row, second);
        row.GetType().GetProperty("Status")!.SetValue(row, status);
        rows.Add(row);
    }

    private static void AddBlock(L12PlatformStore store, string first, string second)
    {
        var rows = Rows(store, "BlockedAccounts");
        var row = Activator.CreateInstance(rows.GetType().GetGenericArguments()[0], nonPublic: true)!;
        row.GetType().GetProperty("AccountId")!.SetValue(row, first);
        row.GetType().GetProperty("BlockedAccountId")!.SetValue(row, second);
        rows.Add(row);
    }

    private static void AddUnrelated(L12PlatformStore store, int count)
    {
        for (var index = 0; index < count; index++)
            AddFriend(store, $"synthetic-a-{index:D6}", $"synthetic-b-{index:D6}", "accepted");
    }

    private static void Save(L12PlatformStore store)
    {
        try { typeof(L12PlatformStore).GetMethod("Save", InstancePrivate)!.Invoke(store, new object[] { true }); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-committed-chat", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal L12AccountView Sender => Owner.Account;
        internal L12AccountView Recipient { get; }
        internal L12AuthenticatedSession Owner { get; }
        internal L12AdminAuditContext Context { get; } = new("committed-chat-test", "admin.accounts.status.write");
        internal Fixture()
        {
            Store = new L12PlatformStore(Path);
            Admin = Store.Login("Admin", "L12master").Account!;
            Owner = Store.AuthenticateTokenSession(Store.Register("chatone", "Password123!").Token)!;
            Recipient = Store.Register("chattwo", "Password123!").Account!;
        }
        internal L12CommittedChatRelationshipView Read() => Store.ReadCommittedChatRelationship(Sender.Id, Recipient.Id);
        internal void Accept()
        {
            Assert.True(Store.SendFriendRequest(Sender.Id, Recipient.Id).Success);
            Assert.True(Store.ResolveFriendRequest(Recipient.Id, Sender.Id, true).Success);
        }
        internal void Mutate(Action action) => Store.ExecuteAdminTransaction(() =>
        {
            action();
            Save(Store);
            return true;
        });
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
