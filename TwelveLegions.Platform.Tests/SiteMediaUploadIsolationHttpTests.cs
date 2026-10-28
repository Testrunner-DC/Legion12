using System.Buffers.Binary;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SiteMediaUploadIsolationHttpTests
{
    [Theory]
    [InlineData("revoke", false)]
    [InlineData("revoke", true)]
    [InlineData("disable", false)]
    [InlineData("disable", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("demote", false)]
    [InlineData("demote", true)]
    [InlineData("version", false)]
    [InlineData("version", true)]
    [InlineData("expire", false)]
    [InlineData("expire", true)]
    public async Task UploadRechecksRealHttpIdentityBeforeAndAfterFilePreparation(string change, bool afterWrite)
    {
        await WithServer(async (store, rootAdmin, actor, client, root) =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var blocked = 0;
            var stageName = afterWrite ? "after-site-media-file-write" : "before-site-media-authorization";
            store.StorageFailureInjector = stage =>
            {
                if (stage != stageName || Interlocked.CompareExchange(ref blocked, 1, 0) != 0) return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("HTTP upload barrier expired");
            };
            var pending = PostUpload(client);
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
                Assert.Empty(store.AdminSiteMedia());
                switch (change)
                {
                    case "revoke": store.RevokeOwnSession(actor, actor.SessionId); break;
                    case "disable": store.SetAccountDisabled(rootAdmin, actor.Account.Id, true,
                        "synthetic upload isolation", new("upload-disable"), true); break;
                    case "delete": store.DeleteAccountPersonalData(rootAdmin, actor.Account.Id,
                        "synthetic upload isolation", new("upload-delete"), true); break;
                    case "demote": Assert.True(store.SetRole(rootAdmin, actor.Account.Id, "player")); break;
                    case "version":
                        Assert.True(store.SetRole(rootAdmin, actor.Account.Id, "player"));
                        Assert.True(store.SetRole(rootAdmin, actor.Account.Id, "admin"));
                        break;
                    case "expire":
                        store.ExecuteAdminTransaction(() =>
                        {
                            var state = typeof(L12PlatformStore).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
                            var sessions = (IEnumerable)state.GetType().GetProperty("Sessions")!.GetValue(state)!;
                            var row = sessions.Cast<object>().Single(item =>
                                (string)item.GetType().GetProperty("Id")!.GetValue(item)! == actor.SessionId);
                            row.GetType().GetProperty("ExpiresAt")!.SetValue(row, DateTimeOffset.UtcNow.AddMinutes(-1));
                            return store.SetRole(rootAdmin, actor.Account.Id, "admin");
                        });
                        break;
                }
                release.Set();
                using var response = await pending;
                Assert.Equal(change is "demote" or "version" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                    response.StatusCode);
                Assert.Empty(store.AdminSiteMedia());
                Assert.Empty(new L12PlatformStore(Path.Combine(root, "platform.json")).AdminSiteMedia());
                var mediaRoot = Path.Combine(root, "site-media");
                if (Directory.Exists(mediaRoot)) Assert.Empty(Directory.GetFiles(mediaRoot));
            }
            finally
            {
                release.Set();
                using var response = await pending;
                store.StorageFailureInjector = null;
            }
        });
    }

    [Fact]
    public async Task SuccessfulUploadKeepsImmutableDeliveryAndRepeatedPostSemantics()
    {
        await WithServer(async (store, _, _, client, root) =>
        {
            using var response = await PostUpload(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var url = json.RootElement.GetProperty("desktopUrl").GetString()!;
            using var image = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/webp", image.Content.Headers.ContentType!.MediaType);
            Assert.Contains("immutable", image.Headers.CacheControl!.ToString());
            Assert.NotNull(image.Headers.ETag);
            using var repeat = await PostUpload(client);
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
            Assert.Equal(2, store.AdminSiteMedia().Count);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "site-media")));
        });
    }

    private static async Task<HttpResponseMessage> PostUpload(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("article"), "kind");
        form.Add(new StringContent("上传隔离测试图"), "altText");
        foreach (var name in new[] { "original", "desktop", "mobile", "thumbnail" })
        {
            var bytes = new byte[30];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 22);
            Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(bytes, 8);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 10);
            bytes[24] = 99; bytes[27] = 99;
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new("image/webp");
            form.Add(part, name, name + ".webp");
        }
        return await client.PostAsync("/api/admin/site/media", form);
    }

    private static async Task WithServer(Func<L12PlatformStore, L12AccountView, L12AuthenticatedSession, HttpClient, string, Task> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "media-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        var passed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var registration = store.Register("uploadedit", "password-123");
            Assert.True(registration.Success, registration.Message);
            Assert.True(store.SetRole(admin, registration.Account!.Id, "admin"));
            var actor = store.AuthenticateTokenSession(registration.Token)!;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new(new(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);
            await run(store, admin, actor, client, root);
            passed = true;
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", oldHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic media fixture retained: " + root);
        }
    }
}
