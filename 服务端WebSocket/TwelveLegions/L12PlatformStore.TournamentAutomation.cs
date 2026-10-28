namespace TwelveLegions.Server;

public sealed record L12TournamentAutomationSweepResult(
    int Changed, IReadOnlyList<string> TournamentIds, IReadOnlyList<L12TournamentKeyNotice>? KeyNotices = null);

public sealed partial class L12PlatformStore
{
    private sealed record AutomaticTournamentChange(int SourceIndex, TournamentRow Before,
        TournamentRow After, string Action, string Detail);

    public L12TournamentAutomationSweepResult AdvanceAutomaticTournaments(DateTimeOffset utcNow)
    {
        using var deployment = EnterDeploymentMutation();
        lock (_gate)
        {
            var plans = new List<AutomaticTournamentChange>();
            for (var index = 0; index < _data.Tournaments.Count; index++)
            {
                var current = _data.Tournaments[index];
                if (current.OperationMode != "automated" || current.Status is "completed" or "canceled"
                    || current.AutomationStatus == "attention-required") continue;
                var working = CloneTournament(current);
                string action;
                string detail;
                try
                {
                    if (!TryAdvanceAutomaticTournament(working, utcNow, out action, out detail)) continue;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException)
                {
                    action = "attention-required";
                    detail = error.Message;
                    SetAutomationAttention(working, detail);
                }

                working.Version++;
                working.UpdatedAt = utcNow;
                plans.Add(new(index, current, working, action, detail));
            }
            // The ordinary idle sweep does not serialize all platform state.
            // Each candidate was advanced once on a clone under this same gate;
            // no authority/account/audit write or random pairing rerun precedes
            // the one complete transaction for a genuinely nonempty plan.
            if (plans.Count == 0) return new(0, [], []);
            return _adminTransactionDepth == 0
                ? ExecuteAdminTransaction(() => CommitAutomaticTournamentChanges(plans, utcNow))
                : CommitAutomaticTournamentChanges(plans, utcNow);
        }
    }

    private L12TournamentAutomationSweepResult CommitAutomaticTournamentChanges(
        IReadOnlyList<AutomaticTournamentChange> plans, DateTimeOffset utcNow)
    {
        var changed = new List<string>();
        var notices = new List<L12TournamentKeyNotice>();
        foreach (var plan in plans)
        {
            var (index, current, working, action, detail) = plan;
            if (AccountById(working.OrganizerAccountId) is { } reputationActor)
                ApplyTournamentReputationTransition(ToView(reputationActor), current, working, action,
                    working.Id, new L12AdminAuditContext($"tournament-automation:{working.Id}:{working.Version}",
                        L12Authorization.Key(L12Permission.TournamentsManage), RequestMethod: "SYSTEM",
                        RequestPath: "/internal/tournaments/automation"), utcNow);
            _data.Tournaments[index] = working;
            if (AccountById(working.OrganizerAccountId) is { } organizer)
                AddAdminAudit(ToView(organizer), "tournament", $"automation-{action}", working.Id,
                    current.Version.ToString(), working.Version.ToString(), detail,
                    new L12AdminAuditContext($"tournament-automation:{working.Id}:{working.Version}",
                        L12Authorization.Key(L12Permission.TournamentsManage),
                        RequestMethod: "SYSTEM", RequestPath: "/internal/tournaments/automation", Reason: detail));
            changed.Add(working.Id);
            if (CreateKeyNotice(current, working, action) is { } notice) notices.Add(notice);
        }
        Save();
        return new(changed.Count, changed, notices);
    }

