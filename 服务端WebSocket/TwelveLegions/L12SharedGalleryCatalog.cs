using System.Collections.Frozen;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

// Only public appearance definitions cross this boundary. Accounts, grants, rules and audit
// remain in the test platform store. There is deliberately no disk cache or configurable URL.
public sealed class L12SharedGalleryCatalog : IAsyncDisposable
{
    internal const string EnvironmentKey = "L12_TESTRUN_SHARED_GALLERY";
    internal const string EnvironmentValue = "production-public-v1";
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal const int MaximumEntries = 4096;
    internal static readonly Uri Endpoint = new("http://127.0.0.1:8083/api/alternate-arts");
    internal static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex MediaPath = new(
        @"\A/api/site/media/[A-Za-z0-9_-]{1,160}/(desktop|thumbnail)/[a-fA-F0-9]{64}\.webp\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CardImageId = new(@"\A[A-Za-z0-9_-]{1,160}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly HttpClient _client;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Snapshot? _snapshot;
    private Task? _worker;
    private int _started;
    private int _stopping;

    internal sealed record Snapshot(IReadOnlyList<L12AlternateArtView> Arts,
        FrozenDictionary<string, L12AlternateArtView> ById, DateTimeOffset LoadedAt, long LoadedTimestamp);

    internal L12SharedGalleryCatalog(HttpMessageHandler handler, TimeProvider? clock = null)
    {
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _clock = clock ?? TimeProvider.System;
    }

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(1),
        MaxResponseHeadersLength = 16,
        MaxConnectionsPerServer = 1,
    };

