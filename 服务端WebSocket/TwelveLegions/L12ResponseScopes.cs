using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

public sealed record L12ResponseScopeDefinition(
    string ScopeId,
    int Sequence,
    string Text,
    string? CostText,
    string ResolutionText,
    string Source);

/// <summary>
/// 卡文的响应/无效范围与原子能力分解是两条独立轴。
/// 默认以顶层换行为范围边界；项目符号分支跟随其父段，只有每个分支都声明了自己的费用时才拆分。
/// 天灾不可响应，不生成范围。未来单卡特例只能在这里以显式覆盖登记，不能从原子数量或“随后”推断。
/// </summary>
public static class L12ResponseScopeRules
{
    private static readonly Regex HeadingPattern = new(@"^【[^】]+】[^。！？：:]*$", RegexOptions.CultureInvariant);
    private static readonly Regex BulletPattern = new(@"^[·•・]", RegexOptions.CultureInvariant);
    private static readonly Regex RuneBranchCostPattern = new(@"^[·•・]?\s*\d+\s*张符文\s*[：:]", RegexOptions.CultureInvariant);
    private static readonly Regex NonCostLabelPattern = new(
        @"^(?:双人模式|多人模式|开场触发|主动触发|选择(?:以下)?一?项|\d+\s*[~～至-]\s*\d+)\s*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyList<L12ResponseScopeDefinition> Build(L12CardDefinition card)
    {
        if (card.CardType is "disaster" or "destruction") return [];
        var text = Normalize(card.Effect);
        if (string.IsNullOrWhiteSpace(text) || text == "无效果") return [];

        var groups = BuildTopLevelGroups(text);
        var result = new List<L12ResponseScopeDefinition>();
        foreach (var group in groups)
        {
            var bullets = group.Where(IsBullet).ToArray();
            if (bullets.Length > 0 && bullets.All(HasBranchSpecificCost))
            {
                var parent = string.Join('\n', group.TakeWhile(line => !IsBullet(line)));
                foreach (var bullet in bullets)
                {
                    var branchText = string.IsNullOrWhiteSpace(parent) ? bullet : $"{parent}\n{bullet}";
                    result.Add(Create(card.Id, result.Count + 1, branchText, "printed-branch-line-default"));
                }
                continue;
            }

            result.Add(Create(card.Id, result.Count + 1, string.Join('\n', group), "printed-line-default"));
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildTopLevelGroups(string text)
    {
        var groups = new List<IReadOnlyList<string>>();
        var pendingHeadings = new List<string>();
        List<string>? current = null;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (HeadingPattern.IsMatch(line))
            {
                if (current is not null)
                {
                    groups.Add(current);
                    current = null;
                }
                pendingHeadings.Add(line);
                continue;
            }
            if (IsBullet(line) && current is not null)
            {
                current.Add(line);
                continue;
            }
            if (current is not null) groups.Add(current);
            current = [.. pendingHeadings, line];
            pendingHeadings.Clear();
        }
        if (current is not null) groups.Add(current);
        else if (pendingHeadings.Count > 0) groups.Add(pendingHeadings);
        return groups;
    }

    private static L12ResponseScopeDefinition Create(string cardId, int sequence, string text, string source)
    {
        var normalized = Normalize(text);
        var (cost, resolution) = SplitCost(normalized);
        return new L12ResponseScopeDefinition(
            $"{cardId}:response-scope:{sequence}", sequence, normalized, cost, resolution, source);
    }

    private static (string? CostText, string ResolutionText) SplitCost(string text)
    {
        var separator = text.IndexOfAny(['：', ':']);
        if (separator <= 0 || separator + 1 >= text.Length) return (null, text);
        var prefix = text[..separator].Trim();
        var clauseStart = prefix.LastIndexOfAny(['。', '；', ';', '\n']);
        var clause = prefix[(clauseStart + 1)..].Trim().TrimStart('·', '•', '・').Trim();
        if (NonCostLabelPattern.IsMatch(clause)) return (null, text);
        return ($"{prefix}{text[separator]}", text[(separator + 1)..].Trim());
    }

    private static bool IsBullet(string line) => BulletPattern.IsMatch(line);

    private static bool HasBranchSpecificCost(string line)
        => RuneBranchCostPattern.IsMatch(line)
            || L12StructuredCardRules.HasPrintedCostBoundary(line.TrimStart('·', '•', '・').Trim());

    private static string Normalize(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Trim();
}
