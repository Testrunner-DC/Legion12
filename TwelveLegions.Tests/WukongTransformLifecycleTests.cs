using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class WukongTransformLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, int moraleCount = 3)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "孙悟空变身生命周期",
            MasterId = "S02-01M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "wukong-transform", "WUKONG-TRANSFORM", seed,
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
        for (var index = 0; index < moraleCount; index++)
            game.State.Players[0].Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1", InstanceId = $"wukong-morale-{index}", Tapped = index % 2 != 0,
            });
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

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = OnlyPrompt(game);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choices.Length == 1 ? choices[0] : null,
            CardInstanceIds: choices.Length == 1 ? null : [.. choices]));
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

    private static void DeclareTransform(L12GameEngine game, int returnCount, string slot = "0:1")
    {
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, game.State.Players[0].Morale.Take(returnCount).Select(card => card.InstanceId).ToArray());
        Resolve(game, slot);
    }

    private static L12ActionEvent Result(L12GameEngine game)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "S02-01M1"));

    [Fact]
    public void PrintedFirstAbilityUsesOneStructuredTransformScene()
    {
        var ability = Catalog.AtomicEffects.Find("S02-01M1")!.Abilities
            .Single(candidate => candidate.Sequence == 1);
        var scene = Assert.Single(ability.Presentations, candidate =>
            candidate.Flow == "wukong-transform-entry");

        Assert.Equal(1, scene.SegmentIndex);
        Assert.Equal(1, scene.SegmentCount);
        Assert.Contains("兵力等于已返还士气数量×1000", scene.DefaultText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "ability:wukongTransform")]
    public void CannotBeginWithoutTwoMoraleOrAnEmptyFrontSlot()
    {
        var insufficient = Create(91501, moraleCount: 1);
        var noMorale = insufficient.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform"));
        Assert.False(noMorale.Accepted);
        Assert.Contains("至少需要返还2张士气", noMorale.Error);

        var full = Create(91502);
        for (var slot = 0; slot < 3; slot++) full.State.Players[0].Field[0][slot] = Card("S02-0004", $"block-{slot}");
        var noSlot = full.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform"));
        Assert.False(noSlot.Accepted);
        Assert.Contains("前排没有空位", noSlot.Error);
    }

    [Fact]
    [Trait("L12Evidence", "ability:wukongTransform")]
    public void CancellingEitherDeclarationStepPaysNothingAndUsesNoTurnCount()
    {
        var first = Create(91503);
        var original = first.State.Players[0].Morale.Select(card => card.InstanceId).ToArray();
        Assert.True(first.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Assert.Contains("skip", OnlyPrompt(first).ValidChoices);
        Resolve(first, "skip");
        Assert.Equal(original, first.State.Players[0].Morale.Select(card => card.InstanceId));
        Assert.DoesNotContain(first.State.Players[0].UsedAbilities, key => key.Contains("wukongTransform", StringComparison.Ordinal));

        var second = Create(91504);
        original = second.State.Players[0].Morale.Select(card => card.InstanceId).ToArray();
        Assert.True(second.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(second, original.Take(2).ToArray());
        Assert.Contains("skip", OnlyPrompt(second).ValidChoices);
        Resolve(second, "skip");
        Assert.Equal(original, second.State.Players[0].Morale.Select(card => card.InstanceId));
        Assert.Empty(second.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:wukongTransform")]
    public void OccupiedDeclaredSlotFailsDuringReverseSettlementAndKeepsReturnedMoraleCost()
    {
        var game = Create(91505);
        DeclareTransform(game, 2);
        game.State.Players[0].Field[0][1] = Card("S02-0004", "wukong-response-blocker");
        PassResponses(game);

        Assert.Single(game.State.Players[0].Morale);
        Assert.DoesNotContain(game.State.Players[0].Field.SelectMany(row => row), card => card?.IsMasterLegion == true);
        Assert.Equal("failed", Result(game).EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("逆结算后", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:wukongTransform")]
    public void NegationKeepsReturnedMoraleCostAndDoesNotEnterAsLegion()
    {
        var game = Create(91506);
        DeclareTransform(game, 3);
        Assert.Empty(game.State.Players[0].Morale);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Empty(game.State.Players[0].Morale);
        Assert.DoesNotContain(game.State.Players[0].Field.SelectMany(row => row), card => card?.IsMasterLegion == true);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:wukongTransform")]
    public void V2RestoreEntersOnceWithFrozenReturnedMoraleCountAndRejectsOldPrompt()
    {
        var game = Create(91507, moraleCount: 4);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, game.State.Players[0].Morale.Take(4).Select(card => card.InstanceId).ToArray());
        var slotPrompt = OnlyPrompt(game);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:2")).Accepted);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:2")).Accepted);
        var wukong = Assert.Single(game.State.Players[0].Field[0], card => card?.IsMasterLegion == true)!;
        Assert.Equal(4000, wukong.CurrentTroops);
        Assert.True(wukong.HasCharge);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
        Assert.Single(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == wukong.InstanceId));
    }
}
