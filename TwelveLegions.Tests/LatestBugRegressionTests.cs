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
    public void PromotionLegionKeepsNormalEntryModeAfterManualResourceSelection()
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
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-morale-choice", payment.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: player.Morale.Select(card => card.InstanceId).ToList())).Accepted);

        Assert.Same(promoted, player.Field[0][0]);
        Assert.Same(foundation, player.Field[0][1]);
        Assert.DoesNotContain(promoted, player.Hand);
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
        for (var step = 0; step < 12 && (!defender.Graveyard.Contains(target)
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

        Assert.Contains(target, defender.Graveyard);
        Assert.NotNull(game.State.PendingDefense);
        Assert.Equal("master", game.State.PendingDefense!.Target.Type);
        Assert.True(game.State.PendingDefense.SuppressAttackTriggers);
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
    public void WorldRingMakesUniversalCardsUseOwnersFactionForSharedFilters()
    {
        var game = Create(6424);
        var owner = game.State.Players[0];
        var universal = Card("S01-0004", "ring-universal-card");
        Assert.False(L12StructuredCardRules.HasFaction(owner, universal, owner.Faction));
        owner.Relic = Card("S02-0008", "world-ring");
        Assert.True(L12StructuredCardRules.HasFaction(owner, universal, owner.Faction));
    }
}
