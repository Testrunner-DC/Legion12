using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TwelveLegions.Server;

internal sealed record L12TournamentRoomAccess(
    string TournamentId,
    string TournamentCode,
    string MatchId,
    string RoomCode,
    bool CanPlay,
    bool CanSpectate,
    bool CanRefereeView);

public sealed partial class L12PlatformStore
{
    internal Action<string>? TournamentDeckPayloadReadObserver { get; set; }

    private sealed record CommittedTournamentAccount(bool Active, bool CanGloballyAccess);

    private sealed record CommittedTournamentLookup(
        int SourceOrder,
        string TournamentId,
        FrozenSet<string> MatchIds);

    private sealed record CommittedTournamentPlayer(
        string AccountId,
        string Username,
        string DeckName,
        string MasterId,
        NormalizedDeckPayload Payload,
        FrozenDictionary<string, string> AlternateArtSelections,
        string? PublicationId,
        int? PublicationVersion,
        string DeckHash);

    private sealed record CommittedTournamentPlayerPreparation(
        CommittedTournamentPlayer? Player,
        string? Failure);

    private sealed record CommittedTournamentAssignment(
        string TournamentId,
        string TournamentCode,
        string MatchId,
        string RoomCode,
        CommittedTournamentPlayer? PlayerA,
        CommittedTournamentPlayer? PlayerB,
        L12OperationsPolicySnapshot Policy,
        string DisasterMode,
        string RulesHash,
        L12RankedTimeControlConfig TimeControl,
        DateTimeOffset? Deadline,
        bool Paused,
        string? PauseReason,
        string? Failure);

    private sealed record CommittedRunningTournamentMatch(
        CommittedTournamentAssignment Assignment,
        string PlayerAAccountId,
        string PlayerBAccountId,
        FrozenSet<string> IneligibleParticipantIds,
        FrozenSet<string> StaffAccountIds,
        bool PublicSpectating);

    private sealed record CommittedTournamentReads(
        bool Available,
        FrozenDictionary<string, CommittedTournamentAccount> Accounts,
        FrozenDictionary<string, CommittedTournamentLookup> TournamentsById,
        FrozenDictionary<string, CommittedTournamentLookup> TournamentsByCode,
        FrozenDictionary<(string TournamentId, string MatchId), CommittedRunningTournamentMatch> RunningMatches,
        FrozenDictionary<string, CommittedTournamentAssignment> AssignmentsByRoom);

    private static readonly CommittedTournamentReads UnavailableTournamentReads = new(false,
        FrozenDictionary<string, CommittedTournamentAccount>.Empty,
        FrozenDictionary<string, CommittedTournamentLookup>.Empty,
        FrozenDictionary<string, CommittedTournamentLookup>.Empty,
        FrozenDictionary<(string, string), CommittedRunningTournamentMatch>.Empty,
        FrozenDictionary<string, CommittedTournamentAssignment>.Empty);

