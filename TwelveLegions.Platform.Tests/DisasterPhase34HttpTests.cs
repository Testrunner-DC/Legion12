using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterPhase34HttpTests
{
    [Fact]
    public async Task ScopedHandlerHasAuthorizationAuditAndPermissionDependentProjectionWithoutCacheMutation()
    {
        await using var facts = await DisasterFactFixture.Create("ranked");
        await facts.Prepare(); await facts.End();
        await using var f = await HttpFixture.Create(facts);
        var roles = (IDictionary<string, IReadOnlySet<L12Permission>>)typeof(L12Authorization)
            .GetField("RolePermissions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const string role = "synthetic-dsc34-analytics";
        roles.Add(role, new HashSet<L12Permission> { L12Permission.AdminAnalyticsRead });
        try
        {
            var uri = "/api/admin/analytics/disasters?effectVersion=all&includedCardId=" + Uri.EscapeDataString(facts.Catalog.DeckAt(0).CardIds[0]);
            using (var response = await f.Get(uri)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await f.Get(uri, f.OwnerToken)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True(f.Platform.SetRole(f.Admin, f.Owner.Id, role));
            var login = f.Platform.Login("dsc34owner", "Synthetic123!"); Assert.True(login.Success);
            using (var response = await f.Get(uri, login.Token))
            {
                response.EnsureSuccessStatusCode(); AssertNoStore(response);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.True(body.RootElement.GetProperty("privatePickGroupsHidden").GetBoolean());
                Assert.All(body.RootElement.GetProperty("items").EnumerateArray(), r => Assert.Equal("ban", r.GetProperty("action").GetString()));
            }
            using (var response = await f.Get(uri, f.AdminToken))
            {
                response.EnsureSuccessStatusCode(); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Contains(body.RootElement.GetProperty("items").EnumerateArray(), r => r.GetProperty("action").GetString() == "pick");
            }
            using (var response = await f.Get(uri + "&deckSignature=bad", f.AdminToken)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await f.Get(uri + "&disasterOrigin=forged", f.AdminToken)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { roles.Remove(role); }
    }

    [Fact]
    public async Task PersonalSummaryRequiresRealAccountOwnershipAndDoesNotRequireReplayPayload()
    {
        await using var facts = await DisasterFactFixture.Create();
        await facts.Prepare(); await facts.End();
        await using var f = await HttpFixture.Create(facts);
        await f.SetRecordedAccounts(f.Owner.Id, f.Other.Id);
        var uri = $"/api/me/matches/{facts.Game.State.MatchId}/disasters";
        using (var response = await f.Get(uri)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await f.Get(uri, f.AdminToken)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await facts.Recorder.RunPlayerReplayCleanupIfDueAsync(activeMatchIds: [], evidenceProvider: () => new([], []), utcNow: facts.Now.AddDays(9));
        using (var response = await f.Get(uri, f.OwnerToken))
        {
            response.EnsureSuccessStatusCode(); AssertNoStore(response);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var decisions = body.RootElement.GetProperty("decisions").EnumerateArray().ToArray();
            Assert.Equal(4, decisions.Length); var pick = Assert.Single(decisions, r => r.GetProperty("action").GetString() == "pick");
            Assert.Equal(0, pick.GetProperty("actorIndex").GetInt32());
            Assert.Equal(3, pick.GetProperty("ownCandidateCardIds").GetArrayLength());
        }
        f.Platform.RevokeOwnSessions(f.Platform.AuthenticateTokenSession(f.OwnerToken)!, C("revoke"));
        using (var response = await f.Get(uri, f.OwnerToken)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TournamentUsesRealEndedBindingAndPublicOnlyProjectionWithVisibilityReadback()
    {
        await using var facts = await DisasterFactFixture.Create("tournament");
        await facts.Prepare(); await facts.End();
        await using var f = await HttpFixture.Create(facts);
        var tournament = global::TwelveLegions.Platform.Tests.TournamentTestSetup.CreateWithRegisteredOrganizer(f.Platform, f.Owner, new("Synthetic DSC", "swiss", "public", 16,
            DateTimeOffset.UtcNow.AddHours(2), "S01/S02", "", "after", "all", "", 50, 5), C("create"), true);
        tournament = f.Platform.RegisterTournament(f.Other, tournament.Id, new(), tournament.Version, C("register"), true);
        foreach (var player in new[] { f.Owner, f.Other }) tournament = f.Platform.PreCheckInTournament(player, tournament.Id,
            new(f.Platform.Decks(player.Id)[0].Name, ""), tournament.Version, C("checkin-" + player.Id), true);
        tournament = f.Platform.StartTournament(f.Owner, tournament.Id, tournament.Version, C("start"), true);
        var table = Assert.Single(tournament.Rounds[0].Matches);
        var uri = $"/api/public/tournaments/code/{tournament.Code}/rounds/1/tables/{table.Table}/disasters";
        using (var response = await f.Get(uri)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        tournament = f.Platform.LinkTournamentMatch(f.Admin, tournament.Id, table.Id, new(facts.Game.State.MatchId), tournament.Version, C("link"), true);
        tournament = f.Platform.ApplyTournamentRuling(f.Admin, tournament.Id, table.Id,
            new("result", null, "player-a", "synthetic result"), tournament.Version, C("result"), true);
        // A wrong recorded reference must not disclose an unrelated game's facts.
        using (var response = await f.Get(uri)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await f.SetRecordedAccounts(table.PlayerAAccountId, table.PlayerBAccountId!);
        using (var response = await f.Get(uri))
        {
            response.EnsureSuccessStatusCode(); AssertNoStore(response);
            var text = await response.Content.ReadAsStringAsync(); using var body = JsonDocument.Parse(text);
            Assert.Equal("public", body.RootElement.GetProperty("visibility").GetString());
            Assert.Equal(3, body.RootElement.GetProperty("decisions").GetArrayLength());
            Assert.All(body.RootElement.GetProperty("decisions").EnumerateArray(), r => Assert.Equal("ban", r.GetProperty("action").GetString()));
            Assert.DoesNotContain(facts.Game.State.MatchId, text); Assert.DoesNotContain("synthetic-account", text);
        }
        var lease = Assert.IsType<L12TournamentDisasterReadLease>(f.Platform.CaptureTournamentDisasterRead(tournament.Code, 1, table.Table, null));
        tournament = f.Platform.SetTournamentVisibility(f.Owner, tournament.Id, new("code", "staff"), tournament.Version, C("visibility"), true);
        Assert.False(f.Platform.RevalidateTournamentDisasterRead(tournament.Code, lease, null));
        using (var response = await f.Get(uri)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await f.Get(uri, "synthetic-invalid-token")) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicDeckReadUsesExistingGenerationAndRejectsArbitrarySlicesAndStaleTokens()
    {
        await using var facts = await DisasterFactFixture.Create("ranked");
        await facts.Prepare(); await facts.End();
        await using var f = await HttpFixture.Create(facts);
        var published = f.Platform.PublishDeck(f.Owner.Id, facts.Catalog.DeckAt(0), null)!;
        var capture = f.Platform.CapturePublicDeckStatisticsRead(facts.Catalog, published.PublicCode, null, null);
        Assert.Equal("available", capture.Status);
        var uri = $"/api/public-decks/{published.PublicCode}/disasters?expectedReadToken={capture.Lease!.ReadToken}";
        using (var response = await f.Get(uri))
        {
            response.EnsureSuccessStatusCode(); AssertNoStore(response);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("insufficient", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, body.RootElement.GetProperty("usableMatches").GetInt32());
        }
        using (var response = await f.Get(uri + "&from=2026-10-01")) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await f.Get(uri.Replace(capture.Lease.ReadToken, new string('b', 64)))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(f.Platform.DeletePublishedDeck(f.Owner.Id, published.Id));
        using (var response = await f.Get(uri)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static L12AdminAuditContext C(string id) => new("dsc34-" + id);
    private static void AssertNoStore(HttpResponseMessage response) => Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");

    private sealed class HttpFixture : IAsyncDisposable
    {
        internal L12PlatformStore Platform = null!;
        internal L12AccountView Admin = null!, Owner = null!, Other = null!;
        internal string AdminToken = "", OwnerToken = "";
        private DisasterFactFixture _facts = null!;
        private L12WebSocketServer? _server;
        private HttpClient? _client;
        private string? _previousHost;
        internal static async Task<HttpFixture> Create(DisasterFactFixture facts)
        {
            var f = new HttpFixture { _facts = facts, _previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST") };
            try
            {
                f.Platform = new(Path.Combine(facts.Root, "synthetic-phase34-platform.json"), facts.Catalog.PresetDecks, officialCards: facts.Catalog.Cards);
                var admin = f.Platform.Login("Admin", "L12master"); Assert.True(admin.Success);
                f.Admin = admin.Account!; f.AdminToken = admin.Token!;
                var owner = f.Platform.Register("dsc34owner", "Synthetic123!"); Assert.True(owner.Success);
                f.Owner = owner.Account!; f.OwnerToken = owner.Token!;
                var other = f.Platform.Register("dsc34other", "Synthetic123!"); Assert.True(other.Success); f.Other = other.Account!;
                f._server = new(new(facts.Catalog, facts.Recorder, f.Platform), facts.Recorder, f.Platform, facts.Catalog);
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1"); await f._server.StartAsync(0);
                f._client = new() { BaseAddress = new Uri(Assert.Single(f._server.Addresses)) };
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        internal async Task SetRecordedAccounts(string a, string b)
        {
            await using var connection = new SqliteConnection($"Data Source={_facts.Database};Pooling=False");
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE matches SET account_0=$a,account_1=$b WHERE match_id=$id; UPDATE match_participants SET account_id=CASE player_index WHEN 0 THEN $a ELSE $b END WHERE match_id=$id;";
            command.Parameters.AddWithValue("$a", a); command.Parameters.AddWithValue("$b", b); command.Parameters.AddWithValue("$id", _facts.Game.State.MatchId);
            await command.ExecuteNonQueryAsync();
        }
        internal async Task<HttpResponseMessage> Get(string uri, string? token = null)
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
