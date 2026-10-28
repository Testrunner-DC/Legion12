using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class FriendHistoryHttpTests
{
    [Fact]
    public async Task HttpPreferenceIsOwnDurableAndControlsFriendReadWithoutReplayPermission()
    {
        await using var f = await Fixture.Start();
        using (var original = await f.Get())
        {
            Assert.Equal(HttpStatusCode.OK, original.StatusCode);
            Assert.Equal("no-store", original.Headers.CacheControl?.ToString());
        }
        using (var response = await f.Put(false)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(f.Store.Authenticate(f.BearerB)!.PublicHistory);
        Assert.True(f.Store.Authenticate(f.BearerA)!.PublicHistory);
        using (var hidden = await f.Get()) Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        var reopened = new L12PlatformStore(f.PlatformPath, officialCards: f.Catalog.Cards);
        Assert.False(reopened.Authenticate(f.BearerB)!.PublicHistory);
        using (var response = await f.Put(true)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var visible = await f.Get()) Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        using var invalid = new HttpRequestMessage(HttpMethod.Put, "/api/me/history-preferences") { Content = JsonContent.Create(new { accountId = f.TargetId }) };
        invalid.Headers.Authorization = AuthenticationHeaderValue.Parse(f.BearerA);
        using var rejected = await f.Client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task AccountOwnedBoundedSafeSummaryMatchesStatisticsAndNeverGuessesLegacyName()
    {
        await using var f = await Fixture.Start();
        for (var i = 0; i < 12; i++) await f.Seed("recent" + i, DateTimeOffset.UtcNow.AddMinutes(-i - 1), i % 2, i % 3);
        await f.Seed("older", DateTimeOffset.UtcNow.AddDays(-8), 0, 1, "friendly");
        await f.Seed("sandbox", DateTimeOffset.UtcNow.AddMinutes(-1), 0, 1, "sandbox");
        await f.Seed("error", DateTimeOffset.UtcNow.AddMinutes(-1), 0, 1, error: "PRIVATE_ERROR");
        await f.Seed("pending", DateTimeOffset.UtcNow.AddMinutes(-1), 0, 1, ended: false);
        await f.Seed("legacy-name-only", DateTimeOffset.UtcNow.AddMinutes(-1), 0, 1, legacy: true);
        using var response = await f.Get("7d");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var overall = root.GetProperty("statistics").GetProperty("overall");
        Assert.Equal(12, overall.GetProperty("games").GetInt32());
        Assert.Equal(4, overall.GetProperty("wins").GetInt32());
        Assert.Equal(4, overall.GetProperty("losses").GetInt32());
        Assert.Equal(4, overall.GetProperty("draws").GetInt32());
        Assert.Equal(10, root.GetProperty("recentMatches").GetArrayLength());
        Assert.Equal(10, root.GetProperty("recentLimit").GetInt32());
        foreach (var match in root.GetProperty("recentMatches").EnumerateArray())
            Assert.Equal(new[] { "startedUtc", "endedUtc", "opponentName", "masterName", "opponentMasterName", "modeId", "result" }, match.EnumerateObject().Select(property => property.Name));
        foreach (var secret in new[] { "PRIVATE_ROOM", "PRIVATE_DECK", "PRIVATE_HASH", "PRIVATE_ERROR", "commandJson", "initialState", "replay", "matchId" })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        using var longer = await f.Get("30d");
        using var longerJson = JsonDocument.Parse(await longer.Content.ReadAsStringAsync());
        Assert.Equal(13, longerJson.RootElement.GetProperty("statistics").GetProperty("overall").GetProperty("games").GetInt32());
        var capture = f.Store.CaptureFriendHistoryRead(f.BearerA, f.TargetId).Lease!;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Recorder.ReadFriendHistoryAsync(capture, "7d", canceled.Token));
        using var forbiddenReplay = new HttpRequestMessage(HttpMethod.Get, "/api/matches/recent0/replay");
        // The viewer participates in these synthetic rows, but no replay payload exists and this feature grants none.
        forbiddenReplay.Headers.Authorization = AuthenticationHeaderValue.Parse(f.BearerC);
        using var inaccessible = await f.Client.SendAsync(forbiddenReplay);
        Assert.NotEqual(HttpStatusCode.OK, inaccessible.StatusCode);
    }

    [Theory]
    [InlineData("private", "private")]
    [InlineData("remove", "not_found")]
    [InlineData("block-forward", "not_found")]
    [InlineData("block-reverse", "not_found")]
    [InlineData("revoke", "unauthorized")]
    [InlineData("disable-target", "not_found")]
    [InlineData("delete-target", "not_found")]
    public async Task ReadLeaseRejectsPermissionChangesAfterDatabaseRead(string change, string expected)
    {
        await using var f = await Fixture.Start();
        var captured = f.Store.CaptureFriendHistoryRead(f.BearerA, f.TargetId).Lease!;
        _ = await f.Recorder.ReadFriendHistoryAsync(captured, "7d", CancellationToken.None);
        switch (change)
        {
            case "private": f.Store.SetOwnHistoryPreference(f.BearerB, false); break;
            case "remove": Assert.True(f.Store.RemoveFriend(f.ViewerId, f.TargetId)); break;
            case "block-forward": Assert.True(f.Store.BlockAccount(f.ViewerId, f.TargetId).Success); break;
            case "block-reverse": Assert.True(f.Store.BlockAccount(f.TargetId, f.ViewerId).Success); break;
            case "revoke":
                var actor = f.Store.AuthenticateSession(f.BearerA)!;
                f.Store.RevokeOwnSession(actor, actor.SessionId); break;
            case "disable-target":
                f.Store.SetAccountDisabled(f.Admin, f.TargetId, true, "合成隐私测试", new("friend-history-disable"), true); break;
            case "delete-target":
                f.Store.DeleteAccountPersonalData(f.Admin, f.TargetId, "合成隐私测试", new("friend-history-delete"), true); break;
        }
        Assert.Equal(expected, f.Store.RevalidateFriendHistoryRead(f.BearerA, captured));
        using var denied = await f.Get();
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
    }

    [Fact]
    public async Task HttpRejectsAnonymousNonFriendAndInvalidRange()
    {
        await using var f = await Fixture.Start();
        using (var anonymous = await f.Client.GetAsync($"/api/friends/{f.TargetId}/history")) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var outsider = new HttpRequestMessage(HttpMethod.Get, $"/api/friends/{f.TargetId}/history");
        outsider.Headers.Authorization = AuthenticationHeaderValue.Parse(f.BearerC);
        using (var response = await f.Client.SendAsync(outsider)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var invalid = await f.Get("all")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var anonymousPut = await f.Client.PutAsJsonAsync("/api/me/history-preferences", new { publicHistory = false });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPut.StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fh-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string? _oldHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        private L12WebSocketServer _server = null!;
        public string PlatformPath => Path.Combine(_root, "p.json");
        private string MatchesPath => Path.Combine(_root, "m.db");
        public L12Catalog Catalog = null!;
        public L12PlatformStore Store = null!;
        public MatchRecorder Recorder = null!;
        public HttpClient Client = null!;
        public string ViewerId = "", TargetId = "", BearerA = "", BearerB = "", BearerC = "";
        public L12AccountView Admin = null!;
        public static async Task<Fixture> Start()
        {
            var f = new Fixture();
            try
            {
                Directory.CreateDirectory(f._root);
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
                f.Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
                f.Store = new(f.PlatformPath, officialCards: f.Catalog.Cards);
                f.Admin = f.Store.Login("Admin", "L12master").Account!;
                var a = f.Store.Register("fhviewer", "password-123"); var b = f.Store.Register("fhtarget", "password-123"); var c = f.Store.Register("fhstranger", "password-123");
                f.ViewerId = a.Account!.Id; f.TargetId = b.Account!.Id;
                f.BearerA = "Bearer " + a.Token; f.BearerB = "Bearer " + b.Token; f.BearerC = "Bearer " + c.Token;
                Assert.True(f.Store.SendFriendRequest(f.ViewerId, f.TargetId).Success);
                Assert.True(f.Store.ResolveFriendRequest(f.TargetId, f.ViewerId, true).Success);
                f.Recorder = new(f.MatchesPath); await f.Recorder.InitializeAsync();
                f._server = new(new(f.Catalog, f.Recorder, f.Store), f.Recorder, f.Store, f.Catalog);
                await f._server.StartAsync(0);
                f.Client = new() { BaseAddress = new Uri(Assert.Single(f._server.Addresses)) };
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public async Task<HttpResponseMessage> Get(string range = "season")
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/friends/{TargetId}/history?range={range}");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(BearerA);
            return await Client.SendAsync(request);
        }
        public async Task<HttpResponseMessage> Put(bool enabled)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/history-preferences") { Content = JsonContent.Create(new { accountId = ViewerId, publicHistory = enabled }) };
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(BearerB);
            return await Client.SendAsync(request);
        }
        public async Task Seed(string id, DateTimeOffset endedAt, int player, int outcome, string mode = "ranked", string? error = null, bool ended = true, bool legacy = false)
        {
            await using var connection = new SqliteConnection("Data Source=" + MatchesPath); await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,ended_utc,winner,final_hash,error,mode_id,account_0,account_1,season_id,first_player)
                VALUES($id,'PRIVATE_ROOM',1,$p0,$p1,'PRIVATE_DECK_A','PRIVATE_DECK_B',$start,$end,$winner,'PRIVATE_HASH',$error,$mode,$a0,$a1,$season,0);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$p0", legacy ? "fhtarget" : "历史名称甲"); command.Parameters.AddWithValue("$p1", "历史名称乙");
            command.Parameters.AddWithValue("$start", endedAt.AddMinutes(-10).ToString("O")); command.Parameters.AddWithValue("$end", ended ? endedAt.ToString("O") : DBNull.Value);
            command.Parameters.AddWithValue("$winner", outcome == 0 ? DBNull.Value : outcome == 1 ? player : 1 - player);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value); command.Parameters.AddWithValue("$mode", mode);
            command.Parameters.AddWithValue("$a0", legacy ? DBNull.Value : player == 0 ? TargetId : ViewerId);
            command.Parameters.AddWithValue("$a1", legacy ? DBNull.Value : player == 1 ? TargetId : ViewerId);
            command.Parameters.AddWithValue("$season", Store.CaptureOperationsPolicy().Season.Id);
            await command.ExecuteNonQueryAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (_server is not null) { await _server.StopAsync(); await _server.DisposeAsync(); }
            if (Recorder is not null) await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", _oldHost);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