    private CommittedTournamentReads PrepareCommittedTournamentReads(DataFile data)
    {
        StorageFailureInjector?.Invoke("before-tournament-read-index-build");
        if (data.OperationsConfig is not
            {
                Season: not null, DisasterPool: { CardIds: not null }, CardRestrictions: not null,
                DefaultPresetDeckIds: not null, DefaultRoomConfig: not null, MatchModes: not null,
                FeatureFlags: not null, Maintenance: not null, ImmediateMaintenance: not null,
                Announcements: not null,
            })
        {
            StorageFailureInjector?.Invoke("after-tournament-read-index-build");
            return UnavailableTournamentReads;
        }

        // Preparation is a projection of the supplied commit candidate only. In particular, it
        // never reaches _data through the legacy deck or operations helpers.
        var basePolicy = FreezeOperationsPolicy(ToPolicySnapshot(data.OperationsConfig));
        var accountRows = new Dictionary<string, AccountRow>(StringComparer.Ordinal);
        foreach (var account in data.Accounts) accountRows.TryAdd(account.Id, account);
        var accounts = accountRows.Values.ToDictionary(account => account.Id,
            account => new CommittedTournamentAccount(!account.Disabled && !account.Deleted,
                L12Authorization.HasPermission(account.Role, L12Permission.TournamentsManage)
                || L12Authorization.HasPermission(account.Role, L12Permission.TournamentRulingsWrite)),
            StringComparer.Ordinal);

        var byId = new Dictionary<string, CommittedTournamentLookup>(StringComparer.Ordinal);
        var byCode = new Dictionary<string, CommittedTournamentLookup>(StringComparer.OrdinalIgnoreCase);
        var running = new Dictionary<(string, string), CommittedRunningTournamentMatch>();
        var byRoom = new Dictionary<string, CommittedTournamentAssignment>(StringComparer.OrdinalIgnoreCase);
        var playerCache = new Dictionary<(TournamentRow Tournament, string AccountId, string? ExpectedDeckHash),
            CommittedTournamentPlayerPreparation>();
        var payloadReadObserver = TournamentDeckPayloadReadObserver;

        foreach (var (tournament, tournamentOrder) in data.Tournaments.Select((row, index) => (row, index)))
        {
            var matchIds = new HashSet<string>(StringComparer.Ordinal);
            var tournamentPolicy = FreezeOperationsPolicy(basePolicy with
            {
                Version = tournament.Version,
                VersionId = $"tournament:{tournament.Id}:{tournament.Rules.Hash}",
                DisasterCardIds = tournament.Rules.DisasterCardIds.ToImmutableArray(),
                CardRestrictions = tournament.Rules.CardRestrictions.ToImmutableArray(),
                DefaultPresetDeckIds = ImmutableArray<string>.Empty,
                DefaultRoomConfig = new L12DefaultRoomConfig("tournament", "disabled", "request",
                    tournament.Rules.DisasterMode),
                MatchModes = ImmutableArray.Create(new L12MatchModeConfig("tournament", "赛事对局", true)),
                Maintenance = new L12MaintenanceConfig(false, string.Empty, null, null),
            });
            var staff = new[] { tournament.OrganizerAccountId }.Concat(tournament.RefereeAccountIds)
                .ToFrozenSet(StringComparer.Ordinal);
            var ineligible = tournament.Participants.Where(row => row.Removed || row.Dropped)
                .Select(row => row.AccountId).ToFrozenSet(StringComparer.Ordinal);

            foreach (var round in tournament.Rounds)
            foreach (var match in round.Matches)
            {
                // RequireMatch and the historical room resolver both use the first source item.
                // Keep the same winner for duplicate ids/codes instead of overwriting with the last.
                var firstMatchById = matchIds.Add(match.Id);
                var assignment = PrepareCommittedTournamentAssignment(data, accountRows, tournament,
                    match, tournamentPolicy, playerCache, payloadReadObserver);
                if (!string.IsNullOrWhiteSpace(assignment.RoomCode))
                    byRoom.TryAdd(assignment.RoomCode, assignment);
                if (!firstMatchById || tournament.Status != "running" || round.Status != "running"
                    || match.Status != "running"
                    || match.PlayerBAccountId is null) continue;
                running.TryAdd((tournament.Id, match.Id), new CommittedRunningTournamentMatch(assignment,
                    match.PlayerAAccountId, match.PlayerBAccountId, ineligible, staff,
                    tournament.Visibility == "public"));
            }

            var lookup = new CommittedTournamentLookup(tournamentOrder, tournament.Id,
                matchIds.ToFrozenSet(StringComparer.Ordinal));
            byId.TryAdd(tournament.Id, lookup);
            byCode.TryAdd(tournament.Code, lookup);
        }

        var prepared = new CommittedTournamentReads(true,
            accounts.ToFrozenDictionary(StringComparer.Ordinal),
            byId.ToFrozenDictionary(StringComparer.Ordinal),
            byCode.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            running.ToFrozenDictionary(), byRoom.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
        StorageFailureInjector?.Invoke("after-tournament-read-index-build");
        return prepared;
    }

    private static CommittedTournamentAssignment PrepareCommittedTournamentAssignment(DataFile data,
        IReadOnlyDictionary<string, AccountRow> accounts, TournamentRow tournament, TournamentMatchRow match,
        L12OperationsPolicySnapshot policy,
        Dictionary<(TournamentRow Tournament, string AccountId, string? ExpectedDeckHash),
            CommittedTournamentPlayerPreparation> playerCache,
        Action<string>? payloadReadObserver)
    {
        var timeControl = NormalizeRankedTimeControl(tournament.TimeControl);
        if (!tournament.UsesLegacyRoundClock && match.TimeExtensionMinutes > 0)
            timeControl = timeControl with
            {
                TotalTimeSeconds = Math.Min(7200,
                    timeControl.TotalTimeSeconds + match.TimeExtensionMinutes * 60),
            };

        var roomCode = match.RoomCode ?? string.Empty;
        CommittedTournamentPlayer? playerA = null;
        CommittedTournamentPlayer? playerB = null;
        string? failure = string.IsNullOrWhiteSpace(roomCode) ? "赛事房间绑定缺少房间编码" : null;
        if (failure is null)
        {
            var prepared = PrepareCommittedTournamentPlayer(data, accounts, tournament,
                match.PlayerAAccountId, match.PlayerADeckHash, playerCache, payloadReadObserver);
            playerA = prepared.Player;
            failure = prepared.Failure;
        }
        if (failure is null && match.PlayerBAccountId is null)
            failure = "赛事房间绑定缺少第二席玩家";
        if (failure is null)
        {
            var prepared = PrepareCommittedTournamentPlayer(data, accounts, tournament,
                match.PlayerBAccountId!, match.PlayerBDeckHash, playerCache, payloadReadObserver);
            playerB = prepared.Player;
            failure = prepared.Failure;
        }

        return new CommittedTournamentAssignment(tournament.Id, tournament.Code, match.Id,
            roomCode, playerA, playerB, policy, tournament.Rules.DisasterMode,
            tournament.Rules.Hash, timeControl, match.Deadline, match.Paused, match.PauseReason, failure);
    }

    private static CommittedTournamentPlayerPreparation PrepareCommittedTournamentPlayer(DataFile data,
        IReadOnlyDictionary<string, AccountRow> accounts, TournamentRow tournament, string accountId,
        string? expectedDeckHash,
        Dictionary<(TournamentRow Tournament, string AccountId, string? ExpectedDeckHash),
            CommittedTournamentPlayerPreparation> playerCache,
        Action<string>? payloadReadObserver)
    {
        var key = (tournament, accountId, expectedDeckHash);
        if (playerCache.TryGetValue(key, out var cached)) return cached;

        CommittedTournamentPlayerPreparation Complete(CommittedTournamentPlayer? player, string? failure)
        {
            var prepared = new CommittedTournamentPlayerPreparation(player, failure);
            playerCache.Add(key, prepared);
            return prepared;
        }

        var participant = tournament.Participants.FirstOrDefault(item => item.AccountId == accountId);
        if (participant is null) return Complete(null, "赛事房间玩家不在报名快照中");
        if (participant.Dropped || participant.Eliminated) return Complete(null, "该玩家已退赛或被淘汰");
        if (string.IsNullOrWhiteSpace(participant.Deck.MasterId))
            return Complete(null, "该报名使用旧版牌库快照，需在开赛前重新选择账号牌库");
        if (!string.Equals(participant.Deck.Hash, expectedDeckHash, StringComparison.Ordinal))
            return Complete(null, "桌次绑定的牌库快照与报名快照不一致");
        if (!accounts.TryGetValue(accountId, out var account)) return Complete(null, "参赛账号不存在");

        var payload = ReadExportDeckPayload(data, participant.Deck.MasterId, participant.Deck.PayloadHash,
            participant.Deck.CardIds, participant.Deck.MoraleIds, participant.Deck.SpecialIds);
        payloadReadObserver?.Invoke(payload.Hash);
        if (CountRecoveryCards(ReadRecoveryCardCounts(payload.MainJson)) == 0
            || CountRecoveryCards(ReadRecoveryCardCounts(payload.MoraleJson)) == 0)
            return Complete(null, "该报名使用旧版牌库快照，需在开赛前重新选择账号牌库");
        var player = new CommittedTournamentPlayer(participant.AccountId, PublicUsername(account),
            participant.Deck.Name, participant.Deck.MasterId, payload,
            participant.Deck.AlternateArtSelections.ToFrozenDictionary(StringComparer.Ordinal),
            participant.Deck.PublicationId, participant.Deck.PublicationVersion, participant.Deck.Hash);
        return Complete(player, null);
    }

    private CommittedTournamentReads ReadCommittedTournamentReads()
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        if (!committed.Available || committed.CommittedTournaments?.Available != true)
            throw new L12PlatformStorageUnavailableException("平台已提交赛事读取状态不可恢复");
        return committed.CommittedTournaments;
    }

