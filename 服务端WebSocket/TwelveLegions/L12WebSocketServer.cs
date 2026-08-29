using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwelveLegions.Server;

public sealed class L12WebSocketServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions CommandJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly L12RoomManager _rooms;
    private readonly MatchRecorder _recorder;
    private readonly L12PlatformStore _platform;
    private readonly L12AdminCommandBus _adminCommands;
    private readonly IL12ReleaseControlAdapter _releaseControl;
    private readonly L12Catalog _catalog;
    private readonly int _cardCount;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly ConcurrentDictionary<Guid, string> _socketPlatformSessions = new();
    private WebApplication? _app;
    private IReadOnlyList<string> _addresses = [];

    public IReadOnlyList<string> Addresses => _addresses;

    public L12WebSocketServer(L12RoomManager rooms, MatchRecorder recorder, L12PlatformStore platform,
        L12Catalog catalog, IL12ReleaseControlAdapter? releaseControl = null)
    {
        _rooms = rooms;
        _recorder = recorder;
        _platform = platform;
        _adminCommands = new L12AdminCommandBus(platform);
        _releaseControl = releaseControl ?? new L12DisabledReleaseControlAdapter();
        _catalog = catalog;
        _cardCount = catalog.Cards.Count;
        _platform.SessionsRevoked += HandlePlatformSessionsRevoked;
    }

    public async Task StartAsync(int port)
    {
        var host = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        if (string.IsNullOrWhiteSpace(host)) host = "0.0.0.0";
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{host}:{port}");
        builder.Services.AddRouting();
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            var correlationId = L12CorrelationIds.AcceptOrCreate(context.Request.Headers[L12CorrelationIds.HeaderName]);
            context.Items[L12CorrelationIds.ContextItemName] = correlationId;
            var originalPath = context.Request.Path.Value ?? string.Empty;
            context.Items[L12CorrelationIds.OriginalPathItemName] = originalPath;
            if (context.Request.Path.StartsWithSegments("/api/admin/v1", out var remaining))
                context.Request.Path = "/api/admin" + remaining;
            context.Response.Headers[L12CorrelationIds.HeaderName] = correlationId;
            var origin = context.Request.Headers.Origin.ToString();
            if (IsAllowedOrigin(origin)) context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Vary = "Origin";
            context.Response.Headers.AccessControlAllowHeaders =
                $"Content-Type, Authorization, {L12CorrelationIds.HeaderName}, Idempotency-Key, If-Match, X-Admin-Reason";
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
            context.Response.Headers.AccessControlExposeHeaders =
                $"{L12CorrelationIds.HeaderName}, X-Command-ID, X-Idempotent-Replay, ETag";
            if (HttpMethods.IsOptions(context.Request.Method)) { context.Response.StatusCode = StatusCodes.Status204NoContent; return; }
            var featureId = context.Request.Path.StartsWithSegments("/api/public-decks") ? "publicDecks"
                : context.Request.Path.StartsWithSegments("/api/tournaments") ? "tournaments"
                : null;
            if (featureId is not null && !_platform.CaptureOperationsPolicy().IsFeatureEnabled(featureId))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "feature_disabled",
                    message = "该功能当前未开放",
                    correlationId,
                });
                return;
            }
            await next();
        });
        _app.UseRouting();
        _app.UseWebSockets();
        _app.MapGet("/health", () => Results.Ok(new { service = "twelve-legions", cards = _cardCount }));
        _app.MapGet("/api/operations/effective-policy", (HttpRequest request) =>
        {
            var policy = _platform.EffectiveOperationsPolicy();
            request.HttpContext.Response.Headers.ETag = $"\"{policy.Version}\"";
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            return Results.Ok(policy);
        });
        _app.MapGet("/api/matches", async (HttpRequest request, int? limit) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized()
                : Results.Ok(await _recorder.ListMatchesForPlayerAsync(account.Username, limit ?? 50));
        });
        _app.MapGet("/api/matches/{matchId}", async (HttpRequest request, string matchId) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var match = await _recorder.GetMatchForPlayerAsync(matchId, account.Username);
            return match is null ? Results.NotFound() : Results.Ok(match);
        });
        _app.MapGet("/api/rankings", async (int? limit) => Results.Ok(await _recorder.ListRankingMatchesAsync(limit ?? 500)));
        _app.MapPost("/api/auth/register", (AuthRequest request) =>
        {
            var result = _platform.Register(request.Username ?? string.Empty, request.Password ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message, result.Account, result.Token }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/auth/login", (HttpRequest request, AuthRequest body) =>
        {
            var clientKey = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
            var result = _platform.Login(body.Username ?? string.Empty, body.Password ?? string.Empty,
                new L12LoginAttemptContext(CorrelationId(request), clientKey,
                    request.HttpContext.Items.TryGetValue(L12CorrelationIds.OriginalPathItemName, out var path)
                        ? path as string ?? request.Path.Value ?? "/api/auth/login"
                        : request.Path.Value ?? "/api/auth/login"));
            if (result.Success) return Results.Ok(new { result.Message, result.Account, result.Token });
            if (result.RetryAfterSeconds > 0)
                request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
            return ApiError(request, result.Code, result.Message,
                result.Code == "login_rate_limited"
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status401Unauthorized);
        });
        _app.MapGet("/api/auth/mfa/capability", () => Results.Ok(_platform.MfaCapability()));
        _app.MapGet("/api/auth/me", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(account);
        });
        _app.MapPost("/api/auth/change-password", (HttpRequest request, ChangePasswordRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            var result = _platform.ChangePassword(authenticated.Account.Id, body.CurrentPassword ?? string.Empty,
                body.NewPassword ?? string.Empty, authenticated.SessionId);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
        });
        _app.MapGet("/api/auth/sessions", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsReadOwn, out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.Sessions(authenticated.Account.Id, authenticated.SessionId));
        });
        _app.MapDelete("/api/auth/sessions/current", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            var result = _platform.RevokeOwnSession(authenticated, authenticated.SessionId,
                AuditContext(request, L12Permission.SessionsRevokeOwn));
            return SessionRevocationResponse(request, result);
        });
        _app.MapDelete("/api/auth/sessions/{sessionId}", (HttpRequest request, string sessionId) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            var result = _platform.RevokeOwnSession(authenticated, sessionId,
                AuditContext(request, L12Permission.SessionsRevokeOwn));
            return SessionRevocationResponse(request, result);
        });
        _app.MapDelete("/api/auth/sessions", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            var result = _platform.RevokeOwnSessions(authenticated,
                AuditContext(request, L12Permission.SessionsRevokeOwn));
            return SessionRevocationResponse(request, result);
        });
        _app.MapGet("/api/players", (HttpRequest request, string? search) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.FindPlayers(account.Id, search)
                .Select(player => new { player.AccountId, player.Username, player.Status, player.Direction,
                    player.CreatedAt, online = _rooms.IsAccountOnline(player.AccountId) }));
        });
        _app.MapGet("/api/presence", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var presence = _rooms.DescribeOnlinePresence(account.Id);
            var friends = _platform.Friends(account.Id)
                .ToDictionary(player => player.AccountId, player => player, StringComparer.OrdinalIgnoreCase);
            var pending = _platform.FriendRequests(account.Id)
                .ToDictionary(player => player.AccountId, player => player, StringComparer.OrdinalIgnoreCase);
            return Results.Ok(_platform.Accounts()
                .Where(player => presence.ContainsKey(player.Id))
                .OrderBy(player => player.Username)
                .Select(player =>
                {
                    var state = presence[player.Id];
                    var relationship = friends.GetValueOrDefault(player.Id) ?? pending.GetValueOrDefault(player.Id);
                    return new
                    {
                        accountId = player.Id,
                        player.Username,
                        online = true,
                        state.Activity,
                        state.RoomCode,
                        state.CanInvite,
                        state.CanSpectate,
                        state.ActionReason,
                        friendStatus = player.Id == account.Id ? "self" : relationship?.Status ?? "none",
                        friendDirection = relationship?.Direction ?? "none",
                    };
                }));
        });
        _app.MapGet("/api/friends", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.Friends(account.Id)
                .Select(player => new { player.AccountId, player.Username, player.Status, player.Direction,
                    player.CreatedAt, online = _rooms.IsAccountOnline(player.AccountId) }));
        });
        _app.MapGet("/api/friends/requests", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.FriendRequests(account.Id));
        });
        _app.MapPost("/api/friends/requests", (HttpRequest request, FriendRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var result = _platform.SendFriendRequest(account.Id, body.AccountId ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/friends/requests/{requesterId}/resolve", (HttpRequest request, string requesterId, FriendResolveRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var result = _platform.ResolveFriendRequest(account.Id, requesterId, body.Accept);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
        });
        _app.MapDelete("/api/friends/{friendId}", (HttpRequest request, string friendId) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return _platform.RemoveFriend(account.Id, friendId) ? Results.Ok() : Results.NotFound();
        });
        _app.MapGet("/api/decks", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.Decks(account.Id));
        });
        _app.MapPut("/api/decks", (HttpRequest request, L12CustomDeckSubmission submission) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var policy = _platform.CaptureOperationsPolicy();
            if (!L12DeckValidator.TryValidate(_catalog, submission, out var deck, out var error,
                    policy.CardRestrictions))
                return Results.BadRequest(new { message = error });
            return Results.Ok(_platform.UpsertDeck(account.Id, deck));
        });
        _app.MapDelete("/api/decks/{name}", (HttpRequest request, string name) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return _platform.DeleteDeck(account.Id, name) ? Results.Ok() : Results.NotFound();
        });
        _app.MapGet("/api/public-decks", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return Results.Ok(_platform.PublishedDecks(account?.Id));
        });
        _app.MapPost("/api/public-decks", (HttpRequest request, PublishedDeckRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (body.Deck is null) return Results.BadRequest(new { message = "牌库数据为空" });
            var policy = _platform.CaptureOperationsPolicy();
            if (!L12DeckValidator.TryValidate(_catalog, body.Deck, out var deck, out var error,
                    policy.CardRestrictions))
                return Results.BadRequest(new { message = error });
            var published = _platform.PublishDeck(account.Id, deck, body.PublicationId);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapDelete("/api/public-decks/{id}", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return _platform.DeletePublishedDeck(account.Id, id) ? Results.Ok() : Results.NotFound();
        });
        _app.MapPost("/api/public-decks/{id}/like", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var published = _platform.TogglePublishedDeckLike(account.Id, id);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapPost("/api/public-decks/{id}/copy", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            var published = _platform.RecordPublishedDeckCopy(id, account?.Id);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapGet("/api/tournaments", (HttpRequest request, string? status, string? search, bool? mine) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.Tournaments(authenticated.Account, status, search, mine ?? false);
            request.HttpContext.Response.Headers.ETag = $"\"{result.PlatformVersion}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/tournaments/code/{code}", (HttpRequest request, string code) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.Tournament(authenticated.Account, code);
            if (result is null) return ApiError(request, "tournament_not_found", "赛事不存在",
                StatusCodes.Status404NotFound);
            request.HttpContext.Response.Headers.ETag = $"\"{result.Version}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/tournaments/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.Tournament(authenticated.Account, id);
            if (result is null) return ApiError(request, "tournament_not_found", "赛事不存在",
                StatusCodes.Status404NotFound);
            request.HttpContext.Response.Headers.ETag = $"\"{result.Version}\"";
            return Results.Ok(result);
        });
        _app.MapPost("/api/tournaments", (HttpRequest request, TournamentCreateRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsCreate;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.create",
                "tournaments", body.Tournament, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CreateTournament(current.Actor, current.Payload,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/tournaments/import-legacy", (HttpRequest request, TournamentLegacyImportRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentImportLegacy;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentLegacyImportPayload(body.Tournaments ?? [], body.PreviewHash);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.import-legacy",
                "tournaments", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ImportLegacyTournaments(current.Actor, current.Payload,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/tournaments/{id}/registrations", (HttpRequest request, string id,
            TournamentRegistrationRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentRegistrationPayload(body.DeckName ?? string.Empty,
                body.DeckCode ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.register",
                $"tournament:{id}/registration:{authenticated.Account.Id}", payload, key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.RegisterTournament(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/registration", (HttpRequest request, string id,
            TournamentRegistrationRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentRegistrationPayload(body.DeckName ?? string.Empty,
                body.DeckCode ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.registration.update",
                $"tournament:{id}/registration:{authenticated.Account.Id}", payload, key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.UpdateTournamentRegistration(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapDelete("/api/tournaments/{id}/registration", (HttpRequest request, string id,
            [Microsoft.AspNetCore.Mvc.FromBody] TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.drop",
                $"tournament:{id}/registration:{authenticated.Account.Id}", new TournamentEmptyPayload(), key,
                expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.DropTournament(current.Actor, id, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/staff", (HttpRequest request, string id,
            TournamentStaffRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new TournamentStaffCommandPayload(id,
                new L12TournamentStaffPayload(body.RefereeAccountIds ?? []));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.staff.set",
                $"tournament:{id}/staff", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.SetTournamentStaff(current.Actor, current.Payload.TournamentId,
                    current.Payload.Staff, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/start", (HttpRequest request, string id, TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.start",
                $"tournament:{id}", new TournamentTargetCommandPayload(id), key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.StartTournament(current.Actor, current.Payload.TournamentId, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds", (HttpRequest request, string id, TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.round.create",
                $"tournament:{id}/rounds", new TournamentTargetCommandPayload(id), key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CreateNextRound(current.Actor, current.Payload.TournamentId, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds/{roundNumber:int}/check-in",
            (HttpRequest request, string id, int roundNumber, TournamentCheckInRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentCheckInPayload(body.AccountId, body.Ready);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.check-in",
                $"tournament:{id}/round:{roundNumber}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CheckInTournament(current.Actor, id, roundNumber, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds/{roundNumber:int}/start",
            (HttpRequest request, string id, int roundNumber, TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.round.start",
                $"tournament:{id}/round:{roundNumber}", new TournamentRoundCommandPayload(id, roundNumber), key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.StartTournamentRound(current.Actor, current.Payload.TournamentId,
                    current.Payload.RoundNumber, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds/{roundNumber:int}/pause",
            (HttpRequest request, string id, int roundNumber, TournamentPauseRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentPausePayload(body.Paused, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.round.pause",
                $"tournament:{id}/round:{roundNumber}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.PauseTournamentRound(current.Actor, id, roundNumber,
                    current.Payload, expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/matches/{matchId}/time-extension",
            (HttpRequest request, string id, string matchId, TournamentTimeExtensionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentTimeExtensionPayload(body.Minutes, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.match.extend",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ExtendTournamentMatch(current.Actor, id, matchId, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/matches/{matchId}/rulings",
            (HttpRequest request, string id, string matchId, TournamentRulingRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new TournamentRulingCommandPayload(id, matchId,
                new L12TournamentRulingPayload(body.Kind ?? string.Empty, body.TargetAccountId,
                    body.Decision ?? string.Empty, body.Reason ?? string.Empty));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.ruling.apply",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ApplyTournamentRuling(current.Actor, current.Payload.TournamentId,
                    current.Payload.MatchId, current.Payload.Ruling,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/matches/{matchId}/reference",
            async (HttpRequest request, string id, string matchId, TournamentMatchReferenceRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var reference = body.RecordedMatchId?.Trim() ?? string.Empty;
            var recorded = string.IsNullOrWhiteSpace(reference) ? null : await _recorder.GetMatchAsync(reference);
            var tournament = _platform.Tournament(authenticated.Account, id);
            var table = tournament?.Rounds.SelectMany(round => round.Matches).FirstOrDefault(item => item.Id == matchId);
            if (recorded is null || recorded.Match.EndedUtc is null || table is null
                || !new HashSet<string>([recorded.Match.Player0, recorded.Match.Player1], StringComparer.OrdinalIgnoreCase)
                    .SetEquals(new[] { table.PlayerAName, table.PlayerBName }))
                return ApiError(request, "invalid_match_reference", "仅可绑定已结束且参赛账号一致的对局记录",
                    StatusCodes.Status409Conflict);
            var payload = new TournamentReferenceCommandPayload(id, matchId,
                new L12TournamentMatchReferencePayload(reference));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.match.reference",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.LinkTournamentMatch(current.Actor, current.Payload.TournamentId,
                    current.Payload.MatchId, current.Payload.Reference,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/complete", (HttpRequest request, string id,
            TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.complete",
                $"tournament:{id}", new TournamentTargetCommandPayload(id), key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CompleteTournament(current.Actor, current.Payload.TournamentId, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapGet("/api/admin/releases/artifacts", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleasesRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.ReleaseArtifacts(authenticated.Account, _releaseControl));
        });
        _app.MapGet("/api/admin/releases/environments", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleaseRuntimeRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.ReleaseEnvironments(authenticated.Account, _releaseControl));
        });
        _app.MapGet("/api/admin/releases/runs", (HttpRequest request, string? environment, string? status,
            int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleasesRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.ReleaseRuns(authenticated.Account, environment, status, limit ?? 100));
        });
        _app.MapGet("/api/admin/releases/runs/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleasesRead, out var authenticated, out var failure))
                return failure;
            var run = _platform.ReleaseRun(authenticated.Account, id);
            return run is null
                ? ApiError(request, "release_run_not_found", "发布记录不存在", StatusCodes.Status404NotFound)
                : Results.Ok(run);
        });
        _app.MapPost("/api/admin/releases/deploy", (HttpRequest request, ReleaseDeployRequest body) =>
        {
            const L12Permission permission = L12Permission.ReleasesExecute;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryReleaseCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            if (TryReplayReleaseCommand(request, authenticated.Account, permission, "release.deploy", key,
                    expected, body.DryRun, body.Reason,
                    payload => payload.Action == "deploy"
                               && payload.Artifact.Id == body.ArtifactId?.Trim()
                               && payload.Environment == body.Environment?.Trim().ToLowerInvariant(),
                    out var replay)) return replay;
            try
            {
                var payload = _platform.CaptureReleaseDeploy(authenticated.Account,
                    body.ArtifactId ?? string.Empty, body.Environment ?? string.Empty, _releaseControl);
                var validation = ExecuteReleaseCommand(authenticated.Account, payload, expected,
                    AuditContext(request, permission), false);
                if (!validation.Success) return ApiError(request, validation.Code, validation.Message,
                    validation.StatusCode);
                var command = CommandEnvelope(request, authenticated.Account, permission, "release.deploy",
                    $"release:{payload.Environment}", payload, key, expected, body.DryRun, body.Reason);
                var outcome = _adminCommands.Execute(command, permission,
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, true),
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, false), L12AdminCommandRisk.High);
                return ReleaseCommandResponse(request, command, outcome, payload.Environment);
            }
            catch (Exception error) when (IsReleaseRequestError(error))
            {
                return ReleaseRequestError(request, authenticated.Account, permission, error);
            }
        });
        _app.MapPost("/api/admin/releases/rollback", (HttpRequest request, ReleaseRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.ReleasesExecute;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryReleaseCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            if (TryReplayReleaseCommand(request, authenticated.Account, permission, "release.rollback", key,
                    expected, body.DryRun, body.Reason,
                    payload => payload.Action == "rollback"
                               && payload.RollbackTargetRunId == body.TargetRunId?.Trim(),
                    out var replay)) return replay;
            try
            {
                var payload = _platform.CaptureReleaseRollback(authenticated.Account,
                    body.TargetRunId ?? string.Empty, _releaseControl);
                var validation = ExecuteReleaseCommand(authenticated.Account, payload, expected,
                    AuditContext(request, permission), false);
                if (!validation.Success) return ApiError(request, validation.Code, validation.Message,
                    validation.StatusCode);
                var command = CommandEnvelope(request, authenticated.Account, permission, "release.rollback",
                    $"release:{payload.Environment}", payload, key, expected, body.DryRun, body.Reason);
                var outcome = _adminCommands.Execute(command, permission,
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, true),
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, false), L12AdminCommandRisk.High);
                return ReleaseCommandResponse(request, command, outcome, payload.Environment);
            }
            catch (Exception error) when (IsReleaseRequestError(error))
            {
                return ReleaseRequestError(request, authenticated.Account, permission, error);
            }
        });
        _app.MapPost("/api/bugs", (HttpRequest request, BugRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Description)) return Results.BadRequest(new { message = "请填写问题描述" });
            var account = _platform.Authenticate(request.Headers.Authorization);
            return Results.Ok(_platform.AddBug(account, body.Title ?? string.Empty, body.Description, body.Page ?? string.Empty,
                body.RoomCode, body.MatchId, body.Version ?? "dev"));
        });
        _app.MapGet("/api/admin/accounts", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAccountsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.Accounts());
        });
        _app.MapPut("/api/admin/accounts/{id}/role", (HttpRequest request, string id, RoleRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountRolesWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = new RoleCommandPayload(id, body.Role?.Trim().ToLowerInvariant() ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.role.set", $"account:{id}", payload,
                idempotencyKey, expectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current =>
                {
                    var before = _platform.Account(current.Payload.AccountId);
                    if (!_platform.SetRole(current.Actor, current.Payload.AccountId, current.Payload.Role,
                            current.AuditContext))
                        return L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "账号或角色无效",
                            StatusCodes.Status400BadRequest);
                    var updated = _platform.Account(current.Payload.AccountId)!;
                    var changed = before is not null
                        && !string.Equals(before.Role, updated.Role, StringComparison.OrdinalIgnoreCase);
                    return L12AdminCommandResult<RoleCommandResult>.Ok(
                        new RoleCommandResult(current.Payload.AccountId, updated.Role, changed),
                        changed ? "账号角色已更新" : "账号角色没有变化");
                },
                current =>
                {
                    if (!_platform.CanSetRole(current.Payload.AccountId, current.Payload.Role))
                        return L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "账号或角色无效",
                            StatusCodes.Status400BadRequest);
                    return L12AdminCommandResult<RoleCommandResult>.Ok(
                        new RoleCommandResult(current.Payload.AccountId, current.Payload.Role, false), "干运行验证通过");
                });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPut("/api/admin/accounts/{id}/status",
            (HttpRequest request, string id, AccountStatusRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountStatusWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = new L12AccountStatusCommandPayload(id, body.Disabled, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.status.set",
                $"account:{id}", payload, idempotencyKey, expectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteAccountStatus(current.Actor, current.Payload, current.AuditContext, true),
                current => ExecuteAccountStatus(current.Actor, current.Payload, current.AuditContext, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/operations/config", (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var result = _platform.OperationsConfig(authenticated.Account);
            request.HttpContext.Response.Headers.ETag = $"\"{result.Version}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/admin/operations/config/history", (HttpRequest request, int? limit) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.OperationsConfigHistory(authenticated.Account, limit ?? 50));
        });
        _app.MapPost("/api/admin/operations/config/preview",
            (HttpRequest request, OperationsConfigPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var result = _platform.PreviewOperationsConfig(authenticated.Account, body.Config,
                    body.ExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault()),
                    AuditContext(request, permission) with { DryRun = true, Outcome = "dry-run" });
                request.HttpContext.Response.Headers.ETag = $"\"{result.CurrentVersion}\"";
                return Results.Ok(result);
            }
            catch (L12OperationsConfigException error)
            {
                return OperationsConfigError(request, error);
            }
        });
        _app.MapPut("/api/admin/operations/config", (HttpRequest request, OperationsConfigApplyRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "operations.config.apply",
                "operations:config", body.Config, key, expected, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.ApplyOperationsConfig(current.Actor,
                    current.Payload, expected, current.Reason, current.AuditContext)));
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/config/rollback",
            (HttpRequest request, OperationsConfigRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12OperationsRollbackCommandPayload(body.VersionId?.Trim() ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "operations.config.rollback",
                "operations:config", payload, key, expected, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.RollbackOperationsConfig(current.Actor,
                    current.Payload.VersionId, expected, current.Reason, current.AuditContext)));
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapGet("/api/admin/runtime/status", (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminRuntimeRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var observedAt = DateTimeOffset.UtcNow;
            var rooms = _rooms.RuntimeStats();
            var releases = _platform.ReleaseEnvironments(authenticated.Account, _releaseControl);
            var status = new L12RuntimeStatusView(observedAt,
                typeof(L12WebSocketServer).Assembly.GetName().Version?.ToString() ?? "unknown",
                _cardCount, rooms.OnlineAccountCount, _sockets.Count, rooms.RoomCount,
                rooms.ActiveGameCount, releases,
                new L12RuntimeDependencyView("cdn", false, "unavailable",
                    "no-authoritative-source", observedAt));
            return Results.Ok(status);
        });
        _app.MapGet("/api/admin/accounts/{accountId}/sessions", (HttpRequest request, string accountId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSessionsRead, out var authenticated, out var failure)) return failure;
            if (!_platform.AccountExists(accountId)) return ApiError(request, "account_not_found", "账号不存在", StatusCodes.Status404NotFound);
            return Results.Ok(_platform.Sessions(accountId, authenticated.SessionId));
        });
        _app.MapDelete("/api/admin/accounts/{accountId}/sessions/{sessionId}",
            (HttpRequest request, string accountId, string sessionId) =>
        {
            const L12Permission permission = L12Permission.AdminSessionsRevoke;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new SessionCommandPayload(accountId, sessionId);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.session.revoke",
                $"account:{accountId}/session:{sessionId}", payload);
            var outcome = _adminCommands.Execute(command, permission,
                current => SessionCommandResult(_platform.RevokeAccountSession(current.Actor,
                    current.Payload.AccountId, current.Payload.SessionId!, current.AuditContext)),
                current => SessionCommandResult(_platform.RevokeAccountSession(current.Actor,
                    current.Payload.AccountId, current.Payload.SessionId!, current.AuditContext, true)));
            return AdminSessionCommandResponse(request, command, outcome);
        });
        _app.MapDelete("/api/admin/accounts/{accountId}/sessions", (HttpRequest request, string accountId) =>
        {
            const L12Permission permission = L12Permission.AdminSessionsRevoke;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new SessionCommandPayload(accountId, null);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.sessions.revoke-all",
                $"account:{accountId}/sessions", payload);
            var outcome = _adminCommands.Execute(command, permission,
                current => SessionCommandResult(_platform.RevokeAccountSessions(current.Actor,
                    current.Payload.AccountId, current.AuditContext)),
                current => SessionCommandResult(_platform.RevokeAccountSessions(current.Actor,
                    current.Payload.AccountId, current.AuditContext, true)));
            return AdminSessionCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/bugs", (HttpRequest request, string? status, string? priority, string? assignee, string? search) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminBugsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.Bugs(status, priority, assignee, search));
        });
        _app.MapPatch("/api/admin/bugs/{id}", (HttpRequest request, string id, BugUpdateRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminBugsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new BugUpdateCommandPayload(id, body.Status, body.Priority, body.Assignee,
                body.AdminNotes, body.Comment);
            var command = CommandEnvelope(request, authenticated.Account, permission, "bug.update", $"bug:{id}",
                payload, body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                var updated = _platform.UpdateBug(current.Actor, current.Payload.Id, current.Payload.Status,
                    current.Payload.Priority, current.Payload.Assignee, current.Payload.AdminNotes,
                    current.Payload.Comment, current.AuditContext);
                return updated is null
                    ? L12AdminCommandResult<L12BugReportView>.Fail("bug_not_found", "Bug 不存在",
                        StatusCodes.Status404NotFound)
                    : L12AdminCommandResult<L12BugReportView>.Ok(updated, "Bug 已更新");
            }, current =>
            {
                var existing = _platform.Bugs(null).FirstOrDefault(item => item.Id == current.Payload.Id);
                return existing is null
                    ? L12AdminCommandResult<L12BugReportView>.Fail("bug_not_found", "Bug 不存在",
                        StatusCodes.Status404NotFound)
                    : L12AdminCommandResult<L12BugReportView>.Ok(existing, "干运行验证通过");
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/content/{key}", (string key) => Results.Ok(new { key, value = _platform.GetContent(key) }));
        _app.MapGet("/api/admin/content/{key}", (HttpRequest request, string key) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.GetContentEntry(key)); }
            catch (ArgumentException error)
            {
                return ApiError(request, "content_key_not_allowed", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPut("/api/admin/content/{key}/draft", (HttpRequest request, string key, ContentRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new ContentDraftCommandPayload(key, body.Value ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "content.draft.save",
                $"content:{key}", payload, body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                if (!_platform.IsContentKeyAllowed(current.Payload.Key))
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_key_not_allowed",
                        "内容键不在白名单中", StatusCodes.Status400BadRequest);
                return L12AdminCommandResult<L12ContentEntryView>.Ok(_platform.SaveContentDraft(current.Actor,
                    current.Payload.Key, current.Payload.Value, current.AuditContext), "草稿已保存");
            }, current =>
            {
                if (!_platform.IsContentKeyAllowed(current.Payload.Key))
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_key_not_allowed",
                        "内容键不在白名单中", StatusCodes.Status400BadRequest);
                var entry = _platform.GetContentEntry(current.Payload.Key);
                return L12AdminCommandResult<L12ContentEntryView>.Ok(entry with
                {
                    DraftValue = current.Payload.Value,
                    Status = current.Payload.Value == entry.PublishedValue ? "published" : "draft",
                }, "干运行验证通过");
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/content/{key}/publish", (HttpRequest request, string key) =>
        {
            const L12Permission permission = L12Permission.AdminContentPublish;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var payload = _platform.CaptureContentPublish([key]);
                var command = CommandEnvelope(request, authenticated.Account, permission, "content.publish.batch",
                    $"content:{key}", payload);
                var outcome = ExecuteContentPublish(command, permission);
                return AdminCommandResponse(request, command, outcome);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "content_key_not_allowed", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/admin/content/keys", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.ContentKeys());
        });
        _app.MapGet("/api/admin/content/batches", (HttpRequest request, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.ContentBatches(limit ?? 100));
        });
        _app.MapPost("/api/admin/content/preview", (HttpRequest request, ContentBatchRequest body) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.PreviewContentPublish(_platform.CaptureContentPublish(body.Keys ?? []))); }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_content_batch", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (L12ContentStateConflictException error)
            {
                return ApiError(request, "content_version_conflict", error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapPost("/api/admin/content/publish", (HttpRequest request, ContentBatchRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentPublish;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var payload = _platform.CaptureContentPublish(body.Keys ?? []);
                var command = CommandEnvelope(request, authenticated.Account, permission, "content.publish.batch",
                    $"content-batch:{string.Join(',', payload.Items.Select(item => item.Key))}", payload,
                    body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
                var outcome = ExecuteContentPublish(command, permission);
                return AdminCommandResponse(request, command, outcome);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_content_batch", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPost("/api/admin/content/rollback", (HttpRequest request, ContentRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentRollback;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var payload = CaptureContentRollbackForCommand(request, authenticated.Account, body);
                var command = CommandEnvelope(request, authenticated.Account, permission, "content.rollback.batch",
                    $"content-batch:{payload.BatchId}", payload, body.IdempotencyKey, body.ExpectedVersion,
                    body.DryRun, body.Reason);
                var outcome = ExecuteContentRollback(command, permission);
                return AdminCommandResponse(request, command, outcome);
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "content_batch_not_found", error.Message, StatusCodes.Status404NotFound);
            }
            catch (L12ContentStateConflictException error)
            {
                return ApiError(request, "content_version_conflict", error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapGet("/api/admin/effect-atoms", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            return Results.Ok(L12EffectAtomRegistry.All);
        });
        _app.MapGet("/api/admin/effects/coverage", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            return Results.Ok(_catalog.AtomicEffects.Coverage());
        });
        _app.MapGet("/api/admin/effects", (HttpRequest request, string? search, string? status, string? product,
            string? atomKind, int? page, int? pageSize) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.ApplyEffectReviews(
                _catalog.AtomicEffects.Query(search, status, product, atomKind, page ?? 1, pageSize ?? 50)));
        });
        _app.MapGet("/api/admin/effects/{cardId}", (HttpRequest request, string cardId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            var effect = _catalog.AtomicEffects.Find(cardId);
            return effect is null ? Results.NotFound() : Results.Ok(_platform.ApplyEffectReviews(effect));
        });
        _app.MapPut("/api/admin/effects/{cardId}/review", (HttpRequest request, string cardId, EffectReviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminEffectsReview;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new EffectReviewCommandPayload(cardId, body.AbilityId, body.Status ?? "unreviewed", body.Note);
            var command = CommandEnvelope(request, authenticated.Account, permission, "effect.review.save",
                body.AbilityId is null ? $"effect:{cardId}" : $"effect:{cardId}/{body.AbilityId}", payload,
                body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, ExecuteEffectReview,
                current => ValidateEffectReview(current, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/commands", (HttpRequest request, string? status, string? type,
            string? actorId, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminCommandsRead, out var authenticated, out var failure))
                return failure;
            var canReview = L12Authorization.HasPermission(authenticated.Account, L12Permission.AdminApprovalsRead);
            return Results.Ok(_platform.AdminCommands(status, type,
                canReview ? actorId : authenticated.Account.Id, limit ?? 200));
        });
        _app.MapGet("/api/admin/commands/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminCommandsRead, out var authenticated, out var failure))
                return failure;
            var command = _platform.AdminCommand(id);
            if (command is null || command.ActorId != authenticated.Account.Id
                && !L12Authorization.HasPermission(authenticated.Account, L12Permission.AdminApprovalsRead))
                return ApiError(request, "command_not_found", "命令不存在", StatusCodes.Status404NotFound);
            return Results.Ok(command);
        });
        _app.MapGet("/api/admin/approvals", (HttpRequest request, string? status, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminApprovalsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminApprovals(status ?? "requested", limit ?? 200));
        });
        _app.MapPost("/api/admin/approvals/{commandId}",
            (HttpRequest request, string commandId, L12AdminApprovalDecision body) =>
        {
            var stored = _platform.AdminCommand(commandId);
            if (stored?.Type.StartsWith("tournament.", StringComparison.Ordinal) == true)
                return ApiError(request, "tournament_approval_disabled", "赛事操作不使用审批流程",
                    StatusCodes.Status409Conflict);
            if (stored?.Type is "account.role.set" or "account.status.set")
                return ApiError(request, "account_approval_disabled", "账号权限和状态变更直接执行，不使用审批流程",
                    StatusCodes.Status409Conflict);
            var permission = stored?.Type.StartsWith("release.", StringComparison.Ordinal) == true
                    ? L12Permission.ReleaseApprovalsReview
                    : L12Permission.AdminApprovalsReview;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            Func<L12AdminCommandView, L12AccountView, bool>? scopeValidator = permission switch
            {
                L12Permission.ReleaseApprovalsReview => L12PlatformStore.CanReviewReleaseCommand,
                _ => null,
            };
            var outcome = _adminCommands.Review(commandId, authenticated.Account, body,
                AuditContext(request, permission) with { CommandId = commandId }, ExecuteApprovedCommand,
                permission, scopeValidator);
            return AdminReviewResponse(request, outcome);
        });
        _app.MapGet("/api/admin/security/status", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.SecurityStatus(authenticated.Account));
        });
        _app.MapGet("/api/admin/security/audit-archives", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.AuditArchiveSegments(authenticated.Account));
        });
        _app.MapPost("/api/admin/security/audit-archives",
            (HttpRequest request, AuditArchiveRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAuditArchive;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = CaptureAuditArchiveForCommand(authenticated.Account, idempotencyKey,
                body.RetentionDays);
            var command = CommandEnvelope(request, authenticated.Account, permission, "security.audit.archive",
                "security:audit", payload, idempotencyKey, expectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteAuditArchive(current.Actor, current.Payload, current.AuditContext, true),
                current => ExecuteAuditArchive(current.Actor, current.Payload, current.AuditContext, false),
                L12AdminCommandRisk.High);
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/security/audit-recovery-rehearsal", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.RehearseAuditArchiveRecovery(authenticated.Account));
        });
        _app.MapGet("/api/admin/audit", (HttpRequest request, string? category, string? outcome,
            string? actorId, string? commandId, string? correlationId, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAuditRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminAudit(category, limit ?? 200, outcome, actorId, commandId,
                correlationId));
        });
        _app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                return;
            }
            await HandleConnectionAsync(context, context.RequestAborted);
        });
        await _app.StartAsync();
        _addresses = _app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? _app.Urls.ToArray();
        Console.WriteLine($"HTTP: http://{host}:{port}  WebSocket: /ws");
    }

    public async Task StopAsync()
    {
        foreach (var socket in _sockets.Values)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stopped", CancellationToken.None); }
            catch { }
            socket.Dispose();
        }
        if (_app is not null) await _app.StopAsync();
    }

    private async Task HandleConnectionAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        WebSocket? socket = null;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            _sockets[sessionId] = socket;
            var buffer = new byte[32 * 1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, buffer, cancellationToken);
                if (message is null) break;
                await DispatchAsync(sessionId, message, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException exception) { Console.WriteLine($"WebSocket {sessionId}: {exception.Message}"); }
        finally
        {
            _sockets.TryRemove(sessionId, out _);
            _socketPlatformSessions.TryRemove(sessionId, out _);
            await SendManyAsync(_rooms.Disconnect(sessionId), CancellationToken.None);
            socket?.Dispose();
        }
    }

    private async Task DispatchAsync(Guid sessionId, string json, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            await SendAsync(sessionId, new { type = "error", message = "消息不是有效 JSON" }, cancellationToken);
            return;
        }
        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                await SendAsync(sessionId, new { type = "error", message = "消息缺少 type" }, cancellationToken);
                return;
            }
            var messageType = typeElement.GetString();
            if (messageType != "hello" && _socketPlatformSessions.TryGetValue(sessionId, out var platformSessionId)
                && !_platform.IsSessionActive(platformSessionId))
            {
                _socketPlatformSessions.TryRemove(sessionId, out _);
                await SendAsync(sessionId, new { type = "authenticationRequired", message = "登录会话已撤销" }, cancellationToken);
                return;
            }
            IReadOnlyList<OutgoingMessage> outgoing = messageType switch
            {
                "hello" => AuthenticateSession(sessionId, root),
                "createRoom" => CreateRoom(sessionId, root),
                "createSandbox" => await CreateSandboxAsync(sessionId, root),
                "joinRoom" => _rooms.JoinRoom(sessionId, GetString(root, "roomCode")),
                "inviteFriend" => _rooms.InviteFriend(sessionId, GetString(root, "accountId")),
                "resolveFriendInvitation" => _rooms.ResolveFriendInvitation(sessionId,
                    GetString(root, "invitationId"), GetBool(root, "accept", false)),
                "spectateRoom" => _rooms.SpectateRoom(sessionId, GetString(root, "roomCode")),
                "leaveRoom" => _rooms.LeaveRoom(sessionId),
                "selectDeck" => _rooms.SelectDeck(sessionId, GetInt(root, "deckIndex")),
                "selectCustomDeck" when root.TryGetProperty("deck", out var deckElement)
                    => SelectCustomDeck(sessionId, deckElement),
                "ready" => await _rooms.SetReadyAsync(sessionId, GetBool(root, "ready", true)),
                "gameAction" when root.TryGetProperty("command", out var command) => await _rooms.HandleActionAsync(sessionId, command),
                "sandboxAction" when root.TryGetProperty("command", out var sandboxCommand)
                    => await _rooms.HandleSandboxActionAsync(sessionId, GetInt(root, "actingPlayerIndex", -1), sandboxCommand),
                "gmAction" when root.TryGetProperty("command", out var gmCommand) => await _rooms.HandleGmActionAsync(sessionId, gmCommand),
                "getEffectiveOperationsPolicy" => [EffectiveOperationsPolicyMessage(sessionId)],
                "ping" => [new OutgoingMessage(sessionId, new { type = "pong", utc = DateTimeOffset.UtcNow })],
                "deploymentProbe" => [new OutgoingMessage(sessionId, new
                {
                    type = "deploymentProbe",
                    service = "twelve-legions",
                    protocolVersion = 1,
                    authentication = "token",
                })],
                _ => [new OutgoingMessage(sessionId, new { type = "error", message = "未知消息类型" })],
            };
            await SendManyAsync(outgoing, cancellationToken);
        }
    }

    private IReadOnlyList<OutgoingMessage> AuthenticateSession(Guid sessionId, JsonElement root)
    {
        var authenticated = _platform.AuthenticateTokenSession(GetString(root, "authToken"));
        if (authenticated is null)
            return [new OutgoingMessage(sessionId, new { type = "authenticationRequired", message = "请先登录账号" })];
        _socketPlatformSessions[sessionId] = authenticated.SessionId;
        var session = new OutgoingMessage(sessionId,
            _rooms.Connect(sessionId, authenticated.Account.Id, authenticated.Account.Username));
        return new[] { session, EffectiveOperationsPolicyMessage(sessionId) }
            .Concat(_rooms.RecoveryState(sessionId)).ToArray();
    }

    private OutgoingMessage EffectiveOperationsPolicyMessage(Guid sessionId)
        => new(sessionId, new
        {
            type = "effectiveOperationsPolicy",
            policy = _platform.EffectiveOperationsPolicy(),
        });

    private static int GetInt(JsonElement root, string propertyName, int fallback = 0)
        => root.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var value) ? value : fallback;

    private IReadOnlyList<OutgoingMessage> SelectCustomDeck(Guid sessionId, JsonElement deckElement)
    {
        try
        {
            var deck = deckElement.Deserialize<L12CustomDeckSubmission>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return deck is null
                ? [new OutgoingMessage(sessionId, new { type = "deckRejected", message = "牌库数据为空" })]
                : _rooms.SelectCustomDeck(sessionId, deck);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "deckRejected", message = "牌库数据格式错误" })];
        }
    }

    private IReadOnlyList<OutgoingMessage> CreateRoom(Guid sessionId, JsonElement root)
    {
        if (!root.TryGetProperty("options", out var optionsElement)) return _rooms.CreateRoom(sessionId);
        try
        {
            var options = optionsElement.Deserialize<L12RoomOptions>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return _rooms.CreateRoom(sessionId, options);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "error", message = "房间设置格式错误" })];
        }
    }

    private async Task<IReadOnlyList<OutgoingMessage>> CreateSandboxAsync(Guid sessionId, JsonElement root)
    {
        try
        {
            var request = root.TryGetProperty("request", out var requestElement)
                ? requestElement.Deserialize<L12SandboxRequest>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : new L12SandboxRequest();
            return await _rooms.CreateSandboxAsync(sessionId, request);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "error", message = "沙盒设置格式错误" })];
        }
    }

    private async Task SendManyAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages) await SendAsync(message.SessionId, message.Payload, cancellationToken);
    }

    private async Task SendAsync(Guid sessionId, object payload, CancellationToken cancellationToken)
    {
        if (!_sockets.TryGetValue(sessionId, out var socket) || socket.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("消息过大");
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static bool GetBool(JsonElement root, string name, bool fallback)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;

    private static bool IsAllowedOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host is "localhost" or "127.0.0.1" or "::1") return true;
        var configured = Environment.GetEnvironmentVariable("L12_ALLOWED_ORIGINS") ?? string.Empty;
        return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private bool TryAuthenticate(HttpRequest request, L12Permission permission,
        out L12AuthenticatedSession authenticated, out IResult failure)
    {
        var current = _platform.AuthenticateSession(request.Headers.Authorization);
        if (current is not null)
        {
            authenticated = current;
            failure = Results.Empty;
            return true;
        }

        var context = AuditContext(request, permission) with { Outcome = "denied", Reason = "authentication-required" };
        _platform.RecordAuthorizationDenied(null, context, L12Authorization.Key(permission), "authentication-required");
        authenticated = null!;
        failure = ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
        return false;
    }

    private bool TryAuthorize(HttpRequest request, L12Permission permission,
        out L12AuthenticatedSession authenticated, out IResult failure)
    {
        if (!TryAuthenticate(request, permission, out authenticated, out failure)) return false;
        if (L12Authorization.HasPermission(authenticated.Account, permission)) return true;

        var context = AuditContext(request, permission) with { Outcome = "denied", Reason = "permission-denied" };
        _platform.RecordAuthorizationDenied(authenticated.Account, context, L12Authorization.Key(permission),
            "permission-denied");
        failure = ApiError(request, "permission_denied", "当前账号没有执行此操作的权限",
            StatusCodes.Status403Forbidden);
        return false;
    }

    private static L12AdminAuditContext AuditContext(HttpRequest request, L12Permission permission)
        => new(CorrelationId(request), L12Authorization.Key(permission), RequestMethod: request.Method,
            RequestPath: request.HttpContext.Items.TryGetValue(L12CorrelationIds.OriginalPathItemName, out var path)
                ? path as string
                : request.Path.Value);

    private static string CorrelationId(HttpRequest request)
        => request.HttpContext.Items.TryGetValue(L12CorrelationIds.ContextItemName, out var value)
           && value is string correlationId
            ? correlationId
            : L12CorrelationIds.AcceptOrCreate(request.Headers[L12CorrelationIds.HeaderName]);

    private static IResult ApiError(HttpRequest request, string code, string message, int statusCode)
        => Results.Json(new L12ApiError(code, message, CorrelationId(request)), statusCode: statusCode);

    private static IResult SessionRevocationResponse(HttpRequest request, L12SessionRevocationResult result)
        => result.Found
            ? Results.Ok(new
            {
                result.SessionId,
                result.RevokedCount,
                result.AlreadyRevoked,
            })
            : ApiError(request, "session_not_found", "会话不存在", StatusCodes.Status404NotFound);

    private L12AdminCommandEnvelope<TPayload> CommandEnvelope<TPayload>(HttpRequest request,
        L12AccountView actor, L12Permission permission, string type, string scope, TPayload payload,
        string? bodyIdempotencyKey = null, long? bodyExpectedVersion = null, bool bodyDryRun = false,
        string? bodyReason = null)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()
            : bodyIdempotencyKey;
        var expectedVersion = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        var dryRun = bodyDryRun || bool.TryParse(request.Query["dryRun"].FirstOrDefault(), out var requestedDryRun)
            && requestedDryRun;
        var reason = string.IsNullOrWhiteSpace(bodyReason)
            ? request.Headers["X-Admin-Reason"].FirstOrDefault()
            : bodyReason;
        var commandId = Guid.NewGuid().ToString("N");
        var audit = AuditContext(request, permission) with
        {
            CommandId = commandId,
            IdempotencyKey = idempotencyKey,
            ExpectedVersion = expectedVersion,
            DryRun = dryRun,
            Reason = reason,
            Outcome = dryRun ? "dry-run" : "succeeded",
        };
        return new L12AdminCommandEnvelope<TPayload>(commandId, idempotencyKey, type, actor,
            DateTimeOffset.UtcNow, scope, reason, dryRun, expectedVersion, payload, audit);
    }

    private bool TrySecurityCommandOptions(HttpRequest request, L12AccountView actor,
        L12Permission permission, string? bodyIdempotencyKey, long? bodyExpectedVersion,
        out string idempotencyKey, out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "security.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "安全治理写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "security.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required",
                "安全治理写操作必须提供 expectedVersion/If-Match", StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private bool TryOperationsCommandOptions(HttpRequest request, L12AccountView actor,
        L12Permission permission, string? bodyIdempotencyKey, long? bodyExpectedVersion,
        out string idempotencyKey, out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "operations.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "运营配置写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "operations.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required",
                "运营配置写操作必须提供 expectedVersion/If-Match", StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private static L12AdminCommandResult<L12OperationsConfigOperationView> ExecuteOperationsConfig(
        Func<L12OperationsConfigOperationView> operation)
    {
        try
        {
            return L12AdminCommandResult<L12OperationsConfigOperationView>.Ok(operation());
        }
        catch (L12OperationsConfigException error)
        {
            var status = error.Code switch
            {
                "operations_version_conflict" => StatusCodes.Status409Conflict,
                "operations_version_not_found" => StatusCodes.Status404NotFound,
                "permission_denied" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest,
            };
            return L12AdminCommandResult<L12OperationsConfigOperationView>.Fail(error.Code,
                error.Message, status);
        }
    }

    private static IResult OperationsConfigError(HttpRequest request, L12OperationsConfigException error)
    {
        var status = error.Code switch
        {
            "operations_version_conflict" => StatusCodes.Status409Conflict,
            "operations_version_not_found" => StatusCodes.Status404NotFound,
            "permission_denied" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return ApiError(request, error.Code, error.Message, status);
    }

    private L12AuditArchiveCommandPayload CaptureAuditArchiveForCommand(L12AccountView actor,
        string idempotencyKey, int? retentionDays)
    {
        var captured = _platform.CaptureAuditArchive(retentionDays);
        var prior = _platform.FindAdminCommand(actor.Id, idempotencyKey);
        if (prior?.View.Type == "security.audit.archive"
            && prior.View.Payload.Deserialize<L12AuditArchiveCommandPayload>(CommandJsonOptions) is { } stored
            && stored.RetentionDays == captured.RetentionDays)
            return stored;
        return captured;
    }

    private bool TryTournamentCommandOptions(HttpRequest request, L12AccountView actor, L12Permission permission,
        string? bodyIdempotencyKey, long? bodyExpectedVersion, out string idempotencyKey,
        out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "tournament.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "赛事写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "tournament.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required", "赛事写操作必须提供 expectedVersion/If-Match",
                StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private L12AdminCommandResult<T> ExecuteTournamentCommand<TPayload, T>(
        L12AdminCommandEnvelope<TPayload> command,
        L12Permission permission,
        Func<L12AdminCommandEnvelope<TPayload>, bool, T> operation,
        L12AdminCommandRisk risk = L12AdminCommandRisk.Low)
    {
        Func<L12AccountView, bool>? scopedAuthorization = permission is L12Permission.TournamentsManage
            or L12Permission.TournamentRulingsWrite
            ? static (L12AccountView _) => true
            : null;
        if (risk == L12AdminCommandRisk.High && !command.DryRun)
        {
            var validation = TournamentOperation(command.Actor, command.AuditContext, permission,
                () => operation(command, false));
            if (!validation.Success) return validation;
        }
        return _adminCommands.Execute(command, permission,
            current => TournamentOperation(current.Actor, current.AuditContext, permission,
                () => operation(current, true)),
            current => TournamentOperation(current.Actor, current.AuditContext, permission,
                () => operation(current, false)), risk, scopedAuthorization);
    }

    private L12AdminCommandResult<T> TournamentOperation<T>(L12AccountView actor, L12AdminAuditContext audit,
        L12Permission permission, Func<T> operation)
    {
        try { return L12AdminCommandResult<T>.Ok(operation()); }
        catch (L12TournamentScopeException error)
        {
            _platform.RecordAuthorizationDenied(actor,
                audit with { Outcome = "denied", Reason = "scope-denied" },
                L12Authorization.Key(permission), "scope-denied");
            return L12AdminCommandResult<T>.Fail("scope_denied", error.Message, StatusCodes.Status403Forbidden);
        }
        catch (L12TournamentVersionConflictException error)
        {
            return L12AdminCommandResult<T>.Fail("tournament_version_conflict", error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (KeyNotFoundException error)
        {
            return L12AdminCommandResult<T>.Fail("tournament_resource_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
        catch (ArgumentException error)
        {
            return L12AdminCommandResult<T>.Fail("invalid_tournament_request", error.Message,
                StatusCodes.Status400BadRequest);
        }
    }

    private IResult TournamentCommandResponse<TPayload, T>(HttpRequest request,
        L12AdminCommandEnvelope<TPayload> command, L12AdminCommandResult<T> outcome, string? tournamentId = null)
    {
        var result = AdminCommandResponse(request, command, outcome);
        var version = tournamentId is null
            ? _platform.Version
            : _platform.AdminCommandResourceVersion("tournament.read", $"tournament:{tournamentId}")
              ?? _platform.Version;
        request.HttpContext.Response.Headers.ETag = $"\"{version}\"";
        return result;
    }

    private bool TryReleaseCommandOptions(HttpRequest request, L12AccountView actor, L12Permission permission,
        string? bodyIdempotencyKey, long? bodyExpectedVersion, out string idempotencyKey,
        out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "release.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "发布写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "release.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required", "发布写操作必须提供 expectedVersion/If-Match",
                StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private bool TryReplayReleaseCommand(HttpRequest request, L12AccountView actor, L12Permission permission,
        string type, string idempotencyKey, long expectedVersion, bool dryRun, string? reason,
        Func<L12ReleaseCommandPayload, bool> matchesRequest, out IResult response)
    {
        var prior = _platform.FindAdminCommand(actor.Id, idempotencyKey);
        if (prior is null)
        {
            response = Results.Empty;
            return false;
        }

        var payload = prior.View.Payload.Deserialize<L12ReleaseCommandPayload>(CommandJsonOptions);
        if (prior.View.Type != type || prior.View.ExpectedVersion != expectedVersion
            || prior.View.DryRun != dryRun || payload is null || !matchesRequest(payload))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with
                {
                    Outcome = "rejected",
                    IdempotencyKey = idempotencyKey,
                    ExpectedVersion = expectedVersion,
                    DryRun = dryRun,
                    Reason = "idempotency-key-reused",
                }, type, request.Path, "idempotency-key-reused");
            response = ApiError(request, "idempotency_conflict", "同一幂等键已用于不同命令",
                StatusCodes.Status409Conflict);
            return true;
        }

        var command = CommandEnvelope(request, actor, permission, type, prior.View.Scope, payload,
            idempotencyKey, expectedVersion, dryRun, reason);
        var outcome = _adminCommands.Execute(command, permission,
            current => ExecuteReleaseCommand(current.Actor, current.Payload, expectedVersion,
                current.AuditContext, true),
            current => ExecuteReleaseCommand(current.Actor, current.Payload, expectedVersion,
                current.AuditContext, false), L12AdminCommandRisk.High);
        response = ReleaseCommandResponse(request, command, outcome, payload.Environment);
        return true;
    }

    private L12AdminCommandResult<L12ReleaseOperationView> ExecuteReleaseCommand(L12AccountView actor,
        L12ReleaseCommandPayload payload, long expectedVersion, L12AdminAuditContext audit, bool apply)
    {
        try
        {
            var operation = apply
                ? _platform.ExecuteRelease(actor, payload, expectedVersion, _releaseControl, audit)
                : _platform.PlanRelease(actor, payload, expectedVersion, _releaseControl);
            if (apply && operation.Run?.Status != "succeeded")
                return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_validation_failed",
                    "发布验证失败，运行记录已保存并按适配器结果执行回滚", StatusCodes.Status502BadGateway);
            return L12AdminCommandResult<L12ReleaseOperationView>.Ok(operation,
                apply ? "发布执行与验证完成" : "发布干运行验证通过");
        }
        catch (L12ReleaseVersionConflictException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_version_conflict", error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (L12ReleaseArtifactException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail(error.Code.Replace('-', '_'), error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (L12ReleaseScopeException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_scope_denied", error.Message,
                StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_resource_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
    }

    private IResult ReleaseCommandResponse<TPayload>(HttpRequest request,
        L12AdminCommandEnvelope<TPayload> command, L12AdminCommandResult<L12ReleaseOperationView> outcome,
        string environment)
    {
        var result = AdminCommandResponse(request, command, outcome);
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.ReleaseEnvironmentVersion(environment)}\"";
        return result;
    }

    private static bool IsReleaseRequestError(Exception error)
        => error is L12ReleaseScopeException or L12ReleaseVersionConflictException
            or L12ReleaseArtifactException or KeyNotFoundException or ArgumentException;

    private IResult ReleaseRequestError(HttpRequest request, L12AccountView actor, L12Permission permission,
        Exception error)
    {
        var audit = AuditContext(request, permission) with { Outcome = "rejected", Reason = error.GetType().Name };
        if (error is L12ReleaseScopeException)
        {
            _platform.RecordAuthorizationDenied(actor, audit with { Outcome = "denied", Reason = "scope-denied" },
                L12Authorization.Key(permission), "scope-denied");
            return ApiError(request, "release_scope_denied", error.Message, StatusCodes.Status403Forbidden);
        }
        _platform.RecordCommandOutcome(actor, audit, "release.prevalidation", request.Path,
            error is L12ReleaseArtifactException artifactError ? artifactError.Code : error.GetType().Name);
        return error switch
        {
            L12ReleaseVersionConflictException => ApiError(request, "release_version_conflict", error.Message,
                StatusCodes.Status409Conflict),
            L12ReleaseArtifactException artifact => ApiError(request, artifact.Code.Replace('-', '_'),
                artifact.Message, StatusCodes.Status409Conflict),
            KeyNotFoundException => ApiError(request, "release_resource_not_found", error.Message,
                StatusCodes.Status404NotFound),
            _ => ApiError(request, "invalid_release_request", error.Message, StatusCodes.Status400BadRequest),
        };
    }

    private L12ContentRollbackCommandPayload CaptureContentRollbackForCommand(HttpRequest request,
        L12AccountView actor, ContentRollbackRequest body)
    {
        var batchId = body.BatchId?.Trim() ?? string.Empty;
        var rawIdempotencyKey = string.IsNullOrWhiteSpace(body.IdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()
            : body.IdempotencyKey;
        var idempotencyKey = rawIdempotencyKey?.Trim();
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var prior = _platform.FindAdminCommand(actor.Id, idempotencyKey);
            if (prior is not null)
            {
                if (string.Equals(prior.View.Type, "content.rollback.batch", StringComparison.Ordinal)
                    && prior.View.Payload.Deserialize<L12ContentRollbackCommandPayload>(CommandJsonOptions) is { } stored
                    && string.Equals(stored.BatchId, batchId, StringComparison.Ordinal))
                    return stored;

                // 让命令总线统一返回幂等键冲突；无需在进入总线前读取或验证另一批次。
                return new L12ContentRollbackCommandPayload(batchId, []);
            }
        }

        return _platform.CaptureContentRollback(batchId);
    }

    private IResult AdminCommandResponse<TPayload, T>(HttpRequest request,
        L12AdminCommandEnvelope<TPayload> command, L12AdminCommandResult<T> outcome)
    {
        request.HttpContext.Response.Headers["X-Command-ID"] = outcome.Command?.Id ?? command.CommandId;
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.Version}\"";
        if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
        if (outcome.Pending)
            return Results.Json(new
            {
                commandId = outcome.Command?.Id ?? command.CommandId,
                status = "requested",
                outcome.Message,
                command = outcome.Command,
            }, statusCode: StatusCodes.Status202Accepted);
        return outcome.Success
            ? Results.Ok(outcome.Value)
            : ApiError(request, outcome.Code, outcome.Message, outcome.StatusCode);
    }

    private IResult AdminReviewResponse(HttpRequest request,
        L12AdminCommandResult<L12AdminCommandView> outcome)
    {
        if (outcome.Command is not null) request.HttpContext.Response.Headers["X-Command-ID"] = outcome.Command.Id;
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.Version}\"";
        if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
        return outcome.Success
            ? Results.Ok(outcome.Value)
            : ApiError(request, outcome.Code, outcome.Message, outcome.StatusCode);
    }

    private L12AdminCommandResult<L12ContentBatchOperationView> ExecuteContentPublish(
        L12AdminCommandEnvelope<L12ContentPublishCommandPayload> command, L12Permission permission)
        => _adminCommands.Execute(command, permission, current =>
        {
            try
            {
                var batch = _platform.PublishContentBatch(current.Actor, current.Payload, current.AuditContext);
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(true, batch, null), "内容批次已发布");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
        }, current =>
        {
            try
            {
                var preview = _platform.PreviewContentPublish(current.Payload);
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(false, null, preview), "干运行验证通过");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
        }, L12AdminCommandRisk.High);

    private L12AdminCommandResult<L12ContentBatchOperationView> ExecuteContentRollback(
        L12AdminCommandEnvelope<L12ContentRollbackCommandPayload> command, L12Permission permission)
        => _adminCommands.Execute(command, permission, current =>
        {
            try
            {
                var batch = _platform.RollbackContentBatch(current.Actor, current.Payload, current.AuditContext);
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(true, batch, null), "内容批次已回滚");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_batch_not_found",
                    error.Message, StatusCodes.Status404NotFound);
            }
        }, current =>
        {
            try
            {
                var preview = _platform.PreviewContentRollback(current.Payload);
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(false, null, preview), "干运行验证通过");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
        }, L12AdminCommandRisk.High);

    private L12AdminCommandResult<L12EffectReviewView> ExecuteEffectReview(
        L12AdminCommandEnvelope<EffectReviewCommandPayload> command)
        => ValidateEffectReview(command, true);

    private L12AdminCommandResult<L12EffectReviewView> ValidateEffectReview(
        L12AdminCommandEnvelope<EffectReviewCommandPayload> command, bool apply)
    {
        var effect = _catalog.AtomicEffects.Find(command.Payload.CardId);
        if (effect is null)
            return L12AdminCommandResult<L12EffectReviewView>.Fail("effect_not_found", "卡牌效果不存在",
                StatusCodes.Status404NotFound);
        var ability = command.Payload.AbilityId is null ? null : effect.Abilities
            .FirstOrDefault(item => string.Equals(item.AbilityId, command.Payload.AbilityId,
                StringComparison.OrdinalIgnoreCase));
        if (command.Payload.AbilityId is not null && ability is null)
            return L12AdminCommandResult<L12EffectReviewView>.Fail("stale_ability_id",
                "能力标识已过期，请刷新后重新审查", StatusCodes.Status409Conflict);
        if (command.Payload.Status is not ("unreviewed" or "human-assisted" or "confirmed" or "rejected"))
            return L12AdminCommandResult<L12EffectReviewView>.Fail("invalid_review_status", "无效的审查状态",
                StatusCodes.Status400BadRequest);
        if (!apply)
            return L12AdminCommandResult<L12EffectReviewView>.Ok(new L12EffectReviewView(command.Payload.CardId,
                command.Payload.AbilityId, command.Payload.Status, command.Payload.Note?.Trim() ?? string.Empty,
                command.Actor.Username, DateTimeOffset.UtcNow, ability?.StructureHash ?? string.Empty), "干运行验证通过");
        return L12AdminCommandResult<L12EffectReviewView>.Ok(_platform.SaveEffectReview(command.Actor,
            command.Payload.CardId, command.Payload.AbilityId, command.Payload.Status, command.Payload.Note,
            ability?.StructureHash, command.AuditContext), "审查记录已保存");
    }

    private L12AdminCommandResult<L12AccountStatusOperationView> ExecuteAccountStatus(L12AccountView actor,
        L12AccountStatusCommandPayload payload, L12AdminAuditContext audit, bool apply)
    {
        try
        {
            var operation = _platform.SetAccountDisabled(actor, payload.AccountId, payload.Disabled,
                payload.Reason, audit, apply);
            return L12AdminCommandResult<L12AccountStatusOperationView>.Ok(operation,
                apply ? "账号状态已更新" : "干运行验证通过");
        }
        catch (L12SecurityPolicyException error)
        {
            var status = error.Code == "security_reason_required"
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status403Forbidden;
            return L12AdminCommandResult<L12AccountStatusOperationView>.Fail(error.Code, error.Message, status);
        }
        catch (KeyNotFoundException error)
        {
            return L12AdminCommandResult<L12AccountStatusOperationView>.Fail("account_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
    }

    private L12AdminCommandResult<L12AuditArchiveOperationView> ExecuteAuditArchive(L12AccountView actor,
        L12AuditArchiveCommandPayload payload, L12AdminAuditContext audit, bool apply)
    {
        try
        {
            var operation = _platform.ArchiveAudit(actor, payload, audit, apply);
            return L12AdminCommandResult<L12AuditArchiveOperationView>.Ok(operation,
                apply ? "审计归档段已生成，独立事件源保持不变" : "干运行验证通过");
        }
        catch (L12SecurityPolicyException error)
        {
            return L12AdminCommandResult<L12AuditArchiveOperationView>.Fail(error.Code, error.Message,
                error.Code == "audit_unavailable"
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status400BadRequest);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return L12AdminCommandResult<L12AuditArchiveOperationView>.Fail("audit_archive_failed",
                error.Message, StatusCodes.Status503ServiceUnavailable);
        }
    }

    private L12AdminCommandResult<JsonElement> ExecuteApprovedCommand(L12AdminCommandView command,
        L12AccountView requester, L12AdminAuditContext audit)
    {
        switch (command.Type)
        {
            case "account.role.set":
            {
                var payload = command.Payload.Deserialize<RoleCommandPayload>(CommandJsonOptions);
                if (payload is null)
                    return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload", "命令载荷无效",
                        StatusCodes.Status400BadRequest);
                var result = _platform.SetRole(requester, payload.AccountId, payload.Role, audit)
                    ? L12AdminCommandResult<RoleCommandResult>.Ok(
                        new RoleCommandResult(payload.AccountId, payload.Role, true), "账号角色已更新")
                    : L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "账号或角色无效",
                        StatusCodes.Status400BadRequest);
                return ToJsonResult(result);
            }
            case "account.status.set":
            {
                var payload = command.Payload.Deserialize<L12AccountStatusCommandPayload>(CommandJsonOptions);
                if (payload is null)
                    return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload", "命令载荷无效",
                        StatusCodes.Status400BadRequest);
                return ToJsonResult(ExecuteAccountStatus(requester, payload, audit, true));
            }
            case "content.publish.batch":
            {
                var payload = command.Payload.Deserialize<L12ContentPublishCommandPayload>(CommandJsonOptions);
                if (payload is null)
                    return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload", "命令载荷无效",
                        StatusCodes.Status400BadRequest);
                try
                {
                    var batch = _platform.PublishContentBatch(requester, payload, audit);
                    return ToJsonResult(L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                        new L12ContentBatchOperationView(true, batch, null), "内容批次已发布"));
                }
                catch (L12ContentStateConflictException error)
                {
                    return L12AdminCommandResult<JsonElement>.Fail("content_version_conflict", error.Message,
                        StatusCodes.Status409Conflict);
                }
            }
            case "content.rollback.batch":
            {
                var payload = command.Payload.Deserialize<L12ContentRollbackCommandPayload>(CommandJsonOptions);
                if (payload is null)
                    return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload", "命令载荷无效",
                        StatusCodes.Status400BadRequest);
                try
                {
                    var batch = _platform.RollbackContentBatch(requester, payload, audit);
                    return ToJsonResult(L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                        new L12ContentBatchOperationView(true, batch, null), "内容批次已回滚"));
                }
                catch (L12ContentStateConflictException error)
                {
                    return L12AdminCommandResult<JsonElement>.Fail("content_version_conflict", error.Message,
                        StatusCodes.Status409Conflict);
                }
                catch (KeyNotFoundException error)
                {
                    return L12AdminCommandResult<JsonElement>.Fail("content_batch_not_found", error.Message,
                        StatusCodes.Status404NotFound);
                }
            }
            case "tournament.staff.set":
                return ExecuteApprovedTournament<TournamentStaffCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentsManage,
                    (payload, expected) => _platform.SetTournamentStaff(requester, payload.TournamentId,
                        payload.Staff, expected, audit, true));
            case "tournament.start":
                return ExecuteApprovedTournament<TournamentTargetCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentsManage,
                    (payload, expected) => _platform.StartTournament(requester, payload.TournamentId,
                        expected, audit, true));
            case "tournament.round.create":
                return ExecuteApprovedTournament<TournamentTargetCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentsManage,
                    (payload, expected) => _platform.CreateNextRound(requester, payload.TournamentId,
                        expected, audit, true));
            case "tournament.round.start":
                return ExecuteApprovedTournament<TournamentRoundCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentsManage,
                    (payload, expected) => _platform.StartTournamentRound(requester, payload.TournamentId,
                        payload.RoundNumber, expected, audit, true));
            case "tournament.ruling.apply":
                return ExecuteApprovedTournament<TournamentRulingCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentRulingsWrite,
                    (payload, expected) => _platform.ApplyTournamentRuling(requester, payload.TournamentId,
                        payload.MatchId, payload.Ruling, expected, audit, true));
            case "tournament.match.reference":
                return ExecuteApprovedTournament<TournamentReferenceCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentRulingsWrite,
                    (payload, expected) => _platform.LinkTournamentMatch(requester, payload.TournamentId,
                        payload.MatchId, payload.Reference, expected, audit, true));
            case "tournament.complete":
                return ExecuteApprovedTournament<TournamentTargetCommandPayload, L12TournamentView>(command,
                    requester, audit, L12Permission.TournamentsManage,
                    (payload, expected) => _platform.CompleteTournament(requester, payload.TournamentId,
                        expected, audit, true));
            case "release.deploy":
            case "release.rollback":
            {
                var payload = command.Payload.Deserialize<L12ReleaseCommandPayload>(CommandJsonOptions);
                if (payload is null || command.ExpectedVersion is not { } expected)
                    return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload",
                        "发布命令载荷无效", StatusCodes.Status400BadRequest);
                return ToJsonResult(ExecuteReleaseCommand(requester, payload, expected, audit, true));
            }
            case "security.audit.archive":
            {
                var payload = command.Payload.Deserialize<L12AuditArchiveCommandPayload>(CommandJsonOptions);
                if (payload is null)
                    return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload", "命令载荷无效",
                        StatusCodes.Status400BadRequest);
                return ToJsonResult(ExecuteAuditArchive(requester, payload, audit, true));
            }
            default:
                return L12AdminCommandResult<JsonElement>.Fail("unsupported_command_type",
                    "该命令类型不支持审批执行", StatusCodes.Status409Conflict);
        }
    }

    private static L12AdminCommandResult<JsonElement> ToJsonResult<T>(L12AdminCommandResult<T> result)
    {
        if (!result.Success)
            return L12AdminCommandResult<JsonElement>.Fail(result.Code, result.Message, result.StatusCode);
        return new L12AdminCommandResult<JsonElement>(true, result.Code, result.Message,
            JsonSerializer.SerializeToElement(result.Value, CommandJsonOptions), result.StatusCode);
    }

    private L12AdminCommandResult<JsonElement> ExecuteApprovedTournament<TPayload, TResult>(
        L12AdminCommandView command, L12AccountView requester, L12AdminAuditContext audit,
        L12Permission permission, Func<TPayload, long, TResult> operation)
    {
        var payload = command.Payload.Deserialize<TPayload>(CommandJsonOptions);
        if (payload is null || command.ExpectedVersion is not { } expected)
            return L12AdminCommandResult<JsonElement>.Fail("invalid_command_payload", "赛事命令载荷无效",
                StatusCodes.Status400BadRequest);
        return ToJsonResult(TournamentOperation(requester, audit, permission,
            () => operation(payload, expected)));
    }

    private static L12AdminCommandResult<L12SessionRevocationResult> SessionCommandResult(
        L12SessionRevocationResult result)
        => result.Found
            ? L12AdminCommandResult<L12SessionRevocationResult>.Ok(result)
            : L12AdminCommandResult<L12SessionRevocationResult>.Fail("session_not_found", "账号或会话不存在",
                StatusCodes.Status404NotFound);

    private IResult AdminSessionCommandResponse(HttpRequest request,
        L12AdminCommandEnvelope<SessionCommandPayload> command,
        L12AdminCommandResult<L12SessionRevocationResult> outcome)
    {
        if (!outcome.Success) return AdminCommandResponse(request, command, outcome);
        request.HttpContext.Response.Headers["X-Command-ID"] = command.CommandId;
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.Version}\"";
        if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
        return SessionRevocationResponse(request, outcome.Value!);
    }

    private static long? ParseExpectedVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..].Trim();
        normalized = normalized.Trim('"');
        return long.TryParse(normalized, out var parsed) && parsed >= 0 ? parsed : null;
    }

    private void HandlePlatformSessionsRevoked(IReadOnlyList<string> sessionIds)
    {
        var revoked = sessionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var mapping in _socketPlatformSessions.Where(item => revoked.Contains(item.Value)).ToArray())
        {
            _socketPlatformSessions.TryRemove(mapping.Key, out _);
            if (_sockets.TryGetValue(mapping.Key, out var socket)) socket.Abort();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _platform.SessionsRevoked -= HandlePlatformSessionsRevoked;
        if (_app is not null) await _app.DisposeAsync();
    }
}

