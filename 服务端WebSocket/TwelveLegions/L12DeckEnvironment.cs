using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

public sealed record L12DeckEnvironmentView(string Status, string? Value = null, string? Reason = null);
public sealed record L12DeckEnvironmentEvidence(string? CardPool, IReadOnlyList<string>? Products);

public static partial class L12DeckEnvironment
{
    private static readonly HashSet<string> CurrentStarterProducts =
    [
        "ST01|天廷阵营预组", "ST02|太阳城阵营预组", "ST03|阿斯加德阵营预组",
        "ST04|高天原阵营预组", "ST05|奥林匹斯阵营预组", "ST06|彼界阵营预组",
    ];
    [GeneratedRegex(@"^ST\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StarterProductPattern();

    public static L12DeckEnvironmentView FromEvidence(IEnumerable<L12DeckEnvironmentEvidence> source)
    {
        var evidence = source.ToArray();
        if (evidence.Length == 0) return new("unconfigured", Reason: "empty_deck");
        if (evidence.Any(item => string.IsNullOrEmpty(item.CardPool))) return new("unconfigured", Reason: "missing_card_pool");
        if (evidence.Any(item => item.Products is null || item.Products.Count == 0
                || item.Products.Any(string.IsNullOrWhiteSpace))) return new("unconfigured", Reason: "missing_products");
        // STS1 is the existing released starter pool. Unknown future pools
        // remain unconfigured even when a known starter product is also listed.
        if (evidence.Any(item => item.CardPool is not ("S01" or "S02" or "STS1"))) return new("unconfigured", Reason: "unknown_card_pool");
        var starters = evidence.SelectMany(item => item.Products!).Select(product => product.Trim())
            .Where(product => StarterProductPattern().IsMatch(product) && !product.EndsWith("（勘误收录）", StringComparison.Ordinal)).ToArray();
        if (starters.Any(product => !CurrentStarterProducts.Contains(product))) return new("unconfigured", Reason: "unknown_starter_product");
        if (starters.Any(CurrentStarterProducts.Contains)) return new("configured", "2.5");
        if (evidence.Any(item => item.CardPool == "STS1")) return new("unconfigured", Reason: "missing_starter_product");
        return new("configured", evidence.Any(item => item.CardPool == "S02") ? "2.0" : "1.0");
    }

    public static L12DeckEnvironmentView ForCardIds(L12Catalog catalog, IEnumerable<string> ids)
        => FromEvidence(ids.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => new L12DeckEnvironmentEvidence(
            catalog.CardPools.GetValueOrDefault(id), catalog.CardProducts.GetValueOrDefault(id))));
}
