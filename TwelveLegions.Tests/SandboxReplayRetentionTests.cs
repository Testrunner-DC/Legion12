using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SandboxReplayRetentionTests
{
    private static readonly JsonSerializerOptions WebJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task SandboxReplayIsAdminOnlyPagedAndExpiresWithoutDeletingBugReport()
    {
        var directory = TestDirectory("admin-expiry");
        var matchPath = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        await using var recorder = new MatchRecorder(matchPath, () => now);
        await recorder.InitializeAsync();

        var initialSchedule = await recorder.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
        Assert.False(initialSchedule.Ran);
        Assert.Equal(now.AddDays(7), initialSchedule.NextRunUtc);

        var game = new L12GameEngine(catalog, "sandbox-admin-replay", "SBOX01", 901,
            ["沙盒控制者", "测试对手"], decks, skipPreparation: true);
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        await recorder.StartAsync(game, "sandbox", "sandbox-owner", null, decks);
        var hiddenOpponentCard = game.State.Players[1].Hand[0].CardId;
        for (var sequence = 1; sequence <= 23; sequence++)
        {
            var command = new L12GmCommand("setLife", 1, Value: 20 + sequence % 5);
            var result = game.HandleGm(command);
            Assert.True(result.Accepted);
            var json = JsonSerializer.Serialize(command);
            await recorder.AppendAsync(game, sequence, -1, json, result);
            if (sequence == 1)
                await recorder.AppendAsync(game, sequence, -1, json, result);
        }

        var stored = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(game.State.MatchId));
        Assert.Equal(23, stored.Commands.Count);
        Assert.Equal(23, stored.Commands.Select(command => command.Sequence).Distinct().Count());
        Assert.All(stored.Commands, command => Assert.Equal(-1, command.PlayerIndex));
        Assert.Contains(hiddenOpponentCard, stored.Commands[^1].State.GetRawText());
        Assert.Empty(await recorder.ListMatchesForAccountAsync("sandbox-owner", "沙盒控制者"));
        Assert.Null(await recorder.GetMatchForAccountAsync(game.State.MatchId, "sandbox-owner", "沙盒控制者"));
        Assert.Empty(await recorder.ListRankingMatchesAsync());
        Assert.Empty((await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: [decks[0].CardIds[0]]))).Items);
        Assert.DoesNotContain((await recorder.ListAdminMatchesAsync(new L12AdminMatchQuery())).Items,
            item => item.MatchId == game.State.MatchId);
        Assert.Contains((await recorder.ListAdminMatchesAsync(new L12AdminMatchQuery(ModeId: "sandbox"))).Items,
            item => item.MatchId == game.State.MatchId);
        Assert.Empty((await recorder.ListAdminMatchesForAccountAsync("sandbox-owner",
            new L12AdminMatchQuery(ModeId: "sandbox"))).Items);
        var adminDetail = Assert.IsType<L12AdminMatchDetail>(
            await recorder.GetAdminMatchAsync(game.State.MatchId, includeReplay: true));
        Assert.Equal(23, adminDetail.Replay.Count);
        Assert.Empty(adminDetail.CardFacts);

        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var player = platform.Register("tsandb04a63", "Password123!").Account!;
        var bug = platform.AddBug(player, "沙盒回放问题", "用于验证过期后 Bug 主记录仍保留", "/sandbox",
            game.State.RoomCode, game.State.MatchId, "test-client");
        var manager = new L12RoomManager(catalog, recorder, platform, () => now);

        await AssertApiVisibilityAsync(manager, recorder, platform, catalog, game.State.MatchId,
            player.Username, "Password123!", hiddenOpponentCard, expired: false);

        Assert.True(await recorder.CloseSandboxAsync(game));
        Assert.Empty(await recorder.ListMatchesForAccountAsync("sandbox-owner", "沙盒控制者"));
        Assert.Null(await recorder.GetMatchForAccountAsync(game.State.MatchId,
            "sandbox-owner", "沙盒控制者"));
        now = now.AddDays(7);
        var boundary = await recorder.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
        Assert.True(boundary.Ran);
        Assert.Equal(0, boundary.Deleted);
        Assert.NotNull(await recorder.GetMatchAsync(game.State.MatchId));
        now = now.AddDays(7);
        var cleanup = await recorder.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
        Assert.True(cleanup.Ran);
        Assert.Equal(1, cleanup.Deleted);
        Assert.Equal(now.AddDays(7), cleanup.NextRunUtc);
        Assert.Null(await recorder.GetMatchAsync(game.State.MatchId));
        Assert.True(await recorder.IsSandboxReplayExpiredAsync(game.State.MatchId));
        Assert.Contains(platform.Bugs(null), item => item.Id == bug.Id && item.MatchId == game.State.MatchId);

        await using (var connection = new SqliteConnection($"Data Source={matchPath}"))
        {
            await connection.OpenAsync();
            var inspect = connection.CreateCommand();
            inspect.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM matches WHERE match_id=$match),
                    (SELECT COUNT(*) FROM match_events WHERE match_id=$match),
                    (SELECT COUNT(*) FROM match_participants WHERE match_id=$match),
                    (SELECT COUNT(*) FROM match_deck_cards WHERE match_id=$match),
                    (SELECT COUNT(*) FROM match_card_facts WHERE match_id=$match),
                    (SELECT COUNT(*) FROM sandbox_recordings WHERE match_id=$match),
                    (SELECT COUNT(*) FROM sandbox_replay_expirations WHERE match_id=$match);
                """;
            inspect.Parameters.AddWithValue("$match", game.State.MatchId);
            await using var reader = await inspect.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (var index = 0; index < 6; index++) Assert.Equal(0, reader.GetInt32(index));
            Assert.Equal(1, reader.GetInt32(6));
        }

        await AssertApiVisibilityAsync(manager, recorder, platform, catalog, game.State.MatchId,
            player.Username, "Password123!", hiddenOpponentCard, expired: true);
        Assert.Contains(platform.AdminAudit(category: "match"), audit =>
            audit.Action == "read-sandbox-replay-expired" && audit.Target == game.State.MatchId
            && audit.Permission == "admin.matches.read");
    }

    [Fact]
    public async Task WeeklyScheduleSurvivesRestartAndProtectsActiveSandboxAtBoundary()
    {
        var directory = TestDirectory("weekly-active");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var origin = new DateTimeOffset(2026, 9, 1, 4, 30, 0, TimeSpan.Zero);
        var now = origin;

        await using (var first = new MatchRecorder(path, () => now))
        {
            await first.InitializeAsync();
            foreach (var id in new[] { "sandbox-protected", "sandbox-expired", "sandbox-ranked-anomaly" })
            {
                var game = new L12GameEngine(catalog, id, id == "sandbox-protected" ? "PROT01" : "EXPR01",
                    id.GetHashCode(), ["甲", "乙"], decks, skipPreparation: true);
                await first.StartAsync(game, "sandbox", "account-a", null, decks);
                Assert.True(await first.CloseSandboxAsync(game));
            }
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var anomaly = connection.CreateCommand();
            anomaly.CommandText = """
                INSERT INTO ranked_match_runtime(
                    match_id,room_code,status,checkpoint_json,checkpoint_hash,checkpoint_generation,updated_utc)
                VALUES('sandbox-ranked-anomaly','ANOM01','completed','{}','hash',1,$utc);
                """;
            anomaly.Parameters.AddWithValue("$utc", origin.ToString("O"));
            await anomaly.ExecuteNonQueryAsync();
        }

        now = origin.AddDays(6).AddHours(23);
        await using (var restarted = new MatchRecorder(path, () => now))
        {
            await restarted.InitializeAsync();
            var early = await restarted.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
            Assert.False(early.Ran);
            Assert.Equal(origin.AddDays(7), early.NextRunUtc);

            now = origin.AddDays(7).AddSeconds(1);
            var boundary = await restarted.RunSandboxReplayCleanupIfDueAsync(
                ["sandbox-protected"], now);
            Assert.True(boundary.Ran);
            Assert.Equal(1, boundary.Deleted);
            Assert.NotNull(await restarted.GetMatchAsync("sandbox-protected"));
            Assert.Null(await restarted.GetMatchAsync("sandbox-expired"));
            Assert.NotNull(await restarted.GetMatchAsync("sandbox-ranked-anomaly"));
        }

        now = origin.AddDays(13);
        await using var secondRestart = new MatchRecorder(path, () => now);
        await secondRestart.InitializeAsync();
        var persisted = await secondRestart.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
        Assert.False(persisted.Ran);
        Assert.Equal(origin.AddDays(14).AddSeconds(1), persisted.NextRunUtc);
        now = origin.AddDays(14).AddSeconds(1);
        var nextWeeklyRun = await secondRestart.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
        Assert.True(nextWeeklyRun.Ran);
        Assert.Equal(1, nextWeeklyRun.Deleted);
        Assert.Null(await secondRestart.GetMatchAsync("sandbox-protected"));
        Assert.NotNull(await secondRestart.GetMatchAsync("sandbox-ranked-anomaly"));
    }

    [Fact]
    public async Task ActiveDatabaseStateAndDurableLeasePreventCleanupAndDuplicateWeeklyRuns()
    {
        var directory = TestDirectory("active-and-lease");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var origin = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var now = origin;

        await using (var owner = new MatchRecorder(path, () => now))
        {
            await owner.InitializeAsync();
            var active = new L12GameEngine(catalog, "sandbox-db-active", "ACTV01", 933,
                ["甲", "乙"], decks, skipPreparation: true);
            await owner.StartAsync(active, "sandbox", "account-a", null, decks);
            now = origin.AddDays(7).AddSeconds(1);
            var protectedRun = await owner.RunSandboxReplayCleanupIfDueAsync(utcNow: now);
            Assert.True(protectedRun.Ran);
            Assert.Equal(0, protectedRun.Deleted);
            Assert.NotNull(await owner.GetMatchAsync(active.State.MatchId));
        }

        // A restart classifies the prior active row as abandoned at its last activity. Move to
        // the next persisted weekly boundary and let two recorder instances contend for one lease.
        now = origin.AddDays(14).AddSeconds(2);
        await using var first = new MatchRecorder(path, () => now);
        await first.InitializeAsync();
        await using var second = new MatchRecorder(path, () => now);
        await second.InitializeAsync();
        var runs = await Task.WhenAll(
            first.RunSandboxReplayCleanupIfDueAsync(utcNow: now),
            second.RunSandboxReplayCleanupIfDueAsync(utcNow: now));
        Assert.Single(runs, result => result.Ran);
        Assert.Equal(1, runs.Sum(result => result.Deleted));
        Assert.Null(await first.GetMatchAsync("sandbox-db-active"));

        await using var inspect = new SqliteConnection($"Data Source={path}");
        await inspect.OpenAsync();
        var schedule = inspect.CreateCommand();
        schedule.CommandText = """
            SELECT last_run_utc,next_run_utc,lease_owner,lease_expires_utc
            FROM sandbox_replay_cleanup_schedule WHERE singleton_id=1;
            """;
        await using var reader = await schedule.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(now, DateTimeOffset.Parse(reader.GetString(0)));
        Assert.Equal(now.AddDays(7), DateTimeOffset.Parse(reader.GetString(1)));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
    }

    [Fact]
    public async Task RestartKeepsOrphanReplayReadableButNeverRestoresSandboxAsLiveRoom()
    {
        var directory = TestDirectory("restart-orphan");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var now = new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
        string matchId;

        await using (var first = new MatchRecorder(path, () => now))
        {
            await first.InitializeAsync();
            var manager = new L12RoomManager(catalog, first, utcNow: () => now);
            var controller = Guid.NewGuid();
            manager.Connect(controller, "sandbox-restart-owner", "重启沙盒控制者");
            var created = await manager.CreateSandboxAsync(controller, new L12SandboxRequest());
            matchId = StateFor(controller, created).GetProperty("matchId").GetString()!;
            var command = await manager.HandleGmActionAsync(controller,
                JsonSerializer.SerializeToElement(new { type = "setLife", targetPlayer = 1, value = 19 }));
            Assert.Equal(19, StateFor(controller, command).GetProperty("players")[1]
                .GetProperty("master").GetProperty("hp").GetInt32());
        }

        now = now.AddHours(1);
        await using var restarted = new MatchRecorder(path, () => now);
        await restarted.InitializeAsync();
        var replay = Assert.IsType<L12AdminMatchDetail>(
            await restarted.GetAdminMatchAsync(matchId, includeReplay: true));
        Assert.Single(replay.Replay);
        var replayPage = Assert.IsType<L12AdminReplayPage>(
            await restarted.GetAdminReplayPageAsync(matchId, null, 10));
        Assert.Single(replayPage.Items);
        Assert.Equal("invalid", replay.Summary.Status);
        Assert.Contains("服务重启", replay.Summary.Error);
        Assert.Empty(await restarted.ListMatchesForAccountAsync("sandbox-restart-owner", "重启沙盒控制者"));

        var freshManager = new L12RoomManager(catalog, restarted, utcNow: () => now);
        var stats = freshManager.RuntimeStats();
        Assert.Equal(0, stats.RoomCount);
        Assert.Equal(0, stats.ActiveGameCount);

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var metadata = connection.CreateCommand();
        metadata.CommandText = """
            SELECT status,retention_anchor_utc,last_activity_utc
            FROM sandbox_recordings WHERE match_id=$match;
            """;
        metadata.Parameters.AddWithValue("$match", matchId);
        await using var reader = await metadata.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("abandoned", reader.GetString(0));
        Assert.Equal(reader.GetString(2), reader.GetString(1));
    }

    [Fact]
    public async Task DisconnectedSandboxGetsReconnectGraceThenUsesLastActivityAsRetentionAnchor()
    {
        var directory = TestDirectory("disconnect-grace");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var origin = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, utcNow: () => now);
        var controller = Guid.NewGuid();
        manager.Connect(controller, "disconnect-sandbox", "断线沙盒");
        var created = await manager.CreateSandboxAsync(controller, new L12SandboxRequest());
        var matchId = StateFor(controller, created).GetProperty("matchId").GetString()!;
        manager.Disconnect(controller);

        now = origin.Add(L12RoomManager.SandboxReconnectGrace).AddSeconds(-1);
        await manager.RunSandboxReplayMaintenanceAsync();
        Assert.Equal(1, manager.RuntimeStats().RoomCount);
        Assert.Null((await recorder.GetMatchAsync(matchId))!.Match.EndedUtc);

        var replacement = Guid.NewGuid();
        var recovered = await manager.ConnectAsync(replacement, "disconnect-sandbox", "断线沙盒");
        Assert.True(recovered.Recovered);
        Assert.Equal(1, manager.RuntimeStats().RoomCount);
        manager.Disconnect(replacement);

        now = now.Add(L12RoomManager.SandboxReconnectGrace).AddSeconds(1);
        await manager.RunSandboxReplayMaintenanceAsync();
        Assert.Equal(0, manager.RuntimeStats().RoomCount);
        var abandoned = Assert.IsType<L12AdminMatchDetail>(
            await recorder.GetAdminMatchAsync(matchId, includeReplay: true));
        Assert.Equal("invalid", abandoned.Summary.Status);
        Assert.Contains("断线超过重连宽限", abandoned.Summary.Error);

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var metadata = connection.CreateCommand();
        metadata.CommandText = """
            SELECT retention_anchor_utc,last_activity_utc
            FROM sandbox_recordings WHERE match_id=$match;
            """;
        metadata.Parameters.AddWithValue("$match", matchId);
        await using var reader = await metadata.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(reader.GetString(1), reader.GetString(0));
        Assert.Equal(origin, DateTimeOffset.Parse(reader.GetString(0)));
    }

    [Fact]
    public async Task ServerRunsDueCleanupOnIndependentObservedMaintenanceLoop()
    {
        var directory = TestDirectory("server-loop");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var decks = new[] { catalog.DeckAt(0), catalog.DeckAt(1) };
        var origin = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        var game = new L12GameEngine(catalog, "sandbox-background-cleanup", "BGCL01", 944,
            ["甲", "乙"], decks, skipPreparation: true);
        await recorder.StartAsync(game, "sandbox", "background-owner", null, decks);
        Assert.True(await recorder.CloseSandboxAsync(game));
        now = origin.AddDays(7).AddSeconds(1);

        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var manager = new L12RoomManager(catalog, recorder, platform, () => now);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog,
            sandboxReplayMaintenanceInterval: TimeSpan.FromMilliseconds(10));
        await server.StartAsync(0);
        try
        {
            for (var attempt = 0; attempt < 300
                                  && !await recorder.IsSandboxReplayExpiredAsync(game.State.MatchId); attempt++)
                await Task.Delay(10);
            Assert.True(await recorder.IsSandboxReplayExpiredAsync(game.State.MatchId));
            Assert.Null(await recorder.GetMatchAsync(game.State.MatchId));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ExactReconnectBoundaryIsSerializedWithoutRevivingAnAbandonedRecording()
    {
        var directory = TestDirectory("reconnect-race");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var origin = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, utcNow: () => now);
        var original = Guid.NewGuid();
        manager.Connect(original, "boundary-owner", "边界重连");
        var created = await manager.CreateSandboxAsync(original, new L12SandboxRequest());
        var matchId = StateFor(original, created).GetProperty("matchId").GetString()!;
        manager.Disconnect(original);
        now = origin.Add(L12RoomManager.SandboxReconnectGrace);

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = Task.Run(async () =>
        {
            await start.Task;
            return await manager.RunSandboxReplayMaintenanceAsync();
        });
        var reconnect = Task.Run(async () =>
        {
            await start.Task;
            return await manager.ConnectAsync(Guid.NewGuid(), "boundary-owner", "边界重连");
        });
        start.SetResult(true);
        await Task.WhenAll(maintenance, reconnect);
        var claim = await reconnect;
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        Assert.Equal(claim.Recovered, detail.Match.EndedUtc is null);
        Assert.Equal(claim.Recovered ? 1 : 0, manager.RuntimeStats().RoomCount);
        if (!claim.Recovered)
            Assert.Equal("沙盒控制者断线超过重连宽限", detail.Match.Error);
    }

    [Fact]
    public async Task ConnectedRealSpectatorBlocksRetirementAndPersistenceFailureNeverDeletesRoom()
    {
        var directory = TestDirectory("retirement-guards");
        var path = Path.Combine(directory, "matches.db");
        var catalog = Catalog();
        var origin = new DateTimeOffset(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, utcNow: () => now);
        var controller = Guid.NewGuid();
        manager.Connect(controller, "guard-owner", "保护沙盒");
        var created = await manager.CreateSandboxAsync(controller, new L12SandboxRequest());
        var state = StateFor(controller, created);
        var matchId = state.GetProperty("matchId").GetString()!;
        var roomCode = state.GetProperty("roomCode").GetString()!;
        manager.Disconnect(controller);

        var spectator = Guid.NewGuid();
        manager.Connect(spectator, "guard-spectator", "真人观战者");
        AddDefensiveSandboxSpectator(manager, roomCode, spectator);
        now = origin.Add(L12RoomManager.SandboxReconnectGrace).AddSeconds(1);
        await manager.RunSandboxReplayMaintenanceAsync();
        Assert.Equal(1, manager.RuntimeStats().RoomCount);
        Assert.Null((await recorder.GetMatchAsync(matchId))!.Match.EndedUtc);

        manager.Disconnect(spectator);
        recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-sandbox-abandon-commit") throw new IOException("injected sandbox failure");
        };
        await manager.RunSandboxReplayMaintenanceAsync();
        Assert.Equal(1, manager.RuntimeStats().RoomCount);
        Assert.Null((await recorder.GetMatchAsync(matchId))!.Match.EndedUtc);
        await using (var inspect = new SqliteConnection($"Data Source={path}"))
        {
            await inspect.OpenAsync();
            var status = inspect.CreateCommand();
            status.CommandText = "SELECT status FROM sandbox_recordings WHERE match_id=$match;";
            status.Parameters.AddWithValue("$match", matchId);
            Assert.Equal("active", Convert.ToString(await status.ExecuteScalarAsync()));
        }

        recorder.StorageFailureInjector = null;
        await manager.RunSandboxReplayMaintenanceAsync();
        Assert.Equal(0, manager.RuntimeStats().RoomCount);
        Assert.NotNull((await recorder.GetMatchAsync(matchId))!.Match.EndedUtc);
    }

    private static async Task AssertApiVisibilityAsync(L12RoomManager manager, MatchRecorder recorder,
        L12PlatformStore platform, L12Catalog catalog, string matchId, string playerName, string playerPassword,
        string hiddenCardId, bool expired)
    {
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var playerLogin = platform.Login(playerName, playerPassword);
            using (var playerReplay = Authorized(HttpMethod.Get, $"/api/matches/{matchId}", playerLogin.Token!))
            using (var response = await client.SendAsync(playerReplay))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var forbidden = Authorized(HttpMethod.Get, $"/api/admin/matches/{matchId}?includeReplay=true",
                       playerLogin.Token!))
            using (var response = await client.SendAsync(forbidden))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var adminLogin = platform.Login("Admin", "L12master");
            using var request = Authorized(HttpMethod.Get,
                $"/api/admin/matches/{matchId}/replay?limit=10", adminLogin.Token!);
            using var adminResponse = await client.SendAsync(request);
            if (expired)
            {
                Assert.Equal(HttpStatusCode.Gone, adminResponse.StatusCode);
                var error = await adminResponse.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("sandbox_replay_expired", error!.Code);
                using var detailRequest = Authorized(HttpMethod.Get,
                    $"/api/admin/matches/{matchId}", adminLogin.Token!);
                using var detailResponse = await client.SendAsync(detailRequest);
                Assert.Equal(HttpStatusCode.Gone, detailResponse.StatusCode);
                var detailError = await detailResponse.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("sandbox_replay_expired", detailError!.Code);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
                var first = await adminResponse.Content.ReadFromJsonAsync<L12AdminReplayPage>();
                Assert.Equal(10, first!.Items.Count);
                Assert.NotNull(first.NextCursor);
                Assert.True(first.PageBytes <= MatchRecorder.MaximumReplayPageBytes);
                Assert.Contains(hiddenCardId, first.Items[^1].State.GetRawText());
                var total = first.Items.Count;
                var cursor = first.NextCursor;
                while (cursor is not null)
                {
                    using var nextRequest = Authorized(HttpMethod.Get,
                        $"/api/admin/matches/{matchId}/replay?limit=10&cursor={Uri.EscapeDataString(cursor)}",
                        adminLogin.Token!);
                    using var nextResponse = await client.SendAsync(nextRequest);
                    Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
                    var page = await nextResponse.Content.ReadFromJsonAsync<L12AdminReplayPage>();
                    total += page!.Items.Count;
                    cursor = page.NextCursor;
                }
                Assert.Equal(23, total);
            }
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static JsonElement StateFor(Guid sessionId, IReadOnlyList<OutgoingMessage> messages) => messages
        .Where(message => message.SessionId == sessionId)
        .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
        .Single(payload => payload.GetProperty("type").GetString() == "gameState")
        .GetProperty("state").Clone();

    private static void AddDefensiveSandboxSpectator(L12RoomManager manager, string roomCode, Guid sessionId)
    {
        var rooms = typeof(L12RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        var room = rooms.GetType().GetProperty("Item")!.GetValue(rooms, [roomCode])!;
        var spectators = (ICollection<Guid>)room.GetType()
            .GetProperty("Spectators", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(room)!;
        spectators.Add(sessionId);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static string TestDirectory(string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-sandbox-replay-retention",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
