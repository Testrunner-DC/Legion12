using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12SeasonIdentityMigrationPreview(
    string MigrationId,
    string Status,
    bool CanApply,
    string PlatformFingerprint,
    string RecorderFingerprint,
    string LegacySeasonZeroDefinitionId,
    string LegacySeasonOneDefinitionId,
    string B0EvidenceFingerprint,
    L12RankedSeasonCutoverReadiness Readiness,
    IReadOnlyList<string> BlockingCodes);

internal sealed record L12SeasonIdentityMigrationClaim(
    string MigrationId,
    string Owner,
    DateTimeOffset LeaseExpiresAt,
    string PlatformFingerprint,
    string RecorderFingerprint,
    bool Replayed);

public sealed record L12SeasonIdentityMigrationResult(
    string MigrationId,
    string Status,
    bool Applied,
    bool Replayed,
    string PlatformFingerprint,
    string RecorderFingerprint,
    long CompletedStorageRevision,
    DateTimeOffset CompletedAt);

public sealed partial class L12PlatformStore
{
    private const string PlatformSeasonIdentityMigrationId = "season-id-normalization-s00-s01-v1";
    private const string LegacySeasonZeroIdentity = "S01";
    private const string LegacySeasonOneIdentity = "T01";
    private const string CanonicalSeasonZeroIdentity = "S00";
    private const string CanonicalSeasonOneIdentity = "S01";
    private const int SeasonIdentityCollectionLimit = 100_000;

    private sealed record PlatformSeasonIdentityMarker(
        string Status,
        string? Owner,
        DateTimeOffset? LeaseExpiresAt,
        string SourcePlatformFingerprint,
        string? ResultPlatformFingerprint,
        string SourceRecorderFingerprint,
        string? ResultRecorderFingerprint,
        string SeasonZeroDefinitionId,
        string SeasonOneDefinitionId,
        string B0EvidenceFingerprint,
        long? CompletedStorageRevision,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? VerifiedAt);

    private sealed record PlatformSeasonIdentityPlan(
        SeasonDefinitionRow Current,
        SeasonArchiveRow Previous,
        RankedSeasonResetRepairRow Repair,
        string Fingerprint,
        IReadOnlyList<string> BlockingCodes);

