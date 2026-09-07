namespace TwelveLegions.Server;

public sealed record L12MatchGovernanceAuditView(
    string Id, string ActorId, string ActorName, string Action,
    string? FromValue, string? ToValue, string? Comment, DateTimeOffset CreatedAt);

public sealed record L12MatchDrawRequestView(
    string Id, string MatchId, string RoomCode, string ModeId,
    string RequesterId, string RequesterName, string ResponderId, string ResponderName,
    string Reason, string Status, string AdminStatus, string? AdminNotes,
    DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RespondedAt,
    IReadOnlyList<L12MatchGovernanceAuditView> History);

public sealed record L12MatchDrawRequestClientView(
    string Id, string MatchId, string RequesterName, string ResponderName, string Reason,
    string Status, bool ViewerIsRequester, bool ViewerCanRespond,
    DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RespondedAt);

public sealed record L12PlayerMatchReportView(
    string Id, string MatchId, string RoomCode, string ModeId,
    string ReporterId, string ReporterName, string ReportedId, string ReportedName,
    string Description, string Status, string? AdminNotes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<L12MatchGovernanceAuditView> History);

public sealed record L12MatchGovernanceUpdate(
    string Status, string? AdminNotes, string? Comment);

public sealed class L12MatchGovernanceConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed partial class L12PlatformStore
{
    internal static readonly TimeSpan MatchDrawRequestLifetime = TimeSpan.FromMinutes(5);

