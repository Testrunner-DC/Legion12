using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // A Save-local proof, never a cross-transaction cache. Merge still validates
    // both complete domains: staged identities (including outside retention)
    // and the latest window, with overlapping bodies read and validated once.
    // SQLite's write transaction excludes other writers until append/commit.
    // Closed writer boundary: between Merge and Append, object/season callbacks
    // may add staged audit rows but must not UPDATE/DELETE this ledger directly.
    // A future direct-ledger writer must use the ordinary validating Append path.
    private sealed class ValidatedAuditAppend
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;
        private readonly Dictionary<string, AuditContentIdentity> _verified;

        internal ValidatedAuditAppend(SqliteConnection connection, SqliteTransaction transaction,
            IEnumerable<AdminAuditRow> retained, IReadOnlySet<string> present)
        {
            _connection = connection;
            _transaction = transaction;
            _verified = retained.Where(row => present.Contains(row.Id)).ToDictionary(
                row => row.Id, AuditContentIdentity.Capture, StringComparer.Ordinal);
        }

        internal void RequireScope(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (!ReferenceEquals(connection, _connection) || !ReferenceEquals(transaction, _transaction)
                || !ReferenceEquals(transaction.Connection, connection))
                throw new InvalidOperationException("审计验证结果不可跨连接或事务复用");
        }

        internal bool ContainsUnchanged(AdminAuditRow row)
        {
            if (!_verified.TryGetValue(row.Id, out var verified)) return false;
            // Rows are mutable staging objects. Reference equality is not proof;
            // compare every persisted value to its immutable validated identity.
            if (verified != AuditContentIdentity.Capture(row))
                throw new InvalidDataException("独立审计事件同一标识存在冲突正文");
            return true;
        }
    }

    private readonly record struct AuditContentIdentity(
        string Id, string ActorId, string ActorName, string Category, string Action,
        string Target, string? FromValue, string? ToValue, string? Comment,
        long CreatedAtTicks, long CreatedAtOffsetTicks, string? CorrelationId,
        string Outcome, string? Permission, string? Reason, string? CommandId,
        string? IdempotencyKey, bool DryRun, long? ExpectedVersion,
        string? RequestMethod, string? RequestPath)
    {
        internal static AuditContentIdentity Capture(AdminAuditRow row) => new(
            row.Id, row.ActorId, row.ActorName, row.Category, row.Action,
            row.Target, row.FromValue, row.ToValue, row.Comment,
            row.CreatedAt.Ticks, row.CreatedAt.Offset.Ticks, row.CorrelationId,
            row.Outcome, row.Permission, row.Reason, row.CommandId,
            row.IdempotencyKey, row.DryRun, row.ExpectedVersion,
            row.RequestMethod, row.RequestPath);
    }
}
