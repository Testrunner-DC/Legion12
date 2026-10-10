using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private sealed record DeckLibraryEntry(L12PublicDeckSummaryView View, IReadOnlySet<string> CardIds);

    private static string LibraryCatalogVersion(L12Catalog catalog)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            CardsAndMorale = DeckSummaryCatalogVersion(catalog),
            Products = catalog.CardProducts.OrderBy(item => item.Key, StringComparer.Ordinal),
            Pools = catalog.CardPools.OrderBy(item => item.Key, StringComparer.Ordinal),
            Presets = catalog.PresetDecks.Select(deck => new
            {
                deck.Name, deck.MasterId,
                Main = L12DeckValidator.CountCards(deck.CardIds), Morale = L12DeckValidator.CountCards(deck.MoraleIds),
                Special = L12DeckValidator.CountCards(deck.SpecialIds), Bench = L12DeckValidator.CountCards(deck.BenchIds),
                deck.AlternateArtCopies,
            }).OrderBy(deck => deck.MasterId, StringComparer.Ordinal).ThenBy(deck => deck.Name, StringComparer.Ordinal),
        })));

    private static string OfficialLibraryId(L12PresetDeckDefinition deck)
        => "official:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deck.MasterId + "\0" + deck.Name))).ToLowerInvariant();

    private static DeckLibraryEntry LibraryEntry(L12Catalog catalog, IReadOnlyList<L12CardRestrictionConfig> restrictions,
        string id, string source, string name, string masterId, string author, string? publicCode, int? publicationVersion,
        DateTimeOffset? createdAt, DateTimeOffset? updatedAt, IReadOnlyList<L12DeckCardQuantity> main,
        IReadOnlyList<L12DeckCardQuantity> morale, IReadOnlyList<L12DeckCardQuantity> special,
        IReadOnlyList<L12DeckCardQuantity> bench, IReadOnlyDictionary<string, IReadOnlyList<string>?>? art,
        int views, int likes, int copies, bool liked, bool canEdit)
    {
        var legal = L12DeckValidator.TryValidateCounts(catalog, new(name, masterId, main, morale, special, bench, art),
            out var reason, restrictions);
        var uncounted = L12DeckValidator.CountCards(main.Where(item => catalog.Cards.TryGetValue(item.CardId, out var card)
            && L12SpecialDeckRules.DoesNotCountTowardMainDeck(card)));
        var counts = new L12DeckSummaryCounts(L12DeckValidator.CountCards(main) - uncounted, uncounted,
            L12DeckValidator.CountCards(morale), L12DeckValidator.CountCards(special), L12DeckValidator.CountCards(bench));
        var cards = main.Concat(morale).Concat(special).Select(item => item.CardId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var environment = L12DeckEnvironment.ForCardIds(catalog, cards.Concat(bench.Select(item => item.CardId)).Append(masterId));
        catalog.Cards.TryGetValue(masterId, out var master);
        return new(new(id, source, name, masterId, master?.NameZh ?? masterId, master?.Faction ?? "unknown", author,
            publicCode, publicationVersion, createdAt, updatedAt, counts, legal, legal ? null : reason, environment,
            views, likes, copies, liked, canEdit), cards);
    }

    private static DeckLibraryEntry PublishedLibraryEntry(DataFile data, PublishedDeckRow row, L12Catalog catalog,
        L12OperationsPolicySnapshot policy, string? viewerId)
    {
        var payload = ReadReferencedDeckPayload(data, row.PayloadHash);
        var owner = data.Accounts.FirstOrDefault(account => account.Id == row.OwnerId);
        var isOwner = viewerId is not null && row.OwnerId == viewerId;
        return LibraryEntry(catalog, policy.CardRestrictions, row.Id, "public", row.Name, row.MasterId,
            owner is null ? "已注销玩家" : PublicUsername(owner), row.PublicCode, isOwner ? row.Version : null, row.CreatedAt, row.UpdatedAt,
            SummaryCardCounts(payload.MainJson), SummaryCardCounts(payload.MoraleJson), SummaryCardCounts(payload.SpecialJson),
            [], null, row.Views, row.LikedByAccountIds.Count, row.Copies,
            viewerId is not null && row.LikedByAccountIds.Contains(viewerId), isOwner);
    }

    private static DeckLibraryEntry OfficialLibraryEntry(L12PresetDeckDefinition deck, L12Catalog catalog, L12OperationsPolicySnapshot policy)
        => LibraryEntry(catalog, policy.CardRestrictions, OfficialLibraryId(deck), "official", deck.Name, deck.MasterId,
            "十二军团官方预组", null, null, null, null, L12DeckValidator.CountCards(deck.CardIds),
            L12DeckValidator.CountCards(deck.MoraleIds), L12DeckValidator.CountCards(deck.SpecialIds),
            L12DeckValidator.CountCards(deck.BenchIds), deck.AlternateArtCopies.ToDictionary(item => item.Key,
                item => (IReadOnlyList<string>?)item.Value), 0, 0, 0, false, false);
}