    // Validate before creating runtime directories or opening any platform/match database.
    internal static bool EnabledForConfiguration(string? marker, string? storageProfile, string? publicBaseUrl, int port)
    {
        if (string.IsNullOrEmpty(marker)) return false;
        if (marker != EnvironmentValue || storageProfile != L12TestRunStorageProfile.EphemeralValue || port != 8084
            || !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "legion-12.com" || !uri.IsDefaultPort
            || uri.AbsolutePath != "/testrun" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new InvalidOperationException("共享图库仅允许固定标记与隔离测试服配置，拒绝用于正式服");
        return true;
    }

    internal static L12SharedGalleryCatalog? Create(bool enabled, bool isolatedTestRun)
        => !enabled ? null : isolatedTestRun ? new(CreateHandler())
            : throw new InvalidOperationException("共享图库要求已验证的隔离测试存储");

    // Readers only observe a complete, immutable, fresh snapshot. A failed refresh clears it;
    // local definitions are never a fallback, even during startup or an upstream outage.
    internal Snapshot? Current
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            return Volatile.Read(ref _stopping) == 0 && snapshot is not null
                && _clock.GetElapsedTime(snapshot.LoadedTimestamp) <= MaximumAge ? snapshot : null;
        }
    }

    internal Snapshot RequireCurrent() => Current
        ?? throw new L12PlatformStorageUnavailableException("共享图库暂不可用，请稍后重试；不会使用测试服旧资料");

    internal async Task StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("共享图库已启动");
        await RefreshAsync();
        if (Volatile.Read(ref _stopping) == 0) _worker = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(_stop.Token)) await RefreshAsync(_stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    internal async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _stopping) != 0) return false;
        if (!await _refresh.WaitAsync(0, cancellationToken)) return false;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            budget.CancelAfter(FetchTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Accept.ParseAdd("application/json");
            // No user request, credential, cookie, or forwarding header enters this client.
            using var response = await SendBoundedAsync(request, budget.Token);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentType?.MediaType != "application/json"
                || response.Content.Headers.ContentEncoding.Count != 0
                || response.Content.Headers.ContentLength > MaximumBytes)
                throw new InvalidDataException("共享图库响应类型或大小不合法");
            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, budget.Token).AsTask().WaitAsync(budget.Token);
                if (read == 0) break;
                if (buffer.Length + read > MaximumBytes) throw new InvalidDataException("共享图库响应过大");
                buffer.Write(chunk, 0, read);
            }
            var arts = Parse(buffer.ToArray());
            budget.Token.ThrowIfCancellationRequested();
            var next = new Snapshot(arts, arts.ToFrozenDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase),
                _clock.GetUtcNow(), _clock.GetTimestamp());
            if (Volatile.Read(ref _stopping) != 0) return false;
            Volatile.Write(ref _snapshot, next);
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException
            or OperationCanceledException or ArgumentException or InvalidOperationException)
        {
            Volatile.Write(ref _snapshot, null);
            return false;
        }
        finally { _refresh.Release(); }
    }

    private async Task<HttpResponseMessage> SendBoundedAsync(HttpRequestMessage request, CancellationToken token)
    {
        var pending = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        try { return await pending.WaitAsync(token); }
        catch
        {
            // Even an injected/non-cooperative handler cannot extend startup. Dispose any late
            // response and observe its failure instead of leaving transport resources unowned.
            _ = pending.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    internal static IReadOnlyList<L12AlternateArtView> Parse(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("共享图库响应过大");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > MaximumEntries)
            throw new InvalidDataException("共享图库条目数量或结构不合法");
        var result = new List<L12AlternateArtView>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.EnumerateArray())
        {
            ValidateObject(element);
            // These fields are required, not deserializer defaults for an incomplete document.
            foreach (var field in new[] { "id", "artCode", "baseCardId", "displayName", "mediaAssetId",
                         "imageUrl", "thumbnailUrl", "cardImageId", "kind", "baseCardName", "effect", "artist" })
                if (!element.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("共享图库缺少字符串字段");
            foreach (var field in new[] { "active", "builtIn" })
                if (!element.TryGetProperty(field, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("共享图库缺少状态字段");
            foreach (var field in new[] { "createdAt", "updatedAt" })
                if (!element.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String
                    || !value.TryGetDateTimeOffset(out _)) throw new InvalidDataException("共享图库缺少时间字段");
            var art = element.Deserialize<L12AlternateArtView>(JsonOptions)
                ?? throw new InvalidDataException("共享图库条目为空");
            Text(art.Id, 160, true); Text(art.ArtCode, 80, true); Text(art.BaseCardId, 100);
            Text(art.DisplayName, 200, true); Text(art.MediaAssetId, 160); Text(art.CardImageId, 160);
            Text(art.BaseCardName, 200); Text(art.Effect, 4000, multiline: true); Text(art.Artist, 120); Text(art.Collaboration, 120);
            Text(art.ProductId, 200); Text(art.ProductName, 200);
            if (art.CardImageId != "" && !CardImageId.IsMatch(art.CardImageId))
                throw new InvalidDataException("共享图库卡图身份不合法");
            if (!ids.Add(art.Id) || art.GrantedAt is not null || !string.IsNullOrEmpty(art.GrantReason)
                || art.Kind is not ("alternate-art" or "gallery-card")
                || (art.Kind == "alternate-art" && string.IsNullOrWhiteSpace(art.BaseCardId))
                || (art.Kind == "gallery-card" && !string.IsNullOrEmpty(art.BaseCardId)))
                throw new InvalidDataException("共享图库身份或公开字段不合法");
            if (!string.IsNullOrEmpty(art.MediaAssetId))
            {
                if (!CardImageId.IsMatch(art.MediaAssetId)
                    || !MediaPath.IsMatch(art.ImageUrl) || !MediaPath.IsMatch(art.ThumbnailUrl)
                    || !art.ImageUrl.StartsWith($"/api/site/media/{art.MediaAssetId}/desktop/", StringComparison.Ordinal)
                    || !art.ThumbnailUrl.StartsWith($"/api/site/media/{art.MediaAssetId}/thumbnail/", StringComparison.Ordinal))
                    throw new InvalidDataException("共享图库媒体路径不合法");
            }
            else if (!art.BuiltIn || !CardImageId.IsMatch(art.CardImageId)
                || art.ImageUrl != "" || art.ThumbnailUrl != "")
                throw new InvalidDataException("共享图库内置图片身份不合法");
            var products = art.Products ?? [];
            var rarities = art.Rarities ?? [];
            if (products.Count > 64 || rarities.Count > 32) throw new InvalidDataException("共享图库元数据数量不合法");
            foreach (var product in products)
            {
                if (product is null) throw new InvalidDataException("共享图库产品为空");
                Text(product.Id, 200, true); Text(product.Name, 200, true); Text(product.CategoryId, 160);
                Text(product.CategoryName, 160); Text(product.Source, 40); Text(product.Status, 40);
                if (product.ReleaseOrder is < 0 or > 100_000) throw new InvalidDataException("共享图库产品顺序不合法");
            }
            foreach (var rarity in rarities)
            {
                if (rarity is null) throw new InvalidDataException("共享图库罕贵为空");
                Text(rarity.Id, 160, true); Text(rarity.Code, 40, true); Text(rarity.Name, 80, true);
            }
            var information = art.CardInformation is { } details
                ? details with { Traits = details.Traits is { } traits ? Array.AsReadOnly(traits.ToArray()) : null }
                : null;
            result.Add(art with { Products = Array.AsReadOnly(products.ToArray()),
                Rarities = Array.AsReadOnly(rarities.ToArray()), CardInformation = information });
        }
        return result.AsReadOnly();
    }

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("共享图库含重复字段");
                ValidateObject(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateObject(item);
        else if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 4000)
            throw new InvalidDataException("共享图库字段过长");
    }

    private static void Text(string? value, int max, bool required = false, bool multiline = false)
    {
        if (value is null || value.Length > max || (required && string.IsNullOrWhiteSpace(value))
            || value.Any(c => char.IsControl(c) && (!multiline || c is not ('\r' or '\n' or '\t'))))
            throw new InvalidDataException("共享图库文本字段不合法");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        Volatile.Write(ref _snapshot, null);
        var cancellation = _stop.CancelAsync();
        _client.Dispose();
        try { await Task.WhenAll(cancellation, _worker ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }
        // A timed-out transport may still be unwinding. Do not dispose its semaphore/CTS.
    }
}
