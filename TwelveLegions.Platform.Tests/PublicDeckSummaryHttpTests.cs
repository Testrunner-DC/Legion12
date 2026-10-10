using System.Net;
using System.Net.Http.Headers;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckSummaryHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealLibraryGetCombinesOfficialAndPublicMetadataWithoutBodies(bool authenticated)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-cq2-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        var passed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), officialCards: catalog.Cards);
            var owner = store.Register("cq2author", "password-123");
            Assert.True(owner.Success, owner.Message);
            var source = catalog.PresetDecks[0];
            var publication = store.PublishDeck(owner.Account!.Id, new L12PresetDeckDefinition
            {
                Name = "CQ2 public", MasterId = source.MasterId, CardIds = [.. source.CardIds],
                MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
            }, null)!;
            var expansions = new List<string>();
            store.DeckPayloadExpansionObserver = expansions.Add;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/deck-library/summaries?pageSize=100");
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token!);
            using var response = await client.SendAsync(request);
            Console.WriteLine($"CQ2 actual GET status={(int)response.StatusCode}, expansions={expansions.Count}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(catalog.PresetDecks.Count + 1, json.RootElement.GetProperty("total").GetInt32());
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var published = Assert.Single(items.Where(item => item.GetProperty("source").GetString() == "public"));
            Assert.Equal(publication.Id, published.GetProperty("id").GetString());
            Assert.Equal(publication.PublicCode, published.GetProperty("publicCode").GetString());
            Assert.Equal(authenticated, published.GetProperty("canEdit").GetBoolean());
            Assert.Equal(authenticated ? publication.Deck.PublicationVersion : null,
                published.GetProperty("publicationVersion").ValueKind == JsonValueKind.Null ? null
                    : (int?)published.GetProperty("publicationVersion").GetInt32());
            foreach (var item in items)
            {
                foreach (var field in new[] { "cardIds", "moraleIds", "specialIds", "benchIds", "alternateArtCopies",
                    "alternateArtSelections", "payloadHash", "code", "ownerId", "likedByAccountIds" })
                    Assert.False(item.TryGetProperty(field, out _));
                Assert.False(item.GetProperty("viewerLiked").GetBoolean());
                if (item.GetProperty("source").GetString() == "official") Assert.False(item.GetProperty("canEdit").GetBoolean());
            }
            Assert.Empty(expansions);
            passed = true;
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ2 HTTP fixture retained: " + root);
        }
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("other")]
    [InlineData("owner")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task RealLibraryGetPublicationVersionIsVisibleOnlyToCurrentOwner(string viewerKind)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-cq2-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        var passed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), officialCards: catalog.Cards);
            var owner = store.Register("cq2vowner", "password-123");
            var other = store.Register("cq2vother", "password-123");
            Assert.True(owner.Success, owner.Message);
            Assert.True(other.Success, other.Message);
            var source = catalog.PresetDecks[0];
            L12PresetDeckDefinition Named(string name) => new()
            {
                Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds],
                MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
            };
            var first = store.PublishDeck(owner.Account!.Id, Named("version one"), null)!;
            var current = store.PublishDeck(owner.Account.Id, Named("version two"), first.Id)!;
            Assert.True(current.Deck.PublicationVersion is >= 2);
            Assert.Null(store.PublishedDeck(current.Id, null)!.Deck.PublicationVersion);
            Assert.Null(store.PublishedDeck(current.Id, other.Account!.Id)!.Deck.PublicationVersion);
            var ownerSession = store.AuthenticateSession("Bearer " + owner.Token)!;
            if (viewerKind == "revoked")
                Assert.Equal(1, store.RevokeOwnSession(ownerSession, ownerSession.SessionId).RevokedCount);
            if (viewerKind == "expired")
            {
                // Expire only this synthetic session in the captured store;
                // no sleep, clock hook or product mutation API is introduced.
                var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
                var sessions = ((IEnumerable)data.GetType().GetProperty("Sessions")!.GetValue(data)!).Cast<object>();
                var session = sessions.Single(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == ownerSession.SessionId);
                session.GetType().GetProperty("ExpiresAt")!.SetValue(session, DateTimeOffset.UnixEpoch);
            }
            if (viewerKind is "revoked" or "expired") Assert.Null(store.AuthenticateSession("Bearer " + owner.Token));
            var expansions = new List<string>();
            store.DeckPayloadExpansionObserver = expansions.Add;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/deck-library/summaries?source=public");
            if (viewerKind != "anonymous") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                viewerKind == "other" ? other.Token! : owner.Token!);
            using var response = await client.SendAsync(request);
            if (viewerKind is "revoked" or "expired")
            {
                Console.WriteLine($"CQ2 version guard viewer={viewerKind}, status={(int)response.StatusCode}, expansions={expansions.Count}");
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                using var anonymousResponse = await client.GetAsync("/api/deck-library/summaries?source=public");
                Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
                using var anonymous = JsonDocument.Parse(await anonymousResponse.Content.ReadAsStringAsync());
                var item = Assert.Single(anonymous.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(JsonValueKind.Null, item.GetProperty("publicationVersion").ValueKind);
                Assert.False(item.GetProperty("canEdit").GetBoolean());
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
                var version = item.GetProperty("publicationVersion");
                Console.WriteLine($"CQ2 version guard viewer={viewerKind}, status={(int)response.StatusCode}, version={version.GetRawText()}, expansions={expansions.Count}");
                Assert.Equal(current.Id, item.GetProperty("id").GetString());
                if (viewerKind == "owner") Assert.Equal(current.Deck.PublicationVersion, version.GetInt32());
                else Assert.Equal(JsonValueKind.Null, version.ValueKind);
                Assert.Equal(viewerKind == "owner", item.GetProperty("canEdit").GetBoolean());
            }
            Assert.Empty(expansions);
            passed = true;
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ2 version fixture retained: " + root);
        }
    }

    [Fact]
    public async Task RealLibraryGetRejectsMalformedQueriesAndInvalidBearerWithoutExpansion()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-cq2-http-guards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        var passed = false;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), officialCards: catalog.Cards);
            var expansions = new List<string>();
            store.DeckPayloadExpansionObserver = expansions.Add;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            foreach (var query in new[] { "page=0", "page=-1", "page=2147483648", "pageSize=101", "pageSize=0",
                "page=1&page=2", "source=private", "source=", "legal=1", "environment=3.0", "sort=random",
                "keyword=" + new string('x', 129), "masterId=" + new string('x', 65), "ownerId=private",
                "updatedAfter=2026-10-07T00:00:00", "updatedAfter=2026-10-07T00:00:00%2B08:00",
                "updatedAfter=2026-10-07", "updatedAfter=not-a-time", "keyword=a%0Ab" })
            {
                using var response = await client.GetAsync("/api/deck-library/summaries?" + query);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            using var badToken = new HttpRequestMessage(HttpMethod.Get, "/api/deck-library/summaries");
            badToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-cq2-token");
            using var badResponse = await client.SendAsync(badToken);
            Assert.Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode);
            Assert.Empty(expansions);
            passed = true;
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ2 HTTP guards fixture retained: " + root);
        }
    }
}
