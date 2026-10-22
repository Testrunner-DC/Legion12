using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace TwelveLegions.Server;

/// <summary>Only recipient-projected snapshot messages; never persistence, HTTP, auth or chat.</summary>
internal static class L12SnapshotJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        // Literal Unicode avoids expanding each CJK character into six ASCII bytes.
        // Keep the safe encoder: HTML-sensitive and line-separator escapes remain.
        // This is JSON encoding, not an adaptive compression dictionary.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    internal static byte[] SerializeToUtf8Bytes<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    internal static JsonWriterOptions WriterOptions => new() { Encoder = Options.Encoder };
}
