using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

// S3 的三条验收：缓存窗口内复用、并发只遍历一次、超时保留上次成功值且不抛。
// 采样器的共享缓存是进程级静态状态，每个用例前后都清一次。
[Collection("Platform environment")]
public sealed class ServerStorageSamplingGuardTests
{
    [Fact]
    public void ReadsWithinTheCacheWindowReuseOneSnapshot()
    {
        L12ServerStorageMonitor.ResetCacheForTest();
        try
        {
            var reads = 0;
            var probe = Probe(_ => { Interlocked.Increment(ref reads); return new(4096, L12StorageCategoryState.Ok); });

            var first = L12ServerStorageMonitor.Read(probe, useSharedCache: true);
            var readsAfterFirst = Volatile.Read(ref reads);
            var second = L12ServerStorageMonitor.Read(probe, useSharedCache: true);

            Assert.True(readsAfterFirst >= first.Categories.Count);
            Assert.Equal(readsAfterFirst, Volatile.Read(ref reads));
            Assert.Equal(first.ObservedAt, second.ObservedAt);
            Assert.Equal(first.CachedFrom, second.CachedFrom);
            Assert.Equal(first.SampleState, second.SampleState);
        }
        finally { L12ServerStorageMonitor.ResetCacheForTest(); }
    }

    [Fact]
    public async Task ConcurrentReadsShareASingleTraversal()
    {
        L12ServerStorageMonitor.ResetCacheForTest();
        try
        {
            var reads = 0;
            var probe = Probe(_ =>
            {
                Interlocked.Increment(ref reads);
                Thread.Sleep(20);
                return new(2048, L12StorageCategoryState.Ok);
            });

            var snapshots = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => L12ServerStorageMonitor.Read(probe, useSharedCache: true))));

            var categories = Assert.Single(snapshots.Select(item => item.Categories.Count).Distinct());
            Assert.True(categories > 0);
            // One traversal covers every category once; a second traversal would double this.
            Assert.Equal(categories, Volatile.Read(ref reads));
            Assert.Single(snapshots.Select(item => item.ObservedAt).Distinct());
        }
        finally { L12ServerStorageMonitor.ResetCacheForTest(); }
    }

    [Fact]
    public void TimeoutKeepsTheLastMeasuredBytesAndDoesNotThrow()
    {
        L12ServerStorageMonitor.ResetCacheForTest();
        try
        {
            var timedOut = false;
            var probe = Probe(_ => timedOut
                ? new L12StorageCategorySample(0, L12StorageCategoryState.Timeout)
                : new L12StorageCategorySample(8192, L12StorageCategoryState.Ok));

            var healthy = L12ServerStorageMonitor.Read(probe);
            Assert.All(healthy.Categories, category => Assert.Equal("ok", category.State));
            Assert.All(healthy.Categories, category => Assert.Equal(8192, category.Bytes));

            timedOut = true;
            var afterTimeout = L12ServerStorageMonitor.Read(probe);

            Assert.All(afterTimeout.Categories, category => Assert.Equal("timeout", category.State));
            // The stale number is still the best fact we have; the page dates it instead of zeroing it.
            Assert.All(afterTimeout.Categories, category => Assert.Equal(8192, category.Bytes));
            Assert.Equal("partial", afterTimeout.SampleState);
            Assert.True(afterTimeout.UnavailableSourceCount >= afterTimeout.Categories.Count);
        }
        finally { L12ServerStorageMonitor.ResetCacheForTest(); }
    }

    private static L12ServerStorageProbe Probe(Func<string, L12StorageCategorySample> readCategory)
        => new(
            // Real clock: the cache window is measured against DateTimeOffset.UtcNow, so a pinned
            // sample time would always look stale and every read would traverse again.
            () => DateTimeOffset.UtcNow,
            () => new L12StorageVolumeProbeResult([new L12StorageVolumeSample(1000, 800, "/")], 0),
            readCategory,
            () => 123);
}
