using Xunit;
using Xunit.Sdk;

namespace TwelveLegions.Tests;

public sealed class LongChainDiagnosticBudgetTests
{
    private static LongChainScenario Scenario => new("diagnostic-budget", "synthetic", 123,
        [], "setup", "action", "comparison unchanged");

    [Fact]
    public void EqualJsonDoesNotEvaluateFailureDiagnostics()
    {
        LongChainAdversarialHarness.AssertJsonEqual(Scenario, [], [], "same", "{}", "{}",
            () => throw new InvalidOperationException("Successful comparison must not build a failure summary"));
    }

    [Fact]
    public void UnequalJsonRetainsDifferenceSummaryCutpointsAndCommandPrefix()
    {
        var calls = 0;
        var error = Assert.Throws<XunitException>(() =>
            LongChainAdversarialHarness.AssertJsonEqual(Scenario, ["first-command"], ["recovery"],
                "different", "{\"value\":1}", "{\"value\":2}", () => { calls++; return "diagnostic-sentinel"; }));
        Assert.Equal(1, calls);
        Assert.Contains("diagnostic-sentinel", error.Message);
        Assert.Contains("value", error.Message);
        Assert.Contains("stage=different", error.Message);
        Assert.Contains("cutpoints=recovery", error.Message);
        Assert.Contains("minimalCommandPrefixCount=1", error.Message);
        Assert.Contains("first-command", error.Message);
    }
}