    internal L12SeasonIdentityMigrationPreview PreviewSeasonIdentityNormalization(
        L12AccountView actor, L12SeasonIdentityRecorderPreview recorder,
        L12RankedSeasonCutoverReadiness readiness, DateTimeOffset observedAt)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminOperationsRead);
        return ExecuteSeasonIdentityStorageMutation((connection, transaction) =>
        {
            var marker = ReadPlatformSeasonIdentityMarker(connection, transaction);
            var preview = BuildSeasonIdentityPreviewLocked(connection, transaction, recorder,
                readiness, observedAt.ToUniversalTime(), marker);
            return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationPreview>(false, preview);
        });
    }

    internal L12SeasonIdentityMigrationClaim ClaimSeasonIdentityNormalization(
        L12AccountView actor, L12SeasonIdentityRecorderPreview recorder,
        L12RankedSeasonCutoverReadiness readiness, string expectedPlatformFingerprint,
        string expectedRecorderFingerprint, string owner, TimeSpan leaseDuration,
        DateTimeOffset observedAt, L12AdminAuditContext context)
        => ExecuteSeasonIdentityStorageMutation((connection, transaction) =>
        {
            EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
            if (string.IsNullOrWhiteSpace(owner) || owner.Trim().Length > 120)
                throw new L12OperationsConfigException("season_identity_owner_invalid",
                    "赛季编号迁移 owner 无效");
            if (leaseDuration < TimeSpan.FromSeconds(10) || leaseDuration > TimeSpan.FromMinutes(15))
                throw new L12OperationsConfigException("season_identity_lease_invalid",
                    "赛季编号迁移租约必须为 10 秒至 15 分钟");
            var now = observedAt.ToUniversalTime();
            var marker = ReadPlatformSeasonIdentityMarker(connection, transaction);
            var preview = BuildSeasonIdentityPreviewLocked(connection, transaction, recorder,
                readiness, now, marker);
            RequireSeasonIdentityFingerprint(expectedPlatformFingerprint, preview.PlatformFingerprint,
                "season_identity_platform_preview_expired");
            RequireSeasonIdentityFingerprint(expectedRecorderFingerprint, preview.RecorderFingerprint,
                "season_identity_recorder_preview_expired");
            if (!preview.CanApply)
                throw new L12OperationsConfigException(preview.BlockingCodes.FirstOrDefault()
                        ?? "season_identity_not_ready",
                    $"赛季编号迁移被安全门禁拒绝：{string.Join(',', preview.BlockingCodes)}");
            if (marker is { Status: "platform_committed" or "verified" })
                return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationClaim>(false,
                    new(PlatformSeasonIdentityMigrationId, string.Empty, marker.CompletedAt ?? now,
                        marker.SourcePlatformFingerprint, marker.SourceRecorderFingerprint, true));

            var lease = now.Add(leaseDuration);
            using var claim = connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText = """
                INSERT INTO platform_season_identity_migrations(
                    migration_id,status,owner,lease_expires_utc,source_platform_fingerprint,
                    result_platform_fingerprint,source_recorder_fingerprint,result_recorder_fingerprint,
                    season_zero_definition_id,season_one_definition_id,b0_evidence_fingerprint,
                    completed_storage_revision,started_utc,completed_utc,verified_utc)
                VALUES($migration,'executing',$owner,$lease,$platform,NULL,$recorder,NULL,
                    $zero,$one,$b0,NULL,$started,NULL,NULL)
                ON CONFLICT(migration_id) DO UPDATE SET
                    status='executing',owner=excluded.owner,lease_expires_utc=excluded.lease_expires_utc,
                    source_platform_fingerprint=excluded.source_platform_fingerprint,
                    result_platform_fingerprint=NULL,
                    source_recorder_fingerprint=excluded.source_recorder_fingerprint,
                    result_recorder_fingerprint=NULL,
                    season_zero_definition_id=excluded.season_zero_definition_id,
                    season_one_definition_id=excluded.season_one_definition_id,
                    b0_evidence_fingerprint=excluded.b0_evidence_fingerprint,
                    completed_storage_revision=NULL,started_utc=excluded.started_utc,
                    completed_utc=NULL,verified_utc=NULL
                WHERE platform_season_identity_migrations.status='executing'
                  AND platform_season_identity_migrations.lease_expires_utc<=$started;
                """;
            claim.Parameters.AddWithValue("$migration", PlatformSeasonIdentityMigrationId);
            claim.Parameters.AddWithValue("$owner", owner.Trim());
            claim.Parameters.AddWithValue("$lease", lease.ToString("O"));
            claim.Parameters.AddWithValue("$platform", preview.PlatformFingerprint);
            claim.Parameters.AddWithValue("$recorder", preview.RecorderFingerprint);
            claim.Parameters.AddWithValue("$zero", preview.LegacySeasonZeroDefinitionId);
            claim.Parameters.AddWithValue("$one", preview.LegacySeasonOneDefinitionId);
            claim.Parameters.AddWithValue("$b0", preview.B0EvidenceFingerprint);
            claim.Parameters.AddWithValue("$started", now.ToString("O"));
            if (claim.ExecuteNonQuery() != 1)
                throw new L12OperationsConfigException("season_identity_lease_conflict",
                    "赛季编号迁移已由另一实例持有");

            AddAdminAudit(actor, "operations", "season-identity-claim",
                $"season-identity:{PlatformSeasonIdentityMigrationId}", null, "executing",
                $"platform={preview.PlatformFingerprint};recorder={preview.RecorderFingerprint}",
                context with { Outcome = "succeeded" });
            return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationClaim>(true,
                new(PlatformSeasonIdentityMigrationId, owner.Trim(), lease,
                    preview.PlatformFingerprint, preview.RecorderFingerprint, false));
        });

    internal L12SeasonIdentityMigrationResult CommitSeasonIdentityNormalization(
        L12AccountView actor, L12SeasonIdentityMigrationClaim claim,
        L12SeasonIdentityRecorderResult recorder, L12RankedSeasonCutoverReadiness readiness,
        string reason, DateTimeOffset observedAt, L12AdminAuditContext context)
        => ExecuteSeasonIdentityStorageMutation((connection, transaction) =>
        {
            EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
            var now = observedAt.ToUniversalTime();
            var marker = ReadPlatformSeasonIdentityMarker(connection, transaction)
                ?? throw new L12OperationsConfigException("season_identity_claim_missing",
                    "赛季编号迁移平台租约不存在");
            if (marker.Status is "platform_committed" or "verified")
                return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationResult>(false,
                    PlatformSeasonIdentityResult(marker, false, true));
            if (marker.Status != "executing" || marker.Owner != claim.Owner
                || marker.LeaseExpiresAt != claim.LeaseExpiresAt || marker.LeaseExpiresAt <= now)
                throw new L12OperationsConfigException("season_identity_lease_lost",
                    "赛季编号迁移平台租约已失效");
            if (!string.Equals(recorder.MigrationId, PlatformSeasonIdentityMigrationId,
                    StringComparison.Ordinal)
                || !string.Equals(recorder.SourceFingerprint, marker.SourceRecorderFingerprint,
                    StringComparison.Ordinal))
                throw new L12OperationsConfigException("season_identity_recorder_result_conflict",
                    "比赛库迁移结果与平台租约不匹配");
            if (!readiness.Ready)
                throw new L12OperationsConfigException("season_identity_ranked_not_ready",
                    "赛季编号迁移期间出现新的在途排位事实");

            var plan = BuildPlatformSeasonIdentityPlanLocked(connection, transaction, readiness, now);
            RequireSeasonIdentityFingerprint(marker.SourcePlatformFingerprint, plan.Fingerprint,
                "season_identity_platform_state_changed");
            if (plan.BlockingCodes.Count > 0)
                throw new L12OperationsConfigException(plan.BlockingCodes[0],
                    $"赛季编号迁移被安全门禁拒绝：{string.Join(',', plan.BlockingCodes)}");
            if (!string.Equals(plan.Previous.SourceDefinitionId, marker.SeasonZeroDefinitionId,
                    StringComparison.Ordinal)
                || !string.Equals(plan.Current.DefinitionId, marker.SeasonOneDefinitionId,
                    StringComparison.Ordinal)
                || !string.Equals(plan.Repair.EvidenceFingerprint, marker.B0EvidenceFingerprint,
                    StringComparison.Ordinal))
                throw new L12OperationsConfigException("season_identity_authority_changed",
                    "赛季编号迁移权威定义或 B0 证据已变化");

            ApplyPlatformSeasonIdentityMappingLocked(plan);
            MigrateSeasonFinalizationCoordination(connection, transaction);
            var resultFingerprint = ComputePlatformSeasonIdentityFingerprintLocked(connection, transaction);
            var normalizedReason = RequireOperationsReason(reason);
            AddAdminAudit(actor, "operations", "season-identity-normalize",
                $"season-identity:{PlatformSeasonIdentityMigrationId}",
                "S01=>S00;T01=>S01", $"platform={resultFingerprint};recorder={recorder.ResultFingerprint}",
                normalizedReason, context with { Reason = normalizedReason, Outcome = "succeeded" });
            var expectedCompletedRevision = checked(_data.Version + 1);
            var result = new L12SeasonIdentityMigrationResult(PlatformSeasonIdentityMigrationId,
                "platform_committed", true, false, resultFingerprint, recorder.ResultFingerprint,
                expectedCompletedRevision, now);
            return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationResult>(true, result,
                completedRevision =>
                {
                    using var complete = connection.CreateCommand();
                    complete.Transaction = transaction;
                    complete.CommandText = """
                        UPDATE platform_season_identity_migrations
                        SET status='platform_committed',owner=NULL,lease_expires_utc=NULL,
                            result_platform_fingerprint=$platform,
                            result_recorder_fingerprint=$recorder,
                            completed_storage_revision=$revision,completed_utc=$completed
                        WHERE migration_id=$migration AND status='executing' AND owner=$owner
                          AND lease_expires_utc=$lease;
                        """;
                    complete.Parameters.AddWithValue("$platform", resultFingerprint);
                    complete.Parameters.AddWithValue("$recorder", recorder.ResultFingerprint);
                    complete.Parameters.AddWithValue("$revision", completedRevision);
                    complete.Parameters.AddWithValue("$completed", now.ToString("O"));
                    complete.Parameters.AddWithValue("$migration", PlatformSeasonIdentityMigrationId);
                    complete.Parameters.AddWithValue("$owner", claim.Owner);
                    complete.Parameters.AddWithValue("$lease", claim.LeaseExpiresAt.ToString("O"));
                    if (complete.ExecuteNonQuery() != 1)
                        throw new L12OperationsConfigException("season_identity_lease_lost",
                            "赛季编号迁移提交前失去平台租约");
                    StorageFailureInjector?.Invoke("after-season-identity-platform-marker");
                });
        });

    internal L12SeasonIdentityMigrationResult VerifySeasonIdentityNormalization(
        L12AccountView actor, L12SeasonIdentityRecorderResult recorder, DateTimeOffset observedAt,
        L12AdminAuditContext context)
        => ExecuteSeasonIdentityStorageMutation((connection, transaction) =>
        {
            EnsureOperationsPermission(actor, L12Permission.AdminOperationsWrite);
            var marker = ReadPlatformSeasonIdentityMarker(connection, transaction)
                ?? throw new L12OperationsConfigException("season_identity_marker_missing",
                    "赛季编号迁移 marker 不存在");
            if (marker.Status == "verified")
                return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationResult>(false,
                    PlatformSeasonIdentityResult(marker, false, true));
            if (marker.Status != "platform_committed"
                || !string.Equals(recorder.MigrationId, PlatformSeasonIdentityMigrationId,
                    StringComparison.Ordinal)
                || !string.Equals(marker.ResultRecorderFingerprint, recorder.ResultFingerprint,
                    StringComparison.Ordinal))
                throw new L12OperationsConfigException("season_identity_verification_not_ready",
                    "赛季编号迁移尚未完成双库提交");
            var actual = ComputePlatformSeasonIdentityFingerprintLocked(connection, transaction);
            RequireSeasonIdentityFingerprint(marker.ResultPlatformFingerprint ?? string.Empty, actual,
                "season_identity_completed_state_drift");
            EnsureCanonicalSeasonIdentityLocked(marker);
            var now = observedAt.ToUniversalTime();
            using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = """
                UPDATE platform_season_identity_migrations
                SET status='verified',verified_utc=$verified
                WHERE migration_id=$migration AND status='platform_committed'
                  AND result_platform_fingerprint=$platform
                  AND result_recorder_fingerprint=$recorder;
                """;
            verify.Parameters.AddWithValue("$verified", now.ToString("O"));
            verify.Parameters.AddWithValue("$migration", PlatformSeasonIdentityMigrationId);
            verify.Parameters.AddWithValue("$platform", actual);
            verify.Parameters.AddWithValue("$recorder", recorder.ResultFingerprint);
            if (verify.ExecuteNonQuery() != 1)
                throw new L12OperationsConfigException("season_identity_verification_conflict",
                    "赛季编号迁移验证状态已变化");
            AddAdminAudit(actor, "operations", "season-identity-verify",
                $"season-identity:{PlatformSeasonIdentityMigrationId}", "platform_committed", "verified",
                $"platform={actual};recorder={recorder.ResultFingerprint}",
                context with { Outcome = "succeeded" });
            return new SeasonFinalizationStorageMutation<L12SeasonIdentityMigrationResult>(true,
                new(PlatformSeasonIdentityMigrationId, "verified", false, false, actual,
                    recorder.ResultFingerprint, marker.CompletedStorageRevision ?? 0,
                    marker.CompletedAt ?? now));
        });

    private L12SeasonIdentityMigrationPreview BuildSeasonIdentityPreviewLocked(
        SqliteConnection connection, SqliteTransaction transaction,
        L12SeasonIdentityRecorderPreview recorder, L12RankedSeasonCutoverReadiness readiness,
        DateTimeOffset now, PlatformSeasonIdentityMarker? marker)
    {
        if (marker is { Status: "platform_committed" or "verified" })
        {
            var actual = ComputePlatformSeasonIdentityFingerprintLocked(connection, transaction);
            var drifted = !string.Equals(actual, marker.ResultPlatformFingerprint, StringComparison.Ordinal);
            var recorderDrifted = !recorder.CanApply
                || !string.Equals(recorder.MigrationId, PlatformSeasonIdentityMigrationId,
                    StringComparison.Ordinal)
                || !string.Equals(recorder.ResultFingerprint, marker.ResultRecorderFingerprint,
                    StringComparison.Ordinal);
            var completedBlockers = recorder.BlockingCodes.ToList();
            if (drifted) completedBlockers.Add("season_identity_completed_state_drift");
            if (recorderDrifted)
                completedBlockers.Add("season_identity_recorder_completed_state_drift");
            return new(PlatformSeasonIdentityMigrationId, marker.Status,
                completedBlockers.Count == 0,
                actual, recorder.Fingerprint, marker.SeasonZeroDefinitionId,
                marker.SeasonOneDefinitionId, marker.B0EvidenceFingerprint, readiness,
                completedBlockers.Distinct(StringComparer.Ordinal).ToArray());
        }

        var plan = BuildPlatformSeasonIdentityPlanLocked(connection, transaction, readiness, now);
        var blockers = plan.BlockingCodes.Concat(recorder.BlockingCodes).Distinct(StringComparer.Ordinal).ToList();
        if (!recorder.CanApply) blockers.Add("season_identity_recorder_not_ready");
        if (marker is { Status: "executing", LeaseExpiresAt: { } lease } && lease > now)
            blockers.Add("season_identity_lease_conflict");
        return new(PlatformSeasonIdentityMigrationId,
            blockers.Count == 0 ? "ready" : marker?.Status ?? "blocked", blockers.Count == 0,
            plan.Fingerprint, recorder.Fingerprint, plan.Previous.SourceDefinitionId,
            plan.Current.DefinitionId, plan.Repair.EvidenceFingerprint, readiness,
            blockers.Distinct(StringComparer.Ordinal).ToArray());
    }

    private PlatformSeasonIdentityPlan BuildPlatformSeasonIdentityPlanLocked(
        SqliteConnection connection, SqliteTransaction transaction,
        L12RankedSeasonCutoverReadiness readiness, DateTimeOffset now)
    {
        var blockers = new List<string>();
        var current = _data.SeasonDefinitions.SingleOrDefault(row => row.LifecycleStatus == "active");
        if (current is null || !string.Equals(current.SeasonId, LegacySeasonOneIdentity, StringComparison.Ordinal))
            blockers.Add("season_identity_current_season_conflict");
        current ??= new SeasonDefinitionRow();
        var previousCandidates = _data.SeasonArchives.Where(row =>
            string.Equals(row.SeasonId, LegacySeasonZeroIdentity, StringComparison.Ordinal)
            && string.Equals(row.NextSeasonId, LegacySeasonOneIdentity, StringComparison.Ordinal)).ToArray();
        if (previousCandidates.Length != 1) blockers.Add("season_identity_previous_archive_conflict");
        var previous = previousCandidates.FirstOrDefault() ?? _data.SeasonArchives.FirstOrDefault()
            ?? new SeasonArchiveRow();
        var repairs = _data.RankedSeasonResetRepairs.Where(row =>
            string.Equals(row.SeasonId, LegacySeasonOneIdentity, StringComparison.Ordinal)
            && string.Equals(row.PreviousSeasonId, LegacySeasonZeroIdentity, StringComparison.Ordinal)).ToArray();
        if (repairs.Length != 1) blockers.Add("season_identity_b0_marker_missing");
        var repair = repairs.FirstOrDefault() ?? new RankedSeasonResetRepairRow();

        var operations = RequireOperationsConfig();
        if (!string.Equals(operations.Season.Id, LegacySeasonOneIdentity, StringComparison.Ordinal))
            blockers.Add("season_identity_runtime_conflict");
        var maintenanceActive = operations.ImmediateMaintenance.Enabled
            || ToPolicySnapshot(operations).IsMaintenanceActive(now);
        if (!maintenanceActive) blockers.Add("season_identity_maintenance_required");
        if (!readiness.Ready || !string.Equals(readiness.SeasonId, LegacySeasonOneIdentity,
                StringComparison.Ordinal)) blockers.Add("season_identity_ranked_not_ready");
        if (current.EndsAt is { } endsAt && now >= endsAt.ToUniversalTime())
            blockers.Add("season_identity_current_season_ended");
        if (_data.SeasonDefinitions.Any(row => row.ActivationPlan is
                { Status: not ("disarmed" or "completed" or "failed") }))
            blockers.Add("season_identity_activation_plan_pending");
        if (_data.SeasonDefinitions.Any(row => row.FinalizationLeaseExpiresAt is { } lease && lease > now))
            blockers.Add("season_identity_finalization_lease_active");
        if (CountPendingSeasonFinalizationRows(connection, transaction) > 0)
            blockers.Add("season_identity_finalization_pending");
        if (CountCanonicalZeroSeasonFinalizationRows(connection, transaction) > 0
            || TypedSeasonReferencesLocked().Any(value => value == CanonicalSeasonZeroIdentity))
            blockers.Add("season_identity_target_collision");
        if (HasSeasonFinalizationTargetCollision(connection, transaction))
            blockers.Add("season_identity_finalization_target_collision");
        if (TypedSeasonReferencesLocked().Any(value => IsMigrationIdentityVariant(value)
                && value is not (LegacySeasonZeroIdentity or LegacySeasonOneIdentity
                    or CanonicalSeasonZeroIdentity)))
            blockers.Add("season_identity_noncanonical_key");
        blockers.AddRange(AlternateArtSeasonReferenceBlockersLocked());

        var b0Matches = repair.TransitionMatchIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentAudits = _data.RankedIntegrityAudits.Where(row =>
                string.Equals(row.SeasonId, LegacySeasonOneIdentity, StringComparison.Ordinal))
            .Select(row => row.MatchId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!b0Matches.SetEquals(currentAudits)) blockers.Add("season_identity_b0_coverage_changed");
        if (AllSeasonIdentityCollectionCounts().Any(count => count > SeasonIdentityCollectionLimit))
            blockers.Add("season_identity_collection_limit_exceeded");

        return new(current, previous, repair,
            ComputePlatformSeasonIdentityFingerprintLocked(connection, transaction),
            blockers.Distinct(StringComparer.Ordinal).ToArray());
    }

    private void ApplyPlatformSeasonIdentityMappingLocked(PlatformSeasonIdentityPlan plan)
    {
        plan.Previous.SeasonOrdinal = 0;
        plan.Current.SeasonOrdinal = 1;
        foreach (var draft in _data.SeasonDefinitions.Where(row => row.LifecycleStatus == "draft"))
            draft.SeasonOrdinal ??= 2;

        _data.OperationsConfig!.Season.Id = MapSeasonIdentity(_data.OperationsConfig.Season.Id);
        foreach (var history in _data.OperationsConfigHistory)
            history.Config.Season.Id = MapSeasonIdentity(history.Config.Season.Id);
        if (_data.RankedPendingGradient is not null)
            _data.RankedPendingGradient.AfterSeasonId = MapSeasonIdentity(
                _data.RankedPendingGradient.AfterSeasonId);
        foreach (var row in _data.SeasonDefinitions)
        {
            row.SeasonId = MapSeasonIdentity(row.SeasonId);
            row.PreviousSeasonId = MapNullableSeasonIdentity(row.PreviousSeasonId);
            row.NextSeasonId = MapNullableSeasonIdentity(row.NextSeasonId);
        }
        foreach (var row in _data.SeasonArchives)
        {
            row.SeasonId = MapSeasonIdentity(row.SeasonId);
            row.PreviousSeasonId = MapNullableSeasonIdentity(row.PreviousSeasonId);
            row.NextSeasonId = MapNullableSeasonIdentity(row.NextSeasonId);
        }
        foreach (var row in _data.RankedProfiles) row.SeasonId = MapSeasonIdentity(row.SeasonId);
        foreach (var row in _data.RankedSeasonResetRepairs)
        {
            row.SeasonId = MapSeasonIdentity(row.SeasonId);
            row.PreviousSeasonId = MapSeasonIdentity(row.PreviousSeasonId);
        }
        foreach (var row in _data.RankedProfileHistory) row.SeasonId = MapSeasonIdentity(row.SeasonId);
        foreach (var row in _data.RankedSettlements)
            if (!string.IsNullOrWhiteSpace(row.SeasonId)) row.SeasonId = MapSeasonIdentity(row.SeasonId);
        foreach (var row in _data.RankedMasterRecords)
            if (!row.SeasonId.Equals("$rolling-master-title-720h-v1", StringComparison.Ordinal))
                row.SeasonId = MapSeasonIdentity(row.SeasonId);
        foreach (var row in _data.RankedIntegrityAudits) row.SeasonId = MapSeasonIdentity(row.SeasonId);
        foreach (var row in _data.RankedHeldRewards)
        {
            row.SeasonId = MapSeasonIdentity(row.SeasonId);
            MapRankedSnapshot(row.FirstBefore); MapRankedSnapshot(row.FirstAfter);
            MapRankedSnapshot(row.SecondBefore); MapRankedSnapshot(row.SecondAfter);
        }
        foreach (var row in _data.RankedSettlementProfileFacts)
        {
            MapRankedSnapshot(row.FirstBefore); MapRankedSnapshot(row.FirstAfter);
            MapRankedSnapshot(row.SecondBefore); MapRankedSnapshot(row.SecondAfter);
        }
        foreach (var row in _data.RankedIntegrityCorrections)
        {
            MapRankedSnapshot(row.Before); MapRankedSnapshot(row.After);
        }
        foreach (var row in _data.AlternateArtAwardRules)
            if (!string.IsNullOrWhiteSpace(row.SeasonId)) row.SeasonId = MapSeasonIdentity(row.SeasonId);
        foreach (var row in _data.AlternateArtGrants) MapAlternateArtSeasonReference(row);
    }

    private void EnsureCanonicalSeasonIdentityLocked(PlatformSeasonIdentityMarker marker)
    {
        var current = _data.SeasonDefinitions.Single(row => row.LifecycleStatus == "active");
        var previous = _data.SeasonArchives.Single(row => row.SourceDefinitionId == marker.SeasonZeroDefinitionId);
        if (current.DefinitionId != marker.SeasonOneDefinitionId || current.SeasonOrdinal != 1
            || current.SeasonId != CanonicalSeasonOneIdentity || previous.SeasonOrdinal != 0
            || previous.SeasonId != CanonicalSeasonZeroIdentity
            || _data.OperationsConfig!.Season.Id != CanonicalSeasonOneIdentity
            || TypedSeasonReferencesLocked().Any(value => value == LegacySeasonOneIdentity))
            throw new L12OperationsConfigException("season_identity_verification_failed",
                "赛季编号迁移后强类型引用未完全归一");
    }

    private IEnumerable<string> TypedSeasonReferencesLocked()
    {
        yield return _data.OperationsConfig?.Season.Id ?? string.Empty;
        foreach (var row in _data.OperationsConfigHistory) yield return row.Config.Season.Id;
        if (_data.RankedPendingGradient is not null) yield return _data.RankedPendingGradient.AfterSeasonId;
        foreach (var row in _data.SeasonDefinitions)
        {
            yield return row.SeasonId;
            if (row.PreviousSeasonId is not null) yield return row.PreviousSeasonId;
            if (row.NextSeasonId is not null) yield return row.NextSeasonId;
        }
        foreach (var row in _data.SeasonArchives)
        {
            yield return row.SeasonId;
            if (row.PreviousSeasonId is not null) yield return row.PreviousSeasonId;
            if (row.NextSeasonId is not null) yield return row.NextSeasonId;
        }
        foreach (var row in _data.RankedProfiles) yield return row.SeasonId;
        foreach (var row in _data.RankedSeasonResetRepairs)
        { yield return row.SeasonId; yield return row.PreviousSeasonId; }
        foreach (var row in _data.RankedProfileHistory) yield return row.SeasonId;
        foreach (var row in _data.RankedSettlements.Where(row => !string.IsNullOrWhiteSpace(row.SeasonId)))
            yield return row.SeasonId;
        foreach (var row in _data.RankedMasterRecords.Where(row =>
                     !row.SeasonId.Equals("$rolling-master-title-720h-v1", StringComparison.Ordinal)))
            yield return row.SeasonId;
        foreach (var row in _data.RankedIntegrityAudits) yield return row.SeasonId;
        foreach (var row in _data.RankedHeldRewards)
        {
            yield return row.SeasonId;
            foreach (var value in SnapshotSeasonIds(row.FirstBefore, row.FirstAfter,
                         row.SecondBefore, row.SecondAfter)) yield return value;
        }
        foreach (var row in _data.RankedSettlementProfileFacts)
            foreach (var value in SnapshotSeasonIds(row.FirstBefore, row.FirstAfter,
                         row.SecondBefore, row.SecondAfter)) yield return value;
        foreach (var row in _data.RankedIntegrityCorrections)
            foreach (var value in SnapshotSeasonIds(row.Before, row.After)) yield return value;
        foreach (var row in _data.AlternateArtAwardRules.Where(row => !string.IsNullOrWhiteSpace(row.SeasonId)))
            yield return row.SeasonId;
    }

    private static IEnumerable<string> SnapshotSeasonIds(params RankedProfileSnapshotRow[] rows)
        => rows.Select(row => row.SeasonId);

    private int[] AllSeasonIdentityCollectionCounts() =>
    [
        _data.OperationsConfigHistory.Count, _data.SeasonDefinitions.Count, _data.SeasonArchives.Count,
        _data.RankedProfiles.Count, _data.RankedSeasonResetRepairs.Count, _data.RankedProfileHistory.Count,
        _data.RankedSettlements.Count, _data.RankedMasterRecords.Count, _data.RankedIntegrityAudits.Count,
        _data.RankedHeldRewards.Count, _data.RankedSettlementProfileFacts.Count,
        _data.RankedIntegrityCorrections.Count, _data.AlternateArtAwardRules.Count,
        _data.AlternateArtGrants.Count,
    ];

    private IEnumerable<string> AlternateArtSeasonReferenceBlockersLocked()
    {
        foreach (var row in _data.AlternateArtGrants)
        {
            var kind = row.SourceKind.Trim().ToLowerInvariant();
            var reference = row.SourceReference.Trim();
            if (kind is "manual" or "event" || string.IsNullOrWhiteSpace(reference)) continue;
            var targetCollision = kind switch
            {
                "rank-reached" or "season-final" =>
                    reference.Equals(CanonicalSeasonZeroIdentity, StringComparison.Ordinal),
                "master-champion-season-final" =>
                    reference.StartsWith(CanonicalSeasonZeroIdentity + ":", StringComparison.Ordinal),
                "ranked-participants" => reference.Equals(
                    "ranked-participants:" + CanonicalSeasonZeroIdentity, StringComparison.Ordinal),
                _ => false,
            };
            if (targetCollision)
            {
                yield return "season_identity_alternate_art_target_collision";
                continue;
            }
            var parsed = kind switch
            {
                "rank-reached" or "season-final" => reference is LegacySeasonZeroIdentity or LegacySeasonOneIdentity,
                "master-champion-season-final" => reference.StartsWith(LegacySeasonZeroIdentity + ":",
                    StringComparison.Ordinal) || reference.StartsWith(LegacySeasonOneIdentity + ":",
                    StringComparison.Ordinal),
                "ranked-participants" => reference is "ranked-participants:S01" or "ranked-participants:T01",
                _ => false,
            };
            var suspicious = reference.Equals(LegacySeasonZeroIdentity, StringComparison.OrdinalIgnoreCase)
                || reference.Equals(LegacySeasonOneIdentity, StringComparison.OrdinalIgnoreCase)
                || reference.Equals(CanonicalSeasonZeroIdentity, StringComparison.OrdinalIgnoreCase)
                || reference.StartsWith(LegacySeasonZeroIdentity + ":", StringComparison.OrdinalIgnoreCase)
                || reference.StartsWith(LegacySeasonOneIdentity + ":", StringComparison.OrdinalIgnoreCase)
                || reference.StartsWith(CanonicalSeasonZeroIdentity + ":", StringComparison.OrdinalIgnoreCase)
                || reference.EndsWith(":" + LegacySeasonZeroIdentity, StringComparison.OrdinalIgnoreCase)
                || reference.EndsWith(":" + LegacySeasonOneIdentity, StringComparison.OrdinalIgnoreCase)
                || reference.EndsWith(":" + CanonicalSeasonZeroIdentity, StringComparison.OrdinalIgnoreCase);
            if (!parsed && suspicious) yield return "season_identity_unknown_alternate_art_reference";
        }
    }

    private static void MapAlternateArtSeasonReference(AlternateArtGrantRow row)
    {
        var kind = row.SourceKind.Trim().ToLowerInvariant();
        var reference = row.SourceReference.Trim();
        row.SourceReference = kind switch
        {
            "rank-reached" or "season-final" => MapSeasonIdentity(reference),
            "master-champion-season-final" when reference.Contains(':') =>
                MapSeasonIdentity(reference[..reference.IndexOf(':')]) + reference[reference.IndexOf(':')..],
            "ranked-participants" when reference.StartsWith("ranked-participants:", StringComparison.Ordinal) =>
                "ranked-participants:" + MapSeasonIdentity(reference["ranked-participants:".Length..]),
            _ => row.SourceReference,
        };
    }

    private static void MapRankedSnapshot(RankedProfileSnapshotRow row)
        => row.SeasonId = MapSeasonIdentity(row.SeasonId);

    private static string MapSeasonIdentity(string value) => value switch
    {
        LegacySeasonZeroIdentity => CanonicalSeasonZeroIdentity,
        LegacySeasonOneIdentity => CanonicalSeasonOneIdentity,
        _ => value,
    };

    private static string CanonicalSeasonIdentityForOrdinal(int ordinal)
        => $"S{ordinal:D2}";

    private static string? MapNullableSeasonIdentity(string? value)
        => value is null ? null : MapSeasonIdentity(value);

    private static bool IsMigrationIdentityVariant(string value)
        => value.Trim().Equals(LegacySeasonZeroIdentity, StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals(LegacySeasonOneIdentity, StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals(CanonicalSeasonZeroIdentity, StringComparison.OrdinalIgnoreCase);

    private static void RequireSeasonIdentityFingerprint(string expected, string actual, string code)
    {
        var normalized = expected?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(normalized),
                Encoding.ASCII.GetBytes(actual)))
            throw new L12OperationsConfigException(code, "赛季编号迁移证据已变化，请重新预览");
    }

    private string ComputePlatformSeasonIdentityFingerprintLocked(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var coordination = ReadSeasonFinalizationEvidence(connection, transaction);
        var payload = JsonSerializer.Serialize(new
        {
            operations = _data.OperationsConfig,
            operationsHistory = _data.OperationsConfigHistory,
            pendingGradient = _data.RankedPendingGradient,
            definitions = _data.SeasonDefinitions,
            archives = _data.SeasonArchives,
            profiles = _data.RankedProfiles,
            repairs = _data.RankedSeasonResetRepairs,
            history = _data.RankedProfileHistory,
            settlements = _data.RankedSettlements,
            masterRecords = _data.RankedMasterRecords,
            audits = _data.RankedIntegrityAudits,
            held = _data.RankedHeldRewards,
            profileFacts = _data.RankedSettlementProfileFacts,
            corrections = _data.RankedIntegrityCorrections,
            awardRules = _data.AlternateArtAwardRules,
            grants = _data.AlternateArtGrants,
            coordination,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string[] ReadSeasonFinalizationEvidence(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT definition_id||'|'||season_id||'|'||status||'|'||COALESCE(completed_storage_revision,'')
            FROM season_finalization_coordination ORDER BY definition_id,season_id;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows.ToArray();
    }

    private static int CountPendingSeasonFinalizationRows(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM season_finalization_coordination WHERE status<>'finalized';
            """;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountCanonicalZeroSeasonFinalizationRows(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM season_finalization_coordination WHERE season_id='S00';
            """;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool HasSeasonFinalizationTargetCollision(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM season_finalization_coordination source
            JOIN season_finalization_coordination target
              ON target.definition_id=source.definition_id
             AND target.season_id=CASE source.season_id WHEN 'S01' THEN 'S00' ELSE 'S01' END
            WHERE source.season_id IN ('S01','T01') AND target.season_id<>source.season_id;
            """;
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void MigrateSeasonFinalizationCoordination(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE season_finalization_coordination
            SET season_id=CASE season_id WHEN 'S01' THEN 'S00' ELSE 'S01' END
            WHERE season_id IN ('S01','T01');
            """;
        command.ExecuteNonQuery();
    }

    private static PlatformSeasonIdentityMarker? ReadPlatformSeasonIdentityMarker(
        SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT status,owner,lease_expires_utc,source_platform_fingerprint,
                   result_platform_fingerprint,source_recorder_fingerprint,
                   result_recorder_fingerprint,season_zero_definition_id,
                   season_one_definition_id,b0_evidence_fingerprint,completed_storage_revision,
                   started_utc,completed_utc,verified_utc
            FROM platform_season_identity_migrations WHERE migration_id=$migration;
            """;
        command.Parameters.AddWithValue("$migration", PlatformSeasonIdentityMigrationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8),
            reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetInt64(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
            reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)));
    }

    private static L12SeasonIdentityMigrationResult PlatformSeasonIdentityResult(
        PlatformSeasonIdentityMarker marker, bool applied, bool replayed)
        => new(PlatformSeasonIdentityMigrationId, marker.Status, applied, replayed,
            marker.ResultPlatformFingerprint ?? marker.SourcePlatformFingerprint,
            marker.ResultRecorderFingerprint ?? marker.SourceRecorderFingerprint,
            marker.CompletedStorageRevision ?? 0, marker.CompletedAt ?? marker.StartedAt);

    private bool IsSeasonIdentityMigrationFencedLocked()
    {
        if (!_storageWritable) return true;
        try
        {
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM platform_season_identity_migrations
                WHERE migration_id=$migration AND status IN ('executing','platform_committed');
                """;
            command.Parameters.AddWithValue("$migration", PlatformSeasonIdentityMigrationId);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
        catch { return true; }
    }
}
