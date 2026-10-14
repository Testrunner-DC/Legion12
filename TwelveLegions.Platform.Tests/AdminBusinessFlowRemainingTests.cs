using System.Net;
using System.Net.Http.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// HTTP linkage only; setup uses synthetic settled facts, not room/WS or production data.
[Collection("Platform environment")]
public sealed class AdminBusinessFlowRemainingTests
{
    [Fact]
    public async Task SeasonHttpActivationFeedsPublicPolicyRankedHistoryAndSummaryExactlyOnce()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var player = f.Store.AuthenticateTokenSession(f.PlayerToken)!.Account;
        var opponent = f.Store.Register("wbseasonop", "password-123").Account!;
        f.Store.SelectRankedFaction(player.Id, "order");
        f.Store.SelectRankedFaction(opponent.Id, "chaos");
        for (var index = 0; index < 6; index++)
            f.Store.SettleRankedMatch($"business-season-{index}", player.Id, opponent.Id, 0);

        var before = await Get<L12RankedOverviewView>(f, "/api/ranked/me", f.PlayerToken);
        Assert.Equal(6, before.Profile.Wins);
        Assert.True(before.Profile.SevenValue > 0);
        var seasons = await Get<L12SeasonCatalogView>(f, "/api/admin/seasons", f.AdminToken);
        var oldSeasonId = seasons.Current.SeasonId;
        var next = Assert.IsType<L12SeasonDefinitionView>(seasons.Next);
        var draft = new L12SeasonDefinitionDraft("S-business-http", "合成HTTP切季",
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(91), next.Configuration);

        L12SeasonDefinitionPreviewView definitionPreview;
        using (var response = await Send(f, HttpMethod.Post,
                   $"/api/admin/seasons/{next.DefinitionId}/preview", f.AdminToken,
                   new SeasonDefinitionPreviewRequest(draft, next.Revision, seasons.OperationsVersion)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            definitionPreview = (await response.Content
                .ReadFromJsonAsync<L12SeasonDefinitionPreviewView>())!;
            Assert.True(definitionPreview.Valid);
        }

        using (var response = await Send(f, HttpMethod.Put,
                   $"/api/admin/seasons/{next.DefinitionId}", f.AdminToken,
                   new SeasonDefinitionApplyRequest(draft, next.Revision,
                       definitionPreview.PreviewToken, "合成切季定义验收",
                       "business-season-definition", seasons.OperationsVersion)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var applied = (await response.Content
                .ReadFromJsonAsync<L12SeasonDefinitionOperationView>())!;
            Assert.True(applied.Applied);
            Assert.Equal("next", applied.Slot);
        }

        seasons = await Get<L12SeasonCatalogView>(f, "/api/admin/seasons", f.AdminToken);
        next = Assert.IsType<L12SeasonDefinitionView>(seasons.Next);
        L12SeasonActivationImpactPreviewView activationPreview;
        using (var response = await Send(f, HttpMethod.Post,
                   $"/api/admin/seasons/draft/{next.DefinitionId}/activation-preview", f.AdminToken,
                   new SeasonActivationPreviewRequest(seasons.Current.Revision, next.Revision,
                       seasons.OperationsVersion)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            activationPreview = (await response.Content
                .ReadFromJsonAsync<L12SeasonActivationImpactPreviewView>())!;
            Assert.True(activationPreview.Valid);
            Assert.True(activationPreview.Readiness.Ready);
            Assert.Equal(2, activationPreview.HistoryRecordCount);
            Assert.Equal(2, activationPreview.SummaryNotificationCount);
        }

        var activateBody = new SeasonActivationRequest(seasons.Current.Revision, next.Revision,
            "合成切季激活验收", "business-season-activate", seasons.OperationsVersion);
        using (var denied = await Send(f, HttpMethod.Post,
                   $"/api/admin/seasons/draft/{next.DefinitionId}/activate", f.PlayerToken,
                   activateBody))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var response = await Send(f, HttpMethod.Post,
                   $"/api/admin/seasons/draft/{next.DefinitionId}/activate", f.AdminToken,
                   activateBody))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var activated = (await response.Content.ReadFromJsonAsync<L12SeasonActivationView>())!;
            Assert.True(activated.Activated);
            Assert.Equal("S-business-http", activated.Current.SeasonId);
            Assert.Equal(oldSeasonId, activated.Archive.SeasonId);
            Assert.True(activated.Readiness.Ready);
        }

        var policy = await Get<L12EffectiveOperationsPolicyView>(f,
            "/api/operations/effective-policy", null);
        Assert.Equal("S-business-http", policy.Season.Id);
        var ranked = await Get<L12RankedOverviewView>(f, "/api/ranked/me", f.PlayerToken);
        Assert.Equal("S-business-http", ranked.Profile.SeasonId);
        Assert.Equal(0, ranked.Profile.SevenValue);
        Assert.Equal(0, ranked.Profile.PlacementPlayed);
        Assert.Equal(0, ranked.Profile.Wins);
        var history = Assert.Single(ranked.History);
        Assert.Equal(oldSeasonId, history.SeasonId);
        Assert.Equal(before.Profile.SevenValue, history.SevenValue);
        Assert.Equal(before.Profile.Wins, history.Wins);

        var archives = await Get<L12SeasonArchiveView[]>(f,
            "/api/admin/seasons/archives", f.AdminToken);
        Assert.Equal(oldSeasonId, Assert.Single(archives).SeasonId);
        var notifications = await Get<L12SeasonSummaryNotificationView[]>(f,
            "/api/me/season-summary-notifications", f.PlayerToken);
        var notification = Assert.Single(notifications);
        Assert.Equal(oldSeasonId, notification.SeasonId);
        Assert.Equal(before.Profile.SevenValue, notification.SevenValue);
        Assert.Equal(before.Profile.Wins, notification.Wins);

        using (var replay = await Send(f, HttpMethod.Post,
                   $"/api/admin/seasons/draft/{next.DefinitionId}/activate", f.AdminToken,
                   activateBody))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("X-Idempotent-Replay")));
        }
        Assert.Single(await Get<L12SeasonArchiveView[]>(f,
            "/api/admin/seasons/archives", f.AdminToken));
        Assert.Single(await Get<L12SeasonSummaryNotificationView[]>(f,
            "/api/me/season-summary-notifications", f.PlayerToken));
    }

    private static async Task<T> Get<T>(AdminWorkbenchTimingTests.Fixture f,
        string path, string? token)
    {
        using var response = await Send(f, HttpMethod.Get, path, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpResponseMessage> Send(AdminWorkbenchTimingTests.Fixture f,
        HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = f.Request(token);
        request.Method = method;
        request.RequestUri = new Uri(path, UriKind.Relative);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Client.SendAsync(request);
    }
}
