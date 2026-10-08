using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlanePhaseThreeStorageTests
{
    [Fact]
    public void LegacyJsonMigratesWithBackupIntegrityAndRecoveryRehearsal()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            const string legacy = """
                {
                  "Version": 7,
                  "Accounts": [],
                  "Sessions": [],
                  "Content": { "home.headline": "legacy-headline" },
                  "AdminAudit": []
                }
                """;
            File.WriteAllText(path, legacy);

            var store = new L12PlatformStore(path);
            var status = store.StorageStatus();

            Assert.Equal("sqlite", status.Mode);
            Assert.True(status.DatabaseIntegrityValid);
            Assert.True(status.SnapshotChecksumValid);
            Assert.True(status.FallbackMirrorHealthy);
            Assert.True(File.Exists(store.TransactionalStoragePath));
            Assert.Equal(legacy, File.ReadAllText(path + ".pre-sqlite.bak"));
            Assert.Equal("legacy-headline", store.GetContent("home.headline"));

            using (var connection = Open(store.TransactionalStoragePath))
            {
                Assert.Equal("ok", Scalar(connection, "PRAGMA quick_check;"));
                Assert.Equal("1", Scalar(connection, "SELECT COUNT(*) FROM platform_state;"));
                Assert.Equal("9", Scalar(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("9", Scalar(connection, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
                Assert.Equal("2", Scalar(connection, """
                    SELECT COUNT(*) FROM pragma_table_info('deck_payloads')
                    WHERE name IN ('payload_format','payload_json');
                    """));
                Assert.Equal("0", Scalar(connection, """
                    SELECT COUNT(*) FROM pragma_table_info('deck_payloads')
                    WHERE name IN ('main_cards_json','morale_cards_json','special_cards_json');
                    """));
            }

            var rehearsal = store.RehearseStorageRecovery();
            Assert.True(rehearsal.Success, rehearsal.Error);
            Assert.Equal(store.Version, rehearsal.BusinessVersion);
            Assert.DoesNotContain(Directory.EnumerateFiles(root), file => file.Contains(".rehearsal-"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SchemaMarkersUpgradeWithoutLosingOperationsConfiguration()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.OperationsConfig(admin);
            var changed = initial.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "schema-upgrade-preserved", null, null),
            };
            var preview = store.PreviewOperationsConfig(admin, changed, initial.Version,
                new L12AdminAuditContext("schema-upgrade-preview"));
            var applied = store.ApplyOperationsConfig(admin, preview.Normalized, initial.Version,
                "schema upgrade regression", new L12AdminAuditContext("schema-upgrade-apply"));
            Assert.Equal("schema-upgrade-preserved", applied.Current.Config.Maintenance.Message);

            using (var connection = OpenWritable(store.TransactionalStoragePath))
            {
                Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
            }

            var legacyPath = Path.Combine(root, "schema-seven", "platform.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            var legacyDatabase = Path.ChangeExtension(legacyPath, ".db");
            File.Copy(store.TransactionalStoragePath, legacyDatabase);
            L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(legacyDatabase);
            L12PlatformStore.RevertCompactDeckPayloadStorageForRehearsal(legacyDatabase);
            using (var legacy = Open(legacyDatabase))
            {
                Assert.Equal("7", Scalar(legacy, "SELECT value FROM storage_meta WHERE key='schema_version';"));
                Assert.Equal("3", Scalar(legacy, """
                    SELECT COUNT(*) FROM pragma_table_info('deck_payloads')
                    WHERE name IN ('main_cards_json','morale_cards_json','special_cards_json');
                    """));
                Assert.Equal("0", Scalar(legacy, """
                    SELECT COUNT(*) FROM pragma_table_info('deck_payloads')
                    WHERE name IN ('payload_format','payload_json');
                    """));
            }

            var reloaded = new L12PlatformStore(legacyPath);
            var reloadedAdmin = reloaded.Login("Admin", "L12master").Account!;
            Assert.Equal("schema-upgrade-preserved",
                reloaded.OperationsConfig(reloadedAdmin).Config.Maintenance.Message);
            using var upgraded = Open(reloaded.TransactionalStoragePath);
            Assert.Equal("9", Scalar(upgraded, "SELECT value FROM storage_meta WHERE key='schema_version';"));
            Assert.Equal("9", Scalar(upgraded, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal("2", Scalar(upgraded, """
                SELECT COUNT(*) FROM pragma_table_info('deck_payloads')
                WHERE name IN ('payload_format','payload_json');
                """));
            Assert.Equal("0", Scalar(upgraded, """
                SELECT COUNT(*) FROM pragma_table_info('deck_payloads')
                WHERE name IN ('main_cards_json','morale_cards_json','special_cards_json');
                """));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SqliteSnapshotAndCompatibilityMirrorSurviveRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var account = store.Register("tphase4e5f0", "password-123");
            var version = store.Version;

            var reloaded = new L12PlatformStore(path);

            Assert.Equal(version, reloaded.Version);
            Assert.NotNull(reloaded.AuthenticateToken(account.Token));
            Assert.Contains(reloaded.Accounts(), item => item.Id == account.Account!.Id);
            using var mirror = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(mirror.RootElement.TryGetProperty("Accounts", out _));
            Assert.Equal("sqlite", reloaded.StorageStatus().Mode);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AccountAudioPreferencesPersistAcrossDevicesAndRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var registered = store.Register("taudio2e2f5", "password-123").Account!;

            var saved = store.UpdateAudioPreferences(registered.Id,
                new L12AudioPreferencesView(false, 0.82, true, 0.47, "large", "fast", "off"));

            Assert.False(saved.MusicEnabled);
            Assert.Equal(0.82, saved.MusicVolume, 3);
            Assert.True(saved.SfxEnabled);
            Assert.Equal(0.47, saved.SfxVolume, 3);
            Assert.Equal("large", saved.CardSize);
            Assert.Equal("fast", saved.Animation);
            Assert.Equal("off", saved.MobileLayout);

            var reloaded = new L12PlatformStore(path);
            var fromAnotherLogin = reloaded.Login("taudio2e2f5", "password-123").Account!;
            Assert.Equal(saved, fromAnotherLogin.AudioPreferences);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedCommitRollsBackMemoryAndDurableSnapshot()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var version = store.Version;
            var before = store.GetContentEntry("home.headline");
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("simulated-partial-write");
            };

            var error = Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.SaveContentDraft(admin, "home.headline", "must-not-persist"));

            Assert.Contains("simulated-partial-write", error.Message);
            Assert.Equal(version, store.Version);
            Assert.Equal(before.DraftValue, store.GetContentEntry("home.headline").DraftValue);
            store.StorageFailureInjector = null;

            var reloaded = new L12PlatformStore(path);
            Assert.Equal(version, reloaded.Version);
            Assert.Equal(before.DraftValue, reloaded.GetContentEntry("home.headline").DraftValue);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptDatabaseFallsBackToValidatedJsonWithoutAcceptingWrites()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var account = store.Register("tfallbf8420", "password-123").Account!;
            var fullLegacyJson = FullJson(store);
            File.WriteAllText(path, fullLegacyJson);
            File.WriteAllText(store.TransactionalStoragePath, "not-a-sqlite-database");

            var fallback = new L12PlatformStore(path);

            Assert.Equal("json-fallback-readonly", fallback.StorageStatus().Mode);
            Assert.Contains(fallback.Accounts(), item => item.Id == account.Id);
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                fallback.Register("tnofal291e3", "password-456"));
            Assert.DoesNotContain(fallback.Accounts(), item => item.Username == "tnofal291e3");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IndependentAuditLedgerRetainsEventsBeyondCompatibilityWindow()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var audits = Enumerable.Range(0, 5001).Select(index => new
            {
                Id = $"audit-{index:D5}",
                ActorId = "legacy-actor",
                ActorName = "Legacy",
                Category = "security",
                Action = "legacy-event",
                Target = index.ToString(),
                CreatedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z").AddSeconds(index),
                Outcome = "succeeded",
            }).ToArray();
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Version = 11,
                BusinessVersion = 11,
                Accounts = Array.Empty<object>(),
                Sessions = Array.Empty<object>(),
                AdminAudit = audits,
            }));

            var store = new L12PlatformStore(path);

            Assert.Equal(5001, store.StorageStatus().RetainedAuditEvents);
            Assert.Equal(1000, store.AdminAudit("security", 1000).Count);
            using var connection = Open(store.TransactionalStoragePath);
            Assert.Equal("5001", Scalar(connection, "SELECT COUNT(*) FROM admin_audit_events;"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenWritable(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
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

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-control-plane-phase-three-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
