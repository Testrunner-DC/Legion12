namespace TwelveLegions.Server;

public sealed record L12RankedTierConfig(string Name, int Minimum, int BaseDelta,
    int WinStreakCap, int LossProtectionCap, int RatingGapCap, string Color, string Icon);
public sealed record L12RankedMasterTitleConfig(string MasterId, string MasterName, string Title);
public sealed record L12RankedFactionConfig(string Id, string Name, string Color, string Icon,
    string FirstTitle, string TopFiveTitle, IReadOnlyList<L12RankedTierConfig> Tiers);
public sealed record L12RankedConfigView(int PlacementMatches, int PlacementMaximum,
    bool BroadcastEnabled, IReadOnlyList<L12RankedFactionConfig> Factions,
    IReadOnlyList<L12RankedMasterTitleConfig> MasterTitles);
public sealed record L12RankedProfileView(string AccountId, string Username, string SeasonId,
    string? Faction, int SevenValue, string DisplayValue, int PlacementPlayed, int PlacementWins,
    bool Placed, int Wins, int Losses, int WinStreak, int LossStreak, string Tier,
    int TierIndex, int FactionRank, string? Title, IReadOnlyList<string> Titles,
    string RankLabel, string? PlacementTitle, string? SelectedMasterTitle,
    IReadOnlyList<string> MasterTitles);
public sealed record L12RankedBattleIdentityView(int PlayerIndex, string RankLabel,
    string? MasterTitle);
public sealed record L12RankedProfileHistoryView(string SeasonId, string Faction, int SevenValue,
    int PlacementPlayed, int PlacementWins, int Wins, int Losses, int WinStreak,
    DateTimeOffset ArchivedAt);
public sealed record L12RankedSeasonHonorView(string SeasonId, string SeasonName, string Username,
    string Faction, string Tier, int SevenValue, string DisplayValue,
    IReadOnlyList<string> Titles, DateTimeOffset AwardedAt);
public sealed record L12RankedSettlementComponent(string Kind, string Label, int Value);
public sealed record L12RankedSettlementView(string MatchId, string AccountId, string Faction,
    bool Won, bool Placement, int PlacementPlayed, int PlacementRequired, int Before, int After,
    int Delta, string TierBefore, string TierAfter, IReadOnlyList<L12RankedSettlementComponent> Components,
    DateTimeOffset SettledAt);
public sealed record L12RankedBroadcastView(string Id, string MatchId, string EventType,
    string Message, DateTimeOffset CreatedAt);
public sealed record L12RankedBroadcastClaimView(L12RankedBroadcastView Broadcast,
    string ClaimToken, DateTimeOffset LeaseExpiresAt);
public sealed record L12RankedLeaderboardEntry(int Rank, string Username,
    string Faction, int SevenValue, string DisplayValue, string Tier, string? Title,
    IReadOnlyList<string> Titles, int Wins, int Losses, int WinStreak);
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
    IReadOnlyList<L12RankedMatchupStatsView> Matchups);
public sealed record L12RankedOverviewView(L12RankedProfileView Profile,
    IReadOnlyDictionary<string, int> FactionTotals, L12RankedConfigView Config,
    IReadOnlyList<L12RankedProfileHistoryView> History);
public sealed record L12RankedSettlementPair(L12RankedSettlementView First,
    L12RankedSettlementView Second, IReadOnlyList<L12RankedBroadcastView> Broadcasts);

