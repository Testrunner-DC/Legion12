using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SkiaSharp;

namespace TwelveLegions.Server;

public sealed record L12ModianSourceSummary(string UpdateId, int Sequence, string Title,
    DateTimeOffset OriginalPublishedAt);

public sealed record L12ModianSourceDetail(string UpdateId, string ProjectId, string CreatorUserId,
    string Title, DateTimeOffset OriginalPublishedAt, string Html, string? LogoUrl,
    string? ProjectCoverUrl = null, bool LogoRejected = false);

public sealed record L12ModianRemoteImage(string ContentType, byte[] Bytes);

public sealed record L12ModianImportRequest(IReadOnlyList<string>? UpdateIds, string? CategoryId,
    IReadOnlyList<string>? ReimportUpdateIds = null, IReadOnlyList<string>? OverwriteLocalChangesIds = null,
    string? IdempotencyKey = null);

public interface IL12ModianImportClient : IDisposable
{
    Task<IReadOnlyList<L12ModianSourceSummary>> ListUpdatesAsync(CancellationToken cancellationToken);
    Task<L12ModianSourceDetail> GetUpdateAsync(string updateId, CancellationToken cancellationToken);
    Task<L12ModianRemoteImage> DownloadImageAsync(string source, CancellationToken cancellationToken);
}

public sealed class L12ModianImportClient : IL12ModianImportClient
{
    public const string ProjectId = "157664";
    public const string CreatorUserId = "1000154630";
    public const int MaxListBytes = 2 * 1024 * 1024;
    public const int MaxDetailBytes = 2 * 1024 * 1024;
    public const int MaxImageBytes = L12PlatformStore.SiteMediaOriginalMaxBytes;
    private const string DetailSignatureSalt = "MzgxOTg3ZDMZTgxO";
    private static readonly Regex Digits = new("^[0-9]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex DateExpression = new(
        "(?<date>[0-9]{4}[-/.][0-9]{1,2}[-/.][0-9]{1,2}\\s+[0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ApiHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "zhongchou.modian.com", "apim.modian.com",
    };
    private static readonly HashSet<string> ImageHosts = Enumerable.Range(1, 9)
        .Select(index => $"p{index}.moimg.net").ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<HttpStatusCode> RetryableStatuses =
    [
        HttpStatusCode.RequestTimeout, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout,
    ];

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requestGate = new(2, 2);
    private readonly object _rateLock = new();
    private readonly TimeSpan _minimumDelay;
    private DateTimeOffset _nextRequestAt;

