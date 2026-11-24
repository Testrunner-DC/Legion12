using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// ⑨ 第 2 步的前置基线：把详情 6 个切片维度的"形状"冻结下来。
// 合并成一次分组之后，这些不变量必须原样成立——维度集合不变、同维度内不出现重复桶、
// 每个维度的样本合计与单卡样本一致、顺序稳定（按维度名 + 样本降序）。
[Collection("Platform environment")]
public sealed class CardAnalyticsBreakdownBaselineTests
{
    private static readonly string[] Dimensions =
        ["master", "opponent-master", "initiative", "rules-version", "effect-version", "season"];

    [Fact]
    public async Task DetailKeepsSixDimensionsWithoutDuplicateBuckets()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A",
            new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false);

        Assert.NotNull(detail);
        Assert.Equal(Dimensions, detail!.Breakdowns.Select(item => item.Dimension).Distinct());
        foreach (var dimension in Dimensions)
        {
            var buckets = detail.Breakdowns.Where(item => item.Dimension == dimension).ToArray();
            Assert.NotEmpty(buckets);
            Assert.Equal(buckets.Length, buckets.Select(item => item.Value).Distinct().Count());
            foreach (var bucket in buckets) Assert.True(bucket.SampleSize > 0, dimension + " / " + bucket.Value);
        }
    }

    [Fact]
    public async Task EachDimensionAccountsForTheSameCardSamples()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A",
            new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false);
        Assert.NotNull(detail);

        var cardSamples = detail!.Summary.SampleSize;
        foreach (var dimension in Dimensions)
        {
            var total = detail.Breakdowns.Where(item => item.Dimension == dimension)
                .Sum(item => item.SampleSize);
            Assert.Equal(cardSamples, total);
        }
    }

    // 逐桶字面值来自合并前的实际输出（artifacts/u9-breakdown-snapshot.json）。合并成一次分组后
    // 这些数字必须一字不差——这是 ⑨② 的等价性判据。
    [Fact]
    public async Task MergedBreakdownsReproduceEveryPreMergeBucket()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Recorder.GetCardAnalyticsAsync("A",
            new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false);
        Assert.NotNull(detail);

        var actual = detail!.Breakdowns
            .Select(item => $"{item.Dimension}|{item.Value}|{item.SampleSize}|{item.EligibleSampleSize}|{item.IncludedMatches}|{item.Wins}")
            .ToArray();
        string[] expected =
        [
            "master|M-A|3|3|3|3",
            "master|M-B|3|3|3|0",
            "opponent-master|M-A|3|3|3|0",
            "opponent-master|M-B|3|3|3|3",
            "initiative|first|3|3|3|2",
            "initiative|second|3|3|3|1",
            "rules-version|legacy|6|6|3|3",
            "effect-version|test-effects|6|6|3|3",
            "season|unassigned|6|6|3|3",
        ];
        Assert.Equal(expected, actual);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        internal MatchRecorder Recorder { get; }
        private Fixture(string root, MatchRecorder recorder) => (_root, Recorder) = (root, recorder);

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "l12-analytics-breakdown-" + Guid.NewGuid().ToString("N"));
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
                        '2026-09-01T00:01:00.0000000+00:00',0,'ranked',$first,'test-effects',2);
                    """;
                command.Parameters.AddWithValue("$id", "match-" + match);
                command.Parameters.AddWithValue("$first", match % 2);
                command.ExecuteNonQuery();
                for (var player = 0; player < 2; player++)
                {
                    using var participant = connection.CreateCommand();
                    participant.CommandText = """
                        INSERT INTO match_participants(match_id,player_index,account_id,display_name,
                            master_id,master_name,deck_name,deck_snapshot_coverage)
                        VALUES($id,$player,$account,'synthetic',$master,'MASTER','deck','exact');
                        INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
                        VALUES($id,$player,'main','A',1);
                        """;
                    participant.Parameters.AddWithValue("$id", "match-" + match);
                    participant.Parameters.AddWithValue("$player", player);
                    participant.Parameters.AddWithValue("$account", $"synthetic-{match}-{player}");
                    participant.Parameters.AddWithValue("$master", player == 0 ? "M-A" : "M-B");
                    participant.ExecuteNonQuery();
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
