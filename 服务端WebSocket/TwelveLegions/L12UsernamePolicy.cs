using System.Globalization;
using System.Text;

namespace TwelveLegions.Server;

internal static class L12UsernamePolicy
{
    public const int MinimumTextElements = 2;
    public const int MaximumTextElements = 11;

    private static readonly string[] ProhibitedTerms =
    [
        // 官方身份与运营人员冒充
        "管理员", "admin", "administrator", "administer", "system", "系统", "官方", "客服", "裁判", "gm", "gamemaster",
        // 严重辱骂、色情和违法交易
        "fuck", "shit", "傻逼", "操你妈", "色情", "约炮", "赌博", "赌场", "毒品", "买号", "卖号", "代充", "外挂",
        // 广告导流和联系方式变体
        "加微信", "微信号", "微商", "qq群", "加qq", "telegram", "http", "www",
    ];

    private static readonly string[] NormalizedTerms = ProhibitedTerms
        .Select(NormalizeForMatching)
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderByDescending(value => value.Length)
        .ToArray();

    public static string? Validate(string username)
    {
        var length = TextElementCount(username);
        if (length is < MinimumTextElements or > MaximumTextElements)
            return $"用户名长度需为 {MinimumTextElements}–{MaximumTextElements} 个可见字符";
        if (username.Any(char.IsControl)) return "用户名包含不允许使用的字符";
        if (FindMaskedTextElements(username).Count > 0) return "用户名包含不允许使用的词语";
        return null;
    }

    public static bool RequiresChange(string username) => Validate(username) is not null;

    public static string PublicName(string username)
    {
        var elements = TextElements(username);
        var masked = FindMaskedTextElements(username);
        if (masked.Count == 0) return username;
        return string.Concat(elements.Select((value, index) => masked.Contains(index) ? "*" : value));
    }

    private static HashSet<int> FindMaskedTextElements(string username)
    {
        var elements = TextElements(username);
        var normalized = new StringBuilder();
        var sourceIndexes = new List<int>();
        for (var elementIndex = 0; elementIndex < elements.Count; elementIndex++)
        {
            var value = elements[elementIndex].Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            foreach (var rune in value.EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.ConnectorPunctuation
                    or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
                    or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                    or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation
                    or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
                    or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol)
                    continue;
                var text = rune.ToString();
                normalized.Append(text);
                for (var index = 0; index < text.Length; index++) sourceIndexes.Add(elementIndex);
            }
        }

        var result = new HashSet<int>();
        var candidate = normalized.ToString();
        foreach (var term in NormalizedTerms)
        {
            var start = 0;
            while ((start = candidate.IndexOf(term, start, StringComparison.Ordinal)) >= 0)
            {
                for (var index = start; index < start + term.Length && index < sourceIndexes.Count; index++)
                    result.Add(sourceIndexes[index]);
                start += Math.Max(1, term.Length);
            }
        }
        return result;
    }

    private static string NormalizeForMatching(string value)
    {
        var builder = new StringBuilder();
        foreach (var rune in value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static int TextElementCount(string value) => new StringInfo(value).LengthInTextElements;

    private static IReadOnlyList<string> TextElements(string value)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext()) result.Add(enumerator.GetTextElement());
        return result;
    }
}
