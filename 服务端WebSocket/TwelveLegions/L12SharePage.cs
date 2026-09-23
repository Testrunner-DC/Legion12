using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace TwelveLegions.Server;

internal sealed record L12ShareMetadata(string Title, string Description, string ImageUrl, string PageUrl,
    string Type);

internal static partial class L12SharePage
{
    internal const string Endpoint = "/_l12/share-page";
    internal const string OriginalPathHeader = "X-L12-Share-Path";
    internal const string TemplatePathEnvironmentKey = "L12_FRONTEND_INDEX_PATH";
    private const string DefaultTitle = "十二军团";
    private const string DefaultDescription = "十二军团服务器权威制网页对战版";
    private const string MetaStart = "<!-- l12-share-meta:start -->";
    private const string MetaEnd = "<!-- l12-share-meta:end -->";
    private static readonly object TemplateGate = new();
    private static string? _cachedTemplatePath;
    private static string? _cachedTemplate;

    internal static L12ShareMetadata Metadata(L12PlatformStore store, string? requestPath, string? publicBaseUrl)
    {
        var publicBase = PublicBase(publicBaseUrl);
        var pagePath = AppPath(requestPath, publicBase.AbsolutePath);
        var pageUrl = AbsolutePageUrl(publicBase, pagePath);
        var fallbackImage = AbsoluteAssetUrl(publicBase, "/favicon.png");
        var match = ArticlePath().Match(pagePath);
        if (!match.Success)
            return new L12ShareMetadata(DefaultTitle, DefaultDescription, fallbackImage, pageUrl, "website");

        string articleKey;
        try { articleKey = Uri.UnescapeDataString(match.Groups[1].Value); }
        catch (UriFormatException) { return new L12ShareMetadata(DefaultTitle, DefaultDescription, fallbackImage, pageUrl, "website"); }
        var article = store.PublicArticle(articleKey);
        if (article is null)
            return new L12ShareMetadata(DefaultTitle, DefaultDescription, fallbackImage, pageUrl, "website");

        var description = PlainText(article.Body);
        if (string.IsNullOrWhiteSpace(description)) description = PlainText(article.Summary);
        description = Truncate(description, 80);
        if (string.IsNullOrWhiteSpace(description)) description = DefaultDescription;
        var candidateImage = !string.IsNullOrWhiteSpace(article.CoverUrl)
            ? article.CoverUrl
            : article.BodyMedia?.FirstOrDefault()?.DesktopUrl;
        var image = PublicImageUrl(publicBase, candidateImage) ?? fallbackImage;
        return new L12ShareMetadata(article.Title.Trim(), description, image, pageUrl, "article");
    }

    internal static string Render(string template, L12ShareMetadata metadata)
    {
        var start = template.IndexOf(MetaStart, StringComparison.Ordinal);
        var end = template.IndexOf(MetaEnd, StringComparison.Ordinal);
        if (start < 0 || end < start) throw new InvalidDataException("前端入口缺少分享信息注入标记");
        end += MetaEnd.Length;
        var block = BuildMetaBlock(metadata);
        return string.Concat(template.AsSpan(0, start), block, template.AsSpan(end));
    }

    internal static string RenderDeploymentPage(L12PlatformStore store, string? requestPath)
    {
        var publicBaseUrl = Environment.GetEnvironmentVariable("L12_PUBLIC_BASE_URL");
        var templatePath = ResolveTemplatePath(publicBaseUrl);
        var template = DeploymentTemplate(templatePath);
        return Render(template, Metadata(store, requestPath, publicBaseUrl));
    }

    private static string DeploymentTemplate(string path)
    {
        lock (TemplateGate)
        {
            if (_cachedTemplate is not null && string.Equals(_cachedTemplatePath, path, StringComparison.Ordinal))
                return _cachedTemplate;
            _cachedTemplate = File.ReadAllText(path, Encoding.UTF8);
            _cachedTemplatePath = path;
            return _cachedTemplate;
        }
    }