    private bool TryAdvanceAutomaticTournament(TournamentRow tournament, DateTimeOffset now,
        out string action, out string detail)
    {
        action = string.Empty;
        detail = string.Empty;
        if (tournament.Status == "registration")
            return TryAdvanceAutomaticRegistration(tournament, now, out action, out detail);
        if (tournament.Status != "running") return false;

        var round = tournament.Rounds.LastOrDefault();
        if (round is null)
        {
            SetAutomationAttention(tournament, "赛事已进入运行状态但没有轮次");
            action = "attention-required";
            detail = tournament.AutomationAttentionReason!;
            return true;
        }

        if (round.Status == "checkin")
        {
            if (tournament.NextAutomaticActionAt is { } due && due > now) return false;
            StartTournamentRoundCore(tournament, round.Number, now, "tournament-automation");
            action = "round-start";
            detail = $"第 {round.Number} 轮轮间准备结束，自动开始本轮";
            return true;
        }

        if (round.Status == "running")
        {
            if (round.Paused) return false;
            var dueMatches = round.Matches.Where(match => match.Status == "waiting"
                    && match.GraceDeadline is { } deadline && deadline <= now)
                .OrderBy(match => match.Table).ToArray();
            if (dueMatches.Length == 0) return false;
            foreach (var match in dueMatches)
            {
                if (match.ReadyA && match.ReadyB)
                {
                    match.Status = "running";
                    match.StartedAt = now;
                    match.GraceDeadline = null;
                    match.Deadline = tournament.UsesLegacyRoundClock
                        ? now.AddMinutes(tournament.RoundMinutes + match.TimeExtensionMinutes) : null;
                    match.Events.Add(NewMatchEvent("late-table-start", null, null, "tournament-automation",
                        "双方在自动迟到宽限期结束时均已到齐"));
                    continue;
                }
                ApplyAutomaticNoShow(tournament, round, match, now);
            }
            FinalizeRoundIfComplete(tournament, round);
            action = tournament.AutomationStatus == "attention-required"
                ? "attention-required" : "late-grace-settlement";
            detail = tournament.AutomationAttentionReason
                ?? $"第 {round.Number} 轮迟到宽限结束，已按签到事实自动处理";
            return true;
        }

        if (round.Status != "completed") return false;
        if (tournament.JudgeCases.Any(item => item.Status is not ("closed" or "rejected")))
        {
            SetAutomationAttention(tournament, "存在未关闭的裁判案件，自动推进已暂停");
            action = "attention-required";
            detail = tournament.AutomationAttentionReason!;
            return true;
        }

        if (AutomaticTournamentHasFinished(tournament, round, out var finishError))
        {
            CompleteTournamentCore(tournament, now);
            action = "complete";
            detail = "全部计划轮次已经产生确定赛果，赛事自动归档";
            return true;
        }
        if (finishError is not null)
        {
            SetAutomationAttention(tournament, finishError);
            action = "attention-required";
            detail = finishError;
            return true;
        }

        CreateNextRoundCore(tournament, now);
        action = "round-create";
        detail = $"第 {round.Number} 轮完成，已自动生成下一轮";
        return true;
    }

    private bool TryAdvanceAutomaticRegistration(TournamentRow tournament, DateTimeOffset now,
        out string action, out string detail)
    {
        action = string.Empty;
        detail = string.Empty;
        if (tournament.StartAt is not { } startAt)
        {
            SetAutomationAttention(tournament, "自动运行赛事缺少计划开赛时间");
            action = "attention-required";
            detail = tournament.AutomationAttentionReason!;
            return true;
        }

        var checkInAt = startAt.AddMinutes(-tournament.CheckInMinutes);
        CaptureDueTournamentCheckInStart(tournament, now);
        if (tournament.Phase is "registration-open" or "registration-closed")
        {
            if (now < checkInAt) return false;
            tournament.Phase = "pre-check-in";
            tournament.RegistrationOpen = false;
            tournament.AutomationStatus = "scheduled";
            tournament.NextAutomaticActionAt = startAt;
            tournament.AutomationAttentionReason = null;
            action = "pre-check-in";
            detail = "报名已按计划关闭，进入赛前签到";
            return true;
        }

        if (tournament.Phase != "pre-check-in" || now < startAt) return false;
        var check = BuildTournamentStartCheck(tournament);
        if (!check.CanStart)
        {
            SetAutomationAttention(tournament, string.Join("；", check.Blockers));
            action = "attention-required";
            detail = tournament.AutomationAttentionReason!;
            return true;
        }
        StartTournamentCore(tournament, now);
        action = "start";
        detail = "赛前检查通过，赛事已按计划自动开赛";
        return true;
    }

    private static void SetAutomationAttention(TournamentRow tournament, string reason)
    {
        tournament.AutomationStatus = "attention-required";
        tournament.NextAutomaticActionAt = null;
        tournament.AutomationAttentionReason = OptionalText(reason, 1000);
    }

    private static void ResumeAutomationAfterManualResult(TournamentRow tournament,
        TournamentRoundRow round, DateTimeOffset now)
    {
        if (tournament.OperationMode != "automated"
            || tournament.AutomationStatus != "attention-required") return;
        tournament.AutomationStatus = round.Status == "checkin" ? "preparing-round" : "round-running";
        tournament.NextAutomaticActionAt = now;
        tournament.AutomationAttentionReason = null;
    }

