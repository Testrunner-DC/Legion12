using System.Buffers;
using System.Security.Cryptography;

namespace TwelveLegions.Server;

// Operation-owned compressed bytes. A small bounded pool avoids growing/copying
// large contiguous buffers; every returned segment is explicitly zeroed first.
internal sealed class L12SegmentedWriteBuffer(ArrayPool<byte>? pool = null) : Stream
{
    internal const int SegmentSize = 64 * 1024;
    private static readonly ArrayPool<byte> CompressionPool = ArrayPool<byte>.Create(SegmentSize, 8);
    private readonly ArrayPool<byte> _pool = pool ?? CompressionPool;
    private readonly List<byte[]> _segments = new(4);
    private int _length;
    private bool _disposed;

    internal byte[] ToArray(int prefixLength = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        var result = new byte[checked(prefixLength + _length)];
        var remaining = _length;
        var offset = prefixLength;
        foreach (var segment in _segments)
        {
            var count = Math.Min(remaining, SegmentSize);
            segment.AsSpan(0, count).CopyTo(result.AsSpan(offset));
            offset += count;
            remaining -= count;
        }
        return result;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Length > int.MaxValue - _length)
            throw new IOException("压缩缓存超过可寻址范围");
        while (!buffer.IsEmpty)
        {
            var inSegment = _length % SegmentSize;
            if (inSegment == 0)
            {
                var segment = _pool.Rent(SegmentSize);
                try { _segments.Add(segment); }
                catch
                {
                    CryptographicOperations.ZeroMemory(segment);
                    _pool.Return(segment);
                    throw;
                }
            }
            var count = Math.Min(buffer.Length, SegmentSize - inSegment);
            buffer[..count].CopyTo(_segments[^1].AsSpan(inSegment));
            _length += count;
            buffer = buffer[count..];
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Flush() => ObjectDisposedException.ThrowIf(_disposed, this);
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _length; }
    }
    public override long Position { get => Length; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            foreach (var segment in _segments)
            {
                CryptographicOperations.ZeroMemory(segment);
                _pool.Return(segment);
            }
            _segments.Clear();
            _length = 0;
        }
        base.Dispose(disposing);
    }
}
