using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// C4 回归守卫：/metrics 必须暴露具名序列与聚合计数，且不得含账号、令牌、手牌、房间码等个人或会话数据。
/// </summary>
public sealed class ServerMetricsEndpointContractTests
{
    [Fact]
    public void MetricsPayloadExposesNamedSeriesAndNoPersonalData()
    {
        var startedAt = DateTimeOffset.Parse("2026-11-21T00:00:00Z");
        var batch = new PerformanceMetricBatch(
            [new PerformanceMetricSummary("platform.operation.transactional-save", "ms", startedAt,
                startedAt.AddMinutes(6), 248, 2395.7, 11.43, false, [0, 0, 248], null, null, 0, 3)],
            new PerformanceMetricDiagnostics(0, 0, 0, 0, 0, 0));

        var payload = L12ServerMetrics.Build(324, 12, 3, 9, 2, 1234.5, batch);
        var json = JsonSerializer.Serialize(payload).ToLowerInvariant();

        Assert.Contains("\"series\"", json, StringComparison.Ordinal);
        Assert.Contains("platform.operation.transactional-save", json, StringComparison.Ordinal);
        Assert.Contains("\"uptimeseconds\":1234.5", json, StringComparison.Ordinal);
        foreach (var fragment in L12ServerMetrics.ForbiddenFragments)
            Assert.DoesNotContain(fragment, json, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsPayloadWithoutSnapshotStillReturnsAggregates()
    {
        var json = JsonSerializer.Serialize(L12ServerMetrics.Build(324, 0, 0, 0, 0, 0, null)).ToLowerInvariant();
        Assert.Contains("\"series\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\":null", json, StringComparison.Ordinal);
    }
}
