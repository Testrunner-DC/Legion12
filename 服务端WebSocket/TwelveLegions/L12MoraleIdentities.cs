using System.Text.Json;

namespace TwelveLegions.Server;

public sealed class L12MoraleIdentityDefinition
{
    public required string Faction { get; init; }
    public required string DisplayName { get; init; }
    public required string CanonicalCardId { get; init; }
    public required List<string> VersionCardIds { get; init; }
    public string? GodPowerCardId { get; init; }
    public string? GodPowerDisplayName { get; init; }
    public string? GodPowerDisplayNumber { get; init; }
    public string? GodPowerEffectText { get; init; }
}

public sealed class L12MoraleIdentityCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IReadOnlyDictionary<string, L12MoraleIdentityDefinition> _byFaction;
    private readonly IReadOnlyDictionary<string, L12MoraleIdentityDefinition> _byVersionCardId;
    private readonly IReadOnlyDictionary<string, L12MoraleIdentityDefinition> _byGodPowerCardId;

    public IReadOnlyList<L12MoraleIdentityDefinition> All { get; }

    private L12MoraleIdentityCatalog(IReadOnlyList<L12MoraleIdentityDefinition> definitions)
    {
        All = definitions;
        _byFaction = definitions.ToDictionary(item => item.Faction, StringComparer.OrdinalIgnoreCase);
        _byVersionCardId = definitions.SelectMany(item => item.VersionCardIds.Select(cardId => (cardId, item)))
            .ToDictionary(pair => pair.cardId, pair => pair.item, StringComparer.OrdinalIgnoreCase);
        _byGodPowerCardId = definitions.Where(item => !string.IsNullOrWhiteSpace(item.GodPowerCardId))
            .ToDictionary(item => item.GodPowerCardId!, StringComparer.OrdinalIgnoreCase);
    }

    public static L12MoraleIdentityCatalog Load(string path,
        IReadOnlyDictionary<string, L12CardDefinition> cards)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"士气身份映射缺失：{path}");
        var definitions = JsonSerializer.Deserialize<List<L12MoraleIdentityDefinition>>(
            File.ReadAllText(path), JsonOptions) ?? [];
        if (definitions.Count != 6)
            throw new InvalidDataException($"士气身份映射应覆盖6个阵营，实际 {definitions.Count} 个。");
        if (definitions.Select(item => item.Faction).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count)
            throw new InvalidDataException("士气身份映射包含重复阵营。");
        var duplicateVersion = definitions.SelectMany(item => item.VersionCardIds)
            .GroupBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateVersion is not null)
            throw new InvalidDataException($"士气版本卡号重复归属：{duplicateVersion.Key}");
        foreach (var identity in definitions)
        {
            if (!identity.DisplayName.Equals($"士气·{FactionName(identity.Faction)}", StringComparison.Ordinal))
                throw new InvalidDataException($"{identity.Faction} 的士气名称未遵守“士气·阵营”规范。");
            if (!identity.VersionCardIds.Contains(identity.CanonicalCardId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"{identity.Faction} 的基准士气不在版本列表中。");
            if (!cards.TryGetValue(identity.CanonicalCardId, out var canonical)
                || canonical.CardType != "rune" || canonical.Faction != identity.Faction)
                throw new InvalidDataException($"{identity.Faction} 的基准士气无效：{identity.CanonicalCardId}");
            if (identity.CanonicalCardId.StartsWith("ST", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{identity.Faction} 不得以ST后出版本作为运行时基准士气。");
            if (identity.GodPowerCardId is { Length: > 0 } powerId
                && (!cards.TryGetValue(powerId, out var power) || power.CardType != "rune" || power.Faction != identity.Faction))
                throw new InvalidDataException($"{identity.Faction} 的神力反面无效：{powerId}");
            if (identity.GodPowerCardId is { Length: > 0 }
                && (string.IsNullOrWhiteSpace(identity.GodPowerDisplayName)
                    || string.IsNullOrWhiteSpace(identity.GodPowerDisplayNumber)
                    || string.IsNullOrWhiteSpace(identity.GodPowerEffectText)))
                throw new InvalidDataException($"{identity.Faction} 的神力展示元数据不完整。");
        }
        return new L12MoraleIdentityCatalog(definitions);
    }

    public L12MoraleIdentityDefinition ForFaction(string faction)
        => _byFaction.TryGetValue(faction, out var identity)
            ? identity
            : throw new KeyNotFoundException($"阵营没有基准士气：{faction}");

    public bool TryForFaction(string faction, out L12MoraleIdentityDefinition identity)
        => _byFaction.TryGetValue(faction, out identity!);

    public string CanonicalDeckCardId(string cardId)
        => _byVersionCardId.TryGetValue(cardId, out var identity)
            || _byGodPowerCardId.TryGetValue(cardId, out identity)
                ? identity.CanonicalCardId
                : cardId;

    public string CanonicalEffectCardId(string cardId)
        => _byVersionCardId.TryGetValue(cardId, out var identity) ? identity.CanonicalCardId : cardId;

    public bool IsVersionForFaction(string cardId, string faction)
        => (_byVersionCardId.TryGetValue(cardId, out var identity)
                || _byGodPowerCardId.TryGetValue(cardId, out identity))
            && identity.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase);

    private static string FactionName(string faction) => faction switch
    {
        "tianting" => "天廷",
        "taiyangcheng" => "太阳城",
        "asgard" => "阿斯加德",
        "gaotianyuan" => "高天原",
        "olympus" => "奥林匹斯",
        "otherworld" => "彼界",
        _ => faction,
    };
}
