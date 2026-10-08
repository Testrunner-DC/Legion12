using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedBroadcastObjectStorageTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    [Fact]
    public void ClaimDoesNotRewritePlatformSnapshotRevisionDecksOrMirror()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var before = PlatformFacts(path, store, viewerId);

            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));

            Assert.Equal("broadcast-object-live", claim.Broadcast.MatchId);
            Assert.Equal(before, PlatformFacts(path, store, viewerId));
        });
    }

    [Fact]
    public void CompleteDoesNotRewritePlatformSnapshotRevisionDecksOrMirror()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var before = PlatformFacts(path, store, viewerId);

            Assert.True(store.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));

            Assert.Equal(before, PlatformFacts(path, store, viewerId));
        });
    }

    [Fact]
    public void ClaimProducesAReadableNonEmptyWalGenerationWhileSnapshotReaderIsFrozen()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            using var frozen = new SqliteConnection(
                $"Data Source={store.TransactionalStoragePath};Mode=ReadOnly;Pooling=False");
            frozen.Open();
            using var frozenTransaction = frozen.BeginTransaction(deferred: true);
            using (var establish = frozen.CreateCommand())
            {
                establish.Transaction = frozenTransaction;
                establish.CommandText = """
                    SELECT value FROM storage_meta
                    WHERE key='ranked_broadcast_generation';
                    """;
                Assert.NotNull(establish.ExecuteScalar());
            }

            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var walPath = store.TransactionalStoragePath + "-wal";
            Assert.True(File.Exists(walPath));
            Assert.True(new FileInfo(walPath).Length > 32);
            using var latest = new SqliteConnection(
                $"Data Source={store.TransactionalStoragePath};Mode=ReadOnly;Pooling=False");
            latest.Open();
            using var verify = latest.CreateCommand();
            verify.CommandText = $"""
                SELECT COUNT(*) FROM ranked_broadcast_deliveries
                WHERE account_id='{viewerId}' AND broadcast_id='{claim.Broadcast.Id}'
                  AND row_generation=(SELECT CAST(value AS INTEGER) FROM storage_meta
                                      WHERE key='ranked_broadcast_generation');
                """;
            Assert.Equal(1L, Convert.ToInt64(verify.ExecuteScalar()));
            frozenTransaction.Rollback();
        });
    }

    [Fact]
    public async Task SixteenIndependentStoresReturnOneClaimWithoutConflicts()
    {
        await InLiveBroadcastAsync(async (path, _, viewerId, subscribedAt) =>
        {
            var stores = Enumerable.Range(0, 16).Select(_ => new L12PlatformStore(path)).ToArray();
            using var start = new ManualResetEventSlim(false);
            var attempts = stores.Select(store => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    return (Claim: store.ClaimRankedBroadcast(viewerId, subscribedAt), Error: (Exception?)null);
                }
                catch (Exception error)
                {
                    return (Claim: (L12RankedBroadcastClaimView?)null, Error: error);
                }
            })).ToArray();
            start.Set();
            var results = await Task.WhenAll(attempts);

            Assert.Empty(results.Where(result => result.Error is not null));
            var claim = Assert.Single(results.Where(result => result.Claim is not null)).Claim!;
            Assert.Equal("broadcast-object-live", claim.Broadcast.MatchId);
            Assert.Null(new L12PlatformStore(path).ClaimRankedBroadcast(viewerId, subscribedAt));
        });
    }

    [Fact]
    public void UnconfirmedClaimIsNotReissuedAndOriginalTokenCompletesAfterFortyFiveSeconds()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var claimedAt = DateTimeOffset.UtcNow;
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcastAt(viewerId, subscribedAt, claimedAt));
            Assert.Equal(claimedAt.AddSeconds(45), claim.LeaseExpiresAt);

            var restarted = new L12PlatformStore(path);
            Assert.Null(restarted.ClaimRankedBroadcastAt(
                viewerId, subscribedAt, claimedAt.AddSeconds(46)));
            Assert.True(restarted.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(restarted.ClaimRankedBroadcastAt(
                viewerId, subscribedAt, claimedAt.AddMinutes(2)));
        });
    }

    [Fact]
    public void WrongAndRepeatedCompleteDoNotAdvanceGeneration()
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var claimedGeneration = BroadcastGeneration(store);

            Assert.False(store.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, "wrong-token"));
            Assert.Equal(claimedGeneration, BroadcastGeneration(store));

            Assert.True(store.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            var completedGeneration = BroadcastGeneration(store);
            Assert.Equal(claimedGeneration + 1, completedGeneration);

            Assert.True(store.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Equal(completedGeneration, BroadcastGeneration(store));
        });
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("delete")]
    public void DisabledOrDeletedAccountCannotCompleteAnExistingClaim(string operation)
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var viewerSession = store.Login("trobjviewer", "password-123");
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var claimedGeneration = BroadcastGeneration(store);
            var writer = new L12PlatformStore(path);
            var admin = writer.Login("Admin", "L12master").Account!;
            var context = new L12AdminAuditContext("ranked-broadcast-account-invalidated");
            if (operation == "disable")
                writer.SetAccountDisabled(admin, viewerId, true, "ranked broadcast guard", context, true);
            else
                writer.DeleteAccountPersonalData(admin, viewerId, "ranked broadcast guard", context, true);

            Assert.Null(new L12PlatformStore(path).AuthenticateTokenSession(viewerSession.Token));
            Assert.False(store.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Equal(claimedGeneration, BroadcastGeneration(store));
        });
    }

    [Fact]
    public void FailureBeforeCompleteCommitLeavesClaimPendingAndGenerationUnchanged()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var claimedGeneration = BroadcastGeneration(store);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-ranked-broadcast-complete-commit")
                    throw new InvalidOperationException("injected-before-ranked-broadcast-complete-commit");
            };
            try
            {
                var error = Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                    store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
                Assert.Contains("injected-before-ranked-broadcast-complete-commit",
                    error.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                store.StorageFailureInjector = null;
            }

            Assert.Equal(claimedGeneration, BroadcastGeneration(store));
            var restarted = new L12PlatformStore(path);
            Assert.True(restarted.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Equal(claimedGeneration + 1, BroadcastGeneration(restarted));
        });
    }

    [Fact]
    public async Task SixteenIndependentStoresCompleteOneTokenWithOneGenerationAdvance()
    {
        await InLiveBroadcastAsync(async (path, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var claimedGeneration = BroadcastGeneration(store);
            var stores = Enumerable.Range(0, 16).Select(_ => new L12PlatformStore(path)).ToArray();
            using var start = new ManualResetEventSlim(false);
            var attempts = stores.Select(candidate => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    return (Completed: candidate.CompleteRankedBroadcast(
                        viewerId, claim.Broadcast.Id, claim.ClaimToken), Error: (Exception?)null);
                }
                catch (Exception error)
                {
                    return (Completed: false, Error: error);
                }
            })).ToArray();
            start.Set();
            var results = await Task.WhenAll(attempts);

            Assert.Empty(results.Where(result => result.Error is not null));
            Assert.All(results, result => Assert.True(result.Completed));
            Assert.Equal(claimedGeneration + 1, BroadcastGeneration(new L12PlatformStore(path)));
        });
    }

    [Fact]
    public void FailureBeforeClaimCommitLeavesNoDeliveryOrGenerationChange()
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var before = BroadcastGeneration(store);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-ranked-broadcast-commit")
                    throw new InvalidOperationException("injected-before-ranked-broadcast-commit");
            };
            try
            {
                var error = Assert.Throws<L12PlatformStorageUnavailableException>(
                    () => store.ClaimRankedBroadcast(viewerId, subscribedAt));
                Assert.Contains("injected-before-ranked-broadcast-commit",
                    error.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                store.StorageFailureInjector = null;
            }

            Assert.Equal(before, BroadcastGeneration(store));
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.Equal("broadcast-object-live", claim.Broadcast.MatchId);
            Assert.Equal(before + 1, BroadcastGeneration(store));
        });
    }

    [Fact]
    public void FailedAndStaleFullSavesCannotEraseCommittedClaim()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var stale = new L12PlatformStore(path);
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var claimedGeneration = BroadcastGeneration(store);

            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit")
                    throw new InvalidOperationException("injected-unrelated-save-failure");
            };
            try
            {
                var error = Assert.Throws<L12PlatformStorageUnavailableException>(
                    () => store.Register("tobjsfail1", "password-123"));
                Assert.Contains("injected-unrelated-save-failure",
                    error.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                store.StorageFailureInjector = null;
            }
            Assert.Equal(claimedGeneration, BroadcastGeneration(store));

            Assert.True(stale.Register("tobjstale1", "password-123").Success);
            Assert.Equal(claimedGeneration, BroadcastGeneration(stale));
            var restarted = new L12PlatformStore(path);
            Assert.True(restarted.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
        });
    }

    [Fact]
    public void HigherVersionOldFullMirrorCannotEraseSqlClaimOrCompletionOnRestart()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var oldFullMirror = JsonNode.Parse(FullMirror(store))!.AsObject();
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            var committedGeneration = BroadcastGeneration(store);
            var promotedVersion = store.Version + 100;
            oldFullMirror["Version"] = promotedVersion;
            oldFullMirror["BusinessVersion"] = promotedVersion;
            File.WriteAllText(path, oldFullMirror.ToJsonString());

            var restarted = new L12PlatformStore(path);

            Assert.Equal(committedGeneration, BroadcastGeneration(restarted));
            Assert.True(restarted.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Equal(committedGeneration, BroadcastGeneration(restarted));
            using var connection = new SqliteConnection(
                $"Data Source={restarted.TransactionalStoragePath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT COUNT(*) FROM ranked_broadcast_deliveries
                WHERE account_id='{viewerId}' AND broadcast_id='{claim.Broadcast.Id}'
                  AND claim_token='{claim.ClaimToken}' AND completed_utc IS NOT NULL;
                """;
            Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
        });
    }

    [Fact]
    public void StaleSeasonTransactionCannotOverwriteNewerCommittedClaim()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var admin = store.Login("Admin", "L12master").Account!;
            var operations = store.OperationsConfig(admin);
            var endsAt = DateTimeOffset.UtcNow.AddMinutes(1);
            store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = endsAt },
            }, operations.Version, "ranked broadcast stale season guard",
                new L12AdminAuditContext("ranked-broadcast-stale-season"));
            var stale = new L12PlatformStore(path);

            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var claimedGeneration = BroadcastGeneration(store);
            Assert.NotNull(stale.TryClaimDueSeasonFinalization(
                "ranked-broadcast-worker", endsAt, TimeSpan.FromMinutes(1)));
            Assert.Equal(claimedGeneration, BroadcastGeneration(stale));

            var restarted = new L12PlatformStore(path);
            Assert.True(restarted.CompleteRankedBroadcast(
                viewerId, claim.Broadcast.Id, claim.ClaimToken));
        });
    }

    [Fact]
    public void RetentionDefinitionDeletionStaleFullSaveAndSeasonPreserveRecentCompletedDelivery()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var oldest = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var first = store.Accounts().Single(row => row.Username == "trobjfirst");
            var second = store.Accounts().Single(row => row.Username == "trobjsecond");
            var index = 0;
            while (BroadcastDefinitionCount(store) < 295)
                store.SettleRankedMatch($"broadcast-object-retention-prefill-{index++}",
                    first.Id, second.Id, 0);

            var admin = store.Login("Admin", "L12master").Account!;
            var operations = store.OperationsConfig(admin);
            var endsAt = DateTimeOffset.UtcNow.AddMinutes(1);
            store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = endsAt },
            }, operations.Version, "ranked broadcast retention interleave",
                new L12AdminAuditContext("ranked-broadcast-retention-season"));
            var protectedSubscribedAt = DateTimeOffset.UtcNow;
            var protectedResult = store.SettleRankedMatch(
                "broadcast-object-retention-protected", first.Id, second.Id, 0);
            Assert.NotEmpty(protectedResult.Broadcasts);
            var staleFull = new L12PlatformStore(path);
            var staleSeason = new L12PlatformStore(path);
            var protectedClaim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, protectedSubscribedAt));
            Assert.Contains(protectedResult.Broadcasts,
                row => string.Equals(row.Id, protectedClaim.Broadcast.Id,
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(store.CompleteRankedBroadcast(viewerId,
                protectedClaim.Broadcast.Id, protectedClaim.ClaimToken));

            Assert.True(staleFull.Register("tobjretfull", "password-123").Success);
            var writer = new L12PlatformStore(path);
            var writerAdmin = writer.Login("Admin", "L12master").Account!;
            L12RankedBroadcastView? deleted = null;
            for (var attempt = 0; attempt < 10 && deleted is null; attempt++)
            {
                var result = writer.SettleRankedMatch(
                    $"broadcast-object-retention-delete-{attempt}", first.Id, second.Id, 0);
                deleted = result.Broadcasts.FirstOrDefault();
            }
            Assert.NotNull(deleted);
            Assert.True(writer.DeleteRankedBroadcast(writerAdmin, deleted!.Id,
                new L12AdminAuditContext("ranked-broadcast-retention-delete")));
            for (var addition = 0; addition < 20; addition++)
                writer.SettleRankedMatch($"broadcast-object-retention-tail-{addition}",
                    first.Id, second.Id, 0);
            Assert.NotNull(staleSeason.TryClaimDueSeasonFinalization(
                "ranked-broadcast-retention-worker", endsAt, TimeSpan.FromMinutes(1)));

            var restarted = new L12PlatformStore(path);
            Assert.True(restarted.CompleteRankedBroadcast(viewerId,
                protectedClaim.Broadcast.Id, protectedClaim.ClaimToken));

            using var connection = new SqliteConnection(
                $"Data Source={restarted.TransactionalStoragePath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                  (SELECT COUNT(*) FROM ranked_broadcast_definitions),
                  (SELECT COUNT(*) FROM ranked_broadcast_deliveries
                   WHERE account_id='{viewerId}' AND broadcast_id='{oldest.Broadcast.Id}'),
                  (SELECT COUNT(*) FROM ranked_broadcast_definitions
                   WHERE broadcast_id='{protectedClaim.Broadcast.Id}'),
                  (SELECT COUNT(*) FROM ranked_broadcast_deliveries
                   WHERE account_id='{viewerId}' AND broadcast_id='{protectedClaim.Broadcast.Id}'
                     AND claim_token='{protectedClaim.ClaimToken}' AND completed_utc IS NOT NULL),
                  (SELECT COUNT(*) FROM ranked_broadcast_definitions
                   WHERE broadcast_id='{deleted.Id}'),
                  (SELECT COUNT(*) FROM pragma_foreign_key_check);
                """;
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(300, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2));
            Assert.Equal(1, reader.GetInt32(3));
            Assert.Equal(0, reader.GetInt32(4));
            Assert.Equal(0, reader.GetInt32(5));
        });
    }

    private static long BroadcastGeneration(L12PlatformStore store)
    {
        using var connection = new SqliteConnection(
            $"Data Source={store.TransactionalStoragePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';";
        return long.Parse(Assert.IsType<string>(command.ExecuteScalar()),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long BroadcastDefinitionCount(L12PlatformStore store)
    {
        using var connection = new SqliteConnection(
            $"Data Source={store.TransactionalStoragePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ranked_broadcast_definitions;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string FullMirror(L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        return (string)typeof(L12PlatformStore).GetMethod(
            "SerializeFullDeckDomainBackup", PrivateStatic)!.Invoke(null, [data])!;
    }

    private static PlatformStateFacts PlatformFacts(string path, L12PlatformStore store, string viewerId)
    {
        using var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Mode=ReadOnly");
        connection.Open();
        using var state = connection.CreateCommand();
        state.CommandText = """
            SELECT schema_version,storage_revision,business_version,snapshot_json,
                   snapshot_sha256,updated_utc
            FROM platform_state WHERE singleton_id=1;
            """;
        using var reader = state.ExecuteReader();
        Assert.True(reader.Read());
        var schema = reader.GetInt32(0);
        var storageRevision = reader.GetInt64(1);
        var businessVersion = reader.GetInt64(2);
        var snapshot = reader.GetString(3);
        var snapshotHash = reader.GetString(4);
        var updated = reader.GetString(5);
        reader.Close();
        using var mirror = connection.CreateCommand();
        mirror.CommandText = "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';";
        var mirrorHash = Assert.IsType<string>(mirror.ExecuteScalar());
        var deckStorage = Assert.IsType<L12DeckStorageStatusView>(store.StorageStatus().DeckStorage);
        return new(schema, storageRevision, businessVersion, snapshot, snapshotHash, updated,
            mirrorHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            JsonSerializer.Serialize(store.Decks(viewerId)), new DeckFacts(deckStorage.Payloads,
                deckStorage.ActiveAccountDecks, deckStorage.RetainedDeletedAccountDecks,
                deckStorage.ActivePublishedDecks, deckStorage.PublishedVersions, deckStorage.Likes,
                deckStorage.TournamentReferences, deckStorage.ContentPayloads, deckStorage.ContentRevisions));
    }

    private static void InLiveBroadcast(
        Action<string, L12PlatformStore, string, DateTimeOffset> action)
        => InLiveBroadcastAsync((path, store, viewerId, subscribedAt) =>
        {
            action(path, store, viewerId, subscribedAt);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static async Task InLiveBroadcastAsync(
        Func<string, L12PlatformStore, string, DateTimeOffset, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-objects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var first = store.Register("trobjfirst", "password-123").Account!;
            var second = store.Register("trobjsecond", "password-123").Account!;
            var viewer = store.Register("trobjviewer", "password-123").Account!;
            Assert.True(store.CreateDeck(viewer.Id, new L12PresetDeckDefinition
            {
                Name = "broadcast-object-deck",
                MasterId = "M1",
                CardIds = ["C1"],
                MoraleIds = [],
                SpecialIds = [],
            }).Success);
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            for (var index = 0; index < 4; index++)
                store.SettleRankedMatch($"broadcast-object-setup-{index}", first.Id, second.Id, 0);
            var subscribedAt = DateTimeOffset.UtcNow;
            store.SettleRankedMatch("broadcast-object-live", first.Id, second.Id, 0);
            await action(path, store, viewer.Id, subscribedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private sealed record PlatformStateFacts(
        int SchemaVersion,
        long StorageRevision,
        long BusinessVersion,
        string SnapshotJson,
        string SnapshotHash,
        string UpdatedUtc,
        string MirrorHash,
        string MirrorFileHash,
        string Decks,
        DeckFacts DeckStorage);

    private sealed record DeckFacts(long Payloads, long ActiveAccountDecks,
        long RetainedDeletedAccountDecks, long ActivePublishedDecks, long PublishedVersions,
        long Likes, long TournamentReferences, long ContentPayloads, long ContentRevisions);
}
