using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace TwelveLegions.Server;

internal sealed record L12SeasonIdentityRecorderPreview(
    string MigrationId,
    string Status,
    bool CanApply,
    string Fingerprint,
    int LegacySeasonZeroMatches,
    int LegacySeasonOneMatches,
    int CanonicalSeasonZeroCollisions,
    int ActiveRankedRuntimes,
    int PendingSettlements,
    int AppliedReconciliationFailures,
    int QuarantinedSettlements,
    IReadOnlyList<string> BlockingCodes);

internal sealed record L12SeasonIdentityRecorderResult(
    string MigrationId,
    bool Applied,
    bool Replayed,
    int SeasonZeroMatchesMigrated,
    int SeasonOneMatchesMigrated,
    string SourceFingerprint,
    string ResultFingerprint,
    DateTimeOffset CompletedAt);

internal sealed class L12SeasonIdentityMigrationException : InvalidOperationException
{
    public string Code { get; }

    public L12SeasonIdentityMigrationException(string code, string message) : base(message)
        => Code = code;
}

public sealed partial class MatchRecorder
{
    private const string SeasonIdentityMigrationId = "season-id-normalization-s00-s01-v1";
    private const string LegacySeasonZeroId = "S01";
    private const string LegacySeasonOneId = "T01";
    private const string CanonicalSeasonZeroId = "S00";
    private const string SeasonIdentitySwapSentinel = "$season-id-normalization-s00-s01-v1";

    private sealed record SeasonIdentityRecorderMarker(
        string Status,
        string Owner,
        DateTimeOffset LeaseExpiresAt,
        string SourceFingerprint,
        string? ResultFingerprint,
        int SeasonZeroMatches,
        int SeasonOneMatches,
        DateTimeOffset? CompletedAt);

    private sealed record SeasonIdentityRecorderSnapshot(
        string Fingerprint,
        int LegacySeasonZeroMatches,
        int LegacySeasonOneMatches,
        int CanonicalSeasonZeroCollisions,
        int ActiveRankedRuntimes,
        int PendingSettlements,
        int AppliedReconciliationFailures,
        int QuarantinedSettlements,
        int NonCanonicalKnownKeys,
        int SentinelCollisions,
        IReadOnlyList<string> BlockingCodes);

