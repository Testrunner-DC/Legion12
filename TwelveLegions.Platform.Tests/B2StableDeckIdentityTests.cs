using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class B2StableDeckIdentityTests
{
    [Fact]
    public async Task HttpContractUsesPostCreateAndRevisionGuardedIdRoutes()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var login = store.Register("tb2apiuser", "password-123");
            Assert.True(login.Success);
            var source = catalog.PresetDecks[0];
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            using var create = Authorized(HttpMethod.Post, "/api/decks", login.Token!, Submission(source, "HTTP 新建"));
            using var createdResponse = await client.SendAsync(create);
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = await createdResponse.Content.ReadFromJsonAsync<L12AccountDeckView>();
            Assert.NotNull(created);
            Assert.NotEmpty(created!.Id);
            Assert.Equal(1, created.Revision);
            Assert.Equal($"/api/decks/by-id/{created.Id}", createdResponse.Headers.Location!.OriginalString);

            using var rename = Authorized(HttpMethod.Put, $"/api/decks/by-id/{created.Id}", login.Token!, new
            {
                deck = Submission(source, "HTTP 改名"),
                expectedRevision = created.Revision,
            });
            using var renamedResponse = await client.SendAsync(rename);
            Assert.Equal(HttpStatusCode.OK, renamedResponse.StatusCode);
            var renamed = await renamedResponse.Content.ReadFromJsonAsync<L12AccountDeckView>();
            Assert.Equal(created.Id, renamed!.Id);
            Assert.Equal(2, renamed.Revision);

            using var stale = Authorized(HttpMethod.Put, $"/api/decks/by-id/{created.Id}", login.Token!, new
            {
                deck = Submission(source, "HTTP 陈旧"),
                expectedRevision = 1,
            });
            using var staleResponse = await client.SendAsync(stale);
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            using (var error = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal("deck_revision_conflict", error.RootElement.GetProperty("code").GetString());
                Assert.Equal(2, error.RootElement.GetProperty("currentRevision").GetInt64());
            }

            using var get = Authorized(HttpMethod.Get, "/api/decks", login.Token!);
            using var getResponse = await client.SendAsync(get);
            var listed = await getResponse.Content.ReadFromJsonAsync<List<L12AccountDeckView>>();
            Assert.Contains(listed!, item => item.Id == created.Id && item.Revision == 2 && item.Name == "HTTP 改名");

            using var delete = Authorized(HttpMethod.Delete,
                $"/api/decks/by-id/{created.Id}?expectedRevision=2", login.Token!);
            using var deletedResponse = await client.SendAsync(delete);
            Assert.Equal(HttpStatusCode.NoContent, deletedResponse.StatusCode);
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CreateRenameConflictAndStaleWritesPreserveOneStableOwnedDeck()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tb2owner", "password-123").Account!;
            var other = store.Register("tb2other", "password-123").Account!;
            var created = store.CreateDeck(owner.Id, Deck("原名称"));
            Assert.True(created.Success);
            Assert.NotEmpty(created.Deck!.Id);
            Assert.Equal(1, created.Deck.Revision);

            var renamed = store.UpdateDeck(owner.Id, created.Deck.Id, 1, Deck("新名称", "C2"));
            Assert.True(renamed.Success);
            Assert.Equal(created.Deck.Id, renamed.Deck!.Id);
            Assert.Equal(2, renamed.Deck.Revision);
            Assert.Equal("新名称", renamed.Deck.Name);

            var stale = store.UpdateDeck(owner.Id, created.Deck.Id, 1, Deck("陈旧标签页"));
            Assert.Equal("revision_conflict", stale.Status);
            Assert.Equal(2, stale.CurrentRevision);
            Assert.Equal("not_found", store.UpdateDeck(other.Id, created.Deck.Id, 2, Deck("越权")) .Status);

            var second = store.CreateDeck(owner.Id, Deck("第二副"));
            Assert.True(second.Success);
            var conflict = store.UpdateDeck(owner.Id, created.Deck.Id, 2, Deck(" 第二副 "));
            Assert.Equal("name_conflict", conflict.Status);
            Assert.Equal("name_conflict", store.CreateDeck(owner.Id, Deck("新名称".ToLowerInvariant())).Status);

            using (var connection = Open(store.TransactionalStoragePath))
            {
                Assert.Equal("1", Scalar(connection,
                    $"SELECT is_deleted FROM account_decks WHERE account_id='{owner.Id}' AND name='原名称';"));
                Assert.Equal(created.Deck.Id, Scalar(connection,
                    $"SELECT deck_id FROM account_decks WHERE account_id='{owner.Id}' AND name='新名称' AND is_deleted=0;"));
                Assert.Equal("1", Scalar(connection,
                    $"SELECT COUNT(*) FROM account_decks WHERE account_id='{owner.Id}' AND deck_id='{created.Deck.Id}' AND is_deleted=0;"));
            }

            var reloaded = new L12PlatformStore(path);
            var restored = reloaded.Decks(owner.Id).Single(item => item.Id == created.Deck.Id);
            Assert.Equal("新名称", restored.Name);
            Assert.Equal(2, restored.Revision);
            Assert.Equal("revision_conflict", reloaded.DeleteDeck(owner.Id, restored.Id, 1).Status);
            Assert.True(reloaded.DeleteDeck(owner.Id, restored.Id, 2).Success);
            Assert.DoesNotContain(reloaded.Decks(owner.Id), item => item.Id == restored.Id);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedCommitRestoresDeckIdentityRevisionAndNameInMemoryAndOnRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tb2failure", "password-123").Account!;
            var created = store.CreateDeck(owner.Id, Deck("提交前"));
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("b2-simulated-commit-failure");
            };

            var error = Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.UpdateDeck(owner.Id, created.Deck!.Id, created.Deck.Revision, Deck("不得留下")));
            Assert.Contains("b2-simulated-commit-failure", error.Message, StringComparison.Ordinal);
            var inMemory = Assert.Single(store.Decks(owner.Id));
            Assert.Equal(created.Deck!.Id, inMemory.Id);
            Assert.Equal(created.Deck.Revision, inMemory.Revision);
            Assert.Equal("提交前", inMemory.Name);

            store.StorageFailureInjector = null;
            var restarted = new L12PlatformStore(path);
            var durable = Assert.Single(restarted.Decks(owner.Id));
            Assert.Equal(inMemory.Id, durable.Id);
            Assert.Equal(inMemory.Revision, durable.Revision);
            Assert.Equal(inMemory.Name, durable.Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OldSqliteRowsBackfillIdentityAndMirrorKeepsItAcrossRestarts()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tb2legacy", "password-123").Account!;
            store.CreateDeck(owner.Id, Deck("旧库牌组"));

            using (var connection = Open(store.TransactionalStoragePath))
            {
                Execute(connection, "DROP TRIGGER IF EXISTS account_decks_active_identity_insert;");
                Execute(connection, "DROP TRIGGER IF EXISTS account_decks_active_identity_update;");
                Execute(connection, "DROP INDEX IF EXISTS ux_account_decks_active_id;");
                Execute(connection, "ALTER TABLE account_decks DROP COLUMN deck_id;");
                Execute(connection, "ALTER TABLE account_decks DROP COLUMN revision;");
                Execute(connection, "DELETE FROM storage_meta WHERE key='deck_identity_state';");
            }
            var identityMap = store.TransactionalStoragePath + ".pre-deck-identity-v1.json.gz";
            if (File.Exists(identityMap)) File.Delete(identityMap);

            var migrated = new L12PlatformStore(path);
            var deck = Assert.Single(migrated.Decks(owner.Id));
            Assert.NotEmpty(deck.Id);
            Assert.Equal(1, deck.Revision);
            using (var connection = Open(migrated.TransactionalStoragePath))
            {
                Assert.Equal(deck.Id, Scalar(connection,
                    $"SELECT deck_id FROM account_decks WHERE account_id='{owner.Id}' AND is_deleted=0;"));
                Assert.Equal("active-v1", Scalar(connection,
                    "SELECT value FROM storage_meta WHERE key='deck_identity_state';"));
            }
            Assert.True(File.Exists(identityMap));
            using (var file = File.OpenRead(identityMap))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            using (var document = JsonDocument.Parse(reader.ReadToEnd()))
            {
                var mirrored = document.RootElement.EnumerateArray().Single();
                Assert.Equal(deck.Id, mirrored.GetProperty("Id").GetString());
                Assert.Equal(1, mirrored.GetProperty("Revision").GetInt64());
            }

            var restarted = new L12PlatformStore(path);
            Assert.Equal(deck.Id, Assert.Single(restarted.Decks(owner.Id)).Id);

            using (var connection = Open(restarted.TransactionalStoragePath))
            {
                Execute(connection, "DROP TRIGGER IF EXISTS account_decks_active_identity_insert;");
                Execute(connection, "DROP TRIGGER IF EXISTS account_decks_active_identity_update;");
                Execute(connection, "DROP INDEX IF EXISTS ux_account_decks_active_id;");
                Execute(connection, "ALTER TABLE account_decks DROP COLUMN deck_id;");
                Execute(connection, "ALTER TABLE account_decks DROP COLUMN revision;");
                Execute(connection, "DELETE FROM storage_meta WHERE key='deck_identity_state';");
            }
            var retried = new L12PlatformStore(path);
            Assert.Equal(deck.Id, Assert.Single(retried.Decks(owner.Id)).Id);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RolledBackBinaryCanInsertWithoutIdentityAndNewStartupBackfillsFromFullBackup()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tb2rollback", "password-123").Account!;
            var original = store.CreateDeck(owner.Id, Deck("新版本牌库"));
            Assert.True(original.Success);
            var rollbackReady = new L12PlatformStore(path);
            var fullBackup = rollbackReady.TransactionalStoragePath + ".pre-deck-identity-v1.full.json.gz";
            Assert.True(File.Exists(fullBackup));
            string payloadHash;
            using (var connection = Open(rollbackReady.TransactionalStoragePath))
            {
                Assert.Equal("0", Scalar(connection, """
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type='trigger' AND name LIKE 'account_decks_active_identity_%';
                    """));
                payloadHash = Scalar(connection,
                    $"SELECT payload_hash FROM account_decks WHERE account_id='{owner.Id}' AND is_deleted=0;");
                Execute(connection, $$"""
                    INSERT INTO account_decks(
                        account_id,name_key,name,payload_hash,alternate_art_selections_json,
                        alternate_art_copies_json,bench_cards_json,updated_utc,is_deleted,
                        publication_id,publication_version)
                    VALUES('{{owner.Id}}','旧客户端新增','旧客户端新增','{{payloadHash}}','{}','{}','[]',
                           '{{DateTimeOffset.UtcNow:O}}',0,NULL,NULL);
                    """);
                Assert.Equal(string.Empty, Scalar(connection,
                    $"SELECT deck_id FROM account_decks WHERE account_id='{owner.Id}' AND name='旧客户端新增';"));
            }

            var migrated = new L12PlatformStore(path);
            var oldBinaryDeck = migrated.Decks(owner.Id).Single(item => item.Name == "旧客户端新增");
            Assert.NotEmpty(oldBinaryDeck.Id);
            Assert.Equal(1, oldBinaryDeck.Revision);
            Assert.NotEqual(original.Deck!.Id, oldBinaryDeck.Id);

            var identityMap = migrated.TransactionalStoragePath + ".pre-deck-identity-v1.json.gz";
            Assert.True(File.Exists(fullBackup));
            Assert.True(File.Exists(identityMap));
            string fullBackupJson;
            using (var file = File.OpenRead(fullBackup))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                fullBackupJson = reader.ReadToEnd();
                using var document = JsonDocument.Parse(fullBackupJson);
                Assert.True(document.RootElement.TryGetProperty("Accounts", out _));
                var decks = document.RootElement.GetProperty("Decks").EnumerateArray().ToArray();
                Assert.Equal(2, decks.Length);
                Assert.Contains(decks, deck => deck.GetProperty("Name").GetString() == "旧客户端新增");
            }
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));

            var restoreRoot = Path.Combine(root, "restore");
            Directory.CreateDirectory(restoreRoot);
            var restoredPath = Path.Combine(restoreRoot, "platform.json");
            File.WriteAllText(restoredPath, fullBackupJson);
            var restoredFromBackup = new L12PlatformStore(restoredPath);
            Assert.Contains(restoredFromBackup.Decks(owner.Id), item => item.Name == "新版本牌库");
            Assert.Contains(restoredFromBackup.Decks(owner.Id), item => item.Name == "旧客户端新增");

            var restarted = new L12PlatformStore(path);
            Assert.Equal(oldBinaryDeck.Id,
                restarted.Decks(owner.Id).Single(item => item.Name == "旧客户端新增").Id);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CopyingEquivalentDeckContentCreatesANewIdentity()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var owner = store.Register("tb2copy", "password-123").Account!;
            var source = store.CreateDeck(owner.Id, Deck("复制来源"));
            var copy = store.CreateDeck(owner.Id, Deck("复制结果"));
            Assert.True(source.Success);
            Assert.True(copy.Success);
            Assert.NotEqual(source.Deck!.Id, copy.Deck!.Id);
            Assert.Equal(source.Deck.CardIds, copy.Deck.CardIds);
        }
        finally { Directory.Delete(root, true); }
    }

    private static L12PresetDeckDefinition Deck(string name, string cardId = "C1") => new()
    {
        Name = name,
        MasterId = "M1",
        CardIds = [cardId, cardId],
        MoraleIds = ["R1"],
        SpecialIds = [],
    };

    private static L12CustomDeckSubmission Submission(L12PresetDeckDefinition source, string name) => new()
    {
        Name = name,
        MasterId = source.MasterId,
        CardIds = [.. source.CardIds],
        MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds],
        BenchIds = [.. source.BenchIds],
    };

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        connection.Open();
        return connection;
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-b2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
