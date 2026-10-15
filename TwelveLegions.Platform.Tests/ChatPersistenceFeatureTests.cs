using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ChatPersistenceFeatureTests
{
    [Fact]
    public void FriendTransactionRollbackTrimReceiptPaginationReadAndPermanentSequenceAreClosed()
    {
        using var fixture = new RankedIdentityFixture();
        var (sender, recipient) = AcceptedPair(fixture.Store);
        var started = Millisecond(DateTimeOffset.UtcNow);
        const string source = "first";
        var text = Apply(source);
        var firstRequest = RequestId(started);
        var firstFingerprint = L12ChatTextPolicy.Fingerprint("friend", recipient.Id, null, source);

        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-chat-commit") throw new IOException("controlled chat rollback");
        };
        var failed = Assert.Throws<L12ChatOperationException>(() => fixture.Store.AppendCommittedFriendChat(
            sender.Id, recipient.Id, firstRequest, firstFingerprint, text, started, started, () => true));
        Assert.Equal("unavailable", failed.Code);
        fixture.Store.StorageFailureInjector = null;

        var first = fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id, firstRequest,
            firstFingerprint, text, started, started, () => true);
        Assert.False(first.Duplicate);
        Assert.Equal(1, first.Sequence);

        for (var index = 0; index < L12PlatformStore.FriendChatMaximumRetainedPerConversation; index++)
        {
            var issued = started.AddMilliseconds(index + 1);
            var body = $"message-{index:D3}";
            fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id, RequestId(issued),
                L12ChatTextPolicy.Fingerprint("friend", recipient.Id, null, body), Apply(body),
                issued, issued, () => true);
        }

        var duplicateAfterBodyTrim = fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id,
            firstRequest, firstFingerprint, text, started.AddMinutes(1), started, () => true);
        Assert.True(duplicateAfterBodyTrim.Duplicate);
        Assert.Null(duplicateAfterBodyTrim.Entry);
        Assert.Equal(first.MessageId, duplicateAfterBodyTrim.MessageId);
        Assert.Equal(1, duplicateAfterBodyTrim.Sequence);

        var newest = fixture.Store.ReadCommittedFriendChatHistory(sender.Id, recipient.Id, null,
            started.AddMinutes(1));
        Assert.True(newest.HasMore);
        Assert.Equal(L12PlatformStore.FriendChatMaximumHistoryPage, newest.Entries.Count);
        Assert.Equal(102, newest.Entries[0].Seq);
        Assert.Equal(201, newest.Entries[^1].Seq);
        var older = fixture.Store.ReadCommittedFriendChatHistory(sender.Id, recipient.Id,
            newest.Entries[0].Seq, started.AddMinutes(1));
        Assert.False(older.HasMore);
        Assert.Equal(L12PlatformStore.FriendChatMaximumHistoryPage, older.Entries.Count);
        Assert.Equal(2, older.Entries[0].Seq);
        Assert.Equal(101, older.Entries[^1].Seq);

        var acceptedRead = fixture.Store.WriteCommittedFriendChatRead(sender.Id, recipient.Id,
            L12PlatformStore.ChatMaximumSafeSequence, started.AddMinutes(1), () => true);
        Assert.Equal(201, acceptedRead);

        var beforeExpiredRetry = Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT COUNT(*) FROM chat_messages;");
        var expired = Assert.Throws<L12ChatOperationException>(() => fixture.Store.AppendCommittedFriendChat(
            sender.Id, recipient.Id, firstRequest, firstFingerprint, text, started.AddHours(73),
            started, () => true));
        Assert.Equal("invalidRequest", expired.Code);
        Assert.Equal(beforeExpiredRetry, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT COUNT(*) FROM chat_messages;"));

        Execute(fixture.Store.TransactionalStoragePath, """
            DELETE FROM chat_messages;
            DELETE FROM chat_request_receipts;
            DELETE FROM chat_read_cursors;
            """);
        var later = started.AddHours(73);
        const string laterBody = "later";
        var resumed = fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id, RequestId(later),
            L12ChatTextPolicy.Fingerprint("friend", recipient.Id, null, laterBody), Apply(laterBody),
            later, later, () => true);
        Assert.Equal(202, resumed.Sequence);
        Assert.Equal(203, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT next_seq FROM chat_conversations;"));
        Assert.Equal(1, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_conversation_usage WHERE singleton_id=1;"));
    }

    [Fact]
    public void UsageAnchorRestartBackfillsOnceAndOrdinarySaveNeverRecountsChatTables()
    {
        using var fixture = new RankedIdentityFixture();
        var (sender, recipient) = AcceptedPair(fixture.Store);
        var now = Millisecond(DateTimeOffset.UtcNow);
        const string source = "anchor";
        fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id, RequestId(now),
            L12ChatTextPolicy.Fingerprint("friend", recipient.Id, null, source), Apply(source),
            now, now, () => true);

        // Model a process loss before the one-time anchor transaction commits: the database is
        // left in the pre-anchor state, so the next startup must perform the bounded migration once.
        Execute(fixture.Store.TransactionalStoragePath, """
            DELETE FROM chat_schema_meta WHERE key='usage-v1';
            UPDATE chat_storage_usage SET retained_items=0,retained_bytes=0 WHERE singleton_id=1;
            UPDATE chat_receipt_usage SET retained_items=0,retained_bytes=0 WHERE singleton_id=1;
            UPDATE chat_conversation_usage SET retained_items=0,retained_bytes=0 WHERE singleton_id=1;
            """);
        _ = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.Equal(1, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_storage_usage WHERE singleton_id=1;"));
        Assert.Equal(1, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_receipt_usage WHERE singleton_id=1;"));
        Assert.Equal(1, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_conversation_usage WHERE singleton_id=1;"));

        // A deliberately distinctive anchored value proves normal platform Save does not execute
        // COUNT/SUM over chat tables. Triggers remain the sole hot-path accounting mechanism.
        Execute(fixture.Store.TransactionalStoragePath,
            "UPDATE chat_storage_usage SET retained_items=777 WHERE singleton_id=1;");
        var restarted = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        using (var measurement = L12PlatformStore.BeginSyntheticStorageMeasurement())
        {
            Assert.True(restarted.Register($"as{Guid.NewGuid():N}"[..10], "Password123!").Success);
            var observed = measurement.Complete();
            Assert.Equal(1, observed.Connections);
            Assert.Equal(0, observed.InstrumentationErrors);
        }
        Assert.Equal(777, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_storage_usage WHERE singleton_id=1;"));
    }

    [Fact]
    public void AdditiveChatTablesSurviveUnrelatedReaderSaveAndWholeDatabaseBackupRestore()
    {
        using var fixture = new RankedIdentityFixture();
        var (sender, recipient) = AcceptedPair(fixture.Store);
        var now = Millisecond(DateTimeOffset.UtcNow);
        const string source = "backup-body";
        fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id, RequestId(now),
            L12ChatTextPolicy.Fingerprint("friend", recipient.Id, null, source), Apply(source),
            now, now, () => true);

        // The core snapshot reader/writer knows nothing about chat rows and must preserve unknown
        // additive schema during a normal schema-9 Save.
        Assert.True(fixture.Store.Register($"or{Guid.NewGuid():N}"[..10], "Password123!").Success);
        Assert.Equal(source, Assert.Single(fixture.Store.ReadCommittedFriendChatHistory(sender.Id,
            recipient.Id, null, now.AddMinutes(1)).Entries).Text);

        var restoreRoot = Path.Combine(Path.GetTempPath(), $"l12-chat-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(restoreRoot);
        try
        {
            var restoreJson = Path.Combine(restoreRoot, "platform.json");
            var restoreDatabase = Path.ChangeExtension(restoreJson, ".db");
            File.Copy(fixture.Path, restoreJson);
            using (var sourceConnection = Open(fixture.Store.TransactionalStoragePath, SqliteOpenMode.ReadOnly))
            using (var destination = Open(restoreDatabase, SqliteOpenMode.ReadWriteCreate))
                sourceConnection.BackupDatabase(destination);
            var restored = new L12PlatformStore(restoreJson, fixture.Catalog.PresetDecks,
                officialCards: fixture.Catalog.Cards);
            Assert.Equal(source, Assert.Single(restored.ReadCommittedFriendChatHistory(sender.Id,
                recipient.Id, null, now.AddMinutes(1)).Entries).Text);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(restoreRoot, true);
        }
    }

    [Fact]
    public void AccountDeletionRollsChatErasureBackOnFailureAndCleansOnlyTheCommittedTarget()
    {
        using var fixture = new RankedIdentityFixture();
        var (sender, recipient) = AcceptedPair(fixture.Store);
        var now = Millisecond(DateTimeOffset.UtcNow);
        const string source = "private-body";
        fixture.Store.AppendCommittedFriendChat(sender.Id, recipient.Id, RequestId(now),
            L12ChatTextPolicy.Fingerprint("friend", recipient.Id, null, source), Apply(source),
            now, now, () => true);

        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("controlled delete rollback");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.DeleteAccountPersonalData(
            fixture.Admin, sender.Id, "chat privacy rollback", fixture.Context, true));
        fixture.Store.StorageFailureInjector = null;
        Assert.True(RowsForAccount(fixture.Store.TransactionalStoragePath, sender.Id) > 0);
        Assert.Empty(PendingDeletionIds(fixture.Store));

        var deleted = fixture.Store.DeleteAccountPersonalData(fixture.Admin, sender.Id,
            "chat privacy commit", fixture.Context, true);
        Assert.True(deleted.Applied);
        Assert.Equal(0, RowsForAccount(fixture.Store.TransactionalStoragePath, sender.Id));
        Assert.Empty(PendingDeletionIds(fixture.Store));
        Assert.Equal(0, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_storage_usage WHERE singleton_id=1;"));
        Assert.Equal(0, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_receipt_usage WHERE singleton_id=1;"));
        Assert.Equal(0, Scalar(fixture.Store.TransactionalStoragePath,
            "SELECT retained_items FROM chat_conversation_usage WHERE singleton_id=1;"));
    }

    private static (L12AccountView Sender, L12AccountView Recipient) AcceptedPair(L12PlatformStore store)
    {
        var sender = store.Register($"cs{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        var recipient = store.Register($"cr{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        Assert.True(store.SendFriendRequest(sender.Id, recipient.Id).Success);
        Assert.True(store.ResolveFriendRequest(recipient.Id, sender.Id, true).Success);
        return (sender, recipient);
    }

    private static L12ChatTextResult Apply(string source)
    {
        Assert.True(L12ChatTextPolicy.TryApply(source, out var result, out var error), error);
        return result;
    }

    private static string RequestId(DateTimeOffset issuedAt)
        => $"c1.{issuedAt.ToUnixTimeMilliseconds()}.{Guid.NewGuid():D}";

    private static DateTimeOffset Millisecond(DateTimeOffset value)
        => DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long RowsForAccount(string path, string accountId)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM chat_messages WHERE first_id=$id OR second_id=$id) +
                (SELECT COUNT(*) FROM chat_request_receipts
                    WHERE sender_id=$id OR recipient_id=$id OR first_id=$id OR second_id=$id) +
                (SELECT COUNT(*) FROM chat_read_cursors
                    WHERE account_id=$id OR first_id=$id OR second_id=$id) +
                (SELECT COUNT(*) FROM chat_conversations WHERE first_id=$id OR second_id=$id);
            """;
        command.Parameters.AddWithValue("$id", accountId);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string[] PendingDeletionIds(L12PlatformStore store)
        => ((HashSet<string>)typeof(L12PlatformStore).GetField("_pendingChatAccountDeletions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store)!).ToArray();
}
