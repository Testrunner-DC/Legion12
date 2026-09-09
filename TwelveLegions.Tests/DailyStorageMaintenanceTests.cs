using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DailyStorageMaintenanceTests
{
    private static string Database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-daily-cleanup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "matches.db");
    }

    private static MatchRecorder Recorder(string path) => new(path)
    {
        StorageCleanupTime = new TimeOnly(4, 0),
        StorageCleanupBudget = TimeSpan.FromSeconds(1)
    };

    [Fact]
    public async Task SharedBudgetConsumersRotateAndRemainSerialEvenAfterOneFailure()
    {
        var now = new DateTimeOffset(2026,9,10,4,0,0,TimeSpan.FromHours(8));
        var firsts = new HashSet<int>();
        for (var day=0;day<4;day++)
        {
            var calls = new List<int>();
            var running = 0;
            var steps = Enumerable.Range(0,4).Select<int,(string Name,Func<CancellationToken,Task> Run)>(id =>
                ($"fixture-{id}",async token =>
                {
                    Assert.Equal(1,Interlocked.Increment(ref running));
                    calls.Add(id); await Task.Yield();
                    Assert.Equal(0,Interlocked.Decrement(ref running));
                    if(id==1)throw new IOException("fixture slice failure");
                })).ToArray();
            await MatchRecorder.RunStorageSlicesAsync(now.AddDays(day),steps,CancellationToken.None);
            Assert.Equal(4,calls.Count); Assert.Equal(4,calls.Distinct().Count()); firsts.Add(calls[0]);
        }
        Assert.Equal(4,firsts.Count);
    }

    [Fact]
    public async Task OnlyFourAmWindowRunsAndRestartCannotRunSameDayAgain()
    {
        var path = Database();
        var start = new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.FromHours(8));
        var count = 0;
        Task Work(CancellationToken _) { count++; return Task.CompletedTask; }
        await using (var recorder = Recorder(path))
        {
            Assert.False(await recorder.RunDailyStorageMaintenanceIfDueAsync(Work, start.AddSeconds(-1)));
            Assert.False(File.Exists(path));
            Assert.True(await recorder.RunDailyStorageMaintenanceIfDueAsync(Work, start));
            Assert.False(await recorder.RunDailyStorageMaintenanceIfDueAsync(Work, start.AddMinutes(1)));
        }
        await using var restarted = Recorder(path);
        Assert.False(await restarted.RunDailyStorageMaintenanceIfDueAsync(Work, start.AddMinutes(2)));
        Assert.True(await restarted.RunDailyStorageMaintenanceIfDueAsync(Work, start.AddDays(1)));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MissedWindowDoesNotScanOnStartupOrCatchUpDuringPeakHours()
    {
        var path = Database();
        var start = new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.FromHours(8));
        await using var recorder = Recorder(path);
        Task Work(CancellationToken _) => throw new InvalidOperationException("must not scan");
        Assert.False(await recorder.RunDailyStorageMaintenanceIfDueAsync(Work, start.AddMinutes(15)));
        Assert.False(await recorder.RunDailyStorageMaintenanceIfDueAsync(Work, start.AddHours(8)));
        Assert.False(File.Exists(path));
        Assert.Equal(start.AddDays(1).ToUniversalTime(), recorder.NextStorageCleanupUtc(start));
    }

    [Fact]
    public async Task WeeklySandboxSweepDoesNotDriftPastNextWeeksDailyWindow()
    {
        var path = Database();
        var origin = new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.FromHours(8));
        await using var recorder = new MatchRecorder(path, () => origin)
        {
            StorageCleanupTime = new TimeOnly(4, 0)
        };
        await recorder.InitializeAsync();
        var first = await recorder.RunSandboxReplayCleanupIfDueAsync(utcNow: origin.AddDays(7).AddMinutes(3));
        Assert.True(first.Ran);
        Assert.Equal(origin.AddDays(14).ToUniversalTime(), first.NextRunUtc);
        Assert.True((await recorder.RunSandboxReplayCleanupIfDueAsync(utcNow: origin.AddDays(14))).Ran);
    }

    [Fact]
    public async Task ChangingWindowOrClockRollbackCannotRunTwiceOnSameLocalDay()
    {
        var path = Database();
        var start = new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.FromHours(8));
        await using var first = Recorder(path);
        Assert.True(await first.RunDailyStorageMaintenanceIfDueAsync(_ => Task.CompletedTask, start));
        await using var changed = Recorder(path);
        changed.StorageCleanupTime = new TimeOnly(5, 0);
        Assert.False(await changed.RunDailyStorageMaintenanceIfDueAsync(_ => Task.CompletedTask, start.AddHours(1)));
        await using var rolledBack = Recorder(path);
        Assert.False(await rolledBack.RunDailyStorageMaintenanceIfDueAsync(_ => Task.CompletedTask, start.AddDays(-1)));
    }

    [Fact]
    public async Task FailedRunIsRecordedAndNotRetriedByAnotherRecorderUntilNextDay()
    {
        var path = Database();
        var start = new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.FromHours(8));
        await using var recorder = Recorder(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RunDailyStorageMaintenanceIfDueAsync(
            _ => throw new InvalidOperationException("fixture failure"), start));
        await using var second = Recorder(path);
        Assert.False(await second.RunDailyStorageMaintenanceIfDueAsync(_ => Task.CompletedTask, start));
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var inspect = connection.CreateCommand();
        inspect.CommandText = "SELECT outcome FROM storage_daily_maintenance;";
        Assert.Equal("failed", await inspect.ExecuteScalarAsync());
        Assert.True(await second.RunDailyStorageMaintenanceIfDueAsync(_ => Task.CompletedTask, start.AddDays(1)));
    }

    [Fact]
    public async Task ConcurrentRecordersOnlyClaimOnceAndBudgetCancellationLeavesNoRetryLoop()
    {
        var path = Database();
        var start = new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.FromHours(8));
        await using var first = Recorder(path);
        await using var second = Recorder(path);
        var count = 0;
        async Task Work(CancellationToken token)
        {
            Interlocked.Increment(ref count);
            await Task.Delay(Timeout.Infinite, token);
        }
        var results = await Task.WhenAll(first.RunDailyStorageMaintenanceIfDueAsync(Work, start),
            second.RunDailyStorageMaintenanceIfDueAsync(Work, start));
        Assert.Equal(1, results.Count(result => result));
        Assert.Equal(1, count);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var inspect = connection.CreateCommand();
        inspect.CommandText = "SELECT outcome FROM storage_daily_maintenance;";
        Assert.Equal("budget-exhausted", await inspect.ExecuteScalarAsync());
    }
}
