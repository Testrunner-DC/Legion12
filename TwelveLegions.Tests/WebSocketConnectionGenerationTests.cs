using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class WebSocketConnectionGenerationTests
{
    [Fact]
    public async Task HealthAndLegacyBugSubmissionExposeAuthoritativeBuildsAndWhitelistDiagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-bug-api-diagnostic", Guid.NewGuid().ToString("N"));
        var platformPath = Path.Combine(directory, "platform.json");
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(platformPath, catalog.PresetDecks, officialCards: catalog.Cards);
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var endpoint = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
            using var client = new HttpClient { BaseAddress = endpoint };
            var health = await client.GetFromJsonAsync<JsonElement>("/health");
            Assert.Equal("ok", health.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(health.GetProperty("serverVersion").GetString()));
            Assert.StartsWith("l12-engine/", health.GetProperty("engineVersion").GetString());

            using var response = await client.PostAsJsonAsync("/api/bugs", new
            {
                title = "legacy-compatible",
                description = "temporary test report",
                page = "/me",
                clientDiagnostic = new
                {
                    capturedAt = DateTimeOffset.UtcNow,
                    currentRoute = "/me",
                    httpStatus = "ok:must-never-be-persisted",
                    httpStatusCode = 200,
                    apiStatus = "authenticated:must-never-be-persisted",
                    apiStatusCode = 200,
                    webSocketReadyState = "closed:must-never-be-persisted",
                    closeCode = 1006,
                    closeReason = "must-never-be-persisted",
                    retryCount = 1,
                    recoveryPhase = "disconnected:must-never-be-persisted",
                    authenticationState = "authenticated:must-never-be-persisted",
                    maintenanceState = "inactive:must-never-be-persisted",
                    token = "must-never-be-persisted",
                    privateHand = new[] { "S01-0001" },
                    ip = "203.0.113.5",
                },
            });
            response.EnsureSuccessStatusCode();
            var report = await response.Content.ReadFromJsonAsync<L12BugReportView>();
            Assert.NotNull(report);
            Assert.Equal("unknown-client", report.ClientVersion);
            Assert.False(string.IsNullOrWhiteSpace(report.ServerVersion));
            Assert.False(string.IsNullOrWhiteSpace(report.EngineVersion));
            Assert.Equal("unknown", report.ClientDiagnostic?.HttpStatus);
            Assert.Equal("unknown", report.ClientDiagnostic?.ApiStatus);
            Assert.Equal("unknown", report.ClientDiagnostic?.WebSocketReadyState);
            Assert.Equal("unknown", report.ClientDiagnostic?.CloseReason);
            Assert.Equal("unknown", report.ClientDiagnostic?.RecoveryPhase);
            Assert.Equal("unknown", report.ClientDiagnostic?.AuthenticationState);
            Assert.Equal("unknown", report.ClientDiagnostic?.MaintenanceState);
            Assert.DoesNotMatch(@"^l12-engine/1\.0\.0(?:\.0)?$", report.EngineVersion);
            var persisted = File.ReadAllText(platformPath);
            Assert.DoesNotContain("must-never-be-persisted", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain("203.0.113.5", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain("S01-0001", persisted, StringComparison.Ordinal);
        }
        finally { await server.StopAsync(); }
    }

    [Fact]
    public async Task NewSocketGenerationFencesTheOlderLiveSocketWithoutRestartingTheServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-ws-generation", Guid.NewGuid().ToString("N"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var account = platform.Register("ws-generation-player", "Password123!").Account!;
        var token = platform.Login(account.Username, "Password123!").Token!;
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var ws = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            using var first = new ClientWebSocket();
            await first.ConnectAsync(ws, CancellationToken.None);
            await SendAsync(first, new { type = "hello", authToken = token });
            var firstSession = await ReceiveTypeAsync(first, "session");
            await ReceiveTypeAsync(first, "recoveryComplete");

            using var second = new ClientWebSocket();
            await second.ConnectAsync(ws, CancellationToken.None);
            await SendAsync(second, new { type = "hello", authToken = token });
            var secondSession = await ReceiveTypeAsync(second, "session");
            var secondAck = await ReceiveTypeAsync(second, "recoveryComplete");
            var superseded = await ReceiveTypeAsync(first, "sessionSuperseded");

            Assert.Equal(firstSession.GetProperty("connectionGeneration").GetInt64() + 1,
                secondSession.GetProperty("connectionGeneration").GetInt64());
            Assert.Equal("fenced-active-session", secondSession.GetProperty("claimDecision").GetString());
            Assert.Equal(secondSession.GetProperty("connectionGeneration").GetInt64(),
                secondAck.GetProperty("connectionGeneration").GetInt64());
            Assert.Equal("newer-connection-generation", superseded.GetProperty("reason").GetString());

            try
            {
                if (first.State == WebSocketState.Open)
                    await SendAsync(first, new { type = "createRoom" });
            }
            catch (Exception error) when (error is WebSocketException or InvalidOperationException)
            {
                // A socket already fenced at transport level is also an accepted rejection path.
            }
            await SendAsync(second, new { type = "syncState" });
            var authorityCheck = await ReceiveTypeAsync(second, "recoveryComplete");
            Assert.Equal(JsonValueKind.Null, authorityCheck.GetProperty("roomCode").ValueKind);
            await SendAsync(second, new { type = "createRoom" });
            var authoritativeRoom = await ReceiveTypeAsync(second, "roomState");
            Assert.False(string.IsNullOrWhiteSpace(authoritativeRoom.GetProperty("roomCode").GetString()));

            var diagnostic = Assert.IsType<L12ConnectionClaimDiagnosticView>(
                manager.CaptureConnectionClaimDiagnostic(account.Id));
            Assert.Equal("fenced-active-session", diagnostic.Decision);
            Assert.Equal("older-connection-fenced", diagnostic.RejectionReason);
        }
        finally { await server.StopAsync(); }
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveTypeAsync(ClientWebSocket socket, string expectedType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var payload = await ReceiveAsync(socket, timeout.Token);
            Assert.NotNull(payload);
            if (payload.Value.GetProperty("type").GetString() == expectedType) return payload.Value;
        }
        throw new Xunit.Sdk.XunitException($"WebSocket did not receive {expectedType}");
    }

    private static async Task<JsonElement?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            return JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray())).RootElement.Clone();
        }
    }
}
