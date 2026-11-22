using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// G1 guard: a deck view must never put an explicit null on the wire for the optional
// sections. Server views spell "not persisted" as null while the client deck cache
// format only knows absent-or-present, so a null failed the cache codec on copy.
public sealed class DeckViewWireShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void UnpersistedSectionsAreOmittedInsteadOfSerializedAsNull()
    {
        var view = new L12AccountDeckView("示例牌库", "S01-01M1", ["S01-0001"], ["S01-01C1"], [],
            DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(view, Web);

        Assert.DoesNotContain("benchIds", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alternateArtSelections", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alternateArtCopies", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersistedSectionsKeepTheirValues()
    {
        var view = new L12AccountDeckView("示例牌库", "S01-01M1", ["S01-0001"], ["S01-01C1"], [],
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string> { ["S01-0001"] = "art-1" },
            new Dictionary<string, IReadOnlyList<string>> { ["S01-0001"] = ["art-1", "art-2"] },
            ["S01-0002"]);

        var json = JsonSerializer.Serialize(view, Web);

        Assert.Contains("\"benchIds\":[\"S01-0002\"]", json, StringComparison.Ordinal);
        Assert.Contains("alternateArtSelections", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alternateArtCopies", json, StringComparison.OrdinalIgnoreCase);
    }
}