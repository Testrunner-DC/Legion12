using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class WebSocketTransportRecoveryAdversarialTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RealWebSocketReconnectReplayAndRecipientProjectionsConvergeWithoutPrivateLeaks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-lc03a-ws", Guid.NewGuid().ToString("N"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var identity = Guid.NewGuid().ToString("N")[..8];
        var firstAccount = platform.Register($"wa{identity}", "Password123!").Account!;
        var secondAccount = platform.Register($"wb{identity}", "Password123!").Account!;
        var spectatorAccount = platform.Register($"ws{identity}", "Password123!").Account!;
        var firstToken = platform.Login(firstAccount.Username, "Password123!").Token!;
        var secondToken = platform.Login(secondAccount.Username, "Password123!").Token!;
        var spectatorToken = platform.Login(spectatorAccount.Username, "Password123!").Token!;

        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        await using var server = new L12WebSocketServer(manager, recorder, platform, catalog);
        await server.StartAsync(0);
        try
        {
            var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
            var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
            await using var first = await WireClient.ConnectAsync(endpoint, firstToken);
            await using var second = await WireClient.ConnectAsync(endpoint, secondToken);
            await using var spectator = await WireClient.ConnectAsync(endpoint, spectatorToken);

            await first.SendAsync(new
            {
                type = "createRoom",
                options = new
                {
                    spectating = "public",
                    handVisibility = "request",
                    disasterMode = "all",
                },
            });
            var room = await first.ReceiveTypeAsync("roomState");
            var roomCode = room["roomCode"]!.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(roomCode));

            await second.SendAsync(new { type = "joinRoom", roomCode });
            _ = await second.ReceiveTypeAsync("roomState");
            await first.SendAsync(new { type = "ready", ready = true });
            await second.SendAsync(new { type = "ready", ready = true });

            var firstInitial = await first.ReceiveGameAsync(state => Phase(state) == "Initiative");
            var initialRevision = Revision(firstInitial);
            var secondInitial = await second.ReceiveGameAsync(state => Revision(state) == initialRevision);
            Assert.Equal(StateHash(firstInitial), StateHash(secondInitial));
            var matchId = MatchId(firstInitial);

            await spectator.SendAsync(new { type = "spectateRoom", roomCode });
            _ = await spectator.ReceiveTypeAsync("roomState");
            var spectatorInitial = await spectator.ReceiveGameAsync(state => MatchId(state) == matchId);
            Assert.Equal(initialRevision, Revision(spectatorInitial));
            Assert.Equal(StateHash(firstInitial), StateHash(spectatorInitial));

            var prepared = await AdvancePreparationToMulliganAsync(first, second, spectator,
                firstInitial, secondInitial, spectatorInitial);
            firstInitial = prepared.First;
            secondInitial = prepared.Second;
            spectatorInitial = prepared.Spectator;
            initialRevision = Revision(firstInitial);
            var preparationCommandCount = (await recorder.GetMatchAsync(matchId))!.Commands.Count;
            Assert.True(preparationCommandCount > 0);

            var firstPrivateIds = PrivateHandIds(firstInitial, 0);
            var secondPrivateIds = PrivateHandIds(secondInitial, 1);
            Assert.NotEmpty(firstPrivateIds);
            Assert.NotEmpty(secondPrivateIds);
            AssertRecipientPrivacy(firstInitial, 0, firstPrivateIds, secondPrivateIds);
            AssertRecipientPrivacy(secondInitial, 1, secondPrivateIds, firstPrivateIds);
            AssertSpectatorPrivacy(spectatorInitial, firstPrivateIds, secondPrivateIds);

            const string firstMulliganId = "lc03a-mulligan-player-0";
            await first.SendAsync(new
            {
                type = "gameAction",
                requestId = firstMulliganId,
                command = new { type = "mulligan", cardInstanceIds = Array.Empty<string>() },
            });
            var firstAfterMulligan = await first.ReceiveGameAsync(state => Revision(state) > initialRevision);
            var afterFirstRevision = Revision(firstAfterMulligan);
            Assert.Equal(firstMulliganId, first.CurrentPayload!["requestId"]?.GetValue<string>());
            var secondAfterFirst = await second.ReceiveGameAsync(state => Revision(state) == afterFirstRevision);
            var spectatorAfterFirst = await spectator.ReceiveGameAsync(state => Revision(state) == afterFirstRevision);
            Assert.Equal(StateHash(firstAfterMulligan), StateHash(secondAfterFirst));
            Assert.Equal(StateHash(firstAfterMulligan), StateHash(spectatorAfterFirst));

            var stateBeforeDuplicate = firstAfterMulligan.DeepClone();
            await first.SendAsync(new
            {
                type = "gameAction",
                requestId = firstMulliganId,
                command = new { type = "surrender" },
            });
            var duplicateFirst = await first.ReceiveGameAsync(state => Revision(state) == afterFirstRevision);
            _ = await second.ReceiveGameAsync(state => Revision(state) == afterFirstRevision);
            _ = await spectator.ReceiveGameAsync(state => Revision(state) == afterFirstRevision);
            Assert.Equal(firstMulliganId, first.CurrentPayload!["requestId"]?.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(stateBeforeDuplicate, duplicateFirst),
                "同一 requestId 的延迟异载荷重放改变了权威投影");
            Assert.NotEqual("GameOver", Phase(duplicateFirst));
            Assert.Equal(preparationCommandCount + 1,
                (await recorder.GetMatchAsync(matchId))!.Commands.Count);

            await using var firstReplacement = await WireClient.ConnectAsync(endpoint, firstToken);
            var supersededFirst = await first.ReceiveTypeAsync("sessionSuperseded");
            Assert.Equal("newer-connection-generation", supersededFirst["reason"]?.GetValue<string>());
            Assert.True(firstReplacement.Session!["recovered"]!.GetValue<bool>());
            Assert.Equal(first.ConnectionGeneration + 1, firstReplacement.ConnectionGeneration);
            Assert.Equal(roomCode, firstReplacement.Recovery!["roomCode"]?.GetValue<string>());
            Assert.Equal(afterFirstRevision, firstReplacement.Recovery["recoveryRevision"]?.GetValue<long>());
            Assert.NotNull(firstReplacement.CurrentState);
            Assert.True(JsonNode.DeepEquals(duplicateFirst, firstReplacement.CurrentState),
                "玩家替代连接没有从完整权威快照收敛");
            Assert.True(firstReplacement.FullGameStateCount > 0);

            await TrySendFromFencedSocketAsync(first, new
            {
                type = "gameAction",
                requestId = "lc03a-fenced-old-socket",
                command = new { type = "surrender" },
            });
            await firstReplacement.SendAsync(new { type = "syncState" });
            var afterOldSocketAttempt = await firstReplacement.ReceiveGameAsync(
                state => Revision(state) == afterFirstRevision);
            Assert.True(JsonNode.DeepEquals(duplicateFirst, afterOldSocketAttempt),
                "旧连接的延迟命令覆盖了新连接恢复状态");
            Assert.Equal(preparationCommandCount + 1,
                (await recorder.GetMatchAsync(matchId))!.Commands.Count);

            const string secondMulliganId = "lc03a-mulligan-player-1";
            await second.SendAsync(new
            {
                type = "gameAction",
                requestId = secondMulliganId,
                command = new { type = "mulligan", cardInstanceIds = Array.Empty<string>() },
            });
            var secondMain = await second.ReceiveGameAsync(state => Revision(state) > afterFirstRevision);
            var mainRevision = Revision(secondMain);
            Assert.Equal("Main", Phase(secondMain));
            Assert.Equal(secondMulliganId, second.CurrentPayload!["requestId"]?.GetValue<string>());
            var firstMain = await firstReplacement.ReceiveGameAsync(state => Revision(state) == mainRevision);
            var spectatorMain = await spectator.ReceiveGameAsync(state => Revision(state) == mainRevision);
            Assert.Equal(StateHash(firstMain), StateHash(secondMain));
            Assert.Equal(StateHash(firstMain), StateHash(spectatorMain));

            var activePlayer = firstMain["activePlayer"]!.GetValue<int>();
            var actor = activePlayer == 0 ? firstReplacement : second;
            const string endTurnId = "lc03a-end-turn";
            await actor.SendAsync(new
            {
                type = "gameAction",
                requestId = endTurnId,
                command = new { type = "endTurn" },
            });
            var actorFinal = await actor.ReceiveGameAsync(state => Revision(state) > mainRevision);
            var finalRevision = Revision(actorFinal);
            Assert.Equal(endTurnId, actor.CurrentPayload!["requestId"]?.GetValue<string>());
            var firstFinal = activePlayer == 0
                ? actorFinal
                : await firstReplacement.ReceiveGameAsync(state => Revision(state) == finalRevision);
            var secondFinal = activePlayer == 1
                ? actorFinal
                : await second.ReceiveGameAsync(state => Revision(state) == finalRevision);
            var spectatorFinal = await spectator.ReceiveGameAsync(state => Revision(state) == finalRevision);
            Assert.Equal(StateHash(firstFinal), StateHash(secondFinal));
            Assert.Equal(StateHash(firstFinal), StateHash(spectatorFinal));
            Assert.True(firstReplacement.DeltaGameStateCount + second.DeltaGameStateCount
                        + spectator.DeltaGameStateCount > 0,
                "真实 WebSocket 链没有产生可校验的增量状态");

            AssertRecipientPrivacy(firstFinal, 0, firstPrivateIds, secondPrivateIds);
            AssertRecipientPrivacy(secondFinal, 1, secondPrivateIds, firstPrivateIds);
            AssertSpectatorPrivacy(spectatorFinal, firstPrivateIds, secondPrivateIds);

            var staleSpectatorDelta = Assert.IsType<byte[]>(spectator.LastDeltaPayload);
            await using var spectatorReplacement = await WireClient.ConnectAsync(endpoint, spectatorToken);
            var supersededSpectator = await spectator.ReceiveTypeAsync("sessionSuperseded");
            Assert.Equal("newer-connection-generation", supersededSpectator["reason"]?.GetValue<string>());
            Assert.True(spectatorReplacement.Session!["recovered"]!.GetValue<bool>());
            Assert.Equal(spectator.ConnectionGeneration + 1, spectatorReplacement.ConnectionGeneration);
            Assert.NotNull(spectatorReplacement.CurrentState);
            Assert.True(JsonNode.DeepEquals(spectatorFinal, spectatorReplacement.CurrentState),
                "普通观战者替代连接没有按原接收者权限恢复");
            AssertSpectatorPrivacy(spectatorReplacement.CurrentState!, firstPrivateIds, secondPrivateIds);
            Assert.Throws<InvalidDataException>(() =>
                WireClient.ApplyDeltaStrict(spectatorReplacement.CurrentPayload!, staleSpectatorDelta));

            await firstReplacement.SendAsync(new { type = "syncState" });
            var firstSynchronized = await firstReplacement.ReceiveGameAsync(
                state => Revision(state) == finalRevision);
            var secondSynchronized = await second.ReceiveGameAsync(state => Revision(state) == finalRevision);
            var spectatorSynchronized = await spectatorReplacement.ReceiveGameAsync(
                state => Revision(state) == finalRevision);
            Assert.True(JsonNode.DeepEquals(firstFinal, firstSynchronized));
            Assert.True(JsonNode.DeepEquals(secondFinal, secondSynchronized));
            Assert.True(JsonNode.DeepEquals(spectatorFinal, spectatorSynchronized));
            Assert.Equal(preparationCommandCount + 3,
                (await recorder.GetMatchAsync(matchId))!.Commands.Count);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static async Task<(JsonObject First, JsonObject Second, JsonObject Spectator)>
        AdvancePreparationToMulliganAsync(WireClient first, WireClient second, WireClient spectator,
            JsonObject firstState, JsonObject secondState, JsonObject spectatorState)
    {
        for (var step = 0; step < 40; step++)
        {
            if (Phase(firstState) == "Mulligan")
                return (firstState, secondState, spectatorState);

            var firstPrompts = firstState["prompts"]!.AsArray();
            var secondPrompts = secondState["prompts"]!.AsArray();
            var owner = firstPrompts.Count > 0 ? 0 : secondPrompts.Count > 0 ? 1 : -1;
            Assert.True(owner >= 0, $"准备阶段 {Phase(firstState)} 没有可处理的 Prompt");
            var prompt = (owner == 0 ? firstPrompts[0] : secondPrompts[0])!.AsObject();
            var kind = prompt["kind"]!.GetValue<string>();
            var valid = prompt["validChoices"]!.AsArray()
                .Select(choice => choice!.GetValue<string>()).ToArray();
            var choices = kind switch
            {
                "initiative" => new[] { "first" },
                "disaster-ban" or "disaster-pick" => valid.Take(1).ToArray(),
                "disaster-reveal" => Array.Empty<string>(),
                "optional" when valid.Contains("no", StringComparer.Ordinal) => new[] { "no" },
                "trial-order" => valid,
                _ => throw new Xunit.Sdk.XunitException($"未识别的准备 Prompt：{kind}"),
            };
            var before = Revision(firstState);
            var actor = owner == 0 ? first : second;
            var requestId = $"lc03a-setup-{step}-{kind}";
            await actor.SendAsync(new
            {
                type = "gameAction",
                requestId,
                command = new
                {
                    type = "resolvePrompt",
                    promptId = prompt["promptId"]!.GetValue<string>(),
                    cardInstanceIds = choices,
                },
            });
            var actorState = await actor.ReceiveGameAsync(state => Revision(state) > before);
            Assert.Equal(requestId, actor.CurrentPayload!["requestId"]?.GetValue<string>());
            var revision = Revision(actorState);
            if (owner == 0)
            {
                firstState = actorState;
                secondState = await second.ReceiveGameAsync(state => Revision(state) == revision);
            }
            else
            {
                secondState = actorState;
                firstState = await first.ReceiveGameAsync(state => Revision(state) == revision);
            }
            spectatorState = await spectator.ReceiveGameAsync(state => Revision(state) == revision);
            Assert.Equal(StateHash(firstState), StateHash(secondState));
            Assert.Equal(StateHash(firstState), StateHash(spectatorState));
        }
        throw new Xunit.Sdk.XunitException("真实 WebSocket 准备流程未在 40 步内进入调度阶段");
    }

    private static async Task TrySendFromFencedSocketAsync(WireClient client, object payload)
    {
        try
        {
            await client.SendAsync(payload);
        }
        catch (Exception error) when (error is WebSocketException or InvalidOperationException
                                     or ObjectDisposedException)
        {
            // Transport-level rejection is an accepted fenced-old-connection outcome.
        }
    }

    private static HashSet<string> PrivateHandIds(JsonObject state, int playerIndex)
        => state["players"]!.AsArray()[playerIndex]!["hand"]!.AsArray()
            .Select(card => card!["instanceId"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

    private static void AssertRecipientPrivacy(JsonObject state, int viewerIndex,
        IReadOnlySet<string> ownPrivateIds, IReadOnlySet<string> opponentPrivateIds)
    {
        var players = state["players"]!.AsArray();
        Assert.NotNull(players[viewerIndex]!["hand"]);
        Assert.Null(players[1 - viewerIndex]!["hand"]);
        Assert.NotNull(players[1 - viewerIndex]!["handCount"]);
        var serialized = state.ToJsonString();
        Assert.All(ownPrivateIds, id => Assert.Contains(id, serialized, StringComparison.Ordinal));
        Assert.All(opponentPrivateIds, id => Assert.DoesNotContain(id, serialized, StringComparison.Ordinal));
    }

    private static void AssertSpectatorPrivacy(JsonObject state, params IReadOnlySet<string>[] privateIdSets)
    {
        var players = state["players"]!.AsArray();
        Assert.All(players, player =>
        {
            Assert.Null(player!["hand"]);
            Assert.NotNull(player["handCount"]);
        });
        Assert.Empty(state["prompts"]!.AsArray());
        var serialized = state.ToJsonString();
        foreach (var ids in privateIdSets)
        foreach (var id in ids)
            Assert.DoesNotContain(id, serialized, StringComparison.Ordinal);
        Assert.Contains(state["sessionDisasters"]!.AsArray(), item => item?["hidden"]?.GetValue<bool>() == true);
    }

    private static long Revision(JsonObject state) => state["revision"]!.GetValue<long>();
    private static string Phase(JsonObject state) => state["phase"]!.GetValue<string>();
    private static string MatchId(JsonObject state) => state["matchId"]!.GetValue<string>();
    private static string StateHash(JsonObject state) => state["stateHash"]!.GetValue<string>();

    private sealed class WireClient : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();

        private WireClient() { }

        internal JsonObject? Session { get; private set; }
        internal JsonObject? Recovery { get; private set; }
        internal JsonObject? CurrentPayload { get; private set; }
        internal JsonObject? CurrentState => CurrentPayload?["state"]?.AsObject();
        internal byte[]? LastDeltaPayload { get; private set; }
        internal int FullGameStateCount { get; private set; }
        internal int DeltaGameStateCount { get; private set; }
        internal long ConnectionGeneration => Session!["connectionGeneration"]!.GetValue<long>();

        internal static async Task<WireClient> ConnectAsync(Uri endpoint, string token)
        {
            var client = new WireClient();
            await client._socket.ConnectAsync(endpoint, CancellationToken.None);
            await client.SendAsync(new
            {
                type = "hello",
                authToken = token,
                capabilities = new { requestIds = true, deltaGameState = true },
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            for (var attempt = 0; attempt < 80 && client.Recovery is null; attempt++)
            {
                var message = await client.ReceiveMessageAsync(timeout.Token);
                var type = message.Node["type"]?.GetValue<string>();
                client.ApplyGameMessage(message.Bytes, message.Node);
                if (type == "session") client.Session = message.Node;
                if (type == "recoveryComplete") client.Recovery = message.Node;
            }
            Assert.NotNull(client.Session);
            Assert.NotNull(client.Recovery);
            return client;
        }

        internal async Task SendAsync(object payload)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        internal async Task<JsonObject> ReceiveTypeAsync(string expectedType)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var message = await ReceiveMessageAsync(timeout.Token);
                ApplyGameMessage(message.Bytes, message.Node);
                if (message.Node["type"]?.GetValue<string>() == expectedType) return message.Node;
            }
            throw new Xunit.Sdk.XunitException($"WebSocket did not receive {expectedType}");
        }

        internal async Task<JsonObject> ReceiveGameAsync(Func<JsonObject, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var message = await ReceiveMessageAsync(timeout.Token);
                if (!ApplyGameMessage(message.Bytes, message.Node)) continue;
                var state = Assert.IsType<JsonObject>(CurrentState?.DeepClone());
                if (predicate(state)) return state;
            }
            throw new Xunit.Sdk.XunitException("WebSocket did not receive the expected game revision");
        }

        private bool ApplyGameMessage(byte[] bytes, JsonObject message)
        {
            var type = message["type"]?.GetValue<string>();
            if (type == "gameState")
            {
                CurrentPayload = message;
                FullGameStateCount++;
                return true;
            }
            if (type != "gameStateDelta") return false;
            CurrentPayload = ApplyDeltaStrict(CurrentPayload
                ?? throw new InvalidDataException("增量到达前没有完整基线"), bytes);
            LastDeltaPayload = bytes.ToArray();
            DeltaGameStateCount++;
            return true;
        }

        internal static JsonObject ApplyDeltaStrict(JsonObject baseline, ReadOnlySpan<byte> deltaBytes)
        {
            var delta = JsonNode.Parse(deltaBytes)?.AsObject()
                ?? throw new InvalidDataException("增量载荷不是 JSON 对象");
            var baselineMatch = baseline["state"]?["matchId"]?.GetValue<string>();
            var baselineRevision = baseline["state"]?["revision"]?.GetValue<long>();
            if (!string.Equals(delta["matchId"]?.GetValue<string>(), baselineMatch,
                    StringComparison.Ordinal)
                || delta["baseRevision"]?.GetValue<long>() != baselineRevision)
                throw new InvalidDataException("增量基线与当前权威投影不匹配");
            return L12SnapshotWireCodec.ApplyDelta(baseline, deltaBytes);
        }

        private async Task<(byte[] Bytes, JsonObject Node)> ReceiveMessageAsync(
            CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException($"WebSocket closed: {_socket.CloseStatus} {_socket.CloseStatusDescription}");
                stream.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var bytes = stream.ToArray();
                var node = JsonNode.Parse(bytes)?.AsObject()
                    ?? throw new InvalidDataException("WebSocket 载荷不是 JSON 对象");
                return (bytes, node);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete",
                        CancellationToken.None);
            }
            catch (WebSocketException) { }
            _socket.Dispose();
        }
    }
}
