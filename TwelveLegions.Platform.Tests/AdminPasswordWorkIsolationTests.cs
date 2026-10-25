using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminPasswordWorkIsolationTests
{
    [Theory]
    [InlineData("reset-password")]
    [InlineData("delete")]
    public async Task RealHttpAdminPasswordWorkDoesNotHoldSharedPlatformGate(string operation)
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-admin-work-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<HttpResponseMessage>? pending = null;
        Task<long>? read = null;
        bool progressed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("pwmanager", "password-123");
            Assert.True(admin.Success, admin.Message); Assert.True(player.Success, player.Message);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            client.DefaultRequestHeaders.Authorization = new("Bearer", admin.Token);
            store.BeforeAdminPasswordWorkForTesting = _ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic admin password barrier.");
            };
            try
            {
                pending = client.PostAsJsonAsync($"/api/admin/accounts/{player.Account!.Id}/{operation}",
                    new { reason = "synthetic regression", idempotencyKey = "admin-work-" + operation,
                        expectedVersion = player.Account.PermissionVersion });
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Real HTTP command must reach password work.");
                read = Task.Factory.StartNew(() => store.Version, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                progressed = await Task.WhenAny(read, Task.Delay(1_000)) == read;
            }
            finally
            {
                release.Set();
                if (pending is not null) using (var response = await pending.WaitAsync(TimeSpan.FromSeconds(10)))
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                if (read is not null) await read.WaitAsync(TimeSpan.FromSeconds(10));
                store.BeforeAdminPasswordWorkForTesting = null;
            }
            Assert.True(progressed, "Actual administrator password work must not block unrelated platform reads.");
        }
        finally
        {
            release.Set();
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Theory]
    [InlineData("reset-password", "busy")]
    [InlineData("delete", "busy")]
    [InlineData("reset-password", "changed")]
    [InlineData("delete", "changed")]
    [InlineData("reset-password", "revoked")]
    [InlineData("delete", "revoked")]
    public async Task RealHttpAdmissionAndFinalRevalidationFailClosed(string operation, string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-apw-http-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<HttpResponseMessage>? pending = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("pwmanager", "password-123");
            Assert.True(admin.Success); Assert.True(player.Success, player.Message);
            var targetId = player.Account!.Id;
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            string? recordedMatchId = null, recorderBefore = null;
            if (operation == "delete" && scenario == "changed")
            {
                var game = new L12GameEngine(catalog, "credential-cas", "PWCAS", 861012,
                    [player.Account.Username, admin.Account!.Username],
                    [catalog.DeckAt(0), catalog.DeckAt(1)], skipPreparation: true);
                await recorder.StartAsync(game, "friendly", targetId, admin.Account.Id);
                recordedMatchId = game.State.MatchId;
                recorderBefore = JsonSerializer.Serialize(await recorder.GetMatchAsync(recordedMatchId));
            }
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            client.DefaultRequestHeaders.Authorization = new("Bearer", admin.Token);
            var body = new { reason = "synthetic admission", idempotencyKey = "admission-test", expectedVersion = player.Account.PermissionVersion };
            if (scenario == "busy")
            {
                store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
                var version = store.Version;
                var audit = store.AdminAudit(null, 100).Count;
                using (store.PasswordWorkBudget.Acquire())
                using (var response = await client.PostAsJsonAsync($"/api/admin/v1/accounts/{targetId}/{operation}", body))
                {
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                    Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter!.Delta);
                }
                Assert.Equal(version, store.Version);
                Assert.Equal(audit, store.AdminAudit(null, 100).Count);
                Assert.Null(store.FindAdminCommand(admin.Account!.Id, "admission-test"));
                using var retry = await client.PostAsJsonAsync($"/api/admin/v1/accounts/{targetId}/{operation}", body);
                Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            }
            else
            {
                store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
                store.BeforeAdminPasswordWorkForTesting = _ =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic HTTP barrier.");
                };
                pending = client.PostAsJsonAsync($"/api/admin/v1/accounts/{targetId}/{operation}", body);
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    if (scenario == "changed") Assert.True(store.ChangePassword(targetId, "password-123", "winner-password").Success);
                    else
                    {
                        var actor = store.AuthenticateTokenSession(admin.Token)!;
                        store.RevokeOwnSession(actor, actor.SessionId);
                    }
                }
                finally { release.Set(); }
                using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(scenario == "changed" ? HttpStatusCode.Conflict : HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.False(store.Account(targetId)!.Deleted);
                Assert.False(store.Account(targetId)!.Disabled);
                Assert.False(store.Account(targetId)!.MustChangePassword);
                Assert.True(store.Login("pwmanager", scenario == "changed" ? "winner-password" : "password-123").Success);
                if (recordedMatchId is not null)
                {
                    Assert.Equal(recorderBefore, JsonSerializer.Serialize(await recorder.GetMatchAsync(recordedMatchId)));
                    // Positive control proves this real record is targeted by the same anonymizer.
                    Assert.Equal(1, await recorder.AnonymizeAccountAsync(targetId, player.Account.Username,
                        L12PlatformStore.DeletedAccountName(targetId)));
                    Assert.NotEqual(recorderBefore, JsonSerializer.Serialize(await recorder.GetMatchAsync(recordedMatchId)));
                }
            }
        }
        finally
        {
            release.Set();
            if (pending is not null) using (await pending.WaitAsync(TimeSpan.FromSeconds(10))) { }
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
