using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ImmortalityRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 6510, string disasterMode = "none")
        => new(Catalog, "immortality-regression", "IMMORTAL", seed,
            ["甲", "乙"], [0, 1], skipPreparation: true, disasterMode: disasterMode);

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
        };
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 20; guard++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault(candidate => candidate.ValidChoices.Contains("pass"));
            if (prompt is null) return;
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        throw new InvalidOperationException("响应窗口未在限定次数内结束");
    }

    [Fact]
    public void DefeatImmortalitySetsOneThousandReappliesContinuousAndPreservesFieldState()
    {
        var game = Create();
        var player = game.State.Players[0];
        var target = Card("S01-0002", "immortal-survivor");
        target.Tapped = true;
        target.HasSureHit = true;
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        target.AttachedCards.Add(Card("S02-06S2", "attached-king-sword"));
        player.Field[0][0] = target;

        var result = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: target.InstanceId));

        Assert.False(result.Accepted);
        Assert.Same(target, player.Field[0][0]);
        Assert.True(target.Tapped);
        Assert.True(target.HasSureHit);
        Assert.Single(target.AttachedCards);
        Assert.Equal(0, target.ImmortalUses);
        Assert.Equal(1000, target.SetTroopsValue);
        Assert.Equal(2000, target.Troops);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("设定为 1000 后重算持续修正"));
    }

    [Fact]
    public void DefeatStillRemovesCardWhenContinuousModifierLeavesImmortalAtZero()
    {
        var game = Create(6511);
        var player = game.State.Players[0];
        var target = Card("S01-0002", "immortal-zero");
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        player.Field[0][0] = target;
        player.Field[1][0] = Card("S02-0523", "trojan-horse");

        var result = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: target.InstanceId));

        Assert.True(result.Accepted, result.Error);
        Assert.Null(player.Field[0][0]);
        Assert.Contains(target, player.Graveyard);
        Assert.Equal(0, target.ImmortalUses);
        Assert.Equal(-1, target.ImmortalUntilTurn);
        Assert.Equal(target.BaseTroops, target.Troops);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("持续兵力修正重算后兵力仍不高于 0"));
    }

    [Fact]
    public void ReturnToHandDoesNotConsumeImmortalityAndLeavingFieldClearsIt()
    {
        var game = Create(6512);
        var player = game.State.Players[0];
        var target = Card("S01-0002", "immortal-return");
        target.Tapped = true;
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        player.Field[0][0] = target;
        var eventCount = game.State.Events.Count;

        var result = game.HandleGm(new L12GmCommand("returnCardToHand", 0,
            CardInstanceId: target.InstanceId));

        Assert.True(result.Accepted, result.Error);
        Assert.Contains(target, player.Hand);
        Assert.DoesNotContain(game.State.Events.Skip(eventCount), entry => entry.Text.Contains("免死生效"));
        Assert.Equal(0, target.ImmortalUses);
        Assert.Equal(-1, target.ImmortalUntilTurn);
        Assert.False(target.Tapped);
    }

    [Fact]
    public void DisasterPutIntoGraveyardDoesNotTriggerImmortality()
    {
        var game = Create(6515, disasterMode: "custom");
        var player = game.State.Players[0];
        var target = Card("S01-0002", "immortal-disaster");
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        player.Field[1][0] = target;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS03", "corrupted-earth"));
        var eventCount = game.State.Events.Count;

        Assert.True(game.HandleGm(new L12GmCommand("triggerDisaster")).Accepted);
        PassResponses(game);

        Assert.Null(player.Field[1][0]);
        Assert.Contains(target, player.Graveyard);
        Assert.DoesNotContain(game.State.Events.Skip(eventCount), entry => entry.Text.Contains("免死生效"));
        Assert.Equal(0, target.ImmortalUses);
        Assert.Equal(-1, target.ImmortalUntilTurn);
    }

    [Fact]
    public void SurvivingByImmortalityDoesNotCountAsAKillForAfterAttackReadyEffects()
    {
        var game = Create(6513);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        attackerPlayer.Hand.Clear();
        defender.Hand.Clear();
        var attacker = Card("S01-0002", "immortal-combat-attacker");
        var target = Card("S01-0002", "immortal-combat-target");
        attacker.Troops = 6000;
        attacker.SummonRound = 0;
        attacker.ReadyAfterNextKillUntilTurn = game.State.TurnSerial;
        attacker.ReadyAfterNextKillSourceName = "测试击杀奖励";
        target.Troops = 1000;
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        PassResponses(game);
        if (game.State.PendingDefense is not null)
        {
            var defense = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
            Assert.True(defense.Accepted, defense.Error);
            PassResponses(game);
        }

        Assert.Same(target, defender.Field[0][0]);
        Assert.True(attacker.Tapped);
        Assert.Equal(game.State.TurnSerial, attacker.ReadyAfterNextKillUntilTurn);
        Assert.Equal("测试击杀奖励", attacker.ReadyAfterNextKillSourceName);
    }

    [Fact]
    public void ImmortalityExpiresWithItsCountAtTurnEnd()
    {
        var game = Create(6514);
        var target = Card("S01-0002", "immortal-expiry");
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);

        Assert.Equal(0, target.ImmortalUses);
        Assert.Equal(-1, target.ImmortalUntilTurn);
    }

    [Fact]
    public void CompletedLakeLadyTrialRemovesKingSwordInsteadOfArthurForLethalEffect()
    {
        var game = Create(6516);
        var player = game.State.Players[0];
        var arthur = Card("S02-0601", "lake-lady-effect-arthur");
        var sword = Card("S02-06S2", "lake-lady-effect-sword");
        var trial = Card("S02-06S3", "lake-lady-effect-trial");
        trial.TrialCompleted = true;
        arthur.AttachedCards.Add(sword);
        L12DerivedStats.ApplyContinuousModifiers(arthur,
            new Dictionary<string, int> { [$"attached:{sword.InstanceId}:king-sword"] = 1000 },
            0, game.State.TurnSerial);
        player.Field[0][0] = arthur;
        player.SpecialZones.Trials.Add(trial);

        var result = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: arthur.InstanceId));

        Assert.False(result.Accepted);
        Assert.Same(arthur, player.Field[0][0]);
        Assert.Empty(arthur.AttachedCards);
        Assert.Contains(sword, player.Graveyard);
        Assert.DoesNotContain(arthur, player.Graveyard);
        Assert.Equal(5000, arthur.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "replacement"
            && entry.Text.Contains("湖中仙女的馈赠") && entry.Text.Contains("王者之剑"));
    }

    [Fact]
    public void CompletedLakeLadyTrialRemovesKingSwordInsteadOfArthurForLethalCombat()
    {
        var game = Create(6517);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        attackerPlayer.Hand.Clear();
        defender.Hand.Clear();
        var attacker = Card("S02-0501", "lake-lady-combat-attacker");
        var arthur = Card("S02-0601", "lake-lady-combat-arthur");
        var sword = Card("S02-06S2", "lake-lady-combat-sword");
        var trial = Card("S02-06S3", "lake-lady-combat-trial");
        trial.TrialCompleted = true;
        attacker.SummonRound = 0;
        arthur.AttachedCards.Add(sword);
        L12DerivedStats.ApplyContinuousModifiers(arthur,
            new Dictionary<string, int> { [$"attached:{sword.InstanceId}:king-sword"] = 1000 },
            0, game.State.TurnSerial);
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = arthur;
        defender.SpecialZones.Trials.Add(trial);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", arthur.InstanceId))).Accepted);
        PassResponses(game);
        if (game.State.PendingDefense is not null)
        {
            var defense = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
            Assert.True(defense.Accepted, defense.Error);
            PassResponses(game);
        }

        Assert.Same(arthur, defender.Field[0][0]);
        Assert.Empty(arthur.AttachedCards);
        Assert.Contains(sword, defender.Graveyard);
        Assert.DoesNotContain(arthur, defender.Graveyard);
        Assert.Equal(5000, arthur.Troops);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation is "combat-lethal-replacement" or "effect-lethal-replacement");
    }
}