    private static void ResumeAutomationAfterClosedCases(TournamentRow tournament, DateTimeOffset now)
    {
        if (tournament.OperationMode != "automated"
            || tournament.AutomationStatus != "attention-required"
            || tournament.JudgeCases.Any(item => item.Status is not ("closed" or "rejected"))) return;
        var round = tournament.Rounds.LastOrDefault();
        if (round?.Status != "completed") return;
        tournament.AutomationStatus = "round-running";
        tournament.NextAutomaticActionAt = now;
        tournament.AutomationAttentionReason = null;
    }

    private void ApplyAutomaticNoShow(TournamentRow tournament, TournamentRoundRow round,
        TournamentMatchRow match, DateTimeOffset now)
    {
        if (!match.ReadyA && !match.ReadyB)
        {
            DropAutomaticAbsentParticipant(tournament, match.PlayerAAccountId, match.Id);
            if (match.PlayerBAccountId is { } secondId)
                DropAutomaticAbsentParticipant(tournament, secondId, match.Id);
            match.Events.Add(NewMatchEvent("double-no-show", null, null, "tournament-automation",
                "双方均未在迟到宽限期内到场"));
            if (round.Stage == "elimination")
            {
                SetAutomationAttention(tournament, $"第 {round.Number} 轮第 {match.Table} 桌双方均未到场，淘汰赛无法自动决定晋级者");
                return;
            }
            match.Result = "double-no-show";
            match.Status = "completed";
            match.GraceDeadline = null;
            match.Rulings.Add(NewAutomaticRuling(match.Id, null, "double-no-show", now,
                "双方均未在迟到宽限期内到场，双方退出后续轮次"));
            return;
        }

        var absentA = !match.ReadyA;
        var absentId = absentA ? match.PlayerAAccountId : match.PlayerBAccountId!;
        var decision = absentA ? "no-show-a" : "no-show-b";
        DropAutomaticAbsentParticipant(tournament, absentId, match.Id);
        match.Result = decision;
        match.Status = "completed";
        match.GraceDeadline = null;
        match.Events.Add(NewMatchEvent("no-show", decision, null, "tournament-automation",
            "玩家未在迟到宽限期内到场，按既有未到场规则判负并退出后续轮次"));
        match.Rulings.Add(NewAutomaticRuling(match.Id, absentId, decision, now,
            "迟到宽限期结束后仍未到场"));
    }

    private static TournamentRulingRow NewAutomaticRuling(string matchId, string? targetAccountId,
        string decision, DateTimeOffset now, string reason)
        => new()
        {
            MatchId = matchId,
            Kind = "no-show",
            TargetAccountId = targetAccountId,
            Decision = decision,
            Reason = reason,
            ActorId = "tournament-automation",
            ActorName = "自动赛事系统",
            CreatedAt = now,
        };

    private static void DropAutomaticAbsentParticipant(TournamentRow tournament, string accountId,
        string matchId)
    {
        var participant = tournament.Participants.First(item => item.AccountId == accountId);
        if (!participant.Dropped) participant.AutomaticDropMatchId = matchId;
        participant.Dropped = true;
        participant.CheckedIn = false;
    }

    private static bool AutomaticTournamentHasFinished(TournamentRow tournament, TournamentRoundRow lastRound,
        out string? error)
    {
        error = null;
        if (lastRound.Stage == "swiss")
            return tournament.Format is "swiss" or "league"
                   && tournament.Rounds.Count(item => item.Stage == "swiss") >= tournament.SwissRounds;

        if (lastRound.Matches.Count != 1) return false;
        if (WinnerAccountId(lastRound.Matches[0]) is not null) return true;
        error = "淘汰赛最终桌没有可确定的胜者";
        return false;
    }

    private void StartTournamentCore(TournamentRow working, DateTimeOffset now,
        string? actorId = null, string? correlationId = null)
    {
        CaptureDueTournamentCheckInStart(working, now);
        CaptureTournamentCheckInStart(working, now, actorId is null ? "automatic-start" : "manual-start",
            actorId, correlationId);
        ApplyTournamentStartingRoster(working, now);
        var startCheck = BuildTournamentStartCheck(working);
        if (!startCheck.CanStart) throw new ArgumentException(string.Join("；", startCheck.Blockers));
        working.Status = "running";
        working.Phase = "running";
        working.RegistrationOpen = false;
        var seed = 1;
        foreach (var participant in working.Participants
                     .OrderBy(item => item.Seed > 0 ? item.Seed : int.MaxValue)
                     .ThenBy(item => item.AccountId, StringComparer.Ordinal))
            participant.Seed = seed++;
        foreach (var participant in working.Participants.Where(item => !item.Dropped && !item.Removed
                     && !item.Waitlisted && item.TournamentCheckedInAt is not null))
        {
            participant.Eliminated = false;
            participant.CheckedIn = false;
        }
        working.Rounds.Add(working.Format == "single"
            ? CreateInitialEliminationRound(working, working.Participants
                .Where(item => !item.Dropped && !item.Removed && !item.Waitlisted
                    && item.TournamentCheckedInAt is not null)
                .OrderBy(item => item.Seed).ToArray(), 1)
            : CreateSwissRound(working, 1));
        if (working.OperationMode == "automated")
        {
            working.AutomationStatus = "preparing-round";
            working.AutomationAttentionReason = null;
            working.NextAutomaticActionAt = now.AddMinutes(working.IntermissionMinutes);
        }
    }

