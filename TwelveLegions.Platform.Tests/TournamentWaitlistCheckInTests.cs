using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TwelveLegions.Server;
using Xunit;
namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentWaitlistCheckInTests
{
    [Fact]
    public async Task WaitlistedPlayerCanCheckInAndLockOwnDeckWithoutChangingSeat()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var first = f.Store.Register("wa", "password-123");
        var waiting = f.Store.Register("wb", "password-123");
        Assert.True(first.Success, first.Message);
        Assert.True(waiting.Success, waiting.Message);
        var ctx = new L12AdminAuditContext("waitlist-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var t = f.Store.CreateTournament(host,
            new L12TournamentCreatePayload("候补签到", "single", "public", 2, DateTimeOffset.UtcNow.AddHours(1),
                "现行规则", "", "after", "none", "", 50, 5, DisasterCardIds: [], CardRestrictions: []), ctx, true);
        foreach (var actor in new[] { host, first.Account!, waiting.Account! })
            t = f.Store.RegisterTournament(actor, t.Id, new(), t.Version, ctx, true);
        var deck = f.Store.Decks(waiting.Account!.Id)[0];
        var payload = new L12TournamentPreCheckInPayload(deck.Name, "", deck.Id);
        var preview = f.Store.PreCheckInTournament(waiting.Account, t.Id, payload, t.Version, ctx, false);
        Assert.NotNull(preview.Participants.Single(p => p.AccountId == waiting.Account.Id).TournamentCheckedInAt);
        Assert.Null(f.Store.Tournament(waiting.Account, t.Id)!.Participants.Single(p => p.AccountId == waiting.Account.Id).TournamentCheckedInAt);
        var saved = f.Store.PreCheckInTournament(waiting.Account, t.Id, payload, t.Version, ctx, true);
        var participant = saved.Participants.Single(p => p.AccountId == waiting.Account.Id);
        Assert.True(participant.Waitlisted);
        Assert.Equal(1, participant.WaitlistPosition);
        Assert.NotNull(participant.TournamentCheckedInAt);
        Assert.NotNull(participant.Deck?.LockedAt);
        var repeated = f.Store.PreCheckInTournament(waiting.Account, t.Id, payload, saved.Version, ctx, true);
        Assert.Equal(saved.Version, repeated.Version);
        Assert.Throws<L12TournamentVersionConflictException>(() => f.Store.PreCheckInTournament(waiting.Account, t.Id, payload, t.Version, ctx, true));
        Assert.Throws<KeyNotFoundException>(() => f.Store.PreCheckInTournament(first.Account!, t.Id, payload, saved.Version, ctx, true));
    }
    [Fact]
    public async Task HttpWaitlistCheckInIsAuthenticatedIdempotentAndPrivate()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var first = f.Store.Register("wc", "password-123");
        var waiting = f.Store.Register("wd", "password-123");
        Assert.True(first.Success, first.Message);
        Assert.True(waiting.Success, waiting.Message);
        var ctx = new L12AdminAuditContext("wait-http", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var t = f.Store.CreateTournament(host, new L12TournamentCreatePayload("候补接口", "single", "public", 2,
            DateTimeOffset.UtcNow.AddHours(1), "现行规则", "", "after", "none", "", 50, 5,
            DisasterCardIds: [], CardRestrictions: []), ctx, true);
        foreach (var actor in new[] { host, first.Account!, waiting.Account! })
            t = f.Store.RegisterTournament(actor, t.Id, new(), t.Version, ctx, true);
        var deck = f.Store.Decks(waiting.Account!.Id)[0];
        var path = $"/api/tournaments/{t.Id}/pre-check-in";
        var body = new TournamentPreCheckInRequest(deck.Name, "", deck.Id, "wait-http-commit", t.Version);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        using var anonymous = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", waiting.Token);
        var injected = false;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-tournament-read-index-build") return;
            injected = true;
            f.Store.StorageFailureInjector = null;
            throw new IOException("synthetic failed waitlist save");
        };
        using var failed = await client.PostAsJsonAsync(path, new TournamentPreCheckInRequest(deck.Name, "", deck.Id, "wait-http-fail", t.Version));
        f.Store.StorageFailureInjector = null;
        Assert.True(injected);
        Assert.False(failed.IsSuccessStatusCode);
        Assert.Null(f.Store.Tournament(waiting.Account, t.Id)!.Participants.Single(p => p.AccountId == waiting.Account.Id).TournamentCheckedInAt);
        using var success = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var saved = f.Store.Tournament(waiting.Account, t.Id)!;
        using var replay = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(saved.Version, f.Store.Tournament(waiting.Account, t.Id)!.Version);
        var otherView = f.Store.Tournament(first.Account!, t.Id)!;
        Assert.Null(otherView.Participants.Single(p => p.AccountId == waiting.Account.Id).Deck);
    }
}