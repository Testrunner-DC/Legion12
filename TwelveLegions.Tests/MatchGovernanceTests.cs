using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MatchGovernanceTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void PlayerReportPersistsOutsideBugLedgerAndRequiresAdminGovernancePermission()
    {
        var directory = TempDirectory("report");
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var reporter = store.Register("tgover516c3", "Password123!").Account!;
        var reported = store.Register("tgovera015b", "Password123!").Account!;
        var now = DateTimeOffset.UtcNow;

        var created = store.CreatePlayerMatchReport("report-request-0001", "match-report-1", "ROOM01",
            "casual", reporter, reported, "对手在结算窗口发送辱骂信息", now);

        Assert.Equal(reporter.Id, created.ReporterId);
        Assert.Equal(reported.Id, created.ReportedId);
        Assert.Equal("new", created.Status);
        Assert.Empty(store.Bugs(null));
        Assert.Throws<UnauthorizedAccessException>(() => store.PlayerMatchReports(reporter));

        var reloaded = new L12PlatformStore(path);
        var admin = reloaded.Login("Admin", "L12master").Account!;
        var persisted = Assert.Single(reloaded.PlayerMatchReports(admin, search: "match-report-1"));
        Assert.Equal("对手在结算窗口发送辱骂信息", persisted.Description);
        var updated = reloaded.UpdatePlayerMatchReport(admin, persisted.Id,
            new L12MatchGovernanceUpdate("reviewing", "等待录像核验", "已分配人工复核"),
            new L12AdminAuditContext("governance-report-test"));
        Assert.Equal("reviewing", updated.Status);
        Assert.Equal("等待录像核验", updated.AdminNotes);
        Assert.Contains(updated.History, item => item.Action == "admin-updated"
            && item.Comment == "已分配人工复核");
    }

    [Fact]
    public async Task DrawRequestRejectsUnauthorizedOrConflictingResolutionAndConcurrentReplayIsIdempotent()
    {
        var directory = TempDirectory("draw-cas");
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var requester = store.Register("tdrawrd2dfe", "Password123!").Account!;
        var responder = store.Register("tdrawr6ba84", "Password123!").Account!;
        var outsider = store.Register("tdrawo055e0", "Password123!").Account!;
        var now = DateTimeOffset.UtcNow;
        var created = store.CreateMatchDrawRequest("draw-request-0001", "match-draw-1", "ROOM02",
            "casual", requester, responder, "同步后卡在结算窗口", now);

        Assert.Equal("pending", created.Status);
        Assert.Throws<L12MatchGovernanceConflictException>(() =>
            store.ResolveMatchDrawRequest(created.MatchId, created.Id, outsider, true, now.AddSeconds(1)));
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
            store.ResolveMatchDrawRequest(created.MatchId, created.Id, responder, true,
                now.AddSeconds(index + 1)))));
        Assert.All(results, result => Assert.Equal("accepted", result.Status));
        Assert.Throws<L12MatchGovernanceConflictException>(() =>
            store.ResolveMatchDrawRequest(created.MatchId, created.Id, responder, false, now.AddMinutes(1)));
        Assert.Throws<L12MatchGovernanceConflictException>(() =>
            store.CreateMatchDrawRequest(created.Id, created.MatchId, "ROOM02", "casual",
                requester, responder, "不同原因", now));

        var reloaded = new L12PlatformStore(path);
        var restored = Assert.IsType<L12MatchDrawRequestClientView>(
            reloaded.MatchDrawRequestForClient(created.MatchId, responder.Id, now.AddMinutes(2)));
        Assert.Equal("accepted", restored.Status);
        Assert.False(restored.ViewerCanRespond);
        var admin = reloaded.Login("Admin", "L12master").Account!;
        Assert.Single(reloaded.MatchDrawRequests(admin, status: "accepted"));
    }

    [Fact]
    public async Task MatchAllowsOneDrawRequestAcrossBothPlayersTerminalStatesReloadAndNewMatches()
    {
        var path = Path.Combine(TempDirectory("draw-once"), "platform.json");
        var store = new L12PlatformStore(path);
        var first = store.Register("tdrawo02d56", "Password123!").Account!;
        var second = store.Register("tdrawoef96b", "Password123!").Account!;
        var now = DateTimeOffset.UtcNow;

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
        {
            var requester = index % 2 == 0 ? first : second;
            var responder = index % 2 == 0 ? second : first;
            try
            {
                return store.CreateMatchDrawRequest($"draw-once-race-{index:D2}", "match-draw-once-race",
                    "ONCE01", "casual", requester, responder, $"并发申请 {index}", now.AddMilliseconds(index));
            }
            catch (L12MatchGovernanceConflictException error)
            {
                Assert.Equal("draw_request_limit_reached", error.Code);
                return null;
            }
        })));
        var created = Assert.Single(concurrent.OfType<L12MatchDrawRequestView>());
        var createdRequester = created.RequesterId == first.Id ? first : second;
        var createdResponder = created.ResponderId == first.Id ? first : second;

        var idempotent = store.CreateMatchDrawRequest(created.Id, created.MatchId, created.RoomCode,
            created.ModeId, createdRequester, createdResponder, created.Reason, now.AddSeconds(1));
        Assert.Equal(created.Id, idempotent.Id);
        Assert.Equal("pending", idempotent.Status);
        store.ResolveMatchDrawRequest(created.MatchId, created.Id, createdResponder, false, now.AddSeconds(2));

        var requesterRetry = Assert.Throws<L12MatchGovernanceConflictException>(() =>
            store.CreateMatchDrawRequest("draw-once-after-reject-requester", created.MatchId,
                created.RoomCode, created.ModeId, createdRequester, createdResponder,
                "拒绝后由原申请方再次申请", now.AddSeconds(3)));
        Assert.Equal("draw_request_limit_reached", requesterRetry.Code);
        var responderRetry = Assert.Throws<L12MatchGovernanceConflictException>(() =>
            store.CreateMatchDrawRequest("draw-once-after-reject-responder", created.MatchId,
                created.RoomCode, created.ModeId, createdResponder, createdRequester,
                "拒绝后由另一方再次申请", now.AddSeconds(3)));
        Assert.Equal("draw_request_limit_reached", responderRetry.Code);

        var reloaded = new L12PlatformStore(path);
        var recoveredRetry = Assert.Throws<L12MatchGovernanceConflictException>(() =>
            reloaded.CreateMatchDrawRequest("draw-once-after-reload", created.MatchId,
                created.RoomCode, created.ModeId, createdRequester, createdResponder,
                "服务重启后再次申请", now.AddSeconds(4)));
        Assert.Equal("draw_request_limit_reached", recoveredRetry.Code);

        var accepted = reloaded.CreateMatchDrawRequest("draw-once-accepted", "match-draw-once-accepted",
            "ONCE02", "ranked", first, second, "接受场景", now);
        reloaded.ResolveMatchDrawRequest(accepted.MatchId, accepted.Id, second, true, now.AddSeconds(1));
        var acceptedRetry = Assert.Throws<L12MatchGovernanceConflictException>(() =>
            reloaded.CreateMatchDrawRequest("draw-once-after-accepted", accepted.MatchId,
                accepted.RoomCode, accepted.ModeId, first, second, "接受后再次申请", now.AddSeconds(2)));
        Assert.Equal("draw_request_limit_reached", acceptedRetry.Code);

        var expired = reloaded.CreateMatchDrawRequest("draw-once-expired", "match-draw-once-expired",
            "ONCE03", "casual", first, second, "过期场景", now);
        Assert.Equal("expired", reloaded.MatchDrawRequestForClient(expired.MatchId, first.Id,
            now + L12PlatformStore.MatchDrawRequestLifetime + TimeSpan.FromSeconds(1))!.Status);
        var expiredRetry = Assert.Throws<L12MatchGovernanceConflictException>(() =>
            reloaded.CreateMatchDrawRequest("draw-once-after-expired", expired.MatchId,
                expired.RoomCode, expired.ModeId, second, first, "过期后再次申请", now.AddMinutes(6)));
        Assert.Equal("draw_request_limit_reached", expiredRetry.Code);

        var cancelled = reloaded.CreateMatchDrawRequest("draw-once-cancelled", "match-draw-once-cancelled",
            "ONCE04", "casual", first, second, "取消场景", now);
        reloaded.CancelOpenMatchDrawRequests(cancelled.MatchId, now.AddSeconds(1), "对局已结束");
        var cancelledRetry = Assert.Throws<L12MatchGovernanceConflictException>(() =>
            reloaded.CreateMatchDrawRequest("draw-once-after-cancelled", cancelled.MatchId,
                cancelled.RoomCode, cancelled.ModeId, second, first, "取消后再次申请", now.AddSeconds(2)));
        Assert.Equal("draw_request_limit_reached", cancelledRetry.Code);

        var nextMatch = reloaded.CreateMatchDrawRequest("draw-once-next-match", "match-draw-once-next",
            "ONCE05", "casual", second, first, "下一局可正常申请", now.AddMinutes(7));
        Assert.Equal("pending", nextMatch.Status);
    }

    [Fact]
    public void PendingDrawExpiresAndCannotBeResolvedAfterReconnectProjection()
    {
        var store = new L12PlatformStore(Path.Combine(TempDirectory("draw-expiry"), "platform.json"));
        var requester = store.Register("texpir09807", "Password123!").Account!;
        var responder = store.Register("texpir8f059", "Password123!").Account!;
        var now = DateTimeOffset.UtcNow;
        var created = store.CreateMatchDrawRequest("draw-request-expiry", "match-draw-expiry", "ROOM03",
            "ranked", requester, responder, "客户端恢复后无法继续操作", now);

        var projection = Assert.IsType<L12MatchDrawRequestClientView>(
            store.MatchDrawRequestForClient(created.MatchId, responder.Id,
                now + L12PlatformStore.MatchDrawRequestLifetime + TimeSpan.FromSeconds(1)));

        Assert.Equal("expired", projection.Status);
        Assert.False(projection.ViewerCanRespond);
        Assert.Throws<L12MatchGovernanceConflictException>(() => store.ResolveMatchDrawRequest(
            created.MatchId, created.Id, responder, true, now + TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void InterruptedAcceptanceCheckpointCanResumeButCannotBeChangedToRejection()
    {
        var path = Path.Combine(TempDirectory("accept-resume"), "platform.json");
        var store = new L12PlatformStore(path);
        var requester = store.Register("tresum1a3a3", "Password123!").Account!;
        var responder = store.Register("tresumded94", "Password123!").Account!;
        var now = DateTimeOffset.UtcNow;
        var created = store.CreateMatchDrawRequest("draw-accept-resume", "match-accept-resume", "ROOM05",
            "casual", requester, responder, "接受处理中服务重启", now);
        store.BeginMatchDrawAcceptance(created.MatchId, created.Id, responder, now.AddSeconds(1));

        var reloaded = new L12PlatformStore(path);
        Assert.Equal("accepting", reloaded.ValidateMatchDrawResolution(created.MatchId, created.Id,
            responder.Id, accept: true, now.AddSeconds(2)).Status);
        Assert.Throws<L12MatchGovernanceConflictException>(() => reloaded.ValidateMatchDrawResolution(
            created.MatchId, created.Id, responder.Id, accept: false, now.AddSeconds(2)));
    }

    [Fact]
    public async Task RankedAgreedDrawCreatesOneZeroDeltaPairWithoutInvalidatingOrChangingStats()
    {
        var directory = TempDirectory("ranked-draw");
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);
        var first = store.Register("tranke0aac5", "Password123!").Account!;
        var second = store.Register("tranke93d96", "Password123!").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        var beforeFirst = store.RankedProfile(first.Id);
        var beforeSecond = store.RankedProfile(second.Id);
        var ended = DateTimeOffset.UtcNow;
        var context = new L12RankedIntegrityContext(ended.AddMinutes(-7), ended, 9,
            L12GameEngine.AgreedDrawConclusionKind, string.Empty, string.Empty, 4);

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
            store.SettleRankedDrawMatch("ranked-agreed-draw", first.Id, second.Id,
                integrity: context))));

        Assert.All(results, pair =>
        {
            Assert.Equal("draw", pair.First.Outcome);
            Assert.Equal("draw", pair.Second.Outcome);
            Assert.Equal(0, pair.First.Delta);
            Assert.Equal(pair.First.Before, pair.First.After);
            Assert.Contains(pair.First.Components, item => item.Kind == "draw" && item.Value == 0);
        });
        var afterFirst = store.RankedProfile(first.Id);
        var afterSecond = store.RankedProfile(second.Id);
        Assert.Equal((beforeFirst.SevenValue, beforeFirst.PlacementPlayed, beforeFirst.Wins, beforeFirst.Losses),
            (afterFirst.SevenValue, afterFirst.PlacementPlayed, afterFirst.Wins, afterFirst.Losses));
        Assert.Equal((beforeSecond.SevenValue, beforeSecond.PlacementPlayed, beforeSecond.Wins, beforeSecond.Losses),
            (afterSecond.SevenValue, afterSecond.PlacementPlayed, afterSecond.Wins, afterSecond.Losses));
        var admin = store.Login("Admin", "L12master").Account!;
        var audit = Assert.Single(store.RankedIntegrityAudits(admin, matchId: "ranked-agreed-draw"));
        Assert.Null(audit.Winner);
        Assert.Equal(L12GameEngine.AgreedDrawConclusionKind, audit.ConclusionKind);

        var reloaded = new L12PlatformStore(path);
        var replay = reloaded.SettleRankedDrawMatch("ranked-agreed-draw", first.Id, second.Id,
            integrity: context);
        Assert.Equal("draw", replay.First.Outcome);
        Assert.Throws<InvalidOperationException>(() => reloaded.RecordInvalidRankedMatch(
            "ranked-agreed-draw", first.Id, second.Id, null, null, context));
    }

    [Fact]
    public async Task RealMatchRequestSurvivesDisconnectAndConcurrentAcceptRecordsOneAuthorityConclusion()
    {
        var directory = TempDirectory("room-recovery");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var first = platform.Register("troomd1b12e", "Password123!").Account!;
        var second = platform.Register("troomd2d148", "Password123!").Account!;
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Connect(firstSession, first.Id, first.Username);
        manager.Connect(secondSession, second.Id, second.Username);
        var roomCode = MessageJson(manager.CreateRoom(firstSession)[0]).GetProperty("roomCode").GetString()!;
        manager.JoinRoom(secondSession, roomCode);
        await manager.SetReadyAsync(firstSession, true);
        var started = await manager.SetReadyAsync(secondSession, true);
        var matchId = MessageJson(started.First(message => message.SessionId == firstSession
                && MessageJson(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state").GetProperty("matchId").GetString()!;

        const string requestId = "draw-room-recovery-0001";
        var requested = await manager.RequestMatchDrawAsync(firstSession, requestId,
            "恢复连接后卡在同一个结算窗口");
        var responderState = MessageJson(requested.First(message => message.SessionId == secondSession
            && MessageJson(message).GetProperty("type").GetString() == "gameState"));
        Assert.True(responderState.GetProperty("matchGovernance").GetProperty("drawRequest")
            .GetProperty("viewerCanRespond").GetBoolean());

        manager.Disconnect(secondSession);
        var recoveredSession = Guid.NewGuid();
        var claim = JsonSerializer.SerializeToElement(await manager.ConnectAsync(recoveredSession,
            second.Id, second.Username), WebJson);
        Assert.True(claim.GetProperty("recovered").GetBoolean());
        var recovered = await manager.RecoveryStateWithAckAsync(recoveredSession, recovered: true);
        var recoveredState = MessageJson(recovered.First(message => message.SessionId == recoveredSession
            && MessageJson(message).GetProperty("type").GetString() == "gameState"));
        Assert.Equal(requestId, recoveredState.GetProperty("matchGovernance").GetProperty("drawRequest")
            .GetProperty("id").GetString());
        Assert.True(recoveredState.GetProperty("matchGovernance").GetProperty("drawRequest")
            .GetProperty("viewerCanRespond").GetBoolean());

        var repeated = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => manager.ResolveMatchDrawAsync(recoveredSession, requestId, true)));
        Assert.All(repeated, messages => Assert.Contains(messages, message =>
            MessageJson(message).TryGetProperty("type", out var type) && type.GetString() == "matchGovernanceResult"
            && MessageJson(message).GetProperty("status").GetString() == "accepted"));
        var finalState = repeated.SelectMany(messages => messages).Select(MessageJson)
            .First(payload => payload.TryGetProperty("type", out var type)
                && type.GetString() == "gameState").GetProperty("state");
        Assert.Equal("GameOver", finalState.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, finalState.GetProperty("winner").ValueKind);
        Assert.Contains(finalState.GetProperty("recentEvents").EnumerateArray(), item =>
            item.GetProperty("type").GetString() == "game-draw");
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        Assert.Single(detail.Commands, command =>
            command.Command.GetProperty("type").GetString() == "authorityConclusion");
        var admin = platform.Login("Admin", "L12master").Account!;
        Assert.Equal("accepted", Assert.Single(platform.MatchDrawRequests(admin,
            search: matchId)).Status);
    }

    [Fact]
    public async Task RejectedDrawConsumesRoomOpportunityForBothPlayersWithoutBlockingOriginalResponse()
    {
        var directory = TempDirectory("room-draw-once");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var first = platform.Register("troomo18927", "Password123!").Account!;
        var second = platform.Register("troomo2b923", "Password123!").Account!;
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Connect(firstSession, first.Id, first.Username);
        manager.Connect(secondSession, second.Id, second.Username);
        var roomCode = MessageJson(manager.CreateRoom(firstSession)[0]).GetProperty("roomCode").GetString()!;
        manager.JoinRoom(secondSession, roomCode);
        await manager.SetReadyAsync(firstSession, true);
        var started = await manager.SetReadyAsync(secondSession, true);
        var matchId = MessageJson(started.First(message => message.SessionId == firstSession
                && MessageJson(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state").GetProperty("matchId").GetString()!;

        const string requestId = "draw-room-once-original";
        const string reason = "本局出现无法继续的同步问题";
        var requested = await manager.RequestMatchDrawAsync(firstSession, requestId, reason);
        var incoming = MessageJson(requested.First(message => message.SessionId == secondSession
            && MessageJson(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("matchGovernance");
        Assert.False(incoming.GetProperty("canRequestDraw").GetBoolean());
        Assert.True(incoming.GetProperty("drawRequest").GetProperty("viewerCanRespond").GetBoolean());

        var pendingBypass = MessageJson(Assert.Single(await manager.RequestMatchDrawAsync(secondSession,
            "draw-room-once-pending-bypass", "另一方尝试绕过待处理申请")));
        Assert.Equal("rejected", pendingBypass.GetProperty("status").GetString());
        Assert.Equal("draw_request_limit_reached", pendingBypass.GetProperty("code").GetString());

        var rejected = await manager.ResolveMatchDrawAsync(secondSession, requestId, false);
        var rejectedState = MessageJson(rejected.First(message => message.SessionId == firstSession
            && MessageJson(message).GetProperty("type").GetString() == "gameState"));
        var governance = rejectedState.GetProperty("matchGovernance");
        Assert.False(governance.GetProperty("canRequestDraw").GetBoolean());
        Assert.Contains("仅可发起一次", governance.GetProperty("drawUnavailableReason").GetString());
        Assert.Equal("rejected", governance.GetProperty("drawRequest").GetProperty("status").GetString());

        foreach (var (sessionId, nextRequestId) in new[]
                 {
                     (firstSession, "draw-room-once-first-retry"),
                     (secondSession, "draw-room-once-second-retry"),
                 })
        {
            var retry = MessageJson(Assert.Single(await manager.RequestMatchDrawAsync(sessionId,
                nextRequestId, "终态后尝试再次申请")));
            Assert.Equal("rejected", retry.GetProperty("status").GetString());
            Assert.Equal("draw_request_limit_reached", retry.GetProperty("code").GetString());
        }

        var idempotentRequest = await manager.RequestMatchDrawAsync(firstSession, requestId, reason);
        Assert.Contains(idempotentRequest, message => message.SessionId == firstSession
            && MessageJson(message).TryGetProperty("status", out var status)
            && status.GetString() == "rejected");
        var idempotentResponse = await manager.ResolveMatchDrawAsync(secondSession, requestId, false);
        Assert.Contains(idempotentResponse, message => message.SessionId == secondSession
            && MessageJson(message).TryGetProperty("status", out var status)
            && status.GetString() == "rejected");

        manager.Disconnect(firstSession);
        var recoveredSession = Guid.NewGuid();
        await manager.ConnectAsync(recoveredSession, first.Id, first.Username);
        var recovered = await manager.RecoveryStateWithAckAsync(recoveredSession, recovered: true);
        var recoveredGovernance = MessageJson(recovered.First(message => message.SessionId == recoveredSession
                && MessageJson(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("matchGovernance");
        Assert.False(recoveredGovernance.GetProperty("canRequestDraw").GetBoolean());
        Assert.Contains("仅可发起一次", recoveredGovernance.GetProperty("drawUnavailableReason").GetString());
        var admin = platform.Login("Admin", "L12master").Account!;
        Assert.Single(platform.MatchDrawRequests(admin, search: matchId));
    }

    [Fact]
    public async Task SandboxAndVirtualOpponentRejectDrawAndReportInsteadOfCreatingRecords()
    {
        var directory = TempDirectory("sandbox");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var player = platform.Register("tsandb9d5fa", "Password123!").Account!;
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var session = Guid.NewGuid();
        manager.Connect(session, player.Id, player.Username);
        await manager.CreateSandboxAsync(session, new L12SandboxRequest());

        var draw = Assert.Single(await manager.RequestMatchDrawAsync(session,
            "sandbox-draw-request", "沙盒原因"));
        var report = Assert.Single(await manager.ReportOpponentAsync(session,
            "sandbox-report-request", "沙盒举报"));

        Assert.Equal("rejected", MessageJson(draw).GetProperty("status").GetString());
        Assert.Contains("沙盒", MessageJson(draw).GetProperty("message").GetString());
        Assert.Equal("rejected", MessageJson(report).GetProperty("status").GetString());
        var admin = platform.Login("Admin", "L12master").Account!;
        Assert.Empty(platform.MatchDrawRequests(admin));
        Assert.Empty(platform.PlayerMatchReports(admin));
    }

    [Fact]
    public async Task NaturalGameEndCancelsPendingDrawAndRejectsLateResponseWithoutBlockingLeave()
    {
        var directory = TempDirectory("natural-end");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var first = platform.Register("tnaturb3928", "Password123!").Account!;
        var second = platform.Register("tnatur5e4cf", "Password123!").Account!;
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Connect(firstSession, first.Id, first.Username);
        manager.Connect(secondSession, second.Id, second.Username);
        var roomCode = MessageJson(manager.CreateRoom(firstSession)[0]).GetProperty("roomCode").GetString()!;
        manager.JoinRoom(secondSession, roomCode);
        await manager.SetReadyAsync(firstSession, true);
        var started = await manager.SetReadyAsync(secondSession, true);
        var matchId = MessageJson(started.First(message => message.SessionId == firstSession
                && MessageJson(message).GetProperty("type").GetString() == "gameState"))
            .GetProperty("state").GetProperty("matchId").GetString()!;
        const string requestId = "draw-natural-end-request";
        await manager.RequestMatchDrawAsync(firstSession, requestId, "等待响应期间对局自然结束");

        var surrender = await manager.HandleActionAsync(firstSession,
            JsonSerializer.SerializeToElement(new { type = "surrender" }));
        Assert.Contains(surrender, message => MessageJson(message).TryGetProperty("state", out var state)
            && state.GetProperty("phase").GetString() == "GameOver");
        var admin = platform.Login("Admin", "L12master").Account!;
        var cancelled = Assert.Single(platform.MatchDrawRequests(admin, search: matchId));
        Assert.Equal("cancelled", cancelled.Status);

        var late = await manager.ResolveMatchDrawAsync(secondSession, requestId, true);
        var lateResult = MessageJson(Assert.Single(late));
        Assert.Equal("rejected", lateResult.GetProperty("status").GetString());
        Assert.Contains("已处理", lateResult.GetProperty("message").GetString());
        Assert.Equal("roomLeft", MessageJson(manager.LeaveRoom(firstSession)
            .Single(message => message.SessionId == firstSession)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task RankedRoomAgreedDrawUsesOneOutboxAndSettlesAsDrawExactlyOnce()
    {
        var directory = TempDirectory("ranked-room");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var first = platform.Register("tranked0c65", "Password123!").Account!;
        var second = platform.Register("tranke17624", "Password123!").Account!;
        platform.SelectRankedFaction(first.Id, "order");
        platform.SelectRankedFaction(second.Id, "chaos");
        var beforeFirst = platform.RankedProfile(first.Id);
        var beforeSecond = platform.RankedProfile(second.Id);
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Connect(firstSession, first.Id, first.Username);
        manager.Connect(secondSession, second.Id, second.Username);
        await manager.JoinMatchmakingAsync(firstSession, "ranked", null);
        await manager.JoinMatchmakingAsync(secondSession, "ranked", null);
        var state = (await manager.RecoveryStateWithAckAsync(firstSession))
            .Where(message => message.SessionId == firstSession).Select(MessageJson)
            .Single(payload => payload.GetProperty("type").GetString() == "gameState");
        var matchId = state.GetProperty("state").GetProperty("matchId").GetString()!;
        const string requestId = "ranked-room-draw-request";
        await manager.RequestMatchDrawAsync(firstSession, requestId, "排位准备界面无法继续");

        var accepted = await manager.ResolveMatchDrawAsync(secondSession, requestId, true);
        var result = MessageJson(accepted.Single(message => message.SessionId == secondSession
            && MessageJson(message).GetProperty("type").GetString() == "matchGovernanceResult"));
        Assert.Equal("accepted", result.GetProperty("status").GetString());
        var final = MessageJson(accepted.First(message =>
            MessageJson(message).GetProperty("type").GetString() == "gameState")).GetProperty("state");
        Assert.Equal("GameOver", final.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, final.GetProperty("winner").ValueKind);
        Assert.Empty(await recorder.ListPendingRankedSettlementsAsync());
        var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
        Assert.Single(detail.Commands, command =>
            command.Command.GetProperty("type").GetString() == "authorityConclusion");
        Assert.Equal("draw", platform.RankedSettlement(matchId, first.Id)!.Outcome);
        Assert.Equal("draw", platform.RankedSettlement(matchId, second.Id)!.Outcome);
        Assert.Equal((beforeFirst.SevenValue, beforeFirst.PlacementPlayed, beforeFirst.Wins, beforeFirst.Losses),
            (platform.RankedProfile(first.Id).SevenValue, platform.RankedProfile(first.Id).PlacementPlayed,
                platform.RankedProfile(first.Id).Wins, platform.RankedProfile(first.Id).Losses));
        Assert.Equal((beforeSecond.SevenValue, beforeSecond.PlacementPlayed, beforeSecond.Wins, beforeSecond.Losses),
            (platform.RankedProfile(second.Id).SevenValue, platform.RankedProfile(second.Id).PlacementPlayed,
                platform.RankedProfile(second.Id).Wins, platform.RankedProfile(second.Id).Losses));
        var admin = platform.Login("Admin", "L12master").Account!;
        Assert.Equal(L12GameEngine.AgreedDrawConclusionKind,
            Assert.Single(platform.RankedIntegrityAudits(admin, matchId: matchId)).ConclusionKind);

        var replay = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => manager.ResolveMatchDrawAsync(secondSession, requestId, true)));
        Assert.All(replay, messages => Assert.Contains(messages, message =>
            MessageJson(message).TryGetProperty("status", out var replayStatus)
            && replayStatus.GetString() == "accepted"));
        Assert.Single((await recorder.GetMatchAsync(matchId))!.Commands, command =>
            command.Command.GetProperty("type").GetString() == "authorityConclusion");
    }

    [Fact]
    public async Task GovernanceAdminHttpRoutesEnforceRbacAndPersistUpdates()
    {
        var directory = TempDirectory("http-rbac");
        var catalog = Catalog;
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var reporter = platform.Register("u6c01d28ffd", "Password123!").Account!;
        var reported = platform.Register("ue33770f6c7", "Password123!").Account!;
        platform.CreatePlayerMatchReport("http-report-request", "http-governance-match", "ROOM04",
            "casual", reporter, reported, "需要管理员核查的对局行为", DateTimeOffset.UtcNow);
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var playerLogin = platform.Login(reporter.Username, "Password123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", playerLogin.Token);
            using (var forbidden = await client.GetAsync("/api/admin/match-governance/player-reports"))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var adminLogin = platform.Login("Admin", "L12master");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.Token);
            var rows = await client.GetFromJsonAsync<L12PlayerMatchReportView[]>(
                "/api/admin/match-governance/player-reports?search=http-governance-match");
            var row = Assert.Single(rows!);
            using var updated = await client.PatchAsJsonAsync(
                $"/api/admin/match-governance/player-reports/{row.Id}",
                new L12MatchGovernanceUpdate("resolved", "已核查", "证据完成复核"));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var body = await updated.Content.ReadFromJsonAsync<L12PlayerMatchReportView>();
            Assert.Equal("resolved", body!.Status);
            Assert.Contains(body.History, item => item.Comment == "证据完成复核");
        }
        finally { await server.StopAsync(); }
    }

    private static JsonElement MessageJson(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WebJson);

    private static string TempDirectory(string suffix)
        => Path.Combine(Path.GetTempPath(), $"l12-match-governance-{suffix}", Guid.NewGuid().ToString("N"));
}
