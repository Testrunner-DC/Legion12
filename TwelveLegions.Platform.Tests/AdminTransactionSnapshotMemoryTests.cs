using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminTransactionSnapshotMemoryTests(ITestOutputHelper output)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public void CompleteRawSnapshotRoundTripsExactlyAndNeverMutatesTheInput()
    {
        using var fixture = new RankedIdentityFixture();
        _ = fixture.CreateAccount("rawsnapshot");
        var data = Data(fixture.Store);
        Content(data)["synthetic-staged-content"] = "未提交内容🙂\r\n";
        var original = JsonSerializer.Serialize(data, data.GetType());
        var captured = Capture(data);
        Assert.Equal(original, JsonSerializer.Serialize(data, data.GetType()));
        var restored = Restore(captured);
        Assert.Equal(original, JsonSerializer.Serialize(restored, restored.GetType()));
        var corrupted = captured.ToArray();
        corrupted[^1] ^= 1;
        var error = Assert.Throws<TargetInvocationException>(() => Restore(corrupted));
        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Throws<TargetInvocationException>(() => Restore([]));
    }

    [Fact]
    public void NestedFailureRestoresOuterStagedStateAndOuterFailureRestoresDurableEntry()
    {
        using var fixture = new RankedIdentityFixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            Content(Data(fixture.Store))["synthetic-nesting"] = "durable";
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var generation = RankedIdentityTestState.CommittedProjection(fixture.Store);
        Assert.Throws<IOException>(() => fixture.Store.ExecuteAdminTransaction<bool>(() =>
        {
            Content(Data(fixture.Store))["synthetic-nesting"] = "outer-staged";
            Assert.Throws<IOException>(() => fixture.Store.ExecuteAdminTransaction<bool>(() =>
            {
                Content(Data(fixture.Store))["synthetic-nesting"] = "inner-staged";
                RankedIdentityTestState.RequestSave(fixture.Store);
                throw new IOException("synthetic inner failure");
            }));
            Assert.Equal("outer-staged", Content(Data(fixture.Store))["synthetic-nesting"]);
            Assert.Same(generation, RankedIdentityTestState.CommittedProjection(fixture.Store));
            throw new IOException("synthetic outer failure");
        }));
        Assert.Equal("durable", Content(Data(fixture.Store))["synthetic-nesting"]);
        Assert.Same(generation, RankedIdentityTestState.CommittedProjection(fixture.Store));
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            Content(Data(fixture.Store))["synthetic-nesting"] = "outer-success";
            fixture.Store.ExecuteAdminTransaction(() =>
            {
                Content(Data(fixture.Store))["synthetic-nesting"] = "inner-success";
                RankedIdentityTestState.RequestSave(fixture.Store);
                return true;
            });
            return true;
        });
        var reopened = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.Equal("inner-success", Content(Data(reopened))["synthetic-nesting"]);
    }

    [Fact]
    public void FourMiBNoopTransactionAvoidsThePreviousWholeUtf16RollbackAllocation()
    {
        using var fixture = new RankedIdentityFixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            Content(Data(fixture.Store))["synthetic-memory"] = new string('a', 4 * 1024 * 1024);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var data = Data(fixture.Store);
        _ = Capture(data); // Warm serializer/compressor metadata.
        var baselineStart = GC.GetAllocatedBytesForCurrentThread();
        var baseline = JsonSerializer.Serialize(data, data.GetType());
        var previousBytes = GC.GetAllocatedBytesForCurrentThread() - baselineStart;
        var optimizedStart = GC.GetAllocatedBytesForCurrentThread();
        var optimized = fixture.Store.ExecuteAdminTransaction(() => true);
        var optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - optimizedStart;
        GC.KeepAlive(baseline);
        Assert.True(optimized);
        output.WriteLine("synthetic_payload_bytes=4194304 old_raw_snapshot_allocated_bytes={0} optimized_noop_allocated_bytes={1}",
            previousBytes, optimizedBytes);
        Assert.True(previousBytes >= 8 * 1024 * 1024);
        Assert.True(optimizedBytes < previousBytes / 4);
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(store)!;
    private static IDictionary Content(object data)
        => (IDictionary)data.GetType().GetProperty("Content")!.GetValue(data)!;
    private static byte[] Capture(object data)
        => (byte[])typeof(L12PlatformStore).GetMethod("SerializeCompleteInMemoryState", Static)!
            .Invoke(null, new[] { data })!;
    private static object Restore(byte[] captured)
        => typeof(L12PlatformStore).GetMethod("DeserializeCompleteInMemoryState", Static)!
            .Invoke(null, new object[] { captured })!;
}
