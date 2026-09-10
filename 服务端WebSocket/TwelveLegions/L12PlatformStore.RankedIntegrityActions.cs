using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12RankedIntegrityActionInput(
    string RequestId,
    string Disposition,
    IReadOnlyList<string> MatchIds,
    IReadOnlyList<string> RestrictedAccountIds,
    int? RestrictionDays,
    string Evidence,
    string Reason,
    string? RevokesDecisionId = null);

public sealed record L12RankedIntegrityAccountEffectView(
    string AccountId,
    string Username,
    int ScoreDelta,
    DateTimeOffset? RestrictionUntil,
    string RewardOutcome,
    string? BlockedReason);

public sealed record L12RankedIntegrityActionPreviewView(
    long Revision,
    string RequestId,
    string Disposition,
    bool CanConfirm,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> MatchIds,
    IReadOnlyList<L12RankedIntegrityAccountEffectView> AccountEffects);

public sealed record L12RankedIntegrityDecisionView(
    string DecisionId,
    string RequestId,
    long Revision,
    string Disposition,
    string EffectiveDisposition,
    IReadOnlyList<string> MatchIds,
    IReadOnlyList<string> RestrictedAccountIds,
    int? RestrictionDays,
    string Evidence,
    string Reason,
    string ActorId,
    string ActorName,
    DateTimeOffset CreatedAt,
    string? RevokesDecisionId,
    string? RevokedByDecisionId,
    IReadOnlyList<L12RankedIntegrityAccountEffectView> AccountEffects);

public sealed record L12RankedIntegrityDecisionPageView(
    IReadOnlyList<L12RankedIntegrityDecisionView> Items,
    string? NextCursor);

public sealed record L12RankedIntegrityNotificationView(
    string Id,
    string DecisionId,
    IReadOnlyList<string> MatchIds,
    string Outcome,
    string Reason,
    DateTimeOffset DecidedAt,
    int ScoreDelta,
    DateTimeOffset? RestrictionUntil,
    string AppealGuidance,
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt,
    string? RelatedDecisionId);

public sealed record L12RankedIntegrityNotificationPageView(
    IReadOnlyList<L12RankedIntegrityNotificationView> Items,
    string? NextCursor,
    int UnreadCount);

