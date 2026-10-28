namespace TwelveLegions.Server;

public sealed record AuthRequest(string? Username, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record ChangeUsernameRequest(string? CurrentPassword, string? NewUsername);
public sealed record UsernameChangeApplicationRequest(string? NewUsername, string? Reason);
public sealed record UsernameChangeReviewRequest(bool Approve, string? Note);
public sealed record CurrentPasswordRequest(string? CurrentPassword);
public sealed record EmailBindingRequest(string? Email, string? CurrentPassword);
public sealed record ForgotPasswordRequest(string? Email);
public sealed record TokenRequest(string? Token);
public sealed record PasswordResetRequest(string? Token, string? NewPassword);
public sealed record FriendRequest(string? AccountId);
public sealed record FriendResolveRequest(bool Accept);
public sealed record RankedFactionRequest(string? Faction);
public sealed record RankedTitleRequest(string? Title);
public sealed record RankedConfigRequest(L12RankedConfigView Config, string? Reason);
public sealed record SeasonDraftCreateRequest(long ExpectedCurrentRevision, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record SeasonDraftUpdateRequest(L12SeasonDefinitionDraft Draft, long ExpectedRevision,
    string? Reason, string? PreviewToken = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null);
public sealed record SeasonDraftDeleteRequest(long ExpectedRevision, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record SeasonDefinitionPreviewRequest(L12SeasonDefinitionDraft Draft,
    long ExpectedRevision, long? ExpectedVersion = null);
public sealed record SeasonDefinitionApplyRequest(L12SeasonDefinitionDraft Draft,
    long ExpectedRevision, string? PreviewToken, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12SeasonDraftCreateCommandPayload(long ExpectedCurrentRevision);
public sealed record L12SeasonDraftDeleteCommandPayload(string DefinitionId, long ExpectedRevision);
public sealed record L12SeasonDefinitionApplyCommandPayload(string DefinitionId,
    L12SeasonDefinitionDraft Draft, long ExpectedRevision, string PreviewToken);
public sealed record RankedBroadcastCompleteRequest(string? ClaimToken);
public sealed record RoleRequest(string? Role, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record RoleCommandPayload(string AccountId, string Role);
public sealed record RoleCommandResult(string AccountId, string Role, bool Changed);
public sealed record AccountStatusRequest(bool Disabled, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record AccountSecurityActionRequest(string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record AccountPasswordResetCommandPayload(string AccountId, string Reason);
public sealed record AccountDeletionCommandPayload(string AccountId, string Reason);
public sealed record OperationsConfigPreviewRequest(L12OperationsConfigPayload Config,
    long? ExpectedVersion = null);
public sealed record OperationsConfigApplyRequest(L12OperationsConfigPayload Config, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null, string? CrossSectionReplaceIntent = null);
public sealed record OperationsConfigRollbackRequest(string? VersionId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null, string? CrossSectionReplaceIntent = null);
public sealed record L12OperationsRollbackCommandPayload(string VersionId);
public sealed record OperationsConfigSectionPreviewRequest(L12OperationsSectionPayload Config,
    IReadOnlyDictionary<string, long>? ExpectedFieldRevisions = null,
    long? ExpectedRevision = null);
public sealed record OperationsConfigSectionApplyRequest(L12OperationsSectionPayload Config,
    IReadOnlyDictionary<string, long>? ExpectedFieldRevisions = null, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedRevision = null);
public sealed record OperationsConfigSectionRollbackRequest(string? VersionId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedRevision = null);
public sealed record L12OperationsSectionApplyCommandPayload(string Section,
    L12OperationsSectionPayload Config, long ExpectedRevision,
    IReadOnlyDictionary<string, long> ExpectedFieldRevisions);
public sealed record L12OperationsSectionRollbackCommandPayload(string Section, string VersionId,
    long ExpectedRevision);
public sealed record SeasonActivationRequest(long ExpectedCurrentRevision, long ExpectedDraftRevision,
    string? Reason = null, string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12SeasonActivationCommandPayload(string DefinitionId,
    long ExpectedCurrentRevision, long ExpectedDraftRevision);
public sealed record RankedSeasonResetRepairPreviewRequest(string? SeasonId,
    long? ExpectedVersion = null, DateTimeOffset? CompetitiveStartAt = null);
public sealed record RankedSeasonResetRepairRequest(string? SeasonId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null,
    string? ExpectedEvidenceFingerprint = null, DateTimeOffset? CompetitiveStartAt = null);
public sealed record SeasonIdentityMigrationPreviewRequest(long? ExpectedVersion = null);
public sealed record SeasonIdentityMigrationRequest(string? ExpectedPlatformFingerprint,
    string? ExpectedRecorderFingerprint, string? Reason = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null);
public sealed record L12RankedSeasonResetRepairCommandPayload(string SeasonId,
    string ExpectedEvidenceFingerprint, DateTimeOffset? CompetitiveStartAt = null);
public sealed record SeasonActivationPreviewRequest(long ExpectedCurrentRevision,
    long ExpectedDraftRevision, long? ExpectedVersion = null);
public sealed record SeasonActivationArmRequest(long ExpectedCurrentRevision, long ExpectedDraftRevision,
    string? Reason = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    string? ImpactPreviewToken = null);
public sealed record L12SeasonActivationArmCommandPayload(string DefinitionId,
    long ExpectedCurrentRevision, long ExpectedDraftRevision, string ImpactPreviewToken);
public sealed record SeasonActivationDisarmRequest(long ExpectedDraftRevision,
    long ExpectedPlanGeneration, string? DisarmGuardToken = null, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12SeasonActivationDisarmCommandPayload(string DefinitionId,
    long ExpectedDraftRevision, long ExpectedPlanGeneration, string DisarmGuardToken);
public sealed record OperationsServerStartRequest(string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12ServerStartCommandPayload(long ExpectedVersion, string RequestedState = "open");
public sealed record OperationsImmediateMaintenanceRequest(int ExpectedDurationHours, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record OperationsImmediateMaintenanceEndRequest(string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12ImmediateMaintenanceStartCommandPayload(int ExpectedDurationHours,
    long ExpectedVersion, string RequestedState = "maintenance");
public sealed record L12ImmediateMaintenanceEndCommandPayload(long ExpectedVersion,
    string RequestedState = "open");
public sealed record AuditArchiveRequest(int? RetentionDays = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record SessionCommandPayload(string AccountId, string? SessionId);
public sealed record ContentRequest(string? Value, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record ArticleDraftRequest(string? Title, string? Summary, string? Body, string? Category,
    string? CoverUrl, string? Link, string? Slug, bool Pinned, DateTimeOffset? PublishAt,
    long? ExpectedRevision = null, string? Kind = null, string? CategoryId = null,
    string? MediaAssetId = null, int SortOrder = 0, string? VideoAuthorName = null);
public sealed record SiteCategoryRequest(string? Kind, string? Name, string? Slug, int SortOrder,
    bool Active = true, long? ExpectedVersion = null);
public sealed record SiteCategoryOrderRequest(IReadOnlyList<string>? Ids);
public sealed record ContentDraftCommandPayload(string Key, string Value);
public sealed record ContentBatchRequest(IReadOnlyList<string>? Keys, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record ContentRollbackRequest(string? BatchId, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectReviewRequest(string? AbilityId, string? Status, string? Note,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectReviewCommandPayload(string CardId, string? AbilityId, string Status, string? Note);
public sealed record EffectPresentationRequest(string? Text = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectPresentationCommandPayload(string CardId, string SceneId, string? Text, bool Restore);
public sealed record BugRequest(string? Title, string Description, string? Page, string? RoomCode,
    string? MatchId, string? Version, L12ClientConnectionDiagnosticView? ClientDiagnostic = null);
public sealed record BugUpdateRequest(string? Status, string? Priority, string? Assignee, string? AdminNotes,
    string? Comment, string? FixCommit = null, string? RegressionTest = null, string? DeployedVersion = null,
    string? VerifiedBy = null, DateTimeOffset? VerifiedAt = null, string? DuplicateOf = null,
    string? ClosureDisposition = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record BugUpdateCommandPayload(string Id, string? Status, string? Priority, string? Assignee,
    string? AdminNotes, string? Comment, string? FixCommit = null, string? RegressionTest = null,
    string? DeployedVersion = null, string? DuplicateOf = null, string? ClosureDisposition = null);
public sealed record PublishedDeckRequest(string? PublicationId, L12CustomDeckSubmission? Deck);
public sealed record AccountDeckUpdateRequest(L12CustomDeckSubmission? Deck, long ExpectedRevision);
public sealed record TournamentCreateRequest(L12TournamentCreatePayload Tournament, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentLegacyImportRequest(IReadOnlyList<L12LegacyTournamentInput>? Tournaments,
    string? PreviewHash = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentRegistrationRequest(string? DeckName, string? DeckCode,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentPreCheckInRequest(string? DeckName, string? DeckCode, string? DeckId = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentRemoveParticipantRequest(string? AccountId, bool BanRegistration, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRegistrationBanRequest(string? AccountId, bool Banned, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentOrganizerTransferRequest(string? AccountId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentOrganizerTransferDecisionRequest(string? RequestId, bool Accept, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentActionRequest(string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentStaffRequest(IReadOnlyList<string>? RefereeAccountIds,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentVisibilityRequest(string? Visibility, string? RegistrationVisibility,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentCheckInRequest(string? AccountId, bool Ready,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentPauseRequest(bool Paused, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentTimeExtensionRequest(int Minutes, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentPhaseRequest(string? Phase, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentAnnouncementRequest(string? Announcement, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRegistrationWindowRequest(DateTimeOffset? RegistrationOpensAt,
    DateTimeOffset? RegistrationClosesAt, string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentPostponeRequest(DateTimeOffset NewStartAt, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentCancelRequest(string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentMatchPauseRequest(bool Paused, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentJudgeCaseCreateRequest(string? MatchId, string? Category, string? Urgency,
    string? Message, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentJudgeCaseAssignRequest(string? CaseId, string? AssigneeAccountId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentJudgeCaseResolveRequest(string? CaseId, string? Status, string? Resolution,
    string? StaffNote, string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentJudgeCaseAppealRequest(string? CaseId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRulingRequest(string? Kind, string? TargetAccountId, string? Decision,
    string? Reason, string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRematchRequest(string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentMatchReferenceRequest(string? RecordedMatchId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentEmptyPayload;
public sealed record TournamentTargetCommandPayload(string TournamentId);
public sealed record TournamentRoundCommandPayload(string TournamentId, int RoundNumber);
public sealed record TournamentStaffCommandPayload(string TournamentId, L12TournamentStaffPayload Staff);
public sealed record TournamentRulingCommandPayload(string TournamentId, string MatchId,
    L12TournamentRulingPayload Ruling);
public sealed record TournamentRematchCommandPayload(string TournamentId, string MatchId,
    L12TournamentRematchPayload Rematch);
public sealed record TournamentReferenceCommandPayload(string TournamentId, string MatchId,
    L12TournamentMatchReferencePayload Reference);
public sealed record ReleaseDeployRequest(string? ArtifactId, string? Environment,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record ReleaseRollbackRequest(string? TargetRunId,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
