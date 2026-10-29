using System.Diagnostics;
using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformCompressionBufferTests(ITestOutputHelper output)
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131079)]
    public void SegmentedBufferPreservesBoundariesAndClearsWholeReturnedArrays(int length)
    {
        var pool = new TrackingPool();
        var expected = new byte[length];
        new Random(921).NextBytes(expected);
        var buffer = new L12SegmentedWriteBuffer(pool);
        for (var offset = 0; offset < length;)
        {
            var count = Math.Min(997, length - offset);
            buffer.Write(expected, offset, count);
            offset += count;
        }
        buffer.Write(ReadOnlySpan<byte>.Empty);
        buffer.Flush();
        Assert.Equal(length, buffer.Length);
        Assert.Equal(expected, buffer.ToArray());
        var prefixed = buffer.ToArray(32);
        Assert.Equal(new byte[32], prefixed.AsSpan(0, 32).ToArray());
        Assert.Equal(expected, prefixed.AsSpan(32).ToArray());
        buffer.Dispose();
        buffer.Dispose();
        Assert.Equal(pool.RentCount, pool.ReturnCount);
        Assert.True(pool.AllReturnedArraysWereZero);
        Assert.Equal(expected, prefixed.AsSpan(32).ToArray());
        Assert.False(buffer.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => buffer.ToArray());
        Assert.Throws<ObjectDisposedException>(() => buffer.WriteByte(1));
        Assert.Throws<ObjectDisposedException>(() => buffer.Flush());
    }

    [Fact]
    public void RentFailureStillReturnsAndClearsPreviouslyWrittenSegments()
    {
        var pool = new TrackingPool { FailRent = 2 };
        using (var buffer = new L12SegmentedWriteBuffer(pool))
            Assert.Throws<IOException>(() => buffer.Write(new byte[L12SegmentedWriteBuffer.SegmentSize + 1]));
        Assert.Equal(1, pool.ReturnCount);
        Assert.True(pool.AllReturnedArraysWereZero);
    }

    [Fact]
    public void BufferRejectsInvalidOperationsWithoutMutatingWrittenBytes()
    {
        using var buffer = new L12SegmentedWriteBuffer(new TrackingPool());
        buffer.WriteByte(42);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ToArray(-1));
        Assert.Throws<OverflowException>(() => buffer.ToArray(int.MaxValue));
        Assert.Throws<ArgumentNullException>(() => buffer.Write(null!, 0, 0));
        Assert.Throws<NotSupportedException>(() => buffer.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => buffer.SetLength(0));
        Assert.Throws<NotSupportedException>(() => buffer.Read(new byte[1], 0, 1));
        Assert.Equal(new byte[] { 42 }, buffer.ToArray());
    }

    private sealed class TrackingPool : ArrayPool<byte>
    {
        internal int RentCount { get; private set; }
        internal int ReturnCount { get; private set; }
        internal int FailRent { get; init; }
        internal bool AllReturnedArraysWereZero { get; private set; } = true;
        private readonly HashSet<byte[]> rented = new(ReferenceEqualityComparer.Instance);
        public override byte[] Rent(int minimumLength)
        {
            RentCount++;
            if (RentCount == FailRent) throw new IOException("synthetic rent failure");
            var bytes = new byte[minimumLength + 17];
            Array.Fill(bytes, (byte)0x7b);
            Assert.True(rented.Add(bytes));
            return bytes;
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.True(rented.Remove(array));
            AllReturnedArraysWereZero &= array.All(value => value == 0);
            ReturnCount++;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VariedCompressionKeepsExactLegacyBytesWithoutGrowingBufferCopies(bool mirror)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-compression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var state = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(store)!;
            var content = (Dictionary<string, string>)state.GetType().GetProperty("Content")!.GetValue(state)!;
            var random = new Random(20261029);
            var bytes = new byte[48 * 1024];
            for (var index = 0; index < 192; index++)
            {
                random.NextBytes(bytes);
                content["synthetic-varied-" + index] = Convert.ToBase64String(bytes);
            }
            var options = (JsonSerializerOptions)typeof(L12PlatformStore).GetField(
                mirror ? "PlatformMirrorJsonOptions" : "PlatformMigrationJsonOptions", Static)!.GetValue(null)!;
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            byte[] actual;
            L12PreparedPlatformMirror? prepared = null;
            if (mirror)
            {
                prepared = L12PreparedPlatformMirror.Create(state, state.GetType(), options);
                // Keep this copy outside the measured preparation boundary.
                actual = [];
            }
            else actual = (byte[])typeof(L12PlatformStore).GetMethod("SerializeCompleteInMemoryState", Static)!
                .Invoke(null, [state])!;
            timer.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (prepared is not null) actual = prepared.CompressedPayload.ToArray();
            using var legacy = new MemoryStream();
            using (var gzip = new GZipStream(legacy, CompressionLevel.Fastest, leaveOpen: true))
                JsonSerializer.Serialize(gzip, state, state.GetType(), options);
            var payload = legacy.ToArray();
            if (mirror)
            {
                Assert.Equal(payload, actual);
                var json = JsonSerializer.SerializeToUtf8Bytes(state, state.GetType(), options);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant(), prepared!.Checksum);
                var destination = Path.Combine(root, "validated-mirror.json");
                prepared.WriteAtomically(destination);
                Assert.Equal(json, File.ReadAllBytes(destination));
            }
            else
            {
                Assert.Equal(payload, actual.AsSpan(32).ToArray());
                Assert.Equal(SHA256.HashData(payload), actual.AsSpan(0, 32).ToArray());
                var restored = typeof(L12PlatformStore).GetMethod("DeserializeCompleteInMemoryState", Static)!
                    .Invoke(null, [actual])!;
                Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(state, state.GetType()),
                    JsonSerializer.SerializeToUtf8Bytes(restored, restored.GetType()));
            }
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            var maximum = actual.LongLength * 5 / 2 + 512 * 1024;
            output.WriteLine(JsonSerializer.Serialize(new { mirror, allocatedBytes = allocated,
                compressedBytes = actual.Length, maximumAllocatedBytes = maximum, elapsedMs = timer.Elapsed.TotalMilliseconds }));
            Assert.True(allocated < maximum,
                $"Compression allocated {allocated} bytes for {actual.Length} output bytes; growing-buffer copies remain.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