    private sealed class MatchGovernanceAuditRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = "系统";
        public string Action { get; set; } = string.Empty;
        public string? FromValue { get; set; }
        public string? ToValue { get; set; }
        public string? Comment { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class MatchDrawRequestRow
    {
        public string Id { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public string ModeId { get; set; } = string.Empty;
        public string RequesterId { get; set; } = string.Empty;
        public string RequesterName { get; set; } = string.Empty;
        public string ResponderId { get; set; } = string.Empty;
        public string ResponderName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public string AdminStatus { get; set; } = "new";
        public string? AdminNotes { get; set; }
        public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? RespondedAt { get; set; }
        public List<MatchGovernanceAuditRow> History { get; set; } = [];
    }

    private sealed class PlayerMatchReportRow
    {
        public string Id { get; set; } = string.Empty;
        public string MatchId { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public string ModeId { get; set; } = string.Empty;
        public string ReporterId { get; set; } = string.Empty;
        public string ReporterName { get; set; } = string.Empty;
        public string ReportedId { get; set; } = string.Empty;
        public string ReportedName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "new";
        public string? AdminNotes { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<MatchGovernanceAuditRow> History { get; set; } = [];
    }

    internal L12MatchDrawRequestView CreateMatchDrawRequest(
        string requestId, string matchId, string roomCode, string modeId,
        L12AccountView requester, L12AccountView responder, string reason, DateTimeOffset now)
    {
        requestId = NormalizeGovernanceId(requestId, "draw");
        reason = NormalizeGovernanceText(reason, 1000, "请填写申请平局的Bug");
        lock (_gate)
        {
            ExpireMatchDrawRequestsLocked(matchId, now);
            var repeated = _data.MatchDrawRequests.FirstOrDefault(row => row.Id == requestId);
            if (repeated is not null)
            {
                if (repeated.MatchId != matchId || repeated.RequesterId != requester.Id
                    || repeated.ResponderId != responder.Id || repeated.Reason != reason)
                    throw new L12MatchGovernanceConflictException("draw_request_id_conflict",
                        "平局申请标识已被其他请求使用");
                return DrawView(repeated);
            }
            if (_data.MatchDrawRequests.Any(row => row.MatchId == matchId))
                throw new L12MatchGovernanceConflictException("draw_request_limit_reached",
                    "每场对局双方合计仅可发起一次平局申请，本局机会已使用");

            var row = new MatchDrawRequestRow
            {
                Id = requestId,
                MatchId = matchId,
                RoomCode = roomCode,
                ModeId = modeId,
                RequesterId = requester.Id,
                RequesterName = requester.Username,
                ResponderId = responder.Id,
                ResponderName = responder.Username,
                Reason = reason,
                RequestedAt = now,
                ExpiresAt = now + MatchDrawRequestLifetime,
            };
            row.History.Add(GovernanceAudit(requester, "requested", null, "pending", reason, now));
            _data.MatchDrawRequests.Add(row);
            Save();
            return DrawView(row);
        }
    }

    internal L12MatchDrawRequestView ValidateMatchDrawResolution(
        string matchId, string requestId, string responderId, bool accept, DateTimeOffset now)
    {
        lock (_gate)
        {
            ExpireMatchDrawRequestsLocked(matchId, now);
            var row = RequireDrawRequestLocked(matchId, requestId);
            if (row.ResponderId != responderId)
                throw new L12MatchGovernanceConflictException("draw_response_forbidden",
                    "只有平局申请的接收方可以处理");
            var terminal = accept ? "accepted" : "rejected";
            if (row.Status == terminal) return DrawView(row);
            // "accepting" is a persisted saga checkpoint. A restarted server may safely
            // resume the same acceptance while the authoritative game is still ongoing.
            if (accept && row.Status == "accepting") return DrawView(row);
            if (row.Status != "pending")
                throw new L12MatchGovernanceConflictException("draw_request_not_pending",
                    "平局申请已处理或已过期");
            return DrawView(row);
        }
    }

    internal L12MatchDrawRequestView ResolveMatchDrawRequest(
        string matchId, string requestId, L12AccountView responder, bool accept, DateTimeOffset now)
    {
        lock (_gate)
        {
            ExpireMatchDrawRequestsLocked(matchId, now);
            var row = RequireDrawRequestLocked(matchId, requestId);
            if (row.ResponderId != responder.Id)
                throw new L12MatchGovernanceConflictException("draw_response_forbidden",
                    "只有平局申请的接收方可以处理");
            var status = accept ? "accepted" : "rejected";
            if (row.Status == status) return DrawView(row);
            if (row.Status != "pending")
                throw new L12MatchGovernanceConflictException("draw_request_not_pending",
                    "平局申请已处理或已过期");
            row.Status = status;
            row.RespondedAt = now;
            row.History.Add(GovernanceAudit(responder, accept ? "accepted" : "rejected",
                "pending", status, null, now));
            Save();
            return DrawView(row);
        }
    }

    internal L12MatchDrawRequestView BeginMatchDrawAcceptance(
        string matchId, string requestId, L12AccountView responder, DateTimeOffset now)
    {
        lock (_gate)
        {
            ExpireMatchDrawRequestsLocked(matchId, now);
            var row = RequireDrawRequestLocked(matchId, requestId);
            if (row.ResponderId != responder.Id)
                throw new L12MatchGovernanceConflictException("draw_response_forbidden",
                    "只有平局申请的接收方可以处理");
            if (row.Status is "accepted" or "accepting") return DrawView(row);
            if (row.Status != "pending")
                throw new L12MatchGovernanceConflictException("draw_request_not_pending",
                    "平局申请已处理或已过期");
            row.Status = "accepting";
            row.RespondedAt = now;
            row.History.Add(GovernanceAudit(responder, "accept-started", "pending", "accepting",
                null, now));
            Save();
            return DrawView(row);
        }
    }

    internal void AbortMatchDrawAcceptance(string matchId, string requestId, DateTimeOffset now,
        string reason)
    {
        lock (_gate)
        {
            var row = _data.MatchDrawRequests.FirstOrDefault(item => item.MatchId == matchId
                && item.Id == requestId && item.Status == "accepting");
            if (row is null) return;
            row.Status = row.ExpiresAt <= now ? "expired" : "pending";
            row.RespondedAt = row.Status == "expired" ? now : null;
            row.History.Add(SystemGovernanceAudit("accept-aborted", "accepting", row.Status,
                NormalizeOptionalGovernanceText(reason, 2000), now));
            Save();
        }
    }

    internal void CancelOpenMatchDrawRequests(string matchId, DateTimeOffset now, string reason)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var row in _data.MatchDrawRequests.Where(item => item.MatchId == matchId
                         && item.Status is "pending" or "accepting"))
            {
                var previous = row.Status;
                row.Status = "cancelled";
                row.RespondedAt ??= now;
                row.History.Add(SystemGovernanceAudit("cancelled", previous, "cancelled",
                    NormalizeOptionalGovernanceText(reason, 2000), now));
                changed = true;
            }
            if (changed) Save();
        }
    }

    internal void FinalizeAgreedDrawFromSettlement(string matchId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var row = _data.MatchDrawRequests.LastOrDefault(item => item.MatchId == matchId
                && item.Status is "pending" or "accepting" or "accepted");
            if (row is null || row.Status == "accepted") return;
            var previous = row.Status;
            row.Status = "accepted";
            row.RespondedAt ??= now;
            row.History.Add(SystemGovernanceAudit("settlement-finalized", previous, "accepted",
                "ranked outbox applied", now));
            Save();
        }
    }

