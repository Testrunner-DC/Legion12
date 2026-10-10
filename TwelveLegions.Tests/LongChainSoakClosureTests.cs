using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

[CollectionDefinition(LongChainSoakClosureCollection.Name, DisableParallelization = true)]
public sealed class LongChainSoakClosureCollection
{
    public const string Name = "LC-06 long-chain soak closure";
}

[Collection(LongChainSoakClosureCollection.Name)]
public sealed class LongChainSoakClosureTests
{
    private const int MaximumStateBytes = 2 * 1024 * 1024;
    private const int MaximumProjectionBytes = 1024 * 1024;
    private const double MaximumSnapshotP95Milliseconds = 1_000;
    private const double MaximumHealthyRecoveryP95Milliseconds = 2_000;
    private const double MaximumFiveHundredCommandSeconds = 120;
    private const double GrowthMultiplier = 1.5;
    private const long GrowthFixedAllowanceBytes = 256 * 1024;
    private static readonly int[] TargetCommandCounts = [200, 350, 500];
    private static readonly string[] ScenarioIds = ["lc01-hanxin", "lc01-arthur", "lc01-oiran"];
    private readonly ITestOutputHelper _output;

    public LongChainSoakClosureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("L12Evidence", "lc06-default-65-compatibility")]
    public async Task DefaultEntryKeepsTheExistingSixtyFiveCommandContract()
    {
        var scenario = LongChainAdversarialHarness.Scenarios.Single(item => item.Id == "lc01-arthur");
        var evidence = await LongChainAdversarialHarness.RunAsync(scenario);

        Assert.Equal(LongChainAdversarialHarness.RepresentativeMinimumCommands,
            evidence.JournalCommandSequence);
        Assert.Equal(evidence.JournalCommandSequence, evidence.Commands.Length);
        Assert.True(evidence.LatestJournalCheckpointSequence
                    >= LongChainAdversarialHarness.MinimumLatestJournalCheckpointSequence);
        Assert.DoesNotContain(evidence.Cutpoints,
            cutpoint => cutpoint.StartsWith("soak-journal-", StringComparison.Ordinal));
        Assert.Single(evidence.JournalRecoveries);
        Assert.Equal("journal", evidence.JournalRecoveries[0].Label);
    }

    [Fact]
    [Trait("L12Evidence", "lc06-deep-soak-closure")]
    public async Task NineDeepCasesKeepRecoveryPrivacyAndGrowthBounded()
    {
        var runOrdinal = Environment.GetEnvironmentVariable("L12_LC06_RUN_ORDINAL") ?? "standalone";
        var results = new List<SoakCaseResult>();
        var suiteWatch = Stopwatch.StartNew();

        foreach (var target in TargetCommandCounts)
        foreach (var scenarioId in ScenarioIds)
        {
            var scenario = LongChainAdversarialHarness.Scenarios.Single(item => item.Id == scenarioId);
            var options = new LongChainRunOptions(
                target,
                scenario.Seed,
                $"lc06-{target}-{scenario.Seed}",
                [target / 3, target * 2 / 3, target]);
            var evidence = await LongChainAdversarialHarness.RunAsync(scenario, options);

            Assert.Equal(target, evidence.JournalCommandSequence);
            Assert.Equal(target, evidence.Commands.Length);
            Assert.Equal(target, evidence.Storage.EventRows);
            Assert.True(evidence.Storage.CheckpointRows >= target / 32 + 1,
                $"{scenarioId}/{target} 检查点不足：{evidence.Storage.CheckpointRows}");
            Assert.Equal([target / 3, target * 2 / 3, target],
                evidence.JournalRecoveries.Select(sample => (int)sample.CommandSequence));
            Assert.All(evidence.JournalRecoveries, sample =>
            {
                Assert.False(string.IsNullOrWhiteSpace(sample.StateHash));
                Assert.False(string.IsNullOrWhiteSpace(sample.Player0ProjectionHash));
                Assert.False(string.IsNullOrWhiteSpace(sample.Player1ProjectionHash));
                Assert.False(string.IsNullOrWhiteSpace(sample.SpectatorProjectionHash));
                Assert.False(string.IsNullOrWhiteSpace(sample.RefereeProjectionHash));
            });
            Assert.InRange(evidence.StateBytes, 1, MaximumStateBytes - 1);
            Assert.InRange(evidence.MaximumProjectionBytes, 1, MaximumProjectionBytes - 1);
            Assert.InRange(evidence.SnapshotP95Milliseconds, 0,
                MaximumSnapshotP95Milliseconds - double.Epsilon);
            Assert.InRange(evidence.RestoreP95Milliseconds, 0,
                MaximumHealthyRecoveryP95Milliseconds - double.Epsilon);
            Assert.InRange(evidence.JournalRecoveryP95Milliseconds, 0,
                MaximumHealthyRecoveryP95Milliseconds - double.Epsilon);
            if (target == 500)
                Assert.True(evidence.TotalMilliseconds <= MaximumFiveHundredCommandSeconds * 1_000,
                    $"{scenarioId}/500 墙钟 {evidence.TotalMilliseconds / 1_000:F3}s 超过 "
                    + $"{MaximumFiveHundredCommandSeconds:F0}s");

            foreach (var risk in scenario.RiskFamilies)
            {
                var evidenceName = LongChainAdversarialHarness.RequiredEvidenceByRisk[risk];
                Assert.True(evidence.EvidenceCounts[evidenceName] > 0,
                    $"{scenarioId}/{target} 未形成 {risk} 的真实证据 {evidenceName}");
            }

            var result = SoakCaseResult.From(runOrdinal, scenario, target, evidence);
            results.Add(result);
            _output.WriteLine(JsonSerializer.Serialize(result));
        }

        Assert.Equal(LongChainAdversarialHarness.RequiredRiskFamilies.Order(StringComparer.Ordinal),
            ScenarioIds.Select(id => LongChainAdversarialHarness.Scenarios.Single(item => item.Id == id))
                .SelectMany(Lc06RiskFamilies)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        var scales = TargetCommandCounts
            .Select(target => ScaleSummary.From(results.Where(result => result.TargetCommandCount == target)))
            .ToArray();
        for (var index = 1; index < scales.Length; index++)
        {
            AssertBoundedGrowth("journal bytes", scales[index - 1].JournalBytes,
                scales[index].JournalBytes, scales[index - 1].TargetCommandCount,
                scales[index].TargetCommandCount);
            AssertBoundedGrowth("checkpoint bytes", scales[index - 1].CheckpointBytes,
                scales[index].CheckpointBytes, scales[index - 1].TargetCommandCount,
                scales[index].TargetCommandCount);
            AssertBoundedGrowth("database bytes", scales[index - 1].DatabaseBytes,
                scales[index].DatabaseBytes, scales[index - 1].TargetCommandCount,
                scales[index].TargetCommandCount);
        }

        suiteWatch.Stop();
        var report = new SoakRunReport(runOrdinal, results.ToArray(), scales,
            suiteWatch.Elapsed.TotalMilliseconds);
        _output.WriteLine(JsonSerializer.Serialize(report));
        WriteEvidenceReport(report);
    }

    private static void AssertBoundedGrowth(string metric, long previousValue, long currentValue,
        int previousCommands, int currentCommands)
    {
        var commandRatio = (double)currentCommands / previousCommands;
        var maximum = previousValue * commandRatio * GrowthMultiplier + GrowthFixedAllowanceBytes;
        Assert.True(currentValue >= previousValue && currentValue <= maximum,
            $"LC-06 {metric} 增长不满足单调有界：previous={previousValue}, current={currentValue}, "
            + $"commandRatio={commandRatio:F3}, maximum={maximum:F0}");
    }

    private static void WriteEvidenceReport(SoakRunReport report)
    {
        var path = Environment.GetEnvironmentVariable("L12_LC06_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static IEnumerable<string> Lc06RiskFamilies(LongChainScenario scenario)
    {
        foreach (var risk in scenario.RiskFamilies) yield return risk;
        // 韩信链的真实响应对象是对方盖伏反击，且每一步都运行双方手牌、牌库、
        // 盖伏身份与四接收者哨兵断言，因此它同时承担 LC-06 的私密域深链。
        if (scenario.Id == "lc01-hanxin") yield return LongChainAdversarialHarness.PrivateRisk;
    }

    private static string StableFingerprint(LongChainEvidence evidence)
        => Sha256(JsonSerializer.Serialize(new
        {
            evidence.FinalStateHash,
            evidence.Player0ProjectionHash,
            evidence.Player1ProjectionHash,
            evidence.SpectatorProjectionHash,
            evidence.RefereeProjectionHash,
            evidence.RandomStateHash,
            evidence.Revision,
            evidence.FlowContractHash,
            evidence.EventContractHash,
            evidence.CardFactHash,
            evidence.CommandSequenceHash,
            Cutpoints = Sha256(JsonSerializer.Serialize(evidence.Cutpoints)),
            JournalRecoveries = evidence.JournalRecoveries.Select(sample => new
            {
                sample.Label,
                sample.CommandSequence,
                sample.StateHash,
                sample.Player0ProjectionHash,
                sample.Player1ProjectionHash,
                sample.SpectatorProjectionHash,
                sample.RefereeProjectionHash,
            }),
        }));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record SoakCaseResult(
        string RunOrdinal,
        string ScenarioId,
        string CardId,
        int Seed,
        int TargetCommandCount,
        string[] RiskFamilies,
        string StableFingerprint,
        int StateBytes,
        int MaximumProjectionBytes,
        long JournalBytes,
        long CheckpointBytes,
        long DatabaseBytes,
        int CheckpointRows,
        double JournalBytesPerCommand,
        double CheckpointBytesPerCommand,
        double DatabaseBytesPerCommand,
        double SnapshotP95Milliseconds,
        double RestoreP95Milliseconds,
        double JournalRecoveryP95Milliseconds,
        double TotalMilliseconds)
    {
        internal static SoakCaseResult From(string runOrdinal, LongChainScenario scenario, int target,
            LongChainEvidence evidence)
            => new(
                runOrdinal,
                scenario.Id,
                scenario.CardId,
                scenario.Seed,
                target,
                Lc06RiskFamilies(scenario).Distinct(StringComparer.Ordinal).ToArray(),
                LongChainSoakClosureTests.StableFingerprint(evidence),
                evidence.StateBytes,
                evidence.MaximumProjectionBytes,
                evidence.Storage.JournalBytes,
                evidence.Storage.CheckpointBytes,
                evidence.Storage.DatabaseBytes,
                evidence.Storage.CheckpointRows,
                (double)evidence.Storage.JournalBytes / target,
                (double)evidence.Storage.CheckpointBytes / target,
                (double)evidence.Storage.DatabaseBytes / target,
                evidence.SnapshotP95Milliseconds,
                evidence.RestoreP95Milliseconds,
                evidence.JournalRecoveryP95Milliseconds,
                evidence.TotalMilliseconds);
    }

    private sealed record ScaleSummary(
        int TargetCommandCount,
        long JournalBytes,
        long CheckpointBytes,
        long DatabaseBytes,
        double JournalBytesPerCommand,
        double CheckpointBytesPerCommand,
        double DatabaseBytesPerCommand)
    {
        internal static ScaleSummary From(IEnumerable<SoakCaseResult> source)
        {
            var results = source.OrderBy(result => result.ScenarioId, StringComparer.Ordinal).ToArray();
            Assert.Equal(3, results.Length);
            return new ScaleSummary(
                results[0].TargetCommandCount,
                Median(results.Select(result => result.JournalBytes)),
                Median(results.Select(result => result.CheckpointBytes)),
                Median(results.Select(result => result.DatabaseBytes)),
                Median(results.Select(result => result.JournalBytesPerCommand)),
                Median(results.Select(result => result.CheckpointBytesPerCommand)),
                Median(results.Select(result => result.DatabaseBytesPerCommand)));
        }

        private static long Median(IEnumerable<long> values)
            => values.Order().ElementAt(1);

        private static double Median(IEnumerable<double> values)
            => values.Order().ElementAt(1);
    }

    private sealed record SoakRunReport(
        string RunOrdinal,
        SoakCaseResult[] Cases,
        ScaleSummary[] Scales,
        double TotalMilliseconds);
}
