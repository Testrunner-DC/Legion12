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

    [Fact]
    public void GmCanRepeatCardsSetDisplayedTroopsAndStartRulesAttack()
    {
        var game = new L12GameEngine(Catalog, "gm-combat", "GMCOMBAT", 1204,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var legion = Catalog.Cards.Values.First(card => card.CardType == "legion");

        var handBefore = game.State.Players[1].Hand.Count;
        Assert.True(game.HandleGm(new L12GmCommand("addCard", 1, legion.Id,
            Destination: "hand", Value: 3)).Accepted);
        var added = game.State.Players[1].Hand.Skip(handBefore).ToArray();
        Assert.Equal(3, added.Length);
        Assert.Equal(3, added.Select(card => card.InstanceId).Distinct().Count());

        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 1, legion.Id,
            Row: 0, Slot: 0, TriggerEffects: false)).Accepted);
        var attacker = game.State.Players[1].Field[0][0]!;
        Assert.True(game.HandleGm(new L12GmCommand("setTroops", 1,
            CardInstanceId: attacker.InstanceId, Value: 6000)).Accepted);
        Assert.Equal(6000, attacker.Troops);

        var activeBeforeRejectedAttack = game.State.ActivePlayer;
        var phaseBeforeRejectedAttack = game.State.Phase;
        var tappedBeforeRejectedAttack = attacker.Tapped;
        var summonRoundBeforeRejectedAttack = attacker.SummonRound;
        var rejectedAttack = game.HandleGm(new L12GmCommand("startAttack", 1,
            CardInstanceId: attacker.InstanceId, TargetInstanceId: "missing-target"));
        Assert.False(rejectedAttack.Accepted);
        Assert.Equal(activeBeforeRejectedAttack, game.State.ActivePlayer);
        Assert.Equal(phaseBeforeRejectedAttack, game.State.Phase);
        Assert.Equal(tappedBeforeRejectedAttack, attacker.Tapped);
        Assert.Equal(summonRoundBeforeRejectedAttack, attacker.SummonRound);

        var attack = game.HandleGm(new L12GmCommand("startAttack", 1,
            CardInstanceId: attacker.InstanceId));
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal(1, game.State.ActivePlayer);
        Assert.True(attacker.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "attack");
        Assert.Contains(game.State.Events, entry => entry.Type == "gm" && entry.Text.Contains("测试进攻"));
    }

    [Fact]
    public void GmSnapshotRevealsBothHandsAndCommandsMoveActualHandInstances()
    {
        var game = new L12GameEngine(Catalog, "gm-hands", "GMHANDS", 1205,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var legion = Catalog.Cards.Values.First(card => card.CardType == "legion");
        Assert.True(game.HandleGm(new L12GmCommand("addCard", 1, legion.Id, Destination: "hand")).Accepted);
        var handCard = game.State.Players[1].Hand.Last(card => card.CardId == legion.Id);

        var normal = JsonSerializer.SerializeToElement(game.SnapshotFor(0), WebJson);
        Assert.False(normal.GetProperty("players")[1].TryGetProperty("hand", out _));
        var gm = JsonSerializer.SerializeToElement(game.SnapshotForGm(0), WebJson);
        Assert.Contains(gm.GetProperty("players")[1].GetProperty("hand").EnumerateArray(),
            card => card.GetProperty("instanceId").GetString() == handCard.InstanceId);

        Assert.True(game.HandleGm(new L12GmCommand("moveHandCard", 1,
            CardInstanceId: handCard.InstanceId, Destination: "graveyard")).Accepted);
        Assert.DoesNotContain(game.State.Players[1].Hand, card => card.InstanceId == handCard.InstanceId);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == handCard.InstanceId);

        Assert.True(game.HandleGm(new L12GmCommand("addCard", 1, legion.Id, Destination: "hand")).Accepted);
        var played = game.State.Players[1].Hand.Last(card => card.CardId == legion.Id);
        Assert.True(game.HandleGm(new L12GmCommand("playHandCard", 1,
            CardInstanceId: played.InstanceId, Row: 0, Slot: 2, TriggerEffects: false)).Accepted);
        Assert.Same(played, game.State.Players[1].Field[0][2]);
        Assert.DoesNotContain(game.State.Players[1].Hand, card => card.InstanceId == played.InstanceId);
    }

    [Fact]
    public void GmReturnsControlledFieldCardToItsOwnerAndResetsFieldState()
    {
        var game = new L12GameEngine(Catalog, "gm-return-hand", "GMRETURN", 1208,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var legion = Catalog.Cards.Values.First(card => card.CardType == "legion");
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 1, legion.Id,
            Row: 0, Slot: 1, TriggerEffects: false)).Accepted);
        var controlled = game.State.Players[1].Field[0][1]!;
        controlled.OwnerIndex = 0;
        controlled.Tapped = true;
        controlled.Troops = Math.Max(1, controlled.BaseTroops + 2000);

        var returned = game.HandleGm(new L12GmCommand("returnCardToHand", 1,
            CardInstanceId: controlled.InstanceId));

        Assert.True(returned.Accepted, returned.Error);
        Assert.Null(game.State.Players[1].Field[0][1]);
        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == controlled.InstanceId);
        Assert.DoesNotContain(game.State.Players[1].Hand, card => card.InstanceId == controlled.InstanceId);
        Assert.Equal(controlled.BaseTroops, controlled.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "gm" && entry.Text.Contains("所有者手牌"));
    }

    [Fact]
    public void GmNextPhaseExecutesEachEnteredPhaseInsteadOfOnlyChangingItsLabel()
    {
        var game = new L12GameEngine(Catalog, "gm-next-phase", "GMNEXT", 1209,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var legion = Catalog.Cards.Values.First(card => card.CardType == "legion");
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, legion.Id,
            Row: 0, Slot: 0, TriggerEffects: false)).Accepted);
        var unit = game.State.Players[0].Field[0][0]!;
        unit.Tapped = true;
        Assert.True(game.HandleGm(new L12GmCommand("setPhase", 0, Phase: "Disaster")).Accepted);

        Assert.True(game.HandleGm(new L12GmCommand("nextPhase")).Accepted);
        Assert.Equal(L12Phase.Reset, game.State.Phase);
        Assert.False(unit.Tapped);
        Assert.True(game.HandleGm(new L12GmCommand("nextPhase")).Accepted);
        Assert.Equal(L12Phase.Draw, game.State.Phase);
        Assert.True(game.HandleGm(new L12GmCommand("nextPhase")).Accepted);
        Assert.Equal(L12Phase.Morale, game.State.Phase);
        Assert.NotEmpty(game.State.Players[0].Morale);
        Assert.True(game.HandleGm(new L12GmCommand("nextPhase")).Accepted);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(game.State.Events, entry => entry.Type == "gm" && entry.Text.Contains("推进至主要阶段"));
    }

    [Fact]
    public async Task DisconnectedAccountReclaimsItsSeatAndReceivesAuthoritativeRecoveryState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-room-recovery", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var originalHost = Guid.NewGuid();
        var guest = Guid.NewGuid();
        manager.Connect(originalHost, "重连甲");
        manager.Connect(guest, "重连乙");
        var created = manager.CreateRoom(originalHost);
        var roomCode = JsonSerializer.SerializeToElement(created[0].Payload, WebJson).GetProperty("roomCode").GetString();
        manager.JoinRoom(guest, roomCode);
        await manager.SetReadyAsync(originalHost, true);
        await manager.SetReadyAsync(guest, true);
        manager.Disconnect(originalHost);

        var replacementHost = Guid.NewGuid();
        var sessionPayload = JsonSerializer.SerializeToElement(manager.Connect(replacementHost, "重连甲"), WebJson);
        Assert.True(sessionPayload.GetProperty("recovered").GetBoolean());
        Assert.Equal(roomCode, sessionPayload.GetProperty("roomCode").GetString());

        var recovery = manager.RecoveryState(replacementHost);
        var room = recovery.Where(message => message.SessionId == replacementHost)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
            .Single(payload => payload.GetProperty("type").GetString() == "roomState");
        var self = room.GetProperty("players").EnumerateArray()
            .Single(player => player.GetProperty("playerIndex").GetInt32() == 0);
        Assert.True(self.GetProperty("connected").GetBoolean());
        var game = recovery.Where(message => message.SessionId == replacementHost)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
            .Single(payload => payload.GetProperty("type").GetString() == "gameState");
        Assert.True(game.GetProperty("state").GetProperty("revision").GetInt64() >= 0);

        var staleAction = await manager.HandleActionAsync(originalHost,
            JsonSerializer.SerializeToElement(new { type = "passResponse" }));
        var stalePayload = JsonSerializer.SerializeToElement(Assert.Single(staleAction).Payload, WebJson);
        Assert.Equal("error", stalePayload.GetProperty("type").GetString());
    }

    [Fact]
    public void GmSnapshotExposesBothPromptOwnersWithoutLeakingThemToNormalPlayers()
    {
        var game = new L12GameEngine(Catalog, "gm-prompts", "GMPROMPTS", 1207,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        game.State.PendingPrompts.Clear();
        game.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "prompt-0", PlayerIndex = 0, Kind = "optional", Text = "甲选择",
            ValidChoices = ["yes", "no"], MinChoose = 1, MaxChoose = 1,
            IsPrivate = true, Continuation = "test",
        });
        game.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "prompt-1", PlayerIndex = 1, Kind = "optional", Text = "乙选择",
            ValidChoices = ["yes", "no"], MinChoose = 1, MaxChoose = 1,
            IsPrivate = true, Continuation = "test",
        });

        var normal = JsonSerializer.SerializeToElement(game.SnapshotFor(0), WebJson);
        Assert.Equal(["prompt-0"], normal.GetProperty("prompts").EnumerateArray()
            .Select(item => item.GetProperty("promptId").GetString()!).ToArray());
        Assert.Equal(1, normal.GetProperty("waitingPrompt").GetProperty("playerIndex").GetInt32());

        var gm = JsonSerializer.SerializeToElement(game.SnapshotForGm(0), WebJson);
        Assert.Equal(["prompt-0", "prompt-1"], gm.GetProperty("prompts").EnumerateArray()
            .Select(item => item.GetProperty("promptId").GetString()!).ToArray());
        Assert.Equal(JsonValueKind.Null, gm.GetProperty("waitingPrompt").ValueKind);
    }

    [Fact]
    public async Task SandboxControllerCanIssueNormalRulesCommandsForEitherPlayerOnlyInSandbox()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-sandbox-actions", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);

        var normalHost = Guid.NewGuid();
        manager.Connect(normalHost, "正式玩家");
        manager.CreateRoom(normalHost);
        var rejected = await manager.HandleSandboxActionAsync(normalHost, 1,
            JsonSerializer.SerializeToElement(new { type = "surrender" }));
        var rejectedPayload = JsonSerializer.SerializeToElement(Assert.Single(rejected).Payload, WebJson);
        Assert.Equal("actionRejected", rejectedPayload.GetProperty("type").GetString());

        var sandboxHost = Guid.NewGuid();
        manager.Connect(sandboxHost, "沙盒控制者");
        await manager.CreateSandboxAsync(sandboxHost, new L12SandboxRequest());
        var accepted = await manager.HandleSandboxActionAsync(sandboxHost, 1,
            JsonSerializer.SerializeToElement(new { type = "surrender" }));
        var state = accepted
            .Where(message => message.SessionId == sandboxHost)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, WebJson))
            .Single(payload => payload.GetProperty("type").GetString() == "gameState")
            .GetProperty("state");
        Assert.Equal(0, state.GetProperty("winner").GetInt32());
        Assert.Contains("投降", state.GetProperty("winnerReason").GetString());

        var invalidPlayer = await manager.HandleSandboxActionAsync(sandboxHost, 2,
            JsonSerializer.SerializeToElement(new { type = "endTurn" }));
        var invalidPayload = JsonSerializer.SerializeToElement(Assert.Single(invalidPlayer).Payload, WebJson);
        Assert.Equal("actionRejected", invalidPayload.GetProperty("type").GetString());
    }

    [Fact]
    public void CustomSandboxKeepsFourVisibleSlotsAndFinalDisasterLocked()
    {
        var game = new L12GameEngine(Catalog, "gm-custom-disaster", "GMCUSTOM", 1206,
            ["甲", "乙"], [0, 1], skipPreparation: true, disasterMode: "custom");
        game.InitializeGmDisasters();
        Assert.Equal(4, game.State.CustomDisasters.Count);
        Assert.Equal("S01-DS10", game.State.CustomDisasters[3].CardId);
        Assert.Equal(game.State.CustomDisasters.Select(card => card.InstanceId),
            game.State.DisasterDeck.Select(card => card.InstanceId));

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0), WebJson);
        var slots = snapshot.GetProperty("sessionDisasters").EnumerateArray().ToArray();
        Assert.Equal(4, slots.Length);
        Assert.All(slots, slot => Assert.False(slot.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean()));

        var replacement = Catalog.Cards.Values.First(card => card.CardType == "destruction"
            && card.Id != "S01-DS10" && game.State.CustomDisasters.All(current => current.CardId != card.Id));
        var previous = game.State.CustomDisasters[0];
        Assert.True(game.HandleGm(new L12GmCommand("replaceDisaster", CardId: replacement.Id, Slot: 0)).Accepted);
        Assert.Equal(replacement.Id, game.State.CustomDisasters[0].CardId);
        Assert.DoesNotContain(game.State.DisasterDeck, card => card.InstanceId == previous.InstanceId);

        var finalBefore = game.State.CustomDisasters[3];
        var rejected = game.HandleGm(new L12GmCommand("replaceDisaster", CardId: replacement.Id, Slot: 3));
        Assert.False(rejected.Accepted);
        Assert.Same(finalBefore, game.State.CustomDisasters[3]);
    }
}
