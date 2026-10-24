using System.Collections.Frozen;

namespace TwelveLegions.Server;

internal sealed record L12CommittedStatisticsExclusions(
    IReadOnlyCollection<string> MatchIds, IReadOnlyCollection<string> AccountIds)
{
    internal bool Available { get; init; } = true;
}

public sealed partial class L12PlatformStore
{
    private static L12CommittedStatisticsExclusions PrepareCommittedStatisticsExclusions(DataFile data)
    {
        // Legacy nullable parts are repaired by the existing Ensure path, not by a read projection.
        // Allow their commit without advertising unknown exclusion facts as an empty valid set.
        if (data.RankedSeasonResetRepairs is null || data.RankedHeldRewards is null
            || data.RankedIntegrityDecisions is null || data.RankedIntegrityAudits is null || data.Accounts is null
            || data.RankedSeasonResetRepairs.Any(row => row.TransitionMatchIds is null)
            || data.RankedIntegrityDecisions.Any(row => row.MatchIds is null))
            return new([], []) { Available = false };
        var waived = data.RankedSeasonResetRepairs.SelectMany(row => row.TransitionMatchIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var held = data.RankedHeldRewards.Select(row => row.MatchId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var revoked = data.RankedIntegrityDecisions.Where(row => row.Disposition == "revoked")
            .Select(row => row.RevokesDecisionId).Where(id => id is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latest = new Dictionary<string, RankedIntegrityDecisionRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in data.RankedIntegrityDecisions.Where(row => row.Scope == "match" && !revoked.Contains(row.Id))
            .OrderByDescending(row => row.Revision))
            foreach (var matchId in row.MatchIds) latest.TryAdd(matchId, row);
        var matches = data.RankedIntegrityAudits.Select(row => row.MatchId)
            .Where(id => waived.Contains(id) || (latest.TryGetValue(id, out var decision)
                ? decision.Disposition is "confirmed" or "system-error" || decision.Disposition == "review" && held.Contains(id)
                : held.Contains(id)))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var accounts = data.Accounts.Where(row => row.Disabled || row.Deleted).Select(row => row.Id)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        return new(matches, accounts);
    }

    internal L12CommittedStatisticsExclusions ReadCommittedStatisticsExclusions()
    {
        if (Monitor.IsEntered(_gate))
        {
            var staged = PrepareCommittedStatisticsExclusions(_data);
            if (!staged.Available)
                throw new L12PlatformStorageUnavailableException("平台暂存统计口径不可恢复");
            return staged;
        }
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available || committed.StatisticsExclusions is not { Available: true })
            throw new L12PlatformStorageUnavailableException("平台已提交统计口径不可恢复");
        return committed.StatisticsExclusions;
    }
}
