using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class EmailAuthAndAccountLifecycleTests
{
    [Fact]
    public void EmailVerificationIsHashedOneTimeAndPersists()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var sender = new FakeEmailSender();
            var store = new L12PlatformStore(path, emailSender: sender, emailFeatureEnabled: true);
            var registered = store.Register("EmailOwner", "password-123");

            var request = store.RequestEmailBinding(registered.Account!.Id, "password-123",
                "Owner@Example.com", "127.0.0.1");
            Assert.True(request.Success);
            Assert.False(store.EmailStatus(registered.Account.Id).Verified);
            var token = sender.LastToken();
            Assert.DoesNotContain(token, File.ReadAllText(path), StringComparison.Ordinal);

            var verified = store.VerifyEmail(token, "127.0.0.1");
            Assert.True(verified.Success);
            Assert.Equal("ow***@example.com", store.EmailStatus(registered.Account.Id).MaskedEmail);
            Assert.False(store.VerifyEmail(token, "127.0.0.1").Success);

            var reloaded = new L12PlatformStore(path, emailSender: sender, emailFeatureEnabled: true);
            Assert.True(reloaded.EmailStatus(registered.Account.Id).Verified);
            Assert.True(reloaded.Login("EmailOwner", "password-123").Success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ForgotPasswordOnlySendsForVerifiedEmailAndResetRevokesEverySession()
    {
        var root = TempRoot();
        try
        {
            var sender = new FakeEmailSender();
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path, emailSender: sender, emailFeatureEnabled: true);
            var account = store.Register("ResetOwner", "password-123").Account!;
            BindAndVerify(store, sender, account.Id, "password-123", "reset@example.com");
            sender.Messages.Clear();
            var first = store.Login("ResetOwner", "password-123");
            var second = store.Login("ResetOwner", "password-123");

            var unknown = store.RequestPasswordReset("missing@example.com", "client-a");
            var accepted = store.RequestPasswordReset("reset@example.com", "client-b");
            Assert.Equal(unknown.Message, accepted.Message);
            Assert.True(SpinWait.SpinUntil(() => sender.Messages.Count == 1, TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => PasswordResetDelivered(path), TimeSpan.FromSeconds(2)));
            Assert.Single(sender.Messages);
            var token = sender.LastToken();
            Assert.True(store.UnbindEmail(account.Id, "password-123").Success);
            BindAndVerify(store, sender, account.Id, "password-123", "reset@example.com");
            Assert.False(store.ResetPassword(token, "stale-password-456", "client-b").Success);
            sender.Messages.Clear();
            Assert.True(store.RequestPasswordReset("reset@example.com", "client-c").Success);
            Assert.True(SpinWait.SpinUntil(() => sender.Messages.Count == 1, TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => PasswordResetDelivered(path), TimeSpan.FromSeconds(2)));
            token = sender.LastToken();
            Assert.True(store.ResetPassword(token, "new-password-456", "client-c").Success);
            Assert.False(store.ResetPassword(token, "another-password-789", "client-b").Success);
            Assert.Null(store.AuthenticateToken(first.Token));
            Assert.Null(store.AuthenticateToken(second.Token));
            Assert.False(store.Login("ResetOwner", "password-123").Success);
            Assert.True(store.Login("ResetOwner", "new-password-456").Success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UnconfiguredMailFailsBindingButForgotResponseRemainsGeneric()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"),
                emailSender: new L12UnavailableEmailSender(), emailFeatureEnabled: true);
            var account = store.Register("NoMailOwner", "password-123").Account!;
            var bind = store.RequestEmailBinding(account.Id, "password-123", "owner@example.com", "client");
            Assert.False(bind.Success);
            Assert.Equal("mail_unavailable", bind.Code);
            var forgot = store.RequestPasswordReset("owner@example.com", "client");
            Assert.True(forgot.Success);
            Assert.Equal("password_reset_accepted", forgot.Code);
            Assert.True(store.RequestPasswordReset("missing@example.com", "client-a").Success);
            Assert.True(store.RequestPasswordReset("missing@example.com", "client-b").Success);
            Assert.True(store.RequestPasswordReset("missing@example.com", "client-c").Success);
            var limited = store.RequestPasswordReset("missing@example.com", "client-d");
            Assert.False(limited.Success);
            Assert.True(limited.RetryAfterSeconds > 0);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DisabledEmailFeatureFailsClosedWithoutDeletingVerifiedEmail()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var sender = new FakeEmailSender();
            var enabled = new L12PlatformStore(path, emailSender: sender, emailFeatureEnabled: true);
            var account = enabled.Register("PreservedEmailOwner", "password-123").Account!;
            BindAndVerify(enabled, sender, account.Id, "password-123", "preserved@example.com");
            var previousMessageCount = sender.Messages.Count;

            var disabled = new L12PlatformStore(path, emailSender: sender, emailFeatureEnabled: false);
            var capability = disabled.EmailCapability();
            Assert.False(capability.Enabled);
            Assert.False(capability.MailConfigured);
            var status = disabled.EmailStatus(account.Id);
            Assert.True(status.Bound);
            Assert.True(status.Verified);
            Assert.False(status.FeatureEnabled);
            Assert.Equal("pr***@example.com", status.MaskedEmail);

            Assert.Equal("email_feature_disabled", disabled.RequestEmailBinding(account.Id, "password-123",
                "other@example.com", "client").Code);
            Assert.Equal("email_feature_disabled", disabled.VerifyEmail("unused-token", "client").Code);
            Assert.Equal("email_feature_disabled", disabled.RequestPasswordReset("preserved@example.com", "client").Code);
            Assert.Equal("email_feature_disabled", disabled.ResetPassword("unused-token", "replacement-123", "client").Code);
            Assert.Equal(previousMessageCount, sender.Messages.Count);
            Assert.True(disabled.Login("PreservedEmailOwner", "password-123").Success);

            var reenabled = new L12PlatformStore(path, emailSender: sender, emailFeatureEnabled: true);
            Assert.True(reenabled.EmailStatus(account.Id).Verified);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EmailFeatureEnvironmentSwitchIsExplicitOptIn()
    {
        var previous = Environment.GetEnvironmentVariable(L12EmailFeature.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(L12EmailFeature.EnvironmentVariable, null);
            Assert.False(L12EmailFeature.EnabledFromEnvironment());
            Environment.SetEnvironmentVariable(L12EmailFeature.EnvironmentVariable, "false");
            Assert.False(L12EmailFeature.EnabledFromEnvironment());
            Environment.SetEnvironmentVariable(L12EmailFeature.EnvironmentVariable, "true");
            Assert.True(L12EmailFeature.EnabledFromEnvironment());
        }
        finally { Environment.SetEnvironmentVariable(L12EmailFeature.EnvironmentVariable, previous); }
    }

    [Fact]
    public async Task HttpEmailRoutesFailClosedWhenFeatureIsDisabled()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var sender = new FakeEmailSender();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, emailSender: sender, emailFeatureEnabled: false);
            var registered = store.Register("HttpDisabledEmail", "password-123");
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            using (var capabilityResponse = await client.GetAsync("/api/auth/email/capability"))
            {
                Assert.Equal(HttpStatusCode.OK, capabilityResponse.StatusCode);
                var capability = await capabilityResponse.Content.ReadFromJsonAsync<L12EmailCapabilityView>();
                Assert.NotNull(capability);
                Assert.False(capability.Enabled);
                Assert.False(capability.MailConfigured);
            }

            using (var bind = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email/bind"))
            {
                bind.Headers.Authorization = new("Bearer", registered.Token);
                bind.Content = JsonContent.Create(new
                    { email = "disabled@example.com", currentPassword = "password-123" });
                using var response = await client.SendAsync(bind);
                await AssertEmailFeatureDisabled(response);
            }
            using (var response = await client.PostAsJsonAsync("/api/auth/email/verify",
                       new { token = "unused-token" }))
                await AssertEmailFeatureDisabled(response);
            using (var response = await client.PostAsJsonAsync("/api/auth/password/forgot",
                       new { email = "disabled@example.com" }))
                await AssertEmailFeatureDisabled(response);
            using (var response = await client.PostAsJsonAsync("/api/auth/password/reset",
                       new { token = "unused-token", newPassword = "replacement-password" }))
                await AssertEmailFeatureDisabled(response);

            Assert.Empty(sender.Messages);
            Assert.True(store.Login("HttpDisabledEmail", "password-123").Success);
            Assert.False(store.EmailStatus(registered.Account!.Id).Bound);
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AdminResetAndLogicalDeletionProtectRootAndSelfAndScrubPersonalData()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("PrivacyOwner", "password-123");
            store.AddBug(player.Account, "personal", "details", "/me", null, null, "test");
            var context = new L12AdminAuditContext("test", "admin.accounts.status.write");

            Assert.Throws<L12SecurityPolicyException>(() =>
                store.AdminResetPassword(admin, admin.Id, "self", context, true));
            var reset = store.AdminResetPassword(admin, player.Account!.Id, "support-reset", context, true);
            Assert.True(reset.Applied);
            Assert.True(reset.Account.MustChangePassword);
            Assert.Null(store.AuthenticateToken(player.Token));
            Assert.True(store.Login("PrivacyOwner", "123456").Success);

            var deleted = store.DeleteAccountPersonalData(admin, player.Account.Id, "user-request", context, true);
            Assert.True(deleted.Applied);
            Assert.True(deleted.Account.Deleted);
            Assert.False(store.Login("PrivacyOwner", "123456").Success);
            Assert.DoesNotContain(store.Bugs(null), bug => bug.ReporterName == "PrivacyOwner");
            Assert.Equal($"deleted-{player.Account.Id}", store.Account(player.Account.Id)!.Username);
            Assert.Throws<L12SecurityPolicyException>(() =>
                store.DeleteAccountPersonalData(admin, admin.Id, "root", context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpRecoveryContractUsesAnonymousGenericResponsesAndOneTimeToken()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var sender = new FakeEmailSender();
            var platformPath = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(platformPath, catalog.PresetDecks,
                officialCards: catalog.Cards, emailSender: sender, emailFeatureEnabled: true);
            var registered = store.Register("HttpEmailOwner", "password-123");
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            using (var bind = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email/bind"))
            {
                bind.Headers.Authorization = new("Bearer", registered.Token);
                bind.Content = JsonContent.Create(new { email = "http@example.com", currentPassword = "password-123" });
                using var response = await client.SendAsync(bind);
                Assert.True(response.StatusCode == HttpStatusCode.Accepted,
                    $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
            var verifyToken = sender.LastToken();
            using (var response = await client.PostAsJsonAsync("/api/auth/email/verify", new { token = verifyToken }))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            sender.Messages.Clear();

            using var unknown = await client.PostAsJsonAsync("/api/auth/password/forgot",
                new { email = "missing@example.com" });
            using var known = await client.PostAsJsonAsync("/api/auth/password/forgot",
                new { email = "http@example.com" });
            Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
            var unknownJson = await unknown.Content.ReadFromJsonAsync<JsonElement>();
            var knownJson = await known.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(unknownJson.GetProperty("message").GetString(), knownJson.GetProperty("message").GetString());
            Assert.True(SpinWait.SpinUntil(() => sender.Messages.Count == 1, TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => PasswordResetDelivered(platformPath), TimeSpan.FromSeconds(2)));
            var resetToken = sender.LastToken();
            using var reset = await client.PostAsJsonAsync("/api/auth/password/reset",
                new { token = resetToken, newPassword = "http-new-password" });
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            using var replay = await client.PostAsJsonAsync("/api/auth/password/reset",
                new { token = resetToken, newPassword = "http-newer-password" });
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

            var admin = store.Login("Admin", "L12master");
            using (var adminReset = new HttpRequestMessage(HttpMethod.Post,
                       $"/api/admin/accounts/{registered.Account!.Id}/reset-password"))
            {
                adminReset.Headers.Authorization = new("Bearer", admin.Token);
                adminReset.Content = JsonContent.Create(new
                {
                    reason = "support-reset",
                    idempotencyKey = "http-admin-reset",
                    expectedVersion = store.Account(registered.Account!.Id)!.PermissionVersion,
                });
                using var response = await client.SendAsync(adminReset);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using var temporaryLogin = await client.PostAsJsonAsync("/api/auth/login",
                new { username = "HttpEmailOwner", password = "123456" });
            var loginJson = await temporaryLogin.Content.ReadFromJsonAsync<JsonElement>();
            var temporaryToken = loginJson.GetProperty("token").GetString()!;
            Assert.True(loginJson.GetProperty("account").GetProperty("mustChangePassword").GetBoolean());
            using (var blocked = new HttpRequestMessage(HttpMethod.Get, "/api/matches"))
            {
                blocked.Headers.Authorization = new("Bearer", temporaryToken);
                using var response = await client.SendAsync(blocked);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("password_change_required",
                    (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }
            using (var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password"))
            {
                change.Headers.Authorization = new("Bearer", temporaryToken);
                change.Content = JsonContent.Create(new { currentPassword = "123456", newPassword = "final-password-123" });
                using var response = await client.SendAsync(change);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using (var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/matches"))
            {
                allowed.Headers.Authorization = new("Bearer", temporaryToken);
                using var response = await client.SendAsync(allowed);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MatchRecorderAnonymizesNamesDeckLabelsAndRecordedJson()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "matches.db");
            await using var recorder = new MatchRecorder(path);
            await recorder.InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc)
                    VALUES('m1','ROOM',1,'PrivacyOwner','Other','Private Deck','Other Deck','2026-01-01T00:00:00Z');
                    INSERT INTO match_events(match_id,sequence,received_utc,player_index,command_json,accepted,error,revision,state_hash,state_json)
                    VALUES('m1',1,'2026-01-01T00:00:01Z',0,'{"note":"PrivacyOwner acted"}',1,NULL,1,'hash',
                    '{"Players":[{"Name":"PrivacyOwner"},{"Name":"Other"}]}');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            Assert.Equal(1, await recorder.AnonymizePlayerAsync("PrivacyOwner", "deleted-account"));
            var summary = Assert.Single(await recorder.ListMatchesAsync());
            Assert.Equal("deleted-account", summary.Player0);
            Assert.Equal("已清理牌库", summary.Deck0);
            var detail = await recorder.GetMatchAsync("m1");
            var recorded = Assert.Single(detail!.Commands);
            Assert.DoesNotContain("PrivacyOwner", recorded.Command.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("PrivacyOwner", recorded.State.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static void BindAndVerify(L12PlatformStore store, FakeEmailSender sender, string accountId,
        string password, string email)
    {
        Assert.True(store.RequestEmailBinding(accountId, password, email, "client").Success);
        Assert.True(store.VerifyEmail(sender.LastToken(), "client").Success);
    }

    private static async Task AssertEmailFeatureDisabled(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
        Assert.NotNull(error);
        Assert.Equal("email_feature_disabled", error.Code);
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-email-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool PasswordResetDelivered(string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("EmailAuthTokens").EnumerateArray().Any(row =>
                row.GetProperty("Purpose").GetString() == "password-reset"
                && row.GetProperty("DeliveredAt").ValueKind == JsonValueKind.String
                && row.GetProperty("ConsumedAt").ValueKind == JsonValueKind.Null);
        }
        catch { return false; }
    }

    private sealed class FakeEmailSender : IL12EmailSender
    {
        public bool IsConfigured => true;
        public string PublicBaseUrl => "https://legion.test";
        public ConcurrentQueue<L12EmailMessage> Messages { get; } = [];
        public L12EmailSendResult Send(L12EmailMessage message)
        {
            Messages.Enqueue(message);
            return new(true, "sent", "sent");
        }

        public string LastToken()
        {
            var link = Messages.Last().TextBody.Split('\n').Single(line => line.StartsWith(PublicBaseUrl,
                StringComparison.Ordinal));
            return Uri.UnescapeDataString(new Uri(link).Fragment["#token=".Length..]);
        }
    }
}
