using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DivinityEffectLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void DivinityRuntimeAbilitiesOwnOnlyTheirMatchingPresentationScenes()
    {
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-05D1"));
        var flip = Assert.Single(card.Abilities, ability => ability.Sequence == 1);
        var power = Assert.Single(card.Abilities, ability => ability.Sequence == 2);
        var freePromotion = Assert.Single(card.Abilities, ability => ability.Sequence == 3);

        Assert.Single(flip.Presentations, scene => scene.Flow == L12SingleSegmentEffectPresentations.Flow);
        Assert.Contains(power.Presentations, scene => scene.Flow == "divinity-power");
        Assert.DoesNotContain(freePromotion.Presentations, scene => scene.Flow == "divinity-power");
        Assert.Single(freePromotion.Presentations,
            scene => scene.Flow == L12SingleSegmentEffectPresentations.Flow);
    }

    [Fact]
    public void DivinityFlipRestoresItsSceneAndPublishesResolved()
    {
        var game = Create(91321, stateFormatVersion: 2);
        var morale = Morale("divinity-flip-success");
        game.State.Players[0].Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityFlipMorale")).Accepted);
        var sceneId = Assert.Single(game.State.Events, entry => entry.EffectResultStatus == "declared"
            && entry.Cards.Any(card => card.InstanceId == "master-0")).EffectSceneId;

        game = Restore(game);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-flip-morale", prompt.Data["action"]);
        var promptId = prompt.PromptId;
        Resolve(game, morale.InstanceId);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: promptId,
            Choice: morale.InstanceId)).Accepted);

        var result = Result(game, "master-0", sceneId);
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.True(game.State.Players[0].Morale.Single().IsGodPower);
    }

    [Fact]
    public void DivinityFlipFailsWhenItsResolutionTimeCandidateDisappears()
    {
        var game = Create(91322);
        var morale = Morale("divinity-flip-invalid");
        game.State.Players[0].Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityFlipMorale")).Accepted);
        morale.IsGodPower = true;
        PassResponses(game);

        Assert.Equal("failed", Result(game, "master-0").EffectResultStatus);
        Assert.Contains(game.State.Players[0].UsedAbilities,
            key => key.Contains("divinityFlipMorale", StringComparison.Ordinal));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void DivinityDamageWithNoLegionsPublishesSkippedAndKeepsGodPowerCost()
    {
        var game = Create(91323);
        var powers = AddGodPowers(game);

        DeclareDamage(game, Enumerable.Repeat("mode:none", 6).ToArray());
        PassResponses(game);

        Assert.Equal("skipped", Result(game, "master-0").EffectResultStatus);
        Assert.All(powers, power =>
        {
            Assert.True(power.Tapped);
            Assert.False(power.IsGodPower);
        });
    }

    [Fact]
    public void DivinityDamageFailsWhenEveryAllocationTargetLeavesBeforeSettlement()
    {
        var game = Create(91324);
        var powers = AddGodPowers(game);
        var target = Card("S02-0004", "divinity-damage-invalid", owner: 1);
        target.Troops = 10000;
        game.State.Players[1].Field[0][0] = target;

        DeclareDamage(game, Enumerable.Repeat(target.InstanceId, 6).ToArray());
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(target);
        PassResponses(game);

        Assert.Equal("failed", Result(game, "master-0").EffectResultStatus);
        Assert.All(powers, power => Assert.True(power.Tapped));
        Assert.Contains(target, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void DivinityDamageResolvesRemainingAllocationsAndLogsPartialInvalidity()
    {
        var game = Create(91325, stateFormatVersion: 2);
        AddGodPowers(game);
        var invalid = Card("S02-0004", "divinity-damage-partial-invalid", owner: 1);
        var valid = Card("S01-0001", "divinity-damage-partial-valid", owner: 1);
        invalid.Troops = 10000;
        valid.Troops = 10000;
        game.State.Players[1].Field[0][0] = invalid;
        game.State.Players[1].Field[0][1] = valid;
        var validTroopsBefore = valid.CurrentTroops;

        DeclareDamage(game,
            invalid.InstanceId, invalid.InstanceId, invalid.InstanceId,
            valid.InstanceId, valid.InstanceId, valid.InstanceId);
        Assert.Equal(3, Assert.Single(game.State.EffectStack).Data["targets"]
            .Split('|').Count(id => id == valid.InstanceId));
        game = Restore(game);
        var restoredInvalid = game.State.Players[1].Field[0][0]!;
        var restoredValid = game.State.Players[1].Field[0][1]!;
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(restoredInvalid);
        PassResponses(game);

        Assert.Equal(validTroopsBefore - 3000, restoredValid.CurrentTroops);
        Assert.Equal("resolved", Result(game, "master-0").EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("3份已声明伤害", StringComparison.Ordinal));
    }

    [Fact]
    public void NegatedFreePromotionKeepsRestCostAndPublishesNegated()
    {
        var game = Create(91326);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityFreePromotion")).Accepted);
        var item = Assert.Single(game.State.EffectStack);
        item.Negated = true;
        PassResponses(game);

        Assert.True(game.State.Players[0].MasterTapped);
        Assert.Equal(0, game.State.Players[0].NextS2PromotionGodPowerDiscount);
        Assert.Equal("negated", Result(game, "master-0").EffectResultStatus);
        Assert.False(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityFreePromotion")).Accepted);
    }

    [Fact]
    public void DivinityRecoveryPublishesDeclinedForPredeclaredNoEntryBranch()
    {
        var game = Create(91327);
        AddGodPowers(game);
        var recovered = Card("S02-0522", "divinity-recover-only");
        game.State.Players[0].Graveyard.Add(recovered);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityPower")).Accepted);
        Resolve(game, "mode:recover");
        Resolve(game, recovered.InstanceId);
        PassResponses(game);

        Assert.Contains(recovered, game.State.Players[0].Hand);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "master-0"))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.Collection(results,
            first =>
            {
                Assert.Equal(1, first.EffectSegmentIndex);
                Assert.Equal("resolved", first.EffectResultStatus);
            },
            second =>
            {
                Assert.Equal(2, second.EffectSegmentIndex);
                Assert.Equal("declined", second.EffectResultStatus);
                Assert.Equal("不登场", second.EffectBranchLabel);
            });
    }

    private static L12GameEngine Create(int seed, int stateFormatVersion = 0)
    {
        var catalog = Catalog;
        var baseDeck = catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "诸神巅生命周期回归",
            MasterId = "S02-05D1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(catalog, "divinity-lifecycle", "DIVINITY", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12MoraleCard[] AddGodPowers(L12GameEngine game)
    {
        var powers = new[]
        {
            Morale("divinity-power-a", isGodPower: true),
            Morale("divinity-power-b", isGodPower: true),
        };
        game.State.Players[0].Morale.AddRange(powers);
        return powers;
    }

    private static void DeclareDamage(L12GameEngine game, params string[] allocations)
    {
        Assert.Equal(6, allocations.Length);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityPower")).Accepted);
        Resolve(game, "mode:damage");
        var allocationIndex = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Continuation == "pending-activation")
        {
            Assert.True(allocationIndex < allocations.Length,
                "伤害声明请求超过卡面规定的6份1000兵力伤害");
            Resolve(game, allocations[allocationIndex++]);
        }
        Assert.Single(game.State.EffectStack);
        Assert.Equal(6, Assert.Single(game.State.EffectStack).Data["targets"]
            .Split('|', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted,
            $"{result.Error}; prompt={prompt.Text}; valid=[{string.Join(',', prompt.ValidChoices)}]; choice={choice}");
    }

    private static void PassResponses(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt
               && count++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceInstanceId,
        string? sceneId = null)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceInstanceId)
            && (sceneId is null || entry.EffectSceneId == sceneId));

    private static L12MoraleCard Morale(string instanceId, bool isGodPower = false) => new()
    {
        CardId = isGodPower ? "S02-05C1" : "S02-05C1A",
        InstanceId = instanceId,
        IsGodPower = isGodPower,
    };

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }
}