    public L12ModianImportClient(HttpMessageHandler? handler = null, TimeSpan? minimumDelay = null)
    {
        _http = new HttpClient(handler ?? CreateSafeHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Legion12-Modian-Draft-Importer/1.0");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
        _minimumDelay = minimumDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task<IReadOnlyList<L12ModianSourceSummary>> ListUpdatesAsync(
        CancellationToken cancellationToken)
    {
        var callback = $"l12_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var uri = new Uri("https://zhongchou.modian.com/realtime/ajax_updates"
            + $"?pro_id={ProjectId}&creater_user_id={CreatorUserId}&jsonpcallback={callback}");
        var response = await SendBoundedAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), MaxListBytes,
            TimeSpan.FromSeconds(12), cancellationToken);
        EnsureTextContentType(response.ContentType, "更新列表");
        var text = Encoding.UTF8.GetString(response.Bytes).Trim();
        using var document = JsonDocument.Parse(ExtractJsonpPayload(text, callback));
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("html", out var htmlValue) || htmlValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("摩点更新列表结构已变化");
        return ParseUpdateListHtml(htmlValue.GetString() ?? string.Empty);
    }

    public async Task<L12ModianSourceDetail> GetUpdateAsync(string updateId,
        CancellationToken cancellationToken)
    {
        if (!Digits.IsMatch(updateId)) throw new ArgumentException("摩点更新标识无效");
        var canonicalBody = $"json_type=1&update_id={Uri.EscapeDataString(updateId)}";
        var moment = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var bodyHash = Md5(canonicalBody);
        var sign = Md5("apim.modian.com/honor/product/update_detail" + DetailSignatureSalt + moment + bodyHash);
        var response = await SendBoundedAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://apim.modian.com/honor/product/update_detail");
            request.Headers.TryAddWithoutValidation("client", "4");
            request.Headers.TryAddWithoutValidation("mt", moment);
            request.Headers.TryAddWithoutValidation("sign", sign);
            request.Headers.Referrer = new Uri($"https://zhongchou.modian.com/item/{ProjectId}.html");
            request.Content = new StringContent(canonicalBody, Encoding.UTF8,
                "application/x-www-form-urlencoded");
            return request;
        }, MaxDetailBytes, TimeSpan.FromSeconds(15), cancellationToken);
        EnsureJsonContentType(response.ContentType, "更新详情");
        using var document = JsonDocument.Parse(response.Bytes);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("摩点更新详情结构已变化");
        var id = JsonScalar(data, "id");
        var projectId = JsonScalar(data, "pro_id");
        var creator = JsonScalar(data, "pro_user_id");
        if (id != updateId || projectId != ProjectId || creator != CreatorUserId)
            throw new InvalidDataException("摩点更新详情身份校验失败");
        var title = CleanSourceText(JsonScalar(data, "title"));
        var html = JsonScalar(data, "content");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(html) || html.Length > 1_000_000)
            throw new InvalidDataException("摩点更新详情缺少标题或正文，或正文超过限制");
        var publishedAt = ParseShanghaiTime(JsonScalar(data, "ctime"));
        var logo = JsonScalar(data, "logo");
        string? normalizedLogo = null;
        var logoRejected = false;
        if (!string.IsNullOrWhiteSpace(logo))
        {
            try { normalizedLogo = NormalizeImageSource(logo); }
            catch (InvalidDataException) { logoRejected = true; }
        }
        string? projectCover = null;
        var rawProjectCover = JsonScalar(data, "pro_cover");
        if (!string.IsNullOrWhiteSpace(rawProjectCover))
        {
            try { projectCover = NormalizeImageSource(rawProjectCover); }
            catch (InvalidDataException) { /* 固定项目封面同样必须通过项目路径允许列表。 */ }
        }
        return new(id, projectId, creator, title, publishedAt, html, normalizedLogo, projectCover,
            logoRejected);
    }

    public async Task<L12ModianRemoteImage> DownloadImageAsync(string source,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeImageSource(source);
        var uri = new Uri(normalized, UriKind.Absolute);
        if (!ImageHosts.Contains(uri.IdnHost)) throw new InvalidDataException("正文图片主机不在固定允许列表中");
        var response = await SendBoundedAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), MaxImageBytes,
            TimeSpan.FromSeconds(15), cancellationToken);
        var detected = DetectImageType(response.Bytes);
        if (detected is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new InvalidDataException("正文图片不是允许的 JPEG、PNG 或静态 WebP");
        var declared = response.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(declared) && declared != "application/octet-stream" && declared != detected)
            throw new InvalidDataException("正文图片 MIME 与文件签名不一致");
        return new(detected, response.Bytes);
    }

    internal static IReadOnlyList<L12ModianSourceSummary> ParseUpdateListHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 1_500_000)
            throw new InvalidDataException("摩点更新列表为空或超过限制");
        var document = new HtmlParser().ParseDocument(html);
        var items = new List<L12ModianSourceSummary>();
        foreach (var element in document.QuerySelectorAll(".tack-gitem[data-update_id], [data-update_id][data-pro_id]"))
        {
            var updateId = element.GetAttribute("data-update_id")?.Trim() ?? string.Empty;
            var projectId = element.GetAttribute("data-pro_id")?.Trim() ?? string.Empty;
            if (!Digits.IsMatch(updateId) || projectId != ProjectId) continue;
            var title = CleanSourceText(element.GetAttribute("data-title")
                ?? element.QuerySelector(".tack-title, .tack-gtitle, .title, h2, h3, h4")?.TextContent
                ?? element.QuerySelector("a")?.TextContent ?? string.Empty);
            var fullText = element.TextContent;
            var dateText = element.QuerySelector("time, .ctime, .tack-time, .time")?.TextContent
                ?? DateExpression.Match(fullText).Groups["date"].Value;
            var sequenceText = element.GetAttribute("data-num")
                ?? element.QuerySelector(".tack-num, .num")?.TextContent ?? fullText;
            var sequenceMatch = Regex.Match(sequenceText, "[0-9]+", RegexOptions.CultureInvariant);
            var sequence = sequenceMatch.Success && int.TryParse(sequenceMatch.Value, out var parsedSequence)
                ? parsedSequence : 0;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(dateText))
                throw new InvalidDataException($"摩点更新 {updateId} 缺少标题或发布时间");
            items.Add(new(updateId, sequence, title, ParseShanghaiTime(dateText)));
        }
        var unique = items.GroupBy(item => item.UpdateId, StringComparer.Ordinal).Select(group => group.First())
            .Take(201).ToArray();
        if (unique.Length == 0 || unique.Length > 200) throw new InvalidDataException("摩点更新列表数量异常");
        return unique;
    }

    internal static string ExtractJsonpPayload(string text, string callback)
    {
        if (!Regex.IsMatch(callback, "^[A-Za-z0-9_]{1,80}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("JSONP 回调标识无效");
        var direct = callback + "(";
        var windowSingle = $"window[decodeURIComponent('{Uri.EscapeDataString(callback)}')](";
        var windowDouble = $"window[decodeURIComponent(\"{Uri.EscapeDataString(callback)}\")](";
        var prefix = text.StartsWith(direct, StringComparison.Ordinal) ? direct
            : text.StartsWith(windowSingle, StringComparison.Ordinal) ? windowSingle
            : text.StartsWith(windowDouble, StringComparison.Ordinal) ? windowDouble
            : throw new InvalidDataException("摩点更新列表未返回本次请求对应的 JSONP 包装");
        var suffixLength = text.EndsWith(");", StringComparison.Ordinal) ? 2
            : text.EndsWith(')') ? 1 : 0;
        if (suffixLength == 0 || text.Length <= prefix.Length + suffixLength)
            throw new InvalidDataException("摩点更新列表 JSONP 结尾无效");
        return text[prefix.Length..^suffixLength];
    }

    internal static string NormalizeImageSource(string source)
    {
        var normalized = WebUtility.HtmlDecode(source ?? string.Empty).Trim();
        if (normalized.StartsWith("//", StringComparison.Ordinal)) normalized = "https:" + normalized;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !ImageHosts.Contains(uri.IdnHost) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443
            || uri.Query.Length > 256)
            throw new InvalidDataException("正文图片地址不在固定 HTTPS 允许列表中");
        string decodedPath;
        try { decodedPath = Uri.UnescapeDataString(uri.AbsolutePath); }
        catch (UriFormatException) { throw new InvalidDataException("正文图片路径无效"); }
        if (!decodedPath.StartsWith("/path/dst_project/1000154630/", StringComparison.Ordinal)
            || decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
            throw new InvalidDataException("正文图片不属于固定项目素材路径");
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }

    private async Task<BoundedResponse> SendBoundedAsync(Func<HttpRequestMessage> requestFactory, int maxBytes,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        Exception? finalError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await WaitForRateSlotAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var request = requestFactory();
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token);
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new HttpRequestException("外部内容响应发生了不允许的重定向");
                if (RetryableStatuses.Contains(response.StatusCode) && attempt < 2)
                {
                    await RetryDelayAsync(response, attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length > maxBytes)
                    throw new InvalidDataException("外部内容超过允许大小");
                var bytes = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(timeoutSource.Token),
                    maxBytes, timeoutSource.Token);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                return new(contentType, bytes);
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                finalError = new TimeoutException("读取外部内容超时", error);
            }
            catch (HttpRequestException error) when (attempt < 2)
            {
                finalError = error;
            }
            if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), cancellationToken);
        }
        throw finalError ?? new HttpRequestException("读取外部内容失败");
    }

    private async Task WaitForRateSlotAsync(CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            TimeSpan delay;
            lock (_rateLock)
            {
                var now = DateTimeOffset.UtcNow;
                delay = _nextRequestAt > now ? _nextRequestAt - now : TimeSpan.Zero;
                _nextRequestAt = (delay > TimeSpan.Zero ? _nextRequestAt : now) + _minimumDelay;
            }
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
        }
        finally { _requestGate.Release(); }
    }

    private static async Task RetryDelayAsync(HttpResponseMessage response, int attempt,
        CancellationToken cancellationToken)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        var delay = retryAfter is { } requested && requested <= TimeSpan.FromSeconds(5)
            ? requested : TimeSpan.FromMilliseconds(400 * (attempt + 1));
        await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(20, 121)), cancellationToken);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes) throw new InvalidDataException("外部内容超过允许大小");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static SocketsHttpHandler CreateSafeHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = ConnectPublicHostAsync,
    };

    private static async ValueTask<Stream> ConnectPublicHostAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        if (!ApiHosts.Contains(host) && !ImageHosts.Contains(host))
            throw new HttpRequestException("外部请求主机不在固定允许列表中");
        var addresses = (await Dns.GetHostAddressesAsync(host, cancellationToken))
            .Where(IsPublicAddress).ToArray();
        if (addresses.Length == 0) throw new HttpRequestException("外部请求主机未解析到公网地址");
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception error) when (error is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                last = error;
                if (error is OperationCanceledException) throw;
            }
        }
        throw new HttpRequestException("无法连接外部内容主机", last);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return false;
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0]; var second = bytes[1];
            return first is not (0 or 10 or 127) && first < 224
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second is 0 or 168)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && bytes[2] == 100)
                && !(first == 203 && second == 0 && bytes[2] == 113);
        }
        return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal
            && (bytes[0] & 0xfe) != 0xfc
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    private static void EnsureTextContentType(string value, string label)
    {
        var mediaType = value.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType is not ("application/json" or "application/javascript" or "text/javascript"
            or "text/plain" or "text/html")) throw new InvalidDataException($"{label} MIME 类型不受支持");
    }

    private static void EnsureJsonContentType(string value, string label)
    {
        var mediaType = value.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType is not ("application/json" or "text/json" or "text/plain"))
            throw new InvalidDataException($"{label} MIME 类型不受支持");
    }

    private static string JsonScalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    internal static DateTimeOffset ParseShanghaiTime(string value)
    {
        var normalized = WebUtility.HtmlDecode(value ?? string.Empty).Trim().Replace('/', '-').Replace('.', '-');
        if (!DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var local))
            throw new InvalidDataException("摩点更新时间格式无效");
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
    }

    internal static string CleanSourceText(string value)
        => Regex.Replace(WebUtility.HtmlDecode(value ?? string.Empty), "\\s+", " ").Trim();

    private static string Md5(string value)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string DetectImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        return "application/octet-stream";
    }

    public void Dispose()
    {
        _requestGate.Dispose();
        _http.Dispose();
    }

    private sealed record BoundedResponse(string ContentType, byte[] Bytes);
}

