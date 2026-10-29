using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PasswordWorkIsolationTests
{
    [Fact]
    public async Task LoginPasswordWorkDoesNotHoldTheSharedPlatformGate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-pw-isolation-{Guid.NewGuid():N}");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var readStarted = new ManualResetEventSlim();
        Task<long>? read = null;
        Task<L12AuthenticationResult>? login = null;
        var readCompletedWhilePasswordWorkBlocked = false;
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            const string username = "pwisolate";
            const string password = "password-123";
            var registered = store.Register(username, password);
            Assert.True(registered.Success, registered.Message);
            store.BeforeLoginPasswordVerificationForTesting = () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic password barrier timed out.");
            };
            login = Task.Factory.StartNew(() => store.Login(username, password),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Actual login must reach password work.");
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
            if (login is not null) Assert.True((await login.WaitAsync(TimeSpan.FromSeconds(10))).Success);
            if (read is not null) await read.WaitAsync(TimeSpan.FromSeconds(10));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        Assert.True(readCompletedWhilePasswordWorkBlocked,
            "The actual login password barrier must not block an unrelated platform gate read.");
    }

    [Theory]
    [InlineData("reset", false)]
    [InlineData("rename", false)]
    [InlineData("disable", false)]
    [InlineData("disable-enable", false)]
    [InlineData("reset", true)]
    [InlineData("rename", true)]
    [InlineData("disable", true)]
    [InlineData("disable-enable", true)]
    public async Task LoginRechecksActualLifecycleChangesAfterPasswordWork(string change, bool asyncLogin)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var barrier = new Barrier();
        f.Store.BeforeLoginPasswordVerificationForTesting = barrier.Block;
        var attempt = StartLogin(f.Store, asyncLogin, Fixture.Username, Fixture.Password);
        try
        {
            barrier.Wait();
            f.Change(change);
        }
        finally { barrier.Release(); }
        var result = await attempt.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.DoesNotContain(f.Store.AdminAudit("authentication", 100), row => row.Reason == "authenticated"
            && row.ActorId == f.Owner.Id);
    }

    [Theory]
    [InlineData("PasswordHash", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("Salt", "AAAAAAAAAAAAAAAAAAAAAA==", false)]
    [InlineData("PasswordAlgorithm", "unknown", false)]
    [InlineData("PasswordIterations", 600000, false)]
    [InlineData("PasswordCredentialVersion", 2, false)]
    [InlineData("PermissionVersion", 2, false)]
    [InlineData("MustChangePassword", true, false)]
    [InlineData("MustChangeUsername", true, false)]
    [InlineData("PasswordHash", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    [InlineData("Salt", "AAAAAAAAAAAAAAAAAAAAAA==", true)]
    [InlineData("PasswordAlgorithm", "unknown", true)]
    [InlineData("PasswordIterations", 600000, true)]
    [InlineData("PasswordCredentialVersion", 2, true)]
    [InlineData("PermissionVersion", 2, true)]
    [InlineData("MustChangePassword", true, true)]
    [InlineData("MustChangeUsername", true, true)]
    public async Task LoginCasComparesEveryCapturedField(string field, object value, bool asyncLogin)
    {
        using var f = new Fixture();
        using var barrier = new Barrier();
        f.Store.BeforeLoginPasswordVerificationForTesting = barrier.Block;
        var attempt = StartLogin(f.Store, asyncLogin, Fixture.Username, Fixture.Password);
        try
        {
            barrier.Wait();
            f.Store.ExecuteAdminTransaction(() =>
            {
                var row = f.Row;
                row.GetType().GetProperty(field)!.SetValue(row, value);
                typeof(L12PlatformStore).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(f.Store, [false]);
                return true;
            });
        }
        finally { barrier.Release(); }
        var result = await attempt.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.Null(result.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorrectPasswordFinishingAfterFifthFailureCannotClearEitherLockBucket(bool asyncLogin)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var barrier = new Barrier();
        var calls = 0;
        f.Store.BeforeLoginPasswordVerificationForTesting = () =>
        {
            if (Interlocked.Increment(ref calls) == 1) barrier.Block();
        };
        var context = new L12LoginAttemptContext("synthetic", "shared-client", "/api/auth/login");
        var correct = StartLogin(f.Store, asyncLogin, Fixture.Username, Fixture.Password, context);
        try
        {
            barrier.Wait();
            for (var index = 0; index < 5; index++)
                Assert.Equal(index == 4 ? "login_rate_limited" : "authentication_failed",
                    (await StartLogin(f.Store, asyncLogin, Fixture.Username, "wrong-password", context)).Code);
        }
        finally { barrier.Release(); }
        Assert.Equal("login_rate_limited", (await correct.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal("login_rate_limited", f.Store.Login("PWISOLATE", Fixture.Password,
            context with { ClientKey = "other-client" }).Code);
        Assert.Equal("login_rate_limited", f.Store.Login("Admin", "L12master", context).Code);
        var reloaded = new L12PlatformStore(f.Path);
        Assert.Equal("login_rate_limited", reloaded.Login(Fixture.Username, Fixture.Password,
            context with { ClientKey = "new-client" }).Code);
    }

    [Fact]
    public void BusyWorkDoesNotMutateVersionAuditCredentialsSessionsOrFailureBuckets()
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        for (var index = 0; index < 4; index++)
            Assert.Equal("authentication_failed", f.Store.Login(Fixture.Username, "wrong-password").Code);
        var beforeVersion = f.Store.Version;
        var beforeAudit = f.Store.AdminAudit("authentication", 100).Count;
        var beforeSessions = f.Store.Sessions(f.Owner.Id).Select(row => row.Id).ToArray();
        using (f.Store.PasswordWorkBudget.Acquire())
        {
            for (var index = 0; index < 6; index++)
                Assert.Throws<L12AuthenticationBusyException>(() => f.Store.Login(Fixture.Username, Fixture.Password));
            Assert.Throws<L12AuthenticationBusyException>(() => f.Store.ChangePassword(f.Owner.Id,
                Fixture.Password, "replacement-123"));
        }
        Assert.Equal(beforeVersion, f.Store.Version);
        Assert.Equal(beforeAudit, f.Store.AdminAudit("authentication", 100).Count);
        Assert.Equal(beforeSessions, f.Store.Sessions(f.Owner.Id).Select(row => row.Id));
        Assert.True(f.Store.Login(Fixture.Username, Fixture.Password).Success);
    }

    [Fact]
    public async Task BusyHttpResponseIsRetryable503NotInvalidCredentialsOrPersistentLockout()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await L12HttpExceptionBoundary.InvokeAsync(context, () => throw new L12AuthenticationBusyException());
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("authentication_busy", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void BudgetHasBoundedWaitAndIdempotentRelease()
    {
        var budget = new L12PasswordWorkBudget(1, 1, TimeSpan.FromMilliseconds(20));
        var first = budget.Acquire();
        Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
        first.Dispose();
        first.Dispose();
        using var second = budget.Acquire();
        Assert.Throws<L12AuthenticationBusyException>(() => budget.Acquire());
    }

    [Fact]
    public void DefaultBudgetIsSharedByIndependentStores()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        Assert.Same(L12PasswordWorkBudget.Shared, first.Store.PasswordWorkBudget);
        Assert.Same(first.Store.PasswordWorkBudget, second.Store.PasswordWorkBudget);
    }

    [Fact]
    public void FaultAndWrongPasswordReleaseTheCpuPermit()
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        f.Store.BeforeLoginPasswordVerificationForTesting = () => throw new IOException("synthetic work fault");
        Assert.Throws<IOException>(() => f.Store.Login(Fixture.Username, Fixture.Password));
        f.Store.BeforeLoginPasswordVerificationForTesting = null;
        Assert.False(f.Store.ChangePassword(f.Owner.Id, "wrong-password", "replacement-123").Success);
        Assert.True(f.Store.Login(Fixture.Username, Fixture.Password).Success);
    }

    [Fact]
    public void MigratedCallsRefuseOuterTransactionRatherThanComputeUnderItsGate()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidOperationException>(() => f.Store.ExecuteAdminTransaction(() =>
            f.Store.Login(Fixture.Username, Fixture.Password)));
        Assert.Throws<InvalidOperationException>(() => f.Store.ExecuteAdminTransaction(() =>
            f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "replacement-123")));
        Assert.True(f.Store.Login(Fixture.Username, Fixture.Password).Success);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("rename")]
    [InlineData("disable")]
    [InlineData("disable-enable")]
    public async Task ChangePasswordCannotOverwriteAnInterveningLifecycleChange(string change)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var barrier = new Barrier();
        f.Store.BeforePasswordChangeVerificationForTesting = barrier.Block;
        var attempt = Run(() => f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "replacement-123"));
        try
        {
            barrier.Wait();
            f.Change(change);
        }
        finally { barrier.Release(); }
        Assert.False((await attempt.WaitAsync(TimeSpan.FromSeconds(10))).Success);
        f.Store.BeforePasswordChangeVerificationForTesting = null;
        Assert.False(f.Store.Login(change == "rename" ? "pwrenamed" : Fixture.Username, "replacement-123").Success);
    }

    [Fact]
    public async Task PasswordChangeWorkDoesNotHoldThePlatformGate()
    {
        using var f = new Fixture();
        using var barrier = new Barrier();
        f.Store.BeforePasswordChangeVerificationForTesting = barrier.Block;
        var attempt = Run(() => f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "replacement-123"));
        try
        {
            barrier.Wait();
            Assert.True(await Run(() => f.Store.AccountExists(f.Owner.Id)).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { barrier.Release(); }
        Assert.True((await attempt.WaitAsync(TimeSpan.FromSeconds(10))).Success);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task StorageFailureRollsBackLoginAndChangePasswordWithoutLeakingAdmission(bool changePassword, bool asyncLogin)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        var beforeSessions = f.Store.Sessions(f.Owner.Id).Select(row => row.Id).ToArray();
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic commit fault");
        };
        await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(async () =>
        {
            if (changePassword) await Run(() => f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "replacement-123"));
            else await StartLogin(f.Store, asyncLogin, Fixture.Username, Fixture.Password);
        });
        f.Store.StorageFailureInjector = null;
        Assert.Equal(0, notifications);
        Assert.Equal(beforeSessions, f.Store.Sessions(f.Owner.Id).Select(row => row.Id));
        Assert.True(f.Store.Login(Fixture.Username, Fixture.Password).Success);
        Assert.False(new L12PlatformStore(f.Path).Login(Fixture.Username, "replacement-123").Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStoreResetWinsAndConflictIsNotSilentlyRetried(bool changePassword)
    {
        using var f = new Fixture();
        using var barrier = new Barrier();
        if (changePassword) f.Store.BeforePasswordChangeVerificationForTesting = barrier.Block;
        else f.Store.BeforeLoginPasswordVerificationForTesting = barrier.Block;
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        var attempt = Run(() =>
        {
            if (changePassword) f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "replacement-123");
            else f.Store.Login(Fixture.Username, Fixture.Password);
            return true;
        });
        try
        {
            barrier.Wait();
            var writer = new L12PlatformStore(f.Path);
            Assert.True(writer.ChangePassword(f.Owner.Id, Fixture.Password, "reset-winner-123").Success);
        }
        finally { barrier.Release(); }
        await Assert.ThrowsAsync<L12PlatformStorageConflictException>(async () =>
            await attempt.WaitAsync(TimeSpan.FromSeconds(10)));
        f.Store.BeforePasswordChangeVerificationForTesting = null;
        f.Store.BeforeLoginPasswordVerificationForTesting = null;
        Assert.Equal(0, notifications);
        Assert.True(f.Store.Login(Fixture.Username, "reset-winner-123").Success);
        Assert.False(new L12PlatformStore(f.Path).Login(Fixture.Username, "replacement-123").Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncLoginWaitingCancellationOrTimeoutDoesNotWriteAuthenticationState(bool cancel)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 8, TimeSpan.FromMilliseconds(250));
        for (var index = 0; index < 4; index++)
            Assert.Equal("authentication_failed", f.Store.Login(Fixture.Username, "wrong-password").Code);
        var before = AuthenticationState(f.Store);
        var held = new[] { f.Store.PasswordWorkBudget.Acquire(), f.Store.PasswordWorkBudget.Acquire() };
        using var cancellation = new CancellationTokenSource();
        Task<L12AuthenticationResult>? attempt = null;
        try
        {
            attempt = LoginAsync(f.Store, Fixture.Username, Fixture.Password, cancellation: cancellation.Token);
            Assert.False(attempt.IsCompleted, "Queued login must return an incomplete task instead of blocking the caller.");
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await attempt);
            }
            else await Assert.ThrowsAsync<L12AuthenticationBusyException>(async () => await attempt);
            Assert.Equal(before, AuthenticationState(f.Store));
        }
        finally
        {
            cancellation.Cancel();
            foreach (var lease in held) lease.Dispose();
            if (attempt is not null) await Drain(attempt);
        }
        // Four real failures before admission remain exactly four afterwards.
        Assert.Equal("login_rate_limited", f.Store.Login(Fixture.Username, "wrong-password").Code);
    }

    [Fact]
    public async Task CancellationAfterKdfAdmissionRetainsThePermitUntilRealWorkEndsAndCommitsTheOriginalLogin()
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 8, TimeSpan.FromMilliseconds(250));
        using var held = f.Store.PasswordWorkBudget.Acquire();
        using var barrier = new Barrier();
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeLoginPasswordVerificationForTesting = barrier.Block;
        var before = AuthenticationState(f.Store);
        Task<L12AuthenticationResult>? attempt = null;
        try
        {
            attempt = LoginAsync(f.Store, Fixture.Username, Fixture.Password, cancellation: cancellation.Token);
            Assert.False(attempt.IsCompleted);
            barrier.Wait();
            cancellation.Cancel();
            Assert.False(attempt.IsCompleted, "Request cancellation cannot abandon an admitted KDF and release its occupied worker.");
            Assert.Throws<L12AuthenticationBusyException>(() => f.Store.PasswordWorkBudget.Acquire());
            Assert.Equal(before, AuthenticationState(f.Store));
            Assert.Equal(f.Store.Version, await Run(() => f.Store.Version).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            barrier.Release();
            if (attempt is not null) await Drain(attempt);
        }
        var result = await attempt!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success);
        Assert.Equal(f.Owner.Id, f.Store.AuthenticateTokenSession(result.Token)!.Account.Id);
        Assert.Contains(f.Store.AdminAudit("authentication", 100), row => row.Reason == "authenticated" && row.ActorId == f.Owner.Id);
        using var reusable = f.Store.PasswordWorkBudget.Acquire();
    }

    [Fact]
    public async Task AsyncLoginStillRefusesPasswordWorkInsideAnOwningTransaction()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await f.Store.ExecuteAdminTransaction(() => LoginAsync(f.Store, Fixture.Username, Fixture.Password)));
        Assert.True(f.Store.Login(Fixture.Username, Fixture.Password).Success);
    }

    [Fact]
    public async Task QueuedLoginBurstOutlivesOldCutoffWithoutBlockingPlatformReads()
    {
        using var f = new Fixture();
        var usernames = Enumerable.Range(0, 8).Select(index => $"pwqueue{index}").ToArray();
        var accounts = usernames.Select(username =>
        {
            var registration = f.Store.Register(username, Fixture.Password);
            Assert.True(registration.Success, registration.Message);
            return registration.Account!;
        }).ToArray();
        f.Store.PasswordWorkBudget = new(2, 8, TimeSpan.FromSeconds(3));
        var held = new[] { f.Store.PasswordWorkBudget.Acquire(), f.Store.PasswordWorkBudget.Acquire() };
        var attempts = usernames.Select(username => LoginAsync(f.Store, username, Fixture.Password)).ToArray();
        try
        {
            await Task.Delay(350);
            Assert.All(attempts, attempt => Assert.False(attempt.IsCompleted));
            Assert.True(await Run(() => f.Store.AccountExists(f.Owner.Id)).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally { foreach (var lease in held) lease.Dispose(); await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10)); }
        var results = await Task.WhenAll(attempts);
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            Assert.True(result.Success, result.Message);
            Assert.Equal(accounts[index].Id, f.Store.AuthenticateTokenSession(result.Token)!.Account.Id);
        }
        Assert.Equal(8, results.Select(result => result.Token).Distinct().Count());
        using var first = f.Store.PasswordWorkBudget.Acquire();
        using var second = f.Store.PasswordWorkBudget.Acquire();
    }

    private static Task<L12AuthenticationResult> StartLogin(L12PlatformStore store, bool asyncLogin,
        string username, string password, L12LoginAttemptContext? context = null)
        => asyncLogin ? LoginAsync(store, username, password, context) : Run(() => store.Login(username, password, context));

    private static Task<L12AuthenticationResult> LoginAsync(L12PlatformStore store, string username, string password,
        L12LoginAttemptContext? context = null, CancellationToken cancellation = default)
    {
        var method = typeof(L12PlatformStore).GetMethod("LoginAsync", BindingFlags.Instance | BindingFlags.Public,
            null, [typeof(string), typeof(string), typeof(L12LoginAttemptContext), typeof(CancellationToken)], null);
        Assert.True(method is not null, "Missing shared-core LoginAsync: HTTP login still has no asynchronous admission path.");
        return method!.CreateDelegate<Func<string, string, L12LoginAttemptContext?, CancellationToken, Task<L12AuthenticationResult>>>(store)
            (username, password, context, cancellation);
    }

    private static string AuthenticationState(L12PlatformStore store)
        => JsonSerializer.Serialize(typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store));

    private static async Task Drain(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception) when (task.IsCompleted) { }
    }

    private static Task<T> Run<T>(Func<T> action) => Task.Factory.StartNew(action,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private sealed class Barrier : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        internal void Block()
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic password barrier timed out.");
        }
        internal void Wait() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(5)), "Password barrier was not reached.");
        internal void Release() => _release.Set();
        public void Dispose() { _entered.Dispose(); _release.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Username = "pwisolate";
        internal const string Password = "password-123";
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"l12-pw-{Guid.NewGuid():N}");
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal L12AccountView Owner { get; }
        internal object Row
        {
            get
            {
                var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Store)!;
                return ((IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!).Cast<object>()
                    .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == Owner.Id);
            }
        }
        internal Fixture()
        {
            Store = new(Path);
            Admin = Store.Login("Admin", "L12master").Account!;
            var registered = Store.Register(Username, Password);
            Assert.True(registered.Success, registered.Message);
            Owner = registered.Account!;
        }
        internal void Change(string change)
        {
            var context = new L12AdminAuditContext("synthetic-password-race", "admin.accounts.status.write");
            if (change == "reset")
            {
                Store.BeforePasswordChangeVerificationForTesting = null;
                Assert.True(Store.ChangePassword(Owner.Id, Password, "reset-winner-123").Success);
            }
            if (change == "rename") Assert.True(Store.SelfServiceChangeUsername(Owner.Id, Password, "pwrenamed").Success);
            if (change is "disable" or "disable-enable")
                Assert.True(Store.SetAccountDisabled(Admin, Owner.Id, true, "synthetic disable", context, true).Applied);
            if (change == "disable-enable")
                Assert.True(Store.SetAccountDisabled(Admin, Owner.Id, false, "synthetic enable", context, true).Applied);
        }
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
