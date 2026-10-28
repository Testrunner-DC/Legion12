using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentReputationPrivateReadHttpTests
{
    [Fact]
    public async Task RevokePreviewFindsOriginalTournamentAndDoesNotWriteAnyState()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "revoke-preview-original", "no-show", at, at);
        var version = f.Store.Version; var mirror = File.ReadAllText(f.MirrorPath);
        using var response = await Post(f, RevokePath(player.Id, "revoke-preview-original") + "/preview",
            f.AdminToken, new { reason = "核实平台故障，撤销原处置", expectedReputationRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await Json(response);
        Assert.Equal(t.Id, preview.GetProperty("tournamentId").GetString());
        Assert.Equal(t.Version, preview.GetProperty("tournamentVersion").GetInt64());
        Assert.Equal(version, preview.GetProperty("platformVersion").GetInt64());
        Assert.Equal(1, preview.GetProperty("reputationRevision").GetInt64());
        Assert.Equal(80, preview.GetProperty("before").GetProperty("score").GetInt32());
        Assert.Equal(100, preview.GetProperty("after").GetProperty("score").GetInt32());
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
        Assert.Equal(1, f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken).Total);
    }

    [Fact]
    public async Task RevokeApplyAppendsOnceAndReplaysCommittedCommandInsteadOfRefundingTwice()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "revoke-apply-original", "no-show", at, at);
        var version = f.Store.Version;
        Assert.NotEqual(player.PermissionVersion, version); // Actual platform-version contract, never the account permission revision.
        var command = new { reason = "核实平台故障，撤销原处置", expectedVersion = version,
            expectedTournamentVersion = t.Version, expectedReputationRevision = 1, idempotencyKey = "reputation-revoke-apply-one" };
        var path = RevokePath(player.Id, "revoke-apply-original");
        using var applied = await Post(f, path, f.AdminToken, command);
        if (applied.StatusCode != HttpStatusCode.OK) Assert.Fail(await applied.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var page = f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken);
        Assert.Equal(100, page.Score); Assert.Equal(2, page.Revision); Assert.Equal(2, page.Total);
        Assert.Single(page.Items, item => item.Kind == "revoke" && item.RevokesEventId == "revoke-apply-original");
        Assert.Contains(page.Items, item => item.Id == "revoke-apply-original" && item.Assessment == "revoked");
        Assert.Equal(version + 1, f.Store.Version);
        using var replay = await Post(f, path, f.AdminToken, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("X-Idempotent-Replay").Single());
        Assert.Equal(version + 1, f.Store.Version);
        using var anotherKey = await Post(f, path, f.AdminToken, new { command.reason,
            expectedVersion = f.Store.Version, command.expectedTournamentVersion,
            expectedReputationRevision = 2, idempotencyKey = "reputation-revoke-apply-two" });
        Assert.Equal(HttpStatusCode.OK, anotherKey.StatusCode);
        Assert.Equal(2, f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken).Total);
    }

    [Fact]
    public async Task RevokeEndpointIsGlobalAdminOnlyNotOrdinaryTournamentOrganizer()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "revoke-auth-original", "no-show", at, at);
        var version = f.Store.Version;
        using var denied = await Post(f, RevokePath(player.Id, "revoke-auth-original") + "/preview",
            f.PlayerToken, new { reason = "本人不能自行撤销", expectedReputationRevision = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(80, f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken).Score);
        Assert.Equal(1, f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken).Total);
        Assert.True(f.Store.Version >= version); // Existing authorization-denied audit may commit, never reputation.
    }

    private static string RevokePath(string accountId, string eventId)
        => $"/api/admin/accounts/{Uri.EscapeDataString(accountId)}/tournament-reputation/{Uri.EscapeDataString(eventId)}/revoke";

    [Fact]
    public async Task RevokeCappedZeroDeltaFactStillRecomputesTheRecoveryAnchor()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "cap-first", "no-show", at, at);
        var secondAt = DateTimeOffset.UtcNow;
        Record(f, t, player, admin, "cap-second", "no-show", secondAt, secondAt);
        var capped = f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken);
        Assert.Equal(0, capped.Items.Single(item => item.Id == "cap-second").EffectiveScoreChange);
        using var response = await Post(f, RevokePath(player.Id, "cap-second") + "/preview", f.AdminToken,
            new { reason = "核实第二个事实不成立", expectedReputationRevision = 2 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await Json(response);
        Assert.Equal(80, preview.GetProperty("before").GetProperty("score").GetInt32());
        Assert.Equal(80, preview.GetProperty("after").GetProperty("score").GetInt32());
        Assert.Equal(secondAt, preview.GetProperty("before").GetProperty("lastViolationAt").GetDateTimeOffset());
        Assert.Equal(at, preview.GetProperty("after").GetProperty("lastViolationAt").GetDateTimeOffset());
        Assert.Equal(at.AddDays(7), preview.GetProperty("after").GetProperty("nextRecoveryAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("tournament")]
    [InlineData("reputation")]
    public async Task RevokeAllThreeVersionPreconditionsRejectWithoutChangingReputation(string stale)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "version-original", "no-show", at, at);
        using var response = await Post(f, RevokePath(player.Id, "version-original"), f.AdminToken,
            new { reason = "版本对抗", expectedVersion = f.Store.Version - (stale == "platform" ? 1 : 0),
                expectedTournamentVersion = t.Version + (stale == "tournament" ? 1 : 0),
                expectedReputationRevision = stale == "reputation" ? 0 : 1, idempotencyKey = "revoke-stale-" + stale });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var page = f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken);
        Assert.Equal(80, page.Score); Assert.Equal(1, page.Revision); Assert.Equal(1, page.Total);
    }

    [Theory]
    [InlineData("before-commit")]
    [InlineData("before-audit-append")]
    public async Task RevokeSaveFailureRollsBackFactProjectionCommandAndAudit(string stage)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "failed-revoke-original", "no-show", at, at);
        var version = f.Store.Version; var mirror = File.ReadAllText(f.MirrorPath);
        var commands = f.Store.AdminCommands().Count;
        f.Store.StorageFailureInjector = actual => { if (actual == stage) throw new IOException("synthetic revoke rollback"); };
        try
        {
            using var response = await Post(f, RevokePath(player.Id, "failed-revoke-original"), f.AdminToken,
                new { reason = "失败回滚", expectedVersion = version, expectedTournamentVersion = t.Version,
                    expectedReputationRevision = 1, idempotencyKey = "revoke-save-failure" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally { f.Store.StorageFailureInjector = null; }
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
        Assert.Equal(commands, f.Store.AdminCommands().Count);
        var page = f.Store.ReadOwnTournamentReputation("Bearer " + f.PlayerToken);
        Assert.Equal(80, page.Score); Assert.Equal(1, page.Total); Assert.Equal(1, page.Revision);
        Assert.Equal(1, new L12PlatformStore(f.MirrorPath).ReadOwnTournamentReputation("Bearer " + f.PlayerToken).Total);
    }

    [Fact]
    public async Task RevokeStorageCasFailurePreservesTheOtherCommittedWriterWithoutRevoke()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "cas-revoke-original", "no-show", at, at);
        var version = f.Store.Version;
        var writer = new L12PlatformStore(f.MirrorPath);
        var committedWriter = writer.Register("rpvcas", "password-123");
        Assert.True(committedWriter.Success, committedWriter.Message);
        var committed = File.ReadAllText(f.MirrorPath);
        using var response = await Post(f, RevokePath(player.Id, "cas-revoke-original"), f.AdminToken,
            new { reason = "CAS失败关闭", expectedVersion = version, expectedTournamentVersion = t.Version,
                expectedReputationRevision = 1, idempotencyKey = "revoke-cas-failure" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(committed, File.ReadAllText(f.MirrorPath));
        var page = new L12PlatformStore(f.MirrorPath).ReadOwnTournamentReputation("Bearer " + f.PlayerToken);
        Assert.Equal(80, page.Score); Assert.Equal(1, page.Total); Assert.Equal(1, page.Revision);
    }

    [Fact]
    public async Task RevokeRequiresReasonAndOriginalAccountAndRejectsFreeFactFields()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "bound-original", "no-show", at, at);
        var version = f.Store.Version; var mirror = File.ReadAllText(f.MirrorPath);
        using var blank = await Post(f, RevokePath(player.Id, "bound-original") + "/preview", f.AdminToken,
            new { reason = " ", expectedReputationRevision = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using var unknown = await Post(f, RevokePath(player.Id, "not-an-event") + "/preview", f.AdminToken,
            new { reason = "原事实必须存在", expectedReputationRevision = 1 });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var free = await Post(f, RevokePath(player.Id, "bound-original") + "/preview", f.AdminToken,
            new { reason = "不能自由给分", expectedReputationRevision = 1, kind = "revoke", score = 100, effectiveAt = at });
        Assert.Equal(HttpStatusCode.BadRequest, free.StatusCode);
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
    }

    [Fact]
    public async Task RevokeOwnAccountAndStaleSessionAreDeniedByTheStoreUnderItsLock()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f, includeAdmin: true);
        at = DateTimeOffset.UtcNow;
        Record(f, t, admin, admin, "own-admin-original", "no-show", at, at);
        using var own = await Post(f, RevokePath(admin.Id, "own-admin-original") + "/preview", f.AdminToken,
            new { reason = "本人需其他管理员处理", expectedReputationRevision = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Throws<UnauthorizedAccessException>(() => f.Store.PreviewAccountTournamentReputationRevoke(
            "Bearer " + f.RevokedToken, admin.Id, "own-admin-original", "会话已撤销", 1));
        var staff = f.Store.Register("revokestaff", "password-123").Account!;
        Assert.True(f.Store.SetRole(staff.Id, "admin"));
        var token = f.Store.Login("revokestaff", "password-123").Token!;
        Assert.True(f.Store.SetRole(staff.Id, "player"));
        Assert.Throws<L12TournamentScopeException>(() => f.Store.PreviewAccountTournamentReputationRevoke(
            "Bearer " + token, admin.Id, "own-admin-original", "权限已移除", 1));
    }

    [Fact]
    public void RevokeOriginalGraceFactPreservesTheExistingIndependentMatchStaffRule()
    {
        using var f = new ReputationProductionFixture();
        var t = f.Create(lateGrace: 0);
        foreach (var player in f.Players.Take(2)) t = f.Lock(f.Register(t, player), player);
        t = f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true);
        var match = Assert.Single(Assert.Single(t.Rounds).Matches);
        var present = f.Players.Single(item => item.Id == match.PlayerAAccountId);
        var absent = f.Players.Single(item => item.Id == match.PlayerBAccountId);
        t = f.Store.CheckInTournament(present, t.Id, 1, new(null, true), t.Version, f.Audit, true);
        t = f.Store.StartTournamentRound(f.Host, t.Id, 1, t.Version, f.Audit, true);
        t = f.Store.ApplyTournamentRuling(f.Host, t.Id, match.Id,
            new("no-show", absent.Id, "no-show-b", "synthetic authoritative absence"), t.Version, f.Audit, true);
        var originalResult = t.Rounds.Single().Matches.Single().Result;
        var absentToken = f.Store.Login(absent.Username, "password-123").Token!;
        var original = Assert.Single(f.Store.ReadOwnTournamentReputation("Bearer " + absentToken).Items);
        Assert.True(f.Store.SetRole(present.Id, "admin"));
        var participantToken = f.Store.Login(present.Username, "password-123").Token!;
        Assert.Throws<L12TournamentScopeException>(() => f.Store.PreviewAccountTournamentReputationRevoke(
            "Bearer " + participantToken, absent.Id, original.Id, "不能裁定自己的桌次", 1));
        var adminToken = f.Store.Login(f.Admin.Username, "password-123").Token!;
        var preview = f.Store.PreviewAccountTournamentReputationRevoke("Bearer " + adminToken,
            absent.Id, original.Id, "独立管理员核实故障", 1);
        Assert.Equal(80, preview.Before.Score); Assert.Equal(100, preview.After.Score);
        Assert.Equal(80, f.Score(absent));
        Assert.Equal(originalResult, f.Store.Tournament(f.Host, t.Id)!.Rounds.Single().Matches.Single().Result);
    }

    private static async Task<HttpResponseMessage> Post(AdminWorkbenchTimingTests.Fixture f,
        string path, string? token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await f.Client.SendAsync(request);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("revoked")]
    public async Task PrivateSelfReadRequiresTheExistingEffectiveSession(string identity)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var response = await Get(f, "/api/me/tournament-reputation", identity == "revoked" ? f.RevokedToken : null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await Json(response);
        Assert.False(body.TryGetProperty("score", out _));
    }

    [Fact]
    public async Task SelfIsSessionBoundAndGlobalAdminReadIsSeparateFromOrganizerAuthority()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "owned-missing", "no-show", at, at);
        using var own = await Get(f, $"/api/me/tournament-reputation?accountId={admin.Id}", f.PlayerToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var self = await Json(own);
        Assert.Equal(80, self.GetProperty("score").GetInt32());
        Assert.True(own.Headers.CacheControl?.NoStore == true);
        using var denied = await Get(f, $"/api/admin/accounts/{admin.Id}/tournament-reputation", f.PlayerToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False((await Json(denied)).TryGetProperty("score", out _));
        using var allowed = await Get(f, $"/api/admin/accounts/{player.Id}/tournament-reputation", f.AdminToken);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(80, (await Json(allowed)).GetProperty("score").GetInt32());
        var raw = self.GetRawText();
        foreach (var forbidden in new[] { "source", "actorId", "correlationId", "formalSeat", "checkInStartedAt", "token", "deck" })
            Assert.DoesNotContain($"\"{forbidden}\"", raw, StringComparison.OrdinalIgnoreCase);
        using var account = await Get(f, "/api/auth/me", f.PlayerToken);
        Assert.DoesNotContain("tournamentReputation", (await Json(account)).GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulReadsArePureAndInitialHundredIsAuthoritativeOnlyOnSuccess()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.MirrorPath);
        using var own = await Get(f, "/api/me/tournament-reputation", f.PlayerToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var page = await Json(own);
        Assert.Equal(100, page.GetProperty("score").GetInt32());
        Assert.Equal(0, page.GetProperty("revision").GetInt64());
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
        using var invalid = await Get(f, "/api/me/tournament-reputation?page=0", f.PlayerToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False((await Json(invalid)).TryGetProperty("score", out _));
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
    }

    [Fact]
    public async Task PagingIsBoundedStableForTimestampTiesAndRejectsMixedRevisions()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        for (var index = 1; index <= 52; index++)
            Record(f, t, player, admin, $"page-{index:D3}", "no-show", at, at);
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.MirrorPath);
        using var firstResponse = await Get(f, "/api/me/tournament-reputation", f.PlayerToken);
        var first = await Json(firstResponse);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(20, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(52, first.GetProperty("total").GetInt32());
        Assert.Equal(52, first.GetProperty("revision").GetInt64());
        Assert.Equal(Enumerable.Range(33, 20).Reverse(), first.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt32()));
        using var secondResponse = await Get(f, "/api/me/tournament-reputation?page=2&expectedRevision=52", f.PlayerToken);
        var second = await Json(secondResponse);
        Assert.Equal(Enumerable.Range(13, 20).Reverse(), second.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("sequence").GetInt32()));
        using var boundedResponse = await Get(f, "/api/me/tournament-reputation?pageSize=1000", f.PlayerToken);
        var bounded = await Json(boundedResponse);
        Assert.Equal(50, bounded.GetProperty("pageSize").GetInt32());
        Assert.Equal(50, bounded.GetProperty("items").GetArrayLength());
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
        var now = DateTimeOffset.UtcNow;
        Record(f, t, player, admin, "page-new", "no-show", now, now);
        using var stale = await Get(f, "/api/me/tournament-reputation?page=2&expectedRevision=52", f.PlayerToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False((await Json(stale)).TryGetProperty("items", out _));
    }

    [Fact]
    public async Task CappedAndRevokedHistoryDoesNotAdvertiseRepeatedDeductionsOrInventARefund()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var (t, player, admin, at) = Seed(f);
        Record(f, t, player, admin, "first-missing", "no-show", at, at);
        Record(f, t, player, admin, "second-missing", "no-show", at, at);
        using var response = await Get(f, "/api/me/tournament-reputation", f.PlayerToken);
        var initial = await Json(response);
        var capped = initial.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "second-missing");
        Assert.Equal("no-additional-deduction", capped.GetProperty("assessment").GetString());
        Assert.Equal(0, capped.GetProperty("effectiveScoreChange").GetInt32());
        var now = DateTimeOffset.UtcNow;
        Record(f, t, player, admin, "revoke-first", "revoke", now, now, "first-missing");
        using var changedResponse = await Get(f, "/api/me/tournament-reputation", f.PlayerToken);
        var changed = await Json(changedResponse);
        Assert.Equal(80, changed.GetProperty("score").GetInt32()); // The other valid fact still reaches the cap.
        var corrected = changed.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "revoke-first");
        Assert.Equal("correction", corrected.GetProperty("assessment").GetString());
        Assert.Equal(JsonValueKind.Null, corrected.GetProperty("effectiveScoreChange").ValueKind);
        var original = changed.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "first-missing");
        Assert.Equal("revoked", original.GetProperty("assessment").GetString());
        Assert.Equal(0, original.GetProperty("effectiveScoreChange").GetInt32());
        Assert.Equal(-20, changed.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "second-missing")
            .GetProperty("effectiveScoreChange").GetInt32());
    }

    [Fact]
    public async Task StoreRechecksCurrentRoleAndRevokedTokenUnderItsOwnLock()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var registration = f.Store.Register("readstaff", "password-123");
        Assert.True(registration.Success, registration.Message);
        var staff = registration.Account!;
        Assert.True(f.Store.SetRole(staff.Id, "admin"));
        var token = f.Store.Login("readstaff", "password-123").Token!;
        var player = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        Assert.Equal(100, f.Store.ReadAccountTournamentReputation("Bearer " + token, player.Id).Score);
        Assert.True(f.Store.SetRole(staff.Id, "player"));
        var error = Xunit.Record.Exception(() => f.Store.ReadAccountTournamentReputation("Bearer " + token, player.Id));
        Assert.True(error is UnauthorizedAccessException or L12TournamentScopeException,
            $"Expected current-session/global-permission denial, got {error?.GetType().Name ?? "success"}");
        Assert.Throws<UnauthorizedAccessException>(() => f.Store.ReadOwnTournamentReputation("Bearer " + f.RevokedToken));
    }

    [Fact]
    public async Task ThereIsNoFreeFactSubmissionApi()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/tournament-reputation")
        { Content = JsonContent.Create(new { kind = "no-show", penalty = 100, effectiveAt = "2026-01-01" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        using var actual = await Get(f, "/api/me/tournament-reputation", f.PlayerToken);
        Assert.Equal(100, (await Json(actual)).GetProperty("score").GetInt32());
    }

    private static (L12TournamentView Tournament, L12AccountView Player, L12AccountView Admin, DateTimeOffset At)
        Seed(AdminWorkbenchTimingTests.Fixture f, bool includeAdmin = false)
    {
        var player = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        var admin = f.Store.Authenticate("Bearer " + f.AdminToken)!;
        var audit = new L12AdminAuditContext("private-read-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/private-read");
        var t = f.Store.CreateTournament(player, new("私有读取合成", "single", "public", 2,
            DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "none", "", 50, 5), audit, true);
        t = f.Store.RegisterTournament(player, t.Id, new(), t.Version, audit, true);
        if (includeAdmin) t = f.Store.RegisterTournament(admin, t.Id, new(), t.Version, audit, true);
        t = f.Store.SetTournamentPhase(player, t.Id, new("pre-check-in", "synthetic opening"), t.Version, audit, true);
        return (t, player, admin, DateTimeOffset.UtcNow);
    }

    private static void Record(AdminWorkbenchTimingTests.Fixture f, L12TournamentView t, L12AccountView player,
        L12AccountView admin, string id, string kind, DateTimeOffset effectiveAt, DateTimeOffset recordedAt, string? revokes = null)
    {
        var revision = f.Store.TournamentReputationProjection(player, player.Id, recordedAt).Revision;
        f.Store.RecordTournamentReputationFact(admin, new(id, t.Id, player.Id, kind, effectiveAt, revokes),
            t.Version, revision, new("private-read-test", "admin.accounts.status.write", RequestMethod: "TEST", RequestPath: "/test/private-read"),
            true, recordedAt);
    }

    private static async Task<HttpResponseMessage> Get(AdminWorkbenchTimingTests.Fixture f, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await f.Client.SendAsync(request);
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
