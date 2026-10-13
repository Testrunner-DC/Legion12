using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // The existing audit ledger is authoritative. A pure read must not rebuild
    // the business snapshot, mirror, rollback cache, or committed read generation.
    private void AppendCommittedReadAudit(L12AccountView actor, L12Permission permission,
        string category, string action, string target, L12AdminAuditContext context,
        CancellationToken cancellationToken)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        var current = RequireCommittedAuditActor(committed, actor, permission);
        var row = new AdminAuditRow
        {
            ActorId = current.Id, ActorName = current.Username, Category = category,
            Action = action, Target = target, Comment = "read",
            CorrelationId = context.CorrelationId, Outcome = "succeeded",
            Permission = L12Authorization.Key(permission), Reason = "read",
            CommandId = context.CommandId, IdempotencyKey = context.IdempotencyKey,
            DryRun = context.DryRun, ExpectedVersion = context.ExpectedVersion,
            RequestMethod = context.RequestMethod, RequestPath = context.RequestPath,
        };
        AppendCommittedReadAuditRow(committed, actor, permission, row, cancellationToken);
    }

    private L12AccountView RequireCommittedAuditActor(CommittedSessionActivity committed,
        L12AccountView actor, L12Permission permission)
    {
        if (!committed.Available || !Volatile.Read(ref _storageWritable))
            throw new L12PlatformStorageUnavailableException("审计事务存储当前不可用");
        if (!committed.ActiveAccounts.TryGetValue(actor.Id, out var current)
            || current.PermissionVersion != actor.PermissionVersion || current.Role != actor.Role
            || !L12Authorization.HasPermission(current, permission))
            throw new UnauthorizedAccessException("管理读取权限已变化，请重新登录");
        return current;
    }

    private void AppendCommittedReadAuditRow(CommittedSessionActivity committed, L12AccountView actor,
        L12Permission permission, AdminAuditRow row, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(row, PlatformSnapshotJsonOptions);
        var checksum = Sha256(payload);
        cancellationToken.ThrowIfCancellationRequested();
        using var deployment = EnterDeploymentMutation();
        try
        {
            using var connection = OpenReadAuditDatabase();
            using var transaction = connection.BeginTransaction(deferred: false);
            var revision = ReadStorageRevision(connection, transaction);
            if (revision != committed.Revision)
                throw new L12PlatformStorageConflictException(committed.Revision, revision);
            using (var schema = connection.CreateCommand())
            {
                schema.Transaction = transaction;
                schema.CommandText = "SELECT schema_version FROM platform_state WHERE singleton_id=1;";
                if (Convert.ToInt32(schema.ExecuteScalar()) != PlatformStorageSchemaVersion)
                    throw new InvalidDataException("只读审计需要当前存储结构");
            }
            cancellationToken.ThrowIfCancellationRequested();
            StorageFailureInjector?.Invoke("before-read-audit-append");
            using (var command = CreateAuditInsert(connection, transaction, row, payload, checksum, ignore: false))
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidDataException("审计追加未写入唯一事件");
            StorageFailureInjector?.Invoke("before-read-audit-commit");
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _committedSessionActivity), committed))
                throw new L12PlatformStorageUnavailableException("已提交权限代次已变化，请重试");
            RequireCommittedAuditActor(committed, actor, permission);
            transaction.Commit();
            // No allocation, publication, callbacks or fallible work after commit.
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (L12PlatformStorageUnavailableException) { throw; }
        catch (Exception error) when (error is SqliteException or IOException or InvalidDataException or JsonException)
        {
            // This independent transaction cannot invalidate the business generation.
            throw new L12PlatformStorageUnavailableException("读取审计暂时无法保存，请稍后重试", error);
        }
    }

    private SqliteConnection OpenReadAuditDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, Cache = SqliteCacheMode.Private, ForeignKeys = true, DefaultTimeout = 1,
        }.ToString());
        try
        {
            connection.Open();
            AttachSyntheticStorageMeasurement(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=1000; PRAGMA synchronous=FULL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static SqliteCommand CreateAuditInsert(SqliteConnection connection, SqliteTransaction transaction,
        AdminAuditRow row, string payload, string checksum, bool ignore)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT {(ignore ? "OR IGNORE " : "")}INTO admin_audit_events(
                id,created_utc,category,outcome,actor_id,command_id,correlation_id,payload_json,payload_sha256)
            VALUES($id,$created,$category,$outcome,$actor,$command,$correlation,$payload,$checksum);
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$created", row.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$category", row.Category);
        command.Parameters.AddWithValue("$outcome", row.Outcome);
        command.Parameters.AddWithValue("$actor", row.ActorId);
        command.Parameters.AddWithValue("$command", (object?)row.CommandId ?? DBNull.Value);
        command.Parameters.AddWithValue("$correlation", (object?)row.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$checksum", checksum);
        return command;
    }

    private static AdminAuditRow ReadValidatedAudit(SqliteDataReader reader, AdminAuditRow? cached = null)
    {
        var payload = reader.GetString(7);
        if (!FixedEquals(reader.GetString(8), Sha256(payload)))
            throw new InvalidDataException("独立审计事件校验和不匹配");
        var row = cached is not null && JsonSerializer.Serialize(cached, PlatformSnapshotJsonOptions) == payload
            ? cached : JsonSerializer.Deserialize<AdminAuditRow>(payload, PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("独立审计事件无法反序列化");
        if (row.Id != reader.GetString(0) || row.CreatedAt.ToString("O") != reader.GetString(1)
            || row.Category != reader.GetString(2) || row.Outcome != reader.GetString(3)
            || row.ActorId != reader.GetString(4)
            || row.CommandId != (reader.IsDBNull(5) ? null : reader.GetString(5))
            || row.CorrelationId != (reader.IsDBNull(6) ? null : reader.GetString(6)))
            throw new InvalidDataException("独立审计事件元数据与正文不一致");
        return row;
    }
}
