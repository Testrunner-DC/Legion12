using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S2UniversalEffectsTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 6201)
        => new(Catalog, "s2-effects", "S2TEST", seed, ["甲", "乙"], [4, 4], skipPreparation: true);

    private static L12GameEngine CreateTianting(int seed)
        => new(Catalog, "s2-tianting", "S2TT", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12CardInstance TakeCard(L12GameEngine game, int playerIndex, string cardId)
    {
        var player = game.State.Players[playerIndex];
        var card = player.Hand.Concat(player.Library).FirstOrDefault(candidate => candidate.CardId == cardId)
            ?? Instance(cardId, $"test-{playerIndex}-{cardId}-{Guid.NewGuid():N}");
        player.Hand.Remove(card);
        player.Library.Remove(card);
        player.Hand.Add(card);
        return card;
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static L12CardInstance SetCounter(L12GameEngine game, int playerIndex, string cardId, int slot = 0)
    {
        var card = TakeCard(game, playerIndex, cardId);
        var player = game.State.Players[playerIndex];
        player.Hand.Remove(card);
        card.Hidden = true;
        card.SetRound = 0;
        player.Field[1][slot] = card;
        return card;
    }

    private static L12CardInstance Instance(string cardId, string instanceId)
    {
        var card = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = card.Id,
            Name = card.NameZh,
            CardType = card.CardType,
            Faction = card.Faction,
            ImageUrl = card.ImageUrl,
            Cost = card.Cost ?? 0,
            EffectText = card.Effect,
            BaseTroops = card.Troops ?? 0,
            Troops = card.Troops ?? 0,
            DisasterLevel = card.DisasterLevel ?? 0,
        };
    }

    [Fact]
    public void ExorcistRaisesTheOpponentsNextActiveTacticCost()
    {
        var game = Create();
        var owner = 0;
        var enemy = 1;
        var exorcist = TakeCard(game, owner, "S02-0001");
        AddMorale(game.State.Players[owner], 3);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(owner, new L12Command("playCard", exorcist.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal(1, game.State.Players[enemy].NextActiveTacticSurcharge);

        var idea = TakeCard(game, enemy, "S02-0014");
        AddMorale(game.State.Players[enemy], 3);
        game.State.ActivePlayer = enemy;
        game.State.Phase = L12Phase.Main;
        Assert.False(game.Handle(enemy, new L12Command("playCard", idea.InstanceId)).Accepted);

        AddMorale(game.State.Players[enemy], 4);
        Assert.True(game.Handle(enemy, new L12Command("playCard", idea.InstanceId)).Accepted);
    }

    [Fact]
    public void ExorcistMayReturnAfterAnOpposingTacticFinishesResolving()
    {
        var game = Create(seed: 6209);
        var tacticPlayer = game.State.Players[0];
        var exorcistOwner = game.State.Players[1];
        var tactic = TakeCard(game, 0, "S02-0014");
        var exorcist = TakeCard(game, 1, "S02-0001");
        exorcistOwner.Hand.Remove(exorcist);
        exorcist.SummonRound = 0;
        exorcistOwner.Field[0][0] = exorcist;
        AddMorale(tacticPlayer, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-exorcist-return", prompt.Data["action"]);
        Assert.Equal(1, prompt.PlayerIndex);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);

        Assert.Null(exorcistOwner.Field[0][0]);
        Assert.Contains(exorcist, exorcistOwner.Hand);
    }

    [Fact]
    public void CourtMagicianCanRestToSealAllCounterTactics()
    {
        var game = Create(seed: 6202);
        var magician = TakeCard(game, 0, "S02-0003");
        AddMorale(game.State.Players[0], 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        Assert.True(game.Handle(0, new L12Command("playCard", magician.InstanceId, Row: 0, Slot: 0)).Accepted);

        Assert.True(game.Handle(0, new L12Command("activateAbility", magician.InstanceId, Ability: "disableCounters")).Accepted);
        Assert.True(magician.Tapped);
        Assert.Equal(game.State.TurnSerial + 2, game.State.CounterTacticsDisabledUntilTurnSerial);
    }

    [Fact]
    public void MagiciansPuppetMayRestFromHandAndRetargetAMasterAttack()
    {
        var game = Create(seed: 6215);
        var attacker = Instance("S02-0003", "puppet-test-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var puppet = TakeCard(game, 1, "S02-0005");
        var support = Instance("S02-0003", "puppet-test-support");
        support.SummonRound = 0;
        game.State.Players[1].Field[1][1] = support;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var responsePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, responsePrompt.PlayerIndex);
        Assert.Contains(puppet.InstanceId, responsePrompt.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId,
            Choice: puppet.InstanceId)).Accepted);

        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-puppet-slot", slotPrompt.Continuation);
        Assert.Contains("cancel", slotPrompt.ValidChoices);
        Assert.All(slotPrompt.ValidChoices.Where(choice => choice != "cancel"),
            choice => Assert.StartsWith("0:", choice));
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:1")).Accepted);

        Assert.Same(puppet, game.State.Players[1].Field[0][1]);
        Assert.True(puppet.Tapped);
        Assert.DoesNotContain(puppet, game.State.Players[1].Hand);
        Assert.Equal("legion", game.State.PendingDefense?.Target.Type);
        Assert.Equal(puppet.InstanceId, game.State.PendingDefense?.Target.InstanceId);
        Assert.Equal(L12Phase.Defense, game.State.Phase);
    }

    [Fact]
    public void MagiciansPuppetMayCancelBeforeCommittingAndReturnsToTheSameResponseWindow()
    {
        var game = Create(seed: 62151);
        var attacker = Instance("S02-0003", "puppet-cancel-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var puppet = TakeCard(game, 1, "S02-0005");
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var responsePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId,
            Choice: puppet.InstanceId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "cancel")).Accepted);

        var resumedResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", resumedResponse.Kind);
        Assert.Equal(1, resumedResponse.PlayerIndex);
        Assert.Contains(puppet.InstanceId, resumedResponse.ValidChoices);
        Assert.Contains(puppet, game.State.Players[1].Hand);
        Assert.False(puppet.Tapped);
        Assert.Equal("master", game.State.PendingDefense?.Target.Type);
    }

    [Fact]
    public void MagiciansPuppetIsNotOfferedWithoutAnEmptyFrontSlot()
    {
        var game = Create(seed: 6216);
        var attacker = Instance("S02-0003", "puppet-full-front-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        TakeCard(game, 1, "S02-0005");
        for (var slot = 0; slot < 3; slot++)
            game.State.Players[1].Field[0][slot] = Instance("S02-0003", $"front-occupant-{slot}");
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(L12Phase.Defense, game.State.Phase);
        Assert.Equal("master", game.State.PendingDefense?.Target.Type);
    }

    [Fact]
    public void NegatedMagiciansPuppetRemainsInHandAndDoesNotRetarget()
    {
        var game = Create(seed: 6217);
        var attacker = Instance("S02-0003", "puppet-negate-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var negate = Instance("S01-0016", "puppet-negate-counter");
        negate.Hidden = true;
        negate.SetRound = 0;
        game.State.Players[0].Field[1][0] = negate;
        game.State.Players[0].Hand.Add(Instance("S02-0007", "puppet-negate-discard"));
        var puppet = TakeCard(game, 1, "S02-0005");
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var puppetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: puppetPrompt.PromptId,
            Choice: puppet.InstanceId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:1")).Accepted);

        var negatePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, negatePrompt.PlayerIndex);
        Assert.Contains(negate.InstanceId, negatePrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: negatePrompt.PromptId,
            Choice: negate.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: "puppet-negate-discard")).Accepted);

        Assert.Contains(puppet, game.State.Players[1].Hand);
        Assert.Null(game.State.Players[1].Field[0][1]);
        Assert.Equal("master", game.State.PendingDefense?.Target.Type);
        Assert.Equal(L12Phase.Defense, game.State.Phase);
    }

    [Fact]
    public void LandlordsCoercionInvalidatesBlockWhenDefenderDeclinesExtraDiscard()
    {
        var game = Create(seed: 6220);
        var attacker = Instance("S02-0004", "coercion-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var counter = SetCounter(game, 0, "S02-0015");
        var blocker = Instance("S02-0004", "coercion-blocker");
        game.State.Players[1].Hand.Add(blocker);
        var hpBefore = game.State.Players[1].Hp;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [blocker.InstanceId])).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: counter.InstanceId)).Accepted);
        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-landlord-extra-discard", discard.Data["action"]);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: discard.PromptId, Choice: "decline")).Accepted);

        Assert.Equal(hpBefore - 1, game.State.Players[1].Hp);
        Assert.Contains(blocker, game.State.Players[1].Hand);
        Assert.Contains(counter, game.State.Players[0].Graveyard);
    }

    [Fact]
    public void BothPlayersCanRespondAtTheOriginalAttackTimingAfterAReactionIsStacked()
    {
        var game = Create(seed: 6225);
        var attacker = Instance("S02-0004", "response-chain-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var firstAmbush = SetCounter(game, 0, "S01-0019");
        var secondAmbush = SetCounter(game, 1, "S01-0019");
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);

        var defenderResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(secondAmbush.InstanceId, defenderResponse.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: defenderResponse.PromptId,
            Choice: secondAmbush.InstanceId)).Accepted);

        var attackerResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, attackerResponse.PlayerIndex);
        Assert.Contains(firstAmbush.InstanceId, attackerResponse.ValidChoices);
    }

    [Fact]
    public void SamePlayerCanUseTwoS2CountersAtTheSameOriginalAuthorityTiming()
    {
        var game = Create(seed: 6226);
        var attacker = Instance("S02-0004", "s2-chain-attacker");
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var first = SetCounter(game, 0, "S02-0015", slot: 0);
        var second = SetCounter(game, 0, "S02-0015", slot: 1);
        var blocker = Instance("S02-0004", "s2-chain-blocker");
        game.State.Players[1].Hand.Add(blocker);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [blocker.InstanceId])).Accepted);

        var firstResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(first.InstanceId, firstResponse.ValidChoices);
        Assert.Contains(second.InstanceId, firstResponse.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: firstResponse.PromptId,
            Choice: first.InstanceId)).Accepted);

        // 对方无合法响应时会被公共响应框架自动让过，优先权直接回到原玩家。
        var secondResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, secondResponse.PlayerIndex);
        Assert.Contains(second.InstanceId, secondResponse.ValidChoices);
    }

    [Fact]
    public void S2CounterTacticCanBeSetNormallyInABackLineSlot()
    {
        var game = Create(seed: 6227);
        var counter = TakeCard(game, 0, "S02-0017");
        AddMorale(game.State.Players[0], 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("playCard", counter.InstanceId, Row: 1, Slot: 1));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(counter, game.State.Players[0].Field[1][1]);
        Assert.True(counter.Hidden);
        Assert.DoesNotContain(counter, game.State.Players[0].Hand);
    }

    [Fact]
    public void RuinedRitualSuppressesADeckSummonedLegionsEnterEffectAndReducesTroops()
    {
        var game = new L12GameEngine(Catalog, "s2-ruin", "S2RUIN", 6221, ["甲", "乙"], [0, 4], skipPreparation: true);
        var liJing = TakeCard(game, 0, "S01-0103");
        var summoned = game.State.Players[0].Hand.Concat(game.State.Players[0].Library)
            .First(card => card.CardId == "S01-0105");
        game.State.Players[0].Hand.Remove(summoned);
        game.State.Players[0].Library.Remove(summoned);
        game.State.Players[0].Library.Insert(0, summoned);
        var counter = SetCounter(game, 1, "S02-0016");
        AddMorale(game.State.Players[0], 8);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", liJing.InstanceId, Row: 0, Slot: 0)).Accepted);
        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("lijing-choice", choice.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choice.PromptId, Choice: "recruit")).Accepted);
        var returnPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", returnPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: returnPrompt.PromptId,
            CardInstanceIds: [returnPrompt.ValidChoices[0]])).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("lijing-slot", slot.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:1")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: counter.InstanceId)).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-ruin-mode", mode.Data["action"]);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "suppress")).Accepted);

        Assert.Null(game.State.Players[0].Field[0][1]);
        Assert.Contains(summoned, game.State.Players[0].Graveyard);
        Assert.Equal(summoned.BaseTroops, summoned.Troops);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == summoned.InstanceId && item.Trigger == "enter");
    }

    [Fact]
    public void SupplyPlunderReturnsAnOpposingHandCardToTopAndDraws()
    {
        var game = Create(seed: 6222);
        var idea = TakeCard(game, 0, "S02-0014");
        while (game.State.Players[0].Hand.Count > 4)
        {
            var extra = game.State.Players[0].Hand.First(card => card.InstanceId != idea.InstanceId);
            game.State.Players[0].Hand.Remove(extra);
            game.State.Players[0].Library.Add(extra);
        }
        var counter = SetCounter(game, 1, "S02-0017");
        var enemyHandBefore = game.State.Players[1].Hand.Count;
        AddMorale(game.State.Players[0], 3);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", idea.InstanceId)).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: counter.InstanceId)).Accepted);
        var select = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-plunder-return", select.Data["action"]);
        Assert.All(select.ValidChoices, choice =>
        {
            Assert.StartsWith("对方手牌 ", select.Data[choice]);
            Assert.Equal("/assets/l12/card-back-official.png", select.Data[$"{choice}:image"]);
            Assert.DoesNotContain(game.State.Players[0].Hand.Select(card => card.Name),
                name => select.Data[choice].Contains(name, StringComparison.Ordinal));
        });
        var returnedId = select.ValidChoices[0];
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: select.PromptId, Choice: returnedId)).Accepted);

        Assert.Equal(returnedId, game.State.Players[0].Library[0].InstanceId);
        Assert.Equal(enemyHandBefore + 1, game.State.Players[1].Hand.Count);
    }

    [Fact]
    public void PoisonActivationNegatesAnEffectReadyAndForcesDiscard()
    {
        var game = new L12GameEngine(Catalog, "s2-poison", "S2POISON", 6223, ["甲", "乙"], [0, 4], skipPreparation: true);
        var attacker = TakeCard(game, 0, "S01-0101");
        game.State.Players[0].Hand.Remove(attacker);
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        var poison = SetCounter(game, 1, "S02-0018");
        AddMorale(game.State.Players[0], 8);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        var ready = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("lubu-ready", ready.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ready.PromptId, Choice: "yes")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(poison.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: poison.InstanceId)).Accepted);
        var discard = Assert.Single(game.State.PendingPrompts);
        var discardedId = discard.ValidChoices[0];
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discard.PromptId, Choice: discardedId)).Accepted);

        Assert.True(attacker.Tapped);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == discardedId);
    }

    [Fact]
    public void ChaoticArrowsSelectsAndKillsUpToThreePrintedLowTroopLegions()
    {
        var game = Create(seed: 6203);
        var tactic = TakeCard(game, 0, "S02-0011");
        AddMorale(game.State.Players[0], 3);
        game.State.Players[1].Field[0][0] = Instance("S02-0005", "target-a");
        game.State.Players[1].Field[0][1] = Instance("S02-0007", "target-b");
        game.State.Players[1].Field[1][2] = Instance("S01-0003", "target-c");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-chaotic-arrows", prompt.Data["action"]);
        Assert.Equal(3, prompt.ValidChoices.Count);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: prompt.ValidChoices)).Accepted);
        Assert.All(game.State.Players[1].Field.SelectMany(row => row), card => Assert.Null(card));
        Assert.Equal(3, game.State.Players[1].Graveyard.Count);
    }

    [Theory]
    [InlineData("S02-0004", 5000)]
    [InlineData("S02-0007", 2000)]
    public void FrontDefendersGainTroopsForTheWholeOpposingTurnWithoutStacking(string cardId, int printedTroops)
    {
        var game = Create(seed: 6204);
        var defender = game.State.Players[1];
        var target = Instance(cardId, $"front-{cardId}");
        defender.Field[0][0] = target;

        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.SnapshotFor(0);
        Assert.Equal(printedTroops + 1000, target.Troops);

        game.SnapshotFor(0);
        Assert.Equal(printedTroops + 1000, target.Troops);

        game.State.ActivePlayer = 1;
        game.SnapshotFor(1);
        Assert.Equal(printedTroops, target.Troops);
    }

    [Theory]
    [InlineData("S01-0107", 1000)]
    [InlineData("S01-0212", 1000)]
    [InlineData("S01-0312", 1000)]
    [InlineData("S02-0004", 1000)]
    [InlineData("S02-0007", 1000)]
    [InlineData("S02-0519", 2000)]
    [InlineData("S02-0615", 1000)]
    public void OpponentTurnStaticTroopsRemainActiveOutsideCombat(string cardId, int bonus)
    {
        var game = Create(seed: 62041);
        var owner = game.State.Players[1];
        var target = Instance(cardId, $"opponent-turn-{cardId}");
        owner.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        game.SnapshotFor(0);
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(target.BaseTroops + bonus, target.Troops);
        if (cardId == "S01-0212") Assert.Equal(target.Cost + 1, target.CurrentCost);

        game.SnapshotFor(1);
        Assert.Equal(target.BaseTroops + bonus, target.Troops);

        game.State.ActivePlayer = 1;
        game.SnapshotFor(1);
        Assert.Equal(target.BaseTroops, target.Troops);
        if (cardId == "S01-0212") Assert.Equal(target.Cost, target.CurrentCost);
    }

    [Fact]
    public void MenesOpponentTurnBonusTracksWhetherA_tombGuardExists()
    {
        var game = Create(seed: 62042);
        var owner = game.State.Players[1];
        var menes = Instance("S01-0203", "menes-static");
        owner.Field[0][0] = menes;
        game.State.ActivePlayer = 0;

        game.SnapshotFor(0);
        Assert.Equal(menes.BaseTroops + 1000, menes.Troops);

        owner.Field[1][0] = Instance("S01-0212", "tomb-guard-static");
        game.SnapshotFor(0);
        Assert.Equal(menes.BaseTroops, menes.Troops);
    }

    [Fact]
    public void HannibalContinuouslyBuffsOnlyAdjacentLegions()
    {
        var game = Create(seed: 62043);
        var owner = game.State.Players[0];
        var left = Instance("S02-0004", "hannibal-left");
        var hannibal = Instance("S02-0516", "hannibal-source");
        var right = Instance("S02-0004", "hannibal-right");
        owner.Field[0][0] = left;
        owner.Field[0][1] = hannibal;
        owner.Field[0][2] = right;
        game.State.ActivePlayer = owner.PlayerIndex;

        game.SnapshotFor(0);
        Assert.Equal(left.BaseTroops + 1000, left.Troops);
        Assert.Equal(hannibal.BaseTroops, hannibal.Troops);
        Assert.Equal(right.BaseTroops + 1000, right.Troops);
    }

    [Fact]
    public void RingDiscardsThenSearchesAUniversalCardAndShuffles()
    {
        var game = Create(seed: 6205);
        var player = game.State.Players[0];
        var ring = TakeCard(game, 0, "S02-0008");
        AddMorale(player, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", ring.InstanceId)).Accepted);
        var optionalPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-ring-start", optionalPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optionalPrompt.PromptId,
            Choice: "yes")).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-ring-discard", discardPrompt.Data["action"]);
        var discardedId = discardPrompt.ValidChoices[0];
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            CardInstanceIds: [discardedId])).Accepted);

        var searchPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-ring-search", searchPrompt.Data["action"]);
        var searchedId = searchPrompt.ValidChoices[0];
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: searchPrompt.PromptId,
            CardInstanceIds: [searchedId])).Accepted);

        Assert.Contains(player.Graveyard, card => card.InstanceId == discardedId);
        Assert.Contains(player.Hand, card => card.InstanceId == searchedId && card.Faction == "universal");
        Assert.Same(ring, player.Relic);
    }

    [Fact]
    public void RingMayDeclineItsOptionalEntryEffectWithoutDiscarding()
    {
        var game = Create(seed: 62051);
        var player = game.State.Players[0];
        var ring = TakeCard(game, 0, "S02-0008");
        AddMorale(player, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        var handIds = player.Hand.Where(card => card.InstanceId != ring.InstanceId)
            .Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("playCard", ring.InstanceId)).Accepted);
        var optionalPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optionalPrompt.PromptId,
            Choice: "no")).Accepted);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(handIds, player.Hand.Select(card => card.InstanceId));
        Assert.Same(ring, player.Relic);
    }

    [Fact]
    public void BlackLotusAdjustsDisasterAndMayBecomeTappedMorale()
    {
        var game = Create(seed: 6212);
        var player = game.State.Players[0];
        var lotus = TakeCard(game, 0, "S02-0010");
        AddMorale(player, 4);
        game.State.DisasterValue = 4;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", lotus.InstanceId)).Accepted);
        var disasterPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-black-lotus-disaster", disasterPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: disasterPrompt.PromptId, Choice: "1")).Accepted);

        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-black-lotus-morale", moralePrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: moralePrompt.PromptId, Choice: "yes")).Accepted);

        Assert.Equal(5, game.State.DisasterValue);
        var converted = Assert.Single(player.Morale, card => card.CardId == "S02-0010");
        Assert.True(converted.Tapped);
        Assert.DoesNotContain(player.Graveyard, card => card.InstanceId == lotus.InstanceId);
    }

    [Fact]
    public void ReturnedBlackLotusGoesToGraveyardInsteadOfMoraleDeck()
    {
        var game = CreateTianting(seed: 6213);
        var player = game.State.Players[0];
        var lotus = Instance("S02-0010", "lotus-return-test");
        var qianyang = Instance("S02-0105", "qianyang-return-test");
        var target = Instance("S02-0003", "lotus-return-target");
        player.Hand.Add(lotus);
        player.Hand.Add(qianyang);
        game.State.Players[1].Field[0][0] = target;
        AddMorale(player, 8);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", lotus.InstanceId)).Accepted);
        var disasterPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: disasterPrompt.PromptId, Choice: "0")).Accepted);
        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: moralePrompt.PromptId, Choice: "yes")).Accepted);
        var converted = Assert.Single(player.Morale, card => card.CardId == "S02-0010");
        player.Morale.Remove(converted);
        player.Morale.Insert(0, converted);

        Assert.True(game.Handle(0, new L12Command("playCard", qianyang.InstanceId)).Accepted);
        var killPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: killPrompt.PromptId,
            CardInstanceIds: [target.InstanceId])).Accepted);
        var drawPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: drawPrompt.PromptId, Choice: "yes")).Accepted);
        var returnPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", returnPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: returnPrompt.PromptId,
            CardInstanceIds: [converted.InstanceId])).Accepted);

        Assert.Contains(player.Graveyard, card => card.InstanceId == lotus.InstanceId && card.CardId == "S02-0010");
        Assert.DoesNotContain(player.MoraleDeck, card => card.InstanceId == lotus.InstanceId);
    }

    [Fact]
    public void DefenseDeploymentSetsUpToTwoCounterTacticsWithoutTheirNormalSetCost()
    {
        var game = Create(seed: 6206);
        var player = game.State.Players[0];
        var deployment = TakeCard(game, 0, "S02-0009");
        var firstCounter = TakeCard(game, 0, "S02-0015");
        var secondCounter = TakeCard(game, 0, "S02-0016");
        AddMorale(player, 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", deployment.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-defense-deployment", prompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [firstCounter.InstanceId, secondCounter.InstanceId])).Accepted);

        var covered = player.Field[1].Where(card => card is not null).Cast<L12CardInstance>().ToArray();
        Assert.Equal(2, covered.Length);
        Assert.All(covered, card => Assert.True(card.Hidden));
        Assert.Contains(covered, card => card.InstanceId == firstCounter.InstanceId);
        Assert.Contains(covered, card => card.InstanceId == secondCounter.InstanceId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == deployment.InstanceId);
    }

    [Fact]
    public void PrayerRitualPublicRevealRequiresBothPlayersToAcknowledgeTheCard()
    {
        var game = Create(seed: 6207);
        var player = game.State.Players[0];
        var prayer = TakeCard(game, 0, "S02-0012");
        var disaster = Instance("S01-DS01", "prayer-disaster");
        game.State.DisasterDeck.Add(disaster);
        AddMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", prayer.InstanceId)).Accepted);
        var consent = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, consent.PlayerIndex);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: consent.PromptId, Choice: "agree")).Accepted);

        Assert.Equal(2, game.State.PendingPrompts.Count);
        Assert.All(game.State.PendingPrompts, prompt =>
        {
            Assert.Equal("s2-prayer-public-confirm", prompt.Continuation);
            Assert.Equal(disaster.InstanceId, prompt.Data["previewCardId"]);
        });
        foreach (var prompt in game.State.PendingPrompts.ToArray())
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [])).Accepted);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(player.Graveyard, card => card.InstanceId == prayer.InstanceId);
        Assert.Same(disaster, game.State.DisasterDeck[0]);
    }

    [Fact]
    public void PrayerRitualCanSpendMoraleForAPrivatePreviewAfterRefusal()
    {
        var game = Create(seed: 6208);
        var player = game.State.Players[0];
        var prayer = TakeCard(game, 0, "S02-0012");
        var disaster = Instance("S01-DS02", "private-prayer-disaster");
        game.State.DisasterDeck.Add(disaster);
        AddMorale(player, 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", prayer.InstanceId)).Accepted);
        var consent = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: consent.PromptId, Choice: "refuse")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-prayer-private-cost", payment.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "yes")).Accepted);

        var preview = Assert.Single(game.State.PendingPrompts);
        Assert.True(preview.IsPrivate);
        Assert.Equal(disaster.InstanceId, preview.Data["previewCardId"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: preview.PromptId,
            CardInstanceIds: [])).Accepted);
        Assert.Contains(player.Graveyard, card => card.InstanceId == prayer.InstanceId);
    }

    [Fact]
    public void HolyLockAttachesToAnEnemyArtifactDisablesItAndCanBeDiscarded()
    {
        var game = Create(seed: 6214);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var holyLock = TakeCard(game, 0, "S02-0013");
        var artifact = Instance("S01-0117", "holy-lock-target");
        opponent.Relic = artifact;
        AddMorale(owner, 2);
        AddMorale(opponent, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", holyLock.InstanceId)).Accepted);
        var attachPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-holy-lock-attach", attachPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: attachPrompt.PromptId,
            CardInstanceIds: [artifact.InstanceId])).Accepted);

        Assert.Contains(holyLock, artifact.AttachedCards);
        Assert.DoesNotContain(holyLock, owner.Graveyard);
        Assert.False(game.Handle(1, new L12Command("activateAbility", artifact.InstanceId, Ability: "artifactDraw")).Accepted);

        game.State.ActivePlayer = 1;
        Assert.True(game.Handle(1, new L12Command("activateAbility", artifact.InstanceId, Ability: "discardHolyLock")).Accepted);
        Assert.Empty(artifact.AttachedCards);
        Assert.Contains(holyLock, owner.Graveyard);
    }

    [Fact]
    public void ShennongDingCanResetOneUsedMasterAbility()
    {
        var game = CreateTianting(seed: 6210);
        var player = game.State.Players[0];
        var shennong = Instance("S02-0104", "shennong-test");
        player.Hand.Add(shennong);
        AddMorale(player, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", shennong.InstanceId)).Accepted);
        var drawPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: drawPrompt.PromptId, Choice: "no")).Accepted);

        var usedKey = "active:master-0:drawCycle";
        player.UsedAbilities.Add(usedKey);
        Assert.True(game.Handle(0, new L12Command("activateAbility", shennong.InstanceId, Ability: "shennongReset")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            CardInstanceIds: ["drawCycle"])).Accepted);
        var returnPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", returnPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: returnPrompt.PromptId,
            CardInstanceIds: [returnPrompt.ValidChoices[0]])).Accepted);

        Assert.True(shennong.Tapped);
        Assert.DoesNotContain(usedKey, player.UsedAbilities);
        Assert.Equal(2, player.Morale.Count);
    }

    [Fact]
    public void QianKunYangKillsByPrintedTroopsThenMayReturnMoraleToDraw()
    {
        var game = CreateTianting(seed: 6211);
        var player = game.State.Players[0];
        var tactic = Instance("S02-0105", "qianyang-test");
        var target = Instance("S02-0003", "qianyang-target");
        player.Hand.Add(tactic);
        game.State.Players[1].Field[0][0] = target;
        AddMorale(player, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var killPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: killPrompt.PromptId,
            CardInstanceIds: [target.InstanceId])).Accepted);
        var drawPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: drawPrompt.PromptId, Choice: "yes")).Accepted);
        var returnPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", returnPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: returnPrompt.PromptId,
            CardInstanceIds: [returnPrompt.ValidChoices[0]])).Accepted);

        Assert.Contains(target, game.State.Players[1].Graveyard);
        Assert.Equal(2, player.Morale.Count);
        Assert.Contains(tactic, player.Graveyard);
    }
}
