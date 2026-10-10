using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class ExpiredWebSocketSessionTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NaturallyExpiredSessionReleasesOwnerAndClosesAfterAuthenticationRequired()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-expired-websocket", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var username = $"ix{Guid.NewGuid():N}"[..10];
            var registered = platform.Register(username, "Password123!");
            var authenticated = platform.AuthenticateTokenSession(registered.Token)!;
            var admin = platform.Login("Admin", "L12master").Account!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            using var socket = new ClientWebSocket();
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                await socket.ConnectAsync(endpoint, CancellationToken.None);
                await SendAsync(socket, new { type = "hello", authToken = registered.Token });
                _ = await ReceiveTypeAsync(socket, "session", TimeSpan.FromSeconds(10));
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                await SendAsync(socket, new { type = "createRoom" });
                _ = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));

                var owners = Assert.IsType<ConcurrentDictionary<string, Guid>>(typeof(L12WebSocketServer)
                    .GetField("_activeAccountSockets", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(server));
                Assert.True(owners.ContainsKey(authenticated.Account.Id));
                var expiredTransportId = owners[authenticated.Account.Id];

                ExpireCommittedSession(platform, authenticated.SessionId, authenticated.Account.Id, admin);
                Assert.False(platform.IsSessionActive(authenticated.SessionId));
                await SendAsync(socket, new { type = "ping" });
                var rejected = await ReceiveTypeAsync(socket, "authenticationRequired", TimeSpan.FromSeconds(2));
                Assert.Equal("platform-session-revoked", rejected.GetProperty("reason").GetString());
                Assert.False(owners.ContainsKey(authenticated.Account.Id));
                Assert.Equal(WebSocketCloseStatus.PolicyViolation,
                    await ReceiveCloseAsync(socket, TimeSpan.FromSeconds(2)));

                Assert.False(ServerMapContains(server, "_sockets", expiredTransportId));
                Assert.False(ServerMapContains(server, "_outboundConnections", expiredTransportId));

                var replacementLogin = platform.Login(username, "Password123!");
                Assert.True(replacementLogin.Success);
                using var replacement = new ClientWebSocket();
                await replacement.ConnectAsync(endpoint, CancellationToken.None);
                await SendAsync(replacement, new { type = "hello", authToken = replacementLogin.Token });
                var replacementSession = await ReceiveTypeAsync(replacement, "session",
                    TimeSpan.FromSeconds(10));
                _ = await ReceiveTypeAsync(replacement, "recoveryComplete", TimeSpan.FromSeconds(10));
                var replacementTransportId = replacementSession.GetProperty("sessionId").GetGuid();
                Assert.Equal(replacementTransportId, owners[authenticated.Account.Id]);

                // The retired transport no longer participates in room publication. The new token
                // and new connection can recover and request a fresh authoritative room snapshot.
                await SendAsync(replacement, new { type = "syncState" });
                _ = await ReceiveTypeAsync(replacement, "recoveryComplete", TimeSpan.FromSeconds(10));
            }
            finally
            {
                socket.Abort();
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-expired-websocket"); }
    }

    [Fact]
    public void ExpectedBindingRemovalCannotDeleteReplacementOnSameTransportId()
    {
        var serverType = typeof(L12WebSocketServer);
        var bindingType = serverType.GetNestedType("SocketPlatformBinding", BindingFlags.NonPublic)!;
        var dictionaryType = typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(Guid), bindingType);
        var bindings = Activator.CreateInstance(dictionaryType)!;
        var transportId = Guid.NewGuid();
        var oldBinding = Activator.CreateInstance(bindingType, "old-session", "same-account", 1L)!;
        var replacement = Activator.CreateInstance(bindingType, "new-session", "same-account", 2L)!;
        dictionaryType.GetProperty("Item")!.SetValue(bindings, replacement, [transportId]);
        var release = serverType.GetMethod("TryReleaseSocketPlatformBinding",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.False((bool)release.Invoke(null, [bindings, transportId, oldBinding])!);
        Assert.Same(replacement, dictionaryType.GetProperty("Item")!.GetValue(bindings,
            [transportId]));
        Assert.True((bool)release.Invoke(null, [bindings, transportId, replacement])!);
        Assert.Equal(0, (int)dictionaryType.GetProperty("Count")!.GetValue(bindings)!);
    }

    [Fact]
    public async Task ValidHelloCannotClaimTransportMissingFromPublicationTables()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-fenced-websocket",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            var registration = platform.Register($"fh{Guid.NewGuid():N}"[..10], "Password123!");
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            var transportId = Guid.NewGuid();
            using var hello = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                authToken = registration.Token,
            }, WireJson));
            var authenticate = typeof(L12WebSocketServer).GetMethod("AuthenticateSessionAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var task = Assert.IsAssignableFrom<Task<IReadOnlyList<OutgoingMessage>>>(
                authenticate.Invoke(server, [transportId, hello.RootElement]));
            var response = Assert.Single(await task);
            var payload = JsonSerializer.SerializeToElement(response.Payload, WireJson);
            Assert.Equal("authenticationRequired", payload.GetProperty("type").GetString());
            Assert.Equal("platform-session-revoked", payload.GetProperty("reason").GetString());
            Assert.False(ServerMapContains(server, "_socketPlatformSessions", transportId));
            Assert.False(ServerMapContains(server, "_socketCapabilities", transportId));
            var roomSessions = typeof(L12RoomManager).GetField("_sessions",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            Assert.Equal(0, (int)roomSessions.GetType().GetProperty("Count")!.GetValue(roomSessions)!);
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-fenced-websocket"); }
    }

    private static void ExpireCommittedSession(L12PlatformStore platform, string sessionId,
        string accountId, L12AccountView admin)
    {
        var state = typeof(L12PlatformStore).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(platform)!;
        var rows = (IEnumerable)state.GetType().GetProperty("Sessions")!.GetValue(state)!;
        var row = rows.Cast<object>().Single(item =>
            (string)item.GetType().GetProperty("Id")!.GetValue(item)! == sessionId);
        row.GetType().GetProperty("ExpiresAt")!.SetValue(row, DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(platform.SetRole(admin, accountId, "player"));
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveTypeAsync(ClientWebSocket socket, string expected,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            var message = await ReceiveOneAsync(socket, cancellation.Token);
            if (message.TryGetProperty("type", out var type) && type.GetString() == expected)
                return message;
        }
    }

    private static async Task<JsonElement> ReceiveOneAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("socket closed before expected message");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static async Task<WebSocketCloseStatus?> ReceiveCloseAsync(ClientWebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[1024];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close) return result.CloseStatus;
        }
    }

    private static bool ServerMapContains(L12WebSocketServer server, string fieldName, Guid sessionId)
    {
        var map = typeof(L12WebSocketServer).GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        return (bool)map.GetType().GetMethod("ContainsKey")!.Invoke(map, [sessionId])!;
    }

    private static void DeleteIsolatedTestDirectory(string directory, string expectedParentName)
    {
        var target = new DirectoryInfo(Path.GetFullPath(directory));
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), expectedParentName));
        if (target.Parent is null
            || !string.Equals(target.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
                expectedParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || target.Name.Length != 32
            || !Guid.TryParseExact(target.Name, "N", out _))
            throw new InvalidOperationException($"refusing to delete unexpected test directory: {target.FullName}");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (target.Exists) target.Delete(recursive: true);
    }
}
