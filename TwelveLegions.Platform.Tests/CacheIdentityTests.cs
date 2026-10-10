using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CardAnalyticsCacheIdentityTests
{
    [Fact]
    public async Task DifferentPagesDoNotReuseFirstPageWithinCacheTtl()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new L12CardAnalyticsQuery(Limit: 1, MinimumSampleSize: 1);
        var first = await fixture.Recorder.ListCardAnalyticsAsync(query);
        var second = await fixture.Recorder.ListCardAnalyticsAsync(query with { Page = 2 });
        Assert.Equal("A", Assert.Single(first.Items).CardId);
        Assert.Equal("B", Assert.Single(second.Items).CardId);
        Assert.Equal(2, second.Page);
        Assert.Equal(3, second.Total);
        Assert.Equal(2, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task DifferentSortAndDirectionDoNotReuseSampleDescendingWithinCacheTtl()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new L12CardAnalyticsQuery(Limit: 1, MinimumSampleSize: 1);
        Assert.Equal("A", Assert.Single((await fixture.Recorder.ListCardAnalyticsAsync(query)).Items).CardId);
        Assert.Equal("C", Assert.Single((await fixture.Recorder.ListCardAnalyticsAsync(
            query with { Sort = "card" })).Items).CardId);
        Assert.Equal("C", Assert.Single((await fixture.Recorder.ListCardAnalyticsAsync(
            query with { Direction = "asc" })).Items).CardId);
        Assert.Equal(3, fixture.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task EquivalentNormalizedQueriesShareCacheAndDetailKeepsStatistics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new L12CardAnalyticsQuery(MinimumSampleSize: 1,
            CandidateCardIds: ["B", "A"], Sort: " SAMPLE-SIZE ", Direction: "ASC");
        var first = await fixture.Recorder.ListCardAnalyticsAsync(query);
        var second = await fixture.Recorder.ListCardAnalyticsAsync(query with
        {
            CandidateCardIds = ["A", "B", "A"], Sort = "sample-size", Direction = "asc",
        });
        Assert.Equal(first.Items.Select(item => item.CardId), second.Items.Select(item => item.CardId));
        Assert.Equal(1, fixture.Recorder.AnalyticsResultCacheCount);
        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A", query, false);
        Assert.NotNull(detail);
        Assert.Equal(6, detail.Summary.SampleSize);
        Assert.Empty(detail.RecentMatches);
    }

    [Fact]
    public async Task RecentMatchesAndPhaseTimingKeepTheirContractWhenStatisticsAreCached()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new L12CardAnalyticsQuery(MinimumSampleSize: 1);
        var timing = new CardAnalyticsRequestTiming();
        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A", query, true,
            CancellationToken.None, timing);
        Assert.NotNull(detail);
        var expected = await fixture.Recorder.ListAdminMatchesAsync(new L12AdminMatchQuery(Limit: 20,
            ModeId: "ranked", Status: "completed", CardId: "A", RequireDecisiveResult: true,
            RequireAnalyticsEligible: true));
        Assert.Equal(expected.Items.Select(item => item.MatchId), detail.RecentMatches.Select(item => item.MatchId));
        Assert.All(detail.RecentMatches, match =>
        {
            Assert.Null(match.Error);
            Assert.All(match.Players, player =>
            { Assert.Null(player.AccountId); Assert.Null(player.DeckName); Assert.StartsWith("参赛方", player.DisplayName); });
        });
        Assert.Contains("scope-total;dur=", timing.ToServerTiming());
        Assert.Contains("breakdowns;dur=", timing.ToServerTiming());
        Assert.Contains("recent;dur=", timing.ToServerTiming());
        var cachedTiming = new CardAnalyticsRequestTiming();
        var cached = await fixture.Recorder.GetCardAnalyticsAsync("A", query, true,
            CancellationToken.None, cachedTiming);
        Assert.Equal(detail.Summary, cached!.Summary);
        Assert.Equal(detail.RecentMatches.Select(match => match.MatchId), cached.RecentMatches.Select(match => match.MatchId));
        Assert.Contains("cache;desc=\"hit\"", cachedTiming.ToServerTiming());
        Assert.Contains("recent;dur=", cachedTiming.ToServerTiming());
        Assert.DoesNotContain("scope-total;dur=", cachedTiming.ToServerTiming());
    }

    [Fact]
    public async Task PreCanceledPublicRequestDoesNotRunSqlOrReturnACachedResult()
    {
        await using var fixture = await Fixture.CreateAsync();
        var query = new L12CardAnalyticsQuery(MinimumSampleSize: 1);
        await fixture.Recorder.ListCardAnalyticsAsync(query);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Recorder.ListCardAnalyticsAsync(query, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Recorder.GetCardAnalyticsAsync("A", query, true, cancellation.Token));
        Assert.Equal(0, fixture.Recorder.CardAnalyticsExecutionCounts.Active);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        public MatchRecorder Recorder { get; }
        private Fixture(string root, MatchRecorder recorder) => (_root, Recorder) = (root, recorder);

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-card-cache-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "matches.db");
            var recorder = new MatchRecorder(path);
            await recorder.InitializeAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            for (var match = 0; match < 3; match++)
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                        started_utc,ended_utc,winner,mode_id,first_player,effect_version,analytics_version)
                    VALUES($id,'SYNTHETIC',1,'P0','P1','D0','D1','2026-09-01T00:00:00.0000000+00:00',
                        '2026-09-01T00:01:00.0000000+00:00',0,'ranked',0,'test-effects',2);
                    """;
                command.Parameters.AddWithValue("$id", "match-" + match);
                command.ExecuteNonQuery();
                for (var player = 0; player < 2; player++)
                {
                    using var participant = connection.CreateCommand();
                    participant.CommandText = """
                        INSERT INTO match_participants(match_id,player_index,account_id,display_name,
                            master_id,master_name,deck_name,deck_snapshot_coverage)
                        VALUES($id,$player,$account,'synthetic','MASTER','MASTER','deck','exact');
                        INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
                        VALUES($id,$player,'main','A',1);
                        """;
                    participant.Parameters.AddWithValue("$id", "match-" + match);
                    participant.Parameters.AddWithValue("$player", player);
                    participant.Parameters.AddWithValue("$account", $"synthetic-{match}-{player}");
                    participant.ExecuteNonQuery();
                    if ((match < 2 && player == 1) || (match == 0 && player == 0))
                    {
                        using var card = connection.CreateCommand();
                        card.CommandText = "INSERT INTO match_deck_cards VALUES($id,$player,'main',$card,1);";
                        card.Parameters.AddWithValue("$id", "match-" + match);
                        card.Parameters.AddWithValue("$player", player);
                        card.Parameters.AddWithValue("$card", player == 0 ? "C" : "B");
                        card.ExecuteNonQuery();
                    }
                }
            }
            return new Fixture(root, recorder);
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            // Only this synthetic, GUID-owned directory is removed; no product data is used.
            using var pooled = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(_root, "matches.db") }.ToString());
            SqliteConnection.ClearPool(pooled);
            Directory.Delete(_root, true);
        }
    }
}
