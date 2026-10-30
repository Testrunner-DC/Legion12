using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SharedGalleryCatalogTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
    private const string Password = "synthetic-password-123";

    [Theory]
    [InlineData("snapshot")]
    [InlineData("public")]
    [InlineData("admin")]
    [InlineData("search")]
    public async Task StandaloneTraitsAreImmutableAcrossSharedConsumers(string consumer)
    {
        var art = Art() with { Id = "shared-standalone", Kind = "gallery-card", BaseCardId = "",
            CardInformation = new(CardType: "legion", Traits: new[] { "人类", "英雄" }) };
        var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source);
        var original = JsonSerializer.Serialize(source.RequireCurrent().Arts, Wire);
        var exposed = consumer switch
        {
            "snapshot" => Assert.Single(source.RequireCurrent().Arts),
            "public" => Assert.Single(store.AlternateArts()),
            "admin" => Assert.Single(store.AdminAlternateArts()),
            _ => Assert.Single(store.SearchAlternateArts(null, null, null).Items),
        };
        var traits = Assert.IsAssignableFrom<IList<string>>(exposed.CardInformation!.Traits);
        Assert.Throws<NotSupportedException>(() => traits[0] = "不应改写共享快照");
        Assert.Equal(new[] { "人类", "英雄" }, Assert.Single(store.AlternateArts()).CardInformation!.Traits);
        Assert.Equal(original, JsonSerializer.Serialize(source.RequireCurrent().Arts, Wire));
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(null, null, null, 8083, false)]
    [InlineData("", "ephemeral", "https://legion-12.com/testrun", 8084, false)]
    [InlineData("production-public-v1", "ephemeral", "https://legion-12.com/testrun", 8084, true)]
    public void StartupHasAnExplicitOptIn(string? marker, string? profile, string? url, int port, bool expected)
        => Assert.Equal(expected, L12SharedGalleryCatalog.EnabledForConfiguration(marker, profile, url, port));

    [Theory]
    [InlineData("production-public-v1", null, "https://legion-12.com/testrun", 8084)]
    [InlineData("production-public-v1", "ephemeral", "https://legion-12.com/", 8084)]
    [InlineData("production-public-v1", "ephemeral", "https://legion-12.com/testrun/", 8084)]
    [InlineData("production-public-v1", "ephemeral", "https://user@legion-12.com/testrun", 8084)]
    [InlineData("production-public-v1", "ephemeral", "https://legion-12.com/testrun?q=1", 8084)]
    [InlineData("production-public-v1", "ephemeral", "https://legion-12.com/testrun", 8083)]
    [InlineData("http://elsewhere/api/alternate-arts", "ephemeral", "https://legion-12.com/testrun", 8084)]
    [InlineData(" ", "ephemeral", "https://legion-12.com/testrun", 8084)]
    public void ProductionOrNonFixedConfigurationIsRejected(string marker, string? profile, string url, int port)
        => Assert.Throws<InvalidOperationException>(() => L12SharedGalleryCatalog.EnabledForConfiguration(marker, profile, url, port));

    [Fact]
    public void RealTransportHasNoAmbientCredentialsCookiesProxyRedirectsOrDecompression()
    {
        using var handler = L12SharedGalleryCatalog.CreateHandler();
        Assert.False(handler.AllowAutoRedirect); Assert.False(handler.UseCookies); Assert.False(handler.UseProxy);
        Assert.False(handler.PreAuthenticate); Assert.Null(handler.Credentials);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(1, handler.MaxConnectionsPerServer);
        Assert.Equal(new Uri("http://127.0.0.1:8083/api/alternate-arts"), L12SharedGalleryCatalog.Endpoint);
        Assert.Throws<InvalidOperationException>(() => L12SharedGalleryCatalog.Create(true, false));
    }

    [Fact]
    public void RealPublicStoreShapeAndMultilineGalleryCardRoundTripWithoutPrivateFields()
    {
        var store = NewStore();
        var admin = store.Login("Admin", "L12master").Account!;
        var media = Upload(store, admin);
        var gallery = store.SaveAlternateArt(admin, new(null, "MULTILINE", "", "独立联动卡", media.Id,
            Kind: "gallery-card", Effect: "第一行\r\n第二行\n第三行\t说明"));
        var source = store.AlternateArts();
        var parsed = L12SharedGalleryCatalog.Parse(JsonSerializer.SerializeToUtf8Bytes(source, Wire));
        Assert.Equal(source.Count, parsed.Count);
        Assert.NotEmpty(parsed.Where(row => row.BuiltIn));
        Assert.Equal(gallery.Effect, parsed.Single(row => row.Id == gallery.Id).Effect);
        Assert.Equal(JsonSerializer.Serialize(source, Wire), JsonSerializer.Serialize(parsed, Wire));
        var invalid = Node(gallery); invalid["effect"] = "拒绝\u0001控制符";
        Assert.Throws<InvalidDataException>(() => ParseNode(invalid));
    }

    [Theory]
    [InlineData("https://other.invalid/api/site/media/media/desktop/HASH.webp")]
    [InlineData("//other.invalid/api/site/media/media/desktop/HASH.webp")]
    [InlineData("/testrun/api/site/media/media/desktop/HASH.webp")]
    [InlineData("/api/site/media/other/desktop/HASH.webp")]
    [InlineData("/api/site/media/media/desktop/HASH.webp?q=1")]
    [InlineData("/api/site/media/media/desktop/../HASH.webp")]
    [InlineData("/api/site/media/media/desktop/%2e%2e.webp")]
    public void UnsafeOrMismatchedMediaPathsAreRejected(string url)
    {
        var art = Node(Art()); art["imageUrl"] = url.Replace("HASH", new string('a', 64));
        Assert.Throws<InvalidDataException>(() => ParseNode(art));
    }

    [Fact]
    public void BoundedParserRejectsDuplicatesMissingStructurePrivateGrantsAndOversizedValues()
    {
        var art = Art();
        Assert.Throws<InvalidDataException>(() => L12SharedGalleryCatalog.Parse(JsonSerializer.SerializeToUtf8Bytes(new[] { art, art }, Wire)));
        Assert.Throws<InvalidDataException>(() => L12SharedGalleryCatalog.Parse(Encoding.UTF8.GetBytes("{}")));
        Assert.Throws<InvalidDataException>(() => L12SharedGalleryCatalog.Parse(new byte[L12SharedGalleryCatalog.MaximumBytes + 1]));
        Assert.Throws<InvalidDataException>(() => L12SharedGalleryCatalog.Parse(Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat("{}", L12SharedGalleryCatalog.MaximumEntries + 1)) + "]")));
        foreach (var name in new[] { "active", "kind", "imageUrl", "baseCardId" })
        {
            var node = Node(art); node.Remove(name);
            Assert.Throws<InvalidDataException>(() => ParseNode(node));
        }
        var privateGrant = Node(art); privateGrant["grantedAt"] = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDataException>(() => ParseNode(privateGrant));
        var longName = Node(art); longName["displayName"] = new string('x', 201);
        Assert.Throws<InvalidDataException>(() => ParseNode(longName));
        var json = JsonSerializer.Serialize(art, Wire).Replace("\"active\":true", "\"active\":true,\"active\":false");
        Assert.Throws<InvalidDataException>(() => L12SharedGalleryCatalog.Parse(Encoding.UTF8.GetBytes("[" + json + "]")));
    }

    [Fact]
    public async Task RefreshIsAnonymousAtomicAndExpiresWithoutFallingBack()
    {
        var clock = new Clock();
        var transport = new Transport { Response = () => Response(Art()) };
        await using var source = new L12SharedGalleryCatalog(transport, clock);
        Assert.Null(source.Current);
        Assert.True(await source.RefreshAsync());
        var first = source.Current!;
        Assert.Single(first.Arts);
        Assert.Equal(HttpMethod.Get, transport.Method);
        Assert.Equal(L12SharedGalleryCatalog.Endpoint, transport.Uri);
        Assert.Equal(new[] { "Accept" }, transport.HeaderNames);
        Assert.Null(transport.Content);
        Assert.Throws<NotSupportedException>(() => ((IList<L12AlternateArtView>)first.Arts).Clear());
        clock.Now += L12SharedGalleryCatalog.MaximumAge + TimeSpan.FromTicks(1);
        Assert.Null(source.Current);
        Assert.Throws<L12PlatformStorageUnavailableException>(() => source.RequireCurrent());
        Assert.True(await source.RefreshAsync());
        transport.Response = () => new HttpResponseMessage(HttpStatusCode.Redirect)
        { Headers = { Location = new Uri("http://127.0.0.1:8083/api/admin/accounts") } };
        Assert.False(await source.RefreshAsync());
        Assert.Null(source.Current);
        Assert.Equal(3, transport.Calls); // A redirect is never followed.
    }

    [Theory]
    [InlineData("type")]
    [InlineData("encoding")]
    [InlineData("length")]
    [InlineData("chunked")]
    [InlineData("malformed")]
    public async Task InvalidRefreshClearsTheWholeSnapshot(string invalid)
    {
        var transport = new Transport { Response = () => Response(Art()) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        transport.Response = () =>
        {
            var response = Response(Art());
            if (invalid == "type") response.Content.Headers.ContentType = new("text/html");
            if (invalid == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
            if (invalid == "length") response.Content.Headers.ContentLength = L12SharedGalleryCatalog.MaximumBytes + 1L;
            if (invalid == "chunked")
            {
                response.Content = new StreamContent(new NonSeekStream(new byte[L12SharedGalleryCatalog.MaximumBytes + 1]));
                response.Content.Headers.ContentType = new("application/json");
            }
            if (invalid == "malformed") response.Content = new StringContent("[{", Encoding.UTF8, "application/json");
            return response;
        };
        Assert.False(await source.RefreshAsync());
        Assert.Null(source.Current);
    }

    [Fact]
    public async Task SlowTransportDoesNotExtendStartupOrShutdownAndCannotPublishAfterStop()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var source = new L12SharedGalleryCatalog(new Transport { Pending = completion.Task });
        var elapsed = Stopwatch.StartNew();
        await source.StartAsync();
        Assert.Null(source.Current);
        await source.DisposeAsync();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(7));
        completion.TrySetResult(Response(Art()));
        Assert.Null(source.Current);
    }

    [Fact]
    public async Task SameDefinitionsDrivePickerGrantOwnedNotificationAndBattleUrlsWithLocalEntitlementsOnly()
    {
        var art = Art();
        var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var path = StorePath();
        var store = NewStore(path, source);
        var admin = store.Login("Admin", "L12master").Account!;
        var owner = store.Register("共享权益甲", Password).Account!;
        var other = store.Register("共享权益乙", Password).Account!;
        Assert.Single(store.AlternateArts()); Assert.Single(store.AdminAlternateArts());
        Assert.Equal(art.Id, Assert.Single(store.SearchAlternateArts(null, null, null).Items).Id);
        Assert.Empty(store.OwnedAlternateArts(owner.Id));
        var grant = store.GrantAlternateArt(admin, new(art.Id, owner.Id, "manual", "本地派发"));
        Assert.Equal(owner.Id, grant.AccountId);
        Assert.Equal(art.ImageUrl, Assert.Single(store.OwnedAlternateArts(owner.Id)).ImageUrl);
        Assert.Equal(art.ImageUrl, Assert.Single(store.PendingAlternateArtGrantNotifications(owner.Id)).ImageUrl);
        Assert.Empty(store.OwnedAlternateArts(other.Id));
        var choices = new Dictionary<string, string> { [art.BaseCardId] = art.Id };
        var copies = new Dictionary<string, List<string>> { [art.BaseCardId] = [art.Id] };
        Assert.Equal(art.ImageUrl, store.ResolveOwnedAlternateArtUrls(owner.Id, choices, copies)[art.BaseCardId]);
        Assert.Equal(art.ImageUrl, store.ResolveOwnedAlternateArtUrls(owner.Id, choices, copies)[art.BaseCardId + "#1"]);
        Assert.Empty(store.ResolveOwnedAlternateArtUrls(other.Id, choices, copies));
        Assert.Equal(1, transport.Calls); // Every platform read/write above is memory-only.
        var reloaded = NewStore(path, source);
        Assert.Single(reloaded.AlternateArtGrants());
        Assert.Equal(art.Id, Assert.Single(reloaded.OwnedAlternateArts(owner.Id)).Id);
        transport.Response = () => Response();
        Assert.True(await source.RefreshAsync());
        Assert.Empty(store.OwnedAlternateArts(owner.Id)); Assert.Empty(store.PendingAlternateArtGrantNotifications(owner.Id));
        Assert.Empty(store.ResolveOwnedAlternateArtUrls(owner.Id, choices, copies));
        Assert.Single(store.AlternateArtGrants()); // Definition removal neither deletes local grants nor imports accounts.
        Assert.Throws<KeyNotFoundException>(() => store.GrantAlternateArt(admin, new(art.Id, owner.Id, "manual")));
    }

    [Fact]
    public async Task PublicFutureRulesAndGalleryCardsNeverBecomeGrantableOrBuildable()
    {
        var unknown = Art() with { Id = "future", BaseCardId = "FUTURE-NOT-IN-TEST" };
        var gallery = Art() with { Id = "gallery", Kind = "gallery-card", BaseCardId = "" };
        var transport = new Transport { Response = () => Response(unknown, gallery) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source);
        var admin = store.Login("Admin", "L12master").Account!;
        foreach (var art in new[] { unknown, gallery })
        {
            Assert.Throws<ArgumentException>(() => store.GrantAlternateArt(admin, new(art.Id, admin.Id, "manual")));
            Assert.Throws<ArgumentException>(() => store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "event", "", "synthetic", 0)));
        }
        Assert.Equal(2, store.AlternateArts().Count);
        Assert.Empty(store.AlternateArtGrants()); Assert.Empty(store.OwnedAlternateArts(admin.Id));
        Assert.Empty(store.ResolveOwnedAlternateArtUrls(admin.Id, new Dictionary<string, string> { [unknown.BaseCardId] = unknown.Id }));
    }

    [Fact]
    public async Task FailedSourcePreservesEveryExistingDeckPreferenceAndNeverUsesLocalDefinitions()
    {
        var art = Art(); var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var path = StorePath();
        var local = NewStore(path);
        var admin = local.Login("Admin", "L12master").Account!;
        var localArt = local.AlternateArts().First();
        local.GrantAlternateArt(admin, new(localArt.Id, admin.Id, "manual", "old-local"));
        var store = NewStore(path, source);
        store.GrantAlternateArt(admin, new(art.Id, admin.Id, "manual", "shared-local-grant"));
        var deck = Deck(art, "before");
        var saved = store.UpsertDeck(admin.Id, deck);
        store.PublishDeck(admin.Id, deck, null);
        var before = JsonSerializer.Serialize(store.Decks(admin.Id), Wire);
        var publishedBefore = JsonSerializer.Serialize(store.PublishedDecks(admin.Id), Wire);
        transport.Response = () => new(HttpStatusCode.ServiceUnavailable);
        Assert.False(await source.RefreshAsync());
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.AlternateArts());
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.AdminAlternateArts());
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.GrantAlternateArt(admin, new(art.Id, admin.Id, "manual")));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.UpsertDeck(admin.Id, Deck(art, "before", "changed")));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.UpdateDeck(admin.Id, saved.Id, saved.Revision, Deck(art, "changed")));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.CreateDeck(admin.Id, Deck(art, "new-failed")));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.PublishDeck(admin.Id, Deck(art, "published-failed"), null));
        Assert.Equal(before, JsonSerializer.Serialize(store.Decks(admin.Id), Wire));
        Assert.Equal(publishedBefore, JsonSerializer.Serialize(store.PublishedDecks(admin.Id), Wire));
        Assert.Empty(store.OwnedAlternateArts(admin.Id));
        transport.Response = () => Response(art);
        Assert.True(await source.RefreshAsync());
        Assert.Single(store.OwnedAlternateArts(admin.Id));
        Assert.True(store.UpdateDeck(admin.Id, saved.Id, saved.Revision, Deck(art, "after")).Success);
        Assert.Equal(art.Id, store.Decks(admin.Id).Single(row => row.Id == saved.Id).AlternateArtSelections![art.BaseCardId]);
        Assert.Contains(localArt.Id, NewStore(path).AlternateArts().Select(row => row.Id));
    }

    [Fact]
    public async Task SharedModeRejectsLocalDefinitionWritesButKeepsLocalGrantRuleAndAuditManagement()
    {
        var art = Art(); await using var source = new L12SharedGalleryCatalog(new Transport { Response = () => Response(art) });
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source); var admin = store.Login("Admin", "L12master").Account!;
        var before = store.Version;
        Action[] writes =
        [
            () => store.SaveAlternateArt(admin, new(null, "X", art.BaseCardId, "x", "media")),
            () => store.SaveAlternateArtMetadata(admin, new(art.Id, [], [])),
            () => store.SaveAlternateArtImage(admin, new(art.Id, "media")),
            () => store.SaveAlternateArtProduct(admin, new(null, "product")),
            () => store.MergeAlternateArtProducts(admin, new("target", ["source"])),
            () => store.SaveAlternateArtRarity(admin, new(null, "R", "rarity")),
            () => store.SaveRegularGalleryCardMetadata(admin, new(art.BaseCardId, [], [])),
            () => store.SaveRegularGalleryCardImage(admin, new(art.BaseCardId, "media")),
            () => store.SaveGalleryProductFilterVisibility(admin, new("product", false)),
        ];
        foreach (var write in writes) Assert.Throws<ArgumentException>(write);
        Assert.Equal(before, store.Version);
        var rule = store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "event", "", "event-local", 0));
        Assert.Single(store.AlternateArtAwardRules());
        Assert.Single(store.DispatchAlternateArtEvent(admin, new(rule.Id, [admin.Id])));
        var grant = Assert.Single(store.AlternateArtGrants()); store.RevokeAlternateArtGrant(admin, grant.Id);
        Assert.Empty(store.OwnedAlternateArts(admin.Id));
        Assert.True(store.Version > before);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("removed")]
    [InlineData("inactive")]
    public async Task ExistingUnchangedActiveRuleCanBeDeactivatedLocallyWithoutItsPublicDefinition(string state)
    {
        var art = Art(); var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var path = StorePath(); var store = NewStore(path, source);
        var admin = store.Login("Admin", "L12master").Account!;
        var rule = store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "event", "season-kept", "event-kept", 2,
            MasterId: "master-kept"));
        transport.Response = state switch
        {
            "removed" => () => Response(),
            "inactive" => () => Response(art with { Active = false }),
            _ => () => new(HttpStatusCode.ServiceUnavailable),
        };
        Assert.Equal(state != "unavailable", await source.RefreshAsync());
        var calls = transport.Calls;
        var draft = RuleDraft(rule) with { Active = false };
        var stopped = store.SaveAlternateArtAwardRule(admin, draft, new("shared-local-stop"));
        Assert.Equal(rule with { Active = false, UpdatedAt = stopped.UpdatedAt }, stopped);
        Assert.Equal(calls, transport.Calls);
        var audit = Assert.Single(store.AdminAudit(category: "alternate-art-award-rule")
            .Where(item => item.Action == "update"));
        Assert.Equal(admin.Id, audit.ActorId); Assert.Equal(rule.Id, audit.Target);
        Assert.Equal("shared-local-stop", audit.CorrelationId);
        Assert.True(JsonSerializer.Deserialize<L12AlternateArtAwardRuleView>(audit.FromValue!)!.Active);
        Assert.False(JsonSerializer.Deserialize<L12AlternateArtAwardRuleView>(audit.ToValue!)!.Active);
        Assert.False(Assert.Single(NewStore(path, source).AlternateArtAwardRules()).Active);
        Assert.Empty(store.AlternateArtGrants());
    }

    [Theory]
    [InlineData("new")]
    [InlineData("unknown-id")]
    [InlineData("other-art")]
    [InlineData("other-kind")]
    [InlineData("other-season")]
    [InlineData("other-event")]
    [InlineData("other-master")]
    [InlineData("other-minimum")]
    [InlineData("still-active")]
    [InlineData("already-inactive")]
    [InlineData("reactivate")]
    public async Task LocalDeactivationCannotCreateRebindReactivateOrEditAnyBusinessField(string change)
    {
        var art = Art(); var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source); var admin = store.Login("Admin", "L12master").Account!;
        var rule = store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "event", "season-kept", "event-kept", 2,
            Active: change is not ("already-inactive" or "reactivate"), MasterId: "master-kept"));
        var draft = RuleDraft(rule) with { Active = false };
        draft = change switch
        {
            "new" => draft with { Id = null },
            "unknown-id" => draft with { Id = "not-an-existing-rule" },
            "other-art" => draft with { AlternateArtId = "not-the-same-art" },
            "other-kind" => draft with { Kind = "rank-reached" },
            "other-season" => draft with { SeasonId = "another-season" },
            "other-event" => draft with { EventId = "another-event" },
            "other-master" => draft with { MasterId = "another-master" },
            "other-minimum" => draft with { MinimumTierIndex = 1 },
            "still-active" or "reactivate" => draft with { Active = true },
            _ => draft,
        };
        transport.Response = () => new(HttpStatusCode.ServiceUnavailable); Assert.False(await source.RefreshAsync());
        var before = FullState(store);
        Assert.Throws<L12PlatformStorageUnavailableException>(() => store.SaveAlternateArtAwardRule(admin, draft));
        Assert.Equal(before, FullState(store));
        Assert.Empty(store.AdminAudit(category: "alternate-art-award-rule").Where(item => item.Action == "update"));
    }

    private static L12AlternateArtAwardRuleDraft RuleDraft(L12AlternateArtAwardRuleView rule)
        => new(rule.Id, rule.AlternateArtId, rule.Kind, rule.SeasonId, rule.EventId, rule.MinimumTierIndex,
            rule.Active, rule.MasterId);

    [Fact]
    public void OrdinaryModePreservesExistingOfficialDefinitionOnlyCallersWithoutInventedRuleCards()
    {
        var definition = new L12OfficialAlternateArtDefinition("legacy", "LEGACY", "legacy-base", "旧原卡", "LEGACY", [], []);
        var store = new L12PlatformStore(StorePath(), officialAlternateArts: [definition]);
        var admin = store.Login("Admin", "L12master").Account!;
        store.GrantAlternateArt(admin, new(definition.Id, admin.Id, "manual"));
        Assert.Single(store.OwnedAlternateArts(admin.Id));
        Assert.Equal("l12-card-id:LEGACY", store.ResolveOwnedAlternateArtUrls(admin.Id,
            new Dictionary<string, string> { [definition.BaseCardId] = definition.Id })[definition.BaseCardId]);
    }

    [Fact]
    public async Task RealRankedOutboxSurvivesSourceFailureRollsBackAndRetriesExactlyOnce()
    {
        var art = Art(); var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var path = StorePath(); var store = NewStore(path, source);
        var admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register("共享排位甲", Password).Account!;
        var second = store.Register("共享排位乙", Password).Account!;
        store.SelectRankedFaction(first.Id, "order"); store.SelectRankedFaction(second.Id, "chaos");
        var season = store.SeasonCatalog(admin).Current.SeasonId;
        store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "rank-reached", season, "", 0));
        var now = DateTimeOffset.UtcNow.AddMinutes(-20); var started = now;
        var deck = Catalog.DeckAt(0);
        var engine = new L12GameEngine(Catalog, "shared-outbox-" + Guid.NewGuid().ToString("N"), "SHARE01", 20261023,
            [first.Username, second.Username], [deck, deck], skipPreparation: true, utcNow: () => now);
        engine.State.Round = 12;
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(path)!, "matches.db"), () => now);
        recorder.AttachCatalog(Catalog); await recorder.InitializeAsync();
        L12RankedRuntimeCheckpoint Runtime(long sequence, string status) => new(1, engine.State.MatchId,
            engine.State.RoomCode, status, sequence + 1, sequence, engine.State.Revision, engine.ComputeStateHash(),
            started, 40, [900_000, 900_000], [120_000, 120_000], [false, false], now,
            status == "completed" ? "surrender" : null, false, [true, true], [null, null],
            ["shared-network-a", "shared-network-b"], [1, 1], now);
        await recorder.StartRankedAsync(engine, first.Id, second.Id, [deck, deck], Runtime(0, "active"));
        now = now.AddMinutes(20);
        var command = new L12Command("surrender"); var result = engine.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        var envelope = new L12RankedSettlementEnvelope(2, engine.State.MatchId, first.Id, second.Id,
            deck.MasterId, deck.MasterId, 1, started, now, 40, "surrender", "shared-network-a", "shared-network-b",
            12, "shared-browser-a", "shared-browser-b", season);
        await recorder.AppendRankedAsync(engine, 1, 0, JsonSerializer.Serialize(command, Wire), result,
            Runtime(1, "completed"), envelope);
        var before = FullState(store);
        transport.Response = () => new(HttpStatusCode.ServiceUnavailable);
        Assert.False(await source.RefreshAsync());
        var rooms = new L12RoomManager(Catalog, recorder, store);
        var failed = await Drain(rooms);
        Assert.Equal((0, 1), failed);
        Assert.Equal(before, FullState(store));
        Assert.Null(store.RankedSettlement(engine.State.MatchId, first.Id));
        Assert.Empty(store.AlternateArtGrants());
        var pending = Assert.Single(await recorder.ListPendingRankedSettlementsAsync());
        Assert.Equal("pending", pending.Status); Assert.Equal(1, pending.Attempts);
        transport.Response = () => Response(art); Assert.True(await source.RefreshAsync());
        Assert.Equal((1, 0), await Drain(rooms));
        Assert.Empty(await recorder.ListPendingRankedSettlementsAsync());
        Assert.Equal(2, store.AlternateArtGrants().Count);
        Assert.Single(store.OwnedAlternateArts(first.Id)); Assert.Single(store.OwnedAlternateArts(second.Id));
        Assert.Equal((0, 0), await Drain(rooms));
        Assert.Equal(2, NewStore(path, source).AlternateArtGrants().Count);
    }

    [Fact]
    public async Task SourceFailureDoesNotBlockUnrelatedSettlementAndSeasonAwardsRollBackThenRecover()
    {
        var art = Art(); var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source); var admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register("共享季末甲", Password).Account!;
        var second = store.Register("共享季末乙", Password).Account!;
        store.SelectRankedFaction(first.Id, "order"); store.SelectRankedFaction(second.Id, "chaos");
        transport.Response = () => new(HttpStatusCode.ServiceUnavailable); Assert.False(await source.RefreshAsync());
        store.SettleRankedMatch("no-gallery-rule", first.Id, second.Id, 0);
        Assert.NotNull(store.RankedSettlement("no-gallery-rule", first.Id));
        transport.Response = () => Response(art); Assert.True(await source.RefreshAsync());
        var seasons = store.SeasonCatalog(admin); var draft = seasons.Next!;
        var updated = store.UpdateSeasonDraft(admin, draft.DefinitionId,
            new("SHARED-NEXT", "共享资料恢复下季", DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow.AddDays(120), draft.Configuration),
            draft.Revision, "synthetic-next-season", new("shared-season-draft"));
        store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "season-final", seasons.Current.SeasonId, "", 0));
        var before = FullState(store);
        L12SeasonActivationView Activate() => store.ActivateSeason(admin, updated.DefinitionId,
            seasons.Current.Revision, updated.Revision, "synthetic-shared-activation",
            new(seasons.Current.SeasonId, 0, 0, 0, 0), new("shared-season-activate"));
        transport.Response = () => new(HttpStatusCode.ServiceUnavailable); Assert.False(await source.RefreshAsync());
        Assert.Throws<L12PlatformStorageUnavailableException>(Activate);
        Assert.Equal(before, FullState(store));
        Assert.Empty(store.AlternateArtGrants()); Assert.Empty(store.SeasonArchives(admin));
        transport.Response = () => Response(art); Assert.True(await source.RefreshAsync());
        Assert.True(Activate().Activated);
        Assert.Equal(2, store.AlternateArtGrants().Count); Assert.Single(store.SeasonArchives(admin));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RankedAwardOperationPinsOneGenerationAcrossBothPlayers(bool inactiveReplacement)
    {
        var clock = new Clock(); var art = Art();
        var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport, clock);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source); var admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register("原子排位甲", Password).Account!;
        var second = store.Register("原子排位乙", Password).Account!;
        store.SelectRankedFaction(first.Id, "order"); store.SelectRankedFaction(second.Id, "chaos");
        var season = store.SeasonCatalog(admin).Current.SeasonId;
        store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "rank-reached", season, "", 0));
        ArmValidRefreshDuringCurrentRead(clock, source, transport, art, inactiveReplacement);
        store.SettleRankedMatch("atomic-generation", first.Id, second.Id, 0);
        Assert.Equal(2, transport.Calls);
        Assert.Null(clock.OnNextTimestamp);
        var grants = store.AlternateArtGrants();
        Assert.Equal(2, grants.Count);
        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(id => id), grants.Select(row => row.AccountId).OrderBy(id => id));
        Assert.All(grants, row => Assert.Equal("rank-reached", row.SourceKind));
        Assert.True(inactiveReplacement ? source.RequireCurrent().Arts.Single().Active == false : source.RequireCurrent().Arts.Count == 0);
        store.SettleRankedMatch("atomic-generation", first.Id, second.Id, 0);
        Assert.Equal(2, store.AlternateArtGrants().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeasonAwardOperationPinsOneGenerationAcrossProfilesAndMasterChampion(bool inactiveReplacement)
    {
        var clock = new Clock(); var art = Art();
        var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport, clock);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source); var admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register("原子季末甲", Password).Account!;
        var second = store.Register("原子季末乙", Password).Account!;
        store.SelectRankedFaction(first.Id, "order"); store.SelectRankedFaction(second.Id, "chaos");
        store.SettleRankedMatch("atomic-season-participation", first.Id, second.Id, 0);
        var masters = Catalog.Cards.Values.Where(card => card.CardType == "master" && card.Id != "S01-02M2")
            .OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase).Take(2).Select(card => card.Id).ToArray();
        var opponents = new[] { second.Id }.Concat(Enumerable.Range(0, 9)
            .Select(index => store.Register("原子主宰对手" + index, Password).Account!.Id)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var facts = Enumerable.Range(0, 20).Select(index => new L12RankedMasterTitleMatchFact(
            "atomic-champion-fact-" + index, first.Id, opponents[index % opponents.Length], masters[0], masters[1],
            0, 12, now.AddDays(-(index % 5)).AddMinutes(-index - 1), "surrender", true)).ToArray();
        Assert.Equal(20, store.ImportRankedMasterTitleFacts(facts));
        Assert.Equal(first.Username, Assert.Single(store.RankedMasterChampions(), row => row.MasterId == masters[0]).Username);
        var seasons = store.SeasonCatalog(admin); var draft = seasons.Next!;
        var updated = store.UpdateSeasonDraft(admin, draft.DefinitionId,
            new("ATOMIC-NEXT", "原子图库下季", now.AddDays(30), now.AddDays(120), draft.Configuration),
            draft.Revision, "synthetic-atomic-season", new("atomic-season-draft"));
        store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "season-final", seasons.Current.SeasonId, "", 0));
        store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "master-champion-season-final", seasons.Current.SeasonId, "", 0,
            MasterId: masters[0]));
        ArmValidRefreshDuringCurrentRead(clock, source, transport, art, inactiveReplacement);
        var activated = store.ActivateSeason(admin, updated.DefinitionId, seasons.Current.Revision, updated.Revision,
            "synthetic-atomic-cutover", new(seasons.Current.SeasonId, 0, 0, 0, 0), new("atomic-season-activate"));
        Assert.True(activated.Activated); Assert.Equal(2, transport.Calls); Assert.Null(clock.OnNextTimestamp);
        var grants = store.AlternateArtGrants();
        Assert.Equal(3, grants.Count);
        Assert.Equal(2, grants.Count(row => row.SourceKind == "season-final"));
        Assert.Equal(first.Id, Assert.Single(grants, row => row.SourceKind == "master-champion-season-final").AccountId);
        Assert.True(inactiveReplacement ? source.RequireCurrent().Arts.Single().Active == false : source.RequireCurrent().Arts.Count == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnmatchedOrInactiveAwardRuleDoesNotReadTheSharedSnapshot(bool inactiveRule)
    {
        var clock = new Clock(); var art = Art(); var transport = new Transport { Response = () => Response(art) };
        await using var source = new L12SharedGalleryCatalog(transport, clock);
        Assert.True(await source.RefreshAsync());
        var store = NewStore(source: source); var admin = store.Login("Admin", "L12master").Account!;
        var first = store.Register("无匹配原子甲", Password).Account!; var second = store.Register("无匹配原子乙", Password).Account!;
        store.SelectRankedFaction(first.Id, "order"); store.SelectRankedFaction(second.Id, "chaos");
        var season = store.SeasonCatalog(admin).Current.SeasonId;
        store.SaveAlternateArtAwardRule(admin, new(null, art.Id, "rank-reached", inactiveRule ? season : "OTHER-SEASON", "", 0,
            Active: !inactiveRule));
        clock.OnNextTimestamp = () => throw new InvalidOperationException("No matching rule must not read Current");
        store.SettleRankedMatch("unmatched-rule-generation", first.Id, second.Id, 0);
        Assert.NotNull(clock.OnNextTimestamp); Assert.Equal(1, transport.Calls); Assert.Empty(store.AlternateArtGrants());
    }

    private static void ArmValidRefreshDuringCurrentRead(Clock clock, L12SharedGalleryCatalog source,
        Transport transport, L12AlternateArtView art, bool inactiveReplacement)
    {
        // Current already captured generation A before asking its existing TimeProvider for age.
        // Publish valid generation B exactly once at that seam; no product hook or timing sleep.
        clock.OnNextTimestamp = () =>
        {
            transport.Response = () => inactiveReplacement ? Response(art with { Active = false }) : Response();
            Assert.True(source.RefreshAsync().GetAwaiter().GetResult());
        };
    }

    private static string FullState(L12PlatformStore store)
        => JsonSerializer.Serialize(typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store), Wire);
    private static Task<(int Applied, int Failed)> Drain(L12RoomManager rooms)
        => (Task<(int Applied, int Failed)>)typeof(L12RoomManager).GetMethod("DrainRankedSettlementOutboxAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(rooms, [null, false])!;

    private static L12PlatformStore NewStore(string? path = null, L12SharedGalleryCatalog? source = null)
        => new(path ?? StorePath(), Catalog.PresetDecks, officialCards: Catalog.Cards,
            officialAlternateArts: Catalog.OfficialAlternateArts, officialCardProducts: Catalog.CardProducts, sharedGallery: source);
    private static string StorePath() => Path.Combine(Path.GetTempPath(), "l12-shared-gallery-" + Guid.NewGuid().ToString("N"), "platform.json");
    private static L12AlternateArtView Art() => new("shared-only", "SHARED-001", Catalog.DeckAt(0).CardIds[0], "共享异画",
        "media", "/api/site/media/media/desktop/" + new string('a', 64) + ".webp",
        "/api/site/media/media/thumbnail/" + new string('b', 64) + ".webp", true,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static L12PresetDeckDefinition Deck(L12AlternateArtView art, string name, string? master = null) => new()
    {
        Name = name, MasterId = master ?? Catalog.DeckAt(0).MasterId, CardIds = [art.BaseCardId], MoraleIds = [],
        AlternateArtSelections = new() { [art.BaseCardId] = art.Id }, AlternateArtCopies = new() { [art.BaseCardId] = [art.Id] },
    };
    private static JsonObject Node(L12AlternateArtView art) => JsonSerializer.SerializeToNode(art, Wire)!.AsObject();
    private static IReadOnlyList<L12AlternateArtView> ParseNode(JsonObject node)
        => L12SharedGalleryCatalog.Parse(Encoding.UTF8.GetBytes("[" + node.ToJsonString() + "]"));
    private static HttpResponseMessage Response(params L12AlternateArtView[] arts)
        => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(arts, Wire), Encoding.UTF8, "application/json") };
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal Action? OnNextTimestamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp()
        {
            Interlocked.Exchange(ref OnNextTimestamp, null)?.Invoke();
            return Now.UtcTicks;
        }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
    private sealed class Transport : HttpMessageHandler
    {
        internal Func<HttpResponseMessage> Response = () => new(HttpStatusCode.ServiceUnavailable);
        internal Task<HttpResponseMessage>? Pending;
        internal int Calls;
        internal HttpMethod? Method;
        internal Uri? Uri;
        internal string[] HeaderNames = [];
        internal HttpContent? Content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Method = request.Method; Uri = request.RequestUri;
            HeaderNames = request.Headers.Select(item => item.Key).ToArray(); Content = request.Content;
            return Pending ?? Task.FromResult(Response());
        }
    }
    private sealed class NonSeekStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private static L12SiteMediaView Upload(L12PlatformStore store, L12AccountView admin)
    {
        var policy = L12PlatformStore.SiteMediaPolicies().Single(row => row.Kind == "card-art");
        return store.UploadSiteMedia(admin, new("card-art", "synthetic.webp", "image/webp",
            Webp(policy.DesktopWidth, policy.DesktopHeight), Webp(policy.DesktopWidth, policy.DesktopHeight),
            Webp(policy.MobileWidth, policy.MobileHeight), Webp(policy.ThumbnailWidth, policy.ThumbnailHeight), "合成图库图", .5, .5));
    }
    // Same metadata-only fixture as the existing media tests; not a browser-pixel assertion.
    private static byte[] Webp(int width, int height)
    {
        var bytes = new byte[30]; Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        var w = width - 1; var h = height - 1;
        bytes[24] = (byte)w; bytes[25] = (byte)(w >> 8); bytes[26] = (byte)(w >> 16);
        bytes[27] = (byte)h; bytes[28] = (byte)(h >> 8); bytes[29] = (byte)(h >> 16);
        return bytes;
    }
}
