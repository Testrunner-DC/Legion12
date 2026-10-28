using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentKeyNoticeTests
{
    [Fact]
    public async Task AnnouncementNotifiesOnlyParticipantsAfterCommitAndNeverOnPreviewReplayOrNoOp()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var participant = f.Store.Register("knpart", "password-123");
        var outsider = f.Store.Register("knoutside", "password-123");
        Assert.True(participant.Success, participant.Message);
        Assert.True(outsider.Success, outsider.Message);
        var context = new L12AdminAuditContext("notice-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var tournament = f.Store.CreateTournament(host,
            new L12TournamentCreatePayload("提示赛事", "single", "code", 16,
                DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5), context, true);
        tournament = f.Store.RegisterTournament(participant.Account!, tournament.Id, new(), tournament.Version, context, true);
        using var hostSocket = await Connect(f.Client.BaseAddress!, f.PlayerToken);
        using var playerSocket = await Connect(f.Client.BaseAddress!, participant.Token!);
        using var outsiderSocket = await Connect(f.Client.BaseAddress!, outsider.Token!);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        var path = $"/api/tournaments/{tournament.Id}/announcement";
        const string secret = "PRIVATE-CONTACT-123";
        var command = new TournamentAnnouncementRequest(secret, "发布公告", "notice-commit", tournament.Version);
        using var first = await client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var hostNotice = await ReceiveUntil(hostSocket, "tournamentKeyNotice", TimeSpan.FromSeconds(5));
        var playerNotice = await ReceiveUntil(playerSocket, "tournamentKeyNotice", TimeSpan.FromSeconds(5));
        Assert.Equal(hostNotice.GetProperty("eventId").GetString(), playerNotice.GetProperty("eventId").GetString());
        Assert.Equal("announcement", hostNotice.GetProperty("kind").GetString());
        Assert.DoesNotContain(secret, hostNotice.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, playerNotice.ToString(), StringComparison.Ordinal);
        using var replay = await client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var saved = f.Store.Tournament(host, tournament.Id)!;
        using var preview = await client.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest("预览", "预览", "notice-preview", saved.Version, true));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var noChange = await client.PostAsJsonAsync(path,
            new TournamentAnnouncementRequest(secret, "相同公告", "notice-no-change", saved.Version));
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode);
        await AssertNoNotice(hostSocket);
        await AssertNoNotice(playerSocket);
        await AssertNoNotice(outsiderSocket);
    }

    [Fact]
    public async Task RescheduleAndCancelNotifyRelatedPlayersWithoutSharingReason()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var player = f.Store.Register("knsecond", "password-123");
        Assert.True(player.Success, player.Message);
        var context = new L12AdminAuditContext("notice-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var originalStart = DateTimeOffset.UtcNow.AddHours(2);
        var tournament = f.Store.CreateTournament(host,
            new L12TournamentCreatePayload("赛程提示", "single", "public", 16,
                originalStart, "现行规则", "", "after", "season", "", 50, 5), context, true);
        tournament = f.Store.RegisterTournament(player.Account!, tournament.Id, new(), tournament.Version, context, true);
        using var playerSocket = await Connect(f.Client.BaseAddress!, player.Token!);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        using var unchanged = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/postpone",
            new TournamentPostponeRequest(originalStart, "无变更", "same-time", tournament.Version));
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        tournament = f.Store.Tournament(host, tournament.Id)!;
        using var changed = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/postpone",
            new TournamentPostponeRequest(originalStart.AddHours(1), "PRIVATE-REASON-123", "new-time", tournament.Version));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var changedNotice = await ReceiveUntil(playerSocket, "tournamentKeyNotice", TimeSpan.FromSeconds(5));
        Assert.Equal("postpone", changedNotice.GetProperty("kind").GetString());
        Assert.DoesNotContain("PRIVATE-REASON-123", changedNotice.ToString(), StringComparison.Ordinal);
        tournament = f.Store.Tournament(host, tournament.Id)!;
        using var cancelled = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/cancel",
            new TournamentCancelRequest("PRIVATE-REASON-456", "cancel-notice", tournament.Version));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var cancelNotice = await ReceiveUntil(playerSocket, "tournamentKeyNotice", TimeSpan.FromSeconds(5));
        Assert.Equal("cancel", cancelNotice.GetProperty("kind").GetString());
        Assert.DoesNotContain("PRIVATE-REASON-456", cancelNotice.ToString(), StringComparison.Ordinal);
        await AssertNoNotice(playerSocket);
    }
    [Theory]
    [InlineData("managed")]
    [InlineData("automated")]
    public async Task FirstPairingProducesOneInternalEventInBothModes(string mode)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var admin = f.Store.Login("Admin", "L12master").Account!;
        var hostPlayer = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var player = f.Store.Register("knpair", "password-123");
        var referee = f.Store.Register("knjudge", "password-123");
        var extra1 = f.Store.Register("knextra1", "password-123").Account!;
        var extra2 = f.Store.Register("knextra2", "password-123").Account!;
        var dropped = f.Store.Register("kndropped", "password-123").Account!;
        var removed = f.Store.Register("knremoved", "password-123").Account!;
        Assert.True(player.Success, player.Message);
        Assert.True(referee.Success, referee.Message);
        Assert.True(f.Store.SendFriendRequest(admin.Id, referee.Account!.Id).Success);
        Assert.True(f.Store.ResolveFriendRequest(referee.Account.Id, admin.Id, true).Success);
        var context = new L12AdminAuditContext("notice-pairing", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var startAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var tournament = f.Store.CreateTournament(admin,
            new L12TournamentCreatePayload("对阵提示", "single", "public", 8, startAt,
                "现行规则", "", "after", "none", "", 50, 5, [referee.Account.Id],
                DisasterCardIds: [], CardRestrictions: [], OperationMode: mode), context, true);
        foreach (var actor in new[] { hostPlayer, player.Account!, extra1, extra2 })
        {
            tournament = f.Store.RegisterTournament(actor, tournament.Id, new(), tournament.Version, context, true);
            var deck = f.Store.Decks(actor.Id)[0];
            tournament = f.Store.PreCheckInTournament(actor, tournament.Id,
                new L12TournamentPreCheckInPayload(deck.Name, "", deck.Id), tournament.Version, context, true);
        }
        tournament = f.Store.RegisterTournament(dropped, tournament.Id, new(), tournament.Version, context, true);
        tournament = f.Store.DropTournament(dropped, tournament.Id, tournament.Version, context, true);
        tournament = f.Store.RegisterTournament(removed, tournament.Id, new(), tournament.Version, context, true);
        tournament = f.Store.RemoveTournamentParticipant(admin, tournament.Id, new L12TournamentRemoveParticipantPayload(removed.Id, false, "移出"), tournament.Version, context, true);
        L12TournamentKeyNotice notice;
        if (mode == "managed")
        {
            var started = f.Store.StartTournament(admin, tournament.Id, tournament.Version, context, true);
            notice = Assert.IsType<L12TournamentKeyNotice>(started.KeyNotice);
            Assert.DoesNotContain("recipientAccountIds", JsonSerializer.Serialize(started), StringComparison.OrdinalIgnoreCase);
            foreach (var match in started.Rounds[0].Matches)
                started = f.Store.ApplyTournamentRuling(referee.Account!, started.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "test"), started.Version, context, true);
            var next = f.Store.CreateNextRound(admin, started.Id, started.Version, context, true);
            var nextNotice = Assert.IsType<L12TournamentKeyNotice>(next.KeyNotice);
            Assert.Equal("pairings", nextNotice.Kind);
            Assert.NotEqual(notice.EventId, nextNotice.EventId);
            Assert.DoesNotContain(dropped.Id, nextNotice.RecipientAccountIds);
            Assert.DoesNotContain(removed.Id, nextNotice.RecipientAccountIds);
        }
        else
        {
            var checkin = f.Store.AdvanceAutomaticTournaments(startAt.AddMinutes(-5));
            Assert.Empty(checkin.KeyNotices!);
            var started = f.Store.AdvanceAutomaticTournaments(startAt);
            notice = Assert.Single(started.KeyNotices!);
            Assert.Empty(f.Store.AdvanceAutomaticTournaments(startAt).KeyNotices!);
            Assert.Empty(f.Store.AdvanceAutomaticTournaments(startAt.AddMinutes(3)).KeyNotices!);
        }
        Assert.Equal("pairings", notice.Kind);
        Assert.Equal(new[] { admin.Id, hostPlayer.Id, player.Account!.Id, extra1.Id, extra2.Id, referee.Account!.Id }.OrderBy(id => id),
            notice.RecipientAccountIds.OrderBy(id => id));
    }
    [Fact]
    public async Task DisconnectedRecipientDoesNotPreventCommittedAnnouncement()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var player = f.Store.Register("knoffline", "password-123");
        Assert.True(player.Success, player.Message);
        var context = new L12AdminAuditContext("notice-offline", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var tournament = f.Store.CreateTournament(host,
            new L12TournamentCreatePayload("离线提示", "single", "public", 8, DateTimeOffset.UtcNow.AddHours(1),
                "现行规则", "", "after", "season", "", 50, 5), context, true);
        tournament = f.Store.RegisterTournament(player.Account!, tournament.Id, new(), tournament.Version, context, true);
        using var disconnected = await Connect(f.Client.BaseAddress!, player.Token!);
        disconnected.Abort();
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        using var response = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/announcement",
            new TournamentAnnouncementRequest("已保存公告", "发布", "offline-commit", tournament.Version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("已保存公告", f.Store.Tournament(host, tournament.Id)!.Announcement);
    }
    [Fact]
    public async Task FailedCommitDoesNotSendNoticeOrPersistAnnouncement()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var context = new L12AdminAuditContext("notice-fail", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var tournament = f.Store.CreateTournament(host,
            new L12TournamentCreatePayload("失败提示", "single", "public", 8, DateTimeOffset.UtcNow.AddHours(1),
                "现行规则", "", "after", "season", "", 50, 5), context, true);
        using var socket = await Connect(f.Client.BaseAddress!, f.PlayerToken);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        var hit = false;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-tournament-read-index-build") return;
            hit = true;
            f.Store.StorageFailureInjector = null;
            throw new IOException("synthetic tournament save failure");
        };
        using var response = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/announcement",
            new TournamentAnnouncementRequest("不应保存", "失败注入", "failed-commit", tournament.Version));
        f.Store.StorageFailureInjector = null;
        Assert.True(hit);
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("", f.Store.Tournament(host, tournament.Id)!.Announcement);
        await AssertNoNotice(socket);
    }
    private static async Task<ClientWebSocket> Connect(Uri address, string token)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new UriBuilder(address) { Scheme = "ws", Path = "/ws" }.Uri, CancellationToken.None);
        var hello = JsonSerializer.SerializeToUtf8Bytes(new { type = "hello", authToken = token });
        await socket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await ReceiveUntil(socket, "session", TimeSpan.FromSeconds(5));
        return socket;
    }

    private static async Task<JsonElement> ReceiveUntil(ClientWebSocket socket, string type, TimeSpan limit)
    {
        using var cancellation = new CancellationTokenSource(limit);
        var buffer = new byte[32768];
        while (true)
        {
            using var bytes = new MemoryStream();
            WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(buffer, cancellation.Token);
                if (frame.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Socket closed");
                bytes.Write(buffer, 0, frame.Count);
            } while (!frame.EndOfMessage);
            using var json = JsonDocument.Parse(bytes.ToArray());
            if (json.RootElement.TryGetProperty("type", out var actual) && actual.GetString() == type)
                return json.RootElement.Clone();
        }
    }

    private static async Task AssertNoNotice(ClientWebSocket socket)
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReceiveUntil(socket, "tournamentKeyNotice", TimeSpan.FromMilliseconds(500)));
    }
}