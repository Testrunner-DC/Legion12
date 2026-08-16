using System.Text.Json;

namespace TwelveLegions.Server;

public sealed class L12Catalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyDictionary<string, L12CardDefinition> Cards { get; }
    public IReadOnlyList<L12PresetDeckDefinition> PresetDecks { get; }

    private L12Catalog(
        IReadOnlyDictionary<string, L12CardDefinition> cards,
        IReadOnlyList<L12PresetDeckDefinition> presetDecks)
    {
        Cards = cards;
        PresetDecks = presetDecks;
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
        if (decks.Count < 2 || decks.Any(deck => deck.CardIds.Count != 40 || deck.MoraleIds.Count != 8))
        {
            throw new InvalidDataException("首测预组必须至少两套，且每套为 40 张主牌 + 8 张士气。");
        }

        var duplicateIds = cards.GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateIds.Length > 0)
            throw new InvalidDataException($"存在重复卡号：{string.Join(", ", duplicateIds)}");
        var byId = cards.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var deck in decks)
        {
            var ids = deck.CardIds.Append(deck.MasterId).Concat(deck.MoraleIds);
            var missing = ids.Where(id => !byId.ContainsKey(id)).Distinct().ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidDataException($"预组 {deck.Name} 引用了不存在的卡：{string.Join(", ", missing)}");
            }
        }

        return new L12Catalog(byId, decks);
    }

    public L12PresetDeckDefinition DeckAt(int index)
        => PresetDecks[index % PresetDecks.Count];
}
