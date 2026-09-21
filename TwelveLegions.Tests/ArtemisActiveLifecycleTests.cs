using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ArtemisActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "阿尔忒弥斯主动生命周期",
            MasterId = "S02-05M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "artemis-active", "ARTEMIS-ACTIVE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            foreach (var row in player.Field) Array.Clear(row);
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? currentCost = null)
    {
        var definition = Catalog.Cards[cardId];
        var card = new L12CardInstance
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
        };
        if (currentCost is { } value) card.CostModifier = value - card.Cost;
        return card;
    }

    private static L12MoraleCard GodPower(string id) => new()
    {
        CardId = "S02-05C1", InstanceId = id, IsGodPower = true, Tapped = false,
    };

    private static void ResolveOnly(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "S02-05M1"));

    [Fact]
    public void PrintedSecondAbilityUsesOneStructuredSceneWithTwoActualResultBranches()
    {
        var ability = Catalog.AtomicEffects.Find("S02-05M1")!.Abilities
            .Single(candidate => candidate.Sequence == 2);
        var scenes = ability.Presentations.Where(scene => scene.Flow == "artemis-grant").ToArray();

        Assert.Contains(scenes, scene => scene.BranchLabel == "获得强攻"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
        Assert.Contains(scenes, scene => scene.BranchLabel == "获得震击"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void PaymentDialogKeepsBothCanonicalOptionsAndDisablesTheUnavailableOne()
    {
        var game = Create(91451);
        var player = game.State.Players[0];
        var target = Card("S02-0502", "artemis-dialog-target", 3);
        player.Field[0][0] = target;
        player.Hand.Add(Card("S02-0001", "artemis-dialog-discard"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);

        Assert.Contains("pay:discard", prompt.ValidChoices);
        Assert.DoesNotContain("pay:god-power", prompt.ValidChoices);
        Assert.Equal("pay:god-power|pay:discard|skip", prompt.Data["displayChoiceIds"]);
        Assert.Contains("没有活跃神力", prompt.Data.Values);
        Assert.Equal("effect-decision", prompt.Data["uiPattern"]);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void PaymentSelectionCanBeCancelledBeforeDiscardingOrUsingTheAbility()
    {
        var game = Create(91452);
        var player = game.State.Players[0];
        var target = Card("S02-0502", "artemis-cancel-target", 3);
        var discard = Card("S02-0001", "artemis-cancel-discard");
        player.Field[0][0] = target;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "skip");

        Assert.Contains(discard, player.Hand);
        Assert.Empty(player.Graveyard);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(player.UsedAbilities, key => key.Contains("artemisBuff", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void TargetCostChangesDuringResponseDoNotInvalidateTheErrataTarget()
    {
        var game = Create(91453);
        var player = game.State.Players[0];
        var target = Card("S02-0502", "artemis-cost-failed-target", 3);
        var discard = Card("S02-0001", "artemis-cost-failed-discard");
        player.Field[0][0] = target;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:discard");
        ResolveOnly(game, discard.InstanceId);
        ResolveOnly(game, target.InstanceId);
        ResolveOnly(game, "buff:strong");
        target.CostModifier += 4;
        PassResponses(game);

        Assert.Contains(discard, player.Graveyard);
        Assert.True(target.HasStrongAttack);
        Assert.False(target.HasShock);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void RingConvertedTargetLosingOlympusDuringResponseFailsAfterGodPowerPayment()
    {
        var game = Create(91454);
        var player = game.State.Players[0];
        var target = Card("S02-0003", "artemis-ring-failed-target", 3);
        var ring = Card("S02-0008", "artemis-ring-failed-ring");
        var power = GodPower("artemis-ring-failed-power");
        player.Field[0][0] = target;
        player.ExtraRelics.Add(ring);
        player.Morale.Add(power);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:god-power");
        ResolveOnly(game, target.InstanceId);
        ResolveOnly(game, "buff:shock");
        player.ExtraRelics.Remove(ring);
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.True(power.IsGodPower);
        Assert.False(target.HasShock);
        Assert.Equal("failed", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void NegationKeepsTheGodPowerCostAndDoesNotGrantTheChosenAbility()
    {
        var game = Create(91455);
        var player = game.State.Players[0];
        var target = Card("S02-0502", "artemis-negated-target", 3);
        var power = GodPower("artemis-negated-power");
        player.Field[0][0] = target;
        player.Morale.Add(power);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:god-power");
        ResolveOnly(game, target.InstanceId);
        ResolveOnly(game, "buff:shock");
        Assert.Equal("消耗1神力", Assert.Single(game.State.EffectStack).Data["paidCostSummary"]);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.True(power.IsGodPower);
        Assert.False(target.HasShock);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [Trait("L12Evidence", "ability:artemisBuff")]
    [Trait("L12Evidence", "entry:artemis-no-cost-range-target")]
    public void ErrataTargetAllowsAnyCostOlympusLegion(int currentCost)
    {
        var game = Create(91459 + currentCost);
        var player = game.State.Players[0];
        var target = Card("S02-0502", $"artemis-any-cost-{currentCost}", currentCost);
        player.Field[0][0] = target;
        player.Hand.Add(Card("S02-0001", $"artemis-any-cost-discard-{currentCost}"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:discard");
        ResolveOnly(game, player.Hand[0].InstanceId);

        Assert.Contains(target.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void V2RestoreGrantsTheFrozenBranchOnceAndRejectsTheOldFinalPrompt()
    {
        var game = Create(91456);
        var player = game.State.Players[0];
        var target = Card("S02-0502", "artemis-restore-target", 3);
        var discard = Card("S02-0001", "artemis-restore-discard");
        player.Field[0][0] = target;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:discard");
        ResolveOnly(game, discard.InstanceId);
        ResolveOnly(game, target.InstanceId);
        var finalPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: finalPrompt.PromptId,
            Choice: "buff:strong")).Accepted);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: finalPrompt.PromptId,
            Choice: "buff:strong")).Accepted);
        var restoredTarget = Assert.Single(game.State.Players[0].Field[0],
            card => card?.InstanceId == target.InstanceId)!;
        Assert.True(restoredTarget.HasStrongAttack);
        Assert.False(restoredTarget.HasShock);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
        Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("本回合获得强攻", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void GrantedShockCreatesAnAttackTimingForALegionWithoutAPrintedAttackEffect()
    {
        var game = Create(91457);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var attacker = Card("S02-0502", "artemis-shock-attacker", 4);
        var discard = Card("S02-0001", "artemis-shock-discard");
        var left = Card("ST01-01", "artemis-shock-left");
        var primary = Card("ST01-01", "artemis-shock-primary");
        var right = Card("ST01-01", "artemis-shock-right");
        attacker.SummonRound = 0;
        attacker.Troops = 10000;
        primary.Troops = 9000;
        player.Field[0][0] = attacker;
        player.Hand.Add(discard);
        opponent.Field[0][0] = left;
        opponent.Field[0][1] = primary;
        opponent.Field[0][2] = right;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:discard");
        ResolveOnly(game, discard.InstanceId);
        ResolveOnly(game, attacker.InstanceId);
        ResolveOnly(game, "buff:shock");
        PassResponses(game);
        Assert.True(attacker.HasShock);

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", primary.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        PassResponses(game);

        Assert.Equal(left.BaseTroops - 2000, left.Troops);
        Assert.Equal(right.BaseTroops - 2000, right.Troops);
        Assert.True(attacker.HasShock);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains(
            "震击使进攻目标左右相邻军团", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:artemisBuff")]
    public void GrantedStrongAttackDealsTwoMasterDamageAndExpiresAtTurnEnd()
    {
        var game = Create(91458);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var attacker = Card("S02-0502", "artemis-strong-attacker", 4);
        var discard = Card("S02-0001", "artemis-strong-discard");
        attacker.SummonRound = 0;
        player.Field[0][0] = attacker;
        player.Hand.Add(discard);
        opponent.Hp = 8;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "artemisBuff")).Accepted);
        ResolveOnly(game, "pay:discard");
        ResolveOnly(game, discard.InstanceId);
        ResolveOnly(game, attacker.InstanceId);
        ResolveOnly(game, "buff:strong");
        PassResponses(game);

        Assert.True(attacker.HasStrongAttack);
        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal(2, game.State.PendingDefense?.MasterDamage);
        PassResponses(game);
        var noBlock = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
        Assert.True(noBlock.Accepted, noBlock.Error);
        Assert.Equal(6, opponent.Hp);
        Assert.True(attacker.HasStrongAttack);

        var endTurn = game.Handle(0, new L12Command("endTurn"));
        Assert.True(endTurn.Accepted, endTurn.Error);
        Assert.False(attacker.HasStrongAttack);
    }
}
