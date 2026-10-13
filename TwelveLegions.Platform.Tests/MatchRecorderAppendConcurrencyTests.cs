using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class MatchRecorderAppendConcurrencyTests
{
    [Fact]
    public async Task ExistingDisposeContractDoesNotTurnDurableHolderOrWaiterIntoFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreateConcludedMatchAsync(301);
        var next = await fixture.CreateConcludedMatchAsync(302);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-match-command-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic dispose barrier");
        };
        var holder = Task.Run(() => fixture.AppendAsync(first));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        var waiter = fixture.AppendAsync(next);
        try
        {
            Assert.False(waiter.IsCompleted);
            await fixture.Recorder.DisposeAsync();
        }
        finally { release.Set(); }
        await Task.WhenAll(holder, waiter).WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Recorder.StorageFailureInjector = null;
        await fixture.AssertRecoveredOnceAsync(first);
        await fixture.AssertRecoveredOnceAsync(next);
    }

    [Fact]
    public async Task ContendedAppendAndCompletionReturnIncompleteTasksWithoutBlockingCaller()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.CreateConcludedMatchAsync(1);
        var second = await fixture.CreateConcludedMatchAsync(2);
        var completionTarget = await fixture.CreateConcludedMatchAsync(3);
        await fixture.AppendAsync(completionTarget);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancelFailsafe = new ManualResetEventSlim();
        var held = 0;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (!string.Equals(stage, "before-match-command-commit", StringComparison.Ordinal)
                || Interlocked.CompareExchange(ref held, 1, 0) != 0)
                return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("synthetic append holder was not released");
        };

        var firstAppend = Task.Run(() => fixture.AppendAsync(first));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failsafe = new Thread(() =>
        {
            if (!cancelFailsafe.Wait(TimeSpan.FromSeconds(2))) release.Set();
        })
        {
            IsBackground = true,
            Name = "match-recorder-append-test-failsafe",
        };
        failsafe.Start();

        Task<bool> completion;
        Task secondAppend;
        TimeSpan callElapsed;
        bool completionFinishedBeforeRelease;
        bool appendFinishedBeforeRelease;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            completion = fixture.Recorder.CompleteAsync(completionTarget.Engine);
            secondAppend = fixture.AppendAsync(second);
            stopwatch.Stop();
            callElapsed = stopwatch.Elapsed;
            completionFinishedBeforeRelease = completion.IsCompleted;
            appendFinishedBeforeRelease = secondAppend.IsCompleted;
            release.Set();
            await Task.WhenAll(firstAppend, completion, secondAppend).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.Set();
            cancelFailsafe.Set();
            failsafe.Join();
            fixture.Recorder.StorageFailureInjector = null;
        }

        Assert.True(callElapsed < TimeSpan.FromSeconds(1),
            $"contended persistence blocked its caller for {callElapsed.TotalMilliseconds:F0} ms");
        Assert.False(completionFinishedBeforeRelease);
        Assert.False(appendFinishedBeforeRelease);
        Assert.True(await completion);
        await fixture.AssertRecoveredOnceAsync(first);
        await fixture.AssertRecoveredOnceAsync(second);
        await fixture.AssertCompletedAsync(completionTarget);
    }

    [Fact]
    public async Task ThirtyFriendlyRoomsAppendRecoverAndDeduplicateIndependently()
    {
        await using var fixture = await Fixture.CreateAsync();
        var matches = new List<PreparedMatch>();
        for (var index = 0; index < 30; index++)
            matches.Add(await fixture.CreateConcludedMatchAsync(100 + index));

        await RunTogetherAsync(matches.Select(match => (Func<Task>)(async () =>
        {
            await fixture.AppendAsync(match);
            await Task.Yield();
            Assert.True(await fixture.Recorder.CompleteAsync(match.Engine));
        }))).WaitAsync(TimeSpan.FromSeconds(30));
        await RunTogetherAsync(matches.Select(match => (Func<Task>)(async () =>
        {
            await fixture.AppendAsync(match);
            Assert.False(await fixture.Recorder.CompleteAsync(match.Engine));
        })))
            .WaitAsync(TimeSpan.FromSeconds(30));

        foreach (var match in matches)
        {
            await fixture.AssertRecoveredOnceAsync(match);
            await fixture.AssertCompletedAsync(match);
        }
    }

    [Fact]
    public async Task FailedAppendOrCompletionReleasesGateAndLeavesOtherRoomWritable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failed = await fixture.CreateConcludedMatchAsync(201);
        var next = await fixture.CreateConcludedMatchAsync(202);
        var injected = 0;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (string.Equals(stage, "before-match-command-commit", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref injected, 1, 0) == 0)
                throw new IOException("synthetic append commit failure");
        };

        await Assert.ThrowsAsync<IOException>(() => fixture.AppendAsync(failed));
        Assert.Empty((await fixture.Recorder.GetMatchAsync(failed.Engine.State.MatchId))!.Commands);

        fixture.Recorder.StorageFailureInjector = null;
        await fixture.AppendAsync(next).WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.AppendAsync(failed).WaitAsync(TimeSpan.FromSeconds(5));

        var completionInjected = 0;
        fixture.Recorder.StorageFailureInjector = stage =>
        {
            if (string.Equals(stage, "before-match-complete-commit", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref completionInjected, 1, 0) == 0)
                throw new IOException("synthetic match completion failure");
        };
        await Assert.ThrowsAsync<IOException>(() => fixture.Recorder.CompleteAsync(failed.Engine));
        Assert.Null((await fixture.Recorder.GetMatchAsync(failed.Engine.State.MatchId))!.Match.EndedUtc);

        fixture.Recorder.StorageFailureInjector = null;
        Assert.True(await fixture.Recorder.CompleteAsync(next.Engine).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await fixture.Recorder.CompleteAsync(failed.Engine).WaitAsync(TimeSpan.FromSeconds(5)));

        await fixture.AssertRecoveredOnceAsync(next);
        await fixture.AssertRecoveredOnceAsync(failed);
        await fixture.AssertCompletedAsync(next);
        await fixture.AssertCompletedAsync(failed);
    }

    private static Task RunTogetherAsync(IEnumerable<Func<Task>> operations)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operations.Select(async operation =>
        {
            await start.Task;
            await operation();
        }).ToArray();
        start.SetResult();
        return Task.WhenAll(tasks);
    }

    internal sealed record PreparedMatch(L12GameEngine Engine, int Index);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly L12Catalog _catalog;

        private Fixture(string root, L12Catalog catalog, MatchRecorder recorder)
        {
            _root = root;
            _catalog = catalog;
            Recorder = recorder;
        }

        internal MatchRecorder Recorder { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(),
                "l12-recorder-append-concurrency-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var catalog = L12Catalog.Load(
                Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            recorder.AttachCatalog(catalog);
            await recorder.InitializeAsync();
            return new Fixture(root, catalog, recorder);
        }

        internal async Task<PreparedMatch> CreateConcludedMatchAsync(int index)
        {
            var engine = new L12GameEngine(_catalog,
                $"append-room-{index}-{Guid.NewGuid():N}", $"A{index:000}", 720900 + index,
                [$"player-{index}-a", $"player-{index}-b"], [0, 1], skipPreparation: true,
                stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
            await Recorder.StartAsync(engine, "friendly", $"account-{index}-a", $"account-{index}-b");
            engine.ConcludeByAuthority(0, Reason(index));
            return new PreparedMatch(engine, index);
        }

        internal Task AppendAsync(PreparedMatch match)
            => Recorder.AppendAsync(match.Engine, 1, -1,
                JsonSerializer.Serialize(new
                {
                    type = "authorityConclusion",
                    reason = Reason(match.Index),
                    winner = match.Engine.State.Winner,
                    agreedDraw = match.Engine.State.EndedByAgreedDraw,
                }), CommandResult.Ok(), RequestId(match.Index));

        internal async Task AssertRecoveredOnceAsync(PreparedMatch match)
        {
            var expectedHash = match.Engine.ComputeStateHash();
            var detail = Assert.IsType<L12MatchDetail>(
                await Recorder.GetMatchAsync(match.Engine.State.MatchId));
            var command = Assert.Single(detail.Commands);
            Assert.Equal(1, command.Sequence);
            Assert.Equal(expectedHash, command.StateHash);

            var recovered = Assert.IsType<L12JournalRecoveryState>(
                await Recorder.LoadJournalEngineAsync(match.Engine.State.MatchId));
            Assert.Equal(1, recovered.CommandSequence);
            Assert.Equal(expectedHash, recovered.Engine.ComputeStateHash());
            var request = Assert.Single(recovered.ProcessedRequests);
            Assert.Equal(-1, request.PlayerIndex);
            Assert.Equal(RequestId(match.Index), request.RequestId);
            Assert.True(request.Accepted);
        }

        internal async Task AssertCompletedAsync(PreparedMatch match)
        {
            var detail = Assert.IsType<L12MatchDetail>(
                await Recorder.GetMatchAsync(match.Engine.State.MatchId));
            Assert.NotNull(detail.Match.EndedUtc);
            Assert.Equal(0, detail.Match.Winner!.Value);
            Assert.Equal(match.Engine.ComputeStateHash(), detail.Match.FinalHash);
        }

        public async ValueTask DisposeAsync()
        {
            Recorder.StorageFailureInjector = null;
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A failed cleanup must not hide persistence assertions on Windows.
            }
        }

        private static string Reason(int index) => $"synthetic append gate conclusion {index}";
        private static string RequestId(int index) => $"append-gate-request-{index}";
    }
}
