namespace TwelveLegions.Server;

public sealed record L12MatchGovernanceClientProjection(
    bool Supported,
    string? OpponentAccountId,
    string? OpponentName,
    bool CanRequestDraw,
    string? DrawUnavailableReason,
    bool CanReportOpponent,
    string? ReportUnavailableReason,
    L12MatchDrawRequestClientView? DrawRequest);

public sealed partial class L12RoomManager
{
    public async Task<IReadOnlyList<OutgoingMessage>> RequestMatchDrawAsync(
        Guid sessionId, string? requestId, string? reason)
    {
        var clientRequestId = requestId?.Trim() ?? string.Empty;
        if (!TryGetMembership(sessionId, out var session, out var room, out var membershipError))
            return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId, membershipError);
        await room.Gate.WaitAsync();
        try
        {
            if (_platform is null)
                return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId,
                    "对局治理服务不可用");
            if (!TryGovernanceOpponent(room, session, requireOngoingGame: true, out var opponent,
                    out var unavailable))
                return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId, unavailable);
            var requester = _platform.Account(session.AccountId!);
            var responder = _platform.Account(opponent.AccountId!);
            if (requester is null || responder is null)
                return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId,
                    "对局双方账号已不可用");
            try
            {
                var record = _platform.CreateMatchDrawRequest(clientRequestId,
                    room.Game!.State.MatchId, room.Code, room.Options.MatchModeId,
                    requester, responder, reason ?? string.Empty, _utcNow());
                var messages = BroadcastGame(room).ToList();
                messages.Add(MatchGovernanceResult(sessionId, "request-draw", clientRequestId,
                    record.Status, record.Id, record.Status == "pending"
                        ? "平局申请已送达对手，等待处理"
                        : $"平局申请当前状态：{DrawStatusLabel(record.Status)}"));
                return messages;
            }
            catch (L12MatchGovernanceConflictException error)
            {
                return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId,
                    error.Message, error.Code);
            }
            catch (ArgumentException error)
            {
                return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId, error.Message);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Match draw request ({room.Code}): {error.Message}");
                return MatchGovernanceFailure(sessionId, "request-draw", clientRequestId,
                    "平局申请暂时无法保存，请稍后重试", "governance_storage_failed");
            }
        }
        finally { room.Gate.Release(); }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> ResolveMatchDrawAsync(
        Guid sessionId, string? requestId, bool accept)
    {
        var clientRequestId = requestId?.Trim() ?? string.Empty;
        if (!TryGetMembership(sessionId, out var session, out var room, out var membershipError))
            return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId, membershipError);
        await room.Gate.WaitAsync();
        try
        {
            if (_platform is null || string.IsNullOrWhiteSpace(session.AccountId))
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId,
                    "对局治理服务不可用");
            var responder = _platform.Account(session.AccountId);
            if (responder is null)
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId,
                    "当前账号已不可用");
            var now = _utcNow();
            L12MatchDrawRequestView validation;
            try
            {
                validation = _platform.ValidateMatchDrawResolution(room.Game?.State.MatchId ?? string.Empty,
                    clientRequestId, responder.Id, accept, now);
            }
            catch (Exception error) when (error is ArgumentException or L12MatchGovernanceConflictException)
            {
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId, error.Message);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Match draw validation ({room.Code}): {error.Message}");
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId,
                    "平局申请记录暂时不可用，请稍后重试", "governance_storage_failed");
            }

            var terminal = accept ? "accepted" : "rejected";
            if (validation.Status == terminal)
            {
                var replay = room.Game is null ? new List<OutgoingMessage>() : BroadcastGame(room).ToList();
                replay.Add(MatchGovernanceResult(sessionId, "resolve-draw", clientRequestId,
                    terminal, validation.Id, accept ? "本局已按双方同意记录为平局" : "已拒绝平局申请，对局继续"));
                return replay;
            }
            if (room.Game is null || room.Game.State.Phase == L12Phase.GameOver)
            {
                _platform.CancelOpenMatchDrawRequests(validation.MatchId, now, "对局已结束");
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId,
                    "对局已经结束，平局申请已取消");
            }
            if (!TryGovernanceOpponent(room, session, requireOngoingGame: true, out _, out var unavailable))
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId, unavailable);

            if (!accept)
            {
                try
                {
                    var rejected = _platform.ResolveMatchDrawRequest(validation.MatchId, validation.Id,
                        responder, false, now);
                    var messages = BroadcastGame(room).ToList();
                    messages.Add(MatchGovernanceResult(sessionId, "resolve-draw", clientRequestId,
                        rejected.Status, rejected.Id, "已拒绝平局申请，对局继续"));
                    return messages;
                }
                catch (Exception error) when (error is ArgumentException or L12MatchGovernanceConflictException)
                {
                    return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId, error.Message);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Match draw rejection ({room.Code}): {error.Message}");
                    return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId,
                        "平局申请处理结果暂时无法保存，请稍后重试", "governance_storage_failed");
                }
            }

            var authorityPersisted = false;
            try
            {
                _platform.BeginMatchDrawAcceptance(validation.MatchId, validation.Id, responder, now);
                SettleRankedClockLocked(room, now);
                if (room.RankedClock is { } rankedClock)
                    rankedClock.ConclusionKind = L12GameEngine.AgreedDrawConclusionKind;
                room.Game.ConcludeAgreedDrawByAuthority("双方同意平局");
                string? completionWarning = null;
                if (room.RankedClock is not null)
                {
                    await ApplyRankedClockConclusionLockedAsync(room, now);
                    authorityPersisted = room.CompletionRecorded;
                    if (!room.RankedResultReported)
                        completionWarning = "排位平局已权威记录，结算账本正在重试";
                }
                else
                {
                    room.CommandSequence++;
                    await _recorder.AppendAuthorityAsync(room.Game, room.CommandSequence,
                        room.Game.State.WinnerReason ?? "双方同意平局");
                    authorityPersisted = true;
                    completionWarning = await CompleteTournamentRoomGameAsync(room);
                }
                try
                {
                    _platform.FinalizeAgreedDrawFromSettlement(validation.MatchId, _utcNow());
                }
                catch (Exception finalizeError)
                {
                    Console.Error.WriteLine($"Match draw governance finalize ({room.Code}): "
                                            + finalizeError.Message);
                    completionWarning ??= "平局已权威结束，治理记录正在重试同步";
                }
                var messages = BroadcastGame(room).ToList();
                messages.Add(MatchGovernanceResult(sessionId, "resolve-draw", clientRequestId,
                    "accepted", validation.Id, completionWarning ?? "双方已同意，本局权威结束为平局"));
                if (completionWarning is not null)
                    messages.AddRange(Error(sessionId, completionWarning, "matchCompletionPending"));
                return messages;
            }
            catch (Exception error)
            {
                authorityPersisted |= room.CompletionRecorded;
                if (!authorityPersisted)
                {
                    try
                    {
                        _platform.AbortMatchDrawAcceptance(validation.MatchId, validation.Id, _utcNow(),
                            "权威终局持久化未完成");
                    }
                    catch (Exception abortError)
                    {
                        Console.Error.WriteLine($"Match draw acceptance rollback ({room.Code}): "
                                                + abortError.Message);
                    }
                    if (room.RankedClock is null) room.Closed = true;
                }
                else
                {
                    Console.Error.WriteLine($"Match draw post-persistence completion ({room.Code}): "
                                            + error.Message);
                    var messages = BroadcastGame(room).ToList();
                    messages.Add(MatchGovernanceResult(sessionId, "resolve-draw", clientRequestId,
                        "accepted", validation.Id, "平局已权威结束，后续记录正在重试同步"));
                    messages.AddRange(Error(sessionId, "平局已权威结束，后续记录正在重试同步",
                        "matchCompletionPending"));
                    return messages;
                }
                return MatchGovernanceFailure(sessionId, "resolve-draw", clientRequestId,
                    room.Closed
                        ? "平局终局持久化失败，房间已安全冻结"
                        : "平局终局未完成持久化，已恢复到最后确认状态，请重试",
                    error is L12MatchGovernanceConflictException conflict
                            ? conflict.Code : "draw_persistence_failed");
            }
        }
        finally { room.Gate.Release(); }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> ReportOpponentAsync(
        Guid sessionId, string? reportId, string? description)
    {
        var clientRequestId = reportId?.Trim() ?? string.Empty;
        if (!TryGetMembership(sessionId, out var session, out var room, out var membershipError))
            return MatchGovernanceFailure(sessionId, "report-opponent", clientRequestId, membershipError);
        await room.Gate.WaitAsync();
        try
        {
            if (_platform is null)
                return MatchGovernanceFailure(sessionId, "report-opponent", clientRequestId,
                    "对局治理服务不可用");
            if (!TryGovernanceOpponent(room, session, requireOngoingGame: false, out var opponent,
                    out var unavailable))
                return MatchGovernanceFailure(sessionId, "report-opponent", clientRequestId, unavailable);
            var reporter = _platform.Account(session.AccountId!);
            var reported = _platform.Account(opponent.AccountId!);
            if (reporter is null || reported is null)
                return MatchGovernanceFailure(sessionId, "report-opponent", clientRequestId,
                    "对局双方账号已不可用");
            try
            {
                var record = _platform.CreatePlayerMatchReport(clientRequestId,
                    room.Game!.State.MatchId, room.Code, room.Options.MatchModeId,
                    reporter, reported, description ?? string.Empty, _utcNow());
                return [MatchGovernanceResult(sessionId, "report-opponent", clientRequestId,
                    "submitted", record.Id, $"举报已提交：{record.Id}")];
            }
            catch (Exception error) when (error is ArgumentException or L12MatchGovernanceConflictException)
            {
                return MatchGovernanceFailure(sessionId, "report-opponent", clientRequestId, error.Message);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Match opponent report ({room.Code}): {error.Message}");
                return MatchGovernanceFailure(sessionId, "report-opponent", clientRequestId,
                    "举报暂时无法保存，请稍后重试", "governance_storage_failed");
            }
        }
        finally { room.Gate.Release(); }
    }

    private L12MatchGovernanceClientProjection MatchGovernanceForClient(Room room, Session viewer)
    {
        if (_platform is null || room.Game is null || string.IsNullOrWhiteSpace(viewer.AccountId))
            return new(false, null, null, false, "当前服务端不支持对局治理", false,
                "当前服务端不支持对局治理", null);
        var hasOpponent = TryGovernanceOpponent(room, viewer, requireOngoingGame: false,
            out var opponent, out var reportReason);
        var drawAvailable = TryGovernanceOpponent(room, viewer, requireOngoingGame: true,
            out _, out var drawReason);
        L12MatchDrawRequestClientView? request;
        try
        {
            if (room.Game.State.EndedByAgreedDraw)
                _platform.FinalizeAgreedDrawFromSettlement(room.Game.State.MatchId, _utcNow());
            request = _platform.MatchDrawRequestForClient(room.Game.State.MatchId,
                viewer.AccountId, _utcNow());
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Match governance projection ({room.Code}): {error.Message}");
            return new(false, hasOpponent ? opponent.AccountId : null,
                hasOpponent ? opponent.Name : null, false, "平局申请记录暂时不可用",
                false, "对局举报服务暂时不可用", null);
        }
        if (request is not null)
        {
            drawAvailable = false;
            drawReason = request.Status is "pending" or "accepting"
                ? request.ViewerCanRespond ? "请先处理当前平局申请" : "已有平局申请等待处理"
                : "每场对局双方合计仅可发起一次平局申请，本局机会已使用";
        }
        return new(true, hasOpponent ? opponent.AccountId : null, hasOpponent ? opponent.Name : null,
            drawAvailable, drawAvailable ? null : drawReason, hasOpponent,
            hasOpponent ? null : reportReason, request);
    }

    private bool TryGovernanceOpponent(Room room, Session viewer, bool requireOngoingGame,
        out Session opponent, out string reason)
    {
        opponent = null!;
        if (room.IsSandbox)
        {
            reason = "单人沙盒不支持此功能";
            return false;
        }
        if (room.TournamentId is not null && requireOngoingGame)
        {
            reason = "赛事对局由赛事裁判处理赛果，不能协商平局";
            return false;
        }
        if (room.Game is null)
        {
            reason = "对局尚未开始";
            return false;
        }
        if (requireOngoingGame && room.Game.State.Phase == L12Phase.GameOver)
        {
            reason = "对局已经结束";
            return false;
        }
        if (room.Sessions.Count != 2 || viewer.PlayerIndex is null
            || !room.Sessions.All(id => _sessions.TryGetValue(id, out var member)
                && !member.IsVirtual && !string.IsNullOrWhiteSpace(member.AccountId)))
        {
            reason = "仅真实双人对局支持此功能";
            return false;
        }
        opponent = room.Sessions.Select(id => _sessions[id])
            .Single(member => member.PlayerIndex != viewer.PlayerIndex);
        reason = string.Empty;
        return true;
    }

    private static OutgoingMessage MatchGovernanceResult(Guid sessionId, string action,
        string clientRequestId, string status, string? recordId, string message)
        => new(sessionId, new
        {
            type = "matchGovernanceResult", action, clientRequestId, status, recordId, message,
        });

    private static IReadOnlyList<OutgoingMessage> MatchGovernanceFailure(Guid sessionId,
        string action, string clientRequestId, string message, string code = "governance_rejected")
        => [new OutgoingMessage(sessionId, new
        {
            type = "matchGovernanceResult", action, clientRequestId,
            status = "rejected", recordId = (string?)null, code, message,
        })];

    private static string DrawStatusLabel(string status) => status switch
    {
        "accepting" => "正在确认",
        "accepted" => "已接受",
        "rejected" => "已拒绝",
        "expired" => "已过期",
        "cancelled" => "已取消",
        _ => "待处理",
    };
}
