using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TwelveLegions.Server;

public sealed record L12RankedMasterTitleMatchFact(
    string MatchId,
    string FirstAccountId,
    string SecondAccountId,
    string FirstMasterId,
    string SecondMasterId,
    int? Winner,
    int FinalRound,
    DateTimeOffset EndedAt,
    string ConclusionKind,
    bool IsRanked);

public sealed partial class L12PlatformStore
{
    internal static readonly TimeSpan RankedMasterTitleWindow = TimeSpan.FromHours(720);
    private const string RankedMasterTitleFactSeason = "$rolling-master-title-720h-v1";

    private sealed record RankedMasterTitleAppearance(
        L12RankedMasterTitleMatchFact Match,
        string AccountId,
        string OpponentAccountId,
        string MasterId,
        bool Won);

    private sealed record RankedMasterTitleCandidate(
        string AccountId,
        string MasterId,
        int Games,
        int Wins,
        double Score);

    private sealed record RankedMasterTitleChampionProjection(
        string AccountId,
        string MasterId,
        int Games,
        int Wins);

    private sealed record RankedMasterTitleFactIndex(
        FrozenDictionary<string, ImmutableArray<L12RankedMasterTitleMatchFact>> FactsByMaster,
        FrozenDictionary<string, FrozenSet<string>> MasterIdsByAccount);

