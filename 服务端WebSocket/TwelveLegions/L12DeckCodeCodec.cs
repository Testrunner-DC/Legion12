using System.Text;

namespace TwelveLegions.Server;

public sealed record L12DecodedDeckCode(string Name, string MasterId, IReadOnlyList<string> CardIds,
    IReadOnlyList<string> MoraleIds, IReadOnlyList<string> SpecialIds);

public static class L12DeckCodeCodec
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int MaxBytes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryDecode(string? code, out L12DecodedDeckCode? deck)
    {
        deck = null;
        var value = code?.Trim() ?? string.Empty;
        if (!value.StartsWith("L12D2-", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var encoded = new string(value[6..].Where(character => character != '-' && !char.IsWhiteSpace(character))
                .Select(char.ToUpperInvariant).ToArray());
            if (encoded.Length is 0 or > MaxBytes * 2) return false;
            var bytes = DecodeBase30(encoded);
            if (bytes.Length < 6) return false;
            var body = bytes.AsSpan(0, bytes.Length - 4);
            var expected = ((uint)bytes[^4] << 24) | ((uint)bytes[^3] << 16) | ((uint)bytes[^2] << 8) | bytes[^1];
            if (Crc32(body) != expected) return false;
            var reader = new Reader(body.ToArray());
            if (reader.Byte() != 2) return false;
            var name = reader.Text(256);
            var masterId = ReadCardId(reader);
            var cards = ReadCards(reader);
            var morale = ReadCards(reader);
            var special = ReadCards(reader);
            if (!reader.Done || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(masterId)) return false;
            deck = new(name[..Math.Min(24, name.Length)], masterId, cards, morale, special);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DecodeBase30(string value)
    {
        var bytes = new List<int> { 0 };
        foreach (var character in value)
        {
            var digit = Alphabet.IndexOf(character);
            if (digit < 0) throw new InvalidDataException("牌库码字符无效");
            var carry = digit;
            for (var index = 0; index < bytes.Count; index++)
            {
                var current = bytes[index] * 30 + carry;
                bytes[index] = current & 0xff;
                carry = current >> 8;
            }
            while (carry > 0)
            {
                bytes.Add(carry & 0xff);
                carry >>= 8;
            }
            if (bytes.Count > MaxBytes) throw new InvalidDataException("牌库码超出长度限制");
        }
        bytes.Reverse();
        return bytes.Select(item => (byte)item).ToArray();
    }

    private static string ReadCardId(Reader reader)
    {
        var kind = reader.Byte();
        if (kind == 1) return reader.Text(128);
        if (kind != 0) throw new InvalidDataException("牌库码卡牌标识无效");
        var season = reader.Varint();
        var suffix = reader.Varint();
        if (season > 99 || suffix >= 1_679_616) throw new InvalidDataException("牌库码卡牌标识超出限制");
        return $"S{season:00}-{ToBase36(suffix).PadLeft(4, '0')}";
    }

    private static List<string> ReadCards(Reader reader)
    {
        var groups = reader.Varint();
        if (groups > 256) throw new InvalidDataException("牌库码卡牌种类超出限制");
        var result = new List<string>();
        for (var index = 0; index < groups; index++)
        {
            var cardId = ReadCardId(reader);
            var quantity = reader.Varint();
            if (string.IsNullOrWhiteSpace(cardId) || quantity is < 1 or > 512 || result.Count + quantity > 512)
                throw new InvalidDataException("牌库码卡牌数量无效");
            for (var copy = 0; copy < quantity; copy++) result.Add(cardId);
        }
        return result;
    }

    private static string ToBase36(int value)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        do
        {
            buffer[--position] = digits[value % 36];
            value /= 36;
        } while (value > 0);
        return new string(buffer[position..]);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return crc ^ 0xffffffffu;
    }

    private sealed class Reader(byte[] bytes)
    {
        private int _offset;
        public bool Done => _offset == bytes.Length;

        public byte Byte()
        {
            if (_offset >= bytes.Length) throw new EndOfStreamException();
            return bytes[_offset++];
        }

        public int Varint()
        {
            var value = 0;
            var factor = 1;
            for (var index = 0; index < 5; index++)
            {
                var next = Byte();
                checked { value += (next & 0x7f) * factor; }
                if ((next & 0x80) == 0) return value;
                checked { factor *= 128; }
            }
            throw new InvalidDataException("牌库码数值超出限制");
        }

        public string Text(int limit)
        {
            var length = Varint();
            if (length > limit || _offset + length > bytes.Length) throw new InvalidDataException("牌库码文本超出限制");
            var result = StrictUtf8.GetString(bytes, _offset, length);
            _offset += length;
            return result;
        }
    }
}
