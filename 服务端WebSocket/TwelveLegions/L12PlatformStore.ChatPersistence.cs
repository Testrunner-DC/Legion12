using System.Text;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal sealed record L12ChatEntry(
    string Id,
    long Seq,
    string Scope,
    string? MatchId,
    string SenderId,
    string SenderName,
    string RecipientId,
    string Text,
    DateTimeOffset CreatedAt,
    bool Moderated,
    string PolicyVersion);

internal sealed record L12ChatHistoryPage(
    IReadOnlyList<L12ChatEntry> Entries,
    bool HasMore);

internal sealed record L12StoredChatWrite(L12ChatEntry? Entry, bool Duplicate,
    string MessageId, long Sequence);

internal sealed class L12ChatOperationException(string code, string message, bool retryable = false,
    Exception? inner = null) : Exception(message, inner)
{
    internal string Code { get; } = code;
    internal bool Retryable { get; } = retryable;
}

public sealed partial class L12PlatformStore
{
    private readonly HashSet<string> _pendingChatAccountDeletions = new(StringComparer.Ordinal);
    internal const int FriendChatRetentionHours = 72;
    internal const int FriendChatMaximumRetainedPerConversation = 200;
    internal const int FriendChatMaximumHistoryPage = 100;
    internal const long ChatMaximumSafeSequence = 9_007_199_254_740_991L;
    internal const int ChatRequestFutureSkewMinutes = 2;

    // These are conservative logical admission caps, not a capacity claim. SQLite pages, WAL,
    // indexes and backup growth remain part of deployment measurement.
    private const int FriendChatMaximumRetainedMessages = 100_000;
    private const long FriendChatMaximumLogicalRetainedBytes = 64L * 1024 * 1024;
    private const int FriendChatMaximumReceipts = 200_000;
    private const long FriendChatMaximumLogicalReceiptBytes = 64L * 1024 * 1024;
    private const int FriendChatMaximumConversations = 100_000;
    private const long FriendChatMaximumLogicalConversationBytes = 32L * 1024 * 1024;
    private const int ChatRetainedMetadataBytes = 512;
    private const int ChatBusyTimeoutMilliseconds = 100;

    internal L12StoredChatWrite AppendCommittedFriendChat(string senderId, string recipientId,
        string requestId, string requestFingerprint, L12ChatTextResult text, DateTimeOffset observedAt,
        DateTimeOffset requestIssuedAt, Func<bool> requestStillFresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestStillFresh);
        var committed = Volatile.Read(ref _committedSessionActivity);
        var admission = RequireCommittedFriendChatPair(committed, senderId, recipientId);
        if (!Volatile.Read(ref _storageWritable))
            throw new L12ChatOperationException("unavailable", "聊天存储当前不可用", retryable: true);
        if (requestIssuedAt > observedAt.AddMinutes(ChatRequestFutureSkewMinutes)
            || requestIssuedAt.AddHours(FriendChatRetentionHours) <= observedAt)
            throw new L12ChatOperationException("invalidRequest", "requestId 已过期或时间无效");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var deployment = EnterDeploymentMutation();
            using var connection = OpenChatDatabase(readOnly: false);
            using var transaction = connection.BeginTransaction(deferred: false);
            RequireChatStorageAnchor(connection, transaction, committed.Revision);
            DeleteExpiredChatBatch(connection, transaction, observedAt, 32);
            DeleteExpiredChatReceiptBatch(connection, transaction, observedAt, 64);
            DeleteEmptyChatMetadataBatch(connection, transaction, 32);

            var pair = ChatPair(senderId, recipientId);
            var existing = ReadStoredChatByRequest(connection, transaction, senderId, requestId);
            if (existing is not null)
            {
                if (!string.Equals(existing.Value.Fingerprint, requestFingerprint, StringComparison.Ordinal)
                    || !string.Equals(existing.Value.RecipientId, recipientId, StringComparison.Ordinal)
                    || !string.Equals(existing.Value.FirstId, pair.FirstId, StringComparison.Ordinal)
                    || !string.Equals(existing.Value.SecondId, pair.SecondId, StringComparison.Ordinal))
                    throw new L12ChatOperationException("requestConflict",
                        "相同 requestId 已用于不同的聊天请求");
                RequireChatCommitBoundary(committed, senderId, recipientId, requestStillFresh);
                transaction.Commit();
                return new(existing.Value.Entry, true, existing.Value.MessageId,
                    existing.Value.Sequence);
            }

