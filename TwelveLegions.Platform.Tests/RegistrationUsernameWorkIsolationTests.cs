using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RegistrationUsernameWorkIsolationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task RegistrationPasswordWorkDoesNotHoldSharedGate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-register-work-{Guid.NewGuid():N}");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var readStarted = new ManualResetEventSlim();
        Task<long>? read = null;
        Task<(bool Success, string Message, L12AccountView? Account, string? Token)>? registration = null;
        var readCompletedWhilePasswordWorkBlocked = false;
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            store.BeforeRegistrationPasswordWorkForTesting = () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Synthetic registration password barrier timed out.");
            };
            registration = Task.Factory.StartNew(() => store.Register("regisolate", "password-123"),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Actual registration must reach password work.");
            read = Task.Factory.StartNew(() =>
            {
                readStarted.Set();
                return store.Version;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(readStarted.Wait(TimeSpan.FromSeconds(5)));
            readCompletedWhilePasswordWorkBlocked = await Task.WhenAny(read, Task.Delay(1_000)) == read;
        }
        finally
        {
            release.Set();
            if (registration is not null)
            {
                var result = await registration.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(result.Success, result.Message);
            }
            if (read is not null) await read.WaitAsync(TimeSpan.FromSeconds(10));
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        Assert.True(readCompletedWhilePasswordWorkBlocked,
            "The actual registration password barrier must not block an unrelated platform gate read.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsernameChangePasswordWorkDoesNotHoldSharedGate(bool selfService)
    {
        using var f = new Fixture();
        var identity = selfService ? f.Register("selflock") : f.RegisterForced("forcelock");
        using var barrier = new Barrier();
        f.Store.BeforeUsernameChangePasswordVerificationForTesting = barrier.Block;
        var attempt = Run(() => ChangeUsername(f.Store, selfService, identity, "locktarget"));
        Task<long>? read = null;
        try
        {
            barrier.Wait();
            read = Run(() => f.Store.Version);
            Assert.Same(read, await Task.WhenAny(read, Task.Delay(1_000)));
        }
        finally { barrier.Release(); }
        var result = await attempt.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success, result.Message);
        if (read is not null) await read.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("forced")]
    [InlineData("self")]
    public void BusyPasswordWorkLeavesRegistrationAndUsernameStateUntouched(string operation)
    {
        using var f = new Fixture();
        var identity = operation switch
        {
            "forced" => f.RegisterForced("busyforce"),
            "self" => f.Register("busyself"),
            _ => null,
        };
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        var beforeVersion = f.Store.Version;
        var beforeAccounts = AccountState(f.Store);
        var beforeSessions = SessionState(f.Store);
        var beforeRenameCount = identity is null ? -1 : f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed;
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;

        using (f.Store.PasswordWorkBudget.Acquire())
        {
            Assert.Throws<L12AuthenticationBusyException>(() =>
            {
                if (operation == "register") f.Store.Register("busynew", Fixture.Password);
                else ChangeUsername(f.Store, operation == "self", identity!, "busytarget");
            });
        }

        Assert.Equal(beforeVersion, f.Store.Version);
        Assert.Equal(beforeAccounts, AccountState(f.Store));
        Assert.Equal(beforeSessions, SessionState(f.Store));
        Assert.Equal(0, notifications);
        Assert.DoesNotContain(f.Store.Accounts(), row =>
            string.Equals(row.Username, "busynew", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Username, "busytarget", StringComparison.OrdinalIgnoreCase));
        if (identity is not null)
            Assert.Equal(beforeRenameCount, f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed);
    }

    [Fact]
    public async Task SameStoreRegistrationsLinearizeCaseInsensitiveDuplicate()
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var arrived = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        f.Store.BeforeRegistrationPasswordWorkForTesting = () => ArriveAndWait(arrived, release);
        var first = Run(() => f.Store.Register("raceclaim", Fixture.Password));
        var second = Run(() => f.Store.Register("RACECLAIM", Fixture.Password));
        try
        {
            Assert.True(arrived.Wait(TimeSpan.FromSeconds(5)), "Both registrations must reach password work.");
        }
        finally { release.Set(); }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        var winner = Assert.Single(results.Where(row => row.Success));
        Assert.True(winner.Success, winner.Message);
        Assert.NotNull(winner.Account);
        Assert.NotNull(winner.Token);
        var loser = Assert.Single(results.Where(row => !row.Success));
        Assert.Equal("用户名已存在", loser.Message);
        Assert.Null(loser.Account);
        Assert.Null(loser.Token);
        Assert.Single(f.Store.Accounts().Where(row =>
            string.Equals(row.Username, "raceclaim", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(f.Store.AuthenticateTokenSession(winner.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistrationAndUsernameChangeLinearizeCaseInsensitiveDuplicate(bool selfService)
    {
        using var f = new Fixture();
        var identity = selfService ? f.Register("selfrace") : f.RegisterForced("forcerace");
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var arrived = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        Action rendezvous = () => ArriveAndWait(arrived, release);
        f.Store.BeforeRegistrationPasswordWorkForTesting = rendezvous;
        f.Store.BeforeUsernameChangePasswordVerificationForTesting = rendezvous;
        var registration = Run(() => f.Store.Register("MIXCLAIM", Fixture.Password));
        var rename = Run(() => ChangeUsername(f.Store, selfService, identity, "mixclaim"));
        try
        {
            Assert.True(arrived.Wait(TimeSpan.FromSeconds(5)), "Registration and rename must both reach password work.");
        }
        finally { release.Set(); }
        var registered = await registration.WaitAsync(TimeSpan.FromSeconds(10));
        var renamed = await rename.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, (registered.Success ? 1 : 0) + (renamed.Success ? 1 : 0));
        if (registered.Success)
        {
            Assert.True(registered.Success, registered.Message);
            Assert.NotNull(registered.Token);
            Assert.False(renamed.Success);
            Assert.Equal("用户名已存在", renamed.Message);
        }
        else
        {
            Assert.Equal("用户名已存在", registered.Message);
            Assert.Null(registered.Token);
            Assert.True(renamed.Success, renamed.Message);
        }
        Assert.Single(f.Store.Accounts().Where(row => !row.Deleted
            && string.Equals(row.Username, "mixclaim", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(renamed.Success && selfService ? 1 : 0, f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("forced")]
    [InlineData("self")]
    public void MigratedEntriesRejectOuterTransactionBeforeAnyPasswordWork(string operation)
    {
        using var f = new Fixture();
        var identity = operation == "forced" ? f.RegisterForced("txforce") : f.Register("txself");
        var beforeAccounts = AccountState(f.Store);
        var beforeVersion = f.Store.Version;
        Assert.Throws<InvalidOperationException>(() => f.Store.ExecuteAdminTransaction(() =>
        {
            if (operation == "register") return f.Store.Register("txnew", Fixture.Password).Success;
            return ChangeUsername(f.Store, operation == "self", identity, "txchanged").Success;
        }));
        Assert.Equal(beforeVersion, f.Store.Version);
        Assert.Equal(beforeAccounts, AccountState(f.Store));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("forced")]
    [InlineData("self")]
    public void CommitFailureRollsBackIdentityChanceAuditAndSessionsThenAllowsRetry(string operation)
    {
        using var f = new Fixture();
        var identity = operation == "forced" ? f.RegisterForced("failforce") : f.Register("failself");
        var other = f.Store.AuthenticateTokenSession(f.Store.Login(identity.Username, Fixture.Password).Token)!;
        var beforeAccounts = AccountState(f.Store);
        var beforeSessions = SessionState(f.Store);
        var beforeVersion = f.Store.Version;
        var beforeAudit = f.Store.AdminAudit("account", 100).Select(row => row.Id).ToArray();
        var beforeChance = f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed;
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        bool Perform() => operation == "register"
            ? f.Store.Register("retrytarget", Fixture.Password).Success
            : ChangeUsername(f.Store, operation == "self", identity, "retrytarget").Success;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic registration/rename commit fault");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => Perform());
        f.Store.StorageFailureInjector = null;
        Assert.Equal(beforeVersion, f.Store.Version);
        Assert.Equal(beforeAccounts, AccountState(f.Store));
        Assert.Equal(beforeSessions, SessionState(f.Store));
        Assert.Equal(beforeAudit, f.Store.AdminAudit("account", 100).Select(row => row.Id));
        Assert.Equal(beforeChance, f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed);
        Assert.Equal(0, notifications);
        Assert.True(Perform());
        Assert.Single(new L12PlatformStore(f.Path).Accounts().Where(row => row.Username == "retrytarget"));
        Assert.True(f.Store.IsSessionActive(identity.SessionId));
        Assert.Equal(operation == "register", f.Store.IsSessionActive(other.SessionId));
        Assert.Equal(operation == "register" ? 0 : 1, notifications);
    }

    [Theory]
    [InlineData(false, "reset")]
    [InlineData(true, "reset")]
    [InlineData(false, "rename")]
    [InlineData(true, "rename")]
    [InlineData(false, "disable")]
    [InlineData(true, "disable")]
    [InlineData(false, "role")]
    [InlineData(true, "role")]
    [InlineData(false, "flag")]
    [InlineData(true, "flag")]
    public async Task RenameCannotUseStalePasswordProofAfterAccountChanges(bool selfService, string change)
    {
        using var f = new Fixture();
        var identity = selfService ? f.Register("casuser") : f.RegisterForced("casuser");
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var barrier = new Barrier();
        f.Store.BeforeUsernameChangePasswordVerificationForTesting = barrier.Block;
        var pending = Run(() => ChangeUsername(f.Store, selfService, identity, "casloser"));
        try
        {
            barrier.Wait();
            f.Store.BeforeUsernameChangePasswordVerificationForTesting = null;
            switch (change)
            {
                case "reset":
                    Assert.True(f.Store.ChangePassword(identity.Id, Fixture.Password, "reset-winner-123").Success);
                    break;
                case "rename":
                    Assert.True(ChangeUsername(f.Store, selfService, identity, "caswinner").Success);
                    break;
                case "disable":
                    Assert.True(f.Store.SetAccountDisabled(f.Admin, identity.Id, true, "synthetic disable",
                        new L12AdminAuditContext("synthetic-rename", "admin.accounts.status.write"), true).Applied);
                    break;
                case "role":
                    SetAccountProperty(f.Store, identity.Id, "Role", "admin");
                    SetAccountProperty(f.Store, identity.Id, "PermissionVersion", 2);
                    break;
                case "flag":
                    SetAccountProperty(f.Store, identity.Id, "MustChangeUsername", selfService);
                    break;
            }
        }
        finally { barrier.Release(); }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.Null(result.Account);
        Assert.DoesNotContain(f.Store.Accounts(), row => row.Username == "casloser");
        Assert.Equal(selfService && change == "rename" ? 1 : 0,
            f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("forced")]
    [InlineData("self")]
    public async Task CrossStoreConflictRefreshesLatestFactsWithoutRetryOrNotification(string operation)
    {
        using var f = new Fixture();
        var identity = operation == "forced" ? f.RegisterForced("xforce") : f.Register("xself");
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        using var barrier = new Barrier();
        if (operation == "register") f.Store.BeforeRegistrationPasswordWorkForTesting = barrier.Block;
        else f.Store.BeforeUsernameChangePasswordVerificationForTesting = barrier.Block;
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        var attempt = Run(() => operation == "register"
            ? f.Store.Register("xloser", Fixture.Password).Success
            : ChangeUsername(f.Store, operation == "self", identity, "xloser").Success);
        try
        {
            barrier.Wait();
            Assert.True(new L12PlatformStore(f.Path).Register("xwinner", Fixture.Password).Success);
        }
        finally { barrier.Release(); }
        await Assert.ThrowsAsync<L12PlatformStorageConflictException>(async () =>
            await attempt.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains(f.Store.Accounts(), row => row.Username == "xwinner");
        Assert.DoesNotContain(f.Store.Accounts(), row => row.Username == "xloser");
        Assert.Equal(identity.Username, f.Store.Account(identity.Id)!.Username);
        Assert.Equal(0, f.Store.UsernameChangeStatus(identity.Id).FreeRenameUsed);
        Assert.True(f.Store.IsSessionActive(identity.SessionId));
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationSessionPublishesOnlyAfterItsDurableCommit(bool fail)
    {
        using var f = new Fixture();
        string? stagedId = null;
        var activityBeforeCommit = new List<bool>();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            var account = f.Store.Accounts().Single(row => row.Username == "stageduser");
            stagedId = Assert.Single(f.Store.Sessions(account.Id)).Id;
            activityBeforeCommit.Add(f.Store.IsSessionActive(stagedId));
            if (fail) throw new IOException("synthetic staged registration fault");
        };
        if (fail) Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.Register("stageduser", Fixture.Password));
        else Assert.True(f.Store.Register("stageduser", Fixture.Password).Success);
        Assert.NotNull(stagedId);
        Assert.Equal(new[] { false }, activityBeforeCommit);
        Assert.Equal(!fail, f.Store.IsSessionActive(stagedId!));
        Assert.Equal(!fail, new L12PlatformStore(f.Path).IsSessionActive(stagedId!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenamePreservesCurrentSessionAndPublishesRevocationOnlyAfterCommit(bool selfService)
    {
        using var f = new Fixture();
        var identity = selfService ? f.Register("sessself") : f.RegisterForced("sessforce");
        var other = f.Store.AuthenticateTokenSession(f.Store.Login(identity.Username, Fixture.Password).Token)!;
        var notifications = 0;
        var notifiedIds = new List<string>();
        var activityAtNotification = new List<(bool Other, bool Current)>();
        f.Store.SessionsRevoked += ids =>
        {
            notifications++;
            notifiedIds.AddRange(ids);
            activityAtNotification.Add((f.Store.IsSessionActive(other.SessionId), f.Store.IsSessionActive(identity.SessionId)));
        };
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            Assert.Equal(0, notifications);
            Assert.True(f.Store.IsSessionActive(other.SessionId));
            Assert.True(f.Store.IsSessionActive(identity.SessionId));
        };
        Assert.True(ChangeUsername(f.Store, selfService, identity, "sessionnew").Success);
        Assert.Equal(1, notifications);
        Assert.Equal(new[] { other.SessionId }, notifiedIds);
        Assert.Equal(new[] { (false, true) }, activityAtNotification);
        Assert.False(f.Store.IsSessionActive(other.SessionId));
        Assert.True(f.Store.IsSessionActive(identity.SessionId));
    }

    private static (bool Success, string Message, L12AccountView? Account) ChangeUsername(
        L12PlatformStore store, bool selfService, Identity identity, string newUsername)
        => selfService
            ? store.SelfServiceChangeUsername(identity.Id, Fixture.Password, newUsername, identity.SessionId)
            : store.ChangeUsername(identity.Id, Fixture.Password, newUsername, identity.SessionId);

    private static void ArriveAndWait(CountdownEvent arrived, ManualResetEventSlim release)
    {
        arrived.Signal();
        if (!release.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Synthetic password rendezvous timed out.");
    }

    private static Task<T> Run<T>(Func<T> action) => Task.Factory.StartNew(action,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static string[] AccountState(L12PlatformStore store) => store.Accounts()
        .OrderBy(row => row.Id, StringComparer.Ordinal)
        .Select(row => $"{row.Id}|{row.Username}|{row.Role}|{row.PermissionVersion}|{row.Disabled}|" +
            $"{row.MustChangePassword}|{row.MustChangeUsername}|{row.Deleted}")
        .ToArray();

    private static string[] SessionState(L12PlatformStore store) => store.Accounts()
        .SelectMany(account => store.Sessions(account.Id).Select(session => $"{account.Id}|{session.Id}"))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static void SetAccountProperty(L12PlatformStore store, string accountId, string property, object value)
    {
        store.ExecuteAdminTransaction(() =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
            var accounts = (IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
            var row = accounts.Cast<object>().Single(item =>
                (string)item.GetType().GetProperty("Id")!.GetValue(item)! == accountId);
            row.GetType().GetProperty(property)!.SetValue(row, value);
            typeof(L12PlatformStore).GetMethod("Save", PrivateInstance)!.Invoke(store, [true]);
            return true;
        });
    }

    private sealed record Identity(string Id, string Username, string Token, string SessionId);

    private sealed class Barrier : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();

        internal void Block()
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Synthetic password barrier timed out.");
        }

        internal void Wait() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(5)),
            "Password barrier was not reached.");

        internal void Release() => _release.Set();

        public void Dispose()
        {
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Password = "password-123";
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"l12-register-username-{Guid.NewGuid():N}");
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }

        internal Fixture()
        {
            Store = new L12PlatformStore(Path);
            var login = Store.Login("Admin", "L12master");
            Assert.True(login.Success, login.Message);
            Admin = login.Account!;
        }

        internal Identity Register(string username)
        {
            var result = Store.Register(username, Password);
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Account);
            Assert.NotNull(result.Token);
            var session = Store.AuthenticateTokenSession(result.Token);
            Assert.NotNull(session);
            return new(result.Account!.Id, result.Account.Username, result.Token!, session!.SessionId);
        }

        internal Identity RegisterForced(string username)
        {
            var identity = Register(username);
            SetAccountProperty(Store, identity.Id, "MustChangeUsername", true);
            return identity;
        }

        public void Dispose()
        {
            Store.BeforeRegistrationPasswordWorkForTesting = null;
            Store.BeforeUsernameChangePasswordVerificationForTesting = null;
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
