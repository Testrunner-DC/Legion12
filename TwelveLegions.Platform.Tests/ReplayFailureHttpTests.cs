using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ReplayFailureHttpTests
{
    [Theory]
    [InlineData("player")]
    [InlineData("admin-detail")]
    [InlineData("admin-page")]
    public async Task AuthorizedBadReplayFailsClosedWithoutTurningHealthyServiceInto500(string route)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(0, 0);
        var command = fixture.PassCommand();
        var result = fixture.Engine.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        await fixture.AppendAsync(1, command, result);
        fixture.Engine.ConcludeByAuthority(0, "synthetic replay HTTP acceptance");
        await fixture.Recorder.AppendAuthorityAsync(fixture.Engine, 2, "synthetic replay HTTP acceptance");
        await fixture.Recorder.CompleteAsync(fixture.Engine);
        var healthyBeforeDamage = Assert.IsType<L12MatchDetail>(
            await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId));
        Assert.Equal(2, healthyBeforeDamage.Commands.Count);
        Assert.Equal(fixture.Engine.ComputeStateHash(), healthyBeforeDamage.Commands[^1].StateHash);

        var platformPath = Path.Combine(Path.GetTempPath(), "l12-replay-http", Guid.NewGuid().ToString("N"), "platform.json");
        Directory.CreateDirectory(Path.GetDirectoryName(platformPath)!);
        var platform = new L12PlatformStore(platformPath, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        var first = platform.Register("clockfirst", "ClockTest-123!").Account!;
        var other = platform.Register("clockother", "ClockTest-123!").Account!;
        var outsider = platform.Register("clockthird", "ClockTest-123!").Account!;
        var matchId = fixture.Engine.State.MatchId;
        var connectionString = (string)typeof(MatchRecorder).GetField("_connectionString",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fixture.Recorder)!;
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            var arrange = connection.CreateCommand();
            arrange.CommandText = """
                UPDATE matches SET account_0=$first,account_1=$other WHERE match_id=$match;
                UPDATE match_events SET state_hash='SYNTHETIC_PRIVATE_HASH_DO_NOT_EXPOSE'
                WHERE match_id=$match AND sequence=1;
                """;
            arrange.Parameters.AddWithValue("$first", first.Id);
            arrange.Parameters.AddWithValue("$other", other.Id);
            arrange.Parameters.AddWithValue("$match", matchId);
            await arrange.ExecuteNonQueryAsync();
        }

        var manager = new L12RoomManager(fixture.Catalog, fixture.Recorder, platform);
        await using var server = new L12WebSocketServer(manager, fixture.Recorder, platform, fixture.Catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var ownerToken = platform.Login(first.Username, "ClockTest-123!").Token!;
            var outsiderToken = platform.Login(outsider.Username, "ClockTest-123!").Token!;
            var adminToken = platform.Login("Admin", "L12master").Token!;
            var path = route switch
            {
                "player" => $"/api/matches/{matchId}",
                "admin-detail" => $"/api/admin/matches/{matchId}?includeReplay=true",
                _ => $"/api/admin/matches/{matchId}/replay?limit=10",
            };
            using var request = Authorized(path, route == "player" ? ownerToken : adminToken);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
            Assert.Equal("replay_incompatible", error!.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.CorrelationId));
            Assert.DoesNotContain("SYNTHETIC_PRIVATE_HASH_DO_NOT_EXPOSE", body);
            Assert.DoesNotContain("synthetic-first", body);
            Assert.DoesNotContain("synthetic-second", body);
            Assert.DoesNotContain("resolvePrompt", body);
            Assert.DoesNotContain("state-hash", body);
            Assert.DoesNotContain("sequence", body, StringComparison.OrdinalIgnoreCase);

            using (var denied = Authorized($"/api/matches/{matchId}", outsiderToken))
            using (var deniedResponse = await client.SendAsync(denied))
                Assert.Equal(HttpStatusCode.NotFound, deniedResponse.StatusCode);
            using (var denied = Authorized($"/api/admin/matches/{matchId}/replay?limit=10", ownerToken))
            using (var deniedResponse = await client.SendAsync(denied))
                Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
            using (var healthy = await client.GetAsync("/health"))
                Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            using (var summary = Authorized($"/api/admin/matches/{matchId}", adminToken))
            using (var summaryResponse = await client.SendAsync(summary))
                Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static HttpRequestMessage Authorized(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