    private static async Task InitializeSeasonIdentityMigrationSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS season_identity_migrations (
                migration_id TEXT PRIMARY KEY,
                status TEXT NOT NULL CHECK(status IN ('executing','recorder_committed')),
                owner TEXT NOT NULL,
                lease_expires_utc TEXT NOT NULL,
                source_fingerprint TEXT NOT NULL,
                result_fingerprint TEXT,
                season_zero_matches INTEGER NOT NULL,
                season_one_matches INTEGER NOT NULL,
                started_utc TEXT NOT NULL,
                completed_utc TEXT
            );
            CREATE TABLE IF NOT EXISTS season_identity_migration_matches (
                migration_id TEXT NOT NULL,
                match_id TEXT NOT NULL,
                source_season_id TEXT NOT NULL,
                target_season_id TEXT NOT NULL,
                outbox_status TEXT NOT NULL,
                outbox_payload_hash TEXT NOT NULL,
                PRIMARY KEY(migration_id,match_id),
                FOREIGN KEY(migration_id) REFERENCES season_identity_migrations(migration_id)
                    ON DELETE RESTRICT,
                FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE RESTRICT
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<L12SeasonIdentityRecorderPreview> PreviewSeasonIdentityNormalizationAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenWriteConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(
            cancellationToken);
        var marker = await ReadSeasonIdentityMarkerAsync(connection, transaction, cancellationToken);
        if (marker is not null && marker.Status == "recorder_committed")
        {
            var currentFingerprint = await ComputeCommittedSeasonIdentityFingerprintAsync(connection,
                transaction, cancellationToken);
            var drifted = !string.Equals(marker.ResultFingerprint, currentFingerprint,
                StringComparison.Ordinal);
            await transaction.CommitAsync(cancellationToken);
            return new(SeasonIdentityMigrationId, "recorder_committed", !drifted,
                marker.SourceFingerprint, marker.SeasonZeroMatches, marker.SeasonOneMatches, 0,
                0, 0, 0, 0, drifted ? ["season_identity_completed_state_drift"] : []);
        }

        var snapshot = await ReadSeasonIdentitySnapshotAsync(connection, transaction,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var preview = ToPreview(snapshot);
        if (marker is null || marker.Status != "executing"
            || marker.LeaseExpiresAt <= _utcNow().ToUniversalTime()) return preview;
        return preview with
        {
            Status = "executing",
            CanApply = false,
            BlockingCodes = preview.BlockingCodes
                .Append("season_identity_lease_conflict").Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    internal async Task<L12SeasonIdentityRecorderResult> ApplySeasonIdentityNormalizationAsync(
        string expectedFingerprint, string owner, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
            throw new ArgumentException("赛季编号迁移需要 preview 指纹", nameof(expectedFingerprint));
        owner = owner?.Trim() ?? string.Empty;
        if (owner.Length is < 1 or > 120)
            throw new ArgumentException("赛季编号迁移 owner 无效", nameof(owner));
        if (leaseDuration < TimeSpan.FromSeconds(1) || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "赛季编号迁移租约必须为 1 秒至 15 分钟");

        await using var connection = await OpenWriteConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var marker = await ReadSeasonIdentityMarkerAsync(connection, transaction, cancellationToken);
        if (marker is not null && marker.Status == "recorder_committed")
        {
            var currentFingerprint = await ComputeCommittedSeasonIdentityFingerprintAsync(connection,
                transaction, cancellationToken);
            if (!string.Equals(marker.ResultFingerprint, currentFingerprint, StringComparison.Ordinal))
                throw new L12SeasonIdentityMigrationException("season_identity_completed_state_drift",
                    "比赛库赛季编号迁移完成后的事实发生漂移，已拒绝幂等重放");
            await transaction.CommitAsync(cancellationToken);
            return new(SeasonIdentityMigrationId, false, true, marker.SeasonZeroMatches,
                marker.SeasonOneMatches, marker.SourceFingerprint, currentFingerprint,
                marker.CompletedAt ?? _utcNow().ToUniversalTime());
        }

        var snapshot = await ReadSeasonIdentitySnapshotAsync(connection, transaction,
            cancellationToken);
        if (!snapshot.Fingerprint.Equals(expectedFingerprint, StringComparison.Ordinal))
            throw new L12SeasonIdentityMigrationException("season_identity_preview_expired",
                "比赛库赛季编号迁移 preview 已失效，请重新预览");
        if (snapshot.BlockingCodes.Count > 0)
        {
            var code = snapshot.BlockingCodes[0];
            throw new L12SeasonIdentityMigrationException(code,
                $"比赛库赛季编号迁移被安全门禁拒绝：{string.Join(',', snapshot.BlockingCodes)}");
        }

        var now = _utcNow().ToUniversalTime();
        var claim = connection.CreateCommand();
        claim.Transaction = transaction;
        claim.CommandText = """
            INSERT INTO season_identity_migrations(
                migration_id,status,owner,lease_expires_utc,source_fingerprint,result_fingerprint,
                season_zero_matches,season_one_matches,started_utc,completed_utc)
            VALUES($migration,'executing',$owner,$lease,$source,NULL,$zero,$one,$started,NULL)
            ON CONFLICT(migration_id) DO UPDATE SET
                status='executing',owner=excluded.owner,lease_expires_utc=excluded.lease_expires_utc,
                source_fingerprint=excluded.source_fingerprint,result_fingerprint=NULL,
                season_zero_matches=excluded.season_zero_matches,
                season_one_matches=excluded.season_one_matches,started_utc=excluded.started_utc,
                completed_utc=NULL
            WHERE season_identity_migrations.status='executing'
              AND season_identity_migrations.lease_expires_utc<=$started;
            """;
        claim.Parameters.AddWithValue("$migration", SeasonIdentityMigrationId);
        claim.Parameters.AddWithValue("$owner", owner);
        claim.Parameters.AddWithValue("$lease", now.Add(leaseDuration).ToString("O"));
        claim.Parameters.AddWithValue("$source", snapshot.Fingerprint);
        claim.Parameters.AddWithValue("$zero", snapshot.LegacySeasonZeroMatches);
        claim.Parameters.AddWithValue("$one", snapshot.LegacySeasonOneMatches);
        claim.Parameters.AddWithValue("$started", now.ToString("O"));
        if (await claim.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new L12SeasonIdentityMigrationException("season_identity_lease_conflict",
                "比赛库赛季编号迁移已由另一 owner 持有");

        var evidence = connection.CreateCommand();
        evidence.Transaction = transaction;
        evidence.CommandText = """
            DELETE FROM season_identity_migration_matches WHERE migration_id=$migration;
            INSERT INTO season_identity_migration_matches(
                migration_id,match_id,source_season_id,target_season_id,outbox_status,outbox_payload_hash)
            SELECT $migration,m.match_id,m.season_id,
                   CASE m.season_id WHEN 'S01' THEN 'S00' ELSE 'S01' END,
                   COALESCE(o.status,''),COALESCE(o.payload_hash,'')
            FROM matches m
            LEFT JOIN ranked_settlement_outbox o ON o.match_id=m.match_id
            WHERE m.season_id IN ('S01','T01')
            ORDER BY m.match_id;
            """;
        evidence.Parameters.AddWithValue("$migration", SeasonIdentityMigrationId);
        await evidence.ExecuteNonQueryAsync(cancellationToken);

        var firstSwap = await UpdateSeasonIdentityAsync(connection, transaction,
            LegacySeasonZeroId, SeasonIdentitySwapSentinel, cancellationToken);
        var secondSwap = await UpdateSeasonIdentityAsync(connection, transaction,
            LegacySeasonOneId, LegacySeasonZeroId, cancellationToken);
        var thirdSwap = await UpdateSeasonIdentityAsync(connection, transaction,
            SeasonIdentitySwapSentinel, CanonicalSeasonZeroId, cancellationToken);
        if (firstSwap != snapshot.LegacySeasonZeroMatches
            || secondSwap != snapshot.LegacySeasonOneMatches
            || thirdSwap != snapshot.LegacySeasonZeroMatches)
            throw new L12SeasonIdentityMigrationException("season_identity_update_count_conflict",
                "比赛库赛季编号迁移条数与 preview 不一致");

        var resultFingerprint = await ComputeCommittedSeasonIdentityFingerprintAsync(connection,
            transaction, cancellationToken);
        var completedAt = _utcNow().ToUniversalTime();
        var complete = connection.CreateCommand();
        complete.Transaction = transaction;
        complete.CommandText = """
            UPDATE season_identity_migrations
            SET status='recorder_committed',result_fingerprint=$result,completed_utc=$completed,
                lease_expires_utc=$completed
            WHERE migration_id=$migration AND status='executing' AND owner=$owner
              AND source_fingerprint=$source AND lease_expires_utc>=$completed;
            """;
        complete.Parameters.AddWithValue("$result", resultFingerprint);
        complete.Parameters.AddWithValue("$completed", completedAt.ToString("O"));
        complete.Parameters.AddWithValue("$migration", SeasonIdentityMigrationId);
        complete.Parameters.AddWithValue("$owner", owner);
        complete.Parameters.AddWithValue("$source", snapshot.Fingerprint);
        if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new L12SeasonIdentityMigrationException("season_identity_lease_lost",
                "比赛库赛季编号迁移提交前失去 owner 租约");

        StorageFailureInjector?.Invoke("before-season-identity-recorder-commit");
        await transaction.CommitAsync(cancellationToken);
        InvalidateAnalyticsCache();
        return new(SeasonIdentityMigrationId, true, false, snapshot.LegacySeasonZeroMatches,
            snapshot.LegacySeasonOneMatches, snapshot.Fingerprint, resultFingerprint, completedAt);
    }

    private static L12SeasonIdentityRecorderPreview ToPreview(SeasonIdentityRecorderSnapshot snapshot)
        => new(SeasonIdentityMigrationId, snapshot.BlockingCodes.Count == 0 ? "ready" : "blocked",
            snapshot.BlockingCodes.Count == 0, snapshot.Fingerprint,
            snapshot.LegacySeasonZeroMatches, snapshot.LegacySeasonOneMatches,
            snapshot.CanonicalSeasonZeroCollisions, snapshot.ActiveRankedRuntimes,
            snapshot.PendingSettlements, snapshot.AppliedReconciliationFailures,
            snapshot.QuarantinedSettlements, snapshot.BlockingCodes);

    private async Task<SeasonIdentityRecorderSnapshot> ReadSeasonIdentitySnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var legacyZero = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM matches WHERE season_id='S01';", cancellationToken);
        var legacyOne = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM matches WHERE season_id='T01';", cancellationToken);
        var collision = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM matches WHERE season_id='S00';", cancellationToken);
        var nonCanonical = await CountAsync(connection, transaction, """
            SELECT COUNT(*) FROM matches
            WHERE UPPER(TRIM(COALESCE(season_id,''))) IN ('S00','S01','T01')
              AND COALESCE(season_id,'') NOT IN ('S00','S01','T01');
            """, cancellationToken);
        var sentinel = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM matches WHERE season_id='$season-id-normalization-s00-s01-v1';",
            cancellationToken);
        var active = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM ranked_match_runtime WHERE status='active';", cancellationToken);
        var pending = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='pending';", cancellationToken);
        var reconciliation = await CountAsync(connection, transaction, """
            SELECT COUNT(*) FROM ranked_settlement_outbox
            WHERE status='applied' AND last_error IS NOT NULL;
            """, cancellationToken);
        var quarantined = await CountAsync(connection, transaction, """
            SELECT
                (SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='quarantined')
              + (SELECT COUNT(*) FROM ranked_recovery_quarantine);
            """, cancellationToken);

        var blockers = new List<string>();
        if (collision > 0) blockers.Add("season_identity_target_collision");
        if (nonCanonical > 0) blockers.Add("season_identity_noncanonical_key");
        if (sentinel > 0) blockers.Add("season_identity_sentinel_collision");
        if (active > 0) blockers.Add("season_identity_active_ranked_runtime");
        if (pending > 0) blockers.Add("season_identity_pending_outbox");
        if (reconciliation > 0) blockers.Add("season_identity_reconciliation_failure");
        if (quarantined > 0) blockers.Add("season_identity_quarantine");
        var fingerprint = await ComputeSeasonIdentityFingerprintAsync(connection, transaction,
            cancellationToken, active, pending, reconciliation, quarantined);
        return new(fingerprint, legacyZero, legacyOne, collision, active, pending, reconciliation,
            quarantined, nonCanonical, sentinel, blockers);
    }

    private async Task<string> ComputeSeasonIdentityFingerprintAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken, int? active = null,
        int? pending = null, int? reconciliation = null, int? quarantined = null)
    {
        active ??= await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM ranked_match_runtime WHERE status='active';", cancellationToken);
        pending ??= await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='pending';", cancellationToken);
        reconciliation ??= await CountAsync(connection, transaction, """
            SELECT COUNT(*) FROM ranked_settlement_outbox
            WHERE status='applied' AND last_error IS NOT NULL;
            """, cancellationToken);
        quarantined ??= await CountAsync(connection, transaction, """
            SELECT
                (SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='quarantined')
              + (SELECT COUNT(*) FROM ranked_recovery_quarantine);
            """, cancellationToken);
        var evidence = new StringBuilder()
            .Append("v1|active=").Append(active)
            .Append("|pending=").Append(pending)
            .Append("|reconciliation=").Append(reconciliation)
            .Append("|quarantined=").Append(quarantined).AppendLine();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT m.match_id,m.season_id,COALESCE(o.status,''),COALESCE(o.payload_hash,'')
            FROM matches m
            LEFT JOIN ranked_settlement_outbox o ON o.match_id=m.match_id
            WHERE m.season_id IN ('S00','S01','T01','$season-id-normalization-s00-s01-v1')
               OR UPPER(TRIM(COALESCE(m.season_id,''))) IN ('S00','S01','T01')
            ORDER BY m.match_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            evidence.Append(reader.GetString(0)).Append('|')
                .Append(reader.GetString(1)).Append('|')
                .Append(reader.GetString(2)).Append('|')
                .Append(reader.GetString(3)).AppendLine();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())))
            .ToLowerInvariant();
    }

    private static async Task<string> ComputeCommittedSeasonIdentityFingerprintAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var evidence = new StringBuilder("v1|committed\n");
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT e.match_id,e.source_season_id,e.target_season_id,
                   COALESCE(m.season_id,''),e.outbox_status,e.outbox_payload_hash,
                   COALESCE(o.status,''),COALESCE(o.payload_hash,'')
            FROM season_identity_migration_matches e
            LEFT JOIN matches m ON m.match_id=e.match_id
            LEFT JOIN ranked_settlement_outbox o ON o.match_id=e.match_id
            WHERE e.migration_id=$migration
            ORDER BY e.match_id;
            """;
        command.Parameters.AddWithValue("$migration", SeasonIdentityMigrationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            for (var index = 0; index < reader.FieldCount; index++)
                evidence.Append(reader.GetString(index)).Append('|');
            evidence.AppendLine();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())))
            .ToLowerInvariant();
    }

    private static async Task<int> CountAsync(SqliteConnection connection,
        SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> UpdateSeasonIdentityAsync(SqliteConnection connection,
        SqliteTransaction transaction, string source, string target,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE matches SET season_id=$target WHERE season_id=$source;";
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$source", source);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SeasonIdentityRecorderMarker?> ReadSeasonIdentityMarkerAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT status,owner,lease_expires_utc,source_fingerprint,result_fingerprint,
                   season_zero_matches,season_one_matches,completed_utc
            FROM season_identity_migrations WHERE migration_id=$migration;
            """;
        command.Parameters.AddWithValue("$migration", SeasonIdentityMigrationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5),
            reader.GetInt32(6), reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));
    }
}
