using System.Collections;
using System.Net.WebSockets;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentIngressGuardTests
{
    private const string ProcessInstance = "11111111111111111111111111111111";
    private const string OperationId = "22222222222222222222222222222222";
    private const string ActiveCommit = "3333333333333333333333333333333333333333";
    private const string TargetCommit = "4444444444444444444444444444444444444444";
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly string ArtifactRoot = Path.Combine("D:\\GPT\\Legion12\\artifacts",
        "deployment-ingress-guard-tests");

    [Theory]
    [InlineData("{\"type\":\"ping\"}", 0)]
    [InlineData("{\"type\":\"deploymentProbe\"}", 0)]
    [InlineData("{\"type\":\"createRoom\"}", 1)]
    [InlineData("{\"type\":\"updateRoomOptions\"}", 1)]
    [InlineData("{\"type\":\"createSandbox\"}", 1)]
    [InlineData("{\"type\":\"joinMatchmaking\"}", 1)]
    [InlineData("{\"type\":\"joinRoom\"}", 1)]
    [InlineData("{\"type\":\"inviteFriend\"}", 1)]
    [InlineData("{\"type\":\"selectDeck\"}", 1)]
    [InlineData("{\"type\":\"selectCustomDeck\"}", 1)]
    [InlineData("{\"type\":\"resolveFriendInvitation\",\"accept\":true}", 1)]
    [InlineData("{\"type\":\"resolveFriendInvitation\",\"accept\":false}", 2)]
    [InlineData("{\"type\":\"ready\"}", 1)]
    [InlineData("{\"type\":\"ready\",\"ready\":false}", 2)]
    [InlineData("{\"type\":\"hello\"}", 3)]
    [InlineData("{\"type\":\"pollMatchmaking\"}", 3)]
    [InlineData("{\"type\":\"syncState\"}", 3)]
    [InlineData("{\"type\":\"enterTournamentMatch\"}", 3)]
    [InlineData("{\"type\":\"cancelMatchmaking\"}", 2)]
    [InlineData("{\"type\":\"cancelFriendInvitation\"}", 2)]
    [InlineData("{\"type\":\"spectateRoom\"}", 2)]
    [InlineData("{\"type\":\"spectateTournamentMatch\"}", 2)]
    [InlineData("{\"type\":\"leaveRoom\"}", 2)]
    [InlineData("{\"type\":\"gameAction\"}", 2)]
    [InlineData("{\"type\":\"getResponsePreference\"}", 2)]
    [InlineData("{\"type\":\"setResponsePreference\"}", 2)]
    [InlineData("{\"type\":\"requestMatchDraw\"}", 2)]
    [InlineData("{\"type\":\"resolveMatchDraw\"}", 2)]
    [InlineData("{\"type\":\"reportOpponent\"}", 2)]
    [InlineData("{\"type\":\"sandboxAction\"}", 2)]
    [InlineData("{\"type\":\"gmAction\"}", 2)]
    [InlineData("{\"type\":\"getEffectiveOperationsPolicy\"}", 2)]
    [InlineData("{\"type\":\"futureCommand\"}", 2)]
    [InlineData("{", 2)]
    [InlineData("[]", 2)]
    public void IngressClassificationIsExplicitAndUnknownShapesAreActivity(string json, int expected)
    {
        Assert.Equal((L12DeploymentIngressKind)expected,
            L12WebSocketServer.ClassifyDeploymentIngress(json));
    }

    [Fact]
    public async Task QueueAdmissionAcceptedBeforeDrainFinishesAndCancelledActivityReleasesLease()
    {
        var coordinator = CreateCoordinator();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = new List<string>();
        var inbound = new L12InboundConnection(async json =>
        {
            handled.Add(json);
            entered.TrySetResult();
            await release.Task;
        }, leaseAcquirer: json =>
            L12WebSocketServer.TryAcquireQueuedDeploymentLease(coordinator, json));

        try
        {
            Assert.Equal(L12InboundEnqueueResult.Accepted,
                inbound.TryEnqueue("{\"type\":\"createRoom\"}"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);

            Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                coordinator.BeginDrain(CreateOwner()).Code);
            Assert.Equal(L12InboundEnqueueResult.DeploymentDraining,
                inbound.TryEnqueue("{\"type\":\"createRoom\"}"));
            Assert.Equal(L12InboundEnqueueResult.Accepted,
                inbound.TryEnqueue("{\"type\":\"syncState\"}"));
            Assert.Equal(1, coordinator.Snapshot().ActivityLeases);

            inbound.StopAcceptingAndCancelPending();
            Assert.Equal(0, coordinator.Snapshot().ActivityLeases);
            Assert.Equal(1, coordinator.Snapshot().AdmissionLeases);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
        Assert.Equal(new[] { "{\"type\":\"createRoom\"}" }, handled.ToArray());
    }

    [Theory]
    [InlineData("POST", "/api/tournaments/x/start", 1)]
    [InlineData("POST", "/API/TOURNAMENTS/x/START/", 1)]
    [InlineData("POST", "/API/FUTURE-MUTATION/", 2)]
    [InlineData("POST", "/api/tournaments/x/rounds", 1)]
    [InlineData("POST", "/api/tournaments/x/rounds/1/start", 1)]
    [InlineData("POST", "/api/tournaments/x/matches/y/rematch", 1)]
    [InlineData("POST", "/api/tournaments/x/rounds/1/check-in", 1)]
    [InlineData("POST", "/api/tournaments/x/cancel", 2)]
    [InlineData("DELETE", "/api/admin/accounts/x", 2)]
    [InlineData("GET", "/api/tournaments/x", 2)]
    [InlineData("POST", "/api/future-mutation", 2)]
    [InlineData("GET", "/health", 0)]
    [InlineData("OPTIONS", "/api/tournaments/x/start", 0)]
    [InlineData("POST", "/api/admin/deployment-drain/seal", 0)]
    public void HttpIngressLeaseCoversWholeEndpointAndControlDoesNotCountItself(string method, string path, int kind)
        => Assert.Equal((L12DeploymentIngressKind)kind, L12WebSocketServer.ClassifyDeploymentHttpIngress(method, path));

    [Fact]
    public async Task HttpDrainRejectsNewTournamentButKeepsReadsUntilSeal()
    {
        await using var f = await WebSocketFixture.CreateAsync(withCoordinator: true);
        await f.StartAsync();
        using var client = new HttpClient { BaseAddress = new UriBuilder(Assert.Single(f.Server.Addresses)) { Host = "127.0.0.1" }.Uri };
        f.Coordinator!.BeginDrain(CreateOwner());
        using var refused = await client.PostAsJsonAsync("/api/tournaments/synthetic/start", new { reason = "synthetic" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Contains("deploymentDrainActive", await refused.Content.ReadAsStringAsync());
        using var caseVariant = await client.PostAsJsonAsync("/API/TOURNAMENTS/synthetic/START/", new { reason = "synthetic" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, caseVariant.StatusCode);
        using var read = await client.GetAsync("/api/operations/effective-policy");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(0, f.Coordinator.Snapshot().ActivityLeases);
        Assert.True(f.Coordinator.TryBeginSeal(CreateOwner()).Succeeded);
        using var sealedRead = await client.GetAsync("/api/operations/effective-policy");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, sealedRead.StatusCode);
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task DrainingSocketRejectsAdmissionButKeepsExistingGameSyncAndControlLaneHealthy()
    {
        await using var fixture = await WebSocketFixture.CreateAsync(withCoordinator: true);
        await fixture.StartAsync();
        var (_, token) = fixture.CreateAccount("drain");
        using var socket = await ConnectAuthenticatedAsync(fixture.Endpoint, token);

        await SendAsync(socket, new { type = "createSandbox" });
        var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
        var roomCode = room.GetProperty("roomCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(roomCode));

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
            fixture.Coordinator!.BeginDrain(CreateOwner()).Code);

        await SendAsync(socket, new { type = "createRoom", requestId = "blocked-room" });
        var blocked = await ReceiveTypeAsync(socket, "error", TimeSpan.FromSeconds(10),
            root => ReadString(root, "code") == "deploymentDrainActive");
        Assert.Equal("blocked-room", ReadString(blocked, "requestId"));
        Assert.Equal(WebSocketState.Open, socket.State);

        await SendAsync(socket, new { type = "syncState" });
        var recovered = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
        Assert.Equal(roomCode, ReadString(recovered, "roomCode"));

        await SendAsync(socket, new { type = "ping" });
        _ = await ReceiveTypeAsync(socket, "pong", TimeSpan.FromSeconds(2));
        await SendAsync(socket, new { type = "deploymentProbe" });
        _ = await ReceiveTypeAsync(socket, "deploymentProbe", TimeSpan.FromSeconds(2));
        await SendAsync(socket, new { type = "futureCommand" });
        var unknown = await ReceiveTypeAsync(socket, "error", TimeSpan.FromSeconds(2));
        Assert.Equal("未知消息类型", ReadString(unknown, "message"));
        Assert.Equal(WebSocketState.Open, socket.State);

        await WaitUntilAsync(() => fixture.Coordinator.Snapshot().AdmissionLeases == 0
            && fixture.Coordinator.Snapshot().ActivityLeases == 0, TimeSpan.FromSeconds(2),
            "WebSocket command leases did not return to zero");
    }

    [Fact]
    public async Task DrainingHelloRecoversExistingGameButFreshHelloCannotStartGame()
    {
        await using var fixture = await WebSocketFixture.CreateAsync(withCoordinator: true);
        await fixture.StartAsync();
        var (_, recoveryToken) = fixture.CreateAccount("recover");
        string roomCode;
        using (var original = await ConnectAuthenticatedAsync(fixture.Endpoint, recoveryToken))
        {
            await SendAsync(original, new { type = "createSandbox" });
            var room = await ReceiveTypeAsync(original, "roomState", TimeSpan.FromSeconds(10));
            roomCode = ReadString(room, "roomCode")!;
            original.Abort();
        }
        await WaitUntilAsync(() => SocketBindingCount(fixture.Server) == 0,
            TimeSpan.FromSeconds(5), "original socket binding was not released");

        Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
            fixture.Coordinator!.BeginDrain(CreateOwner()).Code);

        using var recoveredSocket = new ClientWebSocket();
        await recoveredSocket.ConnectAsync(fixture.Endpoint, CancellationToken.None);
        await SendAsync(recoveredSocket, new { type = "hello", authToken = recoveryToken });
        var session = await ReceiveTypeAsync(recoveredSocket, "session", TimeSpan.FromSeconds(10));
        Assert.True(session.GetProperty("recovered").GetBoolean());
        Assert.Equal(roomCode, ReadString(session, "roomCode"));
        var recovery = await ReceiveTypeAsync(recoveredSocket, "recoveryComplete",
            TimeSpan.FromSeconds(10));
        Assert.Equal(roomCode, ReadString(recovery, "roomCode"));

        var (_, freshToken) = fixture.CreateAccount("fresh");
        using var freshSocket = await ConnectAuthenticatedAsync(fixture.Endpoint, freshToken);
        await SendAsync(freshSocket, new { type = "createSandbox", requestId = "no-new-game" });
        var rejected = await ReceiveTypeAsync(freshSocket, "error", TimeSpan.FromSeconds(10),
            root => ReadString(root, "code") == "deploymentDrainActive");
        Assert.Equal("no-new-game", ReadString(rejected, "requestId"));

        await SendAsync(recoveredSocket, new { type = "syncState" });
        _ = await ReceiveTypeAsync(recoveredSocket, "recoveryComplete", TimeSpan.FromSeconds(10));
        await SendAsync(recoveredSocket, new { type = "ping" });
        _ = await ReceiveTypeAsync(recoveredSocket, "pong", TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LateSessionRevocationCancelsPendingQueueLeaseAndExecutingLeaseEndsInFinally()
    {
        await using var fixture = await WebSocketFixture.CreateAsync(withCoordinator: true);
        await fixture.StartAsync();
        var (_, token) = fixture.CreateAccount("revoke");
        var authenticated = fixture.Platform.AuthenticateTokenSession(token)!;
        using var socket = await ConnectAuthenticatedAsync(fixture.Endpoint, token);
        await SendAsync(socket, new { type = "createSandbox" });
        var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
        var holder = HoldGateAsync(RuntimeGate(fixture.Rooms, ReadString(room, "roomCode")!));
        try
        {
            await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
            await SendAsync(socket, new { type = "syncState" });
            await WaitUntilAsync(() => fixture.Coordinator!.Snapshot().AdmissionLeases == 1,
                TimeSpan.FromSeconds(2), "mixed sync did not retain its admission lease");
            await SendAsync(socket, new { type = "leaveRoom" });
            await WaitUntilAsync(() => fixture.Coordinator!.Snapshot().ActivityLeases == 1,
                TimeSpan.FromSeconds(2), "pending activity lease was not retained");

            Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                fixture.Coordinator!.BeginDrain(CreateOwner()).Code);
            Assert.Equal(1, fixture.Platform.RevokeOwnSession(authenticated,
                authenticated.SessionId).RevokedCount);
            await WaitUntilAsync(() => fixture.Coordinator.Snapshot().ActivityLeases == 0,
                TimeSpan.FromSeconds(2), "revocation did not release the pending activity lease");
            Assert.Equal(1, fixture.Coordinator.Snapshot().AdmissionLeases);
        }
        finally
        {
            holder.Release.TrySetResult();
            await holder.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await WaitUntilAsync(() => fixture.Coordinator!.Snapshot().AdmissionLeases == 0
            && fixture.Coordinator.Snapshot().ActivityLeases == 0,
            TimeSpan.FromSeconds(5), "executing lease did not release after revocation");
    }

    [Fact]
    public async Task SealedHelloReturnsBoundedDrainErrorWithoutPublishingSocketBinding()
    {
        await using var fixture = await WebSocketFixture.CreateAsync(withCoordinator: true);
        var (_, token) = fixture.CreateAccount("sealed");
        var owner = CreateOwner();
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
            fixture.Coordinator!.BeginDrain(owner).Code);
        var sealing = fixture.Coordinator.TryBeginSeal(owner);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied, sealing.Code);
        Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
            fixture.Coordinator.CompleteSeal(owner, sealing.Snapshot.Epoch,
                L12DeploymentExternalReadiness.Clear).Code);
        await fixture.StartAsync();

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(fixture.Endpoint, CancellationToken.None);
        await SendAsync(socket, new
        {
            type = "hello", authToken = token, requestId = new string('x', 512),
            capabilities = new[] { "deltaGameState" },
        });
        var rejected = await ReceiveTypeAsync(socket, "error", TimeSpan.FromSeconds(10),
            root => ReadString(root, "code") == "deploymentDrainActive");
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("requestId").ValueKind);
        Assert.Equal(0, SocketBindingCount(fixture.Server));

        // A rejected hello does not create a business binding; anonymous deployment probing remains
        // available to the deployment controller without borrowing a queue lease.
        await SendAsync(socket, new { type = "deploymentProbe" });
        _ = await ReceiveTypeAsync(socket, "deploymentProbe", TimeSpan.FromSeconds(2));
        Assert.Equal(0, SocketBindingCount(fixture.Server));
    }

    [Fact]
    public async Task DisabledCoordinatorPreservesLegacyHelloAndAdmissionPath()
    {
        await using var fixture = await WebSocketFixture.CreateAsync(withCoordinator: false);
        await fixture.StartAsync();
        var (_, token) = fixture.CreateAccount("disabled");
        using var socket = await ConnectAuthenticatedAsync(fixture.Endpoint, token);
        await SendAsync(socket, new { type = "createRoom" });
        var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
        Assert.False(string.IsNullOrWhiteSpace(ReadString(room, "roomCode")));
    }

    [Fact]
    public async Task AutomaticActivationRunsDuringDrainAndSealingBlocksBeforeClaim()
    {
        var allowedRoot = CreateArtifactDirectory();
        var blockedRoot = CreateArtifactDirectory();
        try
        {
            var now = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
            var due = now.AddMinutes(1);
            var (allowedStore, allowedAdmin, allowedCatalog) = PrepareArmedStore(allowedRoot, due, now);
            await using (var allowedRecorder = new MatchRecorder(Path.Combine(allowedRoot, "matches.db"),
                             () => due))
            {
                await allowedRecorder.InitializeAsync();
                var allowedRooms = new L12RoomManager(allowedCatalog, allowedRecorder, allowedStore,
                    () => due);
                var allowedCoordinator = CreateCoordinator();
                allowedRooms.AttachDeploymentDrain(allowedCoordinator);
                Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                    allowedCoordinator.BeginDrain(CreateOwner()).Code);
                await using var allowedServer = new L12WebSocketServer(allowedRooms, allowedRecorder,
                    allowedStore, allowedCatalog, seasonActivationUtcNow: () => due);

                Assert.True(await allowedServer.RunSeasonActivationOnceAsync(due));
                Assert.Equal("S-ingress", allowedStore.SeasonCatalog(allowedAdmin).Current.SeasonId);
                Assert.Equal(0, allowedCoordinator.Snapshot().ActivityLeases);
            }

            var (blockedStore, blockedAdmin, blockedCatalog) = PrepareArmedStore(blockedRoot, due, now);
            await using (var blockedRecorder = new MatchRecorder(Path.Combine(blockedRoot, "matches.db"),
                             () => due))
            {
                await blockedRecorder.InitializeAsync();
                var blockedRooms = new L12RoomManager(blockedCatalog, blockedRecorder, blockedStore,
                    () => due);
                var blockedCoordinator = CreateCoordinator();
                blockedRooms.AttachDeploymentDrain(blockedCoordinator);
                var owner = CreateOwner();
                Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                    blockedCoordinator.BeginDrain(owner).Code);
                Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                    blockedCoordinator.TryBeginSeal(owner).Code);
                await using var blockedServer = new L12WebSocketServer(blockedRooms, blockedRecorder,
                    blockedStore, blockedCatalog, seasonActivationUtcNow: () => due);

                Assert.False(await blockedServer.RunSeasonActivationOnceAsync(due));
                var plan = blockedStore.SeasonCatalog(blockedAdmin).Next!.ActivationPlan!;
                Assert.Equal("armed", plan.Status);
                Assert.Null(plan.LeaseOwner);
                Assert.Throws<L12DeploymentBarrierClosedException>(() =>
                    blockedStore.TryClaimDueSeasonActivation("verification-worker", due, TimeSpan.FromMinutes(1)));
                Assert.Null(blockedStore.SeasonCatalog(blockedAdmin).Next!.ActivationPlan!.LeaseOwner);
            }
        }
        finally
        {
            DeleteArtifactDirectory(allowedRoot);
            DeleteArtifactDirectory(blockedRoot);
        }
    }

    [Fact]
    public async Task AutomaticFinalizationSealingFenceReturnsBeforeDurableClaimOrDiagnostic()
    {
        var root = CreateArtifactDirectory();
        try
        {
            var due = new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
            var (store, admin, catalog) = PrepareEndingStore(root, due);
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => due);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => due);
            var coordinator = CreateCoordinator();
            rooms.AttachDeploymentDrain(coordinator);
            var owner = CreateOwner();
            Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                coordinator.BeginDrain(owner).Code);
            Assert.Equal(L12DeploymentDrainTransitionCode.Applied,
                coordinator.TryBeginSeal(owner).Code);
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => due);

            Assert.False(await server.RunSeasonFinalizationOnceAsync(due));
            var current = store.SeasonCatalog(admin, due).Current;
            Assert.Equal("draining", current.FinalizationStatus);
            Assert.Null(current.FinalizedAt);
            Assert.Throws<L12DeploymentBarrierClosedException>(() =>
                store.TryClaimDueSeasonFinalization("verification-worker", due, TimeSpan.FromMinutes(1)));
            Assert.Null(store.SeasonCatalog(admin, due).Current.FinalizedAt);
        }
        finally { DeleteArtifactDirectory(root); }
    }

    private static L12DeploymentDrainCoordinator CreateCoordinator()
        => new(ProcessInstance, ActiveCommit, new FakeFenceStore());

    private static L12DeploymentDrainOwner CreateOwner()
    {
        Assert.True(L12DeploymentDrainOwner.TryCreate(OperationId, TargetCommit, ProcessInstance,
            out var owner));
        return owner!;
    }

    private static async Task<ClientWebSocket> ConnectAuthenticatedAsync(Uri endpoint, string token)
    {
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, CancellationToken.None);
            await SendAsync(socket, new { type = "hello", authToken = token });
            _ = await ReceiveTypeAsync(socket, "session", TimeSpan.FromSeconds(10));
            _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
            return socket;
        }
        catch
        {
            socket.Abort();
            socket.Dispose();
            throw;
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveTypeAsync(ClientWebSocket socket, string expected,
        TimeSpan timeout, Func<JsonElement, bool>? predicate = null)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[32 * 1024];
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("socket closed before expected message");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var document = JsonDocument.Parse(stream.ToArray());
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == expected
                && (predicate is null || predicate(root)))
                return root.Clone();
        }
        throw new InvalidDataException($"missing WebSocket message {expected}");
    }

    private static string? ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout,
        string failureMessage)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(predicate(), failureMessage);
    }

    private static int SocketBindingCount(L12WebSocketServer server)
    {
        var bindings = typeof(L12WebSocketServer)
            .GetField("_socketPlatformSessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server)!;
        return (int)bindings.GetType().GetProperty("Count")!.GetValue(bindings)!;
    }

    private static SemaphoreSlim RuntimeGate(L12RoomManager manager, string roomCode)
    {
        var rooms = (IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in rooms)
        {
            var room = entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
            if (!string.Equals((string)room.GetType().GetProperty("Code")!.GetValue(room)!, roomCode,
                    StringComparison.OrdinalIgnoreCase)) continue;
            return (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        }
        throw new InvalidOperationException($"missing runtime room {roomCode}");
    }

    private static GateHolder HoldGateAsync(SemaphoreSlim gate)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Task.Run(async () =>
        {
            await gate.WaitAsync();
            entered.TrySetResult();
            try { await release.Task; }
            finally { gate.Release(); }
        });
        return new GateHolder(entered.Task, release, completion);
    }

    private static (L12PlatformStore Store, L12AccountView Admin, L12Catalog Catalog) PrepareArmedStore(
        string root, DateTimeOffset scheduledAt, DateTimeOffset now)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var seasons = store.SeasonCatalog(admin);
        var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
            new L12SeasonDefinitionDraft("S-ingress", "部署接入测试赛季", scheduledAt,
                scheduledAt.AddDays(90), seasons.Next.Configuration), seasons.Next.Revision,
            "prepare deployment ingress activation", Context("prepare-activation"));
        var operationsVersion = store.OperationsConfig(admin).Version;
        var readiness = new L12RankedSeasonCutoverReadiness(seasons.Current.SeasonId, 0, 0, 0, 0);
        var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
            seasons.Current.Revision, draft.Revision, operationsVersion, readiness, now);
        store.ArmSeasonActivation(admin, draft.DefinitionId, seasons.Current.Revision, draft.Revision,
            operationsVersion, preview.PreviewToken, readiness, "arm deployment ingress activation", now,
            Context("arm-activation", operationsVersion));
        return (store, admin, catalog);
    }

    private static (L12PlatformStore Store, L12AccountView Admin, L12Catalog Catalog) PrepareEndingStore(
        string root, DateTimeOffset endsAt)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var operations = store.OperationsConfig(admin);
        store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = endsAt },
            }, operations.Version, "prepare deployment ingress finalization",
            Context("prepare-finalization"));
        var seasons = store.SeasonCatalog(admin);
        store.DeleteSeasonDraft(admin, seasons.Next!.DefinitionId, seasons.Next.Revision,
            "remove next season for deployment ingress finalization",
            Context("remove-next-season"));
        return (store, admin, catalog);
    }

    private static L12AdminAuditContext Context(string id, long? expected = null)
        => new(id, ExpectedVersion: expected, RequestMethod: "TEST",
            RequestPath: "/test/deployment-ingress");

    private static string CreateArtifactDirectory()
    {
        Directory.CreateDirectory(ArtifactRoot);
        var directory = Path.Combine(ArtifactRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteArtifactDirectory(string directory)
    {
        var target = new DirectoryInfo(Path.GetFullPath(directory));
        var expectedParent = Path.GetFullPath(ArtifactRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (target.Parent is null
            || !string.Equals(target.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
                expectedParent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(target.Name, "N", out _))
            throw new InvalidOperationException($"refusing to delete unexpected test directory: {target.FullName}");
        SqliteConnection.ClearAllPools();
        if (target.Exists) target.Delete(recursive: true);
    }

    private sealed record GateHolder(Task Entered, TaskCompletionSource Release, Task Completion);

    private sealed class FakeFenceStore : IL12DeploymentDrainFenceStore
    {
        private readonly object _gate = new();
        private L12DeploymentPersistedFence? _fence;

        public L12DeploymentDrainFenceLoadResult Load()
            => L12DeploymentDrainFenceLoadResult.Missing;

        public bool TryWrite(L12DeploymentPersistedFence fence)
        {
            lock (_gate) _fence = fence;
            return true;
        }

        public bool TryClear(string operationId, string targetCommit)
        {
            lock (_gate)
            {
                if (_fence is not null && (_fence.OperationId != operationId
                    || _fence.TargetCommit != targetCommit)) return false;
                _fence = null;
                return true;
            }
        }
    }

    private sealed class WebSocketFixture : IAsyncDisposable
    {
        private readonly string _root;
        private bool _started;

        private WebSocketFixture(string root, L12Catalog catalog, L12PlatformStore platform,
            MatchRecorder recorder, L12RoomManager rooms, L12WebSocketServer server,
            L12DeploymentDrainCoordinator? coordinator)
        {
            _root = root;
            Catalog = catalog;
            Platform = platform;
            Recorder = recorder;
            Rooms = rooms;
            Server = server;
            Coordinator = coordinator;
        }

        internal L12Catalog Catalog { get; }
        internal L12PlatformStore Platform { get; }
        internal MatchRecorder Recorder { get; }
        internal L12RoomManager Rooms { get; }
        internal L12WebSocketServer Server { get; }
        internal L12DeploymentDrainCoordinator? Coordinator { get; }
        internal Uri Endpoint { get; private set; } = null!;

        internal static async Task<WebSocketFixture> CreateAsync(bool withCoordinator)
        {
            var root = CreateArtifactDirectory();
            try
            {
                var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory,
                    "TwelveLegions", "Data"));
                var platform = new L12PlatformStore(Path.Combine(root, "platform.json"),
                    catalog.PresetDecks, officialCards: catalog.Cards);
                var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
                await recorder.InitializeAsync();
                var rooms = new L12RoomManager(catalog, recorder, platform);
                L12DeploymentDrainCoordinator? coordinator = null;
                if (withCoordinator)
                {
                    coordinator = CreateCoordinator();
                    rooms.AttachDeploymentDrain(coordinator);
                }
                var server = new L12WebSocketServer(rooms, recorder, platform, catalog);
                return new WebSocketFixture(root, catalog, platform, recorder, rooms, server,
                    coordinator);
            }
            catch
            {
                DeleteArtifactDirectory(root);
                throw;
            }
        }

        internal async Task StartAsync()
        {
            await Server.StartAsync(0);
            var http = new UriBuilder(Assert.Single(Server.Addresses)) { Host = "127.0.0.1" };
            Endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            _started = true;
        }

        internal (L12AccountView Account, string Token) CreateAccount(string prefix)
        {
            var identity = Guid.NewGuid().ToString("N")[..8];
            var registration = Platform.Register($"{prefix[..Math.Min(3, prefix.Length)]}{identity}", "Password123!");
            Assert.True(registration.Success, registration.Message);
            return (registration.Account!, registration.Token!);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_started) await Server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await Server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                await Recorder.DisposeAsync();
                DeleteArtifactDirectory(_root);
            }
        }
    }
}
