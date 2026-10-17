using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterSetupAnalyticsHttpTests
{
    [Fact]
    public async Task RealHandlersKeepAuthorizationAndAnalyticsOnlyRoleCannotReadMatchOrPrivateCandidates()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare();
        await f.End();
        var roles = (IDictionary<string, IReadOnlySet<L12Permission>>)typeof(L12Authorization)
            .GetField("RolePermissions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const string syntheticRole = "dsc-analytics-only-test";
        roles.Add(syntheticRole, new HashSet<L12Permission> { L12Permission.AdminAnalyticsRead });
        try
        {
            await using var http = await HttpFixture.Create(f);
            using (var anonymous = await http.Get("/api/admin/analytics/masters", null))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            var registered = http.Platform.Register("dscuser", "Synthetic123!");
            Assert.True(registered.Success, registered.Message);
            using (var player = await http.Get("/api/admin/analytics/masters", registered.Token))
                Assert.Equal(HttpStatusCode.Forbidden, player.StatusCode);
            Assert.True(http.Platform.SetRole(http.Admin, registered.Account!.Id, syntheticRole));
            var analytics = http.Platform.Login("dscuser", "Synthetic123!");
            Assert.True(analytics.Success);
            var uri = "/api/admin/analytics/masters?effectVersion=all&masterId=" + Uri.EscapeDataString(f.Catalog.DeckAt(0).MasterId);
            using (var response = await http.Get(uri, analytics.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
                Assert.Contains("scope-total", string.Join(',', response.Headers.GetValues("Server-Timing")));
                var text = await response.Content.ReadAsStringAsync();
                using var body = JsonDocument.Parse(text);
                var disasters = body.RootElement.GetProperty("disasters");
                Assert.Equal(2, disasters.GetProperty("coverage").GetProperty("participantSamples").GetInt32());
                Assert.True(disasters.GetProperty("privatePickGroupsHidden").GetBoolean());
                Assert.NotEmpty(disasters.GetProperty("items").EnumerateArray());
                Assert.All(disasters.GetProperty("items").EnumerateArray(),
                    row => Assert.Equal("ban", row.GetProperty("action").GetString()));
                Assert.DoesNotContain(f.Game.State.MatchId, text);
                Assert.DoesNotContain("candidateCardIds", text);
                Assert.DoesNotContain("synthetic-account", text);
            }
            // The very same cached scope must remain complete for a caller authorized to read matches.
            using (var response = await http.Get(uri, http.AdminToken))
            {
                response.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var disasters = body.RootElement.GetProperty("disasters");
                Assert.False(disasters.GetProperty("privatePickGroupsHidden").GetBoolean());
                Assert.Contains(disasters.GetProperty("items").EnumerateArray(),
                    row => row.GetProperty("action").GetString() == "pick");
            }
            using (var denied = await http.Get($"/api/admin/matches/{f.Game.State.MatchId}", analytics.Token))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var invalid = await http.Get(uri + "&disasterOrigin=bad", http.AdminToken))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally { roles.Remove(syntheticRole); }
    }

    [Fact]
    public async Task ActiveDetailIsPrivateAndEndedSummarySurvivesActualReplayGoneResponse()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Step();
        await using var http = await HttpFixture.Create(f);
        var uri = $"/api/admin/matches/{f.Game.State.MatchId}";
        using (var response = await http.Get(uri, http.AdminToken))
        {
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var summary = body.RootElement.GetProperty("disasterSetup");
            Assert.Equal("private", summary.GetProperty("coverage").GetString());
            Assert.Empty(summary.GetProperty("decisions").EnumerateArray());
            Assert.Empty(summary.GetProperty("composition").EnumerateArray());
        }
        await f.Prepare();
        await f.End();
        var cleanup = await f.Recorder.RunPlayerReplayCleanupIfDueAsync(activeMatchIds: [],
            evidenceProvider: () => new([], []), utcNow: f.Now.AddDays(9));
        Assert.Equal(1, cleanup.PurgedMatches);
        using (var gone = await http.Get(uri + "?includeReplay=true", http.AdminToken))
            Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        using (var response = await http.Get(uri, http.AdminToken))
        {
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(5, body.RootElement.GetProperty("disasterSetup").GetProperty("decisions").GetArrayLength());
            Assert.Equal("exact", body.RootElement.GetProperty("disasterSetup").GetProperty("coverage").GetString());
            Assert.Empty(body.RootElement.GetProperty("replay").EnumerateArray());
        }
    }

    private sealed class HttpFixture : IAsyncDisposable
    {
        internal L12PlatformStore Platform = null!;
        internal L12AccountView Admin = null!;
        internal string AdminToken = "";
        private L12WebSocketServer _server = null!;
        private HttpClient _client = null!;
        private string? _previousHost;

        internal static async Task<HttpFixture> Create(DisasterFactFixture facts)
        {
            var fixture = new HttpFixture { _previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST") };
            try
            {
                fixture.Platform = new L12PlatformStore(Path.Combine(facts.Root, "synthetic-http-platform.json"),
                    facts.Catalog.PresetDecks, officialCards: facts.Catalog.Cards);
                var login = fixture.Platform.Login("Admin", "L12master");
                Assert.True(login.Success);
                fixture.Admin = login.Account!;
                fixture.AdminToken = login.Token!;
                fixture._server = new L12WebSocketServer(new L12RoomManager(facts.Catalog, facts.Recorder, fixture.Platform),
                    facts.Recorder, fixture.Platform, facts.Catalog);
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
                await fixture._server.StartAsync(0);
                fixture._client = new HttpClient { BaseAddress = new Uri(Assert.Single(fixture._server.Addresses)) };
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal Task<HttpResponseMessage> Get(string uri, string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Send(request);
        }
        private async Task<HttpResponseMessage> Send(HttpRequestMessage request)
        { using (request) return await _client.SendAsync(request); }
        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_server is not null) await _server.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", _previousHost);
        }
    }
}
