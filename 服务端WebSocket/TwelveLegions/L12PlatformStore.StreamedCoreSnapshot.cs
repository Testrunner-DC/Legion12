using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Prepared and consumed synchronously under the existing write lock/SQLite transaction.
    // The first pass measures exact canonical bytes/hash; the second streams the same bytes
    // into the existing TEXT column. No file, schema change or whole-document managed buffer.
    private sealed record StreamedCoreSnapshot(DataFile Data, int ByteLength);

    private static string CoreSnapshotSqlValue(CoreSnapshot snapshot)
        => snapshot.JsonValue is StreamedCoreSnapshot ? "CAST(zeroblob($json) AS TEXT)" : "CAST($json AS TEXT)";

    private static object CoreSnapshotParameter(CoreSnapshot snapshot)
        => snapshot.JsonValue is StreamedCoreSnapshot streamed ? streamed.ByteLength : snapshot.JsonValue;

    private static void WriteCoreSnapshotPayload(SqliteConnection connection, CoreSnapshot snapshot)
    {
        if (snapshot.JsonValue is not StreamedCoreSnapshot streamed) return;
        using var metric = L12PlatformOperationMetrics.Measure(L12PlatformOperationPhase.SnapshotSerialize);
        using var blob = new SqliteBlob(connection, "platform_state", "snapshot_json", 1);
        if (blob.Length != streamed.ByteLength)
            throw new InvalidDataException("平台流式快照预留长度不一致");
        using var writer = new CoreSnapshotHashStream(blob);
        JsonSerializer.Serialize(writer, streamed.Data, PlatformSnapshotJsonOptions);
        if (writer.Length != streamed.ByteLength || !FixedEquals(writer.Checksum(), snapshot.Checksum))
            throw new InvalidDataException("平台流式快照内容在准备后发生变化");
        // Caller retains ownership of the transaction; any mismatch/partial write rolls back.
    }

    private sealed class CoreSnapshotHashStream(Stream? target = null) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _length;
        private string? _checksum;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _checksum is null;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() => target?.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_checksum is not null) throw new InvalidOperationException("流式快照哈希已结束");
            target?.Write(buffer);
            _hash.AppendData(buffer);
            _length = checked(_length + buffer.Length);
        }
        internal string Checksum()
            => _checksum ??= Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing); // Do not dispose caller-owned SQLite handles.
        }
    }
}
