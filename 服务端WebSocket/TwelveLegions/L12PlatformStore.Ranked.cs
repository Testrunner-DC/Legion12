using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12RankedTierConfig(string Name, int Minimum, int BaseDelta,
    int WinStreakCap, int LossProtectionCap, int RatingGapCap, int StreakTerminationReward,
    string Color, string Icon);
public sealed record L12RankedTierGradientConfig(string Name, int Minimum, int BaseDelta,
    int WinStreakCap, int LossProtectionCap, int RatingGapCap, int StreakTerminationReward);
public sealed record L12RankedPendingGradientConfig(string AfterSeasonId,
    IReadOnlyList<L12RankedTierGradientConfig> Tiers);
public sealed record L12RankedMasterTitleConfig(string MasterId, string MasterName, string Title);
public sealed record L12RankedFactionConfig(string Id, string Name, string Color, string Icon,
    string FirstTitle, string TopFiveTitle, IReadOnlyList<L12RankedTierConfig> Tiers);
public sealed record L12RankedTimeControlConfig(
    int TotalTimeSeconds,
    int OperationTimeSeconds,
    int ReconnectGraceSeconds,
    int DisasterDecisionSeconds,
    int MulliganDecisionSeconds);
public sealed record L12RankedBroadcastConfig(
    int DisplaySeconds,
    int LobbyDelaySeconds,
    int IntervalSeconds,
    int WinStreakThreshold,
    int StreakEndedThreshold,
    int MinimumTierIndex,
    bool WinStreakEnabled,
    bool StreakEndedEnabled,
    bool HighestTierEnabled,
    bool FactionTitleEnabled,
    bool MasterTitleEnabled);
public sealed record L12RankedConfigView(int PlacementMatches, int PlacementMaximum,
    bool BroadcastEnabled, IReadOnlyList<L12RankedFactionConfig> Factions,
    IReadOnlyList<L12RankedMasterTitleConfig> MasterTitles,
    L12RankedTimeControlConfig? TimeControl = null,
    L12RankedBroadcastConfig? Broadcast = null,
    L12RankedPendingGradientConfig? PendingGradient = null);
public sealed record L12RankedProfileView(string AccountId, string Username, string SeasonId,
    string? Faction, int SevenValue, string DisplayValue, int PlacementPlayed, int PlacementWins,
    bool Placed, int Wins, int Losses, int WinStreak, int LossStreak, string Tier,
    int TierIndex, int FactionRank, string? Title, IReadOnlyList<string> Titles,
    string RankLabel, string? PlacementTitle, string? SelectedMasterTitle,
    IReadOnlyList<string> MasterTitles);
public sealed record L12RankedBattleIdentityView(int PlayerIndex, string Faction, int? Rank,
    string Tier, string? PlacementTitle, string? MasterTitle, bool HighestTier);
public sealed record L12RankedProfileHistoryView(string SeasonId, string Faction, int SevenValue,
    int PlacementPlayed, int PlacementWins, int Wins, int Losses, int WinStreak,
    DateTimeOffset ArchivedAt, string SeasonName, string Tier, string DisplayValue,
    double? WinRate, string? FactionTitle, IReadOnlyList<string> MasterTitles,
    IReadOnlyList<string> Titles, int? FactionRank, int? OverallRank, bool? Placed,
    int? PlacementRequired, string? RankLabel, string? SeasonMonth = null);
public sealed record L12SeasonSummaryNotificationView(string Id, string SeasonId, string SeasonName,
    string Faction, bool Placed, string RankLabel, int? FactionRank, int? OverallRank,
    int SevenValue, string DisplayValue, int Wins, int Losses, double? WinRate,
    string? FactionTitle, IReadOnlyList<string> MasterTitles, IReadOnlyList<string> Titles,
    DateTimeOffset AvailableAt);
public sealed record L12RankedSeasonHonorView(string SeasonId, string SeasonName, string Username,
    string Faction, string Tier, int SevenValue, string DisplayValue,
    IReadOnlyList<string> Titles, DateTimeOffset AwardedAt);
public sealed record L12RankedSeasonHonorWinnerView(string Username, string Faction);
public sealed record L12RankedSeasonHonorHistoryView(string SeasonName, string Title,
    IReadOnlyList<L12RankedSeasonHonorWinnerView> Winners, string? MasterId = null);
public sealed record L12RankedSeasonFactionFinalValueView(string Faction, int Value,
    string DisplayValue);
public sealed record L12RankedSeasonFactionTotalsHistoryView(string SeasonName,
    IReadOnlyList<L12RankedSeasonFactionFinalValueView> Factions);
public sealed record L12RankedSeasonHistoryView(
    IReadOnlyList<L12RankedSeasonHonorHistoryView> Honors,
    IReadOnlyList<L12RankedSeasonFactionTotalsHistoryView> FactionTotals,
    string? LatestSeasonName = null);
public sealed record L12RankedSettlementComponent(string Kind, string Label, int Value);
public sealed record L12RankedSettlementView(string MatchId, string AccountId, string Faction,
    string Outcome, bool Won, bool Placement, int PlacementPlayed, int PlacementRequired, int Before, int After,
    int Delta, string TierBefore, string TierAfter, IReadOnlyList<L12RankedSettlementComponent> Components,
    DateTimeOffset SettledAt, string RewardStatus = "applied", int EffectiveDelta = 0,
    int PendingDelta = 0);
public sealed record L12RankedBroadcastView(string Id, string MatchId, string EventType,
    string Message, DateTimeOffset CreatedAt);
public sealed record L12RankedBroadcastClaimView(L12RankedBroadcastView Broadcast,
    string ClaimToken, DateTimeOffset LeaseExpiresAt);
public sealed record L12RankedLeaderboardEntry(int Rank, string Username,
    string Faction, int SevenValue, string DisplayValue, string Tier, string? Title,
    IReadOnlyList<string> Titles, string? FavoriteMasterId, string? FavoriteMasterName,
    int Wins, int Losses, int WinStreak, int? IntervalSevenDelta = null,
    bool IntervalSevenIncomplete = false);
public sealed record L12RankedMasterChampionView(string MasterId, string MasterName,
    string Username, string Title, int SevenValue, string DisplayValue, int Games, int Wins);
public sealed record L12RankedAnalyticsSummary(int Matches, int PlacedPlayers,
    int ActiveMasters, DateTimeOffset? UpdatedAt);
public sealed record L12RankedMasterStatsView(int Rank, string MasterId, string MasterName,
    string? StrongestPlayer, string? Title, int Games, int Wins, int Losses,
    double WinRate, double UsageRate, int FirstGames, int FirstWins, double FirstWinRate,
    int SecondGames, int SecondWins, double SecondWinRate);
public sealed record L12RankedMatchupStatsView(string MasterId, string OpponentMasterId,
    int Games, int Wins, double WinRate, int FirstGames, int FirstWins,
    int SecondGames, int SecondWins);
public sealed record L12RankedAnalyticsView(string Range, L12RankedAnalyticsSummary Summary,
    IReadOnlyList<L12RankedMasterStatsView> Masters,
    IReadOnlyList<L12RankedMatchupStatsView> Matchups,
    DateTimeOffset? FromUtc = null, DateTimeOffset? UntilUtc = null,
    string? SeasonId = null, string? SeasonName = null);
public sealed record L12RankedOverviewView(L12RankedProfileView Profile,
    IReadOnlyDictionary<string, int> FactionTotals, L12RankedConfigView Config,
    IReadOnlyList<L12RankedProfileHistoryView> History);
public sealed record L12RankedSettlementPair(L12RankedSettlementView First,
    L12RankedSettlementView Second, IReadOnlyList<L12RankedBroadcastView> Broadcasts);
public sealed record L12RankedSeasonResetRepairView(string SeasonId, int ProfilesReset,
    int NonzeroSevenValueProfiles, int NonzeroPlacementProfiles, int RankedProfilesWithMatchStats,
    DateTimeOffset AppliedAt, string AppliedBy, bool Replayed,
    DateTimeOffset? CompetitiveStartAt = null, int TransitionMatchesWaived = 0,
    string EvidenceFingerprint = "", long OperationsVersionBefore = 0,
    long OperationsVersionAfter = 0);
public sealed record L12RankedSeasonResetRepairPreviewView(string SeasonId,
    DateTimeOffset? OriginalStartsAt, DateTimeOffset? OriginalActivatedAt, DateTimeOffset? EndsAt,
    int TransitionMatches, int SettlementRows, int ProfileFacts, int ProfilesReset,
    int BroadcastsToRemove, int MasterRecordsToRemove, int GrantsToRevoke,
    string EvidenceFingerprint, int MaximumSupportedMatches,
    DateTimeOffset? CompetitiveStartAt = null);

