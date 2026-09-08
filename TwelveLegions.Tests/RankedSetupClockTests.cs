using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

[Collection(SqlitePoolIsolationCollection.Name)]
public sealed class RankedSetupClockTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DisasterChoiceUsesFreshSixtySecondBudgetAndTimesOutExactlyOnceAtBoundary()
    {
        await using var fixture = await RankedSetupFixture.CreateAsync("disaster-boundary");
        await fixture.ResolveInitiativeAsync();
        var initialClock = await fixture.ClockForAsync();
        var actingPlayer = fixture.ActingPlayer(initialClock);
        var before = await fixture.StateForAsync(actingPlayer);
        var prompt = fixture.SinglePrompt(before, "disaster-ban");
        var expected = prompt.GetProperty("validChoices").EnumerateArray()
            .Select(choice => choice.GetString()!)
            .OrderBy(choice => prompt.GetProperty("data").GetProperty($"{choice}:cardId").GetString(),
                StringComparer.Ordinal)
            .ThenBy(choice => choice, StringComparer.Ordinal)
            .First();
        Assert.Equal(60_000, initialClock.GetProperty("operationLimitMs").GetInt64());
        Assert.All(initialClock.GetProperty("players").EnumerateArray(), player =>
            Assert.Equal(25 * 60_000, player.GetProperty("totalRemainingMs").GetInt64()));

        fixture.Clock.UtcNow += TimeSpan.FromMilliseconds(59_999);
        Assert.Empty(await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow));
        var durableBeforeBoundary = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        Assert.Equal(1, durableBeforeBoundary.OperationRemainingMs[actingPlayer]);
        Assert.All(durableBeforeBoundary.TotalRemainingMs, remaining => Assert.Equal(25 * 60_000, remaining));

        fixture.Clock.UtcNow += TimeSpan.FromMilliseconds(1);
        var batches = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow)));

        Assert.NotEmpty(batches.SelectMany(batch => batch));
        var after = await fixture.StateForAsync(0);
        Assert.Equal("DisasterPreparation", after.GetProperty("phase").GetString());
        Assert.Equal(1, after.GetProperty("disasterPreparationStep").GetInt32());
        Assert.Contains(after.GetProperty("bannedDisasters").EnumerateArray(), card =>
            card.GetProperty("instanceId").GetString() == expected);
        var nextClock = await fixture.ClockForAsync();
        Assert.Equal(60_000, nextClock.GetProperty("operationLimitMs").GetInt64());
        Assert.Equal(60_000, nextClock.GetProperty("players").EnumerateArray()
            .Single(player => player.GetProperty("acting").GetBoolean())
            .GetProperty("operationRemainingMs").GetInt64());
        Assert.All(nextClock.GetProperty("players").EnumerateArray(), player =>
            Assert.Equal(25 * 60_000, player.GetProperty("totalRemainingMs").GetInt64()));

        var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.Single(detail.Commands, command => TimeoutMarker(command.Command));
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        var afterDuplicateTick = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.Single(afterDuplicateTick.Commands, command => TimeoutMarker(command.Command));
    }

    [Fact]
    public async Task SimultaneousMulliganBudgetsAreIndependentAndDisconnectedTimeoutKeepsTheHand()
    {
        await using var fixture = await RankedSetupFixture.CreateAsync("mulligan-disconnect");
        await fixture.AdvanceToMulliganAsync();
        var startingClock = await fixture.ClockForAsync();
        Assert.Equal(60_000, startingClock.GetProperty("operationLimitMs").GetInt64());
        Assert.All(startingClock.GetProperty("players").EnumerateArray(), player =>
        {
            Assert.True(player.GetProperty("acting").GetBoolean());
            Assert.Equal(60_000, player.GetProperty("operationRemainingMs").GetInt64());
            Assert.Equal(25 * 60_000, player.GetProperty("totalRemainingMs").GetInt64());
        });
        var timedOutPlayer = 1;
        var originalHand = (await fixture.StateForAsync(timedOutPlayer)).GetProperty("players")[timedOutPlayer]
            .GetProperty("hand").EnumerateArray().Select(card => card.GetProperty("instanceId").GetString()).ToArray();

        fixture.Clock.UtcNow += TimeSpan.FromSeconds(20);
        await fixture.SubmitMulliganAsync(0, []);
        var afterFirst = await fixture.ClockForAsync();
        var first = afterFirst.GetProperty("players").EnumerateArray()
            .Single(player => player.GetProperty("playerIndex").GetInt32() == 0);
        var second = afterFirst.GetProperty("players").EnumerateArray()
            .Single(player => player.GetProperty("playerIndex").GetInt32() == timedOutPlayer);
        Assert.False(first.GetProperty("acting").GetBoolean());
        Assert.True(second.GetProperty("acting").GetBoolean());
        Assert.Equal(40_000, second.GetProperty("operationRemainingMs").GetInt64());

        fixture.Manager.Disconnect(fixture.SessionFor(timedOutPlayer));
        fixture.Clock.UtcNow += TimeSpan.FromMilliseconds(39_999);
        Assert.Empty(await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow));
        Assert.Equal("Mulligan", (await fixture.StateForAsync(0)).GetProperty("phase").GetString());

        fixture.Clock.UtcNow += TimeSpan.FromMilliseconds(1);
        var staleSelection = originalHand.Take(1).OfType<string>().ToList();
        var boundaryResult = await fixture.Manager.HandleActionAsync(fixture.SessionFor(timedOutPlayer),
            JsonSerializer.SerializeToElement(new { type = "mulligan", cardInstanceIds = staleSelection }, WebJson));

        Assert.DoesNotContain(boundaryResult.Select(MessageJson), payload =>
            payload.GetProperty("type").GetString() == "actionRejected");
        var immediateState = GameMessage(boundaryResult, fixture.SessionFor(timedOutPlayer)).GetProperty("state");
        Assert.NotEqual("Mulligan", immediateState.GetProperty("phase").GetString());
        var afterTimeout = await fixture.StateForAsync(timedOutPlayer);
        Assert.NotEqual("Mulligan", afterTimeout.GetProperty("phase").GetString());
        var retainedHand = afterTimeout.GetProperty("players")[timedOutPlayer].GetProperty("hand")
            .EnumerateArray().Select(card => card.GetProperty("instanceId").GetString()).ToArray();
        Assert.Equal(originalHand, retainedHand);
        var normalClock = await fixture.ClockForAsync();
        Assert.Equal(4 * 60_000, normalClock.GetProperty("operationLimitMs").GetInt64());
        Assert.All(normalClock.GetProperty("players").EnumerateArray(), player =>
            Assert.Equal(25 * 60_000, player.GetProperty("totalRemainingMs").GetInt64()));

        var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.Single(detail.Commands, command => TimeoutMarker(command.Command));
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.MatchId));
        Assert.Single(detail.Commands, command => TimeoutMarker(command.Command));
    }

    [Fact]
    public async Task RestartPreservesPartialSetupBudgetWithoutChargingDowntimeOrGrantingANewMinute()
    {
        await using var fixture = await RankedSetupFixture.CreateAsync("restart-partial");
        await fixture.AdvanceToMulliganAsync();
        fixture.Clock.UtcNow += TimeSpan.FromSeconds(17);
        await fixture.Manager.TickRankedClocksAsync(fixture.Clock.UtcNow);
        var checkpoint = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        Assert.All(checkpoint.OperationRemainingMs, remaining => Assert.Equal(43_000, remaining));

        fixture.Clock.UtcNow += TimeSpan.FromMinutes(2);
        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, fixture.ReloadPlatform(),
            () => fixture.Clock.UtcNow);
        var summary = await restored.RestoreRankedRoomsAsync();
        Assert.Equal(1, summary.Restored);
        Assert.Equal(0, summary.Invalidated);

        var firstReplacement = Guid.NewGuid();
        var secondReplacement = Guid.NewGuid();
        await restored.ConnectAsync(firstReplacement, fixture.First.Id, fixture.First.Username);
        await restored.ConnectAsync(secondReplacement, fixture.Second.Id, fixture.Second.Username);
        var recoveredClock = GameMessage(await restored.RecoveryStateWithAckAsync(firstReplacement, recovered: true),
                firstReplacement)
            .GetProperty("rankedClock");
        Assert.All(recoveredClock.GetProperty("players").EnumerateArray(), player =>
        {
            Assert.Equal(43_000, player.GetProperty("operationRemainingMs").GetInt64());
            Assert.Equal(25 * 60_000, player.GetProperty("totalRemainingMs").GetInt64());
        });

        fixture.Clock.UtcNow += TimeSpan.FromMilliseconds(42_999);
        Assert.Empty(await restored.TickRankedClocksAsync(fixture.Clock.UtcNow));
        fixture.Clock.UtcNow += TimeSpan.FromMilliseconds(1);
        await restored.TickRankedClocksAsync(fixture.Clock.UtcNow);
        var recoveredState = GameMessage(await restored.RecoveryStateWithAckAsync(firstReplacement), firstReplacement)
            .GetProperty("state");
        Assert.NotEqual("Mulligan", recoveredState.GetProperty("phase").GetString());
        var detail = Assert.IsType<L12MatchDetail>(await restoredRecorder.GetMatchAsync(fixture.MatchId));
        Assert.Equal(2, detail.Commands.Count(command => TimeoutMarker(command.Command)));
    }

    [Fact]
    public async Task NormalClockStartsAfterFinalMulliganProcessingWithoutChargingTheSetupActor()
    {
        await using var fixture = await RankedSetupFixture.CreateAsync("normal-clock-baseline");
        await fixture.AdvanceToMulliganAsync();
        await fixture.SubmitMulliganAsync(0, []);

        fixture.Clock.AdvanceAfterRead = TimeSpan.FromMilliseconds(250);
        await fixture.SubmitMulliganAsync(1, []);
        fixture.Clock.AdvanceAfterRead = TimeSpan.Zero;

        var checkpoint = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        Assert.All(checkpoint.TotalRemainingMs, remaining => Assert.Equal(25 * 60_000, remaining));
        Assert.All(checkpoint.OperationRemainingMs, remaining => Assert.Equal(4 * 60_000, remaining));
    }

    [Fact]
    public async Task RankedRoomsFreezeConfiguredLimitsAndRecoveryKeepsThePersistedSnapshot()
    {
        var frozen = new L12RankedTimeControlConfig(1800, 300, 180, 75, 90);
        await using var fixture = await RankedSetupFixture.CreateAsync("configured-freeze", frozen);
        var initialClock = await fixture.ClockForAsync();
        Assert.Equal(1_800_000, initialClock.GetProperty("totalLimitMs").GetInt64());
        Assert.Equal(0, initialClock.GetProperty("operationLimitMs").GetInt64());
        Assert.Equal(180_000, initialClock.GetProperty("reconnectLimitMs").GetInt64());
        Assert.Equal(75, initialClock.GetProperty("timeControl").GetProperty("disasterDecisionSeconds").GetInt32());
        var checkpoint = Assert.IsType<L12RankedRuntimeCheckpoint>(
            await fixture.Recorder.GetRankedRuntimeCheckpointAsync(fixture.MatchId));
        Assert.Equal(frozen, checkpoint.TimeControl);

        var admin = fixture.Platform.Login("Admin", "L12master").Account!;
        var current = fixture.Platform.RankedConfig(admin);
        var next = new L12RankedTimeControlConfig(2100, 360, 210, 80, 100);
        fixture.Platform.UpdateRankedConfig(admin, current with { TimeControl = next },
            "只影响新建排位", new L12AdminAuditContext("ranked-room-freeze"));

        var unchangedClock = await fixture.ClockForAsync();
        Assert.Equal(1_800_000, unchangedClock.GetProperty("totalLimitMs").GetInt64());
        Assert.Equal(0, unchangedClock.GetProperty("operationLimitMs").GetInt64());
        Assert.Equal(180_000, unchangedClock.GetProperty("reconnectLimitMs").GetInt64());
        await fixture.ResolveInitiativeAsync();
        Assert.Equal(75_000, (await fixture.ClockForAsync()).GetProperty("operationLimitMs").GetInt64());
        await fixture.AdvanceToMulliganAsync();
        Assert.Equal(90_000, (await fixture.ClockForAsync()).GetProperty("operationLimitMs").GetInt64());

        var identity = Guid.NewGuid().ToString("N")[..8];
        var first = fixture.Platform.Register($"nf{identity}", "Password123!").Account!;
        var second = fixture.Platform.Register($"ns{identity}", "Password123!").Account!;
        fixture.Platform.SelectRankedFaction(first.Id, "order");
        fixture.Platform.SelectRankedFaction(second.Id, "chaos");
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        fixture.Manager.Connect(firstSession, first.Id, first.Username);
        fixture.Manager.Connect(secondSession, second.Id, second.Username);
        await fixture.Manager.JoinMatchmakingAsync(firstSession, "ranked", null);
        var matched = await fixture.Manager.JoinMatchmakingAsync(secondSession, "ranked", null);
        var newClock = GameMessage(matched, firstSession).GetProperty("rankedClock");
        Assert.Equal(2_100_000, newClock.GetProperty("totalLimitMs").GetInt64());
        Assert.Equal(0, newClock.GetProperty("operationLimitMs").GetInt64());
        Assert.Equal(210_000, newClock.GetProperty("reconnectLimitMs").GetInt64());

        await using var restoredRecorder = new MatchRecorder(fixture.MatchPath);
        await restoredRecorder.InitializeAsync();
        var restored = new L12RoomManager(fixture.Catalog, restoredRecorder, fixture.ReloadPlatform(),
            fixture.Clock.Read);
        var summary = await restored.RestoreRankedRoomsAsync();
        Assert.Equal(2, summary.Restored);
        var replacement = Guid.NewGuid();
        await restored.ConnectAsync(replacement, fixture.First.Id, fixture.First.Username);
        var recovered = GameMessage(await restored.RecoveryStateWithAckAsync(replacement, recovered: true), replacement)
            .GetProperty("rankedClock");
        Assert.Equal(1_800_000, recovered.GetProperty("totalLimitMs").GetInt64());
        Assert.Equal(90_000, recovered.GetProperty("operationLimitMs").GetInt64());
        Assert.Equal(180_000, recovered.GetProperty("reconnectLimitMs").GetInt64());
        Assert.Equal(75, recovered.GetProperty("timeControl").GetProperty("disasterDecisionSeconds").GetInt32());
    }

    private static bool TimeoutMarker(JsonElement command)
        => command.TryGetProperty("destination", out var destination)
           && destination.GetString() == "ranked-setup-timeout";

    private static JsonElement MessageJson(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WebJson);

    private static JsonElement GameMessage(IReadOnlyList<OutgoingMessage> messages, Guid? sessionId = null)
        => messages.Where(message => sessionId is null || message.SessionId == sessionId).Select(MessageJson)
            .Single(payload => payload.GetProperty("type").GetString() == "gameState");

    private sealed class TestClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 6, 8, 0, 0, TimeSpan.Zero);
        public TimeSpan AdvanceAfterRead { get; set; }

        public DateTimeOffset Read()
        {
            var result = UtcNow;
            UtcNow += AdvanceAfterRead;
            return result;
        }
    }

    private sealed class RankedSetupFixture : IAsyncDisposable
    {
        private readonly string _directory;
        public required TestClock Clock { get; init; }
        public required L12Catalog Catalog { get; init; }
        public required L12PlatformStore Platform { get; init; }
        public required MatchRecorder Recorder { get; init; }
        public required L12RoomManager Manager { get; init; }
        public required L12AccountView First { get; init; }
        public required L12AccountView Second { get; init; }
        public required Guid FirstSession { get; init; }
        public required Guid SecondSession { get; init; }
        public required string MatchId { get; init; }
        public string MatchPath => Path.Combine(_directory, "matches.db");

        private RankedSetupFixture(string directory) => _directory = directory;

        public static async Task<RankedSetupFixture> CreateAsync(string suffix,
            L12RankedTimeControlConfig? timeControl = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"l12-ranked-setup-{suffix}-{Guid.NewGuid():N}");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            if (timeControl is not null)
            {
                var admin = platform.Login("Admin", "L12master").Account!;
                var config = platform.RankedConfig(admin);
                platform.UpdateRankedConfig(admin, config with { TimeControl = timeControl },
                    "测试排位计时配置", new L12AdminAuditContext("ranked-setup-fixture"));
            }
            var identity = Guid.NewGuid().ToString("N")[..8];
            var first = platform.Register($"sf{identity}", "Password123!").Account!;
            var second = platform.Register($"ss{identity}", "Password123!").Account!;
            platform.SelectRankedFaction(first.Id, "order");
            platform.SelectRankedFaction(second.Id, "chaos");
            var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var clock = new TestClock();
            var manager = new L12RoomManager(catalog, recorder, platform, clock.Read);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            manager.Connect(firstSession, first.Id, first.Username);
            manager.Connect(secondSession, second.Id, second.Username);
            await manager.JoinMatchmakingAsync(firstSession, "ranked", null);
            var matched = await manager.JoinMatchmakingAsync(secondSession, "ranked", null);
            var game = matched.Where(message => message.SessionId == firstSession).Select(MessageJson)
                .Single(payload => payload.GetProperty("type").GetString() == "gameState");
            return new RankedSetupFixture(directory)
            {
                Clock = clock,
                Catalog = catalog,
                Platform = platform,
                Recorder = recorder,
                Manager = manager,
                First = first,
                Second = second,
                FirstSession = firstSession,
                SecondSession = secondSession,
                MatchId = game.GetProperty("state").GetProperty("matchId").GetString()!,
            };
        }

        public Guid SessionFor(int playerIndex) => playerIndex == 0 ? FirstSession : SecondSession;

        public L12PlatformStore ReloadPlatform()
            => new(Path.Combine(_directory, "platform.json"), Catalog.PresetDecks, officialCards: Catalog.Cards);

        public async Task<JsonElement> StateForAsync(int playerIndex)
            => GameMessage(await Manager.RecoveryStateWithAckAsync(SessionFor(playerIndex)), SessionFor(playerIndex))
                .GetProperty("state").Clone();

        public async Task<JsonElement> ClockForAsync()
            => GameMessage(await Manager.RecoveryStateWithAckAsync(FirstSession), FirstSession)
                .GetProperty("rankedClock").Clone();

        public int ActingPlayer(JsonElement clock)
            => clock.GetProperty("players").EnumerateArray().Single(player =>
                player.GetProperty("acting").GetBoolean()).GetProperty("playerIndex").GetInt32();

        public JsonElement SinglePrompt(JsonElement state, string kind)
            => state.GetProperty("prompts").EnumerateArray().Single(prompt =>
                prompt.GetProperty("kind").GetString() == kind);

        public async Task ResolveInitiativeAsync()
        {
            for (var player = 0; player < 2; player++)
            {
                var state = await StateForAsync(player);
                var prompt = state.GetProperty("prompts").EnumerateArray().FirstOrDefault();
                if (prompt.ValueKind == JsonValueKind.Undefined) continue;
                if (prompt.GetProperty("kind").GetString() != "initiative") continue;
                await SubmitPromptAsync(player, prompt, ["first"]);
                return;
            }
            throw new InvalidOperationException("缺少先后手 Prompt");
        }

        public async Task AdvanceToMulliganAsync()
        {
            for (var step = 0; step < 40; step++)
            {
                var states = new[] { await StateForAsync(0), await StateForAsync(1) };
                if (states[0].GetProperty("phase").GetString() == "Mulligan") return;
                JsonElement prompt = default;
                var owner = -1;
                for (var player = 0; player < 2; player++)
                {
                    var prompts = states[player].GetProperty("prompts").EnumerateArray().ToArray();
                    if (prompts.Length == 0) continue;
                    prompt = prompts[0];
                    owner = player;
                    break;
                }
                if (owner < 0) throw new InvalidOperationException("准备流程没有可处理的 Prompt");
                var kind = prompt.GetProperty("kind").GetString();
                var valid = prompt.GetProperty("validChoices").EnumerateArray()
                    .Select(choice => choice.GetString()!).ToArray();
                var choices = kind switch
                {
                    "initiative" => new[] { "first" },
                    "disaster-ban" or "disaster-pick" => valid.Take(1).ToArray(),
                    "disaster-reveal" => [],
                    "optional" when valid.Contains("no") => new[] { "no" },
                    "trial-order" => valid,
                    _ => throw new InvalidOperationException($"未识别的准备 Prompt：{kind}"),
                };
                await SubmitPromptAsync(owner, prompt, choices);
            }
            throw new InvalidOperationException("准备流程未在有界步骤内进入调度");
        }

        public async Task SubmitMulliganAsync(int playerIndex, IReadOnlyCollection<string> ids)
        {
            var result = await Manager.HandleActionAsync(SessionFor(playerIndex),
                JsonSerializer.SerializeToElement(new { type = "mulligan", cardInstanceIds = ids }, WebJson));
            Assert.DoesNotContain(result.Select(MessageJson), payload =>
                payload.GetProperty("type").GetString() == "actionRejected");
        }

        private async Task SubmitPromptAsync(int playerIndex, JsonElement prompt, IReadOnlyCollection<string> choices)
        {
            var result = await Manager.HandleActionAsync(SessionFor(playerIndex),
                JsonSerializer.SerializeToElement(new
                {
                    type = "resolvePrompt",
                    promptId = prompt.GetProperty("promptId").GetString(),
                    cardInstanceIds = choices,
                }, WebJson));
            Assert.DoesNotContain(result.Select(MessageJson), payload =>
                payload.GetProperty("type").GetString() == "actionRejected");
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            if (!Directory.Exists(_directory)) return;
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = MatchPath,
            }.ToString();
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                using (var poolKey = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                    Microsoft.Data.Sqlite.SqliteConnection.ClearPool(poolKey);
                try
                {
                    Directory.Delete(_directory, true);
                    return;
                }
                catch (IOException) when (OperatingSystem.IsWindows() && attempt < 8)
                {
                    await Task.Delay(50 * attempt);
                }
            }
        }
    }
}
