using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PrivateDeckPersistenceStartupTests
{
    private const string StatusRoute = "/api/admin/storage/private-deck-persistence";

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData(" true ", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void StartupParserDefaultsOffAndAcceptsOnlyBooleanNames(string? value, bool expected)
        => Assert.Equal(expected, L12PrivateDeckPersistenceStartup.Parse(value));

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("true\0")]
    [InlineData("false,true")]
    [InlineData("private-secret-path")]
    public void InvalidStartupConfigurationIsRejectedWithoutEchoingTheValue(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => L12PrivateDeckPersistenceStartup.Parse(value));
        Assert.Contains(L12PrivateDeckPersistenceStartup.EnvironmentKey, error.Message);
        Assert.Equal($"{L12PrivateDeckPersistenceStartup.EnvironmentKey} 仅允许 true 或 false；修改后需重启服务。",
            error.Message);
    }

    [Fact]
    public void ProgramParsesBeforeAnyRuntimeWritesAndAppliesBeforeServing()
    {
        // Guard actual product wiring without spawning Program against its build/runtime directory.
        var source = FindProgramSource();
        var parse = source.IndexOf("var privateDeckObjectPersistenceEnabled = L12PrivateDeckPersistenceStartup.Parse(", StringComparison.Ordinal);
        var createDirectory = source.IndexOf("Directory.CreateDirectory(runtimePath)", StringComparison.Ordinal);
        var prepare = source.IndexOf("L12TestRunStorageProfile.Prepare(", StringComparison.Ordinal);
        var construct = source.IndexOf("var platform = new L12PlatformStore(", StringComparison.Ordinal);
        var apply = source.IndexOf("platform.ApplyPrivateDeckPersistenceStartup(privateDeckObjectPersistenceEnabled)", StringComparison.Ordinal);
        var start = source.IndexOf("await server.StartAsync(port)", StringComparison.Ordinal);
        Assert.True(parse >= 0 && parse < createDirectory && parse < prepare && parse < construct);
        Assert.True(construct < apply && apply < start);
    }

    [Fact]
    public void StartupModeIsCapturedOnceAndEnvironmentChangesDoNotHotSwitchIt()
    {
        var root = TempRoot();
        var previous = Environment.GetEnvironmentVariable(L12PrivateDeckPersistenceStartup.EnvironmentKey);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.False(store.PrivateDeckObjectPersistenceEnabled);
            Environment.SetEnvironmentVariable(L12PrivateDeckPersistenceStartup.EnvironmentKey, "true");
            var captured = L12PrivateDeckPersistenceStartup.Parse(
                Environment.GetEnvironmentVariable(L12PrivateDeckPersistenceStartup.EnvironmentKey));
            store.ApplyPrivateDeckPersistenceStartup(captured);
            Environment.SetEnvironmentVariable(L12PrivateDeckPersistenceStartup.EnvironmentKey, "false");
            Assert.Equal("object", store.PrivateDeckPersistenceStatus(admin).EffectiveMode);
            Assert.Throws<InvalidOperationException>(() => store.ApplyPrivateDeckPersistenceStartup(false));
            Assert.True(store.PrivateDeckObjectPersistenceEnabled);
            Assert.True(store.PrivateDeckPersistenceStatus(admin).RestartRequiredForChange);
        }
        finally
        {
            Environment.SetEnvironmentVariable(L12PrivateDeckPersistenceStartup.EnvironmentKey, previous);
            DeleteRoot(root);
        }
    }

    [Fact]
    public void StartupEnabledWritesThenDisabledRestartPreservesLatestFactsAndUsesFullSave()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var enabled = new L12PlatformStore(path);
            enabled.ApplyPrivateDeckPersistenceStartup(L12PrivateDeckPersistenceStartup.Parse("true"));
            var owner = enabled.Register("startupown", "password-123").Account!;
            for (var i = 0; i < 20; i++)
                Assert.True(enabled.CreateDeck(owner.Id, Deck($"saved-{i}", $"C{i}")).Success);
            var original = enabled.Decks(owner.Id).Single(deck => deck.Name == "saved-0");
            L12SyntheticStorageMeasurement point;
            using (var measurement = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                var result = enabled.UpdateDeck(owner.Id, original.Id, original.Revision, Deck("renamed", "LATEST"));
                Assert.True(result.Success);
                point = measurement.Complete();
            }
            var latest = enabled.Decks(owner.Id).Single(deck => deck.Id == original.Id);
            Assert.Equal(original.Revision + 1, latest.Revision);
            Assert.True(enabled.DeleteDeck(owner.Id, enabled.Decks(owner.Id).Single(deck => deck.Name == "saved-1").Id,
                enabled.Decks(owner.Id).Single(deck => deck.Name == "saved-1").Revision).Success);
            var retainedDeleted = enabled.StorageStatus().DeckStorage!.RetainedDeletedAccountDecks;
            Assert.Equal(2, retainedDeleted);
            var retainedDeletedFacts = ReadAccountDeckFacts(path, deleted: true);
            Assert.Equal(retainedDeleted, retainedDeletedFacts.Count);
            var renamedKey = owner.Id + "\nRENAMED";
            var renamedFacts = ReadAccountDeckFacts(path, deleted: false)[renamedKey];
            var beforeRestart = DurableFingerprint(path);

            // Reopen the latest same database; do not restore an earlier database or JSON mirror.
            var disabled = new L12PlatformStore(path);
            disabled.ApplyPrivateDeckPersistenceStartup(L12PrivateDeckPersistenceStartup.Parse("false"));
            Assert.Equal(beforeRestart, DurableFingerprint(path));
            var recovered = disabled.Decks(owner.Id).Single(deck => deck.Id == latest.Id);
            Assert.Equal(latest.Revision, recovered.Revision);
            Assert.Equal("LATEST", Assert.Single(recovered.CardIds));
            Assert.DoesNotContain(disabled.Decks(owner.Id), deck => deck.Name == "saved-1");
            var admin = disabled.Login("Admin", "L12master").Account!;
            Assert.Equal("full-snapshot", disabled.PrivateDeckPersistenceStatus(admin).EffectiveMode);
            L12SyntheticStorageMeasurement full;
            using (var measurement = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                var result = disabled.UpdateDeck(owner.Id, recovered.Id, recovered.Revision, Deck("after-off", "FULL"));
                Assert.True(result.Success);
                full = measurement.Complete();
            }
            Assert.InRange(point.DeckDomainStatements, 1, 12);
            Assert.True(full.DeckDomainStatements >= 40);
            Assert.True(full.DeckDomainStatements > point.DeckDomainStatements * 3);
            Assert.Equal(0, point.InstrumentationErrors);
            Assert.Equal(0, full.InstrumentationErrors);
            var restarted = new L12PlatformStore(path);
            var final = restarted.Decks(owner.Id).Single(deck => deck.Id == latest.Id);
            Assert.Equal(latest.Revision + 1, final.Revision);
            Assert.Equal("FULL", Assert.Single(final.CardIds));
            Assert.Equal(19, restarted.Decks(owner.Id).Count);
            // The second rename legitimately adds one tombstone; every older tombstone must remain exact.
            var deletedAfterFullSave = ReadAccountDeckFacts(path, deleted: true);
            Assert.Equal(retainedDeleted + 1, restarted.StorageStatus().DeckStorage!.RetainedDeletedAccountDecks);
            Assert.Equal(retainedDeleted + 1, deletedAfterFullSave.Count);
            foreach (var (key, fact) in retainedDeletedFacts)
            {
                Assert.True(deletedAfterFullSave.ContainsKey(key));
                Assert.Equal(fact, deletedAfterFullSave[key]);
            }
            var addedDeletedKey = Assert.Single(deletedAfterFullSave.Keys.Except(retainedDeletedFacts.Keys));
            Assert.Equal(renamedKey, addedDeletedKey);
            Assert.Equal(renamedFacts, deletedAfterFullSave[addedDeletedKey]);
            Assert.Equal(latest.Id, deletedAfterFullSave[addedDeletedKey].DeckId);
            Assert.Equal(latest.Revision, deletedAfterFullSave[addedDeletedKey].Revision);
            Assert.True(restarted.RehearseStorageRecovery().Success);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void FailedConflictRefreshReportsReadonlyEvenWhenStartupWasEnabled()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var stale = new L12PlatformStore(path);
            stale.ApplyPrivateDeckPersistenceStartup(true);
            var admin = stale.Login("Admin", "L12master").Account!;
            var writer = new L12PlatformStore(path);
            Assert.True(writer.Register("lateruser", "password-123").Success);
            stale.StorageFailureInjector = stage =>
            {
                if (stage == "before-conflict-refresh") throw new IOException("internal-private-secret-path");
            };
            Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() => stale.Register("rolledback", "password-123"));
            var status = stale.PrivateDeckPersistenceStatus(admin);
            Assert.True(status.ConfiguredEnabled);
            Assert.False(status.Writable);
            Assert.Equal("readonly", status.EffectiveMode);
            Assert.Equal("unavailable", status.StorageMode);
            Assert.DoesNotContain("internal-private-secret-path", JsonSerializer.Serialize(status));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.Register("blockednow", "password-123"));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MirrorFailureIsReportedWithoutPretendingCommittedDatabaseIsReadonly()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            store.ApplyPrivateDeckPersistenceStartup(true);
            var admin = store.Login("Admin", "L12master").Account!;
            var owner = store.Register("mirrorown", "password-123").Account!;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-private-deck-mirror") throw new IOException("internal-mirror-secret-path");
            };
            Assert.True(store.CreateDeck(owner.Id, Deck("committed", "C1")).Success);
            var status = store.PrivateDeckPersistenceStatus(admin);
            Assert.True(status.Writable);
            Assert.Equal("object", status.EffectiveMode);
            Assert.False(status.FallbackMirrorHealthy);
            Assert.DoesNotContain("internal-mirror-secret-path", JsonSerializer.Serialize(status));
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusHttpRequiresOperationsReadAndExposesNoMutationEndpoint(bool enabled)
    {
        await InHttp(enabled, false, async (store, client, adminToken, playerToken, path) =>
        {
            using (var anonymous = await client.GetAsync(StatusRoute))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
                Assert.True(anonymous.Headers.CacheControl?.NoStore);
            }
            using (var request = Authorized(HttpMethod.Get, StatusRoute, playerToken))
            using (var denied = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                Assert.True(denied.Headers.CacheControl?.NoStore);
                Assert.DoesNotContain(Path.GetDirectoryName(path)!, await denied.Content.ReadAsStringAsync());
            }
            using (var request = Authorized(HttpMethod.Post, StatusRoute, adminToken))
            using (var rejected = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.MethodNotAllowed, rejected.StatusCode);
            var ordinary = store.AuthenticateToken(playerToken)!;
            Assert.Throws<L12OperationsConfigException>(() => store.PrivateDeckPersistenceStatus(ordinary));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task StatusHttpReportsCapturedEffectiveModeWithoutStorageReadsWritesOrPrivatePaths(bool enabled, bool fallback)
    {
        await InHttp(enabled, fallback, async (store, client, adminToken, playerToken, path) =>
        {
            var before = fallback ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : DurableFingerprint(path);
            var revision = store.StorageStatus().StorageRevision;
            var businessVersion = store.Version;
            // A state read must not run storage probes, perform migrations, or serialize a snapshot.
            store.StorageFailureInjector = _ => throw new InvalidOperationException("status-must-not-write-or-probe");
            foreach (var route in new[] { StatusRoute, StatusRoute.Replace("/api/admin/", "/api/admin/v1/", StringComparison.Ordinal) })
            {
                using var request = Authorized(HttpMethod.Get, route, adminToken);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
                Assert.True(response.Headers.Contains(L12CorrelationIds.HeaderName));
                var body = await response.Content.ReadAsStringAsync();
                using var status = JsonDocument.Parse(body);
                Assert.Equal(enabled, status.RootElement.GetProperty("configuredEnabled").GetBoolean());
                Assert.Equal(!fallback, status.RootElement.GetProperty("writable").GetBoolean());
                Assert.Equal(fallback ? "readonly" : enabled ? "object" : "full-snapshot",
                    status.RootElement.GetProperty("effectiveMode").GetString());
                Assert.Equal(fallback ? "json-fallback-readonly" : "sqlite",
                    status.RootElement.GetProperty("storageMode").GetString());
                Assert.True(status.RootElement.GetProperty("restartRequiredForChange").GetBoolean());
                Assert.DoesNotContain(Path.GetDirectoryName(path)!, body);
                Assert.DoesNotContain("platform.json", body);
                Assert.DoesNotContain("platform.db", body);
                Assert.DoesNotContain("status-must-not-write-or-probe", body);
                Assert.False(status.RootElement.TryGetProperty("issue", out _));
            }
            store.StorageFailureInjector = null;
            Assert.Equal(revision, store.StorageStatus().StorageRevision);
            Assert.Equal(businessVersion, store.Version);
            Assert.Equal(before, fallback ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : DurableFingerprint(path));
            using var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            using var publicStatus = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
            Assert.False(publicStatus.RootElement.TryGetProperty("configuredEnabled", out _));
            Assert.False(publicStatus.RootElement.TryGetProperty("effectiveMode", out _));
            Assert.False(publicStatus.RootElement.TryGetProperty("databasePath", out _));
        });
    }

    private static async Task InHttp(bool enabled, bool fallback,
        Func<L12PlatformStore, HttpClient, string, string, string, Task> action)
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("tf2start", "password-123");
            Assert.True(admin.Success, admin.Message);
            Assert.True(player.Success, player.Message);
            if (fallback)
            {
                var fullLegacyJson = FullJson(store);
                SqliteConnection.ClearAllPools();
                File.WriteAllText(path, fullLegacyJson);
                File.WriteAllText(store.TransactionalStoragePath, "synthetic-corrupt-sqlite");
                store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            }
            store.ApplyPrivateDeckPersistenceStartup(enabled);
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            await action(store, client, admin.Token!, player.Token!, path);
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            DeleteRoot(root);
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string route, string token)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record StoredDeckFact(string DeckId, long Revision, string RowJson);

    private static Dictionary<string, StoredDeckFact> ReadAccountDeckFacts(string path, bool deleted)
    {
        var facts = new Dictionary<string, StoredDeckFact>(StringComparer.Ordinal);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.ChangeExtension(path, ".db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        // Include every durable account_decks field except the deliberate active -> deleted transition.
        command.CommandText = """
            SELECT account_id,name_key,name,deck_id,revision,payload_hash,
                alternate_art_selections_json,alternate_art_copies_json,bench_cards_json,updated_utc,
                publication_id,publication_version
            FROM account_decks WHERE is_deleted=$deleted ORDER BY account_id,name_key;
            """;
        command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            facts.Add(reader.GetString(0) + "\n" + reader.GetString(1),
                new(reader.GetString(3), reader.GetInt64(4), JsonSerializer.Serialize(values)));
        }
        return facts;
    }

    private static string DurableFingerprint(string path)
    {
        var rows = new StringBuilder(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.ChangeExtension(path, ".db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        foreach (var sql in new[]
        {
            "SELECT * FROM platform_state ORDER BY singleton_id;",
            "SELECT * FROM storage_meta ORDER BY key;",
            "SELECT * FROM deck_payloads ORDER BY payload_hash;",
            "SELECT * FROM account_decks ORDER BY account_id,name_key;",
            "SELECT * FROM published_decks ORDER BY publication_id;",
            "SELECT * FROM published_deck_versions ORDER BY publication_id,version;",
            "SELECT * FROM tournament_deck_refs ORDER BY tournament_id,account_id;",
            "SELECT * FROM admin_audit_events ORDER BY id;",
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Append(JsonSerializer.Serialize(values)).Append('\n');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rows.ToString())));
    }

    private static string FullJson(L12PlatformStore store)
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags privateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        var data = typeof(L12PlatformStore).GetProperty("_data", privateInstance)!.GetValue(store)!;
        var options = (JsonSerializerOptions)typeof(L12PlatformStore)
            .GetField("PlatformMigrationJsonOptions", privateStatic)!.GetValue(null)!;
        return JsonSerializer.Serialize(data, data.GetType(), options);
    }

    private static string FindProgramSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "服务端WebSocket", "Program.cs");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Program startup contract requires the source checkout.");
    }

    private static L12PresetDeckDefinition Deck(string name, string card) => new()
        { Name = name, MasterId = "M1", CardIds = [card], MoraleIds = ["R1"] };

    private static string TempRoot()
    {
        var root = Path.Combine(@"D:\GPT\Legion12\artifacts\f2-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
}
