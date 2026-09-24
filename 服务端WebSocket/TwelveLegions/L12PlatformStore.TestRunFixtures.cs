namespace TwelveLegions.Server;

public sealed record L12TestRunAcceptanceFixtureSummary(string Owner, int Decks, int PublicDecks,
    int GuidedDecks, int RankedPlayers, int RankedMatches, int ActiveMasters, int HistoricalHonors);

public sealed partial class L12PlatformStore
{
    private const string AcceptanceDeckPrefix = "[验收] ";
    private const string AcceptancePlayerPrefix = "验收玩家";
    private const string AcceptanceHistorySeason = "testrun-previous-season";
    private readonly List<L12RankingMatch> _testRunAcceptanceRankedMatches = [];

    internal L12TestRunAcceptanceFixtureSummary EnsureTestRunAcceptanceFixtures()
    {
        var templates = _officialDecks
            .Where(deck => !string.IsNullOrWhiteSpace(deck.MasterId) && deck.CardIds.Count > 0)
            .GroupBy(deck => deck.MasterId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (templates.Length == 0)
            return new L12TestRunAcceptanceFixtureSummary("unavailable", 0, 0, 0, 0, 0, 0, 0);

        var horizontal = templates.FirstOrDefault(deck => deck.SpecialIds.Count > 0) ?? templates[0];
        var standard = templates.FirstOrDefault(deck => !string.Equals(deck.MasterId, horizontal.MasterId,
            StringComparison.OrdinalIgnoreCase)) ?? horizontal;
        var alternate = templates.FirstOrDefault(deck => !string.Equals(deck.MasterId, horizontal.MasterId,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(deck.MasterId, standard.MasterId, StringComparison.OrdinalIgnoreCase)) ?? standard;

        var desired = new[]
        {
            FixtureDeck(standard, $"{AcceptanceDeckPrefix}可删除牌库"),
            FixtureDeck(alternate, $"{AcceptanceDeckPrefix}指南与对局建议"),
            FixtureDeck(horizontal, $"{AcceptanceDeckPrefix}横卡与超长名称牌库用于编号卡名截断验收"),
        };

        string ownerId;
        string ownerName;
        lock (_gate)
        {
            var owner = _data.Accounts.FirstOrDefault(account => !account.Deleted && !account.Disabled
                    && string.Equals(account.Username, "Aimin", StringComparison.OrdinalIgnoreCase))
                ?? _data.Accounts.First(account => !account.Deleted && !account.Disabled
                    && string.Equals(account.Username, "Admin", StringComparison.Ordinal));
            ownerId = owner.Id;
            ownerName = PublicUsername(owner);
            var changed = false;
            foreach (var deck in desired)
            {
                if (_data.Decks.Any(row => row.AccountId == ownerId
                        && string.Equals(row.Name, deck.Name, StringComparison.OrdinalIgnoreCase))) continue;
                _data.Decks.Add(new DeckRow
                {
                    AccountId = ownerId,
                    Name = deck.Name,
                    MasterId = deck.MasterId,
                    CardIds = deck.CardIds.ToList(),
                    MoraleIds = deck.MoraleIds.ToList(),
                    SpecialIds = deck.SpecialIds.ToList(),
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                changed = true;
            }
            if (changed) Save();
        }

        var guided = 0;
        foreach (var deck in desired.Skip(1))
        {
            var publication = PublishedDecks(ownerId).FirstOrDefault(row =>
                    string.Equals(row.Deck.Name, deck.Name, StringComparison.OrdinalIgnoreCase))
                ?? PublishDeck(ownerId, deck, publicationId: null)
                ?? throw new InvalidOperationException($"无法建立测试服验收公开牌库：{deck.Name}");
            var current = PublicDeckDetails(publication.Id);
            if ((current?.ContentRevision ?? 0) == 0)
            {
                var opponents = templates.Where(row => !string.Equals(row.MasterId, deck.MasterId,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToArray();
                var matchups = opponents.Select((opponent, index) => new L12PublicDeckMatchupView(
                    opponent.MasterId,
                    index == 0
                        ? "验收数据：记录先后手差异、关键交换和资源保留时点；用于验证长内容不会挤压卡牌区域。"
                        : "验收数据：对方展开较快时优先稳定场面，详情页应完整显示本段文字。",
                    "验收数据：点击卡名仍应使用图鉴的统一卡牌详情。",
                    "验收数据：按实际对局环境替换，不作为正式构筑建议。"
                )).ToArray();
                UpdatePublicDeckContent(ownerId, publication.Id, new L12PublicDeckContentInput(
                    new L12PublicDeckGuideView(
                        "这是测试服验收用构筑思路。用于检查同页章节、长文本换行、锚点滚动和移动端阅读，不代表正式攻略。",
                        "优先保留能够建立基础场面的卡牌；这里用于验证起手建议编辑、保存和再次打开。",
                        "关键牌名称应可点击并打开与图鉴一致的详情，不应产生第二套变形卡面。",
                        "按资源、登场、进攻顺序记录常见展开；滚动定位后上下文不能丢失。",
                        "替换建议用于检查空内容与长内容边界，测试数据不会进入正式服。"),
                    matchups));
            }
            if ((PublicDeckDetails(publication.Id)?.ContentRevision ?? 0) > 0) guided++;

            lock (_gate)
            {
                var row = _data.PublishedDecks.First(item => item.Id == publication.Id);
                row.Views = Math.Max(row.Views, deck.SpecialIds.Count > 0 ? 96 : 48);
                row.Copies = Math.Max(row.Copies, deck.SpecialIds.Count > 0 ? 12 : 7);
                if (!row.LikedByAccountIds.Contains(ownerId, StringComparer.OrdinalIgnoreCase))
                    row.LikedByAccountIds.Add(ownerId);
                Save();
            }
        }

        var ranking = EnsureTestRunRankingFixtures(ownerId, templates);
        return new L12TestRunAcceptanceFixtureSummary(ownerName,
            Decks(ownerId).Count(deck => deck.Name.StartsWith(AcceptanceDeckPrefix, StringComparison.Ordinal)),
            PublishedDecks(ownerId).Count(deck => deck.Deck.Name.StartsWith(AcceptanceDeckPrefix,
                StringComparison.Ordinal)), guided, ranking.Players, ranking.Matches,
            ranking.Masters, ranking.Honors);
    }

    internal IReadOnlyList<L12RankingMatch> TestRunAcceptanceRankedMatches()
    {
        lock (_gate) return _testRunAcceptanceRankedMatches.ToArray();
    }

    internal L12PlayerStatisticsView MergeTestRunAcceptanceStatistics(string accountId,
        L12PlayerStatisticsView recorded)
    {
        lock (_gate)
        {
            var source = _testRunAcceptanceRankedMatches.Where(match =>
                    string.Equals(match.AccountId0, accountId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(match.AccountId1, accountId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (source.Length == 0) return recorded;

            var overall = recorded.Overall;
            var ranked = recorded.Ranked;
            var masters = recorded.Masters.ToDictionary(item => item.MasterId,
                item => item, StringComparer.OrdinalIgnoreCase);
            DateTimeOffset? updatedAt = recorded.UpdatedAt;
            foreach (var match in source)
            {
                var playerIndex = string.Equals(match.AccountId0, accountId,
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                var masterId = playerIndex == 0 ? match.MasterId0 : match.MasterId1;
                var masterName = playerIndex == 0 ? match.Master0 : match.Master1;
                overall = AddStat(overall, playerIndex, match.Winner, match.FirstPlayer);
                ranked = AddStat(ranked, playerIndex, match.Winner, match.FirstPlayer);
                if (!string.IsNullOrWhiteSpace(masterId))
                {
                    var existing = masters.TryGetValue(masterId, out var value)
                        ? value : new L12PlayerMasterStatistics(masterId,
                            string.IsNullOrWhiteSpace(masterName) ? masterId : masterName,
                            EmptyStat(), EmptyStat());
                    masters[masterId] = existing with
                    {
                        Overall = AddStat(existing.Overall, playerIndex, match.Winner, match.FirstPlayer),
                        Ranked = AddStat(existing.Ranked, playerIndex, match.Winner, match.FirstPlayer),
                    };
                }
                if (DateTimeOffset.TryParse(match.EndedUtc, out var ended)
                    && (!updatedAt.HasValue || ended > updatedAt)) updatedAt = ended;
            }
            return new L12PlayerStatisticsView(overall, ranked,
                masters.Values.OrderByDescending(item => item.Ranked.Games)
                    .ThenByDescending(item => item.Overall.Games).ToArray(), updatedAt);
        }
    }

    private (int Players, int Matches, int Masters, int Honors) EnsureTestRunRankingFixtures(
        string ownerId, IReadOnlyList<L12PresetDeckDefinition> templates)
    {
        var masterIds = templates.Select(item => item.MasterId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (masterIds.Length < 2) return (0, 0, 0, 0);

        List<AccountRow> players;
        List<L12RankedMasterTitleMatchFact> titleFacts;
        lock (_gate)
        {
            var owner = _data.Accounts.Single(account => account.Id == ownerId);
            players = [owner];
            for (var index = 1; index < 18; index++)
            {
                var username = $"{AcceptancePlayerPrefix}{index:00}";
                var account = _data.Accounts.FirstOrDefault(item =>
                    item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (account is null)
                {
                    account = CreateAccount(username, Convert.ToHexString(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)), "player");
                    _data.Accounts.Add(account);
                }
                account.Disabled = false;
                account.Deleted = false;
                players.Add(account);
            }

            var season = RequireOperationsConfig().Season;
            var placementMatches = _data.RankedConfig!.PlacementMatches;
            for (var index = 0; index < players.Count; index++)
            {
                var account = players[index];
                var faction = RankedFactionIds[index % RankedFactionIds.Length];
                var factionConfig = FactionFor(faction);
                var tierIndex = index == 0 ? factionConfig.Tiers.Count - 1 : index % factionConfig.Tiers.Count;
                var minimum = factionConfig.Tiers[tierIndex].Minimum;
                var next = tierIndex + 1 < factionConfig.Tiers.Count
                    ? factionConfig.Tiers[tierIndex + 1].Minimum : minimum + Math.Max(10_000, minimum / 8);
                var value = minimum + Math.Max(1, (next - minimum) * (index % 4 + 1) / 5);
                var profile = _data.RankedProfiles.FirstOrDefault(item => item.AccountId == account.Id
                    && item.SeasonId == season.Id);
                if (profile is null)
                {
                    profile = new RankedProfileRow { AccountId = account.Id, SeasonId = season.Id };
                    _data.RankedProfiles.Add(profile);
                }
                profile.Faction = faction;
                profile.SevenValue = value;
                profile.HiddenRating = 1350 + index * 23;
                profile.PlacementPlayed = placementMatches;
                profile.PlacementWins = Math.Clamp(2 + index % Math.Max(1, placementMatches), 0,
                    placementMatches);
                profile.Wins = 8 + index * 2;
                profile.Losses = 4 + index;
                profile.WinStreak = index % 6;
                profile.LossStreak = index % 4 == 0 ? 1 : 0;
                profile.HighestFloor = minimum;
                profile.ReachedHighestTier = tierIndex == factionConfig.Tiers.Count - 1;

                if (index < 9 && !_data.RankedProfileHistory.Any(item =>
                        item.AccountId == account.Id && item.SeasonId == AcceptanceHistorySeason))
                {
                    _data.RankedProfileHistory.Add(new RankedProfileHistoryRow
                    {
                        AccountId = account.Id,
                        SeasonId = AcceptanceHistorySeason,
                        UsernameSnapshot = PublicUsername(account),
                        Faction = faction,
                        SevenValue = Math.Max(minimum, value - Math.Max(1, value / 12)),
                        PlacementPlayed = placementMatches,
                        PlacementWins = profile.PlacementWins,
                        Wins = Math.Max(5, profile.Wins - 3),
                        Losses = profile.Losses,
                        WinStreak = Math.Max(1, profile.WinStreak),
                        SeasonName = "验收用上赛季",
                        Tier = factionConfig.Tiers[tierIndex].Name,
                        Titles = [index % 3 == 0 ? factionConfig.FirstTitle : factionConfig.TopFiveTitle],
                        FinalizedSeasonAwards = true,
                        ArchivedAt = DateTimeOffset.UtcNow.AddDays(-35 - index),
                    });
                }
            }

            // One deterministic daily batch keeps repeated starts idempotent while still
            // refreshing the rolling 7/30-day acceptance windows on later dates.
            var now = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(12), TimeSpan.Zero);
            var fixtureDate = now.ToString("yyyyMMdd");
            _testRunAcceptanceRankedMatches.Clear();
            titleFacts = [];
            foreach (var record in _data.RankedMasterRecords)
                record.TitleFacts.RemoveAll(item => item.MatchId.StartsWith(
                    "testrun-acceptance-", StringComparison.OrdinalIgnoreCase));
            _data.RankedMasterRecords.RemoveAll(item => item.SeasonId == RankedMasterTitleFactSeason
                && item.TitleFacts.Count == 0 && item.Games == 0 && item.Wins == 0);
            for (var championIndex = 0; championIndex < Math.Min(3, masterIds.Length); championIndex++)
            {
                var champion = players[championIndex];
                var championMaster = masterIds[championIndex];
                for (var game = 0; game < 24; game++)
                {
                    var opponent = players[3 + game % (players.Count - 3)];
                    var opponentMaster = masterIds[(championIndex + 1 + game) % masterIds.Length];
                    var ended = now.AddDays(-(game % 6)).AddMinutes(-(championIndex * 100 + game * 4));
                    var started = ended.AddMinutes(-(7 + game % 6));
                    var winner = game % 5 is 0 or 1 ? 1 : 0;
                    var first = game % 2;
                    var matchId = $"testrun-acceptance-{fixtureDate}-{championIndex + 1}-{game + 1:00}";
                    var match = new L12RankingMatch(matchId, PublicUsername(champion),
                        PublicUsername(opponent), started.ToString("O"), ended.ToString("O"), winner,
                        MasterNameForFixture(championMaster), MasterNameForFixture(opponentMaster), first,
                        championMaster, opponentMaster, champion.Id, opponent.Id);
                    _testRunAcceptanceRankedMatches.Add(match);
                    titleFacts.Add(new L12RankedMasterTitleMatchFact(matchId, champion.Id, opponent.Id,
                        championMaster, opponentMaster, winner, 7 + game % 4, ended, "completed", true));
                }
            }
            for (var game = 0; game < 36; game++)
            {
                var firstAccount = players[(game + 5) % players.Count];
                var secondAccount = players[(game * 3 + 7) % players.Count];
                if (firstAccount.Id == secondAccount.Id)
                    secondAccount = players[(game * 3 + 8) % players.Count];
                var firstMaster = masterIds[game % masterIds.Length];
                var secondMaster = masterIds[(game * 5 + 2) % masterIds.Length];
                var ended = now.AddDays(-(game % 26)).AddMinutes(-(game * 9));
                var started = ended.AddMinutes(-(6 + game % 8));
                _testRunAcceptanceRankedMatches.Add(new L12RankingMatch(
                    $"testrun-acceptance-{fixtureDate}-varied-{game + 1:00}", PublicUsername(firstAccount),
                    PublicUsername(secondAccount), started.ToString("O"), ended.ToString("O"), game % 2,
                    MasterNameForFixture(firstMaster), MasterNameForFixture(secondMaster), (game + 1) % 2,
                    firstMaster, secondMaster, firstAccount.Id, secondAccount.Id));
            }

            var fixturePlayerIds = players.Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _data.RankedMasterRecords.RemoveAll(item => item.SeasonId == season.Id
                && fixturePlayerIds.Contains(item.AccountId));
            foreach (var group in _testRunAcceptanceRankedMatches.SelectMany(match => new[]
                         {
                             (AccountId: match.AccountId0!, MasterId: match.MasterId0!, Won: match.Winner == 0),
                             (AccountId: match.AccountId1!, MasterId: match.MasterId1!, Won: match.Winner == 1),
                         }).GroupBy(item => (item.AccountId, item.MasterId)))
            {
                var record = _data.RankedMasterRecords.FirstOrDefault(item =>
                    item.AccountId == group.Key.AccountId && item.SeasonId == season.Id
                    && item.MasterId.Equals(group.Key.MasterId, StringComparison.OrdinalIgnoreCase));
                if (record is null)
                {
                    record = new RankedMasterRecordRow
                    {
                        AccountId = group.Key.AccountId,
                        SeasonId = season.Id,
                        MasterId = group.Key.MasterId,
                    };
                    _data.RankedMasterRecords.Add(record);
                }
                record.Games = group.Count();
                record.Wins = group.Count(item => item.Won);
            }
            Save();
        }

        ImportRankedMasterTitleFacts(titleFacts);
        lock (_gate)
        {
            for (var index = 0; index < Math.Min(3, players.Count); index++)
            {
                var profile = _data.RankedProfiles.Single(item => item.AccountId == players[index].Id
                    && item.SeasonId == RequireOperationsConfig().Season.Id);
                profile.SelectedMasterTitle = MasterTitle(masterIds[index]);
            }
            Save();
            return (players.Count, _testRunAcceptanceRankedMatches.Count,
                _testRunAcceptanceRankedMatches.SelectMany(item => new[] { item.MasterId0, item.MasterId1 })
                    .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                _data.RankedProfileHistory.Count(item => item.SeasonId == AcceptanceHistorySeason
                    && item.FinalizedSeasonAwards));
        }

        string MasterNameForFixture(string masterId)
            => _officialCards.TryGetValue(masterId, out var card) ? card.NameZh : masterId;
    }

    private static L12PlayerStatLine EmptyStat() => new(0, 0, 0, 0, 0, 0, 0, 0);

    private static L12PlayerStatLine AddStat(L12PlayerStatLine source, int playerIndex,
        int? winner, int firstPlayer)
        => new(source.Games + 1,
            source.Wins + (winner == playerIndex ? 1 : 0),
            source.Losses + (winner is 0 or 1 && winner != playerIndex ? 1 : 0),
            source.Draws + (winner is not (0 or 1) ? 1 : 0),
            source.FirstGames + (firstPlayer == playerIndex ? 1 : 0),
            source.FirstWins + (firstPlayer == playerIndex && winner == playerIndex ? 1 : 0),
            source.SecondGames + (firstPlayer is 0 or 1 && firstPlayer != playerIndex ? 1 : 0),
            source.SecondWins + (firstPlayer is 0 or 1 && firstPlayer != playerIndex
                && winner == playerIndex ? 1 : 0));

    private static L12PresetDeckDefinition FixtureDeck(L12PresetDeckDefinition source, string name)
        => new()
        {
            Name = name,
            MasterId = source.MasterId,
            CardIds = source.CardIds.ToList(),
            MoraleIds = source.MoraleIds.ToList(),
            SpecialIds = source.SpecialIds.ToList(),
        };
}
