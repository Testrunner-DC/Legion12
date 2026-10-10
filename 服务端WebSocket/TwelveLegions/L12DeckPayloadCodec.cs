using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed record L12DecodedDeckPayload(
    string MainJson,
    string MoraleJson,
    string SpecialJson);

// Internal persistence format only. Public/import deck codes keep their existing format.
internal static class L12DeckPayloadCodec
{
    internal const int CurrentFormat = 1;

    private sealed record LegacyCardCount(string CardId, int Quantity);

    internal static string EncodeLegacyJson(string mainJson, string moraleJson, string specialJson)
    {
        var regions = new[]
        {
            ReadLegacyRegion(mainJson),
            ReadLegacyRegion(moraleJson),
            ReadLegacyRegion(specialJson),
        };
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var region in regions)
            {
                writer.WriteStartArray();
                foreach (var card in region)
                {
                    writer.WriteStartArray();
                    writer.WriteStringValue(card.CardId);
                    writer.WriteNumberValue(card.Quantity);
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    internal static L12DecodedDeckPayload Decode(int format, string payloadJson)
    {
        if (format != CurrentFormat)
            throw new InvalidDataException($"不支持的牌库构筑正文格式：{format}");
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new InvalidDataException("牌库构筑正文为空");

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3)
                throw new InvalidDataException("牌库构筑正文区域数量无效");
            var regions = root.EnumerateArray().Select(ReadCompactRegion).ToArray();
            var decoded = new L12DecodedDeckPayload(
                JsonSerializer.Serialize(regions[0]),
                JsonSerializer.Serialize(regions[1]),
                JsonSerializer.Serialize(regions[2]));
            if (!string.Equals(payloadJson,
                    EncodeLegacyJson(decoded.MainJson, decoded.MoraleJson, decoded.SpecialJson),
                    StringComparison.Ordinal))
                throw new InvalidDataException("牌库构筑正文不是规范紧凑格式");
            return decoded;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException or OverflowException)
        {
            throw new InvalidDataException("牌库构筑正文无效", error);
        }
    }

    internal static string ComputeCanonicalHash(string masterId, L12DecodedDeckPayload payload)
    {
        var normalizedMaster = (masterId ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.Equals(masterId, normalizedMaster, StringComparison.Ordinal))
            throw new InvalidDataException("牌库主将标识不是规范格式");
        var main = ReadLegacyRegion(payload.MainJson);
        var morale = ReadLegacyRegion(payload.MoraleJson);
        var special = ReadLegacyRegion(payload.SpecialJson);
        var canonical = JsonSerializer.Serialize(new
        {
            schema = 1,
            master = normalizedMaster,
            main,
            morale,
            special,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static void ValidateHash(string payloadHash, string masterId, L12DecodedDeckPayload payload)
    {
        if (!string.Equals(payloadHash, ComputeCanonicalHash(masterId, payload), StringComparison.Ordinal))
            throw new InvalidDataException("牌库构筑正文哈希不匹配");
    }

    private static List<LegacyCardCount> ReadLegacyRegion(string json)
    {
        List<LegacyCardCount> cards;
        try
        {
            cards = JsonSerializer.Deserialize<List<LegacyCardCount>>(json)
                ?? throw new InvalidDataException("牌库构筑计数正文为空");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("牌库构筑计数正文无效", error);
        }
        ValidateRegion(cards);
        if (!string.Equals(JsonSerializer.Serialize(cards), json, StringComparison.Ordinal))
            throw new InvalidDataException("牌库构筑计数正文不是规范格式");
        return cards;
    }

    private static List<LegacyCardCount> ReadCompactRegion(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("牌库构筑区域不是数组");
        var cards = new List<LegacyCardCount>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2)
                throw new InvalidDataException("牌库构筑计数项无效");
            var values = item.EnumerateArray().ToArray();
            if (values[0].ValueKind != JsonValueKind.String
                || values[1].ValueKind != JsonValueKind.Number
                || !values[1].TryGetInt32(out var quantity))
                throw new InvalidDataException("牌库构筑计数项类型无效");
            cards.Add(new(values[0].GetString() ?? string.Empty, quantity));
        }
        ValidateRegion(cards);
        return cards;
    }

    private static void ValidateRegion(IReadOnlyList<LegacyCardCount> cards)
    {
        string? previous = null;
        foreach (var card in cards)
        {
            if (card is null
                || string.IsNullOrWhiteSpace(card.CardId)
                || !string.Equals(card.CardId, card.CardId.Trim().ToUpperInvariant(), StringComparison.Ordinal)
                || card.Quantity < 1
                || previous is not null && string.CompareOrdinal(previous, card.CardId) >= 0)
                throw new InvalidDataException("牌库构筑计数项未规范化");
            previous = card.CardId;
        }
    }
}