    internal L12MatchDrawRequestClientView? MatchDrawRequestForClient(
        string matchId, string viewerAccountId, DateTimeOffset now)
    {
        lock (_gate)
        {
            ExpireMatchDrawRequestsLocked(matchId, now);
            var row = _data.MatchDrawRequests.LastOrDefault(item => item.MatchId == matchId);
            if (row is null || viewerAccountId != row.RequesterId && viewerAccountId != row.ResponderId)
                return null;
            return new L12MatchDrawRequestClientView(row.Id, row.MatchId, row.RequesterName,
                row.ResponderName, row.Reason, row.Status, viewerAccountId == row.RequesterId,
                viewerAccountId == row.ResponderId && row.Status == "pending", row.RequestedAt,
                row.ExpiresAt, row.RespondedAt);
        }
    }

    internal L12PlayerMatchReportView CreatePlayerMatchReport(
        string reportId, string matchId, string roomCode, string modeId,
        L12AccountView reporter, L12AccountView reported, string description, DateTimeOffset now)
    {
        reportId = NormalizeGovernanceId(reportId, "report");
        description = NormalizeGovernanceText(description, 5000, "请填写举报内容");
        lock (_gate)
        {
            var repeated = _data.PlayerMatchReports.FirstOrDefault(row => row.Id == reportId);
            if (repeated is not null)
            {
                if (repeated.MatchId != matchId || repeated.ReporterId != reporter.Id
                    || repeated.ReportedId != reported.Id || repeated.Description != description)
                    throw new L12MatchGovernanceConflictException("player_report_id_conflict",
                        "举报标识已被其他请求使用");
                return ReportView(repeated);
            }
            var row = new PlayerMatchReportRow
            {
                Id = reportId,
                MatchId = matchId,
                RoomCode = roomCode,
                ModeId = modeId,
                ReporterId = reporter.Id,
                ReporterName = reporter.Username,
                ReportedId = reported.Id,
                ReportedName = reported.Username,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
            };
            row.History.Add(GovernanceAudit(reporter, "submitted", null, "new", null, now));
            _data.PlayerMatchReports.Add(row);
            Save();
            return ReportView(row);
        }
    }

    public IReadOnlyList<L12MatchDrawRequestView> MatchDrawRequests(
        L12AccountView actor, string? status = null, string? search = null)
    {
        EnsureMatchGovernancePermission(actor, L12Permission.AdminMatchGovernanceRead);
        lock (_gate)
        {
            return _data.MatchDrawRequests
                .Where(row => string.IsNullOrWhiteSpace(status)
                    || row.Status.Equals(status, StringComparison.OrdinalIgnoreCase)
                    || row.AdminStatus.Equals(status, StringComparison.OrdinalIgnoreCase))
                .Where(row => MatchesGovernanceSearch(search, row.Id, row.MatchId, row.RoomCode,
                    row.RequesterName, row.ResponderName, row.Reason))
                .OrderByDescending(row => row.RequestedAt).Take(500).Select(DrawView).ToArray();
        }
    }

