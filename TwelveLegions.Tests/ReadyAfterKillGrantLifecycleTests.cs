using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ReadyAfterKillGrantLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? masterId = null)
    {
        L12GameEngine game;
        if (masterId is null)
        {
            game = new L12GameEngine(Catalog, "ready-after-kill", "READY-AFTER-KILL", seed,
                ["甲", "乙"], [3, 3], skipPreparation: true,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        }
        else
        {
            var baseDeck = Catalog.DeckAt(0);
            var deck = new L12PresetDeckDefinition
            {
                Name = "击杀后转活跃生命周期",
                MasterId = masterId,
                CardIds = [.. baseDeck.CardIds],
                MoraleIds = [.. baseDeck.MoraleIds],
                SpecialIds = [],
            };
            game = new L12GameEngine(Catalog, "ready-after-kill", "READY-AFTER-KILL", seed,
                ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        }
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
        };
    }

    private static L12MoraleCard Morale(string id) => new()
    {
        CardId = "S02-05C1", InstanceId = id, Tapped = false,
    };

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

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    [Fact]
    public void ForgeModesSharePrintedAbilityTwoAndMorriganUsesItsPrintedSecondAbility()
    {
        var forge = Catalog.AtomicEffects.Find("S02-0520")!.Abilities
            .Single(candidate => candidate.Sequence == 2);
        Assert.Contains(forge.Presentations, scene => scene.Flow == "forge-promotion-discount"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
        Assert.Contains(forge.Presentations, scene => scene.Flow == "forge-ready-after-kill"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);

        var morrigan = Catalog.AtomicEffects.Find("S02-06M1")!.Abilities
            .Single(candidate => candidate.Sequence == 2);
        Assert.Contains(morrigan.Presentations, scene => scene.Flow == "morrigan-ready-after-kill"
            && scene.SegmentIndex == 1 && scene.SegmentCount == 1);
    }

    [Fact]
    [Trait("L12Evidence", "ability:forgePromotionDiscount")]
    public void ForgeRejectsAnUnpayableModeWithoutRestingOrCreatingAStack()
    {
        var game = Create(91441);
        var source = Card("S02-0520", "forge-unpayable-source");
        game.State.Players[0].Relic = source;

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "forgePromotionDiscount"));

        Assert.False(result.Accepted);
        Assert.False(source.Tapped);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:forgeReadyOnKill")]
    public void ForgeTargetSelectionCanBeCancelledBeforeEitherCostIsPaid()
    {
        var game = Create(91442);
        var player = game.State.Players[0];
        var source = Card("S02-0520", "forge-cancel-source");
        var target = Card("S02-0502", "forge-cancel-target");
        var morale = Morale("forge-cancel-morale");
        player.Relic = source;
        player.Field[0][0] = target;
        player.Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "forgeReadyOnKill")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.False(source.Tapped);
        Assert.False(morale.Tapped);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:forgeReadyOnKill")]
    public void ForgeTargetBecomingPromotedDuringResponseFailsAndKeepsBothCosts()
    {
        var game = Create(91443);
        var player = game.State.Players[0];
        var source = Card("S02-0520", "forge-failed-source");
        var target = Card("S02-0502", "forge-failed-target");
        var morale = Morale("forge-failed-morale");
        player.Relic = source;
        player.Field[0][0] = target;
        player.Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "forgeReadyOnKill")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        target.Traits.Add("晋升者");
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.True(morale.Tapped);
        Assert.Equal(-1, target.ReadyAfterNextKillUntilTurn);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:morriganReadyOnKill")]
    public void MorriganRingConvertedTargetLosingItsEffectiveFactionDuringResponseFailsAfterPayment()
    {
        var game = Create(91444, "S02-06M1");
        var player = game.State.Players[0];
        var target = Card("S02-0003", "morrigan-failed-target");
        var ring = Card("S02-0008", "morrigan-failed-ring");
        player.Field[0][0] = target;
        player.ExtraRelics.Add(ring);
        player.SpecialZones.Runes = 2;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "morriganReadyOnKill")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        player.ExtraRelics.Remove(ring);
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(-1, target.ReadyAfterNextKillUntilTurn);
        Assert.Equal("failed", Result(game, "master-0").EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:forgePromotionDiscount")]
    public void NegatedForgeDiscountKeepsBothCostsAndDoesNotCreateTheDiscount()
    {
        var game = Create(91445);
        var player = game.State.Players[0];
        var source = Card("S02-0520", "forge-negated-source");
        var morale = Morale("forge-negated-morale");
        player.Relic = source;
        player.Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "forgePromotionDiscount")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.True(morale.Tapped);
        Assert.Equal(0, player.NextS2PromotionGodPowerDiscount);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:forgeReadyOnKill")]
    public void V2RestoreAppliesTheFrozenGrantOnceAndRejectsTheOldTargetPrompt()
    {
        var game = Create(91446);
        var player = game.State.Players[0];
        var source = Card("S02-0520", "forge-restore-source");
        var target = Card("S02-0502", "forge-restore-target");
        player.Relic = source;
        player.Field[0][0] = target;
        player.Morale.Add(Morale("forge-restore-morale"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "forgeReadyOnKill")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        var restored = game.State.Players[0];
        var restoredTarget = Assert.Single(restored.Field[0], card => card?.InstanceId == target.InstanceId)!;
        Assert.Equal(game.State.TurnSerial, restoredTarget.ReadyAfterNextKillUntilTurn);
        Assert.True(restored.Relic!.Tapped);
        Assert.Single(restored.Morale, morale => morale.Tapped);
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
    }
}
