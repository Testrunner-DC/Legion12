using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckCounterResultHttpTests
{
    [Theory]
    [InlineData("view", false)]
    [InlineData("copy", false)]
    [InlineData("like", true)]
    public async Task NewCounterRouteReturnsOnlyScalarsAndNeverExpandsBodies(string kind, bool authenticated)
    {
        await using var fixture = await Fixture.Start();
        var before = fixture.Store.Version;
        fixture.Observe();
        using var request = fixture.Request(kind, authenticated ? fixture.Token : null);
        using var response = await fixture.Client.SendAsync(request);
        Console.WriteLine($"CQ3 counter {kind}: status={(int)response.StatusCode}, expansions={fixture.Expansions.Count}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(fixture.Published.Id, json.RootElement.GetProperty("id").GetString());
        Assert.Equal(fixture.Published.PublicCode, json.RootElement.GetProperty("publicCode").GetString());
        Assert.Equal(kind == "view" ? 1 : 0, json.RootElement.GetProperty("views").GetInt32());
        Assert.Equal(kind == "copy" ? 1 : 0, json.RootElement.GetProperty("copies").GetInt32());
        Assert.Equal(kind == "like" ? 1 : 0, json.RootElement.GetProperty("likes").GetInt32());
        Assert.Equal(kind == "like", json.RootElement.GetProperty("viewerLiked").GetBoolean());
        foreach (var forbidden in new[] { "deck", "cardIds", "moraleIds", "specialIds", "versions", "details",
            "ownerId", "payloadHash", "publicationVersion", "alternateArtCopies" }) Assert.False(json.RootElement.TryGetProperty(forbidden, out _));
        Assert.Equal(before, fixture.Store.Version);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task SuppliedInvalidSessionsCannotMutateAnyCounter(string mode)
    {
        await using var fixture = await Fixture.Start();
        if (mode == "revoked") Assert.Equal(1, fixture.Store.RevokeOwnSession(fixture.Viewer, fixture.Viewer.SessionId).RevokedCount);
        if (mode == "expired")
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Store)!;
            var session = ((IEnumerable)data.GetType().GetProperty("Sessions")!.GetValue(data)!).Cast<object>()
                .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == fixture.Viewer.SessionId);
            session.GetType().GetProperty("ExpiresAt")!.SetValue(session, DateTimeOffset.UnixEpoch);
        }
        var token = mode == "invalid" ? "invalid-synthetic-token" : fixture.Token;
        Assert.Null(fixture.Store.AuthenticateSession("Bearer " + token));
        fixture.Observe();
        foreach (var kind in new[] { "view", "copy", "like" })
        {
            using var request = fixture.Request(kind, token);
            using var response = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal("0|0|0", fixture.CountersSql());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task MissingDeletedBadKindAnonymousLikeAndExtraQueryFailWithoutMutation()
    {
        await using var fixture = await Fixture.Start();
        fixture.Observe();
        using (var response = await fixture.Client.PostAsync($"/api/public-decks/{fixture.Published.PublicCode}/counters/future", null))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await fixture.Client.PostAsync($"/api/public-decks/{fixture.Published.PublicCode}/counters/view?kind=copy", null))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await fixture.Client.PostAsync($"/api/public-decks/{fixture.Published.PublicCode}/counters/like", null))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await fixture.Client.PostAsync("/api/public-decks/missing/counters/view", null))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("0|0|0", fixture.CountersSql());
        Assert.True(fixture.Store.DeletePublishedDeck(fixture.Owner.Id, fixture.Published.Id));
        using (var response = await fixture.Client.PostAsync($"/api/public-decks/{fixture.Published.PublicCode}/counters/view", null))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task PublicFeatureAndDeploymentSealBlockScalarCounterRoute()
    {
        await using var fixture = await Fixture.Start(withDrain: true);
        var admin = fixture.Store.Login("Admin", "L12master").Account!;
        var config = fixture.Store.OperationsConfig(admin);
        fixture.Store.ApplyOperationsConfig(admin, config.Config with
        {
            FeatureFlags = config.Config.FeatureFlags.ToDictionary(item => item.Key, item => item.Key == "publicDecks" ? false : item.Value),
        }, config.Version, "counter synthetic feature", new L12AdminAuditContext("counter-feature",
            Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite), Reason: "counter synthetic", RequestMethod: "PUT", RequestPath: "/synthetic"));
        fixture.Observe();
        using (var request = fixture.Request("view", fixture.Token))
        using (var response = await fixture.Client.SendAsync(request)) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        config = fixture.Store.OperationsConfig(admin);
        fixture.Store.ApplyOperationsConfig(admin, config.Config with
        {
            FeatureFlags = config.Config.FeatureFlags.ToDictionary(item => item.Key, item => item.Key == "publicDecks" ? true : item.Value),
        }, config.Version, "counter synthetic enable", new L12AdminAuditContext("counter-enable",
            Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite), Reason: "counter synthetic", RequestMethod: "PUT", RequestPath: "/synthetic"));
        fixture.Drain!.BeginDrain(fixture.DrainOwner!);
        Assert.True(fixture.Drain.TryBeginSeal(fixture.DrainOwner!).Succeeded);
        using (var request = fixture.Request("copy", fixture.Token))
        using (var response = await fixture.Client.SendAsync(request)) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("0|0|0", fixture.CountersSql());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

