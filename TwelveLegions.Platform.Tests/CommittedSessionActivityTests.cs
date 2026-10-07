using System.Diagnostics;
using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedSessionActivityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedSessionQueryDoesNotWaitForUnrelatedSave(bool fail)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic barrier");
            if (fail) throw new IOException("synthetic commit failure");
        };
        var writer = Task.Factory.StartNew(() =>
        {
            try { store.Register("unrelated", "password-123"); return false; }
            catch (L12PlatformStorageUnavailableException) { return true; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var query = Task.Factory.StartNew(() =>
            {
                var watch = Stopwatch.StartNew();
                var active = store.IsSessionActive(fixture.Owner.SessionId);
                return (active, watch.Elapsed);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var observed = await query.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(observed.active);
            Assert.True(observed.Elapsed < TimeSpan.FromMilliseconds(250));
        }
        finally
        {
            release.Set();
            Assert.Equal(fail, await writer.WaitAsync(TimeSpan.FromSeconds(5)));
            store.StorageFailureInjector = null;
        }
        Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OuterCommitFailureDoesNotNotifyOrInvalidate(bool fail)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var notifications = 0;
        var notifiedIds = new List<string>();
        var activeAtNotification = new List<bool>();
        var beforeCommitNotifications = new List<int>();
        var beforeCommitActivity = new List<bool>();
        store.SessionsRevoked += ids =>
        {
            notifications++;
            notifiedIds.AddRange(ids);
            activeAtNotification.Add(store.IsSessionActive(fixture.Owner.SessionId));
        };
        store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            beforeCommitNotifications.Add(notifications);
            beforeCommitActivity.Add(store.IsSessionActive(fixture.Owner.SessionId));
            if (fail) throw new IOException("synthetic outer commit failure");
        };
        void Change() => store.ExecuteAdminTransaction(() => store.SetAccountDisabled(fixture.Admin,
            fixture.Owner.Account.Id, true, "synthetic", fixture.Context, true));
        if (fail) Assert.Throws<L12PlatformStorageUnavailableException>(Change);
        else Change();
        store.StorageFailureInjector = null;
        Assert.Equal(fail ? 0 : 1, notifications);
        Assert.Equal(new[] { 0 }, beforeCommitNotifications);
        Assert.Equal(new[] { true }, beforeCommitActivity);
        if (!fail)
        {
            Assert.Contains(fixture.Owner.SessionId, notifiedIds);
            Assert.All(activeAtNotification, active => Assert.False(active));
        }
        Assert.Equal(fail, store.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(fail, new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData("before-session-index-build")]
    [InlineData("after-session-index-build")]
    public void IndexBuildFailureLeavesDatabaseAndCommittedProjectionUnchanged(string fault)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var version = store.Version;
        var notified = false;
        var reached = false;
        store.SessionsRevoked += _ => notified = true;
        store.StorageFailureInjector = stage =>
        {
            if (stage != fault) return;
            reached = true;
            throw new IOException("synthetic index failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.ExecuteAdminTransaction(() =>
            store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId)));
        store.StorageFailureInjector = null;
        Assert.True(reached);
        Assert.False(notified);
        Assert.Equal(version, store.Version);
        Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
        Assert.True(new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
        store.ExecuteAdminTransaction(() => store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId));
        Assert.False(store.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData("before-conflict-refresh")]
    [InlineData("after-conflict-snapshot-read")]
    public void FailedConflictRefreshClosesSessionViewEvenWithHealthyOldCache(string fault)
    {
        using var fixture = new Fixture();
        var stale = fixture.Store;
        var cache = Cache(stale);
        var writer = new L12PlatformStore(fixture.Path);
        writer.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        Assert.False(writer.IsSessionActive(fixture.Owner.SessionId));
        Assert.True(stale.IsSessionActive(fixture.Owner.SessionId));
        var reached = false;
        stale.StorageFailureInjector = stage =>
        {
            if (stage != fault) return;
            reached = true;
            throw new IOException("synthetic refresh fault");
        };
        Assert.Throws<L12PlatformStorageRefreshException>(() => stale.ExecuteAdminTransaction(() =>
            stale.Register("confprobe", "password-123")));
        stale.StorageFailureInjector = null;
        Assert.True(reached);
        Assert.Same(cache, Cache(stale));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.IsSessionActive(fixture.Owner.SessionId));
        Assert.False(new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
    }

    [Fact]
    public void SuccessfulConflictRefreshAdoptsLatestRevocationAndMatchingRollbackCache()
    {
        using var fixture = new Fixture();
        var stale = fixture.Store;
        var oldCache = Cache(stale);
        var writer = new L12PlatformStore(fixture.Path);
        writer.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        Assert.Throws<L12PlatformStorageConflictException>(() => stale.ExecuteAdminTransaction(() =>
            stale.Register("confprobe", "password-123")));
        Assert.False(stale.IsSessionActive(fixture.Owner.SessionId));
        Assert.NotSame(oldCache, Cache(stale));
        Assert.Same(Cache(stale), ProjectionCache(stale));
        var projection = Projection(stale);
        Restore(stale);
        Assert.Same(projection, Projection(stale));
        Assert.False(stale.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewLoginCannotBecomeActiveBeforeOuterCommit(bool fail)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        string? pendingId = null;
        var pendingIdsObserved = new List<string?>();
        var activityBeforeCommit = new List<bool>();
        store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            pendingIdsObserved.Add(pendingId);
            activityBeforeCommit.Add(store.IsSessionActive(pendingId!));
            if (fail) throw new IOException("synthetic login commit fault");
        };
        void Login() => store.ExecuteAdminTransaction(() =>
        {
            var result = store.Login("indexowner", "password-123");
            pendingId = store.AuthenticateTokenSession(result.Token)!.SessionId;
            return result;
        });
        if (fail) Assert.Throws<L12PlatformStorageUnavailableException>(Login);
        else Login();
        store.StorageFailureInjector = null;
        Assert.NotNull(Assert.Single(pendingIdsObserved));
        Assert.Equal(new[] { false }, activityBeforeCommit);
        Assert.Equal(!fail, store.IsSessionActive(pendingId!));
        Assert.Equal(!fail, new L12PlatformStore(fixture.Path).IsSessionActive(pendingId!));
        Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData("own")]
    [InlineData("own-all")]
    [InlineData("admin-one")]
    [InlineData("admin-all")]
    [InlineData("disable")]
    [InlineData("delete")]
    [InlineData("reset-password")]
    public void RealLifecycleRevocationsPublishImmediatelyAfterCommit(string operation)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var events = new List<string>();
        var activeAtNotification = new List<bool>();
        store.SessionsRevoked += ids =>
        {
            events.AddRange(ids);
            foreach (var id in ids) activeAtNotification.Add(store.IsSessionActive(id));
        };
        store.ExecuteAdminTransaction(() =>
        {
            switch (operation)
            {
                case "own": store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId); break;
                case "own-all": store.RevokeOwnSessions(fixture.Owner); break;
                case "admin-one": store.RevokeAccountSession(fixture.Admin, fixture.Owner.Account.Id, fixture.Owner.SessionId); break;
                case "admin-all": store.RevokeAccountSessions(fixture.Admin, fixture.Owner.Account.Id); break;
                case "disable": store.SetAccountDisabled(fixture.Admin, fixture.Owner.Account.Id, true, "synthetic", fixture.Context, true); break;
                case "delete": store.DeleteAccountPersonalData(fixture.Admin, fixture.Owner.Account.Id, "synthetic", fixture.Context, true); break;
                case "reset-password": store.AdminResetPassword(fixture.Admin, fixture.Owner.Account.Id, "synthetic", fixture.Context, true); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
            return true;
        });
        Assert.False(store.IsSessionActive(fixture.Owner.SessionId));
        Assert.False(new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(1, events.Count(id => id == fixture.Owner.SessionId));
        Assert.Equal(events.Count, activeAtNotification.Count);
        Assert.All(activeAtNotification, active => Assert.False(active));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("other-sessions")]
    public void CurrentSessionExceptionsRemainIntact(string operation)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var other = store.AuthenticateTokenSession(store.Login("indexowner", "password-123").Token)!;
        if (operation == "password")
            Assert.True(store.ChangePassword(fixture.Owner.Account.Id, "password-123", "password-456", fixture.Owner.SessionId).Success);
        else store.RevokeOtherOwnSessions(fixture.Owner);
        Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
        Assert.False(store.IsSessionActive(other.SessionId));
    }

    [Fact]
    public void ActivityProjectionDoesNotReplaceFreshPermissionSubjectAndKeepsOrdinalComparison()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var token = store.Login("indexowner", "password-123").Token;
        var session = store.AuthenticateTokenSession(token)!;
        Assert.True(store.SetRole(fixture.Admin, session.Account.Id, "admin"));
        Assert.Equal("admin", store.AuthenticateTokenSession(token)!.Account.Role);
        Assert.True(store.IsSessionActive(session.SessionId));
        Assert.True(store.SetRole(fixture.Admin, session.Account.Id, "player"));
        Assert.Equal("player", store.AuthenticateTokenSession(token)!.Account.Role);
        Assert.True(store.IsSessionActive(session.SessionId));
        if (!string.Equals(session.SessionId, session.SessionId.ToUpperInvariant(), StringComparison.Ordinal))
            Assert.False(store.IsSessionActive(session.SessionId.ToUpperInvariant()));
        Assert.False(store.IsSessionActive(""));
        Assert.False(store.IsSessionActive(null!));
    }

    [Fact]
    public void ExpiryUsesCurrentUtcWithoutAnotherCommit()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var state = typeof(L12PlatformStore).GetField("_state", PrivateInstance)!.GetValue(store)!;
        var rows = (IEnumerable)state.GetType().GetProperty("Sessions")!.GetValue(state)!;
        var row = rows.Cast<object>().Single(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == fixture.Owner.SessionId);
        row.GetType().GetProperty("ExpiresAt")!.SetValue(row, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(store.SetRole(fixture.Admin, fixture.Owner.Account.Id, "player"));
        Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
        Thread.Sleep(1100);
        Assert.False(store.IsSessionActive(fixture.Owner.SessionId));
    }

    [Fact]
    public void NestedFailureTrimsOnlyItsEventsAndOuterSuccessDeduplicates()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var second = fixture.CreateSession("secondowner");
        var observed = new List<string>();
        store.SessionsRevoked += ids => observed.AddRange(ids);
        store.ExecuteAdminTransaction(() =>
        {
            Assert.Throws<InvalidOperationException>(() => store.ExecuteAdminTransaction<bool>(() =>
            {
                store.RevokeOwnSession(second, second.SessionId);
                throw new InvalidOperationException("synthetic nested action");
            }));
            store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
            store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
            return true;
        });
        Assert.True(store.IsSessionActive(second.SessionId));
        Assert.False(store.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(new[] { fixture.Owner.SessionId }, observed);
    }

    [Fact]
    public void SubscriberFailureAndReentrantTransactionDoNotSwallowOrDuplicateEvents()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var second = fixture.CreateSession("secondowner");
        var third = fixture.CreateSession("thirdowner");
        var observed = new List<string>();
        var lockStates = new List<bool>();
        var throwingCalls = 0;
        var reentered = false;
        var gate = typeof(L12PlatformStore).GetField("_gate", PrivateInstance)!.GetValue(store)!;
        store.SessionsRevoked += _ => { throwingCalls++; throw new IOException("synthetic subscriber"); };
        store.SessionsRevoked += ids =>
        {
            observed.AddRange(ids);
            lockStates.Add(Monitor.IsEntered(gate));
            if (!reentered && ids.Contains(fixture.Owner.SessionId))
            {
                reentered = true;
                store.ExecuteAdminTransaction(() => store.RevokeOwnSession(second, second.SessionId));
            }
        };
        store.ExecuteAdminTransaction(() => store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId));
        store.ExecuteAdminTransaction(() => store.RevokeOwnSession(third, third.SessionId));
        Assert.True(reentered);
        Assert.Equal(3, throwingCalls);
        Assert.Equal(3, observed.Count);
        Assert.Equal(1, observed.Count(id => id == fixture.Owner.SessionId));
        Assert.Equal(1, observed.Count(id => id == second.SessionId));
        Assert.Equal(1, observed.Count(id => id == third.SessionId));
        Assert.All(lockStates, held => Assert.False(held));
        Assert.False(store.IsSessionActive(second.SessionId));
        Assert.False(store.IsSessionActive(third.SessionId));
    }

    [Fact]
    public async Task IndependentNotifierCannotJoinAnotherThreadsOuterTransaction()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        store.SessionsRevoked += ids => { foreach (var id in ids) observed.Enqueue(id); };
        store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic barrier");
        };
        var writer = Task.Factory.StartNew(() => store.ExecuteAdminTransaction(() =>
            store.Register("unrelated", "password-123")), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            // Synthetic already-committed notifier isolates queue ownership only.
            typeof(L12PlatformStore).GetMethod("NotifySessionsRevoked", PrivateInstance)!.Invoke(store,
                new object[] { new[] { "synthetic-independent-notification" } });
            Assert.Equal(new[] { "synthetic-independent-notification" }, observed.ToArray());
        }
        finally
        {
            release.Set();
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
            store.StorageFailureInjector = null;
        }
        Assert.Single(observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedRollbackCacheRebuildsVerifiedProjectionOrFailsClosed(bool corruptDatabase)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!.SetValue(store, Array.Empty<byte>());
        if (corruptDatabase)
        {
            using var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE platform_state SET snapshot_sha256='invalid';";
            command.ExecuteNonQuery();
        }
        store.StorageFailureInjector = stage => { if (stage == "before-commit") throw new IOException("synthetic rollback"); };
        Assert.ThrowsAny<L12PlatformStorageUnavailableException>(() => store.ExecuteAdminTransaction(() =>
            store.Register("rolledback", "password-123")));
        store.StorageFailureInjector = null;
        if (corruptDatabase)
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.IsSessionActive(fixture.Owner.SessionId));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.Version);
        }
        else
        {
            Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
            Assert.Same(Cache(store), ProjectionCache(store));
            Assert.True(new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
        }
    }

    [Fact]
    public void SeasonNoOpRefreshAndOrdinaryRollbackBindToExactNewCache()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        var operations = store.OperationsConfig(fixture.Admin);
        store.ApplyOperationsConfig(fixture.Admin, operations.Config with
        {
            Season = operations.Config.Season with { EndsAt = endsAt },
        }, operations.Version, "synthetic season end", fixture.Context);
        Assert.NotNull(store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1)));
        var stale = new L12PlatformStore(fixture.Path);
        var oldCache = Cache(stale);
        store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        Assert.Null(stale.TryClaimDueSeasonFinalization("other", endsAt.AddSeconds(1), TimeSpan.FromMinutes(1)));
        Assert.False(stale.IsSessionActive(fixture.Owner.SessionId));
        Assert.NotSame(oldCache, Cache(stale));
        Assert.Same(Cache(stale), ProjectionCache(stale));
        var projection = Projection(stale);
        stale.StorageFailureInjector = stage => { if (stage == "before-commit") throw new IOException("synthetic rollback"); };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.Register("rolledback", "password-123"));
        stale.StorageFailureInjector = null;
        Assert.Same(projection, Projection(stale));
        Assert.False(stale.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData(false, "before-session-index-build")]
    [InlineData(false, "after-session-index-build")]
    [InlineData(true, "before-session-index-build")]
    [InlineData(true, "after-session-index-build")]
    public void SeasonNoOpBuildFailurePreservesOnlyVerifiedSameGeneration(bool newer, string fault)
    {
        using var fixture = new Fixture();
        var endsAt = PrepareDueSeason(fixture);
        Assert.NotNull(fixture.Store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1)));
        var stale = new L12PlatformStore(fixture.Path);
        var projection = Projection(stale);
        if (newer) fixture.Store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        var reached = false;
        stale.StorageFailureInjector = stage =>
        {
            if (stage != fault) return;
            reached = true;
            throw new IOException("synthetic no-op build fault");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.TryClaimDueSeasonFinalization(
            "other", endsAt.AddSeconds(1), TimeSpan.FromMinutes(1)));
        stale.StorageFailureInjector = null;
        Assert.True(reached);
        if (newer)
            Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.IsSessionActive(fixture.Owner.SessionId));
        else
        {
            Assert.Same(projection, Projection(stale));
            Assert.True(stale.IsSessionActive(fixture.Owner.SessionId));
        }
        Assert.Null(stale.TryClaimDueSeasonFinalization("other", endsAt.AddSeconds(1), TimeSpan.FromMinutes(1)));
        Assert.Equal(!newer, stale.IsSessionActive(fixture.Owner.SessionId));
        Assert.Same(Cache(stale), ProjectionCache(stale));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("missing")]
    public void SeasonAuthorityReadFailureCannotReuseHealthyOldSessionProjection(string fault)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var cache = Cache(store);
        SqliteConnection.ClearAllPools();
        if (fault == "missing")
            File.Move(store.TransactionalStoragePath, store.TransactionalStoragePath + ".held");
        else
        {
            using var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE platform_state SET snapshot_sha256='invalid';";
            command.ExecuteNonQuery();
        }
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.TryClaimDueSeasonFinalization(
            "worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        Assert.Same(cache, Cache(store));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.IsSessionActive(fixture.Owner.SessionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeasonBusinessRejectionKeepsOnlyVerifiedSameGenerationAvailable(bool newer)
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var endsAt = PrepareDueSeason(fixture);
        var claim = store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1))!;
        var stale = new L12PlatformStore(fixture.Path);
        var projection = Projection(stale);
        if (newer) store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
        Assert.Throws<L12OperationsConfigException>(() => stale.RecordSeasonFinalizationWaiting(
            claim with { LeaseOwner = "not-the-owner" }, endsAt.AddSeconds(1), "synthetic", "synthetic"));
        if (newer)
            Assert.Throws<L12PlatformStorageUnavailableException>(() => stale.IsSessionActive(fixture.Owner.SessionId));
        else
        {
            Assert.Same(projection, Projection(stale));
            Assert.True(stale.IsSessionActive(fixture.Owner.SessionId));
        }
    }

    [Fact]
    public void MirrorFailureDoesNotRollbackDurableSessionProjectionOrCommittedNotification()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var observed = new List<string>();
        var temp = fixture.Path + ".tmp";
        Directory.CreateDirectory(temp); // Owned synthetic fixture: force only the mirror writer to fail.
        store.SessionsRevoked += ids => observed.AddRange(ids);
        store.ExecuteAdminTransaction(() => store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId));
        Assert.False(store.IsSessionActive(fixture.Owner.SessionId));
        Assert.Equal(new[] { fixture.Owner.SessionId }, observed);
        Assert.Same(Cache(store), ProjectionCache(store));
        Directory.Delete(temp);
        Assert.False(new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
    }

    [Fact]
    public void InitialMigrationRestartAndReadonlyFallbackBindExactCommittedCache()
    {
        using var fixture = new Fixture();
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(fixture.Store)!;
        var options = (System.Text.Json.JsonSerializerOptions)typeof(L12PlatformStore)
            .GetField("PlatformMigrationJsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var legacy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "legacy", "platform.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(legacy)!);
        var fullLegacyJson = System.Text.Json.JsonSerializer.Serialize(data, data.GetType(), options);
        File.WriteAllText(legacy, fullLegacyJson);
        var migrated = new L12PlatformStore(legacy);
        Assert.True(migrated.IsSessionActive(fixture.Owner.SessionId));
        Assert.Same(Cache(migrated), ProjectionCache(migrated));
        var restarted = new L12PlatformStore(legacy);
        Assert.True(restarted.IsSessionActive(fixture.Owner.SessionId));
        Assert.Same(Cache(restarted), ProjectionCache(restarted));
        // Migration writes the compact compatibility mirror. Restore the independent
        // full legacy input before damaging SQLite so readonly recovery proves a
        // complete source rather than treating the compact mirror as complete.
        File.WriteAllText(legacy, fullLegacyJson);
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(migrated.TransactionalStoragePath, new byte[] { 1, 2, 3 });
        var fallback = new L12PlatformStore(legacy);
        Assert.Equal("json-fallback-readonly", fallback.StorageStatus().Mode);
        var projection = Projection(fallback);
        Assert.True(fallback.IsSessionActive(fixture.Owner.SessionId));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fallback.Register("rofail", "password-123"));
        Assert.Same(projection, Projection(fallback));
        Assert.True(fallback.IsSessionActive(fixture.Owner.SessionId));
        Assert.Same(Cache(fallback), ProjectionCache(fallback));
    }

    [Fact]
    public void UnauthorizedRevocationAndExistingFourSessionRetentionRemainUnchanged()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var other = fixture.CreateSession("otherowner");
        Assert.False(store.RevokeAccountSession(fixture.Owner.Account, other.Account.Id, other.SessionId).Found);
        Assert.True(store.IsSessionActive(other.SessionId));
        var ids = new List<string> { fixture.Owner.SessionId };
        for (var index = 0; index < 4; index++)
            ids.Add(store.AuthenticateTokenSession(store.Login("indexowner", "password-123").Token)!.SessionId);
        Assert.False(store.IsSessionActive(ids[0]));
        Assert.All(ids.Skip(1), id => Assert.True(store.IsSessionActive(id)));
        Assert.Equal(4, store.Sessions(fixture.Owner.Account.Id, ids[^1]).Count);
    }

    [Fact]
    public void NestedNoOpAuthorityRefreshDoesNotNotifyAnEarlierUncommittedRevocation()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var endsAt = PrepareDueSeason(fixture);
        Assert.NotNull(store.TryClaimDueSeasonFinalization("worker", endsAt, TimeSpan.FromMinutes(1)));
        var observed = new List<string>();
        store.SessionsRevoked += ids => observed.AddRange(ids);
        store.ExecuteAdminTransaction(() =>
        {
            store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId);
            Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
            Assert.Null(store.TryClaimDueSeasonFinalization("other", endsAt.AddSeconds(1), TimeSpan.FromMinutes(1)));
            return true;
        });
        Assert.Empty(observed);
        Assert.True(store.IsSessionActive(fixture.Owner.SessionId));
        Assert.True(new L12PlatformStore(fixture.Path).IsSessionActive(fixture.Owner.SessionId));
        store.ExecuteAdminTransaction(() => store.RevokeOwnSession(fixture.Owner, fixture.Owner.SessionId));
        Assert.Equal(new[] { fixture.Owner.SessionId }, observed);
        Assert.False(store.IsSessionActive(fixture.Owner.SessionId));
    }

    private static DateTimeOffset PrepareDueSeason(Fixture fixture)
    {
        var endsAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
        var operations = fixture.Store.OperationsConfig(fixture.Admin);
        fixture.Store.ApplyOperationsConfig(fixture.Admin, operations.Config with
        {
            Season = operations.Config.Season with { EndsAt = endsAt },
        }, operations.Version, "synthetic season end", fixture.Context);
        return endsAt;
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Projection(L12PlatformStore store) => typeof(L12PlatformStore)
        .GetField("_committedSessionActivity", PrivateInstance)!.GetValue(store)!;
    private static byte[] Cache(L12PlatformStore store) => (byte[])typeof(L12PlatformStore)
        .GetField("_lastCommittedSnapshot", PrivateInstance)!.GetValue(store)!;
    private static byte[] ProjectionCache(L12PlatformStore store) => (byte[])Projection(store)
        .GetType().GetProperty("RollbackSnapshot")!.GetValue(Projection(store))!;
    private static void Restore(L12PlatformStore store) => typeof(L12PlatformStore)
        .GetMethod("RestoreLastCommittedSnapshot", PrivateInstance)!.Invoke(store, null);

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-committed-sessions", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal L12AuthenticatedSession Owner { get; }
        internal L12AdminAuditContext Context { get; } = new("synthetic-session-index", "admin.accounts.status.write");
        internal Fixture()
        {
            Store = new L12PlatformStore(Path);
            Admin = Store.Login("Admin", "L12master").Account!;
            var owner = Store.Register("indexowner", "password-123");
            Owner = Store.AuthenticateTokenSession(owner.Token)!;
        }
        internal L12AuthenticatedSession CreateSession(string name)
            => Store.AuthenticateTokenSession(Store.Register(name, "password-123").Token)!;
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
