using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckDetailGenerationTests
{
    [Theory]
    [InlineData("publish")]
    [InlineData("content")]
    [InlineData("policy")]
    [InlineData("rename")]
    public async Task ChangedPinnedGenerationReturns409WithZeroExpansionsAndExplicitRefresh(string change)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        using var page = await fixture.Get("versions");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var before = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var readToken = before.RootElement.GetProperty("readToken").GetString();
        if (change == "publish") fixture.Publish(2);
        if (change == "content") fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id, new(new("new guide", "", "", "", ""), []));
        if (change == "policy") fixture.SetFeature(true);
        if (change == "rename") Assert.True(fixture.Store.SelfServiceChangeUsername(fixture.Owner.Id, "password-123", "readrenamed", fixture.Viewer.SessionId).Success);
        fixture.Observe();
        foreach (var suffix in new[] { "current", "versions", "versions/1" })
        {
            using var response = await fixture.Get(suffix + "?expectedReadToken=" + readToken);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("public_deck_read_conflict", json.RootElement.GetProperty("code").GetString());
            Assert.True(json.RootElement.GetProperty("refreshRequired").GetBoolean());
            Assert.False(json.RootElement.TryGetProperty("deck", out _));
            Assert.False(json.RootElement.TryGetProperty("readToken", out _));
        }
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task OwnerPinCannotBeReusedAnonymouslyOrByAnotherViewer()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        using var page = await fixture.Get("versions", fixture.Token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("readToken").GetString();
        fixture.Observe();
        foreach (var viewer in new[] { null, fixture.OtherToken })
            foreach (var suffix in new[] { "current", "versions", "versions/1" })
            {
                using var response = await fixture.Get(suffix + "?expectedReadToken=" + token, viewer);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            }
        Assert.Empty(fixture.Expansions);
        using (var owner = await fixture.Get("current?expectedReadToken=" + token, fixture.Token)) Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Single(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("head")]
    [InlineData("schema")]
    [InlineData("revision")]
    [InlineData("content-hash")]
    [InlineData("content-payload")]
    [InlineData("content-head")]
    public async Task UnknownOrContradictoryFactsNeverUseAnEmptyOrOlderFallback(string kind)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        if (kind.StartsWith("content", StringComparison.Ordinal)) fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
            new(new("nonempty", "", "", "", ""), []));
        switch (kind)
        {
            case "head": fixture.Sql("UPDATE published_decks SET current_version=current_version+1;"); break;
            case "schema": fixture.Sql("UPDATE platform_state SET schema_version=999;"); break;
            case "revision": fixture.Sql("UPDATE platform_state SET storage_revision=storage_revision+1;"); break;
            case "content-hash": fixture.Sql("UPDATE published_deck_content_payloads SET guide_json='null';"); break;
            // Bypass the constraint only in this synthetic connection to prove
            // the consumer rejects an actually missing immutable payload.
            case "content-payload": fixture.Sql("PRAGMA foreign_keys=OFF; DELETE FROM published_deck_content_payloads;"); break;
            case "content-head": fixture.Sql("DELETE FROM published_deck_content_heads;"); break;
        }
        fixture.Observe();
        using var response = await fixture.Get("current");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("public_deck_read_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("deck", out _));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

#if !CQ3_READ_BASELINE
    [Theory]
    [InlineData("revoke")]
    [InlineData("expire")]
    [InlineData("role")]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task CapturedViewerAndTokenCannotBypassFreshPermissionChecks(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var captured = fixture.Viewer;
        var token = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id, viewer: captured).Page!.ReadToken;
        var admin = fixture.Store.Login("Admin", "L12master").Account!;
        var context = new L12AdminAuditContext("read-account", Reason: "CQ3 synthetic", RequestMethod: "POST", RequestPath: "/synthetic");
        if (mode == "revoke") fixture.Store.RevokeOwnSession(captured, captured.SessionId);
        if (mode == "expire") fixture.Expire();
        if (mode == "role") Assert.True(fixture.Store.SetRole(admin, fixture.Owner.Id, "admin"));
        if (mode == "disable") fixture.Store.SetAccountDisabled(admin, fixture.Owner.Id, true, "CQ3 synthetic", context, true);
        if (mode == "delete") fixture.Store.DeleteAccountPersonalData(admin, fixture.Owner.Id, "CQ3 synthetic", context, true);
        fixture.Observe();
        Assert.Equal("unauthorized", fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id, token, captured).Status);
        Assert.Equal("unauthorized", fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id, new(ExpectedReadToken: token), captured).Status);
        Assert.Equal("unauthorized", fixture.Store.ReadPublicDeckVersion(fixture.Catalog, fixture.Published.Id, 1, token, captured).Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task CounterChangesKeepContentPinAndRestartPreservesItWithoutRewritingFacts()
    {
        await using var fixture = await PublicDeckReadFixture.Start(3);
        var before = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id, viewer: fixture.Viewer).Page!;
        var version = fixture.Store.Version;
        fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, "view");
        fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, "copy");
        fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, "like", fixture.Viewer);
        fixture.Observe();
        var current = fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.PublicCode, before.ReadToken, fixture.Viewer).Detail!;
        Assert.Equal(before.ReadToken, current.ReadToken);
        Assert.Equal(1, current.Summary.Views); Assert.Equal(1, current.Summary.Copies); Assert.Equal(1, current.Summary.Likes);
        Assert.True(current.Summary.ViewerLiked); Assert.Single(fixture.Expansions);
        var restarted = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
        var fresh = restarted.AuthenticateSession("Bearer " + fixture.Token)!;
        var calls = new List<string>(); restarted.DeckPayloadExpansionObserver = calls.Add;
        var after = restarted.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id, new(ExpectedReadToken: before.ReadToken), fresh);
        Assert.Equal("ok", after.Status); Assert.Equal(3, after.Page!.Total); Assert.Equal(before.ReadToken, after.Page.ReadToken);
        Assert.Empty(calls); Assert.Equal(version, fixture.Store.Version); Assert.Equal(version, restarted.Version);
        fixture.Complete();
    }

    [Fact]
    public async Task CatalogPoolChangeInvalidatesAllReadPinsBeforeCopies()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var before = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id).Page!;
        var pools = Assert.IsAssignableFrom<IDictionary<string, string?>>(fixture.Catalog.CardPools);
        pools[fixture.Published.Deck.MasterId] = "S03";
        fixture.Observe();
        Assert.Equal("read_conflict", fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id, before.ReadToken).Status);
        Assert.Equal("read_conflict", fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id, new(ExpectedReadToken: before.ReadToken)).Status);
        Assert.Equal("read_conflict", fixture.Store.ReadPublicDeckVersion(fixture.Catalog, fixture.Published.Id, 1, before.ReadToken).Status);
        var after = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id).Page!;
        Assert.NotEqual(before.CatalogVersion, after.CatalogVersion); Assert.NotEqual(before.ReadToken, after.ReadToken);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task FailedPublishRestoresWholeGenerationAndItsPin()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var before = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id).Page!;
        fixture.Store.StorageFailureInjector = stage => { if (stage == "before-commit") throw new IOException("CQ3 synthetic aborted publish"); };
        try { Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Publish(2)); }
        finally { fixture.Store.StorageFailureInjector = null; }
        fixture.Observe();
        var after = fixture.Store.ReadPublicDeckVersionPage(fixture.Catalog, fixture.Published.Id, new(ExpectedReadToken: before.ReadToken));
        Assert.Equal("ok", after.Status); Assert.Equal(before.ReadToken, after.Page!.ReadToken); Assert.Equal(1, after.Page.Total);
        Assert.Empty(fixture.Expansions);
        var detail = fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id, before.ReadToken).Detail!;
        Assert.Equal("read-1", detail.Deck.Name); Assert.Equal(1, detail.Version); Assert.Single(fixture.Expansions);
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_versions;"));
        fixture.Complete();
    }

    [Fact]
    public async Task PublicationWriterCannotStitchNewHeadIntoInFlightBodyRead()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var writerStarted = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<string>();
        fixture.Store.DeckPayloadExpansionObserver = hash =>
        {
            calls.Enqueue(hash); entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("CQ3 synthetic read barrier");
        };
        var read = Task.Run(() => fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id));
        Task? write = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            write = Task.Run(() => { writerStarted.Set(); fixture.Publish(2); });
            Assert.True(writerStarted.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(write.IsCompleted);
        }
        finally { release.Set(); }
        var captured = await read;
        if (write is not null) await write;
        Assert.Equal("ok", captured.Status); Assert.Equal(1, captured.Detail!.Version); Assert.Equal("read-1", captured.Detail.Deck.Name);
        fixture.Store.DeckPayloadExpansionObserver = fixture.Expansions.Add;
        Assert.Equal("read_conflict", fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id, captured.Detail.ReadToken).Status);
        Assert.Empty(fixture.Expansions);
        var current = fixture.Store.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id).Detail!;
        Assert.Equal(2, current.Version); Assert.Equal("read-2", current.Deck.Name); Assert.Single(fixture.Expansions);
        fixture.Complete();
    }
#endif
}