            var createdAt = observedAt.ToUniversalTime();
            var expiresAt = createdAt.AddHours(FriendChatRetentionHours);
            var charge = ChatLogicalCharge(senderId, recipientId, admission.SenderName, requestId,
                requestFingerprint, text.Text, text.PolicyVersion);
            var usage = ReadChatUsage(connection, transaction);
            if (usage.Items >= FriendChatMaximumRetainedMessages
                || charge > FriendChatMaximumLogicalRetainedBytes - usage.Bytes)
                throw new L12ChatOperationException("quota", "聊天存储额度暂时不足", retryable: true);
            var receiptCharge = ChatLogicalCharge(senderId, recipientId, requestId,
                requestFingerprint);
            var receiptUsage = ReadChatReceiptUsage(connection, transaction);
            if (receiptUsage.Items >= FriendChatMaximumReceipts
                || receiptCharge > FriendChatMaximumLogicalReceiptBytes - receiptUsage.Bytes)
                throw new L12ChatOperationException("quota", "聊天请求回执额度暂时不足", retryable: true);

            EnsureChatConversation(connection, transaction, pair, createdAt);
            var sequence = ReadNextChatSequence(connection, transaction, pair);
            if (sequence is <= 0 or > ChatMaximumSafeSequence)
                throw new L12ChatOperationException("unavailable", "聊天会话序号不可用", retryable: true);
            AdvanceChatSequence(connection, transaction, pair, sequence, createdAt);

