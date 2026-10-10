using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class PublicTournamentBrowsingTests
{
    [Fact]
    public async Task AnonymousBrowseShowsOnlyPublicAllowlistedTournamentAndNeverDependsOnIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var publicTournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("公开赛事", "public"), Context("public-create"), true);
        var privateTournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("分享码赛事", "code"), Context("code-create"), true);
        var playerViewer = fixture.Store.Register("c3publicv01", "password-123");
        var refereeViewer = fixture.Store.Register("c3publicf01", "password-123");
        Assert.True(fixture.Store.SendFriendRequest(fixture.Organizer.Id, refereeViewer.Account!.Id).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(refereeViewer.Account.Id, fixture.Organizer.Id, true).Success);
        publicTournament = fixture.Store.SetTournamentStaff(fixture.Organizer, publicTournament.Id,
            new L12TournamentStaffPayload([refereeViewer.Account.Id]), publicTournament.Version,
            Context("public-referee"), true);
        var platformVersion = fixture.Store.Version;
        var publicVersion = publicTournament.Version;
        var storageHash = FileHash(fixture.PlatformPath);

        using var anonymous = await fixture.Client.GetAsync("/api/public/tournaments/summaries?page=1&pageSize=24");
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        AssertNoStore(anonymous);
        var anonymousText = await anonymous.Content.ReadAsStringAsync();
        using var anonymousJson = JsonDocument.Parse(anonymousText);
        var item = Assert.Single(anonymousJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(publicTournament.Code, item.GetProperty("code").GetString());
        Assert.DoesNotContain(privateTournament.Code, anonymousJson.RootElement.GetRawText(),
            StringComparison.Ordinal);
        AssertAllowlisted(item, "code", "name", "organizerName", "status", "format", "phase",
            "maxPlayers", "startAt", "roundMinutes", "checkInMinutes", "counts");

        foreach (var token in new[] { playerViewer.Token!, refereeViewer.Token!, fixture.AdminToken })
        {
            using var identityRequest = new HttpRequestMessage(HttpMethod.Get,
                "/api/public/tournaments/summaries?page=1&pageSize=24");
            identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var withIdentity = await fixture.Client.SendAsync(identityRequest);
            Assert.Equal(HttpStatusCode.OK, withIdentity.StatusCode);
            AssertNoStore(withIdentity);
            Assert.Equal(anonymousText, await withIdentity.Content.ReadAsStringAsync());
        }

        using var detail = await fixture.Client.GetAsync($"/api/public/tournaments/code/{publicTournament.Code}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertNoStore(detail);
        var detailText = await detail.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE ADMIN NOTE", detailText, StringComparison.Ordinal);
        using var detailJson = JsonDocument.Parse(detailText);
        AssertAllowlisted(detailJson.RootElement, "code", "name", "organizerName", "status", "format",
            "phase", "maxPlayers", "startAt", "roundMinutes", "checkInMinutes", "counts", "description",
            "registrationVisibility", "registrationOpen", "rules", "participants", "rounds", "finalStandings");
        AssertAllowlisted(detailJson.RootElement.GetProperty("rules"), "ruleset", "disasterMode", "banList",
            "disasterCardIds", "cardRestrictions", "deckVisibility", "swissRounds", "cutSize",
            "lateGraceMinutes", "timeControl");
        var restriction = Assert.Single(detailJson.RootElement.GetProperty("rules")
            .GetProperty("cardRestrictions").EnumerateArray());
        AssertAllowlisted(restriction, "cardId", "maxCopies", "masterId");
        AssertNoForbiddenProperties(detailJson.RootElement);

        using var hidden = await fixture.Client.GetAsync($"/api/public/tournaments/code/{privateTournament.Code}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        AssertNoStore(hidden);
        Assert.Equal(platformVersion, fixture.Store.Version);
        Assert.Equal(publicVersion, fixture.Store.Tournament(fixture.Organizer, publicTournament.Id)!.Version);
        Assert.Equal(storageHash, FileHash(fixture.PlatformPath));

        using var unauthenticatedPrivate = await fixture.Client.GetAsync($"/api/tournaments/code/{privateTournament.Code}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedPrivate.StatusCode);
        using var authenticatedPrivateRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/tournaments/code/{privateTournament.Code}");
        authenticatedPrivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        using var authenticatedPrivate = await fixture.Client.SendAsync(authenticatedPrivateRequest);
        Assert.Equal(HttpStatusCode.OK, authenticatedPrivate.StatusCode);

        using var rankings = await fixture.Client.GetAsync("/api/rankings/history?limit=1");
        Assert.Equal(HttpStatusCode.OK, rankings.StatusCode);
        AssertNoStore(rankings);
    }

    [Fact]
    public async Task StaffOnlyRegistrationNeverLeaksParticipantIdentityThroughPublicDetail()
    {
        await using var fixture = await Fixture.CreateAsync();
        var playerLogin = fixture.Store.Register("c3publicp01", "password-123");
        var player = playerLogin.Account!;
        var tournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("名单隐藏赛事", "public") with { RegistrationVisibility = "staff" },
            Context("staff-create"), true);
        tournament = fixture.Store.RegisterTournament(player, tournament.Id,
            new L12TournamentRegistrationPayload(), tournament.Version, Context("staff-register"), true);
        tournament = PreCheckIn(fixture.Store, fixture.Organizer, tournament, "staff-host-deck");
        tournament = PreCheckIn(fixture.Store, player, tournament, "staff-player-deck");
        tournament = fixture.Store.StartTournament(fixture.Organizer, tournament.Id, tournament.Version,
            Context("staff-start"), true);
        var match = Assert.Single(tournament.Rounds[0].Matches);
        tournament = fixture.Store.ApplyTournamentRuling(fixture.Organizer, tournament.Id, match.Id,
            new L12TournamentRulingPayload("result", null, "player-a", "confirmed public result"),
            tournament.Version, Context("staff-result"), true);
        tournament = fixture.Store.CompleteTournament(fixture.Organizer, tournament.Id, tournament.Version,
            Context("staff-complete"), true);

        using var response = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(player.Username, text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        Assert.Empty(json.RootElement.GetProperty("participants").EnumerateArray());
        Assert.Equal(2, json.RootElement.GetProperty("counts").GetProperty("registered").GetInt32());
        var publicRound = Assert.Single(json.RootElement.GetProperty("rounds").EnumerateArray());
        Assert.Empty(publicRound.GetProperty("standings").EnumerateArray());
        var publicMatch = Assert.Single(publicRound.GetProperty("matches").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, publicMatch.GetProperty("playerAName").ValueKind);
        Assert.Equal(JsonValueKind.Null, publicMatch.GetProperty("playerBName").ValueKind);
        Assert.Equal("player-a", publicMatch.GetProperty("result").GetString());
        Assert.Empty(json.RootElement.GetProperty("finalStandings").EnumerateArray());

        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var reopened = new L12PlatformStore(fixture.PlatformPath, catalog.PresetDecks, officialCards: catalog.Cards);
        var restored = reopened.PublicTournamentByCode(tournament.Code);
        Assert.NotNull(restored);
        Assert.Empty(restored.Participants);
        Assert.Empty(Assert.Single(restored.Rounds).Standings);
        Assert.Null(Assert.Single(Assert.Single(restored.Rounds).Matches).PlayerAName);

        using var revoke = VisibilityRequest(fixture, tournament, "code", "staff",
            "c3-revoke-completed", false);
        using var revoked = await fixture.Client.SendAsync(revoke);
        Assert.True(revoked.IsSuccessStatusCode, await revoked.Content.ReadAsStringAsync());
        using var hidden = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        AssertNoStore(hidden);
    }

    [Fact]
    public async Task PublicRosterShowsOnlyConfirmedResultsAndCapturedStandingsWithoutInternalIdentifiers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var player = fixture.Store.Register("c3publicr01", "password-123").Account!;
        var tournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("公开赛果赛事", "public"), Context("results-create"), true);
        tournament = fixture.Store.RegisterTournament(player, tournament.Id,
            new L12TournamentRegistrationPayload(), tournament.Version, Context("results-register"), true);
        tournament = PreCheckIn(fixture.Store, fixture.Organizer, tournament, "results-host-deck");
        tournament = PreCheckIn(fixture.Store, player, tournament, "results-player-deck");
        tournament = fixture.Store.StartTournament(fixture.Organizer, tournament.Id, tournament.Version,
            Context("results-start"), true);
        var match = Assert.Single(tournament.Rounds[0].Matches);

        using var pending = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        using var pendingJson = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
        Assert.Contains(pendingJson.RootElement.GetProperty("participants").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == player.Username);
        var pendingRound = Assert.Single(pendingJson.RootElement.GetProperty("rounds").EnumerateArray());
        Assert.Empty(pendingRound.GetProperty("standings").EnumerateArray());
        var pendingMatch = Assert.Single(pendingRound.GetProperty("matches").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, pendingMatch.GetProperty("result").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, pendingMatch.GetProperty("playerAName").ValueKind);

        tournament = fixture.Store.ApplyTournamentRuling(fixture.Organizer, tournament.Id, match.Id,
            new L12TournamentRulingPayload("result", null, "player-a", "confirmed result"),
            tournament.Version, Context("results-confirm"), true);
        tournament = fixture.Store.CompleteTournament(fixture.Organizer, tournament.Id, tournament.Version,
            Context("results-complete"), true);
        using var completed = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var completedText = await completed.Content.ReadAsStringAsync();
        using var completedJson = JsonDocument.Parse(completedText);
        var completedRound = Assert.Single(completedJson.RootElement.GetProperty("rounds").EnumerateArray());
        Assert.Equal(2, completedRound.GetProperty("standings").GetArrayLength());
        Assert.Equal("player-a", Assert.Single(completedRound.GetProperty("matches").EnumerateArray())
            .GetProperty("result").GetString());
        Assert.Equal(2, completedJson.RootElement.GetProperty("finalStandings").GetArrayLength());
        AssertNoForbiddenProperties(completedJson.RootElement);
    }

    [Fact]
    public async Task PublicSummaryFiltersAndPaginationStayBoundedAndExcludeCodeVisibility()
    {
        await using var fixture = await Fixture.CreateAsync();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var first = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("Alpha Open", "public") with { Format = "single", StartAt = start },
            Context("filter-first"), true);
        var second = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("Beta Open", "public") with { Format = "single", StartAt = start.AddHours(1) },
            Context("filter-second"), true);
        var history = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("Alpha Archive", "public") with { Format = "single", StartAt = start.AddHours(-1) },
            Context("filter-history"), true);
        history = fixture.Store.CancelTournament(fixture.Organizer, history.Id,
            new L12TournamentCancelPayload("fixture history"), history.Version, Context("filter-cancel"), true);
        var hidden = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("Alpha Hidden", "code") with { Format = "single", StartAt = start },
            Context("filter-hidden"), true);

        using var pageOne = await fixture.Client.GetAsync(
            "/api/public/tournaments/summaries?section=discover&format=single&page=1&pageSize=1");
        using var pageTwo = await fixture.Client.GetAsync(
            "/api/public/tournaments/summaries?section=discover&format=single&page=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, pageOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pageTwo.StatusCode);
        using var pageOneJson = JsonDocument.Parse(await pageOne.Content.ReadAsStringAsync());
        using var pageTwoJson = JsonDocument.Parse(await pageTwo.Content.ReadAsStringAsync());
        Assert.Equal(first.Code, Assert.Single(pageOneJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("code").GetString());
        Assert.Equal(second.Code, Assert.Single(pageTwoJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("code").GetString());
        Assert.Equal(2, pageOneJson.RootElement.GetProperty("total").GetInt32());

        var from = Uri.EscapeDataString(start.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(start.AddMinutes(1).ToString("O"));
        using var bounded = await fixture.Client.GetAsync(
            $"/api/public/tournaments/summaries?section=discover&format=single&search=Alpha&page=-9&pageSize=1000&startFrom={from}&startTo={to}");
        Assert.Equal(HttpStatusCode.OK, bounded.StatusCode);
        using var boundedJson = JsonDocument.Parse(await bounded.Content.ReadAsStringAsync());
        Assert.Equal(1, boundedJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, boundedJson.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(first.Code, Assert.Single(boundedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("code").GetString());
        Assert.DoesNotContain(hidden.Code, boundedJson.RootElement.GetRawText(), StringComparison.Ordinal);

        using var archived = await fixture.Client.GetAsync(
            "/api/public/tournaments/summaries?section=history&format=single&search=Alpha");
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var archivedJson = JsonDocument.Parse(await archived.Content.ReadAsStringAsync());
        Assert.Equal(history.Code, Assert.Single(archivedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("code").GetString());

        using var revokeCanceled = VisibilityRequest(fixture, history, "code", "public",
            "c3-revoke-canceled", false);
        using var canceledRevoked = await fixture.Client.SendAsync(revokeCanceled);
        Assert.True(canceledRevoked.IsSuccessStatusCode, await canceledRevoked.Content.ReadAsStringAsync());
        using var canceledHidden = await fixture.Client.GetAsync(
            $"/api/public/tournaments/code/{history.Code}");
        Assert.Equal(HttpStatusCode.NotFound, canceledHidden.StatusCode);
        AssertNoStore(canceledHidden);

        using var invalid = await fixture.Client.GetAsync(
            "/api/public/tournaments/summaries?section=mine");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        AssertNoStore(invalid);
    }

    [Fact]
    public async Task OrganizerCanRevokePublicVisibilityWithCasAndPublicReadsStopImmediately()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("待撤回赛事", "public"), Context("revoke-create"), true);
        using var before = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var dryRun = VisibilityRequest(fixture, tournament, "code", "staff", "c3-revoke-preview", true);
        using var preview = await fixture.Client.SendAsync(dryRun);
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.Equal(tournament.Version, fixture.Store.Tournament(fixture.Organizer, tournament.Id)!.Version);
        using var afterPreview = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.OK, afterPreview.StatusCode);

        var outsiderLogin = fixture.Store.Register("c3publicx01", "password-123");
        Assert.True(fixture.Store.SendFriendRequest(fixture.Organizer.Id, outsiderLogin.Account!.Id).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(outsiderLogin.Account.Id, fixture.Organizer.Id, true).Success);
        var otherTournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("他场赛事", "code"), Context("other-event-create"), true);
        _ = fixture.Store.SetTournamentStaff(fixture.Organizer, otherTournament.Id,
            new L12TournamentStaffPayload([outsiderLogin.Account.Id]), otherTournament.Version,
            Context("other-event-referee"), true);
        using var outsiderRequest = VisibilityRequest(tournament, "code", "staff", "c3-revoke-outsider",
            false, outsiderLogin.Token!);
        using var outsider = await fixture.Client.SendAsync(outsiderRequest);
        Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);

        using var staleRequest = VisibilityRequest(tournament with { Version = tournament.Version - 1 },
            "code", "staff", "c3-revoke-stale", false, fixture.Token);
        using var stale = await fixture.Client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var request = VisibilityRequest(fixture, tournament, "code", "staff", "c3-revoke-public", false);
        using var changed = await fixture.Client.SendAsync(request);
        var changedText = await changed.Content.ReadAsStringAsync();
        Assert.True(changed.IsSuccessStatusCode, $"{changed.StatusCode}: {changedText}");

        using var after = await fixture.Client.GetAsync($"/api/public/tournaments/code/{tournament.Code}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        AssertNoStore(after);
        var changedTournament = fixture.Store.Tournament(fixture.Organizer, tournament.Id)!;
        Assert.Equal(tournament.Version + 1, changedTournament.Version);
        Assert.Equal("code", changedTournament.Visibility);
        Assert.Equal("staff", changedTournament.RegistrationVisibility);

        using var replayRequest = VisibilityRequest(fixture, tournament, "code", "staff", "c3-revoke-public",
            false);
        using var replay = await fixture.Client.SendAsync(replayRequest);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(tournament.Version + 1,
            fixture.Store.Tournament(fixture.Organizer, tournament.Id)!.Version);

        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var reopened = new L12PlatformStore(fixture.PlatformPath, catalog.PresetDecks, officialCards: catalog.Cards);
        Assert.Null(reopened.PublicTournamentByCode(tournament.Code));
    }

    [Fact]
    public async Task AnonymousPublicSurfaceDoesNotGrantPrivateActionsAndFeatureFailuresAreNotCacheable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("匿名边界赛事", "public"), Context("anonymous-boundary-create"), true);

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, $"/api/tournaments/{tournament.Id}"),
            new HttpRequestMessage(HttpMethod.Get, $"/api/tournaments/{tournament.Id}/export.csv"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/tournaments/{tournament.Id}/registrations")
            {
                Content = JsonContent.Create(new TournamentRegistrationRequest(null, null,
                    "anonymous-register", tournament.Version)),
            },
            new HttpRequestMessage(HttpMethod.Post, $"/api/tournaments/{tournament.Id}/pre-check-in")
            {
                Content = JsonContent.Create(new TournamentPreCheckInRequest("none", string.Empty, null,
                    "anonymous-check-in", tournament.Version)),
            },
            new HttpRequestMessage(HttpMethod.Put, $"/api/tournaments/{tournament.Id}/visibility")
            {
                Content = JsonContent.Create(new TournamentVisibilityRequest("code", "staff",
                    "anonymous-manage", tournament.Version)),
            },
        };
        foreach (var request in requests)
        {
            using (request)
            using (var response = await fixture.Client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var wrongMethod = await fixture.Client.PostAsJsonAsync(
            "/api/public/tournaments/summaries", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);

        var features = fixture.Store.OperationsConfigSection(fixture.Admin, "features");
        var flags = new Dictionary<string, bool>(features.Config.FeatureFlags ?? new Dictionary<string, bool>(),
            StringComparer.OrdinalIgnoreCase) { ["tournaments"] = false };
        _ = fixture.Store.ApplyOperationsConfigSection(fixture.Admin, "features",
            new L12OperationsSectionPayload(FeatureFlags: flags), features.Revision,
            new Dictionary<string, long>
            {
                ["features/featureFlags/tournaments"] =
                    features.FieldRevisions["features/featureFlags/tournaments"],
            }, "C3 public feature failure", Context("feature-disabled"));
        using var disabled = await fixture.Client.GetAsync("/api/public/tournaments/summaries");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        AssertNoStore(disabled);
    }

    [Fact]
    public async Task AuthenticatedStaffOnlyProjectionHidesUnrelatedIdentitiesAcrossEveryLifecycle()
    {
        await using var fixture = await Fixture.CreateAsync();
        var playerLogins = Enumerable.Range(1, 3)
            .Select(index => fixture.Store.Register($"c3staffp{index:00}", "password-123")).ToArray();
        var outsider = fixture.Store.Register("c3staffout", "password-123");
        var referee = fixture.Store.Register("c3staffref", "password-123");
        Assert.True(fixture.Store.SendFriendRequest(fixture.Organizer.Id, referee.Account!.Id).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(referee.Account.Id, fixture.Organizer.Id, true).Success);
        var accounts = playerLogins.Select(item => item.Account!).ToArray();

        var swiss = CreateStartedStaffTournament(fixture, "全生命周期名单保护", "swiss", accounts);
        swiss = fixture.Store.SetTournamentStaff(fixture.Organizer, swiss.Id,
            new L12TournamentStaffPayload([referee.Account.Id]), swiss.Version,
            Context("staff-referee"), true);
        var swissViewerId = OpponentOfOrganizer(swiss, fixture.Organizer.Id);
        var viewer = playerLogins.Single(item => item.Account!.Id == swissViewerId);
        var unrelatedSwissPlayers = playerLogins.Where(item => item.Account!.Id != swissViewerId)
            .Select(item => item.Account!).ToArray();
        using (var runningParticipant = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", viewer.Token!))
        {
            AssertRestrictedStaffProjection(runningParticipant.RootElement, viewer.Account!.Id, true);
            AssertNoUnrelatedIdentities(runningParticipant.RootElement, unrelatedSwissPlayers);
        }
        using (var runningOutsider = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", outsider.Token!))
        {
            AssertRestrictedStaffProjection(runningOutsider.RootElement, outsider.Account!.Id, false);
            AssertNoUnrelatedIdentities(runningOutsider.RootElement, accounts);
        }
        using (var refereeView = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", referee.Token!))
        {
            Assert.Equal(4, refereeView.RootElement.GetProperty("participants").GetArrayLength());
            Assert.Equal(2, Assert.Single(refereeView.RootElement.GetProperty("rounds").EnumerateArray())
                .GetProperty("matches").GetArrayLength());
        }

        foreach (var match in swiss.Rounds[0].Matches)
            swiss = fixture.Store.ApplyTournamentRuling(fixture.Organizer, swiss.Id, match.Id,
                new L12TournamentRulingPayload("result", null, "player-a", "confirmed result"),
                swiss.Version, Context($"staff-complete-{match.Table}"), true);
        swiss = fixture.Store.CompleteTournament(fixture.Organizer, swiss.Id, swiss.Version,
            Context("staff-completed"), true);

        using (var completedParticipant = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", viewer.Token!))
        {
            AssertRestrictedStaffProjection(completedParticipant.RootElement, viewer.Account!.Id, true);
            Assert.Single(completedParticipant.RootElement.GetProperty("finalSwissStandings").EnumerateArray());
            AssertNoUnrelatedIdentities(completedParticipant.RootElement, unrelatedSwissPlayers);
        }
        using (var completedOutsider = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", outsider.Token!))
        {
            AssertRestrictedStaffProjection(completedOutsider.RootElement, outsider.Account!.Id, false);
            AssertNoUnrelatedIdentities(completedOutsider.RootElement, accounts);
        }
        using (var organizerView = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", fixture.Token))
        {
            Assert.Equal(4, organizerView.RootElement.GetProperty("participants").GetArrayLength());
            Assert.Equal(2, Assert.Single(organizerView.RootElement.GetProperty("rounds").EnumerateArray())
                .GetProperty("matches").GetArrayLength());
            Assert.Equal(4, organizerView.RootElement.GetProperty("finalSwissStandings").GetArrayLength());
        }
        using (var administratorView = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{swiss.Code}", fixture.AdminToken))
            Assert.Equal(4, administratorView.RootElement.GetProperty("participants").GetArrayLength());

        var canceled = CreateStartedStaffTournament(fixture, "取消赛事名单保护", "single", accounts);
        canceled = fixture.Store.CancelTournament(fixture.Organizer, canceled.Id,
            new L12TournamentCancelPayload("fixture cancellation"), canceled.Version,
            Context("staff-canceled"), true);
        var canceledViewerId = OpponentOfOrganizer(canceled, fixture.Organizer.Id);
        var canceledViewer = playerLogins.Single(item => item.Account!.Id == canceledViewerId);
        var unrelatedCanceledPlayers = playerLogins.Where(item => item.Account!.Id != canceledViewerId)
            .Select(item => item.Account!).ToArray();
        using (var canceledParticipant = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{canceled.Code}", canceledViewer.Token!))
        {
            AssertRestrictedStaffProjection(canceledParticipant.RootElement, canceledViewerId, true);
            Assert.Single(canceledParticipant.RootElement.GetProperty("eliminationBracket").EnumerateArray()
                .SelectMany(round => round.GetProperty("matches").EnumerateArray()));
            AssertNoUnrelatedIdentities(canceledParticipant.RootElement, unrelatedCanceledPlayers);
        }
        using (var canceledOutsider = await AuthenticatedJson(fixture,
                   $"/api/tournaments/code/{canceled.Code}", outsider.Token!))
        {
            AssertRestrictedStaffProjection(canceledOutsider.RootElement, outsider.Account!.Id, false);
            AssertNoUnrelatedIdentities(canceledOutsider.RootElement, accounts);
        }

        var publicRoster = fixture.Store.CreateTournament(fixture.Organizer,
            Payload("公开名单控制分支", "public") with { RegistrationVisibility = "public" },
            Context("public-roster-create"), true);
        foreach (var player in accounts)
            publicRoster = fixture.Store.RegisterTournament(player, publicRoster.Id,
                new L12TournamentRegistrationPayload(), publicRoster.Version,
                Context($"public-roster-{player.Id}"), true);
        using var publicRosterView = await AuthenticatedJson(fixture,
            $"/api/tournaments/code/{publicRoster.Code}", outsider.Token!);
        Assert.Equal(4, publicRosterView.RootElement.GetProperty("participants").GetArrayLength());
    }

    private static L12TournamentView CreateStartedStaffTournament(Fixture fixture, string name, string format,
        IReadOnlyList<L12AccountView> players)
    {
        var tournament = fixture.Store.CreateTournament(fixture.Organizer,
            Payload(name, "public") with { Format = format, RegistrationVisibility = "staff" },
            Context($"{format}-create"), true);
        tournament = PreCheckIn(fixture.Store, fixture.Organizer, tournament, $"{format}-host-deck");
        foreach (var player in players)
        {
            tournament = fixture.Store.RegisterTournament(player, tournament.Id,
                new L12TournamentRegistrationPayload(), tournament.Version,
                Context($"{format}-register-{player.Id}"), true);
            tournament = PreCheckIn(fixture.Store, player, tournament, $"{format}-deck-{player.Id}");
        }
        return fixture.Store.StartTournament(fixture.Organizer, tournament.Id, tournament.Version,
            Context($"{format}-start"), true);
    }

    private static async Task<JsonDocument> AuthenticatedJson(Fixture fixture, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static string OpponentOfOrganizer(L12TournamentView tournament, string organizerId)
    {
        var match = tournament.Rounds.SelectMany(round => round.Matches).Single(item =>
            item.PlayerAAccountId == organizerId || item.PlayerBAccountId == organizerId);
        return match.PlayerAAccountId == organizerId ? match.PlayerBAccountId! : match.PlayerAAccountId;
    }

    private static void AssertNoUnrelatedIdentities(JsonElement root,
        IEnumerable<L12AccountView> unrelatedAccounts)
    {
        var payload = root.GetRawText();
        foreach (var account in unrelatedAccounts)
        {
            Assert.DoesNotContain(account.Id, payload, StringComparison.Ordinal);
            Assert.DoesNotContain(account.Username, payload, StringComparison.Ordinal);
        }
    }

    private static void AssertRestrictedStaffProjection(JsonElement root, string viewerId,
        bool participant)
    {
        var participants = root.GetProperty("participants").EnumerateArray().ToArray();
        if (participant)
        {
            Assert.Equal(viewerId, Assert.Single(participants).GetProperty("accountId").GetString());
        }
        else
        {
            Assert.Empty(participants);
        }

        var visibleMatches = root.GetProperty("rounds").EnumerateArray()
            .SelectMany(round => round.GetProperty("matches").EnumerateArray()).ToArray();
        var visibleStandings = root.GetProperty("rounds").EnumerateArray()
            .SelectMany(round => round.GetProperty("standings").EnumerateArray()).ToArray();
        var finalStandings = root.GetProperty("finalSwissStandings").EnumerateArray().ToArray();
        var bracketMatches = root.GetProperty("eliminationBracket").EnumerateArray()
            .SelectMany(round => round.GetProperty("matches").EnumerateArray()).ToArray();
        if (!participant)
        {
            Assert.Empty(visibleMatches);
            Assert.Empty(visibleStandings);
            Assert.Empty(finalStandings);
            Assert.Empty(bracketMatches);
            return;
        }

        Assert.NotEmpty(visibleMatches);
        Assert.All(visibleMatches, match => Assert.True(
            match.GetProperty("playerAAccountId").GetString() == viewerId
            || match.GetProperty("playerBAccountId").GetString() == viewerId));
        Assert.All(visibleStandings,
            standing => Assert.Equal(viewerId, standing.GetProperty("accountId").GetString()));
        Assert.All(finalStandings,
            standing => Assert.Equal(viewerId, standing.GetProperty("accountId").GetString()));
        Assert.All(bracketMatches, match => Assert.True(
            match.GetProperty("playerAAccountId").GetString() == viewerId
            || match.GetProperty("playerBAccountId").GetString() == viewerId));
    }

    private static HttpRequestMessage VisibilityRequest(Fixture fixture, L12TournamentView tournament,
        string visibility, string registrationVisibility, string idempotencyKey, bool dryRun)
        => VisibilityRequest(tournament, visibility, registrationVisibility, idempotencyKey, dryRun,
            fixture.Token);

    private static HttpRequestMessage VisibilityRequest(L12TournamentView tournament, string visibility,
        string registrationVisibility, string idempotencyKey, bool dryRun, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/tournaments/{tournament.Id}/visibility")
        {
            Content = JsonContent.Create(new
            {
                visibility,
                registrationVisibility,
                idempotencyKey,
                expectedVersion = tournament.Version,
                dryRun,
                reason = "public listing visibility audit",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static L12TournamentView PreCheckIn(L12PlatformStore store, L12AccountView account,
        L12TournamentView tournament, string correlationId)
    {
        var deck = store.Decks(account.Id)[0];
        return store.PreCheckInTournament(account, tournament.Id,
            new L12TournamentPreCheckInPayload(deck.Name, string.Empty), tournament.Version,
            Context(correlationId), true);
    }

    private static L12TournamentCreatePayload Payload(string name, string visibility)
        => new(name, "swiss", visibility, 16, DateTimeOffset.UtcNow.AddHours(2), "S01/S02",
            "public description", "after", "season", string.Empty, 50, 5,
            CardRestrictions: [new L12CardRestrictionConfig("S01-0001", 1, "PRIVATE ADMIN NOTE")],
            RegistrationVisibility: "public", TimeControl: new L12RankedTimeControlConfig(1500, 240, 240, 60, 60));

    private static void AssertNoStore(HttpResponseMessage response)
        => Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    private static void AssertAllowlisted(JsonElement value, params string[] expected)
    {
        Assert.Equal(expected.Order(StringComparer.Ordinal),
            value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    private static void AssertNoForbiddenProperties(JsonElement value)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "accountId", "organizerAccountId", "playerAAccountId", "playerBAccountId", "roomCode",
            "recordedMatchId", "codeHash", "hash", "deck", "deckHash", "rulings", "events", "judgeCases",
            "referees", "permissions", "version", "platformVersion", "reason", "staffNote", "audit",
        };
        Walk(value);
        return;

        void Walk(JsonElement current)
        {
            if (current.ValueKind == JsonValueKind.Object)
                foreach (var property in current.EnumerateObject())
                {
                    Assert.DoesNotContain(property.Name, forbidden);
                    Walk(property.Value);
                }
            else if (current.ValueKind == JsonValueKind.Array)
                foreach (var item in current.EnumerateArray()) Walk(item);
        }
    }

    private static string FileHash(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournaments");

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly string? _previousHost;
        private readonly MatchRecorder _recorder;
        private readonly L12WebSocketServer _server;

        public L12PlatformStore Store { get; }
        public string PlatformPath { get; }
        public string Token { get; }
        public L12AccountView Organizer { get; }
        public string AdminToken { get; }
        public L12AccountView Admin { get; }
        public HttpClient Client { get; }

        private Fixture(string root, string platformPath, string? previousHost, L12PlatformStore store,
            L12AccountView organizer, string token, L12AccountView admin, string adminToken,
            MatchRecorder recorder, L12WebSocketServer server, HttpClient client)
        {
            _root = root;
            PlatformPath = platformPath;
            _previousHost = previousHost;
            Store = store;
            Organizer = organizer;
            Token = token;
            Admin = admin;
            AdminToken = adminToken;
            _recorder = recorder;
            _server = server;
            Client = client;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"l12-c3-public-tournaments-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platformPath = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(platformPath, catalog.PresetDecks,
                officialCards: catalog.Cards);
            var adminLogin = store.Login("Admin", "L12master");
            Assert.True(adminLogin.Success, adminLogin.Message);
            var login = store.Register("c3publich01", "password-123");
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store,
                catalog);
            await server.StartAsync(0);
            return new Fixture(root, platformPath, previousHost, store, login.Account!, login.Token!,
                adminLogin.Account!, adminLogin.Token!, recorder, server,
                new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _server.StopAsync();
            await _server.DisposeAsync();
            await _recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", _previousHost);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
