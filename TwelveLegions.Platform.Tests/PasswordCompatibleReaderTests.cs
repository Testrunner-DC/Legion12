using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PasswordCompatibleReaderTests
{
    [Fact]
    public void Persisted600kLoginAndReloadDoNotRehashOrDowngradeCredential()
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        var original = f.Credential(f.Owner.Id);
        var version = f.Store.Account(f.Owner.Id)!.PermissionVersion;
        var login = f.Store.Login(Fixture.Username, Fixture.Password);
        Assert.True(login.Success, login.Message);
        Assert.NotNull(login.Token);
        Assert.Equal(original, f.Credential(f.Owner.Id));
        Assert.Equal(version, login.Account!.PermissionVersion);
        var reopened = new L12PlatformStore(f.Path);
        Assert.True(reopened.Login(Fixture.Username, Fixture.Password).Success);
        Assert.Equal(original, f.Credential(f.Owner.Id, reopened));
        Assert.Equal(600000, f.Iterations(f.Owner.Id, reopened));
    }

    [Fact]
    public void MatchingRootConfigurationDoesNotReplacePersisted600kCredential()
    {
        using var f = new Fixture();
        f.Seed600(f.Admin.Id, Fixture.Password);
        var original = f.Credential(f.Admin.Id);
        var previous = Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", Fixture.Password);
            var reopened = new L12PlatformStore(f.Path);
            Assert.Equal(original, f.Credential(f.Admin.Id, reopened));
            Assert.True(reopened.Login("Admin", Fixture.Password).Success);
            Assert.Equal(original, f.Credential(f.Admin.Id, reopened));
        }
        finally { Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", previous); }
    }

    [Fact]
    public void DifferentRootConfigurationStillUsesOriginal120kResetPolicy()
    {
        using var f = new Fixture();
        f.Seed600(f.Admin.Id, Fixture.Password);
        var previous = Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", "replacement-123");
            var reopened = new L12PlatformStore(f.Path);
            Assert.Equal(120000, f.Iterations(f.Admin.Id, reopened));
            Assert.True(reopened.Login("Admin", "replacement-123").Success);
            Assert.False(reopened.Login("Admin", Fixture.Password).Success);
        }
        finally { Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", previous); }
    }

    [Theory]
    [InlineData("password")]
    [InlineData("forced-name")]
    [InlineData("self-name")]
    public void SharedConsumersRead600kAndKeepExistingWriteAndSessionPolicy(string operation)
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        var original = f.Credential(f.Owner.Id);
        if (operation == "password")
        {
            Assert.True(f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "replacement-123", f.Session.SessionId).Success);
            Assert.Equal(120000, f.Iterations(f.Owner.Id));
            Assert.True(f.Store.Login(Fixture.Username, "replacement-123").Success);
        }
        else
        {
            if (operation == "forced-name")
            {
                f.Mutate(f.Owner.Id, "MustChangeUsername", true);
                Assert.True(f.Store.ChangeUsername(f.Owner.Id, Fixture.Password, "pcreadernew", f.Session.SessionId).Success);
            }
            else Assert.True(f.Store.SelfServiceChangeUsername(f.Owner.Id, Fixture.Password, "pcreadernew", f.Session.SessionId).Success);
            Assert.Equal(original, f.Credential(f.Owner.Id));
            Assert.True(f.Store.Login("pcreadernew", Fixture.Password).Success);
        }
        Assert.True(f.Store.IsSessionActive(f.Session.SessionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdminCredentialReplacementsStillWrite120k(bool delete)
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        if (delete) Assert.True(f.Store.DeleteAccountPersonalData(f.Admin, f.Owner.Id, "synthetic", new("reader-delete"), true).Applied);
        else
        {
            var reset = f.Store.AdminResetPassword(f.Admin, f.Owner.Id, "synthetic", new("reader-reset"), true);
            Assert.True(reset.Applied);
            Assert.True(f.Store.Login(Fixture.Username, reset.TemporaryPassword!).Success);
        }
        Assert.Equal(120000, f.Iterations(f.Owner.Id));
        Assert.False(f.Store.IsSessionActive(f.Session.SessionId));
        var registered = f.Store.Register("pcother", Fixture.Password);
        Assert.True(registered.Success, registered.Message);
        Assert.Equal(120000, f.Iterations(registered.Account!.Id));
    }

    [Theory]
    [InlineData("PasswordAlgorithm", "unknown")]
    [InlineData("PasswordAlgorithm", null)]
    [InlineData("PasswordIterations", null)]
    [InlineData("PasswordCredentialVersion", null)]
    [InlineData("PasswordCredentialVersion", 2)]
    [InlineData("PasswordIterations", 119999)]
    [InlineData("PasswordIterations", 120001)]
    [InlineData("PasswordIterations", 599999)]
    [InlineData("PasswordIterations", 600001)]
    [InlineData("PasswordIterations", -1)]
    [InlineData("PasswordHash", "bad")]
    [InlineData("Salt", "bad")]
    public void KnownHigherCostDoesNotBroadenMetadataOrEncodingWhitelist(string field, object? value)
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        f.Mutate(f.Owner.Id, field, value);
        var before = f.Credential(f.Owner.Id);
        var sessions = f.Store.Sessions(f.Owner.Id).Count;
        var login = f.Store.Login(Fixture.Username, Fixture.Password);
        Assert.False(login.Success);
        Assert.Null(login.Token);
        Assert.Equal(before, f.Credential(f.Owner.Id));
        Assert.Equal(sessions, f.Store.Sessions(f.Owner.Id).Count);
    }

    [Fact]
    public void Wrong600kPasswordStillUsesOriginalFifthFailureLockout()
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        var before = f.Credential(f.Owner.Id);
        for (var count = 1; count <= 5; count++)
        {
            var failure = f.Store.Login(Fixture.Username, "wrong-password");
            Assert.False(failure.Success);
            Assert.Equal(count < 5 ? "authentication_failed" : "login_rate_limited", failure.Code);
        }
        var blocked = f.Store.Login(Fixture.Username, Fixture.Password);
        Assert.Equal("login_rate_limited", blocked.Code);
        Assert.Null(blocked.Token);
        Assert.Equal(before, f.Credential(f.Owner.Id));
    }

    [Fact]
    public void Busy600kVerificationDoesNotWriteAnyPlatformState()
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        var before = f.State();
        using (f.Store.PasswordWorkBudget.Acquire())
            Assert.Throws<L12AuthenticationBusyException>(() => f.Store.Login(Fixture.Username, Fixture.Password));
        Assert.Equal(before, f.State());
        Assert.True(f.Store.Login(Fixture.Username, Fixture.Password).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HigherCostVerificationStaysOutsideGateAndRechecksActualReset(bool reset)
    {
        using var f = new Fixture();
        f.Seed600(f.Owner.Id, Fixture.Password);
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Store.BeforeLoginPasswordVerificationForTesting = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic reader barrier.");
        };
        var login = Run(() => f.Store.Login(Fixture.Username, Fixture.Password));
        Task<long>? read = null;
        string? winningCredential = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            read = Run(() => f.Store.Version);
            await read.WaitAsync(TimeSpan.FromSeconds(1));
            if (reset)
            {
                Assert.True(f.Store.AdminResetPassword(f.Admin, f.Owner.Id, "synthetic reset", new("reader-race"), true).Applied);
                winningCredential = f.Credential(f.Owner.Id);
            }
        }
        finally
        {
            release.Set();
            await Drain(login);
            if (read is not null) await Drain(read);
        }
        Assert.Equal(!reset, (await login).Success);
        if (reset) Assert.Equal(winningCredential, f.Credential(f.Owner.Id));
        f.Store.BeforeLoginPasswordVerificationForTesting = null;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task RealHttpLoginReadsPersisted600kWithoutExposingOrReplacingIt(int burst)
    {
        using var f = new Fixture();
        var usernames = Enumerable.Range(0, burst).Select(index => index == 0 ? Fixture.Username : $"pcread{index}").ToArray();
        var accounts = usernames.Select((username, index) =>
        {
            if (index == 0) return f.Owner;
            var registration = f.Store.Register(username, Fixture.Password);
            Assert.True(registration.Success, registration.Message);
            return registration.Account!;
        }).ToArray();
        foreach (var account in accounts) f.Seed600(account.Id, Fixture.Password);
        f.Store.PasswordWorkBudget = new(2, 8, TimeSpan.FromSeconds(3));
        var original = accounts.Select(account => f.Credential(account.Id)).ToArray();
        var previous = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(System.IO.Path.Combine(f.Directory, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, f.Store), recorder, f.Store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var held = new[] { f.Store.PasswordWorkBudget.Acquire(), f.Store.PasswordWorkBudget.Acquire() };
            var responses = usernames.Select(username => client.PostAsJsonAsync("/api/auth/login", new { username, password = Fixture.Password })).ToArray();
            try
            {
                var cutoff = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                var admitted = typeof(L12PasswordWorkBudget).GetField("_admitted", BindingFlags.Instance | BindingFlags.NonPublic)!;
                while ((int)admitted.GetValue(f.Store.PasswordWorkBudget)! != burst + 2 && DateTime.UtcNow < cutoff)
                    await Task.Delay(10);
                Assert.Equal(burst + 2, (int)admitted.GetValue(f.Store.PasswordWorkBudget)!);
                await Task.Delay(350);
                Assert.All(responses, response => Assert.False(response.IsCompleted));
                using var health = await client.GetAsync("/health").WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            }
            finally { foreach (var lease in held) lease.Dispose(); }
            var completed = await Task.WhenAll(responses).WaitAsync(TimeSpan.FromSeconds(10));
            var tokens = new List<string>();
            try
            {
                for (var index = 0; index < completed.Length; index++)
                {
                    var response = completed[index];
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var body = await response.Content.ReadAsStringAsync();
                    using var json = JsonDocument.Parse(body);
                    var token = json.RootElement.GetProperty("token").GetString();
                    Assert.False(string.IsNullOrWhiteSpace(token));
                    Assert.Equal(accounts[index].Id, json.RootElement.GetProperty("account").GetProperty("id").GetString());
                    Assert.Equal(accounts[index].Id, f.Store.AuthenticateTokenSession(token)!.Account.Id);
                    Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("passwordIterations", body, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("salt", body, StringComparison.OrdinalIgnoreCase);
                    tokens.Add(token!);
                }
                Assert.Equal(burst, tokens.Distinct().Count());
            }
            finally { foreach (var response in completed) response.Dispose(); }
            Assert.Equal(original, accounts.Select(account => f.Credential(account.Id)).ToArray());
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previous);
        }
    }

    private static Task<T> Run<T>(Func<T> action) => Task.Factory.StartNew(action,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private static async Task Drain(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception) when (task.IsCompleted) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Username = "pcreader";
        internal const string Password = "password-123";
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-pcr", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Directory, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal L12AccountView Owner => Session.Account;
        internal L12AuthenticatedSession Session { get; }
        internal Fixture()
        {
            Store = new(Path);
            Admin = Store.Login("Admin", "L12master").Account!;
            var owner = Store.Register(Username, Password);
            Assert.True(owner.Success, owner.Message);
            Session = Store.AuthenticateTokenSession(owner.Token)!;
        }
        private static object Data(L12PlatformStore store) => typeof(L12PlatformStore)
            .GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        private object Row(string id, L12PlatformStore? store = null)
        {
            var data = Data(store ?? Store);
            return ((IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!).Cast<object>()
                .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
        }
        internal string State() => JsonSerializer.Serialize(Data(Store));
        internal string Credential(string id, L12PlatformStore? store = null)
        {
            var row = Row(id, store);
            return JsonSerializer.Serialize(new[] { "PasswordHash", "Salt", "PasswordAlgorithm", "PasswordIterations", "PasswordCredentialVersion" }
                .Select(name => row.GetType().GetProperty(name)!.GetValue(row)).ToArray());
        }
        internal int Iterations(string id, L12PlatformStore? store = null)
            => (int)Row(id, store).GetType().GetProperty("PasswordIterations")!.GetValue(Row(id, store))!;
        internal void Seed600(string id, string password)
        {
            var credential = L12PasswordCredential.Create(password, L12PasswordCredential.RecognizedUpgradeIterations);
            Store.ExecuteAdminTransaction(() =>
            {
                var row = Row(id);
                foreach (var (name, value) in new (string, object)[]
                {
                    ("PasswordHash", credential.PasswordHash), ("Salt", credential.Salt),
                    ("PasswordAlgorithm", credential.Algorithm), ("PasswordIterations", credential.Iterations),
                    ("PasswordCredentialVersion", credential.Version),
                }) row.GetType().GetProperty(name)!.SetValue(row, value);
                Save(); return true;
            });
        }
        internal void Mutate(string id, string field, object? value) => Store.ExecuteAdminTransaction(() =>
        {
            var row = Row(id); row.GetType().GetProperty(field)!.SetValue(row, value);
            Save(); return true;
        });
        private void Save() => typeof(L12PlatformStore).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Store, [false]);
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
