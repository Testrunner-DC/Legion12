using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12AccountRankedRestrictionView(string AccountId, DateTimeOffset? RestrictionUntil,
    string? Reason, DateTimeOffset? SetAt, string? SetByAccountId, DateTimeOffset? ClearedAt,
    DateTimeOffset? EffectiveRestrictionUntil, long Version);

public sealed record L12AccountRankedRestrictionOperation(bool Applied,
    L12AccountRankedRestrictionView Restriction, bool AlreadyApplied);

internal sealed record L12AccountRankedRestrictionPayload(string AccountId, int? RestrictionDays,
    string Reason, bool Clear);

public sealed partial class L12PlatformStore
{
    private sealed class AccountRankedRestrictionRow
    {
        public DateTimeOffset SetAt { get; set; }
        public DateTimeOffset RestrictionUntil { get; set; }
        public string SetByAccountId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset? ClearedAt { get; set; }
        public string? ClearedByAccountId { get; set; }
    }

    // The platform schema, not the gameplay checkpoint contract, is the minimum
    // reader boundary. Published schema-9 readers reject schema 10 before JSON
    // fallback. A JSON mirror alone is not an old-program recovery format.
    private static void InitializeAccountRankedRestrictionSchema(SqliteConnection connection)
    {
        var version = ReadExistingStorageSchemaVersion(connection);
        if (version == PlatformStorageSchemaVersion) return;
        if (version != RankedBroadcastObjectSchemaVersion)
            throw new L12PlatformStorageIncompatibleException("账号排位限制迁移需要完整 schema 9 数据库");
        using var transaction = connection.BeginTransaction(deferred: false);
        var snapshot = ReadSnapshot(connection, transaction);
        if (snapshot is { } stored) DeserializeDataAndValidate(ValidateSnapshot(stored));
        // There is no data rewrite: missing optional state means unrestricted.
        // Both version markers advance atomically; failure leaves schema 9 intact.
        SetStorageSchemaVersion(connection, transaction, PlatformStorageSchemaVersion);
        transaction.Commit();
    }

    private static void ValidateAccountRankedRestrictions(DataFile data)
    {
        foreach (var account in data.Accounts)
        {
            if (account.RankedRestriction is not { } row) continue;
            if (row.SetAt == default || row.RestrictionUntil <= row.SetAt
                || (row.RestrictionUntil - row.SetAt).TotalDays > 3650
                || string.IsNullOrWhiteSpace(row.SetByAccountId)
                || !data.Accounts.Any(actor => actor.Id == row.SetByAccountId)
                || string.IsNullOrWhiteSpace(row.Reason) || row.Reason.Length > 500
                || (row.ClearedAt is null) != (row.ClearedByAccountId is null)
                || (row.ClearedAt is { } clearedAt && clearedAt < row.SetAt)
                || (row.ClearedByAccountId is { } clearedBy
                    && !data.Accounts.Any(actor => actor.Id == clearedBy)))
                throw new L12PlatformStorageIncompatibleException("账号手工排位限制状态无效，不能回退为无限制账号");
        }
    }

    public L12AccountRankedRestrictionView AccountRankedRestriction(L12AccountView actor, string accountId)
    {
        EnsurePermission(actor, L12Permission.AdminAccountsRead);
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在或已删除");
            return AccountRankedRestrictionViewLocked(account, DateTimeOffset.UtcNow);
        }
    }

    internal L12AccountRankedRestrictionOperation SetAccountRankedRestriction(L12AccountView actor,
        L12AccountRankedRestrictionPayload input, L12AdminAuditContext audit, bool apply)
    {
        EnsurePermission(actor, L12Permission.AdminAccountStatusWrite);
        var reason = input.Reason.Trim();
        if (reason.Length is < 1 or > 500)
            throw new ArgumentException("请填写1至500字的禁赛变更理由");
        if (input.Clear ? input.RestrictionDays is not null : input.RestrictionDays is not (>= 1 and <= 3650))
            throw new ArgumentException("禁赛天数必须为1至3650的整数；解除禁赛不得附带天数");
        lock (_gate)
        {
            var account = _data.Accounts.FirstOrDefault(row => row.Id == input.AccountId && !row.Deleted)
                ?? throw new KeyNotFoundException("账号不存在或已删除");
            if (account.Username == "Admin" || account.Id == actor.Id)
                throw new UnauthorizedAccessException("不能通过此入口限制根管理员或自己的排位资格");
            var now = DateTimeOffset.UtcNow;
            var before = account.RankedRestriction;
            var alreadyApplied = input.Clear && (before is null || before.ClearedAt is not null
                || before.RestrictionUntil <= now);
            if (!apply || alreadyApplied)
                return new(false, AccountRankedRestrictionViewLocked(account, now), alreadyApplied);
            if (input.Clear)
            {
                before!.ClearedAt = now;
                before.ClearedByAccountId = actor.Id;
            }
            else
            {
                account.RankedRestriction = new()
                {
                    SetAt = now, RestrictionUntil = now.AddDays(input.RestrictionDays!.Value),
                    SetByAccountId = actor.Id, Reason = reason,
                };
            }
            AddAdminAudit(actor, "account", input.Clear ? "ranked-ban-clear" : "ranked-ban-set",
                $"account:{account.Id}", before?.RestrictionUntil.ToString("O"),
                input.Clear ? "cleared" : account.RankedRestriction!.RestrictionUntil.ToString("O"),
                reason, audit with { Reason = reason, Outcome = "succeeded" });
            // The surrounding existing admin transaction owns commit/rollback.
            // No permission-version bump, session revocation, match, or correction.
            Save();
            return new(true, AccountRankedRestrictionViewLocked(account, now), false);
        }
    }

    private L12AccountRankedRestrictionView AccountRankedRestrictionViewLocked(AccountRow account,
        DateTimeOffset now)
    {
        var row = account.RankedRestriction;
        var manualUntil = row is { ClearedAt: null } && row.RestrictionUntil > now
            ? row.RestrictionUntil : (DateTimeOffset?)null;
        var integrityUntil = RankedIntegrityRestrictionUntilLocked(account.Id, now);
        var effective = manualUntil is null ? integrityUntil
            : integrityUntil is null || manualUntil >= integrityUntil ? manualUntil : integrityUntil;
        return new(account.Id, row?.RestrictionUntil, row?.Reason, row?.SetAt, row?.SetByAccountId,
            row?.ClearedAt, effective, _data.Version);
    }

    private DateTimeOffset? ManualRankedRestrictionUntilLocked(string accountId, DateTimeOffset now)
    {
        var row = _data.Accounts.FirstOrDefault(account => account.Id == accountId)?.RankedRestriction;
        return row is { ClearedAt: null } && row.RestrictionUntil > now ? row.RestrictionUntil : null;
    }

    private DateTimeOffset? RankedIntegrityRestrictionUntilLocked(string accountId, DateTimeOffset now)
        => _data.RankedIntegrityDecisions
            .Where(row => row.Disposition == "confirmed" && !IsDecisionRevokedLocked(row.Id)
                && !row.MatchIds.Any(IsT01TransitionWaivedMatchLocked))
            .SelectMany(row => row.AccountEffects)
            .Where(effect => effect.AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                && effect.RestrictionUntil > now)
            .OrderByDescending(effect => effect.RestrictionUntil)
            .Select(effect => effect.RestrictionUntil).FirstOrDefault();
}
