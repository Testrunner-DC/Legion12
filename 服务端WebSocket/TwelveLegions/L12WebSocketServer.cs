using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwelveLegions.Server;

public sealed class L12WebSocketServer : IAsyncDisposable
{
    private readonly L12RoomManager _rooms;
    private readonly MatchRecorder _recorder;
    private readonly L12PlatformStore _platform;
    private readonly L12Catalog _catalog;
    private readonly int _cardCount;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private WebApplication? _app;

    public L12WebSocketServer(L12RoomManager rooms, MatchRecorder recorder, L12PlatformStore platform, L12Catalog catalog)
    {
        _rooms = rooms;
        _recorder = recorder;
        _platform = platform;
        _catalog = catalog;
        _cardCount = catalog.Cards.Count;
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
            var origin = context.Request.Headers.Origin.ToString();
            if (IsAllowedOrigin(origin)) context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Vary = "Origin";
            context.Response.Headers.AccessControlAllowHeaders = "Content-Type, Authorization";
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
            if (HttpMethods.IsOptions(context.Request.Method)) { context.Response.StatusCode = StatusCodes.Status204NoContent; return; }
            await next();
        });
        _app.UseWebSockets();
        _app.MapGet("/health", () => Results.Ok(new { service = "twelve-legions", cards = _cardCount }));
        _app.MapGet("/api/matches", async (int? limit) => Results.Ok(await _recorder.ListMatchesAsync(limit ?? 50)));
        _app.MapGet("/api/matches/{matchId}", async (string matchId) =>
        {
            var match = await _recorder.GetMatchAsync(matchId);
            return match is null ? Results.NotFound() : Results.Ok(match);
        });
        _app.MapPost("/api/auth/register", (AuthRequest request) =>
        {
            var result = _platform.Register(request.Username ?? string.Empty, request.Password ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message, result.Account, result.Token }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/auth/login", (AuthRequest request) =>
        {
            var result = _platform.Login(request.Username ?? string.Empty, request.Password ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message, result.Account, result.Token }) : Results.BadRequest(new { result.Message });
        });
        _app.MapGet("/api/auth/me", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(account);
        });
        _app.MapPost("/api/auth/change-password", (HttpRequest request, ChangePasswordRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var result = _platform.ChangePassword(account.Id, body.CurrentPassword ?? string.Empty, body.NewPassword ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
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
            if (!L12DeckValidator.TryValidate(_catalog, submission, out var deck, out var error))
                return Results.BadRequest(new { message = error });
            return Results.Ok(_platform.UpsertDeck(account.Id, deck));
        });
        _app.MapDelete("/api/decks/{name}", (HttpRequest request, string name) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return _platform.DeleteDeck(account.Id, name) ? Results.Ok() : Results.NotFound();
        });
        _app.MapPost("/api/bugs", (HttpRequest request, BugRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Description)) return Results.BadRequest(new { message = "请填写问题描述" });
            var account = _platform.Authenticate(request.Headers.Authorization);
            return Results.Ok(_platform.AddBug(account, body.Title ?? string.Empty, body.Description, body.Page ?? string.Empty,
                body.RoomCode, body.MatchId, body.Version ?? "dev"));
        });
        _app.MapGet("/api/admin/accounts", (HttpRequest request) =>
            IsAdmin(request) ? Results.Ok(_platform.Accounts()) : Results.Unauthorized());
        _app.MapPut("/api/admin/accounts/{id}/role", (HttpRequest request, string id, RoleRequest body) =>
            !IsAdmin(request) ? Results.Unauthorized() : _platform.SetRole(id, body.Role ?? string.Empty) ? Results.Ok() : Results.BadRequest());
        _app.MapGet("/api/admin/bugs", (HttpRequest request, string? status) =>
            IsAdmin(request) ? Results.Ok(_platform.Bugs(status)) : Results.Unauthorized());
        _app.MapPatch("/api/admin/bugs/{id}", (HttpRequest request, string id, BugUpdateRequest body) =>
        {
            if (!IsAdmin(request)) return Results.Unauthorized();
            var updated = _platform.UpdateBug(id, body.Status, body.Priority, body.Assignee, body.AdminNotes);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });
        _app.MapGet("/api/content/{key}", (string key) => Results.Ok(new { key, value = _platform.GetContent(key) }));
        _app.MapPut("/api/admin/content/{key}", (HttpRequest request, string key, ContentRequest body) =>
        {
            if (!IsAdmin(request)) return Results.Unauthorized();
            _platform.SetContent(key, body.Value ?? string.Empty);
            return Results.Ok(new { key, body.Value });
        });
        _app.MapGet("/api/admin/effect-atoms", (HttpRequest request) =>
            IsAdmin(request) ? Results.Ok(L12EffectAtomRegistry.All) : Results.Unauthorized());
        _app.MapGet("/api/admin/effects/coverage", (HttpRequest request) =>
            IsAdmin(request) ? Results.Ok(_catalog.AtomicEffects.Coverage()) : Results.Unauthorized());
        _app.MapGet("/api/admin/effects", (HttpRequest request, string? search, string? status, string? product,
            string? atomKind, int? page, int? pageSize) =>
            IsAdmin(request)
                ? Results.Ok(_catalog.AtomicEffects.Query(search, status, product, atomKind, page ?? 1, pageSize ?? 50))
                : Results.Unauthorized());
        _app.MapGet("/api/admin/effects/{cardId}", (HttpRequest request, string cardId) =>
        {
            if (!IsAdmin(request)) return Results.Unauthorized();
            var effect = _catalog.AtomicEffects.Find(cardId);
            return effect is null ? Results.NotFound() : Results.Ok(effect);
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
            IReadOnlyList<OutgoingMessage> outgoing = typeElement.GetString() switch
            {
                "hello" => AuthenticateSession(sessionId, root),
                "createRoom" => CreateRoom(sessionId, root),
                "createSandbox" => await CreateSandboxAsync(sessionId, root),
                "joinRoom" => _rooms.JoinRoom(sessionId, GetString(root, "roomCode")),
                "spectateRoom" => _rooms.SpectateRoom(sessionId, GetString(root, "roomCode")),
                "leaveRoom" => _rooms.LeaveRoom(sessionId),
                "selectDeck" => _rooms.SelectDeck(sessionId, GetInt(root, "deckIndex")),
                "selectCustomDeck" when root.TryGetProperty("deck", out var deckElement)
                    => SelectCustomDeck(sessionId, deckElement),
                "ready" => await _rooms.SetReadyAsync(sessionId, GetBool(root, "ready", true)),
                "gameAction" when root.TryGetProperty("command", out var command) => await _rooms.HandleActionAsync(sessionId, command),
                "gmAction" when root.TryGetProperty("command", out var gmCommand) => await _rooms.HandleGmActionAsync(sessionId, gmCommand),
                "ping" => [new OutgoingMessage(sessionId, new { type = "pong", utc = DateTimeOffset.UtcNow })],
                _ => [new OutgoingMessage(sessionId, new { type = "error", message = "未知消息类型" })],
            };
            await SendManyAsync(outgoing, cancellationToken);
        }
    }

    private IReadOnlyList<OutgoingMessage> AuthenticateSession(Guid sessionId, JsonElement root)
    {
        var account = _platform.AuthenticateToken(GetString(root, "authToken"));
        return account is null
            ? [new OutgoingMessage(sessionId, new { type = "authenticationRequired", message = "请先登录账号" })]
            : [new OutgoingMessage(sessionId, _rooms.Connect(sessionId, account.Username))];
    }

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

    private bool IsAdmin(HttpRequest request) => _platform.Authenticate(request.Headers.Authorization)?.Role == "admin";

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }
}

public sealed record AuthRequest(string? Username, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record RoleRequest(string? Role);
public sealed record ContentRequest(string? Value);
public sealed record BugRequest(string? Title, string Description, string? Page, string? RoomCode, string? MatchId, string? Version);
public sealed record BugUpdateRequest(string? Status, string? Priority, string? Assignee, string? AdminNotes);
