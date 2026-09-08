using System.Text.Json;

namespace TwelveLegions.Server;

public sealed class L12ReplayPayloadTooLargeException(
    string matchId, long commandCount, long serializedBytes, long maximumCommands, long maximumBytes)
    : Exception($"对局 {matchId} 的回放过大（{commandCount} 条命令，{serializedBytes} 字节）；请缩小回放或使用离线归档。")
{
    public long CommandCount { get; } = commandCount;
    public long SerializedBytes { get; } = serializedBytes;
    public long MaximumCommands { get; } = maximumCommands;
    public long MaximumBytes { get; } = maximumBytes;
}

public static class L12CardFactKinds
{
    public const int SchemaVersion = 1;

    public static IReadOnlyList<string> Supported { get; } =
    [
        "deck-included",
        "draw",
        "search-or-hand-add",
        "play",
        "activate",
        "push",
        "resolve",
        "negate",
        "fizzle",
        "zone-move",
        "damage",
        "kill",
    ];
}

public sealed record L12AdminMatchQuery(
    string? Cursor = null,
    int Limit = 50,
    string? ModeId = null,
    string? Status = null,
    string? Player = null,
    string? AccountId = null,
    string? MasterId = null,
    int? Winner = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? CardId = null,
    string? CardOwnerMasterId = null,
    string? CardOwnerOpponentMasterId = null,
    string? CardOwnerInitiative = null,
    string? RulesVersion = null,
    string? SeasonId = null,
    bool RequireDecisiveResult = false,
    string? EffectVersion = null,
    bool RequireAnalyticsEligible = false);

public sealed record L12CardAnalyticsQuery(
    string? Cursor = null,
    int Limit = 50,
    int MinimumSampleSize = 5,
    string? Search = null,
    IReadOnlyList<string>? CandidateCardIds = null,
    string? ModeId = null,
    string? MasterId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? OpponentMasterId = null,
    string? Initiative = null,
    string? RulesVersion = null,
    string? SeasonId = null,
    string? EffectVersion = null);

public sealed record L12AdminMatchPlayer(
    int PlayerIndex,
    string? AccountId,
    string DisplayName,
    string? MasterId,
    string? DeckName,
    string Result);

public sealed record L12AdminMatchSummary(
    string MatchId,
    string ModeId,
    string Status,
    IReadOnlyList<L12AdminMatchPlayer> Players,
    string StartedUtc,
    string? EndedUtc,
    int? DurationSeconds,
    int CommandCount,
    string? Error);

public sealed record L12AdminMatchPage(
    IReadOnlyList<L12AdminMatchSummary> Items,
    long Total,
    string? NextCursor);

public sealed record L12DeckCardSnapshot(string CardId, int Quantity, string Section);

public sealed record L12MatchParticipantDetail(
    int PlayerIndex,
    string? AccountId,
    string DisplayName,
    string? MasterId,
    string? MasterName,
    string? DeckName,
    string Result,
    IReadOnlyList<L12DeckCardSnapshot> DeckCards,
    string DeckSnapshotCoverage);

public sealed record L12CardFactView(
    string Kind,
    long CommandSequence,
    long Revision,
    int Round,
    int Turn,
    string Phase,
    string OccurredUtc,
    int? PlayerIndex,
    string? AccountId,
    string? CardId,
    string? CardInstanceId,
    string? RelatedCardId,
    string? RelatedInstanceId,
    string? SourceZone,
    string? DestinationZone,
    int? Amount,
    string Coverage,
    JsonElement Metadata);

public sealed record L12AnalyticsMetricCoverage(
    string Metric,
    string Unit,
    long EligibleSamples,
    long ObservedSamples,
    long ExactFacts,
    long InferredFacts,
    long PartialFacts);

public sealed record L12AnalyticsConfidenceInterval(
    double Low,
    double High);

public sealed record L12AnalyticsUncertainty(
    string Status,
    string Method,
    double? Low = null,
    double? High = null,
    string? Reason = null);

public sealed record L12AnalyticsSampleStructure(
    long ParticipantSamples,
    long DistinctMatches,
    long DistinctPlayers,
    long KnownPlayerSamples,
    long AnonymousPlayerSamples,
    long MaximumPlayerContribution,
    double MaximumPlayerContributionRate,
    string DependencyStatus,
    L12AnalyticsUncertainty Uncertainty);

public sealed record L12AnalyticsStratifiedComparison(
    long CarriedSamples,
    long ComparisonSamples,
    long InsufficientStrata,
    long ExcludedIncludedSamples,
    double? WinRate,
    double? Delta,
    string Weighting,
    L12AnalyticsUncertainty? Uncertainty = null);