public sealed partial class L12PlatformStore
{
    private static readonly string[] RankedFactionIds = ["order", "chaos", "fate"];
    private sealed class RankedTierRow
    {
        public string Name { get; set; } = string.Empty;
        public int Minimum { get; set; }
        public int BaseDelta { get; set; }
        public int WinStreakCap { get; set; }
        public int LossProtectionCap { get; set; }
        public int RatingGapCap { get; set; }
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
        public List<RankedFactionRow> Factions { get; set; } = [];
        public List<RankedMasterTitleRow> MasterTitles { get; set; } = [];
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
    private sealed class RankedProfileHistoryRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AccountId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string UsernameSnapshot { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
        public int SevenValue { get; set; }
        public int PlacementPlayed { get; set; }
        public int PlacementWins { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int WinStreak { get; set; }
        public string SeasonName { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public List<string> Titles { get; set; } = [];
        public bool FinalizedSeasonAwards { get; set; }
        public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;
    }
    private sealed class RankedSettlementRow
    {
        public string MatchId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
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
    }
    private sealed class RankedBroadcastDeliveryRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string BroadcastId { get; set; } = string.Empty;
        public string ClaimToken { get; set; } = string.Empty;
        public DateTimeOffset LeaseExpiresAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
    private sealed class RankedMasterRecordRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string MasterId { get; set; } = string.Empty;
        public int Games { get; set; }
        public int Wins { get; set; }
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
            _data.RankedProfiles ??= [];
            _data.RankedProfileHistory ??= [];
            foreach (var history in _data.RankedProfileHistory) history.Titles ??= [];
            _data.RankedSettlements ??= [];
            _data.RankedBroadcasts ??= [];
            _data.RankedBroadcastDeliveries ??= [];
            _data.RankedMasterRecords ??= [];
            _data.RankedMasterRecordedMatchIds ??= [];
            _data.RankedIntegrityAudits ??= [];
            _data.RankedConfig.MasterTitles ??= [];
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

    private static RankedConfigRow DefaultRankedConfig()
    {
        static List<RankedTierRow> Tiers() =>
        [
            new() { Name = "初阶", Minimum = 0, BaseDelta = 200, WinStreakCap = 100, LossProtectionCap = 50, RatingGapCap = 50, Color = "#87959c" },
            new() { Name = "进阶", Minimum = 15000, BaseDelta = 400, WinStreakCap = 200, LossProtectionCap = 100, RatingGapCap = 100, Color = "#67a7b7" },
            new() { Name = "精英", Minimum = 30000, BaseDelta = 800, WinStreakCap = 400, LossProtectionCap = 200, RatingGapCap = 200, Color = "#8d73c7" },
            new() { Name = "统领", Minimum = 60000, BaseDelta = 1500, WinStreakCap = 750, LossProtectionCap = 380, RatingGapCap = 380, Color = "#d5904b" },
            new() { Name = "冠冕", Minimum = 100000, BaseDelta = 2500, WinStreakCap = 1250, LossProtectionCap = 630, RatingGapCap = 630, Color = "#e4c15e" },
        ];
        return new RankedConfigRow
        {
            Factions =
            [
                new() { Id = "order", Name = "秩序", Color = "#5ea4c7", FirstTitle = "秩序冠首", TopFiveTitle = "秩序中枢", Tiers = Tiers() },
                new() { Id = "chaos", Name = "混沌", Color = "#c05d65", FirstTitle = "混沌冠首", TopFiveTitle = "混沌先声", Tiers = Tiers() },
                new() { Id = "fate", Name = "命运", Color = "#b698d2", FirstTitle = "命运冠首", TopFiveTitle = "命运织者", Tiers = Tiers() },
            ],
        };
    }

    public L12RankedConfigView RankedConfig(L12AccountView? actor = null)
    {
        if (actor is not null) EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        lock (_gate) return ToView(_data.RankedConfig!);
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
                    && item.SeasonId == row.SeasonId);
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
    {
        lock (_gate)
        {
            var row = RequireRankedProfile(accountId);
            var rank = FactionRank(row);
            var placementTitle = FactionPlacementTitle(row, rank);
            var rankLabel = placementTitle ?? (row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
                ? TierFor(row).Name
                : $"定级 {row.PlacementPlayed}/{_data.RankedConfig.PlacementMatches}");
            var masterTitles = PlayerMasterTitles(row, CurrentMasterChampions());
            var selected = SelectedMasterTitle(row, masterTitles);
            return new L12RankedBattleIdentityView(playerIndex, rankLabel, selected);
        }
    }

    internal double HiddenRating(string accountId)
    {
        lock (_gate) return RequireRankedProfile(accountId).HiddenRating;
    }

    public L12RankedOverviewView RankedOverview(string accountId)
    {
        lock (_gate)
        {
            var profile = RequireRankedProfile(accountId);
            var history = _data.RankedProfileHistory.Where(item => item.AccountId == accountId)
                .OrderByDescending(item => item.ArchivedAt)
                .Select(item => new L12RankedProfileHistoryView(item.SeasonId,
                    FactionFor(item.Faction).Name, item.SevenValue, item.PlacementPlayed,
                    item.PlacementWins, item.Wins, item.Losses, item.WinStreak, item.ArchivedAt))
                .ToArray();
            return new L12RankedOverviewView(ProfileView(profile), FactionTotalsLocked(),
                ToView(_data.RankedConfig!), history);
        }
    }

    public IReadOnlyList<L12RankedSeasonHonorView> RankedSeasonHonors(int limit = 500)
    {
        lock (_gate)
        {
            return _data.RankedProfileHistory
                .Where(row => row.FinalizedSeasonAwards && row.Titles.Count > 0)
                .OrderByDescending(row => row.ArchivedAt).ThenByDescending(row => row.SevenValue)
                .Take(Math.Clamp(limit, 1, 2000))
                .Select(row => new L12RankedSeasonHonorView(row.SeasonId,
                    string.IsNullOrWhiteSpace(row.SeasonName) ? row.SeasonId : row.SeasonName,
                    string.IsNullOrWhiteSpace(row.UsernameSnapshot) ? AccountName(row.AccountId) : row.UsernameSnapshot,
                    FactionFor(row.Faction).Name,
                    string.IsNullOrWhiteSpace(row.Tier) ? FactionFor(row.Faction).Tiers[RankedTierIndex(row.SevenValue)].Name : row.Tier,
                    row.SevenValue, $"七曜值 {row.SevenValue:N0}", row.Titles.ToArray(), row.ArchivedAt))
                .ToArray();
        }
    }

    public IReadOnlyList<L12RankedLeaderboardEntry> RankedLeaderboard(string? faction = null, int limit = 100)
    {
        lock (_gate)
        {
            var season = RequireOperationsConfig().Season.Id;
            var rows = _data.RankedProfiles.Where(row => row.SeasonId == season
                    && row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
                    && !string.IsNullOrWhiteSpace(row.Faction)
                    && (string.IsNullOrWhiteSpace(faction) || string.Equals(row.Faction, faction, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(row => row.SevenValue).ThenByDescending(row => row.HiddenRating)
                .ThenBy(row => AccountName(row.AccountId), StringComparer.OrdinalIgnoreCase).Take(Math.Clamp(limit, 1, 500)).ToArray();
            var champions = CurrentMasterChampions();
            return rows.Select((row, index) => LeaderboardView(row, index + 1, champions)).ToArray();
        }
    }

    public IReadOnlyList<L12RankedMasterChampionView> RankedMasterChampions()
    {
        lock (_gate)
        {
            return CurrentMasterChampions().Values
                .Select(record =>
                {
                    var profile = _data.RankedProfiles.First(row => row.AccountId == record.AccountId
                        && row.SeasonId == record.SeasonId);
                    var masterName = MasterName(record.MasterId);
                    return new L12RankedMasterChampionView(record.MasterId, masterName,
                        AccountName(record.AccountId), MasterTitle(record.MasterId), profile.SevenValue,
                        $"七曜值 {profile.SevenValue:N0}", record.Games, record.Wins);
                })
                .OrderBy(item => item.MasterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public L12RankedAnalyticsView RankedAnalytics(IReadOnlyList<L12RankingMatch> source, string? requestedRange)
    {
        lock (_gate)
        {
            var range = requestedRange is "7d" or "30d" ? requestedRange : "season";
            var season = RequireOperationsConfig().Season;
            var now = DateTimeOffset.UtcNow;
            var seasonStart = season.StartsAt ?? DateTimeOffset.MinValue;
            var rangeStart = range == "7d" ? now.AddDays(-7) : range == "30d" ? now.AddDays(-30) : seasonStart;
            if (rangeStart < seasonStart) rangeStart = seasonStart;
            var rangeEnd = season.EndsAt ?? DateTimeOffset.MaxValue;
            var matches = source.Select(match => new
                {
                    Match = match,
                    Started = DateTimeOffset.TryParse(match.StartedUtc, out var started) ? started : (DateTimeOffset?)null,
                    Ended = DateTimeOffset.TryParse(match.EndedUtc, out var ended) ? ended : (DateTimeOffset?)null,
                    Master0 = MasterIdByName(match.Master0),
                    Master1 = MasterIdByName(match.Master1),
                })
                .Where(item => item.Started is not null && item.Started >= rangeStart && item.Started <= rangeEnd
                    && item.Match.Winner is 0 or 1 && item.Master0 is not null && item.Master1 is not null)
                .ToArray();
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
            var placedPlayers = _data.RankedProfiles.Count(row => row.SeasonId == season.Id
                && row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
                && _data.Accounts.Any(account => account.Id == row.AccountId && !account.Disabled && !account.Deleted));
            var updatedAt = matches.Select(item => item.Ended ?? item.Started).Where(value => value is not null)
                .Select(value => value!.Value).DefaultIfEmpty().Max();
            return new L12RankedAnalyticsView(range,
                new L12RankedAnalyticsSummary(matches.Length, placedPlayers, masters.Count,
                    updatedAt == default ? null : updatedAt), masterViews, matchupViews);

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

    public L12RankedBroadcastClaimView? ClaimRankedBroadcast(string accountId)
    {
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Disabled && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在或不可用");
            var now = DateTimeOffset.UtcNow;
            var pending = _data.RankedBroadcastDeliveries
                .Where(row => row.AccountId == accountId && row.CompletedAt is null)
                .OrderBy(row => row.LeaseExpiresAt).FirstOrDefault();
            if (pending is not null)
            {
                if (pending.LeaseExpiresAt > now) return null;
                var pendingBroadcast = _data.RankedBroadcasts.FirstOrDefault(row => row.Id == pending.BroadcastId);
                if (pendingBroadcast is not null)
                {
                    pending.ClaimToken = Guid.NewGuid().ToString("N");
                    pending.LeaseExpiresAt = now.AddSeconds(45);
                    Save();
                    return new(ToView(pendingBroadcast), pending.ClaimToken, pending.LeaseExpiresAt);
                }
                _data.RankedBroadcastDeliveries.Remove(pending);
            }

            var cutoff = _data.RankedBroadcastDeliveryCutover ?? now;
            if (account.CreatedAt > cutoff) cutoff = account.CreatedAt;
            var delivered = _data.RankedBroadcastDeliveries.Where(row => row.AccountId == accountId)
                .Select(row => row.BroadcastId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var next = _data.RankedBroadcasts.Where(row => row.CreatedAt >= cutoff && !delivered.Contains(row.Id))
                .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (next is null) return null;
            var delivery = new RankedBroadcastDeliveryRow
            {
                AccountId = accountId,
                BroadcastId = next.Id,
                ClaimToken = Guid.NewGuid().ToString("N"),
                LeaseExpiresAt = now.AddSeconds(45),
            };
            _data.RankedBroadcastDeliveries.Add(delivery);
            Save();
            return new(ToView(next), delivery.ClaimToken, delivery.LeaseExpiresAt);
        }
    }

    public bool CompleteRankedBroadcast(string accountId, string broadcastId, string claimToken)
    {
        lock (_gate)
        {
            var delivery = _data.RankedBroadcastDeliveries.FirstOrDefault(row => row.AccountId == accountId
                && row.BroadcastId == broadcastId);
            if (delivery is null || string.IsNullOrWhiteSpace(claimToken)
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(delivery.ClaimToken),
                    System.Text.Encoding.UTF8.GetBytes(claimToken))) return false;
            if (delivery.CompletedAt is not null) return true;
            delivery.CompletedAt = DateTimeOffset.UtcNow;
            Save();
            return true;
        }
    }

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
        L12RankedIntegrityContext? integrity = null)
    {
        lock (_gate)
        {
            ValidateRankedIdentity(matchId, firstAccountId, secondAccountId, winner);
            if (TryGetRankedSettlementReplayLocked(matchId, firstAccountId, secondAccountId, winner,
                    firstMasterId, secondMasterId, integrity, out var replay)) return replay;

            var first = RequireRankedProfile(firstAccountId);
            var second = RequireRankedProfile(secondAccountId);
            if (string.IsNullOrWhiteSpace(first.Faction) || string.IsNullOrWhiteSpace(second.Faction))
                throw new InvalidOperationException("排位结算缺少赛季派系");
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
            var expectedFirst = 1d / (1d + Math.Pow(10d, (secondRating - firstRating) / 400d));
            first.HiddenRating = Math.Clamp(firstRating + 24d * ((winner == 0 ? 1d : 0d) - expectedFirst), 500d, 2500d);
            second.HiddenRating = Math.Clamp(secondRating + 24d * ((winner == 1 ? 1d : 0d) - (1d - expectedFirst)), 500d, 2500d);
            UpdateMasterRecord(first, firstMasterId, winner == 0);
            UpdateMasterRecord(second, secondMasterId, winner == 1);
            if (!_data.RankedMasterRecordedMatchIds.Contains(matchId, StringComparer.OrdinalIgnoreCase))
                _data.RankedMasterRecordedMatchIds.Add(matchId);
            _data.RankedSettlements.Add(firstSettlement);
            _data.RankedSettlements.Add(secondSettlement);
            EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, winner,
                firstMasterId, secondMasterId, integrity);
            var broadcasts = BuildBroadcasts(matchId, first, second, winner, beforeTitles,
                beforeMasterChampions, firstStreakBefore, secondStreakBefore,
                firstMasterId, secondMasterId);
            _data.RankedBroadcasts.AddRange(broadcasts);
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
            var terminate = won && opponentWinStreakBefore >= 5 ? StreakTerminationReward(opponentSevenBefore) : 0;
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
            MatchId = matchId, AccountId = player.AccountId, Faction = player.Faction!, Won = won,
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
        if (!_data.RankedConfig!.BroadcastEnabled) return [];
        var winnerRow = winner == 0 ? first : second;
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
        if (winnerRow.WinStreak >= 5) Add("win-streak", $"【{faction.Name}】{winnerName} 已取得 {winnerRow.WinStreak} 连胜");
        if (loserStreakBefore >= 5) Add("streak-ended", $"【{faction.Name}】{winnerName} 终结了 {AccountName(loserRow.AccountId)} 的 {loserStreakBefore} 连胜");
        if (!winnerRow.ReachedHighestTier && TierIndex(winnerRow) == 4)
        {
            winnerRow.ReachedHighestTier = true;
            Add("highest-tier", $"【{faction.Name}】{winnerName} 晋升至 {faction.Tiers[4].Name}");
        }
        var after = FactionRank(winnerRow);
        var afterTitle = FactionPlacementTitle(winnerRow, after);
        if (afterTitle is not null && beforeTitles.GetValueOrDefault(winnerRow.AccountId) != afterTitle)
            Add(after == 1 ? "faction-first" : "faction-top-five",
                $"【{faction.Name}】{winnerName} 获得称号「{afterTitle}」");
        var afterMasterChampions = CurrentMasterChampions();
        foreach (var masterId in new[] { firstMasterId, secondMasterId }.Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!afterMasterChampions.TryGetValue(masterId, out var champion)
                || beforeMasterChampions.GetValueOrDefault(masterId) == champion.AccountId) continue;
            var championProfile = _data.RankedProfiles.First(row => row.AccountId == champion.AccountId
                && row.SeasonId == champion.SeasonId);
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
        else if (row.SeasonId != season)
        {
            ArchiveRankedProfile(row);
            row.SeasonId = season;
            row.SevenValue = row.PlacementPlayed = row.PlacementWins = row.Wins = row.Losses = 0;
            row.WinStreak = row.LossStreak = row.HighestFloor = 0;
            row.ReachedHighestTier = false;
        }
        _ = account;
        return row;
    }

    private void ArchiveRankedProfile(RankedProfileRow row, string? seasonName = null,
        bool finalizedSeasonAwards = false, IReadOnlyList<string>? frozenTitles = null)
    {
        if (string.IsNullOrWhiteSpace(row.Faction) || row.PlacementPlayed == 0) return;
        if (_data.RankedProfileHistory.Any(history => history.AccountId == row.AccountId
                && history.SeasonId == row.SeasonId && history.FinalizedSeasonAwards)) return;
        _data.RankedProfileHistory.Add(new RankedProfileHistoryRow
        {
            AccountId = row.AccountId,
            SeasonId = row.SeasonId,
            UsernameSnapshot = AccountName(row.AccountId),
            Faction = row.Faction,
            SevenValue = row.SevenValue,
            PlacementPlayed = row.PlacementPlayed,
            PlacementWins = row.PlacementWins,
            Wins = row.Wins,
            Losses = row.Losses,
            WinStreak = row.WinStreak,
            SeasonName = seasonName ?? RequireOperationsConfig().Season.Name,
            Tier = TierFor(row).Name,
            Titles = frozenTitles?.ToList() ?? [],
            FinalizedSeasonAwards = finalizedSeasonAwards,
        });
    }

    private void FinalizeOutgoingRankedSeason(string outgoingSeasonId, string outgoingSeasonName,
        string incomingSeasonId)
    {
        if (string.Equals(outgoingSeasonId, incomingSeasonId, StringComparison.OrdinalIgnoreCase)) return;
        var champions = CurrentMasterChampions();
        var rows = _data.RankedProfiles.Where(row => row.SeasonId == outgoingSeasonId).ToArray();
        foreach (var row in rows)
            ArchiveRankedProfile(row, outgoingSeasonName, true, PlayerTitles(row, FactionRank(row), champions));
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
        var rankLabel = placementTitle ?? (row.PlacementPlayed >= _data.RankedConfig!.PlacementMatches
            ? tier.Name
            : $"定级 {row.PlacementPlayed}/{_data.RankedConfig.PlacementMatches}");
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
        return new(rank, AccountName(row.AccountId), faction.Name, row.SevenValue,
            $"七曜值 {row.SevenValue:N0}", TierFor(row).Name,
            titles.FirstOrDefault(), titles, row.Wins, row.Losses, row.WinStreak);
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
        if (TierIndex(row) != _data.RankedConfig!.Factions[0].Tiers.Count - 1) return null;
        var faction = string.IsNullOrWhiteSpace(row.Faction) ? null : FactionFor(row.Faction);
        return factionRank == 1 ? faction?.FirstTitle
            : factionRank is >= 2 and <= 5 ? faction?.TopFiveTitle : null;
    }

    private void UpdateMasterRecord(RankedProfileRow profile, string? masterId, bool won)
    {
        if (string.IsNullOrWhiteSpace(masterId)) return;
        var row = _data.RankedMasterRecords.FirstOrDefault(item => item.AccountId == profile.AccountId
            && item.SeasonId == profile.SeasonId
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
            && item.SeasonId == seasonId);
    }

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
    {
        var season = RequireOperationsConfig().Season.Id;
        return _data.RankedMasterRecords.Where(record => record.SeasonId == season
                && _data.Accounts.Any(account => account.Id == record.AccountId && !account.Disabled && !account.Deleted))
            .GroupBy(record => record.MasterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Select(record => new
                {
                    Record = record,
                    Profile = _data.RankedProfiles.FirstOrDefault(profile => profile.AccountId == record.AccountId
                        && profile.SeasonId == season && profile.PlacementPlayed >= _data.RankedConfig!.PlacementMatches),
                })
                .Where(item => item.Profile is not null)
                .OrderByDescending(item => item.Profile!.SevenValue)
                .ThenByDescending(item => item.Record.Wins)
                .ThenByDescending(item => item.Record.Games)
                .ThenByDescending(item => item.Profile!.HiddenRating)
                .ThenBy(item => AccountName(item.Record.AccountId), StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Record).FirstOrDefault())
            .Where(record => record is not null)
            .ToDictionary(record => record!.MasterId, record => record!, StringComparer.OrdinalIgnoreCase);
    }

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
        return _data.RankedProfiles.Where(item => item.SeasonId == row.SeasonId && item.Faction == row.Faction
                && item.PlacementPlayed >= _data.RankedConfig.PlacementMatches)
            .OrderByDescending(item => item.SevenValue).ThenByDescending(item => item.HiddenRating)
            .ThenBy(item => AccountName(item.AccountId), StringComparer.OrdinalIgnoreCase).ToList().IndexOf(row) + 1;
    }

    private Dictionary<string, string> CurrentFactionTitleAssignments()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _data.RankedProfiles.Where(row => row.SeasonId == RequireOperationsConfig().Season.Id))
        {
            var title = FactionPlacementTitle(row, FactionRank(row));
            if (title is not null) result[row.AccountId] = title;
        }
        return result;
    }

    private IReadOnlyDictionary<string, int> FactionTotalsLocked()
        => _data.RankedConfig!.Factions.ToDictionary(faction => faction.Name,
            faction => _data.RankedProfiles.Where(row => row.SeasonId == RequireOperationsConfig().Season.Id
                    && row.Faction == faction.Id && row.PlacementPlayed >= _data.RankedConfig.PlacementMatches
                    && _data.Accounts.Any(account => account.Id == row.AccountId && !account.Disabled && !account.Deleted))
                .Sum(row => row.SevenValue));

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
    private static int StreakTerminationReward(int opponentValue) => opponentValue >= 100000 ? 1250
        : opponentValue >= 60000 ? 750 : opponentValue >= 30000 ? 400 : opponentValue >= 15000 ? 200 : 0;
    private string AccountName(string id) => _data.Accounts.FirstOrDefault(row => row.Id == id)?.Username ?? "已注销玩家";

    private static L12RankedConfigView ToView(RankedConfigRow row) => new(row.PlacementMatches,
        row.PlacementMaximum, row.BroadcastEnabled, row.Factions.Select(faction => new L12RankedFactionConfig(
            faction.Id, faction.Name, faction.Color, faction.Icon, faction.FirstTitle, faction.TopFiveTitle,
            faction.Tiers.Select(tier => new L12RankedTierConfig(tier.Name, tier.Minimum, tier.BaseDelta,
                tier.WinStreakCap, tier.LossProtectionCap, tier.RatingGapCap, tier.Color, tier.Icon)).ToArray())).ToArray(),
        row.MasterTitles.Select(item => new L12RankedMasterTitleConfig(item.MasterId, item.MasterName, item.Title)).ToArray());
    private L12RankedSettlementView ToView(RankedSettlementRow row) => new(row.MatchId, row.AccountId,
        FactionFor(row.Faction).Name, row.Won, row.Placement, row.PlacementPlayed, row.PlacementRequired, row.Before, row.After,
        row.Delta, row.TierBefore, row.TierAfter, row.Components.ToArray(), row.SettledAt);
    private static L12RankedBroadcastView ToView(RankedBroadcastRow row) => new(row.Id, row.MatchId,
        row.EventType, row.Message, row.CreatedAt);

    private static RankedConfigRow NormalizeRankedConfig(L12RankedConfigView value)
    {
        if (value.PlacementMatches is < 1 or > 20 || value.PlacementMaximum is < 0 or > 29999)
            throw new L12OperationsConfigException("invalid_ranked_config", "定级场次需为1–20，定级上限不得超过第二段位");
        if (value.Factions.Count != 3 || !value.Factions.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RankedFactionIds))
            throw new L12OperationsConfigException("invalid_ranked_factions", "排位派系必须且只能为秩序、混沌、命运");
        var row = new RankedConfigRow { PlacementMatches = value.PlacementMatches,
            PlacementMaximum = value.PlacementMaximum, BroadcastEnabled = value.BroadcastEnabled };
        foreach (var faction in value.Factions)
        {
            if (faction.Tiers.Count != 5) throw new L12OperationsConfigException("invalid_ranked_tiers", "每个派系必须恰好配置5个段位");
            var tiers = faction.Tiers.OrderBy(tier => tier.Minimum).ToArray();
            if (!tiers.Select(tier => tier.Minimum).SequenceEqual([0, 15000, 30000, 60000, 100000]))
                throw new L12OperationsConfigException("invalid_ranked_thresholds", "五个段位阈值固定为0、15000、30000、60000、100000");
            row.Factions.Add(new RankedFactionRow { Id = faction.Id.ToLowerInvariant(), Name = faction.Name.Trim(),
                Color = faction.Color.Trim(), Icon = faction.Icon.Trim(), FirstTitle = faction.FirstTitle.Trim(),
                TopFiveTitle = faction.TopFiveTitle.Trim(), Tiers = tiers.Select(tier => new RankedTierRow
                { Name = tier.Name.Trim(), Minimum = tier.Minimum, BaseDelta = Math.Max(0, tier.BaseDelta),
                    WinStreakCap = Math.Max(0, tier.WinStreakCap), LossProtectionCap = Math.Max(0, tier.LossProtectionCap),
                    RatingGapCap = Math.Max(0, tier.RatingGapCap), Color = tier.Color.Trim(), Icon = tier.Icon.Trim() }).ToList() });
        }
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
}
