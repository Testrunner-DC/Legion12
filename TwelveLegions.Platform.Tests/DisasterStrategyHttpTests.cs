using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterStrategyHttpTests
{
    [Fact]
    public async Task StrategyHandlerAuthorizesAndProjectsEveryPrivateBranchWithoutMutatingCachedReport()
    {
        await using var facts = await DisasterFactFixture.Create("ranked");
        await facts.Prepare(); await facts.End();
        await using var f = await StrategyHttpFixture.Create(facts);
        using (var response = await f.Get(f.Uri, null)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await f.Get(f.Uri, f.PlayerToken)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var roles = (IDictionary<string, IReadOnlySet<L12Permission>>)typeof(L12Authorization)
            .GetField("RolePermissions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const string role = "synthetic-strategy-analyst";
        roles.Add(role, new HashSet<L12Permission> { L12Permission.AdminAnalyticsRead });
        try
        {
            Assert.True(f.Platform.SetRole(f.Admin, f.Player.Id, role));
            var login = f.Platform.Login("stratuser", "Synthetic123!"); Assert.True(login.Success);
            using (var response = await f.Get(f.Uri, login.Token))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var report = body.RootElement;
                Assert.True(report.GetProperty("privatePickGroupsHidden").GetBoolean());
                Assert.NotEmpty(report.GetProperty("items").EnumerateArray());
                Assert.All(report.GetProperty("items").EnumerateArray(), x => Assert.Equal("ban", x.GetProperty("action").GetString()));
                Assert.All(report.GetProperty("conditions").EnumerateArray(), x => Assert.True(x.GetProperty("step").GetInt32() < 3));
                Assert.DoesNotContain(facts.Game.State.MatchId, report.GetRawText());
                Assert.DoesNotContain("synthetic-account", report.GetRawText());
            }
            using (var response = await f.Get(f.Uri, f.AdminToken))
            {
                response.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.False(body.RootElement.GetProperty("privatePickGroupsHidden").GetBoolean());
                Assert.Contains(body.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("action").GetString() == "pick");
                Assert.Contains(body.RootElement.GetProperty("conditions").EnumerateArray(), x => x.GetProperty("step").GetInt32() == 5);
            }
            using (var response = await f.Get(f.Uri + "&disasterOrigin=forged", f.AdminToken))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { roles.Remove(role); }
    }

    [Fact]
    public async Task PermissionRevocationWhileReadIsQueuedFailsClosedBeforeReturningPrivateCandidates()
    {
        await using var facts = await DisasterFactFixture.Create("ranked");
        await facts.Prepare(); await facts.End();
        await using var f = await StrategyHttpFixture.Create(facts);
        Assert.True(f.Platform.SetRole(f.Admin, f.Player.Id, "admin"));
        var reader = f.Platform.Login("stratuser", "Synthetic123!"); Assert.True(reader.Success);
        var gate = (SemaphoreSlim)typeof(MatchRecorder).GetField("_cardAnalyticsWorkers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(facts.Recorder)!;
        await gate.WaitAsync(); await gate.WaitAsync();
        Task<HttpResponseMessage>? request = null;
        try
        {
            request = f.Get(f.Uri, reader.Token);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (facts.Recorder.CardAnalyticsExecutionCounts.Active == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(facts.Recorder.CardAnalyticsExecutionCounts.Active > 0, "The real authorized request must reach the worker queue before revocation");
            Assert.True(f.Platform.SetRole(f.Admin, f.Player.Id, "player"));
        }
        finally { gate.Release(2); }
        using var response = await request!;
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("candidateCardIds", await response.Content.ReadAsStringAsync());
    }

    private sealed class StrategyHttpFixture : IAsyncDisposable
    {
        internal L12PlatformStore Platform = null!;
        internal L12AccountView Admin = null!, Player = null!;
        internal string AdminToken = "", PlayerToken = "", Uri = "";
        private L12WebSocketServer? _server;
        private HttpClient? _client;
        private string? _previousHost;
        internal static async Task<StrategyHttpFixture> Create(DisasterFactFixture facts)
        {
            var f = new StrategyHttpFixture { _previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST") };
            try
            {
                f.Platform = new(Path.Combine(facts.Root, "synthetic-strategy-platform.json"), facts.Catalog.PresetDecks, officialCards: facts.Catalog.Cards);
                var admin = f.Platform.Login("Admin", "L12master"); Assert.True(admin.Success);
                f.Admin = admin.Account!; f.AdminToken = admin.Token!;
                var player = f.Platform.Register("stratuser", "Synthetic123!"); Assert.True(player.Success, player.Message);
                f.Player = player.Account!; f.PlayerToken = player.Token!;
                f.Uri = "/api/admin/analytics/disasters/strategy?effectVersion=all&initiative=first&masterId="
                    + System.Uri.EscapeDataString(facts.Catalog.DeckAt(0).MasterId) + "&opponentMasterId="
                    + System.Uri.EscapeDataString(facts.Catalog.DeckAt(0).MasterId);
                f._server = new(new L12RoomManager(facts.Catalog, facts.Recorder, f.Platform), facts.Recorder, f.Platform, facts.Catalog);
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
                await f._server.StartAsync(0);
                f._client = new() { BaseAddress = new System.Uri(Assert.Single(f._server.Addresses)) };
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        internal async Task<HttpResponseMessage> Get(string uri, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _client!.SendAsync(request);
        }
        public async ValueTask DisposeAsync()
        {
            _client?.Dispose(); if (_server is not null) await _server.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", _previousHost);
        }
    }
}
