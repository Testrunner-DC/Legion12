using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckVersionHttpTests
{
    [Fact]
    public async Task TwentyFiveImmutableVersionsPageWithoutBodiesAndSelectedDetailHasOnlyOne()
    {
        await using var fixture = await PublicDeckReadFixture.Start(25);
        fixture.Observe();
        using var response = await fixture.Get("versions?page=2&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(25, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("version").GetInt32()));
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            foreach (var field in new[] { "deck", "cardIds", "payloadHash", "ownerId", "publicationVersion", "alternateArtCopies" })
                Assert.False(item.TryGetProperty(field, out _));
        Assert.Empty(fixture.Expansions);
        var readToken = json.RootElement.GetProperty("readToken").GetString();
        using (var selected = await fixture.Get("versions/7?expectedReadToken=" + readToken, byId: true))
        {
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
            using var detail = JsonDocument.Parse(await selected.Content.ReadAsStringAsync());
            Assert.Equal(7, detail.RootElement.GetProperty("metadata").GetProperty("version").GetInt32());
            Assert.Equal("read-7", detail.RootElement.GetProperty("deck").GetProperty("name").GetString());
            Assert.Empty(detail.RootElement.GetProperty("metadata").GetProperty("changes").EnumerateArray());
        }
        Assert.Single(fixture.Expansions);
        fixture.Expansions.Clear();
        using (var empty = await fixture.Get("versions?page=2147483647&pageSize=100"))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using var page = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
            Assert.Equal(25, page.RootElement.GetProperty("total").GetInt32());
            Assert.Empty(page.RootElement.GetProperty("items").EnumerateArray());
        }
        Assert.Empty(fixture.Expansions);
        Assert.Equal("25", fixture.Scalar("SELECT COUNT(*) FROM published_deck_versions;"));
        fixture.Complete();
    }

#if !CQ3_READ_BASELINE
    [Fact]
    public async Task QuantityDiffMatchesExistingHistoryContractWithoutExpandingPredecessor()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var source = fixture.Catalog.PresetDecks[0];
        fixture.Store.PublishDeck(fixture.Owner.Id, new L12PresetDeckDefinition
        {
            Name = "changed", MasterId = source.MasterId, CardIds = [.. source.CardIds, source.CardIds[0]],
            MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
        }, fixture.Published.Id);
        var expected = fixture.Store.PublicDeckDetails(fixture.Published.Id)!.Versions.Single(item => item.Version == 2).Changes;
        fixture.Observe();
        var metadata = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id).Page!;
        Assert.Equal(expected, metadata.Items[0].Changes);
        Assert.Empty(fixture.Expansions);
        var detail = fixture.Store.ReadPublicDeckVersion(fixture.Catalog, fixture.Published.PublicCode, 2).Detail!;
        Assert.Equal(expected, detail.Metadata.Changes);
        Assert.Equal(source.CardIds.Count + 1, detail.Deck.CardIds.Count);
        Assert.Single(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData(90_000, true)]
    [InlineData(100_001, false)]
    public async Task BudgetAppliesToSelectedConsumerAndNeverToAllHistoricalCopies(int quantity, bool allowed)
    {
        await using var fixture = await PublicDeckReadFixture.Start(25);
        fixture.InjectCounts(quantity, allVersions: true);
        var restarted = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
        var calls = new List<string>(); restarted.DeckPayloadExpansionObserver = calls.Add;
        var facts = fixture.Scalar("SELECT COUNT(*) FROM published_deck_versions;") + "|" + fixture.Scalar("SELECT COUNT(*) FROM deck_payloads;");
        var metadata = restarted.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id).Page!;
        Assert.Equal(25, metadata.Total);
        Assert.All(metadata.Items, item => Assert.Equal(quantity, item.Counts.Main));
        Assert.Empty(calls);
        if (allowed)
        {
            Assert.Equal(quantity, restarted.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id).Detail!.Deck.CardIds.Count);
            Assert.Equal(quantity, restarted.ReadPublicDeckVersion(fixture.Catalog, fixture.Published.Id, 7).Detail!.Deck.CardIds.Count);
            Assert.Equal(2, calls.Count);
        }
        else
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() => restarted.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => restarted.ReadPublicDeckVersion(fixture.Catalog, fixture.Published.Id, 7));
            Assert.Empty(calls);
        }
        Assert.Equal(facts, fixture.Scalar("SELECT COUNT(*) FROM published_deck_versions;") + "|" + fixture.Scalar("SELECT COUNT(*) FROM deck_payloads;"));
        fixture.Complete();
    }
#endif

    [Fact]
    public async Task HugeHistoricalPredecessorUsesCountsWhileSelectedSmallBodyStaysReadable()
    {
        await using var fixture = await PublicDeckReadFixture.Start(2);
        fixture.InjectCounts(100_001, allVersions: false);
        fixture.Observe();
        using (var metadata = await fixture.Get("versions")) Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.Empty(fixture.Expansions);
        using (var huge = await fixture.Get("versions/1")) Assert.Equal(HttpStatusCode.ServiceUnavailable, huge.StatusCode);
        Assert.Empty(fixture.Expansions);
        using (var small = await fixture.Get("versions/2"))
        {
            Assert.Equal(HttpStatusCode.OK, small.StatusCode);
            using var json = JsonDocument.Parse(await small.Content.ReadAsStringAsync());
            Assert.Contains(json.RootElement.GetProperty("metadata").GetProperty("changes").EnumerateArray(), change =>
                change.GetProperty("cardId").GetString() == "HUGE" && change.GetProperty("previousQuantity").GetInt32() == 100_001);
        }
        Assert.Single(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("unknown-format")]
    [InlineData("invalid-json")]
    [InlineData("wrong-hash")]
    [InlineData("missing-payload")]
    public async Task CorruptHistoricalReferencesFailBeforeAnyCopiesAndNeverFallback(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start(2);
        // Both names share a body; create a separate orphan so the current fact is intact.
        fixture.InjectCounts(5, allVersions: false);
        var target = "(SELECT payload_hash FROM published_deck_versions WHERE version=1)";
        if (mode == "unknown-format") fixture.Sql("UPDATE deck_payloads SET payload_format=999 WHERE payload_hash=" + target + ";");
        if (mode == "invalid-json") fixture.Sql("UPDATE deck_payloads SET payload_json='[]' WHERE payload_hash=" + target + ";");
        if (mode == "wrong-hash") fixture.Sql("UPDATE deck_payloads SET master_id='WRONG' WHERE payload_hash=" + target + ";");
        // Deliberately corrupt only this owned fixture; the normal writer must
        // retain its foreign-key guard. The read path then sees a real orphan.
        if (mode == "missing-payload") fixture.Sql("PRAGMA foreign_keys=OFF; DELETE FROM deck_payloads WHERE payload_hash=" + target + ";");
        fixture.Observe();
        foreach (var suffix in new[] { "versions", "versions/1", "versions/2" })
        {
            using var response = await fixture.Get(suffix);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("public_deck_read_unavailable", json.RootElement.GetProperty("code").GetString());
        }
        Assert.Empty(fixture.Expansions);
        using (var current = await fixture.Get("current")) Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Single(fixture.Expansions);
        Assert.Equal("2", fixture.Scalar("SELECT COUNT(*) FROM published_deck_versions;"));
        fixture.Complete();
    }
}
