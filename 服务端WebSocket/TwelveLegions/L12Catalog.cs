using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12OfficialAlternateArtDefinition(
    string Id,
    string ArtCode,
    string BaseCardId,
    string DisplayName,
    string CardImageId,
    string ProductName);

public sealed class L12Catalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyDictionary<string, L12CardDefinition> Cards { get; }
    public IReadOnlyList<L12PresetDeckDefinition> PresetDecks { get; }
    public L12MoraleIdentityCatalog MoraleIdentities { get; }
    public L12AtomicEffectCatalog AtomicEffects { get; }
    public IReadOnlyList<L12OfficialAlternateArtDefinition> OfficialAlternateArts { get; }

    private L12Catalog(
        IReadOnlyDictionary<string, L12CardDefinition> cards,
        IReadOnlyList<L12PresetDeckDefinition> presetDecks,
        L12MoraleIdentityCatalog moraleIdentities,
        IReadOnlyList<L12OfficialAlternateArtDefinition> officialAlternateArts)
    {
        Cards = cards;
        PresetDecks = presetDecks;
        MoraleIdentities = moraleIdentities;
        AtomicEffects = L12AtomicEffectCatalog.Build(cards.Values);
        OfficialAlternateArts = officialAlternateArts;
    }

    public static L12Catalog Load(string dataPath)
    {
        var cardFiles = Directory.GetFiles(dataPath, "cards.s*.json").OrderBy(path => path).ToArray();
        var deckFiles = Directory.GetFiles(dataPath, "preset-decks.s*.json").OrderBy(path => path).ToArray();
        if (cardFiles.Length == 0 || deckFiles.Length == 0)
        {
            throw new FileNotFoundException($"十二军团数据缺失：{dataPath}");
        }

        var cards = cardFiles.SelectMany(path => JsonSerializer.Deserialize<List<L12CardDefinition>>(
            File.ReadAllText(path), JsonOptions) ?? []).ToList();
        var decks = deckFiles.SelectMany(path => JsonSerializer.Deserialize<List<L12PresetDeckDefinition>>(
            File.ReadAllText(path), JsonOptions) ?? []).ToList();

        if (cards.Count < 133)
        {
            throw new InvalidDataException($"卡牌数据不完整，至少应包含 133 张 S1 卡牌，实际 {cards.Count} 张。");
        }
        var duplicateIds = cards.GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateIds.Length > 0)
            throw new InvalidDataException($"存在重复卡号：{string.Join(", ", duplicateIds)}");
        var invalidMasters = cards.Where(card => card.CardType == "master"
                && (card.Hp is null || card.Cost is not null || card.Troops is not null))
            .Select(card => card.Id).ToArray();
        if (invalidMasters.Length > 0)
            throw new InvalidDataException($"主宰必须登记血量且不得登记费用或兵力：{string.Join(", ", invalidMasters)}");
        var byId = cards.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);
        var moraleIdentities = L12MoraleIdentityCatalog.Load(
            Path.Combine(dataPath, "morale-identities.json"), byId);
        if (decks.Count < 2) throw new InvalidDataException("至少需要两套官方预组。");
        foreach (var deck in decks)
        {
            var normalizedMorale = deck.MoraleIds.Select(moraleIdentities.CanonicalDeckCardId).ToArray();
            deck.MoraleIds.Clear();
            deck.MoraleIds.AddRange(normalizedMorale);
            var ids = deck.CardIds.Append(deck.MasterId).Concat(deck.MoraleIds).Concat(deck.SpecialIds);
            var missing = ids.Where(id => !byId.ContainsKey(id)).Distinct().ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidDataException($"预组 {deck.Name} 引用了不存在的卡：{string.Join(", ", missing)}");
            }
            var master = byId[deck.MasterId];
            var countedMain = deck.CardIds.Count(id => !L12SpecialDeckRules.DoesNotCountTowardMainDeck(byId[id]));
            var requiredMorale = master.Faction == "taiyangcheng" ? 6 : 8;
            if (countedMain is < 40 or > 50 || deck.MoraleIds.Count != requiredMorale)
                throw new InvalidDataException($"预组 {deck.Name} 应为 40–50 张计入构筑的主牌 + {requiredMorale} 张士气。");
            if (deck.SpecialIds.Any(id => byId[id].CardType != "trial" || byId[id].Faction != master.Faction))
                throw new InvalidDataException($"预组 {deck.Name} 包含无效的特殊区卡牌。");
        }

        var officialAlternateArts = LoadOfficialAlternateArts(dataPath, byId);
        return new L12Catalog(byId, decks, moraleIdentities, officialAlternateArts);
    }

    private static IReadOnlyList<L12OfficialAlternateArtDefinition> LoadOfficialAlternateArts(
        string dataPath, IReadOnlyDictionary<string, L12CardDefinition> cards)
    {
        var path = Path.Combine(dataPath, "card-archive-assets.json");
        if (!File.Exists(path)) throw new FileNotFoundException("画廊异画登记缺失", path);
        var archive = JsonSerializer.Deserialize<ArchiveAlternateArtCatalog>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("画廊异画登记格式无效");
        var definitions = archive.Cards
            .Where(row => !row.Id.Equals("S02-05C1B", StringComparison.OrdinalIgnoreCase))
            .Select(row => OfficialAlternateArt(row.Id, row.BaseCardId, row.Products.FirstOrDefault()
                ?? row.Product, cards)).ToList();
        definitions.Add(OfficialAlternateArt("S02-05C1A", "S02-05C1", "第2季|典藏版", cards));
        var duplicateCodes = definitions.GroupBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateCodes.Length > 0)
            throw new InvalidDataException($"画廊异画编号重复：{string.Join(", ", duplicateCodes)}");
        return definitions.OrderBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static L12OfficialAlternateArtDefinition OfficialAlternateArt(string cardImageId,
        string baseCardId, string productName, IReadOnlyDictionary<string, L12CardDefinition> cards)
    {
        if (!cards.TryGetValue(baseCardId, out var baseCard))
            throw new InvalidDataException($"画廊异画 {cardImageId} 引用了不存在的原卡 {baseCardId}");
        return new L12OfficialAlternateArtDefinition($"catalog-alt:{cardImageId}", cardImageId,
            baseCardId, baseCard.NameZh, cardImageId, productName);
    }

    private sealed class ArchiveAlternateArtCatalog
    {
        public List<ArchiveAlternateArtRow> Cards { get; init; } = [];
    }

    private sealed class ArchiveAlternateArtRow
    {
        public string Id { get; init; } = string.Empty;
        public string BaseCardId { get; init; } = string.Empty;
        public string Product { get; init; } = string.Empty;
        public List<string> Products { get; init; } = [];
    }

    public L12PresetDeckDefinition DeckAt(int index)
        => PresetDecks[index % PresetDecks.Count];
}
