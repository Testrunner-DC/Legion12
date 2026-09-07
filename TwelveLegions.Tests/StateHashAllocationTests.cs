using System.Security.Cryptography;
using System.Text;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StateHashAllocationTests
{
    [Fact]
    public void LongHistoryHashMatchesCanonicalJsonWithoutAllocatingAnotherFullState()
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var engine = new L12GameEngine(catalog, "hash-memory-fixture", "HASH01", 98765,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        for (var i = 0; i < 1200; i++)
            engine.State.Events.Add(new L12ActionEvent(1000 + i, "fixture", 0,
                new string('测', 1000), []));
        var canonical = Encoding.UTF8.GetBytes(engine.SerializeFullState());
        var expected = Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
        Assert.Equal(expected, engine.ComputeStateHash()); // warm serializer metadata/buffers
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 3; i++) Assert.Equal(expected, engine.ComputeStateHash());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < canonical.LongLength,
            $"Repeated hash allocated {allocated} bytes for {canonical.LongLength}-byte state");
        Assert.Equal(1200, engine.State.Events.Count(item => item.Type == "fixture"));
    }
}
