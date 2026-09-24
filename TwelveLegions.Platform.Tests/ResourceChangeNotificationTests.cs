using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ResourceChangeNotificationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FriendMutationPushesTargetedResourceChangeAndAggregatedOverview()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        using var senderSocket = new ClientWebSocket();
        using var recipientSocket = new ClientWebSocket();
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var sender = store.Register("trsynsndr", "password-123");
            var recipient = store.Register("trsynrcpt", "password-456");
            Assert.True(sender.Success);
            Assert.True(recipient.Success);

            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            var address = Assert.Single(server.Addresses);
            var socketUri = new UriBuilder(address) { Scheme = "ws", Path = "/ws" }.Uri;
            await ConnectAsync(senderSocket, socketUri, sender.Token!);
            await ConnectAsync(recipientSocket, socketUri, recipient.Token!);

            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var request = Authorized(HttpMethod.Post, "/api/friends/requests", sender.Token!,
                new { accountId = recipient.Account!.Id });
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var senderChange = await ReceiveUntilAsync(senderSocket, "resourceChanged", "friends");
            var recipientChange = await ReceiveUntilAsync(recipientSocket, "resourceChanged", "friends");
            Assert.Equal(senderChange["epoch"]!.GetValue<string>(), recipientChange["epoch"]!.GetValue<string>());
            Assert.True(senderChange["revision"]!.GetValue<long>() > 0);
            Assert.True(recipientChange["revision"]!.GetValue<long>() > 0);

            using var overviewRequest = Authorized(HttpMethod.Get, "/api/friends/overview", recipient.Token!);
            using var overviewResponse = await client.SendAsync(overviewRequest);
            Assert.Equal(HttpStatusCode.OK, overviewResponse.StatusCode);
            var overview = await overviewResponse.Content.ReadFromJsonAsync<JsonObject>();
            var incoming = Assert.Single(overview!["requests"]!.AsArray());
            Assert.Equal(sender.Account!.Id, incoming!["accountId"]!.GetValue<string>());
            Assert.Equal("incoming", incoming["direction"]!.GetValue<string>());
        }
        finally
        {
            senderSocket.Abort();
            recipientSocket.Abort();
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RuleItemPublishIsAuthorizedIdempotentIsolatedAndPushesRulesContentChange()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        using var adminSocket = new ClientWebSocket();
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("rulepublishplayer", "password-123");
            var draft = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                entries = new[]
                {
                    Ruling("RULING-PUSH-A", "第一条裁定？"),
                    Ruling("RULING-PUSH-B", "第二条仍为草稿？"),
                },
            });
            var saved = store.SaveContentDraft(admin.Account!, "rules.rulings", draft);

            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            var address = Assert.Single(server.Addresses);
            var socketUri = new UriBuilder(address) { Scheme = "ws", Path = "/ws" }.Uri;
            await ConnectAsync(adminSocket, socketUri, admin.Token!);
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var idempotencyKey = $"rule-publish-{Guid.NewGuid():N}";
            var body = new { key = "rules.rulings", collection = "entries", itemId = "RULING-PUSH-A",
                expectedVersion = saved.Version, idempotencyKey };

            using (var denied = Authorized(HttpMethod.Post, "/api/admin/rule-items/publish", player.Token!, body))
            using (var deniedResponse = await client.SendAsync(denied))
                Assert.Equal(HttpStatusCode.Unauthorized, deniedResponse.StatusCode);

            using (var publish = Authorized(HttpMethod.Post, "/api/admin/rule-items/publish", admin.Token!, body))
            using (var publishResponse = await client.SendAsync(publish))
                Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var change = await ReceiveUntilAsync(adminSocket, "resourceChanged", "rulesContent");
            Assert.True(change["revision"]!.GetValue<long>() > 0);

            using (var replay = Authorized(HttpMethod.Post, "/api/admin/rule-items/publish", admin.Token!, body))
            using (var replayResponse = await client.SendAsync(replay))
            {
                Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
                Assert.Equal("true", replayResponse.Headers.GetValues("X-Idempotent-Replay").Single());
            }

            var batch = await client.GetFromJsonAsync<JsonObject>("/api/content?key=rules.rulings");
            var publicValue = batch!["values"]!["rules.rulings"]!.GetValue<string>();
            Assert.Contains("RULING-PUSH-A", publicValue);
            Assert.DoesNotContain("RULING-PUSH-B", publicValue);
        }
        finally
        {
            adminSocket.Abort();
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static object Ruling(string id, string question) => new
    {
        id, scope = "general", question, answer = "已确认答案。", category = "效果与响应",
        sourceKind = "user-ruling", sourceRef = "测试来源", recordedAt = "2026-09-25", status = "pending",
        cardIds = Array.Empty<string>(), productIds = Array.Empty<string>(), tags = Array.Empty<string>(),
        topics = new[] { "effects-stack" }, sourceIds = Array.Empty<string>(), supersedes = Array.Empty<string>(),
    };

    private static async Task ConnectAsync(ClientWebSocket socket, Uri uri, string token)
    {
        await socket.ConnectAsync(uri, CancellationToken.None);
        await SendAsync(socket, new { type = "hello", authToken = token });
        await ReceiveUntilAsync(socket, "session");
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, $"resource-sync-{Guid.NewGuid():N}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonObject> ReceiveUntilAsync(ClientWebSocket socket, string type,
        string? resource = null)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[32 * 1024];
        while (true)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("WebSocket closed before the expected resource event.");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var message = JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()))!.AsObject();
            if (message["type"]?.GetValue<string>() != type) continue;
            if (resource is not null && message["resource"]?.GetValue<string>() != resource) continue;
            return message;
        }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-resource-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