public sealed class L12ModianImportService : IDisposable
{
    public const int MaxUpdatesPerBatch = 50;
    public const int MaxImagesPerUpdate = 30;
    public const long MaxImageBytesPerUpdate = 64L * 1024 * 1024;
    private static readonly Regex UpdateIdPattern = new("^[0-9]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex VisibleModianUrl = new(
        "(?i)(?:(?:https?:)?//|www\\.)[^\\s<>\"']*modian\\.com[^\\s<>\"']*",
        RegexOptions.CultureInvariant);
    private static readonly Regex AnyVisibleUrl = new(
        "(?i)(?:(?:https?:)?//|www\\.)[^\\s<>\"']+", RegexOptions.CultureInvariant);
    private readonly L12PlatformStore _store;
    private readonly IL12ModianImportClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public L12ModianImportService(L12PlatformStore store, IL12ModianImportClient? client = null)
    {
        _store = store;
        _client = client ?? new L12ModianImportClient();
        _ownsClient = client is null;
    }

    public async Task<L12ModianImportPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var preview = _store.DescribeModianUpdates(await _client.ListUpdatesAsync(cancellationToken));
            // 新条目无需详情即可判定；仅复查已导入条目的强指纹，最多两路并发并继续受客户端限流。
            using var detailGate = new SemaphoreSlim(2, 2);
            var inspected = await Task.WhenAll(preview.Items.Select(async item =>
            {
                if (item.State == "new") return item;
                await detailGate.WaitAsync(cancellationToken);
                try
                {
                    var detail = await _client.GetUpdateAsync(item.UpdateId, cancellationToken);
                    var decision = _store.DecideModianDraft(item.UpdateId, SourceFingerprint(detail), false, false);
                    if (decision.Status == "changed-requires-reimport")
                        return item with { State = item.LocalDraftChanged ? "remote-changed-local-edited" : "remote-changed" };
                    return item;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    return item with { State = "check-failed", CheckMessage = SafeFailureMessage(error) };
                }
                finally { detailGate.Release(); }
            }));
            return preview with { Items = inspected };
        }
        finally { _operationGate.Release(); }
    }

    public async Task<L12ModianImportBatchResult> ImportAsync(L12AccountView actor,
        L12ModianImportRequest request, L12AdminAuditContext? context, CancellationToken cancellationToken)
    {
        var updateIds = NormalizeIds(request.UpdateIds, "至少选择一条要导入的更新");
        var reimportIds = NormalizeIds(request.ReimportUpdateIds, null, allowEmpty: true).ToHashSet(StringComparer.Ordinal);
        var overwriteIds = NormalizeIds(request.OverwriteLocalChangesIds, null, allowEmpty: true)
            .ToHashSet(StringComparer.Ordinal);
        if (updateIds.Count > MaxUpdatesPerBatch) throw new ArgumentException($"单批最多导入 {MaxUpdatesPerBatch} 条更新");
        if (reimportIds.Except(updateIds).Any() || overwriteIds.Except(reimportIds).Any())
            throw new ArgumentException("重新导入和覆盖本地草稿的选择范围无效");
        var categoryId = request.CategoryId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(categoryId) || categoryId.Length > 100)
            throw new ArgumentException("必须选择资讯分类");

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var source = await _client.ListUpdatesAsync(cancellationToken);
            var byId = source.ToDictionary(item => item.UpdateId, StringComparer.Ordinal);
            if (updateIds.Any(id => !byId.ContainsKey(id)))
                throw new ArgumentException("所选更新不在固定项目的当前更新列表中");
            var results = new List<L12ModianImportItemResult>();
            foreach (var summary in updateIds.Select(id => byId[id]).OrderBy(item => item.Sequence)
                         .ThenBy(item => item.OriginalPublishedAt))
            {
                try
                {
                    results.Add(await ImportOneAsync(actor, categoryId, summary, reimportIds.Contains(summary.UpdateId),
                        overwriteIds.Contains(summary.UpdateId), context, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    var message = SafeFailureMessage(error);
                    _store.AuditModianImportFailure(actor, summary.UpdateId, message, context);
                    results.Add(new(summary.UpdateId, "failed", message));
                }
            }
            return new(L12PlatformStore.ModianProjectId, updateIds.Count,
                results.Count(item => item.Status == "created"), results.Count(item => item.Status == "updated"),
                results.Count(item => item.Status == "unchanged"),
                results.Count(item => item.Status == "changed-requires-reimport"),
                results.Count(item => item.Status == "local-conflict"), results.Count(item => item.Status == "failed"),
                results);
        }
        finally { _operationGate.Release(); }
    }

    private async Task<L12ModianImportItemResult> ImportOneAsync(L12AccountView actor, string categoryId,
        L12ModianSourceSummary summary, bool allowReimport, bool overwriteLocalChanges,
        L12AdminAuditContext? context, CancellationToken cancellationToken)
    {
        var detail = await _client.GetUpdateAsync(summary.UpdateId, cancellationToken);
        if (detail.UpdateId != summary.UpdateId || detail.ProjectId != L12PlatformStore.ModianProjectId)
            throw new InvalidDataException("摩点更新身份校验失败");
        var parsed = L12ModianHtmlConverter.Convert(detail.UpdateId, detail.Title, detail.Html);
        var title = ScrubVisibleModianUrl(detail.Title);
        var sourceFingerprint = SourceFingerprint(detail);
        var decision = _store.DecideModianDraft(detail.UpdateId, sourceFingerprint, allowReimport,
            overwriteLocalChanges);
        if (decision.Status != "apply")
            return decision.Status switch
            {
                "unchanged" => new(detail.UpdateId, "unchanged", "来源内容未变化，未改动草稿", decision.ArticleId),
                "changed-requires-reimport" => new(detail.UpdateId, "changed-requires-reimport",
                    "检测到来源变化；必须显式选择重新导入才会更新草稿", decision.ArticleId),
                _ => new(detail.UpdateId, "local-conflict",
                    "草稿已有本地编辑；必须显式确认覆盖本地草稿后才能重新导入", decision.ArticleId),
            };

        if (parsed.Images.Count > MaxImagesPerUpdate)
            throw new InvalidDataException($"单篇更新图片超过 {MaxImagesPerUpdate} 张限制");
        var downloads = new List<L12ModianRemoteImage>(parsed.Images.Count);
        long totalBytes = 0;
        foreach (var image in parsed.Images)
        {
            var downloaded = await _client.DownloadImageAsync(image, cancellationToken);
            totalBytes += downloaded.Bytes.LongLength;
            if (totalBytes > MaxImageBytesPerUpdate) throw new InvalidDataException("单篇更新图片总量超过 64MB 限制");
            downloads.Add(downloaded);
        }

        var bodyUploads = downloads.Select((image, index) =>
            L12ImportedImageProcessor.Body(image, $"《{title}》正文图片 {index + 1}")).ToArray();
        L12SiteMediaUpload? cover = null;
        var warnings = parsed.Warnings.ToList();
        if (detail.LogoRejected) warnings.Add("更新封面地址未通过安全校验，已启用封面回退");
        if (!string.IsNullOrWhiteSpace(detail.LogoUrl))
        {
            try
            {
                var logoIndex = -1;
                for (var index = 0; index < parsed.Images.Count; index++)
                    if (string.Equals(parsed.Images[index], detail.LogoUrl, StringComparison.Ordinal))
                    {
                        logoIndex = index;
                        break;
                    }
                var logo = logoIndex >= 0 ? downloads[logoIndex]
                    : await _client.DownloadImageAsync(detail.LogoUrl, cancellationToken);
                if (logoIndex < 0)
                {
                    totalBytes += logo.Bytes.LongLength;
                    if (totalBytes > MaxImageBytesPerUpdate)
                        throw new InvalidDataException("单篇更新图片总量超过 64MB 限制");
                }
                cover = L12ImportedImageProcessor.Cover(logo, title);
            }
            catch (Exception error) when (error is not OperationCanceledException
                                          && totalBytes <= MaxImageBytesPerUpdate)
            {
                warnings.Add("更新封面不可用，已改用正文首图");
            }
        }
        if (cover is null && downloads.Count > 0)
            cover = L12ImportedImageProcessor.Cover(downloads[0], title);
        if (cover is null && !string.IsNullOrWhiteSpace(detail.ProjectCoverUrl))
        {
            var projectCover = await _client.DownloadImageAsync(detail.ProjectCoverUrl, cancellationToken);
            totalBytes += projectCover.Bytes.LongLength;
            if (totalBytes > MaxImageBytesPerUpdate)
                throw new InvalidDataException("单篇更新图片总量超过 64MB 限制");
            cover = L12ImportedImageProcessor.Cover(projectCover, title);
            warnings.Add("更新无可用独立封面或正文图片，已使用固定项目封面");
        }
        if (cover is null) throw new InvalidDataException("来源未提供可用封面，未生成不完整草稿");

        var prepared = new L12ModianPreparedDraft(detail.UpdateId, title, parsed.Summary,
            detail.OriginalPublishedAt, sourceFingerprint, parsed.Blocks, bodyUploads, cover, warnings);
        return _store.ApplyModianDraft(actor, categoryId, prepared, allowReimport, overwriteLocalChanges, context);
    }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? values, string? emptyMessage,
        bool allowEmpty = false)
    {
        var ids = (values ?? []).Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (!allowEmpty && ids.Length == 0) throw new ArgumentException(emptyMessage ?? "选择不能为空");
        if (ids.Any(id => !UpdateIdPattern.IsMatch(id))) throw new ArgumentException("摩点更新标识无效");
        return ids;
    }

    private static string SourceFingerprint(L12ModianSourceDetail detail)
    {
        var canonical = $"l12-modian-v1\n{detail.UpdateId}\n{detail.Title}\n{detail.OriginalPublishedAt:O}\n"
            + $"{detail.LogoUrl}\n{detail.ProjectCoverUrl}\n{detail.LogoRejected}\n{detail.Html}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static string ScrubVisibleModianUrl(string value)
        => Regex.Replace(VisibleModianUrl.Replace(value ?? string.Empty, string.Empty), "\\s+", " ").Trim();

    private static string SafeFailureMessage(Exception error)
    {
        var message = AnyVisibleUrl.Replace(error.Message, "[地址已隐藏]");
        message = Regex.Replace(message, "\\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(message) ? "导入失败" : message[..Math.Min(message.Length, 240)];
    }

    public void Dispose()
    {
        _operationGate.Dispose();
        if (_ownsClient) _client.Dispose();
    }
}

internal sealed record L12ModianParsedContent(IReadOnlyList<L12ModianBlockPlan> Blocks,
    IReadOnlyList<string> Images, string Summary, IReadOnlyList<string> Warnings);

internal static class L12ModianHtmlConverter
{
    private static readonly HashSet<string> ForbiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCRIPT", "STYLE", "IFRAME", "OBJECT", "EMBED", "FORM", "INPUT", "BUTTON", "VIDEO", "AUDIO",
        "SVG", "MATH", "CANVAS", "NOSCRIPT", "TEMPLATE",
    };
    private static readonly HashSet<string> MarkElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "B", "STRONG", "I", "EM", "U", "S", "DEL",
    };

    internal static L12ModianParsedContent Convert(string updateId, string title, string html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 1_000_000)
            throw new InvalidDataException("摩点更新正文为空或超过限制");
        var document = new HtmlParser().ParseDocument(html);
        var root = document.Body ?? document.DocumentElement;
        var blocks = new List<L12ModianBlockPlan>();
        var images = new List<string>();
        var imageIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in root.ChildNodes) AppendBlock(child, updateId, blocks, images, imageIndexes, warnings);
        blocks = blocks.Where(block => block.Type is "image" or "divider" || !string.IsNullOrWhiteSpace(block.Text))
            .Take(201).ToList();
        if (blocks.Count is 0 or > 200) throw new InvalidDataException("转换后的正文内容块数量无效");
        var text = string.Join(' ', blocks.Where(block => block.Type is not ("image" or "divider"))
            .Select(block => block.Text)).Trim();
        var summary = text.Length <= 260 ? text : text[..257].TrimEnd() + "…";
        if (string.IsNullOrWhiteSpace(summary)) summary = L12ModianImportService.ScrubVisibleModianUrl(title);
        return new(blocks, images, summary, warnings.ToArray());
    }

    private static void AppendBlock(INode node, string updateId, List<L12ModianBlockPlan> blocks,
        List<string> images, Dictionary<string, int> imageIndexes, HashSet<string> warnings)
    {
        if (node is IText textNode)
        {
            var text = CleanText(textNode.Data);
            if (!string.IsNullOrWhiteSpace(text)) AddTextBlock("paragraph", text, [], "left", updateId, blocks);
            return;
        }
        if (node is not IElement element || ForbiddenElements.Contains(element.TagName))
        {
            if (node is IElement) warnings.Add("已移除不支持的主动内容");
            return;
        }
        switch (element.TagName)
        {
            case "H1": case "H2":
                AddInlineBlock("h2", element, updateId, blocks, warnings);
                AddDescendantImages(element, updateId, blocks, images, imageIndexes, warnings);
                return;
            case "H3": case "H4": case "H5": case "H6":
                AddInlineBlock("h3", element, updateId, blocks, warnings);
                AddDescendantImages(element, updateId, blocks, images, imageIndexes, warnings);
                return;
            case "P":
                AddInlineBlock("paragraph", element, updateId, blocks, warnings);
                AddDescendantImages(element, updateId, blocks, images, imageIndexes, warnings);
                return;
            case "BLOCKQUOTE":
                AddInlineBlock("quote", element, updateId, blocks, warnings);
                AddDescendantImages(element, updateId, blocks, images, imageIndexes, warnings);
                return;
            case "UL": case "OL":
                AddListBlock(element, updateId, blocks, warnings);
                AddDescendantImages(element, updateId, blocks, images, imageIndexes, warnings);
                return;
            case "HR":
                blocks.Add(new(BlockId(updateId, blocks.Count), "divider")); return;
            case "IMG":
                AddImage(element, updateId, blocks, images, imageIndexes, warnings); return;
            case "FIGURE":
                foreach (var image in element.QuerySelectorAll("img"))
                    AddImage(image, updateId, blocks, images, imageIndexes, warnings);
                var caption = element.QuerySelector("figcaption");
                if (caption is not null) AddInlineBlock("paragraph", caption, updateId, blocks, warnings);
                return;
            default:
                foreach (var child in element.ChildNodes)
                    AppendBlock(child, updateId, blocks, images, imageIndexes, warnings);
                return;
        }
    }

    private static void AddInlineBlock(string type, IElement element, string updateId,
        List<L12ModianBlockPlan> blocks, HashSet<string> warnings)
    {
        var accumulator = new InlineAccumulator(warnings);
        foreach (var child in element.ChildNodes) accumulator.Append(child);
        var value = accumulator.Finish();
        if (!string.IsNullOrWhiteSpace(value.Text))
            AddTextBlock(type, value.Text, value.Marks, ParseAlign(element), updateId, blocks);
    }

    private static void AddListBlock(IElement element, string updateId, List<L12ModianBlockPlan> blocks,
        HashSet<string> warnings)
    {
        var text = new StringBuilder();
        var marks = new List<L12ModianTextMark>();
        foreach (var item in element.Children.Where(child => child.TagName == "LI"))
        {
            var accumulator = new InlineAccumulator(warnings);
            foreach (var child in item.ChildNodes) accumulator.Append(child);
            var value = accumulator.Finish();
            if (string.IsNullOrWhiteSpace(value.Text)) continue;
            if (text.Length > 0) text.Append('\n');
            var offset = text.Length;
            text.Append(value.Text);
            marks.AddRange(value.Marks.Select(mark => mark with { From = mark.From + offset, To = mark.To + offset }));
        }
        if (text.Length > 0) AddTextBlock(element.TagName == "OL" ? "orderedList" : "bulletList",
            text.ToString(), marks, ParseAlign(element), updateId, blocks);
    }

    private static void AddTextBlock(string type, string text, IReadOnlyList<L12ModianTextMark> marks,
        string align, string updateId, List<L12ModianBlockPlan> blocks)
    {
        var sourceText = CleanText(text);
        var cleaned = L12ModianImportService.ScrubVisibleModianUrl(sourceText);
        if (string.IsNullOrWhiteSpace(cleaned)) return;
        if (cleaned.Length > 20_000) throw new InvalidDataException("单个正文文本块超过 20000 字符");
        // 清除可见来源链接会改变字符偏移；此时宁可移除样式，也不能让旧偏移标到错误正文。
        var validMarks = string.Equals(sourceText, cleaned, StringComparison.Ordinal)
            ? marks.Where(mark => mark.From >= 0 && mark.To > mark.From && mark.To <= cleaned.Length)
                .Take(200).ToArray()
            : [];
        blocks.Add(new(BlockId(updateId, blocks.Count), type, cleaned, validMarks, align));
    }

    private static void AddDescendantImages(IElement element, string updateId,
        List<L12ModianBlockPlan> blocks, List<string> images, Dictionary<string, int> imageIndexes,
        HashSet<string> warnings)
    {
        foreach (var image in element.QuerySelectorAll("img"))
            AddImage(image, updateId, blocks, images, imageIndexes, warnings);
    }

    private static void AddImage(IElement element, string updateId, List<L12ModianBlockPlan> blocks,
        List<string> images, Dictionary<string, int> imageIndexes, HashSet<string> warnings)
    {
        var raw = element.GetAttribute("data-src") ?? element.GetAttribute("src") ?? string.Empty;
        try
        {
            var source = L12ModianImportClient.NormalizeImageSource(raw);
            if (!imageIndexes.TryGetValue(source, out var index))
            {
                index = images.Count;
                images.Add(source);
                imageIndexes[source] = index;
            }
            var alt = L12ModianImportService.ScrubVisibleModianUrl(
                CleanText(element.GetAttribute("alt") ?? string.Empty));
            if (string.IsNullOrWhiteSpace(alt)) alt = $"正文图片 {index + 1}";
            blocks.Add(new(BlockId(updateId, blocks.Count), "image", SourceImageIndex: index,
                Alt: alt[..Math.Min(alt.Length, 180)]));
        }
        catch (InvalidDataException)
        {
            warnings.Add("已忽略不在允许图片主机中的图片");
        }
    }

    private static string ParseAlign(IElement element)
    {
        var align = element.GetAttribute("align")?.Trim().ToLowerInvariant();
        if (align is "left" or "center" or "right" or "justify") return align;
        var style = element.GetAttribute("style") ?? string.Empty;
        var match = Regex.Match(style, "(?i)text-align\\s*:\\s*(left|center|right|justify)");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "left";
    }

    private static string BlockId(string updateId, int index) => $"import-block-{index + 1:D3}";
    private static string CleanText(string value) => Regex.Replace(WebUtility.HtmlDecode(value ?? string.Empty),
        "[\\t\\f\\v ]+", " ").Replace(" \n", "\n").Replace("\n ", "\n").Trim();

    private sealed class InlineAccumulator(HashSet<string> warnings)
    {
        private readonly StringBuilder _text = new();
        private readonly List<L12ModianTextMark> _marks = [];

        public void Append(INode node)
        {
            if (node is IText text) { AppendText(text.Data); return; }
            if (node is not IElement element || ForbiddenElements.Contains(element.TagName)) return;
            if (element.TagName == "BR") { AppendBreak(); return; }
            if (element.TagName == "IMG") return;
            if (element.TagName == "A") warnings.Add("正文链接已转为纯文本");
            var markType = MarkElements.Contains(element.TagName) ? element.TagName switch
            {
                "B" or "STRONG" => "bold", "I" or "EM" => "italic", "U" => "underline",
                _ => "strikethrough",
            } : null;
            var start = _text.Length;
            foreach (var child in element.ChildNodes) Append(child);
            if (markType is not null && _text.Length > start)
                _marks.Add(new(markType, start, _text.Length));
        }

        private void AppendText(string value)
        {
            foreach (var character in WebUtility.HtmlDecode(value ?? string.Empty))
            {
                if (char.IsWhiteSpace(character))
                {
                    if (_text.Length > 0 && _text[^1] is not (' ' or '\n')) _text.Append(' ');
                }
                else _text.Append(character);
            }
        }

        private void AppendBreak()
        {
            while (_text.Length > 0 && _text[^1] == ' ') _text.Length--;
            if (_text.Length > 0 && _text[^1] != '\n') _text.Append('\n');
        }

        public (string Text, IReadOnlyList<L12ModianTextMark> Marks) Finish()
        {
            while (_text.Length > 0 && char.IsWhiteSpace(_text[^1])) _text.Length--;
            return (_text.ToString(), _marks.Where(mark => mark.From < _text.Length)
                .Select(mark => mark with { To = Math.Min(mark.To, _text.Length) }).ToArray());
        }
    }
}

