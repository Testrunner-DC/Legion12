using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedClockAndIntegrityTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OperationWatchdogConcludesAndSettlesExactlyOnceUnderConcurrentTicks()
    {
        await using var fixture = await RankedFixture.CreateAsync("operation", linkedNetwork: true);
        var actingPlayer = fixture.ActingPlayer;
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);

        var batches = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow)));

        var ended = Assert.Single(batches.SelectMany(batch => batch)
            .Select(MessageJson).Where(payload => payload.GetProperty("type").GetString() == "gameState")
            .GroupBy(payload => payload.GetProperty("state").GetProperty("matchId").GetString())
            .Select(group => group.First()));
        Assert.Equal("GameOver", ended.GetProperty("state").GetProperty("phase").GetString());
        Assert.Equal(1 - actingPlayer, ended.GetProperty("state").GetProperty("winner").GetInt32());
        Assert.Contains("单次操作超过4分钟",
            ended.GetProperty("state").GetProperty("winnerReason").GetString());

        var recorded = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.NotNull(recorded.Match.EndedUtc);
        Assert.Single(recorded.Commands, command =>
            command.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.NotNull(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.First.Id));
        Assert.NotNull(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.Second.Id));
        var audit = Assert.Single(fixture.Audits());
        Assert.True(audit.ReviewRecommended);
        Assert.Equal("none", audit.Enforcement);
        Assert.True(audit.NetworkLinked);
        Assert.Contains(audit.Signals, signal => signal.Code == "linked-network");
        Assert.Contains(audit.Signals, signal => signal.Code == "no-meaningful-actions");
        Assert.Contains(audit.Signals, signal => signal.Code == "abnormal-timeout");
    }

    [Fact]
    public async Task TotalWatchdogUsesTheTwentyFiveMinutePlayerBudget()
    {
        await using var fixture = await RankedFixture.CreateAsync("total");
        fixture.Clock.UtcNow += TimeSpan.FromMinutes(25);

        var messages = await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

        var state = MessageJson(messages.First(message =>
            MessageJson(message).GetProperty("type").GetString() == "gameState")).GetProperty("state");
        Assert.Equal("GameOver", state.GetProperty("phase").GetString());
        Assert.Contains("25分钟总操作时间耗尽", state.GetProperty("winnerReason").GetString());
    }

    [Fact]
    public async Task DuplicateDisconnectCannotExtendDeadlineAndBothExpiredDisconnectsInvalidateTheMatch()
    {
        await using (var fixture = await RankedFixture.CreateAsync("disconnect-once"))
        {
            var disconnected = fixture.SessionFor(0);
            fixture.Manager.Disconnect(disconnected);
            fixture.Clock.UtcNow += TimeSpan.FromMinutes(3);
            Assert.Empty(fixture.Manager.Disconnect(disconnected));
            fixture.Clock.UtcNow += TimeSpan.FromMinutes(1);

            await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

            var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
            Assert.Equal(1, detail.Match.Winner);
            Assert.Contains("掉线超过4分钟", detail.Commands.Last().State
                .GetProperty("WinnerReason").GetString());
        }

        await using (var fixture = await RankedFixture.CreateAsync("disconnect-both"))
        {
            fixture.Manager.Disconnect(fixture.SessionFor(0));
            fixture.Manager.Disconnect(fixture.SessionFor(1));
            fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);

            await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);

            var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
            Assert.Null(detail.Match.Winner);
            Assert.Contains("对局无效", detail.Commands.Last().State
                .GetProperty("WinnerReason").GetString());
            Assert.Null(fixture.Platform.RankedSettlement(fixture.MatchId, fixture.First.Id));
            var audit = Assert.Single(fixture.Audits());
            Assert.Equal("both-disconnect-timeout", audit.ConclusionKind);
            Assert.Equal("none", audit.Enforcement);
        }
    }

    [Fact]
    public async Task ReconnectBeforeDeadlineResumesClockButReconnectAfterDeadlineCannotEraseTheLoss()
    {
        await using (var fixture = await RankedFixture.CreateAsync("reconnect-in-time"))
        {
            var actingSession = fixture.SessionFor(fixture.ActingPlayer);
            var actingAccount = fixture.AccountFor(fixture.ActingPlayer);
            fixture.Manager.Disconnect(actingSession);
            fixture.Clock.UtcNow += TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(59);
            var replacement = Guid.NewGuid();
            var session = JsonSerializer.SerializeToElement(await fixture.Manager.ConnectAsync(replacement,
                actingAccount.Id, actingAccount.Username), WebJson);
            Assert.True(session.GetProperty("recovered").GetBoolean());
            fixture.Clock.UtcNow += TimeSpan.FromSeconds(2);

            Assert.Empty(await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow));
            Assert.Null((await fixture.Recorder.GetMatchAsync(fixture.MatchId))!.Match.EndedUtc);
        }

        await using (var fixture = await RankedFixture.CreateAsync("reconnect-late"))
        {
            var actingSession = fixture.SessionFor(fixture.ActingPlayer);
            var actingAccount = fixture.AccountFor(fixture.ActingPlayer);
            fixture.Manager.Disconnect(actingSession);
            fixture.Clock.UtcNow += TimeSpan.FromMinutes(4) + TimeSpan.FromMilliseconds(1);

            await fixture.Manager.ConnectAsync(Guid.NewGuid(), actingAccount.Id, actingAccount.Username);

            var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
            Assert.Equal(1 - fixture.ActingPlayer, detail.Match.Winner);
            Assert.NotNull(detail.Match.EndedUtc);
        }
    }

    [Fact]
    public void HmacNetworkEvidenceIsOptionalPseudonymousAndNeverEnforcesByItself()
    {
        const string secret = "ranked-integrity-test-key-32-bytes-minimum";
        var address = IPAddress.Parse("203.0.113.42");
        var first = L12RankedNetworkPrivacy.Fingerprint(address, secret);
        var second = L12RankedNetworkPrivacy.Fingerprint(address, secret);
        Assert.Null(L12RankedNetworkPrivacy.Fingerprint(address, null));
        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.DoesNotContain(address.ToString(), first!, StringComparison.Ordinal);

        var directory = Path.Combine(Path.GetTempPath(), "l12-ranked-integrity", Guid.NewGuid().ToString("N"));
        var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
        var player = store.Register("integrity-first", "Password123!").Account!;
        var opponent = store.Register("integrity-second", "Password123!").Account!;
        store.SelectRankedFaction(player.Id, "order");
        store.SelectRankedFaction(opponent.Id, "chaos");
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        var context = new L12RankedIntegrityContext(started, started.AddMinutes(10), 5, "normal", first, second);

        store.SettleRankedMatch("network-only", player.Id, opponent.Id, 0, integrity: context);

        var admin = store.Login("Admin", "L12master").Account!;
        var audit = Assert.Single(store.RankedIntegrityAudits(admin, matchId: "network-only"));
        Assert.True(audit.NetworkLinked);
        Assert.False(audit.ReviewRecommended);
        Assert.Equal("none", audit.Enforcement);
        Assert.Single(audit.Signals);
        Assert.DoesNotContain(address.ToString(), File.ReadAllText(Path.Combine(directory, "platform.json")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerWatchdogRunsPeriodicallyAndIntegrityAuditApiIsAdminOnly()
    {
        await using var fixture = await RankedFixture.CreateAsync("server-watchdog", linkedNetwork: true);
        await using var server = new L12WebSocketServer(fixture.Manager, fixture.Recorder, fixture.Platform,
            fixture.Catalog, rankedClockWatchdogInterval: TimeSpan.FromMilliseconds(10));
        await server.StartAsync(0);
        try
        {
            fixture.Clock.UtcNow += TimeSpan.FromMinutes(4);
            L12MatchDetail? detail = null;
            for (var attempt = 0; attempt < 100 && detail?.Match.EndedUtc is null; attempt++)
            {
                await Task.Delay(10);
                detail = await fixture.Recorder.GetMatchAsync(fixture.MatchId);
            }
            Assert.NotNull(detail?.Match.EndedUtc);

            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var playerLogin = fixture.Platform.Login(fixture.First.Username, "Password123!");
            using (var forbidden = new HttpRequestMessage(HttpMethod.Get,
                       $"/api/admin/ranked/integrity-audits?matchId={fixture.MatchId}"))
            {
                forbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", playerLogin.Token);
                using var response = await client.SendAsync(forbidden);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            var adminLogin = fixture.Platform.Login("Admin", "L12master");
            using (var allowed = new HttpRequestMessage(HttpMethod.Get,
                       $"/api/admin/ranked/integrity-audits?matchId={fixture.MatchId}"))
            {
                allowed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.Token);
                using var response = await client.SendAsync(allowed);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var audits = await response.Content.ReadFromJsonAsync<L12RankedIntegrityAuditView[]>();
                Assert.Single(audits!);
                Assert.Equal("none", audits![0].Enforcement);
            }
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static JsonElement MessageJson(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WebJson);

    private sealed class TestClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RankedFixture : IAsyncDisposable
    {
        private readonly string _directory;
        public required TestClock Clock { get; init; }
        public required L12Catalog Catalog { get; init; }
        public required L12PlatformStore Platform { get; init; }
        public required MatchRecorder Recorder { get; init; }
        public required L12RoomManager Manager { get; init; }
        public required L12AccountView First { get; init; }
        public required L12AccountView Second { get; init; }
        public required Guid FirstSession { get; init; }
        public required Guid SecondSession { get; init; }
        public required string MatchId { get; init; }
        public required int ActingPlayer { get; init; }

        private RankedFixture(string directory) => _directory = directory;

        public static async Task<RankedFixture> CreateAsync(string suffix, bool linkedNetwork = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"l12-ranked-clock-{suffix}-{Guid.NewGuid():N}");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var first = platform.Register("clock-first", "Password123!").Account!;
            var second = platform.Register("clock-second", "Password123!").Account!;
            platform.SelectRankedFaction(first.Id, "order");
            platform.SelectRankedFaction(second.Id, "chaos");
            var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var clock = new TestClock();
            var manager = new L12RoomManager(catalog, recorder, platform, () => clock.UtcNow);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            var fingerprint = linkedNetwork
                ? L12RankedNetworkPrivacy.Fingerprint(IPAddress.Parse("198.51.100.9"),
                    "clock-integrity-test-key-32-bytes-minimum")
                : null;
            manager.Connect(firstSession, first.Id, first.Username, fingerprint);
            manager.Connect(secondSession, second.Id, second.Username, fingerprint);
            await manager.JoinMatchmakingAsync(firstSession, "ranked", null);
            var matched = await manager.JoinMatchmakingAsync(secondSession, "ranked", null);
            var game = matched.Where(message => message.SessionId == firstSession).Select(MessageJson)
                .Single(payload => payload.GetProperty("type").GetString() == "gameState");
            var acting = game.GetProperty("rankedClock").GetProperty("players").EnumerateArray()
                .Single(player => player.GetProperty("acting").GetBoolean()).GetProperty("playerIndex").GetInt32();
            return new RankedFixture(directory)
            {
                Clock = clock,
                Catalog = catalog,
                Platform = platform,
                Recorder = recorder,
                Manager = manager,
                First = first,
                Second = second,
                FirstSession = firstSession,
                SecondSession = secondSession,
                MatchId = game.GetProperty("state").GetProperty("matchId").GetString()!,
                ActingPlayer = acting,
            };
        }

        public Guid SessionFor(int playerIndex) => playerIndex == 0 ? FirstSession : SecondSession;
        public L12AccountView AccountFor(int playerIndex) => playerIndex == 0 ? First : Second;

        public IReadOnlyList<L12RankedIntegrityAuditView> Audits()
        {
            var admin = Platform.Login("Admin", "L12master").Account!;
            return Platform.RankedIntegrityAudits(admin, matchId: MatchId);
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            if (!Directory.Exists(_directory)) return;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try
                {
                    Directory.Delete(_directory, true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    // Windows may retain a just-closed SQLite handle for a very short time.
                    // Retry only fixture cleanup; never mask a rule/test assertion failure.
                    await Task.Delay(40 * attempt);
                }
            }
        }
    }
}
