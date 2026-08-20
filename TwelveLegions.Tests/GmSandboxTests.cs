using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GmSandboxTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WebJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void EngineGmCommandsMutateAuthoritativeStateAndWriteEvents()
    {
        var game = new L12GameEngine(Catalog, "gm-engine", "GMTEST", 1201,
            ["甲", "乙"], [0, 1], skipPreparation: true, disasterMode: "all");
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        var definition = Catalog.Cards.Values.First(card => card.CardType == "legion");
        var add = game.HandleGm(new L12GmCommand("addCard", 1, definition.Id, Destination: "hand"));
        Assert.True(add.Accepted);
        Assert.Contains(game.State.Players[1].Hand, card => card.CardId == definition.Id);

        var place = game.HandleGm(new L12GmCommand("placeCard", 1, definition.Id, Row: 0, Slot: 1,
            TriggerEffects: false));
        Assert.True(place.Accepted);
        Assert.Equal(definition.Id, game.State.Players[1].Field[0][1]?.CardId);

        Assert.True(game.HandleGm(new L12GmCommand("setCardState", 1,
            CardInstanceId: game.State.Players[1].Field[0][1]!.InstanceId, Destination: "rested")).Accepted);
        Assert.True(game.State.Players[1].Field[0][1]!.Tapped);
        Assert.True(game.HandleGm(new L12GmCommand("setLife", 1, Value: 17)).Accepted);
        Assert.Equal(17, game.State.Players[1].Hp);
        Assert.True(game.HandleGm(new L12GmCommand("setDisaster", 0, Value: 6)).Accepted);
        Assert.Equal(6, game.State.DisasterValue);
        Assert.True(game.HandleGm(new L12GmCommand("setPhase", 1, Phase: "Main")).Accepted);
        Assert.Equal(1, game.State.ActivePlayer);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(game.State.Events, entry => entry.Type == "gm");
    }

    [Fact]
    public async Task RoomRejectsGmOutsideSandboxButSandboxControllerIsRecorded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-gm-sandbox", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);

        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        manager.Connect(host, "正式甲");
        manager.Connect(guest, "正式乙");
        var created = manager.CreateRoom(host);
        var roomCode = JsonSerializer.SerializeToElement(created[0].Payload).GetProperty("roomCode").GetString();
        manager.JoinRoom(guest, roomCode);
        await manager.SetReadyAsync(host, true);
        await manager.SetReadyAsync(guest, true);
        var forbidden = await manager.HandleGmActionAsync(host,
            JsonSerializer.SerializeToElement(new { type = "setLife", targetPlayer = 0, value = 30 }));
        var forbiddenPayload = JsonSerializer.SerializeToElement(Assert.Single(forbidden).Payload, WebJson);
        Assert.Equal("actionRejected", forbiddenPayload.GetProperty("type").GetString());
        var forgedThroughGameAction = await manager.HandleActionAsync(host,
            JsonSerializer.SerializeToElement(new { type = "addCard", targetPlayer = 0, cardId = "S01-0001" }));
        var forgedPayload = JsonSerializer.SerializeToElement(Assert.Single(forgedThroughGameAction).Payload, WebJson);
        Assert.Equal("actionRejected", forgedPayload.GetProperty("type").GetString());

        var sandboxHost = Guid.NewGuid();
        manager.Connect(sandboxHost, "沙盒控制者");
        var sandboxMessages = await manager.CreateSandboxAsync(sandboxHost, new L12SandboxRequest());
        var initial = sandboxMessages
            .Where(message => message.SessionId == sandboxHost)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
            .Single(payload => payload.GetProperty("type").GetString() == "gameState");
        Assert.True(initial.GetProperty("gmEnabled").GetBoolean());
        var matchId = initial.GetProperty("state").GetProperty("matchId").GetString()!;

        var allowed = await manager.HandleGmActionAsync(sandboxHost,
            JsonSerializer.SerializeToElement(new { type = "setLife", targetPlayer = 1, value = 23 }));
        var state = allowed
            .Where(message => message.SessionId == sandboxHost)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
            .Single(payload => payload.GetProperty("type").GetString() == "gameState")
            .GetProperty("state");
        Assert.Equal(23, state.GetProperty("players")[1].GetProperty("master").GetProperty("hp").GetInt32());

        var detail = await recorder.GetMatchAsync(matchId);
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Commands.Count);
        Assert.Equal("setLife", detail.Commands[^1].Command.GetProperty("type").GetString());
        Assert.True(detail.Commands[^1].Accepted);
    }

    [Fact]
    public void GmRejectsUnknownCardsAndOccupiedSlotsWithoutPartialMutation()
    {
        var game = new L12GameEngine(Catalog, "gm-validation", "GMVAL", 1202,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var beforeRevision = game.State.Revision;
        Assert.False(game.HandleGm(new L12GmCommand("addCard", CardId: "S99-DOES-NOT-EXIST")).Accepted);
        Assert.Equal(beforeRevision, game.State.Revision);

        var definition = Catalog.Cards.Values.First(card => card.CardType == "legion");
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", CardId: definition.Id, Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var occupant = game.State.Players[0].Field[0][0];
        var rejected = game.HandleGm(new L12GmCommand("placeCard", CardId: definition.Id, Row: 0, Slot: 0,
            TriggerEffects: false));
        Assert.False(rejected.Accepted);
        Assert.Same(occupant, game.State.Players[0].Field[0][0]);
    }

    [Fact]
    public void SandboxDisastersAndEffectlessTacticsDoNotLeaveDirtyState()
    {
        var game = new L12GameEngine(Catalog, "gm-cleanup", "GMCLEAN", 1203,
            ["甲", "乙"], [0, 1], skipPreparation: true, disasterMode: "all");
        game.InitializeGmDisasters();
        Assert.NotEmpty(game.State.DisasterDeck);
        Assert.Equal("S01-DS10", game.State.DisasterDeck[^1].CardId);

        var tactic = Catalog.Cards.Values.First(card => card.CardType == "tactic");
        var result = game.HandleGm(new L12GmCommand("placeCard", 0, tactic.Id, TriggerEffects: false));
        Assert.True(result.Accepted);
        Assert.Empty(game.State.Players[0].Resolving);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.CardId == tactic.Id);
    }
}