    public IReadOnlyList<L12PlayerMatchReportView> PlayerMatchReports(
        L12AccountView actor, string? status = null, string? search = null)
    {
        EnsureMatchGovernancePermission(actor, L12Permission.AdminMatchGovernanceRead);
        lock (_gate)
        {
            return _data.PlayerMatchReports
                .Where(row => string.IsNullOrWhiteSpace(status)
                    || row.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                .Where(row => MatchesGovernanceSearch(search, row.Id, row.MatchId, row.RoomCode,
                    row.ReporterName, row.ReportedName, row.Description))
                .OrderByDescending(row => row.CreatedAt).Take(500).Select(ReportView).ToArray();
        }
    }

    public L12MatchDrawRequestView UpdateMatchDrawRequest(
        L12AccountView actor, string id, L12MatchGovernanceUpdate update, L12AdminAuditContext context)
    {
        EnsureMatchGovernancePermission(actor, L12Permission.AdminMatchGovernanceWrite);
        lock (_gate)
        {
            var row = _data.MatchDrawRequests.FirstOrDefault(item => item.Id == id)
                ?? throw new KeyNotFoundException("平局申请记录不存在");
            var status = NormalizeAdminGovernanceStatus(update.Status);
            var previous = row.AdminStatus;
            row.AdminStatus = status;
            row.AdminNotes = NormalizeOptionalGovernanceText(update.AdminNotes, 5000);
            row.History.Add(GovernanceAudit(actor, "admin-updated", previous, status,
                NormalizeOptionalGovernanceText(update.Comment, 2000), DateTimeOffset.UtcNow));
            AddAdminAudit(actor, "match-governance", "draw-update", $"draw:{id}", previous, status,
                row.AdminNotes, context);
            Save();
            return DrawView(row);
        }
    }

    public L12PlayerMatchReportView UpdatePlayerMatchReport(
        L12AccountView actor, string id, L12MatchGovernanceUpdate update, L12AdminAuditContext context)
    {
        EnsureMatchGovernancePermission(actor, L12Permission.AdminMatchGovernanceWrite);
        lock (_gate)
        {
            var row = _data.PlayerMatchReports.FirstOrDefault(item => item.Id == id)
                ?? throw new KeyNotFoundException("玩家举报记录不存在");
            var status = NormalizeAdminGovernanceStatus(update.Status);
            var previous = row.Status;
            row.Status = status;
            row.AdminNotes = NormalizeOptionalGovernanceText(update.AdminNotes, 5000);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.History.Add(GovernanceAudit(actor, "admin-updated", previous, status,
                NormalizeOptionalGovernanceText(update.Comment, 2000), row.UpdatedAt));
            AddAdminAudit(actor, "match-governance", "report-update", $"report:{id}", previous, status,
                row.AdminNotes, context);
            Save();
            return ReportView(row);
        }
    }

    private void ExpireMatchDrawRequestsLocked(string matchId, DateTimeOffset now)
    {
        var changed = false;
        foreach (var row in _data.MatchDrawRequests.Where(item => item.MatchId == matchId
                     && item.Status is "pending" or "accepting" && item.ExpiresAt <= now))
        {
            var previous = row.Status;
            row.Status = "expired";
            row.RespondedAt = now;
            row.History.Add(SystemGovernanceAudit("expired", previous, "expired", null, now));
            changed = true;
        }
        if (changed) Save();
    }

    private MatchDrawRequestRow RequireDrawRequestLocked(string matchId, string requestId)
        => _data.MatchDrawRequests.FirstOrDefault(row => row.Id == requestId && row.MatchId == matchId)
           ?? throw new L12MatchGovernanceConflictException("draw_request_not_found",
               "平局申请不存在或已失效");

    private static void EnsureMatchGovernancePermission(L12AccountView actor, L12Permission permission)
    {
        if (!L12Authorization.HasPermission(actor, permission))
            throw new UnauthorizedAccessException("当前账号没有对局治理权限");
    }

    private static string NormalizeGovernanceId(string value, string kind)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 8 or > 80
            || normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException($"{kind} 请求标识无效");
        return normalized;
    }

    private static string NormalizeGovernanceText(string? value, int maximum, string emptyMessage)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException(emptyMessage);
        if (normalized.Length > maximum) throw new ArgumentException($"内容不得超过 {maximum} 个字符");
        return normalized;
    }

    private static string? NormalizeOptionalGovernanceText(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum) throw new ArgumentException($"内容不得超过 {maximum} 个字符");
        return normalized;
    }

    private static string NormalizeAdminGovernanceStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized is not ("new" or "reviewing" or "resolved" or "closed"))
            throw new ArgumentException("无效的对局治理状态");
        return normalized;
    }

    private static bool MatchesGovernanceSearch(string? search, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var query = search.Trim();
        return values.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static MatchGovernanceAuditRow GovernanceAudit(L12AccountView actor, string action,
        string? from, string? to, string? comment, DateTimeOffset now) => new()
    {
        ActorId = actor.Id,
        ActorName = actor.Username,
        Action = action,
        FromValue = from,
        ToValue = to,
        Comment = comment,
        CreatedAt = now,
    };

    private static MatchGovernanceAuditRow SystemGovernanceAudit(string action, string? from,
        string? to, string? comment, DateTimeOffset now) => new()
    {
        ActorId = "system",
        ActorName = "系统",
        Action = action,
        FromValue = from,
        ToValue = to,
        Comment = comment,
        CreatedAt = now,
    };

    private static L12MatchGovernanceAuditView GovernanceAuditView(MatchGovernanceAuditRow row)
        => new(row.Id, row.ActorId, row.ActorName, row.Action, row.FromValue, row.ToValue,
            row.Comment, row.CreatedAt);

    private static L12MatchDrawRequestView DrawView(MatchDrawRequestRow row)
        => new(row.Id, row.MatchId, row.RoomCode, row.ModeId, row.RequesterId, row.RequesterName,
            row.ResponderId, row.ResponderName, row.Reason, row.Status, row.AdminStatus,
            row.AdminNotes, row.RequestedAt, row.ExpiresAt, row.RespondedAt,
            row.History.OrderByDescending(item => item.CreatedAt).Select(GovernanceAuditView).ToArray());

    private static L12PlayerMatchReportView ReportView(PlayerMatchReportRow row)
        => new(row.Id, row.MatchId, row.RoomCode, row.ModeId, row.ReporterId, row.ReporterName,
            row.ReportedId, row.ReportedName, row.Description, row.Status, row.AdminNotes,
            row.CreatedAt, row.UpdatedAt,
            row.History.OrderByDescending(item => item.CreatedAt).Select(GovernanceAuditView).ToArray());
}