public sealed record L12RankedIntegrityAppealView(
    string Id,
    string DecisionId,
    string AccountId,
    string Username,
    string Statement,
    string Status,
    string? Reply,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record L12RankedIntegrityAppealPageView(
    IReadOnlyList<L12RankedIntegrityAppealView> Items,
    string? NextCursor);

public sealed record L12RankedIntegrityAppealReviewInput(
    string RequestId,
    long ExpectedRevision,
    string Status,
    string Reply);

public sealed class L12RankedIntegrityActionException : InvalidOperationException
{
    public string Code { get; }

    public L12RankedIntegrityActionException(string code, string message) : base(message)
        => Code = code;
}

public sealed partial class L12PlatformStore
{
    private const string RankedAppealGuidance = "如有异议，可在“我的-排位处置记录”提交申诉；申诉不会自动解除限制。";
    private static readonly string[] RankedIntegrityDispositions =
        ["normal", "insufficient", "system-error", "confirmed", "review", "revoked"];
    private static readonly string[] RankedIntegrityAppealStatuses =
        ["open", "reviewing", "answered", "closed"];

    private sealed class RankedIntegrityDecisionRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RequestId { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public long Revision { get; set; }
        public string Disposition { get; set; } = string.Empty;
        public List<string> MatchIds { get; set; } = [];
        public List<string> RestrictedAccountIds { get; set; } = [];
        public int? RestrictionDays { get; set; }
        public string Evidence { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string? RevokesDecisionId { get; set; }
        public List<RankedIntegrityAccountEffectRow> AccountEffects { get; set; } = [];
    }

    private sealed class RankedIntegrityAccountEffectRow
    {
        public string AccountId { get; set; } = string.Empty;
        public int ScoreDelta { get; set; }
        public DateTimeOffset? RestrictionUntil { get; set; }
        public string RewardOutcome { get; set; } = "unchanged";
        public string? BlockedReason { get; set; }
    }

    private sealed class RankedProfileSnapshotRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string? Faction { get; set; }
        public double HiddenRating { get; set; }
        public int SevenValue { get; set; }
        public int PlacementPlayed { get; set; }
        public int PlacementWins { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int WinStreak { get; set; }
        public int LossStreak { get; set; }
        public int HighestFloor { get; set; }
        public bool ReachedHighestTier { get; set; }
        public string? SelectedMasterTitle { get; set; }
    }

    private sealed class RankedHeldRewardRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string MatchId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string FirstAccountId { get; set; } = string.Empty;
        public string SecondAccountId { get; set; } = string.Empty;
        public int Winner { get; set; }
        public string FirstMasterId { get; set; } = string.Empty;
        public string SecondMasterId { get; set; } = string.Empty;
        public int FinalRound { get; set; }
        public string ConclusionKind { get; set; } = string.Empty;
        public RankedProfileSnapshotRow FirstBefore { get; set; } = new();
        public RankedProfileSnapshotRow FirstAfter { get; set; } = new();
        public RankedProfileSnapshotRow SecondBefore { get; set; } = new();
        public RankedProfileSnapshotRow SecondAfter { get; set; } = new();
        public DateTimeOffset CooldownUntil { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class RankedSettlementProfileFactRow
    {
        public string MatchId { get; set; } = string.Empty;
        public string FirstAccountId { get; set; } = string.Empty;
        public string SecondAccountId { get; set; } = string.Empty;
        public RankedProfileSnapshotRow FirstBefore { get; set; } = new();
        public RankedProfileSnapshotRow FirstAfter { get; set; } = new();
        public RankedProfileSnapshotRow SecondBefore { get; set; } = new();
        public RankedProfileSnapshotRow SecondAfter { get; set; } = new();
        public bool AppliedInitially { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class RankedIntegrityCorrectionRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DecisionId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public List<string> MatchIds { get; set; } = [];
        public RankedProfileSnapshotRow Before { get; set; } = new();
        public RankedProfileSnapshotRow After { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class RankedIntegrityNotificationRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AccountId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public List<string> MatchIds { get; set; } = [];
        public string Outcome { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset DecidedAt { get; set; }
        public int ScoreDelta { get; set; }
        public DateTimeOffset? RestrictionUntil { get; set; }
        public string AppealGuidance { get; set; } = RankedAppealGuidance;
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public string? RelatedDecisionId { get; set; }
    }

    private sealed class RankedIntegrityAppealRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DecisionId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public string Statement { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<RankedIntegrityAppealEventRow> Events { get; set; } = [];
    }

    private sealed class RankedIntegrityAppealEventRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RequestId { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Reply { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed record RankedIntegrityNormalizedAction(
        string RequestId,
        string RequestFingerprint,
        string Disposition,
        IReadOnlyList<string> MatchIds,
        IReadOnlyList<string> RestrictedAccountIds,
        int? RestrictionDays,
        string Evidence,
        string Reason,
        string? RevokesDecisionId);

    private sealed record RankedProfileTransition(
        string AccountId,
        RankedProfileSnapshotRow Before,
        RankedProfileSnapshotRow After,
        string Kind,
        IReadOnlyList<string> MatchIds);

    private sealed class RankedIntegrityActionPlan
    {
        public required RankedIntegrityNormalizedAction Input { get; init; }
        public List<string> BlockingReasons { get; } = [];
        public List<RankedProfileTransition> Transitions { get; } = [];
        public HashSet<string> ReleasedHeldMatchIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> VoidedAppliedMatchIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public RankedIntegrityDecisionRow? RevokedDecision { get; set; }
    }

    public L12RankedIntegrityActionPreviewView PreviewRankedIntegrityAction(L12AccountView actor,
        L12RankedIntegrityActionInput input)
    {
        EnsureRankedIntegrityPermission(actor, L12Permission.AdminMatchGovernanceWrite);
        lock (_gate)
        {
            var normalized = NormalizeRankedIntegrityActionLocked(input);
            var replay = _data.RankedIntegrityDecisions.FirstOrDefault(row =>
                row.RequestId.Equals(normalized.RequestId, StringComparison.Ordinal));
            if (replay is not null)
            {
                EnsureRequestFingerprint(replay.RequestFingerprint, normalized.RequestFingerprint);
                return new L12RankedIntegrityActionPreviewView(_data.RankedIntegrityRevision,
                    normalized.RequestId, normalized.Disposition, true, [], normalized.MatchIds,
                    replay.AccountEffects.Select(AccountEffectViewWithName).ToArray());
            }
            var plan = BuildRankedIntegrityActionPlanLocked(normalized, DateTimeOffset.UtcNow);
            return PreviewViewLocked(plan);
        }
    }

    public L12RankedIntegrityDecisionView ConfirmRankedIntegrityAction(L12AccountView actor,
        L12RankedIntegrityActionInput input, long expectedRevision, L12AdminAuditContext context)
    {
        EnsureRankedIntegrityPermission(actor, L12Permission.AdminMatchGovernanceWrite);
        lock (_gate)
        {
            var normalized = NormalizeRankedIntegrityActionLocked(input);
            var replay = _data.RankedIntegrityDecisions.FirstOrDefault(row =>
                row.RequestId.Equals(normalized.RequestId, StringComparison.Ordinal));
            if (replay is not null)
            {
                EnsureRequestFingerprint(replay.RequestFingerprint, normalized.RequestFingerprint);
                return RankedIntegrityDecisionViewLocked(replay);
            }
            if (expectedRevision != _data.RankedIntegrityRevision)
                throw new L12RankedIntegrityActionException("ranked_integrity_revision_conflict",
                    "排位处置记录已变化，请重新预览后确认");

            var now = DateTimeOffset.UtcNow;
            var plan = BuildRankedIntegrityActionPlanLocked(normalized, now);
            if (plan.BlockingReasons.Count > 0)
                throw new L12RankedIntegrityActionException("ranked_integrity_action_blocked",
                    string.Join("；", plan.BlockingReasons));

            var decision = new RankedIntegrityDecisionRow
            {
                RequestId = normalized.RequestId,
                RequestFingerprint = normalized.RequestFingerprint,
                Revision = checked(_data.RankedIntegrityRevision + 1),
                Disposition = normalized.Disposition,
                MatchIds = normalized.MatchIds.ToList(),
                RestrictedAccountIds = normalized.RestrictedAccountIds.ToList(),
                RestrictionDays = normalized.RestrictionDays,
                Evidence = normalized.Evidence,
                Reason = normalized.Reason,
                ActorId = actor.Id,
                ActorName = actor.Username,
                CreatedAt = now,
                RevokesDecisionId = normalized.RevokesDecisionId,
            };

            ApplyRankedIntegrityPlanLocked(plan, decision, now);
            decision.AccountEffects = BuildAccountEffectsLocked(plan, now);
            _data.RankedIntegrityDecisions.Add(decision);
            _data.RankedIntegrityRevision = decision.Revision;
            AddRankedIntegrityNotificationsLocked(decision);
            AddAdminAudit(actor, "ranked-integrity", $"decision-{decision.Disposition}",
                $"ranked-integrity:{decision.Id}", null, decision.Id, decision.Reason,
                context with { IdempotencyKey = decision.RequestId, ExpectedVersion = expectedRevision,
                    Reason = decision.Reason, Outcome = "succeeded" });
            Save();
            return RankedIntegrityDecisionViewLocked(decision);
        }
    }

    public L12RankedIntegrityDecisionPageView RankedIntegrityDecisions(L12AccountView actor,
        string? cursor = null, int limit = 20)
    {
        EnsureRankedIntegrityPermission(actor, L12Permission.AdminAuditRead);
        lock (_gate)
        {
            var rows = PageAfter(_data.RankedIntegrityDecisions.OrderByDescending(row => row.Revision)
                .ThenByDescending(row => row.Id, StringComparer.Ordinal), cursor,
                row => row.Id, Math.Clamp(limit, 1, 100), out var next);
            return new(rows.Select(RankedIntegrityDecisionViewLocked).ToArray(), next);
        }
    }

    public L12RankedIntegrityNotificationPageView RankedIntegrityNotifications(L12AccountView actor,
        string? cursor = null, int limit = 20, bool unreadOnly = false)
    {
        lock (_gate)
        {
            RequireActiveRankedIntegrityActorLocked(actor);
            var all = _data.RankedIntegrityNotifications.Where(row =>
                row.AccountId.Equals(actor.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var unreadCount = all.Count(row => row.AcknowledgedAt is null);
            var source = all.Where(row => !unreadOnly || row.AcknowledgedAt is null)
                .OrderByDescending(row => row.DecidedAt).ThenByDescending(row => row.Id,
                    StringComparer.Ordinal);
            var rows = PageAfter(source, cursor, row => row.Id, Math.Clamp(limit, 1, 100), out var next);
            return new(rows.Select(RankedIntegrityNotificationViewLocked).ToArray(), next, unreadCount);
        }
    }

    public bool AcknowledgeRankedIntegrityNotification(L12AccountView actor, string notificationId)
    {
        lock (_gate)
        {
            RequireActiveRankedIntegrityActorLocked(actor);
            var row = _data.RankedIntegrityNotifications.FirstOrDefault(item =>
                item.Id.Equals(notificationId?.Trim(), StringComparison.Ordinal)
                && item.AccountId.Equals(actor.Id, StringComparison.OrdinalIgnoreCase));
            if (row is null) return false;
            if (row.AcknowledgedAt is not null) return true;
            row.AcknowledgedAt = DateTimeOffset.UtcNow;
            Save();
            return true;
        }
    }

    public L12RankedIntegrityAppealView SubmitRankedIntegrityAppeal(L12AccountView actor,
        string decisionId, string requestId, string statement)
    {
        lock (_gate)
        {
            RequireActiveRankedIntegrityActorLocked(actor);
            var normalizedRequestId = RequireRankedIntegrityRequestId(requestId);
            var normalizedStatement = RequireRankedIntegrityText(statement, "申诉说明", 1000);
            var normalizedDecisionId = decisionId?.Trim() ?? string.Empty;
            var decision = _data.RankedIntegrityDecisions.FirstOrDefault(row =>
                row.Id.Equals(normalizedDecisionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new L12RankedIntegrityActionException("ranked_integrity_decision_not_found",
                    "排位处置记录不存在");
            if (!decision.AccountEffects.Any(effect => effect.AccountId.Equals(actor.Id,
                    StringComparison.OrdinalIgnoreCase)))
                throw new L12RankedIntegrityActionException("ranked_integrity_appeal_forbidden",
                    "只能申诉与本人有关的排位处置");

            var fingerprint = RankedIntegrityFingerprint($"appeal\n{decision.Id}\n{actor.Id}\n{normalizedStatement}");
            var replay = _data.RankedIntegrityAppeals.FirstOrDefault(row =>
                row.RequestId.Equals(normalizedRequestId, StringComparison.Ordinal));
            if (replay is not null)
            {
                EnsureRequestFingerprint(replay.RequestFingerprint, fingerprint);
                return RankedIntegrityAppealViewLocked(replay);
            }
            if (_data.RankedIntegrityAppeals.Any(row =>
                    row.DecisionId.Equals(decision.Id, StringComparison.OrdinalIgnoreCase)
                    && row.AccountId.Equals(actor.Id, StringComparison.OrdinalIgnoreCase)
                    && !CurrentAppealStatus(row).Equals("closed", StringComparison.Ordinal)))
                throw new L12RankedIntegrityActionException("ranked_integrity_appeal_already_open",
                    "该处置已有未关闭申诉，请等待复核");

            var appeal = new RankedIntegrityAppealRow
            {
                DecisionId = decision.Id,
                AccountId = actor.Id,
                RequestId = normalizedRequestId,
                RequestFingerprint = fingerprint,
                Statement = normalizedStatement,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _data.RankedIntegrityAppeals.Add(appeal);
            Save();
            return RankedIntegrityAppealViewLocked(appeal);
        }
    }

    public L12RankedIntegrityAppealPageView RankedIntegrityAppealsForPlayer(L12AccountView actor,
        string? cursor = null, int limit = 20)
    {
        lock (_gate)
        {
            RequireActiveRankedIntegrityActorLocked(actor);
            var source = _data.RankedIntegrityAppeals.Where(row => row.AccountId.Equals(actor.Id,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(AppealUpdatedAt).ThenByDescending(row => row.Id, StringComparer.Ordinal);
            var rows = PageAfter(source, cursor, row => row.Id, Math.Clamp(limit, 1, 100), out var next);
            return new(rows.Select(RankedIntegrityAppealViewLocked).ToArray(), next);
        }
    }

    public L12RankedIntegrityAppealPageView RankedIntegrityAppeals(L12AccountView actor,
        string? decisionId = null, string? status = null, string? cursor = null, int limit = 20)
    {
        EnsureRankedIntegrityPermission(actor, L12Permission.AdminAuditRead);
        lock (_gate)
        {
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
            if (normalizedStatus is not null && !RankedIntegrityAppealStatuses.Contains(normalizedStatus))
                throw new L12RankedIntegrityActionException("ranked_integrity_appeal_status_invalid",
                    "申诉状态无效");
            var source = _data.RankedIntegrityAppeals.Where(row =>
                    (string.IsNullOrWhiteSpace(decisionId) || row.DecisionId.Equals(decisionId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    && (normalizedStatus is null || CurrentAppealStatus(row) == normalizedStatus))
                .OrderByDescending(AppealUpdatedAt).ThenByDescending(row => row.Id, StringComparer.Ordinal);
            var rows = PageAfter(source, cursor, row => row.Id, Math.Clamp(limit, 1, 100), out var next);
            return new(rows.Select(RankedIntegrityAppealViewLocked).ToArray(), next);
        }
    }

    public L12RankedIntegrityAppealView ReviewRankedIntegrityAppeal(L12AccountView actor,
        string appealId, L12RankedIntegrityAppealReviewInput input, L12AdminAuditContext context)
    {
        EnsureRankedIntegrityPermission(actor, L12Permission.AdminMatchGovernanceWrite);
        lock (_gate)
        {
            var appeal = _data.RankedIntegrityAppeals.FirstOrDefault(row =>
                row.Id.Equals(appealId?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new L12RankedIntegrityActionException("ranked_integrity_appeal_not_found",
                    "申诉不存在");
            var requestId = RequireRankedIntegrityRequestId(input.RequestId);
            var status = input.Status?.Trim().ToLowerInvariant() ?? string.Empty;
            if (status is not ("reviewing" or "answered" or "closed"))
                throw new L12RankedIntegrityActionException("ranked_integrity_appeal_status_invalid",
                    "管理员复核状态只能是 reviewing、answered 或 closed");
            var reply = RequireRankedIntegrityText(input.Reply, "复核回复", 1000);
            var fingerprint = RankedIntegrityFingerprint($"appeal-review\n{appeal.Id}\n{status}\n{reply}");
            var replay = _data.RankedIntegrityAppeals.SelectMany(row => row.Events)
                .FirstOrDefault(row => row.RequestId.Equals(requestId, StringComparison.Ordinal));
            if (replay is not null)
            {
                EnsureRequestFingerprint(replay.RequestFingerprint, fingerprint);
                return RankedIntegrityAppealViewLocked(appeal);
            }
            var revision = 1L + appeal.Events.Count;
            if (input.ExpectedRevision != revision)
                throw new L12RankedIntegrityActionException("ranked_integrity_appeal_revision_conflict",
                    "申诉已更新，请刷新后重试");

            var now = DateTimeOffset.UtcNow;
            appeal.Events.Add(new RankedIntegrityAppealEventRow
            {
                RequestId = requestId,
                RequestFingerprint = fingerprint,
                ActorId = actor.Id,
                ActorName = actor.Username,
                Status = status,
                Reply = reply,
                CreatedAt = now,
            });
            _data.RankedIntegrityNotifications.Add(new RankedIntegrityNotificationRow
            {
                AccountId = appeal.AccountId,
                DecisionId = appeal.DecisionId,
                MatchIds = _data.RankedIntegrityDecisions.First(row => row.Id == appeal.DecisionId)
                    .MatchIds.ToList(),
                Outcome = $"appeal-{status}",
                Reason = reply,
                DecidedAt = now,
                AppealGuidance = RankedAppealGuidance,
            });
            AddAdminAudit(actor, "ranked-integrity", $"appeal-{status}",
                $"ranked-integrity-appeal:{appeal.Id}", null, status, reply,
                context with { IdempotencyKey = requestId, ExpectedVersion = input.ExpectedRevision,
                    Reason = reply, Outcome = "succeeded" });
            Save();
            return RankedIntegrityAppealViewLocked(appeal);
        }
    }

    internal string? RankedEntryBlock(string accountId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var activeRestriction = _data.RankedIntegrityDecisions
                .Where(row => row.Disposition == "confirmed" && !IsDecisionRevokedLocked(row.Id))
                .SelectMany(row => row.AccountEffects)
                .Where(effect => effect.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                    && effect.RestrictionUntil > now)
                .OrderByDescending(effect => effect.RestrictionUntil).FirstOrDefault();
            if (activeRestriction?.RestrictionUntil is { } restrictedUntil)
                return $"该账号排位资格被限制至 {restrictedUntil.ToOffset(TimeSpan.FromHours(8)):yyyy-MM-dd HH:mm}（UTC+8）";

            var cooldown = _data.RankedHeldRewards.Where(row =>
                    (row.FirstAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                        || row.SecondAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase))
                    && row.CooldownUntil > now && IsRankedMatchExcludedLocked(row.MatchId))
                .OrderByDescending(row => row.CooldownUntil).FirstOrDefault();
            return cooldown is null ? null
                : $"异常极短排位奖励正在暂扣复核，请于 {cooldown.CooldownUntil.ToOffset(TimeSpan.FromHours(8)):HH:mm}（UTC+8）后重试";
        }
    }

    public IReadOnlyCollection<string> RankedIntegrityProtectedMatchIds()
    {
        lock (_gate)
        {
            var protectedIds = _data.RankedHeldRewards.Select(row => row.MatchId)
                .Concat(_data.RankedIntegrityDecisions.SelectMany(row => row.MatchIds))
                .Concat(_data.RankedIntegrityAppeals.Where(row => CurrentAppealStatus(row) != "closed")
                    .SelectMany(row => _data.RankedIntegrityDecisions
                        .Where(decision => decision.Id == row.DecisionId).SelectMany(decision => decision.MatchIds)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return protectedIds.ToArray();
        }
    }

    public IReadOnlyCollection<string> RankedIntegrityExcludedMatchIds()
    {
        lock (_gate)
        {
            return _data.RankedIntegrityAudits.Select(row => row.MatchId)
                .Where(IsRankedMatchExcludedLocked)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private RankedIntegrityNormalizedAction NormalizeRankedIntegrityActionLocked(
        L12RankedIntegrityActionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var requestId = RequireRankedIntegrityRequestId(input.RequestId);
        var disposition = input.Disposition?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!RankedIntegrityDispositions.Contains(disposition))
            throw new L12RankedIntegrityActionException("ranked_integrity_disposition_invalid",
                "排位处置结论无效");
        if (input.MatchIds is null || input.MatchIds.Count is < 1 or > 50)
            throw new L12RankedIntegrityActionException("ranked_integrity_match_scope_invalid",
                "一次处置必须包含 1 至 50 个确切 matchId");

        var matchIds = new List<string>();
        foreach (var requested in input.MatchIds)
        {
            var normalized = requested?.Trim() ?? string.Empty;
            var audit = _data.RankedIntegrityAudits.FirstOrDefault(row =>
                row.MatchId.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                ?? throw new L12RankedIntegrityActionException("ranked_integrity_match_not_found",
                    $"找不到排位完整性记录：{normalized}");
            if (!matchIds.Contains(audit.MatchId, StringComparer.OrdinalIgnoreCase))
                matchIds.Add(audit.MatchId);
        }
        if (matchIds.Count != input.MatchIds.Count)
            throw new L12RankedIntegrityActionException("ranked_integrity_match_scope_invalid",
                "matchId 不能重复");
        matchIds.Sort(StringComparer.OrdinalIgnoreCase);

        var participants = _data.RankedIntegrityAudits.Where(row => matchIds.Contains(row.MatchId,
                StringComparer.OrdinalIgnoreCase))
            .SelectMany(row => new[] { row.FirstAccountId, row.SecondAccountId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restricted = (input.RestrictedAccountIds ?? []).Select(value => value?.Trim() ?? string.Empty)
            .ToArray();
        if (restricted.Any(string.IsNullOrWhiteSpace)
            || restricted.Distinct(StringComparer.OrdinalIgnoreCase).Count() != restricted.Length
            || restricted.Any(accountId => !participants.Contains(accountId)))
            throw new L12RankedIntegrityActionException("ranked_integrity_restriction_scope_invalid",
                "限制账号必须是不重复的所选对局参与者账号 ID");
        Array.Sort(restricted, StringComparer.OrdinalIgnoreCase);

        if (disposition == "confirmed")
        {
            if (restricted.Length == 0 && input.RestrictionDays is not null)
                throw new L12RankedIntegrityActionException("ranked_integrity_restriction_invalid",
                    "没有限制账号时不能设置限制天数");
            if (restricted.Length > 0 && input.RestrictionDays is not (>= 1 and <= 3650))
                throw new L12RankedIntegrityActionException("ranked_integrity_restriction_invalid",
                    "排位限制天数必须是 1 至 3650 的整数");
        }
        else if (restricted.Length > 0 || input.RestrictionDays is not null)
        {
            throw new L12RankedIntegrityActionException("ranked_integrity_restriction_invalid",
                "只有 confirmed 处置可以设置排位限制");
        }

        var evidence = RequireRankedIntegrityText(input.Evidence, "证据摘要", 2000);
        var reason = RequireRankedIntegrityText(input.Reason, "处置理由", 1000);
        var revokesDecisionId = string.IsNullOrWhiteSpace(input.RevokesDecisionId)
            ? null : input.RevokesDecisionId.Trim();
        if (disposition == "revoked")
        {
            var target = _data.RankedIntegrityDecisions.FirstOrDefault(row =>
                row.Id.Equals(revokesDecisionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new L12RankedIntegrityActionException("ranked_integrity_revoke_target_invalid",
                    "撤销目标处置不存在");
            if (target.Disposition is not ("confirmed" or "system-error")
                || IsDecisionRevokedLocked(target.Id))
                throw new L12RankedIntegrityActionException("ranked_integrity_revoke_target_invalid",
                    "只能撤销尚未撤销的 confirmed 或 system-error 处置");
            if (!target.MatchIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(matchIds))
                throw new L12RankedIntegrityActionException("ranked_integrity_revoke_scope_mismatch",
                    "撤销处置的 matchId 必须与原处置完全一致");
            revokesDecisionId = target.Id;
        }
        else if (revokesDecisionId is not null)
        {
            throw new L12RankedIntegrityActionException("ranked_integrity_revoke_target_invalid",
                "只有 revoked 处置可以引用原处置");
        }

        var fingerprintPayload = JsonSerializer.Serialize(new
        {
            requestId,
            disposition,
            matchIds,
            restrictedAccountIds = restricted,
            restrictionDays = input.RestrictionDays,
            evidence,
            reason,
            revokesDecisionId,
        });
        return new(requestId, RankedIntegrityFingerprint(fingerprintPayload), disposition, matchIds, restricted,
            input.RestrictionDays, evidence, reason, revokesDecisionId);
    }

    private RankedIntegrityActionPlan BuildRankedIntegrityActionPlanLocked(
        RankedIntegrityNormalizedAction input, DateTimeOffset now)
    {
        var plan = new RankedIntegrityActionPlan { Input = input };
        if (input.Disposition == "revoked")
        {
            plan.RevokedDecision = _data.RankedIntegrityDecisions.First(row =>
                row.Id.Equals(input.RevokesDecisionId, StringComparison.OrdinalIgnoreCase));
            BuildRevocationPlanLocked(plan);
            return plan;
        }

        foreach (var matchId in input.MatchIds)
        {
            var latest = _data.RankedIntegrityDecisions.Where(row => row.MatchIds.Contains(matchId,
                    StringComparer.OrdinalIgnoreCase) && !IsDecisionRevokedLocked(row.Id))
                .OrderByDescending(row => row.Revision).FirstOrDefault();
            if (latest is not null && latest.Disposition is not "review")
                plan.BlockingReasons.Add($"对局 {matchId} 已有生效终局处置 {latest.Disposition}");
        }
        if (plan.BlockingReasons.Count > 0) return plan;

        if (input.Disposition is "confirmed" or "system-error")
            BuildVoidAppliedRewardsPlanLocked(plan);
        else if (input.Disposition is "normal" or "insufficient")
            BuildHeldReleasePlanLocked(plan, input.MatchIds);
        return plan;
    }

    private void BuildVoidAppliedRewardsPlanLocked(RankedIntegrityActionPlan plan)
    {
        var selected = plan.Input.MatchIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appliedRows = _data.RankedSettlements.Where(row => selected.Contains(row.MatchId)
                && row.Outcome is "win" or "loss" && IsRankedMatchCurrentlyAppliedLocked(row.MatchId))
            .ToArray();

        foreach (var accountRows in appliedRows.GroupBy(row => row.AccountId,
                     StringComparer.OrdinalIgnoreCase))
        {
            var accountId = accountRows.Key;
            if (_data.RankedIntegrityCorrections.Any(row => row.AccountId.Equals(accountId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)} 已有排位修正链，拒绝猜测叠加反算");
                continue;
            }
            var profile = _data.RankedProfiles.FirstOrDefault(row => row.AccountId.Equals(accountId,
                StringComparison.OrdinalIgnoreCase));
            if (profile is null || string.IsNullOrWhiteSpace(profile.Faction))
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)} 当前排位档案不存在");
                continue;
            }
            var rows = accountRows.OrderBy(row => row.SettledAt).ThenBy(row => row.MatchId,
                StringComparer.OrdinalIgnoreCase).ToArray();
            var audits = rows.Select(row => _data.RankedIntegrityAudits.Single(audit =>
                audit.MatchId.Equals(row.MatchId, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (audits.Any(audit => !audit.SeasonId.Equals(profile.SeasonId,
                    StringComparison.OrdinalIgnoreCase))
                || rows.Any(row => !row.Faction.Equals(profile.Faction,
                    StringComparison.OrdinalIgnoreCase)))
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)} 已跨赛季或切换派系，不能安全撤销旧收益");
                continue;
            }

            var effective = _data.RankedSettlements.Where(row =>
                    row.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                    && row.Outcome is "win" or "loss" && IsRankedMatchCurrentlyAppliedLocked(row.MatchId)
                    && row.Faction.Equals(profile.Faction, StringComparison.OrdinalIgnoreCase)
                    && _data.RankedIntegrityAudits.Any(audit => audit.MatchId.Equals(row.MatchId,
                            StringComparison.OrdinalIgnoreCase)
                        && audit.SeasonId.Equals(profile.SeasonId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(row => row.SettledAt).ThenBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var segmentStart = Array.FindLastIndex(effective, row => row.PlacementPlayed == 1);
            if (segmentStart < 0)
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)} 缺少本次排位档案起点");
                continue;
            }
            var segment = effective[segmentStart..];
            if (!ValidateCurrentRankedProfileChainLocked(profile, segment, out var chainReason))
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)}：{chainReason}");
                continue;
            }
            if (rows.Length > segment.Length
                || !segment[^rows.Length..].Select(row => row.MatchId)
                    .SequenceEqual(rows.Select(row => row.MatchId), StringComparer.OrdinalIgnoreCase))
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)} 的所选对局不是当前连续最新结算后缀");
                continue;
            }

            var prefix = segment[..^rows.Length];
            var before = CaptureRankedProfile(profile);
            var exactFacts = rows.Select(row => _data.RankedSettlementProfileFacts.FirstOrDefault(fact =>
                    fact.AppliedInitially && fact.MatchId.Equals(row.MatchId,
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            RankedProfileSnapshotRow after;
            if (exactFacts.All(fact => fact is not null))
            {
                var firstFact = exactFacts[0]!;
                var exactBefore = firstFact.FirstAccountId.Equals(accountId,
                    StringComparison.OrdinalIgnoreCase) ? firstFact.FirstBefore : firstFact.SecondBefore;
                after = CloneRankedProfileSnapshot(exactBefore);
                // 称号选择是结算外的玩家偏好，不随积分处置回滚。
                after.SelectedMasterTitle = before.SelectedMasterTitle;
            }
            else
            {
                after = CloneRankedProfileSnapshot(before);
                var earliest = rows[0];
                after.SevenValue = earliest.Before;
                after.Wins -= rows.Count(row => row.Outcome == "win");
                after.Losses -= rows.Count(row => row.Outcome == "loss");
                ApplyDerivedRankedProfileState(after, prefix);
            }
            if (after.Wins < 0 || after.Losses < 0)
            {
                plan.BlockingReasons.Add($"账号 {AccountName(accountId)} 胜负计数不足以安全回滚");
                continue;
            }
            plan.Transitions.Add(new(accountId, before, after, "void-applied", rows
                .Select(row => row.MatchId).ToArray()));
            foreach (var row in rows) plan.VoidedAppliedMatchIds.Add(row.MatchId);
        }
        if (plan.BlockingReasons.Count == 0 && appliedRows.Length > 0
            && !TryApplyHistoricalHiddenRatingRollbackLocked(plan, appliedRows, out var ratingReason))
            plan.BlockingReasons.Add(ratingReason);
    }

    private bool TryApplyHistoricalHiddenRatingRollbackLocked(RankedIntegrityActionPlan plan,
        IReadOnlyList<RankedSettlementRow> selectedRows, out string reason)
    {
        var transitions = plan.Transitions.ToDictionary(row => row.AccountId,
            StringComparer.OrdinalIgnoreCase);
        var ratings = transitions.ToDictionary(item => item.Key, item => item.Value.Before.HiddenRating,
            StringComparer.OrdinalIgnoreCase);
        var matches = selectedRows.Select(row => row.MatchId).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(matchId => new
            {
                MatchId = matchId,
                SettledAt = selectedRows.Where(row => row.MatchId.Equals(matchId,
                    StringComparison.OrdinalIgnoreCase)).Max(row => row.SettledAt),
                Audit = _data.RankedIntegrityAudits.Single(row => row.MatchId.Equals(matchId,
                    StringComparison.OrdinalIgnoreCase)),
                Fact = _data.RankedSettlementProfileFacts.FirstOrDefault(row => row.MatchId.Equals(matchId,
                    StringComparison.OrdinalIgnoreCase) && row.AppliedInitially),
            })
            .OrderByDescending(row => row.SettledAt).ThenByDescending(row => row.MatchId,
                StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var match in matches)
        {
            if (!ratings.TryGetValue(match.Audit.FirstAccountId, out var firstAfter)
                || !ratings.TryGetValue(match.Audit.SecondAccountId, out var secondAfter))
            {
                reason = $"对局 {match.MatchId} 缺少双方隐藏分链";
                return false;
            }
            double firstBefore;
            double secondBefore;
            if (match.Fact is not null)
            {
                if (Math.Abs(match.Fact.FirstAfter.HiddenRating - firstAfter) > 0.0000001d
                    || Math.Abs(match.Fact.SecondAfter.HiddenRating - secondAfter) > 0.0000001d)
                {
                    reason = $"对局 {match.MatchId} 的持久化隐藏分前后快照与当前反算链冲突";
                    return false;
                }
                firstBefore = match.Fact.FirstBefore.HiddenRating;
                secondBefore = match.Fact.SecondBefore.HiddenRating;
            }
            else if (!TryReverseRankedElo(firstAfter, secondAfter, match.Audit.Winner!.Value,
                         out firstBefore, out secondBefore))
            {
                reason = $"对局 {match.MatchId} 缺少隐藏分快照且触及边界，不能精确反算";
                return false;
            }
            ratings[match.Audit.FirstAccountId] = firstBefore;
            ratings[match.Audit.SecondAccountId] = secondBefore;
        }

        foreach (var item in ratings) transitions[item.Key].After.HiddenRating = item.Value;
        reason = string.Empty;
        return true;
    }

    internal static bool TryReverseRankedElo(double firstAfter, double secondAfter, int winner,
        out double firstBefore, out double secondBefore)
    {
        firstBefore = secondBefore = 0d;
        const double epsilon = 0.0000001d;
        if (firstAfter <= 500d + epsilon || firstAfter >= 2500d - epsilon
            || secondAfter <= 500d + epsilon || secondAfter >= 2500d - epsilon)
            return false;
        var targetDifference = firstAfter - secondAfter;
        static double ProjectDifference(double beforeDifference, int winnerIndex)
        {
            var expectedFirst = 1d / (1d + Math.Pow(10d, -beforeDifference / 400d));
            return beforeDifference + 48d * ((winnerIndex == 0 ? 1d : 0d) - expectedFirst);
        }
        var low = -2000d;
        var high = 2000d;
        if (targetDifference < ProjectDifference(low, winner) - epsilon
            || targetDifference > ProjectDifference(high, winner) + epsilon) return false;
        for (var index = 0; index < 100; index++)
        {
            var middle = (low + high) / 2d;
            if (ProjectDifference(middle, winner) < targetDifference) low = middle;
            else high = middle;
        }
        var beforeDifference = (low + high) / 2d;
        var sum = firstAfter + secondAfter;
        firstBefore = (sum + beforeDifference) / 2d;
        secondBefore = (sum - beforeDifference) / 2d;
        if (firstBefore is < 500d or > 2500d || secondBefore is < 500d or > 2500d) return false;
        var expected = 1d / (1d + Math.Pow(10d, (secondBefore - firstBefore) / 400d));
        var projectedFirst = Math.Clamp(firstBefore + 24d * ((winner == 0 ? 1d : 0d) - expected),
            500d, 2500d);
        var projectedSecond = Math.Clamp(secondBefore + 24d * ((winner == 1 ? 1d : 0d)
            - (1d - expected)), 500d, 2500d);
        return Math.Abs(projectedFirst - firstAfter) <= epsilon
            && Math.Abs(projectedSecond - secondAfter) <= epsilon;
    }

    private void BuildHeldReleasePlanLocked(RankedIntegrityActionPlan plan,
        IEnumerable<string> matchIds)
    {
        var selected = matchIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var holds = _data.RankedHeldRewards.Where(row => selected.Contains(row.MatchId)
                && IsRankedMatchExcludedLocked(row.MatchId))
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (holds.Length == 0) return;

        var working = holds.SelectMany(row => new[] { row.FirstAccountId, row.SecondAccountId })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(accountId => accountId,
                accountId => CaptureRankedProfile(_data.RankedProfiles.Single(row =>
                    row.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase))),
                StringComparer.OrdinalIgnoreCase);
        var original = working.ToDictionary(item => item.Key,
            item => CloneRankedProfileSnapshot(item.Value), StringComparer.OrdinalIgnoreCase);

        foreach (var hold in holds)
        {
            if (!RankedProfileSnapshotsEqual(working[hold.FirstAccountId], hold.FirstBefore)
                || !RankedProfileSnapshotsEqual(working[hold.SecondAccountId], hold.SecondBefore))
            {
                plan.BlockingReasons.Add($"对局 {hold.MatchId} 暂扣后的玩家档案已变化，不能按旧路径补发");
                continue;
            }
            working[hold.FirstAccountId] = CloneRankedProfileSnapshot(hold.FirstAfter);
            working[hold.SecondAccountId] = CloneRankedProfileSnapshot(hold.SecondAfter);
            plan.ReleasedHeldMatchIds.Add(hold.MatchId);
        }
        if (plan.BlockingReasons.Count > 0) return;
        foreach (var item in working)
        {
            if (!RankedProfileSnapshotsEqual(original[item.Key], item.Value))
                plan.Transitions.Add(new(item.Key, original[item.Key], item.Value, "release-held",
                    holds.Where(row => row.FirstAccountId.Equals(item.Key, StringComparison.OrdinalIgnoreCase)
                            || row.SecondAccountId.Equals(item.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(row => row.MatchId).ToArray()));
        }
    }

    private void BuildRevocationPlanLocked(RankedIntegrityActionPlan plan)
    {
        var original = plan.RevokedDecision!;
        var corrections = _data.RankedIntegrityCorrections.Where(row =>
                row.DecisionId.Equals(original.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var correction in corrections)
        {
            var profile = _data.RankedProfiles.FirstOrDefault(row => row.AccountId.Equals(
                correction.AccountId, StringComparison.OrdinalIgnoreCase));
            if (profile is null || !RankedProfileSnapshotsEqual(CaptureRankedProfile(profile), correction.After))
            {
                plan.BlockingReasons.Add($"账号 {AccountName(correction.AccountId)} 在原处置后已有档案变化，不能安全撤销修正");
                continue;
            }
            plan.Transitions.Add(new(correction.AccountId, CloneRankedProfileSnapshot(correction.After),
                CloneRankedProfileSnapshot(correction.Before), "revoke", correction.MatchIds.ToArray()));
        }

        var releasedOriginally = corrections.SelectMany(row => row.MatchIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heldToRelease = original.MatchIds.Where(matchId => _data.RankedHeldRewards.Any(row =>
                row.MatchId.Equals(matchId, StringComparison.OrdinalIgnoreCase))
            && !releasedOriginally.Contains(matchId)).ToArray();
        if (heldToRelease.Length > 0) BuildHeldReleasePlanLocked(plan, heldToRelease);
    }

    private void ApplyRankedIntegrityPlanLocked(RankedIntegrityActionPlan plan,
        RankedIntegrityDecisionRow decision, DateTimeOffset now)
    {
        foreach (var transition in plan.Transitions)
        {
            var profile = _data.RankedProfiles.Single(row => row.AccountId.Equals(transition.AccountId,
                StringComparison.OrdinalIgnoreCase));
            if (!RankedProfileSnapshotsEqual(CaptureRankedProfile(profile), transition.Before))
                throw new L12RankedIntegrityActionException("ranked_integrity_profile_conflict",
                    $"账号 {AccountName(transition.AccountId)} 档案在确认前发生变化");
            ApplyRankedProfileSnapshot(profile, transition.After);
            _data.RankedIntegrityCorrections.Add(new RankedIntegrityCorrectionRow
            {
                DecisionId = decision.Id,
                AccountId = transition.AccountId,
                Kind = transition.Kind,
                MatchIds = transition.MatchIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Before = CloneRankedProfileSnapshot(transition.Before),
                After = CloneRankedProfileSnapshot(transition.After),
                CreatedAt = now,
            });
        }

        if (plan.Input.Disposition is "confirmed" or "system-error")
            AdjustRankedMasterAggregatesLocked(plan.VoidedAppliedMatchIds, -1);
        if (plan.ReleasedHeldMatchIds.Count > 0)
        {
            AdjustRankedMasterAggregatesLocked(plan.ReleasedHeldMatchIds, 1);
            ImportReleasedHeldTitleFactsLocked(plan.ReleasedHeldMatchIds);
        }
        if (plan.Input.Disposition == "revoked" && plan.RevokedDecision is not null)
        {
            var originallyVoided = _data.RankedIntegrityCorrections.Where(row =>
                    row.DecisionId.Equals(plan.RevokedDecision.Id, StringComparison.OrdinalIgnoreCase)
                    && row.Kind == "void-applied")
                .SelectMany(row => row.MatchIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            AdjustRankedMasterAggregatesLocked(originallyVoided, 1);
        }
    }

    private List<RankedIntegrityAccountEffectRow> BuildAccountEffectsLocked(
        RankedIntegrityActionPlan plan, DateTimeOffset now)
    {
        var participants = plan.Input.MatchIds.Select(matchId => _data.RankedIntegrityAudits.Single(row =>
                row.MatchId.Equals(matchId, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(row => new[] { row.FirstAccountId, row.SecondAccountId })
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return participants.Select(accountId =>
        {
            var delta = plan.Transitions.Where(row => row.AccountId.Equals(accountId,
                    StringComparison.OrdinalIgnoreCase)).Sum(row => row.After.SevenValue - row.Before.SevenValue);
            var restricted = plan.Input.Disposition == "confirmed"
                && plan.Input.RestrictedAccountIds.Contains(accountId, StringComparer.OrdinalIgnoreCase);
            var rewardOutcome = plan.Input.Disposition switch
            {
                "confirmed" or "system-error" => "voided",
                "normal" or "insufficient" when plan.ReleasedHeldMatchIds.Count > 0 => "released",
                "review" when plan.Input.MatchIds.Any(matchId => _data.RankedHeldRewards.Any(row =>
                    row.MatchId.Equals(matchId, StringComparison.OrdinalIgnoreCase))) => "held",
                "revoked" => "restored",
                _ => "unchanged",
            };
            return new RankedIntegrityAccountEffectRow
            {
                AccountId = accountId,
                ScoreDelta = delta,
                RestrictionUntil = restricted
                    ? now.AddDays(plan.Input.RestrictionDays!.Value) : null,
                RewardOutcome = rewardOutcome,
            };
        }).ToList();
    }

    private L12RankedIntegrityActionPreviewView PreviewViewLocked(RankedIntegrityActionPlan plan)
    {
        var effects = BuildAccountEffectsLocked(plan, DateTimeOffset.UtcNow);
        var blocked = plan.BlockingReasons.Distinct(StringComparer.Ordinal).ToArray();
        if (blocked.Length > 0)
            foreach (var effect in effects) effect.BlockedReason = string.Join("；", blocked);
        return new(_data.RankedIntegrityRevision, plan.Input.RequestId, plan.Input.Disposition,
            blocked.Length == 0, blocked, plan.Input.MatchIds,
            effects.Select(AccountEffectViewWithName).ToArray());
    }

    private bool TryHoldRankedMatchLocked(string matchId, RankedProfileRow first,
        RankedProfileRow second, int winner, string? firstMasterId, string? secondMasterId,
        L12RankedIntegrityContext? integrity, out L12RankedSettlementPair pair)
    {
        if (!ShouldHoldRankedRewardLocked(first.AccountId, second.AccountId, winner, integrity))
        {
            pair = null!;
            return false;
        }

        var firstBefore = CaptureRankedProfile(first);
        var secondBefore = CaptureRankedProfile(second);
        var firstRating = first.HiddenRating;
        var secondRating = second.HiddenRating;
        var firstSevenBefore = first.SevenValue;
        var secondSevenBefore = second.SevenValue;
        var firstStreakBefore = first.WinStreak;
        var secondStreakBefore = second.WinStreak;
        var firstSettlement = SettleOne(matchId, first, winner == 0, firstRating,
            secondSevenBefore, secondStreakBefore);
        var secondSettlement = SettleOne(matchId, second, winner == 1, secondRating,
            firstSevenBefore, firstStreakBefore);
        var expectedFirst = 1d / (1d + Math.Pow(10d, (secondRating - firstRating) / 400d));
        first.HiddenRating = Math.Clamp(firstRating + 24d * ((winner == 0 ? 1d : 0d) - expectedFirst),
            500d, 2500d);
        second.HiddenRating = Math.Clamp(secondRating + 24d * ((winner == 1 ? 1d : 0d)
            - (1d - expectedFirst)), 500d, 2500d);
        var winnerProfile = winner == 0 ? first : second;
        if (!winnerProfile.ReachedHighestTier && TierIndex(winnerProfile) == 4)
            winnerProfile.ReachedHighestTier = true;
        var firstAfter = CaptureRankedProfile(first);
        var secondAfter = CaptureRankedProfile(second);
        ApplyRankedProfileSnapshot(first, firstBefore);
        ApplyRankedProfileSnapshot(second, secondBefore);

        var now = DateTimeOffset.UtcNow;
        _data.RankedSettlements.Add(firstSettlement);
        _data.RankedSettlements.Add(secondSettlement);
        _data.RankedHeldRewards.Add(new RankedHeldRewardRow
        {
            MatchId = matchId,
            SeasonId = first.SeasonId,
            FirstAccountId = first.AccountId,
            SecondAccountId = second.AccountId,
            Winner = winner,
            FirstMasterId = firstMasterId?.Trim() ?? string.Empty,
            SecondMasterId = secondMasterId?.Trim() ?? string.Empty,
            FinalRound = integrity?.FinalRound ?? 0,
            ConclusionKind = integrity?.ConclusionKind?.Trim().ToLowerInvariant() ?? "unknown",
            FirstBefore = firstBefore,
            FirstAfter = firstAfter,
            SecondBefore = secondBefore,
            SecondAfter = secondAfter,
            CooldownUntil = now + RankedHighRiskCooldown,
            CreatedAt = now,
        });
        var hold = _data.RankedHeldRewards[^1];
        _data.RankedSettlementProfileFacts.Add(new RankedSettlementProfileFactRow
        {
            MatchId = matchId,
            FirstAccountId = first.AccountId,
            SecondAccountId = second.AccountId,
            FirstBefore = CloneRankedProfileSnapshot(firstBefore),
            FirstAfter = CloneRankedProfileSnapshot(firstAfter),
            SecondBefore = CloneRankedProfileSnapshot(secondBefore),
            SecondAfter = CloneRankedProfileSnapshot(secondAfter),
            AppliedInitially = false,
            CreatedAt = integrity?.EndedAt.ToUniversalTime() ?? now,
        });
        EnsureRankedIntegrityAuditLocked(matchId, first.AccountId, second.AccountId, winner,
            firstMasterId, secondMasterId, integrity);
        RecordAutomaticHoldDecisionLocked(hold);
        Save();
        pair = new(ToView(firstSettlement), ToView(secondSettlement), []);
        return true;
    }

    private bool IsRankedMatchCurrentlyAppliedLocked(string matchId)
        => !IsRankedMatchExcludedLocked(matchId);

    private bool IsRankedMatchExcludedLocked(string matchId)
    {
        var latest = _data.RankedIntegrityDecisions.Where(row => row.MatchIds.Contains(matchId,
                StringComparer.OrdinalIgnoreCase) && !IsDecisionRevokedLocked(row.Id))
            .OrderByDescending(row => row.Revision).FirstOrDefault();
        if (latest is not null)
            return latest.Disposition is "confirmed" or "system-error"
                || latest.Disposition == "review" && _data.RankedHeldRewards.Any(row =>
                    row.MatchId.Equals(matchId, StringComparison.OrdinalIgnoreCase));
        return _data.RankedHeldRewards.Any(row => row.MatchId.Equals(matchId,
            StringComparison.OrdinalIgnoreCase));
    }

    private string RankedRewardStatusLocked(string matchId)
    {
        var hasHold = _data.RankedHeldRewards.Any(row => row.MatchId.Equals(matchId,
            StringComparison.OrdinalIgnoreCase));
        var latest = _data.RankedIntegrityDecisions.Where(row => row.MatchIds.Contains(matchId,
                StringComparer.OrdinalIgnoreCase) && !IsDecisionRevokedLocked(row.Id))
            .OrderByDescending(row => row.Revision).FirstOrDefault();
        if (latest?.Disposition is "confirmed" or "system-error") return "voided";
        if (latest?.Disposition == "review" && hasHold) return "held";
        if (hasHold && latest?.Disposition is "normal" or "insufficient") return "released";
        if (hasHold && latest is null) return "held";
        return "applied";
    }

    private bool IsDecisionRevokedLocked(string decisionId)
        => _data.RankedIntegrityDecisions.Any(row => row.Disposition == "revoked"
            && row.RevokesDecisionId?.Equals(decisionId, StringComparison.OrdinalIgnoreCase) == true);

    private bool ValidateCurrentRankedProfileChainLocked(RankedProfileRow profile,
        IReadOnlyList<RankedSettlementRow> segment, out string reason)
    {
        if (segment.Count == 0)
        {
            reason = "排位结算链为空";
            return false;
        }
        for (var index = 1; index < segment.Count; index++)
        {
            if (segment[index].Before != segment[index - 1].After)
            {
                reason = $"matchId {segment[index].MatchId} 的 Before/After 链不连续";
                return false;
            }
        }
        var projected = CloneRankedProfileSnapshot(CaptureRankedProfile(profile));
        ApplyDerivedRankedProfileState(projected, segment);
        var last = segment[^1];
        if (profile.SevenValue != last.After || profile.PlacementPlayed != projected.PlacementPlayed
            || profile.PlacementWins != projected.PlacementWins || profile.Wins != projected.Wins
            || profile.Losses != projected.Losses || profile.WinStreak != projected.WinStreak
            || profile.LossStreak != projected.LossStreak || profile.HighestFloor != projected.HighestFloor
            || profile.ReachedHighestTier != projected.ReachedHighestTier)
        {
            reason = $"当前档案与不可变结算链不一致"
                + $"（七曜{profile.SevenValue}/{last.After}，定级{profile.PlacementPlayed}/{projected.PlacementPlayed}，"
                + $"定级胜{profile.PlacementWins}/{projected.PlacementWins}，胜负{profile.Wins}-{profile.Losses}/"
                + $"{projected.Wins}-{projected.Losses}，连胜负{profile.WinStreak}-{profile.LossStreak}/"
                + $"{projected.WinStreak}-{projected.LossStreak}，保底{profile.HighestFloor}/{projected.HighestFloor}，"
                + $"最高阶{profile.ReachedHighestTier}/{projected.ReachedHighestTier}）";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private void ApplyDerivedRankedProfileState(RankedProfileSnapshotRow target,
        IReadOnlyList<RankedSettlementRow> rows)
    {
        target.PlacementPlayed = rows.Count == 0 ? 0 : rows[^1].PlacementPlayed;
        // 现有结算账本历史上将 PlacementWins 随每次胜局递增（含定级完成后的胜局）；
        // 回滚必须复现该持久化语义，不能凭规则理想值重写旧链。
        target.PlacementWins = rows.Count(row => row.Outcome == "win");
        target.Wins = rows.Count(row => row.Outcome == "win");
        target.Losses = rows.Count(row => row.Outcome == "loss");
        target.WinStreak = 0;
        target.LossStreak = 0;
        foreach (var row in rows)
        {
            if (row.Outcome == "win")
            {
                target.WinStreak++;
                target.LossStreak = 0;
            }
            else
            {
                target.LossStreak++;
                target.WinStreak = 0;
            }
        }
        target.HighestFloor = rows.Count == 0 ? 0 : rows.Max(row => FloorFor(row.After));
        target.ReachedHighestTier = rows.Any(row => row.Outcome == "win"
            && RankedTierIndex(row.After) == _data.RankedConfig!.Factions[0].Tiers.Count - 1);
    }

    private static RankedProfileSnapshotRow CaptureRankedProfile(RankedProfileRow row) => new()
    {
        AccountId = row.AccountId,
        SeasonId = row.SeasonId,
        Faction = row.Faction,
        HiddenRating = row.HiddenRating,
        SevenValue = row.SevenValue,
        PlacementPlayed = row.PlacementPlayed,
        PlacementWins = row.PlacementWins,
        Wins = row.Wins,
        Losses = row.Losses,
        WinStreak = row.WinStreak,
        LossStreak = row.LossStreak,
        HighestFloor = row.HighestFloor,
        ReachedHighestTier = row.ReachedHighestTier,
        SelectedMasterTitle = row.SelectedMasterTitle,
    };

    private static RankedProfileSnapshotRow CloneRankedProfileSnapshot(RankedProfileSnapshotRow row)
        => new()
        {
            AccountId = row.AccountId,
            SeasonId = row.SeasonId,
            Faction = row.Faction,
            HiddenRating = row.HiddenRating,
            SevenValue = row.SevenValue,
            PlacementPlayed = row.PlacementPlayed,
            PlacementWins = row.PlacementWins,
            Wins = row.Wins,
            Losses = row.Losses,
            WinStreak = row.WinStreak,
            LossStreak = row.LossStreak,
            HighestFloor = row.HighestFloor,
            ReachedHighestTier = row.ReachedHighestTier,
            SelectedMasterTitle = row.SelectedMasterTitle,
        };

    private static void ApplyRankedProfileSnapshot(RankedProfileRow row,
        RankedProfileSnapshotRow snapshot)
    {
        row.SeasonId = snapshot.SeasonId;
        row.Faction = snapshot.Faction;
        row.HiddenRating = snapshot.HiddenRating;
        row.SevenValue = snapshot.SevenValue;
        row.PlacementPlayed = snapshot.PlacementPlayed;
        row.PlacementWins = snapshot.PlacementWins;
        row.Wins = snapshot.Wins;
        row.Losses = snapshot.Losses;
        row.WinStreak = snapshot.WinStreak;
        row.LossStreak = snapshot.LossStreak;
        row.HighestFloor = snapshot.HighestFloor;
        row.ReachedHighestTier = snapshot.ReachedHighestTier;
        row.SelectedMasterTitle = snapshot.SelectedMasterTitle;
    }

    private static bool RankedProfileSnapshotsEqual(RankedProfileSnapshotRow first,
        RankedProfileSnapshotRow second)
        => first.AccountId.Equals(second.AccountId, StringComparison.OrdinalIgnoreCase)
           && first.SeasonId.Equals(second.SeasonId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(first.Faction, second.Faction, StringComparison.OrdinalIgnoreCase)
           && Math.Abs(first.HiddenRating - second.HiddenRating) < 0.0000001d
           && first.SevenValue == second.SevenValue
           && first.PlacementPlayed == second.PlacementPlayed
           && first.PlacementWins == second.PlacementWins
           && first.Wins == second.Wins && first.Losses == second.Losses
           && first.WinStreak == second.WinStreak && first.LossStreak == second.LossStreak
           && first.HighestFloor == second.HighestFloor
           && first.ReachedHighestTier == second.ReachedHighestTier
           && string.Equals(first.SelectedMasterTitle, second.SelectedMasterTitle,
               StringComparison.Ordinal);

    private void AdjustRankedMasterAggregatesLocked(IEnumerable<string> matchIds, int direction)
    {
        foreach (var matchId in matchIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var audit = _data.RankedIntegrityAudits.FirstOrDefault(row => row.MatchId.Equals(matchId,
                StringComparison.OrdinalIgnoreCase));
            if (audit is null || audit.Winner is not (0 or 1)) continue;
            Adjust(audit.FirstAccountId, audit.SeasonId, audit.FirstMasterId, audit.Winner == 0);
            Adjust(audit.SecondAccountId, audit.SeasonId, audit.SecondMasterId, audit.Winner == 1);
        }
        return;

        void Adjust(string accountId, string seasonId, string masterId, bool won)
        {
            if (string.IsNullOrWhiteSpace(masterId)) return;
            var row = _data.RankedMasterRecords.FirstOrDefault(item =>
                item.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                && item.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase)
                && item.MasterId.Equals(masterId, StringComparison.OrdinalIgnoreCase));
            if (direction < 0)
            {
                if (row is null || row.Games <= 0 || won && row.Wins <= 0) return;
                row.Games--;
                if (won) row.Wins--;
                return;
            }
            if (row is null)
            {
                row = new RankedMasterRecordRow
                {
                    AccountId = accountId,
                    SeasonId = seasonId,
                    MasterId = masterId,
                };
                _data.RankedMasterRecords.Add(row);
            }
            row.Games++;
            if (won) row.Wins++;
        }
    }

    private void ImportReleasedHeldTitleFactsLocked(IEnumerable<string> matchIds)
    {
        foreach (var matchId in matchIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hold = _data.RankedHeldRewards.Single(row => row.MatchId.Equals(matchId,
                StringComparison.OrdinalIgnoreCase));
            ImportRankedMasterTitleFactLocked(new L12RankedMasterTitleMatchFact(hold.MatchId,
                hold.FirstAccountId, hold.SecondAccountId, hold.FirstMasterId, hold.SecondMasterId,
                hold.Winner, hold.FinalRound, hold.CreatedAt, hold.ConclusionKind, true));
            if (!_data.RankedMasterRecordedMatchIds.Contains(hold.MatchId,
                    StringComparer.OrdinalIgnoreCase))
                _data.RankedMasterRecordedMatchIds.Add(hold.MatchId);
        }
    }

    private void AddRankedIntegrityNotificationsLocked(RankedIntegrityDecisionRow decision)
    {
        foreach (var effect in decision.AccountEffects)
        {
            _data.RankedIntegrityNotifications.Add(new RankedIntegrityNotificationRow
            {
                AccountId = effect.AccountId,
                DecisionId = decision.Id,
                MatchIds = decision.MatchIds.ToList(),
                Outcome = decision.Disposition,
                Reason = decision.Reason,
                DecidedAt = decision.CreatedAt,
                ScoreDelta = effect.ScoreDelta,
                RestrictionUntil = effect.RestrictionUntil,
                AppealGuidance = RankedAppealGuidance,
                RelatedDecisionId = decision.RevokesDecisionId,
            });
        }
    }

    private void RecordAutomaticHoldDecisionLocked(RankedHeldRewardRow hold)
    {
        var requestId = $"hold-{hold.Id}";
        var decision = new RankedIntegrityDecisionRow
        {
            RequestId = requestId,
            RequestFingerprint = RankedIntegrityFingerprint($"automatic-hold\n{hold.MatchId}"),
            Revision = checked(_data.RankedIntegrityRevision + 1),
            Disposition = "review",
            MatchIds = [hold.MatchId],
            Evidence = "短时窗口内，同一账号组合出现至少三局由同一方获胜、每局不超过 60 秒且没有有效规则操作。",
            Reason = "本局排位奖励已自动暂扣，等待人工复核；网络关联或普通重复对局本身不会触发暂扣。",
            ActorId = "$system",
            ActorName = "排位完整性系统",
            CreatedAt = hold.CreatedAt,
            AccountEffects =
            [
                new RankedIntegrityAccountEffectRow
                {
                    AccountId = hold.FirstAccountId,
                    RewardOutcome = "held",
                },
                new RankedIntegrityAccountEffectRow
                {
                    AccountId = hold.SecondAccountId,
                    RewardOutcome = "held",
                },
            ],
        };
        _data.RankedIntegrityDecisions.Add(decision);
        _data.RankedIntegrityRevision = decision.Revision;
        AddRankedIntegrityNotificationsLocked(decision);
    }

    private L12RankedIntegrityDecisionView RankedIntegrityDecisionViewLocked(
        RankedIntegrityDecisionRow row)
    {
        var revokedBy = _data.RankedIntegrityDecisions.FirstOrDefault(item => item.Disposition == "revoked"
            && item.RevokesDecisionId?.Equals(row.Id, StringComparison.OrdinalIgnoreCase) == true);
        return new(row.Id, row.RequestId, row.Revision, row.Disposition,
            revokedBy is null ? row.Disposition : "revoked", row.MatchIds.ToArray(),
            row.RestrictedAccountIds.ToArray(), row.RestrictionDays, row.Evidence, row.Reason,
            row.ActorId, row.ActorName, row.CreatedAt, row.RevokesDecisionId, revokedBy?.Id,
            row.AccountEffects.Select(AccountEffectViewWithName).ToArray());
    }

    private L12RankedIntegrityNotificationView RankedIntegrityNotificationViewLocked(
        RankedIntegrityNotificationRow row)
    {
        var outcome = row.Outcome;
        if (!outcome.StartsWith("appeal-", StringComparison.Ordinal)
            && _data.RankedIntegrityDecisions.Any(decision => decision.Id == row.DecisionId
                && IsDecisionRevokedLocked(decision.Id)))
            outcome = "revoked";
        return new(row.Id, row.DecisionId, row.MatchIds.ToArray(), outcome, row.Reason,
            row.DecidedAt, row.ScoreDelta, row.RestrictionUntil, row.AppealGuidance,
            row.AcknowledgedAt is not null, row.AcknowledgedAt, row.RelatedDecisionId);
    }

    private L12RankedIntegrityAppealView RankedIntegrityAppealViewLocked(RankedIntegrityAppealRow row)
    {
        var latest = row.Events.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id,
            StringComparer.Ordinal).LastOrDefault();
        return new(row.Id, row.DecisionId, row.AccountId, AccountName(row.AccountId), row.Statement,
            latest?.Status ?? "open", latest?.Reply, 1L + row.Events.Count, row.CreatedAt,
            latest?.CreatedAt ?? row.CreatedAt);
    }

    private static string CurrentAppealStatus(RankedIntegrityAppealRow row)
        => row.Events.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal)
            .LastOrDefault()?.Status ?? "open";

    private static DateTimeOffset AppealUpdatedAt(RankedIntegrityAppealRow row)
        => row.Events.Select(item => item.CreatedAt).DefaultIfEmpty(row.CreatedAt).Max();

    private L12RankedIntegrityAccountEffectView AccountEffectViewWithName(
        RankedIntegrityAccountEffectRow row)
        => new(row.AccountId, AccountName(row.AccountId), row.ScoreDelta, row.RestrictionUntil,
            row.RewardOutcome, row.BlockedReason);

    private static IReadOnlyList<T> PageAfter<T>(IEnumerable<T> source, string? cursor,
        Func<T, string> id, int limit, out string? nextCursor)
    {
        var rows = source.ToArray();
        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var index = Array.FindIndex(rows, row => id(row).Equals(cursor.Trim(),
                StringComparison.Ordinal));
            if (index >= 0) start = index + 1;
        }
        var page = rows.Skip(start).Take(limit).ToArray();
        nextCursor = start + page.Length < rows.Length && page.Length > 0 ? id(page[^1]) : null;
        return page;
    }

    private void RequireActiveRankedIntegrityActorLocked(L12AccountView actor)
    {
        if (actor is null || string.IsNullOrWhiteSpace(actor.Id)
            || !_data.Accounts.Any(row => row.Id.Equals(actor.Id, StringComparison.OrdinalIgnoreCase)
                && !row.Disabled && !row.Deleted))
            throw new L12RankedIntegrityActionException("ranked_integrity_account_unavailable",
                "账号不存在或不可用");
    }

    private static void EnsureRankedIntegrityPermission(L12AccountView actor,
        L12Permission permission)
    {
        if (!L12Authorization.HasPermission(actor, permission))
            throw new L12RankedIntegrityActionException("permission_denied", "账号缺少排位完整性处置权限");
    }

    private static string RequireRankedIntegrityRequestId(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!L12CorrelationIds.IsValid(normalized))
            throw new L12RankedIntegrityActionException("ranked_integrity_request_id_invalid",
                "requestId 格式无效");
        return normalized;
    }

    private static string RequireRankedIntegrityText(string? value, string label, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maxLength
            || normalized.Any(character => char.IsControl(character) && character is not '\r' and not '\n'))
            throw new L12RankedIntegrityActionException("ranked_integrity_text_invalid",
                $"{label}必须为 1 至 {maxLength} 个字符且不能包含非法控制字符");
        return normalized;
    }

    private static string RankedIntegrityFingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void EnsureRequestFingerprint(string existing, string requested)
    {
        if (existing.Length != requested.Length || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(existing), Encoding.UTF8.GetBytes(requested)))
            throw new L12RankedIntegrityActionException("ranked_integrity_request_id_conflict",
                "requestId 已用于不同请求");
    }

    private static bool HasInvalidRankedIntegrityActionState(DataFile data)
    {
        static bool InvalidHash(string value) => value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character));
        var accountIds = data.Accounts.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decisionIds = data.RankedIntegrityDecisions.Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return data.RankedIntegrityRevision < 0
            || data.RankedIntegrityDecisions.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityDecisions.GroupBy(row => row.RequestId, StringComparer.Ordinal)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityDecisions.GroupBy(row => row.Revision)
                .Any(group => group.Key <= 0 || group.Count() > 1)
            || data.RankedIntegrityDecisions.Any(row => !RankedIntegrityDispositions.Contains(row.Disposition)
                || InvalidHash(row.RequestFingerprint) || row.MatchIds is null
                || row.MatchIds.Count is < 1 or > 50
                || row.MatchIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != row.MatchIds.Count
                || row.AccountEffects is null || row.AccountEffects.Any(effect => !accountIds.Contains(effect.AccountId))
                || !accountIds.Contains(row.ActorId) && row.ActorId != "$system")
            || data.RankedIntegrityDecisions.Any() && data.RankedIntegrityRevision
                < data.RankedIntegrityDecisions.Max(row => row.Revision)
            || data.RankedHeldRewards.GroupBy(row => row.MatchId, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedHeldRewards.Any(row => row.Winner is not (0 or 1)
                || !accountIds.Contains(row.FirstAccountId) || !accountIds.Contains(row.SecondAccountId)
                || row.FirstAccountId.Equals(row.SecondAccountId, StringComparison.OrdinalIgnoreCase)
                || row.FirstBefore.AccountId != row.FirstAccountId || row.FirstAfter.AccountId != row.FirstAccountId
                || row.SecondBefore.AccountId != row.SecondAccountId || row.SecondAfter.AccountId != row.SecondAccountId)
            || data.RankedSettlementProfileFacts.GroupBy(row => row.MatchId,
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedSettlementProfileFacts.Any(row => !accountIds.Contains(row.FirstAccountId)
                || !accountIds.Contains(row.SecondAccountId)
                || row.FirstAccountId.Equals(row.SecondAccountId, StringComparison.OrdinalIgnoreCase)
                || row.FirstBefore.AccountId != row.FirstAccountId || row.FirstAfter.AccountId != row.FirstAccountId
                || row.SecondBefore.AccountId != row.SecondAccountId || row.SecondAfter.AccountId != row.SecondAccountId)
            || data.RankedIntegrityCorrections.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityCorrections.Any(row => !decisionIds.Contains(row.DecisionId)
                || !accountIds.Contains(row.AccountId) || row.Before.AccountId != row.AccountId
                || row.After.AccountId != row.AccountId || row.MatchIds is null)
            || data.RankedIntegrityNotifications.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityNotifications.Any(row => !accountIds.Contains(row.AccountId)
                || !decisionIds.Contains(row.DecisionId) || row.MatchIds is null)
            || data.RankedIntegrityAppeals.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityAppeals.GroupBy(row => row.RequestId, StringComparer.Ordinal)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            || data.RankedIntegrityAppeals.Any(row => !decisionIds.Contains(row.DecisionId)
                || !accountIds.Contains(row.AccountId) || InvalidHash(row.RequestFingerprint)
                || row.Events is null || row.Events.Any(item => InvalidHash(item.RequestFingerprint)
                    || !RankedIntegrityAppealStatuses.Contains(item.Status)
                    || !accountIds.Contains(item.ActorId)))
            || data.RankedIntegrityAppeals.SelectMany(row => row.Events)
                .GroupBy(row => row.RequestId, StringComparer.Ordinal)
                .Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
    }
}
