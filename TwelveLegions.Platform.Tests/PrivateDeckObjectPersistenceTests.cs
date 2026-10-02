using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PrivateDeckObjectPersistenceTests
{
    [Fact]
    public void FeatureDefaultsOffAndEnabledUpsertUsesBoundedObjectWrite()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("objectowner", "password-123").Account!;
            for (var index = 0; index < 20; index++)
                store.CreateDeck(owner.Id, Deck($"牌库-{index}", $"C{index}"));
            Assert.False(store.PrivateDeckObjectPersistenceEnabled);

            L12SyntheticStorageMeasurement full;
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                store.UpsertDeck(owner.Id, Deck("牌库-0", "FULL"));
                full = scope.Complete();
            }

            store.PrivateDeckObjectPersistenceEnabled = true;
            L12SyntheticStorageMeasurement point;
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                store.UpsertDeck(owner.Id, Deck("牌库-0", "POINT"));
                point = scope.Complete();
            }

            Assert.True(full.DeckDomainStatements >= 40);
            Assert.InRange(point.DeckDomainStatements, 1, 12);
            Assert.True(point.DeckDomainStatements * 3 < full.DeckDomainStatements);
            Assert.InRange(point.InclusiveAffectedRows, 1, 8);
            Assert.True(point.MirrorBytes > 0);
            Assert.Equal(0, point.InstrumentationErrors);
            Assert.Equal("POINT", new L12PlatformStore(path).Decks(owner.Id).Single(deck => deck.Name == "牌库-0").CardIds[0]);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void CreateUpdateRenameAndBothDeleteContractsSurviveRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var owner = store.Register("objectcrud", "password-123").Account!;
            var created = store.CreateDeck(owner.Id, Deck("初始", "C1"));
            Assert.True(created.Success);
            Assert.False(string.IsNullOrWhiteSpace(created.Deck!.Id));
            Assert.Equal(1, created.Deck.Revision);

            var updated = store.UpdateDeck(owner.Id, created.Deck.Id, created.Deck.Revision,
                Deck("改名", "C2"));
            Assert.True(updated.Success);
            Assert.Equal(2, updated.Deck!.Revision);
            var compatible = store.UpsertDeck(owner.Id, Deck("改名", "C3"));
            Assert.Equal(updated.Deck.Id, compatible.Id);
            Assert.Equal(3, compatible.Revision);
            Assert.True(store.DeleteDeck(owner.Id, compatible.Id, compatible.Revision).Success);

            var second = store.UpsertDeck(owner.Id, Deck("兼容新建", "C4"));
            Assert.False(string.IsNullOrWhiteSpace(second.Id));
            Assert.True(store.DeleteDeck(owner.Id, "兼容新建"));
            Assert.Empty(new L12PlatformStore(path).Decks(owner.Id));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void ObjectWriteMatchesLegacyFullSnapshotOnIsolatedCopy()
    {
        var root = TempRoot();
        var seedPath = Path.Combine(root, "seed", "platform.json");
        var fullPath = Path.Combine(root, "full", "platform.json");
        var pointPath = Path.Combine(root, "point", "platform.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
            var seed = new L12PlatformStore(seedPath);
            var owner = seed.Register("objectcopy", "password-123").Account!;
            var created = seed.CreateDeck(owner.Id, Deck("复制前", "C1")).Deck!;
            Checkpoint(seed.TransactionalStoragePath);
            CopyStore(seedPath, fullPath);
            CopyStore(seedPath, pointPath);

            var full = new L12PlatformStore(fullPath);
            var point = new L12PlatformStore(pointPath) { PrivateDeckObjectPersistenceEnabled = true };
            Assert.True(full.UpdateDeck(owner.Id, created.Id, created.Revision, Deck("复制后", "C2")).Success);
            Assert.True(point.UpdateDeck(owner.Id, created.Id, created.Revision, Deck("复制后", "C2")).Success);

            Assert.Equal(ReadDeckProjection(full.TransactionalStoragePath), ReadDeckProjection(point.TransactionalStoragePath));
            Assert.Equal(ReadPlatformProjection(full.TransactionalStoragePath), ReadPlatformProjection(point.TransactionalStoragePath));
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(fullPath)), SHA256.HashData(File.ReadAllBytes(pointPath)));
            Assert.Equal(SemanticDecks(full, owner.Id), SemanticDecks(point, owner.Id));
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData("after-private-deck-payload")]
    [InlineData("after-private-deck-row")]
    [InlineData("before-private-deck-snapshot")]
    [InlineData("before-commit")]
    public void PreCommitFaultRollsBackMemoryDatabaseSnapshotAndMirror(string failureStage)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("objectfault", "password-123").Account!;
            var original = store.CreateDeck(owner.Id, Deck("提交前", "C1")).Deck!;
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(path));
            var platformBefore = ReadPlatformProjection(store.TransactionalStoragePath);
            store.PrivateDeckObjectPersistenceEnabled = true;
            store.StorageFailureInjector = stage =>
            {
                if (stage == failureStage) throw new InvalidOperationException(failureStage);
            };

            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.UpdateDeck(owner.Id, original.Id, original.Revision, Deck("不得留下", "C2")));
            store.StorageFailureInjector = null;

            Assert.Equal("提交前", Assert.Single(store.Decks(owner.Id)).Name);
            Assert.Equal(platformBefore, ReadPlatformProjection(store.TransactionalStoragePath));
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal("提交前", Assert.Single(new L12PlatformStore(path).Decks(owner.Id)).Name);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MirrorFaultKeepsCommittedSqliteAndReportsUnhealthyMirror()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("objmirror", "password-123").Account!;
            var original = store.CreateDeck(owner.Id, Deck("镜像前", "C1")).Deck!;
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(path));
            store.PrivateDeckObjectPersistenceEnabled = true;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-private-deck-mirror") throw new InvalidOperationException(stage);
            };

            var result = store.UpdateDeck(owner.Id, original.Id, original.Revision, Deck("镜像后", "C2"));
            store.StorageFailureInjector = null;

            Assert.True(result.Success);
            Assert.False(store.StorageStatus().FallbackMirrorHealthy);
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal("镜像后", Assert.Single(new L12PlatformStore(path).Decks(owner.Id)).Name);
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData("create", "after-private-deck-payload")]
    [InlineData("create", "after-private-deck-row")]
    [InlineData("create", "before-private-deck-snapshot")]
    [InlineData("create", "before-commit")]
    [InlineData("delete", "after-private-deck-row")]
    [InlineData("delete", "before-private-deck-snapshot")]
    [InlineData("delete", "before-commit")]
    public void CreateAndDeletePreCommitFaultsLeaveNoPartialMutation(string operation, string failureStage)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("objfault2", "password-123").Account!;
            var original = operation == "delete"
                ? store.CreateDeck(owner.Id, Deck("保留", "C1")).Deck
                : null;
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(path));
            var platformBefore = ReadPlatformProjection(store.TransactionalStoragePath);
            store.PrivateDeckObjectPersistenceEnabled = true;
            store.StorageFailureInjector = stage =>
            {
                if (stage == failureStage) throw new InvalidOperationException(failureStage);
            };

            if (operation == "create")
                Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                    store.CreateDeck(owner.Id, Deck("不得创建", "C2")));
            else
                Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                    store.DeleteDeck(owner.Id, original!.Id, original.Revision));
            store.StorageFailureInjector = null;

            var expectedName = operation == "delete" ? "保留" : null;
            Assert.Equal(expectedName, store.Decks(owner.Id).SingleOrDefault()?.Name);
            Assert.Equal(platformBefore, ReadPlatformProjection(store.TransactionalStoragePath));
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(expectedName, new L12PlatformStore(path).Decks(owner.Id).SingleOrDefault()?.Name);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void SameRevisionAndConcurrentNameClaimsHaveOneWinner()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var seed = new L12PlatformStore(path);
            var owner = seed.Register("objectrace", "password-123").Account!;
            var original = seed.CreateDeck(owner.Id, Deck("并发前", "C1")).Deck!;
            var first = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var second = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };

            Assert.True(first.UpdateDeck(owner.Id, original.Id, original.Revision, Deck("胜者", "C2")).Success);
            var stale = second.UpdateDeck(owner.Id, original.Id, original.Revision, Deck("败者", "C3"));
            Assert.Equal("revision_conflict", stale.Status);
            Assert.Equal(2, stale.CurrentRevision);
            Assert.Equal("胜者", Assert.Single(new L12PlatformStore(path).Decks(owner.Id)).Name);

            var third = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var fourth = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            Assert.True(third.CreateDeck(owner.Id, Deck("唯一名称", "C4")).Success);
            Assert.Equal("name_conflict", fourth.CreateDeck(owner.Id, Deck(" 唯一名称 ", "C5")).Status);
            Assert.Equal(2, new L12PlatformStore(path).Decks(owner.Id).Count);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void StaleInstanceCannotOverwriteDifferentDeckSnapshotAndCanRetryAfterRefresh()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var seed = new L12PlatformStore(path);
            var owner = seed.Register("objtwodeck", "password-123").Account!;
            var firstDeck = seed.CreateDeck(owner.Id, Deck("第一副", "C1")).Deck!;
            var secondDeck = seed.CreateDeck(owner.Id, Deck("第二副", "C2")).Deck!;
            var first = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var stale = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };

            Assert.True(first.UpdateDeck(owner.Id, firstDeck.Id, firstDeck.Revision,
                Deck("第一副已更新", "C3")).Success);
            var conflict = stale.UpdateDeck(owner.Id, secondDeck.Id, secondDeck.Revision,
                Deck("不得覆盖快照", "C4"));
            Assert.Equal("storage_conflict", conflict.Status);
            Assert.Contains(stale.Decks(owner.Id), deck => deck.Name == "第一副已更新");
            Assert.Contains(stale.Decks(owner.Id), deck => deck.Name == "第二副" && deck.Revision == 1);

            var refreshedSecond = stale.Decks(owner.Id).Single(deck => deck.Id == secondDeck.Id);
            Assert.True(stale.UpdateDeck(owner.Id, refreshedSecond.Id, refreshedSecond.Revision,
                Deck("第二副已重试", "C5")).Success);
            var recovered = new L12PlatformStore(path).Decks(owner.Id);
            Assert.Contains(recovered, deck => deck.Name == "第一副已更新");
            Assert.Contains(recovered, deck => deck.Name == "第二副已重试");
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void StaleObjectWriteCannotOverwriteNewerNonDeckPlatformState()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var seed = new L12PlatformStore(path);
            var owner = seed.Register("objplatform", "password-123").Account!;
            var deck = seed.CreateDeck(owner.Id, Deck("平台前", "C1")).Deck!;
            var writer = new L12PlatformStore(path);
            var stale = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };

            var lateAccount = writer.Register("lateacct", "password-123");
            Assert.True(lateAccount.Success);
            var conflict = stale.UpdateDeck(owner.Id, deck.Id, deck.Revision, Deck("不得覆盖平台", "C2"));
            Assert.Equal("storage_conflict", conflict.Status);
            Assert.Contains(stale.Accounts(), account => account.Username == "lateacct");
            Assert.Equal("平台前", Assert.Single(stale.Decks(owner.Id)).Name);
            Assert.Contains(new L12PlatformStore(path).Accounts(), account => account.Username == "lateacct");
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void NullIdentityBackfillCanImmediatelyEnterObjectCrud()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var seed = new L12PlatformStore(path);
            var owner = seed.Register("objlegacy", "password-123").Account!;
            seed.CreateDeck(owner.Id, Deck("旧身份", "C1"));
            Checkpoint(seed.TransactionalStoragePath);
            using (var connection = new SqliteConnection($"Data Source={seed.TransactionalStoragePath}"))
            {
                connection.Open();
                Execute(connection, "UPDATE account_decks SET deck_id=NULL,revision=1 WHERE is_deleted=0;");
                Execute(connection, "DELETE FROM storage_meta WHERE key='deck_identity_state';");
            }

            var migrated = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var backfilled = Assert.Single(migrated.Decks(owner.Id));
            Assert.False(string.IsNullOrWhiteSpace(backfilled.Id));
            var updated = migrated.UpdateDeck(owner.Id, backfilled.Id, backfilled.Revision,
                Deck("旧身份已更新", "C2"));
            Assert.True(updated.Success);
            Assert.True(migrated.DeleteDeck(owner.Id, updated.Deck!.Id, updated.Deck.Revision).Success);
            Assert.Empty(new L12PlatformStore(path).Decks(owner.Id));
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleOrdinarySaveCannotOverwriteNewDeckAndCanRetry(bool objectWriter)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var seed = new L12PlatformStore(path);
            var owner = seed.Register("ordrace", "password-123").Account!;
            var deck = seed.CreateDeck(owner.Id, Deck("原牌库", "C1")).Deck!;
            var writer = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = objectWriter };
            var stale = new L12PlatformStore(path);
            Assert.True(writer.UpdateDeck(owner.Id, deck.Id, 1, Deck("最新牌库", "C2")).Success);
            var databaseBefore = ReadPlatformProjection(writer.TransactionalStoragePath);
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(path));

            var error = Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() =>
                stale.Register("不得覆盖", "password-123"));
            Assert.Contains("请", error.Message);
            Assert.Equal(databaseBefore, ReadPlatformProjection(writer.TransactionalStoragePath));
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.DoesNotContain(stale.Accounts(), account => account.Username == "不得覆盖");
            Assert.Equal("最新牌库", Assert.Single(stale.Decks(owner.Id)).Name);
            Assert.True(stale.Register("正常重试", "password-123").Success);
            var recovered = new L12PlatformStore(path);
            Assert.Equal("最新牌库", Assert.Single(recovered.Decks(owner.Id)).Name);
            Assert.Contains(recovered.Accounts(), account => account.Username == "正常重试");
        }
        finally { DeleteRoot(root); }
    }

    private static L12PresetDeckDefinition Deck(string name, string cardId) => new()
    {
        Name = name,
        MasterId = "M1",
        CardIds = [cardId, cardId],
        MoraleIds = ["R1"],
        SpecialIds = [],
        BenchIds = ["B1"],
    };

    private static string[] SemanticDecks(L12PlatformStore store, string accountId) => store.Decks(accountId)
        .OrderBy(deck => deck.Id, StringComparer.Ordinal)
        .Select(deck => $"{deck.Id}|{deck.Revision}|{deck.Name}|{deck.MasterId}|{string.Join(',', deck.CardIds)}")
        .ToArray();

    private static string[] ReadDeckProjection(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id,name_key,name,deck_id,revision,payload_hash,
                   alternate_art_selections_json,alternate_art_copies_json,bench_cards_json,is_deleted,
                   COALESCE(publication_id,''),COALESCE(publication_version,-1)
            FROM account_decks ORDER BY account_id,name_key;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount)
                .Select(index => Convert.ToString(reader.GetValue(index)))));
        return rows.ToArray();
    }

    private static string ReadPlatformProjection(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT storage_revision || '|' || business_version || '|' || snapshot_sha256 || '|'
                   || (SELECT value FROM storage_meta WHERE key='fallback_json_sha256')
            FROM platform_state WHERE singleton_id=1;
            """;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static void Checkpoint(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void CopyStore(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath);
        File.Copy(Path.ChangeExtension(sourcePath, ".db"), Path.ChangeExtension(targetPath, ".db"));
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-private-object-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string root)
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(root, true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(20);
            }
        }
    }
}
