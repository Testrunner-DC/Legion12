using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LongChainJournalRecoveryTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> HistoricalFailureScenarios()
        => LongChainAdversarialHarness.Scenarios.Select(scenario => new object[] { scenario });

    [Theory]
    [MemberData(nameof(HistoricalFailureScenarios))]
    [Trait("L12Evidence", "long-chain-journal-recovery")]
    public async Task HistoricalFailureCardsKeepCheckpointJournalReplayAndProjectionEquivalence(
        LongChainScenario scenario)
    {
        var evidence = await LongChainAdversarialHarness.RunAsync(scenario);

        Assert.NotEmpty(evidence.FinalStateHash);
        Assert.Contains("after-prelude", evidence.Cutpoints);
        Assert.Contains("after-focus", evidence.Cutpoints);
        Assert.Contains("after-disaster-and-trial", evidence.Cutpoints);
        Assert.Contains(evidence.Cutpoints, item => item.StartsWith("journal-sequence-", StringComparison.Ordinal));
        Assert.True(evidence.RejectedCommands >= 3,
            $"{scenario.Id} 未形成越权、非法与过期重复请求证据");
    }

    [Fact]
    [Trait("L12Evidence", "long-chain-at-most-once-request-id")]
    public async Task SamePlayerAndRequestIdIsAppliedAtMostOnceAcrossDelayReorderAndReconnect()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-lc01-request-id", Guid.NewGuid().ToString("N"));
        var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        try
        {
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(Catalog, recorder);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            manager.Connect(first, "lc01-account-a", "甲");
            manager.Connect(second, "lc01-account-b", "乙");
            var roomCode = Payload(manager.CreateRoom(first)[0]).GetProperty("roomCode").GetString();
            manager.JoinRoom(second, roomCode);
            await manager.SetReadyAsync(first, true);
            var started = await manager.SetReadyAsync(second, true);
            var matchId = Payload(started.Single(message => message.SessionId == first
                && Payload(message).GetProperty("type").GetString() == "gameState"))
                .GetProperty("state").GetProperty("matchId").GetString()!;

            const string delayedId = "lc01-delayed-out-of-order";
            var delayed = JsonSerializer.SerializeToElement(new
            {
                type = "resolvePrompt", promptId = "lc01-not-issued", choice = "pass",
            });
            var firstRejection = Payload(Assert.Single(await manager.HandleActionAsync(first, delayed, delayedId)));
            Assert.Equal("actionRejected", firstRejection.GetProperty("type").GetString());
            Assert.Equal(delayedId, firstRejection.GetProperty("requestId").GetString());
            var afterFirstRejection = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
            Assert.Single(afterFirstRejection.Commands);
            var rejectedHash = afterFirstRejection.Commands[0].StateHash;

            var reorderedDifferentPayload = Payload(Assert.Single(await manager.HandleActionAsync(first,
                JsonSerializer.SerializeToElement(new { type = "surrender" }), delayedId)));
            Assert.Equal("actionRejected", reorderedDifferentPayload.GetProperty("type").GetString());
            Assert.Equal(delayedId, reorderedDifferentPayload.GetProperty("requestId").GetString());
            var afterReorderedDuplicate = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
            Assert.Single(afterReorderedDuplicate.Commands);
            Assert.Equal(rejectedHash, afterReorderedDuplicate.Commands[0].StateHash);

            const string acceptedId = "lc01-accepted-reconnect-retry";
            var surrender = JsonSerializer.SerializeToElement(new { type = "surrender" });
            var accepted = await manager.HandleActionAsync(first, surrender, acceptedId);
            var acceptedState = Payload(accepted.Single(message => message.SessionId == first
                && Payload(message).GetProperty("type").GetString() == "gameState"));
            var acceptedHash = acceptedState.GetProperty("state").GetProperty("stateHash").GetString();

            var replacement = Guid.NewGuid();
            var claim = await manager.ConnectAsync(replacement, "lc01-account-a", "甲");
            Assert.True(claim.Recovered);
            var duplicate = await manager.HandleActionAsync(replacement, surrender, acceptedId);
            var duplicateState = Payload(duplicate.Single(message => message.SessionId == replacement
                && Payload(message).GetProperty("type").GetString() == "gameState"));
            Assert.Equal(acceptedHash, duplicateState.GetProperty("state").GetProperty("stateHash").GetString());

            var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
            Assert.Equal(2, detail.Commands.Count);
            var recovery = Assert.IsType<L12JournalRecoveryState>(await recorder.LoadJournalEngineAsync(matchId));
            Assert.Equal(2, recovery.ProcessedRequests.Count);
            Assert.Equal(new[] { delayedId, acceptedId },
                recovery.ProcessedRequests.Select(request => request.RequestId));
            Assert.Equal(acceptedHash, recovery.Engine.ComputeStateHash());
        }
        finally
        {
            await recorder.DisposeAsync();
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Windows 上 SQLite 连接池可能短暂持有数据库句柄；不得覆盖原测试结果。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上，只容忍短暂句柄/权限竞态，不吞测试断言和其他异常。
            }
        }
    }

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
