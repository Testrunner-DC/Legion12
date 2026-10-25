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
public sealed class EmailFeatureDisabledTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitConstructorOptInCannotReenableEmail(bool? requested)
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-email-off-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"),
                emailSender: new ForbiddenSender(), emailFeatureEnabled: requested);
            Assert.Equal(new L12EmailCapabilityView(false, false), store.EmailCapability());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void OldEnvironmentCannotEnableFeatureOrMailTransport(string? setting)
    {
        var old = Environment.GetEnvironmentVariable(L12EmailFeature.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(L12EmailFeature.EnvironmentVariable, setting);
            Assert.False(L12EmailFeature.EnabledFromEnvironment());
            var sender = L12SmtpEmailSender.FromEnvironment();
            Assert.False(sender.IsConfigured);
            Assert.Empty(sender.PublicBaseUrl);
            Assert.Equal("email_feature_disabled",
                sender.Send(new("synthetic@example.invalid", "unused", "unused", "unused")).Code);
        }
        finally { Environment.SetEnvironmentVariable(L12EmailFeature.EnvironmentVariable, old); }
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("unbind")]
    [InlineData("verify")]
    [InlineData("forgot")]
    [InlineData("reset")]
    public void EveryLegacyOperationPreservesStoredEmailTokensCredentialsSessionsAndThrottles(string operation)
    {
        using var f = new Fixture();
        f.SeedHistoricalEmailAndTokens();
        var before = f.Snapshot();
        var mirror = File.ReadAllBytes(f.Path);
        var version = f.Store.Version;
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        f.Store.StorageFailureInjector = _ => throw new IOException("Disabled email must never persist.");
        var result = operation switch
        {
            "bind" => f.Store.RequestEmailBinding(f.Owner.Id, Fixture.Password, "other@example.com", "client"),
            "unbind" => f.Store.UnbindEmail(f.Owner.Id, Fixture.Password),
            "verify" => f.Store.VerifyEmail(Fixture.VerifyToken, "client"),
            "forgot" => f.Store.RequestPasswordReset("legacy@example.com", "client"),
            _ => f.Store.ResetPassword(Fixture.ResetToken, "replacement-123", "client"),
        };
        Assert.Equal(new L12AuthOperationResult(false, "email_feature_disabled", "邮箱功能已停用"), result);
        Assert.Equal(before, f.Snapshot());
        Assert.Equal(mirror, File.ReadAllBytes(f.Path));
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(0, notifications);
        f.Store.StorageFailureInjector = null;
        var reloaded = new L12PlatformStore(f.Path, emailSender: new ForbiddenSender(), emailFeatureEnabled: true);
        Assert.Equal("le***@example.com", reloaded.Account(f.Owner.Id)!.EmailMasked);
        Assert.True(reloaded.Account(f.Owner.Id)!.EmailVerified);
        Assert.Equal(new L12EmailStatusView(false, false, null, null, null, false, false),
            reloaded.EmailStatus(f.Owner.Id));
        Assert.Equal("email_feature_disabled", reloaded.VerifyEmail(Fixture.VerifyToken, "client").Code);
        Assert.Equal("email_feature_disabled", reloaded.ResetPassword(Fixture.ResetToken, "replacement-123", "client").Code);
        Assert.NotNull(reloaded.AuthenticateToken(f.Token));
        Assert.True(reloaded.Login(Fixture.Username, Fixture.Password).Success);
    }

    [Fact]
    public void DisabledOperationsDoNotInspectMalformedInputOrAcquireThePlatformGate()
    {
        using var f = new Fixture();
        var gate = typeof(L12PlatformStore).GetField("_gate", Private)!.GetValue(f.Store)!;
        Task? work;
        bool completed;
        lock (gate)
        {
            work = Task.Factory.StartNew(() =>
            {
                Assert.Equal("email_feature_disabled", f.Store.RequestEmailBinding(null!, null!, null!, null!).Code);
                Assert.Equal("email_feature_disabled", f.Store.UnbindEmail(null!, null!).Code);
                Assert.Equal("email_feature_disabled", f.Store.VerifyEmail(null!, null!).Code);
                Assert.Equal("email_feature_disabled", f.Store.RequestPasswordReset(null!, null!).Code);
                Assert.Equal("email_feature_disabled", f.Store.ResetPassword(null!, null!, null!).Code);
                Assert.False(f.Store.EmailStatus(null!).FeatureEnabled);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            completed = work.Wait(TimeSpan.FromSeconds(1));
        }
        work.GetAwaiter().GetResult();
        Assert.True(completed, "Retired email must not wait for the platform gate.");
    }

    [Fact]
    public void NormalPasswordChangeAndAdminResetAndDeletionRemainAvailable()
    {
        using var f = new Fixture();
        var admin = f.Store.Login("Admin", "L12master").Account!;
        Assert.True(f.Store.ChangePassword(f.Owner.Id, Fixture.Password, "changed-123").Success);
        Assert.True(f.Store.Login(Fixture.Username, "changed-123").Success);
        var context = new L12AdminAuditContext("email-retirement-regression", "admin.accounts.status.write");
        var reset = f.Store.AdminResetPassword(admin, f.Owner.Id, "synthetic support reset", context, true);
        Assert.True(reset.Applied);
        Assert.True(reset.Account.MustChangePassword);
        Assert.NotNull(reset.TemporaryPassword);
        Assert.True(f.Store.Login(Fixture.Username, reset.TemporaryPassword!).Success);
        Assert.True(f.Store.ChangePassword(f.Owner.Id, reset.TemporaryPassword!, "final-password-123").Success);
        Assert.True(f.Store.Login(Fixture.Username, "final-password-123").Success);
        Assert.True(f.Store.DeleteAccountPersonalData(admin, f.Owner.Id, "synthetic deletion", context, true).Applied);
        Assert.False(f.Store.Login(Fixture.Username, "final-password-123").Success);
    }

    [Fact]
    public async Task LegacyHttpEndpointsReturnDisabledEvenWithExplicitOptIn()
    {
        using var f = new Fixture();
        var old = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(f.Root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, f.Store),
                recorder, f.Store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            Assert.Equal(new L12EmailCapabilityView(false, false),
                await client.GetFromJsonAsync<L12EmailCapabilityView>("/api/auth/email/capability"));
            client.DefaultRequestHeaders.Authorization = new("Bearer", f.Token);
            foreach (var endpoint in new[] { "email/bind", "email/unbind", "email/verify", "password/forgot", "password/reset" })
            {
                using var response = await client.PostAsJsonAsync("/api/auth/" + endpoint,
                    new { email = "legacy@example.com", currentPassword = Fixture.Password,
                        token = Fixture.ResetToken, newPassword = "replacement-123" });
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("email_feature_disabled", (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }
            Assert.False((await client.GetFromJsonAsync<L12EmailStatusView>("/api/auth/email"))!.FeatureEnabled);
            Assert.NotNull(f.Store.AuthenticateToken(f.Token));
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", old);
        }
    }

    private sealed class ForbiddenSender : IL12EmailSender
    {
        public bool IsConfigured => throw new InvalidOperationException("Retired sender must not be consulted.");
        public string PublicBaseUrl => throw new InvalidOperationException("Retired sender must not be consulted.");
        public L12EmailSendResult Send(L12EmailMessage message) => throw new InvalidOperationException("No mail may be sent.");
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Username = "emailowner";
        internal const string Password = "password-123";
        internal const string VerifyToken = "synthetic-old-verify-token";
        internal const string ResetToken = "synthetic-old-reset-token";
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"l12-email-off-{Guid.NewGuid():N}");
        internal string Path => System.IO.Path.Combine(Root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Owner { get; }
        internal string Token { get; }
        private object Data => typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(Store)!;
        internal Fixture()
        {
            Store = new(Path, emailSender: new ForbiddenSender(), emailFeatureEnabled: true);
            var registered = Store.Register(Username, Password);
            Assert.True(registered.Success, registered.Message);
            Owner = registered.Account!;
            Token = registered.Token!;
        }
        internal string Snapshot() => JsonSerializer.Serialize(Data, Data.GetType());
        internal void SeedHistoricalEmailAndTokens()
        {
            var account = ((IEnumerable)Data.GetType().GetProperty("Accounts")!.GetValue(Data)!).Cast<object>()
                .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == Owner.Id);
            Set(account, "Email", "legacy@example.com");
            Set(account, "NormalizedEmail", "legacy@example.com");
            Set(account, "EmailVerifiedAt", DateTimeOffset.UtcNow.AddDays(-1));
            var tokens = (IList)Data.GetType().GetProperty("EmailAuthTokens")!.GetValue(Data)!;
            foreach (var pair in new[] { ("email-verify", VerifyToken), ("password-reset", ResetToken) })
            {
                var row = Activator.CreateInstance(tokens.GetType().GetGenericArguments()[0], true)!;
                Set(row, "AccountId", Owner.Id); Set(row, "Purpose", pair.Item1);
                Set(row, "TargetEmail", "legacy@example.com");
                var hash = typeof(L12PlatformStore).GetMethod("HashToken", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [pair.Item2])!;
                Set(row, "TokenHash", hash);
                Set(row, "CreatedAt", DateTimeOffset.UtcNow.AddMinutes(-1));
                Set(row, "DeliveredAt", DateTimeOffset.UtcNow.AddSeconds(-30));
                Set(row, "ExpiresAt", DateTimeOffset.UtcNow.AddMinutes(20));
                tokens.Add(row);
            }
            typeof(L12PlatformStore).GetMethod("Save", Private)!.Invoke(Store, [true]);
        }
        private static void Set(object row, string name, object value)
            => row.GetType().GetProperty(name)!.SetValue(row, value);
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