public sealed record L12AnalyticsUsageMetric(
    string Metric,
    long ObservedParticipantSamples,
    long EventCount,
    long ExactFacts,
    long InferredFacts,
    long PartialFacts,
    long? EligibleSamples,
    string CoverageStatus);

public sealed record L12CardAnalyticsUsage(
    IReadOnlyList<L12AnalyticsUsageMetric> Metrics);

public sealed record L12AnalyticsCoverage(
    int SchemaVersion,
    IReadOnlyList<string> SupportedKinds,
    long ExactFacts,
    long InferredFacts,
    long PartialFacts,
    long ExactDeckSnapshots,
    long InferredDeckSnapshots,
    bool PrivateDuringActiveMatch,
    IReadOnlyList<L12AnalyticsMetricCoverage> Metrics,
    IReadOnlyList<string> Limitations);

public sealed record L12AdminMatchDetail(
    L12AdminMatchSummary Summary,
    IReadOnlyList<L12MatchParticipantDetail> Participants,
    IReadOnlyList<L12RecordedCommand> Replay,
    IReadOnlyList<L12CardFactView> CardFacts,
    L12AnalyticsCoverage Coverage);

public sealed record L12AdminReplayPage(
    IReadOnlyList<L12RecordedCommand> Items,
    string? NextCursor,
    int Limit,
    long PageBytes,
    long TotalCommands,
    long TotalBytes);

public sealed record L12CardAnalyticsItem(
    string CardId,
    long SampleSize,
    long EligibleSampleSize,
    long IncludedMatches,
    double AverageQuantity,
    double InclusionRate,
    long Wins,
    double WinRate,
    L12AnalyticsConfidenceInterval WinRateConfidence,
    double? BaselineWinRate,
    L12AnalyticsConfidenceInterval? BaselineWinRateConfidence,
    double? WinRateDelta,
    L12AnalyticsConfidenceInterval? WinRateDeltaConfidence,
    long DrawnMatches,
    long PlayedMatches,
    long DrawnSamples,
    long PlayedSamples,
    long ActivatedSamples,
    long SettledSamples,
    long ResolvedSamples,
    long NegatedSamples,
    long FizzledSamples,
    long ActivatedCount,
    long ResolvedCount,
    long NegatedCount,
    long FizzledCount,
    L12AnalyticsCoverage Coverage,
    L12AnalyticsSampleStructure? SampleStructure = null,
    L12AnalyticsStratifiedComparison? Comparison = null,
    L12CardAnalyticsUsage? Usage = null);

public sealed record L12CardAnalyticsPageSummary(
    long EligibleMatches,
    long SampleSize,
    double? BaselineWinRate,
    int MinimumSampleSize,
    string StatisticalUnit,
    L12AnalyticsCoverage Coverage);

public sealed record L12CardAnalyticsPage(
    IReadOnlyList<L12CardAnalyticsItem> Items,
    long Total,
    string? NextCursor,
    L12CardAnalyticsPageSummary Summary);

public sealed record L12CardAnalyticsBreakdown(
    string Dimension,
    string Value,
    long SampleSize,
    long EligibleSampleSize,
    long IncludedMatches,
    long Wins,
    double WinRate,
    L12AnalyticsConfidenceInterval WinRateConfidence,
    double? BaselineWinRate,
    L12AnalyticsConfidenceInterval? BaselineWinRateConfidence,
    double? WinRateDelta,
    L12AnalyticsConfidenceInterval? WinRateDeltaConfidence);

public sealed record L12CardAnalyticsQuantityBucket(
    int Quantity,
    long SampleSize,
    long Wins,
    double WinRate);

public sealed record L12CardAnalyticsTurnBucket(
    int Turn,
    long FirstDrawSamples,
    long FirstPlaySamples);

public sealed record L12CardAnalyticsMatchup(
    string MasterId,
    string OpponentMasterId,
    long SampleSize,
    long EligibleSampleSize,
    long Wins,
    double WinRate,
    L12AnalyticsConfidenceInterval WinRateConfidence,
    double? BaselineWinRate,
    L12AnalyticsConfidenceInterval? BaselineWinRateConfidence,
    double? WinRateDelta,
    L12AnalyticsConfidenceInterval? WinRateDeltaConfidence);

public sealed record L12CardAnalyticsDetail(
    L12CardAnalyticsItem Summary,
    IReadOnlyList<L12CardAnalyticsBreakdown> Breakdowns,
    IReadOnlyList<L12CardAnalyticsQuantityBucket> QuantityDistribution,
    IReadOnlyList<L12CardAnalyticsTurnBucket> TurnDistribution,
    IReadOnlyList<L12CardAnalyticsMatchup> Matchups,
    IReadOnlyList<L12AdminMatchSummary> RecentMatches,
    L12AnalyticsCoverage Coverage);
