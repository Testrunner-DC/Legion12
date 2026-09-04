using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StarterMissingEffectsRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "starter-missing", "STMISSING", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.SpecialZones.Trials.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1, OwnerIndex = 0,
        };
    }

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source, string trigger,
        Dictionary<string, string>? data = null)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(game, [0, source, trigger, $"〈{source.Name}〉{trigger}效果", null,
            data ?? new Dictionary<string, string>()]);
    }

    [Fact]
    public void NewlyMigratedTiantingTriggersPayBeforeStackAndResolveTheirPrintedEffects()
    {
        var zhaoGame = Create(20713);
        var zhaoPlayer = zhaoGame.State.Players[0];
        var zhao = Card("ST01-01", "zhao");
        zhaoPlayer.Field[0][0] = zhao;
        zhaoPlayer.Morale.Add(new L12MoraleCard { CardId = "ST01-C1", InstanceId = "zhao-morale" });
        QueueTrigger(zhaoGame, zhao, "enter");
        Choose(zhaoGame, "mode:use");
        Choose(zhaoGame, "zhao-morale");
        Assert.Empty(zhaoPlayer.Morale);
        PassResponses(zhaoGame);
        Assert.True(zhao.HasCharge);
        DeclinePendingOptionalTriggers(zhaoGame);
        zhaoPlayer.Morale.Add(new L12MoraleCard { CardId = "ST01-C1", InstanceId = "zhao-piercing-morale" });
        QueueTrigger(zhaoGame, zhao, "after-attack", new Dictionary<string, string>
        {
            ["killed"] = "true",
            ["combatKillConfirmed"] = "true",
        });
        Choose(zhaoGame, "mode:use");
        Choose(zhaoGame, "zhao-piercing-morale");
        PassResponses(zhaoGame);
        Assert.NotNull(zhaoGame.State.PendingDefense);
        Assert.Equal("master", zhaoGame.State.PendingDefense!.Target.Type);
        Assert.True(zhaoGame.State.PendingDefense.SuppressAttackTriggers);

        var crossbowGame = Create(20714);
        var crossbowPlayer = crossbowGame.State.Players[0];
        var crossbow = Card("ST01-07", "crossbow");
        crossbow.Tapped = true;
        crossbowPlayer.Field[1][0] = crossbow;
        crossbowPlayer.Morale.Add(new L12MoraleCard { CardId = "ST01-C1", InstanceId = "crossbow-morale" });
        QueueTrigger(crossbowGame, crossbow, "after-attack");
        Choose(crossbowGame, "mode:use");
        Choose(crossbowGame, "crossbow-morale");
        PassResponses(crossbowGame);
        Assert.False(crossbow.Tapped);
        Assert.Contains($"trigger:starter-crossbow-ready:{crossbow.InstanceId}:{crossbowGame.State.TurnSerial}",
            crossbowPlayer.UsedAbilities);

        var wangGame = Create(20715);
        var wangPlayer = wangGame.State.Players[0];
        var wang = Card("ST01-09", "wang");
        var draw = Card("ST01-05", "wang-draw");
        wangPlayer.Field[1][0] = wang;
        wangPlayer.Library.Add(draw);
        wangPlayer.Morale.Add(new L12MoraleCard { CardId = "ST01-C1", InstanceId = "wang-morale" });
        QueueTrigger(wangGame, wang, "enter");
        Choose(wangGame, "mode:use");
        Choose(wangGame, "wang-morale");
        PassResponses(wangGame);
        Assert.Contains(draw, wangPlayer.Hand);
    }

    [Fact]
    public void NieYinniangAndKaneReachTheirSharedVerifiedRuntimeFlows()
    {
        var nieGame = Create(20716);
        var niePlayer = nieGame.State.Players[0];
        var nie = Card("ST01-08", "nie");
        niePlayer.Field[0][0] = nie;
        QueueTrigger(nieGame, nie, "enter");
        PassResponses(nieGame);
        Assert.Contains($"starter-taunt-disabled:{nieGame.State.TurnSerial}",
            nieGame.State.Players[1].UsedAbilities);

        var kaneGame = Create(20717);
        var kanePlayer = kaneGame.State.Players[0];
        var kane = Card("ST03-07", "kane");
        var top = Card("ST01-05", "kane-top");
        var next = Card("ST02-09", "kane-next");
        kanePlayer.Field[0][0] = kane;
        kanePlayer.Library.AddRange([top, next]);
        QueueTrigger(kaneGame, kane, "enter");
        Choose(kaneGame, "mode:use");
        PassResponses(kaneGame);
        Assert.Contains(top, kanePlayer.Graveyard);
        Assert.Contains(next, kanePlayer.Graveyard);

        var millOne = Card("ST01-05", "kane-one");
        kanePlayer.Library.Add(millOne);
        kane.Tapped = false;
        var active = kaneGame.Handle(0,
            new L12Command("activateAbility", kane.InstanceId, Ability: "kaneMillOne"));
        Assert.True(active.Accepted, active.Error);
        PassResponses(kaneGame);
        Assert.True(kane.Tapped);
        Assert.Contains(millOne, kanePlayer.Graveyard);
    }

    [Fact]
    public void AkhenatenDrawsForBothPlayersAndItsDeathCostIsPaidBeforeResponses()
    {
        var enterGame = Create(20718);
        var controller = enterGame.State.Players[0];
        var opponent = enterGame.State.Players[1];
        var akhenaten = Card("ST02-08", "akhenaten-enter");
        controller.Field[1][0] = akhenaten;
        var ownDraw = Card("ST01-05", "akhenaten-own-draw");
        var enemyDraw = Card("ST02-09", "akhenaten-enemy-draw");
        enemyDraw.OwnerIndex = 1;
        controller.Library.Add(ownDraw);
        opponent.Library.Add(enemyDraw);
        QueueTrigger(enterGame, akhenaten, "enter");
        PassResponses(enterGame);
        Assert.Contains(ownDraw, controller.Hand);
        Assert.Contains(enemyDraw, opponent.Hand);

        var deathGame = Create(20719);
        var deathPlayer = deathGame.State.Players[0];
        var dead = Card("ST02-08", "akhenaten-death");
        var discard = Card("ST01-05", "akhenaten-discard");
        deathPlayer.Hp = 7;
        deathPlayer.Graveyard.Add(dead);
        deathPlayer.Hand.Add(discard);
        QueueTrigger(deathGame, dead, "death");
        Choose(deathGame, "mode:use");
        Choose(deathGame, discard.InstanceId);
        Assert.Contains(discard, deathPlayer.Graveyard);
        PassResponses(deathGame);
        Assert.Equal(8, deathPlayer.Hp);
    }

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 24 && game.State.PendingPrompts.SingleOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void DeclinePendingOptionalTriggers(L12GameEngine game)
    {
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.SingleOrDefault() is { } prompt
            && prompt.ValidChoices.Contains("mode:none"); safety++)
            Choose(game, "mode:none");
    }

    [Fact]
    public void PenelopePaysOneGodPowerAndSummonsAnEligibleOlympusLegionFromHand()
    {
        var game = Create(20701);
        var player = game.State.Players[0];
        var penelope = Card("ST05-03", "penelope");
        var eligible = Card("ST05-05", "penelope-target");
        var ineligible = Card("ST05-02", "penelope-ineligible");
        player.Field[0][0] = penelope;
        player.Hand.AddRange([eligible, ineligible]);
        var power = new L12MoraleCard
        {
            CardId = "ST05-C1", InstanceId = "penelope-power", IsGodPower = true, Tapped = false,
        };
        player.Morale.Add(power);

        QueueTrigger(game, penelope, "enter");
        var decision = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(["mode:none", "mode:use"], decision.ValidChoices);
        Choose(game, "mode:use");
        var cardChoice = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(eligible.InstanceId, cardChoice.ValidChoices);
        Assert.DoesNotContain(ineligible.InstanceId, cardChoice.ValidChoices);
        Choose(game, eligible.InstanceId);
        Choose(game, "0:1");

        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        PassResponses(game);
        Assert.Same(eligible, player.Field[0][1]);
        Assert.False(eligible.Tapped);
        Assert.DoesNotContain(eligible, player.Hand);
    }

    [Fact]
    public void TelemachusShowsAllTopCardsSelectsOnlyEligibleAndReturnsTheRestToOneEnd()
    {
        var game = Create(20702);
        var player = game.State.Players[0];
        var telemachus = Card("ST05-06", "telemachus");
        var ranged = Card("ST05-03", "telemachus-ranged");
        var invalid = Card("ST05-02", "telemachus-invalid");
        var tactic = Card("ST05-10", "telemachus-tactic");
        player.Field[0][0] = telemachus;
        player.Library.AddRange([ranged, invalid, tactic]);

        var begin = game.Handle(0,
            new L12Command("activateAbility", telemachus.InstanceId, Ability: "telemachusTopThree"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, "mode:use");
        Assert.True(telemachus.Tapped);
        PassResponses(game);

        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("starter-telemachus-pick", choice.Data["action"]);
        Assert.Equal(1, choice.MinChoose);
        Assert.Equal(1, choice.MaxChoose);
        Assert.Equal(string.Join('|', ranged.InstanceId, invalid.InstanceId, tactic.InstanceId),
            choice.Data["displayCardIds"]);
        Assert.Contains(ranged.InstanceId, choice.ValidChoices);
        Assert.Contains(tactic.InstanceId, choice.ValidChoices);
        Assert.DoesNotContain(invalid.InstanceId, choice.ValidChoices);
        var choiceSnapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var choiceData = choiceSnapshot.GetProperty("prompts")[0].GetProperty("data");
        Assert.Equal("single-row", choiceData.GetProperty("layout").GetString());
        Assert.Equal("true", choiceData.GetProperty("cardSelection").GetString());
        Assert.Equal(invalid.CardId, choiceData.GetProperty($"{invalid.InstanceId}:cardId").GetString());
        Assert.Equal(invalid.Name, choiceData.GetProperty($"{invalid.InstanceId}:name").GetString());
        Assert.Equal("牌库", choiceData.GetProperty($"{invalid.InstanceId}:zone").GetString());
        Choose(game, tactic.InstanceId);
        Assert.Contains(tactic, player.Hand);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-top-bottom", order.Data["placementMode"]);
        var orderSnapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var orderData = orderSnapshot.GetProperty("prompts")[0].GetProperty("data");
        Assert.Equal("single-row", orderData.GetProperty("layout").GetString());
        Assert.Equal("true", orderData.GetProperty("cardSelection").GetString());
        Assert.Equal(invalid.CardId, orderData.GetProperty($"{invalid.InstanceId}:cardId").GetString());
        Assert.Equal(ranged.CardId, orderData.GetProperty($"{ranged.InstanceId}:cardId").GetString());
        var result = game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: [invalid.InstanceId, ranged.InstanceId]));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal([invalid.InstanceId, ranged.InstanceId], player.Library.Select(card => card.InstanceId));

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var ability = snapshot.GetProperty("players")[0].GetProperty("field")[0][0]
            .GetProperty("abilities").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == "telemachusTopThree");
        Assert.False(ability.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void HiddenPassReturnsChosenMoraleBeforeStackAndSummonsDeclaredTiantingLegion()
    {
        var game = Create(20703);
        var player = game.State.Players[0];
        var hiddenPass = Card("ST01-10", "hidden-pass");
        hiddenPass.Hidden = true;
        player.Field[1][0] = hiddenPass;
        var entrant = Card("ST01-05", "hidden-pass-entrant");
        player.Hand.Add(entrant);
        var morale = new L12MoraleCard { CardId = "ST01-C1", InstanceId = "hidden-pass-morale" };
        player.Morale.Add(morale);

        QueueTrigger(game, hiddenPass, "reaction");
        Choose(game, "mode:use");
        Choose(game, morale.InstanceId);
        Choose(game, entrant.InstanceId);
        Choose(game, "0:0");

        Assert.DoesNotContain(morale, player.Morale);
        Assert.Contains(morale, player.MoraleDeck);
        Assert.DoesNotContain(hiddenPass, player.Field.SelectMany(row => row));
        Assert.Contains(hiddenPass, player.Resolving);

        PassResponses(game);
        Assert.Same(entrant, player.Field[0][0]);
        Assert.False(entrant.Tapped);
        Assert.Contains(hiddenPass, player.Graveyard);
    }

    [Fact]
    public void OasisDancerRestsAndBuffsEveryTaiyangchengLegionForTheTurn()
    {
        var game = Create(20704);
        var player = game.State.Players[0];
        var dancer = Card("ST02-05", "dancer");
        var ally = Card("ST02-09", "dancer-ally");
        player.Field[0][0] = dancer;
        player.Field[0][1] = ally;

        var result = game.Handle(0,
            new L12Command("activateAbility", dancer.InstanceId, Ability: "oasisDancerBuff"));
        Assert.True(result.Accepted, result.Error);
        Assert.True(dancer.Tapped);
        PassResponses(game);

        Assert.Equal(Catalog.Cards["ST02-05"].Troops!.Value + 1000, dancer.Troops);
        Assert.Equal(Catalog.Cards["ST02-09"].Troops!.Value + 1000, ally.Troops);
    }

    [Fact]
    public void ChristinaReplacesOnlyTheNextEligibleActiveTacticCostWithOneMasterDamage()
    {
        var game = Create(20705);
        var player = game.State.Players[0];
        var christina = Card("ST03-05", "christina");
        var tactic = Card("S01-0008", "christina-tactic");
        player.Field[0][0] = christina;
        player.Hand.Add(tactic);
        player.Hp = 10;

        var activate = game.Handle(0,
            new L12Command("activateAbility", christina.InstanceId, Ability: "christinaFreeTactic"));
        Assert.True(activate.Accepted, activate.Error);
        PassResponses(game);
        Assert.Contains($"starter-christina-free-tactic:{game.State.TurnSerial}", player.UsedAbilities);

        var play = game.Handle(0, new L12Command("playCard", tactic.InstanceId));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        Assert.Equal(9, player.Hp);
        Assert.DoesNotContain($"starter-christina-free-tactic:{game.State.TurnSerial}", player.UsedAbilities);
    }

    [Fact]
    public void OiranUsesOneSharedTargetFlowAndAppliesBothTimedTroopChanges()
    {
        var game = Create(20706);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var oiran = Card("ST04-06", "oiran");
        var ally = Card("ST04-08", "oiran-ally");
        var enemy = Card("ST01-05", "oiran-enemy");
        player.Field[0][0] = oiran;
        player.Field[0][1] = ally;
        opponent.Field[0][0] = enemy;

        var begin = game.Handle(0,
            new L12Command("activateAbility", oiran.InstanceId, Ability: "oiranTransfer"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, enemy.InstanceId);
        Choose(game, ally.InstanceId);
        Assert.True(oiran.Tapped);
        PassResponses(game);

        Assert.Equal(enemy.BaseTroops - 1000, enemy.Troops);
        Assert.Equal(ally.BaseTroops + 1000, ally.Troops);
    }

    [Fact]
    public void LightSwordEntryAndActiveBranchesShareDeclaredTargetsAndCosts()
    {
        var entryGame = Create(20707);
        var entryPlayer = entryGame.State.Players[0];
        var entryOpponent = entryGame.State.Players[1];
        var lightSword = Card("ST06-09", "light-sword-entry");
        var low = Card("ST02-07", "light-sword-low");
        low.OwnerIndex = 1;
        entryPlayer.Relic = lightSword;
        entryOpponent.Field[0][0] = low;

        QueueTrigger(entryGame, lightSword, "enter");
        Choose(entryGame, "mode:use");
        Choose(entryGame, low.InstanceId);
        PassResponses(entryGame);
        Assert.Null(entryOpponent.Field[0][0]);
        Assert.Contains(low, entryOpponent.Graveyard);

        var activeGame = Create(20708);
        var activePlayer = activeGame.State.Players[0];
        var activeSword = Card("ST06-09", "light-sword-active");
        var discard = Card("ST01-05", "light-sword-discard");
        var buffTarget = Card("ST06-02", "light-sword-buff-target");
        activePlayer.Relic = activeSword;
        activePlayer.Field[0][0] = buffTarget;
        activePlayer.Hand.Add(discard);
        var activate = activeGame.Handle(0,
            new L12Command("activateAbility", activeSword.InstanceId, Ability: "lightSwordActive"));
        Assert.True(activate.Accepted, activate.Error);
        var option = Assert.Single(activeGame.State.PendingPrompts);
        Assert.Equal("主动休整 弃置1张手牌：获得1符文。", option.ChoiceLabels["mode:rune"]);
        Assert.Equal("主动休整 弃置1张手牌：选择我方前排1张【彼界】军团，本回合兵力+2000。",
            option.ChoiceLabels["mode:buff"]);
        Choose(activeGame, "mode:rune");
        Choose(activeGame, discard.InstanceId);
        Assert.True(activeSword.Tapped);
        Assert.Contains(discard, activePlayer.Graveyard);
        PassResponses(activeGame);
        Assert.Equal(1, activePlayer.SpecialZones.Runes);
    }

    [Fact]
    public void MedeaConditionAndMedusaAttackUseCurrentBoardAndRealCombatModifiers()
    {
        var medeaGame = Create(20709);
        var medeaPlayer = medeaGame.State.Players[0];
        var medeaOpponent = medeaGame.State.Players[1];
        var medea = Card("ST05-04", "medea");
        medeaPlayer.Field[0][0] = medea;
        medeaOpponent.Field[0][0] = Card("ST01-01", "medea-opponent");
        medeaPlayer.Library.Add(Card("ST01-05", "medea-draw-1"));
        medeaPlayer.Library.Add(Card("ST02-09", "medea-draw-2"));

        QueueTrigger(medeaGame, medea, "enter");
        Choose(medeaGame, "mode:use");
        PassResponses(medeaGame);
        Assert.Equal(2, medeaPlayer.Hand.Count);

        var medusaGame = Create(20710);
        var medusaPlayer = medusaGame.State.Players[0];
        var medusa = Card("ST05-09", "medusa");
        medusaPlayer.Field[0][0] = medusa;
        QueueTrigger(medusaGame, medusa, "attack");
        PassResponses(medusaGame);
        Assert.True(medusa.HasShock);
        Assert.Equal(medusa.BaseTroops + 1000, medusa.Troops);
    }

    [Fact]
    public void StarterTriggeredDisastersResolveForBothBoardsWithoutDeathTriggers()
    {
        var mountainGame = Create(20711);
        var lowFront = Card("ST01-05", "mountain-low");
        var highFront = Card("ST01-01", "mountain-high");
        highFront.Troops = 5000;
        mountainGame.State.Players[0].Field[0][0] = lowFront;
        mountainGame.State.Players[1].Field[0][0] = highFront;
        var mountain = Card("ST-DS01", "mountain");
        mountainGame.State.ActiveDisaster = mountain;
        var mountainItem = new L12StackItem
        {
            StackItemId = "mountain-stack", Controller = 0, SourceInstanceId = mountain.InstanceId,
            SourceCardId = mountain.CardId, SourceName = mountain.Name, Trigger = "disaster", Text = mountain.EffectText ?? string.Empty,
        };
        mountainGame.State.EffectStack.Add(mountainItem);
        typeof(L12GameEngine).GetMethod("ResolveDisasterEffect", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(mountainGame, [mountainItem]);
        Assert.Contains(lowFront, mountainGame.State.Players[0].Graveyard);
        Assert.Same(highFront, mountainGame.State.Players[1].Field[0][0]);
        Assert.Empty(mountainGame.State.PendingTriggerBatches);

        var eyeGame = Create(20712);
        var first = Card("ST01-05", "eye-first");
        var second = Card("ST02-09", "eye-second");
        second.OwnerIndex = 1;
        eyeGame.State.Players[0].Field[0][0] = first;
        eyeGame.State.Players[1].Field[0][0] = second;
        var eye = Card("ST-DS03", "evil-eye");
        eyeGame.State.ActiveDisaster = eye;
        var eyeItem = new L12StackItem
        {
            StackItemId = "eye-stack", Controller = 0, SourceInstanceId = eye.InstanceId,
            SourceCardId = eye.CardId, SourceName = eye.Name, Trigger = "disaster", Text = eye.EffectText ?? string.Empty,
        };
        eyeGame.State.EffectStack.Add(eyeItem);
        typeof(L12GameEngine).GetMethod("ResolveDisasterEffect", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(eyeGame, [eyeItem]);
        Assert.Equal(2, eyeGame.State.PendingPrompts.Count);
        Assert.All(eyeGame.State.PendingPrompts, prompt =>
        {
            Assert.True(prompt.IsPrivate);
            Assert.Equal("post-hidden-reveal", prompt.Data["declarationTiming"]);
        });
        var firstPrompt = eyeGame.State.PendingPrompts.Single(prompt => prompt.PlayerIndex == 0);
        Assert.True(eyeGame.Handle(0, new L12Command("resolvePrompt", PromptId: firstPrompt.PromptId,
            Choice: first.InstanceId)).Accepted);
        Assert.Same(first, eyeGame.State.Players[0].Field[0][0]);
        var secondPrompt = eyeGame.State.PendingPrompts.Single(prompt => prompt.PlayerIndex == 1);
        Assert.True(eyeGame.Handle(1, new L12Command("resolvePrompt", PromptId: secondPrompt.PromptId,
            Choice: second.InstanceId)).Accepted);
        Assert.Contains(first, eyeGame.State.Players[0].Graveyard);
        Assert.Contains(second, eyeGame.State.Players[1].Graveyard);
        Assert.Empty(eyeGame.State.PendingTriggerBatches);
    }
}