    private static string ResolveTemplatePath(string? publicBaseUrl)
    {
        var configured = Environment.GetEnvironmentVariable(TemplatePathEnvironmentKey)?.Trim();
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var publicBase = PublicBase(publicBaseUrl);
        var distribution = publicBase.AbsolutePath.Trim('/') is { Length: > 0 } ? "dist-testrun" : "dist";
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "opcgpro-vue", distribution,
            "index.html"));
    }

    private static Uri PublicBase(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host))
            return new Uri("https://legion-12.com", UriKind.Absolute);
        return uri;
    }

    private static string AppPath(string? requestPath, string basePath)
    {
        var raw = (requestPath ?? "/").Trim();
        var query = raw.IndexOfAny(['?', '#']);
        if (query >= 0) raw = raw[..query];
        if (!raw.StartsWith('/')) raw = "/" + raw;
        var prefix = basePath.TrimEnd('/');
        if (prefix.Length > 0 && raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (raw.Length == prefix.Length || raw[prefix.Length] == '/'))
            raw = raw[prefix.Length..];
        if (string.IsNullOrWhiteSpace(raw)) raw = "/";
        return raw;
    }

    private static string AbsolutePageUrl(Uri publicBase, string pagePath)
        => publicBase.AbsoluteUri.TrimEnd('/') + (pagePath == "/" ? "/" : pagePath);

    private static string AbsoluteAssetUrl(Uri publicBase, string path)
        => publicBase.AbsoluteUri.TrimEnd('/') + "/" + path.TrimStart('/');

    private static string? PublicImageUrl(Uri publicBase, string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.StartsWith("//", StringComparison.Ordinal)) normalized = "https:" + normalized;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
            return absolute.Scheme == Uri.UriSchemeHttps ? absolute.AbsoluteUri : null;
        if (!normalized.StartsWith('/')) return null;
        return AbsoluteAssetUrl(publicBase, normalized);
    }

    private static string PlainText(string? body)
    {
        var value = (body ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (TryStructuredText(value, out var structured)) return NormalizeWhitespace(structured);
        try
        {
            var document = new HtmlParser().ParseDocument(HtmlBlockBoundary().Replace(value, " </$1>"));
            return NormalizeWhitespace(WebUtility.HtmlDecode(document.Body?.TextContent ?? value));
        }
        catch { return NormalizeWhitespace(WebUtility.HtmlDecode(value)); }
    }

    private static bool TryStructuredText(string value, out string text)
    {
        text = string.Empty;
        if (!value.StartsWith('{')) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format)
                || format.GetString() != "l12-blocks" || !root.TryGetProperty("blocks", out var blocks)
                || blocks.ValueKind != JsonValueKind.Array) return false;
            text = string.Join(' ', blocks.EnumerateArray().Select(block =>
                block.ValueKind == JsonValueKind.Object && block.TryGetProperty("text", out var content)
                    && content.ValueKind == JsonValueKind.String ? content.GetString() : null)
                .Where(content => !string.IsNullOrWhiteSpace(content)));
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string NormalizeWhitespace(string value) => Whitespace().Replace(value, " ").Trim();

    private static string Truncate(string value, int maxTextElements)
    {
        var elements = StringInfo.GetTextElementEnumerator(value);
        var builder = new StringBuilder();
        var count = 0;
        while (elements.MoveNext())
        {
            if (count == maxTextElements) return builder.Append('…').ToString();
            builder.Append(elements.GetTextElement());
            count++;
        }
        return builder.ToString();
    }

    private static string BuildMetaBlock(L12ShareMetadata metadata)
    {
        static string Encode(string value) => WebUtility.HtmlEncode(value);
        return $"""
    {MetaStart}
    <title>{Encode(metadata.Title)}</title>
    <meta name="description" content="{Encode(metadata.Description)}" />
    <meta property="og:type" content="{Encode(metadata.Type)}" />
    <meta property="og:site_name" content="{DefaultTitle}" />
    <meta property="og:title" content="{Encode(metadata.Title)}" />
    <meta property="og:description" content="{Encode(metadata.Description)}" />
    <meta property="og:image" content="{Encode(metadata.ImageUrl)}" />
    <meta property="og:url" content="{Encode(metadata.PageUrl)}" />
    <link rel="canonical" href="{Encode(metadata.PageUrl)}" />
    {MetaEnd}
    """;
    }

    [GeneratedRegex("^/news/([^/]+)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArticlePath();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"</(p|div|h[1-6]|li|blockquote|section|article|tr)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBlockBoundary();
}
