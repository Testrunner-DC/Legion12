using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMIServer.Tests;

public sealed class SiteContentPlatformStoreTests
{
    [Fact]
    public void FailureBeforeMediaSaveRestoresTheWholeAuditWindowAfterTrimming()
    {
        WithUploadStore((store, admin, root) =>
        {
            static System.Collections.IList Rows(L12PlatformStore value)
            {
                var state = typeof(L12PlatformStore).GetField("_state",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(value)!;
                return (System.Collections.IList)state.GetType().GetProperty("AdminAudit")!.GetValue(state)!;
            }
            static string[] Ids(L12PlatformStore value) => Rows(value).Cast<object>()
                .Select(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)!).Order().ToArray();
            var rows = Rows(store);
            var rowType = rows.GetType().GetGenericArguments()[0];
            rows.Clear();
            for (var index = 0; index < 5000; index++)
            {
                var row = Activator.CreateInstance(rowType)!;
                rowType.GetProperty("ActorId")!.SetValue(row, admin.Id);
                rowType.GetProperty("ActorName")!.SetValue(row, admin.Username);
                rowType.GetProperty("Category")!.SetValue(row, "upload-audit-window");
                rowType.GetProperty("Action")!.SetValue(row, "seed");
                rowType.GetProperty("Target")!.SetValue(row, "original-" + index);
                rows.Add(row);
            }
            store.UpdateAudioPreferences(admin.Id, new());
            var before = Ids(store);
            Assert.Equal(5000, before.Length);
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-site-media-save") throw new IOException("abort after audit window trimming");
            };
            Assert.Throws<IOException>(() => Upload(store, admin, "hero"));
            Assert.Equal(before, Ids(store));
            Assert.Empty(store.AdminSiteMedia());
            store.StorageFailureInjector = null;
            store.UpdateAudioPreferences(admin.Id, new());
            var restarted = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.Equal(before, Ids(restarted));
            Assert.Empty(restarted.AdminAudit(category: "site-media"));
            Assert.Empty(restarted.AdminSiteMedia());
        });
    }

    [Fact]
    public void FailureBeforeSaveCannotDeleteAnotherStoresCommittedMediaHashes()
    {
        WithUploadStore((store, admin, root) =>
        {
            L12SiteMediaView? winner = null;
            store.StorageFailureInjector = stage =>
            {
                if (stage != "before-site-media-save") return;
                var other = new L12PlatformStore(Path.Combine(root, "platform.json"));
                winner = Upload(other, admin, "hero");
                throw new IOException("first publisher fails before reaching Save or CAS refresh");
            };
            Assert.Throws<IOException>(() => Upload(store, admin, "hero"));
            Assert.NotNull(winner);
            var restarted = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.Equal(winner.Id, Assert.Single(restarted.AdminSiteMedia()).Id);
            Assert.Equal(winner.Id, Assert.Single(restarted.AdminAudit(category: "site-media")).Target);
            Assert.NotNull(restarted.ResolveSiteMediaFile(winner.Id, "desktop", winner.DesktopUrl.Split('/').Last()));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
        });
    }

    [Fact]
    public void FailedModianOuterTransactionCannotDeleteAnotherStoresCommittedMediaHashes()
    {
        WithUploadStore((store, admin, root) =>
        {
            L12SiteMediaView? winner = null;
            var payload = new L12SiteMediaUpload("article", "body.webp", "image/webp",
                Webp(997, 331), Webp(997, 331), Webp(421, 777), Webp(137, 59), "正文图", .5, .5);
            store.StorageFailureInjector = stage =>
            {
                if (stage != "after-site-media-save") return;
                var other = new L12PlatformStore(Path.Combine(root, "platform.json"));
                winner = other.UploadSiteMedia(admin, payload);
            };
            var category = store.AdminSiteCategories("news").First(item => item.Active);
            var invalid = new L12ModianPreparedDraft("221772", "标题", "摘要", DateTimeOffset.UtcNow,
                new string('a', 64), [new("import-block-001", "image", SourceImageIndex: 99, Alt: "正文图")],
                [payload], null, []);
            Assert.Throws<ArgumentException>(() => store.ApplyModianDraft(admin, category.Id, invalid, false, false));
            Assert.NotNull(winner);
            Assert.Empty(store.AdminArticles(kind: "news"));
            var restarted = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.Empty(restarted.AdminArticles(kind: "news"));
            Assert.Equal(winner.Id, Assert.Single(restarted.AdminSiteMedia()).Id);
            Assert.Single(restarted.AdminAudit(category: "site-media"));
            Assert.NotNull(restarted.ResolveSiteMediaFile(winner.Id, "desktop", winner.DesktopUrl.Split('/').Last()));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedMediaPreparationCleansOnlyItsOwnPrivateFiles(int failAt)
    {
        WithUploadStore((store, admin, root) =>
        {
            var existing = Upload(store, admin, "hero");
            var before = Directory.GetFiles(Path.Combine(root, "site-media")).Order().ToArray();
            var writes = 0;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "after-site-media-file-write" && ++writes == failAt)
                    throw new IOException("synthetic interrupted write");
            };
            Assert.Throws<IOException>(() => Upload(store, admin, "hero"));
            Assert.Equal(before, Directory.GetFiles(Path.Combine(root, "site-media")).Order().ToArray());
            Assert.Equal(existing.Id, Assert.Single(store.AdminSiteMedia()).Id);
        });
    }

    [Fact]
    public void FailedMediaSaveLeavesNoRowOrPrivateTempsButRetainsPublishedHashes()
    {
        WithUploadStore((store, admin, root) =>
        {
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("synthetic commit failure");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() => Upload(store, admin, "hero"));
            Assert.Empty(store.AdminSiteMedia());
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "site-media"), "*.upload"));
            Assert.Empty(new L12PlatformStore(Path.Combine(root, "platform.json")).AdminSiteMedia());
            Assert.Empty(store.AdminAudit(category: "site-media"));
            foreach (var file in Directory.GetFiles(Path.Combine(root, "site-media")))
                Assert.Null(store.ResolveSiteMediaFile("missing-media-row", "desktop", Path.GetFileName(file)));
        });
    }

    [Fact]
    public void MediaMirrorFailureDoesNotRollBackTheCommittedUpload()
    {
        WithUploadStore((store, admin, root) =>
        {
            var path = Path.Combine(root, "platform.json");
            store.StorageFailureInjector = stage =>
            {
                if (stage != "before-site-media-save") return;
                File.Delete(path);
                Directory.CreateDirectory(path);
            };
            var media = Upload(store, admin, "hero");
            Assert.Equal(media.Id, Assert.Single(store.AdminSiteMedia()).Id);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
            var restarted = new L12PlatformStore(path);
            Assert.Equal(media.Id, Assert.Single(restarted.AdminSiteMedia()).Id);
            Assert.NotNull(restarted.ResolveSiteMediaFile(media.Id, "desktop", media.DesktopUrl.Split('/').Last()));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MediaViewFailureDistinguishesDurableSaveFromDeferredTransaction(bool deferred)
    {
        WithUploadStore((store, admin, root) =>
        {
            store.StorageFailureInjector = stage =>
            {
                if (stage == "after-site-media-save") throw new IOException("synthetic response failure");
            };
            Assert.Throws<IOException>(() =>
            {
                if (deferred) store.ExecuteAdminTransaction(() => Upload(store, admin, "hero"));
                else Upload(store, admin, "hero");
            });
            var rows = new L12PlatformStore(Path.Combine(root, "platform.json")).AdminSiteMedia();
            if (deferred)
            {
                Assert.Empty(rows);
                Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
                Assert.Empty(Directory.GetFiles(Path.Combine(root, "site-media"), "*.upload"));
                Assert.Empty(store.AdminAudit(category: "site-media"));
                foreach (var file in Directory.GetFiles(Path.Combine(root, "site-media")))
                    Assert.Null(store.ResolveSiteMediaFile("missing-media-row", "desktop", Path.GetFileName(file)));
            }
            else
            {
                var row = Assert.Single(rows);
                Assert.NotNull(store.ResolveSiteMediaFile(row.Id, "desktop", row.DesktopUrl.Split('/').Last()));
                Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UploadConflictPreservesOtherCommittedHashesEvenWhenRowsAreDeletedOrRefreshFails(bool failRefresh)
    {
        WithUploadStore((store, admin, root) =>
        {
            L12SiteMediaView? winner = null;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-site-media-save")
                {
                    var other = new L12PlatformStore(Path.Combine(root, "platform.json"));
                    winner = Upload(other, admin, "hero");
                    other.DeleteSiteMedia(admin, winner.Id);
                }
                if (failRefresh && stage == "before-conflict-refresh")
                    throw new IOException("synthetic refresh failure");
            };
            var error = Record.Exception(() => Upload(store, admin, "hero"));
            if (failRefresh) Assert.IsType<L12PlatformStorageRefreshException>(error);
            else Assert.IsType<L12PlatformStorageConflictException>(error);
            Assert.NotNull(winner);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "site-media"), "*.upload"));
            Assert.Empty(new L12PlatformStore(Path.Combine(root, "platform.json")).AdminSiteMedia());
            if (!failRefresh)
            {
                var state = typeof(L12PlatformStore).GetField("_state",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
                var audit = (System.Collections.IEnumerable)state.GetType().GetProperty("AdminAudit")!.GetValue(state)!;
                Assert.Contains(audit.Cast<object>(), item =>
                    (string)item.GetType().GetProperty("Target")!.GetValue(item)! == winner.Id &&
                    (string)item.GetType().GetProperty("Action")!.GetValue(item)! == "upload");
            }
        });
    }

    [Fact]
    public async Task ConcurrentSameHashUploadDoesNotDeleteTheSuccessfulRequestsFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-race-" + Guid.NewGuid().ToString("N"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<Exception>? first = null;
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var blocked = 0;
            store.StorageFailureInjector = stage =>
            {
                if (stage != "before-site-media-file-write" || Interlocked.CompareExchange(ref blocked, 1, 0) != 0) return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            };
            first = Task.Run(() => Record.Exception(() => Upload(store, admin, "hero")));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var winner = Upload(store, admin, "hero");
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("loser save failure");
            };
            release.Set();
            Assert.IsType<L12PlatformStorageUnavailableException>(await first);
            Assert.Equal(winner.Id, Assert.Single(store.AdminSiteMedia()).Id);
            Assert.NotNull(store.ResolveSiteMediaFile(winner.Id, "desktop", winner.DesktopUrl.Split('/').Last()));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
            store.StorageFailureInjector = null;
            var repeated = Upload(store, admin, "hero");
            Assert.NotEqual(winner.Id, repeated.Id);
            Assert.Equal(winner.DesktopUrl.Split('/').Last(), repeated.DesktopUrl.Split('/').Last());
            Assert.Equal(2, store.AdminSiteMedia().Count);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
        }
        finally
        {
            release.Set();
            if (first is not null) await first;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void WithUploadStore(Action<L12PlatformStore, L12AccountView, string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "media-atomic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            run(store, store.Login("Admin", "L12master").Account!, root);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PreparedPayloadSurvivesAnotherRequestsRollbackOfTheSameHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-reuse-" + Guid.NewGuid().ToString("N"));
        using var published = new ManualResetEventSlim();
        using var secondPrepared = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<Exception>? first = null;
        Task<L12SiteMediaView>? second = null;
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var firstThread = 0;
            var secondWrites = 0;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "after-site-media-file-write" && published.IsSet &&
                    Interlocked.Increment(ref secondWrites) == 3) secondPrepared.Set();
                if (stage == "before-site-media-save" && Interlocked.CompareExchange(ref firstThread,
                    Environment.CurrentManagedThreadId, 0) == 0)
                {
                    published.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
                }
                if (stage == "before-commit" && Environment.CurrentManagedThreadId == firstThread)
                    throw new IOException("first request rolls back its published hashes");
            };
            first = Task.Run(() => Record.Exception(() => Upload(store, admin, "hero")));
            Assert.True(await Task.Run(() => published.Wait(TimeSpan.FromSeconds(10))));
            second = Task.Run(() => Upload(store, admin, "hero"));
            Assert.True(await Task.Run(() => secondPrepared.Wait(TimeSpan.FromSeconds(5))));
            release.Set();
            Assert.IsType<L12PlatformStorageUnavailableException>(await first);
            var winner = await second;
            Assert.Equal(winner.Id, Assert.Single(store.AdminSiteMedia()).Id);
            Assert.NotNull(store.ResolveSiteMediaFile(winner.Id, "desktop", winner.DesktopUrl.Split('/').Last()));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(root, "site-media")).Length);
        }
        finally
        {
            release.Set();
            if (first is not null) await first;
            if (second is not null) await second;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SlowMediaPreparationDoesNotBlockUnrelatedReadersOrPublishPartialRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-lock-" + Guid.NewGuid().ToString("N"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<L12SiteMediaView>? upload = null;
        Task<IReadOnlyList<L12SiteMediaView>>? read = null;
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var drain = new L12DeploymentDrainCoordinator(Guid.NewGuid().ToString("N"), new string('a', 40),
                new L12DeploymentFileFenceStore(root));
            store.AttachDeploymentDrain(drain);
            store.StorageFailureInjector = stage =>
            {
                if (stage != "before-site-media-file-write") return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Media barrier expired");
            };
            upload = Task.Run(() => Upload(store, admin, "article"));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(drain.Snapshot().ActivityLeases > 0);
            read = Task.Run(() => store.AdminSiteMedia());
            Assert.Same(read, await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2))));
            Assert.Empty(await read);
            Assert.False(upload.IsCompleted);
            release.Set();
            Assert.NotNull(await upload);
            Assert.Single(store.AdminSiteMedia());
            Assert.Equal(0, drain.Snapshot().ActivityLeases);
        }
        finally
        {
            release.Set();
            if (upload is not null) await upload;
            if (read is not null) await read;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GalleryProductManagementKeepsIdentityAndOrderAcrossRenameDisableAndReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "gp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            L12PlatformStore Open() => new(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var store = Open();
            var admin = store.Login("Admin", "L12master").Account!;
            var art = store.AdminAlternateArts().First(item => item.BuiltIn && item.Products!.Count > 0);
            var originalProduct = art.Products![0];
            var custom = store.SaveAlternateArtProduct(admin, new(null, "新收录产品"));
            store.SaveGalleryProductFilterVisibility(admin, new(originalProduct.Id, true, 20));
            store.SaveGalleryProductFilterVisibility(admin, new(custom.Id, true, 10));
            store.SaveAlternateArtMetadata(admin, new(art.Id, [originalProduct.Id, custom.Id], []));
            Assert.Equal(custom.Id, store.AlternateArts().Single(item => item.Id == art.Id).Products![0].Id);
            var renamed = store.SaveAlternateArtProduct(admin, new(originalProduct.Id, "改名后的产品", false));
            Assert.Equal(originalProduct.Id, renamed.Id);
            Assert.Equal(20, renamed.ReleaseOrder);
            Assert.DoesNotContain(store.AlternateArtProducts(), item => item.Id == originalProduct.Id);
            Assert.DoesNotContain(store.AlternateArts().Single(item => item.Id == art.Id).Products!, item => item.Id == originalProduct.Id);
            Assert.Contains(store.AdminAlternateArts().Single(item => item.Id == art.Id).Products!, item => item.Id == originalProduct.Id);
            store.SaveAlternateArtProduct(admin, new(originalProduct.Id, "改名后的产品", true));
            store.SaveAlternateArtProduct(admin, new(custom.Id, "自定义改名", false));
            var restarted = Open();
            var restored = restarted.AdminAlternateArts().Single(item => item.Id == art.Id);
            Assert.Equal(new[] { custom.Id, originalProduct.Id }, restored.Products!.Select(item => item.Id));
            Assert.Equal("自定义改名", restored.Products![0].Name);
            Assert.False(restored.Products![0].Active);
            Assert.Equal("改名后的产品", restored.Products![1].Name);
            Assert.Equal(20, restored.Products![1].ReleaseOrder);
            var legacySettings = restarted.SaveGalleryProductFilterVisibility(admin, new(originalProduct.Id, false));
            Assert.Equal(20, legacySettings.ReleaseOrder);
            foreach (var invalid in new[] { -1, 100001 })
                Assert.Throws<ArgumentException>(() => restarted.SaveGalleryProductFilterVisibility(admin, new(originalProduct.Id, true, invalid)));
            Assert.Equal(20, restarted.AlternateArtProducts(true).Single(item => item.Id == originalProduct.Id).ReleaseOrder);
            Assert.False(restarted.AlternateArtProducts(true).Single(item => item.Id == originalProduct.Id).ShowInGalleryFilter);
            Assert.Throws<ArgumentException>(() => restarted.SaveAlternateArtProduct(admin, new("catalog:missing", "不存在")));
            Assert.Throws<ArgumentException>(() => restarted.SaveAlternateArtProduct(admin, new("legacy:missing", "不存在")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void GalleryProductEnableDoesNotPublishDraftProductArticles()
    {
        var root = Path.Combine(Path.GetTempPath(), "gpd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("product").First();
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "私有产品草稿", "摘要", "正文",
                category.Name, "", "", "gp-private", false, null, Kind: "product", CategoryId: category.Id));
            var before = JsonSerializer.Serialize(store.AdminArticle(draft.Id));
            var siteId = "site:" + draft.Id;
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArtProduct(admin, new(siteId, "不得修改商品", true)));
            Assert.Equal(before, JsonSerializer.Serialize(store.AdminArticle(draft.Id)));
            var gallery = store.SaveAlternateArtProduct(admin, new(null, "独立画廊标签", false));
            var productId = gallery.Id;
            store.SaveAlternateArtProduct(admin, new(productId, "新的画廊标签", true));
            var art = store.AdminAlternateArts().First();
            store.SaveAlternateArtMetadata(admin, new(art.Id, [productId], []));
            Assert.Contains(store.AlternateArts().Single(item => item.Id == art.Id).Products!,
                item => item.Id == productId && item.Name == "新的画廊标签" && item.Active);
            Assert.Contains(store.GalleryPresentationCatalog().FilterProducts, item => item.Id == productId);
            Assert.DoesNotContain(store.GalleryPresentationCatalog().FilterProducts, item => item.Id == siteId);
            Assert.Equal("新的画廊标签", store.AdminAlternateArts().Single(item => item.Id == art.Id).Products![0].Name);
            Assert.Equal(before, JsonSerializer.Serialize(store.AdminArticle(draft.Id)));
            Assert.DoesNotContain(store.PublicArticles(kind: "product"), item => item.Id == draft.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AlternateArtProductsAreUniqueAndPersistAcrossStoreReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-products-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var saved = store.SaveAlternateArtProduct(admin, new L12AlternateArtProductDraft(null, "S01 赛季典藏"));
            Assert.Equal("S01 赛季典藏", saved.Name);
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArtProduct(admin,
                new L12AlternateArtProductDraft(null, "s01 赛季典藏")));

            var reloaded = new L12PlatformStore(path);
            Assert.Contains(reloaded.AlternateArtProducts(), item => item.Id == saved.Id && item.Name == saved.Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtProductMergeMigratesReferencesKeepsMerchandiseAndPersistsCatalogAlias()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-product-merge-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("product").First(item => item.Slug == "card-series");
            var productMedia = Upload(store, admin, "product");
            var targetDraft = store.SaveArticleDraft(admin, new(null, "收藏级异画扩充包 | 黄金的理想乡", "",
                "正式产品", category.Name, "", "/products/golden", "golden-paradise", false, null,
                Kind: "product", CategoryId: category.Id, MediaAssetId: productMedia.Id));
            var target = store.PublishArticle(admin, targetDraft.Id);
            var duplicate = store.SaveArticleDraft(admin, new(null, "黄金的理想乡", "", "误建草稿",
                category.Name, "", "", "golden-paradise-draft", false, null,
                Kind: "product", CategoryId: category.Id));
            var targetId = store.SaveAlternateArtProduct(admin, new(null, "画廊黄金的理想乡")).Id;
            var duplicateId = store.SaveAlternateArtProduct(admin, new(null, "画廊误建归属")).Id;
            var merchandiseBefore = JsonSerializer.Serialize(store.AdminArticles(kind: "product"));
            const string catalogId = "catalog:黄金的理想乡";
            Assert.Contains(store.AlternateArtProducts(true), item => item.Id == targetId);
            Assert.Contains(store.AlternateArtProducts(true), item => item.Id == duplicateId);
            Assert.Contains(store.AlternateArtProducts(true), item => item.Id == catalogId);

            var artMedia = Upload(store, admin, "card-art");
            var baseCard = catalog.Cards.Values.First();
            var art = store.SaveAlternateArt(admin, new(null, "ALT-PRODUCT-MERGE", baseCard.Id,
                baseCard.NameZh, artMedia.Id));
            store.SaveAlternateArtMetadata(admin, new(art.Id, [catalogId, duplicateId], []));

            var merged = store.MergeAlternateArtProducts(admin,
                new(targetId, [catalogId, duplicateId]));

            Assert.Equal(targetId, merged.Target.Id);
            Assert.Equal(1, merged.MigratedMetadataRows);
            Assert.Equal(0, merged.ArchivedSiteProducts);
            Assert.Equal("draft", store.AdminArticle(duplicate.Id)!.Status);
            Assert.Equal(merchandiseBefore, JsonSerializer.Serialize(store.AdminArticles(kind: "product")));
            Assert.DoesNotContain(store.AlternateArtProducts(true), item => item.Id == catalogId || item.Id == duplicateId);
            Assert.Equal([targetId], store.AdminAlternateArts().Single(item => item.Id == art.Id).Products.Select(item => item.Id));

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            Assert.DoesNotContain(reloaded.AlternateArtProducts(true), item => item.Id == catalogId || item.Id == duplicateId);
            Assert.Equal([targetId], reloaded.AdminAlternateArts().Single(item => item.Id == art.Id).Products.Select(item => item.Id));
            Assert.Throws<ArgumentException>(() => reloaded.MergeAlternateArtProducts(admin,
                new(catalogId, [targetId])));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RegularGalleryCardMetadataAndProductFilterVisibilityPersistWithoutChangingRules()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-regular-gallery-card-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var cardProduct = catalog.CardProducts.First(item => item.Value.Count > 0 && catalog.Cards.ContainsKey(item.Key));
            var card = catalog.Cards[cardProduct.Key];
            var originalEffect = card.Effect;
            var productId = $"catalog:{cardProduct.Value[0]}";
            var rarity = store.SaveAlternateArtRarity(admin, new(null, "QA", "测试罕贵"));
            var media = Upload(store, admin, "card-art");

            var saved = store.SaveRegularGalleryCardMetadata(admin,
                new(card.Id, [productId], [rarity.Id], "测试画师", "常规联动"));
            var changedImage = store.SaveRegularGalleryCardImage(admin, new(card.Id, media.Id));
            var hidden = store.SaveGalleryProductFilterVisibility(admin, new(productId, false));

            Assert.Equal("regular-card", saved.Kind);
            Assert.Equal(card.Id, saved.Id);
            Assert.Equal(card.Number, saved.ArtCode);
            Assert.Equal(card.NameZh, saved.DisplayName);
            Assert.Equal("测试画师", changedImage.Artist);
            Assert.Equal("常规联动", changedImage.Collaboration);
            Assert.Equal(media.DesktopUrl, changedImage.ImageUrl);
            Assert.Equal([productId], changedImage.Products!.Select(item => item.Id));
            Assert.Equal([rarity.Id], changedImage.Rarities!.Select(item => item.Id));
            Assert.False(hidden.ShowInGalleryFilter);
            Assert.Equal(originalEffect, catalog.Cards[card.Id].Effect);

            var presentation = store.GalleryPresentationCatalog();
            var publicCard = presentation.RegularCards.Single(item => item.Id == card.Id);
            Assert.Equal(media.DesktopUrl, publicCard.ImageUrl);
            Assert.Equal("测试画师", publicCard.Artist);
            Assert.Equal("常规联动", publicCard.Collaboration);
            Assert.Contains(publicCard.Products!, item => item.Id == productId);
            Assert.Contains(presentation.FilterProducts,
                item => item.Id == productId && !item.ShowInGalleryFilter);
            Assert.Contains(store.SearchRegularGalleryCards(card.NameZh, card.Number, productId).Items,
                item => item.Id == card.Id);

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var restoredState = reloaded.GalleryPresentationCatalog();
            Assert.Equal("常规联动", reloaded.SaveRegularGalleryCardMetadata(admin, new(card.Id, [productId], [rarity.Id])).Collaboration);
            Assert.Equal("", reloaded.SaveRegularGalleryCardMetadata(admin, new(card.Id, [productId], [rarity.Id], Collaboration: "")).Collaboration);
            Assert.Contains(restoredState.RegularCards,
                item => item.Id == card.Id && item.Artist == "测试画师" && item.MediaAssetId == media.Id);
            Assert.Contains(restoredState.FilterProducts,
                item => item.Id == productId && !item.ShowInGalleryFilter);

            var restoredImage = reloaded.SaveRegularGalleryCardImage(admin, new(card.Id, ""));
            Assert.Equal("", restoredImage.MediaAssetId);
            reloaded.DeleteSiteMedia(admin, media.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtNameAlwaysFollowsBaseCardAndActiveArtIsPublic()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-card-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "card-art");
            var product = store.SaveAlternateArtProduct(admin, new(null, "活动典藏"));
            var preset = catalog.PresetDecks.First();
            var baseCard = catalog.Cards[preset.CardIds.First()];

            var saved = store.SaveAlternateArt(admin, new(null, "ALT-TEST-001", baseCard.Id,
                "不应采用的自定义卡名", media.Id, Active: true, ProductId: product.Id));

            Assert.Equal(baseCard.NameZh, saved.DisplayName);
            Assert.Equal(product.Id, saved.ProductId);
            Assert.Contains(store.AlternateArts(), item => item.Id == saved.Id && item.Active);

            var entitled = store.Register("ent" + Guid.NewGuid().ToString("N")[..7], "Password123!").Account!;
            var unentitled = store.Register("unent" + Guid.NewGuid().ToString("N")[..5], "Password123!").Account!;
            store.GrantAlternateArt(admin, new(saved.Id, entitled.Id, "manual", "archive-full-pool"));
            Assert.Contains(store.OwnedAlternateArts(entitled.Id), item => item.Id == saved.Id && !item.BuiltIn);
            Assert.DoesNotContain(store.OwnedAlternateArts(unentitled.Id), item => item.Id == saved.Id);
            Assert.Contains(store.AlternateArts(), item => item.Id == saved.Id && item.ImageUrl == saved.ImageUrl);

            Assert.DoesNotContain(store.OwnedAlternateArts(admin.Id), item => item.Id == saved.Id);
            store.GrantAlternateArt(admin, new(saved.Id, admin.Id, "manual", "管理员测试授权"));
            var owned = Assert.Single(store.OwnedAlternateArts(admin.Id), item => item.Id == saved.Id);
            Assert.Equal("管理员测试授权", owned.GrantReason);
            var copies = preset.CardIds.ToList();
            var deck = new L12PresetDeckDefinition
            {
                Name = "自主上传异画构筑",
                MasterId = preset.MasterId,
                CardIds = copies,
                MoraleIds = preset.MoraleIds.ToList(),
                SpecialIds = preset.SpecialIds.ToList(),
                AlternateArtCopies = new(StringComparer.OrdinalIgnoreCase)
                {
                    [baseCard.Id] = Enumerable.Range(0, copies.Count(id => id == baseCard.Id))
                        .Select(index => index == 0 ? saved.Id : string.Empty).ToList(),
                },
            };
            var storedDeck = store.UpsertDeck(admin.Id, deck);
            Assert.Equal(saved.Id, storedDeck.AlternateArtCopies![baseCard.Id][0]);
            var storedCopies = storedDeck.AlternateArtCopies.ToDictionary(pair => pair.Key,
                pair => pair.Value.ToList(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(saved.ImageUrl,
                store.ResolveOwnedAlternateArtUrls(admin.Id, null, storedCopies)[$"{baseCard.Id}#1"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GalleryCardsAreGrantableBuiltInAlternateArtsAndResolveThroughCardManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-gallery-alternate-art-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            Assert.Equal(42, catalog.OfficialAlternateArts.Count);
            Assert.Contains(catalog.OfficialAlternateArts, row => row.CardImageId == "S02-05C1A");
            Assert.DoesNotContain(catalog.OfficialAlternateArts, row => row.CardImageId == "S02-05C1B");

            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("gal" + Guid.NewGuid().ToString("N")[..7],
                "Password123!").Account!;
            var art = store.AlternateArts().First(row => row.BuiltIn);
            Assert.True(art.Active);
            Assert.False(string.IsNullOrWhiteSpace(art.CardImageId));

            store.GrantAlternateArt(admin, new L12AlternateArtGrantDraft(art.Id,
                player.Id, "manual", "gallery-regression"));
            Assert.Contains(store.OwnedAlternateArts(player.Id), row => row.Id == art.Id && row.BuiltIn);

            var preset = catalog.PresetDecks[0];
            var deck = new L12PresetDeckDefinition
            {
                Name = "内置异画测试",
                MasterId = preset.MasterId,
                CardIds = preset.CardIds.ToList(),
                MoraleIds = preset.MoraleIds.ToList(),
                SpecialIds = preset.SpecialIds.ToList(),
                AlternateArtSelections = new(StringComparer.OrdinalIgnoreCase) { [art.BaseCardId] = art.Id },
            };
            var saved = store.UpsertDeck(player.Id, deck);
            Assert.Equal(art.Id, saved.AlternateArtSelections![art.BaseCardId]);
            Assert.Equal($"l12-card-id:{art.CardImageId}",
                store.ResolveOwnedAlternateArtUrls(player.Id, saved.AlternateArtSelections)[art.BaseCardId]);

            Assert.Throws<ArgumentException>(() => store.SaveAlternateArt(admin,
                new(null, art.ArtCode, art.BaseCardId, art.DisplayName, "missing-media")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OfficialAlternateArtImageOverrideKeepsStableIdentityAndCanRestoreCatalogImage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-official-alternate-art-image-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("ofi" + Guid.NewGuid().ToString("N")[..6], "Password123!").Account!;
            var media = Upload(store, admin, "card-art");
            // Product-first gallery order may begin with a morale or a non-preset legion.
            var before = store.AdminAlternateArts().First(item => item.BuiltIn
                && catalog.PresetDecks.Any(preset => preset.CardIds.Contains(item.BaseCardId)));
            store.GrantAlternateArt(admin, new(before.Id, player.Id, "manual", "image-override"));

            var preset = catalog.PresetDecks.First(item => item.CardIds.Contains(before.BaseCardId));
            var deck = new L12PresetDeckDefinition
            {
                Name = "内置异画覆盖图身份回归", MasterId = preset.MasterId,
                CardIds = preset.CardIds.ToList(), MoraleIds = preset.MoraleIds.ToList(),
                SpecialIds = preset.SpecialIds.ToList(),
                AlternateArtSelections = new(StringComparer.OrdinalIgnoreCase) { [before.BaseCardId] = before.Id },
            };
            var storedDeck = store.UpsertDeck(player.Id, deck);

            var changed = store.SaveAlternateArtImage(admin, new(before.Id, media.Id));

            Assert.Equal(before.Id, changed.Id);
            Assert.Equal(before.ArtCode, changed.ArtCode);
            Assert.Equal(before.BaseCardId, changed.BaseCardId);
            Assert.Equal(before.CardImageId, changed.CardImageId);
            Assert.Equal(before.Kind, changed.Kind);
            Assert.True(changed.BuiltIn);
            Assert.Equal(media.Id, changed.MediaAssetId);
            Assert.Equal(media.DesktopUrl, changed.ImageUrl);
            Assert.Equal(media.ThumbnailUrl, changed.ThumbnailUrl);
            Assert.Equal(before.Products!.Select(item => item.Id), changed.Products!.Select(item => item.Id));
            Assert.Equal(before.Rarities!.Select(item => item.Id), changed.Rarities!.Select(item => item.Id));
            Assert.Equal(changed.ImageUrl, store.AlternateArts().Single(item => item.Id == before.Id).ImageUrl);
            Assert.Equal(changed.ImageUrl, store.OwnedAlternateArts(player.Id).Single(item => item.Id == before.Id).ImageUrl);
            Assert.Equal(changed.ImageUrl,
                store.ResolveOwnedAlternateArtUrls(player.Id, storedDeck.AlternateArtSelections)[before.BaseCardId]);
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var persisted = reloaded.AdminAlternateArts().Single(item => item.Id == before.Id);
            Assert.Equal(changed.ImageUrl, persisted.ImageUrl);
            Assert.Equal(before.CardImageId, persisted.CardImageId);
            Assert.Equal(before.Products!.Select(item => item.Id), persisted.Products!.Select(item => item.Id));
            Assert.Equal(before.Rarities!.Select(item => item.Id), persisted.Rarities!.Select(item => item.Id));

            var restored = reloaded.SaveAlternateArtImage(admin, new(before.Id, ""));
            Assert.Equal(before.Id, restored.Id);
            Assert.Equal(before.CardImageId, restored.CardImageId);
            Assert.Equal("", restored.MediaAssetId);
            Assert.Equal("", restored.ImageUrl);
            Assert.Equal(before.Products!.Select(item => item.Id), restored.Products!.Select(item => item.Id));
            Assert.Equal(before.Rarities!.Select(item => item.Id), restored.Rarities!.Select(item => item.Id));
            Assert.Equal($"l12-card-id:{before.CardImageId}",
                reloaded.ResolveOwnedAlternateArtUrls(player.Id, storedDeck.AlternateArtSelections)[before.BaseCardId]);
            reloaded.DeleteSiteMedia(admin, media.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CustomAlternateArtImageReplacementChangesOnlyMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-custom-alternate-art-image-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
                officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var oldMedia = Upload(store, admin, "card-art");
            var newMedia = Upload(store, admin, "card-art");
            var invalidMedia = Upload(store, admin, "product");
            var baseCard = catalog.Cards.Values.First();
            var created = store.SaveAlternateArt(admin, new(null, "ALT-IMAGE-ONLY", baseCard.Id,
                baseCard.NameZh, oldMedia.Id, Effect: "保留的资料"));
            var product = store.AlternateArtProducts(true).First();
            var rarity = store.AlternateArtRarities(true).First();
            var before = store.SaveAlternateArtMetadata(admin,
                new(created.Id, [product.Id], [rarity.Id]));

            var changed = store.SaveAlternateArtImage(admin, new(created.Id, newMedia.Id));

            Assert.Equal(before.Id, changed.Id);
            Assert.Equal(before.ArtCode, changed.ArtCode);
            Assert.Equal(before.BaseCardId, changed.BaseCardId);
            Assert.Equal(before.DisplayName, changed.DisplayName);
            Assert.Equal(before.Kind, changed.Kind);
            Assert.Equal(before.Effect, changed.Effect);
            Assert.Equal(before.Active, changed.Active);
            Assert.Equal(before.Products!.Select(item => item.Id), changed.Products!.Select(item => item.Id));
            Assert.Equal(before.Rarities!.Select(item => item.Id), changed.Rarities!.Select(item => item.Id));
            Assert.Equal(newMedia.Id, changed.MediaAssetId);
            Assert.Equal(newMedia.DesktopUrl, changed.ImageUrl);
            Assert.Equal(newMedia.ThumbnailUrl, changed.ThumbnailUrl);
            var staleMetadataSave = store.SaveAlternateArt(admin, new(created.Id, created.ArtCode,
                created.BaseCardId, created.DisplayName, oldMedia.Id, created.Active, Kind: created.Kind,
                Effect: created.Effect));
            Assert.Equal(newMedia.Id, staleMetadataSave.MediaAssetId);
            Assert.Equal(newMedia.DesktopUrl, staleMetadataSave.ImageUrl);
            store.DeleteSiteMedia(admin, oldMedia.Id);
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, newMedia.Id));
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArtImage(admin, new(created.Id, "")));
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArtImage(admin, new(created.Id, invalidMedia.Id)));
            Assert.Throws<KeyNotFoundException>(() => store.SaveAlternateArtImage(admin, new("missing-art", newMedia.Id)));
            Assert.Equal(newMedia.Id, store.AdminAlternateArts().Single(item => item.Id == created.Id).MediaAssetId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HistoricalBatchAlternateArtMetadataIsManyToManyAndKeepsIdentityAndEntitlements()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-metadata-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("meta" + Guid.NewGuid().ToString("N")[..6], "Password123!").Account!;
            var art = store.AdminAlternateArts().Single(item => item.ArtCode.Equals("S01-0101b",
                StringComparison.OrdinalIgnoreCase));
            Assert.Equal("catalog-alt:S01-0101b", art.Id);
            Assert.Equal("S01-0101b", art.CardImageId);
            Assert.Equal("S01-0101", art.BaseCardId);
            Assert.Contains(art.Products, item => item.Id == "catalog:ST01|天廷阵营预组");
            Assert.Contains(art.Rarities, item => item.Code == "N");
            Assert.DoesNotContain(store.AdminAlternateArts(), item => item.CardImageId == "S02-05C1B");

            var category = store.AdminSiteCategories("product").First(item => item.Slug == "card-series");
            var draftProduct = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "后台未发布商品", "不可公开摘要",
                "不可公开正文", category.Name, "", "/cards", "private-alt-product", false, null,
                Kind: "product", CategoryId: category.Id));
            var products = store.AlternateArtProducts(includeInactive: true);
            Assert.DoesNotContain(products, item => item.Id == $"site:{draftProduct.Id}");
            var galleryProduct = store.SaveAlternateArtProduct(admin, new(null, "独立联动收录"));
            var catalogProduct = products.First(item => item.Id == "catalog:ST01|天廷阵营预组");
            Assert.Equal("legacy", galleryProduct.Source);
            Assert.Equal("active", galleryProduct.Status);

            var foil = store.SaveAlternateArtRarity(admin, new L12AlternateArtRarityDraft(null, "FOIL", "闪卡"));
            var normal = store.AlternateArtRarities(includeInactive: true).Single(item => item.Code == "N");
            var saved = store.SaveAlternateArtMetadata(admin, new L12AlternateArtMetadataDraft(art.Id,
                [catalogProduct.Id, galleryProduct.Id], [normal.Id, foil.Id]));
            Assert.Equal(2, saved.Products.Count);
            Assert.Equal(2, saved.Rarities.Count);

            var grant = store.GrantAlternateArt(admin, new(art.Id, player.Id, "manual", "metadata-regression"));
            var preset = catalog.PresetDecks.First(item => item.CardIds.Contains(art.BaseCardId));
            var deck = new L12PresetDeckDefinition
            {
                Name = "异画元数据身份回归", MasterId = preset.MasterId, CardIds = preset.CardIds.ToList(),
                MoraleIds = preset.MoraleIds.ToList(), SpecialIds = preset.SpecialIds.ToList(),
                AlternateArtSelections = new(StringComparer.OrdinalIgnoreCase) { [art.BaseCardId] = art.Id },
            };
            var storedDeck = store.UpsertDeck(player.Id, deck);
            Assert.Equal(art.Id, storedDeck.AlternateArtSelections![art.BaseCardId]);

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var reloadedAdminArt = reloaded.AdminAlternateArts().Single(item => item.Id == art.Id);
            Assert.Equal(2, reloadedAdminArt.Products.Count);
            Assert.Equal(2, reloadedAdminArt.Rarities.Count);
            Assert.Contains(reloaded.AlternateArts().Single(item => item.Id == art.Id).Products!, item => item.Id == galleryProduct.Id);
            Assert.Contains(reloaded.OwnedAlternateArts(player.Id).Single(item => item.Id == art.Id).Products!, item => item.Id == galleryProduct.Id);
            Assert.Equal("draft", reloaded.AdminArticle(draftProduct.Id)!.Status);
            Assert.DoesNotContain(reloaded.AlternateArts().Single(item => item.Id == art.Id).Products!, item => item.Name.Contains("未发布", StringComparison.Ordinal));
            Assert.Equal(grant.Id, reloaded.AlternateArtGrants().Single(item => item.Id == grant.Id).Id);
            Assert.Equal($"l12-card-id:{art.CardImageId}",
                reloaded.ResolveOwnedAlternateArtUrls(player.Id, storedDeck.AlternateArtSelections)[art.BaseCardId]);

            var reduced = reloaded.SaveAlternateArtMetadata(admin, new L12AlternateArtMetadataDraft(art.Id,
                [catalogProduct.Id], [foil.Id]));
            Assert.Equal([catalogProduct.Id], reduced.Products.Select(item => item.Id));
            Assert.Equal([foil.Id], reduced.Rarities.Select(item => item.Id));
            var cleared = reloaded.SaveAlternateArtMetadata(admin,
                new L12AlternateArtMetadataDraft(art.Id, [], []));
            Assert.Empty(cleared.Products);
            Assert.Empty(cleared.Rarities);
            var clearedReload = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            Assert.Empty(clearedReload.AdminAlternateArts().Single(item => item.Id == art.Id).Products);
            Assert.Empty(clearedReload.AdminAlternateArts().Single(item => item.Id == art.Id).Rarities);
            Assert.Contains(clearedReload.OwnedAlternateArts(player.Id), item => item.Id == art.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LegacySingleProductAssociationMigratesWithoutMergingSameNamedProducts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-legacy-product-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "card-art");
            var legacy = store.SaveAlternateArtProduct(admin, new(null, "第1季|典藏版"));
            var baseCard = catalog.Cards.Values.First();
            var custom = store.SaveAlternateArt(admin, new(null, "ALT-LEGACY-MIGRATION", baseCard.Id,
                baseCard.NameZh, media.Id, ProductId: legacy.Id));

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var art = reloaded.AdminAlternateArts().Single(item => item.Id == custom.Id);
            Assert.Contains(art.Products, item => item.Id == legacy.Id);
            Assert.DoesNotContain(art.Products, item => item.Id == "catalog:第1季|典藏版");
            Assert.Contains(reloaded.AlternateArtProducts(true), item => item.Id == legacy.Id);
            Assert.Contains(reloaded.AlternateArtProducts(true), item => item.Id == "catalog:第1季|典藏版");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StandaloneCollaborationCardIsGalleryOnlyAndNeverBecomesRulesOrDeckAlternateArt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-gallery-collaboration-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
                officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("collab" + Guid.NewGuid().ToString("N")[..5], "Password123!").Account!;
            var media = Upload(store, admin, "card-art");
            var saved = store.SaveAlternateArt(admin, new L12AlternateArtDraft(null, "COLLAB-001", "",
                "十二军团 × 联动角色", media.Id, Kind: "gallery-card", Effect: "此卡具有独立撰写的联动卡效。"));

            Assert.Equal("gallery-card", saved.Kind);
            Assert.Equal("", saved.BaseCardId);
            Assert.Equal("十二军团 × 联动角色", saved.DisplayName);
            Assert.Equal("此卡具有独立撰写的联动卡效。", saved.Effect);
            Assert.DoesNotContain(saved.Id, catalog.Cards.Keys);
            var publicCard = Assert.Single(store.AlternateArts(), item => item.Id == saved.Id);
            Assert.Equal("gallery-card", publicCard.Kind);
            Assert.Throws<ArgumentException>(() => store.GrantAlternateArt(admin,
                new(saved.Id, player.Id, "manual", "gallery-only")));
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArtAwardRule(admin,
                new(null, saved.Id, "event", "", "collaboration", 0)));
            Assert.Throws<ArgumentException>(() => store.PreviewRankedParticipantAlternateArtDispatch(
                new(saved.Id)));
            Assert.Throws<ArgumentException>(() => store.DispatchRankedParticipantAlternateArt(admin,
                new(saved.Id)));
            Assert.Empty(store.OwnedAlternateArts(player.Id));
            Assert.Empty(store.AlternateArtGrants().Where(item => item.AlternateArtId == saved.Id));
            Assert.Empty(store.PendingAlternateArtGrantNotifications(player.Id));

            var preset = catalog.PresetDecks[0];
            var deck = new L12PresetDeckDefinition
            {
                Name = "联动卡不可注入牌库", MasterId = preset.MasterId, CardIds = preset.CardIds.ToList(),
                MoraleIds = preset.MoraleIds.ToList(), SpecialIds = preset.SpecialIds.ToList(),
                AlternateArtSelections = new(StringComparer.OrdinalIgnoreCase)
                {
                    [preset.CardIds[0]] = saved.Id,
                },
            };
            var stored = store.UpsertDeck(player.Id, deck);
            Assert.False(stored.AlternateArtSelections?.Values.Contains(saved.Id) == true);
            Assert.Empty(store.ResolveOwnedAlternateArtUrls(player.Id,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [preset.CardIds[0]] = saved.Id }));

            var normalArt = store.SaveAlternateArt(admin, new L12AlternateArtDraft(null, "ALT-LOCK-001",
                preset.CardIds[0], "ignored", media.Id));
            store.GrantAlternateArt(admin, new(normalArt.Id, player.Id, "manual", "identity-lock"));
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArt(admin, new L12AlternateArtDraft(normalArt.Id,
                normalArt.ArtCode, "", "试图转换", media.Id, Kind: "gallery-card", Effect: "不应保存")));
            var unchanged = Assert.Single(store.AdminAlternateArts(), item => item.Id == normalArt.Id);
            Assert.Equal("alternate-art", unchanged.Kind);
            Assert.Equal(preset.CardIds[0], unchanged.BaseCardId);
            Assert.Single(store.OwnedAlternateArts(player.Id), item => item.Id == normalArt.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GalleryArtistPersistsForBuiltInUploadedAndCollaborationCardsWithOmitAndClearSemantics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-gallery-artist-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "card-art");
            var baseCard = catalog.Cards.Values.First();
            var builtIn = store.AdminAlternateArts().First(item => item.BuiltIn);
            var uploaded = store.SaveAlternateArt(admin, new(null, "ALT-ARTIST-001", baseCard.Id,
                baseCard.NameZh, media.Id));
            var collaboration = store.SaveAlternateArt(admin, new(null, "COLLAB-ARTIST-001", "",
                "联动画师测试", media.Id, Kind: "gallery-card", Effect: "只在画廊展示。"));

            store.SaveAlternateArtMetadata(admin, new(builtIn.Id, [], [], Artist: "内建画师", Collaboration: "内建联动"));
            store.SaveAlternateArtMetadata(admin, new(uploaded.Id, [], [], Artist: "上传画师", Collaboration: "上传联动"));
            store.SaveAlternateArtMetadata(admin, new(collaboration.Id, [], [], Artist: "联动画师", Collaboration: "独立联动"));

            var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialAlternateArts: catalog.OfficialAlternateArts, officialCardProducts: catalog.CardProducts);
            Assert.Equal("内建画师", Assert.Single(reloaded.AlternateArts(), item => item.Id == builtIn.Id).Artist);
            Assert.Equal("上传画师", Assert.Single(reloaded.AlternateArts(), item => item.Id == uploaded.Id).Artist);
            Assert.Equal("联动画师", Assert.Single(reloaded.AlternateArts(), item => item.Id == collaboration.Id).Artist);
            foreach (var (id, expected) in new[] { (builtIn.Id, "内建联动"), (uploaded.Id, "上传联动"), (collaboration.Id, "独立联动") })
                Assert.Equal(expected, reloaded.AlternateArts().Single(item => item.Id == id).Collaboration);

            // Legacy clients omit Artist. That must preserve an existing artist instead of silently clearing it.
            var legacyUpdate = reloaded.SaveAlternateArtMetadata(admin,
                new L12AlternateArtMetadataDraft(uploaded.Id, [], []));
            Assert.Equal("上传画师", legacyUpdate.Artist);
            Assert.Equal("上传联动", legacyUpdate.Collaboration);
            Assert.Equal("", reloaded.SaveAlternateArtMetadata(admin, new(collaboration.Id, [], [], Collaboration: "")).Collaboration);
            var longCollaboration = reloaded.SaveAlternateArtMetadata(admin, new(builtIn.Id, [], [], Collaboration: new string('联', 121)));
            Assert.Equal(120, longCollaboration.Collaboration.Length);
            Assert.Equal("内建画师", longCollaboration.Artist);

            // An explicit empty value is the intentional clear operation.
            var cleared = reloaded.SaveAlternateArtMetadata(admin,
                new L12AlternateArtMetadataDraft(collaboration.Id, [], [], Artist: ""));
            Assert.Equal("", cleared.Artist);
            Assert.Equal("", Assert.Single(reloaded.AlternateArts(), item => item.Id == collaboration.Id).Artist);
            var bounded = reloaded.SaveAlternateArtMetadata(admin,
                new L12AlternateArtMetadataDraft(builtIn.Id, [], [], Artist: new string('画', 121)));
            Assert.Equal(120, bounded.Artist.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GalleryArtistMetadataHttpRequiresPermissionAndPreservesOmittedArtist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-gallery-artist-http-{Guid.NewGuid():N}");
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
                officialCardProducts: catalog.CardProducts);
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var player = store.Register("artist" + Guid.NewGuid().ToString("N")[..5], "Password123!");
            var admin = store.Login("Admin", "L12master");
            var art = store.AdminAlternateArts().First(item => item.BuiltIn);
            store.SaveAlternateArtMetadata(admin.Account!, new(art.Id, [], [], Artist: "原画师"));

            using (var request = Authorized(HttpMethod.Put, "/api/admin/alternate-arts/metadata", player.Token!,
                       new { alternateArtId = art.Id, productIds = Array.Empty<string>(), rarityIds = Array.Empty<string>(), artist = "越权画师" }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Put, "/api/admin/alternate-arts/metadata", admin.Token!,
                       new { alternateArtId = art.Id, productIds = Array.Empty<string>(), rarityIds = Array.Empty<string>() }))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("原画师", (await response.Content.ReadFromJsonAsync<L12AlternateArtView>())!.Artist);
            }

            using (var request = Authorized(HttpMethod.Put, "/api/admin/alternate-arts/metadata", admin.Token!,
                       new { alternateArtId = art.Id, productIds = Array.Empty<string>(), rarityIds = Array.Empty<string>(), artist = "HTTP 画师" }))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("HTTP 画师", (await response.Content.ReadFromJsonAsync<L12AlternateArtView>())!.Artist);
            }

            var publicRows = await client.GetFromJsonAsync<L12AlternateArtView[]>("/api/alternate-arts");
            Assert.Equal("HTTP 画师", Assert.Single(publicRows!, item => item.Id == art.Id).Artist);

            using (var request = Authorized(HttpMethod.Put, "/api/admin/alternate-arts/metadata", admin.Token!,
                       new { alternateArtId = art.Id, productIds = Array.Empty<string>(), rarityIds = Array.Empty<string>(), artist = "" }))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("", (await response.Content.ReadFromJsonAsync<L12AlternateArtView>())!.Artist);
            }
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtRegistrySearchIsFilteredAndPagedWithoutLoadingTheFullRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-search-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var expected = store.AlternateArts().First();

            var byCode = store.SearchAlternateArts(null, expected.ArtCode, null, page: 1, pageSize: 5);
            Assert.Equal(1, byCode.Page);
            Assert.InRange(byCode.Items.Count, 1, 5);
            Assert.Contains(byCode.Items, item => item.Id == expected.Id);

            var byBaseCard = store.SearchAlternateArts(null, null, expected.BaseCardName, page: 1, pageSize: 5);
            Assert.Contains(byBaseCard.Items, item => item.Id == expected.Id);
            Assert.True(store.SearchAlternateArts(null, null, null, page: 2, pageSize: 5).Total > 5);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtGrantNotificationPersistsUntilAcknowledgedAndOwnedViewHasGrantFacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-notification-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("notify" + Guid.NewGuid().ToString("N")[..5], "Password123!").Account!;
            var art = store.AlternateArts().First();
            var grant = store.GrantAlternateArt(admin, new(art.Id, player.Id, "event", "测试活动"));

            var notification = Assert.Single(store.PendingAlternateArtGrantNotifications(player.Id));
            Assert.Equal(grant.Id, notification.Id);
            Assert.Equal(art.ArtCode, notification.ArtCode);
            Assert.Equal("测试活动", notification.Reason);
            var owned = Assert.Single(store.OwnedAlternateArts(player.Id), item => item.Id == art.Id);
            Assert.NotNull(owned.GrantedAt);
            Assert.False(string.IsNullOrWhiteSpace(owned.BaseCardName));

            var reloaded = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            Assert.Single(reloaded.PendingAlternateArtGrantNotifications(player.Id));
            reloaded.AcknowledgeAlternateArtGrantNotification(player.Id, grant.Id);
            Assert.Empty(reloaded.PendingAlternateArtGrantNotifications(player.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtDirectGrantRequiresUniqueActiveAccountIdAndBlankReasonStaysBlank()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-account-target-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var admin = store.Login("Admin", "L12master").Account!;
            var active = store.Register("桐玩家", "Password123!").Account!;
            var disabled = store.Register("桐玩家二", "Password123!").Account!;
            var art = store.AlternateArts().First();

            var grant = store.GrantAlternateArt(admin, new(art.Id, active.Id, "manual", ""));
            Assert.Equal(active.Id, grant.AccountId);
            Assert.Equal("", Assert.Single(store.PendingAlternateArtGrantNotifications(active.Id)).Reason);
            Assert.Throws<KeyNotFoundException>(() =>
                store.GrantAlternateArt(admin, new(art.Id, active.Username, "manual", "用户名不得替代唯一ID")));

            store.SetAccountDisabled(admin, disabled.Id, true, "异画候选状态回归",
                new("alternate-art-disabled-target"), true);
            var error = Assert.Throws<ArgumentException>(() =>
                store.GrantAlternateArt(admin, new(art.Id, disabled.Id, "manual", "不应派发")));
            Assert.Contains("已禁用", error.Message);
            Assert.Empty(store.OwnedAlternateArts(disabled.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtAutomaticGrantMetadataIsNotShownAsRecipientReason()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-private-source-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("event" + Guid.NewGuid().ToString("N")[..6], "Password123!").Account!;
            var art = store.AlternateArts().First();
            var rule = store.SaveAlternateArtAwardRule(admin,
                new(null, art.Id, "event", "", "INTERNAL-EVENT-42", 0));

            store.DispatchAlternateArtEvent(admin, new(rule.Id, [player.Id]));

            var notification = Assert.Single(store.PendingAlternateArtGrantNotifications(player.Id));
            Assert.Equal("", notification.Reason);
            Assert.DoesNotContain("INTERNAL-EVENT-42", notification.Reason);
            Assert.Throws<KeyNotFoundException>(() =>
                store.DispatchAlternateArtEvent(admin, new(rule.Id, [player.Username])));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AlternateArtCopiesShareTheBaseCardCountAndResolvePerCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-alternate-art-copies-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var admin = store.Login("Admin", "L12master").Account!;
            var player = store.Register("copies" + Guid.NewGuid().ToString("N")[..5], "Password123!").Account!;
            var art = store.AlternateArts().First();
            store.GrantAlternateArt(admin, new(art.Id, player.Id, "manual", "混搭测试"));
            var preset = catalog.PresetDecks[0];
            var deck = new L12PresetDeckDefinition
            {
                Name = "原画异画混搭",
                MasterId = preset.MasterId,
                CardIds = [art.BaseCardId, art.BaseCardId],
                MoraleIds = preset.MoraleIds.ToList(),
                SpecialIds = preset.SpecialIds.ToList(),
                AlternateArtCopies = new(StringComparer.OrdinalIgnoreCase)
                {
                    [art.BaseCardId] = [art.Id, string.Empty],
                },
            };

            var saved = store.UpsertDeck(player.Id, deck);
            Assert.Equal([art.Id, string.Empty], saved.AlternateArtCopies![art.BaseCardId]);
            var urls = store.ResolveOwnedAlternateArtUrls(player.Id, null, deck.AlternateArtCopies);
            Assert.True(urls.ContainsKey($"{art.BaseCardId}#1"));
            Assert.False(urls.ContainsKey($"{art.BaseCardId}#2"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleRulingsPublishOnlyStructuredConfirmedEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-rulings-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.True(store.IsContentKeyAllowed("rules.rulings"));
            var published = JsonSerializer.Serialize(new
            {
                entries = new[]
                {
                    new { id = "RULING-TEST-1", scope = "card", question = "测试问题", answer = "裁定：测试答案。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "用户确认裁定 · 2026-09-17",
                        recordedAt = "2026-09-17", status = "published", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), sourceIds = new[] { "LEGACY-FAQ-47" },
                        supersedes = new[] { "LEGACY-FAQ-47" } },
                },
            });
            store.SaveContentDraft(admin, "rules.rulings", published);
            store.PublishContent(admin, "rules.rulings");
            Assert.Equal(published, store.GetContent("rules.rulings"));

            var mixedDraft = JsonSerializer.Serialize(new
            {
                entries = new[]
                {
                    new { id = "RULING-TEST-1", scope = "card", question = "测试问题", answer = "裁定：测试答案。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "用户确认裁定 · 2026-09-17",
                        recordedAt = "2026-09-17", status = "published", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), sourceIds = new[] { "LEGACY-FAQ-47" },
                        supersedes = new[] { "LEGACY-FAQ-47" } },
                    new { id = "RULING-TEST-2", scope = "card", question = "待复核问题", answer = "尚待审核。",
                        category = "单卡裁定", sourceKind = "official-faq", sourceRef = "原始 FAQ",
                        recordedAt = "2026-09-17", status = "pending", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), sourceIds = Array.Empty<string>(),
                        supersedes = Array.Empty<string>() },
                },
            });
            store.SaveContentDraft(admin, "rules.rulings", mixedDraft);
            store.PublishContent(admin, "rules.rulings");
            using var publicDocument = JsonDocument.Parse(store.GetContent("rules.rulings"));
            var publicEntries = publicDocument.RootElement.GetProperty("entries");
            Assert.Single(publicEntries.EnumerateArray());
            Assert.Equal("RULING-TEST-1", publicEntries[0].GetProperty("id").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CardRulingProductsAreDerivedOnReadSaveAndPublish()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-products-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var legacyStore = new L12PlatformStore(path);
            var stale = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                entries = new object[]
                {
                    new { id = "RULING-SINGLE", scope = "card", question = "单卡问题？", answer = "单卡答案。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "管理员复核",
                        recordedAt = "2026-09-28", status = "published", cardIds = new[] { "S02-06S5" },
                        productIds = new[] { "S02" }, tags = Array.Empty<string>(), topics = new[] { "effects-stack" },
                        sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>() },
                    new { id = "RULING-MULTI", scope = "card", question = "多卡问题？", answer = "多卡答案。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "管理员复核",
                        recordedAt = "2026-09-28", status = "pending", cardIds = new[] { "S02-06S5", "S01-0213", "S02-06S5" },
                        productIds = new[] { "MANUAL" }, tags = Array.Empty<string>(), topics = new[] { "effects-stack" },
                        sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>() },
                    new { id = "RULING-MISSING", scope = "card", question = "待关联问题？", answer = "待关联答案。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "管理员复核",
                        recordedAt = "2026-09-28", status = "pending", cardIds = Array.Empty<string>(),
                        productIds = new[] { "SHOULD-BE-CLEARED" }, tags = Array.Empty<string>(), topics = new[] { "effects-stack" },
                        sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>() },
                },
            });
            legacyStore.SetContent("rules.rulings", stale);

            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards,
                officialCardProducts: catalog.CardProducts);
            var fallback = JsonDocument.Parse(store.GetContent("rules.rulings"));
            Assert.Equal(new[] { "第2季|伟大试炼", "第2季|典藏版" }, Products(fallback, "RULING-SINGLE"));

            var admin = store.Login("Admin", "L12master").Account!;
            var saved = store.SaveContentDraft(admin, "rules.rulings", stale);
            using var savedDocument = JsonDocument.Parse(saved.DraftValue);
            Assert.Equal(new[] { "第1季|天御", "第1季|天御·再临", "第1季|典藏版", "第2季|伟大试炼", "第2季|典藏版" },
                Products(savedDocument, "RULING-MULTI"));
            Assert.Empty(Products(savedDocument, "RULING-MISSING"));

            store.PublishRuleItem(admin, new("rules.rulings", "entries", "RULING-SINGLE",
                ExpectedVersion: saved.Version));
            using var published = JsonDocument.Parse(store.GetContentEntry("rules.rulings").PublishedValue);
            Assert.Equal(new[] { "第2季|伟大试炼", "第2季|典藏版" }, Products(published, "RULING-SINGLE"));
            Assert.DoesNotContain("S02\"", store.GetContentEntry("rules.rulings").PublishedValue);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        static string[] Products(JsonDocument document, string id)
            => document.RootElement.GetProperty("entries").EnumerateArray()
                .Single(row => row.GetProperty("id").GetString() == id).GetProperty("productIds")
                .EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    [Fact]
    public void RuleCenterPublishDoesNotExposePendingEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-center-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            Assert.True(store.IsContentKeyAllowed("rules.center"));
            var draft = JsonSerializer.Serialize(new
            {
                coreBlocks = new[] { new { page = "1", text = "核心规则测试文本" } },
                quickStart = Array.Empty<object>(), terms = Array.Empty<object>(), versions = Array.Empty<object>(),
                tournament = new object[]
                {
                    new { id = "tournament-pending", title = "待发布赛制", body = "不能公开。", sourceRef = "内部草稿",
                        tags = Array.Empty<string>(), status = "pending" },
                    new { id = "tournament-published", title = "已发布赛制", body = "可以公开。", sourceRef = "管理员审核",
                        tags = Array.Empty<string>(), status = "published" },
                },
            });
            store.SaveContentDraft(admin, "rules.center", draft);
            store.PublishContent(admin, "rules.center");
            using var publicDocument = JsonDocument.Parse(store.GetContent("rules.center"));
            var tournament = publicDocument.RootElement.GetProperty("tournament");
            Assert.Single(tournament.EnumerateArray());
            Assert.Equal("tournament-published", tournament[0].GetProperty("id").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleCenterSchemaV2PreservesAllCoreBlocksWhenPageNumbersAreBlank()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-center-v2-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var coreBlocks = Enumerable.Range(1, 123).Select(index => new
            {
                id = $"core-rule-{index:000}",
                page = index <= 98 ? string.Empty : index.ToString(),
                topic = index == 1 ? "介绍" : null,
                chapter = $"章节 {index}",
                text = $"规则正文 {index}",
                status = "published",
            }).ToArray();
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                coreBlocks,
                quickStart = Array.Empty<object>(),
                terms = Array.Empty<object>(),
                tournament = Array.Empty<object>(),
                versions = Array.Empty<object>(),
            });

            store.SaveContentDraft(admin, "rules.center", draft);
            store.PublishContent(admin, "rules.center");

            using var published = JsonDocument.Parse(store.GetContent("rules.center"));
            var rows = published.RootElement.GetProperty("coreBlocks").EnumerateArray().ToArray();
            Assert.Equal(123, rows.Length);
            Assert.Equal(98, rows.Count(row => row.GetProperty("page").GetString() == string.Empty));
            Assert.Equal("core-rule-001", rows[0].GetProperty("id").GetString());
            Assert.Equal("core-rule-123", rows[^1].GetProperty("id").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleCenterSchemaV2RejectsDuplicateCoreBlockIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-center-duplicate-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                coreBlocks = new[]
                {
                    new { id = "core-rule-001", page = "", text = "第一段", status = "pending" },
                    new { id = "core-rule-001", page = "", text = "第二段", status = "pending" },
                },
                quickStart = Array.Empty<object>(), terms = Array.Empty<object>(),
                tournament = Array.Empty<object>(), versions = Array.Empty<object>(),
            });

            var error = Assert.Throws<ArgumentException>(() => store.SaveContentDraft(admin, "rules.center", draft));
            Assert.Contains("核心规则块 id 重复", error.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleCenterItemsUseMonotonicStableIdsPublishDraftOrderAndDeletePublicSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-center-items-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                coreBlocks = new[] { new { id = "core-rule-001", page = "", chapter = "第一章", text = "第一节", status = "pending" } },
                quickStart = Array.Empty<object>(), terms = Array.Empty<object>(), tournament = Array.Empty<object>(), versions = Array.Empty<object>(),
            });
            var saved = store.SaveContentDraft(admin, "rules.center", initial);
            var created = store.CreateRuleItem(admin, new("rules.center", "coreBlocks", ExpectedVersion: saved.Version));
            Assert.Contains("core-rule-002", created.DraftValue);

            var deletedDraft = store.DeleteRuleItem(admin, new("rules.center", "coreBlocks", "core-rule-002",
                ExpectedVersion: created.Version));
            var createdAgain = store.CreateRuleItem(admin, new("rules.center", "coreBlocks",
                ExpectedVersion: deletedDraft.Version));
            Assert.Contains("core-rule-003", createdAgain.DraftValue);
            Assert.DoesNotContain("core-rule-002", createdAgain.DraftValue);
            Assert.Throws<L12ContentStateConflictException>(() => store.CreateRuleItem(admin,
                new("rules.center", "coreBlocks", ExpectedVersion: saved.Version)));

            var firstPublished = store.PublishRuleItem(admin,
                new("rules.center", "coreBlocks", "core-rule-001", ExpectedVersion: createdAgain.Version));
            var thirdPublished = store.PublishRuleItem(admin,
                new("rules.center", "coreBlocks", "core-rule-003", ExpectedVersion: firstPublished.Version));
            var reorderedDocument = JsonNode.Parse(thirdPublished.DraftValue)!.AsObject();
            var blocks = reorderedDocument["coreBlocks"]!.AsArray();
            var first = blocks[0]!.DeepClone();
            var third = blocks[1]!.DeepClone();
            blocks.Clear();
            blocks.Add(third);
            blocks.Add(first);
            var reordered = store.SaveContentDraft(admin, "rules.center", reorderedDocument.ToJsonString(),
                expectedVersion: thirdPublished.Version);
            var reorderedPublished = store.PublishRuleItem(admin,
                new("rules.center", "coreBlocks", "core-rule-003", ExpectedVersion: reordered.Version));
            using (var publicDocument = JsonDocument.Parse(store.GetContent("rules.center")))
                Assert.Equal(new[] { "core-rule-003", "core-rule-001" }, publicDocument.RootElement
                    .GetProperty("coreBlocks").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());

            store.DeleteRuleItem(admin, new("rules.center", "coreBlocks", "core-rule-003",
                ExpectedVersion: reorderedPublished.Version));
            using var afterDelete = JsonDocument.Parse(store.GetContent("rules.center"));
            Assert.Equal("core-rule-001", Assert.Single(afterDelete.RootElement.GetProperty("coreBlocks").EnumerateArray())
                .GetProperty("id").GetString());
            Assert.Contains(store.ContentBatches(), batch => batch.Action == "rule-item-delete" &&
                batch.SourceBatchId?.EndsWith("/core-rule-003", StringComparison.Ordinal) == true);
            var publicDeleteAudit = Assert.Single(store.AdminAudit("rule-item").Where(item => item.Action == "delete" &&
                item.Target.EndsWith("/core-rule-003", StringComparison.Ordinal)));
            Assert.Equal("admin.content.publish", publicDeleteAudit.Permission);
            Assert.Contains("已复验发布权限", publicDeleteAudit.Comment);

            var reopened = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            var afterRestart = reopened.GetContentEntry("rules.center");
            var fourth = reopened.CreateRuleItem(reopenedAdmin,
                new("rules.center", "coreBlocks", ExpectedVersion: afterRestart.Version));
            Assert.Contains("core-rule-004", fourth.DraftValue);
            Assert.DoesNotContain("core-rule-003", fourth.DraftValue);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleCenterImagesUseManagedRuleMediaReferencesAndHydrateOnlyForPlayers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-center-media-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "rule");
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                coreBlocks = new[] { new { id = "core-rule-001", page = "", chapter = "图示章节", text = "配图规则正文",
                    mediaAssetId = media.Id, status = "pending" } },
                quickStart = Array.Empty<object>(), terms = Array.Empty<object>(), tournament = Array.Empty<object>(), versions = Array.Empty<object>(),
            });
            var saved = store.SaveContentDraft(admin, "rules.center", draft);
            store.PublishRuleItem(admin, new("rules.center", "coreBlocks", "core-rule-001", ExpectedVersion: saved.Version));

            var stored = store.GetContentEntry("rules.center").PublishedValue;
            using (var storedDocument = JsonDocument.Parse(stored))
                Assert.Equal(media.Id, storedDocument.RootElement.GetProperty("coreBlocks")[0]
                    .GetProperty("mediaAssetId").GetString());
            Assert.DoesNotContain("\"image\"", stored);
            using var player = JsonDocument.Parse(store.GetContent("rules.center"));
            var image = player.RootElement.GetProperty("coreBlocks")[0].GetProperty("image");
            Assert.Equal(media.Id, image.GetProperty("id").GetString());
            Assert.Equal(media.DesktopUrl, image.GetProperty("desktopUrl").GetString());
            Assert.Equal(media.MobileUrl, image.GetProperty("mobileUrl").GetString());
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleRulingsSchemaV2RequiresControlledTopics()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-topics-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                entries = new[]
                {
                    new { id = "RULING-TOPIC", scope = "general", question = "测试问题？", answer = "测试答案。",
                        category = "效果与响应", sourceKind = "user-ruling", sourceRef = "测试来源",
                        recordedAt = "2026-09-25", status = "pending", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), topics = new[] { "自由文本主题" },
                        sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>() },
                },
            });

            var error = Assert.Throws<ArgumentException>(() => store.SaveContentDraft(admin, "rules.rulings", draft));
            Assert.Contains("topics 含无效主题", error.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FutureRuleRulingIsAbsentFromPublicProjectionUntilItsEffectiveBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-effective-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var transition = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                entries = new object[]
                {
                    new { id = "RULING-OLD", scope = "general", question = "当前问题？", answer = "当前答案。",
                        category = "效果与响应", sourceKind = "user-ruling", sourceRef = "当前来源",
                        recordedAt = "2026-09-25", status = "published", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = new[] { "当前标签" }, topics = new[] { "effects-stack" },
                        sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>() },
                    new { id = "RULING-FUTURE", scope = "general", question = "FUTURE-SECRET-QUESTION？",
                        answer = "FUTURE-SECRET-ANSWER。", category = "效果与响应", sourceKind = "user-ruling",
                        sourceRef = "FUTURE-SECRET-SOURCE", recordedAt = "2026-09-25",
                        effectiveAt = transition.ToString("O"), status = "published", cardIds = new[] { "FUTURE-CARD" },
                        productIds = new[] { "FUTURE-PRODUCT" }, tags = new[] { "FUTURE-TAG" },
                        topics = new[] { "effects-stack" }, sourceIds = Array.Empty<string>(), supersedes = new[] { "RULING-OLD" } },
                },
            });
            store.SaveContentDraft(admin, "rules.rulings", draft);
            store.PublishContent(admin, "rules.rulings");

            var before = store.PublicContents(["rules.rulings"], transition.AddMilliseconds(-1));
            Assert.Contains("RULING-OLD", before.Values["rules.rulings"]);
            Assert.DoesNotContain("FUTURE-SECRET", before.Values["rules.rulings"]);
            Assert.DoesNotContain("FUTURE-CARD", before.Values["rules.rulings"]);
            Assert.Equal(transition, before.NextRuleTransitionAt);

            var atBoundary = store.PublicContents(["rules.rulings"], transition);
            Assert.Contains("RULING-FUTURE", atBoundary.Values["rules.rulings"]);
            Assert.Contains("RULING-OLD", atBoundary.Values["rules.rulings"]);
            Assert.Null(atBoundary.NextRuleTransitionAt);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleItemPublicationCreatesReadableContentHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-history-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                entries = new[]
                {
                    new { id = "RULING-HISTORY", scope = "card", question = "历史问题？", answer = "历史答案。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "管理员复核",
                        recordedAt = "2026-09-25", status = "pending", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), topics = new[] { "effects-stack" },
                        sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>() },
                },
            });
            var saved = store.SaveContentDraft(admin, "rules.rulings", draft);
            store.PublishRuleItem(admin, new("rules.rulings", "entries", "RULING-HISTORY",
                ExpectedVersion: saved.Version));

            var batch = Assert.Single(store.ContentBatches().Where(item => item.Action == "rule-item-publish"));
            Assert.Equal("rules.rulings/entries/RULING-HISTORY", batch.SourceBatchId);
            var item = Assert.Single(batch.Items);
            Assert.Equal("rules.rulings", item.Key);
            Assert.Contains("RULING-HISTORY", item.PublishedValue);
            Assert.False(string.IsNullOrWhiteSpace(item.PublishedVersionId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RuleItemPublicationDoesNotPublishSiblingDrafts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rule-item-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = JsonSerializer.Serialize(new
            {
                entries = new[]
                {
                    new { id = "RULING-ONE", scope = "card", question = "第一条？", answer = "第一条裁定。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "管理员复核",
                        recordedAt = "2026-09-19", status = "pending", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), sourceIds = Array.Empty<string>(),
                        supersedes = Array.Empty<string>() },
                    new { id = "RULING-TWO", scope = "card", question = "第二条？", answer = "第二条裁定。",
                        category = "单卡裁定", sourceKind = "user-ruling", sourceRef = "管理员复核",
                        recordedAt = "2026-09-19", status = "pending", cardIds = Array.Empty<string>(),
                        productIds = Array.Empty<string>(), tags = Array.Empty<string>(), sourceIds = Array.Empty<string>(),
                        supersedes = Array.Empty<string>() },
                },
            });
            store.SaveContentDraft(admin, "rules.rulings", draft);
            store.PublishRuleItem(admin, new("rules.rulings", "entries", "RULING-ONE"));

            using var publicDocument = JsonDocument.Parse(store.GetContent("rules.rulings"));
            var publicEntries = publicDocument.RootElement.GetProperty("entries");
            Assert.Single(publicEntries.EnumerateArray());
            Assert.Equal("RULING-ONE", publicEntries[0].GetProperty("id").GetString());
            using var savedDraft = JsonDocument.Parse(store.GetContentEntry("rules.rulings").DraftValue);
            Assert.Equal("published", savedDraft.RootElement.GetProperty("entries")[0].GetProperty("status").GetString());
            Assert.Equal("pending", savedDraft.RootElement.GetProperty("entries")[1].GetProperty("status").GetString());

            var centerDraft = JsonSerializer.Serialize(new
            {
                coreBlocks = Array.Empty<object>(), quickStart = Array.Empty<object>(), tournament = Array.Empty<object>(),
                versions = Array.Empty<object>(),
                terms = new[]
                {
                    new { id = "term-one", title = "术语一", body = "术语一说明。", sourceRef = "管理员复核",
                        tags = Array.Empty<string>(), status = "pending" },
                    new { id = "term-two", title = "术语二", body = "术语二说明。", sourceRef = "管理员复核",
                        tags = Array.Empty<string>(), status = "pending" },
                },
            });
            store.SaveContentDraft(admin, "rules.center", centerDraft);
            store.PublishRuleItem(admin, new("rules.center", "terms", "term-one"));
            using var centerPublic = JsonDocument.Parse(store.GetContent("rules.center"));
            var publicTerms = centerPublic.RootElement.GetProperty("terms");
            Assert.Single(publicTerms.EnumerateArray());
            Assert.Equal("term-one", publicTerms[0].GetProperty("id").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UploadedMediaUsesContentHashesAndCannotBeDeletedWhileAnyVersionReferencesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-content-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var media = Upload(store, admin, "hero");

            Assert.Equal(64, media.ContentHash.Length);
            Assert.True(media.IndependentVariants);
            Assert.NotEqual(media.ContentHash, media.DesktopUrl.Split('/').Last().Replace(".webp", ""));
            var fileName = media.DesktopUrl.Split('/').Last();
            var file = store.ResolveSiteMediaFile(media.Id, "desktop", fileName);
            Assert.NotNull(file);
            Assert.True(File.Exists(file!.Path));

            var noticeArticle = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "首页通知资讯", "摘要", "正文",
                "官方公告", "", "", "home-notice", false, null));
            store.PublishArticle(admin, noticeArticle.Id);

            var composition = JsonSerializer.Serialize(new
            {
                version = 1,
                heroSlides = new[] { new { id = "launch", eyebrow = "STC-01", title = "启航", summary = "第一行\n第二行", footer = "2026.09.05 发布", href = "/battle", mediaAssetId = media.Id, enabled = true } },
                notices = new[] { new { id = "notice", label = "公告", href = $"/news#article-{noticeArticle.Id}", enabled = true } },
            });
            store.SaveContentDraft(admin, L12PlatformStore.HomeCompositionContentKey, composition);
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));
            store.PublishContent(admin, L12PlatformStore.HomeCompositionContentKey);

            var reloaded = new L12PlatformStore(path);
            var home = reloaded.PublicSiteHome();
            Assert.Equal(media.Id, Assert.Single(home.Media).Id);
            Assert.Contains("launch", home.Composition);
            using (var homeJson = JsonDocument.Parse(home.Composition))
            {
                var slide = homeJson.RootElement.GetProperty("heroSlides")[0];
                Assert.Equal("第一行\n第二行", slide.GetProperty("summary").GetString());
                Assert.Equal("2026.09.05 发布", slide.GetProperty("footer").GetString());
            }
            Assert.Throws<L12SiteContentConflictException>(() => reloaded.DeleteSiteMedia(admin, media.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProductsReuseArticlePublishingAndNonEmptyCategoryRequiresAtomicMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-category-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var source = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(null, "product", "限定商品", "limited", 90, true));
            var target = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(null, "product", "常规商品", "regular", 91, true));
            Assert.Throws<ArgumentException>(() => store.SaveSiteCategory(admin,
                new L12SiteCategoryDraft(null, "unknown", "错误分类", "invalid", 92, true)));
            var media = Upload(store, admin, "product");
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "测试商品", "摘要", "商品正文",
                source.Name, "", "/cards", "test-product", false, null, Kind: "product",
                CategoryId: source.Id, MediaAssetId: media.Id, SortOrder: 4));
            store.PublishArticle(admin, draft.Id);

            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteCategory(admin, source.Id, null));
            store.DeleteSiteCategory(admin, source.Id, target.Id);
            var published = Assert.Single(store.PublicArticles(kind: "product"));
            Assert.Equal(target.Id, published.CategoryId);
            Assert.Equal(target.Name, published.Category);
            Assert.DoesNotContain(store.AdminSiteCategories("product"), item => item.Id == source.Id);

            var renamed = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(target.Id, target.Kind,
                "常规商品（更新）", target.Slug, target.SortOrder, true, target.Version));
            Assert.Equal(renamed.Name, Assert.Single(store.PublicArticles(kind: "product")).Category);
            Assert.All(store.ArticleRevisions(draft.Id), revision => Assert.Equal(renamed.Name, revision.Category));

            var disabled = store.SaveSiteCategory(admin, new L12SiteCategoryDraft(renamed.Id, renamed.Kind,
                renamed.Name, renamed.Slug, renamed.SortOrder, false, renamed.Version));
            Assert.False(disabled.Active);
            var changed = store.SaveArticleDraft(admin, new L12ArticleDraft(draft.Id, "测试商品", "摘要", "修改正文",
                target.Name, "", "/cards", "test-product", false, null, published.Revision, "product",
                target.Id, media.Id, 4));
            Assert.Throws<ArgumentException>(() => store.PublishArticle(admin, changed.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UploadRejectsActiveFormatsAndWrongDerivativeDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-upload-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var policies = L12PlatformStore.SiteMediaPolicies();
            var videoPolicy = policies.Single(item => item.Kind == "video");
            Assert.Equal((1280, 720, 1280, 720, 480, 270), (videoPolicy.DesktopWidth, videoPolicy.DesktopHeight,
                videoPolicy.MobileWidth, videoPolicy.MobileHeight, videoPolicy.ThumbnailWidth, videoPolicy.ThumbnailHeight));
            var productPolicy = policies.Single(item => item.Kind == "product");
            Assert.Equal((1600, 1200, 1200, 900, 480, 360), (productPolicy.DesktopWidth, productPolicy.DesktopHeight,
                productPolicy.MobileWidth, productPolicy.MobileHeight, productPolicy.ThumbnailWidth, productPolicy.ThumbnailHeight));
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            Assert.Equal((1600, 900, 1280, 720, 480, 270), (policy.DesktopWidth, policy.DesktopHeight,
                policy.MobileWidth, policy.MobileHeight, policy.ThumbnailWidth, policy.ThumbnailHeight));
            var invalidOriginal = new L12SiteMediaUpload("news", "cover.svg", "image/svg+xml",
                Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>"),
                Webp(policy.DesktopWidth, policy.DesktopHeight), Webp(policy.MobileWidth, policy.MobileHeight),
                Webp(policy.ThumbnailWidth, policy.ThumbnailHeight), "封面", .5, .5);
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, invalidOriginal));

            var arbitraryDimensions = invalidOriginal with
            {
                OriginalFileName = "cover.webp", OriginalContentType = "image/webp",
                Original = Webp(800, 600), DesktopWebp = Webp(800, 600),
            };
            var arbitraryMedia = store.UploadSiteMedia(admin, arbitraryDimensions);
            Assert.Equal((800, 600), (arbitraryMedia.DesktopWidth, arbitraryMedia.DesktopHeight));
            Assert.All(policies, item => Assert.True(item.FlexibleDimensions));

            var articlePolicy = policies.Single(item => item.Kind == "article");
            Assert.True(articlePolicy.FlexibleDimensions);
            var flexible = store.UploadSiteMedia(admin, new L12SiteMediaUpload("article", "inline.webp", "image/webp",
                Webp(997, 331), Webp(997, 331), Webp(421, 777), Webp(137, 59), "任意比例正文插图", .5, .5));
            Assert.Equal((997, 331, 421, 777, 137, 59), (flexible.DesktopWidth, flexible.DesktopHeight,
                flexible.MobileWidth, flexible.MobileHeight, flexible.ThumbnailWidth, flexible.ThumbnailHeight));

        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IccExifOrientationOriginalIsAcceptedAndDeliveryMetadataIsStrippedByRiffChunks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-metadata-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            var original = Convert.FromBase64String(RealJpegWithIccAndExifOrientation6);
            Assert.True(ContainsBytes(original, "ICC_PROFILE"u8));
            Assert.True(ContainsBytes(original, "Exif"u8));
            var desktopWithMetadata = WebpWithMetadata(policy.DesktopWidth, policy.DesktopHeight);
            var mobileWithPayloadMarkers = WebpWithImagePayloadMarkers(policy.MobileWidth, policy.MobileHeight);
            var media = store.UploadSiteMedia(admin, new L12SiteMediaUpload("news", "orientation-6.jpg", "image/jpeg",
                original, desktopWithMetadata, mobileWithPayloadMarkers,
                Webp(policy.ThumbnailWidth, policy.ThumbnailHeight), "带方向与色彩配置的原图", .5, .5));

            Assert.Equal("image/jpeg", media.OriginalFormat);
            var desktop = ReadMedia(store, media, "desktop", media.DesktopUrl);
            Assert.DoesNotContain("ICCP", WebpChunkTypes(desktop));
            Assert.DoesNotContain("EXIF", WebpChunkTypes(desktop));
            Assert.DoesNotContain("XMP ", WebpChunkTypes(desktop));
            Assert.Equal(0, desktop[20] & 0x2e);
            Assert.True(desktop.Length < desktopWithMetadata.Length);

            var mobile = ReadMedia(store, media, "mobile", media.MobileUrl);
            Assert.Contains("VP8 ", WebpChunkTypes(mobile));
            Assert.True(ContainsBytes(mobile, "pixel-EXIF-ICCP-XMP -bytes"u8));
            Assert.DoesNotContain("EXIF", WebpChunkTypes(mobile));
            Assert.DoesNotContain("ICCP", WebpChunkTypes(mobile));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HeroRequiresThreeIndependentVariantsAllowsArbitraryDimensionsAndCreatesOneAtomicMediaGroup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-hero-group-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "hero");
            var legacyStyleUpload = new L12SiteMediaUpload("hero", "desktop.png", "image/png",
                PngSignature(), Webp(policy.DesktopWidth, policy.DesktopHeight),
                Webp(policy.MobileWidth, policy.MobileHeight), Webp(policy.ThumbnailWidth, policy.ThumbnailHeight),
                "同一原图", .5, .5);
            Assert.Throws<ArgumentException>(() => store.UploadSiteMedia(admin, legacyStyleUpload));
            Assert.Empty(store.AdminSiteMedia("hero"));

            var arbitraryThirdVariant = legacyStyleUpload with
            {
                IndependentVariants = true,
                DesktopAltText = "桌面独立构图", MobileAltText = "移动独立构图", ThumbnailAltText = "缩略独立构图",
                ThumbnailWebp = Webp(320, 180),
            };
            var media = store.UploadSiteMedia(admin, arbitraryThirdVariant);
            Assert.True(media.IndependentVariants);
            Assert.Equal("桌面独立构图", media.DesktopAltText);
            Assert.Equal("移动独立构图", media.MobileAltText);
            Assert.Equal("缩略独立构图", media.ThumbnailAltText);
            Assert.Equal((320, 180), (media.ThumbnailWidth, media.ThumbnailHeight));
            Assert.Equal(media.Id, Assert.Single(store.AdminSiteMedia("hero")).Id);
            Assert.NotNull(store.ResolveSiteMediaFile(media.Id, "desktop", media.DesktopUrl.Split('/').Last()));
            Assert.NotNull(store.ResolveSiteMediaFile(media.Id, "mobile", media.MobileUrl.Split('/').Last()));
            Assert.NotNull(store.ResolveSiteMediaFile(media.Id, "thumbnail", media.ThumbnailUrl.Split('/').Last()));
            store.DeleteSiteMedia(admin, media.Id);
            Assert.Empty(store.AdminSiteMedia("hero"));
            Assert.All(Directory.EnumerateFiles(Path.Combine(root, "site-media")), file => Assert.DoesNotContain(".upload", file));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StructuredNewsBodyUsesWhitelistEmbedsMediaAndKeepsPlainTextCompatible()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-article-blocks-{Guid.NewGuid():N}");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var publicDeck = store.PublishDeck(admin.Id, catalog.PresetDecks.First(), null)!;
            var articleCardId = catalog.PresetDecks.First().CardIds.First();
            var media = Upload(store, admin, "article");
            var body = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new object[]
                {
                    new { id = "heading", type = "h2", text = "正式标题", align = "center", marks = Array.Empty<object>() },
                    new { id = "paragraph", type = "paragraph", text = "访问官网", align = "justify", marks = new[] { new { type = "bold", from = 0, to = 2, href = (string?)null }, new { type = "underline", from = 0, to = 2, href = (string?)null }, new { type = "strikethrough", from = 1, to = 2, href = (string?)null }, new { type = "link", from = 2, to = 4, href = (string?)"https://example.com" } } },
                    new { id = "card", type = "card", cardId = articleCardId, caption = "适合在对手展开后使用" },
                    new { id = "public-deck", type = "publicDeck", reference = publicDeck.PublicCode },
                    new { id = "divider", type = "divider" },
                    new { id = "image", type = "image", mediaAssetId = media.Id, alt = "正文插图内容", caption = "图片说明" },
                },
            });
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "结构化资讯", "摘要", body,
                "官方公告", "", "", "structured-news", false, null));
            Assert.Equal(media.Id, Assert.Single(draft.BodyMedia!).Id);
            var published = store.PublishArticle(admin, draft.Id);
            Assert.Equal(media.Id, Assert.Single(published.BodyMedia!).Id);
            using (var publishedBody = JsonDocument.Parse(published.Body))
                Assert.Equal("适合在对手展开后使用", publishedBody.RootElement.GetProperty("blocks")[2].GetProperty("caption").GetString());
            using (var publicBody = JsonDocument.Parse(Assert.Single(store.PublicArticles(kind: "news")).Body))
                Assert.Equal("适合在对手展开后使用", publicBody.RootElement.GetProperty("blocks")[2].GetProperty("caption").GetString());
            Assert.Throws<L12SiteContentConflictException>(() => store.DeleteSiteMedia(admin, media.Id));

            var unsafeLink = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "unsafe", type = "paragraph", text = "危险链接", marks = new[] { new { type = "link", from = 0, to = 4, href = "javascript:alert(1)" } } } },
            });
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法链接", "", unsafeLink, "官方公告", "", "", "unsafe-link", false, null)));
            var invalidAlign = "{\"format\":\"l12-blocks\",\"version\":1,\"blocks\":[{\"id\":\"align\",\"type\":\"paragraph\",\"text\":\"正文\",\"align\":\"absolute\",\"marks\":[]}]}";
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法对齐", "", invalidAlign, "官方公告", "", "", "unsafe-align", false, null)));
            var htmlBlock = "{\"format\":\"l12-blocks\",\"version\":1,\"blocks\":[{\"id\":\"html\",\"type\":\"html\",\"text\":\"<script>alert(1)</script>\",\"marks\":[]}]}";
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法 HTML 块", "", htmlBlock, "官方公告", "", "", "unsafe-html", false, null)));

            var unknownCard = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "unknown-card", type = "card", cardId = "S99-9999" } },
            });
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "未知单卡", "", unknownCard, "官方公告", "", "", "unknown-card", false, null)));
            var cardWithExtraField = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "extra-card", type = "card", cardId = articleCardId, privateNote = "不得保存" } },
            });
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "单卡额外字段", "", cardWithExtraField, "官方公告", "", "", "extra-card", false, null)));
            var maxCaptionBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "max-caption", type = "card", cardId = articleCardId, caption = new string('注', 500) } },
            });
            var maxCaptionDraft = store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "五百字单卡备注", "", maxCaptionBody, "官方公告", "", "", "max-card-caption", false, null));
            using (var maxCaptionDocument = JsonDocument.Parse(maxCaptionDraft.Body))
                Assert.Equal(500, maxCaptionDocument.RootElement.GetProperty("blocks")[0].GetProperty("caption").GetString()!.Length);
            var missingCaptionBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "missing-caption", type = "card", cardId = articleCardId } },
            });
            var missingCaptionDraft = store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "无单卡备注", "", missingCaptionBody, "官方公告", "", "", "missing-card-caption", false, null));
            using (var missingCaptionDocument = JsonDocument.Parse(missingCaptionDraft.Body))
                Assert.False(missingCaptionDocument.RootElement.GetProperty("blocks")[0].TryGetProperty("caption", out _));
            foreach (var invalidCaption in new[] { new string('注', 501), "非法\u0001备注" })
            {
                var invalidCardCaption = JsonSerializer.Serialize(new
                {
                    format = "l12-blocks", version = 1,
                    blocks = new[] { new { id = "card-caption", type = "card", cardId = articleCardId, caption = invalidCaption } },
                });
                Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                    "非法单卡备注", "", invalidCardCaption, "官方公告", "", "", "invalid-card-caption", false, null)));
            }
            foreach (var invalidCaption in new object?[] { 42, new { note = "对象" }, null })
            {
                var invalidCardCaption = JsonSerializer.Serialize(new
                {
                    format = "l12-blocks", version = 1,
                    blocks = new object[] { new { id = "typed-card-caption", type = "card", cardId = articleCardId, caption = invalidCaption } },
                });
                Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                    "非文本单卡备注", "", invalidCardCaption, "官方公告", "", "", "typed-card-caption", false, null)));
            }
            foreach (var invalidReference in new[] { $"/decks/{publicDeck.PublicCode}", "PRIVATE-ID", "23456789ABCE" })
            {
                var unsafeDeck = JsonSerializer.Serialize(new
                {
                    format = "l12-blocks", version = 1,
                    blocks = new[] { new { id = "unsafe-deck", type = "publicDeck", reference = invalidReference } },
                });
                Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                    "非法公开牌库", "", unsafeDeck, "官方公告", "", "", "unsafe-public-deck", false, null)));
            }

            var blankIdBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = "   ", type = "paragraph", text = "正文", align = "left", marks = Array.Empty<object>() } },
            });
            var blankIdError = Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "空块标识", "", blankIdBody, "官方公告", "", "", "blank-block-id", false, null)));
            Assert.Equal("正文内容块缺少标识或标识为空", blankIdError.Message);

            var longIdBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new[] { new { id = new string('x', 81), type = "h3", text = "小标题", align = "left", marks = Array.Empty<object>() } },
            });
            var longIdError = Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "超长块标识", "", longIdBody, "官方公告", "", "", "long-block-id", false, null)));
            Assert.Equal("正文内容块标识不能超过 80 个字符", longIdError.Message);

            var duplicateIdBody = JsonSerializer.Serialize(new
            {
                format = "l12-blocks", version = 1,
                blocks = new object[]
                {
                    new { id = "same", type = "paragraph", text = "第一段", align = "left", marks = Array.Empty<object>() },
                    new { id = "same", type = "divider" },
                },
            });
            var duplicateIdError = Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "重复块标识", "", duplicateIdBody, "官方公告", "", "", "duplicate-block-id", false, null)));
            Assert.Equal("正文内容块标识不能重复：same", duplicateIdError.Message);

            var legacy = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "旧正文", "", "第一段\n第二行",
                "官方公告", "", "", "legacy-body", false, null));
            Assert.Equal("第一段\n第二行", store.PublishArticle(admin, legacy.Id).Body);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void VideoDraftKeepsOnlyCoverTitleAuthorAndLinkWhileLegacyAuthorMayStayEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-video-model-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var category = store.AdminSiteCategories("video").First();
            var media = Upload(store, admin, "video");
            var draft = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "视频标题", "应被清空的摘要", "应被清空的正文",
                category.Name, "", "https://example.com/video", "ignored-video-slug", false, null,
                Kind: "video", CategoryId: category.Id, MediaAssetId: media.Id));
            Assert.Empty(draft.Summary);
            Assert.Empty(draft.Body);
            Assert.Throws<ArgumentException>(() => store.PublishArticle(admin, draft.Id));

            var authored = store.SaveArticleDraft(admin, new L12ArticleDraft(draft.Id, draft.Title, "仍会清空", "仍会清空",
                category.Name, "", draft.Link, draft.Slug, false, null, draft.Revision, "video", category.Id,
                media.Id, VideoAuthorName: "十二军团频道"));
            var published = store.PublishArticle(admin, authored.Id);
            Assert.Equal("十二军团频道", published.VideoAuthorName);
            Assert.Empty(published.Summary);
            Assert.Empty(published.Body);
            Assert.Equal("https://example.com/video", published.Link);

            var legacy = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "旧视频", "", "", category.Name,
                "", "/legacy-video", "legacy-video", false, null, Kind: "video", CategoryId: category.Id,
                MediaAssetId: media.Id));
            MarkVideoAuthorOptionalForLegacyFixture(store, legacy.Id);
            Assert.Empty(store.PublishArticle(admin, legacy.Id).VideoAuthorName);
            Assert.Throws<ArgumentException>(() => store.SaveArticleDraft(admin, new L12ArticleDraft(null,
                "非法作者", "", "", category.Name, "", "/bad-author", "bad-author", false, null,
                Kind: "video", CategoryId: category.Id, MediaAssetId: media.Id, VideoAuthorName: "作者\n伪造")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PublicContentUsesScheduledTimeAndPinnedThenNewestStableOrdering()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-publish-order-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var olderAt = DateTimeOffset.UtcNow.AddDays(-2);
            var newerAt = DateTimeOffset.UtcNow.AddDays(-1);
            var pinned = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "置顶旧稿", "", "正文", "官方公告",
                "", "", "pinned-old", true, olderAt));
            var older = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "普通旧稿", "", "正文", "官方公告",
                "", "", "normal-old", false, olderAt));
            var newer = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "普通新稿", "", "正文", "官方公告",
                "", "", "normal-new", false, newerAt));
            store.PublishArticle(admin, older.Id);
            store.PublishArticle(admin, newer.Id);
            store.PublishArticle(admin, pinned.Id);
            Assert.Equal(new[] { "置顶旧稿", "普通新稿", "普通旧稿" },
                store.PublicArticles(kind: "news").Select(item => item.Title).ToArray());

            var dueAt = DateTimeOffset.UtcNow.AddMilliseconds(350);
            var due = store.SaveArticleDraft(admin, new L12ArticleDraft(null, "到点公开", "", "正文", "官方公告",
                "", "", "scheduled-due", false, dueAt));
            var scheduled = store.PublishArticle(admin, due.Id);
            Assert.Equal("scheduled", scheduled.Status);
            Assert.Equal(new[] { "置顶旧稿", "到点公开", "普通新稿", "普通旧稿" },
                store.AdminArticles(kind: "news").Select(item => item.Title).ToArray());
            Assert.DoesNotContain(store.PublicArticles(kind: "news"), item => item.Id == due.Id);
            Thread.Sleep(550);
            Assert.Equal(new[] { "置顶旧稿", "到点公开", "普通新稿", "普通旧稿" },
                store.PublicArticles(kind: "news").Select(item => item.Title).ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ArticleBatchProjectionKeepsAllBodiesAndActorNamesAcrossManyAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-article-batch-actors-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var actors = Enumerable.Range(0, 40)
                .Select(index =>
                {
                    var registration = store.Register($"artact{index:00}", "password-123");
                    Assert.True(registration.Success, registration.Message);
                    Assert.NotNull(registration.Account);
                    return registration.Account!;
                })
                .ToArray();
            var expected = new Dictionary<string, (string Author, string UpdatedBy, string? PublishedBy,
                string Body)>(StringComparer.Ordinal);
            for (var index = 0; index < 24; index++)
            {
                var creator = actors[index];
                var updater = actors[index + 1];
                var publisher = actors[index + 2];
                var title = $"批量稿件-{index:00}";
                var body = $"正文-{index:00}-" + new string((char)('甲' + index % 8), 512);
                var created = store.SaveArticleDraft(creator, new L12ArticleDraft(null, title, $"摘要-{index:00}",
                    body, "官方公告", "", "", $"batch-actor-{index:00}", false, null));
                var updated = store.SaveArticleDraft(updater, new L12ArticleDraft(created.Id, created.Title,
                    created.Summary, created.Body, created.Category, created.CoverUrl, created.Link, created.Slug,
                    created.Pinned, created.PublishAt, created.Revision));
                if (index % 2 == 0)
                {
                    store.PublishArticle(publisher, updated.Id);
                    expected[title] = (creator.Username, publisher.Username, publisher.Username, body);
                }
                else expected[title] = (creator.Username, updater.Username, null, body);
            }

            var adminRows = store.AdminArticles(limit: 500, kind: "news").ToDictionary(item => item.Title,
                StringComparer.Ordinal);
            Assert.Equal(expected.Count, adminRows.Count);
            foreach (var (title, values) in expected)
            {
                var row = adminRows[title];
                Assert.Equal(values.Author, row.Author);
                Assert.Equal(values.UpdatedBy, row.UpdatedBy);
                Assert.Equal(values.PublishedBy, row.PublishedBy);
                Assert.Equal(values.Body, row.Body);
            }

            var publicRows = store.PublicArticles(limit: 200, kind: "news").ToDictionary(item => item.Title,
                StringComparer.Ordinal);
            Assert.Equal(expected.Count / 2, publicRows.Count);
            Assert.All(publicRows, pair =>
            {
                var values = expected[pair.Key];
                Assert.Equal(values.Author, pair.Value.Author);
                Assert.Equal(values.PublishedBy, pair.Value.PublishedBy);
                Assert.Equal(values.Body, pair.Value.Body);
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SiteContentHttpMutationsRejectPlayerTokensAndAcceptAdminPermission()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-site-http-{Guid.NewGuid():N}");
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var player = store.Register("SitePlayer", "password-123");
            var admin = store.Login("Admin", "L12master");
            var alternateArt = store.AlternateArts().First();

            using (var request = Authorized(HttpMethod.Post, "/api/admin/alternate-art-grants", player.Token!,
                       new { alternateArtId = alternateArt.Id, accountId = player.Account!.Id, sourceKind = "manual", sourceReference = "越权派发" }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/alternate-art-grants", admin.Token!,
                       new { alternateArtId = alternateArt.Id, accountId = player.Account!.Id, sourceKind = "manual", sourceReference = "普通管理员派发" }))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<L12AlternateArtGrantView>();
                Assert.Equal(player.Account.Id, payload!.AccountId);
            }

            using (var request = Authorized(HttpMethod.Post, "/api/admin/articles", player.Token!,
                       new { title = "越权稿件", body = "不应保存", category = "官方公告", kind = "news" }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/site/categories", player.Token!,
                       new { kind = "news", name = "越权分类", slug = "forbidden", sortOrder = 90, active = true }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/site/media", player.Token!, null))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using (var request = Authorized(HttpMethod.Post, "/api/admin/site/categories", admin.Token!,
                       new { kind = "news", name = "HTTP 分类", slug = "http-category", sortOrder = 90, active = true }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var newsPolicy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "news");
            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Webp(1600, 900),
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight))))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var heroPolicy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == "hero");
            using (var request = AuthorizedMedia(admin.Token!, MediaForm("hero", PngSignature(),
                       Webp(heroPolicy.DesktopWidth, heroPolicy.DesktopHeight),
                       Webp(heroPolicy.MobileWidth, heroPolicy.MobileHeight),
                       Webp(heroPolicy.ThumbnailWidth, heroPolicy.ThumbnailHeight), originalName: "hero.png",
                       originalType: "image/png", independentVariants: true,
                       variantAltTexts: ["桌面API构图", "移动API构图", "缩略API构图"])))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<L12SiteMediaView>();
                Assert.True(payload!.IndependentVariants);
                Assert.Equal("移动API构图", payload.MobileAltText);
            }

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("hero", PngSignature(),
                       Webp(heroPolicy.DesktopWidth, heroPolicy.DesktopHeight),
                       Webp(heroPolicy.MobileWidth, heroPolicy.MobileHeight),
                       Webp(heroPolicy.ThumbnailWidth, heroPolicy.ThumbnailHeight), originalName: "legacy-hero.png",
                       originalType: "image/png")))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Encoding.UTF8.GetBytes("<svg><script/></svg>"),
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight), originalName: "bad.svg", originalType: "image/svg+xml")))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Webp(1600, 900),
                       Webp(800, 600), Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight))))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<L12SiteMediaView>();
                Assert.Equal((800, 600), (payload!.DesktopWidth, payload.DesktopHeight));
            }

            var oversizedOriginal = Pad(Webp(1600, 900), L12PlatformStore.SiteMediaOriginalMaxBytes + 1);
            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", oversizedOriginal,
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight))))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("media_upload_too_large", payload.GetProperty("code").GetString());
                Assert.Contains("16MB", payload.GetProperty("message").GetString());
            }

            using (var request = AuthorizedMedia(admin.Token!, MediaForm("news", Webp(1600, 900),
                       Webp(newsPolicy.DesktopWidth, newsPolicy.DesktopHeight),
                       Webp(newsPolicy.MobileWidth, newsPolicy.MobileHeight),
                       Webp(newsPolicy.ThumbnailWidth, newsPolicy.ThumbnailHeight),
                       extra: new byte[checked((int)L12PlatformStore.SiteMediaRequestMaxBytes)])))
            using (var response = await client.SendAsync(request))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("media_upload_too_large", payload.GetProperty("code").GetString());
                Assert.Contains("32MB", payload.GetProperty("message").GetString());
            }
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin, string kind)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(item => item.Kind == kind);
        var desktop = policy.FlexibleDimensions ? (Width: 997, Height: 331) : (Width: policy.DesktopWidth, Height: policy.DesktopHeight);
        var mobile = policy.FlexibleDimensions ? (Width: 421, Height: 777) : (Width: policy.MobileWidth, Height: policy.MobileHeight);
        var thumbnail = policy.FlexibleDimensions ? (Width: 137, Height: 59) : (Width: policy.ThumbnailWidth, Height: policy.ThumbnailHeight);
        return store.UploadSiteMedia(admin, new L12SiteMediaUpload(kind, $"{kind}.webp", "image/webp",
            Webp(desktop.Width, desktop.Height), Webp(desktop.Width, desktop.Height),
            Webp(mobile.Width, mobile.Height), Webp(thumbnail.Width, thumbnail.Height),
            $"{policy.Label}测试图", .5, .5, $"{policy.Label}桌面测试图", $"{policy.Label}移动测试图",
            $"{policy.Label}缩略测试图", kind == "hero"));
    }

    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        var encodedWidth = width - 1; var encodedHeight = height - 1;
        bytes[24] = (byte)encodedWidth; bytes[25] = (byte)(encodedWidth >> 8); bytes[26] = (byte)(encodedWidth >> 16);
        bytes[27] = (byte)encodedHeight; bytes[28] = (byte)(encodedHeight >> 8); bytes[29] = (byte)(encodedHeight >> 16);
        return bytes;
    }

    private static byte[] WebpWithMetadata(int width, int height)
    {
        var bytes = AppendWebpChunk(Webp(width, height), "ICCP", Encoding.ASCII.GetBytes("ICC_PROFILE\0sRGB test profile"));
        bytes = AppendWebpChunk(bytes, "EXIF", ExifOrientation6());
        bytes = AppendWebpChunk(bytes, "XMP ", Encoding.UTF8.GetBytes("<x:xmpmeta>test</x:xmpmeta>"));
        bytes[20] |= 0x2c;
        return bytes;
    }

    private static byte[] WebpWithImagePayloadMarkers(int width, int height)
        => AppendWebpChunk(Webp(width, height), "VP8 ", Encoding.ASCII.GetBytes("pixel-EXIF-ICCP-XMP -bytes"));

    private static byte[] AppendWebpChunk(byte[] source, string type, byte[] payload)
    {
        var paddedLength = payload.Length + (payload.Length & 1);
        var bytes = new byte[source.Length + 8 + paddedLength];
        source.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, source.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(source.Length + 4, 4), (uint)payload.Length);
        payload.CopyTo(bytes, source.Length + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        return bytes;
    }

    private static byte[] ExifOrientation6()
    {
        var bytes = new byte[32];
        Encoding.ASCII.GetBytes("Exif\0\0II").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24, 2), 6);
        return bytes;
    }

    private static IReadOnlyList<string> WebpChunkTypes(byte[] bytes)
    {
        var result = new List<string>();
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            result.Add(Encoding.ASCII.GetString(bytes, offset, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset = checked(offset + 8 + (int)length + ((int)length & 1));
        }
        return result;
    }

    private static byte[] ReadMedia(L12PlatformStore store, L12SiteMediaView media, string variant, string url)
    {
        var file = store.ResolveSiteMediaFile(media.Id, variant, url.Split('/').Last());
        Assert.NotNull(file);
        return File.ReadAllBytes(file!.Path);
    }

    private static bool ContainsBytes(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value)
        => source.IndexOf(value) >= 0;

    private static byte[] PngSignature() => [137, 80, 78, 71, 13, 10, 26, 10];

    // 由 sharp 0.35 生成并复读确认：3×2 JPEG，含标准 sRGB ICC 与 EXIF Orientation=6。
    private const string RealJpegWithIccAndExifOrientation6 = "/9j/4QC8RXhpZgAASUkqAAgAAAAGABIBAwABAAAABgAAABoBBQABAAAAVgAAABsBBQABAAAAXgAAACgBAwABAAAAAgAAABMCAwABAAAAAQAAAGmHBAABAAAAZgAAAAAAAAA4YwAA6AMAADhjAADoAwAABgAAkAcABAAAADAyMTABkQcABAAAAAECAwAAoAcABAAAADAxMDABoAMAAQAAAP//AAACoAQAAQAAAAMAAAADoAQAAQAAAAIAAAAAAAAA/+IB8ElDQ19QUk9GSUxFAAEBAAAB4GxjbXMEIAAAbW50clJHQiBYWVogB+IAAwAUAAkADgAdYWNzcE1TRlQAAAAAc2F3c2N0cmwAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1oYW5keem/Vlo+AbaDI4VVRvdPqgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKZGVzYwAAAPwAAAAkY3BydAAAASAAAAAid3RwdAAAAUQAAAAUY2hhZAAAAVgAAAAsclhZWgAAAYQAAAAUZ1hZWgAAAZgAAAAUYlhZWgAAAawAAAAUclRSQwAAAcAAAAAgZ1RSQwAAAcAAAAAgYlRSQwAAAcAAAAAgbWx1YwAAAAAAAAABAAAADGVuVVMAAAAIAAAAHABzAFIARwBCbWx1YwAAAAAAAAABAAAADGVuVVMAAAAGAAAAHABDAEMAMAAAWFlaIAAAAAAAAPbWAAEAAAAA0y1zZjMyAAAAAAABDD8AAAXd///zJgAAB5AAAP2S///7of///aIAAAPcAADAcVhZWiAAAAAAAABvoAAAOPIAAAOPWFlaIAAAAAAAAGKWAAC3iQAAGNpYWVogAAAAAAAAJKAAAA+FAAC2xHBhcmEAAAAAAAMAAAACZmkAAPKnAAANWQAAE9AAAApb/9sAQwADAgIDAgIDAwMDBAMDBAUIBQUEBAUKBwcGCAwKDAwLCgsLDQ4SEA0OEQ4LCxAWEBETFBUVFQwPFxgWFBgSFBUU/9sAQwEDBAQFBAUJBQUJFA0LDRQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU/8AAEQgAAgADAwEiAAIRAQMRAf/EABUAAQEAAAAAAAAAAAAAAAAAAAAH/8QAHhAAAQQBBQAAAAAAAAAAAAAAAwABAgQFBgcUITH/xAAVAQEBAAAAAAAAAAAAAAAAAAADB//EAB4RAAEBCQAAAAAAAAAAAAAAAAABAgMEBTI0cXKy/9oADAMBAAIRAxEAPwCM7pVAUdXMCsEdcEMdjmgIUWjGLcIHjN0yIiZ3QmCuSuwh9GeUP//Z";

    private static byte[] Pad(byte[] source, long length)
    {
        var bytes = new byte[checked((int)length)];
        source.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent MediaForm(string kind, byte[] original, byte[] desktop,
        byte[] mobile, byte[] thumbnail, string originalName = "cover.webp", string originalType = "image/webp",
        byte[]? extra = null, bool independentVariants = false, string[]? variantAltTexts = null)
    {
        var form = new MultipartFormDataContent($"l12-{Guid.NewGuid():N}");
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent("HTTP 上传测试图"), "altText");
        form.Add(new StringContent((variantAltTexts?.ElementAtOrDefault(0) ?? "HTTP 上传测试图")), "desktopAltText");
        form.Add(new StringContent((variantAltTexts?.ElementAtOrDefault(1) ?? "HTTP 上传测试图")), "mobileAltText");
        form.Add(new StringContent((variantAltTexts?.ElementAtOrDefault(2) ?? "HTTP 上传测试图")), "thumbnailAltText");
        form.Add(new StringContent(independentVariants.ToString()), "independentVariants");
        form.Add(new StringContent("0.5"), "focalX");
        form.Add(new StringContent("0.5"), "focalY");
        AddFile(form, original, "original", originalName, originalType);
        AddFile(form, desktop, "desktop", "desktop.webp", "image/webp");
        AddFile(form, mobile, "mobile", "mobile.webp", "image/webp");
        AddFile(form, thumbnail, "thumbnail", "thumbnail.webp", "image/webp");
        if (extra is not null) AddFile(form, extra, "unused", "oversized.bin", "application/octet-stream");
        return form;
    }

    private static void AddFile(MultipartFormDataContent form, byte[] bytes, string name, string fileName,
        string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(content, name, fileName);
    }

    private static HttpRequestMessage AuthorizedMedia(string token, MultipartFormDataContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/site/media") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, $"site-media-{Guid.NewGuid():N}");
        if (content.Headers.ContentLength > L12PlatformStore.SiteMediaRequestMaxBytes)
            request.Headers.ExpectContinue = true;
        return request;
    }

    private static void MarkVideoAuthorOptionalForLegacyFixture(L12PlatformStore store, string id)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        var rows = (System.Collections.IEnumerable)data.GetType().GetProperty("Articles")!.GetValue(data)!;
        foreach (var row in rows)
        {
            if (!string.Equals((string?)row.GetType().GetProperty("Id")!.GetValue(row), id, StringComparison.Ordinal)) continue;
            row.GetType().GetProperty("VideoAuthorRequired")!.SetValue(row, false);
            return;
        }
        throw new InvalidOperationException("Legacy video fixture was not found.");
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, $"site-{Guid.NewGuid():N}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
