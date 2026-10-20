using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformAllocationPhaseProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void SyntheticFourMiBPlatformPayloadSeparatesReadAllocationsFromWritePhases()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("allocprobe");
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", instance)!.GetValue(fixture.Store)!;
            var content = (IDictionary)data.GetType().GetProperty("Content")!.GetValue(data)!;
            content["synthetic-unrelated-content"] = new string('a', 4 * 1024 * 1024);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var generation = RankedIdentityTestState.CommittedProjection(fixture.Store);
        var data = typeof(L12PlatformStore).GetProperty("_data", instance)!.GetValue(fixture.Store)!;
        var measurements = new List<(string Phase, long Bytes, double Ms)>();
        Measure("admin-raw-snapshot-baseline", () => JsonSerializer.Serialize(data, data.GetType()));
        Measure("database-core-string-baseline", () => InvokeStatic("SerializeSnapshot", data));
        // This compatibility encoder is not the normal UTF-8 Save preparation.
        // Keep it as a baseline, but never rank it as the live allocation source.
        Measure("database-core-utf8-buffer-baseline", () => InvokeStatic("SerializeCoreSnapshot", data, "UTF-8"));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = fixture.Store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            var current = Measure("database-core-prepare-current", () =>
                InvokeStatic("PrepareCoreSnapshot", data, connection, transaction))!;
            var jsonValue = current.GetType().GetProperty("JsonValue")!.GetValue(current)!;
            Assert.Equal("StreamedCoreSnapshot", jsonValue.GetType().Name);
        }
        Measure("disk-mirror", () => InvokeStatic("PrepareFallbackMirror", data));
        var rollback = (byte[])InvokeStatic("SerializeRollbackState", data)!;
        Measure("rollback-gzip", () => InvokeStatic("SerializeRollbackState", data));
        Measure("committed-index", () => typeof(L12PlatformStore).GetMethod("PrepareCommittedSessionActivity", instance)!
            .Invoke(fixture.Store, new[] { data, rollback }));
        Measure("admin-noop-transaction", () => fixture.Store.ExecuteAdminTransaction(() => true));
        Assert.Contains(measurements, row => row.Phase == "database-core-prepare-current");
        var readStart = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var index = 0; index < 1000; index++)
        {
            _ = fixture.Store.CaptureOperationsPolicy();
            _ = fixture.Store.ResponsePreference(player.Id);
            _ = fixture.Store.RankedSettlement("missing-match", player.Id);
        }
        var readBytes = GC.GetAllocatedBytesForCurrentThread() - readStart;
        output.WriteLine("synthetic_payload_bytes=4194304 read_iterations=1000 read_allocated_bytes={0} read_ms={1:F3}",
            readBytes, watch.Elapsed.TotalMilliseconds);
        Assert.True(readBytes < 1024 * 1024); // Readers do not serialize unrelated multi-MiB state.
        Assert.Same(generation, RankedIdentityTestState.CommittedProjection(fixture.Store));
        // Include one real Save (streamed SQLite write, mirror and rollback), not
        // only isolated preparation. Do it after preserving the read-only guard.
        Measure("admin-save-transaction", () => fixture.Store.ExecuteAdminTransaction(() =>
        {
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        }));
        var dominant = measurements.Where(row => !row.Phase.EndsWith("-transaction", StringComparison.Ordinal)
                && !row.Phase.EndsWith("-baseline", StringComparison.Ordinal))
            .OrderByDescending(row => row.Bytes).First();
        output.WriteLine("dominant_allocated_phase={0} allocated_bytes={1}", dominant.Phase, dominant.Bytes);

        object? InvokeStatic(string method, params object[] inputs)
            => typeof(L12PlatformStore).GetMethod(method, statics)!.Invoke(null, inputs);
        object? Measure(string phase, Func<object?> work)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            var result = work();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            measurements.Add((phase, bytes, timer.Elapsed.TotalMilliseconds));
            output.WriteLine("phase={0} allocated_bytes={1} elapsed_ms={2:F3}", phase, bytes, timer.Elapsed.TotalMilliseconds);
            GC.KeepAlive(result);
            return result;
        }
    }
}
