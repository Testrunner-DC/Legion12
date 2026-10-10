using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private sealed record CommittedRankedAccount(string Username, bool Active);

    private sealed record CommittedRankedProfile(
        string AccountId,
        int SourceOrder,
        string SeasonId,
        string? Faction,
        double HiddenRating,
        int SevenValue,
        int PlacementPlayed,
        string? SelectedMasterTitle);

    private sealed record CommittedRankedTier(string Name, int Minimum);

    private sealed record CommittedRankedFaction(
        string Id,
        string Name,
        string FirstTitle,
        string TopFiveTitle,
        ImmutableArray<CommittedRankedTier> Tiers);

    private sealed record CommittedRankedMaster(string Name, string Title);

    private sealed record CommittedRankedConfig(
        int PlacementMatches,
        ImmutableArray<int> SharedTierMinimums,
        FrozenDictionary<string, CommittedRankedFaction> Factions,
        FrozenDictionary<string, CommittedRankedMaster> Masters);

    private sealed record CommittedRankedIdentity(
        bool Available,
        string CurrentSeasonId,
        CommittedRankedConfig? Config,
        FrozenDictionary<string, CommittedRankedAccount> Accounts,
        FrozenDictionary<string, CommittedRankedProfile> Profiles,
        RankedMasterTitleFactIndex? MasterTitleIndex,
        FrozenSet<string> ExcludedMatchIds,
        FrozenSet<string> KnownInactiveAccountIds,
        FrozenSet<string> ChampionEligibleAccountIds,
        FrozenSet<string> SelectableMasterIds);

    private static readonly CommittedRankedIdentity UnavailableRankedIdentity = new(false,
        string.Empty, null,
        FrozenDictionary<string, CommittedRankedAccount>.Empty,
        FrozenDictionary<string, CommittedRankedProfile>.Empty,
        null,
        FrozenSet<string>.Empty, FrozenSet<string>.Empty, FrozenSet<string>.Empty,
        FrozenSet<string>.Empty);

    private CommittedRankedIdentity PrepareCommittedRankedIdentity(DataFile data)
    {
        StorageFailureInjector?.Invoke("before-ranked-identity-index-build");
        if (data.OperationsConfig?.Season is null || data.RankedConfig is null)
        {
            StorageFailureInjector?.Invoke("after-ranked-identity-index-build");
            return UnavailableRankedIdentity;
        }

        var ranked = data.RankedConfig;
        if (ranked.PlacementMatches < 1 || ranked.Factions.Count == 0)
            throw new InvalidDataException("已提交排位配置无法投影");

        var factionRows = ranked.Factions.Select(faction =>
        {
            if (string.IsNullOrWhiteSpace(faction.Id) || faction.Tiers.Count == 0)
                throw new InvalidDataException("已提交排位派系配置无法投影");
            var tiers = faction.Tiers.Select(tier =>
                    new CommittedRankedTier(tier.Name, tier.Minimum))
                .ToImmutableArray();
            if (tiers[0].Minimum != 0
                || tiers.Skip(1).Where((tier, index) => tier.Minimum <= tiers[index].Minimum).Any())
                throw new InvalidDataException("已提交排位段位配置无法投影");
            return new CommittedRankedFaction(faction.Id, faction.Name, faction.FirstTitle,
                faction.TopFiveTitle, tiers);
        }).ToArray();
        var factions = factionRows.ToFrozenDictionary(faction => faction.Id, StringComparer.Ordinal);
        var sharedTierMinimums = factionRows[0].Tiers.Select(tier => tier.Minimum)
            .ToImmutableArray();
        if (factionRows.Any(faction => faction.Tiers.Length != sharedTierMinimums.Length
            || !faction.Tiers.Select(tier => tier.Minimum).SequenceEqual(sharedTierMinimums)))
            throw new InvalidDataException("已提交排位段位梯度不一致");

        var configuredMasters = ranked.MasterTitles.ToDictionary(row => row.MasterId,
            StringComparer.OrdinalIgnoreCase);
        var masters = _officialCards.Values
            .Where(card => card.CardType == "master" && card.Id != "S01-02M2")
            .Select(card =>
            {
                configuredMasters.TryGetValue(card.Id, out var configured);
                var name = configured?.MasterName ?? card.NameZh;
                var title = configured?.Title ?? DefaultMasterTitle(name);
                return new KeyValuePair<string, CommittedRankedMaster>(card.Id,
                    new CommittedRankedMaster(name, title));
            }).ToFrozenDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var accounts = data.Accounts.ToFrozenDictionary(row => row.Id,
            row => new CommittedRankedAccount(PublicUsername(row), !row.Disabled && !row.Deleted),
            StringComparer.Ordinal);
        var profiles = data.RankedProfiles.Select((row, sourceOrder) =>
                new CommittedRankedProfile(row.AccountId, sourceOrder, row.SeasonId, row.Faction,
                    row.HiddenRating, row.SevenValue, row.PlacementPlayed, row.SelectedMasterTitle))
            .ToFrozenDictionary(row => row.AccountId, StringComparer.Ordinal);
        // RankedBattleIdentityAt remains an exact historical-time projection. Keep every committed
        // fact here; this index bounds query fan-out by master, not authoritative history retention.
        var facts = data.RankedMasterRecords.SelectMany(row => row.TitleFacts ?? [])
            .Select(fact => fact with { }).ToImmutableArray();
        var factIndex = IndexRankedMasterTitleFacts(facts);
        var excluded = ProjectRankedMasterTitleExclusions(data, facts);
        var inactive = data.Accounts.Where(row => row.Disabled || row.Deleted).Select(row => row.Id)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var eligible = data.Accounts.Where(row => !row.Disabled && !row.Deleted).Select(row => row.Id)
            .ToFrozenSet(StringComparer.Ordinal);

        var prepared = new CommittedRankedIdentity(true,
            NormalizeSeasonIdentity(data.OperationsConfig.Season.Id),
            new CommittedRankedConfig(ranked.PlacementMatches, sharedTierMinimums, factions, masters),
            accounts, profiles, factIndex, excluded, inactive, eligible,
            masters.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        StorageFailureInjector?.Invoke("after-ranked-identity-index-build");
        return prepared;
    }

    private L12RankedBattleIdentityView ReadCommittedRankedBattleIdentity(string accountId,
        int playerIndex)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        var utcNow = DateTimeOffset.UtcNow;
        return ProjectCommittedRankedBattleIdentity(committed, accountId, playerIndex, utcNow);
    }

    private L12RankedBattleIdentityView ReadCommittedRankedBattleIdentityAt(string accountId,
        int playerIndex, DateTimeOffset utcNow)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        return ProjectCommittedRankedBattleIdentity(committed, accountId, playerIndex, utcNow);
    }

    private static L12RankedBattleIdentityView ProjectCommittedRankedBattleIdentity(
        CommittedSessionActivity committed, string accountId, int playerIndex, DateTimeOffset utcNow)
    {
        var identity = committed.RankedIdentity;
        if (!committed.Available || identity is null || !identity.Available
            || identity.Config is null || identity.MasterTitleIndex is null)
            throw new L12PlatformStorageUnavailableException("平台已提交排位身份不可恢复");
        if (!identity.Accounts.TryGetValue(accountId, out var account) || !account.Active)
            throw new KeyNotFoundException("账号不存在或不可用");

        var profile = identity.Profiles.TryGetValue(accountId, out var stored)
            ? stored : new CommittedRankedProfile(accountId, int.MaxValue,
                identity.CurrentSeasonId, null, 1500, 0, 0, null);
        if (!SeasonIdsEqual(profile.SeasonId, identity.CurrentSeasonId))
            profile = profile with
            {
                SeasonId = identity.CurrentSeasonId,
                SevenValue = 0,
                PlacementPlayed = 0,
            };

        var config = identity.Config;
        if (string.IsNullOrWhiteSpace(profile.Faction))
            return new L12RankedBattleIdentityView(playerIndex, string.Empty, null,
                string.Empty, null, SelectedCommittedMasterTitle(profile, identity, utcNow), false);
        if (!config.Factions.TryGetValue(profile.Faction, out var faction))
            throw new L12PlatformStorageUnavailableException("平台已提交排位派系无法恢复");

        var placed = profile.PlacementPlayed >= config.PlacementMatches;
        var tierIndex = CommittedRankedTierIndex(config, profile.SevenValue);
        if (tierIndex >= faction.Tiers.Length)
            throw new L12PlatformStorageUnavailableException("平台已提交排位段位无法恢复");
        var highestTier = placed && tierIndex == faction.Tiers.Length - 1;
        var factionRank = highestTier ? CommittedFactionRank(identity, profile) : 0;
        var placementTitle = !highestTier ? null : factionRank == 1 ? faction.FirstTitle
            : factionRank is >= 2 and <= 5 ? faction.TopFiveTitle : null;
        var overallRank = highestTier ? CommittedOverallRank(identity, profile) : 0;
        var tier = placed ? faction.Tiers[tierIndex].Name
            : $"定级 {profile.PlacementPlayed}/{config.PlacementMatches}";
        return new L12RankedBattleIdentityView(playerIndex, faction.Name,
            overallRank > 0 ? overallRank : null, tier, placementTitle,
            SelectedCommittedMasterTitle(profile, identity, utcNow), highestTier);
    }

    private static int CommittedRankedTierIndex(CommittedRankedConfig config, int value)
    {
        var result = 0;
        for (var index = 0; index < config.SharedTierMinimums.Length; index++)
            if (value >= config.SharedTierMinimums[index]) result = index;
        return result;
    }

    private static int CommittedFactionRank(CommittedRankedIdentity identity,
        CommittedRankedProfile profile)
        => CommittedRankedRows(identity)
            .Where(row => string.Equals(row.Faction, profile.Faction, StringComparison.Ordinal)
                && row.PlacementPlayed >= identity.Config!.PlacementMatches)
            .OrderByDescending(row => row.SevenValue).ThenByDescending(row => row.HiddenRating)
            .ThenBy(row => CommittedAccountName(identity, row.AccountId),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SourceOrder)
            .Select((row, index) => (row.AccountId, Rank: index + 1))
            .FirstOrDefault(item => item.AccountId == profile.AccountId).Rank;

    private static int CommittedOverallRank(CommittedRankedIdentity identity,
        CommittedRankedProfile profile)
        => CommittedRankedRows(identity)
            .Where(row => !string.IsNullOrWhiteSpace(row.Faction)
                && row.PlacementPlayed >= identity.Config!.PlacementMatches)
            .OrderByDescending(row => row.SevenValue).ThenByDescending(row => row.HiddenRating)
            .ThenBy(row => CommittedAccountName(identity, row.AccountId),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SourceOrder)
            .Select((row, index) => (row.AccountId, Rank: index + 1))
            .FirstOrDefault(item => item.AccountId == profile.AccountId).Rank;

    private static IEnumerable<CommittedRankedProfile> CommittedRankedRows(
        CommittedRankedIdentity identity)
        => identity.Profiles.Values.Where(row => SeasonIdsEqual(row.SeasonId,
                identity.CurrentSeasonId)
            && !identity.KnownInactiveAccountIds.Contains(row.AccountId));

    private static string CommittedAccountName(CommittedRankedIdentity identity, string accountId)
        => identity.Accounts.TryGetValue(accountId, out var account)
            ? account.Username : "已注销玩家";

    private static string? SelectedCommittedMasterTitle(CommittedRankedProfile profile,
        CommittedRankedIdentity identity, DateTimeOffset utcNow)
    {
        var index = identity.MasterTitleIndex!;
        if (!index.MasterIdsByAccount.TryGetValue(profile.AccountId, out var requestedMasterIds))
            return null;
        // Select only ladders this account appeared in; each selected ladder still contains the
        // full server population needed by the sparse threshold and leave-one-player-out baseline.
        var champions = ProjectRankedMasterTitleChampions(index, requestedMasterIds,
            identity.ExcludedMatchIds, identity.KnownInactiveAccountIds,
            identity.ChampionEligibleAccountIds, identity.SelectableMasterIds, utcNow);
        var available = champions.Values.Where(row => row.AccountId == profile.AccountId)
            .Select(row => (row.MasterId, Master: identity.Config!.Masters[row.MasterId]))
            .OrderBy(row => row.Master.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => row.Master.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return available.FirstOrDefault(title => title.Equals(profile.SelectedMasterTitle,
                   StringComparison.OrdinalIgnoreCase))
               ?? available.FirstOrDefault();
    }
}
