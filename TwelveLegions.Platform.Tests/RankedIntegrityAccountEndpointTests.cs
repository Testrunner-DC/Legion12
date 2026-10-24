using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedIntegrityAccountEndpointTests
{
    private const string AccountPreview = "/api/admin/ranked/integrity/account/preview";
    private const string AccountConfirm = "/api/admin/ranked/integrity/account/decisions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DedicatedAccountEndpointsApplyOnlyTheExplicitParticipant(bool restrict)
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var pair = Seed(fixture.Store, restrict);
        using var previewResponse = await Post(fixture.Client, AccountPreview, fixture.AdminToken, pair.Input);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<L12RankedIntegrityActionPreviewView>())!;
        Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
        Assert.Equal("account", preview.Scope);
        Assert.Equal(new[] { pair.First.Id }, preview.TargetAccountIds);
        using var response = await Post(fixture.Client, AccountConfirm, fixture.AdminToken,
            new { input = pair.Input, expectedRevision = preview.Revision });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decision = (await response.Content.ReadFromJsonAsync<L12RankedIntegrityDecisionView>())!;
        Assert.Equal("account", decision.Scope);
        Assert.Equal(new[] { pair.First.Id }, decision.TargetAccountIds);
        Assert.Equal(new[] { pair.First.Id }, decision.PlayerNotificationAccountIds);
        Assert.Equal(pair.First.Id, Assert.Single(decision.AccountEffects).AccountId);
        Assert.Equal(pair.FirstBefore, PlayerState(fixture.Store, pair.First.Id));
        Assert.Equal(pair.SecondBefore, PlayerState(fixture.Store, pair.Second.Id));
        Assert.Equal(restrict, fixture.Store.RankedEntryBlock(pair.First.Id, DateTimeOffset.UtcNow) is not null);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(pair.Second).Items);
        Assert.Equal("applied", fixture.Store.RankedSettlement("wire-selected", pair.Second.Id)!.RewardStatus);
        Assert.Equal("voided", fixture.Store.RankedSettlement("wire-selected", pair.First.Id)!.RewardStatus);
    }

    [Theory]
    [InlineData("preview", "legacy-shape")]
    [InlineData("decisions", "legacy-shape")]
    [InlineData("preview", "match-scope")]
    [InlineData("decisions", "match-scope")]
    [InlineData("preview", "missing-targets")]
    [InlineData("decisions", "missing-targets")]
    public async Task DedicatedAccountEndpointsCannotDefaultMissingScopeToWholeMatch(string endpoint, string shape)
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var pair = Seed(fixture.Store, false);
        var before = MutationState(fixture.Store, pair);
        var input = JsonSerializer.SerializeToNode(pair.Input, JsonOptions)!.AsObject();
        if (shape == "legacy-shape") { input.Remove("scope"); input.Remove("targetAccountIds"); }
        if (shape == "match-scope") { input["scope"] = "match"; input["targetAccountIds"] = new JsonArray(); }
        if (shape == "missing-targets") input.Remove("targetAccountIds");
        object body = endpoint == "preview" ? input
            : new { input, expectedRevision = fixture.Store.PreviewRankedIntegrityAction(
                fixture.Store.Authenticate("Bearer " + fixture.AdminToken)!, pair.Input).Revision };
        using var response = await Post(fixture.Client, "/api/admin/ranked/integrity/account/" + endpoint,
            fixture.AdminToken, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, MutationState(fixture.Store, pair));
    }

    [Fact]
    public async Task DedicatedAccountMutationStillRequiresExistingGovernancePermission()
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var pair = Seed(fixture.Store, false);
        var before = PlayerState(fixture.Store, pair.Second.Id);
        using var response = await Post(fixture.Client, AccountConfirm, fixture.PlayerToken,
            new { input = pair.Input, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(fixture.Store.RankedIntegrityDecisions(
            fixture.Store.Authenticate("Bearer " + fixture.AdminToken)!).Items);
        Assert.Equal(before, PlayerState(fixture.Store, pair.Second.Id));
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(pair.First).Items);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(pair.Second).Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableAccountRoutesOnLegacyServiceNeverReachLegacyConfirmation(bool rollbackAfterPreview)
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var pair = Seed(fixture.Store, false);
        long revision = 0;
        if (rollbackAfterPreview)
        {
            using var previewResponse = await Post(fixture.Client, AccountPreview, fixture.AdminToken, pair.Input);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<L12RankedIntegrityActionPreviewView>())!;
            Assert.Equal("account", preview.Scope);
            revision = preview.Revision;
        }
        await using var legacy = await LegacyRoutes.Start(fixture.Store);
        var before = MutationState(fixture.Store, pair);
        if (!rollbackAfterPreview)
        {
            using var previewResponse = await Post(legacy.Client, AccountPreview, fixture.AdminToken, pair.Input);
            Assert.Equal(HttpStatusCode.NotFound, previewResponse.StatusCode);
        }
        using var response = await Post(legacy.Client, AccountConfirm, fixture.AdminToken,
            new { input = pair.Input, expectedRevision = revision });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, legacy.ConfirmCalls);
        Assert.Equal(before, MutationState(fixture.Store, pair));
    }

    [Fact]
    public async Task LegacyDtoControlReallyIgnoresUnsupportedAccountFields()
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var pair = Seed(fixture.Store, false);
        await using var legacy = await LegacyRoutes.Start(fixture.Store);
        using var previewResponse = await Post(legacy.Client, "/api/admin/ranked/integrity/preview",
            fixture.AdminToken, pair.Input);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<L12RankedIntegrityActionPreviewView>())!;
        Assert.Equal("match", preview.Scope);
        Assert.Equal(2, preview.AccountEffects.Count);
        using var response = await Post(legacy.Client, "/api/admin/ranked/integrity/decisions", fixture.AdminToken,
            new { input = pair.Input, expectedRevision = preview.Revision });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, legacy.ConfirmCalls);
        Assert.NotEqual(pair.SecondBefore, PlayerState(fixture.Store, pair.Second.Id));
        Assert.Single(fixture.Store.RankedIntegrityNotifications(pair.Second).Items);
    }

    private static Pair Seed(L12PlatformStore store, bool restrict)
    {
        var first = store.Register("wireone", "Password123!");
        var second = store.Register("wiretwo", "Password123!");
        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        store.SelectRankedFaction(first.Account!.Id, "order");
        store.SelectRankedFaction(second.Account!.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"wire-placement-{index}", first.Account.Id, second.Account.Id, index % 2);
        var firstBefore = PlayerState(store, first.Account.Id);
        store.SettleRankedMatch("wire-selected", first.Account.Id, second.Account.Id, 0);
        var secondBefore = PlayerState(store, second.Account.Id);
        var input = new L12RankedIntegrityActionInput("wire-request", "confirmed", ["wire-selected"],
            restrict ? [first.Account.Id] : [], restrict ? 7 : null, "真实HTTP合成结算证据", "只撤所选参赛者收益",
            Scope: "account", TargetAccountIds: [first.Account.Id]);
        return new(first.Account, second.Account, firstBefore, secondBefore, input);
    }

    private sealed record Pair(L12AccountView First, L12AccountView Second,
        string FirstBefore, string SecondBefore, L12RankedIntegrityActionInput Input);

    private static string PlayerState(L12PlatformStore store, string id)
        => JsonSerializer.Serialize(new { Profile = store.RankedProfile(id), Rating = store.HiddenRating(id) });

    private static string MutationState(L12PlatformStore store, Pair pair)
        => JsonSerializer.Serialize(new
        {
            First = PlayerState(store, pair.First.Id), Second = PlayerState(store, pair.Second.Id),
            FirstNotices = store.RankedIntegrityNotifications(pair.First),
            SecondNotices = store.RankedIntegrityNotifications(pair.Second),
            FirstSettlement = store.RankedSettlement("wire-selected", pair.First.Id),
            SecondSettlement = store.RankedSettlement("wire-selected", pair.Second.Id),
            Audit = store.AdminAudit(category: "ranked-integrity"),
        });

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await client.SendAsync(request);
    }

    // This is a real HTTP compatibility shell with only the original routes and eight-field DTO,
    // not a historical product binary. It delegates all effects to the same real Store pipeline.
    private sealed record LegacyInput(string RequestId, string Disposition, IReadOnlyList<string> MatchIds,
        IReadOnlyList<string> RestrictedAccountIds, int? RestrictionDays, string Evidence, string Reason,
        string? RevokesDecisionId = null)
    {
        public L12RankedIntegrityActionInput ToInput() => new(RequestId, Disposition, MatchIds,
            RestrictedAccountIds, RestrictionDays, Evidence, Reason, RevokesDecisionId);
    }
    private sealed record LegacyConfirm(LegacyInput Input, long ExpectedRevision);

    private sealed class LegacyRoutes : IAsyncDisposable
    {
        private readonly WebApplication app;
        public HttpClient Client { get; }
        public int ConfirmCalls { get; private set; }
        private LegacyRoutes(WebApplication app) { this.app = app; Client = new() { BaseAddress = new Uri(app.Urls.Single()) }; }

        public static async Task<LegacyRoutes> Start(L12PlatformStore store)
        {
            var builder = WebApplication.CreateBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            LegacyRoutes? holder = null;
            app.MapPost("/api/admin/ranked/integrity/preview", (HttpRequest request, LegacyInput input) =>
            {
                var actor = store.Authenticate(request.Headers.Authorization);
                return actor is null ? Results.Unauthorized()
                    : Results.Ok(store.PreviewRankedIntegrityAction(actor, input.ToInput()));
            });
            app.MapPost("/api/admin/ranked/integrity/decisions", (HttpRequest request, LegacyConfirm body) =>
            {
                var actor = store.Authenticate(request.Headers.Authorization);
                if (actor is null) return Results.Unauthorized();
                holder!.ConfirmCalls++;
                return Results.Ok(store.ConfirmRankedIntegrityAction(actor, body.Input.ToInput(),
                    body.ExpectedRevision, new("legacy-wire-confirm")));
            });
            try { await app.StartAsync(); holder = new LegacyRoutes(app); return holder; }
            catch { await app.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
