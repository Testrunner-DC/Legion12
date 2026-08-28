using TwelveLegions.Server;
using System.Text.Json;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LatestBugRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 6401)
        => new(Catalog, "latest-regression", "LATEST", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}回归牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "latest-regression", "LATEST", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true);
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static L12CardInstance[] SnapshotHand(L12GameEngine game, int playerIndex)
    {
        var view = game.SnapshotFor(playerIndex).Players[playerIndex];
        return Assert.IsType<L12CardInstance[]>(view.GetType().GetProperty("hand")!.GetValue(view));
    }

    private static L12CardInstance[] SnapshotGraveyard(L12GameEngine game, int playerIndex)
    {
        var view = game.SnapshotFor(playerIndex).Players[playerIndex];
        return Assert.IsAssignableFrom<IEnumerable<L12CardInstance>>(
            view.GetType().GetProperty("Graveyard")!.GetValue(view)).ToArray();
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"regression-morale-{player.PlayerIndex}-{index}",
                CardId = "S01-01C1",
                Tapped = false,
            });
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    [Fact]
    public void SiegeCatapultExtendedRangeKeepsEnemyBackLineAndMasterTogether()
    {
        var game = Create(6415);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var catapult = Card("S01-0003", "siege-catapult");
        var backTarget = Card("S01-0002", "enemy-back-line");
        player.Field[1][1] = catapult;
        enemy.Field[1][2] = backTarget;
        AddReadyMorale(player, 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.False(game.SnapshotFor(0).LegalAttackTargets.ContainsKey(catapult.InstanceId));

        var activation = game.Handle(0, new L12Command("activateAbility", catapult.InstanceId,
            Ability: "extendedRange"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);

        var after = game.SnapshotFor(0).LegalAttackTargets[catapult.InstanceId];
        Assert.Contains(backTarget.InstanceId, after);
        Assert.Contains("master", after);
    }

    [Fact]
    public void HuntingMomentCannotBePlayedBeforeItsFourCardGraveyardCostIsLegal()
    {
        var game = Create(6414);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Graveyard.Clear();
        player.Morale.Clear();
        AddReadyMorale(player, 3);
        var huntingMoment = Card("S01-0319", "hunting-moment");
        player.Hand.Add(huntingMoment);
        for (var index = 0; index < 3; index++)
            player.Graveyard.Add(Card("S01-0001", $"hunting-cost-{index}"));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var blockedSnapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var blockedCard = Assert.Single(blockedSnapshot.GetProperty("players")[0]
            .GetProperty("hand").EnumerateArray());
        Assert.Contains("墓地至少有4张", blockedCard.GetProperty("playBlockedReason").GetString());

        var rejected = game.Handle(0, new L12Command("playCard", huntingMoment.InstanceId));
        Assert.False(rejected.Accepted);
        Assert.Contains("墓地至少有4张", rejected.Error);
        Assert.Contains(huntingMoment, player.Hand);
        Assert.Equal(3, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(player.Resolving);

        player.Graveyard.Add(Card("S01-0002", "hunting-cost-3"));
        var legalSnapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var legalCard = Assert.Single(legalSnapshot.GetProperty("players")[0]
            .GetProperty("hand").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, legalCard.GetProperty("playBlockedReason").ValueKind);

        Assert.True(game.Handle(0, new L12Command("playCard", huntingMoment.InstanceId)).Accepted);
        PassResponses(game);
        var costOrder = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("hunt-return", costOrder.Data["action"]);
        Assert.Equal(4, costOrder.MinChoose);
        Assert.Equal(4, costOrder.MaxChoose);
        Assert.DoesNotContain(huntingMoment, player.Hand);
        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));
    }

    [Fact]
    public void InfiltratorCanEnterEitherBattlefieldButCannotReplaceOpponentCounter()
    {
        var game = Create(6415);
        var owner = game.State.Players[0];
        var controller = game.State.Players[1];
        var infiltrator = Card("S01-0004", "cross-field-infiltrator");
        owner.Hand.Clear();
        owner.Hand.Add(infiltrator);
        AddReadyMorale(owner, 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", infiltrator.InstanceId,
            Row: 0, Slot: 1, TargetPlayerIndex: 1)).Accepted);
        PassResponses(game);

        Assert.Same(infiltrator, controller.Field[0][1]);
        Assert.Equal(0, infiltrator.OwnerIndex);
        Assert.True(infiltrator.Tapped);

        var blockedGame = Create(6416);
        var blockedOwner = blockedGame.State.Players[0];
        var counter = Card("S01-0016", "opponent-counter");
        counter.Hidden = true;
        blockedGame.State.Players[1].Field[1][0] = counter;
        var blockedInfiltrator = Card("S01-0004", "blocked-infiltrator");
        blockedOwner.Hand.Clear();
        blockedOwner.Hand.Add(blockedInfiltrator);
        AddReadyMorale(blockedOwner, 2);
        blockedGame.State.ActivePlayer = 0;
        blockedGame.State.Phase = L12Phase.Main;

        var result = blockedGame.Handle(0, new L12Command("playCard", blockedInfiltrator.InstanceId,
            Row: 1, Slot: 0, TargetPlayerIndex: 1));
        Assert.False(result.Accepted);
        Assert.Same(counter, blockedGame.State.Players[1].Field[1][0]);
    }

    [Fact]
    public void InfiltratorDeathAndReturnUseOwnerZonesInsteadOfBattlefieldControllerZones()
    {
        var deathGame = Create(6417);
        var owner = deathGame.State.Players[0];
        var controller = deathGame.State.Players[1];
        var infiltrator = Card("S01-0004", "owned-infiltrator");
        infiltrator.OwnerIndex = 0;
        controller.Field[0][0] = infiltrator;
        AddReadyMorale(controller, 2);
        deathGame.State.ActivePlayer = 1;
        deathGame.State.Phase = L12Phase.Main;
        var ownerHandBefore = owner.Hand.Count;
        var controllerHandBefore = controller.Hand.Count;

        Assert.True(deathGame.Handle(1, new L12Command("activateAbility", infiltrator.InstanceId,
            Ability: "destroyInfiltrator")).Accepted);
        PassResponses(deathGame);

        Assert.Contains(infiltrator, owner.Graveyard);
        Assert.DoesNotContain(infiltrator, controller.Graveyard);
        Assert.Equal(ownerHandBefore + 1, owner.Hand.Count);
        Assert.Equal(controllerHandBefore, controller.Hand.Count);

        var returnGame = Create(6418);
        var returnOwner = returnGame.State.Players[0];
        var returnController = returnGame.State.Players[1];
        var returnedInfiltrator = Card("S01-0004", "returned-infiltrator");
        returnedInfiltrator.OwnerIndex = 0;
        returnController.Field[0][0] = returnedInfiltrator;
        var transfer = Card("S01-0009", "strategic-transfer");
        returnController.Hand.Clear();
        returnController.Hand.Add(transfer);
        AddReadyMorale(returnController, 1);
        returnGame.State.ActivePlayer = 1;
        returnGame.State.Phase = L12Phase.Main;

        Assert.True(returnGame.Handle(1, new L12Command("playCard", transfer.InstanceId)).Accepted);
        PassResponses(returnGame);
        var target = Assert.Single(returnGame.State.PendingPrompts, prompt => prompt.Continuation == "card-effect");
        Assert.True(returnGame.Handle(1, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: returnedInfiltrator.InstanceId)).Accepted);

        Assert.Contains(returnedInfiltrator, returnOwner.Hand);
        Assert.DoesNotContain(returnedInfiltrator, returnController.Hand);
    }

    [Fact]
    public void InfiltratorOwnerCanDestroyItWhileItIsControlledOnOpponentBattlefield()
    {
        var game = Create(6420);
        var owner = game.State.Players[0];
        var controller = game.State.Players[1];
        var infiltrator = Card("S01-0004", "owner-activated-infiltrator");
        infiltrator.OwnerIndex = 0;
        controller.Field[0][2] = infiltrator;
        AddReadyMorale(owner, 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", infiltrator.InstanceId,
            Ability: "destroyInfiltrator")).Accepted);
        PassResponses(game);

        Assert.Contains(infiltrator, owner.Graveyard);
        Assert.Null(controller.Field[0][2]);
    }

    [Fact]
    public void ImhotepEnterEffectOffersEveryLegalSixCostSunCityLegionInGraveyard()
    {
        var game = Create(6414);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var imhotep = Card("S02-0204", "imhotep-enter-source");
        var paladin = Card("S02-0202", "imhotep-grave-paladin");
        player.Hand.Clear();
        player.Hand.Add(imhotep);
        player.Graveyard.Add(paladin);
        while (opponent.Hand.Count < 2)
            opponent.Hand.Add(Card("S01-0001", $"imhotep-opponent-hand-{opponent.Hand.Count}"));
        AddReadyMorale(player, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", imhotep.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("optional-card", prompt.Kind);
        Assert.Contains(paladin.InstanceId, prompt.ValidChoices);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [paladin.InstanceId])).Accepted);
        Assert.Contains(paladin, player.Hand);
        Assert.DoesNotContain(paladin, player.Graveyard);
    }

    [Fact]
    public void StrongAttackGrantedDuringAttackUpdatesTheCurrentMasterDamageSnapshot()
    {
        var game = Create(6497);
        var player = game.State.Players[0];
        var olaf = Card("S01-0306", "olaf-current-attack");
        var graveCard = Card("S01-0001", "olaf-grave-cost");
        olaf.SummonRound = 0;
        player.Field[0][0] = olaf;
        player.Graveyard.Clear();
        player.Graveyard.Add(graveCard);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", olaf.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var effect = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "olaf-strong");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: effect.PromptId,
            CardInstanceIds: [graveCard.InstanceId])).Accepted);

        Assert.True(olaf.HasStrongAttack);
        Assert.Equal(2, game.State.PendingDefense?.MasterDamage);
    }

    [Fact]
    public void TimedSureHitAgainstLegionsPreventsBothBlockingAndSupporting()
    {
        var game = new L12GameEngine(Catalog, "sure-hit-regression", "SUREHIT", 6498,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false);
        var attacker = Card("S01-0003", "fearless-attacker");
        var defender = Card("S01-0003", "fearless-target");
        var blocker = Card("S01-0003", "fearless-blocker");
        var mercenary = Card("S01-0002", "fearless-mercenary");
        attacker.SummonRound = 0;
        attacker.SureHitAgainstLegionsUntilTurn = 2;
        defender.SummonRound = 0;
        blocker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;
        game.State.Players[1].Field[1][0] = blocker;
        game.State.Players[1].Hand.Add(mercenary);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId))).Accepted);
        Assert.True(game.State.PendingDefense?.SureHit);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(mercenary.InstanceId, response.ValidChoices);

        PassResponses(game);
        Assert.Null(game.State.PendingDefense);
        Assert.False(blocker.Tapped);
    }

    [Fact]
    public void CompletedGrailTrialOffersOneRuneWhenRoundTableLegionEnters()
    {
        var game = Create(6499);
        var player = game.State.Players[0];
        var grail = Card("S02-06S4", "grail-entry-trigger");
        var bors = Card("S02-0605", "grail-round-table-entry");
        grail.TrialCompleted = true;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(grail);
        player.Hand.Clear();
        player.Hand.Add(bors);
        AddReadyMorale(player, bors.Cost);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", bors.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-grail-round-table-rune", prompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Contains($"trigger:grail-round-table:{game.State.TurnSerial}", player.UsedAbilities);
    }

    [Fact]
    public void DisasterWaitsForRoundTableEntryTriggerToFullyClose()
    {
        var game = Create(6509);
        var player = game.State.Players[0];
        var grail = Card("S02-06S4", "grail-before-disaster");
        var bors = Card("S02-0605", "bors-crossing-disaster");
        grail.TrialCompleted = true;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(grail);
        player.Hand.Clear();
        player.Hand.Add(bors);
        AddReadyMorale(player, bors.Cost);
        game.State.DisasterValue = 7;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS08", "scheduled-disaster"));
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", bors.InstanceId, Row: 0, Slot: 0)).Accepted);

        Assert.Null(game.State.ActiveDisaster);
        Assert.True(game.State.CheckDisasterAfterStack);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-grail-round-table-rune", prompt.Data["action"]);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")).Accepted);
        PassResponses(game);

        Assert.Equal("S01-DS08", game.State.ActiveDisaster?.CardId);
        Assert.False(game.State.CheckDisasterAfterStack);
    }

    [Fact]
    public void AngusCanAdvanceTrialAfterAReactionTacticResolves()
    {
        var game = CreateWithFirstMaster("S02-06M2", 6500);
        var angusPlayer = game.State.Players[0];
        var attackerPlayer = game.State.Players[1];
        var trial = Card("S02-06S4", "angus-trial");
        var counter = Card("S01-0019", "angus-reaction-tactic");
        var buffTarget = Card("S01-0003", "angus-reaction-target");
        var attacker = Card("S01-0003", "angus-reaction-attacker");
        angusPlayer.SpecialZones.Trials.Clear();
        angusPlayer.SpecialZones.Trials.Add(trial);
        counter.Hidden = true;
        counter.SetRound = 0;
        angusPlayer.Field[1][0] = counter;
        angusPlayer.Field[0][0] = buffTarget;
        buffTarget.SummonRound = 0;
        attackerPlayer.Field[0][0] = attacker;
        attacker.SummonRound = 0;
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);
        PassResponses(game);
        var ambushTarget = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(buffTarget.InstanceId, ambushTarget.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ambushTarget.PromptId,
            Choice: buffTarget.InstanceId)).Accepted);
        PassResponses(game);
        var angusPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-angus-trial", angusPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: angusPrompt.PromptId,
            Choice: "yes")).Accepted);
        Assert.Equal(1, trial.TrialProgress);
    }

    [Fact]
    public void ForgedOrdersTargetsTheOpponentDestinationInsteadOfTheControllersMatchingSlot()
    {
        var game = Create(6421);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var orders = Card("S01-0010", "forged-orders");
        var target = Card("S01-0103", "forged-orders-target");
        player.Hand.Clear();
        player.Hand.Add(orders);
        enemy.Field[1][0] = target;
        AddReadyMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", orders.InstanceId)).Accepted);
        PassResponses(game);
        var pick = Assert.Single(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "orders-pick");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: pick.PromptId,
            CardInstanceIds: [target.InstanceId])).Accepted);

        var destination = Assert.Single(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "orders-row");
        Assert.Equal("1", destination.Data["targetPlayerIndex"]);
        Assert.Equal(["0:0"], destination.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: destination.PromptId, Choice: "0:0")).Accepted);

        Assert.Same(target, enemy.Field[0][0]);
        Assert.Null(enemy.Field[1][0]);
        Assert.Null(player.Field[0][0]);
    }

    [Fact]
    public void WukongReturningAllMoraleQueuesTiantingZeroMoraleTrigger()
    {
        var game = CreateWithFirstMaster("S02-01M1", 6419);
        var player = game.State.Players[0];
        AddReadyMorale(player, 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        var returned = player.Morale.Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "wukongTransform")).Accepted);
        var selection = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: selection.PromptId,
            CardInstanceIds: [.. returned])).Accepted);
        PassResponses(game);

        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Continuation == "faction-zero-recovery");
    }

    [Fact]
    public async Task PlayerCanLeaveTheRoomAfterSurrenderEndsTheGame()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-room-leave", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(Catalog, recorder);
        var host = Guid.NewGuid();
        var guest = Guid.NewGuid();
        manager.Connect(host, "甲");
        manager.Connect(guest, "乙");
        var created = manager.CreateRoom(host);
        var roomCode = JsonSerializer.SerializeToElement(created[0].Payload).GetProperty("roomCode").GetString();
        manager.JoinRoom(guest, roomCode);
        await manager.SetReadyAsync(host, true);
        await manager.SetReadyAsync(guest, true);

        var surrender = JsonSerializer.SerializeToElement(new { type = "surrender" });
        var surrenderMessages = await manager.HandleActionAsync(host, surrender);
        Assert.Contains(surrenderMessages, message => JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString() == "gameState");

        var leaveMessages = manager.LeaveRoom(host);
        var payload = JsonSerializer.SerializeToElement(leaveMessages.Single(message => message.SessionId == host).Payload);
        Assert.Equal("roomLeft", payload.GetProperty("type").GetString());
    }

    [Fact]
    public void RagashaOpponentTurnBonusDoesNotStackWhenMercenaryBlocksTheAttack()
    {
        var game = Create();
        var attacker = Card("S01-0103", "ragasha-attacker");
        var ragasha = Card("S01-0312", "ragasha-defender");
        var mercenary = Card("S01-0002", "ragasha-mercenary");
        attacker.SummonRound = ragasha.SummonRound = 0;
        game.State.Players[0].Hand.Clear();
        game.State.Players[1].Hand.Clear();
        game.State.Players[1].Hand.Add(mercenary);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = ragasha;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        game.SnapshotFor(0);
        var expectedTroops = ragasha.BaseTroops + 1000;
        Assert.Equal(expectedTroops, ragasha.Troops);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", ragasha.InstanceId))).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(mercenary.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: mercenary.InstanceId)).Accepted);

        Assert.Same(ragasha, game.State.Players[1].Field[0][0]);
        Assert.Equal(expectedTroops, ragasha.Troops);
        Assert.Contains(mercenary, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void BloodAxeErikAdvertisesItsFourMoraleSelfDamagePlayCost()
    {
        var game = Create(6402);
        var player = game.State.Players[0];
        var erik = Card("S01-0308", "discounted-erik");
        player.Hand.Clear();
        player.Hand.Add(erik);
        AddReadyMorale(player, 4);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var snapshot = Assert.Single(SnapshotHand(game, 0));
        Assert.Equal(4, snapshot.PlayCost);
        Assert.True(game.Handle(0, new L12Command("playCard", erik.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal("play-cost-choice", Assert.Single(game.State.PendingPrompts).Continuation);
    }

    [Fact]
    public void CanopicJarFreeTacticOpportunityAdvertisesZeroUntilConsumed()
    {
        var game = Create(6403);
        var player = game.State.Players[0];
        var tactic = Card("S01-0015", "free-tactic");
        player.Hand.Clear();
        player.Hand.Add(tactic);
        player.FreeTacticCount = 1;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.Equal(0, Assert.Single(SnapshotHand(game, 0)).PlayCost);
        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Assert.Equal(0, player.FreeTacticCount);
    }

    [Fact]
    public void DimMorningStarFreeActiveTacticDoesNotDiscountCounterAndIsConsumedByActiveTactic()
    {
        var game = Create(64031);
        var player = game.State.Players[0];
        var counter = Card("S01-0016", "morning-star-counter");
        var active = Card("S01-0012", "morning-star-active");
        player.Hand.Clear();
        player.Hand.AddRange([counter, active]);
        AddReadyMorale(player, 2);
        player.UsedAbilities.Add("ds01-free-tactic");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var initialHand = SnapshotHand(game, 0);
        Assert.Equal(2, Assert.Single(initialHand, card => card.InstanceId == counter.InstanceId).PlayCost);
        Assert.Equal(0, Assert.Single(initialHand, card => card.InstanceId == active.InstanceId).PlayCost);

        Assert.True(game.Handle(0, new L12Command("playCard", counter.InstanceId, Row: 1, Slot: 0)).Accepted);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Contains("ds01-free-tactic", player.UsedAbilities);

        Assert.True(game.Handle(0, new L12Command("playCard", active.InstanceId)).Accepted);
        Assert.DoesNotContain("ds01-free-tactic", player.UsedAbilities);
    }

    [Fact]
    public void DefeatedLegionLeavesImmediatelyAndResetsAllTransientFieldState()
    {
        var game = Create(6404);
        var attacker = Card("S01-0001", "reset-attacker");
        var target = Card("S01-0102", "reset-target");
        attacker.SummonRound = target.SummonRound = 0;
        attacker.Troops = 6000;
        target.Troops = 1000;
        target.Tapped = true;
        target.HasCharge = true;
        target.HasStrongAttack = true;
        target.HasSureHit = true;
        target.CostModifier = -2;
        target.TimedModifiers.Add(new L12TimedModifier { TroopsDelta = -1000, ExpiresAfterTurn = 99, Source = "test" });
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Contains(target, game.State.Players[1].Graveyard);
        Assert.Equal(target.BaseTroops, target.Troops);
        Assert.False(target.Tapped);
        Assert.False(target.HasCharge);
        Assert.False(target.HasStrongAttack);
        Assert.False(target.HasSureHit);
        Assert.Equal(0, target.CostModifier);
        Assert.Empty(target.TimedModifiers);
        Assert.Contains(game.State.Events, entry => entry.Type == "support-skipped");
    }

    [Fact]
    public void PromotionLegionCanEnterNormallyWithoutAFoundation()
    {
        var game = Create(6405);
        var player = game.State.Players[0];
        var promoted = Card("S02-0501", "ordinary-promotion-legion");
        player.Hand.Clear();
        player.Hand.Add(promoted);
        AddReadyMorale(player, promoted.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("playCard", promoted.InstanceId, Row: 0, Slot: 0));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(promoted, player.Field[0][0]);
        Assert.DoesNotContain(promoted, player.Hand);
        Assert.Equal(promoted.Cost, player.Morale.Count(card => card.Tapped));
    }

    [Fact]
    public void PromotionLegionKeepsNormalEntryModeWhenAllAvailableResourcesAreForced()
    {
        var game = Create(6406);
        var player = game.State.Players[0];
        var foundation = Card("S02-0502", "promotion-choice-foundation");
        var promoted = Card("S02-0501", "promotion-choice-card");
        player.Field[0][1] = foundation;
        player.Hand.Clear();
        player.Hand.Add(promoted);
        for (var index = 0; index < promoted.Cost; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"promotion-choice-resource-{index}",
                CardId = "S02-05C1A",
                Tapped = false,
                IsGodPower = index < 2,
            });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", promoted.InstanceId, Row: 0, Slot: 0)).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-promotion-mode", mode.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "normal")).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "resource-payment");

        Assert.Same(promoted, player.Field[0][0]);
        Assert.Same(foundation, player.Field[0][1]);
        Assert.DoesNotContain(promoted, player.Hand);
        Assert.All(player.Morale, card => Assert.True(card.Tapped));
    }

    [Fact]
    public void OlympusFactionShowsMoraleEffectFirstAndDisablesGodPowerEffectWithoutActiveGodPower()
    {
        var game = CreateWithFirstMaster("S02-05M2", 6407);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "ordinary-olympus-morale",
            CardId = "S02-05C1A",
            Tapped = false,
            IsGodPower = false,
        });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(game.SnapshotFor(0)));
        var effect = document.RootElement.GetProperty("Players")[0].GetProperty("factionEffect");
        Assert.Equal("S02-05C1A", effect.GetProperty("cardId").GetString());
        var abilities = effect.GetProperty("abilities");
        Assert.Equal("olympusMoraleFlip", abilities[0].GetProperty("Id").GetString());
        Assert.True(abilities[0].GetProperty("Enabled").GetBoolean());
        Assert.Equal("godPowerDraw", abilities[1].GetProperty("Id").GetString());
        Assert.False(abilities[1].GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void IsisCanopicCostDiscardsThreeTombGuardsIntoGraveyardWithoutDefeatingThem()
    {
        var game = CreateWithFirstMaster("S01-02M1", 6408);
        var player = game.State.Players[0];
        player.Graveyard.Clear();
        player.Hand.Clear();
        for (var slot = 0; slot < 3; slot++)
            player.Field[0][slot] = Card("S01-0212", $"isis-cost-guard-{slot}");
        player.Graveyard.Add(Card("S01-0216", "isis-canopic-source"));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("activateAbility", "S01-02M1", Ability: "isisCanopic"));

        Assert.True(result.Accepted, result.Error);
        Assert.All(player.Field[0], card => Assert.Null(card));
        Assert.Equal(3, player.Graveyard.Count(card => card.CardId == "S01-0212"));
        Assert.Equal(3, SnapshotGraveyard(game, 0).Count(card => card.CardId == "S01-0212"));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "death" && entry.Cards.Any(card => card.CardId == "S01-0212"));
    }

    [Fact]
    public void ResourcePaymentPromptOnlyNamesResourcesThatCanActuallyBeSelected()
    {
        var game = CreateWithFirstMaster("S02-05M2", 6409);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary", CardId = "S02-05C1A", Tapped = false });
        player.Morale.Add(new L12MoraleCard { InstanceId = "god-power", CardId = "S02-05C1A", Tapped = false, IsGodPower = true });
        var forge = Card("S02-0520", "dynamic-resource-forge");
        player.Relic = forge;
        player.Field[0][0] = Card("S01-0212", "controlled-tomb-guard");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", forge.InstanceId,
            Ability: "forgePromotionDiscount")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("请选择支付费用的士气、神力", prompt.Text);
        Assert.DoesNotContain("陵墓守卫", prompt.Text);
    }

    [Fact]
    public void TrialLegionCanActAgainWhenAnotherEffectReadiesItInTheSameTurn()
    {
        var game = Create(6410);
        var player = game.State.Players[0];
        var legion = Card("S02-0604", "trial-legion");
        var trial = Card("S02-06S4", "active-trial");
        legion.SummonRound = 0;
        player.Field[0][0] = legion;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var first = game.Handle(0, new L12Command("activateAbility", legion.InstanceId, Ability: "trialAdvance"));

        Assert.True(first.Accepted, first.Error);
        Assert.True(legion.Tapped);
        Assert.Equal(legion.TrialValue, trial.TrialProgress);
        legion.Tapped = false;
        var second = game.Handle(0, new L12Command("activateAbility", legion.InstanceId, Ability: "trialAdvance"));
        Assert.True(second.Accepted, second.Error);
        Assert.Equal(legion.TrialValue * 2, trial.TrialProgress);
    }

    [Fact]
    public void ExplicitCardTextCanStillLockFinnFromAnotherTrialThisTurn()
    {
        var game = Create(64101);
        var player = game.State.Players[0];
        var finn = Card("S02-0610", "finn-trial-lock");
        var trial = Card("S02-06S4", "finn-active-trial");
        finn.SummonRound = 0;
        finn.Tapped = false;
        player.Field[0][0] = finn;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        player.UsedAbilities.Add($"trial-card-lock:{finn.InstanceId}:0");
        game.State.ActivePlayer = 0;
        game.State.TurnSerial = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("activateAbility", finn.InstanceId, Ability: "trialAdvance"));

        Assert.False(result.Accepted);
        Assert.Contains("卡牌效果", result.Error);
        Assert.False(finn.Tapped);
        Assert.Equal(0, trial.TrialProgress);
    }

    [Fact]
    public void ZeroMoraleMoveAutomaticallyUsesTheOnlyAvailableTombGuard()
    {
        var game = CreateWithFirstMaster("S01-02M1", 64102);
        var player = game.State.Players[0];
        var mover = Card("S02-0003", "zero-morale-mover");
        var guard = Card("S01-0212", "zero-morale-tomb-guard");
        mover.SummonRound = -1;
        guard.SummonRound = -1;
        player.Morale.Clear();
        player.Field[0][0] = mover;
        player.Field[0][2] = guard;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var begin = game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 0));

        Assert.True(begin.Accepted, begin.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Same(mover, player.Field[1][0]);
        Assert.Null(player.Field[0][0]);
        Assert.True(guard.Tapped);
    }

    [Fact]
    public void TrialCardCompletionAndCompletedTrialAbilityAreSeparateActions()
    {
        var completionGame = Create(64103);
        var completionPlayer = completionGame.State.Players[0];
        var completableTrial = Card("S02-06S4", "completable-trial-card");
        completableTrial.TrialProgress = 8;
        completionPlayer.SpecialZones.Trials.Clear();
        completionPlayer.SpecialZones.Trials.Add(completableTrial);
        completionGame.State.ActivePlayer = 0;
        completionGame.State.Phase = L12Phase.Main;

        var completion = completionGame.Handle(0, new L12Command("activateAbility",
            completableTrial.InstanceId, Ability: "completeTrial"));

        Assert.True(completion.Accepted, completion.Error);
        PassResponses(completionGame);
        Assert.True(completableTrial.TrialCompleted);
        var trialAnimation = Assert.Single(completionGame.State.Events, entry => entry.Type == "effect-trigger");
        Assert.Equal("触发 可查看我方牌库，选择1张【彼界】军团展示并加入手牌。随后重洗牌库。", trialAnimation.Text);
        Assert.Contains(trialAnimation.Cards, card => card.InstanceId == completableTrial.InstanceId);

        var completedAbilityGame = Create(64104);
        var completedPlayer = completedAbilityGame.State.Players[0];
        var completedTrial = Card("S02-06S5", "completed-trial-card");
        var restedLegion = Card("S02-0610", "completed-trial-target");
        completedTrial.TrialCompleted = true;
        restedLegion.Tapped = true;
        completedPlayer.SpecialZones.Trials.Clear();
        completedPlayer.SpecialZones.Trials.Add(completedTrial);
        completedPlayer.SpecialZones.Runes = 1;
        completedPlayer.Field[0][0] = restedLegion;
        completedAbilityGame.State.ActivePlayer = 0;
        completedAbilityGame.State.Phase = L12Phase.Main;

        var activated = completedAbilityGame.Handle(0, new L12Command("activateAbility",
            completedTrial.InstanceId, Ability: "fenianReady"));

        Assert.True(activated.Accepted, activated.Error);
        var target = Assert.Single(completedAbilityGame.State.PendingPrompts);
        Assert.Contains(restedLegion.InstanceId, target.ValidChoices);
    }

    [Fact]
    public void FenianLegendCreatesThreeIndependentRepeatableDebuffEffects()
    {
        var game = Create(64105);
        var player = game.State.Players[0];
        var trial = Card("S02-06S5", "fenian-independent-trial");
        var enemy = Card("S02-0302", "fenian-repeat-target");
        var counter = Card("S01-0019", "fenian-independent-counter");
        trial.TrialProgress = 8;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        player.SpecialZones.Runes = 3;
        game.State.Players[1].Field[0][0] = enemy;
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[1].Field[1][0] = counter;
        game.State.Round = 2;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "completeTrial")).Accepted);
        PassResponses(game);
        for (var index = 0; index < 3; index++)
        {
            var target = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("s2-fenian-trial-debuff", target.Data["action"]);
            Assert.Contains(enemy.InstanceId, target.ValidChoices);
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
                Choice: enemy.InstanceId)).Accepted);
        }

        Assert.True(game.State.EffectStack.Count == 1,
            $"stack={game.State.EffectStack.Count}; deferred={game.State.DeferredEffectStack.Count}; prompts={string.Join(',', game.State.PendingPrompts.Select(prompt => prompt.Kind + ':' + prompt.Continuation))}; events={string.Join(" / ", game.State.Events.TakeLast(5).Select(entry => entry.Text))}");
        var first = game.State.EffectStack[0];
        Assert.Equal("fenianSingleDebuff", first.Data["ability"]);
        first.Negated = true;
        for (var index = 0; index < 2; index++)
        {
            var response = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", response.Kind);
            Assert.True(game.Handle(response.PlayerIndex, new L12Command("resolvePrompt",
                PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var second = Assert.Single(game.State.EffectStack);
        Assert.NotEqual(first.StackItemId, second.StackItemId);
        Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(enemy.BaseTroops - 6000, enemy.Troops);
    }

    [Fact]
    public void WorldUpheavalRevealsLibraryTopAndBlocksMatchingProfessionLegion()
    {
        var game = Create(6411);
        var player = game.State.Players[0];
        var hand = Card("S02-0604", "same-profession-hand");
        var top = Card("S02-0610", "visible-library-top");
        Assert.Equal(hand.Profession, top.Profession);
        player.Hand.Clear();
        player.Library.Clear();
        player.Hand.Add(hand);
        player.Library.Add(top);
        AddReadyMorale(player, 10);
        game.State.ActiveDisaster = Card("S02-DS01", "world-upheaval");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(game.SnapshotFor(1)));
        Assert.Equal(top.CardId, document.RootElement.GetProperty("Players")[0]
            .GetProperty("libraryTop").GetProperty("CardId").GetString());
        var result = game.Handle(0, new L12Command("playCard", hand.InstanceId, Row: 0, Slot: 0));
        Assert.False(result.Accepted);
        Assert.Contains("天地异变", result.Error);
    }

    [Fact]
    public void BaseAchillesLethalReplacementPreservesTroopsAndReadyState()
    {
        var game = Create(6420);
        var defender = game.State.Players[0];
        var attackerPlayer = game.State.Players[1];
        var achilles = Card("S02-0504", "base-achilles-replacement");
        var attacker = Card("S02-0502", "lethal-attacker");
        attacker.Troops = 5000;
        achilles.Troops = 4000;
        achilles.Tapped = false;
        achilles.SummonRound = 0;
        attacker.SummonRound = 0;
        defender.Field[0][0] = achilles;
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[1] = new L12CardInstance?[3];
        defender.Morale.Clear();
        defender.Morale.Add(new L12MoraleCard
        {
            InstanceId = "achilles-god-power",
            CardId = "S02-05C1",
            IsGodPower = true,
            Tapped = false,
        });
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", achilles.InstanceId))).Accepted);
        PassResponses(game);
        var replacement = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "combat-lethal-replacement");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: replacement.PromptId,
            Choice: "yes")).Accepted);

        Assert.Same(achilles, defender.Field[0][0]);
        Assert.Equal(4000, achilles.Troops);
        Assert.False(achilles.Tapped);
        Assert.True(defender.Morale[0].Tapped);
        Assert.False(defender.Morale[0].IsGodPower);
        Assert.Equal(1000, attacker.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "replacement"
            && entry.Text.Contains("保持当时状态"));
    }

    [Theory]
    [InlineData("S02-0606")]
    [InlineData("S02-0611")]
    public void NativePiercingStartsMasterAttackWithRemainingTroopsAndNoAttackTrigger(string cardId)
    {
        var game = Create(6421);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var attacker = Card(cardId, $"piercing-{cardId}");
        var target = Card("S01-0102", $"piercing-target-{cardId}");
        attacker.Troops = 5000;
        target.Troops = 1000;
        attacker.SummonRound = target.SummonRound = 0;
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = target;
        defender.Field[1] = new L12CardInstance?[3];
        defender.Hand.Clear();
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        for (var step = 0; step < 12 && (!defender.Resolving.Contains(target)
                 || game.State.PendingDefense?.Target.Type != "master"); step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Contains(target, defender.Resolving);
        Assert.DoesNotContain(target, defender.Graveyard);
        Assert.NotNull(game.State.PendingDefense);
        Assert.Equal("master", game.State.PendingDefense!.Target.Type);
        Assert.True(game.State.PendingDefense.SuppressAttackTriggers);
        Assert.Equal(4000, game.State.PendingDefense.AttackValue);
        Assert.Equal(4000, attacker.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "piercing"
            && entry.Text.Contains("剩余兵力4000") && entry.Text.Contains("不触发【进攻时】效果"));
    }

    [Fact]
    public void PiercingUsesTheSameMasterTargetRestrictionsAsAnOrdinaryAttack()
    {
        var game = Create(6425);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var attacker = Card("S02-0606", "piercing-shared-validation");
        var killedTaunt = Card("S01-0107", "piercing-killed-taunt");
        var remainingTaunt = Card("S02-0004", "piercing-remaining-taunt");
        attacker.Troops = 5000;
        killedTaunt.Troops = 1000;
        attacker.SummonRound = killedTaunt.SummonRound = remainingTaunt.SummonRound = 0;
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = killedTaunt;
        defender.Field[0][1] = remainingTaunt;
        defender.Field[1] = new L12CardInstance?[3];
        defender.Hand.Clear();
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", killedTaunt.InstanceId))).Accepted);
        for (var step = 0; step < 12 && !defender.Graveyard.Contains(killedTaunt); step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Contains(killedTaunt, defender.Graveyard);
        Assert.Null(game.State.PendingDefense);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("贯穿进攻失败") && entry.Text.Contains("挑衅"));
    }

    [Fact]
    public void PiercingRetargetedByMagiciansPuppetUsesTheRealRemainingTroopsAndBothLegionsDie()
    {
        var game = Create(64251);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var attacker = Card("S02-0606", "piercing-puppet-attacker");
        var firstTarget = Card("S01-0103", "piercing-puppet-first-target");
        var puppet = Card("S02-0005", "piercing-puppet-response");
        attacker.Troops = 6000;
        firstTarget.Troops = 5000;
        attacker.SummonRound = firstTarget.SummonRound = 0;
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = firstTarget;
        defender.Field[1] = new L12CardInstance?[3];
        defender.Hand.Clear();
        defender.Hand.Add(puppet);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", firstTarget.InstanceId))).Accepted);

        L12Prompt? puppetWindow = null;
        for (var step = 0; step < 40 && puppetWindow is null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                if (game.State.PendingDefense?.Target.Type == "master"
                    && prompt.Kind == "response" && prompt.ValidChoices.Contains(puppet.InstanceId))
                {
                    puppetWindow = prompt;
                    break;
                }
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices[0];
                Assert.True(game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
                continue;
            }
            if (game.State.Phase == L12Phase.Defense && game.State.PendingDefense is not null)
            {
                var defendingPlayer = 1 - game.State.PendingDefense.AttackerPlayer;
                Assert.True(game.Handle(defendingPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
            }
        }

        Assert.NotNull(puppetWindow);
        Assert.Contains(firstTarget, defender.Resolving);
        Assert.DoesNotContain(firstTarget, defender.Graveyard);
        Assert.Equal(1000, attacker.Troops);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: puppetWindow!.PromptId,
            Choice: puppet.InstanceId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:1")).Accepted);

        for (var step = 0; step < 20 && (!attackerPlayer.Graveyard.Contains(attacker)
                 || !defender.Graveyard.Contains(puppet)); step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices[0];
                Assert.True(game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
                continue;
            }
            if (game.State.Phase == L12Phase.Defense && game.State.PendingDefense is not null)
            {
                var defendingPlayer = 1 - game.State.PendingDefense.AttackerPlayer;
                Assert.True(game.Handle(defendingPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
            }
        }

        Assert.Contains(attacker, attackerPlayer.Graveyard);
        Assert.Contains(firstTarget, defender.Graveyard);
        Assert.Contains(puppet, defender.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "piercing"
            && entry.Text.Contains("剩余兵力1000"));
    }

    [Fact]
    public void AbsoluteDefenseNegatesTrapWithoutSwallowingTakedaEnterEffect()
    {
        var game = Create(6422);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var takeda = Card("S02-0401", "nested-stack-takeda");
        var absoluteDefense = Card("S01-0016", "nested-stack-absolute-defense");
        var trap = Card("S01-0018", "nested-stack-trap");
        var discard = Card("S01-0003", "nested-stack-discard");
        owner.Hand.Clear();
        owner.Hand.Add(takeda);
        owner.Hand.Add(discard);
        AddReadyMorale(owner, 8);
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = 0;
        owner.Field[1][0] = absoluteDefense;
        trap.Hidden = true;
        trap.SetRound = 0;
        opponent.Field[1][0] = trap;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", takeda.InstanceId, Row: 0, Slot: 0)).Accepted);
        var trapWindow = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(trap.InstanceId, trapWindow.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: trapWindow.PromptId,
            Choice: trap.InstanceId)).Accepted);
        var defenseWindow = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(absoluteDefense.InstanceId, defenseWindow.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: defenseWindow.PromptId,
            Choice: absoluteDefense.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-discard", discardPrompt.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);

        Assert.Same(takeda, owner.Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-negated"
            && entry.Text.Contains("落穴陷阱"));
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Continuation == "card-effect"
            && prompt.StackItemId == game.State.EffectStack.LastOrDefault()?.StackItemId);
    }

    [Fact]
    public void NegatedTrapStillReturnsToLubuEnterEffectAndTiantingZeroMoraleTrigger()
    {
        var game = Create(6423);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var lubu = Card("S01-0101", "nested-stack-lubu");
        var absoluteDefense = Card("S01-0016", "nested-stack-lubu-defense");
        var trap = Card("S01-0018", "nested-stack-lubu-trap");
        var discard = Card("S01-0003", "nested-stack-lubu-discard");
        var enemyTarget = Card("S01-0102", "nested-stack-lubu-target");
        owner.Hand.Clear();
        lubu.CostModifier = -lubu.Cost;
        owner.Hand.Add(lubu);
        owner.Hand.Add(discard);
        AddReadyMorale(owner, 2);
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = 0;
        owner.Field[1][0] = absoluteDefense;
        trap.Hidden = true;
        trap.SetRound = 0;
        opponent.Field[1][0] = trap;
        opponent.Field[0][0] = enemyTarget;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", lubu.InstanceId, Row: 0, Slot: 0)).Accepted);
        var trapWindow = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: trapWindow.PromptId,
            Choice: trap.InstanceId)).Accepted);
        var defenseWindow = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: defenseWindow.PromptId,
            Choice: absoluteDefense.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);

        var optional = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "card-effect" && prompt.Data.GetValueOrDefault("action") == "lubu-kill");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId,
            Choice: enemyTarget.InstanceId)).Accepted);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Continuation == "faction-zero-recovery");
    }

    [Fact]
    public void PitfallCannotRespondToAnArtifactEnterEffect()
    {
        var game = Create(6426);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var cauldron = Card("S02-0104", "pitfall-artifact-source");
        var trap = Card("S01-0018", "pitfall-artifact-trap");
        owner.Hand.Clear();
        owner.Hand.Add(cauldron);
        AddReadyMorale(owner, cauldron.Cost);
        trap.Hidden = true;
        trap.SetRound = 0;
        opponent.Field[1][0] = trap;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        Assert.True(game.Handle(0, new L12Command("playCard", cauldron.InstanceId)).Accepted);

        Assert.Same(cauldron, owner.Relic);
        Assert.Same(trap, opponent.Field[1][0]);
        Assert.True(trap.Hidden);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(trap.InstanceId));
        Assert.Contains(game.State.PendingPrompts,
            prompt => prompt.Continuation == "card-effect" && prompt.StackItemId is not null);
    }

    [Theory]
    [InlineData("S01-0201")]
    [InlineData("S01-0202")]
    public void SummonTurnCounterTacticProtectionComesFromStructuredRules(string cardId)
    {
        var game = Create(6427);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var protectedLegion = Card(cardId, $"structured-counter-protection-{cardId}");
        var ambush = Card("S01-0019", $"structured-counter-ambush-{cardId}");
        owner.Hand.Clear();
        owner.Hand.Add(protectedLegion);
        AddReadyMorale(owner, protectedLegion.Cost);
        ambush.Hidden = true;
        ambush.SetRound = 0;
        opponent.Field[1][0] = ambush;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var played = game.Handle(0, new L12Command("playCard", protectedLegion.InstanceId, Row: 0, Slot: 0));

        Assert.True(played.Accepted, played.Error);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(ambush.InstanceId));
        Assert.Same(ambush, opponent.Field[1][0]);
        Assert.True(ambush.Hidden);
        Assert.Same(protectedLegion, owner.Field[0][0]);
    }

    [Fact]
    public void RunePowerCompletesOptionalPaymentSearchRevealAndBottomOrder()
    {
        var game = Create(6428);
        var player = game.State.Players[0];
        var runePower = Card("S02-0620", "rune-power-flow");
        var eligible = Card("S02-0609", "rune-power-eligible");
        var neutral = Card("S01-0003", "rune-power-neutral");
        var sameName = Card("S02-0620", "rune-power-same-name");
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Clear();
        player.Hand.Add(runePower);
        player.Library.AddRange([eligible, neutral, sameName]);
        AddReadyMorale(player, runePower.Cost + 1);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var played = game.Handle(0, new L12Command("playCard", runePower.InstanceId));
        Assert.True(played.Accepted, played.Error);
        PassResponses(game);
        var pay = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rune-power-pay-choice", pay.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: pay.PromptId, Choice: "yes")).Accepted);
        if (game.State.PendingPrompts.SingleOrDefault()?.Kind == "resource-payment")
        {
            var resource = Assert.Single(game.State.PendingPrompts);
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: resource.PromptId,
                CardInstanceIds: resource.ValidChoices.Take(1).ToList())).Accepted);
        }

        var pick = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rune-power-pick", pick.Data["action"]);
        Assert.Contains(eligible.InstanceId, pick.ValidChoices);
        Assert.DoesNotContain(neutral.InstanceId, pick.ValidChoices);
        Assert.DoesNotContain(sameName.InstanceId, pick.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: pick.PromptId,
            Choice: eligible.InstanceId)).Accepted);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rune-power-bottom-order", order.Data["action"]);
        var ordered = game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            BottomCardInstanceIds: [sameName.InstanceId, neutral.InstanceId]));
        Assert.True(ordered.Accepted, ordered.Error);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Contains(eligible, player.Hand);
        Assert.Equal([sameName.InstanceId, neutral.InstanceId], player.Library.TakeLast(2).Select(card => card.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal" && entry.Cards.Any(card => card.InstanceId == eligible.InstanceId));
    }

    [Fact]
    public void RobinLabelsSquireSourceAndSummonsTheChosenCardIntoASelectedSlot()
    {
        var game = Create(6429);
        var player = game.State.Players[0];
        var robin = Card("S02-0617", "robin-flow");
        var squire = Card("S02-0609", "robin-library-squire");
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Clear();
        player.Hand.Add(robin);
        player.Library.Add(squire);
        AddReadyMorale(player, robin.Cost);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var played = game.Handle(0, new L12Command("playCard", robin.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        PassResponses(game);
        var choose = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-robin-summon-squire", choose.Data["action"]);
        Assert.Equal("牌库", choose.Data[$"{squire.InstanceId}:zone"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
            Choice: squire.InstanceId)).Accepted);

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("queued-summon-slot", slot.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "1:2")).Accepted);
        Assert.Same(squire, player.Field[1][2]);
        Assert.False(squire.Tapped);
        Assert.DoesNotContain(squire, player.Library);
    }

    [Fact]
    public void RyomaCanAtomicallySwapTwoChosenRestedLegions()
    {
        var game = Create(6430);
        var player = game.State.Players[0];
        var ryoma = Card("S01-0407", "ryoma-flow");
        var first = Card("S01-0401", "ryoma-first");
        var second = Card("S01-0402", "ryoma-second");
        first.Tapped = true;
        second.Tapped = true;
        player.Hand.Clear();
        player.Morale.Clear();
        player.Hand.Add(ryoma);
        player.Field[0][0] = first;
        player.Field[1][2] = second;
        AddReadyMorale(player, ryoma.Cost);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var played = game.Handle(0, new L12Command("playCard", ryoma.InstanceId, Row: 0, Slot: 1));
        Assert.True(played.Accepted, played.Error);
        PassResponses(game);
        var choose = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("ryoma-pick", choose.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
            CardInstanceIds: [first.InstanceId, second.InstanceId])).Accepted);

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("ryoma-slot", slot.Data["action"]);
        Assert.Contains("1:2", slot.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "1:2")).Accepted);
        Assert.Same(second, player.Field[0][0]);
        Assert.Same(first, player.Field[1][2]);
    }

    [Fact]
    public void EnterEffectControllerRegainsResponsePriorityAfterOpponentAmbush()
    {
        var game = Create(6431);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var entering = Card("S01-0114", "enter-response-source");
        var absoluteDefense = Card("S01-0016", "enter-response-defense");
        var ambush = Card("S01-0019", "enter-response-ambush");
        var discardCost = Card("S01-0003", "enter-response-discard");
        owner.Hand.Clear();
        owner.Morale.Clear();
        owner.Hand.AddRange([entering, discardCost]);
        AddReadyMorale(owner, entering.Cost);
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = 0;
        owner.Field[1][0] = absoluteDefense;
        ambush.Hidden = true;
        ambush.SetRound = 0;
        opponent.Field[1][0] = ambush;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var played = game.Handle(0, new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        var opponentResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, opponentResponse.PlayerIndex);
        Assert.Contains(ambush.InstanceId, opponentResponse.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: opponentResponse.PromptId,
            Choice: ambush.InstanceId)).Accepted);

        var ownerResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, ownerResponse.PlayerIndex);
        Assert.Contains(absoluteDefense.InstanceId, ownerResponse.ValidChoices);
    }

    [Fact]
    public void WorldRingMakesUniversalCardsUseOwnersFactionForSharedFilters()
    {
        var game = Create(6424);
        var owner = game.State.Players[0];
        var universalCards = new[]
        {
            Card("S01-0004", "ring-universal-hand"),
            Card("S01-0004", "ring-universal-library"),
            Card("S01-0004", "ring-universal-grave"),
            Card("S01-0004", "ring-universal-removed"),
            Card("S01-0004", "ring-universal-field"),
        };
        owner.Hand.Add(universalCards[0]);
        owner.Library.Add(universalCards[1]);
        owner.Graveyard.Add(universalCards[2]);
        owner.Removed.Add(universalCards[3]);
        owner.Field[0][0] = universalCards[4];
        Assert.All(universalCards,
            universal => Assert.False(L12StructuredCardRules.HasFaction(owner, universal, owner.Faction)));

        var ring = Card("S02-0008", "world-ring");
        owner.ExtraRelics.Add(ring);
        Assert.All(universalCards,
            universal => Assert.True(L12StructuredCardRules.HasFaction(owner, universal, owner.Faction)));
        owner.ExtraRelics.Clear();
        owner.Relic = ring;
        Assert.All(universalCards,
            universal => Assert.True(L12StructuredCardRules.HasFaction(owner, universal, owner.Faction)));
    }

    [Fact]
    public void StrategicTransferReturnsTheWholePromotionStackToHand()
    {
        var game = Create(6433);
        var player = game.State.Players[0];
        var tactic = Card("S01-0009", "strategic-transfer");
        var promoted = Card("S02-0501", "strategic-promoted");
        var foundation = Card("S02-0502", "strategic-foundation");
        var buffTarget = Card("S01-0001", "strategic-buff-target");
        promoted.AttachedCards.Add(foundation);
        player.Hand.Clear();
        player.Hand.Add(tactic);
        player.Field[0][0] = promoted;
        player.Field[0][1] = buffTarget;
        AddReadyMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        PassResponses(game);
        var returnPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("strategic-return", returnPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: returnPrompt.PromptId,
            Choice: promoted.InstanceId)).Accepted);

        Assert.Contains(promoted, player.Hand);
        Assert.Contains(foundation, player.Hand);
        Assert.Empty(promoted.AttachedCards);
        Assert.DoesNotContain(promoted, player.Graveyard);
        Assert.DoesNotContain(foundation, player.Graveyard);
        var buffPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("strategic-buff", buffPrompt.Data["action"]);
    }

    [Fact]
    public void DefeatedPromotionStackMovesHostAndFoundationToTheSameGraveyard()
    {
        var game = Create(6434);
        var player = game.State.Players[0];
        var promoted = Card("S02-0501", "defeated-promoted");
        var foundation = Card("S02-0502", "defeated-foundation");
        promoted.AttachedCards.Add(foundation);
        player.Field[0][0] = promoted;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: promoted.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Contains(promoted, player.Graveyard);
        Assert.Contains(foundation, player.Graveyard);
        Assert.Empty(promoted.AttachedCards);
        Assert.DoesNotContain(promoted, player.Hand);
        Assert.DoesNotContain(foundation, player.Hand);
    }

    [Fact]
    public void WisdomCodexLetsOpponentAbandonTheCurrentTacticEffectWithoutDiscarding()
    {
        var game = Create(6435);
        var codexOwner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var wisdom = Card("S01-0224", "wisdom-abandon");
        var negotiation = Card("S01-0015", "wisdom-opponent-tactic");
        wisdom.Hidden = true;
        wisdom.SetRound = 0;
        codexOwner.Field[1][0] = wisdom;
        codexOwner.Hand.Clear();
        opponent.Hand.Clear();
        opponent.Hand.Add(negotiation);
        var opponentLibraryBefore = opponent.Library.Count;
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("playCard", negotiation.InstanceId)).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, response.PlayerIndex);
        Assert.Contains(wisdom.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: wisdom.InstanceId)).Accepted);
        PassResponses(game);

        var discardOrAbandon = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("wisdom-discard", discardOrAbandon.Data["action"]);
        Assert.Equal(["abandon"], discardOrAbandon.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: discardOrAbandon.PromptId,
            Choice: "abandon")).Accepted);

        Assert.Equal(opponentLibraryBefore, opponent.Library.Count);
        Assert.Empty(codexOwner.Hand);
        Assert.Contains(wisdom, codexOwner.Graveyard);
        Assert.Contains(negotiation, opponent.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-abandoned");
    }

    [Fact]
    public void WisdomCodexRewardsOnlyAfterTheOpponentDiscardsAndArtifactEntrySucceeds()
    {
        var game = Create(6436);
        var codexOwner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var wisdom = Card("S01-0224", "wisdom-success");
        var canopic = Card("S01-0219", "wisdom-artifact-entry");
        var discard = Card("S01-0001", "wisdom-discard-cost");
        var recovery = Card("S01-0012", "wisdom-recovery");
        var draw = Card("S01-0001", "wisdom-draw");
        wisdom.Hidden = true;
        wisdom.SetRound = 0;
        codexOwner.Field[1][0] = wisdom;
        codexOwner.Hand.Clear();
        codexOwner.Library.Clear();
        codexOwner.Library.Add(draw);
        codexOwner.Graveyard.Clear();
        codexOwner.Graveyard.Add(recovery);
        opponent.Hand.Clear();
        opponent.Hand.AddRange([canopic, discard]);
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("playCard", canopic.InstanceId)).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(wisdom.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: wisdom.InstanceId)).Accepted);
        PassResponses(game);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(discard.InstanceId, discardPrompt.ValidChoices);
        Assert.Contains("abandon", discardPrompt.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);
        PassResponses(game);

        var recoveryPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("wisdom-recover", recoveryPrompt.Data["action"]);
        Assert.Contains(recovery.InstanceId, recoveryPrompt.ValidChoices);
        Assert.Contains(draw, codexOwner.Hand);
        Assert.Contains(discard, opponent.Graveyard);
        Assert.Equal(2, opponent.TemporaryMorale);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: recoveryPrompt.PromptId,
            Choice: recovery.InstanceId)).Accepted);
        Assert.Contains(recovery, codexOwner.Hand);
    }

    [Fact]
    public void WisdomCodexCanRespondToArtifactActiveAndTriggeredStackEffects()
    {
        var activeGame = Create(6437);
        var activeCodexOwner = activeGame.State.Players[0];
        var activeOpponent = activeGame.State.Players[1];
        var activeWisdom = Card("S01-0224", "wisdom-active");
        activeWisdom.Hidden = true;
        activeWisdom.SetRound = 0;
        activeCodexOwner.Field[1][0] = activeWisdom;
        var ankh = Card("S01-0215", "wisdom-active-artifact");
        var guard = Card("S01-0212", "wisdom-active-guard");
        activeOpponent.Relic = ankh;
        activeOpponent.Field[0][0] = guard;
        activeGame.State.ActivePlayer = 1;
        activeGame.State.Round = 2;
        activeGame.State.Phase = L12Phase.Main;

        Assert.True(activeGame.Handle(1, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhDraw")).Accepted);
        var targetPrompt = Assert.Single(activeGame.State.PendingPrompts);
        Assert.True(activeGame.Handle(1, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);
        var activeResponse = Assert.Single(activeGame.State.PendingPrompts);
        Assert.Contains(activeWisdom.InstanceId, activeResponse.ValidChoices);

        var triggeredGame = Create(6438);
        var triggeredCodexOwner = triggeredGame.State.Players[0];
        var triggeredOpponent = triggeredGame.State.Players[1];
        var triggeredWisdom = Card("S01-0224", "wisdom-triggered");
        var ring = Card("S02-0305", "wisdom-triggered-artifact");
        var oddr = Card("S01-0313", "wisdom-trigger-source");
        triggeredWisdom.Hidden = true;
        triggeredWisdom.SetRound = 0;
        triggeredCodexOwner.Field[1][0] = triggeredWisdom;
        triggeredOpponent.Relic = ring;
        triggeredOpponent.Hand.Clear();
        triggeredOpponent.Hand.Add(oddr);
        triggeredOpponent.Hp = triggeredOpponent.MaxHp - 2;
        AddReadyMorale(triggeredOpponent, oddr.Cost);
        triggeredGame.State.ActivePlayer = 1;
        triggeredGame.State.Round = 2;
        triggeredGame.State.Phase = L12Phase.Main;

        Assert.True(triggeredGame.Handle(1, new L12Command("playCard", oddr.InstanceId,
            Row: 0, Slot: 0)).Accepted);
        PassResponses(triggeredGame);
        var damagePrompt = Assert.Single(triggeredGame.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "oddr-draw");
        Assert.True(triggeredGame.Handle(1, new L12Command("resolvePrompt", PromptId: damagePrompt.PromptId,
            Choice: "yes")).Accepted);
        var triggeredResponse = Assert.Single(triggeredGame.State.PendingPrompts);
        Assert.Contains(triggeredWisdom.InstanceId, triggeredResponse.ValidChoices);
        Assert.Contains("主宰受到伤害时效果", triggeredGame.State.EffectStack[^1].Text);
    }

    [Fact]
    public void BattlefieldSnapshotPublishesActiveKeywordsAndMasterLegionTroops()
    {
        var game = Create(6432);
        var player = game.State.Players[0];
        var wukong = Card("S02-01M1", "wukong-field-view");
        wukong.IsMasterLegion = true;
        wukong.HasCharge = true;
        wukong.SummonRound = game.State.Round;
        wukong.SetTroopsValue = 4000;
        wukong.Troops = 4000;
        player.Field[0][0] = wukong;

        var keywordLegion = Card("S01-0002", "keyword-field-view");
        keywordLegion.HasStrongAttack = true;
        keywordLegion.HasSureHit = true;
        keywordLegion.HasShock = true;
        keywordLegion.ImmortalUses = 1;
        keywordLegion.ImmortalUntilTurn = game.State.TurnSerial;
        keywordLegion.TauntUntilTurn = game.State.TurnSerial;
        player.UsedAbilities.Add($"crusade-piercing:{keywordLegion.InstanceId}:{game.State.TurnSerial}");
        player.Field[0][1] = keywordLegion;

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var field = snapshot.GetProperty("players")[0].GetProperty("field");
        var wukongView = field[0][0];
        Assert.True(wukongView.GetProperty("isMasterLegion").GetBoolean());
        Assert.Equal(4000, wukongView.GetProperty("troops").GetInt32());
        Assert.Equal(4000, wukongView.GetProperty("displayBaseTroops").GetInt32());
        Assert.Equal(["冲锋"], wukongView.GetProperty("activeKeywords").EnumerateArray()
            .Select(value => value.GetString()!).ToArray());

        var keywords = field[0][1].GetProperty("activeKeywords").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        Assert.Equal(["强攻", "免死", "必中", "挑衅", "震击", "贯穿"], keywords);
    }
}
