using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private sealed record DeckSummaryCatalogStamp(string Value);
    private static readonly ConditionalWeakTable<L12Catalog, DeckSummaryCatalogStamp> DeckSummaryCatalogStamps = new();

    private static string DeckSummaryCatalogVersion(L12Catalog catalog)
        => DeckSummaryCatalogStamps.GetValue(catalog, item => new(Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                Cards = item.Cards.OrderBy(card => card.Key, StringComparer.Ordinal).Select(card => card.Value),
                Morale = item.MoraleIdentities.All.OrderBy(morale => morale.Faction, StringComparer.Ordinal),
            }))))).Value;

    private static IReadOnlyList<L12DeckCardQuantity> SummaryCardCounts(string json)
        => ReadRecoveryCardCounts(json).Select(card => new L12DeckCardQuantity(card.CardId, card.Quantity)).ToArray();

    private static L12PrivateDeckSummaryView PrivateDeckSummary(DataFile data, DeckRow row, L12Catalog catalog,
        IReadOnlyList<L12CardRestrictionConfig> restrictions)
    {
        var payload = ReadReferencedDeckPayload(data, row.PayloadHash);
        var main = SummaryCardCounts(payload.MainJson);
        var morale = SummaryCardCounts(payload.MoraleJson);
        var special = SummaryCardCounts(payload.SpecialJson);
        var bench = SummaryCardCounts(DeckBenchJson(row));
        var legal = L12DeckValidator.TryValidateCounts(catalog, new(row.Name, row.MasterId, main, morale, special, bench,
            row.AlternateArtCopies.ToDictionary(item => item.Key, item => (IReadOnlyList<string>?)item.Value,
                StringComparer.OrdinalIgnoreCase)), out var reason, restrictions);
        var uncounted = L12DeckValidator.CountCards(main.Where(item => catalog.Cards.TryGetValue(item.CardId, out var card)
            && L12SpecialDeckRules.DoesNotCountTowardMainDeck(card)));
        return new(row.Id, row.Revision, row.Name, row.MasterId, row.UpdatedAt, row.PublicationId, row.PublicationVersion,
            new(L12DeckValidator.CountCards(main) - uncounted, uncounted, L12DeckValidator.CountCards(morale),
                L12DeckValidator.CountCards(special), L12DeckValidator.CountCards(bench)), legal, legal ? null : reason);
    }
}
