using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckStatisticsReadHttpTests
{
    [Fact]
    public async Task DirectoryPinReadsBoundedAnonymousStatisticsWithoutDeckExpansion()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, fixture.Published.Deck.MasterId, fixture.OpponentMasterId, 3, "http-basic");
        fixture.Observe();
        var pin = await fixture.SummaryPin();
        var response = await fixture.Statistics("page=1&pageSize=30&expectedReadToken=" + pin.ReadToken);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal(fixture.Published.Id, root.GetProperty("id").GetString());
        Assert.Equal(fixture.Published.PublicCode, root.GetProperty("publicCode").GetString());
        Assert.Equal(pin.ReadToken, root.GetProperty("readToken").GetString());
        Assert.Equal(pin.CatalogVersion, root.GetProperty("catalogVersion").GetString());
        Assert.Equal(pin.PolicyVersion, root.GetProperty("policyVersion").GetInt64());
        Assert.Equal(90, root.GetProperty("recentDays").GetInt32());
        Assert.Equal(3, root.GetProperty("games").GetInt32());
        Assert.Equal("available", root.GetProperty("sampleStatus").GetString());
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(30, root.GetProperty("pageSize").GetInt32());
        var group = Assert.Single(root.GetProperty("groups").EnumerateArray());
        Assert.Equal((1, 3, 3, 0, 0, 1d), (group.GetProperty("version").GetInt32(),
            group.GetProperty("games").GetInt32(), group.GetProperty("wins").GetInt32(),
            group.GetProperty("losses").GetInt32(), group.GetProperty("draws").GetInt32(),
            group.GetProperty("winRate").GetDouble()));
        Assert.True(root.GetProperty("from").GetDateTimeOffset() < root.GetProperty("to").GetDateTimeOffset());
        AssertAnonymous(root);
        Assert.DoesNotContain(fixture.Owner.Id, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Other.Id, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("http-basic", response.Body, StringComparison.Ordinal);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task AnonymousDirectoryPinReadsStatisticsButOwnerPinCannotCrossViewerContext()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, fixture.Published.Deck.MasterId, fixture.OpponentMasterId, 3, "anonymous");
        fixture.Observe();
        var anonymousPin = await fixture.SummaryPin(authenticated: false);
        var anonymous = await fixture.Statistics("expectedReadToken=" + anonymousPin.ReadToken,
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, anonymous.Status);
        using (var json = JsonDocument.Parse(anonymous.Body))
        {
            Assert.Equal(anonymousPin.ReadToken, json.RootElement.GetProperty("readToken").GetString());
            AssertAnonymous(json.RootElement);
        }
        var ownerPin = await fixture.SummaryPin();
        Assert.Equal(HttpStatusCode.Conflict,
            (await fixture.Statistics("expectedReadToken=" + ownerPin.ReadToken, authenticated: false)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fixture.Statistics(authenticated: true, bearer: "invalid-token")).Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task AuthenticationQueryMissingAndFeatureStatesAreExplicit()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        Assert.Equal(HttpStatusCode.OK, (await fixture.Statistics(authenticated: false)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Statistics(bearer: "invalid-token")).Status);
        foreach (var query in new[] { "unknown=1", "page=0", "pageSize=101", "page=1&page=2",
                     "expectedReadToken=short" })
            Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Statistics(query)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.Statistics(reference: Guid.NewGuid().ToString("N"))).Status);
        fixture.SetFeature(false);
        var disabled = await fixture.Statistics();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.Status);
        using var json = JsonDocument.Parse(disabled.Body);
        Assert.Equal("feature_disabled", json.RootElement.GetProperty("code").GetString());
        fixture.Complete();
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("expire")]
    [InlineData("permission")]
    public async Task RevokedExpiredAndPermissionStaleSessionsReturnUnauthorized(string mode)
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        var pin = await fixture.SummaryPin();
        switch (mode)
        {
            case "revoke":
                Assert.Equal(1, fixture.Store.RevokeOwnSession(fixture.Viewer, fixture.Viewer.SessionId).RevokedCount);
                break;
            case "expire": fixture.Expire(); break;
            case "permission": Assert.True(fixture.Store.SetRole(fixture.Admin, fixture.Owner.Id, "admin")); break;
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fixture.Statistics("expectedReadToken=" + pin.ReadToken)).Status);
        fixture.Complete();
    }

    [Fact]
    public async Task StaleBodyPinConflictsButCounterChangesDoNotInvalidateIt()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, fixture.Published.Deck.MasterId, fixture.OpponentMasterId, 3, "pin-counter");
        var pin = await fixture.SummaryPin();
        Assert.Equal("ok", fixture.Store.UpdatePublicDeckCounter(fixture.Published.Id, "view").Status);
        Assert.Equal(HttpStatusCode.OK,
            (await fixture.Statistics("expectedReadToken=" + pin.ReadToken)).Status);
        Assert.NotNull(fixture.Store.UpdatePublicDeckContent(fixture.Owner.Id, fixture.Published.Id,
            new(new("changed", "", "", "", ""), [])));
        var stale = await fixture.Statistics("expectedReadToken=" + pin.ReadToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        using var json = JsonDocument.Parse(stale.Body);
        Assert.Equal("public_deck_read_conflict", json.RootElement.GetProperty("code").GetString());
        fixture.Complete();
    }

    [Theory]
    [InlineData("platform-marker")]
    [InlineData("recorder-marker")]
    [InlineData("stored-date")]
    [InlineData("broken-binding")]
    [InlineData("illegal-binding")]
    public async Task DamagedStorageReturnsPlainUnavailableWithoutLeakingFacts(string mode)
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, fixture.Published.Deck.MasterId, fixture.OpponentMasterId, 3, "damage");
        fixture.Observe();
        if (mode == "platform-marker")
            fixture.PlatformSql("UPDATE storage_meta SET value='unknown' WHERE key='deck_payload_format_state';");
        else if (mode == "recorder-marker")
            fixture.MatchSql("UPDATE match_recorder_schema SET version=999 WHERE component='match-analytics';");
        else if (mode == "stored-date")
            fixture.MatchSql("UPDATE matches SET ended_utc='2026-10-06T07:private-malformed-date+00:00' WHERE match_id='damage-0';");
        else if (mode == "broken-binding")
            fixture.MatchSql("PRAGMA foreign_keys=OFF; DELETE FROM matches WHERE match_id='damage-0';");
        else
            fixture.MatchSql("PRAGMA ignore_check_constraints=ON; UPDATE match_public_deck_bindings SET version=0 WHERE match_id='damage-0';");
        var response = await fixture.Statistics();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("牌库统计暂时无法读取，请稍后重试", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain(fixture.Published.Id, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-malformed-date", response.Body, StringComparison.Ordinal);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("guide", 409)]
    [InlineData("version", 409)]
    [InlineData("policy", 409)]
    [InlineData("catalog", 409)]
    [InlineData("owner-name", 409)]
    [InlineData("delete", 409)]
    [InlineData("exclusions", 409)]
    [InlineData("revoke", 401)]
    [InlineData("expire", 401)]
    [InlineData("permission", 401)]
    [InlineData("account-aba", 401)]
    [InlineData("feature", 503)]
    public async Task ProcessingContextChangesAreRejectedAfterRecorderAwait(string mode, int expectedStatus)
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, fixture.Published.Deck.MasterId, fixture.OpponentMasterId, 3, "await-change");
        fixture.Observe();
        var pin = await fixture.SummaryPin();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Recorder.PublicDeckStatisticsReadPauseHook = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var pending = fixture.Statistics("expectedReadToken=" + pin.ReadToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (var healthRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
        {
            healthRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
            var healthTask = fixture.Client.SendAsync(healthRequest);
            Assert.Same(healthTask, await Task.WhenAny(healthTask, Task.Delay(TimeSpan.FromSeconds(2))));
            using var health = await healthTask;
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }
        try
        {
            fixture.ChangeProcessingContext(mode);
            // Product writes may legitimately expand their submitted deck. The
            // assertion below covers only the resumed metadata/statistics read.
            fixture.Expansions.Clear();
        }
        finally { release.TrySetResult(); }
        var response = await pending;
        Assert.Equal((HttpStatusCode)expectedStatus, response.Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public async Task RequestCancellationNeverReturnsAPartialSuccess()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, fixture.Published.Deck.MasterId, fixture.OpponentMasterId, 3, "cancel");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Recorder.PublicDeckStatisticsReadPauseHook = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var cancellation = new CancellationTokenSource();
        using var request = fixture.StatisticsRequest();
        var pending = fixture.Client.SendAsync(request, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        fixture.Complete();
    }

    [Fact]
    public async Task BusyRecorderNeverReturnsAnAuthoritativeEmptySuccess()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        SqliteConnection.ClearAllPools();
        using var blocker = fixture.OpenMatches();
        using (var journal = blocker.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode=DELETE;";
            Assert.Equal("delete", Convert.ToString(journal.ExecuteScalar()));
        }
        using (var command = blocker.CreateCommand())
        {
            command.CommandText = "PRAGMA locking_mode=EXCLUSIVE;";
            Assert.Equal("exclusive", Convert.ToString(command.ExecuteScalar()));
            command.CommandText = "BEGIN EXCLUSIVE;";
            command.ExecuteNonQuery();
        }
        (HttpStatusCode Status, string Body, string? CacheControl) response;
        try { response = await fixture.Statistics(); }
        finally
        {
            using var rollback = blocker.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("no-store", response.CacheControl);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("storage_unavailable", json.RootElement.GetProperty("code").GetString());
        fixture.Complete();
    }

    private static void AssertAnonymous(JsonElement root)
    {
        var names = new List<string>();
        Collect(root);
        foreach (var forbidden in new[] { "matchId", "accountId", "username", "displayName", "ownerId",
                     "author", "excludedMatchIds", "excludedAccountIds", "deck", "guide", "matchups" })
            Assert.DoesNotContain(names, name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));
        void Collect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject()) { names.Add(property.Name); Collect(property.Value); }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Collect(item);
        }
    }
}

