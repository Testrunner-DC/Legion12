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
    private readonly int _cardCount;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private WebApplication? _app;

    public L12WebSocketServer(L12RoomManager rooms, MatchRecorder recorder, int cardCount)
    {
        _rooms = rooms;
        _recorder = recorder;
        _cardCount = cardCount;
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
            context.Response.Headers.AccessControlAllowOrigin = "*";
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
            await SendAsync(sessionId, _rooms.Connect(sessionId, null), cancellationToken);
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
                "hello" => [new OutgoingMessage(sessionId, _rooms.Connect(sessionId, GetString(root, "name")))],
                "createRoom" => CreateRoom(sessionId, root),
                "joinRoom" => _rooms.JoinRoom(sessionId, GetString(root, "roomCode")),
                "spectateRoom" => _rooms.SpectateRoom(sessionId, GetString(root, "roomCode")),
                "selectDeck" => _rooms.SelectDeck(sessionId, GetInt(root, "deckIndex")),
                "selectCustomDeck" when root.TryGetProperty("deck", out var deckElement)
                    => SelectCustomDeck(sessionId, deckElement),
                "ready" => await _rooms.SetReadyAsync(sessionId, GetBool(root, "ready", true)),
                "gameAction" when root.TryGetProperty("command", out var command) => await _rooms.HandleActionAsync(sessionId, command),
                "ping" => [new OutgoingMessage(sessionId, new { type = "pong", utc = DateTimeOffset.UtcNow })],
                _ => [new OutgoingMessage(sessionId, new { type = "error", message = "未知消息类型" })],
            };
            await SendManyAsync(outgoing, cancellationToken);
        }
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

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }
}
