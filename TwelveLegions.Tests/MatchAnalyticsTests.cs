using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

public sealed class MatchAnalyticsTests
{
    private readonly ITestOutputHelper _output;

    public MatchAnalyticsTests(ITestOutputHelper output) => _output = output;

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
        Assert.Empty(detail.Replay);
        Assert.NotEmpty(detail.Participants[0].DeckCards);
        Assert.Equal("exact", detail.Participants[0].DeckSnapshotCoverage);
        Assert.Contains(detail.CardFacts, fact => fact.Kind == "deck-included");
        var draw = Assert.Single(detail.CardFacts,
            fact => fact.Kind == "draw" && fact.CommandSequence == 1);
        Assert.Equal(1, draw.CommandSequence);
        Assert.Equal("Mulligan", draw.Phase);
        Assert.True(draw.Round > 0);
        var replayDetail = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("fact-match", includeReplay: true));
        Assert.NotEmpty(replayDetail.Replay);

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
        Assert.NotNull(item.WinRateDeltaConfidence);
        Assert.True(item.WinRateDeltaConfidence.Low <= 0
                    && item.WinRateDeltaConfidence.High >= 0);
        Assert.Equal(1, page.Total);

        var oriented = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: [targetCard], MasterId: includedDeck.MasterId,
            OpponentMasterId: comparisonDeck.MasterId));
        Assert.Equal(3, Assert.Single(oriented.Items, item => item.CardId == targetCard).SampleSize);
        Assert.Equal(3, oriented.Summary.SampleSize);
        var reversed = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: [targetCard], MasterId: comparisonDeck.MasterId,
            OpponentMasterId: includedDeck.MasterId));
        Assert.DoesNotContain(reversed.Items, item => item.CardId == targetCard);

        var detail = Assert.IsType<L12CardAnalyticsDetail>(await recorder.GetCardAnalyticsAsync(targetCard,
            new L12CardAnalyticsQuery(MinimumSampleSize: 1, MasterId: includedDeck.MasterId,
                OpponentMasterId: comparisonDeck.MasterId)));
        Assert.NotEmpty(detail.Breakdowns);
        Assert.Contains(detail.Breakdowns, breakdown => breakdown.Dimension == "opponent-master");
        Assert.Contains(detail.Breakdowns, breakdown => breakdown.Dimension == "rules-version");
        Assert.Equal(targetCard, detail.Summary.CardId);
    }

    [Fact]
    public async Task CardAnalyticsUsesParticipantSamplesNullableBaselinesAndMetricCoverage()
    {
        var directory = TestDirectory("participant-units");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var deck = catalog.DeckAt(0);
        var targetCard = deck.CardIds[0];
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var game = new L12GameEngine(catalog, "participant-units", "UNIT01", 55,
            ["甲", "乙"], [deck, deck], skipPreparation: true);
        await recorder.StartAsync(game, "ranked", "unit-a", "unit-b", [deck, deck]);
        game.ConcludeByAuthority(0, "参赛方统计口径测试结束");
        await recorder.AppendAuthorityAsync(game, 1, "参赛方统计口径测试结束");
        await recorder.CompleteAsync(game);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            for (var playerIndex = 0; playerIndex < 2; playerIndex++)
            {
                await InsertCardFactAsync(connection, "participant-units", $"draw-{playerIndex}",
                    "draw", playerIndex, targetCard, turn: 2, coverage: "exact");
                await InsertCardFactAsync(connection, "participant-units", $"play-{playerIndex}",
                    "play", playerIndex, targetCard, turn: 3, coverage: "exact");
                await InsertCardFactAsync(connection, "participant-units", $"activate-{playerIndex}",
                    "activate", playerIndex, targetCard, turn: 3, coverage: "exact");
                await InsertCardFactAsync(connection, "participant-units", $"resolve-{playerIndex}",
                    "resolve", playerIndex, targetCard, turn: 3,
                    coverage: playerIndex == 0 ? "exact" : "partial");
            }
        }

        var page = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: [targetCard]));
        var item = Assert.Single(page.Items, candidate => candidate.CardId == targetCard);
        Assert.Equal("participant", page.Summary.StatisticalUnit);
        Assert.Equal(2, item.SampleSize);
        Assert.Equal(1, item.IncludedMatches);
        var expectedQuantity = deck.CardIds.Count(cardId => cardId == targetCard);
        Assert.Equal(expectedQuantity, item.AverageQuantity);
        Assert.Equal(2, item.DrawnSamples);
        Assert.Equal(2, item.PlayedSamples);
        Assert.Equal(2, item.ActivatedSamples);
        Assert.Equal(1, item.SettledSamples);
        Assert.Equal(1, item.ResolvedSamples);
        Assert.Equal(2, item.ResolvedCount);
        Assert.Null(item.BaselineWinRate);
        Assert.Null(item.BaselineWinRateConfidence);
        Assert.Null(item.WinRateDelta);
        Assert.Null(item.WinRateDeltaConfidence);
        Assert.True(item.WinRateConfidence.Low < item.WinRate
                    && item.WinRateConfidence.High > item.WinRate);
        Assert.Contains(item.Coverage.Metrics, metric => metric.Metric == "draw"
            && metric.Unit == "participant" && metric.ObservedSamples == 2
            && metric.ExactFacts >= metric.ObservedSamples);
        Assert.Contains(item.Coverage.Metrics, metric => metric.Metric == "settlement"
            && metric.ObservedSamples == 1 && metric.PartialFacts == 1);

        var detail = Assert.IsType<L12CardAnalyticsDetail>(await recorder.GetCardAnalyticsAsync(targetCard,
            new L12CardAnalyticsQuery(MinimumSampleSize: 1)));
        Assert.Contains(detail.TurnDistribution, bucket => bucket.Turn == 0
            && bucket.FirstDrawSamples == 2 && bucket.FirstPlaySamples == 0);
        Assert.Contains(detail.TurnDistribution, bucket => bucket.Turn == 3
            && bucket.FirstDrawSamples == 0 && bucket.FirstPlaySamples == 2);
        Assert.Contains(detail.QuantityDistribution, bucket => bucket.Quantity == expectedQuantity
            && bucket.SampleSize == 2);
    }

    [Fact]
    public async Task CardAnalyticsFiltersEveryAdvertisedSliceAndOnlyReturnsCompletedRecentMatches()
    {
        var directory = TestDirectory("all-slices");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var includedDeck = catalog.DeckAt(0);
        var opponentDeck = catalog.PresetDecks.First(deck => deck.MasterId != includedDeck.MasterId);
        var targetCard = includedDeck.CardIds[0];
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();

        var completed = new L12GameEngine(catalog, "slice-completed", "SLC001", 56,
            ["甲", "乙"], [includedDeck, opponentDeck], skipPreparation: true);
        await recorder.StartAsync(completed, "ranked", "slice-a", "slice-b",
            [includedDeck, opponentDeck]);
        completed.ConcludeByAuthority(0, "切片测试结束");
        await recorder.AppendAuthorityAsync(completed, 1, "切片测试结束");
        await recorder.CompleteAsync(completed);

        var ongoing = new L12GameEngine(catalog, "slice-ongoing", "SLC002", 57,
            ["不应泄露甲", "不应泄露乙"], [includedDeck, opponentDeck], skipPreparation: true);
        await recorder.StartAsync(ongoing, "ranked", "private-a", "private-b",
            [includedDeck, opponentDeck]);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE matches SET first_player=0,rules_version='rules-v-test',season_id='season-test',
                    started_utc='2026-09-06T12:00:00.0000000+00:00'
                WHERE match_id IN ('slice-completed','slice-ongoing');
                """;
            await update.ExecuteNonQueryAsync();
        }

        var filtered = new L12CardAnalyticsQuery(MinimumSampleSize: 1,
            CandidateCardIds: [targetCard], ModeId: "ranked", MasterId: includedDeck.MasterId,
            OpponentMasterId: opponentDeck.MasterId, Initiative: "first",
            RulesVersion: "rules-v-test", SeasonId: "season-test",
            FromUtc: DateTimeOffset.Parse("2026-09-06T00:00:00Z"),
            ToUtc: DateTimeOffset.Parse("2026-09-07T00:00:00Z"));
        var page = await recorder.ListCardAnalyticsAsync(filtered);
        Assert.Single(page.Items, item => item.CardId == targetCard);
        Assert.Equal(1, page.Summary.EligibleMatches);
        Assert.Equal(1, page.Summary.SampleSize);

        var detail = Assert.IsType<L12CardAnalyticsDetail>(
            await recorder.GetCardAnalyticsAsync(targetCard, filtered));
        var recent = Assert.Single(detail.RecentMatches);
        Assert.Equal("slice-completed", recent.MatchId);
        Assert.Equal("completed", recent.Status);
        Assert.All(recent.Players, player =>
        {
            Assert.Null(player.AccountId);
            Assert.Null(player.DeckName);
            Assert.DoesNotContain("不应泄露", player.DisplayName, StringComparison.Ordinal);
        });
        Assert.Contains(detail.Breakdowns, row => row.Dimension == "season"
            && row.Value == "season-test");
        Assert.Contains(detail.Matchups, row => row.MasterId == includedDeck.MasterId
            && row.OpponentMasterId == opponentDeck.MasterId);

        var analyticsOnly = Assert.IsType<L12CardAnalyticsDetail>(
            await recorder.GetCardAnalyticsAsync(targetCard, filtered, includeRecentMatches: false));
        Assert.Empty(analyticsOnly.RecentMatches);

        Assert.Null(await recorder.GetCardAnalyticsAsync(targetCard,
            filtered with { Initiative = "second" }));
        Assert.Null(await recorder.GetCardAnalyticsAsync(targetCard,
            filtered with { RulesVersion = "other-rules" }));
        Assert.Null(await recorder.GetCardAnalyticsAsync(targetCard,
            filtered with { SeasonId = "other-season" }));
    }

    [Fact]
    public async Task CompletedDrawStatusAndInclusiveDateOnlyEndAreConsistent()
    {
        var directory = TestDirectory("status-and-date");
        var catalog = Catalog();
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var matchPath = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(matchPath);
        await recorder.InitializeAsync();
        var draw = new L12GameEngine(catalog, "draw-match", "DRAW01", 58,
            ["平局甲", "平局乙"], decks, skipPreparation: true);
        await recorder.StartAsync(draw, "ranked", "draw-a", "draw-b", decks);
        draw.ConcludeAgreedDrawByAuthority("双方同意平局");
        await recorder.AppendAuthorityAsync(draw, 1, "双方同意平局");
        await recorder.CompleteAsync(draw);
        var invalid = new L12GameEngine(catalog, "invalid-match", "BAD001", 60,
            ["无效甲", "无效乙"], decks, skipPreparation: true);
        await recorder.StartAsync(invalid, "ranked", "invalid-a", "invalid-b", decks);
        invalid.ConcludeByAuthority(null, "服务器维护开始，当前对局无效");
        await recorder.AppendAuthorityAsync(invalid, 1, "服务器维护开始，当前对局无效");
        await recorder.CompleteAsync(invalid);
        var decisive = new L12GameEngine(catalog, "date-boundary-match", "DATE01", 59,
            ["边界甲", "边界乙"], decks, skipPreparation: true);
        await recorder.StartAsync(decisive, "ranked", "date-a", "date-b", decks);
        decisive.ConcludeByAuthority(0, "日期边界测试结束");
        await recorder.AppendAuthorityAsync(decisive, 1, "日期边界测试结束");
        await recorder.CompleteAsync(decisive);
        await using (var connection = new SqliteConnection($"Data Source={matchPath}"))
        {
            await connection.OpenAsync();
            var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE matches SET started_utc='2026-09-06T23:59:59.0000000+00:00'
                WHERE match_id IN ('draw-match','date-boundary-match');
                """;
            await update.ExecuteNonQueryAsync();
        }

        var summaries = await recorder.ListAdminMatchesAsync(new L12AdminMatchQuery(Status: "completed"));
        var summary = Assert.Single(summaries.Items, item => item.MatchId == "draw-match");
        Assert.Equal("completed", summary.Status);
        Assert.All(summary.Players, player => Assert.Equal("draw", player.Result));
        Assert.DoesNotContain(summaries.Items, item => item.MatchId == "invalid-match");
        var invalidSummaries = await recorder.ListAdminMatchesAsync(
            new L12AdminMatchQuery(Status: "invalid"));
        var invalidSummary = Assert.Single(invalidSummaries.Items,
            item => item.MatchId == "invalid-match");
        Assert.Equal("invalid", invalidSummary.Status);
        Assert.All(invalidSummary.Players, player => Assert.Equal("invalid", player.Result));

        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var adminLogin = platform.Login("Admin", "L12master");
            using var request = Authorized(HttpMethod.Get,
                "/api/admin/analytics/cards?minimumSample=1&to=2026-09-06", adminLogin.Token!);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var analytics = await response.Content.ReadFromJsonAsync<L12CardAnalyticsPage>();
            Assert.Equal(1, analytics!.Summary.EligibleMatches);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task AnalyticsQueriesUseDedicatedIndexesAndKeepResponsesBounded()
    {
        var directory = TestDirectory("query-bounds");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var targetCard = catalog.DeckAt(0).CardIds[0];
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var seed = connection.CreateCommand();
            seed.CommandText = """
                WITH RECURSIVE sample(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM sample WHERE n<50000)
                INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                    started_utc,ended_utc,winner,mode_id,rules_version,season_id,first_player,fact_schema_version)
                SELECT printf('perf-%04d',n),'PERF',n,'甲','乙','甲牌库','乙牌库',
                    printf('2026-09-%02dT12:00:00.0000000+00:00',1+(n%7)),
                    printf('2026-09-%02dT12:10:00.0000000+00:00',1+(n%7)),n%2,'ranked',
                    printf('rules-%03d',n%250),printf('season-%03d',n%250),n%2,1
                FROM sample;

                WITH RECURSIVE sample(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM sample WHERE n<50000),
                players(player_index) AS (VALUES(0),(1))
                INSERT INTO match_participants(match_id,player_index,display_name,master_id,master_name,
                    deck_name,deck_snapshot_coverage)
                SELECT printf('perf-%04d',n),player_index,printf('玩家-%04d-%d',n,player_index),
                    printf('MASTER-%03d',n%250),printf('主宰-%03d',n%250),'性能牌库','exact'
                FROM sample CROSS JOIN players;

                WITH RECURSIVE sample(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM sample WHERE n<50000),
                players(player_index) AS (VALUES(0),(1))
                INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
                SELECT printf('perf-%04d',n),player_index,'main',$card,1 FROM sample CROSS JOIN players;

                WITH RECURSIVE sample(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM sample WHERE n<50000),
                players(player_index) AS (VALUES(0),(1))
                INSERT INTO match_card_facts(match_id,fact_key,command_sequence,revision,round,turn,
                    phase,occurred_utc,kind,player_index,card_id,coverage,metadata_json)
                SELECT printf('perf-%04d',n),printf('draw-%d',player_index),1,1,1,n%200,'Main',
                    '2026-09-01T12:01:00.0000000+00:00','draw',player_index,$card,'exact','{}'
                FROM sample CROSS JOIN players;
                """;
            seed.Parameters.AddWithValue("$card", targetCard);
            await seed.ExecuteNonQueryAsync();

            var indexes = connection.CreateCommand();
            indexes.CommandText = """
                SELECT name FROM sqlite_master WHERE type='index' AND name IN (
                    'ix_matches_analytics_scope','ix_match_deck_cards_analytics_owner',
                    'ix_match_card_facts_analytics_owner') ORDER BY name;
                """;
            var names = new List<string>();
            await using var reader = await indexes.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            Assert.Equal(3, names.Count);

            var counts = connection.CreateCommand();
            counts.CommandText = """
                SELECT (SELECT COUNT(*) FROM match_participants WHERE match_id LIKE 'perf-%'),
                       (SELECT COUNT(*) FROM match_card_facts WHERE match_id LIKE 'perf-%');
                """;
            await using (var countReader = await counts.ExecuteReaderAsync())
            {
                Assert.True(await countReader.ReadAsync());
                Assert.Equal(100_000, countReader.GetInt64(0));
                Assert.Equal(100_000, countReader.GetInt64(1));
            }

            var explain = connection.CreateCommand();
            explain.CommandText = """
                EXPLAIN QUERY PLAN
                SELECT m.match_id,p.player_index
                FROM matches m
                JOIN match_participants p ON p.match_id=m.match_id
                WHERE m.mode_id='ranked' AND m.ended_utc IS NOT NULL AND m.error IS NULL
                  AND m.winner IN (0,1)
                  AND m.started_utc>='2026-09-01T00:00:00.0000000+00:00'
                  AND m.started_utc<'2026-10-01T00:00:00.0000000+00:00';
                """;
            var plan = new List<string>();
            await using (var planReader = await explain.ExecuteReaderAsync())
                while (await planReader.ReadAsync()) plan.Add(planReader.GetString(3));
            Assert.Contains(plan, step => step.Contains("ix_matches_analytics_scope",
                StringComparison.OrdinalIgnoreCase));

            var factExplain = connection.CreateCommand();
            factExplain.CommandText = """
                EXPLAIN QUERY PLAN
                SELECT card_id,MIN(turn),SUM(CASE WHEN coverage='exact' THEN 1 ELSE 0 END)
                FROM match_card_facts
                WHERE match_id='perf-0001' AND player_index=0 AND card_id IS NOT NULL
                GROUP BY card_id;
                """;
            plan.Clear();
            await using (var planReader = await factExplain.ExecuteReaderAsync())
                while (await planReader.ReadAsync()) plan.Add(planReader.GetString(3));
            Assert.Contains(plan, step => step.Contains("ix_match_card_facts_analytics_owner",
                StringComparison.OrdinalIgnoreCase));
        }

        var performanceQuery = new L12CardAnalyticsQuery(MinimumSampleSize: 1,
            ModeId: "ranked", FromUtc: DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            ToUtc: DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var page = await recorder.ListCardAnalyticsAsync(performanceQuery with
            { Limit = 500, Search = targetCard });
        var detail = Assert.IsType<L12CardAnalyticsDetail>(await recorder.GetCardAnalyticsAsync(
            targetCard, performanceQuery));
        timer.Stop();
        _output.WriteLine("合成 5 万场／10 万参赛方／10 万事实：列表 + 详情 {0:F3} 秒",
            timer.Elapsed.TotalSeconds);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10),
            $"合成 5 万场／10 万参赛方分析查询耗时 {timer.Elapsed}");
        Assert.True(page.Items.Count <= 200);
        Assert.True(detail.Breakdowns.Count <= 6 * 200);
        Assert.True(detail.QuantityDistribution.Count <= 20);
        Assert.True(detail.TurnDistribution.Count <= 200);
        Assert.True(detail.Matchups.Count <= 200);
        Assert.True(detail.RecentMatches.Count <= 20);
    }

    [Fact]
    public async Task HugeReplayUsesStableBoundedPagesWithoutBlockingNormalRankedSnapshot()
    {
        var directory = TestDirectory("huge-replay");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var game = new L12GameEngine(catalog, "huge-replay", "HUGE01", 59,
            ["甲", "乙"], decks, skipPreparation: true);
        await recorder.StartAsync(game, "ranked", "huge-a", "huge-b", decks);
        game.ConcludeByAuthority(0, "超大回放测试结束");
        await recorder.AppendAuthorityAsync(game, 1, "超大回放测试结束");
        await recorder.CompleteAsync(game);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var append = connection.CreateCommand();
            append.CommandText = """
                WITH RECURSIVE replay(sequence) AS (
                    VALUES(100) UNION ALL SELECT sequence+1 FROM replay WHERE sequence<1700
                )
                INSERT INTO match_events(match_id,sequence,received_utc,player_index,command_json,accepted,
                                         error,revision,state_hash,state_json)
                SELECT 'huge-replay',sequence,'2026-09-06T00:00:00.0000000+00:00',0,'{}',1,NULL,
                       sequence,'hash',CASE WHEN sequence=1700 THEN 'invalid-json-must-not-be-read' ELSE '{}' END
                FROM replay;
                """;
            await append.ExecuteNonQueryAsync();
        }

        var snapshot = Assert.IsType<L12AdminMatchDetail>(await recorder.GetAdminMatchAsync("huge-replay"));
        Assert.Equal("ranked", snapshot.Summary.ModeId);
        Assert.NotEmpty(snapshot.Participants[0].DeckCards);
        Assert.Empty(snapshot.Replay);

        var first = Assert.IsType<L12AdminReplayPage>(
            await recorder.GetAdminReplayPageAsync("huge-replay", null, 17));
        Assert.Equal(17, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.True(first.TotalCommands > MatchRecorder.MaximumInlineReplayCommands);
        var second = Assert.IsType<L12AdminReplayPage>(
            await recorder.GetAdminReplayPageAsync("huge-replay", first.NextCursor, 17));
        Assert.True(second.Items[0].Sequence > first.Items[^1].Sequence);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            recorder.GetAdminReplayPageAsync("different-match", first.NextCursor, 17));
        await Assert.ThrowsAsync<L12ReplayPayloadTooLargeException>(() =>
            recorder.GetAdminMatchAsync("huge-replay", includeReplay: true));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            recorder.GetAdminReplayPageAsync("huge-replay", null, 17, cancelled.Token));
    }

    [Fact]
    public async Task ReplayPageByteCapReturnsCursorBeforeTakeLimit()
    {
        var directory = TestDirectory("byte-capped-replay");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var game = new L12GameEngine(catalog, "byte-capped-replay", "BYTE01", 60,
            ["甲", "乙"], decks, skipPreparation: true);
        await recorder.StartAsync(game, "ranked", "byte-a", "byte-b", decks);
        game.ConcludeByAuthority(0, "字节分页测试结束");
        await recorder.AppendAuthorityAsync(game, 1, "字节分页测试结束");
        await recorder.CompleteAsync(game);

        var largeState = JsonSerializer.Serialize(new string('x', 2_200_000));
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await InsertReplayEventAsync(connection, "byte-capped-replay", 100, largeState);
            await InsertReplayEventAsync(connection, "byte-capped-replay", 101, largeState);
            await InsertReplayEventAsync(connection, "byte-capped-replay", 102, "{}");
        }

        var first = Assert.IsType<L12AdminReplayPage>(
            await recorder.GetAdminReplayPageAsync("byte-capped-replay", null, 100));
        Assert.True(first.Items.Count < first.Limit);
        Assert.Equal(100, first.Items[^1].Sequence);
        Assert.True(first.PageBytes <= MatchRecorder.MaximumReplayPageBytes);
        Assert.NotNull(first.NextCursor);
        var second = Assert.IsType<L12AdminReplayPage>(
            await recorder.GetAdminReplayPageAsync("byte-capped-replay", first.NextCursor, 100));
        Assert.Equal(101, second.Items[0].Sequence);
        Assert.Contains(second.Items, item => item.Sequence == 102);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task AdminApisRequirePermissionsAuditReadsAndKeepLegacyPlayerDtoStable()
    {
        var directory = TestDirectory("api");
        var catalog = Catalog();
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var player = platform.Register("api-player", "Password123!").Account!;
        var opponent = platform.Register("tapiop030a1", "Password123!").Account!;
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var matchPath = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(matchPath);
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
            using (var detailRequest = Authorized(HttpMethod.Get, "/api/admin/matches/api-match", adminLogin.Token!))
            using (var response = await client.SendAsync(detailRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var detail = await response.Content.ReadFromJsonAsync<L12AdminMatchDetail>();
                Assert.Empty(detail!.Replay);
            }
            using (var replayRequest = Authorized(HttpMethod.Get, "/api/admin/matches/api-match?includeReplay=true", adminLogin.Token!))
            using (var response = await client.SendAsync(replayRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var detail = await response.Content.ReadFromJsonAsync<L12AdminMatchDetail>();
                Assert.NotEmpty(detail!.Replay);
            }
            await using (var connection = new SqliteConnection($"Data Source={matchPath}"))
            {
                await connection.OpenAsync();
                await InsertReplayEventAsync(connection, "api-match", 10_000,
                    new string('x', checked((int)MatchRecorder.MaximumReplayPageBytes + 1)));
            }
            string oversizedCursor;
            using (var pageRequest = Authorized(HttpMethod.Get,
                       "/api/admin/matches/api-match/replay?limit=100", adminLogin.Token!))
            using (var response = await client.SendAsync(pageRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var page = await response.Content.ReadFromJsonAsync<L12AdminReplayPage>();
                Assert.NotEmpty(page!.Items);
                oversizedCursor = Assert.IsType<string>(page.NextCursor);
            }
            using (var oversizedRequest = Authorized(HttpMethod.Get,
                       $"/api/admin/matches/api-match/replay?limit=100&cursor={Uri.EscapeDataString(oversizedCursor)}",
                       adminLogin.Token!))
            using (var response = await client.SendAsync(oversizedRequest))
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
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

            await using (var connection = new SqliteConnection($"Data Source={matchPath}"))
            {
                await connection.OpenAsync();
                var purge = connection.CreateCommand();
                purge.CommandText = "DELETE FROM match_events WHERE match_id='api-match';";
                Assert.True(await purge.ExecuteNonQueryAsync() > 0);
            }
            using (var playerReplay = Authorized(HttpMethod.Get, "/api/matches/api-match", playerLogin.Token!))
            using (var response = await client.SendAsync(playerReplay))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Equal("replay_payload_expired",
                    (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }
            using (var playerSummaries = Authorized(HttpMethod.Get, "/api/matches", playerLogin.Token!))
            using (var response = await client.SendAsync(playerSummaries))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var summaries = await response.Content.ReadFromJsonAsync<L12MatchSummary[]>();
                Assert.Contains(summaries!, summary => summary.MatchId == "api-match"
                    && summary.CommandCount == 0 && summary.EndedUtc is not null);
            }
            using (var adminReplay = Authorized(HttpMethod.Get,
                       "/api/admin/matches/api-match/replay", adminLogin.Token!))
            using (var response = await client.SendAsync(adminReplay))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Equal("replay_payload_expired",
                    (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }
            using (var adminInlineReplay = Authorized(HttpMethod.Get,
                       "/api/admin/matches/api-match?includeReplay=true", adminLogin.Token!))
            using (var response = await client.SendAsync(adminInlineReplay))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Equal("replay_payload_expired",
                    (await response.Content.ReadFromJsonAsync<L12ApiError>())!.Code);
            }
            using (var preservedSummary = Authorized(HttpMethod.Get,
                       "/api/admin/matches/api-match", adminLogin.Token!))
            using (var response = await client.SendAsync(preservedSummary))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var detail = await response.Content.ReadFromJsonAsync<L12AdminMatchDetail>();
                Assert.Equal(0, detail!.Summary.CommandCount);
            }

            Assert.Contains(platform.AdminAudit(category: "match"), audit =>
                audit.ActorId == adminLogin.Account!.Id && audit.Action == "read-list"
                && audit.Permission == "admin.matches.read");
            Assert.Contains(platform.AdminAudit(category: "match"), audit =>
                audit.ActorId == adminLogin.Account!.Id && audit.Action == "read-replay"
                && audit.Permission == "admin.matches.read" && audit.Target == "api-match");
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

    private static async Task InsertReplayEventAsync(SqliteConnection connection, string matchId,
        long sequence, string stateJson)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO match_events(match_id,sequence,received_utc,player_index,command_json,accepted,
                                     error,revision,state_hash,state_json)
            VALUES($match,$sequence,'2026-09-06T00:00:00.0000000+00:00',0,'{}',1,NULL,$sequence,'hash',$state);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$state", stateJson);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCardFactAsync(SqliteConnection connection, string matchId,
        string factKey, string kind, int playerIndex, string cardId, int turn, string coverage)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO match_card_facts(
                match_id,fact_key,command_sequence,revision,round,turn,phase,occurred_utc,kind,
                player_index,card_id,coverage,metadata_json)
            VALUES($match,$key,99,99,1,$turn,'Main','2026-09-06T12:00:00.0000000+00:00',
                   $kind,$player,$card,$coverage,'{}');
            """;
        command.Parameters.AddWithValue("$match", matchId);
        command.Parameters.AddWithValue("$key", factKey);
        command.Parameters.AddWithValue("$turn", turn);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$player", playerIndex);
        command.Parameters.AddWithValue("$card", cardId);
        command.Parameters.AddWithValue("$coverage", coverage);
        await command.ExecuteNonQueryAsync();
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
