using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class TournamentCreateVersionFlowTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("stale-page-version")]
    [InlineData("intervening-write")]
    public async Task RealHttpPreviewKeepsBusinessVersionAndInterveningChangesStillFailCas(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-tournament-create-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            store.Register("tourversion", "synthetic-password-123");
            var login = store.Login("tourversion", "synthetic-password-123");
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var input = new L12TournamentCreatePayload("Synthetic version flow", "swiss", "public", 16,
                null, "现行规则", "synthetic only", "after", "season", string.Empty, 50, 5, []);
            async Task<long> Context()
            {
                using var request = Authorized(HttpMethod.Get, "/api/tournaments/summaries?section=host&page=1&pageSize=1");
                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return body.GetProperty("platformVersion").GetInt64();
            }
            async Task<HttpResponseMessage> Create(long version, string key, bool preview)
            {
                using var request = Authorized(HttpMethod.Post, "/api/tournaments",
                    new TournamentCreateRequest(input, key, version, preview, "synthetic creation regression"));
                return await client.SendAsync(request);
            }
            HttpRequestMessage Authorized(HttpMethod method, string path, object? body = null)
            {
                var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token!);
                if (body is not null) request.Content = JsonContent.Create(body);
                return request;
            }
            var before = await Context();
            using var preview = await Create(before, "create-preview", true);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.False(preview.Headers.ETag!.IsWeak);
            var afterPreview = long.Parse(preview.Headers.ETag.Tag.Trim('"'));
            Assert.Equal(before, afterPreview); // Audit-only preview does not change the business resource version.
            Assert.Empty(store.Tournaments(login.Account!, null, null, true).Items);
            if (mode == "intervening-write") store.Register("otherowner", "synthetic-password-456");
            var expected = mode == "stale-page-version" ? before - 1 : afterPreview;
            var body = new TournamentCreateRequest(input, "create-commit", expected, false, "synthetic creation regression");
            using var commit = await Create(expected, "create-commit", false);
            if (mode != "success")
            {
                Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
                Assert.Empty(store.Tournaments(login.Account!, null, null, true).Items);
                return;
            }
            Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
            var created = await commit.Content.ReadFromJsonAsync<L12TournamentView>();
            Assert.NotNull(created);
            using var replayRequest = Authorized(HttpMethod.Post, "/api/tournaments", body);
            using var replay = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("X-Idempotent-Replay")));
            Assert.Equal(created.Id, (await replay.Content.ReadFromJsonAsync<L12TournamentView>())!.Id);
            Assert.Single(store.Tournaments(login.Account!, null, null, true).Items);
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }
}
