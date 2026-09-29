using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class SeasonActivationTests
{
    [Fact]
    public void ActivationArchivesOnceAndCarriesCompetitionStateWhileResettingSeasonStatistics()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("赛季承接甲", "Password123!").Account!;
            var second = store.Register("赛季承接乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            for (var index = 0; index < 6; index++)
                store.SettleRankedMatch($"carry-{index}", first.Id, second.Id, index == 5 ? 1 : 0);

            var before = store.RankedProfile(first.Id);
            var hidden = store.HiddenRating(first.Id);
            var catalogBefore = store.SeasonCatalog(admin);
            var draft = catalogBefore.Next!;
            var ranked = draft.Configuration.Ranked with
            {
                Factions = draft.Configuration.Ranked.Factions.Select(faction => faction with
                {
                    Tiers = faction.Tiers.Select((tier, index) => index == 0
                        ? tier with { BaseDelta = tier.BaseDelta + 17 }
                        : tier).ToArray(),
                }).ToArray(),
            };
            var updated = store.UpdateSeasonDraft(admin, draft.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", DateTimeOffset.UtcNow.AddDays(30),
                    DateTimeOffset.UtcNow.AddDays(120), draft.Configuration with { Ranked = ranked }),
                draft.Revision, "prepare S02", Context("prepare", draft.Revision));

            var result = store.ActivateSeason(admin, updated.DefinitionId,
                catalogBefore.Current.Revision, updated.Revision, "activate S02",
                new L12RankedSeasonCutoverReadiness(catalogBefore.Current.SeasonId, 0, 0, 0, 0),
                Context("activate", store.OperationsConfig(admin).Version));

            Assert.True(result.Activated);
            Assert.Equal("S02", result.Current.SeasonId);
            Assert.Equal(catalogBefore.Current.SeasonId, result.Archive.SeasonId);
            Assert.Null(store.SeasonCatalog(admin).Next);
            Assert.Single(store.SeasonArchives(admin));
            Assert.Null(store.RankedConfig(admin).PendingGradient);
            Assert.Equal(ranked.Factions[0].Tiers[0].BaseDelta,
                store.RankedConfig(admin).Factions[0].Tiers[0].BaseDelta);

            var after = store.RankedProfile(first.Id);
            Assert.Equal("S02", after.SeasonId);
            Assert.Equal(before.Faction, after.Faction);
            Assert.Equal(before.SevenValue, after.SevenValue);
            Assert.Equal(hidden, store.HiddenRating(first.Id));
            Assert.Equal(store.RankedConfig(admin).PlacementMatches, after.PlacementPlayed);
            Assert.Equal(0, after.PlacementWins);
            Assert.Equal(0, after.Wins);
            Assert.Equal(0, after.Losses);
            Assert.Equal(before.WinStreak, after.WinStreak);
            Assert.Equal(before.LossStreak, after.LossStreak);
            _ = store.SettleRankedMatch("carry-0", first.Id, second.Id, 0,
                seasonId: catalogBefore.Current.SeasonId);
            Assert.Equal(0, store.RankedProfile(first.Id).Wins);
            Assert.Throws<InvalidOperationException>(() => store.SettleRankedMatch("late-old-season",
                first.Id, second.Id, 0, seasonId: catalogBefore.Current.SeasonId));
            Assert.Equal(0, store.RankedProfile(first.Id).Wins);
            var history = Assert.Single(store.RankedOverview(first.Id).History);
            Assert.Equal(catalogBefore.Current.SeasonId, history.SeasonId);
            Assert.Equal(before.Wins, history.Wins);
            Assert.Equal(before.Losses, history.Losses);

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            Assert.Equal("S02", reopened.SeasonCatalog(reopenedAdmin).Current.SeasonId);
            Assert.Single(reopened.SeasonArchives(reopenedAdmin));
            Assert.Equal(before.SevenValue, reopened.RankedProfile(first.Id).SevenValue);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ActivationRejectsUndrainedSeasonAndRollsBackPersistenceFailure()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var seasons = store.SeasonCatalog(admin);
            var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", null, null,
                    seasons.Next.Configuration), seasons.Next.Revision, "prepare",
                Context("prepare", seasons.Next.Revision));

            var blocked = Assert.Throws<L12OperationsConfigException>(() => store.ActivateSeason(admin,
                draft.DefinitionId, seasons.Current.Revision, draft.Revision, "blocked",
                new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 1, 0, 0, 0),
                Context("blocked")));
            Assert.Equal("season_cutover_not_ready", blocked.Code);
            Assert.Empty(store.SeasonArchives(admin));

            store.StorageFailureInjector = phase =>
            {
                if (phase == "before-commit") throw new IOException("injected season commit failure");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ActivateSeason(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, "atomic failure",
                new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 0, 0, 0, 0),
                Context("failure")));
            store.StorageFailureInjector = null;

            Assert.Equal(seasons.Current.SeasonId, store.SeasonCatalog(admin).Current.SeasonId);
            Assert.NotNull(store.SeasonCatalog(admin).Next);
            Assert.Empty(store.SeasonArchives(admin));
            Assert.Equal(seasons.Current.SeasonId, store.OperationsConfig(admin).Config.Season.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenericOperationsApplyCannotBypassSeasonActivation()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);
            var error = Assert.Throws<L12OperationsConfigException>(() => store.ApplyOperationsConfig(admin,
                current.Config with { Season = current.Config.Season with { Id = "S-bypass" } },
                current.Version, "bypass", Context("bypass")));
            Assert.Equal("season_activation_required", error.Code);
            Assert.Equal(current.Config.Season.Id, store.OperationsConfig(admin).Config.Season.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ActivationApiIsHighRiskIdempotentAndRequiresOperationsPreconditions()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var login = store.Login("Admin", "L12master");
            var seasons = store.SeasonCatalog(login.Account!);
            var draft = store.UpdateSeasonDraft(login.Account!, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S-api-next", "API 新赛季", null, null,
                    seasons.Next.Configuration), seasons.Next.Revision, "prepare api season",
                Context("prepare-api"));
            var operationsVersion = store.OperationsConfig(login.Account!).Version;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var body = new SeasonActivationRequest(seasons.Current.Revision, draft.Revision,
                "activate from api", ExpectedVersion: operationsVersion);

            using (var missingKey = Authorized(HttpMethod.Post,
                       $"/api/admin/seasons/draft/{draft.DefinitionId}/activate", login.Token!, body))
            using (var response = await client.SendAsync(missingKey))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var authorizedBody = body with { IdempotencyKey = "season-activate-api-1" };
            using var firstRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/activate", login.Token!, authorizedBody);
            using var firstResponse = await client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var first = (await firstResponse.Content.ReadFromJsonAsync<L12SeasonActivationView>())!;
            Assert.True(first.Activated);
            Assert.False(firstResponse.Headers.Contains("X-Idempotent-Replay"));

            using var replayRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/draft/{draft.DefinitionId}/activate", login.Token!, authorizedBody);
            using var replayResponse = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            var replay = (await replayResponse.Content.ReadFromJsonAsync<L12SeasonActivationView>())!;
            Assert.True(replay.Activated);
            Assert.Equal("true", replayResponse.Headers.GetValues("X-Idempotent-Replay").Single());
            Assert.Single(store.SeasonArchives(login.Account!));
            Assert.Equal("S-api-next", store.SeasonCatalog(login.Account!).Current.SeasonId);
            Assert.Contains(store.AdminCommands(type: "operations.config.season-activate"),
                command => command.Risk == "high" && command.Status == "executed");
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    private static L12AdminAuditContext Context(string id, long? expected = null)
        => new(id, ExpectedVersion: expected, RequestMethod: "TEST", RequestPath: "/test");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-season-activation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
