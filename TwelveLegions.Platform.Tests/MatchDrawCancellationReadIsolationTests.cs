using System.Collections;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class MatchDrawCancellationReadIsolationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public async Task MissingDrawCancellationDoesNotWaitForUnrelatedCommit()
    {
        using var fixture = new RankedIdentityFixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var committed = RankedIdentityTestState.CommittedProjection(fixture.Store);
        var writer = StartHeldWriter(fixture.Store, entered, release);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var cancellation = StartCancellation(fixture.Store, "missing-draw-match");
            await cancellation.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(writer.IsCompleted);
            Assert.Same(committed, RankedIdentityTestState.CommittedProjection(fixture.Store));
        }
        finally
        {
            release.Set();
            Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(5))).Success);
            fixture.Store.StorageFailureInjector = null;
        }
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("expired")]
    [InlineData("cancelled")]
    public async Task TerminalHistoricalDrawCancellationDoesNotWaitOrRewriteHistory(string status)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount($"ht{status[..2]}a");
        var second = fixture.CreateAccount($"ht{status[..2]}b");
        var now = DateTimeOffset.UtcNow;
        var matchId = $"historical-{status}";
        var draw = fixture.Store.CreateMatchDrawRequest($"draw-historical-{status}", matchId,
            "HIST01", "friendly", first, second, "historical terminal control", now);
        MakeTerminal(fixture.Store, draw, second, status, now);
        var before = Assert.Single(fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId));
        Assert.Equal(status, before.Status);
        var history = before.History.Select(row => row.Action).ToArray();

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var committed = RankedIdentityTestState.CommittedProjection(fixture.Store);
        var writer = StartHeldWriter(fixture.Store, entered, release);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var cancellation = StartCancellation(fixture.Store, matchId);
            await cancellation.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(writer.IsCompleted);
            Assert.Same(committed, RankedIdentityTestState.CommittedProjection(fixture.Store));
        }
        finally
        {
            release.Set();
            Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(5))).Success);
            fixture.Store.StorageFailureInjector = null;
        }

        var after = Assert.Single(fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId));
        Assert.Equal(status, after.Status);
        Assert.Equal(history, after.History.Select(row => row.Action));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("accepting")]
    public async Task EveryOpenLegacyDuplicateKeepsCancellationOnTheAuthoritativeLockedPath(
        string openStatus)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("dupopen1");
        var second = fixture.CreateAccount("dupopen2");
        var now = DateTimeOffset.UtcNow;
        var matchId = $"legacy-duplicate-draw-{openStatus}";
        var open = fixture.Store.CreateMatchDrawRequest($"draw-legacy-open-{openStatus}", matchId,
            "DUP001", "friendly", first, second, "legacy open row", now);
        if (openStatus == "accepting")
            fixture.Store.BeginMatchDrawAcceptance(matchId, open.Id, second, now.AddMilliseconds(500));
        var terminal = fixture.Store.CreateMatchDrawRequest($"draw-legacy-terminal-{openStatus}",
            $"legacy-duplicate-terminal-{openStatus}", "DUP002", "friendly", first, second,
            "legacy later terminal row", now.AddSeconds(1));
        fixture.Store.ResolveMatchDrawRequest(terminal.MatchId, terminal.Id, second, false,
            now.AddSeconds(2));
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var terminalRow = DrawRows(fixture.Store).Cast<object>().Single(row =>
                (string)row.GetType().GetProperty("Id")!.GetValue(row)! == terminal.Id);
            terminalRow.GetType().GetProperty("MatchId")!.SetValue(terminalRow, matchId);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellationStarted = new ManualResetEventSlim();
        var writer = StartHeldWriter(fixture.Store, entered, release);
        Task? cancellation = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            cancellation = Task.Factory.StartNew(() =>
            {
                cancellationStarted.Set();
                fixture.Store.CancelOpenMatchDrawRequests(matchId, now.AddSeconds(3),
                    "legacy duplicate terminal");
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(cancellationStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.NotSame(cancellation, await Task.WhenAny(cancellation, Task.Delay(200)));
            Assert.False(writer.IsCompleted);
        }
        finally
        {
            release.Set();
            Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(5))).Success);
            fixture.Store.StorageFailureInjector = null;
            if (cancellation is not null) await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var rows = fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId);
        Assert.Equal(2, rows.Count);
        var cancelled = Assert.Single(rows, row => row.Id == open.Id);
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Single(cancelled.History, row => row.Action == "cancelled");
        Assert.Equal("rejected", Assert.Single(rows, row => row.Id == terminal.Id).Status);
    }

    [Fact]
    public void OwningTransactionCancelsItsStagedOpenDrawBeforePublication()
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("stageda");
        var second = fixture.CreateAccount("stagedb");
        var now = DateTimeOffset.UtcNow;
        const string matchId = "staged-draw-cancellation";

        fixture.Store.ExecuteAdminTransaction(() =>
        {
            fixture.Store.CreateMatchDrawRequest("draw-staged-cancel", matchId,
                "STAGE1", "friendly", first, second, "staged transaction", now);
            fixture.Store.CancelOpenMatchDrawRequests(matchId, now.AddSeconds(1),
                "staged terminal");
            return true;
        });

        var row = Assert.Single(fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId));
        Assert.Equal("cancelled", row.Status);
        // The existing administrative projection deliberately returns newest history first.
        Assert.Equal(new[] { "cancelled", "requested" }, row.History.Select(item => item.Action));
    }

    [Fact]
    public async Task UnavailableCommittedIndexFallsBackToTheAuthoritativeGate()
    {
        using var fixture = new RankedIdentityFixture();
        var committedField = typeof(L12PlatformStore).GetField("_committedSessionActivity",
            PrivateInstance)!;
        var original = committedField.GetValue(fixture.Store)!;
        var unavailable = typeof(L12PlatformStore).GetField("UnavailableSessionActivity",
            PrivateStatic)!.GetValue(null)!;
        var gate = RankedIdentityTestState.Gate(fixture.Store);
        using var gateEntered = new ManualResetEventSlim();
        using var gateRelease = new ManualResetEventSlim();
        using var cancellationStarted = new ManualResetEventSlim();
        Task? cancellation = null;
        var holder = Task.Factory.StartNew(() =>
        {
            lock (gate)
            {
                gateEntered.Set();
                Assert.True(gateRelease.Wait(TimeSpan.FromSeconds(5)));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            committedField.SetValue(fixture.Store, unavailable);
            Assert.True(gateEntered.Wait(TimeSpan.FromSeconds(1)));
            cancellation = Task.Factory.StartNew(() =>
            {
                cancellationStarted.Set();
                fixture.Store.CancelOpenMatchDrawRequests("unknown-index-match",
                    DateTimeOffset.UtcNow, "unknown index fallback");
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(cancellationStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.NotSame(cancellation, await Task.WhenAny(cancellation, Task.Delay(200)));
        }
        finally
        {
            gateRelease.Set();
            await holder.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancellation is not null) await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
            committedField.SetValue(fixture.Store, original);
        }
    }

    [Fact]
    public void FailedOpenCancellationRollsBackAndRetryAddsOneCancellationHistoryEntry()
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("rollbacka");
        var second = fixture.CreateAccount("rollbackb");
        var now = DateTimeOffset.UtcNow;
        const string matchId = "rollback-open-draw";
        fixture.Store.CreateMatchDrawRequest("draw-rollback-open", matchId,
            "ROLL01", "friendly", first, second, "rollback control", now);
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic cancellation rollback");
        };
        try
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                fixture.Store.CancelOpenMatchDrawRequests(matchId, now.AddSeconds(1), "terminal"));
        }
        finally { fixture.Store.StorageFailureInjector = null; }

        var rolledBack = Assert.Single(fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId));
        Assert.Equal("pending", rolledBack.Status);
        Assert.DoesNotContain(rolledBack.History, row => row.Action == "cancelled");
        fixture.Store.CancelOpenMatchDrawRequests(matchId, now.AddSeconds(2), "terminal retry");
        var completed = Assert.Single(fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId));
        Assert.Equal("cancelled", completed.Status);
        Assert.Single(completed.History, row => row.Action == "cancelled");
    }

    [Fact]
    public void MatchIdComparisonRemainsOrdinal()
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("ordinala");
        var second = fixture.CreateAccount("ordinalb");
        var now = DateTimeOffset.UtcNow;
        const string matchId = "Ordinal-Match-Id";
        fixture.Store.CreateMatchDrawRequest("draw-ordinal-match", matchId,
            "ORD001", "friendly", first, second, "ordinal identity", now);

        fixture.Store.CancelOpenMatchDrawRequests(matchId.ToLowerInvariant(), now.AddSeconds(1),
            "wrong case");
        Assert.Equal("pending", Assert.Single(fixture.Store.MatchDrawRequests(
            fixture.Admin, search: matchId)).Status);
        fixture.Store.CancelOpenMatchDrawRequests(matchId, now.AddSeconds(2), "exact case");
        Assert.Equal("cancelled", Assert.Single(fixture.Store.MatchDrawRequests(
            fixture.Admin, search: matchId)).Status);
    }

    private static Task<(bool Success, string Message, L12AccountView? Account, string? Token)>
        StartHeldWriter(L12PlatformStore store, ManualResetEventSlim entered,
            ManualResetEventSlim release)
    {
        store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("bounded unrelated commit barrier");
        };
        return Task.Factory.StartNew(() => store.Register($"cw{Guid.NewGuid():N}"[..10],
                "Password123!"), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static Task StartCancellation(L12PlatformStore store, string matchId)
        => Task.Factory.StartNew(() => store.CancelOpenMatchDrawRequests(matchId,
                DateTimeOffset.UtcNow, "terminal control"), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void MakeTerminal(L12PlatformStore store, L12MatchDrawRequestView draw,
        L12AccountView responder, string status, DateTimeOffset now)
    {
        switch (status)
        {
            case "accepted":
                store.ResolveMatchDrawRequest(draw.MatchId, draw.Id, responder, true,
                    now.AddSeconds(1));
                break;
            case "rejected":
                store.ResolveMatchDrawRequest(draw.MatchId, draw.Id, responder, false,
                    now.AddSeconds(1));
                break;
            case "expired":
                Assert.Throws<L12MatchGovernanceConflictException>(() =>
                    store.ResolveMatchDrawRequest(draw.MatchId, draw.Id, responder, false,
                        now + L12PlatformStore.MatchDrawRequestLifetime + TimeSpan.FromSeconds(1)));
                break;
            case "cancelled":
                store.CancelOpenMatchDrawRequests(draw.MatchId, now.AddSeconds(1),
                    "terminal setup");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private static IList DrawRows(L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
        return (IList)data.GetType().GetProperty("MatchDrawRequests")!.GetValue(data)!;
    }
}
