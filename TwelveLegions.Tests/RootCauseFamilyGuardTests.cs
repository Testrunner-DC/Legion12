using System.Runtime.CompilerServices;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RootCauseFamilyGuardTests
{
    [Fact]
    public void LibrarySearchFamiliesReuseOnePredicateAtOfferAndSettlement()
    {
        var server = Path.Combine(RepositoryRoot(), "服务端WebSocket", "TwelveLegions");
        AssertOccurrences(server, "L12ActiveAbilities.cs", "IsShanheSearchCandidate", 2);
        AssertOccurrences(server, "L12S1FactionEffects.cs", "IsFactionTopSearchCandidate", 2);
        AssertOccurrences(server, "L12S1ExtendedEffects.cs", "IsCampSearchCandidate", 3);
        AssertOccurrences(server, "L12EffectContinuations.cs", "IsOiranGiftSearchCandidate", 3);
        AssertOccurrences(server, "L12S2FactionEffects.cs", "IsRunePowerSearchCandidate", 2);
        AssertOccurrences(server, "L12S2FactionEffects.cs", "IsMerlinSearchCandidate", 2);

        var predicates = File.ReadAllText(Path.Combine(server, "L12LibrarySearchPredicates.cs"));
        foreach (var name in new[]
                 {
                     "IsShanheSearchCandidate", "IsFactionTopSearchCandidate", "IsCampSearchCandidate",
                     "IsOiranGiftSearchCandidate", "IsRunePowerSearchCandidate", "IsMerlinSearchCandidate",
                 })
            Assert.Contains(name, predicates, StringComparison.Ordinal);
        Assert.Contains("IsStillInInspectedLibrarySet", predicates, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalStateSignalsKeepTheirAuthoritativeWriterSet()
    {
        var server = Path.Combine(RepositoryRoot(), "服务端WebSocket", "TwelveLegions");
        AssertWriterSet(server, "HandDiscardedByMasterThisTurn = true",
            "L12S1ExtendedEffects.cs");
        AssertWriterSet(server, "ReturnedMoraleThisTurn +=",
            "L12GameEngine.cs");
        AssertWriterSet(server, "TombNamedLegionsLeftThisTurn++",
            "L12GameEngine.cs");
        AssertWriterSet(server, "MasterDamageTakenThisTurn +=",
            "L12GameEngine.cs", "L12GameEngine.cs");
        AssertWriterSet(server, "LastActiveTacticTurnSerial =",
            "L12Actions.cs", "L12EffectGeneratedPlay.cs");
    }

    [Fact]
    public void MobileMoralePickerExposesActivityAndSelectionAsDifferentStates()
    {
        var root = RepositoryRoot();
        var board = File.ReadAllText(Path.Combine(root, "opcgpro-vue", "src", "l12", "game", "GameBoard.vue"));
        var css = File.ReadAllText(Path.Combine(root, "opcgpro-vue", "src", "l12", "game", "GameBoard.mobile.css"));
        var returns = File.ReadAllText(Path.Combine(root, "服务端WebSocket", "TwelveLegions", "L12MoraleReturns.cs"));
        var payments = File.ReadAllText(Path.Combine(root, "服务端WebSocket", "TwelveLegions", "L12MoralePayments.cs"));

        Assert.Contains("activity: 'active' | 'rested' | 'fixed'", board, StringComparison.Ordinal);
        Assert.DoesNotContain("mobile-morale-state", board, StringComparison.Ordinal);
        Assert.DoesNotContain("活＝未消耗", board, StringComparison.Ordinal);
        Assert.Contains("activity-rested:not(.unavailable):not(.selected)", css, StringComparison.Ordinal);
        Assert.Contains("activity-rested.selected", css, StringComparison.Ordinal);
        Assert.Contains(":activityState", returns, StringComparison.Ordinal);
        Assert.DoesNotContain(":activityLabel", returns, StringComparison.Ordinal);
        Assert.Contains(":activityState", payments, StringComparison.Ordinal);
        Assert.DoesNotContain(":activityLabel", payments, StringComparison.Ordinal);
    }

    private static void AssertOccurrences(string root, string file, string value, int expected)
    {
        var source = File.ReadAllText(Path.Combine(root, file));
        Assert.Equal(expected, source.Split(value, StringSplitOptions.None).Length - 1);
    }

    private static void AssertWriterSet(string root, string marker, params string[] expectedFiles)
    {
        var actual = Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => File.ReadLines(path)
                .Where(line => line.Contains(marker, StringComparison.Ordinal))
                .Select(_ => Path.GetFileName(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedFiles.Order(StringComparer.Ordinal), actual);
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
