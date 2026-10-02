using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformSaveConcurrencyTests
{
    [Fact]
    public void NonBusinessSaveAlsoRejectsStaleStateAndRetriesWithoutLosingAnotherAccount()
    {
        InStore((path, seed) =>
        {
            var stale = new L12PlatformStore(path);
            var writer = new L12PlatformStore(path);
            Assert.True(writer.Register("lateuser", "password-123").Success);
            var version = writer.StorageStatus();
            Assert.Throws<L12PlatformStorageConflictException>(() => stale.Login("seeduser", "wrong-password"));
            Assert.Equal(version.StorageRevision, stale.StorageStatus().StorageRevision);
            Assert.Equal(version.BusinessVersion, stale.StorageStatus().BusinessVersion);
            Assert.Contains(stale.Accounts(), account => account.Username == "lateuser");
            Assert.Equal("authentication_failed", stale.Login("seeduser", "wrong-password").Code);
            Assert.Equal(version.StorageRevision + 1, stale.StorageStatus().StorageRevision);
            Assert.Equal(version.BusinessVersion, stale.StorageStatus().BusinessVersion);
            Assert.Contains(new L12PlatformStore(path).Accounts(), account => account.Username == "lateuser");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdminMergedSaveKeepsConflictRefreshRatherThanRestoringItsStaleEntrySnapshot(bool refreshFails)
    {
        InStore((path, seed) =>
        {
            var stale = new L12PlatformStore(path);
            var writer = new L12PlatformStore(path);
            Assert.True(writer.Register("latestuser", "password-123").Success);
            if (refreshFails) stale.StorageFailureInjector = stage =>
            {
                if (stage == "before-conflict-refresh") throw new IOException("injected");
            };
            Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() =>
                stale.ExecuteAdminTransaction(() => stale.Register("rolledback", "password-123")));
            Assert.DoesNotContain(stale.Accounts(), account => account.Username == "rolledback");
            Assert.DoesNotContain(new L12PlatformStore(path).Accounts(), account => account.Username == "rolledback");
            if (!refreshFails)
            {
                Assert.Contains(stale.Accounts(), account => account.Username == "latestuser");
                Assert.True(stale.Register("retryuser", "password-123").Success);
            }
            else
            {
                Assert.Equal("unavailable", stale.StorageStatus().Mode);
                Assert.NotNull(stale.StorageStatus().Issue);
                Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.Register("retryuser", "password-123"));
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedRefreshDoesNotAcceptReadableMirrorOrEraseCommittedDecks(bool objectWrite, bool removeMirror)
    {
        InStore((path, seed) =>
        {
            var owner = seed.Accounts().Single(account => account.Username == "seeduser");
            var original = seed.CreateDeck(owner.Id, Deck("saved", "C1")).Deck!;
            Assert.NotNull(seed.PublishDeck(owner.Id, Deck("published", "C1"), null));
            var writer = new L12PlatformStore(path);
            var stale = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = objectWrite };
            Assert.True(writer.Register("latestuser", "password-123").Success);
            stale.StorageFailureInjector = stage =>
            {
                if (stage != "before-conflict-refresh") return;
                if (removeMirror) File.Delete(path);
                using var connection = new SqliteConnection($"Data Source={stale.TransactionalStoragePath}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE platform_state SET snapshot_sha256='invalid' WHERE singleton_id=1;";
                command.ExecuteNonQuery();
            };
            Assert.Throws<L12PlatformStorageRefreshException>(() =>
                stale.UpdateDeck(owner.Id, original.Id, original.Revision, Deck("mustnotpersist", "C2")));
            Assert.Equal("saved", Assert.Single(stale.Decks(owner.Id)).Name);
            Assert.Equal("published", Assert.Single(stale.PublishedDecks(owner.Id)).Deck.Name);
            Assert.Equal("unavailable", stale.StorageStatus().Mode);
            Assert.Contains("刷新失败", stale.StorageStatus().Issue);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.Register("noretry", "password-123"));
        });
    }

    [Fact]
    public void NoOpSeasonClaimMustNotPairLatestRevisionWithStaleDecks()
    {
        InStore((path, seed) =>
        {
            var owner = seed.Accounts().Single(account => account.Username == "seeduser");
            var original = seed.CreateDeck(owner.Id, Deck("before", "C1")).Deck!;
            var stale = new L12PlatformStore(path);
            var writer = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            Assert.True(writer.UpdateDeck(owner.Id, original.Id, 1, Deck("latest", "C2")).Success);
            Assert.Null(stale.TryClaimDueSeasonFinalization("test", DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
            Assert.Equal("latest", Assert.Single(stale.Decks(owner.Id)).Name);
            Assert.True(stale.Register("afterclaim", "password-123").Success);
            Assert.Equal("latest", Assert.Single(new L12PlatformStore(path).Decks(owner.Id)).Name);
        });
    }

    [Theory]
    [InlineData("before-mirror-serialize")]
    [InlineData("before-audit-append")]
    [InlineData("after-audit-append")]
    [InlineData("before-commit")]
    public void OrdinarySaveFaultKeepsDatabaseMirrorAndCompleteRollbackView(string fault)
    {
        InStore((path, store) =>
        {
            var owner = store.Accounts().Single(account => account.Username == "seeduser");
            store.CreateDeck(owner.Id, Deck("saved", "C1"));
            var before = store.StorageStatus();
            var mirror = SHA256.HashData(File.ReadAllBytes(path));
            store.StorageFailureInjector = stage => { if (stage == fault) throw new IOException("injected"); };
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Register("faultuser", "password-123"));
            store.StorageFailureInjector = null;
            Assert.Equal(before.StorageRevision, store.StorageStatus().StorageRevision);
            Assert.Equal(mirror, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal("saved", Assert.Single(store.Decks(owner.Id)).Name);
            Assert.DoesNotContain(new L12PlatformStore(path).Accounts(), account => account.Username == "faultuser");
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SimultaneousWritersHaveOneCommitAndExplicitRetryPreservesBothDecks(bool firstObject, bool secondObject)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var seed = new L12PlatformStore(path);
            var owner = seed.Register("raceuser", "password-123").Account!;
            var deck1 = seed.CreateDeck(owner.Id, Deck("first", "C1")).Deck!;
            var deck2 = seed.CreateDeck(owner.Id, Deck("second", "C2")).Deck!;
            var first = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = firstObject };
            var second = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = secondObject };
            using var ready = new Barrier(2);
            var tasks = new[]
            {
                Task.Run(() => { Assert.True(ready.SignalAndWait(TimeSpan.FromSeconds(10)));
                    return first.UpdateDeck(owner.Id, deck1.Id, 1, Deck("first updated", "C3")); }),
                Task.Run(() => { Assert.True(ready.SignalAndWait(TimeSpan.FromSeconds(10)));
                    return second.UpdateDeck(owner.Id, deck2.Id, 1, Deck("second updated", "C4")); }),
            };
            var results = await Task.WhenAll(tasks);
            Assert.Single(results.Where(result => result.Success));
            Assert.Single(results.Where(result => result.Status == "storage_conflict"));
            var loser = results[0].Success ? second : first;
            var target = results[0].Success ? deck2 : deck1;
            Assert.True(loser.UpdateDeck(owner.Id, target.Id, 1,
                Deck(results[0].Success ? "second updated" : "first updated", "retry")).Success);
            var restored = new L12PlatformStore(path).Decks(owner.Id);
            Assert.Contains(restored, deck => deck.Name == "first updated" && deck.Revision == 2);
            Assert.Contains(restored, deck => deck.Name == "second updated" && deck.Revision == 2);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void ConflictRefreshReadsSnapshotAndDecksFromOneGenerationDuringAnotherCommit()
    {
        InStore((path, seed) =>
        {
            var owner = seed.Accounts().Single(account => account.Username == "seeduser");
            var deck = seed.CreateDeck(owner.Id, Deck("before", "C1")).Deck!;
            var writer = new L12PlatformStore(path) { PrivateDeckObjectPersistenceEnabled = true };
            var stale = new L12PlatformStore(path);
            Assert.True(writer.UpdateDeck(owner.Id, deck.Id, 1, Deck("generation1", "C2")).Success);
            var version1 = writer.StorageStatus().StorageRevision;
            stale.StorageFailureInjector = stage =>
            {
                if (stage == "after-conflict-snapshot-read")
                    Assert.True(writer.UpdateDeck(owner.Id, deck.Id, 2, Deck("generation2", "C3")).Success);
            };
            Assert.Throws<L12PlatformStorageConflictException>(() => stale.Register("firsttry", "password-123"));
            stale.StorageFailureInjector = null;
            Assert.Equal(version1, stale.StorageStatus().StorageRevision);
            Assert.Equal("generation1", Assert.Single(stale.Decks(owner.Id)).Name);
            Assert.Throws<L12PlatformStorageConflictException>(() => stale.Register("secondtry", "password-123"));
            Assert.Equal("generation2", Assert.Single(stale.Decks(owner.Id)).Name);
            Assert.True(stale.Register("thirdtry", "password-123").Success);
            Assert.Equal("generation2", Assert.Single(new L12PlatformStore(path).Decks(owner.Id)).Name);
        });
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("update", false)]
    [InlineData("delete", false)]
    [InlineData("legacy-update", false)]
    [InlineData("legacy-delete", false)]
    [InlineData("create", true)]
    [InlineData("update", true)]
    [InlineData("delete", true)]
    [InlineData("legacy-update", true)]
    [InlineData("legacy-delete", true)]
    public async Task HttpDeckContractsReturnRetryableConflictOrUnavailableWithoutLeakingDetails(string operation, bool refreshFails)
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
            var stale = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards)
                { PrivateDeckObjectPersistenceEnabled = true };
            var login = stale.Register("tcasapi", "password-123");
            Assert.True(login.Success, login.Message);
            var preset = catalog.PresetDecks[0];
            var deck = stale.CreateDeck(login.Account!.Id, new L12PresetDeckDefinition
            {
                Name = "HTTP saved", MasterId = preset.MasterId, CardIds = preset.CardIds,
                MoraleIds = preset.MoraleIds, SpecialIds = preset.SpecialIds,
            }).Deck!;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, stale), recorder, stale, catalog);
            await server.StartAsync(0);
            var writer = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            Assert.True(writer.Register("lateruser", "password-123").Success);
            if (refreshFails) stale.StorageFailureInjector = stage =>
            {
                if (stage == "before-conflict-refresh") throw new IOException("private-path-and-secret");
            };
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var route = operation switch
            {
                "create" or "legacy-update" => "/api/decks",
                "legacy-delete" => "/api/decks/HTTP%20saved",
                "delete" => $"/api/decks/by-id/{deck.Id}?expectedRevision={deck.Revision}",
                _ => $"/api/decks/by-id/{deck.Id}",
            };
            var method = operation == "create" ? HttpMethod.Post
                : operation.Contains("delete") ? HttpMethod.Delete : HttpMethod.Put;
            using var request = new HttpRequestMessage(method, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            var submission = new { name = operation == "create" ? "HTTP new" : "HTTP saved",
                masterId = preset.MasterId, cardIds = preset.CardIds, moraleIds = preset.MoraleIds,
                specialIds = preset.SpecialIds, benchIds = Array.Empty<string>() };
            if (!operation.Contains("delete")) request.Content = operation == "update"
                ? JsonContent.Create(new { deck = submission, expectedRevision = deck.Revision })
                : JsonContent.Create(submission);
            using var response = await client.SendAsync(request);
            Assert.Equal(refreshFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Conflict, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.Contains(L12CorrelationIds.HeaderName));
            var body = await response.Content.ReadAsStringAsync();
            using var parsed = JsonDocument.Parse(body);
            Assert.Equal(refreshFails ? "storage_unavailable" : "storage_conflict",
                parsed.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(root, body);
            Assert.DoesNotContain("private-path-and-secret", body);
            Assert.Equal(deck.Revision, new L12PlatformStore(path).Decks(login.Account.Id)
                .Single(item => item.Id == deck.Id).Revision);
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static L12PresetDeckDefinition Deck(string name, string card) => new()
        { Name = name, MasterId = "M1", CardIds = [card], MoraleIds = ["R1"] };

    private static void InStore(Action<string, L12PlatformStore> action)
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var seed = new L12PlatformStore(path);
            Assert.True(seed.Register("seeduser", "password-123").Success);
            action(path, seed);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-save-cas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
