using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GameEngineTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

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

    private static L12GameEngine Create(int seed = 1206)
        => new(Catalog, "test-match", "ABC123", seed, ["甲", "乙"], [0, 1], skipPreparation: true);

    private static L12CardInstance PutCardInHand(L12GameEngine game, int playerIndex, string cardId)
    {
        var player = game.State.Players[playerIndex];
        var card = player.Hand.Concat(player.Library).First(candidate => candidate.CardId == cardId);
        player.Hand.Remove(card);
        player.Library.Remove(card);
        player.Hand.Add(card);
        while (player.MoraleDeck.Count > 0)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
        game.State.ActivePlayer = playerIndex;
        game.State.Phase = L12Phase.Main;
        return card;
    }

    [Fact]
    public void CatalogContainsS1S2AndStarterCardsAndSixOfficialPresets()
    {
        var catalog = Catalog;
        Assert.Equal(324, catalog.Cards.Count);
        Assert.Equal(76, catalog.Cards.Values.Count(card =>
            card.Id.StartsWith("ST", StringComparison.Ordinal)));
        Assert.Equal(6, catalog.PresetDecks.Count);
        Assert.Equal(6, catalog.PresetDecks.Select(deck => catalog.Cards[deck.MasterId].Faction).Distinct().Count());
        Assert.All(catalog.PresetDecks, deck =>
        {
            var faction = catalog.Cards[deck.MasterId].Faction;
            var countedMain = deck.CardIds.Count(id => id != "S01-0212");
            Assert.InRange(countedMain, 40, 50);
            Assert.Equal(faction == "taiyangcheng" ? 6 : 8, deck.MoraleIds.Count);
            Assert.All(deck.SpecialIds, id => Assert.Equal("trial", catalog.Cards[id].CardType));
        });
    }

    [Fact]
    public void KusanagiUsesCanonicalOriginalNameAcrossCatalog()
    {
        var catalog = Catalog;
        Assert.Equal("草薙剑", catalog.Cards["S01-0417"].NameZh);
        Assert.DoesNotContain(catalog.Cards.Values, card =>
            card.NameZh.Contains("草稚剑", StringComparison.Ordinal)
            || card.NameZh.Contains("草雉剑", StringComparison.Ordinal)
            || card.Effect?.Contains("草稚剑", StringComparison.Ordinal) == true
            || card.Effect?.Contains("草雉剑", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SameSeedProducesSameInitialStateHash()
    {
        var first = Create(42);
        var second = Create(42);
        Assert.Equal(first.ComputeStateHash(), second.ComputeStateHash());
    }

    [Fact]
    public void BothPlayersMustFinishMulliganBeforeGameStarts()
    {
        var game = Create();
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.Equal(L12Phase.Mulligan, game.State.Phase);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.False(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
    }

    [Fact]
    public void ServerRejectsNonTurnPhaseAction()
    {
        var game = Create();
        game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
        game.Handle(1, new L12Command("mulligan", CardInstanceIds: []));
        var wrongPlayer = 1 - game.State.ActivePlayer;
        var result = game.Handle(wrongPlayer, new L12Command("endTurn"));
        Assert.False(result.Accepted);
        Assert.Equal(L12Phase.Main, game.State.Phase);
    }

    [Fact]
    public void DefaultResponseModeSkipsWindowsWithoutAnyLegalResponse()
    {
        var game = new L12GameEngine(Catalog, "response-meaningful", "MEANING", 1208,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var card = PutCardInHand(game, 0, "S01-0103");

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Fact]
    public void ProductionResponseModeOffersAnonymousPassEvenWhenNoResponseCardExists()
    {
        var game = new L12GameEngine(Catalog, "response-privacy", "PRIVACY", 1207,
            ["甲", "乙"], [0, 1], skipPreparation: true, autoPassEmptyResponses: false);
        var card = PutCardInHand(game, 0, "S01-0103");

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        var first = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", first.Kind);
        Assert.Equal(["pass"], first.ValidChoices);
        Assert.True(game.Handle(first.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: first.PromptId, Choice: "pass")).Accepted);

        var second = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", second.Kind);
        Assert.Equal(["pass"], second.ValidChoices);
        Assert.NotEqual(first.PlayerIndex, second.PlayerIndex);
    }

    [Fact]
    public void FormalResponseVisibilityUsesCardPoolWithoutRevealingCoveredCardIdentity()
    {
        var game = new L12GameEngine(Catalog, "response-card-pool", "POOL", 1209,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            concealHiddenResponseAvailability: true);
        var entering = PutCardInHand(game, 0, "S01-0103");
        var defender = game.State.Players[1];
        var unrelatedCounter = Card("S01-0017", "covered-unrelated");
        unrelatedCounter.Hidden = true;
        unrelatedCounter.SetRound = 0;
        defender.Field[1][0] = unrelatedCounter;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(1, response.PlayerIndex);
        Assert.Equal(["pass"], response.ValidChoices);

        Assert.True(game.Handle(1,
            new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(2, game.State.Events.Count(item => item.Type == "priority-pass"));
    }

    [Theory]
    [InlineData("S01-0017")]
    [InlineData("S01-0019")]
    public void FormalResponseVisibilityIgnoresCoveredCounterTacticIdentity(string coveredCardId)
    {
        var game = new L12GameEngine(Catalog, $"response-covered-{coveredCardId}", "COVERED", 1211,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            concealHiddenResponseAvailability: true);
        var entering = PutCardInHand(game, 0, "S01-0103");
        var covered = Card(coveredCardId, $"covered-{coveredCardId}");
        covered.Hidden = true;
        covered.SetRound = 0;
        game.State.Players[1].Field[1][0] = covered;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(1, response.PlayerIndex);
        Assert.Equal(["pass"], response.ValidChoices);
    }

    [Fact]
    public void FormalResponseVisibilityDoesNotTreatAHiddenLegionAsACounterTactic()
    {
        var game = new L12GameEngine(Catalog, "response-hidden-legion", "COVERED", 12111,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            concealHiddenResponseAvailability: true);
        var entering = PutCardInHand(game, 0, "S01-0103");
        var hiddenLegion = Card("S01-0415", "covered-hattori");
        hiddenLegion.Hidden = true;
        hiddenLegion.SetRound = 0;
        game.State.Players[1].Field[1][0] = hiddenLegion;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Fact]
    public void FormalResponseVisibilityDoesNotRevealWhetherHandContainsMercenary()
    {
        var game = new L12GameEngine(Catalog, "response-hand-pool", "HANDPOOL", 1212,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            concealHiddenResponseAvailability: true);
        var attacker = Card("S01-0103", "pool-attacker");
        var target = Card("S01-0104", "pool-target");
        attacker.SummonRound = 0;
        target.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.Clear();
        game.State.Players[1].Hand.Add(Card("S01-0003", "non-response-hand"));
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(1, response.PlayerIndex);
        Assert.Equal(["pass"], response.ValidChoices);
    }

    [Fact]
    public void FormalResponseVisibilityStillListsOnlyActuallyLegalCards()
    {
        var game = new L12GameEngine(Catalog, "response-real-choice", "REAL", 1210,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            concealHiddenResponseAvailability: true);
        var entering = PutCardInHand(game, 0, "S01-0103");
        var defender = game.State.Players[1];
        var ambush = Card("S01-0019", "covered-ambush");
        var ambushTarget = Card("S01-0103", "covered-ambush-target");
        ambush.Hidden = true;
        ambush.SetRound = 0;
        defender.Field[1][0] = ambush;
        defender.Field[0][0] = ambushTarget;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(1, response.PlayerIndex);
        Assert.Contains(ambush.InstanceId, response.ValidChoices);
        Assert.Contains("pass", response.ValidChoices);
        Assert.Equal(2, response.ValidChoices.Count);
    }

    [Fact]
    public void FirstPlayerSkipsOnlyTheirFirstDrawWhileSecondPlayerDrawsNormally()
    {
        var game = Create();
        var first = game.State.FirstPlayer;
        var second = 1 - first;
        var firstHandBefore = game.State.Players[first].Hand.Count;
        var firstLibraryBefore = game.State.Players[first].Library.Count;
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(first, game.State.ActivePlayer);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(firstHandBefore, game.State.Players[first].Hand.Count);
        Assert.Equal(firstLibraryBefore, game.State.Players[first].Library.Count);
        Assert.Single(game.State.Players[first].Morale);
        Assert.Single(game.State.Events, item => item.Type == "draw-skipped" && item.PlayerIndex == first);

        var secondHandBefore = game.State.Players[second].Hand.Count;
        var secondLibraryBefore = game.State.Players[second].Library.Count;
        Assert.True(game.Handle(first, new L12Command("endTurn")).Accepted);
        Assert.Equal(second, game.State.ActivePlayer);
        Assert.Equal(secondHandBefore + 1, game.State.Players[second].Hand.Count);
        Assert.Equal(secondLibraryBefore - 1, game.State.Players[second].Library.Count);
    }

    [Fact]
    public void EndTurnClearsTroopsDamageForBothPlayersAndRunsAllStartPhases()
    {
        var game = Create();
        game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
        game.Handle(1, new L12Command("mulligan", CardInstanceIds: []));
        var active = game.State.ActivePlayer;
        var other = 1 - active;
        var activeCard = new L12CardInstance
        {
            InstanceId = "turn-end-active-legion",
            CardId = "S01-0107",
            Name = "对方回合前排加值军团",
            CardType = "legion",
            Faction = "tianting",
            Cost = 4,
            BaseTroops = 4000,
            Troops = 4000,
        };
        var otherCard = new L12CardInstance
        {
            InstanceId = "turn-end-other-legion",
            CardId = "test-persistent-derived-legion",
            Name = "持续派生军团",
            CardType = "legion",
            Faction = "avalon",
            Cost = 3,
            BaseTroops = 3000,
            Troops = 3000,
        };
        otherCard.AttachedCards.Add(new L12CardInstance
        {
            InstanceId = "persistent-king-sword",
            CardId = "S02-06S2",
            Name = "王者之剑",
            CardType = "special",
            Faction = "avalon",
            Cost = 0,
            BaseTroops = 0,
            Troops = 0,
        });
        game.State.Players[active].Field[0][0] = activeCard;
        game.State.Players[other].Field[0][0] = otherCard;
        game.SnapshotFor(active);
        L12DerivedStats.ApplyTroopsDamage(activeCard, 1000);
        L12DerivedStats.ApplyTroopsDamage(otherCard, 1000);

        Assert.True(game.Handle(active, new L12Command("endTurn")).Accepted);

        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(other, game.State.ActivePlayer);
        Assert.Equal(activeCard.BaseTroops + 1000, activeCard.Troops);
        Assert.Equal(otherCard.BaseTroops + 1000, otherCard.Troops);
        Assert.Contains(game.State.Events, item => item.Text == "执行结束阶段");
        Assert.Contains(game.State.Events, item => item.Text == "执行重置阶段");
        Assert.Contains(game.State.Events, item => item.Text == "进入主要阶段");
    }

    [Fact]
    public void LegionCombatUsesCurrentTroopsForSimultaneousDamage()
    {
        var game = Create();
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = game.State.Players[attackerPlayer].Hand.First(card => card.CardType == "legion" && card.BaseTroops >= 2000);
        var target = game.State.Players[defenderPlayer].Hand.First(card => card.CardType == "legion" && card.BaseTroops >= 2000);
        game.State.Players[attackerPlayer].Hand.Remove(attacker);
        game.State.Players[defenderPlayer].Hand.Remove(target);
        attacker.SummonRound = 1;
        attacker.Troops = 2000;
        target.Troops = 1000;
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        game.State.Players[defenderPlayer].Field[0][0] = target;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        Assert.Equal(1000, attacker.Troops);
        Assert.Contains(target, game.State.Players[defenderPlayer].Graveyard);
        var snapshot = game.SnapshotFor(attackerPlayer);
        Assert.Equal(game.State.Events.Count, snapshot.RecentEvents.Length);
        var attackEvent = snapshot.RecentEvents.Single(item => item.Type == "attack");
        Assert.Contains(attackEvent.Cards, card => card.Name == attacker.Name);
        Assert.Contains(attackEvent.Cards, card => card.Name == target.Name);
    }

    [Fact]
    public void CurrentTurnTroopsBonusIsIncludedInCombatAndRemainingTroopsSurvive()
    {
        var game = Create();
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = game.State.Players[attackerPlayer].Hand
            .Concat(game.State.Players[attackerPlayer].Library)
            .First(card => card.CardType == "legion" && card.BaseTroops == 4000);
        var target = game.State.Players[defenderPlayer].Hand
            .Concat(game.State.Players[defenderPlayer].Library)
            .First(card => card.CardType == "legion" && card.BaseTroops == 5000);
        game.State.Players[attackerPlayer].Hand.Remove(attacker);
        game.State.Players[attackerPlayer].Library.Remove(attacker);
        game.State.Players[defenderPlayer].Hand.Remove(target);
        game.State.Players[defenderPlayer].Library.Remove(target);
        attacker.SummonRound = -1;
        attacker.TimedModifiers.Add(new L12TimedModifier
        {
            TroopsDelta = 2000,
            CostDelta = 0,
            ExpiresAfterTurn = game.State.TurnSerial,
            Source = "本回合兵力加值测试",
        });
        attacker.Troops = 6000;
        target.Troops = 5000;
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        game.State.Players[defenderPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[0][0] = target;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        for (var step = 0; step < 20 && game.State.PendingDefense is not null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) break;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Same(attacker, game.State.Players[attackerPlayer].Field[0][0]);
        Assert.Equal(1000, attacker.Troops);
        Assert.Contains(target, game.State.Players[defenderPlayer].Graveyard);
    }

    [Fact]
    public void SaladinAdjacentAttackBonusExpiresAfterThatAttackAndDoesNotStack()
    {
        var game = Create();
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = new L12CardInstance
        {
            InstanceId = "saladin-bonus-attacker",
            CardId = "test-sun-legion",
            Name = "测试太阳城军团",
            CardType = "legion",
            Faction = "taiyangcheng",
            BaseTroops = 4000,
            Troops = 4000,
            Cost = 4,
            SummonRound = -1,
        };
        var saladin = new L12CardInstance
        {
            InstanceId = "saladin-source",
            CardId = "S01-0206",
            Name = "萨拉丁",
            CardType = "legion",
            Faction = "taiyangcheng",
            BaseTroops = 4000,
            Troops = 4000,
            Cost = 4,
            SummonRound = -1,
        };
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        game.State.Players[attackerPlayer].Field[0][1] = saladin;
        game.State.Players[defenderPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.Equal(5000, attacker.Troops);
        Assert.Equal(1000, game.State.PendingDefense?.TemporaryAttackerTroopsBonus);
        Assert.True(game.Handle(defenderPlayer, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        Assert.Equal(4000, attacker.Troops);

        attacker.Tapped = false;
        game.State.Phase = L12Phase.Main;
        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.Equal(5000, attacker.Troops);
        Assert.Equal(1000, game.State.PendingDefense?.TemporaryAttackerTroopsBonus);
    }

    [Fact]
    public void SaladinAttackBonusAbsorbsCombatDamageBeforeItExpires()
    {
        var game = Create(1217);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = new L12CardInstance
        {
            InstanceId = "saladin-layer-attacker",
            CardId = "test-sun-layer-legion",
            Name = "测试太阳城军团",
            CardType = "legion",
            Faction = "taiyangcheng",
            BaseTroops = 4000,
            Troops = 4000,
            Cost = 4,
            SummonRound = -1,
        };
        var saladin = Card("S01-0206", "saladin-layer-source");
        saladin.SummonRound = -1;
        var target = new L12CardInstance
        {
            InstanceId = "saladin-layer-target",
            CardId = "test-layer-target",
            Name = "测试防守军团",
            CardType = "legion",
            Faction = "universal",
            BaseTroops = 4500,
            Troops = 4500,
            Cost = 4,
            SummonRound = -1,
        };
        game.State.Players[attackerPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[attackerPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        game.State.Players[attackerPlayer].Field[0][1] = saladin;
        game.State.Players[defenderPlayer].Field[0][0] = target;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        for (var step = 0; step < 30 && game.State.PendingDefense is not null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Same(attacker, game.State.Players[attackerPlayer].Field[0][0]);
        Assert.Equal(500, attacker.Troops);
        Assert.Contains(target, game.State.Players[defenderPlayer].Graveyard);
    }

    [Fact]
    public void PromotedAchillesTakesOneThousandExtraIncomingRangedCombatDamage()
    {
        var game = Create(1218);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = Card("S01-0208", "ranged-achilles-attacker");
        var achilles = Card("S02-0503", "promoted-achilles-target");
        attacker.Troops = 4000;
        attacker.SummonRound = -1;
        achilles.Troops = 7000;
        achilles.SummonRound = -1;
        game.State.Players[attackerPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[attackerPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[attackerPlayer].Field[1][0] = attacker;
        game.State.Players[defenderPlayer].Field[0][0] = achilles;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", achilles.InstanceId))).Accepted);
        Assert.True(game.State.PendingDefense?.IsRanged);
        for (var step = 0; step < 30 && game.State.PendingDefense is not null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Same(achilles, game.State.Players[defenderPlayer].Field[0][0]);
        Assert.Equal(2000, achilles.Troops);
        Assert.Equal(4000, attacker.Troops);
    }

    [Fact]
    public void PromotedAchillesDoesNotAddDamageToFrontToFrontCombat()
    {
        var game = Create(1219);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = Card("S01-0208", "melee-achilles-attacker");
        var achilles = Card("S02-0503", "melee-promoted-achilles-target");
        attacker.Troops = 4000;
        attacker.SummonRound = -1;
        achilles.Troops = 5000;
        achilles.SummonRound = -1;
        game.State.Players[attackerPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[attackerPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[0] = new L12CardInstance?[3];
        game.State.Players[defenderPlayer].Field[1] = new L12CardInstance?[3];
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        game.State.Players[defenderPlayer].Field[0][0] = achilles;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", achilles.InstanceId))).Accepted);
        Assert.False(game.State.PendingDefense?.IsRanged);
        for (var step = 0; step < 30 && game.State.PendingDefense is not null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Same(achilles, game.State.Players[defenderPlayer].Field[0][0]);
        Assert.Equal(1000, achilles.Troops);
        Assert.Contains(attacker, game.State.Players[attackerPlayer].Graveyard);
    }

    [Fact]
    public void SameColumnBackLegionIsTheSupportLegion()
    {
        var game = Create();
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = game.State.Players[attackerPlayer].Hand.First(card => card.CardType == "legion");
        var target = game.State.Players[defenderPlayer].Hand.First(card => card.CardType == "legion");
        var support = game.State.Players[defenderPlayer].Hand.First(card => card.CardType == "legion" && card != target);
        game.State.Players[attackerPlayer].Hand.Remove(attacker);
        game.State.Players[defenderPlayer].Hand.Remove(target);
        game.State.Players[defenderPlayer].Hand.Remove(support);
        attacker.SummonRound = 1;
        attacker.Troops = 3000;
        target.Troops = 2000;
        support.Troops = 2000;
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        game.State.Players[defenderPlayer].Field[0][1] = target;
        game.State.Players[defenderPlayer].Field[1][1] = support;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        Assert.True(game.Handle(defenderPlayer, new L12Command("resolveDefense", SupportInstanceId: support.InstanceId)).Accepted);

        Assert.Equal(3000, attacker.Troops);
        Assert.Equal(2000, target.Troops);
        Assert.Null(game.State.Players[defenderPlayer].Field[1][1]);
        Assert.Contains(support, game.State.Players[defenderPlayer].Graveyard);
        Assert.DoesNotContain(target, game.State.Players[defenderPlayer].Graveyard);
    }

    [Fact]
    public void LegionCanDisplaceItsControllersCoveredCounterTactic()
    {
        var game = Create();
        var playerIndex = game.State.ActivePlayer;
        var counter = PutCardInHand(game, playerIndex, "S01-0016");

        Assert.True(game.Handle(playerIndex,
            new L12Command("playCard", counter.InstanceId, Row: 1, Slot: 1)).Accepted);
        Assert.Same(counter, game.State.Players[playerIndex].Field[1][1]);
        Assert.True(counter.Hidden);

        var player = game.State.Players[playerIndex];
        var legion = player.Hand.Concat(player.Library).First(card => card.CardType == "legion");
        player.Library.Remove(legion);
        if (!player.Hand.Contains(legion)) player.Hand.Add(legion);
        var result = game.Handle(playerIndex,
            new L12Command("playCard", legion.InstanceId, Row: 1, Slot: 1));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(legion, game.State.Players[playerIndex].Field[1][1]);
        Assert.Contains(counter, game.State.Players[playerIndex].Graveyard);
        Assert.False(counter.Hidden);
        Assert.Contains(game.State.Events, item => item.Type == "counter-displaced"
            && item.Cards.Any(card => card.InstanceId == counter.InstanceId));
    }

    [Fact]
    public void UnblockedMasterAttackEndsGameAtZeroHp()
    {
        var game = Create();
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attackerOwner = game.State.Players[attackerPlayer];
        var attacker = attackerOwner.Hand.First(card => card.CardType == "legion");
        attackerOwner.Hand.Remove(attacker);
        attacker.SummonRound = 1;
        attackerOwner.Field[0][0] = attacker;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        game.State.Players[defenderPlayer].Hp = 1;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId, Target: new L12AttackTarget("master"))).Accepted);
        Assert.True(game.Handle(defenderPlayer, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        Assert.Equal(attackerPlayer, game.State.Winner);
        Assert.Equal(L12Phase.GameOver, game.State.Phase);
    }

    [Fact]
    public void SnapshotKeepsCompleteEventHistoryBeyondLegacyEightyEventLimit()
    {
        var game = Create();
        game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
        game.Handle(1, new L12Command("mulligan", CardInstanceIds: []));

        for (var turn = 0; turn < 15; turn++)
        {
            var result = game.Handle(game.State.ActivePlayer, new L12Command("endTurn"));
            Assert.True(result.Accepted, result.Error);
        }

        Assert.True(game.State.Events.Count > 80);
        var snapshot = game.SnapshotFor(0);
        Assert.Equal(game.State.Events.Count, snapshot.RecentEvents.Length);
        Assert.Equal(1, snapshot.RecentEvents[0].Sequence);
    }

    [Fact]
    public void SanadaYukimuraChargeAllowsAttackOnEntryRound()
    {
        var game = Create();
        var card = PutCardInHand(game, 1, "S01-0404");

        Assert.True(game.Handle(1, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.True(card.HasCharge);
        Assert.True(game.Handle(1, new L12Command("attack", card.InstanceId, Target: new L12AttackTarget("master"))).Accepted);
        Assert.Contains(game.State.Events, item => item.Type == "effect" && item.Text.Contains("获得冲锋"));
    }

    [Fact]
    public void BaiQiAddsThreeRestedMoraleOnEnter()
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = PutCardInHand(game, 0, "S01-0109");
        while (player.Morale.Count > 5)
        {
            player.MoraleDeck.Add(player.Morale[^1]);
            player.Morale.RemoveAt(player.Morale.Count - 1);
        }
        var before = player.Morale.Count;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);

        var added = player.Morale.Skip(before).ToArray();
        Assert.Equal(3, added.Length);
        Assert.All(added, morale => Assert.True(morale.Tapped));
    }

    [Fact]
    public void MinamotoNoHiromasaDrawsWhenRemainingHandDoesNotExceedFive()
    {
        var game = Create();
        var player = game.State.Players[1];
        var card = PutCardInHand(game, 1, "S01-0413");
        foreach (var other in player.Hand.Where(candidate => candidate != card).ToArray())
        {
            player.Hand.Remove(other);
            player.Library.Add(other);
        }

        Assert.True(game.Handle(1, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var optional = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", optional.Continuation);
        Assert.Contains("mode:use", optional.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(player.Hand);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: optional.PromptId,
            Choice: "mode:use")).Accepted);
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var response = Assert.Single(game.State.PendingPrompts);
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Single(player.Hand);
        Assert.Contains(game.State.Events, item => item.Type == "effect" && item.Text.Contains("抽取 1 张牌"));
    }

    [Fact]
    public void ShanHeSheJiTuAddsOneActiveMoraleOnEnter()
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = PutCardInHand(game, 0, "S01-0117");
        while (player.Morale.Count > 7)
        {
            player.MoraleDeck.Add(player.Morale[^1]);
            player.Morale.RemoveAt(player.Morale.Count - 1);
        }
        var before = player.Morale.Count;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId)).Accepted);

        Assert.Equal(before + 1, player.Morale.Count);
        Assert.False(player.Morale[^1].Tapped);
    }

    [Fact]
    public void QuanJunChuJiGrantsChargeToNextEligibleLegionThisTurn()
    {
        var game = Create();
        var tactic = PutCardInHand(game, 0, "S01-0012");
        var legion = PutCardInHand(game, 0, "S01-0105");

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Assert.Equal(6, game.State.Players[0].NextLegionChargeMaxCost);
        Assert.True(game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 0)).Accepted);
        var optional = game.State.PendingPrompts.FirstOrDefault();
        if (optional is not null)
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId, Choice: "mode:none")).Accepted);

        Assert.True(legion.HasCharge);
        Assert.Null(game.State.Players[0].NextLegionChargeMaxCost);
        Assert.True(game.Handle(0, new L12Command("attack", legion.InstanceId, Target: new L12AttackTarget("master"))).Accepted);
    }
}
