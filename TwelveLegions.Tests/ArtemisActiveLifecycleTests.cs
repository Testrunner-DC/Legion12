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
    public void TargetLeavingTheCostRangeDuringResponseFailsAndKeepsTheDiscardCost()
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
        Assert.False(target.HasStrongAttack);
        Assert.False(target.HasShock);
        Assert.Equal("failed", Result(game).EffectResultStatus);
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
        Assert.False(power.IsGodPower);
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
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        Assert.False(target.HasShock);
        Assert.Equal("negated", Result(game).EffectResultStatus);
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
}
