using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckCurrentDetailHttpTests
{
    [Theory]
    [InlineData("current", 1)]
    [InlineData("versions", 0)]
    [InlineData("versions/1", 1)]
    public async Task ActualNewReadRoutesHaveBoundedBodyConsumers(string suffix, int expectedExpansions)
    {
        await using var fixture = await PublicDeckReadFixture.Start(3);
        fixture.Observe();
        using var response = await fixture.Get(suffix);
        Console.WriteLine($"CQ3 read {suffix}: status={(int)response.StatusCode}, expansions={fixture.Expansions.Count}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedExpansions, fixture.Expansions.Count);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(64, json.RootElement.GetProperty("readToken").GetString()!.Length);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.False(json.RootElement.TryGetProperty("ownerId", out _));
        Assert.False(json.RootElement.TryGetProperty("payloadHash", out _));
        if (suffix == "current")
        {
            Assert.Equal(3, json.RootElement.GetProperty("version").GetInt32());
            Assert.Equal("read-3", json.RootElement.GetProperty("deck").GetProperty("name").GetString());
            Assert.False(json.RootElement.TryGetProperty("versions", out _));
        }
        if (suffix == "versions")
        {
            Assert.Equal(3, json.RootElement.GetProperty("total").GetInt32());
            Assert.All(json.RootElement.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("deck", out _)));
        }
        fixture.Complete();
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("owner")]
    [InlineData("other")]
    public async Task CurrentAndHistoricalConstructProvenanceOnlyBelongsToFreshOwner(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start(2);
        var token = mode == "owner" ? fixture.Token : mode == "other" ? fixture.OtherToken : null;
        fixture.Observe();
        foreach (var suffix in new[] { "current", "versions/1" })
        {
            using var response = await fixture.Get(suffix, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var deck = json.RootElement.GetProperty("deck");
            Assert.Equal(mode == "owner", deck.GetProperty("publicationVersion").ValueKind != JsonValueKind.Null);
            Assert.Equal(mode == "owner", deck.GetProperty("publicationId").ValueKind != JsonValueKind.Null);
            Assert.False(json.RootElement.TryGetProperty("ownerId", out _));
            var edit = suffix == "current" ? json.RootElement.GetProperty("summary").GetProperty("canEdit") : json.RootElement.GetProperty("canEdit");
            Assert.Equal(mode == "owner", edit.GetBoolean());
        }
        Assert.Equal(2, fixture.Expansions.Count);
        fixture.Complete();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("revoke")]
    [InlineData("expire")]
    public async Task InvalidProvidedBearerNeverFallsBackToAnonymous(string mode)
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        if (mode == "revoke") fixture.Store.RevokeOwnSession(fixture.Viewer, fixture.Viewer.SessionId);
        if (mode == "expire") fixture.Expire();
        var token = mode == "invalid" ? "invalid-synthetic-token" : fixture.Token;
        Assert.Null(fixture.Store.AuthenticateSession("Bearer " + token));
        fixture.Observe();
        foreach (var suffix in new[] { "current", "versions", "versions/1" })
        {
            using var response = await fixture.Get(suffix, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task MissingDeletedInvalidQueryAndFeatureFailureNeverExpand()
    {
        await using var fixture = await PublicDeckReadFixture.Start();
        fixture.Observe();
        foreach (var suffix in new[] { "current?page=1", "current?expectedReadToken=x", "versions?page=0", "versions?pageSize=101",
            "versions?page=1&page=2", "versions?old=1", "versions/0", "versions/-1", "versions/2147483648", "versions/1?pageSize=1" })
        {
            using var response = await fixture.Get(suffix);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var response = await fixture.Client.GetAsync("/api/public-decks/missing/current")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await fixture.Get("versions/2")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        fixture.SetFeature(false);
        foreach (var suffix in new[] { "current", "versions", "versions/1" })
        {
            using var response = await fixture.Get(suffix);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        fixture.SetFeature(true);
        Assert.True(fixture.Store.DeletePublishedDeck(fixture.Owner.Id, fixture.Published.Id));
        foreach (var suffix in new[] { "current", "versions", "versions/1" })
        {
            using var response = await fixture.Get(suffix);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM published_deck_versions;"));
        Assert.Equal("1", fixture.Scalar("SELECT is_deleted FROM published_decks;"));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task CurrentGuideIsPinnedAndDoesNotLoadWholeHistory()
    {
        await using var fixture = await PublicDeckReadFixture.Start(25);
        fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
            new(new("bounded build", "opening", "key", "sequence", "substitution"), []));
        fixture.Observe();
        using var response = await fixture.Get("current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("bounded build", json.RootElement.GetProperty("guide").GetProperty("buildIdea").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("contentRevision").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("matchStatistics", out _));
        Assert.False(json.RootElement.TryGetProperty("versions", out _));
        Assert.Single(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task DeploymentDrainBlocksAllNewReadRoutes()
    {
        await using var fixture = await PublicDeckReadFixture.Start(withDrain: true);
        fixture.Drain!.BeginDrain(fixture.DrainOwner!);
        Assert.True(fixture.Drain.TryBeginSeal(fixture.DrainOwner!).Succeeded);
        fixture.Observe();
        foreach (var suffix in new[] { "current", "versions", "versions/1" })
        {
            using var response = await fixture.Get(suffix);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }
}

internal sealed class PublicDeckReadFixture : IAsyncDisposable
{
    internal const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly string root = Path.Combine(Path.GetTempPath(), "l12-cq3-read-" + Guid.NewGuid().ToString("N"));
    private readonly string? previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
    private bool passed;
    private L12WebSocketServer? server;
    private MatchRecorder? recorder;
    internal string PlatformPath => Path.Combine(root, "platform.json");
    internal L12Catalog Catalog { get; } = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
    internal L12PlatformStore Store { get; private set; } = null!;
    internal L12AccountView Owner { get; private set; } = null!;
    internal string Token { get; private set; } = "";
    internal string OtherToken { get; private set; } = "";
    internal L12AuthenticatedSession Viewer { get; private set; } = null!;
    internal L12PublishedDeckView Published { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal List<string> Expansions { get; } = [];
    internal L12DeploymentDrainCoordinator? Drain { get; private set; }
    internal L12DeploymentDrainOwner? DrainOwner { get; private set; }
    internal static async Task<PublicDeckReadFixture> Start(int versions = 1, bool withDrain = false)
    {
        var fixture = new PublicDeckReadFixture();
        try
        {
            Directory.CreateDirectory(fixture.root);
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            fixture.Store = new(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
            var owner = fixture.Store.Register("readowner", "password-123");
            var other = fixture.Store.Register("readother", "password-123");
            Assert.True(owner.Success, owner.Message); Assert.True(other.Success, other.Message);
            fixture.Owner = owner.Account!; fixture.Token = owner.Token!; fixture.OtherToken = other.Token!;
            fixture.Viewer = fixture.Store.AuthenticateSession("Bearer " + fixture.Token)!;
            for (var index = 1; index <= versions; index++) fixture.Publish(index);
            fixture.recorder = new MatchRecorder(Path.Combine(fixture.root, "matches.db"));
            await fixture.recorder.InitializeAsync();
            var rooms = new L12RoomManager(fixture.Catalog, fixture.recorder, fixture.Store);
            if (withDrain)
            {
                fixture.Drain = new("11111111111111111111111111111111", new string('a', 40), new FenceStore());
                rooms.AttachDeploymentDrain(fixture.Drain);
                Assert.True(L12DeploymentDrainOwner.TryCreate("22222222222222222222222222222222", new string('b', 40),
                    fixture.Drain.Snapshot().ProcessInstance, out var ownerLease));
                fixture.DrainOwner = ownerLease;
            }
            fixture.server = new L12WebSocketServer(rooms, fixture.recorder, fixture.Store, fixture.Catalog);
            await fixture.server.StartAsync(0);
            fixture.Client = new() { BaseAddress = new Uri(Assert.Single(fixture.server.Addresses)) };
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    internal void Publish(int index)
    {
        var source = Catalog.PresetDecks[0];
        Published = Store.PublishDeck(Owner.Id, new L12PresetDeckDefinition
        {
            Name = "read-" + index, MasterId = source.MasterId, CardIds = [.. source.CardIds],
            MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
        }, Published?.Id)!;
    }
    internal async Task<HttpResponseMessage> Get(string suffix, string? token = null, bool byId = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/public-decks/{(byId ? Published.Id : Published.PublicCode)}/{suffix}");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }
    internal object Data => typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(Store)!;
    internal void Expire()
    {
        var session = ((IEnumerable)Data.GetType().GetProperty("Sessions")!.GetValue(Data)!).Cast<object>()
            .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == Viewer.SessionId);
        session.GetType().GetProperty("ExpiresAt")!.SetValue(session, DateTimeOffset.UnixEpoch);
    }
    internal void SetFeature(bool enabled)
    {
        var admin = Store.Login("Admin", "L12master").Account!;
        var config = Store.OperationsConfig(admin);
        Store.ApplyOperationsConfig(admin, config.Config with
        {
            FeatureFlags = config.Config.FeatureFlags.ToDictionary(item => item.Key, item => item.Key == "publicDecks" ? enabled : item.Value),
        }, config.Version, "read synthetic feature", new L12AdminAuditContext("read-feature",
            Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite), Reason: "read synthetic", RequestMethod: "PUT", RequestPath: "/synthetic"));
    }
    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store.TransactionalStoragePath, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    internal string Scalar(string sql)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }
    internal void Sql(string sql)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    internal void InjectCounts(int quantity, bool allVersions)
    {
        var master = Published.Deck.MasterId;
        var decoded = new L12DecodedDeckPayload(JsonSerializer.Serialize(new[] { new { CardId = "HUGE", Quantity = quantity } }), "[]", "[]");
        var hash = L12DeckPayloadCodec.ComputeCanonicalHash(master, decoded);
        var code = L12DeckPayloadCodec.EncodeLegacyJson(decoded.MainJson, decoded.MoraleJson, decoded.SpecialJson);
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO deck_payloads(payload_hash,master_id,payload_format,payload_json,created_utc) VALUES($hash,$master,1,$json,$now);
            UPDATE published_deck_versions SET payload_hash=$hash WHERE publication_id=$id AND ($all=1 OR version=1);
            UPDATE published_decks SET current_payload_hash=$hash WHERE publication_id=$id AND $all=1;
            """;
        command.Parameters.AddWithValue("$hash", hash); command.Parameters.AddWithValue("$master", master);
        command.Parameters.AddWithValue("$json", code); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", Published.Id); command.Parameters.AddWithValue("$all", allVersions ? 1 : 0);
        command.ExecuteNonQuery(); transaction.Commit();
    }
    internal void Observe() => Store.DeckPayloadExpansionObserver = Expansions.Add;
    internal void Complete() => passed = true;
    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
        if (recorder is not null) await recorder.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
        if (passed) Directory.Delete(root, true);
        else Console.WriteLine("Failed synthetic CQ3 read fixture retained: " + root);
    }
    private sealed class FenceStore : IL12DeploymentDrainFenceStore
    {
        public L12DeploymentDrainFenceLoadResult Load() => L12DeploymentDrainFenceLoadResult.Missing;
        public bool TryWrite(L12DeploymentPersistedFence fence) => true;
        public bool TryClear(string operationId, string targetCommit) => true;
    }
}
