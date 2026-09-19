using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class UsernameModerationTests
{
    [Fact]
    public void RegistrationUsesElevenVisibleCharactersAndNormalizedProhibitedTerms()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.True(store.Register("对方测试长昵称十二军团", "password-123").Success);
            Assert.False(store.Register("对方测试长昵称十二军团甲", "password-123").Success);
            Assert.False(store.Register("测试官・方账号", "password-123").Success);
            Assert.False(store.Register("ＡＤＭＩＮ玩家", "password-123").Success);
            Assert.False(store.Register("加-微 信联系", "password-123").Success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExistingProhibitedUsernameKeepsLoginCredentialButIsMaskedUntilRenamed()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var original = new L12PlatformStore(path);
            var account = original.Register("旧玩家", "password-123").Account!;
            RewriteStoredUsername(original, account.Id, "测试官方玩家");

            var migrated = new L12PlatformStore(path);
            var login = migrated.Login("测试官方玩家", "password-123");
            Assert.True(login.Success);
            Assert.True(login.Account!.MustChangeUsername);
            Assert.Equal("测试**玩家", login.Account.Username);
            Assert.Equal("测试**玩家", migrated.Account(account.Id)!.Username);

            Assert.False(migrated.ChangeUsername(account.Id, "wrong-password", "新玩家").Success);
            Assert.False(migrated.ChangeUsername(account.Id, "password-123", "官方新玩家").Success);
            var changed = migrated.ChangeUsername(account.Id, "password-123", "新玩家");
            Assert.True(changed.Success);
            Assert.False(changed.Account!.MustChangeUsername);
            Assert.Equal("新玩家", changed.Account.Username);
            Assert.False(migrated.Login("测试官方玩家", "password-123").Success);
            Assert.True(migrated.Login("新玩家", "password-123").Success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpRequiresUsernameChangeAndAllowsOnlyRecoverySurfaceUntilCompletion()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var seed = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var account = seed.Register("旧玩家", "password-123").Account!;
            RewriteStoredUsername(seed, account.Id, "官方玩家");
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var login = store.Login("官方玩家", "password-123");
            var otherLogin = store.Login("官方玩家", "password-123");

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            using (var blocked = new HttpRequestMessage(HttpMethod.Get, "/api/matches"))
            {
                blocked.Headers.Authorization = new("Bearer", login.Token);
                using var response = await client.SendAsync(blocked);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("username_change_required",
                    (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }
            using (var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
            {
                me.Headers.Authorization = new("Bearer", login.Token);
                using var response = await client.SendAsync(me);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(payload.GetProperty("mustChangeUsername").GetBoolean());
                Assert.Equal("**玩家", payload.GetProperty("username").GetString());
            }
            using (var rename = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-username"))
            {
                rename.Headers.Authorization = new("Bearer", login.Token);
                rename.Content = JsonContent.Create(new { currentPassword = "password-123", newUsername = "合规玩家" });
                using var response = await client.SendAsync(rename);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using (var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/matches"))
            {
                allowed.Headers.Authorization = new("Bearer", login.Token);
                using var response = await client.SendAsync(allowed);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            using (var revokedOtherDevice = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
            {
                revokedOtherDevice.Headers.Authorization = new("Bearer", otherLogin.Token);
                using var response = await client.SendAsync(revokedOtherDevice);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
        finally
        {
            if (server is not null) await server.DisposeAsync();
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void RewriteStoredUsername(L12PlatformStore store, string accountId, string username)
    {
        var data = typeof(L12PlatformStore).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        var accounts = (IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
        var row = accounts.Cast<object>().Single(value =>
            string.Equals((string)value.GetType().GetProperty("Id")!.GetValue(value)!, accountId,
                StringComparison.Ordinal));
        row.GetType().GetProperty("Username")!.SetValue(row, username);
        row.GetType().GetProperty("MustChangeUsername")!.SetValue(row, false);
        typeof(L12PlatformStore).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(store, [true]);
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-username-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