internal sealed class PublicDeckStatisticsFixture : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "l12-public-stats-" + Guid.NewGuid().ToString("N"));
    private readonly string? previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
    private int matchSequence;
    private bool passed;
    private L12WebSocketServer? server;
    internal DateTimeOffset Now { get; set; } = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    internal string PlatformPath => Path.Combine(root, "platform.json");
    internal string MatchesPath => Path.Combine(root, "matches.db");
    internal L12Catalog Catalog { get; } = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
    internal L12PlatformStore Store { get; private set; } = null!;
    internal MatchRecorder Recorder { get; private set; } = null!;
    internal L12AccountView Owner { get; private set; } = null!;
    internal L12AccountView Other { get; private set; } = null!;
    internal L12AccountView Admin => Store.Login("Admin", "L12master").Account!;
    internal L12AuthenticatedSession Viewer { get; private set; } = null!;
    internal string Token { get; private set; } = "";
    internal L12PublishedDeckView Published { get; private set; } = null!;
    internal string OpponentMasterId => Catalog.Cards.Values.First(card => card.CardType == "master"
        && card.Id != Published.Deck.MasterId).Id;
    internal HttpClient Client { get; private set; } = null!;
    internal List<string> Expansions { get; } = [];

    internal static async Task<PublicDeckStatisticsFixture> Start()
    {
        var fixture = new PublicDeckStatisticsFixture();
        try
        {
            Directory.CreateDirectory(fixture.root);
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            fixture.Store = new(fixture.PlatformPath, officialCards: fixture.Catalog.Cards);
            var owner = fixture.Store.Register("statsowner", "password-123");
            var other = fixture.Store.Register("statsother", "password-123");
            Assert.True(owner.Success, owner.Message); Assert.True(other.Success, other.Message);
            fixture.Owner = owner.Account!; fixture.Other = other.Account!; fixture.Token = owner.Token!;
            fixture.Viewer = fixture.Store.AuthenticateSession("Bearer " + fixture.Token)!;
            var deck = fixture.Catalog.PresetDecks[0];
            fixture.Published = fixture.Store.PublishDeck(fixture.Owner.Id, new()
            {
                Name = "statistics-public", MasterId = deck.MasterId, CardIds = [.. deck.CardIds],
                MoraleIds = [.. deck.MoraleIds], SpecialIds = [.. deck.SpecialIds],
            }, null)!;
            fixture.Recorder = new(fixture.MatchesPath, () => fixture.Now);
            await fixture.Recorder.InitializeAsync();
            fixture.server = new(new(fixture.Catalog, fixture.Recorder, fixture.Store), fixture.Recorder,
                fixture.Store, fixture.Catalog);
            await fixture.server.StartAsync(0);
            fixture.Client = new() { BaseAddress = new Uri(Assert.Single(fixture.server.Addresses)) };
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    internal void SeedGroup(int version, string masterId, string opponentMasterId, int count, string prefix,
        DateTimeOffset? endedAt = null, string mode = "friendly", string? error = null,
        string? account0 = null, string? account1 = null, int winner = 0)
    {
        for (var index = 0; index < count; index++)
            SeedMatch(version, masterId, opponentMasterId, prefix + "-" + index,
                endedAt ?? Now.AddMinutes(-index), mode, error, account0, account1, winner);
    }

    internal void SeedMatch(int version, string masterId, string opponentMasterId, string matchId,
        DateTimeOffset? endedAt, string mode = "friendly", string? error = null,
        string? account0 = null, string? account1 = null, int winner = 0)
    {
        using var connection = OpenMatches();
        using var transaction = connection.BeginTransaction();
        using var match = connection.CreateCommand();
        match.Transaction = transaction;
        match.CommandText = """
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,
                ended_utc,winner,final_hash,error,mode_id,account_0,account_1)
            VALUES($id,$room,$seed,'甲','乙','公开牌库','对手牌库',$started,$ended,$winner,'hash',$error,$mode,$a0,$a1);
            """;
        match.Parameters.AddWithValue("$id", matchId);
        match.Parameters.AddWithValue("$room", "ST" + Interlocked.Increment(ref matchSequence));
        match.Parameters.AddWithValue("$seed", matchSequence);
        match.Parameters.AddWithValue("$started", (endedAt ?? Now).AddMinutes(-10).ToString("O"));
        match.Parameters.AddWithValue("$ended", endedAt is null ? DBNull.Value : endedAt.Value.ToString("O"));
        match.Parameters.AddWithValue("$winner", winner);
        match.Parameters.AddWithValue("$error", error is null ? DBNull.Value : error);
        match.Parameters.AddWithValue("$mode", mode);
        match.Parameters.AddWithValue("$a0", account0 ?? Owner.Id);
        match.Parameters.AddWithValue("$a1", account1 ?? Other.Id);
        Assert.Equal(1, match.ExecuteNonQuery());
        for (var player = 0; player < 2; player++)
        {
            using var participant = connection.CreateCommand();
            participant.Transaction = transaction;
            participant.CommandText = """
                INSERT INTO match_participants(match_id,player_index,account_id,display_name,master_id,master_name,deck_name)
                VALUES($id,$player,$account,$name,$master,$master,'deck');
                """;
            participant.Parameters.AddWithValue("$id", matchId);
            participant.Parameters.AddWithValue("$player", player);
            participant.Parameters.AddWithValue("$account", player == 0 ? account0 ?? Owner.Id : account1 ?? Other.Id);
            participant.Parameters.AddWithValue("$name", player == 0 ? "甲" : "乙");
            participant.Parameters.AddWithValue("$master", player == 0 ? masterId : opponentMasterId);
            Assert.Equal(1, participant.ExecuteNonQuery());
        }
        using var binding = connection.CreateCommand();
        binding.Transaction = transaction;
        binding.CommandText = """
            INSERT INTO match_public_deck_bindings(match_id,player_index,publication_id,version,payload_hash)
            VALUES($id,0,$publication,$version,$hash);
            """;
        binding.Parameters.AddWithValue("$id", matchId);
        binding.Parameters.AddWithValue("$publication", Published.Id);
        binding.Parameters.AddWithValue("$version", version);
        binding.Parameters.AddWithValue("$hash", new string('a', 64));
        Assert.Equal(1, binding.ExecuteNonQuery());
        transaction.Commit();
    }

    internal async Task<(string ReadToken, string CatalogVersion, long PolicyVersion)> SummaryPin(
        bool authenticated = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/deck-library/summaries?source=public&pageSize=100");
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(value => value.GetProperty("id").GetString() == Published.Id);
        return (item.GetProperty("readToken").GetString()!,
            json.RootElement.GetProperty("catalogVersion").GetString()!,
            json.RootElement.GetProperty("policyVersion").GetInt64());
    }

    internal HttpRequestMessage StatisticsRequest(string query = "", string? reference = null,
        bool authenticated = true, string? bearer = null)
    {
        var suffix = query.Length == 0 ? "" : "?" + query;
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/public-decks/{reference ?? Published.Id}/statistics{suffix}");
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer ?? Token);
        return request;
    }

    internal async Task<(HttpStatusCode Status, string Body, string? CacheControl)> Statistics(string query = "",
        string? reference = null, bool authenticated = true, string? bearer = null)
    {
        using var request = StatisticsRequest(query, reference, authenticated, bearer);
        using var response = await Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Headers.CacheControl?.ToString());
    }

    internal void ChangeProcessingContext(string mode)
    {
        switch (mode)
        {
            case "guide":
                Assert.NotNull(Store.UpdatePublicDeckContent(Owner.Id, Published.Id,
                    new(new("changed", "", "", "", ""), [])));
                break;
            case "version":
                Published = Store.PublishDeck(Owner.Id, Catalog.PresetDecks[1], Published.Id)!;
                break;
            case "policy": ApplyPolicyChange(); break;
            case "catalog":
                var source = Catalog.PresetDecks[0];
                Assert.IsType<List<L12PresetDeckDefinition>>(Catalog.PresetDecks).Add(new()
                {
                    Name = "statistics-catalog-change", MasterId = source.MasterId,
                    CardIds = [.. source.CardIds], MoraleIds = [.. source.MoraleIds],
                    SpecialIds = [.. source.SpecialIds],
                });
                break;
            case "owner-name":
                Assert.True(Store.SelfServiceChangeUsername(Owner.Id, "password-123", "statsrename", Viewer.SessionId).Success);
                break;
            case "delete": Assert.True(Store.DeletePublishedDeck(Owner.Id, Published.Id)); break;
            case "exclusions":
                Assert.True(Store.SetAccountDisabled(Admin, Other.Id, true, "statistics exclusion",
                    new("stats-exclusion"), true).Applied);
                break;
            case "revoke": Assert.Equal(1, Store.RevokeOwnSession(Viewer, Viewer.SessionId).RevokedCount); break;
            case "expire": Expire(); break;
            case "permission": Assert.True(Store.SetRole(Admin, Owner.Id, "admin")); break;
            case "account-aba":
                Assert.True(Store.SetAccountDisabled(Admin, Owner.Id, true, "statistics aba", new("stats-aba-1"), true).Applied);
                Assert.True(Store.SetAccountDisabled(Admin, Owner.Id, false, "statistics aba", new("stats-aba-2"), true).Applied);
                break;
            case "feature": SetFeature(false); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    internal void ApplyPolicyChange()
    {
        var current = Store.OperationsConfig(Admin);
        var card = Catalog.PresetDecks[0].CardIds[0];
        Store.ApplyOperationsConfig(Admin, current.Config with
        {
            CardRestrictions = [new(card, 0, "statistics synthetic")],
        }, current.Version, "statistics policy", new("stats-policy"));
    }

    internal void SetFeature(bool enabled)
    {
        var current = Store.OperationsConfig(Admin);
        Store.ApplyOperationsConfig(Admin, current.Config with
        {
            FeatureFlags = current.Config.FeatureFlags.ToDictionary(item => item.Key,
                item => item.Key == "publicDecks" ? enabled : item.Value),
        }, current.Version, "statistics feature", new("stats-feature"));
    }

    internal void Expire()
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Store)!;
        var session = ((IEnumerable)data.GetType().GetProperty("Sessions")!.GetValue(data)!).Cast<object>()
            .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == Viewer.SessionId);
        session.GetType().GetProperty("ExpiresAt")!.SetValue(session, DateTimeOffset.UnixEpoch);
    }

    internal void Observe() => Store.DeckPayloadExpansionObserver = Expansions.Add;
    internal void MatchSql(string sql) { using var connection = OpenMatches(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    internal void PlatformSql(string sql) { using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store.TransactionalStoragePath, Pooling = false }.ToString()); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    internal SqliteConnection OpenMatches() { var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = MatchesPath, Pooling = false }.ToString()); connection.Open(); return connection; }
    internal void Complete() => passed = true;

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
        if (Recorder is not null) await Recorder.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
        if (passed) Directory.Delete(root, true);
        else Console.WriteLine("Failed synthetic public statistics fixture retained: " + root);
    }
}
