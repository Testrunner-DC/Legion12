using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class GameplayPlatformLockProtocolTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ThirtySecondPlatformCommitDoesNotBlockHealthOrEitherLiveRoomSnapshot()
    {
        using var fixture = new RankedIdentityFixture();
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "protocol.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        await using var server = new L12WebSocketServer(manager, recorder, fixture.Store, fixture.Catalog);
        await server.StartAsync(0);
        var httpUri = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" }.Uri;
        var wsUri = new UriBuilder(httpUri) { Scheme = "ws", Path = "/ws" }.Uri;
        using var http = new HttpClient { BaseAddress = httpUri, Timeout = TimeSpan.FromSeconds(2) };
        var adminToken = fixture.Store.Login("Admin", "L12master").Token;
        var newcomer = fixture.Store.Register("protocolnew", "Password123!");
        Assert.True(newcomer.Success, newcomer.Message);
        var sockets = new List<ClientWebSocket>();
        var sessions = new List<Guid>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task? writer = null;
        try
        {
            for (var index = 0; index < 4; index++)
            {
                var registered = fixture.Store.Register($"protocol{index}", "Password123!");
                Assert.True(registered.Success);
                var socket = new ClientWebSocket();
                sockets.Add(socket);
                await socket.ConnectAsync(wsUri, CancellationToken.None);
                await Send(socket, new { type = "hello", authToken = registered.Token });
                var session = await Receive(socket, "session", 10);
                sessions.Add(session.GetProperty("sessionId").GetGuid());
                _ = await Receive(socket, "recoveryComplete", 10);
            }
            for (var pair = 0; pair < 2; pair++)
            {
                var first = pair * 2;
                await Send(sockets[first], new { type = "createRoom" });
                var room = await Receive(sockets[first], "roomState", 10);
                await Send(sockets[first + 1], new { type = "joinRoom", roomCode = room.GetProperty("roomCode").GetString() });
                _ = await Receive(sockets[first + 1], "roomState", 10);
                await manager.SetReadyAsync(sessions[first], true);
                var started = await manager.SetReadyAsync(sessions[first + 1], true);
                Assert.Contains(started, row => JsonSerializer.SerializeToElement(row.Payload, Wire)
                    .GetProperty("type").GetString() == "gameState");
                await Send(sockets[first], new { type = "syncState" });
                _ = await Receive(sockets[first], "gameState", 10);
                _ = await Receive(sockets[first], "recoveryComplete", 10);
            }

            fixture.Store.StorageFailureInjector = stage =>
            {
                if (stage != "before-commit") return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(40))) throw new TimeoutException("synthetic platform barrier");
            };
            writer = Task.Factory.StartNew(() => fixture.Store.Register("prowriter", "Password123!"),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var held = Stopwatch.StartNew();
            try
            {
                for (var round = 0; round < 3; round++)
                {
                    var healthWatch = Stopwatch.StartNew();
                    using var health = await http.GetAsync("health");
                    health.EnsureSuccessStatusCode();
                    Assert.True(healthWatch.Elapsed < TimeSpan.FromSeconds(1));
                    output.WriteLine("health_ms={0:F2}", healthWatch.Elapsed.TotalMilliseconds);
                    foreach (var index in new[] { 0, 2 })
                    {
                        var watch = Stopwatch.StartNew();
                        await Send(sockets[index], new { type = "syncState" });
                        var snapshot = await Receive(sockets[index], "gameState", 1);
                        Assert.Equal(2, snapshot.GetProperty("playerBadges").GetArrayLength());
                        _ = await Receive(sockets[index], "recoveryComplete", 1);
                        output.WriteLine("room_index={0} snapshot_ms={1:F2}", index / 2, watch.Elapsed.TotalMilliseconds);
                        await Send(sockets[index], new { type = "ping" });
                        _ = await Receive(sockets[index], "pong", 1);
                    }
                }
                using (var me = new HttpRequestMessage(HttpMethod.Get, "api/auth/me"))
                {
                    me.Headers.Authorization = new("Bearer", newcomer.Token);
                    using var response = await http.SendAsync(me);
                    response.EnsureSuccessStatusCode();
                }
                var newSocket = new ClientWebSocket();
                sockets.Add(newSocket);
                await newSocket.ConnectAsync(wsUri, CancellationToken.None);
                var helloWatch = Stopwatch.StartNew();
                await Send(newSocket, new { type = "hello", authToken = newcomer.Token });
                _ = await Receive(newSocket, "session", 1);
                var presence = await Receive(newSocket, "presenceSnapshot", 1);
                var presenceJson = presence.GetRawText();
                Assert.DoesNotContain("email", presenceJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("deck", presenceJson, StringComparison.OrdinalIgnoreCase);
                _ = await Receive(newSocket, "recoveryComplete", 1);
                output.WriteLine("blocked_commit_new_hello_ms={0:F2}", helloWatch.Elapsed.TotalMilliseconds);
                using (var cards = new HttpRequestMessage(HttpMethod.Get, "api/admin/analytics/cards"))
                {
                    cards.Headers.Authorization = new("Bearer", adminToken);
                    var auditWatch = Stopwatch.StartNew();
                    using var response = await http.SendAsync(cards);
                    Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
                    output.WriteLine("blocked_commit_read_audit_503_ms={0:F2}", auditWatch.Elapsed.TotalMilliseconds);
                }
                var remaining = TimeSpan.FromSeconds(30) - held.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
                Assert.False(writer.IsCompleted);
                using var finalHealth = await http.GetAsync("health");
                finalHealth.EnsureSuccessStatusCode();
                output.WriteLine("held_platform_ms={0:F2}", held.Elapsed.TotalMilliseconds);
            }
            finally
            {
                release.Set();
                await writer.WaitAsync(TimeSpan.FromSeconds(10));
                fixture.Store.StorageFailureInjector = null;
            }
        }
        finally
        {
            release.Set();
            if (writer is not null) await writer.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Store.StorageFailureInjector = null;
            foreach (var socket in sockets) { socket.Abort(); socket.Dispose(); }
            await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task Send(ClientWebSocket socket, object value)
        => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, Wire),
            WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonElement> Receive(ClientWebSocket socket, string type, int seconds)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (true)
        {
            using var message = new MemoryStream();
            var buffer = new byte[16384];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, deadline.Token);
                Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
                await message.WriteAsync(buffer.AsMemory(0, result.Count), deadline.Token);
            } while (!result.EndOfMessage);
            using var document = JsonDocument.Parse(message.ToArray());
            if (document.RootElement.GetProperty("type").GetString() == type)
                return document.RootElement.Clone();
        }
    }
}
