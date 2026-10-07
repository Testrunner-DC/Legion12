using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class DeckEnvironmentParityTests
{
    [Theory]
    [InlineData("S01", "第1季|天御", "configured", "1.0")]
    [InlineData("S02", "第2季|伟大试炼", "configured", "2.0")]
    [InlineData("S01", "ST01|天廷阵营预组", "configured", "2.5")]
    [InlineData("S01", "ST02|太阳城阵营预组", "configured", "2.5")]
    [InlineData("S02", "ST06|彼界阵营预组", "configured", "2.5")]
    [InlineData("STS1", "ST05|奥林匹斯阵营预组", "configured", "2.5")]
    [InlineData("S01", "ST07|未来预组（勘误收录）", "configured", "1.0")]
    [InlineData("S01", "ST01|天廷阵营预组（勘误收录）", "configured", "1.0")]
    [InlineData("S01", "ST07|未来预组", "unconfigured", null)]
    [InlineData("S03", "第3季|未来", "unconfigured", null)]
    // A conservative new contract boundary: the old frontend checked ST first.
    [InlineData("S03", "ST05|奥林匹斯阵营预组", "unconfigured", null)]
    [InlineData("STS1", "第1季|天御", "unconfigured", null)]
    [InlineData(null, "ST01|天廷阵营预组", "unconfigured", null)]
    public void ExistingReleasedPoolAndStarterRulesArePreserved(string? pool, string product, string status, string? value)
    {
        var result = L12DeckEnvironment.FromEvidence([new(pool, [product])]);
        Assert.Equal(status, result.Status);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public void MixedPoolsUnknownEvidenceAndUniqueIdsUseSourceMetadata()
    {
        Assert.Equal("2.0", L12DeckEnvironment.FromEvidence([new("S01", ["第1季|天御"]), new("S02", ["第2季|伟大试炼"])]).Value);
        Assert.Equal("empty_deck", L12DeckEnvironment.FromEvidence([]).Reason);
        Assert.Equal("missing_products", L12DeckEnvironment.FromEvidence([new("S01", [])]).Reason);
        Assert.Equal("missing_products", L12DeckEnvironment.FromEvidence([new("S01", null)]).Reason);
        Assert.Equal("unknown_starter_product", L12DeckEnvironment.FromEvidence([
            new("S01", ["ST01|天廷阵营预组", "ST07|未来预组"])]).Reason);
        var dataPath = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data");
        var catalog = L12Catalog.Load(dataPath);
        using var productJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataPath, "card-product-inclusions.json")));
        foreach (var row in productJson.RootElement.GetProperty("cards").EnumerateArray())
            Assert.Equal(row.GetProperty("cardPool").GetString(), catalog.CardPools[row.GetProperty("cardId").GetString()!]);
        var source = catalog.PresetDecks[0];
        Assert.Equal(L12DeckEnvironment.ForCardIds(catalog, [source.MasterId]),
            L12DeckEnvironment.ForCardIds(catalog, Enumerable.Repeat(source.MasterId, 500)));
        Assert.Equal("missing_card_pool", L12DeckEnvironment.ForCardIds(catalog, ["UNKNOWN"]).Reason);
    }

    [Fact]
    public void AllActualProductRowsAndPresetsMatchExistingFrontendEnvironmentRules()
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var released = new HashSet<string>(["ST01|天廷阵营预组", "ST02|太阳城阵营预组", "ST03|阿斯加德阵营预组",
            "ST04|高天原阵营预组", "ST05|奥林匹斯阵营预组", "ST06|彼界阵营预组"]);
        // Frozen source reference: site/deckEnvironment.ts checks known ST
        // before pool membership. Only actual catalog evidence is compared here.
        string? PriorFrontend(IEnumerable<string> ids)
        {
            var unique = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (unique.Length == 0 || unique.Any(id => !catalog.CardPools.TryGetValue(id, out var pool) || string.IsNullOrEmpty(pool)
                || !catalog.CardProducts.TryGetValue(id, out var products) || products.Count == 0)) return null;
            var starters = unique.SelectMany(id => catalog.CardProducts[id]).Select(product => product.Trim())
                .Where(product => System.Text.RegularExpressions.Regex.IsMatch(product, @"^ST\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    && !product.EndsWith("（勘误收录）", StringComparison.Ordinal)).ToArray();
            if (starters.Any(product => !released.Contains(product))) return null;
            if (starters.Any(released.Contains)) return "2.5";
            var pools = unique.Select(id => catalog.CardPools[id]).ToHashSet();
            if (pools.Any(pool => pool is not ("S01" or "S02"))) return null;
            return pools.Contains("S02") ? "2.0" : pools.Contains("S01") ? "1.0" : null;
        }
        foreach (var id in catalog.CardProducts.Keys)
            Assert.Equal(PriorFrontend([id]), L12DeckEnvironment.ForCardIds(catalog, [id]).Value);
        foreach (var deck in catalog.PresetDecks)
        {
            var ids = deck.CardIds.Concat(deck.MoraleIds).Concat(deck.SpecialIds).Concat(deck.BenchIds).Append(deck.MasterId);
            Assert.Equal(PriorFrontend(ids), L12DeckEnvironment.ForCardIds(catalog, ids).Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrNonStringPoolDoesNotChangeExistingProductLoaderSemantics(bool nonString)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-cq2-pool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = false;
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data");
            foreach (var file in Directory.EnumerateFiles(source, "*.json")) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            var before = L12Catalog.Load(root);
            var path = Path.Combine(root, "card-product-inclusions.json");
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            var row = json["cards"]![0]!.AsObject();
            var id = row["cardId"]!.GetValue<string>();
            if (nonString) row["cardPool"] = 123;
            else row.Remove("cardPool");
            File.WriteAllText(path, json.ToJsonString());
            var after = L12Catalog.Load(root);
            Assert.Equal(before.CardProducts[id], after.CardProducts[id]);
            Assert.Null(after.CardPools[id]);
            Assert.Equal("unconfigured", L12DeckEnvironment.ForCardIds(after, [id]).Status);
            passed = true;
        }
        finally
        {
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ2 pool fixture retained: " + root);
        }
    }
}