    private static CommittedTournamentLookup ResolveCommittedTournament(CommittedTournamentReads reads,
        string idOrCode)
    {
        reads.TournamentsById.TryGetValue(idOrCode, out var byId);
        reads.TournamentsByCode.TryGetValue(idOrCode, out var byCode);
        var selected = byId is null ? byCode : byCode is null || byId.SourceOrder <= byCode.SourceOrder
            ? byId : byCode;
        return selected ?? throw new KeyNotFoundException("赛事不存在");
    }

    private static (CommittedRunningTournamentMatch Match, L12TournamentRoomAccess Access)
        ResolveCommittedTournamentAccess(CommittedTournamentReads reads, string accountId,
            string tournamentId, string matchId, bool spectate)
    {
        if (!reads.Accounts.TryGetValue(accountId, out var account))
            throw new L12TournamentScopeException("账号不存在");
        if (!account.Active) throw new L12TournamentScopeException("账号已禁用或删除");
        var tournament = ResolveCommittedTournament(reads, tournamentId);
        if (!tournament.MatchIds.Contains(matchId)) throw new KeyNotFoundException("赛事桌次不存在");
        if (!reads.RunningMatches.TryGetValue((tournament.TournamentId, matchId), out var match))
            throw new L12TournamentVersionConflictException("本桌赛事房间尚未启动或已结束");

        var canPlay = match.PlayerAAccountId == accountId || match.PlayerBAccountId == accountId;
        var canRefereeView = match.StaffAccountIds.Contains(accountId) || account.CanGloballyAccess;
        var canSpectate = canRefereeView || match.PublicSpectating;
        if (spectate ? !canSpectate : !canPlay)
            throw new L12TournamentScopeException(spectate
                ? "该桌当前不可观战"
                : "仅本桌配对玩家可进入专属房间");
        if (canPlay && match.IneligibleParticipantIds.Contains(accountId))
            throw new L12TournamentScopeException("参赛资格已失效，不能进入或恢复本桌");
        if (match.Assignment.Failure is not null)
            throw new L12TournamentVersionConflictException(match.Assignment.Failure);
        return (match, new L12TournamentRoomAccess(match.Assignment.TournamentId,
            match.Assignment.TournamentCode, match.Assignment.MatchId, match.Assignment.RoomCode,
            canPlay, canSpectate, canRefereeView));
    }

