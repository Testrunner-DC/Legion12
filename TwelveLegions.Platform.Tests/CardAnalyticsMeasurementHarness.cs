using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// ⑨③ 的量测装置（手动跑，不进日常门禁的耗时预算）：按固定夹具灌入 N 场对局，
// 量列表与详情的端到端耗时，用来判断"TEMP 索引维度对齐"是否值得改。
[Collection("Platform environment")]
[Trait("Category", "Measurement")]
public sealed class CardAnalyticsMeasurementHarness
{
    [Fact]
    public async Task MeasureListAndDetail()
    {
        // 默认不跑：量测要手动指定场次，否则每次全量门禁都白付几秒到一分钟。
        if (!int.TryParse(Environment.GetEnvironmentVariable("L12_ANALYTICS_MEASURE_MATCHES"), out var matches)
            || matches <= 0) return;
        var root = Path.Combine(Path.GetTempPath(), "l12-analytics-measure-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "matches.db");
        var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var seed = Stopwatch.StartNew();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var tx = connection.BeginTransaction();
            for (var match = 0; match < matches; match++)
            {
                using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                        started_utc,ended_utc,winner,mode_id,first_player,effect_version,analytics_version)
                    VALUES($id,'SYNTHETIC',$seed,'P0','P1','D0','D1',$started,$ended,$winner,'ranked',$first,
                        'test-effects',2);
                    """;
                command.Parameters.AddWithValue("$id", "match-" + match);
                command.Parameters.AddWithValue("$seed", match);
                var started = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(match);
                command.Parameters.AddWithValue("$started", started.ToString("O"));
                command.Parameters.AddWithValue("$ended", started.AddMinutes(1).ToString("O"));
                command.Parameters.AddWithValue("$winner", match % 2);
                command.Parameters.AddWithValue("$first", match % 2);
                command.ExecuteNonQuery();
                for (var player = 0; player < 2; player++)
                {
                    using var participant = connection.CreateCommand();
                    participant.Transaction = tx;
                    participant.CommandText = """
                        INSERT INTO match_participants(match_id,player_index,account_id,display_name,
                            master_id,master_name,deck_name,deck_snapshot_coverage)
                        VALUES($id,$player,$account,'synthetic',$master,'MASTER','deck','exact');
                        """;
                    participant.Parameters.AddWithValue("$id", "match-" + match);
                    participant.Parameters.AddWithValue("$player", player);
                    participant.Parameters.AddWithValue("$account", $"player-{(match + player) % 40}");
                    participant.Parameters.AddWithValue("$master", "M-" + ((match + player) % 6));
                    participant.ExecuteNonQuery();
                    for (var card = 0; card < 10; card++)
                    {
                        using var deckCard = connection.CreateCommand();
                        deckCard.Transaction = tx;
                        deckCard.CommandText = "INSERT INTO match_deck_cards VALUES($id,$player,'main',$card,1);";
                        deckCard.Parameters.AddWithValue("$id", "match-" + match);
                        deckCard.Parameters.AddWithValue("$player", player);
                        deckCard.Parameters.AddWithValue("$card", "CARD-" + ((match + player * 3 + card) % 200));
                        deckCard.ExecuteNonQuery();
                    }
                }
            }
            tx.Commit();
        }
        seed.Stop();

        await recorder.DisposeAsync();
        SqliteConnection.ClearAllPools();

        // 每轮一个新 recorder：结果缓存是实例级的，这样每轮都是冷启动，量的才是真实成本。
        var timings = new List<object>();
        for (var round = 0; round < 5; round++)
        {
            var fresh = new MatchRecorder(path);
            await fresh.InitializeAsync();
            var listWatch = Stopwatch.StartNew();
            var page = await fresh.ListCardAnalyticsAsync(
                new L12CardAnalyticsQuery(MinimumSampleSize: 1, Limit: 20));
            listWatch.Stop();
            var detailWatch = Stopwatch.StartNew();
            var detailId = page.Items.First().CardId;
            await fresh.GetCardAnalyticsAsync(detailId,
                new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false);
            detailWatch.Stop();
            await fresh.DisposeAsync();
            SqliteConnection.ClearAllPools();
            timings.Add(new
            {
                round, listMs = Math.Round(listWatch.Elapsed.TotalMilliseconds, 1),
                detailMs = Math.Round(detailWatch.Elapsed.TotalMilliseconds, 1),
                total = page.Total,
            });
        }
        File.WriteAllText(Path.Combine(@"D:\GPT\Legion12\artifacts",
                Environment.GetEnvironmentVariable("L12_ANALYTICS_MEASURE_OUT") ?? "u9-measure.json"),
            JsonSerializer.Serialize(new
            {
                matches,
                seedMs = Math.Round(seed.Elapsed.TotalMilliseconds, 1),
                timings,
                serverTiming = "n/a",
            }, new JsonSerializerOptions { WriteIndented = true }));
        Directory.Delete(root, true);
        Assert.True(timings.Count == 5);
    }
}
