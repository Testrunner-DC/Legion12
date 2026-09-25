using System.Text;

namespace TwelveLegions.Server;

public sealed record L12TournamentStartCheckView(
    bool CanStart, IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings,
    int EligiblePlayers, int WaitlistedPlayers, int OpenJudgeCases);

public sealed record L12TournamentSummaryView(
    string Id, string Code, string Name, string OrganizerName, string Status, string Phase,
    string Format, string Visibility, int MaxPlayers, DateTimeOffset? StartAt,
    L12TournamentCountsView Counts, string ViewerRole, bool RequiresAction, long Version,
    DateTimeOffset UpdatedAt);

public sealed record L12TournamentSummaryPage(
    long PlatformVersion, IReadOnlyList<L12TournamentSummaryView> Items,
    int Page, int PageSize, int Total, int TotalPages);

public sealed record L12TournamentCareerEntryView(
    string TournamentId, string Code, string Name, string Status, string Format,
    DateTimeOffset? CompletedAt, int? FinalRank, int Wins, int Losses, int Draws,
    bool Organized, bool Refereed);

public sealed record L12TournamentCareerView(
    string AccountId, int Participated, int Organized, int Refereed,
    int Wins, int Losses, int Draws, IReadOnlyList<L12TournamentCareerEntryView> Items,
    int Page, int PageSize, int Total, int TotalPages);

internal sealed record L12TournamentRoomCommandView(
    string Id, string TournamentId, string Kind, string MatchId, string RoomCode, bool? Paused,
    string Reason, DateTimeOffset CreatedAt, int Attempts, string? LastError);

public sealed partial class L12PlatformStore
{
    public L12TournamentSummaryPage TournamentSummaries(L12AccountView viewer, string? section,
        string? format, string? search, int page = 1, int pageSize = 24,
        DateTimeOffset? startFrom = null, DateTimeOffset? startTo = null)
    {
        lock (_gate)
        {
            EnsurePermission(viewer, L12Permission.TournamentsRead);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var normalizedSection = string.IsNullOrWhiteSpace(section)
                ? "discover" : section.Trim().ToLowerInvariant();
            var globalStaff = CanGloballyAccessTournaments(viewer);
            var query = _data.Tournaments.Where(row => globalStaff || row.Visibility == "public"
                || IsConfiguredStaff(row, viewer.Id)
                || row.Participants.Any(item => item.AccountId == viewer.Id));
            query = normalizedSection switch
            {
                "mine" => query.Where(row => IsConfiguredStaff(row, viewer.Id)
                    || row.Participants.Any(item => item.AccountId == viewer.Id)),
                "history" => query.Where(row => row.Status is "completed" or "canceled"),
                "host" => query.Where(row => row.OrganizerAccountId == viewer.Id),
                _ => query.Where(row => row.Visibility == "public"
                    && row.Status is not ("completed" or "canceled")),
            };
            if (!string.IsNullOrWhiteSpace(format))
                query = query.Where(row => row.Format == format.Trim().ToLowerInvariant());
            if (startFrom is not null) query = query.Where(row => row.StartAt >= startFrom);
            if (startTo is not null) query = query.Where(row => row.StartAt < startTo);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var value = search.Trim();
                query = query.Where(row => row.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || row.Code.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || (AccountById(row.OrganizerAccountId)?.Username ?? string.Empty)
                        .Contains(value, StringComparison.OrdinalIgnoreCase));
            }
            var rows = query.OrderByDescending(row => TournamentNeedsViewerAction(row, viewer.Id))
                .ThenBy(row => row.StartAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(row => row.UpdatedAt).ToArray();
            var total = rows.Length;
            var items = rows.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(row => ToSummary(row, viewer)).ToArray();
            return new L12TournamentSummaryPage(Version, items, page, pageSize, total,
                total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
        }
    }

    public L12TournamentStartCheckView TournamentStartCheck(L12AccountView actor, string tournamentId)
    {
        lock (_gate)
        {
            var row = _data.Tournaments.FirstOrDefault(item => item.Id == tournamentId)
                ?? throw new KeyNotFoundException("赛事不存在");
            RequireOrganizerOrGlobalManager(actor, row);
            return BuildTournamentStartCheck(row);
        }
    }

    public L12TournamentView SetTournamentPhase(L12AccountView actor, string tournamentId,
        L12TournamentPhasePayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireOrganizerOrGlobalManager(actor, row);
            var target = Allowed(payload.Phase, "赛事阶段", "registration-open", "registration-closed",
                "pre-check-in", "running", "result-confirmation");
            if (row.Phase == target) return ToView(row, actor);
            var legal = row.Phase switch
            {
                "registration-open" => target is "registration-closed" or "pre-check-in",
                "registration-closed" => target is "registration-open" or "pre-check-in",
                "pre-check-in" => target is "registration-open" or "registration-closed",
                "running" => target == "result-confirmation",
                "result-confirmation" => target == "running",
                _ => false,
            };
            if (!legal) throw new L12TournamentVersionConflictException($"不能从 {row.Phase} 切换到 {target}");
            if (target == "result-confirmation" && row.Rounds.Any(round => round.Status != "completed"))
                throw new L12TournamentVersionConflictException("仍有未完成轮次，不能进入成绩确认");
            var reason = RequireText(payload.Reason, "赛事阶段变更理由", 500);
            return Mutate(actor, row, $"phase-{target}",
                tournamentId, context, apply, working =>
                {
                    working.Phase = target;
                    working.RegistrationOpen = target == "registration-open";
                    working.Status = target == "result-confirmation" ? "running" : "registration";
                });
        }
    }