    public int ImportRankedMasterTitleFacts(IReadOnlyList<L12RankedMasterTitleMatchFact> source)
    {
        lock (_gate)
        {
            var imported = 0;
            foreach (var fact in source.OrderBy(item => item.EndedAt).ThenBy(item => item.MatchId,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (ImportRankedMasterTitleFactLocked(fact)) imported++;
            }
            if (imported > 0) Save();
            return imported;
        }
    }

    internal IReadOnlyList<L12RankedMasterChampionView> RankedMasterChampionsAt(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            return ProjectCurrentMasterChampions(utcNow).Values.Select(record =>
                {
                    var currentSeason = RequireOperationsConfig().Season.Id;
                    var profile = _data.RankedProfiles.FirstOrDefault(row => row.AccountId == record.AccountId
                        && SeasonIdsEqual(row.SeasonId, currentSeason));
                    var masterName = MasterName(record.MasterId);
                    return new L12RankedMasterChampionView(record.MasterId, masterName,
                        AccountName(record.AccountId), MasterTitle(record.MasterId), profile?.SevenValue ?? 0,
                        $"七曜值 {(profile?.SevenValue ?? 0):N0}", record.Games, record.Wins);
                })
                .OrderBy(item => item.MasterName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private bool ImportRankedMasterTitleFactLocked(L12RankedMasterTitleMatchFact source)
    {
        var fact = NormalizeMasterTitleFact(source);
        foreach (var record in _data.RankedMasterRecords)
        {
            record.TitleFacts ??= [];
            var existingIndex = record.TitleFacts.FindIndex(item =>
                item.MatchId.Equals(fact.MatchId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0) continue;
            var existing = record.TitleFacts[existingIndex];
            if (!EquivalentMasterTitleFact(existing, fact))
            {
                // 旧 outbox 没有 FinalRound；启动回填可用权威终局状态补齐，但不得改写其他事实。
                if (existing.FinalRound == 0 && fact.FinalRound > 0
                    && EquivalentMasterTitleFact(existing with { FinalRound = fact.FinalRound }, fact))
                {
                    record.TitleFacts[existingIndex] = fact;
                    return true;
                }
                throw new InvalidOperationException("同一排位 matchId 的最强主宰事实冲突");
            }
            return false;
        }

        var bucketMasterId = string.IsNullOrWhiteSpace(fact.FirstMasterId) ? "$unknown" : fact.FirstMasterId;
        var bucket = _data.RankedMasterRecords.FirstOrDefault(item =>
            item.AccountId.Equals(fact.FirstAccountId, StringComparison.OrdinalIgnoreCase)
            && item.SeasonId == RankedMasterTitleFactSeason
            && item.MasterId.Equals(bucketMasterId, StringComparison.OrdinalIgnoreCase));
        if (bucket is null)
        {
            bucket = new RankedMasterRecordRow
            {
                AccountId = fact.FirstAccountId,
                SeasonId = RankedMasterTitleFactSeason,
                MasterId = bucketMasterId,
            };
            _data.RankedMasterRecords.Add(bucket);
        }
        bucket.TitleFacts ??= [];
        bucket.TitleFacts.Add(fact);
        return true;
    }

    private Dictionary<string, RankedMasterRecordRow> ProjectCurrentMasterChampions(DateTimeOffset utcNow)
    {
        var selectable = SelectableMasterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var facts = _data.RankedMasterRecords.SelectMany(item => item.TitleFacts ?? []).ToArray();
        var index = IndexRankedMasterTitleFacts(facts);
        var excluded = ProjectRankedMasterTitleExclusions(_data, facts);
        var inactive = _data.Accounts.Where(item => item.Disabled || item.Deleted).Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eligible = _data.Accounts.Where(item => !item.Disabled && !item.Deleted).Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var projected = ProjectRankedMasterTitleChampions(index, index.FactsByMaster.Keys,
            excluded, inactive, eligible, selectable, utcNow);
        var seasonId = RequireOperationsConfig().Season.Id;
        return projected.ToDictionary(item => item.Key, item => new RankedMasterRecordRow
        {
            AccountId = item.Value.AccountId,
            SeasonId = seasonId,
            MasterId = item.Value.MasterId,
            Games = item.Value.Games,
            Wins = item.Value.Wins,
        }, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, RankedMasterTitleChampionProjection>
        ProjectRankedMasterTitleChampions(
            RankedMasterTitleFactIndex index,
            IEnumerable<string> requestedMasterIds,
            IReadOnlySet<string> excludedMatchIds,
            IReadOnlySet<string> knownInactiveAccountIds,
            IReadOnlySet<string> championEligibleAccountIds,
            IReadOnlySet<string> selectableMasterIds,
            DateTimeOffset utcNow)
    {
        var now = utcNow.ToUniversalTime();
        var cutoff = now - RankedMasterTitleWindow;
        var result = new Dictionary<string, RankedMasterTitleChampionProjection>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var masterId in requestedMasterIds.Where(selectableMasterIds.Contains)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!index.FactsByMaster.TryGetValue(masterId, out var indexedFacts)) continue;
            var masterMatches = indexedFacts
                .Where(item => IsEligibleMasterTitleMatch(item, cutoff, now))
                .Where(item => !excludedMatchIds.Contains(item.MatchId))
                .Where(item => !knownInactiveAccountIds.Contains(item.FirstAccountId)
                    && !knownInactiveAccountIds.Contains(item.SecondAccountId))
                .Where(item => selectableMasterIds.Contains(item.FirstMasterId)
                    && selectableMasterIds.Contains(item.SecondMasterId))
                .OrderBy(item => item.MatchId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            // 稀疏门槛按全服原始有效对局数判断；镜像两席仍只占一个 matchId。
            var allServerGames = masterMatches.Select(item => item.MatchId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var personalMinimum = allServerGames < 200 ? 20 : 40;
            var appearances = MasterAppearances(masterMatches, masterId).ToArray();
            var candidates = new List<RankedMasterTitleCandidate>();
            foreach (var group in appearances.GroupBy(item => item.AccountId,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!championEligibleAccountIds.Contains(group.Key))
                    continue;
                var personal = group.ToArray();
                if (personal.Length < personalMinimum) continue;
                var activeDays = personal.Select(item => item.Match.EndedAt.ToUniversalTime().AddHours(8).Date)
                    .Distinct().Count();
                if (activeDays < 5) continue;
                var opponents = personal.Select(item => item.OpponentAccountId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (opponents < 10) continue;

                // Leave-one-player-out 会排除候选人参与的整局，包括镜像对手那一席。
                var baselineMatches = masterMatches.Where(item =>
                    !item.FirstAccountId.Equals(group.Key, StringComparison.OrdinalIgnoreCase)
                    && !item.SecondAccountId.Equals(group.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                var baseline = MasterAppearances(baselineMatches, masterId).ToArray();
                var baselineWins = baseline.Count(item => item.Won);
                var baselineRate = (baselineWins + 20d) / (baseline.Length + 40d);
                var personalWins = personal.Count(item => item.Won);
                var score = (personalWins + 15d * baselineRate) / (personal.Length + 15d);
                candidates.Add(new RankedMasterTitleCandidate(group.Key, masterId,
                    personal.Length, personalWins, score));
            }

            var champion = candidates.OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Games).ThenByDescending(item => item.Wins)
                .ThenBy(item => item.AccountId, StringComparer.Ordinal).FirstOrDefault();
            if (champion is null) continue;
            result[masterId] = new RankedMasterTitleChampionProjection(champion.AccountId,
                champion.MasterId, champion.Games, champion.Wins);
        }
        return result;
    }

    private static RankedMasterTitleFactIndex IndexRankedMasterTitleFacts(
        IEnumerable<L12RankedMasterTitleMatchFact> facts)
    {
        var materialized = facts.ToArray();
        var byMaster = materialized.SelectMany(fact =>
                new[] { fact.FirstMasterId, fact.SecondMasterId }
                    .Where(masterId => !string.IsNullOrWhiteSpace(masterId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(masterId => (MasterId: masterId, Fact: fact)))
            .GroupBy(item => item.MasterId, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key,
                group => group.Select(item => item.Fact).ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);
        var byAccount = materialized.SelectMany(fact => new[]
            {
                (AccountId: fact.FirstAccountId, MasterId: fact.FirstMasterId),
                (AccountId: fact.SecondAccountId, MasterId: fact.SecondMasterId),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.AccountId)
                && !string.IsNullOrWhiteSpace(item.MasterId))
            .GroupBy(item => item.AccountId, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key,
                group => group.Select(item => item.MasterId)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
        return new RankedMasterTitleFactIndex(byMaster, byAccount);
    }

    private static FrozenSet<string> ProjectRankedMasterTitleExclusions(DataFile data,
        IEnumerable<L12RankedMasterTitleMatchFact> facts)
    {
        var waived = data.RankedSeasonResetRepairs.SelectMany(row => row.TransitionMatchIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var revoked = data.RankedIntegrityDecisions.Where(row => row.Disposition == "revoked"
                && !string.IsNullOrWhiteSpace(row.RevokesDecisionId))
            .Select(row => row.RevokesDecisionId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeDecisions = data.RankedIntegrityDecisions.Where(row => !revoked.Contains(row.Id))
            .ToArray();
        var held = data.RankedHeldRewards.Select(row => row.MatchId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(waived, StringComparer.OrdinalIgnoreCase);
        foreach (var matchId in facts.Select(row => row.MatchId)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (excluded.Contains(matchId)) continue;
            var latest = activeDecisions.Where(row => row.MatchIds.Contains(matchId,
                    StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(row => row.Revision).FirstOrDefault();
            var isHeld = held.Contains(matchId);
            if (latest is null ? isHeld : latest.Disposition is "confirmed" or "system-error"
                || latest.Disposition == "review" && isHeld)
                excluded.Add(matchId);
        }
        return excluded.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<RankedMasterTitleAppearance> MasterAppearances(
        IEnumerable<L12RankedMasterTitleMatchFact> matches, string masterId)
    {
        foreach (var match in matches)
        {
            if (match.FirstMasterId.Equals(masterId, StringComparison.OrdinalIgnoreCase))
                yield return new RankedMasterTitleAppearance(match, match.FirstAccountId,
                    match.SecondAccountId, masterId, match.Winner == 0);
            if (match.SecondMasterId.Equals(masterId, StringComparison.OrdinalIgnoreCase))
                yield return new RankedMasterTitleAppearance(match, match.SecondAccountId,
                    match.FirstAccountId, masterId, match.Winner == 1);
        }
    }

    private static bool IsEligibleMasterTitleMatch(L12RankedMasterTitleMatchFact item,
        DateTimeOffset cutoff, DateTimeOffset now)
    {
        var conclusion = item.ConclusionKind.Trim();
        return item.IsRanked && item.Winner is 0 or 1 && item.FinalRound >= 6
            && !string.IsNullOrWhiteSpace(item.MatchId)
            && !string.IsNullOrWhiteSpace(item.FirstAccountId)
            && !string.IsNullOrWhiteSpace(item.SecondAccountId)
            && !item.FirstAccountId.Equals(item.SecondAccountId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.FirstMasterId)
            && !string.IsNullOrWhiteSpace(item.SecondMasterId)
            && item.EndedAt.ToUniversalTime() >= cutoff && item.EndedAt.ToUniversalTime() <= now
            && !string.IsNullOrWhiteSpace(conclusion)
            && !conclusion.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            && !conclusion.Contains("disconnect", StringComparison.OrdinalIgnoreCase);
    }

    private static L12RankedMasterTitleMatchFact NormalizeMasterTitleFact(
        L12RankedMasterTitleMatchFact source)
    {
        if (string.IsNullOrWhiteSpace(source.MatchId))
            throw new ArgumentException("最强主宰事实缺少 matchId", nameof(source));
        if (string.IsNullOrWhiteSpace(source.FirstAccountId)
            || string.IsNullOrWhiteSpace(source.SecondAccountId))
            throw new ArgumentException("最强主宰事实缺少双席账号", nameof(source));
        if (source.Winner is not null and not (0 or 1))
            throw new ArgumentException("最强主宰事实胜者席位无效", nameof(source));
        if (source.FinalRound < 0)
            throw new ArgumentException("最强主宰事实回合数不能为负", nameof(source));
        return source with
        {
            MatchId = source.MatchId.Trim(),
            FirstAccountId = source.FirstAccountId.Trim(),
            SecondAccountId = source.SecondAccountId.Trim(),
            FirstMasterId = source.FirstMasterId?.Trim() ?? string.Empty,
            SecondMasterId = source.SecondMasterId?.Trim() ?? string.Empty,
            EndedAt = source.EndedAt.ToUniversalTime(),
            ConclusionKind = string.IsNullOrWhiteSpace(source.ConclusionKind)
                ? "unknown" : source.ConclusionKind.Trim().ToLowerInvariant(),
        };
    }

    private static bool EquivalentMasterTitleFact(L12RankedMasterTitleMatchFact first,
        L12RankedMasterTitleMatchFact second)
        => first.MatchId.Equals(second.MatchId, StringComparison.OrdinalIgnoreCase)
           && first.FirstAccountId.Equals(second.FirstAccountId, StringComparison.OrdinalIgnoreCase)
           && first.SecondAccountId.Equals(second.SecondAccountId, StringComparison.OrdinalIgnoreCase)
           && first.FirstMasterId.Equals(second.FirstMasterId, StringComparison.OrdinalIgnoreCase)
           && first.SecondMasterId.Equals(second.SecondMasterId, StringComparison.OrdinalIgnoreCase)
           && first.Winner == second.Winner && first.FinalRound == second.FinalRound
           && first.EndedAt.ToUniversalTime() == second.EndedAt.ToUniversalTime()
           && first.ConclusionKind.Equals(second.ConclusionKind, StringComparison.OrdinalIgnoreCase)
           && first.IsRanked == second.IsRanked;
}
