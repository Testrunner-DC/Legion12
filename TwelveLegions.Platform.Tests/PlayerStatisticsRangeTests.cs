using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlayerStatisticsRangeTests
{
    [Fact]
    public async Task StatisticsUseServerTimeAndCurrentSeasonForOverallAndMasterRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-stat-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var serverNow = new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
        var clock = serverNow;
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var decks = catalog.PresetDecks.GroupBy(item => item.MasterId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).Take(3).ToArray();
            Assert.Equal(3, decks.Length);
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var owner = store.Register("统计甲", "password-123");
            var opponent = store.Register("统计乙", "password-123").Account!;
            var empty = store.Register("空白者", "password-123");
            Assert.True(owner.Success, owner.Message);
            Assert.True(empty.Success, empty.Message);

            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => clock);
            await recorder.InitializeAsync();
            var basePolicy = store.CaptureOperationsPolicy();

            async Task Record(string id, DateTimeOffset endedAt, string seasonId,
                L12PresetDeckDefinition ownerDeck, int winner)
            {
                clock = endedAt.AddMinutes(-12);
                var policy = basePolicy with
                {
                    Season = new L12SeasonConfig(seasonId, seasonId, "active", null, null),
                };
                var game = new L12GameEngine(catalog, id, "STAT", 42,
                    [owner.Account!.Username, opponent.Username], [ownerDeck, decks[2]],
                    skipPreparation: true, operationsPolicy: policy);
                await recorder.StartAsync(game, "ranked", owner.Account.Id, opponent.Id,
                    [ownerDeck, decks[2]]);
                game.State.FirstPlayer = 0;
                game.State.Phase = L12Phase.GameOver;
                game.State.Winner = winner;
                clock = endedAt;
                await recorder.CompleteAsync(game);
            }

            await Record("stat-current-1d", serverNow.AddDays(-1), "S01", decks[0], 0);
            await Record("stat-previous-2d", serverNow.AddDays(-2), "S00", decks[1], 1);
            await Record("stat-current-7d", serverNow.AddDays(-7), "S01", decks[1], 0);
            await Record("stat-current-10d", serverNow.AddDays(-10), "S01", decks[0], 1);
            await Record("stat-current-40d", serverNow.AddDays(-40), "S01", decks[0], 0);
            clock = serverNow;

            var currentSeason = new L12SeasonConfig("S01", "S01", "active",
                serverNow.AddDays(-30), serverNow.AddDays(10));
            var sevenDays = await recorder.PlayerStatisticsAsync(owner.Account!.Id, owner.Account.Username,
                range: "7d", currentSeason: currentSeason);
            Assert.Equal("7d", sevenDays.Range);
            Assert.Equal(serverNow.AddDays(-7), sevenDays.FromUtc);
            Assert.Equal(serverNow, sevenDays.UntilUtc);
            Assert.Equal(3, sevenDays.Overall.Games);
            Assert.Equal(sevenDays.Overall.Games, sevenDays.Masters.Sum(item => item.Overall.Games));

            var thirtyDays = await recorder.PlayerStatisticsAsync(owner.Account.Id, owner.Account.Username,
                range: "30d", currentSeason: currentSeason);
            Assert.Equal(4, thirtyDays.Overall.Games);
            Assert.Equal(thirtyDays.Overall.Games, thirtyDays.Masters.Sum(item => item.Overall.Games));

            var season = await recorder.PlayerStatisticsAsync(owner.Account.Id, owner.Account.Username,
                range: "season", currentSeason: currentSeason);
            Assert.Equal("S01", season.SeasonId);
            Assert.Equal(serverNow.AddDays(-30), season.FromUtc);
            Assert.Equal(3, season.Overall.Games);
            Assert.Equal(season.Overall.Games, season.Masters.Sum(item => item.Overall.Games));

            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);

            var apiSevenDays = await client.GetFromJsonAsync<L12PlayerStatisticsView>(
                "/api/me/statistics?range=7d");
            Assert.NotNull(apiSevenDays);
            Assert.Equal(3, apiSevenDays.Overall.Games);
            Assert.Equal(apiSevenDays.Overall.Games,
                apiSevenDays.Masters.Sum(item => item.Overall.Games));

            var apiSeason = await client.GetFromJsonAsync<L12PlayerStatisticsView>("/api/me/statistics");
            Assert.NotNull(apiSeason);
            Assert.Equal("season", apiSeason.Range);
            Assert.Equal("S01", apiSeason.SeasonId);
            Assert.Equal(4, apiSeason.Overall.Games);

            using var invalid = await client.GetAsync("/api/me/statistics?range=forever");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", empty.Token);
            var noData = await client.GetFromJsonAsync<L12PlayerStatisticsView>(
                "/api/me/statistics?range=30d");
            Assert.NotNull(noData);
            Assert.Equal(0, noData.Overall.Games);
            Assert.Empty(noData.Masters);
        }
        finally
        {
            if (server is not null) await server.StopAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
        }
    }
}