    public L12TournamentView PostponeTournament(L12AccountView actor, string tournamentId,
        L12TournamentPostponePayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireOrganizerOrGlobalManager(actor, row);
            if (row.Status != "registration")
                throw new L12TournamentVersionConflictException("赛事开赛后不能改为延期；请暂停推进并处理当前桌次");
            if (payload.NewStartAt <= DateTimeOffset.UtcNow)
                throw new ArgumentException("新的计划开赛时间必须晚于当前时间");
            var reason = RequireText(payload.Reason, "延期理由", 500);
            return Mutate(actor, row, "postpone", tournamentId, context, apply, working =>
            {
                working.Postponements.Add(new TournamentPostponementRow
                {
                    PreviousStartAt = working.StartAt,
                    NewStartAt = payload.NewStartAt,
                    Reason = reason,
                    ActorId = actor.Id,
                });
                working.StartAt = payload.NewStartAt;
            });
        }
    }

    public L12TournamentView CancelTournament(L12AccountView actor, string tournamentId,
        L12TournamentCancelPayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireOrganizerOrGlobalManager(actor, row);
            if (row.Status is "completed" or "canceled")
                throw new L12TournamentVersionConflictException("赛事已经结束或取消");
            var reason = RequireText(payload.Reason, "取消理由", 1000);
            return Mutate(actor, row, "cancel", tournamentId, context, apply, working =>
            {
                var now = DateTimeOffset.UtcNow;
                working.Status = "canceled";
                working.Phase = "canceled";
                working.RegistrationOpen = false;
                working.CanceledAt = now;
                working.CancellationReason = reason;
                foreach (var round in working.Rounds.Where(candidate => candidate.Status != "completed"))
                {
                    round.Status = "completed";
                    round.Paused = false;
                    foreach (var match in round.Matches.Where(candidate => candidate.Status != "completed"))
                    {
                        if (match.Status == "running")
                            working.RoomCommands.Add(new TournamentRoomCommandRow
                            {
                                Kind = "cancel", MatchId = match.Id, Reason = reason,
                            });
                        match.Status = "completed";
                        match.Paused = false;
                        match.Events.Add(NewMatchEvent("tournament-canceled", null, null, actor.Id, reason));
                    }
                }
            });
        }
    }

    public L12TournamentView PauseTournamentMatch(L12AccountView actor, string tournamentId, string matchId,
        L12TournamentMatchPausePayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentRulingsWrite);
            var (_, match) = RequireMatch(row, matchId);
            if (match.Status != "running")
                throw new L12TournamentVersionConflictException("只有进行中的桌次可以暂停或恢复");
            if (match.Paused == payload.Paused) return ToView(row, actor);
            var reason = RequireText(payload.Reason, "单桌暂停/恢复理由", 500);
            return Mutate(actor, row, payload.Paused ? "match-pause" : "match-resume",
                matchId, context, apply, working =>
                {
                    var target = RequireMatch(working, matchId).Match;
                    var now = DateTimeOffset.UtcNow;
                    if (payload.Paused)
                    {
                        target.Paused = true;
                        target.PausedAt = now;
                        target.PauseReason = reason;
                    }
                    else
                    {
                        if (target.PausedAt is { } pausedAt)
                            target.TotalPausedSeconds += Math.Max(0, (int)(now - pausedAt).TotalSeconds);
                        target.Paused = false;
                        target.PausedAt = null;
                        target.PauseReason = reason;
                    }
                    target.Events.Add(NewMatchEvent(payload.Paused ? "judge-pause" : "judge-resume",
                        null, null, actor.Id, reason));
                    working.RoomCommands.Add(new TournamentRoomCommandRow
                    {
                        Kind = "pause", MatchId = matchId, Paused = payload.Paused, Reason = reason,
                    });
                });
        }
    }

    internal IReadOnlyList<L12TournamentRoomCommandView> PendingTournamentRoomCommands(
        string? tournamentId = null)
    {
        lock (_gate)
        {
            return _data.Tournaments
                .Where(row => tournamentId is null || row.Id == tournamentId)
                .SelectMany(row => row.RoomCommands.Where(command => command.CompletedAt is null)
                    .Select(command => new L12TournamentRoomCommandView(command.Id, row.Id, command.Kind,
                        command.MatchId, RequireMatch(row, command.MatchId).Match.RoomCode,
                        command.Paused, command.Reason, command.CreatedAt,
                        command.Attempts, command.LastError)))
                .OrderBy(command => command.CreatedAt).ThenBy(command => command.Id).ToArray();
        }
    }

    internal void RecordTournamentRoomCommandAttempt(string tournamentId, string commandId,
        bool completed, string? error)
    {
        lock (_gate)
        {
            var row = _data.Tournaments.FirstOrDefault(item => item.Id == tournamentId)
                ?? throw new KeyNotFoundException("赛事不存在");
            var command = row.RoomCommands.FirstOrDefault(item => item.Id == commandId)
                ?? throw new KeyNotFoundException("赛事房间待办不存在");
            if (command.CompletedAt is not null) return;
            command.Attempts++;
            command.LastError = completed ? null : OptionalText(error, 1000);
            if (completed) command.CompletedAt = DateTimeOffset.UtcNow;
            Save(businessChange: false);
        }
    }

    public L12TournamentView CreateTournamentJudgeCase(L12AccountView actor, string tournamentId,
        L12TournamentJudgeCaseCreatePayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireActiveTournament(row);
            var (round, match) = RequireMatch(row, RequireText(payload.MatchId, "桌次", 128));
            if (match.PlayerAAccountId != actor.Id && match.PlayerBAccountId != actor.Id)
                throw new L12TournamentScopeException("只能为自己所在的桌次呼叫裁判");
            var category = Allowed(payload.Category, "案件类别", "rules", "technical", "late", "result");
            var urgency = Allowed(payload.Urgency, "紧急程度", "normal", "urgent");
            var message = RequireText(payload.Message, "问题说明", 1000);
            if (row.JudgeCases.Any(item => item.MatchId == match.Id && item.RequesterAccountId == actor.Id
                    && item.Status is not ("closed" or "rejected")))
                throw new L12TournamentVersionConflictException("该桌已有你提交的未关闭裁判请求");
            return Mutate(actor, row, "judge-case-create", match.Id, context, apply, working =>
                working.JudgeCases.Add(new TournamentJudgeCaseRow
                {
                    RoundNumber = round.Number,
                    MatchId = match.Id,
                    Category = category,
                    Urgency = urgency,
                    RequesterAccountId = actor.Id,
                    PlayerMessage = message,
                }));
        }
    }

    public L12TournamentView AssignTournamentJudgeCase(L12AccountView actor, string tournamentId,
        L12TournamentJudgeCaseAssignPayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentRulingsWrite);
            var judgeCase = RequireJudgeCase(row, payload.CaseId);
            var assigneeId = RequireText(payload.AssigneeAccountId, "受理裁判", 128);
            if (!IsConfiguredStaff(row, assigneeId))
                throw new L12TournamentScopeException("受理人必须是本场主办者或裁判");
            var match = RequireMatch(row, judgeCase.MatchId).Match;
            if (match.PlayerAAccountId == assigneeId || match.PlayerBAccountId == assigneeId)
                throw new L12TournamentScopeException("参赛工作人员不能处理自己的桌次争议");
            _ = RequireText(payload.Reason, "分派理由", 500);
            return Mutate(actor, row, "judge-case-assign", judgeCase.Id, context, apply, working =>
            {
                var target = RequireJudgeCase(working, judgeCase.Id);
                target.AssigneeAccountId = assigneeId;
                target.Status = "assigned";
                target.UpdatedAt = DateTimeOffset.UtcNow;
            });
        }
    }

    public L12TournamentView ResolveTournamentJudgeCase(L12AccountView actor, string tournamentId,
        L12TournamentJudgeCaseResolvePayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentRulingsWrite);
            var judgeCase = RequireJudgeCase(row, payload.CaseId);
            var match = RequireMatch(row, judgeCase.MatchId).Match;
            if (match.PlayerAAccountId == actor.Id || match.PlayerBAccountId == actor.Id)
                throw new L12TournamentScopeException("参赛工作人员不能处理自己的桌次争议");
            if (judgeCase.AssigneeAccountId is not null && judgeCase.AssigneeAccountId != actor.Id
                && row.OrganizerAccountId != actor.Id)
                throw new L12TournamentScopeException("案件已分派给其他裁判");
            var status = Allowed(payload.Status, "案件状态", "investigating", "ruled", "closed", "rejected");
            var resolution = status is "ruled" or "closed"
                ? RequireText(payload.Resolution, "处理结论", 1000)
                : OptionalText(payload.Resolution, 1000);
            var staffNote = OptionalText(payload.StaffNote, 2000);
            return Mutate(actor, row, "judge-case-resolve", judgeCase.Id, context, apply, working =>
            {
                var target = RequireJudgeCase(working, judgeCase.Id);
                target.AssigneeAccountId ??= actor.Id;
                target.Status = status;
                target.Resolution = resolution;
                target.StaffNote = staffNote;
                target.UpdatedAt = DateTimeOffset.UtcNow;
            });
        }
    }

    public L12TournamentView AppealTournamentJudgeCase(L12AccountView actor, string tournamentId,
        L12TournamentJudgeCaseAppealPayload payload, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            var judgeCase = RequireJudgeCase(row, payload.CaseId);
            var match = RequireMatch(row, judgeCase.MatchId).Match;
            if (match.PlayerAAccountId != actor.Id && match.PlayerBAccountId != actor.Id)
                throw new L12TournamentScopeException("只有该桌参赛者可以申诉");
            if (judgeCase.Status is not ("ruled" or "closed"))
                throw new L12TournamentVersionConflictException("案件尚未裁决，不能申诉");
            if (judgeCase.AppealedAt is not null)
                throw new L12TournamentVersionConflictException("该案件已经申诉");
            var reason = RequireText(payload.Reason, "申诉理由", 1000);
            return Mutate(actor, row, "judge-case-appeal", judgeCase.Id, context, apply, working =>
            {
                var target = RequireJudgeCase(working, judgeCase.Id);
                target.Status = "appealed";
                target.AppealedAt = DateTimeOffset.UtcNow;
                target.AppealReason = reason;
                target.AssigneeAccountId = null;
                target.UpdatedAt = DateTimeOffset.UtcNow;
            });
        }
    }

    public L12TournamentCareerView TournamentCareer(L12AccountView viewer, string? accountId = null,
        int page = 1, int pageSize = 20)
    {
        lock (_gate)
        {
            EnsurePermission(viewer, L12Permission.TournamentsRead);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var target = string.IsNullOrWhiteSpace(accountId) ? viewer.Id : accountId.Trim();
            if (target != viewer.Id && !CanGloballyAccessTournaments(viewer))
                throw new L12TournamentScopeException("只能查看自己的赛事履历");
            var entries = new List<L12TournamentCareerEntryView>();
            foreach (var row in _data.Tournaments.Where(item => item.Participants.Any(p => p.AccountId == target)
                         || item.OrganizerAccountId == target || item.RefereeAccountIds.Contains(target)))
            {
                var wins = 0; var losses = 0; var draws = 0;
                foreach (var match in row.Rounds.SelectMany(round => round.Matches)
                             .Where(match => match.Status == "completed"
                                 && (match.PlayerAAccountId == target || match.PlayerBAccountId == target)))
                {
                    if (match.Result == "draw") draws++;
                    else if (WinnerAccountId(match) == target) wins++;
                    else if (WinnerAccountId(match) is not null) losses++;
                }
                var rank = row.FinalSwissStandings.FirstOrDefault(item => item.AccountId == target)?.Rank;
                entries.Add(new L12TournamentCareerEntryView(row.Id, row.Code, row.Name, row.Status,
                    row.Format, row.CompletedAt, rank, wins, losses, draws,
                    row.OrganizerAccountId == target, row.RefereeAccountIds.Contains(target)));
            }
            var ordered = entries.OrderByDescending(item => item.CompletedAt ?? DateTimeOffset.MaxValue).ToArray();
            var total = ordered.Length;
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            return new L12TournamentCareerView(target,
                _data.Tournaments.Count(item => item.Participants.Any(p => p.AccountId == target)),
                _data.Tournaments.Count(item => item.OrganizerAccountId == target),
                _data.Tournaments.Count(item => item.RefereeAccountIds.Contains(target)),
                ordered.Sum(item => item.Wins), ordered.Sum(item => item.Losses), ordered.Sum(item => item.Draws),
                items, page, pageSize, total,
                total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
        }
    }

    public string TournamentCsv(L12AccountView viewer, string idOrCode)
    {
        lock (_gate)
        {
            var tournament = Tournament(viewer, idOrCode) ?? throw new KeyNotFoundException("赛事不存在");
            var lines = new List<string> { "round,stage,table,player_a,player_b,result,status,recorded_match_id" };
            foreach (var round in tournament.Rounds)
            foreach (var match in round.Matches)
                lines.Add(string.Join(',', round.Number, Csv(round.Stage), match.Table, Csv(match.PlayerAName),
                    Csv(match.PlayerBName), Csv(match.Result ?? string.Empty), Csv(match.Status),
                    Csv(match.RecordedMatchId ?? string.Empty)));
            return string.Join("\r\n", lines);
        }
    }

    private L12TournamentSummaryView ToSummary(TournamentRow row, L12AccountView viewer)
    {
        var view = ToView(row, viewer);
        var role = row.OrganizerAccountId == viewer.Id ? "organizer"
            : row.RefereeAccountIds.Contains(viewer.Id) ? "referee"
            : row.Participants.Any(item => item.AccountId == viewer.Id) ? "participant" : "viewer";
        return new L12TournamentSummaryView(row.Id, row.Code, row.Name, view.OrganizerName,
            row.Status, row.Phase, row.Format, row.Visibility, row.MaxPlayers, row.StartAt,
            view.Counts, role, TournamentNeedsViewerAction(row, viewer.Id), row.Version, row.UpdatedAt);
    }

    private static bool TournamentNeedsViewerAction(TournamentRow row, string accountId)
    {
        var participant = row.Participants.FirstOrDefault(item => item.AccountId == accountId);
        if (participant is { Removed: false, Dropped: false, Waitlisted: false }
            && row.Status == "registration" && participant.TournamentCheckedInAt is null) return true;
        if (row.OrganizerTransfers.Any(item => item.ToAccountId == accountId && item.Status == "pending"
                && item.ExpiresAt > DateTimeOffset.UtcNow)) return true;
        if (row.JudgeCases.Any(item => item.AssigneeAccountId == accountId
                && item.Status is "assigned" or "investigating" or "appealed")) return true;
        return row.Rounds.LastOrDefault()?.Matches.Any(match => match.Status != "completed"
            && (match.PlayerAAccountId == accountId || match.PlayerBAccountId == accountId)) == true;
    }

    private L12TournamentStartCheckView BuildTournamentStartCheck(TournamentRow row)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        if (row.Status != "registration") blockers.Add("赛事不在赛前阶段");
        var eligible = row.Participants.Where(item => !item.Dropped && !item.Removed && !item.Waitlisted
            && item.TournamentCheckedInAt is not null).ToArray();
        if (eligible.Length < 2) blockers.Add("至少需要两名已签到并锁牌的正式参赛者");
        if (eligible.Any(item => string.IsNullOrWhiteSpace(item.Deck.Hash) || item.Deck.LockedAt is null))
            blockers.Add("存在牌库快照缺失或未锁定的正式参赛者");
        if (row.Format == "swiss-cut" && (row.CutSize is null || eligible.Length < row.CutSize))
            blockers.Add("有效参赛人数少于 Cut 人数");
        if (row.TimeControl is null) blockers.Add("赛事计时快照缺失");
        else _ = NormalizeRankedTimeControl(row.TimeControl);
        var waiting = row.Participants.Count(item => item.Waitlisted && !item.Dropped && !item.Removed);
        if (waiting > 0) warnings.Add($"仍有 {waiting} 名候补未递补，将不会进入首轮配对");
        var pendingCheckIn = row.Participants.Count(item => !item.Waitlisted && !item.Dropped && !item.Removed
            && item.TournamentCheckedInAt is null);
        if (pendingCheckIn > 0) warnings.Add($"仍有 {pendingCheckIn} 名正式报名者未完成签到锁牌");
        var openCases = row.JudgeCases.Count(item => item.Status is not ("closed" or "rejected"));
        if (openCases > 0) warnings.Add($"仍有 {openCases} 个未关闭裁判案件");
        if (row.RegistrationOpen) warnings.Add("报名仍开放；开赛时将自动关闭报名");
        return new L12TournamentStartCheckView(blockers.Count == 0, blockers, warnings,
            eligible.Length, waiting, openCases);
    }

    private static int NextWaitlistPosition(TournamentRow row)
        => row.Participants.Where(item => item.Waitlisted && !item.Dropped && !item.Removed)
            .Select(item => item.WaitlistPosition ?? 0).DefaultIfEmpty(0).Max() + 1;

    private static void PromoteNextWaitlisted(TournamentRow row)
    {
        if (row.Participants.Count(item => !item.Dropped && !item.Removed && !item.Waitlisted) >= row.MaxPlayers)
            return;
        var next = row.Participants.Where(item => item.Waitlisted && !item.Dropped && !item.Removed)
            .OrderBy(item => item.WaitlistPosition ?? int.MaxValue).ThenBy(item => item.Seed).FirstOrDefault();
        if (next is null) return;
        next.Waitlisted = false;
        next.WaitlistPosition = null;
        next.PromotedAt = DateTimeOffset.UtcNow;
    }

    private static TournamentJudgeCaseRow RequireJudgeCase(TournamentRow row, string caseId)
        => row.JudgeCases.FirstOrDefault(item => item.Id == caseId)
           ?? throw new KeyNotFoundException("裁判案件不存在");

    private static bool JudgeCaseIncludesViewer(TournamentRow row, TournamentJudgeCaseRow judgeCase,
        string accountId)
    {
        var match = row.Rounds.SelectMany(round => round.Matches)
            .FirstOrDefault(item => item.Id == judgeCase.MatchId);
        return judgeCase.RequesterAccountId == accountId || match?.PlayerAAccountId == accountId
            || match?.PlayerBAccountId == accountId;
    }

    private L12TournamentJudgeCaseView ToJudgeCaseView(TournamentRow row, TournamentJudgeCaseRow judgeCase,
        L12AccountView viewer, bool configuredStaff)
    {
        var match = RequireMatch(row, judgeCase.MatchId).Match;
        var requester = AccountById(judgeCase.RequesterAccountId);
        var assignee = judgeCase.AssigneeAccountId is null ? null : AccountById(judgeCase.AssigneeAccountId);
        var conflict = match.PlayerAAccountId == viewer.Id || match.PlayerBAccountId == viewer.Id;
        var canManage = configuredStaff && !conflict;
        return new L12TournamentJudgeCaseView(judgeCase.Id, judgeCase.RoundNumber, judgeCase.MatchId,
            match.Table, judgeCase.Category, judgeCase.Urgency, judgeCase.Status,
            judgeCase.RequesterAccountId, requester is null ? "已删除账号" : PublicUsername(requester),
            judgeCase.PlayerMessage, judgeCase.AssigneeAccountId,
            assignee is null ? null : PublicUsername(assignee), canManage ? judgeCase.StaffNote : null,
            judgeCase.Resolution, judgeCase.CreatedAt, judgeCase.UpdatedAt, judgeCase.AppealedAt,
            judgeCase.AppealReason, canManage);
    }

    private static string Csv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
