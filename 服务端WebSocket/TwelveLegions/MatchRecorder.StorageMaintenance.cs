using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private static readonly TimeSpan CleanupTimeZone = TimeSpan.FromHours(8);
    private static readonly TimeSpan CleanupStartWindow = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _dailyStorageMaintenanceGate = new(1, 1);
    private long _nextDailyStorageCheckUtcTicks;
    internal TimeOnly StorageCleanupTime { get; set; } = ReadCleanupTime();
    internal TimeSpan StorageCleanupBudget { get; set; } = ReadCleanupBudget();

    private static TimeOnly ReadCleanupTime()
    {
        var value = Environment.GetEnvironmentVariable("L12_STORAGE_CLEANUP_TIME");
        if (string.IsNullOrWhiteSpace(value)) return new TimeOnly(4, 0);
        return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time) ? time
            : throw new InvalidOperationException("L12_STORAGE_CLEANUP_TIME 必须为北京时间 HH:mm");
    }

    private static TimeSpan ReadCleanupBudget()
    {
        var value = Environment.GetEnvironmentVariable("L12_STORAGE_CLEANUP_BUDGET_SECONDS");
        if (string.IsNullOrWhiteSpace(value)) return TimeSpan.FromSeconds(10);
        return int.TryParse(value, out var seconds) && seconds is >= 1 and <= 60
            ? TimeSpan.FromSeconds(seconds)
            : throw new InvalidOperationException("L12_STORAGE_CLEANUP_BUDGET_SECONDS 必须为 1–60");
    }

    internal DateTimeOffset NextStorageCleanupUtc(DateTimeOffset now)
    {
        var local = now.ToOffset(CleanupTimeZone);
        var start = new DateTimeOffset(local.Date + StorageCleanupTime.ToTimeSpan(), CleanupTimeZone);
        return (start > now ? start : start.AddDays(1)).ToUniversalTime();
    }

    internal static async Task RunStorageSlicesAsync(DateTimeOffset now,
        IReadOnlyList<(string Name, Func<CancellationToken,Task> Run)> work,
        CancellationToken cancellationToken)
    {
        if (work.Count == 0) return;
        var first = DateOnly.FromDateTime(now.ToOffset(CleanupTimeZone).Date).DayNumber % work.Count;
        // Rotate the first consumer of the fixed total budget; a busy replay task must not
        // permanently starve audit expiry (or vice versa). Never parallelize SQLite writers.
        for (var step = 0; step < work.Count; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = work[(first + step) % work.Count];
            using var slice = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            slice.CancelAfter(TimeSpan.FromSeconds(3));
            try { await item.Run(slice.Token); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { Console.Error.WriteLine($"{item.Name} deferred to next daily window: {error.Message}"); }
        }
    }

    // 正常定时调用只有时钟比较；只在低峰窗口访问计划表。先持久化当天领取记录，
    // 重启/重复调用/另一个 Recorder 都不能同日重复启动。错过窗口留到次日，不在启动时补扫。
    internal async Task<bool> RunDailyStorageMaintenanceIfDueAsync(
        Func<CancellationToken, Task> work, DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = (utcNow ?? _utcNow()).ToUniversalTime();
        if (now.UtcDateTime.Ticks < Volatile.Read(ref _nextDailyStorageCheckUtcTicks)) return false;
        var local = now.ToOffset(CleanupTimeZone);
        var start = new DateTimeOffset(local.Date + StorageCleanupTime.ToTimeSpan(), CleanupTimeZone)
            .ToUniversalTime();
        if (now < start || now >= start.Add(CleanupStartWindow))
        {
            Volatile.Write(ref _nextDailyStorageCheckUtcTicks, NextStorageCleanupUtc(now).UtcDateTime.Ticks);
            return false;
        }
        await _dailyStorageMaintenanceGate.WaitAsync(cancellationToken);
        try
        {
            if (now.UtcDateTime.Ticks < Volatile.Read(ref _nextDailyStorageCheckUtcTicks)) return false;
            await using var connection = await OpenWriteConnectionAsync(cancellationToken);
            var schema = connection.CreateCommand();
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS storage_daily_maintenance (
                    singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
                    day_key TEXT NOT NULL, window_utc TEXT NOT NULL, started_utc TEXT NOT NULL,
                    finished_utc TEXT, outcome TEXT NOT NULL);
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
            var claim = connection.CreateCommand();
            claim.CommandText = """
                INSERT INTO storage_daily_maintenance(singleton_id,day_key,window_utc,started_utc,outcome)
                VALUES(1,$day,$window,$now,'running')
                ON CONFLICT(singleton_id) DO UPDATE SET day_key=excluded.day_key,window_utc=excluded.window_utc,
                    started_utc=excluded.started_utc,finished_utc=NULL,outcome='running'
                WHERE storage_daily_maintenance.day_key < excluded.day_key;
                """;
            claim.Parameters.AddWithValue("$window", start.ToString("O"));
            claim.Parameters.AddWithValue("$day", local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            claim.Parameters.AddWithValue("$now", now.ToString("O"));
            var claimed = await claim.ExecuteNonQueryAsync(cancellationToken) == 1;
            Volatile.Write(ref _nextDailyStorageCheckUtcTicks, start.AddDays(1).UtcDateTime.Ticks);
            if (!claimed) return false;

            var outcome = "window-completed";
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(StorageCleanupBudget);
            try { await work(budget.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                outcome = "budget-exhausted";
            }
            catch
            {
                outcome = cancellationToken.IsCancellationRequested ? "interrupted" : "failed";
                throw;
            }
            finally
            {
                var finish = connection.CreateCommand();
                finish.CommandText = """
                    UPDATE storage_daily_maintenance SET finished_utc=$now,outcome=$outcome
                    WHERE singleton_id=1 AND window_utc=$window;
                    """;
                finish.Parameters.AddWithValue("$now", _utcNow().ToUniversalTime().ToString("O"));
                finish.Parameters.AddWithValue("$outcome", outcome);
                finish.Parameters.AddWithValue("$window", start.ToString("O"));
                await finish.ExecuteNonQueryAsync(CancellationToken.None);
                Console.WriteLine($"Daily storage cleanup: windowUtc={start:O};outcome={outcome};nextUtc={start.AddDays(1):O}");
            }
            return true;
        }
        finally { _dailyStorageMaintenanceGate.Release(); }
    }
}
