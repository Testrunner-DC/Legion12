using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeckSummaryHttpTests
{
    [Theory]
    [InlineData("summaries")]
    [InlineData("detail")]
    public async Task NewPrivateReadRoutesUseCurrentOwnerWithoutExpandingOtherBodies(string kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-c-q1-http-" + Guid.NewGuid().ToString("N"));
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
            var login = store.Register("cq1owner", "password-123");
            Assert.True(login.Success, login.Message);
            var other = store.Register("cq1other", "password-123");
            Assert.True(other.Success, other.Message);
            var source = catalog.PresetDecks[0];
            L12AccountDeckView? selected = null;
            for (var index = 0; index < 6; index++)
                selected = store.CreateDeck(login.Account!.Id, new L12PresetDeckDefinition
                {
                    Name = "owned " + index, MasterId = source.MasterId, CardIds = [.. source.CardIds],
                    MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
                }).Deck!;
            Assert.True(store.CreateDeck(other.Account!.Id, new L12PresetDeckDefinition
            {
                Name = "private other", MasterId = source.MasterId, CardIds = [.. source.CardIds],
                MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
            }).Success);
            var expansions = new List<string>();
            store.DeckPayloadExpansionObserver = expansions.Add;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var target = kind == "summaries" ? "/api/decks/summaries?page=1&pageSize=2"
                : $"/api/decks/by-id/{selected!.Id}?expectedRevision={selected.Revision}";
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token!);
            using var response = await client.SendAsync(request);
            Console.WriteLine($"CQ1 HTTP {target}: status={(int)response.StatusCode}, expansions={expansions.Count}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (kind == "summaries")
            {
                Assert.Equal(6, json.RootElement.GetProperty("total").GetInt32());
                Assert.Equal(2, json.RootElement.GetProperty("items").GetArrayLength());
                foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
                {
                    Assert.StartsWith("owned ", item.GetProperty("name").GetString());
                    foreach (var forbidden in new[] { "cardIds", "moraleIds", "specialIds", "benchIds",
                        "alternateArtCopies", "alternateArtSelections", "payloadHash", "code", "environment" })
                        Assert.False(item.TryGetProperty(forbidden, out _));
                }
                Assert.Empty(expansions);
            }
            else
            {
                Assert.Equal(selected!.Id, json.RootElement.GetProperty("id").GetString());
                Assert.Equal(source.CardIds.Count, json.RootElement.GetProperty("cardIds").GetArrayLength());
                Assert.Single(expansions);
            }
            passed = true;
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ1 HTTP fixture retained: " + root);
        }
    }

    [Fact]
    public async Task PrivateReadRoutesRejectMalformedQueriesAndAnonymousRequestsBeforeExpansion()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-cq1-http-guards-" + Guid.NewGuid().ToString("N"));
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
            var login = store.Register("cq1guards", "password-123");
            Assert.True(login.Success);
            var source = catalog.PresetDecks[0];
            var created = store.CreateDeck(login.Account!.Id, new L12PresetDeckDefinition
            {
                Name = "guards", MasterId = source.MasterId, CardIds = [.. source.CardIds],
                MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
            }).Deck!;
            var expansions = new List<string>();
            store.DeckPayloadExpansionObserver = expansions.Add;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var detail = "/api/decks/by-id/" + created.Id;
            foreach (var path in new[] { "/api/decks/summaries", detail })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            foreach (var query in new[] { "page=0", "page=-1", "page=2147483648", "page=", "pageSize=0",
                "pageSize=101", "pageSize=1.5", "page=1&page=2", "legal=1", "legal=", "sort=views",
                "environment=current", "ownerId=other", "Page=1" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/decks/summaries?" + query);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token!);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            foreach (var query in new[] { "expectedRevision=", "expectedRevision=0", "expectedRevision=-1",
                "expectedRevision=9223372036854775808", "expectedRevision=1&expectedRevision=2", "name=guards" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, detail + "?" + query);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token!);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
            else Console.WriteLine("Failed synthetic CQ1 HTTP guards fixture retained: " + root);
        }
    }
}
