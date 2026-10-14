using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminGovernanceStatisticsLinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedDecisionRefreshesWarmHttpStatisticsWithoutRemovingRecordedMatches(bool latest)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var player = f.Store.AuthenticateTokenSession(f.PlayerToken)!.Account;
        var registered = f.Store.Register("govpair", "password-123");
        Assert.True(registered.Success, registered.Message);
        var opponent = Assert.IsType<L12AccountView>(registered.Account);
        f.Store.SelectRankedFaction(player.Id, "order");
        f.Store.SelectRankedFaction(opponent.Id, "chaos");
        var deck = catalog.DeckAt(0);
        var comparison = catalog.PresetDecks.First(other => deck.CardIds
            .Except(other.CardIds, StringComparer.OrdinalIgnoreCase).Any());
        var card = deck.CardIds.Except(comparison.CardIds, StringComparer.OrdinalIgnoreCase).First();
        // A second recorder uses only the same owned synthetic fixture database.
        // Public producers generate immutable participants/decks and terminal facts;
        // no hand-written SQL or reflection creates eligible statistics.
        await using var producer = new MatchRecorder(Path.Combine(Path.GetDirectoryName(f.MirrorPath)!, "matches.db"));
        await producer.InitializeAsync();
        for (var index = 0; index < 2; index++)
        {
            var game = new L12GameEngine(catalog, $"gov-http-{index}", $"GOV{index}", 300 + index,
                [player.Username, opponent.Username], [deck, comparison], skipPreparation: true,
                operationsPolicy: f.Store.CaptureOperationsPolicy());
            await producer.StartAsync(game, "ranked", player.Id, opponent.Id, [deck, comparison]);
            game.ConcludeByAuthority(0, "合成治理终局");
            await producer.AppendAuthorityAsync(game, 1, "合成治理终局");
            Assert.True(await producer.CompleteAsync(game));
            f.Store.SettleRankedMatch(game.State.MatchId, player.Id, opponent.Id, 0);
        }

        var cardPath = "/api/admin/analytics/cards?effectVersion=all&minimumSampleSize=1&search=" + Uri.EscapeDataString(card);
        const string masterPath = "/api/admin/analytics/masters?effectVersion=all&minimumSampleSize=1";
        var before = await Get<L12CardAnalyticsPage>(f, cardPath, f.AdminToken);
        Assert.Equal(2, before.Summary.EligibleMatches);
        Assert.Equal(2, Assert.Single(before.Items).SampleSize);
        var warm = await Get<L12CardAnalyticsPage>(f, cardPath, f.AdminToken);
        Assert.Equal(JsonSerializer.Serialize(before.Summary), JsonSerializer.Serialize(warm.Summary));
        var masters = await Get<L12MasterAnalyticsReport>(f, masterPath, f.AdminToken);
        Assert.Equal(4, masters.Items.Sum(item => item.ParticipantSamples));
        _ = await Get<L12MasterAnalyticsReport>(f, masterPath, f.AdminToken);
        Assert.Equal(2, (await Get<L12PlayerStatisticsView>(f, "/api/me/statistics?range=all", f.PlayerToken)).Ranked.Games);

        var target = latest ? "gov-http-1" : "gov-http-0";
        var input = new L12RankedIntegrityActionInput("http-governance-confirm", "confirmed", [target], [], null,
            "已核对本地合成排位结算及不可变对局事实。", "合成治理统计排除验收。");
        L12RankedIntegrityActionPreviewView preview;
        using (var response = await Send(f, HttpMethod.Post, "/api/admin/ranked/integrity/preview", f.AdminToken, input))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            preview = (await response.Content.ReadFromJsonAsync<L12RankedIntegrityActionPreviewView>())!;
            Assert.True(preview.CanConfirm, string.Join(" | ", preview.BlockingReasons));
        }
        var body = new { Input = input, ExpectedRevision = preview.Revision };
        using (var denied = await Send(f, HttpMethod.Post, "/api/admin/ranked/integrity/decisions", f.PlayerToken, body))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("applied", f.Store.RankedSettlement(target, player.Id)!.RewardStatus);
        L12RankedIntegrityDecisionView decision;
        using (var response = await Send(f, HttpMethod.Post, "/api/admin/ranked/integrity/decisions", f.AdminToken, body))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            decision = (await response.Content.ReadFromJsonAsync<L12RankedIntegrityDecisionView>())!;
            Assert.Equal("confirmed", decision.EffectiveDisposition);
        }
        Assert.Equal("voided", f.Store.RankedSettlement(target, player.Id)!.RewardStatus);
        Assert.All(decision.AccountEffects, effect => Assert.Equal(latest ? "voided" : "voided-profile-preserved", effect.RewardOutcome));
        var decisions = await Get<L12RankedIntegrityDecisionPageView>(f, "/api/admin/ranked/integrity/decisions", f.AdminToken);
        Assert.Equal(decision.DecisionId, Assert.Single(decisions.Items).DecisionId);
        var after = await Get<L12CardAnalyticsPage>(f, cardPath, f.AdminToken);
        Assert.Equal(1, after.Summary.EligibleMatches);
        Assert.Equal(1, Assert.Single(after.Items).SampleSize);
        var afterMasters = await Get<L12MasterAnalyticsReport>(f, masterPath, f.AdminToken);
        Assert.Equal(2, afterMasters.Items.Sum(item => item.ParticipantSamples));
        Assert.Equal(1, (await Get<L12PlayerStatisticsView>(f, "/api/me/statistics?range=all", f.PlayerToken)).Ranked.Games);
        // Existing rule: voiding an older match cannot rewrite downstream settled
        // ranking facts. Only a latest suffix rolls the current profile back.
        var profile = (await Get<L12RankedOverviewView>(f, "/api/ranked/me", f.PlayerToken)).Profile;
        Assert.Equal(latest ? 1 : 2, profile.Wins);
        Assert.Equal(latest ? 1 : 2, profile.PlacementPlayed);
        Assert.NotNull(await producer.GetMatchAsync("gov-http-0"));
        Assert.NotNull(await producer.GetMatchAsync("gov-http-1"));
        using (var replay = await Send(f, HttpMethod.Post, "/api/admin/ranked/integrity/decisions", f.AdminToken, body))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(decision.DecisionId, (await replay.Content.ReadFromJsonAsync<L12RankedIntegrityDecisionView>())!.DecisionId);
        }
        Assert.Equal(1, (await Get<L12CardAnalyticsPage>(f, cardPath, f.AdminToken)).Summary.EligibleMatches);
        Assert.Single((await Get<L12RankedIntegrityDecisionPageView>(f, "/api/admin/ranked/integrity/decisions", f.AdminToken)).Items);
    }

    private static async Task<T> Get<T>(AdminWorkbenchTimingTests.Fixture f, string path, string token)
    {
        using var response = await Send(f, HttpMethod.Get, path, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpResponseMessage> Send(AdminWorkbenchTimingTests.Fixture f,
        HttpMethod method, string path, string token, object? body = null)
    {
        using var request = f.Request(token);
        request.Method = method;
        request.RequestUri = new Uri(path, UriKind.Relative);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Client.SendAsync(request);
    }
}
