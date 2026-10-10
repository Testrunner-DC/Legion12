using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PrivateDeckMetadataLookupTests
{
    [Theory]
    [InlineData("exactName=ALPHA")]
    [InlineData("publicationId=source-reference")]
    public void NarrowMetadataLookupAcceptsExplicitIdentityFilters(string raw)
    {
        var values = new QueryCollection(QueryHelpers.ParseQuery(raw));
        Assert.True(L12PrivateDeckQuery.TryParse(values, out var query));
        Assert.True(query.IsValid);
    }

    [Theory]
    [InlineData("exactName=")]
    [InlineData("exactName=%20%20")]
    [InlineData("exactName=abcdefghijklmnopqrstuvwxyz")]
    [InlineData("exactName=a&exactName=b")]
    [InlineData("publicationId=")]
    [InlineData("publicationId=%20")]
    [InlineData("publicationId=a&publicationId=b")]
    [InlineData("ExactName=a")]
    [InlineData("ownerId=other&exactName=a")]
    public void NarrowLookupRejectsAmbiguousOrMalformedInputs(string raw)
        => Assert.False(L12PrivateDeckQuery.TryParse(new QueryCollection(QueryHelpers.ParseQuery(raw)), out _));

    [Fact]
    public void ExactNameAndPublicationFiltersFindOwnedMetadataBeyondTheVisiblePageWithoutBodies()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        for (var index = 0; index < 35; index++) fixture.Create("filler " + index, source);
        var publication = fixture.Store.PublishDeck(fixture.Owner.Id, Deck(source, "public source"), null)!;
        var target = fixture.Create("ALPHA", source, publication.Id, publication.Deck.PublicationVersion);
        Assert.Equal(publication.Id, target.PublicationId);
        fixture.Store.CreateDeck(fixture.Other.Id, Deck(source, "ALPHA"));
        fixture.Store.DeleteDeck(fixture.Owner.Id, fixture.Create("deleted", source).Id, 1);
        fixture.Observe();
        var exact = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(ExactName: " alpha ", PageSize: 1))!;
        Assert.Equal(target.Id, Assert.Single(exact.Items).Id);
        Assert.Equal(1, exact.Total);
        Assert.Equal(1, exact.Facets.Masters.Sum(item => item.Count));
        var sourceLookup = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(PublicationId: publication.Id, PageSize: 1))!;
        Assert.Equal(target.Id, Assert.Single(sourceLookup.Items).Id);
        Assert.Equal(target.Revision, sourceLookup.Items[0].Revision);
        Assert.Equal(exact.Generation, sourceLookup.Generation);
        Assert.Empty(fixture.Store.PrivateDeckSummaries(fixture.Other, fixture.Catalog,
            new(PublicationId: publication.Id))!.Items);
        Assert.Empty(fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(ExactName: "deleted"))!.Items);
        Assert.Empty(fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(ExactName: "ALPHA", PublicationId: "absent"))!.Items);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void RenamedSourceRemainsDiscoverableAndMultipleBindingsAreReportedNotSilentlyChosen()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        var publication = fixture.Store.PublishDeck(fixture.Owner.Id, Deck(source, "old source"), null)!;
        var version = publication.Deck.PublicationVersion;
        var a = fixture.Create("old private", source, publication.Id, version);
        var renamed = fixture.Store.UpdateDeck(fixture.Owner.Id, a.Id, a.Revision,
            Deck(source, "renamed private", publication.Id, version));
        Assert.True(renamed.Success);
        var b = fixture.Create("second linked", source, publication.Id, version);
        fixture.Observe();
        var result = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(PageSize: 1, PublicationId: publication.Id))!;
        Assert.Equal(2, result.Total);
        Assert.Single(result.Items);
        var renamedResult = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(ExactName: "renamed private", PublicationId: publication.Id))!;
        Assert.Equal(a.Id, Assert.Single(renamedResult.Items).Id);
        Assert.Equal(2, renamedResult.Items[0].Revision);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task RealHttpLookupsAreOwnerScopedThinAndRejectMalformedQueries()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        var target = fixture.Create("HTTP source", source);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        MatchRecorder? recorder = null;
        L12WebSocketServer? server = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            recorder = new MatchRecorder(Path.Combine(fixture.Root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(fixture.Catalog, recorder, fixture.Store), recorder, fixture.Store, fixture.Catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            fixture.Observe();
            using var anonymous = await client.GetAsync("/api/decks/summaries?exactName=HTTP%20source&pageSize=1");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
            using var response = await client.GetAsync("/api/decks/summaries?exactName=HTTP%20source&pageSize=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(target.Id, item.GetProperty("id").GetString());
            Assert.False(item.TryGetProperty("cardIds", out _));
            using var malformed = await client.GetAsync("/api/decks/summaries?publicationId=");
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            Assert.Empty(fixture.Expansions);
            fixture.Complete();
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
        }
    }

    private static L12PresetDeckDefinition Deck(L12PresetDeckDefinition source, string name,
        string? publicationId = null, int? publicationVersion = null) => new()
    {
        Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds], MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds], PublicationId = publicationId, PublicationVersion = publicationVersion,
    };
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "private-meta-" + Guid.NewGuid().ToString("N"));
        public L12Catalog Catalog { get; } = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        public L12PlatformStore Store { get; }
        public L12AccountView Owner { get; }
        public L12AccountView Other { get; }
        public string Token { get; }
        public List<string> Expansions { get; } = [];
        private bool completed;
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new(Path.Combine(Root, "platform.json"), officialCards: Catalog.Cards);
            var owner = Store.Register("metalookown", "password-123");
            var other = Store.Register("metalookoth", "password-123");
            Assert.True(owner.Success, owner.Message); Assert.True(other.Success, other.Message);
            Owner = owner.Account!; Other = other.Account!; Token = owner.Token!;
        }
        public L12AccountDeckView Create(string name, L12PresetDeckDefinition source,
            string? publicationId = null, int? publicationVersion = null)
        {
            var result = Store.CreateDeck(Owner.Id, Deck(source, name, publicationId, publicationVersion));
            Assert.True(result.Success); return result.Deck!;
        }
        public void Observe() => Store.DeckPayloadExpansionObserver = Expansions.Add;
        public void Complete() => completed = true;
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (completed) Directory.Delete(Root, true);
            else Console.WriteLine("Failed private metadata fixture retained: " + Root);
        }
    }
}
