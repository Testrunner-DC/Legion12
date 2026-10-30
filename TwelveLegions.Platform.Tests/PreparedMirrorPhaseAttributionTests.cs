using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PreparedMirrorPhaseAttributionTests(ITestOutputHelper output)
{
    private const int BufferSize = 64 * 1024;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type ProductionHashStream = typeof(L12PreparedPlatformMirror)
        .GetNestedType("HashingWriteStream", BindingFlags.NonPublic)!;
    private static readonly ConstructorInfo HashStreamConstructor = ProductionHashStream.GetConstructor(
        Members, binder: null, [typeof(Stream), typeof(bool), typeof(long?)], modifiers: null)!;
    private static readonly MethodInfo CompleteHash = ProductionHashStream.GetMethod("CompleteHash", Members)!;
    private static readonly PropertyInfo BytesWritten = ProductionHashStream.GetProperty("BytesWritten", Members)!;
    private readonly record struct Measurement<T>(T Value, long AllocatedBytes, double ElapsedMs);
    private readonly record struct HashedDrain(long ByteCount, byte[] Checksum);

    [Theory]
    [InlineData(16)]
    [InlineData(192)]
    public void SamePreparedPackageReportsAtomicWriteAndProductionHashControls(int chunks)
    {
        var root = Path.Combine(Path.GetTempPath(), "mirror-phases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var succeeded = false;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            WarmSmallPaths(root, options); // Unmeasured tiny warmup, not a second representative package.
            var value = SyntheticDictionary(chunks);
            var expected = JsonSerializer.SerializeToUtf8Bytes(value, options);
            var expectedHash = SHA256.HashData(expected);
            var create = Measure(() => L12PreparedPlatformMirror.Create(value, value.GetType(), options));
            var prepared = create.Value;
            Assert.Equal(expected.LongLength, prepared.ByteCount);
            Assert.Equal(Convert.ToHexString(expectedHash).ToLowerInvariant(), prepared.Checksum);
            Assert.True(MemoryMarshal.TryGetArray(prepared.CompressedPayload, out var compressed));
            Assert.NotNull(compressed.Array);
            Assert.Equal(prepared.CompressedLength, compressed.Count);
            var packageHash = SHA256.HashData(prepared.CompressedPayload.Span);

            // Both controls read the very same owned compressed array without copying it.
            // Only the null counter is QA code; SHA and its length bound use the original product stream.
            var count = Measure(() => InflateAndCount(compressed));
            var hash = Measure(() => InflateThroughProductionHash(compressed, prepared.ByteCount));
            var path = Path.Combine(root, "mirror.json");
            File.WriteAllText(path, "synthetic previously committed mirror", new UTF8Encoding(false));
            var atomic = Measure(() => { prepared.WriteAtomically(path); return true; });

            Assert.Equal(prepared.ByteCount, count.Value);
            Assert.Equal(prepared.ByteCount, hash.Value.ByteCount);
            Assert.True(CryptographicOperations.FixedTimeEquals(expectedHash, hash.Value.Checksum));
            Assert.Equal(expected, File.ReadAllBytes(path));
            using (var persisted = File.OpenRead(path))
                Assert.Equal(expectedHash, SHA256.HashData(persisted));
            Assert.Equal(expected, JsonSerializer.SerializeToUtf8Bytes(value, options));
            Assert.Equal(packageHash, SHA256.HashData(prepared.CompressedPayload.Span));
            Assert.Empty(Directory.EnumerateFiles(root, "mirror.json.*.tmp"));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                fixture = "deterministic varied string dictionary, not the earlier DataFile fixture",
                chunks,
                jsonBytes = prepared.ByteCount,
                compressedBytes = prepared.CompressedLength,
                prepared.Checksum,
                create = Sample(create),
                gzipToCountingNull = Sample(count),
                gzipToOriginalHashStreamNull = Sample(hash),
                realAtomicWrite = Sample(atomic),
                countedBytes = count.Value,
                hashedBytes = hash.Value.ByteCount,
                controlsUseSamePreparedInstance = true,
                limits = "single local observations; not peak memory, online latency, or pure disk cost by subtraction; controls include stream setup/disposal and the hash control includes reflection; correctness hashes/readbacks are outside measured segments",
            }));
            succeeded = true;
        }
        finally
        {
            // On failure the supervising test run retains its evidence/temp tree.
            if (succeeded) Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, string> SyntheticDictionary(int chunks)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var random = new Random(20261031);
        var bytes = new byte[48 * 1024];
        for (var i = 0; i < chunks; i++)
        {
            random.NextBytes(bytes);
            result[$"synthetic-{i:D3}"] = Convert.ToBase64String(bytes);
        }
        result["unicode-control"] = "中文🙂\n缩进\"与反斜线\\";
        return result;
    }

    private static MemoryStream OpenCompressed(ArraySegment<byte> compressed)
        => new(compressed.Array!, compressed.Offset, compressed.Count, writable: false, publiclyVisible: true);

    private static long InflateAndCount(ArraySegment<byte> compressed)
    {
        using var input = OpenCompressed(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var counter = new CountingNullStream();
        gzip.CopyTo(counter, BufferSize);
        return counter.BytesWritten;
    }

    private static HashedDrain InflateThroughProductionHash(ArraySegment<byte> compressed, long maximumBytes)
    {
        using var input = OpenCompressed(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var hashing = (Stream)HashStreamConstructor.Invoke([Stream.Null, true, maximumBytes]);
        gzip.CopyTo(hashing, BufferSize);
        var checksum = (byte[])CompleteHash.Invoke(hashing, null)!;
        return new((long)BytesWritten.GetValue(hashing)!, checksum);
    }

    private static void WarmSmallPaths(string root, JsonSerializerOptions options)
    {
        var value = new Dictionary<string, string> { ["warm"] = "中文🙂" };
        var prepared = L12PreparedPlatformMirror.Create(value, value.GetType(), options);
        Assert.True(MemoryMarshal.TryGetArray(prepared.CompressedPayload, out var compressed));
        _ = InflateAndCount(compressed);
        _ = InflateThroughProductionHash(compressed, prepared.ByteCount);
        var path = Path.Combine(root, "warm.json");
        prepared.WriteAtomically(path);
        File.Delete(path);
    }

    private static Measurement<T> Measure<T>(Func<T> action)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var tick = Stopwatch.GetTimestamp();
        var value = action();
        return new(value, GC.GetAllocatedBytesForCurrentThread() - bytes, Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
    }

    private static object Sample<T>(Measurement<T> measurement)
        => new { measurement.ElapsedMs, measurement.AllocatedBytes };

    private sealed class CountingNullStream : Stream
    {
        internal long BytesWritten { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten = checked(BytesWritten + buffer.Length);
        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
