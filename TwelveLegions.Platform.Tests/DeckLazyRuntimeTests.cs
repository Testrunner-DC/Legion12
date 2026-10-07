using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeckLazyRuntimeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupAndCommittedWritesKeepOnlyCompactSharedBodies(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            for (var index = 0; index < 60; index++)
                Assert.True(store.CreateDeck(owner, Deck("lazy-" + index, "SHARED")).Success);
            Assert.NotNull(store.PublishDeck(owner, Deck("public", "SHARED"), null));
            AssertCompactRuntime(store, 60, 1);
            var restarted = new L12PlatformStore(path);
            AssertCompactRuntime(restarted, 60, 1);
            Assert.Equal(60, restarted.Decks(owner).Count);
            Assert.All(restarted.Decks(owner), deck => Assert.Equal(new[] { "SHARED", "SHARED" }, deck.CardIds));
            AssertCompactRuntime(restarted, 60, 1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedWriteHasSelfContainedCompactRollbackWithoutDatabaseReads(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            var saved = store.CreateDeck(owner, Deck("committed", "ORIGINAL")).Deck!;
            AssertCompactRuntime(store, 1, 0);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("synthetic-before-commit");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.UpdateDeck(owner, saved.Id, saved.Revision, Deck("uncommitted", "NEW")));
            store.StorageFailureInjector = null;
            // The returned state must remain readable even when the database can
            // no longer supply a body. Compact facts belong to the committed cache.
            SqliteConnection.ClearAllPools();
            var database = store.TransactionalStoragePath;
            var parked = database + ".test-unreadable";
            using var deny = new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.Move(database, parked);
            try
            {
                using var probe = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
                }.ToString());
                Assert.Throws<SqliteException>(() => probe.Open());
                AssertRestoredWithoutDatabase(store, saved, owner);
            }
            finally
            {
                if (File.Exists(parked)) File.Move(parked, database);
            }
        });
    }

    private static void AssertRestoredWithoutDatabase(L12PlatformStore store, L12AccountDeckView saved, string owner)
    {
            var restored = Assert.Single(store.Decks(owner));
            Assert.Equal(saved.Id, restored.Id);
            Assert.Equal(saved.Revision, restored.Revision);
            Assert.Equal("committed", restored.Name);
            Assert.Equal(new[] { "ORIGINAL", "ORIGINAL" }, restored.CardIds);
            AssertCompactRuntime(store, 1, 0);
    }

    [Fact]
    public void MetadataAndOtherAccountReadsDoNotExpandStoredBodies()
    {
        InStore((path, store, owner) =>
        {
            var created = store.CreateDeck(owner, Deck("owned", "ONE")).Deck!;
            var other = store.Register("lazyother", "password-123").Account!.Id;
            var calls = new List<string>();
            ObserveExpansions(store, calls);
            Assert.Empty(store.Decks(other));
            Assert.NotEmpty(store.Accounts());
            _ = store.StorageStatus();
            Assert.False(store.UpdateDeck(other, created.Id, created.Revision, Deck("wrong owner", "BAD")).Success);
            Assert.False(store.DeleteDeck(other, created.Id, created.Revision).Success);
            Assert.Empty(calls);
            Assert.Single(store.Decks(owner));
            Assert.Single(calls);
            AssertCompactRuntime(store, 1, 0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewerDatabaseCannotBeMixedIntoFailedConflictRefresh(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            var saved = store.CreateDeck(owner, Deck("old generation", "OLD")).Deck!;
            var newer = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = objectWrite };
            Assert.True(newer.UpdateDeck(owner, saved.Id, saved.Revision, Deck("new generation", "LATEST")).Success);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "after-conflict-snapshot-read") throw new IOException("synthetic-refresh-failure");
            };
            try
            {
                var result = store.UpdateDeck(owner, saved.Id, saved.Revision, Deck("uncommitted", "NO"));
                Assert.False(result.Success);
            }
            catch (L12PlatformStorageUnavailableException) { }
            store.StorageFailureInjector = null;
            var restored = Assert.Single(store.Decks(owner));
            Assert.Equal(saved.Id, restored.Id);
            Assert.Equal(saved.Revision, restored.Revision);
            Assert.Equal("old generation", restored.Name);
            Assert.Equal(new[] { "OLD", "OLD" }, restored.CardIds);
            AssertCompactRuntime(store, 1, 0);
            Assert.Equal("new generation", Assert.Single(newer.Decks(owner)).Name);
        });
    }

    [Fact]
    public void RetainedHistoryAndDeletedPayloadsStayCompactAndQueryable()
    {
        InStore((path, store, owner) =>
        {
            var saved = store.CreateDeck(owner, Deck("delete me", "RETAINED")).Deck!;
            Assert.True(store.DeleteDeck(owner, saved.Id, saved.Revision).Success);
            string? id = null;
            for (var index = 0; index < 25; index++)
                id = store.PublishDeck(owner, Deck("version-" + index, "HISTORY" + index), id)!.Id;
            var restarted = new L12PlatformStore(path);
            AssertCompactRuntime(restarted, 0, 1);
            Assert.Equal(25, restarted.PublicDeckDetails(id!)!.Versions.Count);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = restarted.TransactionalStoragePath, Mode = SqliteOpenMode.ReadOnly,
            }.ToString()))
            {
                connection.Open();
                using var count = connection.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM deck_payloads;";
                Assert.Equal(26L, Convert.ToInt64(count.ExecuteScalar()));
            }
            AssertCompactRuntime(restarted, 0, 1);
            Assert.True(restarted.RehearseStorageRecovery().Success);
        });
    }

    [Fact]
    public void LockedTournamentUsesSameCompactBodyAfterPrivateDeleteAndRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-c-r-tournament-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = false;
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, officialCards: catalog.Cards);
            var registration = store.Register("lazytourney", "password-123");
            Assert.True(registration.Success, registration.Message);
            var owner = registration.Account!;
            var preset = catalog.PresetDecks[0];
            var saved = store.CreateDeck(owner.Id, new L12PresetDeckDefinition
            {
                Name = "locked", MasterId = preset.MasterId, CardIds = [.. preset.CardIds],
                MoraleIds = [.. preset.MoraleIds], SpecialIds = [.. preset.SpecialIds],
            }).Deck!;
            var tournament = store.CreateTournament(owner,
                new L12TournamentCreatePayload("惰性锁牌验收", "swiss", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "storage", "after", "season",
                    string.Empty, 50, 5, RegistrationVisibility: "public", LateGraceMinutes: 5),
                new L12AdminAuditContext("lazy-tournament-create"), true);
            tournament = store.PreCheckInTournament(owner, tournament.Id,
                new L12TournamentPreCheckInPayload("ignored", string.Empty, saved.Id), tournament.Version,
                new L12AdminAuditContext("lazy-tournament-lock"), true);
            var before = Assert.Single(tournament.Participants).Deck!;
            Assert.True(store.DeleteDeck(owner.Id, saved.Id, saved.Revision).Success);
            var restarted = new L12PlatformStore(path);
            var expansions = new List<string>();
            ObserveExpansions(restarted, expansions);
            var otherRegistration = restarted.Register("lazyguest", "password-123");
            Assert.True(otherRegistration.Success, otherRegistration.Message);
            var stranger = otherRegistration.Account!;
            var hidden = restarted.Tournament(stranger, tournament.Id)!;
            Assert.All(hidden.Participants, item => Assert.Null(item.Deck));
            Assert.Empty(expansions);
            var after = Assert.Single(restarted.Tournament(owner, tournament.Id)!.Participants).Deck!;
            Assert.Equal(before.Hash, after.Hash);
            Assert.Equal(before.SubmittedAt, after.SubmittedAt);
            Assert.Equal(before.LockedAt, after.LockedAt);
            Assert.Equal(before.CardIds, after.CardIds);
            Assert.NotEmpty(expansions);
            var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(restarted)!;
            foreach (var row in Rows(data, "Tournaments"))
            foreach (var person in Rows(row, "Participants"))
            {
                var body = person.GetType().GetProperty("Deck")!.GetValue(person)!;
                Assert.Empty(((IEnumerable)body.GetType().GetProperty("CardIds")!.GetValue(body)!).Cast<object>());
            }
            Assert.True(restarted.RehearseStorageRecovery().Success);
            passed = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic C-R fixture retained: " + root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingAllRegionsAndBenchDoesNotReusePreviousBody(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            var saved = store.CreateDeck(owner, new L12PresetDeckDefinition
            {
                Name = "clearable", MasterId = "M1", CardIds = ["MAIN", "MAIN"],
                MoraleIds = ["MORALE"], SpecialIds = ["SPECIAL"], BenchIds = ["BENCH", "BENCH"],
            }).Deck!;
            Assert.NotEmpty(saved.CardIds);
            Assert.NotEmpty(saved.MoraleIds);
            Assert.NotEmpty(saved.SpecialIds);
            Assert.NotEmpty(saved.BenchIds!);
            var cleared = store.UpdateDeck(owner, saved.Id, saved.Revision, new L12PresetDeckDefinition
            {
                Name = saved.Name, MasterId = "M1", CardIds = [], MoraleIds = [], SpecialIds = [], BenchIds = [],
            });
            Assert.True(cleared.Success);
            foreach (var deck in new[] { cleared.Deck!, Assert.Single(new L12PlatformStore(path).Decks(owner)) })
            {
                Assert.Equal(saved.Id, deck.Id);
                Assert.Equal(saved.Revision + 1, deck.Revision);
                Assert.Empty(deck.CardIds); Assert.Empty(deck.MoraleIds); Assert.Empty(deck.SpecialIds);
                Assert.Empty(deck.BenchIds!);
            }
            AssertCompactRuntime(store, 1, 0);
            AssertPersistentSnapshotHasNoRuntimePayloads(store, path);
            var cache = (byte[])typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!.GetValue(store)!;
            Assert.Equal(cache[..32], System.Security.Cryptography.SHA256.HashData(cache[32..]));
            using var bytes = new MemoryStream(cache, 32, cache.Length - 32, writable: false);
            using var gzip = new System.IO.Compression.GZipStream(bytes, System.IO.Compression.CompressionMode.Decompress);
            using var rollback = System.Text.Json.JsonDocument.Parse(gzip);
            Assert.Single(rollback.RootElement.GetProperty("DeckPayloads").EnumerateObject());
            Assert.Single(rollback.RootElement.GetProperty("Decks").EnumerateArray());
        });
    }

    private static void AssertPersistentSnapshotHasNoRuntimePayloads(L12PlatformStore store, string mirrorPath)
    {
        static void Check(string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("DeckPayloads", out _));
            Assert.False(document.RootElement.TryGetProperty("Decks", out _));
            Assert.False(document.RootElement.TryGetProperty("PublishedDecks", out _));
        }
        Check(File.ReadAllText(mirrorPath));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;";
        Check((string)command.ExecuteScalar()!);
    }

    [Fact]
    public void ContradictoryExpandedBodyCannotReplaceCapturedCompactReference()
    {
        InStore((path, store, owner) =>
        {
            Assert.True(store.CreateDeck(owner, Deck("protected", "ORIGINAL")).Success);
            var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
            var row = Assert.Single(Rows(data, "Decks"));
            var cards = (List<string>)row.GetType().GetProperty("CardIds")!.GetValue(row)!;
            cards.Add("TAMPERED");
            var validate = typeof(L12PlatformStore).GetMethod("ValidateCompactRuntimeDeckDomain",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var error = Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [data]));
            Assert.IsType<InvalidDataException>(error.InnerException);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HugeValidCompactCountsRemainStoredButFailBeforeOnlineCopies(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            Assert.True(store.CreateDeck(owner, Deck("huge", "SMALL")).Success);
            _ = new L12PlatformStore(path); // Existing one-time full backup is prepared before injection.
            InjectValidCompactCounts(store.TransactionalStoragePath, owner, int.MaxValue);
            var loaded = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = objectWrite };
            var calls = new List<string>();
            ObserveExpansions(loaded, calls);
            var before = SqlFacts(loaded.TransactionalStoragePath);
            var rollback = RuntimeCache(loaded);
            var error = Assert.Throws<L12PlatformStorageUnavailableException>(() => loaded.Decks(owner));
            Assert.Contains("资源预算", error.Message);
            Assert.Empty(calls);
            AssertExportBudgetFails(loaded);
            Assert.Empty(calls);
            Assert.Equal(before, SqlFacts(loaded.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(loaded));
            AssertCompactRuntime(loaded, 1, 0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullExportCountsEverySharedConsumerBeforeCopying(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            for (var index = 0; index < 12; index++)
                Assert.True(store.CreateDeck(owner, Deck("aggregate-" + index, "SHARED")).Success);
            _ = new L12PlatformStore(path);
            InjectValidCompactCounts(store.TransactionalStoragePath, owner, 90_000);
            var loaded = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = objectWrite };
            var before = SqlFacts(loaded.TransactionalStoragePath);
            var rollback = RuntimeCache(loaded);
            var calls = new List<string>();
            ObserveExpansions(loaded, calls);
            AssertExportBudgetFails(loaded); // 12 consumers, not one deduplicated body: 1,080,000 copies.
            Assert.Throws<L12PlatformStorageUnavailableException>(() => loaded.Decks(owner));
            Assert.Empty(calls);
            Assert.Equal(before, SqlFacts(loaded.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(loaded));
            AssertCompactRuntime(loaded, 12, 0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateBenchIsBudgetedBeforeAnyRuleBodyArray(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            Assert.True(store.CreateDeck(owner, Deck("huge bench", "SMALL")).Success);
            _ = new L12PlatformStore(path);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.TransactionalStoragePath,
            }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE account_decks SET bench_cards_json=$bench WHERE account_id=$owner;";
                command.Parameters.AddWithValue("$owner", owner);
                command.Parameters.AddWithValue("$bench", System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new { CardId = "BENCH", Quantity = int.MaxValue },
                }));
                command.ExecuteNonQuery();
            }
            var loaded = new L12PlatformStore(path);
            var calls = new List<string>();
            ObserveExpansions(loaded, calls);
            var before = SqlFacts(loaded.TransactionalStoragePath);
            var rollback = RuntimeCache(loaded);
            var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(loaded)!;
            var row = Assert.Single(Rows(data, "Decks"));
            var view = typeof(L12PlatformStore).GetMethod("ToView", PrivateInstance, null, [row.GetType()], null)!;
            var wrapped = Assert.Throws<TargetInvocationException>(() => view.Invoke(loaded, [row]));
            Assert.IsType<L12PlatformStorageUnavailableException>(wrapped.InnerException);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => loaded.Decks(owner));
            AssertExportBudgetFails(loaded);
            Assert.Empty(calls);
            Assert.Equal(before, SqlFacts(loaded.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(loaded));
            AssertCompactRuntime(loaded, 1, 0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryAggregateBudgetRejectsBeforeExpandingAnyVersion(bool objectWrite)
    {
        InStore((path, store, owner) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            Assert.True(store.CreateDeck(owner, Deck("seed", "SHARED")).Success);
            string? publication = null;
            for (var index = 0; index < 12; index++)
                publication = store.PublishDeck(owner, Deck("history-" + index, "SHARED"), publication)!.Id;
            _ = new L12PlatformStore(path);
            InjectValidCompactCounts(store.TransactionalStoragePath, owner, 90_000, includePublic: true);
            var loaded = new L12PlatformStore(path);
            var calls = new List<string>();
            ObserveExpansions(loaded, calls);
            var before = SqlFacts(loaded.TransactionalStoragePath);
            var rollback = RuntimeCache(loaded);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => loaded.PublicDeckDetails(publication!));
            Assert.Empty(calls);
            Assert.Equal(before, SqlFacts(loaded.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(loaded));
            AssertCompactRuntime(loaded, 1, 1);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RoomPairValidatesSecondPlayerBeforeExpandingFirst(bool objectWrite, bool internalEntry)
    {
        InTournamentStore((store, owner, player, _) =>
        {
            store.PrivateDeckObjectPersistenceEnabled = objectWrite;
            var tournament = LockedTournament(store, owner, player, "room preflight", "after");
            tournament = store.StartTournament(owner, tournament.Id, tournament.Version,
                new L12AdminAuditContext("pair-start"), true);
            tournament = store.CheckInTournament(owner, tournament.Id, 1,
                new L12TournamentCheckInPayload(owner.Id, true), tournament.Version,
                new L12AdminAuditContext("pair-ready-a"), true);
            tournament = store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                new L12AdminAuditContext("pair-ready-b"), true);
            tournament = store.StartTournamentRound(owner, tournament.Id, 1, tournament.Version,
                new L12AdminAuditContext("pair-round"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            Assert.NotNull(store.TournamentRoomAssignment(owner.Id, tournament.Id, match.Id, false));
            var second = RuntimeTournamentDeck(store, tournament.Id, match.PlayerBAccountId!);
            ReplaceRuntimeCompactBody(store, second, 100_001);
            var calls = new List<string>();
            ObserveExpansions(store, calls);
            var before = SqlFacts(store.TransactionalStoragePath);
            var rollback = RuntimeCache(store);
            var runtime = RuntimeFacts(store);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => internalEntry
                ? store.TournamentRoomAssignmentByRoom(match.RoomCode)
                : store.TournamentRoomAssignment(owner.Id, tournament.Id, match.Id, false));
            Assert.Empty(calls);
            Assert.Equal(runtime, RuntimeFacts(store));
            Assert.Equal(before, SqlFacts(store.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(store));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyCodePreviewNeverAddsUncommittedFacts(bool preCheckIn)
    {
        InStore((path, store, ownerId) =>
        {
            var owner = store.Account(ownerId)!;
            const string code = "L12D2-B2N7M-ZM2AW-YRPWG-MNDZS-23KMT-KP73X-24KMZ-E6PWG-F8B33-38MT7-ERQFT-V";
            var tournament = preCheckIn ? store.CreateTournament(owner,
                new L12TournamentCreatePayload("legacy preview", "swiss", "public", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "preview", "after", "season",
                    string.Empty, 50, 5), new L12AdminAuditContext("legacy-preview-create"), true) : null;
            var before = SqlFacts(store.TransactionalStoragePath);
            var rollback = RuntimeCache(store);
            var runtime = RuntimeFacts(store);
            if (preCheckIn)
                Assert.NotNull(store.PreCheckInTournament(owner, tournament!.Id,
                    new L12TournamentPreCheckInPayload("unique preview", code), tournament.Version,
                    new L12AdminAuditContext("legacy-precheck-preview"), false));
            else
                Assert.Single(store.ImportLegacyTournaments(owner,
                    new L12TournamentLegacyImportPayload([LegacyInput("pure-preview", owner.Username, code)]),
                    new L12AdminAuditContext("legacy-import-preview"), false).Tournaments);
            Assert.Equal(runtime, RuntimeFacts(store));
            Assert.Equal(before, SqlFacts(store.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(store));
        });
    }

    [Theory]
    [InlineData("list")]
    [InlineData("preview")]
    [InlineData("apply")]
    public void WholeTournamentRequestBudgetsAllVisibleRowsBeforeAnyExpansion(string entry)
    {
        InTournamentStore((store, owner, player, _) =>
        {
            var rows = new List<L12TournamentView>();
            var inputs = new List<L12LegacyTournamentInput>();
            for (var index = 0; index < 12; index++)
            {
                var row = LockedTournament(store, owner, player, "whole request " + index, "after");
                rows.Add(row);
                var source = "budget-" + index;
                var runtime = RuntimeTournamentRow(store, row.Id);
                runtime.GetType().GetProperty("LegacySourceId")!.SetValue(runtime, source);
                inputs.Add(LegacyInput(source, owner.Username));
                ReplaceRuntimeCompactBody(store, RuntimeTournamentDeck(store, row.Id, owner.Id), 90_000);
            }
            var outsiderRegistration = store.Register("reqvisitor", "password-123");
            Assert.True(outsiderRegistration.Success, outsiderRegistration.Message);
            var outsider = outsiderRegistration.Account!;
            var calls = new List<string>();
            ObserveExpansions(store, calls);
            var visible = store.Tournaments(outsider);
            Assert.Equal(12, visible.Items.Count);
            Assert.All(visible.Items, row => Assert.All(row.Participants, person => Assert.Null(person.Deck)));
            Assert.Empty(calls);
            Assert.Equal(12, store.Tournaments(player).Items.Count);
            Assert.Equal(12, calls.Count);
            calls.Clear();
            var before = SqlFacts(store.TransactionalStoragePath);
            var rollback = RuntimeCache(store);
            var runtimeBefore = RuntimeFacts(store);
            if (entry == "list")
                Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Tournaments(owner));
            else
            {
                var payload = new L12TournamentLegacyImportPayload(inputs);
                if (entry == "apply")
                {
                    var hash = (string)typeof(L12PlatformStore).GetMethod("LegacyPreviewHash",
                        BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [inputs])!;
                    payload = payload with { PreviewHash = hash };
                }
                Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ImportLegacyTournaments(owner,
                    payload, new L12AdminAuditContext("whole-request-" + entry), entry == "apply"));
            }
            Assert.Empty(calls);
            Assert.Equal(runtimeBefore, RuntimeFacts(store));
            Assert.Equal(before, SqlFacts(store.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(store));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedLaterLegacyPreviewCannotLeakEarlierUncommittedFacts(bool malformed)
    {
        InTournamentStore((store, owner, player, _) =>
        {
            var existing = LockedTournament(store, owner, player, "failed preview", "after");
            var row = RuntimeTournamentRow(store, existing.Id);
            row.GetType().GetProperty("LegacySourceId")!.SetValue(row, "existing-preview");
            var deck = RuntimeTournamentDeck(store, existing.Id, owner.Id);
            ReplaceRuntimeCompactBody(store, deck, 100_001);
            if (malformed)
            {
                var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
                var facts = (IDictionary)data.GetType().GetProperty("DeckPayloads")!.GetValue(data)!;
                var key = (string)deck.GetType().GetProperty("PayloadHash")!.GetValue(deck)!;
                var fact = facts[key]!;
                facts[key] = Activator.CreateInstance(fact.GetType(),
                    [(string)deck.GetType().GetProperty("MasterId")!.GetValue(deck)!, 99,
                        (string)fact.GetType().GetProperty("PayloadJson")!.GetValue(fact)!])!;
            }
            const string code = "L12D2-B2N7M-ZM2AW-YRPWG-MNDZS-23KMT-KP73X-24KMZ-E6PWG-F8B33-38MT7-ERQFT-V";
            var inputs = new[] { LegacyInput("new-first-preview", owner.Username, code),
                LegacyInput("existing-preview", owner.Username) };
            var calls = new List<string>();
            ObserveExpansions(store, calls);
            var before = SqlFacts(store.TransactionalStoragePath);
            var rollback = RuntimeCache(store);
            var runtime = RuntimeFacts(store);
            if (malformed)
                Assert.Throws<InvalidDataException>(() => store.ImportLegacyTournaments(owner,
                    new L12TournamentLegacyImportPayload(inputs), new L12AdminAuditContext("failed-later-preview"), false));
            else
                Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ImportLegacyTournaments(owner,
                    new L12TournamentLegacyImportPayload(inputs), new L12AdminAuditContext("failed-later-preview"), false));
            Assert.Empty(calls);
            Assert.Equal(runtime, RuntimeFacts(store));
            Assert.Equal(before, SqlFacts(store.TransactionalStoragePath));
            Assert.Same(rollback, RuntimeCache(store));
        });
    }

    private static object RuntimeTournamentRow(L12PlatformStore store, string tournamentId)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        return Rows(data, "Tournaments").Single(row =>
            (string)row.GetType().GetProperty("Id")!.GetValue(row)! == tournamentId);
    }

    private static L12LegacyTournamentInput LegacyInput(string id, string username, string? code = null)
        => new(id, "OLD" + id, "legacy " + id, "ignored", [], "registration", "swiss", "public", 16,
            null, "S01/S02", "synthetic", "after", "season", string.Empty, 50, 5,
            [new L12LegacyTournamentParticipantInput(username, "legacy deck", code ?? string.Empty)],
            [], null, null, null);

    private static string RuntimeFacts(L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Facts = data.GetType().GetProperty("DeckPayloads")!.GetValue(data),
            Tournaments = data.GetType().GetProperty("Tournaments")!.GetValue(data),
        });
    }

    private static object RuntimeTournamentDeck(L12PlatformStore store, string tournamentId, string accountId)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        var tournament = Rows(data, "Tournaments").Single(row =>
            (string)row.GetType().GetProperty("Id")!.GetValue(row)! == tournamentId);
        var participant = Rows(tournament, "Participants").Single(row =>
            (string)row.GetType().GetProperty("AccountId")!.GetValue(row)! == accountId);
        return participant.GetType().GetProperty("Deck")!.GetValue(participant)!;
    }

    private static void ReplaceRuntimeCompactBody(L12PlatformStore store, object row, int quantity)
    {
        var master = (string)row.GetType().GetProperty("MasterId")!.GetValue(row)!;
        var main = new[] { new { CardId = "HUGE", Quantity = quantity } };
        var morale = new[] { new { CardId = "MORALE", Quantity = 1 } };
        var empty = main.Take(0).ToArray();
        var canonical = System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = 1, master, main, morale, special = empty,
        });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var code = System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            new object[] { new object[] { "HUGE", quantity } },
            new object[] { new object[] { "MORALE", 1 } }, Array.Empty<object>(),
        });
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        var facts = (IDictionary)data.GetType().GetProperty("DeckPayloads")!.GetValue(data)!;
        var type = facts.Values.Cast<object>().First().GetType();
        facts[hash] = Activator.CreateInstance(type, [master, 1, code])!;
        row.GetType().GetProperty("PayloadHash")!.SetValue(row, hash);
    }

    private static L12TournamentView LockedTournament(L12PlatformStore store, L12AccountView owner,
        L12AccountView player, string name, string visibility)
    {
        var tournament = store.CreateTournament(owner,
            new L12TournamentCreatePayload(name, "single", "public", 8, DateTimeOffset.UtcNow.AddHours(1),
                "S01/S02", "synthetic", visibility, "season", string.Empty, 50, 5,
                RegistrationVisibility: "public", LateGraceMinutes: 5),
            new L12AdminAuditContext("create-" + name), true);
        tournament = store.RegisterTournament(player, tournament.Id, new L12TournamentRegistrationPayload(),
            tournament.Version, new L12AdminAuditContext("register-" + name), true);
        foreach (var actor in new[] { owner, player })
        {
            var deck = Assert.Single(store.Decks(actor.Id));
            tournament = store.PreCheckInTournament(actor, tournament.Id,
                new L12TournamentPreCheckInPayload("ignored", string.Empty, deck.Id), tournament.Version,
                new L12AdminAuditContext("lock-" + name + actor.Id), true);
        }
        return tournament;
    }

    private static void InTournamentStore(Action<L12PlatformStore, L12AccountView, L12AccountView, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-c-r-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = false;
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, officialCards: catalog.Cards);
            var a = store.Register("requestorg", "password-123");
            var b = store.Register("requestp", "password-123");
            Assert.True(a.Success, a.Message); Assert.True(b.Success, b.Message);
            var owner = store.Account(a.Account!.Id)!;
            var player = b.Account!;
            foreach (var actor in new[] { owner, player })
            {
                var preset = catalog.PresetDecks[0];
                Assert.True(store.CreateDeck(actor.Id, new L12PresetDeckDefinition
                {
                    Name = "request deck", MasterId = preset.MasterId, CardIds = [.. preset.CardIds],
                    MoraleIds = [.. preset.MoraleIds], SpecialIds = [.. preset.SpecialIds],
                }).Success);
            }
            action(store, owner, player, path);
            passed = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic C-R request fixture retained: " + root);
        }
    }

    [Fact]
    public void SuccessfulFullBackupExpandsOnlyTheExportAndLeavesCommittedRuntimeCompact()
    {
        InStore((path, store, owner) =>
        {
            var body = new L12PresetDeckDefinition
            {
                Name = "backup-private", MasterId = "M1", CardIds = ["MAIN", "MAIN"],
                MoraleIds = ["MORALE"], SpecialIds = ["SPECIAL"], BenchIds = ["BENCH", "BENCH"],
            };
            Assert.True(store.CreateDeck(owner, body).Success);
            Assert.NotNull(store.PublishDeck(owner, body, null));
            var beforeSql = SqlFacts(store.TransactionalStoragePath);
            var beforeCache = RuntimeCache(store);
            var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
            var export = typeof(L12PlatformStore).GetMethod("SerializeFullDeckDomainBackup",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var json = Assert.IsType<string>(export.Invoke(null, [data]));
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var privateDeck = Assert.Single(document.RootElement.GetProperty("Decks").EnumerateArray());
            Assert.Equal(new[] { "MAIN", "MAIN" }, privateDeck.GetProperty("CardIds")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
            Assert.Equal(new[] { "BENCH", "BENCH" }, privateDeck.GetProperty("BenchIds")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
            Assert.Single(document.RootElement.GetProperty("PublishedDecks").EnumerateArray());
            Assert.Empty(document.RootElement.GetProperty("DeckPayloads").EnumerateObject());
            AssertCompactRuntime(store, 1, 1);
            Assert.Empty(((IEnumerable)Assert.Single(Rows(data, "Decks")).GetType()
                .GetProperty("BenchIds")!.GetValue(Assert.Single(Rows(data, "Decks")))!).Cast<object>());
            Assert.Equal(beforeSql, SqlFacts(store.TransactionalStoragePath));
            Assert.Same(beforeCache, RuntimeCache(store));
            AssertPersistentSnapshotHasNoRuntimePayloads(store, path);
        });
    }

    private static void AssertExportBudgetFails(L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        var export = typeof(L12PlatformStore).GetMethod("SerializeFullDeckDomainBackup",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var wrapped = Assert.Throws<TargetInvocationException>(() => export.Invoke(null, [data]));
        var failure = Assert.IsType<L12PlatformStorageUnavailableException>(wrapped.InnerException);
        Assert.Contains("资源预算", failure.Message);
    }

    private static byte[] RuntimeCache(L12PlatformStore store) =>
        (byte[])typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!.GetValue(store)!;

    private static void InjectValidCompactCounts(string database, string owner, int quantity, bool includePublic = false)
    {
        var main = new[] { new { CardId = "HUGE", Quantity = quantity } };
        var empty = main.Take(0).ToArray();
        var canonical = System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = 1, master = "M1", main, morale = empty, special = empty,
        });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var code = System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            new object[] { new object[] { "HUGE", quantity } }, Array.Empty<object>(), Array.Empty<object>(),
        });
        using var connection = new SqliteConnection(database is not null
            ? new SqliteConnectionStringBuilder { DataSource = database }.ToString() : throw new ArgumentNullException(nameof(database)));
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO deck_payloads(payload_hash,master_id,payload_format,payload_json,created_utc)
            VALUES($hash,'M1',1,$payload,'2026-10-07T00:00:00Z');
            UPDATE account_decks SET payload_hash=$hash WHERE account_id=$owner AND is_deleted=0;
            """;
        if (includePublic) insert.CommandText += """
            UPDATE published_decks SET current_payload_hash=$hash WHERE owner_id=$owner;
            UPDATE published_deck_versions SET payload_hash=$hash
            WHERE publication_id IN (SELECT publication_id FROM published_decks WHERE owner_id=$owner);
            """;
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$payload", code);
        insert.Parameters.AddWithValue("$owner", owner);
        insert.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string SqlFacts(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        var rows = new List<object[]>();
        foreach (var table in new[] { "deck_payloads", "account_decks", "published_decks", "published_deck_versions", "tournament_deck_refs", "platform_state", "storage_meta" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM " + table + " ORDER BY 1,2;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount + 1];
                values[0] = table;
                for (var index = 0; index < reader.FieldCount; index++)
                    values[index + 1] = reader.IsDBNull(index) ? "NULL" : reader.GetValue(index);
                rows.Add(values);
            }
        }
        return System.Text.Json.JsonSerializer.Serialize(rows);
    }

    private static void AssertCompactRuntime(L12PlatformStore store, int privateCount, int publicCount, int payloadCount = 1)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        var privateRows = Rows(data, "Decks");
        var publicRows = Rows(data, "PublishedDecks");
        Assert.Equal(privateCount, privateRows.Length);
        Assert.Equal(publicCount, publicRows.Length);
        foreach (var row in privateRows.Concat(publicRows))
        {
            foreach (var field in new[] { "CardIds", "MoraleIds", "SpecialIds" })
                Assert.Empty(((IEnumerable)row.GetType().GetProperty(field)!.GetValue(row)!).Cast<object>());
            Assert.False(string.IsNullOrWhiteSpace((string?)row.GetType().GetProperty("PayloadHash")!.GetValue(row)));
        }
        var payloads = (IDictionary)data.GetType().GetProperty("DeckPayloads")!.GetValue(data)!;
        Assert.Equal(payloadCount, payloads.Count);
    }

    private static void ObserveExpansions(L12PlatformStore store, List<string> calls)
        => typeof(L12PlatformStore).GetProperty("DeckPayloadExpansionObserver", PrivateInstance)!
            .SetValue(store, (Action<string>)calls.Add);

    private static object[] Rows(object data, string name) =>
        ((IEnumerable)data.GetType().GetProperty(name)!.GetValue(data)!).Cast<object>().ToArray();

    private static L12PresetDeckDefinition Deck(string name, string card) => new()
    {
        Name = name, MasterId = "M1", CardIds = [card, card], MoraleIds = ["MORALE"],
    };

    private static void InStore(Action<string, L12PlatformStore, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-c-r-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = false;
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var registration = store.Register("lazyruntime", "password-123");
            Assert.True(registration.Success, registration.Message);
            action(path, store, registration.Account!.Id);
            passed = true;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic C-R fixture retained: " + root);
        }
    }
}
