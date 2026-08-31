using TwelveLegions.Server;
using Xunit;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace TwelveLegions.Tests;

public sealed class ExtendedCardEffectsTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int firstDeck, int secondDeck, int seed = 9012)
        => new(Catalog, "extended-effects", "EFFECT", seed, ["甲", "乙"], [firstDeck, secondDeck], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed = 9012)
    {
        var baseDeck = Catalog.DeckAt(2);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}测试牌库", MasterId = masterId,
            CardIds = [.. baseDeck.CardIds], MoraleIds = [.. baseDeck.MoraleIds], SpecialIds = [.. baseDeck.SpecialIds],
        };
        return new L12GameEngine(Catalog, "extended-effects", "EFFECT", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true);
    }

    private static void ReadyMain(L12GameEngine game, int playerIndex)
    {
        game.State.ActivePlayer = playerIndex;
        game.State.Phase = L12Phase.Main;
        while (game.State.Players[playerIndex].MoraleDeck.Count > 0)
        {
            var morale = game.State.Players[playerIndex].MoraleDeck[0];
            game.State.Players[playerIndex].MoraleDeck.RemoveAt(0);
            game.State.Players[playerIndex].Morale.Add(morale);
        }
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            Traits = [.. definition.Traits], Profession = definition.Profession,
        };
    }

    private static L12CardInstance TakeCard(L12PlayerState player, string cardId)
    {
        var card = player.Hand.Concat(player.Library).FirstOrDefault(candidate => candidate.CardId == cardId)
            ?? Card(cardId, $"test-{player.PlayerIndex}-{cardId}-{Guid.NewGuid():N}");
        player.Hand.Remove(card);
        player.Library.Remove(card);
        player.Hand.Add(card);
        return card;
    }

    [Fact]
    public void SolarCityPresetStartsWithThreeTombGuardsInGraveyard()
    {
        var game = Create(2, 3);
        var solar = game.State.Players[0];
        Assert.Equal("taiyangcheng", solar.Faction);
        Assert.Equal(3, solar.Graveyard.Count(card => card.CardId == "S01-0212"));
        Assert.DoesNotContain(solar.Library, card => card.CardId == "S01-0212");
    }

    [Fact]
    public void BeowulfEntryMillsExactlyTwoCards()
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        var beowulf = TakeCard(player, "S01-0301");
        ReadyMain(game, 0);
        var before = player.Library.Count;
        Assert.True(game.Handle(0, new L12Command("playCard", beowulf.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        Assert.Equal(before - 2, player.Library.Count);
        Assert.Contains(game.State.Events, item => item.Type == "mill" && item.Text.Contains("贝奥武夫"));
    }

    [Fact]
    public void CanopicArtifactsDoNotReplaceThePrimaryRelic()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ankh = player.Hand.Concat(player.Library).First(card => card.CardId == "S01-0215");
        player.Hand.Remove(ankh); player.Library.Remove(ankh); player.Hand.Add(ankh);
        Assert.True(game.Handle(0, new L12Command("playCard", ankh.InstanceId)).Accepted);
        PassResponses(game);
        if (game.State.PendingPrompts.Count > 0)
        {
            var prompt = game.State.PendingPrompts[0];
            var choice = prompt.ValidChoices.FirstOrDefault() ?? "skip";
            game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        }
        var canopic = Card("S01-0217", "test-canopic-one");
        player.Hand.Add(canopic);
        Assert.True(game.Handle(0, new L12Command("playCard", canopic.InstanceId)).Accepted);
        Assert.Equal(ankh.InstanceId, player.Relic?.InstanceId);
        Assert.Contains(player.ExtraRelics, card => card.InstanceId == canopic.InstanceId);
    }

    [Fact]
    public void AnkhSteleActiveEffectsPayTheirCostsAndResolve()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ankh = Card("S01-0215", "test-ankh");
        player.Relic = ankh;
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard); guard.Tapped = true; player.Field[0][0] = guard;
        var discard = player.Library[0]; player.Library.RemoveAt(0); player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId, Ability: "ankhReady")).Accepted);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", guardPrompt.Continuation);
        Assert.Contains(guard.InstanceId, guardPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId, Choice: guard.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", discardPrompt.Continuation);
        Assert.Contains(discard.InstanceId, discardPrompt.ValidChoices);
        Assert.False(ankh.Tapped);
        Assert.Contains(discard, player.Hand);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == ankh.InstanceId);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId, Choice: discard.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(ankh.Tapped);
        Assert.False(guard.Tapped);
        Assert.Contains(discard, player.Graveyard);

        guard.Tapped = false;
        var restedAttempt = game.Handle(0, new L12Command("activateAbility", ankh.InstanceId, Ability: "ankhDraw"));
        Assert.False(restedAttempt.Accepted);
        Assert.Contains("休整", restedAttempt.Error);
        Assert.Empty(game.State.PendingPrompts);

        ankh.Tapped = false;
        var handBefore = player.Hand.Count;
        var readiedAttempt = game.Handle(0, new L12Command("activateAbility", ankh.InstanceId, Ability: "ankhDraw"));
        Assert.True(readiedAttempt.Accepted, readiedAttempt.Error);
        var guardCostPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(guard.InstanceId, guardCostPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardCostPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);
        PassResponses(game);
        Assert.True(ankh.Tapped);
        Assert.True(guard.Tapped);
        Assert.Equal(handBefore + 1, player.Hand.Count);
    }

    [Fact]
    public void AnkhSteleEntryDeclaresATombGuardAndGrantsTwoThousandForThisTurn()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ankh = TakeCard(player, "S01-0215");
        var guard = Card("S01-0212", "ankh-entry-guard");
        player.Field[0][0] = guard;

        Assert.True(game.Handle(0, new L12Command("playCard", ankh.InstanceId)).Accepted);
        if (game.State.PendingPrompts.SingleOrDefault()?.Kind == "resource-payment")
        {
            var payment = Assert.Single(game.State.PendingPrompts);
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
                CardInstanceIds: player.Morale.Where(card => !card.Tapped).Take(ankh.Cost)
                    .Select(card => card.InstanceId).ToList())).Accepted);
        }
        PassResponses(game);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("card-effect", target.Continuation);
        Assert.Contains("安卡神碑", target.Text);
        Assert.Contains(guard.InstanceId, target.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: guard.InstanceId)).Accepted);

        Assert.Equal(guard.BaseTroops + 2000, guard.Troops);
        var bonus = Assert.Single(guard.TimedModifiers, modifier => modifier.Source == "安卡神碑");
        Assert.Equal(2000, bonus.TroopsDelta);
        Assert.Equal(game.State.TurnSerial, bonus.ExpiresAfterTurn);
    }

    [Fact]
    public void EffectDrawResponsePublishesOnlyPlayerCountAndOriginSource()
    {
        var game = new L12GameEngine(Catalog, "draw-privacy", "DRAWPRIVATE", 9021,
            ["甲", "乙"], [2, 3], skipPreparation: true, autoPassEmptyResponses: false);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ankh = Card("S01-0215", "draw-privacy-ankh");
        player.Relic = ankh;
        var guard = Card("S01-0212", "draw-privacy-guard");
        player.Field[0][0] = guard;
        var secret = Card("S01-0205", "draw-privacy-secret");
        player.Library.Clear();
        player.Library.Add(secret);

        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId, Ability: "ankhDraw")).Accepted);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);

        while (game.State.EffectStack.LastOrDefault()?.Data.GetValueOrDefault("eventType") != "effect-hand-add"
            && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }

        var authority = Assert.Single(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("eventType") == "effect-hand-add");
        Assert.NotEqual(secret.InstanceId, authority.SourceInstanceId);
        Assert.NotEqual(secret.CardId, authority.SourceCardId);
        Assert.Equal(ankh.Name, authority.SourceName);
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
        {
            var protocol = JsonSerializer.Serialize(new
            {
                snapshot.Prompts,
                snapshot.EffectStack,
                snapshot.LastAction,
                snapshot.RecentEvents,
            }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            Assert.DoesNotContain(secret.InstanceId, protocol);
            Assert.DoesNotContain(secret.CardId, protocol);
            Assert.DoesNotContain(secret.Name, protocol);
            Assert.Contains(ankh.Name, protocol);
            Assert.Contains("1张牌", protocol);
        }
        Assert.DoesNotContain(game.State.Log, line => line.Contains(secret.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void GramDamageActiveEffectReturnsFourAsgardLegionsInChosenOrder()
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var gram = Card("S01-0317", "test-gram"); player.Relic = gram;
        var costs = player.Library.Where(card => card.CardType == "legion" && card.Faction == "asgard").Take(4).ToArray();
        foreach (var card in costs) { player.Library.Remove(card); player.Graveyard.Add(card); }
        var chosenOrder = costs.Reverse().Select(card => card.InstanceId).ToList();
        var enemyHp = game.State.Players[1].Hp;

        Assert.True(game.Handle(0, new L12Command("activateAbility", gram.InstanceId, Ability: "gramDamage")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId, CardInstanceIds: chosenOrder)).Accepted);
        PassResponses(game);

        Assert.True(gram.Tapped);
        Assert.Equal(chosenOrder, player.Library.TakeLast(4).Select(card => card.InstanceId));
        Assert.Equal(enemyHp - 1, game.State.Players[1].Hp);
    }

    [Fact]
    public void PharaohFestivalUsesHandThenGraveThenOrderedBottomStages()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var festival = player.Hand.Concat(player.Library).First(card => card.CardId == "S01-0222");
        player.Hand.Remove(festival); player.Library.Remove(festival); player.Hand.Add(festival);
        var eligible = player.Library.Where(card => card.Faction == "taiyangcheng" && card.CardId != "S01-0222").Take(2).ToArray();
        foreach (var card in eligible) player.Library.Remove(card);
        player.Library.InsertRange(0, eligible);

        Assert.True(game.Handle(0, new L12Command("playCard", festival.InstanceId)).Accepted);
        PassResponses(game);
        var handPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-hand", handPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: handPrompt.PromptId, Choice: eligible[0].InstanceId)).Accepted);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-grave", gravePrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: gravePrompt.PromptId, Choice: eligible[1].InstanceId)).Accepted);
        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-bottom", orderPrompt.Data["placementMode"]);
        var order = orderPrompt.ValidChoices.AsEnumerable().Reverse().ToList();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId, BottomCardInstanceIds: order)).Accepted);

        Assert.Contains(eligible[0], player.Hand);
        Assert.Contains(eligible[1], player.Graveyard);
        Assert.Equal(order, player.Library.TakeLast(order.Count).Select(card => card.InstanceId));
    }

    [Fact]
    public void SolarCityPlayerChoosesWhetherTombGuardsPayAPlayedCardsCost()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard); player.Field[0][0] = guard;
        var legion = Card("S01-0205", "test-paid-legion"); player.Hand.Add(legion);

        Assert.True(game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 1)).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-morale-choice", paymentPrompt.Continuation);
        Assert.Equal("resource-payment", paymentPrompt.Kind);
        var activeMoraleBefore = player.Morale.Count(card => !card.Tapped);
        var selected = new[] { guard.InstanceId }
            .Concat(player.Morale.Where(card => !card.Tapped).Take(legion.Cost - 1).Select(card => card.InstanceId)).ToList();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId, CardInstanceIds: selected)).Accepted);

        Assert.True(guard.Tapped);
        Assert.Equal(activeMoraleBefore - (legion.Cost - 1), player.Morale.Count(card => !card.Tapped));
        Assert.Equal(legion, player.Field[0][1]);
    }

    [Fact]
    public void SolarCityPlayerAlsoChoosesTombGuardPaymentForActiveAbilities()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        while (player.Hand.Count > 3) { var card = player.Hand[^1]; player.Hand.RemoveAt(player.Hand.Count - 1); player.Library.Add(card); }
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard); player.Field[0][0] = guard;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "sunDraw")).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("active-morale-choice", paymentPrompt.Continuation);
        Assert.Equal("resource-payment", paymentPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId,
            CardInstanceIds: [guard.InstanceId])).Accepted);

        Assert.True(guard.Tapped);
        PassResponses(game);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveGodPowerMakesOrdinaryMoralePaymentAPlayerChoice(bool payWithGodPower)
    {
        var game = Create(0, 1);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var legion = Card("S01-0116", "god-power-payment-legion");
        player.Hand.Add(legion);
        var godPower = player.Morale.First(card => !card.Tapped);
        godPower.IsGodPower = true;
        var ordinaryMorale = player.Morale.First(card => !card.Tapped && card.InstanceId != godPower.InstanceId);

        Assert.True(game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 0)).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", paymentPrompt.Kind);
        Assert.Equal("play-morale-choice", paymentPrompt.Continuation);
        Assert.Equal("god-power", paymentPrompt.Data[$"{godPower.InstanceId}:resourceType"]);

        var selected = payWithGodPower ? godPower : ordinaryMorale;
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId,
            CardInstanceIds: [selected.InstanceId])).Accepted);

        Assert.Equal(!payWithGodPower, ordinaryMorale.Tapped);
        Assert.False(ordinaryMorale.IsGodPower);
        Assert.Equal(payWithGodPower, godPower.Tapped);
        Assert.True(godPower.IsGodPower);
        Assert.Same(legion, player.Field[0][0]);
    }

    [Fact]
    public void ExtendedAbilityMetadataIsPublishedBySnapshots()
    {
        var game = Create(0, 1);
        var snapshot = game.SnapshotFor(0);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.Contains("drawCycle", json);
        Assert.Contains("factionAddActive", json);
    }

    [Fact]
    public void DisabledAndTriggerOnlyAbilitiesRemainVisibleButCannotBeActivated()
    {
        var game = Create(2, 3);
        ReadyMain(game, 0);
        var snapshot = game.SnapshotFor(0);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        var player = document.RootElement.GetProperty("Players")[0];
        var factionAbilities = player.GetProperty("factionEffect").GetProperty("abilities").EnumerateArray().ToArray();
        var sunDraw = factionAbilities.Single(entry => entry.GetProperty("Id").GetString() == "sunDraw");
        Assert.False(sunDraw.GetProperty("Enabled").GetBoolean());

        var masterAbilities = player.GetProperty("master").GetProperty("abilities").EnumerateArray().ToArray();
        var reaction = masterAbilities.Single(entry => entry.GetProperty("Id").GetString() == "medjedDamageResponse");
        Assert.False(reaction.GetProperty("Enabled").GetBoolean());
        Assert.True(reaction.GetProperty("TriggerOnly").GetBoolean());
    }

    [Fact]
    public void MedjedOpponentTurnDamageOffersTombGuardAndThenBoardPosition()
    {
        var game = Create(2, 3);
        var defender = game.State.Players[0];
        var attackerPlayer = game.State.Players[1];
        ReadyMain(game, 1);
        game.State.Round = 2;
        var attacker = Card("S01-0002", "medjed-test-attacker");
        attacker.SummonRound = 0;
        attackerPlayer.Field[0][0] = attacker;

        Assert.True(game.Handle(1, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.True(game.Handle(0, new L12Command("resolveDefense")).Accepted);
        PassResponses(game);

        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("medjed-damage-response", guardPrompt.Data["action"]);
        var guardId = guardPrompt.ValidChoices.First(id => id != "skip");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId, Choice: guardId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: slotPrompt.ValidChoices[0])).Accepted);
        Assert.Contains(defender.Field.SelectMany(row => row), card => card?.InstanceId == guardId && !card.Tapped);
    }

    [Fact]
    public void DisasterDamageIsNeutralAndDoesNotTriggerMedjed()
    {
        var game = Create(2, 3, 9013);
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 9;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS02", "neutral-disaster"));

        Assert.True(game.Handle(1, new L12Command("endTurn")).Accepted);
        foreach (var prompt in game.State.PendingPrompts
                     .Where(prompt => prompt.Continuation == "disaster-trigger-confirm").ToArray())
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId)).Accepted);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "medjed-damage-response");
        Assert.DoesNotContain(game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates),
            candidate => candidate.SourceCardId == "S01-02M3");
    }

    [Fact]
    public void BlackbeardLocksBothDiscardChoicesBeforeApplyingEither()
    {
        var game = Create(0, 1, 9014);
        var owner = game.State.Players[0];
        var enemy = game.State.Players[1];
        ReadyMain(game, 0);
        var teach = Card("S01-0001", "teach-simultaneous");
        var ownerCards = new[] { Card("S01-0002", "teach-owner-a"), Card("S01-0003", "teach-owner-b") };
        var enemyCards = new[] { Card("S01-0002", "teach-enemy-a"), Card("S01-0003", "teach-enemy-b") };
        owner.Hand.Clear(); enemy.Hand.Clear();
        owner.Hand.Add(teach); owner.Hand.AddRange(ownerCards); enemy.Hand.AddRange(enemyCards);

        Assert.True(game.Handle(0, new L12Command("playCard", teach.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var prompts = game.State.PendingPrompts.Where(prompt => prompt.Data.GetValueOrDefault("action") == "teach-discard").ToArray();
        Assert.Equal(2, prompts.Length);
        Assert.All(prompts, prompt => Assert.Equal("true", prompt.Data["simultaneous"]));

        var first = prompts[0];
        var firstCards = first.ValidChoices.Take(2).ToList();
        Assert.True(game.Handle(first.PlayerIndex, new L12Command("resolvePrompt", PromptId: first.PromptId,
            CardInstanceIds: firstCards)).Accepted);
        Assert.All(firstCards, id => Assert.Contains(StateHand(game, first.PlayerIndex), card => card.InstanceId == id));

        var second = game.State.PendingPrompts.Single(prompt => prompt.Data.GetValueOrDefault("action") == "teach-discard");
        var secondCards = second.ValidChoices.Take(2).ToList();
        Assert.True(game.Handle(second.PlayerIndex, new L12Command("resolvePrompt", PromptId: second.PromptId,
            CardInstanceIds: secondCards)).Accepted);
        Assert.All(firstCards, id => Assert.Contains(game.State.Players[first.PlayerIndex].Graveyard, card => card.InstanceId == id));
        Assert.All(secondCards, id => Assert.Contains(game.State.Players[second.PlayerIndex].Graveyard, card => card.InstanceId == id));
    }

    [Fact]
    public void AlvidaActiveDiscardIsNotDefeatAndDoesNotQueueHerDeathEffect()
    {
        var game = Create(3, 2, 9015);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var alvida = Card("S01-0307", "alvida-discard");
        alvida.ImmortalUses = 1;
        alvida.ImmortalUntilTurn = game.State.TurnSerial;
        var summon = Card("S01-0308", "alvida-summon-target");
        player.Field[0][0] = alvida;
        player.Hand.Add(summon);
        var hpBeforeActivation = player.Hp;

        Assert.True(game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon")).Accepted);
        var summonDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", summonDeclaration.Continuation);
        var selectedSummon = player.Hand.First(card => summonDeclaration.ValidChoices.Contains(card.InstanceId));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: summonDeclaration.PromptId,
            Choice: selectedSummon.InstanceId)).Accepted);
        var slotDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", slotDeclaration.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotDeclaration.PromptId,
            Choice: "0:1")).Accepted);
        Assert.Contains(alvida, player.Graveyard);
        Assert.Equal(0, alvida.ImmortalUses);
        Assert.Equal(-1, alvida.ImmortalUntilTurn);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("免死生效"));
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.SourceInstanceId == alvida.InstanceId && item.Trigger == "death");
        PassResponses(game);

        Assert.Same(selectedSummon, player.Field[0][1]);
        Assert.Equal(hpBeforeActivation - 1, player.Hp);
        Assert.DoesNotContain(selectedSummon, player.Hand);
    }

    [Fact]
    public void FictitiousChaliceDamagesTheControllerForAnArtifactActiveEffect()
    {
        var game = Create(2, 3, 9017);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        game.State.ActiveDisaster = Card("S01-DS08", "fictitious-chalice");
        var ankh = Card("S01-0215", "chalice-ankh");
        player.Relic = ankh;
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard);
        guard.Tapped = false;
        player.Field[0][0] = guard;
        var hpBefore = player.Hp;

        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId,
            Ability: "ankhDraw")).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);

        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("虚构的圣杯", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:lokiHeal")]
    public void LokiHealReturnsTheTwoGraveCardsChosenByThePlayer()
    {
        var game = Create(3, 2, 9016);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        player.Graveyard.Clear();
        var cards = Enumerable.Range(0, 20)
            .Select(index => Card("S01-0301", $"loki-grave-{index}"))
            .ToArray();
        player.Graveyard.AddRange(cards);
        player.Hp--;
        var hpBefore = player.Hp;

        var activeMoraleBefore = player.Morale.Count(card => !card.Tapped);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "lokiHeal")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Equal(20, prompt.ValidChoices.Count(choice => choice != "skip"));
        Assert.Equal(activeMoraleBefore, player.Morale.Count(card => !card.Tapped));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [cards[^1].InstanceId, cards[^2].InstanceId])).Accepted);
        PassResponses(game);

        Assert.Equal(cards.Take(18), player.Graveyard);
        Assert.DoesNotContain(cards[^2], player.Graveyard);
        Assert.DoesNotContain(cards[^1], player.Graveyard);
        Assert.Equal([cards[^1].InstanceId, cards[^2].InstanceId], player.Library.TakeLast(2).Select(card => card.InstanceId));
        Assert.Equal(hpBefore + 1, player.Hp);

        var secondLokiEffect = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "lokiCycle"));
        Assert.False(secondLokiEffect.Accepted);
        Assert.Contains("本回合", secondLokiEffect.Error);
    }

    private static IEnumerable<L12CardInstance> StateHand(L12GameEngine game, int playerIndex)
        => game.State.Players[playerIndex].Hand;

    [Fact]
    [Trait("L12Evidence", "type:rune")]
    public void BothOlympusMoraleFacesUseMoraleClassification()
    {
        Assert.Equal("rune", Catalog.Cards["S02-05C1"].CardType);
        Assert.Equal("rune", Catalog.Cards["S02-05C1A"].CardType);
    }

    [Theory]
    [InlineData("S02-05C1")]
    [InlineData("S02-05C1A")]
    public void BothOlympusMoraleCardIdsUseTheirCurrentFaceWhenConsumedAsGodPower(string cardId)
    {
        var player = new L12PlayerState
        {
            PlayerIndex = 0,
            Name = "Olympus",
            DeckName = "Olympus test",
            Faction = "olympus",
            MasterId = "S02-05M1",
            MasterName = "Artemis",
        };
        var morale = new L12MoraleCard { CardId = cardId, InstanceId = $"{cardId}-morale", IsGodPower = true };
        player.Morale.Add(morale);

        Assert.True(L12S2ZoneOps.ConsumeAndFlipGodPower(player, 1));
        Assert.True(morale.Tapped);
        Assert.False(morale.IsGodPower);
    }

    [Fact]
    public void EgilDoesNotRepeatHisEntryEffectWhenAttacking()
    {
        var game = Create(3, 2);
        ReadyMain(game, 0);
        game.State.Round = 2;
        var egil = Card("S01-0316", "test-egil");
        var target = Card("S01-0212", "test-target");
        egil.SummonRound = 0;
        target.SummonRound = 0;
        game.State.Players[0].Field[0][0] = egil;
        game.State.Players[1].Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("attack", egil.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Text.Contains("夺命诗人埃吉尔"));
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(L12Phase.Main, game.State.Phase);
    }

    [Fact]
    public void AyLetsThePlayerChooseWhereTheTombGuardEnters()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ay = TakeCard(player, "S01-0208");

        Assert.True(game.Handle(0, new L12Command("playCard", ay.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var placement = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("queued-summon-slot", placement.Data["action"]);
        Assert.Contains("阿伊", placement.Text);
        var chosenSlot = placement.ValidChoices.Last();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: placement.PromptId, Choice: chosenSlot)).Accepted);

        var parts = chosenSlot.Split(':');
        var row = int.Parse(parts[0]);
        var slot = int.Parse(parts[1]);
        Assert.Equal("S01-0212", player.Field[row][slot]?.CardId);
        Assert.True(player.Field[row][slot]!.Tapped);
    }

    [Fact]
    public void GramBottomReplacementAlwaysSendsTombGuardToItsOwnersGraveyard()
    {
        var game = Create(3, 2);
        var asgard = game.State.Players[0];
        var solar = game.State.Players[1];
        ReadyMain(game, 0);
        var guard = solar.Graveyard.First(card => card.CardId == "S01-0212");
        solar.Graveyard.Remove(guard);
        solar.Field[0][0] = guard;
        var gram = TakeCard(asgard, "S01-0317");

        Assert.True(game.Handle(0, new L12Command("playCard", gram.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("gram-bottom", prompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: guard.InstanceId)).Accepted);

        Assert.DoesNotContain(solar.Library, card => card.InstanceId == guard.InstanceId);
        Assert.Contains(solar.Graveyard, card => card.InstanceId == guard.InstanceId);
        Assert.Contains(game.State.Events, item => item.Type == "replacement" && item.Cards.Any(card => card.InstanceId == guard.InstanceId));
    }

    [Fact]
    public void GenericReturnToLibraryEffectsCannotChooseDerivedSpecialCards()
    {
        var game = Create(3, 2);
        var asgard = game.State.Players[0];
        var opponent = game.State.Players[1];
        ReadyMain(game, 0);
        var xiaotian = Card("S02-01S1", "test-derived-xiaotian");
        var legalTarget = Card("S01-0003", "test-legal-target");
        opponent.Field[0][0] = xiaotian;
        opponent.Field[0][1] = legalTarget;
        var gram = TakeCard(asgard, "S01-0317");

        Assert.True(game.Handle(0, new L12Command("playCard", gram.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("gram-bottom", prompt.Data["action"]);
        Assert.DoesNotContain(xiaotian.InstanceId, prompt.ValidChoices);
        Assert.Contains(legalTarget.InstanceId, prompt.ValidChoices);
    }

    [Fact]
    public void BloodEagleOffersEveryOtherLegalAsgardCardFromTheGraveyard()
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        game.State.Round = 2;
        var bloodEagle = Card("S01-0320", "test-blood-eagle-source");
        bloodEagle.Hidden = true;
        bloodEagle.SetRound = 0;
        player.Field[1][0] = bloodEagle;
        var fallen = Card("S01-0309", "test-blood-eagle-fallen");
        player.Field[0][0] = fallen;
        var graveA = Card("S01-0311", "test-blood-eagle-grave-a");
        var graveB = Card("S01-0312", "test-blood-eagle-grave-b");
        var graveC = Card("S01-0313", "test-blood-eagle-grave-c");
        player.Graveyard.AddRange([graveA, graveB, graveC]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: fallen.InstanceId)).Accepted);
        PassResponses(game);

        var triggerOrder = game.State.PendingPrompts.FirstOrDefault(candidate => candidate.Kind == "trigger-order");
        if (triggerOrder is not null)
        {
            var ordered = triggerOrder.ValidChoices
                .OrderByDescending(id => triggerOrder.Data.GetValueOrDefault($"sourceInstance:{id}") == bloodEagle.InstanceId)
                .ToList();
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: triggerOrder.PromptId,
                CardInstanceIds: ordered)).Accepted);
            PassResponses(game);
        }

        var prompt = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Data.GetValueOrDefault("action") == "blood-eagle-pick");
        Assert.DoesNotContain(bloodEagle.InstanceId, prompt.ValidChoices);
        Assert.Contains(graveA.InstanceId, prompt.ValidChoices);
        Assert.Contains(graveB.InstanceId, prompt.ValidChoices);
        Assert.Contains(graveC.InstanceId, prompt.ValidChoices);
    }

    [Fact]
    public void ValhallaDeclaresGraveCardsAndBothDistinctKillTargetsBeforePayingItsRestCost()
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        ReadyMain(game, 0);
        var valhalla = Card("S01-03D1", "test-valhalla");
        player.Relic = valhalla;
        var graveA = Card("S01-0309", "test-valhalla-grave-a");
        var graveB = Card("S01-0311", "test-valhalla-grave-b");
        var graveC = Card("S01-0312", "test-valhalla-grave-c");
        player.Graveyard.AddRange([graveA, graveB, graveC]);
        var low = Card("S01-0004", "test-valhalla-low");
        var broad = Card("S01-0003", "test-valhalla-broad");
        enemy.Field[0][0] = low;
        enemy.Field[0][1] = broad;

        Assert.True(game.Handle(0, new L12Command("activateAbility", valhalla.InstanceId, Ability: "valhallaKill")).Accepted);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(graveC.InstanceId, gravePrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: gravePrompt.PromptId,
            CardInstanceIds: [graveA.InstanceId, graveB.InstanceId])).Accepted);
        Assert.False(valhalla.Tapped);

        var lowPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(low.InstanceId, lowPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: lowPrompt.PromptId, Choice: low.InstanceId)).Accepted);
        var broadPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(low.InstanceId, broadPrompt.ValidChoices);
        Assert.Contains(broad.InstanceId, broadPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: broadPrompt.PromptId, Choice: broad.InstanceId)).Accepted);

        Assert.True(valhalla.Tapped);
        Assert.DoesNotContain(graveA, player.Graveyard);
        Assert.DoesNotContain(graveB, player.Graveyard);
        PassResponses(game);
        Assert.DoesNotContain(low, enemy.Field.SelectMany(row => row).Where(card => card is not null));
        Assert.DoesNotContain(broad, enemy.Field.SelectMany(row => row).Where(card => card is not null));
    }

    [Fact]
    public void ValkyrieDeclaresTwoCardsAndTheirDestinationsBeforePayingItsCosts()
    {
        var game = CreateWithFirstMaster("S01-03M1");
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var graveA = Card("S01-0309", "test-valkyrie-grave-a");
        var graveB = Card("S01-0311", "test-valkyrie-grave-b");
        var graveC = Card("S01-0312", "test-valkyrie-grave-c");
        player.Graveyard.AddRange([graveA, graveB, graveC]);
        var hpBefore = player.Hp;
        var activeMoraleBefore = player.Morale.Count(card => !card.Tapped);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "valkyrieRecover")).Accepted);
        var pairPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(graveC.InstanceId, pairPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: pairPrompt.PromptId,
            CardInstanceIds: [graveA.InstanceId, graveB.InstanceId])).Accepted);
        Assert.Equal(hpBefore, player.Hp);
        Assert.Equal(activeMoraleBefore, player.Morale.Count(card => !card.Tapped));

        var destinationPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(new[] { graveA.InstanceId, graveB.InstanceId }.Order(), destinationPrompt.ValidChoices.Where(id => id != "skip").Order());
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: destinationPrompt.PromptId, Choice: graveB.InstanceId)).Accepted);
        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.Equal(activeMoraleBefore - 1, player.Morale.Count(card => !card.Tapped));
        PassResponses(game);
        Assert.Contains(graveB, player.Hand);
        Assert.Equal(graveA, player.Library[^1]);
        Assert.Contains(graveC, player.Graveyard);
    }

    [Theory]
    [InlineData("S01-0303")]
    [InlineData("S01-0304")]
    [InlineData("S01-0308")]
    [InlineData("S01-0310")]
    [InlineData("S01-0314")]
    public void AsgardSelfDamageEntryDiscountIsAlwaysAnExplicitChoice(string cardId)
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var card = player.Hand.Concat(player.Library).FirstOrDefault(candidate => candidate.CardId == cardId) ?? Card(cardId, $"test-{cardId}");
        player.Hand.Remove(card); player.Library.Remove(card); player.Hand.Add(card);
        var hp = player.Hp;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-cost-choice", prompt.Continuation);
        Assert.Equal(["yes", "no"], prompt.ValidChoices);
        Assert.Null(player.Field[0][0]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);

        Assert.Equal(hp - 1, player.Hp);
        Assert.Equal(card.InstanceId, player.Field[0][0]?.InstanceId);
    }
}
