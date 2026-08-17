using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GameEngineTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

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
    public void CatalogContainsS1AndS2CardsAndSixOfficialPresets()
    {
        var catalog = Catalog;
        Assert.Equal(248, catalog.Cards.Count);
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
    public void FirstPlayerSkipsDrawAndReceivesOneMorale()
    {
        var game = Create();
        game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
        game.Handle(1, new L12Command("mulligan", CardInstanceIds: []));
        var active = game.State.ActivePlayer;
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(6, game.State.Players[active].Hand.Count);
        Assert.Single(game.State.Players[active].Morale);
        Assert.Contains(game.State.Events, item => item.Type == "draw-skipped");
    }

    [Fact]
    public void EndTurnRunsAllStartPhasesAndRestoresBothFields()
    {
        var game = Create();
        game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
        game.Handle(1, new L12Command("mulligan", CardInstanceIds: []));
        var active = game.State.ActivePlayer;
        var other = 1 - active;
        var activeCard = game.State.Players[active].Hand.First(card => card.CardType == "legion");
        var otherCard = game.State.Players[other].Hand.First(card => card.CardType == "legion");
        game.State.Players[active].Hand.Remove(activeCard);
        game.State.Players[other].Hand.Remove(otherCard);
        activeCard.Troops = Math.Max(1, activeCard.BaseTroops - 1000);
        otherCard.Troops = Math.Max(1, otherCard.BaseTroops - 1000);
        game.State.Players[active].Field[0][0] = activeCard;
        game.State.Players[other].Field[0][0] = otherCard;

        Assert.True(game.Handle(active, new L12Command("endTurn")).Accepted);

        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(other, game.State.ActivePlayer);
        Assert.Equal(activeCard.BaseTroops, activeCard.Troops);
        Assert.Equal(otherCard.BaseTroops, otherCard.Troops);
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
        Assert.True(game.Handle(defenderPlayer, new L12Command("resolveDefense")).Accepted);

        Assert.Equal(1000, attacker.Troops);
        Assert.Contains(target, game.State.Players[defenderPlayer].Graveyard);
        var snapshot = game.SnapshotFor(attackerPlayer);
        Assert.Equal(game.State.Events.Count, snapshot.RecentEvents.Length);
        var attackEvent = snapshot.RecentEvents.Single(item => item.Type == "attack");
        Assert.Contains(attackEvent.Cards, card => card.Name == attacker.Name);
        Assert.Contains(attackEvent.Cards, card => card.Name == target.Name);
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
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId, Choice: "skip")).Accepted);

        Assert.True(legion.HasCharge);
        Assert.Null(game.State.Players[0].NextLegionChargeMaxCost);
        Assert.True(game.Handle(0, new L12Command("attack", legion.InstanceId, Target: new L12AttackTarget("master"))).Accepted);
    }
}