    private L12TournamentRoomAssignment ReadCommittedTournamentRoomAssignment(string accountId,
        string tournamentId, string matchId, bool spectate)
    {
        var reads = ReadCommittedTournamentReads();
        var resolved = ResolveCommittedTournamentAccess(reads, accountId, tournamentId, matchId, spectate);
        return MaterializeCommittedTournamentAssignment(resolved.Match.Assignment,
            resolved.Access.CanPlay, resolved.Access.CanSpectate, resolved.Access.CanRefereeView);
    }

    private L12TournamentRoomAssignment ReadCommittedTournamentRoomAssignmentByRoom(string roomCode)
    {
        var reads = ReadCommittedTournamentReads();
        if (!reads.AssignmentsByRoom.TryGetValue(roomCode, out var assignment))
            throw new KeyNotFoundException("赛事房间绑定不存在");
        return MaterializeCommittedTournamentAssignment(assignment, false, false, false);
    }

    private L12TournamentRoomAccess ReadCommittedTournamentRoomAccess(string accountId,
        string tournamentId, string matchId, bool spectate)
    {
        var reads = ReadCommittedTournamentReads();
        return ResolveCommittedTournamentAccess(reads, accountId, tournamentId, matchId, spectate).Access;
    }

    private L12TournamentRoomAssignment MaterializeCommittedTournamentAssignment(
        CommittedTournamentAssignment assignment, bool canPlay, bool canSpectate, bool canRefereeView)
    {
        if (assignment.Failure is not null || assignment.PlayerA is null || assignment.PlayerB is null)
            throw new L12TournamentVersionConflictException(assignment.Failure ?? "赛事房间绑定无法恢复");
        var total = 0L;
        CheckOnlineDeckExpansionBudget(assignment.PlayerA.Payload, null, ref total);
        CheckOnlineDeckExpansionBudget(assignment.PlayerB.Payload, null, ref total);
        var first = MaterializeCommittedTournamentPlayer(assignment.PlayerA);
        var second = MaterializeCommittedTournamentPlayer(assignment.PlayerB);
        return new L12TournamentRoomAssignment(assignment.TournamentId, assignment.TournamentCode,
            assignment.MatchId, assignment.RoomCode, first, second,
            MaterializeCommittedTournamentPolicy(assignment.Policy), assignment.DisasterMode,
            assignment.RulesHash, assignment.TimeControl with { }, assignment.Deadline,
            canPlay, canSpectate, assignment.Paused, assignment.PauseReason, canRefereeView);
    }

