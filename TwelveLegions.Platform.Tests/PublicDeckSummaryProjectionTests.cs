using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckSummaryProjectionTests
{
    [Fact]
    public void UnifiedGlobalPagingFiltersAndFacetsAreConsistentBeforeSlicing()
    {
        using var fixture = new PublicDeckSummaryFixture();
        foreach (var name in new[] { "alpha 甲", "ALPHA 乙", "第三", "最后" }) fixture.Publish(name);
        fixture.Observe();
        var all = fixture.Page(new(PageSize: 100, Sort: "name"));
        var first = fixture.Page(new(PageSize: 2, Sort: "name"));
        var second = fixture.Page(new(Page: 2, PageSize: 2, Sort: "name"));
        Assert.Equal(fixture.Catalog.PresetDecks.Count + 4, all.Total);
        Assert.Equal(all.Items.Take(2).Select(item => item.Id), first.Items.Select(item => item.Id));
        Assert.Equal(all.Items.Skip(2).Take(2).Select(item => item.Id), second.Items.Select(item => item.Id));
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal(first.Total, first.Facets.Sources.Sum(facet => facet.Count));
        Assert.Equal(first.Total, first.Facets.Masters.Sum(facet => facet.Count));
        Assert.Equal(first.Total, first.Facets.Legal + first.Facets.Illegal);
        var filtered = fixture.Page(new(Keyword: "alpha", MasterId: fixture.Source.MasterId, Legal: true));
        Assert.Equal(2, filtered.Total);
        Assert.Equal(2, filtered.Facets.Legal);
        Assert.Equal(2, Assert.Single(filtered.Facets.Sources).Count);
        var containing = fixture.Page(new(Source: "public", CardId: fixture.Source.MoraleIds[0]));
        Assert.Equal(4, containing.Total);
        Assert.Empty(fixture.Page(new(Page: int.MaxValue, PageSize: 100)).Items);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void CountOnlyRowsUseSharedLegalityAndNeverExposeBodiesOrOwnerIdentifiers()
    {
        using var fixture = new PublicDeckSummaryFixture();
        fixture.Publish("season");
        fixture.Configure(config => config with { CardRestrictions = [new(fixture.Source.CardIds[0], 0, "CQ2 synthetic")] });
        fixture.Observe();
        var page = fixture.Page(new(Source: "public"));
        var item = Assert.Single(page.Items);
        Assert.False(L12DeckValidator.TryValidatePreset(fixture.Catalog, fixture.Source, out var error,
            fixture.Store.CaptureOperationsPolicy().CardRestrictions));
        Assert.False(item.Legal);
        Assert.Equal(error, item.LegalityReason);
        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var key in new[] { "cardIds", "moraleIds", "specialIds", "benchIds", "alternateArtCopies",
            "alternateArtSelections", "payloadHash", "ownerId", "likedByAccountIds" }) Assert.DoesNotContain('"' + key + '"', json);
        Assert.False(item.ViewerLiked);
        Assert.False(item.CanEdit);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void DisabledPublicFeatureLeavesOfficialSourceExplicitlyAvailable()
    {
        using var fixture = new PublicDeckSummaryFixture();
        fixture.Publish("disabled");
        fixture.Configure(config => config with { FeatureFlags = new Dictionary<string, bool>(config.FeatureFlags) { ["publicDecks"] = false } });
        fixture.Observe();
        Assert.Equal("feature_disabled", fixture.Store.DeckLibrarySummaries(fixture.Catalog, new(Source: "public")).Status);
        var all = fixture.Page();
        Assert.Equal("disabled", all.SourceAvailability.Public);
        Assert.Equal(fixture.Catalog.PresetDecks.Count, all.Total);
        Assert.All(all.Items, item => Assert.Equal("official", item.Source));
        Assert.Equal(all.Total, fixture.Page(new(Source: "official")).Total);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void StableIdsAndLongTrendSortRemainDeterministicForTiesAndHugeCounters()
    {
        using var fixture = new PublicDeckSummaryFixture();
        var one = fixture.Publish("same one");
        var two = fixture.Publish("same two");
        foreach (var row in fixture.PublicRows())
        {
            row.GetType().GetProperty("Name")!.SetValue(row, "same");
            row.GetType().GetProperty("CreatedAt")!.SetValue(row, DateTimeOffset.UnixEpoch);
            row.GetType().GetProperty("UpdatedAt")!.SetValue(row, DateTimeOffset.UnixEpoch);
            row.GetType().GetProperty("Copies")!.SetValue(row, int.MaxValue);
            row.GetType().GetProperty("Views")!.SetValue(row, int.MaxValue);
        }
        fixture.Observe();
        foreach (var sort in new[] { "trend", "name", "copies", "views", "latest", "likes" })
            Assert.Equal(new[] { one.Id, two.Id }.Order(StringComparer.Ordinal),
                fixture.Page(new(Source: "public", Sort: sort)).Items.Select(item => item.Id));
        var originalOfficial = fixture.Page(new(Source: "official", PageSize: 100, Sort: "name"));
        Assert.IsType<List<L12PresetDeckDefinition>>(fixture.Catalog.PresetDecks).Reverse();
        var reorderedOfficial = fixture.Page(new(Source: "official", PageSize: 100, Sort: "name"));
        Assert.Equal(originalOfficial.Items.Select(item => item.Id), reorderedOfficial.Items.Select(item => item.Id));
        Assert.Equal(originalOfficial.Generation, reorderedOfficial.Generation);
        Assert.All(originalOfficial.Items, item => Assert.StartsWith("official:", item.Id));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void CorruptVisibleBodyFailsWithoutFullListFallbackButDisabledBodyIsNotRead()
    {
        using var fixture = new PublicDeckSummaryFixture();
        fixture.Publish("corrupt");
        var row = fixture.PublicRows()[0];
        var property = row.GetType().GetProperty("PayloadHash")!;
        var originalHash = property.GetValue(row);
        property.SetValue(row, "broken-reference");
        fixture.Observe();
        Assert.Throws<InvalidDataException>(() => fixture.Page());
        Assert.Equal(fixture.Catalog.PresetDecks.Count, fixture.Page(new(Source: "official")).Total);
        property.SetValue(row, originalHash);
        fixture.Configure(config => config with { FeatureFlags = config.FeatureFlags.ToDictionary(item => item.Key,
            item => item.Key == "publicDecks" ? false : item.Value) });
        fixture.PublicRows()[0].GetType().GetProperty("PayloadHash")!.SetValue(fixture.PublicRows()[0], "broken-reference");
        Assert.Equal(fixture.Catalog.PresetDecks.Count, fixture.Page().Total);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }
}

internal sealed class PublicDeckSummaryFixture : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "l12-cq2-store-" + Guid.NewGuid().ToString("N"));
    private bool passed;
    public L12Catalog Catalog { get; } = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
    public L12PresetDeckDefinition Source => Catalog.PresetDecks[0];
    public string PathName => Path.Combine(root, "platform.json");
    public L12PlatformStore Store { get; }
    public L12AccountView Owner { get; }
    public L12AuthenticatedSession Viewer { get; }
    public List<string> Expansions { get; } = [];
    public PublicDeckSummaryFixture()
    {
        Directory.CreateDirectory(root);
        Store = new(PathName, officialCards: Catalog.Cards);
        var login = Store.Register("cq2owner", "password-123");
        Assert.True(login.Success, login.Message);
        Owner = login.Account!;
        Viewer = Store.AuthenticateSession("Bearer " + login.Token)!;
    }
    public L12PublishedDeckView Publish(string name) => Store.PublishDeck(Owner.Id, new L12PresetDeckDefinition
    {
        Name = name, MasterId = Source.MasterId, CardIds = [.. Source.CardIds], MoraleIds = [.. Source.MoraleIds],
        SpecialIds = [.. Source.SpecialIds],
    }, null)!;
    public L12PublicDeckSummaryPage Page(L12PublicDeckSummaryQuery? query = null, L12AuthenticatedSession? viewer = null)
    {
        var result = Store.DeckLibrarySummaries(Catalog, query, viewer);
        Assert.Equal("ok", result.Status);
        return result.Page!;
    }
    public void Configure(Func<L12OperationsConfigPayload, L12OperationsConfigPayload> change)
    {
        var admin = Store.Login("Admin", "L12master").Account!;
        var config = Store.OperationsConfig(admin);
        Store.ApplyOperationsConfig(admin, change(config.Config), config.Version, "CQ2 synthetic",
            new L12AdminAuditContext("cq2-config", Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite),
                Reason: "CQ2 synthetic", RequestMethod: "PUT", RequestPath: "/api/admin/operations/config"));
    }
    public object[] PublicRows()
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Store)!;
        return ((IEnumerable)data.GetType().GetProperty("PublishedDecks")!.GetValue(data)!).Cast<object>().ToArray();
    }
    public void Observe() => Store.DeckPayloadExpansionObserver = Expansions.Add;
    public void Complete() => passed = true;
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (passed) Directory.Delete(root, true);
        else Console.WriteLine("Failed synthetic CQ2 store fixture retained: " + root);
    }
}
