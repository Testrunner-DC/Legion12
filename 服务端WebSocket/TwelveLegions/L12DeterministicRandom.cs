using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TwelveLegions.Server;

internal readonly record struct L12RandomState(
    int Version, ulong State0, ulong State1, ulong State2, ulong State3, long DrawCount);

/// <summary>
/// v2 对局使用的稳定 xoshiro256** 随机源。检查点保存四个内部状态字，而不是根据
/// 不同 Random API 的“调用次数”猜测运行时内部状态。
/// </summary>
internal sealed class L12DeterministicRandom : Random
{
    internal const int StateVersion = 1;
    private const int StatePayloadBytes = 40;
    private const int StateBlobBytes = StatePayloadBytes + 32;
    private ulong _state0;
    private ulong _state1;
    private ulong _state2;
    private ulong _state3;

    internal long DrawCount { get; private set; }

    internal L12DeterministicRandom(int seed)
    {
        var seedState = unchecked((ulong)(uint)seed) ^ 0x6A09E667F3BCC909UL;
        _state0 = SplitMix64(ref seedState);
        _state1 = SplitMix64(ref seedState);
        _state2 = SplitMix64(ref seedState);
        _state3 = SplitMix64(ref seedState);
        EnsureValidState();
    }

    internal L12DeterministicRandom(L12RandomState state)
    {
        if (state.Version != StateVersion)
            throw new InvalidDataException("随机状态版本不受支持");
        if (state.DrawCount < 0) throw new InvalidDataException("随机状态计数无效");
        _state0 = state.State0;
        _state1 = state.State1;
        _state2 = state.State2;
        _state3 = state.State3;
        DrawCount = state.DrawCount;
        EnsureValidState();
    }

    internal L12RandomState CaptureState()
        => new(StateVersion, _state0, _state1, _state2, _state3, DrawCount);

    internal static byte[] EncodeState(L12RandomState state)
    {
        _ = new L12DeterministicRandom(state);
        var blob = new byte[StateBlobBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(blob.AsSpan(0, 8), state.State0);
        BinaryPrimitives.WriteUInt64LittleEndian(blob.AsSpan(8, 8), state.State1);
        BinaryPrimitives.WriteUInt64LittleEndian(blob.AsSpan(16, 8), state.State2);
        BinaryPrimitives.WriteUInt64LittleEndian(blob.AsSpan(24, 8), state.State3);
        BinaryPrimitives.WriteInt64LittleEndian(blob.AsSpan(32, 8), state.DrawCount);
        SHA256.HashData(blob.AsSpan(0, StatePayloadBytes), blob.AsSpan(StatePayloadBytes, 32));
        return blob;
    }

    internal static L12RandomState DecodeState(int version, byte[] blob, long recordedDrawCount)
    {
        if (version != StateVersion) throw new InvalidDataException("随机状态版本不受支持");
        if (blob.Length != StateBlobBytes) throw new InvalidDataException("随机状态长度无效");
        Span<byte> expectedHash = stackalloc byte[32];
        SHA256.HashData(blob.AsSpan(0, StatePayloadBytes), expectedHash);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, blob.AsSpan(StatePayloadBytes, 32)))
            throw new InvalidDataException("随机状态校验失败");
        var drawCount = BinaryPrimitives.ReadInt64LittleEndian(blob.AsSpan(32, 8));
        if (drawCount != recordedDrawCount) throw new InvalidDataException("随机状态计数不一致");
        return new L12RandomState(version,
            BinaryPrimitives.ReadUInt64LittleEndian(blob.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(blob.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(blob.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(blob.AsSpan(24, 8)), drawCount);
    }

    public override int Next() => (int)NextBounded(int.MaxValue);

    public override int Next(int maxValue)
    {
        if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
        return maxValue == 0 ? 0 : (int)NextBounded((ulong)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        if (minValue == maxValue) return minValue;
        var range = (ulong)((long)maxValue - minValue);
        return (int)(minValue + (long)NextBounded(range));
    }

    public override long NextInt64() => (long)NextBounded(long.MaxValue);

    public override long NextInt64(long maxValue)
    {
        if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
        return maxValue == 0 ? 0 : (long)NextBounded((ulong)maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        if (minValue == maxValue) return minValue;
        var range = unchecked((ulong)maxValue - (ulong)minValue);
        return unchecked((long)((ulong)minValue + NextBounded(range)));
    }

    protected override double Sample() => NextUnitDouble();
    public override double NextDouble() => NextUnitDouble();
    public override float NextSingle() => (NextUInt64() >> 40) * (1.0f / 16_777_216.0f);

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        NextBytes(buffer.AsSpan());
    }

    public override void NextBytes(Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var value = NextUInt64();
            for (var byteIndex = 0; byteIndex < sizeof(ulong) && offset < buffer.Length; byteIndex++)
            {
                buffer[offset++] = (byte)value;
                value >>= 8;
            }
        }
    }

    private double NextUnitDouble() => (NextUInt64() >> 11) * (1.0 / 9_007_199_254_740_992.0);

    private ulong NextBounded(ulong exclusiveMaximum)
    {
        var threshold = unchecked(0UL - exclusiveMaximum) % exclusiveMaximum;
        while (true)
        {
            var value = NextUInt64();
            if (value >= threshold) return value % exclusiveMaximum;
        }
    }

    private ulong NextUInt64()
    {
        var result = unchecked(RotateLeft(unchecked(_state1 * 5), 7) * 9);
        var shifted = _state1 << 17;
        _state2 ^= _state0;
        _state3 ^= _state1;
        _state1 ^= _state2;
        _state0 ^= _state3;
        _state2 ^= shifted;
        _state3 = RotateLeft(_state3, 45);
        DrawCount++;
        return result;
    }

    private void EnsureValidState()
    {
        if ((_state0 | _state1 | _state2 | _state3) == 0)
            throw new InvalidDataException("随机状态不能全部为零");
    }

    private static ulong RotateLeft(ulong value, int count)
        => (value << count) | (value >> (64 - count));

    private static ulong SplitMix64(ref ulong state)
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        var value = state;
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
