namespace TwelveLegions.Server;

public sealed record L12RuntimeDependencyView(
    string Name,
    bool Configured,
    string State,
    string? Detail,
    DateTimeOffset ObservedAt);

public sealed record L12HttpPerformanceView(
    int WindowSeconds,
    int SlowRequestThresholdMilliseconds,
    int MinimumSamples,
    int MinimumReadSamples,
    int MinimumMutationSamples,
    long SampleCount,
    long ReadSampleCount,
    long MutationSampleCount,
    long DiagnosticRequestCount,
    long InFlight,
    long PeakInFlight,
    double AverageDurationMilliseconds,
    string P95LatencyBand,
    long SlowRequestCount,
    double SlowRequestPercent,
    long RateLimitedCount,
    double RateLimitedPercent,
    long ServerErrorCount,
    double ServerErrorPercent,
    long ExpectedUnavailableCount,
    double ExpectedUnavailablePercent,
    long ClientCancelledCount,
    double ClientCancelledPercent,
    bool SampleSufficient,
    bool? WithinBudget,
    IReadOnlyList<string> BudgetFailures);

public sealed record L12RuntimeStatusView(
    DateTimeOffset ObservedAt,
    string ServiceVersion,
    int CardCount,
    int OnlineAccountCount,
    int WebSocketConnectionCount,
    int RoomCount,
    int ActiveGameCount,
    IReadOnlyList<L12ReleaseEnvironmentView> ReleaseEnvironments,
    L12RuntimeDependencyView Cdn,
    L12HttpPerformanceView HttpPerformance);

public sealed record L12AdminWorkbenchItemView(
    string Id,
    string Kind,
    string Label,
    string Detail,
    string Path,
    string Severity,
    int? Count = null,
    DateTimeOffset? OccurredAt = null);

public sealed record L12AdminWorkbenchSummaryView(
    DateTimeOffset SampledAt,
    IReadOnlyList<L12AdminWorkbenchItemView> Pending,
    IReadOnlyList<L12AdminWorkbenchItemView> Anomalies,
    IReadOnlyList<L12AdminWorkbenchItemView> RecentActivities);
