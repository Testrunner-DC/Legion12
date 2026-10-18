namespace TwelveLegions.Server;

// Descriptive observations, not a policy optimizer or a causal estimate of a card's strength.
public sealed record L12DisasterStrategyComparison(string Status, long ComparableOpportunities,
    long TotalOpportunities, long ReferenceOpportunities, int MatchedStrata, int MissingStrata,
    double? TargetRate, double? ReferenceRate, double? Difference);
public sealed record L12DisasterStrategyItem(string Party, int Step, string Action, string Origin,
    string CardId, long Opportunities, long Selections, long DistinctMatches, double? SelectionRate,
    L12DisasterStrategyComparison Comparison);
public sealed record L12DisasterStrategyPriorBan(int Step, string CardId);
public sealed record L12DisasterStrategyChoice(string CardId, long Selections, double? SelectionRate);
public sealed record L12DisasterStrategyCondition(string Id, string Party, int Step, string Origin,
    string EffectVersion, string PoolId, IReadOnlyList<L12DisasterStrategyPriorBan> PriorPublicBans,
    string? KnownPublicCardId, IReadOnlyList<string> CandidateCardIds, long Samples, long DistinctMatches,
    IReadOnlyList<L12DisasterStrategyChoice> Choices);
public sealed record L12DisasterStrategyReport(string? MasterId, string? OpponentMasterId,
    string? Initiative, string Status, L12DisasterAnalyticsCoverage Coverage,
    IReadOnlyList<L12DisasterAnalyticsPool> Pools, IReadOnlyList<L12DisasterStrategyItem> Items,
    IReadOnlyList<L12DisasterStrategyCondition> Conditions, string? Mode, string? PoolId, string Origin,
    bool PrivatePickGroupsHidden = false, bool ConditionsTruncated = false, bool PoolsTruncated = false,
    int MinimumOpportunities = 30,
    string ComparisonMethod = "other-opponents; common effect-version/pool/party/step/origin strata; target-opportunity weighting",
    string MechanismStatus = "current-card-text-reference-only");
