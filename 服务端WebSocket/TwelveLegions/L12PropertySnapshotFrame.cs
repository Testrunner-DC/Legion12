using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

/// <summary>
/// Fixed public property-name substitution on an already recipient-projected packet.
/// Values are copied byte-for-byte: no adaptive dictionary or cross-packet state.
/// </summary>
internal static class L12PropertySnapshotFrame
{
    internal const string Capability = "gameStatePropertyFrameV1";
    internal const string WireType = Capability;
    internal const int MaximumWireBytes = 2 * 1024 * 1024;
    internal const int MaximumTokens = 100000;
    // Original payload limit is unchanged. Each property-name token grows by
    // at most two bytes; wrapper plus <=256 UTF-16 matchId units fits 2048 bytes.
    // This independent wire bound is unreachable for a valid bounded input.
    internal const int MaximumEncodedWireBytes = MaximumWireBytes + 2 * MaximumTokens + 2048;
    internal const int MaximumDepth = 64;
    internal const string VocabularySha256 = "9193B87AD3D50DD4B14D445A2B09FDCE62B2503746B2AA61D0B6F72E0FD9DC45";
    private static readonly byte[] Header = "{\"type\":\"gameStatePropertyFrameV1\",\"version\":1,\"matchId\":"u8.ToArray();
    internal static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(LoadNames());
    private static readonly FrozenDictionary<string, byte[]> EncodedNames = Names
        .Select((name, index) => (name, token: Encoding.UTF8.GetBytes(JsonSerializer.Serialize("@" + Base36(index)))))
        .ToFrozenDictionary(item => item.name, item => item.token, StringComparer.Ordinal);

    private static string[] LoadNames()
    {
        using var source = typeof(L12PropertySnapshotFrame).Assembly
            .GetManifestResourceStream("TwelveLegions.Server.GameStatePropertyNamesV1.json")
            ?? throw new InvalidOperationException("Missing immutable property frame vocabulary");
        var names = JsonSerializer.Deserialize<string[]>(source)
            ?? throw new InvalidDataException("Invalid property frame vocabulary");
        if (names.Length != 527 || names.Any(name => string.IsNullOrEmpty(name)
                || name.StartsWith('@') || name.StartsWith('~'))
            || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("Property frame vocabulary identity changed");
        if (names.Select((name, index) =>
                JsonSerializer.SerializeToUtf8Bytes("@" + Base36(index)).Length
                - JsonSerializer.SerializeToUtf8Bytes(name).Length).Any(growth => growth > 2))
            throw new InvalidDataException("Property frame vocabulary exceeds the wire-bound proof");
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(names)));
        if (fingerprint != VocabularySha256)
            throw new InvalidDataException("Version1 property vocabulary is immutable");
        return names;
    }

    private static string Base36(int value)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> result = stackalloc char[8];
        var offset = result.Length;
        do { result[--offset] = alphabet[value % 36]; value /= 36; } while (value != 0);
        return new string(result[offset..]);
    }

    internal static bool TryEncode(byte[] original, out byte[] encoded)
    {
        encoded = [];
        if (original.Length == 0 || original.Length > MaximumWireBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(original);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String) return false;
            var identityRoot = type.GetString() switch
            {
                "gameState" => root.TryGetProperty("state", out var state) ? state : default,
                "gameStateDelta" => root,
                "gameStateCardFrameV1" => root.TryGetProperty("payload", out var payload)
                    && payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("state", out var state)
                        ? state : default,
                _ => default,
            };
            if (identityRoot.ValueKind != JsonValueKind.Object
                || !identityRoot.TryGetProperty("matchId", out var matchId)
                || matchId.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(matchId.GetString())
                || matchId.GetString()!.Length > 256) return false;
            var reader = new Utf8JsonReader(original, new JsonReaderOptions { MaxDepth = MaximumDepth });
            var output = new ArrayBufferWriter<byte>(original.Length);
            output.Write(Header); output.Write(JsonSerializer.SerializeToUtf8Bytes(matchId));
            output.Write(",\"payload\":"u8);
            var copied = 0;
            var rootTypeCount = 0;
            var rootTypePending = false;
            var acceptedType = false;
            var tokens = 0;
            while (reader.Read())
            {
                if (++tokens > MaximumTokens) return false;
                if (rootTypePending)
                {
                    rootTypePending = false;
                    acceptedType = reader.TokenType == JsonTokenType.String
                        && (reader.ValueTextEquals("gameState") || reader.ValueTextEquals("gameStateDelta")
                            || reader.ValueTextEquals("gameStateCardFrameV1"));
                    if (!acceptedType) return false;
                }
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                if (reader.CurrentDepth == 1 && reader.ValueTextEquals("type"))
                {
                    if (++rootTypeCount > 1) return false;
                    rootTypePending = true;
                }
                var key = reader.GetString()!;
                byte[]? replacement = EncodedNames.GetValueOrDefault(key);
                var escapeReserved = replacement is null && (key.StartsWith('@') || key.StartsWith('~'));
                if (replacement is null && !escapeReserved) continue;
                var from = checked((int)reader.TokenStartIndex);
                // Property BytesConsumed includes the colon. ValueSpan is the original
                // escaped UTF-8 lexeme between quotes, so do not remove the delimiter.
                var end = checked(from + reader.ValueSpan.Length + 2);
                output.Write(original.AsSpan(copied, from - copied));
                if (escapeReserved)
                {
                    // Insert one literal byte, retaining original Unicode/HTML
                    // escaping. Reserializing the whole key could expand it 6x.
                    output.Write("\"~"u8);
                    output.Write(original.AsSpan(from + 1, end - from - 1));
                }
                else output.Write(replacement!);
                copied = end;
                if (output.WrittenCount > MaximumEncodedWireBytes) return false;
            }
            if (!acceptedType || rootTypeCount != 1 || reader.BytesConsumed != original.Length) return false;
            output.Write(original.AsSpan(copied)); output.Write("}"u8);
            // Neither a percentage nor an absolute benefit cutoff is safe: public
            // and private subtree sizes could jointly cross it. Once negotiated,
            // every structurally valid, bounded game packet uses this fixed encoding.
            // Small packets may grow slightly; measure the complete workload net gain.
            if (output.WrittenCount > MaximumEncodedWireBytes) return false;
            encoded = output.WrittenSpan.ToArray();
            return true;
        }
        catch (JsonException) { return false; }
    }
}
