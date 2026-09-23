using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    private static async Task InitializeGlobalAnalyticsSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS analytics_daily_accounts(
                activity_day TEXT NOT NULL,
                account_id TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                PRIMARY KEY(activity_day,account_id));
            CREATE TABLE IF NOT EXISTS analytics_page_views(
                activity_day TEXT NOT NULL,
                path TEXT NOT NULL,
                views INTEGER NOT NULL,
                last_seen_utc TEXT NOT NULL,
                PRIMARY KEY(activity_day,path));
            CREATE TABLE IF NOT EXISTS analytics_online_samples(
                sample_minute TEXT PRIMARY KEY,
                activity_day TEXT NOT NULL,
                online_count INTEGER NOT NULL,
                observed_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_analytics_daily_accounts_account
                ON analytics_daily_accounts(account_id,activity_day);
            CREATE INDEX IF NOT EXISTS ix_analytics_page_views_day
                ON analytics_page_views(activity_day,path);
            CREATE INDEX IF NOT EXISTS ix_analytics_online_samples_day
                ON analytics_online_samples(activity_day,sample_minute);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task RecordSiteActivityAsync(string? accountId, string? path, int onlineCount,
        CancellationToken cancellationToken = default)
    {
        var now = _utcNow().ToUniversalTime();
        var day = now.ToString("yyyy-MM-dd");
        var minute = now.ToString("yyyy-MM-ddTHH:mm");
        var normalizedPath = NormalizeTelemetryPath(path);
        await using var connection = await OpenWriteConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO analytics_online_samples(sample_minute,activity_day,online_count,observed_utc)
            VALUES($minute,$day,$online,$now)
            ON CONFLICT(sample_minute) DO UPDATE SET
                online_count=MAX(online_count,excluded.online_count),observed_utc=excluded.observed_utc;
            """;
        command.Parameters.AddWithValue("$minute", minute);
        command.Parameters.AddWithValue("$day", day);
        command.Parameters.AddWithValue("$online", Math.Max(0, onlineCount));
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO analytics_daily_accounts(activity_day,account_id,first_seen_utc,last_seen_utc)
                VALUES($day,$account,$now,$now)
                ON CONFLICT(activity_day,account_id) DO UPDATE SET last_seen_utc=excluded.last_seen_utc;
                """;
            command.Parameters.AddWithValue("$day", day);
            command.Parameters.AddWithValue("$account", accountId.Trim());
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (normalizedPath is not null)
        {
            command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO analytics_page_views(activity_day,path,views,last_seen_utc)
                VALUES($day,$path,1,$now)
                ON CONFLICT(activity_day,path) DO UPDATE SET
                    views=views+1,last_seen_utc=excluded.last_seen_utc;
                """;
            command.Parameters.AddWithValue("$day", day);
            command.Parameters.AddWithValue("$path", normalizedPath);
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<L12GlobalAnalyticsReport> ReadGlobalAnalyticsAsync(DateOnly from, DateOnly to,
        IReadOnlyList<L12AccountView> accounts, CancellationToken cancellationToken = default)
    {
        if (from > to) throw new ArgumentException("开始日期必须早于结束日期");
        if (to.DayNumber - from.DayNumber > 365) throw new ArgumentException("单次最多查询366天");
        var historyFrom = from.AddDays(-29);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var activities = new Dictionary<DateOnly, HashSet<string>>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT activity_day,account_id FROM analytics_daily_accounts
            WHERE activity_day >= $from AND activity_day <= $to;
            """;
        command.Parameters.AddWithValue("$from", historyFrom.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var day = DateOnly.Parse(reader.GetString(0));
                if (!activities.TryGetValue(day, out var set)) activities[day] = set = new(StringComparer.OrdinalIgnoreCase);
                set.Add(reader.GetString(1));
            }

        var matchCounts = await ReadDailyLongsAsync(connection, """
            SELECT substr(ended_utc,1,10),COUNT(*) FROM matches
            WHERE ended_utc IS NOT NULL AND error IS NULL AND ended_utc >= $from AND ended_utc < $until
            GROUP BY substr(ended_utc,1,10);
            """, historyFrom, to.AddDays(1), cancellationToken);
        var pvCounts = await ReadDailyLongsAsync(connection, """
            SELECT activity_day,SUM(views) FROM analytics_page_views
            WHERE activity_day >= $from AND activity_day < $until GROUP BY activity_day;
            """, historyFrom, to.AddDays(1), cancellationToken);
        var online = new Dictionary<DateOnly, List<(int Count, string At)>>();
        command = connection.CreateCommand();
        command.CommandText = """
            SELECT activity_day,online_count,observed_utc FROM analytics_online_samples
            WHERE activity_day >= $from AND activity_day <= $to ORDER BY observed_utc;
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var day = DateOnly.Parse(reader.GetString(0));
                if (!online.TryGetValue(day, out var list)) online[day] = list = [];
                list.Add((reader.GetInt32(1), reader.GetString(2)));
            }

        var createdByDay = accounts.GroupBy(account => DateOnly.FromDateTime(account.CreatedAt.UtcDateTime))
            .ToDictionary(group => group.Key, group => group.Select(account => account.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        var days = new List<L12GlobalAnalyticsDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var daily = activities.GetValueOrDefault(day) ?? [];
            var weekly = UnionAccounts(activities, day.AddDays(-6), day);
            var monthly = UnionAccounts(activities, day.AddDays(-29), day);
            var newUsers = daily.Count(account => createdByDay.GetValueOrDefault(day)?.Contains(account) == true);
            var samples = online.GetValueOrDefault(day) ?? [];
            var peak = samples.OrderByDescending(sample => sample.Count).ThenBy(sample => sample.At).FirstOrDefault();
            days.Add(new L12GlobalAnalyticsDay(day.ToString("yyyy-MM-dd"), daily.Count, weekly.Count, monthly.Count,
                SumDaily(matchCounts, day.AddDays(-0), day), SumDaily(matchCounts, day.AddDays(-6), day),
                SumDaily(matchCounts, day.AddDays(-29), day),
                samples.Count == 0 ? 0 : Math.Round(samples.Average(sample => sample.Count), 2),
                peak.Count, peak.At, newUsers, Math.Max(0, daily.Count - newUsers), pvCounts.GetValueOrDefault(day)));
        }
        command = connection.CreateCommand();
        command.CommandText = """
            SELECT path,SUM(views) FROM analytics_page_views
            WHERE activity_day >= $from AND activity_day <= $to
            GROUP BY path ORDER BY SUM(views) DESC,path LIMIT 200;
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        var pages = new List<L12GlobalPageView>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) pages.Add(new(reader.GetString(0), reader.GetInt64(1)));
        return new(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), days, pages);
    }

    private static string? NormalizeTelemetryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var value = path.Trim().Split('?', '#')[0];
        if (!value.StartsWith('/')) return null;
        return value.Length > 120 ? value[..120] : value;
    }

    private static HashSet<string> UnionAccounts(Dictionary<DateOnly, HashSet<string>> source, DateOnly from, DateOnly to)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var day = from; day <= to; day = day.AddDays(1)) result.UnionWith(source.GetValueOrDefault(day) ?? []);
        return result;
    }

    private static long SumDaily(Dictionary<DateOnly, long> source, DateOnly from, DateOnly to)
    {
        long result = 0;
        for (var day = from; day <= to; day = day.AddDays(1)) result += source.GetValueOrDefault(day);
        return result;
    }

    private static async Task<Dictionary<DateOnly, long>> ReadDailyLongsAsync(SqliteConnection connection,
        string sql, DateOnly from, DateOnly until, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$until", until.ToString("yyyy-MM-dd"));
        var result = new Dictionary<DateOnly, long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[DateOnly.Parse(reader.GetString(0))] = reader.GetInt64(1);
        return result;
    }
}
