using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckContentHeadWriteHttpTests
{
    [Fact]
    public async Task OwnerCanonicalWriteReturnsOnlyCommittedThinHeadAndCurrentAcceptsItsPin()
    {
        await using var fixture = await PublicDeckReadFixture.Start(25);
        var beforePin = await Pin(fixture);
        var mirror = File.ReadAllBytes(fixture.PlatformPath);
        var storageRevision = fixture.Scalar("SELECT storage_revision FROM platform_state WHERE singleton_id=1;");
        var dataVersion = DataVersion(fixture);
        var opponent = Opponent(fixture);
        fixture.Observe();

        var saved = await Put(fixture, beforePin.ReadToken,
            Content("  构筑\r\n思路  ", " 起手 ", "关键牌", "常见展开", "替换建议",
                [new(opponent.ToLowerInvariant(), " 对局 ", "关键", "换牌")]));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal("no-store", saved.CacheControl);
        using var json = JsonDocument.Parse(saved.Body);
        var root = json.RootElement;
        Assert.Equal(10, root.EnumerateObject().Count());
        Assert.Equal(fixture.Published.Id, root.GetProperty("id").GetString());
        Assert.Equal(fixture.Published.PublicCode, root.GetProperty("publicCode").GetString());
        Assert.Equal("构筑\n思路", root.GetProperty("guide").GetProperty("buildIdea").GetString());
        Assert.Equal(opponent, Assert.Single(root.GetProperty("matchups").EnumerateArray())
            .GetProperty("opponentMasterId").GetString());
        Assert.Equal(1, root.GetProperty("contentRevision").GetInt32());
        Assert.NotEqual(beforePin.ReadToken, root.GetProperty("readToken").GetString());
        Assert.Equal(beforePin.CatalogVersion, root.GetProperty("catalogVersion").GetString());
        Assert.Equal(beforePin.PolicyVersion, root.GetProperty("policyVersion").GetInt64());
        Assert.True(root.GetProperty("canEdit").GetBoolean());
        Assert.True(root.GetProperty("contentUpdatedAt").GetDateTimeOffset() > DateTimeOffset.UnixEpoch);
        foreach (var forbidden in new[] { "deck", "versions", "matchStatistics", "statistics", "ownerId", "author" })
            Assert.False(root.TryGetProperty(forbidden, out _));
        Assert.Empty(fixture.Expansions);
        Assert.Equal("25", fixture.Scalar(
            $"SELECT COUNT(*) FROM published_deck_versions WHERE publication_id='{fixture.Published.Id}';"));
        Assert.Equal("1", fixture.Scalar(
            $"SELECT COUNT(*) FROM published_deck_content_revisions WHERE publication_id='{fixture.Published.Id}';"));
        Assert.Equal(storageRevision, fixture.Scalar("SELECT storage_revision FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(dataVersion, DataVersion(fixture));
        Assert.Equal(mirror, File.ReadAllBytes(fixture.PlatformPath));

        var newPin = root.GetProperty("readToken").GetString()!;
        fixture.Expansions.Clear();
        using (var current = await fixture.Get("current?expectedReadToken=" + newPin, fixture.Token))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
            Assert.Equal("构筑\n思路", currentJson.RootElement.GetProperty("guide")
                .GetProperty("buildIdea").GetString());
            Assert.Equal(newPin, currentJson.RootElement.GetProperty("readToken").GetString());
        }
        using (var stale = await fixture.Get("current?expectedReadToken=" + beforePin.ReadToken, fixture.Token))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Single(fixture.Expansions);
        Assert.NotNull(fixture.Store.RecordPublishedDeckView(fixture.Published.Id, fixture.Owner.Id));
        using (var counted = await fixture.Get("current?expectedReadToken=" + newPin, fixture.Token))
            Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        fixture.Complete();
    }

    [Fact]
    public async Task IdenticalWriteIsIdempotentAndLegacyWrapperStillReturnsFullDetailsAfterRestart()
    {
        await using var fixture = await PublicDeckReadFixture.Start(25);
        var pin = await Pin(fixture);
        var body = Content("build", "opening", "key", "sequence", "substitution",
            [new(Opponent(fixture), "notes", "cards", "swaps")]);
        var first = await Put(fixture, pin.ReadToken, body);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        using var firstJson = JsonDocument.Parse(first.Body);
        var firstPin = firstJson.RootElement.GetProperty("readToken").GetString()!;
        var second = await Put(fixture, firstPin, body, reference: fixture.Published.Id);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        using var secondJson = JsonDocument.Parse(second.Body);
        Assert.Equal(1, secondJson.RootElement.GetProperty("contentRevision").GetInt32());
        Assert.Equal(firstPin, secondJson.RootElement.GetProperty("readToken").GetString());
        Assert.Equal("1", fixture.Scalar(
            $"SELECT COUNT(*) FROM published_deck_content_revisions WHERE publication_id='{fixture.Published.Id}';"));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_payloads;"));

        var legacy = fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
            Content("legacy build", "legacy opening", "legacy key", "legacy sequence", "legacy substitute",
                [new(Opponent(fixture), "legacy notes", "legacy cards", "legacy swaps")]))!;
        Assert.Equal(2, legacy.ContentRevision);
        Assert.Equal(25, legacy.Versions.Count);
        Assert.Equal("empty", legacy.MatchStatistics.SampleStatus);
        Assert.Equal("legacy build", legacy.Guide.BuildIdea);
        var latest = await Pin(fixture);
        var restarted = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
        var restored = restarted.ReadPublicDeckCurrent(fixture.Catalog, fixture.Published.Id,
            latest.ReadToken, fixture.Viewer);
        Assert.Equal("ok", restored.Status);
        Assert.Equal(latest.ReadToken, restored.Detail!.ReadToken);
        Assert.Equal(legacy.Guide, restored.Detail.Guide);
        Assert.True(legacy.Matchups.SequenceEqual(restored.Detail.Matchups));
        fixture.Complete();
    }

    [Fact]
    public async Task QueryAuthenticationOwnershipMissingAndFeatureStatesAreStrict()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        var body = Content("strict");
        var invalid = new[]
        {
            "",
            "old=1",
            "expectedReadToken=",
            "expectedReadToken=" + new string('a', 63),
            "expectedReadToken=" + new string('a', 65),
            "expectedReadToken=" + pin.ReadToken.ToUpperInvariant(),
            "expectedReadToken=" + pin.ReadToken + "&expectedReadToken=" + pin.ReadToken,
            "expectedReadToken=" + pin.ReadToken + "&page=1",
            "expectedReadToken=" + Uri.EscapeDataString(pin.ReadToken[..63] + "\n"),
        };
        foreach (var query in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await Put(fixture, query, body, rawQuery: true)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Put(fixture, pin.ReadToken, body, token: null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Put(fixture, pin.ReadToken, body, token: "invalid-token")).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Put(fixture, pin.ReadToken, body, token: fixture.OtherToken)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Put(fixture, pin.ReadToken, body, reference: "missing")).Status);
        fixture.SetFeature(false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Put(fixture, pin.ReadToken, body)).Status);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        fixture.Complete();
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("expire")]
    [InlineData("permission")]
    public async Task StaleAuthenticationContextsAreUnauthorizedWithoutWrites(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        if (mode == "revoke")
            Assert.Equal(1, fixture.Store.RevokeOwnSession(fixture.Viewer, fixture.Viewer.SessionId).RevokedCount);
        if (mode == "expire") fixture.Expire();
        if (mode == "permission")
            Assert.True(fixture.Store.SetRole(fixture.Store.Login("Admin", "L12master").Account!,
                fixture.Owner.Id, "admin"));
        var response = await Put(fixture, pin.ReadToken, Content("stale"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        fixture.Complete();
    }

    [Fact]
    public async Task WrongAndStalePinsNeverChangeSql()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        fixture.Observe();
        Assert.Equal(HttpStatusCode.Conflict,
            (await Put(fixture, new string('0', 64), Content("wrong"))).Status);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        Assert.NotNull(fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
            Content("intervening")));
        fixture.Expansions.Clear();
        Assert.Equal(HttpStatusCode.Conflict,
            (await Put(fixture, pin.ReadToken, Content("stale"))).Status);
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task ConcurrentSamePinAllowsOneChangeAndRejectsTheOther()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        fixture.Observe();
        var left = Put(fixture, pin.ReadToken, Content("left"));
        var right = Put(fixture, pin.ReadToken, Content("right"));
        var responses = await Task.WhenAll(left, right);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            responses.Select(item => item.Status).Order().ToArray());
        Assert.All(responses, response => Assert.Equal("no-store", response.CacheControl));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_heads;"));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("after-payload")]
    [InlineData("after-head")]
    [InlineData("before-commit")]
    public async Task InjectedTransactionalFaultsRollbackEveryContentFact(string phase)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        var mirror = File.ReadAllBytes(fixture.PlatformPath);
        var storageRevision = fixture.Scalar("SELECT storage_revision FROM platform_state WHERE singleton_id=1;");
        var dataVersion = DataVersion(fixture);
        fixture.Store.PublicDeckContentWriteFaultHook = current =>
        {
            if (current == phase) throw new InvalidDataException("synthetic content write fault");
        };
        var response = await Put(fixture, pin.ReadToken, Content("rollback-" + phase));
        fixture.Store.PublicDeckContentWriteFaultHook = null;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        using (var json = JsonDocument.Parse(response.Body))
        {
            Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
            Assert.Equal("牌库内容暂时无法保存，请稍后重试",
                json.RootElement.GetProperty("message").GetString());
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        }
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_payloads;"));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_heads;"));
        Assert.Equal(storageRevision, fixture.Scalar("SELECT storage_revision FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(dataVersion, DataVersion(fixture));
        Assert.Equal(mirror, File.ReadAllBytes(fixture.PlatformPath));
        using var current = await fixture.Get("current?expectedReadToken=" + pin.ReadToken, fixture.Token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        fixture.Complete();
    }

    [Fact]
    public async Task OtherStoreGuideCommitConflictsWhileStorageRevisionDriftFailsClosed()
    {
        await using (var fixture = await PublicDeckReadFixture.Start())
        {
            var pin = await Pin(fixture);
            var other = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
            Assert.NotNull(other.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
                Content("other-store")));
            var response = await Put(fixture, pin.ReadToken, Content("must-conflict"));
            Assert.Equal(HttpStatusCode.Conflict, response.Status);
            Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
            fixture.Complete();
        }
        await using (var fixture = await PublicDeckReadFixture.Start())
        {
            var pin = await Pin(fixture);
            var other = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
            Assert.True(other.Register("newwriter", "password-123").Success);
            var response = await Put(fixture, pin.ReadToken, Content("must-fail-closed"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
            Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
            fixture.Complete();
        }
    }

    [Theory]
    [InlineData("head-date")]
    [InlineData("payload-hash")]
    public async Task DamagedCommittedHeadOrPayloadReturnsPlainUnavailable(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var firstPin = await Pin(fixture);
        var saved = await Put(fixture, firstPin.ReadToken, Content("healthy"));
        using var json = JsonDocument.Parse(saved.Body);
        var pin = json.RootElement.GetProperty("readToken").GetString()!;
        if (mode == "head-date")
            fixture.Sql($"UPDATE published_deck_content_heads SET updated_utc='broken-private-date' WHERE publication_id='{fixture.Published.Id}';");
        else
            fixture.Sql("UPDATE published_deck_content_payloads SET guide_json='{}';");
        var response = await Put(fixture, pin, Content("never-commit"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        Assert.DoesNotContain("broken-private-date", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Published.Id, response.Body, StringComparison.Ordinal);
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        fixture.Complete();
    }

    [Fact]
    public async Task CanonicalFiveSectionsAndSixtyFourMatchupsKeepTheLegacyLimits()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var cards = Assert.IsType<Dictionary<string, L12CardDefinition>>(fixture.Catalog.Cards);
        var matchups = new List<L12PublicDeckMatchupView>();
        for (var index = 0; index < 64; index++)
        {
            var id = $"SYN-MASTER-{index:D2}";
            cards.Add(id, new()
            {
                Id = id, Number = id, NameZh = "合成主宰" + index, CardType = "master",
                Product = "synthetic", Faction = "synthetic",
            });
            matchups.Add(new(id.ToLowerInvariant(), new string('中', 800), new string('关', 800),
                new string('换', 800)));
        }
        var pin = await Pin(fixture);
        fixture.Observe();
        var body = new L12PublicDeckContentInput(new(new string('构', 1200), new string('起', 1200),
            new string('关', 1200), new string('展', 1200), new string('替', 1200)), matchups);
        var response = await Put(fixture, pin.ReadToken, body);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(1200, json.RootElement.GetProperty("guide").GetProperty("buildIdea").GetString()!.Length);
        Assert.Equal(64, json.RootElement.GetProperty("matchups").GetArrayLength());
        Assert.All(json.RootElement.GetProperty("matchups").EnumerateArray(),
            item => Assert.Equal(800, item.GetProperty("notes").GetString()!.Length));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task InvalidLegacyContentLimitsReturnBadRequestWithoutPartialRows()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        var opponent = Opponent(fixture);
        var invalid = new L12PublicDeckContentInput[]
        {
            Content(new string('a', 1201)),
            Content("ok", matchups: [new(opponent, new string('a', 801), "", "")]),
            Content("ok", matchups: Enumerable.Range(0, 65).Select(_ => new L12PublicDeckMatchupView(
                opponent, "", "", "")).ToArray()),
            Content("<script>"),
            Content("javascript:alert(1)"),
            Content("bad\u0001control"),
            Content("ok", matchups: [new("", "missing master", "", "")]),
            Content("ok", matchups: [new(opponent, "", "", ""), new(opponent.ToLowerInvariant(), "", "", "")]),
            Content("ok", matchups: [new("NOT-A-MASTER", "", "", "")]),
        };
        foreach (var body in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await Put(fixture, pin.ReadToken, body)).Status);
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_payloads;"));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        fixture.Complete();
    }

    [Theory]
    [InlineData("guide")]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("catalog")]
    [InlineData("owner-name")]
    public async Task EveryPinnedHeadContextChangeRejectsTheOldToken(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        switch (mode)
        {
            case "guide":
                Assert.NotNull(fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
                    Content("intervening guide")));
                break;
            case "version":
                fixture.Publish(2);
                break;
            case "policy":
                var admin = fixture.Store.Login("Admin", "L12master").Account!;
                var config = fixture.Store.OperationsConfig(admin);
                fixture.Store.ApplyOperationsConfig(admin, config.Config with
                {
                    CardRestrictions = [new(fixture.Catalog.PresetDecks[0].CardIds[0], 0, "write pin")],
                }, config.Version, "write pin", new("write-pin"));
                break;
            case "catalog":
                var source = fixture.Catalog.PresetDecks[0];
                Assert.IsType<List<L12PresetDeckDefinition>>(fixture.Catalog.PresetDecks).Add(new()
                {
                    Name = "write-catalog-change", MasterId = source.MasterId,
                    CardIds = [.. source.CardIds], MoraleIds = [.. source.MoraleIds],
                    SpecialIds = [.. source.SpecialIds],
                });
                break;
            case "owner-name":
                Assert.True(fixture.Store.SelfServiceChangeUsername(fixture.Owner.Id, "password-123",
                    "writername", fixture.Viewer.SessionId).Success);
                break;
        }
        fixture.Expansions.Clear();
        var response = await Put(fixture, pin.ReadToken, Content("must-conflict"));
        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal(mode == "guide" ? "1" : "0",
            fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task DeletedPublicationIsNotResurrected()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        var pin = await Pin(fixture);
        Assert.True(fixture.Store.DeletePublishedDeck(fixture.Owner.Id, fixture.Published.Id));
        var response = await Put(fixture, pin.ReadToken, Content("do not restore"));
        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("1", fixture.Scalar(
            $"SELECT is_deleted FROM published_decks WHERE publication_id='{fixture.Published.Id}';"));
        Assert.Equal("0", fixture.Scalar("SELECT COUNT(*) FROM published_deck_content_revisions;"));
        fixture.Complete();
    }

    private static async Task<PinView> Pin(PublicDeckReadFixture fixture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/deck-library/summaries?source=public&pageSize=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == fixture.Published.Id);
        return new(row.GetProperty("readToken").GetString()!,
            json.RootElement.GetProperty("catalogVersion").GetString()!,
            json.RootElement.GetProperty("policyVersion").GetInt64());
    }

    private static async Task<WriteResponse> Put(PublicDeckReadFixture fixture, string tokenOrQuery,
        L12PublicDeckContentInput body, string? token = "__owner__", string? reference = null,
        bool rawQuery = false)
    {
        var query = rawQuery ? tokenOrQuery : "expectedReadToken=" + Uri.EscapeDataString(tokenOrQuery);
        var path = $"/api/public-decks/{reference ?? fixture.Published.PublicCode}/content/current";
        if (query.Length > 0) path += "?" + query;
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                token == "__owner__" ? fixture.Token : token);
        using var response = await fixture.Client.SendAsync(request);
        return new(response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Headers.CacheControl?.ToString());
    }

    private static L12PublicDeckContentInput Content(string buildIdea, string opening = "",
        string keyCards = "", string commonSequence = "", string substitutions = "",
        IReadOnlyList<L12PublicDeckMatchupView>? matchups = null)
        => new(new(buildIdea, opening, keyCards, commonSequence, substitutions), matchups ?? []);

    private static string Opponent(PublicDeckReadFixture fixture)
        => fixture.Catalog.Cards.Values.First(card => (card.CardType is "master" or "divinity")
            && card.Id != fixture.Published.Deck.MasterId).Id;

    private static long DataVersion(PublicDeckReadFixture fixture)
        => (long)fixture.Data.GetType().GetProperty("Version")!.GetValue(fixture.Data)!;

    private sealed record PinView(string ReadToken, string CatalogVersion, long PolicyVersion);
    private sealed record WriteResponse(HttpStatusCode Status, string Body, string? CacheControl);
}
