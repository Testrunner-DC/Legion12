using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class ControlPlaneModianImportTests
{
    private const string BodyImage = "https://p6.moimg.net/path/dst_project/1000154630/body.png";
    private const string LogoImage = "https://p6.moimg.net/path/dst_project/1000154630/logo.png";
    private const string ProjectCover = "https://p6.moimg.net/path/dst_project/1000154630/project.png";

    [Fact]
    public void RealJsonpWrapperAndThirtyThreeUpdateItemsAreParsedStrictly()
    {
        var html = new StringBuilder("<div class='tack-wrap'><ul class='tack-lists'>");
        for (var sequence = 33; sequence >= 1; sequence--)
        {
            html.Append($"<li class='tack-gitem' data-pro_id='157664' data-update_id='{214806 + sequence}'>")
                .Append($"<div class='tack-gtop'><span>第{sequence}次更新</span><span>2026-06-06 21:53</span></div>")
                .Append($"<a><h4 class='update-title'>更新 {sequence}</h4></a></li>");
        }
        html.Append("<li class='tack-sys'>系统更新 2026-06-05 20:00</li></ul></div>");
        var json = JsonSerializer.Serialize(new { status = "SUCCESS", data = new { html = html.ToString() } });
        var wrapped = $"window[decodeURIComponent('l12probe')]({json});";

        var payload = L12ModianImportClient.ExtractJsonpPayload(wrapped, "l12probe");
        using var document = JsonDocument.Parse(payload);
        var parsed = L12ModianImportClient.ParseUpdateListHtml(
            document.RootElement.GetProperty("data").GetProperty("html").GetString()!);

        Assert.Equal(33, parsed.Count);
        Assert.Equal(33, parsed.Max(item => item.Sequence));
        Assert.Equal("更新 33", parsed.Single(item => item.Sequence == 33).Title);
        Assert.Equal(json, L12ModianImportClient.ExtractJsonpPayload($"l12probe({json})", "l12probe"));
        Assert.Throws<InvalidDataException>(() =>
            L12ModianImportClient.ExtractJsonpPayload($"window[decodeURIComponent('other')]({json});", "l12probe"));
        Assert.Throws<InvalidDataException>(() =>
            L12ModianImportClient.ExtractJsonpPayload($"prefix l12probe({json})", "l12probe"));

        var realFixture = FindFromAncestors(Path.Combine(".runtime", "modian-updates-157664.jsonp"));
        if (realFixture is not null)
        {
            var realPayload = L12ModianImportClient.ExtractJsonpPayload(File.ReadAllText(realFixture), "l12probe");
            using var realDocument = JsonDocument.Parse(realPayload);
            var realItems = L12ModianImportClient.ParseUpdateListHtml(
                realDocument.RootElement.GetProperty("data").GetProperty("html").GetString()!);
            Assert.Equal(33, realItems.Count);
            Assert.Equal("221772", realItems.MaxBy(item => item.Sequence)!.UpdateId);
        }
    }

    [Fact]
    public void ImageUrlAllowlistRequiresFixedHostPortAndProjectOwnerPath()
    {
        Assert.Equal(BodyImage, L12ModianImportClient.NormalizeImageSource(BodyImage));
        Assert.Equal(BodyImage, L12ModianImportClient.NormalizeImageSource("//p6.moimg.net/path/dst_project/1000154630/body.png"));
        Assert.Throws<InvalidDataException>(() => L12ModianImportClient.NormalizeImageSource(
            "https://p10.moimg.net/path/dst_project/1000154630/body.png"));
        Assert.Throws<InvalidDataException>(() => L12ModianImportClient.NormalizeImageSource(
            "https://p6.moimg.net:444/path/dst_project/1000154630/body.png"));
        Assert.Throws<InvalidDataException>(() => L12ModianImportClient.NormalizeImageSource(
            "https://p6.moimg.net/path/dst_avatar/1000154630/avatar.png"));
        Assert.Throws<InvalidDataException>(() => L12ModianImportClient.NormalizeImageSource(
            "https://p6.moimg.net/path/dst_project/9999999999/body.png"));
        Assert.Throws<InvalidDataException>(() => L12ModianImportClient.NormalizeImageSource(
            "https://p6.moimg.net/qrcode/1000154630/code.png"));
        Assert.False(L12ModianImportClient.IsPublicAddress(IPAddress.Loopback));
        Assert.False(L12ModianImportClient.IsPublicAddress(IPAddress.Parse("169.254.1.2")));
        Assert.False(L12ModianImportClient.IsPublicAddress(IPAddress.Parse("192.168.1.2")));
        Assert.True(L12ModianImportClient.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void HtmlConversionStripsActiveContentAndEveryVisibleModianLink()
    {
        var parsed = L12ModianHtmlConverter.Convert("221772", "标题",
            "<h1>主标题</h1><p>正文 <strong>加粗</strong></p><p><a href='javascript:alert(1)'>"
            + "https://zhongchou.modian.com/item/157664.html</a></p><script>alert(1)</script>"
            + $"<p><img src='{BodyImage}' onerror='alert(2)' alt='卡图'></p>"
            + $"<blockquote>引用<img src='{LogoImage}' alt='引用图'></blockquote>"
            + $"<ul><li>列表<img src='{ProjectCover}' alt='列表图'></li></ul><iframe src='x'></iframe>");
        var serialized = JsonSerializer.Serialize(parsed);

        Assert.Contains(parsed.Blocks, block => block.Type == "h2" && block.Text == "主标题");
        Assert.Contains(parsed.Blocks, block => block.Type == "paragraph" && block.Text.Contains("正文"));
        Assert.Contains(parsed.Blocks.SelectMany(block => block.Marks ?? []), mark => mark.Type == "bold");
        Assert.Equal(3, parsed.Images.Count);
        Assert.Equal(3, parsed.Blocks.Count(block => block.Type == "image"));
        Assert.DoesNotContain("modian.com", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("正文链接已转为纯文本", parsed.Warnings);
        Assert.Contains("已移除不支持的主动内容", parsed.Warnings);
    }

    [Fact]
    public void SkiaPipelineProducesSanitizedWebpAndExactCoverSizesAndRejectsTruncation()
    {
        var png = Png(800, 400, SKColors.RoyalBlue);
        var source = new L12ModianRemoteImage("image/png", png);
        var body = L12ImportedImageProcessor.Body(source, "正文图");
        var cover = L12ImportedImageProcessor.Cover(source, "封面图");

        Assert.Equal("image/webp", body.OriginalContentType);
        Assert.Equal((800, 400), WebpSize(body.DesktopWebp));
        Assert.Equal((800, 400), WebpSize(body.MobileWebp));
        Assert.Equal((480, 240), WebpSize(body.ThumbnailWebp));
        Assert.Equal((1600, 900), WebpSize(cover.DesktopWebp));
        Assert.Equal((1280, 720), WebpSize(cover.MobileWebp));
        Assert.Equal((480, 270), WebpSize(cover.ThumbnailWebp));
        Assert.Throws<InvalidDataException>(() => L12ImportedImageProcessor.Body(
            new L12ModianRemoteImage("image/png", png[..^8]), "截断图片"));
        var jpeg = Jpeg(800, 400, SKColors.IndianRed);
        Assert.Throws<InvalidDataException>(() => L12ImportedImageProcessor.Body(
            new L12ModianRemoteImage("image/jpeg", jpeg[..^2]), "截断图片"));
    }

    [Fact]
    public async Task ImportIsDraftOnlyIdempotentDetectsChangesAndPreservesPublishedSnapshot()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("news").First(item => item.Active);
            var fake = FakeClient.Single(DefaultDetail());
            using var service = new L12ModianImportService(store, fake);
            var request = Request(category.Id);

            var created = await service.ImportAsync(admin, request, null, CancellationToken.None);
            Assert.Equal(1, created.Created);
            var article = Assert.Single(store.AdminArticles(kind: "news"));
            Assert.Equal("draft", article.Status);
            Assert.Empty(store.PublicArticles(kind: "news"));
            Assert.Empty(article.Link);
            Assert.NotEmpty(article.CoverUrl);
            Assert.Single(article.BodyMedia!);
            Assert.DoesNotContain("modian.com", JsonSerializer.Serialize(article), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("221772", article.Body, StringComparison.Ordinal);
            Assert.Equal(2, store.AdminSiteMedia().Count);
            Assert.Contains(LogoImage, fake.Downloads);
            Assert.DoesNotContain(ProjectCover, fake.Downloads);

            var replay = await service.ImportAsync(admin, request, null, CancellationToken.None);
            Assert.Equal(1, replay.Unchanged);
            Assert.Equal(2, store.AdminSiteMedia().Count);

            var published = store.PublishArticle(admin, article.Id);
            var publicSnapshot = Assert.Single(store.PublicArticles(kind: "news"));
            fake.Details["221772"] = DefaultDetail() with
            {
                Title = "远端新标题",
                Html = $"<h2>远端更新</h2><p>新的正文</p><img src='{BodyImage}' alt='新图'>",
            };
            var preview = await service.PreviewAsync(CancellationToken.None);
            Assert.Equal("remote-changed", Assert.Single(preview.Items).State);
            var guarded = await service.ImportAsync(admin, request, null, CancellationToken.None);
            Assert.Equal(1, guarded.NeedsReimport);

            var current = store.AdminArticle(article.Id)!;
            store.SaveArticleDraft(admin, new L12ArticleDraft(current.Id, "本地编辑", current.Summary, current.Body,
                current.Category, string.Empty, string.Empty, current.Slug, current.Pinned, current.PublishAt,
                current.Revision, "news", current.CategoryId, current.MediaAssetId));
            var conflict = await service.ImportAsync(admin, request with { ReimportUpdateIds = ["221772"] }, null,
                CancellationToken.None);
            Assert.Equal(1, conflict.Conflicted);

            var updated = await service.ImportAsync(admin, request with
            {
                ReimportUpdateIds = ["221772"], OverwriteLocalChangesIds = ["221772"],
            }, null, CancellationToken.None);
            Assert.Equal(1, updated.Updated);
            var draft = store.AdminArticle(article.Id)!;
            Assert.Equal("远端新标题", draft.Title);
            Assert.True(draft.HasUnpublishedChanges);
            Assert.Equal(published.Status, draft.Status);
            var stillPublic = Assert.Single(store.PublicArticles(kind: "news"));
            Assert.Equal(publicSnapshot.Title, stillPublic.Title);
            Assert.Equal(publicSnapshot.Body, stillPublic.Body);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CoverFallsBackFromMissingLogoToBodyThenFixedProjectCoverAndNeverCreatesCoverlessDraft()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("news").First(item => item.Active);
            var bodyFallback = DefaultDetail() with { UpdateId = "221773", LogoUrl = null, ProjectCoverUrl = ProjectCover };
            var projectFallback = DefaultDetail() with
            {
                UpdateId = "221774", LogoUrl = null, Html = "<h2>无正文图</h2><p>文字正文</p>",
                ProjectCoverUrl = ProjectCover,
            };
            var noCover = DefaultDetail() with
            {
                UpdateId = "221775", LogoUrl = null, Html = "<h2>无图</h2><p>不能生成完整草稿</p>",
                ProjectCoverUrl = null,
            };
            var fake = new FakeClient([Summary(bodyFallback), Summary(projectFallback), Summary(noCover)],
                new[] { bodyFallback, projectFallback, noCover });
            using var service = new L12ModianImportService(store, fake);
            var result = await service.ImportAsync(admin, new(["221773", "221774", "221775"], category.Id),
                null, CancellationToken.None);

            Assert.Equal(2, result.Created);
            Assert.Equal(1, result.Failed);
            Assert.All(store.AdminArticles(kind: "news"), article => Assert.NotEmpty(article.CoverUrl));
            Assert.Equal(1, fake.Downloads.Count(value => value == ProjectCover));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TransactionFailureRestoresMediaRowsAndRemovesOnlyNewFiles()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("news").First(item => item.Active);
            var image = new L12ModianRemoteImage("image/png", Png(320, 180, SKColors.OrangeRed));
            var bodyUpload = L12ImportedImageProcessor.Body(image, "正文图");
            var coverUpload = L12ImportedImageProcessor.Cover(image, "封面图");
            var invalid = new L12ModianPreparedDraft("221772", "标题", "摘要",
                new DateTimeOffset(2026, 9, 5, 11, 6, 0, TimeSpan.FromHours(8)), new string('a', 64),
                [new("import-block-001", "image", SourceImageIndex: 99, Alt: "正文图")], [bodyUpload],
                coverUpload, []);

            Assert.Throws<ArgumentException>(() => store.ApplyModianDraft(admin, category.Id, invalid, false,
                false));
            Assert.Empty(store.AdminArticles(kind: "news"));
            Assert.Empty(store.AdminSiteMedia());
            var mediaRoot = Path.Combine(root, "site-media");
            Assert.True(!Directory.Exists(mediaRoot) || !Directory.EnumerateFiles(mediaRoot).Any());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpEndpointsRequireDraftPermissionAndNeverPublish()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var category = store.AdminSiteCategories("news").First(item => item.Active);
            var fake = FakeClient.Single(DefaultDetail());
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog,
                modianImportClient: fake);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var player = store.Register("tmodiac343e", "password-123");
            var admin = store.Login("Admin", "L12master");

            using (var playerPreview = Authorized(HttpMethod.Get, "/api/admin/articles/modian/preview", player.Token!))
            using (var response = await client.SendAsync(playerPreview))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var playerImport = Authorized(HttpMethod.Post, "/api/admin/articles/modian/import", player.Token!,
                       new { updateIds = new[] { "221772" }, categoryId = category.Id }))
            using (var response = await client.SendAsync(playerImport))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var adminPreview = Authorized(HttpMethod.Get, "/api/admin/articles/modian/preview", admin.Token!))
            using (var response = await client.SendAsync(adminPreview))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var adminImport = Authorized(HttpMethod.Post, "/api/admin/articles/modian/import", admin.Token!,
                       new { updateIds = new[] { "221772" }, categoryId = category.Id }))
            using (var response = await client.SendAsync(adminImport))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<L12ModianImportBatchResult>();
                Assert.Equal(1, result!.Created);
            }
            Assert.Empty(store.PublicArticles(kind: "news"));
            Assert.Equal("draft", Assert.Single(store.AdminArticles(kind: "news")).Status);
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static L12ModianImportRequest Request(string categoryId) => new(["221772"], categoryId);

    private static L12ModianSourceDetail DefaultDetail() => new("221772", "157664", "1000154630",
        "After更新日志01-那些联动者们（上）", new DateTimeOffset(2026, 9, 5, 11, 6, 0, TimeSpan.FromHours(8)),
        $"<h2>更新标题</h2><p>正文 <strong>重点</strong> <a href='https://zhongchou.modian.com/item/157664.html'>https://zhongchou.modian.com/item/157664.html</a></p><img src='{BodyImage}' alt='卡牌图'>",
        LogoImage, ProjectCover);

    private static L12ModianSourceSummary Summary(L12ModianSourceDetail detail)
        => new(detail.UpdateId, int.Parse(detail.UpdateId) - 221740, detail.Title, detail.OriginalPublishedAt);

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-modian-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string? FindFromAncestors(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static byte[] Png(int width, int height, SKColor color)
        => EncodedImage(width, height, color, SKEncodedImageFormat.Png, 100);

    private static byte[] Jpeg(int width, int height, SKColor color)
        => EncodedImage(width, height, color, SKEncodedImageFormat.Jpeg, 90);

    private static byte[] EncodedImage(int width, int height, SKColor color, SKEncodedImageFormat format,
        int quality)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(color); canvas.Flush(); }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static (int Width, int Height) WebpSize(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return (codec.Info.Width, codec.Info.Height);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class FakeClient : IL12ModianImportClient
    {
        private readonly IReadOnlyList<L12ModianSourceSummary> _summaries;
        public Dictionary<string, L12ModianSourceDetail> Details { get; }
        public List<string> Downloads { get; } = [];

        public FakeClient(IReadOnlyList<L12ModianSourceSummary> summaries,
            IEnumerable<L12ModianSourceDetail> details)
        {
            _summaries = summaries;
            Details = details.ToDictionary(item => item.UpdateId, StringComparer.Ordinal);
        }

        public static FakeClient Single(L12ModianSourceDetail detail) => new([Summary(detail)], [detail]);
        public Task<IReadOnlyList<L12ModianSourceSummary>> ListUpdatesAsync(CancellationToken cancellationToken)
            => Task.FromResult(_summaries);
        public Task<L12ModianSourceDetail> GetUpdateAsync(string updateId, CancellationToken cancellationToken)
            => Task.FromResult(Details[updateId]);
        public Task<L12ModianRemoteImage> DownloadImageAsync(string source, CancellationToken cancellationToken)
        {
            Downloads.Add(source);
            var color = source == LogoImage ? SKColors.IndianRed
                : source == ProjectCover ? SKColors.ForestGreen : SKColors.RoyalBlue;
            return Task.FromResult(new L12ModianRemoteImage("image/png", Png(640, 360, color)));
        }
        public void Dispose() { }
    }
}
