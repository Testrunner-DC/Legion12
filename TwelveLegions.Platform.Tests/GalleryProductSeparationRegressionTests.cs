using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class GalleryProductSeparationRegressionTests
{
    [Fact]
    public async Task GalleryCreationAndMergeNeverChangeMerchandise()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var article = CreateArticle(f.Store, actor, "独立商品");
        var before = JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product"));
        Assert.DoesNotContain(f.Store.AlternateArtProducts(true), row => row.Id == "site:" + article.Id);
        var first = f.Store.SaveAlternateArtProduct(actor, new(null, "画廊资料甲"));
        var second = f.Store.SaveAlternateArtProduct(actor, new(null, "画廊资料乙"));
        var result = f.Store.MergeAlternateArtProducts(actor, new(first.Id, [second.Id]));
        Assert.Equal(0, result.ArchivedSiteProducts);
        Assert.Equal(before, JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product")));
        Assert.Contains(f.Store.AlternateArtProducts(), row => row.Id == first.Id);
        Assert.DoesNotContain(f.Store.AlternateArtProducts(true), row => row.Id == second.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalReferencesDetachOnceAndSurviveRestartWithoutChangingArticles(bool existingTarget)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var article = CreateArticle(f.Store, actor, "历史商品名");
        var unrelated = CreateArticle(f.Store, actor, "未关联商品");
        var sourceId = "site:" + article.Id;
        var targetId = "legacy:gallery-site-" + article.Id;
        var art = f.Store.AlternateArts().First(row => row.BuiltIn);
        AddRow(f.Store, "AlternateArtMetadata", new { AlternateArtId = art.Id, ProductIds = new[] { sourceId }, RarityIds = Array.Empty<string>() });
        AddRow(f.Store, "AlternateArtProductPreferences", new { ProductId = sourceId, DisplayName = "联动", ActiveOverride = true, ShowInGalleryFilter = true, ReleaseOrder = 1 });
        if (existingTarget)
        {
            AddRow(f.Store, "AlternateArtProducts", new { Id = "gallery-site-" + article.Id, Name = "保留的画廊名称", Active = true });
            AddRow(f.Store, "AlternateArtProductPreferences", new { ProductId = targetId, ShowInGalleryFilter = false, ReleaseOrder = 9 });
        }
        f.Store.SaveAlternateArtProduct(actor, new(null, "持久化旧格式合成夹具"));
        var articles = JsonSerializer.Serialize(f.Store.AdminArticles(kind: "product"));
        var reopened = Reopen(f.MirrorPath);
        var product = Assert.Single(reopened.AlternateArts().Single(row => row.Id == art.Id).Products!);
        Assert.Equal(targetId, product.Id);
        Assert.Equal(existingTarget ? "保留的画廊名称" : "联动", product.Name);
        Assert.Equal(existingTarget ? 9 : 1, product.ReleaseOrder);
        Assert.Equal(!existingTarget, product.ShowInGalleryFilter);
        Assert.DoesNotContain(reopened.AlternateArtProducts(true), row => row.Id == "site:" + unrelated.Id);
        Assert.Equal(articles, JsonSerializer.Serialize(reopened.AdminArticles(kind: "product")));
        var snapshot = JsonSerializer.Serialize(reopened.GalleryPresentationCatalog());
        var persisted = File.ReadAllBytes(f.MirrorPath);
        for (var i = 0; i < 3; i++)
        {
            reopened = Reopen(f.MirrorPath);
            Assert.Equal(snapshot, JsonSerializer.Serialize(reopened.GalleryPresentationCatalog()));
            Assert.Equal(articles, JsonSerializer.Serialize(reopened.AdminArticles(kind: "product")));
            Assert.Equal(persisted, File.ReadAllBytes(f.MirrorPath));
            Assert.Single(Rows(reopened, "AlternateArtProductMerges").Cast<object>().Where(row =>
                (string?)row.GetType().GetProperty("SourceProductId")!.GetValue(row) == sourceId));
        }
        reopened.ChangeArticleStatus(actor, article.Id, "archive");
        Assert.Equal(snapshot, JsonSerializer.Serialize(reopened.GalleryPresentationCatalog()));
        Assert.Equal(snapshot, JsonSerializer.Serialize(Reopen(f.MirrorPath).GalleryPresentationCatalog()));
        var renamed = reopened.SaveAlternateArtProduct(actor, new(targetId, "独立资料新名称", false));
        Assert.Equal("独立资料新名称", renamed.Name);
        Assert.False(renamed.Active);
        Assert.DoesNotContain(reopened.AlternateArtProducts(), row => row.Id == targetId);
        reopened.SaveAlternateArtProduct(actor, new(targetId, "再次改名", true));
        var edited = Reopen(f.MirrorPath).AlternateArtProducts().Single(row => row.Id == targetId);
        Assert.Equal("再次改名", edited.Name);
        Assert.True(edited.Active);
        Assert.Equal(existingTarget ? 9 : 1, edited.ReleaseOrder);
    }

    [Fact]
    public async Task ExistingMergedSourcesRemainMergedAndPureReadsDoNotPersist()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.AuthenticateTokenSession(f.AdminToken)!.Account;
        var article = CreateArticle(f.Store, actor, "已合并的商品");
        var target = f.Store.SaveAlternateArtProduct(actor, new(null, "原合并目标"));
        var art = f.Store.AlternateArts().First(row => row.BuiltIn);
        AddRow(f.Store, "AlternateArtMetadata", new { AlternateArtId = art.Id, ProductIds = new[] { "site:" + article.Id }, RarityIds = Array.Empty<string>() });
        AddRow(f.Store, "AlternateArtProductMerges", new { SourceProductId = "site:" + article.Id, TargetProductId = target.Id });
        f.Store.SaveAlternateArtProduct(actor, new(target.Id, target.Name));
        var reopened = Reopen(f.MirrorPath);
        var before = File.ReadAllBytes(f.MirrorPath);
        Assert.Equal(target.Id, Assert.Single(reopened.AlternateArts().Single(row => row.Id == art.Id).Products!).Id);
        for (var i = 0; i < 3; i++) { reopened.AlternateArts(); reopened.AlternateArtProducts(); reopened.GalleryPresentationCatalog(); }
        Assert.Equal(before, File.ReadAllBytes(f.MirrorPath));
        Assert.DoesNotContain(reopened.AlternateArtProducts(true), row => row.Id == "legacy:gallery-site-" + article.Id);
        Assert.Equal("draft", reopened.AdminArticle(article.Id)!.Status);
    }

    private static L12ArticleView CreateArticle(L12PlatformStore store, L12AccountView actor, string name)
        => store.SaveArticleDraft(actor, new(null, name, "", "原商品正文", "", "", "", "", false, null,
            Kind: "product", CategoryId: store.AdminSiteCategories("product").First(row => row.Active).Id));

    private static L12PlatformStore Reopen(string path)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        return new(path, catalog.PresetDecks, officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
    }

    private static IList Rows(L12PlatformStore store, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var data = typeof(L12PlatformStore).GetProperty("_data", flags)?.GetValue(store)
            ?? typeof(L12PlatformStore).GetField("_data", flags)!.GetValue(store)!;
        return (IList)data.GetType().GetProperty(name)!.GetValue(data)!;
    }
    private static void AddRow(L12PlatformStore store, string name, object row)
    {
        var rows = Rows(store, name);
        rows.Add(JsonSerializer.Deserialize(JsonSerializer.Serialize(row), rows.GetType().GetGenericArguments()[0])!);
    }
}
