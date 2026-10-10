using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ArchitectureP2P3BoundaryTests
{
    [Fact]
    public void TransportAndRoomLayersDoNotWriteKernelStateOrRecalculateCardEffects()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "服务端WebSocket", "TwelveLegions");
        var roomSources = Directory.EnumerateFiles(sourceRoot, "L12RoomManager*.cs")
            .Concat([Path.Combine(sourceRoot, "L12RankedClock.cs")])
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .ToArray();
        var transport = File.ReadAllText(Path.Combine(sourceRoot, "L12WebSocketServer.cs"));

        var directStateWrite = new Regex(
            @"(?:room\.Game|engine|game)\.State(?:\.[A-Za-z_][A-Za-z0-9_]*|\[[^\]]+\])+\s*(?:(?<![=!<>])=(?!=)|\+\+|--|\+=|-=)",
            RegexOptions.CultureInvariant);
        Assert.All(roomSources, file =>
            Assert.False(directStateWrite.IsMatch(file.Source),
                $"{Path.GetFileName(file.Path)} must submit commands instead of writing kernel state"));
        Assert.DoesNotContain("new L12GameEngine", transport, StringComparison.Ordinal);
        Assert.DoesNotContain(".Handle(", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("L12GameEngine.RestoreCheckpoint", transport, StringComparison.Ordinal);
        Assert.DoesNotContain(".State.Players", transport, StringComparison.Ordinal);
    }

    [Fact]
    public void RecipientProjectionHasOneRoomLayerAdapterAndNoTransportSidePrivacyRules()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "服务端WebSocket", "TwelveLegions");
        var room = File.ReadAllText(Path.Combine(sourceRoot, "L12RoomManager.cs"));
        var transport = File.ReadAllText(Path.Combine(sourceRoot, "L12WebSocketServer.cs"));

        Assert.Contains("L12KernelProjection.ForPlayer", room, StringComparison.Ordinal);
        Assert.Contains("L12KernelProjection.ForSpectator", room, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotForGm(", room, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotForSpectator(", room, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotFor(", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("revealAllHands", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("revealAllDisasters", transport, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceCompatibilityPolicyIsExplicitAndFailsClosedOutsideCurrentRecoveryRange()
    {
        Assert.Equal(2, L12PersistenceContract.CurrentStateFormatVersion);
        Assert.Equal(2, L12PersistenceContract.CurrentJournalStorageVersion);
        Assert.True(L12PersistenceContract.SupportsCheckpointRecovery(2));
        Assert.False(L12PersistenceContract.SupportsCheckpointRecovery(1));
        Assert.False(L12PersistenceContract.SupportsCheckpointRecovery(3));

        var oldError = Assert.Throws<InvalidDataException>(() =>
            L12PersistenceContract.EnsureCheckpointRecoverySupported(1));
        var futureError = Assert.Throws<InvalidDataException>(() =>
            L12PersistenceContract.EnsureCheckpointRecoverySupported(3));
        Assert.Equal("该版本已过期，回放无法生成", oldError.Message);
        Assert.Equal(oldError.Message, futureError.Message);
    }

    [Fact]
    public void ExistingJournalOutboxProjectionAndBackupDrillsRemainRegisteredInReleaseGate()
    {
        var root = RepositoryRoot();
        var releaseGate = File.ReadAllText(Path.Combine(root, "scripts", "test-l12-release-gate.ps1"));
        var deployBehavior = File.ReadAllText(Path.Combine(root, "scripts", "test-l12-deploy-behavior.ps1"));
        var recoveryTests = File.ReadAllText(Path.Combine(root, "TwelveLegions.Tests",
            "RankedPersistenceRecoveryTests.cs"));
        var journalTests = File.ReadAllText(Path.Combine(root, "TwelveLegions.Tests",
            "LatencyAndPersistenceRegressionTests.cs"));
        var analyticsTests = File.ReadAllText(Path.Combine(root, "TwelveLegions.Tests",
            "CardAnalyticsFairnessTests.cs"));

        Assert.Contains("TwelveLegions.Tests", releaseGate, StringComparison.Ordinal);
        Assert.Contains("runtime-backup", deployBehavior, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("上一版本已恢复并通过", deployBehavior, StringComparison.Ordinal);
        Assert.Contains("PendingSettlementReplays", recoveryTests, StringComparison.Ordinal);
        Assert.Contains("事务各阶段失败会恢复最后确认检查点", journalTests, StringComparison.Ordinal);
        Assert.Contains("EpochInvalidationRejectsStaleStatistics", analyticsTests, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
