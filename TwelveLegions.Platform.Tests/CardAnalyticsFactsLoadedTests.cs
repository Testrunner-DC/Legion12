using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// ⑧-a（U-P1）：列表默认不读事实表，此时事实计数恒为 0。0 不能被当成"从未发生"，
// 所以每一项都带 FactsLoaded；本用例锁住这个信号，避免以后又把 0 当前提。
[Collection("Platform environment")]
public sealed class CardAnalyticsFactsLoadedTests
{
    [Fact]
    public async Task ListMarksItemsWhoseFactsWereNotRead()
    {
        await using var fixture = await Fixture.CreateAsync();

        var page = await fixture.Recorder.ListCardAnalyticsAsync(
            new L12CardAnalyticsQuery(MinimumSampleSize: 1, Limit: 10));

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item =>
        {
            Assert.False(item.FactsLoaded);
            Assert.Null(item.Usage);
            // 未读事实 ≠ 没有事实：这三个 0 只有配合 FactsLoaded=false 才是"未加载"。
            Assert.Equal(0, item.DrawnSamples);
            Assert.Equal(0, item.PlayedSamples);
            Assert.Equal(0, item.ActivatedSamples);
        });
    }

    [Fact]
    public async Task DetailSummaryReportsFactsWereRead()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A",
            new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false);

        Assert.NotNull(detail);
        Assert.True(detail!.Summary.FactsLoaded);
        Assert.True(detail.Summary.SampleSize >= 1);
        Assert.NotNull(detail.Summary.Usage);
        Assert.Equal("A", detail.Summary.CardId);
    }

    // ⑧-b：卡存在但样本不足必须是 200 + reason，而不是和"卡不存在"共用一个 404。
    [Fact]
    public async Task InsufficientSampleReturnsRealNumbersWithAReason()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A",
            new L12CardAnalyticsQuery(MinimumSampleSize: 999), includeRecentMatches: false);

        Assert.NotNull(detail);
        Assert.Equal("insufficient-sample", detail!.Reason);
        Assert.True(detail.Summary.SampleSize > 0);
        Assert.True(detail.Summary.SampleSize < 999);
        Assert.Empty(detail.Breakdowns);
        Assert.Empty(detail.QuantityDistribution);
        Assert.Empty(detail.TurnDistribution);
        Assert.Empty(detail.Matchups);
        Assert.Empty(detail.RecentMatches);
    }

    [Fact]
    public async Task UnknownCardStaysAnAbsentPayload()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Recorder.GetCardAnalyticsAsync("NO-SUCH-CARD",
            new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false);

        Assert.Null(detail);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        internal MatchRecorder Recorder { get; }
        private Fixture(string root, MatchRecorder recorder) => (_root, Recorder) = (root, recorder);

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-analytics-facts-" + Guid.NewGuid().ToString("N"));
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
