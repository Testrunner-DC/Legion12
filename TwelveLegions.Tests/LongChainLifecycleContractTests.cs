using Xunit;

namespace TwelveLegions.Tests;

public sealed class LongChainLifecycleContractTests
{
    public static IEnumerable<object[]> DeterministicScenarios()
        => LongChainAdversarialHarness.Scenarios.Select(scenario => new object[] { scenario });

    [Fact]
    [Trait("L12Evidence", "long-chain-inventory")]
    public void FirstBatchCoversAllHistoricalFailuresAndSixRiskFamilies()
    {
        var expectedCards = new[]
        {
            "S01-0104", "S01-0106", "S01-0201", "S01-0208", "S01-0301",
            "S01-0311", "S02-0511", "S02-0516", "S02-0517", "S02-0519",
            "S02-0601", "S02-0606", "S02-0607", "S02-0612", "ST04-06",
        };

        Assert.Equal(expectedCards.Order(), LongChainAdversarialHarness.Scenarios.Select(item => item.CardId).Order());
        Assert.Equal(LongChainAdversarialHarness.RequiredRiskFamilies.Order(),
            LongChainAdversarialHarness.Scenarios.SelectMany(item => item.RiskFamilies).Distinct().Order());
        Assert.Equal(15, LongChainAdversarialHarness.Scenarios.Select(item => item.Seed).Distinct().Count());
        Assert.All(LongChainAdversarialHarness.Scenarios, scenario =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.Setup));
            Assert.False(string.IsNullOrWhiteSpace(scenario.Action));
            Assert.False(string.IsNullOrWhiteSpace(scenario.ExpectedEvidence));
        });
    }

    [Fact]
    [Trait("L12Evidence", "long-chain-failure-evidence-guard")]
    public void FailurePathsCannotDropTheCurrentCommandPrefixOrCutpoints()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (directory is not null && source is null)
        {
            var nested = Path.Combine(directory.FullName, "TwelveLegions.Tests",
                "LongChainAdversarialHarness.cs");
            var direct = Path.Combine(directory.FullName, "LongChainAdversarialHarness.cs");
            if (File.Exists(nested)) source = nested;
            else if (File.Exists(direct)) source = direct;
            directory = directory.Parent;
        }

        Assert.NotNull(source);
        var text = File.ReadAllText(source);
        Assert.DoesNotContain("Fail(scenario, [], []", text, StringComparison.Ordinal);
        Assert.Contains("minimalCommandPrefixCount=", text, StringComparison.Ordinal);
        Assert.Contains("authorityHash[A=", text, StringComparison.Ordinal);
        Assert.Contains("projectionHash[p0=", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DeterministicScenarios))]
    [Trait("L12Evidence", "long-chain-determinism")]
    public async Task EveryScenarioAndSeedProduceTheSameCommandsCutpointsHashesAndActualEvidence(
        LongChainScenario scenario)
    {
        var first = await LongChainAdversarialHarness.RunAsync(scenario);
        var second = await LongChainAdversarialHarness.RunAsync(scenario);

        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.Player0ProjectionHash, second.Player0ProjectionHash);
        Assert.Equal(first.Player1ProjectionHash, second.Player1ProjectionHash);
        Assert.Equal(first.SpectatorProjectionHash, second.SpectatorProjectionHash);
        Assert.Equal(first.RefereeProjectionHash, second.RefereeProjectionHash);
        Assert.Equal(first.Commands, second.Commands);
        Assert.Equal(first.Cutpoints, second.Cutpoints);
        Assert.Equal(first.Postconditions, second.Postconditions);
        Assert.Equal(first.EvidenceCounts.OrderBy(item => item.Key),
            second.EvidenceCounts.OrderBy(item => item.Key));
        Assert.Contains(first.Cutpoints, item => item.Contains("before-prompt", StringComparison.Ordinal)
            || item == "after-focus");
        foreach (var risk in scenario.RiskFamilies)
        {
            var evidenceKey = LongChainAdversarialHarness.RequiredEvidenceByRisk[risk];
            Assert.True(first.EvidenceCounts[evidenceKey] > 0,
                $"{scenario.Id}/{risk} 只有风险标签，没有真实后置状态证据 {evidenceKey}");
        }
        if (scenario.Mechanism == "response")
        {
            Assert.Contains("paid-cost-before-resolution", first.Cutpoints);
            Assert.Contains("response-stack", first.Cutpoints);
            Assert.Contains(first.Cutpoints, item => item.Contains("after-prompt", StringComparison.Ordinal));
        }
        if (scenario.Mechanism == "attachment")
            Assert.Contains("after-attachment-committed", first.Cutpoints);
        if (scenario.Mechanism == "private-order")
            Assert.Contains("after-private-library-order", first.Cutpoints);
        if (scenario.Mechanism == "timed-cleanup")
        {
            Assert.Contains("after-source-left-field", first.Cutpoints);
            Assert.Contains("after-cross-turn-cleanup", first.Cutpoints);
        }
    }

    [Fact]
    [Trait("L12Evidence", "long-chain-budget")]
    public async Task RepresentativeLongChainStaysWithinRetainedPayloadAndRecoveryBudgets()
    {
        var scenario = LongChainAdversarialHarness.Scenarios.Single(item => item.CardId == "S02-0601");
        var evidence = await LongChainAdversarialHarness.RunAsync(scenario);

        Assert.InRange(evidence.StateBytes, 1, 2_000_000);
        Assert.InRange(evidence.MaximumProjectionBytes, 1, 1_000_000);
        Assert.InRange(evidence.SnapshotP95Milliseconds, 0, 1_000);
        Assert.InRange(evidence.RestoreP95Milliseconds, 0, 2_000);
        Assert.True(evidence.RejectedCommands >= 3,
            "代表场景必须真实覆盖越权、非法选择与过期重复命令");
    }
}
