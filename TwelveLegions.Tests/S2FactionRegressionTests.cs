using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S2FactionRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 6301)
        => new(Catalog, "s2-faction", "S2FACTION", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12CardInstance Card(string id, string instanceId)
    {
        var definition = Catalog.Cards[id];
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            CannotAttack = definition.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
        };
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

    [Fact]
    public void AsgardEntryThatCanAttackMasterDoesNotGainGeneralCharge()
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card("S02-0301", "s2-asgard-master-attack");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        Assert.False(card.HasCharge);
        Assert.Equal(game.State.TurnSerial, card.CanAttackMasterOnSummonUntilTurn);
        Assert.True(game.Handle(0, new L12Command("attack", card.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
    }

    [Fact]
    public void AsgardEntryHealsMaster()
    {
        var game = Create(6302);
        var player = game.State.Players[0];
        player.Hp = player.MaxHp - 2;
        var card = Card("S02-0302", "s2-asgard-heal");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        Assert.Equal(player.MaxHp - 1, player.Hp);
    }

    [Fact]
    public void AsgardTacticMillsBeforeItDebuffsTheChosenEnemy()
    {
        var game = Create(6303);
        var player = game.State.Players[0];
        var tactic = Card("S02-0307", "s2-asgard-curse");
        var milled = Card("S02-0007", "s2-asgard-milled");
        var target = Card("S02-0004", "s2-asgard-target");
        player.Hand.Add(tactic);
        player.Library.Insert(0, milled);
        game.State.Players[1].Field[0][0] = target;
        AddMorale(player, tactic.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Assert.Contains(milled, player.Graveyard);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        // The target is a front-row defender, so its existing opposing-turn +1000
        // continuous modifier remains alongside this tactic's -3000 modifier.
        Assert.Equal(target.BaseTroops - 2000, target.Troops);
    }

    [Fact]
    public void OtherworldMerlinEntryAddsARuneAndCannotAttack()
    {
        var game = Create(6304);
        var player = game.State.Players[0];
        var card = Card("S02-0603", "s2-otherworld-merlin-entry");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Same(card, player.Field[0][0]);
        Assert.True(card.CannotAttack);
    }

    [Fact]
    public void MerlinDeclaresModeAndTargetBeforeSpendingRune()
    {
        var game = Create(6308);
        var player = game.State.Players[0];
        var merlin = Card("S02-0603", "s2-otherworld-merlin-active");
        var target = Card("S02-0004", "s2-otherworld-merlin-target");
        player.Field[0][0] = merlin;
        player.SpecialZones.Runes = 1;
        game.State.Players[1].Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", merlin.InstanceId, Ability: "merlinRune")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "mode:debuff")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId, Choice: target.InstanceId)).Accepted);

        Assert.True(merlin.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(target.BaseTroops - 3000, target.Troops);
    }

    [Fact]
    public void OlympusFlipEntryLetsThePlayerChooseOneMoraleToFlip()
    {
        var game = Create(6305);
        var player = game.State.Players[0];
        var card = Card("S02-0513", "s2-olympus-flip-entry");
        player.Hand.Add(card);
        AddMorale(player, card.Cost + 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-flip-morale", prompt.Data["action"]);
        var morale = player.Morale.First(candidate => !candidate.Tapped && prompt.ValidChoices.Contains(candidate.InstanceId));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: morale.InstanceId)).Accepted);
        Assert.True(morale.Tapped);
    }

    [Fact]
    public void AristotleDiscountIsConsumedByTheNextOlympusLegion()
    {
        var game = Create(6306);
        var player = game.State.Players[0];
        var aristotle = Card("S02-0513", "s2-aristotle");
        var next = Card("S02-0518", "s2-theseus");
        player.Field[0][0] = aristotle;
        player.Hand.Add(next);
        AddMorale(player, Math.Max(1, next.Cost - 1));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", aristotle.InstanceId, Ability: "aristotleDiscount")).Accepted);
        Assert.Equal(1, player.NextS2OlympusLegionDiscount);
        Assert.True(game.Handle(0, new L12Command("playCard", next.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.Equal(0, player.NextS2OlympusLegionDiscount);
        Assert.Same(next, player.Field[0][1]);
    }

    [Fact]
    public void OtherworldArthurEntryHasChargeAndPairDiscountApplies()
    {
        var game = Create(6307);
        var player = game.State.Players[0];
        var percival = Card("S02-0611", "s2-percival");
        var arthur = Card("S02-0612", "s2-arthur");
        player.Field[0][0] = percival;
        player.Hand.Add(arthur);
        AddMorale(player, Math.Max(0, arthur.Cost - 2));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", arthur.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.True(arthur.HasCharge);
    }

    [Fact]
    public void RestedAmakineProtectsOnlyActiveTrialLegionsFromAttack()
    {
        var game = Create(6309);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = Card("S02-0004", "amakine-test-attacker");
        attacker.SummonRound = 0;
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        var amakine = Card("S02-0616", "amakine-protector");
        amakine.Tapped = true;
        var galahad = Card("S02-0604", "amakine-trial-legion");
        game.State.Players[defenderPlayer].Field[0][0] = galahad;
        game.State.Players[defenderPlayer].Field[1][0] = amakine;
        game.State.Phase = L12Phase.Main;

        var protectedAttack = game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", galahad.InstanceId)));
        Assert.False(protectedAttack.Accepted);
        Assert.Contains("阿麦金", protectedAttack.Error);

        galahad.Tapped = true;
        var restedTargetAttack = game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", galahad.InstanceId)));
        Assert.True(restedTargetAttack.Accepted, restedTargetAttack.Error);
    }

    [Fact]
    public void AmakineShowsTheTopOtherworldCardAndCanAddItToHand()
    {
        var game = Create(6310);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        var amakine = Card("S02-0616", "amakine-top-source");
        var top = Card("S02-0619", "amakine-top-card");
        player.Field[0][0] = amakine;
        player.Library.Insert(0, top);
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", amakine.InstanceId, Ability: "amakineTop"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.True(amakine.Tapped);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-amakine-top-place", prompt.Data["action"]);
        Assert.Equal(top.InstanceId, prompt.Data["previewCardId"]);
        Assert.Contains("hand", prompt.ValidChoices);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "hand")).Accepted);
        Assert.Contains(top, player.Hand);
        Assert.DoesNotContain(top, player.Library);
    }

    [Fact]
    public void GalahadCanPayItselfAfterTheGrailTrialToDrawAndHeal()
    {
        var game = Create(6311);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        var galahad = Card("S02-0604", "galahad-grail-source");
        var grail = Card("S02-06S4", "completed-grail");
        grail.TrialCompleted = true;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(grail);
        player.Field[0][0] = galahad;
        player.Hp = player.MaxHp - 1;
        var handBefore = player.Hand.Count;
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", galahad.InstanceId, Ability: "galahadGrailReward"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.Contains(galahad, player.Graveyard);
        Assert.Null(player.Field[0][0]);
        Assert.Equal(handBefore + 1, player.Hand.Count);
        Assert.Equal(player.MaxHp, player.Hp);
    }
}
