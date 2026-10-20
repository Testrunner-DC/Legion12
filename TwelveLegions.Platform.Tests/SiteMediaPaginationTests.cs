using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SiteMediaPaginationTests
{
    [Fact]
    public void EveryOldAssetIsReachableBeyondBothLegacyCapsWithStableTieOrdering()
    {
        using var fixture = new RankedIdentityFixture();
        Seed(fixture.Store, fixture.Admin.Id);
        Assert.Equal(500, fixture.Store.AdminSiteMedia("card-art").Count);
        Assert.DoesNotContain(fixture.Store.AdminSiteMedia("card-art", 1000), item => item.Id == "media-0000");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;
        while (true)
        {
            var result = fixture.Store.SearchSiteMedia("card-art", page: page, pageSize: 24);
            Assert.Equal(page, result.Page);
            Assert.Equal(1005, result.Total);
            Assert.InRange(result.Items.Count, 1, 24);
            foreach (var item in result.Items) Assert.True(ids.Add(item.Id), "Repeated page boundary");
            if (!result.HasMore) break;
            page++;
            Assert.True(page < 100);
        }
        Assert.Equal(1005, ids.Count);
        Assert.Contains("media-0000", ids);
        Assert.Equal("media-0001", fixture.Store.SearchSiteMedia("card-art").Items[0].Id);
        Assert.Equal("media-0000", Assert.Single(fixture.Store.SearchSiteMedia("card-art", "远古素材").Items).Id);
        Assert.Equal("media-0007", Assert.Single(fixture.Store.SearchSiteMedia("card-art", "HASH-0007").Items).Id);
        Assert.Equal("media-0000", Assert.Single(fixture.Store.SearchSiteMedia("card-art", id: "media-0000").Items).Id);
        Assert.Empty(fixture.Store.SearchSiteMedia(id: "media-000").Items);
        Assert.Equal(2, fixture.Store.SearchSiteMedia(ids: ["media-0000", "media-0007"], pageSize: 100).Items.Count);
        Assert.Empty(fixture.Store.SearchSiteMedia(ids: []).Items);
        Assert.Throws<ArgumentException>(() => fixture.Store.SearchSiteMedia(ids: Enumerable.Repeat("media-0000", 101).ToArray()));
        Assert.Throws<ArgumentException>(() => fixture.Store.SearchSiteMedia(ids: [""]));
        Assert.Throws<ArgumentException>(() => fixture.Store.SearchSiteMedia(id: new string('x', 101)));
        Assert.Empty(fixture.Store.SearchSiteMedia("card-art", "规则图片").Items);
        Assert.Empty(fixture.Store.SearchSiteMedia(search: "media-1011").Items);
        Assert.Equal(6, fixture.Store.SearchSiteMedia("rule").Total);
        Assert.Equal(1, fixture.Store.SearchSiteMedia(page: int.MinValue, pageSize: int.MinValue).Page);
        Assert.Equal(1, fixture.Store.SearchSiteMedia(pageSize: int.MinValue).PageSize);
        Assert.Equal(100, fixture.Store.SearchSiteMedia(pageSize: int.MaxValue).PageSize);
        Assert.Equal(11, fixture.Store.SearchSiteMedia(page: int.MaxValue, pageSize: int.MaxValue).Page);
        Assert.Throws<ArgumentException>(() => fixture.Store.SearchSiteMedia("bad-kind"));
        Assert.Throws<ArgumentException>(() => fixture.Store.SearchSiteMedia(search: new string('a', 161)));
    }

    [Fact]
    public async Task SearchUsesExistingAdminReadPermissionAndKeepsLegacyArrayContract()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.Store.Register("mediatest", "Password123!");
        var admin = fixture.Store.Login("Admin", "L12master");
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "matches.db"));
        await recorder.InitializeAsync();
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
        await using var server = new L12WebSocketServer(new L12RoomManager(fixture.Catalog, recorder, fixture.Store),
            recorder, fixture.Store, fixture.Catalog);
        try
        {
            await server.StartAsync(0);
            Seed(fixture.Store, fixture.Admin.Id);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/site/media/search")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", player.Token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/site/media/search")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
            var result = await client.GetFromJsonAsync<L12SiteMediaSearchView>("/api/admin/site/media/search?kind=card-art&search=media-0000&pageSize=24");
            Assert.Equal("media-0000", Assert.Single(result!.Items).Id);
            var references = await client.GetFromJsonAsync<L12SiteMediaSearchView>("/api/admin/site/media/search?ids=media-0000,media-0007&pageSize=100");
            Assert.Equal(2, references!.Items.Count);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/site/media/search?ids=" + string.Join(',', Enumerable.Repeat("media-0000", 101)))).StatusCode);
            var legacy = await client.GetFromJsonAsync<L12SiteMediaView[]>("/api/admin/site/media?kind=card-art&limit=1");
            Assert.Single(legacy!);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/site/media/search?kind=unknown")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/site/media/search?search=" + new string('x', 161))).StatusCode);
        }
        finally
        {
            await server.StopAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            SqliteConnection.ClearAllPools();
        }
    }

    private static void Seed(L12PlatformStore store, string adminId)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        var rows = (IList)data.GetType().GetProperty("SiteMedia")!.GetValue(data)!;
        var rowType = rows.GetType().GetGenericArguments()[0];
        var now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        for (var index = 0; index < 1012; index++)
        {
            var row = Activator.CreateInstance(rowType, nonPublic: true)!;
            void Set(string key, object value) => rowType.GetProperty(key)!.SetValue(row, value);
            Set("Id", $"media-{index:D4}"); Set("Kind", index < 1005 ? "card-art" : "rule");
            Set("AltText", index == 0 ? "远古素材" : index < 1005 ? $"卡图 {index}" : "规则图片");
            Set("ContentHash", $"hash-{index:D4}"); Set("CreatedByAccountId", adminId);
            Set("CreatedAt", index == 0 ? now.AddYears(-1) : now);
            if (index == 1011) Set("DeletedAt", now);
            rows.Add(row);
        }
    }
}
