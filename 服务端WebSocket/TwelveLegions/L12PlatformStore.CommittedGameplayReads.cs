using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TwelveLegions.Server;

// Internal projection, not an authority, authorization cache, or new wire contract.
internal sealed record L12CommittedGamePlatformView(long Revision, bool Available,
    IReadOnlyList<L12RankedBattleIdentityView> Badges,
    IReadOnlyDictionary<string, L12MatchDrawRequestClientView?> DrawRequests,
    IReadOnlyDictionary<string, L12RankedSettlementView?> Settlements);

public sealed partial class L12PlatformStore
{
    private sealed record CommittedDrawRequest(string RequesterId, string ResponderId,
        L12MatchDrawRequestClientView View);

    private sealed record CommittedGameplayReads(bool Available, L12OperationsPolicySnapshot? Policy,
        string SeasonDisplayName,
        FrozenDictionary<string, string> ResponseModes,
        FrozenDictionary<string, CommittedDrawRequest> DrawRequests,
        FrozenSet<string> OpenDrawRequestMatchIds,
        FrozenDictionary<(string MatchId, string AccountId), L12RankedSettlementView> Settlements);

    private CommittedGameplayReads PrepareCommittedGameplayReads(DataFile data)
    {
        StorageFailureInjector?.Invoke("before-gameplay-read-index-build");
        // Loading legacy state precedes EnsureOperations/EnsureRanked. Do not synthesize
        // configuration, or reject otherwise recoverable historical settlements at this point.
        if (data.OperationsConfig is not
            {
                Season: not null, DisasterPool: { CardIds: not null }, CardRestrictions: not null,
                DefaultPresetDeckIds: not null, DefaultRoomConfig: not null, MatchModes: not null,
                FeatureFlags: not null, Maintenance: not null, ImmediateMaintenance: not null,
                Announcements: not null,
            } operations || data.RankedConfig is not { MasterTitles: not null }
            || data.RankedSeasonResetRepairs is null
            || data.RankedSeasonResetRepairs.Any(row => row.TransitionMatchIds is null)
            || data.SeasonDefinitions is null || data.SeasonArchives is null)
        {
            StorageFailureInjector?.Invoke("after-gameplay-read-index-build");
            return new(false, null, "当前赛季", FrozenDictionary<string, string>.Empty,
                FrozenDictionary<string, CommittedDrawRequest>.Empty,
                FrozenSet<string>.Empty,
                FrozenDictionary<(string, string), L12RankedSettlementView>.Empty);
        }
        var policy = FreezeOperationsPolicy(ToPolicySnapshot(operations));
        var seasonName = HistoricalSeasonDisplayName(data,
            policy.Season.Id, policy.Season.Name, fallback: "当前赛季");
        var draws = new Dictionary<string, CommittedDrawRequest>(StringComparer.Ordinal);
        var openDrawRequestMatchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in data.MatchDrawRequests)
        {
            draws[row.MatchId] = new(row.RequesterId, row.ResponderId,
                new(row.Id, row.MatchId, row.RequesterName, row.ResponderName, row.Reason,
                    row.Status, false, false, row.RequestedAt, row.ExpiresAt, row.RespondedAt));
            if (row.Status is "pending" or "accepting") openDrawRequestMatchIds.Add(row.MatchId);
        }