            var entry = new L12ChatEntry(Guid.NewGuid().ToString("N"), sequence, "friend", null,
                senderId, admission.SenderName, recipientId, text.Text, createdAt, text.Moderated,
                text.PolicyVersion);
            InsertFriendChat(connection, transaction, pair, entry, requestId, requestFingerprint,
                expiresAt, charge);
            InsertFriendChatReceipt(connection, transaction, pair, entry, requestId,
                requestFingerprint, requestIssuedAt, receiptCharge);
            TrimFriendConversation(connection, transaction, pair);
            StorageFailureInjector?.Invoke("before-chat-commit");
            cancellationToken.ThrowIfCancellationRequested();
            RequireChatCommitBoundary(committed, senderId, recipientId, requestStillFresh);
            transaction.Commit();
            return new(entry, false, entry.Id, entry.Seq);
        }
        catch (L12ChatOperationException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (L12DeploymentBarrierClosedException error)
        {
            throw new L12ChatOperationException("unavailable", "服务器正在切换版本，请稍后重试",
                retryable: true, error);
        }
        catch (Exception error) when (error is SqliteException or IOException or InvalidDataException)
        {
            var busy = error is SqliteException { SqliteErrorCode: 5 or 6 };
            throw new L12ChatOperationException(busy ? "busy" : "unavailable",
                busy ? "聊天存储正忙，请使用原 requestId 重试" : "聊天存储当前不可用",
                retryable: true, error);
        }
    }

    internal L12ChatHistoryPage ReadCommittedFriendChatHistory(string accountId, string peerAccountId,
        long? beforeSeq, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
    {
        var committed = Volatile.Read(ref _committedSessionActivity);
        _ = RequireCommittedFriendChatPair(committed, accountId, peerAccountId);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = OpenChatDatabase(readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            RequireChatStorageAnchor(connection, transaction, committed.Revision);
            var pair = ChatPair(accountId, peerAccountId);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id,seq,sender_id,sender_name,recipient_id,text,created_utc,moderated,policy_version
                FROM chat_messages
                WHERE first_id=$first AND second_id=$second AND expires_utc>$now
                  AND ($before IS NULL OR seq<$before)
                ORDER BY seq DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$first", pair.FirstId);
            command.Parameters.AddWithValue("$second", pair.SecondId);
            command.Parameters.AddWithValue("$now", observedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$before", beforeSeq is null ? DBNull.Value : beforeSeq.Value);
            command.Parameters.AddWithValue("$limit", FriendChatMaximumHistoryPage + 1);
            var rows = new List<L12ChatEntry>(FriendChatMaximumHistoryPage + 1);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read()) rows.Add(ReadFriendChatEntry(reader));
            }
            if (!ReferenceEquals(Volatile.Read(ref _committedSessionActivity), committed))
                throw new L12ChatOperationException("scopeUnavailable", "好友关系已变化");
            _ = RequireCommittedFriendChatPair(committed, accountId, peerAccountId);
            transaction.Commit();
            var hasMore = rows.Count > FriendChatMaximumHistoryPage;
            if (hasMore) rows.RemoveAt(rows.Count - 1);
            rows.Reverse();
            return new(rows, hasMore);
        }
        catch (L12ChatOperationException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is SqliteException or IOException or InvalidDataException)
        {
            var busy = error is SqliteException { SqliteErrorCode: 5 or 6 };
            throw new L12ChatOperationException(busy ? "busy" : "unavailable",
                busy ? "聊天存储正忙，请稍后重试" : "聊天历史当前不可用", retryable: true, error);
        }
    }

    internal long WriteCommittedFriendChatRead(string accountId, string peerAccountId, long requestedSeq,
        DateTimeOffset observedAt, Func<bool> requestStillFresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestStillFresh);
        var committed = Volatile.Read(ref _committedSessionActivity);
        _ = RequireCommittedFriendChatPair(committed, accountId, peerAccountId);
        if (!Volatile.Read(ref _storageWritable))
            throw new L12ChatOperationException("unavailable", "聊天存储当前不可用", retryable: true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var deployment = EnterDeploymentMutation();
            using var connection = OpenChatDatabase(readOnly: false);
            using var transaction = connection.BeginTransaction(deferred: false);
            RequireChatStorageAnchor(connection, transaction, committed.Revision);
            var pair = ChatPair(accountId, peerAccountId);
            using var maximum = connection.CreateCommand();
            maximum.Transaction = transaction;
            maximum.CommandText = """
                SELECT COALESCE(MAX(seq),0) FROM chat_messages
                WHERE first_id=$first AND second_id=$second AND expires_utc>$now;
                """;
            maximum.Parameters.AddWithValue("$first", pair.FirstId);
            maximum.Parameters.AddWithValue("$second", pair.SecondId);
            maximum.Parameters.AddWithValue("$now", observedAt.ToUniversalTime().ToString("O"));
            var accepted = Math.Min(requestedSeq, Convert.ToInt64(maximum.ExecuteScalar()));
            if (accepted > 0)
            {
                using var cursor = connection.CreateCommand();
                cursor.Transaction = transaction;
                cursor.CommandText = """
                    INSERT INTO chat_read_cursors(account_id,first_id,second_id,last_seq,updated_utc)
                    VALUES($account,$first,$second,$seq,$updated)
                    ON CONFLICT(account_id,first_id,second_id) DO UPDATE SET
                        last_seq=MAX(chat_read_cursors.last_seq,excluded.last_seq),
                        updated_utc=excluded.updated_utc;
                    """;
                cursor.Parameters.AddWithValue("$account", accountId);
                cursor.Parameters.AddWithValue("$first", pair.FirstId);
                cursor.Parameters.AddWithValue("$second", pair.SecondId);
                cursor.Parameters.AddWithValue("$seq", accepted);
                cursor.Parameters.AddWithValue("$updated", observedAt.ToUniversalTime().ToString("O"));
                cursor.ExecuteNonQuery();
            }
            RequireChatCommitBoundary(committed, accountId, peerAccountId, requestStillFresh);
            transaction.Commit();
            return accepted;
        }
        catch (L12ChatOperationException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (L12DeploymentBarrierClosedException error)
        {
            throw new L12ChatOperationException("unavailable", "服务器正在切换版本，请稍后重试",
                retryable: true, error);
        }
        catch (Exception error) when (error is SqliteException or IOException or InvalidDataException)
        {
            var busy = error is SqliteException { SqliteErrorCode: 5 or 6 };
            throw new L12ChatOperationException(busy ? "busy" : "unavailable",
                busy ? "聊天存储正忙，请稍后重试" : "聊天读取状态当前不可用", retryable: true, error);
        }
    }

    private L12CommittedChatPairView RequireCommittedFriendChatPair(CommittedSessionActivity committed,
        string senderId, string recipientId)
    {
        var pair = ReadCommittedChatPair(committed, senderId, recipientId);
        if (!pair.Available)
            throw new L12ChatOperationException("unavailable", "平台已提交关系当前不可用", retryable: true);
        if (!pair.AcceptedAndUnblocked)
            throw new L12ChatOperationException("scopeUnavailable", "当前不是可聊天的有效好友");
        return pair;
    }

    private void RequireChatCommitBoundary(CommittedSessionActivity committed, string senderId,
        string recipientId, Func<bool> requestStillFresh)
    {
        if (!ReferenceEquals(Volatile.Read(ref _committedSessionActivity), committed))
            throw new L12ChatOperationException("busy", "好友关系代次已变化，请使用原 requestId 重试",
                retryable: true);
        _ = RequireCommittedFriendChatPair(committed, senderId, recipientId);
        bool fresh;
        try { fresh = requestStillFresh(); }
        catch { fresh = false; }
        if (!fresh) throw new L12ChatOperationException("staleSession", "原发送连接已失效");
    }

    private SqliteConnection OpenChatDatabase(bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = 1,
        }.ToString());
        try
        {
            connection.Open();
            AttachSyntheticStorageMeasurement(connection);
            using var command = connection.CreateCommand();
            command.CommandText = readOnly
                ? $"PRAGMA busy_timeout={ChatBusyTimeoutMilliseconds}; PRAGMA query_only=ON;"
                : $"PRAGMA busy_timeout={ChatBusyTimeoutMilliseconds}; PRAGMA synchronous=FULL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void RequireChatStorageAnchor(SqliteConnection connection, SqliteTransaction transaction,
        long expectedRevision)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT schema_version,storage_revision FROM platform_state WHERE singleton_id=1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != PlatformStorageSchemaVersion)
            throw new InvalidDataException("聊天存储需要当前 platform_state 锚点");
        var actual = reader.GetInt64(1);
        if (actual != expectedRevision) throw new L12PlatformStorageConflictException(expectedRevision, actual);
    }

    private static ((string FirstId, string SecondId) Pair, long Items, long Bytes) ReadChatUsageWithPair(
        SqliteConnection connection, SqliteTransaction transaction, string firstId, string secondId)
    {
        var pair = ChatPair(firstId, secondId);
        var usage = ReadChatUsage(connection, transaction);
        return (pair, usage.Items, usage.Bytes);
    }

    private static (long Items, long Bytes) ReadChatUsage(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT retained_items,retained_bytes FROM chat_storage_usage WHERE singleton_id=1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("聊天存储用量锚点缺失");
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static (long Items, long Bytes) ReadChatReceiptUsage(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT retained_items,retained_bytes FROM chat_receipt_usage WHERE singleton_id=1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("聊天请求回执用量锚点缺失");
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static void EnsureChatConversation(SqliteConnection connection, SqliteTransaction transaction,
        (string FirstId, string SecondId) pair, DateTimeOffset now)
    {
        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = """
                SELECT 1 FROM chat_conversations WHERE first_id=$first AND second_id=$second;
                """;
            existing.Parameters.AddWithValue("$first", pair.FirstId);
            existing.Parameters.AddWithValue("$second", pair.SecondId);
            if (existing.ExecuteScalar() is not null) return;
        }
        var usage = ReadChatConversationUsage(connection, transaction);
        var charge = 256L + 2L * (pair.FirstId.Length + pair.SecondId.Length);
        if (usage.Items >= FriendChatMaximumConversations
            || charge > FriendChatMaximumLogicalConversationBytes - usage.Bytes)
            throw new L12ChatOperationException("quota", "聊天会话元数据额度暂时不足", retryable: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO chat_conversations(first_id,second_id,next_seq,updated_utc,retained_bytes)
            VALUES($first,$second,1,$updated,$bytes)
            ON CONFLICT(first_id,second_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$first", pair.FirstId);
        command.Parameters.AddWithValue("$second", pair.SecondId);
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$bytes", charge);
        command.ExecuteNonQuery();
    }

    private static (long Items, long Bytes) ReadChatConversationUsage(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT retained_items,retained_bytes FROM chat_conversation_usage WHERE singleton_id=1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("聊天会话用量锚点缺失");
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static long ReadNextChatSequence(SqliteConnection connection, SqliteTransaction transaction,
        (string FirstId, string SecondId) pair)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT next_seq FROM chat_conversations WHERE first_id=$first AND second_id=$second;
            """;
        command.Parameters.AddWithValue("$first", pair.FirstId);
        command.Parameters.AddWithValue("$second", pair.SecondId);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void AdvanceChatSequence(SqliteConnection connection, SqliteTransaction transaction,
        (string FirstId, string SecondId) pair, long current, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE chat_conversations SET next_seq=$next,updated_utc=$updated
            WHERE first_id=$first AND second_id=$second AND next_seq=$current;
            """;
        command.Parameters.AddWithValue("$next", checked(current + 1));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$first", pair.FirstId);
        command.Parameters.AddWithValue("$second", pair.SecondId);
        command.Parameters.AddWithValue("$current", current);
        if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("聊天会话序号竞争");
    }

    private static void InsertFriendChat(SqliteConnection connection, SqliteTransaction transaction,
        (string FirstId, string SecondId) pair, L12ChatEntry entry, string requestId,
        string fingerprint, DateTimeOffset expiresAt, long charge)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO chat_messages(id,first_id,second_id,seq,sender_id,sender_name,recipient_id,text,
                created_utc,expires_utc,moderated,policy_version,request_id,request_fingerprint,retained_bytes)
            VALUES($id,$first,$second,$seq,$sender,$name,$recipient,$text,$created,$expires,$moderated,
                $policy,$request,$fingerprint,$bytes);
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$first", pair.FirstId);
        command.Parameters.AddWithValue("$second", pair.SecondId);
        command.Parameters.AddWithValue("$seq", entry.Seq);
        command.Parameters.AddWithValue("$sender", entry.SenderId);
        command.Parameters.AddWithValue("$name", entry.SenderName);
        command.Parameters.AddWithValue("$recipient", entry.RecipientId);
        command.Parameters.AddWithValue("$text", entry.Text);
        command.Parameters.AddWithValue("$created", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$expires", expiresAt.ToString("O"));
        command.Parameters.AddWithValue("$moderated", entry.Moderated ? 1 : 0);
        command.Parameters.AddWithValue("$policy", entry.PolicyVersion);
        command.Parameters.AddWithValue("$request", requestId);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$bytes", charge);
        if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("聊天正文未写入唯一行");
    }

    private static void InsertFriendChatReceipt(SqliteConnection connection,
        SqliteTransaction transaction, (string FirstId, string SecondId) pair, L12ChatEntry entry,
        string requestId, string fingerprint, DateTimeOffset issuedAt, long charge)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO chat_request_receipts(sender_id,request_id,first_id,second_id,recipient_id,
                request_fingerprint,entry_id,seq,issued_utc,expires_utc,retained_bytes)
            VALUES($sender,$request,$first,$second,$recipient,$fingerprint,$entry,$seq,$issued,$expires,$bytes);
            """;
        command.Parameters.AddWithValue("$sender", entry.SenderId);
        command.Parameters.AddWithValue("$request", requestId);
        command.Parameters.AddWithValue("$first", pair.FirstId);
        command.Parameters.AddWithValue("$second", pair.SecondId);
        command.Parameters.AddWithValue("$recipient", entry.RecipientId);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$entry", entry.Id);
        command.Parameters.AddWithValue("$seq", entry.Seq);
        command.Parameters.AddWithValue("$issued", issuedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$expires", issuedAt.ToUniversalTime()
            .AddHours(FriendChatRetentionHours).ToString("O"));
        command.Parameters.AddWithValue("$bytes", charge);
        if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("聊天请求回执未写入唯一行");
    }

    private static (L12ChatEntry? Entry, string Fingerprint, string FirstId, string SecondId,
        string RecipientId, string MessageId, long Sequence)?
        ReadStoredChatByRequest(SqliteConnection connection, SqliteTransaction transaction,
            string senderId, string requestId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.request_fingerprint,r.first_id,r.second_id,r.recipient_id,r.entry_id,r.seq,
                   m.id,m.seq,m.sender_id,m.sender_name,m.recipient_id,m.text,m.created_utc,
                   m.moderated,m.policy_version
            FROM chat_request_receipts r
            LEFT JOIN chat_messages m ON m.id=r.entry_id
            WHERE r.sender_id=$sender AND r.request_id=$request;
            """;
        command.Parameters.AddWithValue("$sender", senderId);
        command.Parameters.AddWithValue("$request", requestId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var entry = reader.IsDBNull(6) ? null : ReadFriendChatEntry(reader, 6);
        return (entry, reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetInt64(5));
    }

    private static L12ChatEntry ReadFriendChatEntry(SqliteDataReader reader)
        => ReadFriendChatEntry(reader, 0);

    private static L12ChatEntry ReadFriendChatEntry(SqliteDataReader reader, int offset)
        => new(reader.GetString(offset), reader.GetInt64(offset + 1), "friend", null,
            reader.GetString(offset + 2), reader.GetString(offset + 3), reader.GetString(offset + 4),
            reader.GetString(offset + 5), DateTimeOffset.Parse(reader.GetString(offset + 6), null,
                System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(offset + 7) != 0, reader.GetString(offset + 8));

    private static void DeleteExpiredChatBatch(SqliteConnection connection, SqliteTransaction transaction,
        DateTimeOffset now, int maximum)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM chat_messages WHERE id IN (
                SELECT id FROM chat_messages WHERE expires_utc<=$now ORDER BY expires_utc,id LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$limit", maximum);
        command.ExecuteNonQuery();
    }

    private static void DeleteExpiredChatReceiptBatch(SqliteConnection connection,
        SqliteTransaction transaction, DateTimeOffset now, int maximum)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM chat_request_receipts WHERE rowid IN (
                SELECT rowid FROM chat_request_receipts
                WHERE expires_utc<=$now ORDER BY expires_utc,rowid LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$now", now.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$limit", maximum);
        command.ExecuteNonQuery();
    }

    private static void DeleteEmptyChatMetadataBatch(SqliteConnection connection,
        SqliteTransaction transaction, int maximum)
    {
        using var cursors = connection.CreateCommand();
        cursors.Transaction = transaction;
        cursors.CommandText = """
            DELETE FROM chat_read_cursors WHERE rowid IN (
                SELECT c.rowid FROM chat_read_cursors c
                WHERE NOT EXISTS (SELECT 1 FROM chat_messages m
                    WHERE m.first_id=c.first_id AND m.second_id=c.second_id)
                  AND NOT EXISTS (SELECT 1 FROM chat_request_receipts r
                    WHERE r.first_id=c.first_id AND r.second_id=c.second_id)
                LIMIT $limit
            );
            """;
        cursors.Parameters.AddWithValue("$limit", maximum);
        cursors.ExecuteNonQuery();
    }

    private static void SynchronizeChatAccountDeletions(SqliteConnection connection,
        SqliteTransaction transaction, DataFile data, IReadOnlyList<string> accountIds)
    {
        foreach (var accountId in accountIds)
        {
            if (!data.Accounts.Any(account => account.Deleted
                    && string.Equals(account.Id, accountId, StringComparison.Ordinal))) continue;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM chat_messages WHERE first_id=$id OR second_id=$id;
                DELETE FROM chat_request_receipts
                    WHERE sender_id=$id OR recipient_id=$id OR first_id=$id OR second_id=$id;
                DELETE FROM chat_read_cursors
                    WHERE account_id=$id OR first_id=$id OR second_id=$id;
                DELETE FROM chat_conversations WHERE first_id=$id OR second_id=$id;
                """;
            command.Parameters.AddWithValue("$id", accountId);
            command.ExecuteNonQuery();
        }
    }

    private static void TrimFriendConversation(SqliteConnection connection, SqliteTransaction transaction,
        (string FirstId, string SecondId) pair)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM chat_messages
            WHERE first_id=$first AND second_id=$second AND seq NOT IN (
                SELECT seq FROM chat_messages WHERE first_id=$first AND second_id=$second
                ORDER BY seq DESC LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$first", pair.FirstId);
        command.Parameters.AddWithValue("$second", pair.SecondId);
        command.Parameters.AddWithValue("$limit", FriendChatMaximumRetainedPerConversation);
        command.ExecuteNonQuery();
    }

    private static long ChatLogicalCharge(params string[] values)
        => ChatRetainedMetadataBytes + values.Sum(value => (long)Encoding.UTF8.GetByteCount(value));

    private static void InitializeChatStorageSchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_conversations (
                first_id TEXT NOT NULL,
                second_id TEXT NOT NULL,
                next_seq INTEGER NOT NULL CHECK(next_seq>0),
                updated_utc TEXT NOT NULL,
                retained_bytes INTEGER NOT NULL CHECK(retained_bytes>0),
                PRIMARY KEY(first_id,second_id),
                CHECK(first_id<second_id)
            );
            CREATE TABLE IF NOT EXISTS chat_schema_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chat_messages (
                id TEXT PRIMARY KEY,
                first_id TEXT NOT NULL,
                second_id TEXT NOT NULL,
                seq INTEGER NOT NULL CHECK(seq>0),
                sender_id TEXT NOT NULL,
                sender_name TEXT NOT NULL,
                recipient_id TEXT NOT NULL,
                text TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                expires_utc TEXT NOT NULL,
                moderated INTEGER NOT NULL CHECK(moderated IN (0,1)),
                policy_version TEXT NOT NULL,
                request_id TEXT NOT NULL,
                request_fingerprint TEXT NOT NULL,
                retained_bytes INTEGER NOT NULL CHECK(retained_bytes>0),
                UNIQUE(first_id,second_id,seq),
                UNIQUE(sender_id,request_id),
                CHECK(first_id<second_id),
                CHECK(sender_id<>recipient_id)
            );
            CREATE INDEX IF NOT EXISTS ix_chat_messages_conversation
                ON chat_messages(first_id,second_id,seq DESC);
            CREATE INDEX IF NOT EXISTS ix_chat_messages_reverse_account
                ON chat_messages(second_id,first_id);
            CREATE INDEX IF NOT EXISTS ix_chat_messages_expiry
                ON chat_messages(expires_utc,id);
            CREATE TABLE IF NOT EXISTS chat_request_receipts (
                sender_id TEXT NOT NULL,
                request_id TEXT NOT NULL,
                first_id TEXT NOT NULL,
                second_id TEXT NOT NULL,
                recipient_id TEXT NOT NULL,
                request_fingerprint TEXT NOT NULL,
                entry_id TEXT NOT NULL,
                seq INTEGER NOT NULL CHECK(seq>0),
                issued_utc TEXT NOT NULL,
                expires_utc TEXT NOT NULL,
                retained_bytes INTEGER NOT NULL CHECK(retained_bytes>0),
                PRIMARY KEY(sender_id,request_id),
                CHECK(first_id<second_id),
                CHECK(sender_id<>recipient_id)
            );
            CREATE INDEX IF NOT EXISTS ix_chat_request_receipts_expiry
                ON chat_request_receipts(expires_utc,sender_id,request_id);
            CREATE INDEX IF NOT EXISTS ix_chat_request_receipts_conversation
                ON chat_request_receipts(first_id,second_id);
            CREATE INDEX IF NOT EXISTS ix_chat_request_receipts_reverse_account
                ON chat_request_receipts(second_id,first_id);
            CREATE INDEX IF NOT EXISTS ix_chat_request_receipts_recipient
                ON chat_request_receipts(recipient_id,sender_id);
            CREATE TABLE IF NOT EXISTS chat_read_cursors (
                account_id TEXT NOT NULL,
                first_id TEXT NOT NULL,
                second_id TEXT NOT NULL,
                last_seq INTEGER NOT NULL CHECK(last_seq>=0),
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(account_id,first_id,second_id),
                CHECK(first_id<second_id)
            );
            CREATE TABLE IF NOT EXISTS chat_storage_usage (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                retained_items INTEGER NOT NULL CHECK(retained_items>=0),
                retained_bytes INTEGER NOT NULL CHECK(retained_bytes>=0)
            );
            CREATE INDEX IF NOT EXISTS ix_chat_read_cursors_conversation
                ON chat_read_cursors(first_id,second_id);
            CREATE INDEX IF NOT EXISTS ix_chat_read_cursors_reverse_account
                ON chat_read_cursors(second_id,first_id);
            CREATE INDEX IF NOT EXISTS ix_chat_conversations_reverse_account
                ON chat_conversations(second_id,first_id);
            INSERT OR IGNORE INTO chat_storage_usage(singleton_id,retained_items,retained_bytes)
                VALUES(1,0,0);
            CREATE TABLE IF NOT EXISTS chat_conversation_usage (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                retained_items INTEGER NOT NULL CHECK(retained_items>=0),
                retained_bytes INTEGER NOT NULL CHECK(retained_bytes>=0)
            );
            INSERT OR IGNORE INTO chat_conversation_usage(singleton_id,retained_items,retained_bytes)
                VALUES(1,0,0);
            CREATE TABLE IF NOT EXISTS chat_receipt_usage (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                retained_items INTEGER NOT NULL CHECK(retained_items>=0),
                retained_bytes INTEGER NOT NULL CHECK(retained_bytes>=0)
            );
            INSERT OR IGNORE INTO chat_receipt_usage(singleton_id,retained_items,retained_bytes)
                VALUES(1,0,0);
            CREATE TRIGGER IF NOT EXISTS chat_messages_usage_insert
            AFTER INSERT ON chat_messages BEGIN
                UPDATE chat_storage_usage
                SET retained_items=retained_items+1,
                    retained_bytes=retained_bytes+NEW.retained_bytes
                WHERE singleton_id=1;
            END;
            CREATE TRIGGER IF NOT EXISTS chat_messages_usage_delete
            AFTER DELETE ON chat_messages BEGIN
                UPDATE chat_storage_usage
                SET retained_items=retained_items-1,
                    retained_bytes=retained_bytes-OLD.retained_bytes
                WHERE singleton_id=1;
            END;
            CREATE TRIGGER IF NOT EXISTS chat_receipts_usage_insert
            AFTER INSERT ON chat_request_receipts BEGIN
                UPDATE chat_receipt_usage
                SET retained_items=retained_items+1,
                    retained_bytes=retained_bytes+NEW.retained_bytes
                WHERE singleton_id=1;
            END;
            CREATE TRIGGER IF NOT EXISTS chat_receipts_usage_delete
            AFTER DELETE ON chat_request_receipts BEGIN
                UPDATE chat_receipt_usage
                SET retained_items=retained_items-1,
                    retained_bytes=retained_bytes-OLD.retained_bytes
                WHERE singleton_id=1;
            END;
            CREATE TRIGGER IF NOT EXISTS chat_conversations_usage_insert
            AFTER INSERT ON chat_conversations BEGIN
                UPDATE chat_conversation_usage
                SET retained_items=retained_items+1,
                    retained_bytes=retained_bytes+NEW.retained_bytes
                WHERE singleton_id=1;
            END;
            CREATE TRIGGER IF NOT EXISTS chat_conversations_usage_delete
            AFTER DELETE ON chat_conversations BEGIN
                UPDATE chat_conversation_usage
                SET retained_items=retained_items-1,
                    retained_bytes=retained_bytes-OLD.retained_bytes
                WHERE singleton_id=1;
            END;
            """;
        command.ExecuteNonQuery();
        using var anchor = connection.CreateCommand();
        anchor.Transaction = transaction;
        anchor.CommandText = "INSERT OR IGNORE INTO chat_schema_meta(key,value) VALUES('usage-v1','initialized');";
        if (anchor.ExecuteNonQuery() != 1)
        {
            transaction.Commit();
            return;
        }
        using var initializeUsage = connection.CreateCommand();
        initializeUsage.Transaction = transaction;
        initializeUsage.CommandText = """
            UPDATE chat_storage_usage SET
                retained_items=(SELECT COUNT(*) FROM chat_messages),
                retained_bytes=(SELECT COALESCE(SUM(retained_bytes),0) FROM chat_messages)
            WHERE singleton_id=1;
            UPDATE chat_receipt_usage SET
                retained_items=(SELECT COUNT(*) FROM chat_request_receipts),
                retained_bytes=(SELECT COALESCE(SUM(retained_bytes),0) FROM chat_request_receipts)
            WHERE singleton_id=1;
            UPDATE chat_conversation_usage SET
                retained_items=(SELECT COUNT(*) FROM chat_conversations),
                retained_bytes=(SELECT COALESCE(SUM(retained_bytes),0) FROM chat_conversations)
            WHERE singleton_id=1;
            """;
        initializeUsage.ExecuteNonQuery();
        transaction.Commit();
    }
}
