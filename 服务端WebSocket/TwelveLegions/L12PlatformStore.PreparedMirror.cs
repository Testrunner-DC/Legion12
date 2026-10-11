using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed class L12PreparedPlatformMirror
{
    private const int StreamBufferSize = 64 * 1024;
    private readonly byte[] _compressedPayload;
    private readonly byte[] _checksumBytes;

    internal ReadOnlyMemory<byte> CompressedPayload
        => _compressedPayload.AsMemory(0, CompressedLength);
    internal int CompressedLength { get; }
    internal string Checksum { get; }
    internal long ByteCount { get; }

    internal L12PreparedPlatformMirror(byte[] compressedPayload, int compressedLength,
        string checksum, long byteCount)
    {
        ArgumentNullException.ThrowIfNull(compressedPayload);
        if (compressedLength <= 0 || compressedLength > compressedPayload.Length)
            throw new ArgumentOutOfRangeException(nameof(compressedLength));
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        try { _checksumBytes = Convert.FromHexString(checksum); }
        catch (FormatException error)
        {
            throw new ArgumentException("兼容镜像校验和格式无效", nameof(checksum), error);
        }
        if (_checksumBytes.Length != SHA256.HashSizeInBytes)
            throw new ArgumentException("兼容镜像校验和长度无效", nameof(checksum));

        _compressedPayload = compressedPayload;
        CompressedLength = compressedLength;
        Checksum = Convert.ToHexString(_checksumBytes).ToLowerInvariant();
        ByteCount = byteCount;
    }

    internal static L12PreparedPlatformMirror Create(object value, Type inputType,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentNullException.ThrowIfNull(options);

        using var payload = new MemoryStream();
        byte[] checksum;
        long byteCount;
        using (var compressed = new GZipStream(payload, CompressionLevel.Fastest, leaveOpen: true))
        using (var output = new HashingWriteStream(compressed, leaveOpen: true))
        {
            JsonSerializer.Serialize(output, value, inputType, options);
            checksum = output.CompleteHash();
            byteCount = output.BytesWritten;
        }

        return new L12PreparedPlatformMirror(payload.GetBuffer(), checked((int)payload.Length),
            Convert.ToHexString(checksum).ToLowerInvariant(), byteCount);
    }

    internal void WriteAtomically(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] checksum;
            long byteCount;
            using (var input = new MemoryStream(_compressedPayload, 0, CompressedLength,
                       writable: false, publiclyVisible: true))
            using (var decompressed = new GZipStream(input, CompressionMode.Decompress))
            using (var writer = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, StreamBufferSize, FileOptions.SequentialScan))
            using (var output = new HashingWriteStream(writer, leaveOpen: true, maximumBytes: ByteCount))
            {
                decompressed.CopyTo(output, StreamBufferSize);
                checksum = output.CompleteHash();
                byteCount = output.BytesWritten;
            }

            if (byteCount != ByteCount || !CryptographicOperations.FixedTimeEquals(checksum, _checksumBytes))
                throw new InvalidDataException("兼容镜像解压后的长度或校验和不匹配");
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // 每次提交只拥有自己的随机临时文件，失败不能清理另一提交的文件。
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { /* 清理失败不能替换原始镜像异常。 */ }
        }
    }

    private sealed class HashingWriteStream(Stream destination, bool leaveOpen, long? maximumBytes = null)
        : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;
        internal long BytesWritten { get; private set; }

        internal byte[] CompleteHash()
        {
            if (_completed) throw new InvalidOperationException("兼容镜像哈希已经完成");
            _completed = true;
            return _hash.GetHashAndReset();
        }

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_completed) throw new InvalidOperationException("兼容镜像哈希已经完成");
            if (maximumBytes is { } maximum && buffer.Length > maximum - BytesWritten)
                throw new InvalidDataException("兼容镜像解压长度超过预期");
            _hash.AppendData(buffer);
            destination.Write(buffer);
            BytesWritten = checked(BytesWritten + buffer.Length);
        }

        public override void Flush() => destination.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
                if (!leaveOpen) destination.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

public sealed partial class L12PlatformStore
{
    private static L12PreparedPlatformMirror PrepareFallbackMirror(DataFile data)
        => L12PreparedPlatformMirror.Create(data, typeof(DataFile), PlatformMirrorJsonOptions);

    private void WriteFallbackMirror(L12PreparedPlatformMirror mirror)
    {
        mirror.WriteAtomically(_path);
        RecordSyntheticMirrorWrite(mirror.ByteCount);
    }
}