    private void CreateNextRoundCore(TournamentRow working, DateTimeOffset now)
    {
        foreach (var participant in working.Participants) participant.CheckedIn = false;
        var previous = working.Rounds.Last();
        TournamentRoundRow next;
        if (previous.Stage == "elimination")
        {
            next = CreateNextEliminationRound(working, previous, previous.Number + 1);
        }
        else if (working.Format == "swiss-cut" && previous.Number >= working.SwissRounds)
        {
            working.FinalSwissStandings = previous.Standings.Select(CloneStanding).ToList();
            var activeStandings = working.FinalSwissStandings.Where(item =>
            {
                var participant = working.Participants.First(candidate => candidate.AccountId == item.AccountId);
                return !participant.Dropped && !participant.Removed && !participant.Waitlisted;
            }).ToArray();
            if (activeStandings.Length < working.CutSize!.Value)
                throw new L12TournamentPairingException("未退赛选手数少于 cut 人数，未创建淘汰轮");
            var cut = activeStandings.Take(working.CutSize.Value).Select(item => item.AccountId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var participant in working.Participants)
                participant.Eliminated = !cut.Contains(participant.AccountId);
            var finalists = activeStandings.Take(working.CutSize.Value)
                .Select(item => working.Participants.First(participant => participant.AccountId == item.AccountId))
                .ToArray();
            next = CreateInitialEliminationRound(working, finalists, previous.Number + 1);
        }
        else
        {
            if (working.Format != "league" && previous.Number >= working.SwissRounds)
                throw new L12TournamentPairingException("已完成计划的全部瑞士轮");
            next = CreateSwissRound(working, previous.Number + 1);
        }
        working.Rounds.Add(next);
        if (working.OperationMode == "automated")
        {
            working.AutomationStatus = "preparing-round";
            working.AutomationAttentionReason = null;
            working.NextAutomaticActionAt = now.AddMinutes(working.IntermissionMinutes);
        }
    }

    private static void StartTournamentRoundCore(TournamentRow working, int roundNumber,
        DateTimeOffset now, string actorId)
    {
        var target = RequireRound(working, roundNumber);
        target.Status = "running";
        target.StartedAt = now;
        foreach (var match in target.Matches.Where(item => item.Status != "completed"))
        {
            if (match.ReadyA && match.ReadyB)
            {
                match.Status = "running";
                match.StartedAt = now;
                match.Deadline = working.UsesLegacyRoundClock
                    ? now.AddMinutes(working.RoundMinutes + match.TimeExtensionMinutes) : null;
                match.Events.Add(NewMatchEvent("table-start", null, null, actorId,
                    "轮次启动时双方已到齐"));
            }
            else
            {
                match.Status = "waiting";
                match.GraceDeadline = now.AddMinutes(working.LateGraceMinutes);
                match.Events.Add(NewMatchEvent("late-grace", null, null, actorId,
                    $"未到齐玩家有 {working.LateGraceMinutes} 分钟宽限期"));
            }
        }
        FinalizeRoundIfComplete(working, target);
        if (working.OperationMode == "automated")
        {
            working.AutomationStatus = "round-running";
            working.AutomationAttentionReason = null;
            working.NextAutomaticActionAt = target.Matches
                .Where(item => item.Status == "waiting" && item.GraceDeadline is not null)
                .Select(item => item.GraceDeadline).Min();
        }
    }

    private static void CompleteTournamentCore(TournamentRow working, DateTimeOffset now)
    {
        working.Status = "completed";
        working.Phase = "completed";
        working.RegistrationOpen = false;
        working.CompletedAt = now;
        working.AutomationStatus = working.OperationMode == "automated" ? "completed" : "disabled";
        working.NextAutomaticActionAt = null;
        working.AutomationAttentionReason = null;
    }
}