#if !CQ3_COUNTER_BASELINE
    [Theory]
    [InlineData("revoke")]
    [InlineData("role")]
    [InlineData("disable")]
    public async Task CapturedViewerIsRevalidatedAtStoreBoundaryBeforeAnySqlWrite(string mode)
    {
        await using var fixture = await Fixture.Start();
        var captured = fixture.Viewer;
        var admin = fixture.Store.Login("Admin", "L12master").Account!;
        if (mode == "revoke") Assert.Equal(1, fixture.Store.RevokeOwnSession(captured, captured.SessionId).RevokedCount);
        if (mode == "role") Assert.True(fixture.Store.SetRole(admin, fixture.Owner.Id, "admin"));
        if (mode == "disable") fixture.Store.SetAccountDisabled(admin, fixture.Owner.Id, true, "counter synthetic",
            new L12AdminAuditContext("counter-disabled", Reason: "counter synthetic", RequestMethod: "POST", RequestPath: "/synthetic"), true);
        fixture.Observe();
        foreach (var kind in new[] { "view", "copy", "like" })
            Assert.Equal("unauthorized", fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, kind, captured).Status);
        Assert.Equal("0|0|0", fixture.CountersSql());
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("view")]
    [InlineData("copy")]
    [InlineData("like")]
    public async Task AbortedSqlTransactionNeverReturnsSuccessOrChangesMemoryCounters(string kind)
    {
        await using var fixture = await Fixture.Start();
        using (var connection = fixture.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = kind == "like"
                ? "CREATE TRIGGER counter_synthetic_abort BEFORE INSERT ON published_deck_likes BEGIN SELECT RAISE(ABORT,'counter-synthetic-failure'); END;"
                : "CREATE TRIGGER counter_synthetic_abort BEFORE UPDATE OF views,copies ON published_decks BEGIN SELECT RAISE(ABORT,'counter-synthetic-failure'); END;";
            command.ExecuteNonQuery();
        }
        var version = fixture.Store.Version;
        fixture.Observe();
        var error = Assert.Throws<SqliteException>(() => fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, kind, fixture.Viewer));
        Assert.Contains("counter-synthetic-failure", error.Message);
        Assert.Equal("0|0|0", fixture.CountersSql());
        Assert.Equal(version, fixture.Store.Version);
        // Summary reads the captured in-memory row, independently of SQL assertions.
        var summary = fixture.Store.DeckLibrarySummaries(fixture.Catalog, new(Source: "public"), fixture.Viewer).Page!;
        var item = Assert.Single(summary.Items);
        Assert.Equal(0, item.Views);
        Assert.Equal(0, item.Copies);
        Assert.Equal(0, item.Likes);
        Assert.False(item.ViewerLiked);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task AtomicScalarWritesSurviveRestartSaturateAndKeepSnapshotMirrorAndWalContract()
    {
        await using var fixture = await Fixture.Start();
        using (var connection = fixture.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        var snapshot = fixture.Scalar("SELECT storage_revision || '|' || snapshot_sha256 || '|' || updated_utc FROM platform_state WHERE singleton_id=1;");
        var mirror = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.PlatformPath)));
        var version = fixture.Store.Version;
        fixture.Observe();
        Parallel.For(0, 128, _ => Assert.Equal("ok", fixture.Store.UpdatePublicDeckCounter(fixture.Published.PublicCode, "view").Status));
        for (var index = 0; index < 7; index++) Assert.Equal("ok", fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, "copy").Status);
        var liked = fixture.Store.UpdatePublicDeckCounter(fixture.Published.PublicCode, "like", fixture.Viewer);
        Assert.True(liked.Counters!.ViewerLiked);
        Assert.Equal("128|7|1", fixture.CountersSql());
        Assert.Equal(snapshot, fixture.Scalar("SELECT storage_revision || '|' || snapshot_sha256 || '|' || updated_utc FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(mirror, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.PlatformPath))));
        Assert.Equal(version, fixture.Store.Version);
        Assert.InRange(File.Exists(fixture.Store.TransactionalStoragePath + "-wal") ? new FileInfo(fixture.Store.TransactionalStoragePath + "-wal").Length : 0, 0, 4 * 1024 * 1024);
        var restarted = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
        restarted.DeckPayloadExpansionObserver = fixture.Expansions.Add;
        var next = restarted.UpdatePublicDeckCounter(fixture.Published.PublicCode, "view");
        Assert.Equal(129, next.Counters!.Views);
        using (var connection = fixture.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE published_decks SET views=2147483647,copies=2147483647 WHERE publication_id=$id;";
            command.Parameters.AddWithValue("$id", fixture.Published.Id);
            command.ExecuteNonQuery();
        }
        var saturated = new L12PlatformStore(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
        saturated.DeckPayloadExpansionObserver = fixture.Expansions.Add;
        Assert.Equal(int.MaxValue, saturated.UpdatePublicDeckCounter(fixture.Published.Id, "view").Counters!.Views);
        Assert.Equal(int.MaxValue, saturated.UpdatePublicDeckCounter(fixture.Published.Id, "copy").Counters!.Copies);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }
#endif

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "l12-counter-" + Guid.NewGuid().ToString("N"));
        private readonly string? previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        private bool passed;
        private L12WebSocketServer? server;
        private MatchRecorder? recorder;
        public L12Catalog Catalog { get; } = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        public string PlatformPath => Path.Combine(root, "platform.json");
        public L12PlatformStore Store { get; private set; } = null!;
        public L12AccountView Owner { get; private set; } = null!;
        public string Token { get; private set; } = "";
        public L12AuthenticatedSession Viewer { get; private set; } = null!;
        public L12PublishedDeckView Published { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public List<string> Expansions { get; } = [];
        public L12DeploymentDrainCoordinator? Drain { get; private set; }
        public L12DeploymentDrainOwner? DrainOwner { get; private set; }
        public static async Task<Fixture> Start(bool withDrain = false)
        {
            var fixture = new Fixture();
            try
            {
                Directory.CreateDirectory(fixture.root);
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
                fixture.Store = new(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
                var login = fixture.Store.Register("cq3owner", "password-123");
                Assert.True(login.Success, login.Message);
                fixture.Owner = login.Account!;
                fixture.Token = login.Token!;
                fixture.Viewer = fixture.Store.AuthenticateSession("Bearer " + login.Token)!;
                var source = fixture.Catalog.PresetDecks[0];
                fixture.Published = fixture.Store.PublishDeck(fixture.Owner.Id, new L12PresetDeckDefinition
                {
                    Name = "counter deck", MasterId = source.MasterId, CardIds = [.. source.CardIds],
                    MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
                }, null)!;
                fixture.recorder = new MatchRecorder(Path.Combine(fixture.root, "matches.db"));
                await fixture.recorder.InitializeAsync();
                var rooms = new L12RoomManager(fixture.Catalog, fixture.recorder, fixture.Store);
                if (withDrain)
                {
                    fixture.Drain = new("11111111111111111111111111111111", new string('a', 40), new FenceStore());
                    rooms.AttachDeploymentDrain(fixture.Drain);
                    Assert.True(L12DeploymentDrainOwner.TryCreate("22222222222222222222222222222222", new string('b', 40),
                        fixture.Drain.Snapshot().ProcessInstance, out var owner));
                    fixture.DrainOwner = owner;
                }
                fixture.server = new L12WebSocketServer(rooms, fixture.recorder, fixture.Store, fixture.Catalog);
                await fixture.server.StartAsync(0);
                fixture.Client = new HttpClient { BaseAddress = new Uri(Assert.Single(fixture.server.Addresses)) };
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public HttpRequestMessage Request(string kind, string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public-decks/{Published.PublicCode}/counters/{kind}");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }
        public SqliteConnection Open()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store.TransactionalStoragePath, Pooling = false }.ToString());
            connection.Open(); return connection;
        }
        public string Scalar(string sql)
        {
            using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        public string CountersSql() => Scalar("SELECT views || '|' || copies || '|' || (SELECT COUNT(*) FROM published_deck_likes WHERE publication_id=published_decks.publication_id) FROM published_decks WHERE is_deleted=0;");
        public void Observe() => Store.DeckPayloadExpansionObserver = Expansions.Add;
        public void Complete() => passed = true;
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ3 counter fixture retained: " + root);
        }
    }
    private sealed class FenceStore : IL12DeploymentDrainFenceStore
    {
        public L12DeploymentDrainFenceLoadResult Load() => L12DeploymentDrainFenceLoadResult.Missing;
        public bool TryWrite(L12DeploymentPersistedFence fence) => true;
        public bool TryClear(string operationId, string targetCommit) => true;
    }
}
