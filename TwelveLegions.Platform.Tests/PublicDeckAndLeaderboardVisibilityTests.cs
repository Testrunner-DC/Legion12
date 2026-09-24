using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckAndLeaderboardVisibilityTests
{
    [Fact]
    public async Task RankingsReturnOnlyTopFiftyAndAppendAuthenticatedViewerAtTrueRank()
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
            var rival = store.Register("tdrankrival", "password-123").Account!;
            store.SelectRankedFaction(rival.Id, "chaos");
            for (var index = 0; index < 50; index++)
            {
                var leader = store.Register($"tdrank{index:000}", "password-123").Account!;
                store.SelectRankedFaction(leader.Id, "order");
                for (var placement = 0; placement < 5; placement++)
                    store.SettleRankedMatch($"rank-top-{index:000}-{placement}", leader.Id, rival.Id, 0);
            }
            var viewerLogin = store.Register("tview51", "password-123");
            Assert.True(viewerLogin.Success, viewerLogin.Message);
            var viewer = viewerLogin.Account!;
            store.SelectRankedFaction(viewer.Id, "order");
            for (var placement = 0; placement < 5; placement++)
                store.SettleRankedMatch($"rank-viewer-{placement}", viewer.Id, rival.Id, 1);

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            var anonymous = await Json(client, new HttpRequestMessage(HttpMethod.Get,
                "/api/rankings?faction=order&limit=500"));
            var anonymousPlayers = anonymous["players"]!.AsArray();
            Assert.Equal(50, anonymousPlayers.Count);
            Assert.Null(anonymous["masterChampions"]);
            Assert.DoesNotContain(anonymousPlayers, row => row!["username"]!.GetValue<string>() == viewer.Username);

            using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get,
                "/api/rankings?faction=order&limit=500");
            authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewerLogin.Token);
            var authenticated = await Json(client, authenticatedRequest);
            var visiblePlayers = authenticated["players"]!.AsArray();
            Assert.Equal(51, visiblePlayers.Count);
            Assert.Equal(viewer.Username, visiblePlayers[^1]!["username"]!.GetValue<string>());
            Assert.Equal(51, visiblePlayers[^1]!["rank"]!.GetValue<int>());

            var topLogin = store.Login("tdrank000", "password-123");
            using var topRequest = new HttpRequestMessage(HttpMethod.Get, "/api/rankings?faction=order");
            topRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", topLogin.Token);
            var top = await Json(client, topRequest);
            var topPlayers = top["players"]!.AsArray();
            Assert.Equal(50, topPlayers.Count);
            Assert.Single(topPlayers, row => row!["username"]!.GetValue<string>() == topLogin.Account!.Username);
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
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PublicDecksExposeFourAuthoritativeSortsAndSeasonComplianceFilter()
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
            var authorLogin = store.Register("tauthor1", "password-123");
            var likerOneLogin = store.Register("tlike1x", "password-123");
            var likerTwoLogin = store.Register("tlike2x", "password-123");
            Assert.True(authorLogin.Success, authorLogin.Message);
            Assert.True(likerOneLogin.Success, likerOneLogin.Message);
            Assert.True(likerTwoLogin.Success, likerTwoLogin.Message);
            var author = authorLogin.Account!;
            var likerOne = likerOneLogin.Account!;
            var likerTwo = likerTwoLogin.Account!;
            var presets = catalog.PresetDecks.Take(4).ToArray();
            Assert.Equal(4, presets.Length);
            L12PresetDeckDefinition Named(L12PresetDeckDefinition source, string name) => new()
            {
                Name = name,
                MasterId = source.MasterId,
                CardIds = [.. source.CardIds],
                MoraleIds = [.. source.MoraleIds],
                SpecialIds = [.. source.SpecialIds],
            };
            var copies = store.PublishDeck(author.Id, Named(presets[0], "最多复制牌库"), null)!;
            var likes = store.PublishDeck(author.Id, Named(presets[1], "最多点赞牌库"), null)!;
            var views = store.PublishDeck(author.Id, Named(presets[2], "最多浏览牌库"), null)!;
            var latest = store.PublishDeck(author.Id, Named(presets[3], "最新发布牌库"), null)!;
            for (var index = 0; index < 3; index++) store.RecordPublishedDeckCopy(copies.Id, null);
            store.TogglePublishedDeckLike(likerOne.Id, likes.Id);
            store.TogglePublishedDeckLike(likerTwo.Id, likes.Id);
            for (var index = 0; index < 4; index++) store.RecordPublishedDeckView(views.Id, null);

            var bannedCardId = presets[0].CardIds.First();
            var admin = store.Login("Admin", "L12master").Account!;
            var operations = store.OperationsConfig(admin);
            store.ApplyOperationsConfig(admin, operations.Config with
            {
                CardRestrictions = [new L12CardRestrictionConfig(bannedCardId, 0, "本赛季禁用")],
            }, operations.Version, "public deck season compliance", Context("deck-season"));

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            Assert.Equal(copies.Id, (await Json(client, Get("/api/public-decks?sort=copies")))[0]!["id"]!.GetValue<string>());
            Assert.Equal(likes.Id, (await Json(client, Get("/api/public-decks?sort=likes")))[0]!["id"]!.GetValue<string>());
            Assert.Equal(views.Id, (await Json(client, Get("/api/public-decks?sort=views")))[0]!["id"]!.GetValue<string>());
            Assert.Equal(latest.Id, (await Json(client, Get("/api/public-decks?sort=latest")))[0]!["id"]!.GetValue<string>());

            var all = await Json(client, Get("/api/public-decks?sort=copies"));
            var banned = Assert.Single(all.AsArray(), row => row!["id"]!.GetValue<string>() == copies.Id)!;
            Assert.False(banned["seasonCompliant"]!.GetValue<bool>());
            Assert.Contains("本赛季禁用", banned["seasonComplianceReason"]!.GetValue<string>());
            var direct = await Json(client, Get($"/api/public-decks/{copies.Id}"));
            Assert.Equal(copies.Id, direct["id"]!.GetValue<string>());
            Assert.Equal("最多复制牌库", direct["deck"]!["name"]!.GetValue<string>());
            Assert.False(direct["seasonCompliant"]!.GetValue<bool>());
            using (var missing = await client.GetAsync("/api/public-decks/not-found"))
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            var compliant = await Json(client, Get("/api/public-decks?sort=copies&seasonCompliant=true"));
            Assert.DoesNotContain(compliant.AsArray(), row => row!["id"]!.GetValue<string>() == copies.Id);
            Assert.All(compliant.AsArray(), row => Assert.True(row!["seasonCompliant"]!.GetValue<bool>()));
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
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    private static string TempRoot()
        => Path.Combine(Path.GetTempPath(), $"l12-public-ranking-{Guid.NewGuid():N}");

    private static HttpRequestMessage Get(string path) => new(HttpMethod.Get, path);

    private static async Task<JsonNode> Json(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }
    }

    private static L12AdminAuditContext Context(string id) => new(id,
        Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite),
        Reason: "public deck regression", RequestMethod: "PUT", RequestPath: "/api/admin/operations/config");
}
