namespace TwelveLegions.Server;

/// <summary>
/// /metrics 的载荷构造。只输出聚合量：不含账号、房间码、令牌、手牌或邮箱等可定位个人的字段。
/// 独立成纯函数，便于平台测试直接断言“有具名序列、无敏感片段”，不必拉起真实 HTTP 监听。
/// </summary>
internal static class L12ServerMetrics
{
    /// <summary>一旦出现在序列化结果里就说明指标泄露了个人或会话数据。</summary>
    internal static readonly string[] ForbiddenFragments =
        ["token", "password", "hand", "accountid", "roomcode", "email"];

    internal static object Build(int cards, int sockets, int rooms, int onlineAccounts, int activeGames,
        double uptimeSeconds, PerformanceMetricBatch? batch)
    {
        var series = new List<object>();
        if (batch is not null)
        {
            foreach (var row in batch.Series.OrderBy(item => item.Stage, StringComparer.Ordinal))
            {
                series.Add(new
                {
                    stage = row.Stage,
                    unit = row.Unit,
                    count = row.Count,
                    average = row.Count == 0 ? 0 : Math.Round(row.Total / row.Count, 2),
                    maximum = Math.Round(row.Maximum, 2),
                    overflow = row.OverflowCount,
                });
            }
        }

        return new
        {
            service = "twelve-legions",
            cards,
            sockets,
            rooms,
            onlineAccounts,
            activeGames,
            uptimeSeconds = Math.Round(uptimeSeconds, 1),
            series,
            diagnostics = batch is null
                ? null
                : new
                {
                    unknownStages = batch.Diagnostics.UnknownStages,
                    invalidSamples = batch.Diagnostics.InvalidSamples,
                    sinkFailures = batch.Diagnostics.SinkFailures,
                    lostSinkSamples = batch.Diagnostics.LostSinkSamples,
                    skippedFlushes = batch.Diagnostics.SkippedFlushes,
                    droppedContention = batch.Series.Sum(row => row.DroppedContentionTotal),
                },
        };
    }
}
