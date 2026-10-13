using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SessionRevocationBatchTests
{
    [Fact]
    public async Task RealHttpBurstRemainsResponsiveAndReturnsOnlyCommittedRevocations()
    {
        using var fixture = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "burst.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        await using var server = new L12WebSocketServer(manager, recorder, fixture.Store, fixture.Catalog);
        await server.StartAsync(0);
        using var http = new HttpClient { BaseAddress = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri,
            Timeout = TimeSpan.FromSeconds(10) };
        var tokens = Enumerable.Range(0, 69).Select(i =>
        {
            var account = fixture.Store.Register($"webburst{i}", "Password123!");
            Assert.True(account.Success, account.Message);
            return account.Token!;
        }).ToArray();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var commits = 0;
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            if (Interlocked.Increment(ref commits) != 1) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic HTTP burst barrier");
        };
        var requests = tokens.Select(token =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "api/auth/sessions/current");
            request.Headers.Authorization = new("Bearer", token);
            return request;
        }).ToArray();
        var pending = requests.Select(request => http.SendAsync(request)).ToArray();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var health = await http.GetAsync("health").WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(health.IsSuccessStatusCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
            Assert.All(tokens, token => Assert.NotNull(fixture.Store.AuthenticateTokenSession(token)));
        }
        finally { release.Set(); }
        var replies = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.All(replies, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
            Assert.All(tokens, token => Assert.Null(fixture.Store.AuthenticateTokenSession(token)));
            Assert.InRange(commits, 1, 4);
        }
        finally
        {
            fixture.Store.StorageFailureInjector = null;
            foreach (var reply in replies) reply.Dispose();
            foreach (var request in requests) request.Dispose();
        }
    }

    [Fact]
    public async Task SixtyNineDistinctLogoutsShareDurableCommitsWithoutPerRequestBlocking()
    {
        using var fixture = new Fixture();
        var actors = Enumerable.Range(0, 69).Select(i => fixture.Create($"burst{i}")).ToArray();
        var commits = 0;
        var notified = new List<string>();
        fixture.Store.StorageFailureInjector = stage => { if (stage == "before-commit") commits++; };
        fixture.Store.SessionsRevoked += ids => notified.AddRange(ids);
        Task<L12SessionRevocationResult>[] work;
        lock (fixture.QueueGate)
            work = actors.Select(actor => fixture.Store.RevokeOwnSessionAsync(actor, actor.SessionId)).ToArray();
        var results = await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(results, result => Assert.Equal(1, result.RevokedCount));
        Assert.InRange(commits, 1, 2);
        Assert.Equal(69, notified.Distinct().Count());
        var reopened = new L12PlatformStore(fixture.Path);
        Assert.All(actors, actor => { Assert.False(fixture.Store.IsSessionActive(actor.SessionId));
            Assert.False(reopened.IsSessionActive(actor.SessionId)); });
        Assert.Equal(69, reopened.AdminAudit("session", 1000).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BarrierRetainsActiveAuthorityAndWithholdsEveryResultUntilCommit(bool fail)
    {
        using var fixture = new Fixture();
        var first = fixture.Create("commitone");
        var second = fixture.Create("committwo");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var notifications = 0;
        fixture.Store.SessionsRevoked += _ => notifications++;
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic commit barrier");
            if (fail) throw new IOException("synthetic commit failure");
        };
        var work = new[] { fixture.Store.RevokeOwnSessionAsync(first, first.SessionId),
            fixture.Store.RevokeOwnSessionAsync(second, second.SessionId) };
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.All(work, task => Assert.False(task.IsCompleted));
            Assert.True(fixture.Store.IsSessionActive(first.SessionId));
            Assert.True(fixture.Store.IsSessionActive(second.SessionId));
            Assert.Equal(0, notifications);
        }
        finally { release.Set(); }
        if (fail) await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(() => Task.WhenAll(work));
        else Assert.All(await Task.WhenAll(work), result => Assert.Equal(1, result.RevokedCount));
        fixture.Store.StorageFailureInjector = null;
        Assert.Equal(fail, fixture.Store.IsSessionActive(first.SessionId));
        Assert.Equal(fail, new L12PlatformStore(fixture.Path).IsSessionActive(second.SessionId));
        Assert.Equal(fail ? 0 : 1, notifications);
        if (fail)
        {
            Assert.Empty(fixture.Store.AdminAudit("session"));
            Assert.Equal(1, (await fixture.Store.RevokeOwnSessionAsync(first, first.SessionId)).RevokedCount);
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expired")]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("permission")]
    public async Task QueuedCallerIsRevalidatedAndCancelledWorkDoesNotMutate(string mode)
    {
        using var fixture = new Fixture();
        var actor = fixture.Create("staleactor");
        using var cancel = new CancellationTokenSource();
        Task<L12SessionRevocationResult> task;
        lock (fixture.Gate)
        {
            task = fixture.Store.RevokeOwnSessionAsync(actor, actor.SessionId, cancellationToken: cancel.Token);
            if (mode == "cancel") cancel.Cancel();
            else if (mode == "disabled") fixture.Store.SetAccountDisabled(fixture.Admin, actor.Account.Id, true, "synthetic disable", new("batch-test"), true);
            else if (mode == "revoked") fixture.Store.RevokeOwnSession(actor, actor.SessionId);
            else if (mode == "permission") fixture.Store.SetRole(fixture.Admin, actor.Account.Id, "admin");
            else
            {
                var data = typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(fixture.Store)!;
                var rows = (System.Collections.IEnumerable)data.GetType().GetProperty("Sessions")!.GetValue(data)!;
                var row = rows.Cast<object>().Single(r => (string)r.GetType().GetProperty("Id")!.GetValue(r)! == actor.SessionId);
                row.GetType().GetProperty("ExpiresAt")!.SetValue(row, DateTimeOffset.UtcNow.AddSeconds(-1));
            }
        }
        if (mode == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
        // A subsequent admitted command also proves the drain did not become stuck.
        var next = fixture.Create("nextactor");
        Assert.Equal(1, (await fixture.Store.RevokeOwnSessionAsync(next, next.SessionId)).RevokedCount);
        if (mode == "cancel") Assert.True(fixture.Store.IsSessionActive(actor.SessionId));
    }

    [Fact]
    public async Task ForeignSessionIsNeverRevokedAndOthersKeepsCurrentSession()
    {
        using var fixture = new Fixture();
        var actor = fixture.Create("ownedactor");
        var foreign = fixture.Create("foreigner");
        var newer = fixture.Store.AuthenticateTokenSession(fixture.Store.Login("ownedactor", "Password123!").Token)!;
        Assert.False((await fixture.Store.RevokeOwnSessionAsync(actor, foreign.SessionId)).Found);
        Assert.True(fixture.Store.IsSessionActive(foreign.SessionId));
        var others = await fixture.Store.RevokeOwnSessionsAsync(newer, others: true);
        Assert.Equal(new[] { actor.SessionId }, others.RevokedSessionIds);
        Assert.True(fixture.Store.IsSessionActive(newer.SessionId));
        Assert.Equal(1, (await fixture.Store.RevokeOwnSessionsAsync(newer)).RevokedCount);
    }

    [Fact]
    public async Task BoundedAdmissionRejectsExcessAndDrainsAllAdmittedRequests()
    {
        using var fixture = new Fixture();
        var actor = fixture.Create("boundactor");
        Task<L12SessionRevocationResult>[] tasks;
        lock (fixture.Gate)
        {
            tasks = Enumerable.Range(0, 128).Select(_ => fixture.Store.RevokeOwnSessionAsync(actor, "missing")).ToArray();
            Assert.Throws<L12PlatformStorageUnavailableException>(() => { _ = fixture.Store.RevokeOwnSessionAsync(actor, "missing"); });
        }
        Assert.All(await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)), result => Assert.False(result.Found));
        Assert.Equal(1, (await fixture.Store.RevokeOwnSessionAsync(actor, actor.SessionId)).RevokedCount);
    }

    [Fact]
    public async Task DuplicateWithinAtomicBatchIsIdempotentAndNotificationOccursOnce()
    {
        using var fixture = new Fixture();
        var actor = fixture.Create("duplicate");
        var notified = new List<string>();
        fixture.Store.SessionsRevoked += ids => notified.AddRange(ids);
        Task<L12SessionRevocationResult>[] work;
        lock (fixture.QueueGate) work = new[] { fixture.Store.RevokeOwnSessionAsync(actor, actor.SessionId),
            fixture.Store.RevokeOwnSessionAsync(actor, actor.SessionId) };
        var results = await Task.WhenAll(work);
        Assert.Equal(1, results.Sum(result => result.RevokedCount));
        Assert.Single(notified);
        Assert.Equal(2, fixture.Store.AdminAudit("session").Count);
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-revoke-batch", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal object Gate => typeof(L12PlatformStore).GetField("_gate", Private)!.GetValue(Store)!;
        internal object QueueGate => typeof(L12PlatformStore).GetField("_sessionRevocationQueueGate", Private)!.GetValue(Store)!;
        internal Fixture() { Store = new(Path); Admin = Store.Login("Admin", "L12master").Account!; }
        internal L12AuthenticatedSession Create(string name)
        {
            var created = Store.Register(name, "Password123!");
            Assert.True(created.Success, created.Message);
            return Store.AuthenticateTokenSession(created.Token)!;
        }
        public void Dispose() { Store.StorageFailureInjector = null; SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
    }
}
