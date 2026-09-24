using System.Runtime.CompilerServices;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ConditionalHandAddPublicityGuardTests
{
    [Fact]
    public void ConditionalHandAddsCannotBypassThePublicOrPreviouslyRevealedGateways()
    {
        var root = Path.Combine(RepositoryRoot(), "服务端WebSocket", "TwelveLegions");
        var actual = Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => File.ReadLines(path)
                .Where(line => line.Contains("AddCardToHandByEffect(", StringComparison.Ordinal)
                    && !line.Contains("PubliclyRevealThenAddCardToHandByEffect(", StringComparison.Ordinal)
                    && !line.Contains("AddPreviouslyRevealedCardToHandByEffect(", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}|{line.Trim()}"))
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var expected = new[]
        {
            "L12AuthorityEvents.cs|private void AddCardToHandByEffect(L12PlayerState player, L12CardInstance card, string originZone, string reason)",
            "L12AuthorityEvents.cs|AddCardToHandByEffect(player, card, originZone, handAddReason);",
            "L12AuthorityEvents.cs|AddCardToHandByEffect(player, card, originZone, handAddReason);",
            "L12AuthorityEvents.cs|=> AddCardToHandByEffect(player, card, originZone, handAddReason);",
            "L12AuthorityEvents.cs|AddCardToHandByEffect(player, card, \"library\", reason);",
            "L12GameEngine.cs|AddCardToHandByEffect(owner, foundation, \"field\",",
            "L12GameEngine.cs|AddCardToHandByEffect(owner, card, \"field\", $\"{card.Name}因效果加入所有者手牌\");",
        }.GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(pair => pair.Key), actual.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void AlreadyRevealedFlowsUseTheNoDuplicateAnimationGateway()
    {
        var root = Path.Combine(RepositoryRoot(), "服务端WebSocket", "TwelveLegions");
        var combined = string.Join('\n', new[]
        {
            "L12MoraleReturns.cs",
            "L12S1ExtendedEffects.cs",
            "L12S2FactionEffects.cs",
        }.Select(file => File.ReadAllText(Path.Combine(root, file))));

        Assert.Contains("AddPreviouslyRevealedCardToHandByEffect(player, top", combined,
            StringComparison.Ordinal);
        Assert.Contains("AddPreviouslyRevealedCardToHandByEffect(player, artifact", combined,
            StringComparison.Ordinal);
        Assert.Contains("AddPreviouslyRevealedCardToHandByEffect(player, card", combined,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