public sealed partial class L12PlatformStore
{
    private static readonly string[] RankedFactionIds = ["order", "chaos", "fate"];
    private static readonly TimeSpan RankedBroadcastRealtimeWindow = TimeSpan.FromMinutes(2);
    private sealed class RankedTierRow
    {
        public string Name { get; set; } = string.Empty;
        public int Minimum { get; set; }
        public int BaseDelta { get; set; }
        public int WinStreakCap { get; set; }
        public int LossProtectionCap { get; set; }
        public int RatingGapCap { get; set; }
        public int StreakTerminationReward { get; set; }
        public string Color { get; set; } = "#d5b85c";
        public string Icon { get; set; } = string.Empty;
    }
    private sealed class RankedFactionRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#d5b85c";
        public string Icon { get; set; } = string.Empty;
        public string FirstTitle { get; set; } = string.Empty;
        public string TopFiveTitle { get; set; } = string.Empty;
        public List<RankedTierRow> Tiers { get; set; } = [];
    }
    private sealed class RankedConfigRow
    {
        public int PlacementMatches { get; set; } = 5;
        public int PlacementMaximum { get; set; } = 29999;
        public bool BroadcastEnabled { get; set; } = true;
        public L12RankedTimeControlConfig? TimeControl { get; set; }
        public L12RankedBroadcastConfig? Broadcast { get; set; }
        public List<RankedFactionRow> Factions { get; set; } = [];
        public List<RankedMasterTitleRow> MasterTitles { get; set; } = [];
    }
    private sealed class RankedPendingGradientRow
    {
        public string AfterSeasonId { get; set; } = string.Empty;
        public int Version { get; set; } = 2;
        public List<RankedTierGradientRow> Tiers { get; set; } = [];
    }
    private sealed class RankedTierGradientRow
    {
        public string Name { get; set; } = string.Empty;
        public int Minimum { get; set; }
        public int BaseDelta { get; set; }
        public int WinStreakCap { get; set; }
        public int LossProtectionCap { get; set; }
        public int RatingGapCap { get; set; }
        public int StreakTerminationReward { get; set; }
    }
    private sealed class RankedMasterTitleRow
    {
        public string MasterId { get; set; } = string.Empty;
        public string MasterName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }
    private sealed class RankedProfileRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string? Faction { get; set; }
        public double HiddenRating { get; set; } = 1500;
        public int SevenValue { get; set; }
        public int PlacementPlayed { get; set; }
        public int PlacementWins { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int WinStreak { get; set; }
        public int LossStreak { get; set; }
        public int HighestFloor { get; set; }
        public bool ReachedHighestTier { get; set; }
        public string? SelectedMasterTitle { get; set; }
    }
    private sealed class RankedSeasonResetRepairRow
    {
        public string SeasonId { get; set; } = string.Empty;
        public string PreviousSeasonId { get; set; } = string.Empty;
        public int ProfilesReset { get; set; }
        public int NonzeroSevenValueProfiles { get; set; }
        public int NonzeroPlacementProfiles { get; set; }
        public int RankedProfilesWithMatchStats { get; set; }
        public DateTimeOffset AppliedAt { get; set; }
        public string AppliedBy { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset? OriginalStartsAt { get; set; }
        public DateTimeOffset? OriginalActivatedAt { get; set; }
        public DateTimeOffset? CompetitiveStartAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public List<string> TransitionMatchIds { get; set; } = [];
        public string EvidenceFingerprint { get; set; } = string.Empty;
        public int SettlementRowsWaived { get; set; }
        public int ProfileFactsWaived { get; set; }
        public int BroadcastsRemoved { get; set; }
        public int MasterRecordsRemoved { get; set; }
        public int GrantsRevoked { get; set; }
        public long OperationsVersionBefore { get; set; }
        public long OperationsVersionAfter { get; set; }
    }

    private sealed class RankedSeasonResetRepairPlan
    {
        public required SeasonDefinitionRow Season { get; init; }
        public required OperationsConfigRow Operations { get; init; }
        public required DateTimeOffset ObservedAt { get; init; }
        public required DateTimeOffset CompetitiveStartAt { get; init; }
        public required string[] MatchIds { get; init; }
        public required RankedIntegrityAuditRow[] Audits { get; init; }
        public required RankedSettlementRow[] Settlements { get; init; }
        public required RankedSettlementProfileFactRow[] ProfileFacts { get; init; }
        public required RankedBroadcastRow[] Broadcasts { get; init; }
        public required RankedMasterRecordRow[] MasterRecords { get; init; }
        public required AlternateArtGrantRow[] Grants { get; init; }
        public required RankedProfileRow[] Profiles { get; init; }
        public required Dictionary<string, double> HiddenRatingBaselines { get; init; }
        public required string EvidenceFingerprint { get; init; }
    }
    private sealed class RankedProfileHistoryRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AccountId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string UsernameSnapshot { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
        public string FactionNameSnapshot { get; set; } = string.Empty;
        public int SevenValue { get; set; }
        public int PlacementPlayed { get; set; }
        public int PlacementWins { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int WinStreak { get; set; }
        public string SeasonName { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public List<string> Titles { get; set; } = [];
        public string? FactionTitle { get; set; }
        public List<string> MasterTitles { get; set; } = [];
        public bool FinalizedSeasonAwards { get; set; }
        public int? FactionRank { get; set; }
        public int? OverallRank { get; set; }
        public bool? Placed { get; set; }
        public int? PlacementRequired { get; set; }
        public string? RankLabel { get; set; }
        public double? WinRate { get; set; }
        public DateTimeOffset? SummaryAvailableAt { get; set; }
        public DateTimeOffset? SummarySeenAt { get; set; }
        public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;
    }
    private sealed class RankedSettlementRow
    {
        public string MatchId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public bool Won { get; set; }
        public bool Placement { get; set; }
        public int PlacementPlayed { get; set; }
        public int PlacementRequired { get; set; }
        public int Before { get; set; }
        public int After { get; set; }
        public int Delta { get; set; }
        public string TierBefore { get; set; } = string.Empty;
        public string TierAfter { get; set; } = string.Empty;
        public List<L12RankedSettlementComponent> Components { get; set; } = [];
        public DateTimeOffset SettledAt { get; set; } = DateTimeOffset.UtcNow;
    }
    private sealed class RankedBroadcastRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string MatchId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public long Generation { get; set; }
    }
    private sealed class RankedBroadcastDeliveryRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string BroadcastId { get; set; } = string.Empty;
        public string ClaimToken { get; set; } = string.Empty;
        public DateTimeOffset LeaseExpiresAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public long Generation { get; set; }
    }
    private sealed class RankedMasterRecordRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string MasterId { get; set; } = string.Empty;
        public int Games { get; set; }
        public int Wins { get; set; }
        public List<L12RankedMasterTitleMatchFact> TitleFacts { get; set; } = [];
    }
    private sealed class RankedMasterStatsAccumulator
    {
        public required string MasterId { get; init; }
        public int Games { get; set; }
        public int Wins { get; set; }
        public int FirstGames { get; set; }
        public int FirstWins { get; set; }
        public int SecondGames { get; set; }
        public int SecondWins { get; set; }
    }
    private sealed class RankedMatchupAccumulator
    {
        public required string MasterId { get; init; }
        public required string OpponentMasterId { get; init; }
        public int Games { get; set; }
        public int Wins { get; set; }
        public int FirstGames { get; set; }
        public int FirstWins { get; set; }
        public int SecondGames { get; set; }
        public int SecondWins { get; set; }
    }

    private void EnsureRankedState()
    {
        lock (_gate)
        {
            var changed = false;
            if (_data.RankedConfig is null)
            {
                _data.RankedConfig = DefaultRankedConfig();
                changed = true;
            }
            if (_data.RankedConfig.Factions.Count != 3)
            {
                _data.RankedConfig = DefaultRankedConfig();
                changed = true;
            }
            if (_data.RankedGradientVersion <= 0)
            {
                _data.RankedGradientVersion = 1;
                foreach (var faction in _data.RankedConfig.Factions)
                    for (var index = 0; index < faction.Tiers.Count; index++)
                        faction.Tiers[index].StreakTerminationReward = LegacyStreakTerminationReward(index);
                changed = true;
            }
            if (_data.RankedGradientVersion < 2 && _data.RankedPendingGradient is null
                && IsLegacySeasonLifecycleMigrationPending())
            {
                _data.RankedPendingGradient = DefaultPendingRankedGradient(
                    RequireOperationsConfig().Season.Id);
                changed = true;
            }
            _data.RankedProfiles ??= [];
            _data.RankedSeasonResetRepairs ??= [];
            foreach (var repair in _data.RankedSeasonResetRepairs)
                repair.TransitionMatchIds ??= [];
            _data.RankedProfileHistory ??= [];
            foreach (var history in _data.RankedProfileHistory)
            {
                history.Titles ??= [];
                history.MasterTitles ??= [];
            }
            _data.RankedSettlements ??= [];
            _data.RankedBroadcasts ??= [];
            _data.RankedBroadcastDeliveries ??= [];
            _data.RankedMasterRecords ??= [];
            foreach (var record in _data.RankedMasterRecords) record.TitleFacts ??= [];
            _data.RankedMasterRecordedMatchIds ??= [];
            _data.RankedIntegrityAudits ??= [];
            _data.RankedHeldRewards ??= [];
            _data.RankedSettlementProfileFacts ??= [];
            _data.RankedIntegrityDecisions ??= [];
            _data.RankedIntegrityCorrections ??= [];
            _data.RankedIntegrityNotifications ??= [];
            _data.RankedIntegrityAppeals ??= [];
            _data.RankedConfig.MasterTitles ??= [];
            if (_data.RankedConfig.TimeControl is null)
            {
                _data.RankedConfig.TimeControl = DefaultRankedTimeControl();
                changed = true;
            }
            if (_data.RankedBroadcastDeliveryCutover is null)
            {
                _data.RankedBroadcastDeliveryCutover = DateTimeOffset.UtcNow;
                changed = true;
            }
            foreach (var masterId in SelectableMasterIds())
            {
                if (_data.RankedConfig.MasterTitles.Any(item => item.MasterId.Equals(masterId,
                        StringComparison.OrdinalIgnoreCase))) continue;
                var masterName = _officialCards.TryGetValue(masterId, out var master) ? master.NameZh : masterId;
                _data.RankedConfig.MasterTitles.Add(new RankedMasterTitleRow
                {
                    MasterId = masterId,
                    MasterName = masterName,
                    Title = DefaultMasterTitle(masterName),
                });
                changed = true;
            }
            if (changed) Save();
        }
    }

    private static string DefaultMasterTitle(string masterName)
        => $"最强{(masterName == "天照大神" ? "天照" : masterName)}";

    internal static L12RankedTimeControlConfig DefaultRankedTimeControl()
        => new(1500, 240, 240, 60, 60);

    internal static L12RankedBroadcastConfig DefaultRankedBroadcastConfig()
        => new(16, 3, 15, 5, 5, 0, true, true, true, true, true);

    private static RankedConfigRow DefaultRankedConfig()
    {
        static List<RankedTierRow> Tiers() =>
        [
            new() { Name = "初阶", Minimum = 0, BaseDelta = 200, WinStreakCap = 100, LossProtectionCap = 50, RatingGapCap = 50, StreakTerminationReward = 0, Color = "#87959c" },
            new() { Name = "进阶", Minimum = 15000, BaseDelta = 400, WinStreakCap = 200, LossProtectionCap = 100, RatingGapCap = 100, StreakTerminationReward = 200, Color = "#67a7b7" },
            new() { Name = "精英", Minimum = 30000, BaseDelta = 800, WinStreakCap = 400, LossProtectionCap = 200, RatingGapCap = 200, StreakTerminationReward = 400, Color = "#8d73c7" },
            new() { Name = "统领", Minimum = 60000, BaseDelta = 1500, WinStreakCap = 750, LossProtectionCap = 380, RatingGapCap = 380, StreakTerminationReward = 750, Color = "#d5904b" },
            new() { Name = "冠冕", Minimum = 100000, BaseDelta = 2500, WinStreakCap = 1250, LossProtectionCap = 630, RatingGapCap = 630, StreakTerminationReward = 1250, Color = "#e4c15e" },
        ];
        return new RankedConfigRow
        {
            TimeControl = DefaultRankedTimeControl(),
            Broadcast = DefaultRankedBroadcastConfig(),
            Factions =
            [
                new() { Id = "order", Name = "秩序", Color = "#5ea4c7", FirstTitle = "秩序冠首", TopFiveTitle = "秩序中枢", Tiers = Tiers() },
                new() { Id = "chaos", Name = "混沌", Color = "#c05d65", FirstTitle = "混沌冠首", TopFiveTitle = "混沌先声", Tiers = Tiers() },
                new() { Id = "fate", Name = "命运", Color = "#b698d2", FirstTitle = "命运冠首", TopFiveTitle = "命运织者", Tiers = Tiers() },
            ],
        };
    }

    private static RankedPendingGradientRow DefaultPendingRankedGradient(string afterSeasonId)
        => new()
        {
            AfterSeasonId = afterSeasonId,
            Version = 2,
            Tiers =
            [
                new() { Name = "初阶", Minimum = 0, BaseDelta = 3800, WinStreakCap = 3300, LossProtectionCap = 3500, RatingGapCap = 600, StreakTerminationReward = 100 },
                new() { Name = "进阶", Minimum = 15000, BaseDelta = 4000, WinStreakCap = 2900, LossProtectionCap = 3400, RatingGapCap = 500, StreakTerminationReward = 150 },
                new() { Name = "精英", Minimum = 30000, BaseDelta = 4200, WinStreakCap = 2400, LossProtectionCap = 3100, RatingGapCap = 400, StreakTerminationReward = 200 },
                new() { Name = "统领", Minimum = 60000, BaseDelta = 4700, WinStreakCap = 1600, LossProtectionCap = 2400, RatingGapCap = 250, StreakTerminationReward = 300 },
                new() { Name = "冠冕", Minimum = 100000, BaseDelta = 1200, WinStreakCap = 100, LossProtectionCap = 0, RatingGapCap = 120, StreakTerminationReward = 400 },
            ],
        };

    public L12RankedConfigView RankedConfig(L12AccountView? actor = null)
    {
        if (actor is not null) EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate) return ToView(_data.RankedConfig!);
    }

    internal L12RankedTimeControlConfig RankedTimeControl()
    {
        lock (_gate) return NormalizeRankedTimeControl(_data.RankedConfig?.TimeControl);
    }

    public L12RankedBroadcastConfig RankedBroadcastSettings()
    {
        lock (_gate) return NormalizeRankedBroadcastConfig(_data.RankedConfig?.Broadcast);
    }

    public L12RankedConfigView UpdateRankedConfig(L12AccountView actor, L12RankedConfigView value,
        string reason, L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        if (string.IsNullOrWhiteSpace(reason)) throw new L12OperationsConfigException("reason_required", "请填写排位配置变更理由");
        var normalized = NormalizeRankedConfig(value);
        lock (_gate)
        {
            _data.RankedConfig = normalized;
            SyncActiveSeasonDefinitionFromRuntime(actor);
            AddAdminAudit(actor, "operations", "ranked-config-apply", "ranked:config", null, null,
                reason.Trim(), context with { Reason = reason.Trim(), Outcome = "succeeded" });
            Save();
            return ToView(normalized);
        }
    }

    public L12RankedProfileView RankedProfile(string accountId)
    {
        lock (_gate)
        {
            var row = RequireRankedProfile(accountId);
            return ProfileView(row);
        }
    }

    public L12RankedProfileView SelectRankedFaction(string accountId, string faction)
    {
        faction = faction.Trim().ToLowerInvariant();
        if (!RankedFactionIds.Contains(faction)) throw new ArgumentException("派系只能选择秩序、混沌或命运");
        lock (_gate)
        {
            var row = RequireRankedProfile(accountId);
            if (!string.Equals(row.Faction, faction, StringComparison.OrdinalIgnoreCase))
            {
                ArchiveRankedProfile(row);
                _data.RankedMasterRecords.RemoveAll(item => item.AccountId == accountId
                    && SeasonIdsEqual(item.SeasonId, row.SeasonId));
                row.Faction = faction;
                row.SevenValue = row.PlacementPlayed = row.PlacementWins = row.Wins = row.Losses = 0;
                row.WinStreak = row.LossStreak = row.HighestFloor = 0;
                row.ReachedHighestTier = false;
                row.SelectedMasterTitle = null;
                Save();
            }
            return ProfileView(row);
        }
    }

    public L12RankedProfileView SelectRankedMasterTitle(string accountId, string? title)
    {
        lock (_gate)
        {
            var row = RequireRankedProfile(accountId);
            var available = PlayerMasterTitles(row, CurrentMasterChampions());
            var normalized = title?.Trim();
            if (!string.IsNullOrWhiteSpace(normalized)
                && !available.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("只能选择当前赛季已获得的最强主宰称号");
            row.SelectedMasterTitle = string.IsNullOrWhiteSpace(normalized) ? null : available
                .First(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            Save();
            return ProfileView(row);
        }
    }

    public L12RankedBattleIdentityView RankedBattleIdentity(string accountId, int playerIndex)
        => ReadCommittedRankedBattleIdentity(accountId, playerIndex);

    internal L12RankedBattleIdentityView RankedBattleIdentityAt(string accountId, int playerIndex,
        DateTimeOffset utcNow)
        => ReadCommittedRankedBattleIdentityAt(accountId, playerIndex, utcNow);

    internal double HiddenRating(string accountId)
    {
        lock (_gate) return RequireRankedProfile(accountId).HiddenRating;
    }

    public L12RankedOverviewView RankedOverview(string accountId)
    {
        lock (_gate)
        {
            var profile = RequireRankedProfile(accountId);
            var history = _data.RankedProfileHistory.Where(item => item.AccountId == accountId
                    && item.FinalizedSeasonAwards)
                .OrderByDescending(item => item.ArchivedAt)
                .Select(item => new L12RankedProfileHistoryView(item.SeasonId,
                    HistoricalHonorFactionNameLocked(item), item.SevenValue, item.PlacementPlayed,
                    item.PlacementWins, item.Wins, item.Losses, item.WinStreak, item.ArchivedAt,
                    HistoricalSeasonDisplayNameLocked(item.SeasonId, item.SeasonName),
                    item.Tier, $"七曜值 {item.SevenValue:N0}",
                    item.WinRate ?? (item.Wins + item.Losses == 0 ? null
                        : Percentage(item.Wins, item.Wins + item.Losses)),
                    item.FactionTitle, item.MasterTitles.ToArray(), item.Titles.ToArray(),
                    item.FactionRank, item.OverallRank, item.Placed, item.PlacementRequired,
                    item.RankLabel, HistoricalSeasonMonthLabelLocked(item.SeasonId)))
                .ToArray();
            return new L12RankedOverviewView(ProfileView(profile), FactionTotalsLocked(),
                ToView(_data.RankedConfig!), history);
        }
    }

    public IReadOnlyList<L12SeasonSummaryNotificationView> PendingSeasonSummaryNotifications(
        string accountId)
    {
        lock (_gate)
        {
            _ = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Disabled && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在或不可用");
            return _data.RankedProfileHistory.Where(row => row.AccountId == accountId
                    && row.FinalizedSeasonAwards && row.SummaryAvailableAt is not null
                    && row.SummarySeenAt is null)
                .OrderBy(row => row.SummaryAvailableAt).ThenBy(row => row.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Take(20).Select(SeasonSummaryView).ToArray();
        }
    }

    public void AcknowledgeSeasonSummaryNotification(string accountId, string id)
    {
        lock (_gate)
        {
            var row = _data.RankedProfileHistory.FirstOrDefault(item => item.Id == id
                && item.AccountId == accountId && item.FinalizedSeasonAwards
                && item.SummaryAvailableAt is not null)
                ?? throw new KeyNotFoundException("赛季总结不存在");
            if (row.SummarySeenAt is not null) return;
            row.SummarySeenAt = DateTimeOffset.UtcNow;
            Save();
        }
    }

    internal IReadOnlyList<string> SeasonSummaryRecipients(string seasonId)
    {
        lock (_gate) return _data.RankedProfileHistory.Where(row => row.FinalizedSeasonAwards
                && row.SummaryAvailableAt is not null && SeasonIdsEqual(row.SeasonId, seasonId))
            .Select(row => row.AccountId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<L12RankedSeasonHonorView> RankedSeasonHonors(int limit = 500)
    {
        lock (_gate)
        {
            return _data.RankedProfileHistory
                .Where(row => row.FinalizedSeasonAwards && row.Titles.Count > 0
                    && IsActiveAccountLocked(row.AccountId))
                .OrderByDescending(row => row.ArchivedAt).ThenByDescending(row => row.SevenValue)
                .Take(Math.Clamp(limit, 1, 2000))
                .Select(row => new L12RankedSeasonHonorView(row.SeasonId,
                    string.IsNullOrWhiteSpace(row.SeasonName) ? row.SeasonId : row.SeasonName,
                    string.IsNullOrWhiteSpace(row.UsernameSnapshot) ? AccountName(row.AccountId)
                        : L12UsernamePolicy.PublicName(row.UsernameSnapshot),
                    FactionFor(row.Faction).Name,
                    string.IsNullOrWhiteSpace(row.Tier) ? FactionFor(row.Faction).Tiers[RankedTierIndex(row.SevenValue)].Name : row.Tier,
                    row.SevenValue, $"七曜值 {row.SevenValue:N0}", row.Titles.ToArray(), row.ArchivedAt))
                .ToArray();
        }
    }

    public IReadOnlyList<L12RankedSeasonHonorHistoryView> RankedSeasonHonorHistory(int limit = 500)
    {
        lock (_gate)
        {
            return _data.RankedProfileHistory
                .Where(row => row.FinalizedSeasonAwards && row.Titles.Count > 0)
                .SelectMany(row => row.Titles.Where(title => !string.IsNullOrWhiteSpace(title))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(title => (Row: row, Title: title.Trim())))
                .GroupBy(item => (SeasonId: item.Row.SeasonId.Trim().ToUpperInvariant(), item.Title))
                .Select(group =>
                {
                    var rows = group.Select(item => item.Row).ToArray();
                    var seasonName = HistoricalSeasonDisplayNameLocked(group.Key.SeasonId,
                        rows.Select(row => row.SeasonName?.Trim())
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));
                    var winners = rows
                        .GroupBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase)
                        .Select(accounts => accounts.OrderByDescending(row => row.SevenValue)
                            .ThenByDescending(row => row.ArchivedAt).First())
                        .OrderByDescending(row => row.SevenValue)
                        .ThenBy(row => HistoricalHonorUsernameLocked(row), StringComparer.Ordinal)
                        .Select(row => new L12RankedSeasonHonorWinnerView(
                            HistoricalHonorUsernameLocked(row), HistoricalHonorFactionNameLocked(row)))
                        .ToArray();
                    return (View: new L12RankedSeasonHonorHistoryView(seasonName,
                        group.Key.Title, winners, HistoricalHonorMasterIdLocked(group.Key.SeasonId,
                            group.Key.Title)), AwardedAt: rows.Max(row => row.ArchivedAt));
                })
                .OrderBy(row => row.View.Title, StringComparer.Ordinal)
                .ThenByDescending(row => row.AwardedAt)
                .ThenBy(row => row.View.SeasonName, StringComparer.Ordinal)
                .Take(Math.Clamp(limit, 1, 2000))
                .Select(row => row.View)
                .ToArray();
        }
    }

    public L12RankedSeasonHistoryView RankedSeasonHistory(int limit = 500)
    {
        lock (_gate)
        {
            var totals = _data.SeasonArchives
                .Where(row => row.FactionFinalTotals.Count > 0)
                .Select(row => (Key: string.IsNullOrWhiteSpace(row.SourceDefinitionId)
                        ? $"season:{row.SeasonId.Trim().ToUpperInvariant()}"
                        : $"definition:{row.SourceDefinitionId}", row.SeasonId, row.Name,
                    row.SeasonOrdinal, At: row.ArchivedAt, row.FactionFinalTotals))
                .Concat(_data.SeasonDefinitions.Where(row => row.FinalizedAt is not null
                        && row.FactionFinalTotals.Count > 0)
                    .Select(row => (Key: $"definition:{row.DefinitionId}", row.SeasonId, row.Name,
                        row.SeasonOrdinal, At: row.FinalizedAt!.Value, row.FactionFinalTotals)))
                .GroupBy(row => row.Key, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(row => row.At).First())
                .OrderByDescending(row => row.At)
                .Take(Math.Clamp(limit, 1, 2000))
                .Select(row => new L12RankedSeasonFactionTotalsHistoryView(
                    HistoricalSeasonDisplayNameLocked(row.SeasonId, row.Name, row.SeasonOrdinal),
                    row.FactionFinalTotals.OrderBy(total => FactionDisplayOrder(total.FactionId))
                        .ThenBy(total => total.FactionNameSnapshot, StringComparer.Ordinal)
                        .Select(total => new L12RankedSeasonFactionFinalValueView(
                            total.FactionNameSnapshot, total.Value, total.Value.ToString("N0")))
                        .ToArray()))
                .ToArray();
            var honors = RankedSeasonHonorHistory(limit);
            var latestSeasonName = totals.FirstOrDefault()?.SeasonName;
            if (latestSeasonName is null)
            {
                var latest = _data.RankedProfileHistory.Where(row => row.FinalizedSeasonAwards)
                    .OrderByDescending(row => row.ArchivedAt).FirstOrDefault();
                if (latest is not null)
                    latestSeasonName = HistoricalSeasonDisplayNameLocked(latest.SeasonId,
                        latest.SeasonName);
            }
            return new(honors, totals, latestSeasonName);
        }
    }

    private string HistoricalHonorUsernameLocked(RankedProfileHistoryRow row)
    {
        var account = _data.Accounts.FirstOrDefault(item => item.Id.Equals(row.AccountId,
            StringComparison.OrdinalIgnoreCase));
        if (account?.Deleted == true) return "已注销玩家";
        if (!string.IsNullOrWhiteSpace(row.UsernameSnapshot))
            return L12UsernamePolicy.PublicName(row.UsernameSnapshot);
        return account is null ? "已注销玩家" : PublicUsername(account);
    }

    private string HistoricalHonorFactionNameLocked(RankedProfileHistoryRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.FactionNameSnapshot)) return row.FactionNameSnapshot.Trim();
        return row.Faction.Trim().ToLowerInvariant() switch
        {
            "order" => "秩序",
            "chaos" => "混沌",
            "fate" => "命运",
            _ => _data.RankedConfig!.Factions.FirstOrDefault(faction => faction.Id.Equals(row.Faction,
                StringComparison.OrdinalIgnoreCase))?.Name ?? row.Faction,
        };
    }

    private sealed record EligibleRankedAnalyticsMatch(L12RankingMatch Match, DateTimeOffset Started,
        DateTimeOffset Ended, string Master0, string Master1);

    private sealed class RankedIntervalPlayerAccumulator
    {
        public required string AccountId { get; init; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int SevenDelta { get; set; }
        public bool SevenIncomplete { get; set; }
        public Dictionary<string, (int Games, int Wins)> Masters { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public int Games => Wins + Losses;
    }

    private string? HistoricalHonorMasterIdLocked(string seasonId, string title)
    {
        var archive = _data.SeasonArchives.FirstOrDefault(row => SeasonIdsEqual(row.SeasonId, seasonId));
        var definition = _data.SeasonDefinitions.FirstOrDefault(row => SeasonIdsEqual(row.SeasonId, seasonId));
        return archive?.Configuration?.Ranked?.MasterTitles?.FirstOrDefault(row => row.Title.Equals(title,
                   StringComparison.OrdinalIgnoreCase))?.MasterId
            ?? definition?.Configuration?.Ranked?.MasterTitles?.FirstOrDefault(row => row.Title.Equals(title,
                   StringComparison.OrdinalIgnoreCase))?.MasterId
            ?? _data.RankedConfig?.MasterTitles?.FirstOrDefault(row => row.Title.Equals(title,
                   StringComparison.OrdinalIgnoreCase))?.MasterId;
    }

    private string? HistoricalSeasonMonthLabelLocked(string seasonId)
    {
        var archive = _data.SeasonArchives.FirstOrDefault(row => SeasonIdsEqual(row.SeasonId, seasonId));
        var definition = _data.SeasonDefinitions.FirstOrDefault(row => SeasonIdsEqual(row.SeasonId, seasonId));
        var monthDate = archive?.StartsAt ?? definition?.StartsAt ?? archive?.EndsAt ?? definition?.EndsAt;
        if (monthDate is null) return null;
        var local = monthDate.Value.ToOffset(TimeSpan.FromHours(8));
        return $"{local.Year}年{local.Month}月";
    }

    private string HistoricalSeasonDisplayNameLocked(string seasonId, string? frozenName,
        int? ordinal = null, string fallback = "历史赛季")
    {
        var name = frozenName?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && !LooksLikeInternalSeasonCode(name)) return name;
        var archive = _data.SeasonArchives.FirstOrDefault(row => SeasonIdsEqual(row.SeasonId, seasonId));
        name = archive?.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && !LooksLikeInternalSeasonCode(name)) return name;
        ordinal ??= archive?.SeasonOrdinal ?? _data.SeasonDefinitions.FirstOrDefault(row =>
            SeasonIdsEqual(row.SeasonId, seasonId))?.SeasonOrdinal;
        return ordinal is { } value ? $"第{value}赛季" : fallback;
    }

    private static bool LooksLikeInternalSeasonCode(string value)
    {
        var normalized = value.Trim();
        return normalized.Length >= 2
            && (normalized[0] is 'S' or 's' or 'T' or 't')
            && normalized.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static int FactionDisplayOrder(string factionId) => factionId.Trim().ToLowerInvariant() switch
    {
        "order" => 0,
        "chaos" => 1,
        "fate" => 2,
        _ => 3,
    };

    public IReadOnlyList<L12RankedLeaderboardEntry> RankedLeaderboard(string? faction = null, int limit = 50,
        string? viewerAccountId = null)
    {
        lock (_gate)
        {
            var season = RequireOperationsConfig().Season.Id;
            var rows = _data.RankedProfiles.Where(row => SeasonIdsEqual(row.SeasonId, season)
                    && row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
                    && !string.IsNullOrWhiteSpace(row.Faction)
                    && IsActiveAccountLocked(row.AccountId)
                    && (string.IsNullOrWhiteSpace(faction) || string.Equals(row.Faction, faction, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(row => row.SevenValue).ThenByDescending(row => row.HiddenRating)
                .ThenBy(row => AccountName(row.AccountId), StringComparer.OrdinalIgnoreCase).ToArray();
            var champions = CurrentMasterChampions();
            var visibleLimit = Math.Clamp(limit, 1, 50);
            var visible = rows.Take(visibleLimit).Select((row, index) => (Row: row, Rank: index + 1)).ToList();
            if (!string.IsNullOrWhiteSpace(viewerAccountId))
            {
                var viewerIndex = Array.FindIndex(rows, row => string.Equals(row.AccountId, viewerAccountId,
                    StringComparison.OrdinalIgnoreCase));
                if (viewerIndex >= visibleLimit)
                    visible.Add((rows[viewerIndex], viewerIndex + 1));
            }
            return visible.Select(item => LeaderboardView(item.Row, item.Rank, champions)).ToArray();
        }
    }

    public IReadOnlyList<L12RankedLeaderboardEntry> RankedIntervalLeaderboard(
        IReadOnlyList<L12RankingMatch> source, string requestedRange, string? faction = null,
        int limit = 50, string? viewerAccountId = null, DateTimeOffset? observedAt = null)
    {
        if (requestedRange is not ("7d" or "30d"))
            throw new ArgumentException("Only rolling ranking ranges are supported", nameof(requestedRange));
        lock (_gate)
        {
            var now = observedAt ?? DateTimeOffset.UtcNow;
            var start = now.AddDays(requestedRange == "7d" ? -7 : -30);
            var matches = EligibleRankedAnalyticsMatchesLocked(source, start, now, now);
            var settlements = _data.RankedSettlements.ToLookup(row =>
                $"{row.MatchId}\u001f{row.AccountId}", StringComparer.OrdinalIgnoreCase);
            var players = new Dictionary<string, RankedIntervalPlayerAccumulator>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in matches)
            {
                // Legacy name-only rows cannot be assigned to a stable account after renames.
                if (string.IsNullOrWhiteSpace(item.Match.AccountId0)
                    || string.IsNullOrWhiteSpace(item.Match.AccountId1)) continue;
                Add(item.Match.AccountId0, item.Master0, item.Match.Winner == 0, item.Match.MatchId);
                Add(item.Match.AccountId1, item.Master1, item.Match.Winner == 1, item.Match.MatchId);
            }

            var currentSeason = RequireOperationsConfig().Season.Id;
            var currentProfiles = _data.RankedProfiles.Where(row => SeasonIdsEqual(row.SeasonId, currentSeason))
                .ToDictionary(row => row.AccountId, StringComparer.OrdinalIgnoreCase);
            var ordered = players.Values.Where(row => string.IsNullOrWhiteSpace(faction)
                    || currentProfiles.TryGetValue(row.AccountId, out var profile)
                    && string.Equals(profile.Faction, faction, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.SevenIncomplete).ThenByDescending(row => row.SevenDelta)
                .ThenByDescending(row => row.Wins).ThenByDescending(row => row.Games)
                .ThenBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase).ToArray();
            var champions = CurrentMasterChampions();
            var visibleLimit = Math.Clamp(limit, 1, 50);
            var visible = ordered.Take(visibleLimit).Select((row, index) => (Row: row, Rank: index + 1)).ToList();
            if (!string.IsNullOrWhiteSpace(viewerAccountId))
            {
                var viewerIndex = Array.FindIndex(ordered, row => row.AccountId.Equals(viewerAccountId,
                    StringComparison.OrdinalIgnoreCase));
                if (viewerIndex >= visibleLimit) visible.Add((ordered[viewerIndex], viewerIndex + 1));
            }
            return visible.Select(item =>
            {
                currentProfiles.TryGetValue(item.Row.AccountId, out var current);
                var hasFaction = current is not null && !string.IsNullOrWhiteSpace(current.Faction);
                var placed = hasFaction && current!.PlacementPlayed >= _data.RankedConfig!.PlacementMatches;
                var titles = hasFaction ? PlayerTitles(current!, FactionRank(current!), champions) : [];
                var favorite = item.Row.Masters.OrderByDescending(pair => pair.Value.Games)
                    .ThenByDescending(pair => pair.Value.Wins)
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Key).FirstOrDefault();
                var displayDelta = item.Row.SevenIncomplete ? "未记录"
                    : $"{(item.Row.SevenDelta >= 0 ? "+" : "")}{item.Row.SevenDelta:N0}";
                return new L12RankedLeaderboardEntry(item.Rank, AccountName(item.Row.AccountId),
                    hasFaction ? FactionFor(current!.Faction!).Name : "本赛季未选择",
                    current?.SevenValue ?? 0, displayDelta,
                    placed ? TierFor(current!).Name : "本赛季未定级", titles.FirstOrDefault(), titles,
                    favorite, favorite is null ? null : MasterName(favorite), item.Row.Wins, item.Row.Losses, 0,
                    item.Row.SevenIncomplete ? null : item.Row.SevenDelta, item.Row.SevenIncomplete);
            }).ToArray();

            void Add(string accountId, string masterId, bool won, string matchId)
            {
                if (!players.TryGetValue(accountId, out var player))
                {
                    player = new RankedIntervalPlayerAccumulator { AccountId = accountId };
                    players.Add(accountId, player);
                }
                if (won) player.Wins++; else player.Losses++;
                player.Masters.TryGetValue(masterId, out var master);
                player.Masters[masterId] = (master.Games + 1, master.Wins + (won ? 1 : 0));
                var facts = settlements[$"{matchId}\u001f{accountId}"].ToArray();
                if (facts.Length != 1 || facts[0].Delta != facts[0].After - facts[0].Before)
                    player.SevenIncomplete = true;
                else player.SevenDelta += facts[0].Delta;
            }
        }
    }

    private EligibleRankedAnalyticsMatch[] EligibleRankedAnalyticsMatchesLocked(
        IReadOnlyList<L12RankingMatch> source, DateTimeOffset rangeStart, DateTimeOffset rangeEnd,
        DateTimeOffset now)
        => source.DistinctBy(match => match.MatchId, StringComparer.OrdinalIgnoreCase).Select(match => new
            {
                Match = match,
                Started = DateTimeOffset.TryParse(match.StartedUtc, out var started) ? started : (DateTimeOffset?)null,
                Ended = DateTimeOffset.TryParse(match.EndedUtc, out var ended) ? ended : (DateTimeOffset?)null,
                Master0 = RankingMasterId(match.MasterId0, match.Master0),
                Master1 = RankingMasterId(match.MasterId1, match.Master1),
            })
            .Where(item => !IsRankedMatchExcludedLocked(item.Match.MatchId)
                && (item.Match.AccountId0 is null || IsActiveAccountLocked(item.Match.AccountId0))
                && (item.Match.AccountId1 is null || IsActiveAccountLocked(item.Match.AccountId1))
                && item.Started is not null && item.Started >= rangeStart && item.Started <= rangeEnd
                && item.Ended is not null && item.Ended >= item.Started && item.Ended <= now
                && item.Match.Winner is 0 or 1 && item.Master0 is not null && item.Master1 is not null)
            .Select(item => new EligibleRankedAnalyticsMatch(item.Match, item.Started!.Value,
                item.Ended!.Value, item.Master0!, item.Master1!)).ToArray();

    public IReadOnlyList<L12RankedMasterChampionView> RankedMasterChampions()
    {
        lock (_gate)
        {
            return CurrentMasterChampions().Values
                .Select(record =>
                {
                    var currentSeason = RequireOperationsConfig().Season.Id;
                    var profile = _data.RankedProfiles.FirstOrDefault(row => row.AccountId == record.AccountId
                        && SeasonIdsEqual(row.SeasonId, currentSeason));
                    var masterName = MasterName(record.MasterId);
                    return new L12RankedMasterChampionView(record.MasterId, masterName,
                        AccountName(record.AccountId), MasterTitle(record.MasterId), profile?.SevenValue ?? 0,
                        $"七曜值 {(profile?.SevenValue ?? 0):N0}", record.Games, record.Wins);
                })
                .OrderBy(item => item.MasterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public L12RankedAnalyticsView RankedAnalytics(IReadOnlyList<L12RankingMatch> source, string? requestedRange,
        DateTimeOffset? observedAt = null)
    {
        lock (_gate)
        {
            var range = requestedRange is "7d" or "30d" ? requestedRange : "season";
            var season = RequireOperationsConfig().Season;
            var now = observedAt ?? DateTimeOffset.UtcNow;
            // A later season can be activated without a configured start date. Its activation
            // is the boundary; the initial season keeps its legacy unbounded start.
            var seasonStart = season.StartsAt ?? _data.SeasonDefinitions.FirstOrDefault(row =>
                row.LifecycleStatus == "active" && SeasonIdsEqual(row.SeasonId, season.Id)
                && !string.IsNullOrWhiteSpace(row.PreviousSeasonId))?.ActivatedAt ?? DateTimeOffset.MinValue;
            var rangeStart = range == "7d" ? now.AddDays(-7) : range == "30d" ? now.AddDays(-30) : seasonStart;
            // Rolling windows span season cutovers; only the season view is bounded by this season.
            var rangeEnd = range == "season" ? season.EndsAt ?? DateTimeOffset.MaxValue : now;
            var matches = EligibleRankedAnalyticsMatchesLocked(source, rangeStart, rangeEnd, now);
            var masters = new Dictionary<string, RankedMasterStatsAccumulator>(StringComparer.OrdinalIgnoreCase);
            var matchups = new Dictionary<string, RankedMatchupAccumulator>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in matches)
            {
                AddMaster(item.Master0!, item.Match.Winner == 0, item.Match.FirstPlayer == 0);
                AddMaster(item.Master1!, item.Match.Winner == 1, item.Match.FirstPlayer == 1);
                AddMatchup(item.Master0!, item.Master1!, item.Match.Winner == 0, item.Match.FirstPlayer == 0);
                AddMatchup(item.Master1!, item.Master0!, item.Match.Winner == 1, item.Match.FirstPlayer == 1);
            }
            var champions = CurrentMasterChampions();
            var totalAppearances = Math.Max(1, masters.Values.Sum(item => item.Games));
            var ordered = masters.Values.OrderByDescending(item => Percentage(item.Wins, item.Games))
                .ThenByDescending(item => item.Games).ThenBy(item => MasterName(item.MasterId), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var masterViews = ordered.Select((item, index) =>
            {
                champions.TryGetValue(item.MasterId, out var champion);
                return new L12RankedMasterStatsView(index + 1, item.MasterId, MasterName(item.MasterId),
                    champion is null ? null : AccountName(champion.AccountId),
                    champion is null ? null : MasterTitle(item.MasterId), item.Games, item.Wins,
                    item.Games - item.Wins, Percentage(item.Wins, item.Games),
                    Percentage(item.Games, totalAppearances), item.FirstGames, item.FirstWins,
                    Percentage(item.FirstWins, item.FirstGames), item.SecondGames, item.SecondWins,
                    Percentage(item.SecondWins, item.SecondGames));
            }).ToArray();
            var matchupViews = matchups.Values.OrderBy(item => item.MasterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.OpponentMasterId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new L12RankedMatchupStatsView(item.MasterId, item.OpponentMasterId,
                    item.Games, item.Wins, Percentage(item.Wins, item.Games), item.FirstGames,
                    item.FirstWins, item.SecondGames, item.SecondWins)).ToArray();
            var placedPlayers = _data.RankedProfiles.Count(row => SeasonIdsEqual(row.SeasonId, season.Id)
                && row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
                && _data.Accounts.Any(account => account.Id == row.AccountId && !account.Disabled && !account.Deleted));
            var updatedAt = matches.Select(item => item.Ended).DefaultIfEmpty().Max();
            var scopeFrom = rangeStart == DateTimeOffset.MinValue ? (DateTimeOffset?)null : rangeStart;
            var scopeUntil = rangeEnd < now ? rangeEnd : now;
            return new L12RankedAnalyticsView(range,
                new L12RankedAnalyticsSummary(matches.Length, placedPlayers, masters.Count,
                    updatedAt == default ? null : updatedAt), masterViews, matchupViews,
                scopeFrom, scopeUntil, range == "season" ? season.Id : null,
                range == "season" ? season.Name : null);

            void AddMaster(string masterId, bool won, bool first)
            {
                if (!masters.TryGetValue(masterId, out var row))
                {
                    row = new RankedMasterStatsAccumulator { MasterId = masterId };
                    masters.Add(masterId, row);
                }
                row.Games++;
                if (won) row.Wins++;
                if (first)
                {
                    row.FirstGames++;
                    if (won) row.FirstWins++;
                }
                else
                {
                    row.SecondGames++;
                    if (won) row.SecondWins++;
                }
            }

            void AddMatchup(string masterId, string opponentMasterId, bool won, bool first)
            {
                var key = $"{masterId}|{opponentMasterId}";
                if (!matchups.TryGetValue(key, out var row))
                {
                    row = new RankedMatchupAccumulator { MasterId = masterId, OpponentMasterId = opponentMasterId };
                    matchups.Add(key, row);
                }
                row.Games++;
                if (won) row.Wins++;
                if (first)
                {
                    row.FirstGames++;
                    if (won) row.FirstWins++;
                }
                else
                {
                    row.SecondGames++;
                    if (won) row.SecondWins++;
                }
            }
        }
    }

    private static double Percentage(int numerator, int denominator)
        => denominator <= 0 ? 0d : Math.Round(numerator * 100d / denominator, 1, MidpointRounding.AwayFromZero);

    public int ImportRankedMasterHistory(IReadOnlyList<L12RankingMatch> matches)
    {
        lock (_gate)
        {
            var season = RequireOperationsConfig().Season;
            var imported = 0;
            foreach (var match in matches.OrderBy(item => item.StartedUtc, StringComparer.Ordinal))
            {
                if (match.Winner is not (0 or 1)
                    || IsRankedMatchExcludedLocked(match.MatchId)
                    || _data.RankedMasterRecordedMatchIds.Contains(match.MatchId, StringComparer.OrdinalIgnoreCase)
                    || !DateTimeOffset.TryParse(match.StartedUtc, out var started)
                    || (season.StartsAt is not null && started < season.StartsAt)
                    || (season.EndsAt is not null && started > season.EndsAt)) continue;
                var first = RankedProfileByUsername(match.Player0, season.Id);
                var second = RankedProfileByUsername(match.Player1, season.Id);
                var firstMasterId = MasterIdByName(match.Master0);
                var secondMasterId = MasterIdByName(match.Master1);
                var recorded = false;
                if (first is not null && firstMasterId is not null)
                {
                    UpdateMasterRecord(first, firstMasterId, match.Winner == 0);
                    recorded = true;
                }
                if (second is not null && secondMasterId is not null)
                {
                    UpdateMasterRecord(second, secondMasterId, match.Winner == 1);
                    recorded = true;
                }
                if (!recorded) continue;
                _data.RankedMasterRecordedMatchIds.Add(match.MatchId);
                imported++;
            }
            if (imported > 0) Save();
            return imported;
        }
    }

    public L12RankedSettlementView? RankedSettlement(string matchId, string accountId)
    {
        lock (_gate)
        {
            var row = _data.RankedSettlements.FirstOrDefault(item => item.MatchId == matchId && item.AccountId == accountId);
            return row is null ? null : ToView(row);
        }
    }

    public IReadOnlyList<L12RankedBroadcastView> RankedBroadcasts(int limit = 30)
    {
        lock (_gate) return _data.RankedBroadcasts.OrderByDescending(row => row.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100)).Select(ToView).ToArray();
    }

    public L12RankedBroadcastClaimView? ClaimRankedBroadcast(string accountId,
        DateTimeOffset? subscriptionStartedAt = null)
        => ClaimRankedBroadcastAt(accountId, subscriptionStartedAt, DateTimeOffset.UtcNow);

    internal L12RankedBroadcastClaimView? ClaimRankedBroadcastAt(string accountId,
        DateTimeOffset? subscriptionStartedAt, DateTimeOffset now)
        => ClaimRankedBroadcastObject(accountId, subscriptionStartedAt, now,
            RankedBroadcastRealtimeWindow);

    public bool CompleteRankedBroadcast(string accountId, string broadcastId, string claimToken)
        => CompleteRankedBroadcastObject(accountId, broadcastId, claimToken, DateTimeOffset.UtcNow);

    public bool DeleteRankedBroadcast(L12AccountView actor, string id, L12AdminAuditContext context)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        lock (_gate)
        {
            var removed = _data.RankedBroadcasts.RemoveAll(row => row.Id == id) > 0;
            if (removed)
            {
                _data.RankedBroadcastDeliveries.RemoveAll(row => row.BroadcastId == id);
                AddAdminAudit(actor, "operations", "ranked-broadcast-delete", $"ranked:broadcast:{id}", null, null, null, context);
                Save();
            }
            return removed;
        }
    }

    internal L12RankedSettlementPair SettleRankedMatch(string matchId, string firstAccountId,
        string secondAccountId, int winner, string? firstMasterId = null, string? secondMasterId = null,
        L12RankedIntegrityContext? integrity = null, string? seasonId = null)
    {
        lock (_gate)
        {
            ValidateRankedIdentity(matchId, firstAccountId, secondAccountId, winner);
            if (TryGetRankedSettlementReplayLocked(matchId, firstAccountId, secondAccountId, winner,
                    firstMasterId, secondMasterId, integrity, out var replay, seasonId))
            {
                if (integrity is not null && !IsRankedMatchExcludedLocked(matchId)
                    && ImportRankedMasterTitleFactLocked(new L12RankedMasterTitleMatchFact(
                        matchId, firstAccountId, secondAccountId, firstMasterId ?? string.Empty,
                        secondMasterId ?? string.Empty, winner, integrity.FinalRound, integrity.EndedAt,
                        integrity.ConclusionKind, true))) Save();
                return replay;
            }

            EnsureRankedSettlementSeason(seasonId);

            var first = RequireRankedProfile(firstAccountId);
            var second = RequireRankedProfile(secondAccountId);
            if (string.IsNullOrWhiteSpace(first.Faction) || string.IsNullOrWhiteSpace(second.Faction))
                throw new InvalidOperationException("排位结算缺少赛季派系");
            if (TryHoldRankedMatchLocked(matchId, first, second, winner, firstMasterId,
                    secondMasterId, integrity, out var held)) return held;
            var firstProfileBefore = CaptureRankedProfile(first);
            var secondProfileBefore = CaptureRankedProfile(second);
            var beforeTitles = CurrentFactionTitleAssignments();
            var beforeMasterChampions = CurrentMasterChampions()
                .ToDictionary(item => item.Key, item => item.Value.AccountId, StringComparer.OrdinalIgnoreCase);
            var firstRating = first.HiddenRating;
            var secondRating = second.HiddenRating;
            var firstSevenBefore = first.SevenValue;
            var secondSevenBefore = second.SevenValue;
            var firstStreakBefore = first.WinStreak;
            var secondStreakBefore = second.WinStreak;
            var firstSettlement = SettleOne(matchId, first, winner == 0, firstRating,
                secondSevenBefore, secondStreakBefore);
            var secondSettlement = SettleOne(matchId, second, winner == 1, secondRating,
                firstSevenBefore, firstStreakBefore);
            // 段位异画以结算后的权威资料为准，重复结算会命中同一来源记录而保持幂等。
            ApplyRankReachedAlternateArtAwardsLocked(first.AccountId, first.SeasonId, TierIndex(first));
            ApplyRankReachedAlternateArtAwardsLocked(second.AccountId, second.SeasonId, TierIndex(second));
            var expectedFirst = 1d / (1d + Math.Pow(10d, (secondRating - firstRating) / 400d));
            first.HiddenRating = Math.Clamp(firstRating + 24d * ((winner == 0 ? 1d : 0d) - expectedFirst), 500d, 2500d);
            second.HiddenRating = Math.Clamp(secondRating + 24d * ((winner == 1 ? 1d : 0d) - (1d - expectedFirst)), 500d, 2500d);
            UpdateMasterRecord(first, firstMasterId, winner == 0);
            UpdateMasterRecord(second, secondMasterId, winner == 1);
            if (integrity is not null)
                ImportRankedMasterTitleFactLocked(new L12RankedMasterTitleMatchFact(matchId,
                    firstAccountId, secondAccountId, firstMasterId ?? string.Empty,
                    secondMasterId ?? string.Empty, winner, integrity.FinalRound, integrity.EndedAt,
                    integrity.ConclusionKind, true));
            if (!_data.RankedMasterRecordedMatchIds.Contains(matchId, StringComparer.OrdinalIgnoreCase))
                _data.RankedMasterRecordedMatchIds.Add(matchId);
            _data.RankedSettlements.Add(firstSettlement);
            _data.RankedSettlements.Add(secondSettlement);
            EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, winner,
                firstMasterId, secondMasterId, integrity, seasonId);
            var broadcasts = BuildBroadcasts(matchId, first, second, winner, beforeTitles,
                beforeMasterChampions, firstStreakBefore, secondStreakBefore,
                firstMasterId, secondMasterId);
            _data.RankedBroadcasts.AddRange(broadcasts);
            _data.RankedSettlementProfileFacts.Add(new RankedSettlementProfileFactRow
            {
                MatchId = matchId,
                FirstAccountId = firstAccountId,
                SecondAccountId = secondAccountId,
                FirstBefore = firstProfileBefore,
                FirstAfter = CaptureRankedProfile(first),
                SecondBefore = secondProfileBefore,
                SecondAfter = CaptureRankedProfile(second),
                AppliedInitially = true,
                CreatedAt = integrity?.EndedAt.ToUniversalTime() ?? DateTimeOffset.UtcNow,
            });
            if (_data.RankedBroadcasts.Count > 300)
            {
                var removedIds = _data.RankedBroadcasts.Take(_data.RankedBroadcasts.Count - 300)
                    .Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _data.RankedBroadcasts.RemoveAll(row => removedIds.Contains(row.Id));
                _data.RankedBroadcastDeliveries.RemoveAll(row => removedIds.Contains(row.BroadcastId));
            }
            Save();
            return new(ToView(firstSettlement), ToView(secondSettlement), broadcasts.Select(ToView).ToArray());
        }
    }

    internal L12RankedSettlementPair SettleRankedDrawMatch(string matchId, string firstAccountId,
        string secondAccountId, string? firstMasterId = null, string? secondMasterId = null,
        L12RankedIntegrityContext? integrity = null, string? seasonId = null)
    {
        lock (_gate)
        {
            ValidateRankedIdentity(matchId, firstAccountId, secondAccountId, winner: null);
            var existing = _data.RankedSettlements.Where(row => string.Equals(row.MatchId, matchId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (existing.Length != 0)
            {
                if (existing.Length != 2)
                    throw new InvalidDataException("排位平局结算账本不完整，已拒绝重复结算");
                var firstReplay = existing.SingleOrDefault(row => row.AccountId == firstAccountId);
                var secondReplay = existing.SingleOrDefault(row => row.AccountId == secondAccountId);
                if (firstReplay is null || secondReplay is null
                    || !string.Equals(firstReplay.Outcome, "draw", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(secondReplay.Outcome, "draw", StringComparison.OrdinalIgnoreCase)
                    || firstReplay.Delta != 0 || secondReplay.Delta != 0
                    || firstReplay.Before != firstReplay.After || secondReplay.Before != secondReplay.After)
                    throw new InvalidOperationException("排位平局重放参数与已结算结果冲突");
                if (EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, null,
                        firstMasterId, secondMasterId, integrity, seasonId)) Save(false);
                return new(ToView(firstReplay), ToView(secondReplay), []);
            }
            if (_data.RankedIntegrityAudits.Any(row => string.Equals(row.MatchId, matchId,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("排位对局已作为其他终局记录，不能重放为平局");

            EnsureRankedSettlementSeason(seasonId);

            var first = RequireRankedProfile(firstAccountId);
            var second = RequireRankedProfile(secondAccountId);
            if (string.IsNullOrWhiteSpace(first.Faction) || string.IsNullOrWhiteSpace(second.Faction))
                throw new InvalidOperationException("排位平局结算缺少赛季派系");
            var now = DateTimeOffset.UtcNow;
            RankedSettlementRow DrawRow(RankedProfileRow player)
            {
                var tier = TierFor(player).Name;
                return new RankedSettlementRow
                {
                    MatchId = matchId,
                    SeasonId = player.SeasonId,
                    AccountId = player.AccountId,
                    Faction = player.Faction!,
                    Outcome = "draw",
                    Won = false,
                    Placement = false,
                    PlacementPlayed = player.PlacementPlayed,
                    PlacementRequired = _data.RankedConfig!.PlacementMatches,
                    Before = player.SevenValue,
                    After = player.SevenValue,
                    Delta = 0,
                    TierBefore = tier,
                    TierAfter = tier,
                    Components = [new("draw", "双方同意平局", 0)],
                    SettledAt = now,
                };
            }

            var firstSettlement = DrawRow(first);
            var secondSettlement = DrawRow(second);
            _data.RankedSettlements.Add(firstSettlement);
            _data.RankedSettlements.Add(secondSettlement);
            EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, null,
                firstMasterId, secondMasterId, integrity, seasonId);
            Save();
            return new(ToView(firstSettlement), ToView(secondSettlement), []);
        }
    }

    private RankedSettlementRow SettleOne(string matchId, RankedProfileRow player, bool won,
        double ratingBefore, int opponentSevenBefore, int opponentWinStreakBefore)
    {
        var config = _data.RankedConfig!;
        var before = player.SevenValue;
        var tierBefore = TierFor(player);
        var placement = player.PlacementPlayed < config.PlacementMatches;
        var components = new List<L12RankedSettlementComponent>();
        player.PlacementPlayed++;
        if (won) player.PlacementWins++;
        player.Wins += won ? 1 : 0;
        player.Losses += won ? 0 : 1;
        player.WinStreak = won ? player.WinStreak + 1 : 0;
        player.LossStreak = won ? 0 : player.LossStreak + 1;
        if (placement)
        {
            if (player.PlacementPlayed >= config.PlacementMatches)
            {
                var ratingPart = Math.Clamp((int)Math.Round((ratingBefore - 1000d) * 15d), 0, 12000);
                var recordPart = player.PlacementWins * 3500;
                player.SevenValue = Math.Min(config.PlacementMaximum, ratingPart + recordPart);
                components.Add(new("placement", "定级结果", player.SevenValue));
            }
        }
        else
        {
            var tier = TierFor(player);
            var baseDelta = won ? tier.BaseDelta : -tier.BaseDelta;
            components.Add(new("base", "基础胜负", baseDelta));
            var gapRaw = (int)Math.Round((opponentSevenBefore - before) / 1000d * (tier.RatingGapCap / 5d));
            // 对手越强，胜利时多得、失败时少扣；对手越弱则相反。修正方向与胜负无关。
            var gap = Math.Clamp(gapRaw, -tier.RatingGapCap, tier.RatingGapCap);
            components.Add(new("gap", "实力差修正", gap));
            var streakStep = tier.WinStreakCap / 10d;
            var winBonus = won ? Math.Min(tier.WinStreakCap, (int)Math.Round(Math.Max(0, player.WinStreak - 1) * streakStep)) : 0;
            if (winBonus != 0) components.Add(new("win-streak", "连胜奖励", winBonus));
            var protectionStep = tier.LossProtectionCap / 5d;
            var lossProtection = !won ? Math.Min(tier.LossProtectionCap, (int)Math.Round(Math.Max(0, player.LossStreak - 1) * protectionStep)) : 0;
            if (lossProtection != 0) components.Add(new("loss-protection", "连败保护", lossProtection));
            var terminate = won && opponentWinStreakBefore >= 5
                ? StreakTerminationReward(opponentSevenBefore) : 0;
            if (terminate != 0) components.Add(new("streak-termination", "终结连胜", terminate));
            var rawAfter = before + components.Sum(item => item.Value);
            var protectedAfter = Math.Max(player.HighestFloor, Math.Max(0, rawAfter));
            if (protectedAfter != rawAfter) components.Add(new("floor", "段位保底", protectedAfter - rawAfter));
            player.SevenValue = protectedAfter;
        }
        player.HighestFloor = Math.Max(player.HighestFloor, FloorFor(player.SevenValue));
        var tierAfter = TierFor(player);
        return new RankedSettlementRow
        {
            MatchId = matchId, SeasonId = player.SeasonId, AccountId = player.AccountId,
            Faction = player.Faction!,
            Outcome = won ? "win" : "loss", Won = won,
            Placement = placement, PlacementPlayed = player.PlacementPlayed,
            PlacementRequired = config.PlacementMatches, Before = before, After = player.SevenValue,
            Delta = player.SevenValue - before, TierBefore = tierBefore.Name, TierAfter = tierAfter.Name,
            Components = components, SettledAt = DateTimeOffset.UtcNow,
        };
    }

    private List<RankedBroadcastRow> BuildBroadcasts(string matchId, RankedProfileRow first,
        RankedProfileRow second, int winner, IReadOnlyDictionary<string, string> beforeTitles,
        IReadOnlyDictionary<string, string> beforeMasterChampions,
        int firstStreakBefore, int secondStreakBefore, string? firstMasterId, string? secondMasterId)
    {
        var winnerRow = winner == 0 ? first : second;
        var highestTierIndex = _data.RankedConfig!.Factions[0].Tiers.Count - 1;
        var reachedHighestTierNow = !winnerRow.ReachedHighestTier
            && TierIndex(winnerRow) == highestTierIndex;
        // 最高阶是排位档案事实，不是广播投递状态。关闭全服广播时也必须照常落档，
        // 否则后续处置会把这类历史档案误判为结算链损坏。
        if (reachedHighestTierNow) winnerRow.ReachedHighestTier = true;
        if (!_data.RankedConfig.BroadcastEnabled) return [];
        var config = NormalizeRankedBroadcastConfig(_data.RankedConfig.Broadcast);
        var loserRow = winner == 0 ? second : first;
        var loserStreakBefore = winner == 0 ? secondStreakBefore : firstStreakBefore;
        var rows = new List<RankedBroadcastRow>();
        void Add(string type, string message)
        {
            if (_data.RankedBroadcasts.Any(row => row.MatchId == matchId && row.EventType == type)) return;
            rows.Add(new RankedBroadcastRow { MatchId = matchId, EventType = type, Message = message });
        }
        var faction = FactionFor(winnerRow.Faction!);
        var winnerName = AccountName(winnerRow.AccountId);
        var winnerMeetsTier = TierIndex(winnerRow) >= config.MinimumTierIndex;
        if (config.WinStreakEnabled && winnerMeetsTier && winnerRow.WinStreak >= config.WinStreakThreshold)
            Add("win-streak", $"【{faction.Name}】{winnerName} 已取得 {winnerRow.WinStreak} 连胜");
        if (config.StreakEndedEnabled && winnerMeetsTier && loserStreakBefore >= config.StreakEndedThreshold)
            Add("streak-ended", $"【{faction.Name}】{winnerName} 终结了 {AccountName(loserRow.AccountId)} 的 {loserStreakBefore} 连胜");
        if (reachedHighestTierNow)
        {
            if (config.HighestTierEnabled)
                Add("highest-tier", $"【{faction.Name}】{winnerName} 晋升至 {faction.Tiers[highestTierIndex].Name}");
        }
        var after = FactionRank(winnerRow);
        var afterTitle = FactionPlacementTitle(winnerRow, after);
        if (config.FactionTitleEnabled && winnerMeetsTier && afterTitle is not null
            && beforeTitles.GetValueOrDefault(winnerRow.AccountId) != afterTitle)
            Add(after == 1 ? "faction-first" : "faction-top-five",
                $"【{faction.Name}】{winnerName} 获得称号「{afterTitle}」");
        var afterMasterChampions = CurrentMasterChampions();
        foreach (var masterId in (config.MasterTitleEnabled && winnerMeetsTier
            ? new[] { firstMasterId, secondMasterId } : []).Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!afterMasterChampions.TryGetValue(masterId, out var champion)
                || beforeMasterChampions.GetValueOrDefault(masterId) == champion.AccountId) continue;
            var championProfile = _data.RankedProfiles.First(row => row.AccountId == champion.AccountId
                && SeasonIdsEqual(row.SeasonId, champion.SeasonId));
            Add($"master-champion-{masterId}",
                $"【{FactionFor(championProfile.Faction!).Name}】{AccountName(champion.AccountId)} 获得称号「{MasterTitle(masterId)}」");
        }
        return rows;
    }

    private RankedProfileRow RequireRankedProfile(string accountId)
    {
        var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Disabled && !row.Deleted)
            ?? throw new KeyNotFoundException("账号不存在或不可用");
        var season = RequireOperationsConfig().Season.Id;
        var row = _data.RankedProfiles.FirstOrDefault(item => item.AccountId == accountId);
        if (row is null)
        {
            row = new RankedProfileRow { AccountId = accountId, SeasonId = season };
            _data.RankedProfiles.Add(row);
        }
        else if (!SeasonIdsEqual(row.SeasonId, season))
        {
            ArchiveRankedProfile(row);
            row.SeasonId = season;
            row.SevenValue = row.PlacementPlayed = row.PlacementWins = row.Wins = row.Losses = 0;
            row.WinStreak = row.LossStreak = row.HighestFloor = 0;
            row.ReachedHighestTier = false;
        }
        else if (!string.Equals(row.SeasonId, season, StringComparison.Ordinal))
        {
            // Heal legacy casing/whitespace/Unicode variants without treating them as a season boundary.
            row.SeasonId = season;
        }
        _ = account;
        return row;
    }

    private void EnsureRankedSettlementSeason(string? seasonId)
    {
        if (string.IsNullOrWhiteSpace(seasonId)) return;
        if (!SeasonIdsEqual(seasonId, RequireOperationsConfig().Season.Id))
            throw new InvalidOperationException("排位结算所属赛季已结束，拒绝写入当前赛季");
    }

    private void ArchiveRankedProfile(RankedProfileRow row, string? seasonName = null,
        bool finalizedSeasonAwards = false, IReadOnlyList<string>? frozenTitles = null,
        string? factionTitle = null, IReadOnlyList<string>? frozenMasterTitles = null,
        DateTimeOffset? finalizedAt = null)
    {
        if (string.IsNullOrWhiteSpace(row.Faction)
            || (!finalizedSeasonAwards && row.PlacementPlayed == 0)) return;
        if (finalizedSeasonAwards && !ParticipatedInRankedSeasonLocked(row.AccountId, row.SeasonId)) return;
        if (_data.RankedProfileHistory.Any(history => history.AccountId == row.AccountId
                && SeasonIdsEqual(history.SeasonId, row.SeasonId) && history.FinalizedSeasonAwards)) return;
        var placed = row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches;
        var factionRank = placed ? FactionRank(row) : 0;
        var highestTier = placed && IsHighestTier(row);
        var archivedAt = finalizedAt ?? DateTimeOffset.UtcNow;
        _data.RankedProfileHistory.Add(new RankedProfileHistoryRow
        {
            AccountId = row.AccountId,
            SeasonId = row.SeasonId,
            UsernameSnapshot = AccountName(row.AccountId),
            Faction = row.Faction,
            FactionNameSnapshot = FactionFor(row.Faction).Name,
            SevenValue = row.SevenValue,
            PlacementPlayed = row.PlacementPlayed,
            PlacementWins = row.PlacementWins,
            Wins = row.Wins,
            Losses = row.Losses,
            WinStreak = row.WinStreak,
            SeasonName = seasonName ?? RequireOperationsConfig().Season.Name,
            Tier = placed ? TierFor(row).Name : string.Empty,
            Titles = frozenTitles?.ToList() ?? [],
            FactionTitle = factionTitle,
            MasterTitles = frozenMasterTitles?.ToList() ?? [],
            FinalizedSeasonAwards = finalizedSeasonAwards,
            FactionRank = finalizedSeasonAwards && factionRank > 0 ? factionRank : null,
            OverallRank = finalizedSeasonAwards && highestTier ? OverallRank(row) : null,
            Placed = finalizedSeasonAwards ? placed : null,
            PlacementRequired = finalizedSeasonAwards ? _data.RankedConfig.PlacementMatches : null,
            RankLabel = finalizedSeasonAwards
                ? placed ? TierFor(row).Name
                    : $"定级 {row.PlacementPlayed}/{_data.RankedConfig.PlacementMatches}"
                : null,
            WinRate = finalizedSeasonAwards && row.Wins + row.Losses > 0
                ? Percentage(row.Wins, row.Wins + row.Losses) : null,
            SummaryAvailableAt = finalizedSeasonAwards ? archivedAt : null,
            ArchivedAt = archivedAt,
        });
    }

    private void FinalizeOutgoingRankedSeason(SeasonDefinitionRow outgoing,
        string incomingSeasonId, DateTimeOffset finalizedAt)
    {
        var outgoingSeasonId = outgoing.SeasonId;
        var outgoingSeasonName = outgoing.Name;
        if (SeasonIdsEqual(outgoingSeasonId, incomingSeasonId)) return;
        if (outgoing.FactionFinalTotals.Count == 0)
            outgoing.FactionFinalTotals = FreezeFactionFinalTotalsLocked(outgoingSeasonId,
                finalizedAt, "season-finalization-v1", string.Empty);
        var champions = CurrentMasterChampions();
        var rows = EligibleOutgoingRankedSeasonRowsLocked(outgoingSeasonId);
        foreach (var row in rows)
        {
            // 在归档前使用该赛季的最终七曜值计算门槛；重复切换同一赛季只会复用同一权益记录。
            ApplySeasonFinalAlternateArtAwardsLocked(row.AccountId, outgoingSeasonId, TierIndex(row));
            var factionRank = FactionRank(row);
            var factionTitle = FactionPlacementTitle(row, factionRank);
            var masterTitles = PlayerMasterTitles(row, champions);
            var titles = new[] { factionTitle }.Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title!).Concat(masterTitles)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            ArchiveRankedProfile(row, outgoingSeasonName, true, titles, factionTitle, masterTitles,
                finalizedAt);
        }
        ApplyMasterChampionSeasonFinalAlternateArtAwardsLocked(champions, outgoingSeasonId);
    }

    private List<SeasonFactionFinalTotalRow> FreezeFactionFinalTotalsLocked(string seasonId,
        DateTimeOffset capturedAt, string provenance, string evidenceFingerprint)
        => _data.RankedConfig!.Factions.Select(faction =>
        {
            var rows = _data.RankedProfiles.Where(row => SeasonIdsEqual(row.SeasonId, seasonId)
                    && string.Equals(row.Faction, faction.Id, StringComparison.OrdinalIgnoreCase)
                    && row.PlacementPlayed >= _data.RankedConfig.PlacementMatches
                    && IsActiveAccountLocked(row.AccountId))
                .ToArray();
            return new SeasonFactionFinalTotalRow
            {
                FactionId = faction.Id,
                FactionNameSnapshot = faction.Name,
                Value = rows.Sum(row => row.SevenValue),
                EligiblePlayers = rows.Length,
                CapturedAt = capturedAt,
                Provenance = provenance,
                EvidenceFingerprint = evidenceFingerprint,
            };
        }).ToList();

    private static SeasonFactionFinalTotalRow CloneFactionFinalTotal(SeasonFactionFinalTotalRow row)
        => new()
        {
            FactionId = row.FactionId,
            FactionNameSnapshot = row.FactionNameSnapshot,
            Value = row.Value,
            EligiblePlayers = row.EligiblePlayers,
            CapturedAt = row.CapturedAt,
            Provenance = row.Provenance,
            EvidenceFingerprint = row.EvidenceFingerprint,
        };

    private RankedProfileRow[] EligibleOutgoingRankedSeasonRowsLocked(string outgoingSeasonId)
        => _data.RankedProfiles
            .Where(row => SeasonIdsEqual(row.SeasonId, outgoingSeasonId)
                && !string.IsNullOrWhiteSpace(row.Faction)
                && ParticipatedInRankedSeasonLocked(row.AccountId, row.SeasonId)
                && !_data.RankedProfileHistory.Any(history => history.AccountId == row.AccountId
                    && SeasonIdsEqual(history.SeasonId, row.SeasonId)
                    && history.FinalizedSeasonAwards))
            // The persisted model normally has one live profile per account. Explicit de-duplication
            // keeps preview counts and finalization identical even when repairing legacy duplicate rows.
            .GroupBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.PlacementPlayed)
                .ThenByDescending(row => row.SevenValue).First())
            .OrderBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private L12RankedSeasonFinalizationImpact RankedSeasonFinalizationImpactLocked(
        string outgoingSeasonId)
    {
        var accountIds = EligibleOutgoingRankedSeasonRowsLocked(outgoingSeasonId)
            .Select(row => row.AccountId).ToArray();
        return new L12RankedSeasonFinalizationImpact(accountIds, accountIds.Length, accountIds.Length);
    }

    private void CarryRankedProfilesIntoSeason(string outgoingSeasonId, string incomingSeasonId)
    {
        foreach (var row in _data.RankedProfiles.Where(row => SeasonIdsEqual(row.SeasonId, outgoingSeasonId)))
        {
            row.SeasonId = incomingSeasonId;
            ResetRankedProfileForNewSeason(row);
        }
    }

    private const int T01TransitionRepairMaximumMatches = 500;

    public L12RankedSeasonResetRepairPreviewView PreviewT01RankedSeasonReset(
        L12AccountView actor, string seasonId, long expectedOperationsVersion,
        L12RankedSeasonCutoverReadiness readiness, DateTimeOffset observedAt,
        DateTimeOffset? competitiveStartAt = null)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        lock (_gate)
        {
            var plan = BuildT01RankedSeasonResetRepairPlanLocked(seasonId,
                expectedOperationsVersion, readiness, observedAt.ToUniversalTime(),
                competitiveStartAt?.ToUniversalTime());
            return new(plan.Season.SeasonId, plan.Season.StartsAt, plan.Season.ActivatedAt,
                plan.Season.EndsAt, plan.MatchIds.Length, plan.Settlements.Length,
                plan.ProfileFacts.Length, plan.Profiles.Length, plan.Broadcasts.Length,
                plan.MasterRecords.Length, plan.Grants.Length, plan.EvidenceFingerprint,
                T01TransitionRepairMaximumMatches, plan.CompetitiveStartAt);
        }
    }

    public L12RankedSeasonResetRepairView RepairT01RankedSeasonReset(L12AccountView actor,
        string seasonId, string reason, long expectedOperationsVersion,
        L12RankedSeasonCutoverReadiness readiness, L12AdminAuditContext context,
        string expectedEvidenceFingerprint = "", DateTimeOffset? observedAt = null,
        DateTimeOffset? competitiveStartAt = null)
        => ExecuteAdminTransaction(() => RepairT01RankedSeasonResetCore(actor, seasonId, reason,
            expectedOperationsVersion, readiness, context, expectedEvidenceFingerprint,
            (observedAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            competitiveStartAt?.ToUniversalTime()));

    private L12RankedSeasonResetRepairView RepairT01RankedSeasonResetCore(L12AccountView actor,
        string seasonId, string reason, long expectedOperationsVersion,
        L12RankedSeasonCutoverReadiness readiness, L12AdminAuditContext context,
        string expectedEvidenceFingerprint, DateTimeOffset observedAt,
        DateTimeOffset? competitiveStartAt)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim();
        if (normalizedReason.Length == 0)
            throw new L12OperationsConfigException("reason_required", "修复新赛季排位数据必须填写原因");

        lock (_gate)
        {
            var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
            var migratedReplay = _data.RankedSeasonResetRepairs.SingleOrDefault(row =>
                string.Equals(row.SeasonId, "S01", StringComparison.Ordinal)
                && string.Equals(row.PreviousSeasonId, "S00", StringComparison.Ordinal));
            if (string.Equals(seasonId, "T01", StringComparison.Ordinal)
                && string.Equals(current.SeasonId, "S01", StringComparison.Ordinal)
                && migratedReplay is not null)
                return RankedSeasonResetRepairView(migratedReplay, true);
            if (!SeasonIdsEqual(seasonId, "T01") || !SeasonIdsEqual(current.SeasonId, "T01"))
                throw new L12OperationsConfigException("ranked_season_reset_repair_scope_invalid",
                    "该一次性修复仅允许当前运行赛季 T01");
            // The marker is the durable once-only boundary. A retry with another idempotency key
            // must report the original repair without inspecting or resetting games played later.
            var existing = _data.RankedSeasonResetRepairs.SingleOrDefault(row =>
                SeasonIdsEqual(row.SeasonId, current.SeasonId));
            if (existing is not null) return RankedSeasonResetRepairView(existing, true);
            var plan = BuildT01RankedSeasonResetRepairPlanLocked(seasonId,
                expectedOperationsVersion, readiness, observedAt, competitiveStartAt);
            var normalizedFingerprint = expectedEvidenceFingerprint?.Trim().ToLowerInvariant()
                ?? string.Empty;
            if (normalizedFingerprint.Length != 64 || !normalizedFingerprint.All(Uri.IsHexDigit))
                throw new L12OperationsConfigException("ranked_season_reset_repair_evidence_required",
                    "必须先预览并提交完整的过渡期证据指纹");
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(normalizedFingerprint),
                    Encoding.ASCII.GetBytes(plan.EvidenceFingerprint)))
                throw new L12OperationsConfigException("ranked_season_reset_repair_evidence_changed",
                    "过渡期排位事实已变化，请重新预览");

            var marker = new RankedSeasonResetRepairRow
            {
                SeasonId = current.SeasonId,
                PreviousSeasonId = current.PreviousSeasonId!,
                ProfilesReset = plan.Profiles.Length,
                NonzeroSevenValueProfiles = plan.Profiles.Count(row => row.SevenValue != 0),
                NonzeroPlacementProfiles = plan.Profiles.Count(row => row.PlacementPlayed != 0),
                RankedProfilesWithMatchStats = plan.Profiles.Count(row =>
                    row.Wins != 0 || row.Losses != 0 || row.PlacementWins != 0),
                AppliedAt = observedAt,
                AppliedBy = actor.Username,
                Reason = normalizedReason,
                OriginalStartsAt = current.StartsAt,
                OriginalActivatedAt = current.ActivatedAt,
                CompetitiveStartAt = plan.CompetitiveStartAt,
                EndsAt = current.EndsAt,
                TransitionMatchIds = plan.MatchIds.ToList(),
                EvidenceFingerprint = plan.EvidenceFingerprint,
                SettlementRowsWaived = plan.Settlements.Length,
                ProfileFactsWaived = plan.ProfileFacts.Length,
                BroadcastsRemoved = plan.Broadcasts.Length,
                MasterRecordsRemoved = plan.MasterRecords.Length,
                GrantsRevoked = plan.Grants.Length,
                OperationsVersionBefore = plan.Operations.Version,
                OperationsVersionAfter = checked(plan.Operations.Version + 1),
            };

            foreach (var profile in plan.Profiles)
            {
                ResetRankedProfileForNewSeason(profile);
                if (plan.HiddenRatingBaselines.TryGetValue(profile.AccountId, out var baseline))
                    profile.HiddenRating = baseline;
            }
            var broadcastIds = plan.Broadcasts.Select(row => row.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _data.RankedBroadcasts.RemoveAll(row => broadcastIds.Contains(row.Id));
            _data.RankedBroadcastDeliveries.RemoveAll(row => broadcastIds.Contains(row.BroadcastId));
            _data.RankedMasterRecords.RemoveAll(row => plan.MasterRecords.Contains(row));
            foreach (var grant in plan.Grants)
            {
                grant.RevokedAt = observedAt;
                grant.RevokedByAccountId = actor.Id;
            }

            var previousOperations = ToPayload(plan.Operations);
            var nextPayload = NormalizeOperationsPayload(previousOperations with
            {
                Season = previousOperations.Season with { StartsAt = plan.CompetitiveStartAt },
            });
            var nextOperations = ToRow(nextPayload, marker.OperationsVersionAfter, actor.Username,
                plan.Operations.ImmediateMaintenance);
            _data.OperationsConfig = nextOperations;
            _data.OperationsConfigHistory.Add(NewOperationsHistory(nextOperations,
                "ranked-season-reset-repair:T01", actor, normalizedReason));
            TrimOperationsHistory();
            current.StartsAt = plan.CompetitiveStartAt;
            current.Revision++;
            current.UpdatedBy = actor.Username;
            current.UpdatedAt = observedAt;
            _data.RankedSeasonResetRepairs.Add(marker);
            AddAdminAudit(actor, "ranked", "season-reset-repair", "ranked-season:T01",
                $"profiles={marker.ProfilesReset};matches={marker.TransitionMatchIds.Count};fingerprint={marker.EvidenceFingerprint}",
                $"competitiveStartAt={plan.CompetitiveStartAt:O};seven=0;placement=0;match-stats=0", normalizedReason,
                context with { ExpectedVersion = expectedOperationsVersion, Reason = normalizedReason,
                    Outcome = "succeeded" });
            Save();
            return RankedSeasonResetRepairView(marker, false);
        }
    }

    private RankedSeasonResetRepairPlan BuildT01RankedSeasonResetRepairPlanLocked(string seasonId,
        long expectedOperationsVersion, L12RankedSeasonCutoverReadiness readiness,
        DateTimeOffset observedAt, DateTimeOffset? requestedCompetitiveStartAt = null)
    {
        var operations = RequireOperationsConfig();
        EnsureOperationsVersion(operations, expectedOperationsVersion);
        var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
        if (!SeasonIdsEqual(seasonId, "T01") || !SeasonIdsEqual(current.SeasonId, "T01")
            || !SeasonIdsEqual(operations.Season.Id, current.SeasonId))
            throw new L12OperationsConfigException("ranked_season_reset_repair_scope_invalid",
                "该一次性修复仅允许当前运行赛季 T01");
        if (current.FinalizedAt is not null)
            throw new L12OperationsConfigException("ranked_season_reset_repair_finalized",
                "T01 已结算，拒绝重置排位数据");
        if (current.EndsAt is { } endsAt && observedAt >= endsAt.ToUniversalTime())
            throw new L12OperationsConfigException("ranked_season_reset_repair_season_ended",
                "修复切点已到达 T01 结束时间，拒绝重定义开季时间");
        var competitiveStartAt = requestedCompetitiveStartAt?.ToUniversalTime() ?? observedAt;
        if (current.StartsAt is { } originalStart && competitiveStartAt < originalStart.ToUniversalTime()
            || current.EndsAt is { } seasonEnd && competitiveStartAt >= seasonEnd.ToUniversalTime())
            throw new L12OperationsConfigException("ranked_season_reset_repair_start_invalid",
                "新开季时间不得早于原开始时间或到达赛季结束时间");
        if (!SeasonIdsEqual(readiness.SeasonId, current.SeasonId) || !readiness.Ready)
            throw new L12OperationsConfigException("ranked_season_reset_repair_not_ready",
                "T01 仍有在途、待结算或待治理排位对局，拒绝修复");
        if (string.IsNullOrWhiteSpace(current.PreviousSeasonId)
            || !_data.SeasonArchives.Any(row => SeasonIdsEqual(row.SeasonId, current.PreviousSeasonId)
                && SeasonIdsEqual(row.NextSeasonId, current.SeasonId)))
            throw new L12OperationsConfigException("ranked_season_reset_repair_activation_unproven",
                "缺少连接到 T01 的已归档上赛季，无法证明这是切季承接污染");
        if (_data.RankedProfileHistory.Any(row => SeasonIdsEqual(row.SeasonId, current.SeasonId)))
            throw new L12OperationsConfigException("ranked_season_reset_repair_history_exists",
                "T01 已存在赛季历史档案，拒绝修复");

        var audits = _data.RankedIntegrityAudits.Where(row =>
                SeasonIdsEqual(row.SeasonId, current.SeasonId))
            .OrderBy(row => row.EndedAt == default ? row.CreatedAt : row.EndedAt)
            .ThenBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (audits.Length > T01TransitionRepairMaximumMatches)
            throw new L12OperationsConfigException("ranked_season_reset_repair_too_many_matches",
                $"过渡期排位共 {audits.Length} 场，超过单次安全上限 {T01TransitionRepairMaximumMatches} 场");
        if (audits.GroupBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
            throw new L12OperationsConfigException("ranked_season_reset_repair_audit_duplicate",
                "T01 存在重复排位完整性审计");
        if (audits.Any(row => (row.EndedAt == default ? row.CreatedAt : row.EndedAt).ToUniversalTime()
                              > observedAt))
            throw new L12OperationsConfigException("ranked_season_reset_repair_future_fact",
                "T01 存在晚于修复切点的排位事实");
        if (requestedCompetitiveStartAt is not null && audits.Any(row =>
                (row.EndedAt == default ? row.CreatedAt : row.EndedAt).ToUniversalTime()
                >= competitiveStartAt))
            throw new L12OperationsConfigException("ranked_season_reset_repair_after_start_fact",
                "预定开季时间之后已有排位事实，拒绝将其归入过渡期");
        var matchIds = audits.Select(row => row.MatchId).ToArray();
        var selected = matchIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activationFloor = current.ActivatedAt?.ToUniversalTime() ?? DateTimeOffset.MinValue;
        var settlements = _data.RankedSettlements.Where(row =>
                SeasonIdsEqual(row.SeasonId, current.SeasonId)
                || string.IsNullOrWhiteSpace(row.SeasonId) && row.SettledAt.ToUniversalTime() >= activationFloor)
            .OrderBy(row => row.SettledAt).ThenBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase).ToArray();
        var facts = _data.RankedSettlementProfileFacts.Where(row =>
                RankedSettlementFactContainsSeason(row, current.SeasonId))
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var held = _data.RankedHeldRewards.Where(row => SeasonIdsEqual(row.SeasonId,
            current.SeasonId)).ToArray();
        if (settlements.Any(row => !selected.Contains(row.MatchId))
            || facts.Any(row => !selected.Contains(row.MatchId))
            || held.Any(row => !selected.Contains(row.MatchId)))
            throw new L12OperationsConfigException("ranked_season_reset_repair_orphan_fact",
                "T01 存在无法关联到完整性审计的结算事实");

        foreach (var audit in audits)
        {
            var rows = settlements.Where(row => row.MatchId.Equals(audit.MatchId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            var matchFacts = facts.Where(row => row.MatchId.Equals(audit.MatchId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (audit.Winner is 0 or 1)
            {
                if (rows.Length != 2 || matchFacts.Length != 1
                    || rows.Select(row => row.AccountId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                        .SetEquals([audit.FirstAccountId, audit.SecondAccountId]) == false
                    || rows.Count(row => row.Outcome == "win") != 1
                    || rows.Count(row => row.Outcome == "loss") != 1)
                    throw new L12OperationsConfigException("ranked_season_reset_repair_match_incomplete",
                        $"对局 {audit.MatchId} 的双方结算或档案快照不完整");
                var winnerId = audit.Winner == 0 ? audit.FirstAccountId : audit.SecondAccountId;
                if (!rows.Single(row => row.AccountId.Equals(winnerId,
                        StringComparison.OrdinalIgnoreCase)).Won)
                    throw new L12OperationsConfigException("ranked_season_reset_repair_match_conflict",
                        $"对局 {audit.MatchId} 的胜者与结算账本冲突");
                var fact = matchFacts[0];
                if (!fact.FirstAccountId.Equals(audit.FirstAccountId, StringComparison.OrdinalIgnoreCase)
                    || !fact.SecondAccountId.Equals(audit.SecondAccountId, StringComparison.OrdinalIgnoreCase))
                    throw new L12OperationsConfigException("ranked_season_reset_repair_match_conflict",
                        $"对局 {audit.MatchId} 的账号与档案快照冲突");
            }
            else if (rows.Length != 0 && (rows.Length != 2 || rows.Any(row => row.Outcome != "draw"))
                     || matchFacts.Length != 0)
                throw new L12OperationsConfigException("ranked_season_reset_repair_match_incomplete",
                    $"无胜者对局 {audit.MatchId} 的结算事实不完整");
        }

        var relatedDecisions = _data.RankedIntegrityDecisions.Where(row =>
            row.MatchIds.Any(selected.Contains) && !IsDecisionRevokedLocked(row.Id)).ToArray();
        if (relatedDecisions.Any(row => row.Disposition != "review"
                || row.MatchIds.Any(matchId => !_data.RankedHeldRewards.Any(hold => hold.MatchId.Equals(
                    matchId, StringComparison.OrdinalIgnoreCase)))))
            throw new L12OperationsConfigException("ranked_season_reset_repair_governance_exists",
                "过渡局已有生效人工处置，拒绝静默覆盖");
        var relatedDecisionIds = relatedDecisions.Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_data.RankedIntegrityCorrections.Any(row => row.MatchIds.Any(selected.Contains))
            || _data.RankedIntegrityAppeals.Any(row => relatedDecisionIds.Contains(row.DecisionId)
                && CurrentAppealStatus(row) != "closed"))
            throw new L12OperationsConfigException("ranked_season_reset_repair_governance_exists",
                "过渡局已有修正链或未决申诉，拒绝静默覆盖");

        var masterRecords = _data.RankedMasterRecords.Where(row =>
            SeasonIdsEqual(row.SeasonId, current.SeasonId)).ToArray();
        var heldMatchIds = held.Select(row => row.MatchId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedMasterRecords = audits.Where(row => row.Winner is 0 or 1
                && !heldMatchIds.Contains(row.MatchId))
            .SelectMany(row => new[]
            {
                (AccountId: row.FirstAccountId, MasterId: row.FirstMasterId, Won: row.Winner == 0),
                (AccountId: row.SecondAccountId, MasterId: row.SecondMasterId, Won: row.Winner == 1),
            }).Where(row => !string.IsNullOrWhiteSpace(row.MasterId))
            .GroupBy(row => (row.AccountId, row.MasterId),
                new RankedMasterRecordKeyComparer())
            .ToDictionary(group => group.Key,
                group => (Games: group.Count(), Wins: group.Count(row => row.Won)),
                new RankedMasterRecordKeyComparer());
        if (masterRecords.Length != expectedMasterRecords.Count
            || masterRecords.Any(row => !expectedMasterRecords.TryGetValue(
                    (row.AccountId, row.MasterId), out var expected)
                || row.Games != expected.Games || row.Wins != expected.Wins))
            throw new L12OperationsConfigException("ranked_season_reset_repair_master_conflict",
                "T01 主宰统计与过渡期权威赛果不一致");

        var profiles = _data.RankedProfiles.Where(row => SeasonIdsEqual(row.SeasonId,
            current.SeasonId)).OrderBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (profiles.GroupBy(row => row.AccountId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() != 1))
            throw new L12OperationsConfigException("ranked_season_reset_repair_duplicate_profile",
                "T01 存在重复排位档案，拒绝自动修复");
        var hiddenBaselines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var chain = facts.Where(row => row.AppliedInitially
                    && (row.FirstAccountId.Equals(profile.AccountId, StringComparison.OrdinalIgnoreCase)
                        || row.SecondAccountId.Equals(profile.AccountId, StringComparison.OrdinalIgnoreCase)))
                .Select(row => new
                {
                    Fact = row,
                    Before = row.FirstAccountId.Equals(profile.AccountId,
                        StringComparison.OrdinalIgnoreCase) ? row.FirstBefore : row.SecondBefore,
                    After = row.FirstAccountId.Equals(profile.AccountId,
                        StringComparison.OrdinalIgnoreCase) ? row.FirstAfter : row.SecondAfter,
                }).OrderBy(row => row.Fact.CreatedAt).ThenBy(row => row.Fact.MatchId,
                    StringComparer.OrdinalIgnoreCase).ToArray();
            if (chain.Length == 0) continue;
            for (var index = 1; index < chain.Length; index++)
                if (!RankedRepairSnapshotsEqual(chain[index - 1].After, chain[index].Before))
                    throw new L12OperationsConfigException("ranked_season_reset_repair_profile_chain_broken",
                        $"账号 {AccountName(profile.AccountId)} 的过渡期档案快照链不连续");
            if (!RankedProfileSettlementStateEqual(profile, chain[^1].After))
                throw new L12OperationsConfigException("ranked_season_reset_repair_profile_conflict",
                    $"账号 {AccountName(profile.AccountId)} 当前档案与过渡期最终快照不一致");
            hiddenBaselines[profile.AccountId] = chain[0].Before.HiddenRating;
        }

        var broadcasts = _data.RankedBroadcasts.Where(row => selected.Contains(row.MatchId)).ToArray();
        var grants = _data.AlternateArtGrants.Where(row => row.RevokedAt is null
            && (row.SourceKind.Equals("rank-reached", StringComparison.OrdinalIgnoreCase)
                && SeasonIdsEqual(row.SourceReference, current.SeasonId)
                || row.SourceKind.Equals("ranked-participants", StringComparison.OrdinalIgnoreCase)
                && row.SourceReference.Equals($"ranked-participants:{current.SeasonId}",
                    StringComparison.OrdinalIgnoreCase))).ToArray();
        if (_data.AlternateArtGrants.Any(row => row.RevokedAt is null
                && (row.SourceKind is "season-final" or "master-champion-season-final")
                && row.SourceReference.StartsWith(current.SeasonId, StringComparison.OrdinalIgnoreCase)))
            throw new L12OperationsConfigException("ranked_season_reset_repair_final_reward_exists",
                "T01 已存在赛季最终奖励，拒绝修复");

        var fingerprintPayload = JsonSerializer.Serialize(new
        {
            seasonId = current.SeasonId,
            requestedCompetitiveStartAt,
            current.StartsAt,
            current.ActivatedAt,
            current.EndsAt,
            audits,
            settlements,
            facts,
            held,
            decisions = relatedDecisions,
            profiles = profiles.Select(CaptureRankedProfile).ToArray(),
            broadcasts,
            masterRecords,
            grants,
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintPayload)))
            .ToLowerInvariant();
        return new RankedSeasonResetRepairPlan
        {
            Season = current,
            Operations = operations,
            ObservedAt = observedAt,
            CompetitiveStartAt = competitiveStartAt,
            MatchIds = matchIds,
            Audits = audits,
            Settlements = settlements,
            ProfileFacts = facts,
            Broadcasts = broadcasts,
            MasterRecords = masterRecords,
            Grants = grants,
            Profiles = profiles,
            HiddenRatingBaselines = hiddenBaselines,
            EvidenceFingerprint = fingerprint,
        };
    }

    private static bool RankedRepairSnapshotsEqual(RankedProfileSnapshotRow left,
        RankedProfileSnapshotRow right)
        => left.AccountId.Equals(right.AccountId, StringComparison.OrdinalIgnoreCase)
           && SeasonIdsEqual(left.SeasonId, right.SeasonId)
           && string.Equals(left.Faction, right.Faction, StringComparison.OrdinalIgnoreCase)
           && Math.Abs(left.HiddenRating - right.HiddenRating) < 0.0000001d
           && left.SevenValue == right.SevenValue
           && left.PlacementPlayed == right.PlacementPlayed
           && left.PlacementWins == right.PlacementWins
           && left.Wins == right.Wins && left.Losses == right.Losses
           && left.WinStreak == right.WinStreak && left.LossStreak == right.LossStreak
           && left.HighestFloor == right.HighestFloor
           && left.ReachedHighestTier == right.ReachedHighestTier;

    private sealed class RankedMasterRecordKeyComparer
        : IEqualityComparer<(string AccountId, string MasterId)>
    {
        public bool Equals((string AccountId, string MasterId) left,
            (string AccountId, string MasterId) right)
            => left.AccountId.Equals(right.AccountId, StringComparison.OrdinalIgnoreCase)
               && left.MasterId.Equals(right.MasterId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string AccountId, string MasterId) value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.AccountId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.MasterId));
    }

    private static void ResetRankedProfileForNewSeason(RankedProfileRow row)
    {
        row.SevenValue = 0;
        row.PlacementPlayed = 0;
        row.PlacementWins = 0;
        row.Wins = 0;
        row.Losses = 0;
        row.WinStreak = 0;
        row.LossStreak = 0;
        row.HighestFloor = 0;
        row.ReachedHighestTier = false;
        row.SelectedMasterTitle = null;
    }

    private static bool RankedSettlementFactContainsSeason(RankedSettlementProfileFactRow row,
        string seasonId) => SeasonIdsEqual(row.FirstBefore.SeasonId, seasonId)
            || SeasonIdsEqual(row.FirstAfter.SeasonId, seasonId)
            || SeasonIdsEqual(row.SecondBefore.SeasonId, seasonId)
            || SeasonIdsEqual(row.SecondAfter.SeasonId, seasonId);

    private static L12RankedSeasonResetRepairView RankedSeasonResetRepairView(
        RankedSeasonResetRepairRow row, bool replayed) => new(row.SeasonId, row.ProfilesReset,
        row.NonzeroSevenValueProfiles, row.NonzeroPlacementProfiles,
        row.RankedProfilesWithMatchStats, row.AppliedAt, row.AppliedBy, replayed,
        row.CompetitiveStartAt, row.TransitionMatchIds.Count, row.EvidenceFingerprint,
        row.OperationsVersionBefore, row.OperationsVersionAfter);

    private void ActivatePendingRankedGradient(string outgoingSeasonId, string incomingSeasonId,
        L12AccountView actor, L12AdminAuditContext context)
    {
        var pending = _data.RankedPendingGradient;
        if (pending is null || SeasonIdsEqual(outgoingSeasonId, incomingSeasonId)
            || !SeasonIdsEqual(pending.AfterSeasonId, outgoingSeasonId)) return;
        if (pending.Tiers.Count != 5 || _data.RankedConfig!.Factions.Any(faction => faction.Tiers.Count != 5))
            throw new InvalidDataException("下赛季排位梯度不完整，已拒绝切换赛季");

        foreach (var faction in _data.RankedConfig.Factions)
        {
            for (var index = 0; index < pending.Tiers.Count; index++)
            {
                var source = pending.Tiers[index];
                var target = faction.Tiers[index];
                target.Minimum = source.Minimum;
                target.BaseDelta = source.BaseDelta;
                target.WinStreakCap = source.WinStreakCap;
                target.LossProtectionCap = source.LossProtectionCap;
                target.RatingGapCap = source.RatingGapCap;
                target.StreakTerminationReward = source.StreakTerminationReward;
            }
        }
        _data.RankedGradientVersion = pending.Version;
        _data.RankedPendingGradient = null;
        AddAdminAudit(actor, "operations", "ranked-gradient-activate", "ranked:gradient",
            outgoingSeasonId, incomingSeasonId, $"排位梯度 v{pending.Version} 随新赛季生效",
            context with { Outcome = "succeeded" });
    }

    private L12RankedProfileView ProfileView(RankedProfileRow row)
    {
        var tier = TierFor(row);
        var rank = FactionRank(row);
        var faction = string.IsNullOrWhiteSpace(row.Faction) ? null : FactionFor(row.Faction);
        var champions = CurrentMasterChampions();
        var titles = PlayerTitles(row, rank, champions);
        var title = titles.FirstOrDefault();
        var placementTitle = FactionPlacementTitle(row, rank);
        var masterTitles = PlayerMasterTitles(row, champions);
        var selectedMasterTitle = SelectedMasterTitle(row, masterTitles);
        var rankLabel = row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
            ? tier.Name
            : $"定级 {row.PlacementPlayed}/{_data.RankedConfig.PlacementMatches}";
        return new(row.AccountId, AccountName(row.AccountId), row.SeasonId, faction?.Name,
            row.SevenValue, $"七曜值 {row.SevenValue:N0}", row.PlacementPlayed, row.PlacementWins,
            row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches, row.Wins, row.Losses,
            row.WinStreak, row.LossStreak, tier.Name, TierIndex(row), rank, title, titles,
            rankLabel, placementTitle, selectedMasterTitle, masterTitles);
    }

    private L12RankedLeaderboardEntry LeaderboardView(RankedProfileRow row, int rank,
        IReadOnlyDictionary<string, RankedMasterRecordRow> champions)
    {
        var faction = FactionFor(row.Faction!);
        var factionRank = FactionRank(row);
        var titles = PlayerTitles(row, factionRank, champions);
        var favoriteMaster = _data.RankedMasterRecords.Where(item => item.AccountId == row.AccountId
                && SeasonIdsEqual(item.SeasonId, row.SeasonId) && item.Games > 0)
            .OrderByDescending(item => item.Games).ThenByDescending(item => item.Wins)
            .ThenBy(item => item.MasterId, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return new(rank, AccountName(row.AccountId), faction.Name, row.SevenValue,
            $"七曜值 {row.SevenValue:N0}", TierFor(row).Name,
            titles.FirstOrDefault(), titles, favoriteMaster?.MasterId,
            favoriteMaster is null ? null : MasterName(favoriteMaster.MasterId),
            row.Wins, row.Losses, row.WinStreak);
    }

    private IReadOnlyList<string> PlayerTitles(RankedProfileRow row, int factionRank,
        IReadOnlyDictionary<string, RankedMasterRecordRow> champions)
    {
        var titles = new List<string>();
        var factionTitle = FactionPlacementTitle(row, factionRank);
        if (factionTitle is not null) titles.Add(factionTitle);
        titles.AddRange(champions.Values.Where(item => item.AccountId == row.AccountId)
            .OrderBy(item => MasterName(item.MasterId), StringComparer.OrdinalIgnoreCase)
            .Select(item => MasterTitle(item.MasterId)));
        return titles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> PlayerMasterTitles(RankedProfileRow row,
        IReadOnlyDictionary<string, RankedMasterRecordRow> champions)
        => champions.Values.Where(item => item.AccountId == row.AccountId)
            .OrderBy(item => MasterName(item.MasterId), StringComparer.OrdinalIgnoreCase)
            .Select(item => MasterTitle(item.MasterId))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string? SelectedMasterTitle(RankedProfileRow row, IReadOnlyList<string> available)
        => available.FirstOrDefault(item => item.Equals(row.SelectedMasterTitle,
               StringComparison.OrdinalIgnoreCase)) ?? available.FirstOrDefault();

    private string? FactionPlacementTitle(RankedProfileRow row, int factionRank)
    {
        if (!IsHighestTier(row)) return null;
        var faction = string.IsNullOrWhiteSpace(row.Faction) ? null : FactionFor(row.Faction);
        return factionRank == 1 ? faction?.FirstTitle
            : factionRank is >= 2 and <= 5 ? faction?.TopFiveTitle : null;
    }

    private bool IsHighestTier(RankedProfileRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Faction)
            || row.PlacementPlayed < _data.RankedConfig!.PlacementMatches) return false;
        var faction = FactionFor(row.Faction);
        return TierIndex(row) == faction.Tiers.Count - 1;
    }

    private void UpdateMasterRecord(RankedProfileRow profile, string? masterId, bool won)
    {
        if (string.IsNullOrWhiteSpace(masterId)) return;
        var row = _data.RankedMasterRecords.FirstOrDefault(item => item.AccountId == profile.AccountId
            && SeasonIdsEqual(item.SeasonId, profile.SeasonId)
            && item.MasterId.Equals(masterId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new RankedMasterRecordRow
                { AccountId = profile.AccountId, SeasonId = profile.SeasonId, MasterId = masterId };
            _data.RankedMasterRecords.Add(row);
        }
        row.Games++;
        if (won) row.Wins++;
    }

    private RankedProfileRow? RankedProfileByUsername(string username, string seasonId)
    {
        var account = _data.Accounts.FirstOrDefault(item => !item.Disabled && !item.Deleted
            && item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        return account is null ? null : _data.RankedProfiles.FirstOrDefault(item => item.AccountId == account.Id
            && SeasonIdsEqual(item.SeasonId, seasonId));
    }

    private string? RankingMasterId(string? id, string name) => id is null ? MasterIdByName(name)
        : SelectableMasterIds().FirstOrDefault(candidate => candidate.Equals(id, StringComparison.OrdinalIgnoreCase));

    private string? MasterIdByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return SelectableMasterIds()
            .FirstOrDefault(id => _officialCards.TryGetValue(id, out var card)
                && card.NameZh.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> SelectableMasterIds()
        => _officialCards.Values.Where(card => card.CardType == "master" && card.Id != "S01-02M2")
            .OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase).Select(card => card.Id);

    private Dictionary<string, RankedMasterRecordRow> CurrentMasterChampions()
        => ProjectCurrentMasterChampions(DateTimeOffset.UtcNow);

    private string MasterName(string masterId)
        => _data.RankedConfig!.MasterTitles.FirstOrDefault(item => item.MasterId.Equals(masterId,
               StringComparison.OrdinalIgnoreCase))?.MasterName
           ?? (_officialCards.TryGetValue(masterId, out var card) ? card.NameZh : masterId);

    private string MasterTitle(string masterId)
        => _data.RankedConfig!.MasterTitles.FirstOrDefault(item => item.MasterId.Equals(masterId,
               StringComparison.OrdinalIgnoreCase))?.Title
           ?? DefaultMasterTitle(MasterName(masterId));

    private int FactionRank(RankedProfileRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Faction) || row.PlacementPlayed < _data.RankedConfig!.PlacementMatches) return 0;
        return _data.RankedProfiles.Where(item => SeasonIdsEqual(item.SeasonId, row.SeasonId)
                && item.Faction == row.Faction
                && item.PlacementPlayed >= _data.RankedConfig.PlacementMatches
                && IsActiveAccountLocked(item.AccountId))
            .OrderByDescending(item => item.SevenValue).ThenByDescending(item => item.HiddenRating)
            .ThenBy(item => AccountName(item.AccountId), StringComparer.OrdinalIgnoreCase).ToList().IndexOf(row) + 1;
    }

    private int OverallRank(RankedProfileRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Faction)
            || row.PlacementPlayed < _data.RankedConfig!.PlacementMatches) return 0;
        return _data.RankedProfiles.Where(item => SeasonIdsEqual(item.SeasonId, row.SeasonId)
                && !string.IsNullOrWhiteSpace(item.Faction)
                && item.PlacementPlayed >= _data.RankedConfig.PlacementMatches
                && IsActiveAccountLocked(item.AccountId))
            .OrderByDescending(item => item.SevenValue).ThenByDescending(item => item.HiddenRating)
            .ThenBy(item => AccountName(item.AccountId), StringComparer.OrdinalIgnoreCase)
            .ToList().IndexOf(row) + 1;
    }

    private bool ParticipatedInRankedSeasonLocked(string accountId, string seasonId)
        => _data.RankedIntegrityAudits.Any(row => SeasonIdsEqual(row.SeasonId, seasonId)
            && (row.FirstAccountId == accountId || row.SecondAccountId == accountId));

    private L12SeasonSummaryNotificationView SeasonSummaryView(RankedProfileHistoryRow row)
        => new(row.Id, row.SeasonId,
            HistoricalSeasonDisplayNameLocked(row.SeasonId, row.SeasonName),
            HistoricalHonorFactionNameLocked(row), row.Placed == true,
            row.RankLabel ?? "历史版本未记录", row.FactionRank, row.OverallRank,
            row.SevenValue, $"七曜值 {row.SevenValue:N0}", row.Wins, row.Losses,
            row.WinRate ?? (row.Wins + row.Losses == 0 ? null
                : Percentage(row.Wins, row.Wins + row.Losses)), row.FactionTitle,
            row.MasterTitles.ToArray(), row.Titles.ToArray(), row.SummaryAvailableAt!.Value);

    private Dictionary<string, string> CurrentFactionTitleAssignments()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _data.RankedProfiles.Where(row =>
                     SeasonIdsEqual(row.SeasonId, RequireOperationsConfig().Season.Id)))
        {
            var title = FactionPlacementTitle(row, FactionRank(row));
            if (title is not null) result[row.AccountId] = title;
        }
        return result;
    }

    private IReadOnlyDictionary<string, int> FactionTotalsLocked()
        => _data.RankedConfig!.Factions.ToDictionary(faction => faction.Name,
            faction => _data.RankedProfiles.Where(row =>
                    SeasonIdsEqual(row.SeasonId, RequireOperationsConfig().Season.Id)
                    && row.Faction == faction.Id && row.PlacementPlayed >= _data.RankedConfig.PlacementMatches
                    && _data.Accounts.Any(account => account.Id == row.AccountId && !account.Disabled && !account.Deleted))
                .Sum(row => row.SevenValue));

    private bool IsActiveAccountLocked(string accountId)
    {
        // 旧导入事实可能只保存了账号标识而没有可关联的账号行；未知历史身份继续保留，
        // 只有平台明确知道已禁用/删除的账号才从统计中排除。
        var account = _data.Accounts.FirstOrDefault(item => item.Id.Equals(accountId,
            StringComparison.OrdinalIgnoreCase));
        return account is null || !account.Disabled && !account.Deleted;
    }

    private RankedFactionRow FactionFor(string id) => _data.RankedConfig!.Factions.First(row => row.Id == id);
    private RankedTierRow TierFor(RankedProfileRow row) => FactionFor(row.Faction ?? "order").Tiers[RankedTierIndex(row.SevenValue)];
    private int TierIndex(RankedProfileRow row) => RankedTierIndex(row.SevenValue);
    private int RankedTierIndex(int value)
    {
        var tiers = _data.RankedConfig!.Factions[0].Tiers;
        var result = 0;
        for (var index = 0; index < tiers.Count; index++) if (value >= tiers[index].Minimum) result = index;
        return result;
    }
    private int FloorFor(int value) => _data.RankedConfig!.Factions[0].Tiers.Where(tier => value >= tier.Minimum).Max(tier => tier.Minimum);
    private int StreakTerminationReward(int opponentValue)
        => _data.RankedConfig!.Factions[0].Tiers[RankedTierIndex(opponentValue)]
            .StreakTerminationReward;
    private static int LegacyStreakTerminationReward(int tierIndex) => tierIndex switch
    {
        4 => 1250, 3 => 750, 2 => 400, 1 => 200, _ => 0,
    };
    private string AccountName(string id)
    {
        var account = _data.Accounts.FirstOrDefault(row => row.Id == id);
        return account is null ? "已注销玩家" : PublicUsername(account);
    }

    private L12RankedConfigView ToView(RankedConfigRow row) => new(row.PlacementMatches,
        row.PlacementMaximum, row.BroadcastEnabled, row.Factions.Select(faction => new L12RankedFactionConfig(
            faction.Id, faction.Name, faction.Color, faction.Icon, faction.FirstTitle, faction.TopFiveTitle,
            faction.Tiers.Select(tier => new L12RankedTierConfig(tier.Name, tier.Minimum, tier.BaseDelta,
                tier.WinStreakCap, tier.LossProtectionCap, tier.RatingGapCap,
                tier.StreakTerminationReward, tier.Color, tier.Icon)).ToArray())).ToArray(),
        row.MasterTitles.Select(item => new L12RankedMasterTitleConfig(item.MasterId, item.MasterName, item.Title)).ToArray(),
        NormalizeRankedTimeControl(row.TimeControl), NormalizeRankedBroadcastConfig(row.Broadcast),
        _data.RankedPendingGradient is null ? null : new L12RankedPendingGradientConfig(
            _data.RankedPendingGradient.AfterSeasonId,
            _data.RankedPendingGradient.Tiers.Select(tier => new L12RankedTierGradientConfig(
                tier.Name, tier.Minimum, tier.BaseDelta, tier.WinStreakCap, tier.LossProtectionCap,
                tier.RatingGapCap, tier.StreakTerminationReward)).ToArray()));
    private L12RankedSettlementView ToView(RankedSettlementRow row)
    {
        var rewardStatus = RankedRewardStatusLocked(row.MatchId);
        var effectiveDelta = rewardStatus is "applied" or "released" ? row.Delta : 0;
        var pendingDelta = rewardStatus == "held" ? row.Delta : 0;
        return new(row.MatchId, row.AccountId, FactionFor(row.Faction).Name, row.Outcome,
            row.Won, row.Placement, row.PlacementPlayed, row.PlacementRequired, row.Before,
            row.After, row.Delta, row.TierBefore, row.TierAfter, row.Components.ToArray(),
            row.SettledAt, rewardStatus, effectiveDelta, pendingDelta);
    }
    private static L12RankedBroadcastView ToView(RankedBroadcastRow row) => new(row.Id, row.MatchId,
        row.EventType, row.Message, row.CreatedAt);

    private static RankedConfigRow NormalizeRankedConfig(L12RankedConfigView value)
    {
        if (value.PlacementMatches is < 1 or > 20 || value.PlacementMaximum < 0)
            throw new L12OperationsConfigException("invalid_ranked_config", "定级场次需为1–20，定级上限不得超过第二段位");
        if (value.Factions.Count != 3 || !value.Factions.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RankedFactionIds))
            throw new L12OperationsConfigException("invalid_ranked_factions", "排位派系必须且只能为秩序、混沌、命运");
        var row = new RankedConfigRow { PlacementMatches = value.PlacementMatches,
            PlacementMaximum = value.PlacementMaximum, BroadcastEnabled = value.BroadcastEnabled,
            TimeControl = NormalizeRankedTimeControl(value.TimeControl),
            Broadcast = NormalizeRankedBroadcastConfig(value.Broadcast) };
        foreach (var faction in value.Factions)
        {
            if (faction.Tiers.Count != 5) throw new L12OperationsConfigException("invalid_ranked_tiers", "每个派系必须恰好配置5个段位");
            var tiers = faction.Tiers.ToArray();
            if (tiers[0].Minimum != 0 || tiers.Any(tier => tier.Minimum is < 0 or > 1_000_000_000)
                || tiers.Skip(1).Where((tier, index) => tier.Minimum <= tiers[index].Minimum).Any())
                throw new L12OperationsConfigException("invalid_ranked_thresholds", "首段门槛必须为0，五段门槛须按等级严格递增且不超过10亿");
            if (value.PlacementMaximum >= tiers[2].Minimum)
                throw new L12OperationsConfigException("invalid_ranked_config", "定级七曜上限必须低于第三段位门槛（不超过第二段位）");
            if (tiers.Any(tier => tier.BaseDelta is < 0 or > 1_000_000
                || tier.WinStreakCap is < 0 or > 1_000_000 || tier.LossProtectionCap is < 0 or > 1_000_000
                || tier.RatingGapCap is < 0 or > 1_000_000
                || tier.StreakTerminationReward is < 0 or > 1_000_000))
                throw new L12OperationsConfigException("invalid_ranked_tier_values", "段位各项分值须为0至100万之间的整数");
            row.Factions.Add(new RankedFactionRow { Id = faction.Id.ToLowerInvariant(), Name = faction.Name.Trim(),
                Color = faction.Color.Trim(), Icon = faction.Icon.Trim(), FirstTitle = faction.FirstTitle.Trim(),
                TopFiveTitle = faction.TopFiveTitle.Trim(), Tiers = tiers.Select(tier => new RankedTierRow
                { Name = tier.Name.Trim(), Minimum = tier.Minimum, BaseDelta = Math.Max(0, tier.BaseDelta),
                    WinStreakCap = Math.Max(0, tier.WinStreakCap), LossProtectionCap = Math.Max(0, tier.LossProtectionCap),
                    RatingGapCap = Math.Max(0, tier.RatingGapCap),
                    StreakTerminationReward = Math.Max(0, tier.StreakTerminationReward),
                    Color = tier.Color.Trim(), Icon = tier.Icon.Trim() }).ToList() });
        }
        var sharedTierValues = value.Factions.Select(faction => faction.Tiers
            .Select(tier => (tier.Minimum, tier.BaseDelta, tier.WinStreakCap,
                tier.LossProtectionCap, tier.RatingGapCap,
                tier.StreakTerminationReward)).ToArray()).ToArray();
        if (sharedTierValues.Skip(1).Any(tiers => !tiers.SequenceEqual(sharedTierValues[0])))
            throw new L12OperationsConfigException("inconsistent_ranked_tier_values",
                "同一段位的阈值、基础分、连胜上限、连败保护上限与分差修正上限必须在三个派系中保持一致");
        foreach (var master in value.MasterTitles ?? [])
        {
            if (string.IsNullOrWhiteSpace(master.MasterId) || string.IsNullOrWhiteSpace(master.Title)) continue;
            if (row.MasterTitles.Any(item => item.MasterId.Equals(master.MasterId, StringComparison.OrdinalIgnoreCase)))
                throw new L12OperationsConfigException("duplicate_ranked_master_title", "同一主宰只能配置一个最强玩家称号");
            row.MasterTitles.Add(new RankedMasterTitleRow
            {
                MasterId = master.MasterId.Trim(),
                MasterName = string.IsNullOrWhiteSpace(master.MasterName) ? master.MasterId.Trim() : master.MasterName.Trim(),
                Title = master.Title.Trim(),
            });
        }
        return row;
    }

    internal static L12RankedTimeControlConfig NormalizeRankedTimeControl(L12RankedTimeControlConfig? value)
    {
        value ??= DefaultRankedTimeControl();
        if (value.TotalTimeSeconds is < 300 or > 7200)
            throw new L12OperationsConfigException("invalid_ranked_total_time", "排位总操作时限需为300–7200秒");
        if (value.OperationTimeSeconds is < 15 or > 900
            || value.OperationTimeSeconds > value.TotalTimeSeconds)
            throw new L12OperationsConfigException("invalid_ranked_operation_time", "排位单步时限需为15–900秒且不得超过总操作时限");
        if (value.ReconnectGraceSeconds is < 15 or > 900)
            throw new L12OperationsConfigException("invalid_ranked_reconnect_grace", "排位断线宽限需为15–900秒");
        if (value.DisasterDecisionSeconds is < 10 or > 300
            || value.MulliganDecisionSeconds is < 10 or > 300)
            throw new L12OperationsConfigException("invalid_ranked_setup_time", "排位天灾选择与调度时限均需为10–300秒");
        return value;
    }

    internal static L12RankedBroadcastConfig NormalizeRankedBroadcastConfig(L12RankedBroadcastConfig? value)
    {
        value ??= DefaultRankedBroadcastConfig();
        if (value.DisplaySeconds is < 5 or > 120)
            throw new L12OperationsConfigException("invalid_ranked_broadcast_display", "广播显示时长需为5–120秒");
        if (value.LobbyDelaySeconds is < 0 or > 120)
            throw new L12OperationsConfigException("invalid_ranked_broadcast_lobby_delay", "大厅进入延迟需为0–120秒");
        if (value.IntervalSeconds is < 3 or > 600)
            throw new L12OperationsConfigException("invalid_ranked_broadcast_interval", "广播间隔需为3–600秒");
        if (value.WinStreakThreshold is < 2 or > 100 || value.StreakEndedThreshold is < 2 or > 100)
            throw new L12OperationsConfigException("invalid_ranked_broadcast_streak", "连胜与终结连胜门槛需为2–100场");
        if (value.MinimumTierIndex is < 0 or > 4)
            throw new L12OperationsConfigException("invalid_ranked_broadcast_tier", "广播最低段位必须是第1–5段");
        return value;
    }
}
