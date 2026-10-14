using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// Local HTTP linkage of existing tournament producers and consumers.
// Direct ruling recusal has a separate named HTTP regression; this lifecycle
// retains its existing independent referee for all recorded results.
[Collection("Platform environment")]
public sealed class TournamentHttpLifecycleTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FourOrdinaryPlayersCompleteTwoRoundSingleThroughRealHttp()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        Assert.True(f.Client.BaseAddress!.IsLoopback);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        // Normal registrations only. Never consume the fixture's AdminToken.
        var organizer = await Register(f.Client, "thorg", ct);
        var players = new[]
        {
            organizer,
            await Register(f.Client, "thp1", ct),
            await Register(f.Client, "thp2", ct),
            await Register(f.Client, "thp3", ct),
        };
        var referee = await Register(f.Client, "thref", ct);
        var outsider = await Register(f.Client, "thout", ct);
        foreach (var identity in players.Append(referee).Append(outsider))
        {
            Assert.Equal("player", identity.Account.Role);
            Assert.False(L12Authorization.HasPermission(identity.Account, L12Permission.TournamentsManage));
            Assert.False(L12Authorization.HasPermission(identity.Account, L12Permission.TournamentRulingsWrite));
        }
        await Expect(f.Client, organizer, HttpMethod.Post, "/api/friends/requests",
            new FriendRequest(referee.Account.Id), HttpStatusCode.OK, ct);
        await Expect(f.Client, referee, HttpMethod.Post,
            $"/api/friends/requests/{organizer.Account.Id}/resolve", new FriendResolveRequest(true), HttpStatusCode.OK, ct);

        // Create is the one platform-version operation; all later writes use
        // the actual returned/read tournament Version, not Store.Version.
        var list = await Read<L12TournamentListView>(f.Client, organizer, "/api/tournaments", ct);
        var payload = new L12TournamentCreatePayload("合成四人HTTP单淘汰", "single", "public", 4,
            null, "S01/S02", "synthetic HTTP lifecycle", "after", "season", string.Empty, 50, 5,
            [referee.Account.Id], RegistrationVisibility: "public");
        var tournament = await Write(f.Client, organizer, "/api/tournaments",
            new TournamentCreateRequest(payload, "th-create", list.PlatformVersion, false, "synthetic create"), ct);
        var path = $"/api/tournaments/{tournament.Id}";
        Assert.Equal(organizer.Account.Id, tournament.OrganizerAccountId);
        Assert.Single(tournament.Participants); // Creator is enrolled by the actual product.
        Assert.Equal(referee.Account.Id, Assert.Single(tournament.Referees).AccountId);

        foreach (var player in players.Skip(1))
            tournament = await Write(f.Client, player, path + "/registrations",
                new TournamentRegistrationRequest(null, null, "th-register-" + player.Account.Username,
                    tournament.Version, false, "synthetic registration"), ct);
        Assert.Equal(4, tournament.Participants.Count);
        Assert.All(tournament.Participants, p => Assert.Null(p.TournamentCheckedInAt));

        var decks = new Dictionary<string, (L12PrivateDeckSummaryView Summary, L12AccountDeckView Body)>(StringComparer.Ordinal);
        foreach (var player in players)
        {
            // Product-seeded official preset copies, filtered by the real
            // legality consumer. Missing legal presets fail; no handcrafted fallback.
            var page = await Read<L12PrivateDeckSummaryPage>(f.Client, player,
                "/api/decks/summaries?page=1&pageSize=1&sort=name&legal=true", ct);
            var summary = Assert.Single(page.Items);
            Assert.True(summary.Legal);
            Assert.False(string.IsNullOrWhiteSpace(summary.Id));
            var body = await Read<L12AccountDeckView>(f.Client, player,
                $"/api/decks/by-id/{summary.Id}?expectedRevision={summary.Revision}", ct);
            Assert.Equal(summary.Id, body.Id);
            Assert.Equal(summary.Revision, body.Revision);
            decks.Add(player.Account.Id, (summary, body));
        }
        Assert.Equal(4, decks.Values.Select(d => d.Summary.Id).Distinct(StringComparer.Ordinal).Count());
        var beforeForeign = tournament.Version;
        await Expect(f.Client, players[1], HttpMethod.Post, path + "/pre-check-in",
            new TournamentPreCheckInRequest("not-an-owned-name", string.Empty, decks[organizer.Account.Id].Summary.Id,
                "th-foreign-deck", tournament.Version), HttpStatusCode.NotFound, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(beforeForeign, tournament.Version);
        Assert.Null(tournament.Participants.Single(p => p.AccountId == players[1].Account.Id).TournamentCheckedInAt);
        foreach (var player in players)
        {
            var selected = decks[player.Account.Id];
            // Deliberately different name proves the stable owned ID route is
            // used instead of a name fallback; no foreign/private account data.
            tournament = await Write(f.Client, player, path + "/pre-check-in",
                new TournamentPreCheckInRequest("not-the-saved-name", string.Empty, selected.Summary.Id,
                    "th-lock-" + player.Account.Username, tournament.Version), ct);
            var locked = tournament.Participants.Single(p => p.AccountId == player.Account.Id);
            Assert.NotNull(locked.TournamentCheckedInAt);
            Assert.NotNull(locked.Deck?.LockedAt);
            Assert.Equal(selected.Body.Name, locked.Deck!.Name);
            Assert.Equal(selected.Body.MasterId, locked.Deck.MasterId);
            Assert.Equal(selected.Body.CardIds, locked.Deck.CardIds);
            Assert.Equal(selected.Body.MoraleIds, locked.Deck.MoraleIds);
            Assert.Equal(selected.Body.SpecialIds, locked.Deck.SpecialIds);
        }
        tournament = await GetTournament(f.Client, organizer, path, ct);
        var lockedVersion = tournament.Version;
        var organizerDeck = tournament.Participants.Single(p => p.AccountId == organizer.Account.Id).Deck!;
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/pre-check-in",
            new TournamentPreCheckInRequest("different-label", string.Empty, decks[players[1].Account.Id].Summary.Id,
                "th-replace-locked", tournament.Version), HttpStatusCode.Conflict, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(lockedVersion, tournament.Version);
        var stillLocked = tournament.Participants.Single(p => p.AccountId == organizer.Account.Id).Deck!;
        Assert.Equal(organizerDeck.Hash, stillLocked.Hash);
        Assert.Equal(organizerDeck.LockedAt, stillLocked.LockedAt);

        await Expect(f.Client, organizer, HttpMethod.Post, path + "/start",
            new TournamentActionRequest("th-missing-version"), (HttpStatusCode)428, ct);
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/start",
            new TournamentActionRequest(null, tournament.Version), HttpStatusCode.BadRequest, ct);
        await Expect(f.Client, outsider, HttpMethod.Post, path + "/start",
            new TournamentActionRequest("th-outsider-start", tournament.Version), HttpStatusCode.Forbidden, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(lockedVersion, tournament.Version);
        var startCheck = await Read<L12TournamentStartCheckView>(f.Client, organizer, path + "/start-check", ct);
        Assert.True(startCheck.CanStart);
        tournament = await Write(f.Client, organizer, path + "/start",
            new TournamentActionRequest("th-start", tournament.Version, false, "synthetic start"), ct);
        Assert.Equal("running", tournament.Status);
        Assert.Equal(2, Assert.Single(tournament.Rounds).Matches.Count);
        Assert.All(tournament.Rounds[0].Matches, m => Assert.Equal(tournament.Rules.Hash, m.RulesHash));

        var identities = players.ToDictionary(p => p.Account.Id, StringComparer.Ordinal);
        foreach (var player in players)
            tournament = await Write(f.Client, player, path + "/rounds/1/check-in",
                new TournamentCheckInRequest(null, true, "th-r1-ready-" + player.Account.Username,
                    tournament.Version), ct);
        tournament = await Write(f.Client, organizer, path + "/rounds/1/start",
            new TournamentActionRequest("th-r1-start", tournament.Version, false, "synthetic round start"), ct);
        Assert.All(tournament.Rounds[0].Matches, m => Assert.True(m.ReadyA && m.ReadyB));

        // Assignment/resolve remain match-local; a participating organizer
        // can assign the case to the independent referee, never to themselves.
        var ownMatch = tournament.Rounds[0].Matches.Single(m =>
            m.PlayerAAccountId == organizer.Account.Id || m.PlayerBAccountId == organizer.Account.Id);
        var opponent = identities[ownMatch.PlayerAAccountId == organizer.Account.Id
            ? ownMatch.PlayerBAccountId! : ownMatch.PlayerAAccountId];
        tournament = await Write(f.Client, opponent, path + "/judge-cases",
            new TournamentJudgeCaseCreateRequest(ownMatch.Id, "rules", "normal", "synthetic order question",
                "th-call-judge", tournament.Version), ct);
        var caseId = Assert.Single(tournament.JudgeCases).Id;
        var caseVersion = tournament.Version;
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/judge-cases/assign",
            new TournamentJudgeCaseAssignRequest(caseId, organizer.Account.Id, "self assignment",
                "th-self-assign", tournament.Version), HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/judge-cases/resolve",
            new TournamentJudgeCaseResolveRequest(caseId, "ruled", "self ruling", null, "conflict probe",
                "th-self-resolve", tournament.Version), HttpStatusCode.Forbidden, ct);
        await Expect(f.Client, outsider, HttpMethod.Post, path + $"/matches/{ownMatch.Id}/rulings",
            new TournamentRulingRequest("result", null, "player-a", "outsider probe",
                "th-outsider-ruling", tournament.Version), HttpStatusCode.Forbidden, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(caseVersion, tournament.Version);
        Assert.False(Assert.Single(tournament.JudgeCases).CanManage);
        Assert.Null(Assert.Single(tournament.JudgeCases).AssigneeAccountId);
        Assert.Empty(tournament.Rounds[0].Matches.Single(m => m.Id == ownMatch.Id).Rulings);
        tournament = await Write(f.Client, organizer, path + "/judge-cases/assign",
            new TournamentJudgeCaseAssignRequest(caseId, referee.Account.Id, "independent referee",
                "th-assign-referee", tournament.Version), ct);
        var refereeView = await GetTournament(f.Client, referee, path, ct);
        Assert.True(Assert.Single(refereeView.JudgeCases).CanManage);
        tournament = await Write(f.Client, referee, path + "/judge-cases/resolve",
            new TournamentJudgeCaseResolveRequest(caseId, "ruled", "continue current step", "synthetic-staff-note",
                "independent resolution", "th-resolve-referee", tournament.Version), ct);
        Assert.Equal("ruled", Assert.Single(tournament.JudgeCases).Status);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Null(Assert.Single(tournament.JudgeCases).StaffNote); // Conflicted organizer projection.

        var semifinals = tournament.Rounds[0].Matches.ToArray();
        var staleVersion = tournament.Version;
        var firstBody = new TournamentRulingRequest("result", null, "player-a", "synthetic semifinal",
            "th-r1-result-1", tournament.Version);
        tournament = await Write(f.Client, referee, path + $"/matches/{semifinals[0].Id}/rulings", firstBody, ct);
        using (var replay = await Send(f.Client, referee, HttpMethod.Post,
            path + $"/matches/{semifinals[0].Id}/rulings", firstBody, ct))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("X-Idempotent-Replay")));
        }
        var versionAfterFirst = tournament.Version;
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(versionAfterFirst, tournament.Version);
        Assert.Single(tournament.Rounds[0].Matches.Single(m => m.Id == semifinals[0].Id).Rulings);
        await Expect(f.Client, referee, HttpMethod.Post, path + $"/matches/{semifinals[1].Id}/rulings",
            new TournamentRulingRequest("result", null, "player-a", "stale probe", "th-stale-result", staleVersion),
            HttpStatusCode.Conflict, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(versionAfterFirst, tournament.Version);
        Assert.Empty(tournament.Rounds[0].Matches.Single(m => m.Id == semifinals[1].Id).Rulings);
        tournament = await Write(f.Client, referee, path + $"/matches/{semifinals[1].Id}/rulings",
            new TournamentRulingRequest("result", null, "player-a", "synthetic semifinal",
                "th-r1-result-2", tournament.Version), ct);
        Assert.Equal("completed", tournament.Rounds[0].Status);
        var semiVersion = tournament.Version;
        await Expect(f.Client, organizer, HttpMethod.Post, path + "/complete",
            new TournamentActionRequest("th-early-complete", tournament.Version), HttpStatusCode.Conflict, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(semiVersion, tournament.Version);
        tournament = await Write(f.Client, organizer, path + "/rounds",
            new TournamentActionRequest("th-final-round", tournament.Version, false, "synthetic final pairing"), ct);
        Assert.Equal(2, tournament.Rounds.Count);
        var final = Assert.Single(tournament.Rounds[1].Matches);
        Assert.Equal("elimination", tournament.Rounds[1].Stage);
        Assert.Equal(semifinals.Select(m => m.Id).OrderBy(id => id), final.SourceMatchIds.OrderBy(id => id));
        Assert.Equal(semifinals.Select(m => m.PlayerAAccountId).OrderBy(id => id),
            new[] { final.PlayerAAccountId, final.PlayerBAccountId! }.OrderBy(id => id));
        foreach (var id in new[] { final.PlayerAAccountId, final.PlayerBAccountId! })
            tournament = await Write(f.Client, identities[id], path + "/rounds/2/check-in",
                new TournamentCheckInRequest(null, true, "th-r2-ready-" + identities[id].Account.Username,
                    tournament.Version), ct);
        tournament = await Write(f.Client, organizer, path + "/rounds/2/start",
            new TournamentActionRequest("th-r2-start", tournament.Version, false, "synthetic final start"), ct);
        tournament = await Write(f.Client, referee, path + $"/matches/{final.Id}/rulings",
            new TournamentRulingRequest("result", null, "player-b", "synthetic final",
                "th-final-result", tournament.Version), ct);
        Assert.All(tournament.Rounds, r => Assert.Equal("completed", r.Status));
        tournament = await Write(f.Client, organizer, path + "/phase",
            new TournamentPhaseRequest("result-confirmation", "synthetic final review", "th-confirm",
                tournament.Version), ct);
        Assert.Equal("result-confirmation", tournament.Phase);
        tournament = await Write(f.Client, organizer, path + "/complete",
            new TournamentActionRequest("th-complete", tournament.Version, false, "synthetic complete"), ct);
        Assert.Equal("completed", tournament.Status);
        Assert.Equal("completed", tournament.Phase);
        Assert.NotNull(tournament.CompletedAt);
        Assert.Equal(2, tournament.EliminationBracket.Count);
        var completedVersion = tournament.Version;
        await Expect(f.Client, referee, HttpMethod.Post, path + $"/matches/{final.Id}/rulings",
            new TournamentRulingRequest("penalty", final.PlayerAAccountId, "warning", "expired staff probe",
                "th-expired-staff", tournament.Version), HttpStatusCode.Forbidden, ct);
        tournament = await GetTournament(f.Client, organizer, path, ct);
        Assert.Equal(completedVersion, tournament.Version);

        using var publicResponse = await Send(f.Client, null, HttpMethod.Get,
            $"/api/public/tournaments/code/{tournament.Code}", null, ct);
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        using var publicJson = JsonDocument.Parse(await publicResponse.Content.ReadAsStringAsync(ct));
        Assert.False(publicJson.RootElement.TryGetProperty("judgeCases", out _));
        Assert.False(publicJson.RootElement.TryGetProperty("referees", out _));
        var published = publicJson.RootElement.Deserialize<L12PublicTournamentDetailView>(Json)!;
        Assert.Equal(tournament.Code, published.Code);
        Assert.Equal("completed", published.Status);
        Assert.Equal("completed", published.Phase);
        Assert.Equal(4, published.Participants.Count);
        Assert.Equal(2, published.Rounds.Count);
        Assert.Equal("player-b", Assert.Single(published.Rounds[1].Matches).Result);
        Assert.All(published.Rounds, r => Assert.Equal("completed", r.Status));
    }

    private sealed record Identity(L12AccountView Account, string Token);
    private sealed record RegistrationResponse(string Message, L12AccountView Account, string Token);

    private static async Task<Identity> Register(HttpClient client, string name, CancellationToken ct)
    {
        using var response = await Send(client, null, HttpMethod.Post, "/api/auth/register",
            new AuthRequest(name, "password-123"), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registration = (await response.Content.ReadFromJsonAsync<RegistrationResponse>(Json, ct))!;
        Assert.NotNull(registration.Account);
        Assert.False(string.IsNullOrWhiteSpace(registration.Token));
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
