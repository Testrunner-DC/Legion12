using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// Batch S2: a category is either measured or refused for a reason the operator can act on.
// "0 B / available=false" conflated an empty directory with a permission problem and with a path
// that was never deployed.
[Collection("Platform environment")]
public sealed class ServerStorageCategoryStateTests
{
    private static L12ServerStorageProbe Probe(
        Func<string, L12StorageCategorySample> readCategory,
        IReadOnlyList<L12StorageVolumeSample>? volumes = null) => new(
        () => new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero),
        () => new L12StorageVolumeProbeResult(volumes ?? [new(1_000, 400, "/")], 0),
        readCategory,
        () => 256);

    [Fact]
    public void CategoriesDistinguishMeasuredMissingAndDenied()
    {
        var probe = Probe(path => path.EndsWith("platform.db", StringComparison.Ordinal)
            ? new L12StorageCategorySample(1_234, L12StorageCategoryState.Ok)
            : path.EndsWith("matches.db", StringComparison.Ordinal)
                ? new L12StorageCategorySample(0, L12StorageCategoryState.Missing)
                : new L12StorageCategorySample(0, L12StorageCategoryState.Denied));

        var snapshot = L12ServerStorageMonitor.Read(probe);

        var byId = snapshot.Categories.ToDictionary(category => category.Id, StringComparer.Ordinal);
        Assert.Equal("ok", byId["platform-db"].State);
        Assert.Equal(1_234, byId["platform-db"].Bytes);
        Assert.Equal("missing", byId["matches-db"].State);
        Assert.Equal(0, byId["matches-db"].Bytes);
        Assert.Equal("denied", byId["site-media"].State);
        // A refused read must never be reported as a measured zero.
        Assert.DoesNotContain(snapshot.Categories,
            category => category.State != "ok" && category.Bytes != 0);
    }

    [Fact]
    public void AThrownAccessFailureBecomesDeniedNotZero()
    {
        var snapshot = L12ServerStorageMonitor.Read(Probe(_ =>
            throw new UnauthorizedAccessException("simulated unreadable directory")));

        Assert.All(snapshot.Categories, category => Assert.Equal("denied", category.State));
        Assert.All(snapshot.Categories, category => Assert.Equal(0, category.Bytes));
    }

    [Fact]
    public void CategoryReportsTheDeepestMatchingVolumeMount()
    {
        var volumes = new List<L12StorageVolumeSample>
        {
            new(1_000, 400, "/"),
            new(2_000, 1_200, "/www"),
        };
        var snapshot = L12ServerStorageMonitor.Read(Probe(_ =>
            new L12StorageCategorySample(10, L12StorageCategoryState.Ok), volumes));

        var releases = snapshot.Categories.Single(category => category.Id == "releases");
        var deployment = snapshot.Categories.Single(category => category.Id == "deployment-backups");
        Assert.Equal("/www", releases.VolumeMount);
        Assert.Equal("/", deployment.VolumeMount);
    }

    [Fact]
    public void CategoryWithoutAnyVolumeReportsNoMount()
    {
        var snapshot = L12ServerStorageMonitor.Read(Probe(_ =>
            new L12StorageCategorySample(10, L12StorageCategoryState.Ok), []));

        Assert.All(snapshot.Categories, category => Assert.Null(category.VolumeMount));
    }

    [Fact]
    public void ConfiguredCategoriesReplaceTheDefaults()
    {
        const string configured =
            "[{\"id\":\"custom-a\",\"label\":\"自定义甲\",\"path\":\"/data/a\"}," +
            "{\"id\":\"custom-b\",\"path\":\"/data/b\"}]";
        var seen = new List<string>();
        var previous = Environment.GetEnvironmentVariable("L12_STORAGE_CATEGORIES");
        try
        {
            Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", configured);
            var snapshot = L12ServerStorageMonitor.Read(Probe(path =>
            {
                seen.Add(path);
                return new L12StorageCategorySample(1, L12StorageCategoryState.Ok);
            }));

            Assert.Equal(new[] { "/data/a", "/data/b" }, seen.ToArray());
            Assert.Equal(new[] { "custom-a", "custom-b" },
                snapshot.Categories.Select(category => category.Id).ToArray());
            // A missing label falls back to the id instead of rendering empty.
            Assert.Equal("custom-b", snapshot.Categories[1].Label);
        }
        finally { Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", previous); }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"not-an-array\"}")]
    [InlineData("[]")]
    [InlineData("[{\"id\":\"\",\"path\":\"/data/a\"}]")]
    public void InvalidCategoryConfigurationFallsBackToTheDefaults(string configured)
    {
        var previous = Environment.GetEnvironmentVariable("L12_STORAGE_CATEGORIES");
        try
        {
            Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", configured);
            var snapshot = L12ServerStorageMonitor.Read(Probe(_ =>
                new L12StorageCategorySample(1, L12StorageCategoryState.Ok)));

            Assert.Equal(6, snapshot.Categories.Count);
            Assert.Contains(snapshot.Categories, category => category.Id == "platform-db");
        }
        finally { Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", previous); }
    }

    [Fact]
    public void DefaultCategoriesDoNotClaimSystemLogsAsThisServicesStorage()
    {
        var previous = Environment.GetEnvironmentVariable("L12_STORAGE_CATEGORIES");
        try
        {
            Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", null);
            // Measured on the host on 2026-11-23: the service account CAN read /var/log (416 MB),
            // while this service writes to journald. The row was noise, so it is gone.
            var snapshot = L12ServerStorageMonitor.Read(Probe(_ =>
                new L12StorageCategorySample(1, L12StorageCategoryState.Ok)));

            Assert.DoesNotContain(snapshot.Categories, category => category.Id == "logs");
            Assert.DoesNotContain(snapshot.Categories,
                category => category.Label.Contains("系统日志", StringComparison.Ordinal));
        }
        finally { Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", previous); }
    }

[Fact]
    public void ConcurrentReadsShareOneTraversal()
    {
        var traversals = 0;
        var probe = new L12ServerStorageProbe(
            () => DateTimeOffset.UtcNow,
            () => new L12StorageVolumeProbeResult([new(1_000, 400, "/")], 0),
            path =>
            {
                Interlocked.Increment(ref traversals);
                Thread.Sleep(40);
                return new L12StorageCategorySample(1, L12StorageCategoryState.Ok);
            },
            () => 256);
        L12ServerStorageMonitor.ResetCacheForTest();
        try
        {
            var results = new System.Collections.Concurrent.ConcurrentBag<L12ServerStorageView>();
            Parallel.For(0, 10, _ => results.Add(L12ServerStorageMonitor.Read(probe, useSharedCache: true)));

            // Ten concurrent requests, one traversal: everyone else waited for the same snapshot.
            Assert.Equal(6, traversals);
            Assert.Single(results.Select(result => result.ObservedAt).Distinct());
        }
        finally { L12ServerStorageMonitor.ResetCacheForTest(); }
    }

    [Fact]
    public void SecondReadInsideTheCacheWindowReusesTheSnapshot()
    {
        var traversals = 0;
        var probe = new L12ServerStorageProbe(
            () => DateTimeOffset.UtcNow,
            () => new L12StorageVolumeProbeResult([new(1_000, 400, "/")], 0),
            _ =>
            {
                Interlocked.Increment(ref traversals);
                return new L12StorageCategorySample(7, L12StorageCategoryState.Ok);
            },
            () => 256);
        L12ServerStorageMonitor.ResetCacheForTest();
        try
        {
            var first = L12ServerStorageMonitor.Read(probe, useSharedCache: true);
            var second = L12ServerStorageMonitor.Read(probe, useSharedCache: true);

            Assert.Equal(6, traversals);
            Assert.Equal(first.ObservedAt, second.ObservedAt);
            Assert.Equal(first.CachedFrom, second.CachedFrom);
        }
        finally { L12ServerStorageMonitor.ResetCacheForTest(); }
    }

    [Fact]
    public void AWedgedCategoryTimesOutWithoutFailingTheSnapshot()
    {
        var previous = Environment.GetEnvironmentVariable("L12_STORAGE_CATEGORIES");
        L12ServerStorageMonitor.ResetCacheForTest();
        try
        {
            // One category only, so the 5 s budget is paid once.
            Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES",
                "[{\"id\":\"slow\",\"label\":\"很慢\",\"path\":\"/slow\"}]");
            var probe = new L12ServerStorageProbe(
                () => DateTimeOffset.UtcNow,
                () => new L12StorageVolumeProbeResult([new(1_000, 400, "/")], 0),
                _ => new L12StorageCategorySample(0, L12StorageCategoryState.Missing),
                () => 256,
                (_, token) =>
                {
                    // Ignores the token on purpose: the budget, not cooperation, must end it.
                    Thread.Sleep(TimeSpan.FromSeconds(20));
                    return new L12StorageCategorySample(0, L12StorageCategoryState.Ok);
                });

            var started = DateTimeOffset.UtcNow;
            var snapshot = L12ServerStorageMonitor.Read(probe);
            var elapsed = DateTimeOffset.UtcNow - started;

            Assert.Equal("timeout", Assert.Single(snapshot.Categories).State);
            Assert.Equal("partial", snapshot.SampleState);
            Assert.True(elapsed < TimeSpan.FromSeconds(8), $"timeout path took {elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            Environment.SetEnvironmentVariable("L12_STORAGE_CATEGORIES", previous);
            L12ServerStorageMonitor.ResetCacheForTest();
        }
    }

    [Fact]
    public void RealDirectoryReadsReportOkAndMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-storage-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "probe.bin"), new string('x', 64));
            var measured = L12ServerStorageMonitor.DirectoryBytes(root);
            Assert.Equal(L12StorageCategoryState.Ok, measured.State);
            Assert.True(measured.Bytes >= 64);

            var absent = L12ServerStorageMonitor.DirectoryBytes(Path.Combine(root, "absent"));
            Assert.Equal(L12StorageCategoryState.Missing, absent.State);
            Assert.Equal(0, absent.Bytes);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}