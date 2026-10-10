using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

[CollectionDefinition(LongChainJournalPerformanceCollection.Name, DisableParallelization = true)]
public sealed class LongChainJournalPerformanceCollection
    : ICollectionFixture<LongChainJournalPerformanceFixture>
{
    public const string Name = "LC-03C long-chain journal performance";
}

public sealed class LongChainJournalPerformanceFixture
{
    internal static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
}

[Collection(LongChainJournalPerformanceCollection.Name)]
public sealed class LongChainJournalPerformanceBudgetTests
{
    private const double GrowthRatioLimit = 6.0;
    private const double TrialSecondsLimit = 60.0;
    private const double RecoveryMillisecondsLimit = 30_000.0;
    private static readonly int[] CommandCounts = [64, 256, 1024];
    private static readonly int[] Seeds = [2026092701, 2026092702, 2026092703];
    private readonly ITestOutputHelper _output;

    public LongChainJournalPerformanceBudgetTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    [Trait("L12Evidence", "lc03c-long-chain-performance-budget")]
    public async Task FourfoldJournalGrowthAndWorstCaseRecoveryStayWithinBudget()
    {
        await RunTrialAsync(32, 2026092700, warmup: true);

        var tiers = new List<ScaleMedian>();
        foreach (var commandCount in CommandCounts)
        {
            var trials = new List<ScaleTrial>();
            foreach (var seed in Seeds)
            {
                var trial = await RunTrialAsync(commandCount, seed, warmup: false);
                trials.Add(trial);
                Assert.True(trial.TotalMilliseconds <= TrialSecondsLimit * 1000,
                    $"LC-03C 长档样本超出 {TrialSecondsLimit:F0}s：{JsonSerializer.Serialize(trial)}");
            }

            var median = ScaleMedian.From(trials);
            tiers.Add(median);
            _output.WriteLine(JsonSerializer.Serialize(median));
            Assert.True(median.RecoveryMilliseconds <= RecoveryMillisecondsLimit,
                $"LC-03C 恢复绝对耗时超限：{JsonSerializer.Serialize(median)}");
        }

        for (var index = 1; index < tiers.Count; index++)
        {
            var previous = tiers[index - 1];
            var current = tiers[index];
            Assert.Equal(previous.CommandCount * 4, current.CommandCount);
            AssertLinearGrowth("journal bytes", previous.JournalBytes, current.JournalBytes,
                previous, current);
            AssertLinearGrowth("checkpoint bytes", previous.CheckpointBytes, current.CheckpointBytes,
                previous, current);
            AssertLinearGrowth("database bytes", previous.DatabaseBytes, current.DatabaseBytes,
                previous, current);
            AssertLinearGrowth("recovery milliseconds",
                Math.Max(1.0, previous.RecoveryMilliseconds), current.RecoveryMilliseconds,
                previous, current);
        }
    }

