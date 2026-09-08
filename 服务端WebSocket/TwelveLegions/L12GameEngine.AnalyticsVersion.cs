using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

// A version is shared across matches. Only its short identity is persisted per match.
// Including the engine build conservatively separates code-only effect fixes as well as catalog edits.
internal static class L12AnalyticsEffectVersion
{
    private sealed record Identity(string Value);
    private static readonly ConditionalWeakTable<L12Catalog, Identity> Versions = new();

    internal static string ForCatalog(L12Catalog catalog) => Versions.GetValue(catalog,
        value => new Identity(Compute(value.Cards.Values, L12RuntimeBuildVersion.Capture().EngineVersion))).Value;

    internal static string Compute(IEnumerable<L12CardDefinition> cards, string engineVersion)
    {
        var canonical = JsonSerializer.Serialize(cards.OrderBy(card => card.Id, StringComparer.Ordinal)
            .Select(card => new
            {
                card.Id, card.Number, card.NameZh, card.CardType, card.Faction,
                card.Cost, card.Hp, card.Troops, card.DisasterLevel, card.TrialValue,
                card.DeckLimit, Traits = card.Traits.OrderBy(trait => trait, StringComparer.Ordinal),
                card.Profession, card.Effect, card.AtomicReference,
            }));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return $"{engineVersion}/{fingerprint[..24]}";
    }
}

public sealed partial class L12GameEngine
{
    internal string AnalyticsEffectVersion => L12AnalyticsEffectVersion.ForCatalog(_catalog);
}
