using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentStartCheckSnapshotTests
{
    [Fact]
    public async Task HttpCheckReturnsSameVersionCountsAndConfiguredFormatOnlyToOrganizer()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var player = f.Store.Register("pa", "password-123");
        var wait = f.Store.Register("pb", "password-123");
        var outsider = f.Store.Register("pc", "password-123");
        Assert.True(player.Success, player.Message); Assert.True(wait.Success, wait.Message); Assert.True(outsider.Success, outsider.Message);
        var ctx = new L12AdminAuditContext("check-snapshot", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var t = f.Store.CreateTournament(host, new L12TournamentCreatePayload("预检数据", "swiss-cut", "public", 2,
            DateTimeOffset.UtcNow.AddHours(1), "现行规则", "", "after", "none", "", 50, 5,
            SwissRounds: 2, CutSize: 2, DisasterCardIds: [], CardRestrictions: []), ctx, true);
        foreach (var a in new[] { host, player.Account!, wait.Account! }) t = f.Store.RegisterTournament(a, t.Id, new(), t.Version, ctx, true);
        foreach (var a in new[] { host, wait.Account! })
        {
            var d = f.Store.Decks(a.Id)[0];
            t = f.Store.PreCheckInTournament(a, t.Id, new L12TournamentPreCheckInPayload(d.Name, "", d.Id), t.Version, ctx, true);
        }
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        var path = $"/api/tournaments/{t.Id}/start-check";
        using var anonymous = await client.GetAsync(path); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.Token);
        using var denied = await client.GetAsync(path); Assert.False(denied.IsSuccessStatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = json.RootElement;
        Assert.Equal(t.Version, root.GetProperty("tournamentVersion").GetInt64());
        Assert.Equal(1, root.GetProperty("eligiblePlayers").GetInt32());
        Assert.Equal(1, root.GetProperty("checkedInWaitlistedPlayers").GetInt32());
        Assert.Equal("swiss-cut", root.GetProperty("format").GetString());
        Assert.Equal(2, root.GetProperty("swissRounds").GetInt32());
        Assert.Equal(2, root.GetProperty("cutSize").GetInt32());
        Assert.True(root.GetProperty("canStart").GetBoolean());
        Assert.Equal(2, root.GetProperty("projectedEligiblePlayers").GetInt32());
        Assert.Equal(1, root.GetProperty("promotedWaitlistedPlayers").GetInt32());
        Assert.DoesNotContain(wait.Account!.Id, root.ToString(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task SingleEliminationHasNoSwissOrCutAndDroppedWaitlistDoesNotCount()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var first = f.Store.Register("pd", "password-123");
        var waiting = f.Store.Register("pe", "password-123");
        Assert.True(first.Success, first.Message); Assert.True(waiting.Success, waiting.Message);
        var ctx = new L12AdminAuditContext("check-single", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var t = f.Store.CreateTournament(host, new L12TournamentCreatePayload("单败预检", "single", "public", 2,
            DateTimeOffset.UtcNow.AddHours(1), "现行规则", "", "after", "none", "", 50, 5,
            DisasterCardIds: [], CardRestrictions: []), ctx, true);
        foreach (var a in new[] { host, first.Account!, waiting.Account! }) t = f.Store.RegisterTournament(a, t.Id, new(), t.Version, ctx, true);
        var deck = f.Store.Decks(waiting.Account!.Id)[0];
        t = f.Store.PreCheckInTournament(waiting.Account, t.Id, new L12TournamentPreCheckInPayload(deck.Name, "", deck.Id), t.Version, ctx, true);
        var before = f.Store.TournamentStartCheck(host, t.Id);
        Assert.Equal(1, before.CheckedInWaitlistedPlayers);
        t = f.Store.DropTournament(waiting.Account, t.Id, t.Version, ctx, true);
        var check = f.Store.TournamentStartCheck(host, t.Id);
        Assert.Equal(t.Version, check.TournamentVersion);
        Assert.Equal(0, check.CheckedInWaitlistedPlayers);
        Assert.Equal("single", check.Format);
        Assert.Null(check.SwissRounds);
        Assert.Null(check.CutSize);
        Assert.False(check.CanStart);
    }
}
