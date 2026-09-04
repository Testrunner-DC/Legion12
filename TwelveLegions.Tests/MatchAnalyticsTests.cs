using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MatchAnalyticsTests
{
    [Fact]
    public async Task LegacySchemaMigratesWithoutLosingRecordedMatches()
    {
        var directory = TestDirectory("migration");
        var path = Path.Combine(directory, "matches.db");
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE matches (
                    match_id TEXT PRIMARY KEY, room_code TEXT NOT NULL, seed INTEGER NOT NULL,
                    player_0 TEXT NOT NULL, player_1 TEXT NOT NULL, deck_0 TEXT NOT NULL, deck_1 TEXT NOT NULL,
                    started_utc TEXT NOT NULL, ended_utc TEXT, winner INTEGER, final_hash TEXT, error TEXT
                );
                CREATE TABLE match_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, match_id TEXT NOT NULL, sequence INTEGER NOT NULL,
                    received_utc TEXT NOT NULL, player_index INTEGER, command_json TEXT NOT NULL,
                    accepted INTEGER NOT NULL, error TEXT, revision INTEGER NOT NULL,
                    state_hash TEXT NOT NULL, state_json TEXT NOT NULL
                );
                INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                                    started_utc,ended_utc,winner,final_hash,error)
                VALUES('legacy-match','LEGACY',7,'旧甲','旧乙','旧牌库甲','旧牌库乙',
                       '2026-01-01T00:00:00.0000000+00:00','2026-01-01T00:05:00.0000000+00:00',0,'hash',NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync("legacy-match"));
        Assert.Equal("旧甲", detail.Match.Player0);
        var admin = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("legacy-match"));
        Assert.Equal("legacy-unavailable", admin.Participants[0].DeckSnapshotCoverage);
        Assert.Empty(admin.Participants[0].DeckCards);

        await using var inspect = new SqliteConnection($"Data Source={path}");
        await inspect.OpenAsync();
        foreach (var table in new[] { "match_participants", "match_deck_cards", "match_card_facts" })
        {
            var exists = inspect.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            exists.Parameters.AddWithValue("$name", table);
            Assert.Equal(1L, Convert.ToInt64(await exists.ExecuteScalarAsync()));
        }
        var preserved = inspect.CreateCommand();
        preserved.CommandText = "SELECT COUNT(*) FROM matches WHERE match_id='legacy-match';";
        Assert.Equal(1L, Convert.ToInt64(await preserved.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ImmutableDeckAndStructuredFactsAreIdempotentAndFreezeTiming()
    {
        var directory = TestDirectory("facts");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var game = new L12GameEngine(catalog, "fact-match", "FACT01", 31,
            ["甲", "乙"], decks, skipPreparation: true);
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await recorder.StartAsync(game, "ranked", "account-a", "account-b", decks);

        var selected = game.State.Players[0].Hand.Take(1).Select(card => card.InstanceId).ToList();
        var result = game.Handle(0, new L12Command("mulligan", CardInstanceIds: selected));
        Assert.True(result.Accepted);
        await recorder.AppendAsync(game, 1, 0, "{\"type\":\"mulligan\"}", result);
        await recorder.AppendAsync(game, 1, 0, "{\"type\":\"mulligan\"}", result);

        var detail = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("fact-match"));
        Assert.Empty(detail.Replay);
        Assert.All(detail.Participants, participant => Assert.Empty(participant.DeckCards));

        game.ConcludeByAuthority(0, "事实测试结束");
        await recorder.AppendAuthorityAsync(game, 2, "事实测试结束");
        await recorder.CompleteAsync(game);
        detail = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("fact-match"));
        Assert.NotEmpty(detail.Participants[0].DeckCards);
        Assert.Equal("exact", detail.Participants[0].DeckSnapshotCoverage);
        Assert.Contains(detail.CardFacts, fact => fact.Kind == "deck-included");
        var draw = Assert.Single(detail.CardFacts,
            fact => fact.Kind == "draw" && fact.CommandSequence == 1);
        Assert.Equal(1, draw.CommandSequence);
        Assert.Equal("Mulligan", draw.Phase);
        Assert.True(draw.Round > 0);

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var eventCount = connection.CreateCommand();
        eventCount.CommandText = "SELECT COUNT(*) FROM match_events WHERE match_id='fact-match' AND sequence=1;";
        Assert.Equal(1L, Convert.ToInt64(await eventCount.ExecuteScalarAsync()));
        var duplicateFacts = connection.CreateCommand();
        duplicateFacts.CommandText = """
            SELECT COUNT(*)-COUNT(DISTINCT fact_key) FROM match_card_facts WHERE match_id='fact-match';
            """;
        Assert.Equal(0L, Convert.ToInt64(await duplicateFacts.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task AdminPrivacySandboxExclusionAndStableAccountFilteringAreEnforced()
    {
        var directory = TestDirectory("privacy");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();

        var ongoing = new L12GameEngine(catalog, "ongoing-match", "ONGO01", 41,
            ["同名", "乙"], decks, skipPreparation: true);
        await recorder.StartAsync(ongoing, "ranked", "stable-a", "stable-b", decks);
        var ongoingDetail = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("ongoing-match"));
        Assert.True(ongoingDetail.Coverage.PrivateDuringActiveMatch);
        Assert.Empty(ongoingDetail.Replay);
        Assert.Empty(ongoingDetail.CardFacts);
        Assert.All(ongoingDetail.Participants, participant =>
        {
            Assert.Null(participant.DeckName);
            Assert.Empty(participant.DeckCards);
        });

        var completed = new L12GameEngine(catalog, "completed-match", "DONE01", 42,
            ["同名", "丙"], decks, skipPreparation: true);
        await recorder.StartAsync(completed, "friendly", "stable-c", "stable-d", decks);
        completed.ConcludeByAuthority(1, "筛选测试结束");
        await recorder.AppendAuthorityAsync(completed, 1, "筛选测试结束");
        await recorder.CompleteAsync(completed);

        var sandbox = new L12GameEngine(catalog, "sandbox-match", "SAND01", 43,
            ["同名", "丁"], decks, skipPreparation: true);
        await recorder.StartAsync(sandbox, "sandbox", "stable-a", "stable-e", decks);

        var stableA = await recorder.ListAdminMatchesForAccountAsync("stable-a", new L12AdminMatchQuery());
        Assert.Single(stableA.Items);
        Assert.Equal("ongoing-match", stableA.Items[0].MatchId);
        var stableC = await recorder.ListAdminMatchesForAccountAsync("stable-c", new L12AdminMatchQuery());
        Assert.Single(stableC.Items);
        Assert.Equal("completed-match", stableC.Items[0].MatchId);
        Assert.DoesNotContain(await recorder.ListMatchesForAccountAsync("stable-a", "同名"),
            match => match.MatchId == "completed-match");
        Assert.Single(await recorder.ListMatchesForAccountAsync("stable-c", "同名"));
        Assert.DoesNotContain((await recorder.ListAdminMatchesAsync(new L12AdminMatchQuery())).Items,
            match => match.MatchId == "sandbox-match");
    }

    [Fact]
    public async Task CardAnalyticsUsesNonIncludingBaselineAndFiltersBeforePagination()
    {
        var directory = TestDirectory("analytics");
        var catalog = Catalog();
        var includedDeck = catalog.DeckAt(0);
        var comparisonDeck = catalog.PresetDecks
            .First(deck => includedDeck.CardIds.Except(deck.CardIds, StringComparer.OrdinalIgnoreCase).Any());
        var targetCard = includedDeck.CardIds.Except(comparisonDeck.CardIds, StringComparer.OrdinalIgnoreCase).First();
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        for (var index = 0; index < 3; index++)
        {
            var game = new L12GameEngine(catalog, $"analytics-{index}", $"ANA{index:000}", 50 + index,
                [$"甲{index}", $"乙{index}"], [includedDeck, comparisonDeck], skipPreparation: true);
            await recorder.StartAsync(game, "ranked", $"included-{index}", $"control-{index}",
                [includedDeck, comparisonDeck]);
            game.ConcludeByAuthority(0, "分析样本结束");
            await recorder.AppendAuthorityAsync(game, 1, "分析样本结束");
            await recorder.CompleteAsync(game);
        }

        var page = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            Limit: 1, MinimumSampleSize: 1, Search: "not-the-id",
            CandidateCardIds: [targetCard]));
        var item = Assert.Single(page.Items);
        Assert.Equal(targetCard, item.CardId);
        Assert.Equal(3, item.SampleSize);
        Assert.Equal(6, item.EligibleSampleSize);
        Assert.Equal(3, item.IncludedMatches);
        Assert.Equal(1, item.WinRate);
        Assert.Equal(0, item.BaselineWinRate);
        Assert.Equal(1, item.WinRateDelta);
        Assert.Equal(1, page.Total);

        var detail = Assert.IsType<L12CardAnalyticsDetail>(await recorder.GetCardAnalyticsAsync(targetCard,
            new L12CardAnalyticsQuery(MinimumSampleSize: 1)));
        Assert.NotEmpty(detail.Breakdowns);
        Assert.Contains(detail.Breakdowns, breakdown => breakdown.Dimension == "opponent-master");
        Assert.Contains(detail.Breakdowns, breakdown => breakdown.Dimension == "rules-version");
        Assert.Equal(targetCard, detail.Summary.CardId);
    }

    [Fact]
    public async Task AdminApisRequirePermissionsAuditReadsAndKeepLegacyPlayerDtoStable()
    {
        var directory = TestDirectory("api");
        var catalog = Catalog();
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var player = platform.Register("api-player", "Password123!").Account!;
        var opponent = platform.Register("api-opponent", "Password123!").Account!;
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var game = new L12GameEngine(catalog, "api-match", "API001", 61,
            [player.Username, opponent.Username], decks, skipPreparation: true);
        await recorder.StartAsync(game, "ranked", player.Id, opponent.Id, decks);
        game.ConcludeByAuthority(0, "API 测试结束");
        await recorder.AppendAuthorityAsync(game, 1, "API 测试结束");
        await recorder.CompleteAsync(game);
        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var playerLogin = platform.Login(player.Username, "Password123!");
            using (var forbidden = Authorized(HttpMethod.Get, "/api/admin/matches", playerLogin.Token!))
            using (var response = await client.SendAsync(forbidden))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var forbidden = Authorized(HttpMethod.Get, "/api/admin/analytics/cards", playerLogin.Token!))
            using (var response = await client.SendAsync(forbidden))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var adminLogin = platform.Login("Admin", "L12master");
            using (var allowed = Authorized(HttpMethod.Get, "/api/admin/matches", adminLogin.Token!))
            using (var response = await client.SendAsync(allowed))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                var page = await response.Content.ReadFromJsonAsync<L12AdminMatchPage>();
                Assert.Contains(page!.Items, match => match.MatchId == "api-match");
            }
            var targetCard = decks[0].CardIds[0];
            var cardName = catalog.Cards[targetCard].NameZh;
            using (var search = Authorized(HttpMethod.Get,
                       $"/api/admin/analytics/cards?search={Uri.EscapeDataString(cardName)}&minimumSample=1",
                       adminLogin.Token!))
            using (var response = await client.SendAsync(search))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var page = await response.Content.ReadFromJsonAsync<L12CardAnalyticsPage>();
                Assert.Contains(page!.Items, item => item.CardId == targetCard);
            }
            using (var legacy = Authorized(HttpMethod.Get, "/api/matches/api-match", adminLogin.Token!))
            using (var response = await client.SendAsync(legacy))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            Assert.Contains(platform.AdminAudit(category: "match"), audit =>
                audit.ActorId == adminLogin.Account!.Id && audit.Action == "read-list"
                && audit.Permission == "admin.matches.read");
            Assert.Contains(platform.AdminAudit(category: "analytics"), audit =>
                audit.ActorId == adminLogin.Account!.Id && audit.Action == "read-card-list"
                && audit.Permission == "admin.analytics.read");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task AccountAnonymizationClearsStableIdentifiersFromAllMatchAnalyticsRows()
    {
        var directory = TestDirectory("anonymize");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var game = new L12GameEngine(catalog, "anonymous-match", "ANON01", 71,
            ["待删除", "保留"], decks, skipPreparation: true);
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await recorder.StartAsync(game, "ranked", "delete-account", "keep-account", decks);
        game.ConcludeByAuthority(0, "匿名化测试结束");
        await recorder.AppendAuthorityAsync(game, 1, "匿名化测试结束");
        await recorder.CompleteAsync(game);

        Assert.Equal(1, await recorder.AnonymizeAccountAsync("delete-account", "待删除", "已删除账号"));
        Assert.Empty((await recorder.ListAdminMatchesForAccountAsync("delete-account",
            new L12AdminMatchQuery())).Items);
        var detail = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("anonymous-match"));
        Assert.Null(detail.Participants[0].AccountId);
        Assert.Equal("已删除账号", detail.Participants[0].DisplayName);
        Assert.All(detail.CardFacts.Where(fact => fact.PlayerIndex == 0), fact => Assert.Null(fact.AccountId));

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var identifiers = connection.CreateCommand();
        identifiers.CommandText = """
            SELECT (SELECT COUNT(*) FROM matches WHERE account_0='delete-account' OR account_1='delete-account')
                 + (SELECT COUNT(*) FROM match_participants WHERE account_id='delete-account')
                 + (SELECT COUNT(*) FROM match_card_facts WHERE account_id='delete-account');
            """;
        Assert.Equal(0L, Convert.ToInt64(await identifiers.ExecuteScalarAsync()));
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static string TestDirectory(string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-match-analytics",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