    private L12TournamentRoomPlayer MaterializeCommittedTournamentPlayer(CommittedTournamentPlayer player)
    {
        DeckPayloadExpansionObserver?.Invoke(player.Payload.Hash);
        return new L12TournamentRoomPlayer(player.AccountId, player.Username, new L12PresetDeckDefinition
        {
            Name = player.DeckName,
            MasterId = player.MasterId,
            CardIds = ExpandCards(player.Payload.MainJson).ToList(),
            MoraleIds = ExpandCards(player.Payload.MoraleJson).ToList(),
            SpecialIds = ExpandCards(player.Payload.SpecialJson).ToList(),
            AlternateArtSelections = new Dictionary<string, string>(player.AlternateArtSelections,
                StringComparer.OrdinalIgnoreCase),
            PublicationId = player.PublicationId,
            PublicationVersion = player.PublicationVersion,
        }, player.DeckHash);
    }

    private static L12OperationsPolicySnapshot MaterializeCommittedTournamentPolicy(
        L12OperationsPolicySnapshot policy)
        => policy with
        {
            DisasterCardIds = policy.DisasterCardIds.ToImmutableArray(),
            CardRestrictions = policy.CardRestrictions.ToImmutableArray(),
            DefaultPresetDeckIds = policy.DefaultPresetDeckIds.ToImmutableArray(),
            MatchModes = policy.MatchModes.ToImmutableArray(),
            FeatureFlags = policy.FeatureFlags.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            Announcements = (policy.Announcements ?? []).ToImmutableArray(),
        };
}
