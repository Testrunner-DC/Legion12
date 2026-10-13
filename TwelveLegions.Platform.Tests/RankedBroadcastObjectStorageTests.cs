using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedBroadcastObjectStorageTests(ITestOutputHelper output)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    [Fact]
    public void WarmEmptyPollAllocationIsBelowThirtyPercentOfForcedFullRefresh()
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            store.ExecuteAdminTransaction(() =>
            {
                var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
                var content = (System.Collections.IDictionary)data.GetType().GetProperty("Content")!.GetValue(data)!;
                var part = new string('x', 32 * 1024);
                for (var i = 0; i < 128; i++) content["synthetic-poll-part-" + i] = part;
                RankedIdentityTestState.RequestSave(store);
                return true;
            });
            var anchor = typeof(L12PlatformStore).GetField("_rankedBroadcastReadAnchor", PrivateInstance)!;
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var warmStart = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 8; i++) Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var warmAllocated = GC.GetAllocatedBytesForCurrentThread() - warmStart;
            var coldStart = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 8; i++)
            {
                anchor.SetValue(store, null); // Same binary/input; explicitly exercise the old full refresh path.
                Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            }
            var coldAllocated = GC.GetAllocatedBytesForCurrentThread() - coldStart;
            output.WriteLine("empty_poll_8x_warm_allocated_bytes={0} forced_refresh_allocated_bytes={1}",
                warmAllocated, coldAllocated);
            Assert.True(coldAllocated > 4 * 1024 * 1024);
            Assert.True(warmAllocated * 10 < coldAllocated * 3);
        });
    }
    [Fact]
    public void EmptyClaimAndIdempotentCompleteKeepTheSameCommittedRollbackBuffer()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(
                store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            // Warm the validated read anchor; the following calls must not rebuild global state.
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var field = typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!;
            var beforeBuffer = Assert.IsType<byte[]>(field.GetValue(store));
            var before = PlatformFacts(path, store, viewerId);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-session-index-build")
                    throw new InvalidOperationException("no-op must not prepare global indexes");
            };
            try
            {
                for (var repeat = 0; repeat < 12; repeat++)
                {
                    Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
                    Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
                    Assert.False(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, "wrong-token"));
                    Assert.Same(beforeBuffer, field.GetValue(store));
                }
            }
            finally { store.StorageFailureInjector = null; }
            Assert.Equal(before, PlatformFacts(path, store, viewerId));
        });
    }

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
    public void WarmAnchorFallsBackAfterRollbackRestoresDataInstance()
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var oldData = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store);
            typeof(L12PlatformStore).GetMethod("RestoreLastCommittedSnapshot", PrivateInstance)!.Invoke(store, null);
            Assert.NotSame(oldData, typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store));
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-session-index-build") throw new InvalidOperationException("verified full refresh");
            };
            try
            {
                var failure = Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ClaimRankedBroadcast(viewerId, subscribedAt));
                Assert.Contains("verified full refresh", failure.ToString());
            }
            finally { store.StorageFailureInjector = null; }
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
        });
    }

    [Fact]
    public void WarmAnchorSeesBroadcastCommittedByAnotherStore()
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var writer = new L12PlatformStore(path);
            var first = writer.Accounts().Single(row => row.Username == "trobjfirst");
            var second = writer.Accounts().Single(row => row.Username == "trobjsecond");
            var fresh = DateTimeOffset.UtcNow;
            writer.SettleRankedMatch("warm-anchor-new-broadcast", first.Id, second.Id, 0);
            var next = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, fresh));
            Assert.Equal("warm-anchor-new-broadcast", next.Broadcast.MatchId);
            Assert.Null(writer.ClaimRankedBroadcast(viewerId, fresh));
        });
    }

    [Theory]
    [InlineData("UTF-16le")]
    [InlineData("UTF-16be")]
    public void Utf16WarmNoOpUsesLegacyRefresh(string encoding)
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-session-index-build") throw new InvalidOperationException("legacy encoding refresh");
            };
            try
            {
                var failure = Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ClaimRankedBroadcast(viewerId, subscribedAt));
                Assert.Contains("legacy encoding refresh", failure.ToString());
            }
            finally { store.StorageFailureInjector = null; }
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
        }, encoding);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("delivery")]
    [InlineData("orphan")]
    public void WarmAnchorDetectsDigestValidSameGenerationObjectRewrite(string kind)
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var field = typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!;
            var beforeBuffer = field.GetValue(store);
            var beforeGeneration = BroadcastGeneration(store);
            using var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var query = connection.CreateCommand();
            query.Parameters.AddWithValue("$id", claim.Broadcast.Id);
            if (kind == "definition")
            {
                query.CommandText = "SELECT match_id,event_type,created_utc,row_generation FROM ranked_broadcast_definitions WHERE broadcast_id=$id;";
                using var reader = query.ExecuteReader();
                Assert.True(reader.Read());
                var digest = typeof(L12PlatformStore).GetMethod("RankedBroadcastDefinitionDigest", PrivateStatic)!
                    .Invoke(null, [claim.Broadcast.Id, reader.GetString(0), reader.GetString(1), "synthetic-rewritten",
                        DateTimeOffset.Parse(reader.GetString(2)), reader.GetInt64(3)]);
                reader.Close();
                query.CommandText = "UPDATE ranked_broadcast_definitions SET message='synthetic-rewritten',row_digest=$digest WHERE broadcast_id=$id;";
                query.Parameters.AddWithValue("$digest", digest!);
                query.ExecuteNonQuery();
                Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
                Assert.NotSame(beforeBuffer, field.GetValue(store));
                Assert.Equal("synthetic-rewritten", store.RankedBroadcasts().Single(row => row.Id == claim.Broadcast.Id).Message);
            }
            else
            {
                query.CommandText = "SELECT lease_expires_utc,completed_utc,row_generation FROM ranked_broadcast_deliveries WHERE broadcast_id=$id AND account_id=$account;";
                query.Parameters.AddWithValue("$account", viewerId);
                using var reader = query.ExecuteReader();
                Assert.True(reader.Read());
                var account = kind == "orphan" ? "synthetic-missing-account" : viewerId;
                var digest = typeof(L12PlatformStore).GetMethod("RankedBroadcastDeliveryDigest", PrivateStatic)!
                    .Invoke(null, [account, claim.Broadcast.Id, "synthetic-valid-token",
                        DateTimeOffset.Parse(reader.GetString(0)), (DateTimeOffset?)DateTimeOffset.Parse(reader.GetString(1)), reader.GetInt64(2)]);
                reader.Close();
                query.CommandText = "UPDATE ranked_broadcast_deliveries SET account_id=$replacement,claim_token='synthetic-valid-token',row_digest=$digest WHERE broadcast_id=$id AND account_id=$account;";
                query.Parameters.AddWithValue("$replacement", account);
                query.Parameters.AddWithValue("$digest", digest!);
                query.ExecuteNonQuery();
                if (kind == "orphan")
                    Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ClaimRankedBroadcast(viewerId, subscribedAt));
                else
                {
                    Assert.False(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
                    Assert.NotSame(beforeBuffer, field.GetValue(store));
                    Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, "synthetic-valid-token"));
                }
            }
            Assert.Equal(beforeGeneration, BroadcastGeneration(store));
        });
    }

    [Theory]
    [InlineData("core-body")]
    [InlineData("core-hash")]
    [InlineData("definition")]
    [InlineData("delivery")]
    [InlineData("generation")]
    public void WarmEmptyPollStillRejectsCorruptSqlAuthority(string corruption)
    {
        InLiveBroadcast((_, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            using var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = corruption switch
            {
                "core-body" => "UPDATE platform_state SET snapshot_json=snapshot_json||' ';",
                "core-hash" => "UPDATE platform_state SET snapshot_sha256='corrupt';",
                "definition" => "UPDATE ranked_broadcast_definitions SET message=message||'corrupt';",
                "delivery" => "UPDATE ranked_broadcast_deliveries SET claim_token='corrupt';",
                _ => "UPDATE storage_meta SET value='not-a-generation' WHERE key='ranked_broadcast_generation';",
            };
            command.ExecuteNonQuery();
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
        });
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("delete")]
    public void WarmNoOpAnchorCannotHideAnotherStoresAccountRevocation(string operation)
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            var claim = Assert.IsType<L12RankedBroadcastClaimView>(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            Assert.True(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Null(store.ClaimRankedBroadcast(viewerId, subscribedAt));
            var writer = new L12PlatformStore(path);
            var admin = writer.Login("Admin", "L12master").Account!;
            var context = new L12AdminAuditContext("warm-poll-revocation");
            if (operation == "disable") writer.SetAccountDisabled(admin, viewerId, true, "guard", context, true);
            else writer.DeleteAccountPersonalData(admin, viewerId, "guard", context, true);
            Assert.False(store.CompleteRankedBroadcast(viewerId, claim.Broadcast.Id, claim.ClaimToken));
            Assert.Throws<KeyNotFoundException>(() => store.ClaimRankedBroadcast(viewerId, subscribedAt));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadAnchorPreparationFailureCannotCommitClaimOrCompletion(bool complete)
    {
        InLiveBroadcast((path, store, viewerId, subscribedAt) =>
        {
            L12RankedBroadcastClaimView? claim = complete
                ? store.ClaimRankedBroadcast(viewerId, subscribedAt) : null;
            var before = BroadcastGeneration(store);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-ranked-broadcast-read-anchor")
                    throw new OutOfMemoryException("synthetic-read-anchor-allocation");
            };
            try
            {
                Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                {
                    if (complete) store.CompleteRankedBroadcast(viewerId, claim!.Broadcast.Id, claim.ClaimToken);
                    else store.ClaimRankedBroadcast(viewerId, subscribedAt);
                });
            }
            finally { store.StorageFailureInjector = null; }
            Assert.Equal(before, BroadcastGeneration(store));
            var reopened = new L12PlatformStore(path);
            if (complete)
                Assert.True(reopened.CompleteRankedBroadcast(viewerId, claim!.Broadcast.Id, claim.ClaimToken));
            else
                Assert.NotNull(reopened.ClaimRankedBroadcast(viewerId, subscribedAt));
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
        Action<string, L12PlatformStore, string, DateTimeOffset> action, string? databaseEncoding = null)
        => InLiveBroadcastAsync((path, store, viewerId, subscribedAt) =>
        {
            action(path, store, viewerId, subscribedAt);
            return Task.CompletedTask;
        }, databaseEncoding).GetAwaiter().GetResult();

    private static async Task InLiveBroadcastAsync(
        Func<string, L12PlatformStore, string, DateTimeOffset, Task> action, string? databaseEncoding = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranked-broadcast-objects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            if (databaseEncoding is not null)
            {
                Assert.Contains(databaseEncoding, new[] { "UTF-16le", "UTF-16be" });
                using var seed = new SqliteConnection($"Data Source={Path.ChangeExtension(path, ".db")};Pooling=False");
                seed.Open();
                using var command = seed.CreateCommand();
                command.CommandText = $"PRAGMA encoding='{databaseEncoding}'; CREATE TABLE encoding_guard(value TEXT);";
                command.ExecuteNonQuery();
            }
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
