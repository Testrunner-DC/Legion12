using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// Match-local ruling authority is separate from tournament-wide management.
[Collection("Platform environment")]
public sealed class TournamentSelfRulingHttpTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task MatchParticipantsCannotRuleTheirOwnTableButStaffCanRuleOtherTables()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        Assert.True(f.Client.BaseAddress!.IsLoopback);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;

        var organizer = await Register(f.Client, "srorg", ct);
        var participantReferee = await Register(f.Client, "srrefp", ct);
        var elevatedParticipant = await Register(f.Client, "srglob", ct);
        var fourth = await Register(f.Client, "srfour", ct);
        var independentReferee = await Register(f.Client, "srindref", ct);
        var players = new[] { organizer, participantReferee, elevatedParticipant, fourth };

        foreach (var referee in new[] { participantReferee, independentReferee })
        {
            await Expect(f.Client, organizer, HttpMethod.Post, "/api/friends/requests",
                new FriendRequest(referee.Account.Id), HttpStatusCode.OK, ct);
            await Expect(f.Client, referee, HttpMethod.Post,
                $"/api/friends/requests/{organizer.Account.Id}/resolve",
                new FriendResolveRequest(true), HttpStatusCode.OK, ct);
        }

        var list = await Read<L12TournamentListView>(f.Client, organizer, "/api/tournaments", ct);
        var created = await Write(f.Client, organizer, "/api/tournaments",
            new TournamentCreateRequest(new L12TournamentCreatePayload(
                    "合成桌次回避HTTP单淘汰", "single", "public", 4,
                    null, "S01/S02", "synthetic match-local recusal", "after", "season",
                    string.Empty, 50, 5,
                    [participantReferee.Account.Id, independentReferee.Account.Id],
                    RegistrationVisibility: "public"),
                "sr-create", list.PlatformVersion, false, "synthetic create"), ct);
        var path = $"/api/tournaments/{created.Id}";

        var tournament = created;
        foreach (var player in players.Skip(1))
        {
            tournament = await Write(f.Client, player, path + "/registrations",
                new TournamentRegistrationRequest(null, null, "sr-register-" + player.Account.Username,
                    tournament.Version, false, "synthetic registration"), ct);
        }

        foreach (var player in players)
        {
            var page = await Read<L12PrivateDeckSummaryPage>(f.Client, player,
                "/api/decks/summaries?page=1&pageSize=1&sort=name&legal=true", ct);
            var deck = Assert.Single(page.Items);
            Assert.True(deck.Legal);
            tournament = await Write(f.Client, player, path + "/pre-check-in",
                new TournamentPreCheckInRequest(string.Empty, string.Empty, deck.Id,
                    "sr-lock-" + player.Account.Username, tournament.Version, false, "synthetic lock"), ct);
        }

        tournament = await Write(f.Client, organizer, path + "/start",
            new TournamentActionRequest("sr-start", tournament.Version, false, "synthetic start"), ct);
        foreach (var player in players)
        {
            tournament = await Write(f.Client, player, path + "/rounds/1/check-in",
                new TournamentCheckInRequest(null, true, "sr-ready-" + player.Account.Username,
                    tournament.Version), ct);
        }
        tournament = await Write(f.Client, organizer, path + "/rounds/1/start",
            new TournamentActionRequest("sr-round-start", tournament.Version, false, "synthetic round start"), ct);

        // Elevate an already-enrolled ordinary player only after setup. Their
        // refreshed token proves global platform permission cannot bypass the
        // exact-match participant guard.
        var bootstrapAdmin = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        Assert.True(f.Store.SetRole(bootstrapAdmin, elevatedParticipant.Account.Id, "admin",
            new L12AdminAuditContext("sr-elevate", "admin.accounts.roles.write")));
        var elevatedLogin = f.Store.Login(elevatedParticipant.Account.Username, "password-123");
        Assert.True(elevatedLogin.Success);
        elevatedParticipant = new Identity(elevatedLogin.Account!, elevatedLogin.Token!);
        Assert.True(L12Authorization.HasPermission(elevatedParticipant.Account,
            L12Permission.TournamentRulingsWrite));
        Assert.DoesNotContain(tournament.Referees,
            item => item.AccountId == elevatedParticipant.Account.Id);

        var matches = tournament.Rounds.Single(round => round.Number == 1).Matches.ToArray();
        Assert.Equal(2, matches.Length);
        var organizerMatch = MatchFor(matches, organizer.Account.Id);
        var refereeMatch = MatchFor(matches, participantReferee.Account.Id);
        var elevatedMatch = MatchFor(matches, elevatedParticipant.Account.Id);
        var otherMatch = matches.Single(match => match.Id != organizerMatch.Id);
        var organizerOpponentId = Opponent(organizerMatch, organizer.Account.Id);
        var beforeRejected = await GetTournament(f.Client, organizer, path, ct);
        var beforeRejectedState = MatchState(beforeRejected);
        var securityAuditBefore = f.Store.AdminAudit("security").Count;

        // Both preview and apply paths must reject an organizer on their exact
        // table; penalty and result are independently covered.
        await Expect(f.Client, organizer, HttpMethod.Post,
            path + $"/matches/{organizerMatch.Id}/rulings",
            new TournamentRulingRequest("penalty", organizer.Account.Id, "warning",
                "self penalty preview", "sr-org-penalty-preview", tournament.Version, true),
            HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, organizer, HttpMethod.Post,
            path + $"/matches/{organizerMatch.Id}/rulings",
            new TournamentRulingRequest("result", null, "player-a",
                "self result apply", "sr-org-result-apply", tournament.Version, false),
            HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, organizer, HttpMethod.Post,
            path + $"/matches/{organizerMatch.Id}/rulings",
            new TournamentRulingRequest("no-show", organizerOpponentId, "no-show-b",
                "self no-show apply", "sr-org-no-show", tournament.Version, false),
            HttpStatusCode.Forbidden, ct);

        // A configured referee who is a participant and a platform-global
        // administrator who is not configured staff are both match-local only.
        await Expect(f.Client, participantReferee, HttpMethod.Post,
            path + $"/matches/{refereeMatch.Id}/rulings",
            new TournamentRulingRequest("penalty", participantReferee.Account.Id, "warning",
                "self referee penalty", "sr-ref-self", tournament.Version),
            HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, elevatedParticipant, HttpMethod.Post,
            path + $"/matches/{elevatedMatch.Id}/rulings",
            new TournamentRulingRequest("result", null, "player-a",
                "global permission self result", "sr-global-self", tournament.Version),
            HttpStatusCode.Forbidden, ct);

        // Participant removal is also a direct result producer. Removing the
        // organizer's opponent would award the organizer's own unfinished table.
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/participants/remove",
            new TournamentRemoveParticipantRequest(organizerOpponentId, false, "self-table removal preview",
                "sr-remove-own-preview", tournament.Version, true), HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/participants/remove",
            new TournamentRemoveParticipantRequest(organizerOpponentId, false, "self-table removal apply",
                "sr-remove-own-apply", tournament.Version, false), HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/participants/remove",
            new TournamentRemoveParticipantRequest(organizer.Account.Id, true, "own participant removal apply",
                "sr-remove-self-apply", tournament.Version, false), HttpStatusCode.Forbidden, ct);

        var afterRejected = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(beforeRejectedState, MatchState(afterRejected));
        Assert.True(f.Store.AdminAudit("security").Count >= securityAuditBefore + 8);

        // Participation elsewhere in the tournament is not a blanket ban: the
        // organizer may issue a penalty on the other table.
        var otherTarget = otherMatch.PlayerAAccountId;
        tournament = await Write(f.Client, organizer,
            path + $"/matches/{otherMatch.Id}/rulings",
            new TournamentRulingRequest("penalty", otherTarget, "warning",
                "independent other-table penalty", "sr-other-table-penalty", afterRejected.Version), ct);
        Assert.Single(MatchFor(tournament.Rounds[0].Matches, otherTarget).Rulings);

        // The implicit-result producer follows the same exact-table rule: a
        // participating organizer may still remove somebody from another table.
        tournament = await Write(f.Client, organizer, path + "/participants/remove",
            new TournamentRemoveParticipantRequest(otherTarget, false, "independent other-table removal",
                "sr-remove-other", tournament.Version, false), ct);
        Assert.True(tournament.Participants.Single(item => item.AccountId == otherTarget).Removed);
        Assert.Equal("completed", MatchById(tournament, otherMatch.Id).Status);

        // An independent referee's dry run returns the projected mutation but
        // cannot persist it. Apply then persists once; exact replay stays once.
        var current = await GetTournament(f.Client, organizer, path, ct);
        var currentState = MatchState(current);
        var independentBody = new TournamentRulingRequest("result", null, "player-a",
            "independent result", "sr-independent-preview", current.Version, true);
        var preview = await Write(f.Client, independentReferee,
            path + $"/matches/{organizerMatch.Id}/rulings", independentBody, ct);
        Assert.Equal(current.Version + 1, preview.Version);
        Assert.Equal("completed", MatchById(preview, organizerMatch.Id).Status);
        var afterPreview = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(currentState, MatchState(afterPreview));

        var applyBody = independentBody with
        {
            IdempotencyKey = "sr-independent-apply",
            ExpectedVersion = afterPreview.Version,
            DryRun = false
        };
        tournament = await Write(f.Client, independentReferee,
            path + $"/matches/{organizerMatch.Id}/rulings", applyBody, ct);
        var afterApplyVersion = tournament.Version;
        Assert.Equal("completed", MatchById(tournament, organizerMatch.Id).Status);
        Assert.Single(MatchById(tournament, organizerMatch.Id).Rulings);
        using (var replay = await Send(f.Client, independentReferee, HttpMethod.Post,
                   path + $"/matches/{organizerMatch.Id}/rulings", applyBody, ct))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("X-Idempotent-Replay")));
        }
        var afterReplay = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(afterApplyVersion, afterReplay.Version);
        Assert.Single(MatchById(afterReplay, organizerMatch.Id).Rulings);
    }

    private static L12TournamentMatchView MatchFor(IEnumerable<L12TournamentMatchView> matches, string accountId)
        => matches.Single(match => match.PlayerAAccountId == accountId || match.PlayerBAccountId == accountId);

    private static L12TournamentMatchView MatchById(L12TournamentView tournament, string matchId)
        => tournament.Rounds.SelectMany(round => round.Matches).Single(match => match.Id == matchId);

    private static string Opponent(L12TournamentMatchView match, string accountId)
        => match.PlayerAAccountId == accountId ? match.PlayerBAccountId! : match.PlayerAAccountId;

    private static string MatchState(L12TournamentView tournament)
        => JsonSerializer.Serialize(new
        {
            tournament.Version,
            Matches = tournament.Rounds.SelectMany(round => round.Matches).Select(match => new
            {
                match.Id,
                match.Status,
                match.Result,
                Rulings = match.Rulings.Select(ruling => new
                {
                    ruling.Id,
                    ruling.Kind,
                    ruling.TargetAccountId,
                    ruling.Decision,
                    ruling.ActorId,
                    ruling.Reason
                }).ToArray(),
                Events = match.Events.Select(item => new
                {
                    item.Id,
                    item.Kind,
                    item.Result,
                    item.ActorId,
                    item.Detail
                }).ToArray()
            }).ToArray()
        }, Json);

    private sealed record Identity(L12AccountView Account, string Token);
    private sealed record RegistrationResponse(string Message, L12AccountView Account, string Token);

    private static async Task<Identity> Register(HttpClient client, string name, CancellationToken ct)
    {
        using var response = await Send(client, null, HttpMethod.Post, "/api/auth/register",
            new AuthRequest(name, "password-123"), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registration = (await response.Content.ReadFromJsonAsync<RegistrationResponse>(Json, ct))!;
        return new(registration.Account, registration.Token);
    }

    private static async Task<T> Read<T>(HttpClient client, Identity actor, string path, CancellationToken ct)
    {
        using var response = await Send(client, actor, HttpMethod.Get, path, null, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private static async Task<L12TournamentView> GetTournament(HttpClient client, Identity actor,
        string path, CancellationToken ct)
    {
        using var response = await Send(client, actor, HttpMethod.Get, path, null, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<L12TournamentView>(Json, ct))!;
        Assert.Equal($"\"{result.Version}\"", response.Headers.ETag!.Tag);
        return result;
    }

    private static async Task<L12TournamentView> Write(HttpClient client, Identity actor,
        string path, object body, CancellationToken ct)
    {
        using var response = await Send(client, actor, HttpMethod.Post, path, body, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<L12TournamentView>(Json, ct))!;
    }

    private static async Task Expect(HttpClient client, Identity actor, HttpMethod method, string path,
        object body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await Send(client, actor, method, path, body, ct);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, Identity? actor,
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        Assert.True(client.BaseAddress!.IsLoopback);
        using var request = new HttpRequestMessage(method, path);
        if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actor.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, ct);
    }
}
