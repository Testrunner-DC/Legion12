using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckOwnerReferenceHttpTests
{
    [Fact]
    public async Task OwnerCanResolveOneHundredReferencesBeyondSummaryPagesWithoutBodies()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var ids = new List<string> { fixture.Published.Id };
        var source = fixture.Catalog.PresetDecks[0];
        for (var index = 1; index < L12PublicDeckReferenceQuery.Limit; index++)
        {
            var published = fixture.Store.PublishDeck(fixture.Owner.Id, Deck(source, "reference-" + index), null)!;
            ids.Add(published.Id);
        }
        using (var summaryRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/deck-library/summaries?source=public&pageSize=100"))
        {
            summaryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
            using var summaryResponse = await fixture.Client.SendAsync(summaryRequest);
            Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
            using var pinJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
            var summaryPins = pinJson.RootElement.GetProperty("items").EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetString()!,
                    item => item.GetProperty("readToken").GetString()!, StringComparer.Ordinal);
            Assert.Equal(100, summaryPins.Count);
            foreach (var id in ids)
            {
                var legacy = fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, id, viewer: fixture.Viewer);
                Assert.Equal("ok", legacy.Status);
                Assert.Equal(legacy.Detail!.ReadToken, summaryPins[id]);
            }
        }
        fixture.Observe();
        var response = await References(fixture, ids, fixture.Token);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("available", json.RootElement.GetProperty("status").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(L12PublicDeckReferenceQuery.Limit, items.Length);
        Assert.Equal(ids, items.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(items, item =>
        {
            Assert.Equal(fixture.Owner.Id, item.GetProperty("ownerId").GetString());
            Assert.True(item.GetProperty("publicationVersion").GetInt32() > 0);
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("publicCode").GetString()));
            Assert.True(item.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["id", "publicCode", "publicationVersion", "ownerId"]));
        });
        using var summary = await fixture.Client.GetAsync("/api/deck-library/summaries?source=public&pageSize=30");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        using var summaryJson = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
        Assert.Equal(30, summaryJson.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(100, summaryJson.RootElement.GetProperty("total").GetInt32());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task ForeignMissingAndDeletedIdsReturnNoProofWhileOwnCurrentVersionDoes()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var source = fixture.Catalog.PresetDecks[0];
        var current = fixture.Store.PublishDeck(fixture.Owner.Id, Deck(source, "reference-current-v2"),
            fixture.Published.Id)!;
        var other = fixture.Store.Authenticate("Bearer " + fixture.OtherToken)!;
        var foreign = fixture.Store.PublishDeck(other.Id, Deck(source, "reference-foreign"), null)!;
        var deleted = fixture.Store.PublishDeck(fixture.Owner.Id, Deck(source, "reference-deleted"), null)!;
        Assert.True(fixture.Store.DeletePublishedDeck(fixture.Owner.Id, deleted.Id));
        var missing = Guid.NewGuid().ToString("N");
        fixture.Observe();

        var mixed = await References(fixture, [current.Id, foreign.Id, missing, deleted.Id], fixture.Token);
        Assert.Equal(HttpStatusCode.OK, mixed.Status);
        using (var json = JsonDocument.Parse(mixed.Body))
        {
            Assert.Equal("available", json.RootElement.GetProperty("status").GetString());
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(current.Id, item.GetProperty("id").GetString());
            Assert.Equal(current.PublicCode, item.GetProperty("publicCode").GetString());
            Assert.Equal(current.Deck.PublicationVersion, item.GetProperty("publicationVersion").GetInt32());
            Assert.Equal(fixture.Owner.Id, item.GetProperty("ownerId").GetString());
        }
        var empty = await References(fixture, [foreign.Id, missing, deleted.Id], fixture.Token);
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        using (var json = JsonDocument.Parse(empty.Body))
        {
            Assert.Equal("available", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
            Assert.DoesNotContain(other.Id, empty.Body, StringComparison.Ordinal);
        }
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task DuplicateEmptyControlOverlongUnknownAndOneHundredOneQueriesAreBadRequests()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var id = fixture.Published.Id;
        var invalid = new[]
        {
            "",
            "publicationId=",
            "publicationId=%20",
            "publicationId=a%0Ab",
            "publicationId=" + new string('x', 65),
            "publicationId=" + id + "&publicationId=" + id,
            "publicationId=" + id + "&unknown=1",
            string.Join('&', Enumerable.Range(0, 101).Select(index => "publicationId=id" + index.ToString("D3"))),
        };
        fixture.Observe();
        foreach (var query in invalid)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "/api/me/public-deck-references" + (query.Length == 0 ? "" : "?" + query));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
            using var response = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("permission")]
    public async Task InvalidSessionAndPermissionGenerationAreUnauthorized(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var token = fixture.Token;
        if (mode == "invalid") token = "invalid-reference-token";
        if (mode == "revoked")
            Assert.Equal(1, fixture.Store.RevokeOwnSession(fixture.Viewer, fixture.Viewer.SessionId).RevokedCount);
        if (mode == "expired") fixture.Expire();
        if (mode == "permission")
        {
            var admin = fixture.Store.Login("Admin", "L12master").Account!;
            Assert.True(fixture.Store.SetRole(admin, fixture.Owner.Id, "admin"));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/me/public-deck-references?publicationId=" + fixture.Published.Id);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        fixture.Complete();
    }

    [Fact]
    public async Task FeatureOffIsExplicit503AndDoesNotDisablePrivateSummaries()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var source = fixture.Catalog.PresetDecks[0];
        Assert.True(fixture.Store.CreateDeck(fixture.Owner.Id, Deck(source, "private-still-readable")).Success);
        fixture.SetFeature(false);
        var response = await References(fixture, [fixture.Published.Id], fixture.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("feature_disabled", json.RootElement.GetProperty("code").GetString());
        var currentOwner = fixture.Store.Authenticate("Bearer " + fixture.Token)!;
        var privatePage = fixture.Store.PrivateDeckSummaries(currentOwner, fixture.Catalog);
        Assert.NotNull(privatePage);
        Assert.Equal(1, privatePage.Total);
        fixture.Complete();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task EmptyProofRequestsStillFailClosedOnCommittedGenerationDrift(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var id = Guid.NewGuid().ToString("N");
        if (mode == "foreign")
        {
            var other = fixture.Store.Authenticate("Bearer " + fixture.OtherToken)!;
            id = fixture.Store.PublishDeck(other.Id, Deck(fixture.Catalog.PresetDecks[0], "drift-foreign"), null)!.Id;
        }
        fixture.Sql("UPDATE platform_state SET storage_revision=storage_revision+1 WHERE singleton_id=1;");
        fixture.Observe();
        var response = await References(fixture, [id], fixture.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task RestartReadsTheSameCommittedOwnerProofWithoutBodyExpansion()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var expected = fixture.Store.PublicDeckReferences(fixture.Catalog, [fixture.Published.Id], fixture.Viewer);
        Assert.Equal("available", expected.Status);
        var restarted = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
        var expansions = new List<string>();
        restarted.DeckPayloadExpansionObserver = expansions.Add;
        var actual = restarted.PublicDeckReferences(fixture.Catalog, [fixture.Published.Id], fixture.Viewer);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Items.ToArray(), actual.Items.ToArray());
        Assert.Empty(expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task RequestedOwnBadHeadIsStorageUnavailableInsteadOfMissing()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        fixture.Sql($"UPDATE published_decks SET public_code='DRIFTED' WHERE publication_id='{fixture.Published.Id}';");
        fixture.Observe();
        var response = await References(fixture, [fixture.Published.Id], fixture.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task MalformedStoredHeadTimestampIsUnavailableWithoutProofOrBodyExpansion()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        const string malformed = "private-malformed-updated-value";
        fixture.Sql($"UPDATE published_decks SET updated_utc='{malformed}' WHERE publication_id='{fixture.Published.Id}';");
        fixture.Observe();
        var response = await References(fixture, [fixture.Published.Id], fixture.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("items", out _));
        Assert.DoesNotContain(malformed, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Published.Id, response.Body, StringComparison.Ordinal);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    private static L12PresetDeckDefinition Deck(L12PresetDeckDefinition source, string name) => new()
    {
        Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds],
        MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
    };

    private static async Task<(HttpStatusCode Status, string Body)> References(PublicDeckReadFixture fixture,
        IReadOnlyList<string> ids, string token)
    {
        var query = string.Join('&', ids.Select(id => "publicationId=" + Uri.EscapeDataString(id)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/public-deck-references?" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
