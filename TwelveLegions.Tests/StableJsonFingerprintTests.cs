using System.Text.Json;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StableJsonFingerprintTests
{
    [Fact]
    public void DictionaryInsertionAndObjectPropertyOrderDoNotChangeFingerprint()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var first = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["z"] = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 },
            ["a"] = new[] { "first", "second" },
        };
        var second = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["a"] = new[] { "first", "second" },
            ["z"] = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
        };

        Assert.Equal(StableJsonFingerprint.Compute(first, options),
            StableJsonFingerprint.Compute(second, options));
    }
}
