using System.Runtime.CompilerServices;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class EffectReadyMutationGuardTests
{
    [Fact]
    [Trait("L12Evidence", "guard:effect-ready-authoritative-mutation")]
    public void DirectReadyStateWritesRemainLimitedToReviewedNonEffectOrAuthorityExemptions()
    {
        var root = SourceRoot();
        var server = Path.Combine(root, "服务端WebSocket", "TwelveLegions");
        var actual = Directory.EnumerateFiles(server, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => File.ReadLines(path)
                .Where(line => line.Contains(".Tapped = false", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}|{line.Trim()}"))
            .GroupBy(entry => entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var expected = new[]
        {
            "L12GmCommands.cs|attacker.Tapped = false;",
            "L12AuthorityEvents.cs|if (card is not null) card.Tapped = false;",
            "L12AuthorityEvents.cs|if (morale is not null) morale.Tapped = false;",
            "L12GameEngine.cs|card.Tapped = false;",
            "L12GameEngine.cs|card.Tapped = false;",
            "L12GameEngine.cs|if (morale.CannotUntapUntilRound < State.Round) morale.Tapped = false;",
            "L12GameEngine.cs|if (card is not null && card.CannotUntapUntilRound < State.Round) card.Tapped = false;",
            "L12GameEngine.cs|if (player.Relic is not null && player.Relic.CannotUntapUntilRound < State.Round) player.Relic.Tapped = false;",
            "L12S1ExtendedEffects.cs|artifact.Tapped = false;",
            "L12S2FactionEffects.cs|xiaotian.Tapped = false;",
            "L12S2RemainingEffects.cs|morale.Tapped = false;",
            "L12StarterRemainingEffects.cs|target.Tapped = false;",
            "L12StarterRemainingEffects.cs|target.Tapped = false;",
        }.GroupBy(entry => entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(pair => pair.Key), actual.OrderBy(pair => pair.Key));
    }

    private static string SourceRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
