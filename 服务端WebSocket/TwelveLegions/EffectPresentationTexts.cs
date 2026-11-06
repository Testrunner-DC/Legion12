using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

public sealed record L12FrozenEffectPresentation(
    string SceneId,
    string CardId,
    string SceneKey,
    string Text,
    IReadOnlyList<string> AllowedPlaceholders);

public static class L12EffectPresentationText
{
    private static readonly Regex PlaceholderPattern = new(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}",
        RegexOptions.CultureInvariant);
    private static readonly Regex BracePattern = new(@"[{}]", RegexOptions.CultureInvariant);

    public static string Normalize(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Trim();

    public static string Validate(string? value, IReadOnlyCollection<string> allowedPlaceholders)
    {
        var normalized = Normalize(value);
        if (normalized.Length is < 1 or > 2000)
            throw new ArgumentException("动效文案长度必须为 1–2000 个字符", nameof(value));
        if (normalized.Any(character => char.IsControl(character) && character != '\n'))
            throw new ArgumentException("动效文案包含不允许的控制字符", nameof(value));

        var matches = PlaceholderPattern.Matches(normalized);
        var withoutPlaceholders = PlaceholderPattern.Replace(normalized, string.Empty);
        if (BracePattern.IsMatch(withoutPlaceholders))
            throw new ArgumentException("动效文案包含无效的占位符", nameof(value));
        var allowed = allowedPlaceholders.ToHashSet(StringComparer.Ordinal);
        var unknown = matches.Select(match => match.Groups["name"].Value)
            .FirstOrDefault(name => !allowed.Contains(name));
        if (unknown is not null)
            throw new ArgumentException($"动效文案不允许占位符 {{{unknown}}}", nameof(value));
        return normalized;
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = PlaceholderPattern.Replace(template, match =>
        {
            var key = match.Groups["name"].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });
        return Normalize(rendered);
    }
}

public static class L12EffectPresentationSceneCatalog
{
    private sealed record Definition(string CardId, string? AbilityTrigger, string SceneKey,
        string EventType, string Label, string DefaultText, params string[] Placeholders);

