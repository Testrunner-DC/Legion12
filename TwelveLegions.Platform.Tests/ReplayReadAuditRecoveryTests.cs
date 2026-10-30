using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ReplayReadAuditRecoveryTests
{
    private static readonly L12AdminAuditContext Context = new("synthetic-replay-audit", "admin.matches.read");
    private static void Read(RankedIdentityFixture f, CancellationToken cancellation = default)
        => f.Store.RecordAdminRead(f.Admin, L12Permission.AdminMatchesRead,
            "match", "read-replay-page", "synthetic-replay", Context, cancellation);

    [Fact]
    public void UnrelatedCommitDuringReadAuditRetriesAgainstFreshAuthorityAndAppendsOnce()
    {
        using var f = new RankedIdentityFixture();
        var attempts = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-read-audit-attempt" && ++attempts == 1)
                Assert.True(f.Store.Register("readrace", "Password123!").Success);
        };
        Read(f);
        Assert.Equal(2, attempts);
        Assert.Single(f.Store.AdminAudit(category: "match"), row => row.Target == "synthetic-replay");
    }

    [Fact]
    public void BusyBeforeCommitRetriesSameAuditIdWithoutDuplicatingOrChangingBusinessState()
    {
        using var f = new RankedIdentityFixture();
        var generation = RankedIdentityTestState.CommittedProjection(f.Store);
        var version = f.Store.Version;
        var commits = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-read-audit-commit" && ++commits == 1)
                throw new SqliteException("synthetic private detail", 5);
        };
        Read(f);
        Assert.Equal(2, commits);
        Assert.Equal(version, f.Store.Version);
        Assert.Same(generation, RankedIdentityTestState.CommittedProjection(f.Store));
        Assert.Single(f.Store.AdminAudit(category: "match"), row => row.Target == "synthetic-replay");
    }

    [Fact]
    public void PermissionRevokedBetweenAttemptsCannotAppendAnAudit()
    {
        using var f = new RankedIdentityFixture();
        var attempts = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-read-audit-attempt" || ++attempts != 1) return;
            f.Store.ExecuteAdminTransaction(() =>
            {
                var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Store)!;
                var accounts = (System.Collections.IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
                var actor = accounts.Cast<object>().Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == f.Admin.Id);
                actor.GetType().GetProperty("Role")!.SetValue(actor, "player");
                RankedIdentityTestState.RequestSave(f.Store);
                return true;
            });
        };
        Assert.Throws<UnauthorizedAccessException>(() => Read(f));
        Assert.DoesNotContain(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
    }

    [Fact]
    public void PermanentIoFailureIsNotRetriedAndPreservesTheCommittedGeneration()
    {
        using var f = new RankedIdentityFixture();
        var generation = RankedIdentityTestState.CommittedProjection(f.Store);
        var commits = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-read-audit-commit") { commits++; throw new IOException("synthetic permanent failure"); }
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => Read(f));
        Assert.Equal(1, commits);
        Assert.Same(generation, RankedIdentityTestState.CommittedProjection(f.Store));
        Assert.DoesNotContain(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageErrorsKeepCorrelatedCauseWithoutWritingPrivateDetails(bool conflict)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/admin/matches/private-match/replay";
        context.Request.QueryString = new QueryString("?cursor=private-cursor");
        context.Response.Body = new MemoryStream();
        context.Items[L12CorrelationIds.ContextItemName] = "synthetic-replay-failure";
        using var output = new StringWriter();
        var old = Console.Error;
        try
        {
            Console.SetError(output);
            await L12HttpExceptionBoundary.InvokeAsync(context, () => throw (conflict
                ? new L12PlatformStorageConflictException(1, 2)
                : new L12PlatformStorageUnavailableException("private-state", new SqliteException("private-sql", 5))));
        }
        finally { Console.SetError(old); }
        Assert.Equal(conflict ? 409 : 503, context.Response.StatusCode);
        var diagnostic = output.ToString();
        Assert.Contains("synthetic-replay-failure", diagnostic);
        Assert.Contains(conflict ? "storage_conflict" : "storage_unavailable", diagnostic);
        if (!conflict) Assert.Contains("sqliteCode=5", diagnostic);
        Assert.DoesNotContain("private-", diagnostic);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.DoesNotContain("private-", body.RootElement.ToString());
    }

    [Fact]
    public async Task RevokedReadAuthorityIsForbiddenRatherThanRetryableServerFailure()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items[L12CorrelationIds.ContextItemName] = "synthetic-revoked-read";
        var error = new UnauthorizedAccessException("private permission detail");
        error.Data[nameof(L12ReadAuditDiagnostic)] = new L12ReadAuditDiagnostic("permission");
        await L12HttpExceptionBoundary.InvokeAsync(context, () => throw error);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("forbidden", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private", body.RootElement.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealSqliteWriterLockRecoversWhenReleasedOrStopsAfterTwoAttempts(bool releaseAfterFirst)
    {
        using var f = new RankedIdentityFixture();
        var version = f.Store.Version;
        var generation = RankedIdentityTestState.CommittedProjection(f.Store);
        using var blocker = OpenPlatformDatabase(f.Store);
        using var transaction = blocker.BeginTransaction(deferred: false);
        using var retryEntered = new ManualResetEventSlim();
        using var continueRetry = new ManualResetEventSlim();
        var attempts = 0;
        var retries = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-read-audit-attempt") Interlocked.Increment(ref attempts);
            if (stage != "before-read-audit-retry") return;
            Interlocked.Increment(ref retries);
            if (!releaseAfterFirst) return;
            retryEntered.Set();
            if (!continueRetry.Wait(TimeSpan.FromSeconds(8)))
                throw new TimeoutException("Read audit retry barrier was not released");
        };
        var read = Task.Factory.StartNew(() => Read(f), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (releaseAfterFirst)
            {
                // No exception is injected: a real, independent SQLite transaction
                // holds the writer slot until the first actual busy result reaches retry.
                Assert.True(retryEntered.Wait(TimeSpan.FromSeconds(6)));
                Assert.Equal(1, Volatile.Read(ref attempts));
                transaction.Rollback();
                continueRetry.Set();
                await read.WaitAsync(TimeSpan.FromSeconds(6));
                Assert.Single(f.Store.AdminAudit(category: "match"), row => row.Target == "synthetic-replay");
            }
            else
            {
                var error = await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(
                    async () => await read.WaitAsync(TimeSpan.FromSeconds(8)));
                var sqlite = Assert.IsType<SqliteException>(error.InnerException);
                Assert.True(sqlite.SqliteErrorCode is 5 or 6);
                var diagnostic = Assert.IsType<L12ReadAuditDiagnostic>(error.Data[nameof(L12ReadAuditDiagnostic)]);
                Assert.Equal(2, diagnostic.Attempts);
                Assert.True(diagnostic.ElapsedMilliseconds > 0);
                transaction.Rollback();
                Assert.DoesNotContain(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
            }
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal(1, Volatile.Read(ref retries));
            Assert.Equal(version, f.Store.Version);
            Assert.Same(generation, RankedIdentityTestState.CommittedProjection(f.Store));
        }
        finally
        {
            continueRetry.Set();
            transaction.Dispose();
            try { await read.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (L12PlatformStorageUnavailableException) { }
            f.Store.StorageFailureInjector = null;
        }
    }

    [Fact]
    public async Task CancellationDuringRealBusyRetryGapPreventsASecondSqliteAttempt()
    {
        using var f = new RankedIdentityFixture();
        using var blocker = OpenPlatformDatabase(f.Store);
        using var transaction = blocker.BeginTransaction(deferred: false);
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var retries = 0;
        long gapStarted = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-read-audit-attempt") Interlocked.Increment(ref attempts);
            if (stage != "before-read-audit-retry") return;
            Interlocked.Increment(ref retries);
            gapStarted = Stopwatch.GetTimestamp();
            // The real SQLite busy result has already rolled back. Cancel while
            // production code is waiting in its cancellable 50ms retry gap.
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(10));
        };
        var read = Task.Factory.StartNew(() => Read(f, cancellation.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await read.WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(1, Volatile.Read(ref attempts));
            Assert.Equal(1, Volatile.Read(ref retries));
            Assert.True(gapStarted > 0);
            Assert.True(Stopwatch.GetElapsedTime(gapStarted) < TimeSpan.FromSeconds(2));
            transaction.Rollback();
            Assert.DoesNotContain(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
        }
        finally
        {
            cancellation.Cancel();
            transaction.Dispose();
            try { await read.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (OperationCanceledException) { }
            f.Store.StorageFailureInjector = null;
        }
    }

    [Fact]
    public void SameStorageRevisionAuthorityRepublishRetriesWithoutInventingABusinessChange()
    {
        using var f = new RankedIdentityFixture();
        var before = RankedIdentityTestState.CommittedProjection(f.Store);
        var revision = StorageRevision(f.Store);
        var businessVersion = f.Store.Version;
        var attempts = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-read-audit-attempt" || ++attempts != 1) return;
            // Force the real cold-anchor path, not a fake authority replacement.
            // Verified warm no-ops now deliberately preserve the committed identity.
            typeof(L12PlatformStore).GetField("_rankedBroadcastReadAnchor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(f.Store, null);
            // A cold no-op still rehydrates and republishes the same SQL revision.
            Assert.Null(f.Store.TryClaimDueSeasonFinalization("synthetic-read-worker",
                DateTimeOffset.MinValue, TimeSpan.FromMinutes(1)));
            Assert.Equal(revision, StorageRevision(f.Store));
            Assert.NotSame(before, RankedIdentityTestState.CommittedProjection(f.Store));
        };
        Read(f);
        Assert.Equal(2, attempts);
        Assert.Equal(revision, StorageRevision(f.Store));
        Assert.Equal(businessVersion, f.Store.Version);
        Assert.Single(f.Store.AdminAudit(category: "match"), row => row.Target == "synthetic-replay");
    }

    [Fact]
    public void StaleStoreConflictKeepsOriginalTypeAndCarriesAuthorityAttemptDiagnostics()
    {
        using var f = new RankedIdentityFixture();
        var stale = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        Assert.True(f.Store.Register("readgen", "Password123!").Success);
        var error = Assert.Throws<L12PlatformStorageConflictException>(() =>
            stale.RecordAdminRead(f.Admin, L12Permission.AdminMatchesRead,
                "match", "read-replay-page", "synthetic-replay", Context));
        var diagnostic = Assert.IsType<L12ReadAuditDiagnostic>(error.Data[nameof(L12ReadAuditDiagnostic)]);
        Assert.Equal("authority", diagnostic.Phase);
        Assert.Equal(1, diagnostic.Attempts);
        Assert.True(error.CurrentStorageRevision > error.ExpectedStorageRevision);
        Assert.DoesNotContain(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualAdminReplayPageRevocationAfterReadOrBetweenRealBusyRetriesIs403WithoutPage(bool betweenRetries)
    {
        await using var replay = await CommandDeadlineClockFixture.CreateAsync(0, 0);
        await CompleteReplayAsync(replay);
        using var f = new RankedIdentityFixture();
        var reader = f.CreateAccount("readauditor");
        Assert.True(f.Store.SetRole(f.Admin, reader.Id, "admin"));
        var token = f.Store.Login(reader.Username, "Password123!").Token!;
        var manager = new L12RoomManager(replay.Catalog, replay.Recorder, f.Store);
        await using var server = new L12WebSocketServer(manager, replay.Recorder, f.Store, replay.Catalog);
        await server.StartAsync(0);
        using var client = AdminClient(server, token);
        var path = $"/api/admin/matches/{replay.Engine.State.MatchId}/replay?limit=1";
        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        SqliteConnection? blocker = null;
        SqliteTransaction? transaction = null;
        Task<HttpResponseMessage>? pending = null;
        var attempts = 0;
        var retries = 0;
        var correlation = Guid.NewGuid().ToString();
        try
        {
            using (var control = await client.GetAsync(path))
            {
                Assert.Equal(HttpStatusCode.OK, control.StatusCode);
                using var body = JsonDocument.Parse(await control.Content.ReadAsStringAsync());
                Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
            }
            if (betweenRetries)
            {
                blocker = OpenPlatformDatabase(f.Store);
                transaction = blocker.BeginTransaction(deferred: false);
            }
            f.Store.StorageFailureInjector = stage =>
            {
                if (stage == "before-read-audit-attempt") Interlocked.Increment(ref attempts);
                if (stage == "before-read-audit-retry") Interlocked.Increment(ref retries);
                if (stage != (betweenRetries ? "before-read-audit-retry" : "before-read-audit-attempt")) return;
                reached.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(8)))
                    throw new TimeoutException("Replay permission race barrier was not released");
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(L12CorrelationIds.HeaderName, correlation);
            pending = client.SendAsync(request);
            Assert.True(reached.Wait(TimeSpan.FromSeconds(6)));
            // The handler has already reconstructed a real page and authenticated the
            // reader. For the retry case, the first attempt hit a real SQLite writer.
            transaction?.Rollback();
            Assert.True(f.Store.SetRole(f.Admin, reader.Id, "player"));
            resume.Set();
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            using var error = JsonDocument.Parse(text);
            Assert.Equal("forbidden", error.RootElement.GetProperty("code").GetString());
            Assert.Equal(correlation, error.RootElement.GetProperty("correlationId").GetString());
            Assert.False(error.RootElement.TryGetProperty("items", out _));
            Assert.DoesNotContain("synthetic-first", text);
            Assert.DoesNotContain("synthetic-second", text);
            Assert.DoesNotContain("stateHash", text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, Volatile.Read(ref attempts));
            // After-read revocation fails the revision check before retrying; between-
            // retries revocation has already reached the actual busy retry barrier.
            Assert.Equal(1, Volatile.Read(ref retries));
            Assert.DoesNotContain(f.Store.AdminAudit(), row => row.CorrelationId == correlation
                && row.Action == "read-replay-page");
        }
        finally
        {
            resume.Set();
            transaction?.Dispose(); blocker?.Dispose();
            if (pending is not null)
                using (await pending.WaitAsync(TimeSpan.FromSeconds(8))) { }
            f.Store.StorageFailureInjector = null;
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ActualAdminReplayPageStaleConflictLogsAuthorityAndOneAttemptWithoutReturningAPage()
    {
        await using var replay = await CommandDeadlineClockFixture.CreateAsync(0, 0);
        await CompleteReplayAsync(replay);
        using var f = new RankedIdentityFixture();
        var token = f.Store.Login("Admin", "L12master").Token!;
        var stale = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        var manager = new L12RoomManager(replay.Catalog, replay.Recorder, stale);
        await using var server = new L12WebSocketServer(manager, replay.Recorder, stale, replay.Catalog);
        await server.StartAsync(0);
        using var client = AdminClient(server, token);
        var path = $"/api/admin/matches/{replay.Engine.State.MatchId}/replay?limit=1";
        using var captured = new StringWriter();
        var originalError = Console.Error;
        try
        {
            using (var control = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.OK, control.StatusCode);
            var writer = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
            var registration = writer.Register("readrace", "Password123!");
            Assert.True(registration.Success, registration.Message);
            Console.SetError(captured);
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("storage_conflict", error.RootElement.GetProperty("code").GetString());
            Assert.False(error.RootElement.TryGetProperty("items", out _));
            var correlation = error.RootElement.GetProperty("correlationId").GetString()!;
            Assert.Contains(correlation, captured.ToString());
            Assert.Contains("phase=authority", captured.ToString());
            Assert.Contains("attempts=1", captured.ToString());
            Assert.DoesNotContain("synthetic-first", captured.ToString());
            Assert.DoesNotContain("synthetic-second", captured.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task LocalWriterBusyKeepsOriginalTwoSecondReadBudgetWithoutASecondSqliteAttempt()
    {
        using var f = new RankedIdentityFixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var attempts = 0;
        var retries = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-read-audit-attempt") Interlocked.Increment(ref attempts);
            if (stage == "before-read-audit-retry") Interlocked.Increment(ref retries);
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("local writer barrier");
        };
        var writer = Task.Factory.StartNew(() => f.Store.Register("localwrite", "Password123!"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var watch = Stopwatch.StartNew();
            var error = Assert.Throws<L12PlatformStorageUnavailableException>(() => Read(f));
            Assert.True(Assert.IsType<SqliteException>(error.InnerException).SqliteErrorCode is 5 or 6);
            Assert.Equal(1, Assert.IsType<L12ReadAuditDiagnostic>(error.Data[nameof(L12ReadAuditDiagnostic)]).Attempts);
            Assert.Equal(1, Volatile.Read(ref attempts));
            Assert.Equal(0, Volatile.Read(ref retries));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
            Assert.False(writer.IsCompleted);
        }
        finally
        {
            release.Set();
            var result = await writer.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Success, result.Message);
            f.Store.StorageFailureInjector = null;
        }
        Assert.DoesNotContain(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
        Read(f);
        Assert.Single(f.Store.AdminAudit(), row => row.Target == "synthetic-replay");
    }

    private static SqliteConnection OpenPlatformDatabase(L12PlatformStore store)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, Cache = SqliteCacheMode.Private, DefaultTimeout = 1,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long StorageRevision(L12PlatformStore store)
    {
        using var connection = OpenPlatformDatabase(store);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT storage_revision FROM platform_state WHERE singleton_id=1;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static async Task CompleteReplayAsync(CommandDeadlineClockFixture replay)
    {
        var command = replay.PassCommand();
        var result = replay.Engine.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        await replay.AppendAsync(1, command, result);
        replay.Now = DateTimeOffset.UtcNow;
        replay.Engine.ConcludeByAuthority(0, "synthetic read-audit HTTP regression");
        await replay.Recorder.AppendAuthorityAsync(replay.Engine, 2, "synthetic read-audit HTTP regression");
        await replay.Recorder.CompleteAsync(replay.Engine);
        var healthy = Assert.IsType<L12MatchDetail>(await replay.Recorder.GetMatchAsync(replay.Engine.State.MatchId));
        Assert.Equal(2, healthy.Commands.Count);
        Assert.Equal(replay.Engine.ComputeStateHash(), healthy.Commands[^1].StateHash);
    }

    private static HttpClient AdminClient(L12WebSocketServer server, string token)
    {
        var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
        var client = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
