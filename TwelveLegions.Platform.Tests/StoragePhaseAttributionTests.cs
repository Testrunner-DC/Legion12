using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class StoragePhaseAttributionTests
{
    private static readonly string[] StoragePhases =
    [
        "duration.platform.phase.deck-domain",
        "duration.platform.phase.ranked-broadcast-sync",
        "duration.platform.phase.snapshot-write",
        "duration.platform.phase.sqlite-commit",
    ];

    [Fact]
    public void OrdinarySaveRecordsEveryStoragePhase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-storage-phase-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            // The first save establishes the database instead of taking the
            // transactional save path, so anchor the measurement after it.
            Assert.True(store.Register("phaseboot1", "password-123").Success);
            var before = Counts();
            Assert.True(store.Register("phaseattr1", "password-123").Success);
            var after = Counts();
            var deltas = StoragePhases.ToDictionary(stage => stage,
                stage => after.GetValueOrDefault(stage) - before.GetValueOrDefault(stage),
                StringComparer.Ordinal);
            Assert.True(deltas.All(pair => pair.Value > 0),
                string.Join(", ", deltas.Select(pair => $"{pair.Key}={pair.Value}")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static Dictionary<string, long> Counts() =>
        L12PerformanceMetrics.TrySnapshot(out var batch) && batch is not null
            ? batch.Series.ToDictionary(row => row.Stage, row => row.Count, StringComparer.Ordinal)
            : throw new InvalidOperationException("performance window unavailable");
}