    private static readonly Definition[] ExplicitScenes =
    [
        Scene("S01-0007", "play", "reveal-add", "reveal", "展示并加入手牌", "野外扎营展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-0013", "play", "opponent-hand", "reveal", "查看对方手牌", "前线侦查查看对方全部{count}张手牌", "count"),
        Scene("S01-0103", "enter", "top-card", "reveal", "展示牌库顶牌", "李靖展示牌库顶部的〈{cardName}〉", "cardName"),
        Scene("S01-0105", "active", "search-hit", "reveal", "检索成功", "刘备展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-0111", "attack", "attack-top-card", "reveal", "进攻时展示顶牌", "诸葛亮展示 {cardName}", "cardName"),
        Scene("S01-0111", "death", "death-top-card", "reveal", "阵亡时展示顶牌", "诸葛亮展示 {cardName}", "cardName"),
        Scene("S01-0117", "active", "search-hit", "reveal", "检索成功", "山河社稷图展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-0117", "active", "search-miss", "reveal", "检索未命中", "山河社稷图检索未命中，向对手展示顶部 3 张牌"),
        Scene("S01-0216", "enter", "search-hit", "reveal", "检索成功", "卡诺匹斯箱展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-0222", "play", "search-hit", "reveal", "加入手牌", "法老王的庆典展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-02D1", "text:公开牌库顶部3张牌", "top-three", "reveal", "公开牌库顶部三张", "众神之乡公开牌库顶部3张牌"),
        Scene("S01-02D1", "text:公开牌库顶部3张牌", "search-hit", "reveal", "加入手牌", "〈众神之乡〉展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-0419", "play", "reveal-add", "reveal", "展示并加入手牌", "花魁的馈赠展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S01-0419", "play", "search-add", "search", "加入手牌结果", "花魁的馈赠将〈{cardName}〉加入手牌", "cardName"),
        Scene("S01-0415", "enter", "enter-hide", "hidden-reveal", "发动隐匿前展示", "{cardName}展示后发动隐匿", "cardName"),
        Scene("S01-0204", "leave", "tomb-fallback-transition", "effect-trigger", "阵亡无效后转为离场效果", "陵墓构造体的【阵亡时】效果被无效，改由【离场时】效果直接发动"),
        Scene("S02-0008", "enter", "search-hit", "reveal", "检索成功", "万物统御之戒展示并将〈{cardName}〉加入手牌", "cardName"),
        Scene("S02-0012", "play", "public-disaster", "reveal", "公开天灾", "〈祷告仪式〉公开下1张天灾卡〈{cardName}〉", "cardName"),
        Scene("S02-0101", "enter", "condition-failed-hand", "reveal", "发动条件未满足", "始皇帝 嬴政登场时未满足发动条件，展示我方所有手牌"),
        Scene("S02-0102", "enter", "top-card", "reveal", "展示牌库顶牌", "李牧登场时，展示牌库顶的1张牌。"),
        Scene("S02-0103", "attack", "top-card", "reveal", "展示牌库顶牌", "平阳昭公主展示牌库顶部的〈{cardName}〉", "cardName"),
        Scene("S02-0106", null, "top-card", "reveal", "展示牌库顶牌", "〈乾坤·阴〉展示牌库顶部的〈{cardName}〉", "cardName"),
        Scene("S02-03M1", null, "setup-hammer", "reveal", "开局展示雷神之锤", "{playerName}展示卡牌〈雷神之锤〉", "playerName"),
        Scene("S02-0401", "enter", "search-hit", "reveal", "检索成功", "武田信玄展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0403", "attack", "top-card", "reveal", "展示牌库顶牌", "冲田总司展示牌库顶部的〈{cardName}〉", "cardName"),
        Scene("S02-0404", "enter", "search-hit", "reveal", "检索成功", "八尺琼勾玉展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0405", "play", "top-five", "reveal", "展示牌库顶部五张", "〈武运在天 铠甲在前〉展示牌库顶部5张牌"),
        Scene("S02-0405", "play", "artifact-picked", "reveal", "确认圣物加入手牌", "〈武运在天 铠甲在前〉确认将〈{cardName}〉加入手牌", "cardName"),
        Scene("S02-0405", "play", "uesugi-picked", "reveal", "确认上杉谦信加入手牌", "〈武运在天 铠甲在前〉确认将〈{cardName}〉加入手牌", "cardName"),
        Scene("S02-0501", "promotion-enter", "promotion-cost-declaration", "reveal", "公开声明时展示晋升效果费用", "赫拉克勒斯·晋升展示〈{cardName}〉并放回牌库顶部", "cardName"),
        Scene("S02-0501", "promotion-enter", "promotion-cost-resolution", "reveal", "旧式选择流程展示晋升效果费用", "赫拉克勒斯·晋升展示手牌中的〈{cardName}〉并放回牌库顶部", "cardName"),
        Scene("S02-0509", "attack", "attack-cost", "reveal", "展示进攻效果费用", "奥德修斯展示手牌中的〈{cardName}〉作为进攻效果费用", "cardName"),
        Scene("S02-0514", "enter", "search-hit", "reveal", "检索成功", "〈柏拉图〉展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0518", "death", "grave-hit", "reveal", "墓地卡牌加入手牌", "忒修斯展示墓地的〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0521", "play-additional", "search-hit", "reveal", "检索成功", "〈荣耀之路〉展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-05M2", "active", "search-hit", "reveal", "检索成功", "普罗米修斯展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0603", "active", "search-hit", "reveal", "检索成功", "梅林展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0603", "active", "search-miss", "reveal", "检索未命中", "梅林查看牌库，但未找到费用不高于4的主动战术"),
        Scene("S02-0616", "active", "top-card", "reveal", "展示牌库顶牌", "阿麦金展示牌库顶部的〈{cardName}〉", "cardName"),
        Scene("S02-0620", "play", "search-hit", "reveal", "检索成功", "〈符文之力〉展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-0621", "play", "search-hit", "reveal", "检索成功", "圆桌领域展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("S02-06S4", null, "search-hit", "reveal", "试炼检索成功", "寻找圣杯之旅展示〈{cardName}〉并加入手牌", "cardName"),
        Scene("ST03-03", "enter", "grave-hit", "reveal", "墓地卡牌加入手牌", "弗蕾迪斯展示〈{cardName}〉并将其加入手牌", "cardName"),
        Scene("ST05-06", "active", "search-hit", "reveal", "检索成功", "特勒马科斯展示〈{cardName}〉并将其加入手牌", "cardName"),
    ];

    public static L12AtomicAbility[] AttachExplicitScenes(L12AtomicAbility[] abilities)
    {
        if (abilities.Length == 0) return abilities;
        var cardId = abilities[0].CardId;
        var definitions = ExplicitScenes.Where(scene => scene.CardId.Equals(cardId,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (definitions.Length == 0) return abilities;

        var result = abilities.ToArray();
        foreach (var definition in definitions)
        {
            var index = definition.AbilityTrigger is null
                ? 0
                : definition.AbilityTrigger.StartsWith("text:", StringComparison.Ordinal)
                    ? Array.FindIndex(result, ability => ability.Text.Contains(
                        definition.AbilityTrigger["text:".Length..], StringComparison.Ordinal))
                    : Array.FindIndex(result, ability => ability.Trigger.Equals(definition.AbilityTrigger,
                        StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            var ability = result[index];
            var scene = new L12EffectPresentationScene(
                $"{ability.AbilityId}:presentation:{definition.SceneKey}", cardId, ability.AbilityId,
                definition.SceneKey, definition.DefaultText, EventType: definition.EventType,
                Label: definition.Label, AllowedPlaceholders: definition.Placeholders);
            result[index] = ability with { Presentations = ability.Presentations.Append(scene).ToArray() };
        }
        return result;
    }

    public static IReadOnlyList<string> ExplicitSceneKeys(string cardId)
        => ExplicitScenes.Where(scene => scene.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase))
            .Select(scene => scene.SceneKey).ToArray();

    public static IReadOnlyList<L12FrozenEffectPresentation> Freeze(
        IEnumerable<L12AtomicCardEffect> cards)
        => cards.OrderBy(card => card.CardId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(card => card.Abilities.OrderBy(ability => ability.Sequence))
            .SelectMany(ability => ability.Presentations)
            .Where(scene => scene.Overridden)
            .OrderBy(scene => scene.SceneId, StringComparer.Ordinal)
            .Select(scene => new L12FrozenEffectPresentation(scene.SceneId, scene.CardId,
                scene.Trigger, scene.EffectiveText, scene.Placeholders))
            .ToArray();

    private static Definition Scene(string cardId, string? trigger, string key, string eventType,
        string label, string text, params string[] placeholders)
        => new(cardId, trigger, key, eventType, label, text, placeholders);
}
