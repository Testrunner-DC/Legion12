using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckSummaryReadPinTests
{
    [Fact]
    public async Task RealSummaryPinFeedsSameActorCurrentWhileCountersOnlyChangeDirectoryGeneration()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        fixture.Observe();
        var first = await Summary(fixture, "source=all&pageSize=100", fixture.Token);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal("no-store", first.CacheControl);
        using var firstJson = JsonDocument.Parse(first.Body);
        var firstItems = firstJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var published = Assert.Single(firstItems.Where(item => item.GetProperty("source").GetString() == "public"));
        var token = published.GetProperty("readToken").GetString();
        Assert.Equal(64, token!.Length);
        Assert.All(firstItems.Where(item => item.GetProperty("source").GetString() == "official"),
            item => Assert.Equal(JsonValueKind.Null, item.GetProperty("readToken").ValueKind));
        var generation = firstJson.RootElement.GetProperty("generation").GetString();
        Assert.Empty(fixture.Expansions);

        using (var current = await fixture.Get("current?expectedReadToken=" + token, fixture.Token))
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Single(fixture.Expansions);
        fixture.Expansions.Clear();
        using (var counter = await fixture.Client.PostAsync(
            $"/api/public-decks/{fixture.Published.PublicCode}/counters/view", null))
            Assert.Equal(HttpStatusCode.OK, counter.StatusCode);

        var second = await Summary(fixture, "source=all&pageSize=100", fixture.Token);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        using var secondJson = JsonDocument.Parse(second.Body);
        var secondPublished = Assert.Single(secondJson.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("source").GetString() == "public"));
        Assert.NotEqual(generation, secondJson.RootElement.GetProperty("generation").GetString());
        Assert.Equal(token, secondPublished.GetProperty("readToken").GetString());
        Assert.Equal(1, secondPublished.GetProperty("views").GetInt32());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void GuideVersionCatalogAndPolicyEachAdvanceTheContentPinWithoutExpandingBodies()
    {
        using var fixture = new PublicDeckSummaryFixture();
        var published = fixture.Publish("pin-v1");
        var query = new L12PublicDeckSummaryQuery(Source: "public");
        fixture.Observe();
        var baseline = fixture.Page(query, fixture.Viewer);
        var baselineItem = Assert.Single(baseline.Items);
        Assert.Equal(64, baselineItem.ReadToken!.Length);

        fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, published.Id,
            new(new("guide changed", "", "", "", ""), []));
        fixture.Expansions.Clear();
        var guided = fixture.Page(query, fixture.Viewer);
        Assert.Equal(baseline.Generation, guided.Generation);
        Assert.NotEqual(baselineItem.ReadToken, Assert.Single(guided.Items).ReadToken);
        Assert.Empty(fixture.Expansions);

        var source = fixture.Source;
        fixture.Store.PublishDeck(fixture.Owner.Id, new L12PresetDeckDefinition
        {
            Name = "pin-v2", MasterId = source.MasterId, CardIds = [.. source.CardIds],
            MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
        }, published.Id);
        fixture.Expansions.Clear();
        var versioned = fixture.Page(query, fixture.Viewer);
        Assert.NotEqual(guided.Generation, versioned.Generation);
        Assert.NotEqual(guided.Items[0].ReadToken, versioned.Items[0].ReadToken);
        Assert.Empty(fixture.Expansions);

        fixture.Configure(config => config with { CardRestrictions = [new(source.CardIds[0], 0)] });
        fixture.Expansions.Clear();
        var policy = fixture.Page(query, fixture.Viewer);
        Assert.NotEqual(versioned.PolicyVersion, policy.PolicyVersion);
        Assert.NotEqual(versioned.Items[0].ReadToken, policy.Items[0].ReadToken);
        Assert.Empty(fixture.Expansions);

        var pools = Assert.IsAssignableFrom<IDictionary<string, string?>>(fixture.Catalog.CardPools);
        pools[source.MasterId] = "S03";
        var catalog = fixture.Page(query, fixture.Viewer);
        Assert.NotEqual(policy.CatalogVersion, catalog.CatalogVersion);
        Assert.NotEqual(policy.Items[0].ReadToken, catalog.Items[0].ReadToken);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void SessionAndPermissionChangesAdvancePinsAndRejectStaleActor()
    {
        using var fixture = new PublicDeckSummaryFixture();
        fixture.Publish("actor-pin");
        var query = new L12PublicDeckSummaryQuery(Source: "public");
        var first = Assert.Single(fixture.Page(query, fixture.Viewer).Items).ReadToken;

        var secondLogin = fixture.Store.Login("cq2owner", "password-123");
        Assert.True(secondLogin.Success, secondLogin.Message);
        var secondViewer = fixture.Store.AuthenticateSession("Bearer " + secondLogin.Token)!;
        var second = Assert.Single(fixture.Page(query, secondViewer).Items).ReadToken;
        Assert.NotEqual(first, second);

        var admin = fixture.Store.Login("Admin", "L12master").Account!;
        Assert.True(fixture.Store.SetRole(admin, fixture.Owner.Id, "admin"));
        Assert.Equal("unauthorized", fixture.Store.DeckLibrarySummaries(fixture.Catalog, query, secondViewer).Status);
        var thirdLogin = fixture.Store.Login("cq2owner", "password-123");
        Assert.True(thirdLogin.Success, thirdLogin.Message);
        var thirdViewer = fixture.Store.AuthenticateSession("Bearer " + thirdLogin.Token)!;
        var third = Assert.Single(fixture.Page(query, thirdViewer).Items).ReadToken;
        Assert.NotEqual(second, third);
        fixture.Complete();
    }

    [Fact]
    public void PaginationPinsOnlyReturnedPublicItemsAndOfficialItemsRemainNull()
    {
        using var fixture = new PublicDeckSummaryFixture();
        for (var index = 0; index < 5; index++) fixture.Publish("page-pin-" + index);
        fixture.Observe();
        var pinned = new List<L12PublicDeckSummaryView>();
        for (var page = 1; page <= 3; page++)
        {
            var result = fixture.Page(new(Source: "public", Page: page, PageSize: 2), fixture.Viewer);
            pinned.AddRange(result.Items);
            Assert.All(result.Items, item => Assert.Equal(64, item.ReadToken!.Length));
        }
        Assert.Equal(5, pinned.Count);
        Assert.Empty(fixture.Expansions);
        foreach (var item in pinned)
            Assert.Equal("ok", fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, item.Id,
                item.ReadToken, fixture.Viewer).Status);
        Assert.Equal(5, fixture.Expansions.Count);
        fixture.Expansions.Clear();
        var official = fixture.Page(new(Source: "official", PageSize: 100), fixture.Viewer);
        Assert.NotEmpty(official.Items);
        Assert.All(official.Items, item => Assert.Null(item.ReadToken));
        Assert.Empty(fixture.Expansions);
        var empty = fixture.Page(new(Source: "public", Page: 99, PageSize: 2), fixture.Viewer);
        Assert.Empty(empty.Items);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task OffPageBadHeadDoesNotPinOrExpandUntilThatPageIsRequested()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var source = fixture.Catalog.PresetDecks[0];
        var offPage = fixture.Store.PublishDeck(fixture.Owner.Id, new L12PresetDeckDefinition
        {
            Name = "zz-off-page", MasterId = source.MasterId, CardIds = [.. source.CardIds],
            MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
        }, null)!;
        fixture.Sql($"UPDATE published_decks SET public_code='DRIFTED' WHERE publication_id='{offPage.Id}';");
        fixture.Observe();
        var first = await Summary(fixture, "source=public&sort=name&page=1&pageSize=1", fixture.Token);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        using (var json = JsonDocument.Parse(first.Body))
            Assert.Equal("read-1", Assert.Single(json.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("name").GetString());
        Assert.Empty(fixture.Expansions);
        var second = await Summary(fixture, "source=public&sort=name&page=2&pageSize=1", fixture.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("storage_revision")]
    [InlineData("unknown_format")]
    [InlineData("bad_head")]
    public async Task RealSummaryFailsClosedAs503ForDriftUnknownFormatAndBadHead(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        fixture.Observe();
        var query = "source=public&page=999&pageSize=1";
        switch (mode)
        {
            case "storage_revision":
                fixture.Sql("UPDATE platform_state SET storage_revision=storage_revision+1 WHERE singleton_id=1;");
                break;
            case "unknown_format":
                fixture.Sql("UPDATE storage_meta SET value='unknown-v9' WHERE key='deck_payload_format_state';");
                break;
            case "bad_head":
                fixture.Sql("UPDATE published_decks SET public_code='DRIFTED' WHERE is_deleted=0;");
                query = "source=public&page=1&pageSize=1";
                break;
        }
        var response = await Summary(fixture, query, fixture.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task ConcurrentGuideCommitProducesOnlyCommittedPinsAndRestartReadsTheFinalPin()
    {
        using var fixture = new PublicDeckSummaryFixture();
        var published = fixture.Publish("concurrent-pin");
        var query = new L12PublicDeckSummaryQuery(Source: "public");
        var before = Assert.Single(fixture.Page(query, fixture.Viewer).Items).ReadToken!;
        using var release = new ManualResetEventSlim();
        var readers = Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            release.Wait();
            return Assert.Single(fixture.Page(query, fixture.Viewer).Items).ReadToken!;
        })).ToArray();
        var writer = Task.Run(() =>
        {
            release.Wait();
            fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, published.Id,
                new(new("committed guide", "", "", "", ""), []));
        });
        release.Set();
        await writer;
        var observed = await Task.WhenAll(readers);
        var after = Assert.Single(fixture.Page(query, fixture.Viewer).Items).ReadToken!;
        Assert.NotEqual(before, after);
        Assert.All(observed, token => Assert.Contains(token, new[] { before, after }));

        var restarted = new L12PlatformStore(fixture.PathName, officialCards: fixture.Catalog.Cards);
        var restartedResult = restarted.DeckLibrarySummaries(fixture.Catalog, query, fixture.Viewer);
        Assert.Equal("ok", restartedResult.Status);
        Assert.Equal(after, Assert.Single(restartedResult.Page!.Items).ReadToken);
        fixture.Complete();
    }

    [Fact]
    public void SummaryAndReferenceCallersReuseOnePrecomputedCatalogVersion()
    {
        var detail = File.ReadAllText(RepositoryFile("服务端WebSocket", "TwelveLegions",
            "L12PlatformStore.PublicDeckDetailQueries.cs"));
        var summary = File.ReadAllText(RepositoryFile("服务端WebSocket", "TwelveLegions",
            "L12PlatformStore.PublicDeckSummaryQueries.cs"));
        var references = File.ReadAllText(RepositoryFile("服务端WebSocket", "TwelveLegions",
            "L12PlatformStore.PublicDeckReferenceQueries.cs"));
        Assert.Contains("string? frozenCatalogVersion = null", detail, StringComparison.Ordinal);
        Assert.Contains("frozenCatalogVersion ?? LibraryCatalogVersion(catalog)", detail, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(summary, "LibraryCatalogVersion(catalog)"));
        Assert.Equal(1, Occurrences(references, "LibraryCatalogVersion(catalog)"));
        Assert.Contains("policy, viewer, catalogVersion", summary, StringComparison.Ordinal);
        Assert.Contains("policy, viewer, catalogVersion", references, StringComparison.Ordinal);
    }

    private static async Task<(HttpStatusCode Status, string Body, string? CacheControl)> Summary(PublicDeckReadFixture fixture,
        string query, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/deck-library/summaries?" + query);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.CacheControl?.ToString());
    }

    private static int Occurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("无法定位仓库源文件", string.Join('/', segments));
    }
}
