using System.Text.Json;

namespace TwelveLegions.Server;

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
    string? CardId = null);

public sealed record L12CardAnalyticsQuery(
    string? Cursor = null,
    int Limit = 50,
    int MinimumSampleSize = 5,
    string? Search = null,
    IReadOnlyList<string>? CandidateCardIds = null,
    string? ModeId = null,
    string? MasterId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

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

public sealed record L12AnalyticsCoverage(
    int SchemaVersion,
    IReadOnlyList<string> SupportedKinds,
    long ExactFacts,
    long InferredFacts,
    long PartialFacts,
    long ExactDeckSnapshots,
    long InferredDeckSnapshots,
    bool PrivateDuringActiveMatch,
    IReadOnlyList<string> Limitations);

public sealed record L12AdminMatchDetail(
    L12AdminMatchSummary Summary,
    IReadOnlyList<L12MatchParticipantDetail> Participants,
    IReadOnlyList<L12RecordedCommand> Replay,
    IReadOnlyList<L12CardFactView> CardFacts,
    L12AnalyticsCoverage Coverage);

public sealed record L12CardAnalyticsItem(
    string CardId,
    long SampleSize,
    long EligibleSampleSize,
    long IncludedMatches,
    double InclusionRate,
    long Wins,
    double WinRate,
    double BaselineWinRate,
    double WinRateDelta,
    long DrawnMatches,
    long PlayedMatches,
    long ActivatedCount,
    long ResolvedCount,
    long NegatedCount,
    long FizzledCount,
    L12AnalyticsCoverage Coverage);

public sealed record L12CardAnalyticsPageSummary(
    long EligibleMatches,
    long SampleSize,
    double BaselineWinRate,
    int MinimumSampleSize,
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
    double BaselineWinRate,
    double WinRateDelta);

public sealed record L12CardAnalyticsDetail(
    L12CardAnalyticsItem Summary,
    IReadOnlyList<L12CardAnalyticsBreakdown> Breakdowns,
    IReadOnlyList<L12AdminMatchSummary> RecentMatches,
    L12AnalyticsCoverage Coverage);