        // One linear preparation, not one full integrity-history scan per settlement/read.
        var held = data.RankedHeldRewards.Select(row => row.MatchId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waived = data.RankedSeasonResetRepairs.SelectMany(row => row.TransitionMatchIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var revoked = data.RankedIntegrityDecisions.Where(row => row.Disposition == "revoked")
            .Select(row => row.RevokesDecisionId).Where(id => id is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var personalExclusions = ProjectRankedPersonalRewardExclusions(data);
        foreach (var decision in data.RankedIntegrityDecisions.OrderByDescending(row => row.Revision))
        {
            if (revoked.Contains(decision.Id) || decision.Scope != "match") continue;
            foreach (var matchId in decision.MatchIds) decisions.TryAdd(matchId, decision.Disposition);
        }
        var factions = data.RankedConfig.Factions.ToDictionary(row => row.Id, row => row.Name,
            StringComparer.Ordinal);
        var previous = Volatile.Read(ref _committedSessionActivity).GameplayReads;
        var oldSettlements = previous?.Available == true ? previous.Settlements
            : FrozenDictionary<(string, string), L12RankedSettlementView>.Empty;
        Dictionary<(string, string), L12RankedSettlementView>? changedSettlements = null;
        var seen = new HashSet<(string, string)>();
        foreach (var row in data.RankedSettlements)
        {
            var key = (row.MatchId, row.AccountId);
            if (!seen.Add(key)) continue; // Preserve the original FirstOrDefault rule.
            if (!factions.TryGetValue(row.Faction, out var faction))
                throw new InvalidDataException("已提交结算派系无法投影");
            decisions.TryGetValue(row.MatchId, out var disposition);
            var reward = ProjectRankedRewardStatus(waived.Contains(row.MatchId),
                held.Contains(row.MatchId), personalExclusions.Contains(key) ? "confirmed" : disposition);
            if (oldSettlements.TryGetValue(key, out var old)
                && UnchangedCommittedSettlement(row, faction, reward, old)) continue;
            changedSettlements ??= new(oldSettlements);
            changedSettlements[key] = ProjectRankedSettlement(row, faction, reward,
                row.Components.ToImmutableArray());
        }
        FrozenDictionary<(string, string), L12RankedSettlementView> settlements;
        if (changedSettlements is null && seen.Count == oldSettlements.Count)
            settlements = oldSettlements;
        else
        {
            changedSettlements ??= new(oldSettlements);
            foreach (var key in changedSettlements.Keys.Where(key => !seen.Contains(key)).ToArray())
                changedSettlements.Remove(key);
            settlements = changedSettlements.ToFrozenDictionary();
        }
        var modes = data.Accounts.Where(row => !row.Disabled && !row.Deleted)
            .ToFrozenDictionary(row => row.Id, row => L12GameEngine.IsValidResponseMode(row.ResponseMode)
                ? row.ResponseMode : L12GameEngine.DefaultResponseMode, StringComparer.Ordinal);
        var prepared = new CommittedGameplayReads(true, policy, seasonName, modes,
            draws.ToFrozenDictionary(StringComparer.Ordinal),
            openDrawRequestMatchIds.ToFrozenSet(StringComparer.Ordinal), settlements);
        StorageFailureInjector?.Invoke("after-gameplay-read-index-build");
        return prepared;
    }

    private static bool UnchangedCommittedSettlement(RankedSettlementRow row, string faction,
        string reward, L12RankedSettlementView previous)
        => previous.Faction == faction && previous.RewardStatus == reward
            && previous.MatchId == row.MatchId && previous.AccountId == row.AccountId
            && previous.Outcome == row.Outcome && previous.Won == row.Won
            && previous.Placement == row.Placement && previous.PlacementPlayed == row.PlacementPlayed
            && previous.PlacementRequired == row.PlacementRequired && previous.Before == row.Before
            && previous.After == row.After && previous.Delta == row.Delta
            && previous.TierBefore == row.TierBefore && previous.TierAfter == row.TierAfter
            && previous.SettledAt == row.SettledAt && previous.Components.SequenceEqual(row.Components);

    private static L12OperationsPolicySnapshot FreezeOperationsPolicy(L12OperationsPolicySnapshot policy)
        => policy with
        {
            DisasterCardIds = policy.DisasterCardIds.ToImmutableArray(),
            CardRestrictions = policy.CardRestrictions.ToImmutableArray(),
            DefaultPresetDeckIds = policy.DefaultPresetDeckIds.ToImmutableArray(),
            MatchModes = policy.MatchModes.ToImmutableArray(),
            FeatureFlags = policy.FeatureFlags.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            Announcements = (policy.Announcements ?? []).ToImmutableArray(),
        };

    private CommittedGameplayReads ReadCommittedGameplayReads()
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available || committed.GameplayReads?.Available != true)
            throw new L12PlatformStorageUnavailableException("平台已提交对局读取状态不可恢复");
        return committed.GameplayReads;
    }

    private static L12MatchDrawRequestClientView? ProjectCommittedDrawRequest(
        CommittedGameplayReads reads, string matchId, string viewerAccountId, DateTimeOffset now)
    {
        if (!reads.DrawRequests.TryGetValue(matchId, out var row)
            || viewerAccountId != row.RequesterId && viewerAccountId != row.ResponderId) return null;
        var view = row.View;
        var expired = view.Status is "pending" or "accepting" && view.ExpiresAt <= now;
        var status = expired ? "expired" : view.Status;
        return view with
        {
            Status = status,
            ViewerIsRequester = viewerAccountId == row.RequesterId,
            ViewerCanRespond = viewerAccountId == row.ResponderId && status == "pending",
            RespondedAt = expired ? view.ExpiresAt : view.RespondedAt,
        };
    }

    internal L12CommittedGamePlatformView ReadCommittedGamePlatformView(string matchId,
        IReadOnlyList<(string AccountId, int PlayerIndex)> viewers, DateTimeOffset now,
        bool includeSettlements)
    {
        // Exactly one generation for both players, badges, requests and rewards in this broadcast.
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available || committed.GameplayReads?.Available != true
            || committed.RankedIdentity is not { Available: true, Config: not null, MasterTitleIndex: not null })
            return new(committed.Revision, false, [],
                FrozenDictionary<string, L12MatchDrawRequestClientView?>.Empty,
                FrozenDictionary<string, L12RankedSettlementView?>.Empty);
        var reads = committed.GameplayReads;
        var badges = new List<L12RankedBattleIdentityView>(viewers.Count);
        var draws = new Dictionary<string, L12MatchDrawRequestClientView?>(StringComparer.Ordinal);
        var settlements = new Dictionary<string, L12RankedSettlementView?>(StringComparer.Ordinal);
        foreach (var viewer in viewers)
        {
            try { badges.Add(ProjectCommittedRankedBattleIdentity(committed, viewer.AccountId,
                viewer.PlayerIndex, now)); }
            catch (L12PlatformStorageUnavailableException)
            {
                return new(committed.Revision, false, [],
                    FrozenDictionary<string, L12MatchDrawRequestClientView?>.Empty,
                    FrozenDictionary<string, L12RankedSettlementView?>.Empty);
            }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException) { }
            draws[viewer.AccountId] = ProjectCommittedDrawRequest(reads, matchId, viewer.AccountId, now);
            if (includeSettlements)
                settlements[viewer.AccountId] = reads.Settlements.GetValueOrDefault((matchId, viewer.AccountId));
        }
        return new(committed.Revision, true, badges.ToImmutableArray(),
            draws.ToFrozenDictionary(StringComparer.Ordinal), settlements.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
