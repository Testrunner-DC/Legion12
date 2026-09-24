using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckDetailContentTests
{
    [Fact]
    public async Task ContentEndpointRequiresThePublicationAuthor()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var ownerLogin = store.Register("tdapiown", "password-123");
            var otherLogin = store.Register("tdapioth", "password-123");
            var preset = catalog.PresetDecks[0];
            var published = store.PublishDeck(ownerLogin.Account!.Id, new L12PresetDeckDefinition
            {
                Name = "接口权限", MasterId = preset.MasterId, CardIds = [.. preset.CardIds],
                MoraleIds = [.. preset.MoraleIds], SpecialIds = [.. preset.SpecialIds],
            }, null)!;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            var body = Content("接口保存", "M2");
            using (var anonymous = await client.PutAsJsonAsync($"/api/public-decks/{published.Id}/content", body))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using (var forbiddenRequest = new HttpRequestMessage(HttpMethod.Put,
                       $"/api/public-decks/{published.Id}/content") { Content = JsonContent.Create(body) })
            {
                forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherLogin.Token);
                using var forbidden = await client.SendAsync(forbiddenRequest);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }
            using (var ownerRequest = new HttpRequestMessage(HttpMethod.Put,
                       $"/api/public-decks/{published.Id}/content") { Content = JsonContent.Create(body) })
            {
                ownerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerLogin.Token);
                using var saved = await client.SendAsync(ownerRequest);
                Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            }
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AuthorContentUsesDeduplicatedAppendOnlyRevisionsAndSurvivesRestart()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var owner = store.Register("tdetowner", "password-123").Account!;
            var published = store.PublishDeck(owner.Id, Deck("长期详情", "C1", "C2"), null)!;
            var first = store.UpdatePublicDeckContent(owner.Id, published.Id, Content("先手保留低费", "M2"))!;
            var unchanged = store.UpdatePublicDeckContent(owner.Id, published.Id, Content("先手保留低费", "M2"))!;
            var second = store.UpdatePublicDeckContent(owner.Id, published.Id, Content("后手也保留低费", "M2"))!;

            Assert.Equal(1, first.ContentRevision);
            Assert.Equal(1, unchanged.ContentRevision);
            Assert.Equal(2, second.ContentRevision);
            using (var connection = Open(store.TransactionalStoragePath))
            {
                Assert.Equal("2", Scalar(connection,
                    $"SELECT COUNT(*) FROM published_deck_content_revisions WHERE publication_id='{published.Id}';"));
                Assert.Equal("2", Scalar(connection, "SELECT COUNT(*) FROM published_deck_content_payloads;"));
            }

            var reloaded = new L12PlatformStore(path);
            var restored = reloaded.PublicDeckDetails(published.Id)!;
            Assert.Equal("后手也保留低费", restored.Guide.Opening);
            Assert.Equal("M2", Assert.Single(restored.Matchups).OpponentMasterId);
            Assert.Equal(2, restored.ContentRevision);
            Assert.Equal(2, reloaded.StorageStatus().DeckStorage!.ContentPayloads);
            Assert.Equal(2, reloaded.StorageStatus().DeckStorage!.ContentRevisions);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void OnlyAuthorCanWriteAndInvalidPlainTextIsRejected()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var owner = store.Register("tdetauth", "password-123").Account!;
            var stranger = store.Register("tdetother", "password-123").Account!;
            var published = store.PublishDeck(owner.Id, Deck("权限牌库", "C1"), null)!;

            Assert.Throws<UnauthorizedAccessException>(() =>
                store.UpdatePublicDeckContent(stranger.Id, published.Id, Content("越权", "M2")));
            Assert.Throws<ArgumentException>(() => store.UpdatePublicDeckContent(owner.Id, published.Id,
                Content("<script>alert(1)</script>", "M2")));
            Assert.Throws<ArgumentException>(() => store.UpdatePublicDeckContent(owner.Id, published.Id,
                Content(new string('a', 1201), "M2")));
            Assert.Throws<ArgumentException>(() => store.UpdatePublicDeckContent(owner.Id, published.Id,
                new L12PublicDeckContentInput(new("", "", "", "", ""),
                    [new("M2", "", "", ""), new("m2", "", "", "")])));
            Assert.Throws<ArgumentException>(() => store.UpdatePublicDeckContent(owner.Id, published.Id,
                new L12PublicDeckContentInput(new("", "", "", "", ""),
                    [new("", "不能缺少主宰", "", "")])));
            var cleared = store.UpdatePublicDeckContent(owner.Id, published.Id,
                new L12PublicDeckContentInput(new("   ", "", "", "", ""), []))!;
            Assert.Equal(string.Empty, cleared.Guide.BuildIdea);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void AllVersionsExposeFullDecksAndQuantityDiffsWithoutInventingMatches()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var owner = store.Register("tdetvers", "password-123").Account!;
            L12PublishedDeckView? published = null;
            for (var index = 0; index < 25; index++)
                published = store.PublishDeck(owner.Id, Deck("版本牌库", "BASE", $"C{index:D2}"), published?.Id);

            var details = store.PublicDeckDetails(published!.Id)!;
            Assert.Equal(25, details.Versions.Count);
            Assert.Equal(25, details.Versions[0].Version);
            Assert.Equal(1, details.Versions[^1].Version);
            Assert.Contains(details.Versions[0].Changes,
                change => change.CardId == "C24" && change.PreviousQuantity == 0 && change.CurrentQuantity == 1);
            Assert.Contains(details.Versions[0].Changes,
                change => change.CardId == "C23" && change.PreviousQuantity == 1 && change.CurrentQuantity == 0);
            Assert.Empty(details.Matches);
            Assert.Equal("unavailable", details.MatchBindingStatus);
            Assert.Contains("不会用作者总战绩替代", details.MatchBindingMessage);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static L12PresetDeckDefinition Deck(string name, params string[] cards) => new()
    {
        Name = name, MasterId = "M1", CardIds = [.. cards], MoraleIds = ["R1"], SpecialIds = [],
    };

    private static L12PublicDeckContentInput Content(string opening, string opponent) => new(
        new("强调曲线", opening, "关键牌", "常见展开", "替换建议"),
        [new(opponent, "对局思路", "关键牌", "建议换牌")]);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        connection.Open();
        return connection;
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-public-detail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