public sealed record AuthRequest(string? Username, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record FriendRequest(string? AccountId);
public sealed record FriendResolveRequest(bool Accept);
public sealed record RoleRequest(string? Role, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record RoleCommandPayload(string AccountId, string Role);
public sealed record RoleCommandResult(string AccountId, string Role, bool Changed);
public sealed record AccountStatusRequest(bool Disabled, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record OperationsConfigPreviewRequest(L12OperationsConfigPayload Config,
    long? ExpectedVersion = null);
public sealed record OperationsConfigApplyRequest(L12OperationsConfigPayload Config, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record OperationsConfigRollbackRequest(string? VersionId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12OperationsRollbackCommandPayload(string VersionId);
public sealed record AuditArchiveRequest(int? RetentionDays = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record SessionCommandPayload(string AccountId, string? SessionId);
public sealed record ContentRequest(string? Value, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record ContentDraftCommandPayload(string Key, string Value);
public sealed record ContentBatchRequest(IReadOnlyList<string>? Keys, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record ContentRollbackRequest(string? BatchId, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectReviewRequest(string? AbilityId, string? Status, string? Note,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectReviewCommandPayload(string CardId, string? AbilityId, string Status, string? Note);
public sealed record BugRequest(string? Title, string Description, string? Page, string? RoomCode, string? MatchId, string? Version);
public sealed record BugUpdateRequest(string? Status, string? Priority, string? Assignee, string? AdminNotes,
    string? Comment, string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false,
    string? Reason = null);
public sealed record BugUpdateCommandPayload(string Id, string? Status, string? Priority, string? Assignee,
    string? AdminNotes, string? Comment);
public sealed record PublishedDeckRequest(string? PublicationId, L12CustomDeckSubmission? Deck);
public sealed record TournamentCreateRequest(L12TournamentCreatePayload Tournament, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentLegacyImportRequest(IReadOnlyList<L12LegacyTournamentInput>? Tournaments,
    string? PreviewHash = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentRegistrationRequest(string? DeckName, string? DeckCode,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentActionRequest(string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentStaffRequest(IReadOnlyList<string>? RefereeAccountIds,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentCheckInRequest(string? AccountId, bool Ready,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentPauseRequest(bool Paused, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentTimeExtensionRequest(int Minutes, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRulingRequest(string? Kind, string? TargetAccountId, string? Decision,
    string? Reason, string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentMatchReferenceRequest(string? RecordedMatchId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentEmptyPayload;
public sealed record TournamentTargetCommandPayload(string TournamentId);
public sealed record TournamentRoundCommandPayload(string TournamentId, int RoundNumber);
public sealed record TournamentStaffCommandPayload(string TournamentId, L12TournamentStaffPayload Staff);
public sealed record TournamentRulingCommandPayload(string TournamentId, string MatchId,
    L12TournamentRulingPayload Ruling);
public sealed record TournamentReferenceCommandPayload(string TournamentId, string MatchId,
    L12TournamentMatchReferencePayload Reference);
public sealed record ReleaseDeployRequest(string? ArtifactId, string? Environment,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record ReleaseRollbackRequest(string? TargetRunId,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
