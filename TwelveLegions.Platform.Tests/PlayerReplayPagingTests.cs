using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlayerReplayPagingTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private const string PrivateDeck0 = "SYNTHETIC_PRIVATE_DECK_ALPHA";
    private const string PrivateDeck1 = "SYNTHETIC_PRIVATE_DECK_BETA";

    // HTTP/JsonDocument deliberately keep this RED compilable before the candidate API/DTO exists.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public async Task OwnedPagesMatchExistingPrivateProjectionWithoutChangingArchive(int format, int viewer)
    {
        await using var fixture = await Fixture.CreateAsync(format);
        var before = await fixture.ArchiveDigestAsync();
        var legacy = await fixture.LegacyAsync(viewer);
        Assert.Equal(HttpStatusCode.OK, legacy.Status);
        using var original = JsonDocument.Parse(legacy.Body);
        var expected = original.RootElement.GetProperty("commands").EnumerateArray().Select(row => row.GetRawText()).ToArray();
        var actual = new List<string>();
        string? cursor = null;
        var reads = 0;
        do
        {
            var response = await fixture.PageAsync(viewer, cursor);
            Assert.Equal(HttpStatusCode.OK, response.Status);
            using var page = JsonDocument.Parse(response.Body);
            AssertPageSurface(page.RootElement, fixture.MatchId, viewer);
            Assert.Equal(original.RootElement.GetProperty("match").GetRawText(), page.RootElement.GetProperty("match").GetRawText());
            Assert.Equal(expected.Length, page.RootElement.GetProperty("totalCommands").GetInt64());
            Assert.Equal(2, page.RootElement.GetProperty("limit").GetInt32());
            var items = page.RootElement.GetProperty("items");
            Assert.InRange(items.GetArrayLength(), 1, 2);
            actual.AddRange(items.EnumerateArray().Select(row => row.GetRawText()));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            Assert.InRange(++reads, 1, expected.Length);
        } while (cursor is not null);
        Assert.Equal(expected, actual);
        Assert.Equal(before, await fixture.ArchiveDigestAsync());
        Assert.Equal(legacy.Body, (await fixture.LegacyAsync(viewer)).Body);
        fixture.Succeeded = true;
    }

    [Theory]
    [InlineData("outsider")]
    [InlineData("same-name-outsider")]
    [InlineData("ongoing")]
    [InlineData("older-than-window")]
    [InlineData("payload-expired")]
    [InlineData("outside-recent-ten")]
    [InlineData("anonymous")]
    public async Task PageScopeKeepsExistingOwnershipAndRetentionDenials(string scenario)
    {
        await using var fixture = await Fixture.CreateAsync(2, completed: scenario != "ongoing");
        if (scenario == "same-name-outsider")
            await fixture.SqlAsync("UPDATE matches SET player_0=$value WHERE match_id=$match;", fixture.OutsiderName);
        if (scenario == "older-than-window")
            await fixture.SqlAsync("UPDATE matches SET ended_utc=$value WHERE match_id=$match;", fixture.Now.AddDays(-8).ToString("O"));
        if (scenario == "payload-expired")
            await fixture.SqlAsync("""
                INSERT INTO player_replay_payload_expirations(match_id,expired_utc,retained_command_count,cleared_payload_bytes)
                VALUES($match,$value,8,0);
                """, fixture.Now.ToString("O"));
        if (scenario == "outside-recent-ten")
            await fixture.SeedNewerSummariesAsync();
        var token = scenario is "outsider" or "same-name-outsider" ? fixture.OutsiderToken : fixture.Tokens[0];
        var before = await fixture.ArchiveDigestAsync();
        var response = await fixture.PageAsync(token: token, anonymous: scenario == "anonymous");
        Assert.Equal(scenario == "anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound, response.Status);
        Assert.DoesNotContain(PrivateDeck0, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateDeck1, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"items\"", response.Body, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.ArchiveDigestAsync());
        fixture.Succeeded = true;
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1_000, 100)]
    public async Task PageCommandLimitIsClampedWithoutChangingTotal(int requested, int expected)
    {
        await using var fixture = await Fixture.CreateAsync(2, count: 112);
        var response = await fixture.PageAsync(limit: requested);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        using var page = JsonDocument.Parse(response.Body);
        AssertPageSurface(page.RootElement, fixture.MatchId, 0);
        Assert.Equal(expected, page.RootElement.GetProperty("limit").GetInt32());
        Assert.InRange(page.RootElement.GetProperty("items").GetArrayLength(), 1, expected);
        Assert.Equal(112, page.RootElement.GetProperty("totalCommands").GetInt64());
        fixture.Succeeded = true;
    }

    [Fact]
    public async Task CursorCannotBeReusedForAnotherOwnedMatch()
    {
        await using var fixture = await Fixture.CreateAsync(2);
        var first = await fixture.PageAsync();
        Assert.Equal(HttpStatusCode.OK, first.Status);
        using var page = JsonDocument.Parse(first.Body);
        var cursor = Assert.IsType<string>(page.RootElement.GetProperty("nextCursor").GetString());
        var otherId = (await fixture.RecordMatchAsync(2, 4, true)).State.MatchId;
        var response = await fixture.PageAsync(cursor: cursor, matchId: otherId);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.DoesNotContain("\"items\"", response.Body, StringComparison.Ordinal);
        fixture.Succeeded = true;
    }

    [Fact]
    public async Task FirstPageOf402JournalRowsDoesNotReconstructDamagedLaterPage()
    {
        await using var fixture = await Fixture.CreateAsync(2, count: 402);
        Assert.True(await fixture.ScalarAsync("SELECT COUNT(*) FROM match_state_checkpoints WHERE match_id=$match;") >= 14);
        var healthy = await fixture.LegacyAsync(0);
        Assert.Equal(HttpStatusCode.OK, healthy.Status);
        using (var original = JsonDocument.Parse(healthy.Body))
            Assert.Equal(402, original.RootElement.GetProperty("commands").GetArrayLength());
        await fixture.SqlAsync("UPDATE match_events SET state_hash=$value WHERE match_id=$match AND sequence=400;",
            "SYNTHETIC_CORRUPT_TAIL_HASH_DO_NOT_EXPOSE");
        var legacy = await fixture.LegacyAsync(0);
        Assert.Equal(HttpStatusCode.Conflict, legacy.Status); // Confirms the real full reader reaches the damaged tail.
        var before = await fixture.ArchiveDigestAsync();
        var first = await fixture.PageAsync(limit: 2);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        using var firstPage = JsonDocument.Parse(first.Body);
        AssertPageSurface(firstPage.RootElement, fixture.MatchId, 0);
        Assert.Equal(new long[] { 1, 2 }, firstPage.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt64()));
        var cursor = firstPage.RootElement.GetProperty("nextCursor").GetString();
        var reachedDamagedPage = false;
        for (var read = 0; read < 402 && cursor is not null; read++)
        {
            var response = await fixture.PageAsync(cursor: cursor, limit: 100);
            if (response.Status == HttpStatusCode.Conflict)
            {
                using var failure = JsonDocument.Parse(response.Body);
                Assert.Equal("replay_incompatible", failure.RootElement.GetProperty("code").GetString());
                Assert.DoesNotContain("SYNTHETIC_CORRUPT_TAIL_HASH_DO_NOT_EXPOSE", response.Body, StringComparison.Ordinal);
                Assert.DoesNotContain("\"items\"", response.Body, StringComparison.Ordinal);
                reachedDamagedPage = true;
                break;
            }
            Assert.Equal(HttpStatusCode.OK, response.Status);
            using var page = JsonDocument.Parse(response.Body);
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
        }
        Assert.True(reachedDamagedPage);
        Assert.Equal(before, await fixture.ArchiveDigestAsync());
        fixture.Succeeded = true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task HiddenRawVolumeAcrossFourMiBDoesNotChangePublicPrefixOrCursor(int format)
    {
        const string sharedMatchId = "11111111111111111111111111111111";
        await using var small = await Fixture.CreateAsync(format, count: 3, matchIdOverride: sharedMatchId);
        await using var large = await Fixture.CreateAsync(format, count: 3,
            hiddenHandNameChars: 2_200_000, matchIdOverride: sharedMatchId);
        var smallRaw = Assert.IsType<L12MatchDetail>(await small.Recorder.GetMatchAsync(small.MatchId));
        var largeRaw = Assert.IsType<L12MatchDetail>(await large.Recorder.GetMatchAsync(large.MatchId));
        var smallSizes = smallRaw.Commands.Select(item => Encoding.UTF8.GetByteCount(item.State.GetRawText())).ToArray();
        var largeSizes = largeRaw.Commands.Select(item => Encoding.UTF8.GetByteCount(item.State.GetRawText())).ToArray();
        Assert.True(smallSizes.Sum(size => (long)size) < MatchRecorder.MaximumReplayPageBytes);
        Assert.True(largeSizes.Take(2).Sum(size => (long)size) > MatchRecorder.MaximumReplayPageBytes);
        Assert.All(largeSizes, size => Assert.InRange(size, 1, checked((int)MatchRecorder.MaximumReplayPageBytes)));
        // GetMatchAsync verifies genuine V2 checkpoints/hashes; no post-record hash repair is used.
        var smallLegacy = await small.LegacyAsync(0);
        var largeLegacy = await large.LegacyAsync(0);
        Assert.Equal(HttpStatusCode.OK, smallLegacy.Status);
        Assert.Equal(HttpStatusCode.OK, largeLegacy.Status);
        Assert.Equal(NormalizedCommitments(smallLegacy.Body), NormalizedCommitments(largeLegacy.Body));
        var smallBefore = await small.ArchiveDigestAsync();
        var largeBefore = await large.ArchiveDigestAsync();
        foreach (var limit in new[] { 2, 100 })
        {
            var smallResponse = await small.PageAsync(limit: limit);
            var largeResponse = await large.PageAsync(limit: limit);
            Assert.Equal(HttpStatusCode.OK, smallResponse.Status);
            Assert.Equal(HttpStatusCode.OK, largeResponse.Status);
            using var smallPage = JsonDocument.Parse(smallResponse.Body);
            using var largePage = JsonDocument.Parse(largeResponse.Body);
            AssertPageSurface(smallPage.RootElement, sharedMatchId, 0);
            AssertPageSurface(largePage.RootElement, sharedMatchId, 0);
            Assert.Equal(Math.Min(limit, 3), largePage.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(smallPage.RootElement.GetProperty("nextCursor").GetString(), largePage.RootElement.GetProperty("nextCursor").GetString());
            Assert.Equal(smallPage.RootElement.GetProperty("pageBytes").GetInt64(), largePage.RootElement.GetProperty("pageBytes").GetInt64());
            Assert.Equal(NormalizedCommitments(smallResponse.Body), NormalizedCommitments(largeResponse.Body));
            Assert.DoesNotContain("SYNTHETIC_HIDDEN_HAND_", largeResponse.Body, StringComparison.Ordinal);
        }
        Assert.Equal(smallBefore, await small.ArchiveDigestAsync());
        Assert.Equal(largeBefore, await large.ArchiveDigestAsync());
        small.Succeeded = large.Succeeded = true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task VisibleOutputBudgetAloneCanCutPageBeforeRequestedCommandLimit(int format)
    {
        await using var fixture = await Fixture.CreateAsync(format, count: 3, visibleHandNameChars: 2_200_000);
        var legacy = await fixture.LegacyAsync(0);
        Assert.Equal(HttpStatusCode.OK, legacy.Status);
        using var original = JsonDocument.Parse(legacy.Body);
        Assert.True(Encoding.UTF8.GetByteCount(original.RootElement.GetProperty("commands").GetRawText())
            > MatchRecorder.MaximumReplayPageBytes);
        var before = await fixture.ArchiveDigestAsync();
        var collected = new List<string>();
        string? cursor = null;
        for (var index = 0; index < 3; index++)
        {
            var response = await fixture.PageAsync(cursor: cursor, limit: 100);
            Assert.Equal(HttpStatusCode.OK, response.Status);
            using var page = JsonDocument.Parse(response.Body);
            AssertPageSurface(page.RootElement, fixture.MatchId, 0);
            Assert.Equal(1, page.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(3, page.RootElement.GetProperty("totalCommands").GetInt64());
            collected.Add(page.RootElement.GetProperty("items")[0].GetRawText());
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            if (index < 2) Assert.NotNull(cursor); else Assert.Null(cursor);
        }
        Assert.Equal(original.RootElement.GetProperty("commands").EnumerateArray().Select(item => item.GetRawText()), collected);
        Assert.Equal(before, await fixture.ArchiveDigestAsync());
        fixture.Succeeded = true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NewPlayerPageOversizedRawInputFailsEntireReadWithoutChangingOldWholeReader(int format)
    {
        // This 4 MiB input cap is a NEW player-page protection, not an old whole-reader contract.
        await using var fixture = await Fixture.CreateAsync(format, count: 3,
            hiddenHandNameChars: checked((int)MatchRecorder.MaximumReplayPageBytes + 1_024));
        var raw = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.True(Encoding.UTF8.GetByteCount(raw.Commands[0].State.GetRawText()) > MatchRecorder.MaximumReplayPageBytes);
        var legacyBefore = await fixture.LegacyAsync(0);
        Assert.Equal(HttpStatusCode.OK, legacyBefore.Status);
        var before = await fixture.ArchiveDigestAsync();
        var response = await fixture.PageAsync(limit: 100);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.Status);
        using var failure = JsonDocument.Parse(response.Body);
        Assert.Equal(new[] { "code", "correlationId", "message" },
            failure.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(failure.RootElement.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("SYNTHETIC_HIDDEN_HAND_", response.Body, StringComparison.Ordinal);
        Assert.False(failure.RootElement.TryGetProperty("items", out _));
        Assert.False(failure.RootElement.TryGetProperty("nextCursor", out _));
        Assert.Equal(before, await fixture.ArchiveDigestAsync());
        Assert.Equal(legacyBefore.Body, (await fixture.LegacyAsync(0)).Body);
        fixture.Succeeded = true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task OversizedUtf8CommandIsRejectedBeforeReconstructionWithoutReturningAPrefix(int format)
    {
        await using var fixture = await Fixture.CreateAsync(format, count: 3);
        var command = "{\"type\":\"oversizedSynthetic\",\"padding\":\"" + new string('汉', 1_500_000) + "\"}";
        Assert.True(command.Length < MatchRecorder.MaximumReplayPageBytes);
        Assert.True(Encoding.UTF8.GetByteCount(command) > MatchRecorder.MaximumReplayPageBytes);
        await fixture.SqlAsync("UPDATE match_events SET command_json=$value WHERE match_id=$match AND sequence=2;", command);
        var before = await fixture.ArchiveDigestAsync();
        var response = await fixture.PageAsync(limit: 100);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.Status);
        using var failure = JsonDocument.Parse(response.Body);
        Assert.Equal("replay_page_too_large", failure.RootElement.GetProperty("code").GetString());
        Assert.False(failure.RootElement.TryGetProperty("items", out _));
        Assert.False(failure.RootElement.TryGetProperty("nextCursor", out _));
        Assert.DoesNotContain("oversizedSynthetic", response.Body, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.ArchiveDigestAsync());
        fixture.Succeeded = true;
    }

    private static string NormalizedCommitments(string body)
    {
        var root = JsonNode.Parse(body)!.AsObject();
        // Hash values legitimately commit to hidden state; their fixed-length representation
        // must not affect page cuts. Only these commitments differ in this paired initial state.
        root["match"]!.AsObject()["finalHash"] = new string('h', 64);
        var items = (root["items"] ?? root["commands"])!.AsArray();
        foreach (var item in items) item!.AsObject()["stateHash"] = new string('h', 64);
        return root.ToJsonString();
    }

    [Theory]
    [InlineData("GetMatchAsync")]
    [InlineData("GetMatchForAccountAsync")]
    public async Task ExistingWholeReaderSupportsPreCancelledTokenWithoutChangingCompatibility(string name)
    {
        await using var fixture = await Fixture.CreateAsync(2);
        var parameterTypes = name == "GetMatchAsync"
            ? new[] { typeof(string), typeof(CancellationToken) }
            : new[] { typeof(string), typeof(string), typeof(string), typeof(CancellationToken) };
        var method = typeof(MatchRecorder).GetMethod(name, BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: parameterTypes, modifiers: null);
        Assert.NotNull(method); // Baseline remains compilable: a missing cancellation contract is a test failure.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            Task task;
            try
            {
                var arguments = name == "GetMatchAsync"
                    ? new object[] { fixture.MatchId, cancellation.Token }
                    : new object[] { fixture.MatchId, fixture.OwnerIds[0], fixture.OwnerNames[0], cancellation.Token };
                task = Assert.IsAssignableFrom<Task>(method.Invoke(fixture.Recorder, arguments));
            }
            catch (TargetInvocationException error) when (error.InnerException is OperationCanceledException cancelled)
            {
                throw cancelled;
            }
            await task;
        });
        Assert.Equal(HttpStatusCode.OK, (await fixture.LegacyAsync(0)).Status);
        fixture.Succeeded = true;
    }

    private static void AssertPageSurface(JsonElement page, string matchId, int viewer)
    {
        Assert.Equal(new[] { "items", "limit", "match", "nextCursor", "pageBytes", "totalCommands", "viewerPlayerIndex" },
            page.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(matchId, page.GetProperty("match").GetProperty("matchId").GetString());
        Assert.Equal(viewer, page.GetProperty("viewerPlayerIndex").GetInt32());
        Assert.Equal(string.Empty, page.GetProperty("match").GetProperty("deck0").GetString());
        Assert.Equal(string.Empty, page.GetProperty("match").GetProperty("deck1").GetString());
        Assert.InRange(page.GetProperty("pageBytes").GetInt64(), 0, MatchRecorder.MaximumReplayPageBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(page.GetProperty("items").GetRawText()), page.GetProperty("pageBytes").GetInt64());
        Assert.InRange(Encoding.UTF8.GetByteCount(page.GetRawText()), 1, checked((int)MatchRecorder.MaximumReplayPageBytes));
        Assert.DoesNotContain(PrivateDeck0, page.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateDeck1, page.GetRawText(), StringComparison.Ordinal);
    }

    private sealed record Response(HttpStatusCode Status, string Body);

    private sealed class Fixture : IAsyncDisposable
    {
        private const string Password = "ReplayTest-123!";
        private readonly string directory = Path.Combine(Path.GetTempPath(), "l12-player-pages", Guid.NewGuid().ToString("N"));
        private readonly string? previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        private L12WebSocketServer? server;
        private HttpClient? client;
        internal DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        internal readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        internal MatchRecorder Recorder { get; private set; } = null!;
        internal L12PlatformStore Store { get; private set; } = null!;
        internal string MatchId { get; private set; } = string.Empty;
        internal string[] OwnerIds { get; private set; } = [];
        internal string[] OwnerNames { get; private set; } = [];
        internal string[] Tokens { get; private set; } = [];
        internal string OutsiderName { get; private set; } = string.Empty;
        internal string OutsiderToken { get; private set; } = string.Empty;
        internal bool Succeeded;
        private string DbPath => Path.Combine(directory, "matches.db");

        internal static async Task<Fixture> CreateAsync(int format, int count = 8, bool completed = true,
            int hiddenHandNameChars = 0, int visibleHandNameChars = 0, string? matchIdOverride = null)
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.directory);
            try
            {
                fixture.Recorder = new MatchRecorder(fixture.DbPath, () => fixture.Now);
                fixture.Recorder.AttachCatalog(fixture.Catalog);
                await fixture.Recorder.InitializeAsync();
                fixture.Store = new L12PlatformStore(Path.Combine(fixture.directory, "platform.json"),
                    fixture.Catalog.PresetDecks, officialCards: fixture.Catalog.Cards);
                var firstRegistration = fixture.Store.Register("pagefirst", Password);
                var secondRegistration = fixture.Store.Register("pagesecond", Password);
                var outsiderRegistration = fixture.Store.Register("pageother", Password);
                Assert.True(firstRegistration.Success, firstRegistration.Message);
                Assert.True(secondRegistration.Success, secondRegistration.Message);
                Assert.True(outsiderRegistration.Success, outsiderRegistration.Message);
                var first = Assert.IsType<L12AccountView>(firstRegistration.Account);
                var second = Assert.IsType<L12AccountView>(secondRegistration.Account);
                var outsider = Assert.IsType<L12AccountView>(outsiderRegistration.Account);
                fixture.OwnerIds = [first.Id, second.Id];
                fixture.OwnerNames = [first.Username, second.Username];
                fixture.Tokens = fixture.OwnerNames.Select(name => fixture.Store.Login(name, Password).Token!).ToArray();
                fixture.OutsiderName = outsider.Username;
                fixture.OutsiderToken = fixture.Store.Login(outsider.Username, Password).Token!;
                fixture.MatchId = (await fixture.RecordMatchAsync(format, count, completed,
                    hiddenHandNameChars, visibleHandNameChars, matchIdOverride)).State.MatchId;
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
                var manager = new L12RoomManager(fixture.Catalog, fixture.Recorder, fixture.Store, utcNow: () => fixture.Now);
                fixture.server = new L12WebSocketServer(manager, fixture.Recorder, fixture.Store, fixture.Catalog);
                await fixture.server.StartAsync(0);
                fixture.client = new HttpClient
                {
                    BaseAddress = new UriBuilder(Assert.Single(fixture.server.Addresses)) { Host = "127.0.0.1" }.Uri,
                    Timeout = TimeSpan.FromSeconds(10),
                };
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal async Task<L12GameEngine> RecordMatchAsync(int format, int count, bool completed,
            int hiddenHandNameChars = 0, int visibleHandNameChars = 0, string? matchIdOverride = null)
        {
            var decks = new[] { NamedDeck(Catalog.DeckAt(0), PrivateDeck0), NamedDeck(Catalog.DeckAt(1), PrivateDeck1) };
            var engine = new L12GameEngine(Catalog, matchIdOverride ?? Guid.NewGuid().ToString("N"), "PAGE01", 812104,
                OwnerNames, decks, skipPreparation: true, stateFormatVersion: format,
                autoPassEmptyResponses: false, utcNow: () => Now);
            PadInitialHandName(engine, 1, hiddenHandNameChars, "SYNTHETIC_HIDDEN_HAND_");
            PadInitialHandName(engine, 0, visibleHandNameChars, "SYNTHETIC_VISIBLE_HAND_");
            await Recorder.StartAsync(engine, "friendly", OwnerIds[0], OwnerIds[1], decks);
            for (var actor = 0; actor < 2; actor++)
            {
                Now = Now.AddMilliseconds(1);
                var command = new L12Command("mulligan", CardInstanceIds: []);
                var result = engine.Handle(actor, command);
                Assert.True(result.Accepted, result.Error);
                await Recorder.AppendAsync(engine, actor + 1, actor, JsonSerializer.Serialize(command, WireJson), result);
            }
            var lastPreference = completed ? count - 1 : count;
            for (var sequence = 3; sequence <= lastPreference; sequence++)
            {
                Now = Now.AddMilliseconds(1);
                var mode = (sequence & 1) == 0 ? L12GameEngine.DefaultResponseMode : L12GameEngine.ValidOnlyResponseMode;
                var result = engine.ApplyResponsePreference(0, mode);
                Assert.True(result.Accepted, result.Error);
                await Recorder.AppendAsync(engine, sequence, 0,
                    JsonSerializer.Serialize(new { type = "setResponsePreference", responseMode = mode }, WireJson), result);
            }
            if (completed)
            {
                Now = Now.AddMilliseconds(1);
                const string reason = "synthetic player paging complete";
                engine.ConcludeByAuthority(0, reason);
                await Recorder.AppendAuthorityAsync(engine, count, reason);
                Assert.True(await Recorder.CompleteAsync(engine));
            }
            return engine;
        }

        internal Task<Response> LegacyAsync(int viewer) => SendAsync($"/api/matches/{MatchId}", Tokens[viewer]);

        internal Task<Response> PageAsync(int viewer = 0, string? cursor = null, int limit = 2,
            string? matchId = null, string? token = null, bool anonymous = false)
        {
            var path = $"/api/matches/{matchId ?? MatchId}/replay?limit={limit}";
            if (cursor is not null) path += "&cursor=" + Uri.EscapeDataString(cursor);
            return SendAsync(path, anonymous ? null : token ?? Tokens[viewer]);
        }

        private async Task<Response> SendAsync(string path, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client!.SendAsync(request);
            return new Response(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        internal async Task SqlAsync(string sql, string value)
        {
            await using var connection = new SqliteConnection($"Data Source={DbPath}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$match", MatchId);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<long> ScalarAsync(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$match", MatchId);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        internal async Task SeedNewerSummariesAsync()
        {
            await SqlAsync("UPDATE matches SET ended_utc=$value WHERE match_id=$match;", Now.AddMinutes(-30).ToString("O"));
            await using var connection = new SqliteConnection($"Data Source={DbPath}");
            await connection.OpenAsync();
            for (var index = 0; index < 11; index++)
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                        started_utc,ended_utc,mode_id,account_0,account_1)
                    VALUES($id,'NEWER',1,$first,$second,'','',$start,$end,'friendly',$owner0,$owner1);
                    """;
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("$first", OwnerNames[0]);
                command.Parameters.AddWithValue("$second", OwnerNames[1]);
                command.Parameters.AddWithValue("$owner0", OwnerIds[0]);
                command.Parameters.AddWithValue("$owner1", OwnerIds[1]);
                command.Parameters.AddWithValue("$start", Now.AddMinutes(-20).ToString("O"));
                command.Parameters.AddWithValue("$end", Now.AddSeconds(-index - 1).ToString("O"));
                await command.ExecuteNonQueryAsync();
            }
        }

        internal async Task<string> ArchiveDigestAsync()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var connection = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                "SELECT command_json,state_json,state_hash FROM match_events WHERE match_id=$match ORDER BY sequence;",
                "SELECT state_hash,state_blob FROM match_state_checkpoints WHERE match_id=$match ORDER BY sequence;",
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$match", MatchId);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        var value = reader.GetValue(index);
                        hash.AppendData(value is byte[] bytes ? bytes : Encoding.UTF8.GetBytes(Convert.ToString(value)!));
                        hash.AppendData(new byte[] { 0 });
                    }
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public async ValueTask DisposeAsync()
        {
            client?.Dispose();
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (Recorder is not null) await Recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Succeeded)
            {
                var target = Path.GetFullPath(directory);
                var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "l12-player-pages"));
                if (Path.GetDirectoryName(target) != parent || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
                    throw new InvalidOperationException("Unowned player replay fixture path");
                PlatformTestDirectoryCleanup.Delete(target);
            }
            else Console.WriteLine("Failed synthetic player replay fixture retained: " + directory);
        }

        private static L12PresetDeckDefinition NamedDeck(L12PresetDeckDefinition source, string name) => new()
        {
            Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds],
            MoraleIds = [.. source.MoraleIds], SpecialIds = [.. source.SpecialIds],
        };

        private static void PadInitialHandName(L12GameEngine engine, int actor, int length, string marker)
        {
            if (length == 0) return;
            var hand = engine.State.Players[actor].Hand;
            Assert.NotEmpty(hand);
            // Immutable card identity/type/cost/keywords are cloned unchanged before StartAsync;
            // only existing display text differs, and Recorder generates the real V2 hashes.
            var json = JsonSerializer.SerializeToNode(hand[0])!.AsObject();
            json["Name"] = marker + new string('z', length);
            var options = new JsonSerializerOptions { PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate };
            hand[0] = Assert.IsType<L12CardInstance>(json.Deserialize<L12CardInstance>(options));
        }
    }
}
