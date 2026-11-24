using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// ⑨ 第 1 步：把"列表 total 与分页"的行为冻结成基线。⑨ 会把 total 从独立的一次 GROUP BY
// 合并进列表查询（COUNT(*) OVER ()），这组用例是那次重构的等价性对照：total 必须仍是
// "全部合格分组的数量"，与当前页是否有行无关。
[Collection("Platform environment")]
public sealed class CardAnalyticsTotalConsistencyTests
{
    [Fact]
    public async Task TotalCountsEveryEligibleGroupNotJustThePage()
    {
        await using var fixture = await Fixture.CreateAsync();

        var page = await fixture.Recorder.ListCardAnalyticsAsync(
            new L12CardAnalyticsQuery(MinimumSampleSize: 1, Limit: 10));

        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { "A", "B", "C" }, page.Items.Select(item => item.CardId));
        Assert.Equal(new long[] { 6, 2, 1 }, page.Items.Select(item => item.SampleSize));
    }

    [Fact]
    public async Task EveryPageReportsTheSameTotal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seen = new List<string>();

        for (var number = 1; number <= 4; number++)
        {
            var page = await fixture.Recorder.ListCardAnalyticsAsync(
                new L12CardAnalyticsQuery(MinimumSampleSize: 1, Limit: 1, Page: number));
            Assert.Equal(3, page.Total);
            seen.AddRange(page.Items.Select(item => item.CardId));
        }

        Assert.Equal(new[] { "A", "B", "C" }, seen);
    }

    [Fact]
    public async Task TotalFollowsTheFilters()
    {
        await using var fixture = await Fixture.CreateAsync();

        var candidates = await fixture.Recorder.ListCardAnalyticsAsync(
            new L12CardAnalyticsQuery(MinimumSampleSize: 1, CandidateCardIds: ["B", "C"]));
        Assert.Equal(2, candidates.Total);
        Assert.Equal(new[] { "B", "C" }, candidates.Items.Select(item => item.CardId));

        var threshold = await fixture.Recorder.ListCardAnalyticsAsync(
            new L12CardAnalyticsQuery(MinimumSampleSize: 2));
        Assert.Equal(2, threshold.Total);
        Assert.Equal(new[] { "A", "B" }, threshold.Items.Select(item => item.CardId));
    }

    [Fact]
    public async Task PageBeyondTheEndStillReportsTheRealTotal()
    {
        await using var fixture = await Fixture.CreateAsync();

        var page = await fixture.Recorder.ListCardAnalyticsAsync(
            new L12CardAnalyticsQuery(MinimumSampleSize: 1, Limit: 1, Page: 9));

        Assert.Empty(page.Items);
        Assert.Equal(3, page.Total);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        internal MatchRecorder Recorder { get; }
        private Fixture(string root, MatchRecorder recorder) => (_root, Recorder) = (root, recorder);

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-analytics-total-" + Guid.NewGuid().ToString("N"));
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
                        using var extra = connection.CreateCommand();
                        extra.CommandText = "INSERT INTO match_deck_cards VALUES($id,$player,'main',$card,1);";
                        extra.Parameters.AddWithValue("$id", "match-" + match);
                        extra.Parameters.AddWithValue("$player", player);
                        extra.Parameters.AddWithValue("$card", player == 0 ? "C" : "B");
                        extra.ExecuteNonQuery();
                    }
                }
            }
            return new Fixture(root, recorder);
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            using var pooled = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(_root, "matches.db") }.ToString());
            SqliteConnection.ClearPool(pooled);
            Directory.Delete(_root, true);
        }
    }
}
