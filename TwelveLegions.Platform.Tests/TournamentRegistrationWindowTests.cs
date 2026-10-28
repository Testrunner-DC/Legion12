using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentRegistrationWindowTests
{
    [Fact]
    public void FutureOpeningIsEnforcedByTheServerAndSurvivesReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var host = store.Register("windowhost", "password-123").Account!;
            var payload = new L12TournamentCreatePayload("窗口赛事", "single", "public", 16,
                DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5);
            var json = JsonSerializer.SerializeToNode(payload)!.AsObject();
            json["RegistrationOpensAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.UtcNow.AddHours(1));
            json["RegistrationClosesAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.UtcNow.AddHours(2));
            payload = json.Deserialize<L12TournamentCreatePayload>()!;
            var context = new L12AdminAuditContext("window-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
            var tournament = store.CreateTournament(host, payload, context, true);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.RegisterTournament(host, tournament.Id, new(), tournament.Version, context, true));
            var reloaded = new L12PlatformStore(path);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                reloaded.RegisterTournament(host, tournament.Id, new(), tournament.Version, context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void WindowEditingIsScopedPreviewableAndPersistedWithoutOverridingManualClosure()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var host = store.Register("winhost", "password-123").Account!;
            var other = store.Register("winother", "password-123").Account!;
            var context = new L12AdminAuditContext("window-test", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
            var payload = new L12TournamentCreatePayload("窗口赛事", "single", "public", 16,
                DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5);
            var tournament = store.CreateTournament(host, payload, context, true);
            Assert.True(tournament.RegistrationOpen);
            var oldPublic = store.PublicTournamentByCode(tournament.Code)!;
            Assert.Null(oldPublic.RegistrationOpensAt);
            Assert.Null(oldPublic.RegistrationClosesAt);
            var hidden = store.CreateTournament(host, payload with { Visibility = "code",
                RegistrationOpensAt = DateTimeOffset.UtcNow.AddHours(-2),
                RegistrationClosesAt = DateTimeOffset.UtcNow.AddHours(-1) }, context, true);
            Assert.Null(store.PublicTournamentByCode(hidden.Code));
            var opened = DateTimeOffset.UtcNow.AddHours(-2);
            var expired = DateTimeOffset.UtcNow.AddHours(-1);
            Assert.Throws<L12TournamentScopeException>(() =>
                store.SetTournamentRegistrationWindow(other, tournament.Id, opened, expired, tournament.Version, context, true));
            var preview = store.SetTournamentRegistrationWindow(host, tournament.Id, opened, expired, tournament.Version, context, false);
            Assert.False(preview.RegistrationOpen);
            Assert.True(store.Tournament(host, tournament.Id)!.RegistrationOpen);
            tournament = store.SetTournamentRegistrationWindow(host, tournament.Id, opened, expired, tournament.Version, context, true);
            Assert.False(tournament.RegistrationOpen);
            Assert.False(store.PublicTournamentByCode(tournament.Code)!.RegistrationOpen);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.RegisterTournament(other, tournament.Id, new(), tournament.Version, context, true));
            var restored = new L12PlatformStore(path);
            tournament = restored.Tournament(host, tournament.Id)!;
            Assert.Equal(opened, tournament.RegistrationOpensAt);
            Assert.Equal(expired, tournament.RegistrationClosesAt);
            var publicView = restored.PublicTournamentByCode(tournament.Code)!;
            Assert.False(publicView.RegistrationOpen);
            Assert.Equal(opened, publicView.RegistrationOpensAt);
            Assert.Equal(expired, publicView.RegistrationClosesAt);
            tournament = restored.SetTournamentRegistrationWindow(host, tournament.Id, null, null, tournament.Version, context, true);
            Assert.True(tournament.RegistrationOpen);
            tournament = restored.SetTournamentPhase(host, tournament.Id, new("registration-closed", "测试"), tournament.Version, context, true);
            tournament = restored.SetTournamentRegistrationWindow(host, tournament.Id, opened, DateTimeOffset.UtcNow.AddHours(1), tournament.Version, context, true);
            Assert.False(tournament.RegistrationOpen);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                restored.RegisterTournament(other, tournament.Id, new(), tournament.Version, context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidWindowIsRejectedBeforeCreatingOrChangingAnEvent()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var host = store.Register("wininvalid", "password-123").Account!;
            var context = new L12AdminAuditContext("window-invalid", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
            var when = DateTimeOffset.UtcNow.AddHours(1);
            var payload = new L12TournamentCreatePayload("窗口赛事", "single", "public", 16,
                when.AddHours(1), "现行规则", "", "after", "season", "", 50, 5,
                RegistrationOpensAt: when, RegistrationClosesAt: when);
            Assert.Throws<ArgumentException>(() => store.CreateTournament(host, payload, context, true));
            var valid = store.CreateTournament(host, payload with { RegistrationOpensAt = null, RegistrationClosesAt = null }, context, true);
            Assert.Throws<ArgumentException>(() => store.SetTournamentRegistrationWindow(host, valid.Id, when, when, valid.Version, context, true));
            Assert.True(store.Tournament(host, valid.Id)!.RegistrationOpen);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void OpenIsInclusiveAndCloseIsExclusive()
    {
        var method = typeof(L12PlatformStore).GetMethod("IsRegistrationWindowOpen",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var open = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var close = open.AddHours(1);
        bool Evaluate(DateTimeOffset now) => (bool)method.Invoke(null, [open, close, now])!;
        Assert.False(Evaluate(open.AddTicks(-1)));
        Assert.True(Evaluate(open));
        Assert.True(Evaluate(close.AddTicks(-1)));
        Assert.False(Evaluate(close));
    }
    [Fact]
    public async Task HttpWindowEditChecksPermissionVersionPreviewAndIdempotency()
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var host = fixture.Store.Login("Admin", "L12master").Account!;
        var context = new L12AdminAuditContext("window-http", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var payload = new L12TournamentCreatePayload("HTTP窗口赛事", "single", "public", 16,
            DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5);
        var tournament = fixture.Store.CreateTournament(host, payload, context, true);
        var path = $"/api/tournaments/{tournament.Id}/registration-window";
        var opens = DateTimeOffset.UtcNow.AddHours(1);
        var closes = opens.AddHours(1);
        async Task<HttpResponseMessage> Send(string token, TournamentRegistrationWindowRequest body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
            return await fixture.Client.SendAsync(request);
        }
        using var forbidden = await Send(fixture.PlayerToken, new(opens, closes, "测试", "unauthorized", tournament.Version));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var preview = await Send(fixture.AdminToken, new(opens, closes, "测试", "preview-window", tournament.Version, true));
        Assert.Equal(System.Net.HttpStatusCode.OK, preview.StatusCode);
        Assert.Null(fixture.Store.Tournament(host, tournament.Id)!.RegistrationOpensAt);
        var requestBody = new TournamentRegistrationWindowRequest(opens, closes, "测试", "commit-window", tournament.Version);
        using var applied = await Send(fixture.AdminToken, requestBody);
        Assert.Equal(System.Net.HttpStatusCode.OK, applied.StatusCode);
        var saved = fixture.Store.Tournament(host, tournament.Id)!;
        Assert.Equal(opens, saved.RegistrationOpensAt);
        using var repeated = await Send(fixture.AdminToken, requestBody);
        Assert.Equal(System.Net.HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(saved.Version, fixture.Store.Tournament(host, tournament.Id)!.Version);
        using var stale = await Send(fixture.AdminToken, new(null, null, "测试", "stale-window", tournament.Version));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(opens, fixture.Store.Tournament(host, tournament.Id)!.RegistrationOpensAt);
    }
    [Theory]
    [InlineData("managed")]
    [InlineData("automated")]
    public void ValidWindowAdmitsAndExpiredWindowRejectsInBothModes(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var host = store.Login("Admin", "L12master").Account!;
            var first = store.Register("winfirst", "password-123").Account!;
            var second = store.Register("winsecond", "password-123").Account!;
            var context = new L12AdminAuditContext("window-modes", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
            var payload = new L12TournamentCreatePayload("窗口赛事", "single", "public", 16,
                DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "season", "", 50, 5,
                OperationMode: mode, RegistrationOpensAt: DateTimeOffset.UtcNow.AddHours(-1),
                RegistrationClosesAt: DateTimeOffset.UtcNow.AddHours(1));
            var tournament = store.CreateTournament(host, payload, context, true);
            Assert.True(tournament.RegistrationOpen);
            tournament = store.RegisterTournament(first, tournament.Id, new(), tournament.Version, context, true);
            Assert.Contains(tournament.Participants, item => item.AccountId == first.Id);
            tournament = store.SetTournamentRegistrationWindow(host, tournament.Id, DateTimeOffset.UtcNow.AddHours(-2),
                DateTimeOffset.UtcNow.AddHours(-1), tournament.Version, context, true);
            Assert.False(tournament.RegistrationOpen);
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.RegisterTournament(second, tournament.Id, new(), tournament.Version, context, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OrdinaryOrganizerHttpCreationRetainsUtcWindowAndCanEditOnlyOwnEvent()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", f.PlayerToken);
        var opens = DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(8));
        var closes = opens.AddHours(1);
        var payload = new L12TournamentCreatePayload("主办者窗口接口", "single", "public", 16,
            DateTimeOffset.UtcNow.AddHours(3), "现行规则", "", "after", "season", "", 50, 5,
            RegistrationOpensAt: opens, RegistrationClosesAt: closes);
        using var created = await client.PostAsync("/api/tournaments", System.Net.Http.Json.JsonContent.Create(
            new TournamentCreateRequest(payload, "ordinary-create-window", f.Store.Version)));
        Assert.Equal(System.Net.HttpStatusCode.OK, created.StatusCode);
        var organizer = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var tournament = Assert.Single(f.Store.Tournaments(organizer).Items);
        Assert.Equal(opens.ToUniversalTime(), tournament.RegistrationOpensAt);
        Assert.Equal(TimeSpan.Zero, tournament.RegistrationOpensAt!.Value.Offset);
        Assert.Equal(closes.ToUniversalTime(), tournament.RegistrationClosesAt);
        Assert.False(tournament.RegistrationOpen);
        Assert.Empty(tournament.Participants);
        using var edited = await client.PostAsync($"/api/tournaments/{tournament.Id}/registration-window",
            System.Net.Http.Json.JsonContent.Create(new TournamentRegistrationWindowRequest(null, null,
                "主办者取消窗口限制", "ordinary-edit-window", tournament.Version)));
        Assert.Equal(System.Net.HttpStatusCode.OK, edited.StatusCode);
        tournament = f.Store.Tournament(organizer, tournament.Id)!;
        Assert.True(tournament.RegistrationOpen);
        var admin = f.Store.Login("Admin", "L12master").Account!;
        var context = new L12AdminAuditContext("other-host", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
        var other = f.Store.CreateTournament(admin, payload, context, true);
        using var denied = await client.PostAsync($"/api/tournaments/{other.Id}/registration-window",
            System.Net.Http.Json.JsonContent.Create(new TournamentRegistrationWindowRequest(null, null,
                "不可编辑其他主办者", "ordinary-other-window", other.Version)));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(opens, f.Store.Tournament(admin, other.Id)!.RegistrationOpensAt);
    }

    [Theory]
    [InlineData("managed")]
    [InlineData("automated")]
    public void PostponingDoesNotShiftWindowsOrReopenManualClosure(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-window-postpone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var host = store.Login("Admin", "L12master").Account!;
            var context = new L12AdminAuditContext("postpone-window", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
            var opens = DateTimeOffset.UtcNow.AddHours(-1);
            var closes = DateTimeOffset.UtcNow.AddHours(2);
            var payload = new L12TournamentCreatePayload("延期窗口", "single", "public", 16,
                DateTimeOffset.UtcNow.AddHours(3), "现行规则", "", "after", "season", "", 50, 5,
                OperationMode: mode, RegistrationOpensAt: opens, RegistrationClosesAt: closes);
            var tournament = store.CreateTournament(host, payload, context, true);
            tournament = store.SetTournamentPhase(host, tournament.Id, new("registration-closed", "人工关闭"), tournament.Version, context, true);
            var later = DateTimeOffset.UtcNow.AddHours(5);
            tournament = store.PostponeTournament(host, tournament.Id, new(later, "延期"), tournament.Version, context, true);
            Assert.Equal(later, tournament.StartAt);
            Assert.Equal(opens, tournament.RegistrationOpensAt);
            Assert.Equal(closes, tournament.RegistrationClosesAt);
            Assert.False(tournament.RegistrationOpen);
            if (mode == "automated")
                Assert.Equal(later.AddMinutes(-tournament.CheckInMinutes), tournament.NextAutomaticActionAt);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StartCheckWarningUsesTheSameEffectiveWindowAsRegistrationAndDetails()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-window-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var host = store.Register("winwarning", "password-123").Account!;
            var context = new L12AdminAuditContext("window-warning", "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test");
            var payload = new L12TournamentCreatePayload("开赛检查窗口", "single", "public", 16,
                DateTimeOffset.UtcNow.AddHours(3), "现行规则", "", "after", "season", "", 50, 5,
                RegistrationOpensAt: DateTimeOffset.UtcNow.AddHours(1), RegistrationClosesAt: DateTimeOffset.UtcNow.AddHours(2));
            var tournament = store.CreateTournament(host, payload, context, true);
            Assert.False(tournament.RegistrationOpen);
            Assert.DoesNotContain(store.TournamentStartCheck(host, tournament.Id).Warnings, text => text.StartsWith("报名仍开放", StringComparison.Ordinal));
            tournament = store.SetTournamentRegistrationWindow(host, tournament.Id, null, null, tournament.Version, context, true);
            Assert.True(tournament.RegistrationOpen);
            Assert.Contains(store.TournamentStartCheck(host, tournament.Id).Warnings, text => text.StartsWith("报名仍开放", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }
}