    private async Task<ScaleTrial> RunTrialAsync(int commandCount, int seed, bool warmup)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-lc03c-performance", Guid.NewGuid().ToString("N"));
        var database = Path.Combine(root, "matches.db");
        var matchId = $"lc03c-{commandCount}-{seed}";
        Directory.CreateDirectory(root);
        var totalWatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long workingSetHighWater = SampleWorkingSet();
        try
        {
            await using var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            recorder.AttachCatalog(LongChainJournalPerformanceFixture.Catalog);
            var engine = new L12GameEngine(LongChainJournalPerformanceFixture.Catalog, matchId,
                $"LC03C-{commandCount}", seed, ["性能甲", "性能乙"], [0, 1],
                skipPreparation: true, stateFormatVersion: 2);
            await recorder.StartAsync(engine, "lc-03c");

            for (var sequence = 1; sequence <= commandCount; sequence++)
            {
                var command = new L12GmCommand("setLife", sequence % 2,
                    Value: 10 + sequence % 17);
                var commandResult = engine.HandleGm(command);
                Assert.True(commandResult.Accepted);
                await recorder.AppendAsync(engine, sequence, -1, JsonSerializer.Serialize(command), commandResult,
                    $"lc03c-{seed}-{sequence:D5}");
                if (sequence % 32 == 0) workingSetHighWater = Math.Max(workingSetHighWater, SampleWorkingSet());
            }

            var expectedState = engine.SerializeFullState();
            var expectedHash = engine.ComputeStateHash();
            var expectedRandom = engine.RandomState;
            var expectedRevision = engine.State.Revision;
            var expectedEventSequence = engine.State.EventSequence;
            var expectedCardFactSequence = engine.CardFactSignalSequence;
            var storage = await ReadStorageMetricsAsync(database, matchId);
            Assert.Equal(commandCount, storage.EventRows);
            Assert.Equal(commandCount / MatchRecorder.CheckpointInterval + 1, storage.CheckpointRows);

            // Keep the initial checkpoint valid and damage every later hash. Recovery must inspect the
            // candidates, fall back to sequence zero, and verify the complete command tail.
            await ExecuteAsync(database, """
                UPDATE match_state_checkpoints
                SET state_hash='lc03c-damaged-' || sequence
                WHERE match_id=$match AND sequence>0;
                """, matchId);

            var firstWatch = Stopwatch.StartNew();
            var first = Assert.IsType<L12JournalRecoveryState>(
                await recorder.LoadJournalEngineAsync(matchId));
            firstWatch.Stop();
            AssertRecovery(first, commandCount, expectedState, expectedHash, expectedRandom,
                expectedRevision, expectedEventSequence, expectedCardFactSequence);

            var continuation = new L12GmCommand("setLife", 0, Value: 37);
            var continuationResult = first.Engine.HandleGm(continuation);
            Assert.True(continuationResult.Accepted);
            await recorder.AppendAsync(first.Engine, commandCount + 1, -1,
                JsonSerializer.Serialize(continuation), continuationResult,
                $"lc03c-{seed}-continue");
            var continuedState = first.Engine.SerializeFullState();
            var continuedHash = first.Engine.ComputeStateHash();
            var continuedRandom = first.Engine.RandomState;
            var continuedRevision = first.Engine.State.Revision;
            var continuedEventSequence = first.Engine.State.EventSequence;
            var continuedCardFactSequence = first.Engine.CardFactSignalSequence;

            var secondWatch = Stopwatch.StartNew();
            var second = Assert.IsType<L12JournalRecoveryState>(
                await recorder.LoadJournalEngineAsync(matchId));
            secondWatch.Stop();
            AssertRecovery(second, commandCount + 1, continuedState, continuedHash, continuedRandom,
                continuedRevision, continuedEventSequence, continuedCardFactSequence);
            Assert.Contains(second.ProcessedRequests,
                request => request.RequestId == $"lc03c-{seed}-continue");
            Assert.Equal(commandCount / MatchRecorder.CheckpointInterval,
                await CountDamagedCheckpointsAsync(database, matchId));

            workingSetHighWater = Math.Max(workingSetHighWater, SampleWorkingSet());
            totalWatch.Stop();
            var result = new ScaleTrial(
                commandCount,
                seed,
                storage.JournalBytes,
                storage.CheckpointBytes,
                StorageFootprintBytes(database),
                storage.CheckpointRows,
                Math.Max(firstWatch.Elapsed.TotalMilliseconds, secondWatch.Elapsed.TotalMilliseconds),
                totalWatch.Elapsed.TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
                workingSetHighWater,
                engine.SerializeFullState().Length,
                first.ProcessedRequests.Count,
                second.ProcessedRequests.Count);
            if (!warmup) _output.WriteLine(JsonSerializer.Serialize(result));
            return result;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A transient Windows SQLite handle must not hide the measurement result.
            }
            catch (UnauthorizedAccessException)
            {
                // Same boundary as the existing persistence fixtures.
            }
        }
    }

    private static void AssertRecovery(L12JournalRecoveryState recovery, long expectedSequence,
        string expectedState, string expectedHash, L12RandomState? expectedRandom, long expectedRevision,
        long expectedEventSequence, long expectedCardFactSequence)
    {
        Assert.Equal(expectedSequence, recovery.CommandSequence);
        Assert.Equal(expectedState, recovery.Engine.SerializeFullState());
        Assert.Equal(expectedHash, recovery.Engine.ComputeStateHash());
        Assert.Equal(expectedRandom, recovery.Engine.RandomState);
        Assert.Equal(expectedRevision, recovery.Engine.State.Revision);
        Assert.Equal(expectedEventSequence, recovery.Engine.State.EventSequence);
        Assert.Equal(expectedCardFactSequence, recovery.Engine.CardFactSignalSequence);
    }

    private static void AssertLinearGrowth(string metric, double previousValue, double currentValue,
        ScaleMedian previous, ScaleMedian current)
    {
        var ratio = currentValue / previousValue;
        Assert.True(currentValue >= previousValue && ratio <= GrowthRatioLimit,
            $"LC-03C {metric} 超出四倍规模增长预算：ratio={ratio:F3}, limit={GrowthRatioLimit:F1}, "
            + $"previous={JsonSerializer.Serialize(previous)}, current={JsonSerializer.Serialize(current)}");
    }

    private static async Task<StorageMetrics> ReadStorageMetricsAsync(string database, string matchId)
    {
        await using var connection = OpenConnection(database);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              COALESCE((SELECT SUM(length(command_json)+length(state_hash)+length(state_json)
                  +length(COALESCE(error,''))+length(COALESCE(request_id,'')))
                  FROM match_events WHERE match_id=$match),0)
              +COALESCE((SELECT SUM(length(request_id)+length(state_hash)+length(COALESCE(error,'')))
                  FROM match_action_requests WHERE match_id=$match),0)
              +COALESCE((SELECT SUM(length(event_json))
                  FROM match_action_events WHERE match_id=$match),0),
              COALESCE((SELECT SUM(length(state_blob)+length(random_state_blob)+length(state_hash))
                  FROM match_state_checkpoints WHERE match_id=$match),0),
              (SELECT COUNT(*) FROM match_events WHERE match_id=$match),
              (SELECT COUNT(*) FROM match_state_checkpoints WHERE match_id=$match);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new StorageMetrics(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
            reader.GetInt32(3));
    }

    private static async Task ExecuteAsync(string database, string sql, string matchId)
    {
        await using var connection = OpenConnection(database);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$match", matchId);
        Assert.True(await command.ExecuteNonQueryAsync() > 0);
    }

    private static async Task<long> CountDamagedCheckpointsAsync(string database, string matchId)
    {
        await using var connection = OpenConnection(database);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM match_state_checkpoints
            WHERE match_id=$match AND sequence>0 AND state_hash LIKE 'lc03c-damaged-%';
            """;
        command.Parameters.AddWithValue("$match", matchId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SqliteConnection OpenConnection(string database)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long SampleWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    private static long StorageFootprintBytes(string database)
        => new[] { database, database + "-wal", database + "-shm" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);

    private sealed record StorageMetrics(
        long JournalBytes,
        long CheckpointBytes,
        int EventRows,
        int CheckpointRows);

    private sealed record ScaleTrial(
        int CommandCount,
        int Seed,
        long JournalBytes,
        long CheckpointBytes,
        long DatabaseBytes,
        int CheckpointRows,
        double RecoveryMilliseconds,
        double TotalMilliseconds,
        long AllocatedBytes,
        long WorkingSetHighWaterBytes,
        int StateCharacters,
        int FirstProcessedRequests,
        int SecondProcessedRequests);

    private sealed record ScaleMedian(
        int CommandCount,
        long JournalBytes,
        long CheckpointBytes,
        long DatabaseBytes,
        int CheckpointRows,
        double RecoveryMilliseconds,
        double TotalMilliseconds,
        long AllocatedBytes,
        long WorkingSetHighWaterBytes,
        int StateCharacters,
        int FirstProcessedRequests,
        int SecondProcessedRequests)
    {
        internal static ScaleMedian From(IReadOnlyList<ScaleTrial> trials)
            => new(
                trials[0].CommandCount,
                Median(trials.Select(item => item.JournalBytes)),
                Median(trials.Select(item => item.CheckpointBytes)),
                Median(trials.Select(item => item.DatabaseBytes)),
                Median(trials.Select(item => item.CheckpointRows)),
                Median(trials.Select(item => item.RecoveryMilliseconds)),
                Median(trials.Select(item => item.TotalMilliseconds)),
                Median(trials.Select(item => item.AllocatedBytes)),
                Median(trials.Select(item => item.WorkingSetHighWaterBytes)),
                Median(trials.Select(item => item.StateCharacters)),
                Median(trials.Select(item => item.FirstProcessedRequests)),
                Median(trials.Select(item => item.SecondProcessedRequests)));

        private static long Median(IEnumerable<long> values)
            => values.Order().ElementAt(1);

        private static int Median(IEnumerable<int> values)
            => values.Order().ElementAt(1);

        private static double Median(IEnumerable<double> values)
            => values.Order().ElementAt(1);
    }
}
