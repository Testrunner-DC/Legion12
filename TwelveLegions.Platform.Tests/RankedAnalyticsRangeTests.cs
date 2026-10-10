using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedAnalyticsRangeTests
{
    [Fact]
    public async Task RawWindowLimitFailsClosedEvenWhenDamagedPayloadShrinksVisibleRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranking-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var database = Path.Combine(root, "matches.db");
            await using var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            var now = DateTimeOffset.UtcNow;

            async Task Record(string id, DateTimeOffset started)
            {
                var engine = new L12GameEngine(catalog, id, "QA", 77, ["甲", "乙"], [0, 1],
                    skipPreparation: true);
                await recorder.StartAsync(engine.State, "ranked", "first", "second");
                engine.ConcludeByAuthority(0, "测试结束");
                await recorder.CompleteAsync(engine);
                await using var connection = new SqliteConnection($"Data Source={database}");
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE matches SET started_utc=$start,ended_utc=$end,storage_version=2 WHERE match_id=$id";
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$start", started.ToString("O"));
                command.Parameters.AddWithValue("$end", started.AddMinutes(1).ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            await Record("recent-valid", now.AddDays(-1));
            await Record("older-damaged", now.AddDays(-10));
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc) VALUES('older-damaged','{}','bad-hash','applied',$now)";
                command.Parameters.AddWithValue("$now", now.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            Assert.Single(await recorder.ListRankedAnalyticsMatchesAsync(2));
            Assert.False(await recorder.IsRankedAnalyticsWindowCompleteAsync(now.AddDays(-30), 2));
            Assert.False(await recorder.IsRankedAnalyticsWindowCompleteAsync(now.AddDays(-10), 2));
            Assert.True(await recorder.IsRankedAnalyticsWindowCompleteAsync(now.AddDays(-7), 2));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void RollingPlayerBoardUsesWindowMatchesAndVerifiedSettlementsButCurrentIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranking-players-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var masters = catalog.PresetDecks.Select(deck => deck.MasterId).Distinct().Take(2).ToArray();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("windowone", "password-123").Account!;
            var second = store.Register("windowtwo", "password-123").Account!;
            var noFact = store.Register("windownone", "password-123").Account!;
            var noFactOpponent = store.Register("windowother", "password-123").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");

            var source = new List<L12RankingMatch>();
            var priorAt = DateTimeOffset.UtcNow.AddDays(-2);
            L12RankingMatch Match(string id, string account0, string account1, DateTimeOffset at) =>
                new(id, "first", "second", at.ToString("O"), at.ToString("O"), 0,
                    "", "", 0, masters[0], masters[1], account0, account1);
            const string olderId = "older-season-thirty-days";
            store.SettleRankedMatch(olderId, first.Id, second.Id, 1);
            source.Add(Match(olderId, first.Id, second.Id, priorAt.AddDays(-18)) with { Winner = 1 });
            for (var index = 0; index < 5; index++)
            {
                var id = $"prior-season-{index}";
                store.SettleRankedMatch(id, first.Id, second.Id, 0);
                source.Add(Match(id, first.Id, second.Id, priorAt.AddMinutes(index)));
            }
            var seasons = store.SeasonCatalog(admin);
            var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", null, null, seasons.Next.Configuration),
                seasons.Next.Revision, "test player window cutover",
                new L12AdminAuditContext("window-board-draft"));
            store.ActivateSeason(admin, draft.DefinitionId, seasons.Current.Revision, draft.Revision,
                "test player window cutover", new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId,
                    0, 0, 0, 0), new L12AdminAuditContext("window-board-activate"));
            const string currentId = "current-season-one";
            store.SettleRankedMatch(currentId, first.Id, second.Id, 0);
            source.Add(Match(currentId, first.Id, second.Id, DateTimeOffset.UtcNow));
            source.Add(Match("missing-settlement", noFact.Id, noFactOpponent.Id, DateTimeOffset.UtcNow));
            source.Add(new L12RankingMatch("legacy-name-only", first.Username, second.Username,
                priorAt.ToString("O"), priorAt.ToString("O"), 0, "", "", 0, masters[0], masters[1]));

            var board = store.RankedIntervalLeaderboard(source, "7d");
            Assert.Equal(4, board.Count);
            var player = board.Single(row => row.Username == first.Username);
            Assert.Equal(6, player.Wins);
            Assert.Equal(0, player.Losses);
            Assert.Equal("本赛季未定级", player.Tier);
            Assert.Equal(masters[0], player.FavoriteMasterId);
            Assert.Equal(Enumerable.Range(0, 5).Sum(index => store.RankedSettlement($"prior-season-{index}", first.Id)!.Delta)
                + store.RankedSettlement(currentId, first.Id)!.Delta, player.IntervalSevenDelta);
            Assert.False(player.IntervalSevenIncomplete);
            Assert.Equal(0, player.SevenValue); // Current-season absolute value is separate from interval delta.
            var thirtyPlayer = store.RankedIntervalLeaderboard(source, "30d")
                .Single(row => row.Username == first.Username);
            Assert.Equal(6, thirtyPlayer.Wins);
            Assert.Equal(1, thirtyPlayer.Losses);
            Assert.Equal(player.IntervalSevenDelta + store.RankedSettlement(olderId, first.Id)!.Delta,
                thirtyPlayer.IntervalSevenDelta);
            Assert.Empty(store.RankedLeaderboard()); // Neither player has placed this season.
            var incomplete = board.Single(row => row.Username == noFact.Username);
            Assert.Null(incomplete.IntervalSevenDelta);
            Assert.True(incomplete.IntervalSevenIncomplete);
            Assert.Equal("未记录", incomplete.DisplayValue);
            Assert.True(incomplete.Rank > player.Rank);
            Assert.Equal("本赛季未选择", incomplete.Faction);
            Assert.Single(store.RankedIntervalLeaderboard(source, "7d", "order"));

            var voidAction = new L12RankedIntegrityActionInput("window-current-void", "confirmed",
                [currentId], [], null, "已核对服务器排位结算与对局证据。", "排除该场异常对局。");
            var preview = store.PreviewRankedIntegrityAction(admin, voidAction);
            Assert.True(preview.CanConfirm, string.Join(" | ", preview.BlockingReasons));
            store.ConfirmRankedIntegrityAction(admin, voidAction, preview.Revision,
                new L12AdminAuditContext("window-current-void"));
            Assert.Equal("voided", store.RankedSettlement(currentId, first.Id)!.RewardStatus);
            var afterVoid = store.RankedIntervalLeaderboard(source, "7d")
                .Single(row => row.Username == first.Username);
            Assert.Equal(5, afterVoid.Wins);
            Assert.Equal(Enumerable.Range(0, 5).Sum(index =>
                store.RankedSettlement($"prior-season-{index}", first.Id)!.Delta), afterVoid.IntervalSevenDelta);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void NullStartUsesCutoverForLaterSeasonWithoutTruncatingInitialSeason()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranking-null-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var masters = catalog.PresetDecks.Select(deck => deck.MasterId).Distinct().Take(2).ToArray();
            Assert.Equal(2, masters.Length);
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.Null(store.OperationsConfig(admin).Config.Season.StartsAt);
            var priorAt = DateTimeOffset.UtcNow.AddDays(-2);
            L12RankingMatch Match(string id, DateTimeOffset at) => new(id, "first", "second",
                at.ToString("O"), at.ToString("O"), 0, "", "", 0, masters[0], masters[1]);
            var prior = Match("initial-season-prior-to-definition-activation", priorAt);
            Assert.Equal(1, store.RankedAnalytics([prior], "season").Summary.Matches);

            var seasons = store.SeasonCatalog(admin);
            var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
                new L12SeasonDefinitionDraft("S02", "第二赛季", null, null, seasons.Next.Configuration),
                seasons.Next.Revision, "test null-start season cutover",
                new L12AdminAuditContext("ranked-null-start-draft"));
            store.ActivateSeason(admin, draft.DefinitionId, seasons.Current.Revision, draft.Revision,
                "test null-start season cutover", new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId,
                    0, 0, 0, 0), new L12AdminAuditContext("ranked-null-start-activate"));
            Assert.Null(store.OperationsConfig(admin).Config.Season.StartsAt);
            var current = Match("current-season-after-activation", DateTimeOffset.UtcNow);

            Assert.Equal(1, store.RankedAnalytics([prior, current], "season").Summary.Matches);
            Assert.Equal(2, store.RankedAnalytics([prior, current], "7d").Summary.Matches);
            Assert.Equal(2, store.RankedAnalytics([prior, current], "30d").Summary.Matches);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void RollingRangesCrossSeasonBoundaryButSeasonRemainsCurrentOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranking-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var masters = catalog.PresetDecks.Select(deck => deck.MasterId).Distinct().Take(2).ToArray();
            Assert.Equal(2, masters.Length);
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var first = store.Register("rangeone", "password-123").Account!;
            var second = store.Register("rangetwo", "password-123").Account!;
            var disabled = store.Register("rangedead", "password-123").Account!;
            store.SetAccountDisabled(admin, disabled.Id, true, "test excluded player",
                new L12AdminAuditContext("ranked-analytics-disabled"), true);
            var operations = store.OperationsConfig(admin);
            var now = DateTimeOffset.UtcNow;
            store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { StartsAt = now.AddDays(-1), EndsAt = now.AddDays(60) },
            }, operations.Version, "test rolling analytics season boundary",
                new L12AdminAuditContext("ranked-analytics-range", RequestMethod: "TEST", RequestPath: "/test"));

            L12RankingMatch Match(string id, double daysAgo, string? accountId0 = null) => new(id, "player zero", "player one",
                now.AddDays(-daysAgo).AddMinutes(-12).ToString("O"), now.AddDays(-daysAgo).ToString("O"),
                0, "", "", 0, masters[0], masters[1], accountId0 ?? first.Id, second.Id);
            var source = new[]
            {
                Match("current-season", .5),
                Match("previous-season-seven-days", 2),
                Match("previous-season-thirty-days", 20),
                Match("outside-thirty-days", 40),
                Match("future-match", -1),
                Match("disabled-participant", .25, disabled.Id),
            };

            var seven = store.RankedAnalytics(source, "7d", now);
            Assert.Equal(2, seven.Summary.Matches);
            Assert.Equal(now.AddDays(-7), seven.FromUtc);
            Assert.Equal(now, seven.UntilUtc);
            Assert.Null(seven.SeasonId);
            Assert.Null(seven.SeasonName);
            Assert.Equal(2, seven.Masters.Single(row => row.MasterId == masters[0]).Games);
            Assert.Equal(2, seven.Matchups.Single(row => row.MasterId == masters[0]
                && row.OpponentMasterId == masters[1]).Games);

            var thirty = store.RankedAnalytics(source, "30d", now);
            Assert.Equal(3, thirty.Summary.Matches);
            Assert.Equal(now.AddDays(-30), thirty.FromUtc);
            Assert.Equal(now, thirty.UntilUtc);
            Assert.Equal(3, thirty.Masters.Single(row => row.MasterId == masters[0]).Games);

            var season = store.RankedAnalytics(source, "season", now);
            Assert.Equal(1, season.Summary.Matches);
            Assert.Equal(now.AddDays(-1), season.FromUtc);
            Assert.Equal(now, season.UntilUtc);
            Assert.Equal(operations.Config.Season.Id, season.SeasonId);
            Assert.Equal(operations.Config.Season.Name, season.SeasonName);
            Assert.Equal(1, store.RankedAnalytics(source, "unexpected").Summary.Matches);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
