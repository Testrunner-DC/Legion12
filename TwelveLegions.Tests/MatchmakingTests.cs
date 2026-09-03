using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MatchmakingTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RankedQueuePairsDifferentAccountsAndStartsLockedGameImmediately()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-matchmaking", Guid.NewGuid().ToString("N"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var first = platform.Register("queue-first", "Password123!").Account!;
        var second = platform.Register("queue-second", "Password123!").Account!;
        platform.SelectRankedFaction(first.Id, "order");
        platform.SelectRankedFaction(second.Id, "chaos");
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Connect(firstSession, first.Id, first.Username);
        manager.Connect(secondSession, second.Id, second.Username);

        var queued = await manager.JoinMatchmakingAsync(firstSession, "ranked", null);
        var matched = await manager.JoinMatchmakingAsync(secondSession, "ranked", null);

        Assert.Equal("matchmakingState", JsonSerializer.SerializeToElement(Assert.Single(queued).Payload, WebJson)
            .GetProperty("type").GetString());
        Assert.Contains(matched, message => message.SessionId == firstSession
            && JsonSerializer.SerializeToElement(message.Payload, WebJson).GetProperty("type").GetString() == "matchmakingFound");
        foreach (var sessionId in new[] { firstSession, secondSession })
        {
            var payloads = matched.Where(message => message.SessionId == sessionId)
                .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson)).ToArray();
            var found = Assert.Single(payloads,
                payload => payload.GetProperty("type").GetString() == "matchmakingFound");
            var room = Assert.Single(payloads,
                payload => payload.GetProperty("type").GetString() == "roomState");
            var game = Assert.Single(payloads,
                payload => payload.GetProperty("type").GetString() == "gameState");
            Assert.Equal(room.GetProperty("roomCode").GetString(), found.GetProperty("roomCode").GetString());
            Assert.Equal(game.GetProperty("state").GetProperty("matchId").GetString(),
                found.GetProperty("matchId").GetString());
            Assert.True(room.GetProperty("started").GetBoolean());
        }
        var firstGame = matched.Where(message => message.SessionId == firstSession)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
            .Single(payload => payload.GetProperty("type").GetString() == "gameState");
        Assert.Equal("Initiative", firstGame.GetProperty("state").GetProperty("phase").GetString());

        var stalePoll = (await manager.PollMatchmakingAsync(firstSession))
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson)).ToArray();
        Assert.DoesNotContain(stalePoll,
            payload => payload.GetProperty("type").GetString() == "matchmakingState");
        Assert.Contains(stalePoll, payload => payload.GetProperty("type").GetString() == "roomState");
        Assert.Contains(stalePoll, payload => payload.GetProperty("type").GetString() == "gameState");
    }

    [Fact]
    public async Task DuplicateAccountCannotMatchItself()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-matchmaking-self", Guid.NewGuid().ToString("N"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var account = platform.Register("queue-self", "Password123!").Account!;
        platform.SelectRankedFaction(account.Id, "order");
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Connect(firstSession, account.Id, account.Username);
        manager.Connect(secondSession, account.Id, account.Username);

        await manager.JoinMatchmakingAsync(firstSession, "ranked", null);
        var result = await manager.JoinMatchmakingAsync(secondSession, "ranked", null);

        var payload = JsonSerializer.SerializeToElement(Assert.Single(result).Payload, WebJson);
        Assert.Equal("matchmakingState", payload.GetProperty("type").GetString());
        Assert.True(payload.GetProperty("queued").GetBoolean());
    }

    [Fact]
    public async Task PollingPreservesOriginalQueueTimeInsteadOfRestartingRatingExpansion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-matchmaking-poll", Guid.NewGuid().ToString("N"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var account = platform.Register("queue-poll", "Password123!").Account!;
        platform.SelectRankedFaction(account.Id, "order");
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, platform);
        var session = Guid.NewGuid();
        manager.Connect(session, account.Id, account.Username);

        var queued = JsonSerializer.SerializeToElement(
            Assert.Single(await manager.JoinMatchmakingAsync(session, "ranked", null)).Payload, WebJson);
        await Task.Delay(25);
        var polled = JsonSerializer.SerializeToElement(
            Assert.Single(await manager.PollMatchmakingAsync(session)).Payload, WebJson);

        Assert.True(polled.GetProperty("queued").GetBoolean());
        Assert.Equal(queued.GetProperty("joinedAt").GetDateTimeOffset(),
            polled.GetProperty("joinedAt").GetDateTimeOffset());
    }
}