internal static class L12ImportedImageProcessor
{
    internal const long MaxPixels = 24_000_000;
    internal const int MaxDimension = 12_000;

    internal static L12SiteMediaUpload Body(L12ModianRemoteImage source, string alt)
        => Build(source, "article", alt, [(1600, 1600, false), (1280, 1280, false), (480, 480, false)]);

    internal static L12SiteMediaUpload Cover(L12ModianRemoteImage source, string alt)
        => Build(source, "news", alt, [(1600, 900, true), (1280, 720, true), (480, 270, true)]);

    private static L12SiteMediaUpload Build(L12ModianRemoteImage source, string kind, string alt,
        IReadOnlyList<(int Width, int Height, bool Crop)> variants)
    {
        ValidateCompleteContainer(source);
        using var input = SKData.CreateCopy(source.Bytes);
        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("无法识别正文图片");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension
            || (long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("正文图片像素或尺寸超过限制");
        // Skia 对无多帧容器的静态 PNG/JPEG 可能报告 0；只有明确多于一帧才是动画。
        if (codec.FrameCount > 1) throw new InvalidDataException("正文图片不能是动画");
        var decodeInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var decodedResult = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (decodedResult != SKCodecResult.Success)
            throw new InvalidDataException("正文图片解码失败");
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var rendered = variants.Select(variant => Render(oriented, variant.Width, variant.Height, variant.Crop))
            .ToArray();
        var sanitizedOriginal = EncodeWebp(oriented, 90);
        return new(kind, "imported.webp", "image/webp", sanitizedOriginal, rendered[0], rendered[1],
            rendered[2], alt[..Math.Min(alt.Length, 180)], .5, .5);
    }

    private static void ValidateCompleteContainer(L12ModianRemoteImage source)
    {
        var bytes = source.Bytes.AsSpan();
        var complete = source.ContentType switch
        {
            "image/jpeg" => bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8
                && bytes[^2] == 0xff && bytes[^1] == 0xd9,
            "image/png" => bytes.Length >= 20
                && bytes[^12..^8].SequenceEqual(new byte[] { 0, 0, 0, 0 })
                && bytes[^8..^4].SequenceEqual("IEND"u8),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes[8..12].SequenceEqual("WEBP"u8)
                && (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) + 8 == bytes.Length,
            _ => false,
        };
        if (!complete) throw new InvalidDataException("正文图片容器不完整或格式不受支持");
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, source.Width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, source.Width, 0, -1, source.Height),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, source.Height),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, source.Height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, source.Height, -1, 0, source.Width),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, source.Width),
            _ => Matrix(1, 0, 0, 0, 1, 0),
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return output;
    }

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY,
        float transY) => new()
    {
        ScaleX = scaleX, SkewX = skewX, TransX = transX,
        SkewY = skewY, ScaleY = scaleY, TransY = transY, Persp2 = 1,
    };

    private static byte[] Render(SKBitmap source, int width, int height, bool crop)
    {
        var targetWidth = width;
        var targetHeight = height;
        SKRect sourceRect;
        if (crop)
        {
            var sourceRatio = source.Width / (double)source.Height;
            var targetRatio = width / (double)height;
            if (sourceRatio > targetRatio)
            {
                var cropWidth = (float)(source.Height * targetRatio);
                sourceRect = new SKRect((source.Width - cropWidth) / 2f, 0,
                    (source.Width + cropWidth) / 2f, source.Height);
            }
            else
            {
                var cropHeight = (float)(source.Width / targetRatio);
                sourceRect = new SKRect(0, (source.Height - cropHeight) / 2f,
                    source.Width, (source.Height + cropHeight) / 2f);
            }
        }
        else
        {
            var ratio = Math.Min(1d, Math.Min(width / (double)source.Width, height / (double)source.Height));
            targetWidth = Math.Max(1, (int)Math.Round(source.Width * ratio));
            targetHeight = Math.Max(1, (int)Math.Round(source.Height * ratio));
            sourceRect = new SKRect(0, 0, source.Width, source.Height);
        }
        using var output = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            using var sourceImage = SKImage.FromBitmap(source);
            canvas.DrawImage(sourceImage, sourceRect, new SKRect(0, 0, targetWidth, targetHeight),
                new SKSamplingOptions(SKCubicResampler.Mitchell), null);
            canvas.Flush();
        }
        return EncodeWebp(output, 82);
    }

    private static byte[] EncodeWebp(SKBitmap source, int quality)
    {
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality)
            ?? throw new InvalidDataException("正文图片 WebP 编码失败");
        return data.ToArray();
    }
}
